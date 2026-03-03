using System.Text.Json;
using Total.Recall.Infrastructure;
using Total.Recall.Models;
using Total.Recall.Tools;

namespace Total.Recall.Tests.Tools;

/// <summary>
/// Tests for GotchaInsightsTool.GetGotchaInsights — clusters gotchas by pattern,
/// generates category distributions, identifies hot types, and produces AGENTS.md footguns.
/// </summary>
[Collection("ToolTests")]
public sealed class GotchaInsightsToolTests : ToolTestBase
{

    // ── No data / insufficient data ──

    [Fact]
    public void GetGotchaInsights_NoData_ReturnsNoGotchasMessage()
    {
        var result = GotchaInsightsTool.GetGotchaInsights();
        Assert.Contains("No gotchas recorded", result);
    }

    [Fact]
    public void GetGotchaInsights_OnlyOneGotcha_ReturnsTooFewMessage()
    {
        SeedGotchas(new Gotcha { Type = "Foo", Category = "bug", Description = "only one", Date = "2025-01-01" });

        var result = GotchaInsightsTool.GetGotchaInsights();
        Assert.Contains("Only 1 gotcha", result);
    }

    [Fact]
    public void GetGotchaInsights_TwoGotchas_ReturnsTooFewMessage()
    {
        SeedGotchas(
            new Gotcha { Type = "Foo", Category = "bug", Description = "first", Date = "2025-01-01" },
            new Gotcha { Type = "Bar", Category = "enum", Description = "second", Date = "2025-01-02" }
        );

        var result = GotchaInsightsTool.GetGotchaInsights();
        Assert.Contains("Only 2 gotcha", result);
    }

    // ── Clustering ──

    [Fact]
    public void GetGotchaInsights_EnumCluster_DetectsMoqPattern()
    {
        SeedGotchas(
            new Gotcha { Type = "ClassA", Category = "mock", Description = "CS0854 on optional param", Date = "2025-01-01" },
            new Gotcha { Type = "ClassB", Category = "mock", Description = "expression tree limitation with default param", Date = "2025-01-02" },
            new Gotcha { Type = "ClassC", Category = "mock", Description = "CS0854 prevents Moq Setup", Date = "2025-01-03" },
            new Gotcha { Type = "ClassD", Category = "other", Description = "unrelated issue", Date = "2025-01-04" }
        );

        var result = GotchaInsightsTool.GetGotchaInsights();
        var doc = JsonDocument.Parse(result);
        var root = doc.RootElement;

        Assert.Equal(4, root.GetProperty("totalGotchas").GetInt32());
        Assert.True(root.GetProperty("clusteredCount").GetInt32() >= 2);

        var clusters = root.GetProperty("clusters");
        Assert.True(clusters.GetArrayLength() > 0);

        // Should have a "Moq Expression Tree Limitations" cluster
        var clusterNames = clusters.EnumerateArray()
            .Select(c => c.GetProperty("cluster").GetString()!)
            .ToList();
        Assert.Contains(clusterNames, n => n.Contains("Moq"));
    }

    [Fact]
    public void GetGotchaInsights_ConstructorCluster_DetectsCtorPattern()
    {
        SeedGotchas(
            new Gotcha { Type = "A", Category = "constructor", Description = "ctor leaves property null", Date = "2025-01-01" },
            new Gotcha { Type = "B", Category = "init", Description = "parameterless constructor missing", Date = "2025-01-02" },
            new Gotcha { Type = "C", Category = "bug", Description = "NRE in ctor when null passed", Date = "2025-01-03" }
        );

        var result = GotchaInsightsTool.GetGotchaInsights();
        var doc = JsonDocument.Parse(result);
        var clusters = doc.RootElement.GetProperty("clusters");

        var clusterNames = clusters.EnumerateArray()
            .Select(c => c.GetProperty("cluster").GetString()!)
            .ToList();
        Assert.Contains(clusterNames, n => n.Contains("Constructor"));
    }

    // ── Category distribution ──

