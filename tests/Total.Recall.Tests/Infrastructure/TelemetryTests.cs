using Total.Recall.Infrastructure;
using Total.Recall.Models;

namespace Total.Recall.Tests.Infrastructure;

[Collection("ToolTests")]
public sealed class TelemetryTests
{
    [Fact]
    public void Track_Passive_AppendsToolCallRow()
    {
        using var h = new TelemetryTestHarness();
        var result = Telemetry.Track("test_tool", null, new { a = 1 }, () => "ok");
        Assert.Equal("ok", result);
        var rows = StoreRegistry.ForNamespace(null).ToolCalls.LoadAll();
        Assert.Single(rows);
        Assert.Equal("test_tool", rows[0].ToolName);
        Assert.False(rows[0].Error);
        Assert.Equal(2L, rows[0].ResponseBytes);
    }

    [Fact]
    public void Track_OffMode_DoesNotRecord()
    {
        using var h = new TelemetryTestHarness(mode: "off");
        var result = Telemetry.Track("test_tool", null, new { a = 1 }, () => "ok");
        Assert.Equal("ok", result);
        var rows = StoreRegistry.ForNamespace(null).ToolCalls.LoadAll();
        Assert.Empty(rows);
    }

    [Fact]
    public void Track_RepeatCall_IncrementsDedupeAndSetsRepeatOfId()
    {
        using var h = new TelemetryTestHarness();
        Telemetry.Track("t", null, new { a = 1 }, () => "first");
        Telemetry.Track("t", null, new { a = 1 }, () => "second");
        var rows = StoreRegistry.ForNamespace(null).ToolCalls.LoadAll();
        Assert.Equal(2, rows.Count);
        Assert.Equal(1, rows[0].DedupeCount);
        Assert.Equal(2, rows[1].DedupeCount);
        Assert.Null(rows[0].RepeatOfId);
        Assert.Equal(rows[0].Id, rows[1].RepeatOfId);
    }

    [Fact]
    public void Track_DifferentParams_DoNotDedupe()
    {
        using var h = new TelemetryTestHarness();
        Telemetry.Track("t", null, new { a = 1 }, () => "a");
        Telemetry.Track("t", null, new { a = 2 }, () => "b");
        var rows = StoreRegistry.ForNamespace(null).ToolCalls.LoadAll();
        Assert.Equal(2, rows.Count);
        Assert.Equal(1, rows[0].DedupeCount);
        Assert.Equal(1, rows[1].DedupeCount);
        Assert.NotEqual(rows[0].ParamHash, rows[1].ParamHash);
    }

    [Fact]
    public void Track_ErrorPrefix_RecordsErrorTrue()
    {
        using var h = new TelemetryTestHarness();
        Telemetry.Track("t", null, new { }, () => "ERROR in T: boom");
        var rows = StoreRegistry.ForNamespace(null).ToolCalls.LoadAll();
        Assert.True(rows[0].Error);
    }

    [Fact]
    public void Track_HandlerThrows_RecordsAndRethrows()
    {
        using var h = new TelemetryTestHarness();
        Assert.Throws<InvalidOperationException>(() =>
            Telemetry.Track("t", null, new { }, () => throw new InvalidOperationException("boom")));
        var rows = StoreRegistry.ForNamespace(null).ToolCalls.LoadAll();
        Assert.Single(rows);
        Assert.True(rows[0].Error);
    }

    [Fact]
    public void SummarizeParams_HandlesNull()
    {
        Assert.Equal("", Telemetry.SummarizeParams(null));
    }

    [Fact]
    public void SummarizeParams_SkipsNullProperties()
    {
        var s = Telemetry.SummarizeParams(new { a = 1, b = (string?)null, c = "hi" });
        Assert.Contains("a=1", s);
        Assert.Contains("c=hi", s);
        Assert.DoesNotContain("b=", s);
    }

    [Fact]
    public void SummarizeParams_TruncatesLongValues()
    {
        var bigValue = new string('x', 200);
        var s = Telemetry.SummarizeParams(new { v = bigValue });
        Assert.True(s.Length <= 200);
    }

    [Fact]
    public void Hash_StableForSameInput()
    {
        Assert.Equal(Telemetry.Hash("hello"), Telemetry.Hash("hello"));
        Assert.NotEqual(Telemetry.Hash("hello"), Telemetry.Hash("world"));
    }

    [Fact]
    public void Hash_EmptyReturnsZero()
    {
        Assert.Equal("0", Telemetry.Hash(""));
    }

    [Fact]
    public void Track_TaskIdStamped_WhenActive()
    {
        using var h = new TelemetryTestHarness();
        Telemetry.ActiveTaskId = "task-123";
        Telemetry.Track("t", null, new { }, () => "ok");
        Telemetry.ActiveTaskId = null;
        var rows = StoreRegistry.ForNamespace(null).ToolCalls.LoadAll();
        Assert.Equal("task-123", rows[0].TaskId);
    }
}
