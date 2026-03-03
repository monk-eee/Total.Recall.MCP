using System.Text.Json.Serialization;

namespace Total.Recall.Models;

/// <summary>
/// A zero-or-near-zero coverage class that is trivially testable:
/// minimal constructor params, mostly properties/stubs, no complex mocking.
/// These are the highest-ROI targets when class-level scores are low.
/// Used by get_stub_classes (v3).
/// </summary>
public sealed class StubClassTarget
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

    /// <summary>Number of methods that are not property accessors or constructors.</summary>
    [JsonPropertyName("realMethodCount")]
    public int RealMethodCount { get; set; }

    /// <summary>Number of property-accessor and constructor methods (boilerplate).</summary>
    [JsonPropertyName("boilerplateMethodCount")]
    public int BoilerplateMethodCount { get; set; }

    /// <summary>Fewest constructor parameters across all constructors (0 = parameterless ctor).</summary>
    [JsonPropertyName("minCtorParams")]
    public int MinCtorParams { get; set; }

    /// <summary>Whether all constructor params are interfaces (mockable).</summary>
    [JsonPropertyName("allParamsMockable")]
    public bool AllParamsMockable { get; set; }

    /// <summary>Whether a test file already exists for this class.</summary>
    [JsonPropertyName("hasTestFile")]
    public bool HasTestFile { get; set; }

    /// <summary>Paths to existing test files for this class.</summary>
    [JsonPropertyName("testFiles")]
    public List<string> TestFiles { get; set; } = [];

    /// <summary>Number of existing test methods for this class.</summary>
    [JsonPropertyName("existingTestCount")]
    public int ExistingTestCount { get; set; }

    /// <summary>What kind of stub this is: "poco", "static-helpers", "enum-like", "simple-logic".</summary>
    [JsonPropertyName("category")]
    public string Category { get; set; } = "";

    /// <summary>
    /// Composite score: higher = easier to test and more lines to cover.
    /// Zero-coupling classes with many uncovered lines score highest.
    /// </summary>
    [JsonPropertyName("score")]
    public double Score { get; set; }

    /// <summary>Human-readable reason for the score.</summary>
    [JsonPropertyName("reason")]
    public string Reason { get; set; } = "";
}
