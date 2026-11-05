using QuickER.Dialects;
using QuickER.Model;
using QuickER.Tests.TestSupport;

namespace QuickER.Tests.Mcp.Tools;

internal static partial class ToolScenarioRows
{
    /// <summary>128 文字の親テーブル名（SQL Server の識別子の長さの上限ちょうど）</summary>
    private static string LongParent => new('p', 128);

    /// <summary>128 文字の子テーブル名</summary>
    private static string LongChild => new('c', 128);

    /// <summary>上限を超える名前（SQL Server は 128 の UTF-16 コード単位）</summary>
    private static string OverLimitName => new('a', 129);

    /// <summary>128 文字の親子テーブル（どちらも主キー id を持ち、子に parent_id を足す）</summary>
    private static IEnumerable<(string Tool, object Args)> LongTables =>
        [
            ("add_entity", new { table_name = LongParent }),
            (
                "add_column",
                new
                {
                    table_name = LongParent,
                    column_name = "id",
                    data_type = "int",
                    is_primary_key = true,
                }
            ),
            ("add_entity", new { table_name = LongChild }),
            (
                "add_column",
                new
                {
                    table_name = LongChild,
                    column_name = "id",
                    data_type = "int",
                    is_primary_key = true,
                }
            ),
            (
                "add_column",
                new
                {
                    table_name = LongChild,
                    column_name = "parent_id",
                    data_type = "int",
                }
            ),
        ];

    private static object LongRelationshipArgs(string childColumn) =>
        new
        {
            source_table = LongParent,
            target_table = LongChild,
            relationship_type = "OneToMany",
            source_columns = new[] { "id" },
            target_columns = new[] { childColumn },
        };

    /// <summary>名前の無いリレーション（取込・古いファイル・手編集で生じる）を 1 本持つ 128 文字の親子の図</summary>
    private static ToolRow WithUnnamedLongRelationship(ToolRow row) =>
        row.Base(CustomerOrder)
            .Base(LongTables)
            .Arrange("add_relationship", LongRelationshipArgs("parent_id"))
            .Seed("リレーションの制約名を消す", session => session.ClearConstraintNames());

