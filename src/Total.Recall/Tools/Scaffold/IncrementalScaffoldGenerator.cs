using System.Text;
using System.Text.Json;
using Total.Recall.Infrastructure;
using Total.Recall.Models;

namespace Total.Recall.Tools.Scaffold;

/// <summary>
/// Produces only [Fact] method stubs (no class skeleton, no constructor, no mock fields)
/// for a caller-specified set of method names. Used when extending an existing test file
/// for a partially-covered class.
///
/// Pure store-driven generator: takes a class name + comma-separated method names + ns,
/// returns a JSON envelope containing the rendered stub block. No telemetry or error
/// handling here \u2014 <see cref="TestScaffoldTool"/> wraps that.
/// </summary>
internal static class IncrementalScaffoldGenerator
{
    public static string Generate(string className, string methodNames, string? ns)
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
                g.ShortName.Equals(typeRecord.Name, StringComparison.OrdinalIgnoreCase)
                || g.ShortName.Equals(className, StringComparison.OrdinalIgnoreCase)
                || g.ClassName.Equals(className, StringComparison.OrdinalIgnoreCase))
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
            sb.AppendLine($"    // \u26a0\ufe0f  GOTCHAS for {typeRecord.Name}:");
            foreach (var g in gotchas)
                sb.AppendLine($"    //   [{g.Category}] {g.Description}");
            sb.AppendLine();
        }

        sb.AppendLine($"    // \u2500\u2500 Incremental stubs for {typeRecord.Name} ({allMethods.Count} methods) \u2500\u2500");

        // Build disambiguated names for overloaded methods
        var disambiguatedNames = MethodNaming.BuildDisambiguatedNames(allMethods);

        foreach (var method in allMethods)
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
            var hint = AssertionRules.GetAssertionHint(method.Name);
            if (method.UncoveredLineCount > 0)
                sb.AppendLine($"        // {hint} (uncovered: lines {method.FirstUncoveredLine}-{method.LastUncoveredLine}, {method.UncoveredLineCount} of {method.TotalLines})");
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
}
