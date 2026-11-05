namespace QuickER.Tests.Mcp.Tools;

internal static partial class ToolScenarioRows
{
    /// <summary>add_entity（列を自動生成しない）と set_entity_property（指定したプロパティだけ更新・変更対象なしは拒否）</summary>
    private static IEnumerable<ToolRow> EntityRows()
    {
        yield return Row(
                "entity-add-creates-table-without-columns-sqlserver",
                "add_entity でテーブルが追加され列は自動生成されない"
            )
            .Call(
                "add_entity",
                new { table_name = "Book" },
                e => e.Ok().Schema.Kind("table", "table Book *").Schema.Kind("col")
            );

        yield return Row(
                "entity-add-creates-table-without-columns-sqlite",
                "add_entity はテーブルを追加し列は自動生成しない"
            )
            .Dialect("sqlite")
            .Call(
                "add_entity",
                new { table_name = "Book", description = "books" },
                e =>
                    e.Ok()
                        .Result.Contains("Book")
                        .Schema.Kind("table", "table Book desc=\"books\" *")
                        .Schema.Kind("col")
            );

        yield return Row(
                "entity-set-property-updates-specified-only-sqlite",
                "set_entity_property は指定プロパティのみ更新する"
            )
            .Dialect("sqlite")
            .Arrange("add_entity", new { table_name = "Book" })
            .Call(
                "set_entity_property",
                new
                {
                    table_name = "Book",
                    new_table_name = "Books",
                    description = "renamed",
                },
                e => e.Ok().Schema.Kind("table", "table Books desc=\"renamed\" memo=\"\"")
            );

        yield return Row(
                "entity-set-property-no-change-is-rejected-sqlite",
                "set_entity_property は変更対象未指定をエラーにする"
            )
            .Dialect("sqlite")
            .Arrange("add_entity", new { table_name = "Book" })
            .Call("set_entity_property", new { table_name = "Book" }, e => e.Fails());

        yield return Row(
                "entity-remove-missing-table-is-rejected",
                "remove_entity は存在しないテーブルを断り、図を変えない"
            )
            .Base(CustomerOrder)
            .Call("remove_entity", new { table_name = "Invoice" }, e => e.Fails().Unchanged());
    }
}
