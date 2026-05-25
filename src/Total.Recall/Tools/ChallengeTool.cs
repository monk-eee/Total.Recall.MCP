using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using Total.Recall.Infrastructure;
using Total.Recall.Models;

namespace Total.Recall.Tools;

/// <summary>
/// Cut 5 — Active eval tools. Discoverable only when TOTAL_RECALL_MODE=active-eval,
/// but the MCP SDK doesn't support runtime tool gating in 0.3.0-preview — so the
/// tools always exist, and they short-circuit with a clear message when mode != active-eval.
/// This is intentional: agents that try to use them out-of-mode get an explanation.
/// </summary>
[McpServerToolType]
public static class ChallengeTool
{
    [McpServerTool, Description(
        "Cut 5 (active-eval mode) — Return the next un-passed challenge for the current model. " +
        "Challenges test specific Total.Recall workflows (mocking, scaffolding, target selection). " +
        "Requires TOTAL_RECALL_MODE=active-eval.")]
    public static string GetNextChallenge(
        [Description("Model name (e.g. 'claude-opus-4.7') — required for leaderboard tracking")] string model,
        [Description("Optional category filter (mocking | scaffolding | resolution | coverage-targeting)")] string? category = null,
        [Description("Optional: namespace/session (default: server default)")] string? ns = null)
    {
        return Telemetry.Track("get_next_challenge", ns, new { model, category, ns }, () =>
        {
            if (!TelemetryConfig.IsActiveEval)
                return "Active-eval tools are inactive. Set TOTAL_RECALL_MODE=active-eval to enable.";

            try
            {
                var stores = StoreRegistry.ForNamespace(ns);
                var challenges = stores.Challenges.LoadAll();
                if (challenges.Count == 0)
                    return "No challenges registered yet. Seed challenges.jsonl first.";

                var evals = stores.Evals.LoadAll();
                var passedIds = evals
                    .Where(e => e.Model == model && e.Passed)
                    .Select(e => e.ChallengeId)
                    .ToHashSet(StringComparer.Ordinal);

                IEnumerable<ChallengeRecord> q = challenges;
                if (!string.IsNullOrWhiteSpace(category)) q = q.Where(c => c.Category == category);
                var next = q.FirstOrDefault(c => !passedIds.Contains(c.Id));

                if (next is null) return $"Model '{model}' has passed all available challenges{(string.IsNullOrWhiteSpace(category) ? "" : $" in category '{category}'")}.";

                return JsonSerializer.Serialize(new
                {
                    challengeId = next.Id,
                    category = next.Category,
                    prompt = next.Prompt,
                    maxToolCalls = next.Expected.MaxToolCalls,
                    maxTokens = next.MaxTokens,
                    tags = next.Tags,
                    hint = "Call submit_challenge(challengeId, model, submission) when done."
                }, SharedJsonOptions.CamelCaseIndented);
            }
            catch (Exception ex)
            {
                Log.Error($"[GetNextChallenge] failed: {ex.GetType().Name}: {ex.Message}");
                return $"ERROR in GetNextChallenge: {ex.Message}";
            }
        });
    }

    [McpServerTool, Description(
        "Cut 5 (active-eval mode) — Submit a solution to a challenge. Grades against the " +
        "rubric (required tools called, budget respected, output content checks) and writes " +
        "an evals.jsonl row. Returns the grade breakdown.")]
    public static string SubmitChallenge(
        [Description("Challenge id (from get_next_challenge)")] string challengeId,
        [Description("Model name — must match the one passed to get_next_challenge")] string model,
        [Description("The submission: typically the generated test code or analysis output")] string submission,
        [Description("Optional: namespace/session (default: server default)")] string? ns = null)
    {
        return Telemetry.Track("submit_challenge", ns, new { challengeId, model, ns }, () =>
        {
            if (!TelemetryConfig.IsActiveEval)
                return "Active-eval tools are inactive. Set TOTAL_RECALL_MODE=active-eval to enable.";

            try
            {
                var stores = StoreRegistry.ForNamespace(ns);
                var challenge = stores.Challenges.LoadAll().FirstOrDefault(c => c.Id == challengeId);
                if (challenge is null) return $"Challenge '{challengeId}' not found.";

                // Use this session's tool calls for grading.
                var sessionCalls = stores.ToolCalls.LoadAll()
                    .Where(c => c.SessionId == Telemetry.SessionId)
                    .ToList();

                var grade = ChallengeGrader.Grade(challenge, submission, sessionCalls);

                var record = new EvalRecord
                {
                    Id = Guid.NewGuid().ToString("N"),
                    ChallengeId = challengeId,
                    Model = model,
                    SessionId = Telemetry.SessionId,
                    StartedUtc = sessionCalls.FirstOrDefault()?.Timestamp ?? DateTime.UtcNow.ToString("O"),
                    EndedUtc = DateTime.UtcNow.ToString("O"),
                    Passed = grade.Passed,
                    Score = grade.Score,
                    ToolCallsObserved = sessionCalls.Count,
                    ExpectedTools = challenge.Expected.MustCallTools,
                    ActualTools = grade.ActualTools,
                    GradeBreakdown = grade.Breakdown,
                    Submission = submission.Length > 4000 ? submission[..4000] + "…(truncated)" : submission,
                    Feedback = grade.Feedback
                };
                stores.Evals.Append(record);
                Metrics.Increment(Metrics.ChallengesGraded);

                return JsonSerializer.Serialize(new
                {
                    challengeId,
                    passed = grade.Passed,
                    score = grade.Score,
                    breakdown = grade.Breakdown,
                    feedback = grade.Feedback
                }, SharedJsonOptions.CamelCaseIndented);
            }
            catch (Exception ex)
            {
                Log.Error($"[SubmitChallenge] failed: {ex.GetType().Name}: {ex.Message}");
                return $"ERROR in SubmitChallenge: {ex.Message}";
            }
        });
    }

    [McpServerTool, Description(
        "Cut 5 — Per-model leaderboard across all challenges. Pass rate, avg score, " +
        "avg tool calls per challenge.")]
    public static string GetEvalLeaderboard(
        [Description("Optional: namespace/session (default: server default)")] string? ns = null)
    {
        return Telemetry.Track("get_eval_leaderboard", ns, new { ns }, () =>
        {
            try
            {
                var stores = StoreRegistry.ForNamespace(ns);
                var evals = stores.Evals.LoadAll();
                if (evals.Count == 0) return "No challenge submissions yet.";

                var board = evals.GroupBy(e => e.Model).Select(g => new
                {
                    model = g.Key,
                    attempts = g.Count(),
                    passed = g.Count(e => e.Passed),
                    passRatePct = Math.Round(100.0 * g.Count(e => e.Passed) / g.Count(), 1),
                    avgScore = Math.Round(g.Average(e => e.Score), 3),
                    avgToolCalls = Math.Round(g.Average(e => e.ToolCallsObserved), 1),
                    distinctChallenges = g.Select(e => e.ChallengeId).Distinct().Count()
                })
                .OrderByDescending(x => x.passed)
                .ThenByDescending(x => x.avgScore)
                .ToList();
                return JsonSerializer.Serialize(new { leaderboard = board }, SharedJsonOptions.CamelCaseIndented);
            }
            catch (Exception ex)
            {
                Log.Error($"[GetEvalLeaderboard] failed: {ex.GetType().Name}: {ex.Message}");
                return $"ERROR in GetEvalLeaderboard: {ex.Message}";
            }
        });
    }
}
