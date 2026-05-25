using System.Text.Json;
using Total.Recall.Infrastructure;
using Total.Recall.Models;
using Total.Recall.Tools;

namespace Total.Recall.Tests.Tools;

[Collection("ToolTests")]
public sealed class SourceSnippetToolTests : ToolTestBase
{
    private readonly string _sourceDir;
    private readonly string? _originalSourceRootEnv;

    public SourceSnippetToolTests()
    {
        _sourceDir = Path.Combine(TempDir, "source-repo");
        Directory.CreateDirectory(_sourceDir);
        _originalSourceRootEnv = Environment.GetEnvironmentVariable("TOTAL_RECALL_SOURCE_ROOT");
        Environment.SetEnvironmentVariable("TOTAL_RECALL_SOURCE_ROOT", null);
        SourceSnippetTool.ResetSourceRootCache();
    }

    public override void Dispose()
    {
        SourceSnippetTool.ResetSourceRootCache();
        Environment.SetEnvironmentVariable("TOTAL_RECALL_SOURCE_ROOT", _originalSourceRootEnv);
        base.Dispose();
    }

    private void SetSourceRootEnv(string path)
    {
        Environment.SetEnvironmentVariable("TOTAL_RECALL_SOURCE_ROOT", path);
    }

    private void WriteConfigJson(string sourceRoot)
    {
        var config = new NamespaceConfig { SourceRoot = sourceRoot };
        var json = JsonSerializer.Serialize(config, SharedJsonOptions.CamelCase);
        File.WriteAllText(Path.Combine(TempDir, "config.json"), json);
    }

    private string CreateSourceFile(string relativePath, string content)
    {
        var fullPath = Path.Combine(_sourceDir, relativePath.Replace('/', Path.DirectorySeparatorChar));
        var dir = Path.GetDirectoryName(fullPath)!;
        Directory.CreateDirectory(dir);
        File.WriteAllText(fullPath, content);
        return relativePath;
    }

    // ── No source root configured ──

    [Fact]
    public void GetSourceSnippet_NoSourceRoot_ReturnsConfigError()
    {
        SeedCoverageGaps(new CoverageGap { Class = "MyClass", File = "src/MyClass.cs" });

        var result = SourceSnippetTool.GetSourceSnippet("MyClass");

        Assert.Contains("Source root not configured", result);
    }

    // ── Source root from env var ──

    [Fact]
    public void GetSourceSnippet_SourceRootFromEnv_ResolvesFile()
    {
        SetSourceRootEnv(_sourceDir);
        StoreRegistry.Reset();

        var filePath = CreateSourceFile("src/Calculator.cs", "public class Calculator { public int Add(int a, int b) => a + b; }");
        SeedCoverageGaps(new CoverageGap { Class = "Calculator", File = filePath });

        var result = SourceSnippetTool.GetSourceSnippet("Calculator");

        Assert.Contains("Calculator", result);
        Assert.Contains("public int Add", result);
    }

    // ── Source root from config.json ──

    [Fact]
    public void GetSourceSnippet_SourceRootFromConfig_ResolvesFile()
    {
        WriteConfigJson(_sourceDir);
        StoreRegistry.Reset();

        var filePath = CreateSourceFile("src/Parser.cs", "public class Parser { public void Parse() { } }");
        SeedCoverageGaps(new CoverageGap { Class = "Parser", File = filePath });

        var result = SourceSnippetTool.GetSourceSnippet("Parser");

        Assert.Contains("Parser", result);
        Assert.Contains("public void Parse", result);
    }

    // ── Source root directory not found ──

    [Fact]
    public void GetSourceSnippet_SourceRootDirNotFound_ReturnsError()
    {
        SetSourceRootEnv(Path.Combine(TempDir, "does-not-exist"));
        StoreRegistry.Reset();

        SeedCoverageGaps(new CoverageGap { Class = "X", File = "src/X.cs" });

        var result = SourceSnippetTool.GetSourceSnippet("X");

        Assert.Contains("Source root directory not found", result);
    }

    // ── No coverage data ──

    [Fact]
    public void GetSourceSnippet_NoCoverageData_ReturnsError()
    {
        SetSourceRootEnv(_sourceDir);
        StoreRegistry.Reset();

        var result = SourceSnippetTool.GetSourceSnippet("AnyClass");

        Assert.Contains("No coverage data found", result);
    }

