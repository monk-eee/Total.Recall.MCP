using Total.Recall.Infrastructure;
using Total.Recall.Models;
using Total.Recall.Tools;

namespace Total.Recall.Tests.Infrastructure;

/// <summary>
/// Tests for namespace support — multi-namespace stores,
/// RepoConfig namespace resolution, and tool namespace parameter.
/// </summary>
[Collection("ToolTests")]
public sealed class NamespaceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string? _originalEnv;
    private readonly string? _originalNsEnv;

    public NamespaceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "total-recall-tests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempDir);
        _originalEnv = Environment.GetEnvironmentVariable(RepoConfig.EnvVarName);
        _originalNsEnv = Environment.GetEnvironmentVariable(RepoConfig.NamespaceEnvVar);
        Environment.SetEnvironmentVariable(RepoConfig.EnvVarName, _tempDir);
        Environment.SetEnvironmentVariable(RepoConfig.NamespaceEnvVar, null);
        StoreRegistry.Reset();
    }

    public void Dispose()
    {
        StoreRegistry.Reset();
        Environment.SetEnvironmentVariable(RepoConfig.EnvVarName, _originalEnv);
        Environment.SetEnvironmentVariable(RepoConfig.NamespaceEnvVar, _originalNsEnv);
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    // ── RepoConfig.GetNamespacePath ──

    [Fact]
    public void GetNamespacePath_NoEnvNamespace_ReturnsRootDirectly()
    {
        var result = RepoConfig.GetNamespacePath();

        Assert.Equal(Path.GetFullPath(_tempDir), result);
    }

    [Fact]
    public void GetNamespacePath_ExplicitNamespace_ReturnsSubdirectory()
    {
        var result = RepoConfig.GetNamespacePath("linter");

        Assert.Equal(Path.GetFullPath(Path.Combine(_tempDir, "linter")), result);
    }

    [Fact]
    public void GetNamespacePath_WithEnvNamespace_ReturnsSubdirectory()
    {
        Environment.SetEnvironmentVariable(RepoConfig.NamespaceEnvVar, "my-project");
        StoreRegistry.Reset(); // clear cached namespace

        var result = RepoConfig.GetNamespacePath();

        Assert.Equal(Path.GetFullPath(Path.Combine(_tempDir, "my-project")), result);
    }

    [Fact]
    public void GetNamespacePath_LegacyLayout_ReturnsRoot()
    {
        // Create .jsonl file in root (legacy layout)
        File.WriteAllText(Path.Combine(_tempDir, "type-registry.jsonl"), "{}");
        Environment.SetEnvironmentVariable(RepoConfig.NamespaceEnvVar, "linter");
        StoreRegistry.Reset();

        // Even with env namespace set, legacy layout should return root
        var result = RepoConfig.GetNamespacePath();

        Assert.Equal(Path.GetFullPath(_tempDir), result);
    }

    [Fact]
    public void GetNamespacePath_ExplicitNs_OverridesLegacy()
    {
        // Create .jsonl file in root (legacy layout)
        File.WriteAllText(Path.Combine(_tempDir, "type-registry.jsonl"), "{}");

        // Explicit ns parameter always creates subdirectory, even with legacy data in root
        var result = RepoConfig.GetNamespacePath("other");

        Assert.Equal(Path.GetFullPath(Path.Combine(_tempDir, "other")), result);
    }

    // ── RepoConfig.IsLegacyLayout ──

    [Fact]
    public void IsLegacyLayout_EmptyDir_ReturnsFalse()
    {
        Assert.False(RepoConfig.IsLegacyLayout(_tempDir));
    }

    [Fact]
    public void IsLegacyLayout_WithJsonlFiles_ReturnsTrue()
    {
        File.WriteAllText(Path.Combine(_tempDir, "gotchas.jsonl"), "{}");

        Assert.True(RepoConfig.IsLegacyLayout(_tempDir));
    }

    [Fact]
    public void IsLegacyLayout_NonExistentDir_ReturnsFalse()
    {
        Assert.False(RepoConfig.IsLegacyLayout(Path.Combine(_tempDir, "nope")));
    }

    // ── RepoConfig.ListNamespaces ──

    [Fact]
    public void ListNamespaces_EmptyRoot_ReturnsEmpty()
    {
        var result = RepoConfig.ListNamespaces();

        Assert.Empty(result);
    }

    [Fact]
    public void ListNamespaces_LegacyLayout_ReturnsRootName()
    {
        File.WriteAllText(Path.Combine(_tempDir, "gotchas.jsonl"), "{}");

        var result = RepoConfig.ListNamespaces();

        Assert.Single(result);
        Assert.Equal(Path.GetFileName(_tempDir), result[0]);
    }

    [Fact]
    public void ListNamespaces_MultipleNamespaces_ReturnsAll()
    {
        var ns1 = Path.Combine(_tempDir, "linter");
        var ns2 = Path.Combine(_tempDir, "docs-build");
        var ns3 = Path.Combine(_tempDir, "empty-ns"); // has no .jsonl files
        Directory.CreateDirectory(ns1);
        Directory.CreateDirectory(ns2);
        Directory.CreateDirectory(ns3);
        File.WriteAllText(Path.Combine(ns1, "type-registry.jsonl"), "{}");
        File.WriteAllText(Path.Combine(ns2, "gotchas.jsonl"), "{}");

        var result = RepoConfig.ListNamespaces();

        Assert.Equal(2, result.Count);
        Assert.Contains("docs-build", result);
        Assert.Contains("linter", result);
        Assert.DoesNotContain("empty-ns", result);
    }

    // ── RepoConfig.GetDefaultNamespace ──

    [Fact]
    public void GetDefaultNamespace_NoEnv_ReturnsDefault()
    {
        var result = RepoConfig.GetDefaultNamespace();

        Assert.Equal("default", result);
    }

    [Fact]
    public void GetDefaultNamespace_WithEnv_ReturnsEnvValue()
    {
        Environment.SetEnvironmentVariable(RepoConfig.NamespaceEnvVar, "custom-ns");
        StoreRegistry.Reset();

        var result = RepoConfig.GetDefaultNamespace();

        Assert.Equal("custom-ns", result);
    }

    // ── StoreRegistry multi-namespace isolation ──

    [Fact]
    public void ForNamespace_DifferentNamespaces_ReturnDifferentStores()
    {
        var ns1Dir = Path.Combine(_tempDir, "ns1");
        var ns2Dir = Path.Combine(_tempDir, "ns2");
        Directory.CreateDirectory(ns1Dir);
        Directory.CreateDirectory(ns2Dir);

        var stores1 = StoreRegistry.ForNamespace("ns1");
        var stores2 = StoreRegistry.ForNamespace("ns2");

        Assert.NotSame(stores1, stores2);
        Assert.NotSame(stores1.TypeRegistry, stores2.TypeRegistry);
    }

    [Fact]
    public void ForNamespace_SameNamespace_ReturnsSameStores()
    {
        var stores1 = StoreRegistry.ForNamespace("linter");
        var stores2 = StoreRegistry.ForNamespace("linter");

        Assert.Same(stores1, stores2);
    }

    [Fact]
    public void ForNamespace_DataIsolation_NamespacesDoNotLeakData()
    {
        // Create two namespace dirs with different data
        var ns1Dir = Path.Combine(_tempDir, "ns1");
        var ns2Dir = Path.Combine(_tempDir, "ns2");
        Directory.CreateDirectory(ns1Dir);
        Directory.CreateDirectory(ns2Dir);

        var store1 = new JsonLineStore<Gotcha>(RepoConfig.GotchasPath(ns1Dir));
        store1.WriteAll([new Gotcha { Type = "TypeA", Category = "bug", Description = "ns1 gotcha", Date = "2025-01-01" }]);

        var store2 = new JsonLineStore<Gotcha>(RepoConfig.GotchasPath(ns2Dir));
        store2.WriteAll([new Gotcha { Type = "TypeB", Category = "bug", Description = "ns2 gotcha", Date = "2025-01-01" }]);

        // Query via StoreRegistry
        var gotchas1 = StoreRegistry.ForNamespace("ns1").Gotchas.LoadAll();
        var gotchas2 = StoreRegistry.ForNamespace("ns2").Gotchas.LoadAll();

        Assert.Single(gotchas1);
        Assert.Equal("TypeA", gotchas1[0].Type);
        Assert.Single(gotchas2);
        Assert.Equal("TypeB", gotchas2[0].Type);
    }

    // ── Tool namespace parameter ──

    [Fact]
    public void GotchaTool_WithNamespaceParam_WritesToCorrectNamespace()
    {
        var nsDir = Path.Combine(_tempDir, "project-a");
        Directory.CreateDirectory(nsDir);

        GotchaTool.AddGotcha("Widget", "bug", "Something wrong", ns: "project-a");

        // Verify data is in the namespace directory
        var store = new JsonLineStore<Gotcha>(RepoConfig.GotchasPath(nsDir));
        var all = store.LoadAll();
        Assert.Single(all);
        Assert.Equal("Widget", all[0].Type);

        // Default namespace should not have the data
        var defaultStore = new JsonLineStore<Gotcha>(RepoConfig.GotchasPath(_tempDir));
        Assert.False(defaultStore.HasData());
    }

    [Fact]
    public void GotchaTool_GetGotchas_ReadsFromCorrectNamespace()
    {
        var nsDir = Path.Combine(_tempDir, "project-b");
        Directory.CreateDirectory(nsDir);
        var store = new JsonLineStore<Gotcha>(RepoConfig.GotchasPath(nsDir));
        store.WriteAll([new Gotcha { Type = "MyType", Category = "mock", Description = "test gotcha", Date = "2025-01-01" }]);

        var result = GotchaTool.GetGotchas("MyType", ns: "project-b");

        Assert.Contains("test gotcha", result);
    }

    [Fact]
    public void AssessmentTool_WithNamespaceParam_WritesToCorrectNamespace()
    {
        var nsDir = Path.Combine(_tempDir, "test-ns");
        Directory.CreateDirectory(nsDir);

        AssessmentTool.AddAssessment("MyClass", "testable", "OK", ns: "test-ns");

        var store = new JsonLineStore<Assessment>(RepoConfig.AssessmentsPath(nsDir));
        var all = store.LoadAll();
        Assert.Single(all);
        Assert.Equal("MyClass", all[0].Class);
    }

    // ── RepoConfig.AssessmentsPath ──

    [Fact]
    public void AssessmentsPath_CombinesCorrectly()
    {
        var result = RepoConfig.AssessmentsPath(@"C:\data");

        Assert.Equal(Path.Combine(@"C:\data", "assessments.jsonl"), result);
    }

    // ── NamespaceStores properties ──

    [Fact]
    public void NamespaceStores_HasAllStoreProperties()
    {
        var stores = StoreRegistry.ForNamespace("test");

        Assert.NotNull(stores.TypeRegistry);
        Assert.NotNull(stores.CoverageGaps);
        Assert.NotNull(stores.TestInventory);
        Assert.NotNull(stores.Gotchas);
        Assert.NotNull(stores.MockRecipes);
        Assert.NotNull(stores.Assessments);
        Assert.Equal("test", stores.Name);
    }

    [Fact]
    public void NamespaceStores_GetTypeIndex_ReturnsEmptyWhenNoData()
    {
        var stores = StoreRegistry.ForNamespace("empty-ns");

        var (exact, ci) = stores.GetTypeIndex();

        Assert.Empty(exact);
        Assert.Empty(ci);
    }

    [Fact]
    public void NamespaceStores_GetTypeIndex_BuildsIndexFromData()
    {
        var nsDir = Path.Combine(_tempDir, "indexed");
        Directory.CreateDirectory(nsDir);
        var store = new JsonLineStore<TypeRecord>(RepoConfig.TypeRegistryPath(nsDir));
        store.WriteAll([
            new TypeRecord { Name = "ClassA", Namespace = "App" },
            new TypeRecord { Name = "ClassB", Namespace = "App.Services" }
        ]);

        var (exact, _) = StoreRegistry.ForNamespace("indexed").GetTypeIndex();

        Assert.Equal(2, exact.Count);
        Assert.True(exact.ContainsKey("ClassA"));
        Assert.True(exact.ContainsKey("ClassB"));
    }
}
