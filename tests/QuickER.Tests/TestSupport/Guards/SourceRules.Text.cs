using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace QuickER.Tests.TestSupport.Guards;

/// <summary>ソースの規則の表：ソースのテキストの横断の規則（見えない文字・英語ガードの CJK の範囲・カルチャ・変異テストの Safe Mode）</summary>
/// <remarks>
/// tests を走査する規則が、このファイル自身の文字列を拾わないよう、目印の文字はすべて数値から作る（範囲は狭めない）。
/// </remarks>
internal static partial class SourceRules
{
    /// <summary>見えない文字の規則が守ること</summary>
    private const string InvisibleCharacterBecause =
        "タブ・FF・NEL・U+2028・U+2029 はエスケープ表記（\\t・\\u2028 など）で書く（ツール越しの書き込みで実文字に化けると、コンパイルが通ったまま値だけが変わる）";

    /// <summary>ソースに置いてはいけない文字と、その表示名（文字は数値から作る）</summary>
    private static readonly (char Character, string Name)[] InvisibleCharacters =
    [
        ((char)0x0009, "TAB"),
        ((char)0x000C, "FF"),
        ((char)0x0085, "NEL"),
        ((char)0x2028, "U+2028"),
        ((char)0x2029, "U+2029"),
    ];

    /// <summary>CJK の文字の範囲の写しの目印（範囲の両端を「-」でつないだもの。数値から作る）</summary>
    private static readonly string[] CjkRangeMarkers =
    [
        new string([(char)0x3000, '-', (char)0x9FFF]),
        new string([(char)0xFF00, '-', (char)0xFFEF]),
    ];

