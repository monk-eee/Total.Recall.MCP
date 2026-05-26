using System.ComponentModel;
using System.Security.Cryptography;
using System.Text.Json;
using ModelContextProtocol.Server;
using Total.Recall.Infrastructure;
using Total.Recall.Models;

namespace Total.Recall.Tools;

/// <summary>
/// MCP tools for filing and querying class-scoped bug reports.
///
/// Bugs are the third leg of Total.Recall's persistent knowledge surface
/// alongside gotchas (testing pitfalls) and assessments (testability verdicts).
/// They capture broken behaviour discovered while writing tests or reading
/// code so the next session — possibly a different model — sees the known
/// issue before authoring tests for it.
///
/// Storage is append-only JSONL keyed by stable <c>bug-{hex}</c> ids. Status
/// transitions append a new record with the same id; the latest record per
/// id wins on read (mirrors the assessments deduplication pattern).
/// </summary>
[McpServerToolType]
public static class BugReportTool
{
    internal static readonly string[] AllowedSeverities = ["low", "medium", "high", "critical"];
    internal static readonly string[] AllowedStatuses = ["open", "triaged", "fixed", "wontfix"];

    [McpServerTool, Description(
        "File a bug report against a class (and optionally a method) discovered while " +
        "writing tests or reading code. Returns the assigned bug id. " +
        "Severity must be one of: low|medium|high|critical. " +
        "Future sessions see open bugs for a class via get_context, so report what you find — " +
        "the persistent knowledge surface is where Total.Recall earns its keep.")]
    public static string ReportBug(
        [Description("Class the bug applies to (required)")] string className,
        [Description("Severity: low|medium|high|critical")] string severity,
        [Description("Short description of the broken behaviour")] string description,
        [Description("Optional: method name if the bug is method-scoped")] string? methodName = null,
        [Description("Optional: minimal repro snippet or steps")] string? repro = null,
        [Description("Optional: test name that surfaced the bug")] string? foundInTestName = null,
        [Description("Optional: model identifier (e.g. 'claude-opus-4.7')")] string? model = null,
        [Description("Optional: namespace/session to write to (default: server default)")] string? ns = null)
    {
        return Telemetry.Track("report_bug", ns, new { className, severity, methodName, ns }, () =>
        {
            Metrics.Increment(Metrics.ToolReportBug);
            Log.Debug($"[ReportBug] class='{className}' method='{methodName ?? "(none)"}' severity='{severity}' ns='{ns ?? "(default)"}'");
            try
            {
                if (string.IsNullOrWhiteSpace(className))
                    return "ERROR in ReportBug: className is required.";
                if (string.IsNullOrWhiteSpace(description))
                    return "ERROR in ReportBug: description is required.";

                var sev = (severity ?? "").Trim().ToLowerInvariant();
                if (!AllowedSeverities.Contains(sev))
                    return $"ERROR in ReportBug: severity must be one of {string.Join("|", AllowedSeverities)} (got '{severity}').";

                var stores = StoreRegistry.ForNamespace(ns);
                var nowUtc = DateTime.UtcNow.ToString("O");
                var id = "bug-" + RandomHex(12);

                var record = new BugReport
                {
                    SchemaVersion = 1,
                    Id = id,
                    Class = className.Trim(),
                    Method = string.IsNullOrWhiteSpace(methodName) ? null : methodName.Trim(),
                    Severity = sev,
                    Description = description.Trim(),
                    Repro = string.IsNullOrWhiteSpace(repro) ? null : repro,
                    FoundInTestName = string.IsNullOrWhiteSpace(foundInTestName) ? null : foundInTestName.Trim(),
                    Status = "open",
                    StatusNotes = null,
                    Model = string.IsNullOrWhiteSpace(model) ? null : model.Trim(),
                    SessionId = Telemetry.SessionId,
                    TaskId = Telemetry.ActiveTaskId,
                    CreatedAt = nowUtc,
                    UpdatedAt = nowUtc
                };

                stores.Bugs.Append(record);
                Log.Debug($"[ReportBug] filed {id} for '{className}' [{sev}]");

                return JsonSerializer.Serialize(
                    new { ok = true, id, @class = record.Class, method = record.Method, severity = sev, status = "open" },
                    SharedJsonOptions.CamelCaseIndented);
            }
            catch (Exception ex)
            {
                Log.Error($"[ReportBug] failed for '{className}': {ex.GetType().Name}: {ex.Message}");
                return $"ERROR in ReportBug: {ex.GetType().Name}: {ex.Message}";
            }
        });
    }

