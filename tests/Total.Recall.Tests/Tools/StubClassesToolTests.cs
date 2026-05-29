using System.Text.Json;
using Total.Recall.Infrastructure;
using Total.Recall.Models;
using Total.Recall.Tools;

namespace Total.Recall.Tests.Tools;

[Collection("ToolTests")]
public sealed class StubClassesToolTests : ToolTestBase
{
    private CoverageGap MakeZeroCoverageGap(string className, int uncoveredLines = 30,
        params (string Name, int Lines)[] methods)
    {
        if (methods.Length == 0)
            methods = [("DoSomething", 10), ("get_Value", 5), (".ctor", 3)];

        var totalLines = uncoveredLines;
        var line = 10;
        var uncoveredMethods = new List<UncoveredMethod>();
        foreach (var (name, lines) in methods)
        {
            uncoveredMethods.Add(new UncoveredMethod
            {
                Name = name,
                UncoveredLines = Enumerable.Range(line, lines).ToArray(),
                TotalLines = lines
            });
            line += lines + 2;
        }

        return new CoverageGap
        {
            ClassName = $"App.{className}",
            FilePath = $"src/{className}.cs",
            LinesTotal = totalLines,
            LinesCovered = 0,
            CoveragePercent = 0.0,
            TestabilityScore = 0.85,
            UncoveredMethods = uncoveredMethods
        };
    }

    private CoverageGap MakeGapWithCoverage(string className, int uncoveredLines, int totalLines,
        params (string Name, int Lines)[] methods)
    {
        if (methods.Length == 0)
            methods = [("Run", uncoveredLines)];

        var line = 10;
        var uncoveredMethods = new List<UncoveredMethod>();
        foreach (var (name, lines) in methods)
        {
            uncoveredMethods.Add(new UncoveredMethod
            {
                Name = name,
                UncoveredLines = Enumerable.Range(line, lines).ToArray(),
                TotalLines = lines
            });
            line += lines + 2;
        }

        return new CoverageGap
        {
            ClassName = $"App.{className}",
            FilePath = $"src/{className}.cs",
            LinesTotal = totalLines,
            LinesCovered = totalLines - uncoveredLines,
            CoveragePercent = Math.Round(100.0 * (totalLines - uncoveredLines) / totalLines, 1),
            TestabilityScore = 0.85,
            UncoveredMethods = uncoveredMethods
        };
    }

    private TypeRecord MakeTypeRecord(string name, bool isStatic = false, bool isAbstract = false,
        bool isInterface = false, bool isEnum = false, params string[][] ctors)
    {
        var constructors = ctors.Select(c => new ConstructorRecord { Params = c.ToList() }).ToList();
        return new TypeRecord
        {
            Name = name,
            Namespace = "App",
            IsStatic = isStatic,
            IsAbstract = isAbstract,
            IsInterface = isInterface,
            IsEnum = isEnum,
            Constructors = constructors
        };
    }

    // ── No data ──

    [Fact]
    public void GetStubClasses_NoCoverageData_ReturnsNotFoundMessage()
    {
        var result = StubClassesTool.GetStubClasses();
        Assert.Contains("No coverage data found", result);
    }

    // ── Basic discovery ──

    [Fact]
    public void GetStubClasses_FindsZeroCoverageClasses()
    {
        SeedCoverageGaps(
            MakeZeroCoverageGap("SimpleStub", 30),
            MakeGapWithCoverage("WellCovered", 5, 200)  // 97.5% coverage — excluded
        );

        var result = StubClassesTool.GetStubClasses();
        Assert.Contains("SimpleStub", result);
        Assert.DoesNotContain("WellCovered", result);
    }

    [Fact]
    public void GetStubClasses_RespectsMaxCoveragePercent()
    {
        SeedCoverageGaps(
            MakeZeroCoverageGap("ZeroPct", 30),
            MakeGapWithCoverage("ThreePct", 29, 30)  // ~3.3% coverage
        );

        var result = StubClassesTool.GetStubClasses(maxCoveragePercent: 0.0);
        Assert.Contains("ZeroPct", result);
        Assert.DoesNotContain("ThreePct", result);
    }

    [Fact]
    public void GetStubClasses_MaxCoverage5_IncludesLowCoverage()
    {
        SeedCoverageGaps(
            MakeGapWithCoverage("LowCov", 48, 50)  // 4% coverage
        );

        var result = StubClassesTool.GetStubClasses(maxCoveragePercent: 5.0);
        Assert.Contains("LowCov", result);
    }

