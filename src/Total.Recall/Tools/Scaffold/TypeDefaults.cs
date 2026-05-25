namespace Total.Recall.Tools.Scaffold;

/// <summary>
/// Catalog of C# primitive and common-framework type names and the default-value
/// literals used to initialize them in generated test scaffolds.
///
/// Two responsibilities:
///   1. <see cref="IsCommon"/>  — "is this a type I do NOT need to mock?" used by the
///      anti-pattern detector to decide whether a concrete constructor parameter
///      represents a real dependency or a primitive value.
///   2. <see cref="DefaultLiteral"/>  — "what literal initializer should I emit for
///      a non-interface constructor parameter of this type?" used by the planner
///      when building concrete-field initializers.
///
/// Both methods share one source-of-truth list so the two views never drift.
/// </summary>
internal static class TypeDefaults
{
    private static readonly HashSet<string> s_commonScalars = new(StringComparer.Ordinal)
    {
        "string", "int", "long", "bool", "double", "float", "decimal",
        "byte", "short", "char", "uint", "ulong", "ushort",
        "Guid", "DateTime", "DateTimeOffset", "TimeSpan", "Uri",
        "CancellationToken", "Stream", "Type", "object"
    };

    private static readonly string[] s_commonGenericPrefixes =
    {
        "List<", "Dictionary<",
        "IList<", "IEnumerable<", "IReadOnlyList<", "ICollection<", "IDictionary<",
        "Func<", "Action"
    };

    /// <summary>
    /// True if the type is a CLR primitive or a common framework type that does not
    /// need mocking (collections, delegates, well-known value types).
    /// </summary>
    public static bool IsCommon(string typeName)
    {
        var baseType = typeName.TrimEnd('?');
        if (s_commonScalars.Contains(baseType))
            return true;
        foreach (var prefix in s_commonGenericPrefixes)
        {
            if (baseType.StartsWith(prefix, StringComparison.Ordinal))
                return true;
        }
        return baseType.EndsWith("[]", StringComparison.Ordinal);
    }

    /// <summary>
    /// Return a C# default-value literal for a type name, suitable for initializing
    /// a concrete constructor parameter in a generated test class.
    /// </summary>
    public static string DefaultLiteral(string typeName)
    {
        var baseType = typeName.TrimEnd('?');
        var isNullable = typeName.EndsWith('?');

        // For nullable types other than string, null is the simplest valid value
        if (isNullable && baseType != "string")
            return "null";

        return baseType switch
        {
            "string" => "\"test-value\"",
            "int" => "0",
            "long" => "0L",
            "bool" => "false",
            "double" => "0.0",
            "float" => "0f",
            "decimal" => "0m",
            "byte" => "(byte)0",
            "short" => "(short)0",
            "ushort" => "(ushort)0",
            "uint" => "0u",
            "ulong" => "0UL",
            "char" => "'a'",
            "Guid" => "Guid.NewGuid()",
            "DateTime" => "DateTime.UtcNow",
            "DateTimeOffset" => "DateTimeOffset.UtcNow",
            "TimeSpan" => "TimeSpan.FromSeconds(1)",
            "Uri" => "new Uri(\"https://example.com\")",
            "CancellationToken" => "CancellationToken.None",
            "Stream" => "Stream.Null",
            "Type" => "typeof(object)",
            "object" => "new object()",
            _ when baseType.StartsWith("List<") => $"new {baseType}()",
            _ when baseType.StartsWith("IList<") => $"new {baseType.Replace("IList<", "List<")}()",
            _ when baseType.StartsWith("IEnumerable<") => $"Array.Empty<{ExtractGenericArg(baseType, "IEnumerable<")}>()",
            _ when baseType.StartsWith("IReadOnlyList<") => $"Array.Empty<{ExtractGenericArg(baseType, "IReadOnlyList<")}>()",
            _ when baseType.StartsWith("ICollection<") => $"new List<{ExtractGenericArg(baseType, "ICollection<")}>()",
            _ when baseType.StartsWith("Dictionary<") => $"new {baseType}()",
            _ when baseType.StartsWith("IDictionary<") => $"new {baseType.Replace("IDictionary<", "Dictionary<")}()",
            _ when baseType.StartsWith("Func<") => "null!",
            _ when baseType.StartsWith("Action<") || baseType == "Action" => "() => {{ }}",
            _ when baseType.EndsWith("[]") => $"Array.Empty<{baseType[..^2]}>()",
            _ when baseType.StartsWith("Nullable<") => "null",
            _ when baseType.EndsWith("Enum") || baseType.EndsWith("Type") => $"default({baseType})",
            _ => $"default({baseType})!" // nullable reference fallback
        };
    }

    /// <summary>
    /// Extract the generic type argument from a generic type name (e.g. "IEnumerable&lt;string&gt;" -&gt; "string").
    /// Returns "object" if the input does not match the expected shape.
    /// </summary>
    private static string ExtractGenericArg(string typeName, string prefix)
    {
        if (typeName.StartsWith(prefix, StringComparison.Ordinal) && typeName.EndsWith(">", StringComparison.Ordinal))
            return typeName[prefix.Length..^1];
        return "object";
    }
}
