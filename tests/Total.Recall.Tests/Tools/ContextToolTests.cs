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
public sealed class ContextToolTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string? _originalEnv;

    public ContextToolTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "total-recall-tests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempDir);
        _originalEnv = Environment.GetEnvironmentVariable(RepoConfig.EnvVarName);
        Environment.SetEnvironmentVariable(RepoConfig.EnvVarName, _tempDir);
        StoreRegistry.Reset();
    }

    public void Dispose()
    {
        StoreRegistry.Reset();
        Environment.SetEnvironmentVariable(RepoConfig.EnvVarName, _originalEnv);
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private void SeedTypes(params TypeRecord[] records)
    {
        var store = new JsonLineStore<TypeRecord>(RepoConfig.TypeRegistryPath(_tempDir));
        store.WriteAll(records);
    }

    private void SeedGotchas(params Gotcha[] records)
    {
        var store = new JsonLineStore<Gotcha>(RepoConfig.GotchasPath(_tempDir));
        store.WriteAll(records);
    }

    private void SeedTests(params TestInventoryEntry[] records)
    {
        var store = new JsonLineStore<TestInventoryEntry>(RepoConfig.TestInventoryPath(_tempDir));
        store.WriteAll(records);
    }

    private void SeedMockRecipes(params MockRecipe[] records)
    {
        var store = new JsonLineStore<MockRecipe>(RepoConfig.MockRecipesPath(_tempDir));
        store.WriteAll(records);
    }

    [Fact]
    public void GetContext_NoData_ReturnsNullType()
    {
        var result = ContextTool.GetContext("Anything");

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
            new TypeRecord { Name = "Parser", Namespace = "Server.Parsing" }
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
            Interfaces = ["IDisposable", "IContentBase"]
        });
        SeedMockRecipes(
            new MockRecipe { Interface = "IContentBase", Namespace = "Server.Content", Recipe = "mock setup code" },
            new MockRecipe { Interface = "ILogger", Namespace = "Microsoft.Extensions.Logging", Recipe = "logger mock" }
        );

        var result = ContextTool.GetContext("AuditEntry");

        var doc = JsonDocument.Parse(result);
        var mockRecipes = doc.RootElement.GetProperty("mockRecipes");
        Assert.Equal(1, mockRecipes.GetArrayLength());
        Assert.Contains("IContentBase", mockRecipes[0].GetProperty("interface").GetString());
    }

    [Fact]
    public void GetContext_NoInterfaces_MockRecipesEmpty()
    {
        SeedTypes(new TypeRecord { Name = "SimpleClass", Namespace = "App", Interfaces = [] });
        SeedMockRecipes(
            new MockRecipe { Interface = "IContentBase", Namespace = "Server", Recipe = "code" }
        );

        var result = ContextTool.GetContext("SimpleClass");

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

        var result = ContextTool.GetContext("PlainClass");

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
            Interfaces = ["IContentBase"],
            Properties = [new PropertyRecord { Name = "Id", ClrType = "int", HasSet = true }]
        });
        SeedGotchas(new Gotcha { Type = "AuditEntry", Category = "bug", Description = "watch out", Date = "2025-01-01" });
        SeedTests(new TestInventoryEntry { Class = "AuditEntry", TestCount = 2, TestMethods = ["A", "B"] });
        SeedMockRecipes(new MockRecipe { Interface = "IContentBase", Namespace = "Server", Recipe = "setup" });

        var result = ContextTool.GetContext("AuditEntry");

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
            Interfaces = ["IJobOutputInstance"]
        });
        SeedMockRecipes(
            new MockRecipe { Interface = "IJobOutputInstance", Namespace = "Server", Recipe = "job output mock" }
        );

        var result = ContextTool.GetContext("MyService");

        var doc = JsonDocument.Parse(result);
        Assert.Equal(1, doc.RootElement.GetProperty("mockRecipes").GetArrayLength());
    }

}
