using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using Total.Recall.Infrastructure;
using Total.Recall.Models;

namespace Total.Recall.Tools;

/// <summary>
/// Serves actual source code from the target repo, using file paths from coverage data
/// and a configured source root. Eliminates the "I have to read the file anyway" problem.
/// </summary>
[McpServerToolType]
public static class SourceSnippetTool
{
    // Cache resolved source roots per data directory to avoid repeated filesystem + env lookups and log spam
    private static readonly Dictionary<string, string?> _sourceRootCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Reset cached source roots (for testing).</summary>
    internal static void ResetSourceRootCache() => _sourceRootCache.Clear();

    [McpServerTool, Description(
        "Get actual source code for a class or specific method(s) from the target repo. " +
        "Uses coverage data file paths + configured source root to locate files. " +
        "Returns the source code so you don't need to call read_file separately. " +
        "Supports comma-separated method names to fetch multiple methods at once. " +
        "Requires TOTAL_RECALL_SOURCE_ROOT env var or scanner --source-root to be set.")]
    public static string GetSourceSnippet(
        [Description("Class name to get source for")] string className,
        [Description("Optional: method name(s) to extract. Comma-separated for multiple (e.g. 'Validate,Process,Execute')")] string? methodName = null,
        [Description("Max lines to return (default: 200)")] int maxLines = 200,
        [Description("Optional: namespace/session to query (default: server default)")] string? ns = null)
    {
        return Telemetry.Track("get_source_snippet", ns, new { className, methodName, maxLines, ns }, () =>
        {
        Metrics.Increment(Metrics.ToolGetSourceSnippet);
        Log.Debug($"[GetSourceSnippet] className='{className}' method='{methodName ?? "(all)"}' maxLines={maxLines} ns='{ns ?? "(default)"}'");
        try
        {
            return GetSourceSnippetCore(className, methodName, maxLines, ns);
        }
        catch (Exception ex)
        {
            Log.Error($"[GetSourceSnippet] failed for '{className}': {ex.GetType().Name}: {ex.Message}");
            return $"ERROR in GetSourceSnippet: {ex.GetType().Name}: {ex.Message}";
        }
        });
    }

