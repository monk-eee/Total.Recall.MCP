using System.Text.Json;
using Total.Recall.Infrastructure;
using Total.Recall.Models;
using Total.Recall.Scanners;

namespace Total.Recall.Tests.Scanners;

/// <summary>
/// Tests for DependencyAnalyzer.Analyze — static analysis on type-registry.jsonl
/// producing class-metrics.jsonl, dependency-graph.jsonl, analysis-report.json, and mermaid diagram.
/// </summary>
public sealed class DependencyAnalyzerTests : IDisposable
{
    private readonly string _tempDir;

    public DependencyAnalyzerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "total-recall-tests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private void SeedTypes(params TypeRecord[] records)
    {
        var store = new JsonLineStore<TypeRecord>(RepoConfig.TypeRegistryPath(_tempDir));
        store.WriteAll(records);
    }

    private void SeedCoverageGaps(params CoverageGap[] records)
    {
        var store = new JsonLineStore<CoverageGap>(RepoConfig.CoverageGapsPath(_tempDir));
        store.WriteAll(records);
    }

    private List<ClassMetrics> LoadMetrics()
    {
        var store = new JsonLineStore<ClassMetrics>(DependencyAnalyzer.ClassMetricsPath(_tempDir));
        return store.LoadAll();
    }

    private List<DependencyEdge> LoadEdges()
    {
        var store = new JsonLineStore<DependencyEdge>(DependencyAnalyzer.DependencyGraphPath(_tempDir));
        return store.LoadAll();
    }

    private AnalysisReport LoadReport()
    {
        var json = File.ReadAllText(DependencyAnalyzer.AnalysisReportPath(_tempDir));
        return JsonSerializer.Deserialize<AnalysisReport>(json, SharedJsonOptions.CamelCase)!;
    }

    // ── No data ──

    [Fact]
    public void Analyze_NoData_ThrowsInvalidOperation()
    {
        Assert.Throws<InvalidOperationException>(() => DependencyAnalyzer.Analyze(_tempDir));
    }

    // ── Basic edge extraction ──

    [Fact]
    public void Analyze_CtorInterfaceParam_CreatesCtorInterfaceEdge()
    {
        SeedTypes(
            new TypeRecord { Name = "MyService", Namespace = "App", Constructors = [new ConstructorRecord { Params = ["ILogger _logger"] }] },
            new TypeRecord { Name = "ILogger", Namespace = "Microsoft", IsInterface = true }
        );

        var (metricsCount, edgeCount) = DependencyAnalyzer.Analyze(_tempDir);
        Assert.True(metricsCount > 0);
        Assert.True(edgeCount > 0);

        var edges = LoadEdges();
        var ctorEdge = edges.FirstOrDefault(e => e.From == "MyService" && e.To == "ILogger");
        Assert.NotNull(ctorEdge);
        Assert.Equal("ctor-interface", ctorEdge.Kind);
        Assert.Equal("logger", ctorEdge.ParamName);
    }

    [Fact]
    public void Analyze_CtorConcreteParam_CreatesCtorConcreteEdge()
    {
        SeedTypes(
            new TypeRecord { Name = "Worker", Namespace = "App", Constructors = [new ConstructorRecord { Params = ["Config _config"] }] },
            new TypeRecord { Name = "Config", Namespace = "App" }
        );

        DependencyAnalyzer.Analyze(_tempDir);
        var edges = LoadEdges();

        var ctorEdge = edges.FirstOrDefault(e => e.From == "Worker" && e.To == "Config");
        Assert.NotNull(ctorEdge);
        Assert.Equal("ctor-concrete", ctorEdge.Kind);
    }

    [Fact]
    public void Analyze_BaseType_CreatesBaseTypeEdge()
    {
        SeedTypes(
            new TypeRecord { Name = "SpecialService", Namespace = "App", BaseType = "BaseService" },
            new TypeRecord { Name = "BaseService", Namespace = "App" }
        );

        DependencyAnalyzer.Analyze(_tempDir);
        var edges = LoadEdges();

        var baseEdge = edges.FirstOrDefault(e => e.From == "SpecialService" && e.To == "BaseService");
        Assert.NotNull(baseEdge);
        Assert.Equal("base-type", baseEdge.Kind);
    }

    [Fact]
    public void Analyze_ImplementsInterface_CreatesImplementsEdge()
    {
        SeedTypes(
            new TypeRecord { Name = "MyService", Namespace = "App", Interfaces = ["IMyService", "IDisposable"] }
        );

        DependencyAnalyzer.Analyze(_tempDir);
        var edges = LoadEdges();

        // IMyService should get an "implements" edge
        Assert.Contains(edges, e => e.From == "MyService" && e.To == "IMyService" && e.Kind == "implements");
        // IDisposable is a framework interface — should be filtered out
        Assert.DoesNotContain(edges, e => e.To == "IDisposable");
    }

