using Total.Recall.Infrastructure;

namespace Total.Recall.Tests.Infrastructure;

/// <summary>
/// Tests for v2.3.0 data directory resolution fixes:
///   - Bug #1: --namespace flag now correctly uses TOTAL_RECALL_DATA as root
///   - Bug #1: --output + --namespace correctly combines both
///   - Improvement #1: Data directory mismatch warning
///   - Improvement #2: RepoConfig.ClearCache() is public
/// </summary>
[Collection("ToolTests")]
public sealed class DataDirResolutionTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string? _originalEnv;
    private readonly string? _originalNsEnv;

    public DataDirResolutionTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "total-recall-tests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempDir);
        _originalEnv = Environment.GetEnvironmentVariable(RepoConfig.EnvVarName);
        _originalNsEnv = Environment.GetEnvironmentVariable(RepoConfig.NamespaceEnvVar);
        StoreRegistry.Reset();
    }

    public void Dispose()
    {
        StoreRegistry.Reset();
        Environment.SetEnvironmentVariable(RepoConfig.EnvVarName, _originalEnv);
        Environment.SetEnvironmentVariable(RepoConfig.NamespaceEnvVar, _originalNsEnv);
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    // ── Bug #1: --namespace uses TOTAL_RECALL_DATA as root ──

    [Fact]
    public void GetNamespacePath_WithNamespaceFlag_UsesEnvVarAsRoot()
    {
        // Before fix: --namespace myproject would use CWD/data as root
        // After fix: --namespace myproject uses TOTAL_RECALL_DATA as root
        Environment.SetEnvironmentVariable(RepoConfig.EnvVarName, _tempDir);
        RepoConfig.ClearCache();

        var result = RepoConfig.GetNamespacePath("myproject");

        Assert.Equal(Path.GetFullPath(Path.Combine(_tempDir, "myproject")), result);
    }

    [Fact]
    public void GetNamespacePath_WithNamespaceFlag_NoEnvVar_FallsBackToDataDir()
    {
        Environment.SetEnvironmentVariable(RepoConfig.EnvVarName, null);
        RepoConfig.ClearCache();

        var result = RepoConfig.GetNamespacePath("myproject");

        Assert.Equal(Path.GetFullPath(Path.Combine("data", "myproject")), result);
    }

    // ── Bug #1: --output + --namespace correctly combines ──

    [Fact]
    public void GetNamespacePath_OutputAndNamespace_CombinesBoth()
    {
        // --output C:\my\data --namespace myproject → C:\my\data\myproject
        var outputPath = Path.Combine(_tempDir, "custom-output");

        var result = RepoConfig.GetNamespacePath("myproject", outputPath);

        Assert.Equal(Path.GetFullPath(Path.Combine(outputPath, "myproject")), result);
    }

    [Fact]
    public void GetNamespacePath_OutputOnly_NoNamespace_UsesOutputAsRoot()
    {
        // --output C:\my\data (no --namespace) → uses output and applies env ns / legacy / root logic
        var outputPath = Path.Combine(_tempDir, "output-only");
        // No legacy layout, no env namespace → returns output path directly
        Environment.SetEnvironmentVariable(RepoConfig.NamespaceEnvVar, null);
        RepoConfig.ClearCache();

        var result = RepoConfig.GetNamespacePath(null, outputPath);

        Assert.Equal(Path.GetFullPath(outputPath), result);
    }

    [Fact]
    public void GetNamespacePath_OutputWithEnvNamespace_CombinesOutputAndEnvNs()
    {
        // --output C:\root + env TOTAL_RECALL_NAMESPACE=myproject → C:\root\myproject
        var outputPath = Path.Combine(_tempDir, "root-dir");
        Environment.SetEnvironmentVariable(RepoConfig.NamespaceEnvVar, "myproject");
        RepoConfig.ClearCache();

        var result = RepoConfig.GetNamespacePath(null, outputPath);

        Assert.Equal(Path.GetFullPath(Path.Combine(outputPath, "myproject")), result);
    }

    [Fact]
    public void GetNamespacePath_NeitherFlag_UsesEnvVarPath()
    {
        Environment.SetEnvironmentVariable(RepoConfig.EnvVarName, _tempDir);
        Environment.SetEnvironmentVariable(RepoConfig.NamespaceEnvVar, null);
        RepoConfig.ClearCache();

        var result = RepoConfig.GetNamespacePath(null, null);

        Assert.Equal(Path.GetFullPath(_tempDir), result);
    }

    [Fact]
    public void GetNamespacePath_NeitherFlag_WithEnvNamespace_AppliesSubdir()
    {
        Environment.SetEnvironmentVariable(RepoConfig.EnvVarName, _tempDir);
        Environment.SetEnvironmentVariable(RepoConfig.NamespaceEnvVar, "my-ns");
        RepoConfig.ClearCache();

        var result = RepoConfig.GetNamespacePath(null, null);

        Assert.Equal(Path.GetFullPath(Path.Combine(_tempDir, "my-ns")), result);
    }

    // ── Improvement #2: ClearCache is public ──

    [Fact]
    public void ClearCache_IsPublicAndResetsCachedPaths()
    {
        // Set env var and cache it
        Environment.SetEnvironmentVariable(RepoConfig.EnvVarName, @"C:\first\path");
        RepoConfig.ClearCache();
        var first = RepoConfig.GetRootPath();
        Assert.Equal(Path.GetFullPath(@"C:\first\path"), first);

        // Change env var
        Environment.SetEnvironmentVariable(RepoConfig.EnvVarName, @"C:\second\path");

        // Without ClearCache, this would still return the cached first path
        RepoConfig.ClearCache();
        var second = RepoConfig.GetRootPath();
        Assert.Equal(Path.GetFullPath(@"C:\second\path"), second);
    }

    [Fact]
    public void ClearCache_ResetsNamespaceCache()
    {
        Environment.SetEnvironmentVariable(RepoConfig.NamespaceEnvVar, "ns1");
        RepoConfig.ClearCache();
        var first = RepoConfig.GetDefaultNamespace();
        Assert.Equal("ns1", first);

        Environment.SetEnvironmentVariable(RepoConfig.NamespaceEnvVar, "ns2");
        RepoConfig.ClearCache();
        var second = RepoConfig.GetDefaultNamespace();
        Assert.Equal("ns2", second);
    }

    [Fact]
    public void ResetCache_StillWorksAsInternalAlias()
    {
        // ResetCache is an internal alias for ClearCache — verify backward compat
        Environment.SetEnvironmentVariable(RepoConfig.EnvVarName, @"C:\cache-test");
        RepoConfig.ResetCache();
        var result = RepoConfig.GetRootPath();
        Assert.Equal(Path.GetFullPath(@"C:\cache-test"), result);
    }

    // ── Regression: ensures all four input combos produce deterministic results ──

    [Theory]
    [InlineData(null, null, false)]       // neither flag
    [InlineData("myproject", null, false)]   // namespace only
    [InlineData(null, "explicit", false)]  // output only
    [InlineData("myproject", "explicit", false)] // both flags
    public void GetNamespacePath_AllCombinations_DoNotThrow(
        string? ns, string? output, bool _)
    {
        Environment.SetEnvironmentVariable(RepoConfig.EnvVarName, _tempDir);
        RepoConfig.ClearCache();

        var outputPath = output is not null ? Path.Combine(_tempDir, output) : null;
        var result = RepoConfig.GetNamespacePath(ns, outputPath);

        Assert.NotNull(result);
        Assert.True(Path.IsPathRooted(result), $"Expected absolute path, got: {result}");
    }
}
