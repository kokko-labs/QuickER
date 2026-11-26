using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace QuickER.Tests.TestSupport.Engines;

/// <summary>領域の標準の軸の 1 つ</summary>
/// <param name="Id">軸の Id（領域の中で一意・小文字とハイフン）</param>
/// <param name="Name">軸の名前</param>
/// <param name="Question">修正のたびに答える問い（どんな隣の場合があるか）</param>
/// <param name="RequiresDeclarationCheck">
/// 交差させると答えるとき、水準の選び方の点検（直した分岐が読む方言の宣言ごとに、真の方言と偽の方言で答えが変わる水準があるか）を
/// <see cref="AxisAnswer.Declarations"/> に書かせる軸か
/// </param>
public sealed record StandardAxis(
    string Id,
    string Name,
    string Question,
    bool RequiresDeclarationCheck = false
);

/// <summary>領域の標準の軸の一覧</summary>
/// <param name="Id">領域の Id（台帳の項目の <see cref="BranchAxisItem.Area"/> が指す）</param>
/// <param name="Name">領域の名前</param>
/// <param name="Axes">軸（台帳の項目は全部に答える）</param>
public sealed record StandardAxisArea(string Id, string Name, IReadOnlyList<StandardAxis> Axes);

/// <summary>標準の軸への答え</summary>
/// <param name="Axis">軸の Id</param>
/// <param name="Crosses">交差させるか（偽なら関係ない）</param>
/// <param name="Reason">交差させる水準か、関係ない理由</param>
/// <param name="Declarations">
/// 水準の選び方の点検（<see cref="StandardAxis.RequiresDeclarationCheck"/> の軸で交差させるとき 1 つ以上。
/// 「宣言のメンバー名：真の方言と偽の方言で答えが変わる水準」の形の文）
/// </param>
public sealed record AxisAnswer(
    string Axis,
    bool Crosses,
    string Reason,
    IReadOnlyList<string>? Declarations = null
)
{
    /// <summary>交差させる</summary>
    /// <param name="axis">軸の Id</param>
    /// <param name="levels">交差させる水準</param>
    /// <param name="declarations">水準の選び方の点検（方言の宣言の軸だけ）</param>
    public static AxisAnswer Cross(string axis, string levels, params string[] declarations) =>
        new(axis, true, levels, declarations);

    /// <summary>関係ない</summary>
    /// <param name="axis">軸の Id</param>
    /// <param name="reason">関係ない理由</param>
    public static AxisAnswer Irrelevant(string axis, string reason) => new(axis, false, reason);
}

/// <summary>台帳の項目の状態</summary>
public enum BranchAxisStatus
{
    /// <summary>予定（因子の名前を文で持つ・表に未結び）</summary>
    Planned,

    /// <summary>結び済み・未修正（生成の表に水準を足したが、製品はまだ直していない）</summary>
    BoundUnfixed,

    /// <summary>結び済み・修正済み（指摘のセルが必ず含む行にあり、観測され、分岐に入った証拠が出る）</summary>
    BoundFixed,
}

/// <summary>予定の項目の結び（文で持つ）</summary>
/// <param name="Table">生成の表（作る区切りを添える）</param>
/// <param name="Levels">入力の因子と水準</param>
/// <param name="Cell">指摘のセル（範囲つき）</param>
/// <param name="Evidence">分岐に入った証拠</param>
public sealed record PlannedBinding(string Table, string Levels, string Cell, string Evidence);

/// <summary>入力の因子の水準</summary>
/// <param name="Factor">因子の名前</param>
/// <param name="Level">水準の名前</param>
public sealed record LevelRef(string Factor, string Level);

/// <summary>項目の水準が出ない方言・実装先</summary>
/// <param name="Target">方言・実装先の名前</param>
/// <param name="Reason">出ない理由</param>
/// <param name="Basis">根拠（リポジトリの根からの相対パスと「見出し」、か宣言のメンバー名 <c>型.メンバー</c>）</param>
public sealed record AbsentTarget(string Target, string Reason, string Basis);

