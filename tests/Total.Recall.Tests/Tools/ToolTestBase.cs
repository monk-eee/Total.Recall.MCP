using Total.Recall.Infrastructure;
using Total.Recall.Models;

namespace Total.Recall.Tests.Tools;

/// <summary>
/// Shared base class for all MCP tool tests. Handles:
/// - Temp directory creation and cleanup
/// - TOTAL_RECALL_DATA env var save/restore
/// - Optional TOTAL_RECALL_NAMESPACE env var save/restore
/// - StoreRegistry.Reset() in ctor and Dispose
/// - Common JSONL seed helpers for the 7 standard data files
/// </summary>
public abstract class ToolTestBase : IDisposable
{
    protected string TempDir { get; }

    private readonly string? _savedDataEnv;
    private readonly string? _savedNamespaceEnv;
    private readonly bool _restoreNamespace;

    /// <summary>
    /// Initializes temp directory and env vars.
    /// </summary>
    /// <param name="saveNamespace">
    /// When true, saves and clears TOTAL_RECALL_NAMESPACE (Variant B pattern).
    /// When false, only saves TOTAL_RECALL_DATA (Variant A pattern — default).
    /// </param>
    protected ToolTestBase(bool saveNamespace = false)
    {
        TempDir = Path.Combine(Path.GetTempPath(), "total-recall-tests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(TempDir);

        _savedDataEnv = Environment.GetEnvironmentVariable(RepoConfig.EnvVarName);
        Environment.SetEnvironmentVariable(RepoConfig.EnvVarName, TempDir);

        _restoreNamespace = saveNamespace;
        if (saveNamespace)
        {
            _savedNamespaceEnv = Environment.GetEnvironmentVariable(RepoConfig.NamespaceEnvVar);
            Environment.SetEnvironmentVariable(RepoConfig.NamespaceEnvVar, null);
        }

        StoreRegistry.Reset();
    }

    public virtual void Dispose()
    {
        StoreRegistry.Reset();
        Environment.SetEnvironmentVariable(RepoConfig.EnvVarName, _savedDataEnv);

        if (_restoreNamespace)
            Environment.SetEnvironmentVariable(RepoConfig.NamespaceEnvVar, _savedNamespaceEnv);

        if (Directory.Exists(TempDir))
            Directory.Delete(TempDir, recursive: true);
    }

    // ── Common seed helpers ──

    protected void SeedData<T>(string filePath, params T[] records) where T : class
    {
        var store = new JsonLineStore<T>(filePath);
        store.WriteAll(records);
    }

    protected void SeedTypeRegistry(params TypeRecord[] records)
        => SeedData(RepoConfig.TypeRegistryPath(TempDir), records);

    protected void SeedCoverageGaps(params CoverageGap[] records)
        => SeedData(RepoConfig.CoverageGapsPath(TempDir), records);

    protected void SeedGotchas(params Gotcha[] records)
        => SeedData(RepoConfig.GotchasPath(TempDir), records);

    protected void SeedTestInventory(params TestInventoryEntry[] records)
        => SeedData(RepoConfig.TestInventoryPath(TempDir), records);

    protected void SeedAssessments(params Assessment[] records)
        => SeedData(RepoConfig.AssessmentsPath(TempDir), records);

    protected void SeedMockRecipes(params MockRecipe[] records)
        => SeedData(RepoConfig.MockRecipesPath(TempDir), records);

    protected void SeedSessions(params SessionRecord[] records)
        => SeedData(RepoConfig.SessionsPath(TempDir), records);

    protected void SeedBugs(params BugReport[] records)
        => SeedData(RepoConfig.BugsPath(TempDir), records);
}
