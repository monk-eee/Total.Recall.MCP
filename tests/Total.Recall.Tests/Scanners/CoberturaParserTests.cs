using Total.Recall.Scanners;

namespace Total.Recall.Tests.Scanners;

public sealed class CoberturaParserTests : IDisposable
{
    private readonly string _tempDir;

    public CoberturaParserTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "total-recall-tests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private string WriteCoberturaXml(string xml)
    {
        var path = Path.Combine(_tempDir, "coverage.xml");
        File.WriteAllText(path, xml);
        return path;
    }

    [Fact]
    public void Parse_FileNotFound_ThrowsFileNotFoundException()
    {
        Assert.Throws<FileNotFoundException>(
            () => CoberturaParser.Parse(Path.Combine(_tempDir, "missing.xml"), _tempDir));
    }

    [Fact]
    public void Parse_SingleClassAllCovered_ReturnsOneRecord()
    {
        var xml = """
            <?xml version="1.0" encoding="utf-8"?>
            <coverage>
              <packages>
                <package name="MyApp">
                  <classes>
                    <class name="MyApp.Service.Calculator" filename="Calculator.cs">
                      <methods>
                        <method name="Add" signature="(int,int)int">
                          <lines>
                            <line number="10" hits="5" />
                            <line number="11" hits="3" />
                          </lines>
                        </method>
                      </methods>
                      <lines>
                        <line number="10" hits="5" />
                        <line number="11" hits="3" />
                      </lines>
                    </class>
                  </classes>
                </package>
              </packages>
            </coverage>
            """;
        var coberturaPath = WriteCoberturaXml(xml);

        var count = CoberturaParser.Parse(coberturaPath, _tempDir);

        Assert.Equal(1, count);
    }

    [Fact]
    public void Parse_ClassWithUncoveredLines_RecordsCoverageGap()
    {
        var xml = """
            <?xml version="1.0" encoding="utf-8"?>
            <coverage>
              <packages>
                <package name="MyApp">
                  <classes>
                    <class name="MyApp.Service.Parser" filename="Parser.cs">
                      <methods>
                        <method name="Parse" signature="(string)void">
                          <lines>
                            <line number="10" hits="2" />
                            <line number="11" hits="0" />
                            <line number="12" hits="0" />
                          </lines>
                        </method>
                      </methods>
                      <lines>
                        <line number="10" hits="2" />
                        <line number="11" hits="0" />
                        <line number="12" hits="0" />
                      </lines>
                    </class>
                  </classes>
                </package>
              </packages>
            </coverage>
            """;
        var coberturaPath = WriteCoberturaXml(xml);

        CoberturaParser.Parse(coberturaPath, _tempDir);

        // Verify the output file was created and can be read
        var outputPath = Path.Combine(_tempDir, "coverage-gaps.jsonl");
        Assert.True(File.Exists(outputPath));
        var lines = File.ReadAllLines(outputPath).Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
        Assert.Single(lines);
        Assert.Contains("Parser", lines[0]);
    }

    [Fact]
    public void Parse_SplitsNamespaceAndClassName()
    {
        var xml = """
            <?xml version="1.0" encoding="utf-8"?>
            <coverage>
              <packages>
                <package name="MyApp">
                  <classes>
                    <class name="MyApp.Deep.Namespace.MyClass" filename="MyClass.cs">
                      <methods />
                      <lines>
                        <line number="1" hits="1" />
                      </lines>
                    </class>
                  </classes>
                </package>
              </packages>
            </coverage>
            """;
        var coberturaPath = WriteCoberturaXml(xml);

        CoberturaParser.Parse(coberturaPath, _tempDir);

        var outputPath = Path.Combine(_tempDir, "coverage-gaps.jsonl");
        var content = File.ReadAllText(outputPath);
        // Class name should be "MyClass", namespace should be "MyApp.Deep.Namespace"
        Assert.Contains("\"class\":\"MyClass\"", content);
        Assert.Contains("\"namespace\":\"MyApp.Deep.Namespace\"", content);
    }

    [Fact]
    public void Parse_DeduplicatesPartialClasses()
    {
        var xml = """
            <?xml version="1.0" encoding="utf-8"?>
            <coverage>
              <packages>
                <package name="MyApp">
                  <classes>
                    <class name="MyApp.Partial" filename="Partial.cs">
                      <methods />
                      <lines>
                        <line number="1" hits="1" />
                        <line number="2" hits="0" />
                      </lines>
                    </class>
                    <class name="MyApp.Partial" filename="Partial.extra.cs">
                      <methods />
                      <lines>
                        <line number="10" hits="1" />
                        <line number="11" hits="1" />
                      </lines>
                    </class>
                  </classes>
                </package>
              </packages>
            </coverage>
            """;
        var coberturaPath = WriteCoberturaXml(xml);

        var count = CoberturaParser.Parse(coberturaPath, _tempDir);

        // Should dedup to 1 record with merged line counts
        Assert.Equal(1, count);
        var outputPath = Path.Combine(_tempDir, "coverage-gaps.jsonl");
        var content = File.ReadAllText(outputPath);
        Assert.Contains("\"totalLines\":4", content);
        Assert.Contains("\"coveredLines\":3", content);
    }

