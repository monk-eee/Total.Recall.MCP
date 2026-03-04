using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using Total.Recall.Infrastructure;
using Total.Recall.Models;

namespace Total.Recall.Tools;

/// <summary>
/// Bidirectional session tracking: agents write session outcomes (tokens, tests, coverage delta)
/// back to MCP for cross-session learning and ROI measurement.
/// </summary>
[McpServerToolType]
public static class SessionTool
{
    [McpServerTool, Description(
        "Log a coverage session's outcomes for cross-session learning. " +
        "Records: model used, token usage, classes attempted/succeeded/failed, " +
        "tests generated, coverage delta, gotchas discovered. " +
        "Call this at the END of a coverage session to persist what happened. " +
        "Data feeds into future get_testable_targets scoring and aggregate analytics.")]
    public static string LogSession(
        [Description("LLM model used (e.g. 'claude-sonnet-4-20250514')")] string model,
        [Description("Approximate prompt tokens consumed")] long promptTokens = 0,
        [Description("Approximate completion tokens consumed")] long completionTokens = 0,
        [Description("Comma-separated CLASS NAMES attempted (e.g. 'ClassA, ClassB'). NOT a count — pass the actual names.")] string classesAttempted = "",
        [Description("Comma-separated CLASS NAMES that succeeded — compiled + passed (e.g. 'ClassA'). NOT a count.")] string classesSucceeded = "",
        [Description("Comma-separated 'ClassName:reason' for failed classes (e.g. 'ClassB:compile error')")] string classesFailed = "",
        [Description("Total number of test methods generated")] int testsGenerated = 0,
        [Description("Line coverage % before session (e.g. 25.66)")] double coverageBefore = 0,
        [Description("Line coverage % after session (e.g. 54.1)")] double coverageAfter = 0,
        [Description("Number of new gotchas discovered this session")] int gotchasDiscovered = 0,
        [Description("Number of new assessments recorded this session")] int assessmentsRecorded = 0,
        [Description("Actual lines of new code covered this session (for ROI tracking). Optional.")] int coveredLines = 0,
        [Description("Free-form session notes / learnings")] string? notes = null,
        [Description("Optional: namespace/session to write to (default: server default)")] string? ns = null)
    {
        Metrics.Increment(Metrics.ToolLogSession);
        Log.Debug($"[LogSession] model='{model}' classes={classesAttempted} ns='{ns ?? "(default)"}'");
        try
        {
            return LogSessionCore(model, promptTokens, completionTokens,
                classesAttempted, classesSucceeded, classesFailed,
                testsGenerated, coverageBefore, coverageAfter,
                gotchasDiscovered, assessmentsRecorded, coveredLines, notes, ns);
        }
        catch (Exception ex)
        {
            Log.Error($"[LogSession] failed: {ex.GetType().Name}: {ex.Message}");
            return $"ERROR in LogSession: {ex.GetType().Name}: {ex.Message}";
        }
    }

