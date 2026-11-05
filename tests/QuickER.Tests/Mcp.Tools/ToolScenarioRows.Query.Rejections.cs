using QuickER.Mcp.Tools;
using ChatStrings = QuickER.AI.Chat.Resources.Strings;

namespace QuickER.Tests.Mcp.Tools;

internal static partial class ToolScenarioRows
{
    /// <summary>set_query が保存を断り、図を変えない（引数・テーブル・戻りの形・方式・定義の検証）</summary>
    private static IEnumerable<ToolRow> QueryRejectionRows()
    {
        // table_name の欠落は、GUI はアダプタの引数の形の検査が・File は共有コアが断る（コアの MissingArgument は最終防衛）
        yield return Row(
                "query-set-missing-table-and-query-name-fails-sqlite",
                "SetQuery: table_name/query_name 欠落は MissingArgument"
            )
            .Dialect("sqlite")
            .Base(Orders)
            .Call(
                QueryToolCore.SetQueryToolName,
                new { returns = "list" },
                e => e.Fails().Unchanged().Core.Status(QueryToolStatus.MissingArgument)
            );

        // 結果のテーブル名は応答の構造にだけあるので Core.Check で見る
        yield return Row(
                "query-set-unknown-table-fails-sqlite",
                "set_query: 存在しないテーブルはエラー"
            )
            .Dialect("sqlite")
            .Base(Orders)
            .Call(
                QueryToolCore.SetQueryToolName,
                new
                {
                    table_name = "nope",
                    query_name = "X",
                    returns = "list",
                },
                e =>
                    e.Fails()
                        .Unchanged()
                        .Core.Status(QueryToolStatus.TableNotFound)
                        .Core.Check(
                            "結果のテーブル名は nope",
                            outcome => outcome.TableName == "nope" ? null : $"{outcome.TableName}"
                        )
                        .ResultContainsByHost(
                            gui: string.Format(ChatStrings.QueryTool_TableNotFound, "nope"),
                            file: "not found"
                        )
            );

        // returns の欠落は、GUI はアダプタの引数の形の検査が・File は共有コアが断る
        yield return Row(
                "query-set-missing-returns-fails-sqlite",
                "set_query: returns 未指定は保存拒否"
            )
            .Dialect("sqlite")
            .Base(Orders)
            .Call(
                QueryToolCore.SetQueryToolName,
                new { table_name = "orders", query_name = "X" },
                e =>
                    e.Fails()
                        .Unchanged()
                        .Core.Status(QueryToolStatus.InvalidReturns)
                        .ResultContainsByHost(
                            gui: string.Format(ChatStrings.Tool_ArgumentMissing, "returns"),
                            file: "returns"
                        )
            );

        yield return Row(
                "query-set-invalid-implementation-fails-sqlite",
                "SetQuery: 不正な implementation は InvalidImplementation"
            )
            .Dialect("sqlite")
            .Base(Orders)
            .Call(
                QueryToolCore.SetQueryToolName,
                new
                {
                    table_name = "orders",
                    query_name = "X",
                    returns = "list",
                    implementation = "bogus",
                },
                e => e.Fails().Unchanged().Core.Status(QueryToolStatus.InvalidImplementation)
            );

        foreach (var row in QueryValidationRows())
        {
            yield return row;
        }

        yield return Row(
                "query-set-parameter-without-type-or-source-fails-sqlite",
                "set_query: パラメータの type / source_column の両方欠落は保存拒否"
            )
            .Dialect("sqlite")
            .Base(Orders)
            .Call(
                QueryToolCore.SetQueryToolName,
                new
                {
                    table_name = "orders",
                    query_name = "X",
                    returns = "list",
                    parameters = new[] { new { name = "p" } },
                },
                e =>
                    e.Fails()
                        .Unchanged()
                        .ResultContainsByHost(
                            gui: string.Format(
                                ChatStrings.QueryTool_Diag_ParameterTypeSourceExclusive,
                                "p"
                            ),
                            file: "exactly one"
                        )
            );

        yield return Row(
                "query-set-dsl-unknown-column-fails-sqlite",
                "set_query: DSL の未知列参照は保存拒否"
            )
            .Dialect("sqlite")
            .Base(Orders)
            .Call(
                QueryToolCore.SetQueryToolName,
                new
                {
                    table_name = "orders",
                    query_name = "X",
                    returns = "list",
                    condition = "no_such_col = @p",
                    parameters = new[] { new { name = "p", type = "int32" } },
                },
                e => e.Fails().Unchanged().ConditionDiagnosticByHost()
            );

        yield return Row(
                "query-set-dsl-undeclared-parameter-fails-sqlite",
                "set_query: DSL の未宣言 @パラメータは保存拒否"
            )
            .Dialect("sqlite")
            .Base(Orders)
            .Call(
                QueryToolCore.SetQueryToolName,
                new
                {
                    table_name = "orders",
                    query_name = "X",
                    returns = "list",
                    condition = "customer_id = @missing",
                },
                e => e.Fails().Unchanged().ConditionDiagnosticByHost()
            );
    }