    /// <summary>
    /// 既定の制約名を図の対象 DBMS の上限に収めること、名前の上限を超える名前への注意文、一意制約の名前の衝突検査、
    /// 名前の無いリレーションの DDL 上の名前
    /// </summary>
    private static IEnumerable<ToolRow> NamesRows()
    {
        var shortenedForeignKey = ConstraintNames.ForeignKey(
            LongChild,
            LongParent,
            IdentifierLengthLimits.SqlServer
        );
        var unshortenedForeignKey = ConstraintNames.ForeignKey(
            LongChild,
            LongParent,
            IdentifierLengthLimit.Unlimited
        );

        // ---------------- 既定名を上限に収める ----------------

        yield return Row(
                "long-default-name-add-relationship-stores-shortened-default",
                "add_relationship: 名前を省くと、図の対象 DBMS の上限に収めた既定名を保存する"
            )
            .Base(CustomerOrder)
            .Base(LongTables)
            .Call(
                "add_relationship",
                LongRelationshipArgs("parent_id"),
                e =>
                    e.Ok()
                        .Schema.Single(
                            $"rel {LongParent}->{LongChild} OneToMany name={shortenedForeignKey} *"
                        )
                        .Check(
                            "保存した名前が上限に収まる",
                            observation =>
                            {
                                var line = observation.After.Single(line =>
                                    line.StartsWith(
                                        $"rel {LongParent}->{LongChild} ",
                                        StringComparison.Ordinal
                                    )
                                );
                                var start = line.IndexOf(" name=", StringComparison.Ordinal) + 6;
                                var name = line[
                                    start..line.IndexOf(" del=", StringComparison.Ordinal)
                                ];

                                return IdentifierLengthLimits.SqlServer.Fits(name)
                                    ? null
                                    : $"上限を超える名前: {name}";
                            }
                        )
            );

        var synthesizedShortened = UniqueConstraint.SynthesizeName(
            LongChild,
            ["parent_id"],
            IdentifierLengthLimits.SqlServer
        );
        var synthesizedUnshortened = UniqueConstraint.SynthesizeName(
            LongChild,
            ["parent_id"],
            IdentifierLengthLimit.Unlimited
        );

        yield return Row(
                "long-default-name-summary-shows-shortened-synthesized-unique-name",
                "get_diagram_summary: 一意制約の合成名は、図の対象 DBMS の上限に収めた名前（DDL と同じ）を見せる"
            )
            .Base(CustomerOrder)
            .Base(LongTables)
            .Arrange(
                "set_unique_constraint",
                new { table_name = LongChild, columns = new[] { "parent_id" } }
            )
            .Call(
                "get_diagram_summary",
                new { },
                e =>
                    e.Ok()
                        .Result.Contains(synthesizedShortened)
                        .Result.Absent(synthesizedUnshortened)
            );

        yield return WithUnnamedLongRelationship(
                Row(
                    "long-default-name-add-relationship-name-equal-to-shortened-default-of-unnamed-is-rejected",
                    "add_relationship: 名前の無い外部キーが持つ、上限で縮めた既定名と同じ constraint_name は断る"
                )
            )
            .Arrange(
                "add_column",
                new
                {
                    table_name = LongChild,
                    column_name = "parent_id2",
                    data_type = "int",
                }
            )
            .Call(
                "add_relationship",
                new
                {
                    source_table = LongParent,
                    target_table = LongChild,
                    relationship_type = "OneToMany",
                    source_columns = new[] { "id" },
                    target_columns = new[] { "parent_id2" },
                    constraint_name = shortenedForeignKey,
                },
                e => e.Fails().Result.Contains(shortenedForeignKey).Schema.Kind("rel", "rel *")
            );

        var shortenedPrimaryKey = ConstraintNames.PrimaryKey(
            LongChild,
            IdentifierLengthLimits.SqlServer
        );

        yield return Row(
                "long-default-name-set-unique-constraint-name-equal-to-shortened-primary-key-name-is-rejected",
                "set_unique_constraint: 主キーの、上限で縮めた既定名と同じ name は断る"
            )
            .Base(CustomerOrder)
            .Base(LongTables)
            .Call(
                "set_unique_constraint",
                new
                {
                    table_name = LongChild,
                    columns = new[] { "parent_id" },
                    name = shortenedPrimaryKey,
                },
                e => e.Fails().Result.Contains(shortenedPrimaryKey).Schema.Kind("uq")
            );

        yield return WithUnnamedLongRelationship(
                Row(
                    "long-default-name-unnamed-relationship-is-shown-and-removed-by-shortened-default",
                    "名前の無い外部キーは、上限で縮めた既定名（DDL と同じ）で要約に出て、その名前で消せる"
                )
            )
            .Call(
                "get_diagram_summary",
                new { },
                e =>
                    e.Ok().Result.Contains(shortenedForeignKey).Result.Absent(unshortenedForeignKey)
            )
            .Call(
                "remove_relationship",
                new
                {
                    source_table = LongParent,
                    target_table = LongChild,
                    constraint_name = shortenedForeignKey,
                },
                e => e.Ok().Schema.Kind("rel")
            );

        yield return WithUnnamedLongRelationship(
                Row(
                    "long-default-name-add-relationship-duplicate-of-unnamed-names-it-by-shortened-default",
                    "add_relationship: 名前の無い外部キーと同じ列ペアの重複は、上限で縮めた既定名でその外部キーを名指しして断る"
                )
            )
            .Call(
                "add_relationship",
                LongRelationshipArgs("parent_id"),
                e =>
                    e.Fails()
                        .Result.Contains(shortenedForeignKey)
                        .Result.Absent(unshortenedForeignKey)
            );

        // ---------------- 名前の上限を超える名前への注意文 ----------------

        yield return Row(
                "name-limit-add-entity-over-limit-appends-note",
                "add_entity: 上限を超えるテーブル名は保存したうえで注意文を添える"
            )
            .Base(CustomerOrder)
            .Call(
                "add_entity",
                new { table_name = OverLimitName },
                e =>
                    e.Ok()
                        .Result.Contains("Note:", OverLimitName, "128 UTF-16 code units")
                        .Schema.Contains($"table {OverLimitName} *")
            );

        yield return Row(
                "name-limit-add-column-over-limit-appends-note",
                "add_column: 上限を超えるカラム名に注意文を添える"
            )
            .Base(CustomerOrder)
            .Call(
                "add_column",
                new
                {
                    table_name = "Customer",
                    column_name = OverLimitName,
                    data_type = "int",
                },
                e => e.Ok().Result.Contains("Note:", "column of 'Customer'", OverLimitName)
            );

        yield return Row(
                "name-limit-rename-table-over-limit-appends-note",
                "set_entity_property: 上限を超えるテーブル名への変更に注意文を添える"
            )
            .Base(CustomerOrder)
            .Call(
                "set_entity_property",
                new { table_name = "Customer", new_table_name = OverLimitName },
                e => e.Ok().Result.Contains("Note:", OverLimitName)
            );

        yield return Row(
                "name-limit-add-relationship-over-limit-constraint-name-appends-note",
                "add_relationship: 上限を超える constraint_name に注意文を添える"
            )
            .Base(CustomerOrder)
            .Call(
                "add_relationship",
                new
                {
                    source_table = "Customer",
                    target_table = "Order",
                    relationship_type = "OneToMany",
                    source_columns = new[] { "CustomerId" },
                    target_columns = new[] { "CustomerId" },
                    constraint_name = OverLimitName,
                },
                e => e.Ok().Result.Contains("Note:", "foreign key", OverLimitName)
            );

        yield return Row(
                "name-limit-set-unique-constraint-over-limit-name-appends-note",
                "set_unique_constraint: 上限を超える name に注意文を添える"
            )
            .Base(CustomerOrder)
            .Call(
                "set_unique_constraint",
                new
                {
                    table_name = "Customer",
                    columns = new[] { "Name" },
                    name = OverLimitName,
                },
                e =>
                    e.Ok()
                        .Result.Contains("Note:", "unique constraint of 'Customer'", OverLimitName)
            );

        yield return Row(
                "name-limit-within-limit-adds-no-note-and-existing-violation-is-not-repeated",
                "上限内の名前には注意文を添えず、すでにある違反を次の呼び出しで繰り返さない"
            )
            .Base(CustomerOrder)
            .Call("add_entity", new { table_name = "Product" }, e => e.Ok().Result.Absent("Note:"))
            .Call(
                "add_entity",
                new { table_name = OverLimitName },
                e => e.Ok().Result.Contains("Note:")
            )
            .Call(
                "add_entity",
                new { table_name = "Category" },
                e => e.Ok().Result.Absent("Note:")
            );

        // ---------------- 一意制約の名前の衝突検査 ----------------

        foreach (
            var (shape, secondName) in new[]
            {
                ("same-name", "UQ_Customer_Name"),
                ("case-only-difference", "uq_customer_name"),
                ("padded-name", "  UQ_Customer_Name  "),
            }
        )
        {
            yield return Row(
                    $"unique-constraint-name-repeated-with-own-name-succeeds-{shape}",
                    "set_unique_constraint: 同じ列集合・同じ名前（大文字小文字違いも）で呼び直しても成功し、制約は 1 つのまま"
                )
                .Base(CustomerOrder)
                .Arrange(
                    "set_unique_constraint",
                    new
                    {
                        table_name = "Customer",
                        columns = new[] { "Name" },
                        name = "UQ_Customer_Name",
                    }
                )
                .Call(
                    "set_unique_constraint",
                    new
                    {
                        table_name = "Customer",
                        columns = new[] { "Name" },
                        name = secondName,
                    },
                    e => e.Ok().Schema.Kind("uq", $"uq Customer.{secondName.Trim()} (Name)")
                );
        }

        yield return Row(
                "unique-constraint-name-of-another-constraint-is-rejected",
                "set_unique_constraint: 別の制約が使っている名前は拒否し、図を変えない"
            )
            .Base(CustomerOrder)
            .Arrange(
                "set_unique_constraint",
                new
                {
                    table_name = "Customer",
                    columns = new[] { "Name" },
                    name = "UQ_Customer_Name",
                }
            )
            .Call(
                "set_unique_constraint",
                new
                {
                    table_name = "Order",
                    columns = new[] { "CustomerId" },
                    name = "uq_customer_name",
                },
                e =>
                    e.Fails()
                        .Result.Contains("uq_customer_name")
                        .Schema.Kind("uq", "uq Customer.UQ_Customer_Name (Name)")
            );

        // ---------------- 名前の無いリレーションの DDL 上の名前 ----------------

        // 同じ向きの 2 本は、先の 1 本が既定名を取り、後の 1 本は子の列名を足した名前になる（DDL・同期と同じ正本から求める）
        var (nameByCustomerId, nameByShipTo) = DdlNamesOfTwoUnnamedRelationships();

        yield return TwoUnnamedRelationships(
                Row(
                    "unnamed-relationship-remove-selects-unnamed-by-its-ddl-name",
                    "同じ向きのテーブル対に名前の無いリレーションが 2 本あっても、DDL が付ける名前で 1 本を選んで消せる"
                )
            )
            .Call(
                "remove_relationship",
                new
                {
                    source_table = "Customer",
                    target_table = "Order",
                    constraint_name = nameByCustomerId,
                },
                e =>
                    e.Ok()
                        .Schema.Kind(
                            "rel",
                            "rel Customer->Order OneToMany name=* del=* upd=* pairs=(CustomerId>ShipToCustomerId)"
                        )
            );

        yield return TwoUnnamedRelationships(
                Row(
                    "unnamed-relationship-summary-shows-ddl-name",
                    "get_diagram_summary は、名前の無いリレーションにも DDL が付ける名前を示す"
                )
            )
            .Call(
                "get_diagram_summary",
                new { },
                e =>
                    e.Ok()
                        .Result.Contains(nameByCustomerId, nameByShipTo)
                        .Schema.Kind(
                            "rel",
                            "rel Customer->Order OneToMany name=- *",
                            "rel Customer->Order OneToMany name=- *"
                        )
            );
    }