    // ── Class not found in coverage ──

    [Fact]
    public void GetSourceSnippet_ClassNotInCoverage_ReturnsError()
    {
        SetSourceRootEnv(_sourceDir);
        StoreRegistry.Reset();

        SeedCoverageGaps(new CoverageGap { Class = "OtherClass", File = "src/OtherClass.cs" });

        var result = SourceSnippetTool.GetSourceSnippet("NonExistent");

        Assert.Contains("No coverage data found for class", result);
    }

    // ── Partial class name match ──

    [Fact]
    public void GetSourceSnippet_PartialMatch_FindsClass()
    {
        SetSourceRootEnv(_sourceDir);
        StoreRegistry.Reset();

        var filePath = CreateSourceFile("src/StringHelper.cs", "public class StringHelper { }");
        SeedCoverageGaps(new CoverageGap { Class = "StringHelper", File = filePath });

        var result = SourceSnippetTool.GetSourceSnippet("Helper");

        Assert.Contains("StringHelper", result);
    }

    // ── Empty file path in coverage ──

    [Fact]
    public void GetSourceSnippet_EmptyFilePath_ReturnsError()
    {
        SetSourceRootEnv(_sourceDir);
        StoreRegistry.Reset();

        SeedCoverageGaps(new CoverageGap { Class = "NoFile", File = "" });

        var result = SourceSnippetTool.GetSourceSnippet("NoFile");

        Assert.Contains("has no file path", result);
    }

    // ── Path traversal detection ──

    [Fact]
    public void GetSourceSnippet_PathTraversal_ReturnsSecurityError()
    {
        SetSourceRootEnv(_sourceDir);
        StoreRegistry.Reset();

        SeedCoverageGaps(new CoverageGap { Class = "Evil", File = "../../etc/passwd" });

        var result = SourceSnippetTool.GetSourceSnippet("Evil");

        Assert.Contains("Security", result);
    }

    // ── Source file not found on disk ──

    [Fact]
    public void GetSourceSnippet_FileNotOnDisk_ReturnsError()
    {
        SetSourceRootEnv(_sourceDir);
        StoreRegistry.Reset();

        SeedCoverageGaps(new CoverageGap { Class = "Missing", File = "src/Missing.cs" });

        var result = SourceSnippetTool.GetSourceSnippet("Missing");

        Assert.Contains("Source file not found", result);
    }

    // ── Full class returned within maxLines ──

    [Fact]
    public void GetSourceSnippet_FullClass_ReturnsAllLines()
    {
        SetSourceRootEnv(_sourceDir);
        StoreRegistry.Reset();

        var source = "line1\nline2\nline3\nline4\nline5";
        var filePath = CreateSourceFile("src/Small.cs", source);
        SeedCoverageGaps(new CoverageGap { Class = "Small", File = filePath });

        var result = SourceSnippetTool.GetSourceSnippet("Small", maxLines: 200);
        var doc = JsonDocument.Parse(result);

        Assert.Equal(5, doc.RootElement.GetProperty("totalFileLines").GetInt32());
        Assert.Equal(5, doc.RootElement.GetProperty("returnedLines").GetInt32());
        Assert.False(doc.RootElement.GetProperty("truncated").GetBoolean());
    }

    // ── Truncation when file exceeds maxLines ──

    [Fact]
    public void GetSourceSnippet_LargeFile_TruncatesToMaxLines()
    {
        SetSourceRootEnv(_sourceDir);
        StoreRegistry.Reset();

        var lines = string.Join("\n", Enumerable.Range(1, 50).Select(i => $"// line {i}"));
        var filePath = CreateSourceFile("src/BigFile.cs", lines);
        SeedCoverageGaps(new CoverageGap { Class = "BigFile", File = filePath });

        var result = SourceSnippetTool.GetSourceSnippet("BigFile", maxLines: 10);
        var doc = JsonDocument.Parse(result);

        Assert.Equal(50, doc.RootElement.GetProperty("totalFileLines").GetInt32());
        Assert.Equal(10, doc.RootElement.GetProperty("returnedLines").GetInt32());
        Assert.True(doc.RootElement.GetProperty("truncated").GetBoolean());
    }

    // ── Method extraction with coverage line data ──

