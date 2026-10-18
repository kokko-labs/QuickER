using System;
using System.Collections.Generic;
using System.Linq;

namespace QuickER.Tests.Integration.Dialects.SyncPlanFuzz;

/// <summary>同期計画の総当たりの既知の失敗の 1 項目</summary>
/// <param name="Finding">指摘の名前</param>
/// <param name="Dialects">起きる方言（<see cref="SyncPlanFuzzDialect.Name"/>）</param>
/// <param name="FixedIn">一覧から外す区切り</param>
/// <param name="Matches">失敗した種がこの項目の形か（種の番号でなく、変更と失敗の中身で判定する）</param>
internal sealed record SyncPlanFuzzKnownFailure(
    string Finding,
    IReadOnlyList<string> Dialects,
    string FixedIn,
    Func<FuzzSeedOutcome, bool> Matches
);

/// <summary>
/// 同期計画の総当たり（<see cref="SyncPlanFuzzTestsBase"/>）の既知の失敗の一覧（厳密: 一覧に無い失敗も、一覧に載っているのに
/// 起きない失敗も赤）
/// </summary>
/// <remarks>
/// 項目は種の番号でなく形で失敗を名指しする（生成器を変えて乱数の列がずれても、同じ形の失敗は同じ項目に当たる）。
/// 載っている方言の失敗だけを既知とし、ほかの方言で同じ形が起きたら新しい失敗として赤にする。修正の区切りは項目を消してから
/// 修正し、総当たりが緑になることで直ったことを確かめる。「起きなかった」の判定は既定の種の数で流したときだけ行う
/// （種を絞ると、その形が出ないことがある）。
/// </remarks>
internal static class SyncPlanFuzzKnownFailures
{
    /// <summary>一覧</summary>
    public static readonly IReadOnlyList<SyncPlanFuzzKnownFailure> All = [];

    /// <summary>
    /// 方言の失敗した種を一覧と照合し、一覧に無い失敗と、一覧に載っているのに起きなかった項目を返す
    /// </summary>
    /// <param name="dialect">方言名</param>
    /// <param name="failures">違反のあった種</param>
    /// <param name="checkNotObserved">一覧に載っているのに起きなかった項目も数えるか（既定の種の数で流したときだけ）</param>
    public static (List<FuzzSeedOutcome> Unexpected, List<string> NotObserved) Classify(
        string dialect,
        IReadOnlyList<FuzzSeedOutcome> failures,
        bool checkNotObserved
    )
    {
        var known = All.Where(k => k.Dialects.Contains(dialect)).ToList();
        var observed = new HashSet<SyncPlanFuzzKnownFailure>();
        var unexpected = new List<FuzzSeedOutcome>();

        foreach (var failure in failures)
        {
            var item = known.FirstOrDefault(k => k.Matches(failure));

            if (item is null)
            {
                unexpected.Add(failure);
                continue;
            }

            observed.Add(item);
        }

        var notObserved = checkNotObserved
            ? known
                .Where(k => !observed.Contains(k))
                .Select(k => $"{k.Finding}（{k.FixedIn}）")
                .ToList()
            : [];
        return (unexpected, notObserved);
    }
}
