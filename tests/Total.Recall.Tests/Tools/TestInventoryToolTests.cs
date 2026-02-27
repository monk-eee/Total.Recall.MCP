using Total.Recall.Infrastructure;
using Total.Recall.Models;
using Total.Recall.Tools;

namespace Total.Recall.Tests.Tools;

[Collection("ToolTests")]
public sealed class TestInventoryToolTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string? _originalEnv;

    public TestInventoryToolTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "total-recall-tests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempDir);
        _originalEnv = Environment.GetEnvironmentVariable(RepoConfig.EnvVarName);
        Environment.SetEnvironmentVariable(RepoConfig.EnvVarName, _tempDir);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(RepoConfig.EnvVarName, _originalEnv);
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private void SeedTestInventory(params TestInventoryEntry[] records)
    {
        var store = new JsonLineStore<TestInventoryEntry>(RepoConfig.TestInventoryPath(_tempDir));
        store.WriteAll(records);
    }

    [Fact]
    public void GetTestInventory_NoData_ReturnsNotFoundMessage()
    {
        var result = TestInventoryTool.GetTestInventory("Anything");

        Assert.Contains("No test inventory found", result);
    }

    [Fact]
    public void GetTestInventory_MatchingClass_ReturnsEntry()
    {
        SeedTestInventory(new TestInventoryEntry
        {
            Class = "Calculator",
            TestFiles = ["CalculatorTests.cs"],
            TestMethods = ["Add_TwoNumbers_ReturnsSum"],
            TestCount = 1,
            InferredCoveredMethods = ["Add"]
        });

        var result = TestInventoryTool.GetTestInventory("Calculator");

        Assert.Contains("Calculator", result);
        Assert.Contains("Add_TwoNumbers_ReturnsSum", result);
    }

    [Fact]
    public void GetTestInventory_PartialMatch_ReturnsContaining()
    {
        SeedTestInventory(
            new TestInventoryEntry { Class = "StringHelper", TestFiles = [], TestMethods = [], TestCount = 0, InferredCoveredMethods = [] },
            new TestInventoryEntry { Class = "DateHelper", TestFiles = [], TestMethods = [], TestCount = 0, InferredCoveredMethods = [] },
            new TestInventoryEntry { Class = "Parser", TestFiles = [], TestMethods = [], TestCount = 0, InferredCoveredMethods = [] }
        );

        var result = TestInventoryTool.GetTestInventory("Helper");

        Assert.Contains("StringHelper", result);
        Assert.Contains("DateHelper", result);
        Assert.DoesNotContain("Parser", result);
    }

    [Fact]
    public void GetTestInventory_NoMatch_ReturnsNotFoundMessage()
    {
        SeedTestInventory(new TestInventoryEntry
        {
            Class = "Foo",
            TestFiles = [],
            TestMethods = [],
            TestCount = 0,
            InferredCoveredMethods = []
        });

        var result = TestInventoryTool.GetTestInventory("NonExistent");

        Assert.Contains("No existing tests found", result);
    }

    [Fact]
    public void GetTestInventory_CaseInsensitiveSearch()
    {
        SeedTestInventory(new TestInventoryEntry
        {
            Class = "MyService",
            TestFiles = ["MyServiceTests.cs"],
            TestMethods = ["DoWork_Succeeds"],
            TestCount = 1,
            InferredCoveredMethods = ["DoWork"]
        });

        var result = TestInventoryTool.GetTestInventory("myservice");

        Assert.Contains("MyService", result);
    }
}
