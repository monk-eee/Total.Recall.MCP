using System.Text.Json;
using Total.Recall.Infrastructure;
using Total.Recall.Models;
using Total.Recall.Tools;

namespace Total.Recall.Tests.Tools;

/// <summary>
/// Tests for ContextTool.GetContext — combined context lookup returning
/// type record, gotchas, test inventory, and mock recipes.
/// </summary>
[Collection("ToolTests")]
public sealed class ContextToolTests : ToolTestBase
{
    // Thin aliases for base-class seed helpers (preserve existing call-site names)
    private void SeedTypes(params TypeRecord[] records) => SeedTypeRegistry(records);
    private void SeedTests(params TestInventoryEntry[] records) => SeedTestInventory(records);

    [Fact]
    public void GetContext_NoData_ReturnsNullType()
    {
        var result = ContextTool.GetContext("Anything", depth: "full");

        // Should still return valid JSON with null type
        var doc = JsonDocument.Parse(result);
        Assert.True(doc.RootElement.GetProperty("type").ValueKind == JsonValueKind.Null);
        Assert.Equal(0, doc.RootElement.GetProperty("gotchas").GetArrayLength());
        Assert.Equal(0, doc.RootElement.GetProperty("tests").GetArrayLength());
        Assert.Equal(0, doc.RootElement.GetProperty("mockRecipes").GetArrayLength());
    }

    [Fact]
    public void GetContext_ExactMatch_ReturnsTypeRecord()
    {
        SeedTypes(
            new TypeRecord { Name = "AuditEntry", Namespace = "Server.Auditing" },
            new TypeRecord { Name = "Parser", Namespace = "MyApp.Parsing" }
        );

        var result = ContextTool.GetContext("AuditEntry");

        var doc = JsonDocument.Parse(result);
        Assert.Equal("AuditEntry", doc.RootElement.GetProperty("type").GetProperty("name").GetString());
    }

    [Fact]
    public void GetContext_CaseInsensitiveMatch_ReturnsTypeRecord()
    {
        SeedTypes(new TypeRecord { Name = "AuditEntry", Namespace = "Server.Auditing" });

        var result = ContextTool.GetContext("auditentry");

        var doc = JsonDocument.Parse(result);
        Assert.Equal("AuditEntry", doc.RootElement.GetProperty("type").GetProperty("name").GetString());
    }

    [Fact]
    public void GetContext_ContainsMatch_ReturnsTypeRecord()
    {
        SeedTypes(new TypeRecord { Name = "AuditEntryValidator", Namespace = "Server.Auditing" });

        var result = ContextTool.GetContext("AuditEntry");

        var doc = JsonDocument.Parse(result);
        Assert.Equal("AuditEntryValidator",
            doc.RootElement.GetProperty("type").GetProperty("name").GetString());
    }

    [Fact]
    public void GetContext_NoTypeMatch_TypeIsNull()
    {
        SeedTypes(new TypeRecord { Name = "Foo", Namespace = "Bar" });

        var result = ContextTool.GetContext("NonExistent");

        var doc = JsonDocument.Parse(result);
        Assert.Equal(JsonValueKind.Null, doc.RootElement.GetProperty("type").ValueKind);
    }

    [Fact]
    public void GetContext_WithGotchas_ReturnsMatchingGotchas()
    {
        SeedTypes(new TypeRecord { Name = "AuditEntry", Namespace = "Server.Auditing" });
        SeedGotchas(
            new Gotcha { Type = "AuditEntry", Category = "constructor", Description = "Needs ILogger", Date = "2025-01-01" },
            new Gotcha { Type = "AuditEntry", Category = "enum", Description = "StatusEnum trap", Date = "2025-01-02" },
            new Gotcha { Type = "OtherClass", Category = "bug", Description = "Unrelated", Date = "2025-01-03" }
        );

        var result = ContextTool.GetContext("AuditEntry");

        var doc = JsonDocument.Parse(result);
        Assert.Equal(2, doc.RootElement.GetProperty("gotchas").GetArrayLength());
    }

    [Fact]
    public void GetContext_WithTestInventory_ReturnsMatchingTests()
    {
        SeedTypes(new TypeRecord { Name = "AuditEntry", Namespace = "Server.Auditing" });
        SeedTests(
            new TestInventoryEntry { Class = "AuditEntry", TestCount = 5, TestMethods = ["Test1", "Test2", "Test3", "Test4", "Test5"] },
            new TestInventoryEntry { Class = "Other", TestCount = 3, TestMethods = ["A", "B", "C"] }
        );

        var result = ContextTool.GetContext("AuditEntry");

        var doc = JsonDocument.Parse(result);
        Assert.Equal(1, doc.RootElement.GetProperty("tests").GetArrayLength());
    }

