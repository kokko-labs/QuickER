using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using QuickER.Tests.Docs;

namespace QuickER.Tests.TestSupport.Guards;

/// <summary>ソースの規則の表：src・ワークフロー・スクリプト・生成テンプレートが名指すテストの名前と行 Id の実在</summary>
/// <remarks>
/// <para>
/// CLAUDE.md の名指しは <see cref="ClaudeMdReferenceTests"/> が照合するが、src の XmlDoc・コメントやワークフロー・スクリプトの
/// 名指しはテストの改名・表の行への移し替えで古いまま残る（テストのクラスを表の行へ移したとき、src の XmlDoc 5 か所が消えたクラスを
/// 名指ししたまま残っていた）。読み方は <see cref="ClaudeMdReferenceTests.FindMissingTestNames"/>・
/// <see cref="ClaudeMdReferenceTests.FindMissingRowIds"/>（書式の契約）をそのまま使い、写しを作らない。
/// </para>
/// <para>
/// C# のソースはコメント（<c>//</c>・<c>///</c>・<c>/* */</c>。条件コンパイルのどの分岐も読む）だけを読む。ワークフロー・スクリプト・
/// 生成テンプレートは本文の全部を読む（コメントのほか、テストの絞り込みの文字列もテストの名前を書くので、実在しなければ同じく古い）。
/// 行 Id は「行」の直後に書いたもの（<c>行 xxx-yyy</c>・<c>行 &lt;c&gt;xxx-yyy&lt;/c&gt;</c>・<c>行 `xxx-yyy`</c>）だけを拾う。
/// </para>
/// </remarks>
internal static partial class SourceRules
{
    /// <summary>名指しの規則が守ること</summary>
    private const string StaleTestNameBecause =
        "コメント・文書・ワークフローが名指すテストの名前と行 Id は実在する（改名・表の行への移し替えに追従する）";

    /// <summary>「行」の直後の行 Id（囲みは無し・&lt;c&gt;・バッククォート）</summary>
    private static readonly Regex RowIdAfterRowWord = new(
        "行\\s*(?:<c>|`)?([a-z][0-9a-z]*-[-0-9a-z*]*[0-9a-z*])",
        RegexOptions.CultureInvariant
    );

    private static IEnumerable<SourceRule> Comments() =>
        [
            SourceRule.Forbid(
                "comment-test-names-src",
                StaleTestNameBecause,
                Src,
                GuardFinder.File(GuardReader.Tokens, FindStaleNamesInComments)
            ),
            SourceRule.Forbid(
                "comment-test-names-templates",
                StaleTestNameBecause,
                SourceScope.Under("src", ".scriban"),
                GuardFinder.File(GuardReader.Text, FindStaleNamesInText)
            ),
            SourceRule.Forbid(
                "comment-test-names-workflows",
                StaleTestNameBecause,
                SourceScope.Under(".github/workflows", ".yml"),
                GuardFinder.File(GuardReader.Text, FindStaleNamesInText)
            ),
            SourceRule.Forbid(
                "comment-test-names-scripts",
                StaleTestNameBecause,
                SourceScope.Under("scripts", ".ps1"),
                GuardFinder.File(GuardReader.Text, FindStaleNamesInText)
            ),
        ];

    /// <summary>C# のソースのコメント（全組み合わせの構文木で、同じコメントは 1 回だけ）から、実在しない名指しを拾う</summary>
    private static IEnumerable<GuardFinding> FindStaleNamesInComments(GuardFile file)
    {
        var seen = new HashSet<int>();

        foreach (var token in file.Tokens)
        {
            foreach (var trivia in token.LeadingTrivia.Concat(token.TrailingTrivia))
            {
                if (!IsComment(trivia) || !seen.Add(trivia.SpanStart))
                {
                    continue;
                }

                var firstLine = trivia.GetLocation().GetLineSpan().StartLinePosition.Line + 1;

                foreach (var finding in FindStaleNames(trivia.ToFullString(), firstLine))
                {
                    yield return finding;
                }
            }
        }
    }

    /// <summary>本文の全部から、実在しない名指しを拾う</summary>
    private static IEnumerable<GuardFinding> FindStaleNamesInText(GuardFile file) =>
        FindStaleNames(file.Text, 1);

    /// <summary>コメントの種類か（行・ブロック・ドキュメント）</summary>
    private static bool IsComment(SyntaxTrivia trivia) =>
        trivia.IsKind(SyntaxKind.SingleLineCommentTrivia)
        || trivia.IsKind(SyntaxKind.MultiLineCommentTrivia)
        || trivia.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia)
        || trivia.IsKind(SyntaxKind.MultiLineDocumentationCommentTrivia);

    /// <summary>本文を行ごとに読み、tests/ に実在しないテストの名前と「行」の直後の行 Id を返す</summary>
    /// <param name="text">本文</param>
    /// <param name="firstLine">本文の 1 行目のファイルの中の行（1 始まり）</param>
    private static IEnumerable<GuardFinding> FindStaleNames(string text, int firstLine)
    {
        var sources = ClaudeMdReferenceTests.Sources.Value;
        var lines = text.Split('\n');

        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index];

            foreach (var name in ClaudeMdReferenceTests.FindMissingTestNames(line, sources))
            {
                yield return new GuardFinding(name, firstLine + index);
            }

            foreach (Match match in RowIdAfterRowWord.Matches(line))
            {
                var id = match.Groups[1].Value;

                if (ClaudeMdReferenceTests.FindMissingRowIds($"`{id}`", sources).Count > 0)
                {
                    yield return new GuardFinding("行 " + id, firstLine + index);
                }
            }
        }
    }
}
