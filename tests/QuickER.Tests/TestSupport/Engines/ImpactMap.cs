using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace QuickER.Tests.TestSupport.Engines;

/// <summary>守りの種類</summary>
public enum ProtectionKind
{
    /// <summary>エンジンの部分（名前は <c>{エンジンの ID}/{部分}</c>）</summary>
    Engine,

    /// <summary>単体の表（名前は <c>table:{名前}</c>）</summary>
    Table,

    /// <summary>ソースの規則（名前は <c>rule:{Id}</c>）</summary>
    Rule,

    /// <summary>照合の表の行（名前は <c>parity:{Id}</c>）</summary>
    Parity,

    /// <summary>英語の文言の表の行（名前は <c>english:{Id}</c>）</summary>
    English,

    /// <summary>必須の文言の表の行（名前は <c>phrase:{Id}</c>）</summary>
    Phrase,
}

/// <summary>影響の地図の守り（エンジンの部分・単体の表・規則・照合の行など）</summary>
/// <param name="Name">地図の名前（一意。形は <see cref="ProtectionKind"/> の各値の説明のとおり）</param>
/// <param name="Kind">種類</param>
/// <param name="Title">表示の名前（無ければ空）</param>
/// <param name="SourceFiles">テストのソース（リポジトリの根からの相対パス。無ければ空）</param>
/// <param name="Covers">
/// 守る src の範囲（リポジトリの根からの相対パス・ファイルかフォルダ）。空なら横断の守り（src 全体を走査する規則・src の外を読む行）で、
/// 単位の守りには数えない
/// </param>
/// <param name="Scope">横断の守りの読む範囲の表記（<paramref name="Covers"/> が空のときの報告用。無ければ空）</param>
public sealed record Protection(
    string Name,
    ProtectionKind Kind,
    string Title,
    IReadOnlyList<string> SourceFiles,
    IReadOnlyList<string> Covers,
    string Scope
);

/// <summary>守りの無い領域の分類</summary>
public enum UnprotectedKind
{
    /// <summary>エンジン・表で埋める候補（後の区切りの材料）</summary>
    Pending,

    /// <summary>エンジン・表で守る対象でないと判断したもの（理由に根拠を書く）</summary>
    Accepted,
}

/// <summary>理由つきの守りの無い領域（単位・分類・理由）</summary>
/// <param name="Unit">単位（<see cref="ImpactUnits.Enumerate"/> が返すパスのどれか）</param>
/// <param name="Kind">分類</param>
/// <param name="Reason">守りが無い理由（具体的に書く）</param>
public sealed record UnprotectedArea(string Unit, UnprotectedKind Kind, string Reason);

/// <summary>単位の守られ方</summary>
public enum UnitCoverage
{
    /// <summary>単位そのものか、その祖先が守りの範囲にある</summary>
    Whole,

    /// <summary>単位の中のファイル・フォルダだけが守りの範囲にある</summary>
    Partial,

    /// <summary>どの守りの範囲にも入らない</summary>
    None,
}

/// <summary>影響の地図の単位の行</summary>
/// <param name="Unit">単位</param>
/// <param name="Coverage">守られ方</param>
/// <param name="Protections">単位に当たる守りの名前（序数の順）</param>
/// <param name="Unprotected">宣言された守りの無い領域（無ければ null。プロジェクトごとの宣言を受け継いだ単位はプロジェクトの宣言）</param>
public sealed record ImpactUnitRow(
    string Unit,
    UnitCoverage Coverage,
    IReadOnlyList<string> Protections,
    UnprotectedArea? Unprotected
);

/// <summary>src の単位（地図と照合のガードの単位）の列挙</summary>
/// <remarks>
/// 単位は src のプロジェクト（<c>*.csproj</c> を持つ src の直下のフォルダ）と、その直下のフォルダのうち手で書いたソース
/// （<c>.cs</c>〔<c>*.Designer.cs</c>・<c>*.g.cs</c> を除く〕か <c>.scriban</c>）を下に持つもの。<c>Resources/</c>（生成の
/// Designer.cs と resx だけ）・<c>Themes/</c>（XAML だけ）は単位にしない。<c>bin</c> / <c>obj</c> は読まない。
/// src にファイルを足しただけでは単位は変わらない（変わるのはプロジェクト・直下のフォルダを足したとき）。
/// </remarks>
public static class ImpactUnits
{
    /// <summary>src のフォルダ</summary>
    public const string SourceRoot = "src";

