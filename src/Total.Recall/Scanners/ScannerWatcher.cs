using Total.Recall.Infrastructure;

namespace Total.Recall.Scanners;

/// <summary>
/// File-system watcher that monitors assembly, coverage, and test files for changes
/// and automatically re-runs the appropriate scanners with debouncing.
/// 
/// Watches:
///   - Assembly .dll → re-runs AssemblyScanner
///   - Coverage .xml → re-runs CoberturaParser
///   - Test directory .cs files → re-runs TestProjectScanner
///
/// After any scanner re-run, optionally re-runs enrichment and static analysis.
/// Uses a 1-second debounce to coalesce rapid file events (e.g., build output).
/// </summary>
public sealed class ScannerWatcher : IDisposable
{
    private readonly string _dataDir;
    private readonly string? _assemblyPath;
    private readonly string? _coveragePath;
    private readonly string? _testsPath;
    private readonly Func<string, int>? _enrichFunc;
    private readonly Func<string, (int, int)>? _analyzeFunc;

    private readonly List<FileSystemWatcher> _watchers = [];
    private readonly object _lock = new();
    private readonly HashSet<ScanAction> _pendingActions = [];
    private Timer? _debounceTimer;
    private bool _disposed;

    /// <summary>Debounce window in milliseconds. Coalesces rapid file events.</summary>
    private const int DebounceMs = 1500;

    public ScannerWatcher(
        string dataDir,
        string? assemblyPath,
        string? coveragePath,
        string? testsPath,
        Func<string, int>? enrichFunc,
        Func<string, (int, int)>? analyzeFunc)
    {
        _dataDir = dataDir;
        _assemblyPath = assemblyPath;
        _coveragePath = coveragePath;
        _testsPath = testsPath;
        _enrichFunc = enrichFunc;
        _analyzeFunc = analyzeFunc;
    }

    /// <summary>
    /// Start watching. Sets up FileSystemWatchers and blocks until cancellation.
    /// </summary>
    public async Task WatchAsync(CancellationToken ct)
    {
        SetupWatchers();

        var watchCount = _watchers.Count;
        if (watchCount == 0)
        {
            Console.WriteLine("  ⚠ No paths to watch. Provide --assembly, --coverage, or --tests.");
            return;
        }

        Console.WriteLine();
        Console.WriteLine($"  👁 Watching {watchCount} path(s) for changes (Ctrl+C to stop):");
        if (_assemblyPath is not null)
            Console.WriteLine($"    • Assembly: {_assemblyPath}");
        if (_coveragePath is not null)
            Console.WriteLine($"    • Coverage: {Path.GetDirectoryName(_coveragePath)}\\*.xml");
        if (_testsPath is not null)
            Console.WriteLine($"    • Tests:    {_testsPath}\\**\\*.cs");
        Console.WriteLine();

        // Block until cancelled
        try
        {
            await Task.Delay(Timeout.Infinite, ct);
        }
        catch (OperationCanceledException)
        {
            // Clean shutdown
        }
        finally
        {
            Console.WriteLine();
            Console.WriteLine("  ■ Watch mode stopped.");
        }
    }

