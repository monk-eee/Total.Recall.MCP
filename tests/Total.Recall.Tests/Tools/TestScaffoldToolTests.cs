using System.Text.Json;
using Total.Recall.Infrastructure;
using Total.Recall.Models;
using Total.Recall.Tools;

namespace Total.Recall.Tests.Tools;

[Collection("ToolTests")]
public sealed class TestScaffoldToolTests : ToolTestBase
{

    // ── No data / not found ──

    [Fact]
    public void GenerateTestScaffold_NoTypeRegistry_ReturnsNotFoundMessage()
    {
        var result = TestScaffoldTool.GenerateTestScaffold("Anything");

        Assert.Contains("not found in type registry", result);
    }

    [Fact]
    public void GenerateTestScaffold_TypeNotFound_ReturnsNotFoundMessage()
    {
        SeedTypeRegistry(new TypeRecord { Name = "Foo", Namespace = "App" });

        var result = TestScaffoldTool.GenerateTestScaffold("Nonexistent");

        Assert.Contains("not found in type registry", result);
    }

    // ── Type resolution strategies ──

    [Fact]
    public void GenerateTestScaffold_ExactMatch_ReturnsScaffold()
    {
        SeedTypeRegistry(new TypeRecord { Name = "Calculator", Namespace = "App" });

        var result = TestScaffoldTool.GenerateTestScaffold("Calculator");

        Assert.Contains("Calculator", result);
        Assert.Contains("scaffold", result);
    }

    [Fact]
    public void GenerateTestScaffold_CaseInsensitiveMatch_ReturnsScaffold()
    {
        SeedTypeRegistry(new TypeRecord { Name = "Calculator", Namespace = "App" });

        var result = TestScaffoldTool.GenerateTestScaffold("calculator");

        Assert.Contains("Calculator", result);
        Assert.Contains("scaffold", result);
    }

    [Fact]
    public void GenerateTestScaffold_ContainsFallback_ReturnsScaffold()
    {
        SeedTypeRegistry(new TypeRecord { Name = "StringCalculator", Namespace = "App" });

        var result = TestScaffoldTool.GenerateTestScaffold("Calc");

        Assert.Contains("StringCalculator", result);
    }

    // ── Parameterless constructor ──

    [Fact]
    public void GenerateTestScaffold_ParameterlessCtor_GeneratesBasicScaffold()
    {
        SeedTypeRegistry(new TypeRecord
        {
            Name = "SimpleClass",
            Namespace = "App.Services",
            Constructors = [new ConstructorRecord { Params = [] }]
        });

        var result = TestScaffoldTool.GenerateTestScaffold("SimpleClass");
        var doc = JsonDocument.Parse(result);
        var scaffold = doc.RootElement.GetProperty("scaffold").GetString()!;

        Assert.Contains("using Xunit;", scaffold);
        Assert.Contains("using Moq;", scaffold);
        Assert.Contains("using App.Services;", scaffold);
        Assert.Contains("namespace App.Services.Tests;", scaffold);
        Assert.Contains("public class SimpleClassTests", scaffold);
        Assert.Contains("_sut = new SimpleClass();", scaffold);
        Assert.Contains("Ctor_ShouldCreateInstance", scaffold);
    }

    // ── Interface parameters → mock fields ──

    [Fact]
    public void GenerateTestScaffold_InterfaceParams_CreatesMockFields()
    {
        SeedTypeRegistry(new TypeRecord
        {
            Name = "MyService",
            Namespace = "App",
            Constructors = [new ConstructorRecord { Params = ["ILogger _logger", "IRepository _repo"] }]
        });

        var result = TestScaffoldTool.GenerateTestScaffold("MyService");
        var doc = JsonDocument.Parse(result);
        var scaffold = doc.RootElement.GetProperty("scaffold").GetString()!;
        var mockCount = doc.RootElement.GetProperty("mockCount").GetInt32();

        Assert.Equal(2, mockCount);
        Assert.Contains("Mock<ILogger> _mockLogger", scaffold);
        Assert.Contains("Mock<IRepository> _mockRepository", scaffold);
        Assert.Contains("_mockLogger = new Mock<ILogger>()", scaffold);
        Assert.Contains("_mockRepository = new Mock<IRepository>()", scaffold);
        Assert.Contains("_mockLogger.Object", scaffold);
        Assert.Contains("_mockRepository.Object", scaffold);
    }

    // ── Concrete parameters → default values ──

    [Fact]
    public void GenerateTestScaffold_ConcreteParams_CreatesFieldsWithDefaults()
    {
        SeedTypeRegistry(new TypeRecord
        {
            Name = "Widget",
            Namespace = "App",
            Constructors = [new ConstructorRecord { Params = ["string name", "int count", "bool enabled"] }]
        });

        var result = TestScaffoldTool.GenerateTestScaffold("Widget");
        var doc = JsonDocument.Parse(result);
        var scaffold = doc.RootElement.GetProperty("scaffold").GetString()!;

        Assert.Contains("_name = \"test-value\"", scaffold);
        Assert.Contains("_count = 0", scaffold);
        Assert.Contains("_enabled = false", scaffold);
    }

    // ── Mixed interface + concrete params ──

    [Fact]
    public void GenerateTestScaffold_MixedParams_HandlesBothTypes()
    {
        SeedTypeRegistry(new TypeRecord
        {
            Name = "Processor",
            Namespace = "App",
            Constructors = [new ConstructorRecord { Params = ["ILogger _logger", "string connectionString"] }]
        });

        var result = TestScaffoldTool.GenerateTestScaffold("Processor");
        var doc = JsonDocument.Parse(result);
        var scaffold = doc.RootElement.GetProperty("scaffold").GetString()!;

        Assert.Contains("Mock<ILogger>", scaffold);
        Assert.Contains("_connectionString = \"test-value\"", scaffold);
    }

    // ── Multi-param ctor (>3) → multiline constructor ──

    [Fact]
    public void GenerateTestScaffold_ManyParams_UsesMultilineConstructor()
    {
        SeedTypeRegistry(new TypeRecord
        {
            Name = "BigService",
            Namespace = "App",
            Constructors = [new ConstructorRecord
            {
                Params = ["ILogger _logger", "IRepo _repo", "ICache _cache", "IConfig _config"]
            }]
        });

        var result = TestScaffoldTool.GenerateTestScaffold("BigService");
        var doc = JsonDocument.Parse(result);
        var scaffold = doc.RootElement.GetProperty("scaffold").GetString()!;

        // With >3 params, should use multi-line format
        Assert.Contains("_sut = new BigService(", scaffold);
        // Each param on its own line
        Assert.Contains("_mockLogger.Object,", scaffold);
        Assert.Contains("_mockConfig.Object);", scaffold);
    }

    // ── Inline ctor (≤3 params) ──

    [Fact]
    public void GenerateTestScaffold_FewParams_UsesInlineConstructor()
    {
        SeedTypeRegistry(new TypeRecord
        {
            Name = "SmallService",
            Namespace = "App",
            Constructors = [new ConstructorRecord { Params = ["ILogger _logger", "IRepo _repo"] }]
        });

        var result = TestScaffoldTool.GenerateTestScaffold("SmallService");
        var doc = JsonDocument.Parse(result);
        var scaffold = doc.RootElement.GetProperty("scaffold").GetString()!;

        // With ≤3 params, single line
        Assert.Contains("_sut = new SmallService(_mockLogger.Object, _mockRepo.Object);", scaffold);
    }

