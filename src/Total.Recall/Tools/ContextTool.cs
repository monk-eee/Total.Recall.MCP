using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using Total.Recall.Infrastructure;
using Total.Recall.Models;

namespace Total.Recall.Tools;

/// <summary>
/// Combined context tool — returns type record, gotchas, test inventory,
/// mock recipes, and assessments in a single call (replaces 5 separate tool calls).
/// Uses StoreRegistry singletons for cross-call caching.
/// </summary>
[McpServerToolType]
public static class ContextTool
{
    [McpServerTool, Description(
        "Get context for a type in one call. Depth controls how much data is returned: " +
        "'shallow' = type record only (constructors, properties, namespace) — ~50 tokens. " +
        "'standard' = type + coverage gap + gotchas + test inventory — ~200 tokens. " +
        "'full' = everything: type + coverage + gotchas + tests + mock recipes + assessments + session history + patterns — ~2000 tokens. " +
        "Use 'shallow' for quick type checks. Use 'standard' for most test authoring. " +
        "Use 'full' only when starting a new class or debugging failures. Default: 'standard'.")]
    public static string GetContext(
        [Description("The type name to look up (e.g. 'AuditEntry', 'IContentBase')")] string typeName,
        [Description("How much context to return: 'shallow' (type only), 'standard' (type+coverage+gotchas+tests), or 'full' (everything). Default: 'standard'")] string depth = "standard",
        [Description("Optional: namespace/session to query (default: server default)")] string? ns = null)
    {
        return Telemetry.Track("get_context", ns, new { typeName, depth, ns }, () =>
        {
        Metrics.Increment(Metrics.ToolGetContext);
        Log.Debug($"[GetContext] typeName='{typeName}' depth='{depth}' ns='{ns ?? "(default)"}'");
        try
        {
            return GetContextCore(typeName, depth, ns);
        }
        catch (Exception ex)
        {
            Log.Error($"[GetContext] failed for '{typeName}': {ex.GetType().Name}: {ex.Message}");
            return $"ERROR in GetContext: {ex.GetType().Name}: {ex.Message}";
        }
        });
    }

