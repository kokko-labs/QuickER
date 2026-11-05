using QuickER.Mcp.Tools;
using QuickER.Model;
using QuickER.Tests.TestSupport;
using ChatStrings = QuickER.AI.Chat.Resources.Strings;

namespace QuickER.Tests.Mcp.Tools;

internal static partial class ToolScenarioRows
{
    /// <summary>
    /// orders（order_id PK・customer_id・amount・memo）の図（名前付きクエリの行の共通の前提・方言は sqlite）
    /// </summary>
    public static IEnumerable<(string Tool, object Args)> Orders =>
        [.. OrdersWithoutMemo, OrdersColumn("memo", "nvarchar(50)")];

    /// <summary>orders（order_id PK・customer_id・amount）の図（入力の扱いの行の前提・方言は sqlite）</summary>
    public static IEnumerable<(string Tool, object Args)> OrdersWithoutMemo =>
        [
            ("add_entity", new { table_name = "orders" }),
            (
                "add_column",
                new
                {
                    table_name = "orders",
                    column_name = "order_id",
                    data_type = "int",
                    is_primary_key = true,
                    is_nullable = false,
                }
            ),
            OrdersColumn("customer_id", "int"),
            OrdersColumn("amount", "decimal(10,2)"),
        ];

    /// <summary>Order（OrderId PK・CustomerId）の図（内蔵チャットのアダプタの行の前提・方言は sqlserver）</summary>
    public static IEnumerable<(string Tool, object Args)> OrderTable =>
        [
            ("add_entity", new { table_name = "Order" }),
            (
                "add_column",
                new
                {
                    table_name = "Order",
                    column_name = "OrderId",
                    data_type = "int",
                    is_primary_key = true,
                }
            ),
            (
                "add_column",
                new
                {
                    table_name = "Order",
                    column_name = "CustomerId",
                    data_type = "int",
                }
            ),
        ];

    private static (string Tool, object Args) OrdersColumn(string name, string dataType) =>
        (
            "add_column",
            new
            {
                table_name = "orders",
                column_name = name,
                data_type = dataType,
                is_primary_key = false,
                is_nullable = true,
            }
        );

    /// <summary>
    /// 名前付きクエリのツール（set_query・list_queries・remove_query）。GUI はチャットのアダプタ経由で流し、
    /// 共有コアの構造の結果（<see cref="ToolExpect.Core"/>）は <see cref="ToolScenarioTests.Row_MatchesCore"/> が照合する
    /// </summary>
    private static IEnumerable<ToolRow> QueryRows() =>
        [
            .. QuerySaveRows(),
            .. QueryWarningRows(),
            .. QueryRejectionRows(),
            .. QueryListAndRemoveRows(),
            .. QueryInputRuleRows(),
            .. QueryOrderTableRows(),
        ];

    /// <summary>set_query が戻りの形・実装の方式ごとに定義を保存する</summary>
    private static IEnumerable<ToolRow> QuerySaveRows()
    {
        yield return Row(
                "query-set-dsl-list-saves-sqlite",
                "set_query: DSL 一覧（条件・パラメータ・並び順・ページング）を保存する"
            )
            .Dialect("sqlite")
            .Base(Orders)
            .Call(
                QueryToolCore.SetQueryToolName,
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
                },
                e =>
                    e.Ok()
                        .Wrote()
                        .Core.Status(QueryToolStatus.Success)
                        .Core.WasUpdate(false)
                        .Core.Errors()
                        .Core.Warnings()
                        .Schema.Kind(
                            "qdef",
                            "qdef orders.GetByCustomer List id=* impl=Dsl scalar=- result=- cond=\"customer_id = @customerId\" params=(customerId:int32) order=(@order_id desc) paging=True fields=() sql=() desc=\"orders by customer, newest first\""
                        )
            );