    // ── Gotchas → comment block at top ──

    [Fact]
    public void GenerateTestScaffold_WithGotchas_IncludesGotchaComments()
    {
        SeedTypeRegistry(new TypeRecord { Name = "TrickyType", Namespace = "App" });
        SeedGotchas(
            new Gotcha { Type = "TrickyType", Category = "constructor", Description = "Watch out for null", Date = "2025-01-01" },
            new Gotcha { Type = "TrickyType", Category = "enum", Description = "Hidden enum value", Date = "2025-01-02" }
        );

        var result = TestScaffoldTool.GenerateTestScaffold("TrickyType");
        var doc = JsonDocument.Parse(result);
        var scaffold = doc.RootElement.GetProperty("scaffold").GetString()!;
        var gotchaCount = doc.RootElement.GetProperty("gotchaCount").GetInt32();

        Assert.Equal(2, gotchaCount);
        Assert.Contains("KNOWN GOTCHAS", scaffold);
        Assert.Contains("Watch out for null", scaffold);
        Assert.Contains("Hidden enum value", scaffold);
        Assert.Contains("[constructor]", scaffold);
        Assert.Contains("[enum]", scaffold);
    }

    // ── Coverage gaps → uncovered method stubs ──

    [Fact]
    public void GenerateTestScaffold_WithCoverageGaps_GeneratesMethodStubs()
    {
        SeedTypeRegistry(new TypeRecord { Name = "Parser", Namespace = "App" });
        SeedCoverageGaps(new CoverageGap
        {
            Class = "Parser",
            UncoveredMethods =
            [
                new UncoveredMethod { Name = "Parse", StartLine = 10, EndLine = 30, UncoveredLines = 15 },
                new UncoveredMethod { Name = "Validate", StartLine = 35, EndLine = 50, UncoveredLines = 8 }
            ]
        });

        var result = TestScaffoldTool.GenerateTestScaffold("Parser");
        var doc = JsonDocument.Parse(result);
        var scaffold = doc.RootElement.GetProperty("scaffold").GetString()!;
        var methodCount = doc.RootElement.GetProperty("uncoveredMethodCount").GetInt32();

        Assert.Equal(2, methodCount);
        Assert.Contains("[Fact]", scaffold);
        Assert.Contains("Parse_ShouldWork", scaffold);
        Assert.Contains("Validate_ShouldWork", scaffold);
        Assert.Contains("// Arrange", scaffold);
        Assert.Contains("// Act", scaffold);
        Assert.Contains("// Assert", scaffold);
    }

    // ── Mock recipes → recipe comments injected ──

    [Fact]
    public void GenerateTestScaffold_WithMockRecipes_InjectsRecipeComments()
    {
        SeedTypeRegistry(new TypeRecord
        {
            Name = "AuditService",
            Namespace = "App",
            Constructors = [new ConstructorRecord { Params = ["ILogger _logger"] }]
        });
        SeedMockRecipes(new MockRecipe
        {
            Interface = "ILogger",
            Namespace = "Microsoft.Extensions.Logging",
            RequiredUsings = ["using Microsoft.Extensions.Logging;"],
            Recipe = "var mock = new Mock<ILogger>();\nmock.Setup(x => x.IsEnabled(It.IsAny<LogLevel>())).Returns(true);"
        });

        var result = TestScaffoldTool.GenerateTestScaffold("AuditService");
        var doc = JsonDocument.Parse(result);
        var scaffold = doc.RootElement.GetProperty("scaffold").GetString()!;

        Assert.Contains("using Microsoft.Extensions.Logging;", scaffold);
        Assert.Contains("Mock recipe for ILogger", scaffold);
        Assert.Contains("mock.Setup", scaffold);
    }

    // ── No coverage gaps and no ctor → basic ctor test ──

    [Fact]
    public void GenerateTestScaffold_NoCoverageGaps_GeneratesCtorTest()
    {
        SeedTypeRegistry(new TypeRecord { Name = "Plain", Namespace = "App" });

        var result = TestScaffoldTool.GenerateTestScaffold("Plain");
        var doc = JsonDocument.Parse(result);
        var scaffold = doc.RootElement.GetProperty("scaffold").GetString()!;

        Assert.Contains("Ctor_ShouldCreateInstance", scaffold);
        Assert.Contains("Assert.NotNull(_sut)", scaffold);
    }

    // ── Picks largest ctor ──

    [Fact]
    public void GenerateTestScaffold_MultipleCtors_PicksLargest()
    {
        SeedTypeRegistry(new TypeRecord
        {
            Name = "MultiCtor",
            Namespace = "App",
            Constructors =
            [
                new ConstructorRecord { Params = [] },
                new ConstructorRecord { Params = ["ILogger _logger", "IRepo _repo"] },
                new ConstructorRecord { Params = ["ILogger _logger"] }
            ]
        });

        var result = TestScaffoldTool.GenerateTestScaffold("MultiCtor");
        var doc = JsonDocument.Parse(result);
        var scaffold = doc.RootElement.GetProperty("scaffold").GetString()!;

        // Should pick the 2-param ctor
        Assert.Contains("_mockLogger", scaffold);
        Assert.Contains("_mockRepo", scaffold);
    }

    // ── Default value coverage for various types ──

    [Theory]
    [InlineData("long count", "0L")]
    [InlineData("double rate", "0.0")]
    [InlineData("float score", "0f")]
    [InlineData("decimal price", "0m")]
    [InlineData("byte val", "(byte)0")]
    public void GenerateTestScaffold_VariousConcreteTypes_ProducesCorrectDefaults(string param, string expectedDefault)
    {
        SeedTypeRegistry(new TypeRecord
        {
            Name = "TypedClass",
            Namespace = "App",
            Constructors = [new ConstructorRecord { Params = [param] }]
        });

        var result = TestScaffoldTool.GenerateTestScaffold("TypedClass");
        var doc = JsonDocument.Parse(result);
        var scaffold = doc.RootElement.GetProperty("scaffold").GetString()!;

        Assert.Contains(expectedDefault, scaffold);
    }

    [Fact]
    public void GenerateTestScaffold_ListParam_ProducesNewList()
    {
        SeedTypeRegistry(new TypeRecord
        {
            Name = "ListHolder",
            Namespace = "App",
            Constructors = [new ConstructorRecord { Params = ["List<string> items"] }]
        });

        var result = TestScaffoldTool.GenerateTestScaffold("ListHolder");
        var doc = JsonDocument.Parse(result);
        var scaffold = doc.RootElement.GetProperty("scaffold").GetString()!;

        Assert.Contains("new List<string>()", scaffold);
    }

    [Fact]
    public void GenerateTestScaffold_DictionaryParam_ProducesNewDictionary()
    {
        SeedTypeRegistry(new TypeRecord
        {
            Name = "MapHolder",
            Namespace = "App",
            Constructors = [new ConstructorRecord { Params = ["Dictionary<string,int> lookup"] }]
        });

        var result = TestScaffoldTool.GenerateTestScaffold("MapHolder");
        var doc = JsonDocument.Parse(result);
        var scaffold = doc.RootElement.GetProperty("scaffold").GetString()!;

        Assert.Contains("new Dictionary<string,int>()", scaffold);
    }

    // ── SanitizeMethodName via coverage method stubs ──

