using Total.Recall.Infrastructure;
using Total.Recall.Models;
using Total.Recall.Tools;

namespace Total.Recall.Tests.Tools;

[Collection("ToolTests")]
public sealed class MockRecipeToolTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string? _originalEnv;

    public MockRecipeToolTests()
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

    private void SeedMockRecipes(params MockRecipe[] records)
    {
        var store = new JsonLineStore<MockRecipe>(RepoConfig.MockRecipesPath(_tempDir));
        store.WriteAll(records);
    }

    [Fact]
    public void GetMockRecipe_NoData_ReturnsNotFoundMessage()
    {
        var result = MockRecipeTool.GetMockRecipe("IService");

        Assert.Contains("No mock recipes found", result);
    }

    [Fact]
    public void GetMockRecipe_ExactInterfaceMatch_ReturnsRecipe()
    {
        SeedMockRecipes(new MockRecipe
        {
            Interface = "ILogger",
            Namespace = "Microsoft.Extensions.Logging",
            Recipe = "var mock = new Mock<ILogger>();",
            RequiredUsings = ["Microsoft.Extensions.Logging"],
            Gotchas = [],
            UsedByClasses = []
        });

        var result = MockRecipeTool.GetMockRecipe("ILogger");

        Assert.Contains("ILogger", result);
        Assert.Contains("Mock", result);
    }

    [Fact]
    public void GetMockRecipe_WithoutIPrefix_NormalizesAndFinds()
    {
        SeedMockRecipes(new MockRecipe
        {
            Interface = "IConfiguration",
            Namespace = "Microsoft.Extensions.Configuration",
            Recipe = "var mock = new Mock<IConfiguration>();",
            RequiredUsings = [],
            Gotchas = [],
            UsedByClasses = []
        });

        // Search without the "I" prefix
        var result = MockRecipeTool.GetMockRecipe("Configuration");

        Assert.Contains("IConfiguration", result);
    }

    [Fact]
    public void GetMockRecipe_WithIPrefix_MatchesWithout()
    {
        SeedMockRecipes(new MockRecipe
        {
            Interface = "IConfiguration",
            Namespace = "Microsoft.Extensions.Configuration",
            Recipe = "var mock = new Mock<IConfiguration>();",
            RequiredUsings = [],
            Gotchas = [],
            UsedByClasses = []
        });

        var result = MockRecipeTool.GetMockRecipe("IConfiguration");

        Assert.Contains("IConfiguration", result);
    }

    [Fact]
    public void GetMockRecipe_NoMatch_ReturnsNotFoundMessage()
    {
        SeedMockRecipes(new MockRecipe
        {
            Interface = "ILogger",
            Namespace = "Microsoft.Extensions.Logging",
            Recipe = "mock",
            RequiredUsings = [],
            Gotchas = [],
            UsedByClasses = []
        });

        var result = MockRecipeTool.GetMockRecipe("ICompletelyDifferent");

        Assert.Contains("No mock recipe found", result);
    }

    [Fact]
    public void GetMockRecipe_PartialMatch_ReturnsContainingRecipes()
    {
        SeedMockRecipes(
            new MockRecipe { Interface = "IJobOutputInstance", Namespace = "App", Recipe = "r1", RequiredUsings = [], Gotchas = [], UsedByClasses = [] },
            new MockRecipe { Interface = "IJobInput", Namespace = "App", Recipe = "r2", RequiredUsings = [], Gotchas = [], UsedByClasses = [] }
        );

        var result = MockRecipeTool.GetMockRecipe("Job");

        Assert.Contains("IJobOutputInstance", result);
        Assert.Contains("IJobInput", result);
    }
}
