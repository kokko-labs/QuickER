using System.Text.Json;
using AwesomeAssertions;
using QuickER.Mcp.Tools;
using QuickER.Model;
using QuickER.Tests.TestSupport;

namespace QuickER.Tests.Mcp.Tools;

/// <summary>
/// set_query / remove_query の入力の扱い（使われない入力は警告で知らせ断らない・使われる入力の食い違いは断る・
/// どのテーブルにも属さないクエリを残さない・応答の改行をそろえる）を確かめる
/// </summary>
/// <remarks>共有コア <see cref="QueryToolCore"/> を直接呼ぶ（MCP と内蔵チャットの両方がこのコアを通る）</remarks>
public sealed class QueryToolCoreInputRuleTests
{
    /// <summary>既知の失敗（32-4-P1 の現状）。直したら外し、区切りの最後に空にする</summary>
    private static readonly KnownFailureLedger Ledger = new(new Dictionary<string, string> { });

    /// <summary>orders（order_id PK・customer_id・amount）だけを持つ図</summary>
    private static ErDiagram BuildOrders()
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
        orders.Columns.Add(new Column { Name = "amount", DataType = "decimal(10,2)" });

        var diagram = new ErDiagram { TargetDbms = "sqlite" };
        diagram.Entities.Add(orders);