    [Fact]
    public void GenerateTestScaffold_PropertyAccessors_SanitizedCorrectly()
    {
        SeedTypeRegistry(new TypeRecord { Name = "PropClass", Namespace = "App" });
        SeedCoverageGaps(new CoverageGap
        {
            Class = "PropClass",
            UncoveredMethods =
            [
                new UncoveredMethod { Name = "get_Value", StartLine = 1, EndLine = 5, UncoveredLines = 3 },
                new UncoveredMethod { Name = "set_Value", StartLine = 6, EndLine = 10, UncoveredLines = 3 }
            ]
        });

        var result = TestScaffoldTool.GenerateTestScaffold("PropClass");
        var doc = JsonDocument.Parse(result);
        var scaffold = doc.RootElement.GetProperty("scaffold").GetString()!;

        Assert.Contains("GetValue_ShouldWork", scaffold);
        Assert.Contains("SetValue_ShouldWork", scaffold);
    }

    // ── Metadata in JSON result ──

    [Fact]
    public void GenerateTestScaffold_ReturnsCorrectMetadata()
    {
        SeedTypeRegistry(new TypeRecord
        {
            Name = "MetaClass",
            Namespace = "Server.Common",
            Constructors = [new ConstructorRecord { Params = ["ILogger _logger"] }]
        });
        SeedGotchas(new Gotcha { Type = "MetaClass", Category = "bug", Description = "test", Date = "2025-01-01" });
        SeedCoverageGaps(new CoverageGap
        {
            Class = "MetaClass",
            UncoveredMethods = [new UncoveredMethod { Name = "DoWork", StartLine = 1, EndLine = 10, UncoveredLines = 5 }]
        });

        var result = TestScaffoldTool.GenerateTestScaffold("MetaClass");
        var doc = JsonDocument.Parse(result);

        Assert.Equal("MetaClass", doc.RootElement.GetProperty("className").GetString());
        Assert.Equal("Server.Common.Tests", doc.RootElement.GetProperty("namespace").GetString());
        Assert.Equal("MetaClassTests.cs", doc.RootElement.GetProperty("suggestedFileName").GetString());
        Assert.Equal(1, doc.RootElement.GetProperty("mockCount").GetInt32());
        Assert.Equal(1, doc.RootElement.GetProperty("uncoveredMethodCount").GetInt32());
        Assert.Equal(1, doc.RootElement.GetProperty("gotchaCount").GetInt32());
    }

    // ── Interface namespace lookup via type index ──

    [Fact]
    public void GenerateTestScaffold_InterfaceInRegistry_AddsItsNamespace()
    {
        SeedTypeRegistry(
            new TypeRecord { Name = "Worker", Namespace = "App", Constructors = [new ConstructorRecord { Params = ["IJobService _svc"] }] },
            new TypeRecord { Name = "IJobService", Namespace = "App.Contracts", IsInterface = true }
        );

        var result = TestScaffoldTool.GenerateTestScaffold("Worker");
        var doc = JsonDocument.Parse(result);
        var scaffold = doc.RootElement.GetProperty("scaffold").GetString()!;

        Assert.Contains("using App.Contracts;", scaffold);
    }

    // ── No namespace → fallback ──

    [Fact]
    public void GenerateTestScaffold_EmptyNamespace_GeneratesEmptyNamespace()
    {
        SeedTypeRegistry(new TypeRecord { Name = "Orphan", Namespace = "" });

        var result = TestScaffoldTool.GenerateTestScaffold("Orphan");
        var doc = JsonDocument.Parse(result);
        var scaffold = doc.RootElement.GetProperty("scaffold").GetString()!;

        // Empty namespace produces "namespace ;" — the code uses ?? "Tests" only for null
        Assert.Contains("namespace", scaffold);
        Assert.Contains("OrphanTests", scaffold);
    }

    // ── Enum/Type suffix param → default() ──

    [Fact]
    public void GenerateTestScaffold_EnumSuffixParam_UsesDefault()
    {
        SeedTypeRegistry(new TypeRecord
        {
            Name = "StatusHolder",
            Namespace = "App",
            Constructors = [new ConstructorRecord { Params = ["StatusEnum status"] }]
        });

        var result = TestScaffoldTool.GenerateTestScaffold("StatusHolder");
        var doc = JsonDocument.Parse(result);
        var scaffold = doc.RootElement.GetProperty("scaffold").GetString()!;

        Assert.Contains("default(StatusEnum)", scaffold);
    }

    // ── Unknown type param → default(T)! ──

    [Fact]
    public void GenerateTestScaffold_UnknownTypeParam_UsesDefaultBangFallback()
    {
        SeedTypeRegistry(new TypeRecord
        {
            Name = "CustomHolder",
            Namespace = "App",
            Constructors = [new ConstructorRecord { Params = ["SomeWeirdThing thing"] }]
        });

        var result = TestScaffoldTool.GenerateTestScaffold("CustomHolder");
        var doc = JsonDocument.Parse(result);
        var scaffold = doc.RootElement.GetProperty("scaffold").GetString()!;

        Assert.Contains("default(SomeWeirdThing)!", scaffold);
    }

    // ── ParseParam edge case: no space → type only ──

    [Fact]
    public void GenerateTestScaffold_ParamWithNoSpace_HandlesGracefully()
    {
        SeedTypeRegistry(new TypeRecord
        {
            Name = "EdgeCase",
            Namespace = "App",
            Constructors = [new ConstructorRecord { Params = ["ILogger"] }]
        });

        // Should not crash — will treat "ILogger" as type with default name "param"
        var result = TestScaffoldTool.GenerateTestScaffold("EdgeCase");
        var doc = JsonDocument.Parse(result);
        var scaffold = doc.RootElement.GetProperty("scaffold").GetString()!;

        Assert.Contains("Mock<ILogger>", scaffold);
    }

    // ── No constructors → parameterless fallback ──

    [Fact]
    public void GenerateTestScaffold_NoConstructors_UsesParameterlessCtor()
    {
        SeedTypeRegistry(new TypeRecord
        {
            Name = "NoCtor",
            Namespace = "App",
            Constructors = []
        });

        var result = TestScaffoldTool.GenerateTestScaffold("NoCtor");
        var doc = JsonDocument.Parse(result);
        var scaffold = doc.RootElement.GetProperty("scaffold").GetString()!;

        Assert.Contains("_sut = new NoCtor();", scaffold);
    }

    // ── Async method support ──

    [Fact]
    public void GenerateTestScaffold_AsyncMethod_GeneratesAsyncTestStub()
    {
        SeedTypeRegistry(new TypeRecord { Name = "AsyncService", Namespace = "App" });
        SeedCoverageGaps(new CoverageGap
        {
            Class = "AsyncService",
            UncoveredMethods =
            [
                new UncoveredMethod { Name = "ProcessAsync", StartLine = 10, EndLine = 30, UncoveredLines = 15 }
            ]
        });

        var result = TestScaffoldTool.GenerateTestScaffold("AsyncService");
        var doc = JsonDocument.Parse(result);
        var scaffold = doc.RootElement.GetProperty("scaffold").GetString()!;

        Assert.Contains("public async Task", scaffold);
        Assert.Contains("await _sut.ProcessAsync", scaffold);
        Assert.Contains("using System.Threading.Tasks;", scaffold);
    }

