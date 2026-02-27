using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using Total.Recall.Infrastructure;
using Total.Recall.Models;

namespace Total.Recall.Tools;

/// <summary>
/// Combined context tool — returns type record, gotchas, test inventory,
/// and matching mock recipes in a single call (replaces 4 separate tool calls).
/// Uses StoreRegistry singletons for cross-call caching.
/// </summary>
[McpServerToolType]
public static class ContextTool
{
    [McpServerTool, Description(
        "Get full context for a type in one call: type record, gotchas, " +
        "test inventory, and mock recipes for its interfaces. " +
        "Use this instead of calling ResolveType + GetGotchas + GetTestInventory + GetMockRecipe separately.")]
    public static string GetContext(
        [Description("The type name to look up (e.g. 'AuditEntry', 'IContentBase')")] string typeName)
    {
        try
        {
        return GetContextCore(typeName);
        }
        catch (Exception ex)
        {
            Log.Error($"[GetContext] failed for '{typeName}': {ex.GetType().Name}: {ex.Message}");
            return $"ERROR in GetContext: {ex.GetType().Name}: {ex.Message}";
        }
    }

    private static string GetContextCore(string typeName)
    {
        // Use pre-built dictionary index for O(1) exact lookup, fall back to linear scan for contains
        var (exactIndex, ciIndex) = StoreRegistry.GetTypeIndex();

        TypeRecord? typeRecord = null;
        if (exactIndex.TryGetValue(typeName, out var exact))
            typeRecord = exact;
        else if (ciIndex.TryGetValue(typeName, out var ci))
            typeRecord = ci;
        else
        {
            // Contains fallback — linear scan only when dictionary misses
            typeRecord = StoreRegistry.TypeRegistry.LoadAll().FirstOrDefault(t =>
                t.Name.Contains(typeName, StringComparison.OrdinalIgnoreCase));
        }

        // Get gotchas for this type
        var gotchas = StoreRegistry.Gotchas.Query(g =>
            g.Type.Contains(typeName, StringComparison.OrdinalIgnoreCase));

        // Get test inventory
        var tests = StoreRegistry.TestInventory.Query(t =>
            t.Class.Contains(typeName, StringComparison.OrdinalIgnoreCase));

        // Get mock recipes for interfaces this type implements
        var mockRecipes = new List<MockRecipe>();
        if (typeRecord?.Interfaces is { Count: > 0 })
        {
            var allMocks = StoreRegistry.MockRecipes.LoadAll();
            foreach (var iface in typeRecord.Interfaces)
            {
                var normalized = iface.StartsWith("I") ? iface[1..] : iface;
                var recipe = allMocks.FirstOrDefault(m =>
                    m.Interface.Equals(iface, StringComparison.OrdinalIgnoreCase) ||
                    m.Interface.Equals("I" + normalized, StringComparison.OrdinalIgnoreCase));
                if (recipe is not null)
                    mockRecipes.Add(recipe);
            }
        }

        var result = new
        {
            type = typeRecord,
            gotchas,
            tests,
            mockRecipes
        };

        return JsonSerializer.Serialize(result, SharedJsonOptions.CamelCaseIndented);
    }

}
