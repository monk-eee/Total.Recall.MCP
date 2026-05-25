using System.ComponentModel;
using System.Text;
using System.Text.Json;
using ModelContextProtocol.Server;
using Total.Recall.Infrastructure;
using Total.Recall.Models;
using Total.Recall.Scanners;

namespace Total.Recall.Tools;

/// <summary>
/// MCP tools for querying static analysis results — class metrics, dependency graph,
/// and cluster data produced by DependencyAnalyzer.
/// </summary>
[McpServerToolType]
public static class AnalysisTool
{
    [McpServerTool, Description(
        "Get static analysis metrics for a class: coupling (afferent/efferent), instability, " +
        "archetype classification, dependency list, and cluster membership. " +
        "Also shows which other classes depend on it. " +
        "Use to understand a class's position in the dependency graph before writing tests.")]
    public static string GetClassMetrics(
        [Description("Class name to look up")] string className,
        [Description("Optional: namespace/session to query")] string? ns = null)
    {
        return Telemetry.Track("get_class_metrics", ns, new { className, ns }, () =>
        {
        Metrics.Increment("tool.getClassMetrics");
        Log.Debug($"[GetClassMetrics] className='{className}' ns='{ns ?? "(default)"}'");
        try
        {
            return GetClassMetricsCore(className, ns);
        }
        catch (Exception ex)
        {
            Log.Error($"[GetClassMetrics] failed: {ex.GetType().Name}: {ex.Message}");
            return $"ERROR in GetClassMetrics: {ex.GetType().Name}: {ex.Message}";
        }
        });
    }

    [McpServerTool, Description(
        "Get the dependency graph neighborhood for a class — its direct dependencies, " +
        "direct dependents, and a Mermaid diagram of the local subgraph. " +
        "Use to visualize how a class connects to the rest of the codebase.")]
    public static string GetDependencyGraph(
        [Description("Class name to center the graph on")] string className,
        [Description("Graph depth: 1 = direct deps, 2 = include transitive (default: 1)")] int depth = 1,
        [Description("Optional: namespace/session to query")] string? ns = null)
    {
        return Telemetry.Track("get_dependency_graph", ns, new { className, depth, ns }, () =>
        {
        Metrics.Increment("tool.getDependencyGraph");
        Log.Debug($"[GetDependencyGraph] className='{className}' depth={depth} ns='{ns ?? "(default)"}'");
        try
        {
            return GetDependencyGraphCore(className, depth, ns);
        }
        catch (Exception ex)
        {
            Log.Error($"[GetDependencyGraph] failed: {ex.GetType().Name}: {ex.Message}");
            return $"ERROR in GetDependencyGraph: {ex.GetType().Name}: {ex.Message}";
        }
        });
    }

    [McpServerTool, Description(
        "Get a summary of the static analysis: hot interfaces (most consumed), " +
        "most coupled classes, dependency clusters, and isolated classes. " +
        "Use at the start of a session for an architectural overview.")]
    public static string GetAnalysisSummary(
        [Description("Optional: namespace/session to query")] string? ns = null)
    {
        return Telemetry.Track("get_analysis_summary", ns, new { ns }, () =>
        {
        Metrics.Increment("tool.getAnalysisSummary");
        Log.Debug($"[GetAnalysisSummary] ns='{ns ?? "(default)"}'");
        try
        {
            return GetAnalysisSummaryCore(ns);
        }
        catch (Exception ex)
        {
            Log.Error($"[GetAnalysisSummary] failed: {ex.GetType().Name}: {ex.Message}");
            return $"ERROR in GetAnalysisSummary: {ex.GetType().Name}: {ex.Message}";
        }
        });
    }

    // ── Core implementations ──

