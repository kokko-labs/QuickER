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
        // 受け入れ（P2-3-N15・修正 C で受け入れと決めた）：端末で一意な値を回した（入れ替えた）行は、行ごとの最後の値の書き込み
        // では上げられず、ServerWins でも戻せない（夜間の種 366＝2 行・331＝3 行）。サーバーで回した行の割り切りと同じ形で、docs の
        // 割り切りを両方の向きへ広げた
        AcceptedEntry(
            "P2-3-N15",
            "docs/code-generation.md の既知の割り切り「Rows whose unique values were rotated on the device or on the server are not carried across.」（RefreshAsync か、仮の値を書いた後に一度同期してから最後の値を書く書き換えで解消する）。端末で回した値は、行ごとの最後の値を送るので途中の仮の値が届かず、サーバーがどの行も断る",
            "n15-pending-unique-cycle",
            "1/no-quiesce:pending-unique-cycle:model_folders"
        ),
        AcceptedEntry(
            "P2-3-N15",
            "docs/code-generation.md の既知の割り切り「Rows whose unique values were rotated on the device or on the server are not carried across.」（RefreshAsync か、仮の値を書いた後に一度同期してから最後の値を書く書き換えで解消する）。3 行以上で回しても同じ",
            "n15-pending-unique-cycle-three-items",
            "1/no-quiesce:pending-unique-cycle:model_items"
        ),
        // 受け入れ：サーバーで一意な値を入れ替えた 2 行は、端末へ適用できず毎回打ち切る（洗い替えで抜ける）
        AcceptedEntry(
            "P2-3",
            "docs/code-generation.md の既知の割り切り「Rows whose unique values were rotated on the device or on the server are not carried across.」（RefreshAsync か、仮の値を書いた後に一度同期してから最後の値を書く書き換えで解消する）と、既存の決定的なシナリオ n8-true-swap-stops（入れ替えた値の持ち主の読み直しは、洗い替えで抜けるまで止まる＝既に決めた挙動）。乱数の種では、サーバーでの複数の実行に分かれた付け替えの合成でも端末から見て入れ替わる",
            "swap-without-refresh-does-not-settle",
            "1/no-quiesce:unique-swap:model_items"
        ),
        AcceptedEntry(
            "P2-3",
            "docs/code-generation.md の既知の割り切り「Rows whose unique values were rotated on the device or on the server are not carried across.」（RefreshAsync か、仮の値を書いた後に一度同期してから最後の値を書く書き換えで解消する）と、既存の決定的なシナリオ n8-true-swap-stops（入れ替えた値の持ち主の読み直しは、洗い替えで抜けるまで止まる＝既に決めた挙動）。乱数の種では、サーバーでの複数の実行に分かれた付け替えの合成でも端末から見て入れ替わる",
            "swap-folder-names-without-refresh-does-not-settle",
            "1/no-quiesce:unique-swap:model_folders"
        ),
        // 一意な値の持ち主を別の表の未送信の行が参照し、その行のサーバーの行が値を取る行の下にあると、どのポリシーの周でも抜けない
        // （区切り H3 の広い世界の種 120022 で見つけた。乱数の項目はこの正規形から作る）
        Entry(
            "H3-1",
            StuckSyncFixedIn,
            "holder-referenced-by-other-table-does-not-settle",
            "1/no-quiesce:holder-referenced-by-other-table:model_folders",
            "サーバーの値を取る行のダウンロードは、名前の持ち主（サーバーに無い）を別の表のローカルの未送信の行が参照していて消せないので毎回止まり、ServerWins はその未送信の行のサーバーの行の親（値を取る行の下）を持てないので戻さず記録を残す"
        ),
        // 同じ形の品目の表（夜間の種 400 で見つけた。静止化の失敗の署名は表の名前を残すので、表ごとに項目を持つ）
        Entry(
            "H3-1",
            StuckSyncFixedIn,
            "holder-referenced-by-other-table-items-does-not-settle",
            "1/no-quiesce:holder-referenced-by-other-table:model_items",
            "サーバーの値を取る品目のダウンロードは、code の持ち主（サーバーに無い）を別の表のローカルの未送信のイベントが参照していて消せないので毎回止まり、ServerWins はそのイベントのサーバーの行の親（値を取る品目）を持てないので戻さず記録を残す"
        ),
        // 洗い替えの取得の後にサーバーの他者が親を消すと、バッチの古い写しの子が消えた親を指したまま入る
        Entry(
            "AG-R1",
            RefreshStaleParentFixedIn,
            "interrupt-server-AfterServerFetchBeforeApply-parent-after-child-selfref-delete-next-sync-refresh",
            RefreshStaleParentSignature,
            RefreshStaleParentReason
        ),
        Entry(
            "AG-R1",
            RefreshStaleParentFixedIn,
            "interrupt-server-AfterServerFetchBeforeApply-parent-after-child-selfref-delete-next-sync-serverwins-refresh",
            RefreshStaleParentSignature,
            RefreshStaleParentReason
        ),
    ];

    /// <summary>一意な値の持ち主を別の表の未送信の行が参照する行き詰まりの項目の直す区切り（区切り H3 で見つけ、別に立てる）</summary>
    public const string StuckSyncFixedIn = "同期の行き詰まりの区切り（未起動）";

    /// <summary>洗い替えの取得の後にサーバーで消えた親の項目の直す区切り</summary>
    private const string RefreshStaleParentFixedIn = "きっかけ待ち（台帳 本番 AG-R1）";

    /// <summary>洗い替えの取得の後にサーバーで消えた親の項目の理由</summary>
    private const string RefreshStaleParentReason =
        "洗い替えのバッチの古い写しの子が、取得の後にサーバーで消えた親を指し、ローカルの外部キーで例外になる（洗い替えは失敗として止まり、次の洗い替えで取り直せる）";

    /// <summary>
    /// 洗い替えの取得の後にサーバーで消えた親の項目の署名（洗い替えの取得の後に割り込む書き込みは乱数の種の列に無いので、種には
    /// 正規形の項目を作らない＝<see cref="SeedOnlySignatures"/>）
    /// </summary>
    private const string RefreshStaleParentSignature =
        "1/throw:RefreshAsync~SyncTableBase.WriteRowsAndBlobsAsync>SqliteRepositoryCore.BulkInsertAsync:SQLite19:FOREIGN KEY constraint failed[apply:cross:in-scope]";

    /// <summary>
    /// 乱数の種の項目の再現の扱い（正規形ごと。載っていない正規形は NotGuaranteed）。夜間の種で見つけた形は NightlySeeds
    /// （P2-3 C'・夜間の 1,200 種）
    /// </summary>
    private static readonly Dictionary<string, KnownReproduction> RandomReproductions = new(
        StringComparer.Ordinal
    )
    {
        ["1/no-quiesce:pending-unique-cycle:model_folders"] = KnownReproduction.NightlySeeds,
        ["1/no-quiesce:pending-unique-cycle:model_items"] = KnownReproduction.NightlySeeds,
    };

    /// <summary>
    /// 正規形が広すぎて、種を限らずに数えるとほかの不具合を隠す署名（決定的な項目の正規形から乱数の項目を作らず、種を名指しした
    /// 項目 <see cref="SeedNamedRandomEntries"/> だけで数える）
    /// </summary>
    private static readonly HashSet<string> SeedOnlySignatures = [RefreshStaleParentSignature];

    /// <summary>
    /// 種を名指しした乱数の項目（分類できない静止化の失敗のように、正規形が広くて種を限らないとほかの不具合を隠す署名を、種で絞って
    /// 載せる。いまは無い）
    /// </summary>
    private static readonly IReadOnlyList<KnownEntry> SeedNamedRandomEntries = [];

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
    /// <param name="fixedIn">直す区切り（区切りの名前か、きっかけを待つ台帳の項目）</param>
    /// <param name="scenario">その失敗を必ず起こす決定的なシナリオの名前</param>
    /// <param name="signature">失敗の署名（<see cref="ModelFailure.Signature"/>）</param>
    /// <param name="reason">失敗の理由（製品の事実。省略できる）</param>
    public static KnownEntry Entry(
        string finding,
        string fixedIn,
        string scenario,
        string signature,
        string? reason = null
    ) =>
        new(
            finding,
            KnownKind.Pending,
            fixedIn,
            reason,
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
