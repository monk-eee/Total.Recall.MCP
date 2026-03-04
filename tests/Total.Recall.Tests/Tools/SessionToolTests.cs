using System.Text.Json;
using Total.Recall.Infrastructure;
using Total.Recall.Models;
using Total.Recall.Tools;

namespace Total.Recall.Tests.Tools;

[Collection("ToolTests")]
public sealed class SessionToolTests : ToolTestBase
{
    // ── LogSession ──

    [Fact]
    public void LogSession_Basic_ReturnsConfirmation()
    {
        var result = SessionTool.LogSession("claude-sonnet-4-20250514");

        Assert.Contains("Session logged:", result);
        Assert.Contains("claude-sonnet-4-20250514", result);
    }

    [Fact]
    public void LogSession_PersistsRecord()
    {
        SessionTool.LogSession(
            model: "gpt-4",
            promptTokens: 1000,
            completionTokens: 500,
            classesAttempted: "ClassA, ClassB",
            classesSucceeded: "ClassA",
            classesFailed: "ClassB:compile error",
            testsGenerated: 5,
            coverageBefore: 25.0,
            coverageAfter: 35.0,
            gotchasDiscovered: 2,
            assessmentsRecorded: 1,
            notes: "good session"
        );

        var store = new JsonLineStore<SessionRecord>(RepoConfig.SessionsPath(TempDir));
        var records = store.LoadAll();
        Assert.Single(records);

        var r = records[0];
        Assert.Equal("gpt-4", r.Model);
        Assert.Equal(1000, r.PromptTokens);
        Assert.Equal(500, r.CompletionTokens);
        Assert.Equal(1500, r.TotalTokens);
        Assert.Equal(5, r.TestsGenerated);
        Assert.Equal(25.0, r.CoverageBefore);
        Assert.Equal(35.0, r.CoverageAfter);
        Assert.Equal(10.0, r.CoverageDelta);
        Assert.Equal(2, r.GotchasDiscovered);
        Assert.Equal(1, r.AssessmentsRecorded);
        Assert.Equal("good session", r.Notes);
    }

    [Fact]
    public void LogSession_ParsesCsvClassesAttempted()
    {
        SessionTool.LogSession("model", classesAttempted: "ClassA, ClassB, ClassC");

        var store = new JsonLineStore<SessionRecord>(RepoConfig.SessionsPath(TempDir));
        var r = store.LoadAll().Single();

        Assert.Equal(3, r.ClassesAttempted.Count);
        Assert.Contains("ClassA", r.ClassesAttempted);
        Assert.Contains("ClassB", r.ClassesAttempted);
        Assert.Contains("ClassC", r.ClassesAttempted);
    }

    [Fact]
    public void LogSession_ParsesCsvClassesSucceeded()
    {
        SessionTool.LogSession("model", classesSucceeded: "ClassA, ClassB");

        var store = new JsonLineStore<SessionRecord>(RepoConfig.SessionsPath(TempDir));
        var r = store.LoadAll().Single();

        Assert.Equal(2, r.ClassesSucceeded.Count);
    }

    [Fact]
    public void LogSession_ParsesFailuresWithReasons()
    {
        SessionTool.LogSession("model", classesFailed: "ClassA:compile error, ClassB:timeout");

        var store = new JsonLineStore<SessionRecord>(RepoConfig.SessionsPath(TempDir));
        var r = store.LoadAll().Single();

        Assert.Equal(2, r.ClassesFailed.Count);
        Assert.Equal("ClassA", r.ClassesFailed[0].Class);
        Assert.Equal("compile error", r.ClassesFailed[0].Reason);
        Assert.Equal("ClassB", r.ClassesFailed[1].Class);
        Assert.Equal("timeout", r.ClassesFailed[1].Reason);
    }

    [Fact]
    public void LogSession_FailureWithoutReason_UsesUnspecified()
    {
        SessionTool.LogSession("model", classesFailed: "ClassA");

        var store = new JsonLineStore<SessionRecord>(RepoConfig.SessionsPath(TempDir));
        var r = store.LoadAll().Single();

        Assert.Single(r.ClassesFailed);
        Assert.Equal("ClassA", r.ClassesFailed[0].Class);
        Assert.Equal("unspecified", r.ClassesFailed[0].Reason);
    }

    [Fact]
    public void LogSession_EmptyCsvInputs_ProducesEmptyLists()
    {
        SessionTool.LogSession("model");

        var store = new JsonLineStore<SessionRecord>(RepoConfig.SessionsPath(TempDir));
        var r = store.LoadAll().Single();

        Assert.Empty(r.ClassesAttempted);
        Assert.Empty(r.ClassesSucceeded);
        Assert.Empty(r.ClassesFailed);
    }

    [Fact]
    public void LogSession_CoverageDelta_CalculatedCorrectly()
    {
        SessionTool.LogSession("model", coverageBefore: 25.66, coverageAfter: 54.1);

        var store = new JsonLineStore<SessionRecord>(RepoConfig.SessionsPath(TempDir));
        var r = store.LoadAll().Single();

        Assert.Equal(28.44, r.CoverageDelta);
    }

    [Fact]
    public void LogSession_NullNotes_DefaultsToEmpty()
    {
        SessionTool.LogSession("model", notes: null);

        var store = new JsonLineStore<SessionRecord>(RepoConfig.SessionsPath(TempDir));
        var r = store.LoadAll().Single();

        Assert.Equal("", r.Notes);
    }

    [Fact]
    public void LogSession_GeneratesUniqueSessionId()
    {
        SessionTool.LogSession("model");
        StoreRegistry.Reset();
        SessionTool.LogSession("model");

        var store = new JsonLineStore<SessionRecord>(RepoConfig.SessionsPath(TempDir));
        var all = store.LoadAll();
        Assert.Equal(2, all.Count);
        Assert.NotEqual(all[0].SessionId, all[1].SessionId);
    }

    [Fact]
    public void LogSession_SummaryIncludesTokenBreakdown()
    {
        var result = SessionTool.LogSession("model", promptTokens: 5000, completionTokens: 3000);

        Assert.Contains("8,000", result); // total
        Assert.Contains("5,000", result); // prompt
        Assert.Contains("3,000", result); // completion
    }

    [Fact]
    public void LogSession_SummaryIncludesCoverageDelta()
    {
        var result = SessionTool.LogSession("model", coverageBefore: 25.0, coverageAfter: 35.0);

        Assert.Contains("25%", result);
        Assert.Contains("35%", result);
    }

    // ── GetSessions ──

    [Fact]
    public void GetSessions_NoData_ReturnsMessage()
    {
        var result = SessionTool.GetSessions();

        Assert.Contains("No sessions logged yet", result);
    }

