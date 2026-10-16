using AwesomeAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using QuickER.Platform;
using QuickER.Tests.TestSupport;

namespace QuickER.Tests.Platform;

/// <summary>
/// パスがフォルダの中にあるかを、QuickER.Platform の外で文字列の前方一致として書いていないことを固定する
/// （判定は <see cref="PathContainment"/> を通す）
/// </summary>
/// <remarks>
/// <para>
/// 拾う形：<see cref="string.StartsWith(string)"/> の多重定義の呼び出しで、第 1 引数（それが局所変数なら、その初期化子をたどって
/// 3 段まで）が <c>System.IO.Path</c> の区切りの扱い（<c>DirectorySeparatorChar</c>・<c>AltDirectorySeparatorChar</c>・
/// <c>EndsInDirectorySeparator</c>・<c>TrimEndingDirectorySeparator</c>）か <c>GetFullPath</c> を使うもの。シンボルで判定するので、
/// <c>using static</c>・別名・文字列補間も同じに拾う。
/// </para>
/// <para>
/// 拾わない形：<c>/</c> 区切りの相対パスどうしの前方一致（フルパスの包含ではない）・文字の多重定義・フィールドや引数をそのまま
/// 渡すもの（初期化子をたどれない）。受け手（<c>full.StartsWith</c> の <c>full</c>）は見ない（UNC の判定のような包含でない前方一致を拾うため）。
/// メソッドを解決できない呼び出しは数えないのでなく、「未解決」の別の欄として照合する。
/// </para>
/// </remarks>
public sealed class PathPrefixGuardTests
{
    [Fact(DisplayName = "パスの包含を文字列の前方一致で書くのは、QuickER.Platform の中だけ")]
    public void PathPrefixComparisons_OutsidePlatform_AreNone()
    {
        var found = SourceSymbolScan
            .SrcProjects.Where(project => project.Name != "QuickER.Platform")
            .SelectMany(project =>
                project.Files.SelectMany(file =>
                {
                    var model = project.Compilation.GetSemanticModel(file.Tree);

                    return file
                        .Tree.GetRoot(TestContext.Current.CancellationToken)
                        .DescendantNodes()
                        .Select(node => PathPrefixCalls.Describe(model, node))
                        .OfType<string>()
                        .Select(call => $"{file.RelativePath}: {call}");
                })
            )
            .ToList();

        string.Join(Environment.NewLine, found)
            .Should()
            .BeEmpty("フォルダの中にあるかは PathContainment で判定する");
    }

    [Fact(
        DisplayName = "QuickER.Platform の PathContainment 自身の前方一致は拾える（走査が空振りしていない）"
    )]
    public void PathContainment_ItselfIsFound()
    {
        var platform = SourceSymbolScan.SrcProjects.Single(project =>
            project.Name == "QuickER.Platform"
        );
        var file = platform.Files.Single(file =>
            file.RelativePath.EndsWith("/PathContainment.cs", StringComparison.Ordinal)
        );
        var model = platform.Compilation.GetSemanticModel(file.Tree);

        file.Tree.GetRoot(TestContext.Current.CancellationToken)
            .DescendantNodes()
            .Select(node => PathPrefixCalls.Describe(model, node))
            .OfType<string>()
            .Should()
            .Equal("string.StartsWith");
    }

    /// <summary>ガードが拾うべき書き方と、拾わない書き方</summary>
    private const string ProbeSource = """
        using System;
        using System.IO;
        using static System.IO.Path;
        using P = System.IO.Path;

        internal static class Probe
        {
            public static void Hits(string path, string root)
            {
                path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
                var prefix = Path.EndsInDirectorySeparator(root) ? root : root + Path.DirectorySeparatorChar;
                path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
                var full = Path.GetFullPath(root);
                var withSeparator = full + "\\";
                path.StartsWith(withSeparator);
                path.StartsWith($"{root}{DirectorySeparatorChar}");
                path.StartsWith(root + P.AltDirectorySeparatorChar);
            }

            public static void Misses(string path, string root, string relative)
            {
                relative.StartsWith(root + "/", StringComparison.OrdinalIgnoreCase);
                path.StartsWith('x');
                path.StartsWith("interval ", StringComparison.Ordinal);
                path.StartsWith(root);
                Path.GetFullPath(path).StartsWith(@"\\", StringComparison.Ordinal);
            }
        }
        """;

