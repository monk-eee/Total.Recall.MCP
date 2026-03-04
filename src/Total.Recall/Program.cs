using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Total.Recall.Infrastructure;
using Total.Recall.Scanners;

// Dual-mode entry point:
//   Default (no args)  → stdio MCP server (VS Code launches this)
//   "scan" subcommand  → CLI scanner that writes JSONL and exits

try
{
    var version = AppVersion.Current;
    Log.Info($"starting Total.Recall v{version}");
    Log.Info($"  PID: {Environment.ProcessId}");
    Log.Info($"  args: [{string.Join(", ", args)}]");
    Log.Info($"  cwd: {Environment.CurrentDirectory}");
    Log.Info($"  log level: {Log.Level}");
    Log.Info($"  env TOTAL_RECALL_DATA: {Environment.GetEnvironmentVariable(RepoConfig.EnvVarName) ?? "(not set)"}");
    Log.Info($"  env TOTAL_RECALL_NAMESPACE: {Environment.GetEnvironmentVariable(RepoConfig.NamespaceEnvVar) ?? "(not set)"}");
    Log.Info($"  env TOTAL_RECALL_LOG_LEVEL: {Environment.GetEnvironmentVariable(Log.LogLevelEnvVar) ?? "(not set)"}");

    if (args.Length > 0 && args[0].Equals("scan", StringComparison.OrdinalIgnoreCase))
    {
        await RunScannerAsync(args);
        return;
    }

    // ── MCP Server Mode ──
    Log.Info("mode: MCP server (stdio)");

    var builder = Host.CreateApplicationBuilder(args);

    // Suppress default console logging — it writes to stdout and corrupts the JSON-RPC transport.
    // Our Log class writes to stderr instead.
    builder.Logging.ClearProviders();

    builder.Services
        .AddMcpServer()
        .WithStdioServerTransport()
        .WithToolsFromAssembly();

    // Validate data on startup — write to stderr so it doesn't interfere with stdio JSON-RPC
    ValidateDataOnStartup();

    Log.Info("starting host...");
    await builder.Build().RunAsync();
}
catch (Exception ex)
{
    Log.Error($"FATAL: server crashed during startup: {ex.GetType().Name}: {ex.Message}");
    Log.Error(ex.StackTrace ?? "(no stack trace)");
    if (ex.InnerException is not null)
        Log.Error($"  inner: {ex.InnerException.GetType().Name}: {ex.InnerException.Message}");
    Environment.ExitCode = 1;
}

// ── Startup Validation ──
static void ValidateDataOnStartup()
{
    string dataDir;
    try
    {
        dataDir = RepoConfig.GetDataPath();
    }
    catch (Exception ex)
    {
        Log.Error($"failed to resolve data path: {ex.Message}");
        Log.Warn("server will start but tools will fail — set TOTAL_RECALL_DATA env var to a valid directory");
        return;
    }

    var ns = RepoConfig.GetDefaultNamespace();
    Log.Info($"default namespace: '{ns}'");
    Log.Info($"data dir: {dataDir}");

    // List available namespaces
    try
    {
        var namespaces = RepoConfig.ListNamespaces();
        if (namespaces.Count > 0)
            Log.Info($"  available namespaces: [{string.Join(", ", namespaces)}]");
        else
            Log.Info("  no namespace subdirectories found (using root or legacy layout)");
    }
    catch (Exception ex)
    {
        Log.Warn($"  could not enumerate namespaces: {ex.Message}");
    }

    if (!Directory.Exists(dataDir))
    {
        Log.Warn($"data dir does NOT exist: {dataDir}");
        Log.Warn("server will start but tools will return empty results — run 'total-recall scan' first");
        return;
    }

    // Pre-warm StoreRegistry caches
    void LogStore<T>(string label, Func<JsonLineStore<T>> getStore) where T : class
    {
        try
        {
            var store = getStore();
            if (store.HasData())
            {
                var count = store.LoadAll().Count;
                Log.Info($"  ✓ {label}: {count} records (cached)");
            }
            else
            {
                Log.Warn($"  ✗ {label}: empty or not found at {store.FilePath}");
            }
        }
        catch (Exception ex)
        {
            Log.Error($"  ✗ {label}: FAILED to load — {ex.GetType().Name}: {ex.Message}");
        }
    }

    LogStore("type-registry", () => StoreRegistry.TypeRegistry);
    LogStore("coverage-gaps", () => StoreRegistry.CoverageGaps);
    LogStore("test-inventory", () => StoreRegistry.TestInventory);
    LogStore("gotchas", () => StoreRegistry.Gotchas);
    LogStore("mock-recipes", () => StoreRegistry.MockRecipes);
    LogStore("assessments", () => StoreRegistry.Assessments);
    LogStore("sessions", () => StoreRegistry.Sessions);

    // Pre-build the type name index
    try
    {
        var (exact, _) = StoreRegistry.GetTypeIndex();
        Log.Info($"  ⚡ type index: {exact.Count} entries (O(1) lookups ready)");
    }
    catch (Exception ex)
    {
        Log.Error($"  ⚡ type index: FAILED — {ex.GetType().Name}: {ex.Message}");
    }

    Log.Info($"telemetry tracking active (started {Metrics.StartedUtc:yyyy-MM-dd HH:mm:ss} UTC)");
    Log.Info("startup validation complete");
}

