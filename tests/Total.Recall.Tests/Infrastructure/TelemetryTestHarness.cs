using Total.Recall.Infrastructure;

namespace Total.Recall.Tests.Infrastructure;

/// <summary>
/// Disposable harness for Cuts 1-6 tests: creates a temp data dir, sets
/// TOTAL_RECALL_DATA + TOTAL_RECALL_MODE=passive, resets every relevant cache.
/// </summary>
internal sealed class TelemetryTestHarness : IDisposable
{
    public string TempDir { get; }
    private readonly string? _origData;
    private readonly string? _origMode;

    public TelemetryTestHarness(string mode = "passive")
    {
        TempDir = Path.Combine(Path.GetTempPath(), "total-recall-tests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(TempDir);
        _origData = Environment.GetEnvironmentVariable(RepoConfig.EnvVarName);
        _origMode = Environment.GetEnvironmentVariable(TelemetryConfig.EnvVarName);
        Environment.SetEnvironmentVariable(RepoConfig.EnvVarName, TempDir);
        Environment.SetEnvironmentVariable(TelemetryConfig.EnvVarName, mode);
        RepoConfig.ClearCache();
        TelemetryConfig.ResetCache();
        StoreRegistry.Reset();
        Telemetry.ResetForTests();
        CycleDetector.ResetForTests();
        Total.Recall.Tools.TaskTool.ResetForTests();
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(RepoConfig.EnvVarName, _origData);
        Environment.SetEnvironmentVariable(TelemetryConfig.EnvVarName, _origMode);
        RepoConfig.ClearCache();
        TelemetryConfig.ResetCache();
        StoreRegistry.Reset();
        Telemetry.ResetForTests();
        CycleDetector.ResetForTests();
        Total.Recall.Tools.TaskTool.ResetForTests();
        try
        {
            if (Directory.Exists(TempDir)) Directory.Delete(TempDir, recursive: true);
        }
        catch { /* best effort */ }
    }
}
