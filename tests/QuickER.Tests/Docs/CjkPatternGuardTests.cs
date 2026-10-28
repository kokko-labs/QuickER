using System.IO;
using AwesomeAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using QuickER.Tests.Resources;
using QuickER.Tests.TestSupport;

namespace QuickER.Tests.Docs;

/// <summary>
/// 英語ガードのテストが使う CJK の文字の範囲（<see cref="CjkText.Pattern"/>）を、テストのソースへ写さないことを固定する
/// </summary>
/// <remarks>
/// <para>
/// 拾う基準：<c>tests/</c> の <c>.cs</c>（<c>bin</c> / <c>obj</c> と <c>TestSupport/CjkText.cs</c> を除く）を構文木で走査し、
/// 文字列リテラルの値（エスケープを解いた後の値）に、範囲の始まり「U+3000〜U+9FFF」か「U+FF00〜U+FFEF」の表記を含むものを
/// 数える。値で見るので、実文字で書いた写しも <c>\u3000</c> の形で書いた写しも拾う。一覧は空で、1 つでも赤にする。
/// </para>
/// <para>
/// 目印の文字列はこのファイル自身にも書かない（数値から作る）。
/// </para>
/// </remarks>
public sealed class CjkPatternGuardTests
{
    /// <summary>写しの目印（範囲の両端を「-」でつないだもの）</summary>
    private static readonly string[] Markers =
    [
        new string([(char)0x3000, '-', (char)0x9FFF]),
        new string([(char)0xFF00, '-', (char)0xFFEF]),
    ];

    [Fact(
        DisplayName = "CJK の文字の範囲は TestSupport の CjkText だけが持ち、テストのソースに写しが無い"
    )]
    public void CjkRange_IsDefinedOnlyInTestSupport()
    {
        var root = NeutralResxFiles.FindRepositoryRoot();
        var tests = Path.Combine(root, "tests");
        var shared = Path.Combine(tests, "QuickER.Tests", "TestSupport", "CjkText.cs");
        var found = new List<string>();
        var sharedHasMarker = false;

        foreach (var file in SourceFiles.Enumerate("tests"))
        {
            var path = file.FullPath;
            var relative = file.RelativePath;
            var literals = ConditionalSourceParse
                .DescendantTokens(
                    ConditionalSourceParse.Parse(
                        File.ReadAllText(path),
                        cancellationToken: TestContext.Current.CancellationToken
                    ),
                    TestContext.Current.CancellationToken
                )
                .Where(token =>
                    token.IsKind(SyntaxKind.StringLiteralToken)
                    && Markers.Any(marker =>
                        token.ValueText.Contains(marker, StringComparison.Ordinal)
                    )
                );

            foreach (var literal in literals)
            {
                if (string.Equals(path, shared, StringComparison.OrdinalIgnoreCase))
                {
                    sharedHasMarker = true;
                    continue;
                }

                found.Add(
                    $"{relative}:{literal.GetLocation().GetLineSpan().StartLinePosition.Line + 1}"
                );
            }
        }

        sharedHasMarker
            .Should()
            .BeTrue("共有の CjkText が範囲を持っていること（走査が空振りしていないこと）");
        found.Should().BeEmpty("CJK の文字の範囲は TestSupport.CjkText.Pattern を使う");
    }
}
