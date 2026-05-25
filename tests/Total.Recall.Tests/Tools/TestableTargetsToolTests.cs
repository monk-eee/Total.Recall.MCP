using System.Text.Json;
using Total.Recall.Infrastructure;
using Total.Recall.Models;
using Total.Recall.Tools;

namespace Total.Recall.Tests.Tools;

[Collection("ToolTests")]
public sealed class TestableTargetsToolTests : ToolTestBase
{

    private CoverageGap MakeGap(string className, int uncoveredLines = 20, int totalLines = 100,
        string testability = "high", int existingTestCount = 0, string? skipReason = null)
    {
        return new CoverageGap
        {
            Class = className,
            Namespace = "App",
            File = $"src/{className}.cs",
            TotalLines = totalLines,
            CoveredLines = totalLines - uncoveredLines,
            UncoveredLines = uncoveredLines,
            CoveragePercent = totalLines > 0 ? Math.Round(100.0 * (totalLines - uncoveredLines) / totalLines, 1) : 0,
            Testability = testability,
            ExistingTestCount = existingTestCount,
            SkipReason = skipReason,
            UncoveredMethods =
            [
                new UncoveredMethod { Name = "DoWork", StartLine = 10, EndLine = 30, UncoveredLines = uncoveredLines }
            ]
        };
    }

    // ── No data ──

    [Fact]
    public void GetTestableTargets_NoCoverageData_ReturnsError()
    {
        var result = TestableTargetsTool.GetTestableTargets();

        Assert.Contains("No coverage data found", result);
    }

    // ── Basic targets returned ──

    [Fact]
    public void GetTestableTargets_WithGaps_ReturnsTargets()
    {
        SeedCoverageGaps(MakeGap("ClassA", 50), MakeGap("ClassB", 30));

        var result = TestableTargetsTool.GetTestableTargets();
        var doc = JsonDocument.Parse(result);

        Assert.Equal(2, doc.RootElement.GetProperty("count").GetInt32());
    }

    // ── Score ordering: more uncovered lines = higher score ──

    [Fact]
    public void GetTestableTargets_OrderedByScoreDescending()
    {
        SeedCoverageGaps(MakeGap("Small", 10), MakeGap("Big", 100), MakeGap("Medium", 50));

        var result = TestableTargetsTool.GetTestableTargets();
        var doc = JsonDocument.Parse(result);
        var targets = doc.RootElement.GetProperty("targets");

        // Big (100 uncovered) should be first
        Assert.Equal("Big", targets[0].GetProperty("class").GetString());
        Assert.Equal("Small", targets[2].GetProperty("class").GetString());
    }

    // ── Top N limit ──

    [Fact]
    public void GetTestableTargets_RespectsTopLimit()
    {
        SeedCoverageGaps(MakeGap("A", 10), MakeGap("B", 20), MakeGap("C", 30));

        var result = TestableTargetsTool.GetTestableTargets(top: 2);
        var doc = JsonDocument.Parse(result);

        Assert.Equal(2, doc.RootElement.GetProperty("count").GetInt32());
    }

    // ── Filter: excludeAbstract ──

    [Fact]
    public void GetTestableTargets_ExcludeAbstract_FiltersAbstractClasses()
    {
        SeedCoverageGaps(MakeGap("ConcreteClass", 50), MakeGap("AbstractClass", 50));
        SeedTypeRegistry(
            new TypeRecord { Name = "ConcreteClass", Namespace = "App" },
            new TypeRecord { Name = "AbstractClass", Namespace = "App", IsAbstract = true }
        );

        var result = TestableTargetsTool.GetTestableTargets(excludeAbstract: true);
        var doc = JsonDocument.Parse(result);

        Assert.Equal(1, doc.RootElement.GetProperty("count").GetInt32());
        Assert.Equal("ConcreteClass", doc.RootElement.GetProperty("targets")[0].GetProperty("class").GetString());
    }

    [Fact]
    public void GetTestableTargets_IncludeAbstract_DoesNotFilter()
    {
        SeedCoverageGaps(MakeGap("ConcreteClass", 50), MakeGap("AbstractClass", 50));
        SeedTypeRegistry(
            new TypeRecord { Name = "ConcreteClass", Namespace = "App" },
            new TypeRecord { Name = "AbstractClass", Namespace = "App", IsAbstract = true }
        );

        var result = TestableTargetsTool.GetTestableTargets(excludeAbstract: false);
        var doc = JsonDocument.Parse(result);

        Assert.Equal(2, doc.RootElement.GetProperty("count").GetInt32());
    }

    // ── Filter: static classes always excluded ──

    [Fact]
    public void GetTestableTargets_StaticClasses_AlwaysExcluded()
    {
        SeedCoverageGaps(MakeGap("NormalClass", 50), MakeGap("StaticHelper", 50));
        SeedTypeRegistry(
            new TypeRecord { Name = "NormalClass", Namespace = "App" },
            new TypeRecord { Name = "StaticHelper", Namespace = "App", IsStatic = true }
        );

        var result = TestableTargetsTool.GetTestableTargets();
        var doc = JsonDocument.Parse(result);

        Assert.Equal(1, doc.RootElement.GetProperty("count").GetInt32());
        Assert.Equal("NormalClass", doc.RootElement.GetProperty("targets")[0].GetProperty("class").GetString());
    }

    // ── Filter: skipReason excludes entries ──

    [Fact]
    public void GetTestableTargets_SkipReason_ExcludesMarkedEntries()
    {
        SeedCoverageGaps(
            MakeGap("Good", 50),
            MakeGap("Bad", 50, skipReason: "generated code")
        );

        var result = TestableTargetsTool.GetTestableTargets();
        var doc = JsonDocument.Parse(result);

        Assert.Equal(1, doc.RootElement.GetProperty("count").GetInt32());
    }

    // ── Filter: 0 uncovered lines skipped ──

    [Fact]
    public void GetTestableTargets_ZeroUncoveredLines_Excluded()
    {
        SeedCoverageGaps(MakeGap("HasGaps", 50), MakeGap("FullyCovered", 0));

        var result = TestableTargetsTool.GetTestableTargets();
        var doc = JsonDocument.Parse(result);

        Assert.Equal(1, doc.RootElement.GetProperty("count").GetInt32());
    }

    // ── Filter: maxCtorParams ──

    [Fact]
    public void GetTestableTargets_MaxCtorParams_FiltersComplex()
    {
        SeedCoverageGaps(MakeGap("Simple", 50), MakeGap("Complex", 50));
        SeedTypeRegistry(
            new TypeRecord { Name = "Simple", Namespace = "App", Constructors = [new ConstructorRecord { Params = ["ILogger _logger"] }] },
            new TypeRecord { Name = "Complex", Namespace = "App", Constructors = [new ConstructorRecord { Params = ["ILogger a", "IRepo b", "ICache c", "IConfig d", "IAuth e", "IMap f"] }] }
        );

        var result = TestableTargetsTool.GetTestableTargets(maxCtorParams: 3);
        var doc = JsonDocument.Parse(result);

        Assert.Equal(1, doc.RootElement.GetProperty("count").GetInt32());
        Assert.Equal("Simple", doc.RootElement.GetProperty("targets")[0].GetProperty("class").GetString());
    }

    // ── Filter: maxTotalLines ──

    [Fact]
    public void GetTestableTargets_MaxTotalLines_FiltersLargeClasses()
    {
        SeedCoverageGaps(MakeGap("Small", 20, totalLines: 80), MakeGap("Huge", 200, totalLines: 1000));

        var result = TestableTargetsTool.GetTestableTargets(maxTotalLines: 500);
        var doc = JsonDocument.Parse(result);

        Assert.Equal(1, doc.RootElement.GetProperty("count").GetInt32());
        Assert.Equal("Small", doc.RootElement.GetProperty("targets")[0].GetProperty("class").GetString());
    }

    // ── Filter: requireZeroTests ──

    [Fact]
    public void GetTestableTargets_RequireZeroTests_FiltersTestedClasses()
    {
        SeedCoverageGaps(MakeGap("Untested", 50), MakeGap("Tested", 50, existingTestCount: 5));

        var result = TestableTargetsTool.GetTestableTargets(requireZeroTests: true);
        var doc = JsonDocument.Parse(result);

        Assert.Equal(1, doc.RootElement.GetProperty("count").GetInt32());
        Assert.Equal("Untested", doc.RootElement.GetProperty("targets")[0].GetProperty("class").GetString());
    }

    // ── Filter: excludeAssessed with skip/coupled ──

    [Fact]
    public void GetTestableTargets_ExcludeAssessed_FiltersSkipAndCoupled()
    {
        SeedCoverageGaps(MakeGap("Good", 50), MakeGap("Skipped", 50), MakeGap("Coupled", 50));
        SeedAssessments(
            new Assessment { Class = "Skipped", Verdict = "skip", Reasoning = "not testable", Date = "2025-01-01" },
            new Assessment { Class = "Coupled", Verdict = "coupled", Reasoning = "tight coupling", Date = "2025-01-01" }
        );

        var result = TestableTargetsTool.GetTestableTargets(excludeAssessed: true);
        var doc = JsonDocument.Parse(result);

        Assert.Equal(1, doc.RootElement.GetProperty("count").GetInt32());
        Assert.Equal("Good", doc.RootElement.GetProperty("targets")[0].GetProperty("class").GetString());
    }

    [Fact]
    public void GetTestableTargets_ExcludeAssessedFalse_KeepsAll()
    {
        SeedCoverageGaps(MakeGap("Good", 50), MakeGap("Skipped", 50));
        SeedAssessments(
            new Assessment { Class = "Skipped", Verdict = "skip", Reasoning = "not testable", Date = "2025-01-01" }
        );

        var result = TestableTargetsTool.GetTestableTargets(excludeAssessed: false);
        var doc = JsonDocument.Parse(result);

        Assert.Equal(2, doc.RootElement.GetProperty("count").GetInt32());
    }

    // ── Assessment deduplication: last wins ──

    [Fact]
    public void GetTestableTargets_AssessmentDedup_LastWins()
    {
        SeedCoverageGaps(MakeGap("FlipFlop", 50));
        SeedAssessments(
            new Assessment { Class = "FlipFlop", Verdict = "skip", Reasoning = "initially skipped", Date = "2025-01-01" },
            new Assessment { Class = "FlipFlop", Verdict = "testable", Reasoning = "reconsidered", Date = "2025-01-02" }
        );

        var result = TestableTargetsTool.GetTestableTargets(excludeAssessed: true);
        var doc = JsonDocument.Parse(result);

        // Last assessment is "testable", so it should NOT be excluded
        Assert.Equal(1, doc.RootElement.GetProperty("count").GetInt32());
    }

    // ── Scoring: testability multiplier ──

