using System;
using System.Collections.Generic;
using System.Linq;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// 同期のモデルベーステストの既知の失敗の一覧（厳格）。
/// </summary>
/// <remarks>
/// <para>
/// 決定的なシナリオの項目は <see cref="Entry"/> で作る（範囲＝シナリオの名前・署名＝<see cref="ModelFailure.Signature"/> の全文）。
/// 載っているシナリオがその署名の組で失敗しなければ赤（直ったか、別の形で失敗した）。載っていない失敗も赤。
/// 乱数の種の失敗は、ここに載ったどれかの署名の正規形（<see cref="SyncModelRuntimeTests.Family"/>）に当たれば既知として数え、
/// 載っているのに起きないことは見ない（種の数が少なくて起きないことがある）。乱数の項目は <see cref="Build"/> が決定的な項目から作る。
/// </para>
/// <para>
/// 区切り 30 の終わりに直す区切り 30 の行を、区切り 31 で 31 の行を消し、この一覧を空にする。
/// </para>
/// </remarks>
internal static class SyncModelKnownFailures
{
    /// <summary>乱数の種の失敗の範囲</summary>
    public const string RandomScope = "random";

    /// <summary>決定的なシナリオの項目（<see cref="Entry"/> で作る）</summary>
    public static IReadOnlyList<KnownEntry> Scenarios { get; } =
    [
        // 4m-D2（修正 C で直す）: 参照する行の無いフォルダーの削除が一時的な失敗で落ちると、エンジンは「参照する表がある」
        // ことだけで DeleteRefusedOnServer と判定する。Collect・LocalWins は偽の競合を報告し（次の同期で届く）、
        // 後勝ち＋ServerWins は決着させて利用者の削除を捨てる（届かない）
        Entry("4m-D2", "修正 C", "repro-d2-versioned-collect", D2Spurious),
        Entry("4m-D2", "修正 C", "repro-d2-lww-serverwins", D2Spurious),
        Entry("4m-D2", "修正 C", "reach-after-fault-delete-versioned-collect", D2Spurious),
        Entry("4m-D2", "修正 C", "reach-after-fault-delete-versioned-localwins", D2Spurious),
        Entry("4m-D2", "修正 C", "reach-after-fault-delete-lww-collect", D2Spurious),
        Entry("4m-D2", "修正 C", "reach-after-fault-delete-lww-localwins", D2Spurious),
        Entry("4m-D2", "修正 C", "reach-after-fault-delete-lww-serverwins", D2Spurious),
        Entry(
            "4m-D2",
            "修正 C",
            "reach-after-fault-delete-lww-serverwins",
            "5/expect-local:model_folders"
        ),
        // 4m-B3（修正 C で直す）: ServerWins＋削除伝搬なしの読み直しが、一意な名前がぶつかって外した親（ローカルに無い）の
        // 子だけを戻し、ローカルの外部キーで毎回止まる。消える条件：外した親の子も一緒に保留する（ApplyServerRowsAsync の
        // 読み直しの段・SQLite の FOREIGN KEY の例外が出なくなる）
        Entry("4m-B3", "修正 C", "repro-b3", B3Throw),
        // 4m-D3（修正 C で直す）: 同じ表の別の行の更新が一意な値を空けても、先に送られて断られた取る更新をやり直さない。
        // Collect は根拠の無い UniqueValueTakenOnServer を報告し（1 回で届かない）、後勝ち＋ServerWins は決着させて利用者の
        // 更新を捨てる（届かない）。消える条件：空けた更新が届いた後に、UniqueValueTakenOnServer の書き込みをやり直す
        .. D3Entries("repro-d3", ModelTables.Items),
        .. D3Entries("repro-d3-folder-name", ModelTables.Folders),
        // 4m-D4（修正 C で直す）: 後回しの段へ回した付け替え（消す品目の code を取る）を待たずに、元のフォルダーの削除を送って
        // DeleteRefusedOnServer で断られ、やり直さない。消える条件：後回しの段の後に、断られた削除をやり直す
        Entry(
            "4m-D4",
            "修正 C",
            "repro-d4-versioned-collect",
            "3/spurious:model_folders:DeleteRefusedOnServer:pending-reparent"
        ),
        Entry("4m-D4", "修正 C", "repro-d4-versioned-collect", "5/expect-local:model_folders"),
        Entry(
            "4m-D4",
            "修正 C",
            "repro-d4-lww-serverwins",
            "3/spurious:model_folders:DeleteRefusedOnServer:pending-reparent"
        ),
        Entry("4m-D4", "修正 C", "repro-d4-lww-serverwins", "5/expect-local:model_folders"),
        // P2-3-N13（修正 C で直す）: サーバーで消えたフォルダーの名前を、別の親へ移した行が取る。端末では、値を取る行自身の
        // 古い親の参照が持ち主（サーバーに無い）を残すので、削除伝搬も持ち主の読み直しも持ち主を消せず、値を取る行は名前が
        // ぶつかって降りない（毎回打ち切る）。消える条件：値を取る行の付け替えを適用してから持ち主を消す（または一緒に適用する）
        Entry("P2-3-N13", "修正 C", "n13-holder-referenced-by-taker", N13Stuck),
        // 4m-D2 の upsert の形（修正 C で直す）: 取る更新にサーバーへの書き込みの一時的な失敗が当たると、持ち主がまだ値を持つので
        // UniqueValueTakenOnServer と判定し、後勝ち＋ServerWins は決着させて利用者の更新を捨てる。消える条件：4m-D2 と同じく、
        // 一時的な失敗をサーバーが断った理由と取り違えない
        Entry(
            "4m-D2",
            "修正 C",
            "repro-d2-upsert-unique-lww-serverwins",
            "3/spurious:model_folders:settled-upsert:after-fault"
        ),
        Entry(
            "4m-D2",
            "修正 C",
            "repro-d2-upsert-unique-lww-serverwins",
            "5/expect-local:model_folders"
        ),
        Entry(
            "4m-D2",
            "修正 C",
            "repro-d2-upsert-unique-lww-localwins",
            "3/spurious:model_folders:UniqueValueTakenOnServer:after-fault"
        ),
        // P2-3-N14（修正 C で直す）: ダウンロードの一意な値のぶつかりを、持ち主の読み直し（親も読み直す）で解いた後のやり直しが
        // 一意制約の例外で止まる（夜間の種 132・146）。見立て（製品を動かして確かめるのは修正 C）：読み直しで入れた行（持ち主の
        // 新しい親）を、やり直すバッチがもう一度挿入して一意制約の例外になり、どの行もローカルでぶつからないので、ぶつかりの検出が
        // 元の例外を投げ直す。消える条件：読み直しで入れた行を、やり直すバッチが挿入し直さない
        Entry("P2-3-N14", "修正 C", "n14-holder-moved-under-new-parent-in-batch", N14Throw),
        Entry(
            "P2-3-N14",
            "修正 C",
            "n14-holder-moved-under-new-parent-in-batch",
            "1/quiesce-throw:" + N14Detail
        ),
        // P2-3-N15（修正 C で直す）: 端末で一意な値を入れ替えた（循環させた）行は、行ごとの最後の値の書き込みでは上げられず、
        // ServerWins でも戻せない（夜間の種 366＝2 行・331＝3 行）。消える条件：循環する値を上げる（仮の値を挟むか一括で書く）か、
        // ServerWins が循環をまとめて戻す
        Entry(
            "P2-3-N15",
            "修正 C",
            "n15-pending-unique-cycle",
            "1/no-quiesce:pending-unique-cycle:model_folders"
        ),
        Entry(
            "P2-3-N15",
            "修正 C",
            "n15-pending-unique-cycle-three-items",
            "1/no-quiesce:pending-unique-cycle:model_items"
        ),
        // P2-3-N16（修正 C で直す。オラクルの側の扱いはレビューで判断）: 後勝ち＋ServerWins の同期が、親の無い追加の品目をダウンロードの
        // 前の削除伝搬でローカルから消し（RemovedBeforeDownload を例外の Data で報告）、後の失敗の注入で例外になって記録を残す。
        // 次の同期は「行なし＋追加だけ」の記録を送るものが無いとして黙って捨てる（夜間の種 786）。モデルは、記録が残った実行の
        // 決着を数えないので、端末の追加が消えたと数える
        Entry(
            "P2-3-N16",
            "修正 C",
            "n786-thrown-serverwins-removal-then-record-dropped",
            "3/lost:model_items:present-but-absent"
        ),
        // 受け入れ：サーバーで一意な値を入れ替えた 2 行は、端末へ適用できず毎回打ち切る（洗い替えで抜ける）
        AcceptedEntry(
            "P2-3",
            "docs/code-generation.md の既知の割り切り「Two rows whose unique values the server swapped are not applied.」（RefreshAsync で解消する）と、既存の決定的なシナリオ n8-true-swap-stops（入れ替えた値の持ち主の読み直しは、洗い替えで抜けるまで止まる＝既に決めた挙動）。乱数の種では、サーバーでの複数の実行に分かれた付け替えの合成でも端末から見て入れ替わる",
            "swap-without-refresh-does-not-settle",
            "1/no-quiesce:unique-swap:model_items"
        ),
        AcceptedEntry(
            "P2-3",
            "docs/code-generation.md の既知の割り切り「Two rows whose unique values the server swapped are not applied.」（RefreshAsync で解消する）と、既存の決定的なシナリオ n8-true-swap-stops（入れ替えた値の持ち主の読み直しは、洗い替えで抜けるまで止まる＝既に決めた挙動）。乱数の種では、サーバーでの複数の実行に分かれた付け替えの合成でも端末から見て入れ替わる",
            "swap-folder-names-without-refresh-does-not-settle",
            "1/no-quiesce:unique-swap:model_folders"
        ),
    ];

