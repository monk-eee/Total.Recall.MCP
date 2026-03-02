using System.ComponentModel;
using System.Text.Json;
using System.Text.RegularExpressions;
using ModelContextProtocol.Server;
using Total.Recall.Infrastructure;
using Total.Recall.Models;

namespace Total.Recall.Tools;

/// <summary>
/// Analyzes existing test files to learn local conventions (naming, assertion style,
/// mock patterns, helper methods). Results feed into GenerateTestScaffold to produce
/// scaffolds that match the project's established style.
/// </summary>
[McpServerToolType]
public static partial class TestPatternsTool
{
    [McpServerTool, Description(
        "Analyze existing test files to learn project-level conventions: " +
        "naming patterns, assertion styles, mock patterns, helper methods, common usings. " +
        "Results are used by GenerateTestScaffold to produce code that matches existing style. " +
        "Set maxFiles to limit analysis scope (default: 20). " +
        "Requires config.json testsPath to be set (done automatically by scan command).")]
    public static string LearnTestPatterns(
        [Description("Maximum number of test files to analyze (default: 20)")] int maxFiles = 20,
        [Description("Optional: namespace/session to query (default: server default)")] string? ns = null)
    {
        Metrics.Increment(Metrics.ToolLearnTestPatterns);
        Log.Debug($"[LearnTestPatterns] maxFiles={maxFiles} ns='{ns ?? "(default)"}'");
        try
        {
            return LearnTestPatternsCore(maxFiles, ns);
        }
        catch (Exception ex)
        {
            Log.Error($"[LearnTestPatterns] failed: {ex.GetType().Name}: {ex.Message}");
            return $"ERROR in LearnTestPatterns: {ex.GetType().Name}: {ex.Message}";
        }
    }