    /// <summary>Customer → Order に、CustomerId と ShipToCustomerId の 2 本の名前の無いリレーションを用意する</summary>
    private static ToolRow TwoUnnamedRelationships(ToolRow row)
    {
        row.Base(CustomerOrder);

        foreach (var column in new[] { "CustomerId", "ShipToCustomerId" })
        {
            row.Arrange(
                "add_relationship",
                new
                {
                    source_table = "Customer",
                    target_table = "Order",
                    source_columns = new[] { "CustomerId" },
                    target_columns = new[] { column },
                    relationship_type = "OneToMany",
                }
            );
        }

        return row.Seed("リレーションの制約名を消す", session => session.ClearConstraintNames());
    }

    /// <summary>
    /// <see cref="TwoUnnamedRelationships"/> の図（SQL Server）の 2 本について、DDL が付ける名前
    /// （<see cref="ConstraintNames.ResolveForeignKeyNames"/>）を求める。両ホストの Guid に依らないよう、同じ形の図を作って引く
    /// </summary>
    private static (
        string ByCustomerId,
        string ByShipToCustomerId
    ) DdlNamesOfTwoUnnamedRelationships()
    {
        var customerId = new Column { Name = "CustomerId", IsPrimaryKey = true };
        var customer = new Entity { TableName = "Customer", Columns = [customerId] };
        var orderCustomerId = new Column { Name = "CustomerId" };
        var shipTo = new Column { Name = "ShipToCustomerId" };
        var order = new Entity
        {
            TableName = "Order",
            Columns =
            [
                new Column { Name = "OrderId", IsPrimaryKey = true },
                orderCustomerId,
                shipTo,
            ],
        };
        var byCustomerId = new Relationship
        {
            SourceEntityId = customer.Id,
            TargetEntityId = order.Id,
            ColumnPairs = [new RelationshipColumnPair(customerId.Id, orderCustomerId.Id)],
        };
        var byShipTo = new Relationship
        {
            SourceEntityId = customer.Id,
            TargetEntityId = order.Id,
            ColumnPairs = [new RelationshipColumnPair(customerId.Id, shipTo.Id)],
        };
        var names = ConstraintNames.ResolveForeignKeyNames(
            [customer, order],
            [byCustomerId, byShipTo],
            IdentifierLengthLimits.SqlServer
        );

        return (names[byCustomerId], names[byShipTo]);
    }
}