    [Fact]
    public void GetSourceSnippet_MethodWithCoverageData_ExtractsCorrectLines()
    {
        SetSourceRootEnv(_sourceDir);
        StoreRegistry.Reset();

        var source = string.Join("\n", Enumerable.Range(1, 30).Select(i => $"// line {i}"));
        var filePath = CreateSourceFile("src/Methods.cs", source);
        SeedCoverageGaps(new CoverageGap
        {
            Class = "Methods",
            File = filePath,
            UncoveredMethods =
            [
                new UncoveredMethod { Name = "DoWork", StartLine = 10, EndLine = 20, UncoveredLines = 5 }
            ]
        });

        var result = SourceSnippetTool.GetSourceSnippet("Methods", methodName: "DoWork");
        var doc = JsonDocument.Parse(result);

        Assert.Equal("DoWork", doc.RootElement.GetProperty("methodName").GetString());
        // StartLine should include context (5 lines before)
        Assert.Equal(5, doc.RootElement.GetProperty("startLine").GetInt32());
        // EndLine should include context (5 lines after)
        Assert.Equal(25, doc.RootElement.GetProperty("endLine").GetInt32());
    }

    // ── Method partial match in coverage ──

    [Fact]
    public void GetSourceSnippet_MethodPartialMatch_FindsMethod()
    {
        SetSourceRootEnv(_sourceDir);
        StoreRegistry.Reset();

        var source = string.Join("\n", Enumerable.Range(1, 20).Select(i => $"// line {i}"));
        var filePath = CreateSourceFile("src/Partial.cs", source);
        SeedCoverageGaps(new CoverageGap
        {
            Class = "Partial",
            File = filePath,
            UncoveredMethods =
            [
                new UncoveredMethod { Name = "ExecuteCommand", StartLine = 5, EndLine = 15, UncoveredLines = 8 }
            ]
        });

        var result = SourceSnippetTool.GetSourceSnippet("Partial", methodName: "Execute");
        var doc = JsonDocument.Parse(result);

        Assert.Equal("ExecuteCommand", doc.RootElement.GetProperty("methodName").GetString());
    }

    // ── Method not found → fallback text search ──

    [Fact]
    public void GetSourceSnippet_MethodNotInCoverage_FallsBackToTextSearch()
    {
        SetSourceRootEnv(_sourceDir);
        StoreRegistry.Reset();

        var source = "namespace App\n{\n    public class MyClass\n    {\n        public void TargetMethod()\n        {\n            // body\n        }\n    }\n}";
        var filePath = CreateSourceFile("src/Fallback.cs", source);
        SeedCoverageGaps(new CoverageGap
        {
            Class = "Fallback",
            File = filePath,
            UncoveredMethods = []  // No methods in coverage
        });

        var result = SourceSnippetTool.GetSourceSnippet("Fallback", methodName: "TargetMethod");
        var doc = JsonDocument.Parse(result);

        Assert.Contains("TargetMethod", doc.RootElement.GetProperty("source").GetString());
        Assert.Contains("source search", doc.RootElement.GetProperty("note").GetString()!);
    }

    // ── Method not found anywhere → error ──

    [Fact]
    public void GetSourceSnippet_MethodNotFoundAnywhere_ReturnsError()
    {
        SetSourceRootEnv(_sourceDir);
        StoreRegistry.Reset();

        var source = "public class Empty { }";
        var filePath = CreateSourceFile("src/Empty.cs", source);
        SeedCoverageGaps(new CoverageGap
        {
            Class = "Empty",
            File = filePath,
            UncoveredMethods =
            [
                new UncoveredMethod { Name = "OtherMethod", StartLine = 1, EndLine = 5, UncoveredLines = 3 }
            ]
        });

        var result = SourceSnippetTool.GetSourceSnippet("Empty", methodName: "NonExistentMethod");

        Assert.Contains("not found in source file", result);
        Assert.Contains("OtherMethod", result); // suggests available methods
    }

    // ── JSON structure for full class ──

