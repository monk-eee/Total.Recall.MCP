using System.Text.Json;
using Total.Recall.Infrastructure;
using Total.Recall.Models;
using Total.Recall.Tools;

namespace Total.Recall.Tests.Tools;

/// <summary>
/// Tests for TestPatternsTool.LearnTestPatterns — analyzes existing test files
/// to learn naming, assertion, mock, and helper patterns.
/// </summary>
[Collection("ToolTests")]
public sealed class TestPatternsToolTests : ToolTestBase
{
    private readonly string _testsDir;

    public TestPatternsToolTests() : base(saveNamespace: true)
    {
        _testsDir = Path.Combine(TempDir, "TestProject");
        Directory.CreateDirectory(_testsDir);
    }

    // ── Helpers ──

    private void SeedConfig(string? testsPath = null)
    {
        var config = new NamespaceConfig { TestsPath = testsPath ?? _testsDir };
        var json = JsonSerializer.Serialize(config, SharedJsonOptions.CamelCaseIndented);
        File.WriteAllText(RepoConfig.ConfigJsonPath(TempDir), json);
    }


    private string WriteTestFile(string fileName, string content)
    {
        var filePath = Path.Combine(_testsDir, fileName);
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        File.WriteAllText(filePath, content);
        return filePath;
    }

    // ── No data ──

    [Fact]
    public void LearnTestPatterns_NoInventory_ReturnsError()
    {
        var result = TestPatternsTool.LearnTestPatterns();
        Assert.Contains("No test inventory data found", result);
    }

    [Fact]
    public void LearnTestPatterns_NoTestsPath_ReturnsError()
    {
        // Inventory exists but no actual files and no config testsPath
        SeedTestInventory(new TestInventoryEntry
        {
            Class = "MyClass",
            TestFiles = ["/nonexistent/path/MyClassTests.cs"]
        });

        var result = TestPatternsTool.LearnTestPatterns();
        Assert.Contains("Cannot resolve test directory", result);
    }

    // ── xUnit assertion style detection ──

    [Fact]
    public void LearnTestPatterns_XunitAssertions_DetectsXunitStyle()
    {
        var filePath = WriteTestFile("ServiceTests.cs", """
            using Xunit;
            using Moq;

            public class ServiceTests
            {
                private readonly Mock<ILogger> _mockLogger;

                public ServiceTests()
                {
                    _mockLogger = new Mock<ILogger>();
                }

                [Fact]
                public void DoWork_WhenCalled_ReturnsTrue()
                {
                    Assert.True(true);
                }

                [Fact]
                public void DoWork_WithNull_ThrowsException()
                {
                    Assert.NotNull("test");
                }
            }
            """);

        SeedTestInventory(new TestInventoryEntry
        {
            Class = "ServiceTests",
            TestFiles = [filePath]
        });

        var result = TestPatternsTool.LearnTestPatterns();

        Assert.Contains("xUnit.Assert", result);
        Assert.Contains("MethodName_Scenario_Expected", result);
    }

    // ── FluentAssertions detection ──

    [Fact]
    public void LearnTestPatterns_FluentAssertions_DetectsFluentStyle()
    {
        var file1 = WriteTestFile("Test1.cs", """
            using FluentAssertions;

            public class Test1
            {
                public void Method_Should_Work()
                {
                    "hello".Should().Be("hello");
                }
            }
            """);
        var file2 = WriteTestFile("Test2.cs", """
            using FluentAssertions;

            public class Test2
            {
                public void Another_Should_Pass()
                {
                    42.Should().Be(42);
                }
            }
            """);

        SeedTestInventory(
            new TestInventoryEntry { Class = "Test1", TestFiles = [file1] },
            new TestInventoryEntry { Class = "Test2", TestFiles = [file2] });

        var result = TestPatternsTool.LearnTestPatterns();
        Assert.Contains("FluentAssertions", result);
    }

    // ── Constructor setup detection ──

    [Fact]
    public void LearnTestPatterns_ConstructorSetup_DetectsCtorPattern()
    {
        var file1 = WriteTestFile("ServiceTests.cs", """
            using Moq;

            public class ServiceTests : IDisposable
            {
                private readonly Mock<IService> _mockService;

                public ServiceTests()
                {
                    _mockService = new Mock<IService>();
                }

                public void Dispose() { }

                public void TestMethod_One_Two()
                {
                    Assert.Equal(1, 1);
                }
            }
            """);
        var file2 = WriteTestFile("RepoTests.cs", """
            using Moq;

            public class RepoTests : IDisposable
            {
                private readonly Mock<IRepo> _mockRepo;

                public RepoTests()
                {
                    _mockRepo = new Mock<IRepo>();
                }

                public void Dispose() { }

                public void TestAnother_Case_Result()
                {
                    Assert.True(true);
                }
            }
            """);

        SeedTestInventory(
            new TestInventoryEntry { Class = "ServiceTests", TestFiles = [file1] },
            new TestInventoryEntry { Class = "RepoTests", TestFiles = [file2] });

        var result = TestPatternsTool.LearnTestPatterns();

        Assert.Contains("\"usesConstructorSetup\": true", result);
        Assert.Contains("\"usesDisposable\": true", result);
        Assert.Contains("\"mockPattern\": \"field\"", result);
    }