    [Fact]
    public void Analyze_InterfaceOrEnum_NoEdgesExtracted()
    {
        SeedTypes(
            new TypeRecord { Name = "IService", Namespace = "App", IsInterface = true, Constructors = [new ConstructorRecord { Params = ["ILogger _x"] }] },
            new TypeRecord { Name = "Status", Namespace = "App", IsEnum = true }
        );

        DependencyAnalyzer.Analyze(_tempDir);
        var edges = LoadEdges();

        Assert.DoesNotContain(edges, e => e.From == "IService");
        Assert.DoesNotContain(edges, e => e.From == "Status");
    }

    // ── Metrics computation ──

    [Fact]
    public void Analyze_ComputesCouplingMetrics()
    {
        SeedTypes(
            new TypeRecord { Name = "ServiceA", Namespace = "App", Constructors = [new ConstructorRecord { Params = ["ILogger _log", "IRepo _repo"] }] },
            new TypeRecord { Name = "ServiceB", Namespace = "App", Constructors = [new ConstructorRecord { Params = ["ILogger _log"] }] },
            new TypeRecord { Name = "ILogger", Namespace = "Microsoft", IsInterface = true },
            new TypeRecord { Name = "IRepo", Namespace = "App", IsInterface = true }
        );

        DependencyAnalyzer.Analyze(_tempDir);
        var metrics = LoadMetrics();

        var a = metrics.First(m => m.Class == "ServiceA");
        Assert.Equal(2, a.EfferentCoupling); // depends on ILogger + IRepo
        Assert.Equal(0, a.AfferentCoupling); // nobody depends on it

        var logger = metrics.First(m => m.Class == "ILogger");
        Assert.Equal(2, logger.AfferentCoupling); // ServiceA + ServiceB depend on it
    }

    [Fact]
    public void Analyze_Instability_CalculatedCorrectly()
    {
        // ServiceA depends on ILogger (Ce=1) and nothing depends on ServiceA (Ca=0)
        // Instability = Ce / (Ca + Ce) = 1 / (0 + 1) = 1.0
        SeedTypes(
            new TypeRecord { Name = "ServiceA", Namespace = "App", Constructors = [new ConstructorRecord { Params = ["ILogger _log"] }] },
            new TypeRecord { Name = "ILogger", Namespace = "Microsoft", IsInterface = true }
        );

        DependencyAnalyzer.Analyze(_tempDir);
        var metrics = LoadMetrics();

        var a = metrics.First(m => m.Class == "ServiceA");
        Assert.Equal(1.0, a.Instability);
    }

    [Fact]
    public void Analyze_InheritanceDepth_CalculatedForChain()
    {
        SeedTypes(
            new TypeRecord { Name = "Level3", Namespace = "App", BaseType = "Level2" },
            new TypeRecord { Name = "Level2", Namespace = "App", BaseType = "Level1" },
            new TypeRecord { Name = "Level1", Namespace = "App", BaseType = "Object" }
        );

        DependencyAnalyzer.Analyze(_tempDir);
        var metrics = LoadMetrics();

        var l3 = metrics.First(m => m.Class == "Level3");
        Assert.True(l3.InheritanceDepth >= 3, $"Expected depth >= 3, got {l3.InheritanceDepth}");
    }

    // ── Archetype classification ──

    [Theory]
    [InlineData("UserService", "service")]
    [InlineData("RequestHandler", "handler")]
    [InlineData("WidgetFactory", "factory")]
    [InlineData("ConfigProvider", "provider")]
    [InlineData("UserRepository", "repository")]
    [InlineData("HomeController", "controller")]
    [InlineData("AuthMiddleware", "middleware")]
    [InlineData("StringHelper", "helper")]
    [InlineData("StringExtensions", "extension")]
    [InlineData("JsonConverter", "converter")]
    [InlineData("QueryBuilder", "builder")]
    [InlineData("HtmlParser", "parser")]
    [InlineData("InputValidator", "validator")]
    [InlineData("DbAdapter", "adapter")]
    [InlineData("CustomException", "exception")]
    public void Analyze_ArchetypeClassification_ByNameSuffix(string className, string expectedArchetype)
    {
        SeedTypes(new TypeRecord { Name = className, Namespace = "App" });

        DependencyAnalyzer.Analyze(_tempDir);
        var metrics = LoadMetrics();

        var m = metrics.First(m => m.Class == className);
        Assert.Equal(expectedArchetype, m.Archetype);
    }

