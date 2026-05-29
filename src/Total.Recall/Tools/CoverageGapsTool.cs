using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using Total.Recall.Infrastructure;
using Total.Recall.Models;

namespace Total.Recall.Tools;

/// <summary>
/// MCP tool for querying coverage gap data. Returns classes ranked by ROI score
/// (factoring in uncovered lines, testability, and existing test count) to help
/// AI agents prioritize which classes to write tests for next.
/// </summary>
[McpServerToolType]
public static class CoverageGapsTool
{
    [McpServerTool, Description(
        "Get the top N classes ranked by ROI score (factors in uncovered lines, " +
        "testability, and existing test count). " +
        "Includes uncovered method names and line ranges. " +
        "Use to decide which classes to write tests for next.")]
    public static string GetCoverageGaps(
        [Description("Max results (default: 20)")] int top = 20,
        [Description("Filter out untestable classes (default: true)")] bool skipUntestable = true,
        [Description("Sort by: 'roi' (default), 'uncovered', 'coverage'")] string sortBy = "roi",
        [Description("Return condensed summary (class, uncoveredLines, ROI) without method details (default: false)")] bool summaryOnly = false,
        [Description("Optional: namespace/session to query (default: server default)")] string? ns = null)
    {
        return Telemetry.Track("get_coverage_gaps", ns, new { top, skipUntestable, sortBy, summaryOnly, ns }, () =>
        {
        Metrics.Increment(Metrics.ToolGetCoverageGaps);
        Log.Debug($"[GetCoverageGaps] top={top} skipUntestable={skipUntestable} sortBy='{sortBy}' summaryOnly={summaryOnly} ns='{ns ?? "(default)"}'");
        try
        {
            return GetCoverageGapsCore(top, skipUntestable, sortBy, summaryOnly, ns);
        }
        catch (Exception ex)
        {
            Log.Error($"[GetCoverageGaps] failed: {ex.GetType().Name}: {ex.Message}");
            return $"ERROR in GetCoverageGaps: {ex.GetType().Name}: {ex.Message}";
        }
        });
    }

    private static string GetCoverageGapsCore(int top, bool skipUntestable, string sortBy, bool summaryOnly, string? ns)
    {
        var stores = StoreRegistry.ForNamespace(ns);

        if (!stores.CoverageGaps.HasData())
        {
            Log.Debug("[GetCoverageGaps] no coverage data found");
            return "No coverage data found. Run 'total-recall scan --coverage <cobertura.xml>' first.";
        }

        var all = stores.CoverageGaps.LoadAll();
        Log.Debug($"[GetCoverageGaps] loaded {all.Count} classes, applying filters (skipUntestable={skipUntestable})");

        IEnumerable<CoverageGap> filtered = all;
        if (skipUntestable)
            filtered = all.Where(g => g.TestabilityScore is null or >= 0.3);

        var scored = filtered.Select(g => new
        {
            gap = g,
            roiScore = CalculateRoi(g)
        });

        var ordered = sortBy.ToLowerInvariant() switch
        {
            "uncovered" => scored.OrderByDescending(x => x.gap.UncoveredLineCount),
            "coverage" => scored.OrderBy(x => x.gap.CoveragePercent),
            _ => scored.OrderByDescending(x => x.roiScore)
        };

        var results = ordered.Take(top).ToList();

        if (summaryOnly)
        {
            var summaryResults = results.Select(x => new
            {
                className = x.gap.ClassName,
                x.gap.LinesTotal,
                x.gap.LinesCovered,
                uncoveredLineCount = x.gap.UncoveredLineCount,
                x.gap.CoveragePercent,
                x.gap.ExistingTests,
                roiScore = Math.Round(x.roiScore, 1)
            }).ToList();
            return JsonSerializer.Serialize(summaryResults, SharedJsonOptions.CamelCaseIndented);
        }

        var detailedResults = results.Select(x => new
        {
            x.gap.SchemaVersion,
            x.gap.ClassName,
            x.gap.FilePath,
            x.gap.LinesTotal,
            x.gap.LinesCovered,
            uncoveredLineCount = x.gap.UncoveredLineCount,
            x.gap.CoveragePercent,
            x.gap.UncoveredMethods,
            x.gap.ExistingTests,
            x.gap.TestabilityScore,
            roiScore = Math.Round(x.roiScore, 1)
        }).ToList();

        return JsonSerializer.Serialize(detailedResults, SharedJsonOptions.CamelCaseIndented);
    }


    /// <summary>
    /// ROI = uncoveredLineCount * testabilityScore / (1 + existingTests).
    /// Null testabilityScore defaults to 0.5 (neutral baseline). Higher = better target.
    /// </summary>
    private static double CalculateRoi(CoverageGap gap)
    {
        var testability = gap.TestabilityScore ?? 0.5;
        return gap.UncoveredLineCount * testability / (1 + (gap.ExistingTests ?? 0));
    }
}
