using Total.Recall.Models;

namespace Total.Recall.Infrastructure;

/// <summary>
/// Detects four wasteful patterns in agent behaviour and writes to <c>cycles.jsonl</c>.
///
/// <list type="bullet">
///   <item><c>re-query</c>: same dedupeKey called ≥3× in this session with no intervening
///         write to its target. Means the agent forgot an answer it already had.</item>
///   <item><c>context-loss</c>: <c>resolve_type</c>/<c>get_context</c> on the same type
///         called ≥2× with no <c>add_gotcha</c>/<c>add_assessment</c>/<c>generate_test_scaffold</c>
///         for that type in between. Strong signal of a context-window roll.</item>
///   <item><c>oscillation</c>: 3+ different targets visited via <c>get_source_snippet</c>
///         in a 5-call window without an <c>add_assessment</c> in between. The agent
///         can't commit to a target.</item>
///   <item><c>re-attempt</c>: cross-session. Detected by <c>SessionTool</c> at log time,
///         not here.</item>
/// </list>
///
/// One cycle row per (sessionId, dedupeKey, pattern) — repeat detections update the
/// last-seen counters in-memory but only fire once per session to avoid flooding
/// the store.
/// </summary>
public static class CycleDetector
{
    public const int ReQueryThreshold = 3;
    public const int ContextLossThreshold = 2;
    public const int OscillationWindow = 5;
    public const int OscillationDistinctTargets = 3;

    // sessionId|dedupeKey|pattern → already-fired this session
    private static readonly HashSet<string> s_fired = [];
    private static readonly object s_lock = new();

    /// <summary>
    /// Called by <see cref="Telemetry"/> after every recorded tool call.
    /// Cheap: O(1) checks against the in-memory dedupe count + a single tail-scan
    /// of <c>tool-calls.jsonl</c> when a threshold is breached.
    /// </summary>
    public static void Observe(ToolCall call, string? ns)
    {
        try
        {
            if (call.DedupeCount >= ReQueryThreshold)
            {
                FireOnce(call, ns, "re-query",
                    $"{call.ToolName} called {call.DedupeCount}× with identical params in this session");
            }

            if (IsLookupTool(call.ToolName) && call.DedupeCount >= ContextLossThreshold)
            {
                if (NoWriteSinceLastIdenticalCall(call, ns))
                {
                    FireOnce(call, ns, "context-loss",
                        $"{call.ToolName} repeated with no add_gotcha/add_assessment/generate_test_scaffold for the target in between — likely context reset");
                }
            }

            if (call.ToolName == "get_source_snippet")
            {
                if (DetectOscillation(call, ns))
                {
                    FireOnce(call, ns, "oscillation",
                        $"≥{OscillationDistinctTargets} distinct snippet targets in last {OscillationWindow} calls without an add_assessment — agent is not committing to a target");
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"[CycleDetector] observe failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static bool IsLookupTool(string tool) =>
        tool is "resolve_type" or "get_context" or "get_gotchas" or "get_mock_recipe" or "get_coverage_gaps";

    private static void FireOnce(ToolCall call, string? ns, string pattern, string note)
    {
        var key = $"{call.SessionId}|{call.DedupeKey}|{pattern}";
        lock (s_lock)
        {
            if (!s_fired.Add(key)) return;
        }

        var stores = StoreRegistry.ForNamespace(ns);
        var record = new CycleRecord
        {
            Id = Guid.NewGuid().ToString("N"),
            SessionId = call.SessionId,
            TaskId = call.TaskId,
            Pattern = pattern,
            DedupeKey = call.DedupeKey,
            ToolName = call.ToolName,
            FirstSeenAt = call.Timestamp,
            LastSeenAt = call.Timestamp,
            Occurrences = call.DedupeCount,
            WastedBytes = call.ResponseBytes * Math.Max(0, call.DedupeCount - 1),
            Evidence = [call.Id, call.RepeatOfId ?? ""],
            Note = note
        };
        stores.Cycles.Append(record);
        Metrics.Increment(Metrics.CyclesDetected);
        Log.Info($"[CycleDetector] {pattern} detected: {note}");
    }

    /// <summary>
    /// Returns true if no write tool (add_gotcha / add_assessment / generate_test_scaffold)
    /// mentioning this call's params has been recorded since the previous identical call.
    /// </summary>
    internal static bool NoWriteSinceLastIdenticalCall(ToolCall call, string? ns)
    {
        if (string.IsNullOrEmpty(call.RepeatOfId)) return false;

        var stores = StoreRegistry.ForNamespace(ns);
        var all = stores.ToolCalls.LoadAll();
        var prevIdx = -1;
        for (var i = all.Count - 1; i >= 0; i--)
        {
            if (all[i].Id == call.RepeatOfId) { prevIdx = i; break; }
        }
        if (prevIdx < 0) return false;

        for (var i = prevIdx + 1; i < all.Count; i++)
        {
            var c = all[i];
            if (c.SessionId != call.SessionId) continue;
            if (c.ToolName is "add_gotcha" or "add_assessment" or "generate_test_scaffold" or "log_session" or "log_task")
                return false;
        }
        return true;
    }

    /// <summary>
    /// Look back over the last OscillationWindow get_source_snippet calls in this session.
    /// If they touch ≥OscillationDistinctTargets distinct classes and no add_assessment
    /// appears in between, the agent is oscillating.
    /// </summary>
    internal static bool DetectOscillation(ToolCall call, string? ns)
    {
        var stores = StoreRegistry.ForNamespace(ns);
        var all = stores.ToolCalls.LoadAll();
        var sessionCalls = all
            .Where(c => c.SessionId == call.SessionId)
            .Reverse()
            .Take(OscillationWindow * 3) // scan a slightly wider window for interspersed assessments
            .Reverse()
            .ToList();

        var snippetCalls = sessionCalls
            .Where(c => c.ToolName == "get_source_snippet")
            .TakeLast(OscillationWindow)
            .ToList();
        if (snippetCalls.Count < OscillationDistinctTargets) return false;

        // Look for add_assessment between first and last snippet call
        var first = snippetCalls[0];
        var last = snippetCalls[^1];
        var firstIdx = sessionCalls.FindIndex(c => c.Id == first.Id);
        var lastIdx = sessionCalls.FindIndex(c => c.Id == last.Id);
        if (firstIdx < 0 || lastIdx < 0) return false;

        for (var i = firstIdx + 1; i < lastIdx; i++)
        {
            if (sessionCalls[i].ToolName == "add_assessment") return false;
        }

        var distinct = snippetCalls.Select(c => c.ParamHash).Distinct().Count();
        return distinct >= OscillationDistinctTargets;
    }

    internal static void ResetForTests()
    {
        lock (s_lock) s_fired.Clear();
    }
}
