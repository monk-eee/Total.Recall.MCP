using System.Text.Json.Serialization;

namespace Total.Recall.Models;

/// <summary>
/// Coverage gap data for a single class, parsed from Cobertura XML.
/// One record per class in coverage-gaps.jsonl.
/// </summary>
public sealed class CoverageGap
{
    [JsonPropertyName("class")]
    public string Class { get; set; } = "";

    [JsonPropertyName("namespace")]
    public string Namespace { get; set; } = "";

    [JsonPropertyName("file")]
    public string File { get; set; } = "";

    [JsonPropertyName("totalLines")]
    public int TotalLines { get; set; }

    [JsonPropertyName("coveredLines")]
    public int CoveredLines { get; set; }

    [JsonPropertyName("uncoveredLines")]
    public int UncoveredLines { get; set; }

    [JsonPropertyName("coveragePercent")]
    public double CoveragePercent { get; set; }

    [JsonPropertyName("uncoveredMethods")]
    public List<UncoveredMethod> UncoveredMethods { get; set; } = [];

    [JsonPropertyName("existingTestCount")]
    public int ExistingTestCount { get; set; }

    [JsonPropertyName("testability")]
    public string Testability { get; set; } = "unknown";

    [JsonPropertyName("skipReason")]
    public string? SkipReason { get; set; }
}

public sealed class UncoveredMethod
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("startLine")]
    public int StartLine { get; set; }

    [JsonPropertyName("endLine")]
    public int EndLine { get; set; }

    [JsonPropertyName("uncoveredLines")]
    public int UncoveredLines { get; set; }
}
