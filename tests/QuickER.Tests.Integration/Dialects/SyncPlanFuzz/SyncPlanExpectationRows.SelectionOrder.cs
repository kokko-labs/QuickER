using System.Collections.Generic;
using QuickER.Dialects;
using QuickER.Model;
using QuickER.Provider;
using QuickER.Tests.TestSupport.Engines;
using S = QuickER.Tests.Integration.Dialects.SyncPlanFuzz.ExpectationSchema;

namespace QuickER.Tests.Integration.Dialects.SyncPlanFuzz;

/// <summary>
/// 計画の段の期待の行（除外の周の規則の並びと取り下げの当て方：1 つの項目が 2 つの規則に当たるときは規則の表で前の規則が名指しし、
/// 警告と外した項目の並びは規則の表の順で決まり、規則が外した項目は次の規則の前に並びから落ち、代わりの項目は元の位置に残る）
/// </summary>
internal static partial class SyncPlanExpectationRows
{
    private static IEnumerable<ScenarioRow<SyncPlanExpectationInput>> PlanSelectionOrderRows() =>
        [
            // 前提の欠け（主キーに残る列の削除）は列が残らない表の判定より前：主キーの列の削除を外した後の並びで列を数えるので、
            // もう 1 本の列の削除は表の列を空にせず残る。外した項目は主キーの列の削除の 1 件だけ（同じ項目を 2 回外さない）
            Row(
                    "plan-selection-order-pk-column-drop-is-named-as-missing-pk-change-and-other-drop-stays",
                    "規則の並び: 主キーの列ともう 1 本の列だけの表で両方の列の削除を選び主キーの変更を選ばないと、前提の欠けの規則が主キーの列の削除だけを外し、もう 1 本の列の削除は残る（列が残らない表の規則は外した後の並びで数える）",
                    DialectNames.PostgreSql
                )
                .Live(s => s.Table("sel_t", S.Pk("code", "integer"), S.Nullable("f0", "integer")))
                .Target(s => s.Table("sel_t", S.Pk("n", "integer")))
                .Select(items =>
                {
                    foreach (var item in items)
                    {
                        item.IsSelected = item.Kind == SchemaDiffKind.DropColumn;
                    }
                })
                .Expect(e =>
                    e.Excluded.Exact("DropColumn sel_t.code")
                        .Warnings.Exact(
                            "SelectedItemRequiresUnselectedItem(sel_t): code (PRIMARY KEY)"
                        )
                        .Sections.Exact("DropColumn: DropColumn sel_t.f0")
                ),
            // 前提の欠けは型の食い違いより前：選んでいない列の追加を前提にし、同期後に両端の型も食い違う外部キーの追加は、前提の欠けで
            // 名指しする
            Row(
                    "plan-selection-order-fk-add-missing-column-and-type-mismatch-is-named-as-missing-column",
                    "規則の並び: 選んでいない列の追加を前提にし、同じ同期で親の列の型も食い違う外部キーの追加は、前提の欠けの規則が名指しする（型の食い違いの規則は外した後の並びを受ける）",
                    DialectNames.SqlServer
                )
                .Live(s =>
                {
                    s.Table("sel_p", S.Pk("id", "int"));
                    s.Table("sel_c", S.Pk("id", "int"));
                })
                .Target(s =>
                {
                    s.Table("sel_p", S.Pk("id", "decimal(12,2)"));
                    s.Table("sel_c", S.Pk("id", "int"), S.Nullable("pid", "int"));
                    s.Fk("sel_p", "sel_c", "fk_sel_c_p", ("id", "pid"));
                })
                .Select(items =>
                {
                    foreach (var item in items)
                    {
                        item.IsSelected =
                            item.Kind is SchemaDiffKind.AlterColumn or SchemaDiffKind.AddForeignKey;
                    }
                })
                .Expect(e =>
                    e.Excluded.Exact("AddForeignKey sel_c* fk=fk_sel_c_p")
                        .Warnings.Single("SelectedItemRequiresUnselectedItem(sel_c): *")
                        .Warnings.Absent("ForeignKeyColumnTypeMismatch*")
                ),
            // 前提の欠けは NOT NULL の列への SET NULL より前：選んでいない表の追加を前提にし、子の列が NOT NULL の SET NULL の外部キーの
            // 追加は、前提の欠けで名指しする
            Row(
                    "plan-selection-order-fk-add-missing-prerequisite-and-set-null-on-not-null-is-named-as-missing-prerequisite",
                    "規則の並び: SET NULL を作れない方言で、選んでいない表の追加を前提にし、子の列が NOT NULL の SET NULL の外部キーの追加は、前提の欠けの規則が名指しする（SET NULL の規則は外した後の並びを受ける）",
                    DialectNames.MySql
                )
                .Live(s => s.Table("sel_c", S.Pk("id", "int"), S.NotNull("pid", "int")))
                .Target(s =>
                {
                    s.Table("sel_c", S.Pk("id", "int"), S.NotNull("pid", "int"));
                    s.Table("sel_p", S.Pk("id", "int"));
                    s.Fk(
                        "sel_p",
                        "sel_c",
                        "fk_sel_c_p",
                        ForeignKeyReferentialAction.SetNull,
                        ("id", "pid")
                    );
                })
                .Select(items =>
                {
                    foreach (var item in items)
                    {
                        item.IsSelected = item.Kind == SchemaDiffKind.AddForeignKey;
                    }
                })
                .Expect(e =>
                    e.Excluded.Exact("AddForeignKey sel_c* fk=fk_sel_c_p")
                        .Warnings.Single("SelectedItemRequiresUnselectedItem(sel_c): *")
                        .Warnings.Absent("SetNullOnNotNullColumn*")
                ),
            // 名前の重なる表の追加は前提の欠けより前：項目の並びでは前提の欠けの項目（選んでいない列の説明）が表の追加より前にあっても、
            // 警告と外した項目は「名前の重なり → 前提の欠け」の順に並ぶ
            Row(
                    "plan-selection-order-colliding-table-is-named-before-missing-prerequisite",
                    "規則の並び: 名前の重なる表の追加と、別の表の前提の欠けの項目（項目の並びでは表の追加より前）を同時に選ぶと、警告と外した項目は規則の表の順（名前の重なり → 前提の欠け）に並ぶ",
                    DialectNames.PostgreSql
                )
                .Live(s =>
                {
                    s.Table("old_t", S.Pk("id", "integer"), S.NotNull("u", "integer"));
                    s.Unique("old_t", "uq_keep", "u");
                    s.Table("sel_a", S.Pk("id", "integer"));
                })
                .Target(s =>
                {
                    var memo = S.Nullable("x", "integer");
                    memo.Description = "memo";
                    s.Table("sel_a", S.Pk("id", "integer"), memo);
                    s.Table("new_t", S.Pk("id", "integer"), S.NotNull("u", "integer"));
                    s.Unique("new_t", "uq_keep", "u");
                })
                .Select(items =>
                {
                    foreach (var item in items)
                    {
                        item.IsSelected =
                            item.Kind
                                is SchemaDiffKind.AddTable
                                    or SchemaDiffKind.SetColumnDescription;
                    }
                })
                .Expect(e =>
                    e.Excluded.Exact("AddTable new_t", "SetColumnDescription sel_a.x")
                        .Warnings.Exact(
                            "SelectedItemRequiresUnselectedItem(new_t): uq_keep (old_t.uq_keep)",
                            "SelectedItemRequiresUnselectedItem(sel_a): *"
                        )
                        .Sections.Empty()
                ),
            // 型の食い違いは NOT NULL の列への SET NULL より前：同期後に両端の型が食い違い、子の列が NOT NULL の SET NULL の外部キーの
            // 追加は、型の食い違いで名指しする
            Row(
                    "plan-selection-order-fk-add-type-mismatch-and-set-null-on-not-null-is-named-as-type-mismatch",
                    "規則の並び: SET NULL を作れない方言で、同期後に両端の型が食い違い、子の列が NOT NULL の SET NULL の外部キーの追加は、型の食い違いの規則が名指しする（SET NULL の規則は外した後の並びを受ける）",
                    DialectNames.MySql
                )
                .Live(s =>
                {
                    s.Table("sel_p", S.Pk("id", "int"));
                    s.Table("sel_c", S.Pk("id", "int"), S.NotNull("pid", "int"));
                })
                .Target(s =>
                {
                    s.Table("sel_p", S.Pk("id", "int"));
                    s.Table("sel_c", S.Pk("id", "int"), S.NotNull("pid", "bigint"));
                    s.Fk(
                        "sel_p",
                        "sel_c",
                        "fk_sel_c_p",
                        ForeignKeyReferentialAction.SetNull,
                        ("id", "pid")
                    );
                })
                .Select(items =>
                {
                    foreach (var item in items)
                    {
                        item.IsSelected =
                            item.Kind is SchemaDiffKind.AlterColumn or SchemaDiffKind.AddForeignKey;
                    }
                })
                .Expect(e =>
                    e.Excluded.Exact("AddForeignKey sel_c* fk=fk_sel_c_p")
                        .Warnings.Single("ForeignKeyColumnTypeMismatch(sel_c): fk_sel_c_p")
                        .Warnings.Absent("SetNullOnNotNullColumn*")
                ),
            // 1 つの規則が 2 つ以上の項目を外すときは、取り下げの順（項目の並びの順）に警告と外した項目が並ぶ
            Row(
                    "plan-selection-withdrawals-keep-item-order",
                    "取り下げの当て方: 前提の欠けの規則が同じ周で 2 つの外部キーの追加を外すと、警告と外した項目は取り下げの順（項目の並びの順）に並ぶ",
                    DialectNames.SqlServer
                )
                .Live(s =>
                {
                    s.Table("sel_p", S.Pk("id", "int"));
                    s.Table("sel_c", S.Pk("id", "int"));
                })
                .Target(s =>
                {
                    s.Table("sel_p", S.Pk("id", "int"));
                    s.Table(
                        "sel_c",
                        S.Pk("id", "int"),
                        S.Nullable("a", "int"),
                        S.Nullable("b", "int")
                    );
                    s.Fk("sel_p", "sel_c", "fk_sel_c_a", ("id", "a"));
                    s.Fk("sel_p", "sel_c", "fk_sel_c_b", ("id", "b"));
                })
                .Select(items =>
                {
                    foreach (var item in items)
                    {
                        item.IsSelected = item.Kind == SchemaDiffKind.AddForeignKey;
                    }
                })
                .Expect(e =>
                    e.Excluded.Exact(
                            "AddForeignKey sel_c* fk=fk_sel_c_a",
                            "AddForeignKey sel_c* fk=fk_sel_c_b"
                        )
                        .Warnings.Exact(
                            "SelectedItemRequiresUnselectedItem(sel_c): fk_sel_c_a *",
                            "SelectedItemRequiresUnselectedItem(sel_c): fk_sel_c_b *"
                        )
                        .Sections.Empty()
                ),
            // 代わりの項目は外した項目の元の位置に残る：型の変更も含む主キーの列の NULL 許容化から NULL 許容化だけを外した複製は、
            // 後ろに並ぶ別の列の変更より前にある
            Row(
                    "plan-selection-replacement-keeps-its-position",
                    "取り下げの当て方: 主キーに残る列の型の変更と NULL 許容化から NULL 許容化だけを外した代わりの項目は、外した項目の元の位置（後ろの列の変更より前）に残る",
                    DialectNames.PostgreSql
                )
                .Live(s =>
                    s.Table(
                        "sel_t",
                        S.Pk("a", "integer"),
                        S.NotNull("b", "integer"),
                        S.NotNull("c", "integer")
                    )
                )
                .Target(s =>
                    s.Table(
                        "sel_t",
                        S.Nullable("a", "bigint"),
                        S.Pk("b", "integer"),
                        S.NotNull("c", "bigint")
                    )
                )
                .Select(items =>
                {
                    foreach (var item in items)
                    {
                        item.IsSelected = item.Kind == SchemaDiffKind.AlterColumn;
                    }
                })
                .Expect(e =>
                    e.Excluded.Exact("AlterColumn sel_t.a")
                        .Warnings.Exact(
                            "SelectedItemRequiresUnselectedItem(sel_t): a (PRIMARY KEY)"
                        )
                        .Sections.Exact("AlterColumn: AlterColumn sel_t.a; AlterColumn sel_t.c")
                        .Items.Contains("AlterColumn > AlterColumn sel_t.a * col=bigint notnull*")
                ),
        ];
}