    [Fact]
    public void GetSourceSnippet_FullClass_ReturnsCorrectJsonStructure()
    {
        SetSourceRootEnv(_sourceDir);
        StoreRegistry.Reset();

        var filePath = CreateSourceFile("src/Struct.cs", "public class Struct { }");
        SeedCoverageGaps(new CoverageGap { Class = "Struct", File = filePath });

        var result = SourceSnippetTool.GetSourceSnippet("Struct");
        var doc = JsonDocument.Parse(result);

        Assert.Equal("Struct", doc.RootElement.GetProperty("className").GetString());
        Assert.True(doc.RootElement.TryGetProperty("filePath", out _));
        Assert.True(doc.RootElement.TryGetProperty("relativePath", out _));
        Assert.True(doc.RootElement.TryGetProperty("totalFileLines", out _));
        Assert.True(doc.RootElement.TryGetProperty("returnedLines", out _));
        Assert.True(doc.RootElement.TryGetProperty("truncated", out _));
        Assert.True(doc.RootElement.TryGetProperty("source", out _));
    }

    // ── Env var takes precedence over config.json ──

    [Fact]
    public void GetSourceSnippet_EnvVarOverridesConfig()
    {
        var altSourceDir = Path.Combine(TempDir, "alt-source");
        Directory.CreateDirectory(altSourceDir);

        // config.json points to _sourceDir, env var points to altSourceDir
        WriteConfigJson(_sourceDir);
        SetSourceRootEnv(altSourceDir);
        StoreRegistry.Reset();

        var filePath = CreateSourceFile("src/X.cs", "file in source-repo dir");
        // Also write to alt source
        var altPath = Path.Combine(altSourceDir, "src", "X.cs");
        Directory.CreateDirectory(Path.GetDirectoryName(altPath)!);
        File.WriteAllText(altPath, "file in alt-source dir");

        SeedCoverageGaps(new CoverageGap { Class = "X", File = filePath });

        var result = SourceSnippetTool.GetSourceSnippet("X");

        Assert.Contains("file in alt-source dir", result);
    }

    // ── Line number annotations ──

    [Fact]
    public void GetSourceSnippet_FullClass_HasLineNumbers()
    {
        SetSourceRootEnv(_sourceDir);
        StoreRegistry.Reset();

        var source = "line1\nline2\nline3";
        var filePath = CreateSourceFile("src/Numbered.cs", source);
        SeedCoverageGaps(new CoverageGap { Class = "Numbered", File = filePath });

        var result = SourceSnippetTool.GetSourceSnippet("Numbered");
        var doc = JsonDocument.Parse(result);
        var returnedSource = doc.RootElement.GetProperty("source").GetString()!;

        Assert.Contains("1 | line1", returnedSource);
        Assert.Contains("2 | line2", returnedSource);
        Assert.Contains("3 | line3", returnedSource);
    }

    [Fact]
    public void GetSourceSnippet_MethodExtraction_HasLineNumbers()
    {
        SetSourceRootEnv(_sourceDir);
        StoreRegistry.Reset();

        var lines = string.Join("\n", Enumerable.Range(1, 30).Select(i => $"// line {i}"));
        var filePath = CreateSourceFile("src/NumMethod.cs", lines);
        SeedCoverageGaps(new CoverageGap
        {
            Class = "NumMethod",
            File = filePath,
            UncoveredMethods =
            [
                new UncoveredMethod { Name = "DoWork", StartLine = 10, EndLine = 20, UncoveredLines = 5 }
            ]
        });

        var result = SourceSnippetTool.GetSourceSnippet("NumMethod", methodName: "DoWork");
        var doc = JsonDocument.Parse(result);
        var returnedSource = doc.RootElement.GetProperty("source").GetString()!;

        // Should have line numbers starting from startLine (10 - 5 context = 5)
        Assert.Contains("5 | ", returnedSource);
        Assert.Contains("10 | ", returnedSource);
        Assert.Contains("15 | ", returnedSource);
    }

    // ── AnnotateWithLineNumbers unit tests ──

    [Fact]
    public void AnnotateWithLineNumbers_BasicLines_FormatsCorrectly()
    {
        var lines = new[] { "first", "second", "third" };
        var result = SourceSnippetTool.AnnotateWithLineNumbers(lines, 1);

        Assert.Contains("1 | first", result);
        Assert.Contains("2 | second", result);
        Assert.Contains("3 | third", result);
    }

    [Fact]
    public void AnnotateWithLineNumbers_HighLineNumbers_PadsCorrectly()
    {
        var lines = new[] { "a", "b" };
        var result = SourceSnippetTool.AnnotateWithLineNumbers(lines, 99);

        // Lines 99-100: "100" is 3 chars, so " 99" should be padded to 3 chars
        Assert.Contains(" 99 | a", result);
        Assert.Contains("100 | b", result);
    }