    private static string GetContextCore(string typeName, string depth, string? ns)
    {
        var stores = StoreRegistry.ForNamespace(ns);
        var isShallow = depth.Equals("shallow", StringComparison.OrdinalIgnoreCase);
        var isFull = depth.Equals("full", StringComparison.OrdinalIgnoreCase);

        // Use centralized 3-step type resolution (exact → CI → contains)
        var typeRecord = stores.ResolveType(typeName);
        Log.Debug($"[GetContext] type resolved: {(typeRecord is not null ? typeRecord.Name : "(null)")} depth={depth}");

        // SHALLOW: just the type record — minimal tokens
        if (isShallow)
        {
            return JsonSerializer.Serialize(new { type = typeRecord, depth = "shallow" }, SharedJsonOptions.CamelCaseIndented);
        }

        // STANDARD and FULL: always include gotchas, coverage, tests
        var gotchas = stores.Gotchas.Query(g =>
            g.Type.Contains(typeName, StringComparison.OrdinalIgnoreCase));
        Log.Debug($"[GetContext] gotchas={gotchas.Count}");

        var tests = stores.TestInventory.Query(t =>
            t.Class.Contains(typeName, StringComparison.OrdinalIgnoreCase));
        Log.Debug($"[GetContext] tests={tests.Count}");

        var coverageGap = stores.CoverageGaps.HasData()
            ? stores.CoverageGaps.LoadAll().FirstOrDefault(g =>
                g.Class.Equals(typeName, StringComparison.OrdinalIgnoreCase)
                || (typeRecord is not null && g.Class.Equals(typeRecord.Name, StringComparison.OrdinalIgnoreCase)))
            : null;

        // STANDARD: type + gotchas + tests + coverage — good enough for most test authoring
        if (!isFull)
        {
            var standardResult = new
            {
                type = typeRecord,
                gotchas,
                tests,
                coverageGap,
                depth = "standard"
            };
            return JsonSerializer.Serialize(standardResult, SharedJsonOptions.CamelCaseIndented);
        }

        // FULL: everything including mock recipes, assessments, session history, patterns

        // Get assessments for this type
        var assessments = stores.Assessments.HasData()
            ? stores.Assessments.Query(a =>
                a.Class.Contains(typeName, StringComparison.OrdinalIgnoreCase))
            : [];

        // Get mock recipes for interfaces this type implements
        var mockRecipes = new List<MockRecipe>();
        if (typeRecord?.Interfaces is { Count: > 0 })
        {
            var allMocks = stores.MockRecipes.LoadAll();
            Log.Debug($"[GetContext] searching {allMocks.Count} mock recipes for {typeRecord.Interfaces.Count} interfaces");
            foreach (var iface in typeRecord.Interfaces)
            {
                var normalized = iface.StartsWith("I") ? iface[1..] : iface;
                var recipe = allMocks.FirstOrDefault(m =>
                    m.Interface.Equals(iface, StringComparison.OrdinalIgnoreCase) ||
                    m.Interface.Equals("I" + normalized, StringComparison.OrdinalIgnoreCase));
                if (recipe is not null)
                    mockRecipes.Add(recipe);
            }
        }

        // Get session history involving this type
        var sessionHistory = new List<object>();
        if (stores.Sessions.HasData())
        {
            var allSessions = stores.Sessions.LoadAll();
            var matchName = typeRecord?.Name ?? typeName;
            foreach (var s in allSessions)
            {
                var attempted = s.ClassesAttempted.Any(c => c.Equals(matchName, StringComparison.OrdinalIgnoreCase));
                var succeeded = s.ClassesSucceeded.Any(c => c.Equals(matchName, StringComparison.OrdinalIgnoreCase));
                var failEntry = s.ClassesFailed.FirstOrDefault(f => f.Class.Equals(matchName, StringComparison.OrdinalIgnoreCase));

                if (attempted || succeeded || failEntry is not null)
                {
                    sessionHistory.Add(new
                    {
                        sessionId = s.SessionId,
                        date = s.StartedUtc,
                        model = s.Model,
                        attempted,
                        succeeded,
                        failed = failEntry is not null,
                        failReason = failEntry?.Reason
                    });
                }
            }
        }

        // Recommend test patterns based on type shape
        var recommendedPatterns = GetRecommendedPatterns(typeRecord, coverageGap);

        var result = new
        {
            type = typeRecord,
            gotchas,
            tests,
            mockRecipes,
            assessments,
            coverageGap,
            sessionHistory,
            recommendedPatterns,
            depth = "full"
        };

        return JsonSerializer.Serialize(result, SharedJsonOptions.CamelCaseIndented);
    }

