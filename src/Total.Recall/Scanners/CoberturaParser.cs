using System.Xml.Linq;
using Total.Recall.Infrastructure;
using Total.Recall.Models;

namespace Total.Recall.Scanners;

/// <summary>
/// Parses a Cobertura XML coverage report and writes coverage-gaps.jsonl.
/// </summary>
public static class CoberturaParser
{
    /// <summary>
    /// Parse Cobertura XML and write coverage-gaps.jsonl.
    /// Returns the number of classes parsed.
    /// </summary>
    public static int Parse(string coberturaPath, string dataDir)
    {
        if (!File.Exists(coberturaPath))
            throw new FileNotFoundException($"Cobertura XML not found: {coberturaPath}");

        var doc = XDocument.Load(coberturaPath);
        var records = new List<CoverageGap>();

        var packages = doc.Descendants("package");
        foreach (var pkg in packages)
        {
            var classes = pkg.Descendants("class");
            foreach (var cls in classes)
            {
                var record = ParseClass(cls);
                if (record is not null)
                    records.Add(record);
            }
        }

        // Deduplicate by class name (Cobertura sometimes duplicates partial classes)
        records = records
            .GroupBy(r => r.Class)
            .Select(g =>
            {
                if (g.Count() == 1) return g.First();
                // Merge: sum lines, combine uncovered methods
                var first = g.First();
                first.TotalLines = g.Sum(x => x.TotalLines);
                first.CoveredLines = g.Sum(x => x.CoveredLines);
                first.UncoveredLines = g.Sum(x => x.UncoveredLines);
                first.CoveragePercent = first.TotalLines > 0
                    ? Math.Round(100.0 * first.CoveredLines / first.TotalLines, 2)
                    : 0;
                first.UncoveredMethods = g.SelectMany(x => x.UncoveredMethods).ToList();
                return first;
            })
            .OrderByDescending(r => r.UncoveredLines)
            .ToList();

        var store = new JsonLineStore<CoverageGap>(RepoConfig.CoverageGapsPath(dataDir));
        store.WriteAll(records);

        return records.Count;
    }

    private static CoverageGap? ParseClass(XElement cls)
    {
        var fullName = cls.Attribute("name")?.Value;
        var fileName = cls.Attribute("filename")?.Value;

        if (string.IsNullOrEmpty(fullName))
            return null;

        // Split fully qualified name into namespace + class
        var lastDot = fullName.LastIndexOf('.');
        var className = lastDot >= 0 ? fullName[(lastDot + 1)..] : fullName;
        var ns = lastDot >= 0 ? fullName[..lastDot] : "";

        // Parse lines
        var lines = cls.Descendants("line").ToList();
        var totalLines = lines.Count;
        var coveredLines = lines.Count(l => int.Parse(l.Attribute("hits")?.Value ?? "0") > 0);
        var uncoveredLines = totalLines - coveredLines;

        if (totalLines == 0)
            return null;

        var coveragePercent = Math.Round(100.0 * coveredLines / totalLines, 2);

        // Parse uncovered methods
        var uncoveredMethods = ParseUncoveredMethods(cls);

        return new CoverageGap
        {
            Class = className,
            Namespace = ns,
            File = fileName ?? "",
            TotalLines = totalLines,
            CoveredLines = coveredLines,
            UncoveredLines = uncoveredLines,
            CoveragePercent = coveragePercent,
            UncoveredMethods = uncoveredMethods,
            ExistingTestCount = 0, // Will be enriched later
            Testability = "unknown"
        };
    }

    private static List<UncoveredMethod> ParseUncoveredMethods(XElement cls)
    {
        var result = new List<UncoveredMethod>();

        var methods = cls.Element("methods")?.Elements("method") ?? [];
        foreach (var method in methods)
        {
            var methodName = method.Attribute("name")?.Value ?? "unknown";
            var methodLines = method.Descendants("line").ToList();

            if (methodLines.Count == 0)
                continue;

            var uncoveredLineNums = methodLines
                .Where(l => int.Parse(l.Attribute("hits")?.Value ?? "0") == 0)
                .Select(l => int.Parse(l.Attribute("number")?.Value ?? "0"))
                .ToList();

            if (uncoveredLineNums.Count == 0)
                continue;

            var allLineNums = methodLines
                .Select(l => int.Parse(l.Attribute("number")?.Value ?? "0"))
                .ToList();

            result.Add(new UncoveredMethod
            {
                Name = methodName,
                StartLine = allLineNums.Min(),
                EndLine = allLineNums.Max(),
                UncoveredLines = uncoveredLineNums.Count
            });
        }

        return result;
    }
}
