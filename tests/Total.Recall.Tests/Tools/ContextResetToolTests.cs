using Total.Recall.Infrastructure;
using Total.Recall.Tests.Infrastructure;
using Total.Recall.Tools;

namespace Total.Recall.Tests.Tools;

[Collection("ToolTests")]
public sealed class ContextResetToolTests
{
    [Fact]
    public void ReportContextReset_AppendsCycleRecordWithExpiryPattern()
    {
        using var h = new TelemetryTestHarness();
        var before = Metrics.Get(Metrics.ContextResetsReported);
        var result = ContextResetTool.ReportContextReset("hit token cap", priorSessionId: "abc123");
        Assert.Contains("Recorded context-expiry", result);

        var cycles = StoreRegistry.ForNamespace(null).Cycles.LoadAll();
        var row = Assert.Single(cycles);
        Assert.Equal("context-expiry", row.Pattern);
        Assert.Contains("abc123", row.Evidence[0]);
        Assert.Contains("hit token cap", row.Note);

        Assert.Equal(before + 1, Metrics.Get(Metrics.ContextResetsReported));
    }

    [Fact]
    public void ReportContextReset_NoNote_UsesDefaultNote()
    {
        using var h = new TelemetryTestHarness();
        ContextResetTool.ReportContextReset();
        var cycles = StoreRegistry.ForNamespace(null).Cycles.LoadAll();
        var row = Assert.Single(cycles);
        Assert.Equal("context reset reported", row.Note);
    }
}