    [Fact]
    public void GetContext_WithMockRecipes_ReturnsMocksForInterfaces()
    {
        SeedTypes(new TypeRecord
        {
            Name = "AuditEntry",
            Namespace = "Server.Auditing",
            Interfaces = ["IDisposable", "IOrderSource"]
        });
        SeedMockRecipes(
            new MockRecipe { Interface = "IOrderSource", Namespace = "Server.Content", Recipe = "mock setup code" },
            new MockRecipe { Interface = "ILogger", Namespace = "Microsoft.Extensions.Logging", Recipe = "logger mock" }
        );

        var result = ContextTool.GetContext("AuditEntry", depth: "full");

        var doc = JsonDocument.Parse(result);
        var mockRecipes = doc.RootElement.GetProperty("mockRecipes");
        Assert.Equal(1, mockRecipes.GetArrayLength());
        Assert.Contains("IOrderSource", mockRecipes[0].GetProperty("interface").GetString());
    }

    [Fact]
    public void GetContext_NoInterfaces_MockRecipesEmpty()
    {
        SeedTypes(new TypeRecord { Name = "SimpleClass", Namespace = "App", Interfaces = [] });
        SeedMockRecipes(
            new MockRecipe { Interface = "IOrderSource", Namespace = "Server", Recipe = "code" }
        );

        var result = ContextTool.GetContext("SimpleClass", depth: "full");

        var doc = JsonDocument.Parse(result);
        Assert.Equal(0, doc.RootElement.GetProperty("mockRecipes").GetArrayLength());
    }

    [Fact]
    public void GetContext_NullInterfaces_MockRecipesEmpty()
    {
        // TypeRecord with Interfaces not set (defaults to empty list)
        SeedTypes(new TypeRecord { Name = "PlainClass", Namespace = "App" });
        SeedMockRecipes(
            new MockRecipe { Interface = "IService", Namespace = "App", Recipe = "code" }
        );

        var result = ContextTool.GetContext("PlainClass", depth: "full");

        var doc = JsonDocument.Parse(result);
        Assert.Equal(0, doc.RootElement.GetProperty("mockRecipes").GetArrayLength());
    }

    [Fact]
    public void GetContext_CombinedData_AllFieldsPopulated()
    {
        SeedTypes(new TypeRecord
        {
            Name = "AuditEntry",
            Namespace = "Server.Auditing",
            Interfaces = ["IOrderSource"],
            Properties = [new PropertyRecord { Name = "Id", ClrType = "int", HasSet = true }]
        });
        SeedGotchas(new Gotcha { Type = "AuditEntry", Category = "bug", Description = "watch out", Date = "2025-01-01" });
        SeedTests(new TestInventoryEntry { Class = "AuditEntry", TestCount = 2, TestMethods = ["A", "B"] });
        SeedMockRecipes(new MockRecipe { Interface = "IOrderSource", Namespace = "Server", Recipe = "setup" });

        var result = ContextTool.GetContext("AuditEntry", depth: "full");

        var doc = JsonDocument.Parse(result);
        Assert.NotEqual(JsonValueKind.Null, doc.RootElement.GetProperty("type").ValueKind);
        Assert.True(doc.RootElement.GetProperty("gotchas").GetArrayLength() > 0);
        Assert.True(doc.RootElement.GetProperty("tests").GetArrayLength() > 0);
        Assert.True(doc.RootElement.GetProperty("mockRecipes").GetArrayLength() > 0);
    }

    [Fact]
    public void GetContext_InterfaceNormalization_MatchesWithOrWithoutIPrefix()
    {
        // ContextTool strips leading "I" and tries both forms
        SeedTypes(new TypeRecord
        {
            Name = "MyService",
            Namespace = "App",
            Interfaces = ["IOrderExport"]
        });
        SeedMockRecipes(
            new MockRecipe { Interface = "IOrderExport", Namespace = "Server", Recipe = "job output mock" }
        );

        var result = ContextTool.GetContext("MyService", depth: "full");

        var doc = JsonDocument.Parse(result);
        Assert.Equal(1, doc.RootElement.GetProperty("mockRecipes").GetArrayLength());
    }

    // ── Coverage gap enrichment ──

