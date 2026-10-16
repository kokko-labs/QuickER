using System.IO;
using AwesomeAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using QuickER.Tests.Resources;

namespace QuickER.Tests.Docs;

/// <summary>
/// 大文字小文字を無視する正規表現は、書式のカルチャに依らず同じ文字を同じとみなすこと（<c>CultureInvariant</c>）を固定する
/// </summary>
/// <remarks>
/// <para>
/// <c>RegexOptions.IgnoreCase</c> だけの <c>new Regex</c> は、作った時点の書式のカルチャで大文字小文字を対応づける。
/// トルコ語（tr-TR）では <c>I</c> の小文字が <c>ı</c>・<c>i</c> の大文字が <c>İ</c> なので、<c>AUTOINCREMENT</c> が
/// <c>autoincrement</c> に一致しなくなる。静的なフィールドの Regex は型を初めて使った時点のカルチャで作られるので、
/// トルコ語の Windows で起動すると、SQLite の再構築で失われる属性の警告が欠け、モックの検証が外部参照を見落とす
/// （実挙動は <see cref="QuickER.Tests.Provider.TableRebuildAttributeDetectorCultureTests"/> が確かめる）。
/// </para>
/// <para>
/// 拾う基準：<c>src/</c> の <c>.cs</c>（<c>bin</c> / <c>obj</c> を除く）を構文木で走査し、<c>RegexOptions.IgnoreCase</c> を含む
/// <c>|</c> の連なり（いちばん外側のビット和）に <c>RegexOptions.CultureInvariant</c> が無いものと、文字列リテラルの中の
/// インラインの大文字小文字の指定（<c>(?i</c>）を数える。一覧は空で、出現は 1 つでも赤にする。
/// <c>[GeneratedRegex]</c> もこの規則に含める（実測では tr-TR でも一致したが、生成器の実装に頼らない）。
/// </para>
/// </remarks>
public sealed class RegexCultureGuardTests
{
    [Fact(DisplayName = "大文字小文字を無視する正規表現には、必ず CultureInvariant を付ける")]
    public void IgnoreCaseRegexes_AreCultureInvariant()
    {
        var root = NeutralResxFiles.FindRepositoryRoot();
        var src = Path.Combine(root, "src");
        var found = new List<string>();
        var scanned = 0;

        foreach (
            var path in Directory
                .EnumerateFiles(src, "*.cs", SearchOption.AllDirectories)
                .Where(path =>
                    !path.Split(Path.DirectorySeparatorChar)
                        .Any(segment => segment is "bin" or "obj")
                )
        )
        {
            var relative = Path.GetRelativePath(root, path).Replace('\\', '/');
            var tree = CSharpSyntaxTree.ParseText(
                File.ReadAllText(path),
                cancellationToken: TestContext.Current.CancellationToken
            );
            var nodes = tree.GetRoot(TestContext.Current.CancellationToken)
                .DescendantNodes()
                .ToList();

            foreach (
                var access in nodes
                    .OfType<MemberAccessExpressionSyntax>()
                    .Where(access => IsRegexOption(access, "IgnoreCase"))
            )
            {
                scanned++;
                var chain = OutermostBitwiseOr(access);

                if (
                    !chain
                        .DescendantNodesAndSelf()
                        .OfType<MemberAccessExpressionSyntax>()
                        .Any(member => IsRegexOption(member, "CultureInvariant"))
                )
                {
                    found.Add($"{relative}:{Line(access)}: {chain}");
                }
            }

            foreach (
                var literal in nodes
                    .OfType<LiteralExpressionSyntax>()
                    .Where(literal => literal.IsKind(SyntaxKind.StringLiteralExpression))
                    .Where(literal =>
                        literal.Token.ValueText.Contains("(?i", StringComparison.Ordinal)
                    )
            )
            {
                found.Add($"{relative}:{Line(literal)}: インラインの (?i");
            }
        }

        scanned.Should().BeGreaterThan(40, "大文字小文字を無視する正規表現を見つけられること");
        found
            .Should()
            .BeEmpty(
                "大文字小文字を無視する正規表現は RegexOptions.CultureInvariant を併せて付ける（トルコ語では I と i が対応しない）"
            );
    }