    [Fact]
    public void Parse_MultipleClasses_SortsByUncoveredLinesDescending()
    {
        var xml = """
            <?xml version="1.0" encoding="utf-8"?>
            <coverage>
              <packages>
                <package name="MyApp">
                  <classes>
                    <class name="MyApp.Small" filename="Small.cs">
                      <methods />
                      <lines>
                        <line number="1" hits="0" />
                      </lines>
                    </class>
                    <class name="MyApp.Big" filename="Big.cs">
                      <methods />
                      <lines>
                        <line number="1" hits="0" />
                        <line number="2" hits="0" />
                        <line number="3" hits="0" />
                        <line number="4" hits="0" />
                        <line number="5" hits="0" />
                      </lines>
                    </class>
                    <class name="MyApp.Medium" filename="Medium.cs">
                      <methods />
                      <lines>
                        <line number="1" hits="0" />
                        <line number="2" hits="0" />
                        <line number="3" hits="0" />
                      </lines>
                    </class>
                  </classes>
                </package>
              </packages>
            </coverage>
            """;
        var coberturaPath = WriteCoberturaXml(xml);

        CoberturaParser.Parse(coberturaPath, _tempDir);

        var outputPath = Path.Combine(_tempDir, "coverage-gaps.jsonl");
        var lines = File.ReadAllLines(outputPath).Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
        Assert.Equal(3, lines.Count);
        // Big (5 uncovered) should be first
        Assert.Contains("Big", lines[0]);
        Assert.Contains("Medium", lines[1]);
        Assert.Contains("Small", lines[2]);
    }

    [Fact]
    public void Parse_ClassWithZeroLines_IsSkipped()
    {
        var xml = """
            <?xml version="1.0" encoding="utf-8"?>
            <coverage>
              <packages>
                <package name="MyApp">
                  <classes>
                    <class name="MyApp.Empty" filename="Empty.cs">
                      <methods />
                      <lines />
                    </class>
                    <class name="MyApp.HasLines" filename="HasLines.cs">
                      <methods />
                      <lines>
                        <line number="1" hits="1" />
                      </lines>
                    </class>
                  </classes>
                </package>
              </packages>
            </coverage>
            """;
        var coberturaPath = WriteCoberturaXml(xml);

        var count = CoberturaParser.Parse(coberturaPath, _tempDir);

        Assert.Equal(1, count);
    }

    [Fact]
    public void Parse_UncoveredMethod_RecordsMethodDetails()
    {
        var xml = """
            <?xml version="1.0" encoding="utf-8"?>
            <coverage>
              <packages>
                <package name="MyApp">
                  <classes>
                    <class name="MyApp.Svc" filename="Svc.cs">
                      <methods>
                        <method name="DoWork" signature="()void">
                          <lines>
                            <line number="15" hits="0" />
                            <line number="16" hits="0" />
                            <line number="17" hits="0" />
                          </lines>
                        </method>
                        <method name="AllCovered" signature="()void">
                          <lines>
                            <line number="5" hits="2" />
                          </lines>
                        </method>
                      </methods>
                      <lines>
                        <line number="5" hits="2" />
                        <line number="15" hits="0" />
                        <line number="16" hits="0" />
                        <line number="17" hits="0" />
                      </lines>
                    </class>
                  </classes>
                </package>
              </packages>
            </coverage>
            """;
        var coberturaPath = WriteCoberturaXml(xml);

        CoberturaParser.Parse(coberturaPath, _tempDir);

        var outputPath = Path.Combine(_tempDir, "coverage-gaps.jsonl");
        var content = File.ReadAllText(outputPath);
        // Should contain uncovered method "DoWork" but NOT "AllCovered"
        Assert.Contains("DoWork", content);
        Assert.DoesNotContain("AllCovered", content);
    }

    [Fact]
    public void Parse_ClassWithNoName_IsSkipped()
    {
        var xml = """
            <?xml version="1.0" encoding="utf-8"?>
            <coverage>
              <packages>
                <package name="MyApp">
                  <classes>
                    <class filename="NoName.cs">
                      <methods />
                      <lines>
                        <line number="1" hits="1" />
                      </lines>
                    </class>
                  </classes>
                </package>
              </packages>
            </coverage>
            """;
        var coberturaPath = WriteCoberturaXml(xml);

        var count = CoberturaParser.Parse(coberturaPath, _tempDir);

        Assert.Equal(0, count);
    }