    [Fact]
    public void GetContext_WithCoverageGap_ReturnsCoverageData()
    {
        SeedTypes(new TypeRecord { Name = "AuditEntry", Namespace = "Server.Auditing" });
        SeedCoverageGaps(new CoverageGap
        {
            Class = "AuditEntry",
            Namespace = "Server.Auditing",
            TotalLines = 100,
            UncoveredLines = 30,
            CoveragePercent = 70.0,
            UncoveredMethods = [new UncoveredMethod { Name = "Validate", StartLine = 10, EndLine = 20, UncoveredLines = 8 }]
        });

        var result = ContextTool.GetContext("AuditEntry");
        var doc = JsonDocument.Parse(result);

        var gap = doc.RootElement.GetProperty("coverageGap");
        Assert.NotEqual(JsonValueKind.Null, gap.ValueKind);
        Assert.Equal(30, gap.GetProperty("uncoveredLines").GetInt32());
        Assert.Equal(70.0, gap.GetProperty("coveragePercent").GetDouble());
    }

    [Fact]
    public void GetContext_NoCoverageData_CoverageGapIsNull()
    {
        SeedTypes(new TypeRecord { Name = "Clean", Namespace = "App" });

        var result = ContextTool.GetContext("Clean");
        var doc = JsonDocument.Parse(result);

        Assert.Equal(JsonValueKind.Null, doc.RootElement.GetProperty("coverageGap").ValueKind);
    }

    // ── Session history enrichment ──

    [Fact]
    public void GetContext_WithSessionHistory_ReturnsMatchingSessions()
    {
        SeedTypes(new TypeRecord { Name = "Parser", Namespace = "App" });
        SeedSessions(
            new SessionRecord
            {
                SessionId = "s1",
                StartedUtc = "2025-01-01T00:00:00Z",
                Model = "claude-sonnet",
                ClassesAttempted = ["Parser"],
                ClassesSucceeded = ["Parser"],
                ClassesFailed = []
            },
            new SessionRecord
            {
                SessionId = "s2",
                StartedUtc = "2025-01-02T00:00:00Z",
                Model = "gpt-4",
                ClassesAttempted = ["OtherClass"],
                ClassesSucceeded = [],
                ClassesFailed = [new SessionFailure { Class = "OtherClass", Reason = "compile error" }]
            }
        );

        var result = ContextTool.GetContext("Parser", depth: "full");
        var doc = JsonDocument.Parse(result);
        var history = doc.RootElement.GetProperty("sessionHistory");

        Assert.Equal(1, history.GetArrayLength());
        Assert.Equal("s1", history[0].GetProperty("sessionId").GetString());
        Assert.True(history[0].GetProperty("succeeded").GetBoolean());
    }

    [Fact]
    public void GetContext_SessionWithFailure_IncludesFailReason()
    {
        SeedTypes(new TypeRecord { Name = "Broken", Namespace = "App" });
        SeedSessions(new SessionRecord
        {
            SessionId = "s-fail",
            StartedUtc = "2025-01-01T00:00:00Z",
            Model = "claude-sonnet",
            ClassesAttempted = ["Broken"],
            ClassesSucceeded = [],
            ClassesFailed = [new SessionFailure { Class = "Broken", Reason = "DI too complex" }]
        });

        var result = ContextTool.GetContext("Broken", depth: "full");
        var doc = JsonDocument.Parse(result);
        var history = doc.RootElement.GetProperty("sessionHistory");

        Assert.Equal(1, history.GetArrayLength());
        Assert.True(history[0].GetProperty("failed").GetBoolean());
        Assert.Equal("DI too complex", history[0].GetProperty("failReason").GetString());
    }

    [Fact]
    public void GetContext_NoSessions_SessionHistoryEmpty()
    {
        SeedTypes(new TypeRecord { Name = "Fresh", Namespace = "App" });

        var result = ContextTool.GetContext("Fresh", depth: "full");
        var doc = JsonDocument.Parse(result);

        Assert.Equal(0, doc.RootElement.GetProperty("sessionHistory").GetArrayLength());
    }

    // ── Recommended patterns ──

    [Fact]
    public void GetContext_DisposableType_RecommendsDisposePattern()
    {
        SeedTypes(new TypeRecord { Name = "Resource", Namespace = "App", Interfaces = ["IDisposable"] });

        var result = ContextTool.GetContext("Resource", depth: "full");
        var doc = JsonDocument.Parse(result);
        var patterns = doc.RootElement.GetProperty("recommendedPatterns");

        Assert.True(patterns.GetArrayLength() > 0);
        Assert.Contains("DISPOSE", patterns.EnumerateArray().Select(p => p.GetString()!).First(p => p.Contains("DISPOSE")));
    }