    [Fact]
    public void GetTestableTargets_HighTestability_ScoresHigherThanLow()
    {
        SeedCoverageGaps(
            MakeGap("HighTest", 50, testability: "high"),
            MakeGap("LowTest", 50, testability: "low")
        );

        var result = TestableTargetsTool.GetTestableTargets();
        var doc = JsonDocument.Parse(result);
        var targets = doc.RootElement.GetProperty("targets");

        var highScore = targets[0].GetProperty("score").GetDouble();
        var lowScore = targets[1].GetProperty("score").GetDouble();

        Assert.True(highScore > lowScore, $"High ({highScore}) should be > Low ({lowScore})");
        Assert.Equal("HighTest", targets[0].GetProperty("class").GetString());
    }

    // ── Scoring: fewer ctor params → higher score ──

    [Fact]
    public void GetTestableTargets_FewerCtorParams_ScoresHigher()
    {
        SeedCoverageGaps(MakeGap("SimpleCtor", 50), MakeGap("ComplexCtor", 50));
        SeedTypeRegistry(
            new TypeRecord { Name = "SimpleCtor", Namespace = "App", Constructors = [new ConstructorRecord { Params = [] }] },
            new TypeRecord { Name = "ComplexCtor", Namespace = "App", Constructors = [new ConstructorRecord { Params = ["ILogger a", "IRepo b", "ICache c", "IConfig d"] }] }
        );

        var result = TestableTargetsTool.GetTestableTargets();
        var doc = JsonDocument.Parse(result);
        var targets = doc.RootElement.GetProperty("targets");

        var simpleScore = targets.EnumerateArray()
            .First(t => t.GetProperty("class").GetString() == "SimpleCtor")
            .GetProperty("score").GetDouble();
        var complexScore = targets.EnumerateArray()
            .First(t => t.GetProperty("class").GetString() == "ComplexCtor")
            .GetProperty("score").GetDouble();

        Assert.True(simpleScore > complexScore);
    }

    // ── Scoring: existing tests reduce score ──

    [Fact]
    public void GetTestableTargets_ExistingTests_ReduceScore()
    {
        SeedCoverageGaps(MakeGap("NoTests", 50), MakeGap("HasTests", 50, existingTestCount: 10));

        var result = TestableTargetsTool.GetTestableTargets();
        var doc = JsonDocument.Parse(result);
        var targets = doc.RootElement.GetProperty("targets");

        var noTestScore = targets.EnumerateArray()
            .First(t => t.GetProperty("class").GetString() == "NoTests")
            .GetProperty("score").GetDouble();
        var hasTestScore = targets.EnumerateArray()
            .First(t => t.GetProperty("class").GetString() == "HasTests")
            .GetProperty("score").GetDouble();

        Assert.True(noTestScore > hasTestScore);
    }

    // ── Scoring: gotchas reduce score ──

    [Fact]
    public void GetTestableTargets_Gotchas_ReduceScore()
    {
        SeedCoverageGaps(MakeGap("Clean", 50), MakeGap("Gotchy", 50));
        SeedGotchas(
            new Gotcha { Type = "Gotchy", Category = "bug", Description = "g1", Date = "2025-01-01" },
            new Gotcha { Type = "Gotchy", Category = "enum", Description = "g2", Date = "2025-01-01" },
            new Gotcha { Type = "Gotchy", Category = "mock", Description = "g3", Date = "2025-01-01" }
        );

        var result = TestableTargetsTool.GetTestableTargets();
        var doc = JsonDocument.Parse(result);
        var targets = doc.RootElement.GetProperty("targets");

        var cleanScore = targets.EnumerateArray()
            .First(t => t.GetProperty("class").GetString() == "Clean")
            .GetProperty("score").GetDouble();
        var gotchyScore = targets.EnumerateArray()
            .First(t => t.GetProperty("class").GetString() == "Gotchy")
            .GetProperty("score").GetDouble();

        Assert.True(cleanScore > gotchyScore);
    }

    // ── Scoring: mock recipes boost score ──

    [Fact]
    public void GetTestableTargets_MockRecipes_BoostScore()
    {
        SeedCoverageGaps(MakeGap("WithRecipe", 50), MakeGap("NoRecipe", 50));
        SeedTypeRegistry(
            new TypeRecord { Name = "WithRecipe", Namespace = "App", Constructors = [new ConstructorRecord { Params = ["ILogger _logger"] }] },
            new TypeRecord { Name = "NoRecipe", Namespace = "App", Constructors = [new ConstructorRecord { Params = ["IEventBus _bus"] }] }
        );
        SeedMockRecipes(new MockRecipe { Interface = "ILogger", Recipe = "mock setup", Namespace = "MS" });

        var result = TestableTargetsTool.GetTestableTargets();
        var doc = JsonDocument.Parse(result);
        var targets = doc.RootElement.GetProperty("targets");

        var recipeScore = targets.EnumerateArray()
            .First(t => t.GetProperty("class").GetString() == "WithRecipe")
            .GetProperty("score").GetDouble();
        var noRecipeScore = targets.EnumerateArray()
            .First(t => t.GetProperty("class").GetString() == "NoRecipe")
            .GetProperty("score").GetDouble();

        Assert.True(recipeScore > noRecipeScore);
    }

    // ── Cross-join with test inventory ──

    [Fact]
    public void GetTestableTargets_TestInventory_UsesHigherTestCount()
    {
        SeedCoverageGaps(MakeGap("InvClass", 50, existingTestCount: 2));
        SeedTestInventory(new TestInventoryEntry { Class = "InvClass", TestCount = 8 });

        var result = TestableTargetsTool.GetTestableTargets();
        var doc = JsonDocument.Parse(result);
        var target = doc.RootElement.GetProperty("targets")[0];

        // Should use Math.Max(2, 8) = 8
        Assert.Equal(8, target.GetProperty("existingTestCount").GetInt32());
    }

    // ── No matching targets after all filters ──

    [Fact]
    public void GetTestableTargets_NoMatchingTargets_ReturnsMessage()
    {
        SeedCoverageGaps(MakeGap("OnlyAbstract", 50));
        SeedTypeRegistry(new TypeRecord { Name = "OnlyAbstract", Namespace = "App", IsAbstract = true });

        var result = TestableTargetsTool.GetTestableTargets(excludeAbstract: true);

        Assert.Contains("No testable targets found", result);
    }

    // ── Reason string includes relevant details ──

    [Fact]
    public void GetTestableTargets_BuildsReasonString_WithDetails()
    {
        SeedCoverageGaps(MakeGap("Detailed", 42, existingTestCount: 3));
        SeedTypeRegistry(new TypeRecord
        {
            Name = "Detailed",
            Namespace = "App",
            Constructors = [new ConstructorRecord { Params = ["ILogger _logger", "IRepo _repo"] }]
        });
        SeedGotchas(new Gotcha { Type = "Detailed", Category = "bug", Description = "test", Date = "2025-01-01" });

        var result = TestableTargetsTool.GetTestableTargets();
        var doc = JsonDocument.Parse(result);
        var reason = doc.RootElement.GetProperty("targets")[0].GetProperty("reason").GetString()!;

        Assert.Contains("42 uncovered lines", reason);
        Assert.Contains("all-interface ctor", reason);
        Assert.Contains("2 params", reason);
        Assert.Contains("all mockable", reason);
        Assert.Contains("1 known gotchas", reason);
        Assert.Contains("3 existing tests", reason);
    }

    // ── Filters JSON included in response ──

    [Fact]
    public void GetTestableTargets_IncludesFiltersInResponse()
    {
        SeedCoverageGaps(MakeGap("Any", 50));

        var result = TestableTargetsTool.GetTestableTargets(top: 3, maxCtorParams: 4, maxTotalLines: 300);
        var doc = JsonDocument.Parse(result);
        var filters = doc.RootElement.GetProperty("filters");

        Assert.Equal(3, filters.GetProperty("top").GetInt32());
        Assert.Equal(4, filters.GetProperty("maxCtorParams").GetInt32());
        Assert.Equal(300, filters.GetProperty("maxTotalLines").GetInt32());
    }

    // ── Target properties populated ──

    [Fact]
    public void GetTestableTargets_PopulatesTargetProperties()
    {
        SeedCoverageGaps(new CoverageGap
        {
            Class = "FullTarget",
            Namespace = "App.Core",
            File = "src/Core/FullTarget.cs",
            TotalLines = 200,
            CoveredLines = 150,
            UncoveredLines = 50,
            CoveragePercent = 75.0,
            Testability = "high",
            UncoveredMethods =
            [
                new UncoveredMethod { Name = "Execute", StartLine = 10, EndLine = 50, UncoveredLines = 30 },
                new UncoveredMethod { Name = "Validate", StartLine = 55, EndLine = 70, UncoveredLines = 20 }
            ]
        });
        SeedTypeRegistry(new TypeRecord
        {
            Name = "FullTarget",
            Namespace = "App.Core",
            BaseType = "BaseProcessor",
            Constructors = [new ConstructorRecord { Params = ["ILogger _logger"] }]
        });

        var result = TestableTargetsTool.GetTestableTargets();
        var doc = JsonDocument.Parse(result);
        var target = doc.RootElement.GetProperty("targets")[0];

        Assert.Equal("FullTarget", target.GetProperty("class").GetString());
        Assert.Equal("App.Core", target.GetProperty("namespace").GetString());
        Assert.Equal(200, target.GetProperty("totalLines").GetInt32());
        Assert.Equal(50, target.GetProperty("uncoveredLines").GetInt32());
        Assert.Equal(75.0, target.GetProperty("coveragePercent").GetDouble());
        Assert.Equal(2, target.GetProperty("uncoveredMethodCount").GetInt32());
        Assert.Equal(1, target.GetProperty("ctorParamCount").GetInt32());
        Assert.Equal(1, target.GetProperty("mockableParamCount").GetInt32());
        Assert.Equal("BaseProcessor", target.GetProperty("baseType").GetString());
    }

    // ── Session learning: failures reduce score ──

    [Fact]
    public void GetTestableTargets_PastFailures_ReduceScore()
    {
        SeedCoverageGaps(MakeGap("Fresh", 50), MakeGap("Failed", 50));
        SeedSessions(new SessionRecord
        {
            SessionId = "s1",
            Model = "claude-sonnet",
            ClassesAttempted = ["Failed"],
            ClassesFailed = [new SessionFailure { Class = "Failed", Reason = "DI error" }]
        });

        var result = TestableTargetsTool.GetTestableTargets();
        var doc = JsonDocument.Parse(result);
        var targets = doc.RootElement.GetProperty("targets");

        var freshScore = targets.EnumerateArray()
            .First(t => t.GetProperty("class").GetString() == "Fresh")
            .GetProperty("score").GetDouble();
        var failedScore = targets.EnumerateArray()
            .First(t => t.GetProperty("class").GetString() == "Failed")
            .GetProperty("score").GetDouble();

        Assert.True(freshScore > failedScore, $"Fresh ({freshScore}) should score higher than Failed ({failedScore})");
    }

