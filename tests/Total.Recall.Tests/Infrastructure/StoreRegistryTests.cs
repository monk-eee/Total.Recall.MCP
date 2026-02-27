using Total.Recall.Infrastructure;
using Total.Recall.Models;

namespace Total.Recall.Tests.Infrastructure;

[Collection("ToolTests")]
public sealed class StoreRegistryTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string? _originalEnv;

    public StoreRegistryTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "total-recall-tests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempDir);
        _originalEnv = Environment.GetEnvironmentVariable(RepoConfig.EnvVarName);
        Environment.SetEnvironmentVariable(RepoConfig.EnvVarName, _tempDir);
        StoreRegistry.Reset();
    }

    public void Dispose()
    {
        StoreRegistry.Reset();
        Environment.SetEnvironmentVariable(RepoConfig.EnvVarName, _originalEnv);
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public void TypeRegistry_ReturnsSameInstanceOnSecondCall()
    {
        var first = StoreRegistry.TypeRegistry;
        var second = StoreRegistry.TypeRegistry;
        Assert.Same(first, second);
    }

    [Fact]
    public void CoverageGaps_ReturnsSameInstanceOnSecondCall()
    {
        var first = StoreRegistry.CoverageGaps;
        var second = StoreRegistry.CoverageGaps;
        Assert.Same(first, second);
    }

    [Fact]
    public void TestInventory_ReturnsSameInstanceOnSecondCall()
    {
        var first = StoreRegistry.TestInventory;
        var second = StoreRegistry.TestInventory;
        Assert.Same(first, second);
    }

    [Fact]
    public void Gotchas_ReturnsSameInstanceOnSecondCall()
    {
        var first = StoreRegistry.Gotchas;
        var second = StoreRegistry.Gotchas;
        Assert.Same(first, second);
    }

    [Fact]
    public void MockRecipes_ReturnsSameInstanceOnSecondCall()
    {
        var first = StoreRegistry.MockRecipes;
        var second = StoreRegistry.MockRecipes;
        Assert.Same(first, second);
    }

    [Fact]
    public void Reset_ClearsAllInstances()
    {
        var before = StoreRegistry.TypeRegistry;
        StoreRegistry.Reset();
        // After reset, re-setting the env recreates fresh stores
        Environment.SetEnvironmentVariable(RepoConfig.EnvVarName, _tempDir);
        var after = StoreRegistry.TypeRegistry;
        Assert.NotSame(before, after);
    }

    [Fact]
    public void GetTypeIndex_ReturnsEmptyDictionaries_WhenNoData()
    {
        var (exact, ci) = StoreRegistry.GetTypeIndex();
        Assert.Empty(exact);
        Assert.Empty(ci);
    }

    [Fact]
    public void GetTypeIndex_BuildsDictionariesFromTypeRegistry()
    {
        // Seed type registry
        var store = new JsonLineStore<TypeRecord>(RepoConfig.TypeRegistryPath(_tempDir));
        store.WriteAll([
            new TypeRecord { Name = "MyClass", Namespace = "App" },
            new TypeRecord { Name = "MyService", Namespace = "App.Services" }
        ]);

        var (exact, ci) = StoreRegistry.GetTypeIndex();

        Assert.Equal(2, exact.Count);
        Assert.True(exact.ContainsKey("MyClass"));
        Assert.True(exact.ContainsKey("MyService"));
    }

    [Fact]
    public void GetTypeIndex_CaseInsensitiveDictionary_MatchesIgnoringCase()
    {
        var store = new JsonLineStore<TypeRecord>(RepoConfig.TypeRegistryPath(_tempDir));
        store.WriteAll([
            new TypeRecord { Name = "MyClass", Namespace = "App" }
        ]);

        var (_, ci) = StoreRegistry.GetTypeIndex();

        Assert.True(ci.ContainsKey("myclass"));
        Assert.True(ci.ContainsKey("MYCLASS"));
    }

    [Fact]
    public void GetTypeIndex_ReturnsCachedResult_OnSecondCall()
    {
        var store = new JsonLineStore<TypeRecord>(RepoConfig.TypeRegistryPath(_tempDir));
        store.WriteAll([
            new TypeRecord { Name = "MyClass", Namespace = "App" }
        ]);

        var (exact1, ci1) = StoreRegistry.GetTypeIndex();
        var (exact2, ci2) = StoreRegistry.GetTypeIndex();

        Assert.Same(exact1, exact2);
        Assert.Same(ci1, ci2);
    }

    [Fact]
    public void GetTypeIndex_DuplicateNames_FirstWins()
    {
        var store = new JsonLineStore<TypeRecord>(RepoConfig.TypeRegistryPath(_tempDir));
        store.WriteAll([
            new TypeRecord { Name = "Widget", Namespace = "App.V1" },
            new TypeRecord { Name = "Widget", Namespace = "App.V2" }
        ]);

        var (exact, _) = StoreRegistry.GetTypeIndex();

        Assert.Equal("App.V1", exact["Widget"].Namespace);
    }

}
