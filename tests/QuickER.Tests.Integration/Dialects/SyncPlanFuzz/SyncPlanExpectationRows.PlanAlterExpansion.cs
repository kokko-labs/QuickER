using System.Collections.Generic;
using System.Linq;
using QuickER.Dialects;
using QuickER.Model;
using QuickER.Tests.TestSupport.Engines;
using S = QuickER.Tests.Integration.Dialects.SyncPlanFuzz.ExpectationSchema;

namespace QuickER.Tests.Integration.Dialects.SyncPlanFuzz;

/// <summary>
/// 計画の段の期待の行（作り直しの差分項目＝AlterForeignKey / AlterUniqueConstraint が計画の入口で削除と追加へ展開され、計画のどこにも
/// 作り直しの種別のまま残らない）
/// </summary>
/// <remarks>
/// 計画のセクションにも方言別レンダラーにも作り直しの種別は無い。展開を外すと例外にはならず、選んだ作り直しが黙って計画から消える
/// ため、「残らない」ことと「選んだ作り直しが削除と追加として現れる」ことの両方を書く。
/// </remarks>
internal static partial class SyncPlanExpectationRows
{
    private static IEnumerable<ScenarioRow<SyncPlanExpectationInput>> PlanAlterExpansionRows() =>
        [
            .. new[] { true, false }.Select(selectAlters =>
                (ScenarioRow<SyncPlanExpectationInput>)
                    Row(
                            $"plan-alter-expansion-{(selectAlters ? "selected" : "unselected")}",
                            "作り直しの展開: 計画に作り直しの種別は残らず、選んだ作り直しは削除と追加として現れる",
                            AllDialects
                        )
                        .Live(s =>
                            AlterExpansionSchema(s, ForeignKeyReferentialAction.NoAction, ["code"])
                        )
                        .Target(s =>
                            AlterExpansionSchema(
                                s,
                                ForeignKeyReferentialAction.Cascade,
                                ["code", "customer_id"]
                            )
                        )
                        .Select(items =>
                        {
                            foreach (var item in items)
                            {
                                item.IsSelected = selectAlters;
                            }
                        })
                        .Expect(e =>
                        {
                            e.Diff.Unordered("AlterForeignKey *", "AlterUniqueConstraint *")
                                .Sections.Absent("*AlterForeignKey*", "*AlterUniqueConstraint*")
                                .Sources.Absent(
                                    "* > AlterForeignKey *",
                                    "* > AlterUniqueConstraint *"
                                )
                                .Rebuilds.Absent(
                                    "* source *AlterForeignKey*",
                                    "* source *AlterUniqueConstraint*"
                                )
                                .Reorders.Absent(
                                    "* source *AlterForeignKey*",
                                    "* source *AlterUniqueConstraint*"
                                );

                            if (!selectAlters)
                            {
                                e.Items.Empty().Sources.Empty();
                                return;
                            }

                            // 選んだ作り直しは、外す側が旧い定義・付ける側が新しい定義の削除と追加になる（逐次 DDL の方言は
                            // セクションの項目、SQLite は再構築へ畳んだ元の項目）
                            foreach (var dialect in AllDialects)
                            {
                                e.For(
                                    dialect,
                                    d =>
                                    {
                                        var (planned, other) =
                                            dialect == DialectNames.Sqlite
                                                ? (d.Sources, d.Items)
                                                : (d.Items, d.Sources);
                                        other.Absent(
                                            "* > DropForeignKey *",
                                            "* > AddForeignKey *",
                                            "* > DropUniqueConstraint *",
                                            "* > AddUniqueConstraint *"
                                        );
                                        planned.Count("* > DropForeignKey *", 1);
                                        planned.Single("* > DropForeignKey * del=NoAction *");
                                        planned.Count("* > AddForeignKey *", 1);
                                        planned.Single("* > AddForeignKey * del=Cascade *");
                                        planned.Count("* > DropUniqueConstraint *", 1);
                                        planned.Single(
                                            "* > DropUniqueConstraint * uq=UQ_orders_code * uqcols=(code) *"
                                        );
                                        planned.Count("* > AddUniqueConstraint *", 1);
                                        planned.Single(
                                            "* > AddUniqueConstraint * uq=UQ_orders_code * uqcols=(code,customer_id) *"
                                        );
                                    }
                                );
                            }
                        })
            ),
        ];

    /// <summary>customer(id) ← orders(id, customer_id, code) と、名前付き外部キー・一意制約 UQ_orders_code を持つ図を足す</summary>
    private static void AlterExpansionSchema(
        S s,
        ForeignKeyReferentialAction onDelete,
        string[] uniqueColumns
    )
    {
        s.Table("customer", S.Pk("id", "INTEGER"));
        s.Table(
            "orders",
            S.Pk("id", "INTEGER"),
            S.NotNull("customer_id", "INTEGER"),
            S.NotNull("code", "INTEGER")
        );
        s.Unique("orders", "UQ_orders_code", uniqueColumns);
        s.Fk("customer", "orders", "FK_orders_customer", onDelete, ("id", "customer_id"));
    }
}