        // 結果の名前・表・戻りの形・方式は応答の構造にだけあり、面（Status など）で書けないので Core.Check で見る
        yield return Row(
                "query-set-dsl-list-returns-structured-success-sqlite",
                "SetQuery: DSL 一覧の成功結果データを返し図へ追加する"
            )
            .Dialect("sqlite")
            .Base(Orders)
            .Call(
                QueryToolCore.SetQueryToolName,
                new
                {
                    table_name = "orders",
                    query_name = "GetByCustomer",
                    returns = "list",
                    condition = "customer_id = @customerId",
                    parameters = new[] { new { name = "customerId", type = "int32" } },
                },
                e =>
                    e.Ok()
                        .Wrote()
                        .Core.Status(QueryToolStatus.Success)
                        .Core.WasUpdate(false)
                        .Core.Errors()
                        .Core.Warnings()
                        .Core.Check(
                            "outcome は GetByCustomer・orders・List・Dsl",
                            outcome =>
                                outcome
                                    is {
                                        QueryName: "GetByCustomer",
                                        TableName: "orders",
                                        Returns: QueryReturnShape.List,
                                        Implementation: QueryImplementationKind.Dsl,
                                    }
                                    ? null
                                    : $"{outcome.QueryName}・{outcome.TableName}・{outcome.Returns}・{outcome.Implementation}"
                        )
                        .Schema.Kind(
                            "qdef",
                            "qdef orders.GetByCustomer List id=* impl=Dsl scalar=- result=- cond=\"customer_id = @customerId\" params=(customerId:int32) order=() paging=False fields=() sql=() desc=\"\""
                        )
            );

        yield return Row(
                "query-set-dsl-single-saves-sqlite",
                "set_query: DSL 単一（条件なし）を保存する"
            )
            .Dialect("sqlite")
            .Base(Orders)
            .Call(
                QueryToolCore.SetQueryToolName,
                new
                {
                    table_name = "orders",
                    query_name = "FindTop",
                    returns = "single",
                },
                e =>
                    e.Ok()
                        .Wrote()
                        .Schema.Kind(
                            "qdef",
                            "qdef orders.FindTop Single id=* impl=Dsl scalar=- result=- cond=- params=() order=() paging=False fields=() sql=() desc=\"\""
                        )
            );

        yield return Row("query-set-dsl-count-saves-sqlite", "set_query: DSL 件数を保存する")
            .Dialect("sqlite")
            .Base(Orders)
            .Call(
                QueryToolCore.SetQueryToolName,
                new
                {
                    table_name = "orders",
                    query_name = "CountByCustomer",
                    returns = "count",
                    condition = "customer_id = @customerId",
                    parameters = new[] { new { name = "customerId", type = "int32" } },
                },
                e =>
                    e.Ok()
                        .Wrote()
                        .Schema.Kind(
                            "qdef",
                            "qdef orders.CountByCustomer Count id=* impl=Dsl scalar=- result=- cond=\"customer_id = @customerId\" params=(customerId:int32) order=() paging=False fields=() sql=() desc=\"\""
                        )
            );

