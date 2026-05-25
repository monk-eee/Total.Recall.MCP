using Total.Recall.Infrastructure;

namespace Total.Recall.Tests.Infrastructure;

[Collection("ToolTests")]
public sealed class CycleDetectorTests
{
    [Fact]
    public void ReQuery_FiresOnThirdIdenticalCall()
    {
        using var h = new TelemetryTestHarness();
        Telemetry.Track("resolve_type", null, new { name = "Foo" }, () => "1");
        Telemetry.Track("resolve_type", null, new { name = "Foo" }, () => "2");
        // Cut: at 2 calls, context-loss may fire (lookup tool, no write). Verify re-query NOT yet fired.
        var cyclesAfter2 = StoreRegistry.ForNamespace(null).Cycles.LoadAll();
        Assert.DoesNotContain(cyclesAfter2, c => c.Pattern == "re-query");

        Telemetry.Track("resolve_type", null, new { name = "Foo" }, () => "3");
        var cyclesAfter3 = StoreRegistry.ForNamespace(null).Cycles.LoadAll();
        Assert.Contains(cyclesAfter3, c => c.Pattern == "re-query");
    }

    [Fact]
    public void ContextLoss_FiresWhenLookupRepeatsWithNoWriteBetween()
    {
        using var h = new TelemetryTestHarness();
        Telemetry.Track("resolve_type", null, new { name = "Bar" }, () => "1");
        Telemetry.Track("resolve_type", null, new { name = "Bar" }, () => "2");
        var cycles = StoreRegistry.ForNamespace(null).Cycles.LoadAll();
        Assert.Contains(cycles, c => c.Pattern == "context-loss");
    }

    [Fact]
    public void ContextLoss_DoesNotFireWhenWriteToolBreaksTheLoop()
    {
        using var h = new TelemetryTestHarness();
        Telemetry.Track("resolve_type", null, new { name = "Baz" }, () => "1");
        Telemetry.Track("add_assessment", null, new { className = "Baz" }, () => "ok");
        Telemetry.Track("resolve_type", null, new { name = "Baz" }, () => "2");
        var cycles = StoreRegistry.ForNamespace(null).Cycles.LoadAll();
        Assert.DoesNotContain(cycles, c => c.Pattern == "context-loss");
    }

    [Fact]
    public void Oscillation_FiresOnThreeDistinctSnippetTargets()
    {
        using var h = new TelemetryTestHarness();
        Telemetry.Track("get_source_snippet", null, new { className = "A" }, () => "a");
        Telemetry.Track("get_source_snippet", null, new { className = "B" }, () => "b");
        Telemetry.Track("get_source_snippet", null, new { className = "C" }, () => "c");
        var cycles = StoreRegistry.ForNamespace(null).Cycles.LoadAll();
        Assert.Contains(cycles, c => c.Pattern == "oscillation");
    }

    [Fact]
    public void Oscillation_SuppressedByAddAssessmentBetween()
    {
        using var h = new TelemetryTestHarness();
        Telemetry.Track("get_source_snippet", null, new { className = "A" }, () => "a");
        Telemetry.Track("add_assessment", null, new { className = "A" }, () => "ok");
        Telemetry.Track("get_source_snippet", null, new { className = "B" }, () => "b");
        Telemetry.Track("add_assessment", null, new { className = "B" }, () => "ok");
        Telemetry.Track("get_source_snippet", null, new { className = "C" }, () => "c");
        var cycles = StoreRegistry.ForNamespace(null).Cycles.LoadAll();
        Assert.DoesNotContain(cycles, c => c.Pattern == "oscillation");
    }

    [Fact]
    public void ReQuery_FiresOnlyOnce_NotEveryAdditionalCall()
    {
        using var h = new TelemetryTestHarness();
        for (var i = 0; i < 6; i++)
            Telemetry.Track("resolve_type", null, new { name = "X" }, () => "r");
        var cycles = StoreRegistry.ForNamespace(null).Cycles.LoadAll();
        var reQuery = cycles.Where(c => c.Pattern == "re-query").ToList();
        Assert.Single(reQuery);
    }
}
