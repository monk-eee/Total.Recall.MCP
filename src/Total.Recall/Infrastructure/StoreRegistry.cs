using System.Collections.Concurrent;
using Total.Recall.Models;

namespace Total.Recall.Infrastructure;

/// <summary>
/// Namespace-keyed store registry. Each namespace gets its own set of JsonLineStore
/// singletons with independent caches and type indexes.
///
/// Usage:
///   StoreRegistry.TypeRegistry                    — default namespace
///   StoreRegistry.ForNamespace("linter").TypeRegistry — explicit namespace
/// </summary>
public static class StoreRegistry
{
    private static readonly ConcurrentDictionary<string, NamespaceStores> s_namespaces = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Get (or create) the store set for a specific namespace.
    /// Uses the resolved data directory as the cache key, so ForNamespace(null)
    /// and ForNamespace("linter") map to the same stores when they resolve to the same path.
    /// </summary>
    public static NamespaceStores ForNamespace(string? ns = null)
    {
        var dataDir = RepoConfig.GetNamespacePath(ns);
        var displayName = !string.IsNullOrWhiteSpace(ns) ? ns.Trim() : RepoConfig.GetDefaultNamespace();
        return s_namespaces.GetOrAdd(dataDir, _ =>
        {
            Log.Info($"initializing stores for namespace '{displayName}' → {dataDir}");
            return new NamespaceStores(displayName, dataDir);
        });
    }

    // ── Default-namespace shortcuts (backward compatible) ──

    public static JsonLineStore<TypeRecord> TypeRegistry => ForNamespace().TypeRegistry;
    public static JsonLineStore<CoverageGap> CoverageGaps => ForNamespace().CoverageGaps;
    public static JsonLineStore<TestInventoryEntry> TestInventory => ForNamespace().TestInventory;
    public static JsonLineStore<Gotcha> Gotchas => ForNamespace().Gotchas;
    public static JsonLineStore<MockRecipe> MockRecipes => ForNamespace().MockRecipes;
    public static JsonLineStore<Assessment> Assessments => ForNamespace().Assessments;

    /// <summary>
    /// Get pre-built name→TypeRecord dictionaries for O(1) lookups (default namespace).
    /// </summary>
    public static (Dictionary<string, TypeRecord> Exact, Dictionary<string, TypeRecord> CaseInsensitive) GetTypeIndex()
        => ForNamespace().GetTypeIndex();

    /// <summary>
    /// Reset all cached stores and indexes for all namespaces. Used by tests.
    /// </summary>
    internal static void Reset()
    {
        s_namespaces.Clear();
        RepoConfig.ResetCache();
    }
}

/// <summary>
/// Per-namespace set of JsonLineStore singletons + type index.
/// </summary>
public sealed class NamespaceStores
{
    private readonly string _dataDir;
    private readonly object _indexLock = new();

    // Pre-built lookup dictionaries (invalidated when cache refreshes)
    private Dictionary<string, TypeRecord>? _typeByExactName;
    private Dictionary<string, TypeRecord>? _typeByCiName;
    private int _typeIndexVersion;

    public NamespaceStores(string name, string dataDir)
    {
        Name = name;
        _dataDir = dataDir;

        TypeRegistry = new JsonLineStore<TypeRecord>(RepoConfig.TypeRegistryPath(dataDir));
        CoverageGaps = new JsonLineStore<CoverageGap>(RepoConfig.CoverageGapsPath(dataDir));
        TestInventory = new JsonLineStore<TestInventoryEntry>(RepoConfig.TestInventoryPath(dataDir));
        Gotchas = new JsonLineStore<Gotcha>(RepoConfig.GotchasPath(dataDir));
        MockRecipes = new JsonLineStore<MockRecipe>(RepoConfig.MockRecipesPath(dataDir));
        Assessments = new JsonLineStore<Assessment>(RepoConfig.AssessmentsPath(dataDir));
    }

    public string Name { get; }
    public string DataDir => _dataDir;

    public JsonLineStore<TypeRecord> TypeRegistry { get; }
    public JsonLineStore<CoverageGap> CoverageGaps { get; }
    public JsonLineStore<TestInventoryEntry> TestInventory { get; }
    public JsonLineStore<Gotcha> Gotchas { get; }
    public JsonLineStore<MockRecipe> MockRecipes { get; }
    public JsonLineStore<Assessment> Assessments { get; }

    /// <summary>
    /// Get pre-built name→TypeRecord dictionaries for O(1) lookups.
    /// Rebuilds automatically when the underlying store cache refreshes.
    /// </summary>
    public (Dictionary<string, TypeRecord> Exact, Dictionary<string, TypeRecord> CaseInsensitive) GetTypeIndex()
    {
        var all = TypeRegistry.LoadAll();
        var version = all.GetHashCode(); // reference changes when cache refreshes

        lock (_indexLock)
        {
            if (_typeByExactName is not null && _typeIndexVersion == version)
            {
                Metrics.Increment(Metrics.TypeIndexHit);
                return (_typeByExactName, _typeByCiName!);
            }

            // Build both dictionaries in a single pass
            Metrics.Increment(Metrics.TypeIndexRebuild);
            var exact = new Dictionary<string, TypeRecord>(all.Count, StringComparer.Ordinal);
            var ci = new Dictionary<string, TypeRecord>(all.Count, StringComparer.OrdinalIgnoreCase);

            foreach (var t in all)
            {
                exact.TryAdd(t.Name, t); // first wins for duplicates
                ci.TryAdd(t.Name, t);
            }

            _typeByExactName = exact;
            _typeByCiName = ci;
            _typeIndexVersion = version;

            return (exact, ci);
        }
    }
}
