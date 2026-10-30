using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using QuickER.Dialects;
using QuickER.Model;
using QuickER.Provider;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.Integration.Dialects.SyncPlanFuzz;

/// <summary>
/// 同期計画の来歴の行：DB の来歴（今の DDL・旧版の DDL）× 子の表名の長さ × 後から図に足した 2 本目の外部キーの全積から、
/// PostgreSQL の決定的な行を作る（網と実 PG の総当たりが同じ行を流す）
/// </summary>
/// <remarks>
/// <para>
/// 行の図は親の表 <c>hp1</c>・<c>hp2</c> と、<c>hp1</c> を名前の無い外部キーで参照する子の表（列 <c>p2_ref</c> も持つ）。
/// DB 側の図の名前の無い外部キーは、行の来歴（<see cref="SyncPlanHistory"/>）の規則で名付けてから DB を作り、目標の図ではその名前を
/// 外す（利用者の図は名前を付けていない）。2 本目の水準 <c>Added</c> は、目標の図で <c>p2_ref</c> から <c>hp2</c> へ名前の無い外部キーを
/// 足す（旧版の DB には 1 本だけあり、2 本目は後で図に足した＝来歴＋その後の編集）。
/// </para>
/// <para>
/// 4m-F1 は「旧版 × 長い子の表名 × 2 本目を足す」の 1 行：旧版の 2 本の既定名は同じ名前に切り詰められ、差分の吸収が 2 本目の追加にも
/// DB にある 1 本目と同じ名前を与え、既定で選ばれる追加が同じ名前で断られる。ほかの 7 行は合格の行で、網では静的な規則が失敗を予言せず、
/// 実 PG では既定の同期がコミットし、取り込み直した差分が空になる（<see cref="NetProblems"/>・<see cref="RealProblems"/>）。
/// 切り詰めは PostgreSQL だけの事実なので、ほかの方言の行は作らない（ほかの方言の旧版は長い名前の DDL を DB が断ったので、その来歴の
/// DB は無い）。
/// </para>
/// </remarks>
internal static class SyncPlanHistoryRows
{
    /// <summary>DB の来歴</summary>
    public const string History = "History";

    /// <summary>子の表名の長さ（Long＝外部キーの縮める前の既定名が方言の上限を超える）</summary>
    public const string ChildName = "ChildName";

    /// <summary>目標の図で足す 2 本目の外部キー</summary>
    public const string SecondForeignKey = "SecondForeignKey";

    /// <summary>行の Id の頭（合格側の行の <see cref="SyncPlanScenarios.PassPrefix"/> と別＝期待が逆）</summary>
    public const string IdPrefix = "history-";

    /// <summary>行が観測を書く図の形の頭（<see cref="FuzzSeedOutcome.Shapes"/>）</summary>
    public const string ShapePrefix = "history:";

    /// <summary>4m-F1 の指摘の名前</summary>
    public const string F1Finding = "4m-f1-legacy-fk-name";

    /// <summary>次元表</summary>
    public static DimensionTable Table { get; } =
        new(
            EngineIds.SyncPlan,
            [
                new Factor(
                    History,
                    [.. Enum.GetValues<SyncPlanHistory>().Select(h => new Level(h.ToString()))],
                    "DB の来歴（今の DDL・旧版の DDL）"
                ),
                new Factor(
                    ChildName,
                    [new Level("Short"), new Level("Long")],
                    "子の表名の長さ（Long＝外部キーの縮める前の既定名が方言の上限を超える）"
                ),
                new Factor(
                    SecondForeignKey,
                    [new Level("None"), new Level("Added")],
                    "目標の図で足す 2 本目の名前の無い外部キー"
                ),
            ],
            [new FactorGroup(History, ChildName, SecondForeignKey)]
        );

    /// <summary>台帳の行（来歴の行の次元表。行は同期計画の総当たりと能力の組の網の決定的な行として流れる）</summary>
    public static EngineDescriptor Descriptor { get; } =
        new(
            EngineIds.SyncPlan,
            "HistoryRows",
            "同期計画の来歴の行（DB の来歴 × 子の表名の長さ × 2 本目の外部キー）",
            Table,
            Cells.Count,
            [
                "tests/QuickER.Tests.Integration/Dialects/SyncPlanFuzz/SyncPlanHistoryRows.cs",
                "tests/QuickER.Tests.Integration/Dialects/SyncPlanFuzz/SyncPlanHistories.cs",
            ],
            ["src/QuickER.Provider/SchemaDiffService.cs", "src/QuickER.Model/ConstraintNames.cs"]
        );

