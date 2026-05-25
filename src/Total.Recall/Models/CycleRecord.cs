using System.Text.Json.Serialization;

namespace Total.Recall.Models;

/// <summary>
/// A detected wasteful loop in an agent session. Persisted to cycles.jsonl by the
/// <see cref="Infrastructure.CycleDetector"/> when it spots:
/// <list type="bullet">
///   <item><c>re-query</c> — same dedupeKey called ≥N times in a session with no intervening write</item>
///   <item><c>oscillation</c> — testable_targets repeatedly interleaved with snippets for different classes (agent can't commit)</item>
///   <item><c>re-attempt</c> — same class fails across multiple sessions (cross-session)</item>
///   <item><c>context-loss</c> — resolve_type repeats with no write to the type between calls</item>
/// </list>
/// </summary>
public sealed class CycleRecord
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("sessionId")]
    public string SessionId { get; set; } = "";

    [JsonPropertyName("taskId")]
    public string? TaskId { get; set; }

    [JsonPropertyName("pattern")]
    public string Pattern { get; set; } = "";

    [JsonPropertyName("dedupeKey")]
    public string DedupeKey { get; set; } = "";

    [JsonPropertyName("toolName")]
    public string ToolName { get; set; } = "";

    [JsonPropertyName("firstSeenAt")]
    public string FirstSeenAt { get; set; } = "";

    [JsonPropertyName("lastSeenAt")]
    public string LastSeenAt { get; set; } = "";

    [JsonPropertyName("occurrences")]
    public int Occurrences { get; set; }

    [JsonPropertyName("wastedBytes")]
    public long WastedBytes { get; set; }

    /// <summary>Tool-call ids that evidence the cycle.</summary>
    [JsonPropertyName("evidence")]
    public List<string> Evidence { get; set; } = [];

    [JsonPropertyName("note")]
    public string Note { get; set; } = "";
}
