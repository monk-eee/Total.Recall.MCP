using Total.Recall.Infrastructure;
using Total.Recall.Models;
using Total.Recall.Tools;

namespace Total.Recall.Tests.Tools;

[Collection("ToolTests")]
public sealed class CoverageGapsToolTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string? _originalEnv;

    public CoverageGapsToolTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "total-recall-tests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempDir);
        _originalEnv = Environment.GetEnvironmentVariable(RepoConfig.EnvVarName);
        Environment.SetEnvironmentVariable(RepoConfig.EnvVarName, _tempDir);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(RepoConfig.EnvVarName, _originalEnv);
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private void SeedCoverageGaps(params CoverageGap[] records)
    {
        var store = new JsonLineStore<CoverageGap>(RepoConfig.CoverageGapsPath(_tempDir));
        store.WriteAll(records);
    }

    [Fact]
    public void GetCoverageGaps_NoData_ReturnsNotFoundMessage()
    {
        var result = CoverageGapsTool.GetCoverageGaps();

        Assert.Contains("No coverage data found", result);
    }

    [Fact]
    public void GetCoverageGaps_ReturnsTopNByUncoveredLines()
    {
        SeedCoverageGaps(
            new CoverageGap { Class = "Small", UncoveredLines = 5 },
            new CoverageGap { Class = "Big", UncoveredLines = 50 },
            new CoverageGap { Class = "Medium", UncoveredLines = 20 }
        );

        var result = CoverageGapsTool.GetCoverageGaps(top: 2);

        Assert.Contains("Big", result);
        Assert.Contains("Medium", result);
        Assert.DoesNotContain("Small", result);
    }

    [Fact]
    public void GetCoverageGaps_SkipUntestable_FiltersOutSkippedClasses()
    {
        SeedCoverageGaps(
            new CoverageGap { Class = "Testable", UncoveredLines = 30, SkipReason = null },
            new CoverageGap { Class = "Untestable", UncoveredLines = 100, SkipReason = "Creates HttpClient internally" }
        );

        var result = CoverageGapsTool.GetCoverageGaps(skipUntestable: true);

        Assert.Contains("Testable", result);
        Assert.DoesNotContain("Untestable", result);
    }

    [Fact]
    public void GetCoverageGaps_SkipUntestableFalse_IncludesSkippedClasses()
    {
        SeedCoverageGaps(
            new CoverageGap { Class = "Testable", UncoveredLines = 30, SkipReason = null },
            new CoverageGap { Class = "Untestable", UncoveredLines = 100, SkipReason = "Creates HttpClient internally" }
        );

        var result = CoverageGapsTool.GetCoverageGaps(skipUntestable: false);

        Assert.Contains("Testable", result);
        Assert.Contains("Untestable", result);
    }

    [Fact]
    public void GetCoverageGaps_DefaultTop_ReturnsTwenty()
    {
        var records = Enumerable.Range(1, 25)
            .Select(i => new CoverageGap { Class = $"Class{i}", UncoveredLines = i })
            .ToArray();
        SeedCoverageGaps(records);

        var result = CoverageGapsTool.GetCoverageGaps();

        // Default top is 20, so Class1-5 (lowest uncovered) shouldn't appear
        Assert.Contains("Class25", result);
        Assert.Contains("Class6", result);
        Assert.DoesNotContain("\"Class5\"", result); // exact match avoids Class25 containing "5"
    }

    // --- sortBy parameter tests ---

    [Fact]
    public void GetCoverageGaps_SortByUncovered_OrdersByUncoveredLinesDescending()
    {
        SeedCoverageGaps(
            new CoverageGap { Class = "Small", UncoveredLines = 5, Testability = "high" },
            new CoverageGap { Class = "Big", UncoveredLines = 50, Testability = "high" },
            new CoverageGap { Class = "Medium", UncoveredLines = 20, Testability = "high" }
        );

        var result = CoverageGapsTool.GetCoverageGaps(sortBy: "uncovered");

        // Big (50) should appear before Medium (20) should appear before Small (5)
        var bigIdx = result.IndexOf("Big");
        var medIdx = result.IndexOf("Medium");
        var smallIdx = result.IndexOf("Small");
        Assert.True(bigIdx < medIdx, "Big should come before Medium");
        Assert.True(medIdx < smallIdx, "Medium should come before Small");
    }

    [Fact]
    public void GetCoverageGaps_SortByCoverage_OrdersByCoveragePercentAscending()
    {
        SeedCoverageGaps(
            new CoverageGap { Class = "MostCovered", CoveragePercent = 90, UncoveredLines = 5 },
            new CoverageGap { Class = "LeastCovered", CoveragePercent = 10, UncoveredLines = 50 },
            new CoverageGap { Class = "HalfCovered", CoveragePercent = 50, UncoveredLines = 20 }
        );

        var result = CoverageGapsTool.GetCoverageGaps(sortBy: "coverage");

        // LeastCovered (10%) should appear before HalfCovered (50%) before MostCovered (90%)
        var leastIdx = result.IndexOf("LeastCovered");
        var halfIdx = result.IndexOf("HalfCovered");
        var mostIdx = result.IndexOf("MostCovered");
        Assert.True(leastIdx < halfIdx, "LeastCovered should come before HalfCovered");
        Assert.True(halfIdx < mostIdx, "HalfCovered should come before MostCovered");
    }

    [Fact]
    public void GetCoverageGaps_SortByRoi_DefaultSortUsesRoiScore()
    {
        SeedCoverageGaps(
            // ROI = 10 * 1.0 / (1+0) = 10.0
            new CoverageGap { Class = "HighRoi", UncoveredLines = 10, Testability = "high", ExistingTestCount = 0 },
            // ROI = 100 * 0.3 / (1+0) = 30.0
            new CoverageGap { Class = "LowTestability", UncoveredLines = 100, Testability = "low", ExistingTestCount = 0 },
            // ROI = 50 * 0.7 / (1+5) = 5.83
            new CoverageGap { Class = "ManyTests", UncoveredLines = 50, Testability = "medium", ExistingTestCount = 5 }
        );

        var result = CoverageGapsTool.GetCoverageGaps(sortBy: "roi");

        // LowTestability (30.0) > HighRoi (10.0) > ManyTests (5.83)
        var lowTestIdx = result.IndexOf("LowTestability");
        var highRoiIdx = result.IndexOf("HighRoi");
        var manyTestsIdx = result.IndexOf("ManyTests");
        Assert.True(lowTestIdx < highRoiIdx, "LowTestability (ROI=30) should come before HighRoi (ROI=10)");
        Assert.True(highRoiIdx < manyTestsIdx, "HighRoi (ROI=10) should come before ManyTests (ROI=5.83)");
    }

    [Fact]
    public void GetCoverageGaps_RoiScore_UnknownTestability_Uses05Multiplier()
    {
        SeedCoverageGaps(
            // ROI = 20 * 0.5 / (1+0) = 10.0 (unknown testability)
            new CoverageGap { Class = "Unknown", UncoveredLines = 20, ExistingTestCount = 0 },
            // ROI = 20 * 1.0 / (1+0) = 20.0 (high testability)
            new CoverageGap { Class = "High", UncoveredLines = 20, Testability = "high", ExistingTestCount = 0 }
        );

        var result = CoverageGapsTool.GetCoverageGaps(sortBy: "roi");

        // High (20.0) should come before Unknown (10.0)
        var highIdx = result.IndexOf("High");
        var unknownIdx = result.IndexOf("Unknown");
        Assert.True(highIdx < unknownIdx, "High testability should rank higher than unknown");
    }

    [Fact]
    public void GetCoverageGaps_ResultIncludesRoiScoreField()
    {
        SeedCoverageGaps(
            new CoverageGap { Class = "TestClass", UncoveredLines = 20, Testability = "high", ExistingTestCount = 0 }
        );

        var result = CoverageGapsTool.GetCoverageGaps();

        Assert.Contains("roiScore", result);
    }
}
