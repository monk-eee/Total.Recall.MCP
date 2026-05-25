using System.Text.Json.Serialization;

namespace Total.Recall.Models;

/// <summary>
/// A testability assessment for a class, produced during the coverage-uplift workflow.
/// One record per assessment in assessments.jsonl. Append-only — latest wins for a given class.
/// </summary>
public sealed class Assessment
{
    [JsonPropertyName("class")]
    public string Class { get; set; } = "";

    /// <summary>
    /// testable | coupled | skip | deferred
    /// </summary>
    [JsonPropertyName("verdict")]
    public string Verdict { get; set; } = "";

    [JsonPropertyName("reasoning")]
    public string Reasoning { get; set; } = "";

    /// <summary>
    /// Optional: key dependencies that drive the verdict.
    /// e.g. ["MyTestHarnessBase", "MyService.LoadFromFile"]
    /// </summary>
    [JsonPropertyName("dependencies")]
    public List<string> Dependencies { get; set; } = [];

    /// <summary>
    /// Optional: cluster name if the class was grouped with related types.
    /// e.g. "InvoiceSchema compositional graph"
    /// </summary>
    [JsonPropertyName("cluster")]
    public string? Cluster { get; set; }

    [JsonPropertyName("date")]
    public string Date { get; set; } = "";
}
