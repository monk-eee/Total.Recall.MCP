using Total.Recall.Infrastructure;
using Total.Recall.Tests.Infrastructure;
using Total.Recall.Tools;

namespace Total.Recall.Tests.Tools;

[Collection("ToolTests")]
public sealed class TaskToolTests
{
    [Fact]
    public void StartTask_SetsActiveTaskIdAndReturnsId()
    {
        using var h = new TelemetryTestHarness();
        var result = TaskTool.StartTask("MyClass", "test it");
        Assert.NotNull(Telemetry.ActiveTaskId);
        Assert.Contains("taskId", result);
        Assert.Contains(Telemetry.ActiveTaskId!, result);
    }

    [Fact]
    public void EndTask_WritesRowAndClearsActiveTaskId()
    {
        using var h = new TelemetryTestHarness();
        TaskTool.StartTask("MyClass");
        var taskId = Telemetry.ActiveTaskId!;
        Telemetry.Track("get_context", null, new { x = 1 }, () => "data");
        var ended = TaskTool.EndTask("success", testsGenerated: 3, coveredLines: 12);
        Assert.Contains("success", ended);
        Assert.Null(Telemetry.ActiveTaskId);
        var tasks = StoreRegistry.ForNamespace(null).Tasks.LoadAll();
        var row = Assert.Single(tasks);
        Assert.Equal(taskId, row.Id);
        Assert.Equal("MyClass", row.Target);
        Assert.Equal("success", row.Outcome);
        Assert.Equal(3, row.TestsGenerated);
        Assert.Equal(12, row.CoveredLines);
        Assert.True(row.ToolCalls >= 1, "task should have captured the get_context call");
    }

    [Fact]
    public void EndTask_WithNoActiveTask_ReturnsFriendlyMessage()
    {
        using var h = new TelemetryTestHarness();
        var r = TaskTool.EndTask("success");
        Assert.Contains("No active task", r);
    }

    [Fact]
    public void StartTask_TwiceInARow_AutoAbandonsPreviousTask()
    {
        using var h = new TelemetryTestHarness();
        TaskTool.StartTask("First");
        TaskTool.StartTask("Second");
        var tasks = StoreRegistry.ForNamespace(null).Tasks.LoadAll();
        Assert.Single(tasks);
        Assert.Equal("First", tasks[0].Target);
        Assert.Equal("abandoned", tasks[0].Outcome);
    }

    [Fact]
    public void LogTask_UpdatesTokensOnEndedRow()
    {
        using var h = new TelemetryTestHarness();
        TaskTool.StartTask("X");
        var id = Telemetry.ActiveTaskId!;
        TaskTool.EndTask("success");
        TaskTool.LogTask(id, inputTokens: 1000, outputTokens: 200);
        var tasks = StoreRegistry.ForNamespace(null).Tasks.LoadAll();
        // Last row for id should reflect updated tokens (WriteAll rewrites the file)
        var last = tasks.Last(t => t.Id == id);
        Assert.Equal(1000, last.InputTokens);
        Assert.Equal(200, last.OutputTokens);
    }

    [Fact]
    public void LogTask_ForUnknownTaskId_AppendsInProgressRow()
    {
        using var h = new TelemetryTestHarness();
        TaskTool.LogTask("unknown-id", 500, 100);
        var tasks = StoreRegistry.ForNamespace(null).Tasks.LoadAll();
        var row = Assert.Single(tasks);
        Assert.Equal("unknown-id", row.Id);
        Assert.Equal("in-progress", row.Outcome);
        Assert.Equal(500, row.InputTokens);
    }
}
