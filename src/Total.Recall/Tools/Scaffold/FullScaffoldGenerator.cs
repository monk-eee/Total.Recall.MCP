using System.Text;
using System.Text.Json;
using Total.Recall.Infrastructure;
using Total.Recall.Models;

namespace Total.Recall.Tools.Scaffold;

/// <summary>
/// Produces a complete C# test class skeleton (full file, including usings, namespace,
/// class declaration, constructor wiring, mock fields, and one [Fact] stub per uncovered
/// method) by combining TypeRecord + MockRecipes + CoverageGaps + Gotchas.
///
/// Pure store-driven generator: takes a class name + ns, returns the rendered scaffold
/// serialized as a JSON envelope. No telemetry or error handling here \u2014 the MCP entry
/// (<see cref="TestScaffoldTool"/>) is the only wrapper that adds those concerns.
/// </summary>
internal static class FullScaffoldGenerator
{
    public static string Generate(string className, bool generateEdgeCases, string? ns)
    {
        var stores = StoreRegistry.ForNamespace(ns);

        // Resolve the type using centralized 3-step lookup (exact -> CI -> contains)
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

        // Always-needed usings -- framework-aware
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
                    var defaultVal = TypeDefaults.DefaultLiteral(paramType);
                    concreteFields.Add((fieldName, paramType, defaultVal));
                }
            }
        }

        // Sort usings for readability (single sort after all usings are collected)
        var hasAsyncMethods = coverageGap?.UncoveredMethods.Any(m => MethodNaming.IsAsync(m.Name, typeRecord)) == true;
        if (hasAsyncMethods)
            usings.Add("using System.Threading.Tasks;");

        var sortedUsings = usings.OrderBy(u => u).ToList();

        // -- Write the file --

        // Class archetype guidance at the top
        var archetype = ArchetypeClassifier.ClassifyArchetype(typeRecord, mockFields.Count, concreteFields.Count, coverageGap);
        if (archetype is not null)
        {
            sb.AppendLine($"// Test Strategy: {archetype}");
            sb.AppendLine();
        }

        // Gotcha warnings at the top
        if (gotchas.Count > 0)
        {
            sb.AppendLine("// ============================================================");
            sb.AppendLine($"// \u26a0\ufe0f  KNOWN GOTCHAS for {typeRecord.Name} ({gotchas.Count} total)");
            foreach (var g in gotchas)
                sb.AppendLine($"//   [{g.Category}] {g.Description}");
            sb.AppendLine("// ============================================================");
            sb.AppendLine();
        }

        // Using statements
        foreach (var u in sortedUsings)
            sb.AppendLine(u);
        sb.AppendLine();

        // Namespace + class -- derived from config pattern
        var testNs = FrameworkTemplates.DeriveTestNamespace(typeRecord.Namespace, nsPattern);
        sb.AppendLine($"namespace {testNs};");
        sb.AppendLine();
        var classAttr = FrameworkTemplates.GetClassAttribute(framework);
        if (classAttr is not null)
            sb.AppendLine(classAttr);
        sb.AppendLine($"public class {typeRecord.Name}Tests");
        sb.AppendLine("{");

        // Mock fields -- framework-aware declarations
        foreach (var (fieldName, ifaceType, _, _) in mockFields)
            sb.AppendLine($"    {FrameworkTemplates.GetMockFieldDeclaration(mockLib, ifaceType, fieldName)}");

        // Concrete fields
        foreach (var (fieldName, paramType, _) in concreteFields)
            sb.AppendLine($"    private readonly {paramType} {fieldName};");

        // SUT field
        sb.AppendLine($"    private readonly {typeRecord.Name} _sut;");
        sb.AppendLine();

        // Constructor or Setup method -- framework-dependent
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

        // Initialize mocks -- framework-aware
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
            // Build disambiguated names for overloaded methods
            var disambiguatedNames = MethodNaming.BuildDisambiguatedNames(uncoveredMethods);

            sb.AppendLine();
            sb.AppendLine("    // ── Uncovered methods (from coverage data) ──");

            foreach (var method in uncoveredMethods)
            {
                var testName = disambiguatedNames.TryGetValue(method, out var dn) ? dn : MethodNaming.Sanitize(method.Name);
                var isAsync = MethodNaming.IsAsync(method.Name, typeRecord);
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
                var hint = AssertionRules.GetAssertionHint(method.Name);
                sb.AppendLine($"        // {hint} (lines {method.StartLine}-{method.EndLine}, {method.UncoveredLines} uncovered)");
                sb.AppendLine("    }");

                // Edge case stubs for methods with recognizable parameter patterns
                if (generateEdgeCases)
                    AssertionRules.AppendEdgeCaseStubs(sb, method.Name, testName, isAsync, typeRecord, testAttr);
            }
        }
        else
        {
            // No coverage data -- generate a basic ctor test
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
        var warnings = ArchetypeClassifier.GetAntiPatternWarnings(typeRecord, mockFields, concreteFields, mockLib);
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
            asyncMethodCount = uncoveredMethods.Count(m => MethodNaming.IsAsync(m.Name, typeRecord)),
            nullGuardTestCount = mockFields.Count,
            gotchaCount = gotchas.Count,
            antiPatternWarnings = warnings.Count,
            scaffold = sb.ToString()
        };

        return JsonSerializer.Serialize(result, SharedJsonOptions.CamelCaseIndented);
    }
}