    private static string LogSessionCore(
        string model, long promptTokens, long completionTokens,
        string classesAttempted, string classesSucceeded, string classesFailed,
        int testsGenerated, double coverageBefore, double coverageAfter,
        int gotchasDiscovered, int assessmentsRecorded, int coveredLines,
        string? notes, string? ns)
    {
        // Detect common agent mistake: passing a count (e.g. "3") instead of class names
        var warnings = new List<string>();
        if (LooksLikeCount(classesAttempted))
            warnings.Add($"classesAttempted='{classesAttempted}' looks like a count, not class names. Pass comma-separated names (e.g. 'ClassA, ClassB').");
        if (LooksLikeCount(classesSucceeded))
            warnings.Add($"classesSucceeded='{classesSucceeded}' looks like a count, not class names. Pass comma-separated names.");

        var stores = StoreRegistry.ForNamespace(ns);

        // ── Auto-aggregation (P2) ──
        // When agent passes 0 for gotchas/assessments (common), auto-count from stores
        // by comparing against the last session's end timestamp.
        var autoGotchas = false;
        var autoAssessments = false;
        if (gotchasDiscovered == 0 && stores.Gotchas.HasData())
        {
            var lastSessionEnd = GetLastSessionEndTime(stores);
            var allGotchas = stores.Gotchas.LoadAll();
            gotchasDiscovered = CountRecordsSince(allGotchas, g => g.Date, lastSessionEnd);
            if (gotchasDiscovered > 0)
                autoGotchas = true;
        }
        if (assessmentsRecorded == 0 && stores.Assessments.HasData())
        {
            var lastSessionEnd = GetLastSessionEndTime(stores);
            var allAssessments = stores.Assessments.LoadAll();
            assessmentsRecorded = CountRecordsSince(allAssessments, a => a.Date, lastSessionEnd);
            if (assessmentsRecorded > 0)
                autoAssessments = true;
        }

        // ── testsGenerated auto-estimation (P3) ──
        // When agent passes 0 for testsGenerated but succeeded classes are known,
        // estimate from past session history (average tests per succeeded class).
        var autoTests = false;
        if (testsGenerated == 0)
        {
            var succeeded = ParseCsv(classesSucceeded);
            if (succeeded.Count > 0 && stores.Sessions.HasData())
            {
                var estimate = EstimateTestsGenerated(stores.Sessions.LoadAll(), succeeded.Count);
                if (estimate > 0)
                {
                    testsGenerated = estimate;
                    autoTests = true;
                }
            }
        }

        var sessionId = Guid.NewGuid().ToString();
        var now = DateTime.UtcNow;

        var record = new SessionRecord
        {
            SessionId = sessionId,
            StartedUtc = now.ToString("o"),
            EndedUtc = now.ToString("o"),
            Model = model,
            PromptTokens = promptTokens,
            CompletionTokens = completionTokens,
            TotalTokens = promptTokens + completionTokens,
            ClassesAttempted = ParseCsv(classesAttempted),
            ClassesSucceeded = ParseCsv(classesSucceeded),
            ClassesFailed = ParseFailures(classesFailed),
            TestsGenerated = testsGenerated,
            CoverageBefore = coverageBefore,
            CoverageAfter = coverageAfter,
            CoverageDelta = Math.Round(coverageAfter - coverageBefore, 2),
            CoveredLines = coveredLines,
            GotchasDiscovered = gotchasDiscovered,
            AssessmentsRecorded = assessmentsRecorded,
            Notes = notes ?? ""
        };

        // ── Coverage delta sanity check ──
        // Agents sometimes pass absolute coverage % (e.g. coverageBefore=54.1, coverageAfter=54.1)
        // or swap before/after. Flag suspicious deltas so the data isn't silently wrong.
        if (Math.Abs(record.CoverageDelta) >= 10.0)
        {
            warnings.Add($"Coverage delta {record.CoverageDelta:+#.##;-#.##;0}% is unusually large (|Δ| ≥ 10%). " +
                         $"Verify coverageBefore={coverageBefore} and coverageAfter={coverageAfter} are correct " +
                         "(should be line coverage percentages, e.g. 25.66 → 27.12).");
        }
        else if (record.CoverageDelta < 0 && record.ClassesSucceeded.Count > 0)
        {
            warnings.Add($"Coverage went DOWN by {record.CoverageDelta:F2}% despite {record.ClassesSucceeded.Count} succeeded class(es). " +
                         "This may indicate coverageBefore/coverageAfter values were swapped, or the coverage XML was stale.");
        }

        stores.Sessions.Append(record);

        var summary = $"Session logged: {sessionId}\n" +
                      $"  Model: {model}\n" +
                      $"  Tokens: {record.TotalTokens:N0} ({promptTokens:N0} prompt + {completionTokens:N0} completion)\n" +
                      $"  Classes: {record.ClassesAttempted.Count} attempted, {record.ClassesSucceeded.Count} succeeded, {record.ClassesFailed.Count} failed\n" +
                      $"  Tests: {testsGenerated}{(autoTests ? " (estimated from past session avg)" : "")}\n" +
                      $"  Coverage: {coverageBefore}% → {coverageAfter}% (Δ{record.CoverageDelta:+#.##;-#.##;0}%)\n" +
                      (coveredLines > 0 ? $"  Covered lines: {coveredLines} ({(testsGenerated > 0 ? $"{(double)coveredLines / testsGenerated:F1} lines/test" : "n/a")})\n" : "") +
                      $"  Gotchas: {gotchasDiscovered} new{(autoGotchas ? " (auto-counted from store)" : "")}, " +
                      $"Assessments: {assessmentsRecorded} new{(autoAssessments ? " (auto-counted from store)" : "")}";

        if (warnings.Count > 0)
            summary += "\n\n⚠ WARNINGS:\n  " + string.Join("\n  ", warnings);

        return summary;
    }

