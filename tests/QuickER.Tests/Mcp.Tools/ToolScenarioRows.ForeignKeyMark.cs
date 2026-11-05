namespace QuickER.Tests.Mcp.Tools;

internal static partial class ToolScenarioRows
{
    /// <summary>
    /// 外部キーの印の後始末（リレーションから外れた子列の印を、remove_relationship・親の remove_entity・親列の remove_column の
    /// 3 つの削除経路で落とす。別のリレーションが使い続ける列と、どの列ペアにも現れない列へ手で付けた印は残す）
    /// </summary>
    private static IEnumerable<ToolRow> ForeignKeyMarkRows()
    {
        yield return FkMarkTables(
                Row(
                    "foreign-key-mark-setup-marks-every-child-column",
                    "準備した図では列ペアの子列すべてに外部キー印が立つ"
                )
            )
            .Call(
                "get_diagram_summary",
                new { },
                e =>
                    e.Ok()
                        .Schema.Single(
                            "col Order.OrderId int pk notnull nofk *",
                            "col Order.CustomerId int nopk notnull fk *",
                            "col Order.BillToCustomerId int nopk notnull fk *",
                            "col Order.RegionId int nopk notnull fk *",
                            "col Order.RegionCode nvarchar(10) nopk notnull fk *"
                        )
            );

        yield return FkMarkTables(
                Row(
                    "foreign-key-mark-remove-relationship-releases-child-column",
                    "remove_relationship は外れた子列の外部キー印を落とす"
                )
            )
            .Call(
                "remove_relationship",
                new
                {
                    source_table = "Customer",
                    target_table = "Order",
                    constraint_name = "FK_BillTo",
                },
                e =>
                    e.Ok()
                        .Schema.Single(
                            "col Order.BillToCustomerId int nopk notnull nofk *",
                            "col Order.CustomerId int nopk notnull fk *",
                            "col Order.RegionId int nopk notnull fk *"
                        )
            );

        yield return FkMarkTables(
                Row(
                    "foreign-key-mark-remove-parent-entity-releases-child-column-marks",
                    "親テーブルの remove_entity は子テーブルに残る列の外部キー印を落とす"
                )
            )
            .Call(
                "remove_entity",
                new { table_name = "Region" },
                e =>
                    e.Ok()
                        .Schema.Single(
                            "col Order.RegionId int nopk notnull nofk *",
                            "col Order.RegionCode nvarchar(10) nopk notnull nofk *",
                            "col Order.CustomerId int nopk notnull fk *",
                            "col Order.BillToCustomerId int nopk notnull fk *"
                        )
            );

        yield return FkMarkTables(
                Row(
                    "foreign-key-mark-remove-parent-column-releases-every-composite-child-column-mark",
                    "親列の remove_column は複合外部キーの子列すべての印を落とす"
                )
            )
            .Call(
                "remove_column",
                new { table_name = "Region", column_name = "Code" },
                e =>
                    e.Ok()
                        .Schema.Single(
                            "col Order.RegionId int nopk notnull nofk *",
                            "col Order.RegionCode nvarchar(10) nopk notnull nofk *",
                            "col Order.CustomerId int nopk notnull fk *"
                        )
            );

        yield return FkMarkTables(
                Row(
                    "foreign-key-mark-column-still-used-by-another-relationship-keeps-mark",
                    "別のリレーションが使い続けている子列の外部キー印は残す"
                )
            )
            .Call(
                "remove_entity",
                new { table_name = "Account" },
                // Order.CustomerId は Customer→Order の列ペアにも現れる
                e => e.Ok().Schema.Single("col Order.CustomerId int nopk notnull fk *")
            );

        yield return FkMarkTables(
                Row(
                    "foreign-key-mark-manual-mark-outside-any-pair-is-kept",
                    "どの列ペアにも現れない列へ手で付けた外部キー印は残す"
                )
            )
            .Arrange("add_column", ColumnArgs("Order", "LegacyRef", "int", nullable: true))
            // DB 取込や GUI の手動チェックで付いた印に相当する（ツールには印だけを立てる操作が無い）
            .Seed(
                "Order.LegacyRef に外部キーの印を直接付ける",
                session => session.SetForeignKeyMark("Order", "LegacyRef", true)
            )
            .Call(
                "remove_entity",
                new { table_name = "Region" },
                e => e.Ok().Schema.Single("col Order.LegacyRef int nopk null fk *")
            );
    }

    /// <summary>
    /// 外部キーの印の準備。Customer(CustomerId) と Region(RegionId, Code の複合主キー) と Account(AccountId) が Order を参照する
    /// （Order.CustomerId は Customer と Account の 2 本から参照される共有の子列）。リレーション：Customer→Order（CustomerId→CustomerId・
    /// 既定名）／Customer→Order（CustomerId→BillToCustomerId・FK_BillTo）／Region→Order（RegionId→RegionId, Code→RegionCode の複合）／
    /// Account→Order（AccountId→CustomerId）
    /// </summary>
    private static ToolRow FkMarkTables(ToolRow row) =>
        row.Arrange("add_entity", new { table_name = "Customer" })
            .Arrange(
                "add_column",
                ColumnArgs("Customer", "CustomerId", "int", pk: true, nullable: false)
            )
            .Arrange("add_entity", new { table_name = "Region" })
            .Arrange(
                "add_column",
                ColumnArgs("Region", "RegionId", "int", pk: true, nullable: false)
            )
            .Arrange(
                "add_column",
                ColumnArgs("Region", "Code", "nvarchar(10)", pk: true, nullable: false)
            )
            .Arrange("add_entity", new { table_name = "Account" })
            .Arrange(
                "add_column",
                ColumnArgs("Account", "AccountId", "int", pk: true, nullable: false)
            )
            .Arrange("add_entity", new { table_name = "Order" })
            .Arrange("add_column", ColumnArgs("Order", "OrderId", "int", pk: true, nullable: false))
            .Arrange("add_column", ColumnArgs("Order", "CustomerId", "int", nullable: false))
            .Arrange("add_column", ColumnArgs("Order", "BillToCustomerId", "int", nullable: false))
            .Arrange("add_column", ColumnArgs("Order", "RegionId", "int", nullable: false))
            .Arrange(
                "add_column",
                ColumnArgs("Order", "RegionCode", "nvarchar(10)", nullable: false)
            )
            .Arrange(
                "add_relationship",
                RelationshipArgs("Customer", "Order", ["CustomerId"], ["CustomerId"])
            )
            .Arrange(
                "add_relationship",
                RelationshipArgs(
                    "Customer",
                    "Order",
                    ["CustomerId"],
                    ["BillToCustomerId"],
                    constraintName: "FK_BillTo"
                )
            )
            .Arrange(
                "add_relationship",
                RelationshipArgs(
                    "Region",
                    "Order",
                    ["RegionId", "Code"],
                    ["RegionId", "RegionCode"]
                )
            )
            .Arrange(
                "add_relationship",
                RelationshipArgs("Account", "Order", ["AccountId"], ["CustomerId"])
            );
}
