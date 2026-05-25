using System.Text.Json.Serialization;

namespace Total.Recall.Models;

/// <summary>
/// A registered active-eval challenge. Loaded from challenges.jsonl. Agents pull
/// these via <c>get_next_challenge</c> and submit answers via <c>submit_challenge</c>.
/// </summary>
public sealed class ChallengeRecord
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    /// <summary>Free-form category — mocking | scaffolding | coverage-targeting | resolution | …</summary>
    [JsonPropertyName("category")]
    public string Category { get; set; } = "";

    [JsonPropertyName("prompt")]
    public string Prompt { get; set; } = "";

    [JsonPropertyName("expected")]
    public ChallengeExpectation Expected { get; set; } = new();

    [JsonPropertyName("maxTokens")]
    public int MaxTokens { get; set; } = 4000;

    [JsonPropertyName("tags")]
    public List<string> Tags { get; set; } = [];

    [JsonPropertyName("referenceNote")]
    public string ReferenceNote { get; set; } = "";
}

/// <summary>Grading rubric for a challenge — used by <see cref="Infrastructure.ChallengeGrader"/>.</summary>
public sealed class ChallengeExpectation
{
    [JsonPropertyName("mustCallTools")]
    public List<string> MustCallTools { get; set; } = [];

    [JsonPropertyName("mustNotCallTools")]
    public List<string> MustNotCallTools { get; set; } = [];

    [JsonPropertyName("maxToolCalls")]
    public int MaxToolCalls { get; set; } = 50;

    [JsonPropertyName("outputMustContain")]
    public List<string> OutputMustContain { get; set; } = [];

    [JsonPropertyName("outputMustNotContain")]
    public List<string> OutputMustNotContain { get; set; } = [];
}
