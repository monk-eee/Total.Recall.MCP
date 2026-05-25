using System.Text.Json.Serialization;

namespace Total.Recall.Models;

/// <summary>
/// One graded attempt at a <see cref="ChallengeRecord"/>. Append-only to evals.jsonl.
/// Drives the leaderboard and per-model scorecard.
/// </summary>
public sealed class EvalRecord
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("challengeId")]
    public string ChallengeId { get; set; } = "";

    [JsonPropertyName("model")]
    public string Model { get; set; } = "";

    [JsonPropertyName("sessionId")]
    public string SessionId { get; set; } = "";

    [JsonPropertyName("startedUtc")]
    public string StartedUtc { get; set; } = "";

    [JsonPropertyName("endedUtc")]
    public string EndedUtc { get; set; } = "";

    [JsonPropertyName("passed")]
    public bool Passed { get; set; }

    /// <summary>0.0–1.0 normalized score.</summary>
    [JsonPropertyName("score")]
    public double Score { get; set; }

    [JsonPropertyName("toolCallsObserved")]
    public int ToolCallsObserved { get; set; }

    [JsonPropertyName("tokensReported")]
    public long TokensReported { get; set; }

    [JsonPropertyName("expectedTools")]
    public List<string> ExpectedTools { get; set; } = [];

    [JsonPropertyName("actualTools")]
    public List<string> ActualTools { get; set; } = [];

    [JsonPropertyName("gradeBreakdown")]
    public Dictionary<string, double> GradeBreakdown { get; set; } = new();

    [JsonPropertyName("submission")]
    public string Submission { get; set; } = "";

    [JsonPropertyName("feedback")]
    public string Feedback { get; set; } = "";
}
