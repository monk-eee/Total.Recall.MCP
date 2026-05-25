using System.Text.Json;
using Total.Recall.Cli;
using Total.Recall.Infrastructure;
using Total.Recall.Models;
using Total.Recall.Tests.Infrastructure;

namespace Total.Recall.Tests.Cli;

[Collection("ToolTests")]
public class InitRunnerTests : IDisposable
{
    private readonly string _repoRoot;
    private readonly TelemetryTestHarness _harness;

    public InitRunnerTests()
    {
        // Synthetic repo
        _repoRoot = Path.Combine(Path.GetTempPath(), "tr-init-tests", Guid.NewGuid().ToString());
        var srcDir = Path.Combine(_repoRoot, "src", "Sample");
        var binDir = Path.Combine(srcDir, "bin", "Debug", "net8.0");
        var testDir = Path.Combine(_repoRoot, "tests", "Sample.Tests");
        var coverageDir = Path.Combine(_repoRoot, "TestResults", Guid.NewGuid().ToString());
        Directory.CreateDirectory(binDir);
        Directory.CreateDirectory(testDir);
        Directory.CreateDirectory(coverageDir);
        File.WriteAllText(Path.Combine(srcDir, "Sample.csproj"), "<Project/>");
        File.WriteAllText(Path.Combine(testDir, "Sample.Tests.csproj"),
            "<Project><ItemGroup><PackageReference Include=\"Microsoft.NET.Test.Sdk\" /></ItemGroup></Project>");
        File.WriteAllText(Path.Combine(binDir, "Sample.dll"), "fake");
        File.WriteAllText(Path.Combine(coverageDir, "coverage.cobertura.xml"), "<coverage/>");

        // Data root (via env var) lives in a separate temp dir
        _harness = new TelemetryTestHarness();
    }

    public void Dispose()
    {
        _harness.Dispose();
        try { if (Directory.Exists(_repoRoot)) Directory.Delete(_repoRoot, recursive: true); }
        catch { /* best effort */ }
    }

    [Fact]
    public void RunInit_WithRepoPath_WritesConfigAndPrintsReport()
    {
        using var sw = new StringWriter();
        var exit = InitRunner.RunInit(["init", _repoRoot, "--namespace", "sample"], sw);

        Assert.Equal(0, exit);
        var output = sw.ToString();
        Assert.Contains("Discovered", output);
        Assert.Contains("Sample.dll", output);
        Assert.Contains("coverage.cobertura.xml", output);
        Assert.Contains("Suggested .vscode/mcp.json", output);
        Assert.Contains("\"TOTAL_RECALL_NAMESPACE\": \"sample\"", output);

        // Config file was written
        var dataDir = Path.Combine(_harness.TempDir, "sample");
        var configPath = RepoConfig.ConfigJsonPath(dataDir);
        Assert.True(File.Exists(configPath), $"expected config at {configPath}");

        var config = JsonSerializer.Deserialize<NamespaceConfig>(
            File.ReadAllText(configPath), SharedJsonOptions.CamelCase);
        Assert.NotNull(config);
        Assert.EndsWith("Sample.dll", config!.AssemblyPath);
        Assert.EndsWith("coverage.cobertura.xml", config.CoveragePath);
        Assert.EndsWith("Sample.Tests", config.TestsPath);
        Assert.Equal(Path.Combine(_repoRoot, "src"), config.SourceRoot);
    }

    [Fact]
    public void RunInit_MissingRepoPath_ReturnsUsageError()
    {
        using var sw = new StringWriter();
        var exit = InitRunner.RunInit(["init"], sw);
        Assert.Equal(1, exit);
        Assert.Contains("<repo-path> is required", sw.ToString());
        Assert.Contains("Usage: total-recall init", sw.ToString());
    }

    [Fact]
    public void RunInit_HelpFlag_ReturnsZeroAndPrintsUsage()
    {
        using var sw = new StringWriter();
        var exit = InitRunner.RunInit(["init", "--help"], sw);
        Assert.Equal(0, exit);
        Assert.Contains("Usage: total-recall init", sw.ToString());
    }

    [Fact]
    public void RunInit_NonexistentRepoPath_ReturnsFsError()
    {
        var bogus = Path.Combine(_repoRoot, "does-not-exist");
        using var sw = new StringWriter();
        var exit = InitRunner.RunInit(["init", bogus], sw);
        Assert.Equal(2, exit);
        Assert.Contains("not found", sw.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RunInit_DefaultNamespace_DerivedFromRepoDirName()
    {
        using var sw = new StringWriter();
        var exit = InitRunner.RunInit(["init", _repoRoot], sw);
        Assert.Equal(0, exit);
        // Repo root leaf name was used (a guid) — sluggified. We just check that
        // a config.json was written under the data root.
        var subdirs = Directory.GetDirectories(_harness.TempDir);
        Assert.Single(subdirs);
    }

    [Fact]
    public void RunInit_EmitsScanCommandWithDiscoveredPaths()
    {
        using var sw = new StringWriter();
        var exit = InitRunner.RunInit(["init", _repoRoot, "--namespace", "sample"], sw);
        Assert.Equal(0, exit);
        var output = sw.ToString();
        Assert.Contains("total-recall scan", output);
        Assert.Contains("--assembly", output);
        Assert.Contains("--coverage", output);
        Assert.Contains("--tests", output);
        Assert.Contains("--namespace sample", output);
    }

    [Fact]
    public void RunInit_PreservesExistingTestFrameworkAndMockLibrary()
    {
        // Pre-write a config with non-default settings
        var dataDir = Path.Combine(_harness.TempDir, "sample");
        Directory.CreateDirectory(dataDir);
        var existing = new NamespaceConfig
        {
            TestFramework = TestFramework.NUnit,
            MockLibrary = MockLibrary.NSubstitute,
            TestNamespacePattern = "{RootNamespace}.Tests.{Rest}",
            ScannedUtc = "2024-01-01T00:00:00Z"
        };
        File.WriteAllText(RepoConfig.ConfigJsonPath(dataDir),
            JsonSerializer.Serialize(existing, SharedJsonOptions.CamelCaseIndented));

        using var sw = new StringWriter();
        var exit = InitRunner.RunInit(["init", _repoRoot, "--namespace", "sample"], sw);
        Assert.Equal(0, exit);

        var reread = JsonSerializer.Deserialize<NamespaceConfig>(
            File.ReadAllText(RepoConfig.ConfigJsonPath(dataDir)), SharedJsonOptions.CamelCase);
        Assert.Equal(TestFramework.NUnit, reread!.TestFramework);
        Assert.Equal(MockLibrary.NSubstitute, reread.MockLibrary);
        Assert.Equal("{RootNamespace}.Tests.{Rest}", reread.TestNamespacePattern);
        Assert.Equal("2024-01-01T00:00:00Z", reread.ScannedUtc);
        // But discovery fields were rewritten
        Assert.EndsWith("Sample.dll", reread.AssemblyPath);
    }

    [Fact]
    public void RunInit_EmptyRepo_WarnsAboutMissingArtefacts()
    {
        var emptyRepo = Path.Combine(_repoRoot, "empty-sub");
        Directory.CreateDirectory(emptyRepo);

        using var sw = new StringWriter();
        var exit = InitRunner.RunInit(["init", emptyRepo, "--namespace", "empty"], sw);
        Assert.Equal(0, exit); // still succeeds — writes a partial config
        var output = sw.ToString();
        Assert.Contains("No production .dll found", output);
        Assert.Contains("No coverage.cobertura.xml found", output);
        Assert.Contains("No test project detected", output);
    }
}
