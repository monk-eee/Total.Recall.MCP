using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using Total.Recall.Infrastructure;
using Total.Recall.Models;

namespace Total.Recall.Tools;

/// <summary>
/// Cut 4 — Aggregations over tool-calls.jsonl, tasks.jsonl, cycles.jsonl, sessions.jsonl.
/// No new writes; pure read tools that produce the artefacts an operator actually reads:
/// per-tool call stats, per-session efficiency report, per-model scorecard.
/// </summary>
[McpServerToolType]
public static class ScorecardTool
{
    [McpServerTool, Description(
        "Cut 4 — Per-tool aggregated stats: call count, avg/max latency, dedupe rate, " +
        "total response bytes served. Identifies which tools are hot and which are " +
        "burning context on repeat queries.")]
    public static string GetToolCallStats(
        [Description("Limit to current process session (default: false)")] bool currentSessionOnly = false,
        [Description("Optional: namespace/session (default: server default)")] string? ns = null)
    {
        return Telemetry.Track("get_tool_call_stats", ns, new { currentSessionOnly, ns }, () =>
        {
            try
            {
                var stores = StoreRegistry.ForNamespace(ns);
                var calls = stores.ToolCalls.LoadAll();
                if (currentSessionOnly) calls = calls.Where(c => c.SessionId == Telemetry.SessionId).ToList();
                if (calls.Count == 0) return "No tool calls recorded yet. Tool-call telemetry requires TOTAL_RECALL_MODE != off.";

                var grouped = calls.GroupBy(c => c.ToolName)
                    .Select(g => new
                    {
                        toolName = g.Key,
                        calls = g.Count(),
                        repeatCalls = g.Count(c => c.DedupeCount > 1),
                        dedupeRatePct = Math.Round(100.0 * g.Count(c => c.DedupeCount > 1) / g.Count(), 1),
                        errors = g.Count(c => c.Error),
                        avgLatencyMs = (long)g.Average(c => c.LatencyMs),
                        maxLatencyMs = g.Max(c => c.LatencyMs),
                        totalBytes = g.Sum(c => c.ResponseBytes),
                        avgBytes = (long)g.Average(c => c.ResponseBytes)
                    })
                    .OrderByDescending(x => x.calls)
                    .ToList();

                return JsonSerializer.Serialize(new
                {
                    totalCalls = calls.Count,
                    sessions = calls.Select(c => c.SessionId).Distinct().Count(),
                    tools = grouped
                }, SharedJsonOptions.CamelCaseIndented);
            }
            catch (Exception ex)
            {
                Log.Error($"[GetToolCallStats] failed: {ex.GetType().Name}: {ex.Message}");
                return $"ERROR in GetToolCallStats: {ex.Message}";
            }
        });
    }

    [McpServerTool, Description(
        "Cut 4 — Session-level efficiency report: response bytes served vs agent-reported " +
        "tokens, cycle count, dedupe ratio, wasted-bytes ratio. Run mid-session to spot " +
        "trouble; the ratio of repeated-call bytes to total bytes is the key signal.")]
    public static string GetEfficiencyReport(
        [Description("Limit to current process session (default: true)")] bool currentSessionOnly = true,
        [Description("Optional: namespace/session (default: server default)")] string? ns = null)
    {
        return Telemetry.Track("get_efficiency_report", ns, new { currentSessionOnly, ns }, () =>
        {
            try
            {
                var stores = StoreRegistry.ForNamespace(ns);
                var calls = stores.ToolCalls.LoadAll();
                if (currentSessionOnly) calls = calls.Where(c => c.SessionId == Telemetry.SessionId).ToList();
                if (calls.Count == 0) return "No tool calls recorded yet.";

                var totalBytes = calls.Sum(c => c.ResponseBytes);
                var repeatBytes = calls.Where(c => c.DedupeCount > 1).Sum(c => c.ResponseBytes);
                var cycles = stores.Cycles.LoadAll();
                if (currentSessionOnly) cycles = cycles.Where(c => c.SessionId == Telemetry.SessionId).ToList();

                var sessions = stores.Sessions.LoadAll();
                if (currentSessionOnly) sessions = sessions.Where(s => s.SessionId == Telemetry.SessionId).ToList();
                var reportedTokens = sessions.Sum(s => s.TotalTokens);

                return JsonSerializer.Serialize(new
                {
                    sessionId = currentSessionOnly ? Telemetry.SessionId : "(all sessions)",
                    toolCalls = calls.Count,
                    repeatCalls = calls.Count(c => c.DedupeCount > 1),
                    dedupeRatePct = Math.Round(100.0 * calls.Count(c => c.DedupeCount > 1) / calls.Count, 1),
                    totalResponseBytes = totalBytes,
                    repeatedResponseBytes = repeatBytes,
                    wastedBytesRatioPct = totalBytes == 0 ? 0 : Math.Round(100.0 * repeatBytes / totalBytes, 1),
                    cyclesDetected = cycles.Count,
                    cyclePatterns = cycles.GroupBy(c => c.Pattern).ToDictionary(g => g.Key, g => g.Count()),
                    agentReportedTokens = reportedTokens,
                    bytesPerReportedToken = reportedTokens == 0 ? 0 : Math.Round((double)totalBytes / reportedTokens, 2),
                    note = totalBytes > 0 && repeatBytes * 4 > totalBytes
                        ? "⚠ >25% of bytes served were repeats — agent is forgetting answers"
                        : "ratios within normal range"
                }, SharedJsonOptions.CamelCaseIndented);
            }
            catch (Exception ex)
            {
                Log.Error($"[GetEfficiencyReport] failed: {ex.GetType().Name}: {ex.Message}");
                return $"ERROR in GetEfficiencyReport: {ex.Message}";
            }
        });
    }