    [McpServerTool, Description(
        "Query bug reports. By default returns OPEN bugs only. " +
        "Filter by className (partial match), severity (low|medium|high|critical), or " +
        "status (open|triaged|fixed|wontfix). Pass status='all' to include closed bugs. " +
        "Latest record per bug id wins (append-only with status transitions).")]
    public static string GetBugs(
        [Description("Optional: class name (partial match)")] string? className = null,
        [Description("Optional: severity filter (low|medium|high|critical)")] string? severity = null,
        [Description("Optional: status filter (open|triaged|fixed|wontfix|all). Default: open")] string? status = "open",
        [Description("Max results to return (default: 50). Use 0 for no limit.")] int top = 50,
        [Description("Optional: namespace/session to query (default: server default)")] string? ns = null)
    {
        return Telemetry.Track("get_bugs", ns, new { className, severity, status, top, ns }, () =>
        {
            Metrics.Increment(Metrics.ToolGetBugs);
            Log.Debug($"[GetBugs] class='{className ?? "(any)"}' severity='{severity ?? "(any)"}' status='{status ?? "open"}' ns='{ns ?? "(default)"}'");
            try
            {
                var stores = StoreRegistry.ForNamespace(ns);
                if (!stores.Bugs.HasData())
                    return "No bugs recorded yet. Use report_bug when you find broken behaviour.";

                var latest = BuildLatest(stores.Bugs.LoadAll());
                IEnumerable<BugReport> results = latest.Values;

                var statusFilter = (status ?? "open").Trim().ToLowerInvariant();
                if (statusFilter != "all")
                    results = results.Where(b => b.Status.Equals(statusFilter, StringComparison.OrdinalIgnoreCase));

                if (!string.IsNullOrWhiteSpace(className))
                    results = results.Where(b => b.Class.Contains(className, StringComparison.OrdinalIgnoreCase));

                if (!string.IsNullOrWhiteSpace(severity))
                    results = results.Where(b => b.Severity.Equals(severity.Trim(), StringComparison.OrdinalIgnoreCase));

                var ordered = results
                    .OrderBy(b => SeverityRank(b.Severity))
                    .ThenByDescending(b => b.UpdatedAt, StringComparer.Ordinal)
                    .ToList();

                var totalCount = ordered.Count;
                if (top > 0) ordered = ordered.Take(top).ToList();

                if (ordered.Count == 0)
                    return $"No bugs matched (status='{statusFilter}', class='{className ?? "any"}', severity='{severity ?? "any"}').";

                return JsonSerializer.Serialize(
                    new { totalCount, returned = ordered.Count, bugs = ordered },
                    SharedJsonOptions.CamelCaseIndented);
            }
            catch (Exception ex)
            {
                Log.Error($"[GetBugs] failed: {ex.GetType().Name}: {ex.Message}");
                return $"ERROR in GetBugs: {ex.GetType().Name}: {ex.Message}";
            }
        });
    }

    [McpServerTool, Description(
        "Transition a bug to a new status. Appends a new record with the same id " +
        "(append-only — history is preserved). Status must be one of: open|triaged|fixed|wontfix.")]
    public static string UpdateBugStatus(
        [Description("Bug id returned from report_bug (e.g. 'bug-a1b2c3d4e5f6')")] string bugId,
        [Description("New status: open|triaged|fixed|wontfix")] string status,
        [Description("Optional: notes for the transition (resolution, triage rationale, etc.)")] string? notes = null,
        [Description("Optional: namespace/session to write to (default: server default)")] string? ns = null)
    {
        return Telemetry.Track("update_bug_status", ns, new { bugId, status, ns }, () =>
        {
            Metrics.Increment(Metrics.ToolUpdateBugStatus);
            Log.Debug($"[UpdateBugStatus] id='{bugId}' status='{status}' ns='{ns ?? "(default)"}'");
            try
            {
                if (string.IsNullOrWhiteSpace(bugId))
                    return "ERROR in UpdateBugStatus: bugId is required.";

                var newStatus = (status ?? "").Trim().ToLowerInvariant();
                if (!AllowedStatuses.Contains(newStatus))
                    return $"ERROR in UpdateBugStatus: status must be one of {string.Join("|", AllowedStatuses)} (got '{status}').";

                var stores = StoreRegistry.ForNamespace(ns);
                if (!stores.Bugs.HasData())
                    return $"ERROR in UpdateBugStatus: no bugs recorded — '{bugId}' not found.";

                var latest = BuildLatest(stores.Bugs.LoadAll());
                if (!latest.TryGetValue(bugId, out var existing))
                    return $"ERROR in UpdateBugStatus: bug '{bugId}' not found.";

                var transition = new BugReport
                {
                    SchemaVersion = existing.SchemaVersion,
                    Id = existing.Id,
                    Class = existing.Class,
                    Method = existing.Method,
                    Severity = existing.Severity,
                    Description = existing.Description,
                    Repro = existing.Repro,
                    FoundInTestName = existing.FoundInTestName,
                    Status = newStatus,
                    StatusNotes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim(),
                    Model = existing.Model,
                    SessionId = Telemetry.SessionId,
                    TaskId = Telemetry.ActiveTaskId,
                    CreatedAt = existing.CreatedAt,
                    UpdatedAt = DateTime.UtcNow.ToString("O")
                };

                stores.Bugs.Append(transition);
                Log.Debug($"[UpdateBugStatus] {bugId}: {existing.Status} → {newStatus}");

                return JsonSerializer.Serialize(
                    new
                    {
                        ok = true,
                        id = bugId,
                        previousStatus = existing.Status,
                        newStatus,
                        @class = existing.Class,
                        method = existing.Method
                    },
                    SharedJsonOptions.CamelCaseIndented);
            }
            catch (Exception ex)
            {
                Log.Error($"[UpdateBugStatus] failed for '{bugId}': {ex.GetType().Name}: {ex.Message}");
                return $"ERROR in UpdateBugStatus: {ex.GetType().Name}: {ex.Message}";
            }
        });
    }

    // ── Helpers ──

    /// <summary>
    /// Collapse the append-only bug log to one record per id (latest wins).
    /// Mirrors <c>AssessmentLookup.BuildLatest</c>; kept local because the
    /// dedupe key differs (id vs class).
    /// </summary>
    internal static Dictionary<string, BugReport> BuildLatest(List<BugReport> all)
    {
        var latest = new Dictionary<string, BugReport>(StringComparer.Ordinal);
        foreach (var b in all)
        {
            if (string.IsNullOrEmpty(b.Id)) continue;
            latest[b.Id] = b;
        }
        return latest;
    }

    internal static int SeverityRank(string severity) => severity.ToLowerInvariant() switch
    {
        "critical" => 0,
        "high" => 1,
        "medium" => 2,
        "low" => 3,
        _ => 4
    };

    private static string RandomHex(int length)
    {
        var bytes = new byte[(length + 1) / 2];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToHexString(bytes).ToLowerInvariant()[..length];
    }
}