    /// <summary>単位を序数の順に並べる（区切りは <c>/</c>・リポジトリの根からの相対パス）</summary>
    /// <param name="repositoryRoot">リポジトリの根</param>
    public static IReadOnlyList<string> Enumerate(string repositoryRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);

        var units = new List<string>();
        var src = Path.Combine(repositoryRoot, SourceRoot);

        foreach (var project in Directory.EnumerateDirectories(src))
        {
            if (!Directory.EnumerateFiles(project, "*.csproj").Any())
            {
                continue;
            }

            var projectUnit = $"{SourceRoot}/{Path.GetFileName(project)}";
            units.Add(projectUnit);

            foreach (var folder in Directory.EnumerateDirectories(project))
            {
                var name = Path.GetFileName(folder);

                if (name is "bin" or "obj" || !HasHandwrittenSource(folder))
                {
                    continue;
                }

                units.Add($"{projectUnit}/{name}");
            }
        }

        units.Sort(StringComparer.Ordinal);
        return units;
    }

    /// <summary>手で書いたソースのファイルか（地図の単位を決める）</summary>
    public static bool IsHandwrittenSource(string fileName) =>
        fileName.EndsWith(".scriban", StringComparison.OrdinalIgnoreCase)
        || (
            fileName.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
            && !fileName.EndsWith(".Designer.cs", StringComparison.OrdinalIgnoreCase)
            && !fileName.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase)
        );

    private static bool HasHandwrittenSource(string folder) =>
        Directory
            .EnumerateFiles(folder, "*", SearchOption.AllDirectories)
            .Where(path =>
                !path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    .Any(segment => segment is "bin" or "obj")
            )
            .Any(path => IsHandwrittenSource(Path.GetFileName(path)));
}

/// <summary>
/// 影響の地図：src の単位（プロジェクトと直下のフォルダ）ごとに、それを守るテストのエンジンの部分・単体の表・範囲を絞った規則を引く
/// </summary>
/// <remarks>
/// <para>
/// 守りの範囲（<see cref="Protection.Covers"/>）は宣言（エンジンの台帳・単体の表の台帳・規則の範囲）から作り、手で書いた対応表を
/// 正本にしない。範囲が単位そのものか祖先なら「全体」、単位の中なら「一部」。
/// </para>
/// <para>
/// 生成物の Markdown（<see cref="Render"/>）は数（シナリオ・ファイル・テストの数）を入れない（行やファイルを足すたびに地図が変わらないように）。
/// 変異テストの結果を領域ごとにまとめるときは <see cref="UnitOf"/> と <see cref="ProtectionsOf"/> で引く。
/// </para>
/// </remarks>
public sealed class ImpactMap
{
    /// <summary>地図を作る</summary>
    /// <param name="units">単位（<see cref="ImpactUnits.Enumerate"/>）</param>
    /// <param name="protections">守り</param>
    /// <param name="unprotected">理由つきの守りの無い領域</param>
    public ImpactMap(
        IReadOnlyList<string> units,
        IReadOnlyList<Protection> protections,
        IReadOnlyList<UnprotectedArea> unprotected
    )
    {
        ArgumentNullException.ThrowIfNull(units);
        ArgumentNullException.ThrowIfNull(protections);
        ArgumentNullException.ThrowIfNull(unprotected);

        Units = units.OrderBy(unit => unit, StringComparer.Ordinal).ToList();
        Protections = protections;
        Unprotected = unprotected;
        Rows = Units.Select(BuildRow).ToList();
    }

    /// <summary>単位（序数の順）</summary>
    public IReadOnlyList<string> Units { get; }

    /// <summary>守り（渡した順）</summary>
    public IReadOnlyList<Protection> Protections { get; }

