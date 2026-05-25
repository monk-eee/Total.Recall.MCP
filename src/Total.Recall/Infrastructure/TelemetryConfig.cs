namespace Total.Recall.Infrastructure;

/// <summary>
/// Operating mode for the telemetry / eval harness.
///
/// <list type="bullet">
///   <item><c>Off</c>: no tool-call recording. Pre-Cut-1 behaviour.</item>
///   <item><c>Passive</c> (default): every tool call appended to tool-calls.jsonl. Zero agent-visible change.</item>
///   <item><c>ActiveEval</c>: passive + challenge tools become discoverable (Cut 5).</item>
/// </list>
/// </summary>
public enum TelemetryMode
{
    Off,
    Passive,
    ActiveEval
}

/// <summary>
/// Resolves the telemetry mode from the <c>TOTAL_RECALL_MODE</c> environment variable.
/// Accepts: off | passive | active-eval (case-insensitive, aliases: none/disabled, observe, eval).
/// Default: Passive.
/// </summary>
public static class TelemetryConfig
{
    public const string EnvVarName = "TOTAL_RECALL_MODE";

    private static TelemetryMode? s_cachedMode;

    /// <summary>Current effective telemetry mode (cached after first read).</summary>
    public static TelemetryMode Mode
    {
        get
        {
            // Snapshot into a local to avoid TOCTOU races with ResetCache() in tests.
            var cached = s_cachedMode;
            if (cached.HasValue) return cached.Value;
            var resolved = Parse(Environment.GetEnvironmentVariable(EnvVarName));
            s_cachedMode = resolved;
            Log.Info($"telemetry mode: {resolved}");
            return resolved;
        }
    }

    /// <summary>True when tool calls should be appended to tool-calls.jsonl.</summary>
    public static bool IsRecording => Mode != TelemetryMode.Off;

    /// <summary>True when active-eval tools (challenges) are exposed.</summary>
    public static bool IsActiveEval => Mode == TelemetryMode.ActiveEval;

    internal static TelemetryMode Parse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return TelemetryMode.Passive;
        return raw.Trim().ToLowerInvariant() switch
        {
            "off" or "none" or "disabled" or "0" or "false" => TelemetryMode.Off,
            "passive" or "observe" or "telemetry" => TelemetryMode.Passive,
            "active-eval" or "active_eval" or "eval" or "active" => TelemetryMode.ActiveEval,
            _ => TelemetryMode.Passive
        };
    }

    /// <summary>Test-only: clear the cached mode so a new env value can take effect.</summary>
    internal static void ResetCache() => s_cachedMode = null;
}