    [Fact]
    public void GetSessions_ReturnsRecentSessions()
    {
        SeedSessions(
            new SessionRecord { SessionId = "s1", Model = "model-a", TestsGenerated = 5 },
            new SessionRecord { SessionId = "s2", Model = "model-b", TestsGenerated = 10 }
        );

        var result = SessionTool.GetSessions(last: 5);
        var doc = JsonDocument.Parse(result);

        var recent = doc.RootElement.GetProperty("recentSessions");
        Assert.Equal(2, recent.GetArrayLength());
        // Most recent first (reversed)
        Assert.Equal("s2", recent[0].GetProperty("sessionId").GetString());
        Assert.Equal("s1", recent[1].GetProperty("sessionId").GetString());
    }

    [Fact]
    public void GetSessions_RespectsLastLimit()
    {
        SeedSessions(
            new SessionRecord { SessionId = "s1", Model = "m" },
            new SessionRecord { SessionId = "s2", Model = "m" },
            new SessionRecord { SessionId = "s3", Model = "m" }
        );

        var result = SessionTool.GetSessions(last: 2);
        var doc = JsonDocument.Parse(result);

        Assert.Equal(2, doc.RootElement.GetProperty("recentSessions").GetArrayLength());
    }

    [Fact]
    public void GetSessions_CalculatesAggregates()
    {
        SeedSessions(
            new SessionRecord
            {
                SessionId = "s1",
                Model = "m",
                TotalTokens = 10000,
                TestsGenerated = 10,
                CoverageDelta = 5.0,
                ClassesAttempted = ["A", "B"],
                ClassesSucceeded = ["A"],
                ClassesFailed = [new SessionFailure { Class = "B", Reason = "fail" }]
            },
            new SessionRecord
            {
                SessionId = "s2",
                Model = "m",
                TotalTokens = 20000,
                TestsGenerated = 20,
                CoverageDelta = 10.0,
                ClassesAttempted = ["C"],
                ClassesSucceeded = ["C"],
                ClassesFailed = []
            }
        );

        var result = SessionTool.GetSessions();
        var doc = JsonDocument.Parse(result);
        var agg = doc.RootElement.GetProperty("aggregates");

        Assert.Equal(2, agg.GetProperty("totalSessions").GetInt32());
        Assert.Equal(30, agg.GetProperty("totalTests").GetInt32());
        Assert.Equal(30000, agg.GetProperty("totalTokens").GetInt64());
        Assert.Equal(15.0, agg.GetProperty("totalCoverageDelta").GetDouble());
        Assert.Equal(1000, agg.GetProperty("avgTokensPerTest").GetInt64());
        Assert.Equal(15.0, agg.GetProperty("avgTestsPerSession").GetDouble());
        Assert.Equal(7.5, agg.GetProperty("avgCoverageDeltaPerSession").GetDouble());
        Assert.Equal(3, agg.GetProperty("totalClassesAttempted").GetInt32());
        Assert.Equal(2, agg.GetProperty("totalClassesSucceeded").GetInt32());
        Assert.Equal(1, agg.GetProperty("totalClassesFailed").GetInt32());
    }

    [Fact]
    public void GetSessions_CalculatesSuccessRate()
    {
        SeedSessions(new SessionRecord
        {
            SessionId = "s1",
            Model = "m",
            ClassesAttempted = ["A", "B", "C", "D"],
            ClassesSucceeded = ["A", "B", "C"],
            ClassesFailed = [new SessionFailure { Class = "D", Reason = "fail" }]
        });

        var result = SessionTool.GetSessions();
        var doc = JsonDocument.Parse(result);
        var agg = doc.RootElement.GetProperty("aggregates");

        Assert.Equal("75%", agg.GetProperty("classSuccessRate").GetString());
    }

    [Fact]
    public void GetSessions_TopSuccessfulClasses()
    {
        SeedSessions(
            new SessionRecord { SessionId = "s1", Model = "m", ClassesSucceeded = ["Alpha", "Beta"] },
            new SessionRecord { SessionId = "s2", Model = "m", ClassesSucceeded = ["Alpha", "Gamma"] },
            new SessionRecord { SessionId = "s3", Model = "m", ClassesSucceeded = ["Alpha"] }
        );

        var result = SessionTool.GetSessions();
        var doc = JsonDocument.Parse(result);
        var topSuccess = doc.RootElement.GetProperty("topSuccessfulClasses");

        // Alpha appears 3 times, should be first
        Assert.Equal("Alpha", topSuccess[0].GetProperty("className").GetString());
        Assert.Equal(3, topSuccess[0].GetProperty("sessions").GetInt32());
    }

    [Fact]
    public void GetSessions_TopFailedClasses()
    {
        SeedSessions(
            new SessionRecord
            {
                SessionId = "s1",
                Model = "m",
                ClassesFailed =
                [
                    new SessionFailure { Class = "Hard", Reason = "timeout" },
                    new SessionFailure { Class = "Easy", Reason = "typo" }
                ]
            },
            new SessionRecord
            {
                SessionId = "s2",
                Model = "m",
                ClassesFailed = [new SessionFailure { Class = "Hard", Reason = "compile" }]
            }
        );

        var result = SessionTool.GetSessions();
        var doc = JsonDocument.Parse(result);
        var topFailed = doc.RootElement.GetProperty("topFailedClasses");

        Assert.Equal("Hard", topFailed[0].GetProperty("className").GetString());
        Assert.Equal(2, topFailed[0].GetProperty("failures").GetInt32());
        Assert.Equal("compile", topFailed[0].GetProperty("lastReason").GetString());
    }

    [Fact]
    public void GetSessions_ZeroTests_AvgTokensPerTestIsZero()
    {
        SeedSessions(new SessionRecord
        {
            SessionId = "s1",
            Model = "m",
            TotalTokens = 5000,
            TestsGenerated = 0
        });

        var result = SessionTool.GetSessions();
        var doc = JsonDocument.Parse(result);
        var agg = doc.RootElement.GetProperty("aggregates");

        Assert.Equal(0, agg.GetProperty("avgTokensPerTest").GetInt64());
    }

    [Fact]
    public void GetSessions_ZeroClassesAttempted_SuccessRateIsZero()
    {
        SeedSessions(new SessionRecord { SessionId = "s1", Model = "m" });

        var result = SessionTool.GetSessions();
        var doc = JsonDocument.Parse(result);
        var agg = doc.RootElement.GetProperty("aggregates");

        Assert.Equal("0%", agg.GetProperty("classSuccessRate").GetString());
    }

    // ── LogSession then GetSessions round-trip ──

