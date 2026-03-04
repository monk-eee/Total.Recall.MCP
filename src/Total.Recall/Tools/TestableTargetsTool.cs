using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using Total.Recall.Infrastructure;
using Total.Recall.Models;

namespace Total.Recall.Tools;

/// <summary>
/// Cross-joins coverage gaps, type registry, test inventory, assessments, gotchas,
/// and mock recipes to produce a pre-filtered, pre-scored list of "here's your next
/// N classes to test" — the decision the agent used to make manually.
/// </summary>
[McpServerToolType]
public static class TestableTargetsTool
{
    [McpServerTool, Description(
        "Get the top N most testable classes ranked by a composite score. " +
        "Cross-joins coverage gaps with type registry, test inventory, assessments, gotchas, and mock recipes. " +
        "Pre-filters by constructor complexity, class size, abstract/static exclusion, and previous assessments. " +
        "Returns ready-to-act-on targets with DI complexity, mock coverage, and uncovered methods. " +
        "Use this FIRST when starting a coverage session — replaces manual target selection.")]
    public static string GetTestableTargets(
        [Description("Max results (default: 10)")] int top = 10,
        [Description("Max constructor params to include (default: 5). Lower = simpler DI.")] int maxCtorParams = 5,
        [Description("Max total lines in class to include (default: 500). Keeps targets manageable.")] int maxTotalLines = 500,
        [Description("Exclude abstract classes (default: true)")] bool excludeAbstract = true,
        [Description("Exclude classes with 'skip' or 'coupled' assessments (default: true)")] bool excludeAssessed = true,
        [Description("Only show classes with zero existing tests (default: false)")] bool requireZeroTests = false,
        [Description("ROI score threshold below which a warning is emitted (default: 5.0). Lower threshold = more aggressive targeting.")] double roiThreshold = 5.0,
        [Description("Optional: namespace/session to query (default: server default)")] string? ns = null)
    {
        Metrics.Increment(Metrics.ToolGetTestableTargets);
        Log.Debug($"[GetTestableTargets] top={top} maxCtor={maxCtorParams} maxLines={maxTotalLines} roiThreshold={roiThreshold} ns='{ns ?? "(default)"}'");
        try
        {
            return GetTestableTargetsCore(top, maxCtorParams, maxTotalLines, excludeAbstract, excludeAssessed, requireZeroTests, roiThreshold, ns);
        }
        catch (Exception ex)
        {
            Log.Error($"[GetTestableTargets] failed: {ex.GetType().Name}: {ex.Message}");
            return $"ERROR in GetTestableTargets: {ex.GetType().Name}: {ex.Message}";
        }
    }

