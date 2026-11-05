using QuickER.Tests.TestSupport;
using GuiStrings = QuickER.Resources.Strings;

namespace QuickER.Tests.Mcp.Tools;

internal static partial class ToolScenarioRows
{
    /// <summary>
    /// set_primary_key（単一列・複合の指定順・NULL 許容列の NOT NULL 化・旧主キー列の NULL 許容は据え置き・列名の大文字小文字の無視・
    /// 不正な引数の拒否）。膜（IsPrimaryKey）と順序（pkids）の両方が指定どおりに書かれ、拒否した呼び出しは図を変えない
    /// </summary>
    private static IEnumerable<ToolRow> PrimaryKeyRows()
    {
        yield return PkOrderLine(
                Row(
                    "primary-key-sets-single-column-key-sqlite",
                    "set_primary_key は単一列の主キーを設定する"
                )
            )
            .Call(
                "set_primary_key",
                new { table_name = "OrderLine", columns = new[] { "OrderId" } },
                e =>
                    e.Ok()
                        .ForHost(
                            ToolHostKind.Gui,
                            "GUI の応答は resx（UI の言語）・MCP は英語の固定文",
                            gui =>
                                gui.Result.Exact(
                                    string.Format(
                                        GuiStrings.Tool_PrimaryKeySet,
                                        "OrderLine",
                                        "OrderId"
                                    )
                                )
                        )
                        .ForHost(
                            ToolHostKind.File,
                            "GUI の応答は resx（UI の言語）・MCP は英語の固定文",
                            file =>
                                file.Result.Exact(
                                    "Set primary key on table 'OrderLine' (columns: OrderId)."
                                )
                        )
                        .Schema.Kind(
                            "col",
                            "col OrderLine.OrderLineId int nopk notnull *",
                            "col OrderLine.OrderId int pk notnull *",
                            "col OrderLine.LineNo int nopk notnull *",
                            "col OrderLine.Memo nvarchar(100) nopk null *"
                        )
                        .Schema.Single("pkorder OrderLine (OrderId)", "pkids OrderLine (OrderId)")
            );

        yield return PkTenantRegion(
                Row(
                    "primary-key-composite-reversed-order-is-recorded-as-given-sqlserver",
                    "set_primary_key は列宣言順と逆順の複合主キーを指定順どおり記録する"
                )
            )
            .Call(
                "set_primary_key",
                new { table_name = "TenantRegion", columns = new[] { "RegionCode", "TenantId" } },
                e =>
                    e.Ok()
                        .Schema.Single(
                            "pkorder TenantRegion (RegionCode,TenantId)",
                            "pkids TenantRegion (RegionCode,TenantId)",
                            "pkreorder TenantRegion (RegionCode,TenantId)"
                        )
            );

        // 列宣言順は OrderLineId → OrderId → LineNo なので、逆順の指定は宣言順と食い違う
        yield return PkOrderLine(
                Row(
                    "primary-key-composite-reversed-order-is-recorded-as-given-sqlite",
                    "set_primary_key は列宣言順と逆順の複合主キーを指定順どおり記録する"
                )
            )
            .Call(
                "set_primary_key",
                new { table_name = "OrderLine", columns = new[] { "LineNo", "OrderId" } },
                e =>
                    e.Ok()
                        .Schema.Single(
                            "pkorder OrderLine (LineNo,OrderId)",
                            "pkids OrderLine (LineNo,OrderId)",
                            "pkreorder OrderLine (LineNo,OrderId)"
                        )
            );

        yield return PkOrderLine(
                Row(
                    "primary-key-three-columns-records-whole-order-sqlite",
                    "set_primary_key は 3 列の主キーの並びを全体として記録する"
                )
            )
            .Call(
                "set_primary_key",
                new
                {
                    table_name = "OrderLine",
                    columns = new[] { "LineNo", "OrderLineId", "OrderId" },
                },
                e =>
                    // 部分でなく全体（3 列すべて）が並びとして残る
                    e.Ok()
                        .Schema.Single(
                            "pkorder OrderLine (LineNo,OrderLineId,OrderId)",
                            "pkids OrderLine (LineNo,OrderLineId,OrderId)"
                        )
            );

        yield return PkOrderLine(
                Row(
                    "primary-key-dropped-column-keeps-nullability-sqlite",
                    "set_primary_key は旧主キー列を外すが NULL 許容は据え置く"
                )
            )
            .Call(
                "set_primary_key",
                new { table_name = "OrderLine", columns = new[] { "OrderId" } },
                // 主キーから外れただけで NULL 許容へは倒さない
                e => e.Ok().Schema.Single("col OrderLine.OrderLineId int nopk notnull *")
            );

        yield return PkTenantRegion(
                Row(
                    "primary-key-nullable-column-becomes-not-null-sqlserver",
                    "set_primary_key は NULL 許容列を主キーに含めると NOT NULL へ正規化する"
                )
            )
            .Call(
                "set_primary_key",
                new { table_name = "TenantRegion", columns = new[] { "Code" } },
                e =>
                    e.Ok()
                        .Schema.Kind(
                            "col",
                            // 主キーから外れた列の NULL 許容は据え置き（外したことが NULL 許容の変更まで意味しない）
                            "col TenantRegion.TenantId int nopk notnull *",
                            "col TenantRegion.RegionCode nvarchar(10) nopk notnull *",
                            "col TenantRegion.Code nvarchar(20) pk notnull *"
                        )
                        .Schema.Single("pkorder TenantRegion (Code)")
            );

        yield return PkOrderLine(
                Row(
                    "primary-key-nullable-column-becomes-not-null-sqlite",
                    "set_primary_key は NULL 許容列を主キーに含めると NOT NULL へ正規化する"
                )
            )
            .Call(
                "set_primary_key",
                new { table_name = "OrderLine", columns = new[] { "Memo" } },
                e => e.Ok().Schema.Single("col OrderLine.Memo nvarchar(100) pk notnull *")
            );

        yield return PkTenantRegion(
                Row(
                    "primary-key-column-names-are-matched-case-insensitively-sqlserver",
                    "set_primary_key は列名の大文字小文字を無視して照合する"
                )
            )
            .Call(
                "set_primary_key",
                new { table_name = "TenantRegion", columns = new[] { "regioncode", "tenantid" } },
                e =>
                    e.Ok()
                        .Schema.Single(
                            "pkorder TenantRegion (RegionCode,TenantId)",
                            "pkids TenantRegion (RegionCode,TenantId)"
                        )
            );

        yield return PkOrderLine(
                Row(
                    "primary-key-column-names-are-matched-case-insensitively-sqlite",
                    "set_primary_key は列名の大文字小文字を無視して照合する"
                )
            )
            .Call(
                "set_primary_key",
                new { table_name = "OrderLine", columns = new[] { "orderid", "LINENO" } },
                e => e.Ok().Schema.Single("pkids OrderLine (OrderId,LineNo)")
            );

        // ---------------- 不正な引数（図を変えない） ----------------

        yield return PkOrderLine(
                Row(
                    "primary-key-invalid-arguments-fail-without-touching-diagram-missing-table-name-sqlite",
                    "set_primary_key は不正な引数をエラーにし図を変更しない"
                )
            )
            .Call(
                "set_primary_key",
                new { columns = new[] { "OrderId" } },
                e => e.Fails().Result.NotEmpty().Unchanged()
            );

        foreach (
            var (dialect, table, existing) in new[]
            {
                ("sqlserver", "TenantRegion", "TenantId"),
                ("sqlite", "OrderLine", "OrderId"),
            }
        )
        {
            var shapes = new (string Shape, object Args)[]
            {
                ("unknown-table", new { table_name = "Ghost", columns = new[] { existing } }),
                ("columns-missing", new { table_name = table }),
                ("columns-empty", new { table_name = table, columns = Array.Empty<string>() }),
                ("unknown-column", new { table_name = table, columns = new[] { "NoSuchColumn" } }),
                (
                    "duplicate-column",
                    new
                    {
                        table_name = table,
                        columns = new[] { existing, existing.ToLowerInvariant() },
                    }
                ),
            };

            foreach (var (shape, args) in shapes)
            {
                var row = Row(
                    $"primary-key-invalid-arguments-fail-without-touching-diagram-{shape}-{dialect}",
                    "set_primary_key は不正な引数をエラーにし図を変更しない"
                );

                yield return (dialect == "sqlserver" ? PkTenantRegion(row) : PkOrderLine(row)).Call(
                    "set_primary_key",
                    args,
                    e => e.Fails().Result.NotEmpty().Unchanged()
                );
            }
        }
    }

