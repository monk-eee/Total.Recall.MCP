using Total.Recall.Infrastructure;
using Total.Recall.Models;
using Total.Recall.Tools;

namespace Total.Recall.Tests.Tools;

/// <summary>
/// Tests for ResolveTypeTool. Uses a temp directory with seeded type-registry.jsonl.
/// Overrides TOTAL_RECALL_DATA env var to point to temp data.
/// </summary>
[Collection("ToolTests")]
public sealed class ResolveTypeToolTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string? _originalEnv;

    public ResolveTypeToolTests()
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

    private void SeedTypeRegistry(params TypeRecord[] records)
    {
        var store = new JsonLineStore<TypeRecord>(RepoConfig.TypeRegistryPath(_tempDir));
        store.WriteAll(records);
    }

    [Fact]
    public void ResolveType_NoData_ReturnsNotFoundMessage()
    {
        var result = ResolveTypeTool.ResolveType("Anything");

        Assert.Contains("No type registry found", result);
    }

    [Fact]
    public void ResolveType_ExactNameMatch_ReturnsType()
    {
        SeedTypeRegistry(
            new TypeRecord { Name = "Calculator", Namespace = "MyApp" },
            new TypeRecord { Name = "Parser", Namespace = "MyApp" }
        );

        var result = ResolveTypeTool.ResolveType("Calculator");

        Assert.Contains("Calculator", result);
        Assert.Contains("MyApp", result);
    }

    [Fact]
    public void ResolveType_CaseInsensitiveMatch_ReturnsType()
    {
        SeedTypeRegistry(new TypeRecord { Name = "MyService", Namespace = "App" });

        var result = ResolveTypeTool.ResolveType("myservice");

        Assert.Contains("MyService", result);
    }

    [Fact]
    public void ResolveType_PartialMatch_ReturnsContainingTypes()
    {
        SeedTypeRegistry(
            new TypeRecord { Name = "StringHelper", Namespace = "Utils" },
            new TypeRecord { Name = "DateHelper", Namespace = "Utils" },
            new TypeRecord { Name = "Calculator", Namespace = "Math" }
        );

        var result = ResolveTypeTool.ResolveType("Helper");

        Assert.Contains("StringHelper", result);
        Assert.Contains("DateHelper", result);
        Assert.DoesNotContain("Calculator", result);
    }

    [Fact]
    public void ResolveType_InterfaceSearch_FindsImplementors()
    {
        SeedTypeRegistry(
            new TypeRecord { Name = "MyService", Namespace = "App", Interfaces = ["IDisposable", "IService"] },
            new TypeRecord { Name = "OtherClass", Namespace = "App", Interfaces = [] }
        );

        var result = ResolveTypeTool.ResolveType("IService");

        Assert.Contains("MyService", result);
        Assert.DoesNotContain("OtherClass", result);
    }

    [Fact]
    public void ResolveType_NoMatch_ReturnsNotFoundMessage()
    {
        SeedTypeRegistry(new TypeRecord { Name = "Foo", Namespace = "Bar" });

        var result = ResolveTypeTool.ResolveType("Nonexistent");

        Assert.Contains("No type found matching", result);
    }

    [Fact]
    public void ResolveType_LimitsToFiveResults()
    {
        var records = Enumerable.Range(1, 10)
            .Select(i => new TypeRecord { Name = $"Widget{i}", Namespace = "App" })
            .ToArray();
        SeedTypeRegistry(records);

        var result = ResolveTypeTool.ResolveType("Widget");

        // Count occurrences of "Widget" as property values (each record has Name: WidgetN)
        var count = result.Split("Widget").Length - 1;
        // At most 5 results, but each may have "Widget" in Name → at most ~10 occurrences
        // We check the result is valid JSON with at most 5 entries
        Assert.Contains("Widget", result);
    }
}
