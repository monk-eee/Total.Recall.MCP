using Total.Recall.Infrastructure;

namespace Total.Recall.Tests.Infrastructure;

[Collection("ToolTests")]
public sealed class MetricsTests : IDisposable
{
    public MetricsTests()
    {
        Metrics.Reset();
    }

    public void Dispose()
    {
        Metrics.Reset();
    }

    [Fact]
    public void Increment_NewCounter_StartsAtOne()
    {
        Metrics.Increment("test.counter");

        Assert.Equal(1, Metrics.Get("test.counter"));
    }

    [Fact]
    public void Increment_ExistingCounter_Increments()
    {
        Metrics.Increment("test.counter");
        Metrics.Increment("test.counter");
        Metrics.Increment("test.counter");

        Assert.Equal(3, Metrics.Get("test.counter"));
    }

    [Fact]
    public void Get_NonExistentCounter_ReturnsZero()
    {
        Assert.Equal(0, Metrics.Get("does.not.exist"));
    }

    [Fact]
    public void GetAll_ReturnsAllCounters()
    {
        Metrics.Increment("a");
        Metrics.Increment("b");
        Metrics.Increment("a");

        var all = Metrics.GetAll();

        Assert.True(all.ContainsKey("a"));
        Assert.True(all.ContainsKey("b"));
        Assert.Equal(2, all["a"]);
        Assert.Equal(1, all["b"]);
    }

    [Fact]
    public void Reset_ClearsAllCounters()
    {
        Metrics.Increment("x");
        Metrics.Increment("y");

        Metrics.Reset();

        Assert.Equal(0, Metrics.Get("x"));
        Assert.Equal(0, Metrics.Get("y"));
        Assert.Empty(Metrics.GetAll());
    }

    [Fact]
    public void StartedUtc_IsBeforeNow()
    {
        Assert.True(Metrics.StartedUtc <= DateTime.UtcNow);
    }

    [Fact]
    public void Uptime_IsNonNegative()
    {
        Assert.True(Metrics.Uptime.TotalMilliseconds >= 0);
    }

    [Fact]
    public void WellKnownCounters_AreDefinedCorrectly()
    {
        // Verify the counter names match expected patterns
        Assert.StartsWith("tool.", Metrics.ToolResolveType);
        Assert.StartsWith("tool.", Metrics.ToolGetContext);
        Assert.StartsWith("tool.", Metrics.ToolGetCoverageGaps);
        Assert.StartsWith("tool.", Metrics.ToolGetGotchas);
        Assert.StartsWith("tool.", Metrics.ToolAddGotcha);
        Assert.StartsWith("tool.", Metrics.ToolGetMockRecipe);
        Assert.StartsWith("tool.", Metrics.ToolGetTestInventory);
        Assert.StartsWith("tool.", Metrics.ToolAddAssessment);
        Assert.StartsWith("tool.", Metrics.ToolGetAssessments);
        Assert.StartsWith("tool.", Metrics.ToolGetMetrics);

        Assert.StartsWith("cache.", Metrics.CacheHit);
        Assert.StartsWith("cache.", Metrics.CacheMiss);
        Assert.StartsWith("cache.", Metrics.CacheReload);

        Assert.StartsWith("typeindex.", Metrics.TypeIndexHit);
        Assert.StartsWith("typeindex.", Metrics.TypeIndexRebuild);

        Assert.StartsWith("lookup.", Metrics.LookupExact);
        Assert.StartsWith("lookup.", Metrics.LookupCaseInsensitive);
        Assert.StartsWith("lookup.", Metrics.LookupContains);
        Assert.StartsWith("lookup.", Metrics.LookupInterface);
        Assert.StartsWith("lookup.", Metrics.LookupNamespace);
        Assert.StartsWith("lookup.", Metrics.LookupMiss);
    }
}
