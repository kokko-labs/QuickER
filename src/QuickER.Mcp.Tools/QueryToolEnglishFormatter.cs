using System.Text;
using QuickER.Model;

namespace QuickER.Mcp.Tools;

/// <summary>
/// 名前付きクエリツールの構造化結果（<see cref="QueryToolOutcome"/>）を英語テキストへ整形する MCP 面フォーマッタ。
/// </summary>
/// <remarks>
/// 外部 AI エージェント向け MCP サーバの応答は中立言語（英語）が正本。次ステージで内蔵チャット用の日本語版
/// フォーマッタを対で用意する。本クラスの出力は分離前の <c>DocumentErDiagramToolHost.Queries</c> と
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
                sb.AppendLine(
                    $"Cannot set query '{outcome.QueryName}': validation failed. The file was not modified."
                );

                foreach (var error in outcome.Errors)
                {
                    sb.AppendLine($"  - {Describe(error)}");
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
        sb.AppendLine($"Queries: {listing.TotalCount}");

        foreach (var group in listing.Groups)
        {
            sb.AppendLine();
            sb.AppendLine($"[{group.TableName ?? "(unknown entity)"}]");

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
                $"Unknown SQL dialect '{d.Dialect}' (must be one of: {string.Join(", ", QueryToolCore.SupportedDbms)}).",
            QueryToolDiagnosticCode.SqlDialectNotString =>
                $"SQL for dialect '{d.Dialect}' must be a string.",
            QueryToolDiagnosticCode.ConditionDiagnostic => $"Condition: {d.Detail}",
            QueryToolDiagnosticCode.RawSqlDiagnostic => $"{d.Dialect}: {d.Detail}",
            QueryToolDiagnosticCode.ParameterUnusedInCondition =>
                $"Parameter '{d.Name}' is declared but not used in the condition.",
            _ => $"Unexpected diagnostic: {d.Code}",
        };
    }

    /// <summary>クエリ 1 件の要約（戻り形・実装方式・条件／SQL・パラメータ等）をテキスト化する</summary>
    private static void AppendQuerySummary(StringBuilder sb, QueryListingItem query)
    {
        sb.AppendLine($"  - {query.Name}: {Lower(query.Returns)}, {Lower(query.Implementation)}");

        if (query.Description is not null)
        {
            sb.AppendLine($"      description: {query.Description}");
        }

        if (query.Returns == QueryReturnShape.Scalar && query.ScalarType is not null)
        {
            sb.AppendLine($"      scalar type: {query.ScalarType}");
        }

        if (query.Implementation == QueryImplementationKind.Dsl && query.Condition is not null)
        {
            sb.AppendLine($"      condition: {query.Condition}");
        }
        else if (query.Implementation == QueryImplementationKind.Sql && query.SqlDialects.Count > 0)
        {
            sb.AppendLine($"      sql dialects: {string.Join(", ", query.SqlDialects)}");
        }

        if (query.Parameters.Count > 0)
        {
            var parts = query.Parameters.Select(DescribeParameter);
            sb.AppendLine($"      parameters: {string.Join(", ", parts)}");
        }

        if (query.OrderBy.Count > 0)
        {
            var parts = query.OrderBy.Select(DescribeOrdering);
            sb.AppendLine($"      order by: {string.Join(", ", parts)}");
        }

        if (query.HasPaging)
        {
            sb.AppendLine("      paging: yes");
        }

        if (query.Returns == QueryReturnShape.Projection)
        {
            sb.AppendLine($"      result type: {query.ResultTypeName}");

            if (query.Fields.Count > 0)
            {
                var parts = query.Fields.Select(DescribeField);
                sb.AppendLine($"      fields: {string.Join(", ", parts)}");
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
