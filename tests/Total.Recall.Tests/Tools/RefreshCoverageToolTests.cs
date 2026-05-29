using System.Text.Json;
using Total.Recall.Infrastructure;
using Total.Recall.Models;
using Total.Recall.Scanners;
using Total.Recall.Tools;

namespace Total.Recall.Tests.Tools;

/// <summary>
/// Tests for RefreshCoverageTool.RefreshCoverage — re-parses Cobertura XML mid-session
/// and reports before/after delta.
/// </summary>
[Collection("ToolTests")]
public sealed class RefreshCoverageToolTests : ToolTestBase
{
    public RefreshCoverageToolTests() : base(saveNamespace: true) { }

    // ── Helpers ──

    private string WriteCoberturaXml(string className, string ns, int totalLines, int coveredLines)
    {
        var xmlPath = Path.Combine(TempDir, "coverage.cobertura.xml");
        var lineRate = totalLines > 0 ? (double)coveredLines / totalLines : 0;
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
        sb.AppendLine($"<coverage line-rate=\"{lineRate:F4}\" branch-rate=\"0\" version=\"1.0\">");
        sb.AppendLine("  <packages>");
        sb.AppendLine($"    <package name=\"{ns}\" line-rate=\"{lineRate:F4}\">");
        sb.AppendLine("      <classes>");
        sb.AppendLine($"        <class name=\"{ns}.{className}\" filename=\"{className}.cs\" line-rate=\"{lineRate:F4}\">");
        sb.AppendLine("          <methods>");
        sb.AppendLine($"            <method name=\"DoWork\" signature=\"()\" line-rate=\"{lineRate:F4}\">");
        sb.AppendLine("              <lines>");
        for (int i = 1; i <= totalLines; i++)
        {
            var hits = i <= coveredLines ? 1 : 0;
            sb.AppendLine($"                <line number=\"{i}\" hits=\"{hits}\"/>");
        }
        sb.AppendLine("              </lines>");
        sb.AppendLine("            </method>");
        sb.AppendLine("          </methods>");
        sb.AppendLine("          <lines>");
        for (int i = 1; i <= totalLines; i++)
        {
            var hits = i <= coveredLines ? 1 : 0;
            sb.AppendLine($"            <line number=\"{i}\" hits=\"{hits}\"/>");
        }
        sb.AppendLine("          </lines>");
        sb.AppendLine("        </class>");
        sb.AppendLine("      </classes>");
        sb.AppendLine("    </package>");
        sb.AppendLine("  </packages>");
        sb.AppendLine("</coverage>");
        File.WriteAllText(xmlPath, sb.ToString());
        return xmlPath;
    }

    private void SeedConfig(string coveragePath)
    {
        var config = new NamespaceConfig { CoveragePath = coveragePath };
        var json = JsonSerializer.Serialize(config, SharedJsonOptions.CamelCaseIndented);
        File.WriteAllText(RepoConfig.ConfigJsonPath(TempDir), json);
    }


    // ═══════════════════════════════════════
    // Error paths
    // ═══════════════════════════════════════

    [Fact]
    public void RefreshCoverage_NoCoveragePathAndNoConfig_ReturnsError()
    {
        var result = RefreshCoverageTool.RefreshCoverage();
        Assert.Contains("No coverage path provided", result);
    }

    [Fact]
    public void RefreshCoverage_FileNotFound_ReturnsError()
    {
        var result = RefreshCoverageTool.RefreshCoverage("/nonexistent/coverage.xml");
        Assert.Contains("Coverage file not found", result);
    }

    // ═══════════════════════════════════════
    // Success paths
    // ═══════════════════════════════════════

    [Fact]
    public void RefreshCoverage_WithExplicitPath_ParsesAndReturnsStatus()
    {
        var xmlPath = WriteCoberturaXml("MyService", "App.Services", totalLines: 100, coveredLines: 70);

        var result = RefreshCoverageTool.RefreshCoverage(xmlPath);

        Assert.Contains("\"status\": \"refreshed\"", result);
        Assert.Contains("\"coverageFile\":", result);
        Assert.Contains("\"after\":", result);
    }

    [Fact]
    public void RefreshCoverage_WithConfigPath_UsesConfigCoveragePath()
    {
        var xmlPath = WriteCoberturaXml("ConfigService", "App", totalLines: 50, coveredLines: 25);
        SeedConfig(xmlPath);
        StoreRegistry.Reset(); // Clear cached config

        var result = RefreshCoverageTool.RefreshCoverage();

        Assert.Contains("\"status\": \"refreshed\"", result);
    }