    [Fact]
    public void AnnotateWithLineNumbers_EmptyInput_ReturnsEmpty()
    {
        var result = SourceSnippetTool.AnnotateWithLineNumbers([], 1);
        Assert.Equal(string.Empty, result);
    }

    // ── Error path coverage ──

    [Fact]
    public void GetSourceSnippet_InvalidNamespace_ReturnsError()
    {
        var result = SourceSnippetTool.GetSourceSnippet("Any", ns: "\0");

        Assert.StartsWith("ERROR in GetSourceSnippet", result);
    }

    // ── Config.json parse error path (covers L245-250) ──

    [Fact]
    public void GetSourceSnippet_CorruptConfigJson_FallsBackToNoSourceRoot()
    {
        // Write corrupt config.json so ResolveSourceRoot's catch fires
        File.WriteAllText(Path.Combine(TempDir, "config.json"), "NOT VALID JSON {{{{");
        StoreRegistry.Reset();

        SeedCoverageGaps(new CoverageGap { Class = "MyClass", File = "src/MyClass.cs" });

        var result = SourceSnippetTool.GetSourceSnippet("MyClass");

        // Should fall through to "Source root not configured" since config.json parse failed
        Assert.Contains("Source root not configured", result);
    }

    // ── Method extraction maxLines truncation (covers L140) ──

