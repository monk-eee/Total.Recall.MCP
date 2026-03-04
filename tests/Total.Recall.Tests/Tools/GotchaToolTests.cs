using Total.Recall.Infrastructure;
using Total.Recall.Models;
using Total.Recall.Tools;

namespace Total.Recall.Tests.Tools;

[Collection("ToolTests")]
public sealed class GotchaToolTests : ToolTestBase
{

    [Fact]
    public void GetGotchas_NoData_ReturnsNotFoundMessage()
    {
        var result = GotchaTool.GetGotchas("Anything");

        Assert.Contains("No gotchas database found", result);
    }

    [Fact]
    public void GetGotchas_MatchingType_ReturnsGotchas()
    {
        SeedGotchas(
            new Gotcha { Type = "AuditEntry", Category = "constructor", Description = "Needs ILogger", Date = "2025-01-01" },
            new Gotcha { Type = "AuditEntry", Category = "enum", Description = "StatusEnum has hidden value", Date = "2025-01-02" }
        );

        var result = GotchaTool.GetGotchas("AuditEntry");

        Assert.Contains("Needs ILogger", result);
        Assert.Contains("StatusEnum has hidden value", result);
    }

    [Fact]
    public void GetGotchas_PartialMatch_ReturnsContaining()
    {
        SeedGotchas(
            new Gotcha { Type = "StringExtensions", Category = "bug", Description = "null check", Date = "2025-01-01" },
            new Gotcha { Type = "DateExtensions", Category = "bug", Description = "timezone", Date = "2025-01-01" }
        );

        var result = GotchaTool.GetGotchas("Extensions");

        Assert.Contains("null check", result);
        Assert.Contains("timezone", result);
    }

    [Fact]
    public void GetGotchas_NoMatch_ReturnsCleanMessage()
    {
        SeedGotchas(
            new Gotcha { Type = "Foo", Category = "bug", Description = "x", Date = "2025-01-01" }
        );

        var result = GotchaTool.GetGotchas("NonExistent");

        Assert.Contains("Looks clean!", result);
    }

    [Fact]
    public void AddGotcha_AppendsToFile()
    {
        var result = GotchaTool.AddGotcha("MyClass", "constructor", "Requires ILogger<T>");

        Assert.Contains("Added gotcha", result);
        Assert.Contains("MyClass", result);

        // Verify it was persisted
        var store = new JsonLineStore<Gotcha>(RepoConfig.GotchasPath(TempDir));
        var all = store.LoadAll();
        Assert.Single(all);
        Assert.Equal("MyClass", all[0].Type);
        Assert.Equal("constructor", all[0].Category);
        Assert.Equal("Requires ILogger<T>", all[0].Description);
    }

    [Fact]
    public void AddGotcha_MultipleAppends_AccumulateRecords()
    {
        GotchaTool.AddGotcha("A", "bug", "first");
        GotchaTool.AddGotcha("B", "enum", "second");
        GotchaTool.AddGotcha("A", "mock", "third");

        var store = new JsonLineStore<Gotcha>(RepoConfig.GotchasPath(TempDir));
        var all = store.LoadAll();
        Assert.Equal(3, all.Count);
    }

    [Fact]
    public void AddGotcha_SetsDateToToday()
    {
        GotchaTool.AddGotcha("X", "bug", "test");

        var store = new JsonLineStore<Gotcha>(RepoConfig.GotchasPath(TempDir));
        var record = store.LoadAll().Single();
        Assert.Equal(DateTime.UtcNow.ToString("yyyy-MM-dd"), record.Date);
    }

    [Fact]
    public void AddGotcha_ThenGetGotchas_RoundTrips()
    {
        GotchaTool.AddGotcha("Widget", "property", "HasInit is unreliable");

        var result = GotchaTool.GetGotchas("Widget");

        Assert.Contains("HasInit is unreliable", result);
    }

    // ── Error path coverage ──