        yield return Row(
                "query-set-dsl-projection-saves-column-fields-sqlite",
                "set_query: DSL 射影（列参照フィールド）を保存する"
            )
            .Dialect("sqlite")
            .Base(Orders)
            .Call(
                QueryToolCore.SetQueryToolName,
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
                },
                e =>
                    e.Ok()
                        .Wrote()
                        .Schema.Kind(
                            "qdef",
                            "qdef orders.GetSummaries Projection id=* impl=Dsl scalar=- result=OrderSummaryRow cond=\"customer_id = @customerId\" params=(customerId:int32) order=() paging=False fields=(CustomerId:-@customer_id,Amount:-@amount) sql=() desc=\"\""
                        )
            );

        yield return Row(
                "query-set-sql-scalar-saves-sqlite",
                "set_query: 生 SQL スカラーを保存する"
            )
            .Dialect("sqlite")
            .Base(Orders)
            .Call(
                QueryToolCore.SetQueryToolName,
                new
                {
                    table_name = "orders",
                    query_name = "SumAmounts",
                    returns = "scalar",
                    scalar_type = "decimal(10,2)",
                    implementation = "sql",
                    sql = new Dictionary<string, string>
                    {
                        ["sqlite"] =
                            "SELECT SUM(amount) FROM orders WHERE customer_id = @customerId",
                    },
                    parameters = new[] { new { name = "customerId", type = "int32" } },
                },
                e =>
                    e.Ok()
                        .Wrote()
                        .Schema.Kind(
                            "qdef",
                            "qdef orders.SumAmounts Scalar id=* impl=Sql scalar=decimal(10,2) result=- cond=- params=(customerId:int32) order=() paging=False fields=() sql=(sqlite=\"SELECT SUM(amount) FROM orders WHERE customer_id = @customerId\") desc=\"\""
                        )
            );

        // SQL の * はパターンの任意の文字列と同じ文字なので、方言のキーと引用だけを書く
        yield return Row("query-set-sql-list-saves-sqlite", "set_query: 生 SQL 一覧を保存する")
            .Dialect("sqlite")
            .Base(Orders)
            .Call(
                QueryToolCore.SetQueryToolName,
                new
                {
                    table_name = "orders",
                    query_name = "ListSql",
                    returns = "list",
                    implementation = "sql",
                    sql = new Dictionary<string, string> { ["sqlite"] = "SELECT * FROM orders" },
                },
                e =>
                    e.Ok()
                        .Wrote()
                        .Schema.Kind(
                            "qdef",
                            "qdef orders.ListSql List id=* impl=Sql scalar=- result=- cond=- params=() order=() paging=False fields=() sql=(sqlite=\"SELECT * FROM orders\") desc=\"\""
                        )
            );

        yield return Row(
                "query-set-sql-projection-saves-typed-fields-sqlite",
                "set_query: 生 SQL 射影（型トークンフィールド）を保存する"
            )
            .Dialect("sqlite")
            .Base(Orders)
            .Call(
                QueryToolCore.SetQueryToolName,
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
                },
                e =>
                    e.Ok()
                        .Wrote()
                        .Schema.Kind(
                            "qdef",
                            "qdef orders.MemoRows Projection id=* impl=Sql scalar=- result=OrderMemoRow cond=- params=() order=() paging=False fields=(OrderId:int32,Memo:string(50)) sql=(sqlite=\"SELECT order_id AS OrderId, memo AS Memo FROM orders\") desc=\"\""
                        )
            );

        yield return Row(
                "query-set-manual-saves-contract-sqlite",
                "set_query: manual（契約のみ）を保存する"
            )
            .Dialect("sqlite")
            .Base(Orders)
            .Call(
                QueryToolCore.SetQueryToolName,
                new
                {
                    table_name = "orders",
                    query_name = "SpecialLookup",
                    returns = "single",
                    implementation = "manual",
                    parameters = new[] { new { name = "customerId", type = "int32" } },
                },
                e =>
                    e.Ok()
                        .Wrote()
                        .Schema.Kind(
                            "qdef",
                            "qdef orders.SpecialLookup Single id=* impl=Manual scalar=- result=- cond=- params=(customerId:int32) order=() paging=False fields=() sql=() desc=\"\""
                        )
            );

        yield return Row(
                "query-set-column-typed-parameter-saves-column-id-sqlite",
                "SetQuery: 列参照パラメータは列 ID で保存する（Type は null）"
            )
            .Dialect("sqlite")
            .Base(Orders)
            .Call(
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
                },
                e =>
                    e.Ok()
                        .Wrote()
                        .Core.Status(QueryToolStatus.Success)
                        .Schema.Kind(
                            "qdef",
                            "qdef orders.GetByCustomer List id=* impl=Dsl scalar=- result=- cond=\"customer_id = @customerId\" params=(customerId:-@customer_id) order=() paging=False fields=() sql=() desc=\"\""
                        )
            );

        yield return Row(
                "query-set-column-typed-parameter-saves-sqlite",
                "set_query: 列参照型付けパラメータは列 ID で保存する"
            )
            .Dialect("sqlite")
            .Base(Orders)
            .Call(
                QueryToolCore.SetQueryToolName,
                new
                {
                    table_name = "orders",
                    query_name = "GetByCustomerTyped",
                    returns = "list",
                    condition = "customer_id = @customerId",
                    parameters = new[]
                    {
                        new { name = "customerId", source_column = "customer_id" },
                    },
                },
                e =>
                    e.Ok()
                        .Wrote()
                        .Schema.Kind(
                            "qdef",
                            "qdef orders.GetByCustomerTyped List id=* impl=Dsl scalar=- result=- cond=\"customer_id = @customerId\" params=(customerId:-@customer_id) order=() paging=False fields=() sql=() desc=\"\""
                        )
            );

        yield return Row(
                "query-set-in-list-parameter-saves-sqlite",
                "set_query: IN 条件（リストパラメータ）を保存する"
            )
            .Dialect("sqlite")
            .Base(Orders)
            .Call(
                QueryToolCore.SetQueryToolName,
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
                },
                e =>
                    e.Ok()
                        .Wrote()
                        .Schema.Kind(
                            "qdef",
                            "qdef orders.GetByIds List id=* impl=Dsl scalar=- result=- cond=\"order_id IN @ids\" params=(ids:int32[]) order=() paging=False fields=() sql=() desc=\"\""
                        )
            );

        yield return Row(
                "query-set-single-with-order-by-is-accepted-sqlite",
                "set_query: single＋order_by（並び替えて先頭 1 件）は正当"
            )
            .Dialect("sqlite")
            .Base(Orders)
            .Call(
                QueryToolCore.SetQueryToolName,
                new
                {
                    table_name = "orders",
                    query_name = "FindTop",
                    returns = "single",
                    order_by = new[] { new { column = "order_id", descending = true } },
                },
                e =>
                    e.Ok()
                        .Wrote()
                        .Schema.Kind(
                            "qdef",
                            "qdef orders.FindTop Single id=* impl=Dsl scalar=- result=- cond=- params=() order=(@order_id desc) paging=False fields=() sql=() desc=\"\""
                        )
            );

        yield return Row(
                "query-set-upsert-preserves-id-and-replaces-sqlite",
                "set_query: 同名で再定義すると Id を温存して丸ごと置換する"
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
                    parameters = new[] { new { name = "customerId", type = "int32" } },
                }
            )
            .Call(
                QueryToolCore.SetQueryToolName,
                new
                {
                    table_name = "orders",
                    query_name = "getbycustomer",
                    returns = "count",
                },
                e =>
                    e.Ok()
                        .Wrote()
                        .Core.Status(QueryToolStatus.Success)
                        .Core.WasUpdate(true)
                        .QueryIdKeptAcrossRename("orders.GetByCustomer", "orders.getbycustomer")
                        .Schema.Kind(
                            "qdef",
                            "qdef orders.getbycustomer Count id=* impl=Dsl scalar=- result=- cond=- params=() order=() paging=False fields=() sql=() desc=\"\""
                        )
                        .ResultContainsByHost(
                            gui: string.Format(
                                ChatStrings.QueryTool_SetSuccessUpdated,
                                "getbycustomer",
                                "orders",
                                "count",
                                "dsl"
                            ),
                            file: "Updated"
                        )
            );
    }
}