    [Fact]
    public void GetSourceSnippet_ConfigWithEmptySourceRoot_FallsThrough()
    {
        // config.json exists and parses OK but SourceRoot is empty → try block falls through (covers L245)
        StoreRegistry.Reset();
        var config = new NamespaceConfig { SourceRoot = "" };
        var json = JsonSerializer.Serialize(config, SharedJsonOptions.CamelCase);
        File.WriteAllText(Path.Combine(TempDir, "config.json"), json);
        SeedCoverageGaps(new CoverageGap { Class = "MyClass", File = "src/MyClass.cs" });

        var result = SourceSnippetTool.GetSourceSnippet("MyClass");

        // With no source root resolved, should indicate configuration needed
        Assert.Contains("source root", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetSourceSnippet_MethodExceedsMaxLines_TruncatesMethodRange()
    {
        SetSourceRootEnv(_sourceDir);
        StoreRegistry.Reset();

        // Create a file with 100 lines
        var source = string.Join("\n", Enumerable.Range(1, 100).Select(i => $"// line {i}"));
        var filePath = CreateSourceFile("src/LongMethod.cs", source);
        SeedCoverageGaps(new CoverageGap
        {
            Class = "LongMethod",
            File = filePath,
            UncoveredMethods =
            [
                // Method spanning lines 10-90 (80 lines), but context adds 5 before/after → 85 lines
                new UncoveredMethod { Name = "BigMethod", StartLine = 10, EndLine = 90, UncoveredLines = 40 }
            ]
        });

        // Request with very small maxLines (5) — method range with context is ~85 lines, way > 5
        var result = SourceSnippetTool.GetSourceSnippet("LongMethod", methodName: "BigMethod", maxLines: 5);
        var doc = JsonDocument.Parse(result);

        // startLine = max(1, 10-5) = 5, endLine would be 95 but truncated to 5+5-1 = 9 due to maxLines
        Assert.Equal(5, doc.RootElement.GetProperty("startLine").GetInt32());
        Assert.Equal(9, doc.RootElement.GetProperty("endLine").GetInt32());
    }

    // ── Name collision: multiple classes with same name ──

    [Fact]
    public void GetSourceSnippet_MultipleClassesSameName_PrefersMoreUncoveredLines()
    {
        SetSourceRootEnv(_sourceDir);
        StoreRegistry.Reset();

        // Two files both defining "PivotEntry" — small POCO vs larger implementation
        var smallFile = CreateSourceFile("src/Models/PivotEntry.cs", "public class PivotEntry { public string Name { get; set; } }");
        var largeFile = CreateSourceFile("src/Invoices/PivotEntry.cs",
            string.Join("\n", Enumerable.Range(1, 58).Select(i => $"// implementation line {i}")));

        SeedCoverageGaps(
            new CoverageGap { Class = "PivotEntry", Namespace = "Models", File = smallFile, TotalLines = 7, UncoveredLines = 3 },
            new CoverageGap { Class = "PivotEntry", Namespace = "Invoices", File = largeFile, TotalLines = 58, UncoveredLines = 40 }
        );

        var result = SourceSnippetTool.GetSourceSnippet("PivotEntry");
        var doc = JsonDocument.Parse(result);

        // Should pick the Invoices version (40 uncovered lines vs 3)
        Assert.Contains("Invoices", doc.RootElement.GetProperty("relativePath").GetString());
        Assert.True(doc.RootElement.TryGetProperty("ambiguityNote", out var note));
        Assert.Contains("2 classes named", note.GetString());
    }

    [Fact]
    public void GetSourceSnippet_SingleMatch_NoAmbiguityNote()
    {
        SetSourceRootEnv(_sourceDir);
        StoreRegistry.Reset();

        var filePath = CreateSourceFile("src/Unique.cs", "public class Unique { }");
        SeedCoverageGaps(new CoverageGap { Class = "Unique", File = filePath, TotalLines = 1, UncoveredLines = 1 });

        var result = SourceSnippetTool.GetSourceSnippet("Unique");
        var doc = JsonDocument.Parse(result);

        // ambiguityNote should be null → serialized as null in JSON
        var noteProperty = doc.RootElement.GetProperty("ambiguityNote");
        Assert.Equal(System.Text.Json.JsonValueKind.Null, noteProperty.ValueKind);
    }

    [Fact]
    public void GetSourceSnippet_MultiplePartialMatches_PrefersLargerFile()
    {
        SetSourceRootEnv(_sourceDir);
        StoreRegistry.Reset();

        var smallFile = CreateSourceFile("src/SmallZone.cs", "public class SmallPivotEntry { }");
        var largeFile = CreateSourceFile("src/LargeZone.cs",
            string.Join("\n", Enumerable.Range(1, 30).Select(i => $"// line {i}")));

        SeedCoverageGaps(
            new CoverageGap { Class = "SmallPivotEntry", Namespace = "App", File = smallFile, TotalLines = 5, UncoveredLines = 2 },
            new CoverageGap { Class = "LargePivotEntry", Namespace = "App", File = largeFile, TotalLines = 30, UncoveredLines = 20 }
        );

        var result = SourceSnippetTool.GetSourceSnippet("PivotEntry");
        var doc = JsonDocument.Parse(result);

        // Partial match — should pick LargePivotEntry (20 uncovered vs 2)
        Assert.Contains("LargeZone", doc.RootElement.GetProperty("relativePath").GetString());
    }

    // ── Source root caching (Bug fix) ──

    [Fact]
    public void ResolveSourceRoot_CachesResult_SecondCallSkipsLog()
    {
        SetSourceRootEnv(_sourceDir);
        SourceSnippetTool.ResetSourceRootCache();

        // First call resolves and caches
        var result1 = SourceSnippetTool.ResolveSourceRoot(TempDir);
        Assert.NotNull(result1);

        // Second call hits cache — no filesystem / env lookup
        var result2 = SourceSnippetTool.ResolveSourceRoot(TempDir);
        Assert.Equal(result1, result2);
    }

    [Fact]
    public void ResolveSourceRoot_DifferentDataDirs_CacheSeparately()
    {
        SetSourceRootEnv(_sourceDir);
        SourceSnippetTool.ResetSourceRootCache();

        var result1 = SourceSnippetTool.ResolveSourceRoot(TempDir);
        Assert.NotNull(result1);

        // Different key → separate resolution
        var otherDir = Path.Combine(TempDir, "other");
        Directory.CreateDirectory(otherDir);
        var result2 = SourceSnippetTool.ResolveSourceRoot(otherDir);
        Assert.Equal(result1, result2); // same env var
    }

    [Fact]
    public void ResolveSourceRoot_ResetCache_ReResolvesOnNextCall()
    {
        SetSourceRootEnv(_sourceDir);
        SourceSnippetTool.ResetSourceRootCache();

        var result1 = SourceSnippetTool.ResolveSourceRoot(TempDir);
        Assert.NotNull(result1);

        SourceSnippetTool.ResetSourceRootCache();

        // After reset, resolves again (cache cleared)
        var result2 = SourceSnippetTool.ResolveSourceRoot(TempDir);
        Assert.Equal(result1, result2);
    }

    [Fact]
    public void ResolveSourceRoot_NoEnvNoConfig_ReturnsNull()
    {
        Environment.SetEnvironmentVariable("TOTAL_RECALL_SOURCE_ROOT", null);
        SourceSnippetTool.ResetSourceRootCache();

        var result = SourceSnippetTool.ResolveSourceRoot(TempDir);
        Assert.Null(result);
    }

    // ── Item #7: Multi-method source snippets ──

    [Fact]
    public void GetSourceSnippet_CommaSeparatedMethods_ReturnsMultiple()
    {
        SetSourceRootEnv(_sourceDir);
        StoreRegistry.Reset();

        var source = @"public class Worker
{
    public void Start() { Console.WriteLine(""starting""); }
    public void Stop() { Console.WriteLine(""stopping""); }
    public void Pause() { Console.WriteLine(""pausing""); }
}";
        var filePath = CreateSourceFile("src/Worker.cs", source);
        SeedCoverageGaps(new CoverageGap
        {
            Class = "Worker", File = filePath,
            TotalLines = 6, CoveredLines = 0, UncoveredLines = 6,
            UncoveredMethods =
            [
                new UncoveredMethod { Name = "Start", StartLine = 3, EndLine = 3, UncoveredLines = 1 },
                new UncoveredMethod { Name = "Stop", StartLine = 4, EndLine = 4, UncoveredLines = 1 },
                new UncoveredMethod { Name = "Pause", StartLine = 5, EndLine = 5, UncoveredLines = 1 }
            ]
        });

        var result = SourceSnippetTool.GetSourceSnippet("Worker", methodName: "Start,Stop");
        var doc = JsonDocument.Parse(result);

        Assert.Equal(2, doc.RootElement.GetProperty("requestedMethods").GetInt32());
        Assert.Equal(2, doc.RootElement.GetProperty("returnedMethods").GetInt32());
        Assert.Equal(2, doc.RootElement.GetProperty("methods").GetArrayLength());
    }

    [Fact]
    public void GetSourceSnippet_CommaSeparatedMethods_NotFoundTracked()
    {
        SetSourceRootEnv(_sourceDir);
        StoreRegistry.Reset();

        var source = "public class Calc { public int Add(int a, int b) => a + b; }";
        var filePath = CreateSourceFile("src/Calc.cs", source);
        SeedCoverageGaps(new CoverageGap
        {
            Class = "Calc", File = filePath,
            TotalLines = 1, CoveredLines = 0, UncoveredLines = 1,
            UncoveredMethods = [new UncoveredMethod { Name = "Add", StartLine = 1, EndLine = 1, UncoveredLines = 1 }]
        });

        var result = SourceSnippetTool.GetSourceSnippet("Calc", methodName: "Add,NonExistent");
        var doc = JsonDocument.Parse(result);

        Assert.Equal(2, doc.RootElement.GetProperty("requestedMethods").GetInt32());
        Assert.Equal(1, doc.RootElement.GetProperty("returnedMethods").GetInt32());
        var notFound = doc.RootElement.GetProperty("notFound").EnumerateArray().Select(e => e.GetString()).ToList();
        Assert.Contains("NonExistent", notFound);
    }

    [Fact]
    public void GetSourceSnippet_SingleMethodName_ReturnsSingleObject()
    {
        SetSourceRootEnv(_sourceDir);
        StoreRegistry.Reset();

        var source = "public class Svc { public void Run() { } }";
        var filePath = CreateSourceFile("src/Svc.cs", source);
        SeedCoverageGaps(new CoverageGap
        {
            Class = "Svc", File = filePath,
            TotalLines = 1, CoveredLines = 0, UncoveredLines = 1,
            UncoveredMethods = [new UncoveredMethod { Name = "Run", StartLine = 1, EndLine = 1, UncoveredLines = 1 }]
        });

        var result = SourceSnippetTool.GetSourceSnippet("Svc", methodName: "Run");
        var doc = JsonDocument.Parse(result);

        // Single method mode — returns method result directly, NOT an envelope with "methods" array
        Assert.True(doc.RootElement.TryGetProperty("methodName", out _));
        Assert.False(doc.RootElement.TryGetProperty("methods", out _));
    }
}
