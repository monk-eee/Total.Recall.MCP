using Total.Recall.Infrastructure;
using Total.Recall.Models;
using Total.Recall.Tests.Infrastructure;
using Total.Recall.Tools;

namespace Total.Recall.Tests.Tools;

[Collection("ToolTests")]
public sealed class ScorecardToolTests
{
    [Fact]
    public void GetToolCallStats_AggregatesByTool()
    {
        using var h = new TelemetryTestHarness();
        Telemetry.Track("tool_a", null, new { i = 1 }, () => "x");
        Telemetry.Track("tool_a", null, new { i = 2 }, () => "yy");
        Telemetry.Track("tool_b", null, new { }, () => "zzz");
        var r = ScorecardTool.GetToolCallStats(currentSessionOnly: false);
        Assert.Contains("tool_a", r);
        Assert.Contains("tool_b", r);
    }

    [Fact]
    public void GetToolCallStats_NoCalls_ReturnsFriendlyMessage()
    {
        using var h = new TelemetryTestHarness();
        var r = ScorecardTool.GetToolCallStats();
        Assert.Contains("No tool calls recorded", r);
    }

    [Fact]
    public void GetEfficiencyReport_ShowsDedupeRatio()
    {
        using var h = new TelemetryTestHarness();
        Telemetry.Track("t", null, new { a = 1 }, () => "11111");
        Telemetry.Track("t", null, new { a = 1 }, () => "11111");
        var r = ScorecardTool.GetEfficiencyReport();
        Assert.Contains("dedupeRatePct", r);
        Assert.Contains("wastedBytesRatioPct", r);
    }

    [Fact]
    public void GetModelScorecard_NoSessions_ReturnsFriendlyMessage()
    {
        using var h = new TelemetryTestHarness();
        var r = ScorecardTool.GetModelScorecard();
        Assert.Contains("No sessions logged", r);
    }

    [Fact]
    public void GetModelScorecard_AggregatesByModel()
    {
        using var h = new TelemetryTestHarness();
        var sessions = StoreRegistry.ForNamespace(null).Sessions;
        sessions.Append(new SessionRecord
        {
            SessionId = "s1",
            Model = "claude",
            TotalTokens = 1000,
            CoveredLines = 50,
            TestsGenerated = 10
        });
        sessions.Append(new SessionRecord
        {
            SessionId = "s2",
            Model = "gpt-5",
            TotalTokens = 2000,
            CoveredLines = 40,
            TestsGenerated = 10
        });
        var r = ScorecardTool.GetModelScorecard();
        Assert.Contains("claude", r);
        Assert.Contains("gpt-5", r);
        Assert.Contains("tokensPerCoveredLine", r);
    }
}