    [Fact]
    public void GenerateTestScaffold_MixedSyncAndAsync_GeneratesBothStyles()
    {
        SeedTypeRegistry(new TypeRecord { Name = "MixedService", Namespace = "App" });
        SeedCoverageGaps(new CoverageGap
        {
            Class = "MixedService",
            UncoveredMethods =
            [
                new UncoveredMethod { Name = "SyncWork", StartLine = 5, EndLine = 15, UncoveredLines = 8 },
                new UncoveredMethod { Name = "DoWorkAsync", StartLine = 20, EndLine = 40, UncoveredLines = 12 }
            ]
        });

        var result = TestScaffoldTool.GenerateTestScaffold("MixedService");
        var doc = JsonDocument.Parse(result);
        var scaffold = doc.RootElement.GetProperty("scaffold").GetString()!;

        Assert.Contains("public void SyncWork_ShouldWork", scaffold);
        Assert.Contains("public async Task DoWork_ShouldWork", scaffold);  // "Async" suffix stripped
        Assert.Contains("// TODO: call _sut.SyncWork", scaffold);
        Assert.Contains("// TODO: await _sut.DoWorkAsync", scaffold);
    }

    [Fact]
    public void GenerateTestScaffold_AsyncMethodCount_InMetadata()
    {
        SeedTypeRegistry(new TypeRecord { Name = "AsyncMeta", Namespace = "App" });
        SeedCoverageGaps(new CoverageGap
        {
            Class = "AsyncMeta",
            UncoveredMethods =
            [
                new UncoveredMethod { Name = "LoadAsync", StartLine = 1, EndLine = 10, UncoveredLines = 5 },
                new UncoveredMethod { Name = "SaveAsync", StartLine = 11, EndLine = 20, UncoveredLines = 5 },
                new UncoveredMethod { Name = "Reset", StartLine = 21, EndLine = 30, UncoveredLines = 5 }
            ]
        });

        var result = TestScaffoldTool.GenerateTestScaffold("AsyncMeta");
        var doc = JsonDocument.Parse(result);

        Assert.Equal(2, doc.RootElement.GetProperty("asyncMethodCount").GetInt32());
        Assert.Equal(3, doc.RootElement.GetProperty("uncoveredMethodCount").GetInt32());
    }

    // ── IsAsyncMethod unit tests ──

    [Theory]
    [InlineData("ProcessAsync", true)]
    [InlineData("ExecuteAsync", true)]
    [InlineData("DoWork", false)]
    [InlineData("SaveAsync", true)]
    [InlineData("Validate", false)]
    public void IsAsyncMethod_DetectsAsyncByName(string methodName, bool expected)
    {
        Assert.Equal(expected, TestScaffoldTool.IsAsyncMethod(methodName, null));
    }

    // ── Expanded GetDefaultValue ──

    [Theory]
    [InlineData("Guid", "Guid.NewGuid()")]
    [InlineData("DateTime", "DateTime.UtcNow")]
    [InlineData("DateTimeOffset", "DateTimeOffset.UtcNow")]
    [InlineData("TimeSpan", "TimeSpan.FromSeconds(1)")]
    [InlineData("char", "'a'")]
    [InlineData("CancellationToken", "CancellationToken.None")]
    [InlineData("Uri", "new Uri(\"https://example.com\")")]
    [InlineData("Stream", "Stream.Null")]
    [InlineData("object", "new object()")]
    [InlineData("short", "(short)0")]
    [InlineData("uint", "0u")]
    [InlineData("ulong", "0UL")]
    public void GetDefaultValue_ExpandedTypes_ReturnsCorrectDefaults(string typeName, string expected)
    {
        Assert.Equal(expected, TestScaffoldTool.GetDefaultValue(typeName));
    }

    [Theory]
    [InlineData("int?", "null")]
    [InlineData("bool?", "null")]
    [InlineData("Guid?", "null")]
    [InlineData("DateTime?", "null")]
    [InlineData("Nullable<int>", "null")]
    public void GetDefaultValue_NullableTypes_ReturnsNull(string typeName, string expected)
    {
        Assert.Equal(expected, TestScaffoldTool.GetDefaultValue(typeName));
    }

    [Fact]
    public void GetDefaultValue_NullableString_ReturnsTestValue()
    {
        // string? should still return "test-value" since string can already be null
        Assert.Equal("\"test-value\"", TestScaffoldTool.GetDefaultValue("string?"));
    }

    [Theory]
    [InlineData("string[]", "Array.Empty<string>()")]
    [InlineData("int[]", "Array.Empty<int>()")]
    public void GetDefaultValue_Arrays_ReturnsArrayEmpty(string typeName, string expected)
    {
        Assert.Equal(expected, TestScaffoldTool.GetDefaultValue(typeName));
    }

    [Theory]
    [InlineData("IEnumerable<string>", "Array.Empty<string>()")]
    [InlineData("IReadOnlyList<int>", "Array.Empty<int>()")]
    [InlineData("IList<string>", "new List<string>()")]
    [InlineData("ICollection<int>", "new List<int>()")]
    [InlineData("IDictionary<string,int>", "new Dictionary<string,int>()")]
    public void GetDefaultValue_CollectionInterfaces_ReturnsCorrectDefaults(string typeName, string expected)
    {
        Assert.Equal(expected, TestScaffoldTool.GetDefaultValue(typeName));
    }

    // ── Null-guard constructor tests ──

    [Fact]
    public void GenerateTestScaffold_InterfaceParams_GeneratesNullGuardTests()
    {
        SeedTypeRegistry(new TypeRecord
        {
            Name = "GuardedService",
            Namespace = "App",
            Constructors = [new ConstructorRecord { Params = ["ILogger _logger", "IRepository _repo"] }]
        });

        var result = TestScaffoldTool.GenerateTestScaffold("GuardedService");
        var doc = JsonDocument.Parse(result);
        var scaffold = doc.RootElement.GetProperty("scaffold").GetString()!;

        Assert.Contains("Constructor null-guard tests", scaffold);
        Assert.Contains("Ctor_NullLogger_ThrowsArgumentNullException", scaffold);
        Assert.Contains("Ctor_NullRepository_ThrowsArgumentNullException", scaffold);
        Assert.Contains("Assert.Throws<ArgumentNullException>", scaffold);
        Assert.Contains("null!", scaffold);
    }

    [Fact]
    public void GenerateTestScaffold_NullGuardCount_InMetadata()
    {
        SeedTypeRegistry(new TypeRecord
        {
            Name = "ThreeIface",
            Namespace = "App",
            Constructors = [new ConstructorRecord { Params = ["ILogger _logger", "IRepo _repo", "ICache _cache"] }]
        });

        var result = TestScaffoldTool.GenerateTestScaffold("ThreeIface");
        var doc = JsonDocument.Parse(result);

        Assert.Equal(3, doc.RootElement.GetProperty("nullGuardTestCount").GetInt32());
    }

    [Fact]
    public void GenerateTestScaffold_NoInterfaceParams_NoNullGuardTests()
    {
        SeedTypeRegistry(new TypeRecord
        {
            Name = "PureValue",
            Namespace = "App",
            Constructors = [new ConstructorRecord { Params = ["string name", "int count"] }]
        });

        var result = TestScaffoldTool.GenerateTestScaffold("PureValue");
        var doc = JsonDocument.Parse(result);
        var scaffold = doc.RootElement.GetProperty("scaffold").GetString()!;

        Assert.DoesNotContain("null-guard", scaffold);
        Assert.Equal(0, doc.RootElement.GetProperty("nullGuardTestCount").GetInt32());
    }

