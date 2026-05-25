using System.Text;
using Total.Recall.Models;

namespace Total.Recall.Tools.Scaffold;

/// <summary>
/// Pure helpers for transforming method names and signatures into identifiers used by
/// the test scaffold generator: sanitized test method names, async-method detection,
/// and disambiguation of overloads by parameter-type suffix.
/// </summary>
internal static class MethodNaming
{
    /// <summary>
    /// Well-known async method names that don't follow the *Async suffix convention.
    /// </summary>
    private static readonly HashSet<string> s_asyncMethodNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "ExecuteAsync", "InvokeAsync", "RunAsync", "StartAsync", "StopAsync",
        "ReadAsync", "WriteAsync", "SendAsync", "ReceiveAsync",
        "InitializeAsync", "DisposeAsync", "LoadAsync", "SaveAsync",
        "ConnectAsync", "DisconnectAsync", "ProcessAsync", "HandleAsync",
        "ValidateAsync", "ConfigureAsync"
    };

    /// <summary>
    /// Sanitize a method name for use as a test method name.
    /// Handles property accessors (get_/set_), strips Async suffix,
    /// and removes non-alphanumeric characters.
    /// </summary>
    public static string Sanitize(string methodName)
    {
        if (methodName.StartsWith("get_"))
            return $"Get{methodName[4..]}";
        if (methodName.StartsWith("set_"))
            return $"Set{methodName[4..]}";

        var name = methodName;
        if (name.EndsWith("Async") && name.Length > 5)
            name = name[..^5];

        var sb = new StringBuilder();
        foreach (var ch in name)
        {
            if (char.IsLetterOrDigit(ch) || ch == '_')
                sb.Append(ch);
        }

        var result = sb.ToString();
        return string.IsNullOrEmpty(result) ? "Method" : result;
    }

    /// <summary>
    /// Heuristic to detect if a method is async based on naming conventions and type info.
    /// Methods named *Async or returning Task/Task&lt;T&gt; are treated as async.
    /// </summary>
    public static bool IsAsync(string methodName, TypeRecord? typeRecord)
    {
        if (methodName.EndsWith("Async", StringComparison.Ordinal))
            return true;

        if (s_asyncMethodNames.Contains(methodName))
            return true;

        var baseName = methodName;
        if (baseName.StartsWith("get_") || baseName.StartsWith("set_"))
            baseName = baseName[4..];

        if (typeRecord?.Interfaces?.Any(i =>
            i.Contains("IAsync", StringComparison.OrdinalIgnoreCase)) == true)
        {
            if (baseName.Contains("Async", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Extract a human-readable parameter suffix from a Cobertura CLR signature.
    /// Examples:
    ///   "(System.Object)System.Boolean" -> "Object"
    ///   "(System.String, System.Int32)System.Void" -> "String_Int32"
    ///   "()" or "" -> "NoArgs"
    /// </summary>
    public static string ExtractParamSuffix(string signature)
    {
        if (string.IsNullOrEmpty(signature))
            return "NoArgs";

        var openParen = signature.IndexOf('(');
        var closeParen = signature.IndexOf(')');

        if (openParen < 0 || closeParen <= openParen + 1)
            return "NoArgs";

        var paramSection = signature[(openParen + 1)..closeParen].Trim();
        if (string.IsNullOrEmpty(paramSection))
            return "NoArgs";

        var paramTypes = paramSection.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var parts = new List<string>();
        foreach (var param in paramTypes)
        {
            // "System.Object" -> "Object", "System.Collections.Generic.List`1" -> "List"
            var shortName = param;
            var lastDot = param.LastIndexOf('.');
            if (lastDot >= 0 && lastDot < param.Length - 1)
                shortName = param[(lastDot + 1)..];

            var backtick = shortName.IndexOf('`');
            if (backtick > 0)
                shortName = shortName[..backtick];

            var clean = new string(shortName.Where(c => char.IsLetterOrDigit(c) || c == '_').ToArray());
            if (!string.IsNullOrEmpty(clean))
                parts.Add(clean);
        }

        return parts.Count > 0 ? string.Join("_", parts) : "NoArgs";
    }

    /// <summary>
    /// Build a disambiguation map for overloaded methods.
    /// When multiple UncoveredMethods share the same sanitized test name, appends parameter
    /// type info to differentiate them. Single-instance methods keep the simpler name.
    /// </summary>
    public static Dictionary<UncoveredMethod, string> BuildDisambiguatedNames(IList<UncoveredMethod> methods)
    {
        var result = new Dictionary<UncoveredMethod, string>();

        var groups = methods
            .GroupBy(m => Sanitize(m.Name), StringComparer.Ordinal)
            .ToList();

        foreach (var grp in groups)
        {
            var list = grp.ToList();
            if (list.Count == 1)
            {
                result[list[0]] = grp.Key;
                continue;
            }

            foreach (var method in list)
            {
                var suffix = ExtractParamSuffix(method.Signature);
                result[method] = $"{grp.Key}_{suffix}";
            }

            // Fallback: numeric suffixes if param-type disambiguation still collides
            var usedNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (var method in list)
            {
                var name = result[method];
                if (!usedNames.Add(name))
                {
                    var counter = 2;
                    while (!usedNames.Add($"{name}_{counter}"))
                        counter++;
                    result[method] = $"{name}_{counter}";
                }
            }
        }

        return result;
    }
}
