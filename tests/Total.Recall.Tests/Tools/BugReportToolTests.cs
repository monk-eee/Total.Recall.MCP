using System.Text.Json;
using Total.Recall.Infrastructure;
using Total.Recall.Models;
using Total.Recall.Tools;

namespace Total.Recall.Tests.Tools;

[Collection("ToolTests")]
public sealed class BugReportToolTests : ToolTestBase
{
    // ── ReportBug ──

    [Fact]
    public void ReportBug_HappyPath_AppendsRecordWithGeneratedId()
    {
        var result = BugReportTool.ReportBug(
            className: "UserService",
            severity: "high",
            description: "UpdateEmail throws NRE when email is null instead of ArgumentNullException",
            methodName: "UpdateEmail");

        Assert.Contains("\"ok\": true", result);
        Assert.Contains("\"id\": \"bug-", result);

        var store = new JsonLineStore<BugReport>(RepoConfig.BugsPath(TempDir));
        var all = store.LoadAll();
        Assert.Single(all);
        Assert.Equal("UserService", all[0].Class);
        Assert.Equal("UpdateEmail", all[0].Method);
        Assert.Equal("high", all[0].Severity);
        Assert.Equal("open", all[0].Status);
        Assert.Equal(1, all[0].SchemaVersion);
        Assert.StartsWith("bug-", all[0].Id);
        Assert.Equal(12, all[0].Id.Length - "bug-".Length);
    }

    [Fact]
    public void ReportBug_MissingClassName_ReturnsError()
    {
        var result = BugReportTool.ReportBug("", "high", "broken");
        Assert.StartsWith("ERROR in ReportBug", result);
        Assert.Contains("className is required", result);
    }

    [Fact]
    public void ReportBug_MissingDescription_ReturnsError()
    {
        var result = BugReportTool.ReportBug("Foo", "high", "");
        Assert.StartsWith("ERROR in ReportBug", result);
        Assert.Contains("description is required", result);
    }

    [Fact]
    public void ReportBug_InvalidSeverity_ReturnsError()
    {
        var result = BugReportTool.ReportBug("Foo", "showstopper", "broken");
        Assert.StartsWith("ERROR in ReportBug", result);
        Assert.Contains("severity must be one of", result);
    }

    [Theory]
    [InlineData("low")]
    [InlineData("medium")]
    [InlineData("high")]
    [InlineData("critical")]
    [InlineData("CRITICAL")] // case-insensitive
    public void ReportBug_AllowedSeverities_AreAccepted(string severity)
    {
        var result = BugReportTool.ReportBug("Foo", severity, "broken");
        Assert.Contains("\"ok\": true", result);
    }

    [Fact]
    public void ReportBug_CapturesSessionId()
    {
        BugReportTool.ReportBug("Foo", "low", "x");

        var record = new JsonLineStore<BugReport>(RepoConfig.BugsPath(TempDir)).LoadAll().Single();
        Assert.Equal(Telemetry.SessionId, record.SessionId);
    }

    [Fact]
    public void ReportBug_GeneratesUniqueIds()
    {
        BugReportTool.ReportBug("Foo", "low", "first");
        BugReportTool.ReportBug("Foo", "low", "second");

        var all = new JsonLineStore<BugReport>(RepoConfig.BugsPath(TempDir)).LoadAll();
        Assert.Equal(2, all.Count);
        Assert.NotEqual(all[0].Id, all[1].Id);
    }

    // ── GetBugs ──

    [Fact]
    public void GetBugs_NoData_ReturnsFriendlyMessage()
    {
        var result = BugReportTool.GetBugs();
        Assert.Contains("No bugs recorded yet", result);
    }

    [Fact]
    public void GetBugs_DefaultsToOpenOnly()
    {
        SeedBugs(
            MakeBug("bug-1", "A", "open", "high"),
            MakeBug("bug-2", "B", "fixed", "high"));

        var result = BugReportTool.GetBugs();
        Assert.Contains("bug-1", result);
        Assert.DoesNotContain("bug-2", result);
    }

    [Fact]
    public void GetBugs_StatusAll_IncludesClosed()
    {
        SeedBugs(
            MakeBug("bug-1", "A", "open", "high"),
            MakeBug("bug-2", "B", "fixed", "high"));

        var result = BugReportTool.GetBugs(status: "all");
        Assert.Contains("bug-1", result);
        Assert.Contains("bug-2", result);
    }

    [Fact]
    public void GetBugs_FiltersByClassNamePartial()
    {
        SeedBugs(
            MakeBug("bug-1", "UserService", "open", "high"),
            MakeBug("bug-2", "OrderService", "open", "high"));

        var result = BugReportTool.GetBugs(className: "User");
        Assert.Contains("bug-1", result);
        Assert.DoesNotContain("bug-2", result);
    }

    [Fact]
    public void GetBugs_FiltersBySeverity()
    {
        SeedBugs(
            MakeBug("bug-1", "A", "open", "critical"),
            MakeBug("bug-2", "A", "open", "low"));

        var result = BugReportTool.GetBugs(severity: "critical");
        Assert.Contains("bug-1", result);
        Assert.DoesNotContain("bug-2", result);
    }

