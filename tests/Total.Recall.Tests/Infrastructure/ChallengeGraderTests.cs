using Total.Recall.Infrastructure;
using Total.Recall.Models;

namespace Total.Recall.Tests.Infrastructure;

[Collection("ToolTests")]
public sealed class ChallengeGraderTests
{
    private static ChallengeRecord BasicChallenge() => new()
    {
        Id = "c1",
        Category = "mocking",
        Prompt = "test something",
        Expected = new ChallengeExpectation
        {
            MustCallTools = new() { "get_mock_recipe", "generate_test_scaffold" },
            MustNotCallTools = new() { "add_gotcha" },
            MaxToolCalls = 5,
            OutputMustContain = new() { "[Fact]" },
            OutputMustNotContain = new() { "TODO" }
        }
    };

    private static ToolCall Call(string tool) => new()
    {
        Id = Guid.NewGuid().ToString("N"),
        SessionId = "s",
        ToolName = tool,
        Timestamp = DateTime.UtcNow.ToString("O")
    };

    [Fact]
    public void Grade_AllChecksPass_PassesWithHighScore()
    {
        var calls = new[] { Call("get_mock_recipe"), Call("generate_test_scaffold") };
        var r = ChallengeGrader.Grade(BasicChallenge(), "public class T { [Fact] public void X() {} }", calls);
        Assert.True(r.Passed);
        Assert.True(r.Score >= 0.7);
    }

    [Fact]
    public void Grade_MissingRequiredTool_FailsRequiredCheck()
    {
        var calls = new[] { Call("get_mock_recipe") };
        var r = ChallengeGrader.Grade(BasicChallenge(), "[Fact]", calls);
        Assert.True(r.Breakdown["calledRequiredTools"] < 1.0);
        Assert.Contains("missing required tools", r.Feedback);
    }

    [Fact]
    public void Grade_ForbiddenTool_FailsBudgetCheck()
    {
        var calls = new[] { Call("get_mock_recipe"), Call("generate_test_scaffold"), Call("add_gotcha") };
        var r = ChallengeGrader.Grade(BasicChallenge(), "[Fact]", calls);
        Assert.True(r.Breakdown["stayedUnderBudget"] < 1.0);
    }

    [Fact]
    public void Grade_ExceedsBudget_FailsBudgetCheck()
    {
        var calls = Enumerable.Range(0, 10).Select(_ => Call("get_mock_recipe")).ToArray();
        var r = ChallengeGrader.Grade(BasicChallenge(), "[Fact] generate_test_scaffold", calls);
        Assert.True(r.Breakdown["stayedUnderBudget"] < 1.0);
    }

    [Fact]
    public void Grade_OutputMissingRequired_FailsOutputCheck()
    {
        var calls = new[] { Call("get_mock_recipe"), Call("generate_test_scaffold") };
        var r = ChallengeGrader.Grade(BasicChallenge(), "no fact attr here", calls);
        Assert.True(r.Breakdown["outputCorrectness"] < 1.0);
    }

    [Fact]
    public void Grade_OutputContainsForbidden_FailsOutputCheck()
    {
        var calls = new[] { Call("get_mock_recipe"), Call("generate_test_scaffold") };
        var r = ChallengeGrader.Grade(BasicChallenge(), "[Fact] TODO finish me", calls);
        Assert.True(r.Breakdown["outputCorrectness"] < 1.0);
    }

    [Fact]
    public void Grade_AllChecksFail_DoesNotPass()
    {
        var calls = new[] { Call("add_gotcha") };
        var r = ChallengeGrader.Grade(BasicChallenge(), "TODO nothing useful", calls);
        Assert.False(r.Passed);
        Assert.True(r.Score < 0.7);
    }
}
