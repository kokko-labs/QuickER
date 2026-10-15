using System.Text;
using QuickER.CodeGen.CSharp;
using QuickER.Dialects;
using QuickER.Model;

namespace QuickER.Mcp.Tools;

/// <summary>
/// 名前付きクエリツールの構造化結果（<see cref="QueryToolOutcome"/>）を英語テキストへ整形する MCP 面フォーマッタ。
/// </summary>
/// <remarks>
/// 外部 AI エージェント向け MCP サーバの応答は中立言語（英語）が正本＝ヘッドレス実行の応答は UI 言語に
/// 依らず英語で固定する（DSL パーサ・生 SQL アナライザ由来の診断も
/// <see cref="QuickER.CodeGen.CSharp.Queries.QueryDiagnosticText.FormatEnglish"/> で英語描画する）。
/// 内蔵チャット面は対の日本語版フォーマッタ（<c>QueryToolLocalizedFormatter</c>）が UI 言語追従で描画する。
/// 本クラスの出力は分離前の <c>DocumentErDiagramToolHost.Queries</c> と
/// バイト等価（既存テストが文言をアサートしている）。<c>list_queries</c> のファイル由来の前置き
/// （新フォーマット警告）はファイル IO 層の責務のため、ホスト側が本体へ前置する。
/// </remarks>
public static class QueryToolEnglishFormatter
{
    /// <summary>set_query の結果を英語テキスト化する</summary>
    public static string FormatSetQuery(QueryToolOutcome outcome)
    {
        switch (outcome.Status)
        {
            case QueryToolStatus.Success:
                var result = new StringBuilder();
                result.Append(
                    $"{(outcome.WasUpdate ? "Updated" : "Added")} query '{outcome.QueryName}' on table '{outcome.TableName}' (returns {Lower(outcome.Returns!.Value)}, {Lower(outcome.Implementation!.Value)})."
                );

                foreach (var warning in outcome.Warnings)
                {
                    result.Append($"\n  Warning: {Describe(warning)}");
                }

                return result.ToString();

            case QueryToolStatus.ValidationFailed:
                var sb = new StringBuilder();
                sb.Append(
                        $"Cannot set query '{outcome.QueryName}': validation failed. The file was not modified."
                    )
                    .Append('\n');

                foreach (var error in outcome.Errors)
                {
                    sb.Append($"  - {Describe(error)}").Append('\n');
                }

                return sb.ToString().TrimEnd();

            default:
                return FormatCommonFailure(outcome);
        }
    }

    /// <summary>remove_query の結果を英語テキスト化する</summary>
    public static string FormatRemoveQuery(QueryToolOutcome outcome)
    {
        return outcome.Status switch
        {
            QueryToolStatus.Success when outcome.RemovedOrphans == 1 =>
                $"Removed query '{outcome.QueryName}', which belonged to a table that is no longer in the diagram.",
            QueryToolStatus.Success when outcome.RemovedOrphans > 1 =>
                $"Removed {outcome.RemovedOrphans} queries named '{outcome.QueryName}', which belonged to tables that are no longer in the diagram.",
            QueryToolStatus.Success =>
                $"Removed query '{outcome.QueryName}' from table '{outcome.TableName}'.",
            QueryToolStatus.QueryNotFound =>
                $"Query '{outcome.QueryName}' not found on table '{outcome.TableName}'.",
            _ => FormatCommonFailure(outcome),
        };
    }

    /// <summary>list_queries の一覧本体を英語テキスト化する（新フォーマット警告はホストが前置する）</summary>
    public static string FormatListing(QueryListing listing)
    {
        var sb = new StringBuilder();
        sb.Append($"Queries: {listing.TotalCount}").Append('\n');

        foreach (var group in listing.Groups)
        {
            sb.Append('\n');
            sb.Append($"[{group.TableName ?? "(unknown entity)"}]").Append('\n');

            foreach (var query in group.Queries)
            {
                AppendQuerySummary(sb, query);
            }
        }

        return sb.ToString().TrimEnd();
    }