    [Fact]
    public void GetStubClasses_RespectsTopParameter()
    {
        SeedCoverageGaps(
            MakeZeroCoverageGap("A", 50),
            MakeZeroCoverageGap("B", 40),
            MakeZeroCoverageGap("C", 30)
        );

        var result = StubClassesTool.GetStubClasses(top: 2);
        var parsed = JsonSerializer.Deserialize<JsonElement>(result);
        var classes = parsed.GetProperty("classes");
        Assert.Equal(2, classes.GetArrayLength());
    }

    // ── Constructor complexity filtering ──

    [Fact]
    public void GetStubClasses_ExcludesHighCtorParamClasses()
    {
        SeedCoverageGaps(MakeZeroCoverageGap("ComplexCtor", 40));
        SeedTypeRegistry(MakeTypeRecord("ComplexCtor",
            ctors: [["ILogger log", "IConfig cfg", "IService svc"]]));

        var result = StubClassesTool.GetStubClasses(maxCtorParams: 2);
        Assert.DoesNotContain("ComplexCtor", result);
    }

    [Fact]
    public void GetStubClasses_IncludesParameterlessCtor()
    {
        SeedCoverageGaps(MakeZeroCoverageGap("SimplePoco", 20));
        SeedTypeRegistry(MakeTypeRecord("SimplePoco", ctors: [[]]));

        var result = StubClassesTool.GetStubClasses();
        Assert.Contains("SimplePoco", result);
    }

    [Fact]
    public void GetStubClasses_UsesMinCtorAcrossOverloads()
    {
        SeedCoverageGaps(MakeZeroCoverageGap("MultiCtor", 25));
        SeedTypeRegistry(MakeTypeRecord("MultiCtor",
            ctors: [["ILogger log", "IConfig cfg", "IService svc"], []]));

        var result = StubClassesTool.GetStubClasses(maxCtorParams: 0);
        Assert.Contains("MultiCtor", result);
    }

    // ── Assessment exclusion ──

    [Theory]
    [InlineData("skip", "Too coupled", true)]
    [InlineData("coupled", "Deep dependencies", true)]
    [InlineData("deferred", "For later", true)]
    [InlineData("testable", "Simple", false)]
    public void GetStubClasses_AssessmentVerdict_FiltersCorrectly(
        string verdict, string reasoning, bool shouldBeExcluded)
    {
        SeedCoverageGaps(MakeZeroCoverageGap("AssessedClass", 40));
        SeedAssessments(new Assessment
        {
            Class = "AssessedClass",
            Verdict = verdict,
            Reasoning = reasoning
        });

        var result = StubClassesTool.GetStubClasses();
        if (shouldBeExcluded)
            Assert.Contains("No stub classes found", result);
        else
            Assert.Contains("AssessedClass", result);
    }

    // ── Type filtering ──

    [Theory]
    [InlineData(true, false, false, "IMyInterface", 20)]
    [InlineData(false, true, false, "MyEnum", 10)]
    [InlineData(false, false, true, "AbstractBase", 30)]
    public void GetStubClasses_NonClassTypes_ExcludesCorrectly(
        bool isInterface, bool isEnum, bool isAbstract, string typeName, int uncoveredLines)
    {
        SeedCoverageGaps(MakeZeroCoverageGap(typeName, uncoveredLines));
        SeedTypeRegistry(MakeTypeRecord(typeName, isInterface: isInterface, isEnum: isEnum, isAbstract: isAbstract));

        var result = StubClassesTool.GetStubClasses();
        Assert.Contains("No stub classes found", result);
    }

    // ── SkipReason exclusion ──

    [Fact]
    public void GetStubClasses_ExcludesSkipReasonClasses()
    {
        var gap = MakeZeroCoverageGap("SkippedByReason", 30);
        gap.TestabilityScore = 0.1;
        SeedCoverageGaps(gap);

        var result = StubClassesTool.GetStubClasses();
        Assert.Contains("No stub classes found", result);
    }

    // ── Zero uncovered lines exclusion ──

    [Fact]
    public void GetStubClasses_ExcludesZeroUncoveredLines()
    {
        var gap = MakeZeroCoverageGap("FullyCovered", 0);
        gap.LinesTotal = gap.LinesCovered;
        SeedCoverageGaps(gap);

        var result = StubClassesTool.GetStubClasses();
        Assert.Contains("No stub classes found", result);
    }

    // ── Test inventory integration ──

