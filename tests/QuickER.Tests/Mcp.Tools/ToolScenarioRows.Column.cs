using QuickER.Model;
using QuickER.Tests.TestSupport;

namespace QuickER.Tests.Mcp.Tools;

internal static partial class ToolScenarioRows
{
    /// <summary>add_column の引数（<paramref name="pk"/>・<paramref name="nullable"/>・<paramref name="description"/> は null なら渡さない）</summary>
    private static Dictionary<string, object> ColumnArgs(
        string table,
        string column,
        string type,
        bool? pk = null,
        bool? nullable = null,
        string? description = null
    )
    {
        var args = new Dictionary<string, object>
        {
            ["table_name"] = table,
            ["column_name"] = column,
            ["data_type"] = type,
        };

        if (pk is { } isPrimaryKey)
        {
            args["is_primary_key"] = isPrimaryKey;
        }

        if (nullable is { } isNullable)
        {
            args["is_nullable"] = isNullable;
        }

        if (description is not null)
        {
            args["description"] = description;
        }

        return args;
    }

    /// <summary>Book に主キー BookId（int・NOT NULL）を足すところまでの前提</summary>
    private static ToolRow BookWithKey(ToolRow row) =>
        row.Arrange("add_entity", new { table_name = "Book" })
            .Arrange(
                "add_column",
                ColumnArgs("Book", "BookId", "int", pk: true, nullable: false, description: "")
            );

