using QuickER.Mcp.Tools;
using QuickER.Model;
using ChatStrings = QuickER.AI.Chat.Resources.Strings;

namespace QuickER.Tests.Mcp.Tools;

internal static partial class ToolScenarioRows
{
    /// <summary>orders に FindTop（単一）を set_query で入れる前提</summary>
    private static ToolRow WithFindTop(ToolRow row) =>
        row.Arrange(
            QueryToolCore.SetQueryToolName,
            new
            {
                table_name = "orders",
                query_name = "FindTop",
                returns = "single",
            }
        );

    /// <summary>list_queries（一覧）と remove_query（削除）</summary>
    private static IEnumerable<ToolRow> QueryListAndRemoveRows()
    {
        // 結果のクエリ名は応答の構造にだけあるので Core.Check で見る
        yield return WithFindTop(
                Row("query-remove-removes-sqlite", "remove_query: 1 件削除する")
                    .Dialect("sqlite")
                    .Base(Orders)
            )
            .Call(
                QueryToolCore.RemoveQueryToolName,
                new { table_name = "orders", query_name = "FindTop" },
                e =>
                    e.Ok()
                        .Wrote()
                        .Core.Status(QueryToolStatus.Success)
                        .Core.Check(
                            "結果のクエリ名は FindTop",
                            outcome =>
                                outcome.QueryName == "FindTop" ? null : $"{outcome.QueryName}"
                        )
                        .Schema.Kind("query")
                        .Schema.Kind("qdef")
            );

        yield return Row("query-remove-missing-fails-sqlite", "remove_query: 不在はエラー")
            .Dialect("sqlite")
            .Base(Orders)
            .Call(
                QueryToolCore.RemoveQueryToolName,
                new { table_name = "orders", query_name = "NoSuch" },
                e =>
                    e.Fails()
                        .Unchanged()
                        .Core.Status(QueryToolStatus.QueryNotFound)
                        .Result.Contains("NoSuch")
                        .ResultContainsByHost(
                            gui: string.Format(
                                ChatStrings.QueryTool_QueryNotFound,
                                "NoSuch",
                                "orders"
                            ),
                            file: "not found"
                        )
            );

        yield return WithFindTop(
                Row(
                        "query-remove-missing-keeps-other-queries-sqlite",
                        "RemoveQuery: 不在は QueryNotFound で図を変えない"
                    )
                    .Dialect("sqlite")
                    .Base(Orders)
            )
            .Call(
                QueryToolCore.RemoveQueryToolName,
                new { table_name = "orders", query_name = "NoSuch" },
                e =>
                    e.Fails()
                        .Unchanged()
                        .Core.Status(QueryToolStatus.QueryNotFound)
                        .Schema.Kind("query", "query orders.FindTop Single")
            );

        yield return Row(
                "query-remove-unknown-table-fails-sqlite",
                "RemoveQuery: 未知テーブルは TableNotFound"
            )
            .Dialect("sqlite")
            .Base(Orders)
            .Call(
                QueryToolCore.RemoveQueryToolName,
                new { table_name = "nope", query_name = "X" },
                e => e.Fails().Unchanged().Core.Status(QueryToolStatus.TableNotFound)
            );

        // 一覧の構造（グループ・列の参照の名前の解決）は応答の構造にだけあるので Core.Check で見る
        yield return Row(
                "query-list-returns-structured-listing-sqlite",
                "ListQueries: テーブル別グループ＋列参照名解決の一覧データを返す"
            )
            .Dialect("sqlite")
            .Base(Orders)
            .Arrange(
                QueryToolCore.SetQueryToolName,
                new
                {
                    table_name = "orders",
                    query_name = "GetByCustomer",
                    returns = "list",
                    condition = "customer_id = @customerId",
                    parameters = new[]
                    {
                        new { name = "customerId", source_column = "customer_id" },
                    },
                    order_by = new[] { new { column = "order_id", descending = true } },
                }
            )
            .Call(
                QueryToolCore.ListQueriesToolName,
                new { },
                e =>
                    e.Ok()
                        .Unchanged()
                        .Core.Status(QueryToolStatus.Success)
                        .Core.Check(
                            "一覧は orders の GetByCustomer 1 件（列参照のパラメータ customer_id・並び order_id desc）",
                            outcome =>
                                outcome.Listing
                                    is {
                                        TotalCount: 1,
                                        Groups: [
                                            {
                                                TableName: "orders",
                                                Queries: [
                                                    {
                                                        Name: "GetByCustomer",
                                                        Returns: QueryReturnShape.List,
                                                        Parameters: [
                                                            {
                                                                IsColumnReference: true,
                                                                ColumnName: "customer_id",
                                                            },
                                                        ],
                                                        OrderBy: [
                                                            {
                                                                ColumnName: "order_id",
                                                                Descending: true
                                                            },
                                                        ],
                                                    },
                                                ],
                                            },
                                        ],
                                    }
                                    ? null
                                    : "一覧の形が違う"
                        )
            );

        yield return Row(
                "query-list-lists-by-entity-sqlite",
                "list_queries: エンティティ別に一覧する"
            )
            .Dialect("sqlite")
            .Base(Orders)
            .Arrange(
                QueryToolCore.SetQueryToolName,
                new
                {
                    table_name = "orders",
                    query_name = "CountByCustomer",
                    returns = "count",
                    condition = "customer_id = @customerId",
                    parameters = new[] { new { name = "customerId", type = "int32" } },
                }
            )
            .Call(
                QueryToolCore.ListQueriesToolName,
                new { },
                e =>
                    e.Ok()
                        .Unchanged()
                        .Result.Contains("[orders]", "CountByCustomer", "count", "customerId")
                        .ResultContainsByHost(
                            gui: string.Format(ChatStrings.QueryTool_ListHeader, 1),
                            file: "Queries: 1"
                        )
            );

        yield return Row("query-list-empty-succeeds-sqlite", "list_queries: クエリなしでも成功する")
            .Dialect("sqlite")
            .Base(Orders)
            .Call(
                QueryToolCore.ListQueriesToolName,
                new { },
                e =>
                    e.Ok()
                        .Unchanged()
                        .Core.Check(
                            "一覧は総数 0・グループなし",
                            outcome =>
                                outcome.Listing is { TotalCount: 0, Groups: [] }
                                    ? null
                                    : "一覧が空でない"
                        )
                        .ResultContainsByHost(
                            gui: string.Format(ChatStrings.QueryTool_ListHeader, 0),
                            file: "Queries: 0"
                        )
            );

        // 参照先の消えたクエリはツールで作れないので直接足す。グループのテーブル名（null）は応答の構造にだけある
        yield return Row(
                "query-list-orphan-is-grouped-under-unknown-entity-sqlite",
                "ListQueries: 参照先が消えたクエリは不明エンティティグループ（TableName=null）に入る"
            )
            .Dialect("sqlite")
            .Base(Orders)
            .Seed(
                "どのテーブルにも属さないクエリ Dangling（一覧）",
                session =>
                    session.AddQuery(
                        new QueryDefinition
                        {
                            EntityId = Guid.NewGuid(),
                            Name = "Dangling",
                            Returns = QueryReturnShape.List,
                        }
                    )
            )
            .Call(
                QueryToolCore.ListQueriesToolName,
                new { },
                e =>
                    e.Ok()
                        .Unchanged()
                        .Core.Check(
                            "グループは TableName=null の 1 つで、Dangling を持つ",
                            outcome =>
                                outcome.Listing
                                    is {
                                        Groups: [
                                            { TableName: null, Queries: [{ Name: "Dangling" }] },
                                        ],
                                    }
                                    ? null
                                    : "グループの形が違う"
                        )
            );
    }

