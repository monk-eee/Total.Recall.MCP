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
    Log.Info("starting Total.Recall");
    Log.Info($"  PID: {Environment.ProcessId}");
    Log.Info($"  args: [{string.Join(", ", args)}]");
    Log.Info($"  cwd: {Environment.CurrentDirectory}");
    Log.Info($"  env TOTAL_RECALL_DATA: {Environment.GetEnvironmentVariable(RepoConfig.EnvVarName) ?? "(not set)"}");

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

    Log.Info($"data dir: {dataDir}");

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

    Log.Info("startup validation complete");
}

// ── Scanner CLI Mode ──
static async Task RunScannerAsync(string[] args)
{
    Log.Info("mode: scanner CLI");

    string? assemblyPath = null;
    string? coveragePath = null;
    string? testsPath = null;
    string? outputPath = null;

    for (int i = 1; i < args.Length - 1; i++)
    {
        switch (args[i].ToLowerInvariant())
        {
            case "--assembly":
                assemblyPath = args[++i];
                break;
            case "--coverage":
                coveragePath = args[++i];
                break;
            case "--tests":
                testsPath = args[++i];
                break;
            case "--output":
                outputPath = args[++i];
                break;
        }
    }

    var dataDir = RepoConfig.GetDataPath(outputPath);
    Directory.CreateDirectory(dataDir);

    Console.WriteLine($"Total.Recall Scanner — output: {dataDir}");

    if (!string.IsNullOrEmpty(assemblyPath))
    {
        try
        {
            Console.Write("Scanning assembly... ");
            var count = AssemblyScanner.Scan(assemblyPath, dataDir);
            Console.WriteLine($"✓ type-registry.jsonl — {count} types");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"✗ FAILED: {ex.GetType().Name}: {ex.Message}");
            Log.Error($"Assembly scan failed: {ex}");
        }
    }

    if (!string.IsNullOrEmpty(coveragePath))
    {
        try
        {
            Console.Write("Parsing coverage... ");
            var count = CoberturaParser.Parse(coveragePath, dataDir);
            Console.WriteLine($"✓ coverage-gaps.jsonl — {count} classes");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"✗ FAILED: {ex.GetType().Name}: {ex.Message}");
            Log.Error($"Coverage parse failed: {ex}");
        }
    }

    if (!string.IsNullOrEmpty(testsPath))
    {
        try
        {
            Console.Write("Scanning tests... ");
            var count = TestProjectScanner.Scan(testsPath, dataDir);
            Console.WriteLine($"✓ test-inventory.jsonl — {count} test files");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"✗ FAILED: {ex.GetType().Name}: {ex.Message}");
            Log.Error($"Test scan failed: {ex}");
        }
    }

    Console.WriteLine("Done.");
    await Task.CompletedTask;
}
