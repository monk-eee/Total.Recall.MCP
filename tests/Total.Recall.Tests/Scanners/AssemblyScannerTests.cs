using Total.Recall.Infrastructure;
using Total.Recall.Models;
using Total.Recall.Scanners;

namespace Total.Recall.Tests.Scanners;

public sealed class AssemblyScannerTests : IDisposable
{
    private readonly string _tempDir;

    // Path to our own built assembly — guaranteed to exist after build
    private static readonly string s_assemblyPath = Path.Combine(
        AppContext.BaseDirectory, "Total.Recall.dll");

    public AssemblyScannerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "total-recall-tests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public void Scan_FileNotFound_ThrowsFileNotFoundException()
    {
        Assert.Throws<FileNotFoundException>(
            () => AssemblyScanner.Scan(Path.Combine(_tempDir, "nonexistent.dll"), _tempDir));
    }

    [Fact]
    public void Scan_OwnAssembly_ReturnsNonZeroTypeCount()
    {
        var count = AssemblyScanner.Scan(s_assemblyPath, _tempDir);

        Assert.True(count > 0, $"Expected types > 0, got {count}");
    }

    [Fact]
    public void Scan_OwnAssembly_CreatesTypeRegistryFile()
    {
        AssemblyScanner.Scan(s_assemblyPath, _tempDir);

        var outputPath = RepoConfig.TypeRegistryPath(_tempDir);
        Assert.True(File.Exists(outputPath));
        var lines = File.ReadAllLines(outputPath).Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
        Assert.True(lines.Count > 0);
    }

    [Fact]
    public void Scan_OwnAssembly_FindsKnownTypes()
    {
        AssemblyScanner.Scan(s_assemblyPath, _tempDir);

        var store = new JsonLineStore<TypeRecord>(RepoConfig.TypeRegistryPath(_tempDir));
        var all = store.LoadAll();
        var typeNames = all.Select(t => t.Name).ToHashSet();

        // These types definitely exist in Total.Recall
        Assert.Contains("TypeRecord", typeNames);
        Assert.Contains("CoverageGap", typeNames);
        Assert.Contains("Gotcha", typeNames);
        Assert.Contains("MockRecipe", typeNames);
        Assert.Contains("TestInventoryEntry", typeNames);
        Assert.Contains("RepoConfig", typeNames);
    }

    [Fact]
    public void Scan_OwnAssembly_ExtractsNamespaces()
    {
        AssemblyScanner.Scan(s_assemblyPath, _tempDir);

        var store = new JsonLineStore<TypeRecord>(RepoConfig.TypeRegistryPath(_tempDir));
        var all = store.LoadAll();

        var typeRecord = all.First(t => t.Name == "TypeRecord");
        Assert.Equal("Total.Recall.Models", typeRecord.Namespace);
        Assert.Equal("using Total.Recall.Models;", typeRecord.FullUsing);
    }

    [Fact]
    public void Scan_OwnAssembly_ExtractsProperties()
    {
        AssemblyScanner.Scan(s_assemblyPath, _tempDir);

        var store = new JsonLineStore<TypeRecord>(RepoConfig.TypeRegistryPath(_tempDir));
        var all = store.LoadAll();

        var typeRecord = all.First(t => t.Name == "TypeRecord");
        Assert.True(typeRecord.Properties.Count > 0);
        var nameProp = typeRecord.Properties.FirstOrDefault(p => p.Name == "Name");
        Assert.NotNull(nameProp);
        Assert.Equal("string", nameProp.ClrType);
        Assert.True(nameProp.HasSet);
    }

    [Fact]
    public void Scan_OwnAssembly_DetectsStaticClasses()
    {
        AssemblyScanner.Scan(s_assemblyPath, _tempDir);

        var store = new JsonLineStore<TypeRecord>(RepoConfig.TypeRegistryPath(_tempDir));
        var all = store.LoadAll();

        var repoConfig = all.First(t => t.Name == "RepoConfig");
        Assert.True(repoConfig.IsStatic);
        Assert.False(repoConfig.IsAbstract); // Static should not be reported as abstract
    }

    [Fact]
    public void Scan_OwnAssembly_DetectsInterfaces()
    {
        AssemblyScanner.Scan(s_assemblyPath, _tempDir);

        var store = new JsonLineStore<TypeRecord>(RepoConfig.TypeRegistryPath(_tempDir));
        var all = store.LoadAll();

        // TypeRecord has no interfaces declared on-type
        var typeRecord = all.First(t => t.Name == "TypeRecord");
        Assert.False(typeRecord.IsInterface);
    }

