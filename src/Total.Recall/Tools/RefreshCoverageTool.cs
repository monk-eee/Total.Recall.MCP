using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using Total.Recall.Infrastructure;
using Total.Recall.Models;
using Total.Recall.Scanners;

namespace Total.Recall.Tools;

/// <summary>
/// Incremental coverage refresh — re-parses a Cobertura XML and updates coverage-gaps.jsonl
/// without doing a full assembly + test rescan. Use after running tests with coverage
/// to update ROI rankings mid-session.
/// </summary>
[McpServerToolType]
public static class RefreshCoverageTool
{
    [McpServerTool, Description(
        "Re-parse a Cobertura XML coverage report and update coverage gaps without a full rescan. " +
        "Use this after running tests with coverage to refresh ROI rankings mid-session. " +
        "Much faster than a full 'scan' — only regenerates coverage-gaps.jsonl. " +
        "If coveragePath is omitted, uses the path from config.json (set during last full scan). " +
        "Set reEnrich=true to also update test counts and testability scores after refreshing. " +
        "Returns a summary of before vs after coverage stats.")]
    public static string RefreshCoverage(
        [Description("Path to new Cobertura XML file. If omitted, uses the path from last scan.")] string? coveragePath = null,
        [Description("Re-enrich coverage gaps (update test counts + testability) after refresh (default: true)")] bool reEnrich = true,
        [Description("Optional: namespace/session to query (default: server default)")] string? ns = null)
    {
        Metrics.Increment(Metrics.ToolRefreshCoverage);
        Log.Debug($"[RefreshCoverage] coveragePath='{coveragePath ?? "(from config)"}' reEnrich={reEnrich} ns='{ns ?? "(default)"}'");
        try
        {
            return RefreshCoverageCore(coveragePath, reEnrich, ns);
        }
        catch (Exception ex)
        {
            Log.Error($"[RefreshCoverage] failed: {ex.GetType().Name}: {ex.Message}");
            return $"ERROR in RefreshCoverage: {ex.GetType().Name}: {ex.Message}";
        }
    }