    [Fact]
    public void Analyze_InterfaceArchetype_ClassifiedAsInterface()
    {
        SeedTypes(new TypeRecord { Name = "IService", Namespace = "App", IsInterface = true });

        DependencyAnalyzer.Analyze(_tempDir);
        var metrics = LoadMetrics();

        Assert.Equal("interface", metrics.First(m => m.Class == "IService").Archetype);
    }

    [Fact]
    public void Analyze_EnumArchetype_ClassifiedAsEnum()
    {
        SeedTypes(new TypeRecord { Name = "Status", Namespace = "App", IsEnum = true });

        DependencyAnalyzer.Analyze(_tempDir);
        var metrics = LoadMetrics();

        Assert.Equal("enum", metrics.First(m => m.Class == "Status").Archetype);
    }

    [Fact]
    public void Analyze_StaticClass_ClassifiedAsStaticHelper()
    {
        SeedTypes(new TypeRecord { Name = "Utilities", Namespace = "App", IsStatic = true });

        DependencyAnalyzer.Analyze(_tempDir);
        var metrics = LoadMetrics();

        Assert.Equal("static-helper", metrics.First(m => m.Class == "Utilities").Archetype);
    }

    [Fact]
    public void Analyze_ModelClass_ClassifiedAsModel()
    {
        SeedTypes(new TypeRecord
        {
            Name = "UserDto",
            Namespace = "App",
            Constructors = [new ConstructorRecord { Params = [] }],
            Properties = [new PropertyRecord { Name = "Name", ClrType = "string", HasSet = true }]
        });

        DependencyAnalyzer.Analyze(_tempDir);
        var metrics = LoadMetrics();

        Assert.Equal("model", metrics.First(m => m.Class == "UserDto").Archetype);
    }

    // ── Cluster detection ──

    [Fact]
    public void Analyze_SharedDependencies_CreatesCluster()
    {
        // ServiceA and ServiceB both depend on ILogger and IRepo → should be clustered
        SeedTypes(
            new TypeRecord { Name = "ServiceA", Namespace = "App", Constructors = [new ConstructorRecord { Params = ["ILogger _log", "IRepo _repo"] }] },
            new TypeRecord { Name = "ServiceB", Namespace = "App", Constructors = [new ConstructorRecord { Params = ["ILogger _log", "IRepo _repo"] }] },
            new TypeRecord { Name = "Standalone", Namespace = "App" },
            new TypeRecord { Name = "ILogger", Namespace = "Microsoft", IsInterface = true },
            new TypeRecord { Name = "IRepo", Namespace = "App", IsInterface = true }
        );

        DependencyAnalyzer.Analyze(_tempDir);
        var metrics = LoadMetrics();

        var a = metrics.First(m => m.Class == "ServiceA");
        var b = metrics.First(m => m.Class == "ServiceB");
        var s = metrics.First(m => m.Class == "Standalone");

        // A and B should be in the same cluster
        Assert.True(a.Cluster >= 0, "ServiceA should be in a cluster");
        Assert.Equal(a.Cluster, b.Cluster);

        // Standalone should not be in a cluster
        Assert.Equal(-1, s.Cluster);
    }

    // ── Output files ──

    [Fact]
    public void Analyze_WritesAllOutputFiles()
    {
        SeedTypes(
            new TypeRecord { Name = "SomeService", Namespace = "App", Constructors = [new ConstructorRecord { Params = ["ILogger _log"] }] },
            new TypeRecord { Name = "ILogger", Namespace = "Microsoft", IsInterface = true }
        );

        DependencyAnalyzer.Analyze(_tempDir);

        Assert.True(File.Exists(DependencyAnalyzer.ClassMetricsPath(_tempDir)));
        Assert.True(File.Exists(DependencyAnalyzer.DependencyGraphPath(_tempDir)));
        Assert.True(File.Exists(DependencyAnalyzer.AnalysisReportPath(_tempDir)));
        Assert.True(File.Exists(DependencyAnalyzer.MermaidPath(_tempDir)));
    }

    // ── Analysis report ──

    [Fact]
    public void Analyze_ReportContainsExpectedFields()
    {
        SeedTypes(
            new TypeRecord { Name = "ServiceA", Namespace = "App", Constructors = [new ConstructorRecord { Params = ["ILogger _log", "IRepo _repo"] }] },
            new TypeRecord { Name = "ServiceB", Namespace = "App", Constructors = [new ConstructorRecord { Params = ["ILogger _log"] }] },
            new TypeRecord { Name = "IsolatedClass", Namespace = "App" },
            new TypeRecord { Name = "ILogger", Namespace = "Microsoft", IsInterface = true },
            new TypeRecord { Name = "IRepo", Namespace = "App", IsInterface = true }
        );

        DependencyAnalyzer.Analyze(_tempDir);
        var report = LoadReport();

        Assert.Equal(5, report.TotalTypes);
        Assert.True(report.TotalEdges > 0);
        Assert.NotEmpty(report.AnalyzedUtc);

        // ILogger should be a hot interface (consumed by ServiceA + ServiceB)
        Assert.Contains(report.HotInterfaces, h => h.Name == "ILogger" && h.Consumers == 2);

        // IsolatedClass should be in isolated list
        Assert.Contains("IsolatedClass", report.IsolatedClasses);
    }