    private void SetupWatchers()
    {
        // Watch assembly file for changes (rebuild detection)
        if (!string.IsNullOrEmpty(_assemblyPath) && File.Exists(_assemblyPath))
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(_assemblyPath))!;
            var file = Path.GetFileName(_assemblyPath);
            var watcher = CreateWatcher(dir, file, ScanAction.Assembly);
            _watchers.Add(watcher);
            Log.Info($"[Watch] Watching assembly: {Path.Combine(dir, file)}");
        }

        // Watch coverage file's directory for any .xml changes (test run output)
        if (!string.IsNullOrEmpty(_coveragePath))
        {
            // Watch the specific file's directory, but also look for TestResults patterns
            var coverageFullPath = Path.GetFullPath(_coveragePath);
            var coverageDir = Path.GetDirectoryName(coverageFullPath)!;

            if (Directory.Exists(coverageDir))
            {
                var watcher = CreateWatcher(coverageDir, "*.xml", ScanAction.Coverage);
                watcher.IncludeSubdirectories = true; // TestResults/{guid}/coverage.cobertura.xml
                _watchers.Add(watcher);
                Log.Info($"[Watch] Watching coverage: {coverageDir}\\**\\*.xml");
            }
        }

        // Watch test directory for .cs file changes (new/modified tests)
        if (!string.IsNullOrEmpty(_testsPath) && Directory.Exists(_testsPath))
        {
            var watcher = CreateWatcher(Path.GetFullPath(_testsPath), "*.cs", ScanAction.Tests);
            watcher.IncludeSubdirectories = true;
            _watchers.Add(watcher);
            Log.Info($"[Watch] Watching tests: {_testsPath}\\**\\*.cs");
        }
    }

    private FileSystemWatcher CreateWatcher(string directory, string filter, ScanAction action)
    {
        var watcher = new FileSystemWatcher(directory, filter)
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.CreationTime,
            EnableRaisingEvents = true
        };

        watcher.Changed += (_, e) => OnFileChanged(e.FullPath, action);
        watcher.Created += (_, e) => OnFileChanged(e.FullPath, action);
        watcher.Renamed += (_, e) => OnFileChanged(e.FullPath, action);

        return watcher;
    }

    private void OnFileChanged(string fullPath, ScanAction action)
    {
        // Ignore changes to our own output files
        if (fullPath.StartsWith(_dataDir, StringComparison.OrdinalIgnoreCase))
            return;

        // Ignore obj/ and bin/ subdirectory churn that isn't the target assembly
        var fileName = Path.GetFileName(fullPath);
        if (action == ScanAction.Tests && (
            fullPath.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) ||
            fullPath.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)))
            return;

        Log.Debug($"[Watch] File changed: {fullPath} → queuing {action}");

        lock (_lock)
        {
            _pendingActions.Add(action);

            // Reset debounce timer
            _debounceTimer?.Dispose();
            _debounceTimer = new Timer(OnDebounceElapsed, null, DebounceMs, Timeout.Infinite);
        }
    }

    private void OnDebounceElapsed(object? state)
    {
        HashSet<ScanAction> actions;
        lock (_lock)
        {
            if (_pendingActions.Count == 0)
                return;

            actions = [.. _pendingActions];
            _pendingActions.Clear();
        }

        ExecuteScans(actions);
    }

    private void ExecuteScans(HashSet<ScanAction> actions)
    {
        var timestamp = DateTime.Now.ToString("HH:mm:ss");
        Console.WriteLine($"  [{timestamp}] Changes detected — re-scanning...");

        var scanResults = new List<string>();
        var anySuccess = false;

        // Run scanners in dependency order: assembly → coverage → tests
        if (actions.Contains(ScanAction.Assembly) && !string.IsNullOrEmpty(_assemblyPath))
        {
            try
            {
                Console.Write($"    Scanning assembly... ");
                var count = AssemblyScanner.Scan(_assemblyPath, _dataDir);
                Console.WriteLine($"✓ {count} types");
                scanResults.Add($"types:{count}");
                anySuccess = true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"✗ {ex.GetType().Name}: {ex.Message}");
                Log.Error($"[Watch] Assembly scan failed: {ex}");
            }
        }

        if (actions.Contains(ScanAction.Coverage) && !string.IsNullOrEmpty(_coveragePath))
        {
            // Find the most recent coverage file (test runners create new GUIDs)
            var effectivePath = FindLatestCoverageFile(_coveragePath);
            if (effectivePath is not null)
            {
                try
                {
                    Console.Write($"    Parsing coverage... ");
                    var count = CoberturaParser.Parse(effectivePath, _dataDir);
                    Console.WriteLine($"✓ {count} classes");
                    scanResults.Add($"coverage:{count}");
                    anySuccess = true;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"✗ {ex.GetType().Name}: {ex.Message}");
                    Log.Error($"[Watch] Coverage parse failed: {ex}");
                }
            }
        }

        if (actions.Contains(ScanAction.Tests) && !string.IsNullOrEmpty(_testsPath))
        {
            try
            {
                Console.Write($"    Scanning tests... ");
                var count = TestProjectScanner.Scan(_testsPath, _dataDir);
                Console.WriteLine($"✓ {count} test files");
                scanResults.Add($"tests:{count}");
                anySuccess = true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"✗ {ex.GetType().Name}: {ex.Message}");
                Log.Error($"[Watch] Test scan failed: {ex}");
            }
        }

        // Post-scan enrichment and analysis (only if at least one scan succeeded)
        if (anySuccess)
        {
            if (_enrichFunc is not null)
            {
                try
                {
                    Console.Write($"    Enriching... ");
                    var enriched = _enrichFunc(_dataDir);
                    Console.WriteLine($"✓ {enriched} classes");
                    scanResults.Add($"enriched:{enriched}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"✗ Enrich failed: {ex.Message}");
                    Log.Error($"[Watch] Enrichment failed: {ex}");
                }
            }

            if (_analyzeFunc is not null)
            {
                try
                {
                    Console.Write($"    Analyzing... ");
                    var (m, e) = _analyzeFunc(_dataDir);
                    Console.WriteLine($"✓ {m} classes, {e} edges");
                    scanResults.Add($"metrics:{m}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"✗ Analysis failed: {ex.Message}");
                    Log.Error($"[Watch] Analysis failed: {ex}");
                }
            }
        }

        Console.WriteLine($"    Done. [{string.Join(", ", scanResults)}]");
        Console.WriteLine();
    }

    /// <summary>
    /// Find the most recently created coverage.cobertura.xml in the TestResults hierarchy.
    /// Test runners create TestResults/{guid}/coverage.cobertura.xml — we want the newest.
    /// Falls back to the originally-specified path.
    /// </summary>
    private static string? FindLatestCoverageFile(string originalPath)
    {
        // If the original path still exists and is a file, check its parent for newer files
        var searchDir = Path.GetDirectoryName(Path.GetFullPath(originalPath));
        if (searchDir is null)
            return File.Exists(originalPath) ? originalPath : null;

        // Walk up to find TestResults-style directory structure
        var parentDir = Directory.GetParent(searchDir);
        if (parentDir?.Exists == true)
        {
            var candidates = parentDir.GetFiles("coverage.cobertura.xml", SearchOption.AllDirectories);
            if (candidates.Length > 0)
            {
                var newest = candidates.OrderByDescending(f => f.LastWriteTimeUtc).First();
                Log.Debug($"[Watch] Latest coverage file: {newest.FullName}");
                return newest.FullName;
            }
        }

        return File.Exists(originalPath) ? originalPath : null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _debounceTimer?.Dispose();
        foreach (var w in _watchers)
        {
            w.EnableRaisingEvents = false;
            w.Dispose();
        }
        _watchers.Clear();
    }

    private enum ScanAction
    {
        Assembly,
        Coverage,
        Tests
    }
}