    /// <summary>
    /// Detect when an agent passes a numeric count instead of class names.
    /// "3" → true, "ClassA, ClassB" → false, "" → false.
    /// </summary>
    internal static bool LooksLikeCount(string value)
    {
        return !string.IsNullOrWhiteSpace(value) && long.TryParse(value.Trim(), out _);
    }

    [McpServerTool, Description(
        "Get session history and aggregate analytics. " +
        "Shows past sessions with token usage, classes tested, coverage gains, and success rates. " +
        "Use to understand patterns: which class shapes succeed, tokens-per-test, coverage gain per session.")]
    public static string GetSessions(
        [Description("Number of recent sessions to return (default: 5)")] int last = 5,
        [Description("Optional: namespace/session to query (default: server default)")] string? ns = null)
    {
        Metrics.Increment(Metrics.ToolGetSessions);
        try
        {
            return GetSessionsCore(last, ns);
        }
        catch (Exception ex)
        {
            Log.Error($"[GetSessions] failed: {ex.GetType().Name}: {ex.Message}");
            return $"ERROR in GetSessions: {ex.GetType().Name}: {ex.Message}";
        }
    }

    private static string GetSessionsCore(int last, string? ns)
    {
        var stores = StoreRegistry.ForNamespace(ns);

        if (!stores.Sessions.HasData())
            return "No sessions logged yet. Use log_session at the end of a coverage session.";

        var all = stores.Sessions.LoadAll();
        var recent = all.TakeLast(last).Reverse().ToList();

        // Compute aggregates across ALL sessions
        var totalSessions = all.Count;
        var totalTests = all.Sum(s => s.TestsGenerated);
        var totalTokens = all.Sum(s => s.TotalTokens);
        var totalCoverageDelta = all.Sum(s => s.CoverageDelta);
        var totalCoveredLines = all.Sum(s => s.CoveredLines);
        var totalClassesAttempted = all.Sum(s => s.ClassesAttempted.Count);
        var totalClassesSucceeded = all.Sum(s => s.ClassesSucceeded.Count);
        var totalClassesFailed = all.Sum(s => s.ClassesFailed.Count);

        var avgTokensPerTest = totalTests > 0 ? totalTokens / totalTests : 0;
        var avgTestsPerSession = totalSessions > 0 ? (double)totalTests / totalSessions : 0;
        var avgCoverageDeltaPerSession = totalSessions > 0 ? totalCoverageDelta / totalSessions : 0;
        var avgLinesPerTest = totalTests > 0 ? Math.Round((double)totalCoveredLines / totalTests, 2) : 0;
        var successRate = totalClassesAttempted > 0
            ? Math.Round(100.0 * totalClassesSucceeded / totalClassesAttempted, 1)
            : 0;

        // Find most successful class patterns (frequently succeeded)
        var successfulClasses = all
            .SelectMany(s => s.ClassesSucceeded)
            .GroupBy(c => c, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count())
            .Take(5)
            .Select(g => new { className = g.Key, sessions = g.Count() })
            .ToList();

        // Find most failed class patterns
        var failedPatterns = all
            .SelectMany(s => s.ClassesFailed)
            .GroupBy(f => f.Class, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count())
            .Take(5)
            .Select(g => new { className = g.Key, failures = g.Count(), lastReason = g.Last().Reason })
            .ToList();

        // ── Plateau detection (v3) ──
        // When recent sessions show declining ROI (avgLinesPerTest < 0.5),
        // signal that class-level targeting is exhausted — switch to method-level.
        var plateauWarning = DetectPlateau(all);

        // ── Session-driven recommendations (v3) ──
        var recommendations = GenerateRecommendations(all, stores);

        var result = new
        {
            aggregates = new
            {
                totalSessions,
                totalTests,
                totalTokens,
                totalCoverageDelta = Math.Round(totalCoverageDelta, 2),
                totalCoveredLines,
                avgTokensPerTest,
                avgTestsPerSession = Math.Round(avgTestsPerSession, 1),
                avgCoverageDeltaPerSession = Math.Round(avgCoverageDeltaPerSession, 2),
                avgLinesPerTest,
                classSuccessRate = $"{successRate}%",
                totalClassesAttempted,
                totalClassesSucceeded,
                totalClassesFailed
            },
            plateauWarning,
            recommendations,
            topSuccessfulClasses = successfulClasses,
            topFailedClasses = failedPatterns,
            recentSessions = recent
        };

        return JsonSerializer.Serialize(result, SharedJsonOptions.CamelCaseIndented);
    }

