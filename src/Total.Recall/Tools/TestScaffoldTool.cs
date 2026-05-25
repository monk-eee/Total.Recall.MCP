using System.ComponentModel;
using ModelContextProtocol.Server;
using Total.Recall.Infrastructure;
using Total.Recall.Tools.Scaffold;

namespace Total.Recall.Tools;

/// <summary>
/// MCP entry point for the <c>generate_test_scaffold</c> tool. Routes between two
/// generators based on whether <c>methodNames</c> is supplied:
/// <list type="bullet">
/// <item><see cref="FullScaffoldGenerator"/> \u2014 complete test class skeleton.</item>
/// <item><see cref="IncrementalScaffoldGenerator"/> \u2014 [Fact] stubs only, for extending
/// an existing test file.</item>
/// </list>
/// This type intentionally owns no generation logic of its own \u2014 it is purely the
/// MCP-protocol surface (Telemetry, Metrics, Log, try/catch envelope).
/// </summary>
[McpServerToolType]
public static class TestScaffoldTool
{
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
            "When set, generates only [Fact] stubs for the specified methods \u2014 no class skeleton, " +
            "no constructor, no fields. Use when extending an existing test file.")] string? methodNames = null,
        [Description("Generate edge case test stubs (null input, empty collection, boundaries) for each method (default: true)")] bool generateEdgeCases = true,
        [Description("Optional: namespace/session to query (default: server default)")] string? ns = null)
    {
        return Telemetry.Track("generate_test_scaffold", ns, new { className, methodNames, generateEdgeCases, ns }, () =>
        {
            Metrics.Increment(Metrics.ToolGenerateTestScaffold);
            Log.Debug($"[GenerateTestScaffold] className='{className}' methodNames='{methodNames ?? "(all)"}' generateEdgeCases={generateEdgeCases} ns='{ns ?? "(default)"}'");
            try
            {
                // Incremental mode: generate only method stubs for specified methods
                if (!string.IsNullOrWhiteSpace(methodNames))
                    return IncrementalScaffoldGenerator.Generate(className, methodNames, ns);

                return FullScaffoldGenerator.Generate(className, generateEdgeCases, ns);
            }
            catch (Exception ex)
            {
                Log.Error($"[GenerateTestScaffold] failed for '{className}': {ex.GetType().Name}: {ex.Message}");
                return $"ERROR in GenerateTestScaffold: {ex.GetType().Name}: {ex.Message}";
            }
        });
    }
}
