using QuickER.CodeGen.CSharp.Queries;
using QuickER.Mcp.Tools;
using QuickER.Model;

namespace QuickER.Tests.Mcp.Tools;

internal static partial class ToolScenarioRows
{
    /// <summary>Order に名前付きクエリを直接足す（ツールでは作れない名前＝生成の規則に反する GetById を前提にする）</summary>
    private static ToolRow WithOrderQuery(ToolRow row, string name, QueryReturnShape returns) =>
        row.Seed(
            $"Order のクエリ {name}（{returns}）",
            session =>
                session.AddQuery(
                    new QueryDefinition
                    {
                        EntityId = session
                            .Schema.Entities.Single(entity => entity.TableName == "Order")
                            .Id,
                        Name = name,
                        Returns = returns,
                    }
                )
        );

    /// <summary>
    /// Order（OrderId PK・CustomerId）の図で名前付きクエリのツールを流す（GUI はチャットのアダプタが成功のときだけ
    /// 図のクエリを置き換える＝履歴の世代が進む。失敗・読み取りでは書かない）
    /// </summary>
    private static IEnumerable<ToolRow> QueryOrderTableRows()
    {
        yield return Row(
                "query-set-success-writes-queries-sqlserver",
                "set_query 成功は ReplaceQueries に反映される"
            )
            .Base(OrderTable)
            .Call(
                QueryToolCore.SetQueryToolName,
                new
                {
                    table_name = "Order",
                    query_name = "FindByCustomer",
                    returns = "single",
                },
                e => e.Ok().Wrote().Schema.Single("query Order.FindByCustomer *")
            );

        yield return Row(
                "query-set-upsert-preserves-id-sqlserver",
                "set_query の upsert は既存クエリの Id を温存する"
            )
            .Base(OrderTable)
            .Arrange(
                QueryToolCore.SetQueryToolName,
                new
                {
                    table_name = "Order",
                    query_name = "FindByCustomer",
                    returns = "list",
                }
            )
            .Call(
                QueryToolCore.SetQueryToolName,
                new
                {
                    table_name = "Order",
                    query_name = "findbycustomer",
                    returns = "single",
                },
                e =>
                    e.Ok()
                        .Wrote()
                        .QueryIdKeptAcrossRename("Order.FindByCustomer", "Order.findbycustomer")
                        .Schema.Kind("qdef", "qdef Order.findbycustomer Single *")
            );

        yield return Row(
                "query-set-validation-failure-is-not-written-sqlserver",
                "set_query の検証失敗は ReplaceQueries を呼ばない"
            )
            .Base(OrderTable)
            .Call(
                QueryToolCore.SetQueryToolName,
                new
                {
                    table_name = "Order",
                    query_name = "Bad",
                    returns = "scalar",
                },
                e => e.Fails().Unchanged().Schema.Kind("query")
            );

        yield return Row(
                "query-set-generation-rule-violation-is-rejected-with-generator-message-sqlserver",
                "set_query は生成で必ずエラーになる定義を生成時診断と同じ文言で拒否する"
            )
            .Base(OrderTable)
            .Call(
                QueryToolCore.SetQueryToolName,
                new
                {
                    table_name = "Order",
                    query_name = "GetById",
                    returns = "list",
                },
                e =>
                {
                    var text = ReservedGetByIdViolationText();
                    e.Fails()
                        .Unchanged()
                        .ResultContainsByHost(gui: text.Format(null), file: text.FormatEnglish());
                }
            );

        yield return Row(
                "query-set-unknown-table-is-not-written-sqlserver",
                "set_query で存在しないテーブルは失敗する"
            )
            .Base(OrderTable)
            .Call(
                QueryToolCore.SetQueryToolName,
                new
                {
                    table_name = "NoSuchTable",
                    query_name = "X",
                    returns = "list",
                },
                e => e.Fails().Unchanged()
            );

        yield return WithOrderQuery(
                Row(
                        "query-list-returns-summary-without-write-sqlserver",
                        "list_queries はクエリ要約を返し書き戻さない"
                    )
                    .Base(OrderTable),
                "GetById",
                QueryReturnShape.Single
            )
            .Call(
                QueryToolCore.ListQueriesToolName,
                new { },
                e => e.Ok().Unchanged().Result.Contains("Order", "GetById")
            );

        yield return WithOrderQuery(
                Row(
                        "query-remove-success-writes-queries-sqlserver",
                        "remove_query 成功は ReplaceQueries に反映される"
                    )
                    .Base(OrderTable),
                "GetById",
                QueryReturnShape.Single
            )
            .Call(
                QueryToolCore.RemoveQueryToolName,
                new { table_name = "Order", query_name = "GetById" },
                e => e.Ok().Wrote().Schema.Kind("query")
            );

        yield return Row(
                "query-remove-missing-is-not-written-sqlserver",
                "remove_query で不在クエリは失敗し書き戻さない"
            )
            .Base(OrderTable)
            .Call(
                QueryToolCore.RemoveQueryToolName,
                new { table_name = "Order", query_name = "Missing" },
                e => e.Fails().Unchanged()
            );
    }

    /// <summary>
    /// Order（OrderId PK・CustomerId）の一覧の GetById（標準メンバーとぶつかる名前）に対する生成の規則の違反の文
    /// （生成時診断と同じ文。GUI は UI の言語・MCP は英語で描く）
    /// </summary>
    private static QueryDiagnosticText ReservedGetByIdViolationText()
    {
        var order = new Entity { TableName = "Order" };
        order.Columns.Add(
            new Column
            {
                Name = "OrderId",
                DataType = "int",
                IsPrimaryKey = true,
            }
        );
        order.Columns.Add(new Column { Name = "CustomerId", DataType = "int" });

        return QueryDefinitionRules
            .Validate(
                order,
                new QueryDefinition
                {
                    EntityId = order.Id,
                    Name = "GetById",
                    Returns = QueryReturnShape.List,
                },
                []
            )
            .Single()
            .Text;
    }
}