// ── Scanner CLI Mode ──
static async Task RunScannerAsync(string[] args)
{
    Log.Info("mode: scanner CLI");

    var options = ParseScanOptions(args);

    if (options.ShowHelp)
    {
        PrintScanHelp();
        return;
    }

    // Validate: at least one scan action required
    if (string.IsNullOrEmpty(options.AssemblyPath) &&
        string.IsNullOrEmpty(options.CoveragePath) &&
        string.IsNullOrEmpty(options.TestsPath) &&
        !options.Enrich &&
        !options.Analyze &&
        !options.Watch)
    {
        Console.WriteLine("Error: At least one of --assembly, --coverage, --tests, --enrich, or --analyze is required.");
        Console.WriteLine("Run with --help for usage.");
        Environment.ExitCode = 1;
        return;
    }

    // Validate paths exist
    if (!string.IsNullOrEmpty(options.AssemblyPath) && !File.Exists(options.AssemblyPath))
    {
        Console.WriteLine($"Error: Assembly not found: {options.AssemblyPath}");
        Environment.ExitCode = 1;
        return;
    }
    if (!string.IsNullOrEmpty(options.CoveragePath))
    {
        if (Directory.Exists(options.CoveragePath))
        {
            // User passed a directory — auto-discover the latest coverage XML
            var dir = new DirectoryInfo(options.CoveragePath);
            var candidates = dir.GetFiles("coverage.cobertura.xml", SearchOption.AllDirectories);
            if (candidates.Length > 0)
            {
                var newest = candidates.OrderByDescending(f => f.LastWriteTimeUtc).First();
                Console.WriteLine($"  Resolved coverage directory to: {newest.FullName}");
                options.CoveragePath = newest.FullName;
            }
            else
            {
                Console.WriteLine($"Error: No coverage.cobertura.xml found in: {options.CoveragePath}");
                Environment.ExitCode = 1;
                return;
            }
        }
        else if (!File.Exists(options.CoveragePath))
        {
            Console.WriteLine($"Error: Coverage XML not found: {options.CoveragePath}");
            Environment.ExitCode = 1;
            return;
        }
    }
    if (!string.IsNullOrEmpty(options.TestsPath) && !Directory.Exists(options.TestsPath))
    {
        Console.WriteLine($"Error: Test directory not found: {options.TestsPath}");
        Environment.ExitCode = 1;
        return;
    }
    if (!string.IsNullOrEmpty(options.SourceRoot) && !Directory.Exists(options.SourceRoot))
    {
        Console.WriteLine($"Error: Source root directory not found: {options.SourceRoot}");
        Environment.ExitCode = 1;
        return;
    }

    // Resolve data directory
    string dataDir;
    if (!string.IsNullOrEmpty(options.OutputPath))
        dataDir = RepoConfig.GetDataPath(options.OutputPath);
    else if (!string.IsNullOrEmpty(options.NamespaceName))
        dataDir = RepoConfig.GetNamespacePath(options.NamespaceName);
    else
        dataDir = RepoConfig.GetDataPath();

    Directory.CreateDirectory(dataDir);
    Console.WriteLine($"Total.Recall Scanner v{AppVersion.Current} — output: {dataDir}");

    var scanResults = new List<string>();

    if (!string.IsNullOrEmpty(options.AssemblyPath))
    {
        try
        {
            Console.Write("  Scanning assembly... ");
            var count = AssemblyScanner.Scan(options.AssemblyPath, dataDir);
            Console.WriteLine($"✓ type-registry.jsonl — {count} types");
            scanResults.Add($"types:{count}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"✗ FAILED: {ex.GetType().Name}: {ex.Message}");
            Log.Error($"Assembly scan failed: {ex}");
        }
    }

    if (!string.IsNullOrEmpty(options.CoveragePath))
    {
        try
        {
            Console.Write("  Parsing coverage... ");
            var count = CoberturaParser.Parse(options.CoveragePath, dataDir);
            Console.WriteLine($"✓ coverage-gaps.jsonl — {count} classes");
            scanResults.Add($"coverage-classes:{count}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"✗ FAILED: {ex.GetType().Name}: {ex.Message}");
            Log.Error($"Coverage parse failed: {ex}");
        }
    }

    if (!string.IsNullOrEmpty(options.TestsPath))
    {
        try
        {
            Console.Write("  Scanning tests... ");
            var count = TestProjectScanner.Scan(options.TestsPath, dataDir);
            Console.WriteLine($"✓ test-inventory.jsonl — {count} test files");
            scanResults.Add($"test-files:{count}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"✗ FAILED: {ex.GetType().Name}: {ex.Message}");
            Log.Error($"Test scan failed: {ex}");
        }
    }

    // Enrichment: cross-reference coverage gaps with type registry + test inventory
    if (options.Enrich)
    {
        try
        {
            Console.Write("  Enriching coverage data... ");
            var enriched = EnrichCoverageGaps(dataDir);
            Console.WriteLine($"✓ {enriched} classes enriched with test counts + testability");
            scanResults.Add($"enriched:{enriched}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"✗ Enrichment FAILED: {ex.GetType().Name}: {ex.Message}");
            Log.Error($"Enrichment failed: {ex}");
        }

        // Auto-generate mock recipes for popular interfaces (5+ consumers)
        try
        {
            Console.Write("  Auto-generating mock recipes... ");
            var newRecipes = AutoGenerateMockRecipes(dataDir);
            if (newRecipes > 0)
            {
                Console.WriteLine($"✓ {newRecipes} new mock recipe(s) generated");
                scanResults.Add($"mock-recipes-generated:{newRecipes}");
            }
            else
            {
                Console.WriteLine("✓ no new recipes needed");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"✗ Mock recipe generation FAILED: {ex.GetType().Name}: {ex.Message}");
            Log.Error($"Mock recipe auto-generation failed: {ex}");
        }
    }

    // Static analysis: dependency graph, coupling metrics, cluster detection
    if (options.Analyze)
    {
        try
        {
            Console.Write("  Running static analysis... ");
            var (metricsCount, edgeCount) = DependencyAnalyzer.Analyze(dataDir);
            Console.WriteLine($"✓ {metricsCount} classes analyzed, {edgeCount} dependency edges, see dependency-graph.md");
            scanResults.Add($"metrics:{metricsCount}");
            scanResults.Add($"edges:{edgeCount}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"✗ Analysis FAILED: {ex.GetType().Name}: {ex.Message}");
            Log.Error($"Static analysis failed: {ex}");
        }
    }

    // Write config.json with scan metadata
    WriteConfig(dataDir, options);

    Console.WriteLine($"Done. [{string.Join(", ", scanResults)}]");

    // Watch mode: keep running and re-scan on file changes
    if (options.Watch)
    {
        // Pass enrichment/analysis as delegates (local functions can't be accessed externally)
        Func<string, int>? enrichFunc = options.Enrich ? EnrichCoverageGaps : null;
        Func<string, (int, int)>? analyzeFunc = options.Analyze
            ? (dir) => DependencyAnalyzer.Analyze(dir)
            : null;

        using var watcher = new ScannerWatcher(
            dataDir,
            options.AssemblyPath,
            options.CoveragePath,
            options.TestsPath,
            enrichFunc,
            analyzeFunc);

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };

        await watcher.WatchAsync(cts.Token);
    }
}

/// <summary>
/// Cross-reference coverage gaps with type registry and test inventory
/// to fill in existingTestCount and testability fields.
/// </summary>
static int EnrichCoverageGaps(string dataDir)
{
    var coverageStore = new JsonLineStore<Total.Recall.Models.CoverageGap>(RepoConfig.CoverageGapsPath(dataDir));
    if (!coverageStore.HasData())
        return 0;

    var gaps = coverageStore.LoadAll();

    // Load type registry for testability heuristics
    var typeStore = new JsonLineStore<Total.Recall.Models.TypeRecord>(RepoConfig.TypeRegistryPath(dataDir));
    var typeMap = typeStore.HasData()
        ? typeStore.LoadAll()
            .GroupBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase)
        : new Dictionary<string, Total.Recall.Models.TypeRecord>(StringComparer.OrdinalIgnoreCase);

    // Load test inventory for test counts
    var testStore = new JsonLineStore<Total.Recall.Models.TestInventoryEntry>(RepoConfig.TestInventoryPath(dataDir));
    var testMap = testStore.HasData()
        ? testStore.LoadAll()
            .GroupBy(t => t.Class, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase)
        : new Dictionary<string, Total.Recall.Models.TestInventoryEntry>(StringComparer.OrdinalIgnoreCase);

    var enrichedCount = 0;
    foreach (var gap in gaps)
    {
        // Enrich test count
        if (testMap.TryGetValue(gap.Class, out var testEntry))
        {
            gap.ExistingTestCount = testEntry.TestCount;
            enrichedCount++;
        }

        // Enrich testability based on type metadata
        if (typeMap.TryGetValue(gap.Class, out var typeRecord))
        {
            gap.Testability = ClassifyTestability(typeRecord);
        }
    }

    coverageStore.WriteAll(gaps);
    return enrichedCount;
}

/// <summary>
/// Auto-generate basic mock recipes for interfaces that appear as constructor parameters
/// in 5+ classes. Only generates for interfaces that don't already have a mock recipe.
/// </summary>
static int AutoGenerateMockRecipes(string dataDir)
{
    var typeStore = new JsonLineStore<Total.Recall.Models.TypeRecord>(RepoConfig.TypeRegistryPath(dataDir));
    if (!typeStore.HasData())
        return 0;

    var types = typeStore.LoadAll();

    // Count how many classes use each interface as a ctor param
    var interfaceConsumerCounts = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
    foreach (var type in types)
    {
        if (type.IsInterface || type.IsAbstract)
            continue;

        foreach (var ctor in type.Constructors)
        {
            foreach (var param in ctor.Params)
            {
                var paramType = ParamHelper.ExtractTypeName(param);
                if (ParamHelper.IsInterfaceLike(paramType))
                {
                    if (!interfaceConsumerCounts.ContainsKey(paramType))
                        interfaceConsumerCounts[paramType] = [];
                    if (!interfaceConsumerCounts[paramType].Contains(type.Name, StringComparer.OrdinalIgnoreCase))
                        interfaceConsumerCounts[paramType].Add(type.Name);
                }
            }
        }
    }

    // Load existing mock recipes to avoid duplicates
    var recipeStore = new JsonLineStore<Total.Recall.Models.MockRecipe>(RepoConfig.MockRecipesPath(dataDir));
    var existingRecipes = recipeStore.HasData()
        ? recipeStore.LoadAll()
            .Select(r => r.Interface)
            .ToHashSet(StringComparer.OrdinalIgnoreCase)
        : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    // Find the interface type records (for namespace lookup)
    var interfaceTypes = types
        .Where(t => t.IsInterface)
        .ToDictionary(t => t.Name, t => t, StringComparer.OrdinalIgnoreCase);

    // Generate recipes for popular interfaces (5+ consumers) without existing recipes
    var newRecipes = new List<Total.Recall.Models.MockRecipe>();
    foreach (var (iface, consumers) in interfaceConsumerCounts)
    {
        if (consumers.Count < 5)
            continue;
        if (existingRecipes.Contains(iface))
            continue;

        // Find our type record for namespace info
        interfaceTypes.TryGetValue(iface, out var ifaceRecord);
        var ns = ifaceRecord?.Namespace ?? "";

        // Build basic recipe: new Mock<IFoo>() with no complex setup
        var cleanName = iface.StartsWith("I") && iface.Length > 1 && char.IsUpper(iface[1])
            ? iface[1..] : iface;
        var varName = $"mock{cleanName}";

        var recipe = new Total.Recall.Models.MockRecipe
        {
            Interface = iface,
            Namespace = ns,
            RequiredUsings = string.IsNullOrEmpty(ns) ? ["Moq"] : ["Moq", $"using {ns}"],
            Recipe = $"var {varName} = new Mock<{iface}>();",
            Gotchas = [],
            UsedByClasses = consumers.Take(10).ToList()
        };

        newRecipes.Add(recipe);
        Log.Debug($"[AutoMockRecipes] generated recipe for {iface} (used by {consumers.Count} classes: {string.Join(", ", consumers.Take(5))})");
    }

    if (newRecipes.Count > 0)
    {
        foreach (var recipe in newRecipes)
            recipeStore.Append(recipe);
    }

    return newRecipes.Count;
}

static string ClassifyTestability(Total.Recall.Models.TypeRecord type)
{
    // Heuristic testability classification
    if (type.IsAbstract || type.IsInterface)
        return "low";

    if (type.IsStatic)
        return "medium"; // static classes can be tested but need special handling

    var maxCtorParams = type.Constructors.Count > 0
        ? type.Constructors.Max(c => c.Params.Count)
        : 0;

    if (maxCtorParams == 0)
        return "high"; // parameterless = very easy to test

    if (maxCtorParams <= 3)
        return "high";

    if (maxCtorParams <= 6)
        return "medium";

    return "low"; // heavy DI = hard to test
}

static void WriteConfig(string dataDir, ScanOptions options)
{
    var config = new Total.Recall.Models.NamespaceConfig
    {
        SourceRoot = options.SourceRoot,
        ScannedUtc = DateTime.UtcNow.ToString("o"),
        AssemblyPath = options.AssemblyPath,
        CoveragePath = options.CoveragePath,
        TestsPath = options.TestsPath
    };

    // Parse test framework if specified
    if (!string.IsNullOrEmpty(options.TestFramework) &&
        Enum.TryParse<Total.Recall.Models.TestFramework>(options.TestFramework, ignoreCase: true, out var fw))
        config.TestFramework = fw;

    // Parse mock library if specified
    if (!string.IsNullOrEmpty(options.MockLibrary) &&
        Enum.TryParse<Total.Recall.Models.MockLibrary>(options.MockLibrary, ignoreCase: true, out var ml))
        config.MockLibrary = ml;

    // Set test namespace pattern if specified
    if (!string.IsNullOrEmpty(options.TestNamespacePattern))
        config.TestNamespacePattern = options.TestNamespacePattern;

    // Merge with existing config to preserve settings not specified this run
    var configPath = RepoConfig.ConfigJsonPath(dataDir);
    if (File.Exists(configPath))
    {
        try
        {
            var existing = System.Text.Json.JsonSerializer.Deserialize<Total.Recall.Models.NamespaceConfig>(
                File.ReadAllText(configPath), SharedJsonOptions.CamelCase);
            if (existing is not null)
            {
                if (string.IsNullOrEmpty(options.SourceRoot) && existing.SourceRoot is not null)
                    config.SourceRoot = existing.SourceRoot;
                if (string.IsNullOrEmpty(options.TestFramework))
                    config.TestFramework = existing.TestFramework;
                if (string.IsNullOrEmpty(options.MockLibrary))
                    config.MockLibrary = existing.MockLibrary;
                if (string.IsNullOrEmpty(options.TestNamespacePattern) && existing.TestNamespacePattern != "{Namespace}.Tests")
                    config.TestNamespacePattern = existing.TestNamespacePattern;
            }
        }
        catch { /* ignore corrupt config */ }
    }

    var json = System.Text.Json.JsonSerializer.Serialize(config, SharedJsonOptions.CamelCaseIndented);
    File.WriteAllText(configPath, json);
    Console.WriteLine($"  ✓ config.json updated");
}

static ScanOptions ParseScanOptions(string[] args)
{
    var options = new ScanOptions();

    for (int i = 1; i < args.Length; i++)
    {
        var arg = args[i].ToLowerInvariant();
        switch (arg)
        {
            case "--assembly" when i + 1 < args.Length:
                options.AssemblyPath = args[++i];
                break;
            case "--coverage" when i + 1 < args.Length:
                options.CoveragePath = args[++i];
                break;
            case "--tests" when i + 1 < args.Length:
                options.TestsPath = args[++i];
                break;
            case "--output" when i + 1 < args.Length:
                options.OutputPath = args[++i];
                break;
            case "--namespace" when i + 1 < args.Length:
                options.NamespaceName = args[++i];
                break;
            case "--source-root" when i + 1 < args.Length:
                options.SourceRoot = args[++i];
                break;
            case "--enrich":
                options.Enrich = true;
                break;
            case "--analyze":
                options.Analyze = true;
                break;
            case "--watch":
                options.Watch = true;
                break;
            case "--test-framework" when i + 1 < args.Length:
                options.TestFramework = args[++i];
                break;
            case "--mock-library" when i + 1 < args.Length:
                options.MockLibrary = args[++i];
                break;
            case "--test-namespace-pattern" when i + 1 < args.Length:
                options.TestNamespacePattern = args[++i];
                break;
            case "--help" or "-h":
                options.ShowHelp = true;
                break;
            default:
                if (arg.StartsWith("--"))
                    Console.WriteLine($"Warning: unknown option '{args[i]}'");
                break;
        }
    }

    return options;
}

static void PrintScanHelp()
{
    Console.WriteLine($$"""
        Total.Recall Scanner v{{AppVersion.Current}}

        Usage: dotnet run -- scan [options]

        Options:
          --assembly <path>      Path to target .NET assembly (.dll) for type registry scan
          --coverage <path>      Path to Cobertura XML coverage report for coverage gaps
          --tests <path>         Path to test project directory for test inventory scan
          --source-root <path>   Path to target repo source root (enables get_source_snippet)
          --output <path>        Override data output directory
          --namespace <name>     Namespace subdirectory under TOTAL_RECALL_DATA root
          --enrich               Cross-reference coverage with type registry + test inventory
          --analyze              Run static analysis: dependency graph, coupling metrics, clusters
          --watch                Watch mode: re-scan automatically when files change (Ctrl+C to stop)
          --test-framework <fw>  Test framework: xunit (default), nunit, mstest
          --mock-library <lib>   Mock library: moq (default), nsubstitute, fakeiteasy
          --test-namespace-pattern <pat>  Namespace pattern for test classes (default: "{Namespace}.Tests")
                                          Use {Namespace} for full, {RootNamespace}/{Rest} for split
          --help, -h             Show this help

        Examples:
          # Full scan with source root
          dotnet run -- scan \
            --assembly "path/to/Server.dll" \
            --coverage "path/to/coverage.cobertura.xml" \
            --tests "path/to/UnitTest" \
            --source-root "path/to/Server/src" \
            --namespace linter \
            --enrich --analyze

          # Full scan for an NUnit + NSubstitute project
          dotnet run -- scan \
            --assembly "path/to/MyApp.dll" \
            --tests "path/to/MyApp.Tests" \
            --namespace myapp \
            --test-framework nunit \
            --mock-library nsubstitute \
            --enrich

          # Just re-parse coverage after a new test run
          dotnet run -- scan --coverage "path/to/coverage.cobertura.xml" --namespace linter --enrich

          # Watch mode: auto-rescan on file changes
          dotnet run -- scan \
            --assembly "path/to/Server.dll" \
            --coverage "path/to/coverage.cobertura.xml" \
            --tests "path/to/UnitTest" \
            --namespace linter \
            --enrich --analyze --watch

          # Enrich existing data without re-scanning
          dotnet run -- scan --namespace linter --enrich

        Environment:
          TOTAL_RECALL_DATA       Root data directory (default: "data")
          TOTAL_RECALL_NAMESPACE  Default namespace (default: "default")
        """);
}

record ScanOptions
{
    public string? AssemblyPath { get; set; }
    public string? CoveragePath { get; set; }
    public string? TestsPath { get; set; }
    public string? OutputPath { get; set; }
    public string? NamespaceName { get; set; }
    public string? SourceRoot { get; set; }
    public bool Enrich { get; set; }
    public bool Analyze { get; set; }
    public bool Watch { get; set; }
    public bool ShowHelp { get; set; }
    public string? TestFramework { get; set; }
    public string? MockLibrary { get; set; }
    public string? TestNamespacePattern { get; set; }
}
