using System.Text.Json.Serialization;

namespace Total.Recall.Models;

/// <summary>
/// Static analysis metrics for a single class, computed from type registry + coverage data.
/// One record per class in class-metrics.jsonl.
/// </summary>
public sealed class ClassMetrics
{
    [JsonPropertyName("class")]
    public string Class { get; set; } = "";

    [JsonPropertyName("namespace")]
    public string Namespace { get; set; } = "";

    // ── Coupling metrics ──

    /// <summary>Afferent coupling: how many other classes depend on this type (consume it as a ctor param).</summary>
    [JsonPropertyName("afferentCoupling")]
    public int AfferentCoupling { get; set; }

    /// <summary>Efferent coupling: how many types this class depends on (its ctor params).</summary>
    [JsonPropertyName("efferentCoupling")]
    public int EfferentCoupling { get; set; }

    /// <summary>Instability = Ce / (Ca + Ce). 0 = maximally stable (many dependents), 1 = maximally unstable.</summary>
    [JsonPropertyName("instability")]
    public double Instability { get; set; }

    // ── Size metrics ──

    /// <summary>Number of public instance methods (excluding property accessors and constructors).</summary>
    [JsonPropertyName("publicMethodCount")]
    public int PublicMethodCount { get; set; }

    /// <summary>Number of public properties.</summary>
    [JsonPropertyName("propertyCount")]
    public int PropertyCount { get; set; }

    /// <summary>Total lines from coverage data (0 if no coverage).</summary>
    [JsonPropertyName("totalLines")]
    public int TotalLines { get; set; }

    // ── Complexity indicators ──

    /// <summary>Max constructor parameter count (proxy for DI complexity).</summary>
    [JsonPropertyName("maxCtorParams")]
    public int MaxCtorParams { get; set; }

    /// <summary>Number of interfaces implemented.</summary>
    [JsonPropertyName("interfaceCount")]
    public int InterfaceCount { get; set; }

    /// <summary>Depth of inheritance (1 = direct from Object, 2+ = deeper).</summary>
    [JsonPropertyName("inheritanceDepth")]
    public int InheritanceDepth { get; set; }

    // ── Classification ──

    [JsonPropertyName("isAbstract")]
    public bool IsAbstract { get; set; }

    [JsonPropertyName("isStatic")]
    public bool IsStatic { get; set; }

    [JsonPropertyName("isInterface")]
    public bool IsInterface { get; set; }

    [JsonPropertyName("isEnum")]
    public bool IsEnum { get; set; }

    /// <summary>Heuristic classification: "service", "model", "extension", "handler", "factory", "helper", "other".</summary>
    [JsonPropertyName("archetype")]
    public string Archetype { get; set; } = "other";

    /// <summary>Dependency cluster ID (0-based). Classes in the same cluster share dependencies.</summary>
    [JsonPropertyName("cluster")]
    public int Cluster { get; set; } = -1;

    /// <summary>Names of types this class depends on (ctor params, resolved to concrete names where possible).</summary>
    [JsonPropertyName("dependsOn")]
    public List<string> DependsOn { get; set; } = [];

    /// <summary>Names of types that depend on this class.</summary>
    [JsonPropertyName("dependedOnBy")]
    public List<string> DependedOnBy { get; set; } = [];
}

/// <summary>
/// A single directed edge in the dependency graph.
/// One record per dependency in dependency-graph.jsonl.
/// </summary>
public sealed class DependencyEdge
{
    /// <summary>The class that has the dependency (consumer).</summary>
    [JsonPropertyName("from")]
    public string From { get; set; } = "";

    /// <summary>The type being depended on (interface or concrete param type).</summary>
    [JsonPropertyName("to")]
    public string To { get; set; } = "";

    /// <summary>How the dependency is expressed: "ctor-interface", "ctor-concrete", "base-type", "implements".</summary>
    [JsonPropertyName("kind")]
    public string Kind { get; set; } = "";

    /// <summary>The parameter name (for ctor dependencies) or null.</summary>
    [JsonPropertyName("paramName")]
    public string? ParamName { get; set; }
}

/// <summary>
/// Summary of a dependency cluster — a group of tightly-connected classes.
/// </summary>
public sealed class DependencyCluster
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("classes")]
    public List<string> Classes { get; set; } = [];

    [JsonPropertyName("sharedInterfaces")]
    public List<string> SharedInterfaces { get; set; } = [];

    /// <summary>Total classes in the cluster.</summary>
    [JsonPropertyName("size")]
    public int Size { get; set; }

    /// <summary>Average instability of classes in the cluster.</summary>
    [JsonPropertyName("avgInstability")]
    public double AvgInstability { get; set; }
}

/// <summary>
/// Top-level analysis result containing metrics, graph, and clusters.
/// Serialized as analysis-report.json (standard JSON, not JSONL).
/// </summary>
public sealed class AnalysisReport
{
    [JsonPropertyName("analyzedUtc")]
    public string AnalyzedUtc { get; set; } = "";

    [JsonPropertyName("totalTypes")]
    public int TotalTypes { get; set; }

    [JsonPropertyName("totalEdges")]
    public int TotalEdges { get; set; }

    [JsonPropertyName("totalClusters")]
    public int TotalClusters { get; set; }

    /// <summary>Top 10 most-depended-on interfaces (highest afferent coupling).</summary>
    [JsonPropertyName("hotInterfaces")]
    public List<HotInterface> HotInterfaces { get; set; } = [];

    /// <summary>Top 10 most coupled classes (highest efferent coupling).</summary>
    [JsonPropertyName("mostCoupled")]
    public List<string> MostCoupled { get; set; } = [];

    /// <summary>Classes with zero dependencies and zero dependents (isolated).</summary>
    [JsonPropertyName("isolatedClasses")]
    public List<string> IsolatedClasses { get; set; } = [];

    /// <summary>Mermaid flowchart source for visual dependency graph.</summary>
    [JsonPropertyName("mermaidGraph")]
    public string MermaidGraph { get; set; } = "";
}

public sealed class HotInterface
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("consumers")]
    public int Consumers { get; set; }

    [JsonPropertyName("consumerNames")]
    public List<string> ConsumerNames { get; set; } = [];
}
