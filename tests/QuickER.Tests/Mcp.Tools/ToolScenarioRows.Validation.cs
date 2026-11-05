using QuickER.Tests.TestSupport;

namespace QuickER.Tests.Mcp.Tools;

internal static partial class ToolScenarioRows
{
    /// <summary>
    /// 入力の検証（空・大文字小文字だけ違う名前・空のデータ型・relationship_type・同じ列ペアの外部キーの重複）。
    /// 拒否した呼び出しが図を一部だけ変えていないことも見る（GUI は変更をその場で反映するため、検証が変更より後ろにあると
    /// 一部だけ反映される）
    /// </summary>
    private static IEnumerable<ToolRow> ValidationRows()
    {
        // ---------------- テーブル名 ----------------

        foreach (var (shape, tableName) in new[] { ("empty", ""), ("spaces", "   ") })
        {
            yield return Row(
                    $"validation-add-entity-blank-name-is-rejected-{shape}",
                    "add_entity は空・空白だけのテーブル名を拒否する"
                )
                .Base(CustomerOrder)
                .Call(
                    "add_entity",
                    new { table_name = tableName },
                    e => e.Fails().Schema.Kind("table", "table Customer *", "table Order *")
                );
        }

        yield return Row(
                "validation-add-entity-case-only-duplicate-is-rejected",
                "add_entity は既存テーブルと大文字小文字だけ違う名前を拒否する"
            )
            .Base(CustomerOrder)
            .Call(
                "add_entity",
                new { table_name = "customer" },
                e =>
                    e.Fails()
                        .Result.Contains("'Customer'")
                        .Schema.Kind("table", "table Customer *", "table Order *")
            );

        yield return Row(
                "validation-rename-entity-to-other-table-is-rejected-without-partial-change",
                "set_entity_property は他のテーブルと同じ名前への改名を拒否し、同時に指定した変更も反映しない"
            )
            .Base(CustomerOrder)
            .Call(
                "set_entity_property",
                new
                {
                    table_name = "Order",
                    new_table_name = "CUSTOMER",
                    memo = "must not be applied",
                },
                e =>
                    e.Fails().Result.Contains("'Customer'").Schema.Single("table Order * memo=\"\"")
            );

        yield return Row(
                "validation-rename-entity-blank-name-is-rejected",
                "set_entity_property は空白だけの new_table_name を拒否する"
            )
            .Base(CustomerOrder)
            .Call(
                "set_entity_property",
                new { table_name = "Order", new_table_name = "  " },
                e => e.Fails().Schema.Kind("table", "table Customer *", "table Order *")
            );

        yield return Row(
                "validation-rename-entity-case-only-of-itself-is-allowed",
                "set_entity_property は自分自身の大文字小文字だけを変える改名を許す"
            )
            .Base(CustomerOrder)
            .Call(
                "set_entity_property",
                new { table_name = "Order", new_table_name = "ORDER" },
                e => e.Ok().Schema.Kind("table", "table Customer *", "table ORDER *")
            );

        // ---------------- 列名・データ型 ----------------

        yield return Row(
                "validation-add-column-case-only-duplicate-is-rejected",
                "add_column は同じテーブルの列と大文字小文字だけ違う名前を拒否する"
            )
            .Base(CustomerOrder)
            .Call(
                "add_column",
                new
                {
                    table_name = "Order",
                    column_name = "customerid",
                    data_type = "int",
                },
                e =>
                    e.Fails()
                        .Result.Contains("'CustomerId'")
                        .Schema.Exact(
                            "table Customer *",
                            "col Customer.CustomerId *",
                            "col Customer.Name *",
                            "pkorder Customer *",
                            "pkids Customer *",
                            "table Order *",
                            "col Order.OrderId *",
                            "col Order.CustomerId *",
                            "col Order.ShipToCustomerId *",
                            "pkorder Order *",
                            "pkids Order *"
                        )
            );

        yield return Row(
                "validation-add-column-same-name-in-another-table-is-allowed",
                "add_column は別のテーブルにある同名の列を妨げない"
            )
            .Base(CustomerOrder)
            .Call(
                "add_column",
                new
                {
                    table_name = "Order",
                    column_name = "Name",
                    data_type = "nvarchar(50)",
                },
                e => e.Ok()
            );

        yield return Row(
                "validation-add-column-blank-or-missing-data-type-is-rejected",
                "add_column は data_type の省略と空・空白だけの値を拒否する（既定の型で黙って列を作らない）"
            )
            .Base(CustomerOrder)
            .Call(
                "add_column",
                new
                {
                    table_name = "Order",
                    column_name = "Note",
                    data_type = " ",
                },
                e => e.Fails()
            )
            .Call(
                "add_column",
                new { table_name = "Order", column_name = "Memo" },
                e => e.Fails().Schema.Absent("col Order.Note *", "col Order.Memo *")
            );

        yield return Row(
                "validation-set-column-blank-data-type-is-rejected-before-any-change",
                "set_column_property は空の data_type を拒否し、同時に指定した説明も反映しない"
            )
            .Base(CustomerOrder)
            .Call(
                "set_column_property",
                new
                {
                    table_name = "Order",
                    column_name = "CustomerId",
                    description = "must not be applied",
                    data_type = "",
                },
                e => e.Fails().Schema.Single("col Order.CustomerId int * desc=\"\"")
            );

        // ---------------- relationship_type ----------------

        foreach (
            var (relationshipType, expected) in new[]
            {
                ("onetoone", "OneToOne"),
                ("ONETOMANY", "OneToMany"),
            }
        )
        {
            yield return Row(
                    $"validation-relationship-type-ignores-case-{relationshipType}",
                    "add_relationship の relationship_type は大文字小文字を無視して受ける"
                )
                .Base(CustomerOrder)
                .Call(
                    "add_relationship",
                    new
                    {
                        source_table = "Customer",
                        target_table = "Order",
                        source_columns = new[] { "CustomerId" },
                        target_columns = new[] { "CustomerId" },
                        relationship_type = relationshipType,
                    },
                    e => e.Ok().Schema.Kind("rel", $"rel * {expected} *")
                );
        }

        yield return Row(
                "validation-relationship-type-missing-is-reported-as-required",
                "add_relationship は relationship_type の省略を、無効な値でなく必須の欠落として断る"
            )
            .Base(CustomerOrder)
            .Call(
                "add_relationship",
                new
                {
                    source_table = "Customer",
                    target_table = "Order",
                    source_columns = new[] { "CustomerId" },
                    target_columns = new[] { "CustomerId" },
                },
                e =>
                    e.Fails()
                        .ForHost(
                            ToolHostKind.Gui,
                            "GUI の応答は resx（UI の言語）・MCP は英語の固定文",
                            gui =>
                                gui.Result.Exact(
                                    string.Format(
                                        QuickER.Resources.Strings.Tool_RelationshipTypeRequired,
                                        "OneToOne, OneToMany, ManyToMany"
                                    )
                                )
                        )
                        .ForHost(
                            ToolHostKind.File,
                            "GUI の応答は resx（UI の言語）・MCP は英語の固定文",
                            file =>
                                file.Result.Exact(
                                    "relationship_type is required (one of: OneToOne, OneToMany, ManyToMany)."
                                )
                        )
            );

        foreach (
            var (shape, relationshipType) in new (string, string?)[]
            {
                ("null", null),
                ("snake-case", "one_to_many"),
                ("empty", ""),
                ("number", "1"),
            }
        )
        {
            yield return Row(
                    $"validation-relationship-type-unknown-is-rejected-{shape}",
                    "add_relationship は省略・未知の relationship_type を一対多へ落とさず拒否する"
                )
                .Base(CustomerOrder)
                .Call(
                    "add_relationship",
                    new
                    {
                        source_table = "Customer",
                        target_table = "Order",
                        source_columns = new[] { "CustomerId" },
                        target_columns = new[] { "CustomerId" },
                        relationship_type = relationshipType,
                    },
                    e =>
                        e.Fails()
                            .Result.Contains("OneToOne, OneToMany, ManyToMany")
                            .Schema.Kind("rel")
                );
        }

        // ---------------- 外部キーの重複 ----------------

        yield return Row(
                "validation-relationship-same-column-pairs-twice-is-rejected",
                "add_relationship は同じ親子・同じ列ペアの外部キーの重複を拒否する"
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
            // 種別・制約名が違っても、同じ列ペアなら同じ外部キー
            .Call(
                "add_relationship",
                new
                {
                    source_table = "customer",
                    target_table = "order",
                    source_columns = new[] { "customerid" },
                    target_columns = new[] { "customerid" },
                    relationship_type = "OneToOne",
                    constraint_name = "FK_Another",
                },
                e => e.Fails().Result.Contains("FK_Order_Customer").Schema.Kind("rel", "rel *")
            );

        yield return Row(
                "validation-relationship-same-column-pairs-other-order-is-rejected",
                "add_relationship は列ペアの並びだけが違う複合外部キーも重複として拒否する"
            )
            .Base(CustomerOrder)
            .Arrange(
                "add_relationship",
                new
                {
                    source_table = "Customer",
                    target_table = "Order",
                    source_columns = new[] { "CustomerId", "Name" },
                    target_columns = new[] { "CustomerId", "ShipToCustomerId" },
                    relationship_type = "OneToMany",
                }
            )
            .Call(
                "add_relationship",
                new
                {
                    source_table = "Customer",
                    target_table = "Order",
                    source_columns = new[] { "Name", "CustomerId" },
                    target_columns = new[] { "ShipToCustomerId", "CustomerId" },
                    relationship_type = "OneToMany",
                },
                e => e.Fails().Schema.Kind("rel", "rel *")
            );

        yield return Row(
                "validation-relationship-different-column-pairs-is-allowed",
                "add_relationship は同じ親子でも列ペアが違えば 2 本目を許す"
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
            .Call(
                "add_relationship",
                new
                {
                    source_table = "Customer",
                    target_table = "Order",
                    source_columns = new[] { "CustomerId" },
                    target_columns = new[] { "ShipToCustomerId" },
                    relationship_type = "OneToMany",
                },
                e => e.Ok().Schema.Kind("rel", "rel *", "rel *")
            );

        var manyToMany = new
        {
            source_table = "Customer",
            target_table = "Order",
            source_columns = new[] { "CustomerId" },
            target_columns = new[] { "CustomerId" },
            relationship_type = "ManyToMany",
        };

        yield return Row(
                "validation-relationship-many-to-many-twice-is-allowed",
                "add_relationship は列ペアを持たない多対多の 2 本目を重複として扱わない"
            )
            .Base(CustomerOrder)
            .Arrange("add_relationship", manyToMany)
            .Call("add_relationship", manyToMany, e => e.Ok().Schema.Kind("rel", "rel *", "rel *"));
    }
}
