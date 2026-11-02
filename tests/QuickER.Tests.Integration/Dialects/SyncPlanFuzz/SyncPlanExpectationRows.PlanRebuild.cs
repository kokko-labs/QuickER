using System.Collections.Generic;
using QuickER.Dialects;
using QuickER.Model;
using QuickER.Provider;
using QuickER.Tests.TestSupport.Engines;
using C = QuickER.Tests.Integration.Dialects.SyncPlanFuzz.SyncPlanExpectationCapabilities;
using I = QuickER.Tests.Integration.Dialects.SyncPlanFuzz.ExpectationItems;
using S = QuickER.Tests.Integration.Dialects.SyncPlanFuzz.ExpectationSchema;

namespace QuickER.Tests.Integration.Dialects.SyncPlanFuzz;

/// <summary>
/// 計画の段の期待の行（テーブル再構築の方言の集約＝合成スキーマ・外部キーの集合・新しい表への外部キーの畳み込み・主キーの変更）
/// </summary>
internal static partial class SyncPlanExpectationRows
{
    private static IEnumerable<ScenarioRow<SyncPlanExpectationInput>> PlanRebuildRows() =>
        [
            Row(
                    "plan-rebuild-null-context-throws",
                    "再構築方言で context 省略は例外",
                    DialectNames.Sqlite
                )
                .Items(_ => [I.Item(SchemaDiffKind.AlterColumn)])
                .NoContext()
                .Plan()
                .Expect(e => e.Exception.Single("InvalidOperationException: *")),
            Row(
                    "plan-rebuild-alter-column-synthesizes-definition",
                    "AlterColumn は合成スキーマのテーブル再構築になる",
                    DialectNames.Sqlite
                )
                .Live(s => s.Tbl("orders", ("id", "INT", true), ("note", "TEXT", false)))
                .Items(w =>
                    [
                        new SchemaDiffItem
                        {
                            Kind = SchemaDiffKind.AlterColumn,
                            TableName = "orders",
                            ColumnName = "note",
                            Column = S.NotNull("note", "NVARCHAR(200)"),
                            OldColumn = w.Live.Col("orders", "note"),
                            IsSelected = true,
                        },
                    ]
                )
                // live と合成後の両方にある列だけが移送の対象
                .Expect(e =>
                    e.Sections.Empty()
                        .Rebuilds.Count("* cols *", 1)
                        .Rebuilds.Single(
                            "orders rebuild",
                            "orders cols id:INT notnull pk live, note:NVARCHAR(200) notnull live",
                            "orders copy (id,note)"
                        )
                ),
            Row(
                    "plan-rebuild-unselected-changes-do-not-leak",
                    "未選択の変更は合成スキーマへ紛れ込まない",
                    DialectNames.Sqlite
                )
                .Live(s =>
                    s.Tbl("t", ("id", "INT", true), ("a", "INT", false), ("b", "INT", false))
                )
                .Items(_ =>
                    [
                        new SchemaDiffItem
                        {
                            Kind = SchemaDiffKind.AlterColumn,
                            TableName = "t",
                            ColumnName = "a",
                            Column = S.Nullable("a", "BIGINT"),
                            IsSelected = true,
                        },
                        // b の削除は未選択 → 合成後も b が残る
                        new SchemaDiffItem
                        {
                            Kind = SchemaDiffKind.DropColumn,
                            TableName = "t",
                            ColumnName = "b",
                            Column = S.Nullable("b", "INT"),
                            IsSelected = false,
                        },
                    ]
                )
                .Expect(e =>
                    e.Rebuilds.Count("* cols *", 1)
                        .Rebuilds.Single(
                            "t cols id:INT notnull pk live, a:BIGINT null live, b:INT null live"
                        )
                ),
            Row(
                    "plan-rebuild-folds-selected-add-column",
                    "再構築対象の AddColumn は末尾へ畳み込まれる",
                    DialectNames.Sqlite
                )
                .Live(s => s.Tbl("t", ("id", "INT", true), ("a", "INT", false)))
                .Items(_ =>
                    [
                        new SchemaDiffItem
                        {
                            Kind = SchemaDiffKind.AlterColumn,
                            TableName = "t",
                            ColumnName = "a",
                            Column = S.Nullable("a", "BIGINT"),
                            IsSelected = true,
                        },
                        new SchemaDiffItem
                        {
                            Kind = SchemaDiffKind.AddColumn,
                            TableName = "t",
                            ColumnName = "c",
                            Column = S.Nullable("c", "TEXT"),
                            IsSelected = true,
                        },
                    ]
                )
                // 新しい列 c は live に無いため移送の対象外
                .Expect(e =>
                    e.Sections.Empty()
                        .Rebuilds.Count("* cols *", 1)
                        .Rebuilds.Single(
                            "t cols id:INT notnull pk live, a:BIGINT null live, c:TEXT null",
                            "t copy (id,a)"
                        )
                ),
            Row(
                    "plan-rebuild-add-column-on-non-rebuild-table-stays-in-sections",
                    "非再構築テーブルの AddColumn はセクションに残る",
                    DialectNames.Sqlite
                )
                .Items(_ =>
                    [
                        new SchemaDiffItem
                        {
                            Kind = SchemaDiffKind.AddColumn,
                            TableName = "t",
                            ColumnName = "c",
                            Column = S.Nullable("c", "TEXT"),
                            IsSelected = true,
                        },
                    ]
                )
                .Expect(e => e.Rebuilds.Empty().Sections.Exact("AddColumn: #0 AddColumn t.c")),
            Row(
                    "plan-rebuild-new-table-fk-is-inlined-into-create-only",
                    "新規テーブルへの FK は CreateOnly にインライン化される",
                    DialectNames.Sqlite
                )
                .Live(s => s.Tbl("customer", ("id", "INT", true)))
                .Items(w =>
                {
                    var parent = w.Live["customer"];
                    var child = S.Detached(
                        "orders",
                        S.Pk("id", "INT"),
                        S.Nullable("customer_id", "INT")
                    );
                    var rel = new Relationship
                    {
                        SourceEntityId = parent.Id,
                        TargetEntityId = child.Id,
                        ColumnPairs = [new(parent.Columns[0].Id, child.Columns[1].Id)],
                    };

                    return
                    [
                        new SchemaDiffItem
                        {
                            Kind = SchemaDiffKind.AddTable,
                            TableName = "orders",
                            Entity = child,
                            IsSelected = true,
                        },
                        new SchemaDiffItem
                        {
                            Kind = SchemaDiffKind.AddForeignKey,
                            TableName = "orders",
                            ColumnName = "customer_id",
                            ForeignKeyColumnPairs = [new("id", "customer_id")],
                            ParentEntity = parent,
                            ChildEntity = child,
                            Relationship = rel,
                            IsSelected = true,
                        },
                    ];
                })
                // AddTable も AddForeignKey もセクションには出ない（CreateOnly へ畳まれる）
                .Expect(e =>
                    e.Sections.Empty()
                        .Rebuilds.Count("* cols *", 1)
                        .Rebuilds.Count("orders fk *", 1)
                        .Rebuilds.Single(
                            "orders create-only",
                            "orders copy ()",
                            "orders fk * (customer_id) -> customer(id) *"
                        )
                ),
            Row(
                    "plan-rebuild-fk-set-is-live-minus-drop-plus-add",
                    "既存テーブルの FK 集合は live から Drop を除き Add を足す",
                    DialectNames.Sqlite
                )
                .Live(s =>
                {
                    s.Tbl("customer", ("id", "INT", true));
                    s.Tbl("supplier", ("id", "INT", true));
                    s.Tbl("orders", ("id", "INT", true), ("ref_id", "INT", false));
                    // live FK: orders.ref_id -> customer.id
                    s.Fk("customer", "orders", "FK_orders_customer_0", ("id", "ref_id"));
                })
                .Items(w =>
                {
                    var customer = w.Live["customer"];
                    var supplier = w.Live["supplier"];
                    var orders = w.Live["orders"];
                    // 目標 FK: orders.ref_id -> supplier.id
                    var addRel = new Relationship
                    {
                        SourceEntityId = supplier.Id,
                        TargetEntityId = orders.Id,
                        ColumnPairs = [new(supplier.Columns[0].Id, orders.Columns[1].Id)],
                    };

                    return
                    [
                        new SchemaDiffItem
                        {
                            Kind = SchemaDiffKind.DropForeignKey,
                            TableName = "orders",
                            ParentEntity = customer,
                            ChildEntity = orders,
                            Relationship = w.Live.Relationships[0],
                            ForeignKeyName = "FK_orders_customer_0",
                            IsSelected = true,
                        },
                        new SchemaDiffItem
                        {
                            Kind = SchemaDiffKind.AddForeignKey,
                            TableName = "orders",
                            ColumnName = "ref_id",
                            ForeignKeyColumnPairs = [new("id", "ref_id")],
                            ParentEntity = supplier,
                            ChildEntity = orders,
                            Relationship = addRel,
                            IsSelected = true,
                        },
                    ];
                })
                // live の customer FK は削除され、supplier FK だけが残る
                .Expect(e =>
                    e.Sections.Empty()
                        .Rebuilds.Count("* cols *", 1)
                        .Rebuilds.Count("orders fk *", 1)
                        .Rebuilds.Single("orders fk * (ref_id) -> supplier(id) *")
                ),
            Row(
                    "plan-rebuild-groups-by-table",
                    "再構築はテーブル単位でグループ化される",
                    DialectNames.Sqlite
                )
                .Live(s =>
                {
                    s.Tbl("t1", ("id", "INT", true), ("a", "INT", false));
                    s.Tbl("t2", ("id", "INT", true), ("b", "INT", false));
                })
                .Items(_ =>
                    [
                        new SchemaDiffItem
                        {
                            Kind = SchemaDiffKind.AlterColumn,
                            TableName = "t1",
                            ColumnName = "a",
                            Column = S.Nullable("a", "BIGINT"),
                            IsSelected = true,
                        },
                        new SchemaDiffItem
                        {
                            Kind = SchemaDiffKind.DropColumn,
                            TableName = "t2",
                            ColumnName = "b",
                            Column = S.Nullable("b", "INT"),
                            IsSelected = true,
                        },
                    ]
                )
                .Expect(e =>
                    e.Rebuilds.Count("* cols *", 2)
                        .Rebuilds.Single("t1 cols *", "t2 cols id:INT notnull pk live")
                ),
            Row(
                    "plan-rebuild-fk-to-unselected-new-table-is-excluded-with-warning",
                    "AddTable 未選択の新規テーブルへの FK は例外にせず計画から外して警告する",
                    DialectNames.Sqlite
                )
                .Live(s => s.Tbl("customers", ("id", "INT", true)))
                // orders は live に無く、AddTable も未選択（＝計画に含まれない）
                .Items(w =>
                    [
                        I.Item(SchemaDiffKind.AddTable, selected: false, table: "orders"),
                        new SchemaDiffItem
                        {
                            Kind = SchemaDiffKind.AddForeignKey,
                            TableName = "orders",
                            ColumnName = "customer_id",
                            ForeignKeyColumnPairs = [new("id", "customer_id")],
                            ParentEntity = w.Live["customers"],
                            ChildEntity = S.Detached(
                                "orders",
                                S.Pk("id", "INT"),
                                S.Nullable("customer_id", "INT")
                            ),
                            IsSelected = true,
                        },
                    ]
                )
                .Expect(e =>
                    e.Rebuilds.Empty()
                        .Sections.Empty()
                        .Warnings.Exact(
                            "SelectedItemRequiresUnselectedItem(orders): FK_orders_customers (orders)"
                        )
                ),
            Row(
                    "plan-rebuild-unresolvable-fk-on-new-table-stays-in-sections",
                    "解決できない FK は CreateOnly へ畳まずセクションへ残す",
                    DialectNames.Sqlite
                )
                // 親に主キーが無く、FK 列も未指定 → 解決不能
                .Live(s => s.Table("no_pk", S.Nullable("code", "INT")))
                .Items(w =>
                {
                    var newChild = S.Detached("orders", S.Pk("id", "INT"));

                    return
                    [
                        new SchemaDiffItem
                        {
                            Kind = SchemaDiffKind.AddTable,
                            TableName = "orders",
                            Entity = newChild,
                            IsSelected = true,
                        },
                        new SchemaDiffItem
                        {
                            Kind = SchemaDiffKind.AddForeignKey,
                            TableName = "orders",
                            ColumnName = null,
                            ParentEntity = w.Live["no_pk"],
                            ChildEntity = newChild,
                            IsSelected = true,
                        },
                    ];
                })
                .Expect(e =>
                    e.Rebuilds.Count("* cols *", 1)
                        .Rebuilds.Absent("orders fk *")
                        .Rebuilds.Count("orders source *", 1)
                        .Rebuilds.Single("orders create-only", "orders source #0 AddTable orders")
                        .Sections.Exact("AddForeignKey: #1 AddForeignKey orders")
                ),
            Row(
                    "plan-rebuild-composite-fk-child-table-keeps-all-pairs",
                    "SQLite: 複合外部キーの子テーブルも通常どおり再構築される",
                    DialectNames.Sqlite
                )
                .Live(s =>
                {
                    s.Tbl("orders", ("id", "INT", true), ("line_no", "INT", false));
                    s.Tbl(
                        "order_line",
                        ("id", "INT", true),
                        ("order_id", "INT", false),
                        ("line_no", "INT", false),
                        ("note", "TEXT", false)
                    );
                    s.Fk(
                        "orders",
                        "order_line",
                        "FK_order_line_orders",
                        ("id", "order_id"),
                        ("line_no", "line_no")
                    );
                    s.Tbl("memo", ("id", "INT", true), ("note", "TEXT", false));
                })
                .Items(_ =>
                    [
                        new SchemaDiffItem
                        {
                            Kind = SchemaDiffKind.AlterColumn,
                            TableName = "order_line",
                            ColumnName = "note",
                            Column = S.Nullable("note", "INT"),
                            IsSelected = true,
                        },
                        new SchemaDiffItem
                        {
                            Kind = SchemaDiffKind.AlterColumn,
                            TableName = "memo",
                            ColumnName = "note",
                            Column = S.Nullable("note", "INT"),
                            IsSelected = true,
                        },
                    ]
                )
                // 両テーブルとも再構築され（複合外部キーによるブロックは無い）、外部キーは全構成列を保つ。
                // 全項目が再構築へ畳まれるためセクションは残らない
                .Expect(e =>
                    e.Rebuilds.Count("* cols *", 2)
                        .Rebuilds.Count("order_line fk *", 1)
                        .Rebuilds.Single(
                            "order_line cols *",
                            "memo cols *",
                            "order_line fk FK_order_line_orders (order_id,line_no) -> orders(id,line_no) *"
                        )
                        .Sections.Empty()
                        .Warnings.Empty()
                ),
            Row(
                    "plan-rebuild-alter-pk-is-folded-into-rebuild",
                    "SQLite: AlterPrimaryKey は再構築へ畳まれ NewDefinition に PK が反映される",
                    DialectNames.Sqlite
                )
                .Live(s =>
                    s.Tbl("t", ("id", "INT", true), ("code", "INT", false), ("note", "TEXT", false))
                )
                // 目標：id は主キーでなくなり、code が主キーになる
                .Items(_ =>
                    [
                        I.AlterPk(
                            "t",
                            S.Detached(
                                "t",
                                S.Nullable("id", "INT"),
                                S.Pk("code", "INT"),
                                S.Nullable("note", "TEXT")
                            )
                        ),
                    ]
                )
                // 主キーの変更は列の集合を変えないため、全列が移送の対象のまま
                .Expect(e =>
                    e.Sections.Empty()
                        .Rebuilds.Count("* cols *", 1)
                        .Rebuilds.Single(
                            "t rebuild",
                            "t cols id:INT notnull live, code:INT notnull pk live, note:TEXT null live",
                            "t copy (id,code,note)"
                        )
                ),
        ];
}
