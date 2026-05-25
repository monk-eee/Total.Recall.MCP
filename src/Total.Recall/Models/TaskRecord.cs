using System.Text.Json.Serialization;

namespace Total.Recall.Models;

/// <summary>
/// A logical unit of work below a session — typically "test class X" or "investigate Y".
/// Bracketed by <c>start_task</c> / <c>end_task</c> tool calls. While a task is active,
/// every <see cref="ToolCall"/> is stamped with its id.
/// Append-only to tasks.jsonl. End writes a final row; start writes nothing on its own
/// (we want one row = one completed task).
/// </summary>
public sealed class TaskRecord
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("sessionId")]
    public string SessionId { get; set; } = "";

    [JsonPropertyName("target")]
    public string Target { get; set; } = "";

    [JsonPropertyName("description")]
    public string Description { get; set; } = "";

    [JsonPropertyName("startedUtc")]
    public string StartedUtc { get; set; } = "";

    [JsonPropertyName("endedUtc")]
    public string EndedUtc { get; set; } = "";

    /// <summary>success | fail | abandoned</summary>
    [JsonPropertyName("outcome")]
    public string Outcome { get; set; } = "";

    [JsonPropertyName("toolCalls")]
    public int ToolCalls { get; set; }

    [JsonPropertyName("repeatToolCalls")]
    public int RepeatToolCalls { get; set; }

    [JsonPropertyName("inputTokens")]
    public long InputTokens { get; set; }

    [JsonPropertyName("outputTokens")]
    public long OutputTokens { get; set; }

    [JsonPropertyName("responseBytesServed")]
    public long ResponseBytesServed { get; set; }

    [JsonPropertyName("testsGenerated")]
    public int TestsGenerated { get; set; }

    [JsonPropertyName("coveredLines")]
    public int CoveredLines { get; set; }

    [JsonPropertyName("notes")]
    public string Notes { get; set; } = "";
}
