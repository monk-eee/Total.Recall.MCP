namespace Total.Recall.Infrastructure;

/// <summary>
/// Simple stderr logging helper. All output goes to stderr so it doesn't
/// interfere with the stdio JSON-RPC MCP transport on stdout.
/// Thread-safe (Console.Error is synchronized).
/// </summary>
public static class Log
{
    private static readonly string s_prefix = "[Total.Recall]";

    public static void Info(string message)
        => Console.Error.WriteLine($"{s_prefix} {message}");

    public static void Warn(string message)
        => Console.Error.WriteLine($"{s_prefix} WARN: {message}");

    public static void Error(string message)
        => Console.Error.WriteLine($"{s_prefix} ERROR: {message}");
}
