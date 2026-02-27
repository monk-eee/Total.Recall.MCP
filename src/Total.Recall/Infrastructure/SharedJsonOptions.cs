using System.Text.Json;

namespace Total.Recall.Infrastructure;

/// <summary>
/// Shared JsonSerializerOptions instances. Eliminates per-tool and per-call allocations.
/// System.Text.Json caches reflection metadata inside the options object, so reusing
/// the same instance gives a ~3x speedup on subsequent serializations.
/// </summary>
public static class SharedJsonOptions
{
    /// <summary>
    /// camelCase, compact — used for JSONL data storage.
    /// </summary>
    public static readonly JsonSerializerOptions CamelCase = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    /// <summary>
    /// camelCase, indented — used for tool response JSON sent back to the agent.
    /// </summary>
    public static readonly JsonSerializerOptions CamelCaseIndented = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    /// <summary>
    /// Default (PascalCase), indented — used for gotcha responses.
    /// </summary>
    public static readonly JsonSerializerOptions Indented = new()
    {
        WriteIndented = true
    };
}
