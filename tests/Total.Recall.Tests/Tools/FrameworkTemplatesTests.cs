using Total.Recall.Models;
using Total.Recall.Tools;

namespace Total.Recall.Tests.Tools;

/// <summary>
/// Tests for FrameworkTemplates — pure static helpers for test scaffold code generation.
/// Covers all three test frameworks (xUnit, NUnit, MSTest) and all three mock libraries
/// (Moq, NSubstitute, FakeItEasy).
/// </summary>
public sealed class FrameworkTemplatesTests
{
    // ── GetTestUsing ──

    [Theory]
    [InlineData(TestFramework.XUnit, "using Xunit;")]
    [InlineData(TestFramework.NUnit, "using NUnit.Framework;")]
    [InlineData(TestFramework.MSTest, "using Microsoft.VisualStudio.TestTools.UnitTesting;")]
    public void GetTestUsing_ReturnsCorrectUsing(TestFramework framework, string expected)
    {
        Assert.Equal(expected, FrameworkTemplates.GetTestUsing(framework));
    }

    // ── GetTestAttribute ──

    [Theory]
    [InlineData(TestFramework.XUnit, "[Fact]")]
    [InlineData(TestFramework.NUnit, "[Test]")]
    [InlineData(TestFramework.MSTest, "[TestMethod]")]
    public void GetTestAttribute_ReturnsCorrectAttribute(TestFramework framework, string expected)
    {
        Assert.Equal(expected, FrameworkTemplates.GetTestAttribute(framework));
    }

    // ── GetClassAttribute ──

    [Fact]
    public void GetClassAttribute_XUnit_ReturnsNull()
    {
        Assert.Null(FrameworkTemplates.GetClassAttribute(TestFramework.XUnit));
    }

    [Theory]
    [InlineData(TestFramework.NUnit, "[TestFixture]")]
    [InlineData(TestFramework.MSTest, "[TestClass]")]
    public void GetClassAttribute_NonXUnit_ReturnsAttribute(TestFramework framework, string expected)
    {
        Assert.Equal(expected, FrameworkTemplates.GetClassAttribute(framework));
    }

    // ── GetAssertNotNull ──

    [Theory]
    [InlineData(TestFramework.XUnit, "Assert.NotNull(_sut);")]
    [InlineData(TestFramework.NUnit, "Assert.That(_sut, Is.Not.Null);")]
    [InlineData(TestFramework.MSTest, "Assert.IsNotNull(_sut);")]
    public void GetAssertNotNull_ReturnsCorrectAssertion(TestFramework framework, string expected)
    {
        Assert.Equal(expected, FrameworkTemplates.GetAssertNotNull(framework, "_sut"));
    }

    // ── GetAssertThrows ──

    [Fact]
    public void GetAssertThrows_XUnit_ReturnsXUnitThrows()
    {
        var result = FrameworkTemplates.GetAssertThrows(TestFramework.XUnit, "ArgumentNullException", "new Sut(null)");
        Assert.Contains("Assert.Throws<ArgumentNullException>", result);
        Assert.Contains("new Sut(null)", result);
    }

    [Fact]
    public void GetAssertThrows_NUnit_ReturnsNUnitThrows()
    {
        var result = FrameworkTemplates.GetAssertThrows(TestFramework.NUnit, "ArgumentNullException", "new Sut(null)");
        Assert.Contains("Assert.Throws<ArgumentNullException>", result);
    }

    [Fact]
    public void GetAssertThrows_MSTest_ReturnsMSTestThrows()
    {
        var result = FrameworkTemplates.GetAssertThrows(TestFramework.MSTest, "ArgumentNullException", "new Sut(null)");
        Assert.Contains("Assert.ThrowsException<ArgumentNullException>", result);
    }

    // ── GetSetupAttribute ──

    [Fact]
    public void GetSetupAttribute_XUnit_ReturnsNull()
    {
        Assert.Null(FrameworkTemplates.GetSetupAttribute(TestFramework.XUnit));
    }

    [Theory]
    [InlineData(TestFramework.NUnit, "[SetUp]")]
    [InlineData(TestFramework.MSTest, "[TestInitialize]")]
    public void GetSetupAttribute_NonXUnit_ReturnsAttribute(TestFramework framework, string expected)
    {
        Assert.Equal(expected, FrameworkTemplates.GetSetupAttribute(framework));
    }

    // ── UsesConstructorSetup ──

    [Fact]
    public void UsesConstructorSetup_XUnit_ReturnsTrue()
    {
        Assert.True(FrameworkTemplates.UsesConstructorSetup(TestFramework.XUnit));
    }

    [Theory]
    [InlineData(TestFramework.NUnit)]
    [InlineData(TestFramework.MSTest)]
    public void UsesConstructorSetup_NonXUnit_ReturnsFalse(TestFramework framework)
    {
        Assert.False(FrameworkTemplates.UsesConstructorSetup(framework));
    }

    // ── GetMockUsing ──

    [Theory]
    [InlineData(MockLibrary.Moq, "using Moq;")]
    [InlineData(MockLibrary.NSubstitute, "using NSubstitute;")]
    [InlineData(MockLibrary.FakeItEasy, "using FakeItEasy;")]
    public void GetMockUsing_ReturnsCorrectUsing(MockLibrary library, string expected)
    {
        Assert.Equal(expected, FrameworkTemplates.GetMockUsing(library));
    }

