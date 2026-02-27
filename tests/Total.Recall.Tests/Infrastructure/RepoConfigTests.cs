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

    [Fact]
    public void TypeRegistryPath_CombinesCorrectly()
    {
        var result = RepoConfig.TypeRegistryPath(@"C:\data");

        Assert.Equal(Path.Combine(@"C:\data", "type-registry.jsonl"), result);
    }

    [Fact]
    public void MockRecipesPath_CombinesCorrectly()
    {
        var result = RepoConfig.MockRecipesPath(@"C:\data");

        Assert.Equal(Path.Combine(@"C:\data", "mock-recipes.jsonl"), result);
    }

    [Fact]
    public void CoverageGapsPath_CombinesCorrectly()
    {
        var result = RepoConfig.CoverageGapsPath(@"C:\data");

        Assert.Equal(Path.Combine(@"C:\data", "coverage-gaps.jsonl"), result);
    }

    [Fact]
    public void GotchasPath_CombinesCorrectly()
    {
        var result = RepoConfig.GotchasPath(@"C:\data");

        Assert.Equal(Path.Combine(@"C:\data", "gotchas.jsonl"), result);
    }

    [Fact]
    public void TestInventoryPath_CombinesCorrectly()
    {
        var result = RepoConfig.TestInventoryPath(@"C:\data");

        Assert.Equal(Path.Combine(@"C:\data", "test-inventory.jsonl"), result);
    }

    [Fact]
    public void EnvVarName_IsExpectedValue()
    {
        // This tests integration: consumers depend on this exact string
        Assert.Equal("TOTAL_RECALL_DATA", RepoConfig.EnvVarName);
    }
}
