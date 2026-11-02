using System.Collections.Generic;
using System.Linq;
using QuickER.Dialects;
using QuickER.Provider;
using QuickER.Tests.TestSupport.Engines;
using C = QuickER.Tests.Integration.Dialects.SyncPlanFuzz.SyncPlanExpectationCapabilities;
using I = QuickER.Tests.Integration.Dialects.SyncPlanFuzz.ExpectationItems;
using S = QuickER.Tests.Integration.Dialects.SyncPlanFuzz.ExpectationSchema;

namespace QuickER.Tests.Integration.Dialects.SyncPlanFuzz;

/// <summary>
/// 計画の段の期待の行（消える端：同じ計画で子表・子列・親の表・親の列が消える外部キーは外すだけで付け直さない）
/// </summary>
internal static partial class SyncPlanExpectationRows
{
    /// <summary>表・列の削除だけを選ぶ形（形, 表, 列＝null なら表の削除）</summary>
    private static (string Shape, string Table, string? Column)[] DropEndpointShapes =>
        [
            ("parent-table", "customer", null),
            ("child-table", "orders", null),
            ("child-column", "orders", "customer_id"),
            ("parent-key-column", "customer", "id"),
        ];

    private static IEnumerable<ScenarioRow<SyncPlanExpectationInput>> PlanVanishingEndpointRows() =>
        [
            .. new[] { SchemaDiffKind.DropTable, SchemaDiffKind.DropColumn }.Select(dropKind =>
                (ScenarioRow<SyncPlanExpectationInput>)
                    Row(
                            $"plan-vanishing-endpoint-pk-change-with-dropped-child-{(dropKind == SchemaDiffKind.DropTable ? "table" : "column")}-drops-only",
                            "消える端: 子表の削除・子列の削除と重なる外部キーは外すだけで付け直さない",
                            DialectNames.SqlServer
                        )
                        .Live(s => ExpectationScenarios.CustomerOrders(s))
                        .Items(w =>
                            [
                                I.AlterPk("customer", w.Live["customer"].Clone(preserveId: false)),
                                new SchemaDiffItem
                                {
                                    Kind = dropKind,
                                    TableName = "orders",
                                    ColumnName =
                                        dropKind == SchemaDiffKind.DropColumn
                                            ? "customer_id"
                                            : null,
                                    Entity = w.Live["orders"],
                                    IsSelected = true,
                                },
                            ]
                        )
                        .Expect(e =>
                            e.Sections.Count("DropForeignKey: *", 1)
                                .Items.Count("DropForeignKey > *", 1)
                                .Items.Single("DropForeignKey > * fk=FK_orders_customer sel=*")
                                .Sections.Absent("AddForeignKey: *")
                                .Warnings.Empty()
                        )
            ),
            .. DropEndpointShapes.Select(shape =>
                (ScenarioRow<SyncPlanExpectationInput>)
                    Row(
                            $"plan-vanishing-endpoint-drop-only-injects-fk-drop-{shape.Shape}",
                            "消える端: 表・列の削除だけを選んでも、端の消える外部キーを先に外す",
                            DialectNames.SqlServer
                        )
                        .Live(s =>
                        {
                            ExpectationScenarios.CustomerOrders(s);

                            // 親の主キーの列を消すときは、親の表にほかの列を持たせる（id だけの表から id を消すと、表に列が
                            // 残らないので列の削除そのものを外す＝TableLeftWithoutColumns）
                            if (shape.Shape == "parent-key-column")
                            {
                                s["customer"].Columns.Add(S.Nullable("name", "nvarchar(50)"));
                            }
                        })
                        .Items(w =>
                        {
                            List<SchemaDiffItem> items =
                            [
                                new SchemaDiffItem
                                {
                                    Kind = shape.Column is null
                                        ? SchemaDiffKind.DropTable
                                        : SchemaDiffKind.DropColumn,
                                    TableName = shape.Table,
                                    ColumnName = shape.Column,
                                    Entity = w.Live[shape.Table],
                                    IsSelected = true,
                                },
                            ];

                            // 親の主キーの列の削除は、その列を外す主キーの変更（主キーを外すだけ）と一緒に選ぶ
                            if (shape.Shape == "parent-key-column")
                            {
                                var keyless = w.Live["customer"].Clone(preserveId: true);
                                keyless.Columns.Single(c => c.Name == "id").IsPrimaryKey = false;
                                items.Add(I.AlterPk("customer", keyless));
                            }

                            return items;
                        })
                        // 外部キーの削除は表・列の削除より前のセクションで、付け直さない
                        .Expect(e =>
                        {
                            var drop = shape.Column is null ? "DropTable: *" : "DropColumn: *";

                            if (shape.Shape == "parent-key-column")
                            {
                                e.Sections.Exact(
                                    "DropForeignKey: DropForeignKey orders fk=FK_orders_customer",
                                    "AlterPrimaryKey*: *",
                                    drop
                                );
                            }
                            else
                            {
                                e.Sections.Exact(
                                    "DropForeignKey: DropForeignKey orders fk=FK_orders_customer",
                                    drop
                                );
                            }

                            e.Warnings.Empty();
                        })
            ),
            .. new[] { false, true }.Select(selectForeignKeyDrop =>
                (ScenarioRow<SyncPlanExpectationInput>)
                    Row(
                            $"plan-vanishing-endpoint-drop-parent-and-child-drops-fk-once-{(selectForeignKeyDrop ? "fk-drop-selected" : "fk-drop-unselected")}",
                            "消える端: 親と子の表を消すとき外部キーを 1 回だけ先に外す",
                            DialectNames.SqlServer
                        )
                        .Live(s => ExpectationScenarios.CustomerOrders(s))
                        .Items(w =>
                            [
                                new SchemaDiffItem
                                {
                                    Kind = SchemaDiffKind.DropTable,
                                    TableName = "customer",
                                    Entity = w.Live["customer"],
                                    IsSelected = true,
                                },
                                new SchemaDiffItem
                                {
                                    Kind = SchemaDiffKind.DropTable,
                                    TableName = "orders",
                                    Entity = w.Live["orders"],
                                    IsSelected = true,
                                },
                                new SchemaDiffItem
                                {
                                    Kind = SchemaDiffKind.DropForeignKey,
                                    TableName = "orders",
                                    Entity = w.Live["orders"],
                                    ParentEntity = w.Live["customer"],
                                    ChildEntity = w.Live["orders"],
                                    Relationship = w.Live.Relationships[0],
                                    ForeignKeyName = w.Live.Relationships[0].ConstraintName,
                                    ForeignKeyColumnPairs = [new("id", "customer_id")],
                                    IsSelected = selectForeignKeyDrop,
                                },
                            ]
                        )
                        // 親の表が先に並んでも、外部キーを先に 1 回だけ外す（明示の削除を選んでいれば重ねない）
                        .Expect(e =>
                            e.Sections.Exact(
                                selectForeignKeyDrop
                                    ? "DropForeignKey: #2 DropForeignKey orders fk=FK_orders_customer"
                                    : "DropForeignKey: DropForeignKey orders fk=FK_orders_customer",
                                "DropTable: *"
                            )
                        )
            ),
            .. new[] { ("customer", true), ("orders", false) }.Select(drop =>
                (ScenarioRow<SyncPlanExpectationInput>)
                    Row(
                            $"plan-vanishing-endpoint-rebuild-drop-{(drop.Item2 ? "parent-rebuilds-child" : "child-rebuilds-nothing")}",
                            "消える端: 再構築の方言は親の削除で子を作り直し、子の削除では作り直さない",
                            DialectNames.Sqlite
                        )
                        .Live(s => ExpectationScenarios.CustomerOrders(s))
                        .Items(w =>
                            [
                                new SchemaDiffItem
                                {
                                    Kind = SchemaDiffKind.DropTable,
                                    TableName = drop.Item1,
                                    Entity = w.Live[drop.Item1],
                                    IsSelected = true,
                                },
                            ]
                        )
                        .Expect(e =>
                        {
                            if (drop.Item2)
                            {
                                // 子の表を作り直して外部キーを外す
                                e.Rebuilds.Count("* cols *", 1)
                                    .Rebuilds.Single("orders cols *")
                                    .Rebuilds.Absent("* fk *");
                            }
                            else
                            {
                                e.Rebuilds.Empty();
                            }

                            e.Sections.Exact("DropTable: *");
                        })
            ),
        ];
}
