using System.IO;
using AwesomeAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using QuickER.Tests.Resources;

namespace QuickER.Tests.TestSupport;

/// <summary>
/// テストが src・tests のソースファイルをパースするときは <see cref="ConditionalSourceParse"/> を通し、
/// Roslyn のパース（<c>CSharpSyntaxTree.ParseText</c> / <c>SyntaxFactory.ParseSyntaxTree</c>）を直接呼ぶのは理由つきの一覧にあるものだけであることを固定する
/// </summary>
/// <remarks>
/// <para>
/// ソースを走査するガードが直接パースすると、1 通りのプリプロセッサの記号で外れる分岐（<c>#if !DEBUG</c> の中・<c>#if QUICKER_*</c> の
/// <c>#else</c>）がトリビアになり、そこに書いた禁止の呼び出しを見落とす。新しいガードが直接パースへ戻らないよう、tests/ の全 <c>.cs</c>
/// （<c>bin</c> / <c>obj</c> を除く）にある直接のパースの呼び出しを数え、理由つきの <see cref="Known"/> と厳密に照合する
/// （一覧に無い出現も、一覧にあるのに無い出現＝件数の違いも赤）。
/// </para>
/// <para>
/// 一覧に載せてよいのは、ディスクのソースを走査するのでなく、生成器・レンダラーの出力やテストが組み立てた文字列を
/// 1 つの構成でコンパイル・読み取りするもの（生成物は条件コンパイルを出さない）と、入口そのもの・自己検査の対照・プローブだけ。
/// 拾う形は、メソッド名が <c>ParseText</c> / <c>ParseSyntaxTree</c> の呼び出し（受け手の書き方・完全修飾・<c>using static</c> を問わない）。
/// </para>
/// </remarks>
public sealed class SourceParseEntryGuardTests
{
    /// <summary>生成器・レンダラーの出力（メモリ上の文字列）</summary>
    private const string GeneratedOutput =
        "生成器・レンダラーの出力（メモリ上の文字列）を 1 つの構成でコンパイル・読み取りする（生成物は条件コンパイルを出さない）";

    /// <summary>生成物をコンパイルする共有の部品</summary>
    private const string GeneratedCompiler =
        "生成物（ディスクのフィクスチャ・ランタイムのパッケージ用ソースを含む）とテストの追加ソースを 1 つの構成でコンパイルする共有の部品"
        + "（生成物は条件コンパイルを出さない。走査ではなくコンパイル。生成物が #if を持つようになったら ConditionalSourceParse へ移す）";

    /// <summary>テストが組み立てたソース</summary>
    private const string ComposedSource =
        "テストが組み立てたソースの文字列をコンパイルする（ディスクのソースではない）";

    /// <summary>既知の出現（ファイル・件数・理由）</summary>
    private static readonly (string File, int Count, string Reason)[] Known =
    [
        ("tests/QuickER.Tests/CodeGen.CSharp/GeneratedCodeCompiler.cs", 1, GeneratedCompiler),
        (
            "tests/QuickER.Tests/CodeGen.CSharp/GeneratedFixedMemberDriftTests.cs",
            1,
            GeneratedOutput
        ),
        ("tests/QuickER.Tests/CodeGen.CSharp/QueryCodegenDiagnosticsTests.cs", 1, ComposedSource),
        ("tests/QuickER.Tests/CodeGen.CSharp/QueryGenerationTests.cs", 1, GeneratedOutput),
        ("tests/QuickER.Tests/CodeGen.CSharp/QueryLiteralEscapeScanTests.cs", 1, GeneratedOutput),
        (
            "tests/QuickER.Tests/TestSupport/ConditionalSourceParse.cs",
            2,
            "src・tests のソースをパースする唯一の入口（基準の構文木と、記号の組み合わせごとの構文木）"
        ),
        (
            "tests/QuickER.Tests/TestSupport/ConditionalSourceParseTests.cs",
            2,
            "自己検査の対照（1 通りのパースが分岐の中を見落とすことを示す）"
        ),
        (
            "tests/QuickER.Tests/TestSupport/SourceSymbolScan.cs",
            1,
            "自己検査のプローブの文字列をコンパイルする（CompileProbe）"
        ),
    ];

    [Fact(
        DisplayName = "Roslyn のパースを直接呼ぶのは理由つきの一覧にあるものだけ（ソースの走査は ConditionalSourceParse を通す）"
    )]
    public void DirectParses_AreOnlyKnown()
    {
        var root = NeutralResxFiles.FindRepositoryRoot();
        var found = SourceFiles
            .Enumerate("tests")
            .Select(file =>
                (
                    File: file.RelativePath,
                    Count: ConditionalSourceParse
                        .DescendantNodes(
                            ConditionalSourceParse.Parse(
                                File.ReadAllText(file.FullPath),
                                cancellationToken: TestContext.Current.CancellationToken
                            ),
                            TestContext.Current.CancellationToken
                        )
                        .Count(IsDirectParse)
                )
            )
            .Where(hit => hit.Count > 0)
            .ToDictionary(hit => hit.File, hit => hit.Count);

        GuardKnownList.AssertMatches(
            found,
            Known.Select(known => (known.File, known.Count)),
            (file, count) => $"{file} x{count}",
            "ソースファイルの走査は ConditionalSourceParse を通し（#if のどの分岐も読む）、例外は理由つきで一覧に載せる"
        );
    }

    /// <summary>ガードが拾うべき書き方と、拾わない書き方</summary>
    private const string ProbeSource = """
        using Microsoft.CodeAnalysis.CSharp;
        using QuickER.Tests.TestSupport;
        using static Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree;
        using T = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree;

        internal static class Probe
        {
            public static void Hits(string text)
            {
                CSharpSyntaxTree.ParseText(text);
                Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(text);
                SyntaxFactory.ParseSyntaxTree(text);
                ParseText(text);
                T.ParseText(text);
            }

            public static void Misses(string text)
            {
                ConditionalSourceParse.Parse(text);
                Microsoft.CodeAnalysis.Text.SourceText.From(text);
                _ = "CSharpSyntaxTree.ParseText(text)";
            }
        }
        """;

    [Fact(
        DisplayName = "走査は完全修飾・using static・別名の呼び出しも拾い、入口・文字列は拾わない"
    )]
    public void IsDirectParse_FindsEveryForm()
    {
        var root = ConditionalSourceParse
            .Parse(ProbeSource, cancellationToken: TestContext.Current.CancellationToken)
            .Single()
            .GetRoot(TestContext.Current.CancellationToken);

        int Hits(string method) =>
            root.DescendantNodes()
                .OfType<MethodDeclarationSyntax>()
                .Single(declaration => declaration.Identifier.ValueText == method)
                .DescendantNodes()
                .Count(IsDirectParse);

        Hits("Hits").Should().Be(5);
        Hits("Misses").Should().Be(0);
    }

    /// <summary>節が Roslyn のパースの直接の呼び出しか（メソッド名で判定する）</summary>
    private static bool IsDirectParse(SyntaxNode node) =>
        node is InvocationExpressionSyntax invocation
        && (
            invocation.Expression switch
            {
                MemberAccessExpressionSyntax member => member.Name.Identifier.ValueText,
                IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
                _ => null,
            }
        )
            is "ParseText"
                or "ParseSyntaxTree";
}
