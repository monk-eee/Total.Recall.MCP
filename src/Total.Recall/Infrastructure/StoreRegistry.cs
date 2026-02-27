using Total.Recall.Models;

namespace Total.Recall.Infrastructure;

/// <summary>
/// Singleton store registry — one JsonLineStore per JSONL file per process.
/// Eliminates redundant store construction across tool calls, preserving
/// the in-memory cache that JsonLineStore provides.
/// </summary>
public static class StoreRegistry
{
    private static readonly object s_lock = new();
    private static string? s_dataDir;

    private static JsonLineStore<TypeRecord>? s_typeRegistry;
    private static JsonLineStore<CoverageGap>? s_coverageGaps;
    private static JsonLineStore<TestInventoryEntry>? s_testInventory;
    private static JsonLineStore<Gotcha>? s_gotchas;
    private static JsonLineStore<MockRecipe>? s_mockRecipes;

    // Pre-built lookup dictionaries (invalidated when cache refreshes)
    private static Dictionary<string, TypeRecord>? s_typeByExactName;
    private static Dictionary<string, TypeRecord>? s_typeByCiName;
    private static int s_typeIndexVersion;

    private static string DataDir
    {
        get
        {
            if (s_dataDir is null)
            {
                lock (s_lock)
                {
                    if (s_dataDir is null)
                    {
                        try
                        {
                            s_dataDir = RepoConfig.GetDataPath();
                        }
                        catch (Exception ex)
                        {
                            Log.Error($"failed to resolve data directory: {ex.GetType().Name}: {ex.Message}");
                            throw;
                        }
                    }
                }
            }
            return s_dataDir;
        }
    }

    public static JsonLineStore<TypeRecord> TypeRegistry =>
        s_typeRegistry ??= new JsonLineStore<TypeRecord>(RepoConfig.TypeRegistryPath(DataDir));

    public static JsonLineStore<CoverageGap> CoverageGaps =>
        s_coverageGaps ??= new JsonLineStore<CoverageGap>(RepoConfig.CoverageGapsPath(DataDir));

    public static JsonLineStore<TestInventoryEntry> TestInventory =>
        s_testInventory ??= new JsonLineStore<TestInventoryEntry>(RepoConfig.TestInventoryPath(DataDir));

    public static JsonLineStore<Gotcha> Gotchas =>
        s_gotchas ??= new JsonLineStore<Gotcha>(RepoConfig.GotchasPath(DataDir));

    public static JsonLineStore<MockRecipe> MockRecipes =>
        s_mockRecipes ??= new JsonLineStore<MockRecipe>(RepoConfig.MockRecipesPath(DataDir));

    /// <summary>
    /// Get pre-built name→TypeRecord dictionaries for O(1) lookups.
    /// Rebuilds automatically when the underlying store cache refreshes.
    /// </summary>
    public static (Dictionary<string, TypeRecord> Exact, Dictionary<string, TypeRecord> CaseInsensitive) GetTypeIndex()
    {
        var all = TypeRegistry.LoadAll();
        var version = all.GetHashCode(); // reference changes when cache refreshes

        if (s_typeByExactName is not null && s_typeIndexVersion == version)
            return (s_typeByExactName, s_typeByCiName!);

        // Build both dictionaries in a single pass
        var exact = new Dictionary<string, TypeRecord>(all.Count, StringComparer.Ordinal);
        var ci = new Dictionary<string, TypeRecord>(all.Count, StringComparer.OrdinalIgnoreCase);

        foreach (var t in all)
        {
            exact.TryAdd(t.Name, t); // first wins for duplicates
            ci.TryAdd(t.Name, t);
        }

        s_typeByExactName = exact;
        s_typeByCiName = ci;
        s_typeIndexVersion = version;

        return (exact, ci);
    }

    /// <summary>
    /// Reset all cached stores and indexes. Used by tests to ensure
    /// StoreRegistry picks up a fresh TOTAL_RECALL_DATA env var.
    /// </summary>
    internal static void Reset()
    {
        lock (s_lock)
        {
            s_dataDir = null;
            s_typeRegistry = null;
            s_coverageGaps = null;
            s_testInventory = null;
            s_gotchas = null;
            s_mockRecipes = null;
            s_typeByExactName = null;
            s_typeByCiName = null;
            s_typeIndexVersion = 0;
        }
        RepoConfig.ResetCache();
    }
}