        return diagram;
    }

    /// <summary><see cref="BuildOrders"/> に、どのテーブルにも属さない射影のクエリ（結果型名 OrderRow）を足した図</summary>
    private static ErDiagram BuildOrdersWithOrphanQuery()
    {
        var diagram = BuildOrders();
        diagram.Queries.Add(
            new QueryDefinition
            {
                EntityId = Guid.NewGuid(),
                Name = "Leftover",
                Returns = QueryReturnShape.Projection,
                ResultTypeName = "OrderRow",
            }
        );

        return diagram;
    }

    private static QueryToolOutcome SetQuery(ErDiagram diagram, object args) =>
        QueryToolCore.SetQuery(diagram, JsonSerializer.SerializeToElement(args));

    [Fact(
        DisplayName = "生 SQL・手動実装で order_by を指定したら、生成で使われないことを警告で知らせる（保存は GUI と同じく行う）"
    )]
    public void OrderBy_WithNonDslImplementation_IsWarned() =>
        Ledger.Run(
            "OrderBy_WithNonDslImplementation_IsWarned",
            OrderBy_WithNonDslImplementation_IsWarnedCore
        );

    private void OrderBy_WithNonDslImplementation_IsWarnedCore()
    {
        var diagram = BuildOrders();

        var outcome = SetQuery(
            diagram,
            new
            {
                table_name = "orders",
                query_name = "Latest",
                returns = "list",
                implementation = "manual",
                order_by = new[] { new { column = "order_id", descending = true } },
            }
        );

        outcome.Success.Should().BeTrue();
        outcome
            .Warnings.Select(warning => warning.Code.ToString())
            .Should()
            .Contain("OrderByIgnoredForImplementation");
    }

    [Fact(
        DisplayName = "生 SQL 以外の実装では、使わない sql の未知の方言キーで保存を断らない（警告にとどめる）"
    )]
    public void UnknownDialectKey_WithDslImplementation_IsOnlyWarned() =>
        Ledger.Run(
            "UnknownDialectKey_WithDslImplementation_IsOnlyWarned",
            UnknownDialectKey_WithDslImplementation_IsOnlyWarnedCore
        );

    private void UnknownDialectKey_WithDslImplementation_IsOnlyWarnedCore()
    {
        var diagram = BuildOrders();

        var outcome = SetQuery(
            diagram,
            new
            {
                table_name = "orders",
                query_name = "All",
                returns = "list",
                implementation = "dsl",
                sql = new { postgres = "SELECT * FROM orders" },
            }
        );

        outcome.Success.Should().BeTrue("使わない入力は警告で知らせ、断らない");
        outcome.Warnings.Should().NotBeEmpty();
    }

    [Fact(
        DisplayName = "生成器が生 SQL の実装を作らない方言（postgresql・mysql・oracle）の SQL は、手動実装になると警告する"
    )]
    public void SqlForDialectWithoutRawSqlImplementation_IsWarned() =>
        Ledger.Run(
            "SqlForDialectWithoutRawSqlImplementation_IsWarned",
            SqlForDialectWithoutRawSqlImplementation_IsWarnedCore
        );

    private void SqlForDialectWithoutRawSqlImplementation_IsWarnedCore()
    {
        var diagram = BuildOrders();

        var outcome = SetQuery(
            diagram,
            new
            {
                table_name = "orders",
                query_name = "All",
                returns = "list",
                implementation = "sql",
                sql = new { postgresql = "SELECT * FROM orders" },
            }
        );

        outcome.Success.Should().BeTrue();
        outcome
            .Warnings.Should()
            .Contain(warning => warning.Dialect == "postgresql", "定義の時点で知らせる");
    }

    [Fact(
        DisplayName = "sql の方言キーが大文字小文字だけ違って 2 つあれば、黙って片方を捨てず断る"
    )]
    public void DialectKeysDifferingOnlyInCase_AreRefused() =>
        Ledger.Run(
            "DialectKeysDifferingOnlyInCase_AreRefused",
            DialectKeysDifferingOnlyInCase_AreRefusedCore
        );

    private void DialectKeysDifferingOnlyInCase_AreRefusedCore()
    {
        var diagram = BuildOrders();
        var args = JsonDocument
            .Parse(
                """
                {
                  "table_name": "orders",
                  "query_name": "All",
                  "returns": "list",
                  "implementation": "sql",
                  "sql": { "sqlite": "SELECT 1", "SQLite": "SELECT 2" }
                }
                """
            )
            .RootElement;

        var outcome = QueryToolCore.SetQuery(diagram, args);

        outcome.Success.Should().BeFalse();
        diagram.Queries.Should().BeEmpty();
    }

    [Fact(
        DisplayName = "条件を書かない簡易 DSL で宣言したパラメータは、条件で使うパラメータと同じく未使用の警告を出す"
    )]
    public void DslParameters_WithoutCondition_AreWarnedAsUnused() =>
        Ledger.Run(
            "DslParameters_WithoutCondition_AreWarnedAsUnused",
            DslParameters_WithoutCondition_AreWarnedAsUnusedCore
        );

    private void DslParameters_WithoutCondition_AreWarnedAsUnusedCore()
    {
        var diagram = BuildOrders();

        var outcome = SetQuery(
            diagram,
            new
            {
                table_name = "orders",
                query_name = "ByCustomer",
                returns = "list",
                implementation = "dsl",
                parameters = new[] { new { name = "customerId", source_column = "customer_id" } },
            }
        );

        outcome.Success.Should().BeTrue();
        outcome
            .Warnings.Should()
            .Contain(warning =>
                warning.Code == QueryToolDiagnosticCode.ParameterUnusedInCondition
                && warning.Name == "customerId"
            );
    }

    [Fact(
        DisplayName = "どのテーブルにも属さないクエリ（生成器が読み飛ばす）の結果型名は、新しい射影の結果型名を塞がない"
    )]
    public void OrphanQuery_DoesNotBlockResultTypeName() =>
        Ledger.Run(
            "OrphanQuery_DoesNotBlockResultTypeName",
            OrphanQuery_DoesNotBlockResultTypeNameCore
        );

    private void OrphanQuery_DoesNotBlockResultTypeNameCore()
    {
        var diagram = BuildOrdersWithOrphanQuery();

        var outcome = SetQuery(
            diagram,
            new
            {
                table_name = "orders",
                query_name = "Rows",
                returns = "projection",
                result_type_name = "OrderRow",
                fields = new[] { new { name = "Amount", source_column = "amount" } },
            }
        );

        outcome.Success.Should().BeTrue(string.Join(", ", outcome.Errors));
    }

    [Fact(
        DisplayName = "どのテーブルにも属さないクエリは、テーブル名が図に無くてもクエリ名で remove_query から消せる"
    )]
    public void OrphanQuery_CanBeRemovedByQueryName() =>
        Ledger.Run("OrphanQuery_CanBeRemovedByQueryName", OrphanQuery_CanBeRemovedByQueryNameCore);

    private void OrphanQuery_CanBeRemovedByQueryNameCore()
    {
        var diagram = BuildOrdersWithOrphanQuery();

        var outcome = QueryToolCore.RemoveQuery(
            diagram,
            JsonSerializer.SerializeToElement(
                new { table_name = "deleted_table", query_name = "Leftover" }
            )
        );

        outcome.Success.Should().BeTrue();
        diagram.Queries.Should().BeEmpty();
    }

    [Fact(
        DisplayName = "set_query・list_queries の英語の応答は、成否や警告の有無に依らず改行を LF にそろえる"
    )]
    public void EnglishFormatter_UsesLfOnly() =>
        Ledger.Run("EnglishFormatter_UsesLfOnly", EnglishFormatter_UsesLfOnlyCore);

    private void EnglishFormatter_UsesLfOnlyCore()
    {
        var diagram = BuildOrders();
        var withWarning = SetQuery(
            diagram,
            new
            {
                table_name = "orders",
                query_name = "ByCustomer",
                returns = "list",
                parameters = new[] { new { name = "customerId", source_column = "customer_id" } },
                condition = "customer_id = 1",
            }
        );
        var failed = SetQuery(
            diagram,
            new
            {
                table_name = "orders",
                query_name = "Broken",
                returns = "list",
                condition = "no_such_column = 1 AND",
            }
        );

        withWarning.Warnings.Should().NotBeEmpty("警告つきの成功を作れている");
        failed.Success.Should().BeFalse("失敗を作れている");

        var texts = new[]
        {
            QueryToolEnglishFormatter.FormatSetQuery(withWarning),
            QueryToolEnglishFormatter.FormatSetQuery(failed),
            QueryToolEnglishFormatter.FormatListing(QueryToolCore.ListQueries(diagram).Listing!),
        };

        texts
            .Should()
            .OnlyContain(text => !text.Contains('\r'), "1 つの応答の中で改行の書き方を混ぜない");
    }
}
