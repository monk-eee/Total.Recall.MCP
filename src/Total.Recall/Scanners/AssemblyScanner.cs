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
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 1. All DLLs in the target assembly's directory
        var targetDir = Path.GetDirectoryName(Path.GetFullPath(assemblyPath));
        if (targetDir is not null)
        {
            foreach (var dll in Directory.GetFiles(targetDir, "*.dll"))
                paths.Add(dll);
        }

        // 2. Runtime assemblies (for framework types like System.Object, System.String, etc.)
        var runtimeDir = Path.GetDirectoryName(typeof(object).Assembly.Location);
        if (runtimeDir is not null)
        {
            foreach (var dll in Directory.GetFiles(runtimeDir, "*.dll"))
                paths.Add(dll);
        }

        return new PathAssemblyResolver(paths);
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
