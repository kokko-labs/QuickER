namespace QuickER.Tests.Mcp.Tools;

internal static partial class ToolScenarioRows
{
    /// <summary>
    /// 成功しても何も変えない呼び出しは図を書かない（GUI は履歴を積まず未保存にもしない・MCP は図のファイルを書き直さない）。
    /// 断る呼び出しは保存の前で戻るので、「書かない」は成功する呼び出しで見る
    /// </summary>
    private static IEnumerable<ToolRow> NoOpRows()
    {
        yield return Row(
                "no-op-set-entity-property-same-description-does-not-write",
                "set_entity_property で今と同じ説明を指定しても図を書かない"
            )
            .Base(CustomerOrder)
            .Call(
                "set_entity_property",
                new { table_name = "Customer", description = "" },
                e => e.Ok().Unchanged()
            );

        yield return Row(
                "no-op-set-column-property-same-data-type-does-not-write",
                "set_column_property で今と同じデータ型を指定しても図を書かない"
            )
            .Base(CustomerOrder)
            .Call(
                "set_column_property",
                new
                {
                    table_name = "Customer",
                    column_name = "Name",
                    data_type = "nvarchar(50)",
                },
                e => e.Ok().Unchanged()
            );

        yield return Row(
                "no-op-set-column-property-nullable-on-primary-key-does-not-write",
                "set_column_property で主キー列へ is_nullable=true を指定しても（無視して注意を添えるだけで）図を書かない"
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
                e => e.Ok().Unchanged()
            );

        var uniqueName = new
        {
            table_name = "Customer",
            columns = new[] { "Name" },
            name = "UQ_Customer_Name",
        };

        yield return Row(
                "no-op-set-unique-constraint-same-definition-does-not-write",
                "set_unique_constraint で今と同じ定義を指定しても図を書かない"
            )
            .Base(CustomerOrder)
            .Arrange("set_unique_constraint", uniqueName)
            .Call("set_unique_constraint", uniqueName, e => e.Ok().Unchanged());
    }
}
