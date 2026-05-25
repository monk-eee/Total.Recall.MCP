using System.Text;
using System.Text.Json;
using Total.Recall.Infrastructure;
using Total.Recall.Models;

namespace Total.Recall.Scanners;

/// <summary>
/// Static analysis pass that runs after AssemblyScanner.
/// Reads type-registry.jsonl and coverage-gaps.jsonl, computes per-class metrics,
/// builds a dependency graph, detects clusters, and writes:
///   - class-metrics.jsonl    (one ClassMetrics per type)
///   - dependency-graph.jsonl (one DependencyEdge per dependency)
///   - analysis-report.json   (summary with clusters, hot interfaces, mermaid diagram)
/// </summary>
public static class DependencyAnalyzer
{
    /// <summary>
    /// Run static analysis on the type registry and optionally cross-reference with coverage data.
    /// Returns (metricsCount, edgeCount).
    /// </summary>
    public static (int Metrics, int Edges) Analyze(string dataDir)
    {
        var typeStore = new JsonLineStore<TypeRecord>(RepoConfig.TypeRegistryPath(dataDir));
        if (!typeStore.HasData())
            throw new InvalidOperationException("No type-registry.jsonl found. Run assembly scan first.");

        var types = typeStore.LoadAll();
        var typeMap = types
            .GroupBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        // Load coverage data if available (for TotalLines metric)
        var coverageStore = new JsonLineStore<CoverageGap>(RepoConfig.CoverageGapsPath(dataDir));
        var coverageMap = coverageStore.HasData()
            ? coverageStore.LoadAll()
                .GroupBy(c => c.Class, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, CoverageGap>(StringComparer.OrdinalIgnoreCase);

        // ── Phase 1: Build edges ──
        var edges = BuildEdges(types);

        // ── Phase 2: Compute per-class metrics ──
        var afferentMap = BuildAfferentMap(edges, types);
        var metrics = ComputeMetrics(types, edges, afferentMap, coverageMap, typeMap);

        // ── Phase 3: Detect clusters ──
        var clusters = DetectClusters(metrics, edges);
        AssignClusters(metrics, clusters);

        // ── Phase 4: Write outputs ──
        var metricsStore = new JsonLineStore<ClassMetrics>(ClassMetricsPath(dataDir));
        metricsStore.WriteAll(metrics);

        var edgeStore = new JsonLineStore<DependencyEdge>(DependencyGraphPath(dataDir));
        edgeStore.WriteAll(edges);

        // Write analysis report (standard JSON, not JSONL)
        var report = BuildReport(metrics, edges, clusters, types);
        var reportJson = JsonSerializer.Serialize(report, SharedJsonOptions.CamelCaseIndented);
        File.WriteAllText(AnalysisReportPath(dataDir), reportJson);

        // Write mermaid diagram as standalone .md file
        File.WriteAllText(MermaidPath(dataDir), $"# Dependency Graph\n\n```mermaid\n{report.MermaidGraph}\n```\n");

        return (metrics.Count, edges.Count);
    }

    // ── Path helpers ──

    public static string ClassMetricsPath(string dataDir) => Path.Combine(dataDir, "class-metrics.jsonl");
    public static string DependencyGraphPath(string dataDir) => Path.Combine(dataDir, "dependency-graph.jsonl");
    public static string AnalysisReportPath(string dataDir) => Path.Combine(dataDir, "analysis-report.json");
    public static string MermaidPath(string dataDir) => Path.Combine(dataDir, "dependency-graph.md");

    // ── Phase 1: Edge extraction ──

    private static List<DependencyEdge> BuildEdges(List<TypeRecord> types)
    {
        var edges = new List<DependencyEdge>();

        foreach (var type in types)
        {
            if (type.IsInterface || type.IsEnum)
                continue;

            // Constructor injection edges
            foreach (var ctor in type.Constructors)
            {
                foreach (var paramStr in ctor.Params)
                {
                    var (paramType, paramName) = ParamHelper.ParseParam(paramStr);
                    var kind = ParamHelper.IsInterfaceLike(paramType) ? "ctor-interface" : "ctor-concrete";

                    edges.Add(new DependencyEdge
                    {
                        From = type.Name,
                        To = paramType,
                        Kind = kind,
                        ParamName = paramName
                    });
                }
            }

            // Base type edge
            if (!string.IsNullOrEmpty(type.BaseType) && type.BaseType != "Object" && type.BaseType != "ValueType" && type.BaseType != "Enum")
            {
                edges.Add(new DependencyEdge
                {
                    From = type.Name,
                    To = type.BaseType,
                    Kind = "base-type"
                });
            }

            // Interface implementation edges
            foreach (var iface in type.Interfaces)
            {
                // Skip very common framework interfaces (noise)
                if (IsFrameworkInterface(iface))
                    continue;

                edges.Add(new DependencyEdge
                {
                    From = type.Name,
                    To = iface,
                    Kind = "implements"
                });
            }
        }

        return edges;
    }

    /// <summary>
    /// Build a map of type name → list of classes that depend on it (afferent coupling).
    /// Only counts ctor-injection and base-type edges (not "implements" — those go outward).
    /// </summary>
    private static Dictionary<string, List<string>> BuildAfferentMap(List<DependencyEdge> edges, List<TypeRecord> types)
    {
        var map = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        // Initialize all known types
        foreach (var t in types)
            map[t.Name] = [];

        // Count incoming ctor-injection and base-type edges
        foreach (var edge in edges)
        {
            if (edge.Kind is "ctor-interface" or "ctor-concrete" or "base-type")
            {
                if (!map.ContainsKey(edge.To))
                    map[edge.To] = [];
                map[edge.To].Add(edge.From);
            }
        }

        return map;
    }

    // ── Phase 2: Metrics computation ──

    private static List<ClassMetrics> ComputeMetrics(
        List<TypeRecord> types,
        List<DependencyEdge> edges,
        Dictionary<string, List<string>> afferentMap,
        Dictionary<string, CoverageGap> coverageMap,
        Dictionary<string, TypeRecord> typeMap)
    {
        // Build efferent map: class → outgoing ctor/base deps
        var efferentMap = edges
            .Where(e => e.Kind is "ctor-interface" or "ctor-concrete" or "base-type")
            .GroupBy(e => e.From, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Select(e => e.To).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                StringComparer.OrdinalIgnoreCase);

        var results = new List<ClassMetrics>();

        foreach (var type in types)
        {
            var ca = afferentMap.TryGetValue(type.Name, out var dependents) ? dependents.Count : 0;
            var ce = efferentMap.TryGetValue(type.Name, out var deps) ? deps.Count : 0;
            var instability = (ca + ce) > 0 ? (double)ce / (ca + ce) : 0.0;

            // Count public methods (rough: count properties first, subtract from total)
            var methodCount = type.Properties.Count(p => true); // Properties are already counted
            // Note: TypeRecord doesn't track methods separately — we use properties + ctor count as proxy

            var maxCtorParams = type.Constructors.Count > 0
                ? type.Constructors.Max(c => c.Params.Count)
                : 0;

            // Inheritance depth: walk BaseType chain through type map
            var inheritanceDepth = ComputeInheritanceDepth(type, typeMap);

            // Total lines from coverage
            var totalLines = coverageMap.TryGetValue(type.Name, out var gap) ? gap.TotalLines : 0;

            var m = new ClassMetrics
            {
                Class = type.Name,
                Namespace = type.Namespace,
                AfferentCoupling = ca,
                EfferentCoupling = ce,
                Instability = Math.Round(instability, 3),
                PublicMethodCount = 0, // Not available from TypeRecord currently
                PropertyCount = type.Properties.Count,
                TotalLines = totalLines,
                MaxCtorParams = maxCtorParams,
                InterfaceCount = type.Interfaces.Count,
                InheritanceDepth = inheritanceDepth,
                IsAbstract = type.IsAbstract,
                IsStatic = type.IsStatic,
                IsInterface = type.IsInterface,
                IsEnum = type.IsEnum,
                Archetype = ClassifyArchetype(type),
                DependsOn = deps?.ToList() ?? [],
                DependedOnBy = dependents?.ToList() ?? []
            };

            results.Add(m);
        }

        return results;
    }

    private static int ComputeInheritanceDepth(TypeRecord type, Dictionary<string, TypeRecord> typeMap)
    {
        if (type.IsInterface || type.IsEnum || type.IsStatic)
            return 0;

        var depth = 1; // Object is depth 0, direct subclass is 1
        var current = type.BaseType;
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { type.Name };

        while (!string.IsNullOrEmpty(current) && current != "Object" && current != "ValueType")
        {
            if (!visited.Add(current))
                break; // Cycle guard

            depth++;
            if (typeMap.TryGetValue(current, out var parent))
                current = parent.BaseType;
            else
                break; // Base type not in our assembly
        }

        return depth;
    }

    private static string ClassifyArchetype(TypeRecord type)
    {
        if (type.IsInterface) return "interface";
        if (type.IsEnum) return "enum";
        if (type.IsStatic) return "static-helper";

        var name = type.Name;

        if (name.EndsWith("Service", StringComparison.Ordinal)) return "service";
        if (name.EndsWith("Handler", StringComparison.Ordinal)) return "handler";
        if (name.EndsWith("Factory", StringComparison.Ordinal)) return "factory";
        if (name.EndsWith("Provider", StringComparison.Ordinal)) return "provider";
        if (name.EndsWith("Repository", StringComparison.Ordinal)) return "repository";
        if (name.EndsWith("Controller", StringComparison.Ordinal)) return "controller";
        if (name.EndsWith("Middleware", StringComparison.Ordinal)) return "middleware";
        if (name.EndsWith("Helper", StringComparison.Ordinal) || name.EndsWith("Helpers", StringComparison.Ordinal)) return "helper";
        if (name.EndsWith("Extensions", StringComparison.Ordinal)) return "extension";
        if (name.EndsWith("Converter", StringComparison.Ordinal)) return "converter";
        if (name.EndsWith("Builder", StringComparison.Ordinal)) return "builder";
        if (name.EndsWith("Parser", StringComparison.Ordinal)) return "parser";
        if (name.EndsWith("Validator", StringComparison.Ordinal)) return "validator";
        if (name.EndsWith("Adapter", StringComparison.Ordinal)) return "adapter";
        if (name.EndsWith("Exception", StringComparison.Ordinal)) return "exception";

        // Models: parameterless ctors with properties, or pure data types
        if (type.Constructors.Any(c => c.Params.Count == 0) && type.Properties.Count > 0)
            return "model";

        // Service-like: has interface deps in ctor
        if (type.Constructors.Any(c => c.Params.Any(p => ParamHelper.IsInterfaceLike(ParamHelper.ExtractTypeName(p)))))
            return "service";

        return "other";
    }

    // ── Phase 3: Cluster detection ──

    /// <summary>
    /// Simple connected-components clustering on ctor-injection edges.
    /// Classes that share the same interface dependencies get grouped together.
    /// </summary>
    private static List<DependencyCluster> DetectClusters(List<ClassMetrics> metrics, List<DependencyEdge> edges)
    {
        // Build adjacency: two classes are connected if they share a ctor dependency target
        var classToDepTargets = edges
            .Where(e => e.Kind is "ctor-interface" or "ctor-concrete")
            .GroupBy(e => e.From, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Select(e => e.To).ToHashSet(StringComparer.OrdinalIgnoreCase),
                StringComparer.OrdinalIgnoreCase);

        // Invert: dependency target → set of consumers
        var targetToConsumers = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (cls, targets) in classToDepTargets)
        {
            foreach (var target in targets)
            {
                if (!targetToConsumers.ContainsKey(target))
                    targetToConsumers[target] = [];
                targetToConsumers[target].Add(cls);
            }
        }

        // Union-Find for clustering
        var parent = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        string Find(string x)
        {
            if (!parent.ContainsKey(x)) parent[x] = x;
            if (parent[x] != x) parent[x] = Find(parent[x]);
            return parent[x];
        }

        void Union(string a, string b)
        {
            var ra = Find(a);
            var rb = Find(b);
            if (ra != rb) parent[ra] = rb;
        }

        // Connect classes that share at least 2 common dependencies (tight coupling signal)
        var classNames = classToDepTargets.Keys.ToList();
        for (int i = 0; i < classNames.Count; i++)
        {
            for (int j = i + 1; j < classNames.Count; j++)
            {
                var shared = classToDepTargets[classNames[i]]
                    .Intersect(classToDepTargets[classNames[j]], StringComparer.OrdinalIgnoreCase)
                    .Count();

                if (shared >= 2)
                    Union(classNames[i], classNames[j]);
            }
        }

        // Build cluster groups
        var groups = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var cls in classNames)
        {
            var root = Find(cls);
            if (!groups.ContainsKey(root))
                groups[root] = [];
            groups[root].Add(cls);
        }