    /// <summary>
    /// 定義の検証の失敗（診断の種類ごと）。共有コアは ValidationFailed と診断の種類を返し、両ホストは保存しない
    /// </summary>
    private static IEnumerable<ToolRow> QueryValidationRows()
    {
        yield return QueryValidationRow(
            "query-set-scalar-without-scalar-type-fails-sqlite",
            "set_query: scalar で scalar_type 欠落は保存拒否",
            new
            {
                table_name = "orders",
                query_name = "X",
                returns = "scalar",
                implementation = "sql",
                sql = new Dictionary<string, string> { ["sqlite"] = "SELECT COUNT(*) FROM orders" },
            },
            QueryToolDiagnosticCode.ScalarRequiresScalarType,
            e =>
                e.ResultContainsByHost(
                    gui: ChatStrings.QueryTool_Diag_ScalarRequiresScalarType,
                    file: "scalar_type"
                )
        );

        yield return QueryValidationRow(
            "query-set-projection-without-result-type-name-fails-sqlite",
            "set_query: projection で result_type_name 欠落は保存拒否",
            new
            {
                table_name = "orders",
                query_name = "X",
                returns = "projection",
                fields = new[] { new { name = "OrderId", source_column = "order_id" } },
            },
            QueryToolDiagnosticCode.ProjectionRequiresResultTypeName,
            e =>
                e.ResultContainsByHost(
                    gui: ChatStrings.QueryTool_Diag_ProjectionRequiresResultTypeName,
                    file: "result_type_name"
                )
        );

        yield return QueryValidationRow(
            "query-set-projection-without-fields-fails-sqlite",
            "set_query: projection で fields 欠落は保存拒否",
            new
            {
                table_name = "orders",
                query_name = "X",
                returns = "projection",
                result_type_name = "Row",
            },
            QueryToolDiagnosticCode.ProjectionRequiresFields,
            e =>
                e.ResultContainsByHost(
                    gui: ChatStrings.QueryTool_Diag_ProjectionRequiresFields,
                    file: "fields"
                )
        );

        yield return QueryValidationRow(
            "query-set-parameter-with-type-and-source-fails-sqlite",
            "set_query: パラメータの type と source_column の両方指定は保存拒否",
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
            e =>
                e.ResultContainsByHost(
                    gui: string.Format(
                        ChatStrings.QueryTool_Diag_ParameterTypeSourceExclusive,
                        "p"
                    ),
                    file: "exactly one"
                )
        );

        yield return QueryValidationRow(
            "query-set-unknown-parameter-source-column-fails-sqlite",
            "set_query: パラメータの source_column が所属エンティティに無ければエラー",
            new
            {
                table_name = "orders",
                query_name = "X",
                returns = "list",
                parameters = new[] { new { name = "p", source_column = "no_such_col" } },
            },
            QueryToolDiagnosticCode.ParameterSourceColumnNotFound,
            e => e.Result.Contains("no_such_col")
        );

        yield return QueryValidationRow(
            "query-set-order-by-on-count-fails-sqlite",
            "set_query: order_by を list/single/projection 以外に使うと保存拒否",
            new
            {
                table_name = "orders",
                query_name = "X",
                returns = "count",
                order_by = new[] { new { column = "order_id" } },
            },
            QueryToolDiagnosticCode.OrderByInvalidForReturnShape,
            e =>
                e.ResultContainsByHost(
                    gui: string.Format(
                        ChatStrings.QueryTool_Diag_OrderByInvalidForReturnShape,
                        "count"
                    ),
                    file: "order_by"
                )
        );

        yield return QueryValidationRow(
            "query-set-unknown-order-by-column-fails-sqlite",
            "set_query: order_by の列が無ければエラー",
            new
            {
                table_name = "orders",
                query_name = "X",
                returns = "list",
                order_by = new[] { new { column = "no_such_col" } },
            },
            QueryToolDiagnosticCode.OrderByColumnNotFound,
            e => e.Result.Contains("no_such_col")
        );

        yield return QueryValidationRow(
            "query-set-unknown-sql-dialect-fails-sqlite",
            "set_query: 未知の SQL 方言名は保存拒否",
            new
            {
                table_name = "orders",
                query_name = "X",
                returns = "list",
                implementation = "sql",
                sql = new Dictionary<string, string> { ["db2"] = "SELECT * FROM orders" },
            },
            QueryToolDiagnosticCode.UnknownSqlDialect,
            e => e.Result.Contains("db2")
        );

        yield return QueryValidationRow(
            "query-set-dsl-syntax-error-fails-sqlite",
            "set_query: DSL 構文エラーは保存拒否・ファイル不変",
            new
            {
                table_name = "orders",
                query_name = "X",
                returns = "list",
                condition = "customer_id = = @p",
            },
            QueryToolDiagnosticCode.ConditionDiagnostic,
            e => e.ConditionDiagnosticByHost()
        );

        yield return QueryValidationRow(
            "query-set-raw-sql-undeclared-parameter-fails-sqlite",
            "set_query: 生 SQL の未宣言パラメータは保存拒否",
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
            e => e.Core.Errors(QueryToolDiagnosticCode.RawSqlDiagnostic)
        );
    }

    /// <summary>
    /// 定義の検証の失敗の行（失敗・図を変えない・コアは ValidationFailed とこの種類の診断を含む＋応答の期待）
    /// </summary>
    private static ToolRow QueryValidationRow(
        string id,
        string description,
        object args,
        QueryToolDiagnosticCode code,
        Action<ToolExpect> response
    ) =>
        Row(id, description)
            .Dialect("sqlite")
            .Base(Orders)
            .Call(
                QueryToolCore.SetQueryToolName,
                args,
                e =>
                {
                    e.Fails()
                        .Unchanged()
                        .Core.Status(QueryToolStatus.ValidationFailed)
                        .CoreErrorsContain(code);
                    response(e);
                }
            );
}