/// <summary>結び済みの項目の結び（構造で持つ）</summary>
/// <param name="TablePart">生成の表＝台帳の部分の名前（<see cref="ProtectionNames.EnginePart"/> の形。例 <c>SyncPlan/ShapeRows</c>）</param>
/// <param name="Levels">入力の因子と水準（表の次元表にあるもの）</param>
/// <param name="CellRow">指摘のセル＝生成の表の必ず含む行（<see cref="RequiredRow.Id"/>）</param>
/// <param name="CellTargets">指摘のセルの範囲（方言・実装先）</param>
/// <param name="Evidence">分岐に入った証拠の名前（判定の関数は生成の表の側に置き、名前で引く）</param>
/// <param name="AbsentTargets">項目の水準が出ない方言・実装先と理由・根拠</param>
public sealed record BranchBinding(
    string TablePart,
    IReadOnlyList<LevelRef> Levels,
    string CellRow,
    IReadOnlyList<string> CellTargets,
    string Evidence,
    IReadOnlyList<AbsentTarget> AbsentTargets
);

/// <summary>分岐の軸の台帳の 1 項目（直し残しの指摘 1 つ）</summary>
/// <param name="Id">項目の Id（小文字・数字・ハイフン。新しい行の札 <see cref="RowTag.Ledger"/> が指す）</param>
/// <param name="Finding">指摘（何が直し残しか）</param>
/// <param name="Area">領域の標準の軸の一覧の Id</param>
/// <param name="Status">状態</param>
/// <param name="Plan">予定の結び（予定の項目だけ）</param>
/// <param name="Binding">結び済みの結び（結び済みの項目だけ）</param>
/// <param name="Judge">
/// 判定役（結び済みの項目は CLAUDE.md の「ガード：」と同じ書き方＝地図の名前・テストの名前。予定の項目は文。修正済みは必須）
/// </param>
/// <param name="Answers">標準の軸への答え（領域の全部の軸にちょうど 1 つ）</param>
/// <param name="FixedIn">直す区切り（予定と結び済み・未修正）</param>
public sealed record BranchAxisItem(
    string Id,
    string Finding,
    string Area,
    BranchAxisStatus Status,
    PlannedBinding? Plan,
    BranchBinding? Binding,
    string Judge,
    IReadOnlyList<AxisAnswer> Answers,
    string? FixedIn
);

/// <summary>
/// 分岐の軸の台帳の照合（構造・状態・標準の軸への答え・根拠の形）。宣言は純粋なデータで、生成の表に依る照合（証拠・セルの観測・
/// 水準が出る方言）と判定役の実在は呼び出し側が行う
/// </summary>
public static class BranchAxisLedgerChecks
{
    /// <summary>台帳の宣言のファイルの名前（CLAUDE.md の照合が、行 Id の照合の本文から外す）</summary>
    public const string DeclarationFileName = "BranchAxisLedgerEntries.cs";

    private static readonly Regex IdPattern = new(
        "^[a-z0-9]+(?:-[a-z0-9]+)*$",
        RegexOptions.CultureInvariant
    );

    private static readonly Regex EnginePartPattern = new(
        "^[A-Za-z]+/[A-Z][A-Za-z0-9]*$",
        RegexOptions.CultureInvariant
    );

    /// <summary>根拠の形：相対パスと「見出し」</summary>
    private static readonly Regex HeadingBasisPattern = new(
        "^(?<path>[^「」\\s]+)「(?<heading>[^「」]+)」$",
        RegexOptions.CultureInvariant
    );

    /// <summary>根拠の形：宣言のメンバー名（型.メンバー）</summary>
    private static readonly Regex MemberBasisPattern = new(
        "^(?<type>[A-Za-z_][A-Za-z0-9_]*)\\.(?<member>[A-Za-z_][A-Za-z0-9_]*)$",
        RegexOptions.CultureInvariant
    );

    /// <summary>ファイルの名前が台帳の宣言のファイルか</summary>
    /// <param name="fileName">ファイルの名前（パスを含まない）</param>
    public static bool IsDeclarationFileName(string fileName) =>
        string.Equals(fileName, DeclarationFileName, StringComparison.Ordinal);