    [Fact]
    public void GetStubClasses_ExcludesClassesWithTestsByDefault()
    {
        SeedCoverageGaps(MakeZeroCoverageGap("TestedClass", 30));
        SeedTestInventory(new TestInventoryEntry
        {
            Class = "TestedClass",
            TestCount = 3,
            TestMethods = ["Test1", "Test2", "Test3"],
            TestFiles = ["TestedClassTests.cs"]
        });

        var result = StubClassesTool.GetStubClasses();
        Assert.Contains("No stub classes found", result);
    }

    [Fact]
    public void GetStubClasses_IncludeWithTests_ShowsTestedClasses()
    {
        SeedCoverageGaps(MakeZeroCoverageGap("TestedClass", 30));
        SeedTestInventory(new TestInventoryEntry
        {
            Class = "TestedClass",
            TestCount = 2,
            TestMethods = ["Test1", "Test2"],
            TestFiles = ["TestedClassTests.cs"]
        });

        var result = StubClassesTool.GetStubClasses(includeWithTests: true);
        Assert.Contains("TestedClass", result);
    }

    [Fact]
    public void GetStubClasses_HasTestFile_DetectedFromInventory()
    {
        SeedCoverageGaps(MakeZeroCoverageGap("WithFile", 30));
        SeedTestInventory(new TestInventoryEntry
        {
            Class = "WithFile",
            TestCount = 0,
            TestFiles = ["WithFileTests.cs"]
        });

        var result = StubClassesTool.GetStubClasses();
        var parsed = JsonSerializer.Deserialize<JsonElement>(result);
        var first = parsed.GetProperty("classes")[0];
        Assert.True(first.GetProperty("hasTestFile").GetBoolean());
    }

    // ── Classification ──

    [Theory]
    [InlineData(true, 3, 0, 20, "static-helpers")]
    [InlineData(false, 0, 5, 15, "poco")]
    [InlineData(false, 3, 1, 25, "simple-logic")]
    [InlineData(false, 8, 2, 60, "logic-heavy")]
    public void ClassifyStub_VaryingMethodProfile_ReturnsCorrectCategory(
        bool isStatic, int realMethods, int boilerplateMethods, int uncoveredLines, string expectedCategory)
    {
        var tr = MakeTypeRecord("TestClass", isStatic: isStatic);
        var gap = MakeZeroCoverageGap("TestClass", uncoveredLines);
        var result = StubClassesTool.ClassifyStub(tr, gap, realMethods: realMethods, boilerplateMethods: boilerplateMethods);
        Assert.Equal(expectedCategory, result);
    }

    // ── Scoring ──

    [Fact]
    public void CalculateStubScore_ParameterlessCtor_ScoresHigherThanWithParams()
    {
        var noParams = StubClassesTool.CalculateStubScore(
            uncoveredLines: 30, minCtorParams: 0, allParamsMockable: true,
            hasTestFile: false, existingTestCount: 0, realMethodCount: 2, totalLines: 40);

        var withParams = StubClassesTool.CalculateStubScore(
            uncoveredLines: 30, minCtorParams: 2, allParamsMockable: true,
            hasTestFile: false, existingTestCount: 0, realMethodCount: 2, totalLines: 40);

        Assert.True(noParams > withParams, $"noParams ({noParams}) should > withParams ({withParams})");
    }

    [Fact]
    public void CalculateStubScore_MockableParams_ScoresHigherThanConcrete()
    {
        var mockable = StubClassesTool.CalculateStubScore(
            uncoveredLines: 30, minCtorParams: 2, allParamsMockable: true,
            hasTestFile: false, existingTestCount: 0, realMethodCount: 2, totalLines: 40);

        var concrete = StubClassesTool.CalculateStubScore(
            uncoveredLines: 30, minCtorParams: 2, allParamsMockable: false,
            hasTestFile: false, existingTestCount: 0, realMethodCount: 2, totalLines: 40);

        Assert.True(mockable > concrete, $"mockable ({mockable}) should > concrete ({concrete})");
    }

    [Fact]
    public void CalculateStubScore_HasTestFile_AppliesOnePointFiveMultiplier()
    {
        var without = StubClassesTool.CalculateStubScore(
            uncoveredLines: 30, minCtorParams: 0, allParamsMockable: true,
            hasTestFile: false, existingTestCount: 0, realMethodCount: 2, totalLines: 40);

        var with = StubClassesTool.CalculateStubScore(
            uncoveredLines: 30, minCtorParams: 0, allParamsMockable: true,
            hasTestFile: true, existingTestCount: 0, realMethodCount: 2, totalLines: 40);

        Assert.True(with > without);
        Assert.Equal(with, without * 1.5, precision: 1);
    }