    [Fact]
    public void GetGotchaInsights_CategoryDistribution_GroupsCorrectly()
    {
        SeedGotchas(
            new Gotcha { Type = "A", Category = "mock", Description = "mock issue 1", Date = "2025-01-01" },
            new Gotcha { Type = "B", Category = "mock", Description = "mock issue 2", Date = "2025-01-02" },
            new Gotcha { Type = "C", Category = "enum", Description = "enum issue", Date = "2025-01-03" },
            new Gotcha { Type = "D", Category = "bug", Description = "dead code found", Date = "2025-01-04" }
        );

        var result = GotchaInsightsTool.GetGotchaInsights();
        var doc = JsonDocument.Parse(result);

        var dist = doc.RootElement.GetProperty("categoryDistribution");
        Assert.True(dist.GetArrayLength() >= 2);

        // "mock" should be first (most frequent)
        var first = dist[0];
        Assert.Equal("mock", first.GetProperty("category").GetString());
        Assert.Equal(2, first.GetProperty("count").GetInt32());
    }

    // ── Hot types ──

    [Fact]
    public void GetGotchaInsights_HotTypes_IdentifiesTypesWithMultipleGotchas()
    {
        SeedGotchas(
            new Gotcha { Type = "HotClass", Category = "mock", Description = "issue 1", Date = "2025-01-01" },
            new Gotcha { Type = "HotClass", Category = "enum", Description = "issue 2", Date = "2025-01-02" },
            new Gotcha { Type = "HotClass", Category = "bug", Description = "issue 3", Date = "2025-01-03" },
            new Gotcha { Type = "SingleClass", Category = "bug", Description = "only one", Date = "2025-01-04" }
        );

        var result = GotchaInsightsTool.GetGotchaInsights();
        var doc = JsonDocument.Parse(result);

        var hotTypes = doc.RootElement.GetProperty("hotTypes");
        Assert.True(hotTypes.GetArrayLength() >= 1);

        var first = hotTypes[0];
        Assert.Equal("HotClass", first.GetProperty("type").GetString());
        Assert.Equal(3, first.GetProperty("count").GetInt32());
    }

    // ── Footguns markdown ──

    [Fact]
    public void GetGotchaInsights_GenerateFootguns_IncludesMarkdown()
    {
        SeedGotchas(
            new Gotcha { Type = "X", Category = "mock", Description = "CS0854 expression tree error", Date = "2025-01-01" },
            new Gotcha { Type = "Y", Category = "mock", Description = "CS0854 with optional param", Date = "2025-01-02" },
            new Gotcha { Type = "Z", Category = "other", Description = "unrelated thing", Date = "2025-01-03" }
        );

        var result = GotchaInsightsTool.GetGotchaInsights(generateFootguns: true);
        var doc = JsonDocument.Parse(result);

        var md = doc.RootElement.GetProperty("footgunsMarkdown").GetString();
        Assert.NotNull(md);
        Assert.Contains("## Footguns", md);
        Assert.Contains("Moq Expression Tree Limitations", md);
        Assert.Contains("2 occurrences", md);
    }

    [Fact]
    public void GetGotchaInsights_GenerateFootgunsFalse_NoMarkdown()
    {
        SeedGotchas(
            new Gotcha { Type = "X", Category = "mock", Description = "CS0854 expression tree error", Date = "2025-01-01" },
            new Gotcha { Type = "Y", Category = "mock", Description = "CS0854 with optional param", Date = "2025-01-02" },
            new Gotcha { Type = "Z", Category = "other", Description = "unrelated thing", Date = "2025-01-03" }
        );

        var result = GotchaInsightsTool.GetGotchaInsights(generateFootguns: false);
        var doc = JsonDocument.Parse(result);

        Assert.Equal(JsonValueKind.Null, doc.RootElement.GetProperty("footgunsMarkdown").ValueKind);
    }

    // ── MinClusterSize filter ──

