using System.Collections.Generic;
using QuickER.Dialects;
using QuickER.Model;
using QuickER.Provider;
using QuickER.Tests.TestSupport.Engines;
using C = QuickER.Tests.Integration.Dialects.SyncPlanFuzz.SyncPlanExpectationCapabilities;
using ProviderStrings = QuickER.Provider.Resources.Strings;

namespace QuickER.Tests.Integration.Dialects.SyncPlanFuzz;

/// <summary>差分の段の期待の行（表・列の差分の組み方）</summary>
internal static partial class SyncPlanExpectationRows
{
    private static IEnumerable<ScenarioRow<SyncPlanExpectationInput>> DiffRows() =>
        [
            Row(
                    "diff-new-table-is-add-table",
                    "DB 側に無いテーブルは AddTable になる",
                    DialectNames.SqlServer
                )
                .Target(s =>
                    s.Tbl("Customer", ("Id", "int", true), ("Name", "nvarchar(50)", false))
                )
                .Expect(e => e.Diff.Single("AddTable Customer *")),
            Row(
                    "diff-new-column-is-add-column",
                    "DB 側に無い列は AddColumn になる",
                    DialectNames.SqlServer
                )
                .Live(s => s.Tbl("Customer", ("Id", "int", true)))
                .Target(s =>
                    s.Tbl("Customer", ("Id", "int", true), ("Name", "nvarchar(50)", false))
                )
                .Expect(e => e.Diff.Single("AddColumn Customer.Name *")),
            Row(
                    "diff-type-change-is-unselected-alter-column",
                    "型が変われば AlterColumn になり、既定では未選択",
                    DialectNames.SqlServer
                )
                .Live(s => s.Tbl("Customer", ("Id", "int", true), ("Name", "nvarchar(50)", false)))
                .Target(s =>
                    s.Tbl("Customer", ("Id", "int", true), ("Name", "nvarchar(100)", false))
                )
                .Expect(e => e.Diff.Single("AlterColumn *", "AlterColumn * sel=0 *")),
            Row(
                    "diff-nullability-change-is-alter-column",
                    "NULL 許容が変われば AlterColumn になり、説明は NULL 許容の変更だけ",
                    DialectNames.SqlServer
                )
                .Live(s =>
                    s.Tbl("Customer", ("Id", "int", true), ("Name", "nvarchar(50)", false))
                        .Columns[1]
                        .IsNullable = true
                )
                .Target(s =>
                    s.Tbl("Customer", ("Id", "int", true), ("Name", "nvarchar(50)", false))
                        .Columns[1]
                        .IsNullable = false
                )
                .Expect(e =>
                    e.Diff.Single(
                        "AlterColumn *",
                        "AlterColumn Customer.Name sel=0 * desc=\""
                            + string.Format(
                                ProviderStrings.Diff_ColumnChangePrefix,
                                "Customer",
                                "Name"
                            )
                            + string.Format(
                                ProviderStrings.Diff_NullableChange,
                                ProviderStrings.Diff_Nullable_Allow,
                                ProviderStrings.Diff_Nullable_Deny
                            )
                            + "\""
                    )
                ),
            Row(
                    "diff-computed-column-nullability-is-not-diffed",
                    "計算列の NULL 許容差は AlterColumn にならない（5 方言）",
                    AllDialects
                )
                .Live(s => ComputedTotal(s, "decimal(21,2)", nullable: false))
                .Target(s => ComputedTotal(s, "decimal(21,2)", nullable: true))
                .Expect(e => e.Diff.Absent("AlterColumn *")),
            ComputedTypeChangeRow(),
            Row(
                    "diff-live-only-computed-column-nullability-is-not-diffed",
                    "計算列の型差: DB 側だけが計算列でも NULL 許容は出さず型差は案内項目",
                    DialectNames.SqlServer
                )
                .Live(s => ComputedTotal(s, "decimal(18,2)", nullable: false))
                .Target(s =>
                    s.Tbl("Item", ("Id", "int", true), ("Total", "decimal(18,2)", false))
                        .Columns[1]
                        .IsNullable = true
                )
                .Expect(e => e.Diff.Absent("AlterColumn *")),
            Row(
                    "diff-live-only-computed-column-type-change-is-guidance",
                    "計算列の型差: DB 側だけが計算列でも NULL 許容は出さず型差は案内項目",
                    DialectNames.SqlServer
                )
                .Live(s => ComputedTotal(s, "decimal(18,2)", nullable: false))
                .Target(s =>
                    s.Tbl("Item", ("Id", "int", true), ("Total", "decimal(21,2)", false))
                        .Columns[1]
                        .IsNullable = true
                )
                .Expect(e => e.Diff.Single("AlterColumn *", "AlterColumn * able=0 *")),
            // 図だけが計算列（DDL は計算列を普通の列として作る＝docs/code-generation.md）：DB の列は普通の列なので、型も NULL 許容も
            // 普通の列として比べて変えられる（DB が計算列かで決める）
            Row(
                    "diff-diagram-only-computed-column-type-change-is-selectable",
                    "計算列の型差: 図だけが計算列（DB は普通の列）なら、型の変更は選べる普通の列の変更（5 方言）",
                    AllDialects
                )
                .Live(s =>
                    s.Tbl("Item", ("Id", "int", true), ("Total", "decimal(18,2)", false))
                        .Columns[1]
                        .IsNullable = false
                )
                .Target(s => ComputedTotal(s, "decimal(21,2)", nullable: false))
                .Expect(e =>
                    e.Diff.Single(
                        "AlterColumn *",
                        "AlterColumn * able=1 *",
                        "*"
                            + string.Format(
                                ProviderStrings.Diff_TypeChange,
                                "decimal(18,2)",
                                "decimal(21,2)"
                            )
                            + "*"
                    )
                ),
            Row(
                    "diff-diagram-only-computed-column-nullability-is-alter-column",
                    "計算列の NULL 許容差: 図だけが計算列（DB は普通の列）なら、NULL 許容は普通の列として比べる（5 方言）",
                    AllDialects
                )
                .Live(s =>
                    s.Tbl("Item", ("Id", "int", true), ("Total", "decimal(21,2)", false))
                        .Columns[1]
                        .IsNullable = false
                )
                .Target(s => ComputedTotal(s, "decimal(21,2)", nullable: true))
                .Expect(e =>
                    e.Diff.Single(
                        "AlterColumn *",
                        "AlterColumn * able=1 *",
                        "*"
                            + string.Format(
                                ProviderStrings.Diff_NullableChange,
                                ProviderStrings.Diff_Nullable_Deny,
                                ProviderStrings.Diff_Nullable_Allow
                            )
                            + "*"
                    )
                ),
            Row(
                    "diff-non-computed-column-nullability-is-alter-column",
                    "計算列でなければ NULL 許容差は従来どおり AlterColumn になる",
                    DialectNames.SqlServer
                )
                .Live(s =>
                    s.Tbl("Item", ("Id", "int", true), ("Total", "decimal(21,2)", false))
                        .Columns[1]
                        .IsNullable = false
                )
                .Target(s =>
                    s.Tbl("Item", ("Id", "int", true), ("Total", "decimal(21,2)", false))
                        .Columns[1]
                        .IsNullable = true
                )
                .Expect(e => e.Diff.Single("AlterColumn *")),
            Row(
                    "diff-removed-column-is-unselected-drop-column",
                    "ER 図側に無い列は DropColumn になり、既定では未選択",
                    DialectNames.SqlServer
                )
                .Live(s => s.Tbl("Customer", ("Id", "int", true), ("Old", "int", false)))
                .Target(s => s.Tbl("Customer", ("Id", "int", true)))
                .Expect(e => e.Diff.Single("DropColumn *", "DropColumn Customer.Old sel=0 *")),
            Row(
                    "diff-removed-table-is-drop-table",
                    "ER 図側に無いテーブルは DropTable になる",
                    DialectNames.SqlServer
                )
                .Live(s => s.Tbl("Old", ("Id", "int", true)))
                .Target(_ => { })
                .Expect(e => e.Diff.Single("DropTable Old *")),
            Row("diff-identical-is-empty", "差分が無ければ Items は空", DialectNames.SqlServer)
                .Live(s => s.Tbl("A", ("Id", "int", true)))
                .Target(s => s.Tbl("A", ("Id", "int", true)))
                .Expect(e => e.Diff.Empty()),
            Row(
                    "diff-table-description-change-is-set-table-description",
                    "テーブル説明が変わると SetTableDescription になる",
                    DialectNames.SqlServer
                )
                .Live(s => s.Tbl("Customer", ("Id", "int", true)).Description = "古い説明")
                .Target(s =>
                    s.Tbl("Customer", ("Id", "int", true)).Description = "新しい顧客テーブル"
                )
                .Expect(e =>
                    e.Diff.Single(
                        "SetTableDescription *",
                        "SetTableDescription * sel=1 * newdesc=\"新しい顧客テーブル\" olddesc=\"古い説明\" *"
                    )
                ),
            Row(
                    "diff-column-description-change-is-set-column-description",
                    "列の説明が変わると SetColumnDescription になる",
                    DialectNames.SqlServer
                )
                .Live(s =>
                    s.Tbl("Customer", ("Id", "int", true), ("Name", "nvarchar(50)", false))
                        .Columns[1]
                        .Description = "旧"
                )
                .Target(s =>
                    s.Tbl("Customer", ("Id", "int", true), ("Name", "nvarchar(50)", false))
                        .Columns[1]
                        .Description = "顧客名"
                )
                .Expect(e =>
                    e.Diff.Single(
                        "SetColumnDescription *",
                        "SetColumnDescription *.Name * newdesc=\"顧客名\" olddesc=\"旧\" *"
                    )
                ),
            Row(
                    "diff-same-description-is-empty",
                    "説明が同じなら差分にならない",
                    DialectNames.SqlServer
                )
                .Live(s => s.Tbl("Customer", ("Id", "int", true)).Description = "同じ説明")
                .Target(s => s.Tbl("Customer", ("Id", "int", true)).Description = "同じ説明")
                .Expect(e => e.Diff.Empty()),
            Row(
                    "diff-new-table-with-descriptions-emits-set-descriptions",
                    "新規テーブル + 列の説明があれば SetTable/Column も同時に出力される",
                    DialectNames.SqlServer
                )
                .Target(s =>
                {
                    var customer = s.Tbl(
                        "Customer",
                        ("Id", "int", true),
                        ("Name", "nvarchar(50)", false)
                    );
                    customer.Description = "顧客マスタ";
                    customer.Columns[1].Description = "顧客名";
                })
                .Expect(e =>
                    e.Diff.Single(
                        "AddTable *",
                        "SetTableDescription * newdesc=\"顧客マスタ\" *",
                        "SetColumnDescription *.Name * newdesc=\"顧客名\" *"
                    )
                ),
            Row(
                    "diff-descriptions-suppressed-without-description-support",
                    "説明非対応方言では説明差分を生成しない",
                    C.NeutralWithoutDescriptions
                )
                .Live(s =>
                {
                    var customer = s.Tbl(
                        "Customer",
                        ("Id", "int", true),
                        ("Name", "nvarchar(50)", false)
                    );
                    customer.Description = "旧テーブル説明";
                    customer.Columns[1].Description = "旧列説明";
                })
                .Target(s =>
                {
                    var customer = s.Tbl(
                        "Customer",
                        ("Id", "int", true),
                        ("Name", "nvarchar(50)", false)
                    );
                    customer.Description = "新テーブル説明";
                    customer.Columns[1].Description = "新列説明";
                    s.Tbl("Order", ("Id", "int", true)).Description = "新規テーブルの説明";
                })
                .Expect(e =>
                    e.Diff.Absent("SetTableDescription *", "SetColumnDescription *")
                        .Diff.Single("AddTable Order *")
                ),
            Row(
                    "diff-description-truncation-keeps-surrogate-pair",
                    "説明の切り詰め: サロゲートペアを割らない",
                    DialectNames.SqlServer
                )
                .Live(s => s.Tbl("Item", ("Id", "int", true)))
                .Target(s =>
                    s.Tbl("Item", ("Id", "int", true)).Description =
                        new string('a', 29) + "\U00020BB7" + "b"
                )
                .Expect(e =>
                    e.Diff.Single(
                        "SetTableDescription *",
                        "SetTableDescription * desc=\""
                            + string.Format(
                                ProviderStrings.Diff_UpdateTableDescription,
                                "Item",
                                new string('a', 29) + "…"
                            )
                            + "\""
                    )
                ),
            Row(
                    "diff-case-only-duplicate-columns-match-exactly",
                    "大文字小文字だけ違う列が共存しても例外にならず、組めなかった側だけが差分になる",
                    DialectNames.SqlServer
                )
                .Live(s =>
                    s.Tbl("t", ("id", "int", true), ("Dup", "int", false), ("dup", "int", false))
                )
                .Target(s => s.Tbl("t", ("id", "int", true), ("Dup", "int", false)))
                .Expect(e => e.Diff.Exact("DropColumn t.dup *")),
            Row(
                    "diff-case-only-duplicate-tables-are-not-matched",
                    "大文字小文字だけ違うテーブルが共存しても例外にならず、曖昧な名前は組まない",
                    C.Neutral
                )
                .Live(s =>
                {
                    s.Tbl("Dup", ("id", "int", true));
                    s.Tbl("dup", ("id", "int", true));
                })
                .Target(s => s.Tbl("DUP", ("id", "int", true)))
                .Expect(e =>
                    e.Diff.Unordered("AddTable DUP *", "DropTable Dup *", "DropTable dup *")
                ),
            Row(
                    "diff-case-only-names-unique-on-both-sides-match",
                    "大文字小文字だけ違う名前は双方で一意なら同じテーブル・列として比べる",
                    DialectNames.SqlServer
                )
                .Live(s => s.Tbl("orders", ("id", "int", true), ("name", "int", false)))
                .Target(s => s.Tbl("Orders", ("Id", "int", true), ("Name", "int", false)))
                .Expect(e => e.Diff.Empty()),
            Row(
                    "diff-case-only-matched-names-use-live-spelling",
                    "大文字小文字だけ違う綴りで組んだ表・列は DB の綴りで、図にしか無いものは図の綴りで項目を作る",
                    DialectNames.SqlServer
                )
                .Live(s => CaseOnlyLiveOrders(s))
                .Target(s => CaseOnlyTargetOrders(s))
                .Expect(e =>
                    e.Diff.Single(
                            "AlterColumn *",
                            "AlterColumn ORDERS.NAME * col=VARCHAR2(100) *",
                            "SetTableDescription *",
                            "SetTableDescription ORDERS *",
                            "AddColumn ORDERS.CustomerId *",
                            "AddColumn ORDERS.NewCol *",
                            "AddTable *",
                            "AddTable Items *",
                            "AddForeignKey *",
                            "AddForeignKey * parent=CUSTOMERS child=ORDERS *"
                        )
                        .Diff.Count("AddColumn *", 2)
                ),
        ];

