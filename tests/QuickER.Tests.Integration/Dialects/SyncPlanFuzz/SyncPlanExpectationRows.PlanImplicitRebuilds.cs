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
/// 計画の段の期待の行（主キー・一意制約・外部キーの暗黙の外し直し：主キーの構成列の型の変更・自表の外部キー・外部キーを支える
/// 一意制約・同じ列ペアの外部キー 2 本・旧主キーに支えられた外部キーだけ・消える構成列・名前の無い外部キーの付け直しの名前）
/// </summary>
internal static partial class SyncPlanExpectationRows
{
    private static IEnumerable<ScenarioRow<SyncPlanExpectationInput>> PlanImplicitRebuildRows() =>
        [
            Row(
                    "plan-implicit-pk-rebuild-on-pk-column-type-change",
                    "暗黙の主キー: 主キー列の型変更で同じ構成の主キーを外して付け直す",
                    DialectNames.SqlServer
                )
                .Live(s => s.Add(ExpectationScenarios.PairTable(["b", "a"])))
                .Items(_ =>
                    [
                        new SchemaDiffItem
                        {
                            Kind = SchemaDiffKind.AlterColumn,
                            TableName = "pair",
                            ColumnName = "a",
                            Column = S.Pk("a", "BIGINT"),
                            IsSelected = true,
                        },
                    ]
                )
                // 「解除 → 列定義変更 → 付与」の 3 フェーズ。付け直す主キーは live の構成・順序のまま
                .Expect(e =>
                    e.Sections.Exact(
                            "AlterPrimaryKey/Drop: *",
                            "AlterColumn: *",
                            "AlterPrimaryKey/Add: *"
                        )
                        .Items.Count("AlterPrimaryKey/Add > *", 1)
                        .Items.Single("AlterPrimaryKey/Add > * pk=(b,a) *")
                ),
            Row(
                    "plan-implicit-pk-rebuild-not-duplicated-with-selected-pk-change",
                    "暗黙の主キー: 主キー変更を選んでいれば重ねて注入しない",
                    DialectNames.SqlServer
                )
                .Live(s => s.Add(ExpectationScenarios.PairTable(["a", "b"])))
                .Items(_ =>
                    [
                        new SchemaDiffItem
                        {
                            Kind = SchemaDiffKind.AlterColumn,
                            TableName = "pair",
                            ColumnName = "a",
                            Column = S.Nullable("a", "BIGINT"),
                            IsSelected = true,
                        },
                        I.AlterPk("pair", ExpectationScenarios.PairTable(["b", "a"])),
                    ]
                )
                // 主キーの変更のセクションはどれも項目がちょうど 1 つ（2 つ以上なら「; 」が、空なら行末の「: 」が現れる）
                .Expect(e => e.Sections.Absent("AlterPrimaryKey*;*", "AlterPrimaryKey*: ")),
            Row(
                    "plan-implicit-key-rebuild-mysql-like-does-not-rebuild-keys",
                    "暗黙の主キー: MySQL 相当では主キー・一意制約を外し直さない",
                    DialectNames.MySql
                )
                .Live(s =>
                {
                    s.Add(ExpectationScenarios.PairTable(["a", "b"]));
                    s.Unique("pair", "UQ_pair_memo", "memo");
                })
                .Items(_ =>
                    [
                        new SchemaDiffItem
                        {
                            Kind = SchemaDiffKind.AlterColumn,
                            TableName = "pair",
                            ColumnName = "a",
                            Column = S.Nullable("a", "BIGINT"),
                            IsSelected = true,
                        },
                        new SchemaDiffItem
                        {
                            Kind = SchemaDiffKind.AlterColumn,
                            TableName = "pair",
                            ColumnName = "memo",
                            Column = S.Nullable("memo", "VARCHAR(20)"),
                            IsSelected = true,
                        },
                    ]
                )
                .Expect(e => e.Sections.Exact("AlterColumn: *")),
            Row(
                    "plan-implicit-fk-own-fk-on-leading-pk-columns-rebuilt-for-mysql-like",
                    "自表の外部キー: MySQL 相当では旧主キーの先頭に乗る外部キーを外し直す",
                    DialectNames.MySql,
                    DialectNames.SqlServer
                )
                .Live(s =>
                {
                    s.Tbl("p", ("id", "INT", true));
                    s.Tbl("q", ("id", "INT", true));
                    s.Tbl(
                        "link",
                        ("p_id", "INT", true),
                        ("x", "INT", true),
                        ("q_id", "INT", false)
                    );
                    s.Fk("p", "link", "FK_link_p", ("id", "p_id"));
                    s.Fk("q", "link", "FK_link_q", ("id", "q_id"));
                })
                // 主キーを (p_id, x) から (x, p_id) へ並べ替える
                .Items(w =>
                {
                    var target = w.Live["link"].Clone(preserveId: true);
                    target.PrimaryKeyColumnIds = [target.Columns[1].Id, target.Columns[0].Id];
                    return [I.AlterPk("link", target)];
                })
                .Expect(e =>
                    e.For(
                            DialectNames.MySql,
                            x =>
                                x.Sections.Count("DropForeignKey: *", 1)
                                    .Items.Count("DropForeignKey > *", 1)
                                    .Items.Single("DropForeignKey > * fk=FK_link_p sel=*")
                        )
                        .For(DialectNames.SqlServer, x => x.Sections.Absent("DropForeignKey: *"))
                ),
            Row(
                    "plan-implicit-fk-unique-supporting-fk-drop-rebuilds-fk-for-mysql-like",
                    "索引の喪失: 外部キーを支える一意制約の削除で、その外部キーを外して付け直す（MySQL 相当）",
                    DialectNames.MySql,
                    DialectNames.SqlServer
                )
                .Live(s =>
                {
                    ExpectationScenarios.CustomerOrders(s);
                    s.Unique("orders", "UQ_orders_customer", "customer_id");
                })
                .Items(_ =>
                    [
                        I.Unique(
                            SchemaDiffKind.DropUniqueConstraint,
                            "orders",
                            "UQ_orders_customer",
                            ["customer_id"]
                        ),
                    ]
                )
                // 索引を要しない方言では外し直さない（対照）
                .Expect(e =>
                    e.For(
                            DialectNames.MySql,
                            x =>
                                x.Sections.Exact(
                                    "DropForeignKey: *",
                                    "DropUniqueConstraint: *",
                                    "AddForeignKey: *"
                                )
                        )
                        .For(
                            DialectNames.SqlServer,
                            x => x.Sections.Exact("DropUniqueConstraint: *")
                        )
                ),
            Row(
                    "plan-implicit-fk-drop-one-of-same-pair-other-is-still-rebuilt",
                    "外部キーの照合: 同じ列ペアの 2 本の片方を外しても、もう 1 本は列の変更に合わせて外し直す",
                    DialectNames.MySql
                )
                .Live(s => ExpectationScenarios.TwoForeignKeysOnSameColumn(s, false))
                // 両端の型を BIGINT へ変え、目標の図には FK_a だけを残す（FK_b の削除と列の変更を選ぶ）
                .TargetWith(w =>
                {
                    w.Target.Entities.AddRange(
                        w.Live.Entities.Select(e => e.Clone(preserveId: true))
                    );
                    w.Target["orders"].Columns[1].DataType = "BIGINT";
                    w.Target["customer"].Columns[0].DataType = "BIGINT";
                    var relA = w.Live.Relationships[0];
                    w.Target.Relationships.Add(
                        new QuickER.Model.Relationship
                        {
                            SourceEntityId = relA.SourceEntityId,
                            TargetEntityId = relA.TargetEntityId,
                            ColumnPairs = [.. relA.ColumnPairs.Select(p => p.Clone())],
                            ConstraintName = relA.ConstraintName,
                        }
                    );
                })
                .Select(items =>
                {
                    foreach (var item in items)
                    {
                        item.IsSelected =
                            item.Kind
                                is SchemaDiffKind.AlterColumn
                                    or SchemaDiffKind.DropForeignKey;
                    }
                })
                .Expect(e =>
                    e.Sections.Count("DropForeignKey: *", 1)
                        .Items.Count("DropForeignKey > *", 2)
                        .Items.Single(
                            "DropForeignKey > * fk=FK_a sel=*",
                            "DropForeignKey > * fk=FK_b sel=*"
                        )
                        .Sections.Count("AddForeignKey: *", 1)
                        .Items.Count("AddForeignKey > *", 1)
                        .Items.Single("AddForeignKey > * fk=FK_a sel=*")
                        .Warnings.Empty()
                ),
            Row(
                    "plan-implicit-fk-pk-change-skips-fk-on-unique-constraint",
                    "主キー変更: 外し直すのは旧主キーに支えられた外部キーだけ（MySQL は先頭の列だけを参照するものも）",
                    DialectNames.SqlServer
                )
                // 一意制約 (code) を参照する外部キーは、主キー (id) → (id, code) の変更に巻き込まれない
                // （code は最初から NOT NULL＝主キーへ加える NOT NULL 化の列の変更が外し直しを呼ばない形にする）
                .Live(s =>
                {
                    s.Table("customer", S.Pk("id", "INT"), S.NotNull("code", "INT"));
                    s.Unique("customer", "UQ_customer_code", "code");
                    s.Tbl(
                        "orders",
                        ("id", "INT", true),
                        ("customer_id", "INT", false),
                        ("customer_code", "INT", false)
                    );
                    s.Fk("customer", "orders", "FK_orders_customer", ("code", "customer_code"));
                })
                .Items(w =>
                {
                    var widened = w.Live["customer"].Clone(preserveId: true);
                    widened.Columns[1].IsPrimaryKey = true;
                    widened.Columns[1].IsNullable = false;
                    return [I.AlterPk("customer", widened)];
                })
                .Expect(e => e.Sections.Absent("DropForeignKey: *")),
            Row(
                    "plan-implicit-fk-pk-change-rebuilds-fk-on-old-pk-prefix-for-mysql-like",
                    "主キー変更: 外し直すのは旧主キーに支えられた外部キーだけ（MySQL は先頭の列だけを参照するものも）",
                    DialectNames.MySql,
                    DialectNames.SqlServer
                )
                // 旧主キー (id, code) の先頭の列 id だけを参照する外部キー
                .Live(s =>
                {
                    ExpectationScenarios.CustomerOrders(s);
                    s["customer"].Columns.Add(S.Pk("code", "INT"));
                })
                .Items(w =>
                {
                    var codeOnly = w.Live["customer"].Clone(preserveId: true);
                    codeOnly.Columns[0].IsPrimaryKey = false;
                    return [I.AlterPk("customer", codeOnly)];
                })
                .Expect(e =>
                    e.For(
                            DialectNames.MySql,
                            x =>
                                x.Sections.Count("DropForeignKey: *", 1)
                                    .Items.Count("DropForeignKey > *", 1)
                                    .Items.Single("DropForeignKey > * fk=FK_orders_customer sel=*")
                        )
                        .For(DialectNames.SqlServer, x => x.Sections.Absent("DropForeignKey: *"))
                ),
            Row(
                    "plan-implicit-unique-rebuild-with-dropped-column-drops-only",
                    "暗黙の一意制約: 構成列が消える一意制約は外すだけで付け直さない",
                    DialectNames.SqlServer
                )
                .Live(s =>
                {
                    s.Tbl("t", ("id", "INT", true), ("a", "INT", false), ("b", "INT", false));
                    s.Unique("t", "UQ_t_ab", "a", "b");
                })
                .Items(w =>
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
                            Kind = SchemaDiffKind.DropColumn,
                            TableName = "t",
                            ColumnName = "b",
                            Entity = w.Live["t"],
                            Column = w.Live["t"].Columns[2],
                            IsSelected = true,
                        },
                    ]
                )
                .Expect(e =>
                    e.Sections.Contains("DropUniqueConstraint: *")
                        .Sections.Absent("AddUniqueConstraint: *")
                ),
            Row(
                    "plan-implicit-unique-drop-warns-for-surviving-fk",
                    "一意制約: 削除の警告は、同期で消える外部キーには出さない",
                    DialectNames.SqlServer
                )
                .Live(CustomerOrdersWithUniqueId)
                .Items(_ => [DropCustomerIdUnique()])
                .Expect(e => e.Warnings.Count("UniqueConstraintDropMayBreakForeignKey*", 1)),
            Row(
                    "plan-implicit-unique-drop-does-not-warn-for-fk-that-goes-away",
                    "一意制約: 削除の警告は、同期で消える外部キーには出さない",
                    DialectNames.SqlServer
                )
                .Live(CustomerOrdersWithUniqueId)
                .Items(w =>
                    [
                        DropCustomerIdUnique(),
                        new SchemaDiffItem
                        {
                            Kind = SchemaDiffKind.DropTable,
                            TableName = "orders",
                            Entity = w.Live.Entities[1],
                            IsSelected = true,
                        },
                    ]
                )
                .Expect(e => e.Warnings.Absent("UniqueConstraintDropMayBreakForeignKey*")),
            Row(
                    "plan-implicit-fk-unnamed-same-pair-fks-use-ddl-names",
                    "暗黙の付け直し: 名前の無い同じ組の外部キー 2 本は、DDL と同じ別々の名前で付け直す",
                    DialectNames.SqlServer
                )
                .Live(s =>
                {
                    var (a, b) = ExpectationScenarios.TwoForeignKeysOnSameColumn(s, false);
                    a.ConstraintName = null;
                    b.ConstraintName = null;
                })
                .Items(_ =>
                    [
                        new SchemaDiffItem
                        {
                            Kind = SchemaDiffKind.AlterColumn,
                            TableName = "orders",
                            ColumnName = "customer_id",
                            Column = S.Nullable("customer_id", "BIGINT"),
                            IsSelected = true,
                        },
                        new SchemaDiffItem
                        {
                            Kind = SchemaDiffKind.AlterColumn,
                            TableName = "customer",
                            ColumnName = "id",
                            Column = S.Nullable("id", "BIGINT"),
                            IsSelected = true,
                        },
                    ]
                )
                // 付け直しの名前は図全体から求めた名前（ConstraintNames.ResolveForeignKeyNames＝DDL と同じ規則・2 本目は
                // 子の列名を足して一意化した名前）で、2 本が別々の名前
                .Expect(e =>
                    e.Sections.Count("AddForeignKey: *", 1)
                        .Items.Count("AddForeignKey > *", 2)
                        .Items.Single(
                            "AddForeignKey > * fk=FK_orders_customer sel=*",
                            "AddForeignKey > * fk=FK_orders_customer_customer_id sel=*"
                        )
                ),
        ];

    /// <summary>customer(id) ← orders(customer_id) に、customer の一意制約 UQ_customer_id(id) を足す</summary>
    private static void CustomerOrdersWithUniqueId(ExpectationSchema s)
    {
        ExpectationScenarios.CustomerOrders(s);
        s.Unique("customer", "UQ_customer_id", "id");
    }

    /// <summary>customer の一意制約 UQ_customer_id(id) の削除の項目</summary>
    private static SchemaDiffItem DropCustomerIdUnique() =>
        I.Unique(SchemaDiffKind.DropUniqueConstraint, "customer", "UQ_customer_id", ["id"]);
}