    [Fact]
    public void GetContext_AsyncMethods_RecommendsCancellationPattern()
    {
        SeedTypes(new TypeRecord { Name = "AsyncWorker", Namespace = "App" });
        SeedCoverageGaps(new CoverageGap
        {
            Class = "AsyncWorker",
            UncoveredMethods = [new UncoveredMethod { Name = "DoWorkAsync", StartLine = 1, EndLine = 10, UncoveredLines = 5 }]
        });

        var result = ContextTool.GetContext("AsyncWorker", depth: "full");
        var doc = JsonDocument.Parse(result);
        var patterns = doc.RootElement.GetProperty("recommendedPatterns");

        var patternList = patterns.EnumerateArray().Select(p => p.GetString()!).ToList();
        Assert.Contains(patternList, p => p.Contains("CANCELLATION"));
    }

    [Fact]
    public void GetContext_MultipleCtors_RecommendsCtorTests()
    {
        SeedTypes(new TypeRecord
        {
            Name = "Overloaded",
            Namespace = "App",
            Constructors =
            [
                new ConstructorRecord { Params = [] },
                new ConstructorRecord { Params = ["ILogger _logger"] }
            ]
        });

        var result = ContextTool.GetContext("Overloaded", depth: "full");
        var doc = JsonDocument.Parse(result);
        var patterns = doc.RootElement.GetProperty("recommendedPatterns");

        var patternList = patterns.EnumerateArray().Select(p => p.GetString()!).ToList();
        Assert.Contains(patternList, p => p.Contains("MULTIPLE CTORS"));
    }

    [Fact]
    public void GetContext_WithInterfaces_RecommendsInterfaceContracts()
    {
        SeedTypes(new TypeRecord { Name = "Implementor", Namespace = "App", Interfaces = ["IService", "IDisposable"] });

        var result = ContextTool.GetContext("Implementor", depth: "full");
        var doc = JsonDocument.Parse(result);
        var patterns = doc.RootElement.GetProperty("recommendedPatterns");

        var patternList = patterns.EnumerateArray().Select(p => p.GetString()!).ToList();
        Assert.Contains(patternList, p => p.Contains("INTERFACE CONTRACTS"));
    }

    [Fact]
    public void GetContext_SettableProperties_RecommendsRoundtripTests()
    {
        SeedTypes(new TypeRecord
        {
            Name = "SettableClass",
            Namespace = "App",
            Properties = [new PropertyRecord { Name = "Name", ClrType = "string", HasSet = true }]
        });

        var result = ContextTool.GetContext("SettableClass", depth: "full");
        var doc = JsonDocument.Parse(result);
        var patterns = doc.RootElement.GetProperty("recommendedPatterns");

        var patternList = patterns.EnumerateArray().Select(p => p.GetString()!).ToList();
        Assert.Contains(patternList, p => p.Contains("PROPERTY ROUNDTRIP"));
    }

    [Fact]
    public void GetContext_EnumType_RecommendsEnumValueTests()
    {
        SeedTypes(new TypeRecord
        {
            Name = "StatusCode",
            Namespace = "App",
            IsEnum = true,
            EnumValues = ["Active", "Inactive", "Pending"]
        });

        var result = ContextTool.GetContext("StatusCode", depth: "full");
        var doc = JsonDocument.Parse(result);
        var patterns = doc.RootElement.GetProperty("recommendedPatterns");

        var patternList = patterns.EnumerateArray().Select(p => p.GetString()!).ToList();
        Assert.Contains(patternList, p => p.Contains("ENUM VALUES") && p.Contains("3"));
    }

    [Fact]
    public void GetContext_StaticClass_RecommendsStaticPattern()
    {
        SeedTypes(new TypeRecord { Name = "Utilities", Namespace = "App", IsStatic = true });

        var result = ContextTool.GetContext("Utilities", depth: "full");
        var doc = JsonDocument.Parse(result);
        var patterns = doc.RootElement.GetProperty("recommendedPatterns");

        var patternList = patterns.EnumerateArray().Select(p => p.GetString()!).ToList();
        Assert.Contains(patternList, p => p.Contains("STATIC CLASS"));
    }