    [Fact]
    public void Scan_OwnAssembly_FiltersCompilerGeneratedTypes()
    {
        AssemblyScanner.Scan(s_assemblyPath, _tempDir);

        var store = new JsonLineStore<TypeRecord>(RepoConfig.TypeRegistryPath(_tempDir));
        var all = store.LoadAll();

        // No types should start with < or contain __
        Assert.All(all, t =>
        {
            Assert.False(t.Name.StartsWith('<'), $"Compiler-generated type leaked: {t.Name}");
            Assert.False(t.Name.Contains("__"), $"Compiler-generated type leaked: {t.Name}");
            Assert.False(t.Name.Contains("DisplayClass"), $"Compiler-generated type leaked: {t.Name}");
        });
    }

    [Fact]
    public void Scan_OwnAssembly_ExtractsConstructors()
    {
        AssemblyScanner.Scan(s_assemblyPath, _tempDir);

        var store = new JsonLineStore<TypeRecord>(RepoConfig.TypeRegistryPath(_tempDir));
        var all = store.LoadAll();

        // TypeRecord should have a parameterless constructor
        var typeRecord = all.First(t => t.Name == "TypeRecord");
        Assert.True(typeRecord.Constructors.Count > 0, "TypeRecord should have at least one constructor");
    }

    [Fact]
    public void Scan_OwnAssembly_FormatsCSharpTypeAliases()
    {
        AssemblyScanner.Scan(s_assemblyPath, _tempDir);

        var store = new JsonLineStore<TypeRecord>(RepoConfig.TypeRegistryPath(_tempDir));
        var all = store.LoadAll();

        // CoverageGap.TotalLines is int, CoverageGap.CoveragePercent is double
        var coverageGap = all.First(t => t.Name == "CoverageGap");
        var totalLines = coverageGap.Properties.FirstOrDefault(p => p.Name == "TotalLines");
        Assert.NotNull(totalLines);
        Assert.Equal("int", totalLines.ClrType);

        var covPct = coverageGap.Properties.FirstOrDefault(p => p.Name == "CoveragePercent");
        Assert.NotNull(covPct);
        Assert.Equal("double", covPct.ClrType);
    }

    [Fact]
    public void Scan_OwnAssembly_HandlesGenericTypes()
    {
        AssemblyScanner.Scan(s_assemblyPath, _tempDir);

        var store = new JsonLineStore<TypeRecord>(RepoConfig.TypeRegistryPath(_tempDir));
        var all = store.LoadAll();

        // TypeRecord.Constructors is List<ConstructorRecord> — check the property type contains generic info
        var typeRecord = all.First(t => t.Name == "TypeRecord");
        var ctorsProp = typeRecord.Properties.FirstOrDefault(p => p.Name == "Constructors");
        Assert.NotNull(ctorsProp);
        Assert.Contains("List", ctorsProp.ClrType);
    }

    [Fact]
    public void Scan_OwnAssembly_FiltersTypesWithoutNamespace()
    {
        AssemblyScanner.Scan(s_assemblyPath, _tempDir);

        var store = new JsonLineStore<TypeRecord>(RepoConfig.TypeRegistryPath(_tempDir));
        var all = store.LoadAll();

        // All types should have a non-empty namespace
        Assert.All(all, t => Assert.False(string.IsNullOrEmpty(t.Namespace)));
    }

    // -----------------------------------------------------------------------
    // schemaVersion / kind / lang block (v2.5 sibling-scanner contract)
    // -----------------------------------------------------------------------

    /// <summary>
    /// Regression test: every record emitted by the .NET scanner must carry
    /// <c>schemaVersion = 1</c>. This pins the on-disk contract shared by the
    /// Python and TypeScript sibling scanners — the MCP server reads any
    /// scanner's output through the same TypeRecord deserializer, so an
    /// accidental zero / unset on .NET would mask compatibility regressions.
    /// </summary>
    [Fact]
    public void Scan_OwnAssembly_StampsSchemaVersionOneOnEveryRecord()
    {
        AssemblyScanner.Scan(s_assemblyPath, _tempDir);

        var store = new JsonLineStore<TypeRecord>(RepoConfig.TypeRegistryPath(_tempDir));
        var all = store.LoadAll();

        Assert.NotEmpty(all);
        Assert.All(all, t => Assert.Equal(1, t.SchemaVersion));
    }

