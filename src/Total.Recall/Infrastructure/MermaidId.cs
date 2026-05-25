namespace Total.Recall.Infrastructure;

/// <summary>
/// Helpers for emitting Mermaid diagram identifiers. Mermaid node IDs
/// cannot contain generics brackets, commas, spaces, or dots — these
/// characters get replaced with underscores so type names like
/// <c>System.Collections.Generic.List&lt;string&gt;</c> become valid IDs.
/// </summary>
internal static class MermaidId
{
    internal static string Sanitize(string name)
    {
        return name
            .Replace('<', '_')
            .Replace('>', '_')
            .Replace(',', '_')
            .Replace(' ', '_')
            .Replace('.', '_');
    }
}