    [Fact]
    public void GenerateTestScaffold_NullGuard_SetsCorrectNullArg()
    {
        SeedTypeRegistry(new TypeRecord
        {
            Name = "TwoMock",
            Namespace = "App",
            Constructors = [new ConstructorRecord { Params = ["ILogger _logger", "IRepo _repo"] }]
        });

        var result = TestScaffoldTool.GenerateTestScaffold("TwoMock");
        var doc = JsonDocument.Parse(result);
        var scaffold = doc.RootElement.GetProperty("scaffold").GetString()!;

        // First null-guard test: null for Logger, valid for Repo
        Assert.Contains("new TwoMock(null!, _mockRepo.Object)", scaffold);
        // Second null-guard test: valid for Logger, null for Repo
        Assert.Contains("new TwoMock(_mockLogger.Object, null!)", scaffold);
    }

    // ── Smart assertion hints ──

    [Theory]
    [InlineData("Validate", "Assert.True/False on validation result")]
    [InlineData("GetUser", "Assert.NotNull on result")]
    [InlineData("IsActive", "Assert.True for positive case")]
    [InlineData("ParseToken", "Assert.Equal on expected output")]
    [InlineData("CreateItem", "Assert.NotNull on created object")]
    [InlineData("DeleteEntry", "Verify item is removed")]
    [InlineData("AddItem", "Verify state change occurred")]
    [InlineData("InitializeAsync", "Verify initialization side-effects")]
    [InlineData("Dispose", "Verify resources released")]
    [InlineData("HandleRequest", "Verify side-effects via mock.Verify")]
    [InlineData("TryParse", "Assert.True for success case")]
    [InlineData("FormatOutput", "Assert.Equal on expected string")]
    [InlineData("CheckPermission", "Assert.True/False on validation result")]
    [InlineData("CountItems", "Assert.Equal on expected count")]
    [InlineData("EnsureValid", "Assert.Throws for invalid input")]
    [InlineData("OnClick", "Verify event handler side-effects")]
    [InlineData("UnknownMethod", "TODO: verify behavior")]
    public void GetAssertionHint_ReturnsPatternSpecificHint(string methodName, string expectedContains)
    {
        var hint = TestScaffoldTool.GetAssertionHint(methodName);

        Assert.Contains(expectedContains, hint);
    }

    [Fact]
    public void GenerateTestScaffold_CoverageGap_HasSmartAssertionHints()
    {
        SeedTypeRegistry(new TypeRecord { Name = "Validator", Namespace = "App" });
        SeedCoverageGaps(new CoverageGap
        {
            Class = "Validator",
            UncoveredMethods =
            [
                new UncoveredMethod { Name = "Validate", StartLine = 10, EndLine = 30, UncoveredLines = 15 }
            ]
        });

        var result = TestScaffoldTool.GenerateTestScaffold("Validator");
        var doc = JsonDocument.Parse(result);
        var scaffold = doc.RootElement.GetProperty("scaffold").GetString()!;

        // Should have smart hint instead of generic "TODO: verify behavior"
        Assert.Contains("Assert.True/False on validation result", scaffold);
        Assert.DoesNotContain("TODO: verify behavior (lines", scaffold);
    }

    // ── Edge case stubs ──

    [Fact]
    public void GenerateTestScaffold_StringMethodName_GeneratesEdgeCaseStubs()
    {
        SeedTypeRegistry(new TypeRecord { Name = "NameParser", Namespace = "App" });
        SeedCoverageGaps(new CoverageGap
        {
            Class = "NameParser",
            UncoveredMethods =
            [
                new UncoveredMethod { Name = "ParseName", StartLine = 5, EndLine = 20, UncoveredLines = 10 }
            ]
        });

        var result = TestScaffoldTool.GenerateTestScaffold("NameParser");
        var doc = JsonDocument.Parse(result);
        var scaffold = doc.RootElement.GetProperty("scaffold").GetString()!;

        Assert.Contains("_NullInput_ShouldThrowOrHandle", scaffold);
        Assert.Contains("_EmptyInput_ShouldHandle", scaffold);
        Assert.Contains("Edge case: pass null string", scaffold);
        Assert.Contains("Edge case: pass empty string", scaffold);
    }

    [Fact]
    public void GenerateTestScaffold_CollectionMethodName_GeneratesEdgeCaseStubs()
    {
        SeedTypeRegistry(new TypeRecord { Name = "BatchProcessor", Namespace = "App" });
        SeedCoverageGaps(new CoverageGap
        {
            Class = "BatchProcessor",
            UncoveredMethods =
            [
                new UncoveredMethod { Name = "ProcessBatchItems", StartLine = 5, EndLine = 20, UncoveredLines = 10 }
            ]
        });

        var result = TestScaffoldTool.GenerateTestScaffold("BatchProcessor");
        var doc = JsonDocument.Parse(result);
        var scaffold = doc.RootElement.GetProperty("scaffold").GetString()!;

        Assert.Contains("_EmptyCollection_ShouldHandle", scaffold);
        Assert.Contains("Edge case: pass empty collection", scaffold);
    }

    [Fact]
    public void GenerateTestScaffold_NumericMethodName_GeneratesEdgeCaseStubs()
    {
        SeedTypeRegistry(new TypeRecord { Name = "Paginator", Namespace = "App" });
        SeedCoverageGaps(new CoverageGap
        {
            Class = "Paginator",
            UncoveredMethods =
            [
                new UncoveredMethod { Name = "GetPageCount", StartLine = 5, EndLine = 20, UncoveredLines = 10 }
            ]
        });

        var result = TestScaffoldTool.GenerateTestScaffold("Paginator");
        var doc = JsonDocument.Parse(result);
        var scaffold = doc.RootElement.GetProperty("scaffold").GetString()!;

        Assert.Contains("_ZeroValue_ShouldHandle", scaffold);
        Assert.Contains("_NegativeValue_ShouldThrowOrHandle", scaffold);
    }

    [Fact]
    public void GenerateTestScaffold_GenericMethodName_NoEdgeCaseStubs()
    {
        SeedTypeRegistry(new TypeRecord { Name = "Worker", Namespace = "App" });
        SeedCoverageGaps(new CoverageGap
        {
            Class = "Worker",
            UncoveredMethods =
            [
                new UncoveredMethod { Name = "Execute", StartLine = 5, EndLine = 20, UncoveredLines = 10 }
            ]
        });

        var result = TestScaffoldTool.GenerateTestScaffold("Worker");
        var doc = JsonDocument.Parse(result);
        var scaffold = doc.RootElement.GetProperty("scaffold").GetString()!;

        // "Execute" doesn't hint at string/collection/numeric params
        Assert.DoesNotContain("_NullInput_", scaffold);
        Assert.DoesNotContain("_EmptyCollection_", scaffold);
        Assert.DoesNotContain("_ZeroValue_", scaffold);
    }

    [Fact]
    public void AppendEdgeCaseStubs_AsyncMethod_GeneratesAsyncEdgeCases()
    {
        var sb = new System.Text.StringBuilder();
        TestScaffoldTool.AppendEdgeCaseStubs(sb, "ParseNameAsync", "ParseName", isAsync: true, null);

        var output = sb.ToString();
        Assert.Contains("public async Task ParseName_NullInput_ShouldThrowOrHandle", output);
        Assert.Contains("await _sut.ParseNameAsync", output);
    }

