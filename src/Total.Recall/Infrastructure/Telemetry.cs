using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Total.Recall.Models;

namespace Total.Recall.Infrastructure;

/// <summary>
/// Tool-call telemetry interceptor. Wraps every MCP tool body via
/// <see cref="Track(string, string?, object?, Func{string})"/> so we can record
/// timing, response size, dedupe-against-previous-call-in-session, and (Cut 3)
/// task attribution.
///
/// Behaviour:
///   - When <see cref="TelemetryConfig.IsRecording"/> is false, falls through to the
///     handler with no overhead and no recording.
///   - Otherwise, builds a <see cref="ToolCall"/> record and appends to
///     <c>tool-calls.jsonl</c> in the targeted namespace.
///   - Per-process <see cref="SessionId"/> regenerates on server restart — gives
///     us a natural session boundary without a separate handshake.
///   - Per-session dedupe map tracks how many times each <c>dedupeKey</c> has fired
///     so cycle detection (Cut 2) can read it from the JSONL without recomputing.
///
/// Failure mode: telemetry errors are logged and swallowed — the tool's response
/// is never blocked or altered by an instrumentation failure.
/// </summary>
public static class Telemetry
{
    /// <summary>Per-process session id. New on every server start.</summary>
    public static string SessionId { get; } = Guid.NewGuid().ToString("N");

    /// <summary>Active task id (set by Cut 3 task tools). Null when no task is open.</summary>
    public static string? ActiveTaskId { get; set; }

    // dedupeKey → count of times seen this session
    private static readonly ConcurrentDictionary<string, int> s_dedupe = new(StringComparer.Ordinal);
    // dedupeKey → last call id
    private static readonly ConcurrentDictionary<string, string> s_lastCallId = new(StringComparer.Ordinal);

    /// <summary>
    /// Wrap a tool body. Records the call to tool-calls.jsonl, returns whatever
    /// the handler returned (unmodified). Safe to call even when recording is off —
    /// adds < 10µs of overhead in that path.
    /// </summary>
    public static string Track(string toolName, string? ns, object? paramObject, Func<string> handler)
    {
        if (!TelemetryConfig.IsRecording)
        {
            return handler();
        }

        var sw = Stopwatch.StartNew();
        string response;
        bool error = false;
        try
        {
            response = handler();
            // Detect error responses produced by the tool's own try/catch.
            // The "ERROR in" convention is used by every tool body.
            error = response.StartsWith("ERROR ", StringComparison.Ordinal)
                 || response.StartsWith("ERROR in ", StringComparison.Ordinal);
        }
        catch
        {
            sw.Stop();
            // Best-effort error record before rethrowing so we don't swallow it.
            TryRecord(toolName, ns, paramObject, sw.ElapsedMilliseconds, "", 0, error: true);
            throw;
        }
        sw.Stop();

        TryRecord(toolName, ns, paramObject, sw.ElapsedMilliseconds, response, response.Length, error);
        return response;
    }

    private static void TryRecord(
        string toolName,
        string? ns,
        object? paramObject,
        long latencyMs,
        string response,
        long responseBytes,
        bool error)
    {
        try
        {
            var resolvedNs = string.IsNullOrWhiteSpace(ns) ? RepoConfig.GetDefaultNamespace() : ns.Trim();
            var paramSummary = SummarizeParams(paramObject);
            var paramHash = Hash(paramSummary);
            var dedupeKey = toolName + ":" + paramHash;

            var count = s_dedupe.AddOrUpdate(dedupeKey, 1, (_, v) => v + 1);
            s_lastCallId.TryGetValue(dedupeKey, out var prevId);

            var id = Guid.NewGuid().ToString("N");
            s_lastCallId[dedupeKey] = id;

            var record = new ToolCall
            {
                Id = id,
                SessionId = SessionId,
                TaskId = ActiveTaskId,
                Timestamp = DateTime.UtcNow.ToString("O"),
                ToolName = toolName,
                Namespace = resolvedNs,
                ParamHash = paramHash,
                ParamSummary = paramSummary,
                DedupeKey = dedupeKey,
                DedupeCount = count,
                RepeatOfId = count > 1 ? prevId : null,
                LatencyMs = latencyMs,
                ResponseBytes = responseBytes,
                ResponseHash = response.Length == 0 ? "" : Hash(response).Substring(0, 16),
                Error = error
            };

            StoreRegistry.ForNamespace(ns).ToolCalls.Append(record);
            Metrics.Increment(Metrics.ToolCallsRecorded);
            if (count > 1) Metrics.Increment(Metrics.ToolCallsRepeat);

            // Cut 2 — cycle detection runs synchronously off the recorded call.
            CycleDetector.Observe(record, ns);
        }
        catch (Exception ex)
        {
            // Never let telemetry break a tool.
            Log.Warn($"[Telemetry] record failed for {toolName}: {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>
    /// Build a stable, one-line, truncated summary of an anonymous param object.
    /// Reflection-based to keep call sites free of allocations and ceremony.
    /// </summary>
    internal static string SummarizeParams(object? paramObject)
    {
        if (paramObject is null) return "";
        var sb = new StringBuilder();
        var type = paramObject.GetType();
        var first = true;
        foreach (var prop in type.GetProperties())
        {
            var value = prop.GetValue(paramObject);
            if (value is null) continue;
            if (!first) sb.Append(", ");
            sb.Append(prop.Name).Append('=');
            var s = value.ToString() ?? "";
            if (s.Length > 60) s = s.Substring(0, 60) + "…";
            sb.Append(s);
            first = false;
            if (sb.Length > 200) break;
        }
        if (sb.Length > 200) sb.Length = 200;
        return sb.ToString();
    }

    internal static string Hash(string input)
    {
        if (string.IsNullOrEmpty(input)) return "0";
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        // 16 hex chars = 64 bits — plenty for in-session dedupe collisions.
        var sb = new StringBuilder(16);
        for (var i = 0; i < 8; i++) sb.Append(bytes[i].ToString("x2"));
        return sb.ToString();
    }

    /// <summary>Test-only: clear dedupe state between tests.</summary>
    internal static void ResetForTests()
    {
        s_dedupe.Clear();
        s_lastCallId.Clear();
        ActiveTaskId = null;
    }
}
