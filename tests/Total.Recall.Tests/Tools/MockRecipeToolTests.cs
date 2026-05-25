using Total.Recall.Infrastructure;
using Total.Recall.Models;
using Total.Recall.Tools;

namespace Total.Recall.Tests.Tools;

[Collection("ToolTests")]
public sealed class MockRecipeToolTests : ToolTestBase
{

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
            new MockRecipe { Interface = "IOrderExport", Namespace = "App", Recipe = "r1", RequiredUsings = [], Gotchas = [], UsedByClasses = [] },
            new MockRecipe { Interface = "IOrderInput", Namespace = "App", Recipe = "r2", RequiredUsings = [], Gotchas = [], UsedByClasses = [] }
        );

        var result = MockRecipeTool.GetMockRecipe("Order");

        Assert.Contains("IOrderExport", result);
        Assert.Contains("IOrderInput", result);
    }

    // ── Error path coverage ──

    [Fact]
    public void GetMockRecipe_InvalidNamespace_ReturnsError()
    {
        var result = MockRecipeTool.GetMockRecipe("Any", ns: "\0");

        Assert.StartsWith("ERROR in GetMockRecipe", result);
    }
}