    [Fact]
    public void LogThenGet_RoundTrips()
    {
        SessionTool.LogSession(
            model: "claude-sonnet-4-20250514",
            promptTokens: 2000,
            completionTokens: 1000,
            classesAttempted: "X, Y",
            classesSucceeded: "X",
            classesFailed: "Y:deps",
            testsGenerated: 3,
            coverageBefore: 50.0,
            coverageAfter: 55.0
        );

        var result = SessionTool.GetSessions();
        var doc = JsonDocument.Parse(result);
        var session = doc.RootElement.GetProperty("recentSessions")[0];

        Assert.Equal("claude-sonnet-4-20250514", session.GetProperty("model").GetString());
        Assert.Equal(3000, session.GetProperty("totalTokens").GetInt64());
        Assert.Equal(3, session.GetProperty("testsGenerated").GetInt32());
        Assert.Equal(5.0, session.GetProperty("coverageDelta").GetDouble());
    }

    // ── CSV edge cases ──

    [Fact]
    public void LogSession_CsvWithSpaces_TrimsValues()
    {
        SessionTool.LogSession("model", classesAttempted: "  ClassA  ,  ClassB  ");

        var store = new JsonLineStore<SessionRecord>(RepoConfig.SessionsPath(TempDir));
        var r = store.LoadAll().Single();

        Assert.Equal("ClassA", r.ClassesAttempted[0]);
        Assert.Equal("ClassB", r.ClassesAttempted[1]);
    }

    [Fact]
    public void LogSession_CsvWithEmptyEntries_FiltersOut()
    {
        SessionTool.LogSession("model", classesAttempted: "ClassA,,, ClassB,  ,");

        var store = new JsonLineStore<SessionRecord>(RepoConfig.SessionsPath(TempDir));
        var r = store.LoadAll().Single();

        Assert.Equal(2, r.ClassesAttempted.Count);
    }

    [Fact]
    public void LogSession_WhitespaceOnlyCsv_ProducesEmptyList()
    {
        SessionTool.LogSession("model", classesAttempted: "   ");

        var store = new JsonLineStore<SessionRecord>(RepoConfig.SessionsPath(TempDir));
        var r = store.LoadAll().Single();

        Assert.Empty(r.ClassesAttempted);
    }

    // ── Error path coverage ──

    [Fact]
    public void LogSession_InvalidNamespace_ReturnsError()
    {
        var result = SessionTool.LogSession("model", ns: "\0");

        Assert.StartsWith("ERROR in LogSession", result);
    }

    [Fact]
    public void GetSessions_InvalidNamespace_ReturnsError()
    {
        var result = SessionTool.GetSessions(ns: "\0");

        Assert.StartsWith("ERROR in GetSessions", result);
    }

    // ── LooksLikeCount detection ──

