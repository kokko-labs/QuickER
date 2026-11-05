using QuickER.Model;
using QuickER.Tests.TestSupport;

namespace QuickER.Tests.Mcp.Tools;

internal static partial class ToolScenarioRows
{
    /// <summary>図のテーブルへ名前付きクエリを直接足す（クエリはツールの対象外の経路で作られる）</summary>
    private static Action<ToolHostSession> SeedQuery(string tableName, string queryName) =>
        session =>
            session.AddQuery(
                new QueryDefinition
                {
                    EntityId = session
                        .Schema.Entities.Single(entity => entity.TableName == tableName)
                        .Id,
                    Name = queryName,
                    Returns = QueryReturnShape.List,
                }
            );

    /// <summary>
    /// remove_entity がそのテーブルの名前付きクエリも取り除くこと（クエリはテーブル単位で定義されるので、残すと
    /// テーブル名で照合する remove_query でもクエリ定義ダイアログでも触れない孤児になる）
    /// </summary>
    private static IEnumerable<ToolRow> RemoveEntityRows()
    {
        yield return Row(
                "remove-entity-removes-queries-of-that-table",
                "remove_entity はそのテーブルの名前付きクエリも取り除き、応答で名指しする"
            )
            .Base(CustomerOrder)
            .Seed("Order のクエリ FindByCustomer", SeedQuery("Order", "FindByCustomer"))
            .Seed("Customer のクエリ FindByName", SeedQuery("Customer", "FindByName"))
            .Seed("Order のクエリ CountAll", SeedQuery("Order", "CountAll"))
            .Call(
                "remove_entity",
                new { table_name = "Order" },
                e =>
                    e.Ok()
                        .Result.Contains("FindByCustomer", "CountAll")
                        .Schema.Kind("query", "query Customer.FindByName List")
            );

        yield return Row(
                "remove-entity-without-queries-keeps-other-tables-queries",
                "remove_entity は他のテーブルのクエリを残し、クエリが無ければ従来の応答のまま"
            )
            .Base(CustomerOrder)
            .Seed("Customer のクエリ FindByName", SeedQuery("Customer", "FindByName"))
            .Call(
                "remove_entity",
                new { table_name = "Order" },
                e =>
                    e.Ok()
                        .Result.Absent("FindByName")
                        .Schema.Kind("query", "query Customer.FindByName List")
            );

        yield return Row(
                "remove-entity-without-any-queries-keeps-previous-message",
                "remove_entity（MCP）はクエリが無いとき従来と同じ文言を返す"
            )
            .Base(CustomerOrder)
            .Call(
                "remove_entity",
                new { table_name = "Order" },
                e =>
                    e.Ok()
                        .Schema.Kind("query")
                        .ForHost(
                            ToolHostKind.File,
                            "GUI の応答は resx（UI の言語）・MCP は英語の固定文",
                            file => file.Result.Exact("Removed table 'Order'.")
                        )
            );
    }
}
