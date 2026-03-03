using System.Text.Json;
using Total.Recall.Infrastructure;
using Total.Recall.Models;
using Total.Recall.Scanners;
using Total.Recall.Tools;

namespace Total.Recall.Tests.Tools;

/// <summary>
/// Tests for AnalysisTool — GetClassMetrics, GetDependencyGraph, GetAnalysisSummary.
/// Seeds analysis output files (class-metrics.jsonl, dependency-graph.jsonl, analysis-report.json)
/// into a temp directory and verifies the MCP tool output.
/// </summary>
[Collection("ToolTests")]
public sealed class AnalysisToolTests : ToolTestBase
{
    public AnalysisToolTests() : base(saveNamespace: true) { }

    // ── Helpers ──

    private void SeedMetrics(params ClassMetrics[] records)
    {
        var store = new JsonLineStore<ClassMetrics>(DependencyAnalyzer.ClassMetricsPath(TempDir));
        store.WriteAll(records);
    }

    private void SeedEdges(params DependencyEdge[] records)
    {
        var store = new JsonLineStore<DependencyEdge>(DependencyAnalyzer.DependencyGraphPath(TempDir));
        store.WriteAll(records);
    }

    private void SeedReport(AnalysisReport report)
    {
        var json = JsonSerializer.Serialize(report, SharedJsonOptions.CamelCaseIndented);
        File.WriteAllText(DependencyAnalyzer.AnalysisReportPath(TempDir), json);
    }

    private static ClassMetrics MakeMetrics(string className, string ns = "App",
        int ca = 0, int ce = 0, double instability = 0, string archetype = "service",
        int cluster = -1, int maxCtorParams = 0, int totalLines = 0,
        List<string>? dependsOn = null, List<string>? dependedOnBy = null)
    {
        return new ClassMetrics
        {
            Class = className,
            Namespace = ns,
            AfferentCoupling = ca,
            EfferentCoupling = ce,
            Instability = instability,
            Archetype = archetype,
            Cluster = cluster,
            MaxCtorParams = maxCtorParams,
            TotalLines = totalLines,
            DependsOn = dependsOn ?? [],
            DependedOnBy = dependedOnBy ?? []
        };
    }

    // ═══════════════════════════════════════
    // GetClassMetrics
    // ═══════════════════════════════════════

    [Fact]
    public void GetClassMetrics_NoData_ReturnsNoAnalysisMessage()
    {
        var result = AnalysisTool.GetClassMetrics("SomeClass");
        Assert.Contains("No analysis data found", result);
    }

    [Fact]
    public void GetClassMetrics_ExactMatch_ReturnsFormattedMetrics()
    {
        SeedMetrics(MakeMetrics("OrderService", ce: 3, ca: 1, instability: 0.75,
            archetype: "service", cluster: 0, maxCtorParams: 3, totalLines: 200,
            dependsOn: ["ILogger", "IRepo", "ICache"],
            dependedOnBy: ["OrderController"]));

        var result = AnalysisTool.GetClassMetrics("OrderService");

        Assert.Contains("## OrderService", result);
        Assert.Contains("**Archetype:** service", result);
        Assert.Contains("**Cluster:** 0", result);
        Assert.Contains("Afferent coupling (Ca) | 1", result);
        Assert.Contains("Efferent coupling (Ce) | 3", result);
        Assert.Contains("0.750", result);
        Assert.Contains("Max ctor params | 3", result);
        Assert.Contains("Total lines (coverage) | 200", result);
        Assert.Contains("### Depends On (3)", result);
        Assert.Contains("- ILogger", result);
        Assert.Contains("### Depended On By (1)", result);
        Assert.Contains("- OrderController", result);
    }

    [Fact]
    public void GetClassMetrics_CaseInsensitiveMatch()
    {
        SeedMetrics(MakeMetrics("OrderService"));

        var result = AnalysisTool.GetClassMetrics("orderservice");
        Assert.Contains("## OrderService", result);
    }

    [Fact]
    public void GetClassMetrics_FuzzyMatch_SuggestsCandidates()
    {
        SeedMetrics(MakeMetrics("OrderService"), MakeMetrics("OrderProcessor"));

        var result = AnalysisTool.GetClassMetrics("Order");
        Assert.Contains("Did you mean", result);
        Assert.Contains("OrderService", result);
        Assert.Contains("OrderProcessor", result);
    }

    [Fact]
    public void GetClassMetrics_NotFound_NoFuzzyMatches()
    {
        SeedMetrics(MakeMetrics("OrderService"));

        var result = AnalysisTool.GetClassMetrics("CompletelyUnrelated");
        Assert.Contains("not found in analysis data", result);
    }

