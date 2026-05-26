using System.Text.Json.Serialization;

namespace Total.Recall.Models;

/// <summary>
/// Represents a single symbol (class / interface / enum / struct / record)
/// extracted from a target codebase. One record per type in type-registry.jsonl.
///
/// Schema is documented in docs/SCANNER_SCHEMA.md and is identical across all
/// language scanners (.NET, Python, TypeScript, …). Language-specific extension
/// fields live under <see cref="Lang"/>; cross-language consumers should read
/// the common fields (Name, Namespace, Kind, Constructors, Properties, …) and
/// peek into <see cref="Lang"/> only when they need a per-language detail.
/// </summary>
public sealed class TypeRecord
{
    /// <summary>
    /// Schema major version. Defaults to 1. Bumped only on breaking changes;
    /// additive changes (new fields) do NOT bump the version — readers ignore
    /// unknown fields.
    /// </summary>
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; set; } = 1;

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("namespace")]
    public string Namespace { get; set; } = "";

    /// <summary>
    /// Language-agnostic kind discriminator. One of: class, interface, enum,
    /// struct, function, type-alias, module, protocol. Defaults to "class"
    /// for backwards compatibility with pre-2.5 records.
    /// </summary>
    [JsonPropertyName("kind")]
    public string Kind { get; set; } = "class";

    [JsonPropertyName("fullUsing")]
    public string FullUsing { get; set; } = "";

    [JsonPropertyName("constructors")]
    public List<ConstructorRecord> Constructors { get; set; } = [];

    [JsonPropertyName("baseType")]
    public string? BaseType { get; set; }

    [JsonPropertyName("interfaces")]
    public List<string> Interfaces { get; set; } = [];

    [JsonPropertyName("isAbstract")]
    public bool IsAbstract { get; set; }

    [JsonPropertyName("isStatic")]
    public bool IsStatic { get; set; }

    [JsonPropertyName("isInternal")]
    public bool IsInternal { get; set; }

    [JsonPropertyName("isInterface")]
    public bool IsInterface { get; set; }

    [JsonPropertyName("isEnum")]
    public bool IsEnum { get; set; }

    [JsonPropertyName("properties")]
    public List<PropertyRecord> Properties { get; set; } = [];

    [JsonPropertyName("enumValues")]
    public List<string>? EnumValues { get; set; }

    /// <summary>
    /// Optional source file path relative to the target repo's source root.
    /// Forward-slashed, no leading slash. Always set by Python / TypeScript
    /// scanners; absent on .NET scans because <see cref="MetadataLoadContext"/>
    /// reads compiled assemblies and the source file isn't available.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("filePath")]
    public string? FilePath { get; set; }

    /// <summary>
    /// Language-specific extension block (per <c>lang.kind</c> discriminator).
    /// Common fields above are normalized across all scanners; per-language
    /// quirks (records, dataclasses, ambient declarations, generic arity, …)
    /// live here.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("lang")]
    public LangInfo? Lang { get; set; }
}

/// <summary>
/// Language-specific extension fields for a <see cref="TypeRecord"/>. The
/// <see cref="Kind"/> string discriminates between language families and
/// determines which of the other fields are populated.
///
/// .NET scanners populate <see cref="IsSealed"/>, <see cref="IsRecord"/>,
/// <see cref="GenericArity"/>. Python and TypeScript scanners populate their
/// own subset of the union fields. Null fields are omitted from JSON output
/// (see <c>JsonIgnoreCondition.WhenWritingNull</c>).
/// </summary>
public sealed class LangInfo
{
    /// <summary>"dotnet" | "python" | "typescript".</summary>
    [JsonPropertyName("kind")]
    public string Kind { get; set; } = "dotnet";

    // .NET
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("isSealed")]
    public bool? IsSealed { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("isRecord")]
    public bool? IsRecord { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("genericArity")]
    public int? GenericArity { get; set; }

    // Python
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("isDataclass")]
    public bool? IsDataclass { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("isFrozen")]
    public bool? IsFrozen { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("isAbc")]
    public bool? IsAbc { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("isProtocol")]
    public bool? IsProtocol { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("decorators")]
    public List<string>? Decorators { get; set; }

    // TypeScript
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("isExported")]
    public bool? IsExported { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("isAmbient")]
    public bool? IsAmbient { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("isReadonlyClass")]
    public bool? IsReadonlyClass { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("generics")]
    public List<string>? Generics { get; set; }
}

public sealed class ConstructorRecord
{
    [JsonPropertyName("params")]
    public List<string> Params { get; set; } = [];
}

public sealed class PropertyRecord
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("clrType")]
    public string ClrType { get; set; } = "";

    [JsonPropertyName("hasSet")]
    public bool HasSet { get; set; }

    [JsonPropertyName("hasInit")]
    public bool HasInit { get; set; }
}