        // Only keep clusters with 2+ members
        var clusters = new List<DependencyCluster>();
        var id = 0;
        foreach (var (_, members) in groups.Where(g => g.Value.Count >= 2).OrderByDescending(g => g.Value.Count))
        {
            // Find shared interfaces across cluster members
            var sharedInterfaces = members
                .Where(m => classToDepTargets.ContainsKey(m))
                .Select(m => classToDepTargets[m])
                .Aggregate((a, b) => a.Intersect(b, StringComparer.OrdinalIgnoreCase).ToHashSet(StringComparer.OrdinalIgnoreCase))
                .Where(i => ParamHelper.IsInterfaceLike(i))
                .ToList();

            var memberMetrics = metrics.Where(m => members.Contains(m.Class, StringComparer.OrdinalIgnoreCase)).ToList();
            var avgInstability = memberMetrics.Count > 0 ? memberMetrics.Average(m => m.Instability) : 0;

            clusters.Add(new DependencyCluster
            {
                Id = id,
                Classes = members.OrderBy(m => m).ToList(),
                SharedInterfaces = sharedInterfaces.OrderBy(i => i).ToList(),
                Size = members.Count,
                AvgInstability = Math.Round(avgInstability, 3)
            });
            id++;
        }

        return clusters;
    }

    private static void AssignClusters(List<ClassMetrics> metrics, List<DependencyCluster> clusters)
    {
        var classToCluster = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var cluster in clusters)
        {
            foreach (var cls in cluster.Classes)
                classToCluster[cls] = cluster.Id;
        }

        foreach (var m in metrics)
        {
            if (classToCluster.TryGetValue(m.Class, out var clusterId))
                m.Cluster = clusterId;
        }
    }

    // ── Phase 4: Report generation ──

    private static AnalysisReport BuildReport(List<ClassMetrics> metrics, List<DependencyEdge> edges,
        List<DependencyCluster> clusters, List<TypeRecord> types)
    {
        // Hot interfaces: most consumed via ctor injection
        var hotInterfaces = edges
            .Where(e => e.Kind == "ctor-interface")
            .GroupBy(e => e.To, StringComparer.OrdinalIgnoreCase)
            .Select(g => new HotInterface
            {
                Name = g.Key,
                Consumers = g.Select(e => e.From).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                ConsumerNames = g.Select(e => e.From).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(n => n).ToList()
            })
            .OrderByDescending(h => h.Consumers)
            .Take(10)
            .ToList();

        // Most coupled classes (highest efferent coupling)
        var mostCoupled = metrics
            .Where(m => !m.IsInterface && !m.IsEnum)
            .OrderByDescending(m => m.EfferentCoupling)
            .Take(10)
            .Select(m => $"{m.Class} (Ce={m.EfferentCoupling})")
            .ToList();

        // Isolated classes (no dependencies and no dependents, non-interface, non-enum)
        var isolated = metrics
            .Where(m => !m.IsInterface && !m.IsEnum && m.AfferentCoupling == 0 && m.EfferentCoupling == 0)
            .Select(m => m.Class)
            .OrderBy(n => n)
            .ToList();

        // Mermaid graph (top 30 most connected classes to keep it readable)
        var mermaid = GenerateMermaid(metrics, edges, clusters);

        return new AnalysisReport
        {
            AnalyzedUtc = DateTime.UtcNow.ToString("o"),
            TotalTypes = types.Count,
            TotalEdges = edges.Count,
            TotalClusters = clusters.Count,
            HotInterfaces = hotInterfaces,
            MostCoupled = mostCoupled,
            IsolatedClasses = isolated,
            MermaidGraph = mermaid
        };
    }

    private static string GenerateMermaid(List<ClassMetrics> metrics, List<DependencyEdge> edges,
        List<DependencyCluster> clusters)
    {
        var sb = new StringBuilder();
        sb.AppendLine("flowchart LR");

        // Focus on classes with coupling (skip isolated, interfaces, enums)
        var interesting = metrics
            .Where(m => !m.IsInterface && !m.IsEnum && (m.AfferentCoupling > 0 || m.EfferentCoupling > 0))
            .OrderByDescending(m => m.AfferentCoupling + m.EfferentCoupling)
            .Take(40)
            .Select(m => m.Class)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (interesting.Count == 0)
        {
            sb.AppendLine("  NoData[No coupled classes found]");
            return sb.ToString();
        }

        // Add cluster subgraphs
        var clusterAssigned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var cluster in clusters.Where(c => c.Classes.Any(cl => interesting.Contains(cl))))
        {
            sb.AppendLine($"  subgraph Cluster{cluster.Id}[Cluster {cluster.Id}]");
            foreach (var cls in cluster.Classes.Where(c => interesting.Contains(c)))
            {
                var archetype = metrics.FirstOrDefault(m => m.Class == cls)?.Archetype ?? "other";
                sb.AppendLine($"    {MermaidId.Sanitize(cls)}[\"{cls}<br/><small>{archetype}</small>\"]");
                clusterAssigned.Add(cls);
            }
            sb.AppendLine("  end");
        }

        // Add unclustered nodes
        foreach (var cls in interesting.Where(c => !clusterAssigned.Contains(c)))
        {
            var archetype = metrics.FirstOrDefault(m => m.Class == cls)?.Archetype ?? "other";
            sb.AppendLine($"  {MermaidId.Sanitize(cls)}[\"{cls}<br/><small>{archetype}</small>\"]");
        }

        // Add edges (only for interesting classes)
        var rendered = new HashSet<string>();
        foreach (var edge in edges)
        {
            if (!interesting.Contains(edge.From))
                continue;

            // For ctor-interface edges, draw to the interface node
            // For ctor-concrete and base-type, draw to concrete class
            var target = edge.To;
            var fromId = MermaidId.Sanitize(edge.From);
            var toId = MermaidId.Sanitize(target);
            var edgeKey = $"{fromId}->{toId}:{edge.Kind}";

            if (!rendered.Add(edgeKey))
                continue;

            var style = edge.Kind switch
            {
                "ctor-interface" => $"  {fromId} -.->|inject| {toId}",
                "ctor-concrete" => $"  {fromId} -->|concrete| {toId}",
                "base-type" => $"  {fromId} ==>|inherits| {toId}",
                "implements" => $"  {fromId} -.->|impl| {toId}",
                _ => $"  {fromId} --> {toId}"
            };

            sb.AppendLine(style);
        }

        // Style clusters
        foreach (var cluster in clusters.Where(c => c.Classes.Any(cl => interesting.Contains(cl))))
        {
            sb.AppendLine($"  style Cluster{cluster.Id} fill:#f5f5f5,stroke:#999,stroke-width:2px");
        }

        return sb.ToString();
    }

    private static bool IsFrameworkInterface(string name)
    {
        return name is "IDisposable" or "IAsyncDisposable" or "IEnumerable" or "IEnumerator"
            or "IComparable" or "IEquatable`1" or "ICloneable" or "IFormattable"
            or "IConvertible" or "ISerializable";
    }
}