    /// <summary>4m-B3 の署名（ServerWins の読み直しの適用が、ローカルの外部キーで例外になる）</summary>
    private const string B3Throw =
        "1/throw:ApplyServerRowsAsync~SyncTableBase.SaveAppliedRowsAsync>EntityGraphSaver.ExecuteAsync:"
        + "SQLite19:FOREIGN KEY constraint failed[apply:self]";

    /// <summary>
    /// 4m-D3 の項目（Versioned＋Collect は根拠の無い UniqueValueTakenOnServer、後勝ち＋ServerWins は根拠の無い決着。どちらも
    /// 値を空ける未送信の更新が持ち主＝pending-release。1 回で届かないので最初の同期の後の期待も外れる）
    /// </summary>
    private static IEnumerable<KnownEntry> D3Entries(string prefix, string table) =>
        [
            Entry(
                "4m-D3",
                "修正 C",
                $"{prefix}-versioned-collect",
                $"3/spurious:{table}:UniqueValueTakenOnServer:pending-release"
            ),
            Entry("4m-D3", "修正 C", $"{prefix}-versioned-collect", $"5/expect-local:{table}"),
            Entry(
                "4m-D3",
                "修正 C",
                $"{prefix}-lww-serverwins",
                $"3/spurious:{table}:{SyncModelRun.SettledUpsertKind}:pending-release"
            ),
            Entry("4m-D3", "修正 C", $"{prefix}-lww-serverwins", $"5/expect-local:{table}"),
        ];

