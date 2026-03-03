using System.Text.Json.Serialization;

namespace Total.Recall.Models;

/// <summary>
/// A record of an agent session: tokens spent, classes tested, coverage delta, learnings.
/// Append-only to sessions.jsonl. Enables cross-session learning and ROI measurement.
/// </summary>
public sealed class SessionRecord
{
    [JsonPropertyName("sessionId")]
    public string SessionId { get; set; } = "";

    [JsonPropertyName("startedUtc")]
    public string StartedUtc { get; set; } = "";

    [JsonPropertyName("endedUtc")]
    public string EndedUtc { get; set; } = "";

    [JsonPropertyName("model")]
    public string Model { get; set; } = "";

    [JsonPropertyName("promptTokens")]
    public long PromptTokens { get; set; }

    [JsonPropertyName("completionTokens")]
    public long CompletionTokens { get; set; }

    [JsonPropertyName("totalTokens")]
    public long TotalTokens { get; set; }

    [JsonPropertyName("classesAttempted")]
    public List<string> ClassesAttempted { get; set; } = [];

    [JsonPropertyName("classesSucceeded")]
    public List<string> ClassesSucceeded { get; set; } = [];

    [JsonPropertyName("classesFailed")]
    public List<SessionFailure> ClassesFailed { get; set; } = [];

    [JsonPropertyName("testsGenerated")]
    public int TestsGenerated { get; set; }

    [JsonPropertyName("coverageBefore")]
    public double CoverageBefore { get; set; }

    [JsonPropertyName("coverageAfter")]
    public double CoverageAfter { get; set; }

    [JsonPropertyName("coverageDelta")]
    public double CoverageDelta { get; set; }

    /// <summary>
    /// Actual lines of new code covered this session.
    /// Enables ROI tracking: linesPerTest = coveredLines / testsGenerated.
    /// </summary>
    [JsonPropertyName("coveredLines")]
    public int CoveredLines { get; set; }

    [JsonPropertyName("gotchasDiscovered")]
    public int GotchasDiscovered { get; set; }

    [JsonPropertyName("assessmentsRecorded")]
    public int AssessmentsRecorded { get; set; }

    [JsonPropertyName("notes")]
    public string Notes { get; set; } = "";
}

public sealed class SessionFailure
{
    [JsonPropertyName("class")]
    public string Class { get; set; } = "";

    [JsonPropertyName("reason")]
    public string Reason { get; set; } = "";
}