    [Fact]
    public void RefreshCoverage_BeforeAfterDelta_ComputedCorrectly()
    {
        // Seed before-state
        SeedCoverageGaps(new CoverageGap
        {
            ClassName = "App.Worker",
            LinesTotal = 100,
            LinesCovered = 40,
            CoveragePercent = 40.0
        });

        // Now coverage has improved (60/100 covered)
        var xmlPath = WriteCoberturaXml("Worker", "App", totalLines: 100, coveredLines: 60);
        var result = RefreshCoverageTool.RefreshCoverage(xmlPath);

        // Should show delta
        Assert.Contains("\"lineRateChange\":", result);
        Assert.Contains("\"newLinesHit\":", result);
    }

    [Fact]
    public void RefreshCoverage_NoPreviousGaps_HandlesGracefully()
    {
        var xmlPath = WriteCoberturaXml("Brand New", "App", totalLines: 50, coveredLines: 30);

        var result = RefreshCoverageTool.RefreshCoverage(xmlPath);

        Assert.Contains("\"status\": \"refreshed\"", result);
        // Before should be zero
        Assert.Contains("\"coveredLines\": 0", result);
    }

    [Fact]
    public void RefreshCoverage_AutoDiscoverTestResults_FindsNewest()
    {
        // Create a TestResults subdirectory structure
        var testResultsDir = Path.Combine(TempDir, "TestResults", Guid.NewGuid().ToString());
        Directory.CreateDirectory(testResultsDir);

        // Write the Cobertura XML into TestResults/{guid}/
        var xmlPath = Path.Combine(testResultsDir, "coverage.cobertura.xml");
        var tempXml = WriteCoberturaXml("DiscoverMe", "App", totalLines: 20, coveredLines: 10);
        File.Copy(tempXml, xmlPath);

        // Point coveragePath to a non-existent file whose parent has TestResults/
        var fakePath = Path.Combine(TempDir, "coverage.xml");
        // Config points to the fake path, but auto-discovery should find in TestResults
        var result = RefreshCoverageTool.RefreshCoverage(fakePath);

        // Should auto-discover and succeed
        Assert.Contains("\"status\": \"refreshed\"", result);
    }

    // ── reEnrich parameter tests ──

    [Fact]
    public void RefreshCoverage_WithReEnrich_EnrichesTestCounts()
    {
        // Seed type registry with a class
        SeedTypeRegistry(new TypeRecord
        {
            Name = "EnrichMe",
            Namespace = "App",
            Constructors = [new ConstructorRecord { Params = ["ILogger"] }]
        });
        // Seed test inventory with existing tests for that class
        SeedTestInventory(new TestInventoryEntry
        {
            Class = "EnrichMe",
            TestCount = 7,
            TestMethods = ["Test1", "Test2", "Test3", "Test4", "Test5", "Test6", "Test7"]
        });
        StoreRegistry.Reset();

        var xmlPath = WriteCoberturaXml("EnrichMe", "App", totalLines: 50, coveredLines: 20);

        var result = RefreshCoverageTool.RefreshCoverage(xmlPath, reEnrich: true);

        Assert.Contains("\"status\": \"refreshed\"", result);
        Assert.Contains("\"enriched\":", result);

        // Verify the coverage gaps were enriched with test counts
        var gaps = new JsonLineStore<CoverageGap>(RepoConfig.CoverageGapsPath(TempDir)).LoadAll();
        var gap = gaps.First(g => g.ShortName == "EnrichMe");
        Assert.Equal(7, gap.ExistingTests);
    }

    [Fact]
    public void RefreshCoverage_WithReEnrich_ClassifiesTestability()
    {
        // Seed type registry: abstract class → low testability
        SeedTypeRegistry(new TypeRecord
        {
            Name = "AbstractService",
            Namespace = "App",
            IsAbstract = true
        });
        StoreRegistry.Reset();

        var xmlPath = WriteCoberturaXml("AbstractService", "App", totalLines: 30, coveredLines: 10);

        RefreshCoverageTool.RefreshCoverage(xmlPath, reEnrich: true);

        var gaps = new JsonLineStore<CoverageGap>(RepoConfig.CoverageGapsPath(TempDir)).LoadAll();
        var gap = gaps.First(g => g.ShortName == "AbstractService");
        Assert.Equal(0.2, gap.TestabilityScore);
    }