    /// <summary>入力の扱い（使われない入力は警告で知らせ断らない・食い違いは断る・どのテーブルにも属さないクエリ）</summary>
    private static IEnumerable<ToolRow> QueryInputRuleRows()
    {
        yield return Row(
                "query-input-order-by-with-non-dsl-implementation-is-warned-sqlite",
                "生 SQL・手動実装で order_by を指定したら、生成で使われないことを警告で知らせる（保存は GUI と同じく行う）"
            )
            .Dialect("sqlite")
            .Base(OrdersWithoutMemo)
            .Call(
                QueryToolCore.SetQueryToolName,
                new
                {
                    table_name = "orders",
                    query_name = "Latest",
                    returns = "list",
                    implementation = "manual",
                    order_by = new[] { new { column = "order_id", descending = true } },
                },
                e =>
                    e.Ok()
                        .Wrote()
                        .CoreWarningsContain(
                            QueryToolDiagnosticCode.OrderByIgnoredForImplementation
                        )
                        .Schema.Kind("qdef", "qdef orders.Latest List * order=(@order_id desc) *")
            );

        yield return Row(
                "query-input-unknown-dialect-key-with-dsl-is-only-warned-sqlite",
                "生 SQL 以外の実装では、使わない sql の未知の方言キーで保存を断らない（警告にとどめる）"
            )
            .Dialect("sqlite")
            .Base(OrdersWithoutMemo)
            .Call(
                QueryToolCore.SetQueryToolName,
                new
                {
                    table_name = "orders",
                    query_name = "All",
                    returns = "list",
                    implementation = "dsl",
                    sql = new { postgres = "SELECT * FROM orders" },
                },
                e =>
                    e.Ok()
                        .Wrote()
                        .Core.Check(
                            "警告がある",
                            outcome => outcome.Warnings.Count > 0 ? null : "警告が無い"
                        )
            );

        yield return Row(
                "query-input-sql-for-dialect-without-raw-sql-implementation-is-warned-sqlite",
                "生成器が生 SQL の実装を作らない方言（postgresql・mysql・oracle）の SQL は、手動実装になると警告する"
            )
            .Dialect("sqlite")
            .Base(OrdersWithoutMemo)
            .Call(
                QueryToolCore.SetQueryToolName,
                new
                {
                    table_name = "orders",
                    query_name = "All",
                    returns = "list",
                    implementation = "sql",
                    sql = new { postgresql = "SELECT * FROM orders" },
                },
                e =>
                    e.Ok()
                        .Wrote()
                        .Core.Check(
                            "方言 postgresql の警告がある",
                            outcome =>
                                outcome.Warnings.Any(warning => warning.Dialect == "postgresql")
                                    ? null
                                    : "方言 postgresql の警告が無い"
                        )
            );

        // 大文字小文字だけ違う 2 つのキーは匿名型・辞書の直列化で書けるが、元の入力（JSON の文字列）のまま渡す
        yield return Row(
                "query-input-dialect-keys-differing-only-in-case-are-refused-sqlite",
                "sql の方言キーが大文字小文字だけ違って 2 つあれば、黙って片方を捨てず断る"
            )
            .Dialect("sqlite")
            .Base(OrdersWithoutMemo)
            .Call(
                QueryToolCore.SetQueryToolName,
                System
                    .Text.Json.JsonDocument.Parse(
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
                    .RootElement,
                e =>
                    e.Fails()
                        .Unchanged()
                        .Core.Check("失敗する", outcome => outcome.Success ? "成功した" : null)
            );

        yield return Row(
                "query-input-dsl-parameters-without-condition-are-warned-as-unused-sqlite",
                "条件を書かない簡易 DSL で宣言したパラメータは、条件で使うパラメータと同じく未使用の警告を出す"
            )
            .Dialect("sqlite")
            .Base(OrdersWithoutMemo)
            .Call(
                QueryToolCore.SetQueryToolName,
                new
                {
                    table_name = "orders",
                    query_name = "ByCustomer",
                    returns = "list",
                    implementation = "dsl",
                    parameters = new[]
                    {
                        new { name = "customerId", source_column = "customer_id" },
                    },
                },
                e =>
                    e.Ok()
                        .Wrote()
                        .CoreWarningsContain(
                            QueryToolDiagnosticCode.ParameterUnusedInCondition,
                            warning => warning.Name == "customerId",
                            "named customerId"
                        )
            );

        yield return WithLeftoverQuery(
                Row(
                    "query-input-orphan-query-does-not-block-result-type-name-sqlite",
                    "どのテーブルにも属さないクエリ（生成器が読み飛ばす）の結果型名は、新しい射影の結果型名を塞がない"
                )
            )
            .Call(
                QueryToolCore.SetQueryToolName,
                new
                {
                    table_name = "orders",
                    query_name = "Rows",
                    returns = "projection",
                    result_type_name = "OrderRow",
                    fields = new[] { new { name = "Amount", source_column = "amount" } },
                },
                e => e.Ok().Wrote()
            );

        yield return WithLeftoverQuery(
                Row(
                    "query-input-orphan-query-is-removed-by-query-name-sqlite",
                    "どのテーブルにも属さないクエリは、テーブル名が図に無くてもクエリ名で remove_query から消せる"
                )
            )
            .Call(
                QueryToolCore.RemoveQueryToolName,
                new { table_name = "deleted_table", query_name = "Leftover" },
                e => e.Ok().Wrote().Schema.Kind("query").Schema.Kind("qdef")
            );
    }

    /// <summary>
    /// orders（memo なし）に、どのテーブルにも属さない射影のクエリ Leftover（結果型名 OrderRow）を直接足した前提
    /// （参照先の消えたクエリはツールで作れない）
    /// </summary>
    private static ToolRow WithLeftoverQuery(ToolRow row) =>
        row.Dialect("sqlite")
            .Base(OrdersWithoutMemo)
            .Seed(
                "どのテーブルにも属さない射影のクエリ Leftover（結果型名 OrderRow）",
                session =>
                    session.AddQuery(
                        new QueryDefinition
                        {
                            EntityId = Guid.NewGuid(),
                            Name = "Leftover",
                            Returns = QueryReturnShape.Projection,
                            ResultTypeName = "OrderRow",
                        }
                    )
            );
}
