using System.Text.Json.Serialization;

namespace Total.Recall.Models;

/// <summary>
/// A type-specific pitfall discovered during test generation.
/// One record per gotcha in gotchas.jsonl.
/// </summary>
public sealed class Gotcha
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "";

    [JsonPropertyName("category")]
    public string Category { get; set; } = "";

    [JsonPropertyName("gotcha")]
    public string Description { get; set; } = "";

    [JsonPropertyName("discoveredInGen")]
    public int? DiscoveredInGen { get; set; }

    [JsonPropertyName("date")]
    public string Date { get; set; } = "";
}
