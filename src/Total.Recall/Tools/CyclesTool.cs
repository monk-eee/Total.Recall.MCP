using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using Total.Recall.Infrastructure;

namespace Total.Recall.Tools;

/// <summary>
/// Cut 2 — Read-side of cycle detection. Returns the cycle records that
/// <see cref="CycleDetector"/> has already written.
/// </summary>
[McpServerToolType]
public static class CyclesTool
{
    [McpServerTool, Description(
        "Cut 2 — List detected wasteful loops (re-query, context-loss, oscillation) " +
        "for the current session or a recent window. Use this when you notice the agent " +
        "feels stuck — it surfaces concrete evidence (which tool, how many times, how many bytes wasted).")]
    public static string GetCycles(
        [Description("Limit to current process session (default: true). Set false to see cross-session history.")] bool currentSessionOnly = true,
        [Description("Max results (default: 20)")] int top = 20,
        [Description("Optional: filter by pattern (re-query | context-loss | oscillation)")] string? pattern = null,
        [Description("Optional: namespace/session (default: server default)")] string? ns = null)
    {
        return Telemetry.Track("get_cycles", ns, new { currentSessionOnly, top, pattern, ns }, () =>
        {
            try
            {
                var stores = StoreRegistry.ForNamespace(ns);
                var all = stores.Cycles.LoadAll();
                IEnumerable<Models.CycleRecord> q = all;
                if (currentSessionOnly) q = q.Where(c => c.SessionId == Telemetry.SessionId);
                if (!string.IsNullOrWhiteSpace(pattern)) q = q.Where(c => c.Pattern == pattern);
                var rows = q.OrderByDescending(c => c.LastSeenAt).Take(Math.Max(1, top)).ToList();
                return JsonSerializer.Serialize(new
                {
                    sessionId = Telemetry.SessionId,
                    totalCycles = all.Count,
                    returned = rows.Count,
                    cycles = rows
                }, SharedJsonOptions.CamelCaseIndented);
            }
            catch (Exception ex)
            {
                Log.Error($"[GetCycles] failed: {ex.GetType().Name}: {ex.Message}");
                return $"ERROR in GetCycles: {ex.Message}";
            }
        });
    }
}
