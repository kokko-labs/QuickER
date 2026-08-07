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
    /// <summary>外部キーの構成列ペアを差分情報から取り出す（宣言順・空なら FK 句を作れない）</summary>
    /// <remarks>
    /// 列ペアは差分計算時に解決済みで <see cref="SchemaDiffItem.ForeignKeyColumnPairs"/> へ載っている。
    /// 推測フォールバック（親の主キー先頭列・命名規約による子列）は行わない。
    /// </remarks>
    public static IReadOnlyList<ForeignKeyColumnNamePair> ResolveColumnPairs(SchemaDiffItem item) =>
        item.ForeignKeyColumnPairs;

    /// <summary>NULL 許容句を返す（主キーまたは非 NULL 許容なら NOT NULL）</summary>
    public static string GetNullabilityClause(Column column) =>
        column.IsPrimaryKey || !column.IsNullable ? "NOT NULL" : "NULL";

    /// <summary>構成列を解決できない一意制約のスキップコメントを組み立てる（固定文は英語が正本）</summary>
    /// <remarks>
    /// 差分計算は構成列を解決できた一意制約しか出さないため通常は現れない防御。生成 SQL の決定性を保つため、
    /// 表示用の <see cref="SchemaDiffItem.Description"/>（UI 言語で変わる）は使わない。
    /// </remarks>
    public static string BuildUniqueConstraintSkipComment(SchemaDiffItem item) =>
        $"-- Skipped '{item.Kind}' on {item.TableName}: the unique constraint has no resolvable columns.";

    /// <summary>外部キーの ON DELETE / ON UPDATE 参照アクション句を生成する</summary>
    public static string BuildReferentialActionClause(Relationship? relationship) =>
        relationship is null
            ? string.Empty
            : ForeignKeyReferentialActionHelper.BuildReferentialActionClause(
                relationship.OnDelete,
                relationship.OnUpdate
            );
}
