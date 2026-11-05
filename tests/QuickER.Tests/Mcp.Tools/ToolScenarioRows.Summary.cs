using QuickER.Tests.TestSupport;
using GuiStrings = QuickER.Resources.Strings;

namespace QuickER.Tests.Mcp.Tools;

internal static partial class ToolScenarioRows
{
    /// <summary>get_diagram_summary の外部キーの列ペアと制約名、主キーの順序の注記</summary>
    private static IEnumerable<ToolRow> SummaryRows()
    {
        yield return Row(
                "summary-shows-foreign-key-column-pairs-sqlserver",
                "get_diagram_summary は外部キーの列ペアを表示する"
            )
            .Base(TenantTables("TenantRef", "RegionRef"))
            .Arrange(
                "add_relationship",
                RelationshipArgs(
                    "TenantRegion",
                    "TenantUser",
                    ["TenantId", "RegionCode"],
                    ["TenantRef", "RegionRef"]
                )
            )
            .Call(
                "get_diagram_summary",
                new { },
                e => e.Ok().Result.Contains("FK: (TenantId → TenantRef, RegionCode → RegionRef)")
            );

        yield return SqliteParentChild(
                Row(
                    "summary-shows-foreign-key-column-pairs-and-constraint-name-sqlite",
                    "get_diagram_summary は外部キーの列ペアと制約名を表示する"
                )
            )
            .Arrange("add_relationship", RelationshipArgs("Customer", "Order"))
            .Call(
                "get_diagram_summary",
                new { },
                e => e.Ok().Result.Contains("FK: (CustomerId → CustomerId)", "[FK_Order_Customer]")
            );

        // 主キーの順序の注記は、列宣言順と食い違うときだけ出る（GUI の文言は resx・MCP は英語の固定文）
        foreach (var reversed in new[] { true, false })
        {
            var shape = reversed ? "reordered" : "natural";

            yield return PrimaryKeyOrderSummary(
                    Row(
                        $"summary-primary-key-order-note-only-when-it-differs-{shape}-sqlserver",
                        "get_diagram_summary は主キー順が列宣言順と食い違うときだけ注記する"
                    ),
                    reversed,
                    withChild: false
                )
                .Call("get_diagram_summary", new { }, e => PrimaryKeyOrderNote(e.Ok(), reversed));

            yield return PrimaryKeyOrderSummary(
                    Row(
                            $"summary-primary-key-order-note-only-when-it-differs-{shape}-sqlite",
                            "get_diagram_summary は主キー順が列宣言順と食い違うときだけ注記する"
                        )
                        .Dialect("sqlite"),
                    reversed,
                    withChild: true
                )
                .Call("get_diagram_summary", new { }, e => PrimaryKeyOrderNote(e.Ok(), reversed));
        }
    }

    /// <summary>
    /// 複合主キーの親 TenantRegion（列宣言順 TenantId, RegionCode）の図。<paramref name="reversed"/> が真なら主キーの実効順を
    /// 列宣言順の逆へ宣言し、<paramref name="withChild"/> が真なら同名の外部キー列候補を持つ子 TenantUser も作る
    /// </summary>
    private static ToolRow PrimaryKeyOrderSummary(ToolRow row, bool reversed, bool withChild)
    {
        row.Base(TenantTables());

        if (withChild)
        {
            row.Arrange("add_column", ColumnArgs("TenantUser", "TenantId", "int", nullable: false))
                .Arrange(
                    "add_column",
                    ColumnArgs("TenantUser", "RegionCode", "nvarchar(10)", nullable: false)
                );
        }

        if (reversed)
        {
            row.Arrange(
                "set_primary_key",
                new { table_name = "TenantRegion", columns = new[] { "RegionCode", "TenantId" } }
            );
        }

        return row;
    }

    /// <summary>主キー順の注記があるか（<paramref name="reversed"/>）・無いかを、ホストごとの文言で表明する</summary>
    private static ToolExpect PrimaryKeyOrderNote(ToolExpect expect, bool reversed) =>
        reversed
            ? expect
                .ForHost(
                    ToolHostKind.Gui,
                    "GUI の応答は resx（UI の言語）・MCP は英語の固定文",
                    gui =>
                        gui.Result.Contains(
                            string.Format(
                                GuiStrings.Tool_Summary_PrimaryKeyOrder,
                                "RegionCode, TenantId"
                            )
                        )
                )
                .ForHost(
                    ToolHostKind.File,
                    "GUI の応答は resx（UI の言語）・MCP は英語の固定文",
                    file => file.Result.Contains("Primary key order: RegionCode, TenantId")
                )
            : expect
                .ForHost(
                    ToolHostKind.Gui,
                    "GUI の応答は resx（UI の言語）・MCP は英語の固定文",
                    gui =>
                        gui.Result.Absent(
                            GuiStrings
                                .Tool_Summary_PrimaryKeyOrder.Replace("{0}", string.Empty)
                                .Trim()
                        )
                )
                .ForHost(
                    ToolHostKind.File,
                    "GUI の応答は resx（UI の言語）・MCP は英語の固定文",
                    file => file.Result.Absent("Primary key order")
                );
}
