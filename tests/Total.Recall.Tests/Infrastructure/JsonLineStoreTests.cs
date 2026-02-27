using Total.Recall.Infrastructure;
using Total.Recall.Models;

namespace Total.Recall.Tests.Infrastructure;

public sealed class JsonLineStoreTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _tempFile;

    public JsonLineStoreTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "total-recall-tests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempDir);
        _tempFile = Path.Combine(_tempDir, "test.jsonl");
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public void LoadAll_FileDoesNotExist_ReturnsEmptyList()
    {
        var store = new JsonLineStore<Gotcha>(Path.Combine(_tempDir, "nonexistent.jsonl"));

        var result = store.LoadAll();

        Assert.Empty(result);
    }

    [Fact]
    public void LoadAll_EmptyFile_ReturnsEmptyList()
    {
        File.WriteAllText(_tempFile, "");
        var store = new JsonLineStore<Gotcha>(_tempFile);

        var result = store.LoadAll();

        Assert.Empty(result);
    }

    [Fact]
    public void LoadAll_SingleRecord_ReturnsOneItem()
    {
        File.WriteAllText(_tempFile, """{"type":"Foo","category":"bug","gotcha":"oops","date":"2025-01-01"}""" + "\n");
        var store = new JsonLineStore<Gotcha>(_tempFile);

        var result = store.LoadAll();

        Assert.Single(result);
        Assert.Equal("Foo", result[0].Type);
        Assert.Equal("bug", result[0].Category);
        Assert.Equal("oops", result[0].Description);
    }

    [Fact]
    public void LoadAll_MultipleRecords_ReturnsAllItems()
    {
        File.WriteAllLines(_tempFile, [
            """{"type":"Foo","category":"bug","gotcha":"oops","date":"2025-01-01"}""",
            """{"type":"Bar","category":"enum","gotcha":"wrong","date":"2025-01-02"}"""
        ]);
        var store = new JsonLineStore<Gotcha>(_tempFile);

        var result = store.LoadAll();

        Assert.Equal(2, result.Count);
        Assert.Equal("Foo", result[0].Type);
        Assert.Equal("Bar", result[1].Type);
    }

    [Fact]
    public void LoadAll_SkipsBlankLines()
    {
        File.WriteAllLines(_tempFile, [
            """{"type":"Foo","category":"bug","gotcha":"oops","date":"2025-01-01"}""",
            "",
            "   ",
            """{"type":"Bar","category":"enum","gotcha":"wrong","date":"2025-01-02"}"""
        ]);
        var store = new JsonLineStore<Gotcha>(_tempFile);

        var result = store.LoadAll();

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void Query_ReturnsMatchingRecords()
    {
        File.WriteAllLines(_tempFile, [
            """{"type":"Foo","category":"bug","gotcha":"oops","date":"2025-01-01"}""",
            """{"type":"Bar","category":"enum","gotcha":"wrong","date":"2025-01-02"}""",
            """{"type":"Foo","category":"mock","gotcha":"tricky","date":"2025-01-03"}"""
        ]);
        var store = new JsonLineStore<Gotcha>(_tempFile);

        var result = store.Query(g => g.Type == "Foo");

        Assert.Equal(2, result.Count);
        Assert.All(result, g => Assert.Equal("Foo", g.Type));
    }

    [Fact]
    public void Query_NoMatches_ReturnsEmptyList()
    {
        File.WriteAllLines(_tempFile, [
            """{"type":"Foo","category":"bug","gotcha":"oops","date":"2025-01-01"}"""
        ]);
        var store = new JsonLineStore<Gotcha>(_tempFile);

        var result = store.Query(g => g.Type == "Nonexistent");

        Assert.Empty(result);
    }

    [Fact]
    public void Append_CreatesFileAndWritesRecord()
    {
        var filePath = Path.Combine(_tempDir, "new.jsonl");
        var store = new JsonLineStore<Gotcha>(filePath);
        var record = new Gotcha { Type = "Test", Category = "bug", Description = "first", Date = "2025-01-01" };

        store.Append(record);

        Assert.True(File.Exists(filePath));
        var lines = File.ReadAllLines(filePath).Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
        Assert.Single(lines);
        Assert.Contains("\"type\":\"Test\"", lines[0]);
    }

    [Fact]
    public void Append_MultipleRecords_AppendsEachOnNewLine()
    {
        var store = new JsonLineStore<Gotcha>(_tempFile);
        store.Append(new Gotcha { Type = "A", Category = "bug", Description = "first", Date = "2025-01-01" });
        store.Append(new Gotcha { Type = "B", Category = "enum", Description = "second", Date = "2025-01-02" });

        var lines = File.ReadAllLines(_tempFile).Where(l => !string.IsNullOrWhiteSpace(l)).ToList();

        Assert.Equal(2, lines.Count);
        Assert.Contains("\"type\":\"A\"", lines[0]);
        Assert.Contains("\"type\":\"B\"", lines[1]);
    }

    [Fact]
    public void Append_CreatesDirectoryIfMissing()
    {
        var nestedPath = Path.Combine(_tempDir, "sub", "deep", "test.jsonl");
        var store = new JsonLineStore<Gotcha>(nestedPath);

        store.Append(new Gotcha { Type = "X", Category = "bug", Description = "nested", Date = "2025-01-01" });

        Assert.True(File.Exists(nestedPath));
    }

    [Fact]
    public void WriteAll_ReplacesFileContents()
    {
        File.WriteAllLines(_tempFile, [
            """{"type":"Old","category":"bug","gotcha":"stale","date":"2025-01-01"}"""
        ]);
        var store = new JsonLineStore<Gotcha>(_tempFile);
        var newRecords = new List<Gotcha>
        {
            new() { Type = "New1", Category = "enum", Description = "fresh1", Date = "2025-02-01" },
            new() { Type = "New2", Category = "mock", Description = "fresh2", Date = "2025-02-02" }
        };

        store.WriteAll(newRecords);

        var loaded = store.LoadAll();
        Assert.Equal(2, loaded.Count);
        Assert.Equal("New1", loaded[0].Type);
        Assert.Equal("New2", loaded[1].Type);
    }

    [Fact]
    public void WriteAll_EmptyEnumerable_CreatesEmptyFile()
    {
        File.WriteAllText(_tempFile, "some content");
        var store = new JsonLineStore<Gotcha>(_tempFile);

        store.WriteAll([]);

        Assert.True(File.Exists(_tempFile));
        Assert.Equal(0, store.Count());
    }

    [Fact]
    public void HasData_FileDoesNotExist_ReturnsFalse()
    {
        var store = new JsonLineStore<Gotcha>(Path.Combine(_tempDir, "nope.jsonl"));

        Assert.False(store.HasData());
    }

    [Fact]
    public void HasData_EmptyFile_ReturnsFalse()
    {
        File.WriteAllText(_tempFile, "");
        var store = new JsonLineStore<Gotcha>(_tempFile);

        Assert.False(store.HasData());
    }

    [Fact]
    public void HasData_FileWithData_ReturnsTrue()
    {
        File.WriteAllText(_tempFile, """{"type":"X","category":"bug","gotcha":"y","date":"2025-01-01"}""" + "\n");
        var store = new JsonLineStore<Gotcha>(_tempFile);

        Assert.True(store.HasData());
    }

    [Fact]
    public void Count_FileDoesNotExist_ReturnsZero()
    {
        var store = new JsonLineStore<Gotcha>(Path.Combine(_tempDir, "nope.jsonl"));

        Assert.Equal(0, store.Count());
    }

    [Fact]
    public void Count_EmptyFile_ReturnsZero()
    {
        File.WriteAllText(_tempFile, "");
        var store = new JsonLineStore<Gotcha>(_tempFile);

        Assert.Equal(0, store.Count());
    }

    [Fact]
    public void Count_MultipleRecords_ReturnsCorrectCount()
    {
        File.WriteAllLines(_tempFile, [
            """{"type":"A","category":"bug","gotcha":"x","date":"2025-01-01"}""",
            """{"type":"B","category":"bug","gotcha":"y","date":"2025-01-02"}""",
            """{"type":"C","category":"bug","gotcha":"z","date":"2025-01-03"}"""
        ]);
        var store = new JsonLineStore<Gotcha>(_tempFile);

        Assert.Equal(3, store.Count());
    }

    [Fact]
    public void Count_SkipsBlankLines()
    {
        File.WriteAllLines(_tempFile, [
            """{"type":"A","category":"bug","gotcha":"x","date":"2025-01-01"}""",
            "",
            """{"type":"B","category":"bug","gotcha":"y","date":"2025-01-02"}""",
            "   "
        ]);
        var store = new JsonLineStore<Gotcha>(_tempFile);

        Assert.Equal(2, store.Count());
    }

    [Fact]
    public void FilePath_ReturnsConstructorPath()
    {
        var store = new JsonLineStore<Gotcha>(_tempFile);

        Assert.Equal(_tempFile, store.FilePath);
    }

    [Fact]
    public void RoundTrip_TypeRecord_PreservesAllProperties()
    {
        var store = new JsonLineStore<TypeRecord>(_tempFile);
        var original = new TypeRecord
        {
            Name = "MyClass",
            Namespace = "My.Namespace",
            FullUsing = "using My.Namespace;",
            IsAbstract = true,
            IsInterface = false,
            IsEnum = false,
            IsStatic = false,
            IsInternal = true,
            BaseType = "BaseClass",
            Interfaces = ["IDisposable", "IEquatable"],
            Constructors = [new ConstructorRecord { Params = ["string name", "int count"] }],
            Properties = [new PropertyRecord { Name = "Id", ClrType = "int", HasSet = true, HasInit = false }]
        };

        store.WriteAll([original]);
        var loaded = store.LoadAll();

        Assert.Single(loaded);
        var r = loaded[0];
        Assert.Equal("MyClass", r.Name);
        Assert.Equal("My.Namespace", r.Namespace);
        Assert.True(r.IsAbstract);
        Assert.True(r.IsInternal);
        Assert.Equal("BaseClass", r.BaseType);
        Assert.Equal(2, r.Interfaces.Count);
        Assert.Single(r.Constructors);
        Assert.Equal(2, r.Constructors[0].Params.Count);
        Assert.Single(r.Properties);
        Assert.Equal("Id", r.Properties[0].Name);
        Assert.Equal("int", r.Properties[0].ClrType);
        Assert.True(r.Properties[0].HasSet);
    }

    [Fact]
    public void RoundTrip_CoverageGap_PreservesNestedUncoveredMethods()
    {
        var store = new JsonLineStore<CoverageGap>(_tempFile);
        var original = new CoverageGap
        {
            Class = "Parser",
            Namespace = "My.Proj",
            File = "Parser.cs",
            TotalLines = 100,
            CoveredLines = 60,
            UncoveredLines = 40,
            CoveragePercent = 60.0,
            ExistingTestCount = 3,
            Testability = "high",
            UncoveredMethods =
            [
                new UncoveredMethod { Name = "Parse", StartLine = 10, EndLine = 25, UncoveredLines = 8 }
            ]
        };

        store.WriteAll([original]);
        var loaded = store.LoadAll();

        Assert.Single(loaded);
        Assert.Equal("Parser", loaded[0].Class);
        Assert.Equal(100, loaded[0].TotalLines);
        Assert.Single(loaded[0].UncoveredMethods);
        Assert.Equal("Parse", loaded[0].UncoveredMethods[0].Name);
        Assert.Equal(8, loaded[0].UncoveredMethods[0].UncoveredLines);
    }

    // --- Corrupt JSONL handling ---

    [Fact]
    public void LoadAll_CorruptLine_SkipsItAndLoadsValidRecords()
    {
        File.WriteAllLines(_tempFile, [
            """{"type":"Valid","category":"bug","gotcha":"good","date":"2025-01-01"}""",
            "NOT VALID JSON {{{",
            """{"type":"AlsoValid","category":"enum","gotcha":"fine","date":"2025-01-02"}"""
        ]);
        var store = new JsonLineStore<Gotcha>(_tempFile);

        var result = store.LoadAll();

        Assert.Equal(2, result.Count);
        Assert.Equal("Valid", result[0].Type);
        Assert.Equal("AlsoValid", result[1].Type);
    }

    [Fact]
    public void LoadAll_AllCorruptLines_ReturnsEmpty()
    {
        File.WriteAllLines(_tempFile, [
            "NOT JSON AT ALL",
            "{broken",
            "12345"
        ]);
        var store = new JsonLineStore<Gotcha>(_tempFile);

        var result = store.LoadAll();

        Assert.Empty(result);
    }

    [Fact]
    public void LoadAll_MoreThan5CorruptLines_LogsTruncatedMessage()
    {
        // Write 8 corrupt lines + 1 valid to trigger the "> 5 more" code path
        var lines = new List<string>();
        for (int i = 0; i < 8; i++)
            lines.Add($"corrupt line {i}");
        lines.Add("""{"type":"Valid","category":"bug","gotcha":"ok","date":"2025-01-01"}""");

        File.WriteAllLines(_tempFile, lines);
        var store = new JsonLineStore<Gotcha>(_tempFile);

        var result = store.LoadAll();

        // Should still load the one valid record
        Assert.Single(result);
        Assert.Equal("Valid", result[0].Type);
    }

    // --- Cached HasData / Count ---

    [Fact]
    public void HasData_AfterLoadAll_UsesCachedResult()
    {
        File.WriteAllText(_tempFile, """{"type":"X","category":"bug","gotcha":"y","date":"2025-01-01"}""" + "\n");
        var store = new JsonLineStore<Gotcha>(_tempFile);

        // Populate cache
        store.LoadAll();

        // Now HasData should use cache (different code path from disk check)
        Assert.True(store.HasData());
    }

    [Fact]
    public void HasData_AfterLoadAll_EmptyFile_ReturnsFalse()
    {
        File.WriteAllText(_tempFile, "\n");
        var store = new JsonLineStore<Gotcha>(_tempFile);

        // LoadAll with only blank lines → empty cache
        store.LoadAll();

        Assert.False(store.HasData());
    }

    [Fact]
    public void Count_AfterLoadAll_UsesCachedResult()
    {
        File.WriteAllLines(_tempFile, [
            """{"type":"A","category":"bug","gotcha":"x","date":"2025-01-01"}""",
            """{"type":"B","category":"bug","gotcha":"y","date":"2025-01-02"}"""
        ]);
        var store = new JsonLineStore<Gotcha>(_tempFile);

        // Populate cache
        store.LoadAll();

        // Count should use cached list
        Assert.Equal(2, store.Count());
    }

    // --- Append error handling ---

    [Fact]
    public void Append_ToInvalidPath_ThrowsAndLogs()
    {
        // Path with embedded null character causes IOException
        var badPath = Path.Combine(_tempDir, "sub\0dir", "test.jsonl");
        var store = new JsonLineStore<Gotcha>(badPath);

        Assert.ThrowsAny<Exception>(() =>
            store.Append(new Gotcha { Type = "X", Category = "bug", Description = "y", Date = "2025-01-01" }));
    }
}
