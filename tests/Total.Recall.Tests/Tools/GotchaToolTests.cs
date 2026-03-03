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

}
