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
}
