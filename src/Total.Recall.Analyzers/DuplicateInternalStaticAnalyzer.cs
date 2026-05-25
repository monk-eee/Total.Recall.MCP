using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Total.Recall.Analyzers;

/// <summary>
/// TR0001 — flags duplicate <c>internal static</c> method signatures defined
/// inside files under <c>Tools/</c> and/or <c>Scanners/</c>.
/// </summary>
/// <remarks>
/// AGENTS.md "Mechanical enforcement" lists this as a desired guardrail:
/// helper duplication across <c>Tools/</c> and <c>Scanners/</c> is the exact
/// failure mode this analyzer prevents. The fix is always extraction to
/// <c>Infrastructure/</c> — never silence the diagnostic with an allowlist.
///
/// The analyzer keys on a folder-name suffix (<c>/Tools/</c> or <c>/Scanners/</c>
/// anywhere on the source path) so it works regardless of where the repo is
/// cloned. Same file is fine (in-file overloads are not duplicates across the
/// canonical-home boundary the rule is guarding).
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DuplicateInternalStaticAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "TR0001";

    private static readonly DiagnosticDescriptor s_rule = new(
        id: DiagnosticId,
        title: "Duplicate internal static method across Tools/Scanners",
        messageFormat: "Internal static '{0}' is also defined in '{1}'. Extract a single implementation to Infrastructure/ and call it from both.",
        category: "Total.Recall.CodeReuse",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "AGENTS.md forbids forking helpers across Tools/ and Scanners/. Duplicate signatures must be extracted to Infrastructure/.",
        customTags: new[] { WellKnownDiagnosticTags.CompilationEnd });

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(s_rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(OnCompilationStart);
    }

    private static void OnCompilationStart(CompilationStartAnalysisContext startContext)
    {
        // signature -> list of (location, filePath)
        var occurrences = new ConcurrentDictionary<string, ConcurrentBag<(Location loc, string path)>>();

        startContext.RegisterSyntaxNodeAction(ctx =>
        {
            var method = (MethodDeclarationSyntax)ctx.Node;

            if (!IsInternalStatic(method))
            {
                return;
            }

            var path = ctx.Node.SyntaxTree.FilePath ?? string.Empty;
            if (!IsInToolsOrScanners(path))
            {
                return;
            }

            var signature = BuildSignature(method);
            var bag = occurrences.GetOrAdd(signature, _ => new ConcurrentBag<(Location, string)>());
            bag.Add((method.Identifier.GetLocation(), path));
        }, SyntaxKind.MethodDeclaration);

        startContext.RegisterCompilationEndAction(endContext =>
        {
            foreach (var kvp in occurrences)
            {
                var hits = kvp.Value.ToArray();
                // Same file is fine — in-file overloads aren't a duplication-across-boundary smell.
                // We only fire when the same signature appears in two or more distinct files.
                var distinctFiles = hits.Select(h => h.path).Distinct(System.StringComparer.OrdinalIgnoreCase).ToArray();
                if (distinctFiles.Length < 2)
                {
                    continue;
                }

                foreach (var hit in hits)
                {
                    var others = distinctFiles
                        .Where(f => !string.Equals(f, hit.path, System.StringComparison.OrdinalIgnoreCase))
                        .Select(System.IO.Path.GetFileName);
                    var otherList = string.Join(", ", others);
                    endContext.ReportDiagnostic(Diagnostic.Create(s_rule, hit.loc, kvp.Key, otherList));
                }
            }
        });
    }

    private static bool IsInternalStatic(MethodDeclarationSyntax method)
    {
        var hasInternal = false;
        var hasStatic = false;
        var hasPublic = false;
        var hasPrivate = false;
        var hasProtected = false;

        foreach (var modifier in method.Modifiers)
        {
            switch (modifier.Kind())
            {
                case SyntaxKind.InternalKeyword: hasInternal = true; break;
                case SyntaxKind.StaticKeyword: hasStatic = true; break;
                case SyntaxKind.PublicKeyword: hasPublic = true; break;
                case SyntaxKind.PrivateKeyword: hasPrivate = true; break;
                case SyntaxKind.ProtectedKeyword: hasProtected = true; break;
            }
        }

        // Must be exactly internal (not internal-protected, not public, not private).
        return hasInternal && hasStatic && !hasPublic && !hasPrivate && !hasProtected;
    }

    private static bool IsInToolsOrScanners(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return false;
        }

        var segments = path.Replace('\\', '/').Split('/');
        foreach (var segment in segments)
        {
            if (string.Equals(segment, "Tools", System.StringComparison.OrdinalIgnoreCase)
                || string.Equals(segment, "Scanners", System.StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    private static string BuildSignature(MethodDeclarationSyntax method)
    {
        var name = method.Identifier.ValueText;
        var paramTypes = method.ParameterList.Parameters
            .Select(p => p.Type?.ToString() ?? "?")
            .ToArray();
        return $"{name}({string.Join(",", paramTypes)})";
    }
}