    [Fact]
    public void CalculateStubScore_MoreRealMethods_ScoresHigher()
    {
        var few = StubClassesTool.CalculateStubScore(
            uncoveredLines: 30, minCtorParams: 0, allParamsMockable: true,
            hasTestFile: false, existingTestCount: 0, realMethodCount: 0, totalLines: 40);

        var many = StubClassesTool.CalculateStubScore(
            uncoveredLines: 30, minCtorParams: 0, allParamsMockable: true,
            hasTestFile: false, existingTestCount: 0, realMethodCount: 5, totalLines: 40);

        Assert.True(many > few, $"many methods ({many}) should > few ({few})");
    }

    [Fact]
    public void CalculateStubScore_SmallClass_GetsBonus()
    {
        var small = StubClassesTool.CalculateStubScore(
            uncoveredLines: 30, minCtorParams: 0, allParamsMockable: true,
            hasTestFile: false, existingTestCount: 0, realMethodCount: 2, totalLines: 40);

        var large = StubClassesTool.CalculateStubScore(
            uncoveredLines: 30, minCtorParams: 0, allParamsMockable: true,
            hasTestFile: false, existingTestCount: 0, realMethodCount: 2, totalLines: 300);

        Assert.True(small > large, $"small ({small}) should > large ({large})");
    }

    [Fact]
    public void CalculateStubScore_ExistingTests_DiminishReturns()
    {
        var noTests = StubClassesTool.CalculateStubScore(
            uncoveredLines: 30, minCtorParams: 0, allParamsMockable: true,
            hasTestFile: false, existingTestCount: 0, realMethodCount: 2, totalLines: 40);

        var withTests = StubClassesTool.CalculateStubScore(
            uncoveredLines: 30, minCtorParams: 0, allParamsMockable: true,
            hasTestFile: true, existingTestCount: 5, realMethodCount: 2, totalLines: 40);

        Assert.True(noTests > withTests, $"noTests ({noTests}) should > withTests ({withTests})");
    }

    // ── GetMinCtorParams ──

    [Fact]
    public void GetMinCtorParams_NullTypeRecord_ReturnsZero()
    {
        Assert.Equal(0, StubClassesTool.GetMinCtorParams(null));
    }

    [Fact]
    public void GetMinCtorParams_StaticClass_ReturnsZero()
    {
        var tr = MakeTypeRecord("Static", isStatic: true);
        Assert.Equal(0, StubClassesTool.GetMinCtorParams(tr));
    }

    [Fact]
    public void GetMinCtorParams_NoConstructors_ReturnsZero()
    {
        var tr = MakeTypeRecord("Simple");
        Assert.Equal(0, StubClassesTool.GetMinCtorParams(tr));
    }

    [Fact]
    public void GetMinCtorParams_MultipleCtors_ReturnsMinimum()
    {
        var tr = MakeTypeRecord("Multi",
            ctors: [["ILogger log", "IConfig cfg"], ["ILogger log"]]);
        Assert.Equal(1, StubClassesTool.GetMinCtorParams(tr));
    }

    // ── AreAllParamsMockable ──

    [Fact]
    public void AreAllParamsMockable_NullTypeRecord_ReturnsTrue()
    {
        Assert.True(StubClassesTool.AreAllParamsMockable(null));
    }

    [Fact]
    public void AreAllParamsMockable_AllInterfaces_ReturnsTrue()
    {
        var tr = MakeTypeRecord("AllIface", ctors: [["ILogger log", "IConfig cfg"]]);
        Assert.True(StubClassesTool.AreAllParamsMockable(tr));
    }

    [Fact]
    public void AreAllParamsMockable_HasConcreteParam_ReturnsFalse()
    {
        var tr = MakeTypeRecord("HasConcrete", ctors: [["ILogger log", "MyService svc"]]);
        Assert.False(StubClassesTool.AreAllParamsMockable(tr));
    }

    [Fact]
    public void AreAllParamsMockable_ParameterlessCtor_ReturnsTrue()
    {
        var tr = MakeTypeRecord("Empty", ctors: [[]]);
        Assert.True(StubClassesTool.AreAllParamsMockable(tr));
    }

