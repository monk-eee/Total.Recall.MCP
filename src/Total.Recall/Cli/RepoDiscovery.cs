using System.Text.RegularExpressions;

namespace Total.Recall.Cli;

/// <summary>
/// Pure discovery helpers for the <c>init</c> sub-command: given a repo root,
/// guess the production assembly, the test project, the newest Cobertura
/// coverage XML, and the source root. All methods are static and side-effect
/// free aside from filesystem reads — they do not write files, build projects,
/// or invoke the scanner. The <c>init</c> command composes them and writes
/// <c>config.json</c>.
/// </summary>
internal static class RepoDiscovery
{
    /// <summary>
    /// Discover repo layout starting from <paramref name="repoRoot"/>. Returns a
    /// record describing what was found; any field may be null if discovery failed.
    /// </summary>
    public static DiscoveryResult Discover(string repoRoot)
    {
        if (string.IsNullOrWhiteSpace(repoRoot))
            throw new ArgumentException("Repo root is required", nameof(repoRoot));

        var fullRoot = Path.GetFullPath(repoRoot);
        if (!Directory.Exists(fullRoot))
            throw new DirectoryNotFoundException($"Repo root not found: {fullRoot}");

        // Find all csproj under repo, excluding bin/obj
        var csprojs = SafeEnumerate(fullRoot, "*.csproj")
            .Where(p => !IsUnderBinOrObj(p))
            .ToList();

        var (productionCsproj, testCsproj) = ClassifyCsprojs(csprojs);

        var assemblyPath = productionCsproj is not null
            ? FindNewestAssembly(productionCsproj)
            : null;

        var testsPath = testCsproj is not null
            ? Path.GetDirectoryName(testCsproj)
            : null;

        var coveragePath = FindNewestCoverageXml(fullRoot);

        var sourceRoot = ResolveSourceRoot(fullRoot, productionCsproj);

        var suggestedNamespace = SuggestNamespace(fullRoot);

        return new DiscoveryResult(
            RepoRoot: fullRoot,
            AssemblyPath: assemblyPath,
            CoveragePath: coveragePath,
            TestsPath: testsPath,
            SourceRoot: sourceRoot,
            ProductionCsproj: productionCsproj,
            TestCsproj: testCsproj,
            SuggestedNamespace: suggestedNamespace);
    }

