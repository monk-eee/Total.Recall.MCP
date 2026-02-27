using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using Total.Recall.Infrastructure;
using Total.Recall.Models;

namespace Total.Recall.Tools;

/// <summary>
/// Combined context tool — returns type record, gotchas, test inventory,
/// mock recipes, and assessments in a single call (replaces 5 separate tool calls).
/// Uses StoreRegistry singletons for cross-call caching.
/// </summary>
[McpServerToolType]
public static class ContextTool
{
    [McpServerTool, Description(
        "Get full context for a type in one call: type record, gotchas, " +
        "test inventory, mock recipes for its interfaces, and testability assessments. " +
        "Use this instead of calling ResolveType + GetGotchas + GetTestInventory + GetMockRecipe separately.")]
    public static string GetContext(
        [Description("The type name to look up (e.g. 'AuditEntry', 'IContentBase')")] string typeName,
        [Description("Optional: namespace/session to query (default: server default)")] string? ns = null)
    {
        Metrics.Increment(Metrics.ToolGetContext);
        try
        {
            return GetContextCore(typeName, ns);
        }
        catch (Exception ex)
        {
            Log.Error($"[GetContext] failed for '{typeName}': {ex.GetType().Name}: {ex.Message}");
            return $"ERROR in GetContext: {ex.GetType().Name}: {ex.Message}";
        }
    }

    private static string GetContextCore(string typeName, string? ns)
    {
        var stores = StoreRegistry.ForNamespace(ns);

        // Use pre-built dictionary index for O(1) exact lookup, fall back to linear scan for contains
        var (exactIndex, ciIndex) = stores.GetTypeIndex();

        TypeRecord? typeRecord = null;
        if (exactIndex.TryGetValue(typeName, out var exact))
        {
            Metrics.Increment(Metrics.LookupExact);
            typeRecord = exact;
        }
        else if (ciIndex.TryGetValue(typeName, out var ci))
        {
            Metrics.Increment(Metrics.LookupCaseInsensitive);
            typeRecord = ci;
        }
        else
        {
            // Contains fallback — linear scan only when dictionary misses
            typeRecord = stores.TypeRegistry.LoadAll().FirstOrDefault(t =>
                t.Name.Contains(typeName, StringComparison.OrdinalIgnoreCase));
            Metrics.Increment(typeRecord is not null ? Metrics.LookupContains : Metrics.LookupMiss);
        }

        // Get gotchas for this type
        var gotchas = stores.Gotchas.Query(g =>
            g.Type.Contains(typeName, StringComparison.OrdinalIgnoreCase));

        // Get test inventory
        var tests = stores.TestInventory.Query(t =>
            t.Class.Contains(typeName, StringComparison.OrdinalIgnoreCase));

        // Get assessments for this type
        var assessments = stores.Assessments.HasData()
            ? stores.Assessments.Query(a =>
                a.Class.Contains(typeName, StringComparison.OrdinalIgnoreCase))
            : [];

        // Get mock recipes for interfaces this type implements
        var mockRecipes = new List<MockRecipe>();
        if (typeRecord?.Interfaces is { Count: > 0 })
        {
            var allMocks = stores.MockRecipes.LoadAll();
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
            mockRecipes,
            assessments
        };

        return JsonSerializer.Serialize(result, SharedJsonOptions.CamelCaseIndented);
    }
}
