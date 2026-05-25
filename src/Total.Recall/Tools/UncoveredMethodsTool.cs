using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using Total.Recall.Infrastructure;
using Total.Recall.Models;

namespace Total.Recall.Tools;

/// <summary>
/// Method-level coverage targeting: flattens CoverageGap.UncoveredMethods into
/// per-method targets, cross-joined with test inventory to surface the highest-ROI
/// individual methods. Prioritizes methods in classes that already have test files
/// (extending is 2-4x cheaper than creating new test infrastructure).
/// </summary>
[McpServerToolType]
public static class UncoveredMethodsTool
{
    [McpServerTool, Description(
        "Get the top N uncovered methods ranked by ROI. " +
        "Flattens class-level coverage gaps into individual method targets. " +
        "Methods in classes with existing test files score 2x higher (extending tests is cheaper than creating). " +
        "Use when class-level targets are exhausted or when extending existing test files for maximum ROI. " +
        "Pair with generate_test_scaffold to create stubs for specific methods.")]
    public static string GetUncoveredMethods(
        [Description("Max results (default: 20)")] int top = 20,
        [Description("Minimum uncovered lines per method to include (default: 3). Filters trivial one-liners.")] int minUncoveredLines = 3,
        [Description("Only show methods in classes with existing test files (default: false). Set true for 'extend existing' strategy.")] bool onlyWithExistingTests = false,
        [Description("Exclude boilerplate methods: constructors, property accessors (default: true)")] bool excludeBoilerplate = true,
        [Description("Optional: namespace/session to query (default: server default)")] string? ns = null)
    {
        return Telemetry.Track("get_uncovered_methods", ns, new { top, minUncoveredLines, onlyWithExistingTests, excludeBoilerplate, ns }, () =>
        {
        Metrics.Increment(Metrics.ToolGetUncoveredMethods);
        Log.Debug($"[GetUncoveredMethods] top={top} minLines={minUncoveredLines} onlyExisting={onlyWithExistingTests} ns='{ns ?? "(default)"}'");
        try
        {
            return GetUncoveredMethodsCore(top, minUncoveredLines, onlyWithExistingTests, excludeBoilerplate, ns);
        }
        catch (Exception ex)
        {
            Log.Error($"[GetUncoveredMethods] failed: {ex.GetType().Name}: {ex.Message}");
            return $"ERROR in GetUncoveredMethods: {ex.GetType().Name}: {ex.Message}";
        }
        });
    }

