using System.Collections.Concurrent;

namespace Total.Recall.Infrastructure;

/// <summary>
/// In-memory telemetry counters for the MCP server process.
/// Thread-safe, zero-allocation increment path. Not persisted — resets on server restart.
/// Query via the get_metrics MCP tool.
/// </summary>
public static class Metrics
{
    private static readonly ConcurrentDictionary<string, long> s_counters = new();
    private static readonly DateTime s_startedUtc = DateTime.UtcNow;

    /// <summary>
    /// Increment a named counter by 1. Creates the counter if it doesn't exist.
    /// </summary>
    public static void Increment(string name)
    {
        s_counters.AddOrUpdate(name, 1, (_, v) => v + 1);
    }

    /// <summary>
    /// Get the current value of a counter. Returns 0 if not found.
    /// </summary>
    public static long Get(string name)
    {
        return s_counters.GetValueOrDefault(name, 0);
    }

    /// <summary>
    /// Get all counters as a snapshot dictionary.
    /// </summary>
    public static Dictionary<string, long> GetAll()
    {
        return new Dictionary<string, long>(s_counters);
    }

    /// <summary>
    /// Server process start time (UTC).
    /// </summary>
    public static DateTime StartedUtc => s_startedUtc;

    /// <summary>
    /// How long the server has been running.
    /// </summary>
    public static TimeSpan Uptime => DateTime.UtcNow - s_startedUtc;

    /// <summary>
    /// Reset all counters. Used by tests.
    /// </summary>
    internal static void Reset()
    {
        s_counters.Clear();
    }

    // ── Well-known counter names ──

    // Tool invocations
    public const string ToolResolveType = "tool.resolve_type";
    public const string ToolGetContext = "tool.get_context";
    public const string ToolGetCoverageGaps = "tool.get_coverage_gaps";
    public const string ToolGetGotchas = "tool.get_gotchas";
    public const string ToolAddGotcha = "tool.add_gotcha";
    public const string ToolGetMockRecipe = "tool.get_mock_recipe";
    public const string ToolGetTestInventory = "tool.get_test_inventory";
    public const string ToolAddAssessment = "tool.add_assessment";
    public const string ToolGetAssessments = "tool.get_assessments";
    public const string ToolReportBug = "tool.report_bug";
    public const string ToolGetBugs = "tool.get_bugs";
    public const string ToolUpdateBugStatus = "tool.update_bug_status";
    public const string ToolGetMetrics = "tool.get_metrics";
    public const string ToolGetTestableTargets = "tool.get_testable_targets";
    public const string ToolGetSourceSnippet = "tool.get_source_snippet";
    public const string ToolGenerateTestScaffold = "tool.generate_test_scaffold";
    public const string ToolLogSession = "tool.log_session";
    public const string ToolGetSessions = "tool.get_sessions";
    public const string ToolGetUncoveredMethods = "tool.get_uncovered_methods";
    public const string ToolGetStubClasses = "tool.get_stub_classes";
    public const string ToolGetGotchaInsights = "tool.get_gotcha_insights";
    public const string ToolLearnTestPatterns = "tool.learn_test_patterns";
    public const string ToolRefreshCoverage = "tool.refresh_coverage";

    // Cache behavior
    public const string CacheHit = "cache.hit";
    public const string CacheMiss = "cache.miss";
    public const string CacheReload = "cache.reload";

    // Type index
    public const string TypeIndexHit = "typeindex.hit";
    public const string TypeIndexRebuild = "typeindex.rebuild";

    // Lookup strategy (which path resolved the type)
    public const string LookupExact = "lookup.exact";
    public const string LookupCaseInsensitive = "lookup.case_insensitive";
    public const string LookupContains = "lookup.contains";
    public const string LookupInterface = "lookup.interface";
    public const string LookupNamespace = "lookup.namespace";
    public const string LookupMiss = "lookup.miss";

    // Telemetry harness (Cut 1+)
    public const string ToolCallsRecorded = "telemetry.tool_calls_recorded";
    public const string ToolCallsRepeat = "telemetry.tool_calls_repeat";
    public const string CyclesDetected = "telemetry.cycles_detected";
    public const string TasksStarted = "telemetry.tasks_started";
    public const string TasksEnded = "telemetry.tasks_ended";
    public const string ChallengesGraded = "telemetry.challenges_graded";
    public const string ContextResetsReported = "telemetry.context_resets_reported";
}