    [Fact]
    public void GetBugs_OrdersBySeverityCriticalFirst()
    {
        SeedBugs(
            MakeBug("bug-low", "A", "open", "low"),
            MakeBug("bug-crit", "A", "open", "critical"),
            MakeBug("bug-med", "A", "open", "medium"));

        var result = BugReportTool.GetBugs();
        var iCrit = result.IndexOf("bug-crit", StringComparison.Ordinal);
        var iMed = result.IndexOf("bug-med", StringComparison.Ordinal);
        var iLow = result.IndexOf("bug-low", StringComparison.Ordinal);
        Assert.True(iCrit < iMed && iMed < iLow, $"order was crit={iCrit} med={iMed} low={iLow}");
    }

    [Fact]
    public void GetBugs_LatestRecordPerIdWins()
    {
        // Same id appears twice — second record (fixed) is the truth.
        SeedBugs(
            MakeBug("bug-1", "A", "open", "high"),
            MakeBug("bug-1", "A", "fixed", "high"));

        // Default status=open => should be excluded.
        var open = BugReportTool.GetBugs();
        Assert.Contains("No bugs matched", open);

        var fixedOnly = BugReportTool.GetBugs(status: "fixed");
        Assert.Contains("bug-1", fixedOnly);
    }

    // ── UpdateBugStatus ──

    [Fact]
    public void UpdateBugStatus_HappyPath_AppendsTransition()
    {
        var reportResult = BugReportTool.ReportBug("UserService", "high", "broken");
        var id = ExtractId(reportResult);

        var update = BugReportTool.UpdateBugStatus(id, "fixed", "Pinned by regression test in #1234");

        Assert.Contains("\"ok\": true", update);
        Assert.Contains("\"previousStatus\": \"open\"", update);
        Assert.Contains("\"newStatus\": \"fixed\"", update);

        var all = new JsonLineStore<BugReport>(RepoConfig.BugsPath(TempDir)).LoadAll();
        Assert.Equal(2, all.Count); // append-only: original + transition
        Assert.Equal("open", all[0].Status);
        Assert.Equal("fixed", all[1].Status);
        Assert.Equal(id, all[1].Id);
        Assert.Equal("Pinned by regression test in #1234", all[1].StatusNotes);
    }

    [Fact]
    public void UpdateBugStatus_UnknownId_ReturnsError()
    {
        SeedBugs(MakeBug("bug-real", "A", "open", "high"));

        var result = BugReportTool.UpdateBugStatus("bug-ghost", "fixed");
        Assert.StartsWith("ERROR in UpdateBugStatus", result);
        Assert.Contains("not found", result);
    }

    [Fact]
    public void UpdateBugStatus_InvalidStatus_ReturnsError()
    {
        SeedBugs(MakeBug("bug-1", "A", "open", "high"));

        var result = BugReportTool.UpdateBugStatus("bug-1", "completed");
        Assert.StartsWith("ERROR in UpdateBugStatus", result);
        Assert.Contains("status must be one of", result);
    }

    [Fact]
    public void UpdateBugStatus_NoBugsStore_ReturnsError()
    {
        var result = BugReportTool.UpdateBugStatus("bug-anything", "fixed");
        Assert.StartsWith("ERROR in UpdateBugStatus", result);
    }

    [Fact]
    public void UpdateBugStatus_PreservesOriginalCreatedAtAndDescription()
    {
        SeedBugs(MakeBug("bug-1", "A", "open", "high", description: "original desc"));

        BugReportTool.UpdateBugStatus("bug-1", "triaged", "looking into it");

        var all = new JsonLineStore<BugReport>(RepoConfig.BugsPath(TempDir)).LoadAll();
        var transition = all.Last();
        Assert.Equal("original desc", transition.Description);
        Assert.Equal(all[0].CreatedAt, transition.CreatedAt);
        Assert.NotEqual(all[0].UpdatedAt, transition.UpdatedAt);
    }

    // ── ContextTool integration ──

    [Fact]
    public void GetContext_Standard_IncludesOpenBugsForType()
    {
        SeedTypeRegistry(new TypeRecord { Name = "UserService", Namespace = "App.Users" });
        SeedBugs(
            MakeBug("bug-1", "UserService", "open", "high", description: "null email crash"),
            MakeBug("bug-2", "UserService", "fixed", "low"));

        var result = ContextTool.GetContext("UserService");

        Assert.Contains("openBugs", result);
        Assert.Contains("null email crash", result);
        Assert.Contains("bug-1", result);
        Assert.DoesNotContain("bug-2", result); // fixed → excluded
    }

    // ── Helpers ──

    private static BugReport MakeBug(
        string id,
        string className,
        string status,
        string severity,
        string description = "broken")
    {
        var ts = DateTime.UtcNow.ToString("O");
        return new BugReport
        {
            SchemaVersion = 1,
            Id = id,
            Class = className,
            Severity = severity,
            Description = description,
            Status = status,
            CreatedAt = ts,
            UpdatedAt = ts
        };
    }

    private static string ExtractId(string reportJsonResult)
    {
        using var doc = JsonDocument.Parse(reportJsonResult);
        return doc.RootElement.GetProperty("id").GetString()!;
    }
}