/// <summary>名前付きクエリの行の期待の組み立て（ホストで文言が違う応答・面で書けない表明）</summary>
internal static class QueryRowExpect
{
    /// <summary>名前付きクエリのツールの応答がホストで違う理由</summary>
    public const string Reason = "GUI の応答は resx（UI の言語）・MCP は英語の固定文";

    /// <summary>応答が、GUI は <paramref name="gui"/> を・MCP は <paramref name="file"/> を含む</summary>
    public static ToolExpect ResultContainsByHost(
        this ToolExpect expect,
        string gui,
        string file
    ) =>
        expect
            .ForHost(ToolHostKind.Gui, Reason, g => g.Result.Contains(gui))
            .ForHost(ToolHostKind.File, Reason, f => f.Result.Contains(file));

    /// <summary>応答が、GUI は <paramref name="gui"/> を・MCP は <paramref name="file"/> を含まない</summary>
    public static ToolExpect ResultAbsentByHost(this ToolExpect expect, string gui, string file) =>
        expect
            .ForHost(ToolHostKind.Gui, Reason, g => g.Result.Absent(gui))
            .ForHost(ToolHostKind.File, Reason, f => f.Result.Absent(file));

    /// <summary>応答が簡易 DSL の条件の診断を含む（GUI は resx の「条件:」の前置き・MCP は英語の Condition）</summary>
    public static ToolExpect ConditionDiagnosticByHost(this ToolExpect expect) =>
        expect.ResultContainsByHost(
            gui: ChatStrings.QueryTool_Diag_Condition.Replace("{0}", string.Empty).Trim(),
            file: "Condition"
        );