    // ── Session learning: successes deprioritize ──

    [Fact]
    public void GetTestableTargets_PastSuccesses_DeprioritizeClass()
    {
        SeedCoverageGaps(MakeGap("Untouched", 50), MakeGap("AlreadyDone", 50));
        SeedSessions(new SessionRecord
        {
            SessionId = "s1",
            Model = "claude-sonnet",
            ClassesAttempted = ["AlreadyDone"],
            ClassesSucceeded = ["AlreadyDone"]
        });

        var result = TestableTargetsTool.GetTestableTargets();
        var doc = JsonDocument.Parse(result);
        var targets = doc.RootElement.GetProperty("targets");

        var untouchedScore = targets.EnumerateArray()
            .First(t => t.GetProperty("class").GetString() == "Untouched")
            .GetProperty("score").GetDouble();
        var doneScore = targets.EnumerateArray()
            .First(t => t.GetProperty("class").GetString() == "AlreadyDone")
            .GetProperty("score").GetDouble();

        Assert.True(untouchedScore > doneScore);
    }

    // ── Session learning: reason includes session info ──

    [Fact]
    public void GetTestableTargets_SessionHistory_IncludedInReason()
    {
        SeedCoverageGaps(MakeGap("Retried", 50));
        SeedSessions(
            new SessionRecord
            {
                SessionId = "s1",
                Model = "claude-sonnet",
                ClassesAttempted = ["Retried"],
                ClassesFailed = [new SessionFailure { Class = "Retried", Reason = "error" }]
            },
            new SessionRecord
            {
                SessionId = "s2",
                Model = "gpt-4",
                ClassesAttempted = ["Retried"],
                ClassesSucceeded = ["Retried"]
            }
        );

        var result = TestableTargetsTool.GetTestableTargets();
        var doc = JsonDocument.Parse(result);
        var reason = doc.RootElement.GetProperty("targets")[0].GetProperty("reason").GetString()!;

        Assert.Contains("succeeded in 1 past session", reason);
        Assert.Contains("failed in 1 past session", reason);
    }

    // ── Session learning: pastSuccesses and pastFailures in output ──

    [Fact]
    public void GetTestableTargets_SessionCounts_InTargetOutput()
    {
        SeedCoverageGaps(MakeGap("Tracked", 50));
        SeedSessions(
            new SessionRecord { SessionId = "s1", Model = "m", ClassesAttempted = ["Tracked"], ClassesSucceeded = ["Tracked"] },
            new SessionRecord { SessionId = "s2", Model = "m", ClassesAttempted = ["Tracked"], ClassesFailed = [new SessionFailure { Class = "Tracked", Reason = "err" }] },
            new SessionRecord { SessionId = "s3", Model = "m", ClassesAttempted = ["Tracked"], ClassesFailed = [new SessionFailure { Class = "Tracked", Reason = "err2" }] }
        );

        var result = TestableTargetsTool.GetTestableTargets();
        var doc = JsonDocument.Parse(result);
        var target = doc.RootElement.GetProperty("targets")[0];

        Assert.Equal(1, target.GetProperty("pastSuccesses").GetInt32());
        Assert.Equal(2, target.GetProperty("pastFailures").GetInt32());
    }

    // ── CalculateScore unit test: session penalties ──

    [Fact]
    public void CalculateScore_WithFailures_ReducesScore()
    {
        var baseScore = TestableTargetsTool.CalculateScore(50, "high", 0, 0, 0, 0, 0, 0, 0);
        var failScore = TestableTargetsTool.CalculateScore(50, "high", 0, 0, 0, 0, 0, 0, 2);

        Assert.True(baseScore > failScore);
        // 2 failures = 0.7^2 = 0.49 multiplier
        Assert.InRange(failScore / baseScore, 0.45, 0.55);
    }

    [Fact]
    public void CalculateScore_WithSuccesses_ReducesScore()
    {
        var baseScore = TestableTargetsTool.CalculateScore(50, "high", 0, 0, 0, 0, 0, 0, 0);
        var successScore = TestableTargetsTool.CalculateScore(50, "high", 0, 0, 0, 0, 0, 1, 0);

        Assert.True(baseScore > successScore);
        // 1 success = 0.5 multiplier
        Assert.InRange(successScore / baseScore, 0.45, 0.55);
    }

    // ── Error path coverage ──

    [Fact]
    public void GetTestableTargets_InvalidNamespace_ReturnsError()
    {
        var result = TestableTargetsTool.GetTestableTargets(ns: "\0");

        Assert.StartsWith("ERROR in GetTestableTargets", result);
    }

    // ── Static class filter (covers L109) ──

    [Fact]
    public void GetTestableTargets_StaticClasses_AreExcluded()
    {
        SeedCoverageGaps(MakeGap("StaticUtil", 50), MakeGap("NormalClass", 50));
        SeedTypeRegistry(
            new TypeRecord { Name = "StaticUtil", Namespace = "App", IsStatic = true },
            new TypeRecord { Name = "NormalClass", Namespace = "App" }
        );

        var result = TestableTargetsTool.GetTestableTargets();
        var doc = JsonDocument.Parse(result);
        var targets = doc.RootElement.GetProperty("targets");

        var names = targets.EnumerateArray().Select(t => t.GetProperty("class").GetString()).ToList();
        Assert.DoesNotContain("StaticUtil", names);
        Assert.Contains("NormalClass", names);
    }

    // ── Case-insensitive type lookup fallback (covers L109) ──

    [Fact]
    public void GetTestableTargets_CaseInsensitiveTypeMatch_StillFiltersStatic()
    {
        // Coverage gap uses lowercase "staticutil", type registry has PascalCase "StaticUtil"
        // Exact match fails → falls through to case-insensitive match (L109) → still filters static
        SeedCoverageGaps(MakeGap("staticutil", 50), MakeGap("Normal", 50));
        SeedTypeRegistry(
            new TypeRecord { Name = "StaticUtil", Namespace = "App", IsStatic = true },
            new TypeRecord { Name = "Normal", Namespace = "App" }
        );

        var result = TestableTargetsTool.GetTestableTargets();
        var doc = JsonDocument.Parse(result);
        var names = doc.RootElement.GetProperty("targets")
            .EnumerateArray()
            .Select(t => t.GetProperty("class").GetString())
            .ToList();

        Assert.DoesNotContain("staticutil", names);
        Assert.Single(names);
    }

    // ── CalculateScore: uncovered testability + ctor branches (covers L242, L244, L253, L255) ──

    [Theory]
    [InlineData("medium", 0.65, 0.75)]
    [InlineData("low", 0.25, 0.35)]
    [InlineData(null, 0.45, 0.55)]
    public void CalculateScore_Testability_AppliesExpectedMultiplier(string? testability, double expectedMinRatio, double expectedMaxRatio)
    {
        var highScore = TestableTargetsTool.CalculateScore(100, "high", 0, 0, 0, 0, 0);
        var score = TestableTargetsTool.CalculateScore(100, testability, 0, 0, 0, 0, 0);

        Assert.InRange(score / highScore, expectedMinRatio, expectedMaxRatio);
    }

    [Fact]
    public void CalculateScore_ThreeCtorParams_ZeroMockable_AppliesHalfMultiplier()
    {
        var zeroParamScore = TestableTargetsTool.CalculateScore(100, "high", 0, 0, 0, 0, 0);
        var threeParamScore = TestableTargetsTool.CalculateScore(100, "high", 3, 0, 0, 0, 0);

        // 0 params = 1.2x, 3 params with 0 mockable = 0.5x → ratio = 0.5/1.2 ≈ 0.417
        Assert.InRange(threeParamScore / zeroParamScore, 0.38, 0.45);
    }

    [Fact]
    public void CalculateScore_FivePlusCtorParams_ZeroMockable_AppliesHalfMultiplier()
    {
        var zeroParamScore = TestableTargetsTool.CalculateScore(100, "high", 0, 0, 0, 0, 0);
        var fiveParamScore = TestableTargetsTool.CalculateScore(100, "high", 5, 0, 0, 0, 0);

        // 0 params = 1.2x, 5 params with 0 mockable = 0.5x → ratio = 0.5/1.2 ≈ 0.417
        Assert.InRange(fiveParamScore / zeroParamScore, 0.38, 0.45);
    }

    // ── BuildReason: complex ctor (covers L299) ──

    [Fact]
    public void GetTestableTargets_AllInterfaceCtor_ReasonSaysAllInterface()
    {
        SeedCoverageGaps(MakeGap("ComplexClass", 50));
        SeedTypeRegistry(new TypeRecord
        {
            Name = "ComplexClass",
            Namespace = "App",
            Constructors = [new ConstructorRecord
            {
                Params = ["ILogger _a", "IRepo _b", "ICache _c", "IBus _d", "IMapper _e"]
            }]
        });

        var result = TestableTargetsTool.GetTestableTargets(maxCtorParams: 10);
        var doc = JsonDocument.Parse(result);
        var reason = doc.RootElement.GetProperty("targets")[0].GetProperty("reason").GetString()!;

        Assert.Contains("all-interface ctor", reason);
        Assert.Contains("5 params", reason);
        Assert.Contains("all mockable", reason);
    }

    // ── Concrete dependency penalty tests ──

    [Fact]
    public void CalculateScore_OneConcreteDep_PenalizesScore()
    {
        // 2 params, 1 interface, 0 concrete (from test perspective, concreteParamCount=0 default)
        var allInterfaceScore = TestableTargetsTool.CalculateScore(100, "high", 2, 2, 0, 0, 0);
        // 2 params, 1 interface, 1 concrete
        var oneConcreteScore = TestableTargetsTool.CalculateScore(100, "high", 2, 1, 0, 0, 0,
            concreteParamCount: 1);

        Assert.True(allInterfaceScore > oneConcreteScore,
            $"All-interface ({allInterfaceScore:F1}) should score higher than one-concrete ({oneConcreteScore:F1})");
    }

    [Fact]
    public void CalculateScore_CoupledDep_HeavyPenalty()
    {
        // 1 concrete param
        var concreteScore = TestableTargetsTool.CalculateScore(100, "high", 1, 0, 0, 0, 0,
            concreteParamCount: 1);
        // 1 concrete param that's also coupled/skip-listed
        var coupledScore = TestableTargetsTool.CalculateScore(100, "high", 1, 0, 0, 0, 0,
            concreteParamCount: 1, coupledParamCount: 1);

        Assert.True(concreteScore > coupledScore,
            $"Concrete ({concreteScore:F1}) should score higher than coupled ({coupledScore:F1})");
        // Coupled penalty is 0.15x on top of concrete 0.3x — should be dramatic
        Assert.InRange(coupledScore / concreteScore, 0.10, 0.20);
    }

