using System.ComponentModel;
using System.Text;
using System.Text.Json;
using ModelContextProtocol.Server;
using Total.Recall.Infrastructure;
using Total.Recall.Models;

namespace Total.Recall.Tools;

/// <summary>
/// Clusters related gotchas across types and generates AGENTS.md-ready documentation.
/// Closes the loop: individual gotchas accumulate → patterns emerge → documentation is auto-generated.
/// </summary>
[McpServerToolType]
public static class GotchaInsightsTool
{
    /// <summary>
    /// Well-known gotcha clusters: patterns that recur across types.
    /// Each cluster has keywords to match against gotcha descriptions and a canonical title.
    /// </summary>
    private static readonly GotchaCluster[] s_clusters =
    [
        new("Moq Expression Tree Limitations",
            "CS0854|expression tree|optional param|default param",
            "Moq Setup/Verify calls fail with CS0854 when the target method has optional/default parameters. " +
            "Fix: explicitly pass all parameters in the Setup lambda (e.g., `.Export(false)` instead of `.Export()`)."),

        new("Enum Value Gotchas",
            "enum|member name|default\\(|value 0|no .* member",
            "Enums with non-obvious member names, missing expected members, or default(T) pointing to " +
            "an unexpected value. Always check actual member names via IntelliSense, not assumptions."),

        new("Constructor / Initialization Traps",
            "constructor|ctor|parameterless|init-only|NRE|null.*ctor|leaves.*null",
            "Constructors that leave required properties null, have hidden dependencies, or require " +
            "specific initialization order. Watch for init-only properties requiring reflection in tests."),

        new("Mock Setup Complexity",
            "mock|setup|Moq|verify|ILogger|extension method|self-referencing|proxy",
            "Interface mocking complications: extension methods can't be verified with Moq, " +
            "self-referencing types cause proxy loops, generic ILogger requires IsEnabled setup."),

        new("Namespace / Type Resolution",
            "namespace|ambiguous|using|alias|Octokit|System\\.IO|collision",
            "Type name collisions requiring aliases, ambiguous references between packages, " +
            "or namespace paths that don't match folder structure."),

        new("Record / Equality Semantics",
            "record|equality|value equality|reference|partial record",
            "Records auto-generate value equality, breaking reference-inequality assertions. " +
            "Partial records may not include all fields in equality comparison."),

        new("Property Accessor Quirks",
            "init-only|read-only|setter|backing field|reflection|FormatterServices",
            "Properties that are init-only or read-only require reflection or " +
            "FormatterServices.GetUninitializedObject to set in tests."),

        new("Dead Code / Design Bugs",
            "dead.code|unreachable|bug|branch.*never|self-add|token.*instead",
            "Production code with unreachable branches, copy-paste bugs, or logic errors " +
            "discovered during test generation. Document but don't try to cover dead code."),

        new("ICU / Culture-Sensitive Comparisons",
            "ICU|ordinal|culture|BOM|ZWS|invisible|char.*ignor",
            "String comparisons affected by ICU normalization, invisible characters (BOM, ZWS), " +
            "or culture-specific sorting. Use StringComparison.Ordinal for predictable behavior."),

        new("Static State / Initialization",
            "static|RegexQuery|static ctor|static init|leak",
            "Static fields that cause test pollution, static constructors that fail in test context, " +
            "or static state that leaks between test runs. Use [Collection] to prevent parallel execution.")
    ];

    [McpServerTool, Description(
        "Analyze gotchas across all types to find recurring patterns and clusters. " +
        "Returns: (1) clustered gotchas grouped by pattern, (2) cross-type insights, " +
        "(3) AGENTS.md-ready 'Footguns' section for documentation. " +
        "Use after accumulating 10+ gotchas to identify systemic issues worth documenting.")]
    public static string GetGotchaInsights(
        [Description("Minimum gotchas in a cluster to report (default: 2)")] int minClusterSize = 2,
        [Description("Generate AGENTS.md Footguns section (default: true)")] bool generateFootguns = true,
        [Description("Optional: namespace/session to query (default: server default)")] string? ns = null)
    {
        Metrics.Increment(Metrics.ToolGetGotchaInsights);
        Log.Debug($"[GetGotchaInsights] minClusterSize={minClusterSize} generateFootguns={generateFootguns} ns='{ns ?? "(default)"}'");
        try
        {
            return GetGotchaInsightsCore(minClusterSize, generateFootguns, ns);
        }
        catch (Exception ex)
        {
            Log.Error($"[GetGotchaInsights] failed: {ex.GetType().Name}: {ex.Message}");
            return $"ERROR in GetGotchaInsights: {ex.GetType().Name}: {ex.Message}";
        }
    }