    /// <summary>
    /// Analyze session history to generate actionable recommendations.
    /// Closes the feedback loop: sessions log data → recommendations influence next session.
    /// </summary>
    internal static List<string> GenerateRecommendations(List<SessionRecord> all, NamespaceStores stores)
    {
        var recs = new List<string>();

        if (all.Count < 2)
            return recs;

        // 1. Repeat-failure detection: classes attempted 2+ times with 0 successes
        var attemptCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var successCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var failReasons = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var session in all)
        {
            foreach (var cls in session.ClassesAttempted)
            {
                attemptCounts[cls] = attemptCounts.GetValueOrDefault(cls) + 1;
            }
            foreach (var cls in session.ClassesSucceeded)
            {
                successCounts[cls] = successCounts.GetValueOrDefault(cls) + 1;
            }
            foreach (var fail in session.ClassesFailed)
            {
                if (!failReasons.ContainsKey(fail.Class))
                    failReasons[fail.Class] = [];
                failReasons[fail.Class].Add(fail.Reason);
            }
        }

        var repeatFailures = attemptCounts
            .Where(kvp => kvp.Value >= 2 && !successCounts.ContainsKey(kvp.Key))
            .OrderByDescending(kvp => kvp.Value)
            .Take(5)
            .ToList();

        foreach (var (cls, attempts) in repeatFailures)
        {
            var reasons = failReasons.TryGetValue(cls, out var r) ? r : [];
            var reasonSummary = reasons.Count > 0
                ? $" Reasons: {string.Join("; ", reasons.Distinct().Take(3))}"
                : "";
            recs.Add($"⚠ REPEAT FAILURE: '{cls}' attempted {attempts} times, never succeeded.{reasonSummary} " +
                     $"→ Consider AddAssessment('{cls}', 'coupled', 'failed {attempts} times across sessions')");
        }

        // 2. Declining efficiency: compare first half vs second half of sessions
        if (all.Count >= 4)
        {
            var midpoint = all.Count / 2;
            var firstHalf = all.Take(midpoint).ToList();
            var secondHalf = all.Skip(midpoint).ToList();

            var firstAvgDelta = firstHalf.Where(s => s.CoverageDelta > 0).Select(s => s.CoverageDelta).DefaultIfEmpty(0).Average();
            var secondAvgDelta = secondHalf.Where(s => s.CoverageDelta > 0).Select(s => s.CoverageDelta).DefaultIfEmpty(0).Average();

            if (firstAvgDelta > 0 && secondAvgDelta < firstAvgDelta * 0.3)
            {
                recs.Add($"📉 DIMINISHING RETURNS: Recent sessions average {secondAvgDelta:F2}% coverage gain " +
                         $"vs {firstAvgDelta:F2}% in earlier sessions ({secondAvgDelta / firstAvgDelta * 100:F0}% of prior efficiency). " +
                         "→ Switch to method-level targeting via GetUncoveredMethods, or focus on extending existing test files.");
            }
        }

