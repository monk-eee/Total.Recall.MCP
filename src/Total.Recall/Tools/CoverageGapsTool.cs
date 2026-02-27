using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using Total.Recall.Infrastructure;
using Total.Recall.Models;

namespace Total.Recall.Tools;

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
        [Description("Sort by: 'roi' (default), 'uncovered', 'coverage'")] string sortBy = "roi")
    {
        try
        {
        return GetCoverageGapsCore(top, skipUntestable, sortBy);
        }
        catch (Exception ex)
        {
            Log.Error($"[GetCoverageGaps] failed: {ex.GetType().Name}: {ex.Message}");
            return $"ERROR in GetCoverageGaps: {ex.GetType().Name}: {ex.Message}";
        }
    }

    private static string GetCoverageGapsCore(int top, bool skipUntestable, string sortBy)
    {
        if (!StoreRegistry.CoverageGaps.HasData())
            return "No coverage data found. Run 'total-recall scan --coverage <cobertura.xml>' first.";

        var all = StoreRegistry.CoverageGaps.LoadAll();

        IEnumerable<CoverageGap> filtered = all;
        if (skipUntestable)
            filtered = all.Where(g => string.IsNullOrEmpty(g.SkipReason));

        var scored = filtered.Select(g => new
        {
            gap = g,
            roiScore = CalculateRoi(g)
        });

        var ordered = sortBy.ToLowerInvariant() switch
        {
            "uncovered" => scored.OrderByDescending(x => x.gap.UncoveredLines),
            "coverage" => scored.OrderBy(x => x.gap.CoveragePercent),
            _ => scored.OrderByDescending(x => x.roiScore)
        };

        var results = ordered.Take(top).Select(x => new
        {
            x.gap.Class,
            x.gap.Namespace,
            x.gap.File,
            x.gap.TotalLines,
            x.gap.CoveredLines,
            x.gap.UncoveredLines,
            x.gap.CoveragePercent,
            x.gap.UncoveredMethods,
            x.gap.ExistingTestCount,
            x.gap.Testability,
            RoiScore = Math.Round(x.roiScore, 1)
        }).ToList();

        return JsonSerializer.Serialize(results, SharedJsonOptions.CamelCaseIndented);
    }


    /// <summary>
    /// ROI = uncoveredLines * testabilityMultiplier / (1 + existingTestCount).
    /// Higher score = more value from writing tests for this class.
    /// </summary>
    private static double CalculateRoi(CoverageGap gap)
    {
        var testabilityMultiplier = (gap.Testability?.ToLowerInvariant()) switch
        {
            "high" => 1.0,
            "medium" => 0.7,
            "low" => 0.3,
            _ => 0.5 // unknown
        };

        return gap.UncoveredLines * testabilityMultiplier / (1 + gap.ExistingTestCount);
    }
}
