using System.Text;
using QuickER.AI.Chat.Resources;
using QuickER.Mcp.Tools;
using QuickER.Model;

namespace QuickER.AI.Chat;

/// <summary>
/// 名前付きクエリツールの構造化結果（<see cref="QueryToolOutcome"/>）を、UI カルチャに追従する
/// テキストへ整形する内蔵チャット面のフォーマッタ。
/// </summary>
/// <remarks>
/// <para>
/// MCP 面の英語専用フォーマッタ（<see cref="QueryToolEnglishFormatter"/>）と対を成す。文言は AI.Chat の
/// resx（中立＝英語 / <c>ja</c> サテライト＝日本語）から解決するため、アプリの表示言語に追従する。
/// 構造（インデント・戻り形／実装方式の小文字トークン）は言語非依存のためコード側にリテラルで残す。
/// </para>
/// <para>
/// DSL パーサ・生 SQL アナライザの診断（<see cref="QueryToolDiagnostic.Detail"/>）は既にローカライズ済みの
/// 文字列なので、そのまま「データとして」埋め込む。
/// </para>
/// </remarks>
public static class QueryToolLocalizedFormatter
{
    /// <summary>set_query の結果をローカライズテキスト化する</summary>
    public static string FormatSetQuery(QueryToolOutcome outcome)
    {
        switch (outcome.Status)
        {
            case QueryToolStatus.Success:
                var result = new StringBuilder();
                var template = outcome.WasUpdate
                    ? Strings.QueryTool_SetSuccessUpdated
                    : Strings.QueryTool_SetSuccessAdded;
                result.Append(
                    string.Format(
                        template,
                        outcome.QueryName,
                        outcome.TableName,
                        Lower(outcome.Returns!.Value),
                        Lower(outcome.Implementation!.Value)
                    )
                );

                foreach (var warning in outcome.Warnings)
                {
                    result.Append(
                        "\n  " + string.Format(Strings.QueryTool_Warning, Describe(warning))
                    );
                }

                return result.ToString();

            case QueryToolStatus.ValidationFailed:
                var sb = new StringBuilder();
                sb.AppendLine(
                    string.Format(Strings.QueryTool_SetValidationFailed, outcome.QueryName)
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

    /// <summary>remove_query の結果をローカライズテキスト化する</summary>
    public static string FormatRemoveQuery(QueryToolOutcome outcome)
    {
        return outcome.Status switch
        {
            QueryToolStatus.Success => string.Format(
                Strings.QueryTool_RemoveSuccess,
                outcome.QueryName,
                outcome.TableName
            ),
            QueryToolStatus.QueryNotFound => string.Format(
                Strings.QueryTool_QueryNotFound,
                outcome.QueryName,
                outcome.TableName
            ),
            _ => FormatCommonFailure(outcome),
        };
    }

    /// <summary>list_queries の一覧本体をローカライズテキスト化する</summary>
    public static string FormatListing(QueryListing listing)
    {
        var sb = new StringBuilder();
        sb.AppendLine(string.Format(Strings.QueryTool_ListHeader, listing.TotalCount));

        foreach (var group in listing.Groups)
        {
            sb.AppendLine();
            sb.AppendLine($"[{group.TableName ?? Strings.QueryTool_UnknownEntity}]");

            foreach (var query in group.Queries)
            {
                AppendQuerySummary(sb, query);
            }
        }

        return sb.ToString().TrimEnd();
    }

    /// <summary>複数ツール共通の失敗（引数欠落・戻り形／実装方式不正・テーブル不在）をローカライズテキスト化する</summary>
    private static string FormatCommonFailure(QueryToolOutcome outcome)
    {
        return outcome.Status switch
        {
            QueryToolStatus.MissingArgument => Strings.QueryTool_MissingArgument,
            QueryToolStatus.InvalidReturns => Strings.QueryTool_InvalidReturns,
            QueryToolStatus.InvalidImplementation => Strings.QueryTool_InvalidImplementation,
            QueryToolStatus.TableNotFound => string.Format(
                Strings.QueryTool_TableNotFound,
                outcome.TableName
            ),
            _ => string.Format(Strings.QueryTool_UnexpectedStatus, outcome.Status),
        };
    }

    /// <summary>診断 1 件（エラー／警告共通）をローカライズメッセージへ整形する</summary>
    private static string Describe(QueryToolDiagnostic d)
    {
        return d.Code switch
        {
            QueryToolDiagnosticCode.ParameterMissingName =>
                Strings.QueryTool_Diag_ParameterMissingName,
            QueryToolDiagnosticCode.ParameterTypeSourceExclusive => string.Format(
                Strings.QueryTool_Diag_ParameterTypeSourceExclusive,
                d.Name
            ),
            QueryToolDiagnosticCode.ParameterSourceColumnNotFound => string.Format(
                Strings.QueryTool_Diag_ParameterSourceColumnNotFound,
                d.Name,
                d.Column,
                d.Table
            ),
            QueryToolDiagnosticCode.ScalarRequiresScalarType =>
                Strings.QueryTool_Diag_ScalarRequiresScalarType,
            QueryToolDiagnosticCode.ProjectionRequiresResultTypeName =>
                Strings.QueryTool_Diag_ProjectionRequiresResultTypeName,
            QueryToolDiagnosticCode.ProjectionRequiresFields =>
                Strings.QueryTool_Diag_ProjectionRequiresFields,
            QueryToolDiagnosticCode.OrderByInvalidForReturnShape => string.Format(
                Strings.QueryTool_Diag_OrderByInvalidForReturnShape,
                d.Detail
            ),
            QueryToolDiagnosticCode.OrderByMissingColumn =>
                Strings.QueryTool_Diag_OrderByMissingColumn,
            QueryToolDiagnosticCode.OrderByColumnNotFound => string.Format(
                Strings.QueryTool_Diag_OrderByColumnNotFound,
                d.Column,
                d.Table
            ),
            QueryToolDiagnosticCode.ProjectionFieldMissingName =>
                Strings.QueryTool_Diag_ProjectionFieldMissingName,
            QueryToolDiagnosticCode.ProjectionFieldTypeSourceExclusive => string.Format(
                Strings.QueryTool_Diag_ProjectionFieldTypeSourceExclusive,
                d.Name
            ),
            QueryToolDiagnosticCode.ProjectionFieldSourceColumnNotFound => string.Format(
                Strings.QueryTool_Diag_ProjectionFieldSourceColumnNotFound,
                d.Name,
                d.Column,
                d.Table
            ),
            QueryToolDiagnosticCode.UnknownSqlDialect => string.Format(
                Strings.QueryTool_Diag_UnknownSqlDialect,
                d.Dialect,
                string.Join(", ", QueryToolCore.SupportedDbms)
            ),
            QueryToolDiagnosticCode.SqlDialectNotString => string.Format(
                Strings.QueryTool_Diag_SqlDialectNotString,
                d.Dialect
            ),
            QueryToolDiagnosticCode.ConditionDiagnostic => string.Format(
                Strings.QueryTool_Diag_Condition,
                d.Detail
            ),
            QueryToolDiagnosticCode.RawSqlDiagnostic => string.Format(
                Strings.QueryTool_Diag_RawSql,
                d.Dialect,
                d.Detail
            ),
            QueryToolDiagnosticCode.ParameterUnusedInCondition => string.Format(
                Strings.QueryTool_Diag_ParameterUnusedInCondition,
                d.Name
            ),
            _ => string.Format(Strings.QueryTool_Diag_Unexpected, d.Code),
        };
    }

    /// <summary>クエリ 1 件の要約（戻り形・実装方式・条件／SQL・パラメータ等）をテキスト化する</summary>
    private static void AppendQuerySummary(StringBuilder sb, QueryListingItem query)
    {
        sb.AppendLine($"  - {query.Name}: {Lower(query.Returns)}, {Lower(query.Implementation)}");

        if (query.Description is not null)
        {
            sb.AppendLine(
                $"      {string.Format(Strings.QueryTool_ListDescription, query.Description)}"
            );
        }

        if (query.Returns == QueryReturnShape.Scalar && query.ScalarType is not null)
        {
            sb.AppendLine(
                $"      {string.Format(Strings.QueryTool_ListScalarType, query.ScalarType)}"
            );
        }

        if (query.Implementation == QueryImplementationKind.Dsl && query.Condition is not null)
        {
            sb.AppendLine(
                $"      {string.Format(Strings.QueryTool_ListCondition, query.Condition)}"
            );
        }
        else if (query.Implementation == QueryImplementationKind.Sql && query.SqlDialects.Count > 0)
        {
            sb.AppendLine(
                $"      {string.Format(Strings.QueryTool_ListSqlDialects, string.Join(", ", query.SqlDialects))}"
            );
        }

        if (query.Parameters.Count > 0)
        {
            var parts = query.Parameters.Select(DescribeParameter);
            sb.AppendLine(
                $"      {string.Format(Strings.QueryTool_ListParameters, string.Join(", ", parts))}"
            );
        }

        if (query.OrderBy.Count > 0)
        {
            var parts = query.OrderBy.Select(DescribeOrdering);
            sb.AppendLine(
                $"      {string.Format(Strings.QueryTool_ListOrderBy, string.Join(", ", parts))}"
            );
        }

        if (query.HasPaging)
        {
            sb.AppendLine($"      {Strings.QueryTool_ListPaging}");
        }

        if (query.Returns == QueryReturnShape.Projection)
        {
            sb.AppendLine(
                $"      {string.Format(Strings.QueryTool_ListResultType, query.ResultTypeName)}"
            );

            if (query.Fields.Count > 0)
            {
                var parts = query.Fields.Select(DescribeField);
                sb.AppendLine(
                    $"      {string.Format(Strings.QueryTool_ListFields, string.Join(", ", parts))}"
                );
            }
        }
    }

    /// <summary>パラメータ 1 件を「名前: 型（または列参照）［(list)］」に整形する</summary>
    private static string DescribeParameter(QueryListingParameter parameter)
    {
        var typeText = parameter.IsColumnReference
            ? string.Format(
                Strings.QueryTool_ColumnRef,
                parameter.ColumnName ?? Strings.QueryTool_Unknown
            )
            : parameter.Type ?? Strings.QueryTool_Untyped;
        var list = parameter.IsList ? Strings.QueryTool_ListSuffix : string.Empty;

        return $"{parameter.Name}: {typeText}{list}";
    }

    /// <summary>並び順 1 件を「列名 [desc]」に整形する</summary>
    private static string DescribeOrdering(QueryListingOrder ordering)
    {
        var name = ordering.ColumnName ?? Strings.QueryTool_Unknown;

        return ordering.Descending ? string.Format(Strings.QueryTool_OrderDesc, name) : name;
    }

    /// <summary>射影フィールド 1 件を「名前: 型（または列参照）」に整形する</summary>
    private static string DescribeField(QueryListingField field)
    {
        var typeText = field.IsColumnReference
            ? string.Format(
                Strings.QueryTool_ColumnRef,
                field.ColumnName ?? Strings.QueryTool_Unknown
            )
            : field.Type ?? Strings.QueryTool_Untyped;

        return $"{field.Name}: {typeText}";
    }

    /// <summary>列挙値を小文字表記へ（戻り形・実装方式の表示・応答用。言語非依存トークン）</summary>
    private static string Lower<TEnum>(TEnum value)
        where TEnum : struct, Enum => value.ToString().ToLowerInvariant();
}
