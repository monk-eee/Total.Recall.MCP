using System.Text.Json.Serialization;

namespace Total.Recall.Models;

/// <summary>
/// Learned test patterns from existing test files in the project.
/// Captures naming conventions, helper methods, assertion styles, and common usings.
/// Cached per namespace — rebuilt when test-inventory.jsonl changes.
/// </summary>
public sealed class TestPatterns
{
    /// <summary>
    /// Most common helper method names found across test files
    /// (e.g., "CreateMockAuditable", "MakeRange", "CreateSut").
    /// </summary>
    [JsonPropertyName("helperMethods")]
    public List<TestHelperMethod> HelperMethods { get; set; } = [];

    /// <summary>
    /// Assertion style used in the project (e.g., "Assert.Equal", "FluentAssertions").
    /// </summary>
    [JsonPropertyName("assertionStyle")]
    public string AssertionStyle { get; set; } = "xUnit.Assert";

    /// <summary>
    /// Most common test method naming pattern (e.g., "MethodName_Scenario_Expected").
    /// </summary>
    [JsonPropertyName("namingPattern")]
    public string NamingPattern { get; set; } = "MethodName_Scenario_Expected";

    /// <summary>
    /// Common usings found in existing test files, beyond framework defaults.
    /// </summary>
    [JsonPropertyName("commonUsings")]
    public List<string> CommonUsings { get; set; } = [];

    /// <summary>
    /// Whether test classes use constructor-based setup or method-based setup.
    /// </summary>
    [JsonPropertyName("usesConstructorSetup")]
    public bool UsesConstructorSetup { get; set; } = true;

    /// <summary>
    /// Whether existing tests use IDisposable for cleanup.
    /// </summary>
    [JsonPropertyName("usesDisposable")]
    public bool UsesDisposable { get; set; }

    /// <summary>
    /// Mock creation pattern: "field" (class-level Mock<T> fields) or "local" (per-method).
    /// </summary>
    [JsonPropertyName("mockPattern")]
    public string MockPattern { get; set; } = "field";

    /// <summary>
    /// Average number of tests per class in existing test files.
    /// </summary>
    [JsonPropertyName("avgTestsPerClass")]
    public double AvgTestsPerClass { get; set; }

    /// <summary>
    /// Total test files analyzed to produce these patterns.
    /// </summary>
    [JsonPropertyName("analyzedFileCount")]
    public int AnalyzedFileCount { get; set; }
}

/// <summary>
/// A helper method found in existing test files.
/// </summary>
public sealed class TestHelperMethod
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    /// <summary>Which test file(s) this helper appears in.</summary>
    [JsonPropertyName("foundIn")]
    public List<string> FoundIn { get; set; } = [];

    /// <summary>How many test files use this helper.</summary>
    [JsonPropertyName("usageCount")]
    public int UsageCount { get; set; }
}