    private static string GetClassMetricsCore(string className, string? ns)
    {
        var stores = StoreRegistry.ForNamespace(ns);
        var dataDir = stores.DataDir;

        var metricsStore = new JsonLineStore<ClassMetrics>(DependencyAnalyzer.ClassMetricsPath(dataDir));
        if (!metricsStore.HasData())
            return "No analysis data found. Run 'total-recall scan --assembly <dll> --enrich' to generate static analysis.";

        var allMetrics = metricsStore.LoadAll();
        var match = allMetrics.FirstOrDefault(m =>
            m.Class.Equals(className, StringComparison.OrdinalIgnoreCase));

        if (match is null)
        {
            // Fuzzy search
            var candidates = allMetrics
                .Where(m => m.Class.Contains(className, StringComparison.OrdinalIgnoreCase))
                .Take(5)
                .Select(m => m.Class)
                .ToList();

            if (candidates.Count > 0)
                return $"Class '{className}' not found. Did you mean: {string.Join(", ", candidates)}?";
            return $"Class '{className}' not found in analysis data.";
        }

        var sb = new StringBuilder();
        sb.AppendLine($"## {match.Class}");
        sb.AppendLine($"**Namespace:** {match.Namespace}");
        sb.AppendLine($"**Archetype:** {match.Archetype}");
        if (match.Cluster >= 0)
            sb.AppendLine($"**Cluster:** {match.Cluster}");
        sb.AppendLine();

        sb.AppendLine("### Coupling Metrics");
        sb.AppendLine($"| Metric | Value |");
        sb.AppendLine($"|--------|-------|");
        sb.AppendLine($"| Afferent coupling (Ca) | {match.AfferentCoupling} |");
        sb.AppendLine($"| Efferent coupling (Ce) | {match.EfferentCoupling} |");
        sb.AppendLine($"| Instability (Ce/(Ca+Ce)) | {match.Instability:F3} |");
        sb.AppendLine($"| Max ctor params | {match.MaxCtorParams} |");
        sb.AppendLine($"| Properties | {match.PropertyCount} |");
        sb.AppendLine($"| Interfaces implemented | {match.InterfaceCount} |");
        sb.AppendLine($"| Inheritance depth | {match.InheritanceDepth} |");
        if (match.TotalLines > 0)
            sb.AppendLine($"| Total lines (coverage) | {match.TotalLines} |");
        sb.AppendLine();

        if (match.DependsOn.Count > 0)
        {
            sb.AppendLine($"### Depends On ({match.DependsOn.Count})");
            foreach (var dep in match.DependsOn.OrderBy(d => d))
                sb.AppendLine($"- {dep}");
            sb.AppendLine();
        }

        if (match.DependedOnBy.Count > 0)
        {
            sb.AppendLine($"### Depended On By ({match.DependedOnBy.Count})");
            foreach (var dep in match.DependedOnBy.OrderBy(d => d))
                sb.AppendLine($"- {dep}");
            sb.AppendLine();
        }

        return sb.ToString();
    }

    private static string GetDependencyGraphCore(string className, int depth, string? ns)
    {
        var stores = StoreRegistry.ForNamespace(ns);
        var dataDir = stores.DataDir;

        var edgeStore = new JsonLineStore<DependencyEdge>(DependencyAnalyzer.DependencyGraphPath(dataDir));
        if (!edgeStore.HasData())
            return "No dependency graph data found. Run 'total-recall scan --assembly <dll> --enrich' to generate.";

        var allEdges = edgeStore.LoadAll();
        var metricsStore = new JsonLineStore<ClassMetrics>(DependencyAnalyzer.ClassMetricsPath(dataDir));
        var allMetrics = metricsStore.HasData() ? metricsStore.LoadAll() : [];

        // Verify class exists
        var classExists = allEdges.Any(e =>
            e.From.Equals(className, StringComparison.OrdinalIgnoreCase) ||
            e.To.Equals(className, StringComparison.OrdinalIgnoreCase));

        if (!classExists)
        {
            var candidates = allMetrics
                .Where(m => m.Class.Contains(className, StringComparison.OrdinalIgnoreCase))
                .Take(5)
                .Select(m => m.Class)
                .ToList();

            if (candidates.Count > 0)
                return $"Class '{className}' not found in graph. Did you mean: {string.Join(", ", candidates)}?";
            return $"Class '{className}' not found in dependency graph.";
        }

        // Collect neighborhood nodes
        var nodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { className };
        var relevantEdges = new List<DependencyEdge>();

        for (int d = 0; d < Math.Min(depth, 3); d++)
        {
            var newNodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var edge in allEdges)
            {
                if (nodes.Contains(edge.From) || nodes.Contains(edge.To))
                {
                    newNodes.Add(edge.From);
                    newNodes.Add(edge.To);
                    relevantEdges.Add(edge);
                }
            }
            foreach (var n in newNodes) nodes.Add(n);
        }

        var sb = new StringBuilder();
        sb.AppendLine($"## Dependency Graph: {className} (depth={depth})");
        sb.AppendLine();