    /// <summary>DB 側の ORDERS(ID, NAME)・CUSTOMERS(ID)（大文字の綴り）</summary>
    private static void CaseOnlyLiveOrders(ExpectationSchema s)
    {
        s.Tbl("ORDERS", ("ID", "NUMBER(10)", true), ("NAME", "VARCHAR2(50)", false));
        s.Tbl("CUSTOMERS", ("ID", "NUMBER(10)", true));
    }

    /// <summary>図の Orders・Customers・Items（綴りが DB と違う・列と表と外部キーを足す）</summary>
    private static void CaseOnlyTargetOrders(ExpectationSchema s)
    {
        s.Tbl(
            "Orders",
            ("Id", "NUMBER(10)", true),
            ("Name", "VARCHAR2(100)", false),
            ("CustomerId", "NUMBER(10)", false),
            ("NewCol", "NUMBER(10)", false)
        ).Description = "orders";
        s.Tbl("Customers", ("Id", "NUMBER(10)", true));
        s.Tbl("Items", ("Id", "NUMBER(10)", true));
        s.Fk("Customers", "Orders", "FK_Orders_Customers", ("Id", "CustomerId"));
    }

    /// <summary>主キー Id と計算列 Total の表 Item</summary>
    private static void ComputedTotal(ExpectationSchema s, string type, bool nullable)
    {
        var total = s.Tbl("Item", ("Id", "int", true), ("Total", type, false)).Columns[1];
        total.IsNullable = nullable;
        total.IsComputed = true;
    }

