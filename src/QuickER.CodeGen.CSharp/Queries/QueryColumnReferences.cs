using QuickER.Model;

namespace QuickER.CodeGen.CSharp.Queries;

/// <summary>名前付きクエリが列を参照している箇所の種類</summary>
public enum QueryColumnUsageKind
{
    /// <summary>パラメータの型の参照元（<see cref="QueryParameter.SourceColumnId"/>）</summary>
    Parameter,

    /// <summary>並び順（<see cref="QueryOrdering.ColumnId"/>）</summary>
    SortOrder,

    /// <summary>射影フィールドの参照元（<see cref="ProjectionField.SourceColumnId"/>）</summary>
    Field,

    /// <summary>簡易 DSL の条件の中の列名（<see cref="QueryDefinition.Condition"/>）</summary>
    Condition,
}

/// <summary>名前付きクエリが列を参照している箇所 1 つ</summary>
/// <param name="Kind">参照の種類</param>
/// <param name="Name">パラメータ名・フィールド名（並び順と条件は null）</param>
public sealed record QueryColumnUsage(QueryColumnUsageKind Kind, string? Name);

/// <summary>列を参照している名前付きクエリ 1 本と、その参照の箇所</summary>
/// <param name="Query">クエリ</param>
/// <param name="Usages">参照の箇所（パラメータ・並び順・射影フィールド・条件の順。並び順と条件は多くとも 1 つ）</param>
public sealed record QueryColumnReference(
    QueryDefinition Query,
    IReadOnlyList<QueryColumnUsage> Usages
);

/// <summary>
/// 名前付きクエリの中の列の参照を探す（列の削除で参照が切れるクエリを知らせる・列名の変更で条件を追従させる）
/// </summary>
/// <remarks>
/// <para>
/// 列の削除はクエリを書き換えない。パラメータ・射影フィールドを消すとメソッドの引数・DTO の形が変わり、条件から列を外すと
/// 条件の意味が変わるため（名前の変更の追従が「条件式で読める名前のときだけ書き換える」のと同じ考え方）。代わりに、
/// 切れる参照をツールの応答（<see cref="DescribeRemovedColumn"/>）と GUI のステータスバーで名指しする。列が Undo で
/// 戻れば、Id も名前も同じなので参照はそのまま生き返る。
/// </para>
/// <para>
/// 条件の中の列名の読み方（<see cref="FindInCondition"/>）は名前の変更の追従（<see cref="QueryConditionRenamer"/>）と
/// 共有する。写しを作ると、追従が書き換える参照と削除で名指しする参照が食い違う。
/// </para>
/// </remarks>
public static class QueryColumnReferences
{
    /// <summary>
    /// 条件式（簡易 DSL）の中で、指定した列名を指す列参照（名前の同一性は <see cref="SchemaNames.AreSame"/>）を返す。
    /// パース不能な条件は列参照の位置が信用できないので null を返す
    /// </summary>
    /// <param name="conditionText">簡易 DSL の条件式</param>
    /// <param name="columnName">列名</param>
    /// <remarks>パラメータ（<c>@名前</c>）と文字列リテラルの中の同じ綴りは列参照ではないので含まない</remarks>
    public static IReadOnlyList<ColumnReference>? FindInCondition(
        string conditionText,
        string columnName
    )
    {
        ArgumentNullException.ThrowIfNull(conditionText);
        ArgumentNullException.ThrowIfNull(columnName);

        var parsed = QueryConditionParser.Parse(conditionText);

        if (parsed.Root is null)
        {
            return null;
        }

        return parsed
            .ColumnReferences.Where(reference => SchemaNames.AreSame(reference.Text, columnName))
            .ToList();
    }

    /// <summary>テーブルの名前付きクエリのうち、指定した列を参照しているものを、参照の箇所つきで返す</summary>
    /// <param name="queries">図の名前付きクエリ</param>
    /// <param name="entityId">列が属するテーブルの Id（ほかのテーブルのクエリは同じ名前の列があっても数えない）</param>
    /// <param name="columnId">列の Id（パラメータ・並び順・射影フィールドの参照を照合する）</param>
    /// <param name="columnName">列名（条件の中の列名を照合する）</param>
    /// <remarks>
    /// 条件は実装方式が簡易 DSL のときだけ数える（生 SQL・手動実装では条件を使わず、切れても検証も生成も止まらない）。
    /// パース不能な条件は数えない。
    /// </remarks>
    public static IReadOnlyList<QueryColumnReference> Find(
        IEnumerable<QueryDefinition> queries,
        Guid entityId,
        Guid columnId,
        string columnName
    )
    {
        ArgumentNullException.ThrowIfNull(queries);
        ArgumentNullException.ThrowIfNull(columnName);

        var found = new List<QueryColumnReference>();

        foreach (var query in queries.Where(query => query.EntityId == entityId))
        {
            var usages = new List<QueryColumnUsage>();

            usages.AddRange(
                query
                    .Parameters.Where(parameter => parameter.SourceColumnId == columnId)
                    .Select(parameter => new QueryColumnUsage(
                        QueryColumnUsageKind.Parameter,
                        parameter.Name
                    ))
            );

            if (query.OrderBy.Any(ordering => ordering.ColumnId == columnId))
            {
                usages.Add(new QueryColumnUsage(QueryColumnUsageKind.SortOrder, null));
            }

            usages.AddRange(
                query
                    .Fields.Where(field => field.SourceColumnId == columnId)
                    .Select(field => new QueryColumnUsage(QueryColumnUsageKind.Field, field.Name))
            );

            if (
                query.Implementation == QueryImplementationKind.Dsl
                && query.Condition is { } condition
                && FindInCondition(condition, columnName) is { Count: > 0 }
            )
            {
                usages.Add(new QueryColumnUsage(QueryColumnUsageKind.Condition, null));
            }

            if (usages.Count > 0)
            {
                found.Add(new QueryColumnReference(query, usages));
            }
        }

        return found;
    }

    /// <summary>
    /// 列を削除したツールの応答へ足す英語の注意文（参照しているクエリが無ければ null）。MCP のツールホストと内蔵チャットの
    /// ツールが同じ文を返す
    /// </summary>
    /// <param name="tableName">列を削除したテーブル名</param>
    /// <param name="columnName">削除した列名</param>
    /// <param name="references"><see cref="Find"/> の結果（列を削除する前に求めたもの）</param>
    public static string? DescribeRemovedColumn(
        string tableName,
        string columnName,
        IReadOnlyList<QueryColumnReference> references
    )
    {
        ArgumentNullException.ThrowIfNull(references);

        if (references.Count == 0)
        {
            return null;
        }

        var queries = string.Join(
            ", ",
            references.Select(reference =>
                $"{reference.Query.Name} ({string.Join(", ", reference.Usages.Select(DescribeUsage))})"
            )
        );

        return $"Named queries on table '{tableName}' still refer to the removed column '{columnName}': {queries}. Fix them with set_query or remove them with remove_query.";
    }

    /// <summary>参照の箇所 1 つの英語の表記</summary>
    private static string DescribeUsage(QueryColumnUsage usage) =>
        usage.Kind switch
        {
            QueryColumnUsageKind.Parameter => $"parameter '{usage.Name}'",
            QueryColumnUsageKind.SortOrder => "sort order",
            QueryColumnUsageKind.Field => $"field '{usage.Name}'",
            QueryColumnUsageKind.Condition => "condition",
            _ => throw new ArgumentOutOfRangeException(nameof(usage)),
        };
}