    [Fact]
    public void CalculateScore_FourInterfaceParams_BetterThanOneConcrete()
    {
        // The key improvement: 4 all-interface params should score better than 1 coupled concrete param
        var fourInterface = TestableTargetsTool.CalculateScore(100, "high", 4, 4, 0, 0, 0);
        var oneCoupled = TestableTargetsTool.CalculateScore(100, "high", 1, 0, 0, 0, 0,
            concreteParamCount: 1, coupledParamCount: 1);

        Assert.True(fourInterface > oneCoupled,
            $"4 interface params ({fourInterface:F1}) should beat 1 coupled concrete ({oneCoupled:F1})");
    }

    [Fact]
    public void CalculateScore_AllInterfaceParams_GetsFullMockableBonus()
    {
        // 3 params, all interfaces → mockableRatio = 1.0 → multiplier = 0.5 + 0.6 = 1.1
        // But also: 3 interfaces with 0 recipes → recipe coverage = 0.7x
        // Net: 1.1 * 0.7 = 0.77, vs 0 params: 1.2 → ratio ≈ 0.642
        var score = TestableTargetsTool.CalculateScore(100, "high", 3, 3, 0, 0, 0);
        var zeroParam = TestableTargetsTool.CalculateScore(100, "high", 0, 0, 0, 0, 0);

        Assert.InRange(score / zeroParam, 0.60, 0.68);
    }

    [Fact]
    public void GetTestableTargets_ConcreteParam_ReasonShowsWarning()
    {
        SeedCoverageGaps(MakeGap("Coupled", 50));
        SeedTypeRegistry(new TypeRecord
        {
            Name = "Coupled",
            Namespace = "App",
            Constructors = [new ConstructorRecord
            {
                Params = ["ILogger _logger", "MyExtension _ext"]
            }]
        });

        var result = TestableTargetsTool.GetTestableTargets();
        var doc = JsonDocument.Parse(result);
        var target = doc.RootElement.GetProperty("targets")[0];
        var reason = target.GetProperty("reason").GetString()!;

        Assert.Contains("1 concrete", reason);
        Assert.Contains("MyExtension", reason);
        Assert.Equal(1, target.GetProperty("concreteParamCount").GetInt32());
    }

    [Fact]
    public void GetTestableTargets_CoupledAssessedParam_CountedAsCoupled()
    {
        SeedCoverageGaps(MakeGap("NeedsHeavy", 50));
        SeedTypeRegistry(new TypeRecord
        {
            Name = "NeedsHeavy",
            Namespace = "App",
            Constructors = [new ConstructorRecord
            {
                Params = ["HeavyDep _dep"]
            }]
        });
        SeedAssessments(new Assessment
        {
            Class = "HeavyDep",
            Verdict = "coupled",
            Reasoning = "too many deps",
            Date = "2025-01-01"
        });

        var result = TestableTargetsTool.GetTestableTargets(excludeAssessed: false);
        var doc = JsonDocument.Parse(result);
        var target = doc.RootElement.GetProperty("targets")[0];

        Assert.Equal(1, target.GetProperty("concreteParamCount").GetInt32());
        Assert.Equal(1, target.GetProperty("coupledParamCount").GetInt32());
    }

    // ── Stub/empty-body detection ──

    [Fact]
    public void GetTestableTargets_ZeroUncoveredMethods_Excluded()
    {
        // Class has uncovered lines (e.g., auto-property boilerplate) but no named methods
        SeedCoverageGaps(new CoverageGap
        {
            Class = "DataPoco",
            Namespace = "App",
            TotalLines = 20,
            CoveredLines = 10,
            UncoveredLines = 10,
            CoveragePercent = 50,
            Testability = "high",
            UncoveredMethods = [] // No methods — just property lines
        });

        var result = TestableTargetsTool.GetTestableTargets();

        Assert.Contains("No testable targets found", result);
    }

    [Fact]
    public void GetTestableTargets_OnlyPropertyAccessors_Excluded()
    {
        // Class has only get_/set_ property accessors as uncovered methods
        SeedCoverageGaps(new CoverageGap
        {
            Class = "PocoModel",
            Namespace = "App",
            TotalLines = 30,
            CoveredLines = 10,
            UncoveredLines = 20,
            CoveragePercent = 33.3,
            Testability = "high",
            UncoveredMethods =
            [
                new UncoveredMethod { Name = "get_Name", StartLine = 5, EndLine = 5, UncoveredLines = 1 },
                new UncoveredMethod { Name = "set_Name", StartLine = 6, EndLine = 6, UncoveredLines = 1 },
                new UncoveredMethod { Name = "get_Value", StartLine = 8, EndLine = 8, UncoveredLines = 1 }
            ]
        });

        var result = TestableTargetsTool.GetTestableTargets();

        Assert.Contains("No testable targets found", result);
    }

    [Fact]
    public void GetTestableTargets_MixedAccessorsAndMethods_Included()
    {
        // Class has SOME property accessors but also real methods → should be included
        SeedCoverageGaps(new CoverageGap
        {
            Class = "MixedClass",
            Namespace = "App",
            TotalLines = 50,
            CoveredLines = 20,
            UncoveredLines = 30,
            CoveragePercent = 40,
            Testability = "high",
            UncoveredMethods =
            [
                new UncoveredMethod { Name = "get_Name", StartLine = 5, EndLine = 5, UncoveredLines = 1 },
                new UncoveredMethod { Name = "Execute", StartLine = 10, EndLine = 30, UncoveredLines = 20 }
            ]
        });

        var result = TestableTargetsTool.GetTestableTargets();
        var doc = JsonDocument.Parse(result);

        Assert.Equal(1, doc.RootElement.GetProperty("count").GetInt32());
    }

    // ── IsPropertyAccessor unit tests ──

    [Theory]
    [InlineData("get_Name", true)]
    [InlineData("set_Name", true)]
    [InlineData("get_", true)]
    [InlineData("set_", true)]
    [InlineData("Execute", false)]
    [InlineData("GetValue", false)]
    [InlineData("SetValue", false)]
    [InlineData("Reset", false)]
    public void IsPropertyAccessor_DetectsAccessors(string methodName, bool expected)
    {
        Assert.Equal(expected, TestableTargetsTool.IsPropertyAccessor(methodName));
    }

    // ── NormalizeName unit tests ──

    [Theory]
    [InlineData("SimpleClass", "SimpleClass")]
    [InlineData("Parent/Nested", "Nested")]
    [InlineData("A/B/C", "C")]
    [InlineData("OuterClass/NestedClass", "NestedClass")]
    public void NormalizeName_HandlesNestedClasses(string input, string expected)
    {
        Assert.Equal(expected, TestableTargetsTool.NormalizeName(input));
    }

    // ── Nested class name matching ──

    [Fact]
    public void GetTestableTargets_NestedClassName_MatchesAssessment()
    {
        // Coverage gap has nested class name "Parent/Child"
        // Assessment was recorded for just "Child"
        SeedCoverageGaps(MakeGap("Parent/Child", 50));
        SeedAssessments(new Assessment
        {
            Class = "Child",
            Verdict = "skip",
            Reasoning = "not testable",
            Date = "2025-01-01"
        });

        var result = TestableTargetsTool.GetTestableTargets(excludeAssessed: true);

        // Should be excluded because "Child" assessment matches via bare name
        Assert.Contains("No testable targets found", result);
    }

    [Fact]
    public void GetTestableTargets_NestedClassName_MatchesTestInventory()
    {
        SeedCoverageGaps(MakeGap("Outer/Inner", 50, existingTestCount: 0));
        SeedTestInventory(new TestInventoryEntry { Class = "Inner", TestCount = 10 });

        var result = TestableTargetsTool.GetTestableTargets();
        var doc = JsonDocument.Parse(result);
        var target = doc.RootElement.GetProperty("targets")[0];

        // Should find test inventory via bare name fallback
        Assert.Equal(10, target.GetProperty("existingTestCount").GetInt32());
    }

    [Fact]
    public void GetTestableTargets_NestedClassName_MatchesTypeRegistry()
    {
        SeedCoverageGaps(MakeGap("ThreadManager/TaskScheduler", 50));
        SeedTypeRegistry(new TypeRecord
        {
            Name = "TaskScheduler",
            Namespace = "App",
            IsAbstract = true
        });

        // The abstract filter should work via bare name matching
        var result = TestableTargetsTool.GetTestableTargets(excludeAbstract: true);

        Assert.Contains("No testable targets found", result);
    }

    // ── Fuzzy test inventory matching ──

    [Fact]
    public void TryFuzzyTestMatch_StripBaseSuffix_FindsMatch()
    {
        var inventory = new Dictionary<string, TestInventoryEntry>(StringComparer.OrdinalIgnoreCase)
        {
            ["MyService"] = new TestInventoryEntry { Class = "MyService", TestCount = 25 }
        };

        var found = TestableTargetsTool.TryFuzzyTestMatch(inventory, "MyServiceBase", out var entry);

        Assert.True(found);
        Assert.Equal(25, entry.TestCount);
    }

    [Fact]
    public void TryFuzzyTestMatch_StripImplSuffix_FindsMatch()
    {
        var inventory = new Dictionary<string, TestInventoryEntry>(StringComparer.OrdinalIgnoreCase)
        {
            ["Parser"] = new TestInventoryEntry { Class = "Parser", TestCount = 10 }
        };

        var found = TestableTargetsTool.TryFuzzyTestMatch(inventory, "ParserImpl", out var entry);

        Assert.True(found);
        Assert.Equal(10, entry.TestCount);
    }

    [Fact]
    public void TryFuzzyTestMatch_PrefixMatch_FindsMatch()
    {
        var inventory = new Dictionary<string, TestInventoryEntry>(StringComparer.OrdinalIgnoreCase)
        {
            ["AuditEntry"] = new TestInventoryEntry { Class = "AuditEntry", TestCount = 8 }
        };

        var found = TestableTargetsTool.TryFuzzyTestMatch(inventory, "AuditEntryBuilder", out var entry);

        Assert.True(found);
        Assert.Equal(8, entry.TestCount);
    }

    [Fact]
    public void TryFuzzyTestMatch_NoMatch_ReturnsFalse()
    {
        var inventory = new Dictionary<string, TestInventoryEntry>(StringComparer.OrdinalIgnoreCase)
        {
            ["Unrelated"] = new TestInventoryEntry { Class = "Unrelated", TestCount = 5 }
        };

        var found = TestableTargetsTool.TryFuzzyTestMatch(inventory, "TotallyDifferent", out _);

        Assert.False(found);
    }

    // ── Executable-line weighting ──

    [Fact]
    public void CalculateScore_MoreRealMethods_HigherScore()
    {
        var fewMethods = TestableTargetsTool.CalculateScore(100, "high", 0, 0, 0, 0, 0,
            realMethodCount: 1);
        var manyMethods = TestableTargetsTool.CalculateScore(100, "high", 0, 0, 0, 0, 0,
            realMethodCount: 10);

        Assert.True(manyMethods > fewMethods,
            $"Many methods ({manyMethods:F1}) should score higher than few ({fewMethods:F1})");
    }

