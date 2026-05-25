namespace Total.Recall.Infrastructure;

/// <summary>
/// Resolves the data root + namespace to produce per-namespace data directories.
/// Layout: {root}/{namespace}/ — e.g. data/myproject/, data/other-project/
///
/// Environment variables:
///   TOTAL_RECALL_DATA      — root data directory (default: "data")
///   TOTAL_RECALL_NAMESPACE — default namespace   (default: "default")
///
/// Backward compatible: if TOTAL_RECALL_DATA contains .jsonl files directly,
/// it is treated as a single-namespace root (legacy mode).
/// </summary>
public static class RepoConfig
{
    public const string EnvVarName = "TOTAL_RECALL_DATA";
    public const string NamespaceEnvVar = "TOTAL_RECALL_NAMESPACE";
    public const string DefaultNamespaceFallback = "default";

    private static string? s_cachedRootPath;
    private static string? s_cachedDefaultNamespace;

    /// <summary>
    /// Get the root data directory. Priority: explicit path > env var > "data".
    /// </summary>
    public static string GetRootPath(string? explicitPath = null)
    {
        if (!string.IsNullOrEmpty(explicitPath))
        {
            var resolved = Path.GetFullPath(explicitPath);
            Log.Info($"data root (explicit): {resolved}");
            return resolved;
        }

        if (s_cachedRootPath is not null)
            return s_cachedRootPath;

        var envPath = Environment.GetEnvironmentVariable(EnvVarName);
        string root;

        if (!string.IsNullOrEmpty(envPath))
        {
            root = Path.GetFullPath(envPath);
            Log.Info($"data root (from {EnvVarName}): {root}");
        }
        else
        {
            root = Path.GetFullPath("data");
            Log.Warn($"{EnvVarName} env var not set — falling back to: {root}");
        }

        s_cachedRootPath = root;
        return root;
    }

    /// <summary>
    /// Get the default namespace name. Priority: env var > "default".
    /// </summary>
    public static string GetDefaultNamespace()
    {
        if (s_cachedDefaultNamespace is not null)
            return s_cachedDefaultNamespace;

        var ns = Environment.GetEnvironmentVariable(NamespaceEnvVar);
        s_cachedDefaultNamespace = !string.IsNullOrWhiteSpace(ns) ? ns.Trim() : DefaultNamespaceFallback;
        Log.Info($"default namespace: {s_cachedDefaultNamespace}");
        return s_cachedDefaultNamespace;
    }

    /// <summary>
    /// Resolve the full data path for a namespace.
    ///
    /// Behavior:
    ///   - Explicit ns parameter → always {root}/{ns}/
    ///   - No ns + TOTAL_RECALL_NAMESPACE set → {root}/{envNs}/
    ///   - No ns + no env namespace + legacy .jsonl in root → root as-is
    ///   - No ns + no env namespace → root as-is (single-namespace backward compat)
    /// </summary>
    public static string GetNamespacePath(string? ns = null, string? explicitRoot = null)
    {
        var root = GetRootPath(explicitRoot);

        // Explicit namespace parameter → always use subdirectory
        if (!string.IsNullOrWhiteSpace(ns))
            return Path.GetFullPath(Path.Combine(root, ns.Trim()));

        // Legacy mode: if root contains .jsonl files directly, use root as the data dir
        if (IsLegacyLayout(root))
            return root;

        // If TOTAL_RECALL_NAMESPACE env var is explicitly set, use as subdirectory
        var envNs = Environment.GetEnvironmentVariable(NamespaceEnvVar);
        if (!string.IsNullOrWhiteSpace(envNs))
            return Path.GetFullPath(Path.Combine(root, envNs.Trim()));

        // No explicit namespace → use root directly (single-namespace / backward compat)
        return root;
    }

    /// <summary>
    /// Backward compat: check if .jsonl files live directly in root (no namespace subdirs).
    /// </summary>
    internal static bool IsLegacyLayout(string root)
    {
        if (!Directory.Exists(root))
            return false;
        return Directory.EnumerateFiles(root, "*.jsonl").Any();
    }

    /// <summary>
    /// List all available namespaces. A subdirectory of root counts as a
    /// namespace if it contains at least one <c>*.jsonl</c> file (scanner has
    /// run) OR a <c>config.json</c> file (freshly initialised via
    /// <c>total-recall init</c> but not yet scanned).
    /// </summary>
    public static List<string> ListNamespaces(string? explicitRoot = null)
    {
        var root = GetRootPath(explicitRoot);

        if (!Directory.Exists(root))
            return [];

        // Legacy mode: root IS the namespace
        if (IsLegacyLayout(root))
            return [Path.GetFileName(root)];

        return Directory.GetDirectories(root)
            .Where(d =>
                Directory.EnumerateFiles(d, "*.jsonl").Any()
                || File.Exists(Path.Combine(d, "config.json")))
            .Select(d => Path.GetFileName(d))
            .OrderBy(n => n)
            .ToList();
    }

    // ── Backward-compatible convenience methods (use default namespace) ──

    /// <summary>
    /// Get the data directory path for the default namespace.
    /// Backward compatible with code that calls GetDataPath().
    /// </summary>
    public static string GetDataPath(string? explicitPath = null)
    {
        // If an explicit path is provided (scanner --output), use it directly
        if (!string.IsNullOrEmpty(explicitPath))
        {
            var resolved = Path.GetFullPath(explicitPath);
            Log.Info($"data path (explicit): {resolved}");
            return resolved;
        }

        return GetNamespacePath();
    }

    public static string TypeRegistryPath(string dataDir) => Path.Combine(dataDir, "type-registry.jsonl");
    public static string MockRecipesPath(string dataDir) => Path.Combine(dataDir, "mock-recipes.jsonl");
    public static string CoverageGapsPath(string dataDir) => Path.Combine(dataDir, "coverage-gaps.jsonl");
    public static string GotchasPath(string dataDir) => Path.Combine(dataDir, "gotchas.jsonl");
    public static string TestInventoryPath(string dataDir) => Path.Combine(dataDir, "test-inventory.jsonl");
    public static string AssessmentsPath(string dataDir) => Path.Combine(dataDir, "assessments.jsonl");
    public static string SessionsPath(string dataDir) => Path.Combine(dataDir, "sessions.jsonl");
    public static string ConfigJsonPath(string dataDir) => Path.Combine(dataDir, "config.json");

    // ── Eval harness stores (Cuts 1–6) ──
    public static string ToolCallsPath(string dataDir) => Path.Combine(dataDir, "tool-calls.jsonl");
    public static string TasksPath(string dataDir) => Path.Combine(dataDir, "tasks.jsonl");
    public static string CyclesPath(string dataDir) => Path.Combine(dataDir, "cycles.jsonl");
    public static string ChallengesPath(string dataDir) => Path.Combine(dataDir, "challenges.jsonl");
    public static string EvalsPath(string dataDir) => Path.Combine(dataDir, "evals.jsonl");

    /// <summary>
    /// Clear all cached paths. Use in tests to ensure fresh env var resolution,
    /// or after changing environment variables at runtime.
    /// </summary>
    public static void ClearCache()
    {
        s_cachedRootPath = null;
        s_cachedDefaultNamespace = null;
    }

    /// <summary>Alias for <see cref="ClearCache"/> — kept for backward compat.</summary>
    internal static void ResetCache() => ClearCache();
}
