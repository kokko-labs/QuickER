using System.Collections.Generic;
using System.Linq;
using QuickER.Dialects;
using QuickER.Provider;
using QuickER.Tests.TestSupport.Engines;
using C = QuickER.Tests.Integration.Dialects.SyncPlanFuzz.SyncPlanExpectationCapabilities;
using ProviderStrings = QuickER.Provider.Resources.Strings;
using S = QuickER.Tests.Integration.Dialects.SyncPlanFuzz.ExpectationSchema;

namespace QuickER.Tests.Integration.Dialects.SyncPlanFuzz;

/// <summary>
/// 計画の段の期待の行（主キー・一意制約の構成列の削除を、それを前提にする変更を選ばずに選んだとき：主キーの列の削除は、その列を
/// 外す主キーの変更を選んだときだけ行い、選ばなければ外して名指しする。一意制約の構成列の削除は、その一意制約を暗黙に外して名指し
/// する。表の列が 1 本も残らない列の削除は外す。図で列の無い表の追加は外す）
/// </summary>
internal static partial class SyncPlanExpectationRows
{
    /// <summary>暗黙に外す一意制約 UQ_t_cd の削除の説明</summary>
    private static string AutoUniqueConstraintDropDescription =>
        string.Format(ProviderStrings.Diff_AutoUniqueConstraintDrop, "c, d");