    private static string GetTestableTargetsCore(
        int top, int maxCtorParams, int maxTotalLines,
        bool excludeAbstract, bool excludeAssessed, bool requireZeroTests, double roiThreshold, string? ns)
    {
        var stores = StoreRegistry.ForNamespace(ns);

        if (!stores.CoverageGaps.HasData())
            return "No coverage data found. Run 'total-recall scan --coverage <cobertura.xml>' first.";

        var gaps = stores.CoverageGaps.LoadAll();
        var (exactIndex, ciIndex) = stores.GetTypeIndex();

        // Build lookup tables for cross-referencing
        var testInventory = stores.TestInventory.HasData()
            ? stores.TestInventory.LoadAll()
                .GroupBy(t => t.Class, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, TestInventoryEntry>(StringComparer.OrdinalIgnoreCase);

        var assessments = stores.Assessments.HasData()
            ? BuildLatestAssessments(stores.Assessments.LoadAll())
            : new Dictionary<string, Assessment>(StringComparer.OrdinalIgnoreCase);

        var gotchaCounts = stores.Gotchas.HasData()
            ? stores.Gotchas.LoadAll()
                .GroupBy(g => g.Type, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        // Also build per-type gotcha category lists for interface gotcha propagation (P3)
        var gotchasByType = stores.Gotchas.HasData()
            ? stores.Gotchas.LoadAll()
                .GroupBy(g => g.Type, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Select(x => x.Category).ToList(), StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        var mockRecipes = stores.MockRecipes.HasData()
            ? stores.MockRecipes.LoadAll()
                .ToDictionary(m => m.Interface, m => m, StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, MockRecipe>(StringComparer.OrdinalIgnoreCase);

        // Build session history for cross-session learning
        var sessionSuccesses = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var sessionFailures = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (stores.Sessions.HasData())
        {
            foreach (var session in stores.Sessions.LoadAll())
            {
                foreach (var cls in session.ClassesSucceeded)
                    sessionSuccesses[cls] = sessionSuccesses.GetValueOrDefault(cls) + 1;
                foreach (var fail in session.ClassesFailed)
                    sessionFailures[fail.Class] = sessionFailures.GetValueOrDefault(fail.Class) + 1;
            }
        }

        // Pre-compute namespace coupling counts: how many skip/coupled classes per namespace (P0-C)
        var namespaceCoupledCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var assessment in assessments.Values)
        {
            if (assessment.Verdict is "skip" or "coupled" && !string.IsNullOrEmpty(assessment.Class))
            {
                // Find the namespace for this class from coverage gaps
                var matchingGap = gaps.FirstOrDefault(g =>
                    g.Class.Equals(assessment.Class, StringComparison.OrdinalIgnoreCase)
                    || NormalizeName(g.Class).Equals(assessment.Class, StringComparison.OrdinalIgnoreCase));
                if (matchingGap is not null && !string.IsNullOrEmpty(matchingGap.Namespace))
                {
                    namespaceCoupledCounts[matchingGap.Namespace] =
                        namespaceCoupledCounts.GetValueOrDefault(matchingGap.Namespace) + 1;
                }
            }
        }

        // Build set of known problematic dependencies from coupled/skip assessment dependency lists.
        // Enables transitive coupling detection: if ClassX is coupled because of DependencyY,
        // any other class depending on DependencyY gets penalized too.
        var knownBadDeps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var assessment in assessments.Values)
        {
            if (assessment.Verdict is "skip" or "coupled" && assessment.Dependencies is { Count: > 0 })
            {
                foreach (var dep in assessment.Dependencies)
                    knownBadDeps.Add(dep);
            }
        }

        var targets = new List<TestableTarget>();

        foreach (var gap in gaps)
        {
            if (gap.UncoveredLines == 0)
                continue;

            // Skip classes explicitly marked untestable in coverage data
            if (!string.IsNullOrEmpty(gap.SkipReason))
                continue;

            // ── Stub/empty-body detection ──
            // Skip classes with 0 uncovered methods — they have uncovered lines only from
            // auto-property boilerplate (get_*/set_*), generated code, or empty bodies.
            if (gap.UncoveredMethods.Count == 0)
                continue;

            // Skip classes where ALL uncovered methods are boilerplate:
            // property accessors (get_X, set_X), constructors (.ctor, .cctor).
            // These are POCOs/data classes, not worth testing directly.
            if (gap.UncoveredMethods.All(m => IsBoilerplateMethod(m.Name)))
                continue;

            // Resolve class name — handle nested classes (Parent/Nested → try both forms)
            var className = gap.Class;
            var bareName = NormalizeName(className);

            // Cross-reference with type registry using centralized resolver
            TypeRecord? typeRecord = null;
            if (exactIndex.TryGetValue(className, out var exact))
                typeRecord = exact;
            else if (ciIndex.TryGetValue(className, out var ci))
                typeRecord = ci;
            else if (bareName != className)
            {
                // Nested class: try bare name fallback
                if (exactIndex.TryGetValue(bareName, out var bareExact))
                    typeRecord = bareExact;
                else if (ciIndex.TryGetValue(bareName, out var bareCi))
                    typeRecord = bareCi;
            }

            // Apply filters
            if (excludeAbstract && typeRecord is { IsAbstract: true })
                continue;

            if (typeRecord is { IsStatic: true })
                continue; // static classes are rarely unit-testable in isolation

            if (gap.TotalLines > maxTotalLines)
                continue;

            // Determine constructor complexity
            var ctorParamCount = 0;
            var ctorParams = new List<string>();
            var mockableParams = 0;
            var recipeCoveredParams = 0;
            var concreteParamCount = 0;
            var coupledParamCount = 0;
            var concreteParamNames = new List<string>();
            var interfaceGotchaCount = 0;
            var unmockableInterfaceCount = 0;
            var externalDepCount = 0;

            if (typeRecord?.Constructors is { Count: > 0 })
            {
                // Pick the constructor with the most params (DI constructor)
                var mainCtor = typeRecord.Constructors.OrderByDescending(c => c.Params.Count).First();
                ctorParamCount = mainCtor.Params.Count;
                ctorParams = mainCtor.Params;

                foreach (var param in mainCtor.Params)
                {
                    var paramType = ParamHelper.ExtractTypeName(param);
                    if (ParamHelper.IsInterfaceLike(paramType))
                    {
                        mockableParams++;
                        if (mockRecipes.ContainsKey(paramType) || mockRecipes.ContainsKey("I" + paramType))
                            recipeCoveredParams++;

                        // Gotcha→interface propagation (P3):
                        // If this interface has mock-related gotchas, it's harder to mock
                        if (gotchasByType.TryGetValue(paramType, out var ifaceGotchas))
                        {
                            interfaceGotchaCount += ifaceGotchas.Count(c =>
                                c.Contains("mock", StringComparison.OrdinalIgnoreCase)
                                || c.Contains("CS0854", StringComparison.OrdinalIgnoreCase)
                                || c.Contains("self-referencing", StringComparison.OrdinalIgnoreCase)
                                || c.Contains("circular", StringComparison.OrdinalIgnoreCase));
                        }

                        // Assessment-based interface unmockability:
                        // If this interface is assessed as skip/coupled or is a known bad dependency
                        if (assessments.TryGetValue(paramType, out var ifaceAssessment)
                            && ifaceAssessment.Verdict is "skip" or "coupled")
                        {
                            unmockableInterfaceCount++;
                        }
                        else if (knownBadDeps.Contains(paramType))
                        {
                            unmockableInterfaceCount++;
                        }

                        // External dependency detection: interfaces wrapping external services
                        if (ParamHelper.IsExternalDependency(paramType))
                            externalDepCount++;
                    }
                    else
                    {
                        // Concrete (non-interface) parameter — harder to mock
                        concreteParamCount++;
                        concreteParamNames.Add(paramType);

                        // External dependency detection: concrete external service types
                        if (ParamHelper.IsExternalDependency(paramType))
                            externalDepCount++;

                        // Check if this concrete type is skip/coupled in assessments
                        if (assessments.TryGetValue(paramType, out var paramAssessment)
                            && paramAssessment.Verdict is "skip" or "coupled")
                        {
                            coupledParamCount++;
                        }
                        // Also check transitive dependency set
                        else if (knownBadDeps.Contains(paramType))
                        {
                            coupledParamCount++;
                        }
                    }
                }
            }

            if (ctorParamCount > maxCtorParams)
                continue;

            // Base type coupling: inheriting from a coupled/skip base class
            var baseTypeCoupled = false;
            if (typeRecord?.BaseType is { Length: > 0 } baseType
                && baseType is not "Object" and not "object")
            {
                if ((assessments.TryGetValue(baseType, out var baseAssessment)
                        && baseAssessment.Verdict is "skip" or "coupled")
                    || knownBadDeps.Contains(baseType))
                {
                    baseTypeCoupled = true;
                }
            }

            // Check existing tests — try exact, then bare nested name, then fuzzy
            var existingTestCount = gap.ExistingTestCount;
            var hasTestFile = false;
            var testFilesList = new List<string>();
            if (testInventory.TryGetValue(className, out var testEntry)
                || (bareName != className && testInventory.TryGetValue(bareName, out testEntry))
                || TryFuzzyTestMatch(testInventory, className, out testEntry))
            {
                existingTestCount = Math.Max(existingTestCount, testEntry.TestCount);
                hasTestFile = testEntry.TestFiles is { Count: > 0 };
                testFilesList = testEntry.TestFiles ?? [];
            }

            if (requireZeroTests && existingTestCount > 0)
                continue;

            // Check previous assessments — try exact, then bare nested name
            var assessmentMatch = TryGetAssessment(assessments, className, bareName);
            if (excludeAssessed && assessmentMatch is not null)
            {
                if (assessmentMatch.Verdict is "skip" or "coupled" or "deferred")
                    continue;
            }
            var selfVerdict = assessmentMatch?.Verdict;

            // Count gotchas — try both names
            var gotchaCount = 0;
            if (!gotchaCounts.TryGetValue(className, out gotchaCount) && bareName != className)
                gotchaCounts.TryGetValue(bareName, out gotchaCount);

            // Session learning: count past successes and failures — try both names
            var pastSuccesses = 0;
            var pastFailures = 0;
            if (!sessionSuccesses.TryGetValue(className, out pastSuccesses) && bareName != className)
                sessionSuccesses.TryGetValue(bareName, out pastSuccesses);
            if (!sessionFailures.TryGetValue(className, out pastFailures) && bareName != className)
                sessionFailures.TryGetValue(bareName, out pastFailures);

            // Calculate composite score
            // Filter out property accessors for "real" method count
            var realMethodCount = gap.UncoveredMethods.Count(m => !IsPropertyAccessor(m.Name));
            var nsCoupledCount = namespaceCoupledCounts.GetValueOrDefault(gap.Namespace);
            var score = CalculateScore(
                gap.UncoveredLines, gap.Testability, ctorParamCount,
                mockableParams, recipeCoveredParams, existingTestCount, gotchaCount,
                pastSuccesses, pastFailures, concreteParamCount, coupledParamCount,
                realMethodCount, gap.TotalLines, nsCoupledCount, interfaceGotchaCount,
                selfVerdict, unmockableInterfaceCount, baseTypeCoupled, externalDepCount,
                hasTestFile);

            var reason = BuildReason(
                gap.UncoveredLines, ctorParamCount, mockableParams, concreteParamCount, coupledParamCount,
                gap.UncoveredMethods.Count, gotchaCount, existingTestCount,
                pastSuccesses, pastFailures, concreteParamNames,
                gap.TotalLines, nsCoupledCount, interfaceGotchaCount,
                selfVerdict, unmockableInterfaceCount, baseTypeCoupled, typeRecord?.BaseType,
                externalDepCount, hasTestFile);

            targets.Add(new TestableTarget
            {
                Class = gap.Class,
                Namespace = gap.Namespace,
                File = gap.File,
                TotalLines = gap.TotalLines,
                UncoveredLines = gap.UncoveredLines,
                CoveragePercent = gap.CoveragePercent,
                UncoveredMethodCount = gap.UncoveredMethods.Count,
                UncoveredMethods = gap.UncoveredMethods.Select(m => m.Name).ToList(),
                ExistingTestCount = existingTestCount,
                HasTestFile = hasTestFile,
                TestFiles = testFilesList,
                CtorParamCount = ctorParamCount,
                CtorParams = ctorParams,
                MockableParamCount = mockableParams,
                RecipeCoveredParams = recipeCoveredParams,
                ConcreteParamCount = concreteParamCount,
                CoupledParamCount = coupledParamCount,
                ConcreteParamNames = concreteParamNames,
                BaseType = typeRecord?.BaseType,
                IsAbstract = typeRecord?.IsAbstract ?? false,
                IsStatic = typeRecord?.IsStatic ?? false,
                PreviousVerdict = assessmentMatch?.Verdict,
                PastSuccesses = pastSuccesses,
                PastFailures = pastFailures,
                GotchaCount = gotchaCount,
                Score = Math.Round(score, 1),
                Reason = reason
            });
        }

        var results = targets
            .OrderByDescending(t => t.Score)
            .Take(top)
            .ToList();

        if (results.Count == 0)
            return "No testable targets found matching the criteria. Try relaxing filters (increase maxCtorParams or maxTotalLines, set excludeAssessed=false).";

        // ── ROI threshold warning ──
        // When the best available target scores below the threshold, the remaining targets are
        // likely coupled, heavily-tested, or otherwise low-ROI. Signal the agent to
        // shift strategy rather than grinding through diminishing returns.
        string? roiWarning = null;
        var topScore = results[0].Score;
        if (topScore < roiThreshold)
        {
            var strategies = new List<string>();
            if (topScore < 1.0)
                strategies.Add("Class-level targets are effectively exhausted.");
            else
                strategies.Add($"Top score is only {topScore:F1} (below threshold {roiThreshold:F1}) — remaining targets have low ROI.");

            strategies.Add("Recommended next steps:");
            strategies.Add("  1. Switch to method-level targeting: get_uncovered_methods(onlyWithExistingTests=true)");
            strategies.Add("  2. Try stub/simple classes: get_stub_classes()");
            strategies.Add("  3. Extend existing test files for partially-covered classes");
            strategies.Add("  4. Consider integration tests for tightly-coupled code");
            roiWarning = string.Join("\n", strategies);
        }

        // ── Session ROI trend ──
        // Compare current top score against context from the last logged session.
        // Helps agents understand whether targeting is improving or declining.
        object? sessionROITrend = null;
        if (stores.Sessions.HasData())
        {
            sessionROITrend = BuildSessionROITrend(stores.Sessions.LoadAll(), topScore);
        }

        var summary = new
        {
            count = results.Count,
            filters = new { top, maxCtorParams, maxTotalLines, excludeAbstract, excludeAssessed, requireZeroTests, roiThreshold },
            warning = roiWarning,
            sessionROITrend,
            targets = results
        };

        return JsonSerializer.Serialize(summary, SharedJsonOptions.CamelCaseIndented);
    }

    /// <summary>
    /// Build a session ROI trend object comparing the current top score against recent session history.
    /// Returns null if insufficient session data.
    /// </summary>
    internal static object? BuildSessionROITrend(List<SessionRecord> sessions, double currentTopScore)
    {
        if (sessions.Count == 0)
            return null;

        var lastSession = sessions[^1];
        var sessionsWithCoverage = sessions
            .Where(s => s.CoveredLines > 0 && s.TestsGenerated > 0)
            .ToList();

        // Compute trend fields
        var lastCoverageDelta = lastSession.CoverageDelta;
        var lastTestsGenerated = lastSession.TestsGenerated;
        var lastLinesPerTest = lastSession.TestsGenerated > 0
            ? Math.Round((double)lastSession.CoveredLines / lastSession.TestsGenerated, 2)
            : 0.0;

        string trend;
        if (sessionsWithCoverage.Count < 2)
        {
            trend = "insufficient data";
        }
        else
        {
            var recentAvgLpt = sessionsWithCoverage.TakeLast(3)
                .Average(s => (double)s.CoveredLines / s.TestsGenerated);
            var olderAvgLpt = sessionsWithCoverage.Count > 3
                ? sessionsWithCoverage.SkipLast(3).TakeLast(3)
                    .Average(s => (double)s.CoveredLines / s.TestsGenerated)
                : sessionsWithCoverage.First().CoveredLines > 0
                    ? (double)sessionsWithCoverage.First().CoveredLines / sessionsWithCoverage.First().TestsGenerated
                    : recentAvgLpt;

            if (recentAvgLpt >= olderAvgLpt * 0.9)
                trend = "stable";
            else if (recentAvgLpt >= olderAvgLpt * 0.5)
                trend = "declining";
            else
                trend = "steep decline — consider strategy shift";
        }

        return new
        {
            currentTopScore = Math.Round(currentTopScore, 1),
            lastSession = new
            {
                coverageDelta = lastCoverageDelta,
                testsGenerated = lastTestsGenerated,
                linesPerTest = lastLinesPerTest
            },
            trend,
            sessionCount = sessions.Count
        };
    }

    /// <summary>
    /// Composite score: higher = better ROI for writing tests.
    /// Factors: log-scaled uncovered lines (prevents coupled-but-large classes from dominating),
    /// mockability of ctor params (not just count),
    /// concrete-dependency penalty (harsher: 0.3x per concrete param),
    /// coupled-dependency penalty (near-fatal: 0.15x per coupled param),
    /// external-service dependency penalty (0.5x per File/Http/Db/Stream param),
    /// recipe coverage, session history, executable-line weighting,
    /// medium-complexity class boost, namespace cluster coupling penalty,
    /// interface gotcha propagation, unmockable interface penalty (0.2x per),
    /// base type coupling (0.15x), self-assessment penalty (0.1x),
    /// and transitive dependency detection via assessment Dependencies fields.
    /// </summary>
    internal static double CalculateScore(
        int uncoveredLines, string? testability, int ctorParamCount,
        int mockableParams, int recipeCoveredParams, int existingTestCount, int gotchaCount,
        int pastSuccesses = 0, int pastFailures = 0,
        int concreteParamCount = 0, int coupledParamCount = 0,
        int realMethodCount = 0, int totalLines = 0,
        int namespaceCoupledCount = 0, int interfaceGotchaCount = 0,
        string? selfVerdict = null, int unmockableInterfaceCount = 0,
        bool baseTypeCoupled = false, int externalDepCount = 0,
        bool hasTestFile = false)
    {
        // Base: log-scaled uncovered lines.
        // Prevents coupled-but-large classes (200 lines) from dominating pure-but-small ones (30 lines).
        // 20→43, 50→57, 100→67, 200→77. Compresses range so testability factors dominate.
        double score = 10.0 * Math.Log2(1 + uncoveredLines);

        // Testability multiplier
        score *= (testability?.ToLowerInvariant()) switch
        {
            "high" => 1.0,
            "medium" => 0.7,
            "low" => 0.3,
            _ => 0.5
        };

        // ── Medium-complexity class boost (P0-B) ──
        // Classes in the "sweet spot" (100-400 lines) yield more coverage per test.
        // Small classes (<50 lines) are likely POCOs or trivial.
        // totalLines=0 is unknown → neutral (1.0x).
        if (totalLines > 0)
        {
            score *= totalLines switch
            {
                < 50 => 0.7,      // too small — limited ROI
                < 100 => 0.9,     // small — moderate payoff
                <= 400 => 1.3,    // sweet spot — complex logic, manageable size
                <= 800 => 1.0,    // large — neutral
                _ => 0.8          // very large — high coupling risk
            };
        }

        // ── Mockability-aware constructor scoring ──
        // Instead of penalizing by total param count, score by HOW mockable the params are.
        // All-interface ctors are easy regardless of count. One concrete coupled dep is fatal.
        if (ctorParamCount == 0)
        {
            score *= 1.2; // parameterless = easiest
        }
        else
        {
            // Base: interface-heavy ctors get a bonus, concrete-heavy get penalized
            var mockableRatio = (double)mockableParams / ctorParamCount;
            // Range: 0.5 (all concrete) to 1.1 (all interfaces)
            score *= 0.5 + (0.6 * mockableRatio);

            // Concrete dependency penalty: each concrete param harshly reduces score (P0-A)
            // A class with 1 concrete dep is likely untestable without significant refactoring
            if (concreteParamCount > 0)
                score *= Math.Pow(0.3, concreteParamCount);

            // Coupled/skip-listed concrete params: near-fatal penalty (P0-A)
            // These are known-untestable deps (e.g. LinterExtension — 2588 lines, no interface)
            if (coupledParamCount > 0)
                score *= Math.Pow(0.15, coupledParamCount);
        }

        // ── Interface gotcha penalty (P3) ──
        // If constructor interface params have mock-related gotchas, mockability is degraded
        if (interfaceGotchaCount > 0)
            score *= Math.Pow(0.7, interfaceGotchaCount);

        // ── Unmockable interface penalty ──
        // Interface params assessed as coupled/skip, or in known-bad dependency lists.
        // Stronger than gotcha penalty: assessments are confirmed blockers, not just warnings.
        if (unmockableInterfaceCount > 0)
            score *= Math.Pow(0.2, unmockableInterfaceCount);

        // ── Base type coupling penalty ──
        // Inheriting from a coupled/skip base class propagates the coupling problem.
        if (baseTypeCoupled)
            score *= 0.15;

        // ── External service dependency penalty ──
        // Params that smell like file system, HTTP, database, or stream access
        // are structural blockers that can't be solved by mocking alone.
        if (externalDepCount > 0)
            score *= Math.Pow(0.5, externalDepCount);

        // ── Test file existence bias (v3) ──
        // Extending an existing test file is significantly cheaper than creating new infrastructure.
        // Mocks are already wired, patterns established, using statements present.
        if (hasTestFile)
            score *= 1.5;

        // Mock coverage (params that have recipes = less work)
        if (mockableParams > 0)
        {
            var coverage = (double)recipeCoveredParams / mockableParams;
            score *= 0.7 + (0.3 * coverage); // range: 0.7 (no recipes) to 1.0 (all covered)
        }

        // Existing tests penalty (diminishing returns)
        // At 15+ existing tests, the class is well-tested — remaining uncovered lines
        // are likely coupled/untestable. Steep cliff prevents agents wasting tokens.
        score /= (1 + existingTestCount);
        if (existingTestCount >= 15)
            score *= 0.3;

        // Gotcha risk discount (more gotchas = more likely to hit problems)
        score /= (1 + gotchaCount * 0.1);

        // Session learning: penalize classes that failed in past sessions
        // Each failure reduces score by 30% (compounding) — avoids repeatedly hitting walls
        if (pastFailures > 0)
            score *= Math.Pow(0.7, pastFailures);

        // Session learning: deprioritize classes already successfully tested
        // They likely already gained coverage — focus on untouched classes
        if (pastSuccesses > 0)
            score *= Math.Pow(0.5, pastSuccesses);

        // ── Namespace cluster coupling penalty (P0-C) ──
        // If 3+ classes in this namespace are assessed as skip/coupled,
        // the namespace is a "bad neighborhood" — higher coupling risk
        if (namespaceCoupledCount >= 3)
            score *= 0.85;

        // ── Self-assessment penalty ──
        // When excludeAssessed=false, assessed-coupled/skip/deferred classes still appear
        // but ranked very low. Prevents re-attempting known-untestable classes.
        if (selfVerdict is "skip" or "coupled" or "deferred")
            score *= 0.1;

        // ── Executable-line weighting ──
        // Classes with more real (non-accessor) methods represent more testable surface.
        // A 200-line class with 10 methods is more valuable than a 200-line POCO.
        // Mild boost: log-scaled so 1 method = 1.0x, 5 methods = ~1.14x, 10 methods = ~1.23x
        if (realMethodCount > 1)
            score *= 1.0 + (0.1 * Math.Log2(realMethodCount));

        return score;
    }

    private static string BuildReason(
        int uncoveredLines, int ctorParamCount, int mockableParams,
        int concreteParamCount, int coupledParamCount,
        int uncoveredMethodCount, int gotchaCount, int existingTestCount,
        int pastSuccesses = 0, int pastFailures = 0,
        List<string>? concreteParamNames = null,
        int totalLines = 0, int namespaceCoupledCount = 0, int interfaceGotchaCount = 0,
        string? selfVerdict = null, int unmockableInterfaceCount = 0,
        bool baseTypeCoupled = false, string? baseTypeName = null,
        int externalDepCount = 0, bool hasTestFile = false)
    {
        var parts = new List<string>
        {
            $"{uncoveredLines} uncovered lines"
        };

        // Complexity tier
        if (totalLines > 0)
        {
            var tier = totalLines switch
            {
                < 50 => "small",
                < 100 => "moderate",
                <= 400 => "★ sweet-spot",
                <= 800 => "large",
                _ => "very large"
            };
            parts.Add($"{totalLines} total lines ({tier})");
        }

        if (ctorParamCount == 0)
        {
            parts.Add("parameterless ctor");
        }
        else if (concreteParamCount == 0)
        {
            parts.Add($"all-interface ctor ({ctorParamCount} params, all mockable)");
        }
        else
        {
            var ctorDesc = $"ctor ({ctorParamCount} params: {mockableParams} interfaces, {concreteParamCount} concrete";
            if (coupledParamCount > 0)
                ctorDesc += $", {coupledParamCount} coupled/skip";
            ctorDesc += ")";
            parts.Add(ctorDesc);

            if (concreteParamNames is { Count: > 0 })
                parts.Add($"⚠ concrete deps: {string.Join(", ", concreteParamNames)}");
        }

        parts.Add($"{uncoveredMethodCount} untested methods");

        if (gotchaCount > 0)
            parts.Add($"{gotchaCount} known gotchas");

        if (existingTestCount >= 15)
            parts.Add($"⚠ {existingTestCount} existing tests (heavily tested — diminishing ROI)");
        else if (existingTestCount > 0)
            parts.Add($"{existingTestCount} existing tests");

        if (pastSuccesses > 0)
            parts.Add($"✓ succeeded in {pastSuccesses} past session(s)");

        if (pastFailures > 0)
            parts.Add($"✗ failed in {pastFailures} past session(s)");

        if (namespaceCoupledCount >= 3)
            parts.Add($"⚠ namespace has {namespaceCoupledCount} coupled/skip classes");

        if (interfaceGotchaCount > 0)
            parts.Add($"⚠ {interfaceGotchaCount} interface mock gotcha(s)");

        if (unmockableInterfaceCount > 0)
            parts.Add($"⚠ {unmockableInterfaceCount} unmockable interface dep(s)");

        if (baseTypeCoupled)
            parts.Add($"⚠ coupled base type{(string.IsNullOrEmpty(baseTypeName) || baseTypeName is "Object" or "object" ? "" : $": {baseTypeName}")}");

        if (externalDepCount > 0)
            parts.Add($"⚠ {externalDepCount} external service dep(s)");

        if (hasTestFile)
            parts.Add("★ test file exists (extend)");

        if (selfVerdict is "skip" or "coupled" or "deferred")
            parts.Add($"⚠ assessed as '{selfVerdict}'");

        return string.Join(", ", parts);
    }

    private static Dictionary<string, Assessment> BuildLatestAssessments(List<Assessment> all)
    {
        var latest = new Dictionary<string, Assessment>(StringComparer.OrdinalIgnoreCase);
        foreach (var a in all)
            latest[a.Class] = a; // last wins
        return latest;
    }

    /// <summary>
    /// Normalize nested class names: "Parent/Nested" → "Nested".
    /// Also handles dot-separated nested names: "Parent.Nested" → "Nested".
    /// If no nesting separator exists, returns the original name.
    /// </summary>
    internal static string NormalizeName(string className)
    {
        // Cobertura uses / for nested classes
        var slashIdx = className.LastIndexOf('/');
        if (slashIdx >= 0 && slashIdx < className.Length - 1)
            return className[(slashIdx + 1)..];

        return className;
    }

    /// <summary>
    /// Detect property accessor methods: get_X, set_X.
    /// These are auto-property backing methods in Cobertura output.
    /// </summary>
    internal static bool IsPropertyAccessor(string methodName)
    {
        return methodName.StartsWith("get_", StringComparison.Ordinal)
            || methodName.StartsWith("set_", StringComparison.Ordinal);
    }

    /// <summary>
    /// Detect boilerplate methods that don't represent testable logic:
    /// property accessors (get_X, set_X), constructors (.ctor, .cctor).
    /// Used to filter pure POCO/data classes where all uncovered methods are boilerplate.
    /// </summary>
    internal static bool IsBoilerplateMethod(string methodName)
    {
        return IsPropertyAccessor(methodName)
            || methodName is ".ctor" or ".cctor";
    }

    /// <summary>
    /// Try to find an assessment for a class, trying the full name first, then the bare nested name.
    /// </summary>
    private static Assessment? TryGetAssessment(Dictionary<string, Assessment> assessments, string className, string bareName)
    {
        if (assessments.TryGetValue(className, out var assessment))
            return assessment;
        if (bareName != className && assessments.TryGetValue(bareName, out assessment))
            return assessment;
        return null;
    }

    /// <summary>
    /// Fuzzy test inventory matching: when exact match fails, try:
    /// 1. Class name with "Base" suffix stripped (WriteOperationConfigurationBase → WriteOperationConfiguration)
    /// 2. Any test inventory key that starts with the gap class name
    /// 3. Any test inventory key where the gap class name starts with the key
    /// Returns the best match (highest test count).
    /// </summary>
    internal static bool TryFuzzyTestMatch(
        Dictionary<string, TestInventoryEntry> testInventory,
        string className, out TestInventoryEntry entry)
    {
        entry = null!;

        // 1. Strip common suffixes: Base, Impl, Default
        var stripped = className;
        foreach (var suffix in new[] { "Base", "Impl", "Default" })
        {
            if (stripped.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) && stripped.Length > suffix.Length)
            {
                var candidate = stripped[..^suffix.Length];
                if (testInventory.TryGetValue(candidate, out var found))
                {
                    entry = found;
                    return true;
                }
            }
        }

        // 2. Any inventory key that starts with or is a prefix of the class name
        TestInventoryEntry? bestMatch = null;
        foreach (var kvp in testInventory)
        {
            if (className.StartsWith(kvp.Key, StringComparison.OrdinalIgnoreCase)
                || kvp.Key.StartsWith(className, StringComparison.OrdinalIgnoreCase))
            {
                if (bestMatch is null || kvp.Value.TestCount > bestMatch.TestCount)
                    bestMatch = kvp.Value;
            }
        }

        if (bestMatch is not null)
        {
            entry = bestMatch;
            return true;
        }

        return false;
    }
}
