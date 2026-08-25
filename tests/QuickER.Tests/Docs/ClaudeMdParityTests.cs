using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using AwesomeAssertions;
using QuickER.Tests.Resources;

namespace QuickER.Tests.Docs;

/// <summary>
/// CLAUDE.md の「機械照合できる一覧」（アーキテクチャ図の依存矢印・プロジェクトの網羅・テストフォルダ一覧）が
/// 実リポジトリと一致することを構造的に固定する。
/// </summary>
/// <remarks>
/// <para>
/// 2026-08-25 の全域監査で、CLAUDE.md の陳腐化 11 件のうち 9 件が「変更時に能動的に書き換える動機が
/// 生まれない一覧」（依存方向図の参照列挙・フォルダ列挙）に集中していた（設計判断の散文はゼロ）。
/// プロジェクト参照やテストフォルダは増設した本人が CLAUDE.md を思い出さない限り追従されないため、
/// 一覧の側を実体と突合する網を張る（ResxKeyParityTests・RuntimePackageProjectDependencyGuardTests と同じ流儀）。
/// </para>
/// <para>
/// このテストは CLAUDE.md の書式を契約にする: (1) アーキテクチャ図は「QuickER.Model」で始まる最初の
/// 素の ``` フェンスブロック。(2) 依存矢印行は「QuickER.X → A, B, C」（参照列挙の終端は空白 2 個以上か行末。
/// 矢印を持たない行＝はしご部・依存ゼロ宣言は矢印照合の対象外）。(3) テストのミラー一覧は
/// 「tests/QuickER.Tests/{A|B|…}/」の中括弧、横断フォルダは「横断フォルダは」を含む行のバッククォート
/// 「`X/`」列挙。書式を変えるときはこの契約も更新すること。
/// </para>
/// </remarks>
public class ClaudeMdParityTests
{
    /// <summary>リポジトリルート</summary>
    private static readonly string Root = NeutralResxFiles.FindRepositoryRoot();

    /// <summary>CLAUDE.md の全文</summary>
    private static readonly string ClaudeMd = File.ReadAllText(Path.Combine(Root, "CLAUDE.md"));

    /// <summary>アーキテクチャ図（「QuickER.Model」で始まるフェンスブロック）を取り出す</summary>
    /// <remarks>
    /// フェンスは開閉が対で現れるため、正規表現の単発マッチでなく行走査で開閉を対応付ける
    /// （``` を単発で探すと直前ブロックの閉じフェンスを開きと誤認し、以降の切り出しがすべてずれる）。
    /// </remarks>
    private static string ArchitectureBlock()
    {
        List<string>? current = null;

        foreach (var raw in ClaudeMd.Split('\n'))
        {
            var line = raw.TrimEnd('\r');

            if (line.StartsWith("```", StringComparison.Ordinal))
            {
                if (current is null)
                {
                    current = [];
                }
                else
                {
                    var first = current.FirstOrDefault(l => l.Trim().Length > 0);

                    if (
                        first is not null
                        && first.StartsWith("QuickER.Model", StringComparison.Ordinal)
                    )
                    {
                        return string.Join('\n', current);
                    }

                    current = null;
                }

                continue;
            }

            current?.Add(line);
        }

        throw new InvalidOperationException(
            "アーキテクチャ図のコードブロック（QuickER.Model で始まるフェンスブロック）が見つからない"
        );
    }

