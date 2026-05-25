using Total.Recall.Infrastructure;
using Total.Recall.Tests.Infrastructure;
using Total.Recall.Tools;

namespace Total.Recall.Tests.Tools;

[Collection("ToolTests")]
public sealed class CyclesToolTests
{
    [Fact]
    public void GetCycles_ReturnsEmptyEnvelope_WhenNoCyclesYet()
    {
        using var h = new TelemetryTestHarness();
        var result = CyclesTool.GetCycles();
        Assert.Contains("\"totalCycles\": 0", result);
    }

    [Fact]
    public void GetCycles_ReturnsDetectedCycles()
    {
        using var h = new TelemetryTestHarness();
        for (var i = 0; i < 3; i++)
            Telemetry.Track("resolve_type", null, new { name = "X" }, () => "r");
        var result = CyclesTool.GetCycles();
        Assert.Contains("re-query", result);
    }

    [Fact]
    public void GetCycles_PatternFilter_Works()
    {
        using var h = new TelemetryTestHarness();
        for (var i = 0; i < 3; i++)
            Telemetry.Track("resolve_type", null, new { name = "X" }, () => "r");
        var result = CyclesTool.GetCycles(pattern: "oscillation");
        Assert.Contains("\"returned\": 0", result);
    }
}
