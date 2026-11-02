using System.Collections.Generic;
using QuickER.Dialects;
using QuickER.Provider;
using QuickER.Tests.TestSupport.Engines;
using C = QuickER.Tests.Integration.Dialects.SyncPlanFuzz.SyncPlanExpectationCapabilities;
using I = QuickER.Tests.Integration.Dialects.SyncPlanFuzz.ExpectationItems;
using S = QuickER.Tests.Integration.Dialects.SyncPlanFuzz.ExpectationSchema;

namespace QuickER.Tests.Integration.Dialects.SyncPlanFuzz;

/// <summary>
/// 計画の段の期待の行（再構築・列順の変更の細部：大文字小文字だけ違う live テーブルの引き当て・解決できない項目・削除する表）
/// </summary>
internal static partial class SyncPlanExpectationRows
{
    private static IEnumerable<ScenarioRow<SyncPlanExpectationInput>> PlanRebuildDetailRows() =>
        [
            // 引き当ての規則は差分と同じ（序数一致の 1 件を優先し、大文字小文字を無視した一致は 1 件のときだけ）
            Row(
                    "plan-rebuild-case-only-different-live-tables-exact-match",
                    "再構築: 大文字小文字だけ違う live テーブルは序数一致で引き、曖昧なら再構築せずスキップを明示する",
                    DialectNames.Sqlite
                )
                .Live(DupTables)
                .Items(_ => [AlterDupNote("dup")])
                // 序数一致: 小文字のほうを作り直す
                .Expect(e => e.Rebuilds.Count("* cols *", 1).Rebuilds.Single("dup cols *")),
            Row(
                    "plan-rebuild-case-only-different-live-tables-ambiguous-is-skipped",
                    "再構築: 大文字小文字だけ違う live テーブルは序数一致で引き、曖昧なら再構築せずスキップを明示する",
                    DialectNames.Sqlite
                )
                .Live(DupTables)
                .Items(_ => [AlterDupNote("DUP")])
                // 曖昧な名前は並び順で片方を選ばず「live に無いテーブル」と同じ扱い＝再構築せず、項目はセクションへ残る
                // （レンダラーがスキップを明示する）
                .Expect(e =>
                    e.Rebuilds.Empty().Sections.Exact("AlterColumn: #0 AlterColumn DUP.note")
                ),
            Row(
                    "plan-reorder-native-case-only-different-live-tables-exact-match",
                    "列順変更: 大文字小文字だけ違う live テーブルが曖昧なら並べ替え計画に含めない",
                    DialectNames.MySql
                )
                .Live(DupIntTables)
                .Items(_ => [I.Reorder("Dup", S.DetachedInts("Dup", "b", "a"))])
                .Expect(e =>
                    e.Reorders.Exact(
                        "Dup move a:INT null after b",
                        "Dup source #0 ReorderColumns Dup"
                    )
                ),
            Row(
                    "plan-reorder-native-case-only-different-live-tables-ambiguous-is-dropped",
                    "列順変更: 大文字小文字だけ違う live テーブルが曖昧なら並べ替え計画に含めない",
                    DialectNames.MySql
                )
                .Live(DupIntTables)
                .Items(_ => [I.Reorder("DUP", S.DetachedInts("DUP", "b", "a"))])
                .Expect(e => e.Reorders.Empty()),
            // 処理済みにすると、何も変えずに成功扱いになり、次の差分でまた出た
            Row(
                    "plan-rebuild-unresolvable-item-stays-in-section",
                    "再構築: 解決できない項目は処理済みにせずセクションへ残す",
                    DialectNames.Sqlite
                )
                .Live(s =>
                    s.Tbl("t", ("id", "INT", true), ("x", "INT", false), ("y", "INT", false))
                )
                // 同じ計画で消える列の一意制約（再構築の合成で解決できない）
                .Items(w =>
                    [
                        new SchemaDiffItem
                        {
                            Kind = SchemaDiffKind.DropColumn,
                            TableName = "t",
                            ColumnName = "x",
                            Entity = w.Live["t"],
                            Column = w.Live["t"].Columns[1],
                            IsSelected = true,
                        },
                        I.Unique(SchemaDiffKind.AddUniqueConstraint, "t", "UQ_t_x", ["x"]),
                    ]
                )
                .Expect(e =>
                    e.Rebuilds.Count("* cols *", 1)
                        .Rebuilds.Absent("* source #1 *", "* uq *")
                        .Items.Contains("* > #1 AddUniqueConstraint *")
                ),
            // 作り直してから消すのは無駄で、意味の無い喪失警告も出た。その表への作り直しの項目は表ごと消えるので捨てる
            Row(
                    "plan-rebuild-dropped-table-is-not-rebuilt",
                    "再構築: 削除を選んだ表は作り直さない",
                    DialectNames.Sqlite
                )
                .Live(s => s.Tbl("t", ("id", "INT", true), ("x", "INT", false)))
                .Items(w =>
                    [
                        new SchemaDiffItem
                        {
                            Kind = SchemaDiffKind.AlterColumn,
                            TableName = "t",
                            ColumnName = "x",
                            Column = S.Nullable("x", "TEXT"),
                            IsSelected = true,
                        },
                        new SchemaDiffItem
                        {
                            Kind = SchemaDiffKind.DropTable,
                            TableName = "t",
                            Entity = w.Live["t"],
                            IsSelected = true,
                        },
                    ]
                )
                .Context(w => new SyncPlanContext
                {
                    LiveEntities = w.Live.Entities,
                    LiveRelationships = w.Live.Relationships,
                    TableCreateSql = new Dictionary<string, string>
                    {
                        ["t"] = "CREATE TABLE t (id INT PRIMARY KEY, x INT DEFAULT 0)",
                    },
                })
                .Expect(e => e.Rebuilds.Empty().Warnings.Empty().Sections.Exact("DropTable: *")),
        ];

    /// <summary>大文字小文字だけ違う表 Dup・dup（主キー id と NULL 許容の note TEXT）を足す</summary>
    private static void DupTables(ExpectationSchema s)
    {
        s.Tbl("Dup", ("id", "INT", true), ("note", "TEXT", false));
        s.Tbl("dup", ("id", "INT", true), ("note", "TEXT", false));
    }

    /// <summary>大文字小文字だけ違う表 Dup・dup（NULL 許容の INT の列 a・b）を足す</summary>
    private static void DupIntTables(ExpectationSchema s)
    {
        s.Add(S.DetachedInts("Dup", "a", "b"));
        s.Add(S.DetachedInts("dup", "a", "b"));
    }

    /// <summary>表 <paramref name="table"/> の列 note を INTEGER へ変える列の変更の項目</summary>
    private static SchemaDiffItem AlterDupNote(string table) =>
        new()
        {
            Kind = SchemaDiffKind.AlterColumn,
            TableName = table,
            ColumnName = "note",
            Column = S.Nullable("note", "INTEGER"),
            IsSelected = true,
        };
}
