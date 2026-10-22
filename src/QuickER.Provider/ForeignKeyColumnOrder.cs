using System;
using System.Collections.Generic;
using System.Linq;
using QuickER.Model;

namespace QuickER.Provider;

/// <summary>
/// 複合外部キーの列ペアを、参照先のキー（主キー、無ければ一意制約）の列の並びへ並べ替える
/// （<see cref="QuickER.Dialects.UnsupportedDefinitionKind.CompositeForeignKeyColumnOrder"/>）。
/// </summary>
/// <remarks>
/// <para>
/// 列ペアは「親の列 → 子の列」の対応の集合で、並びは意味を持たない。ところが SQL Server（Msg 1776）と MySQL は、
/// 参照する列の並びが参照先のキーの並びと一致しないと外部キーを作れない。そこで、扱いの表の DDL のマスが
/// 「並べ替え」の方言では、DDL を出すときだけ列ペアを参照先のキーの並びへ並べ替える（図は書き換えない）。
/// 差分は 5 方言とも列ペアを並びを見ずに比べるので（<see cref="SchemaDiffService"/>）、並べ替えて作った外部キーを
/// 取り込み直しても差分は出ない。
/// </para>
/// <para>
/// 同期は、扱いの表の同期のマスが「並べ替え」の方言で、計画（<see cref="SyncPlanner"/>）が外部キーの追加
/// （明示の追加・作り直し・暗黙の付け直し）を参照先の同期後のキー（主キーの変更を選べば新しい主キー）の並びへ
/// 並べ替える（<see cref="ArrangeToKey"/>）。
/// </para>
/// <para>
/// 参照先の列の集合が主キーとも一意制約ともちょうど一致しないときは、並べ替えない（キーが決まらないため）。
/// </para>
/// </remarks>
public static class ForeignKeyColumnOrder
{
    /// <summary>列ペアを参照先のキーの並びへ並べ替える（キーが決まらなければそのまま返す）</summary>
    /// <param name="pairs">宣言順の列ペア</param>
    /// <param name="parent">参照先（親）の表</param>
    /// <returns>参照先のキーの並びに並べ替えた列ペア</returns>
    public static List<ForeignKeyColumnNamePair> ArrangeToReferencedKey(
        IReadOnlyList<ForeignKeyColumnNamePair> pairs,
        Entity parent
    ) => ArrangeToKey(pairs, CandidateKeys(parent));

    /// <summary>
    /// 列ペアを、渡したキーのうち親の列の集合がちょうど一致する最初のキーの並びへ並べ替える
    /// （一致するキーが無い・列ペアが 1 組以下なら、そのまま返す）
    /// </summary>
    /// <param name="pairs">宣言順の列ペア</param>
    /// <param name="keys">参照先のキーの列名（優先する順。主キー、続いて一意制約）</param>
    /// <returns>キーの並びに並べ替えた列ペア</returns>
    public static List<ForeignKeyColumnNamePair> ArrangeToKey(
        IReadOnlyList<ForeignKeyColumnNamePair> pairs,
        IEnumerable<IReadOnlyList<string>> keys
    )
    {
        if (pairs.Count < 2)
        {
            return [.. pairs];
        }

        var signature = UniqueConstraintNaming.ColumnSetSignature(
            pairs.Select(p => p.ParentColumn)
        );

        foreach (var key in keys)
        {
            if (
                key.Count != pairs.Count
                || !string.Equals(
                    UniqueConstraintNaming.ColumnSetSignature(key),
                    signature,
                    StringComparison.Ordinal
                )
            )
            {
                continue;
            }

            var arranged = new List<ForeignKeyColumnNamePair>(pairs.Count);
            var remaining = pairs.ToList();

            foreach (var column in key)
            {
                var pair = remaining.FirstOrDefault(p =>
                    SchemaNames.AreSame(p.ParentColumn, column)
                );

                if (pair is null)
                {
                    return [.. pairs];
                }

                remaining.Remove(pair);
                arranged.Add(pair);
            }

            return arranged;
        }

        return [.. pairs];
    }

    /// <summary>参照先の表のキー（主キーの実効順、続いて一意制約の宣言順）</summary>
    private static IEnumerable<IReadOnlyList<string>> CandidateKeys(Entity parent)
    {
        var primaryKey = parent.GetPrimaryKeyColumnsInOrder().Select(c => c.Name).ToList();

        if (primaryKey.Count > 0)
        {
            yield return primaryKey;
        }

        foreach (var unique in parent.UniqueConstraints)
        {
            if (UniqueConstraintNaming.TryResolveColumnNames(parent, unique, out var columns))
            {
                yield return columns;
            }
        }
    }
}
