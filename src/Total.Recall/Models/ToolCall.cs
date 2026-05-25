using System.Text.Json.Serialization;

namespace Total.Recall.Models;

/// <summary>
/// A single MCP tool invocation captured by the telemetry interceptor.
/// Append-only to tool-calls.jsonl. Drives cycle detection, scorecards, and
/// efficiency reports. Pure observability — never read by the tools themselves
/// during their own execution (avoids feedback loops).
/// </summary>
public sealed class ToolCall
{
    /// <summary>Unique call id (Guid).</summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    /// <summary>Process-scoped session id (regenerated on server restart).</summary>
    [JsonPropertyName("sessionId")]
    public string SessionId { get; set; } = "";

    /// <summary>Optional active task id (Cut 3).</summary>
    [JsonPropertyName("taskId")]
    public string? TaskId { get; set; }

    /// <summary>UTC ISO-8601 timestamp.</summary>
    [JsonPropertyName("timestamp")]
    public string Timestamp { get; set; } = "";

    /// <summary>Tool name (e.g. "get_gotchas").</summary>
    [JsonPropertyName("toolName")]
    public string ToolName { get; set; } = "";

    /// <summary>Namespace the tool targeted.</summary>
    [JsonPropertyName("ns")]
    public string Namespace { get; set; } = "";

    /// <summary>Deterministic hash of the input parameters. Used for dedupe detection.</summary>
    [JsonPropertyName("paramHash")]
    public string ParamHash { get; set; } = "";

    /// <summary>Human-readable one-line param summary (truncated to ~200 chars).</summary>
    [JsonPropertyName("paramSummary")]
    public string ParamSummary { get; set; } = "";

    /// <summary>Composite key for dedupe: toolName + ":" + paramHash.</summary>
    [JsonPropertyName("dedupeKey")]
    public string DedupeKey { get; set; } = "";

    /// <summary>How many times this dedupeKey has been called in the current session (1 = first call).</summary>
    [JsonPropertyName("dedupeCount")]
    public int DedupeCount { get; set; }

    /// <summary>Id of the previous identical call in this session, if any.</summary>
    [JsonPropertyName("repeatOfId")]
    public string? RepeatOfId { get; set; }

    /// <summary>Wall-clock milliseconds for the tool to produce its response.</summary>
    [JsonPropertyName("latencyMs")]
    public long LatencyMs { get; set; }

    /// <summary>Size of the response payload in bytes (proxy for context spend).</summary>
    [JsonPropertyName("responseBytes")]
    public long ResponseBytes { get; set; }

    /// <summary>Hash of the response body (lets us detect identical responses).</summary>
    [JsonPropertyName("responseHash")]
    public string ResponseHash { get; set; } = "";

    /// <summary>True if the tool returned an ERROR / exception path.</summary>
    [JsonPropertyName("error")]
    public bool Error { get; set; }
}