    /// <summary>複数ツール共通の失敗（引数欠落・戻り形／実装方式不正・テーブル不在）を英語テキスト化する</summary>
    private static string FormatCommonFailure(QueryToolOutcome outcome)
    {
        return outcome.Status switch
        {
            QueryToolStatus.MissingArgument => "table_name and query_name are required.",
            QueryToolStatus.InvalidReturns =>
                "Invalid or missing 'returns' (must be one of: list, single, count, scalar, projection).",
            QueryToolStatus.InvalidImplementation =>
                "Invalid 'implementation' (must be one of: dsl, sql, manual).",
            QueryToolStatus.TableNotFound => $"Table '{outcome.TableName}' not found.",
            _ => $"Unexpected query tool status: {outcome.Status}",
        };
    }

    /// <summary>診断 1 件（エラー／警告共通）を英語メッセージへ整形する</summary>
    private static string Describe(QueryToolDiagnostic d)
    {
        return d.Code switch
        {
            QueryToolDiagnosticCode.ParameterMissingName =>
                "A parameter is missing its required 'name'.",
            QueryToolDiagnosticCode.ParameterTypeSourceExclusive =>
                $"Parameter '{d.Name}' must specify exactly one of 'type' or 'source_column'.",
            QueryToolDiagnosticCode.ParameterSourceColumnNotFound =>
                $"Parameter '{d.Name}': source_column '{d.Column}' not found in table '{d.Table}'.",
            QueryToolDiagnosticCode.ScalarRequiresScalarType =>
                "returns=scalar requires 'scalar_type'.",
            QueryToolDiagnosticCode.ProjectionRequiresResultTypeName =>
                "returns=projection requires 'result_type_name'.",
            QueryToolDiagnosticCode.ProjectionRequiresFields =>
                "returns=projection requires at least one entry in 'fields'.",
            QueryToolDiagnosticCode.OrderByInvalidForReturnShape =>
                $"order_by is only valid when returns is 'list', 'single', or 'projection' (got '{d.Detail}').",
            QueryToolDiagnosticCode.OrderByMissingColumn =>
                "An order_by entry is missing its required 'column'.",
            QueryToolDiagnosticCode.OrderByColumnNotFound =>
                $"order_by column '{d.Column}' not found in table '{d.Table}'.",
            QueryToolDiagnosticCode.ProjectionFieldMissingName =>
                "A projection field is missing its required 'name'.",
            QueryToolDiagnosticCode.ProjectionFieldTypeSourceExclusive =>
                $"Projection field '{d.Name}' must specify exactly one of 'type' or 'source_column'.",
            QueryToolDiagnosticCode.ProjectionFieldSourceColumnNotFound =>
                $"Projection field '{d.Name}': source_column '{d.Column}' not found in table '{d.Table}'.",
            QueryToolDiagnosticCode.UnknownSqlDialect =>
                $"Unknown SQL dialect '{d.Dialect}' (must be one of: {string.Join(", ", DialectNames.All)}).",
            QueryToolDiagnosticCode.SqlDialectNotString =>
                $"SQL for dialect '{d.Dialect}' must be a string.",
            // DSL パーサ・生 SQL アナライザの診断は、UI 言語に依らず中立言語（英語）で描画する
            QueryToolDiagnosticCode.ConditionDiagnostic =>
                $"Condition: {d.DetailText?.FormatEnglish()}",
            QueryToolDiagnosticCode.RawSqlDiagnostic =>
                $"{d.Dialect}: {d.DetailText?.FormatEnglish()}",
            QueryToolDiagnosticCode.ParameterUnusedInCondition =>
                $"Parameter '{d.Name}' is declared but not used in the condition.",
            // 生成時診断と同じ文言（規則と対象のクエリ名・名前を含む）を中立言語（英語）で描画する
            QueryToolDiagnosticCode.GenerationRule => d.DetailText?.FormatEnglish() ?? string.Empty,
            QueryToolDiagnosticCode.SqlIgnoredForImplementation =>
                $"'sql' is used only when implementation is 'sql' (got '{d.Detail}'); the SQL was not saved.",
            QueryToolDiagnosticCode.ConditionIgnoredForImplementation =>
                $"'condition' is used only when implementation is 'dsl' (got '{d.Detail}'); the condition was not saved.",
            QueryToolDiagnosticCode.SqlImplementationWithoutSql =>
                "implementation is 'sql' but no SQL was given for any dialect, so the generated code leaves the method to be written by hand (partial class) for every dialect.",
            QueryToolDiagnosticCode.OrderByIgnoredForImplementation =>
                $"'order_by' is applied only when implementation is 'dsl' (got '{d.Detail}'); the ordering was saved, but the generated method does not use it.",
            QueryToolDiagnosticCode.SqlDialectNotImplemented =>
                $"The code generator implements raw SQL only for {string.Join(", ", CodeGenerationOptions.SupportedRepositoryDialects)}; the SQL for '{d.Dialect}' was saved, but for that dialect the method is generated as a contract to write by hand (partial class).",
            QueryToolDiagnosticCode.SqlDialectDuplicate =>
                $"'sql' gives dialect '{d.Detail}' more than once (keys that differ only in letter case, such as '{d.Dialect}'); give each dialect once.",
            _ => $"Unexpected diagnostic: {d.Code}",
        };
    }

