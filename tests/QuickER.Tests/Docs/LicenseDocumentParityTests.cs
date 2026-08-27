using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using AwesomeAssertions;
using QuickER.Tests.Resources;
using Xunit;

namespace QuickER.Tests.Docs;

/// <summary>
/// ライセンス文書の列挙（<c>LICENSING.md</c> の MIT 行・<c>LICENSE-NC.md</c> のスコープ）が、
/// 公開パッケージのライセンスメタデータと一致することを検証する。
/// </summary>
/// <remarks>
/// <para>
/// これらの列挙は「どのパッケージがどのライセンスか」を読者が確定する場所で、総称へ置き換えられない
/// （<c>QuickER.Cli</c> が MIT でないことは、名前が並んでいて初めて読み取れる）。情報として必要な一方、
/// 追随義務も残る——実際 <c>QuickER.Runtime.Sync</c> の追加時に、LICENSING.md と Directory.Build.props の
/// 両方の列挙から同時に漏れていた。漏れた読者は自分のパッケージが MIT か確認できないまま
/// PolyForm Noncommercial と題した文書へ着地する。
/// </para>
/// <para>
/// <c>LICENSE-NC.md</c> のスコープ列挙は法的に効く正本であり、ここから漏れたプロジェクトは MIT 扱いになる。
/// 逆に MIT のパッケージがここに載ると、商用利用できるはずのものを利用者が諦める。どちらの向きの
/// 食い違いも検出する。
/// </para>
/// </remarks>
public sealed class LicenseDocumentParityTests
{
    private static readonly string Root = NeutralResxFiles.FindRepositoryRoot();

    /// <summary>LICENSING の MIT 行に並ぶパッケージ名が、MIT で公開する 公開パッケージと一致する</summary>
    [Theory(DisplayName = "LICENSING の MIT 行は MIT で公開する 公開パッケージと一致する")]
    [InlineData("LICENSING.md")]
    [InlineData("LICENSING.ja.md")]
    public void LicensingMitRow_MatchesPublishedPackages(string fileName)
    {
        var row = FindMitRow(fileName);

        row.Should()
            .NotBeNull(
                "{0} に MIT のパッケージを並べた表の行（`QuickER.Runtime` を含み `| MIT |` で終わる行）が"
                    + "見つからない。表の書き方を変えたなら、変更後の記述が実際のライセンスメタデータと"
                    + "一致することを確かめたうえで本テストのアンカーを更新すること"
                    + "（見つからないときに検証を飛ばすと、行を書き換えた瞬間にこのガードが静かに無効化される）",
                fileName
            );

        ExpandPackageNames(row!)
            .Should()
            .BeEquivalentTo(
                PublishedPackages().Where(p => p.IsMit).Select(p => p.PackageId),
                "{0} の MIT 行は、PackageLicenseExpression が MIT の 公開パッケージを"
                    + "過不足なく並べていなければならない（読者はこの行で自分のパッケージが MIT かを確認する）",
                fileName
            );
    }

    /// <summary>LICENSE-NC.md のスコープ列挙が、ライセンスファイルを宣言するプロジェクトと整合する</summary>
    [Fact(
        DisplayName = "LICENSE-NC.md のスコープ列挙は NC のパッケージを含み MIT のパッケージを含まない"
    )]
    public void NcScopeList_MatchesPackageLicenseMetadata()
    {
        var scope = NcScopeDirectories();

        scope.Should().NotBeEmpty("LICENSE-NC.md の冒頭にスコープの箇条書きが見つからない");

        foreach (var directory in scope)
        {
            Directory
                .Exists(Path.Combine(Root, directory.Replace('/', Path.DirectorySeparatorChar)))
                .Should()
                .BeTrue(
                    "LICENSE-NC.md が列挙する {0} が実在しない（プロジェクトを改名・削除したなら"
                        + "ライセンス文書の列挙も更新すること）",
                    directory
                );
        }

        foreach (var project in PublishedPackages())
        {
            var listed = scope.Contains(project.ScopePath, StringComparer.OrdinalIgnoreCase);

            if (project.IsMit)
            {
                listed
                    .Should()
                    .BeFalse(
                        "{0} は PackageLicenseExpression が MIT なのに LICENSE-NC.md のスコープへ載っている"
                            + "（商用利用できるはずのものを利用者が諦める）",
                        project.PackageId
                    );
            }
            else
            {
                listed
                    .Should()
                    .BeTrue(
                        "{0} は PackageLicenseFile で {1} を同梱するのに、その文書のスコープ列挙へ載っていない"
                            + "（スコープ外は MIT 扱いになるため、同梱した条件が及ばない）",
                        project.PackageId,
                        project.LicenseFile
                    );
            }
        }
    }

