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
        // 方言プロバイダの並記（QuickER.Provider.SqlServer / PostgreSql / MySql / Oracle / Sqlite）は
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

    /// <summary>
    /// 「テンプレート変更時は…」の箇条（再生成手順の正本）の本文を取り出す。
    /// </summary>
    /// <remarks>
    /// 範囲は <c>- **テンプレート変更時は**</c> で始まる行から、次に行頭が <c>- </c> の行の直前まで
    /// （途中の再生成コマンドのフェンスと、それに続く段落も本文に含む）。
    /// <b>照合は CLAUDE.md 全体でなくこの本文に限る</b>＝全体で探すと、別の節での言及で偶然通ってしまう。
    /// 書式を変えるときはこの契約も更新すること。
    /// </remarks>
    private static string FixtureListItem()
    {
        var lines = ClaudeMd.Split('\n').Select(line => line.TrimEnd('\r')).ToList();
        var start = lines.FindIndex(line =>
            line.StartsWith("- **テンプレート変更時は**", StringComparison.Ordinal)
        );
        start.Should().BeGreaterThanOrEqualTo(0, "固定フィクスチャ一覧の箇条が見つかること");

        var end = lines.FindIndex(
            start + 1,
            line => line.StartsWith("- ", StringComparison.Ordinal)
        );
        end.Should().BeGreaterThan(start, "箇条の終端（次の箇条）が見つかること");

        return string.Join('\n', lines.GetRange(start, end - start));
    }

    /// <summary>識別子を構成する文字（名前の境界判定に使う）</summary>
    private static bool IsWordCharacter(char c) => char.IsAsciiLetterOrDigit(c) || c == '_';

    /// <summary>
    /// <paramref name="text"/> の中に <paramref name="name"/> が<b>両側の境界つきで</b>現れるかを返す。
    /// </summary>
    /// <param name="text">探す対象（固定フィクスチャ一覧の箇条の本文）</param>
    /// <param name="name">探す名前（ファイル名またはフィクスチャ名・クラス名）</param>
    /// <param name="forbiddenSuffix">
    /// 非 null なら、直後がこの文字列である出現を一致とみなさない
    /// </param>
    /// <remarks>
    /// <para>
    /// 素の部分一致では使えない＝フィクスチャ名どうしが包含する
    /// （<c>SqlServerBinaryFixture.g.cs</c> ⊃ <c>BinaryFixture.g.cs</c>・
    /// <c>SqlitePortableFixture.g.cs</c> ⊃ <c>PortableFixture.g.cs</c>）。
    /// <b>左境界</b>がないと、別のフィクスチャの記載で本体の記載漏れが素通りする。
    /// </para>
    /// <para>
    /// <b>右境界</b>も要る＝基底名で照合するとき（下記）、本体の記載を消しても同じ箇条に残る
    /// <c>ComputedColumnFixtureDriftTests</c> や <c>BinaryFixtureDefinition.Build()</c> の記述で
    /// 一致してしまう。ドリフトテストのクラス名も、将来の接尾辞つきの名前を右境界で防ぐ。
    /// </para>
    /// </remarks>
    private static bool MentionsWithBoundaries(
        string text,
        string name,
        string? forbiddenSuffix = null
    )
    {
        for (var index = text.IndexOf(name, StringComparison.Ordinal); index >= 0; )
        {
            var after = index + name.Length;
            var leftOk = index == 0 || !IsWordCharacter(text[index - 1]);
            var rightOk = after >= text.Length || !IsWordCharacter(text[after]);
            var suffixOk =
                forbiddenSuffix is null
                || !text.AsSpan(after).StartsWith(forbiddenSuffix, StringComparison.Ordinal);

            if (leftOk && rightOk && suffixOk)
            {
                return true;
            }

            index = text.IndexOf(name, index + 1, StringComparison.Ordinal);
        }

        return false;
    }

    /// <summary>フィクスチャ生成物の置き場</summary>
    private static string GeneratedFixtureDirectory() =>
        Path.Combine(Root, "tests", "QuickER.Tests", "GeneratedFixture");

    /// <summary>
    /// <c>GeneratedFixture/</c> 直下の生成物（<c>*.g.cs</c>）が、すべて固定フィクスチャ一覧に載っていることを検証する。
    /// </summary>
    /// <remarks>
    /// <para>
    /// この一覧は再生成手順の正本で、フィクスチャを足した本人が CLAUDE.md を思い出さない限り追従しない
    /// （実際 2026-09-26 に <c>ComputedColumnFixture</c> を足したとき、一覧は 17 個のまま残った）。
    /// 依存図・フォルダ一覧と同じ流儀で、名前で突合する網を張る（件数は照合しない）。
    /// </para>
    /// <para>
    /// <b>本体の生成物は「ファイル名」か「<c>.g.cs</c> を外した名前」のどちらでも載っていればよい</b>＝
    /// 2 ファイル構成のフィクスチャ（<c>ConcurrencyFixture</c> / <c>RemoteServiceFixture</c> / <c>SyncFixture</c>）は
    /// 「本体＋<c>X.RemoteServer.g.cs</c> の 2 ファイル構成」と書く流儀のため、本体側がフィクスチャ名で呼ばれている。
    /// ただし基底名の一致は<b>直後が <c>.RemoteServer</c> でない出現に限る</b>＝サーバー側の記載だけで
    /// 本体の記載漏れを見逃さないため。<c>*.RemoteServer.g.cs</c> は 4 本ともフルのファイル名で書かれているので
    /// 照合に含める。
    /// </para>
    /// </remarks>
    [Fact(DisplayName = "CLAUDE.md の固定フィクスチャ一覧に、実在する生成物がすべて載っている")]
    public void FixtureListItem_NamesEveryGeneratedFixtureFile()
    {
        var item = FixtureListItem();

        var missing = Directory
            .GetFiles(GeneratedFixtureDirectory(), "*.g.cs")
            .Select(Path.GetFileName)
            .Where(fileName =>
                !MentionsWithBoundaries(item, fileName!)
                && !(
                    !fileName!.EndsWith(".RemoteServer.g.cs", StringComparison.Ordinal)
                    && MentionsWithBoundaries(
                        item,
                        fileName[..^".g.cs".Length],
                        forbiddenSuffix: ".RemoteServer"
                    )
                )
            )
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        missing
            .Should()
            .BeEmpty(
                "固定フィクスチャ一覧は再生成手順の正本なので、生成物を足したら同じ箇条へ書く"
                    + $"（載っていない生成物: [{string.Join(", ", missing)}]）"
            );
    }

    /// <summary>
    /// <c>GeneratedFixture/</c> 直下のドリフトテストが、すべて固定フィクスチャ一覧のドリフトテスト列挙に
    /// 載っていることを検証する。
    /// </summary>
    /// <remarks>
    /// 対象は <c>*FixtureDriftTests</c> だけ＝<c>RuntimePackageSourceDriftTests</c>（ランタイムパッケージ用ソース）と
    /// サンプルのドリフトテスト（<c>EcOrderSampleDriftTests</c> / <c>EcOrderRemoteSampleDriftTests</c>）は、
    /// 同じ箇条でも別の文で扱われているため照合しない。
    /// </remarks>
    [Fact(
        DisplayName = "CLAUDE.md の固定フィクスチャ一覧に、実在するドリフトテストがすべて載っている"
    )]
    public void FixtureListItem_NamesEveryFixtureDriftTest()
    {
        var item = FixtureListItem();

        var missing = Directory
            .GetFiles(GeneratedFixtureDirectory(), "*FixtureDriftTests.cs")
            .Select(path => Path.GetFileNameWithoutExtension(path))
            .Where(className => !MentionsWithBoundaries(item, className))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        missing
            .Should()
            .BeEmpty(
                "ドリフトテストの列挙も再生成手順の正本なので、足したら同じ箇条へ書く"
                    + $"（載っていないドリフトテスト: [{string.Join(", ", missing)}]）"
            );
    }
}