    /// <summary>TenantRegion(TenantId PK / RegionCode PK / Code NULL 許容) の SQL Server の図</summary>
    private static ToolRow PkTenantRegion(ToolRow row) =>
        row.Arrange("add_entity", new { table_name = "TenantRegion" })
            .Arrange(
                "add_column",
                ColumnArgs("TenantRegion", "TenantId", "int", pk: true, nullable: false)
            )
            .Arrange(
                "add_column",
                ColumnArgs("TenantRegion", "RegionCode", "nvarchar(10)", pk: true, nullable: false)
            )
            .Arrange(
                "add_column",
                ColumnArgs("TenantRegion", "Code", "nvarchar(20)", nullable: true)
            );

    /// <summary>OrderLine(OrderLineId PK / OrderId / LineNo / Memo〔NULL 許容〕) の SQLite の図</summary>
    private static ToolRow PkOrderLine(ToolRow row) =>
        row.Dialect("sqlite")
            .Arrange("add_entity", new { table_name = "OrderLine" })
            .Arrange(
                "add_column",
                ColumnArgs("OrderLine", "OrderLineId", "int", pk: true, nullable: false)
            )
            .Arrange("add_column", ColumnArgs("OrderLine", "OrderId", "int", nullable: false))
            .Arrange("add_column", ColumnArgs("OrderLine", "LineNo", "int", nullable: false))
            .Arrange(
                "add_column",
                ColumnArgs("OrderLine", "Memo", "nvarchar(100)", nullable: true)
            );
}
