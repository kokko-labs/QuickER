using System.Collections.ObjectModel;

namespace QuickER.ViewModels;

/// <summary>コンボボックスの選択候補コレクションを、望む並びへ差分だけで合わせる</summary>
/// <remarks>
/// <para>
/// ItemsSource を丸ごと差し替えたり、選択中の項目をいったん外して入れ直したりすると、コンボボックスが
/// 選択を落として <c>null</c> を書き戻す（TwoWay 束縛ではそれが利用者の編集として正本まで届く）。
/// そのため既存の要素インスタンスは作り直さず、並びの違いは <c>Move</c> で吸収する。
/// </para>
/// <para>
/// 一意制約の構成列行（<see cref="UniqueConstraintMemberViewModel"/>）と列ペア行
/// （<see cref="RelationshipColumnPairViewModel"/>）の候補が共有する。
/// </para>
/// </remarks>
internal static class CandidateListSync
{
    /// <summary><paramref name="candidates"/> を <paramref name="desired"/> と同じ要素・同じ並びにする</summary>
    /// <param name="candidates">合わせる側のコレクション（コンボボックスの ItemsSource）</param>
    /// <param name="desired">望む並び（同じ要素は 1 度だけ現れる前提）</param>
    /// <remarks>
    /// 手順は「望む並びに無い要素を削る → 先頭から位置ごとに、後ろにあれば Move・無ければ Insert →
    /// 余りを末尾から削る」。要素の同一性は参照で判定する
    /// </remarks>
    internal static void SyncTo<T>(ObservableCollection<T> candidates, IEnumerable<T> desired)
        where T : class
    {
        var target = desired.ToList();
        var targetSet = new HashSet<T>(target, ReferenceEqualityComparer.Instance);

        for (var i = candidates.Count - 1; i >= 0; i--)
        {
            if (!targetSet.Contains(candidates[i]))
            {
                candidates.RemoveAt(i);
            }
        }

        for (var i = 0; i < target.Count; i++)
        {
            if (i < candidates.Count && ReferenceEquals(candidates[i], target[i]))
            {
                continue;
            }

            var existing = IndexOfReference(candidates, target[i], i + 1);

            if (existing >= 0)
            {
                candidates.Move(existing, i);
            }
            else
            {
                candidates.Insert(i, target[i]);
            }
        }

        while (candidates.Count > target.Count)
        {
            candidates.RemoveAt(candidates.Count - 1);
        }
    }

    /// <summary><paramref name="start"/> 以降で <paramref name="item"/> と同じ参照の位置を返す（無ければ -1）</summary>
    private static int IndexOfReference<T>(ObservableCollection<T> items, T item, int start)
        where T : class
    {
        for (var i = start; i < items.Count; i++)
        {
            if (ReferenceEquals(items[i], item))
            {
                return i;
            }
        }

        return -1;
    }
}