    /// <summary>台帳と標準の軸の一覧の構造の問題をすべて並べる</summary>
    /// <param name="items">台帳の項目</param>
    /// <param name="areas">領域の標準の軸の一覧</param>
    public static IReadOnlyList<string> Problems(
        IReadOnlyList<BranchAxisItem> items,
        IReadOnlyList<StandardAxisArea> areas
    )
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(areas);

        var problems = new List<string>();
        var areaById = new Dictionary<string, StandardAxisArea>(StringComparer.Ordinal);

        foreach (var area in areas)
        {
            if (area is null || !IdPattern.IsMatch(area.Id ?? ""))
            {
                problems.Add($"領域の Id の形でない {area?.Id}");
                continue;
            }

            if (!areaById.TryAdd(area.Id!, area))
            {
                problems.Add($"領域 {area.Id} が重複している");
            }

            if (area.Axes is null || area.Axes.Count == 0)
            {
                problems.Add($"領域 {area.Id} に軸が無い");
                continue;
            }

            foreach (
                var duplicate in area
                    .Axes.GroupBy(axis => axis.Id, StringComparer.Ordinal)
                    .Where(group => group.Count() > 1)
            )
            {
                problems.Add($"領域 {area.Id} の軸 {duplicate.Key} が重複している");
            }

            problems.AddRange(
                area.Axes.Where(axis =>
                        !IdPattern.IsMatch(axis.Id ?? "")
                        || string.IsNullOrWhiteSpace(axis.Name)
                        || string.IsNullOrWhiteSpace(axis.Question)
                    )
                    .Select(axis =>
                        $"領域 {area.Id} の軸 {axis.Id} の Id・名前・問いが整っていない"
                    )
            );
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);

        foreach (var item in items)
        {
            if (item is null)
            {
                problems.Add("null の項目がある");
                continue;
            }

            var name = $"項目 {item.Id}";

            if (!IdPattern.IsMatch(item.Id ?? ""))
            {
                problems.Add($"{name}: Id の形でない（小文字・数字・ハイフン）");
            }
            else if (!ids.Add(item.Id!))
            {
                problems.Add($"{name}: Id が重複している");
            }

            if (string.IsNullOrWhiteSpace(item.Finding))
            {
                problems.Add($"{name}: 指摘が空");
            }

            problems.AddRange(StatusProblems(item).Select(problem => $"{name}: {problem}"));

            if (!areaById.TryGetValue(item.Area ?? "", out var itemArea))
            {
                problems.Add($"{name}: 知らない領域 {item.Area}");
                continue;
            }

            problems.AddRange(
                AnswerProblems(item, itemArea).Select(problem => $"{name}: {problem}")
            );
        }

