using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using Total.Recall.Infrastructure;
using Total.Recall.Models;

namespace Total.Recall.Tools;

/// <summary>
/// Combined context tool — returns type record, gotchas, test inventory,
/// and matching mock recipes in a single call (replaces 4 separate tool calls).
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
        var dataDir = RepoConfig.GetDataPath();

        var typeStore = new JsonLineStore<TypeRecord>(RepoConfig.TypeRegistryPath(dataDir));
        var gotchaStore = new JsonLineStore<Gotcha>(RepoConfig.GotchasPath(dataDir));
        var testStore = new JsonLineStore<TestInventoryEntry>(RepoConfig.TestInventoryPath(dataDir));
        var mockStore = new JsonLineStore<MockRecipe>(RepoConfig.MockRecipesPath(dataDir));

        // Resolve the type (exact → case-insensitive → contains)
        var allTypes = typeStore.LoadAll();
        var typeRecord = allTypes.FirstOrDefault(t =>
                t.Name == typeName) ??
            allTypes.FirstOrDefault(t =>
                t.Name.Equals(typeName, StringComparison.OrdinalIgnoreCase)) ??
            allTypes.FirstOrDefault(t =>
                t.Name.Contains(typeName, StringComparison.OrdinalIgnoreCase));

        // Get gotchas for this type
        var gotchas = gotchaStore.Query(g =>
            g.Type.Contains(typeName, StringComparison.OrdinalIgnoreCase));

        // Get test inventory
        var tests = testStore.Query(t =>
            t.Class.Contains(typeName, StringComparison.OrdinalIgnoreCase));

        // Get mock recipes for interfaces this type implements
        var mockRecipes = new List<MockRecipe>();
        if (typeRecord?.Interfaces is { Count: > 0 })
        {
            var allMocks = mockStore.LoadAll();
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

        return JsonSerializer.Serialize(result, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        });
    }
}
