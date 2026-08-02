using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using AwesomeAssertions;
using QuickER.Documents;
using QuickER.Mcp.Tools;
using QuickER.Model;

namespace QuickER.Tests.Mcp.Tools;

/// <summary>
/// 名前付きクエリ定義ツール（<c>set_query</c> / <c>list_queries</c> / <c>remove_query</c>）の
/// 正常系・検証エラー系を、一時ファイルベースで検証する。
/// </summary>
public sealed class DocumentErDiagramQueryToolsTests : IDisposable
{
    private const string Create = DocumentErDiagramToolHost.CreateDiagramToolName;
    private const string SetQuery = DocumentErDiagramToolHost.SetQueryToolName;
    private const string ListQueries = DocumentErDiagramToolHost.ListQueriesToolName;
    private const string RemoveQuery = DocumentErDiagramToolHost.RemoveQueryToolName;

    private readonly string _dir = Path.Combine(
        Path.GetTempPath(),
        "quicker-mcp-query-" + Guid.NewGuid().ToString("N")
    );

    public DocumentErDiagramQueryToolsTests()
    {
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
            // クリーンアップ失敗はテスト結果に影響させない
        }
    }

    private string PathFor(string name) => Path.Combine(_dir, name);

    private static (string Result, bool Success) Exec(string tool, string file, object args) =>
        DocumentErDiagramToolHost.Execute(tool, file, JsonSerializer.SerializeToElement(args));

    private static List<QueryDefinition> QueriesOf(string file) =>
        JsonStorageService.Load(file).Schema.Queries;

    /// <summary>orders（order_id PK・customer_id・amount・memo）の図ファイルを用意する</summary>
    private string CreateOrdersFile()
    {
        var file = PathFor("diagram.json");
        Exec(Create, file, new { target_dbms = "sqlite" }).Success.Should().BeTrue();
        Exec("add_entity", file, new { table_name = "orders" });
        AddColumn(file, "order_id", "int", isPrimaryKey: true);
        AddColumn(file, "customer_id", "int");
        AddColumn(file, "amount", "decimal(10,2)");
        AddColumn(file, "memo", "nvarchar(50)");

        return file;
    }

    private static void AddColumn(
        string file,
        string name,
        string dataType,
        bool isPrimaryKey = false
    ) =>
        Exec(
            "add_column",
            file,
            new
            {
                table_name = "orders",
                column_name = name,
                data_type = dataType,
                is_primary_key = isPrimaryKey,
                is_nullable = !isPrimaryKey,
            }
        );

    // ---------------- 正常系: 全戻り形 × 全方式 ----------------

    [Fact(DisplayName = "set_query: DSL 一覧（条件・パラメータ・並び順・ページング）を保存する")]
    public void SetQuery_ListDsl_Saves()
    {
        var file = CreateOrdersFile();

        var (_, success) = Exec(
            SetQuery,
            file,
            new
            {
                table_name = "orders",
                query_name = "GetByCustomer",
                description = "orders by customer, newest first",
                returns = "list",
                condition = "customer_id = @customerId",
                parameters = new[] { new { name = "customerId", type = "int32" } },
                order_by = new[] { new { column = "order_id", descending = true } },
                paging = true,
            }
        );

        success.Should().BeTrue();
        var query = QueriesOf(file).Single();
        query.Name.Should().Be("GetByCustomer");
        query.Returns.Should().Be(QueryReturnShape.List);
        query.Implementation.Should().Be(QueryImplementationKind.Dsl);
        query.Condition.Should().Be("customer_id = @customerId");
        query.Parameters.Single().Type.Should().Be("int32");
        query.OrderBy.Single().Descending.Should().BeTrue();
        query.HasPaging.Should().BeTrue();
    }

    [Fact(DisplayName = "set_query: DSL 単一（条件なし）を保存する")]
    public void SetQuery_SingleDsl_Saves()
    {
        var file = CreateOrdersFile();

        var (_, success) = Exec(
            SetQuery,
            file,
            new
            {
                table_name = "orders",
                query_name = "FindTop",
                returns = "single",
            }
        );

        success.Should().BeTrue();
        var query = QueriesOf(file).Single();
        query.Returns.Should().Be(QueryReturnShape.Single);
        query.Condition.Should().BeNull();
    }

    [Fact(DisplayName = "set_query: DSL 件数を保存する")]
    public void SetQuery_CountDsl_Saves()
    {
        var file = CreateOrdersFile();

        var (_, success) = Exec(
            SetQuery,
            file,
            new
            {
                table_name = "orders",
                query_name = "CountByCustomer",
                returns = "count",
                condition = "customer_id = @customerId",
                parameters = new[] { new { name = "customerId", type = "int32" } },
            }
        );

        success.Should().BeTrue();
        QueriesOf(file).Single().Returns.Should().Be(QueryReturnShape.Count);
    }

    [Fact(DisplayName = "set_query: DSL 射影（列参照フィールド）を保存する")]
    public void SetQuery_ProjectionDsl_Saves()
    {
        var file = CreateOrdersFile();

        var (_, success) = Exec(
            SetQuery,
            file,
            new
            {
                table_name = "orders",
                query_name = "GetSummaries",
                returns = "projection",
                result_type_name = "OrderSummaryRow",
                condition = "customer_id = @customerId",
                parameters = new[] { new { name = "customerId", type = "int32" } },
                fields = new[]
                {
                    new { name = "CustomerId", source_column = "customer_id" },
                    new { name = "Amount", source_column = "amount" },
                },
            }
        );

        success.Should().BeTrue();
        var orders = JsonStorageService.Load(file).Schema.Entities.Single();
        var q = QueriesOf(file).Single();
        q.Returns.Should().Be(QueryReturnShape.Projection);
        q.ResultTypeName.Should().Be("OrderSummaryRow");
        q.Fields.Should().HaveCount(2);
        // 列参照フィールドは列 ID 参照で保存され、型トークンは持たない
        q.Fields.All(f => f.SourceColumnId is not null && f.Type is null).Should().BeTrue();
        q.Fields.Select(f => f.SourceColumnId)
            .Should()
            .BeEquivalentTo(
                new[]
                {
                    orders.Columns.Single(c => c.Name == "customer_id").Id,
                    orders.Columns.Single(c => c.Name == "amount").Id,
                }
            );
    }

    [Fact(DisplayName = "set_query: 生 SQL スカラーを保存する")]
    public void SetQuery_ScalarSql_Saves()
    {
        var file = CreateOrdersFile();

        var (_, success) = Exec(
            SetQuery,
            file,
            new
            {
                table_name = "orders",
                query_name = "SumAmounts",
                returns = "scalar",
                scalar_type = "decimal(10,2)",
                implementation = "sql",
                sql = new Dictionary<string, string>
                {
                    ["sqlite"] = "SELECT SUM(amount) FROM orders WHERE customer_id = @customerId",
                },
                parameters = new[] { new { name = "customerId", type = "int32" } },
            }
        );

        success.Should().BeTrue();
        var query = QueriesOf(file).Single();
        query.Returns.Should().Be(QueryReturnShape.Scalar);
        query.ScalarType.Should().Be("decimal(10,2)");
        query.Implementation.Should().Be(QueryImplementationKind.Sql);
        query.Sql.Should().ContainKey("sqlite");
    }

    [Fact(DisplayName = "set_query: 生 SQL 一覧を保存する")]
    public void SetQuery_ListSql_Saves()
    {
        var file = CreateOrdersFile();

        var (_, success) = Exec(
            SetQuery,
            file,
            new
            {
                table_name = "orders",
                query_name = "ListSql",
                returns = "list",
                implementation = "sql",
                sql = new Dictionary<string, string> { ["sqlite"] = "SELECT * FROM orders" },
            }
        );

        success.Should().BeTrue();
        QueriesOf(file).Single().Implementation.Should().Be(QueryImplementationKind.Sql);
    }

    [Fact(DisplayName = "set_query: 生 SQL 射影（型トークンフィールド）を保存する")]
    public void SetQuery_ProjectionSql_Saves()
    {
        var file = CreateOrdersFile();

        var (_, success) = Exec(
            SetQuery,
            file,
            new
            {
                table_name = "orders",
                query_name = "MemoRows",
                returns = "projection",
                result_type_name = "OrderMemoRow",
                implementation = "sql",
                sql = new Dictionary<string, string>
                {
                    ["sqlite"] = "SELECT order_id AS OrderId, memo AS Memo FROM orders",
                },
                fields = new[]
                {
                    new { name = "OrderId", type = "int32" },
                    new { name = "Memo", type = "string(50)" },
                },
            }
        );

        success.Should().BeTrue();
        var query = QueriesOf(file).Single();
        query.Fields.Should().HaveCount(2);
        query.Fields.All(f => f.Type is not null && f.SourceColumnId is null).Should().BeTrue();
    }

    [Fact(DisplayName = "set_query: manual（契約のみ）を保存する")]
    public void SetQuery_Manual_Saves()
    {
        var file = CreateOrdersFile();

        var (_, success) = Exec(
            SetQuery,
            file,
            new
            {
                table_name = "orders",
                query_name = "SpecialLookup",
                returns = "single",
                implementation = "manual",
                parameters = new[] { new { name = "customerId", type = "int32" } },
            }
        );

        success.Should().BeTrue();
        QueriesOf(file).Single().Implementation.Should().Be(QueryImplementationKind.Manual);
    }

    [Fact(DisplayName = "set_query: 列参照型付けパラメータは列 ID で保存する")]
    public void SetQuery_ColumnTypedParameter_Saves()
    {
        var file = CreateOrdersFile();

        var (_, success) = Exec(
            SetQuery,
            file,
            new
            {
                table_name = "orders",
                query_name = "GetByCustomerTyped",
                returns = "list",
                condition = "customer_id = @customerId",
                parameters = new[] { new { name = "customerId", source_column = "customer_id" } },
            }
        );

        success.Should().BeTrue();
        var orders = JsonStorageService.Load(file).Schema.Entities.Single();
        var parameter = QueriesOf(file).Single().Parameters.Single();
        parameter
            .SourceColumnId.Should()
            .Be(orders.Columns.Single(c => c.Name == "customer_id").Id);
        parameter.Type.Should().BeNull();
    }

    [Fact(DisplayName = "set_query: IN 条件（リストパラメータ）を保存する")]
    public void SetQuery_InListParameter_Saves()
    {
        var file = CreateOrdersFile();

        var (_, success) = Exec(
            SetQuery,
            file,
            new
            {
                table_name = "orders",
                query_name = "GetByIds",
                returns = "list",
                condition = "order_id IN @ids",
                parameters = new[]
                {
                    new
                    {
                        name = "ids",
                        type = "int32",
                        is_list = true,
                    },
                },
            }
        );

        success.Should().BeTrue();
        QueriesOf(file).Single().Parameters.Single().IsList.Should().BeTrue();
    }

    // ---------------- upsert ----------------

    [Fact(DisplayName = "set_query: 同名で再定義すると Id を温存して丸ごと置換する")]
    public void SetQuery_Upsert_PreservesIdAndReplaces()
    {
        var file = CreateOrdersFile();

        Exec(
            SetQuery,
            file,
            new
            {
                table_name = "orders",
                query_name = "GetByCustomer",
                returns = "list",
                condition = "customer_id = @customerId",
                parameters = new[] { new { name = "customerId", type = "int32" } },
            }
        );

        var originalId = QueriesOf(file).Single().Id;

        var (result, success) = Exec(
            SetQuery,
            file,
            new
            {
                table_name = "orders",
                query_name = "getbycustomer", // 大文字小文字を区別せず同一とみなす
                returns = "count",
            }
        );

        success.Should().BeTrue();
        result.Should().Contain("Updated");
        var query = QueriesOf(file).Single();
        query.Id.Should().Be(originalId);
        query.Returns.Should().Be(QueryReturnShape.Count);
        // 丸ごと置換なので旧条件・旧パラメータは残らない
        query.Condition.Should().BeNull();
        query.Parameters.Should().BeEmpty();
    }

    // ---------------- remove_query ----------------

    [Fact(DisplayName = "remove_query: 1 件削除する")]
    public void RemoveQuery_Removes()
    {
        var file = CreateOrdersFile();
        Exec(
            SetQuery,
            file,
            new
            {
                table_name = "orders",
                query_name = "FindTop",
                returns = "single",
            }
        );

        var (_, success) = Exec(
            RemoveQuery,
            file,
            new { table_name = "orders", query_name = "FindTop" }
        );

        success.Should().BeTrue();
        QueriesOf(file).Should().BeEmpty();
    }

    [Fact(DisplayName = "remove_query: 不在はエラー")]
    public void RemoveQuery_NotFound_Error()
    {
        var file = CreateOrdersFile();

        var (result, success) = Exec(
            RemoveQuery,
            file,
            new { table_name = "orders", query_name = "NoSuch" }
        );

        success.Should().BeFalse();
        result.Should().Contain("not found");
    }

    // ---------------- list_queries ----------------

    [Fact(DisplayName = "list_queries: エンティティ別に一覧する")]
    public void ListQueries_ListsByEntity()
    {
        var file = CreateOrdersFile();
        Exec(
            SetQuery,
            file,
            new
            {
                table_name = "orders",
                query_name = "CountByCustomer",
                returns = "count",
                condition = "customer_id = @customerId",
                parameters = new[] { new { name = "customerId", type = "int32" } },
            }
        );

        var (result, success) = Exec(ListQueries, file, new { });

        success.Should().BeTrue();
        result.Should().Contain("Queries: 1");
        result.Should().Contain("[orders]");
        result.Should().Contain("CountByCustomer");
        result.Should().Contain("count");
        result.Should().Contain("customerId");
    }

    [Fact(DisplayName = "list_queries: クエリなしでも成功する")]
    public void ListQueries_Empty_Succeeds()
    {
        var file = CreateOrdersFile();

        var (result, success) = Exec(ListQueries, file, new { });

        success.Should().BeTrue();
        result.Should().Contain("Queries: 0");
    }

    // ---------------- 名前解決エラー ----------------

    [Fact(DisplayName = "set_query: 存在しないテーブルはエラー")]
    public void SetQuery_UnknownTable_Error()
    {
        var file = CreateOrdersFile();

        var (result, success) = Exec(
            SetQuery,
            file,
            new
            {
                table_name = "nope",
                query_name = "X",
                returns = "list",
            }
        );

        success.Should().BeFalse();
        result.Should().Contain("not found");
    }

    [Fact(DisplayName = "set_query: パラメータの source_column が所属エンティティに無ければエラー")]
    public void SetQuery_UnknownSourceColumn_Error()
    {
        var file = CreateOrdersFile();

        var (result, success) = Exec(
            SetQuery,
            file,
            new
            {
                table_name = "orders",
                query_name = "X",
                returns = "list",
                parameters = new[] { new { name = "p", source_column = "no_such_col" } },
            }
        );

        success.Should().BeFalse();
        result.Should().Contain("no_such_col");
        QueriesOf(file).Should().BeEmpty();
    }

    [Fact(DisplayName = "set_query: order_by の列が無ければエラー")]
    public void SetQuery_UnknownOrderByColumn_Error()
    {
        var file = CreateOrdersFile();

        var (result, success) = Exec(
            SetQuery,
            file,
            new
            {
                table_name = "orders",
                query_name = "X",
                returns = "list",
                order_by = new[] { new { column = "no_such_col" } },
            }
        );

        success.Should().BeFalse();
        result.Should().Contain("no_such_col");
    }

    // ---------------- 検証エラー ----------------

    [Fact(DisplayName = "set_query: DSL 構文エラーは保存拒否・ファイル不変")]
    public void SetQuery_DslSyntaxError_Rejected_FileUnchanged()
    {
        var file = CreateOrdersFile();
        var before = File.ReadAllText(file);

        var (result, success) = Exec(
            SetQuery,
            file,
            new
            {
                table_name = "orders",
                query_name = "X",
                returns = "list",
                condition = "customer_id = = @p",
            }
        );

        success.Should().BeFalse();
        result.Should().Contain("Condition");
        File.ReadAllText(file).Should().Be(before);
        QueriesOf(file).Should().BeEmpty();
    }

    [Fact(DisplayName = "set_query: DSL の未知列参照は保存拒否")]
    public void SetQuery_DslUnknownColumn_Rejected()
    {
        var file = CreateOrdersFile();

        var (result, success) = Exec(
            SetQuery,
            file,
            new
            {
                table_name = "orders",
                query_name = "X",
                returns = "list",
                condition = "no_such_col = @p",
                parameters = new[] { new { name = "p", type = "int32" } },
            }
        );

        success.Should().BeFalse();
        result.Should().Contain("Condition");
    }

    [Fact(DisplayName = "set_query: DSL の未宣言 @パラメータは保存拒否")]
    public void SetQuery_DslUndeclaredParameter_Rejected()
    {
        var file = CreateOrdersFile();

        var (result, success) = Exec(
            SetQuery,
            file,
            new
            {
                table_name = "orders",
                query_name = "X",
                returns = "list",
                condition = "customer_id = @missing",
            }
        );

        success.Should().BeFalse();
        result.Should().Contain("Condition");
    }

    [Fact(DisplayName = "set_query: 生 SQL の未宣言パラメータは保存拒否")]
    public void SetQuery_RawSqlUndeclared_Rejected()
    {
        var file = CreateOrdersFile();

        var (result, success) = Exec(
            SetQuery,
            file,
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

        success.Should().BeFalse();
        QueriesOf(file).Should().BeEmpty();
    }

    [Fact(DisplayName = "set_query: 生 SQL の未使用パラメータは警告付きで保存継続")]
    public void SetQuery_RawSqlUnused_WarningSuccess()
    {
        var file = CreateOrdersFile();

        var (result, success) = Exec(
            SetQuery,
            file,
            new
            {
                table_name = "orders",
                query_name = "X",
                returns = "list",
                implementation = "sql",
                sql = new Dictionary<string, string> { ["sqlite"] = "SELECT * FROM orders" },
                parameters = new[] { new { name = "unused", type = "int32" } },
            }
        );

        success.Should().BeTrue();
        result.Should().Contain("Warning");
        QueriesOf(file).Should().HaveCount(1);
    }

    [Fact(DisplayName = "set_query: DSL の宣言済み未使用パラメータは警告付きで保存継続")]
    public void SetQuery_DslUnusedParameter_WarningSuccess()
    {
        var file = CreateOrdersFile();

        var (result, success) = Exec(
            SetQuery,
            file,
            new
            {
                table_name = "orders",
                query_name = "X",
                returns = "list",
                condition = "customer_id = @customerId",
                parameters = new[]
                {
                    new { name = "customerId", type = "int32" },
                    new { name = "unused", type = "int32" },
                },
            }
        );

        success.Should().BeTrue();
        result.Should().Contain("Warning");
        result.Should().Contain("unused");
    }

    [Fact(DisplayName = "set_query: scalar で scalar_type 欠落は保存拒否")]
    public void SetQuery_ScalarMissingType_Rejected()
    {
        var file = CreateOrdersFile();

        var (result, success) = Exec(
            SetQuery,
            file,
            new
            {
                table_name = "orders",
                query_name = "X",
                returns = "scalar",
                implementation = "sql",
                sql = new Dictionary<string, string> { ["sqlite"] = "SELECT COUNT(*) FROM orders" },
            }
        );

        success.Should().BeFalse();
        result.Should().Contain("scalar_type");
    }

    [Fact(DisplayName = "set_query: projection で fields 欠落は保存拒否")]
    public void SetQuery_ProjectionMissingFields_Rejected()
    {
        var file = CreateOrdersFile();

        var (result, success) = Exec(
            SetQuery,
            file,
            new
            {
                table_name = "orders",
                query_name = "X",
                returns = "projection",
                result_type_name = "Row",
            }
        );

        success.Should().BeFalse();
        result.Should().Contain("fields");
    }

    [Fact(DisplayName = "set_query: projection で result_type_name 欠落は保存拒否")]
    public void SetQuery_ProjectionMissingResultTypeName_Rejected()
    {
        var file = CreateOrdersFile();

        var (result, success) = Exec(
            SetQuery,
            file,
            new
            {
                table_name = "orders",
                query_name = "X",
                returns = "projection",
                fields = new[] { new { name = "OrderId", source_column = "order_id" } },
            }
        );

        success.Should().BeFalse();
        result.Should().Contain("result_type_name");
    }

    [Fact(DisplayName = "set_query: パラメータの type と source_column の両方指定は保存拒否")]
    public void SetQuery_ParamBothTypeAndSource_Rejected()
    {
        var file = CreateOrdersFile();

        var (result, success) = Exec(
            SetQuery,
            file,
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
            }
        );

        success.Should().BeFalse();
        result.Should().Contain("exactly one");
    }

    [Fact(DisplayName = "set_query: パラメータの type / source_column の両方欠落は保存拒否")]
    public void SetQuery_ParamNeither_Rejected()
    {
        var file = CreateOrdersFile();

        var (result, success) = Exec(
            SetQuery,
            file,
            new
            {
                table_name = "orders",
                query_name = "X",
                returns = "list",
                parameters = new[] { new { name = "p" } },
            }
        );

        success.Should().BeFalse();
        result.Should().Contain("exactly one");
    }

    [Fact(DisplayName = "set_query: order_by を list/single/projection 以外に使うと保存拒否")]
    public void SetQuery_OrderByMisuse_Rejected()
    {
        var file = CreateOrdersFile();

        var (result, success) = Exec(
            SetQuery,
            file,
            new
            {
                table_name = "orders",
                query_name = "X",
                returns = "count",
                order_by = new[] { new { column = "order_id" } },
            }
        );

        success.Should().BeFalse();
        result.Should().Contain("order_by");
    }

    [Fact(DisplayName = "set_query: single＋order_by（並び替えて先頭 1 件）は正当")]
    public void SetQuery_SingleWithOrderBy_Succeeds()
    {
        var file = CreateOrdersFile();

        var (_, success) = Exec(
            SetQuery,
            file,
            new
            {
                table_name = "orders",
                query_name = "FindTop",
                returns = "single",
                order_by = new[] { new { column = "order_id", descending = true } },
            }
        );

        success.Should().BeTrue();
        var query = QueriesOf(file).Single();
        query.Returns.Should().Be(QueryReturnShape.Single);
        query.OrderBy.Single().Descending.Should().BeTrue();
    }

    [Fact(DisplayName = "set_query: 未知の SQL 方言名は保存拒否")]
    public void SetQuery_UnknownSqlDialect_Rejected()
    {
        var file = CreateOrdersFile();

        var (result, success) = Exec(
            SetQuery,
            file,
            new
            {
                table_name = "orders",
                query_name = "X",
                returns = "list",
                implementation = "sql",
                sql = new Dictionary<string, string> { ["db2"] = "SELECT * FROM orders" },
            }
        );

        success.Should().BeFalse();
        result.Should().Contain("db2");
    }

    [Fact(DisplayName = "set_query: returns 未指定は保存拒否")]
    public void SetQuery_MissingReturns_Rejected()
    {
        var file = CreateOrdersFile();

        var (result, success) = Exec(
            SetQuery,
            file,
            new { table_name = "orders", query_name = "X" }
        );

        success.Should().BeFalse();
        result.Should().Contain("returns");
    }
}