    [Fact]
    public void Parse_MethodWithNoLines_IsSkipped()
    {
        var xml = """
            <?xml version="1.0" encoding="utf-8"?>
            <coverage>
              <packages>
                <package name="MyApp">
                  <classes>
                    <class name="MyApp.Service.Empty" filename="Empty.cs">
                      <methods>
                        <method name="NoLines" signature="()void">
                          <lines />
                        </method>
                        <method name="HasLines" signature="()void">
                          <lines>
                            <line number="10" hits="0" />
                          </lines>
                        </method>
                      </methods>
                      <lines>
                        <line number="10" hits="0" />
                      </lines>
                    </class>
                  </classes>
                </package>
              </packages>
            </coverage>
            """;
        var coberturaPath = WriteCoberturaXml(xml);

        CoberturaParser.Parse(coberturaPath, _tempDir);

        var content = File.ReadAllText(Path.Combine(_tempDir, "coverage-gaps.jsonl"));
        // The method with no lines should be skipped; only HasLines should appear
        Assert.DoesNotContain("NoLines", content);
        Assert.Contains("HasLines", content);
    }

    [Fact]
    public void Parse_CoveragePercentCalculatedCorrectly()
    {
        var xml = """
            <?xml version="1.0" encoding="utf-8"?>
            <coverage>
              <packages>
                <package name="MyApp">
                  <classes>
                    <class name="MyApp.Calc" filename="Calc.cs">
                      <methods />
                      <lines>
                        <line number="1" hits="1" />
                        <line number="2" hits="1" />
                        <line number="3" hits="0" />
                        <line number="4" hits="0" />
                      </lines>
                    </class>
                  </classes>
                </package>
              </packages>
            </coverage>
            """;
        var coberturaPath = WriteCoberturaXml(xml);

        CoberturaParser.Parse(coberturaPath, _tempDir);

        var outputPath = Path.Combine(_tempDir, "coverage-gaps.jsonl");
        var content = File.ReadAllText(outputPath);
        // 2 covered / 4 total = 50%
        Assert.Contains("\"coveragePercent\":50", content);
    }

    // ── Namespace-qualified deduplication ──

    [Fact]
    public void Parse_SameClassDifferentNamespaces_KeptSeparate()
    {
        var xml = """
            <?xml version="1.0" encoding="utf-8"?>
            <coverage>
              <packages>
                <package name="MyApp">
                  <classes>
                    <class name="MyApp.Models.ZonePivot" filename="Models/ZonePivot.cs">
                      <methods />
                      <lines>
                        <line number="1" hits="0" />
                        <line number="2" hits="0" />
                      </lines>
                    </class>
                    <class name="MyApp.ContentBlocks.ZonePivot" filename="ContentBlocks/ZonePivot.cs">
                      <methods />
                      <lines>
                        <line number="10" hits="0" />
                        <line number="11" hits="0" />
                        <line number="12" hits="0" />
                      </lines>
                    </class>
                  </classes>
                </package>
              </packages>
            </coverage>
            """;
        var coberturaPath = WriteCoberturaXml(xml);

        var count = CoberturaParser.Parse(coberturaPath, _tempDir);

        // Two separate records — should NOT be merged
        Assert.Equal(2, count);
    }

    [Fact]
    public void Parse_SameClassSameNamespace_StillMergedAsPartials()
    {
        var xml = """
            <?xml version="1.0" encoding="utf-8"?>
            <coverage>
              <packages>
                <package name="MyApp">
                  <classes>
                    <class name="MyApp.Service.Worker" filename="Worker.cs">
                      <methods />
                      <lines>
                        <line number="1" hits="1" />
                        <line number="2" hits="0" />
                      </lines>
                    </class>
                    <class name="MyApp.Service.Worker" filename="Worker.Generated.cs">
                      <methods />
                      <lines>
                        <line number="10" hits="1" />
                        <line number="11" hits="0" />
                      </lines>
                    </class>
                  </classes>
                </package>
              </packages>
            </coverage>
            """;
        var coberturaPath = WriteCoberturaXml(xml);

        var count = CoberturaParser.Parse(coberturaPath, _tempDir);

        // Same namespace + same class → merged as partial
        Assert.Equal(1, count);
        var content = File.ReadAllText(Path.Combine(_tempDir, "coverage-gaps.jsonl"));
        Assert.Contains("\"totalLines\":4", content);
    }
}
