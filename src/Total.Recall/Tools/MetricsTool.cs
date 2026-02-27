using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using Total.Recall.Infrastructure;

namespace Total.Recall.Tools;

[McpServerToolType]
public static class MetricsTool
{
    [McpServerTool, Description(
        "Get server telemetry: tool call counts, cache hit/miss rates, type lookup strategy distribution, " +
        "and uptime. Resets on server restart. Use to monitor MCP server health and effectiveness.")]
    public static string GetMetrics()
    {
        Metrics.Increment(Metrics.ToolGetMetrics);
        try
        {
            var counters = Metrics.GetAll();
            var uptime = Metrics.Uptime;

            // Calculate derived metrics
            var cacheHits = Metrics.Get(Metrics.CacheHit);
            var cacheMisses = Metrics.Get(Metrics.CacheMiss);
            var cacheTotal = cacheHits + cacheMisses;
            var cacheHitRate = cacheTotal > 0 ? Math.Round((double)cacheHits / cacheTotal * 100, 1) : 0.0;

            var totalToolCalls = counters
                .Where(kv => kv.Key.StartsWith("tool."))
                .Sum(kv => kv.Value);

            var result = new
            {
                uptime = new
                {
                    hours = Math.Round(uptime.TotalHours, 2),
                    minutes = Math.Round(uptime.TotalMinutes, 1),
                    startedUtc = Metrics.StartedUtc.ToString("yyyy-MM-dd HH:mm:ss")
                },
                totalToolCalls,
                cache = new
                {
                    hits = cacheHits,
                    misses = cacheMisses,
                    reloads = Metrics.Get(Metrics.CacheReload),
                    hitRate = $"{cacheHitRate}%"
                },
                typeIndex = new
                {
                    hits = Metrics.Get(Metrics.TypeIndexHit),
                    rebuilds = Metrics.Get(Metrics.TypeIndexRebuild)
                },
                lookupStrategy = new
                {
                    exact = Metrics.Get(Metrics.LookupExact),
                    caseInsensitive = Metrics.Get(Metrics.LookupCaseInsensitive),
                    contains = Metrics.Get(Metrics.LookupContains),
                    @interface = Metrics.Get(Metrics.LookupInterface),
                    @namespace = Metrics.Get(Metrics.LookupNamespace),
                    miss = Metrics.Get(Metrics.LookupMiss)
                },
                tools = counters
                    .Where(kv => kv.Key.StartsWith("tool."))
                    .OrderByDescending(kv => kv.Value)
                    .ToDictionary(kv => kv.Key, kv => kv.Value)
            };

            return JsonSerializer.Serialize(result, SharedJsonOptions.CamelCaseIndented);
        }
        catch (Exception ex)
        {
            Log.Error($"[GetMetrics] failed: {ex.GetType().Name}: {ex.Message}");
            return $"ERROR in GetMetrics: {ex.GetType().Name}: {ex.Message}";
        }
    }
}
