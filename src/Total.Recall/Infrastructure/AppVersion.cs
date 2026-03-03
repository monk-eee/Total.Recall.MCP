using System.Reflection;

namespace Total.Recall.Infrastructure;

/// <summary>
/// Provides the assembly version as a single source of truth.
/// Reads from <see cref="AssemblyInformationalVersionAttribute"/> (set by csproj Version property).
/// </summary>
internal static class AppVersion
{
    /// <summary>
    /// Semantic version string (e.g. "2.1.0").
    /// </summary>
    public static string Current { get; } = ReadVersion();

    private static string ReadVersion()
    {
        var attr = typeof(AppVersion).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>();

        if (attr?.InformationalVersion is { Length: > 0 } raw)
        {
            // Strip the "+commitHash" suffix that SDK builds may append
            var plus = raw.IndexOf('+');
            return plus > 0 ? raw[..plus] : raw;
        }

        // Fallback to assembly version (always present)
        var asm = typeof(AppVersion).Assembly.GetName().Version;
        return asm is not null ? $"{asm.Major}.{asm.Minor}.{asm.Build}" : "0.0.0";
    }
}