    private static string GetGotchaInsightsCore(int minClusterSize, bool generateFootguns, string? ns)
    {
        var stores = StoreRegistry.ForNamespace(ns);

        if (!stores.Gotchas.HasData())
            return "No gotchas recorded yet. Use AddGotcha to record pitfalls during test generation.";

        var allGotchas = stores.Gotchas.LoadAll();

        if (allGotchas.Count < 3)
            return $"Only {allGotchas.Count} gotcha(s) recorded. Accumulate more before clustering (recommend 10+).";

        // ── Cluster gotchas by pattern matching ──
        var clusterResults = new List<object>();
        var clusteredGotchaIds = new HashSet<int>();

        foreach (var cluster in s_clusters)
        {
            var keywords = cluster.Keywords.Split('|');
            var matches = new List<(int Index, Gotcha Gotcha)>();

            for (int i = 0; i < allGotchas.Count; i++)
            {
                var g = allGotchas[i];
                var text = $"{g.Description} {g.Category}";
                if (keywords.Any(kw =>
                    text.Contains(kw, StringComparison.OrdinalIgnoreCase)))
                {
                    matches.Add((i, g));
                }
            }

            if (matches.Count >= minClusterSize)
            {
                foreach (var (idx, _) in matches)
                    clusteredGotchaIds.Add(idx);

                var affectedTypes = matches.Select(m => m.Gotcha.Type).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

                clusterResults.Add(new
                {
                    cluster = cluster.Title,
                    count = matches.Count,
                    affectedTypes,
                    canonicalFix = cluster.CanonicalFix,
                    instances = matches.Select(m => new
                    {
                        type = m.Gotcha.Type,
                        category = m.Gotcha.Category,
                        gotcha = m.Gotcha.Description,
                        date = m.Gotcha.Date
                    }).ToList()
                });
            }
        }

        // ── Category distribution ──
        var categoryDist = allGotchas
            .GroupBy(g => g.Category, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count())
            .Select(g => new { category = g.Key, count = g.Count() })
            .ToList();

        // ── Unclustered gotchas ──
        var unclustered = allGotchas
            .Select((g, i) => new { g, i })
            .Where(x => !clusteredGotchaIds.Contains(x.i))
            .Select(x => new { type = x.g.Type, category = x.g.Category, gotcha = x.g.Description })
            .ToList();

        // ── Most-gotcha'd types ──
        var hotTypes = allGotchas
            .GroupBy(g => g.Type, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() >= 2)
            .OrderByDescending(g => g.Count())
            .Take(10)
            .Select(g => new { type = g.Key, count = g.Count(), categories = g.Select(x => x.Category).Distinct().ToList() })
            .ToList();

        // ── Generate AGENTS.md Footguns section ──
        string? footgunsMarkdown = null;
        if (generateFootguns && clusterResults.Count > 0)
        {
            footgunsMarkdown = GenerateFootgunsMarkdown(clusterResults, allGotchas.Count);
        }

        var result = new
        {
            totalGotchas = allGotchas.Count,
            clusteredCount = clusteredGotchaIds.Count,
            unclusteredCount = unclustered.Count,
            clusters = clusterResults,
            categoryDistribution = categoryDist,
            hotTypes,
            unclusteredGotchas = unclustered.Count <= 20 ? unclustered : unclustered.Take(20).ToList(),
            footgunsMarkdown
        };

        return JsonSerializer.Serialize(result, SharedJsonOptions.CamelCaseIndented);
    }

    /// <summary>
    /// Generate an AGENTS.md-ready "## Footguns" section from clustered gotchas.
    /// Output is ready to paste into AGENTS.md.
    /// </summary>
    private static string GenerateFootgunsMarkdown(List<object> clusters, int totalGotchas)
    {
        var sb = new StringBuilder();
        sb.AppendLine("## Footguns (Auto-Generated from Gotchas)");
        sb.AppendLine();
        sb.AppendLine($"> Auto-generated from {totalGotchas} gotchas by Total.Recall GetGotchaInsights.");
        sb.AppendLine($"> Last updated: {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC");
        sb.AppendLine();

        int num = 1;
        foreach (var cluster in clusters)
        {
            // Use JSON round-trip to access anonymous type properties
            var json = JsonSerializer.Serialize(cluster, SharedJsonOptions.CamelCase);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var title = root.GetProperty("cluster").GetString() ?? "Unknown";
            var count = root.GetProperty("count").GetInt32();
            var fix = root.GetProperty("canonicalFix").GetString() ?? "";
            var types = root.GetProperty("affectedTypes").EnumerateArray()
                .Select(t => t.GetString() ?? "").ToList();

            sb.AppendLine($"{num}. **{title}** ({count} occurrences across {types.Count} types)");
            sb.AppendLine($"   - Types: {string.Join(", ", types.Take(10))}");
            sb.AppendLine($"   - Fix: {fix}");
            sb.AppendLine();
            num++;
        }

        return sb.ToString();
    }

    /// <summary>
    /// Represents a gotcha cluster pattern with keyword matching and canonical fix.
    /// </summary>
    internal record GotchaCluster(string Title, string Keywords, string CanonicalFix);
}