        // 3. Unassessed repeat failures: suggest auto-assessment
        if (stores.Assessments.HasData())
        {
            var assessments = stores.Assessments.LoadAll()
                .GroupBy(a => a.Class, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Last(), StringComparer.OrdinalIgnoreCase);

            var unassessedFailures = repeatFailures
                .Where(kvp => !assessments.ContainsKey(kvp.Key))
                .ToList();

            if (unassessedFailures.Count > 0)
            {
                var names = string.Join(", ", unassessedFailures.Select(kvp => kvp.Key).Take(5));
                recs.Add($"📋 UNASSESSED FAILURES: {unassessedFailures.Count} class(es) have failed repeatedly " +
                         $"but have no assessment recorded: {names}. " +
                         "→ Record assessments to prevent future sessions from retrying them.");
            }
        }

        // 4. Token efficiency trend
        var sessionsWithTokens = all.Where(s => s.TotalTokens > 0 && s.TestsGenerated > 0).ToList();
        if (sessionsWithTokens.Count >= 3)
        {
            var recentTokenEfficiency = sessionsWithTokens.TakeLast(3)
                .Average(s => (double)s.TotalTokens / s.TestsGenerated);
            var overallTokenEfficiency = sessionsWithTokens
                .Average(s => (double)s.TotalTokens / s.TestsGenerated);

            if (recentTokenEfficiency > overallTokenEfficiency * 2)
            {
                recs.Add($"🔥 TOKEN COST RISING: Recent sessions use {recentTokenEfficiency:N0} tokens/test " +
                         $"vs {overallTokenEfficiency:N0} overall average. " +
                         "→ Focus on simpler targets or use GenerateTestScaffold to reduce boilerplate generation.");
            }
        }

        // 5. Best-performing class shapes (what works)
        var successPatterns = all
            .SelectMany(s => s.ClassesSucceeded.Select(c => new { Class = c, s.CoverageDelta, s.TestsGenerated }))
            .GroupBy(x => x.Class, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() >= 2)
            .Select(g => new { Class = g.Key, AvgDelta = g.Average(x => x.CoverageDelta) })
            .OrderByDescending(x => x.AvgDelta)
            .Take(3)
            .ToList();

        if (successPatterns.Count > 0)
        {
            var names = string.Join(", ", successPatterns.Select(p => p.Class));
            recs.Add($"✅ HIGH-ROI PATTERN: Classes tested multiple times with good results: {names}. " +
                     "→ Look for similar classes (same namespace, same base type, similar ctor signature).");
        }

        // 6. Strategy shift recommendations (when ROI is declining)
        var sessionsWithCoverage = all.Where(s => s.CoveredLines > 0 && s.TestsGenerated > 0).ToList();
        if (sessionsWithCoverage.Count >= 3)
        {
            var recentLpt = sessionsWithCoverage.TakeLast(3)
                .Average(s => (double)s.CoveredLines / s.TestsGenerated);

            if (recentLpt < 0.5)
            {
                recs.Add("🔄 STRATEGY SHIFT: Class-level targeting is exhausted (lines/test < 0.5). " +
                         "Recommended approach in priority order:\n" +
                         "  1. get_uncovered_methods(onlyWithExistingTests=true) — extend existing test files for partially-covered methods\n" +
                         "  2. get_stub_classes() — find zero-coverage trivially-testable classes (POCOs, static helpers)\n" +
                         "  3. Focus on integration tests for tightly-coupled clusters\n" +
                         "  4. Consider branch coverage (if/else paths) instead of line coverage for already-tested classes\n" +
                         "  5. Refactor tightly-coupled classes to extract testable interfaces");
            }
            else if (recentLpt < 1.5)
            {
                recs.Add($"📊 ROI SOFTENING: Recent lines/test ({recentLpt:F1}) suggests diminishing returns from standard class targeting. " +
                         "Consider supplementing with:\n" +
                         "  • get_stub_classes() for easy wins\n" +
                         "  • Incremental scaffold mode (methodNames param) for surgical coverage of specific methods");
            }
        }