    /// <summary>
    /// Analyzes type characteristics and returns recommended test patterns that the
    /// consuming AI agent should implement for comprehensive coverage.
    /// </summary>
    internal static List<string> GetRecommendedPatterns(TypeRecord? typeRecord, CoverageGap? coverageGap)
    {
        if (typeRecord is null) return [];

        var patterns = new List<string>();

        // IDisposable → dispose pattern test
        if (typeRecord.Interfaces.Any(i => i.Contains("IDisposable", StringComparison.OrdinalIgnoreCase)))
        {
            patterns.Add("DISPOSE: Implement IDisposable on test class. Verify Dispose() releases resources. Test that methods throw ObjectDisposedException after disposal.");
        }

        // Async methods → cancellation token tests
        var hasAsync = coverageGap?.UncoveredMethods.Any(m =>
            m.Name.EndsWith("Async", StringComparison.OrdinalIgnoreCase)) == true
            || typeRecord.Interfaces.Any(i => i.Contains("IAsync", StringComparison.OrdinalIgnoreCase));
        if (hasAsync)
        {
            patterns.Add("CANCELLATION: Pass CancellationToken.None for happy path. Create a pre-cancelled token via new CancellationTokenSource() and test that OperationCanceledException is thrown.");
        }

        // Event-like properties → event subscription tests
        if (typeRecord.Properties.Any(p =>
            p.ClrType.Contains("EventHandler") || p.ClrType.Contains("event")
            || p.Name.StartsWith("On", StringComparison.OrdinalIgnoreCase)))
        {
            patterns.Add("EVENTS: Subscribe before acting. Assert event was raised with correct args. Unsubscribe in cleanup to prevent test pollution.");
        }

        // Multiple constructors → test each ctor path
        if (typeRecord.Constructors.Count > 1)
        {
            patterns.Add($"MULTIPLE CTORS ({typeRecord.Constructors.Count}): Test object creation via each constructor overload. Verify default values when optional params are omitted.");
        }

        // Interface types → test all interface contracts
        if (typeRecord.Interfaces.Count > 0 && !typeRecord.IsInterface)
        {
            var ifaceList = string.Join(", ", typeRecord.Interfaces.Take(5));
            patterns.Add($"INTERFACE CONTRACTS ({ifaceList}): Cast _sut to each interface and test the contract methods. Ensures polymorphic behavior is correct.");
        }

        // Properties with setters → test property roundtrip
        var settableProps = typeRecord.Properties.Where(p => p.HasSet || p.HasInit).ToList();
        if (settableProps.Count > 0)
        {
            patterns.Add($"PROPERTY ROUNDTRIP: Test set+get for {settableProps.Count} settable properties. Verify values survive roundtrip and validation logic fires on set.");
        }

        // Enum type → test all enum values
        if (typeRecord.IsEnum && typeRecord.EnumValues is { Count: > 0 })
        {
            patterns.Add($"ENUM VALUES: Use [Theory] + [InlineData] to test behavior for all {typeRecord.EnumValues.Count} enum values. Include undefined/cast int values for robustness.");
        }

        // Has constructor with interface params → null guard tests
        var biggestCtor = typeRecord.Constructors.OrderByDescending(c => c.Params.Count).FirstOrDefault();
        var interfaceParams = biggestCtor?.Params
            .Where(p => ParamHelper.IsInterfaceLike(ParamHelper.ExtractTypeName(p)))
            .ToList() ?? [];

        if (interfaceParams.Count > 0)
        {
            patterns.Add($"NULL GUARDS: Test that constructor throws ArgumentNullException when each of the {interfaceParams.Count} interface parameters is null.");
        }

        // Base type suggesting pattern
        if (!string.IsNullOrEmpty(typeRecord.BaseType))
        {
            if (typeRecord.BaseType.Contains("Exception", StringComparison.OrdinalIgnoreCase))
                patterns.Add("EXCEPTION TYPE: Test message, inner exception, and serialization roundtrip. Verify custom properties are preserved.");
            else if (typeRecord.BaseType.Contains("Controller", StringComparison.OrdinalIgnoreCase))
                patterns.Add("CONTROLLER: Test each action returns correct ActionResult type. Verify model validation, status codes, and error responses.");
            else if (typeRecord.BaseType.Contains("Handler", StringComparison.OrdinalIgnoreCase))
                patterns.Add("HANDLER: Test Handle method with valid/invalid input. Verify correct delegation to dependencies and return values.");
        }

        // Static class → no instance, test static methods directly
        if (typeRecord.IsStatic)
        {
            patterns.Add("STATIC CLASS: Call methods directly (no _sut). Use [Collection] to prevent parallel test execution if methods use shared state.");
        }

        // Abstract class → needs test double
        if (typeRecord.IsAbstract && !typeRecord.IsInterface)
        {
            patterns.Add("ABSTRACT CLASS: Create a private TestDouble subclass inside the test file that implements abstract members with minimal logic.");
        }

        return patterns;
    }
}
