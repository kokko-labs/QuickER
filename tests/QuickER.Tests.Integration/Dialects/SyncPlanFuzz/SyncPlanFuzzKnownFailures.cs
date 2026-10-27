using System;
using System.Collections.Generic;
using System.Linq;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.Integration.Dialects.SyncPlanFuzz;

/// <summary>
/// 同期計画の総当たり（<see cref="SyncPlanFuzzTestsBase"/>）の既知の失敗の一覧（厳密: 一覧に無い失敗も、一覧に載っているのに
/// 起きない失敗も赤）
/// </summary>
/// <remarks>
/// 項目は <see cref="Entry"/> で作り、種の番号でなく形で失敗を名指しする（生成器を変えて乱数の列がずれても、同じ形の失敗は
/// 同じ項目に当たる）。載っている方言の失敗だけを既知とし（範囲＝方言名）、ほかの方言で同じ形が起きたら新しい失敗として赤にする。
/// 修正の区切りは項目を消してから修正し、総当たりが緑になることで直ったことを確かめる。「起きなかった」の判定は既定の種の数で
/// 流したときだけ行う（種を絞ると、その形が出ないことがある）。照合の規則は <see cref="KnownFailureSet"/> が持つ。
/// </remarks>
internal static class SyncPlanFuzzKnownFailures
{
    /// <summary>項目（<see cref="Entry"/> で作る）</summary>
    public static readonly IReadOnlyList<KnownEntry> Entries = [];

    /// <summary>一覧</summary>
    public static KnownFailureSet Set { get; } = new("SyncPlan", Entries);

    /// <summary>項目を作る（方言ごとに 1 項目）</summary>
    /// <param name="finding">指摘の名前</param>
    /// <param name="dialects">起きる方言（<see cref="SyncPlanFuzzDialect.Name"/>）</param>
    /// <param name="fixedIn">一覧から外す区切り</param>
    /// <param name="matches">失敗した種がこの項目の形か（種の番号でなく、変更と失敗の中身で判定する）</param>
    public static IEnumerable<KnownEntry> Entry(
        string finding,
        IReadOnlyList<string> dialects,
        string fixedIn,
        Func<FuzzSeedOutcome, bool> matches
    ) =>
        dialects.Select(dialect => new KnownEntry(
            finding,
            KnownKind.Pending,
            fixedIn,
            null,
            dialect,
            KnownMatch.Predicate,
            Predicate: failure => failure.Source is FuzzSeedOutcome outcome && matches(outcome),
            RandomSeeds: true
        ));

    /// <summary>違反のあった種を、照合に渡す失敗にする（範囲＝方言・元の結果を持つ）</summary>
    /// <param name="outcome">違反のあった種の結果</param>
    public static EngineFailure ToFailure(FuzzSeedOutcome outcome) =>
        new(
            outcome.Dialect,
            outcome.Violations.FirstOrDefault() ?? "",
            outcome.Describe(),
            outcome.Seed,
            outcome
        );
}
