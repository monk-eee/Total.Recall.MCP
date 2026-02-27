using System.Text.Json.Serialization;

namespace Total.Recall.Models;

/// <summary>
/// Represents a single .NET type extracted via reflection from a target assembly.
/// One record per type in type-registry.jsonl.
/// </summary>
public sealed class TypeRecord
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("namespace")]
    public string Namespace { get; set; } = "";

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