    [Fact]
    public void CalculateScore_ZeroRealMethods_NoBoost()
    {
        var noMethods = TestableTargetsTool.CalculateScore(100, "high", 0, 0, 0, 0, 0,
            realMethodCount: 0);
        var oneMethod = TestableTargetsTool.CalculateScore(100, "high", 0, 0, 0, 0, 0,
            realMethodCount: 1);

        // 0 methods and 1 method should get same score (no boost below 2)
        Assert.Equal(noMethods, oneMethod);
    }

    // ── Medium-complexity class boost (P0-B) ──

    [Theory]
    [InlineData(0, 1.0)]     // unknown — neutral
    [InlineData(20, 0.7)]    // too small — penalty
    [InlineData(80, 0.9)]    // small — moderate
    [InlineData(200, 1.3)]   // sweet spot — boost
    [InlineData(400, 1.3)]   // sweet spot boundary
    [InlineData(600, 1.0)]   // large — neutral
    [InlineData(1000, 0.8)]  // very large — penalty
    public void CalculateScore_MediumComplexityBoost_AppliesCorrectMultiplier(int totalLines, double expectedMultiplier)
    {
        var baseScore = TestableTargetsTool.CalculateScore(100, "high", 0, 0, 0, 0, 0,
            totalLines: 0); // baseline with unknown totalLines
        var boostedScore = TestableTargetsTool.CalculateScore(100, "high", 0, 0, 0, 0, 0,
            totalLines: totalLines);

        if (totalLines == 0)
        {
            Assert.Equal(baseScore, boostedScore);
        }
        else
        {
            var ratio = boostedScore / baseScore;
            Assert.InRange(ratio, expectedMultiplier - 0.05, expectedMultiplier + 0.05);
        }
    }

    [Fact]
    public void CalculateScore_SweetSpotClass_ScoresHigherThanSmall()
    {
        // 200-line class should score higher than 20-line class with same uncovered lines
        var sweetSpot = TestableTargetsTool.CalculateScore(50, "high", 0, 0, 0, 0, 0,
            totalLines: 200);
        var small = TestableTargetsTool.CalculateScore(50, "high", 0, 0, 0, 0, 0,
            totalLines: 20);

        Assert.True(sweetSpot > small,
            $"Sweet-spot ({sweetSpot:F1}) should score higher than small ({small:F1})");
    }

    // ── Harsher concrete dep penalty (P0-A) ──

    [Fact]
    public void CalculateScore_TwoConcreteDeps_NearZeroScore()
    {
        var cleanScore = TestableTargetsTool.CalculateScore(100, "high", 0, 0, 0, 0, 0);
        var twoConcreteScore = TestableTargetsTool.CalculateScore(100, "high", 2, 0, 0, 0, 0,
            concreteParamCount: 2);

        // 0.3^2 = 0.09 for concrete penalty alone, plus mockable ratio penalty
        Assert.True(twoConcreteScore / cleanScore < 0.1,
            $"Two concrete deps ({twoConcreteScore:F1}) should be <10% of clean ({cleanScore:F1})");
    }

    [Fact]
    public void CalculateScore_OneConcretePlusCoupled_DramaticallyLow()
    {
        var cleanScore = TestableTargetsTool.CalculateScore(100, "high", 0, 0, 0, 0, 0);
        var coupledScore = TestableTargetsTool.CalculateScore(100, "high", 1, 0, 0, 0, 0,
            concreteParamCount: 1, coupledParamCount: 1);

        // 0.3 * 0.15 = 0.045 × mockable penalty → essentially zero
        Assert.True(coupledScore / cleanScore < 0.03,
            $"Coupled concrete ({coupledScore:F1}) should be <3% of clean ({cleanScore:F1})");
    }

    // ── Namespace cluster coupling penalty (P0-C) ──

    [Fact]
    public void CalculateScore_NamespaceCoupledBelow3_NoPenalty()
    {
        var score0 = TestableTargetsTool.CalculateScore(100, "high", 0, 0, 0, 0, 0,
            namespaceCoupledCount: 0);
        var score2 = TestableTargetsTool.CalculateScore(100, "high", 0, 0, 0, 0, 0,
            namespaceCoupledCount: 2);

        Assert.Equal(score0, score2); // no penalty below 3
    }

    [Fact]
    public void CalculateScore_NamespaceCoupled3Plus_Applies85Penalty()
    {
        var clean = TestableTargetsTool.CalculateScore(100, "high", 0, 0, 0, 0, 0,
            namespaceCoupledCount: 0);
        var coupled = TestableTargetsTool.CalculateScore(100, "high", 0, 0, 0, 0, 0,
            namespaceCoupledCount: 3);

        Assert.InRange(coupled / clean, 0.80, 0.90); // 0.85x penalty
    }

    // ── Interface gotcha penalty (P3) ──

    [Fact]
    public void CalculateScore_InterfaceGotchas_ReduceScore()
    {
        var clean = TestableTargetsTool.CalculateScore(100, "high", 2, 2, 0, 0, 0);
        var withGotchas = TestableTargetsTool.CalculateScore(100, "high", 2, 2, 0, 0, 0,
            interfaceGotchaCount: 2);

        Assert.True(clean > withGotchas,
            $"Clean ({clean:F1}) should score higher than with gotchas ({withGotchas:F1})");
        // 0.7^2 = 0.49
        Assert.InRange(withGotchas / clean, 0.44, 0.54);
    }

    [Fact]
    public void CalculateScore_ZeroInterfaceGotchas_NoPenalty()
    {
        var score0 = TestableTargetsTool.CalculateScore(100, "high", 2, 2, 0, 0, 0,
            interfaceGotchaCount: 0);
        var scoreBase = TestableTargetsTool.CalculateScore(100, "high", 2, 2, 0, 0, 0);

        Assert.Equal(score0, scoreBase);
    }

    // ── IsBoilerplateMethod unit tests ──

    [Theory]
    [InlineData("get_Name", true)]
    [InlineData("set_Name", true)]
    [InlineData(".ctor", true)]
    [InlineData(".cctor", true)]
    [InlineData("Execute", false)]
    [InlineData("GetValue", false)]
    [InlineData("SetValue", false)]
    [InlineData("Reset", false)]
    [InlineData("ToString", false)]
    public void IsBoilerplateMethod_DetectsBoilerplate(string methodName, bool expected)
    {
        Assert.Equal(expected, TestableTargetsTool.IsBoilerplateMethod(methodName));
    }

    // ── POCO filter: .ctor-only classes excluded ──

    [Fact]
    public void GetTestableTargets_CtorOnlyMethods_Excluded()
    {
        // Class has only .ctor and property accessors as uncovered methods → POCO
        SeedCoverageGaps(new CoverageGap
        {
            Class = "GlobalMetadataYaml",
            Namespace = "App.Models",
            TotalLines = 25,
            CoveredLines = 10,
            UncoveredLines = 15,
            CoveragePercent = 40,
            Testability = "high",
            UncoveredMethods =
            [
                new UncoveredMethod { Name = ".ctor", StartLine = 3, EndLine = 10, UncoveredLines = 7 },
                new UncoveredMethod { Name = "get_Title", StartLine = 12, EndLine = 12, UncoveredLines = 1 },
                new UncoveredMethod { Name = "set_Title", StartLine = 13, EndLine = 13, UncoveredLines = 1 }
            ]
        });

        var result = TestableTargetsTool.GetTestableTargets();

        Assert.Contains("No testable targets found", result);
    }

    [Fact]
    public void GetTestableTargets_CtorPlusRealMethod_Included()
    {
        // Class has .ctor AND a real method → NOT a POCO, should be included
        SeedCoverageGaps(new CoverageGap
        {
            Class = "ServiceClass",
            Namespace = "App",
            TotalLines = 100,
            CoveredLines = 50,
            UncoveredLines = 50,
            CoveragePercent = 50,
            Testability = "high",
            UncoveredMethods =
            [
                new UncoveredMethod { Name = ".ctor", StartLine = 3, EndLine = 10, UncoveredLines = 7 },
                new UncoveredMethod { Name = "Process", StartLine = 12, EndLine = 50, UncoveredLines = 30 }
            ]
        });

        var result = TestableTargetsTool.GetTestableTargets();
        var doc = JsonDocument.Parse(result);

        Assert.Equal(1, doc.RootElement.GetProperty("count").GetInt32());
    }

    // ── Namespace cluster coupling integration ──

    [Fact]
    public void GetTestableTargets_NamespaceWithManyCoupled_PenalizesClean()
    {
        // Namespace "App.Heavy" has 3 coupled classes + 1 clean target
        SeedCoverageGaps(
            MakeGap("CleanInBadNs", 50, totalLines: 200),
            MakeGap("CleanInGoodNs", 50, totalLines: 200)
        );
        // Override namespaces
        var gapStore = new JsonLineStore<CoverageGap>(RepoConfig.CoverageGapsPath(TempDir));
        gapStore.WriteAll(
        [
            new CoverageGap { Class = "CleanInBadNs", Namespace = "App.Heavy", TotalLines = 200, UncoveredLines = 50, CoveragePercent = 75, Testability = "high",
                UncoveredMethods = [new UncoveredMethod { Name = "DoWork", StartLine = 10, EndLine = 50, UncoveredLines = 50 }] },
            new CoverageGap { Class = "CleanInGoodNs", Namespace = "App.Light", TotalLines = 200, UncoveredLines = 50, CoveragePercent = 75, Testability = "high",
                UncoveredMethods = [new UncoveredMethod { Name = "DoWork", StartLine = 10, EndLine = 50, UncoveredLines = 50 }] }
        ]);
        StoreRegistry.Reset();

        // Put 3 coupled assessments in App.Heavy namespace — need coverage gaps for namespace resolution
        var extraGaps = new JsonLineStore<CoverageGap>(RepoConfig.CoverageGapsPath(TempDir));
        extraGaps.WriteAll(
        [
            new CoverageGap { Class = "CleanInBadNs", Namespace = "App.Heavy", TotalLines = 200, UncoveredLines = 50, CoveragePercent = 75, Testability = "high",
                UncoveredMethods = [new UncoveredMethod { Name = "DoWork", StartLine = 10, EndLine = 50, UncoveredLines = 50 }] },
            new CoverageGap { Class = "CleanInGoodNs", Namespace = "App.Light", TotalLines = 200, UncoveredLines = 50, CoveragePercent = 75, Testability = "high",
                UncoveredMethods = [new UncoveredMethod { Name = "DoWork", StartLine = 10, EndLine = 50, UncoveredLines = 50 }] },
            new CoverageGap { Class = "Heavy1", Namespace = "App.Heavy", TotalLines = 500, UncoveredLines = 100, CoveragePercent = 80, Testability = "high",
                UncoveredMethods = [new UncoveredMethod { Name = "X", StartLine = 1, EndLine = 2, UncoveredLines = 1 }] },
            new CoverageGap { Class = "Heavy2", Namespace = "App.Heavy", TotalLines = 500, UncoveredLines = 100, CoveragePercent = 80, Testability = "high",
                UncoveredMethods = [new UncoveredMethod { Name = "X", StartLine = 1, EndLine = 2, UncoveredLines = 1 }] },
            new CoverageGap { Class = "Heavy3", Namespace = "App.Heavy", TotalLines = 500, UncoveredLines = 100, CoveragePercent = 80, Testability = "high",
                UncoveredMethods = [new UncoveredMethod { Name = "X", StartLine = 1, EndLine = 2, UncoveredLines = 1 }] }
        ]);
        StoreRegistry.Reset();

        SeedAssessments(
            new Assessment { Class = "Heavy1", Verdict = "coupled", Reasoning = "r", Date = "2025-01-01" },
            new Assessment { Class = "Heavy2", Verdict = "skip", Reasoning = "r", Date = "2025-01-01" },
            new Assessment { Class = "Heavy3", Verdict = "coupled", Reasoning = "r", Date = "2025-01-01" }
        );
        StoreRegistry.Reset();

        var result = TestableTargetsTool.GetTestableTargets(excludeAssessed: false, maxTotalLines: 1000);
        var doc = JsonDocument.Parse(result);
        var targets = doc.RootElement.GetProperty("targets");

        var badNsScore = targets.EnumerateArray()
            .First(t => t.GetProperty("class").GetString() == "CleanInBadNs")
            .GetProperty("score").GetDouble();
        var goodNsScore = targets.EnumerateArray()
            .First(t => t.GetProperty("class").GetString() == "CleanInGoodNs")
            .GetProperty("score").GetDouble();

        Assert.True(goodNsScore > badNsScore,
            $"Good namespace ({goodNsScore:F1}) should score higher than bad namespace ({badNsScore:F1})");
    }