    /// <summary>src/{プロジェクト}/{プロジェクト}.csproj の ProjectReference 先（プロジェクト名）を読む</summary>
    private static HashSet<string> ProjectReferencesOf(string projectName)
    {
        var path = Path.Combine(Root, "src", projectName, projectName + ".csproj");
        File.Exists(path).Should().BeTrue($"図に載る {projectName} の csproj が存在すること");

        return Regex
            .Matches(
                File.ReadAllText(path),
                @"ProjectReference\s+Include=""[^""]*?([^""\\/]+)\.csproj"""
            )
            .Select(match => match.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>依存矢印行（プロジェクト名 → 参照列挙）をパースする</summary>
    private static IReadOnlyList<(string Project, HashSet<string> Refs)> ArrowLines()
    {
        var result = new List<(string, HashSet<string>)>();

        foreach (var line in ArchitectureBlock().Split('\n'))
        {
            var match = Regex.Match(line.TrimEnd(), @"^(QuickER\.[A-Za-z.]+)\s*→\s*(.+)$");

            if (!match.Success)
            {
                continue;
            }

            // 参照列挙は空白 2 個以上（説明文との区切り）か行末で終わる
            var rest = match.Groups[2].Value;
            var gap = Regex.Match(rest, @"\s{2,}");
            var refsPart = gap.Success ? rest[..gap.Index] : rest;

            var refs = refsPart
                .Split(',')
                .Select(name => name.Trim())
                .Where(name => name.Length > 0)
                .Select(name => "QuickER." + name)
                .ToHashSet(StringComparer.Ordinal);

            result.Add((match.Groups[1].Value, refs));
        }

        return result;
    }

    /// <summary>依存矢印行の参照列挙が、実 csproj の ProjectReference と過不足なく一致すること</summary>
    [Fact(DisplayName = "CLAUDE.md: アーキテクチャ図の依存矢印が実 csproj の参照と一致する")]
    public void ArchitectureArrows_MatchProjectReferences()
    {
        var arrows = ArrowLines();
        arrows.Should().NotBeEmpty("矢印行が 1 本もパースできないのは書式契約が壊れている合図");

        var problems = new List<string>();

        foreach (var (project, declared) in arrows)
        {
            var actual = ProjectReferencesOf(project);
            var missing = actual.Except(declared).OrderBy(name => name, StringComparer.Ordinal);
            var stale = declared.Except(actual).OrderBy(name => name, StringComparer.Ordinal);

            if (missing.Any() || stale.Any())
            {
                problems.Add(
                    $"{project}: 図に無い実参照=[{string.Join(", ", missing)}] "
                        + $"実体に無い図の参照=[{string.Join(", ", stale)}]"
                );
            }
        }

        problems
            .Should()
            .BeEmpty(
                "依存方向図の矢印行は csproj の ProjectReference と過不足なく一致させること:"
                    + Environment.NewLine
                    + string.Join(Environment.NewLine, problems)
            );
    }

    /// <summary>src/ 配下の全プロジェクトが CLAUDE.md のどこかに登場すること（図または本文）</summary>
    /// <remarks>ランタイムパッケージ 7 種は図でなく「ランタイム配布」節の本文で説明されるため、全文を対象に照合する</remarks>
    [Fact(DisplayName = "CLAUDE.md: src の全プロジェクトが本文に登場する")]
    public void EverySourceProject_AppearsInClaudeMd()
    {
        var projects = Directory
            .GetDirectories(Path.Combine(Root, "src"))
            .Select(Path.GetFileName)
            .Where(name => name!.StartsWith("QuickER.", StringComparison.Ordinal))
            .ToList();

        projects.Should().NotBeEmpty();

        // 前方一致の誤検出（QuickER.Runtime が QuickER.Runtime.Sqlite の部分文字列として拾われる等）を
        // 避けるため、直後にプロジェクト名の続きが来ない出現を要求する。
        // 方言プロバイダの並記（QuickER.SqlServer / PostgreSql / MySql / Oracle / Sqlite）は
        // 「/ 短縮名」の形も正とする
        var missing = projects
            .Where(name =>
                !Regex.IsMatch(
                    ClaudeMd,
                    @"(?:QuickER\.|/ )"
                        + Regex.Escape(name!["QuickER.".Length..])
                        + @"(?!\.?[A-Za-z])"
                )
            )
            .ToList();

        missing
            .Should()
            .BeEmpty(
                "CLAUDE.md に一度も登場しないプロジェクトがある（アーキテクチャ図か該当節へ追記すること）: "
                    + string.Join(", ", missing)
            );
    }

    /// <summary>テストのミラー一覧＋横断フォルダ一覧が tests/QuickER.Tests の実フォルダと一致すること</summary>
    [Fact(DisplayName = "CLAUDE.md: テストのフォルダ一覧（ミラー＋横断）が実フォルダと一致する")]
    public void TestFolderLists_MatchActualFolders()
    {
        var braces = Regex.Match(ClaudeMd, @"tests/QuickER\.Tests/\{([^}]+)\}");
        braces.Success.Should().BeTrue("ミラー一覧 tests/QuickER.Tests/{…}/ が見つかること");
        var mirror = braces
            .Groups[1]
            .Value.Split('|')
            .Select(name => name.Trim())
            .ToHashSet(StringComparer.Ordinal);

        var crossLine = ClaudeMd
            .Split('\n')
            .FirstOrDefault(line => line.Contains("横断フォルダは", StringComparison.Ordinal));
        crossLine.Should().NotBeNull("横断フォルダの列挙行が見つかること");
        var crossCutting = Regex
            .Matches(crossLine!, @"`([A-Za-z.]+)/`")
            .Select(match => match.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);
        crossCutting.Should().NotBeEmpty();

        mirror
            .Intersect(crossCutting)
            .Should()
            .BeEmpty("同じフォルダがミラー一覧と横断一覧の両方に載ってはいけない");

        // ビルド成果物・テスト実行の残骸は一覧の対象外
        var ignored = new HashSet<string>(StringComparer.Ordinal) { "bin", "obj", "TestResults" };
        var actual = Directory
            .GetDirectories(Path.Combine(Root, "tests", "QuickER.Tests"))
            .Select(Path.GetFileName)
            .Where(name => !ignored.Contains(name!))
            .ToHashSet(StringComparer.Ordinal)!;

        var declared = mirror.Union(crossCutting).ToHashSet(StringComparer.Ordinal);
        var undeclared = actual.Except(declared).OrderBy(name => name, StringComparer.Ordinal);
        var phantom = declared.Except(actual!).OrderBy(name => name, StringComparer.Ordinal);

        (undeclared.Any() || phantom.Any())
            .Should()
            .BeFalse(
                $"CLAUDE.md の一覧と実フォルダが食い違う: 一覧に無い実フォルダ=[{string.Join(", ", undeclared)}] "
                    + $"実在しない一覧項目=[{string.Join(", ", phantom)}]"
            );
    }
}
