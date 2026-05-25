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

    /// <summary>
    /// Regression test: a namespace freshly created by <c>total-recall init</c>
    /// contains only <c>config.json</c> (no <c>*.jsonl</c> yet) until the scanner
    /// runs. Previously, <c>ListNamespaces</c> filtered to dirs containing at
    /// least one JSONL file, so <c>doctor</c> would report "no namespaces found"
    /// even after a successful <c>init</c>. Surfaced by Copilot code review on
    /// PR #10. Fix: include dirs with <c>config.json</c> OR <c>*.jsonl</c>.
    /// </summary>
    [Fact]
    public void ListNamespaces_IncludesConfigOnlyDirs_FromFreshInit()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(root);
        try
        {
            // Freshly init'd namespace — config.json only, no JSONL yet.
            var freshlyInited = Path.Combine(root, "fresh");
            Directory.CreateDirectory(freshlyInited);
            File.WriteAllText(Path.Combine(freshlyInited, "config.json"), "{}");

            // Scanned namespace — has JSONL.
            var scanned = Path.Combine(root, "scanned");
            Directory.CreateDirectory(scanned);
            File.WriteAllText(Path.Combine(scanned, "type-registry.jsonl"), "");

            // Empty namespace — neither file. Must NOT appear.
            Directory.CreateDirectory(Path.Combine(root, "empty"));

            var result = RepoConfig.ListNamespaces(root);

            Assert.Contains("fresh", result);
            Assert.Contains("scanned", result);
            Assert.DoesNotContain("empty", result);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

}