    // ── Interface gotcha propagation integration ──

    [Fact]
    public void GetTestableTargets_InterfaceWithMockGotcha_ReducesScore()
    {
        SeedCoverageGaps(MakeGap("CleanDeps", 50), MakeGap("GotchaDeps", 50));
        SeedTypeRegistry(
            new TypeRecord { Name = "CleanDeps", Namespace = "App", Constructors = [new ConstructorRecord { Params = ["ILogger _a"] }] },
            new TypeRecord { Name = "GotchaDeps", Namespace = "App", Constructors = [new ConstructorRecord { Params = ["IProblematic _b"] }] }
        );
        // IProblematic has a mock gotcha
        SeedGotchas(new Gotcha
        {
            Type = "IProblematic",
            Category = "mock",
            Description = "CS0854: self-referencing loop when mocking",
            Date = "2025-01-01"
        });

        var result = TestableTargetsTool.GetTestableTargets();
        var doc = JsonDocument.Parse(result);
        var targets = doc.RootElement.GetProperty("targets");

        var cleanScore = targets.EnumerateArray()
            .First(t => t.GetProperty("class").GetString() == "CleanDeps")
            .GetProperty("score").GetDouble();
        var gotchaScore = targets.EnumerateArray()
            .First(t => t.GetProperty("class").GetString() == "GotchaDeps")
            .GetProperty("score").GetDouble();

        Assert.True(cleanScore > gotchaScore,
            $"Clean ({cleanScore:F1}) should score higher than gotcha deps ({gotchaScore:F1})");
    }

    // ── BuildReason includes complexity tier and namespace info ──

    [Fact]
    public void GetTestableTargets_Reason_IncludesComplexityTier()
    {
        SeedCoverageGaps(new CoverageGap
        {
            Class = "MediumClass",
            Namespace = "App",
            TotalLines = 200,
            CoveredLines = 150,
            UncoveredLines = 50,
            CoveragePercent = 75,
            Testability = "high",
            UncoveredMethods = [new UncoveredMethod { Name = "Run", StartLine = 10, EndLine = 50, UncoveredLines = 50 }]
        });

        var result = TestableTargetsTool.GetTestableTargets();
        var doc = JsonDocument.Parse(result);
        var reason = doc.RootElement.GetProperty("targets")[0].GetProperty("reason").GetString()!;

        Assert.Contains("200 total lines", reason);
        Assert.Contains("sweet-spot", reason);
    }

    // ── Self-assessment penalty (when excludeAssessed=false) ──

    [Theory]
    [InlineData("coupled", 0.08, 0.12)]
    [InlineData("skip", 0.08, 0.12)]
    [InlineData("deferred", 0.08, 0.12)]
    [InlineData("testable", 1.0, 1.0)]
    public void CalculateScore_SelfVerdict_AppliesExpectedPenalty(string selfVerdict, double expectedMinRatio, double expectedMaxRatio)
    {
        var clean = TestableTargetsTool.CalculateScore(100, "high", 0, 0, 0, 0, 0);
        var penalized = TestableTargetsTool.CalculateScore(100, "high", 0, 0, 0, 0, 0,
            selfVerdict: selfVerdict);

        Assert.InRange(penalized / clean, expectedMinRatio, expectedMaxRatio);
    }

    // ── Unmockable interface penalty ──

    [Fact]
    public void CalculateScore_OneUnmockableInterface_StrongPenalty()
    {
        var clean = TestableTargetsTool.CalculateScore(100, "high", 2, 2, 0, 0, 0);
        var unmockable = TestableTargetsTool.CalculateScore(100, "high", 2, 2, 0, 0, 0,
            unmockableInterfaceCount: 1);

        Assert.InRange(unmockable / clean, 0.15, 0.25); // 0.2x
    }

    [Fact]
    public void CalculateScore_TwoUnmockableInterfaces_NearZero()
    {
        var clean = TestableTargetsTool.CalculateScore(100, "high", 2, 2, 0, 0, 0);
        var unmockable = TestableTargetsTool.CalculateScore(100, "high", 2, 2, 0, 0, 0,
            unmockableInterfaceCount: 2);

        Assert.True(unmockable / clean < 0.05,
            $"Two unmockable interfaces ({unmockable:F1}) should be <5% of clean ({clean:F1})");
    }

    // ── Base type coupling penalty ──

    [Fact]
    public void CalculateScore_BaseTypeCoupled_SeverePenalty()
    {
        var clean = TestableTargetsTool.CalculateScore(100, "high", 0, 0, 0, 0, 0);
        var coupled = TestableTargetsTool.CalculateScore(100, "high", 0, 0, 0, 0, 0,
            baseTypeCoupled: true);

        Assert.InRange(coupled / clean, 0.13, 0.17); // 0.15x
    }

    // ── Self-assessment integration (excludeAssessed=false) ──

    [Fact]
    public void GetTestableTargets_ExcludeAssessedFalse_CoupledGetsLowScore()
    {
        SeedCoverageGaps(MakeGap("Clean", 50), MakeGap("Coupled", 50));
        SeedAssessments(
            new Assessment { Class = "Coupled", Verdict = "coupled", Reasoning = "deps", Date = "2025-01-01" }
        );

        var result = TestableTargetsTool.GetTestableTargets(excludeAssessed: false);
        var doc = JsonDocument.Parse(result);
        var targets = doc.RootElement.GetProperty("targets");

        var cleanScore = targets.EnumerateArray()
            .First(t => t.GetProperty("class").GetString() == "Clean")
            .GetProperty("score").GetDouble();
        var coupledScore = targets.EnumerateArray()
            .First(t => t.GetProperty("class").GetString() == "Coupled")
            .GetProperty("score").GetDouble();

        Assert.True(cleanScore > coupledScore * 5,
            $"Clean ({cleanScore:F1}) should be >5x Coupled ({coupledScore:F1})");
    }

    [Fact]
    public void GetTestableTargets_ExcludeAssessedFalse_ReasonShowsVerdict()
    {
        SeedCoverageGaps(MakeGap("Coupled", 50));
        SeedAssessments(
            new Assessment { Class = "Coupled", Verdict = "coupled", Reasoning = "deps", Date = "2025-01-01" }
        );

        var result = TestableTargetsTool.GetTestableTargets(excludeAssessed: false);
        var doc = JsonDocument.Parse(result);
        var reason = doc.RootElement.GetProperty("targets")[0].GetProperty("reason").GetString()!;

        Assert.Contains("assessed as 'coupled'", reason);
    }

    // ── Base type coupling integration ──

    [Fact]
    public void GetTestableTargets_CoupledBaseType_GetsPenalized()
    {
        SeedCoverageGaps(MakeGap("ChildClass", 50), MakeGap("CleanClass", 50));
        SeedTypeRegistry(
            new TypeRecord { Name = "ChildClass", Namespace = "App", BaseType = "MyOperationBase" },
            new TypeRecord { Name = "CleanClass", Namespace = "App" }
        );
        SeedAssessments(
            new Assessment { Class = "MyOperationBase", Verdict = "coupled", Reasoning = "heavy deps", Date = "2025-01-01" }
        );

        var result = TestableTargetsTool.GetTestableTargets();
        var doc = JsonDocument.Parse(result);
        var targets = doc.RootElement.GetProperty("targets");

        var cleanScore = targets.EnumerateArray()
            .First(t => t.GetProperty("class").GetString() == "CleanClass")
            .GetProperty("score").GetDouble();
        var childScore = targets.EnumerateArray()
            .First(t => t.GetProperty("class").GetString() == "ChildClass")
            .GetProperty("score").GetDouble();

        Assert.True(cleanScore > childScore * 5,
            $"Clean ({cleanScore:F1}) should be >5x child with coupled base ({childScore:F1})");
    }

    [Fact]
    public void GetTestableTargets_CoupledBaseType_ReasonShowsBaseType()
    {
        SeedCoverageGaps(MakeGap("ChildClass", 50));
        SeedTypeRegistry(
            new TypeRecord { Name = "ChildClass", Namespace = "App", BaseType = "MyOperationBase" }
        );
        SeedAssessments(
            new Assessment { Class = "MyOperationBase", Verdict = "coupled", Reasoning = "heavy deps", Date = "2025-01-01" }
        );

        var result = TestableTargetsTool.GetTestableTargets();
        var doc = JsonDocument.Parse(result);
        var reason = doc.RootElement.GetProperty("targets")[0].GetProperty("reason").GetString()!;

        Assert.Contains("coupled base type", reason);
        Assert.Contains("MyOperationBase", reason);
    }

    // ── Transitive dependency detection (knownBadDeps) ──

