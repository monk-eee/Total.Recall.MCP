using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using Total.Recall.Infrastructure;
using Total.Recall.Models;

namespace Total.Recall.Tools;

[McpServerToolType]
public static class CoverageGapsTool
{
    private static readonly JsonSerializerOptions s_json = new() { WriteIndented = true };

    [McpServerTool, Description(
        "Get the top N classes ranked by uncovered lines. " +
        "Includes uncovered method names and line ranges. " +
        "Use to decide which classes to write tests for next.")]
    public static string GetCoverageGaps(
        [Description("Max results (default: 20)")] int top = 20,
        [Description("Filter out untestable classes (default: true)")] bool skipUntestable = true)
    {
        var dataDir = RepoConfig.GetDataPath();
        var store = new JsonLineStore<CoverageGap>(RepoConfig.CoverageGapsPath(dataDir));

        if (!store.HasData())
            return "No coverage data found. Run 'total-recall scan --coverage <cobertura.xml>' first.";

        var all = store.LoadAll();

        if (skipUntestable)
            all = all.Where(g => string.IsNullOrEmpty(g.SkipReason)).ToList();

        var results = all
            .OrderByDescending(g => g.UncoveredLines)
            .Take(top)
            .ToList();

        return JsonSerializer.Serialize(results, s_json);
    }
}