    [Fact]
    public void GetContext_AbstractClass_RecommendsTestDouble()
    {
        SeedTypes(new TypeRecord { Name = "BaseHandler", Namespace = "App", IsAbstract = true });

        var result = ContextTool.GetContext("BaseHandler", depth: "full");
        var doc = JsonDocument.Parse(result);
        var patterns = doc.RootElement.GetProperty("recommendedPatterns");

        var patternList = patterns.EnumerateArray().Select(p => p.GetString()!).ToList();
        Assert.Contains(patternList, p => p.Contains("ABSTRACT CLASS") && p.Contains("TestDouble"));
    }

    [Fact]
    public void GetContext_InterfaceCtorParams_RecommendsNullGuards()
    {
        SeedTypes(new TypeRecord
        {
            Name = "Guarded",
            Namespace = "App",
            Constructors = [new ConstructorRecord { Params = ["ILogger _logger", "IRepo _repo"] }]
        });

        var result = ContextTool.GetContext("Guarded", depth: "full");
        var doc = JsonDocument.Parse(result);
        var patterns = doc.RootElement.GetProperty("recommendedPatterns");

        var patternList = patterns.EnumerateArray().Select(p => p.GetString()!).ToList();
        Assert.Contains(patternList, p => p.Contains("NULL GUARDS") && p.Contains("2"));
    }

    [Fact]
    public void GetContext_ExceptionBase_RecommendsExceptionPattern()
    {
        SeedTypes(new TypeRecord { Name = "CustomException", Namespace = "App", BaseType = "InvalidOperationException" });

        var result = ContextTool.GetContext("CustomException", depth: "full");
        var doc = JsonDocument.Parse(result);
        var patterns = doc.RootElement.GetProperty("recommendedPatterns");

        var patternList = patterns.EnumerateArray().Select(p => p.GetString()!).ToList();
        Assert.Contains(patternList, p => p.Contains("EXCEPTION TYPE"));
    }

    [Fact]
    public void GetContext_NoType_EmptyRecommendedPatterns()
    {
        var result = ContextTool.GetContext("Nonexistent", depth: "full");
        var doc = JsonDocument.Parse(result);

        Assert.Equal(0, doc.RootElement.GetProperty("recommendedPatterns").GetArrayLength());
    }

    // ── GetRecommendedPatterns unit tests ──

    [Fact]
    public void GetRecommendedPatterns_NullType_ReturnsEmpty()
    {
        Assert.Empty(ContextTool.GetRecommendedPatterns(null, null));
    }

    [Fact]
    public void GetRecommendedPatterns_ControllerBase_RecommendsControllerPattern()
    {
        var type = new TypeRecord { Name = "MyController", Namespace = "App", BaseType = "ControllerBase" };
        var patterns = ContextTool.GetRecommendedPatterns(type, null);

        Assert.Contains(patterns, p => p.Contains("CONTROLLER"));
    }

    [Fact]
    public void GetRecommendedPatterns_HandlerBase_RecommendsHandlerPattern()
    {
        var type = new TypeRecord { Name = "MyHandler", Namespace = "App", BaseType = "RequestHandler" };
        var patterns = ContextTool.GetRecommendedPatterns(type, null);

        Assert.Contains(patterns, p => p.Contains("HANDLER"));
    }

    // ── Error path coverage ──

    [Fact]
    public void GetContext_InvalidNamespace_ReturnsError()
    {
        var result = ContextTool.GetContext("Any", ns: "\0");

        Assert.StartsWith("ERROR in GetContext", result);
    }

    // ── Event-like properties pattern (covers L162-164) ──

    [Fact]
    public void GetRecommendedPatterns_EventHandlerProperty_RecommendsEventPattern()
    {
        var type = new TypeRecord
        {
            Name = "EventSource",
            Namespace = "App",
            Properties = [new PropertyRecord { Name = "StateChanged", ClrType = "EventHandler", HasSet = true }]
        };
        var patterns = ContextTool.GetRecommendedPatterns(type, null);

        Assert.Contains(patterns, p => p.Contains("EVENTS"));
    }

    [Fact]
    public void GetRecommendedPatterns_OnPrefixProperty_RecommendsEventPattern()
    {
        var type = new TypeRecord
        {
            Name = "Callback",
            Namespace = "App",
            Properties = [new PropertyRecord { Name = "OnCompleted", ClrType = "Action", HasSet = true }]
        };
        var patterns = ContextTool.GetRecommendedPatterns(type, null);

        Assert.Contains(patterns, p => p.Contains("EVENTS"));
    }
}
