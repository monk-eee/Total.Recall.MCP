using System.ComponentModel;
using System.Text;
using System.Text.Json;
using ModelContextProtocol.Server;
using Total.Recall.Infrastructure;
using Total.Recall.Models;

namespace Total.Recall.Tools;

/// <summary>
/// Generates a complete C# test class skeleton by combining TypeRecord + MockRecipes +
/// CoverageGaps + Gotchas. Eliminates boilerplate and ensures correct using statements.
/// </summary>
[McpServerToolType]
public static class TestScaffoldTool
{
    /// <summary>
    /// Well-known async method names that don't follow the *Async suffix convention.
    /// Hoisted to static field to avoid per-call allocation.
    /// </summary>
    private static readonly HashSet<string> s_asyncMethodNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "ExecuteAsync", "InvokeAsync", "RunAsync", "StartAsync", "StopAsync",
        "ReadAsync", "WriteAsync", "SendAsync", "ReceiveAsync",
        "InitializeAsync", "DisposeAsync", "LoadAsync", "SaveAsync",
        "ConnectAsync", "DisconnectAsync", "ProcessAsync", "HandleAsync",
        "ValidateAsync", "ConfigureAsync"
    };
    [McpServerTool, Description(
        "Generate a complete C# test class skeleton for a type. " +
        "Combines type metadata (constructors, namespace), mock recipes (interface setup), " +
        "coverage gaps (uncovered methods), and gotchas (warnings) into a ready-to-fill test file. " +
        "Includes correct using statements, mock field declarations, constructor wiring, " +
        "and [Fact] stubs for each uncovered method with assertion hints and edge cases. " +
        "Use after get_testable_targets to quickly scaffold tests for selected classes. " +
        "Set methodNames to generate only method stubs (incremental mode for extending existing test files).")]
    public static string GenerateTestScaffold(
        [Description("Class name to generate test scaffold for")] string className,
        [Description("Optional comma-separated method names to generate stubs for (incremental mode). " +
            "When set, generates only [Fact] stubs for the specified methods — no class skeleton, " +
            "no constructor, no fields. Use when extending an existing test file.")] string? methodNames = null,
        [Description("Generate edge case test stubs (null input, empty collection, boundaries) for each method (default: true)")] bool generateEdgeCases = true,
        [Description("Optional: namespace/session to query (default: server default)")] string? ns = null)
    {
        Metrics.Increment(Metrics.ToolGenerateTestScaffold);
        Log.Debug($"[GenerateTestScaffold] className='{className}' methodNames='{methodNames ?? "(all)"}' generateEdgeCases={generateEdgeCases} ns='{ns ?? "(default)"}'");
        try
        {
            // Incremental mode: generate only method stubs for specified methods
            if (!string.IsNullOrWhiteSpace(methodNames))
                return GenerateIncrementalStubs(className, methodNames, ns);

            return GenerateTestScaffoldCore(className, generateEdgeCases, ns);
        }
        catch (Exception ex)
        {
            Log.Error($"[GenerateTestScaffold] failed for '{className}': {ex.GetType().Name}: {ex.Message}");
            return $"ERROR in GenerateTestScaffold: {ex.GetType().Name}: {ex.Message}";
        }
    }

    private static string GenerateTestScaffoldCore(string className, bool generateEdgeCases, string? ns)
    {
        var stores = StoreRegistry.ForNamespace(ns);

        // Resolve the type using centralized 3-step lookup (exact → CI → contains)
        var typeRecord = stores.ResolveType(className);

        if (typeRecord is null)
            return $"Type '{className}' not found in type registry. Cannot generate scaffold.";

        // Read framework configuration from namespace config
        var config = stores.Config;
        var framework = config.TestFramework;
        var mockLib = config.MockLibrary;
        var nsPattern = config.TestNamespacePattern;

        // Gather supporting data
        var gotchas = stores.Gotchas.HasData()
            ? stores.Gotchas.Query(g => g.Type.Equals(typeRecord.Name, StringComparison.OrdinalIgnoreCase))
            : [];

        var coverageGap = stores.CoverageGaps.HasData()
            ? stores.CoverageGaps.LoadAll().FirstOrDefault(g =>
                g.Class.Equals(typeRecord.Name, StringComparison.OrdinalIgnoreCase))
            : null;

        var mockRecipes = stores.MockRecipes.HasData()
            ? stores.MockRecipes.LoadAll().ToDictionary(m => m.Interface, m => m, StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, MockRecipe>(StringComparer.OrdinalIgnoreCase);

        // Build the scaffold
        var sb = new StringBuilder();
        var usings = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var mockFields = new List<(string FieldName, string InterfaceType, string ShortName, MockRecipe? Recipe)>();
        var concreteFields = new List<(string FieldName, string ParamType, string DefaultValue)>();

        // Always-needed usings — framework-aware
        usings.Add(FrameworkTemplates.GetTestUsing(framework));
        usings.Add(FrameworkTemplates.GetMockUsing(mockLib));

        // Type's own namespace
        if (!string.IsNullOrEmpty(typeRecord.Namespace))
            usings.Add($"using {typeRecord.Namespace};");

        // Analyze constructor parameters
        var mainCtor = typeRecord.Constructors
            .OrderByDescending(c => c.Params.Count)
            .FirstOrDefault();

        if (mainCtor is not null)
        {
            foreach (var param in mainCtor.Params)
            {
                var (paramType, paramName) = ParamHelper.ParseParam(param);

                if (ParamHelper.IsInterfaceLike(paramType))
                {
                    var fieldName = $"_mock{ParamHelper.StripIPrefix(paramType)}";
                    mockFields.Add((fieldName, paramType, paramName, null));

                    // Check for mock recipe
                    if (mockRecipes.TryGetValue(paramType, out var recipe))
                    {
                        mockFields[^1] = (fieldName, paramType, paramName, recipe);
                        foreach (var u in recipe.RequiredUsings)
                            usings.Add(u);
                        if (!string.IsNullOrEmpty(recipe.Namespace))
                            usings.Add($"using {recipe.Namespace};");
                    }
                    else
                    {
                        // Try to find the interface in type registry for its namespace
                        var ifaceType = stores.ResolveType(paramType);
                        if (ifaceType is not null && !string.IsNullOrEmpty(ifaceType.Namespace))
                            usings.Add($"using {ifaceType.Namespace};");
                    }
                }
                else
                {
                    var fieldName = $"_{char.ToLower(paramName[0])}{paramName[1..]}";
                    var defaultVal = GetDefaultValue(paramType);
                    concreteFields.Add((fieldName, paramType, defaultVal));
                }
            }
        }

        // Sort usings for readability (single sort after all usings are collected)
        var hasAsyncMethods = coverageGap?.UncoveredMethods.Any(m => IsAsyncMethod(m.Name, typeRecord)) == true;
        if (hasAsyncMethods)
            usings.Add("using System.Threading.Tasks;");

        var sortedUsings = usings.OrderBy(u => u).ToList();

        // ── Write the file ──

        // Class archetype guidance at the top
        var archetype = ClassifyArchetype(typeRecord, mockFields.Count, concreteFields.Count, coverageGap);
        if (archetype is not null)
        {
            sb.AppendLine($"// Test Strategy: {archetype}");
            sb.AppendLine();
        }

        // Gotcha warnings at the top
        if (gotchas.Count > 0)
        {
            sb.AppendLine("// ============================================================");
            sb.AppendLine($"// ⚠️  KNOWN GOTCHAS for {typeRecord.Name} ({gotchas.Count} total)");
            foreach (var g in gotchas)
                sb.AppendLine($"//   [{g.Category}] {g.Description}");
            sb.AppendLine("// ============================================================");
            sb.AppendLine();
        }

        // Using statements
        foreach (var u in sortedUsings)
            sb.AppendLine(u);
        sb.AppendLine();

        // Namespace + class — derived from config pattern
        var testNs = FrameworkTemplates.DeriveTestNamespace(typeRecord.Namespace, nsPattern);
        sb.AppendLine($"namespace {testNs};");
        sb.AppendLine();
        var classAttr = FrameworkTemplates.GetClassAttribute(framework);
        if (classAttr is not null)
            sb.AppendLine(classAttr);
        sb.AppendLine($"public class {typeRecord.Name}Tests");
        sb.AppendLine("{");

        // Mock fields — framework-aware declarations
        foreach (var (fieldName, ifaceType, _, _) in mockFields)
            sb.AppendLine($"    {FrameworkTemplates.GetMockFieldDeclaration(mockLib, ifaceType, fieldName)}");

        // Concrete fields
        foreach (var (fieldName, paramType, _) in concreteFields)
            sb.AppendLine($"    private readonly {paramType} {fieldName};");

        // SUT field
        sb.AppendLine($"    private readonly {typeRecord.Name} _sut;");
        sb.AppendLine();

        // Constructor or Setup method — framework-dependent
        var setupAttr = FrameworkTemplates.GetSetupAttribute(framework);
        if (setupAttr is not null)
        {
            // NUnit [SetUp] or MSTest [TestInitialize]
            sb.AppendLine($"    {setupAttr}");
            sb.AppendLine($"    public void Setup()");
        }
        else
        {
            // xUnit uses constructor
            sb.AppendLine($"    public {typeRecord.Name}Tests()");
        }
        sb.AppendLine("    {");

        // Initialize mocks — framework-aware
        foreach (var (fieldName, ifaceType, _, recipe) in mockFields)
        {
            sb.AppendLine($"        {FrameworkTemplates.GetMockInitialization(mockLib, ifaceType, fieldName)}");
            if (recipe is not null)
            {
                sb.AppendLine($"        // Mock recipe for {ifaceType}:");
                foreach (var line in recipe.Recipe.Split('\n'))
                {
                    var trimmed = line.Trim();
                    if (!string.IsNullOrEmpty(trimmed) && !trimmed.StartsWith("var mock", StringComparison.OrdinalIgnoreCase))
                        sb.AppendLine($"        // {trimmed}");
                }
            }
        }

        // Initialize concrete values
        foreach (var (fieldName, _, defaultVal) in concreteFields)
            sb.AppendLine($"        {fieldName} = {defaultVal};");

        sb.AppendLine();

        // Create SUT
        if (mainCtor is not null && mainCtor.Params.Count > 0)
        {
            var ctorArgs = new List<string>();
            foreach (var param in mainCtor.Params)
            {
                var (paramType, paramName) = ParamHelper.ParseParam(param);
                if (ParamHelper.IsInterfaceLike(paramType))
                {
                    var fieldName = $"_mock{ParamHelper.StripIPrefix(paramType)}";
                    ctorArgs.Add(FrameworkTemplates.GetMockObjectExpression(mockLib, fieldName));
                }
                else
                {
                    var fieldName = $"_{char.ToLower(paramName[0])}{paramName[1..]}";
                    ctorArgs.Add(fieldName);
                }
            }

            if (ctorArgs.Count <= 3)
            {
                sb.AppendLine($"        _sut = new {typeRecord.Name}({string.Join(", ", ctorArgs)});");
            }
            else
            {
                sb.AppendLine($"        _sut = new {typeRecord.Name}(");
                for (int i = 0; i < ctorArgs.Count; i++)
                {
                    var comma = i < ctorArgs.Count - 1 ? "," : ");";
                    sb.AppendLine($"            {ctorArgs[i]}{comma}");
                }
            }
        }
        else
        {
            sb.AppendLine($"        _sut = new {typeRecord.Name}();");
        }

        sb.AppendLine("    }");

        // Test method stubs
        var testAttr = FrameworkTemplates.GetTestAttribute(framework);
        var uncoveredMethods = coverageGap?.UncoveredMethods ?? [];
        if (uncoveredMethods.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("    // ── Uncovered methods (from coverage data) ──");

            foreach (var method in uncoveredMethods)
            {
                var testName = SanitizeMethodName(method.Name);
                var isAsync = IsAsyncMethod(method.Name, typeRecord);
                sb.AppendLine();
                sb.AppendLine($"    {testAttr}");
                if (isAsync)
                {
                    sb.AppendLine($"    public async Task {testName}_ShouldWork()");
                    sb.AppendLine("    {");
                    sb.AppendLine("        // Arrange");
                    sb.AppendLine();
                    sb.AppendLine("        // Act");
                    sb.AppendLine($"        // TODO: await _sut.{method.Name}(...)");
                }
                else
                {
                    sb.AppendLine($"    public void {testName}_ShouldWork()");
                    sb.AppendLine("    {");
                    sb.AppendLine("        // Arrange");
                    sb.AppendLine();
                    sb.AppendLine("        // Act");
                    sb.AppendLine($"        // TODO: call _sut.{method.Name}(...)");
                }
                sb.AppendLine();
                sb.AppendLine("        // Assert");
                // Smart assertion hints based on method name patterns
                var hint = GetAssertionHint(method.Name);
                sb.AppendLine($"        // {hint} (lines {method.StartLine}-{method.EndLine}, {method.UncoveredLines} uncovered)");
                sb.AppendLine("    }");

                // Edge case stubs for methods with recognizable parameter patterns
                if (generateEdgeCases)
                    AppendEdgeCaseStubs(sb, method.Name, testName, isAsync, typeRecord, testAttr);
            }
        }
        else
        {
            // No coverage data — generate a basic ctor test
            sb.AppendLine();
            sb.AppendLine($"    {testAttr}");
            sb.AppendLine($"    public void Ctor_ShouldCreateInstance()");
            sb.AppendLine("    {");
            sb.AppendLine("        // Assert");
            sb.AppendLine($"        {FrameworkTemplates.GetAssertNotNull(framework, "_sut")}");
            sb.AppendLine("    }");
        }

        // Null-guard constructor parameter tests
        if (mockFields.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("    // ── Constructor null-guard tests ──");

            foreach (var (fieldName, ifaceType, paramName, _) in mockFields)
            {
                sb.AppendLine();
                sb.AppendLine($"    {testAttr}");
                sb.AppendLine($"    public void Ctor_Null{ParamHelper.StripIPrefix(ifaceType)}_ThrowsArgumentNullException()");
                sb.AppendLine("    {");

                // Build ctor args with the current one set to null
                var nullCtorArgs = new List<string>();
                foreach (var param in mainCtor!.Params)
                {
                    var (pType, pName) = ParamHelper.ParseParam(param);
                    if (pName == paramName && pType == ifaceType)
                    {
                        nullCtorArgs.Add("null!");
                    }
                    else if (ParamHelper.IsInterfaceLike(pType))
                    {
                        var fn = $"_mock{ParamHelper.StripIPrefix(pType)}";
                        nullCtorArgs.Add(FrameworkTemplates.GetMockObjectExpression(mockLib, fn));
                    }
                    else
                    {
                        var fn = $"_{char.ToLower(pName[0])}{pName[1..]}";
                        nullCtorArgs.Add(fn);
                    }
                }

                var ctorCall = $"new {typeRecord.Name}({string.Join(", ", nullCtorArgs)})";
                sb.AppendLine($"        {FrameworkTemplates.GetAssertThrows(framework, "ArgumentNullException", ctorCall)}");
                sb.AppendLine("    }");
            }
        }

        // Anti-pattern warnings based on type characteristics
        var warnings = GetAntiPatternWarnings(typeRecord, mockFields, concreteFields, mockLib);
        if (warnings.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("    // ── ⚠️  ANTI-PATTERN WARNINGS ──");
            foreach (var warning in warnings)
                sb.AppendLine($"    // {warning}");
        }

        sb.AppendLine("}");

        // Return the scaffold as JSON with metadata
        var result = new
        {
            className = typeRecord.Name,
            @namespace = testNs,
            suggestedFileName = $"{typeRecord.Name}Tests.cs",
            mockCount = mockFields.Count,
            uncoveredMethodCount = uncoveredMethods.Count,
            asyncMethodCount = uncoveredMethods.Count(m => IsAsyncMethod(m.Name, typeRecord)),
            nullGuardTestCount = mockFields.Count,
            gotchaCount = gotchas.Count,
            antiPatternWarnings = warnings.Count,
            scaffold = sb.ToString()
        };

        return JsonSerializer.Serialize(result, SharedJsonOptions.CamelCaseIndented);
    }

    /// <summary>
    /// Incremental scaffold mode: generates only [Fact] method stubs for specific methods.
    /// No class skeleton, no constructor, no mock fields — just the test methods to paste
    /// into an existing test file. Used when extending test files for partially-covered classes.
    /// </summary>
    internal static string GenerateIncrementalStubs(string className, string methodNames, string? ns)
    {
        var stores = StoreRegistry.ForNamespace(ns);

        var typeRecord = stores.ResolveType(className);
        if (typeRecord is null)
            return $"Type '{className}' not found in type registry. Cannot generate stubs.";

        // Read framework configuration
        var config = stores.Config;
        var framework = config.TestFramework;
        var testAttr = FrameworkTemplates.GetTestAttribute(framework);

        var coverageGap = stores.CoverageGaps.HasData()
            ? stores.CoverageGaps.LoadAll().FirstOrDefault(g =>
                g.Class.Equals(typeRecord.Name, StringComparison.OrdinalIgnoreCase)
                || g.Class.Equals(className, StringComparison.OrdinalIgnoreCase))
            : null;

        var gotchas = stores.Gotchas.HasData()
            ? stores.Gotchas.Query(g => g.Type.Equals(typeRecord.Name, StringComparison.OrdinalIgnoreCase))
            : [];

        // Parse requested method names
        var requestedMethods = methodNames.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Match requested methods against coverage data
        var uncoveredMethods = coverageGap?.UncoveredMethods
            .Where(m => requestedMethods.Contains(m.Name))
            .ToList() ?? [];

        // For methods not in coverage data, create synthetic entries
        var coveredMethodNames = uncoveredMethods.Select(m => m.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var syntheticMethods = requestedMethods
            .Where(m => !coveredMethodNames.Contains(m))
            .Select(m => new UncoveredMethod { Name = m })
            .ToList();

        var allMethods = uncoveredMethods.Concat(syntheticMethods).ToList();

        if (allMethods.Count == 0)
            return $"No matching methods found for '{className}'. Requested: {methodNames}";

        var sb = new StringBuilder();

        // Gotcha warnings as comments
        if (gotchas.Count > 0)
        {
            sb.AppendLine($"    // ⚠️  GOTCHAS for {typeRecord.Name}:");
            foreach (var g in gotchas)
                sb.AppendLine($"    //   [{g.Category}] {g.Description}");
            sb.AppendLine();
        }

        sb.AppendLine($"    // ── Incremental stubs for {typeRecord.Name} ({allMethods.Count} methods) ──");

        foreach (var method in allMethods)
        {
            var testName = SanitizeMethodName(method.Name);
            var isAsync = IsAsyncMethod(method.Name, typeRecord);

            sb.AppendLine();
            sb.AppendLine($"    {testAttr}");
            if (isAsync)
            {
                sb.AppendLine($"    public async Task {testName}_ShouldWork()");
                sb.AppendLine("    {");
                sb.AppendLine("        // Arrange");
                sb.AppendLine();
                sb.AppendLine("        // Act");
                sb.AppendLine($"        // TODO: await _sut.{method.Name}(...)");
            }
            else
            {
                sb.AppendLine($"    public void {testName}_ShouldWork()");
                sb.AppendLine("    {");
                sb.AppendLine("        // Arrange");
                sb.AppendLine();
                sb.AppendLine("        // Act");
                sb.AppendLine($"        // TODO: call _sut.{method.Name}(...)");
            }
            sb.AppendLine();
            sb.AppendLine("        // Assert");
            var hint = GetAssertionHint(method.Name);
            if (method.UncoveredLines > 0)
                sb.AppendLine($"        // {hint} (lines {method.StartLine}-{method.EndLine}, {method.UncoveredLines} uncovered)");
            else
                sb.AppendLine($"        // {hint}");
            sb.AppendLine("    }");
        }

        var resultObj = new
        {
            mode = "incremental",
            className = typeRecord.Name,
            methodCount = allMethods.Count,
            fromCoverageData = uncoveredMethods.Count,
            synthetic = syntheticMethods.Count,
            gotchaCount = gotchas.Count,
            stubs = sb.ToString()
        };

        return JsonSerializer.Serialize(resultObj, SharedJsonOptions.CamelCaseIndented);
    }

    /// <summary>
    /// Return sensible C# default value literals for common types.
    /// Used to initialize concrete (non-interface) constructor parameters in scaffolded tests.
    /// </summary>
    internal static string GetDefaultValue(string typeName)
    {
        // Strip nullable suffix for matching
        var baseType = typeName.TrimEnd('?');
        var isNullable = typeName.EndsWith('?');

        // For nullable types, null is the simplest valid value
        if (isNullable && baseType != "string")
            return "null";

        return baseType switch
        {
            "string" => "\"test-value\"",
            "int" => "0",
            "long" => "0L",
            "bool" => "false",
            "double" => "0.0",
            "float" => "0f",
            "decimal" => "0m",
            "byte" => "(byte)0",
            "short" => "(short)0",
            "ushort" => "(ushort)0",
            "uint" => "0u",
            "ulong" => "0UL",
            "char" => "'a'",
            "Guid" => "Guid.NewGuid()",
            "DateTime" => "DateTime.UtcNow",
            "DateTimeOffset" => "DateTimeOffset.UtcNow",
            "TimeSpan" => "TimeSpan.FromSeconds(1)",
            "Uri" => "new Uri(\"https://example.com\")",
            "CancellationToken" => "CancellationToken.None",
            "Stream" => "Stream.Null",
            "Type" => "typeof(object)",
            "object" => "new object()",
            _ when baseType.StartsWith("List<") => $"new {baseType}()",
            _ when baseType.StartsWith("IList<") => $"new {baseType.Replace("IList<", "List<")}()",
            _ when baseType.StartsWith("IEnumerable<") => $"Array.Empty<{ExtractGenericArg(baseType, "IEnumerable<")}>()",
            _ when baseType.StartsWith("IReadOnlyList<") => $"Array.Empty<{ExtractGenericArg(baseType, "IReadOnlyList<")}>()",
            _ when baseType.StartsWith("ICollection<") => $"new List<{ExtractGenericArg(baseType, "ICollection<")}>()",
            _ when baseType.StartsWith("Dictionary<") => $"new {baseType}()",
            _ when baseType.StartsWith("IDictionary<") => $"new {baseType.Replace("IDictionary<", "Dictionary<")}()",
            _ when baseType.StartsWith("Func<") => "null!",
            _ when baseType.StartsWith("Action<") || baseType == "Action" => "() => {{ }}",
            _ when baseType.EndsWith("[]") => $"Array.Empty<{baseType[..^2]}>()",
            _ when baseType.StartsWith("Nullable<") => "null",
            _ when baseType.EndsWith("Enum") || baseType.EndsWith("Type") => $"default({baseType})",
            _ => $"default({baseType})!" // nullable reference fallback
        };
    }

    /// <summary>
    /// Extract the generic type argument from a generic type name, e.g. "IEnumerable&lt;string&gt;" → "string".
    /// </summary>
    private static string ExtractGenericArg(string typeName, string prefix)
    {
        if (typeName.StartsWith(prefix) && typeName.EndsWith(">"))
            return typeName[prefix.Length..^1];
        return "object";
    }

    /// <summary>
    /// Sanitize a method name for use as a test method name.
    /// Handles property accessors (get_/set_), strips Async suffix,
    /// and removes non-alphanumeric characters.
    /// </summary>
    private static string SanitizeMethodName(string methodName)
    {
        // Handle property accessors
        if (methodName.StartsWith("get_"))
            return $"Get{methodName[4..]}";
        if (methodName.StartsWith("set_"))
            return $"Set{methodName[4..]}";

        // Strip "Async" suffix for the test name (will be re-marked with async keyword)
        var name = methodName;
        if (name.EndsWith("Async") && name.Length > 5)
            name = name[..^5];

        // Remove non-alphanumeric characters
        var sb = new StringBuilder();
        foreach (var ch in name)
        {
            if (char.IsLetterOrDigit(ch) || ch == '_')
                sb.Append(ch);
        }

        var result = sb.ToString();
        return string.IsNullOrEmpty(result) ? "Method" : result;
    }

    /// <summary>
    /// Heuristic to detect if a method is async based on naming conventions and type info.
    /// Methods named *Async or returning Task/Task&lt;T&gt; are treated as async.
    /// </summary>
    internal static bool IsAsyncMethod(string methodName, TypeRecord? typeRecord)
    {
        // Common naming convention: methods ending in "Async"
        if (methodName.EndsWith("Async", StringComparison.Ordinal))
            return true;

        // Well-known async method names (uses pre-built static HashSet — O(1))
        if (s_asyncMethodNames.Contains(methodName))
            return true;

        // Strip property accessor prefixes and check remainder
        var baseName = methodName;
        if (baseName.StartsWith("get_") || baseName.StartsWith("set_"))
            baseName = baseName[4..];

        // If the base type name suggests async patterns (e.g., IAsyncEnumerable)
        if (typeRecord?.Interfaces?.Any(i =>
            i.Contains("IAsync", StringComparison.OrdinalIgnoreCase)) == true)
        {
            // Methods like MoveNextAsync, GetAsyncEnumerator
            if (baseName.Contains("Async", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Returns a method-specific assertion hint based on method name patterns.
    /// Replaces generic "TODO: verify behavior" with actionable guidance.
    /// </summary>
    internal static string GetAssertionHint(string methodName)
    {
        var name = methodName;
        if (name.StartsWith("get_"))
            name = name[4..];
        if (name.StartsWith("set_"))
            return "Assert the property value was stored correctly";

        // Strip Async suffix for pattern matching
        if (name.EndsWith("Async"))
            name = name[..^5];

        if (name.StartsWith("Validate", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("Check", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("Verify", StringComparison.OrdinalIgnoreCase))
            return "Assert.True/False on validation result; also test with invalid input → expect false or exception";

        if (name.StartsWith("Get", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("Find", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("Fetch", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("Load", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("Read", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("Resolve", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("Lookup", StringComparison.OrdinalIgnoreCase))
            return "Assert.NotNull on result; verify expected return value with Assert.Equal";

        if (name.StartsWith("Is", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("Has", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("Can", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("Should", StringComparison.OrdinalIgnoreCase))
            return "Assert.True for positive case; write separate test with Assert.False for negative case";

        if (name.StartsWith("Count", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("Size", StringComparison.OrdinalIgnoreCase))
            return "Assert.Equal on expected count; also test empty input → 0";

        if (name.StartsWith("Parse", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("Convert", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("Transform", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("Map", StringComparison.OrdinalIgnoreCase))
            return "Assert.Equal on expected output; also Assert.Throws for malformed input";

        if (name.StartsWith("Create", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("Build", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("Make", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("New", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("Generate", StringComparison.OrdinalIgnoreCase))
            return "Assert.NotNull on created object; verify key properties are correctly set";

        if (name.StartsWith("Delete", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("Remove", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("Clear", StringComparison.OrdinalIgnoreCase))
            return "Verify item is removed; mock.Verify the dependency call was made";

        if (name.StartsWith("Add", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("Insert", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("Register", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("Set", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("Update", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("Save", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("Write", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("Store", StringComparison.OrdinalIgnoreCase))
            return "Verify state change occurred; mock.Verify the dependency was called with correct args";

        if (name.StartsWith("Init", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("Setup", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("Configure", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("Start", StringComparison.OrdinalIgnoreCase))
            return "Verify initialization side-effects; check state is ready after call";

        if (name.StartsWith("Dispose", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("Close", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("Shutdown", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("Stop", StringComparison.OrdinalIgnoreCase))
            return "Verify resources released; calling methods after dispose should throw ObjectDisposedException";

        if (name.StartsWith("Handle", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("Process", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("Execute", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("Run", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("Invoke", StringComparison.OrdinalIgnoreCase))
            return "Verify side-effects via mock.Verify; check return value if non-void";

        if (name.StartsWith("Try", StringComparison.OrdinalIgnoreCase))
            return "Assert.True for success case returning true; separate test with Assert.False for failure case";

        if (name.StartsWith("Throw", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("Ensure", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("Require", StringComparison.OrdinalIgnoreCase))
            return "Assert.Throws for invalid input; verify no exception for valid input";

        if (name.StartsWith("On", StringComparison.OrdinalIgnoreCase))
            return "Verify event handler side-effects via mock.Verify; test with varying event args";

        if (name.StartsWith("Format", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("Render", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("ToString", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("Serialize", StringComparison.OrdinalIgnoreCase))
            return "Assert.Equal on expected string output; Assert.Contains for key substrings";

        return "TODO: verify behavior — Assert.NotNull for queries, mock.Verify for commands";
    }

    /// <summary>
    /// Appends edge case test stubs for methods with recognizable name or parameter patterns.
    /// For example, methods taking implicit string params get null/empty edge case tests.
    /// </summary>
    internal static void AppendEdgeCaseStubs(StringBuilder sb, string methodName, string testName, bool isAsync, TypeRecord? typeRecord, string testAttr = "[Fact]")
    {
        var edgeCases = new List<(string Suffix, string Comment)>();

        // Detect parameter-type-hinting from method name
        var lowerName = methodName.ToLowerInvariant();

        // Methods with string-like parameters (name, path, text, input, value, key, etc.)
        if (lowerName.Contains("name") || lowerName.Contains("path") || lowerName.Contains("text")
            || lowerName.Contains("input") || lowerName.Contains("parse") || lowerName.Contains("format")
            || lowerName.Contains("string") || lowerName.Contains("file") || lowerName.Contains("url")
            || lowerName.Contains("key") || lowerName.Contains("content") || lowerName.Contains("message"))
        {
            edgeCases.Add(("_NullInput_ShouldThrowOrHandle", "// Edge case: pass null string argument — expect ArgumentNullException or graceful handling"));
            edgeCases.Add(("_EmptyInput_ShouldHandle", "// Edge case: pass empty string — verify behavior with string.Empty"));
        }

        // Methods suggesting collection input
        if (lowerName.Contains("items") || lowerName.Contains("list") || lowerName.Contains("collection")
            || lowerName.Contains("batch") || lowerName.Contains("all") || lowerName.Contains("many")
            || lowerName.Contains("multiple") || lowerName.Contains("each") || lowerName.Contains("entries"))
        {
            edgeCases.Add(("_EmptyCollection_ShouldHandle", "// Edge case: pass empty collection — verify behavior with no items"));
        }

        // Methods suggesting numeric input
        if (lowerName.Contains("count") || lowerName.Contains("index") || lowerName.Contains("size")
            || lowerName.Contains("limit") || lowerName.Contains("offset") || lowerName.Contains("max")
            || lowerName.Contains("min") || lowerName.Contains("page") || lowerName.Contains("number"))
        {
            edgeCases.Add(("_ZeroValue_ShouldHandle", "// Edge case: pass zero — verify boundary behavior"));
            edgeCases.Add(("_NegativeValue_ShouldThrowOrHandle", "// Edge case: pass negative number — expect ArgumentOutOfRangeException or graceful handling"));
        }

        foreach (var (suffix, comment) in edgeCases)
        {
            sb.AppendLine();
            sb.AppendLine($"    {testAttr}");
            if (isAsync)
            {
                sb.AppendLine($"    public async Task {testName}{suffix}()");
                sb.AppendLine("    {");
                sb.AppendLine($"        {comment}");
                sb.AppendLine($"        // TODO: await _sut.{methodName}(edgeCaseValue)");
                sb.AppendLine("    }");
            }
            else
            {
                sb.AppendLine($"    public void {testName}{suffix}()");
                sb.AppendLine("    {");
                sb.AppendLine($"        {comment}");
                sb.AppendLine($"        // TODO: _sut.{methodName}(edgeCaseValue)");
                sb.AppendLine("    }");
            }
        }
    }

    /// <summary>
    /// Returns anti-pattern warning comments based on type characteristics to help
    /// the AI agent avoid common test authoring mistakes.
    /// </summary>
    internal static List<string> GetAntiPatternWarnings(
        TypeRecord typeRecord,
        List<(string FieldName, string InterfaceType, string ShortName, MockRecipe? Recipe)> mockFields,
        List<(string FieldName, string ParamType, string DefaultValue)> concreteFields,
        MockLibrary mockLib = MockLibrary.Moq)
    {
        var warnings = new List<string>();

        if (typeRecord.IsStatic)
            warnings.Add("STATIC CLASS: Static state may leak between tests. Use [Collection] to prevent parallel execution, or reset state in Dispose().");

        if (typeRecord.Interfaces.Any(i => i.Contains("IDisposable", StringComparison.OrdinalIgnoreCase)))
            warnings.Add("IMPLEMENTS IDisposable: Ensure _sut is disposed in test cleanup. Implement IDisposable on this test class and call _sut.Dispose() in Dispose().");

        // Concrete class dependencies (not primitive/string but not interface either)
        var concreteDeps = concreteFields.Where(f =>
            !IsPrimitiveOrCommon(f.ParamType)).ToList();
        if (concreteDeps.Count > 0)
        {
            var depNames = string.Join(", ", concreteDeps.Select(d => d.ParamType));
            warnings.Add($"CONCRETE DEPENDENCIES ({depNames}): Avoid mocking concrete classes. Consider extracting interfaces or using real instances with known state.");
        }

        if (mockFields.Count >= 5)
            warnings.Add($"HIGH MOCK COUNT ({mockFields.Count}): Many mocks may indicate tight coupling. Tests may become brittle — focus {FrameworkTemplates.GetVerifyHint(mockLib)} on essential interactions only.");

        if (typeRecord.IsAbstract)
            warnings.Add("ABSTRACT CLASS: Cannot instantiate directly. Create a minimal test subclass that implements abstract members.");

        if (typeRecord.IsInternal)
            warnings.Add("INTERNAL CLASS: Ensure test project has [InternalsVisibleTo] or use a public API surface for testing.");

        // Check for event-like properties that suggest event subscription patterns
        if (typeRecord.Properties.Any(p =>
            p.ClrType.Contains("EventHandler")
            || p.ClrType == "Action"
            || p.ClrType.StartsWith("Action<")
            || p.Name.StartsWith("On", StringComparison.OrdinalIgnoreCase)))
            warnings.Add("HAS EVENTS: Subscribe to events before acting, unsubscribe in cleanup to prevent memory leaks in tests.");

        return warnings;
    }

    /// <summary>
    /// Returns true for primitive CLR types and common framework types that don't need mocking.
    /// Used by <see cref="GetAntiPatternWarnings"/> to identify concrete dependencies
    /// that may indicate design issues (non-primitive, non-interface ctor params).
    /// </summary>
    private static bool IsPrimitiveOrCommon(string typeName)
    {
        var baseType = typeName.TrimEnd('?');
        return baseType switch
        {
            "string" or "int" or "long" or "bool" or "double" or "float" or "decimal"
            or "byte" or "short" or "char" or "uint" or "ulong" or "ushort"
            or "Guid" or "DateTime" or "DateTimeOffset" or "TimeSpan" or "Uri"
            or "CancellationToken" or "Stream" or "Type" or "object" => true,
            _ when baseType.StartsWith("List<") || baseType.StartsWith("Dictionary<")
                || baseType.StartsWith("IList<") || baseType.StartsWith("IEnumerable<")
                || baseType.StartsWith("IReadOnlyList<") || baseType.StartsWith("ICollection<")
                || baseType.StartsWith("IDictionary<") || baseType.StartsWith("Func<")
                || baseType.StartsWith("Action") || baseType.EndsWith("[]") => true,
            _ => false
        };
    }

    /// <summary>
    /// Classify the class archetype and return a testing strategy comment.
    /// Helps agents understand the best approach for testing this class shape.
    /// </summary>
    internal static string? ClassifyArchetype(
        TypeRecord typeRecord, int mockFieldCount, int concreteFieldCount,
        CoverageGap? coverageGap)
    {
        // Static helper class — pure functions, no mocking
        if (typeRecord.IsStatic)
            return "STATIC HELPER — Pure function tests. No mocking needed. " +
                   "Test each method with representative inputs, edge cases (null, empty, boundary values), " +
                   "and verify return values with Assert.Equal/Assert.Contains.";

        // POCO / data class — no ctor params, many properties
        if (mockFieldCount == 0 && concreteFieldCount == 0 && typeRecord.Properties.Count > 3)
            return "POCO/DATA CLASS — Test property round-trips, constructor defaults, " +
                   "and any computed properties. Verify equality/GetHashCode if it's a record.";

        // Service with all-interface DI
        if (mockFieldCount > 0 && concreteFieldCount == 0)
        {
            if (mockFieldCount >= 5)
                return $"HEAVY-DI SERVICE ({mockFieldCount} dependencies) — Focus on the most important " +
                       "interactions. Use mock.Verify sparingly (only essential side-effects). " +
                       "Consider testing method groups that share common mock setups together.";

            return $"STANDARD SERVICE ({mockFieldCount} dependencies) — Classic mock + verify pattern. " +
                   "Arrange mock returns, Act on SUT, Assert return values and Verify mock interactions.";
        }

        // Mixed dependencies (interface + concrete)
        if (mockFieldCount > 0 && concreteFieldCount > 0)
            return $"MIXED-DI SERVICE ({mockFieldCount} mocked + {concreteFieldCount} concrete) — " +
                   "Concrete dependencies can't be mocked; use known-good values. " +
                   "Focus tests on behavior that doesn't depend on concrete dep internals.";

        // Builder/factory pattern — creates instances
        if (typeRecord.Name.Contains("Builder") || typeRecord.Name.Contains("Factory")
            || typeRecord.Name.Contains("Provider") || typeRecord.Name.Contains("Creator"))
            return "BUILDER/FACTORY — Test the build/create output, not the process. " +
                   "Verify key properties of created objects. Test with various configurations.";

        return null;
    }
}
