using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using Total.Recall.Infrastructure;
using Total.Recall.Models;

namespace Total.Recall.Tools;

/// <summary>
/// MCP tool for querying and recording type-specific pitfalls (gotchas).
/// Tracks construction traps, namespace issues, enum quirks, and API surprises
/// discovered during test generation sessions.
/// </summary>
[McpServerToolType]
public static class GotchaTool
{
    [McpServerTool, Description(
        "Get all known pitfalls/gotchas for a specific type. " +
        "Returns construction traps, namespace issues, enum quirks, and API surprises " +
        "discovered during previous test generation sessions.")]
    public static string GetGotchas(
        [Description("Type name to look up gotchas for")] string typeName,
        [Description("Optional: namespace/session to query (default: server default)")] string? ns = null)
    {
        return Telemetry.Track("get_gotchas", ns, new { typeName, ns }, () =>
        {
        Metrics.Increment(Metrics.ToolGetGotchas);
        Log.Debug($"[GetGotchas] typeName='{typeName}' ns='{ns ?? "(default)"}'");
        try
        {
            var stores = StoreRegistry.ForNamespace(ns);

            if (!stores.Gotchas.HasData())
            {
                Log.Debug($"[GetGotchas] no gotchas data for ns='{stores.Name}'");
                return $"No gotchas database found. No known issues for '{typeName}'.";
            }

            var matches = stores.Gotchas.Query(g =>
                g.Type.Contains(typeName, StringComparison.OrdinalIgnoreCase));

            Log.Debug($"[GetGotchas] found {matches.Count} matches for '{typeName}'");

            if (matches.Count == 0)
                return $"No gotchas found for '{typeName}'. Looks clean!";

            return JsonSerializer.Serialize(matches, SharedJsonOptions.Indented);
        }
        catch (Exception ex)
        {
            Log.Error($"[GetGotchas] failed for '{typeName}': {ex.GetType().Name}: {ex.Message}");
            return $"ERROR in GetGotchas: {ex.GetType().Name}: {ex.Message}";
        }
        });
    }

    [McpServerTool, Description(
        "Record a new gotcha discovered during test generation. " +
        "Persists to disk for future sessions. " +
        "Categories: constructor, namespace, enum, equality, mock, unreachable, property, inheritance, bug, static")]
    public static string AddGotcha(
        [Description("Type name the gotcha applies to")] string typeName,
        [Description("Category: constructor|namespace|enum|equality|mock|unreachable|property|inheritance|bug|static")] string category,
        [Description("Description of the pitfall/gotcha")] string gotcha,
        [Description("Optional: namespace/session to write to (default: server default)")] string? ns = null)
    {
        return Telemetry.Track("add_gotcha", ns, new { typeName, category, ns }, () =>
        {
        Metrics.Increment(Metrics.ToolAddGotcha);
        Log.Debug($"[AddGotcha] typeName='{typeName}' category='{category}' ns='{ns ?? "(default)"}'");
        try
        {
            var stores = StoreRegistry.ForNamespace(ns);
            var record = new Gotcha
            {
                Type = typeName,
                Category = category,
                Description = gotcha,
                DiscoveredInGen = null,
                Date = DateTime.UtcNow.ToString("yyyy-MM-dd")
            };

            stores.Gotchas.Append(record);
            Log.Debug($"[AddGotcha] appended gotcha for '{typeName}'");

            // ── Assessment downgrade hint ──
            // When a type accumulates >3 gotchas while assessed as "testable",
            // the mounting gotchas suggest the class is harder to test than originally thought.
            string? downgradeHint = null;
            var totalGotchas = stores.Gotchas.Query(g =>
                g.Type.Equals(typeName, StringComparison.OrdinalIgnoreCase)).Count;

            if (totalGotchas > 3 && stores.Assessments.HasData())
            {
                var assessments = stores.Assessments.LoadAll();
                var latestAssessment = assessments
                    .LastOrDefault(a => a.Class.Equals(typeName, StringComparison.OrdinalIgnoreCase));
                if (latestAssessment is not null && latestAssessment.Verdict is "testable")
                {
                    downgradeHint = $"\n⚠ DOWNGRADE HINT: '{typeName}' now has {totalGotchas} gotchas but is assessed as 'testable'. " +
                                    $"Consider: AddAssessment('{typeName}', 'coupled', '{totalGotchas} gotchas accumulated — likely harder than initially assessed')";
                }
            }

            return $"Added gotcha for '{typeName}' [{category}]: {gotcha}{downgradeHint ?? ""}";
        }
        catch (Exception ex)
        {
            Log.Error($"[AddGotcha] failed for '{typeName}': {ex.GetType().Name}: {ex.Message}");
            return $"ERROR in AddGotcha: {ex.GetType().Name}: {ex.Message}";
        }
        });
    }
}
