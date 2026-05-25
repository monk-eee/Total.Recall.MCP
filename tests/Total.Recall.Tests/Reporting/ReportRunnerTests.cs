using Total.Recall.Infrastructure;
using Total.Recall.Models;
using Total.Recall.Reporting;
using Total.Recall.Tests.Infrastructure;

namespace Total.Recall.Tests.Reporting;

[Collection("ToolTests")]
public class ReportRunnerTests
{
    [Fact]
    public void RunReport_WithNoSubCommand_PrintsHelpAndReturns1()
    {
        using var sw = new StringWriter();
        var exit = ReportRunner.RunReport(["report"], sw);
        Assert.Equal(1, exit);
        Assert.Contains("Usage: total-recall report", sw.ToString());
        Assert.Contains("tool-stats", sw.ToString());
    }

    [Fact]
    public void RunReport_WithHelpFlag_PrintsHelpAndReturns0()
    {
        using var sw = new StringWriter();
        var exit = ReportRunner.RunReport(["report", "--help"], sw);
        Assert.Equal(0, exit);
        Assert.Contains("Usage: total-recall report", sw.ToString());
    }

    [Fact]
    public void RunReport_WithUnknownSubCommand_PrintsErrorAndReturns1()
    {
        using var sw = new StringWriter();
        var exit = ReportRunner.RunReport(["report", "bogus"], sw);
        Assert.Equal(1, exit);
        Assert.Contains("Unknown report sub-command: 'bogus'", sw.ToString());
    }

    [Fact]
    public void RunReport_ToolStats_WithNoData_ReturnsEmptyMessage()
    {
        using var harness = new TelemetryTestHarness();
        using var sw = new StringWriter();
        var exit = ReportRunner.RunReport(["report", "tool-stats"], sw);
        Assert.Equal(0, exit);
        Assert.Contains("No tool calls recorded yet", sw.ToString());
    }

    [Fact]
    public void RunReport_Sessions_WithNoData_ReturnsValidJson()
    {
        using var harness = new TelemetryTestHarness();
        using var sw = new StringWriter();
        var exit = ReportRunner.RunReport(["report", "sessions"], sw);
        Assert.Equal(0, exit);
        // GetSessionsCore returns JSON envelope even when empty
        var output = sw.ToString().Trim();
        Assert.True(output.StartsWith('{') || output.Contains("No sessions"),
            $"Expected JSON or empty-message but got: {output[..Math.Min(200, output.Length)]}");
    }

    [Fact]
    public void RunReport_Scorecard_WithNoData_ReturnsEmptyMessage()
    {
        using var harness = new TelemetryTestHarness();
        using var sw = new StringWriter();
        var exit = ReportRunner.RunReport(["report", "scorecard"], sw);
        Assert.Equal(0, exit);
        Assert.Contains("No sessions logged yet", sw.ToString());
    }

    [Fact]
    public void RunReport_Leaderboard_WithNoData_ReturnsEmptyMessage()
    {
        using var harness = new TelemetryTestHarness();
        using var sw = new StringWriter();
        var exit = ReportRunner.RunReport(["report", "leaderboard"], sw);
        Assert.Equal(0, exit);
        Assert.Contains("No challenge submissions yet", sw.ToString());
    }

    [Fact]
    public void RunReport_RestoresTelemetryModeAfterCall()
    {
        using var harness = new TelemetryTestHarness("passive");
        Assert.Equal("passive", Environment.GetEnvironmentVariable("TOTAL_RECALL_MODE"));

        using var sw = new StringWriter();
        ReportRunner.RunReport(["report", "tool-stats"], sw);

        // Mode env var should be restored to "passive" (its prior value)
        Assert.Equal("passive", Environment.GetEnvironmentVariable("TOTAL_RECALL_MODE"));
    }

    [Fact]
    public void RunReport_ToolStats_DoesNotPolluteToolCallsJsonl()
    {
        using var harness = new TelemetryTestHarness("passive");

        // Pre-seed one tool call so the file exists with known count
        var stores = StoreRegistry.ForNamespace(null);
        stores.ToolCalls.Append(new ToolCall
        {
            Id = Guid.NewGuid().ToString(),
            ToolName = "seeded",
            Namespace = "default",
            SessionId = "test-session",
            Timestamp = DateTimeOffset.UtcNow.ToString("O"),
            LatencyMs = 5,
            ResponseBytes = 100
        });
        var initialCount = stores.ToolCalls.LoadAll().Count;

        using var sw = new StringWriter();
        ReportRunner.RunReport(["report", "tool-stats"], sw);

        // Re-read after the report ran. The report itself should NOT have appended
        // a new tool-call record because it forces MODE=off.
        StoreRegistry.Reset();
        var stores2 = StoreRegistry.ForNamespace(null);
        var finalCount = stores2.ToolCalls.LoadAll().Count;
        Assert.Equal(initialCount, finalCount);
    }

    [Fact]
    public void ParseOptions_ParsesNamespaceLastAndPattern()
    {
        var opts = ReportRunner.ParseOptions(
            ["report", "cycles", "--ns", "myproj", "--last", "42", "--pattern", "re-query"]);
        Assert.Equal("myproj", opts.Namespace);
        Assert.Equal(42, opts.Last);
        Assert.Equal("re-query", opts.Pattern);
    }

    [Fact]
    public void ParseOptions_AcceptsNamespaceAliasFlag()
    {
        var opts = ReportRunner.ParseOptions(["report", "sessions", "--namespace", "alt"]);
        Assert.Equal("alt", opts.Namespace);
    }

    [Fact]
    public void ParseOptions_IgnoresInvalidLastValue()
    {
        var opts = ReportRunner.ParseOptions(["report", "cycles", "--last", "notanumber"]);
        Assert.Null(opts.Last);
    }
}