    [Fact]
    public void GetGotchas_InvalidNamespace_ReturnsError()
    {
        var result = GotchaTool.GetGotchas("Any", ns: "\0");

        Assert.StartsWith("ERROR in GetGotchas", result);
    }

    [Fact]
    public void AddGotcha_InvalidNamespace_ReturnsError()
    {
        var result = GotchaTool.AddGotcha("Any", "bug", "test", ns: "\0");

        Assert.StartsWith("ERROR in AddGotcha", result);
    }

    // ── Assessment downgrade hint ──

    [Fact]
    public void AddGotcha_FourthGotcha_TestableAssessment_ReturnsDowngradeHint()
    {
        // Seed 3 existing gotchas
        SeedGotchas(
            new Gotcha { Type = "HardClass", Category = "mock", Description = "g1", Date = "2025-01-01" },
            new Gotcha { Type = "HardClass", Category = "enum", Description = "g2", Date = "2025-01-02" },
            new Gotcha { Type = "HardClass", Category = "bug", Description = "g3", Date = "2025-01-03" }
        );
        // Seed assessment as "testable"
        SeedAssessments(new Assessment { Class = "HardClass", Verdict = "testable", Reasoning = "seemed ok" });
        StoreRegistry.Reset();

        // Add the 4th gotcha — should trigger downgrade hint
        var result = GotchaTool.AddGotcha("HardClass", "constructor", "4th issue");

        Assert.Contains("DOWNGRADE HINT", result);
        Assert.Contains("4 gotchas", result);
        Assert.Contains("AddAssessment", result);
    }

    [Fact]
    public void AddGotcha_FourthGotcha_CoupledAssessment_NoDowngradeHint()
    {
        SeedGotchas(
            new Gotcha { Type = "CoupledClass", Category = "mock", Description = "g1", Date = "2025-01-01" },
            new Gotcha { Type = "CoupledClass", Category = "enum", Description = "g2", Date = "2025-01-02" },
            new Gotcha { Type = "CoupledClass", Category = "bug", Description = "g3", Date = "2025-01-03" }
        );
        SeedAssessments(new Assessment { Class = "CoupledClass", Verdict = "coupled", Reasoning = "too many deps" });
        StoreRegistry.Reset();

        var result = GotchaTool.AddGotcha("CoupledClass", "constructor", "4th issue");

        Assert.DoesNotContain("DOWNGRADE HINT", result);
    }

    [Fact]
    public void AddGotcha_FourthGotcha_NoAssessment_NoDowngradeHint()
    {
        SeedGotchas(
            new Gotcha { Type = "NewClass", Category = "mock", Description = "g1", Date = "2025-01-01" },
            new Gotcha { Type = "NewClass", Category = "enum", Description = "g2", Date = "2025-01-02" },
            new Gotcha { Type = "NewClass", Category = "bug", Description = "g3", Date = "2025-01-03" }
        );
        // No assessments seeded at all
        StoreRegistry.Reset();

        var result = GotchaTool.AddGotcha("NewClass", "constructor", "4th issue");

        Assert.DoesNotContain("DOWNGRADE HINT", result);
    }

    [Fact]
    public void AddGotcha_ThreeOrFewerGotchas_NoDowngradeHint()
    {
        SeedGotchas(
            new Gotcha { Type = "FineClass", Category = "mock", Description = "g1", Date = "2025-01-01" },
            new Gotcha { Type = "FineClass", Category = "enum", Description = "g2", Date = "2025-01-02" }
        );
        SeedAssessments(new Assessment { Class = "FineClass", Verdict = "testable", Reasoning = "ok" });
        StoreRegistry.Reset();

        // Add 3rd gotcha — should NOT trigger (only triggers at >3)
        var result = GotchaTool.AddGotcha("FineClass", "bug", "3rd issue");

        Assert.DoesNotContain("DOWNGRADE HINT", result);
    }

}
