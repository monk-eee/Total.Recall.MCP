using System.Text.Json.Serialization;

namespace Total.Recall.Models;

/// <summary>
/// A method-level coverage target: one uncovered method within a class.
/// Flattened from CoverageGap.UncoveredMethods and cross-joined with test inventory.
/// Used by get_uncovered_methods for method-level targeting (v3).
/// </summary>
public sealed class UncoveredMethodTarget
{
    [JsonPropertyName("class")]
    public string Class { get; set; } = "";

    [JsonPropertyName("namespace")]
    public string Namespace { get; set; } = "";

    [JsonPropertyName("file")]
    public string File { get; set; } = "";

    [JsonPropertyName("method")]
    public string Method { get; set; } = "";

    [JsonPropertyName("uncoveredLines")]
    public int UncoveredLines { get; set; }

    [JsonPropertyName("startLine")]
    public int StartLine { get; set; }

    [JsonPropertyName("endLine")]
    public int EndLine { get; set; }

    /// <summary>Whether a test file already exists for this class (extending is cheaper than creating).</summary>
    [JsonPropertyName("hasTestFile")]
    public bool HasTestFile { get; set; }

    /// <summary>Paths to existing test files for this class.</summary>
    [JsonPropertyName("testFiles")]
    public List<string> TestFiles { get; set; } = [];

    /// <summary>Number of existing test methods for this class.</summary>
    [JsonPropertyName("existingTestCount")]
    public int ExistingTestCount { get; set; }

    /// <summary>
    /// Composite score: higher = better ROI.
    /// Methods in classes with existing test files score 2x (extending is cheaper than creating).
    /// </summary>
    [JsonPropertyName("score")]
    public double Score { get; set; }

    /// <summary>Human-readable reason for the score.</summary>
    [JsonPropertyName("reason")]
    public string Reason { get; set; } = "";
}
