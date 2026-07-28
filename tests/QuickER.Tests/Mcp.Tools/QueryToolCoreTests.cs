using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using FluentAssertions;
using QuickER.Mcp.Tools;
using QuickER.Model;

namespace QuickER.Tests.Mcp.Tools;

/// <summary>
/// 面非依存のクエリツール実行コア <see cref="QueryToolCore"/> の構造化結果を検証する。
/// </summary>
/// <remarks>
/// MCP 面の文字列整形（<see cref="QueryToolEnglishFormatter"/>）は
/// <see cref="DocumentErDiagramQueryToolsTests"/> がファイル経由で文言までカバーするため、ここでは
/// コアが返す <see cref="QueryToolOutcome"/> の形（成否・状態・診断種別・警告・一覧データ）と、
/// 失敗時に図（<see cref="ErDiagram.Queries"/>）を変更しないことに集中する。
/// </remarks>
public sealed class QueryToolCoreTests
{
    /// <summary>orders（order_id PK・customer_id・amount・memo）だけを持つ図を組み立てる</summary>
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
        orders.Columns.Add(new Column { Name = "memo", DataType = "nvarchar(50)" });

        var diagram = new ErDiagram { TargetDbms = "sqlite" };
        diagram.Entities.Add(orders);

        return diagram;
    }

    private static JsonElement Args(object value) => JsonSerializer.SerializeToElement(value);

    private static QueryToolOutcome SetQuery(ErDiagram diagram, object args) =>
        QueryToolCore.SetQuery(diagram, Args(args));

    // ---------------- 成功系 ----------------

    [Fact(DisplayName = "SetQuery: DSL 一覧の成功結果データを返し図へ追加する")]
    public void SetQuery_ListDsl_Success_ReturnsData()
    {
        var diagram = BuildOrders();

        var outcome = SetQuery(
            diagram,
            new
            {
                table_name = "orders",
                query_name = "GetByCustomer",
                returns = "list",
                condition = "customer_id = @customerId",
                parameters = new[] { new { name = "customerId", type = "int32" } },
            }
        );

        outcome.Success.Should().BeTrue();
        outcome.Status.Should().Be(QueryToolStatus.Success);
        outcome.WasUpdate.Should().BeFalse();
        outcome.QueryName.Should().Be("GetByCustomer");
        outcome.TableName.Should().Be("orders");
        outcome.Returns.Should().Be(QueryReturnShape.List);
        outcome.Implementation.Should().Be(QueryImplementationKind.Dsl);
        outcome.Errors.Should().BeEmpty();
        outcome.Warnings.Should().BeEmpty();

        var query = diagram.Queries.Single();
        query.Name.Should().Be("GetByCustomer");
        query.Condition.Should().Be("customer_id = @customerId");
        query.Parameters.Single().Type.Should().Be("int32");
    }

    [Fact(DisplayName = "SetQuery: 同名再定義は WasUpdate=true・Id 温存で丸ごと置換する")]
    public void SetQuery_Upsert_WasUpdatePreservesId()
    {
        var diagram = BuildOrders();

        SetQuery(
            diagram,
            new
            {
                table_name = "orders",
                query_name = "GetByCustomer",
                returns = "list",
                condition = "customer_id = @customerId",
                parameters = new[] { new { name = "customerId", type = "int32" } },
            }
        );
        var originalId = diagram.Queries.Single().Id;

        var outcome = SetQuery(
            diagram,
            new
            {
                table_name = "orders",
                query_name = "getbycustomer",
                returns = "count",
            }
        );

        outcome.Success.Should().BeTrue();
        outcome.WasUpdate.Should().BeTrue();
        var query = diagram.Queries.Single();
        query.Id.Should().Be(originalId);
        query.Returns.Should().Be(QueryReturnShape.Count);
        query.Condition.Should().BeNull();
        query.Parameters.Should().BeEmpty();
    }

    [Fact(DisplayName = "SetQuery: 列参照パラメータは列 ID で保存する（Type は null）")]
    public void SetQuery_ColumnTypedParameter_SavesColumnId()
    {
        var diagram = BuildOrders();

        var outcome = SetQuery(
            diagram,
            new
            {
                table_name = "orders",
                query_name = "GetByCustomer",
                returns = "list",
                condition = "customer_id = @customerId",
                parameters = new[] { new { name = "customerId", source_column = "customer_id" } },
            }
        );

        outcome.Success.Should().BeTrue();
        var customerId = diagram.Entities.Single().Columns.Single(c => c.Name == "customer_id").Id;
        var parameter = diagram.Queries.Single().Parameters.Single();
        parameter.SourceColumnId.Should().Be(customerId);
        parameter.Type.Should().BeNull();
    }

    // ---------------- 警告系（保存継続） ----------------

    [Fact(DisplayName = "SetQuery: DSL の宣言済み未使用パラメータは警告付きで成功する")]
    public void SetQuery_DslUnusedParameter_WarningSuccess()
    {
        var diagram = BuildOrders();

        var outcome = SetQuery(
            diagram,
            new
            {
                table_name = "orders",
                query_name = "GetByCustomer",
                returns = "list",
                condition = "customer_id = @customerId",
                parameters = new[]
                {
                    new { name = "customerId", type = "int32" },
                    new { name = "unused", type = "int32" },
                },
            }
        );

        outcome.Success.Should().BeTrue();
        diagram.Queries.Should().HaveCount(1);
        outcome
            .Warnings.Should()
            .ContainSingle()
            .Which.Code.Should()
            .Be(QueryToolDiagnosticCode.ParameterUnusedInCondition);
        outcome.Warnings.Single().Name.Should().Be("unused");
    }

    [Fact(DisplayName = "SetQuery: 生 SQL の未使用パラメータは方言付き警告で成功する")]
    public void SetQuery_RawSqlUnused_WarningSuccess()
    {
        var diagram = BuildOrders();

        var outcome = SetQuery(
            diagram,
            new
            {
                table_name = "orders",
                query_name = "ListSql",
                returns = "list",
                implementation = "sql",
                sql = new Dictionary<string, string> { ["sqlite"] = "SELECT * FROM orders" },
                parameters = new[] { new { name = "unused", type = "int32" } },
            }
        );

        outcome.Success.Should().BeTrue();
        var warning = outcome.Warnings.Should().ContainSingle().Subject;
        warning.Code.Should().Be(QueryToolDiagnosticCode.RawSqlDiagnostic);
        warning.Dialect.Should().Be("sqlite");
        // 診断は描画前（資源キー＋書式引数）で届く。面ごとにカルチャを明示して文字列化する
        warning.DetailText.Should().NotBeNull();
        warning.DetailText!.FormatEnglish().Should().NotBeNullOrWhiteSpace();
    }

    // ---------------- 早期失敗（単一状態） ----------------

    [Fact(DisplayName = "SetQuery: table_name/query_name 欠落は MissingArgument")]
    public void SetQuery_MissingArgument()
    {
        var diagram = BuildOrders();

        var outcome = SetQuery(diagram, new { returns = "list" });

        outcome.Status.Should().Be(QueryToolStatus.MissingArgument);
        outcome.Success.Should().BeFalse();
        diagram.Queries.Should().BeEmpty();
    }

    [Fact(DisplayName = "SetQuery: 未知テーブルは TableNotFound")]
    public void SetQuery_TableNotFound()
    {
        var diagram = BuildOrders();

        var outcome = SetQuery(
            diagram,
            new
            {
                table_name = "nope",
                query_name = "X",
                returns = "list",
            }
        );

        outcome.Status.Should().Be(QueryToolStatus.TableNotFound);
        outcome.TableName.Should().Be("nope");
        diagram.Queries.Should().BeEmpty();
    }

    [Fact(DisplayName = "SetQuery: returns 未指定は InvalidReturns")]
    public void SetQuery_InvalidReturns()
    {
        var diagram = BuildOrders();

        var outcome = SetQuery(diagram, new { table_name = "orders", query_name = "X" });

        outcome.Status.Should().Be(QueryToolStatus.InvalidReturns);
        diagram.Queries.Should().BeEmpty();
    }

    [Fact(DisplayName = "SetQuery: 不正な implementation は InvalidImplementation")]
    public void SetQuery_InvalidImplementation()
    {
        var diagram = BuildOrders();

        var outcome = SetQuery(
            diagram,
            new
            {
                table_name = "orders",
                query_name = "X",
                returns = "list",
                implementation = "bogus",
            }
        );

        outcome.Status.Should().Be(QueryToolStatus.InvalidImplementation);
        diagram.Queries.Should().BeEmpty();
    }

    // ---------------- 検証失敗（Errors に種別） ----------------

    [Theory(
        DisplayName = "SetQuery: 各検証エラーは ValidationFailed＋対応する診断種別を返し図を変えない"
    )]
    [MemberData(nameof(ValidationCases))]
    public void SetQuery_ValidationFailed_ProducesDiagnosticCode(
        object args,
        QueryToolDiagnosticCode expected
    )
    {
        var diagram = BuildOrders();

        var outcome = SetQuery(diagram, args);

        outcome.Success.Should().BeFalse();
        outcome.Status.Should().Be(QueryToolStatus.ValidationFailed);
        outcome.Errors.Select(error => error.Code).Should().Contain(expected);
        diagram.Queries.Should().BeEmpty("validation failure must not mutate the diagram");
    }

    public static IEnumerable<object[]> ValidationCases()
    {
        yield return
        [
            new
            {
                table_name = "orders",
                query_name = "X",
                returns = "scalar",
                implementation = "sql",
                sql = new Dictionary<string, string> { ["sqlite"] = "SELECT COUNT(*) FROM orders" },
            },
            QueryToolDiagnosticCode.ScalarRequiresScalarType,
        ];
        yield return
        [
            new
            {
                table_name = "orders",
                query_name = "X",
                returns = "projection",
                fields = new[] { new { name = "OrderId", source_column = "order_id" } },
            },
            QueryToolDiagnosticCode.ProjectionRequiresResultTypeName,
        ];
        yield return
        [
            new
            {
                table_name = "orders",
                query_name = "X",
                returns = "projection",
                result_type_name = "Row",
            },
            QueryToolDiagnosticCode.ProjectionRequiresFields,
        ];
        yield return
        [
            new
            {
                table_name = "orders",
                query_name = "X",
                returns = "list",
                parameters = new[]
                {
                    new
                    {
                        name = "p",
                        type = "int32",
                        source_column = "customer_id",
                    },
                },
            },
            QueryToolDiagnosticCode.ParameterTypeSourceExclusive,
        ];
        yield return
        [
            new
            {
                table_name = "orders",
                query_name = "X",
                returns = "list",
                parameters = new[] { new { name = "p", source_column = "no_such_col" } },
            },
            QueryToolDiagnosticCode.ParameterSourceColumnNotFound,
        ];
        yield return
        [
            new
            {
                table_name = "orders",
                query_name = "X",
                returns = "count",
                order_by = new[] { new { column = "order_id" } },
            },
            QueryToolDiagnosticCode.OrderByInvalidForReturnShape,
        ];
        yield return
        [
            new
            {
                table_name = "orders",
                query_name = "X",
                returns = "list",
                order_by = new[] { new { column = "no_such_col" } },
            },
            QueryToolDiagnosticCode.OrderByColumnNotFound,
        ];
        yield return
        [
            new
            {
                table_name = "orders",
                query_name = "X",
                returns = "list",
                implementation = "sql",
                sql = new Dictionary<string, string> { ["db2"] = "SELECT * FROM orders" },
            },
            QueryToolDiagnosticCode.UnknownSqlDialect,
        ];
        yield return
        [
            new
            {
                table_name = "orders",
                query_name = "X",
                returns = "list",
                condition = "customer_id = = @p",
            },
            QueryToolDiagnosticCode.ConditionDiagnostic,
        ];
        yield return
        [
            new
            {
                table_name = "orders",
                query_name = "X",
                returns = "list",
                implementation = "sql",
                sql = new Dictionary<string, string>
                {
                    ["sqlite"] = "SELECT * FROM orders WHERE customer_id = @missing",
                },
            },
            QueryToolDiagnosticCode.RawSqlDiagnostic,
        ];
    }

    [Fact(
        DisplayName = "SetQuery: 生 SQL 未宣言パラメータは RawSqlDiagnostic エラーで図を変えない"
    )]
    public void SetQuery_RawSqlUndeclared_IsError()
    {
        var diagram = BuildOrders();

        var outcome = SetQuery(
            diagram,
            new
            {
                table_name = "orders",
                query_name = "X",
                returns = "list",
                implementation = "sql",
                sql = new Dictionary<string, string>
                {
                    ["sqlite"] = "SELECT * FROM orders WHERE customer_id = @missing",
                },
            }
        );

        outcome.Status.Should().Be(QueryToolStatus.ValidationFailed);
        outcome
            .Errors.Should()
            .ContainSingle()
            .Which.Code.Should()
            .Be(QueryToolDiagnosticCode.RawSqlDiagnostic);
        diagram.Queries.Should().BeEmpty();
    }

    // ---------------- remove_query ----------------

    [Fact(DisplayName = "RemoveQuery: 1 件削除で成功する")]
    public void RemoveQuery_Removes()
    {
        var diagram = BuildOrders();
        SetQuery(
            diagram,
            new
            {
                table_name = "orders",
                query_name = "FindTop",
                returns = "single",
            }
        );

        var outcome = QueryToolCore.RemoveQuery(
            diagram,
            Args(new { table_name = "orders", query_name = "FindTop" })
        );

        outcome.Success.Should().BeTrue();
        outcome.QueryName.Should().Be("FindTop");
        diagram.Queries.Should().BeEmpty();
    }

    [Fact(DisplayName = "RemoveQuery: 不在は QueryNotFound で図を変えない")]
    public void RemoveQuery_NotFound()
    {
        var diagram = BuildOrders();
        SetQuery(
            diagram,
            new
            {
                table_name = "orders",
                query_name = "FindTop",
                returns = "single",
            }
        );

        var outcome = QueryToolCore.RemoveQuery(
            diagram,
            Args(new { table_name = "orders", query_name = "NoSuch" })
        );

        outcome.Status.Should().Be(QueryToolStatus.QueryNotFound);
        diagram.Queries.Should().HaveCount(1);
    }

    [Fact(DisplayName = "RemoveQuery: 未知テーブルは TableNotFound")]
    public void RemoveQuery_TableNotFound()
    {
        var diagram = BuildOrders();

        var outcome = QueryToolCore.RemoveQuery(
            diagram,
            Args(new { table_name = "nope", query_name = "X" })
        );

        outcome.Status.Should().Be(QueryToolStatus.TableNotFound);
    }

    // ---------------- list_queries ----------------

    [Fact(DisplayName = "ListQueries: テーブル別グループ＋列参照名解決の一覧データを返す")]
    public void ListQueries_ReturnsStructuredListing()
    {
        var diagram = BuildOrders();
        SetQuery(
            diagram,
            new
            {
                table_name = "orders",
                query_name = "GetByCustomer",
                returns = "list",
                condition = "customer_id = @customerId",
                parameters = new[] { new { name = "customerId", source_column = "customer_id" } },
                order_by = new[] { new { column = "order_id", descending = true } },
            }
        );

        var outcome = QueryToolCore.ListQueries(diagram);

        outcome.Success.Should().BeTrue();
        var listing = outcome.Listing!;
        listing.TotalCount.Should().Be(1);
        var group = listing.Groups.Should().ContainSingle().Subject;
        group.TableName.Should().Be("orders");

        var item = group.Queries.Should().ContainSingle().Subject;
        item.Name.Should().Be("GetByCustomer");
        item.Returns.Should().Be(QueryReturnShape.List);

        var parameter = item.Parameters.Should().ContainSingle().Subject;
        parameter.IsColumnReference.Should().BeTrue();
        parameter.ColumnName.Should().Be("customer_id");

        var ordering = item.OrderBy.Should().ContainSingle().Subject;
        ordering.ColumnName.Should().Be("order_id");
        ordering.Descending.Should().BeTrue();
    }

    [Fact(DisplayName = "ListQueries: クエリなしは空グループ・総数 0 を返す")]
    public void ListQueries_Empty()
    {
        var diagram = BuildOrders();

        var outcome = QueryToolCore.ListQueries(diagram);

        outcome.Success.Should().BeTrue();
        outcome.Listing!.TotalCount.Should().Be(0);
        outcome.Listing!.Groups.Should().BeEmpty();
    }

    [Fact(
        DisplayName = "ListQueries: 参照先が消えたクエリは不明エンティティグループ（TableName=null）に入る"
    )]
    public void ListQueries_Orphan_GroupedUnderNull()
    {
        var diagram = BuildOrders();
        diagram.Queries.Add(
            new QueryDefinition
            {
                EntityId = Guid.NewGuid(),
                Name = "Dangling",
                Returns = QueryReturnShape.List,
            }
        );

        var outcome = QueryToolCore.ListQueries(diagram);

        var group = outcome.Listing!.Groups.Should().ContainSingle().Subject;
        group.TableName.Should().BeNull();
        group.Queries.Single().Name.Should().Be("Dangling");
    }
}
