using System.Text.RegularExpressions;
using Total.Recall.Infrastructure;
using Total.Recall.Models;

namespace Total.Recall.Scanners;

/// <summary>
/// Scans test project .cs files to build a test inventory:
/// which test methods exist per production class.
/// </summary>
public static partial class TestProjectScanner
{
    // Matches test method attributes across all supported frameworks:
    // xUnit:  [Fact], [Theory]
    // NUnit:  [Test], [TestCase], [TestCaseSource]
    // MSTest: [TestMethod], [DataTestMethod]
    [GeneratedRegex(@"\[(Fact|Theory|Test|TestCase|TestCaseSource|TestMethod|DataTestMethod)(?:\(.*?\))?\]", RegexOptions.Compiled)]
    private static partial Regex TestAttributeRegex();

    // Matches: public (async Task|void) MethodName(
    [GeneratedRegex(@"public\s+(?:async\s+)?(?:Task\s+|void\s+)(\w+)\s*\(", RegexOptions.Compiled)]
    private static partial Regex MethodSignatureRegex();

    /// <summary>
    /// Scan test directory for *Tests*.cs files and write test-inventory.jsonl.
    /// Returns the number of test classes found.
    /// </summary>
    public static int Scan(string testDirectory, string dataDir)
    {
        if (!Directory.Exists(testDirectory))
            throw new DirectoryNotFoundException($"Test directory not found: {testDirectory}");

        var testFiles = Directory.GetFiles(testDirectory, "*Tests*.cs", SearchOption.AllDirectories)
            .Concat(Directory.GetFiles(testDirectory, "*Test.cs", SearchOption.AllDirectories))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Group by inferred production class
        var classMap = new Dictionary<string, TestInventoryEntry>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in testFiles)
        {
            var fileName = Path.GetFileNameWithoutExtension(file);
            var productionClass = InferProductionClass(fileName);

            if (string.IsNullOrEmpty(productionClass))
                continue;

            if (!classMap.TryGetValue(productionClass, out var entry))
            {
                entry = new TestInventoryEntry
                {
                    Class = productionClass,
                    TestFiles = [],
                    TestMethods = [],
                    InferredCoveredMethods = []
                };
                classMap[productionClass] = entry;
            }

            entry.TestFiles.Add(Path.GetFileName(file));

            var methods = ExtractTestMethods(file);
            entry.TestMethods.AddRange(methods);
        }

        // Set counts and infer covered methods
        foreach (var entry in classMap.Values)
        {
            entry.TestCount = entry.TestMethods.Count;
            entry.InferredCoveredMethods = entry.TestMethods
                .Select(InferCoveredMethod)
                .Where(m => !string.IsNullOrEmpty(m))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList()!;
        }

        var records = classMap.Values
            .OrderByDescending(e => e.TestCount)
            .ToList();

        var store = new JsonLineStore<TestInventoryEntry>(RepoConfig.TestInventoryPath(dataDir));
        store.WriteAll(records);

        return records.Count;
    }

    private static List<string> ExtractTestMethods(string filePath)
    {
        var methods = new List<string>();
        var lines = File.ReadAllLines(filePath);
        var testAttrRegex = TestAttributeRegex();
        var methodRegex = MethodSignatureRegex();

        for (int i = 0; i < lines.Length; i++)
        {
            if (!testAttrRegex.IsMatch(lines[i]))
                continue;

            // Look at the next few lines for the method signature
            for (int j = i + 1; j < Math.Min(i + 4, lines.Length); j++)
            {
                var match = methodRegex.Match(lines[j]);
                if (match.Success)
                {
                    methods.Add(match.Groups[1].Value);
                    break;
                }
            }
        }

        return methods;
    }

    /// <summary>
    /// Infer the production class name from a test file name.
    /// "AuditEntryTests.cs" → "AuditEntry"
    /// "AuditEntryAdditionalTests.cs" → "AuditEntry"
    /// </summary>
    private static string InferProductionClass(string testFileName)
    {
        // Strip common test suffixes
        var name = testFileName;

        // Handle "AdditionalTests", "ExtendedTests", etc.
        name = Regex.Replace(name, @"(Additional|Extended|Extra|More|Integration)Tests?$", "", RegexOptions.IgnoreCase);

        // Strip trailing "Tests" or "Test"
        name = Regex.Replace(name, @"Tests?$", "", RegexOptions.IgnoreCase);

        return name.Trim();
    }

    /// <summary>
    /// Heuristic: infer which production method a test covers from the test method name.
    /// "Ctor_Default_SetsDefaults" → "Ctor"
    /// "SetText_Trims_Whitespace" → "SetText"
    /// "Groups_ReturnsNonNullList" → "Groups"
    /// </summary>
    private static string InferCoveredMethod(string testMethodName)
    {
        // Take the first segment before underscore
        var parts = testMethodName.Split('_', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length > 0 ? parts[0] : "";
    }
}