    [Fact(
        DisplayName = "走査はシンボルで判定し、using static・別名・補間・局所変数の初期化子の形も拾う"
    )]
    public void Describe_FindsEveryPrefixForm()
    {
        var (compilation, tree) = SourceSymbolScan.CompileProbe(ProbeSource);
        var model = compilation.GetSemanticModel(tree);
        var root = tree.GetRoot(TestContext.Current.CancellationToken);

        compilation
            .GetDiagnostics(TestContext.Current.CancellationToken)
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .Should()
            .BeEmpty();

        string[] Hits(string method) =>
            root.DescendantNodes()
                .OfType<MethodDeclarationSyntax>()
                .Single(declaration => declaration.Identifier.ValueText == method)
                .DescendantNodes()
                .Select(node => PathPrefixCalls.Describe(model, node))
                .OfType<string>()
                .ToArray();

        Hits("Hits").Should().HaveCount(5).And.AllBe("string.StartsWith");
        Hits("Misses").Should().BeEmpty();
    }
}

/// <summary>パスの包含を文字列の前方一致で書く呼び出しの判定（<see cref="PathPrefixGuardTests"/> の走査）</summary>
internal static class PathPrefixCalls
{
    private const int InitializerDepth = 3;

    private static readonly HashSet<string> PathMembers = new(StringComparer.Ordinal)
    {
        "DirectorySeparatorChar",
        "AltDirectorySeparatorChar",
        "EndsInDirectorySeparator",
        "TrimEndingDirectorySeparator",
        "GetFullPath",
    };

    /// <summary>節が拾う形の呼び出しならその名前（<c>string.StartsWith</c> か、解決できなければ <c>未解決 StartsWith</c>）</summary>
    public static string? Describe(SemanticModel model, SyntaxNode node)
    {
        if (
            node
            is not InvocationExpressionSyntax
            {
                Expression: MemberAccessExpressionSyntax
                {
                    Name.Identifier.ValueText: nameof(string.StartsWith)
                },
                ArgumentList.Arguments: [var first, ..],
            } invocation
        )
        {
            return null;
        }

        var method = SourceSymbolScan.ResolveMethod(model, invocation);

        if (
            method is not null
            && (
                method.ContainingType?.SpecialType != SpecialType.System_String
                || method.Parameters[0].Type.SpecialType != SpecialType.System_String
            )
        )
        {
            return null;
        }

        if (!MentionsPathMember(model, first.Expression, InitializerDepth))
        {
            return null;
        }

        return method is null ? "未解決 StartsWith" : "string.StartsWith";
    }

    private static bool MentionsPathMember(SemanticModel model, SyntaxNode expression, int depth)
    {
        foreach (
            var identifier in expression.DescendantNodesAndSelf().OfType<IdentifierNameSyntax>()
        )
        {
            var symbol = model.GetSymbolInfo(identifier).Symbol;

            if (
                symbol?.ContainingType?.ToDisplayString() == "System.IO.Path"
                && PathMembers.Contains(symbol.Name)
            )
            {
                return true;
            }

            if (depth > 0 && symbol is ILocalSymbol local)
            {
                foreach (var reference in local.DeclaringSyntaxReferences)
                {
                    if (
                        reference.GetSyntax()
                            is VariableDeclaratorSyntax { Initializer.Value: { } value }
                        && MentionsPathMember(model, value, depth - 1)
                    )
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }
}
