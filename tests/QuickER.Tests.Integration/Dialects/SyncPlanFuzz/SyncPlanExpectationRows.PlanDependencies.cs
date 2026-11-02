using System.Collections.Generic;
using System.Linq;
using QuickER.Dialects;
using QuickER.Model;
using QuickER.Provider;
using QuickER.Tests.TestSupport.Engines;
using C = QuickER.Tests.Integration.Dialects.SyncPlanFuzz.SyncPlanExpectationCapabilities;
using I = QuickER.Tests.Integration.Dialects.SyncPlanFuzz.ExpectationItems;
using S = QuickER.Tests.Integration.Dialects.SyncPlanFuzz.ExpectationSchema;

namespace QuickER.Tests.Integration.Dialects.SyncPlanFuzz;

/// <summary>
/// 計画の段の期待の行（選んでいない変更への依存：前提にする項目を外して名指しする・主キー列の暗黙の NOT NULL 化・
/// 主キーと同じ列集合の一意制約・主キーに残る列の NULL 許容化）
/// </summary>
internal static partial class SyncPlanExpectationRows
{
    private static IEnumerable<ScenarioRow<SyncPlanExpectationInput>> PlanDependencyRows() =>
        [
            .. new[] { (false, false), (false, true), (true, false), (true, true) }.Select(shape =>
            {
                var (rebuildDialect, selectAddColumn) = shape;
                return (ScenarioRow<SyncPlanExpectationInput>)
                    Row(
                            $"plan-dependency-add-fk-on-new-column-{(rebuildDialect ? "rebuild" : "sequential")}-{(selectAddColumn ? "column-selected" : "column-unselected")}",
                            "依存: 選んでいない列の追加を前提にする外部キーの追加は外して警告する",
                            rebuildDialect ? DialectNames.Sqlite : DialectNames.SqlServer
                        )
                        .Live(s => ExpectationScenarios.CustomerOrders(s))
                        .Items(w =>
                        {
                            var addForeignKey = AddForeignKeyOnNewColumn(w);
                            return
                            [
                                new SchemaDiffItem
                                {
                                    Kind = SchemaDiffKind.AddColumn,
                                    TableName = "orders",
                                    ColumnName = "customer_ref",
                                    Entity = addForeignKey.ChildEntity,
                                    Column = S.Nullable("customer_ref", "INT"),
                                    IsSelected = selectAddColumn,
                                },
                                addForeignKey,
                            ];
                        })
                        .Expect(e =>
                        {
                            if (!selectAddColumn)
                            {
                                e.Sections.Empty()
                                    .Rebuilds.Empty()
                                    .Reorders.Empty()
                                    .Warnings.Exact(
                                        "SelectedItemRequiresUnselectedItem(orders): FK_orders_customer_ref (orders.customer_ref)"
                                    );
                                return;
                            }

                            // 列の追加も選んでいれば、外部キーの追加はそのまま実行する（逐次 DDL はセクション・再構築は畳む）
                            e.Warnings.Empty();

                            if (rebuildDialect)
                            {
                                e.Rebuilds.Contains("* source #1 AddForeignKey *");
                            }
                            else
                            {
                                e.Items.Contains("* > #1 AddForeignKey *");
                            }
                        });
            }),
            Row(
                    "plan-dependency-fk-alter-on-new-column-is-excluded-as-a-whole",
                    "依存: 選んでいない列の追加を前提にする外部キーの作り直しは削除ごと外す",
                    DialectNames.SqlServer
                )
                .Live(s => ExpectationScenarios.CustomerOrders(s))
                .Items(w =>
                {
                    var add = AddForeignKeyOnNewColumn(w);
                    var customer = w.Live["customer"];
                    var orders = w.Live["orders"];
                    return
                    [
                        new SchemaDiffItem
                        {
                            Kind = SchemaDiffKind.AlterForeignKey,
                            TableName = "orders",
                            ColumnName = add.ColumnName,
                            Entity = add.Entity,
                            ParentEntity = customer,
                            ChildEntity = add.ChildEntity,
                            Relationship = add.Relationship,
                            ForeignKeyName = "FK_orders_customer",
                            ForeignKeyColumnPairs = add.ForeignKeyColumnPairs,
                            ReplacedForeignKey = new SchemaDiffItem
                            {
                                Kind = SchemaDiffKind.DropForeignKey,
                                TableName = "orders",
                                Entity = orders,
                                ParentEntity = customer,
                                ChildEntity = orders,
                                Relationship = w.Live.Relationships[0],
                                ForeignKeyName = "FK_orders_customer",
                                ForeignKeyColumnPairs = [new("id", "customer_id")],
                            },
                            IsSelected = true,
                        },
                    ];
                })
                // 削除だけが走って外部キーが消えたまま残らない
                .Expect(e =>
                    e.Sections.Empty()
                        .Rebuilds.Empty()
                        .Reorders.Empty()
                        .Warnings.Exact("*: FK_orders_customer (orders.customer_ref)")
                ),
            .. new[] { false, true }.Select(rebuildDialect =>
                (ScenarioRow<SyncPlanExpectationInput>)
                    Row(
                            $"plan-dependency-pk-and-unique-on-new-column-are-excluded-{(rebuildDialect ? "rebuild" : "sequential")}",
                            "依存: 選んでいない列の追加を前提にする主キーの変更・一意制約の追加は外して警告する",
                            rebuildDialect ? DialectNames.Sqlite : DialectNames.SqlServer
                        )
                        .Live(s =>
                        {
                            ExpectationScenarios.CustomerOrders(s);
                            s["customer"].Columns.Add(S.Nullable("name", "INT"));
                        })
                        .Items(w =>
                        {
                            var target = w.Live["customer"].Clone(preserveId: false);
                            target.Columns.Add(S.NotNull("code", "INT"));
                            target.Columns.Single(c => c.Name == "id").IsPrimaryKey = false;
                            target.Columns.Single(c => c.Name == "name").IsPrimaryKey = true;
                            target.Columns.Single(c => c.Name == "code").IsPrimaryKey = true;
                            return
                            [
                                I.AlterPk("customer", target),
                                new SchemaDiffItem
                                {
                                    Kind = SchemaDiffKind.AddUniqueConstraint,
                                    TableName = "customer",
                                    Entity = target,
                                    UniqueConstraintColumns = ["code"],
                                    IsSelected = true,
                                },
                            ];
                        })
                        // SQLite の再構築は無い列を読み飛ばし、部分的な主キーのまま成功していた
                        .Expect(e =>
                            e.Sections.Empty()
                                .Rebuilds.Empty()
                                .Reorders.Empty()
                                .Warnings.Unordered(
                                    "*: PRIMARY KEY (customer.code)",
                                    "*: UQ_customer_code (customer.code)"
                                )
                        )
            ),
            .. new[] { false, true }.Select(rebuildDialect =>
                (ScenarioRow<SyncPlanExpectationInput>)
                    Row(
                            $"plan-dependency-unique-alter-on-new-column-is-excluded-{(rebuildDialect ? "rebuild" : "sequential")}",
                            "依存: 選んでいない列の追加を前提にする一意制約の作り直しは、削除ごと外して警告する",
                            rebuildDialect ? DialectNames.Sqlite : DialectNames.SqlServer
                        )
                        .Live(s =>
                        {
                            ExpectationScenarios.CustomerOrders(s);
                            s["customer"].Columns.Add(S.Nullable("name", "INT"));
                            s.Unique("customer", "UQ_customer_key", "name");
                        })
                        // 展開の前に判定する＝削除だけが走って一意制約が消えたまま残らない
                        .Items(w =>
                            [
                                new SchemaDiffItem
                                {
                                    Kind = SchemaDiffKind.AlterUniqueConstraint,
                                    TableName = "customer",
                                    Entity = w.Live["customer"],
                                    UniqueConstraintName = "UQ_customer_key",
                                    UniqueConstraintColumns = ["name", "code"],
                                    ReplacedUniqueConstraint = I.Unique(
                                        SchemaDiffKind.DropUniqueConstraint,
                                        "customer",
                                        "UQ_customer_key",
                                        ["name"]
                                    ),
                                    IsSelected = true,
                                },
                            ]
                        )
                        .Expect(e =>
                            e.Sections.Empty()
                                .Rebuilds.Empty()
                                .Reorders.Empty()
                                .Warnings.Count("SelectedItemRequiresUnselectedItem*", 1)
                                .Warnings.Single(
                                    "SelectedItemRequiresUnselectedItem(*): UQ_customer_key (customer.code)"
                                )
                        )
            ),
            .. new[] { false, true }.Select(selectAlterColumn =>
                (ScenarioRow<SyncPlanExpectationInput>)
                    Row(
                            $"plan-dependency-pk-change-nullable-new-key-column-is-made-not-null-{(selectAlterColumn ? "alter-selected" : "alter-unselected")}",
                            "主キー列の NOT NULL 化: 新しく主キーに加わる NULL 許容の列は暗黙に NOT NULL にする",
                            DialectNames.SqlServer
                        )
                        .Live(s =>
                        {
                            ExpectationScenarios.CustomerOrders(s);
                            s["customer"].Columns.Add(S.Nullable("code", "INT"));
                        })
                        .Items(w =>
                        {
                            var target = w.Live["customer"].Clone(preserveId: false);
                            var code = target.Columns.Single(c => c.Name == "code");
                            code.IsPrimaryKey = true;
                            code.IsNullable = false;
                            target.Columns.Single(c => c.Name == "id").IsPrimaryKey = true;
                            List<SchemaDiffItem> items = [I.AlterPk("customer", target)];

                            if (selectAlterColumn)
                            {
                                items.Add(
                                    new SchemaDiffItem
                                    {
                                        Kind = SchemaDiffKind.AlterColumn,
                                        TableName = "customer",
                                        ColumnName = "code",
                                        Entity = target,
                                        Column = code,
                                        OldColumn = w.Live.Col("customer", "code"),
                                        IsSelected = true,
                                    }
                                );
                            }

                            return items;
                        })
                        // 列の NOT NULL 化を選んでいなくても live の型のまま NOT NULL にし、選んでいれば重ねて足さない
                        .Expect(e =>
                            e.Sections.Count("AlterColumn: *", 1)
                                .Items.Count("AlterColumn > *", 1)
                                .Items.Single(
                                    "AlterColumn > *AlterColumn customer.code sel=* col=INT notnull*"
                                )
                                .Warnings.Absent("SelectedItemRequiresUnselectedItem*")
                        )
            ),
            .. new[]
            {
                ("rebuild-pk-change-unselected-is-excluded", true, false, true),
                ("rebuild-pk-change-selected-runs", true, true, false),
                ("sequential-runs", false, false, false),
            }.Select(shape =>
            {
                var (name, rebuildDialect, selectPrimaryKeyChange, excluded) = shape;
                return (ScenarioRow<SyncPlanExpectationInput>)
                    Row(
                            $"plan-dependency-unique-on-staying-pk-{name}",
                            "依存: 同期後の主キーと同じ列集合の一意制約は、持てない方言では外して警告する",
                            rebuildDialect ? DialectNames.Sqlite : DialectNames.SqlServer
                        )
                        .Live(s =>
                        {
                            ExpectationScenarios.CustomerOrders(s);
                            s["customer"].Columns.Add(S.NotNull("code", "INT"));
                        })
                        .Items(w =>
                        {
                            var target = w.Live["customer"].Clone(preserveId: false);
                            target.Columns.Single(c => c.Name == "id").IsPrimaryKey = false;
                            target.Columns.Single(c => c.Name == "code").IsPrimaryKey = true;
                            return
                            [
                                new SchemaDiffItem
                                {
                                    Kind = SchemaDiffKind.AddUniqueConstraint,
                                    TableName = "customer",
                                    Entity = target,
                                    UniqueConstraintColumns = ["id"],
                                    IsSelected = true,
                                },
                                I.AlterPk("customer", target, selected: selectPrimaryKeyChange),
                            ];
                        })
                        .Expect(e =>
                        {
                            if (excluded)
                            {
                                e.Sections.Empty()
                                    .Rebuilds.Empty()
                                    .Reorders.Empty()
                                    .Warnings.Exact("*: UQ_customer_id (PRIMARY KEY)");
                                return;
                            }

                            // 主キーを別の列へ移す変更を選んでいれば、持てない方言でも実行する（持てる方言は選ばなくても実行する）
                            e.Warnings.Absent("SelectedItemRequiresUnselectedItem*");

                            if (rebuildDialect)
                            {
                                e.Rebuilds.Contains("* source #0 AddUniqueConstraint *");
                            }
                            else
                            {
                                e.Items.Contains("* > #0 AddUniqueConstraint *");
                            }
                        });
            }),
            .. new[] { ("INT", false), ("BIGINT", true) }.Select(shape =>
            {
                var (newType, keepsTypeChange) = shape;
                return (ScenarioRow<SyncPlanExpectationInput>)
                    Row(
                            $"plan-dependency-nullable-on-staying-pk-{(keepsTypeChange ? "keeps-type-change" : "is-dropped")}",
                            "依存: 主キーに残る列の NULL 許容化は外し、型の変更は残す",
                            DialectNames.SqlServer
                        )
                        .Live(s => ExpectationScenarios.CustomerOrders(s))
                        .Items(w =>
                            [
                                new SchemaDiffItem
                                {
                                    Kind = SchemaDiffKind.AlterColumn,
                                    TableName = "customer",
                                    ColumnName = "id",
                                    Entity = w.Live["customer"],
                                    Column = S.Nullable("id", newType),
                                    OldColumn = w.Live.Col("customer", "id"),
                                    IsSelected = true,
                                },
                            ]
                        )
                        .Expect(e =>
                        {
                            e.Warnings.Count("SelectedItemRequiresUnselectedItem*", 1)
                                .Warnings.Single(
                                    "SelectedItemRequiresUnselectedItem(*): id (PRIMARY KEY)"
                                );

                            if (keepsTypeChange)
                            {
                                e.Sections.Count("AlterColumn: *", 1)
                                    .Items.Count("AlterColumn > *", 1)
                                    .Items.Single("AlterColumn > * col=BIGINT notnull*");
                            }
                            else
                            {
                                e.Sections.Absent("AlterColumn: *");
                            }
                        });
            }),
        ];

    /// <summary>
    /// DB 側の orders へ足す（差分では AddColumn になる）列 customer_ref の外部キー FK_orders_customer_ref の追加項目を作る
    /// （子は orders の複製に列を足したもの・親は DB 側の customer）
    /// </summary>
    private static SchemaDiffItem AddForeignKeyOnNewColumn(ExpectationWorld w)
    {
        var target = w.Live["orders"].Clone(preserveId: false);
        target.Columns.Add(S.Nullable("customer_ref", "INT"));

        return new SchemaDiffItem
        {
            Kind = SchemaDiffKind.AddForeignKey,
            TableName = "orders",
            ColumnName = "customer_ref",
            Entity = target,
            ParentEntity = w.Live["customer"],
            ChildEntity = target,
            Relationship = new Relationship(),
            ForeignKeyName = "FK_orders_customer_ref",
            ForeignKeyColumnPairs = [new("id", "customer_ref")],
            IsSelected = true,
        };
    }
}
