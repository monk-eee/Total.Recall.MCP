using System.Xml.Linq;
using Total.Recall.Infrastructure;
using Total.Recall.Models;

namespace Total.Recall.Scanners;

/// <summary>
/// Parses a Cobertura XML coverage report and writes coverage-gaps.jsonl.
/// Emits the canonical schema documented in docs/SCANNER_SCHEMA.md so the .NET
/// MCP server can consume JSONL from any language's scanner.
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

        // Deduplicate by FQN. Cobertura sometimes emits one <class> entry per partial-class
        // file fragment, and the merged record needs both line totals and uncovered methods
        // summed across the fragments. FQN keying keeps classes with the same short name
        // in different namespaces (e.g. Item in Models vs Catalog) separate.
        records = records
            .GroupBy(r => r.ClassName, StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                if (g.Count() == 1) return g.First();
                var first = g.First();
                first.LinesTotal = g.Sum(x => x.LinesTotal);
                first.LinesCovered = g.Sum(x => x.LinesCovered);
                first.CoveragePercent = first.LinesTotal > 0
                    ? Math.Round(100.0 * first.LinesCovered / first.LinesTotal, 2)
                    : 0;
                first.UncoveredMethods = g.SelectMany(x => x.UncoveredMethods).ToList();
                return first;
            })
            .OrderByDescending(r => r.UncoveredLineCount)
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

        var lines = cls.Descendants("line").ToList();
        var linesTotal = lines.Count;
        var linesCovered = lines.Count(l => int.Parse(l.Attribute("hits")?.Value ?? "0") > 0);

        if (linesTotal == 0)
            return null;

        var coveragePercent = Math.Round(100.0 * linesCovered / linesTotal, 2);

        var uncoveredMethods = ParseUncoveredMethods(cls);

        return new CoverageGap
        {
            ClassName = fullName,
            FilePath = fileName ?? "",
            LinesTotal = linesTotal,
            LinesCovered = linesCovered,
            CoveragePercent = coveragePercent,
            UncoveredMethods = uncoveredMethods,
            // ExistingTests / TestabilityScore stay null until enrichment runs.
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
                .OrderBy(n => n)
                .ToArray();

            if (uncoveredLineNums.Length == 0)
                continue;

            var signature = method.Attribute("signature")?.Value ?? "";

            result.Add(new UncoveredMethod
            {
                Name = methodName,
                Signature = signature,
                UncoveredLines = uncoveredLineNums,
                TotalLines = methodLines.Count,
            });
        }

        return result;
    }
}
