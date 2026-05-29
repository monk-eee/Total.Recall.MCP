using Total.Recall.Infrastructure;
using Total.Recall.Models;
using Total.Recall.Tools;

namespace Total.Recall.Tests.Tools;

[Collection("ToolTests")]
public sealed class CoverageGapsToolTests : ToolTestBase
{

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
            new CoverageGap { ClassName = "Small", LinesTotal = 5, LinesCovered = 0 },
            new CoverageGap { ClassName = "Big", LinesTotal = 50, LinesCovered = 0 },
            new CoverageGap { ClassName = "Medium", LinesTotal = 20, LinesCovered = 0 }
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
            new CoverageGap { ClassName = "Testable", LinesTotal = 30, LinesCovered = 0 },
            new CoverageGap { ClassName = "Untestable", LinesTotal = 100, LinesCovered = 0, TestabilityScore = 0.1 }
        );

        var result = CoverageGapsTool.GetCoverageGaps(skipUntestable: true);

        Assert.Contains("Testable", result);
        Assert.DoesNotContain("Untestable", result);
    }

    [Fact]
    public void GetCoverageGaps_SkipUntestableFalse_IncludesSkippedClasses()
    {
        SeedCoverageGaps(
            new CoverageGap { ClassName = "Testable", LinesTotal = 30, LinesCovered = 0 },
            new CoverageGap { ClassName = "Untestable", LinesTotal = 100, LinesCovered = 0, TestabilityScore = 0.1 }
        );

        var result = CoverageGapsTool.GetCoverageGaps(skipUntestable: false);

        Assert.Contains("Testable", result);
        Assert.Contains("Untestable", result);
    }

    [Fact]
    public void GetCoverageGaps_DefaultTop_ReturnsTwenty()
    {
        var records = Enumerable.Range(1, 25)
            .Select(i => new CoverageGap { ClassName = $"Class{i}", LinesTotal = i, LinesCovered = 0 })
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
            new CoverageGap { ClassName = "Small", LinesTotal = 5, LinesCovered = 0, TestabilityScore = 0.85 },
            new CoverageGap { ClassName = "Big", LinesTotal = 50, LinesCovered = 0, TestabilityScore = 0.85 },
            new CoverageGap { ClassName = "Medium", LinesTotal = 20, LinesCovered = 0, TestabilityScore = 0.85 }
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
            new CoverageGap { ClassName = "MostCovered", CoveragePercent = 90, LinesTotal = 50, LinesCovered = 45 },
            new CoverageGap { ClassName = "LeastCovered", CoveragePercent = 10, LinesTotal = 100, LinesCovered = 50 },
            new CoverageGap { ClassName = "HalfCovered", CoveragePercent = 50, LinesTotal = 40, LinesCovered = 20 }
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
            // ROI = 10 * 0.85 / (1+0) = 8.5
            new CoverageGap { ClassName = "HighRoi", LinesTotal = 10, LinesCovered = 0, TestabilityScore = 0.85, ExistingTests = 0 },
            // ROI = 100 * 0.3 / (1+0) = 30.0
            new CoverageGap { ClassName = "LowTestability", LinesTotal = 100, LinesCovered = 0, TestabilityScore = 0.3, ExistingTests = 0 },
            // ROI = 50 * 0.55 / (1+5) = 4.58
            new CoverageGap { ClassName = "ManyTests", LinesTotal = 50, LinesCovered = 0, TestabilityScore = 0.55, ExistingTests = 5 }
        );

        var result = CoverageGapsTool.GetCoverageGaps(sortBy: "roi");

        // LowTestability (30.0) > HighRoi (8.5) > ManyTests (4.58)
        var lowTestIdx = result.IndexOf("LowTestability");
        var highRoiIdx = result.IndexOf("HighRoi");
        var manyTestsIdx = result.IndexOf("ManyTests");
        Assert.True(lowTestIdx < highRoiIdx, "LowTestability (ROI=30) should come before HighRoi (ROI=8.5)");
        Assert.True(highRoiIdx < manyTestsIdx, "HighRoi (ROI=8.5) should come before ManyTests (ROI=4.58)");
    }

    [Fact]
    public void GetCoverageGaps_RoiScore_UnknownTestability_Uses05Multiplier()
    {
        SeedCoverageGaps(
            // ROI = 20 * 0.5 / (1+0) = 10.0 (null TestabilityScore defaults to 0.5)
            new CoverageGap { ClassName = "Unknown", LinesTotal = 20, LinesCovered = 0, ExistingTests = 0 },
            // ROI = 20 * 0.85 / (1+0) = 17.0 (high testability)
            new CoverageGap { ClassName = "High", LinesTotal = 20, LinesCovered = 0, TestabilityScore = 0.85, ExistingTests = 0 }
        );

        var result = CoverageGapsTool.GetCoverageGaps(sortBy: "roi");

        // High (17.0) should come before Unknown (10.0)
        var highIdx = result.IndexOf("High");
        var unknownIdx = result.IndexOf("Unknown");
        Assert.True(highIdx < unknownIdx, "High testability should rank higher than unknown");
    }

    [Fact]
    public void GetCoverageGaps_ResultIncludesRoiScoreField()
    {
        SeedCoverageGaps(
            new CoverageGap { ClassName = "TestClass", LinesTotal = 20, LinesCovered = 0, TestabilityScore = 0.85, ExistingTests = 0 }
        );

        var result = CoverageGapsTool.GetCoverageGaps();

        Assert.Contains("roiScore", result);
    }

    // ── Error path coverage ──

    [Fact]
    public void GetCoverageGaps_InvalidNamespace_ReturnsError()
    {
        var result = CoverageGapsTool.GetCoverageGaps(ns: "\0");

        Assert.StartsWith("ERROR in GetCoverageGaps", result);
    }

    // ── summaryOnly parameter ──

    [Fact]
    public void GetCoverageGaps_SummaryOnly_ReturnsCondensedOutput()
    {
        SeedCoverageGaps(
            new CoverageGap
            {
                ClassName = "App.Services.MyService",
                LinesTotal = 100,
                LinesCovered = 70,
                CoveragePercent = 60.5,
                ExistingTests = 2,
                TestabilityScore = 0.85,
                FilePath = "MyService.cs",
                UncoveredMethods = [new UncoveredMethod { Name = "DoWork", UncoveredLines = Enumerable.Range(10, 31).ToArray(), TotalLines = 31 }]
            }
        );

        var result = CoverageGapsTool.GetCoverageGaps(summaryOnly: true);

        // Should contain summary fields (new canonical schema)
        Assert.Contains("\"className\":", result);
        Assert.Contains("\"linesTotal\":", result);
        Assert.Contains("\"linesCovered\":", result);
        Assert.Contains("\"uncoveredLineCount\":", result);
        Assert.Contains("\"coveragePercent\":", result);
        Assert.Contains("\"existingTests\":", result);
        Assert.Contains("\"roiScore\":", result);

        // Should NOT contain detailed-only fields
        Assert.DoesNotContain("\"uncoveredMethods\":", result);
        Assert.DoesNotContain("\"filePath\":", result);
        Assert.DoesNotContain("\"testabilityScore\":", result);
    }

    [Fact]
    public void GetCoverageGaps_SummaryOnlyFalse_ReturnsDetailedOutput()
    {
        SeedCoverageGaps(
            new CoverageGap
            {
                ClassName = "App.DetailService",
                LinesTotal = 50,
                LinesCovered = 30,
                FilePath = "DetailService.cs",
                TestabilityScore = 0.85,
                UncoveredMethods = [new UncoveredMethod { Name = "Run", UncoveredLines = Enumerable.Range(5, 21).ToArray(), TotalLines = 21 }]
            }
        );

        var result = CoverageGapsTool.GetCoverageGaps(summaryOnly: false);

        // Should contain detailed fields (new canonical schema)
        Assert.Contains("\"uncoveredMethods\":", result);
        Assert.Contains("\"filePath\":", result);
        Assert.Contains("\"linesTotal\":", result);
    }
}