    /// <summary>理由つきの守りの無い領域（渡した順）</summary>
    public IReadOnlyList<UnprotectedArea> Unprotected { get; }

    /// <summary>単位の行（単位の順）</summary>
    public IReadOnlyList<ImpactUnitRow> Rows { get; }

    /// <summary>パス（区切りは <c>/</c>・リポジトリの根からの相対）が属する、いちばん深い単位（無ければ null）</summary>
    public string? UnitOf(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        return Units
            .Where(unit => IsSameOrUnder(path, unit))
            .OrderByDescending(unit => unit.Length)
            .FirstOrDefault();
    }

    /// <summary>パスを範囲に含む守り（範囲がパスそのものか祖先のもの。渡した順）</summary>
    public IReadOnlyList<Protection> ProtectionsOf(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        return Protections
            .Where(protection => protection.Covers.Any(cover => IsSameOrUnder(path, cover)))
            .ToList();
    }

    /// <summary>パスが基準のパスそのものか、その下にあるか（区切りは <c>/</c>・序数で比べる）</summary>
    public static bool IsSameOrUnder(string path, string basePath) =>
        string.Equals(path, basePath, StringComparison.Ordinal)
        || path.StartsWith(basePath + "/", StringComparison.Ordinal);

    /// <summary>単位の守られ方（範囲の和から）</summary>
    public static UnitCoverage CoverageOf(string unit, IEnumerable<string> covers)
    {
        var coverage = UnitCoverage.None;

        foreach (var cover in covers)
        {
            if (IsSameOrUnder(unit, cover))
            {
                return UnitCoverage.Whole;
            }

            if (cover.StartsWith(unit + "/", StringComparison.Ordinal))
            {
                coverage = UnitCoverage.Partial;
            }
        }

        return coverage;
    }

    /// <summary>単位の上のプロジェクトの単位（単位がプロジェクトならそれ自身）</summary>
    public static string ProjectOf(string unit)
    {
        var segments = unit.Split('/');

        return segments.Length <= 2 ? unit : $"{segments[0]}/{segments[1]}";
    }

    private ImpactUnitRow BuildRow(string unit)
    {
        var touching = Protections
            .Where(protection => CoverageOf(unit, protection.Covers) != UnitCoverage.None)
            .Select(protection => protection.Name)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();
        var coverage = CoverageOf(unit, Protections.SelectMany(protection => protection.Covers));
        var project = ProjectOf(unit);
        var declared =
            Unprotected.FirstOrDefault(area => area.Unit == unit)
            ?? Unprotected.FirstOrDefault(area => area.Unit == project);

        return new ImpactUnitRow(unit, coverage, touching, declared);
    }