        return problems;
    }

    /// <summary>結び済みの項目の、水準が出ない方言・実装先の根拠が実在しない問題を並べる</summary>
    /// <param name="items">台帳の項目</param>
    /// <param name="basisExists">根拠（<see cref="AbsentTarget.Basis"/>）が実在するか</param>
    public static IReadOnlyList<string> BasisProblems(
        IReadOnlyList<BranchAxisItem> items,
        Func<string, bool> basisExists
    )
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(basisExists);

        return
        [
            .. items
                .Where(item => item?.Binding?.AbsentTargets is not null)
                .SelectMany(item =>
                    item.Binding!.AbsentTargets.Where(absent =>
                            absent is not null
                            && IsBasisForm(absent.Basis)
                            && !basisExists(absent.Basis)
                        )
                        .Select(absent =>
                            $"項目 {item.Id}: {absent.Target} の根拠 {absent.Basis} が実在しない"
                        )
                ),
        ];
    }

    /// <summary>根拠が「相対パス「見出し」」か「型.メンバー」の形か</summary>
    /// <param name="basis">根拠</param>
    public static bool IsBasisForm(string? basis) =>
        basis is not null
        && (HeadingBasisPattern.IsMatch(basis) || MemberBasisPattern.IsMatch(basis));

    /// <summary>根拠を読む（相対パスと見出しか、型とメンバー。どちらでもなければ null の組）</summary>
    /// <param name="basis">根拠</param>
    public static (string? Path, string? Heading, string? Type, string? Member) ParseBasis(
        string basis
    )
    {
        ArgumentNullException.ThrowIfNull(basis);
        var heading = HeadingBasisPattern.Match(basis);

        if (heading.Success)
        {
            return (heading.Groups["path"].Value, heading.Groups["heading"].Value, null, null);
        }

        var member = MemberBasisPattern.Match(basis);

        return member.Success
            ? (null, null, member.Groups["type"].Value, member.Groups["member"].Value)
            : (null, null, null, null);
    }

    /// <summary>リポジトリの相対パスのファイルに、Markdown の見出しの行（# の後ろがちょうど見出し）があるか</summary>
    /// <param name="repositoryRoot">リポジトリの根</param>
    /// <param name="path">相対パス</param>
    /// <param name="heading">見出し</param>
    public static bool HeadingExists(string repositoryRoot, string path, string heading)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        var full = Path.Combine(repositoryRoot, path);

        return File.Exists(full)
            && File.ReadLines(full)
                .Any(line =>
                    line.StartsWith('#')
                    && string.Equals(line.TrimStart('#').Trim(), heading, StringComparison.Ordinal)
                );
    }

    private static IEnumerable<string> StatusProblems(BranchAxisItem item)
    {
        if (!Enum.IsDefined(item.Status))
        {
            yield return $"知らない状態 {item.Status}";
            yield break;
        }

        if (item.Status == BranchAxisStatus.Planned)
        {
            if (item.Plan is null)
            {
                yield return "予定の項目に予定の結び（表・因子と水準・指摘のセル・証拠の文）が無い";
            }
            else if (
                new[] { item.Plan.Table, item.Plan.Levels, item.Plan.Cell, item.Plan.Evidence }.Any(
                    string.IsNullOrWhiteSpace
                )
            )
            {
                yield return "予定の結びに空の欄がある";
            }

            if (item.Binding is not null)
            {
                yield return "予定の項目が結び済みの結びを持つ（結んだら状態を結び済みにする）";
            }
        }
        else
        {
            if (item.Binding is null)
            {
                yield return $"{item.Status} の項目に結び済みの結び（表・水準・指摘のセル・証拠）が無い（予定の項目は結ぶまで結び済みにできない）";
            }
            else
            {
                foreach (var problem in BindingProblems(item.Binding))
                {
                    yield return problem;
                }
            }

            if (item.Plan is not null)
            {
                yield return "結び済みの項目が予定の結びを持つ（結んだら予定の結びを消す）";
            }
        }

        if (item.Status == BranchAxisStatus.BoundFixed)
        {
            if (string.IsNullOrWhiteSpace(item.Judge))
            {
                yield return "修正済みの項目に判定役が無い（判定役のある結び済みの項目だけを修正済みにできる）";
            }

            if (item.FixedIn is not null)
            {
                yield return "修正済みの項目が直す区切りを持つ";
            }
        }
        else if (string.IsNullOrWhiteSpace(item.FixedIn))
        {
            yield return "直す区切りが無い";
        }

        if (item.Status == BranchAxisStatus.Planned && string.IsNullOrWhiteSpace(item.Judge))
        {
            yield return "予定の項目に判定役の文が無い";
        }
    }

    private static IEnumerable<string> BindingProblems(BranchBinding binding)
    {
        if (!EnginePartPattern.IsMatch(binding.TablePart ?? ""))
        {
            yield return $"生成の表 {binding.TablePart} が台帳の部分の名前の形でない";
        }

        if (binding.Levels is null || binding.Levels.Count == 0)
        {
            yield return "入力の水準が無い";
        }
        else
        {
            foreach (
                var level in binding.Levels.Where(level =>
                    string.IsNullOrWhiteSpace(level?.Factor)
                    || string.IsNullOrWhiteSpace(level.Level)
                )
            )
            {
                yield return "入力の水準に空の因子か水準がある";
            }

            foreach (
                var duplicate in binding
                    .Levels.Where(level => level is not null)
                    .GroupBy(level => level.Factor, StringComparer.Ordinal)
                    .Where(group => group.Count() > 1)
            )
            {
                yield return $"入力の因子 {duplicate.Key} が 2 回ある";
            }
        }

        if (string.IsNullOrWhiteSpace(binding.CellRow))
        {
            yield return "指摘のセルの行が空";
        }

        if (
            binding.CellTargets is null
            || binding.CellTargets.Count == 0
            || binding.CellTargets.Any(string.IsNullOrWhiteSpace)
        )
        {
            yield return "指摘のセルの範囲（方言・実装先）が無い";
        }

        if (string.IsNullOrWhiteSpace(binding.Evidence))
        {
            yield return "分岐に入った証拠の名前が空";
        }

        foreach (var absent in binding.AbsentTargets ?? [])
        {
            if (absent is null || string.IsNullOrWhiteSpace(absent.Target))
            {
                yield return "水準が出ない方言・実装先の名前が空";
                continue;
            }

            if (
                string.IsNullOrWhiteSpace(absent.Reason)
                || FactorInventoryGuard.IsVagueReason(absent.Reason.Trim())
            )
            {
                yield return $"水準が出ない {absent.Target} の理由が具体的でない（{absent.Reason}）";
            }

            if (!IsBasisForm(absent.Basis))
            {
                yield return $"水準が出ない {absent.Target} の根拠 {absent.Basis} が「相対パス「見出し」」か「型.メンバー」の形でない";
            }

            if (binding.CellTargets?.Contains(absent.Target, StringComparer.Ordinal) == true)
            {
                yield return $"指摘のセルの範囲の {absent.Target} を水準が出ないと書いている";
            }
        }
    }

    private static IEnumerable<string> AnswerProblems(BranchAxisItem item, StandardAxisArea area)
    {
        var answers = item.Answers ?? [];
        var axes = area.Axes.ToDictionary(axis => axis.Id, StringComparer.Ordinal);

        foreach (var answer in answers.Where(answer => answer is not null))
        {
            if (!axes.ContainsKey(answer.Axis ?? ""))
            {
                yield return $"領域 {area.Id} に無い軸 {answer.Axis} に答えている";
            }
        }

        foreach (
            var duplicate in answers
                .Where(answer => answer is not null)
                .GroupBy(answer => answer.Axis, StringComparer.Ordinal)
                .Where(group => group.Count() > 1)
        )
        {
            yield return $"軸 {duplicate.Key} に 2 回答えている";
        }

        foreach (var axis in area.Axes)
        {
            var answer = answers.FirstOrDefault(answer =>
                string.Equals(answer?.Axis, axis.Id, StringComparison.Ordinal)
            );

            if (answer is null)
            {
                yield return $"標準の軸 {axis.Id}（{axis.Name}）に答えていない（交差させるか、関係ない理由を書く）";
                continue;
            }

            if (string.IsNullOrWhiteSpace(answer.Reason))
            {
                yield return answer.Crosses
                    ? $"軸 {axis.Id} の交差させる水準が空"
                    : $"軸 {axis.Id} の関係ない理由が空";
            }
            else if (!answer.Crosses && FactorInventoryGuard.IsVagueReason(answer.Reason.Trim()))
            {
                yield return $"軸 {axis.Id} の関係ない理由が具体的でない（{answer.Reason}）";
            }

            if (
                axis.RequiresDeclarationCheck
                && answer.Crosses
                && (
                    answer.Declarations is null
                    || answer.Declarations.Count == 0
                    || answer.Declarations.Any(string.IsNullOrWhiteSpace)
                )
            )
            {
                yield return $"軸 {axis.Id} に交差させると答えたのに、水準の選び方の点検（読む方言の宣言ごとに真と偽の方言で答えが変わる水準）が無い";
            }
        }
    }
}