    // ── Anti-pattern warnings ──

    [Fact]
    public void GenerateTestScaffold_StaticClass_HasStaticWarning()
    {
        SeedTypeRegistry(new TypeRecord { Name = "StaticHelper", Namespace = "App", IsStatic = true });

        var result = TestScaffoldTool.GenerateTestScaffold("StaticHelper");
        var doc = JsonDocument.Parse(result);
        var scaffold = doc.RootElement.GetProperty("scaffold").GetString()!;

        Assert.Contains("STATIC CLASS", scaffold);
        Assert.Contains("Static state may leak", scaffold);
    }

    [Fact]
    public void GenerateTestScaffold_DisposableClass_HasDisposeWarning()
    {
        SeedTypeRegistry(new TypeRecord
        {
            Name = "ResourceHolder",
            Namespace = "App",
            Interfaces = ["IDisposable"]
        });

        var result = TestScaffoldTool.GenerateTestScaffold("ResourceHolder");
        var doc = JsonDocument.Parse(result);
        var scaffold = doc.RootElement.GetProperty("scaffold").GetString()!;

        Assert.Contains("IMPLEMENTS IDisposable", scaffold);
        Assert.Contains("disposed in test cleanup", scaffold);
    }

    [Fact]
    public void GenerateTestScaffold_ManyMocks_HasHighMockWarning()
    {
        SeedTypeRegistry(new TypeRecord
        {
            Name = "MegaService",
            Namespace = "App",
            Constructors = [new ConstructorRecord
            {
                Params = ["IA _a", "IB _b", "IC _c", "ID _d", "IE _e"]
            }]
        });

        var result = TestScaffoldTool.GenerateTestScaffold("MegaService");
        var doc = JsonDocument.Parse(result);
        var scaffold = doc.RootElement.GetProperty("scaffold").GetString()!;

        Assert.Contains("HIGH MOCK COUNT (5)", scaffold);
        Assert.Contains("tight coupling", scaffold);
    }

    [Fact]
    public void GenerateTestScaffold_AbstractClass_HasAbstractWarning()
    {
        SeedTypeRegistry(new TypeRecord { Name = "BaseProcessor", Namespace = "App", IsAbstract = true });

        var result = TestScaffoldTool.GenerateTestScaffold("BaseProcessor");
        var doc = JsonDocument.Parse(result);
        var scaffold = doc.RootElement.GetProperty("scaffold").GetString()!;

        Assert.Contains("ABSTRACT CLASS", scaffold);
        Assert.Contains("test subclass", scaffold);
    }

    [Fact]
    public void GenerateTestScaffold_InternalClass_HasInternalWarning()
    {
        SeedTypeRegistry(new TypeRecord { Name = "InternalHelper", Namespace = "App", IsInternal = true });

        var result = TestScaffoldTool.GenerateTestScaffold("InternalHelper");
        var doc = JsonDocument.Parse(result);
        var scaffold = doc.RootElement.GetProperty("scaffold").GetString()!;

        Assert.Contains("INTERNAL CLASS", scaffold);
        Assert.Contains("InternalsVisibleTo", scaffold);
    }

    [Fact]
    public void GenerateTestScaffold_ConcreteDeps_HasConcreteDepsWarning()
    {
        SeedTypeRegistry(new TypeRecord
        {
            Name = "TightCoupled",
            Namespace = "App",
            Constructors = [new ConstructorRecord { Params = ["MyDatabaseContext db", "ILogger _logger"] }]
        });

        var result = TestScaffoldTool.GenerateTestScaffold("TightCoupled");
        var doc = JsonDocument.Parse(result);
        var scaffold = doc.RootElement.GetProperty("scaffold").GetString()!;

        Assert.Contains("CONCRETE DEPENDENCIES (MyDatabaseContext)", scaffold);
        Assert.Contains("Avoid mocking concrete classes", scaffold);
    }

    [Fact]
    public void GenerateTestScaffold_NoWarnings_NoAntiPatternSection()
    {
        SeedTypeRegistry(new TypeRecord
        {
            Name = "CleanClass",
            Namespace = "App",
            Constructors = [new ConstructorRecord { Params = ["ILogger _logger"] }]
        });

        var result = TestScaffoldTool.GenerateTestScaffold("CleanClass");
        var doc = JsonDocument.Parse(result);
        var scaffold = doc.RootElement.GetProperty("scaffold").GetString()!;

        Assert.DoesNotContain("ANTI-PATTERN WARNINGS", scaffold);
    }

    [Fact]
    public void GenerateTestScaffold_WarningCount_InMetadata()
    {
        SeedTypeRegistry(new TypeRecord
        {
            Name = "ProblematicClass",
            Namespace = "App",
            IsStatic = true,
            IsAbstract = true,
            Interfaces = ["IDisposable"]
        });

        var result = TestScaffoldTool.GenerateTestScaffold("ProblematicClass");
        var doc = JsonDocument.Parse(result);

        Assert.True(doc.RootElement.GetProperty("antiPatternWarnings").GetInt32() >= 3);
    }

    // ── GetAntiPatternWarnings unit tests ──

    [Fact]
    public void GetAntiPatternWarnings_PrimitiveParams_NoWarnings()
    {
        var typeRecord = new TypeRecord { Name = "Simple", Namespace = "App" };
        var mockFields = new List<(string, string, string, MockRecipe?)>();
        var concreteFields = new List<(string, string, string)>
        {
            ("_name", "string", "\"test\""),
            ("_count", "int", "0")
        };

        var warnings = TestScaffoldTool.GetAntiPatternWarnings(typeRecord, mockFields, concreteFields);

        Assert.DoesNotContain(warnings, w => w.Contains("CONCRETE DEPENDENCIES"));
    }

    // ── Error path coverage ──

    [Fact]
    public void GenerateTestScaffold_InvalidNamespace_ReturnsError()
    {
        var result = TestScaffoldTool.GenerateTestScaffold("Any", ns: "\0");

        Assert.StartsWith("ERROR in GenerateTestScaffold", result);
    }

    // ── GetDefaultValue: uncovered type aliases (covers L392, L403, L428) ──

    [Theory]
    [InlineData("float", "0f")]
    [InlineData("Type", "typeof(object)")]
    [InlineData("ushort", "(ushort)0")]
    [InlineData("Func<string>", "null!")]
    public void GetDefaultValue_RareTypes_ReturnsCorrectDefaults(string typeName, string expected)
    {
        Assert.Equal(expected, TestScaffoldTool.GetDefaultValue(typeName));
    }

    [Theory]
    [InlineData("Action", "() => {{ }}")]
    [InlineData("Action<string>", "() => {{ }}")]
    public void GetDefaultValue_ActionTypes_ReturnsLambda(string typeName, string expected)
    {
        Assert.Equal(expected, TestScaffoldTool.GetDefaultValue(typeName));
    }

    [Theory]
    [InlineData("StatusEnum", "default(StatusEnum)")]
    [InlineData("ContentType", "default(ContentType)")]
    public void GetDefaultValue_SuffixTypes_ReturnsDefault(string typeName, string expected)
    {
        Assert.Equal(expected, TestScaffoldTool.GetDefaultValue(typeName));
    }

    [Fact]
    public void GetDefaultValue_UnknownType_ReturnsDefaultBang()
    {
        Assert.Equal("default(MyCustomClass)!", TestScaffoldTool.GetDefaultValue("MyCustomClass"));
    }