    [Fact]
    public void GetTestableTargets_TransitiveConcreteDep_CoupledPenalty()
    {
        // ClassX was assessed as coupled with dependency "MyTestHarnessBase"
        // ClassY has a concrete ctor param of type "MyTestHarnessBase"
        // ClassY should get the coupled param penalty via knownBadDeps
        SeedCoverageGaps(MakeGap("ClassY", 50));
        SeedTypeRegistry(
            new TypeRecord { Name = "ClassY", Namespace = "App",
                Constructors = [new ConstructorRecord { Params = ["MyTestHarnessBase _harness"] }] }
        );
        SeedAssessments(
            new Assessment { Class = "ClassX", Verdict = "coupled", Reasoning = "tight deps",
                Dependencies = ["MyTestHarnessBase"], Date = "2025-01-01" }
        );

        var result = TestableTargetsTool.GetTestableTargets();
        var doc = JsonDocument.Parse(result);
        var targets = doc.RootElement.GetProperty("targets");
        var score = targets[0].GetProperty("score").GetDouble();

        // Should have severe penalty: concrete (0.3x) + coupled via transitive (0.15x)
        var cleanScore = TestableTargetsTool.CalculateScore(50, "high", 0, 0, 0, 0, 0, totalLines: 100);
        Assert.True(score < cleanScore * 0.1,
            $"Transitive coupled ({score:F1}) should be <10% of clean ({cleanScore:F1})");
    }

    [Fact]
    public void GetTestableTargets_TransitiveInterfaceDep_UnmockablePenalty()
    {
        // ClassX was assessed as coupled with dependency "IDocumentService"
        // ClassZ has interface ctor param "IDocumentService"
        // ClassZ should get unmockable interface penalty via knownBadDeps
        SeedCoverageGaps(MakeGap("CleanDep", 50), MakeGap("TransitiveDep", 50));
        SeedTypeRegistry(
            new TypeRecord { Name = "CleanDep", Namespace = "App",
                Constructors = [new ConstructorRecord { Params = ["ILogger _log"] }] },
            new TypeRecord { Name = "TransitiveDep", Namespace = "App",
                Constructors = [new ConstructorRecord { Params = ["IDocumentService _docs"] }] }
        );
        SeedAssessments(
            new Assessment { Class = "SomeOther", Verdict = "coupled", Reasoning = "IDocumentService blocks mocking",
                Dependencies = ["IDocumentService"], Date = "2025-01-01" }
        );

        var result = TestableTargetsTool.GetTestableTargets();
        var doc = JsonDocument.Parse(result);
        var targets = doc.RootElement.GetProperty("targets");

        var cleanScore = targets.EnumerateArray()
            .First(t => t.GetProperty("class").GetString() == "CleanDep")
            .GetProperty("score").GetDouble();
        var transitiveScore = targets.EnumerateArray()
            .First(t => t.GetProperty("class").GetString() == "TransitiveDep")
            .GetProperty("score").GetDouble();

        Assert.True(cleanScore > transitiveScore * 3,
            $"Clean ({cleanScore:F1}) should be >3x transitive unmockable ({transitiveScore:F1})");
    }

    // ── Interface assessment penalty integration ──

    [Fact]
    public void GetTestableTargets_InterfaceAssessedCoupled_ReducesScore()
    {
        SeedCoverageGaps(MakeGap("CleanIface", 50), MakeGap("CoupledIface", 50));
        SeedTypeRegistry(
            new TypeRecord { Name = "CleanIface", Namespace = "App",
                Constructors = [new ConstructorRecord { Params = ["ILogger _log"] }] },
            new TypeRecord { Name = "CoupledIface", Namespace = "App",
                Constructors = [new ConstructorRecord { Params = ["ISomeServiceClient _client"] }] }
        );
        SeedAssessments(
            new Assessment { Class = "ISomeServiceClient", Verdict = "skip",
                Reasoning = "extension methods block mocking", Date = "2025-01-01" }
        );

        var result = TestableTargetsTool.GetTestableTargets();
        var doc = JsonDocument.Parse(result);
        var targets = doc.RootElement.GetProperty("targets");

        var cleanScore = targets.EnumerateArray()
            .First(t => t.GetProperty("class").GetString() == "CleanIface")
            .GetProperty("score").GetDouble();
        var coupledScore = targets.EnumerateArray()
            .First(t => t.GetProperty("class").GetString() == "CoupledIface")
            .GetProperty("score").GetDouble();

        Assert.True(cleanScore > coupledScore * 3,
            $"Clean ({cleanScore:F1}) should be >3x interface-coupled ({coupledScore:F1})");
    }

    [Fact]
    public void GetTestableTargets_InterfaceAssessedCoupled_ReasonShowsUnmockable()
    {
        SeedCoverageGaps(MakeGap("CoupledIface", 50));
        SeedTypeRegistry(
            new TypeRecord { Name = "CoupledIface", Namespace = "App",
                Constructors = [new ConstructorRecord { Params = ["ISomeServiceClient _client"] }] }
        );
        SeedAssessments(
            new Assessment { Class = "ISomeServiceClient", Verdict = "skip",
                Reasoning = "extension methods", Date = "2025-01-01" }
        );

        var result = TestableTargetsTool.GetTestableTargets();
        var doc = JsonDocument.Parse(result);
        var reason = doc.RootElement.GetProperty("targets")[0].GetProperty("reason").GetString()!;

        Assert.Contains("unmockable interface", reason);
    }

    // ── Combined penalties stack correctly ──

    [Fact]
    public void CalculateScore_AllPenaltiesStack_NearZero()
    {
        // Class with coupled self-verdict + base type + unmockable interface + concrete dep
        var score = TestableTargetsTool.CalculateScore(100, "high", 2, 1, 0, 0, 0,
            concreteParamCount: 1, coupledParamCount: 1,
            selfVerdict: "coupled", unmockableInterfaceCount: 1, baseTypeCoupled: true);

        // Should be near zero: all penalties stacking
        Assert.True(score < 0.1, $"All penalties stacked should yield near-zero, got {score:F4}");
    }

    // ── Log-scaled base score ──

    [Fact]
    public void CalculateScore_LogScaleBase_CompressesRange()
    {
        // With linear base, 200-line class was 10x a 20-line class (200/20).
        // With log-scale, 200 → ~77.2, 20 → ~43.4 → ratio ~1.78x (much closer)
        var smallScore = TestableTargetsTool.CalculateScore(20, "high", 0, 0, 0, 0, 0);
        var largeScore = TestableTargetsTool.CalculateScore(200, "high", 0, 0, 0, 0, 0);

        var ratio = largeScore / smallScore;
        Assert.InRange(ratio, 1.5, 2.2); // compressed from 10x to ~1.8x
    }

    [Fact]
    public void CalculateScore_LogScale_PureSmallClassBeatsLargeCoupled()
    {
        // The core fix: a 30-line pure class should beat a 200-line coupled class
        var pureSmall = TestableTargetsTool.CalculateScore(30, "high", 2, 2, 2, 0, 0,
            totalLines: 100); // sweet-spot, all-interface deps, has recipes
        var largeCoupled = TestableTargetsTool.CalculateScore(200, "high", 3, 1, 0, 0, 0,
            concreteParamCount: 2, totalLines: 300); // 2 concrete deps, no recipes

        Assert.True(pureSmall > largeCoupled,
            $"Pure small class ({pureSmall:F1}) should beat large coupled ({largeCoupled:F1})");
    }

    // ── External dependency penalty ──

    [Theory]
    [InlineData(0, 1.0, 1.0)]
    [InlineData(1, 0.45, 0.55)]
    [InlineData(2, 0.20, 0.30)]
    public void CalculateScore_ExternalDeps_AppliesExpectedPenalty(int externalDepCount, double expectedMinRatio, double expectedMaxRatio)
    {
        var clean = TestableTargetsTool.CalculateScore(100, "high", 0, 0, 0, 0, 0);
        var penalized = TestableTargetsTool.CalculateScore(100, "high", 0, 0, 0, 0, 0,
            externalDepCount: externalDepCount);

        Assert.InRange(penalized / clean, expectedMinRatio, expectedMaxRatio);
    }

    // ── ExcludeAssessed: deferred filtering ──

    [Fact]
    public void GetTestableTargets_ExcludeAssessed_FiltersDeferredToo()
    {
        SeedCoverageGaps(MakeGap("Good", 50), MakeGap("Deferred", 50));
        SeedAssessments(
            new Assessment { Class = "Deferred", Verdict = "deferred", Reasoning = "needs refactor", Date = "2025-01-01" }
        );

        var result = TestableTargetsTool.GetTestableTargets(excludeAssessed: true);
        var doc = JsonDocument.Parse(result);

        Assert.Equal(1, doc.RootElement.GetProperty("count").GetInt32());
        Assert.Equal("Good", doc.RootElement.GetProperty("targets")[0].GetProperty("class").GetString());
    }

    [Fact]
    public void GetTestableTargets_ExcludeAssessedFalse_DeferredGetsLowScore()
    {
        SeedCoverageGaps(MakeGap("Clean", 50), MakeGap("Deferred", 50));
        SeedAssessments(
            new Assessment { Class = "Deferred", Verdict = "deferred", Reasoning = "waiting", Date = "2025-01-01" }
        );

        var result = TestableTargetsTool.GetTestableTargets(excludeAssessed: false);
        var doc = JsonDocument.Parse(result);
        var targets = doc.RootElement.GetProperty("targets");

        var cleanScore = targets.EnumerateArray()
            .First(t => t.GetProperty("class").GetString() == "Clean")
            .GetProperty("score").GetDouble();
        var deferredScore = targets.EnumerateArray()
            .First(t => t.GetProperty("class").GetString() == "Deferred")
            .GetProperty("score").GetDouble();

        Assert.True(cleanScore > deferredScore * 5,
            $"Clean ({cleanScore:F1}) should be >5x Deferred ({deferredScore:F1})");
    }

    [Fact]
    public void GetTestableTargets_ExcludeAssessedFalse_ReasonShowsDeferredVerdict()
    {
        SeedCoverageGaps(MakeGap("Deferred", 50));
        SeedAssessments(
            new Assessment { Class = "Deferred", Verdict = "deferred", Reasoning = "waiting", Date = "2025-01-01" }
        );

        var result = TestableTargetsTool.GetTestableTargets(excludeAssessed: false);
        var doc = JsonDocument.Parse(result);
        var reason = doc.RootElement.GetProperty("targets")[0].GetProperty("reason").GetString()!;

        Assert.Contains("assessed as 'deferred'", reason);
    }

    // ── External dependency integration ──

    [Fact]
    public void GetTestableTargets_FileSystemParam_IncreasesExternalDepCount()
    {
        SeedCoverageGaps(MakeGap("FileUser", 50));
        SeedTypeRegistry(new TypeRecord
        {
            Name = "FileUser",
            Namespace = "App",
            Constructors = [new ConstructorRecord
            {
                Params = ["IFileSystem _fs", "ILogger _logger"]
            }]
        });

        var result = TestableTargetsTool.GetTestableTargets();
        var doc = JsonDocument.Parse(result);
        var reason = doc.RootElement.GetProperty("targets")[0].GetProperty("reason").GetString()!;

        Assert.Contains("external service dep", reason);
    }

