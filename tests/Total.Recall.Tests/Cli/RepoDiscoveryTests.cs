using Total.Recall.Cli;

namespace Total.Recall.Tests.Cli;

/// <summary>
/// Tests for <see cref="RepoDiscovery"/>. Each test builds a synthetic repo
/// layout under a temp directory and verifies the discovery heuristics.
/// </summary>
public class RepoDiscoveryTests : IDisposable
{
    private readonly string _tempRoot;

    public RepoDiscoveryTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "tr-discover-tests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempRoot)) Directory.Delete(_tempRoot, recursive: true); }
        catch { /* best effort */ }
    }

    [Fact]
    public void Discover_StandardSrcTestsLayout_FindsAllArtefacts()
    {
        // src/MyApp/MyApp.csproj + bin/Debug/net8.0/MyApp.dll
        // tests/MyApp.Tests/MyApp.Tests.csproj
        // TestResults/<guid>/coverage.cobertura.xml
        var srcDir = Path.Combine(_tempRoot, "src", "MyApp");
        var testDir = Path.Combine(_tempRoot, "tests", "MyApp.Tests");
        var binDir = Path.Combine(srcDir, "bin", "Debug", "net8.0");
        var coverageDir = Path.Combine(_tempRoot, "TestResults", Guid.NewGuid().ToString());
        Directory.CreateDirectory(binDir);
        Directory.CreateDirectory(testDir);
        Directory.CreateDirectory(coverageDir);
        File.WriteAllText(Path.Combine(srcDir, "MyApp.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        File.WriteAllText(Path.Combine(testDir, "MyApp.Tests.csproj"),
            "<Project Sdk=\"Microsoft.NET.Sdk\"><ItemGroup><PackageReference Include=\"Microsoft.NET.Test.Sdk\" Version=\"17\" /></ItemGroup></Project>");
        File.WriteAllText(Path.Combine(binDir, "MyApp.dll"), "fake binary");
        File.WriteAllText(Path.Combine(coverageDir, "coverage.cobertura.xml"), "<coverage/>");

        var result = RepoDiscovery.Discover(_tempRoot);

        Assert.Equal(Path.Combine(srcDir, "MyApp.csproj"), result.ProductionCsproj);
        Assert.Equal(Path.Combine(testDir, "MyApp.Tests.csproj"), result.TestCsproj);
        Assert.Equal(Path.Combine(binDir, "MyApp.dll"), result.AssemblyPath);
        Assert.Equal(Path.Combine(coverageDir, "coverage.cobertura.xml"), result.CoveragePath);
        Assert.Equal(testDir, result.TestsPath);
        // Prefers top-level src/ over csproj parent dir
        Assert.Equal(Path.Combine(_tempRoot, "src"), result.SourceRoot);
    }

    [Fact]
    public void Discover_EmptyRepo_ReturnsNullsButSourceRootStillRepoRoot()
    {
        var result = RepoDiscovery.Discover(_tempRoot);
        Assert.Null(result.ProductionCsproj);
        Assert.Null(result.TestCsproj);
        Assert.Null(result.AssemblyPath);
        Assert.Null(result.CoveragePath);
        Assert.Null(result.TestsPath);
        Assert.Equal(_tempRoot, result.SourceRoot);
    }

    [Fact]
    public void Discover_MissingRepo_Throws()
    {
        var missing = Path.Combine(_tempRoot, "does-not-exist");
        Assert.Throws<DirectoryNotFoundException>(() => RepoDiscovery.Discover(missing));
    }

    [Fact]
    public void Discover_OnlyTestProject_NoProduction()
    {
        var testDir = Path.Combine(_tempRoot, "tests", "Foo.Tests");
        Directory.CreateDirectory(testDir);
        File.WriteAllText(Path.Combine(testDir, "Foo.Tests.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\" />");

        var result = RepoDiscovery.Discover(_tempRoot);
        Assert.Null(result.ProductionCsproj);
        Assert.NotNull(result.TestCsproj);
        Assert.Null(result.AssemblyPath); // no production csproj → no assembly lookup
    }

    [Fact]
    public void Discover_TestCsprojDetectedByIsTestProjectMarker()
    {
        var dir = Path.Combine(_tempRoot, "weird");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "WeirdName.csproj"),
            "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><IsTestProject>true</IsTestProject></PropertyGroup></Project>");

        var result = RepoDiscovery.Discover(_tempRoot);
        Assert.Null(result.ProductionCsproj);
        Assert.Equal(Path.Combine(dir, "WeirdName.csproj"), result.TestCsproj);
    }

    [Fact]
    public void Discover_PicksNewestCoverageXmlAcrossMultipleRuns()
    {
        var run1 = Path.Combine(_tempRoot, "TestResults", "older");
        var run2 = Path.Combine(_tempRoot, "TestResults", "newer");
        Directory.CreateDirectory(run1);
        Directory.CreateDirectory(run2);
        var older = Path.Combine(run1, "coverage.cobertura.xml");
        var newer = Path.Combine(run2, "coverage.cobertura.xml");
        File.WriteAllText(older, "<old/>");
        File.WriteAllText(newer, "<new/>");
        // Force older to actually be older
        File.SetLastWriteTimeUtc(older, DateTime.UtcNow.AddHours(-2));
        File.SetLastWriteTimeUtc(newer, DateTime.UtcNow);

        var result = RepoDiscovery.Discover(_tempRoot);
        Assert.Equal(newer, result.CoveragePath);
    }

    [Fact]
    public void Discover_PicksNewestAssemblyAcrossDebugAndRelease()
    {
        var srcDir = Path.Combine(_tempRoot, "src", "App");
        var debugBin = Path.Combine(srcDir, "bin", "Debug", "net8.0");
        var releaseBin = Path.Combine(srcDir, "bin", "Release", "net8.0");
        Directory.CreateDirectory(debugBin);
        Directory.CreateDirectory(releaseBin);
        File.WriteAllText(Path.Combine(srcDir, "App.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        var debugDll = Path.Combine(debugBin, "App.dll");
        var releaseDll = Path.Combine(releaseBin, "App.dll");
        File.WriteAllText(debugDll, "old");
        File.WriteAllText(releaseDll, "new");
        File.SetLastWriteTimeUtc(debugDll, DateTime.UtcNow.AddHours(-2));
        File.SetLastWriteTimeUtc(releaseDll, DateTime.UtcNow);

        var result = RepoDiscovery.Discover(_tempRoot);
        Assert.Equal(releaseDll, result.AssemblyPath);
    }

    [Fact]
    public void Discover_BinAndObjCsprojsAreIgnored()
    {
        // Real csproj
        var realDir = Path.Combine(_tempRoot, "src", "Real");
        Directory.CreateDirectory(realDir);
        File.WriteAllText(Path.Combine(realDir, "Real.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\" />");

        // Stale csproj inside bin/ (e.g. NuGet restore artefact). Must be ignored.
        var binCsproj = Path.Combine(_tempRoot, "src", "Real", "bin", "Debug", "weird", "InsideBin.csproj");
        Directory.CreateDirectory(Path.GetDirectoryName(binCsproj)!);
        File.WriteAllText(binCsproj, "<Project Sdk=\"Microsoft.NET.Sdk\" />");

        var result = RepoDiscovery.Discover(_tempRoot);
        Assert.Equal(Path.Combine(realDir, "Real.csproj"), result.ProductionCsproj);
    }

    [Fact]
    public void SuggestNamespace_FromRepoRoot_LowercasesAndSluggifies()
    {
        var dir = Path.Combine(_tempRoot, "My.Cool Project!");
        Directory.CreateDirectory(dir);
        var ns = RepoDiscovery.SuggestNamespace(dir);
        Assert.Equal("my-cool-project", ns);
    }

    [Fact]
    public void ClassifyCsprojs_PrefersProductionCsprojUnderSrc()
    {
        var inSrc = Path.Combine(_tempRoot, "src", "A", "A.csproj");
        var inRoot = Path.Combine(_tempRoot, "B", "B.csproj");
        Directory.CreateDirectory(Path.GetDirectoryName(inSrc)!);
        Directory.CreateDirectory(Path.GetDirectoryName(inRoot)!);
        File.WriteAllText(inSrc, "<Project/>");
        File.WriteAllText(inRoot, "<Project/>");

        var (production, _) = RepoDiscovery.ClassifyCsprojs(new[] { inRoot, inSrc });
        Assert.Equal(inSrc, production);
    }
}
