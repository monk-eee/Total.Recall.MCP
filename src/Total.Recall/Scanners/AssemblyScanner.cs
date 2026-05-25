using System.Reflection;
using Total.Recall.Infrastructure;
using Total.Recall.Models;

namespace Total.Recall.Scanners;

/// <summary>
/// Scans a .NET assembly via MetadataLoadContext (reflection-only, no dependency loading)
/// and writes type metadata to type-registry.jsonl.
/// </summary>
public static class AssemblyScanner
{
    /// <summary>
    /// Scan the target assembly and write type-registry.jsonl.
    /// Returns the number of types scanned.
    /// </summary>
    public static int Scan(string assemblyPath, string dataDir)
    {
        if (!File.Exists(assemblyPath))
            throw new FileNotFoundException($"Assembly not found: {assemblyPath}");

        var resolver = CreateResolver(assemblyPath);
        using var mlc = new MetadataLoadContext(resolver);

        var assembly = mlc.LoadFromAssemblyPath(Path.GetFullPath(assemblyPath));
        var types = assembly.GetTypes()
            .Where(t => !IsCompilerGenerated(t))
            .Where(t => !string.IsNullOrEmpty(t.Namespace))
            .ToList();

        var records = new List<TypeRecord>();

        foreach (var type in types)
        {
            try
            {
                records.Add(ExtractTypeRecord(type));
            }
            catch
            {
                // Skip types that fail to reflect (missing dependencies, etc.)
            }
        }

        var store = new JsonLineStore<TypeRecord>(RepoConfig.TypeRegistryPath(dataDir));
        store.WriteAll(records);

        return records.Count;
    }

    private static TypeRecord ExtractTypeRecord(Type type)
    {
        var record = new TypeRecord
        {
            Name = type.Name,
            Namespace = type.Namespace ?? "",
            FullUsing = string.IsNullOrEmpty(type.Namespace) ? "" : $"using {type.Namespace};",
            IsAbstract = type.IsAbstract && !type.IsSealed, // static classes are abstract+sealed
            IsStatic = type.IsAbstract && type.IsSealed,
            IsInternal = !type.IsPublic && !type.IsNestedPublic,
            IsInterface = type.IsInterface,
            IsEnum = type.IsEnum,
            BaseType = type.BaseType?.Name
        };

        // Constructors
        try
        {
            foreach (var ctor in type.GetConstructors(BindingFlags.Public | BindingFlags.Instance))
            {
                var ctorRecord = new ConstructorRecord
                {
                    Params = ctor.GetParameters()
                        .Select(p => $"{FormatTypeName(p.ParameterType)} {p.Name}")
                        .ToList()
                };
                record.Constructors.Add(ctorRecord);
            }
        }
        catch { /* Some types fail constructor reflection in MetadataLoadContext */ }

        // Properties
        try
        {
            foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                record.Properties.Add(new PropertyRecord
                {
                    Name = prop.Name,
                    ClrType = FormatTypeName(prop.PropertyType),
                    HasSet = prop.SetMethod is not null,
                    HasInit = false // MetadataLoadContext can't reliably detect init-only
                });
            }
        }
        catch { /* Skip property reflection failures */ }

        // Interfaces
        try
        {
            record.Interfaces = type.GetInterfaces()
                .Select(i => i.Name)
                .ToList();
        }
        catch { /* Skip interface reflection failures */ }

        // Enum values
        if (type.IsEnum)
        {
            try
            {
                record.EnumValues = type.GetFields(BindingFlags.Public | BindingFlags.Static)
                    .Select(f => f.Name)
                    .ToList();
            }
            catch { /* Skip enum reflection failures */ }
        }

        return record;
    }

    private static PathAssemblyResolver CreateResolver(string assemblyPath)
    {
        return new PathAssemblyResolver(BuildResolverPaths(assemblyPath));
    }

    /// <summary>
    /// Build the deduplicated path list fed to <see cref="PathAssemblyResolver"/>.
    /// Internal so tests can verify identity-deduplication directly.
    ///
    /// Deduplicate by assembly simple-name (NOT by file path). PathAssemblyResolver
    /// throws FileLoadException("Assembly with same name is already loaded") when
    /// two distinct paths resolve to the same assembly identity during core-assembly
    /// probing — which happens routinely when the target dir is a publish output
    /// containing its own copy of mscorlib.dll / System.Private.CoreLib.dll /
    /// netstandard.dll alongside the host runtime dir. Resolution order: target dir
    /// wins (its versions are the ones we want to read), runtime dir fills the rest.
    /// </summary>
    internal static IReadOnlyCollection<string> BuildResolverPaths(string assemblyPath)
    {
        var bySimpleName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // 1. All DLLs in the target assembly's directory (preferred)
        var targetDir = Path.GetDirectoryName(Path.GetFullPath(assemblyPath));
        if (targetDir is not null)
            AddDirectory(bySimpleName, targetDir, overwriteExisting: true);

        // 2. Runtime assemblies (only those not already provided by target dir)
        var runtimeDir = Path.GetDirectoryName(typeof(object).Assembly.Location);
        if (runtimeDir is not null)
            AddDirectory(bySimpleName, runtimeDir, overwriteExisting: false);

        return bySimpleName.Values;
    }

    private static void AddDirectory(Dictionary<string, string> bySimpleName, string dir, bool overwriteExisting)
    {
        // NOTE on perf: we call AssemblyName.GetAssemblyName(dll) for every dll
        // in both the target dir and the runtime dir (~250 dlls in a typical
        // .NET 8 shared framework). Each call parses the PE header — sub-ms,
        // total ~100–300 ms one-time at scanner startup. A filename-only key
        // would be cheaper but reintroduces the very bug this method exists to
        // fix: two DLLs in different dirs can share a filename without sharing
        // identity (ref-assembly shims, side-by-side framework copies). Identity
        // is the only correct dedup key here. Don't "optimise" to filename.
        foreach (var dll in Directory.EnumerateFiles(dir, "*.dll"))
        {
            string? simpleName;
            try
            {
                simpleName = AssemblyName.GetAssemblyName(dll).Name;
            }
            catch
            {
                // Native DLLs, corrupt files, or non-assembly .dlls — skip silently.
                continue;
            }

            if (string.IsNullOrEmpty(simpleName))
                continue;

            if (overwriteExisting || !bySimpleName.ContainsKey(simpleName))
                bySimpleName[simpleName] = dll;
        }
    }

    private static bool IsCompilerGenerated(Type type)
    {
        // Filter out compiler-generated types: <>c, <Module>, DisplayClass, etc.
        return type.Name.StartsWith('<')
            || type.Name.Contains("__")
            || type.Name.Contains("DisplayClass")
            || type.Name == "<Module>";
    }

    private static string FormatTypeName(Type type)
    {
        if (type.IsGenericType)
        {
            var baseName = type.Name.Contains('`')
                ? type.Name[..type.Name.IndexOf('`')]
                : type.Name;

            var args = type.GetGenericArguments()
                .Select(FormatTypeName);

            return $"{baseName}<{string.Join(", ", args)}>";
        }

        // Map common CLR names to C# aliases
        return type.Name switch
        {
            "String" => "string",
            "Int32" => "int",
            "Int64" => "long",
            "Boolean" => "bool",
            "Double" => "double",
            "Single" => "float",
            "Decimal" => "decimal",
            "Void" => "void",
            "Object" => "object",
            "Byte" => "byte",
            _ => type.Name
        };
    }
}
