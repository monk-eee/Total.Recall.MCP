using Total.Recall.Infrastructure;
using Total.Recall.Tools;

namespace Total.Recall.Reporting;

/// <summary>
/// CLI sub-command <c>report</c>. Thin wrapper that dispatches to existing MCP tool
/// methods and writes their JSON output to <paramref name="stdout"/>. Sets
/// <c>TOTAL_RECALL_MODE=off</c> for the duration of the call so report invocations
/// do not pollute the telemetry data they are reading.
///
/// All report types are read-only. Exit code is 0 on success, 1 on unknown
/// sub-command or argument error, 2 on tool exception.
/// </summary>
internal static class ReportRunner
{
    public static int RunReport(string[] args, TextWriter stdout)
    {
        if (args.Length < 2 || args[1].Equals("--help", StringComparison.OrdinalIgnoreCase) || args[1] == "-h")
        {
            WriteHelp(stdout);
            return args.Length < 2 ? 1 : 0;
        }

        // Suppress telemetry recording for the duration of the report call so that
        // reading the data does not also write to it.
        var priorMode = Environment.GetEnvironmentVariable("TOTAL_RECALL_MODE");
        Environment.SetEnvironmentVariable("TOTAL_RECALL_MODE", "off");
        TelemetryConfig.ResetCache();

        try
        {
            var subCommand = args[1].ToLowerInvariant();
            var opts = ParseOptions(args);

            string output;
            switch (subCommand)
            {
                case "tool-stats":
                    output = ScorecardTool.GetToolCallStats(currentSessionOnly: false, ns: opts.Namespace);
                    break;
                case "efficiency":
                    output = ScorecardTool.GetEfficiencyReport(currentSessionOnly: false, ns: opts.Namespace);
                    break;
                case "scorecard":
                    output = ScorecardTool.GetModelScorecard(ns: opts.Namespace);
                    break;
                case "cycles":
                    output = CyclesTool.GetCycles(
                        currentSessionOnly: false,
                        top: opts.Last ?? 20,
                        pattern: opts.Pattern,
                        ns: opts.Namespace);
                    break;
                case "sessions":
                    output = SessionTool.GetSessions(last: opts.Last ?? 5, ns: opts.Namespace);
                    break;
                case "leaderboard":
                    output = ChallengeTool.GetEvalLeaderboard(ns: opts.Namespace);
                    break;
                case "bugs":
                    output = BugReportTool.GetBugs(
                        className: opts.Class,
                        severity: opts.Severity,
                        status: opts.Status ?? "open",
                        top: opts.Last ?? 50,
                        ns: opts.Namespace);
                    break;
                default:
                    stdout.WriteLine($"Unknown report sub-command: '{subCommand}'");
                    stdout.WriteLine();
                    WriteHelp(stdout);
                    return 1;
            }

            stdout.WriteLine(opts.Format == "table" ? TableRenderer.Render(output) : output);
            return 0;
        }
        catch (Exception ex)
        {
            stdout.WriteLine($"ERROR running report: {ex.GetType().Name}: {ex.Message}");
            return 2;
        }
        finally
        {
            Environment.SetEnvironmentVariable("TOTAL_RECALL_MODE", priorMode);
            TelemetryConfig.ResetCache();
        }
    }

    internal sealed class ReportOptions
    {
        public string? Namespace { get; set; }
        public int? Last { get; set; }
        public string? Pattern { get; set; }
        public string Format { get; set; } = "json";
        public string? Class { get; set; }
        public string? Severity { get; set; }
        public string? Status { get; set; }
    }

    internal static ReportOptions ParseOptions(string[] args)
    {
        var opts = new ReportOptions();
        // args[0] = "report", args[1] = sub-command. Parse from index 2.
        for (int i = 2; i < args.Length; i++)
        {
            var a = args[i].ToLowerInvariant();
            switch (a)
            {
                case "--ns" when i + 1 < args.Length:
                case "--namespace" when i + 1 < args.Length:
                    opts.Namespace = args[++i];
                    break;
                case "--last" when i + 1 < args.Length:
                    if (int.TryParse(args[++i], out var n)) opts.Last = n;
                    break;
                case "--pattern" when i + 1 < args.Length:
                    opts.Pattern = args[++i];
                    break;
                case "--format" when i + 1 < args.Length:
                    var fmt = args[++i].ToLowerInvariant();
                    if (fmt is "json" or "table") opts.Format = fmt;
                    break;
                case "--class" when i + 1 < args.Length:
                    opts.Class = args[++i];
                    break;
                case "--severity" when i + 1 < args.Length:
                    opts.Severity = args[++i];
                    break;
                case "--status" when i + 1 < args.Length:
                    opts.Status = args[++i];
                    break;
            }
        }
        return opts;
    }

    private static void WriteHelp(TextWriter stdout)
    {
        stdout.WriteLine("Usage: total-recall report <sub-command> [options]");
        stdout.WriteLine();
        stdout.WriteLine("Sub-commands:");
        stdout.WriteLine("  tool-stats     Per-tool call counts, p50/p95 latency, response bytes");
        stdout.WriteLine("  efficiency     Session-level tokens / bytes / cycles / dedupe report");
        stdout.WriteLine("  scorecard      Per-model aggregated metrics across sessions+tasks+evals");
        stdout.WriteLine("  cycles         Recent detected behaviour cycles (re-query, context-loss, oscillation)");
        stdout.WriteLine("  sessions       Session history + plateau warning + lines-per-test ROI");
        stdout.WriteLine("  leaderboard    Per-model eval pass rate / avg score");
        stdout.WriteLine("  bugs           Class-scoped bug reports (latest record per id wins)");
        stdout.WriteLine();
        stdout.WriteLine("Options:");
        stdout.WriteLine("  --ns <name>        Namespace to query (default: TOTAL_RECALL_NAMESPACE env var)");
        stdout.WriteLine("  --last <N>         Limit results (cycles default 20, sessions default 5, bugs default 50)");
        stdout.WriteLine("  --pattern <name>   Filter cycles by pattern: re-query | context-loss | oscillation");
        stdout.WriteLine("  --class <name>     (bugs) Filter by class (partial match)");
        stdout.WriteLine("  --severity <s>     (bugs) Filter by severity: low|medium|high|critical");
        stdout.WriteLine("  --status <s>       (bugs) Filter by status: open|triaged|fixed|wontfix|all (default: open)");
        stdout.WriteLine("  --format <json|table>  Output format (default: json)");
        stdout.WriteLine();
        stdout.WriteLine("JSON output is suitable for piping through 'ConvertFrom-Json | Format-Table'");
        stdout.WriteLine("(PowerShell) or 'jq'. Use --format table for a built-in text table.");
        stdout.WriteLine();
        stdout.WriteLine("Examples:");
        stdout.WriteLine("  total-recall report tool-stats --ns myproject");
        stdout.WriteLine("  total-recall report cycles --pattern re-query --last 50");
        stdout.WriteLine("  total-recall report scorecard --format table");
    }
}