    /// <summary>クエリ 1 件の要約（戻り形・実装方式・条件／SQL・パラメータ等）をテキスト化する</summary>
    private static void AppendQuerySummary(StringBuilder sb, QueryListingItem query)
    {
        sb.Append($"  - {query.Name}: {Lower(query.Returns)}, {Lower(query.Implementation)}")
            .Append('\n');

        if (query.Description is not null)
        {
            sb.Append($"      description: {query.Description}").Append('\n');
        }

        if (query.Returns == QueryReturnShape.Scalar && query.ScalarType is not null)
        {
            sb.Append($"      scalar type: {query.ScalarType}").Append('\n');
        }

        if (query.Implementation == QueryImplementationKind.Dsl && query.Condition is not null)
        {
            sb.Append($"      condition: {query.Condition}").Append('\n');
        }
        else if (query.Implementation == QueryImplementationKind.Sql && query.SqlDialects.Count > 0)
        {
            sb.Append($"      sql dialects: {string.Join(", ", query.SqlDialects)}").Append('\n');
        }

        if (query.Parameters.Count > 0)
        {
            var parts = query.Parameters.Select(DescribeParameter);
            sb.Append($"      parameters: {string.Join(", ", parts)}").Append('\n');
        }

        if (query.OrderBy.Count > 0)
        {
            var parts = query.OrderBy.Select(DescribeOrdering);
            sb.Append($"      order by: {string.Join(", ", parts)}").Append('\n');
        }

        if (query.HasPaging)
        {
            sb.Append("      paging: yes").Append('\n');
        }

        if (query.Returns == QueryReturnShape.Projection)
        {
            sb.Append($"      result type: {query.ResultTypeName}").Append('\n');

            if (query.Fields.Count > 0)
            {
                var parts = query.Fields.Select(DescribeField);
                sb.Append($"      fields: {string.Join(", ", parts)}").Append('\n');
            }
        }
    }

    /// <summary>パラメータ 1 件を「名前: 型（または列参照）［(list)］」に整形する</summary>
    private static string DescribeParameter(QueryListingParameter parameter)
    {
        var typeText = parameter.IsColumnReference
            ? $"column {parameter.ColumnName ?? "(unknown)"}"
            : parameter.Type ?? "(untyped)";
        var list = parameter.IsList ? " (list)" : string.Empty;

        return $"{parameter.Name}: {typeText}{list}";
    }

    /// <summary>並び順 1 件を「列名 [desc]」に整形する</summary>
    private static string DescribeOrdering(QueryListingOrder ordering)
    {
        var name = ordering.ColumnName ?? "(unknown)";

        return ordering.Descending ? $"{name} desc" : name;
    }

    /// <summary>射影フィールド 1 件を「名前: 型（または列参照）」に整形する</summary>
    private static string DescribeField(QueryListingField field)
    {
        var typeText = field.IsColumnReference
            ? $"column {field.ColumnName ?? "(unknown)"}"
            : field.Type ?? "(untyped)";

        return $"{field.Name}: {typeText}";
    }

    /// <summary>列挙値を小文字表記へ（戻り形・実装方式の表示・応答用）</summary>
    private static string Lower<TEnum>(TEnum value)
        where TEnum : struct, Enum => value.ToString().ToLowerInvariant();
}