    private static ExpectationRow ComputedTypeChangeRow()
    {
        var row = Row(
                "diff-computed-column-type-change-selectable-only-where-supported",
                "計算列の型差: NULL 許容は運ばず、PostgreSQL と MySQL だけ選べる（5 方言）",
                AllDialects
            )
            .Live(s => ComputedTotal(s, "decimal(18,2)", nullable: false))
            .Target(s => ComputedTotal(s, "decimal(21,2)", nullable: true));

        var supported = ProviderStrings.Diff_ColumnChangePrefix;
        return row.Expect(e =>
        {
            e.Diff.Single("AlterColumn *");

            foreach (var dialect in AllDialects)
            {
                var selectable = dialect is DialectNames.PostgreSql or DialectNames.MySql;
                var description = selectable
                    ? string.Format(ProviderStrings.Diff_ColumnChangePrefix, "Item", "Total")
                        + string.Format(
                            ProviderStrings.Diff_TypeChange,
                            "decimal(18,2)",
                            "decimal(21,2)"
                        )
                    : string.Format(
                        ProviderStrings.Diff_ComputedColumnTypeNotSynced,
                        "Item",
                        "Total",
                        "decimal(18,2)",
                        "decimal(21,2)"
                    );
                e.For(
                    dialect,
                    d =>
                        d.Diff.Single(
                            $"AlterColumn Item.Total sel=0 able={(selectable ? 1 : 0)} * col=decimal(21,2) notnull computed * desc=\"{description}\""
                        )
                );
            }
        });
    }
}
