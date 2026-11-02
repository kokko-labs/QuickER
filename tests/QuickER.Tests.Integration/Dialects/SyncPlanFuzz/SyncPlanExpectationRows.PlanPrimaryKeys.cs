using System.Collections.Generic;
using System.Linq;
using QuickER.Dialects;
using QuickER.Provider;
using QuickER.Tests.TestSupport.Engines;
using C = QuickER.Tests.Integration.Dialects.SyncPlanFuzz.SyncPlanExpectationCapabilities;
using I = QuickER.Tests.Integration.Dialects.SyncPlanFuzz.ExpectationItems;
using ProviderStrings = QuickER.Provider.Resources.Strings;
using S = QuickER.Tests.Integration.Dialects.SyncPlanFuzz.ExpectationSchema;

namespace QuickER.Tests.Integration.Dialects.SyncPlanFuzz;

/// <summary>
/// 計画の段の期待の行（主キーの変更と列の定義の変更に伴う外部キーの暗黙の外し直し・候補キーの喪失の警告・複合外部キー）
/// </summary>
internal static partial class SyncPlanExpectationRows
{
    /// <summary>複合外部キーの構成列の変更（形, 子の列, 親の列）</summary>
    private static (string Shape, string Child, string Parent)[] CompositeForeignKeyMembers =>
        [("parent-id", "parent_id", "id"), ("order-no", "order_no", "code")];