    [Fact]
    public void AreAllParamsMockable_UsesSimplestCtor()
    {
        // Complex ctor has concrete params, but simplest ctor is parameterless
        var tr = MakeTypeRecord("OverloadedCtor",
            ctors: [["MyService svc", "ThingFactory factory"], []]);
        Assert.True(StubClassesTool.AreAllParamsMockable(tr));
    }

    // ── BuildStubReason ──

    [Fact]
    public void BuildStubReason_ParameterlessCtor_ShowsParamlessInfo()
    {
        var reason = StubClassesTool.BuildStubReason(
            30, 0, true, false, 0, "poco", 0, 40);
        Assert.Contains("parameterless ctor", reason);
        Assert.Contains("category: poco", reason);
    }

    [Fact]
    public void BuildStubReason_WithMockableParams_ShowsAllMockable()
    {
        var reason = StubClassesTool.BuildStubReason(
            30, 2, true, false, 0, "simple-logic", 3, 40);
        Assert.Contains("all mockable", reason);
        Assert.Contains("3 real method", reason);
    }

    [Fact]
    public void BuildStubReason_WithConcreteParams_ShowsConcreteDeps()
    {
        var reason = StubClassesTool.BuildStubReason(
            30, 1, false, false, 0, "simple-logic", 2, 80);
        Assert.Contains("has concrete deps", reason);
    }

    [Fact]
    public void BuildStubReason_SmallClass_ShowsQuickWin()
    {
        var reason = StubClassesTool.BuildStubReason(
            20, 0, true, false, 0, "poco", 0, 40);
        Assert.Contains("small class (quick win)", reason);
    }

    [Fact]
    public void BuildStubReason_TestFileExists_ShowsStar()
    {
        var reason = StubClassesTool.BuildStubReason(
            20, 0, true, true, 0, "poco", 0, 40);
        Assert.Contains("test file exists", reason);
    }

    // ── Output structure ──

    [Fact]
    public void GetStubClasses_OutputHasExpectedStructure()
    {
        SeedCoverageGaps(MakeZeroCoverageGap("SimpleClass", 25));

        var result = StubClassesTool.GetStubClasses();
        var parsed = JsonSerializer.Deserialize<JsonElement>(result);

        Assert.True(parsed.TryGetProperty("count", out _));
        Assert.True(parsed.TryGetProperty("filters", out _));
        Assert.True(parsed.TryGetProperty("stats", out _));
        Assert.True(parsed.TryGetProperty("classes", out _));

        var stats = parsed.GetProperty("stats");
        Assert.True(stats.TryGetProperty("totalUncoveredLines", out _));
        Assert.True(stats.TryGetProperty("avgUncoveredLines", out _));
        Assert.True(stats.TryGetProperty("parameterlessCtors", out _));
        Assert.True(stats.TryGetProperty("allMockable", out _));
        Assert.True(stats.TryGetProperty("categories", out _));
    }

    [Fact]
    public void GetStubClasses_ClassProperties_AllPresent()
    {
        SeedCoverageGaps(MakeZeroCoverageGap("MyClass", 30));

        var result = StubClassesTool.GetStubClasses();
        var parsed = JsonSerializer.Deserialize<JsonElement>(result);
        var first = parsed.GetProperty("classes")[0];

        Assert.Equal("MyClass", first.GetProperty("class").GetString());
        Assert.Equal("App", first.GetProperty("namespace").GetString());
        Assert.True(first.TryGetProperty("totalLines", out _));
        Assert.True(first.TryGetProperty("uncoveredLines", out _));
        Assert.True(first.TryGetProperty("coveragePercent", out _));
        Assert.True(first.TryGetProperty("realMethodCount", out _));
        Assert.True(first.TryGetProperty("boilerplateMethodCount", out _));
        Assert.True(first.TryGetProperty("minCtorParams", out _));
        Assert.True(first.TryGetProperty("allParamsMockable", out _));
        Assert.True(first.TryGetProperty("hasTestFile", out _));
        Assert.True(first.TryGetProperty("category", out _));
        Assert.True(first.TryGetProperty("score", out _));
        Assert.True(first.TryGetProperty("reason", out _));
    }

    // ── Summary stats ──

    [Fact]
    public void GetStubClasses_Stats_CountsCategoriesCorrectly()
    {
        SeedCoverageGaps(
            MakeZeroCoverageGap("Poco1", 20, ("get_Name", 5), (".ctor", 5), ("get_Value", 10)),
            MakeZeroCoverageGap("Logic1", 30, ("Process", 15), ("Validate", 10), ("get_X", 5))
        );

        var result = StubClassesTool.GetStubClasses();
        var parsed = JsonSerializer.Deserialize<JsonElement>(result);
        var stats = parsed.GetProperty("stats");
        var categories = stats.GetProperty("categories");

        Assert.True(categories.TryGetProperty("poco", out _));
    }

