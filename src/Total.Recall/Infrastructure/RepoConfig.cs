namespace Total.Recall.Infrastructure;

/// <summary>
/// Resolves the data directory path from environment variable or command line.
/// </summary>
public static class RepoConfig
{
    public const string EnvVarName = "TOTAL_RECALL_DATA";

    /// <summary>
    /// Get the data directory path. Priority: explicit path > env var > current dir.
    /// </summary>
    public static string GetDataPath(string? explicitPath = null)
    {
        if (!string.IsNullOrEmpty(explicitPath))
            return Path.GetFullPath(explicitPath);

        var envPath = Environment.GetEnvironmentVariable(EnvVarName);
        if (!string.IsNullOrEmpty(envPath))
            return Path.GetFullPath(envPath);

        return Path.GetFullPath("data");
    }

    public static string TypeRegistryPath(string dataDir) => Path.Combine(dataDir, "type-registry.jsonl");
    public static string MockRecipesPath(string dataDir) => Path.Combine(dataDir, "mock-recipes.jsonl");
    public static string CoverageGapsPath(string dataDir) => Path.Combine(dataDir, "coverage-gaps.jsonl");
    public static string GotchasPath(string dataDir) => Path.Combine(dataDir, "gotchas.jsonl");
    public static string TestInventoryPath(string dataDir) => Path.Combine(dataDir, "test-inventory.jsonl");
}