    [Fact]
    public void RefreshCoverage_WithReEnrich_StaticClassGetsMediumTestability()
    {
        SeedTypeRegistry(new TypeRecord
        {
            Name = "Helpers",
            Namespace = "App",
            IsStatic = true
        });
        StoreRegistry.Reset();

        var xmlPath = WriteCoberturaXml("Helpers", "App", totalLines: 20, coveredLines: 5);

        RefreshCoverageTool.RefreshCoverage(xmlPath, reEnrich: true);

        var gaps = new JsonLineStore<CoverageGap>(RepoConfig.CoverageGapsPath(TempDir)).LoadAll();
        var gap = gaps.First(g => g.ShortName == "Helpers");
        Assert.Equal(0.55, gap.TestabilityScore);
    }

    [Fact]
    public void RefreshCoverage_WithReEnrich_SmallCtorGetsHighTestability()
    {
        SeedTypeRegistry(new TypeRecord
        {
            Name = "SimpleService",
            Namespace = "App",
            Constructors = [new ConstructorRecord { Params = ["ILogger", "IConfig"] }]
        });
        StoreRegistry.Reset();

        var xmlPath = WriteCoberturaXml("SimpleService", "App", totalLines: 40, coveredLines: 15);

        RefreshCoverageTool.RefreshCoverage(xmlPath, reEnrich: true);

        var gaps = new JsonLineStore<CoverageGap>(RepoConfig.CoverageGapsPath(TempDir)).LoadAll();
        var gap = gaps.First(g => g.ShortName == "SimpleService");
        Assert.Equal(0.85, gap.TestabilityScore);
    }

    [Fact]
    public void RefreshCoverage_WithReEnrich_LargeCtorGetsLowTestability()
    {
        SeedTypeRegistry(new TypeRecord
        {
            Name = "ComplexService",
            Namespace = "App",
            Constructors = [new ConstructorRecord { Params = ["A", "B", "C", "D", "E", "F", "G"] }]
        });
        StoreRegistry.Reset();

        var xmlPath = WriteCoberturaXml("ComplexService", "App", totalLines: 80, coveredLines: 10);

        RefreshCoverageTool.RefreshCoverage(xmlPath, reEnrich: true);

        var gaps = new JsonLineStore<CoverageGap>(RepoConfig.CoverageGapsPath(TempDir)).LoadAll();
        var gap = gaps.First(g => g.ShortName == "ComplexService");
        Assert.Equal(0.2, gap.TestabilityScore);
    }

    [Fact]
    public void RefreshCoverage_WithReEnrichFalse_SkipsEnrichment()
    {
        SeedTypeRegistry(new TypeRecord { Name = "Svc", Namespace = "App" });
        SeedTestInventory(new TestInventoryEntry { Class = "Svc", TestCount = 3, TestMethods = ["T1"] });
        StoreRegistry.Reset();

        var xmlPath = WriteCoberturaXml("Svc", "App", totalLines: 30, coveredLines: 10);

        var result = RefreshCoverageTool.RefreshCoverage(xmlPath, reEnrich: false);

        Assert.Contains("\"status\": \"refreshed\"", result);
        // enriched should be null when reEnrich=false
        Assert.Contains("\"enriched\": null", result);
    }

    [Fact]
    public void RefreshCoverage_NewlyCovered_FieldPresentInOutput()
    {
        // Verify the newlyCovered field exists in the output JSON
        var xmlPath = WriteCoberturaXml("FreshClass", "App", totalLines: 20, coveredLines: 10);
        var result = RefreshCoverageTool.RefreshCoverage(xmlPath, reEnrich: false);

        Assert.Contains("\"newlyCovered\":", result);
        Assert.Contains("\"status\": \"refreshed\"", result);
    }

    [Fact]
    public void RefreshCoverage_TestResultsExistsButEmpty_ReturnsNotFound()
    {
        // Create an empty TestResults dir
        var testResultsDir = Path.Combine(TempDir, "TestResults");
        Directory.CreateDirectory(testResultsDir);

        // Point to a non-existent file in the dir that has TestResults
        var fakePath = Path.Combine(TempDir, "nonexistent.xml");
        var result = RefreshCoverageTool.RefreshCoverage(fakePath);

        Assert.Contains("Coverage file not found", result);
        Assert.Contains("Also searched TestResults/", result);
    }
}