    // ── IsAsyncMethod: IAsync interface + property accessor path (covers L473, L483, L485-487) ──

    [Fact]
    public void IsAsyncMethod_WellKnownName_ReturnsTrue()
    {
        // "MoveNextAsync" is a well-known async method name (covers s_asyncMethodNames.Contains)
        Assert.True(TestScaffoldTool.IsAsyncMethod("MoveNextAsync", null));
    }

    [Fact]
    public void IsAsyncMethod_PropertyAccessorOnAsyncInterface_ReturnsTrue()
    {
        var type = new TypeRecord
        {
            Name = "AsyncStream",
            Namespace = "App",
            Interfaces = ["IAsyncEnumerable"]
        };

        // get_*Async* with IAsync interface -> true (covers property accessor + IAsync branch)
        Assert.True(TestScaffoldTool.IsAsyncMethod("get_AsyncValue", type));
    }

    [Fact]
    public void IsAsyncMethod_PropertyAccessorNoAsyncInterface_ReturnsFalse()
    {
        var type = new TypeRecord
        {
            Name = "SyncType",
            Namespace = "App",
            Interfaces = ["IDisposable"]
        };

        Assert.False(TestScaffoldTool.IsAsyncMethod("get_Value", type));
    }

    // ── GetAntiPatternWarnings: event-like properties (covers L701) ──

    [Theory]
    [InlineData("EventHandler", "Changed")]
    [InlineData("Action", "Execute")]
    [InlineData("Action<string>", "Handler")]
    [InlineData("string", "OnStartup")]
    public void GetAntiPatternWarnings_EventLikeProperty_WarnsAboutEvents(string clrType, string propertyName)
    {
        var typeRecord = new TypeRecord
        {
            Name = "TestHost",
            Namespace = "App",
            Properties = [new PropertyRecord { Name = propertyName, ClrType = clrType, HasSet = true }]
        };
        var mockFields = new List<(string, string, string, MockRecipe?)>();
        var concreteFields = new List<(string, string, string)>();

        var warnings = TestScaffoldTool.GetAntiPatternWarnings(typeRecord, mockFields, concreteFields);

        Assert.Contains(warnings, w => w.Contains("HAS EVENTS"));
    }

    // ── ExtractGenericArg fallback (covers L428 via GetDefaultValue) ──

    [Fact]
    public void GetDefaultValue_MalformedGenericType_FallsBackToObject()
    {
        // "IEnumerable<string" (missing closing >) — ExtractGenericArg can't find > at end → returns "object"
        Assert.Equal("Array.Empty<object>()", TestScaffoldTool.GetDefaultValue("IEnumerable<string"));
    }

    // ── IsAsyncMethod: IAsync interface + non-Async baseName → fall-through (covers L487) ──

    [Fact]
    public void IsAsyncMethod_IAsyncInterfaceNonAsyncMethod_ReturnsFalse()
    {
        var type = new TypeRecord
        {
            Name = "AsyncStream",
            Namespace = "App",
            Interfaces = ["IAsyncEnumerable`1"]
        };

        // "get_Current" → baseName = "Current", which does NOT contain "Async"
        // So the inner if is false → falls through the IAsync block (L487) → returns false
        Assert.False(TestScaffoldTool.IsAsyncMethod("get_Current", type));
    }

    // ── Incremental scaffold mode (v3) ──

    [Fact]
    public void GenerateTestScaffold_IncrementalMode_GeneratesOnlyMethodStubs()
    {
        SeedTypeRegistry(new TypeRecord
        {
            Name = "MyService",
            Namespace = "App",
            Constructors = [new ConstructorRecord { Params = ["ILogger logger"] }]
        });
        SeedCoverageGaps(new CoverageGap
        {
            Class = "MyService",
            Namespace = "App",
            UncoveredMethods =
            [
                new UncoveredMethod { Name = "DoWork", StartLine = 10, EndLine = 20, UncoveredLines = 8 },
                new UncoveredMethod { Name = "Process", StartLine = 30, EndLine = 40, UncoveredLines = 5 },
                new UncoveredMethod { Name = "Validate", StartLine = 50, EndLine = 60, UncoveredLines = 3 }
            ]
        });

        var result = TestScaffoldTool.GenerateTestScaffold("MyService", methodNames: "DoWork, Process");

        var parsed = JsonSerializer.Deserialize<JsonElement>(result);
        Assert.Equal("incremental", parsed.GetProperty("mode").GetString());
        Assert.Equal(2, parsed.GetProperty("methodCount").GetInt32());

        var stubs = parsed.GetProperty("stubs").GetString()!;
        Assert.Contains("DoWork_ShouldWork", stubs);
        Assert.Contains("Process_ShouldWork", stubs);
        Assert.DoesNotContain("Validate_ShouldWork", stubs);
        // No class skeleton
        Assert.DoesNotContain("public class", stubs);
        Assert.DoesNotContain("private readonly", stubs);
    }

    [Fact]
    public void GenerateTestScaffold_IncrementalMode_IncludesCoverageInfo()
    {
        SeedTypeRegistry(new TypeRecord
        {
            Name = "MyService",
            Namespace = "App"
        });
        SeedCoverageGaps(new CoverageGap
        {
            Class = "MyService",
            Namespace = "App",
            UncoveredMethods =
            [
                new UncoveredMethod { Name = "DoWork", StartLine = 10, EndLine = 20, UncoveredLines = 8 }
            ]
        });

        var result = TestScaffoldTool.GenerateTestScaffold("MyService", methodNames: "DoWork");

        var parsed = JsonSerializer.Deserialize<JsonElement>(result);
        var stubs = parsed.GetProperty("stubs").GetString()!;
        Assert.Contains("lines 10-20", stubs);
        Assert.Contains("8 uncovered", stubs);
    }

    [Fact]
    public void GenerateTestScaffold_IncrementalMode_SyntheticMethodsWhenNoCoverage()
    {
        SeedTypeRegistry(new TypeRecord
        {
            Name = "MyService",
            Namespace = "App"
        });
        // No coverage data for this class

        var result = TestScaffoldTool.GenerateTestScaffold("MyService", methodNames: "DoWork, Process");

        var parsed = JsonSerializer.Deserialize<JsonElement>(result);
        Assert.Equal(2, parsed.GetProperty("synthetic").GetInt32());
        Assert.Equal(0, parsed.GetProperty("fromCoverageData").GetInt32());

        var stubs = parsed.GetProperty("stubs").GetString()!;
        Assert.Contains("DoWork_ShouldWork", stubs);
        Assert.Contains("Process_ShouldWork", stubs);
    }

    [Fact]
    public void GenerateTestScaffold_IncrementalMode_IncludesGotchaWarnings()
    {
        SeedTypeRegistry(new TypeRecord
        {
            Name = "MyService",
            Namespace = "App"
        });
        SeedGotchas(new Gotcha
        {
            Type = "MyService",
            Category = "mock",
            Description = "Watch out for async disposal",
            Date = DateTime.UtcNow.ToString("o")
        });

        var result = TestScaffoldTool.GenerateTestScaffold("MyService", methodNames: "DoWork");

        var parsed = JsonSerializer.Deserialize<JsonElement>(result);
        Assert.Equal(1, parsed.GetProperty("gotchaCount").GetInt32());

        var stubs = parsed.GetProperty("stubs").GetString()!;
        Assert.Contains("GOTCHAS", stubs);
        Assert.Contains("async disposal", stubs);
    }