    /// <summary>生成物の Markdown（改行は LF）</summary>
    /// <param name="regenerateSteps">書き直しの手順（見出しの下の注記に書く）</param>
    /// <param name="handwrittenTests">守りの無い領域の単位 → エンジン・表の外の手書きのテストがあるフォルダ（無ければ含めない）</param>
    public string Render(
        string regenerateSteps,
        IReadOnlyDictionary<string, string> handwrittenTests
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(regenerateSteps);
        ArgumentNullException.ThrowIfNull(handwrittenTests);

        var text = new StringBuilder();
        var line = (string value) => text.Append(value).Append('\n');

        line("# 影響の地図");
        line("");
        line(
            $"<!-- 生成物（ImpactMapDriftTests が宣言から作る）。手で直さない。書き直し（リポジトリ直下・PowerShell）：{regenerateSteps} -->"
        );
        line("");
        line(
            "src の単位（プロジェクトと、その直下のフォルダ）ごとに、それを守るテストのエンジンの部分・単体の表・範囲を絞った規則を並べる。"
        );
        line(
            "出どころはエンジンの台帳（EngineRegistry の Covers）・単体の表の台帳（TableRegistry）・規則の範囲（SourceRules の Scope）・Integration の台帳の写し（protection-registry.json）。"
        );
        line("");
        line("| 名前の形 | 守りの種類 |");
        line("|---|---|");
        line("| `{エンジンの ID}/{部分}` | エンジンの部分 |");
        line("| `table:{名前}` | 単体の表 |");
        line("| `rule:{Id}` | ソースの規則 |");
        line("| `parity:{Id}` | 照合の表の行 |");
        line("| `english:{Id}` | 英語の文言の表の行 |");
        line("| `phrase:{Id}` | 必須の文言の表の行 |");
        line("");

        line("## 単位の表");
        line("");
        line("| 単位 | 状態 | 守り |");
        line("|---|---|---|");

        foreach (var row in Rows)
        {
            line($"| `{row.Unit}` | {StateLabel(row)} | {Names(row.Protections)} |");
        }

        line("");
        line("## 範囲の表");
        line("");
        line(
            "守りの範囲（宣言されたパス）ごとの守り。パスの属する範囲は、パスそのものか祖先の行。"
        );
        line("");
        line("| 範囲 | 守り |");
        line("|---|---|");

        var covers = Protections
            .SelectMany(protection =>
                protection.Covers.Select(cover => (Cover: cover, protection.Name))
            )
            .GroupBy(pair => pair.Cover, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal);

        foreach (var group in covers)
        {
            var names = group
                .Select(pair => pair.Name)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToList();
            line($"| `{group.Key}` | {Names(names)} |");
        }

        line("");
        line("## 守りの一覧");
        line("");
        line("範囲を持つ守り（エンジンの部分・単体の表・範囲を絞った規則）。");

        foreach (var protection in Protections.Where(protection => protection.Covers.Count > 0))
        {
            line("");
            line($"### `{protection.Name}`");
            line("");

            if (protection.Title.Length > 0)
            {
                line($"- 名前：{protection.Title}");
            }

            if (protection.SourceFiles.Count > 0)
            {
                line($"- テストのソース：{Paths(protection.SourceFiles)}");
            }

            line($"- 範囲：{Paths(protection.Covers)}");

            if (protection.Scope.Length > 0)
            {
                line($"- 単位の守りに数えない範囲：{Cell(protection.Scope)}");
            }
        }

        line("");
        line("## 横断の守り");
        line("");
        line(
            "src 全体か src の外を読む守り。単位の守りには数えない（数えると全単位が守りありになる）。"
        );
        line("");
        line("| 名前 | 読む範囲 |");
        line("|---|---|");

        foreach (var protection in Protections.Where(protection => protection.Covers.Count == 0))
        {
            line($"| `{protection.Name}` | {Cell(protection.Scope)} |");
        }

        line("");
        line("## 守りの無い領域");
        line("");
        line(
            "どの守りの範囲にも入らない単位と理由。Pending はエンジン・表で埋める候補、Accepted はエンジン・表で守る対象でないと判断したもの。"
        );
        line("");
        line("| 単位 | 分類 | エンジン・表の外のテスト | 理由 |");
        line("|---|---|---|---|");

        foreach (var area in Unprotected.OrderBy(area => area.Unit, StringComparer.Ordinal))
        {
            var tests = handwrittenTests.TryGetValue(area.Unit, out var folder)
                ? $"単体の手書きのテストだけ（`{folder}`）"
                : "無い";
            line($"| `{area.Unit}` | {area.Kind} | {tests} | {Cell(area.Reason)} |");
        }

        return text.ToString();
    }

    private static string StateLabel(ImpactUnitRow row) =>
        row.Unprotected is { } area
            ? area.Unit == row.Unit
                ? $"守りの無い領域（{area.Kind}）"
                : $"守りの無い領域（{area.Kind}・プロジェクトごと）"
            : row.Coverage switch
            {
                UnitCoverage.Whole => "全体",
                UnitCoverage.Partial => "一部",
                _ => "宣言が無い",
            };

    private static string Names(IReadOnlyList<string> names) =>
        names.Count == 0 ? "（なし）" : string.Join("・", names.Select(name => $"`{name}`"));

    private static string Paths(IReadOnlyList<string> paths) =>
        string.Join("・", paths.Select(path => $"`{path}`"));

    private static string Cell(string value) =>
        value.Length == 0
            ? "（なし）"
            : value.Replace("|", "\\|", StringComparison.Ordinal).Replace('\n', ' ');
}
