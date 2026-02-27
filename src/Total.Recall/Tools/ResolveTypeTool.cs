using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using Total.Recall.Infrastructure;
using Total.Recall.Models;

namespace Total.Recall.Tools;

[McpServerToolType]
public static class ResolveTypeTool
{
    private static readonly JsonSerializerOptions s_json = new() { WriteIndented = true };

    [McpServerTool, Description(
        "Resolve a .NET type name to its full namespace, constructors, properties, base type, and interfaces. " +
        "Supports partial name matching. Returns up to 5 results.")]
    public static string ResolveType(
        [Description("Exact or partial class/interface/enum name")] string typeName)
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

        if (matches.Count == 0)
            return $"No type found matching '{typeName}'.";

        var results = matches.Take(5).ToList();
        return JsonSerializer.Serialize(results, s_json);
    }
}