    /// <summary>
    /// Regression test: the language-agnostic <c>kind</c> discriminator must
    /// be set correctly for the four .NET shapes. Cross-language consumers
    /// (e.g. MCP tools that filter by kind) rely on this string being one of
    /// <c>class | interface | enum | struct</c> for .NET output.
    /// </summary>
    [Theory]
    [InlineData("TypeRecord", "class")]      // sealed class
    [InlineData("CoverageGap", "class")]     // plain class
    [InlineData("RepoConfig", "class")]      // static class still reports as class
    public void Scan_OwnAssembly_DerivesKindForKnownTypes(string typeName, string expectedKind)
    {
        AssemblyScanner.Scan(s_assemblyPath, _tempDir);

        var store = new JsonLineStore<TypeRecord>(RepoConfig.TypeRegistryPath(_tempDir));
        var record = store.LoadAll().First(t => t.Name == typeName);

        Assert.Equal(expectedKind, record.Kind);
    }

    /// <summary>
    /// Regression test: enums and interfaces in the scanned assembly must be
    /// tagged with the correct <c>kind</c> discriminator. Uses a discovery
    /// pattern (find-any-enum / find-any-interface) rather than hard-coding
    /// type names so the test survives renames inside Total.Recall.
    /// </summary>
    [Fact]
    public void Scan_OwnAssembly_DerivesKindForEnumsAndInterfaces()
    {
        AssemblyScanner.Scan(s_assemblyPath, _tempDir);

        var store = new JsonLineStore<TypeRecord>(RepoConfig.TypeRegistryPath(_tempDir));
        var all = store.LoadAll();

        var anyEnum = all.FirstOrDefault(t => t.IsEnum);
        if (anyEnum is not null)
            Assert.Equal("enum", anyEnum.Kind);

        var anyInterface = all.FirstOrDefault(t => t.IsInterface);
        if (anyInterface is not null)
            Assert.Equal("interface", anyInterface.Kind);
    }

    /// <summary>
    /// Regression test: every emitted record must carry the <c>lang</c> block
    /// with <c>kind = "dotnet"</c>. This is the discriminator that Python and
    /// TypeScript scanners will replace with <c>"python"</c> / <c>"typescript"</c>
    /// in their own output; the MCP server uses it to route language-specific
    /// rendering (e.g. mock-recipe templates).
    /// </summary>
    [Fact]
    public void Scan_OwnAssembly_StampsLangBlockOnEveryRecord()
    {
        AssemblyScanner.Scan(s_assemblyPath, _tempDir);

        var store = new JsonLineStore<TypeRecord>(RepoConfig.TypeRegistryPath(_tempDir));
        var all = store.LoadAll();

        Assert.NotEmpty(all);
        Assert.All(all, t =>
        {
            Assert.NotNull(t.Lang);
            Assert.Equal("dotnet", t.Lang!.Kind);
        });
    }

    /// <summary>
    /// Regression test: <see cref="AssemblyScanner.DeriveKind"/> classifies
    /// every .NET shape correctly when called directly with a real
    /// <see cref="Type"/>. Pure unit test — no MetadataLoadContext.
    /// </summary>
    [Theory]
    [InlineData(typeof(TypeRecord), "class")]
    [InlineData(typeof(IDisposable), "interface")]
    [InlineData(typeof(DayOfWeek), "enum")]
    [InlineData(typeof(int), "struct")]
    [InlineData(typeof(Guid), "struct")]
    public void DeriveKind_ClassifiesEveryDotNetShape(Type type, string expectedKind)
    {
        Assert.Equal(expectedKind, AssemblyScanner.DeriveKind(type));
    }

    /// <summary>
    /// Regression test: <see cref="AssemblyScanner.BuildLangInfo"/> reports
    /// <c>genericArity = 0</c> for closed types and the correct arity for
    /// open generic definitions. Cross-language scanners use this field to
    /// decide whether to surface generic placeholders in mock recipes.
    /// </summary>
    [Theory]
    [InlineData(typeof(TypeRecord), 0)]
    [InlineData(typeof(List<>), 1)]
    [InlineData(typeof(Dictionary<,>), 2)]
    public void BuildLangInfo_ReportsGenericArity(Type type, int expectedArity)
    {
        var info = AssemblyScanner.BuildLangInfo(type);

        Assert.Equal("dotnet", info.Kind);
        Assert.Equal(expectedArity, info.GenericArity);
    }

