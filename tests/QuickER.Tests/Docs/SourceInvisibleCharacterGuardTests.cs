using System.IO;
using AwesomeAssertions;
using QuickER.Tests.Resources;

namespace QuickER.Tests.Docs;

/// <summary>
/// src と tests の C# ソースに、タブと、改行に化ける見えない文字（FF・NEL・U+2028・U+2029）の実文字が
/// 無いことを全数検査する。
/// </summary>
/// <remarks>
/// <para>
/// ツール越しにソースを書くと、エスケープ表記（<c>\t</c>・<c>\u2028</c> など）が実文字に化けることがある。
/// 文字列リテラルの中に入るとコンパイルも通り、値だけが黙って変わる（実際、出力先パスのつもりの
/// <c>C:\temp</c> が「<c>C:</c>＋タブ＋<c>emp</c>」になっていた）。U+2028 などは表示上は改行に見え、
/// 生成テンプレートの出力やレンダラーの改行の正規化で別の改行へ書き換わる。
/// </para>
/// <para>
/// 目印の文字はこのファイル自身にも書かない（数値から作る）。インデントのタブは整形（CSharpier）が空白へ
/// 直すので、違反になるのは整形の届かない文字列リテラル・コメントの中だけになる。
/// </para>
/// </remarks>
public sealed class SourceInvisibleCharacterGuardTests
{
    /// <summary>リポジトリのルート</summary>
    private static readonly string Root = NeutralResxFiles.FindRepositoryRoot();

    /// <summary>ソースに置いてはいけない文字と、その表示名</summary>
    private static readonly (char Character, string Name)[] Forbidden =
    [
        ((char)0x0009, "TAB"),
        ((char)0x000C, "FF"),
        ((char)0x0085, "NEL"),
        ((char)0x2028, "U+2028"),
        ((char)0x2029, "U+2029"),
    ];

    /// <summary>テキスト中の禁止文字を「パス:行: 文字名」の形で列挙する（行は LF で数える）</summary>
    internal static IEnumerable<string> FindViolations(string relativePath, string text)
    {
        var line = 1;

        foreach (var c in text)
        {
            if (c == '\n')
            {
                line++;
                continue;
            }

            foreach (var (character, name) in Forbidden)
            {
                if (c == character)
                {
                    yield return $"{relativePath}:{line}: {name}";
                }
            }
        }
    }

    [Fact(DisplayName = "src と tests の C# ソースにタブ・FF・NEL・U+2028・U+2029 の実文字が無い")]
    public void Sources_ContainNoInvisibleCharacters()
    {
        var violations = new[] { "src", "tests" }
            .SelectMany(top =>
                Directory.EnumerateFiles(
                    Path.Combine(Root, top),
                    "*.cs",
                    SearchOption.AllDirectories
                )
            )
            .Where(path =>
                !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
            )
            .SelectMany(path =>
                FindViolations(Path.GetRelativePath(Root, path), File.ReadAllText(path))
            )
            .ToList();

        violations
            .Should()
            .BeEmpty(
                "エスケープ表記（\\t・\\u2028 など）で書くこと。ツール越しの書き込みで実文字に化けていないか確かめる"
            );
    }

    [Fact(DisplayName = "禁止文字の検出は、5 種すべてをファイルと行で名指しする")]
    public void FindViolations_NamesFileAndLine()
    {
        var text = string.Concat(
            "a",
            (char)0x0009,
            "\n",
            (char)0x000C,
            (char)0x0085,
            "\n\n",
            (char)0x2028,
            (char)0x2029
        );

        FindViolations("x.cs", text)
            .Should()
            .Equal("x.cs:1: TAB", "x.cs:2: FF", "x.cs:2: NEL", "x.cs:4: U+2028", "x.cs:4: U+2029");
        FindViolations("x.cs", "plain\r\ntext\n").Should().BeEmpty("CR・LF は対象外");
    }
}
