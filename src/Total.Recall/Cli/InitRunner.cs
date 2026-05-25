using System.Text.Json;
using Total.Recall.Infrastructure;
using Total.Recall.Models;

namespace Total.Recall.Cli;

/// <summary>
/// CLI sub-command <c>init</c>. Walks a target repo, auto-discovers the
/// production assembly / coverage XML / test project / source root, writes
/// <c>config.json</c> into the resolved namespace data directory, and prints
/// a ready-to-paste <c>.vscode/mcp.json</c> block. Does NOT run the scanner —
/// the user runs <c>scan</c> afterwards. Output is plain text suitable for a
/// terminal; not JSON.
/// </summary>
internal static class InitRunner
{
    /// <summary>
    /// Run the <c>init</c> sub-command. Returns 0 on success, 1 on usage error,
    /// 2 on filesystem error.
    /// </summary>
    public static int RunInit(string[] args, TextWriter stdout)
    {
        var opts = ParseOptions(args);

        if (opts.ShowHelp)
        {
            WriteHelp(stdout);
            return 0;
        }

        if (string.IsNullOrWhiteSpace(opts.RepoPath))
        {
            stdout.WriteLine("Error: <repo-path> is required.");
            stdout.WriteLine();
            WriteHelp(stdout);
            return 1;
        }

        DiscoveryResult discovery;
        try
        {
            discovery = RepoDiscovery.Discover(opts.RepoPath);
        }
        catch (DirectoryNotFoundException ex)
        {
            stdout.WriteLine($"Error: {ex.Message}");
            return 2;
        }
        catch (Exception ex)
        {
            stdout.WriteLine($"Error discovering repo layout: {ex.GetType().Name}: {ex.Message}");
            return 2;
        }

        var ns = string.IsNullOrWhiteSpace(opts.NamespaceName)
            ? discovery.SuggestedNamespace
            : opts.NamespaceName!.Trim();

        var dataRoot = string.IsNullOrWhiteSpace(opts.DataRoot)
            ? RepoConfig.GetRootPath()
            : Path.GetFullPath(opts.DataRoot!);
        var dataDir = Path.Combine(dataRoot, ns);

        // Write config.json (merging with any existing one so we don't blow away
        // a hand-edited test-framework / mock-library setting).
        try
        {
            Directory.CreateDirectory(dataDir);
            WriteOrMergeConfig(dataDir, discovery);
        }
        catch (Exception ex)
        {
            stdout.WriteLine($"Error writing config.json to {dataDir}: {ex.GetType().Name}: {ex.Message}");
            return 2;
        }

        PrintReport(stdout, discovery, ns, dataRoot, dataDir);
        return 0;
    }

    internal static void WriteOrMergeConfig(string dataDir, DiscoveryResult discovery)
    {
        var configPath = RepoConfig.ConfigJsonPath(dataDir);
        NamespaceConfig config = new()
        {
            SourceRoot = discovery.SourceRoot,
            AssemblyPath = discovery.AssemblyPath,
            CoveragePath = discovery.CoveragePath,
            TestsPath = discovery.TestsPath,
            ScannedUtc = null // not scanned yet; the scanner stamps this
        };

        if (File.Exists(configPath))
        {
            try
            {
                var existing = JsonSerializer.Deserialize<NamespaceConfig>(
                    File.ReadAllText(configPath), SharedJsonOptions.CamelCase);
                if (existing is not null)
                {
                    // Preserve a previous successful scan timestamp; the user may
                    // want to know when their data was last refreshed.
                    config.ScannedUtc = existing.ScannedUtc;
                    config.TestFramework = existing.TestFramework;
                    config.MockLibrary = existing.MockLibrary;
                    if (!string.IsNullOrEmpty(existing.TestNamespacePattern))
                        config.TestNamespacePattern = existing.TestNamespacePattern;
                }
            }
            catch
            {
                // corrupt config — overwrite
            }
        }

        var json = JsonSerializer.Serialize(config, SharedJsonOptions.CamelCaseIndented);
        File.WriteAllText(configPath, json);
    }

    internal static void PrintReport(
        TextWriter stdout,
        DiscoveryResult discovery,
        string ns,
        string dataRoot,
        string dataDir)
    {
        stdout.WriteLine($"Total.Recall init v{AppVersion.Current}");
        stdout.WriteLine();
        stdout.WriteLine("── Discovered ──");
        stdout.WriteLine($"  repo root        : {discovery.RepoRoot}");
        stdout.WriteLine($"  source root      : {discovery.SourceRoot}");
        stdout.WriteLine($"  production csproj: {Display(discovery.ProductionCsproj)}");
        stdout.WriteLine($"  test csproj      : {Display(discovery.TestCsproj)}");
        stdout.WriteLine($"  assembly (.dll)  : {Display(discovery.AssemblyPath)}");
        stdout.WriteLine($"  coverage XML     : {Display(discovery.CoveragePath)}");
        stdout.WriteLine($"  tests directory  : {Display(discovery.TestsPath)}");
        stdout.WriteLine();
        stdout.WriteLine("── Resolved ──");
        stdout.WriteLine($"  namespace        : {ns}");
        stdout.WriteLine($"  data root        : {dataRoot}");
        stdout.WriteLine($"  data dir         : {dataDir}");
        stdout.WriteLine($"  config.json      : {RepoConfig.ConfigJsonPath(dataDir)} (written)");
        stdout.WriteLine();

        // Warnings about missing artefacts
        var warnings = new List<string>();
        if (discovery.AssemblyPath is null)
            warnings.Add("No production .dll found under bin/. Build the project before running 'scan'.");
        if (discovery.CoveragePath is null)
            warnings.Add("No coverage.cobertura.xml found. Run 'dotnet test --collect:\"XPlat Code Coverage\"' to generate one.");
        if (discovery.TestsPath is null)
            warnings.Add("No test project detected. Coverage and test-inventory tools will be limited.");
        if (warnings.Count > 0)
        {
            stdout.WriteLine("── Warnings ──");
            foreach (var w in warnings)
                stdout.WriteLine($"  ! {w}");
            stdout.WriteLine();
        }

        stdout.WriteLine("── Suggested .vscode/mcp.json (paste into your target workspace) ──");
        stdout.WriteLine();
        stdout.WriteLine(BuildMcpJson(ns, dataRoot, discovery.SourceRoot));
        stdout.WriteLine();

        stdout.WriteLine("── Next steps ──");
        var scanCmd = BuildScanCommand(discovery, ns);
        stdout.WriteLine($"  1. Run the scanner to populate data:");
        stdout.WriteLine($"       {scanCmd}");
        stdout.WriteLine($"  2. Add the JSON block above to .vscode/mcp.json in your target workspace.");
        stdout.WriteLine($"  3. Restart VS Code, then ask Copilot: \"get testable targets, top 5\".");
        stdout.WriteLine($"  4. Run 'total-recall doctor --ns {ns}' anytime to verify the install.");
    }