    [Fact]
    public void GetClassMetrics_NoDependencies_OmitsDependsSections()
    {
        SeedMetrics(MakeMetrics("Standalone", archetype: "model"));

        var result = AnalysisTool.GetClassMetrics("Standalone");
        Assert.DoesNotContain("### Depends On", result);
        Assert.DoesNotContain("### Depended On By", result);
    }

    [Fact]
    public void GetClassMetrics_NoCluster_OmitsClusterLine()
    {
        SeedMetrics(MakeMetrics("Solo", cluster: -1));

        var result = AnalysisTool.GetClassMetrics("Solo");
        Assert.DoesNotContain("**Cluster:**", result);
    }

    [Fact]
    public void GetClassMetrics_ZeroTotalLines_OmitsTotalLinesRow()
    {
        SeedMetrics(MakeMetrics("NoCoverage", totalLines: 0));

        var result = AnalysisTool.GetClassMetrics("NoCoverage");
        Assert.DoesNotContain("Total lines (coverage)", result);
    }

    // ═══════════════════════════════════════
    // GetDependencyGraph
    // ═══════════════════════════════════════

    [Fact]
    public void GetDependencyGraph_NoData_ReturnsNoGraphMessage()
    {
        var result = AnalysisTool.GetDependencyGraph("SomeClass");
        Assert.Contains("No dependency graph data found", result);
    }

    [Fact]
    public void GetDependencyGraph_ClassNotFound_ReturnsNotFoundMessage()
    {
        SeedEdges(new DependencyEdge { From = "A", To = "B", Kind = "ctor-interface" });

        var result = AnalysisTool.GetDependencyGraph("NonExistent");
        Assert.Contains("not found in dependency graph", result);
    }

    [Fact]
    public void GetDependencyGraph_ClassNotFound_SuggestsFuzzy()
    {
        SeedEdges(new DependencyEdge { From = "OrderService", To = "ILogger", Kind = "ctor-interface" });
        SeedMetrics(MakeMetrics("OrderService"));

        var result = AnalysisTool.GetDependencyGraph("Order");
        Assert.Contains("Did you mean", result);
        Assert.Contains("OrderService", result);
    }

    [Fact]
    public void GetDependencyGraph_Outgoing_ShowsDependencies()
    {
        SeedEdges(
            new DependencyEdge { From = "Worker", To = "ILogger", Kind = "ctor-interface", ParamName = "logger" },
            new DependencyEdge { From = "Worker", To = "Config", Kind = "ctor-concrete", ParamName = "config" });

        var result = AnalysisTool.GetDependencyGraph("Worker");

        Assert.Contains("## Dependency Graph: Worker", result);
        Assert.Contains("### Dependencies (2)", result);
        Assert.Contains("**ILogger** (ctor-interface, param: logger)", result);
        Assert.Contains("**Config** (ctor-concrete, param: config)", result);
    }

    [Fact]
    public void GetDependencyGraph_Incoming_ShowsConsumers()
    {
        SeedEdges(
            new DependencyEdge { From = "ServiceA", To = "ILogger", Kind = "ctor-interface" },
            new DependencyEdge { From = "ServiceB", To = "ILogger", Kind = "ctor-interface" });

        var result = AnalysisTool.GetDependencyGraph("ILogger");

        Assert.Contains("### Consumers (2)", result);
        Assert.Contains("**ServiceA**", result);
        Assert.Contains("**ServiceB**", result);
    }

    [Fact]
    public void GetDependencyGraph_ContainsMermaid()
    {
        SeedEdges(new DependencyEdge { From = "Worker", To = "ILogger", Kind = "ctor-interface" });

        var result = AnalysisTool.GetDependencyGraph("Worker");

        Assert.Contains("```mermaid", result);
        Assert.Contains("flowchart LR", result);
        Assert.Contains("Worker", result);
    }

    [Fact]
    public void GetDependencyGraph_Depth2_IncludesTransitive()
    {
        SeedEdges(
            new DependencyEdge { From = "A", To = "B", Kind = "ctor-interface" },
            new DependencyEdge { From = "B", To = "C", Kind = "ctor-interface" });

        var result = AnalysisTool.GetDependencyGraph("A", depth: 2);

        // With depth=2, should find the transitive edge B→C
        Assert.Contains("B", result);
        Assert.Contains("C", result);
    }

    [Fact]
    public void GetDependencyGraph_MermaidEdgeStyles()
    {
        SeedEdges(
            new DependencyEdge { From = "Svc", To = "ILog", Kind = "ctor-interface" },
            new DependencyEdge { From = "Svc", To = "Config", Kind = "ctor-concrete" },
            new DependencyEdge { From = "Svc", To = "Base", Kind = "base-type" },
            new DependencyEdge { From = "Svc", To = "IService", Kind = "implements" });

        var result = AnalysisTool.GetDependencyGraph("Svc");

        Assert.Contains("-.->|inject|", result);
        Assert.Contains("-->|concrete|", result);
        Assert.Contains("==>|inherits|", result);
        Assert.Contains("-.->|impl|", result);
    }

