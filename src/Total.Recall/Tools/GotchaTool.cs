using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using Total.Recall.Infrastructure;
using Total.Recall.Models;

namespace Total.Recall.Tools;

[McpServerToolType]
public static class GotchaTool
{
    private static readonly JsonSerializerOptions s_json = new() { WriteIndented = true };

    [McpServerTool, Description(
        "Get all known pitfalls/gotchas for a specific type. " +
        "Returns construction traps, namespace issues, enum quirks, and API surprises " +
        "discovered during previous test generation sessions.")]
    public static string GetGotchas(
        [Description("Type name to look up gotchas for")] string typeName)
    {
        var dataDir = RepoConfig.GetDataPath();
        var store = new JsonLineStore<Gotcha>(RepoConfig.GotchasPath(dataDir));

        if (!store.HasData())
            return $"No gotchas database found. No known issues for '{typeName}'.";

        var matches = store.Query(g =>
            g.Type.Contains(typeName, StringComparison.OrdinalIgnoreCase));

        if (matches.Count == 0)
            return $"No gotchas found for '{typeName}'. Looks clean!";

        return JsonSerializer.Serialize(matches, s_json);
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
        var dataDir = RepoConfig.GetDataPath();
        var store = new JsonLineStore<Gotcha>(RepoConfig.GotchasPath(dataDir));

        var record = new Gotcha
        {
            Type = typeName,
            Category = category,
            Description = gotcha,
            DiscoveredInGen = null,
            Date = DateTime.UtcNow.ToString("yyyy-MM-dd")
        };

        store.Append(record);

        return $"Added gotcha for '{typeName}' [{category}]: {gotcha}";
    }
}
