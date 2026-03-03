using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using Total.Recall.Infrastructure;
using Total.Recall.Models;

namespace Total.Recall.Tools;

/// <summary>
/// MCP tool for recording and querying testability assessments.
/// Persists verdicts (testable, coupled, skip, deferred) so future sessions
/// can skip re-evaluating already-assessed classes.
/// </summary>
[McpServerToolType]
public static class AssessmentTool
{
    [McpServerTool, Description(
        "Record a testability assessment for a class. Verdicts: testable, coupled, skip, deferred. " +
        "Persists to disk — future sessions skip re-evaluating this class. " +
        "Include key dependencies and optional cluster name for grouping.")]
    public static string AddAssessment(
        [Description("Class name being assessed (e.g. 'AuditEntryBuilder')")] string className,
        [Description("Verdict: testable|coupled|skip|deferred")] string verdict,
        [Description("Why this verdict — key coupling, base classes, dependencies")] string reasoning,
        [Description("Optional: key dependencies driving the verdict (comma-separated)")] string? dependencies = null,
        [Description("Optional: cluster name if grouped with related types")] string? cluster = null,
        [Description("Optional: namespace/session to write to (default: server default)")] string? ns = null)
    {
        Metrics.Increment(Metrics.ToolAddAssessment);
        Log.Debug($"[AddAssessment] className='{className}' verdict='{verdict}' ns='{ns ?? "(default)"}'");
        try
        {
            var stores = StoreRegistry.ForNamespace(ns);
            var record = new Assessment
            {
                Class = className,
                Verdict = verdict.ToLowerInvariant(),
                Reasoning = reasoning,
                Dependencies = string.IsNullOrWhiteSpace(dependencies)
                    ? []
                    : dependencies.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList(),
                Cluster = string.IsNullOrWhiteSpace(cluster) ? null : cluster.Trim(),
                Date = DateTime.UtcNow.ToString("yyyy-MM-dd")
            };

            stores.Assessments.Append(record);
            Log.Debug($"[AddAssessment] recorded assessment for '{className}': {verdict}");

            var depsText = record.Dependencies.Count > 0
                ? $" deps=[{string.Join(", ", record.Dependencies)}]"
                : "";
            var clusterText = record.Cluster is not null
                ? $" cluster='{record.Cluster}'"
                : "";

            return $"Recorded assessment for '{className}': {verdict}{depsText}{clusterText}";
        }
        catch (Exception ex)
        {
            Log.Error($"[AddAssessment] failed for '{className}': {ex.GetType().Name}: {ex.Message}");
            return $"ERROR in AddAssessment: {ex.GetType().Name}: {ex.Message}";
        }
    }

    [McpServerTool, Description(
        "Get testability assessments from previous sessions. " +
        "Returns the latest verdict for each class. " +
        "Filter by class name (partial match) or verdict (testable/coupled/skip/deferred). " +
        "Omit both parameters to get all assessments.")]
    public static string GetAssessments(
        [Description("Optional: class name to look up (partial match)")] string? className = null,
        [Description("Optional: filter by verdict (testable|coupled|skip|deferred)")] string? verdict = null,
        [Description("Optional: namespace/session to query (default: server default)")] string? ns = null)
    {
        Metrics.Increment(Metrics.ToolGetAssessments);
        Log.Debug($"[GetAssessments] className='{className ?? "(all)"}' verdict='{verdict ?? "(all)"}' ns='{ns ?? "(default)"}'");
        try
        {
            var stores = StoreRegistry.ForNamespace(ns);

            if (!stores.Assessments.HasData())
            {
                Log.Debug("[GetAssessments] no assessment data found");
                return "No assessments recorded yet. Use add_assessment during testability analysis.";
            }

            var all = stores.Assessments.LoadAll();
            Log.Debug($"[GetAssessments] loaded {all.Count} raw records");

            // Deduplicate: latest assessment wins per class (append-only, so last = latest)
            var latest = new Dictionary<string, Assessment>(StringComparer.OrdinalIgnoreCase);
            foreach (var a in all)
                latest[a.Class] = a;

            IEnumerable<Assessment> results = latest.Values;

            if (!string.IsNullOrWhiteSpace(className))
                results = results.Where(a =>
                    a.Class.Contains(className, StringComparison.OrdinalIgnoreCase));

            if (!string.IsNullOrWhiteSpace(verdict))
                results = results.Where(a =>
                    a.Verdict.Equals(verdict.Trim(), StringComparison.OrdinalIgnoreCase));

            var list = results.OrderBy(a => a.Verdict).ThenBy(a => a.Class).ToList();
            Log.Debug($"[GetAssessments] returning {list.Count} deduplicated assessments (from {latest.Count} unique classes)");

            if (list.Count == 0)
            {
                var filter = className ?? verdict ?? "any";
                return $"No assessments found matching '{filter}'.";
            }

            return JsonSerializer.Serialize(list, SharedJsonOptions.CamelCaseIndented);
        }
        catch (Exception ex)
        {
            Log.Error($"[GetAssessments] failed: {ex.GetType().Name}: {ex.Message}");
            return $"ERROR in GetAssessments: {ex.GetType().Name}: {ex.Message}";
        }
    }
}
