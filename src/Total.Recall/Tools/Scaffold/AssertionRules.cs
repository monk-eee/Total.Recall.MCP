using System.Text;
using Total.Recall.Models;

namespace Total.Recall.Tools.Scaffold;

/// <summary>
/// Data-driven rules that translate a method name into:
///   1. An assertion hint (<see cref="GetAssertionHint"/>) shown as a guidance comment.
///   2. Edge-case test stubs (<see cref="AppendEdgeCaseStubs"/>) appended after the main stub.
///
/// Both transformations were originally long if/else chains. They are now expressed as
/// static readonly tables so adding a new pattern is one row, not one branch. The order
/// of rows is significant — the first match wins, mirroring the original short-circuit
/// behaviour of the if-chain.
/// </summary>
internal static class AssertionRules
{
    /// <summary>
    /// Ordered prefix-to-hint rules. First match wins. Prefixes are matched
    /// case-insensitively against the method name (after stripping property-accessor
    /// prefixes and the Async suffix).
    /// </summary>
    private static readonly (string[] Prefixes, string Hint)[] s_prefixHints =
    {
        (new[] { "Validate", "Check", "Verify" },
            "Assert.True/False on validation result; also test with invalid input \u2192 expect false or exception"),
        (new[] { "Get", "Find", "Fetch", "Load", "Read", "Resolve", "Lookup" },
            "Assert.NotNull on result; verify expected return value with Assert.Equal"),
        (new[] { "Is", "Has", "Can", "Should" },
            "Assert.True for positive case; write separate test with Assert.False for negative case"),
        (new[] { "Count", "Size" },
            "Assert.Equal on expected count; also test empty input \u2192 0"),
        (new[] { "Parse", "Convert", "Transform", "Map" },
            "Assert.Equal on expected output; also Assert.Throws for malformed input"),
        (new[] { "Create", "Build", "Make", "New", "Generate" },
            "Assert.NotNull on created object; verify key properties are correctly set"),
        (new[] { "Delete", "Remove", "Clear" },
            "Verify item is removed; mock.Verify the dependency call was made"),
        (new[] { "Add", "Insert", "Register", "Set", "Update", "Save", "Write", "Store" },
            "Verify state change occurred; mock.Verify the dependency was called with correct args"),
        (new[] { "Init", "Setup", "Configure", "Start" },
            "Verify initialization side-effects; check state is ready after call"),
        (new[] { "Dispose", "Close", "Shutdown", "Stop" },
            "Verify resources released; calling methods after dispose should throw ObjectDisposedException"),
        (new[] { "Handle", "Process", "Execute", "Run", "Invoke" },
            "Verify side-effects via mock.Verify; check return value if non-void"),
        (new[] { "Try" },
            "Assert.True for success case returning true; separate test with Assert.False for failure case"),
        (new[] { "Throw", "Ensure", "Require" },
            "Assert.Throws for invalid input; verify no exception for valid input"),
        (new[] { "On" },
            "Verify event handler side-effects via mock.Verify; test with varying event args"),
        (new[] { "Format", "Render", "ToString", "Serialize" },
            "Assert.Equal on expected string output; Assert.Contains for key substrings"),
    };

    private const string DefaultHint =
        "TODO: verify behavior \u2014 Assert.NotNull for queries, mock.Verify for commands";

    /// <summary>
    /// Edge-case rules: when the lower-cased method name contains any of the keywords,
    /// each (suffix, comment) edge-case stub is appended to the test class. Multiple rules
    /// can fire for the same method.
    /// </summary>
    private static readonly (string[] Keywords, (string Suffix, string Comment)[] EdgeCases)[] s_edgeCaseRules =
    {
        (new[] { "name", "path", "text", "input", "parse", "format", "string", "file", "url", "key", "content", "message" },
         new[]
         {
            ("_NullInput_ShouldThrowOrHandle", "// Edge case: pass null string argument \u2014 expect ArgumentNullException or graceful handling"),
            ("_EmptyInput_ShouldHandle", "// Edge case: pass empty string \u2014 verify behavior with string.Empty"),
         }),
        (new[] { "items", "list", "collection", "batch", "all", "many", "multiple", "each", "entries" },
         new[]
         {
            ("_EmptyCollection_ShouldHandle", "// Edge case: pass empty collection \u2014 verify behavior with no items"),
         }),
        (new[] { "count", "index", "size", "limit", "offset", "max", "min", "page", "number" },
         new[]
         {
            ("_ZeroValue_ShouldHandle", "// Edge case: pass zero \u2014 verify boundary behavior"),
            ("_NegativeValue_ShouldThrowOrHandle", "// Edge case: pass negative number \u2014 expect ArgumentOutOfRangeException or graceful handling"),
         }),
    };

    /// <summary>
    /// Returns a method-specific assertion hint based on the method name.
    /// Replaces generic "TODO: verify behavior" with actionable guidance.
    /// </summary>
    public static string GetAssertionHint(string methodName)
    {
        var name = methodName;
        if (name.StartsWith("get_"))
            name = name[4..];
        if (name.StartsWith("set_"))
            return "Assert the property value was stored correctly";

        if (name.EndsWith("Async"))
            name = name[..^5];

        foreach (var (prefixes, hint) in s_prefixHints)
        {
            foreach (var prefix in prefixes)
            {
                if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    return hint;
            }
        }

        return DefaultHint;
    }

    /// <summary>
    /// Appends edge-case test stubs for methods with recognizable name patterns.
    /// </summary>
    public static void AppendEdgeCaseStubs(
        StringBuilder sb,
        string methodName,
        string testName,
        bool isAsync,
        TypeRecord? typeRecord,
        string testAttr = "[Fact]")
    {
        var edgeCases = new List<(string Suffix, string Comment)>();
        var lowerName = methodName.ToLowerInvariant();

        foreach (var (keywords, cases) in s_edgeCaseRules)
        {
            if (keywords.Any(k => lowerName.Contains(k)))
                edgeCases.AddRange(cases);
        }

        foreach (var (suffix, comment) in edgeCases)
        {
            sb.AppendLine();
            sb.AppendLine($"    {testAttr}");
            if (isAsync)
            {
                sb.AppendLine($"    public async Task {testName}{suffix}()");
                sb.AppendLine("    {");
                sb.AppendLine($"        {comment}");
                sb.AppendLine($"        // TODO: await _sut.{methodName}(edgeCaseValue)");
                sb.AppendLine("    }");
            }
            else
            {
                sb.AppendLine($"    public void {testName}{suffix}()");
                sb.AppendLine("    {");
                sb.AppendLine($"        {comment}");
                sb.AppendLine($"        // TODO: _sut.{methodName}(edgeCaseValue)");
                sb.AppendLine("    }");
            }
        }
    }
}
