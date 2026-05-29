using System.Text.Json;
using Total.Recall.Infrastructure;
using Total.Recall.Models;

namespace Total.Recall.Tests.Models;

/// <summary>
/// Pins the canonical on-disk schema for coverage-gaps.jsonl per docs/SCANNER_SCHEMA.md §2.
/// The .NET MCP server must read JSONL written by any sibling scanner (Python, TypeScript),
/// so the field names emitted by C# serialization must match the cross-language contract
/// exactly. A drift here silently breaks multi-scanner deployments.
/// </summary>
public class CoverageGapSchemaTests
{
    [Fact]
    public void CoverageGap_Serialization_EmitsCanonicalFieldNames()
    {
        var gap = new CoverageGap
        {
            ClassName = "MyApp.Services.UserService",
            FilePath = "src/MyApp/Services/UserService.cs",
            LinesCovered = 12,
            LinesTotal = 30,
            CoveragePercent = 40.0,
            ExistingTests = 3,
            TestabilityScore = 0.85,
            UncoveredMethods =
            [
                new UncoveredMethod
                {
                    Name = "ProcessOrder",
                    Signature = "(System.Object)System.Boolean",
                    UncoveredLines = [42, 43, 44, 51, 52],
                    TotalLines = 18,
                },
            ],
        };

        var json = JsonSerializer.Serialize(gap, SharedJsonOptions.CamelCase);

        Assert.Contains("\"schemaVersion\":1", json);
        Assert.Contains("\"className\":\"MyApp.Services.UserService\"", json);
        Assert.Contains("\"filePath\":\"src/MyApp/Services/UserService.cs\"", json);
        Assert.Contains("\"linesCovered\":12", json);
        Assert.Contains("\"linesTotal\":30", json);
        Assert.Contains("\"coveragePercent\":40", json);
        Assert.Contains("\"uncoveredMethods\":[", json);
        Assert.Contains("\"existingTests\":3", json);
        Assert.Contains("\"testabilityScore\":0.85", json);
        Assert.Contains("\"name\":\"ProcessOrder\"", json);
        Assert.Contains("\"signature\":\"(System.Object)System.Boolean\"", json);
        Assert.Contains("\"uncoveredLines\":[42,43,44,51,52]", json);
        Assert.Contains("\"totalLines\":18", json);
    }

    [Fact]
    public void CoverageGap_Serialization_DoesNotEmitLegacyFieldNames()
    {
        var gap = new CoverageGap
        {
            ClassName = "App.Foo",
            FilePath = "Foo.cs",
            LinesCovered = 5,
            LinesTotal = 10,
            UncoveredMethods = [new UncoveredMethod { Name = "Bar", UncoveredLines = [1, 2], TotalLines = 4 }],
        };

        var json = JsonSerializer.Serialize(gap, SharedJsonOptions.CamelCase);

        // Legacy property-name fragments removed in the 2.7 canonical-schema realignment.
        Assert.DoesNotContain("\"class\":", json);
        Assert.DoesNotContain("\"namespace\":", json);
        Assert.DoesNotContain("\"file\":", json);
        Assert.DoesNotContain("\"totalLines\":10", json); // gap-level TotalLines is now linesTotal
        Assert.DoesNotContain("\"coveredLines\":", json);
        Assert.DoesNotContain("\"existingTestCount\":", json);
        Assert.DoesNotContain("\"testability\":", json);
        Assert.DoesNotContain("\"skipReason\":", json);
        Assert.DoesNotContain("\"startLine\":", json);
        Assert.DoesNotContain("\"endLine\":", json);
    }

    [Fact]
    public void CoverageGap_Deserialization_ReadsPythonScannerOutput()
    {
        // Exact byte-shape a Python sibling scanner would emit per docs/SCANNER_SCHEMA.md §2.
        var json = """
            {"schemaVersion":1,"className":"myapp.services.OrderProcessor","filePath":"myapp/services/order_processor.py","linesCovered":8,"linesTotal":20,"coveragePercent":40.0,"uncoveredMethods":[{"name":"process","signature":"process(self, order)","uncoveredLines":[10,11,12],"totalLines":6}],"existingTests":null,"testabilityScore":null}
            """;

        var gap = JsonSerializer.Deserialize<CoverageGap>(json, SharedJsonOptions.CamelCase);

        Assert.NotNull(gap);
        Assert.Equal("myapp.services.OrderProcessor", gap!.ClassName);
        Assert.Equal("OrderProcessor", gap.ShortName);
        Assert.Equal("myapp.services", gap.NamespacePart);
        Assert.Equal("myapp/services/order_processor.py", gap.FilePath);
        Assert.Equal(8, gap.LinesCovered);
        Assert.Equal(20, gap.LinesTotal);
        Assert.Equal(12, gap.UncoveredLineCount);
        Assert.Null(gap.ExistingTests);
        Assert.Null(gap.TestabilityScore);

        var method = Assert.Single(gap.UncoveredMethods);
        Assert.Equal("process", method.Name);
        Assert.Equal(3, method.UncoveredLineCount);
        Assert.Equal(10, method.FirstUncoveredLine);
        Assert.Equal(12, method.LastUncoveredLine);
        Assert.Equal(6, method.TotalLines);
    }

    [Fact]
    public void CoverageGap_DerivedNameHelpers_HandleUnqualifiedNames()
    {
        var gap = new CoverageGap { ClassName = "PlainName" };
        Assert.Equal("PlainName", gap.ShortName);
        Assert.Equal("", gap.NamespacePart);
    }

    [Fact]
    public void CoverageGap_DerivedNameHelpers_HandleEmptyClassName()
    {
        var gap = new CoverageGap();
        Assert.Equal("", gap.ShortName);
        Assert.Equal("", gap.NamespacePart);
        Assert.Equal(0, gap.UncoveredLineCount);
    }
}
