using System;
using System.Collections.Generic;
using System.Linq;
using QuickER.Dialects;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.Integration.Dialects.SyncPlanFuzz;

/// <summary>
/// 同期計画の総当たり（<see cref="SyncPlanFuzzTestsBase"/>）の既知の失敗の一覧（厳密: 一覧に無い失敗も、一覧に載っているのに
/// 起きない失敗も赤）
/// </summary>
/// <remarks>
/// 項目は <see cref="Entry"/> で作り、種の番号でなく形で失敗を名指しする（生成器を変えて乱数の列がずれても、同じ形の失敗は
/// 同じ項目に当たる）。載っている方言の失敗だけを既知とし（範囲＝方言名）、ほかの方言で同じ形が起きたら新しい失敗として赤にする。
/// 修正の区切りは項目を消してから修正し、総当たりが緑になることで直ったことを確かめる。「起きなかった」の判定は、項目の
/// <see cref="KnownEntry.Reproduction"/> が言う既定の種の数で流したときだけ行う（種を絞ると、その形が出ないことがある。
/// 夜間に見つけた形は夜間の既定の種の数でだけ数える）。照合の規則は <see cref="KnownFailureSet"/> が持つ。
/// </remarks>
internal static class SyncPlanFuzzKnownFailures
{
    /// <summary>項目（<see cref="Entry"/> で作る）</summary>
    public static readonly IReadOnlyList<KnownEntry> Entries =
    [
        // nightly-sqlite-pk-nonunique（受け入れ・P2-4 で切り分けた）：値が一意でない列への主キーの付け替えは、DB がデータで断る
        // （SQLite は再構築のデータの移送で UNIQUE constraint failed になり、トランザクションを巻き戻して何も変えない）。計画は列の値を
        // 知らないので警告できない（DB の事実）。乱数の種で出ていた形（旧い実 DB の種 12503・18316、P2-4 の夜間の SQLite の種 5631・
        // 8979・18819、MySQL の種 861）は、主キーを外した・縮めた・列を消した後で一意でない列を主キーへ足す生成器の穴だったので、
        // 生成器を直して乱数の項目を消した。行 sqlite-pk-nonunique は、巻き戻して何も変えない振る舞いを残すために受け入れで持つ
        .. Scenario(
            "nightly-sqlite-pk-nonunique",
            DialectNames.Sqlite,
            "P2",
            ["sqlite-pk-nonunique"],
            IsNonUniquePrimaryKey,
            acceptedReason: "値が一意でない列への主キーは DB がデータで断る（SQLite は再構築を巻き戻して何も変えない）。計画は列の値を知らないので警告できない（DB の事実）"
        ),
    ];

    /// <summary>nightly-sqlite-pk-nonunique：一意でない列への主キーの付け替えが UNIQUE 違反</summary>
    private static bool IsNonUniquePrimaryKey(FuzzSeedOutcome o) =>
        OnlyViolations(o, "failed without a warning: SQLite Error 19: 'UNIQUE constraint failed:")
        && o.Selected.Any(s => s.StartsWith("AlterPrimaryKey", StringComparison.Ordinal));

    /// <summary>一覧</summary>
    public static KnownFailureSet Set { get; } = new("SyncPlan", Entries);

    /// <summary>項目を作る（方言ごとに 1 項目）</summary>
    /// <param name="finding">指摘の名前</param>
    /// <param name="dialects">起きる方言（<see cref="SyncPlanFuzzDialect.Name"/>）</param>
    /// <param name="fixedIn">一覧から外す区切り</param>
    /// <param name="matches">失敗した種がこの項目の形か（種の番号でなく、変更と失敗の中身で判定する）</param>
    /// <param name="reproduction">どの既定の種の数で流せば必ず起きるか（「起きない」を数えるのはそのときだけ）</param>
    public static IEnumerable<KnownEntry> Entry(
        string finding,
        IReadOnlyList<string> dialects,
        string fixedIn,
        Func<FuzzSeedOutcome, bool> matches,
        KnownReproduction reproduction
    ) =>
        dialects.Select(dialect => new KnownEntry(
            finding,
            KnownKind.Pending,
            fixedIn,
            null,
            dialect,
            KnownMatch.Predicate,
            Predicate: failure => failure.Source is FuzzSeedOutcome outcome && matches(outcome),
            RandomSeeds: true,
            Reproduction: reproduction
        ));

    /// <summary>
    /// 決定的な行（<see cref="SyncPlanScenarios"/>）の項目を作る（行ごとに 1 項目・範囲は方言名＋
    /// <see cref="SyncPlanScenarios.ScopeSuffix"/>・どの種の数で流しても「起きない」を数える）
    /// </summary>
    /// <param name="finding">指摘の名前（乱数の項目と同じ）</param>
    /// <param name="dialect">行の方言</param>
    /// <param name="fixedIn">一覧から外す区切り</param>
    /// <param name="scenarioIds">行の Id（表に無い Id は断る）</param>
    /// <param name="matches">失敗した行がこの項目の形か（乱数の項目と同じ述語）</param>
    public static IEnumerable<KnownEntry> Scenario(
        string finding,
        string dialect,
        string fixedIn,
        IReadOnlyList<string> scenarioIds,
        Func<FuzzSeedOutcome, bool> matches,
        string? acceptedReason = null
    ) =>
        scenarioIds.Select(id =>
        {
            var row = SyncPlanScenarios.Table[id];

            if (!string.Equals(row.Input.Dialect, dialect, StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    $"行 {id} の方言は {row.Input.Dialect}（項目は {dialect}）"
                );
            }

            return new KnownEntry(
                finding,
                acceptedReason is null ? KnownKind.Pending : KnownKind.Accepted,
                acceptedReason is null ? fixedIn : null,
                acceptedReason,
                SyncPlanScenarios.ScopeOf(dialect, id),
                KnownMatch.Predicate,
                Predicate: failure =>
                    failure.Source is FuzzSeedOutcome outcome
                    && string.Equals(outcome.ScenarioId, id, StringComparison.Ordinal)
                    && matches(outcome)
            );
        });

    /// <summary>違反が 1 つ以上あり、どれも指定の文で始まるか</summary>
    private static bool OnlyViolations(FuzzSeedOutcome outcome, string prefix) =>
        outcome.Violations.Count > 0
        && outcome.Violations.All(v => v.StartsWith(prefix, StringComparison.Ordinal));

    /// <summary>
    /// 違反のあった種か行を、照合に渡す失敗にする（範囲＝方言、行なら方言＋<see cref="SyncPlanScenarios.ScopeSuffix"/>・
    /// 行は種を持たない・元の結果を持つ）
    /// </summary>
    /// <param name="outcome">違反のあった種か行の結果</param>
    public static EngineFailure ToFailure(FuzzSeedOutcome outcome) =>
        new(
            outcome.ScenarioId is null
                ? outcome.Dialect
                : SyncPlanScenarios.ScopeOf(outcome.Dialect, outcome.ScenarioId),
            outcome.Violations.FirstOrDefault() ?? "",
            outcome.Describe(),
            outcome.ScenarioId is null ? outcome.Seed : null,
            outcome
        );
}
