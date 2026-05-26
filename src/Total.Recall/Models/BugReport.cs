using System.Text.Json.Serialization;

namespace Total.Recall.Models;

/// <summary>
/// A class-scoped bug report filed by an agent (or human) during test generation
/// or code review. One record per state transition in bugs.jsonl — append-only,
/// latest record per <see cref="Id"/> wins (mirrors the assessments deduplication
/// convention).
///
/// Bugs differ from <see cref="Gotcha"/> in intent: a gotcha is "this code is
/// hard or surprising to test in this way", a bug is "this code is broken and
/// needs to be fixed". Bugs surface alongside gotchas in <c>get_context</c> so
/// future sessions see known-broken behaviour before authoring tests for it.
/// </summary>
public sealed class BugReport
{
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; set; } = 1;

    /// <summary>
    /// Stable identifier of the form <c>bug-{12-hex-chars}</c>. Multiple records
    /// can share an id (status transitions are appended, not mutated); the last
    /// record for an id wins.
    /// </summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    /// <summary>Class the bug applies to. Required.</summary>
    [JsonPropertyName("class")]
    public string Class { get; set; } = "";

    /// <summary>Method name if the bug is method-scoped. Optional.</summary>
    [JsonPropertyName("method")]
    public string? Method { get; set; }

    /// <summary>One of <c>low|medium|high|critical</c>.</summary>
    [JsonPropertyName("severity")]
    public string Severity { get; set; } = "medium";

    /// <summary>Short human description of the broken behaviour.</summary>
    [JsonPropertyName("description")]
    public string Description { get; set; } = "";

    /// <summary>Optional code snippet or steps to reproduce.</summary>
    [JsonPropertyName("repro")]
    public string? Repro { get; set; }

    /// <summary>Optional test name that surfaced the bug.</summary>
    [JsonPropertyName("foundInTestName")]
    public string? FoundInTestName { get; set; }

    /// <summary>One of <c>open|triaged|fixed|wontfix</c>. Default <c>open</c>.</summary>
    [JsonPropertyName("status")]
    public string Status { get; set; } = "open";

    /// <summary>Notes attached to the most recent status transition. Optional.</summary>
    [JsonPropertyName("statusNotes")]
    public string? StatusNotes { get; set; }

    /// <summary>Reporting model identifier (e.g. <c>claude-opus-4.7</c>). Optional.</summary>
    [JsonPropertyName("model")]
    public string? Model { get; set; }

    /// <summary>Telemetry session id captured at write time.</summary>
    [JsonPropertyName("sessionId")]
    public string? SessionId { get; set; }

    /// <summary>Active task id (from <c>start_task</c>) if any.</summary>
    [JsonPropertyName("taskId")]
    public string? TaskId { get; set; }

    /// <summary>UTC ISO-8601 timestamp of the first record for this <see cref="Id"/>.</summary>
    [JsonPropertyName("createdAt")]
    public string CreatedAt { get; set; } = "";

    /// <summary>UTC ISO-8601 timestamp of this record.</summary>
    [JsonPropertyName("updatedAt")]
    public string UpdatedAt { get; set; } = "";
}
