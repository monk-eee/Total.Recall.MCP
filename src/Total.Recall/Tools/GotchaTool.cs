using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using Total.Recall.Infrastructure;
using Total.Recall.Models;

namespace Total.Recall.Tools;

[McpServerToolType]
public static class GotchaTool
{
    [McpServerTool, Description(
        "Get all known pitfalls/gotchas for a specific type. " +
        "Returns construction traps, namespace issues, enum quirks, and API surprises " +
        "discovered during previous test generation sessions.")]
    public static string GetGotchas(
        [Description("Type name to look up gotchas for")] string typeName)
    {
        try
        {
            if (!StoreRegistry.Gotchas.HasData())
                return $"No gotchas database found. No known issues for '{typeName}'.";

            var matches = StoreRegistry.Gotchas.Query(g =>
                g.Type.Contains(typeName, StringComparison.OrdinalIgnoreCase));

            if (matches.Count == 0)
                return $"No gotchas found for '{typeName}'. Looks clean!";

            return JsonSerializer.Serialize(matches, SharedJsonOptions.Indented);
        }
        catch (Exception ex)
        {
            Log.Error($"[GetGotchas] failed for '{typeName}': {ex.GetType().Name}: {ex.Message}");
            return $"ERROR in GetGotchas: {ex.GetType().Name}: {ex.Message}";
        }
    }

    [McpServerTool, Description(
        "Record a new gotcha discovered during test generation. " +
        "Persists to disk for future sessions. " +
        "Categories: constructor, namespace, enum, equality, mock, unreachable, property, inheritance, bug, static")]
    public static string AddGotcha(
        [Description("Type name the gotcha applies to")] string typeName,
        [Description("Category: constructor|namespace|enum|equality|mock|unreachable|property|inheritance|bug|static")] string category,
        [Description("Description of the pitfall/gotcha")] string gotcha)
    {
        try
        {
            var record = new Gotcha
            {
                Type = typeName,
                Category = category,
                Description = gotcha,
                DiscoveredInGen = null,
                Date = DateTime.UtcNow.ToString("yyyy-MM-dd")
            };

            StoreRegistry.Gotchas.Append(record);

            return $"Added gotcha for '{typeName}' [{category}]: {gotcha}";
        }
        catch (Exception ex)
        {
            Log.Error($"[AddGotcha] failed for '{typeName}': {ex.GetType().Name}: {ex.Message}");
            return $"ERROR in AddGotcha: {ex.GetType().Name}: {ex.Message}";
        }
    }
}