    [Fact]
    public void GenerateTestScaffold_IncrementalMode_TypeNotFound_ReturnsError()
    {
        var result = TestScaffoldTool.GenerateTestScaffold("NonExistent", methodNames: "DoWork");

        Assert.Contains("not found", result);
    }

    [Fact]
    public void GenerateTestScaffold_IncrementalMode_EmptyMethodNames_ReturnsError()
    {
        SeedTypeRegistry(new TypeRecord
        {
            Name = "MyService",
            Namespace = "App"
        });
        SeedCoverageGaps(new CoverageGap
        {
            Class = "MyService",
            Namespace = "App",
            UncoveredMethods =
            [
                new UncoveredMethod { Name = "DoWork", StartLine = 10, EndLine = 20, UncoveredLines = 8 }
            ]
        });

        var result = TestScaffoldTool.GenerateTestScaffold("MyService", methodNames: "NonExistentMethod");

        // The method should be generated as a synthetic stub since it's not empty
        var parsed = JsonSerializer.Deserialize<JsonElement>(result);
        Assert.Equal(1, parsed.GetProperty("synthetic").GetInt32());
    }

    // ── ClassifyArchetype tests ──

    [Fact]
    public void ClassifyArchetype_StaticClass_ReturnsStaticHelper()
    {
        var type = new TypeRecord { Name = "StringExtensions", IsStatic = true };

        var result = TestScaffoldTool.ClassifyArchetype(type, mockFieldCount: 0, concreteFieldCount: 0, coverageGap: null);

        Assert.NotNull(result);
        Assert.Contains("STATIC HELPER", result);
        Assert.Contains("Pure function", result);
    }

    [Fact]
    public void ClassifyArchetype_PocoDataClass_ReturnsPoco()
    {
        var type = new TypeRecord
        {
            Name = "UserDto",
            Properties =
            [
                new PropertyRecord { Name = "Id", ClrType = "int" },
                new PropertyRecord { Name = "Name", ClrType = "string" },
                new PropertyRecord { Name = "Email", ClrType = "string" },
                new PropertyRecord { Name = "Age", ClrType = "int" }
            ]
        };

        var result = TestScaffoldTool.ClassifyArchetype(type, mockFieldCount: 0, concreteFieldCount: 0, coverageGap: null);

        Assert.NotNull(result);
        Assert.Contains("POCO/DATA CLASS", result);
    }

    [Fact]
    public void ClassifyArchetype_HeavyDiService_ReturnsHeavyDi()
    {
        var type = new TypeRecord { Name = "ComplexService" };

        var result = TestScaffoldTool.ClassifyArchetype(type, mockFieldCount: 6, concreteFieldCount: 0, coverageGap: null);

        Assert.NotNull(result);
        Assert.Contains("HEAVY-DI SERVICE", result);
        Assert.Contains("6 dependencies", result);
    }

    [Fact]
    public void ClassifyArchetype_StandardService_ReturnsStandard()
    {
        var type = new TypeRecord { Name = "OrderService" };

        var result = TestScaffoldTool.ClassifyArchetype(type, mockFieldCount: 3, concreteFieldCount: 0, coverageGap: null);

        Assert.NotNull(result);
        Assert.Contains("STANDARD SERVICE", result);
    }

    [Fact]
    public void ClassifyArchetype_MixedDiService_ReturnsMixed()
    {
        var type = new TypeRecord { Name = "HybridService" };

        var result = TestScaffoldTool.ClassifyArchetype(type, mockFieldCount: 2, concreteFieldCount: 1, coverageGap: null);

        Assert.NotNull(result);
        Assert.Contains("MIXED-DI SERVICE", result);
        Assert.Contains("2 mocked", result);
        Assert.Contains("1 concrete", result);
    }

    [Fact]
    public void ClassifyArchetype_BuilderFactory_ReturnsPattern()
    {
        var type = new TypeRecord { Name = "ReportBuilder" };

        var result = TestScaffoldTool.ClassifyArchetype(type, mockFieldCount: 0, concreteFieldCount: 0, coverageGap: null);

        Assert.NotNull(result);
        Assert.Contains("BUILDER/FACTORY", result);
    }

    [Fact]
    public void ClassifyArchetype_FactoryPattern_ReturnsPattern()
    {
        var type = new TypeRecord { Name = "EntityFactory" };

        var result = TestScaffoldTool.ClassifyArchetype(type, mockFieldCount: 0, concreteFieldCount: 0, coverageGap: null);

        Assert.NotNull(result);
        Assert.Contains("BUILDER/FACTORY", result);
    }

    [Fact]
    public void ClassifyArchetype_ProviderPattern_ReturnsPattern()
    {
        var type = new TypeRecord { Name = "ConfigProvider" };

        var result = TestScaffoldTool.ClassifyArchetype(type, mockFieldCount: 0, concreteFieldCount: 0, coverageGap: null);

        Assert.NotNull(result);
        Assert.Contains("BUILDER/FACTORY", result);
    }

    [Fact]
    public void ClassifyArchetype_PlainClass_ReturnsNull()
    {
        var type = new TypeRecord { Name = "SomeClass" };

        var result = TestScaffoldTool.ClassifyArchetype(type, mockFieldCount: 0, concreteFieldCount: 0, coverageGap: null);

        Assert.Null(result);
    }

    // ── generateEdgeCases parameter ──

    [Fact]
    public void GenerateTestScaffold_EdgeCasesFalse_NoEdgeCaseStubs()
    {
        SeedTypeRegistry(new TypeRecord
        {
            Name = "EdgeTarget",
            Namespace = "App",
            Constructors = [new ConstructorRecord { Params = ["IMyService"] }]
        });
        SeedCoverageGaps(new CoverageGap
        {
            Class = "EdgeTarget",
            Namespace = "App",
            UncoveredMethods =
            [
                new UncoveredMethod { Name = "GetName", StartLine = 10, EndLine = 20, UncoveredLines = 8 }
            ]
        });

        var result = TestScaffoldTool.GenerateTestScaffold("EdgeTarget", generateEdgeCases: false);
        var doc = JsonDocument.Parse(result);
        var scaffold = doc.RootElement.GetProperty("scaffold").GetString()!;

        // Should NOT include edge case stubs like _NullInput, _EmptyString
        Assert.DoesNotContain("_NullInput_", scaffold);
        Assert.DoesNotContain("_EmptyString_", scaffold);
    }

    [Fact]
    public void GenerateTestScaffold_EdgeCasesTrue_IncludesEdgeCaseStubs()
    {
        SeedTypeRegistry(new TypeRecord
        {
            Name = "EdgeTarget2",
            Namespace = "App",
            Constructors = [new ConstructorRecord { Params = ["IMyService"] }]
        });
        SeedCoverageGaps(new CoverageGap
        {
            Class = "EdgeTarget2",
            Namespace = "App",
            UncoveredMethods =
            [
                new UncoveredMethod { Name = "GetName", StartLine = 10, EndLine = 20, UncoveredLines = 8 }
            ]
        });

        var result = TestScaffoldTool.GenerateTestScaffold("EdgeTarget2", generateEdgeCases: true);
        var doc = JsonDocument.Parse(result);
        var scaffold = doc.RootElement.GetProperty("scaffold").GetString()!;

        // Should include edge case stubs since the method has a string-like name
        Assert.Contains("GetName", scaffold);
    }
}