    private static string GetSourceSnippetCore(string className, string? methodName, int maxLines, string? ns)
    {
        var stores = StoreRegistry.ForNamespace(ns);

        // Resolve source root
        var sourceRoot = ResolveSourceRoot(stores.DataDir);
        if (string.IsNullOrEmpty(sourceRoot))
        {
            return "Source root not configured. Set TOTAL_RECALL_SOURCE_ROOT env var, " +
                   "or run scanner with --source-root. " +
                   "Falling back: use read_file with the file path from get_coverage_gaps instead.";
        }

        if (!Directory.Exists(sourceRoot))
            return $"Source root directory not found: {sourceRoot}. Check TOTAL_RECALL_SOURCE_ROOT or config.json.";

        // Find the file path from coverage data — prefer by most uncovered lines when ambiguous
        if (!stores.CoverageGaps.HasData())
            return "No coverage data found. Run 'total-recall scan --coverage <cobertura.xml>' first.";

        var allGaps = stores.CoverageGaps.LoadAll();

        // Exact match (may return multiple classes with the same name in different namespaces/files)
        var exactMatches = allGaps
            .Where(g => g.ShortName.Equals(className, StringComparison.OrdinalIgnoreCase))
            .ToList();

        CoverageGap? gap;
        string? ambiguityNote = null;

        if (exactMatches.Count > 1)
        {
            // Multiple classes with the same name — pick the one with most uncovered lines (substantive implementation)
            gap = exactMatches.OrderByDescending(g => g.UncoveredLineCount).ThenByDescending(g => g.LinesTotal).First();
            ambiguityNote = $"Note: {exactMatches.Count} classes named '{className}' found. " +
                           $"Returning the one with most uncovered lines ({gap.UncoveredLineCount} lines in {gap.FilePath}). " +
                           $"Other matches: {string.Join(", ", exactMatches.Where(g => !ReferenceEquals(g, gap)).Select(g => $"{g.ClassName} ({g.FilePath}, {g.UncoveredLineCount} uncovered)"))}";
        }
        else if (exactMatches.Count == 1)
        {
            gap = exactMatches[0];
        }
        else
        {
            // Try partial match — also prefer by uncovered lines
            var partialMatches = allGaps
                .Where(g => g.ShortName.Contains(className, StringComparison.OrdinalIgnoreCase)
                         || g.ClassName.Contains(className, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(g => g.UncoveredLineCount)
                .ThenByDescending(g => g.LinesTotal)
                .ToList();

            gap = partialMatches.FirstOrDefault();

            if (partialMatches.Count > 1)
            {
                ambiguityNote = $"Note: {partialMatches.Count} partial matches for '{className}'. " +
                               $"Returning '{gap!.ClassName}' ({gap.FilePath}, {gap.UncoveredLineCount} uncovered lines).";
            }
        }

        if (gap is null)
            return $"No coverage data found for class '{className}'. Cannot resolve file path.";

        if (string.IsNullOrEmpty(gap.FilePath))
            return $"Coverage data for '{className}' has no file path. Cannot locate source.";

        // Resolve the full file path
        var relativePath = gap.FilePath.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
        var fullPath = Path.GetFullPath(Path.Combine(sourceRoot, relativePath));

        // Security: ensure the resolved path is under the source root
        var normalizedRoot = Path.GetFullPath(sourceRoot);
        if (!fullPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
            return $"Security: resolved path '{fullPath}' is outside source root '{normalizedRoot}'. Possible path traversal.";

        if (!File.Exists(fullPath))
            return $"Source file not found: {fullPath}. The source may have moved or the coverage data is stale.";

        // Read the file
        var allLines = File.ReadAllLines(fullPath);

        if (methodName is not null)
        {
            // Support comma-separated method names for multi-method extraction
            var methodNames = methodName.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            if (methodNames.Length > 1)
            {
                // Multi-method mode: extract each method and combine results
                var perMethodMax = Math.Max(50, maxLines / methodNames.Length);
                var results = new List<object>();
                var notFound = new List<string>();

                foreach (var mName in methodNames)
                {
                    var methodJson = ExtractMethod(allLines, fullPath, gap, mName, perMethodMax, className, null);
                    if (methodJson.StartsWith("Method '"))
                    {
                        // Method not found — collect for combined error
                        notFound.Add(mName);
                    }
                    else
                    {
                        try
                        {
                            var parsed = JsonSerializer.Deserialize<JsonElement>(methodJson);
                            results.Add(parsed);
                        }
                        catch
                        {
                            notFound.Add(mName);
                        }
                    }
                }

                var multiResult = new
                {
                    className,
                    filePath = fullPath,
                    relativePath = gap.FilePath,
                    requestedMethods = methodNames.Length,
                    returnedMethods = results.Count,
                    ambiguityNote,
                    notFound = notFound.Count > 0 ? notFound : null,
                    methods = results
                };

                return JsonSerializer.Serialize(multiResult, SharedJsonOptions.CamelCaseIndented);
            }

            // Single method — existing behavior
            return ExtractMethod(allLines, fullPath, gap, methodNames[0], maxLines, className, ambiguityNote);
        }

        // Return the whole class (up to maxLines)
        var linesToReturn = Math.Min(allLines.Length, maxLines);
        var truncated = allLines.Length > maxLines;

        var result = new
        {
            className,
            filePath = fullPath,
            relativePath = gap.FilePath,
            totalFileLines = allLines.Length,
            returnedLines = linesToReturn,
            truncated,
            startLine = 1,
            endLine = linesToReturn,
            ambiguityNote,
            source = AnnotateWithLineNumbers(allLines.Take(linesToReturn), 1)
        };

        return JsonSerializer.Serialize(result, SharedJsonOptions.CamelCaseIndented);
    }

    private static string ExtractMethod(
        string[] allLines, string fullPath, Models.CoverageGap gap,
        string methodName, int maxLines, string className, string? ambiguityNote = null)
    {
        // Try to find the method in coverage data for line range
        var method = gap.UncoveredMethods.FirstOrDefault(m =>
            m.Name.Equals(methodName, StringComparison.OrdinalIgnoreCase));

        if (method is null)
        {
            // Try partial match
            method = gap.UncoveredMethods.FirstOrDefault(m =>
                m.Name.Contains(methodName, StringComparison.OrdinalIgnoreCase));
        }

        if (method is not null && method.UncoveredLines.Length > 0)
        {
            // Derive method span from first..last uncovered line. Cobertura only tells us where the
            // uncovered statements are, not the full method extent — the context window below makes
            // the snippet readable when the actual signature/closing brace fall outside the span.
            var firstLine = method.FirstUncoveredLine;
            var lastLine = method.LastUncoveredLine;
            var contextLines = 5;
            var startLine = Math.Max(1, firstLine - contextLines);
            var endLine = Math.Min(allLines.Length, lastLine + contextLines);
            var lineCount = endLine - startLine + 1;

            if (lineCount > maxLines)
                endLine = startLine + maxLines - 1;

            var lines = allLines.Skip(startLine - 1).Take(endLine - startLine + 1);

            var result = new
            {
                className,
                methodName = method.Name,
                filePath = fullPath,
                relativePath = gap.FilePath,
                startLine,
                endLine,
                uncoveredLines = method.UncoveredLines,
                ambiguityNote,
                source = AnnotateWithLineNumbers(lines, startLine)
            };

            return JsonSerializer.Serialize(result, SharedJsonOptions.CamelCaseIndented);
        }

        // Fallback: search for the method name in the source file
        var methodLineIdx = -1;
        for (int i = 0; i < allLines.Length; i++)
        {
            // Look for method signature: "methodName(" pattern
            if (allLines[i].Contains(methodName, StringComparison.OrdinalIgnoreCase)
                && (allLines[i].Contains('(') || allLines[i].TrimEnd().EndsWith(methodName, StringComparison.OrdinalIgnoreCase)))
            {
                methodLineIdx = i;
                break;
            }
        }

        if (methodLineIdx < 0)
        {
            return $"Method '{methodName}' not found in source file for '{className}'. " +
                   $"Available uncovered methods: [{string.Join(", ", gap.UncoveredMethods.Select(m => m.Name))}]";
        }

        // Extract method with context
        var mStart = Math.Max(0, methodLineIdx - 2);
        var mEnd = Math.Min(allLines.Length - 1, methodLineIdx + maxLines - 1);

        // Try to find the method's closing brace
        var braceDepth = 0;
        var foundOpen = false;
        for (int i = methodLineIdx; i < allLines.Length && i <= mEnd + 200; i++)
        {
            foreach (var ch in allLines[i])
            {
                if (ch == '{') { braceDepth++; foundOpen = true; }
                if (ch == '}') braceDepth--;
            }
            if (foundOpen && braceDepth == 0)
            {
                mEnd = Math.Min(i + 1, allLines.Length - 1);
                break;
            }
        }

        mEnd = Math.Min(mEnd, mStart + maxLines - 1);
        var methodLines = allLines.Skip(mStart).Take(mEnd - mStart + 1);

        var fallbackResult = new
        {
            className,
            methodName,
            filePath = fullPath,
            relativePath = gap.FilePath,
            startLine = mStart + 1,
            endLine = mEnd + 1,
            note = "Line range from source search (not coverage data)",
            ambiguityNote,
            source = AnnotateWithLineNumbers(methodLines, mStart + 1)
        };

        return JsonSerializer.Serialize(fallbackResult, SharedJsonOptions.CamelCaseIndented);
    }

    /// <summary>
    /// Resolve the source root from: env var → config.json → null.
    /// Results are cached per data directory — only logs on first resolution.
    /// </summary>
    internal static string? ResolveSourceRoot(string dataDir)
    {
        if (_sourceRootCache.TryGetValue(dataDir, out var cached))
            return cached;

        string? result = null;

        // 1. Environment variable takes precedence
        var envRoot = Environment.GetEnvironmentVariable("TOTAL_RECALL_SOURCE_ROOT");
        if (!string.IsNullOrWhiteSpace(envRoot))
        {
            result = Path.GetFullPath(envRoot.Trim());
            Log.Info($"source root (from env): {result}");
            _sourceRootCache[dataDir] = result;
            return result;
        }

        // 2. Read from config.json in the data directory
        var configPath = Path.Combine(dataDir, "config.json");
        if (File.Exists(configPath))
        {
            try
            {
                var json = File.ReadAllText(configPath);
                var config = JsonSerializer.Deserialize<Models.NamespaceConfig>(json, SharedJsonOptions.CamelCase);
                if (!string.IsNullOrWhiteSpace(config?.SourceRoot))
                {
                    result = Path.GetFullPath(config.SourceRoot);
                    Log.Info($"source root (from config.json): {result}");
                    _sourceRootCache[dataDir] = result;
                    return result;
                }
            }
            catch (Exception ex)
            {
                Log.Warn($"Failed to read config.json: {ex.Message}");
            }
        }

        _sourceRootCache[dataDir] = null;
        return null;
    }

    /// <summary>
    /// Annotate source lines with line numbers for easy reference.
    /// Format: "  42 | source code here"
    /// </summary>
    internal static string AnnotateWithLineNumbers(IEnumerable<string> lines, int startLineNumber)
    {
        var lineList = lines.ToList();
        if (lineList.Count == 0)
            return string.Empty;

        var maxLineNum = startLineNumber + lineList.Count - 1;
        var padWidth = maxLineNum.ToString().Length;

        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < lineList.Count; i++)
        {
            var lineNum = (startLineNumber + i).ToString().PadLeft(padWidth);
            sb.Append(lineNum);
            sb.Append(" | ");
            sb.AppendLine(lineList[i]);
        }

        // Remove trailing newline for clean output
        if (sb.Length > 0 && sb[^1] == '\n')
        {
            sb.Length--;
            if (sb.Length > 0 && sb[^1] == '\r')
                sb.Length--;
        }

        return sb.ToString();
    }
}