    [Fact]
    public void Analyze_MermaidDiagram_ContainsFlowchart()
    {
        SeedTypes(
            new TypeRecord { Name = "Worker", Namespace = "App", Constructors = [new ConstructorRecord { Params = ["ILogger _log"] }] },
            new TypeRecord { Name = "ILogger", Namespace = "Microsoft", IsInterface = true }
        );

        DependencyAnalyzer.Analyze(_tempDir);

        var mermaidContent = File.ReadAllText(DependencyAnalyzer.MermaidPath(_tempDir));
        Assert.Contains("```mermaid", mermaidContent);
        Assert.Contains("flowchart LR", mermaidContent);
    }

    // ── Coverage cross-reference ──

    [Fact]
    public void Analyze_WithCoverage_SetsTotalLinesInMetrics()
    {
        SeedTypes(new TypeRecord { Name = "MyClass", Namespace = "App" });
        SeedCoverageGaps(new CoverageGap
        {
            ClassName = "App.MyClass",
            LinesTotal = 150,
            LinesCovered = 100
        });

        DependencyAnalyzer.Analyze(_tempDir);
        var metrics = LoadMetrics();

        var m = metrics.First(m => m.Class == "MyClass");
        Assert.Equal(150, m.TotalLines);
    }

    // ── BaseType filters ──

    [Theory]
    [InlineData("Object")]
    [InlineData("ValueType")]
    [InlineData("Enum")]
    public void Analyze_FrameworkBaseTypes_NotIncludedAsEdges(string baseType)
    {
        SeedTypes(new TypeRecord { Name = "MyType", Namespace = "App", BaseType = baseType });

        DependencyAnalyzer.Analyze(_tempDir);
        var edges = LoadEdges();

        Assert.DoesNotContain(edges, e => e.To == baseType);
    }

    // ── Path helpers ──

    [Fact]
    public void PathHelpers_ReturnExpectedPaths()
    {
        var dir = "/some/dir";
        Assert.EndsWith("class-metrics.jsonl", DependencyAnalyzer.ClassMetricsPath(dir));
        Assert.EndsWith("dependency-graph.jsonl", DependencyAnalyzer.DependencyGraphPath(dir));
        Assert.EndsWith("analysis-report.json", DependencyAnalyzer.AnalysisReportPath(dir));
        Assert.EndsWith("dependency-graph.md", DependencyAnalyzer.MermaidPath(dir));
    }

    // ── Counting returned values ──

    [Fact]
    public void Analyze_ReturnsTuple_WithCorrectCounts()
    {
        SeedTypes(
            new TypeRecord { Name = "Svc", Namespace = "App", Constructors = [new ConstructorRecord { Params = ["ILogger _log"] }] },
            new TypeRecord { Name = "ILogger", Namespace = "Microsoft", IsInterface = true }
        );

        var (metricsCount, edgesCount) = DependencyAnalyzer.Analyze(_tempDir);
        Assert.Equal(2, metricsCount); // 2 types → 2 metrics records
        Assert.True(edgesCount >= 1); // at least the ctor-interface edge
    }

    // ── Multiple constructors ──

    [Fact]
    public void Analyze_MaxCtorParams_UsesLargestCtor()
    {
        SeedTypes(new TypeRecord
        {
            Name = "MultiCtor",
            Namespace = "App",
            Constructors =
            [
                new ConstructorRecord { Params = ["ILogger _log"] },
                new ConstructorRecord { Params = ["ILogger _log", "IRepo _repo", "ICache _cache"] }
            ]
        });

        DependencyAnalyzer.Analyze(_tempDir);
        var metrics = LoadMetrics();

        var m = metrics.First(m => m.Class == "MultiCtor");
        Assert.Equal(3, m.MaxCtorParams);
    }

    // ── Service-like archetype via interface ctor params ──

    [Fact]
    public void Analyze_InterfaceCtorParams_ClassifiedAsService()
    {
        SeedTypes(new TypeRecord
        {
            Name = "ProcessorWorker",
            Namespace = "App",
            Constructors = [new ConstructorRecord { Params = ["IProcessor _proc"] }]
        });

        DependencyAnalyzer.Analyze(_tempDir);
        var metrics = LoadMetrics();

        Assert.Equal("service", metrics.First(m => m.Class == "ProcessorWorker").Archetype);
    }
}