    // ── GetMockFieldDeclaration ──

    [Fact]
    public void GetMockFieldDeclaration_Moq_ReturnsMockWrapper()
    {
        var result = FrameworkTemplates.GetMockFieldDeclaration(MockLibrary.Moq, "ILogger", "_mockLogger");
        Assert.Equal("private readonly Mock<ILogger> _mockLogger;", result);
    }

    [Theory]
    [InlineData(MockLibrary.NSubstitute)]
    [InlineData(MockLibrary.FakeItEasy)]
    public void GetMockFieldDeclaration_DirectProxy_ReturnsInterfaceType(MockLibrary library)
    {
        var result = FrameworkTemplates.GetMockFieldDeclaration(library, "ILogger", "_mockLogger");
        Assert.Equal("private readonly ILogger _mockLogger;", result);
    }

    // ── GetMockInitialization ──

    [Fact]
    public void GetMockInitialization_Moq_ReturnsNewMock()
    {
        var result = FrameworkTemplates.GetMockInitialization(MockLibrary.Moq, "IService", "_mockService");
        Assert.Equal("_mockService = new Mock<IService>();", result);
    }

    [Fact]
    public void GetMockInitialization_NSubstitute_ReturnsSubstituteFor()
    {
        var result = FrameworkTemplates.GetMockInitialization(MockLibrary.NSubstitute, "IService", "_mockService");
        Assert.Equal("_mockService = Substitute.For<IService>();", result);
    }

    [Fact]
    public void GetMockInitialization_FakeItEasy_ReturnsAFake()
    {
        var result = FrameworkTemplates.GetMockInitialization(MockLibrary.FakeItEasy, "IService", "_mockService");
        Assert.Equal("_mockService = A.Fake<IService>();", result);
    }

    // ── GetMockObjectExpression ──

    [Fact]
    public void GetMockObjectExpression_Moq_AppendsDotObject()
    {
        Assert.Equal("_mock.Object", FrameworkTemplates.GetMockObjectExpression(MockLibrary.Moq, "_mock"));
    }

    [Theory]
    [InlineData(MockLibrary.NSubstitute)]
    [InlineData(MockLibrary.FakeItEasy)]
    public void GetMockObjectExpression_DirectProxy_ReturnsFieldNameOnly(MockLibrary library)
    {
        Assert.Equal("_mock", FrameworkTemplates.GetMockObjectExpression(library, "_mock"));
    }

    // ── GetVerifyHint ──

    [Theory]
    [InlineData(MockLibrary.Moq, "mock.Verify")]
    [InlineData(MockLibrary.NSubstitute, "Received()")]
    [InlineData(MockLibrary.FakeItEasy, "A.CallTo(...).MustHaveHappened()")]
    public void GetVerifyHint_ReturnsCorrectHint(MockLibrary library, string expected)
    {
        Assert.Equal(expected, FrameworkTemplates.GetVerifyHint(library));
    }

    // ── DeriveTestNamespace ──

    [Fact]
    public void DeriveTestNamespace_NullNamespace_ReturnsTests()
    {
        Assert.Equal("Tests", FrameworkTemplates.DeriveTestNamespace(null, "{Namespace}.Tests"));
    }

    [Fact]
    public void DeriveTestNamespace_EmptyNamespace_ReturnsTests()
    {
        Assert.Equal("Tests", FrameworkTemplates.DeriveTestNamespace("", "{Namespace}.Tests"));
    }

    [Fact]
    public void DeriveTestNamespace_SimplePattern_AppendsTests()
    {
        var result = FrameworkTemplates.DeriveTestNamespace("App.Services", "{Namespace}.Tests");
        Assert.Equal("App.Services.Tests", result);
    }

    [Fact]
    public void DeriveTestNamespace_RootRestPattern_SplitsCorrectly()
    {
        var result = FrameworkTemplates.DeriveTestNamespace("MyApp.Common.Extensions", "{RootNamespace}.Tests.{Rest}");
        Assert.Equal("MyApp.Tests.Common.Extensions", result);
    }

    [Fact]
    public void DeriveTestNamespace_RootRestPattern_SingleSegment_CleansDots()
    {
        // Single-segment namespace: "App" → RootNamespace="App", Rest=""
        // Pattern "{RootNamespace}.Tests.{Rest}" → "App.Tests." → cleaned to "App.Tests"
        var result = FrameworkTemplates.DeriveTestNamespace("App", "{RootNamespace}.Tests.{Rest}");
        Assert.Equal("App.Tests", result);
    }

    [Fact]
    public void DeriveTestNamespace_FallbackPattern_AppendsTests()
    {
        // A pattern without placeholders falls through to the default
        var result = FrameworkTemplates.DeriveTestNamespace("MyApp", "SomeRandomPattern");
        Assert.Equal("MyApp.Tests", result);
    }

    [Fact]
    public void DeriveTestNamespace_PrefixPattern_PrependsTests()
    {
        var result = FrameworkTemplates.DeriveTestNamespace("App.Services", "Tests.{Namespace}");
        Assert.Equal("Tests.App.Services", result);
    }
}
