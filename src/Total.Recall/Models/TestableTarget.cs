using System.Text.Json.Serialization;

namespace Total.Recall.Models;

/// <summary>
/// A pre-scored, pre-filtered target for test generation.
/// Produced by cross-joining coverage gaps, type registry, test inventory, and assessments.
/// One instance per class in the ranked output.
/// </summary>
public sealed class TestableTarget
{
    [JsonPropertyName("class")]
    public string Class { get; set; } = "";

    [JsonPropertyName("namespace")]
    public string Namespace { get; set; } = "";

    [JsonPropertyName("file")]
    public string File { get; set; } = "";

    [JsonPropertyName("totalLines")]
    public int TotalLines { get; set; }

    [JsonPropertyName("uncoveredLines")]
    public int UncoveredLines { get; set; }

    [JsonPropertyName("coveragePercent")]
    public double CoveragePercent { get; set; }

    [JsonPropertyName("uncoveredMethodCount")]
    public int UncoveredMethodCount { get; set; }

    [JsonPropertyName("uncoveredMethods")]
    public List<string> UncoveredMethods { get; set; } = [];

    [JsonPropertyName("existingTestCount")]
    public int ExistingTestCount { get; set; }

    /// <summary>Number of constructor parameters (proxy for DI complexity).</summary>
    [JsonPropertyName("ctorParamCount")]
    public int CtorParamCount { get; set; }

    /// <summary>The constructor parameter types (e.g. "ILogger", "IRepoBase").</summary>
    [JsonPropertyName("ctorParams")]
    public List<string> CtorParams { get; set; } = [];

    /// <summary>How many ctor params are interfaces (mockable).</summary>
    [JsonPropertyName("mockableParamCount")]
    public int MockableParamCount { get; set; }

    /// <summary>How many ctor params have pre-built mock recipes.</summary>
    [JsonPropertyName("recipeCoveredParams")]
    public int RecipeCoveredParams { get; set; }

    [JsonPropertyName("baseType")]
    public string? BaseType { get; set; }

    [JsonPropertyName("isAbstract")]
    public bool IsAbstract { get; set; }

    [JsonPropertyName("isStatic")]
    public bool IsStatic { get; set; }

    /// <summary>Previous assessment verdict, if any.</summary>
    [JsonPropertyName("previousVerdict")]
    public string? PreviousVerdict { get; set; }

    /// <summary>Number of known gotchas for this type.</summary>
    [JsonPropertyName("gotchaCount")]
    public int GotchaCount { get; set; }

    /// <summary>
    /// Composite score: higher = better ROI for writing tests.
    /// Factors: uncovered lines, ctor simplicity, mockable params, existing test gaps.
    /// </summary>
    [JsonPropertyName("score")]
    public double Score { get; set; }

    /// <summary>Human-readable reason why this target ranks well/poorly.</summary>
    [JsonPropertyName("reason")]
    public string Reason { get; set; } = "";
}
