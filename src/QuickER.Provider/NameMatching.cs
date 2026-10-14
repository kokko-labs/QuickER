using System;
using System.Collections.Generic;
using System.Linq;
using QuickER.Model;

namespace QuickER.Provider;

/// <summary>
/// テーブル名・列名で要素を引き当てる規則の正本（大文字小文字が違うだけの名前の扱いを 1 つにする）
/// </summary>
/// <remarks>
/// <para>
/// 同じ名前かは <see cref="SchemaNames"/>（前後の空白を除き大文字小文字を無視して一致）が決める。引き当ては 2 段で行う。
/// (1) 名前が序数一致で 1 件だけあればそれ。(2) 序数一致が無く、<see cref="SchemaNames"/> で同じ名前が 1 件だけならそれ。
/// どちらでも 2 件以上なら見つからない扱いにする（並び順で決めない）。
/// </para>
/// <para>
/// (1) の序数一致は同一性の判定ではなく、<see cref="SchemaNames"/> が同じとみなす相手が片側に複数あるときに、綴りまで
/// 一致する 1 件を選ぶ優先順位（同一性は <see cref="SchemaNames"/> が決める）。片側に同じ名前が複数あるのは、名前の重複を
/// 断る入口を通らずに作られた図（手で編集したファイルなど）だけで、そのとき綴りまで一致する相手を
/// 組めば、残りだけが追加・削除の候補になる（1 段に畳むと、組めない側の表まで削除の候補に出る）。
/// </para>
/// <para>
/// DB は大文字小文字の扱いが方言・設定ごとに違う（MySQL の <c>lower_case_table_names=1</c> は名前を小文字で返し、
/// PostgreSQL の引用識別子は <c>"Dup"</c> と <c>dup</c> を共存させる）。素の序数一致では図の <c>Orders</c> と DB の
/// <c>orders</c> を別物と判断し、素の大文字小文字非依存の辞書では共存する 2 つの名前で例外になるか、
/// 並び順で片方を選ぶ。マージ取込・差分・同期計画が同じ規則で引き当てるよう、ここへ 1 本化する。
/// </para>
/// </remarks>
internal sealed class NameLookup<T>
    where T : class
{
    private readonly Dictionary<string, (T Item, int Count)> _exact = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (T Item, int Count)> _folded = new(SchemaNames.Comparer);

    /// <summary>要素と名前の取り出し方から索引を作る</summary>
    /// <param name="items">引き当ての対象</param>
    /// <param name="nameSelector">要素の名前（比較に使う形へ整えたもの）</param>
    public NameLookup(IEnumerable<T> items, Func<T, string> nameSelector)
    {
        foreach (var item in items)
        {
            var name = nameSelector(item);
            Count(_exact, name, item);
            Count(_folded, name, item);
        }
    }

    /// <summary>名前で要素を引く（序数一致で 1 件、無ければ大文字小文字を無視して 1 件のときだけ見つかる）</summary>
    public bool TryGet(string name, out T item)
    {
        if (_exact.TryGetValue(name, out var exact))
        {
            return Unique(exact, out item);
        }

        if (_folded.TryGetValue(name, out var folded))
        {
            return Unique(folded, out item);
        }

        item = null!;
        return false;
    }

    /// <summary>名前で引けるかを返す</summary>
    public bool Contains(string name) => TryGet(name, out _);

    private static bool Unique((T Item, int Count) entry, out T item)
    {
        item = entry.Count == 1 ? entry.Item : null!;
        return entry.Count == 1;
    }

    private static void Count(Dictionary<string, (T Item, int Count)> index, string name, T item)
    {
        index[name] = index.TryGetValue(name, out var existing)
            ? (existing.Item, existing.Count + 1)
            : (item, 1);
    }
}

/// <summary>2 つの集まりの要素を名前で組にする（規則は <see cref="NameLookup{T}"/>）</summary>
internal static class NameMatching
{
    /// <summary>名前で引き当てた組を返す</summary>
    /// <param name="left">片側の要素（組は この並び順で返す）</param>
    /// <param name="right">もう片側の要素</param>
    /// <param name="leftName">左の要素の名前</param>
    /// <param name="rightName">右の要素の名前</param>
    /// <returns>互いに相手の名前で引くと自分が見つかる組だけ（どちらかで曖昧なら組まない）</returns>
    /// <remarks>
    /// 組にするのは「左の名前で右を引くと y、y の名前で左を引くと x」となる x と y だけ。
    /// 片側にしか無い・曖昧な要素は組にならず、呼び出し側が新規・削除として扱う。
    /// </remarks>
    public static List<(TLeft Left, TRight Right)> Match<TLeft, TRight>(
        IReadOnlyList<TLeft> left,
        IReadOnlyList<TRight> right,
        Func<TLeft, string> leftName,
        Func<TRight, string> rightName
    )
        where TLeft : class
        where TRight : class
    {
        var leftLookup = new NameLookup<TLeft>(left, leftName);
        var rightLookup = new NameLookup<TRight>(right, rightName);
        var pairs = new List<(TLeft, TRight)>();

        foreach (var x in left)
        {
            if (
                rightLookup.TryGet(leftName(x), out var y)
                && leftLookup.TryGet(rightName(y), out var back)
                && ReferenceEquals(back, x)
            )
            {
                pairs.Add((x, y));
            }
        }

        return pairs;
    }
}