    /// <summary>行の水準の全積</summary>
    private static IReadOnlyList<(SyncPlanHistory History, bool Long, bool Second)> Cells =>
        [
            .. Enum.GetValues<SyncPlanHistory>()
                .SelectMany(history =>
                    new[] { false, true }.SelectMany(isLong =>
                        new[] { false, true }.Select(second => (history, isLong, second))
                    )
                ),
        ];

    /// <summary>行の Id</summary>
    public static string IdOf(SyncPlanHistory history, bool isLong, bool second) =>
        $"{IdPrefix}{history}-{(isLong ? "long" : "short")}-{(second ? "added" : "none")}";

    /// <summary>4m-F1 の行の Id</summary>
    public static string F1Id => IdOf(SyncPlanHistory.PostgreSqlUnshortened, true, true);

    /// <summary>行</summary>
    public static IEnumerable<ScenarioRow<SyncPlanScenarioInput>> Rows =>
        Cells.Select(cell =>
        {
            var id = IdOf(cell.History, cell.Long, cell.Second);
            var child = cell.Long ? LongChildName : "h_child";

            return new ScenarioRow<SyncPlanScenarioInput>(
                id,
                id == F1Id ? F1Finding : null,
                $"来歴の行：{History}={cell.History} × {ChildName}={(cell.Long ? "Long" : "Short")} × {SecondForeignKey}={(cell.Second ? "Added" : "None")}",
                new SyncPlanScenarioInput(
                    DialectNames.PostgreSql,
                    fuzzDialect => new SyncPlanScenarioCase(
                        fuzzDialect,
                        live => Build(live, child),
                        target => Mutate(target, child, cell.Second),
                        null,
                        cell.History
                    )
                )
            );
        });

    /// <summary>
    /// 長い子の表名（60 文字＝<c>FK_{子}_hp1</c> と <c>FK_{子}_hp2</c> が 63 バイトの上限を超え、切り詰めると同じ名前になる）
    /// </summary>
    private static string LongChildName => "h_" + new string('x', 58);

    private static void Build(ScenarioLive live, string child)
    {
        var p1 = live.Table("hp1");
        live.Int(p1, "id", pk: true);
        var p2 = live.Table("hp2");
        live.Int(p2, "id", pk: true);
        var c = live.Table(child);
        live.Int(c, "id", pk: true);
        live.Ref(c, p1, "p1_ref", constraintName: null);
        live.Int(c, "p2_ref");
    }

    private static void Mutate(ScenarioTarget target, string child, bool second)
    {
        if (second)
        {
            target.AddForeignKey(child, "p2_ref", "hp2", name: null);
        }
    }

    /// <summary>行が実際に作った状態から水準を読む図の形（来歴・子の表名の長さ・2 本目）</summary>
    /// <param name="history">行の来歴</param>
    /// <param name="named">来歴で名付けた外部キー</param>
    internal static IEnumerable<string> LiveShapes(
        SyncPlanHistory history,
        IReadOnlyList<SyncPlanHistories.NamedForeignKey> named
    )
    {
        yield return $"{ShapePrefix}{History}={history}";

        // 長さは実際に作った外部キーの縮める前の既定名から読む（上限を超えるか）
        var isLong = named.Any(n => n.ExceedsLimit);
        yield return $"{ShapePrefix}{ChildName}={(isLong ? "Long" : "Short")}";
    }

    /// <summary>目標の図で 2 本目を足したかを読む図の形</summary>
    internal static string SecondShape(
        IReadOnlyList<Relationship> before,
        IReadOnlyList<Relationship> after
    ) => $"{ShapePrefix}{SecondForeignKey}={(after.Count > before.Count ? "Added" : "None")}";