    private static IEnumerable<ScenarioRow<SyncPlanExpectationInput>> PlanDropMemberColumnRows() =>
        [
            Row(
                    "plan-drop-member-column-drops-only-exclude-pk-column-and-drop-unique",
                    "列の削除: 主キーの列は主キーの変更が無ければ外し、一意制約の構成列は一意制約ごと外して警告する",
                    AllDialects
                )
                .Live(DropMemberColumnLive)
                .Target(DropMemberColumnTarget)
                .Select(items =>
                {
                    foreach (var item in items)
                    {
                        item.IsSelected = item.Kind == SchemaDiffKind.DropColumn;
                    }
                })
                .Expect(e =>
                {
                    e.Warnings.Count("SelectedItemRequiresUnselectedItem*", 1)
                        .Warnings.Single("SelectedItemRequiresUnselectedItem(t): b (PRIMARY KEY)")
                        .Warnings.Count("UniqueConstraintDroppedWithColumn*", 1)
                        .Warnings.Single("UniqueConstraintDroppedWithColumn(t): UQ_t_cd (c, d)");

                    // SQLite の再構築は主キーを保ち、一意制約を持たない
                    e.For(
                        DialectNames.Sqlite,
                        d =>
                            d.Rebuilds.Count("* cols *", 1)
                                .Rebuilds.Single(
                                    "t cols a:int notnull pk live, b:int notnull pk live, c:int null live",
                                    "t pk (a,b)"
                                )
                                .Rebuilds.Absent("t uq *")
                    );

                    // 逐次 DDL の方言は主キーの変更を出さず、列 d だけを消し、その前に一意制約を暗黙に外す
                    foreach (var dialect in SequentialDropMemberColumnDialects)
                    {
                        e.For(
                            dialect,
                            d =>
                                d.Sections.Absent("AlterPrimaryKey*")
                                    .Sections.Count("DropColumn: *", 1)
                                    .Sections.Single("DropColumn: DropColumn t.d")
                                    .Sections.Count("DropUniqueConstraint: *", 1)
                                    .Items.Count("DropUniqueConstraint > *", 1)
                                    .Items.Single(
                                        $"DropUniqueConstraint > DropUniqueConstraint t uq=UQ_t_cd * desc=\"{AutoUniqueConstraintDropDescription}\""
                                    )
                        );
                    }
                }),
            Row(
                    "plan-drop-member-column-pk-column-with-pk-change-is-kept",
                    "列の削除: その列を外す主キーの変更も選べば、主キーの列を削除する",
                    AllDialects
                )
                .Live(DropMemberColumnLive)
                .Target(DropMemberColumnTarget)
                .Select(items =>
                {
                    foreach (var item in items)
                    {
                        item.IsSelected =
                            item.Kind == SchemaDiffKind.AlterPrimaryKey
                            || item is { Kind: SchemaDiffKind.DropColumn, ColumnName: "b" };
                    }
                })
                .Expect(e =>
                {
                    e.Warnings.Absent("SelectedItemRequiresUnselectedItem*");
                    e.For(
                        DialectNames.Sqlite,
                        d =>
                            d.Rebuilds.Count("* cols *", 1)
                                .Rebuilds.Single(
                                    "t cols a:int notnull pk live, c:int null live, d:int null live",
                                    "t pk (a)"
                                )
                    );

                    foreach (var dialect in SequentialDropMemberColumnDialects)
                    {
                        e.For(
                            dialect,
                            d =>
                                d.Sections.Count("DropColumn: *", 1)
                                    .Sections.Single("DropColumn: DropColumn t.b")
                        );
                    }
                }),
            Row(
                    "plan-drop-member-column-unique-column-with-explicit-drop-adds-nothing",
                    "列の削除: 一意制約の削除を選んでいれば、暗黙に足さず警告もしない",
                    DialectNames.SqlServer
                )
                .Live(DropMemberColumnLive)
                .Target(DropMemberColumnTarget)
                .Select(items =>
                {
                    foreach (var item in items)
                    {
                        item.IsSelected =
                            item.Kind == SchemaDiffKind.DropUniqueConstraint
                            || item is { Kind: SchemaDiffKind.DropColumn, ColumnName: "d" };
                    }
                })
                .Expect(e =>
                    e.Warnings.Absent("UniqueConstraintDroppedWithColumn*")
                        .Sections.Count("DropUniqueConstraint: *", 1)
                        .Items.Count("DropUniqueConstraint > *", 1)
                        .Items.Absent(
                            $"DropUniqueConstraint > * desc=\"{AutoUniqueConstraintDropDescription}\""
                        )
                ),
            Row(
                    "plan-drop-member-column-pk-column-of-excluded-pk-change-is-excluded",
                    "列の削除: 外れた主キーの変更を前提にする主キーの列の削除も外す",
                    DialectNames.SqlServer
                )
                .Live(s => s.Table("t", S.Pk("a", "int"), S.Nullable("x", "int")))
                .Target(s => s.Table("t", S.Pk("n", "int"), S.Nullable("x", "int")))
                .Select(items =>
                {
                    foreach (var item in items)
                    {
                        item.IsSelected =
                            item.Kind
                                is SchemaDiffKind.AlterPrimaryKey
                                    or SchemaDiffKind.DropColumn;
                    }
                })
                .Expect(e =>
                    e.Warnings.Count("SelectedItemRequiresUnselectedItem*", 2)
                        .Warnings.Single(
                            "SelectedItemRequiresUnselectedItem(*): PRIMARY KEY (t.n)",
                            "SelectedItemRequiresUnselectedItem(*): a (PRIMARY KEY)"
                        )
                        .Sections.Absent("DropColumn*", "AlterPrimaryKey*")
                ),
            .. new[] { false, true }.Select(addColumn =>
                (ScenarioRow<SyncPlanExpectationInput>)
                    Row(
                            $"plan-drop-member-column-drop-all-columns-{(addColumn ? "with-added-column-is-kept" : "is-excluded")}",
                            "列の削除: 表の列が 1 本も残らないなら列の削除を外して警告し、暗黙の削除も足さない（列を足すなら外さない）",
                            AllDialects
                        )
                        .Live(s =>
                        {
                            DropMemberColumnLive(s);
                            s.Table("p", S.Pk("id", "int"));
                            s.Fk("p", "t", "FK_t_p", ("id", "c"));
                        })
                        .Target(s =>
                        {
                            var target = s.Table("t");

                            if (addColumn)
                            {
                                target.Columns.Add(S.Nullable("n", "int"));
                            }

                            s.Table("p", S.Pk("id", "int"));
                        })
                        .Select(items =>
                        {
                            foreach (var item in items)
                            {
                                item.IsSelected =
                                    item.Kind
                                        is SchemaDiffKind.AlterPrimaryKey
                                            or SchemaDiffKind.DropColumn
                                            or SchemaDiffKind.AddColumn;
                            }
                        })
                        .Expect(e =>
                        {
                            if (addColumn)
                            {
                                e.Warnings.Absent("TableLeftWithoutColumns*").Excluded.Empty();
                                return;
                            }

                            e.Warnings.Count("TableLeftWithoutColumns*", 1)
                                .Warnings.Single("TableLeftWithoutColumns(t): a, b, c, d")
                                .Excluded.Unordered(
                                    "DropColumn t.a",
                                    "DropColumn t.b",
                                    "DropColumn t.c",
                                    "DropColumn t.d"
                                )
                                .Warnings.Absent("UniqueConstraintDroppedWithColumn*");

                            // SQLite の再構築は列・一意制約・外部キーを保ち、主キーだけを外す
                            e.For(
                                DialectNames.Sqlite,
                                d =>
                                    d.Rebuilds.Count("* cols *", 1)
                                        .Rebuilds.Single(
                                            "t cols a:* live, b:* live, c:* live, d:* live",
                                            "t pk ()",
                                            "t copy (a,b,c,d)"
                                        )
                                        .Rebuilds.Count("t uq *", 1)
                                        .Rebuilds.Count("t fk *", 1)
                            );

                            foreach (var dialect in SequentialDropMemberColumnDialects)
                            {
                                e.For(
                                    dialect,
                                    d =>
                                        d.Sections.Absent(
                                                "DropColumn*",
                                                "DropUniqueConstraint*",
                                                "DropForeignKey*"
                                            )
                                            .Sections.Count("AlterPrimaryKey/Drop: *", 1)
                                );
                            }
                        })
            ),
            // 見る面は計画（差分・項目・セクション・再構築・外した項目・警告）。同期スクリプトの文の面は方言の DDL の表の行
            // sync-add-table-without-columns-is-excluded-with-warning（DialectDdlTableTests）が見る
            Row(
                    "plan-add-table-without-columns-is-excluded-with-warning",
                    "表の追加: 図で列の無い表の追加は外して名指しし（方言に依らない）、ほかの表の追加はそのまま計画する",
                    [C.Neutral, .. AllDialects]
                )
                .Live(s => TableWithoutColumnsLive(s, "int"))
                .Target(s => TableWithoutColumnsTarget(s, "int"))
                .Expect(e =>
                {
                    e.Diff.Contains("AddTable orders *", "AddTable empty *")
                        .Items.Absent("* AddTable empty *", "* SetTableDescription empty *")
                        .Rebuilds.Absent("empty *");

                    // 逐次 DDL の方言（と中立）は表の追加のセクションに orders だけを置き、外した表の追加を前提にする表の説明も
                    // 外して名指しする。SQLite は orders を作るだけの再構築にする（説明を持たないので説明の項目は無い）
                    foreach (
                        var capability in (string[])
                            [C.Neutral, .. SequentialDropMemberColumnDialects]
                    )
                    {
                        e.For(
                            capability,
                            d =>
                                d.Sections.Exact("AddTable: AddTable orders")
                                    .Warnings.Unordered(
                                        "TableWithoutColumns(empty)",
                                        "SelectedItemRequiresUnselectedItem(empty): COMMENT (*)"
                                    )
                                    .Excluded.Unordered(
                                        "AddTable empty",
                                        "SetTableDescription empty"
                                    )
                        );
                    }

                    e.For(
                        DialectNames.Sqlite,
                        d =>
                            d.Sections.Empty()
                                .Rebuilds.Contains("orders cols *")
                                .Warnings.Exact("TableWithoutColumns(empty)")
                                .Excluded.Exact("AddTable empty")
                    );
                }),
        ];

