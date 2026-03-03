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
            Class = "Worker",
            Namespace = "App",
            TotalLines = 100,
            CoveredLines = 40,
            UncoveredLines = 60,
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
}
