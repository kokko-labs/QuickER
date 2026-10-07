using System.Collections.Generic;

namespace QuickER.UndoRedo;

/// <summary>一覧から条件に合う要素を元の位置とともに取り除き、Undo で同じ位置へ戻す（削除コマンドが共有する）</summary>
/// <remarks>
/// 末尾へ追加し直すと、Undo のたびに一覧の順序が変わる（図のテーブル・リレーションの順序は保存内容・DDL・
/// 重なり順に現れる）。取り除く前の位置を記録しておき、昇順に挿し戻して元の順序へ戻す。
/// </remarks>
internal static class IndexedRemoval
{
    /// <summary>条件に合う要素を取り除き、元の位置とともに返す</summary>
    /// <typeparam name="T">要素の型</typeparam>
    /// <param name="list">対象の一覧</param>
    /// <param name="predicate">取り除く要素の条件</param>
    /// <returns>取り除いた要素と、取り除く前の位置（昇順）</returns>
    public static List<(int Index, T Item)> RemoveWhere<T>(IList<T> list, Func<T, bool> predicate)
    {
        var removed = new List<(int Index, T Item)>();

        for (var i = 0; i < list.Count; i++)
        {
            if (predicate(list[i]))
            {
                removed.Add((i, list[i]));
            }
        }

        // 後ろから取り除けば、記録した位置がずれない
        for (var i = removed.Count - 1; i >= 0; i--)
        {
            list.RemoveAt(removed[i].Index);
        }

        return removed;
    }

    /// <summary>取り除いた要素を元の位置へ戻す</summary>
    /// <typeparam name="T">要素の型</typeparam>
    /// <param name="list">対象の一覧</param>
    /// <param name="removed"><see cref="RemoveWhere{T}"/> が返した一覧</param>
    /// <remarks>
    /// 昇順に挿し戻すと、各要素は取り除く前の位置へ戻る。一覧は Undo の対象外の操作で差し替わることがあるので、
    /// 位置は一覧の長さで頭打ちにする
    /// </remarks>
    public static void Restore<T>(IList<T> list, IReadOnlyList<(int Index, T Item)> removed)
    {
        foreach (var (index, item) in removed)
        {
            list.Insert(Math.Min(index, list.Count), item);
        }
    }
}