    /// <summary>P2-3-N14 の署名の本体（例外の段と型）</summary>
    private const string N14Detail =
        "DownloadAsync~SyncTableBase.SaveAppliedRowsAsync>EntityGraphSaver.ExecuteAsync:SQLite19:UNIQUE constraint failed: model_folders.name";

    /// <summary>P2-3-N14 の署名（途中の同期の例外）</summary>
    private const string N14Throw = "1/throw:" + N14Detail;

    /// <summary>P2-3-N13 の署名（値を取る行自身の古い参照が、サーバーに無い持ち主を残して静止化しない）</summary>
    private const string N13Stuck = "1/no-quiesce:holder-referenced-by-taker:model_folders";

    /// <summary>
    /// 乱数の種の項目の再現の扱い（正規形ごと。載っていない正規形は NotGuaranteed）。P2-3-N13 は既定の種 13 で起きる（RegularSeeds）。
    /// 夜間の種で見つけた形は NightlySeeds（P2-3 C'・夜間の 1,200 種）
    /// </summary>
    private static readonly Dictionary<string, KnownReproduction> RandomReproductions = new(
        StringComparer.Ordinal
    )
    {
        [N13Stuck] = KnownReproduction.RegularSeeds,
        [N14Throw] = KnownReproduction.NightlySeeds,
        ["1/no-quiesce:pending-unique-cycle:model_folders"] = KnownReproduction.NightlySeeds,
        ["1/no-quiesce:pending-unique-cycle:model_items"] = KnownReproduction.NightlySeeds,
        ["3/spurious:*:settled-upsert:after-fault"] = KnownReproduction.NightlySeeds,
        ["3/spurious:*:UniqueValueTakenOnServer:after-fault"] = KnownReproduction.NightlySeeds,
    };

    /// <summary>
    /// 正規形が広すぎて、種を限らずに数えるとほかの不具合を隠す署名（決定的な項目の正規形から乱数の項目を作らず、種を名指しした
    /// 項目 <see cref="SeedNamedRandomEntries"/> だけで数える）
    /// </summary>
    private static readonly HashSet<string> SeedOnlySignatures =
    [
        "3/lost:model_items:present-but-absent",
    ];

