using Total.Recall.Scanners;

namespace Total.Recall.Tests.Scanners;

public sealed class ScannerWatcherTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _dataDir;

    public ScannerWatcherTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "total-recall-tests", Guid.NewGuid().ToString());
        _dataDir = Path.Combine(_tempDir, "data");
        Directory.CreateDirectory(_dataDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public void Constructor_AcceptsNullPaths()
    {
        using var watcher = new ScannerWatcher(
            _dataDir, null, null, null, null, null);
        // Should not throw
    }

    [Fact]
    public async Task WatchAsync_NoPaths_ReturnsImmediately()
    {
        using var watcher = new ScannerWatcher(
            _dataDir, null, null, null, null, null);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        // Should return quickly since there are no paths to watch
        await watcher.WatchAsync(cts.Token);
    }

    [Fact]
    public async Task WatchAsync_WithCancellation_StopsCleanly()
    {
        // Create a file to watch
        var assemblyDir = Path.Combine(_tempDir, "bin");
        Directory.CreateDirectory(assemblyDir);
        var assemblyPath = Path.Combine(assemblyDir, "test.dll");
        File.WriteAllText(assemblyPath, "fake dll content");

        using var watcher = new ScannerWatcher(
            _dataDir, assemblyPath, null, null, null, null);

        using var cts = new CancellationTokenSource();
        var watchTask = watcher.WatchAsync(cts.Token);

        // Cancel after a brief delay
        await Task.Delay(200);
        cts.Cancel();

        // Should complete cleanly (no exception)
        await watchTask;
    }

    [Fact]
    public void Dispose_BeforeWatch_DoesNotThrow()
    {
        var watcher = new ScannerWatcher(
            _dataDir, null, null, null, null, null);
        watcher.Dispose();
        // Double dispose should also be safe
        watcher.Dispose();
    }

    [Fact]
    public async Task WatchAsync_WithTestsPath_WatchesForCsFiles()
    {
        var testsDir = Path.Combine(_tempDir, "tests");
        Directory.CreateDirectory(testsDir);

        using var watcher = new ScannerWatcher(
            _dataDir, null, null, testsDir, null, null);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        var watchTask = watcher.WatchAsync(cts.Token);

        // Create a .cs file — should not crash the watcher
        await Task.Delay(100);
        File.WriteAllText(Path.Combine(testsDir, "NewTest.cs"), "// test");
        await Task.Delay(200);

        cts.Cancel();
        await watchTask;
    }

    [Fact]
    public async Task WatchAsync_WithCoveragePath_WatchesForXmlFiles()
    {
        var coverageDir = Path.Combine(_tempDir, "TestResults");
        var guidDir = Path.Combine(coverageDir, Guid.NewGuid().ToString());
        Directory.CreateDirectory(guidDir);
        var coveragePath = Path.Combine(guidDir, "coverage.cobertura.xml");
        File.WriteAllText(coveragePath, "<coverage />");

        using var watcher = new ScannerWatcher(
            _dataDir, null, coveragePath, null, null, null);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var watchTask = watcher.WatchAsync(cts.Token);

        await Task.Delay(100);
        cts.Cancel();
        await watchTask;
    }

    [Fact]
    public void Constructor_WithDelegates_StoresEnrichAndAnalyze()
    {
        var enrichCalled = false;
        var analyzeCalled = false;

        Func<string, int> enrich = dir => { enrichCalled = true; return 5; };
        Func<string, (int, int)> analyze = dir => { analyzeCalled = true; return (10, 20); };

        using var watcher = new ScannerWatcher(
            _dataDir, null, null, null, enrich, analyze);

        // Delegates are stored but not called until file changes trigger rescan
        Assert.False(enrichCalled);
        Assert.False(analyzeCalled);
    }

    [Fact]
    public async Task WatchAsync_NonexistentAssemblyPath_SkipsAssemblyWatcher()
    {
        // Assembly path doesn't exist — watcher should skip it and still work
        using var watcher = new ScannerWatcher(
            _dataDir,
            Path.Combine(_tempDir, "nonexistent.dll"),
            null, null, null, null);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        // With no valid paths, should return immediately (0 watchers)
        await watcher.WatchAsync(cts.Token);
    }

    [Fact]
    public async Task WatchAsync_NonexistentTestsPath_SkipsTestsWatcher()
    {
        using var watcher = new ScannerWatcher(
            _dataDir,
            null, null,
            Path.Combine(_tempDir, "nonexistent-tests"),
            null, null);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await watcher.WatchAsync(cts.Token);
    }

    [Fact]
    public async Task WatchAsync_AllPathsValid_DoesNotReturnImmediately()
    {
        // Create valid paths
        var assemblyDir = Path.Combine(_tempDir, "bin");
        Directory.CreateDirectory(assemblyDir);
        var assemblyPath = Path.Combine(assemblyDir, "test.dll");
        File.WriteAllText(assemblyPath, "fake");

        var testsDir = Path.Combine(_tempDir, "tests");
        Directory.CreateDirectory(testsDir);

        using var watcher = new ScannerWatcher(
            _dataDir, assemblyPath, null, testsDir, null, null);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
        var sw = System.Diagnostics.Stopwatch.StartNew();

        await watcher.WatchAsync(cts.Token);

        sw.Stop();
        // Should have blocked until cancellation (at least ~400ms)
        Assert.True(sw.ElapsedMilliseconds >= 300,
            $"Expected watch to block, but returned in {sw.ElapsedMilliseconds}ms");
    }
}