        // 7. Session count milestone recommendations
        if (all.Count >= 10 && all.Count % 5 == 0)
        {
            var totalAttempted = attemptCounts.Values.Sum();
            var totalSucceeded = successCounts.Values.Sum();
            var overallSuccessRate = totalAttempted > 0
                ? 100.0 * totalSucceeded / totalAttempted
                : 0;
            if (overallSuccessRate < 50)
            {
                recs.Add($"📈 SESSION {all.Count} CHECKPOINT: Overall success rate is {overallSuccessRate:F0}% " +
                         $"({totalSucceeded}/{totalAttempted}). " +
                         "If most failures are coupled classes, run get_gotcha_insights() to identify systemic patterns, " +
                         "then document them in AGENTS.md to prevent future agent reattempts.");
            }
        }

        return recs;
    }

    /// <summary>
    /// Detect coverage plateau: when recent sessions show declining ROI.
    /// Returns a warning string if plateau detected, null otherwise.
    /// Plateau criteria: at least 3 sessions with coveredLines data, and the last 3 sessions
    /// average less than 0.5 lines/test — meaning class-level targeting is exhausted.
    /// </summary>
    internal static string? DetectPlateau(List<SessionRecord> allSessions)
    {
        // Only analyze sessions that have coveredLines data (v2.1+ sessions)
        var sessionsWithData = allSessions
            .Where(s => s.CoveredLines > 0 && s.TestsGenerated > 0)
            .ToList();

        if (sessionsWithData.Count < 3)
            return null;

        // Look at the last 3 sessions with data
        var recentWithData = sessionsWithData.TakeLast(3).ToList();
        var recentTotalLines = recentWithData.Sum(s => s.CoveredLines);
        var recentTotalTests = recentWithData.Sum(s => s.TestsGenerated);

        if (recentTotalTests == 0)
            return null;

        var recentLinesPerTest = (double)recentTotalLines / recentTotalTests;

        if (recentLinesPerTest < 0.5)
        {
            return $"⚠ Coverage plateau detected: last {recentWithData.Count} sessions averaged " +
                   $"{recentLinesPerTest:F2} lines/test (threshold: 0.5). " +
                   "Class-level targets are likely exhausted. " +
                   "Recommended: use get_uncovered_methods with onlyWithExistingTests=true to extend " +
                   "existing test files for partially-covered methods.";
        }

        // Also check for declining trend even if above threshold
        if (sessionsWithData.Count >= 4)
        {
            var olderSessions = sessionsWithData.SkipLast(3).TakeLast(3).ToList();
            if (olderSessions.Count >= 2)
            {
                var olderTotalLines = olderSessions.Sum(s => s.CoveredLines);
                var olderTotalTests = olderSessions.Sum(s => s.TestsGenerated);
                if (olderTotalTests > 0)
                {
                    var olderLinesPerTest = (double)olderTotalLines / olderTotalTests;
                    if (olderLinesPerTest > 0 && recentLinesPerTest < olderLinesPerTest * 0.5)
                    {
                        return $"⚠ ROI declining: recent sessions averaged {recentLinesPerTest:F2} lines/test " +
                               $"vs {olderLinesPerTest:F2} previously ({recentLinesPerTest / olderLinesPerTest * 100:F0}% of prior efficiency). " +
                               "Consider switching to get_uncovered_methods for method-level targeting.";
                    }
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Parse a class list that may be CSV ("ClassA, ClassB") or a JSON array ("[\"ClassA\", \"ClassB\"]").
    /// Agents commonly pass JSON arrays when the tool description says "comma-separated".
    /// </summary>
    internal static List<string> ParseCsv(string csv)
    {
        if (string.IsNullOrWhiteSpace(csv))
            return [];

        var trimmed = csv.Trim();

        // Detect JSON array input: ["ClassA", "ClassB"]
        if (trimmed.StartsWith('[') && trimmed.EndsWith(']'))
        {
            try
            {
                var parsed = JsonSerializer.Deserialize<List<string>>(trimmed);
                if (parsed is not null)
                    return parsed.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()).ToList();
            }
            catch (JsonException)
            {
                // Not valid JSON — fall through to CSV parsing
            }
        }

        return csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(s => !string.IsNullOrEmpty(s))
            .ToList();
    }

    /// <summary>
    /// Parse failures that may be CSV ("ClassA:reason, ClassB:reason") or a JSON array.
    /// </summary>
    internal static List<SessionFailure> ParseFailures(string csv)
    {
        if (string.IsNullOrWhiteSpace(csv))
            return [];

        var trimmed = csv.Trim();

        // Detect JSON array input: ["ClassA:reason", "ClassB:reason"] or [{"class":"A","reason":"r"}]
        if (trimmed.StartsWith('[') && trimmed.EndsWith(']'))
        {
            try
            {
                // Try as List<string> first (e.g. ["ClassA:compile error", "ClassB:timeout"])
                var parsed = JsonSerializer.Deserialize<List<string>>(trimmed);
                if (parsed is not null)
                    return parsed
                        .Where(s => !string.IsNullOrWhiteSpace(s))
                        .Select(ParseSingleFailure)
                        .ToList();
            }
            catch (JsonException)
            {
                // Not valid JSON — fall through to CSV parsing
            }
        }

        return csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(ParseSingleFailure)
            .ToList();
    }

    private static SessionFailure ParseSingleFailure(string entry)
    {
        var colonIdx = entry.IndexOf(':');
        if (colonIdx > 0)
        {
            return new SessionFailure
            {
                Class = entry[..colonIdx].Trim(),
                Reason = entry[(colonIdx + 1)..].Trim()
            };
        }
        return new SessionFailure { Class = entry.Trim(), Reason = "unspecified" };
    }

    /// <summary>
    /// Get the end time of the last recorded session, or null if no sessions exist.
    /// Used for auto-aggregation: count records added since last session.
    /// </summary>
    internal static DateTime? GetLastSessionEndTime(NamespaceStores stores)
    {
        if (!stores.Sessions.HasData())
            return null;

        var sessions = stores.Sessions.LoadAll();
        if (sessions.Count == 0)
            return null;

        var lastSession = sessions[^1];
        if (DateTimeOffset.TryParse(lastSession.EndedUtc, out var endTime))
            return endTime.UtcDateTime;

        return null;
    }

    /// <summary>
    /// Count records whose date field is after the given cutoff time.
    /// If cutoff is null (no previous sessions), counts ALL records.
    /// </summary>
    internal static int CountRecordsSince<T>(List<T> records, Func<T, string> dateSelector, DateTime? cutoff)
    {
        if (cutoff is null)
            return records.Count;

        return records.Count(r =>
        {
            var dateStr = dateSelector(r);
            return DateTimeOffset.TryParse(dateStr, out var d) && d.UtcDateTime > cutoff.Value;
        });
    }

    /// <summary>
    /// Estimate testsGenerated from recent session averages.
    /// Uses only the most recent 3 sessions with real test data to avoid inflated estimates
    /// from early high-ROI sessions. Returns 0 if no usable data exists.
    /// </summary>
    internal static int EstimateTestsGenerated(List<SessionRecord> pastSessions, int succeededCount)
    {
        if (succeededCount <= 0) return 0;
        var withTests = pastSessions
            .Where(s => s.TestsGenerated > 0 && s.ClassesSucceeded.Count > 0)
            .TakeLast(3) // recency bias: only last 3 sessions with real data
            .ToList();
        if (withTests.Count == 0) return 0;
        var avg = withTests.Average(s => (double)s.TestsGenerated / s.ClassesSucceeded.Count);
        return (int)Math.Round(avg * succeededCount);
    }
}
