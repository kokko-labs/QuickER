using System.Text.Json;
using AwesomeAssertions;
using QuickER.AI.Chat;
using QuickER.Mcp.Tools;
using QuickER.Model;
using QuickER.Tests.TestSupport;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.AI.Chat;

/// <summary>内蔵チャットのクエリツールの応答（<see cref="QueryToolLocalizedFormatter"/>）の改行をそろえることを確かめる</summary>
/// <remarks>MCP 側の英語の応答と同じ規則（<c>QueryToolCoreInputRuleTests.EnglishFormatter_UsesLfOnly</c>）</remarks>
public sealed class QueryToolLocalizedFormatterLineEndingTests
{
    /// <summary>既知の失敗（32-4-P1 の現状）。直したら外し、区切りの最後に空にする</summary>
    private static readonly KnownCaseLedger Ledger = new(
        "QueryToolLocalizedFormatterLineEndingTests",
        []
    );

    [Fact(
        DisplayName = "set_query・list_queries のチャットの応答は、成否や警告の有無に依らず改行を LF にそろえる"
    )]
    public void LocalizedFormatter_UsesLfOnly() =>
        Ledger.Run("LocalizedFormatter_UsesLfOnly", LocalizedFormatter_UsesLfOnlyCore);

    private void LocalizedFormatter_UsesLfOnlyCore()
    {
        var orders = new Entity { TableName = "orders" };
        orders.Columns.Add(
            new Column
            {
                Name = "order_id",
                DataType = "int",
                IsPrimaryKey = true,
            }
        );
        orders.Columns.Add(new Column { Name = "customer_id", DataType = "int" });
        var diagram = new ErDiagram { TargetDbms = "sqlite" };
        diagram.Entities.Add(orders);

        var withWarning = QueryToolCore.SetQuery(
            diagram,
            JsonSerializer.SerializeToElement(
                new
                {
                    table_name = "orders",
                    query_name = "ByCustomer",
                    returns = "list",
                    parameters = new[]
                    {
                        new { name = "customerId", source_column = "customer_id" },
                    },
                    condition = "customer_id = 1",
                }
            )
        );
        var failed = QueryToolCore.SetQuery(
            diagram,
            JsonSerializer.SerializeToElement(
                new
                {
                    table_name = "orders",
                    query_name = "Broken",
                    returns = "list",
                    condition = "no_such_column = 1 AND",
                }
            )
        );

        withWarning.Warnings.Should().NotBeEmpty("警告つきの成功を作れている");
        failed.Success.Should().BeFalse("失敗を作れている");

        var texts = new[]
        {
            QueryToolLocalizedFormatter.FormatSetQuery(withWarning),
            QueryToolLocalizedFormatter.FormatSetQuery(failed),
            QueryToolLocalizedFormatter.FormatListing(QueryToolCore.ListQueries(diagram).Listing!),
        };

        texts
            .Should()
            .OnlyContain(text => !text.Contains('\r'), "1 つの応答の中で改行の書き方を混ぜない");
    }
}