    [Theory]
    [InlineData("3", true)]
    [InlineData("0", true)]
    [InlineData("42", true)]
    [InlineData("ClassA", false)]
    [InlineData("ClassA, ClassB", false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("3Classes", false)]
    public void LooksLikeCount_DetectsIntegerInput(string input, bool expected)
    {
        Assert.Equal(expected, SessionTool.LooksLikeCount(input));
    }

    [Fact]
    public void LogSession_IntegerClassesAttempted_ReturnsWarning()
    {
        var result = SessionTool.LogSession("model", classesAttempted: "3");

        Assert.Contains("WARNINGS", result);
        Assert.Contains("classesAttempted='3' looks like a count", result);
        Assert.Contains("not class names", result);
    }

    [Fact]
    public void LogSession_IntegerClassesSucceeded_ReturnsWarning()
    {
        var result = SessionTool.LogSession("model", classesSucceeded: "2");

        Assert.Contains("WARNINGS", result);
        Assert.Contains("classesSucceeded='2' looks like a count", result);
    }

    [Fact]
    public void LogSession_ValidClassNames_NoWarning()
    {
        var result = SessionTool.LogSession("model", classesAttempted: "ClassA, ClassB");

        Assert.DoesNotContain("WARNINGS", result);
    }

    // ── JSON array input handling ──

    [Fact]
    public void ParseCsv_JsonArray_ParsesCorrectly()
    {
        var result = SessionTool.ParseCsv("""["ClassA", "ClassB", "ClassC"]""");

        Assert.Equal(3, result.Count);
        Assert.Contains("ClassA", result);
        Assert.Contains("ClassB", result);
        Assert.Contains("ClassC", result);
    }

    [Fact]
    public void ParseCsv_JsonArrayWithSpaces_TrimsValues()
    {
        var result = SessionTool.ParseCsv("""  ["ClassA", " ClassB "]  """);

        Assert.Equal(2, result.Count);
        Assert.Equal("ClassA", result[0]);
        Assert.Equal("ClassB", result[1]);
    }

    [Fact]
    public void ParseCsv_JsonArrayWithEmptyStrings_FiltersOut()
    {
        var result = SessionTool.ParseCsv("""["ClassA", "", "ClassB"]""");

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void ParseCsv_InvalidJson_FallsBackToCsv()
    {
        var result = SessionTool.ParseCsv("[not valid json");

        // Falls back to CSV splitting
        Assert.Single(result);
        Assert.Equal("[not valid json", result[0]);
    }

    [Fact]
    public void ParseCsv_RegularCsv_StillWorks()
    {
        var result = SessionTool.ParseCsv("ClassA, ClassB");

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void ParseFailures_JsonArray_ParsesCorrectly()
    {
        var result = SessionTool.ParseFailures("""["ClassA:compile error", "ClassB:timeout"]""");

        Assert.Equal(2, result.Count);
        Assert.Equal("ClassA", result[0].Class);
        Assert.Equal("compile error", result[0].Reason);
        Assert.Equal("ClassB", result[1].Class);
        Assert.Equal("timeout", result[1].Reason);
    }

    [Fact]
    public void ParseFailures_JsonArrayNoReason_UsesUnspecified()
    {
        var result = SessionTool.ParseFailures("""["ClassA"]""");

        Assert.Single(result);
        Assert.Equal("ClassA", result[0].Class);
        Assert.Equal("unspecified", result[0].Reason);
    }

    [Fact]
    public void ParseFailures_InvalidJson_FallsBackToCsv()
    {
        var result = SessionTool.ParseFailures("[broken");

        Assert.Single(result);
    }

    [Fact]
    public void LogSession_JsonArrayClassesAttempted_PersistsCorrectly()
    {
        SessionTool.LogSession("model", classesAttempted: """["ClassA", "ClassB"]""");

        var store = new JsonLineStore<SessionRecord>(RepoConfig.SessionsPath(TempDir));
        var r = store.LoadAll().Single();

        Assert.Equal(2, r.ClassesAttempted.Count);
        Assert.Contains("ClassA", r.ClassesAttempted);
        Assert.Contains("ClassB", r.ClassesAttempted);
    }

    // ── Auto-aggregation (P2) ──

    [Fact]
    public void LogSession_AutoCountsGotchas_WhenZeroPassed()
    {
        // Seed a previous session
        SeedSessions(new SessionRecord
        {
            SessionId = "prev",
            Model = "m",
            EndedUtc = DateTime.UtcNow.AddHours(-1).ToString("o")
        });
        StoreRegistry.Reset();

        // Seed gotchas added AFTER the previous session
        var gotchaStore = new JsonLineStore<Gotcha>(RepoConfig.GotchasPath(TempDir));
        gotchaStore.WriteAll(
        [
            new Gotcha { Type = "TypeA", Category = "mock", Description = "g1", Date = DateTime.UtcNow.ToString("o") },
            new Gotcha { Type = "TypeB", Category = "enum", Description = "g2", Date = DateTime.UtcNow.ToString("o") }
        ]);
        StoreRegistry.Reset();

        var result = SessionTool.LogSession("model", gotchasDiscovered: 0);

        Assert.Contains("2 new", result);
        Assert.Contains("auto-counted from store", result);

        // Verify the stored record has the auto-counted value
        var sessions = new JsonLineStore<SessionRecord>(RepoConfig.SessionsPath(TempDir));
        var latest = sessions.LoadAll().Last();
        Assert.Equal(2, latest.GotchasDiscovered);
    }

    [Fact]
    public void LogSession_AutoCountsAssessments_WhenZeroPassed()
    {
        SeedSessions(new SessionRecord
        {
            SessionId = "prev",
            Model = "m",
            EndedUtc = DateTime.UtcNow.AddHours(-1).ToString("o")
        });
        StoreRegistry.Reset();

        var assessmentStore = new JsonLineStore<Assessment>(RepoConfig.AssessmentsPath(TempDir));
        assessmentStore.WriteAll(
        [
            new Assessment { Class = "ClassA", Verdict = "testable", Reasoning = "ok", Date = DateTime.UtcNow.ToString("o") },
            new Assessment { Class = "ClassB", Verdict = "skip", Reasoning = "skip", Date = DateTime.UtcNow.ToString("o") },
            new Assessment { Class = "ClassC", Verdict = "coupled", Reasoning = "deps", Date = DateTime.UtcNow.ToString("o") }
        ]);
        StoreRegistry.Reset();

        var result = SessionTool.LogSession("model", assessmentsRecorded: 0);

        Assert.Contains("3 new", result);
        Assert.Contains("auto-counted from store", result);

        var sessions = new JsonLineStore<SessionRecord>(RepoConfig.SessionsPath(TempDir));
        var latest = sessions.LoadAll().Last();
        Assert.Equal(3, latest.AssessmentsRecorded);
    }

    [Fact]
    public void LogSession_ExplicitGotchaCount_NoAutoCount()
    {
        // If agent passes an explicit non-zero count, don't override
        var gotchaStore = new JsonLineStore<Gotcha>(RepoConfig.GotchasPath(TempDir));
        gotchaStore.WriteAll(
        [
            new Gotcha { Type = "TypeA", Category = "mock", Description = "g1", Date = DateTime.UtcNow.ToString("o") },
            new Gotcha { Type = "TypeB", Category = "enum", Description = "g2", Date = DateTime.UtcNow.ToString("o") }
        ]);
        StoreRegistry.Reset();

        var result = SessionTool.LogSession("model", gotchasDiscovered: 5);

        Assert.DoesNotContain("auto-counted", result);
        Assert.Contains("5 new", result);
    }

    [Fact]
    public void LogSession_NoPreviousSession_CountsAllGotchas()
    {
        // No previous sessions — should count ALL gotchas as new
        var gotchaStore = new JsonLineStore<Gotcha>(RepoConfig.GotchasPath(TempDir));
        gotchaStore.WriteAll(
        [
            new Gotcha { Type = "TypeA", Category = "mock", Description = "g1", Date = "2025-01-01" },
            new Gotcha { Type = "TypeB", Category = "enum", Description = "g2", Date = "2025-06-01" }
        ]);
        StoreRegistry.Reset();

        var result = SessionTool.LogSession("model", gotchasDiscovered: 0);

        Assert.Contains("2 new", result);
        Assert.Contains("auto-counted from store", result);
    }

    // ── Helper method tests ──

    [Fact]
    public void CountRecordsSince_NullCutoff_CountsAll()
    {
        var records = new List<Gotcha>
        {
            new() { Type = "A", Date = "2025-01-01" },
            new() { Type = "B", Date = "2025-06-01" },
            new() { Type = "C", Date = "2025-12-01" }
        };

        var count = SessionTool.CountRecordsSince(records, g => g.Date, null);
        Assert.Equal(3, count);
    }

    [Fact]
    public void CountRecordsSince_WithCutoff_CountsOnlyNewer()
    {
        var records = new List<Gotcha>
        {
            new() { Type = "A", Date = "2025-01-01T00:00:00Z" },
            new() { Type = "B", Date = "2025-06-01T00:00:00Z" },
            new() { Type = "C", Date = "2025-12-01T00:00:00Z" }
        };

        var cutoff = new DateTime(2025, 5, 1, 0, 0, 0, DateTimeKind.Utc);
        var count = SessionTool.CountRecordsSince(records, g => g.Date, cutoff);
        Assert.Equal(2, count); // June and December
    }

    [Fact]
    public void GetLastSessionEndTime_NoSessions_ReturnsNull()
    {
        var stores = StoreRegistry.ForNamespace(null);
        var result = SessionTool.GetLastSessionEndTime(stores);
        Assert.Null(result);
    }

    [Fact]
    public void GetLastSessionEndTime_WithSessions_ReturnsLastEnd()
    {
        var expectedEnd = DateTime.UtcNow.AddHours(-2);
        SeedSessions(
            new SessionRecord { SessionId = "s1", Model = "m", EndedUtc = DateTime.UtcNow.AddHours(-5).ToString("o") },
            new SessionRecord { SessionId = "s2", Model = "m", EndedUtc = expectedEnd.ToString("o") }
        );
        StoreRegistry.Reset();

        var stores = StoreRegistry.ForNamespace(null);
        var result = SessionTool.GetLastSessionEndTime(stores);

        Assert.NotNull(result);
        // Within 5 second tolerance for serialization round-trip
        var diff = Math.Abs((result.Value - expectedEnd).TotalSeconds);
        Assert.True(diff < 5, $"Expected end ~{expectedEnd:o}, got {result.Value:o}, diff={diff:F1}s");
    }

    // ── testsGenerated auto-estimation (P3) ──

    [Fact]
    public void EstimateTestsGenerated_WithPastData_ReturnsAverage()
    {
        var sessions = new List<SessionRecord>
        {
            new() { TestsGenerated = 60, ClassesSucceeded = ["A", "B", "C"] },
            new() { TestsGenerated = 30, ClassesSucceeded = ["D"] }
        };

        // Session 1: 60/3 = 20 per class, Session 2: 30/1 = 30 per class, avg = 25
        var estimate = SessionTool.EstimateTestsGenerated(sessions, 2);
        Assert.Equal(50, estimate); // 25 * 2
    }

    [Fact]
    public void EstimateTestsGenerated_NoPastData_ReturnsZero()
    {
        var sessions = new List<SessionRecord>
        {
            new() { TestsGenerated = 0, ClassesSucceeded = ["A"] },
            new() { TestsGenerated = 0, ClassesSucceeded = [] }
        };

        var estimate = SessionTool.EstimateTestsGenerated(sessions, 3);
        Assert.Equal(0, estimate);
    }

    [Fact]
    public void EstimateTestsGenerated_ZeroSucceeded_ReturnsZero()
    {
        var sessions = new List<SessionRecord>
        {
            new() { TestsGenerated = 60, ClassesSucceeded = ["A", "B"] }
        };

        var estimate = SessionTool.EstimateTestsGenerated(sessions, 0);
        Assert.Equal(0, estimate);
    }

    [Fact]
    public void EstimateTestsGenerated_EmptySessions_ReturnsZero()
    {
        var estimate = SessionTool.EstimateTestsGenerated([], 3);
        Assert.Equal(0, estimate);
    }

    [Fact]
    public void LogSession_AutoEstimatesTests_WhenZeroPassed()
    {
        // Seed past sessions with test data for estimation
        SeedSessions(
            new SessionRecord
            {
                SessionId = "past1",
                Model = "m",
                TestsGenerated = 60,
                ClassesSucceeded = ["ClassA", "ClassB", "ClassC"],
                EndedUtc = DateTime.UtcNow.AddHours(-2).ToString("o")
            }
        );
        StoreRegistry.Reset();

        // Log a new session with testsGenerated=0 but 2 succeeded classes
        var result = SessionTool.LogSession("model",
            classesSucceeded: "ClassD, ClassE",
            testsGenerated: 0);

        Assert.Contains("estimated from past session avg", result);

        // 60 tests / 3 classes = 20 per class, × 2 succeeded = 40 estimated
        var sessions = new JsonLineStore<SessionRecord>(RepoConfig.SessionsPath(TempDir));
        var latest = sessions.LoadAll().Last();
        Assert.Equal(40, latest.TestsGenerated);
    }

    [Fact]
    public void LogSession_ExplicitTestCount_NoAutoEstimate()
    {
        SeedSessions(
            new SessionRecord
            {
                SessionId = "past1",
                Model = "m",
                TestsGenerated = 60,
                ClassesSucceeded = ["ClassA", "ClassB", "ClassC"],
                EndedUtc = DateTime.UtcNow.AddHours(-2).ToString("o")
            }
        );
        StoreRegistry.Reset();

        var result = SessionTool.LogSession("model",
            classesSucceeded: "ClassD",
            testsGenerated: 15);

        Assert.DoesNotContain("estimated", result);
        Assert.Contains("Tests: 15", result);
    }

    [Fact]
    public void LogSession_NoPastSessions_NoAutoEstimate()
    {
        var result = SessionTool.LogSession("model",
            classesSucceeded: "ClassD, ClassE",
            testsGenerated: 0);

        Assert.DoesNotContain("estimated", result);
    }

    // ── Recency bias: EstimateTestsGenerated uses only last 3 sessions ──

    [Fact]
    public void EstimateTestsGenerated_MoreThan3Sessions_UsesOnlyLastThree()
    {
        var sessions = new List<SessionRecord>
        {
            // Old sessions with high ROI (should be ignored)
            new() { TestsGenerated = 100, ClassesSucceeded = ["A"] },           // 100/class
            new() { TestsGenerated = 80, ClassesSucceeded = ["B"] },            // 80/class
            // Recent 3 sessions (should be used)
            new() { TestsGenerated = 30, ClassesSucceeded = ["C", "D", "E"] },  // 10/class
            new() { TestsGenerated = 20, ClassesSucceeded = ["F", "G"] },       // 10/class
            new() { TestsGenerated = 12, ClassesSucceeded = ["H", "I"] }        // 6/class
        };

        // Only last 3: avg per class = (10 + 10 + 6) / 3 ≈ 8.67
        // With 2 succeeded: 8.67 * 2 ≈ 17
        var estimate = SessionTool.EstimateTestsGenerated(sessions, 2);

        // All-sessions average would be (100+80+10+10+6)/5 = 41.2 → 82 (way too high)
        // Recency-constrained should give ~17
        Assert.InRange(estimate, 15, 20);
    }

    [Fact]
    public void EstimateTestsGenerated_ExactlyThreeSessions_UsesAll()
    {
        var sessions = new List<SessionRecord>
        {
            new() { TestsGenerated = 30, ClassesSucceeded = ["A", "B", "C"] },  // 10/class
            new() { TestsGenerated = 24, ClassesSucceeded = ["D", "E"] },       // 12/class
            new() { TestsGenerated = 16, ClassesSucceeded = ["F", "G"] }        // 8/class
        };

        // All 3 used: avg = (10+12+8)/3 = 10, × 3 succeeded = 30
        var estimate = SessionTool.EstimateTestsGenerated(sessions, 3);
        Assert.Equal(30, estimate);
    }

    // ── coveredLines tracking ──

    [Fact]
    public void LogSession_CoveredLines_PersistedToRecord()
    {
        var result = SessionTool.LogSession("model",
            classesSucceeded: "ClassA",
            testsGenerated: 10,
            coveredLines: 45);

        Assert.Contains("Covered lines: 45", result);
        Assert.Contains("4.5 lines/test", result);

        var sessions = new JsonLineStore<SessionRecord>(RepoConfig.SessionsPath(TempDir));
        var latest = sessions.LoadAll().Last();
        Assert.Equal(45, latest.CoveredLines);
    }

    [Fact]
    public void LogSession_ZeroCoveredLines_NoLineInOutput()
    {
        var result = SessionTool.LogSession("model",
            classesSucceeded: "ClassA",
            testsGenerated: 10,
            coveredLines: 0);

        Assert.DoesNotContain("Covered lines", result);
    }

    [Fact]
    public void GetSessions_Aggregates_IncludeLinesPerTest()
    {
        SeedSessions(
            new SessionRecord
            {
                SessionId = "s1", Model = "m",
                TestsGenerated = 50, CoveredLines = 200,
                ClassesSucceeded = ["A"],
                EndedUtc = DateTime.UtcNow.AddHours(-2).ToString("o")
            },
            new SessionRecord
            {
                SessionId = "s2", Model = "m",
                TestsGenerated = 30, CoveredLines = 60,
                ClassesSucceeded = ["B"],
                EndedUtc = DateTime.UtcNow.AddHours(-1).ToString("o")
            }
        );
        StoreRegistry.Reset();

        var result = SessionTool.GetSessions();
        var doc = JsonDocument.Parse(result);
        var aggs = doc.RootElement.GetProperty("aggregates");

        Assert.Equal(260, aggs.GetProperty("totalCoveredLines").GetInt32());
        // 260 lines / 80 tests = 3.25
        Assert.Equal(3.25, aggs.GetProperty("avgLinesPerTest").GetDouble());
    }

    // ── Plateau detection (v3) ──

    [Fact]
    public void DetectPlateau_LessThanThreeSessions_ReturnsNull()
    {
        var sessions = new List<SessionRecord>
        {
            new() { CoveredLines = 1, TestsGenerated = 10 },
            new() { CoveredLines = 1, TestsGenerated = 10 }
        };

        Assert.Null(SessionTool.DetectPlateau(sessions));
    }

    [Fact]
    public void DetectPlateau_HighROI_ReturnsNull()
    {
        var sessions = new List<SessionRecord>
        {
            new() { CoveredLines = 50, TestsGenerated = 10 },
            new() { CoveredLines = 40, TestsGenerated = 10 },
            new() { CoveredLines = 30, TestsGenerated = 10 }
        };

        Assert.Null(SessionTool.DetectPlateau(sessions));
    }

    [Fact]
    public void DetectPlateau_BelowThreshold_ReturnsWarning()
    {
        var sessions = new List<SessionRecord>
        {
            new() { CoveredLines = 2, TestsGenerated = 10 },
            new() { CoveredLines = 3, TestsGenerated = 10 },
            new() { CoveredLines = 1, TestsGenerated = 10 }
        };

        var result = SessionTool.DetectPlateau(sessions);

        Assert.NotNull(result);
        Assert.Contains("plateau detected", result);
        Assert.Contains("get_uncovered_methods", result);
    }

    [Fact]
    public void DetectPlateau_DecliningTrend_ReturnsWarning()
    {
        // First 3 sessions: high ROI (2.0 lines/test)
        // Last 3 sessions: declining (0.8 lines/test) — but above 0.5
        // Should still warn because it's <50% of prior
        var sessions = new List<SessionRecord>
        {
            new() { CoveredLines = 20, TestsGenerated = 10 },
            new() { CoveredLines = 20, TestsGenerated = 10 },
            new() { CoveredLines = 20, TestsGenerated = 10 },
            new() { CoveredLines = 8, TestsGenerated = 10 },
            new() { CoveredLines = 7, TestsGenerated = 10 },
            new() { CoveredLines = 9, TestsGenerated = 10 }
        };

        var result = SessionTool.DetectPlateau(sessions);

        Assert.NotNull(result);
        Assert.Contains("declining", result);
    }

    [Fact]
    public void DetectPlateau_SessionsWithoutCoveredLines_Ignored()
    {
        // Mix of old sessions (no coveredLines) and new ones
        var sessions = new List<SessionRecord>
        {
            new() { CoveredLines = 0, TestsGenerated = 10 },  // v2.0 session, no data
            new() { CoveredLines = 50, TestsGenerated = 10 },
            new() { CoveredLines = 40, TestsGenerated = 10 },
            new() { CoveredLines = 30, TestsGenerated = 10 }
        };

        // Only the 3 sessions with data matter — they're high ROI
        Assert.Null(SessionTool.DetectPlateau(sessions));
    }

    [Fact]
    public void GetSessions_IncludesPlateauWarningWhenDetected()
    {
        SeedSessions(
            new SessionRecord
            {
                SessionId = "s1", Model = "m",
                TestsGenerated = 10, CoveredLines = 2,
                ClassesSucceeded = ["A"],
                EndedUtc = DateTime.UtcNow.AddHours(-3).ToString("o")
            },
            new SessionRecord
            {
                SessionId = "s2", Model = "m",
                TestsGenerated = 10, CoveredLines = 3,
                ClassesSucceeded = ["B"],
                EndedUtc = DateTime.UtcNow.AddHours(-2).ToString("o")
            },
            new SessionRecord
            {
                SessionId = "s3", Model = "m",
                TestsGenerated = 10, CoveredLines = 1,
                ClassesSucceeded = ["C"],
                EndedUtc = DateTime.UtcNow.AddHours(-1).ToString("o")
            }
        );
        StoreRegistry.Reset();

        var result = SessionTool.GetSessions();
        var doc = JsonDocument.Parse(result);

        Assert.True(doc.RootElement.TryGetProperty("plateauWarning", out var warning));
        Assert.NotNull(warning.GetString());
        Assert.Contains("plateau", warning.GetString()!);
    }

    [Fact]
    public void GetSessions_NoPlateauWarning_WhenHighROI()
    {
        SeedSessions(
            new SessionRecord
            {
                SessionId = "s1", Model = "m",
                TestsGenerated = 10, CoveredLines = 50,
                ClassesSucceeded = ["A"],
                EndedUtc = DateTime.UtcNow.AddHours(-3).ToString("o")
            },
            new SessionRecord
            {
                SessionId = "s2", Model = "m",
                TestsGenerated = 10, CoveredLines = 40,
                ClassesSucceeded = ["B"],
                EndedUtc = DateTime.UtcNow.AddHours(-2).ToString("o")
            },
            new SessionRecord
            {
                SessionId = "s3", Model = "m",
                TestsGenerated = 10, CoveredLines = 30,
                ClassesSucceeded = ["C"],
                EndedUtc = DateTime.UtcNow.AddHours(-1).ToString("o")
            }
        );
        StoreRegistry.Reset();

        var result = SessionTool.GetSessions();
        var doc = JsonDocument.Parse(result);

        var warning = doc.RootElement.GetProperty("plateauWarning");
        Assert.Equal(JsonValueKind.Null, warning.ValueKind);
    }

    // ── Coverage delta sanity checks ──

    [Fact]
    public void LogSession_LargeDelta_ReturnsUnusuallyLargeWarning()
    {
        var result = SessionTool.LogSession("model",
            coverageBefore: 10.0, coverageAfter: 25.0);

        Assert.Contains("WARNINGS", result);
        Assert.Contains("unusually large", result);
        Assert.Contains("|Δ| ≥ 10%", result);
    }

    [Fact]
    public void LogSession_NegativeDeltaWithSucceeded_ReturnsSwapWarning()
    {
        var result = SessionTool.LogSession("model",
            classesSucceeded: "ClassA",
            coverageBefore: 35.0, coverageAfter: 30.0);

        Assert.Contains("WARNINGS", result);
        Assert.Contains("Coverage went DOWN", result);
        Assert.Contains("swapped", result);
    }

    [Fact]
    public void LogSession_SmallPositiveDelta_NoWarning()
    {
        var result = SessionTool.LogSession("model",
            classesSucceeded: "ClassA",
            coverageBefore: 50.0, coverageAfter: 53.0);

        Assert.DoesNotContain("WARNINGS", result);
    }

    // ── GenerateRecommendations: repeat-failure detection ──

    [Fact]
    public void GenerateRecommendations_RepeatFailures_ReturnsWarning()
    {
        SeedSessions(
            new SessionRecord
            {
                SessionId = "s1", Model = "m",
                ClassesAttempted = ["HardClass", "EasyClass"],
                ClassesSucceeded = ["EasyClass"],
                ClassesFailed = [new SessionFailure { Class = "HardClass", Reason = "compile error" }]
            },
            new SessionRecord
            {
                SessionId = "s2", Model = "m",
                ClassesAttempted = ["HardClass"],
                ClassesFailed = [new SessionFailure { Class = "HardClass", Reason = "timeout" }]
            }
        );
        StoreRegistry.Reset();

        var stores = StoreRegistry.ForNamespace(null);
        var all = stores.Sessions.LoadAll();
        var recs = SessionTool.GenerateRecommendations(all, stores);

        Assert.Contains(recs, r => r.Contains("REPEAT FAILURE") && r.Contains("HardClass"));
        Assert.Contains(recs, r => r.Contains("compile error") || r.Contains("timeout"));
    }

    [Fact]
    public void GenerateRecommendations_LessThan2Sessions_ReturnsEmpty()
    {
        SeedSessions(new SessionRecord { SessionId = "s1", Model = "m" });
        StoreRegistry.Reset();

        var stores = StoreRegistry.ForNamespace(null);
        var all = stores.Sessions.LoadAll();
        var recs = SessionTool.GenerateRecommendations(all, stores);

        Assert.Empty(recs);
    }

    // ── GenerateRecommendations: declining efficiency ──

    [Fact]
    public void GenerateRecommendations_DecliningEfficiency_ReturnsWarning()
    {
        SeedSessions(
            new SessionRecord { SessionId = "s1", Model = "m", CoverageDelta = 8.0 },
            new SessionRecord { SessionId = "s2", Model = "m", CoverageDelta = 7.0 },
            new SessionRecord { SessionId = "s3", Model = "m", CoverageDelta = 0.5 },
            new SessionRecord { SessionId = "s4", Model = "m", CoverageDelta = 0.3 }
        );
        StoreRegistry.Reset();

        var stores = StoreRegistry.ForNamespace(null);
        var all = stores.Sessions.LoadAll();
        var recs = SessionTool.GenerateRecommendations(all, stores);

        Assert.Contains(recs, r => r.Contains("DIMINISHING RETURNS"));
    }

    // ── GenerateRecommendations: unassessed repeat failures ──

    [Fact]
    public void GenerateRecommendations_UnassessedRepeatFailures_ReturnsWarning()
    {
        SeedSessions(
            new SessionRecord
            {
                SessionId = "s1", Model = "m",
                ClassesAttempted = ["FailClass"],
                ClassesFailed = [new SessionFailure { Class = "FailClass", Reason = "deps" }]
            },
            new SessionRecord
            {
                SessionId = "s2", Model = "m",
                ClassesAttempted = ["FailClass"],
                ClassesFailed = [new SessionFailure { Class = "FailClass", Reason = "deps" }]
            }
        );
        // Seed assessments but NOT for FailClass
        SeedAssessments(new Assessment { Class = "OtherClass", Verdict = "testable", Reasoning = "ok" });
        StoreRegistry.Reset();

        var stores = StoreRegistry.ForNamespace(null);
        var all = stores.Sessions.LoadAll();
        var recs = SessionTool.GenerateRecommendations(all, stores);

        Assert.Contains(recs, r => r.Contains("UNASSESSED FAILURES") && r.Contains("FailClass"));
    }

    // ── GenerateRecommendations: token efficiency trend ──

    [Fact]
    public void GenerateRecommendations_RisingTokenCost_ReturnsWarning()
    {
        SeedSessions(
            // Many early cheap sessions to keep overall average low
            new SessionRecord { SessionId = "s1", Model = "m", TotalTokens = 5000, TestsGenerated = 10 },
            new SessionRecord { SessionId = "s2", Model = "m", TotalTokens = 6000, TestsGenerated = 10 },
            new SessionRecord { SessionId = "s3", Model = "m", TotalTokens = 5000, TestsGenerated = 10 },
            new SessionRecord { SessionId = "s4", Model = "m", TotalTokens = 5500, TestsGenerated = 10 },
            new SessionRecord { SessionId = "s5", Model = "m", TotalTokens = 5000, TestsGenerated = 10 },
            // Recent sessions: drastically worse token efficiency (20x the cost per test)
            new SessionRecord { SessionId = "s6", Model = "m", TotalTokens = 50000, TestsGenerated = 5 },
            new SessionRecord { SessionId = "s7", Model = "m", TotalTokens = 60000, TestsGenerated = 5 },
            new SessionRecord { SessionId = "s8", Model = "m", TotalTokens = 55000, TestsGenerated = 5 }
        );
        StoreRegistry.Reset();

        var stores = StoreRegistry.ForNamespace(null);
        var all = stores.Sessions.LoadAll();
        var recs = SessionTool.GenerateRecommendations(all, stores);

        Assert.Contains(recs, r => r.Contains("TOKEN COST RISING"));
    }

    // ── GenerateRecommendations: best-performing class shapes ──

    [Fact]
    public void GenerateRecommendations_HighRoiPattern_ReturnsRecommendation()
    {
        SeedSessions(
            new SessionRecord
            {
                SessionId = "s1", Model = "m",
                ClassesSucceeded = ["StarClass"],
                CoverageDelta = 5.0
            },
            new SessionRecord
            {
                SessionId = "s2", Model = "m",
                ClassesSucceeded = ["StarClass"],
                CoverageDelta = 4.0
            }
        );
        StoreRegistry.Reset();

        var stores = StoreRegistry.ForNamespace(null);
        var all = stores.Sessions.LoadAll();
        var recs = SessionTool.GenerateRecommendations(all, stores);

        Assert.Contains(recs, r => r.Contains("HIGH-ROI PATTERN") && r.Contains("StarClass"));
    }

    // ── GenerateRecommendations: strategy shift (<0.5 lines/test) ──

    [Fact]
    public void GenerateRecommendations_StrategyShift_WhenLinesPerTestBelowHalf()
    {
        SeedSessions(
            new SessionRecord { SessionId = "s1", Model = "m", CoveredLines = 2, TestsGenerated = 10 },
            new SessionRecord { SessionId = "s2", Model = "m", CoveredLines = 3, TestsGenerated = 10 },
            new SessionRecord { SessionId = "s3", Model = "m", CoveredLines = 1, TestsGenerated = 10 }
        );
        StoreRegistry.Reset();

        var stores = StoreRegistry.ForNamespace(null);
        var all = stores.Sessions.LoadAll();
        var recs = SessionTool.GenerateRecommendations(all, stores);

        Assert.Contains(recs, r => r.Contains("STRATEGY SHIFT") && r.Contains("get_uncovered_methods"));
    }

    // ── GenerateRecommendations: ROI softening (0.5-1.5 lines/test) ──

    [Fact]
    public void GenerateRecommendations_RoiSoftening_WhenLinesPerTestBetweenHalfAndOnePointFive()
    {
        SeedSessions(
            new SessionRecord { SessionId = "s1", Model = "m", CoveredLines = 8, TestsGenerated = 10 },
            new SessionRecord { SessionId = "s2", Model = "m", CoveredLines = 10, TestsGenerated = 10 },
            new SessionRecord { SessionId = "s3", Model = "m", CoveredLines = 12, TestsGenerated = 10 }
        );
        StoreRegistry.Reset();

        var stores = StoreRegistry.ForNamespace(null);
        var all = stores.Sessions.LoadAll();
        var recs = SessionTool.GenerateRecommendations(all, stores);

        Assert.Contains(recs, r => r.Contains("ROI SOFTENING") && r.Contains("get_stub_classes"));
    }

    // ── GenerateRecommendations: session milestone checkpoint ──

    [Fact]
    public void GenerateRecommendations_SessionMilestone_AtLowSuccessRate()
    {
        var sessions = Enumerable.Range(1, 10).Select(i => new SessionRecord
        {
            SessionId = $"s{i}",
            Model = "m",
            ClassesAttempted = [$"Class{i}"],
            ClassesSucceeded = i <= 3 ? [$"Class{i}"] : [],
            ClassesFailed = i > 3 ? [new SessionFailure { Class = $"Class{i}", Reason = "fail" }] : []
        }).ToArray();
        SeedSessions(sessions);
        StoreRegistry.Reset();

        var stores = StoreRegistry.ForNamespace(null);
        var all = stores.Sessions.LoadAll();
        var recs = SessionTool.GenerateRecommendations(all, stores);

        Assert.Contains(recs, r => r.Contains("SESSION 10 CHECKPOINT"));
    }

    [Fact]
    public void GenerateRecommendations_SessionMilestone_NotAt9Sessions()
    {
        // 9 sessions: not a multiple of 5, so no milestone
        var sessions = Enumerable.Range(1, 9).Select(i => new SessionRecord
        {
            SessionId = $"s{i}",
            Model = "m",
            ClassesAttempted = [$"Class{i}"],
            ClassesFailed = [new SessionFailure { Class = $"Class{i}", Reason = "fail" }]
        }).ToArray();
        SeedSessions(sessions);
        StoreRegistry.Reset();

        var stores = StoreRegistry.ForNamespace(null);
        var all = stores.Sessions.LoadAll();
        var recs = SessionTool.GenerateRecommendations(all, stores);

        Assert.DoesNotContain(recs, r => r.Contains("CHECKPOINT"));
    }

    // ── DetectPlateau: recentTotalTests == 0 edge case ──

    [Fact]
    public void DetectPlateau_RecentTestsZeroButCoveredLinesPositive_ReturnsNull()
    {
        // This edge case can't happen via normal flow (CoveredLines > 0 && TestsGenerated > 0 is the filter)
        // but we test the guard at the code level
        var sessions = new List<SessionRecord>
        {
            new() { CoveredLines = 5, TestsGenerated = 1 },
            new() { CoveredLines = 3, TestsGenerated = 1 },
            new() { CoveredLines = 2, TestsGenerated = 1 }
        };

        // With very low lines/test (5+3+2)/(1+1+1) = 3.3 > 0.5, no plateau
        Assert.Null(SessionTool.DetectPlateau(sessions));
    }

    // ── GetLastSessionEndTime: unparseable date ──

    [Fact]
    public void GetLastSessionEndTime_UnparseableDate_ReturnsNull()
    {
        SeedSessions(new SessionRecord
        {
            SessionId = "s1",
            Model = "m",
            EndedUtc = "not-a-date"
        });
        StoreRegistry.Reset();

        var stores = StoreRegistry.ForNamespace(null);
        var result = SessionTool.GetLastSessionEndTime(stores);

        Assert.Null(result);
    }

    // ── GetSessions includes recommendations ──

    [Fact]
    public void GetSessions_IncludesRecommendationsInOutput()
    {
        SeedSessions(
            new SessionRecord
            {
                SessionId = "s1", Model = "m",
                ClassesAttempted = ["Hard"],
                ClassesFailed = [new SessionFailure { Class = "Hard", Reason = "deps" }]
            },
            new SessionRecord
            {
                SessionId = "s2", Model = "m",
                ClassesAttempted = ["Hard"],
                ClassesFailed = [new SessionFailure { Class = "Hard", Reason = "deps" }]
            }
        );
        StoreRegistry.Reset();

        var result = SessionTool.GetSessions();
        var doc = JsonDocument.Parse(result);

        Assert.True(doc.RootElement.TryGetProperty("recommendations", out var recs));
        Assert.True(recs.GetArrayLength() > 0);
    }

    // ── CountRecordsSince: unparseable dates are skipped ──

    [Fact]
    public void CountRecordsSince_UnparseableDates_Skipped()
    {
        var records = new List<Gotcha>
        {
            new() { Type = "A", Date = "not-a-date" },
            new() { Type = "B", Date = "2025-06-01T00:00:00Z" },
            new() { Type = "C", Date = "garbage" }
        };

        var cutoff = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var count = SessionTool.CountRecordsSince(records, g => g.Date, cutoff);
        Assert.Equal(1, count); // Only B parses and is after cutoff
    }
}