    [Fact]
    public void GetTestableTargets_HttpClientParam_DetectedAsExternal()
    {
        SeedCoverageGaps(MakeGap("ApiClient", 50));
        SeedTypeRegistry(new TypeRecord
        {
            Name = "ApiClient",
            Namespace = "App",
            Constructors = [new ConstructorRecord
            {
                Params = ["HttpClient _client", "ILogger _logger"]
            }]
        });

        var result = TestableTargetsTool.GetTestableTargets();
        var doc = JsonDocument.Parse(result);
        var reason = doc.RootElement.GetProperty("targets")[0].GetProperty("reason").GetString()!;

        Assert.Contains("external service dep", reason);
    }

    // ── hasExistingTests cliff penalty ──

    [Theory]
    [InlineData(14, 15, 0.0, 0.35)]  // cliff at 15: (1/16 * 0.3) / (1/15) ≈ 0.28
    [InlineData(10, 11, 0.88, 0.96)] // linear diminishing returns: 1/12 vs 1/11 ≈ 0.917
    [InlineData(0, 30, 0.0, 0.015)]  // 1/31 * 0.3 ≈ 0.0097x
    public void CalculateScore_HeavilyTested_CliffPenalty(int baselineTestCount, int targetTestCount, double expectedMinRatio, double expectedMaxRatio)
    {
        var baseline = TestableTargetsTool.CalculateScore(100, "high", 0, 0, 0, baselineTestCount, 0);
        var target = TestableTargetsTool.CalculateScore(100, "high", 0, 0, 0, targetTestCount, 0);

        Assert.InRange(target / baseline, expectedMinRatio, expectedMaxRatio);
    }

    [Fact]
    public void GetTestableTargets_HeavilyTestedClass_ReasonShowsWarning()
    {
        SeedCoverageGaps(MakeGap("WellTested", 50));
        SeedTestInventory(
            new TestInventoryEntry { Class = "WellTested", TestCount = 20, TestMethods = Enumerable.Range(1, 20).Select(i => $"Test{i}").ToList() }
        );

        var result = TestableTargetsTool.GetTestableTargets();
        var doc = JsonDocument.Parse(result);
        var reason = doc.RootElement.GetProperty("targets")[0].GetProperty("reason").GetString()!;

        Assert.Contains("heavily tested", reason);
        Assert.Contains("diminishing ROI", reason);
    }

    [Fact]
    public void GetTestableTargets_FewTests_ReasonDoesNotShowWarning()
    {
        SeedCoverageGaps(MakeGap("LightlyTested", 50));
        SeedTestInventory(
            new TestInventoryEntry { Class = "LightlyTested", TestCount = 5, TestMethods = ["Test1", "Test2", "Test3", "Test4", "Test5"] }
        );

        var result = TestableTargetsTool.GetTestableTargets();
        var doc = JsonDocument.Parse(result);
        var reason = doc.RootElement.GetProperty("targets")[0].GetProperty("reason").GetString()!;

        Assert.Contains("5 existing tests", reason);
        Assert.DoesNotContain("heavily tested", reason);
    }

    // ── hasTestFile scoring bias (v3) ──

    [Fact]
    public void CalculateScore_HasTestFile_AppliesOnePointFiveMultiplier()
    {
        var without = TestableTargetsTool.CalculateScore(50, "high", 0, 0, 0, 0, 0, hasTestFile: false);
        var with = TestableTargetsTool.CalculateScore(50, "high", 0, 0, 0, 0, 0, hasTestFile: true);

        Assert.True(with > without);
        Assert.InRange(with / without, 1.49, 1.51);
    }

    [Fact]
    public void GetTestableTargets_HasTestFile_FieldPopulated()
    {
        SeedCoverageGaps(MakeGap("WithTests", 20));
        SeedTestInventory(new TestInventoryEntry
        {
            Class = "WithTests",
            TestFiles = ["tests/WithTestsTests.cs"],
            TestMethods = ["Test1"],
            TestCount = 1
        });

        var result = TestableTargetsTool.GetTestableTargets();
        var doc = JsonDocument.Parse(result);
        var target = doc.RootElement.GetProperty("targets")[0];

        Assert.True(target.GetProperty("hasTestFile").GetBoolean());
        Assert.Single(target.GetProperty("testFiles").EnumerateArray());
    }

    [Fact]
    public void GetTestableTargets_NoTestFile_HasTestFileFalse()
    {
        SeedCoverageGaps(MakeGap("NoTests", 20));

        var result = TestableTargetsTool.GetTestableTargets();
        var doc = JsonDocument.Parse(result);
        var target = doc.RootElement.GetProperty("targets")[0];

        Assert.False(target.GetProperty("hasTestFile").GetBoolean());
    }

    [Fact]
    public void GetTestableTargets_HasTestFile_RanksHigherThanWithout()
    {
        SeedCoverageGaps(
            MakeGap("WithTests", 20),
            MakeGap("NoTests", 20)
        );
        // Both classes have 0 existing tests — only the hasTestFile flag differs
        SeedTestInventory(new TestInventoryEntry
        {
            Class = "WithTests",
            TestFiles = ["tests/WithTestsTests.cs"],
            TestMethods = [],
            TestCount = 0
        });

        var result = TestableTargetsTool.GetTestableTargets();
        var doc = JsonDocument.Parse(result);
        var targets = doc.RootElement.GetProperty("targets");

        // WithTests should come first: same uncovered lines, but 1.5x test file bonus
        var first = targets[0].GetProperty("class").GetString();
        Assert.Equal("WithTests", first);
    }

    [Fact]
    public void GetTestableTargets_HasTestFile_ReasonShowsStar()
    {
        SeedCoverageGaps(MakeGap("WithTests", 20));
        SeedTestInventory(new TestInventoryEntry
        {
            Class = "WithTests",
            TestFiles = ["tests/WithTestsTests.cs"],
            TestMethods = ["Test1"],
            TestCount = 1
        });

        var result = TestableTargetsTool.GetTestableTargets();
        var doc = JsonDocument.Parse(result);
        var reason = doc.RootElement.GetProperty("targets")[0].GetProperty("reason").GetString()!;

        Assert.Contains("test file exists", reason);
    }

    // ── Item #2: Configurable ROI threshold + sessionROITrend ──

    [Fact]
    public void GetTestableTargets_LowScore_TriggersRoiWarning()
    {
        // Seed a class that will score very low (coupled assessment)
        SeedCoverageGaps(new CoverageGap
        {
            Class = "Tiny", Namespace = "App", File = "Tiny.cs",
            TotalLines = 10, CoveredLines = 8, UncoveredLines = 2,
            UncoveredMethods = [new UncoveredMethod { Name = "Run", StartLine = 1, EndLine = 5, UncoveredLines = 2 }]
        });
        SeedTypeRegistry(new TypeRecord { Name = "Tiny", Namespace = "App" });

        // Use a high threshold so warning always triggers
        var result = TestableTargetsTool.GetTestableTargets(roiThreshold: 999.0);
        var doc = JsonDocument.Parse(result);

        Assert.True(doc.RootElement.TryGetProperty("warning", out var warning));
        Assert.Contains("below threshold 999", warning.GetString());
    }

    [Fact]
    public void GetTestableTargets_CustomRoiThreshold_AppearsInFilters()
    {
        SeedCoverageGaps(new CoverageGap
        {
            Class = "Foo", Namespace = "App", File = "Foo.cs",
            TotalLines = 100, CoveredLines = 50, UncoveredLines = 50,
            UncoveredMethods = [new UncoveredMethod { Name = "Run", StartLine = 1, EndLine = 50, UncoveredLines = 50 }]
        });
        SeedTypeRegistry(new TypeRecord { Name = "Foo", Namespace = "App" });

        var result = TestableTargetsTool.GetTestableTargets(roiThreshold: 7.5);
        var doc = JsonDocument.Parse(result);

        var filters = doc.RootElement.GetProperty("filters");
        Assert.Equal(7.5, filters.GetProperty("roiThreshold").GetDouble());
    }

    [Fact]
    public void BuildSessionROITrend_NoSessions_ReturnsNull()
    {
        var result = TestableTargetsTool.BuildSessionROITrend([], 50.0);
        Assert.Null(result);
    }

    [Fact]
    public void BuildSessionROITrend_WithSessions_ReturnsFields()
    {
        var sessions = new List<SessionRecord>
        {
            new()
            {
                SessionId = "s1", Model = "m", CoverageDelta = 2.5,
                TestsGenerated = 10, CoveredLines = 30,
                ClassesAttempted = ["A"], ClassesSucceeded = ["A"],
                ClassesFailed = [],
                StartedUtc = "2025-01-01T00:00:00Z", EndedUtc = "2025-01-01T01:00:00Z"
            }
        };

        var result = TestableTargetsTool.BuildSessionROITrend(sessions, 42.0);
        Assert.NotNull(result);

        var json = System.Text.Json.JsonSerializer.Serialize(result);
        var doc = JsonDocument.Parse(json);

        Assert.Equal(42.0, doc.RootElement.GetProperty("currentTopScore").GetDouble());
        Assert.Equal(1, doc.RootElement.GetProperty("sessionCount").GetInt32());
        Assert.Equal(2.5, doc.RootElement.GetProperty("lastSession").GetProperty("coverageDelta").GetDouble());
    }

    [Fact]
    public void GetTestableTargets_WithSessionData_IncludesSessionROITrend()
    {
        SeedCoverageGaps(new CoverageGap
        {
            Class = "BigClass", Namespace = "App", File = "BigClass.cs",
            TotalLines = 200, CoveredLines = 50, UncoveredLines = 150,
            UncoveredMethods = [new UncoveredMethod { Name = "Run", StartLine = 1, EndLine = 150, UncoveredLines = 150 }]
        });
        SeedTypeRegistry(new TypeRecord
        {
            Name = "BigClass", Namespace = "App",
            Constructors = [new ConstructorRecord { Params = [] }]
        });
        SeedSessions(new SessionRecord
        {
            SessionId = "s1", Model = "claude", CoverageDelta = 5.0,
            TestsGenerated = 10, CoveredLines = 25,
            ClassesAttempted = ["X"], ClassesSucceeded = ["X"], ClassesFailed = [],
            StartedUtc = "2025-01-01T00:00:00Z", EndedUtc = "2025-01-01T01:00:00Z"
        });

        var result = TestableTargetsTool.GetTestableTargets();
        var doc = JsonDocument.Parse(result);

        Assert.True(doc.RootElement.TryGetProperty("sessionROITrend", out var trend));
        Assert.Equal(1, trend.GetProperty("sessionCount").GetInt32());
    }
}
