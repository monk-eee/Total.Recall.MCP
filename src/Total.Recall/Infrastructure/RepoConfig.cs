namespace Total.Recall.Infrastructure;

/// <summary>
/// Resolves the data directory path from environment variable or command line.
/// Caches the resolved path to avoid repeated env var lookups and Path.GetFullPath calls.
/// </summary>
public static class RepoConfig
{
    public const string EnvVarName = "TOTAL_RECALL_DATA";

    private static string? s_cachedDataPath;

    /// <summary>
    /// Get the data directory path. Priority: explicit path > env var > current dir.
    /// Result is cached after first resolution (env var + GetFullPath only called once).
    /// </summary>
    public static string GetDataPath(string? explicitPath = null)
    {
        // Explicit path bypasses cache (used by scanner CLI with --output)
        if (!string.IsNullOrEmpty(explicitPath))
        {
            var explicit_ = Path.GetFullPath(explicitPath);
            Log.Info($"data path (explicit): {explicit_}");
            return explicit_;
        }

        if (s_cachedDataPath is not null)
            return s_cachedDataPath;

        var envPath = Environment.GetEnvironmentVariable(EnvVarName);
        string resolved;

        if (!string.IsNullOrEmpty(envPath))
        {
            resolved = Path.GetFullPath(envPath);
            Log.Info($"data path (from {EnvVarName}): {resolved}");
        }
        else
        {
            resolved = Path.GetFullPath("data");
            Log.Warn($"{EnvVarName} env var not set — falling back to: {resolved}");
        }

        if (!Directory.Exists(resolved))
            Log.Warn($"data directory does not exist: {resolved}");

        s_cachedDataPath = resolved;
        return resolved;
    }

    public static string TypeRegistryPath(string dataDir) => Path.Combine(dataDir, "type-registry.jsonl");
    public static string MockRecipesPath(string dataDir) => Path.Combine(dataDir, "mock-recipes.jsonl");
    public static string CoverageGapsPath(string dataDir) => Path.Combine(dataDir, "coverage-gaps.jsonl");
    public static string GotchasPath(string dataDir) => Path.Combine(dataDir, "gotchas.jsonl");
    public static string TestInventoryPath(string dataDir) => Path.Combine(dataDir, "test-inventory.jsonl");

    /// <summary>
    /// Clear the cached data path. Used by tests to ensure fresh env var resolution.
    /// </summary>
    internal static void ResetCache()
    {
        s_cachedDataPath = null;
    }
}