    /// <summary>
    /// 種を名指しした乱数の項目。P2-3-N16＝夜間の種 786（`3/lost:*:present-but-absent` は正規形が広く、種を限らないとほかの消失を
    /// 隠すので、種で絞る）
    /// </summary>
    private static readonly IReadOnlyList<KnownEntry> SeedNamedRandomEntries =
    [
        new(
            "P2-3-N16",
            KnownKind.Pending,
            "修正 C",
            null,
            RandomScope,
            KnownMatch.Predicate,
            Predicate: failure =>
                failure.Seed == 786
                && SyncModelRuntimeTests.Family(failure.Signature) == "3/lost:*:present-but-absent",
            RandomSeeds: true,
            Reproduction: KnownReproduction.NightlySeeds
        ),
    ];

    /// <summary>4m-D2 の署名（根拠の無い DeleteRefusedOnServer・そのキーに注入した失敗が当たった形）</summary>
    private const string D2Spurious = "3/spurious:model_folders:DeleteRefusedOnServer:after-fault";

    /// <summary>一覧（決定的なシナリオの項目と、その正規形から作った乱数の項目）</summary>
    public static KnownFailureSet Set { get; } =
        Build(
            "SyncModel",
            Scenarios,
            SyncModelRuntimeTests.Family,
            RandomReproductions,
            SeedOnlySignatures,
            SeedNamedRandomEntries
        );

    /// <summary>決定的なシナリオの項目を作る</summary>
    /// <param name="finding">指摘の番号（三巡目レビューのまとめの番号。新しく見つけたものはその番号の案）</param>
    /// <param name="fixedIn">直す区切り（<c>区切り 30</c> または <c>区切り 31</c>）</param>
    /// <param name="scenario">その失敗を必ず起こす決定的なシナリオの名前</param>
    /// <param name="signature">失敗の署名（<see cref="ModelFailure.Signature"/>）</param>
    public static KnownEntry Entry(
        string finding,
        string fixedIn,
        string scenario,
        string signature
    ) =>
        new(
            finding,
            KnownKind.Pending,
            fixedIn,
            null,
            scenario,
            KnownMatch.ExactSignature,
            signature
        );

    /// <summary>決定的なシナリオの、受け入れた失敗の項目を作る（製品の事実で直さない形）</summary>
    /// <param name="finding">指摘の番号</param>
    /// <param name="reason">受け入れる理由（製品の事実と根拠）</param>
    /// <param name="scenario">その失敗を必ず起こす決定的なシナリオの名前</param>
    /// <param name="signature">失敗の署名</param>
    public static KnownEntry AcceptedEntry(
        string finding,
        string reason,
        string scenario,
        string signature
    ) =>
        new(
            finding,
            KnownKind.Accepted,
            null,
            reason,
            scenario,
            KnownMatch.ExactSignature,
            signature
        );

    /// <summary>
    /// 決定的なシナリオの項目に、その署名を正規化した乱数の種の項目（同じ正規形は 1 つ）を足した一覧を作る
    /// </summary>
    /// <param name="engineId">一覧の名前</param>
    /// <param name="scenarios">決定的なシナリオの項目</param>
    /// <param name="family">乱数の種の失敗の署名の正規化</param>
    /// <param name="reproductions">正規形ごとの乱数の項目の再現の扱い（載っていない正規形は NotGuaranteed）</param>
    /// <param name="seedOnlySignatures">決定的な項目の署名のうち、正規形から乱数の項目を作らないもの</param>
    /// <param name="seedNamed">種を名指しした乱数の項目</param>
    public static KnownFailureSet Build(
        string engineId,
        IReadOnlyList<KnownEntry> scenarios,
        Func<string, string> family,
        IReadOnlyDictionary<string, KnownReproduction>? reproductions = null,
        IReadOnlySet<string>? seedOnlySignatures = null,
        IReadOnlyList<KnownEntry>? seedNamed = null
    ) =>
        new(
            engineId,
            [
                .. scenarios,
                .. scenarios
                    .Where(entry => seedOnlySignatures?.Contains(entry.Signature ?? "") != true)
                    .GroupBy(entry => family(entry.Signature ?? ""), StringComparer.Ordinal)
                    .Select(group =>
                        group.First() with
                        {
                            Scope = RandomScope,
                            Match = KnownMatch.Family,
                            Signature = group.Key,
                            RandomSeeds = true,
                            Reproduction =
                                reproductions?.GetValueOrDefault(
                                    group.Key,
                                    KnownReproduction.NotGuaranteed
                                ) ?? KnownReproduction.NotGuaranteed,
                        }
                    ),
                .. seedNamed ?? [],
            ],
            family
        );
}