    private static string RefreshCoverageCore(string? coveragePath, bool reEnrich, string? ns)
    {
        var stores = StoreRegistry.ForNamespace(ns);
        var config = stores.Config;
        var dataDir = stores.DataDir;

        // Resolve the coverage file path
        var resolvedPath = coveragePath;
        if (string.IsNullOrEmpty(resolvedPath))
        {
            resolvedPath = config.CoveragePath;
            if (string.IsNullOrEmpty(resolvedPath))
                return "No coverage path provided and none found in config.json. Provide a coveragePath argument or run a full scan first.";
        }

        if (!File.Exists(resolvedPath))
        {
            // Try to find the most recent Cobertura XML in TestResults
            var testResultsDir = Path.Combine(Path.GetDirectoryName(resolvedPath) ?? ".", "TestResults");
            if (Directory.Exists(testResultsDir))
            {
                var newestCoverage = Directory.GetFiles(testResultsDir, "coverage.cobertura.xml", SearchOption.AllDirectories)
                    .Select(f => new FileInfo(f))
                    .OrderByDescending(f => f.LastWriteTimeUtc)
                    .FirstOrDefault();

                if (newestCoverage is not null)
                {
                    resolvedPath = newestCoverage.FullName;
                    Log.Info($"[RefreshCoverage] auto-discovered coverage at: {resolvedPath}");
                }
                else
                {
                    return $"Coverage file not found: {resolvedPath}. Also searched TestResults/ subdirectories.";
                }
            }
            else
            {
                return $"Coverage file not found: {resolvedPath}";
            }
        }

        // Capture before-state
        var beforeGaps = stores.CoverageGaps.HasData()
            ? stores.CoverageGaps.LoadAll()
            : [];
        var beforeTotalLines = beforeGaps.Sum(g => g.TotalLines);
        var beforeCoveredLines = beforeGaps.Sum(g => g.CoveredLines);
        var beforeClassCount = beforeGaps.Count;

        // Re-parse coverage
        var classCount = CoberturaParser.Parse(resolvedPath, dataDir);

        // Optional: re-enrich coverage gaps with test counts and testability
        int enrichedCount = 0;
        if (reEnrich)
        {
            try
            {
                enrichedCount = EnrichAfterRefresh(stores);
                Log.Debug($"[RefreshCoverage] re-enriched {enrichedCount} classes");
            }
            catch (Exception ex)
            {
                Log.Warn($"[RefreshCoverage] enrichment failed (non-fatal): {ex.Message}");
            }
        }

        // Force cache invalidation by reloading
        var afterGaps = stores.CoverageGaps.LoadAll();
        var afterTotalLines = afterGaps.Sum(g => g.TotalLines);
        var afterCoveredLines = afterGaps.Sum(g => g.CoveredLines);

        var beforeRate = beforeTotalLines > 0 ? Math.Round(100.0 * beforeCoveredLines / beforeTotalLines, 2) : 0;
        var afterRate = afterTotalLines > 0 ? Math.Round(100.0 * afterCoveredLines / afterTotalLines, 2) : 0;
        var delta = Math.Round(afterRate - beforeRate, 2);

        // Find newly covered classes (were uncovered, now have coverage)
        var newlyCovered = new List<string>();
        var beforeByClass = beforeGaps.ToDictionary(g => g.Class, g => g, StringComparer.OrdinalIgnoreCase);
        foreach (var gap in afterGaps)
        {
            if (beforeByClass.TryGetValue(gap.Class, out var before))
            {
                if (before.CoveragePercent < 1 && gap.CoveragePercent >= 1)
                    newlyCovered.Add(gap.Class);
            }
        }

        // Top 5 classes with biggest coverage improvement
        var improvements = afterGaps
            .Where(g => beforeByClass.ContainsKey(g.Class))
            .Select(g => new
            {
                g.Class,
                before = beforeByClass[g.Class].CoveragePercent,
                after = g.CoveragePercent,
                delta = g.CoveragePercent - beforeByClass[g.Class].CoveragePercent
            })
            .Where(x => x.delta > 0)
            .OrderByDescending(x => x.delta)
            .Take(5)
            .ToList();

        var result = new
        {
            status = "refreshed",
            coverageFile = resolvedPath,
            fileModified = new FileInfo(resolvedPath).LastWriteTimeUtc.ToString("o"),
            before = new { lineRate = beforeRate, coveredLines = beforeCoveredLines, totalLines = beforeTotalLines, classCount = beforeClassCount },
            after = new { lineRate = afterRate, coveredLines = afterCoveredLines, totalLines = afterTotalLines, classCount },
            delta = new { lineRateChange = delta, newLinesHit = afterCoveredLines - beforeCoveredLines },
            enriched = reEnrich ? enrichedCount : (int?)null,
            newlyCovered,
            topImprovements = improvements
        };

        Log.Info($"[RefreshCoverage] done: {beforeRate}% → {afterRate}% ({delta:+0.##}%)");
        return JsonSerializer.Serialize(result, SharedJsonOptions.CamelCaseIndented);
    }

    /// <summary>
    /// Lightweight enrichment: cross-reference fresh coverage gaps with type registry
    /// and test inventory to update test counts and testability scores.
    /// Runs in-process (no CLI) using the NamespaceStores already loaded.
    /// </summary>
    private static int EnrichAfterRefresh(NamespaceStores stores)
    {
        if (!stores.CoverageGaps.HasData())
            return 0;

        var gaps = stores.CoverageGaps.LoadAll();

        // Build type map for testability classification
        var typeMap = stores.TypeRegistry.HasData()
            ? stores.TypeRegistry.LoadAll()
                .GroupBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, TypeRecord>(StringComparer.OrdinalIgnoreCase);

        // Build test inventory map
        var testMap = stores.TestInventory.HasData()
            ? stores.TestInventory.LoadAll()
                .GroupBy(t => t.Class, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, TestInventoryEntry>(StringComparer.OrdinalIgnoreCase);

        var enrichedCount = 0;
        foreach (var gap in gaps)
        {
            if (testMap.TryGetValue(gap.Class, out var testEntry))
            {
                gap.ExistingTestCount = testEntry.TestCount;
                enrichedCount++;
            }

            if (typeMap.TryGetValue(gap.Class, out var typeRecord))
            {
                gap.Testability = ClassifyTestability(typeRecord);
            }
        }

        stores.CoverageGaps.WriteAll(gaps);
        return enrichedCount;
    }

    private static string ClassifyTestability(TypeRecord type)
    {
        if (type.IsAbstract || type.IsInterface)
            return "low";
        if (type.IsStatic)
            return "medium";
        var maxCtorParams = type.Constructors.Count > 0
            ? type.Constructors.Max(c => c.Params.Count)
            : 0;
        if (maxCtorParams <= 3) return "high";
        if (maxCtorParams <= 6) return "medium";
        return "low";
    }
}
