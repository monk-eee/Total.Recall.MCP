using System.Text.Json.Serialization;

namespace Total.Recall.Models;

/// <summary>
/// Existing test methods for a production class.
/// One record per tested class in test-inventory.jsonl.
/// </summary>
public sealed class TestInventoryEntry
{
    [JsonPropertyName("class")]
    public string Class { get; set; } = "";

    [JsonPropertyName("testFiles")]
    public List<string> TestFiles { get; set; } = [];

    [JsonPropertyName("testMethods")]
    public List<string> TestMethods { get; set; } = [];

    [JsonPropertyName("testCount")]
    public int TestCount { get; set; }

    [JsonPropertyName("inferredCoveredMethods")]
    public List<string> InferredCoveredMethods { get; set; } = [];
}
