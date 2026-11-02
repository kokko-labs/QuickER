using System.Collections.Generic;
using QuickER.Dialects;
using QuickER.Provider;
using QuickER.Tests.TestSupport.Engines;
using C = QuickER.Tests.Integration.Dialects.SyncPlanFuzz.SyncPlanExpectationCapabilities;
using I = QuickER.Tests.Integration.Dialects.SyncPlanFuzz.ExpectationItems;
using S = QuickER.Tests.Integration.Dialects.SyncPlanFuzz.ExpectationSchema;

namespace QuickER.Tests.Integration.Dialects.SyncPlanFuzz;

/// <summary>
/// 計画の段の期待の行（列順の変更＝ReorderColumns：再構築の方言の並べ替え・ネイティブの方言の最小の移動・列順を持たない方言）
/// </summary>
internal static partial class SyncPlanExpectationRows
{
    private static IEnumerable<ScenarioRow<SyncPlanExpectationInput>> PlanReorderRows() =>
        [
            Row(
                    "plan-reorder-rebuild-produces-target-ordered-rebuild",
                    "SQLite: ReorderColumns 単独選択で target 順の再構築になる",
                    DialectNames.Sqlite
                )
                .Live(s =>
                    s.Tbl(
                        "t",
                        ("id", "INT", true),
                        ("a", "INT", false),
                        ("b", "INT", false),
                        ("c", "INT", false)
                    )
                )
                .Items(_ => [I.Reorder("t", S.DetachedInts("t", "id", "c", "a", "b"))])
                // 再構築の方言はネイティブの並べ替えの計画を作らない
                .Expect(e =>
                    e.Sections.Empty()
                        .Reorders.Empty()
                        .Rebuilds.Count("* cols *", 1)
                        .Rebuilds.Single(
                            "t rebuild",
                            "t cols id:INT notnull pk live, c:INT null live, a:INT null live, b:INT null live"
                        )
                ),
            Row(
                    "plan-reorder-rebuild-leftover-column-goes-to-end",
                    "SQLite: 未選択 Drop の残存列は並べ替え後の末尾へ回る",
                    DialectNames.Sqlite
                )
                .Live(s =>
                    s.Tbl(
                        "t",
                        ("id", "INT", true),
                        ("a", "INT", false),
                        ("b", "INT", false),
                        ("leftover", "INT", false)
                    )
                )
                // 目標には leftover が無い（＝その DropColumn は未選択のまま渡さない）
                .Items(_ => [I.Reorder("t", S.DetachedInts("t", "id", "b", "a"))])
                // 目標の列（id,b,a）を目標の順に並べ、目標に無い leftover は元の相対順のまま末尾へ
                .Expect(e =>
                    e.Rebuilds.Count("* cols *", 1)
                        .Rebuilds.Single(
                            "t cols id:INT notnull pk live, b:INT null live, a:INT null live, leftover:INT null live"
                        )
                ),
            Row(
                    "plan-reorder-native-minimal-move-with-after",
                    "MySQL: 単純な並べ替えは最小 1 移動＋AFTER 指定になる",
                    DialectNames.MySql
                )
                // live: id,a,b,c → 目標: id,c,a,b（c を id の直後へ 1 回動かせば済む＝最長増加部分列は id,a,b）
                .Live(s => s.Add(S.DetachedInts("t", "id", "a", "b", "c")))
                .Items(_ => [I.Reorder("t", S.DetachedInts("t", "id", "c", "a", "b"))])
                .Expect(e =>
                    e.Rebuilds.Empty()
                        .Reorders.Exact(
                            "t move c:INT null after id",
                            "t source #0 ReorderColumns t"
                        )
                ),
            Row(
                    "plan-reorder-native-move-to-front-is-first",
                    "MySQL: 先頭へ動かす列は FIRST（AfterColumn=null）",
                    DialectNames.MySql
                )
                // live: a,b,c → 目標: c,a,b（c を先頭へ）
                .Live(s => s.Add(S.DetachedInts("t", "a", "b", "c")))
                .Items(_ => [I.Reorder("t", S.DetachedInts("t", "c", "a", "b"))])
                .Expect(e =>
                    e.Reorders.Exact("t move c:INT null after ^", "t source #0 ReorderColumns t")
                ),
            Row(
                    "plan-reorder-native-moved-column-uses-live-definition-when-alter-unselected",
                    "MySQL: 移動列の定義は未選択 Alter を無視し live 定義になる",
                    DialectNames.MySql
                )
                // live: id,a,b → 目標: b,id,a（b を先頭へ動かす＝b が確実に移動する列）。b の型の変更は未選択
                .Live(s => s.Add(S.DetachedInts("t", "id", "a", "b")))
                .Items(_ =>
                    [
                        I.Reorder("t", S.DetachedInts("t", "b", "id", "a")),
                        new SchemaDiffItem
                        {
                            Kind = SchemaDiffKind.AlterColumn,
                            TableName = "t",
                            ColumnName = "b",
                            Column = S.Nullable("b", "BIGINT"),
                            IsSelected = false,
                        },
                    ]
                )
                // 未選択の Alter（BIGINT）は反映されず live の定義（INT）で動かす
                .Expect(e =>
                    e.Reorders.Count("* move *", 1).Reorders.Single("t move b:INT * after *")
                ),
            Row(
                    "plan-reorder-native-moved-column-uses-altered-definition-when-selected",
                    "MySQL: 移動列の定義は選択済み Alter があれば新定義になる",
                    DialectNames.MySql
                )
                // b を先頭へ動かす（＝b が確実に移動する列）
                .Live(s => s.Add(S.DetachedInts("t", "id", "a", "b")))
                .Items(_ =>
                    [
                        I.Reorder("t", S.DetachedInts("t", "b", "id", "a")),
                        new SchemaDiffItem
                        {
                            Kind = SchemaDiffKind.AlterColumn,
                            TableName = "t",
                            ColumnName = "b",
                            Column = S.Nullable("b", "BIGINT"),
                            IsSelected = true,
                        },
                    ]
                )
                .Expect(e =>
                    e.Reorders.Count("* move *", 1).Reorders.Single("t move b:BIGINT * after ^")
                ),
            Row(
                    "plan-reorder-native-includes-selected-added-column",
                    "MySQL: 選択済み AddColumn の列も並べ替え対象になる",
                    DialectNames.MySql
                )
                // live: id,a ＋ 追加 x → 実効: id,a,x。目標: x,id,a（追加列 x を先頭へ動かす＝x が確実に移動する列）
                .Live(s => s.Add(S.DetachedInts("t", "id", "a")))
                .Items(_ =>
                    [
                        I.Reorder("t", S.DetachedInts("t", "x", "id", "a")),
                        new SchemaDiffItem
                        {
                            Kind = SchemaDiffKind.AddColumn,
                            TableName = "t",
                            ColumnName = "x",
                            Column = S.Nullable("x", "TEXT"),
                            IsSelected = true,
                        },
                    ]
                )
                // AddColumn の新しい定義で動かす
                .Expect(e =>
                    e.Reorders.Count("* move *", 1).Reorders.Single("t move x:TEXT * after ^")
                ),
            Row(
                    "plan-reorder-native-already-ordered-is-empty",
                    "MySQL: 既に目標順なら並べ替え計画は空",
                    DialectNames.MySql
                )
                .Live(s => s.Add(S.DetachedInts("t", "id", "a", "b")))
                .Items(_ => [I.Reorder("t", S.DetachedInts("t", "id", "a", "b"))])
                .Expect(e => e.Reorders.Empty()),
            Row(
                    "plan-reorder-native-unknown-table-is-dropped",
                    "MySQL: live に無いテーブルの ReorderColumns は無視される",
                    DialectNames.MySql
                )
                .Items(_ => [I.Reorder("ghost", S.DetachedInts("ghost", "id", "b", "a"))])
                .Expect(e => e.Reorders.Empty().Sections.Empty().Rebuilds.Empty()),
            Row(
                    "plan-reorder-native-null-context-throws",
                    "MySQL: ReorderColumns 選択かつ context 省略は例外",
                    DialectNames.MySql
                )
                .Items(_ => [I.Reorder("t", S.DetachedInts("t", "id", "b", "a"))])
                .NoContext()
                .Plan()
                .Expect(e => e.Exception.Single("InvalidOperationException: *")),
            Row(
                    "plan-reorder-none-dialect-disappears-from-plan",
                    "None 方言では ReorderColumns は計画から消える",
                    DialectNames.SqlServer
                )
                .Items(_ => [I.Reorder("t", S.DetachedInts("t", "id", "b", "a"))])
                .NoContext()
                .Expect(e => e.Reorders.Empty().Sections.Empty().Rebuilds.Empty()),
        ];
}