    internal static string GetUncoveredMethodsCore(
        int top, int minUncoveredLines, bool onlyWithExistingTests, bool excludeBoilerplate, string? ns)
    {
        var stores = StoreRegistry.ForNamespace(ns);

        if (!stores.CoverageGaps.HasData())
            return "No coverage data found. Run 'total-recall scan --coverage <cobertura.xml>' first.";

        var gaps = stores.CoverageGaps.LoadAll();

        // Build test inventory lookup
        var testInventory = stores.TestInventory.HasData()
            ? stores.TestInventory.LoadAll()
                .GroupBy(t => t.Class, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, TestInventoryEntry>(StringComparer.OrdinalIgnoreCase);

        // Build assessment lookup (to skip coupled/skip classes)
        var assessments = stores.Assessments.HasData()
            ? BuildLatestAssessments(stores.Assessments.LoadAll())
            : new Dictionary<string, Assessment>(StringComparer.OrdinalIgnoreCase);

        var targets = new List<UncoveredMethodTarget>();

        foreach (var gap in gaps)
        {
            if (gap.UncoveredLines == 0)
                continue;

            // Skip explicitly untestable classes
            if (!string.IsNullOrEmpty(gap.SkipReason))
                continue;

            var className = gap.Class;
            var bareName = TestableTargetsTool.NormalizeName(className);

            // Skip assessed-skip/coupled classes
            if (assessments.TryGetValue(className, out var assessment)
                || (bareName != className && assessments.TryGetValue(bareName, out assessment)))
            {
                if (assessment?.Verdict is "skip" or "coupled")
                    continue;
            }

            // Find test inventory entry
            TestInventoryEntry? testEntry = null;
            if (!testInventory.TryGetValue(className, out testEntry)
                && bareName != className)
            {
                testInventory.TryGetValue(bareName, out testEntry);
            }

            // Fuzzy match fallback
            testEntry ??= TestableTargetsTool.TryFuzzyTestMatch(testInventory, className, out var fuzzyEntry)
                ? fuzzyEntry
                : null;

            var hasTestFile = testEntry?.TestFiles is { Count: > 0 };
            var testFiles = testEntry?.TestFiles ?? [];
            var existingTestCount = testEntry?.TestCount ?? gap.ExistingTestCount;

            if (onlyWithExistingTests && !hasTestFile)
                continue;

            // Flatten each uncovered method into its own target
            foreach (var method in gap.UncoveredMethods)
            {
                if (method.UncoveredLines < minUncoveredLines)
                    continue;

                if (excludeBoilerplate && TestableTargetsTool.IsBoilerplateMethod(method.Name))
                    continue;

                var score = CalculateMethodScore(method.UncoveredLines, hasTestFile, existingTestCount);
                var reason = BuildMethodReason(method.UncoveredLines, hasTestFile, existingTestCount, testFiles.Count);

                targets.Add(new UncoveredMethodTarget
                {
                    Class = gap.Class,
                    Namespace = gap.Namespace,
                    File = gap.File,
                    Method = method.Name,
                    UncoveredLines = method.UncoveredLines,
                    StartLine = method.StartLine,
                    EndLine = method.EndLine,
                    HasTestFile = hasTestFile,
                    TestFiles = testFiles,
                    ExistingTestCount = existingTestCount,
                    Score = Math.Round(score, 1),
                    Reason = reason
                });
            }
        }

        var results = targets
            .OrderByDescending(t => t.Score)
            .Take(top)
            .ToList();

        if (results.Count == 0)
            return "No uncovered methods found matching the criteria. Try lowering minUncoveredLines or setting onlyWithExistingTests=false.";

        // Summary stats
        var withTestFile = results.Count(r => r.HasTestFile);
        var summary = new
        {
            count = results.Count,
            filters = new { top, minUncoveredLines, onlyWithExistingTests, excludeBoilerplate },
            stats = new
            {
                methodsWithTestFile = withTestFile,
                methodsWithoutTestFile = results.Count - withTestFile,
                avgUncoveredLines = Math.Round(results.Average(r => r.UncoveredLines), 1),
                distinctClasses = results.Select(r => r.Class).Distinct(StringComparer.OrdinalIgnoreCase).Count()
            },
            methods = results
        };

        return JsonSerializer.Serialize(summary, SharedJsonOptions.CamelCaseIndented);
    }

    /// <summary>
    /// Method-level score: uncovered lines as base, with strong bias toward existing test files.
    /// hasTestFile = 2.0x multiplier (extending is cheap: mocks already wired, patterns established).
    /// No test file = 0.5x multiplier (must create infrastructure from scratch).
    /// Mild diminishing returns for heavily-tested classes (but less harsh than class-level).
    /// </summary>
    internal static double CalculateMethodScore(int uncoveredLines, bool hasTestFile, int existingTestCount)
    {
        // Log-scaled base (same philosophy as class-level scoring)
        double score = 10.0 * Math.Log2(1 + uncoveredLines);

        // Test file existence bias — the key v3 insight:
        // Extending an existing test file is 2-4x cheaper than creating new infrastructure.
        score *= hasTestFile ? 2.0 : 0.5;

        // Mild diminishing returns for existing tests — but gentler than class-level.
        // At method level, even heavily-tested classes can have individual uncovered methods worth testing.
        if (existingTestCount > 0)
            score /= (1 + existingTestCount * 0.05);

        return score;
    }

    internal static string BuildMethodReason(int uncoveredLines, bool hasTestFile, int existingTestCount, int testFileCount)
    {
        var parts = new List<string>
        {
            $"{uncoveredLines} uncovered lines"
        };

        if (hasTestFile)
            parts.Add($"★ test file exists ({testFileCount} file{(testFileCount != 1 ? "s" : "")})");
        else
            parts.Add("no test file (must create infrastructure)");

        if (existingTestCount > 0)
            parts.Add($"{existingTestCount} existing tests");

        return string.Join(", ", parts);
    }

    private static Dictionary<string, Assessment> BuildLatestAssessments(List<Assessment> all)
    {
        var latest = new Dictionary<string, Assessment>(StringComparer.OrdinalIgnoreCase);
        foreach (var a in all)
            latest[a.Class] = a;
        return latest;
    }
}