    /// <summary>
    /// Split csproj paths into (production, test). A csproj is treated as a test
    /// project when its name ends in <c>.Tests</c>, <c>.Test</c>, <c>.UnitTests</c>,
    /// <c>.IntegrationTests</c>, OR its content references xunit/nunit/mstest test
    /// SDK packages, OR its path contains a <c>tests/</c> segment.
    /// </summary>
    public static (string? production, string? test) ClassifyCsprojs(IReadOnlyList<string> csprojs)
    {
        if (csprojs.Count == 0)
            return (null, null);

        var productionCandidates = new List<string>();
        var testCandidates = new List<string>();

        foreach (var path in csprojs)
        {
            if (LooksLikeTestProject(path))
                testCandidates.Add(path);
            else
                productionCandidates.Add(path);
        }

        // Production: prefer csproj whose name appears in a top-level src/ dir;
        // fallback to first by name.
        var production = productionCandidates
            .OrderBy(p => p.IndexOf($"{Path.DirectorySeparatorChar}src{Path.DirectorySeparatorChar}",
                StringComparison.OrdinalIgnoreCase) >= 0 ? 0 : 1)
            .ThenBy(p => Path.GetFileName(p), StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

        // Test: prefer csproj whose name targets the production project, else first.
        string? test = null;
        if (production is not null)
        {
            var prodName = Path.GetFileNameWithoutExtension(production);
            test = testCandidates.FirstOrDefault(t =>
                Path.GetFileNameWithoutExtension(t).StartsWith(prodName, StringComparison.OrdinalIgnoreCase));
        }
        test ??= testCandidates
            .OrderBy(p => Path.GetFileName(p), StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

        return (production, test);
    }

    /// <summary>
    /// Search <paramref name="csprojPath"/>'s bin/ tree for the newest matching
    /// .dll. Considers Debug and Release equally; picks the most recently written.
    /// Returns null if no build output exists.
    /// </summary>
    public static string? FindNewestAssembly(string csprojPath)
    {
        var dir = Path.GetDirectoryName(csprojPath);
        if (dir is null) return null;

        var binDir = Path.Combine(dir, "bin");
        if (!Directory.Exists(binDir))
            return null;

        var assemblyName = Path.GetFileNameWithoutExtension(csprojPath) + ".dll";
        var candidates = SafeEnumerate(binDir, assemblyName).ToList();
        if (candidates.Count == 0)
            return null;

        return candidates
            .Select(p => new FileInfo(p))
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .First()
            .FullName;
    }

    /// <summary>
    /// Find the newest <c>coverage.cobertura.xml</c> anywhere under the repo.
    /// Cobertura coverage runs land under <c>TestResults/&lt;guid&gt;/</c>.
    /// </summary>
    public static string? FindNewestCoverageXml(string repoRoot)
    {
        var candidates = SafeEnumerate(repoRoot, "coverage.cobertura.xml").ToList();
        if (candidates.Count == 0)
            return null;

        return candidates
            .Select(p => new FileInfo(p))
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .First()
            .FullName;
    }

    /// <summary>
    /// Pick a source-root directory. Prefers a top-level <c>src/</c>; else the
    /// directory containing the production csproj; else the repo root.
    /// </summary>
    public static string ResolveSourceRoot(string repoRoot, string? productionCsproj)
    {
        var srcDir = Path.Combine(repoRoot, "src");
        if (Directory.Exists(srcDir))
            return srcDir;

        if (productionCsproj is not null)
        {
            var parent = Path.GetDirectoryName(productionCsproj);
            if (parent is not null && Directory.Exists(parent))
                return parent;
        }

        return repoRoot;
    }

    /// <summary>
    /// Suggest a namespace name from the repo directory: lowercase, dots stripped,
    /// non-alphanumerics replaced with <c>-</c>, collapsed.
    /// </summary>
    public static string SuggestNamespace(string repoRoot)
    {
        var leaf = Path.GetFileName(repoRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (string.IsNullOrWhiteSpace(leaf))
            leaf = "default";

        var lower = leaf.ToLowerInvariant();
        var cleaned = Regex.Replace(lower, "[^a-z0-9]+", "-").Trim('-');
        return string.IsNullOrEmpty(cleaned) ? "default" : cleaned;
    }

    private static bool LooksLikeTestProject(string csprojPath)
    {
        var name = Path.GetFileNameWithoutExtension(csprojPath);
        if (name.EndsWith(".Tests", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".Test", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".UnitTests", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".IntegrationTests", StringComparison.OrdinalIgnoreCase))
            return true;

        var pathSeg = $"{Path.DirectorySeparatorChar}tests{Path.DirectorySeparatorChar}";
        if (csprojPath.IndexOf(pathSeg, StringComparison.OrdinalIgnoreCase) >= 0)
            return true;

        try
        {
            var content = File.ReadAllText(csprojPath);
            if (content.IndexOf("<IsTestProject>true</IsTestProject>", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (content.IndexOf("Microsoft.NET.Test.Sdk", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        }
        catch
        {
            // unreadable csproj — fall through and treat as production
        }
        return false;
    }

    private static bool IsUnderBinOrObj(string path)
    {
        var binSeg = $"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}";
        var objSeg = $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}";
        return path.IndexOf(binSeg, StringComparison.OrdinalIgnoreCase) >= 0
            || path.IndexOf(objSeg, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static IEnumerable<string> SafeEnumerate(string root, string pattern)
    {
        try
        {
            return Directory.EnumerateFiles(root, pattern, SearchOption.AllDirectories);
        }
        catch (UnauthorizedAccessException)
        {
            return [];
        }
        catch (DirectoryNotFoundException)
        {
            return [];
        }
    }
}

/// <summary>
/// Result of <see cref="RepoDiscovery.Discover"/>. Any path may be null when the
/// corresponding artefact was not located in the repo tree.
/// </summary>
internal sealed record DiscoveryResult(
    string RepoRoot,
    string? AssemblyPath,
    string? CoveragePath,
    string? TestsPath,
    string SourceRoot,
    string? ProductionCsproj,
    string? TestCsproj,
    string SuggestedNamespace);
