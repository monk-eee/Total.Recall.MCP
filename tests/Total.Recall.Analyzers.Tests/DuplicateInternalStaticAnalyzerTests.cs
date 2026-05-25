using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Total.Recall.Analyzers;

namespace Total.Recall.Analyzers.Tests;

public class DuplicateInternalStaticAnalyzerTests
{
    private const string DuplicatedInToolsA = """
        namespace Total.Recall.Tools;
        internal static class A
        {
            internal static string Resolve(int x) => x.ToString();
        }
        """;

    private const string DuplicatedInScannersA = """
        namespace Total.Recall.Scanners;
        internal static class B
        {
            internal static string Resolve(int x) => (x + 1).ToString();
        }
        """;

    private const string DuplicatedInToolsB = """
        namespace Total.Recall.Tools;
        internal static class C
        {
            internal static string Resolve(int x) => x.ToString();
        }
        """;

    private const string UniqueHelper = """
        namespace Total.Recall.Tools;
        internal static class D
        {
            internal static int OnlyHere(int x) => x;
        }
        """;

    private const string PublicStatic = """
        namespace Total.Recall.Tools;
        public static class E
        {
            public static string Resolve(int x) => x.ToString();
        }
        """;

    private const string OutsideToolsOrScanners = """
        namespace Total.Recall.Infrastructure;
        internal static class F
        {
            internal static string Resolve(int x) => x.ToString();
        }
        """;

    [Fact]
    public async Task FiresWhenSameSignatureInToolsAndScanners()
    {
        var diagnostics = await RunAsync(
            ("Tools/A.cs", DuplicatedInToolsA),
            ("Scanners/B.cs", DuplicatedInScannersA));

        Assert.Equal(2, diagnostics.Length);
        Assert.All(diagnostics, d => Assert.Equal("TR0001", d.Id));
    }

    [Fact]
    public async Task FiresWhenSameSignatureInTwoToolsFiles()
    {
        var diagnostics = await RunAsync(
            ("Tools/A.cs", DuplicatedInToolsA),
            ("Tools/C.cs", DuplicatedInToolsB));

        Assert.Equal(2, diagnostics.Length);
    }

    [Fact]
    public async Task DoesNotFireForUniqueSignature()
    {
        var diagnostics = await RunAsync(
            ("Tools/A.cs", DuplicatedInToolsA),
            ("Tools/D.cs", UniqueHelper));

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task DoesNotFireForPublicStatic()
    {
        var diagnostics = await RunAsync(
            ("Tools/E.cs", PublicStatic),
            ("Scanners/E2.cs", PublicStatic.Replace("class E", "class E2")));

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task DoesNotFireOutsideToolsAndScanners()
    {
        var diagnostics = await RunAsync(
            ("Tools/A.cs", DuplicatedInToolsA),
            ("Infrastructure/F.cs", OutsideToolsOrScanners));

        // Only one occurrence is in a Tools/ folder; the other is in Infrastructure, so no duplicate boundary crossing.
        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task DoesNotFireForInFileOverloads()
    {
        const string overloads = """
            namespace Total.Recall.Tools;
            internal static class G
            {
                internal static string Resolve(int x) => x.ToString();
                internal static string Resolve(string s) => s;
            }
            """;

        var diagnostics = await RunAsync(("Tools/G.cs", overloads));

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task FiresForPrivateStaticDuplicateAcrossFolders()
    {
        const string privateInTools = """
            namespace Total.Recall.Tools;
            internal static class H
            {
                private static string SanitizeId(string name) => name.Replace('.', '_');
            }
            """;
        const string privateInScanners = """
            namespace Total.Recall.Scanners;
            internal static class I
            {
                private static string SanitizeId(string name) => name.Replace('.', '_');
            }
            """;

        var diagnostics = await RunAsync(
            ("Tools/H.cs", privateInTools),
            ("Scanners/I.cs", privateInScanners));

        Assert.Equal(2, diagnostics.Length);
        Assert.All(diagnostics, d => Assert.Equal("TR0001", d.Id));
    }

    [Fact]
    public async Task DoesNotFireForProtectedStatic()
    {
        // protected-static exposes a member through inheritance; that's
        // deliberate API surface, not a forked helper.
        const string protectedInToolsA = """
            namespace Total.Recall.Tools;
            internal class J
            {
                protected static string Resolve(int x) => x.ToString();
            }
            """;
        const string protectedInScannersA = """
            namespace Total.Recall.Scanners;
            internal class K
            {
                protected static string Resolve(int x) => x.ToString();
            }
            """;

        var diagnostics = await RunAsync(
            ("Tools/J.cs", protectedInToolsA),
            ("Scanners/K.cs", protectedInScannersA));

        Assert.Empty(diagnostics);
    }

    private static async Task<ImmutableArray<Diagnostic>> RunAsync(params (string path, string source)[] files)
    {
        var trees = files
            .Select(f => CSharpSyntaxTree.ParseText(f.source, path: f.path))
            .ToArray();

        var references = new[]
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(System.Console).Assembly.Location),
        };

        var compilation = CSharpCompilation.Create(
            "AnalyzerTest",
            trees,
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var analyzers = ImmutableArray.Create<DiagnosticAnalyzer>(new DuplicateInternalStaticAnalyzer());
        var withAnalyzers = compilation.WithAnalyzers(analyzers);
        var all = await withAnalyzers.GetAnalyzerDiagnosticsAsync();
        return all.Where(d => d.Id == "TR0001").ToImmutableArray();
    }
}
