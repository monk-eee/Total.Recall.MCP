using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using Total.Recall.Infrastructure;
using Total.Recall.Models;

namespace Total.Recall.Tools;

/// <summary>
/// Cut 3 — task bracketing. A task is a logical unit of work below a session
/// (e.g. "test ClassFoo"). While a task is open, every tool call is stamped
/// with its id so we can attribute response-bytes / latency / cycles per task.
///
/// Tasks are in-process state — they do not survive a server restart.
/// </summary>
[McpServerToolType]
public static class TaskTool
{
    internal sealed class ActiveTask
    {
        public string Id { get; set; } = "";
        public string Target { get; set; } = "";
        public string Description { get; set; } = "";
        public DateTime StartedUtc { get; set; }
        public int ToolCallsAtStart { get; set; }
        public int RepeatToolCallsAtStart { get; set; }
        public string? Namespace { get; set; }
    }

    private static ActiveTask? s_current;
    private static readonly object s_lock = new();

    [McpServerTool, Description(
        "Cut 3 — Begin a tracked unit of work (e.g. testing one class). Every subsequent " +
        "tool call until end_task is stamped with this task's id. Returns the new task id. " +
        "Tasks let get_model_scorecard attribute tokens/cycles per class.")]
    public static string StartTask(
        [Description("What you're working on (e.g. class name, target)")] string target,
        [Description("Optional one-line description of the goal")] string description = "",
        [Description("Optional: namespace/session (default: server default)")] string? ns = null)
    {
        return Telemetry.Track("start_task", ns, new { target, description, ns }, () =>
        {
            try
            {
                lock (s_lock)
                {
                    if (s_current is not null)
                    {
                        // Auto-abandon the previous open task — never leak state.
                        WriteFinalRow(s_current, "abandoned", "", 0, 0, "auto-abandoned by start_task");
                    }
                    s_current = new ActiveTask
                    {
                        Id = Guid.NewGuid().ToString("N"),
                        Target = target,
                        Description = description,
                        StartedUtc = DateTime.UtcNow,
                        ToolCallsAtStart = (int)Metrics.Get(Metrics.ToolCallsRecorded),
                        RepeatToolCallsAtStart = (int)Metrics.Get(Metrics.ToolCallsRepeat),
                        Namespace = ns
                    };
                    Telemetry.ActiveTaskId = s_current.Id;
                    Metrics.Increment(Metrics.TasksStarted);
                }
                Log.Info($"[StartTask] {s_current!.Id} target='{target}'");
                return JsonSerializer.Serialize(new { taskId = s_current.Id, target, started = s_current.StartedUtc.ToString("O") }, SharedJsonOptions.CamelCaseIndented);
            }
            catch (Exception ex)
            {
                Log.Error($"[StartTask] failed: {ex.GetType().Name}: {ex.Message}");
                return $"ERROR in StartTask: {ex.Message}";
            }
        });
    }

    [McpServerTool, Description(
        "Cut 3 — End the currently active task. Writes a row to tasks.jsonl with " +
        "duration, tool calls observed, dedupe rate, response bytes served, and outcome. " +
        "Outcomes: success | fail | abandoned.")]
    public static string EndTask(
        [Description("Outcome: success | fail | abandoned")] string outcome = "success",
        [Description("Optional: tests generated for this task")] int testsGenerated = 0,
        [Description("Optional: covered lines attributable to this task")] int coveredLines = 0,
        [Description("Optional: free-form notes (failure reason, observations)")] string notes = "",
        [Description("Optional: namespace/session (default: server default)")] string? ns = null)
    {
        return Telemetry.Track("end_task", ns, new { outcome, testsGenerated, coveredLines, ns }, () =>
        {
            try
            {
                ActiveTask? task;
                lock (s_lock)
                {
                    task = s_current;
                    s_current = null;
                    Telemetry.ActiveTaskId = null;
                }
                if (task is null) return "No active task to end. Call start_task first.";
                WriteFinalRow(task, outcome, notes, testsGenerated, coveredLines, "");
                Metrics.Increment(Metrics.TasksEnded);
                return $"Ended task {task.Id} target='{task.Target}' outcome={outcome}";
            }
            catch (Exception ex)
            {
                Log.Error($"[EndTask] failed: {ex.GetType().Name}: {ex.Message}");
                return $"ERROR in EndTask: {ex.Message}";
            }
        });
    }

    [McpServerTool, Description(
        "Cut 3 — Log per-task token usage. Cheaper than waiting until log_session — the " +
        "scorecard can read these between sessions. Pass the same taskId returned by start_task.")]
    public static string LogTask(
        [Description("Task id (from start_task)")] string taskId,
        [Description("Prompt/input tokens reported by the agent")] long inputTokens,
        [Description("Completion/output tokens reported by the agent")] long outputTokens,
        [Description("Optional: namespace/session (default: server default)")] string? ns = null)
    {
        return Telemetry.Track("log_task", ns, new { taskId, inputTokens, outputTokens, ns }, () =>
        {
            try
            {
                var stores = StoreRegistry.ForNamespace(ns);
                // Find the most recent row matching this taskId and overwrite tokens.
                var all = stores.Tasks.LoadAll();
                var idx = -1;
                for (var i = all.Count - 1; i >= 0; i--)
                {
                    if (all[i].Id == taskId) { idx = i; break; }
                }
                if (idx < 0)
                {
                    // No row yet (task still open) — record a partial row that EndTask will supersede.
                    var partial = new TaskRecord
                    {
                        Id = taskId,
                        SessionId = Telemetry.SessionId,
                        Target = "(open)",
                        StartedUtc = DateTime.UtcNow.ToString("O"),
                        EndedUtc = "",
                        Outcome = "in-progress",
                        InputTokens = inputTokens,
                        OutputTokens = outputTokens
                    };
                    stores.Tasks.Append(partial);
                    return $"Logged tokens for in-progress task {taskId}";
                }
                all[idx].InputTokens = inputTokens;
                all[idx].OutputTokens = outputTokens;
                stores.Tasks.WriteAll(all);
                return $"Updated tokens for task {taskId}: input={inputTokens}, output={outputTokens}";
            }
            catch (Exception ex)
            {
                Log.Error($"[LogTask] failed: {ex.GetType().Name}: {ex.Message}");
                return $"ERROR in LogTask: {ex.Message}";
            }
        });
    }

    private static void WriteFinalRow(ActiveTask t, string outcome, string notes, int tests, int covered, string extraNote)
    {
        var stores = StoreRegistry.ForNamespace(t.Namespace);
        var endedUtc = DateTime.UtcNow;

        // Compute task-scoped tool call stats by reading tool-calls.jsonl.
        var calls = stores.ToolCalls.LoadAll()
            .Where(c => c.TaskId == t.Id)
            .ToList();

        var record = new TaskRecord
        {
            Id = t.Id,
            SessionId = Telemetry.SessionId,
            Target = t.Target,
            Description = t.Description,
            StartedUtc = t.StartedUtc.ToString("O"),
            EndedUtc = endedUtc.ToString("O"),
            Outcome = outcome,
            ToolCalls = calls.Count,
            RepeatToolCalls = calls.Count(c => c.DedupeCount > 1),
            ResponseBytesServed = calls.Sum(c => c.ResponseBytes),
            TestsGenerated = tests,
            CoveredLines = covered,
            Notes = string.IsNullOrEmpty(extraNote) ? notes : (notes + " | " + extraNote).Trim(' ', '|')
        };
        stores.Tasks.Append(record);
    }

    internal static void ResetForTests()
    {
        lock (s_lock) s_current = null;
        Telemetry.ActiveTaskId = null;
    }
}