    internal static string BuildMcpJson(string ns, string dataRoot, string sourceRoot)
    {
        var dataRootJson = JsonEncode(dataRoot);
        var sourceRootJson = JsonEncode(sourceRoot);
        var nsJson = JsonEncode(ns);
        return $$"""
            {
              "servers": {
                "Total.Recall": {
                  "type": "stdio",
                  "command": "total-recall",
                  "env": {
                    "TOTAL_RECALL_DATA": {{dataRootJson}},
                    "TOTAL_RECALL_NAMESPACE": {{nsJson}},
                    "TOTAL_RECALL_LOG_LEVEL": "info",
                    "TOTAL_RECALL_SOURCE_ROOT": {{sourceRootJson}}
                  }
                }
              }
            }
            """;
    }

    internal static string BuildScanCommand(DiscoveryResult discovery, string ns)
    {
        var parts = new List<string> { "total-recall scan" };
        if (discovery.AssemblyPath is not null)
            parts.Add($"--assembly \"{discovery.AssemblyPath}\"");
        if (discovery.CoveragePath is not null)
            parts.Add($"--coverage \"{discovery.CoveragePath}\"");
        if (discovery.TestsPath is not null)
            parts.Add($"--tests \"{discovery.TestsPath}\"");
        parts.Add($"--source-root \"{discovery.SourceRoot}\"");
        parts.Add($"--namespace {ns}");
        parts.Add("--enrich");
        return string.Join(" ", parts);
    }

    private static string JsonEncode(string s) => JsonSerializer.Serialize(s);

    private static string Display(string? path) => string.IsNullOrEmpty(path) ? "(not found)" : path;

    internal sealed class InitOptions
    {
        public string? RepoPath { get; set; }
        public string? NamespaceName { get; set; }
        public string? DataRoot { get; set; }
        public bool ShowHelp { get; set; }
    }

    internal static InitOptions ParseOptions(string[] args)
    {
        var opts = new InitOptions();
        // args[0] is "init"; parse from index 1.
        for (int i = 1; i < args.Length; i++)
        {
            var a = args[i];
            var lower = a.ToLowerInvariant();
            switch (lower)
            {
                case "--namespace" when i + 1 < args.Length:
                case "--ns" when i + 1 < args.Length:
                    opts.NamespaceName = args[++i];
                    break;
                case "--data-root" when i + 1 < args.Length:
                case "--output" when i + 1 < args.Length:
                    opts.DataRoot = args[++i];
                    break;
                case "--help":
                case "-h":
                    opts.ShowHelp = true;
                    break;
                default:
                    if (a.StartsWith('-'))
                    {
                        // unknown flag — ignore (forward-compat)
                    }
                    else if (opts.RepoPath is null)
                    {
                        opts.RepoPath = a;
                    }
                    break;
            }
        }
        return opts;
    }

    private static void WriteHelp(TextWriter stdout)
    {
        stdout.WriteLine("Usage: total-recall init <repo-path> [options]");
        stdout.WriteLine();
        stdout.WriteLine("Auto-discovers the layout of a target .NET repo, writes config.json,");
        stdout.WriteLine("and prints a ready-to-paste .vscode/mcp.json block. Does NOT run the");
        stdout.WriteLine("scanner — run 'total-recall scan' (the printed command) afterwards.");
        stdout.WriteLine();
        stdout.WriteLine("Arguments:");
        stdout.WriteLine("  <repo-path>          Path to the target repo's root directory");
        stdout.WriteLine();
        stdout.WriteLine("Options:");
        stdout.WriteLine("  --namespace <name>   Namespace subdirectory (default: derived from repo dir name)");
        stdout.WriteLine("  --data-root <path>   Override data root (default: TOTAL_RECALL_DATA env var or 'data')");
        stdout.WriteLine("  --help, -h           Show this help");
        stdout.WriteLine();
        stdout.WriteLine("Examples:");
        stdout.WriteLine("  total-recall init C:\\repos\\MyProject");
        stdout.WriteLine("  total-recall init . --namespace myproject");
        stdout.WriteLine("  total-recall init ../OtherRepo --data-root C:\\total-recall-data");
    }
}