    /// <summary>行の結果を次元表のケースとして数える（行が書いた図の形から水準を読む）</summary>
    /// <param name="counter">数える先</param>
    /// <param name="outcome">行の結果</param>
    public static void Observe(CoverageCounter counter, FuzzSeedOutcome outcome)
    {
        var assignment = outcome
            .Shapes.Where(s => s.StartsWith(ShapePrefix, StringComparison.Ordinal))
            .Select(s => s[ShapePrefix.Length..].Split('=', 2))
            .ToDictionary(p => p[0], p => p[1], StringComparer.Ordinal);
        counter.Observe(assignment);
    }

    /// <summary>
    /// 差分の表明（網と実 DB の共通）：図を変えない行は差分が無く（DB の来歴の名前を同じ外部キーとみなす）、2 本目を足す行は
    /// 外部キーの追加だけが 1 つ出る
    /// </summary>
    private static IEnumerable<string> DiffProblems(FuzzSeedOutcome outcome)
    {
        var added = outcome.Shapes.Contains($"{ShapePrefix}{SecondForeignKey}=Added");
        var expected = added ? 1 : 0;

        if (
            outcome.Selected.Count != expected
            || outcome.Unselected.Count > 0
            || (
                added
                && !outcome
                    .Selected[0]
                    .StartsWith($"{SchemaDiffKind.AddForeignKey}:", StringComparison.Ordinal)
            )
        )
        {
            yield return $"history row {outcome.ScenarioId}: expected {(added ? "only the added foreign key" : "no difference")}, got: "
                + string.Join(" / ", outcome.Selected.Concat(outcome.Unselected));
        }
    }

    /// <summary>来歴の行か</summary>
    public static bool IsHistoryRow(FuzzSeedOutcome outcome) =>
        outcome.ScenarioId?.StartsWith(IdPrefix, StringComparison.Ordinal) == true;

    /// <summary>
    /// 網の表明：差分が来歴どおり（<see cref="DiffProblems"/>）で、4m-F1 の行は静的な規則が失敗を予言し、合格の行は予言せず違反も無い
    /// （破れた行ごとに問題の文）
    /// </summary>
    /// <param name="outcomes">流した行の結果</param>
    public static IEnumerable<string> NetProblems(IEnumerable<FuzzSeedOutcome> outcomes)
    {
        foreach (var outcome in outcomes.Where(IsHistoryRow))
        {
            foreach (var problem in DiffProblems(outcome))
            {
                yield return problem;
            }

            var predicted = outcome.Notes.Contains(SyncPlanFuzzRunner.StaticRulePredictedNote);

            if (outcome.ScenarioId == F1Id)
            {
                if (!predicted)
                {
                    yield return $"history row {outcome.ScenarioId}: the static rules must predict the name collision";
                }

                continue;
            }

            if (predicted)
            {
                yield return $"history row {outcome.ScenarioId}: the static rules must not predict a failure";
            }

            if (outcome.Violations.Count > 0)
            {
                yield return $"history row {outcome.ScenarioId}: expected no violations, got: {string.Join(" / ", outcome.Violations)}";
            }
        }
    }

    /// <summary>
    /// 実 DB の表明：差分が来歴どおり（<see cref="DiffProblems"/>）で、合格の行は既定の同期がコミットし、取り込み直した差分が空
    /// （4m-F1 の行の失敗は既知の失敗の一覧が受け持つ）
    /// </summary>
    /// <param name="outcomes">流した行の結果</param>
    public static IEnumerable<string> RealProblems(IEnumerable<FuzzSeedOutcome> outcomes)
    {
        foreach (var outcome in outcomes.Where(IsHistoryRow))
        {
            foreach (var problem in DiffProblems(outcome))
            {
                yield return problem;
            }

            if (outcome.ScenarioId == F1Id)
            {
                continue;
            }

            if (!outcome.Committed)
            {
                yield return $"history row {outcome.ScenarioId}: the default sync must commit, got: {outcome.Error}";
                continue;
            }

            if (outcome.AfterDiff.Count > 0)
            {
                var detail = new StringBuilder();
                detail.AppendJoin(" / ", outcome.AfterDiff);
                yield return $"history row {outcome.ScenarioId}: the re-imported schema must match the diagram, got: {detail}";
            }
        }
    }
}
