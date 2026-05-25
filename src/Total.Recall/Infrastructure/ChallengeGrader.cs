using Total.Recall.Models;

namespace Total.Recall.Infrastructure;

/// <summary>
/// Cut 5 — Grades a submission against a <see cref="ChallengeRecord"/>. Pure,
/// deterministic. Reads the agent's tool-call history for the session to verify
/// expected-tool/forbidden-tool rules and tool-call budget.
/// </summary>
public static class ChallengeGrader
{
    public sealed record GradeResult(
        bool Passed,
        double Score,
        List<string> ActualTools,
        Dictionary<string, double> Breakdown,
        string Feedback);

    /// <summary>
    /// Grade a submission. Score is a weighted sum:
    ///   0.4 calledRequiredTools (none missing)
    ///   0.2 stayedUnderBudget (and avoided forbidden tools)
    ///   0.4 outputCorrectness (must-contain ∧ must-not-contain)
    /// Passed = score >= 0.7.
    /// </summary>
    public static GradeResult Grade(ChallengeRecord challenge, string submission, IReadOnlyList<ToolCall> sessionCalls)
    {
        var feedback = new List<string>();
        var breakdown = new Dictionary<string, double>();

        var actualTools = sessionCalls.Select(c => c.ToolName).Distinct(StringComparer.Ordinal).ToList();

        // ── 1. Required tools ──
        var requiredMissing = challenge.Expected.MustCallTools
            .Where(t => !actualTools.Contains(t, StringComparer.Ordinal))
            .ToList();
        var requiredScore = challenge.Expected.MustCallTools.Count == 0
            ? 1.0
            : 1.0 - ((double)requiredMissing.Count / challenge.Expected.MustCallTools.Count);
        breakdown["calledRequiredTools"] = requiredScore;
        if (requiredMissing.Count > 0)
            feedback.Add($"missing required tools: {string.Join(", ", requiredMissing)}");

        // ── 2. Budget + forbidden ──
        var forbiddenHit = challenge.Expected.MustNotCallTools
            .Where(t => actualTools.Contains(t, StringComparer.Ordinal))
            .ToList();
        var underBudget = sessionCalls.Count <= challenge.Expected.MaxToolCalls;
        var budgetScore = (underBudget ? 0.5 : 0.0) + (forbiddenHit.Count == 0 ? 0.5 : 0.0);
        breakdown["stayedUnderBudget"] = budgetScore;
        if (!underBudget)
            feedback.Add($"exceeded tool-call budget ({sessionCalls.Count}/{challenge.Expected.MaxToolCalls})");
        if (forbiddenHit.Count > 0)
            feedback.Add($"called forbidden tools: {string.Join(", ", forbiddenHit)}");

        // ── 3. Output correctness ──
        var missContain = challenge.Expected.OutputMustContain
            .Where(s => submission.IndexOf(s, StringComparison.Ordinal) < 0)
            .ToList();
        var hitForbid = challenge.Expected.OutputMustNotContain
            .Where(s => submission.IndexOf(s, StringComparison.Ordinal) >= 0)
            .ToList();
        var totalChecks = challenge.Expected.OutputMustContain.Count + challenge.Expected.OutputMustNotContain.Count;
        var failedChecks = missContain.Count + hitForbid.Count;
        var outputScore = totalChecks == 0 ? 1.0 : 1.0 - ((double)failedChecks / totalChecks);
        breakdown["outputCorrectness"] = outputScore;
        if (missContain.Count > 0) feedback.Add($"output missing: {string.Join(", ", missContain)}");
        if (hitForbid.Count > 0) feedback.Add($"output contains forbidden: {string.Join(", ", hitForbid)}");

        var score = (requiredScore * 0.4) + (budgetScore * 0.2) + (outputScore * 0.4);
        var passed = score >= 0.7;
        if (feedback.Count == 0) feedback.Add("all checks passed");

        return new GradeResult(passed, Math.Round(score, 3), actualTools, breakdown, string.Join("; ", feedback));
    }
}
