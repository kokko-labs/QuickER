using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using AwesomeAssertions;
using QuickER.Tests.Resources;
using Xunit;

namespace QuickER.Tests.Docs;

/// <summary>
/// ドキュメント索引（<c>docs/README.md</c>・<c>docs/README.ja.md</c>）とルート README の
/// ドキュメント節が、<c>docs/</c> 配下の実ファイルと一致することを検証する。
/// </summary>
/// <remarks>
/// <para>
/// ドキュメントを 1 本足すと、追随すべき列挙が 4 つになる（索引 en/ja・ルート README en/ja）。
/// この種の「変更時に能動的に書き換える動機が生まれない一覧」は実際に漏れる
/// （<see cref="LicenseDocumentParityTests"/> の LICENSING 漏れが前例）。漏れた読者は、
/// フォルダを開いても索引に無いドキュメントへ到達できない。
/// </para>
/// <para>
/// 正本は <c>docs/</c> 配下の実ファイルで、列挙の側を実体へ突き合わせる
/// （ClaudeMdParityTests・ResxKeyParityTests と同じ流儀）。
/// 言語の対も固定する＝英語版だけ足して日本語版を忘れると、日本語の読者は索引から
/// 英語ページへ落ちる。
/// </para>
/// </remarks>
public sealed class DocsIndexParityTests
{
    /// <summary>リポジトリルート</summary>
    private static readonly string Root = NeutralResxFiles.FindRepositoryRoot();

    /// <summary>ドキュメントフォルダ</summary>
    private static readonly string DocsDirectory = Path.Combine(Root, "docs");

    /// <summary>Markdown のインラインリンク（<c>](リンク先)</c>）</summary>
    private static readonly Regex LinkPattern = new(@"\]\(([^)\s]+)\)", RegexOptions.Compiled);

    /// <summary>docs/ 配下の実ドキュメント（索引 README 自身は除く）のファイル名</summary>
    /// <param name="japanese">true なら日本語版（<c>*.ja.md</c>）、false なら英語版</param>
    private static IReadOnlyList<string> DocumentFileNames(bool japanese) =>
        Directory
            .GetFiles(DocsDirectory, "*.md")
            .Select(Path.GetFileName)
            .Where(name => name is not null)
            .Select(name => name!)
            .Where(name => !IsIndex(name))
            .Where(name => IsJapanese(name) == japanese)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

    /// <summary>索引 README 自身か</summary>
    private static bool IsIndex(string fileName) =>
        fileName.Equals("README.md", StringComparison.OrdinalIgnoreCase)
        || fileName.Equals("README.ja.md", StringComparison.OrdinalIgnoreCase);

    /// <summary>日本語版のファイル名か</summary>
    private static bool IsJapanese(string fileName) =>
        fileName.EndsWith(".ja.md", StringComparison.OrdinalIgnoreCase);

    /// <summary>アンカー（<c>#...</c>）を落としてリンク先のパスだけを取り出す</summary>
    private static string StripAnchor(string link)
    {
        var hash = link.IndexOf('#');

        return hash < 0 ? link : link[..hash];
    }

    /// <summary>索引 README が列挙する、同じフォルダ内のドキュメント（索引自身と外部リンクは除く）</summary>
    private static IReadOnlyList<string> IndexedDocuments(string indexFileName)
    {
        var text = File.ReadAllText(Path.Combine(DocsDirectory, indexFileName));

        return LinkPattern
            .Matches(text)
            .Select(match => StripAnchor(match.Groups[1].Value))
            // 同じフォルダ内の相対リンクだけを索引の項目とみなす（../ 始まりと http(s): は対象外）
            .Where(link =>
                link.EndsWith(".md", StringComparison.OrdinalIgnoreCase) && !link.Contains('/')
            )
            .Where(link => !IsIndex(link))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(link => link, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>ルート README の指定見出し節が列挙する <c>docs/</c> 配下のドキュメント</summary>
    private static IReadOnlyList<string> RootReadmeSectionDocuments(
        string readmeFileName,
        string heading
    )
    {
        var lines = File.ReadAllLines(Path.Combine(Root, readmeFileName));
        var start = Array.FindIndex(lines, line => line.Trim() == heading);
        start.Should().BeGreaterThanOrEqualTo(0, $"{readmeFileName} に見出し「{heading}」がある");

        var section = lines
            .Skip(start + 1)
            .TakeWhile(line => !line.StartsWith("## ", StringComparison.Ordinal));

        return LinkPattern
            .Matches(string.Join('\n', section))
            .Select(match => StripAnchor(match.Groups[1].Value))
            .Where(link => link.StartsWith("docs/", StringComparison.Ordinal))
            .Select(link => link["docs/".Length..])
            .Where(link => !link.Contains('/') && !IsIndex(link))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(link => link, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>索引 README の列挙が docs/ 配下の実ファイルと一致する</summary>
    [Theory(DisplayName = "docs の索引 README は同言語の全ドキュメントをちょうど列挙する")]
    [InlineData("README.md", false)]
    [InlineData("README.ja.md", true)]
    public void DocsIndex_ListsEveryDocumentOfItsLanguage(string indexFileName, bool japanese)
    {
        IndexedDocuments(indexFileName)
            .Should()
            .Equal(
                DocumentFileNames(japanese),
                $"{indexFileName} の列挙は docs/ 配下の実ファイルと一致する（ドキュメントを増減したら索引も更新する）"
            );
    }

    /// <summary>ルート README のドキュメント節が docs/ 配下の実ファイルと一致する</summary>
    [Theory(
        DisplayName = "ルート README のドキュメント節は同言語の全ドキュメントをちょうど列挙する"
    )]
    [InlineData("README.md", "## Documentation", false)]
    [InlineData("README.ja.md", "## ドキュメント", true)]
    public void RootReadme_ListsEveryDocumentOfItsLanguage(
        string readmeFileName,
        string heading,
        bool japanese
    )
    {
        RootReadmeSectionDocuments(readmeFileName, heading)
            .Should()
            .Equal(
                DocumentFileNames(japanese),
                $"{readmeFileName} の「{heading}」節は docs/ 配下の実ファイルと一致する"
            );
    }

    /// <summary>すべてのドキュメントに英語版と日本語版の対がある</summary>
    [Fact(DisplayName = "docs の全ドキュメントに en / ja の対がある")]
    public void EveryDocument_HasBothLanguages()
    {
        var japanese = DocumentFileNames(japanese: true)
            .Select(name => name[..^".ja.md".Length])
            .OrderBy(name => name, StringComparer.Ordinal);

        var english = DocumentFileNames(japanese: false)
            .Select(name => name[..^".md".Length])
            .OrderBy(name => name, StringComparer.Ordinal);

        japanese
            .Should()
            .Equal(english, "英語版と日本語版は対で増減する（片方だけ足すと索引が他言語へ落とす）");
    }
}
