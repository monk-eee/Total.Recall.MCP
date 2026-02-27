using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using Total.Recall.Infrastructure;
using Total.Recall.Models;

namespace Total.Recall.Tools;

[McpServerToolType]
public static class ResolveTypeTool
{
    [McpServerTool, Description(
        "Resolve a .NET type name to its full namespace, constructors, properties, base type, and interfaces. " +
        "Supports partial name matching, namespace search, and file path search. Returns up to 5 results.")]
    public static string ResolveType(
        [Description("Exact or partial class/interface/enum name")] string typeName,
        [Description("Optional: filter by namespace prefix (e.g. 'Server.Auditing')")] string? namespacePart = null,
        [Description("Optional: filter by source file path substring (e.g. 'Parsing/Output')")] string? filePath = null)
    {
        try
        {
        return ResolveTypeCore(typeName, namespacePart, filePath);
        }
        catch (Exception ex)
        {
            Log.Error($"[ResolveType] failed for '{typeName}': {ex.GetType().Name}: {ex.Message}");
            return $"ERROR in ResolveType: {ex.GetType().Name}: {ex.Message}";
        }
    }

    private static string ResolveTypeCore(string typeName, string? namespacePart, string? filePath)
    {
        if (!StoreRegistry.TypeRegistry.HasData())
            return "No type registry found. Run 'total-recall scan --assembly <dll>' first.";

        // Use pre-built dictionary index for exact/case-insensitive lookups (O(1))
        var (exactIndex, ciIndex) = StoreRegistry.GetTypeIndex();
        List<TypeRecord> matches;

        // 1. Exact name match via dictionary
        if (exactIndex.TryGetValue(typeName, out var exactMatch))
        {
            matches = [exactMatch];
        }
        // 2. Case-insensitive exact match via dictionary
        else if (ciIndex.TryGetValue(typeName, out var ciMatch))
        {
            matches = [ciMatch];
        }
        else
        {
            // 3-5: Fall back to linear scan only for partial/interface/namespace matches
            var all = StoreRegistry.TypeRegistry.LoadAll();

            // 3. Contains (partial match)
            matches = all.Where(t => t.Name.Contains(typeName, StringComparison.OrdinalIgnoreCase)).ToList();

            // 4. Interface name match (search interfaces list)
            if (matches.Count == 0)
                matches = all.Where(t => t.Interfaces.Any(i => i.Contains(typeName, StringComparison.OrdinalIgnoreCase))).ToList();

            // 5. Namespace search — if no name match, try namespace contains
            if (matches.Count == 0)
                matches = all.Where(t =>
                    t.Namespace?.Contains(typeName, StringComparison.OrdinalIgnoreCase) == true).ToList();
        }

        // Apply optional namespace filter
        if (!string.IsNullOrEmpty(namespacePart))
            matches = matches.Where(t =>
                t.Namespace?.Contains(namespacePart, StringComparison.OrdinalIgnoreCase) == true).ToList();

        // Apply optional file path filter (cross-reference coverage data for file paths)
        if (!string.IsNullOrEmpty(filePath))
        {
            var coverageData = StoreRegistry.CoverageGaps.LoadAll();
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
        return JsonSerializer.Serialize(results, SharedJsonOptions.CamelCaseIndented);
    }

}
