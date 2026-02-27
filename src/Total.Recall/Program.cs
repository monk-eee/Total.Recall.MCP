using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Total.Recall.Infrastructure;
using Total.Recall.Scanners;

// Dual-mode entry point:
//   Default (no args)  → stdio MCP server (VS Code launches this)
//   "scan" subcommand  → CLI scanner that writes JSONL and exits

if (args.Length > 0 && args[0].Equals("scan", StringComparison.OrdinalIgnoreCase))
{
    await RunScannerAsync(args);
    return;
}

// ── MCP Server Mode ──
var builder = Host.CreateApplicationBuilder(args);
builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly();

// Validate data on startup — write to stderr so it doesn't interfere with stdio JSON-RPC
ValidateDataOnStartup();

await builder.Build().RunAsync();

// ── Startup Validation ──
static void ValidateDataOnStartup()
{
    var dataDir = RepoConfig.GetDataPath();
    var files = new (string Label, string Path)[]
    {
        ("type-registry", RepoConfig.TypeRegistryPath(dataDir)),
        ("coverage-gaps", RepoConfig.CoverageGapsPath(dataDir)),
        ("test-inventory", RepoConfig.TestInventoryPath(dataDir)),
        ("gotchas", RepoConfig.GotchasPath(dataDir)),
        ("mock-recipes", RepoConfig.MockRecipesPath(dataDir)),
    };

    Console.Error.WriteLine($"[Total.Recall] data dir: {dataDir}");
    foreach (var (label, path) in files)
    {
        if (File.Exists(path))
        {
            var lines = File.ReadLines(path).Count(l => !string.IsNullOrWhiteSpace(l));
            Console.Error.WriteLine($"  ✓ {label}: {lines} records");
        }
        else
        {
            Console.Error.WriteLine($"  ✗ {label}: NOT FOUND");
        }
    }
}

// ── Scanner CLI Mode ──
static async Task RunScannerAsync(string[] args)
{
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
        Console.Write("Scanning assembly... ");
        var count = AssemblyScanner.Scan(assemblyPath, dataDir);
        Console.WriteLine($"✓ type-registry.jsonl — {count} types");
    }

    if (!string.IsNullOrEmpty(coveragePath))
    {
        Console.Write("Parsing coverage... ");
        var count = CoberturaParser.Parse(coveragePath, dataDir);
        Console.WriteLine($"✓ coverage-gaps.jsonl — {count} classes");
    }

    if (!string.IsNullOrEmpty(testsPath))
    {
        Console.Write("Scanning tests... ");
        var count = TestProjectScanner.Scan(testsPath, dataDir);
        Console.WriteLine($"✓ test-inventory.jsonl — {count} test files");
    }

    Console.WriteLine("Done.");
    await Task.CompletedTask;
}
