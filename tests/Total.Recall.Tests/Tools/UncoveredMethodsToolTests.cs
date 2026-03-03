using System.Text.Json;
using Total.Recall.Infrastructure;
using Total.Recall.Models;
using Total.Recall.Tools;

namespace Total.Recall.Tests.Tools;

[Collection("ToolTests")]
public sealed class UncoveredMethodsToolTests : ToolTestBase
{
    private CoverageGap MakeGapWithMethods(string className, params (string Name, int Lines)[] methods)
    {
        var totalLines = methods.Sum(m => m.Lines) + 20; // some covered lines too
        var uncoveredLines = methods.Sum(m => m.Lines);
        var line = 10;
        var uncoveredMethods = new List<UncoveredMethod>();
        foreach (var (name, lines) in methods)
        {
            uncoveredMethods.Add(new UncoveredMethod
            {
                Name = name,
                StartLine = line,
                EndLine = line + lines - 1,
                UncoveredLines = lines
            });
            line += lines + 5;
        }

        return new CoverageGap
        {
            Class = className,
            Namespace = "App",
            File = $"src/{className}.cs",
            TotalLines = totalLines,
            CoveredLines = 20,
            UncoveredLines = uncoveredLines,
            CoveragePercent = Math.Round(100.0 * 20 / totalLines, 1),
            Testability = "high",
            UncoveredMethods = uncoveredMethods
        };
    }

    // ── No data ──

    [Fact]
    public void GetUncoveredMethods_NoCoverageData_ReturnsNotFoundMessage()
    {
        var result = UncoveredMethodsTool.GetUncoveredMethods();

        Assert.Contains("No coverage data found", result);
    }

    // ── Basic flattening ──

    [Fact]
    public void GetUncoveredMethods_FlattensMethodsFromMultipleClasses()
    {
        SeedCoverageGaps(
            MakeGapWithMethods("ClassA", ("DoWork", 10), ("Process", 5)),
            MakeGapWithMethods("ClassB", ("Handle", 15))
        );

        var result = UncoveredMethodsTool.GetUncoveredMethods();

        Assert.Contains("DoWork", result);
        Assert.Contains("Process", result);
        Assert.Contains("Handle", result);
    }

    [Fact]
    public void GetUncoveredMethods_RespectsTopParameter()
    {
        SeedCoverageGaps(
            MakeGapWithMethods("ClassA", ("DoWork", 20), ("Process", 15), ("Validate", 10))
        );

        var result = UncoveredMethodsTool.GetUncoveredMethods(top: 2);
        var parsed = JsonSerializer.Deserialize<JsonElement>(result);
        var methods = parsed.GetProperty("methods");

        Assert.Equal(2, methods.GetArrayLength());
    }

    // ── minUncoveredLines filter ──

    [Fact]
    public void GetUncoveredMethods_FiltersByMinUncoveredLines()
    {
        SeedCoverageGaps(
            MakeGapWithMethods("ClassA", ("BigMethod", 20), ("TinyMethod", 2))
        );

        var result = UncoveredMethodsTool.GetUncoveredMethods(minUncoveredLines: 3);

        Assert.Contains("BigMethod", result);
        Assert.DoesNotContain("TinyMethod", result);
    }

    // ── Boilerplate exclusion ──

    [Fact]
    public void GetUncoveredMethods_ExcludesBoilerplateByDefault()
    {
        SeedCoverageGaps(
            MakeGapWithMethods("ClassA", ("DoWork", 10), (".ctor", 5), ("get_Name", 3), ("set_Name", 3))
        );

        var result = UncoveredMethodsTool.GetUncoveredMethods();

        Assert.Contains("DoWork", result);
        Assert.DoesNotContain(".ctor", result);
        Assert.DoesNotContain("get_Name", result);
    }

    [Fact]
    public void GetUncoveredMethods_IncludesBoilerplateWhenDisabled()
    {
        SeedCoverageGaps(
            MakeGapWithMethods("ClassA", ("DoWork", 10), (".ctor", 5))
        );

        var result = UncoveredMethodsTool.GetUncoveredMethods(excludeBoilerplate: false);

        Assert.Contains("DoWork", result);
        Assert.Contains(".ctor", result);
    }

    // ── hasTestFile scoring bias ──

    [Fact]
    public void GetUncoveredMethods_MethodsWithTestFileScoreHigher()
    {
        SeedCoverageGaps(
            MakeGapWithMethods("WithTests", ("DoWork", 10)),
            MakeGapWithMethods("NoTests", ("Process", 10))
        );
        SeedTestInventory(new TestInventoryEntry
        {
            Class = "WithTests",
            TestFiles = ["tests/WithTestsTests.cs"],
            TestMethods = ["DoWork_Test"],
            TestCount = 1
        });

        var result = UncoveredMethodsTool.GetUncoveredMethods();
        var parsed = JsonSerializer.Deserialize<JsonElement>(result);
        var methods = parsed.GetProperty("methods");

        // WithTests.DoWork should rank higher due to 2.0x multiplier
        var first = methods[0].GetProperty("class").GetString();
        Assert.Equal("WithTests", first);
    }

