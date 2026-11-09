using QuickER.Tests.TestSupport;

namespace QuickER.Tests.Mcp.Tools;

internal static partial class ToolScenarioRows
{
    /// <summary>
    /// 成功のまま返すが利用者が気付くべきことを応答に添える規則（要約の対象 DBMS・主キー列の NULL 許容の無視・
    /// 列ペアの推論が親の主キーに届かなかったこと・明示した親側の列が候補キーでないこと）と、複数行の応答の改行
    /// </summary>
    private static IEnumerable<ToolRow> NotesRows()
    {
        // ---------------- get_diagram_summary の対象 DBMS ----------------

        yield return Row(
                "note-summary-first-line-shows-target-dbms",
                "get_diagram_summary は先頭行で対象 DBMS を示す"
            )
            .Base(CustomerOrder)
            .Call(
                "get_diagram_summary",
                new { },
                e =>
                    e.Ok()
                        // 先頭行の切り出しは面で書けないので Check
                        .Check(
                            "先頭行が対象 DBMS を含む",
                            observation =>
                                observation
                                    .Result.Split('\n')[0]
                                    .Contains("sqlserver", StringComparison.Ordinal)
                                    ? null
                                    : "先頭行に sqlserver が無い"
                        )
                        .ForHost(
                            ToolHostKind.File,
                            "GUI の応答は resx（UI の言語）・MCP は英語の固定文",
                            file =>
                                file.Check(
                                    "先頭行が 'Target DBMS: sqlserver'",
                                    observation =>
                                        observation.Result.Split('\n')[0].TrimEnd('\r')
                                        == "Target DBMS: sqlserver"
                                            ? null
                                            : "先頭行が違う"
                                )
                        )
            );

        // ---------------- 主キー列の is_nullable=true ----------------

        yield return Row(
                "note-set-column-property-nullable-on-primary-key-is-reported",
                "set_column_property は主キー列への is_nullable=true を無視したと応答で告げる"
            )
            .Base(CustomerOrder)
            .Call(
                "set_column_property",
                new
                {
                    table_name = "Customer",
                    column_name = "CustomerId",
                    is_nullable = true,
                },
                e =>
                    e.Ok()
                        .Result.Contains("'CustomerId'", "is_nullable=true")
                        .Schema.Single("col Customer.CustomerId int pk notnull *")
            );

        yield return Row(
                "note-set-column-property-nullable-on-primary-key-with-other-change-applies-and-reports",
                "set_column_property は他の変更を反映したうえで、無視した is_nullable=true も告げる"
            )
            .Base(CustomerOrder)
            .Call(
                "set_column_property",
                new
                {
                    table_name = "Customer",
                    column_name = "CustomerId",
                    description = "Identifier",
                    is_nullable = true,
                },
                e =>
                    e.Ok()
                        .Result.Contains("is_nullable=true")
                        .Schema.Single(
                            "col Customer.CustomerId int pk notnull nofk desc=\"Identifier\""
                        )
            );

        yield return Row(
                "note-set-column-property-nullable-on-ordinary-column-has-no-note",
                "set_column_property は主キーでない列の is_nullable=true では注意を添えない"
            )
            .Base(CustomerOrder)
            .Call(
                "set_column_property",
                new
                {
                    table_name = "Customer",
                    column_name = "Name",
                    is_nullable = true,
                },
                e => e.Ok().Result.Absent("is_nullable=true")
            );

        // ---------------- 列ペアの推論が親の主キーに届かない ----------------

        yield return Row(
                "note-add-relationship-fully-inferred-pairs-has-no-note",
                "add_relationship は列ペアを親の主キー全列へ推論できたときは注意を添えない"
            )
            .Base(CustomerOrder)
            .Call(
                "add_relationship",
                new
                {
                    source_table = "Customer",
                    target_table = "Order",
                    relationship_type = "OneToMany",
                },
                e =>
                    e.Ok()
                        .Result.Absent("source_columns")
                        .Schema.Kind(
                            "rel",
                            "rel Customer->Order OneToMany * pairs=(CustomerId>CustomerId)"
                        )
            );

        yield return Row(
                "note-add-relationship-identifying-relationship-pairs-child-primary-key",
                "add_relationship は識別関係（子の主キー (OrderId, LineNo) の OrderId）を推論し、注意を添えない"
            )
            .Base(CustomerOrder)
            .Arrange("add_entity", new { table_name = "OrderLine" })
            .Arrange(
                "add_column",
                new
                {
                    table_name = "OrderLine",
                    column_name = "OrderId",
                    data_type = "int",
                    is_primary_key = true,
                }
            )
            .Arrange(
                "add_column",
                new
                {
                    table_name = "OrderLine",
                    column_name = "LineNo",
                    data_type = "int",
                    is_primary_key = true,
                }
            )
            .Call(
                "add_relationship",
                new
                {
                    source_table = "Order",
                    target_table = "OrderLine",
                    relationship_type = "OneToMany",
                },
                e =>
                    e.Ok()
                        .Result.Absent("source_columns")
                        .Schema.Kind(
                            "rel",
                            "rel Order->OrderLine OneToMany * pairs=(OrderId>OrderId)"
                        )
                        .Schema.Single("col OrderLine.OrderId * pk * fk *")
            );

        yield return Row(
                "note-add-relationship-parent-without-key-is-reported",
                "add_relationship は親に主キーが無く列ペアを推論できないと注意を添える"
            )
            .Base(CustomerOrder)
            .Arrange("add_entity", new { table_name = "Tag" })
            .Arrange(
                "add_column",
                new
                {
                    table_name = "Tag",
                    column_name = "Label",
                    data_type = "nvarchar(20)",
                }
            )
            .Call(
                "add_relationship",
                new
                {
                    source_table = "Tag",
                    target_table = "Order",
                    relationship_type = "OneToMany",
                },
                e =>
                    e.Ok()
                        .Result.Contains("'Tag'", "source_columns")
                        .Schema.Kind("rel", "rel Tag->Order OneToMany * pairs=()")
            );

        yield return Row(
                "note-add-relationship-partially-inferred-pairs-is-reported",
                "add_relationship は複合主キーの一部しか対応付けられないと本数を添えて注意する"
            )
            .Base(CustomerOrder)
            .Arrange("add_entity", new { table_name = "Region" })
            .Arrange(
                "add_column",
                new
                {
                    table_name = "Region",
                    column_name = "RegionId",
                    data_type = "int",
                    is_primary_key = true,
                }
            )
            .Arrange(
                "add_column",
                new
                {
                    table_name = "Region",
                    column_name = "Code",
                    data_type = "nvarchar(10)",
                    is_primary_key = true,
                }
            )
            .Arrange(
                "add_column",
                new
                {
                    table_name = "Order",
                    column_name = "RegionId",
                    data_type = "int",
                }
            )
            .Call(
                "add_relationship",
                new
                {
                    source_table = "Region",
                    target_table = "Order",
                    relationship_type = "OneToMany",
                },
                e =>
                    e.Ok()
                        .Result.Contains("1", "2", "source_columns")
                        .Schema.Kind(
                            "rel",
                            "rel Region->Order OneToMany * pairs=(RegionId>RegionId)"
                        )
            );

        yield return Row(
                "note-add-relationship-explicit-columns-or-many-to-many-has-no-note",
                "add_relationship は列を明示したときと多対多では推論の注意を添えない"
            )
            .Base(CustomerOrder)
            .Arrange("add_entity", new { table_name = "Tag" })
            .Arrange(
                "add_column",
                new
                {
                    table_name = "Tag",
                    column_name = "Label",
                    data_type = "nvarchar(20)",
                }
            )
            // 明示する親側の列は候補キー（一意制約）にしておく＝候補キーでない列の注意とは切り分ける
            .Arrange(
                "set_unique_constraint",
                new { table_name = "Tag", columns = new[] { "Label" } }
            )
            .Call(
                "add_relationship",
                new
                {
                    source_table = "Tag",
                    target_table = "Order",
                    relationship_type = "ManyToMany",
                },
                e => e.Ok().Result.Absent("source_columns")
            )
            .Call(
                "add_relationship",
                new
                {
                    source_table = "Tag",
                    target_table = "Order",
                    source_columns = new[] { "Label" },
                    target_columns = new[] { "ShipToCustomerId" },
                    relationship_type = "OneToMany",
                },
                e => e.Ok().Result.Absent("source_columns")
            );

        yield return Row(
                "note-add-relationship-no-child-column-matches-parent-key-is-reported",
                "add_relationship は親に主キーがあっても子に対応する列が 1 本も無いと注意を添える"
            )
            .Base(CustomerOrder)
            .Arrange("add_entity", new { table_name = "Region" })
            .Arrange(
                "add_column",
                new
                {
                    table_name = "Region",
                    column_name = "RegionId",
                    data_type = "int",
                    is_primary_key = true,
                }
            )
            // Order に RegionId に当たる列は無い＝推論できた列ペアは 0 本
            .Call(
                "add_relationship",
                new
                {
                    source_table = "Region",
                    target_table = "Order",
                    relationship_type = "OneToMany",
                },
                e =>
                    e.Ok()
                        .Result.Contains("0", "1", "source_columns")
                        .Schema.Kind("rel", "rel Region->Order OneToMany * pairs=()")
            );

        // ---------------- 明示した親側の列が候補キーでない ----------------

        yield return Row(
                "note-add-relationship-explicit-non-candidate-key-is-reported-single",
                "add_relationship は明示した親側の列が主キーでも一意制約でもなければ、作成したうえで注意を添える"
            )
            .Base(CustomerOrder)
            .Call(
                "add_relationship",
                new
                {
                    source_table = "Customer",
                    target_table = "Order",
                    source_columns = new[] { "Name" },
                    target_columns = new[] { "CustomerId" },
                    relationship_type = "OneToMany",
                },
                e =>
                    e.Ok()
                        .Result.Contains("'Customer'", "Name", "source_columns")
                        .Schema.Kind(
                            "rel",
                            "rel Customer->Order OneToMany * pairs=(Name>CustomerId)"
                        )
            );

        yield return Row(
                "note-add-relationship-explicit-non-candidate-key-is-reported-composite",
                "add_relationship は明示した親側の列が主キーでも一意制約でもなければ、作成したうえで注意を添える"
            )
            .Base(CustomerOrder)
            .Call(
                "add_relationship",
                new
                {
                    source_table = "Customer",
                    target_table = "Order",
                    source_columns = new[] { "CustomerId", "Name" },
                    target_columns = new[] { "CustomerId", "ShipToCustomerId" },
                    relationship_type = "OneToMany",
                },
                e =>
                    e.Ok()
                        .Result.Contains("'Customer'", "Name", "source_columns")
                        .Schema.Kind(
                            "rel",
                            "rel Customer->Order OneToMany * pairs=(CustomerId>CustomerId,Name>ShipToCustomerId)"
                        )
            );

        yield return Row(
                "note-add-relationship-explicit-candidate-key-has-no-note",
                "add_relationship は明示した親側の列が主キー全列か一意制約と一致すれば、並びを問わず注意を添えない"
            )
            .Base(CustomerOrder)
            .Arrange("add_entity", new { table_name = "Region" })
            .Arrange(
                "add_column",
                new
                {
                    table_name = "Region",
                    column_name = "RegionId",
                    data_type = "int",
                    is_primary_key = true,
                }
            )
            .Arrange(
                "add_column",
                new
                {
                    table_name = "Region",
                    column_name = "Code",
                    data_type = "int",
                    is_primary_key = true,
                }
            )
            .Arrange(
                "add_column",
                new
                {
                    table_name = "Region",
                    column_name = "Slug",
                    data_type = "nvarchar(20)",
                }
            )
            .Arrange(
                "set_unique_constraint",
                new { table_name = "Region", columns = new[] { "Slug" } }
            )
            .Arrange(
                "add_column",
                new
                {
                    table_name = "Order",
                    column_name = "RegionId",
                    data_type = "int",
                }
            )
            .Arrange(
                "add_column",
                new
                {
                    table_name = "Order",
                    column_name = "RegionCode",
                    data_type = "int",
                }
            )
            .Arrange(
                "add_column",
                new
                {
                    table_name = "Order",
                    column_name = "RegionSlug",
                    data_type = "int",
                }
            )
            // 主キー全列（宣言と逆の並び）
            .Call(
                "add_relationship",
                new
                {
                    source_table = "Region",
                    target_table = "Order",
                    source_columns = new[] { "Code", "RegionId" },
                    target_columns = new[] { "RegionCode", "RegionId" },
                    relationship_type = "OneToMany",
                },
                e => e.Ok().Result.Absent("source_columns")
            )
            // 一意制約の列
            .Call(
                "add_relationship",
                new
                {
                    source_table = "Region",
                    target_table = "Order",
                    source_columns = new[] { "Slug" },
                    target_columns = new[] { "RegionSlug" },
                    relationship_type = "OneToMany",
                },
                e => e.Ok().Result.Absent("source_columns")
            );

        // ---------------- 複数行の応答の改行 ----------------

        yield return Row(
                "line-ending-summary-uses-lf-only",
                "get_diagram_summary の応答は、どちらのホストでも改行が LF だけ"
            )
            .Base(CustomerOrder)
            .Arrange(
                "add_relationship",
                new
                {
                    source_table = "Customer",
                    target_table = "Order",
                    source_columns = new[] { "CustomerId" },
                    target_columns = new[] { "CustomerId" },
                    relationship_type = "OneToMany",
                }
            )
            .Arrange(
                "set_unique_constraint",
                new { table_name = "Customer", columns = new[] { "Name" } }
            )
            .Arrange("set_entity_property", new { table_name = "Customer", description = "Buyers" })
            .Call(
                "get_diagram_summary",
                new { },
                // 複数行の応答を作れている（\n を含む）うえで、\r が無い
                e => e.Ok().Result.Contains("\n").Result.Absent("\r")
            );

        // 要約は「1 行が 1 件の記録」の形なので、表・列の説明の改行（CR LF・CR・LF・U+2028）は空白 1 つへ畳む。
        // 畳まないと説明の続きが行頭に出て、偽の [表] 見出しや偽の列の行に読める
        var lineSeparator = ((char)0x2028).ToString();

        yield return Row(
                "line-ending-summary-folds-description-line-breaks",
                "get_diagram_summary は表・列の説明の改行を空白へ畳み、偽の見出し・列の行を作らない"
            )
            .Base(CustomerOrder)
            .Arrange(
                "set_entity_property",
                new
                {
                    table_name = "Customer",
                    description = "Buyers\r\nfirst\rsecond\nthird"
                        + lineSeparator
                        + "[Ghost]\n  - Ghost: int",
                }
            )
            .Arrange(
                "set_column_property",
                new
                {
                    table_name = "Customer",
                    column_name = "Name",
                    description = "Name of\r\nthe\rbuyer\n[Phantom]"
                        + lineSeparator
                        + "  - Phantom: int",
                }
            )
            .Call(
                "get_diagram_summary",
                new { },
                e =>
                    e.Ok()
                        // 畳んだ説明は 1 行に入る（表の説明は見出しの下の 1 行・列の説明は列の行の末尾）
                        .Result.Contains(
                            "Buyers first second third [Ghost]   - Ghost: int\n",
                            "\n  - Name: nvarchar(50) // Name of the buyer [Phantom]   - Phantom: int\n"
                        )
                        // 説明の続きが行頭に出ない（偽の見出し・偽の列の行・改行の残り）
                        .Result.Absent(
                            "\n[Ghost]",
                            "\n  - Ghost",
                            "\n[Phantom]",
                            "\n  - Phantom",
                            "\nfirst",
                            "\nthird",
                            "\nthe",
                            "\nbuyer",
                            "\r",
                            lineSeparator
                        )
            );
    }
}
