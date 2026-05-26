using System.Collections.Concurrent;
using System.Text.Json;
using Total.Recall.Models;

namespace Total.Recall.Infrastructure;

/// <summary>
/// Namespace-keyed store registry. Each namespace gets its own set of JsonLineStore
/// singletons with independent caches and type indexes.
///
/// Usage:
///   StoreRegistry.TypeRegistry                    — default namespace
///   StoreRegistry.ForNamespace("myproject").TypeRegistry — explicit namespace
/// </summary>
public static class StoreRegistry
{
    private static readonly ConcurrentDictionary<string, NamespaceStores> s_namespaces = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Get (or create) the store set for a specific namespace.
    /// Uses the resolved data directory as the cache key, so ForNamespace(null)
    /// and ForNamespace("myproject") map to the same stores when they resolve to the same path.
    /// </summary>
    public static NamespaceStores ForNamespace(string? ns = null)
    {
        var dataDir = RepoConfig.GetNamespacePath(ns);
        var displayName = !string.IsNullOrWhiteSpace(ns) ? ns.Trim() : RepoConfig.GetDefaultNamespace();
        return s_namespaces.GetOrAdd(dataDir, _ =>
        {
            Log.Info($"initializing stores for namespace '{displayName}' → {dataDir}");
            Log.Debug($"[StoreRegistry] creating 7 JsonLineStore instances for '{displayName}'");
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
    public static JsonLineStore<SessionRecord> Sessions => ForNamespace().Sessions;
    public static JsonLineStore<BugReport> Bugs => ForNamespace().Bugs;
    public static JsonLineStore<ToolCall> ToolCalls => ForNamespace().ToolCalls;
    public static JsonLineStore<TaskRecord> Tasks => ForNamespace().Tasks;
    public static JsonLineStore<CycleRecord> Cycles => ForNamespace().Cycles;
    public static JsonLineStore<ChallengeRecord> Challenges => ForNamespace().Challenges;
    public static JsonLineStore<EvalRecord> Evals => ForNamespace().Evals;

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

    // Lazy-loaded namespace config (framework, mock library, namespace pattern)
    private NamespaceConfig? _config;
    private bool _configLoaded;

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
        Sessions = new JsonLineStore<SessionRecord>(RepoConfig.SessionsPath(dataDir));
        Bugs = new JsonLineStore<BugReport>(RepoConfig.BugsPath(dataDir));
        ToolCalls = new JsonLineStore<ToolCall>(RepoConfig.ToolCallsPath(dataDir));
        Tasks = new JsonLineStore<TaskRecord>(RepoConfig.TasksPath(dataDir));
        Cycles = new JsonLineStore<CycleRecord>(RepoConfig.CyclesPath(dataDir));
        Challenges = new JsonLineStore<ChallengeRecord>(RepoConfig.ChallengesPath(dataDir));
        Evals = new JsonLineStore<EvalRecord>(RepoConfig.EvalsPath(dataDir));
    }

    public string Name { get; }
    public string DataDir => _dataDir;

    public JsonLineStore<TypeRecord> TypeRegistry { get; }
    public JsonLineStore<CoverageGap> CoverageGaps { get; }
    public JsonLineStore<TestInventoryEntry> TestInventory { get; }
    public JsonLineStore<Gotcha> Gotchas { get; }
    public JsonLineStore<MockRecipe> MockRecipes { get; }
    public JsonLineStore<Assessment> Assessments { get; }
    public JsonLineStore<SessionRecord> Sessions { get; }
    public JsonLineStore<BugReport> Bugs { get; }
    public JsonLineStore<ToolCall> ToolCalls { get; }
    public JsonLineStore<TaskRecord> Tasks { get; }
    public JsonLineStore<CycleRecord> Cycles { get; }
    public JsonLineStore<ChallengeRecord> Challenges { get; }
    public JsonLineStore<EvalRecord> Evals { get; }

    /// <summary>
    /// Gets the namespace configuration (test framework, mock library, namespace pattern).
    /// Lazy-loaded from config.json on first access. Returns defaults (xUnit/Moq) if not found.
    /// </summary>
    public NamespaceConfig Config
    {
        get
        {
            if (!_configLoaded)
            {
                var configPath = RepoConfig.ConfigJsonPath(_dataDir);
                if (File.Exists(configPath))
                {
                    try
                    {
                        var json = File.ReadAllText(configPath);
                        _config = JsonSerializer.Deserialize<NamespaceConfig>(json, SharedJsonOptions.CamelCase);
                        Log.Debug($"[NamespaceStores] loaded config for '{Name}': framework={_config?.TestFramework}, mock={_config?.MockLibrary}");
                    }
                    catch (Exception ex)
                    {
                        Log.Warn($"[NamespaceStores] failed to load config.json for '{Name}': {ex.Message}");
                    }
                }
                _config ??= new NamespaceConfig();
                _configLoaded = true;
            }
            return _config!;
        }
    }

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
            Log.Debug($"[TypeIndex] rebuilding index for '{Name}' ({all.Count} types)");
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

    /// <summary>
    /// Resolve a type name using the 3-step strategy: exact → case-insensitive → contains.
    /// Returns null if no match found. Tracks lookup metrics automatically.
    /// This centralizes the type resolution pattern used by ContextTool, TestScaffoldTool,
    /// and TestableTargetsTool.
    /// </summary>
    public TypeRecord? ResolveType(string typeName)
    {
        Log.Debug($"[ResolveType] resolving '{typeName}' in namespace '{Name}'");
        var (exactIndex, ciIndex) = GetTypeIndex();

        if (exactIndex.TryGetValue(typeName, out var exact))
        {
            Metrics.Increment(Metrics.LookupExact);
            Log.Debug($"[ResolveType] exact match: {exact.Name} ({exact.Namespace})");
            return exact;
        }

        if (ciIndex.TryGetValue(typeName, out var ci))
        {
            Metrics.Increment(Metrics.LookupCaseInsensitive);
            Log.Debug($"[ResolveType] case-insensitive match: {ci.Name} ({ci.Namespace})");
            return ci;
        }

        // Contains fallback — linear scan only when dictionary misses
        var match = TypeRegistry.LoadAll().FirstOrDefault(t =>
            t.Name.Contains(typeName, StringComparison.OrdinalIgnoreCase));

        Metrics.Increment(match is not null ? Metrics.LookupContains : Metrics.LookupMiss);
        Log.Debug($"[ResolveType] contains scan: {(match is not null ? match.Name : "miss")}");
        return match;
    }
}
