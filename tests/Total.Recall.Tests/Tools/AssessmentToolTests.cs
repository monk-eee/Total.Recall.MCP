using System.Text.Json;
using Total.Recall.Infrastructure;
using Total.Recall.Models;
using Total.Recall.Tools;

namespace Total.Recall.Tests.Tools;

[Collection("ToolTests")]
public sealed class AssessmentToolTests : ToolTestBase
{
    public AssessmentToolTests() : base(saveNamespace: true) { }

    [Fact]
    public void AddAssessment_Testable_ReturnsConfirmation()
    {
        var result = AssessmentTool.AddAssessment("MyClass", "testable", "No dependencies");

        Assert.Contains("Recorded assessment", result);
        Assert.Contains("MyClass", result);
        Assert.Contains("testable", result);
    }

    [Fact]
    public void AddAssessment_WithDependencies_ParsesCommaSeparatedList()
    {
        AssessmentTool.AddAssessment("CoupledClass", "coupled", "Heavy deps", "ILogger,IRepo,IConfig");

        var store = new JsonLineStore<Assessment>(RepoConfig.AssessmentsPath(TempDir));
        var all = store.LoadAll();
        Assert.Single(all);
        Assert.Equal(3, all[0].Dependencies.Count);
        Assert.Contains("ILogger", all[0].Dependencies);
        Assert.Contains("IRepo", all[0].Dependencies);
        Assert.Contains("IConfig", all[0].Dependencies);
    }

    [Fact]
    public void AddAssessment_WithCluster_SetsCluster()
    {
        AssessmentTool.AddAssessment("Widget", "testable", "OK", cluster: "widget-cluster");

        var store = new JsonLineStore<Assessment>(RepoConfig.AssessmentsPath(TempDir));
        var record = store.LoadAll().Single();
        Assert.Equal("widget-cluster", record.Cluster);
    }

    [Fact]
    public void AddAssessment_NullDependencies_DefaultsToEmptyList()
    {
        AssessmentTool.AddAssessment("Simple", "testable", "No deps");

        var store = new JsonLineStore<Assessment>(RepoConfig.AssessmentsPath(TempDir));
        var record = store.LoadAll().Single();
        Assert.Empty(record.Dependencies);
    }

    [Fact]
    public void AddAssessment_SetsDateToToday()
    {
        AssessmentTool.AddAssessment("X", "skip", "Not worth it");

        var store = new JsonLineStore<Assessment>(RepoConfig.AssessmentsPath(TempDir));
        var record = store.LoadAll().Single();
        Assert.Equal(DateTime.UtcNow.ToString("yyyy-MM-dd"), record.Date);
    }

    [Fact]
    public void AddAssessment_NormalizesVerdictToLowercase()
    {
        AssessmentTool.AddAssessment("X", "TESTABLE", "OK");

        var store = new JsonLineStore<Assessment>(RepoConfig.AssessmentsPath(TempDir));
        var record = store.LoadAll().Single();
        Assert.Equal("testable", record.Verdict);
    }

    [Fact]
    public void GetAssessments_NoData_ReturnsMessage()
    {
        var result = AssessmentTool.GetAssessments();

        Assert.Contains("No assessments recorded", result);
    }

    [Fact]
    public void GetAssessments_AllAssessments_ReturnsAll()
    {
        SeedAssessments(
            new Assessment { Class = "A", Verdict = "testable", Reasoning = "OK", Date = "2025-01-01" },
            new Assessment { Class = "B", Verdict = "coupled", Reasoning = "Heavy", Date = "2025-01-01" }
        );

        var result = AssessmentTool.GetAssessments();

        Assert.Contains("\"A\"", result);
        Assert.Contains("\"B\"", result);
    }

    [Fact]
    public void GetAssessments_FilterByClassName_ReturnsMatching()
    {
        SeedAssessments(
            new Assessment { Class = "AuditEntry", Verdict = "testable", Reasoning = "OK", Date = "2025-01-01" },
            new Assessment { Class = "Parser", Verdict = "coupled", Reasoning = "Heavy", Date = "2025-01-01" }
        );

        var result = AssessmentTool.GetAssessments(className: "Audit");

        Assert.Contains("AuditEntry", result);
        Assert.DoesNotContain("Parser", result);
    }

    [Fact]
    public void GetAssessments_FilterByVerdict_ReturnsMatching()
    {
        SeedAssessments(
            new Assessment { Class = "A", Verdict = "testable", Reasoning = "Good", Date = "2025-01-01" },
            new Assessment { Class = "B", Verdict = "coupled", Reasoning = "Bad", Date = "2025-01-01" },
            new Assessment { Class = "C", Verdict = "testable", Reasoning = "Also good", Date = "2025-01-01" }
        );

        var result = AssessmentTool.GetAssessments(verdict: "testable");
        var doc = JsonDocument.Parse(result);
        var arr = doc.RootElement.EnumerateArray().ToList();

        Assert.Equal(2, arr.Count);
        Assert.All(arr, e => Assert.Equal("testable", e.GetProperty("verdict").GetString()));
    }

    [Fact]
    public void GetAssessments_Deduplication_LatestWinsPerClass()
    {
        SeedAssessments(
            new Assessment { Class = "X", Verdict = "deferred", Reasoning = "First try", Date = "2025-01-01" },
            new Assessment { Class = "X", Verdict = "testable", Reasoning = "Retry OK", Date = "2025-01-02" }
        );

        var result = AssessmentTool.GetAssessments(className: "X");
        var doc = JsonDocument.Parse(result);
        var arr = doc.RootElement.EnumerateArray().ToList();

        Assert.Single(arr);
        Assert.Equal("testable", arr[0].GetProperty("verdict").GetString());
        Assert.Equal("Retry OK", arr[0].GetProperty("reasoning").GetString());
    }

    [Fact]
    public void GetAssessments_NoMatch_ReturnsNotFoundMessage()
    {
        SeedAssessments(
            new Assessment { Class = "Foo", Verdict = "testable", Reasoning = "OK", Date = "2025-01-01" }
        );

        var result = AssessmentTool.GetAssessments(className: "NonExistent");

        Assert.Contains("No assessments found", result);
    }

    [Fact]
    public void AddThenGetAssessments_RoundTrips()
    {
        AssessmentTool.AddAssessment("Widget", "coupled", "Needs IRepo", "IRepo,ILogger", "widget-cluster");

        var result = AssessmentTool.GetAssessments(className: "Widget");

        Assert.Contains("Widget", result);
        Assert.Contains("coupled", result);
        Assert.Contains("widget-cluster", result);
    }

    [Fact]
    public void AddAssessment_IncrementsMetrics()
    {
        Metrics.Reset();

        AssessmentTool.AddAssessment("X", "testable", "OK");

        Assert.Equal(1, Metrics.Get(Metrics.ToolAddAssessment));
    }

    [Fact]
    public void GetAssessments_IncrementsMetrics()
    {
        Metrics.Reset();

        AssessmentTool.GetAssessments();

        Assert.Equal(1, Metrics.Get(Metrics.ToolGetAssessments));
    }

    // ── Error path coverage ──

    [Fact]
    public void AddAssessment_InvalidNamespace_ReturnsError()
    {
        var result = AssessmentTool.AddAssessment("X", "testable", "OK", ns: "\0");

        Assert.StartsWith("ERROR in AddAssessment", result);
    }

    [Fact]
    public void GetAssessments_InvalidNamespace_ReturnsError()
    {
        var result = AssessmentTool.GetAssessments(ns: "\0");

        Assert.StartsWith("ERROR in GetAssessments", result);
    }
}