    // ── onlyWithExistingTests filter ──

    [Fact]
    public void GetUncoveredMethods_OnlyWithExistingTests_FiltersCorrectly()
    {
        SeedCoverageGaps(
            MakeGapWithMethods("WithTests", ("DoWork", 10)),
            MakeGapWithMethods("NoTests", ("Process", 10))
        );
        SeedTestInventory(new TestInventoryEntry
        {
            Class = "WithTests",
            TestFiles = ["tests/WithTestsTests.cs"],
            TestMethods = ["SomeTest"],
            TestCount = 1
        });

        var result = UncoveredMethodsTool.GetUncoveredMethods(onlyWithExistingTests: true);

        Assert.Contains("WithTests", result);
        Assert.DoesNotContain("NoTests", result);
    }

    [Fact]
    public void GetUncoveredMethods_OnlyWithExistingTests_NoMatches_ReturnsMessage()
    {
        SeedCoverageGaps(
            MakeGapWithMethods("NoTests", ("Process", 10))
        );

        var result = UncoveredMethodsTool.GetUncoveredMethods(onlyWithExistingTests: true);

        Assert.Contains("No uncovered methods found", result);
    }

    // ── Skip/coupled assessment exclusion ──

    [Fact]
    public void GetUncoveredMethods_SkipsAssessedCoupledClasses()
    {
        SeedCoverageGaps(
            MakeGapWithMethods("CoupledClass", ("DoWork", 20)),
            MakeGapWithMethods("GoodClass", ("Process", 10))
        );
        SeedAssessments(new Assessment
        {
            Class = "CoupledClass",
            Verdict = "coupled",
            Reasoning = "too coupled",
            Date = DateTime.UtcNow.ToString("o")
        });

        var result = UncoveredMethodsTool.GetUncoveredMethods();

        Assert.DoesNotContain("CoupledClass", result);
        Assert.Contains("GoodClass", result);
    }

    // ── Skip reason exclusion ──

    [Fact]
    public void GetUncoveredMethods_SkipsClassesWithSkipReason()
    {
        var gap = MakeGapWithMethods("SkippedClass", ("DoWork", 20));
        gap.SkipReason = "auto-generated code";
        SeedCoverageGaps(gap, MakeGapWithMethods("GoodClass", ("Process", 10)));

        var result = UncoveredMethodsTool.GetUncoveredMethods();

        Assert.DoesNotContain("SkippedClass", result);
        Assert.Contains("GoodClass", result);
    }

    // ── Zero uncovered lines class ──

    [Fact]
    public void GetUncoveredMethods_SkipsZeroUncoveredLinesClass()
    {
        SeedCoverageGaps(
            new CoverageGap
            {
                Class = "FullyCovered",
                Namespace = "App",
                UncoveredLines = 0,
                UncoveredMethods = []
            },
            MakeGapWithMethods("GoodClass", ("Process", 10))
        );

        var result = UncoveredMethodsTool.GetUncoveredMethods();

        Assert.DoesNotContain("FullyCovered", result);
        Assert.Contains("GoodClass", result);
    }

    // ── Summary stats ──

    [Fact]
    public void GetUncoveredMethods_IncludesSummaryStats()
    {
        SeedCoverageGaps(
            MakeGapWithMethods("ClassA", ("DoWork", 10)),
            MakeGapWithMethods("ClassB", ("Process", 15))
        );
        SeedTestInventory(new TestInventoryEntry
        {
            Class = "ClassA",
            TestFiles = ["tests/ClassATests.cs"],
            TestMethods = ["Test1"],
            TestCount = 1
        });

        var result = UncoveredMethodsTool.GetUncoveredMethods();
        var parsed = JsonSerializer.Deserialize<JsonElement>(result);

        Assert.True(parsed.TryGetProperty("stats", out var stats));
        Assert.Equal(1, stats.GetProperty("methodsWithTestFile").GetInt32());
        Assert.Equal(1, stats.GetProperty("methodsWithoutTestFile").GetInt32());
        Assert.Equal(2, stats.GetProperty("distinctClasses").GetInt32());
    }

    // ── Output structure ──

