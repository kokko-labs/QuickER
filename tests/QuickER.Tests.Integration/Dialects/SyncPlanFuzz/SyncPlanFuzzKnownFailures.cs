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
        // 夜間の実行（MySQL 種 1191）で見つけた。参照される表の主キーを外す変更（remove pk）を選ぶと、外し直した外部キーの
        // 付け直しが「Missing unique key for constraint」で失敗し（候補キー喪失の警告は出る）、DDL が暗黙コミットされるため、
        // 同じ計画で外していた選んでいない外部キーが消えたまま残る
        .. Entry(
            "nightly-mysql-1191",
            [DialectNames.MySql],
            "P2",
            o =>
                OnlyViolations(o, "constraint lost after a failed sync:")
                && o.Error?.Contains("Missing unique key", StringComparison.Ordinal) == true
                && o.Mutations.Any(m => m.StartsWith("remove pk ", StringComparison.Ordinal)),
            KnownReproduction.NightlySeeds
        ),
        // 夜間の実行（PostgreSQL 種 512）で見つけた。自己参照の外部キーの参照アクションの変更（AlterForeignKey）と、参照先の
        // 主キー列の型の変更を一緒に選ぶと、計画は型の食い違い（ForeignKeyColumnTypeMismatch）を警告して作り直しを外すが、
        // 外した作り直しを名指ししないので、選んだ項目が同期の後も残る
        .. Entry(
            "nightly-postgresql-512",
            [DialectNames.PostgreSql],
            "P2",
            o =>
                o.Committed
                && OnlyViolations(o, "selected item remains: FK")
                && o.Warnings.Any(w =>
                    w.StartsWith("ForeignKeyColumnTypeMismatch", StringComparison.Ordinal)
                ),
            KnownReproduction.NightlySeeds
        ),
        // 夜間の実行（Oracle 種 100・111）で見つけた。DB にある一意制約と同じ列へ主キーを付け替える変更（AlterPrimaryKey）は
        // ORA-02261 で失敗するのに、計画が警告を出さない（CLAUDE.md の既知の残件を、警告なしの失敗として名指しする）
        .. Entry(
            "nightly-oracle-100-111",
            [DialectNames.Oracle],
            "P2",
            o =>
                OnlyViolations(o, "failed without a warning: ORA-02261")
                && o.Selected.Any(s => s.StartsWith("AlterPrimaryKey", StringComparison.Ordinal)),
            KnownReproduction.NightlySeeds
        ),
        // 夜間の実行（SQLite 種 2301・2667・7088・7411・7898・15266・18203）で見つけた。表の列をすべて消す変更（主キーの解除と
        // 列の削除）を選ぶと、再構築の合成で列が 0 本の表になり、`CREATE TABLE "…_quicker_rebuild" ();` が構文エラーで失敗する。
        // 計画は警告も拒否もしない（利用者の図でも起き得る製品の欠け）
        .. Entry(
            "nightly-sqlite-empty-rebuild",
            [DialectNames.Sqlite],
            "P2",
            o =>
                OnlyViolations(
                    o,
                    "failed without a warning: SQLite Error 1: 'near \")\": syntax error'"
                ) && EmptyRebuildTable().IsMatch(o.Script),
            KnownReproduction.NightlySeeds
        ),
        // 夜間の実行（SQLite 種 12503・18316）で見つけた。主キーを、列の削除などの後で値が一意でなくなる列へ付け替える変更
        // （AlterPrimaryKey）が、再構築のデータの移送で UNIQUE constraint failed になる（警告なし）。生成器の前提（一意な列だけを
        // 主キーの候補にする）の穴か、製品の警告の欠けかは P2 で切り分ける
        .. Entry(
            "nightly-sqlite-pk-nonunique",
            [DialectNames.Sqlite],
            "P2",
            o =>
                OnlyViolations(
                    o,
                    "failed without a warning: SQLite Error 19: 'UNIQUE constraint failed:"
                ) && o.Selected.Any(s => s.StartsWith("AlterPrimaryKey", StringComparison.Ordinal)),
            KnownReproduction.NightlySeeds
        ),
        // 夜間の実行（SQLite 種 16938）で見つけた。外部キーの付け替え先が選んでいない新しい表（AddTable）のとき、計画は
        // SelectedItemRequiresUnselectedItem で名指しするのに、同期の後に作り直しの追加の側が「選んだのに残った」として数えられる。
        // ランナーの数え方（作り直しの展開後の対応づけ）の前提か、計画の名指しの欠けかは P2 で切り分ける
        .. Entry(
            "nightly-sqlite-alterfk-unselected-target",
            [DialectNames.Sqlite],
            "P2",
            o =>
                o.Committed
                && OnlyViolations(o, "selected item remains: FK+")
                && o.Warnings.Any(w =>
                    w.StartsWith("SelectedItemRequiresUnselectedItem", StringComparison.Ordinal)
                )
                && o.Unselected.Any(s => s.StartsWith("AddTable", StringComparison.Ordinal)),
            KnownReproduction.NightlySeeds
        ),
        // 夜間の実行（SQLite 種 17592）で見つけた。同じ列ペアの外部キー 2 本の参照アクションをどちらも変え、片方の作り直し
        // （AlterForeignKey）だけを選ぶと、再構築が選ばなかったほうまで作り直す
        .. Entry(
            "nightly-sqlite-twin-fk-rebuild",
            [DialectNames.Sqlite],
            "P2",
            o =>
                o.Committed
                && OnlyViolations(o, "unselected item was applied: FK")
                && o.Selected.Any(s => s.StartsWith("AlterForeignKey", StringComparison.Ordinal))
                && o.Unselected.Any(s => s.StartsWith("AlterForeignKey", StringComparison.Ordinal)),
            KnownReproduction.NightlySeeds
        ),
    ];

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

    /// <summary>違反が 1 つ以上あり、どれも指定の文で始まるか</summary>
    private static bool OnlyViolations(FuzzSeedOutcome outcome, string prefix) =>
        outcome.Violations.Count > 0
        && outcome.Violations.All(v => v.StartsWith(prefix, StringComparison.Ordinal));

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