    private static IEnumerable<SourceRule> Text() =>
        [
            // src と tests の C# ソースに、タブと改行に化ける見えない文字の実文字を置かない（文字列リテラル・コメントの中も）。
            // インデントのタブは整形（CSharpier）が空白へ直すので、違反になるのは整形の届かない文字列リテラル・コメントの中だけ
            SourceRule.Forbid(
                "source-invisible-characters-src",
                InvisibleCharacterBecause,
                Src,
                GuardFinder.File(GuardReader.Text, FindInvisibleCharacters)
            ),
            SourceRule.Forbid(
                "source-invisible-characters-tests",
                InvisibleCharacterBecause,
                SourceScope.Under("tests"),
                GuardFinder.File(GuardReader.Text, FindInvisibleCharacters)
            ),
            // 英語ガードのテストが使う CJK の文字の範囲は TestSupport の CjkText.Pattern だけが持つ（写しを置くと、1 か所だけ狭い
            // 写しの出力の混入を見逃す）。文字列リテラルの値（エスケープを解いた後）に範囲の始まりの表記を含むものを数える
            // （実文字で書いた写しも U+3000 のエスケープの形で書いた写しも拾う）
            SourceRule.Forbid(
                "cjk-range-copies",
                "CJK の文字の範囲は TestSupport.CjkText.Pattern を使い、写さない",
                SourceScope.Under("tests"),
                GuardFinder.Tokens(token =>
                    token.IsKind(SyntaxKind.StringLiteralToken)
                    && CjkRangeMarkers.Any(marker =>
                        token.ValueText.Contains(marker, StringComparison.Ordinal)
                    )
                        ? "CJK の範囲"
                        : null
                ),
                new GuardKnown(
                    "tests/QuickER.Tests/TestSupport/CjkText.cs",
                    "CJK の範囲",
                    1,
                    "範囲の正本（CjkText.Pattern）そのもの（無くなると走査が空振りしている）"
                )
            ),
            // 大文字小文字を無視する正規表現は、書式のカルチャに依らず同じ文字を同じとみなす（CultureInvariant）。IgnoreCase だけの
            // new Regex は作った時点のカルチャで対応づけるので、トルコ語では I と i が対応せず AUTOINCREMENT が autoincrement に一致しない
            // （実挙動は TableRebuildAttributeDetectorCultureTests）。RegexOptions.IgnoreCase を含むいちばん外側のビット和に
            // CultureInvariant が無いものと、文字列リテラルの中のインラインの (?i を数える。[GeneratedRegex] もこの規則に含める
            SourceRule.Forbid(
                "regex-ignore-case-culture",
                "大文字小文字を無視する正規表現は RegexOptions.CultureInvariant を併せて付ける（トルコ語では I と i が対応しない）",
                Src,
                GuardFinder.Nodes(DescribeCultureSensitiveRegex)
            ) with
            {
                Floor = new(
                    GuardFinder.Nodes(node =>
                        node is MemberAccessExpressionSyntax access
                        && IsRegexOption(access, "IgnoreCase")
                            ? "IgnoreCase"
                            : null
                    ),
                    41,
                    "RegexOptions.IgnoreCase の箇所"
                ),
            },
            // 正規表現の数字は [0-9] で書き、\d は RegexOptions.ECMAScript を付けたときだけ使う（.NET の \d は全角などの Unicode の
            // 数字にも一致し、インバリアントの数値の解析と食い違う）。文字列リテラルの値に奇数個の \ に続く d を含むもので、
            // 同じ呼び出し・属性の引数に ECMAScript が無いものを数える
            SourceRule.Forbid(
                "regex-digit-classes",
                "正規表現の数字は [0-9] で書く（\\d は全角などの Unicode の数字にも一致し、インバリアントの数値の解析と食い違う）",
                Src,
                GuardFinder.Nodes(node =>
                    node is LiteralExpressionSyntax literal
                    && literal.IsKind(SyntaxKind.StringLiteralExpression)
                    && ContainsDigitClass(literal.Token.ValueText)
                    && !(
                        literal.Parent?.Parent is { } arguments
                        && arguments
                            .DescendantNodes()
                            .OfType<MemberAccessExpressionSyntax>()
                            .Any(member => IsRegexOption(member, "ECMAScript"))
                    )
                        ? literal.ToString()
                        : null
                )
            ) with
            {
                Floor = new(
                    GuardFinder.Nodes(node =>
                        node is LiteralExpressionSyntax literal
                        && literal.IsKind(SyntaxKind.StringLiteralExpression)
                        && literal.Ancestors().Any(IsRegexConstruction)
                            ? "パターン"
                            : null
                    ),
                    41,
                    "正規表現を作る構文の中の文字列リテラル"
                ),
            },
            // 変異テストの範囲のプロジェクトでは、早く抜ける if の条件の || の 2 つ目以降の項で宣言した out の変数と、条件のパターンの
            // 変数（単独の is not { } x を含む）を if の後ろで使わない。Stryker が if の跳び（continue / break / return; / throw）を消す
            // 変異で変数が確実に代入されなくなり（CS0165）、そのメソッドの変異を全部外す（Safe Mode）。項ごとに早く抜ける if に分けて
            // out の呼び出しを 1 つ目の項にするか、初期化子つきの変数を null で判定するか、肯定形の then で使う
            SourceRule.Forbid(
                "mutation-safe-mode-jump-guards",
                "変異テストの範囲で、早く抜ける if の || の 2 つ目以降の項の out の変数と条件のパターンの変数を if の後ろで使わない（跳びを消す変異が CS0165 になり、Stryker がそのメソッドの変異を全部外す）",
                Src.Within("QuickER.Model")
                    .Within("QuickER.Provider")
                    .Within("QuickER.CodeGen.CSharp"),
                GuardFinder.Nodes(SafeModeJumpGuards.Describe)
            ) with
            {
                // 範囲が 8 つのプロジェクト（Model・Provider と 5 方言・CodeGen.CSharp）を読んでいること（いまは 186 ファイル）
                MinimumFiles = 150,
                // 候補（跳びで終わる if 文）を見つけていること（いまは 432）
                Floor = new(
                    GuardFinder.Nodes(SafeModeJumpGuards.DescribeCandidate),
                    400,
                    "跳びで終わる if 文"
                ),
            },
        ];

    /// <summary>
    /// IgnoreCase を含むいちばん外側のビット和に CultureInvariant が無ければそのビット和の表記を、インラインの <c>(?i</c> を含む
    /// 文字列リテラルなら「インラインの (?i」を返す
    /// </summary>
    private static string? DescribeCultureSensitiveRegex(SyntaxNode node)
    {
        if (node is MemberAccessExpressionSyntax access && IsRegexOption(access, "IgnoreCase"))
        {
            var chain = OutermostBitwiseOr(access);

            return chain
                .DescendantNodesAndSelf()
                .OfType<MemberAccessExpressionSyntax>()
                .Any(member => IsRegexOption(member, "CultureInvariant"))
                ? null
                : chain.ToString();
        }

        return
            node is LiteralExpressionSyntax literal
            && literal.IsKind(SyntaxKind.StringLiteralExpression)
            && literal.Token.ValueText.Contains("(?i", StringComparison.Ordinal)
            ? "インラインの (?i"
            : null;
    }

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

    /// <summary>本文の見えない文字（1 文字 1 件。何かは文字の表示名・行は LF で数える）</summary>
    private static IEnumerable<GuardFinding> FindInvisibleCharacters(GuardFile file)
    {
        var line = 1;

        foreach (var c in file.Text)
        {
            if (c == '\n')
            {
                line++;
                continue;
            }

            foreach (var (character, name) in InvisibleCharacters)
            {
                if (c == character)
                {
                    yield return new GuardFinding(name, line);
                }
            }
        }
    }
}