    /// <summary>
    /// 呼び出しの前の <paramref name="before"/>（<c>表.名前</c>）と後の <paramref name="after"/> の qdef の Id が同じ
    /// </summary>
    /// <remarks>
    /// 同名（大文字小文字を無視）の再定義は、新しい綴りで保存し直し Id を温存する。前後で名前の綴りが変わるので、
    /// 同じ名前で前後を引く <see cref="ToolExpect.QueryIdKept"/> では書けない。
    /// </remarks>
    public static ToolExpect QueryIdKeptAcrossRename(
        this ToolExpect expect,
        string before,
        string after
    ) =>
        expect.Check(
            $"query id kept {before} -> {after}",
            observation =>
            {
                var beforeId = QueryId(observation.Before, before);
                var afterId = QueryId(observation.After, after);

                if (beforeId is null || afterId is null)
                {
                    return $"前後のどちらかにクエリが無い（前 {beforeId ?? "なし"}・後 {afterId ?? "なし"}）";
                }

                return beforeId == afterId ? null : $"Id が変わった（{beforeId} → {afterId}）";
            }
        );

    /// <summary>共有コアの警告に、この種類（と追加の条件）のものがある</summary>
    public static ToolExpect CoreWarningsContain(
        this ToolExpect expect,
        QueryToolDiagnosticCode code,
        Func<QueryToolDiagnostic, bool>? match = null,
        string? matchDescription = null
    ) =>
        expect.Core.Check(
            $"core warnings contain {code}{(matchDescription is null ? string.Empty : " " + matchDescription)}",
            outcome =>
                outcome.Warnings.Any(warning =>
                    warning.Code == code && (match is null || match(warning))
                )
                    ? null
                    : $"警告が [{string.Join(", ", outcome.Warnings.Select(Describe))}]"
        );

    /// <summary>共有コアのエラーに、この種類のものがある</summary>
    public static ToolExpect CoreErrorsContain(
        this ToolExpect expect,
        QueryToolDiagnosticCode code
    ) =>
        expect.Core.Check(
            $"core errors contain {code}",
            outcome =>
                outcome.Errors.Any(error => error.Code == code)
                    ? null
                    : $"エラーが [{string.Join(", ", outcome.Errors.Select(Describe))}]"
        );

    private static string Describe(QueryToolDiagnostic diagnostic) =>
        $"{diagnostic.Code}(name={diagnostic.Name} dialect={diagnostic.Dialect} detail={diagnostic.Detail})";

    /// <summary>写しの qdef の行から Id を読む（無ければ null）</summary>
    private static string? QueryId(IReadOnlyList<string> lines, string tableAndName)
    {
        var prefix = $"qdef {tableAndName} ";
        var line = lines.SingleOrDefault(line => line.StartsWith(prefix, StringComparison.Ordinal));

        if (line is null)
        {
            return null;
        }

        var start = line.IndexOf(" id=", StringComparison.Ordinal) + 4;

        return line[start..line.IndexOf(' ', start)];
    }
}
