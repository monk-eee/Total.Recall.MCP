namespace Total.Recall.Infrastructure;

/// <summary>
/// Shared helpers for parsing constructor parameter strings ("TypeName paramName")
/// and detecting interface types. Used by TestScaffoldTool, TestableTargetsTool,
/// and ContextTool to avoid duplicating param-parsing and interface-detection logic.
/// </summary>
public static class ParamHelper
{
    /// <summary>
    /// Parse a constructor parameter string like "ILogger _logger" into (Type, Name).
    /// Strips leading underscores from names. Returns ("param") as name if no space found.
    /// </summary>
    public static (string Type, string Name) ParseParam(string param)
    {
        var trimmed = param.Trim();
        var spaceIdx = trimmed.LastIndexOf(' ');
        if (spaceIdx <= 0)
            return (trimmed, "param");

        var type = trimmed[..spaceIdx].Trim();
        var name = trimmed[(spaceIdx + 1)..].Trim().TrimStart('_');

        if (name.Length == 0)
            name = "param";

        return (type, name);
    }

    /// <summary>
    /// Extract just the type name from a parameter string like "ILogger _logger" → "ILogger".
    /// Equivalent to <see cref="ParseParam"/> but returns only the type portion.
    /// </summary>
    public static string ExtractTypeName(string param)
    {
        var trimmed = param.Trim();
        var spaceIdx = trimmed.IndexOf(' ');
        return spaceIdx > 0 ? trimmed[..spaceIdx] : trimmed;
    }

    /// <summary>
    /// Heuristic: does this type name look like a .NET interface?
    /// Matches names starting with 'I' followed by an uppercase letter (e.g. ILogger, IDisposable).
    /// </summary>
    public static bool IsInterfaceLike(string typeName)
    {
        return typeName.Length >= 2
            && typeName[0] == 'I'
            && char.IsUpper(typeName[1]);
    }

    /// <summary>
    /// Strip the 'I' prefix from an interface name for use in field/variable names.
    /// "ILogger" → "Logger", "IContentBase" → "ContentBase".
    /// Returns the original name if it doesn't match the interface naming pattern.
    /// </summary>
    public static string StripIPrefix(string interfaceName)
    {
        if (interfaceName.Length >= 2 && interfaceName[0] == 'I' && char.IsUpper(interfaceName[1]))
            return interfaceName[1..];
        return interfaceName;
    }

    /// <summary>
    /// Count how many constructor parameters look like interfaces in a parameter list.
    /// </summary>
    public static int CountInterfaceParams(IEnumerable<string> ctorParams)
    {
        return ctorParams.Count(p => IsInterfaceLike(ExtractTypeName(p)));
    }

    /// <summary>
    /// Heuristic: does this type name smell like an external service dependency?
    /// Matches names containing keywords associated with file system, HTTP, database, or stream access.
    /// These dependencies are structural blockers — even with mocking, tests become brittle.
    /// Examples: FileSystem, HttpClient, IFileProvider, SqlConnection, Stream, DbContext.
    /// </summary>
    public static bool IsExternalDependency(string typeName)
    {
        // Check against common external-service-smelling substrings (case-insensitive)
        return typeName.Contains("File", StringComparison.OrdinalIgnoreCase)
            || typeName.Contains("Http", StringComparison.OrdinalIgnoreCase)
            || typeName.Contains("Stream", StringComparison.OrdinalIgnoreCase)
            || typeName.Contains("Socket", StringComparison.OrdinalIgnoreCase)
            || typeName.Contains("Connection", StringComparison.OrdinalIgnoreCase)
            || typeName.Contains("DbContext", StringComparison.OrdinalIgnoreCase)
            || typeName.Contains("Database", StringComparison.OrdinalIgnoreCase)
            || typeName.Contains("Process", StringComparison.OrdinalIgnoreCase)
            || typeName.Contains("Registry", StringComparison.OrdinalIgnoreCase)
            || typeName.Contains("Environment", StringComparison.OrdinalIgnoreCase);
    }
}
