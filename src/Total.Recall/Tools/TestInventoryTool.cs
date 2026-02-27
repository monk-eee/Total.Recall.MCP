using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using Total.Recall.Infrastructure;
using Total.Recall.Models;

namespace Total.Recall.Tools;

[McpServerToolType]
public static class TestInventoryTool
{
    private static readonly JsonSerializerOptions s_json = new() { WriteIndented = true };

    [McpServerTool, Description(
        "Get existing test methods for a class, including which file they're in " +
        "and inferred method coverage. Prevents writing duplicate tests.")]
    public static string GetTestInventory(
        [Description("Class name to look up existing tests for")] string className)
    {
        var dataDir = RepoConfig.GetDataPath();
        var store = new JsonLineStore<TestInventoryEntry>(RepoConfig.TestInventoryPath(dataDir));

        if (!store.HasData())
            return $"No test inventory found. Run 'total-recall scan --tests <dir>' first.";

        var matches = store.Query(t =>
            t.Class.Contains(className, StringComparison.OrdinalIgnoreCase));

        if (matches.Count == 0)
            return $"No existing tests found for '{className}'.";

        return JsonSerializer.Serialize(matches, s_json);
    }
}