    /// <summary>add_column・set_column_property・remove_column（列ペアの後始末）</summary>
    private static IEnumerable<ToolRow> ColumnRows()
    {
        // ---------------- add_column ----------------

        yield return Row(
                "column-add-primary-key-sets-flags-sqlserver",
                "add_column で is_primary_key=true の列を追加できる"
            )
            .Arrange("add_entity", new { table_name = "Book" })
            .Call(
                "add_column",
                ColumnArgs("Book", "BookId", "int", pk: true, nullable: false),
                e => e.Ok().Schema.Kind("col", "col Book.BookId * pk *")
            );

        yield return Row(
                "column-add-primary-key-sets-flags-sqlite",
                "add_column は主キー・NULL 許容フラグを指定どおり設定する"
            )
            .Dialect("sqlite")
            .Arrange("add_entity", new { table_name = "Book" })
            .Call(
                "add_column",
                ColumnArgs("Book", "BookId", "int", pk: true, nullable: false),
                e => e.Ok().Schema.Kind("col", "col Book.BookId * pk notnull *")
            );

        yield return Row(
                "column-add-multiple-columns-reflects-design-sqlserver",
                "add_column を複数回呼び出すと AI が設計した列構成になる"
            )
            .Arrange("add_entity", new { table_name = "Book" })
            .Call(
                "add_column",
                ColumnArgs("Book", "BookId", "int", pk: true, nullable: false),
                e => e.Ok()
            )
            .Call(
                "add_column",
                ColumnArgs("Book", "Title", "nvarchar(200)", pk: false, nullable: false),
                e => e.Ok()
            )
            .Call(
                "add_column",
                ColumnArgs("Book", "AuthorId", "int", pk: false, nullable: false),
                e =>
                    e.Ok()
                        .Schema.Kind(
                            "col",
                            "col Book.BookId * pk *",
                            "col Book.Title * nopk *",
                            "col Book.AuthorId * nopk *"
                        )
            );

        yield return Row(
                "column-add-primary-key-without-nullable-flag-is-not-nullable-sqlite",
                "add_column は is_nullable 省略でも主キー列を NULL 不可にする"
            )
            .Dialect("sqlite")
            .Arrange("add_entity", new { table_name = "Book" })
            .Call(
                "add_column",
                ColumnArgs("Book", "BookId", "int", pk: true),
                e => e.Ok().Schema.Kind("col", "col Book.BookId * pk notnull *")
            );

        yield return Row(
                "column-add-unknown-table-is-rejected-sqlite",
                "add_column は存在しないテーブルをエラーにする"
            )
            .Dialect("sqlite")
            .Call("add_column", ColumnArgs("NoSuch", "X", "int"), e => e.Fails());

        // ---------------- set_column_property ----------------

        yield return BookWithKey(
                Row(
                    "column-set-property-updates-description-sqlserver",
                    "set_column_property でカラムの説明を更新できる"
                )
            )
            .Call(
                "set_column_property",
                new
                {
                    table_name = "Book",
                    column_name = "BookId",
                    description = "書籍を一意に識別するID",
                },
                e => e.Ok().Schema.Single("col Book.BookId * desc=\"書籍を一意に識別するID\"")
            );

        yield return Row(
                "column-set-property-updates-data-type-and-nullable-sqlserver",
                "set_column_property でデータ型と NULL 許容を更新できる"
            )
            .Arrange("add_entity", new { table_name = "Book" })
            .Arrange(
                "add_column",
                ColumnArgs("Book", "Title", "nvarchar(100)", pk: false, nullable: false)
            )
            .Call(
                "set_column_property",
                new
                {
                    table_name = "Book",
                    column_name = "Title",
                    data_type = "nvarchar(500)",
                    is_nullable = true,
                },
                e => e.Ok().Schema.Single("col Book.Title nvarchar(500) * null *")
            );

        yield return Row(
                "column-set-property-updates-data-type-and-nullable-sqlite",
                "set_column_property はデータ型と NULL 許容を更新する"
            )
            .Dialect("sqlite")
            .Arrange("add_entity", new { table_name = "Book" })
            .Arrange("add_column", ColumnArgs("Book", "Title", "nvarchar(100)", nullable: false))
            .Call(
                "set_column_property",
                new
                {
                    table_name = "Book",
                    column_name = "Title",
                    data_type = "nvarchar(500)",
                    is_nullable = true,
                },
                e => e.Ok().Schema.Single("col Book.Title nvarchar(500) * null *")
            );

        yield return Row(
                "column-set-property-unknown-column-is-rejected-sqlserver",
                "set_column_property で存在しないカラムを指定するとエラーになる"
            )
            .Arrange("add_entity", new { table_name = "Book" })
            .Call(
                "set_column_property",
                new
                {
                    table_name = "Book",
                    column_name = "NoSuchColumn",
                    description = "説明",
                },
                e => e.Fails()
            );

        // ---------------- remove_column ----------------

        yield return SqliteParentChild(
                Row(
                    "column-remove-foreign-key-column-clears-relationship-pair-sqlite",
                    "remove_column は FK 列削除時にリレーションの参照をクリアする"
                )
            )
            .Arrange(
                "add_relationship",
                new
                {
                    source_table = "Customer",
                    target_table = "Order",
                    relationship_type = "OneToMany",
                }
            )
            .Call(
                "remove_column",
                new { table_name = "Order", column_name = "CustomerId" },
                e =>
                    e.Ok()
                        .Schema.Absent("col Order.CustomerId *")
                        .Schema.Kind("rel", "rel Customer->Order OneToMany * pairs=()")
            );

        yield return Row(
                "column-remove-missing-column-is-rejected",
                "remove_column は存在しない列を断り、図を変えない"
            )
            .Base(CustomerOrder)
            .Call(
                "remove_column",
                new { table_name = "Order", column_name = "Note" },
                e => e.Fails().Unchanged()
            );

        // ---------------- remove_column と名前付きクエリの列参照 ----------------
        // 削除はクエリを書き換えず、削除した列を参照するクエリを応答で名指しする（両ホストで同じ英語の注意文）

        yield return Row(
                "column-remove-referenced-by-queries-names-each-query",
                "remove_column はクエリを書き換えず、削除した列を参照するクエリを参照の種類つきで名指しする"
            )
            .Base(CustomerOrder)
            .Seed(
                "Order のクエリ 4 本が CustomerId を参照する（パラメータ・並び順・射影フィールド・条件）",
                session =>
                {
                    SeedQueryOn(
                        session,
                        "Order",
                        "ByCustomerParameter",
                        (query, order) =>
                            query.Parameters.Add(
                                new QueryParameter
                                {
                                    Name = "customerId",
                                    SourceColumnId = ColumnIdOf(order, "CustomerId"),
                                }
                            )
                    );
                    SeedQueryOn(
                        session,
                        "Order",
                        "ByCustomerSort",
                        (query, order) =>
                            query.OrderBy.Add(
                                new QueryOrdering
                                {
                                    ColumnId = ColumnIdOf(order, "CustomerId"),
                                    Descending = true,
                                }
                            )
                    );
                    SeedQueryOn(
                        session,
                        "Order",
                        "CustomerProjection",
                        (query, order) =>
                        {
                            query.Returns = QueryReturnShape.Projection;
                            query.ResultTypeName = "CustomerRow";
                            query.Fields.Add(
                                new ProjectionField
                                {
                                    Name = "Customer",
                                    SourceColumnId = ColumnIdOf(order, "CustomerId"),
                                }
                            );
                        }
                    );
                    SeedQueryOn(
                        session,
                        "Order",
                        "ByCustomerCondition",
                        (query, _) => query.Condition = "CustomerId = 1"
                    );
                }
            )
            .Seed(
                "参照しないクエリ（Order の OrderId だけ・Customer の同じ名前の列 CustomerId）",
                session =>
                {
                    SeedQueryOn(
                        session,
                        "Order",
                        "ByOrderId",
                        (query, _) => query.Condition = "OrderId = 1"
                    );
                    SeedQueryOn(
                        session,
                        "Customer",
                        "CustomerById",
                        (query, _) => query.Condition = "CustomerId = 1"
                    );
                }
            )
            .Call(
                "remove_column",
                new { table_name = "Order", column_name = "CustomerId" },
                e =>
                    e.Ok()
                        .Result.Contains(
                            "ByCustomerParameter (parameter 'customerId')",
                            "ByCustomerSort (sort order)",
                            "CustomerProjection (field 'Customer')",
                            "ByCustomerCondition (condition)",
                            "set_query",
                            "remove_query"
                        )
                        .Result.Absent("ByOrderId", "CustomerById")
                        .Schema.Absent("col Order.CustomerId *")
                        .Schema.Contains(
                            "qdef Order.ByCustomerParameter * params=(customerId:-@?) *",
                            "qdef Order.ByCustomerSort * order=(@? desc) *",
                            "qdef Order.CustomerProjection * fields=(Customer:-@?) *",
                            "qdef Order.ByCustomerCondition * cond=\"CustomerId = 1\" *"
                        )
            );

        yield return Row(
                "column-remove-condition-reference-is-case-insensitive",
                "remove_column の名指しは条件の列名を大文字小文字を無視して読み、パラメータ名・文字列リテラルの同じ綴りと、生 SQL・手動実装のクエリに残った条件は数えない"
            )
            .Base(CustomerOrder)
            .Seed(
                "生 SQL・手動実装のクエリが、使わない条件に列名を残している（条件は簡易 DSL のときだけ使うので、切れても検証も生成も止まらない）",
                session =>
                {
                    SeedQueryOn(
                        session,
                        "Order",
                        "RawSqlWithLeftoverCondition",
                        (query, _) =>
                        {
                            query.Implementation = QueryImplementationKind.Sql;
                            query.Condition = "CustomerId = 1";
                            query.Sql["sqlserver"] = "SELECT * FROM [Order] WHERE OrderId = 1";
                        }
                    );
                    SeedQueryOn(
                        session,
                        "Order",
                        "ManualWithLeftoverCondition",
                        (query, _) =>
                        {
                            query.Implementation = QueryImplementationKind.Manual;
                            query.Condition = "CustomerId = 1";
                        }
                    );
                }
            )
            .Seed(
                "条件の列名だけが大小違いのクエリ・パラメータ名と文字列リテラルにだけ同じ綴りがあるクエリ・3 種で参照するクエリ",
                session =>
                {
                    SeedQueryOn(
                        session,
                        "Order",
                        "LowerCaseCondition",
                        (query, _) => query.Condition = "customerid = 1"
                    );
                    SeedQueryOn(
                        session,
                        "Order",
                        "SameSpellingElsewhere",
                        (query, _) =>
                        {
                            query.Condition =
                                "OrderId = @CustomerId AND ShipToCustomerId <> 'CustomerId'";
                            query.Parameters.Add(
                                new QueryParameter { Name = "CustomerId", Type = "int32" }
                            );
                        }
                    );
                    SeedQueryOn(
                        session,
                        "Order",
                        "Mixed",
                        (query, order) =>
                        {
                            query.Condition = "CUSTOMERID = @c";
                            query.Parameters.Add(
                                new QueryParameter
                                {
                                    Name = "c",
                                    SourceColumnId = ColumnIdOf(order, "CustomerId"),
                                }
                            );
                            query.OrderBy.Add(
                                new QueryOrdering { ColumnId = ColumnIdOf(order, "CustomerId") }
                            );
                        }
                    );
                }
            )
            .Call(
                "remove_column",
                new { table_name = "Order", column_name = "CustomerId" },
                e =>
                    e.Ok()
                        .Result.Contains(
                            "LowerCaseCondition (condition)",
                            "Mixed (parameter 'c', sort order, condition)"
                        )
                        .Result.Absent(
                            "SameSpellingElsewhere",
                            "RawSqlWithLeftoverCondition",
                            "ManualWithLeftoverCondition"
                        )
            );

        yield return Row(
                "column-remove-not-referenced-keeps-previous-message",
                "remove_column は削除した列を参照するクエリが無ければ従来の応答のままで、前から切れているクエリも名指ししない"
            )
            .Base(CustomerOrder)
            .Seed(
                "前から切れているクエリ（どこも指さない並び順・無い列名の条件）と、ほかの列だけを参照するクエリ",
                session =>
                {
                    SeedQueryOn(
                        session,
                        "Order",
                        "Stale",
                        (query, _) =>
                        {
                            query.Condition = "Missing = 1";
                            query.OrderBy.Add(new QueryOrdering { ColumnId = Guid.NewGuid() });
                        }
                    );
                    SeedQueryOn(
                        session,
                        "Order",
                        "ByCustomerCondition",
                        (query, _) => query.Condition = "CustomerId = 1"
                    );
                }
            )
            .Call(
                "remove_column",
                new { table_name = "Order", column_name = "ShipToCustomerId" },
                e =>
                    e.Ok()
                        .Result.Absent("Stale", "ByCustomerCondition", "set_query")
                        .ForHost(
                            ToolHostKind.File,
                            "GUI の応答は resx（UI の言語）・MCP は英語の固定文",
                            file =>
                                file.Result.Exact(
                                    "Removed column 'ShipToCustomerId' from table 'Order'."
                                )
                        )
            );
    }

    /// <summary>図のテーブルへ名前付きクエリを直接足す（列の参照は表の列を引いて組む）</summary>
    private static void SeedQueryOn(
        ToolHostSession session,
        string tableName,
        string queryName,
        Action<QueryDefinition, Entity> configure
    )
    {
        var entity = session.Schema.Entities.Single(candidate => candidate.TableName == tableName);
        var query = new QueryDefinition { EntityId = entity.Id, Name = queryName };
        configure(query, entity);
        session.AddQuery(query);
    }

    /// <summary>表の列の Id</summary>
    private static Guid ColumnIdOf(Entity entity, string columnName) =>
        entity.Columns.Single(column => column.Name == columnName).Id;
}
