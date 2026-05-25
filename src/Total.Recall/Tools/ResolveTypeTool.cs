using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using Total.Recall.Infrastructure;
using Total.Recall.Models;

namespace Total.Recall.Tools;

[McpServerToolType]
public static class ResolveTypeTool
{
    [McpServerTool, Description(
        "Resolve a .NET type name to its full namespace, constructors, properties, base type, and interfaces. " +
        "Supports partial name matching, namespace search, and file path search. Returns up to 5 results.")]
    public static string ResolveType(
        [Description("Exact or partial class/interface/enum name")] string typeName,
        [Description("Optional: filter by namespace prefix (e.g. 'Server.Auditing')")] string? namespacePart = null,
        [Description("Optional: filter by source file path substring (e.g. 'Parsing/Output')")] string? filePath = null,
        [Description("Optional: namespace/session to query (default: server default)")] string? ns = null)
    {
        return Telemetry.Track("resolve_type", ns, new { typeName, namespacePart, filePath, ns }, () =>
        {
        Metrics.Increment(Metrics.ToolResolveType);
        Log.Debug($"[ResolveType] typeName='{typeName}' namespacePart='{namespacePart ?? "(none)"}' filePath='{filePath ?? "(none)"}' ns='{ns ?? "(default)"}'");
        try
        {
            return ResolveTypeCore(typeName, namespacePart, filePath, ns);
        }
        catch (Exception ex)
        {
            Log.Error($"[ResolveType] failed for '{typeName}': {ex.GetType().Name}: {ex.Message}");
            return $"ERROR in ResolveType: {ex.GetType().Name}: {ex.Message}";
        }
        });
    }

    private static string ResolveTypeCore(string typeName, string? namespacePart, string? filePath, string? ns)
    {
        var stores = StoreRegistry.ForNamespace(ns);

        if (!stores.TypeRegistry.HasData())
        {
            Log.Debug("[ResolveType] no type registry data found");
            return "No type registry found. Run 'total-recall scan --assembly <dll>' first.";
        }

        // Use pre-built dictionary index for exact/case-insensitive lookups (O(1))
        var (exactIndex, ciIndex) = stores.GetTypeIndex();
        Log.Debug($"[ResolveType] index has {exactIndex.Count} entries");
        List<TypeRecord> matches;

        // 1. Exact name match via dictionary
        if (exactIndex.TryGetValue(typeName, out var exactMatch))
        {
            Metrics.Increment(Metrics.LookupExact);
            matches = [exactMatch];
        }
        // 2. Case-insensitive exact match via dictionary
        else if (ciIndex.TryGetValue(typeName, out var ciMatch))
        {
            Metrics.Increment(Metrics.LookupCaseInsensitive);
            matches = [ciMatch];
        }
        else
        {
            // 3-5: Fall back to linear scan only for partial/interface/namespace matches
            var all = stores.TypeRegistry.LoadAll();

            // 3. Contains (partial match)
            matches = all.Where(t => t.Name.Contains(typeName, StringComparison.OrdinalIgnoreCase)).ToList();

            if (matches.Count > 0)
            {
                Metrics.Increment(Metrics.LookupContains);
            }
            else
            {
                // 4. Interface name match (search interfaces list)
                matches = all.Where(t => t.Interfaces.Any(i => i.Contains(typeName, StringComparison.OrdinalIgnoreCase))).ToList();

                if (matches.Count > 0)
                {
                    Metrics.Increment(Metrics.LookupInterface);
                }
                else
                {
                    // 5. Namespace search — if no name match, try namespace contains
                    matches = all.Where(t =>
                        t.Namespace?.Contains(typeName, StringComparison.OrdinalIgnoreCase) == true).ToList();

                    Metrics.Increment(matches.Count > 0 ? Metrics.LookupNamespace : Metrics.LookupMiss);
                }
            }
        }

        // Apply optional namespace filter
        if (!string.IsNullOrEmpty(namespacePart))
            matches = matches.Where(t =>
                t.Namespace?.Contains(namespacePart, StringComparison.OrdinalIgnoreCase) == true).ToList();

        // Apply optional file path filter (cross-reference coverage data for file paths)
        if (!string.IsNullOrEmpty(filePath))
        {
            var coverageData = stores.CoverageGaps.LoadAll();
            var classesInFile = coverageData
                .Where(c => c.File?.Contains(filePath, StringComparison.OrdinalIgnoreCase) == true)
                .Select(c => c.Class)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            matches = matches.Where(t => classesInFile.Contains(t.Name)).ToList();
        }

        if (matches.Count == 0)
        {
            Log.Debug($"[ResolveType] no matches for '{typeName}'");
            return $"No type found matching '{typeName}'" +
                   (namespacePart is not null ? $" in namespace '{namespacePart}'" : "") +
                   (filePath is not null ? $" in file '{filePath}'" : "") +
                   ".";
        }

        var results = matches.Take(5).ToList();
        Log.Debug($"[ResolveType] returning {results.Count} result(s) for '{typeName}'");
        return JsonSerializer.Serialize(results, SharedJsonOptions.CamelCaseIndented);
    }
}