    /// <summary>
    /// Regression test: <see cref="AssemblyScanner.BuildLangInfo"/> sets
    /// <c>isSealed = false</c> for static classes even though .NET reports
    /// <c>Type.IsSealed = true</c> on them (static = abstract + sealed). The
    /// "sealed" discriminator in the JSONL is meant to signal user intent
    /// ("you cannot inherit from this"), not the compiler's bit pattern.
    /// </summary>
    [Fact]
    public void BuildLangInfo_StaticClassNotReportedAsSealed()
    {
        var info = AssemblyScanner.BuildLangInfo(typeof(Total.Recall.Infrastructure.RepoConfig));

        Assert.False(info.IsSealed);
    }

    // -----------------------------------------------------------------------

    /// <summary>
    /// Regression test: target dirs that ship their own copy of a runtime
    /// assembly (e.g. publish output containing mscorlib.dll / System.Runtime.dll
    /// alongside the host runtime dir's copy) used to crash
    /// <see cref="AssemblyScanner.Scan(string, string)"/> with
    /// <c>FileLoadException: Assembly with same name is already loaded</c>
    /// during MetadataLoadContext core-assembly probing. The root cause was
    /// <see cref="System.Reflection.PathAssemblyResolver"/> being fed two
    /// distinct file paths that resolved to the same assembly identity.
    ///
    /// Fix: deduplicate by <see cref="AssemblyName.Name"/>, preferring the
    /// target directory's copy over the runtime dir's copy.
    ///
    /// Impact when broken: <c>scan --assembly &lt;path&gt;</c> crashed mid-scan
    /// for any publish-style target, leaving the type-registry unwritten.
    ///
    /// Contract: <see cref="AssemblyScanner.BuildResolverPaths(string)"/> must
    /// return at most one path per assembly simple-name, even when the target
    /// dir and the runtime dir both contain a copy of the same identity.
    /// </summary>
    [Fact]
    public void BuildResolverPaths_DuplicateIdentityAcrossDirs_DedupesByAssemblyName()
    {
        // Arrange: synthesize a publish-style target dir containing copies of
        // multiple framework DLLs that ALSO live in the host runtime dir.
        var targetDir = Path.Combine(_tempDir, "publish-style-target");
        Directory.CreateDirectory(targetDir);

        var copiedAssembly = Path.Combine(targetDir, Path.GetFileName(s_assemblyPath));
        File.Copy(s_assemblyPath, copiedAssembly);

        var runtimeDir = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        var duplicateCandidates = new[] { "System.Runtime.dll", "System.Collections.dll", "System.Linq.dll" }
            .Select(n => Path.Combine(runtimeDir, n))
            .Where(File.Exists)
            .ToList();

        Assert.NotEmpty(duplicateCandidates);
        foreach (var dll in duplicateCandidates)
            File.Copy(dll, Path.Combine(targetDir, Path.GetFileName(dll)));

        // Act
        var paths = AssemblyScanner.BuildResolverPaths(copiedAssembly);

        // Assert: every assembly simple-name appears at most once.
        var byName = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in paths)
        {
            var name = System.Reflection.AssemblyName.GetAssemblyName(p).Name!;
            if (!byName.TryGetValue(name, out var list))
                byName[name] = list = new List<string>();
            list.Add(p);
        }

        var duplicates = byName.Where(kv => kv.Value.Count > 1).ToList();
        Assert.Empty(duplicates);

        // And each duplicate-identity DLL we planted MUST resolve to the target dir copy.
        foreach (var src in duplicateCandidates)
        {
            var name = System.Reflection.AssemblyName.GetAssemblyName(src).Name!;
            Assert.True(byName.ContainsKey(name), $"Expected resolver to include identity '{name}'");
            Assert.StartsWith(targetDir, byName[name][0], StringComparison.OrdinalIgnoreCase);
        }

        // And the actual scan must succeed end-to-end.
        var count = AssemblyScanner.Scan(copiedAssembly, _tempDir);
        Assert.True(count > 0, $"Expected types > 0, got {count}");
    }
}
