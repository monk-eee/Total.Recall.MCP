using Total.Recall.Infrastructure;
using Total.Recall.Models;
using Total.Recall.Tests.Infrastructure;
using Total.Recall.Tools;

namespace Total.Recall.Tests.Tools;

[Collection("ToolTests")]
public sealed class ChallengeToolTests
{
    private static void SeedChallenge(string? ns = null)
    {
        var c = new ChallengeRecord
        {
            Id = "c1",
            Category = "mocking",
            Prompt = "p",
            Expected = new ChallengeExpectation
            {
                MustCallTools = new() { "get_mock_recipe" },
                MaxToolCalls = 50,
                OutputMustContain = new() { "[Fact]" }
            }
        };
        StoreRegistry.ForNamespace(ns).Challenges.Append(c);
    }

    [Fact]
    public void Tools_BlockedWhenModeNotActiveEval()
    {
        using var h = new TelemetryTestHarness(mode: "passive");
        var r = ChallengeTool.GetNextChallenge("claude");
        Assert.Contains("active-eval tools are inactive", r, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetNextChallenge_ReturnsFirstUnpassedChallenge()
    {
        using var h = new TelemetryTestHarness(mode: "active-eval");
        SeedChallenge();
        var r = ChallengeTool.GetNextChallenge("claude");
        Assert.Contains("c1", r);
        Assert.Contains("mocking", r);
    }

    [Fact]
    public void GetNextChallenge_NoChallengesAvailable_ReturnsMessage()
    {
        using var h = new TelemetryTestHarness(mode: "active-eval");
        var r = ChallengeTool.GetNextChallenge("claude");
        Assert.Contains("No challenges registered", r);
    }

    [Fact]
    public void SubmitChallenge_RecordsEvalAndReturnsGrade()
    {
        using var h = new TelemetryTestHarness(mode: "active-eval");
        SeedChallenge();
        // Pretend the agent already called get_mock_recipe this session.
        Telemetry.Track("get_mock_recipe", null, new { i = "IFoo" }, () => "stub");
        var r = ChallengeTool.SubmitChallenge("c1", "claude", "[Fact] public void T() {}");
        Assert.Contains("passed", r);
        var evals = StoreRegistry.ForNamespace(null).Evals.LoadAll();
        var row = Assert.Single(evals);
        Assert.Equal("c1", row.ChallengeId);
        Assert.Equal("claude", row.Model);
    }

    [Fact]
    public void SubmitChallenge_UnknownChallenge_ReturnsNotFound()
    {
        using var h = new TelemetryTestHarness(mode: "active-eval");
        var r = ChallengeTool.SubmitChallenge("missing", "claude", "anything");
        Assert.Contains("not found", r);
    }

    [Fact]
    public void GetEvalLeaderboard_AggregatesByModel()
    {
        using var h = new TelemetryTestHarness(mode: "active-eval");
        var evals = StoreRegistry.ForNamespace(null).Evals;
        evals.Append(new EvalRecord { Model = "claude", Passed = true, Score = 0.9, ToolCallsObserved = 5 });
        evals.Append(new EvalRecord { Model = "claude", Passed = false, Score = 0.3, ToolCallsObserved = 9 });
        evals.Append(new EvalRecord { Model = "gpt-5", Passed = true, Score = 1.0, ToolCallsObserved = 3 });
        var r = ChallengeTool.GetEvalLeaderboard();
        Assert.Contains("claude", r);
        Assert.Contains("gpt-5", r);
        Assert.Contains("passRatePct", r);
    }
}
