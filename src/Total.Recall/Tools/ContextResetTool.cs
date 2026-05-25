using System.ComponentModel;
using ModelContextProtocol.Server;
using Total.Recall.Infrastructure;
using Total.Recall.Models;

namespace Total.Recall.Tools;

/// <summary>
/// Cut 6 — A first-class hook the agent (or its driver) can call when its context window
/// is reset/truncated. We write a CycleRecord with pattern="context-expiry" so the next
/// scorecard run can quantify how often the agent had to start over.
/// </summary>
[McpServerToolType]
public static class ContextResetTool
{
    [McpServerTool, Description(
        "Cut 6 — Tell Total.Recall that the agent's context just expired / was reset. " +
        "Records the event so context-expiry frequency shows up in the scorecard. " +
        "Call this at the START of a fresh session if you know the previous one ran out of context.")]
    public static string ReportContextReset(
        [Description("Optional: one-line note (e.g. 'after class N, hit token cap')")] string note = "",
        [Description("Optional: prior session id if known (for forensics)")] string? priorSessionId = null,
        [Description("Optional: namespace/session (default: server default)")] string? ns = null)
    {
        return Telemetry.Track("report_context_reset", ns, new { note, priorSessionId, ns }, () =>
        {
            try
            {
                var stores = StoreRegistry.ForNamespace(ns);
                var nowIso = DateTime.UtcNow.ToString("O");
                var record = new CycleRecord
                {
                    Id = Guid.NewGuid().ToString("N"),
                    SessionId = Telemetry.SessionId,
                    TaskId = Telemetry.ActiveTaskId,
                    Pattern = "context-expiry",
                    DedupeKey = $"context-expiry|{priorSessionId ?? "unknown"}",
                    ToolName = "(none)",
                    FirstSeenAt = nowIso,
                    LastSeenAt = nowIso,
                    Occurrences = 1,
                    WastedBytes = 0,
                    Evidence = new List<string> { priorSessionId is null ? "(no prior session id)" : $"prior={priorSessionId}" },
                    Note = string.IsNullOrWhiteSpace(note) ? "context reset reported" : note
                };
                stores.Cycles.Append(record);
                Metrics.Increment(Metrics.ContextResetsReported);
                return $"Recorded context-expiry event for session {Telemetry.SessionId}";
            }
            catch (Exception ex)
            {
                Log.Error($"[ReportContextReset] failed: {ex.GetType().Name}: {ex.Message}");
                return $"ERROR in ReportContextReset: {ex.Message}";
            }
        });
    }
}
