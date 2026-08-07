using System.Collections.Generic;
using System.Linq;
using QuickER.Model;

namespace QuickER.Provider;

/// <summary>
/// 外部キーの構成列ペア 1 組を、解決済みの列名で表す。
/// </summary>
/// <param name="ParentColumn">参照先（親・被参照）テーブル側の列名</param>
/// <param name="ChildColumn">外部キーを保有する（子）テーブル側の列名</param>
public sealed record ForeignKeyColumnNamePair(string ParentColumn, string ChildColumn);

/// <summary>
/// リレーションの列ペア（<see cref="Relationship.ColumnPairs"/>）を列名へ解決する共通ヘルパー。
/// </summary>
/// <remarks>
/// <para>
/// <b>列ペアが外部キー定義の唯一の正本</b>で、「親は先頭の主キー列」「子は <c>{親表}_{PK列}</c> の命名列」
/// といった推測フォールバックは一切行わない。ペアが 0 件（多対多・列未設定）、または 1 列でも
/// 列 ID を解決できない場合は <c>null</c> を返し、呼び出し側はその外部キーをスキップする。
/// </para>
/// <para>
/// DDL 生成・差分計算・同期計画が同じ規則で列を解決するよう、判定をここへ 1 本化する。
/// </para>
/// </remarks>
public static class ForeignKeyColumnPairResolver
{
    /// <summary>リレーションの列ペアを列名へ解決する</summary>
    /// <param name="relationship">対象のリレーション</param>
    /// <param name="parent">起点（親・被参照）エンティティ</param>
    /// <param name="child">終点（子・外部キー保有）エンティティ</param>
    /// <returns>宣言順の列名ペア一覧。ペア 0 件・解決不能な列を含む場合は <c>null</c></returns>
    public static List<ForeignKeyColumnNamePair>? Resolve(
        Relationship relationship,
        Entity parent,
        Entity child
    )
    {
        if (relationship.ColumnPairs.Count == 0)
        {
            return null;
        }

        var pairs = new List<ForeignKeyColumnNamePair>(relationship.ColumnPairs.Count);

        foreach (var pair in relationship.ColumnPairs)
        {
            var parentColumn = parent.Columns.FirstOrDefault(c => c.Id == pair.SourceColumnId);
            var childColumn = child.Columns.FirstOrDefault(c => c.Id == pair.TargetColumnId);

            if (parentColumn is null || childColumn is null)
            {
                return null;
            }

            pairs.Add(new ForeignKeyColumnNamePair(parentColumn.Name, childColumn.Name));
        }

        return pairs;
    }

    /// <summary>列名ペア一覧から親側の列名だけを宣言順で取り出す</summary>
    public static IEnumerable<string> ParentColumns(IEnumerable<ForeignKeyColumnNamePair> pairs) =>
        pairs.Select(p => p.ParentColumn);

    /// <summary>列名ペア一覧から子側の列名だけを宣言順で取り出す</summary>
    public static IEnumerable<string> ChildColumns(IEnumerable<ForeignKeyColumnNamePair> pairs) =>
        pairs.Select(p => p.ChildColumn);
}
