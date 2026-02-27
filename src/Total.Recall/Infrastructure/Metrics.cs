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
    public const string ToolGetMetrics = "tool.get_metrics";

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
}
