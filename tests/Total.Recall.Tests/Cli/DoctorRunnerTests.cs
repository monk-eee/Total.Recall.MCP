using System.Text.Json;
using Total.Recall.Cli;
using Total.Recall.Infrastructure;
using Total.Recall.Models;
using Total.Recall.Tests.Infrastructure;

namespace Total.Recall.Tests.Cli;

[Collection("ToolTests")]
public class DoctorRunnerTests
{
    [Fact]
    public void RunDoctor_DataRootMissing_ReturnsExit2()
    {
        using var harness = new TelemetryTestHarness();
        // Delete the data root the harness created
        Directory.Delete(harness.TempDir, recursive: true);

        using var sw = new StringWriter();
        var exit = DoctorRunner.RunDoctor(["doctor"], sw);

        Assert.Equal(2, exit);
        var output = sw.ToString();
        Assert.Contains("Data Root", output);
        Assert.Contains("FAIL", output);
    }

    [Fact]
    public void RunDoctor_EmptyDataRoot_ReturnsWarning()
    {
        using var harness = new TelemetryTestHarness();
        using var sw = new StringWriter();

        var exit = DoctorRunner.RunDoctor(["doctor"], sw);

        Assert.Equal(1, exit);
        Assert.Contains("no namespaces found", sw.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RunDoctor_PopulatedNamespace_AllOk_ReturnsZero()
    {
        using var harness = new TelemetryTestHarness();
        var ns = "good";
        var dir = Path.Combine(harness.TempDir, ns);
        Directory.CreateDirectory(dir);
        // Write all three core JSONL files with at least one record
        File.WriteAllText(RepoConfig.TypeRegistryPath(dir), "{\"name\":\"X\"}\n");
        File.WriteAllText(RepoConfig.CoverageGapsPath(dir), "{\"className\":\"X\"}\n");
        File.WriteAllText(RepoConfig.TestInventoryPath(dir), "{\"className\":\"X\"}\n");

        // Config.json pointing at real on-disk paths
        var sourceRoot = Path.Combine(harness.TempDir, "src");
        Directory.CreateDirectory(sourceRoot);
        var assemblyPath = Path.Combine(harness.TempDir, "fake.dll");
        File.WriteAllText(assemblyPath, "fake");
        var coveragePath = Path.Combine(harness.TempDir, "coverage.cobertura.xml");
        File.WriteAllText(coveragePath, "<coverage/>");
        var testsPath = Path.Combine(harness.TempDir, "tests");
        Directory.CreateDirectory(testsPath);

        var config = new NamespaceConfig
        {
            SourceRoot = sourceRoot,
            AssemblyPath = assemblyPath,
            CoveragePath = coveragePath,
            TestsPath = testsPath,
            ScannedUtc = DateTime.UtcNow.ToString("O")
        };
        File.WriteAllText(RepoConfig.ConfigJsonPath(dir),
            JsonSerializer.Serialize(config, SharedJsonOptions.CamelCaseIndented));

        using var sw = new StringWriter();
        var exit = DoctorRunner.RunDoctor(["doctor"], sw);

        Assert.Equal(0, exit);
        var output = sw.ToString();
        Assert.Contains("Namespace: good", output);
        Assert.Contains("OK", output);
    }

    [Fact]
    public void RunDoctor_NamespaceMissingCoreFile_ReturnsWarning()
    {
        using var harness = new TelemetryTestHarness();
        var dir = Path.Combine(harness.TempDir, "partial");
        Directory.CreateDirectory(dir);
        // Only one core file present
        File.WriteAllText(RepoConfig.TypeRegistryPath(dir), "{\"name\":\"X\"}\n");

        using var sw = new StringWriter();
        var exit = DoctorRunner.RunDoctor(["doctor"], sw);

        Assert.Equal(1, exit);
        var output = sw.ToString();
        Assert.Contains("MISSING (core)", output);
        Assert.Contains("WARN", output);
    }

    [Fact]
    public void RunDoctor_ConfigPathsBroken_ReturnsWarningWithMarkers()
    {
        using var harness = new TelemetryTestHarness();
        var dir = Path.Combine(harness.TempDir, "broken");
        Directory.CreateDirectory(dir);
        File.WriteAllText(RepoConfig.TypeRegistryPath(dir), "{}\n");
        File.WriteAllText(RepoConfig.CoverageGapsPath(dir), "{}\n");
        File.WriteAllText(RepoConfig.TestInventoryPath(dir), "{}\n");

        var config = new NamespaceConfig
        {
            SourceRoot = @"C:\does\not\exist\src",
            AssemblyPath = @"C:\does\not\exist\fake.dll",
            CoveragePath = @"C:\does\not\exist\coverage.xml",
            TestsPath = @"C:\does\not\exist\tests"
        };
        File.WriteAllText(RepoConfig.ConfigJsonPath(dir),
            JsonSerializer.Serialize(config, SharedJsonOptions.CamelCaseIndented));

        using var sw = new StringWriter();
        var exit = DoctorRunner.RunDoctor(["doctor"], sw);

        Assert.Equal(1, exit);
        var output = sw.ToString();
        Assert.Contains("[MISSING]", output);
    }

    [Fact]
    public void RunDoctor_HelpFlag_PrintsUsageAndReturnsZero()
    {
        using var sw = new StringWriter();
        var exit = DoctorRunner.RunDoctor(["doctor", "--help"], sw);
        Assert.Equal(0, exit);
        Assert.Contains("Usage: total-recall doctor", sw.ToString());
    }

    [Fact]
    public void RunDoctor_NamespaceFilter_OnlyShowsRequestedNamespace()
    {
        using var harness = new TelemetryTestHarness();
        var ns1 = Path.Combine(harness.TempDir, "alpha");
        var ns2 = Path.Combine(harness.TempDir, "beta");
        Directory.CreateDirectory(ns1);
        Directory.CreateDirectory(ns2);
        File.WriteAllText(RepoConfig.TypeRegistryPath(ns1), "{}\n");
        File.WriteAllText(RepoConfig.TypeRegistryPath(ns2), "{}\n");

        using var sw = new StringWriter();
        var exit = DoctorRunner.RunDoctor(["doctor", "--ns", "alpha"], sw);

        // exit may be 1 (missing other core files) but the filter must work
        var output = sw.ToString();
        Assert.Contains("Namespace: alpha", output);
        Assert.DoesNotContain("Namespace: beta", output);
        Assert.True(exit == 0 || exit == 1);
    }

    [Fact]
    public void RunDoctor_PrintsEnvVarSection()
    {
        using var harness = new TelemetryTestHarness();
        using var sw = new StringWriter();
        DoctorRunner.RunDoctor(["doctor"], sw);
        var output = sw.ToString();
        Assert.Contains("Environment", output);
        Assert.Contains("TOTAL_RECALL_DATA", output);
        Assert.Contains("TOTAL_RECALL_NAMESPACE", output);
        Assert.Contains("TOTAL_RECALL_MODE", output);
    }
}
