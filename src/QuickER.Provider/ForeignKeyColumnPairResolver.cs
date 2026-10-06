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

/// <summary>外部キーとして扱えるリレーションを、親子のテーブルと列名ペアまで解決したもの</summary>
/// <param name="Relationship">元のリレーション</param>
/// <param name="Parent">参照先（親）のテーブル</param>
/// <param name="Child">外部キーを持つ（子）テーブル</param>
/// <param name="ColumnPairs">構成列の名前のペア（宣言順）</param>
internal sealed record ResolvedForeignKeyRelationship(
    Relationship Relationship,
    Entity Parent,
    Entity Child,
    IReadOnlyList<ForeignKeyColumnNamePair> ColumnPairs
);

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
    /// <summary>外部キーとして扱えるリレーションを、親子と列名ペアまで解決して列挙する</summary>
    /// <param name="relationships">対象のリレーション（並び順を保つ）</param>
    /// <param name="entities">リレーションの親子を引くテーブル（同じ Id が複数あれば先のもの）</param>
    /// <returns>多対多と、親子・列ペアを解決できないリレーションを除いたもの</returns>
    /// <remarks>
    /// 差分計算と同期計画が、リレーションごとにテーブル一覧を線形に探し直さずに済むよう、Id の索引を 1 回だけ作る。
    /// </remarks>
    internal static List<ResolvedForeignKeyRelationship> ResolveAll(
        IEnumerable<Relationship> relationships,
        IEnumerable<Entity> entities
    )
    {
        var entitiesById = new Dictionary<Guid, Entity>();

        foreach (var entity in entities)
        {
            entitiesById.TryAdd(entity.Id, entity);
        }

        var resolved = new List<ResolvedForeignKeyRelationship>();

        foreach (var relationship in relationships)
        {
            if (
                relationship.Type == RelationshipType.ManyToMany
                || !entitiesById.TryGetValue(relationship.SourceEntityId, out var parent)
                || !entitiesById.TryGetValue(relationship.TargetEntityId, out var child)
                || Resolve(relationship, parent, child) is not { } columnPairs
            )
            {
                continue;
            }

            resolved.Add(
                new ResolvedForeignKeyRelationship(relationship, parent, child, columnPairs)
            );
        }

        return resolved;
    }

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
