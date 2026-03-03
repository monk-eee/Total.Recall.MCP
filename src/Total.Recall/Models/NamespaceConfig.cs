using System.Text.Json.Serialization;

namespace Total.Recall.Models;

/// <summary>
/// Supported test frameworks for scaffold generation and test discovery.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TestFramework
{
    XUnit,
    NUnit,
    MSTest
}

/// <summary>
/// Supported mock libraries for scaffold generation.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum MockLibrary
{
    Moq,
    NSubstitute,
    FakeItEasy
}

/// <summary>
/// Per-namespace configuration written by the scanner and read by tools at runtime.
/// Stored as config.json (standard JSON, not JSONL) in each namespace data directory.
/// </summary>
public sealed class NamespaceConfig
{
    [JsonPropertyName("sourceRoot")]
    public string? SourceRoot { get; set; }

    [JsonPropertyName("scannedUtc")]
    public string? ScannedUtc { get; set; }

    [JsonPropertyName("assemblyPath")]
    public string? AssemblyPath { get; set; }

    [JsonPropertyName("coveragePath")]
    public string? CoveragePath { get; set; }

    [JsonPropertyName("testsPath")]
    public string? TestsPath { get; set; }

    /// <summary>
    /// Test framework used by the target repo (default: XUnit).
    /// Controls scaffold attributes ([Fact] vs [Test] vs [TestMethod]) and assertion styles.
    /// </summary>
    [JsonPropertyName("testFramework")]
    public TestFramework TestFramework { get; set; } = TestFramework.XUnit;

    /// <summary>
    /// Mock library used by the target repo (default: Moq).
    /// Controls mock creation patterns (Mock&lt;T&gt; vs Substitute.For&lt;T&gt; vs A.Fake&lt;T&gt;).
    /// </summary>
    [JsonPropertyName("mockLibrary")]
    public MockLibrary MockLibrary { get; set; } = MockLibrary.Moq;

    /// <summary>
    /// Pattern for deriving test namespace from production namespace.
    /// Use {Namespace} as placeholder for the full production namespace.
    /// Use {RootNamespace} for the first segment and {Rest} for the remainder.
    /// Default: "{Namespace}.Tests" → "MyApp.Services" becomes "MyApp.Services.Tests"
    /// Example: "{RootNamespace}.Tests.{Rest}" → "MyApp.Services" becomes "MyApp.Tests.Services"
    /// </summary>
    [JsonPropertyName("testNamespacePattern")]
    public string TestNamespacePattern { get; set; } = "{Namespace}.Tests";
}