    /// <summary>公開パッケージはライセンス式とライセンスファイルのどちらか一方だけを宣言する</summary>
    /// <remarks>NuGet は両方の同時指定を許さない。未指定のまま公開するとライセンス不明のパッケージになる。</remarks>
    [Fact(DisplayName = "公開パッケージはライセンス式とファイルのどちらか一方だけを宣言する")]
    public void PublishedPackages_DeclareExactlyOneLicenseForm()
    {
        foreach (var project in PublishedPackages())
        {
            (project.IsMit ^ project.LicenseFile is not null)
                .Should()
                .BeTrue(
                    "{0} は PackageLicenseExpression と PackageLicenseFile のどちらか一方だけを"
                        + "宣言していなければならない（式={1} / ファイル={2}）",
                    project.PackageId,
                    project.LicenseExpression ?? "なし",
                    project.LicenseFile ?? "なし"
                );
        }
    }

    /// <summary>LICENSING の、MIT のパッケージを並べた表の行を 1 つ取り出す（見つからなければ null）</summary>
    private static string? FindMitRow(string fileName) =>
        File.ReadAllLines(Path.Combine(Root, fileName))
            .FirstOrDefault(line =>
                line.Contains("`QuickER.Runtime`", StringComparison.Ordinal)
                && line.Contains("| MIT |", StringComparison.Ordinal)
            );

    /// <summary>
    /// 表の行に並ぶパッケージ名を展開する。2 つ目以降は先頭を省いた短縮形
    /// （<c>`QuickER.Runtime` / `.SqlServer`</c>）で書かれているため、先頭の名前を補う。
    /// </summary>
    private static IReadOnlyList<string> ExpandPackageNames(string row)
    {
        var tokens = Regex
            .Matches(row, "`([^`]+)`")
            .Select(match => match.Groups[1].Value)
            .ToList();

        if (tokens.Count == 0)
        {
            return [];
        }

        var prefix = tokens[0];

        return tokens.Select(token => token.StartsWith('.') ? prefix + token : token).ToList();
    }

    /// <summary>LICENSE-NC.md の冒頭スコープ列挙（<c>- `src/QuickER.Xxx`</c>）を読む</summary>
    private static IReadOnlyList<string> NcScopeDirectories() =>
        File.ReadAllLines(Path.Combine(Root, "LICENSE-NC.md"))
            .Select(line => Regex.Match(line, @"^- `(src/[^`]+)`\s*$"))
            .Where(match => match.Success)
            .Select(match => match.Groups[1].Value)
            .ToList();

    /// <summary>publish.yml が pack する各プロジェクトのライセンスメタデータを読む</summary>
    /// <remarks>
    /// 対象の正本は csproj のプロパティではなく publish.yml の pack 一覧とする。<c>IsPackable</c> を
    /// 見る実装では <c>QuickER.Cli</c>（<c>PackAsTool</c> で成立させており <c>IsPackable</c> を書いていない）が
    /// 漏れ、唯一の NC パッケージが検証対象外になっていた。公開一覧から引けば、パッケージを追加した
    /// のにライセンスメタデータを書き忘れた場合もここで落ちる。
    /// </remarks>
    private static IReadOnlyList<PublishedPackage> PublishedPackages()
    {
        var workflow = File.ReadAllText(Path.Combine(Root, ".github", "workflows", "publish.yml"));

        var projectPaths = Regex
            .Matches(workflow, @"src/(?<dir>[^/\s]+)/[^/\s]+\.csproj")
            .Select(match => match.Groups["dir"].Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        projectPaths
            .Should()
            .NotBeEmpty(
                "publish.yml から pack 対象のプロジェクトを 1 つも読み取れなかった"
                    + "（ワークフローの書き方を変えたなら本テストの抽出も更新すること）"
            );

        return projectPaths
            .Select(directoryName =>
            {
                var csproj = Path.Combine(Root, "src", directoryName, $"{directoryName}.csproj");
                var document = XDocument.Load(csproj);

                return new PublishedPackage(
                    Property(document, "PackageId") ?? directoryName,
                    $"src/{directoryName}",
                    Property(document, "PackageLicenseExpression"),
                    Property(document, "PackageLicenseFile")
                );
            })
            .ToList();
    }

    /// <summary>プロジェクト XML から指定プロパティの値を読む（名前空間非依存・最初の 1 つ）</summary>
    private static string? Property(XDocument document, string propertyName) =>
        document.Descendants().FirstOrDefault(e => e.Name.LocalName == propertyName)?.Value.Trim();

    /// <summary>公開パッケージのライセンスメタデータ</summary>
    private sealed record PublishedPackage(
        string PackageId,
        string ScopePath,
        string? LicenseExpression,
        string? LicenseFile
    )
    {
        /// <summary>MIT のライセンス式で公開するか</summary>
        public bool IsMit =>
            string.Equals(LicenseExpression, "MIT", StringComparison.OrdinalIgnoreCase);
    }
}