        // Outgoing (efferent)
        var outgoing = relevantEdges
            .Where(e => e.From.Equals(className, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (outgoing.Count > 0)
        {
            sb.AppendLine($"### Dependencies ({outgoing.Count})");
            foreach (var e in outgoing.OrderBy(e => e.Kind).ThenBy(e => e.To))
                sb.AppendLine($"- **{e.To}** ({e.Kind}{(e.ParamName is not null ? $", param: {e.ParamName}" : "")})");
            sb.AppendLine();
        }

        // Incoming (afferent)
        var incoming = relevantEdges
            .Where(e => e.To.Equals(className, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (incoming.Count > 0)
        {
            sb.AppendLine($"### Consumers ({incoming.Count})");
            foreach (var e in incoming.OrderBy(e => e.Kind).ThenBy(e => e.From))
                sb.AppendLine($"- **{e.From}** ({e.Kind}{(e.ParamName is not null ? $", param: {e.ParamName}" : "")})");
            sb.AppendLine();
        }

        // Mermaid subgraph
        sb.AppendLine("### Mermaid Diagram");
        sb.AppendLine("```mermaid");
        sb.AppendLine("flowchart LR");

        // Deduplicate edges
        var rendered = new HashSet<string>();
        foreach (var node in nodes.Take(30)) // Limit for readability
        {
            var isCenter = node.Equals(className, StringComparison.OrdinalIgnoreCase);
            var id = SanitizeId(node);
            if (isCenter)
                sb.AppendLine($"  {id}[[\"{node}\"]]");
            else
                sb.AppendLine($"  {id}[\"{node}\"]");
        }

        foreach (var edge in relevantEdges)
        {
            if (!nodes.Contains(edge.From) || !nodes.Contains(edge.To))
                continue;

            var fromId = SanitizeId(edge.From);
            var toId = SanitizeId(edge.To);
            var key = $"{fromId}->{toId}";
            if (!rendered.Add(key))
                continue;

            var arrow = edge.Kind switch
            {
                "ctor-interface" => $"  {fromId} -.->|inject| {toId}",
                "ctor-concrete" => $"  {fromId} -->|concrete| {toId}",
                "base-type" => $"  {fromId} ==>|inherits| {toId}",
                "implements" => $"  {fromId} -.->|impl| {toId}",
                _ => $"  {fromId} --> {toId}"
            };
            sb.AppendLine(arrow);
        }

        // Highlight center node
        sb.AppendLine($"  style {SanitizeId(className)} fill:#ff9,stroke:#f80,stroke-width:3px");
        sb.AppendLine("```");

        return sb.ToString();
    }

    private static string GetAnalysisSummaryCore(string? ns)
    {
        var stores = StoreRegistry.ForNamespace(ns);
        var dataDir = stores.DataDir;

        var reportPath = DependencyAnalyzer.AnalysisReportPath(dataDir);
        if (!File.Exists(reportPath))
            return "No analysis report found. Run 'total-recall scan --assembly <dll> --enrich' to generate.";

        var report = JsonSerializer.Deserialize<AnalysisReport>(
            File.ReadAllText(reportPath), SharedJsonOptions.CamelCase);

        if (report is null)
            return "Analysis report is corrupt.";

        var sb = new StringBuilder();
        sb.AppendLine("## Static Analysis Summary");
        sb.AppendLine();
        sb.AppendLine($"| Metric | Value |");
        sb.AppendLine($"|--------|-------|");
        sb.AppendLine($"| Total types | {report.TotalTypes} |");
        sb.AppendLine($"| Total edges | {report.TotalEdges} |");
        sb.AppendLine($"| Clusters | {report.TotalClusters} |");
        sb.AppendLine($"| Isolated classes | {report.IsolatedClasses.Count} |");
        sb.AppendLine($"| Analyzed | {report.AnalyzedUtc} |");
        sb.AppendLine();

        if (report.HotInterfaces.Count > 0)
        {
            sb.AppendLine("### Hot Interfaces (most consumed via DI)");
            sb.AppendLine("| Interface | Consumers |");
            sb.AppendLine("|-----------|-----------|");
            foreach (var hi in report.HotInterfaces)
                sb.AppendLine($"| {hi.Name} | {hi.Consumers} ({string.Join(", ", hi.ConsumerNames.Take(5))}{(hi.ConsumerNames.Count > 5 ? "..." : "")}) |");
            sb.AppendLine();
        }

        if (report.MostCoupled.Count > 0)
        {
            sb.AppendLine("### Most Coupled Classes (highest Ce)");
            foreach (var cls in report.MostCoupled)
                sb.AppendLine($"- {cls}");
            sb.AppendLine();
        }

        // Load cluster data for summary
        var metricsStore = new JsonLineStore<ClassMetrics>(DependencyAnalyzer.ClassMetricsPath(dataDir));
        if (metricsStore.HasData())
        {
            var allMetrics = metricsStore.LoadAll();
            var clustered = allMetrics.Where(m => m.Cluster >= 0).GroupBy(m => m.Cluster).OrderBy(g => g.Key);

            if (clustered.Any())
            {
                sb.AppendLine("### Clusters");
                foreach (var group in clustered)
                {
                    var members = group.Select(m => m.Class).OrderBy(n => n).ToList();
                    var avgInst = group.Average(m => m.Instability);
                    sb.AppendLine($"**Cluster {group.Key}** ({members.Count} classes, avg instability {avgInst:F2}):");
                    sb.AppendLine($"  {string.Join(", ", members.Take(10))}{(members.Count > 10 ? "..." : "")}");
                }
                sb.AppendLine();
            }

            // Archetype distribution
            var archetypes = allMetrics
                .Where(m => !m.IsInterface && !m.IsEnum)
                .GroupBy(m => m.Archetype)
                .OrderByDescending(g => g.Count());

            sb.AppendLine("### Archetype Distribution");
            sb.AppendLine("| Archetype | Count |");
            sb.AppendLine("|-----------|-------|");
            foreach (var g in archetypes)
                sb.AppendLine($"| {g.Key} | {g.Count()} |");
        }

        return sb.ToString();
    }

    private static string SanitizeId(string name)
    {
        return name
            .Replace('<', '_')
            .Replace('>', '_')
            .Replace(',', '_')
            .Replace(' ', '_')
            .Replace('.', '_');
    }
}
