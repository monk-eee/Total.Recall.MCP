using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using Total.Recall.Infrastructure;
using Total.Recall.Models;

namespace Total.Recall.Tools;

/// <summary>
/// MCP tool for retrieving pre-built Moq setup recipes for .NET interfaces.
/// Each recipe includes required usings, C# mock code, and known gotchas.
/// Supports lookup with or without the 'I' prefix.
/// </summary>
[McpServerToolType]
public static class MockRecipeTool
{
    [McpServerTool, Description(
        "Get a pre-built Moq setup recipe for a .NET interface, including required usings, " +
        "C# mock code, and known gotchas. Supports names with or without the 'I' prefix.")]
    public static string GetMockRecipe(
        [Description("Interface name (e.g. 'IJobOutputInstance' or 'JobOutputInstance')")] string interfaceName,
        [Description("Optional: namespace/session to query (default: server default)")] string? ns = null)
    {
        return Telemetry.Track("get_mock_recipe", ns, new { interfaceName, ns }, () =>
        {
        Metrics.Increment(Metrics.ToolGetMockRecipe);
        Log.Debug($"[GetMockRecipe] interfaceName='{interfaceName}' ns='{ns ?? "(default)"}'");
        try
        {
            var stores = StoreRegistry.ForNamespace(ns);

            if (!stores.MockRecipes.HasData())
            {
                Log.Debug("[GetMockRecipe] no mock recipe data found");
                return "No mock recipes found. Seed mock-recipes.jsonl first.";
            }

            // Normalize: ensure we search with and without 'I' prefix
            var withI = interfaceName.StartsWith("I") && char.IsUpper(interfaceName.ElementAtOrDefault(1))
                ? interfaceName
                : "I" + interfaceName;
            var withoutI = interfaceName.StartsWith("I") && char.IsUpper(interfaceName.ElementAtOrDefault(1))
                ? interfaceName[1..]
                : interfaceName;

            Log.Debug($"[GetMockRecipe] searching withI='{withI}' withoutI='{withoutI}'");

            var matches = stores.MockRecipes.Query(r =>
                r.Interface.Equals(withI, StringComparison.OrdinalIgnoreCase) ||
                r.Interface.Equals(withoutI, StringComparison.OrdinalIgnoreCase) ||
                r.Interface.Contains(interfaceName, StringComparison.OrdinalIgnoreCase));

            Log.Debug($"[GetMockRecipe] found {matches.Count} matches");

            if (matches.Count == 0)
                return $"No mock recipe found for '{interfaceName}'.";

            return JsonSerializer.Serialize(matches, SharedJsonOptions.Indented);
        }
        catch (Exception ex)
        {
            Log.Error($"[GetMockRecipe] failed for '{interfaceName}': {ex.GetType().Name}: {ex.Message}");
            return $"ERROR in GetMockRecipe: {ex.GetType().Name}: {ex.Message}";
        }
        });
    }
}
