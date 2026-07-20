using System.Linq;
using QuickER.Model;

namespace QuickER.Provider;

/// <summary>
/// 同期スクリプト生成で方言をまたいで共通する小さなヘルパー群。
/// </summary>
/// <remarks>
/// 参照先列の解決・NULL 許容句・参照アクション句は方言差が無いため、各方言へ重複させずここへ集約する。
/// </remarks>
public static class SyncScriptBuilderHelper
{
    /// <summary>外部キーの参照先列を差分情報から解決する</summary>
    /// <remarks>明示指定された列を優先し、無ければ親テーブルの主キー先頭列にフォールバックする</remarks>
    public static Column? ResolveReferencedColumn(SchemaDiffItem item)
    {
        if (item.Relationship?.SourceColumnId is not null)
        {
            var byId = item.ParentEntity?.Columns.FirstOrDefault(c =>
                c.Id == item.Relationship.SourceColumnId
            );

            if (byId is not null)
            {
                return byId;
            }
        }

        return item.ParentEntity?.Columns.FirstOrDefault(c => c.IsPrimaryKey);
    }

    /// <summary>NULL 許容句を返す（主キーまたは非 NULL 許容なら NOT NULL）</summary>
    public static string GetNullabilityClause(Column column) =>
        column.IsPrimaryKey || !column.IsNullable ? "NOT NULL" : "NULL";

    /// <summary>外部キーの ON DELETE / ON UPDATE 参照アクション句を生成する</summary>
    public static string BuildReferentialActionClause(Relationship? relationship) =>
        relationship is null
            ? string.Empty
            : ForeignKeyReferentialActionHelper.BuildReferentialActionClause(
                relationship.OnDelete,
                relationship.OnUpdate
            );
}