    /// <summary>
    /// 正規表現の数字は <c>[0-9]</c> で書き、<c>\d</c> は <c>RegexOptions.ECMAScript</c> を付けたときだけ使う
    /// </summary>
    /// <remarks>
    /// .NET の <c>\d</c> は Unicode の 10 進数字（全角の <c>１</c>・アラビア文字の数字など）にも一致する。数値として読む箇所は
    /// インバリアントの解析が ASCII の数字しか読まないので、一致したのに読めずに例外になったり（SQL Server の型の表記の正規化）、
    /// 読めない数字を含む型の表記を安全とみなして DDL へ通したりしていた。拾う基準：<c>src/</c> の <c>.cs</c>（<c>bin</c> / <c>obj</c> を除く）の
    /// 文字列リテラルのうち、値に奇数個の <c>\</c> に続く <c>d</c> を含むもので、同じ呼び出し・属性の引数に
    /// <c>RegexOptions.ECMAScript</c> が無いもの。一覧は空で、出現は 1 つでも赤にする。
    /// </remarks>
    [Fact(DisplayName = "正規表現の数字は [0-9] で書く（\\d は ECMAScript を付けたときだけ）")]
    public void DigitClasses_AreAsciiOnly()
    {
        var root = NeutralResxFiles.FindRepositoryRoot();
        var src = Path.Combine(root, "src");
        var found = new List<string>();
        var scanned = 0;

        foreach (
            var path in Directory
                .EnumerateFiles(src, "*.cs", SearchOption.AllDirectories)
                .Where(path =>
                    !path.Split(Path.DirectorySeparatorChar)
                        .Any(segment => segment is "bin" or "obj")
                )
        )
        {
            var relative = Path.GetRelativePath(root, path).Replace('\\', '/');
            var tree = CSharpSyntaxTree.ParseText(
                File.ReadAllText(path),
                cancellationToken: TestContext.Current.CancellationToken
            );

            foreach (
                var literal in tree.GetRoot(TestContext.Current.CancellationToken)
                    .DescendantNodes()
                    .OfType<LiteralExpressionSyntax>()
                    .Where(literal => literal.IsKind(SyntaxKind.StringLiteralExpression))
            )
            {
                if (literal.Ancestors().Any(IsRegexConstruction))
                {
                    scanned++;
                }

                if (
                    ContainsDigitClass(literal.Token.ValueText)
                    && !(
                        literal.Parent?.Parent is { } arguments
                        && arguments
                            .DescendantNodes()
                            .OfType<MemberAccessExpressionSyntax>()
                            .Any(member => IsRegexOption(member, "ECMAScript"))
                    )
                )
                {
                    found.Add($"{relative}:{Line(literal)}: {literal}");
                }
            }
        }

        scanned.Should().BeGreaterThan(40, "正規表現のパターンを見つけられること");
        found
            .Should()
            .BeEmpty(
                "正規表現の数字は [0-9] で書く（\\d は全角などの Unicode の数字にも一致し、インバリアントの数値の解析と食い違う）"
            );
    }

    [Theory(DisplayName = "\\d の判定は、直前の \\ の数が奇数のときだけ数える")]
    [InlineData(@"a\d", true)]
    [InlineData(@"a\\d", false)]
    [InlineData(@"a\\\d", true)]
    [InlineData(@"[0-9]d", false)]
    public void ContainsDigitClass_CountsPrecedingBackslashes(string value, bool expected) =>
        ContainsDigitClass(value).Should().Be(expected);

    /// <summary>値に正規表現の <c>\d</c>（奇数個の <c>\</c> に続く <c>d</c>）を含むか</summary>
    private static bool ContainsDigitClass(string value)
    {
        for (var i = value.IndexOf('d'); i >= 0; i = value.IndexOf('d', i + 1))
        {
            var backslashes = 0;

            while (i - backslashes - 1 >= 0 && value[i - backslashes - 1] == '\\')
            {
                backslashes++;
            }

            if (backslashes % 2 == 1)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>正規表現を作る構文（<c>new Regex(…)</c>・<c>Regex.静的メソッド(…)</c>・<c>[GeneratedRegex(…)]</c>）か</summary>
    private static bool IsRegexConstruction(SyntaxNode node) =>
        node switch
        {
            ObjectCreationExpressionSyntax creation => creation
                .Type.ToString()
                .EndsWith("Regex", StringComparison.Ordinal),
            InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax member } => member
                .Expression.ToString()
                .Split('.')[^1] == "Regex",
            AttributeSyntax attribute => attribute
                .Name.ToString()
                .EndsWith("GeneratedRegex", StringComparison.Ordinal),
            _ => false,
        };

    /// <summary><c>RegexOptions.名前</c> の参照か（<c>System.Text.RegularExpressions.RegexOptions.名前</c> も含む）</summary>
    private static bool IsRegexOption(MemberAccessExpressionSyntax access, string name) =>
        access.Name.Identifier.ValueText == name
        && access.Expression.ToString().Split('.')[^1] == "RegexOptions";

    /// <summary>節を含むいちばん外側のビット和（<c>|</c>）。連なっていなければ節そのもの</summary>
    private static SyntaxNode OutermostBitwiseOr(SyntaxNode node)
    {
        var current = node;

        while (
            current.Parent is BinaryExpressionSyntax binary
                && binary.IsKind(SyntaxKind.BitwiseOrExpression)
            || current.Parent is ParenthesizedExpressionSyntax
        )
        {
            current = current.Parent;
        }

        return current;
    }

    private static int Line(SyntaxNode node) =>
        node.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
}
