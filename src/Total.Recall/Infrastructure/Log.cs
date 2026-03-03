namespace Total.Recall.Infrastructure;

/// <summary>
/// Configurable stderr logging helper. All output goes to stderr so it doesn't
/// interfere with the stdio JSON-RPC MCP transport on stdout.
/// Thread-safe (Console.Error is synchronized).
///
/// Log level is configured via:
///   1. <see cref="SetLevel"/> (programmatic, tests)
///   2. <c>TOTAL_RECALL_LOG_LEVEL</c> env var (quiet|error|warn|info|debug)
///   3. Default: Info
///
/// Messages below the configured level are silently dropped.
/// </summary>
public static class Log
{
    public const string LogLevelEnvVar = "TOTAL_RECALL_LOG_LEVEL";

    private static readonly string s_prefix = "[Total.Recall]";
    private static LogLevel s_level = ResolveDefaultLevel();

    /// <summary>
    /// Current minimum log level. Messages below this level are dropped.
    /// </summary>
    public static LogLevel Level => s_level;

    /// <summary>
    /// Set the minimum log level at runtime. Use in tests or early startup.
    /// </summary>
    public static void SetLevel(LogLevel level) => s_level = level;

    /// <summary>
    /// Re-read the log level from the environment variable.
    /// Called automatically at static init; call again after changing the env var.
    /// </summary>
    public static void ResetLevel() => s_level = ResolveDefaultLevel();

    public static void Debug(string message)
    {
        if (s_level <= LogLevel.Debug)
            Write("DEBUG", message);
    }

    public static void Info(string message)
    {
        if (s_level <= LogLevel.Info)
            Write(null, message);
    }

    public static void Warn(string message)
    {
        if (s_level <= LogLevel.Warn)
            Write("WARN", message);
    }

    public static void Error(string message)
    {
        if (s_level <= LogLevel.Error)
            Write("ERROR", message);
    }

    /// <summary>
    /// Check whether a given level is enabled, so callers can skip expensive
    /// string interpolation when the message would be dropped.
    /// </summary>
    public static bool IsEnabled(LogLevel level) => s_level <= level;

    private static void Write(string? tag, string message)
    {
        var ts = DateTime.UtcNow.ToString("HH:mm:ss.fff");
        var line = tag is null
            ? $"{s_prefix} {ts} {message}"
            : $"{s_prefix} {ts} {tag}: {message}";
        Console.Error.WriteLine(line);
    }

    private static LogLevel ResolveDefaultLevel()
    {
        var env = Environment.GetEnvironmentVariable(LogLevelEnvVar);
        if (string.IsNullOrWhiteSpace(env))
            return LogLevel.Info;

        return env.Trim().ToLowerInvariant() switch
        {
            "quiet" or "silent" or "none" => LogLevel.Quiet,
            "error" or "err" => LogLevel.Error,
            "warn" or "warning" => LogLevel.Warn,
            "info" or "information" => LogLevel.Info,
            "debug" or "verbose" or "trace" => LogLevel.Debug,
            _ => LogLevel.Info
        };
    }
}

/// <summary>
/// Log verbosity levels, ordered from most verbose (Debug) to silent (Quiet).
/// </summary>
public enum LogLevel
{
    /// <summary>Diagnostic detail — tool inputs, record counts, lookup paths.</summary>
    Debug = 0,
    /// <summary>Normal operation — startup, config resolution, data validation.</summary>
    Info = 1,
    /// <summary>Recoverable issues — missing data files, fallback paths.</summary>
    Warn = 2,
    /// <summary>Failures — tool exceptions, corrupt data, startup crashes.</summary>
    Error = 3,
    /// <summary>No output at all.</summary>
    Quiet = 4
}
