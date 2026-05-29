using System.Text.Json.Serialization;

namespace Total.Recall.Models;

/// <summary>
/// Coverage gap data for a single class. One record per class in coverage-gaps.jsonl.
/// Persisted shape matches the cross-language scanner contract in docs/SCANNER_SCHEMA.md
/// so the .NET MCP server can read JSONL emitted by any language's scanner (.NET, Python, TypeScript).
/// </summary>
public sealed class CoverageGap
{
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; set; } = 1;

    /// <summary>Fully-qualified class name: "{namespace}.{shortName}" (or just shortName when no namespace).</summary>
    [JsonPropertyName("className")]
    public string ClassName { get; set; } = "";

    [JsonPropertyName("filePath")]
    public string FilePath { get; set; } = "";

    [JsonPropertyName("linesCovered")]
    public int LinesCovered { get; set; }

    [JsonPropertyName("linesTotal")]
    public int LinesTotal { get; set; }

    [JsonPropertyName("coveragePercent")]
    public double CoveragePercent { get; set; }

    [JsonPropertyName("uncoveredMethods")]
    public List<UncoveredMethod> UncoveredMethods { get; set; } = [];

    /// <summary>Number of existing test methods targeting this class. Null until enriched by the test inventory scan.</summary>
    [JsonPropertyName("existingTests")]
    public int? ExistingTests { get; set; }

    /// <summary>Testability heuristic 0.0 (low) … 1.0 (high). Null until enriched against type metadata.</summary>
    [JsonPropertyName("testabilityScore")]
    public double? TestabilityScore { get; set; }

    // ── In-memory convenience views over canonical fields (not persisted) ──

    [JsonIgnore]
    public int UncoveredLineCount => LinesTotal - LinesCovered;

    [JsonIgnore]
    public string ShortName
    {
        get
        {
            if (string.IsNullOrEmpty(ClassName)) return "";
            var dot = ClassName.LastIndexOf('.');
            return dot >= 0 ? ClassName[(dot + 1)..] : ClassName;
        }
    }

    [JsonIgnore]
    public string NamespacePart
    {
        get
        {
            if (string.IsNullOrEmpty(ClassName)) return "";
            var dot = ClassName.LastIndexOf('.');
            return dot >= 0 ? ClassName[..dot] : "";
        }
    }
}

public sealed class UncoveredMethod
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    /// <summary>
    /// CLR method signature from Cobertura XML, e.g. "(System.Object)System.Boolean".
    /// Used to disambiguate overloaded methods in scaffold generation.
    /// </summary>
    [JsonPropertyName("signature")]
    public string Signature { get; set; } = "";

    /// <summary>Source-file line numbers that are uncovered. Empty when the whole method is covered.</summary>
    [JsonPropertyName("uncoveredLines")]
    public int[] UncoveredLines { get; set; } = [];

    /// <summary>Total executable line count for the method.</summary>
    [JsonPropertyName("totalLines")]
    public int TotalLines { get; set; }

    // ── In-memory convenience views (not persisted) ──

    [JsonIgnore]
    public int UncoveredLineCount => UncoveredLines.Length;

    [JsonIgnore]
    public int FirstUncoveredLine => UncoveredLines.Length == 0 ? 0 : UncoveredLines.Min();

    [JsonIgnore]
    public int LastUncoveredLine => UncoveredLines.Length == 0 ? 0 : UncoveredLines.Max();
}
