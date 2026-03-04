using System.Text.Json.Serialization;

namespace Total.Recall.Models;

/// <summary>
/// Pre-built Moq setup code for a commonly-mocked interface.
/// One record per interface in mock-recipes.jsonl.
/// </summary>
public sealed class MockRecipe
{
    [JsonPropertyName("interface")]
    public string Interface { get; set; } = "";

    [JsonPropertyName("namespace")]
    public string Namespace { get; set; } = "";

    [JsonPropertyName("requiredUsings")]
    public List<string> RequiredUsings { get; set; } = [];

    [JsonPropertyName("recipe")]
    public string Recipe { get; set; } = "";

    [JsonPropertyName("gotchas")]
    public List<string> Gotchas { get; set; } = [];

    [JsonPropertyName("usedByClasses")]
    public List<string> UsedByClasses { get; set; } = [];

    /// <summary>
    /// Real usage examples from the test codebase showing how this mock is configured.
    /// Populated during --enrich when test files contain Mock&lt;IFoo&gt; patterns.
    /// </summary>
    [JsonPropertyName("usageExamples")]
    public List<string> UsageExamples { get; set; } = [];
}
