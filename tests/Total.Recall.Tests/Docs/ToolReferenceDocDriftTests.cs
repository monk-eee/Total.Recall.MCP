using System.Reflection;
using System.Text;
using ModelContextProtocol.Server;

namespace Total.Recall.Tests.Docs;

/// <summary>
/// Guards against documentation drift: every public method decorated with
/// [McpServerTool] in the production assembly must be referenced by its
/// snake_case tool name in docs/TOOL_REFERENCE.md.
///
/// When this test fails, the fix is to update the docs — never to silence the
/// test. The contract for an MCP server is the tool surface; docs that lie
/// about it train agents wrong.
/// </summary>
public class ToolReferenceDocDriftTests
{
    [Fact]
    public void EveryMcpServerTool_IsDocumentedIn_ToolReferenceMd()
    {
        var assembly = typeof(Total.Recall.Tools.MetricsTool).Assembly;
        var toolNames = DiscoverToolNames(assembly);

        Assert.NotEmpty(toolNames);

        var docPath = LocateToolReferenceMd();
        var doc = File.ReadAllText(docPath);

        var missing = new List<string>();
        foreach (var name in toolNames)
        {
            // Tool name should appear at least once in the doc (as a heading,
            // inline code, or anywhere). We require an exact substring match
            // so partial-name collisions don't pass.
            if (!doc.Contains(name, StringComparison.Ordinal))
            {
                missing.Add(name);
            }
        }

        if (missing.Count > 0)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"docs/TOOL_REFERENCE.md is missing entries for {missing.Count} tool(s):");
            foreach (var name in missing)
            {
                sb.AppendLine($"  - {name}");
            }
            sb.AppendLine();
            sb.AppendLine("Fix: add a section for each missing tool to docs/TOOL_REFERENCE.md.");
            sb.AppendLine("(See AGENTS.md 'Doc discipline (NON-NEGOTIABLE)'.)");
            Assert.Fail(sb.ToString());
        }
    }

    [Fact]
    public void ToolReferenceMd_DoesNotReferenceRetiredTools()
    {
        var assembly = typeof(Total.Recall.Tools.MetricsTool).Assembly;
        var live = new HashSet<string>(DiscoverToolNames(assembly), StringComparer.Ordinal);

        var docPath = LocateToolReferenceMd();
        var doc = File.ReadAllText(docPath);

        // Find every `^### tool_name` or `^## tool_name` heading that looks like
        // a tool reference (lowercase + underscores only). Compare against the
        // live set; anything left is a retired tool the docs still claim exists.
        var headingRegex = new System.Text.RegularExpressions.Regex(
            @"^#{2,3}\s+(?<name>[a-z][a-z0-9_]+)\s*$",
            System.Text.RegularExpressions.RegexOptions.Multiline);

        var stale = new List<string>();
        foreach (System.Text.RegularExpressions.Match match in headingRegex.Matches(doc))
        {
            var name = match.Groups["name"].Value;
            // Skip section headings that look like tool names but aren't (these
            // are an allowlist of false positives in the doc structure).
            if (name is "purpose" or "parameters" or "returns" or "example"
                or "notes" or "scoring" or "scoring_formula") continue;
            if (!live.Contains(name)) stale.Add(name);
        }

        Assert.True(stale.Count == 0,
            $"docs/TOOL_REFERENCE.md references tools that no longer exist: {string.Join(", ", stale)}. " +
            "Either restore the tool or remove the section from the docs.");
    }

    [Fact]
    public void ToolCountInDocs_MatchesLiveToolCount()
    {
        var assembly = typeof(Total.Recall.Tools.MetricsTool).Assembly;
        var live = DiscoverToolNames(assembly).Count;

        var docPath = LocateToolReferenceMd();
        var doc = File.ReadAllText(docPath);

        // Header line claims "all N MCP tools" — find the integer, assert match.
        var headerRegex = new System.Text.RegularExpressions.Regex(@"all\s+(?<n>\d+)\s+MCP tools",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        var headerMatch = headerRegex.Match(doc);
        Assert.True(headerMatch.Success,
            "TOOL_REFERENCE.md header should declare 'all N MCP tools' so the count is visible to readers.");

        var declared = int.Parse(headerMatch.Groups["n"].Value);
        Assert.True(declared == live,
            $"TOOL_REFERENCE.md header claims {declared} tools but the assembly exposes {live}. " +
            "Update the header (and likely also README.md / AGENTS.md tool count tables).");
    }

    private static List<string> DiscoverToolNames(Assembly assembly)
    {
        var names = new List<string>();
        foreach (var type in assembly.GetTypes())
        {
            if (type.GetCustomAttribute<McpServerToolTypeAttribute>() is null) continue;
            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Static))
            {
                if (method.GetCustomAttribute<McpServerToolAttribute>() is null) continue;
                names.Add(ToSnakeCase(method.Name));
            }
        }
        names.Sort(StringComparer.Ordinal);
        return names;
    }

    private static string ToSnakeCase(string pascalCase)
    {
        var sb = new StringBuilder(pascalCase.Length + 8);
        for (int i = 0; i < pascalCase.Length; i++)
        {
            char c = pascalCase[i];
            if (char.IsUpper(c) && i > 0) sb.Append('_');
            sb.Append(char.ToLowerInvariant(c));
        }
        return sb.ToString();
    }

    private static string LocateToolReferenceMd()
    {
        // Walk up from the test binary directory until we find docs/TOOL_REFERENCE.md.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "docs", "TOOL_REFERENCE.md");
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        throw new FileNotFoundException(
            "Could not locate docs/TOOL_REFERENCE.md by walking up from the test binary directory. " +
            "Make sure the test runs from inside the Total.Recall repo.");
    }
}
