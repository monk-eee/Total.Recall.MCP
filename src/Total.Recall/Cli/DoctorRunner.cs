using System.Text.Json;
using Total.Recall.Infrastructure;
using Total.Recall.Models;

namespace Total.Recall.Cli;

/// <summary>
/// CLI sub-command <c>doctor</c>. Reports environment, data root resolution,
/// namespaces, per-namespace data file presence + record counts, and config.json
/// path validity. Designed as the first command a user runs when "something
/// isn't working" — surfaces missing data, stale paths, and unset env vars.
/// Exit codes: 0 healthy, 1 warnings, 2 errors (data root missing entirely).
/// </summary>
internal static class DoctorRunner
{
    public static int RunDoctor(string[] args, TextWriter stdout)
    {
        var opts = ParseOptions(args);

        if (opts.ShowHelp)
        {
            WriteHelp(stdout);
            return 0;
        }

        var report = BuildReport(opts.NamespaceFilter);
        WriteReport(stdout, report);
        return report.ExitCode;
    }

    internal static DoctorReport BuildReport(string? namespaceFilter)
    {
        var envVars = new Dictionary<string, string?>
        {
            [RepoConfig.EnvVarName] = Environment.GetEnvironmentVariable(RepoConfig.EnvVarName),
            [RepoConfig.NamespaceEnvVar] = Environment.GetEnvironmentVariable(RepoConfig.NamespaceEnvVar),
            ["TOTAL_RECALL_SOURCE_ROOT"] = Environment.GetEnvironmentVariable("TOTAL_RECALL_SOURCE_ROOT"),
            ["TOTAL_RECALL_LOG_LEVEL"] = Environment.GetEnvironmentVariable("TOTAL_RECALL_LOG_LEVEL"),
            ["TOTAL_RECALL_MODE"] = Environment.GetEnvironmentVariable("TOTAL_RECALL_MODE")
        };

        var dataRoot = RepoConfig.GetRootPath();
        var dataRootExists = Directory.Exists(dataRoot);

        var namespaces = new List<NamespaceHealth>();
        var warnings = 0;
        var errors = 0;

        if (!dataRootExists)
        {
            errors++;
        }
        else
        {
            var nsList = RepoConfig.ListNamespaces();
            if (namespaceFilter is not null)
            {
                nsList = nsList
                    .Where(n => string.Equals(n, namespaceFilter, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }

            if (nsList.Count == 0)
            {
                warnings++; // data root exists but contains no scanned namespaces
            }

            foreach (var ns in nsList)
            {
                var dir = RepoConfig.IsLegacyLayout(dataRoot)
                    ? dataRoot
                    : Path.Combine(dataRoot, ns);

                var health = InspectNamespace(ns, dir);
                namespaces.Add(health);
                warnings += health.Warnings.Count;
            }
        }

        var exitCode = errors > 0 ? 2 : warnings > 0 ? 1 : 0;

        return new DoctorReport(
            EnvVars: envVars,
            DataRoot: dataRoot,
            DataRootExists: dataRootExists,
            Namespaces: namespaces,
            Warnings: warnings,
            Errors: errors,
            ExitCode: exitCode);
    }

    private static NamespaceHealth InspectNamespace(string ns, string dir)
    {
        var files = new List<DataFileStatus>();
        foreach (var (name, path) in EnumerateDataFiles(dir))
        {
            files.Add(InspectFile(name, path));
        }

        var configPath = RepoConfig.ConfigJsonPath(dir);
        var configStatus = InspectConfig(configPath);

        var warnings = new List<string>();
        // Missing core data files
        foreach (var f in files)
        {
            if (!f.Exists && IsCoreFile(f.Name))
                warnings.Add($"missing core data file: {f.Name}");
        }
        if (configStatus.Exists)
        {
            if (configStatus.SourceRootMissing) warnings.Add("config.json: sourceRoot does not exist");
            if (configStatus.AssemblyMissing) warnings.Add("config.json: assemblyPath does not exist");
            if (configStatus.CoverageMissing) warnings.Add("config.json: coveragePath does not exist");
            if (configStatus.TestsMissing) warnings.Add("config.json: testsPath does not exist");
        }
        else
        {
            warnings.Add("no config.json — 'init' has not been run for this namespace");
        }

        return new NamespaceHealth(
            Name: ns,
            Directory: dir,
            Files: files,
            Config: configStatus,
            Warnings: warnings);
    }

    private static IEnumerable<(string name, string path)> EnumerateDataFiles(string dir)
    {
        yield return ("type-registry.jsonl", RepoConfig.TypeRegistryPath(dir));
        yield return ("mock-recipes.jsonl", RepoConfig.MockRecipesPath(dir));
        yield return ("coverage-gaps.jsonl", RepoConfig.CoverageGapsPath(dir));
        yield return ("gotchas.jsonl", RepoConfig.GotchasPath(dir));
        yield return ("test-inventory.jsonl", RepoConfig.TestInventoryPath(dir));
        yield return ("assessments.jsonl", RepoConfig.AssessmentsPath(dir));
        yield return ("sessions.jsonl", RepoConfig.SessionsPath(dir));
        yield return ("tool-calls.jsonl", RepoConfig.ToolCallsPath(dir));
        yield return ("tasks.jsonl", RepoConfig.TasksPath(dir));
        yield return ("cycles.jsonl", RepoConfig.CyclesPath(dir));
        yield return ("challenges.jsonl", RepoConfig.ChallengesPath(dir));
        yield return ("evals.jsonl", RepoConfig.EvalsPath(dir));
    }

    private static bool IsCoreFile(string name) => name switch
    {
        "type-registry.jsonl" => true,
        "coverage-gaps.jsonl" => true,
        "test-inventory.jsonl" => true,
        _ => false
    };

    private static DataFileStatus InspectFile(string name, string path)
    {
        if (!File.Exists(path))
            return new DataFileStatus(name, path, Exists: false, RecordCount: 0, LastWriteUtc: null);

        long count = 0;
        try
        {
            foreach (var line in File.ReadLines(path))
            {
                if (!string.IsNullOrWhiteSpace(line)) count++;
            }
        }
        catch
        {
            // unreadable — treat as zero-count
        }
        var fi = new FileInfo(path);
        return new DataFileStatus(name, path, Exists: true, RecordCount: count, LastWriteUtc: fi.LastWriteTimeUtc);
    }

    private static ConfigStatus InspectConfig(string path)
    {
        if (!File.Exists(path))
            return new ConfigStatus(path, Exists: false, null, null, null, null, null,
                false, false, false, false);

        try
        {
            var json = File.ReadAllText(path);
            var cfg = JsonSerializer.Deserialize<NamespaceConfig>(json, SharedJsonOptions.CamelCase);
            if (cfg is null)
                return new ConfigStatus(path, Exists: true, null, null, null, null, null,
                    false, false, false, false);

            return new ConfigStatus(
                Path: path,
                Exists: true,
                SourceRoot: cfg.SourceRoot,
                AssemblyPath: cfg.AssemblyPath,
                CoveragePath: cfg.CoveragePath,
                TestsPath: cfg.TestsPath,
                ScannedUtc: cfg.ScannedUtc,
                SourceRootMissing: !string.IsNullOrEmpty(cfg.SourceRoot) && !Directory.Exists(cfg.SourceRoot),
                AssemblyMissing: !string.IsNullOrEmpty(cfg.AssemblyPath) && !File.Exists(cfg.AssemblyPath),
                CoverageMissing: !string.IsNullOrEmpty(cfg.CoveragePath) && !File.Exists(cfg.CoveragePath),
                TestsMissing: !string.IsNullOrEmpty(cfg.TestsPath) && !Directory.Exists(cfg.TestsPath));
        }
        catch
        {
            return new ConfigStatus(path, Exists: true, null, null, null, null, null,
                false, false, false, false);
        }
    }

    internal static void WriteReport(TextWriter stdout, DoctorReport report)
    {
        stdout.WriteLine($"Total.Recall doctor v{AppVersion.Current}");
        stdout.WriteLine();
        stdout.WriteLine("── Environment ──");
        foreach (var (k, v) in report.EnvVars)
            stdout.WriteLine($"  {k,-30} = {(string.IsNullOrEmpty(v) ? "(not set)" : v)}");
        stdout.WriteLine();

        stdout.WriteLine("── Data Root ──");
        stdout.WriteLine($"  path   : {report.DataRoot}");
        stdout.WriteLine($"  exists : {(report.DataRootExists ? "yes" : "NO")}");
        stdout.WriteLine();

        if (!report.DataRootExists)
        {
            stdout.WriteLine("FAIL: data root does not exist. Run 'total-recall init <repo-path>' to create it.");
            return;
        }

        if (report.Namespaces.Count == 0)
        {
            stdout.WriteLine("WARN: no namespaces found under data root. Run 'total-recall init <repo-path>' to create one.");
            return;
        }

        foreach (var ns in report.Namespaces)
        {
            stdout.WriteLine($"── Namespace: {ns.Name} ──");
            stdout.WriteLine($"  dir: {ns.Directory}");
            stdout.WriteLine();
            stdout.WriteLine("  Data files:");
            foreach (var f in ns.Files)
            {
                if (f.Exists)
                {
                    var when = f.LastWriteUtc?.ToString("yyyy-MM-dd HH:mm 'UTC'") ?? "?";
                    stdout.WriteLine($"    {f.Name,-22} {f.RecordCount,8:N0} records   last write {when}");
                }
                else
                {
                    var tag = IsCoreFile(f.Name) ? "MISSING (core)" : "missing";
                    stdout.WriteLine($"    {f.Name,-22} {tag}");
                }
            }
            stdout.WriteLine();
            stdout.WriteLine("  config.json:");
            if (!ns.Config.Exists)
            {
                stdout.WriteLine("    (not written — run 'total-recall init')");
            }
            else
            {
                stdout.WriteLine($"    sourceRoot   = {Show(ns.Config.SourceRoot, ns.Config.SourceRootMissing)}");
                stdout.WriteLine($"    assemblyPath = {Show(ns.Config.AssemblyPath, ns.Config.AssemblyMissing)}");
                stdout.WriteLine($"    coveragePath = {Show(ns.Config.CoveragePath, ns.Config.CoverageMissing)}");
                stdout.WriteLine($"    testsPath    = {Show(ns.Config.TestsPath, ns.Config.TestsMissing)}");
                stdout.WriteLine($"    scannedUtc   = {ns.Config.ScannedUtc ?? "(never)"}");
            }
            if (ns.Warnings.Count > 0)
            {
                stdout.WriteLine();
                stdout.WriteLine("  Issues:");
                foreach (var w in ns.Warnings)
                    stdout.WriteLine($"    ! {w}");
            }
            stdout.WriteLine();
        }

        stdout.WriteLine("── Summary ──");
        if (report.ExitCode == 0)
            stdout.WriteLine("  OK");
        else if (report.ExitCode == 1)
            stdout.WriteLine($"  WARN: {report.Warnings} issue(s) — see above.");
        else
            stdout.WriteLine($"  FAIL: data root missing.");
    }

    private static string Show(string? value, bool missing)
    {
        if (string.IsNullOrEmpty(value)) return "(empty)";
        return missing ? $"{value}   [MISSING]" : value;
    }

    internal sealed class DoctorOptions
    {
        public string? NamespaceFilter { get; set; }
        public bool ShowHelp { get; set; }
    }

    internal static DoctorOptions ParseOptions(string[] args)
    {
        var opts = new DoctorOptions();
        for (int i = 1; i < args.Length; i++)
        {
            var a = args[i].ToLowerInvariant();
            switch (a)
            {
                case "--namespace" when i + 1 < args.Length:
                case "--ns" when i + 1 < args.Length:
                    opts.NamespaceFilter = args[++i];
                    break;
                case "--help":
                case "-h":
                    opts.ShowHelp = true;
                    break;
            }
        }
        return opts;
    }

    private static void WriteHelp(TextWriter stdout)
    {
        stdout.WriteLine("Usage: total-recall doctor [--ns <name>]");
        stdout.WriteLine();
        stdout.WriteLine("Health-check the Total.Recall data root and namespace config.");
        stdout.WriteLine("Reports env vars, data file presence + record counts, and config.json validity.");
        stdout.WriteLine();
        stdout.WriteLine("Options:");
        stdout.WriteLine("  --ns <name>    Only check the named namespace (default: all)");
        stdout.WriteLine("  --help, -h     Show this help");
        stdout.WriteLine();
        stdout.WriteLine("Exit codes: 0 healthy, 1 warnings, 2 data root missing.");
    }
}

internal sealed record DoctorReport(
    Dictionary<string, string?> EnvVars,
    string DataRoot,
    bool DataRootExists,
    List<NamespaceHealth> Namespaces,
    int Warnings,
    int Errors,
    int ExitCode);

internal sealed record NamespaceHealth(
    string Name,
    string Directory,
    List<DataFileStatus> Files,
    ConfigStatus Config,
    List<string> Warnings);

internal sealed record DataFileStatus(
    string Name,
    string Path,
    bool Exists,
    long RecordCount,
    DateTime? LastWriteUtc);

internal sealed record ConfigStatus(
    string Path,
    bool Exists,
    string? SourceRoot,
    string? AssemblyPath,
    string? CoveragePath,
    string? TestsPath,
    string? ScannedUtc,
    bool SourceRootMissing,
    bool AssemblyMissing,
    bool CoverageMissing,
    bool TestsMissing);
