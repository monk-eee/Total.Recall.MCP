using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using Total.Recall.Infrastructure;
using Total.Recall.Models;

namespace Total.Recall.Tools;

/// <summary>
/// Identifies zero-or-near-zero coverage classes that are trivially testable:
/// POCOs, stubs, static helpers, small logic classes with no/simple constructors.
/// These targets were consistently the highest ROI across coverage sessions because
/// they require no mocking complexity — pure new-and-assert patterns.
/// </summary>
[McpServerToolType]
public static class StubClassesTool
{
    [McpServerTool, Description(
        "Find zero-or-near-zero coverage classes that are trivially testable. " +
        "Identifies POCOs, stubs, static helpers, and simple logic classes with no mocking complexity. " +
        "These are the highest-ROI targets when class-level scores are low (all complex classes exhausted). " +
        "Filters by coverage threshold, constructor complexity, and excludes coupled/skip-assessed classes. " +
        "Use when get_testable_targets scores are all <5 — stub classes are the next tier of easy wins.")]
    public static string GetStubClasses(
        [Description("Max results (default: 20)")] int top = 20,
        [Description("Max coverage percent to include (default: 5.0). 0 = only completely uncovered classes.")] double maxCoveragePercent = 5.0,
        [Description("Max constructor params to include (default: 2). Stubs should have trivial constructors.")] int maxCtorParams = 2,
        [Description("Include classes with existing tests (default: false). When false, shows only untested classes.")] bool includeWithTests = false,
        [Description("Optional: namespace/session to query (default: server default)")] string? ns = null)
    {
        return Telemetry.Track("get_stub_classes", ns, new { top, maxCoveragePercent, maxCtorParams, includeWithTests, ns }, () =>
        {
        Metrics.Increment(Metrics.ToolGetStubClasses);
        Log.Debug($"[GetStubClasses] top={top} maxCov={maxCoveragePercent} maxCtor={maxCtorParams} includeWithTests={includeWithTests} ns='{ns ?? "(default)"}'");
        try
        {
            return GetStubClassesCore(top, maxCoveragePercent, maxCtorParams, includeWithTests, ns);
        }
        catch (Exception ex)
        {
            Log.Error($"[GetStubClasses] failed: {ex.GetType().Name}: {ex.Message}");
            return $"ERROR in GetStubClasses: {ex.GetType().Name}: {ex.Message}";
        }
        });
    }

