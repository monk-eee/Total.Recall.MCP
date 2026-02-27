using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using Total.Recall.Infrastructure;
using Total.Recall.Models;

namespace Total.Recall.Tools;

[McpServerToolType]
public static class TestInventoryTool
{
    [McpServerTool, Description(
        "Get existing test methods for a class, including which file they're in " +
        "and inferred method coverage. Prevents writing duplicate tests.")]
    public static string GetTestInventory(
        [Description("Class name to look up existing tests for")] string className,
        [Description("Optional: namespace/session to query (default: server default)")] string? ns = null)
    {
        Metrics.Increment(Metrics.ToolGetTestInventory);
        try
        {
            var stores = StoreRegistry.ForNamespace(ns);

            if (!stores.TestInventory.HasData())
                return $"No test inventory found. Run 'total-recall scan --tests <dir>' first.";

            var matches = stores.TestInventory.Query(t =>
                t.Class.Contains(className, StringComparison.OrdinalIgnoreCase));

            if (matches.Count == 0)
                return $"No existing tests found for '{className}'.";

            return JsonSerializer.Serialize(matches, SharedJsonOptions.Indented);
        }
        catch (Exception ex)
        {
            Log.Error($"[GetTestInventory] failed for '{className}': {ex.GetType().Name}: {ex.Message}");
            return $"ERROR in GetTestInventory: {ex.GetType().Name}: {ex.Message}";
        }
    }
}