    // ── Helper method detection ──

    [Fact]
    public void LearnTestPatterns_SharedHelpers_DetectsAcrossFiles()
    {
        var file1 = WriteTestFile("HelperTest1.cs", """
            public class HelperTest1
            {
                private MyEntity CreateTestEntity()
                {
                    return new MyEntity();
                }

                public void Test_Create_Works()
                {
                    var e = CreateTestEntity();
                }
            }
            """);
        var file2 = WriteTestFile("HelperTest2.cs", """
            public class HelperTest2
            {
                private MyEntity CreateTestEntity()
                {
                    return new MyEntity { Id = 42 };
                }

                public void Test_Update_Works()
                {
                    var e = CreateTestEntity();
                }
            }
            """);

        SeedTestInventory(
            new TestInventoryEntry { Class = "HelperTest1", TestFiles = [file1] },
            new TestInventoryEntry { Class = "HelperTest2", TestFiles = [file2] });

        var result = TestPatternsTool.LearnTestPatterns();

        Assert.Contains("CreateTestEntity", result);
        Assert.Contains("\"usageCount\": 2", result);
    }

    // ── Common usings aggregation ──

    [Fact]
    public void LearnTestPatterns_CommonUsings_AggregatesAcrossFiles()
    {
        var file1 = WriteTestFile("Using1.cs", """
            using Moq;
            using MyCompany.TestHelpers;

            public class Using1Tests
            {
                public void Test_A_B() { }
            }
            """);
        var file2 = WriteTestFile("Using2.cs", """
            using Moq;
            using MyCompany.TestHelpers;

            public class Using2Tests
            {
                public void Test_C_D() { }
            }
            """);

        SeedTestInventory(
            new TestInventoryEntry { Class = "Using1Tests", TestFiles = [file1] },
            new TestInventoryEntry { Class = "Using2Tests", TestFiles = [file2] });

        var result = TestPatternsTool.LearnTestPatterns();

        Assert.Contains("Moq", result);
        Assert.Contains("MyCompany.TestHelpers", result);
    }

    // ── Should‐style naming detection ──

    [Fact]
    public void LearnTestPatterns_ShouldNaming_DetectsShouldVerbStyle()
    {
        var file1 = WriteTestFile("ShouldTest.cs", """
            public class ShouldTests
            {
                public void ShouldReturnTrueWhenValid() { }
                public void ShouldThrowExceptionWhenNull() { }
                public void ShouldProcessInput() { }
            }
            """);

        SeedTestInventory(new TestInventoryEntry
        {
            Class = "ShouldTests",
            TestFiles = [file1]
        });

        var result = TestPatternsTool.LearnTestPatterns();
        Assert.Contains("ShouldVerb_WhenCondition", result);
    }

    // ── maxFiles parameter ──

    [Fact]
    public void LearnTestPatterns_MaxFiles_LimitsAnalysis()
    {
        var file1 = WriteTestFile("Limited1.cs", """
            public class Limited1Tests
            {
                public void Test_A_B() { }
            }
            """);
        var file2 = WriteTestFile("Limited2.cs", """
            public class Limited2Tests
            {
                public void Test_C_D() { }
            }
            """);

        SeedTestInventory(
            new TestInventoryEntry { Class = "Limited1Tests", TestFiles = [file1] },
            new TestInventoryEntry { Class = "Limited2Tests", TestFiles = [file2] });

        var result = TestPatternsTool.LearnTestPatterns(maxFiles: 1);
        Assert.Contains("\"filesAnalyzed\": 1", result);
    }

    // ── Directory fallback scan ──

    [Fact]
    public void LearnTestPatterns_FallbackDirScan_FindsTestFiles()
    {
        SeedConfig(_testsDir);
        WriteTestFile("MyServiceTests.cs", """
            using Xunit;

            public class MyServiceTests
            {
                [Fact]
                public void TestMethod_Scenario_Expected()
                {
                    Assert.Equal(1, 1);
                }
            }
            """);

        // Inventory has entries but no valid file paths → triggers fallback
        SeedTestInventory(new TestInventoryEntry
        {
            Class = "MyServiceTests",
            TestFiles = ["/nonexistent/MyServiceTests.cs"]
        });

        var result = TestPatternsTool.LearnTestPatterns();

        Assert.Contains("\"filesAnalyzed\": 1", result);
    }

    // ── Average tests per class ──

    [Fact]
    public void LearnTestPatterns_AvgTestsPerClass_Calculated()
    {
        var file1 = WriteTestFile("Multi1.cs", """
            public class Multi1Tests
            {
                public void Test_A_B() { }
                public void Test_C_D() { }
                public void Test_E_F() { }
            }
            """);

        SeedTestInventory(new TestInventoryEntry
        {
            Class = "Multi1Tests",
            TestFiles = [file1]
        });

        var result = TestPatternsTool.LearnTestPatterns();
        Assert.Contains("\"totalTestMethods\": 3", result);
    }
}
