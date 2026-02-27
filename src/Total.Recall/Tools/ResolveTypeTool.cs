using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using Total.Recall.Infrastructure;
using Total.Recall.Models;

namespace Total.Recall.Tools;

[McpServerToolType]
public static class ResolveTypeTool
{
    private static readonly JsonSerializerOptions s_json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    [McpServerTool, Description(
        "Resolve a .NET type name to its full namespace, constructors, properties, base type, and interfaces. " +
        "Supports partial name matching, namespace search, and file path search. Returns up to 5 results.")]
    public static string ResolveType(
        [Description("Exact or partial class/interface/enum name")] string typeName,
        [Description("Optional: filter by namespace prefix (e.g. 'Server.Auditing')")] string? namespacePart = null,
        [Description("Optional: filter by source file path substring (e.g. 'Parsing/Output')")] string? filePath = null)
    {
        var dataDir = RepoConfig.GetDataPath();
        var store = new JsonLineStore<TypeRecord>(RepoConfig.TypeRegistryPath(dataDir));

        if (!store.HasData())
            return "No type registry found. Run 'total-recall scan --assembly <dll>' first.";

        var all = store.LoadAll();

        // 1. Exact name match
        var matches = all.Where(t => t.Name.Equals(typeName, StringComparison.Ordinal)).ToList();

        // 2. Case-insensitive exact match
        if (matches.Count == 0)
            matches = all.Where(t => t.Name.Equals(typeName, StringComparison.OrdinalIgnoreCase)).ToList();

        // 3. Contains (partial match)
        if (matches.Count == 0)
            matches = all.Where(t => t.Name.Contains(typeName, StringComparison.OrdinalIgnoreCase)).ToList();

        // 4. Interface name match (search interfaces list)
        if (matches.Count == 0)
            matches = all.Where(t => t.Interfaces.Any(i => i.Contains(typeName, StringComparison.OrdinalIgnoreCase))).ToList();

        // 5. Namespace search — if no name match, try namespace contains
        if (matches.Count == 0)
            matches = all.Where(t =>
                t.Namespace?.Contains(typeName, StringComparison.OrdinalIgnoreCase) == true).ToList();

        // Apply optional namespace filter
        if (!string.IsNullOrEmpty(namespacePart))
            matches = matches.Where(t =>
                t.Namespace?.Contains(namespacePart, StringComparison.OrdinalIgnoreCase) == true).ToList();

        // Apply optional file path filter (cross-reference coverage data for file paths)
        if (!string.IsNullOrEmpty(filePath))
        {
            var coverageStore = new JsonLineStore<CoverageGap>(RepoConfig.CoverageGapsPath(dataDir));
            var coverageData = coverageStore.LoadAll();
            var classesInFile = coverageData
                .Where(c => c.File?.Contains(filePath, StringComparison.OrdinalIgnoreCase) == true)
                .Select(c => c.Class)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            matches = matches.Where(t => classesInFile.Contains(t.Name)).ToList();
        }

        if (matches.Count == 0)
            return $"No type found matching '{typeName}'" +
                   (namespacePart is not null ? $" in namespace '{namespacePart}'" : "") +
                   (filePath is not null ? $" in file '{filePath}'" : "") +
                   ".";

        var results = matches.Take(5).ToList();
        return JsonSerializer.Serialize(results, s_json);
    }
}