    private static string LearnTestPatternsCore(int maxFiles, string? ns)
    {
        var stores = StoreRegistry.ForNamespace(ns);
        var config = stores.Config;

        // Get test file paths from test inventory
        var testInventory = stores.TestInventory.HasData()
            ? stores.TestInventory.LoadAll()
            : [];

        if (testInventory.Count == 0)
            return "No test inventory data found. Run the scanner with --tests first.";

        // Resolve actual test file paths from config or inventory
        var testsPath = config.TestsPath;
        if (string.IsNullOrEmpty(testsPath) || !Directory.Exists(testsPath))
        {
            // Try to infer from test inventory file paths
            var firstFile = testInventory.SelectMany(t => t.TestFiles).FirstOrDefault();
            if (firstFile is not null && File.Exists(firstFile))
                testsPath = Path.GetDirectoryName(firstFile);
        }

        if (string.IsNullOrEmpty(testsPath) || !Directory.Exists(testsPath))
            return "Cannot resolve test directory. Set testsPath in config.json or ensure test inventory contains valid file paths.";

        // Collect test file paths: prefer inventory paths, fall back to directory scan
        var testFilePaths = testInventory
            .SelectMany(t => t.TestFiles)
            .Where(f => !string.IsNullOrEmpty(f) && File.Exists(f))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(maxFiles)
            .ToList();

        if (testFilePaths.Count == 0)
        {
            // Fall back to directory scan
            testFilePaths = Directory.GetFiles(testsPath, "*Tests*.cs", SearchOption.AllDirectories)
                .Concat(Directory.GetFiles(testsPath, "*Test.cs", SearchOption.AllDirectories))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(maxFiles)
                .ToList();
        }

        if (testFilePaths.Count == 0)
            return "No test files found to analyze.";

        // Analyze each file
        var patterns = new TestPatterns { AnalyzedFileCount = testFilePaths.Count };
        var allUsings = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var helperCounts = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        int totalTestMethods = 0;
        int filesWithCtorSetup = 0;
        int filesWithDisposable = 0;
        int filesWithFieldMocks = 0;
        int filesWithFluentAssertions = 0;
        int filesWithXunitAssert = 0;
        int filesWithNunitAssert = 0;
        int filesWithMstestAssert = 0;

        // Naming pattern counters
        int namingMethodScenarioExpected = 0;  // Method_Scenario_Expected
        int namingShouldWhen = 0;               // Should_Verb_When_Condition
        int namingGivenWhenThen = 0;            // GivenX_WhenY_ThenZ

        foreach (var filePath in testFilePaths)
        {
            try
            {
                var content = File.ReadAllText(filePath);
                var fileName = Path.GetFileName(filePath);

                // --- Usings ---
                foreach (Match m in UsingRegex().Matches(content))
                {
                    var usingNs = m.Groups[1].Value;
                    // Skip framework usings (always present)
                    if (usingNs is "System" or "Xunit" or "NUnit.Framework" or "Microsoft.VisualStudio.TestTools.UnitTesting")
                        continue;
                    allUsings.TryGetValue(usingNs, out var count);
                    allUsings[usingNs] = count + 1;
                }

                // --- Assertion style ---
                if (content.Contains("Assert.Equal") || content.Contains("Assert.True") || content.Contains("Assert.NotNull"))
                    filesWithXunitAssert++;
                if (content.Contains(".Should()") || content.Contains(".ShouldBe(") || content.Contains("Should().Be"))
                    filesWithFluentAssertions++;
                if (content.Contains("Assert.That(") || content.Contains("Assert.AreEqual"))
                    filesWithNunitAssert++;
                if (content.Contains("Assert.AreEqual(") && content.Contains("[TestMethod]"))
                    filesWithMstestAssert++;

                // --- Constructor setup vs method setup ---
                if (CtorSetupRegex().IsMatch(content))
                    filesWithCtorSetup++;
                if (content.Contains("IDisposable") || content.Contains(": IAsyncDisposable"))
                    filesWithDisposable++;

                // --- Mock pattern ---
                if (FieldMockRegex().IsMatch(content))
                    filesWithFieldMocks++;

                // --- Test method names → naming pattern ---
                foreach (Match m in TestMethodRegex().Matches(content))
                {
                    totalTestMethods++;
                    var name = m.Groups[1].Value;
                    var underscores = name.Count(c => c == '_');

                    if (underscores >= 2)
                    {
                        if (name.Contains("_Should", StringComparison.OrdinalIgnoreCase))
                            namingMethodScenarioExpected++;
                        else if (name.StartsWith("Given", StringComparison.OrdinalIgnoreCase)
                            || name.StartsWith("When", StringComparison.OrdinalIgnoreCase))
                            namingGivenWhenThen++;
                        else
                            namingMethodScenarioExpected++; // Default 3-part is Method_Scenario_Expected
                    }
                    else if (name.Contains("Should", StringComparison.OrdinalIgnoreCase))
                    {
                        namingShouldWhen++;
                    }
                    else
                    {
                        namingMethodScenarioExpected++;
                    }
                }

                // --- Helper methods (private/protected non-test methods) ---
                foreach (Match m in HelperMethodRegex().Matches(content))
                {
                    var helperName = m.Groups[1].Value;
                    // Skip test methods and common override names
                    if (helperName == "Dispose" || helperName == "DisposeAsync")
                        continue;
                    if (!helperCounts.TryGetValue(helperName, out var files))
                    {
                        files = [];
                        helperCounts[helperName] = files;
                    }
                    files.Add(fileName);
                }
            }
            catch (Exception ex)
            {
                Log.Warn($"[LearnTestPatterns] failed to read '{filePath}': {ex.Message}");
            }
        }

        // --- Aggregate results ---

        // Assertion style
        var maxAssert = Math.Max(Math.Max(filesWithXunitAssert, filesWithFluentAssertions),
            Math.Max(filesWithNunitAssert, filesWithMstestAssert));
        patterns.AssertionStyle = maxAssert switch
        {
            _ when maxAssert == filesWithFluentAssertions && maxAssert > 0 => "FluentAssertions",
            _ when maxAssert == filesWithNunitAssert && maxAssert > 0 => "NUnit.Assert",
            _ when maxAssert == filesWithMstestAssert && maxAssert > 0 => "MSTest.Assert",
            _ => "xUnit.Assert"
        };

        // Naming pattern
        var maxNaming = Math.Max(Math.Max(namingMethodScenarioExpected, namingShouldWhen), namingGivenWhenThen);
        patterns.NamingPattern = maxNaming switch
        {
            _ when maxNaming == namingShouldWhen && maxNaming > 0 => "ShouldVerb_WhenCondition",
            _ when maxNaming == namingGivenWhenThen && maxNaming > 0 => "GivenX_WhenY_ThenZ",
            _ => "MethodName_Scenario_Expected"
        };

        // Constructor setup
        patterns.UsesConstructorSetup = filesWithCtorSetup > testFilePaths.Count / 2;
        patterns.UsesDisposable = filesWithDisposable > testFilePaths.Count / 4;
        patterns.MockPattern = filesWithFieldMocks > testFilePaths.Count / 2 ? "field" : "local";

        // Average tests per class
        patterns.AvgTestsPerClass = testFilePaths.Count > 0
            ? (double)totalTestMethods / testFilePaths.Count
            : 0;

        // Common usings (appearing in >25% of files)
        var threshold = Math.Max(2, testFilePaths.Count / 4);
        patterns.CommonUsings = allUsings
            .Where(kv => kv.Value >= threshold)
            .OrderByDescending(kv => kv.Value)
            .Select(kv => kv.Key)
            .Take(15)
            .ToList();

        // Helper methods used in multiple files
        patterns.HelperMethods = helperCounts
            .Where(kv => kv.Value.Count >= 2) // appeared in 2+ files
            .Select(kv => new TestHelperMethod
            {
                Name = kv.Key,
                FoundIn = kv.Value.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                UsageCount = kv.Value.Count
            })
            .OrderByDescending(h => h.UsageCount)
            .Take(20)
            .ToList();

        var result = new
        {
            patterns,
            summary = new
            {
                filesAnalyzed = testFilePaths.Count,
                totalTestMethods,
                assertionStyle = patterns.AssertionStyle,
                namingPattern = patterns.NamingPattern,
                usesConstructorSetup = patterns.UsesConstructorSetup,
                mockPattern = patterns.MockPattern,
                commonUsingCount = patterns.CommonUsings.Count,
                sharedHelperCount = patterns.HelperMethods.Count,
                namingBreakdown = new { methodScenarioExpected = namingMethodScenarioExpected, shouldWhen = namingShouldWhen, givenWhenThen = namingGivenWhenThen }
            }
        };

        return JsonSerializer.Serialize(result, SharedJsonOptions.CamelCaseIndented);
    }

    // ── Compiled regex patterns (source-generated for performance) ──

    [GeneratedRegex(@"^using\s+([\w.]+);", RegexOptions.Multiline)]
    private static partial Regex UsingRegex();

    [GeneratedRegex(@"public\s+\w+Tests?\s*\(", RegexOptions.Compiled)]
    private static partial Regex CtorSetupRegex();

    [GeneratedRegex(@"private\s+(?:readonly\s+)?Mock<", RegexOptions.Compiled)]
    private static partial Regex FieldMockRegex();

    [GeneratedRegex(@"public\s+(?:async\s+)?(?:Task\s+|void\s+)(\w+)\s*\(", RegexOptions.Compiled)]
    private static partial Regex TestMethodRegex();

    [GeneratedRegex(@"private\s+(?:static\s+)?(?:\w+\s+)(\w+)\s*\(", RegexOptions.Compiled)]
    private static partial Regex HelperMethodRegex();
}
