using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
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
internal static partial class SyncPlanFuzzKnownFailures
{
    /// <summary>項目（<see cref="Entry"/> で作る）</summary>
    public static readonly IReadOnlyList<KnownEntry> Entries =
    [
        // mysql-temporal-zero-precision-respelling（MySQL・修正 E・P2-4）：小数秒の精度 0 を明示した綴り（datetime(0)・time(0)・
        // timestamp(0)）を MySqlTypeNotation.Normalize が素の綴りへ寄せないので、取り込んだ綴り（datetime・time・timestamp）と
        // 食い違う AlterColumn が出て、同期しても消えない（CLAUDE.md の取込の節 (12) の型の欠陥。表明 2「選んだ項目が残る」）
        // - 実 DB の種 77：日時の列の型を datetime から datetime(0) へ書き換える（respell type）
        .. Entry(
            "mysql-temporal-zero-precision-respelling",
            [DialectNames.MySql],
            "修正 E",
            IsZeroPrecisionRespelling,
            KnownReproduction.RegularSeeds
        ),
        .. Scenario(
            "mysql-temporal-zero-precision-respelling",
            DialectNames.MySql,
            "修正 E",
            ["mysql-77-respell-datetime"],
            IsZeroPrecisionRespelling
        ),
        // sqlite-rebuild-replace-all-columns（SQLite・修正 B・P2-4）：表の元の列をすべて消して（主キーの解除と列の削除）新しい列を
        // 足すと、再構築に写す列が無く INSERT … SELECT が出ず、行がすべて消える（警告なし・コミット。逐次 DDL の方言では行は残り、
        // 新しい列は NULL になる）。表明 5（行数）の「row count changed」で出る。nightly-sqlite-empty-rebuild の隣の形
        // - 実 DB の種 795：主キーの解除・一意制約の削除・列 2 本の削除と列 2 本の追加を選ぶ → 行が 3 → 0
        .. Entry(
            "sqlite-rebuild-replace-all-columns",
            [DialectNames.Sqlite],
            "修正 B",
            IsRebuildReplacingAllColumns,
            KnownReproduction.RegularSeeds
        ),
        .. Scenario(
            "sqlite-rebuild-replace-all-columns",
            DialectNames.Sqlite,
            "修正 B",
            ["sqlite-795-replace-all-columns"],
            IsRebuildReplacingAllColumns
        ),
        // synthesized-unique-name-collision（修正 E・P2-4）：名前付きの一意制約の列を変え（名前は保つ）、元の列に名前の無い一意制約を
        // 足すと、名前の無い一意制約の合成名（UniqueConstraint.SynthesizeName＝UQ_{表}_{列}）が既存の名前と同じになり、MySQL
        // 「Duplicate key name」・PostgreSQL 42P07 で失敗する（警告なし。外部キーの既定名は UniqueForeignKey で一意化するが、一意制約の
        // 合成名は一意化しない）
        // - 実 DB の夜間の MySQL・PostgreSQL の種 386
        .. Entry(
            "synthesized-unique-name-collision",
            [DialectNames.MySql, DialectNames.PostgreSql],
            "修正 E",
            IsSynthesizedUniqueNameCollision,
            KnownReproduction.NightlySeeds
        ),
        .. Scenario(
            "synthesized-unique-name-collision",
            DialectNames.MySql,
            "修正 E",
            ["mysql-386-synthesized-unique-name"],
            IsSynthesizedUniqueNameCollision
        ),
        .. Scenario(
            "synthesized-unique-name-collision",
            DialectNames.PostgreSql,
            "修正 E",
            ["postgresql-386-synthesized-unique-name"],
            IsSynthesizedUniqueNameCollision
        ),
        // 4m-f1-legacy-fk-name（4m-F1・修正 E・P2-6）：旧版（v0.1.0〜v0.2.0）の DDL で作った PostgreSQL の DB（名前の無い外部キーの
        // 縮める前の既定名を PostgreSQL が 63 バイトで切り詰めた名前）に、同じ子の表から名前の無い外部キーを図で 1 本足すと、差分の
        // 吸収が 2 本目の追加にも DB にある 1 本目と同じ切り詰めた名前を与え、既定で選ばれる追加が 42710 で断られる（警告なし）。
        // 行は来歴の行（SyncPlanHistoryRows）の「旧版 × 長い子の表名 × 2 本目を足す」
        .. Scenario(
            SyncPlanHistoryRows.F1Finding,
            DialectNames.PostgreSql,
            "修正 E",
            [SyncPlanHistoryRows.F1Id],
            IsLegacyForeignKeyNameCollision
        ),
        // 夜間の実行（SQLite の実 DB の種 2301・2667・7088・7411・7898・15266・18203）で見つけた。表の列をすべて消す変更（主キーの解除と
        // 列の削除）を選ぶと、再構築の合成で列が 0 本の表になり、`CREATE TABLE "…_quicker_rebuild" ();` が構文エラーで失敗する。
        // 計画は警告も拒否もしない（利用者の図でも起き得る製品の欠け）
        .. Entry(
            "nightly-sqlite-empty-rebuild",
            [DialectNames.Sqlite],
            "P2",
            IsEmptyRebuild,
            KnownReproduction.NightlySeeds
        ),
        .. Scenario(
            "nightly-sqlite-empty-rebuild",
            DialectNames.Sqlite,
            "P2",
            ["sqlite-empty-rebuild"],
            IsEmptyRebuild
        ),
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

    /// <summary>synthesized-unique-name-collision：名前の無い一意制約の合成名が既存の一意制約の名前と衝突して失敗する</summary>
    private static bool IsSynthesizedUniqueNameCollision(FuzzSeedOutcome o) =>
        !o.Committed
        && o.Violations.Count == 1
        && o.Violations[0].StartsWith("failed without a warning:", StringComparison.Ordinal)
        && (
            o.Violations[0].Contains("Duplicate key name", StringComparison.Ordinal)
            || o.Violations[0].Contains("42P07", StringComparison.Ordinal)
        )
        && o.Mutations.Any(m => m.StartsWith("change unique ", StringComparison.Ordinal))
        && o.Mutations.Any(m => m.StartsWith("add unique ", StringComparison.Ordinal));

    /// <summary>
    /// 4m-f1-legacy-fk-name：旧版の名前の吸収が 2 本目の追加に DB の外部キーと同じ名前を与え、追加が 42710 で失敗する（静的な規則 R4 も
    /// 予言する）
    /// </summary>
    private static bool IsLegacyForeignKeyNameCollision(FuzzSeedOutcome o) =>
        !o.Committed
        && o.Violations.Count == 1
        && o.Violations[0].StartsWith("failed without a warning:", StringComparison.Ordinal)
        && o.Violations[0].Contains("42710", StringComparison.Ordinal)
        && o.Notes.Contains(SyncPlanFuzzRunner.StaticRulePredictedNote)
        && o.Mutations.Any(m => m.StartsWith("add fk ", StringComparison.Ordinal));

    /// <summary>mysql-temporal-zero-precision-respelling：精度 0 を明示した日時の綴りの AlterColumn が同期しても残る</summary>
    private static bool IsZeroPrecisionRespelling(FuzzSeedOutcome o) =>
        o.Committed
        && OnlyViolations(o, "selected item remains: AlterColumn")
        // 選んだ項目の説明は UI の言語で変わるので、変更の記録（言語に依らない）で見る
        && o.Mutations.Any(m =>
            m.StartsWith("respell type ", StringComparison.Ordinal)
            && m.EndsWith("(0)", StringComparison.Ordinal)
        );

    /// <summary>sqlite-rebuild-replace-all-columns：警告なしのコミットで行数が減る（再構築が行を写さない）</summary>
    private static bool IsRebuildReplacingAllColumns(FuzzSeedOutcome o) =>
        o.Committed
        && o.Warnings.Count == 0
        && o.Violations.Count > 0
        && o.Violations.All(v =>
            v.StartsWith("row count changed:", StringComparison.Ordinal)
            && v.EndsWith("-> 0", StringComparison.Ordinal)
        );

    /// <summary>nightly-sqlite-empty-rebuild：列が 0 本の再構築の表が構文エラー</summary>
    private static bool IsEmptyRebuild(FuzzSeedOutcome o) =>
        OnlyViolations(o, "failed without a warning: SQLite Error 1: 'near \")\": syntax error'")
        && EmptyRebuildTable().IsMatch(o.Script);

    /// <summary>nightly-sqlite-pk-nonunique：一意でない列への主キーの付け替えが UNIQUE 違反</summary>
    private static bool IsNonUniquePrimaryKey(FuzzSeedOutcome o) =>
        OnlyViolations(o, "failed without a warning: SQLite Error 19: 'UNIQUE constraint failed:")
        && o.Selected.Any(s => s.StartsWith("AlterPrimaryKey", StringComparison.Ordinal));

    /// <summary>列が 0 本の再構築の表の作成文</summary>
    [GeneratedRegex(@"_quicker_rebuild"" \(\s*\);")]
    private static partial Regex EmptyRebuildTable();

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