    // ═══════════════════════════════════════
    // GetAnalysisSummary
    // ═══════════════════════════════════════

    [Fact]
    public void GetAnalysisSummary_NoReport_ReturnsNoReportMessage()
    {
        var result = AnalysisTool.GetAnalysisSummary();
        Assert.Contains("No analysis report found", result);
    }

    [Fact]
    public void GetAnalysisSummary_ValidReport_ReturnsFormattedSummary()
    {
        SeedReport(new AnalysisReport
        {
            AnalyzedUtc = "2024-01-15T10:00:00Z",
            TotalTypes = 50,
            TotalEdges = 120,
            TotalClusters = 3,
            HotInterfaces =
            [
                new HotInterface { Name = "ILogger", Consumers = 10, ConsumerNames = ["A", "B", "C", "D", "E", "F"] }
            ],
            MostCoupled = ["ServiceA (Ce=5)", "ServiceB (Ce=4)"],
            IsolatedClasses = ["Stub1", "Stub2"]
        });

        var result = AnalysisTool.GetAnalysisSummary();

        Assert.Contains("## Static Analysis Summary", result);
        Assert.Contains("Total types | 50", result);
        Assert.Contains("Total edges | 120", result);
        Assert.Contains("Clusters | 3", result);
        Assert.Contains("Isolated classes | 2", result);
        Assert.Contains("### Hot Interfaces", result);
        Assert.Contains("ILogger", result);
        Assert.Contains("10", result);
        // Consumer names should be truncated with ...
        Assert.Contains("...", result);
        Assert.Contains("### Most Coupled Classes", result);
        Assert.Contains("ServiceA (Ce=5)", result);
    }

    [Fact]
    public void GetAnalysisSummary_WithClusterMetrics_ShowsClusterSection()
    {
        SeedReport(new AnalysisReport
        {
            TotalTypes = 5, TotalEdges = 4, TotalClusters = 1,
            AnalyzedUtc = "2024-01-01T00:00:00Z",
            HotInterfaces = [], MostCoupled = [], IsolatedClasses = []
        });
        SeedMetrics(
            MakeMetrics("SvcA", cluster: 0, instability: 0.8),
            MakeMetrics("SvcB", cluster: 0, instability: 0.6),
            MakeMetrics("Solo", cluster: -1)
        );

        var result = AnalysisTool.GetAnalysisSummary();

        Assert.Contains("### Clusters", result);
        Assert.Contains("Cluster 0", result);
        Assert.Contains("SvcA", result);
        Assert.Contains("SvcB", result);
    }

    [Fact]
    public void GetAnalysisSummary_ArchetypeDistribution_Shows()
    {
        SeedReport(new AnalysisReport
        {
            TotalTypes = 3, TotalEdges = 0, TotalClusters = 0,
            AnalyzedUtc = "2024-01-01T00:00:00Z",
            HotInterfaces = [], MostCoupled = [], IsolatedClasses = []
        });
        SeedMetrics(
            MakeMetrics("OrderService", archetype: "service"),
            MakeMetrics("UserService", archetype: "service"),
            MakeMetrics("UserDto", archetype: "model")
        );

        var result = AnalysisTool.GetAnalysisSummary();

        Assert.Contains("### Archetype Distribution", result);
        Assert.Contains("service", result);
        Assert.Contains("model", result);
    }

    [Fact]
    public void GetAnalysisSummary_CorruptReport_ReturnsError()
    {
        File.WriteAllText(DependencyAnalyzer.AnalysisReportPath(TempDir), "not json");

        // Should return an ERROR string (wrapped by the try/catch)
        var result = AnalysisTool.GetAnalysisSummary();
        Assert.Contains("ERROR", result);
    }

    // ═══════════════════════════════════════
    // Error handling (try/catch wrappers)
    // ═══════════════════════════════════════

    [Fact]
    public void GetClassMetrics_CorruptData_ReturnsNotFound()
    {
        // Corrupt JSONL lines are silently skipped → empty list → not found
        File.WriteAllText(DependencyAnalyzer.ClassMetricsPath(TempDir), "not json\n");

        var result = AnalysisTool.GetClassMetrics("Anything");
        Assert.Contains("not found", result);
    }

    [Fact]
    public void GetDependencyGraph_CorruptData_ReturnsNotFound()
    {
        // Corrupt JSONL lines are silently skipped → empty edge list → not found
        File.WriteAllText(DependencyAnalyzer.DependencyGraphPath(TempDir), "bad data\n");

        var result = AnalysisTool.GetDependencyGraph("Anything");
        Assert.Contains("not found", result);
    }
}