    // ── Ranking ──

    [Fact]
    public void GetStubClasses_RanksHighUncoveredLinesFirst()
    {
        SeedCoverageGaps(
            MakeZeroCoverageGap("SmallGap", 10),
            MakeZeroCoverageGap("BigGap", 50)
        );

        var result = StubClassesTool.GetStubClasses();
        var parsed = JsonSerializer.Deserialize<JsonElement>(result);
        var classes = parsed.GetProperty("classes");

        Assert.Equal("BigGap", classes[0].GetProperty("class").GetString());
        Assert.Equal("SmallGap", classes[1].GetProperty("class").GetString());
    }

    // ── Static class detection ──

    [Fact]
    public void GetStubClasses_StaticClass_CategorizedCorrectly()
    {
        SeedCoverageGaps(MakeZeroCoverageGap("StringExtensions", 25,
            ("Truncate", 10), ("ToSlug", 15)));
        SeedTypeRegistry(MakeTypeRecord("StringExtensions", isStatic: true));

        var result = StubClassesTool.GetStubClasses();
        var parsed = JsonSerializer.Deserialize<JsonElement>(result);
        var first = parsed.GetProperty("classes")[0];

        Assert.Equal("static-helpers", first.GetProperty("category").GetString());
    }

    // ── Nested class normalization ──

    [Fact]
    public void GetStubClasses_NestedClassName_NormalizesForLookups()
    {
        SeedCoverageGaps(MakeZeroCoverageGap("Parent/Nested", 20));
        SeedAssessments(new Assessment
        {
            Class = "Nested",
            Verdict = "skip",
            Reasoning = "Coupled"
        });

        var result = StubClassesTool.GetStubClasses();
        Assert.Contains("No stub classes found", result);
    }

    // ── Metrics ──

    [Fact]
    public void GetStubClasses_IncrementsMetrics()
    {
        Metrics.Reset();
        SeedCoverageGaps(MakeZeroCoverageGap("X", 10));

        StubClassesTool.GetStubClasses();

        Assert.Equal(1, Metrics.Get(Metrics.ToolGetStubClasses));
    }

    // ── No results message ──

    [Fact]
    public void GetStubClasses_NoMatchingClasses_ReturnsHelpfulMessage()
    {
        SeedCoverageGaps(MakeGapWithCoverage("HighCov", 2, 100));

        var result = StubClassesTool.GetStubClasses();
        Assert.Contains("No stub classes found", result);
        Assert.Contains("maxCoveragePercent", result);
    }

    // ── ExistingTestCount from CoverageGap fallback ──

    [Fact]
    public void GetStubClasses_UsesGapExistingTestCount_WhenNoInventory()
    {
        var gap = MakeZeroCoverageGap("FallbackClass", 30);
        gap.ExistingTests = 2;
        SeedCoverageGaps(gap);

        // No test inventory seeded — should fall back to gap.ExistingTests
        // With includeWithTests=false (default), this class should be excluded
        var result = StubClassesTool.GetStubClasses();
        Assert.Contains("No stub classes found", result);

        // With includeWithTests=true, it should appear with existingTestCount=2
        result = StubClassesTool.GetStubClasses(includeWithTests: true);
        Assert.Contains("FallbackClass", result);
        var parsed = JsonSerializer.Deserialize<JsonElement>(result);
        var cls = parsed.GetProperty("classes")[0];
        Assert.Equal(2, cls.GetProperty("existingTestCount").GetInt32());
    }

    // ── Method counting ──

    [Fact]
    public void GetStubClasses_CountsRealAndBoilerplateMethodsSeparately()
    {
        SeedCoverageGaps(MakeZeroCoverageGap("Mixed", 40,
            ("Process", 10), ("Validate", 8), ("get_Name", 5), ("set_Name", 5), (".ctor", 12)));

        var result = StubClassesTool.GetStubClasses();
        var parsed = JsonSerializer.Deserialize<JsonElement>(result);
        var first = parsed.GetProperty("classes")[0];

        Assert.Equal(2, first.GetProperty("realMethodCount").GetInt32());
        Assert.Equal(3, first.GetProperty("boilerplateMethodCount").GetInt32());
    }
}
