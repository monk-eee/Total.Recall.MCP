using Total.Recall.Models;

namespace Total.Recall.Tools;

/// <summary>
/// Framework-specific code generation templates for test scaffolding.
/// Supports xUnit, NUnit, and MSTest for test attributes/assertions,
/// and Moq, NSubstitute, and FakeItEasy for mocking.
/// </summary>
internal static class FrameworkTemplates
{
    // ── Test Framework Templates ──

    /// <summary>
    /// Returns the using statement for the configured test framework.
    /// </summary>
    public static string GetTestUsing(TestFramework framework) => framework switch
    {
        TestFramework.NUnit => "using NUnit.Framework;",
        TestFramework.MSTest => "using Microsoft.VisualStudio.TestTools.UnitTesting;",
        _ => "using Xunit;"
    };

    /// <summary>
    /// Returns the test method attribute (e.g., [Fact], [Test], [TestMethod]).
    /// </summary>
    public static string GetTestAttribute(TestFramework framework) => framework switch
    {
        TestFramework.NUnit => "[Test]",
        TestFramework.MSTest => "[TestMethod]",
        _ => "[Fact]"
    };

    /// <summary>
    /// Returns the test class attribute if required by the framework.
    /// NUnit and MSTest require class-level attributes; xUnit does not.
    /// </summary>
    public static string? GetClassAttribute(TestFramework framework) => framework switch
    {
        TestFramework.NUnit => "[TestFixture]",
        TestFramework.MSTest => "[TestClass]",
        _ => null
    };

    /// <summary>
    /// Returns a "not null" assertion for the configured framework.
    /// </summary>
    public static string GetAssertNotNull(TestFramework framework, string expression) => framework switch
    {
        TestFramework.NUnit => $"Assert.That({expression}, Is.Not.Null);",
        TestFramework.MSTest => $"Assert.IsNotNull({expression});",
        _ => $"Assert.NotNull({expression});"
    };

    /// <summary>
    /// Returns an Assert.Throws pattern for the configured framework.
    /// </summary>
    public static string GetAssertThrows(TestFramework framework, string exceptionType, string lambda) => framework switch
    {
        TestFramework.NUnit => $"Assert.Throws<{exceptionType}>(() =>\n            {lambda});",
        TestFramework.MSTest => $"Assert.ThrowsException<{exceptionType}>(() =>\n            {lambda});",
        _ => $"Assert.Throws<{exceptionType}>(() =>\n            {lambda});"
    };

    /// <summary>
    /// Returns the setup method attribute/convention hint for the configured framework.
    /// xUnit uses constructor; NUnit uses [SetUp]; MSTest uses [TestInitialize].
    /// </summary>
    public static string? GetSetupAttribute(TestFramework framework) => framework switch
    {
        TestFramework.NUnit => "[SetUp]",
        TestFramework.MSTest => "[TestInitialize]",
        _ => null // xUnit uses constructor
    };

    /// <summary>
    /// Returns whether the framework uses constructor for test setup.
    /// If false, uses a Setup method with an attribute.
    /// </summary>
    public static bool UsesConstructorSetup(TestFramework framework) => framework == TestFramework.XUnit;

    // ── Mock Library Templates ──

    /// <summary>
    /// Returns the using statement for the configured mock library.
    /// </summary>
    public static string GetMockUsing(MockLibrary library) => library switch
    {
        MockLibrary.NSubstitute => "using NSubstitute;",
        MockLibrary.FakeItEasy => "using FakeItEasy;",
        _ => "using Moq;"
    };

    /// <summary>
    /// Returns the mock field declaration for an interface type.
    /// </summary>
    public static string GetMockFieldDeclaration(MockLibrary library, string interfaceType, string fieldName) => library switch
    {
        MockLibrary.NSubstitute => $"private readonly {interfaceType} {fieldName};",
        MockLibrary.FakeItEasy => $"private readonly {interfaceType} {fieldName};",
        _ => $"private readonly Mock<{interfaceType}> {fieldName};"
    };

    /// <summary>
    /// Returns the mock initialization expression.
    /// </summary>
    public static string GetMockInitialization(MockLibrary library, string interfaceType, string fieldName) => library switch
    {
        MockLibrary.NSubstitute => $"{fieldName} = Substitute.For<{interfaceType}>();",
        MockLibrary.FakeItEasy => $"{fieldName} = A.Fake<{interfaceType}>();",
        _ => $"{fieldName} = new Mock<{interfaceType}>();"
    };

    /// <summary>
    /// Returns the expression to pass a mock to a constructor parameter.
    /// Moq requires .Object; NSubstitute and FakeItEasy use the field directly.
    /// </summary>
    public static string GetMockObjectExpression(MockLibrary library, string fieldName) => library switch
    {
        MockLibrary.NSubstitute => fieldName,
        MockLibrary.FakeItEasy => fieldName,
        _ => $"{fieldName}.Object"
    };

    /// <summary>
    /// Returns a mock.Verify-equivalent hint for anti-pattern warnings.
    /// </summary>
    public static string GetVerifyHint(MockLibrary library) => library switch
    {
        MockLibrary.NSubstitute => "Received()",
        MockLibrary.FakeItEasy => "A.CallTo(...).MustHaveHappened()",
        _ => "mock.Verify"
    };

    /// <summary>
    /// Derive test namespace from production namespace using the configured pattern.
    /// Supports {Namespace}, {RootNamespace}, and {Rest} placeholders.
    /// </summary>
    public static string DeriveTestNamespace(string? productionNamespace, string pattern)
    {
        if (string.IsNullOrEmpty(productionNamespace))
            return "Tests";

        var ns = productionNamespace;

        // Simple case: pattern uses {Namespace} directly
        if (pattern.Contains("{Namespace}"))
            return pattern.Replace("{Namespace}", ns);

        // Split pattern: {RootNamespace} and {Rest}
        if (pattern.Contains("{RootNamespace}") || pattern.Contains("{Rest}"))
        {
            var dotIndex = ns.IndexOf('.');
            string rootNs, rest;
            if (dotIndex >= 0)
            {
                rootNs = ns[..dotIndex];
                rest = ns[(dotIndex + 1)..];
            }
            else
            {
                rootNs = ns;
                rest = string.Empty;
            }

            var result = pattern
                .Replace("{RootNamespace}", rootNs)
                .Replace("{Rest}", rest);

            // Clean up double dots from empty {Rest}
            while (result.Contains(".."))
                result = result.Replace("..", ".");
            return result.Trim('.');
        }

        // Fallback: just append .Tests
        return $"{ns}.Tests";
    }
}