    [McpServerTool, Description(
        "Cut 4 — Per-model scorecard. For each model that has run sessions in this namespace: " +
        "sessions, tasks, tokens-per-covered-line, cycles-per-session, success rate, deferred rate, " +
        "wasted-bytes ratio, top failure mode. This is the artefact that answers " +
        "'which model is most efficient at this job'.")]
    public static string GetModelScorecard(
        [Description("Optional: namespace/session (default: server default)")] string? ns = null)
    {
        return Telemetry.Track("get_model_scorecard", ns, new { ns }, () =>
        {
            try
            {
                var stores = StoreRegistry.ForNamespace(ns);
                var sessions = stores.Sessions.LoadAll();
                if (sessions.Count == 0) return "No sessions logged yet. Call log_session at the end of a coverage run.";

                var cycles = stores.Cycles.LoadAll();
                var calls = stores.ToolCalls.LoadAll();

                var byModel = sessions
                    .Where(s => !string.IsNullOrEmpty(s.Model))
                    .GroupBy(s => s.Model)
                    .Select(g =>
                    {
                        var modelSessions = g.ToList();
                        var sessionIds = modelSessions.Select(s => s.SessionId).ToHashSet();
                        var modelCycles = cycles.Where(c => sessionIds.Contains(c.SessionId)).ToList();
                        var modelCalls = calls.Where(c => sessionIds.Contains(c.SessionId)).ToList();
                        var totalTokens = modelSessions.Sum(s => s.TotalTokens);
                        var totalCovered = modelSessions.Sum(s => (long)s.CoveredLines);
                        var totalTests = modelSessions.Sum(s => s.TestsGenerated);
                        return new
                        {
                            model = g.Key,
                            sessions = modelSessions.Count,
                            totalTokens,
                            totalCoveredLines = totalCovered,
                            totalTestsGenerated = totalTests,
                            tokensPerCoveredLine = totalCovered == 0 ? 0 : Math.Round((double)totalTokens / totalCovered, 0),
                            avgLinesPerTest = totalTests == 0 ? 0 : Math.Round((double)totalCovered / totalTests, 2),
                            cyclesPerSession = modelSessions.Count == 0 ? 0 : Math.Round((double)modelCycles.Count / modelSessions.Count, 2),
                            toolCallsPerSession = modelSessions.Count == 0 ? 0 : Math.Round((double)modelCalls.Count / modelSessions.Count, 1),
                            dedupeRatePct = modelCalls.Count == 0 ? 0 : Math.Round(100.0 * modelCalls.Count(c => c.DedupeCount > 1) / modelCalls.Count, 1),
                            topCyclePattern = modelCycles.GroupBy(c => c.Pattern).OrderByDescending(x => x.Count()).FirstOrDefault()?.Key ?? "(none)"
                        };
                    })
                    .OrderByDescending(x => x.totalCoveredLines)
                    .ToList();

                return JsonSerializer.Serialize(new { models = byModel }, SharedJsonOptions.CamelCaseIndented);
            }
            catch (Exception ex)
            {
                Log.Error($"[GetModelScorecard] failed: {ex.GetType().Name}: {ex.Message}");
                return $"ERROR in GetModelScorecard: {ex.Message}";
            }
        });
    }
}