    [Fact]
    public void GetGotchaInsights_HighMinClusterSize_ReducesClusters()
    {
        SeedGotchas(
            new Gotcha { Type = "A", Category = "mock", Description = "CS0854 issue", Date = "2025-01-01" },
            new Gotcha { Type = "B", Category = "mock", Description = "expression tree issue", Date = "2025-01-02" },
            new Gotcha { Type = "C", Category = "other", Description = "something else", Date = "2025-01-03" }
        );

        // With minClusterSize=2, the mock cluster (2 matches) should appear
        var result2 = GotchaInsightsTool.GetGotchaInsights(minClusterSize: 2);
        var doc2 = JsonDocument.Parse(result2);
        var clusters2 = doc2.RootElement.GetProperty("clusters").GetArrayLength();

        // With minClusterSize=5, the mock cluster (2 matches) should NOT appear
        StoreRegistry.Reset();
        SeedGotchas(
            new Gotcha { Type = "A", Category = "mock", Description = "CS0854 issue", Date = "2025-01-01" },
            new Gotcha { Type = "B", Category = "mock", Description = "expression tree issue", Date = "2025-01-02" },
            new Gotcha { Type = "C", Category = "other", Description = "something else", Date = "2025-01-03" }
        );
        var result5 = GotchaInsightsTool.GetGotchaInsights(minClusterSize: 5);
        var doc5 = JsonDocument.Parse(result5);
        var clusters5 = doc5.RootElement.GetProperty("clusters").GetArrayLength();

        Assert.True(clusters2 >= clusters5);
    }

    // ── Unclustered gotchas ──

    [Fact]
    public void GetGotchaInsights_UnclusteredGotchas_AreIncluded()
    {
        SeedGotchas(
            new Gotcha { Type = "A", Category = "unique", Description = "very specific weird issue xyz987", Date = "2025-01-01" },
            new Gotcha { Type = "B", Category = "unique", Description = "another unique problem abc123", Date = "2025-01-02" },
            new Gotcha { Type = "C", Category = "unique", Description = "third unique thing def456", Date = "2025-01-03" }
        );

        var result = GotchaInsightsTool.GetGotchaInsights();
        var doc = JsonDocument.Parse(result);

        // These gotchas shouldn't match any cluster keywords
        Assert.True(doc.RootElement.GetProperty("unclusteredCount").GetInt32() > 0);
        var unclustered = doc.RootElement.GetProperty("unclusteredGotchas");
        Assert.True(unclustered.GetArrayLength() > 0);
    }

    // ── Error handling ──

    [Fact]
    public void GetGotchaInsights_InvalidNamespace_ReturnsError()
    {
        var result = GotchaInsightsTool.GetGotchaInsights(ns: "\0");
        Assert.StartsWith("ERROR in GetGotchaInsights", result);
    }

    // ── Multiple clusters ──

    [Fact]
    public void GetGotchaInsights_MultiplePatterns_DetectsMultipleClusters()
    {
        SeedGotchas(
            // Mock cluster
            new Gotcha { Type = "A", Category = "mock", Description = "CS0854 on method", Date = "2025-01-01" },
            new Gotcha { Type = "B", Category = "mock", Description = "expression tree limit", Date = "2025-01-02" },
            // Enum cluster
            new Gotcha { Type = "C", Category = "enum", Description = "enum member name mismatch", Date = "2025-01-03" },
            new Gotcha { Type = "D", Category = "enum", Description = "enum default(T) is wrong value 0", Date = "2025-01-04" },
            // Filler (to reach 5)
            new Gotcha { Type = "E", Category = "other", Description = "unrelated", Date = "2025-01-05" }
        );

        var result = GotchaInsightsTool.GetGotchaInsights();
        var doc = JsonDocument.Parse(result);

        var clusters = doc.RootElement.GetProperty("clusters");
        Assert.True(clusters.GetArrayLength() >= 2, $"Expected at least 2 clusters, got {clusters.GetArrayLength()}");
    }

    // ── AffectedTypes in cluster ──

    [Fact]
    public void GetGotchaInsights_ClusterAffectedTypes_ListsDistinctTypes()
    {
        SeedGotchas(
            new Gotcha { Type = "ClassA", Category = "mock", Description = "CS0854 problem", Date = "2025-01-01" },
            new Gotcha { Type = "ClassA", Category = "mock", Description = "another expression tree issue", Date = "2025-01-02" },
            new Gotcha { Type = "ClassB", Category = "mock", Description = "CS0854 again", Date = "2025-01-03" }
        );

        var result = GotchaInsightsTool.GetGotchaInsights();
        var doc = JsonDocument.Parse(result);

        var cluster = doc.RootElement.GetProperty("clusters")[0];
        var affectedTypes = cluster.GetProperty("affectedTypes").EnumerateArray()
            .Select(t => t.GetString()!)
            .ToList();

        // Should have ClassA and ClassB
        Assert.Contains("ClassA", affectedTypes);
        Assert.Contains("ClassB", affectedTypes);
        Assert.Equal(2, affectedTypes.Count); // distinct
    }
}