    private static IEnumerable<ScenarioRow<SyncPlanExpectationInput>> PlanPrimaryKeyRows() =>
        [
            Row(
                    "plan-implicit-fk-rebuild-lowercase-only-equal-column-is-not-rebuilt",
                    "暗黙の付け直し: 小文字化だけで同じになる別の列の型の変更では、外部キーを外し直さない",
                    C.FkRebuild
                )
                // 列 k の型を変えても、別の列（ケルビン記号 U+212A）に張った外部キーは型の変更に関わらない
                .Live(s =>
                {
                    s.Tbl("customer", ("id", "INT", true));
                    s.Tbl(
                        "orders",
                        ("id", "INT", true),
                        ("\u212A", "INT", false),
                        ("k", "INT", false)
                    );
                    s.Fk("customer", "orders", "FK_orders_customer", ("id", "\u212A"));
                })
                .Items(w =>
                    [
                        new SchemaDiffItem
                        {
                            Kind = SchemaDiffKind.AlterColumn,
                            TableName = "orders",
                            ColumnName = "k",
                            Column = S.Nullable("k", "BIGINT"),
                            OldColumn = w.Live["orders"].Columns[2],
                            IsSelected = true,
                        },
                    ]
                )
                // 別の列の外部キーを、変える列の外部キーと取り違えて「両端の型が食い違う」と警告しない
                .Expect(e =>
                    e.Sections.Exact("AlterColumn: #0 AlterColumn orders.k").Warnings.Empty()
                ),
            Row(
                    "plan-alter-pk-injects-implicit-fk-drop-and-readd",
                    "主キー変更: 参照している live FK が自動で外れて再作成される",
                    DialectNames.SqlServer
                )
                .Live(CustomerOrdersWithCode)
                .Items(_ => [MoveCustomerPrimaryKeyToCode()])
                // 依存 FK の自動 DROP は主キー解除より前・自動 ADD は主キー付与より後（主キーが無い区間の外側で外して戻す）。
                // 説明は自動の付け直しの文言
                .Expect(e =>
                    e.Sections.Exact(
                            "DropForeignKey: DropForeignKey orders fk=FK_orders_customer",
                            "AlterPrimaryKey/Drop: #0 AlterPrimaryKey customer",
                            "AlterPrimaryKey/Add: #0 AlterPrimaryKey customer",
                            "AddForeignKey: AddForeignKey orders.customer_id*"
                        )
                        .Items.Single(
                            "DropForeignKey > DropForeignKey orders fk=FK_orders_customer sel=1 * parent=customer child=orders *",
                            "AddForeignKey > AddForeignKey orders.customer_id* parent=customer child=orders * desc=\""
                                + string.Format(
                                    ProviderStrings.Diff_AutoForeignKeyRebuild,
                                    "FK_orders_customer"
                                )
                                + "\""
                        )
                ),
            Row(
                    "plan-alter-pk-does-not-touch-outgoing-fks",
                    "主キー変更: 自テーブルから出ている FK は自動で外さない",
                    DialectNames.SqlServer
                )
                .Live(s => ExpectationScenarios.CustomerOrders(s))
                // 主キーを変えるのは子の表 orders 自身（customer を参照する外向きの外部キーは影響を受けない）
                .Items(_ =>
                    [
                        I.AlterPk(
                            "orders",
                            S.Detached(
                                "orders",
                                S.Nullable("id", "INT"),
                                S.Nullable("customer_id", "INT")
                            )
                        ),
                    ]
                )
                .Expect(e => e.Sections.Exact("AlterPrimaryKey*: #0 AlterPrimaryKey orders")),
            Row(
                    "plan-alter-column-with-capability-injects-fk-rebuild",
                    "AlterColumn: capability が真の方言では依存 FK が自動で外れて再作成される",
                    DialectNames.SqlServer
                )
                .Live(s => ExpectationScenarios.CustomerOrders(s))
                // 型は同じまま NULL 許容だけを変える（両端の型が食い違う変更は外し直さない＝別の行）
                .Items(_ =>
                    [
                        new SchemaDiffItem
                        {
                            Kind = SchemaDiffKind.AlterColumn,
                            TableName = "orders",
                            ColumnName = "customer_id",
                            Column = S.NotNull("customer_id", "INT"),
                            IsSelected = true,
                        },
                    ]
                )
                .Expect(e =>
                    e.Sections.Exact("DropForeignKey: *", "AlterColumn: *", "AddForeignKey: *")
                ),
            Row(
                    "plan-alter-parent-column-injects-fk-rebuild",
                    "AlterColumn: 親側の被参照列の変更でも依存 FK が外れて再作成される",
                    DialectNames.SqlServer
                )
                .Live(s => ExpectationScenarios.CustomerOrders(s))
                // 両端を同じ型へ変える（片側だけだと付け直しが必ず失敗するため外し直さない）
                .Items(_ =>
                    [
                        new SchemaDiffItem
                        {
                            Kind = SchemaDiffKind.AlterColumn,
                            TableName = "customer",
                            ColumnName = "id",
                            Column = S.Nullable("id", "BIGINT"),
                            IsSelected = true,
                        },
                        new SchemaDiffItem
                        {
                            Kind = SchemaDiffKind.AlterColumn,
                            TableName = "orders",
                            ColumnName = "customer_id",
                            Column = S.Nullable("customer_id", "BIGINT"),
                            IsSelected = true,
                        },
                    ]
                )
                .Expect(e => e.Sections.Contains("DropForeignKey: *", "AddForeignKey: *")),
            Row(
                    "plan-alter-column-without-capability-does-not-inject-fk-rebuild",
                    "AlterColumn: capability が偽の方言では FK を自動で外さない",
                    DialectNames.SqlServer
                )
                .Live(s => ExpectationScenarios.CustomerOrders(s))
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
                    ]
                )
                .Expect(e => e.Sections.Exact("AlterColumn: *")),
            Row(
                    "plan-explicitly-dropped-fk-is-neither-duplicated-nor-readded",
                    "明示的に DROP された FK は自動 DROP も再作成もしない",
                    C.Neutral
                )
                .Live(s => ExpectationScenarios.CustomerOrders(s))
                .Items(w =>
                    [
                        I.AlterPk("customer", S.Detached("customer", S.Nullable("id", "INT"))),
                        new SchemaDiffItem
                        {
                            Kind = SchemaDiffKind.DropForeignKey,
                            TableName = "orders",
                            Entity = w.Live["orders"],
                            ParentEntity = w.Live["customer"],
                            ChildEntity = w.Live["orders"],
                            Relationship = w.Live.Relationships[0],
                            ForeignKeyName = "FK_orders_customer",
                            IsSelected = true,
                        },
                    ]
                )
                // DropForeignKey は明示の選択の 1 件だけ・付け直しのセクションは生じない
                .Expect(e =>
                    e.Sections.Exact(
                        "DropForeignKey: #1 DropForeignKey orders fk=FK_orders_customer",
                        "AlterPrimaryKey*: *"
                    )
                ),
            Row(
                    "plan-null-context-skips-implicit-fk-injection",
                    "context 省略時は FK の自動注入をしない",
                    DialectNames.SqlServer
                )
                .Items(_ =>
                    [I.AlterPk("customer", S.Detached("customer", S.Nullable("id", "INT")))]
                )
                .NoContext()
                .Expect(e => e.Sections.Exact("AlterPrimaryKey*: #0 AlterPrimaryKey customer")),
            Row(
                    "plan-alter-pk-referenced-column-leaves-pk-warns-candidate-key-loss",
                    "主キー変更: 被参照列が新主キーから外れると候補キー喪失の警告が積まれる",
                    DialectNames.SqlServer
                )
                .Live(CustomerOrdersWithCode)
                // 新しい主キーは code のみ＝外部キーが参照する customer.id は主キーから外れる
                .Items(_ => [MoveCustomerPrimaryKeyToCode()])
                // 一意制約を取り込んでいないので断定できず、警告に留める（外部キーの自動の外し直しは出る）
                .Expect(e =>
                    e.Warnings.Exact(
                            "ForeignKeyRebuildMayLoseCandidateKey(orders): FK_orders_customer"
                        )
                        .Sections.Contains("AddForeignKey: *")
                ),
            Row(
                    "plan-alter-pk-referenced-column-joins-composite-pk-warns-candidate-key-loss",
                    "主キー変更: 被参照列が複合主キーの一部になると候補キー喪失の警告が積まれる",
                    DialectNames.SqlServer
                )
                .Live(CustomerOrdersWithCode)
                // id を主キーに残したまま code を加える（複合主キー化）＝ customer.id 単独の一意性は保証されなくなる
                .Items(_ =>
                    [
                        I.AlterPk(
                            "customer",
                            S.Detached("customer", S.Pk("id", "INT"), S.Pk("code", "INT"))
                        ),
                    ]
                )
                .Expect(e =>
                    e.Warnings.Exact("ForeignKeyRebuildMayLoseCandidateKey(orders)*")
                        .Sections.Contains("AddForeignKey: *")
                ),
            Row(
                    "plan-alter-pk-new-pk-exactly-referenced-column-no-warning",
                    "主キー変更: 新主キーが被参照列 1 列ちょうどなら警告は積まれない",
                    DialectNames.MySql
                )
                // live は複合主キー (id, code)・外部キーは customer.id を参照する（旧主キーの先頭の列だけを参照する
                // 外部キーは、参照先の索引の先頭の列を要する方言で旧主キーに支えられて作れる形）
                .Live(s =>
                {
                    s.Tbl("customer", ("id", "INT", true), ("code", "INT", true));
                    s.Tbl("orders", ("id", "INT", true), ("customer_id", "INT", false));
                    s.Fk("customer", "orders", "FK_orders_customer", ("id", "customer_id"));
                })
                // 主キーを (id) へ縮める＝被参照列 id が単独で候補キーになる
                .Items(_ =>
                    [
                        I.AlterPk(
                            "customer",
                            S.Detached("customer", S.Pk("id", "INT"), S.Nullable("code", "INT"))
                        ),
                    ]
                )
                .Expect(e => e.Warnings.Empty().Sections.Contains("AddForeignKey: *")),
            Row(
                    "plan-composite-fk-parent-pk-change-rebuilds-all-pairs",
                    "複合外部キー: 参照先テーブルの主キー変更は全構成列のまま計画へ入る",
                    DialectNames.PostgreSql
                )
                .Live(s =>
                {
                    CompositeForeignKeyChild(s);
                    // live の主キーを (id, code) にする＝複合外部キーは旧主キーに支えられている（主キーの解除に巻き込まれる）
                    var code = s.Col("parent", "code");
                    code.IsPrimaryKey = true;
                    code.IsNullable = false;
                })
                // 主キーを (code, id) へ並べ替える。並びを問わない方言では被参照列の集合 (id, code) は候補キーのまま
                .Items(_ =>
                {
                    var target = S.Detached("parent", S.Pk("id", "INT"), S.Pk("code", "INT"));
                    target.PrimaryKeyColumnIds = [target.Columns[1].Id, target.Columns[0].Id];
                    return [I.AlterPk("parent", target)];
                })
                // 複合外部キーが暗黙の外し直しとして入り、単列へ縮まず全構成列のまま付け直される。
                // 被参照列の集合は同期後の主キーとちょうど一致するため候補キーの喪失の警告も出ない
                .Expect(e =>
                    e.Items.Count("DropForeignKey > *", 1)
                        .Items.Count("AddForeignKey > *", 1)
                        .Items.Single(
                            "DropForeignKey > * fk=FK_child_parent *",
                            "AddForeignKey > * pairs=(id>parent_id,code>order_no) *"
                        )
                        .Warnings.Empty()
                ),
            Row(
                    "plan-composite-fk-unrelated-parent-pk-change-leaves-composite-alone",
                    "複合外部キー: 無関係な FK の参照先テーブルの主キー変更は複合 FK を巻き込まない",
                    DialectNames.SqlServer
                )
                .Live(CompositeForeignKeyChild)
                // 主キーの構成は変えない（差分の項目の有無だけを見る合成の形）＝候補キーの喪失の警告も出ない
                .Items(_ => [I.AlterPk("vendor", S.Detached("vendor", S.Pk("id", "INT")))])
                // 自動で外して戻すのは無関係な単列の外部キーだけ（複合外部キーは触らない）
                .Expect(e =>
                    e.Sections.Exact(
                            "DropForeignKey: *",
                            "AlterPrimaryKey*: *",
                            "AlterPrimaryKey*: *",
                            "AddForeignKey: *"
                        )
                        .Warnings.Empty()
                        .Items.Count("AddForeignKey > *", 1)
                        .Items.Single(
                            "AddForeignKey > AddForeignKey child.vendor_id fk=FK_child_vendor * pairs=(id>vendor_id) parent=vendor child=child *"
                        )
                ),
            .. CompositeForeignKeyMemberRows(),
            Row(
                    "plan-composite-fk-unrelated-column-change-does-not-rebuild-fk",
                    "複合外部キー: 無関係な列の定義変更は FK を作り直さない",
                    DialectNames.SqlServer
                )
                .Live(CompositeForeignKeyChild)
                .Items(_ =>
                    [
                        new SchemaDiffItem
                        {
                            Kind = SchemaDiffKind.AlterColumn,
                            TableName = "child",
                            ColumnName = "memo",
                            Column = S.Nullable("memo", "NVARCHAR(50)"),
                            IsSelected = true,
                        },
                    ]
                )
                .Expect(e =>
                    e.Sections.Exact("AlterColumn: #0 AlterColumn child.memo").Warnings.Empty()
                ),
            Row(
                    "plan-composite-fk-member-change-without-capability-does-not-rebuild-fk",
                    "複合外部キー: capability が偽の方言では構成列の変更でも FK を作り直さない",
                    C.Neutral
                )
                .Live(CompositeForeignKeyChild)
                .Items(_ =>
                    [
                        new SchemaDiffItem
                        {
                            Kind = SchemaDiffKind.AlterColumn,
                            TableName = "child",
                            ColumnName = "parent_id",
                            Column = S.Nullable("parent_id", "BIGINT"),
                            IsSelected = true,
                        },
                    ]
                )
                .Expect(e =>
                    e.Sections.Exact("AlterColumn: #0 AlterColumn child.parent_id").Warnings.Empty()
                ),
        ];

    private static IEnumerable<
        ScenarioRow<SyncPlanExpectationInput>
    > CompositeForeignKeyMemberRows() =>
        CompositeForeignKeyMembers.Select(member =>
            (ScenarioRow<SyncPlanExpectationInput>)
                Row(
                        $"plan-composite-fk-member-change-rebuilds-all-pairs-{member.Shape}",
                        "複合外部キー: 構成列の定義変更は全構成列のままの FK 再作成を伴う",
                        C.FkRebuild
                    )
                    .Live(CompositeForeignKeyChild)
                    // 1 組の両端を同じ型へ変える（片側だけだと付け直しが必ず失敗するため外し直さない）
                    .Items(_ =>
                        [
                            new SchemaDiffItem
                            {
                                Kind = SchemaDiffKind.AlterColumn,
                                TableName = "child",
                                ColumnName = member.Child,
                                Column = S.Nullable(member.Child, "BIGINT"),
                                IsSelected = true,
                            },
                            new SchemaDiffItem
                            {
                                Kind = SchemaDiffKind.AlterColumn,
                                TableName = "parent",
                                ColumnName = member.Parent,
                                Column = S.Nullable(member.Parent, "BIGINT"),
                                IsSelected = true,
                            },
                        ]
                    )
                    .Expect(e =>
                        e.Sections.Count("AlterColumn: *", 1)
                            .Sections.Single(
                                $"AlterColumn: #0 AlterColumn child.{member.Child}; #1 AlterColumn parent.{member.Parent}"
                            )
                            .Items.Count("AddForeignKey > *", 1)
                            .Items.Single(
                                "AddForeignKey > * fk=FK_child_parent * pairs=(id>parent_id,code>order_no) parent=parent child=child *"
                            )
                            .Warnings.Empty()
                    )
        );

    /// <summary>customer(id) ← orders(customer_id) に、customer の NOT NULL の列 code（主キーの付け替え先）を足す</summary>
    private static void CustomerOrdersWithCode(ExpectationSchema s)
    {
        ExpectationScenarios.CustomerOrders(s);
        // 新しい主キーの列 code は live に在る（無ければ主キーの変更は「選んでいない列の追加」に依存して外れる）
        s["customer"].Columns.Add(S.NotNull("code", "INT"));
    }

    /// <summary>customer の主キーを code へ付け替える主キーの変更（目標の customer）</summary>
    private static SchemaDiffItem MoveCustomerPrimaryKeyToCode() =>
        I.AlterPk("customer", S.Detached("customer", S.Nullable("id", "INT"), S.Pk("code", "INT")));

    /// <summary>
    /// 複合外部キー parent(id, code) ← child(parent_id, order_no) と、同じ子の表が持つ無関係な単列の外部キー
    /// vendor(id) ← child(vendor_id) を足す（child.memo はどの外部キーにも関わらない列）
    /// </summary>
    private static void CompositeForeignKeyChild(ExpectationSchema s)
    {
        s.Tbl("parent", ("id", "INT", true), ("code", "INT", false));
        s.Tbl("vendor", ("id", "INT", true));
        s.Tbl(
            "child",
            ("id", "INT", true),
            ("parent_id", "INT", false),
            ("order_no", "INT", false),
            ("vendor_id", "INT", false),
            ("memo", "TEXT", false)
        );
        s.Fk("parent", "child", "FK_child_parent", ("id", "parent_id"), ("code", "order_no"));
        s.Fk("vendor", "child", "FK_child_vendor", ("id", "vendor_id"));
    }
}
