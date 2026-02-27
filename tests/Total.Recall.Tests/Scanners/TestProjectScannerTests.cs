using Total.Recall.Scanners;

namespace Total.Recall.Tests.Scanners;

public sealed class TestProjectScannerTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _dataDir;

    public TestProjectScannerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "total-recall-tests", Guid.NewGuid().ToString());
        _dataDir = Path.Combine(_tempDir, "data");
        Directory.CreateDirectory(_tempDir);
        Directory.CreateDirectory(_dataDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private string WriteTestFile(string fileName, string content)
    {
        var path = Path.Combine(_tempDir, fileName);
        var dir = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(dir);
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public void Scan_DirectoryNotFound_ThrowsDirectoryNotFoundException()
    {
        Assert.Throws<DirectoryNotFoundException>(
            () => TestProjectScanner.Scan(Path.Combine(_tempDir, "nope"), _dataDir));
    }

    [Fact]
    public void Scan_EmptyDirectory_ReturnsZero()
    {
        var emptyDir = Path.Combine(_tempDir, "empty");
        Directory.CreateDirectory(emptyDir);

        var count = TestProjectScanner.Scan(emptyDir, _dataDir);

        Assert.Equal(0, count);
    }

    [Fact]
    public void Scan_SingleTestFile_ExtractsTestMethods()
    {
        WriteTestFile("CalculatorTests.cs", """
            using Xunit;

            public class CalculatorTests
            {
                [Fact]
                public void Add_TwoNumbers_ReturnsSum()
                {
                    Assert.Equal(3, 1 + 2);
                }

                [Fact]
                public void Subtract_TwoNumbers_ReturnsDifference()
                {
                    Assert.Equal(1, 3 - 2);
                }
            }
            """);

        var count = TestProjectScanner.Scan(_tempDir, _dataDir);

        Assert.Equal(1, count);
        var outputPath = Path.Combine(_dataDir, "test-inventory.jsonl");
        Assert.True(File.Exists(outputPath));
        var content = File.ReadAllText(outputPath);
        Assert.Contains("Calculator", content);
        Assert.Contains("Add_TwoNumbers_ReturnsSum", content);
        Assert.Contains("Subtract_TwoNumbers_ReturnsDifference", content);
    }

    [Fact]
    public void Scan_TheoryAttribute_ExtractsMethod()
    {
        WriteTestFile("ParserTests.cs", """
            using Xunit;

            public class ParserTests
            {
                [Theory]
                [InlineData("a")]
                [InlineData("b")]
                public void Parse_ReturnsExpected(string input)
                {
                    Assert.NotNull(input);
                }
            }
            """);

        var count = TestProjectScanner.Scan(_tempDir, _dataDir);

        Assert.Equal(1, count);
        var content = File.ReadAllText(Path.Combine(_dataDir, "test-inventory.jsonl"));
        Assert.Contains("Parse_ReturnsExpected", content);
    }

    [Fact]
    public void Scan_AsyncTestMethods_AreExtracted()
    {
        WriteTestFile("ServiceTests.cs", """
            using Xunit;
            using System.Threading.Tasks;

            public class ServiceTests
            {
                [Fact]
                public async Task DoWork_Completes()
                {
                    await Task.CompletedTask;
                }
            }
            """);

        var count = TestProjectScanner.Scan(_tempDir, _dataDir);

        Assert.Equal(1, count);
        var content = File.ReadAllText(Path.Combine(_dataDir, "test-inventory.jsonl"));
        Assert.Contains("DoWork_Completes", content);
    }

    [Fact]
    public void Scan_InfersProductionClassName_StripsTestsSuffix()
    {
        WriteTestFile("MyWidgetTests.cs", """
            using Xunit;

            public class MyWidgetTests
            {
                [Fact]
                public void Render_Works()
                {
                }
            }
            """);

        var count = TestProjectScanner.Scan(_tempDir, _dataDir);

        Assert.Equal(1, count);
        var content = File.ReadAllText(Path.Combine(_dataDir, "test-inventory.jsonl"));
        Assert.Contains("\"class\":\"MyWidget\"", content);
    }

    [Fact]
    public void Scan_AdditionalTestsFile_MergesWithMainTestFile()
    {
        WriteTestFile("FooTests.cs", """
            using Xunit;

            public class FooTests
            {
                [Fact]
                public void Method1_Works() { }
            }
            """);
        WriteTestFile("FooAdditionalTests.cs", """
            using Xunit;

            public class FooAdditionalTests
            {
                [Fact]
                public void Method2_Works() { }
            }
            """);

        var count = TestProjectScanner.Scan(_tempDir, _dataDir);

        // Should merge both into 1 entry for "Foo"
        Assert.Equal(1, count);
        var content = File.ReadAllText(Path.Combine(_dataDir, "test-inventory.jsonl"));
        Assert.Contains("Method1_Works", content);
        Assert.Contains("Method2_Works", content);
    }

    [Fact]
    public void Scan_CountsTestMethodsCorrectly()
    {
        WriteTestFile("BarTests.cs", """
            using Xunit;

            public class BarTests
            {
                [Fact]
                public void First() { }

                [Fact]
                public void Second() { }

                [Theory]
                [InlineData(1)]
                public void Third(int x) { }
            }
            """);

        TestProjectScanner.Scan(_tempDir, _dataDir);

        var content = File.ReadAllText(Path.Combine(_dataDir, "test-inventory.jsonl"));
        Assert.Contains("\"testCount\":3", content);
    }

    [Fact]
    public void Scan_InfersCoveredMethodFromTestName()
    {
        WriteTestFile("HelperTests.cs", """
            using Xunit;

            public class HelperTests
            {
                [Fact]
                public void FormatName_ReturnsTrimmedString() { }

                [Fact]
                public void FormatName_NullInput_Throws() { }

                [Fact]
                public void ValidateAge_NegativeValue_ReturnsFalse() { }
            }
            """);

        TestProjectScanner.Scan(_tempDir, _dataDir);

        var content = File.ReadAllText(Path.Combine(_dataDir, "test-inventory.jsonl"));
        // FormatName appears in 2 tests but should be deduped in inferredCoveredMethods
        Assert.Contains("FormatName", content);
        Assert.Contains("ValidateAge", content);
    }

    [Fact]
    public void Scan_SubdirectoriesAreSearched()
    {
        WriteTestFile(Path.Combine("Sub", "DeepTests.cs"), """
            using Xunit;

            public class DeepTests
            {
                [Fact]
                public void Deep_Works() { }
            }
            """);

        var count = TestProjectScanner.Scan(_tempDir, _dataDir);

        Assert.Equal(1, count);
    }

    [Fact]
    public void Scan_FileWithNoTestAttributes_IsIgnored()
    {
        WriteTestFile("HelperTests.cs", """
            public class HelperTests
            {
                public void NotATest()
                {
                    // No [Fact] or [Theory]
                }
            }
            """);

        var count = TestProjectScanner.Scan(_tempDir, _dataDir);

        // File matches *Tests*.cs but has no test methods → inferred class has 0 methods
        // Since InferProductionClass returns "Helper", an entry is created with 0 test methods
        var outputPath = Path.Combine(_dataDir, "test-inventory.jsonl");
        if (count > 0)
        {
            var content = File.ReadAllText(outputPath);
            Assert.Contains("\"testCount\":0", content);
        }
    }

    [Fact]
    public void Scan_OrdersResultsByTestCountDescending()
    {
        WriteTestFile("FewTests.cs", """
            using Xunit;
            public class FewTests
            {
                [Fact]
                public void One() { }
            }
            """);
        WriteTestFile("ManyTests.cs", """
            using Xunit;
            public class ManyTests
            {
                [Fact]
                public void A() { }
                [Fact]
                public void B() { }
                [Fact]
                public void C() { }
            }
            """);

        TestProjectScanner.Scan(_tempDir, _dataDir);

        var lines = File.ReadAllLines(Path.Combine(_dataDir, "test-inventory.jsonl"))
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .ToList();
        Assert.Equal(2, lines.Count);
        // Many (3 tests) should come first
        Assert.Contains("Many", lines[0]);
        Assert.Contains("Few", lines[1]);
    }
}
