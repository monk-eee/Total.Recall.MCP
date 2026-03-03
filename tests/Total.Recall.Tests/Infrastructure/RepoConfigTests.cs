using Total.Recall.Infrastructure;

namespace Total.Recall.Tests.Infrastructure;

[Collection("ToolTests")]
public sealed class RepoConfigTests : IDisposable
{
    private readonly string? _originalEnv;

    public RepoConfigTests()
    {
        _originalEnv = Environment.GetEnvironmentVariable(RepoConfig.EnvVarName);
        StoreRegistry.Reset();
    }

    public void Dispose()
    {
        // Restore original env var
        StoreRegistry.Reset();
        Environment.SetEnvironmentVariable(RepoConfig.EnvVarName, _originalEnv);
    }

    [Fact]
    public void GetDataPath_ExplicitPath_ReturnsExplicitPath()
    {
        var result = RepoConfig.GetDataPath(@"C:\my\data");

        Assert.Equal(Path.GetFullPath(@"C:\my\data"), result);
    }

    [Fact]
    public void GetDataPath_ExplicitPathTakesPrecedenceOverEnvVar()
    {
        Environment.SetEnvironmentVariable(RepoConfig.EnvVarName, @"C:\env\data");

        var result = RepoConfig.GetDataPath(@"C:\explicit\data");

        Assert.Equal(Path.GetFullPath(@"C:\explicit\data"), result);
    }

    [Fact]
    public void GetDataPath_NoExplicitPath_UsesEnvVar()
    {
        Environment.SetEnvironmentVariable(RepoConfig.EnvVarName, @"C:\env\data");

        var result = RepoConfig.GetDataPath();

        Assert.Equal(Path.GetFullPath(@"C:\env\data"), result);
    }

    [Fact]
    public void GetDataPath_NoExplicitPath_NoEnvVar_DefaultsToDataDir()
    {
        Environment.SetEnvironmentVariable(RepoConfig.EnvVarName, null);

        var result = RepoConfig.GetDataPath();

        Assert.Equal(Path.GetFullPath("data"), result);
    }

    [Fact]
    public void GetDataPath_EmptyExplicitPath_FallsToEnvVar()
    {
        Environment.SetEnvironmentVariable(RepoConfig.EnvVarName, @"C:\env\data");

        var result = RepoConfig.GetDataPath("");

        Assert.Equal(Path.GetFullPath(@"C:\env\data"), result);
    }

    [Fact]
    public void GetDataPath_EmptyExplicitAndEmptyEnv_DefaultsToDataDir()
    {
        Environment.SetEnvironmentVariable(RepoConfig.EnvVarName, "");

        var result = RepoConfig.GetDataPath("");

        Assert.Equal(Path.GetFullPath("data"), result);
    }

    [Theory]
    [InlineData(nameof(RepoConfig.TypeRegistryPath), "type-registry.jsonl")]
    [InlineData(nameof(RepoConfig.MockRecipesPath), "mock-recipes.jsonl")]
    [InlineData(nameof(RepoConfig.CoverageGapsPath), "coverage-gaps.jsonl")]
    [InlineData(nameof(RepoConfig.GotchasPath), "gotchas.jsonl")]
    [InlineData(nameof(RepoConfig.TestInventoryPath), "test-inventory.jsonl")]
    [InlineData(nameof(RepoConfig.ConfigJsonPath), "config.json")]
    public void DataFilePath_CombinesCorrectly(string methodName, string expectedFileName)
    {
        var method = typeof(RepoConfig).GetMethod(methodName,
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static,
            null, new[] { typeof(string) }, null);
        Assert.NotNull(method);

        var result = (string)method.Invoke(null, new object[] { @"C:\data" })!;

        Assert.NotNull(result);
        Assert.Equal(Path.Combine(@"C:\data", expectedFileName), result);
        Assert.True(result.EndsWith(expectedFileName), $"Path should end with {expectedFileName}");
    }

    [Fact]
    public void EnvVarName_IsExpectedValue()
    {
        // This tests integration: consumers depend on this exact string
        Assert.Equal("TOTAL_RECALL_DATA", RepoConfig.EnvVarName);
    }

    // ── GetRootPath with explicit path (covers L29-32) ──

    [Fact]
    public void GetRootPath_ExplicitPath_ReturnsResolvedPath()
    {
        var result = RepoConfig.GetRootPath(@"C:\my\root");

        Assert.Equal(Path.GetFullPath(@"C:\my\root"), result);
    }

    [Fact]
    public void GetRootPath_ExplicitPath_BypassesCache()
    {
        // First call via env var → caches
        Environment.SetEnvironmentVariable(RepoConfig.EnvVarName, @"C:\cached\root");
        var cached = RepoConfig.GetRootPath();
        Assert.Equal(Path.GetFullPath(@"C:\cached\root"), cached);

        // Second call with explicit → ignores cache
        var explicit_ = RepoConfig.GetRootPath(@"C:\explicit\root");
        Assert.Equal(Path.GetFullPath(@"C:\explicit\root"), explicit_);
    }

    // ── ListNamespaces with non-existent root (covers L118) ──

    [Fact]
    public void ListNamespaces_NonExistentRoot_ReturnsEmpty()
    {
        var nonExistent = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString(), "does-not-exist");

        var result = RepoConfig.ListNamespaces(nonExistent);

        Assert.NotNull(result);
        Assert.Empty(result);
    }

}
