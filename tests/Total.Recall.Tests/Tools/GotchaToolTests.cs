using Total.Recall.Infrastructure;
using Total.Recall.Models;
using Total.Recall.Tools;

namespace Total.Recall.Tests.Tools;

[Collection("ToolTests")]
public sealed class GotchaToolTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string? _originalEnv;

    public GotchaToolTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "total-recall-tests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempDir);
        _originalEnv = Environment.GetEnvironmentVariable(RepoConfig.EnvVarName);
        Environment.SetEnvironmentVariable(RepoConfig.EnvVarName, _tempDir);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(RepoConfig.EnvVarName, _originalEnv);
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private void SeedGotchas(params Gotcha[] records)
    {
        var store = new JsonLineStore<Gotcha>(RepoConfig.GotchasPath(_tempDir));
        store.WriteAll(records);
    }

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
        var store = new JsonLineStore<Gotcha>(RepoConfig.GotchasPath(_tempDir));
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

        var store = new JsonLineStore<Gotcha>(RepoConfig.GotchasPath(_tempDir));
        var all = store.LoadAll();
        Assert.Equal(3, all.Count);
    }

    [Fact]
    public void AddGotcha_SetsDateToToday()
    {
        GotchaTool.AddGotcha("X", "bug", "test");

        var store = new JsonLineStore<Gotcha>(RepoConfig.GotchasPath(_tempDir));
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
}
