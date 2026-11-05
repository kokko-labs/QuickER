using QuickER.Mcp.Tools;
using ChatStrings = QuickER.AI.Chat.Resources.Strings;

namespace QuickER.Tests.Mcp.Tools;

internal static partial class ToolScenarioRows
{
    /// <summary>set_query が警告つきで保存する（使われない入力・方式に合わない入力）</summary>
    private static IEnumerable<ToolRow> QueryWarningRows()
    {
        // 警告のパラメータ名は応答の構造にだけあるので Core.Check で見る
        yield return Row(
                "query-set-dsl-unused-parameter-warns-structured-sqlite",
                "SetQuery: DSL の宣言済み未使用パラメータは警告付きで成功する"
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
                        new { name = "customerId", type = "int32" },
                        new { name = "unused", type = "int32" },
                    },
                },
                e =>
                    e.Ok()
                        .Wrote()
                        .Core.Warnings(QueryToolDiagnosticCode.ParameterUnusedInCondition)
                        .Core.Check(
                            "警告のパラメータ名は unused",
                            outcome =>
                                outcome.Warnings.Single().Name == "unused"
                                    ? null
                                    : $"名前が {outcome.Warnings.Single().Name}"
                        )
                        .Schema.Kind("qdef", "qdef orders.GetByCustomer *")
            );

        yield return Row(
                "query-set-dsl-unused-parameter-warns-sqlite",
                "set_query: DSL の宣言済み未使用パラメータは警告付きで保存継続"
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
                    condition = "customer_id = @customerId",
                    parameters = new[]
                    {
                        new { name = "customerId", type = "int32" },
                        new { name = "unused", type = "int32" },
                    },
                },
                e =>
                    e.Ok()
                        .Wrote()
                        .Result.Contains("unused")
                        .ResultContainsByHost(
                            gui: string.Format(
                                ChatStrings.QueryTool_Warning,
                                string.Format(
                                    ChatStrings.QueryTool_Diag_ParameterUnusedInCondition,
                                    "unused"
                                )
                            ),
                            file: "Warning"
                        )
                        .Schema.Kind("qdef", "qdef orders.X *")
            );

        // 警告の方言と描画前の文は応答の構造にだけあるので Core.Check で見る
        yield return Row(
                "query-set-raw-sql-unused-parameter-warns-structured-sqlite",
                "SetQuery: 生 SQL の未使用パラメータは方言付き警告で成功する"
            )
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
                    parameters = new[] { new { name = "unused", type = "int32" } },
                },
                e =>
                    e.Ok()
                        .Wrote()
                        .Core.Warnings(QueryToolDiagnosticCode.RawSqlDiagnostic)
                        .Core.Check(
                            "警告は方言 sqlite・描画前の文があり英語で描ける",
                            outcome =>
                                outcome.Warnings.Single()
                                    is { Dialect: "sqlite", DetailText: { } text }
                                && !string.IsNullOrWhiteSpace(text.FormatEnglish())
                                    ? null
                                    : $"警告が {outcome.Warnings.Single()}"
                        )
                        .Schema.Kind("qdef", "qdef orders.ListSql *")
            );

        yield return Row(
                "query-set-raw-sql-unused-parameter-warns-sqlite",
                "set_query: 生 SQL の未使用パラメータは警告付きで保存継続"
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
                    implementation = "sql",
                    sql = new Dictionary<string, string> { ["sqlite"] = "SELECT * FROM orders" },
                    parameters = new[] { new { name = "unused", type = "int32" } },
                },
                e =>
                    e.Ok()
                        .Wrote()
                        .ResultContainsByHost(
                            gui: ChatStrings.QueryTool_Warning.Replace("{0}", string.Empty).Trim(),
                            file: "Warning"
                        )
                        .Schema.Kind("qdef", "qdef orders.X *")
            );

        // 警告の Detail（渡した方式）は応答の構造にだけあるので Core.Check で見る
        foreach (var implementation in new[] { "dsl", "manual" })
        {
            yield return Row(
                    $"query-set-sql-for-other-implementation-is-dropped-with-warning-{implementation}-sqlite",
                    "SetQuery: sql 方式以外で渡した SQL は保存せず警告する"
                )
                .Dialect("sqlite")
                .Base(Orders)
                .Call(
                    QueryToolCore.SetQueryToolName,
                    new
                    {
                        table_name = "orders",
                        query_name = "ListAll",
                        returns = "list",
                        implementation,
                        sql = new Dictionary<string, string>
                        {
                            ["sqlite"] = "SELECT * FROM orders",
                        },
                    },
                    e =>
                        e.Ok()
                            .Wrote()
                            .Core.Warnings(QueryToolDiagnosticCode.SqlIgnoredForImplementation)
                            .Core.Check(
                                $"警告の Detail は {implementation}",
                                outcome =>
                                    outcome.Warnings.Single().Detail == implementation
                                        ? null
                                        : $"Detail が {outcome.Warnings.Single().Detail}"
                            )
                            .Schema.Kind("qdef", "qdef orders.ListAll * sql=() *")
                );
        }

        foreach (var implementation in new[] { "sql", "manual" })
        {
            yield return Row(
                    $"query-set-condition-for-other-implementation-warns-{implementation}-sqlite",
                    "SetQuery: dsl 方式以外で渡した condition は警告する（保存しないのは従来どおり）"
                )
                .Dialect("sqlite")
                .Base(Orders)
                .Call(
                    QueryToolCore.SetQueryToolName,
                    new
                    {
                        table_name = "orders",
                        query_name = "ListAll",
                        returns = "list",
                        implementation,
                        condition = "customer_id = 1",
                        sql = new Dictionary<string, string>
                        {
                            ["sqlite"] = "SELECT * FROM orders",
                        },
                    },
                    e =>
                        e.Ok()
                            .Wrote()
                            .CoreWarningsContain(
                                QueryToolDiagnosticCode.ConditionIgnoredForImplementation
                            )
                            .Core.Check(
                                $"condition の警告の Detail は {implementation}",
                                outcome =>
                                    outcome
                                        .Warnings.Single(warning =>
                                            warning.Code
                                            == QueryToolDiagnosticCode.ConditionIgnoredForImplementation
                                        )
                                        .Detail == implementation
                                        ? null
                                        : "Detail が違う"
                            )
                            .Schema.Kind("qdef", "qdef orders.ListAll * cond=- *")
                );
        }

        foreach (
            var (shape, sql) in new (string Shape, string? Sql)[]
            {
                ("missing", null),
                ("blank", "   "),
            }
        )
        {
            object args = sql is null
                ? new
                {
                    table_name = "orders",
                    query_name = "ListAll",
                    returns = "list",
                    implementation = "sql",
                }
                : new
                {
                    table_name = "orders",
                    query_name = "ListAll",
                    returns = "list",
                    implementation = "sql",
                    sql = new Dictionary<string, string> { ["sqlite"] = sql },
                };

            yield return Row(
                    $"query-set-sql-implementation-without-sql-warns-{shape}-sqlite",
                    "SetQuery: sql 方式で中身のある SQL が無ければ全方言が手動実装になる旨を警告する"
                )
                .Dialect("sqlite")
                .Base(Orders)
                .Call(
                    QueryToolCore.SetQueryToolName,
                    args,
                    e =>
                        e.Ok()
                            .Wrote()
                            .CoreWarningsContain(
                                QueryToolDiagnosticCode.SqlImplementationWithoutSql
                            )
                );
        }

        yield return Row(
                "query-set-blank-sql-is-not-saved-sqlite",
                "SetQuery: 空白だけの SQL は保存せず、中身のある方言の SQL だけを保存する"
            )
            .Dialect("sqlite")
            .Base(Orders)
            .Call(
                QueryToolCore.SetQueryToolName,
                new
                {
                    table_name = "orders",
                    query_name = "ListAll",
                    returns = "list",
                    implementation = "sql",
                    sql = new Dictionary<string, string>
                    {
                        ["sqlite"] = "  \r\n ",
                        ["sqlserver"] = "SELECT 1",
                    },
                },
                e =>
                    e.Ok()
                        .Wrote()
                        .Schema.Kind("qdef", "qdef orders.ListAll * sql=(sqlserver=\"SELECT 1\") *")
            );

        yield return Row(
                "query-set-matching-inputs-no-implementation-warnings-sqlite",
                "SetQuery: 方式に合った入力では実装方式の警告を出さず SQL を保存する"
            )
            .Dialect("sqlite")
            .Base(Orders)
            .Call(
                QueryToolCore.SetQueryToolName,
                new
                {
                    table_name = "orders",
                    query_name = "ListAll",
                    returns = "list",
                    implementation = "sql",
                    sql = new Dictionary<string, string> { ["sqlite"] = "SELECT * FROM orders" },
                },
                e =>
                    e.Ok()
                        .Wrote()
                        .Core.Warnings()
                        .Schema.Kind("qdef", "qdef orders.ListAll * sql=(sqlite=\"*\") *")
            );
    }
}