    internal static string GetStubClassesCore(
        int top, double maxCoveragePercent, int maxCtorParams, bool includeWithTests, string? ns)
    {
        var stores = StoreRegistry.ForNamespace(ns);

        if (!stores.CoverageGaps.HasData())
            return "No coverage data found. Run 'total-recall scan --coverage <cobertura.xml>' first.";

        var gaps = stores.CoverageGaps.LoadAll();
        var (exactIndex, ciIndex) = stores.GetTypeIndex();

        // Build lookup tables
        var testInventory = stores.TestInventory.HasData()
            ? stores.TestInventory.LoadAll()
                .GroupBy(t => t.Class, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, TestInventoryEntry>(StringComparer.OrdinalIgnoreCase);

        var assessments = stores.Assessments.HasData()
            ? AssessmentLookup.BuildLatest(stores.Assessments.LoadAll())
            : new Dictionary<string, Assessment>(StringComparer.OrdinalIgnoreCase);

        var targets = new List<StubClassTarget>();

        foreach (var gap in gaps)
        {
            if (gap.UncoveredLineCount == 0)
                continue;

            // Only include classes at or below the coverage threshold
            if (gap.CoveragePercent > maxCoveragePercent)
                continue;

            // Skip very low-testability classes (formerly the SkipReason marker).
            if (gap.TestabilityScore is < 0.3)
                continue;

            var className = gap.ShortName;
            var bareName = TestableTargetsTool.NormalizeName(className);

            // Skip assessed-skip/coupled/deferred classes
            var assessment = AssessmentLookup.TryGet(assessments, className, bareName);
            if (assessment?.Verdict is "skip" or "coupled" or "deferred")
                continue;

            // Look up type record for constructor info
            TypeRecord? typeRecord = null;
            if (!exactIndex.TryGetValue(className, out typeRecord)
                && !ciIndex.TryGetValue(className, out typeRecord)
                && bareName != className)
            {
                if (!exactIndex.TryGetValue(bareName, out typeRecord))
                    ciIndex.TryGetValue(bareName, out typeRecord);
            }

            // Skip interfaces, enums, abstract classes — they aren't "stubs" to test directly
            if (typeRecord is { IsInterface: true } or { IsEnum: true } or { IsAbstract: true })
                continue;

            // Constructor complexity check
            int minCtorParams = GetMinCtorParams(typeRecord);
            if (minCtorParams > maxCtorParams)
                continue;

            bool allParamsMockable = AreAllParamsMockable(typeRecord);

            // Find test inventory entry
            TestInventoryEntry? testEntry = null;
            if (!testInventory.TryGetValue(className, out testEntry)
                && bareName != className)
            {
                testInventory.TryGetValue(bareName, out testEntry);
            }
            testEntry ??= TestableTargetsTool.TryFuzzyTestMatch(testInventory, className, out var fuzzyEntry)
                ? fuzzyEntry
                : null;

            var hasTestFile = testEntry?.TestFiles is { Count: > 0 };
            var testFiles = testEntry?.TestFiles ?? [];
            var existingTestCount = testEntry?.TestCount ?? gap.ExistingTests ?? 0;

            // Filter by test existence
            if (!includeWithTests && existingTestCount > 0)
                continue;

            // Categorize methods
            int realMethods = 0;
            int boilerplateMethods = 0;
            foreach (var method in gap.UncoveredMethods)
            {
                if (TestableTargetsTool.IsBoilerplateMethod(method.Name))
                    boilerplateMethods++;
                else
                    realMethods++;
            }

            var category = ClassifyStub(typeRecord, gap, realMethods, boilerplateMethods);

            var score = CalculateStubScore(gap.UncoveredLineCount, minCtorParams, allParamsMockable,
                hasTestFile, existingTestCount, realMethods, gap.LinesTotal);
            var reason = BuildStubReason(gap.UncoveredLineCount, minCtorParams, allParamsMockable,
                hasTestFile, existingTestCount, category, realMethods, gap.LinesTotal);

            targets.Add(new StubClassTarget
            {
                Class = gap.ShortName,
                Namespace = gap.NamespacePart,
                File = gap.FilePath,
                TotalLines = gap.LinesTotal,
                UncoveredLines = gap.UncoveredLineCount,
                CoveragePercent = Math.Round(gap.CoveragePercent, 1),
                RealMethodCount = realMethods,
                BoilerplateMethodCount = boilerplateMethods,
                MinCtorParams = minCtorParams,
                AllParamsMockable = allParamsMockable,
                HasTestFile = hasTestFile,
                TestFiles = testFiles,
                ExistingTestCount = existingTestCount,
                Category = category,
                Score = Math.Round(score, 1),
                Reason = reason,
            });
        }

        var results = targets
            .OrderByDescending(t => t.Score)
            .Take(top)
            .ToList();

        if (results.Count == 0)
            return "No stub classes found matching the criteria. Try raising maxCoveragePercent or maxCtorParams.";

        // Summary stats
        var categories = results.GroupBy(r => r.Category)
            .ToDictionary(g => g.Key, g => g.Count());
        var summary = new
        {
            count = results.Count,
            filters = new { top, maxCoveragePercent, maxCtorParams, includeWithTests },
            stats = new
            {
                totalUncoveredLines = results.Sum(r => r.UncoveredLines),
                avgUncoveredLines = Math.Round(results.Average(r => (double)r.UncoveredLines), 1),
                parameterlessCtors = results.Count(r => r.MinCtorParams == 0),
                allMockable = results.Count(r => r.AllParamsMockable),
                withTestFiles = results.Count(r => r.HasTestFile),
                categories
            },
            classes = results
        };

        return JsonSerializer.Serialize(summary, SharedJsonOptions.CamelCaseIndented);
    }

    /// <summary>
    /// Stub score: prioritizes uncovered lines, penalizes constructor complexity.
    /// Parameterless ctor → full score. Each ctor param → mild penalty.
    /// Static classes (minCtorParams = -1 sentinel from no constructors) treated as parameterless.
    /// </summary>
    internal static double CalculateStubScore(
        int uncoveredLines, int minCtorParams, bool allParamsMockable,
        bool hasTestFile, int existingTestCount, int realMethodCount, int totalLines)
    {
        // Log-scaled base (consistent with other scoring)
        double score = 10.0 * Math.Log2(1 + uncoveredLines);

        // Constructor simplicity bonus: parameterless = 1.0x, 1 param = 0.8x, 2 = 0.65x
        if (minCtorParams == 0)
            score *= 1.0;
        else if (allParamsMockable)
            score *= Math.Pow(0.8, minCtorParams);
        else
            score *= Math.Pow(0.6, minCtorParams);

        // Test file existence: extending is cheaper
        if (hasTestFile)
            score *= 1.5;

        // Real methods bonus: more testable methods = more coverage per setup cost
        if (realMethodCount >= 3)
            score *= 1.3;
        else if (realMethodCount >= 1)
            score *= 1.1;

        // Diminishing returns for existing tests
        if (existingTestCount > 0)
            score /= (1 + existingTestCount);

        // Class size sweet spot: small stubs are quick wins
        if (totalLines <= 50)
            score *= 1.2;
        else if (totalLines <= 100)
            score *= 1.1;
        else if (totalLines <= 200)
            score *= 1.0;
        else
            score *= 0.8; // Large "stubs" aren't really stubs

        return score;
    }

    /// <summary>
    /// Classify what kind of stub/simple class this is for agent context.
    /// </summary>
    internal static string ClassifyStub(TypeRecord? typeRecord, CoverageGap gap, int realMethods, int boilerplateMethods)
    {
        // Static class with methods = static helpers
        if (typeRecord is { IsStatic: true })
            return "static-helpers";

        // All methods are boilerplate (get/set/ctor) = pure POCO
        if (realMethods == 0 && boilerplateMethods > 0)
            return "poco";

        // Has real methods but simple constructor
        if (realMethods >= 1 && realMethods <= 5)
            return "simple-logic";

        if (realMethods > 5)
            return "logic-heavy";

        // Fallback: uncovered lines exist but no methods cataloged (edge case)
        return "unclassified";
    }

    internal static string BuildStubReason(
        int uncoveredLines, int minCtorParams, bool allParamsMockable,
        bool hasTestFile, int existingTestCount, string category, int realMethods, int totalLines)
    {
        var parts = new List<string>
        {
            $"{uncoveredLines} uncovered lines",
            $"category: {category}"
        };

        if (minCtorParams == 0)
            parts.Add("parameterless ctor");
        else if (allParamsMockable)
            parts.Add($"{minCtorParams} param(s) — all mockable");
        else
            parts.Add($"{minCtorParams} param(s) — has concrete deps");

        if (hasTestFile)
            parts.Add("★ test file exists (extend)");

        if (realMethods > 0)
            parts.Add($"{realMethods} real method{(realMethods != 1 ? "s" : "")}");

        if (totalLines <= 50)
            parts.Add("small class (quick win)");

        if (existingTestCount > 0)
            parts.Add($"{existingTestCount} existing tests");

        return string.Join(", ", parts);
    }

    /// <summary>
    /// Get minimum constructor param count. Returns 0 for parameterless ctors,
    /// or for classes with no type record. For static classes, returns 0.
    /// </summary>
    internal static int GetMinCtorParams(TypeRecord? typeRecord)
    {
        if (typeRecord is null)
            return 0; // Unknown — assume simple

        if (typeRecord.IsStatic)
            return 0;

        if (typeRecord.Constructors.Count == 0)
            return 0; // Implicit parameterless ctor

        return typeRecord.Constructors.Min(c => c.Params.Count);
    }

    /// <summary>
    /// Check if all params in the simplest constructor are interfaces (mockable).
    /// Returns true for parameterless ctors and unknown types.
    /// </summary>
    internal static bool AreAllParamsMockable(TypeRecord? typeRecord)
    {
        if (typeRecord is null)
            return true; // Unknown — assume mockable

        if (typeRecord.IsStatic || typeRecord.Constructors.Count == 0)
            return true; // No params = trivially mockable

        var simplest = typeRecord.Constructors
            .OrderBy(c => c.Params.Count)
            .First();

        if (simplest.Params.Count == 0)
            return true;

        // Interface params start with "I" and have a second uppercase letter (IFoo, IService)
        return simplest.Params.All(p => ParamHelper.IsInterfaceLike(ParamHelper.ExtractTypeName(p)));
    }
}