    [Fact]
    public void GetUncoveredMethods_OutputContainsExpectedFields()
    {
        SeedCoverageGaps(
            MakeGapWithMethods("ClassA", ("DoWork", 10))
        );
        SeedTestInventory(new TestInventoryEntry
        {
            Class = "ClassA",
            TestFiles = ["tests/ClassATests.cs"],
            TestMethods = ["Test1"],
            TestCount = 1
        });

        var result = UncoveredMethodsTool.GetUncoveredMethods();
        var parsed = JsonSerializer.Deserialize<JsonElement>(result);
        var method = parsed.GetProperty("methods")[0];

        Assert.Equal("ClassA", method.GetProperty("class").GetString());
        Assert.Equal("DoWork", method.GetProperty("method").GetString());
        Assert.Equal(10, method.GetProperty("uncoveredLines").GetInt32());
        Assert.True(method.GetProperty("hasTestFile").GetBoolean());
        Assert.True(method.GetProperty("score").GetDouble() > 0);
        Assert.False(string.IsNullOrEmpty(method.GetProperty("reason").GetString()));
    }

    // ── Reason text ──

    [Fact]
    public void GetUncoveredMethods_ReasonShowsTestFileStatus()
    {
        SeedCoverageGaps(
            MakeGapWithMethods("WithTests", ("DoWork", 10)),
            MakeGapWithMethods("NoTests", ("Process", 10))
        );
        SeedTestInventory(new TestInventoryEntry
        {
            Class = "WithTests",
            TestFiles = ["tests/WithTestsTests.cs"],
            TestMethods = ["Test1"],
            TestCount = 1
        });

        var result = UncoveredMethodsTool.GetUncoveredMethods();

        Assert.Contains("test file exists", result);
        Assert.Contains("no test file", result);
    }

    // ── CalculateMethodScore unit tests ──

    [Fact]
    public void CalculateMethodScore_WithTestFile_DoubleScore()
    {
        var without = UncoveredMethodsTool.CalculateMethodScore(20, hasTestFile: false, existingTestCount: 0);
        var with = UncoveredMethodsTool.CalculateMethodScore(20, hasTestFile: true, existingTestCount: 0);

        Assert.True(with > without);
        // hasTestFile=true → 2.0x, hasTestFile=false → 0.5x, ratio = 4.0
        Assert.InRange(with / without, 3.9, 4.1);
    }

    [Fact]
    public void CalculateMethodScore_MoreUncoveredLines_HigherScore()
    {
        var small = UncoveredMethodsTool.CalculateMethodScore(5, hasTestFile: true, existingTestCount: 0);
        var large = UncoveredMethodsTool.CalculateMethodScore(50, hasTestFile: true, existingTestCount: 0);

        Assert.True(large > small);
    }

    [Fact]
    public void CalculateMethodScore_ExistingTests_MildDiminishingReturns()
    {
        var zero = UncoveredMethodsTool.CalculateMethodScore(20, hasTestFile: true, existingTestCount: 0);
        var ten = UncoveredMethodsTool.CalculateMethodScore(20, hasTestFile: true, existingTestCount: 10);

        Assert.True(ten < zero);
        // 10 tests → 1/(1 + 10*0.05) = 1/1.5 ≈ 0.67x — mild, not cliff
        Assert.True(ten > zero * 0.5);
    }

    // ── Nested class name handling ──

    [Fact]
    public void GetUncoveredMethods_HandlesNestedClassNames()
    {
        SeedCoverageGaps(MakeGapWithMethods("Parent/Nested", ("Handle", 10)));
        SeedTestInventory(new TestInventoryEntry
        {
            Class = "Nested",
            TestFiles = ["tests/NestedTests.cs"],
            TestMethods = ["Test1"],
            TestCount = 1
        });

        var result = UncoveredMethodsTool.GetUncoveredMethods();

        Assert.Contains("Handle", result);
        Assert.Contains("hasTestFile", result);
    }

    // ── Metrics increment ──

    [Fact]
    public void GetUncoveredMethods_IncrementsMetrics()
    {
        SeedCoverageGaps(MakeGapWithMethods("ClassA", ("DoWork", 10)));
        Metrics.Reset();

        UncoveredMethodsTool.GetUncoveredMethods();

        Assert.Equal(1, Metrics.Get(Metrics.ToolGetUncoveredMethods));
    }

    // ── BuildMethodReason ──

    [Fact]
    public void BuildMethodReason_WithTestFile_ShowsStar()
    {
        var reason = UncoveredMethodsTool.BuildMethodReason(10, hasTestFile: true, existingTestCount: 3, testFileCount: 1);

        Assert.Contains("test file exists", reason);
        Assert.Contains("3 existing tests", reason);
    }

    [Fact]
    public void BuildMethodReason_NoTestFile_ShowsWarning()
    {
        var reason = UncoveredMethodsTool.BuildMethodReason(10, hasTestFile: false, existingTestCount: 0, testFileCount: 0);

        Assert.Contains("no test file", reason);
        Assert.DoesNotContain("existing tests", reason);
    }
}
