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
    }
}