    /// <summary>DB 側の図: a(id PK)</summary>
    /// <param name="s">図</param>
    /// <param name="intType">整数型の綴り（実 DB の段は方言の取り込み直した綴り）</param>
    private static void TableWithoutColumnsLive(S s, string intType) =>
        s.Table("a", S.Pk("id", intType));

    /// <summary>目標の図: a(id PK)・orders(id PK)・説明を持つ列の無い表 empty</summary>
    /// <param name="s">図</param>
    /// <param name="intType">整数型の綴り（実 DB の段は方言の取り込み直した綴り）</param>
    private static void TableWithoutColumnsTarget(S s, string intType)
    {
        s.Table("a", S.Pk("id", intType));
        s.Table("orders", S.Pk("id", intType));
        s.Table("empty").Description = "no columns yet";
    }

    /// <summary>逐次 DDL の 4 方言</summary>
    private static string[] SequentialDropMemberColumnDialects =>
        [DialectNames.SqlServer, DialectNames.PostgreSql, DialectNames.MySql, DialectNames.Oracle];

    /// <summary>DB 側の図: t(a PK, b PK, c, d, UNIQUE UQ_t_cd (c, d))</summary>
    private static void DropMemberColumnLive(S s)
    {
        s.Table(
            "t",
            S.Pk("a", "int"),
            S.Pk("b", "int"),
            S.Nullable("c", "int"),
            S.Nullable("d", "int")
        );
        s.Unique("t", "UQ_t_cd", "c", "d");
    }

    /// <summary>目標の図: t(a PK, c)</summary>
    private static void DropMemberColumnTarget(S s) =>
        s.Table("t", S.Pk("a", "int"), S.Nullable("c", "int"));
}
