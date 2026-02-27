using System.Text.Json;
using Total.Recall.Infrastructure;
using Total.Recall.Tools;

namespace Total.Recall.Tests.Tools;

[Collection("ToolTests")]
public sealed class MetricsToolTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string? _originalEnv;

    public MetricsToolTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "total-recall-tests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempDir);
        _originalEnv = Environment.GetEnvironmentVariable(RepoConfig.EnvVarName);
        Environment.SetEnvironmentVariable(RepoConfig.EnvVarName, _tempDir);
        StoreRegistry.Reset();
        Metrics.Reset();
    }

    public void Dispose()
    {
        StoreRegistry.Reset();
        Environment.SetEnvironmentVariable(RepoConfig.EnvVarName, _originalEnv);
        Metrics.Reset();
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public void GetMetrics_ReturnsValidJson()
    {
        var result = MetricsTool.GetMetrics();

        var doc = JsonDocument.Parse(result);
        Assert.True(doc.RootElement.TryGetProperty("uptime", out _));
        Assert.True(doc.RootElement.TryGetProperty("cache", out _));
        Assert.True(doc.RootElement.TryGetProperty("typeIndex", out _));
        Assert.True(doc.RootElement.TryGetProperty("lookupStrategy", out _));
        Assert.True(doc.RootElement.TryGetProperty("tools", out _));
    }

    [Fact]
    public void GetMetrics_IncludesUptime()
    {
        var result = MetricsTool.GetMetrics();

        var doc = JsonDocument.Parse(result);
        var uptime = doc.RootElement.GetProperty("uptime");
        Assert.True(uptime.GetProperty("hours").GetDouble() >= 0);
        Assert.True(uptime.GetProperty("minutes").GetDouble() >= 0);
        Assert.False(string.IsNullOrEmpty(uptime.GetProperty("startedUtc").GetString()));
    }

    [Fact]
    public void GetMetrics_TracksTotalToolCalls()
    {
        Metrics.Increment(Metrics.ToolResolveType);
        Metrics.Increment(Metrics.ToolResolveType);
        Metrics.Increment(Metrics.ToolGetContext);

        var result = MetricsTool.GetMetrics();

        var doc = JsonDocument.Parse(result);
        // 3 manual increments + 1 from GetMetrics itself
        Assert.Equal(4, doc.RootElement.GetProperty("totalToolCalls").GetInt64());
    }

    [Fact]
    public void GetMetrics_CacheHitRate_ZeroWhenNoActivity()
    {
        var result = MetricsTool.GetMetrics();

        var doc = JsonDocument.Parse(result);
        Assert.Equal("0%", doc.RootElement.GetProperty("cache").GetProperty("hitRate").GetString());
    }

    [Fact]
    public void GetMetrics_CacheHitRate_CalculatesCorrectly()
    {
        Metrics.Increment(Metrics.CacheHit);
        Metrics.Increment(Metrics.CacheHit);
        Metrics.Increment(Metrics.CacheHit);
        Metrics.Increment(Metrics.CacheMiss);

        var result = MetricsTool.GetMetrics();

        var doc = JsonDocument.Parse(result);
        Assert.Equal("75%", doc.RootElement.GetProperty("cache").GetProperty("hitRate").GetString());
    }

    [Fact]
    public void GetMetrics_LookupStrategyCounts()
    {
        Metrics.Increment(Metrics.LookupExact);
        Metrics.Increment(Metrics.LookupExact);
        Metrics.Increment(Metrics.LookupContains);

        var result = MetricsTool.GetMetrics();

        var doc = JsonDocument.Parse(result);
        var strategy = doc.RootElement.GetProperty("lookupStrategy");
        Assert.Equal(2, strategy.GetProperty("exact").GetInt64());
        Assert.Equal(1, strategy.GetProperty("contains").GetInt64());
        Assert.Equal(0, strategy.GetProperty("caseInsensitive").GetInt64());
    }

    [Fact]
    public void GetMetrics_ToolBreakdown_SortedByFrequency()
    {
        Metrics.Increment(Metrics.ToolGetCoverageGaps);
        Metrics.Increment(Metrics.ToolResolveType);
        Metrics.Increment(Metrics.ToolResolveType);
        Metrics.Increment(Metrics.ToolResolveType);

        var result = MetricsTool.GetMetrics();

        var doc = JsonDocument.Parse(result);
        var tools = doc.RootElement.GetProperty("tools");

        // tool.get_metrics increments too (from the GetMetrics call itself)
        var props = tools.EnumerateObject().ToList();
        // First tool should be highest frequency
        Assert.Equal("tool.resolve_type", props[0].Name);
        Assert.Equal(3, props[0].Value.GetInt64());
    }

    [Fact]
    public void GetMetrics_IncrementsItsOwnCounter()
    {
        Metrics.Reset();

        MetricsTool.GetMetrics();
        MetricsTool.GetMetrics();

        Assert.Equal(2, Metrics.Get(Metrics.ToolGetMetrics));
    }
}
