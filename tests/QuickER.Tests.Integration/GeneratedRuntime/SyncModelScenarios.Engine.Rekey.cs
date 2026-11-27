using System.Collections.Generic;
using System.Linq;
using QuickER.Tests.GeneratedSyncModelFixture;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// 端末が品目の主キーを生の SQL で付け替える（<see cref="RekeyItemOp"/>）シナリオ。付け替え先 3 通り（新しいキー・端末が消した
/// サーバーが知るキー・仮のキー経由の入れ替え）× 動かす行の blob の状態 3 通り（端末が持っている・写し待ちの新しい行・写し待ちの
/// 書き直された行）×（ポリシー 3 通り〔Collect・LocalWins・ServerWins〕＋付け替えの後の失敗 2 通り〔写し・送信〕）。転送はすべて。
/// </summary>
/// <remarks>
/// <para>
/// 初期データはフォルダー 1 と品目 1・2（blob は 0A0B0001・0A0B0002。品目 1 の版が古い）。端末 1 が付け替えて blob を運ぶ同期を
/// 流す。どの形も書き手は端末 1 だけ（写し待ちの前段のサーバーの書き込みを除く）なので、競合の根拠は無い。
/// </para>
/// <list type="bullet">
/// <item>blob の状態：端末が持っている（付け替える行の blob は端末とサーバーで同じ）／写し待ちの新しい行（サーバーの品目 3 を
/// 降ろす同期で blob の写しが落ち、端末の blob は空の代わりの値）／写し待ちの書き直された行（サーバーが品目 1 の blob を 0C0C に
/// 書き直し、降ろす同期で写しが落ちた。端末の blob は前の 0A0B0001）。写し待ちの 2 つは、前段の終わりに端末の代わりの値を
/// 表明する。</item>
/// <item>Collect・LocalWins：同期は競合を報告せず、未送信を残さず、サーバーの行が端末の付け替えた内容に、付け替え先の blob が動かした
/// 行の本当の blob（サーバーの元のキーの blob）になる。端末の代わりの値は送らない（写し待ちは付け替え先へ運ばれ、送る前にサーバーの
/// 元のキーから写す）。入れ替えだけは、上げる 2 つの更新が一意な値を入れ替えるので両方とも UniqueValueTakenOnServer で断られ
/// （版の競合にはしない）、端末が片方の値を空けた次の同期で届く。</item>
/// <item>ServerWins：Versioned の ServerWins は何も送らずに範囲の未送信を捨て、サーバーの行で戻す（docs の「競合」の節）。サーバーは
/// 前段の後のまま、端末の行と blob はサーバーの行と blob に戻り、未送信を残さない（戻した行の blob の対照）。</item>
/// <item>付け替えの後の失敗（Collect）：付け替えの後の最初の同期で、元のキーからの写し（写し待ちの行だけ。端末が持っている行には
/// 写すものが無い）か blob の送信が失敗して例外で終わり、次の同期で付け替え先の blob が本当の blob になる。入れ替えは、写しの
/// 失敗を一意な値で断られる同期の前に、送信の失敗を値を空けた後の同期に置く（送るのはその同期）。</item>
/// </list>
/// <para>
/// blob を運ばない同期の組は置かない：運ばない実行は blob の列を持たずに記録を決着させる（docs の「無制限バイナリ列」の割り切り。
/// 対照は <c>SyncModelRuntimeTests.EmptyBlobWithoutConflict_IsStillAViolation</c>）ので、付け替えの記録が blob の列を持つかで結果が
/// 変わらず、乱数の列も常に運ぶ。後勝ちのモードも置かない：後勝ちは版を照合せずに上書きするので、付け替えた行が版の比べ方で
/// 競合にされる形を通らず、blob を送るかの形は Versioned の Collect・LocalWins と同じ記録を読む。
/// </para>
/// </remarks>
internal static partial class SyncModelScenarios
{
    /// <summary>付け替えの新しいキー・仮のキー（端末 1 の付け替えの帯）</summary>
    private const int RekeyFreshKey = 1000 + SyncModelGenerator.RekeyKeyOffset + 1;

    /// <summary>
    /// 付け替える行の blob の状態ごとの入力
    /// </summary>
    /// <param name="From">動かす行のキー</param>
    /// <param name="ServerBlob">動かす行の本当の blob（サーバーの元のキーの blob）</param>
    /// <param name="Owed">写し待ちか（写しの失敗の水準は写し待ちの行だけ）</param>
    /// <param name="Prefix">付け替えの前の前段（写し待ちを作る）</param>
    /// <param name="Items">前段の後の、サーバーの品目（キー・行・blob）</param>
    private sealed record RekeyBlobCase(
        int From,
        string ServerBlob,
        bool Owed,
        ModelOp[] Prefix,
        (int Key, string Row, string Blob)[] Items
    );

    /// <summary>付け替えの形ごとの入力</summary>
    /// <param name="Shape">形</param>
    private sealed record RekeyShapeCase(RekeyShape Shape)
    {
        /// <summary>付け替えの手（入れ替えの相手と、端末が消すキーは品目 2）</summary>
        public RekeyItemOp Op(int from) =>
            Shape switch
            {
                RekeyShape.Fresh => new RekeyItemOp(D1, from, RekeyFreshKey, RekeyShape.Fresh),
                RekeyShape.OntoDeleted => new RekeyItemOp(D1, from, 2, RekeyShape.OntoDeleted),
                _ => new RekeyItemOp(D1, from, 2, RekeyShape.Swap, RekeyFreshKey),
            };

        /// <summary>動かした行の付け替え先のキー</summary>
        public int Target => Shape == RekeyShape.Fresh ? RekeyFreshKey : 2;
    }

    private static IEnumerable<SyncModelScenario> EngineRekeyScenarios()
    {
        // シナリオの一覧の静的な初期化の中で呼ばれるので、表は静的なフィールドにせずここで作る
        (int Key, string Row, string Blob) item1 = (
            1,
            "1 folder=1 label=null code=c-1 title=t1",
            "0A0B0001"
        );
        (int Key, string Row, string Blob) item2 = (
            2,
            "2 folder=1 label=null code=c-2 title=t2",
            "0A0B0002"
        );
        (int Key, string Row, string Blob) item3 = (
            3,
            "3 folder=1 label=null code=c-3 title=t3",
            "0A0B0003"
        );
        RekeyBlobCase[] blobCases =
        [
            new(1, "0A0B0001", false, [], [item1, item2]),
            new(
                3,
                "0A0B0003",
                true,
                [
                    Item(SV, 3, folder: 1),
                    new ArmFaultOp(D1, ModelFaultKind.BlobDownload),
                    new ExpectThrowOp(D1),
                    Plain(D1),
                    Expect.Result(D1).Contains("exception *"),
                    Expect.Rows(D1, I).Contains(item3.Row),
                    new ExpectPayloadOp(D1, 3, ""),
                ],
                [item1, item2, item3]
            ),
            new(
                1,
                "0C0C",
                true,
                [
                    new WritePayloadOp(SV, 1, "0C0C"),
                    new ArmFaultOp(D1, ModelFaultKind.BlobDownload),
                    new ExpectThrowOp(D1),
                    Plain(D1),
                    Expect.Result(D1).Contains("exception *"),
                    new ExpectPayloadOp(D1, 1, "0A0B0001"),
                ],
                [item1 with { Blob = "0C0C" }, item2]
            ),
        ];
        RekeyShapeCase fresh = new(RekeyShape.Fresh);
        RekeyShapeCase ontoDeleted = new(RekeyShape.OntoDeleted);
        RekeyShapeCase swap = new(RekeyShape.Swap);

        // 行の名前は、作らない形の宣言・CLAUDE.md が名指せるよう文字列のまま書く。並びは Collect・LocalWins の上げ、ServerWins の
        // 戻し、付け替えの後の写しの失敗（写し待ちの行だけ。端末が持っている行には写すものが無く、仕掛けた失敗が起きない）、
        // 送信の失敗
        (RekeyShapeCase Shape, RekeyBlobCase Blob, string[] Names)[] rows =
        [
            (
                fresh,
                blobCases[0],
                [
                    "rekey-fresh-key-uploads-rows-and-blobs-collect",
                    "rekey-fresh-key-uploads-rows-and-blobs-localwins",
                    "rekey-fresh-key-serverwins-restores-rows-and-blobs",
                    "rekey-fresh-key-owned-send-fails-then-next-sync-uploads",
                ]
            ),
            (
                fresh,
                blobCases[1],
                [
                    "rekey-fresh-key-owed-new-row-uploads-rows-and-blobs-collect",
                    "rekey-fresh-key-owed-new-row-uploads-rows-and-blobs-localwins",
                    "rekey-fresh-key-owed-new-row-serverwins-restores-rows-and-blobs",
                    "rekey-fresh-key-owed-new-row-copy-fails-then-next-sync-uploads",
                    "rekey-fresh-key-owed-new-row-send-fails-then-next-sync-uploads",
                ]
            ),
            (
                fresh,
                blobCases[2],
                [
                    "rekey-fresh-key-owed-rewritten-row-uploads-rows-and-blobs-collect",
                    "rekey-fresh-key-owed-rewritten-row-uploads-rows-and-blobs-localwins",
                    "rekey-fresh-key-owed-rewritten-row-serverwins-restores-rows-and-blobs",
                    "rekey-fresh-key-owed-rewritten-row-copy-fails-then-next-sync-uploads",
                    "rekey-fresh-key-owed-rewritten-row-send-fails-then-next-sync-uploads",
                ]
            ),
            (
                ontoDeleted,
                blobCases[0],
                [
                    "rekey-onto-locally-deleted-key-uploads-rows-and-blobs-collect",
                    "rekey-onto-locally-deleted-key-uploads-rows-and-blobs-localwins",
                    "rekey-onto-locally-deleted-key-serverwins-restores-rows-and-blobs",
                    "rekey-onto-locally-deleted-key-owned-send-fails-then-next-sync-uploads",
                ]
            ),
            (
                ontoDeleted,
                blobCases[1],
                [
                    "rekey-onto-locally-deleted-key-owed-new-row-uploads-rows-and-blobs-collect",
                    "rekey-onto-locally-deleted-key-owed-new-row-uploads-rows-and-blobs-localwins",
                    "rekey-onto-locally-deleted-key-owed-new-row-serverwins-restores-rows-and-blobs",
                    "rekey-onto-locally-deleted-key-owed-new-row-copy-fails-then-next-sync-uploads",
                    "rekey-onto-locally-deleted-key-owed-new-row-send-fails-then-next-sync-uploads",
                ]
            ),
            (
                ontoDeleted,
                blobCases[2],
                [
                    "rekey-onto-locally-deleted-key-owed-rewritten-row-uploads-rows-and-blobs-collect",
                    "rekey-onto-locally-deleted-key-owed-rewritten-row-uploads-rows-and-blobs-localwins",
                    "rekey-onto-locally-deleted-key-owed-rewritten-row-serverwins-restores-rows-and-blobs",
                    "rekey-onto-locally-deleted-key-owed-rewritten-row-copy-fails-then-next-sync-uploads",
                    "rekey-onto-locally-deleted-key-owed-rewritten-row-send-fails-then-next-sync-uploads",
                ]
            ),
            (
                swap,
                blobCases[0],
                [
                    "rekey-swap-through-temporary-key-uploads-rows-and-blobs-collect",
                    "rekey-swap-through-temporary-key-uploads-rows-and-blobs-localwins",
                    "rekey-swap-through-temporary-key-serverwins-restores-rows-and-blobs",
                    "rekey-swap-through-temporary-key-owned-send-fails-then-next-sync-uploads",
                ]
            ),
            (
                swap,
                blobCases[1],
                [
                    "rekey-swap-through-temporary-key-owed-new-row-uploads-rows-and-blobs-collect",
                    "rekey-swap-through-temporary-key-owed-new-row-uploads-rows-and-blobs-localwins",
                    "rekey-swap-through-temporary-key-owed-new-row-serverwins-restores-rows-and-blobs",
                    "rekey-swap-through-temporary-key-owed-new-row-copy-fails-then-next-sync-uploads",
                    "rekey-swap-through-temporary-key-owed-new-row-send-fails-then-next-sync-uploads",
                ]
            ),
            (
                swap,
                blobCases[2],
                [
                    "rekey-swap-through-temporary-key-owed-rewritten-row-uploads-rows-and-blobs-collect",
                    "rekey-swap-through-temporary-key-owed-rewritten-row-uploads-rows-and-blobs-localwins",
                    "rekey-swap-through-temporary-key-owed-rewritten-row-serverwins-restores-rows-and-blobs",
                    "rekey-swap-through-temporary-key-owed-rewritten-row-copy-fails-then-next-sync-uploads",
                    "rekey-swap-through-temporary-key-owed-rewritten-row-send-fails-then-next-sync-uploads",
                ]
            ),
        ];

        foreach (var (shape, blob, names) in rows)
        {
            yield return RekeyDelivers(names[0], shape, blob, SyncConflictPolicy.Collect, null);
            yield return RekeyDelivers(names[1], shape, blob, SyncConflictPolicy.LocalWins, null);
            yield return RekeyRestores(names[2], shape, blob);

            if (blob.Owed)
            {
                yield return RekeyDelivers(
                    names[3],
                    shape,
                    blob,
                    SyncConflictPolicy.Collect,
                    ModelFaultKind.BlobDownload
                );
            }

            yield return RekeyDelivers(
                names[^1],
                shape,
                blob,
                SyncConflictPolicy.Collect,
                ModelFaultKind.BlobUpload
            );
        }

        var rewritten = blobCases[2];

        // 写す元が無い：付け替えの後、端末の次の同期の前にサーバーの他者が元のキーの品目を消すと、写すものが無いので端末の値
        // （書き直す前の blob）を送る。付け替え先（端末が消したサーバーが知るキー）にサーバーの古い blob（消した行の blob）を残さない
        yield return Moved(
            "rekey-onto-locally-deleted-key-owed-rewritten-row-origin-deleted-on-server-sends-device-blob",
            [Folder(SV, 1), Item(SV, 1, folder: 1), Item(SV, 2, folder: 1)],
            [
                .. rewritten.Prefix,
                new RekeyItemOp(D1, 1, 2, RekeyShape.OntoDeleted),
                new DeleteItemOp(SV, Direct, 1),
                Plain(D1),
                Expect
                    .Rows(SV, I)
                    .Contains("2 folder=1 label=null code=c-1 title=t1")
                    .Absent("1 *"),
                new ExpectPayloadOp(SV, 2, "0A0B0001"),
                new ExpectPayloadOp(D1, 2, "0A0B0001"),
            ]
        );

        // 後で同じキーへ降りた行：写し待ちの行を新しいキーへ動かして届けた後、サーバーの他者がそのキーの blob を書き直し、降ろす
        // 同期で写しが落ちる。次の同期は、そのキーの写し待ちをそのキー（付け替えの元のキーでない）のサーバーの行から写す
        yield return Moved(
            "rekey-fresh-key-owed-rewritten-row-later-download-copies-from-new-key",
            [Folder(SV, 1), Item(SV, 1, folder: 1), Item(SV, 2, folder: 1)],
            [
                .. rewritten.Prefix,
                new RekeyItemOp(D1, 1, RekeyFreshKey, RekeyShape.Fresh),
                Plain(D1),
                new ExpectPayloadOp(SV, RekeyFreshKey, "0C0C"),
                new WritePayloadOp(SV, RekeyFreshKey, "0D0D"),
                new ArmFaultOp(D1, ModelFaultKind.BlobDownload),
                new ExpectThrowOp(D1),
                Plain(D1),
                Plain(D1),
                Expect.Pending(D1).Empty(),
                new ExpectPayloadOp(D1, RekeyFreshKey, "0D0D"),
            ]
        );

        // 写しが試されなかった行：サーバーの新しい品目 3・4 を降ろす同期で、blob の写しの失敗が先に読む品目 3 で当たり、品目 4 の
        // 写しは試されないまま実行が止まる（端末の品目 4 の blob は空の代わりの値）。品目 4 を新しいキーへ動かすと、送るのは
        // サーバーの品目 4 の blob
        yield return Moved(
            "rekey-fresh-key-owed-new-row-copy-not-tried-uploads-server-blob",
            [Folder(SV, 1), Item(SV, 1, folder: 1), Item(SV, 2, folder: 1)],
            [
                Item(SV, 3, folder: 1),
                Item(SV, 4, folder: 1),
                new ArmFaultOp(D1, ModelFaultKind.BlobDownload),
                new ExpectThrowOp(D1),
                Plain(D1),
                Expect.Result(D1).Contains("exception *"),
                new ExpectPayloadOp(D1, 3, ""),
                new ExpectPayloadOp(D1, 4, ""),
                new RekeyItemOp(D1, 4, RekeyFreshKey, RekeyShape.Fresh),
                Plain(D1),
                Expect.Result(D1).Absent("conflict *"),
                Expect.Pending(D1).Empty(),
                new ExpectPayloadOp(SV, RekeyFreshKey, "0A0B0004"),
                new ExpectPayloadOp(D1, RekeyFreshKey, "0A0B0004"),
            ]
        );

        // 届けた後の上書き：写し待ちの行を新しいキーへ動かして届けた後（届いた行の写す元は、そのキーのサーバーの行になる）、
        // サーバーの他者がそのキーの blob を書き直し、端末はそのキーの題名を編集して LocalWins で上げるだけの実行で上書きする
        // （送らなかった blob を写し待ちにする）。次の同期は、写し待ちをそのキー（付け替えの元のキーでない）のサーバーの行から写す
        yield return Moved(
            "rekey-fresh-key-owed-rewritten-row-overwrite-after-delivery-copies-from-new-key",
            [Folder(SV, 1), Item(SV, 1, folder: 1), Item(SV, 2, folder: 1)],
            [
                .. rewritten.Prefix,
                new RekeyItemOp(D1, 1, RekeyFreshKey, RekeyShape.Fresh),
                Plain(D1),
                new ExpectPayloadOp(SV, RekeyFreshKey, "0C0C"),
                new WritePayloadOp(SV, RekeyFreshKey, "0D0D"),
                new UpdateItemOp(D1, Direct, RekeyFreshKey, 1, null, "c-1", "edited"),
                PlainOnly(D1, ModelSyncKind.Upload, policy: SyncConflictPolicy.LocalWins),
                Expect
                    .Rows(SV, I)
                    .Contains($"{RekeyFreshKey} folder=1 label=null code=c-1 title=edited"),
                new ExpectPayloadOp(SV, RekeyFreshKey, "0D0D"),
                Plain(D1),
                Expect.Pending(D1).Empty(),
                new ExpectPayloadOp(SV, RekeyFreshKey, "0D0D"),
                new ExpectPayloadOp(D1, RekeyFreshKey, "0D0D"),
            ]
        );

        // Force の洗い替え：写し待ちの行を端末が消したキーへ動かした後、送らずに Force の洗い替え（blob を運ぶ）で未送信を捨てる。
        // 付け替え先はサーバーのそのキーの行で置き換わり、その写し待ちはそのキー（付け替えの元のキーでない）のサーバーの行から写す
        yield return Moved(
            "rekey-onto-locally-deleted-key-owed-rewritten-row-force-refresh-copies-from-own-key",
            [Folder(SV, 1), Item(SV, 1, folder: 1), Item(SV, 2, folder: 1)],
            [
                .. rewritten.Prefix,
                new RekeyItemOp(D1, 1, 2, RekeyShape.OntoDeleted),
                new RefreshWithOptionsOp(D1, SyncMode.Versioned, true, false, true, []),
                Expect.Pending(D1).Empty(),
                Expect
                    .Rows(D1, I)
                    .Contains(
                        "1 folder=1 label=null code=c-1 title=t1",
                        "2 folder=1 label=null code=c-2 title=t2"
                    ),
                new ExpectPayloadOp(D1, 1, "0C0C"),
                new ExpectPayloadOp(D1, 2, "0A0B0002"),
            ]
        );

        // blob を運ばない ServerWins：写し待ちを運んだ付け替え行を、blob を運ばない Versioned の ServerWins が捨てる（何も送らず、
        // 元のキーの削除も送らない）。戻したキーは写す元をそのキーのサーバーの行にし（適用が写す元のキーを消す）、写し待ちのまま
        // 残る。次の blob を運ぶ同期が、どちらのキーもそのキーのサーバーの blob を写す（付け替えの元のキーから写さない・空を送らない）
        yield return Moved(
            "rekey-onto-locally-deleted-key-owed-rewritten-row-serverwins-without-blobs-then-sync-copies-own-blobs",
            [Folder(SV, 1), Item(SV, 1, folder: 1), Item(SV, 2, folder: 1)],
            [
                .. rewritten.Prefix,
                new RekeyItemOp(D1, 1, 2, RekeyShape.OntoDeleted),
                new ModelSyncOp(
                    D1,
                    ModelSyncKind.Sync,
                    SyncMode.Versioned,
                    SyncConflictPolicy.ServerWins,
                    [],
                    false,
                    500
                ),
                Expect.Pending(D1).Empty(),
                Expect
                    .Rows(D1, I)
                    .Contains(
                        "1 folder=1 label=null code=c-1 title=t1",
                        "2 folder=1 label=null code=c-2 title=t2"
                    ),
                Plain(D1),
                Expect.Result(D1).Absent("conflict *"),
                Expect.Pending(D1).Empty(),
                new ExpectPayloadOp(SV, 1, "0C0C"),
                new ExpectPayloadOp(SV, 2, "0A0B0002"),
                new ExpectPayloadOp(D1, 1, "0C0C"),
                new ExpectPayloadOp(D1, 2, "0A0B0002"),
            ]
        );

        // blob を捨てる Force の洗い替え：写し待ちを運んだ付け替え行を、送らずに blob を捨てる（運ばない）Force の洗い替えで
        // 置き換える。写し待ちは捨てられ（表の写し待ちを空にする）、端末の blob は空になる。次の blob を運ぶ同期は何も写さず、
        // 何も送らない（付け替えの元のキーから写さない）
        yield return Moved(
            "rekey-onto-locally-deleted-key-owed-rewritten-row-force-refresh-discarding-blobs-copies-nothing",
            [Folder(SV, 1), Item(SV, 1, folder: 1), Item(SV, 2, folder: 1)],
            [
                .. rewritten.Prefix,
                new RekeyItemOp(D1, 1, 2, RekeyShape.OntoDeleted),
                new RefreshWithOptionsOp(D1, SyncMode.Versioned, false, true, true, []),
                Expect.Pending(D1).Empty(),
                new ExpectPayloadOp(D1, 1, ""),
                new ExpectPayloadOp(D1, 2, ""),
                Plain(D1),
                Expect.Result(D1).Absent("conflict *").Contains("uploaded=0"),
                Expect.Pending(D1).Empty(),
                new ExpectPayloadOp(SV, 1, "0C0C"),
                new ExpectPayloadOp(SV, 2, "0A0B0002"),
                new ExpectPayloadOp(D1, 1, ""),
                new ExpectPayloadOp(D1, 2, ""),
                // 静止化の前に、blob を運ぶ洗い替えで端末をそろえ直す（文書の割り切り O1）
                new ModelRefreshOp(D1, SyncMode.Versioned, IncludeBlobs: true),
            ]
        );

        // 写す元の書き直し：付け替えの後、端末の次の同期の前にサーバーの他者が元のキーの blob をもう一度書き直すと、送るのは
        // 写す時点の最新の blob。元のキーの版が変わるので、元のキーの削除は版で守ると ModifiedOnServer になる（docs の競合の節）。
        // LocalWins で上書きして届ける
        yield return Moved(
            "rekey-fresh-key-owed-rewritten-row-origin-rewritten-on-server-sends-latest-blob",
            [Folder(SV, 1), Item(SV, 1, folder: 1), Item(SV, 2, folder: 1)],
            [
                .. rewritten.Prefix,
                new RekeyItemOp(D1, 1, RekeyFreshKey, RekeyShape.Fresh),
                new WritePayloadOp(SV, 1, "0E0E"),
                Plain(D1, policy: SyncConflictPolicy.LocalWins),
                Expect.Pending(D1).Empty(),
                Expect
                    .Rows(SV, I)
                    .Contains($"{RekeyFreshKey} folder=1 label=null code=c-1 title=t1")
                    .Absent("1 *"),
                Expect.Result(D1).Absent("conflict *"),
                new ExpectPayloadOp(SV, RekeyFreshKey, "0E0E"),
                new ExpectPayloadOp(D1, RekeyFreshKey, "0E0E"),
            ]
        );

        // 写した後の付け替え：写し待ちを次の同期が写した（端末とサーバーの blob がそろった）後、サーバーの他者が品目 1 の blob を
        // 書き直し、端末はそれを降ろす前に付け替える。端末の blob はもう写し待ちでないので、端末の値（写した 0C0C）を送る。元の
        // キーの版が変わるので LocalWins で上書きして届ける
        yield return Moved(
            "rekey-fresh-key-owed-rewritten-row-copied-then-origin-rewritten-sends-device-blob",
            [Folder(SV, 1), Item(SV, 1, folder: 1), Item(SV, 2, folder: 1)],
            [
                .. rewritten.Prefix,
                Plain(D1),
                new ExpectPayloadOp(D1, 1, "0C0C"),
                new WritePayloadOp(SV, 1, "0E0E"),
                new RekeyItemOp(D1, 1, RekeyFreshKey, RekeyShape.Fresh),
                Plain(D1, policy: SyncConflictPolicy.LocalWins),
                Expect.Pending(D1).Empty(),
                new ExpectPayloadOp(SV, RekeyFreshKey, "0C0C"),
                new ExpectPayloadOp(D1, RekeyFreshKey, "0C0C"),
            ]
        );

        // 付け替えの前の端末の書き込み：写し待ちの行の blob を端末が書いてから付け替えると、端末の書いた blob が届く（写し待ちは
        // 端末の書き込みで外れ、付け替え先へ運ばれない）
        yield return Moved(
            "rekey-fresh-key-owed-rewritten-row-written-before-move-sends-device-blob",
            [Folder(SV, 1), Item(SV, 1, folder: 1), Item(SV, 2, folder: 1)],
            [
                .. rewritten.Prefix,
                new WritePayloadOp(D1, 1, "0F0F"),
                new RekeyItemOp(D1, 1, RekeyFreshKey, RekeyShape.Fresh),
                Plain(D1),
                Expect.Result(D1).Absent("conflict *"),
                new ExpectPayloadOp(SV, RekeyFreshKey, "0F0F"),
                new ExpectPayloadOp(D1, RekeyFreshKey, "0F0F"),
            ]
        );

        // 上書きの後の写し待ち：サーバーが品目 1 の blob を書き直した後、端末が品目 1 の題名を編集して LocalWins で上げるだけの
        // 実行（ダウンロードしない）は、版のガードを外して行を上書きし、送らなかった blob を写し待ちにする。その行を付け替えると、
        // 送るのはサーバーの元のキーの blob
        yield return Moved(
            "rekey-fresh-key-owed-by-overwrite-uploads-server-blob",
            [Folder(SV, 1), Item(SV, 1, folder: 1), Item(SV, 2, folder: 1)],
            [
                new WritePayloadOp(SV, 1, "0C0C"),
                new UpdateItemOp(D1, Direct, 1, 1, null, "c-1", "edited"),
                PlainOnly(D1, ModelSyncKind.Upload, policy: SyncConflictPolicy.LocalWins),
                Expect.Rows(SV, I).Contains("1 folder=1 label=null code=c-1 title=edited"),
                new ExpectPayloadOp(SV, 1, "0C0C"),
                new ExpectPayloadOp(D1, 1, "0A0B0001"),
                new RekeyItemOp(D1, 1, RekeyFreshKey, RekeyShape.Fresh),
                Plain(D1),
                Expect.Result(D1).Absent("conflict *"),
                Expect.Pending(D1).Empty(),
                new ExpectPayloadOp(SV, RekeyFreshKey, "0C0C"),
                new ExpectPayloadOp(D1, RekeyFreshKey, "0C0C"),
            ]
        );
    }

    /// <summary>
    /// 付け替えて届ける行（Collect・LocalWins・付け替えの後の失敗）。失敗は、写しの失敗を付け替えの後の最初の同期に、送信の
    /// 失敗を送る同期（入れ替えは値を空けた後の同期）に置く
    /// </summary>
    private static SyncModelScenario RekeyDelivers(
        string name,
        RekeyShapeCase shape,
        RekeyBlobCase blob,
        SyncConflictPolicy policy,
        ModelFaultKind? fault
    )
    {
        var from = blob.From;
        ModelOp[] Failing(ModelFaultKind kind) =>
            [new ArmFaultOp(D1, kind), new ExpectThrowOp(D1), Plain(D1, policy: policy)];
        var ops = new List<ModelOp>(blob.Prefix) { shape.Op(from) };

        if (shape.Shape == RekeyShape.Swap)
        {
            var other = blob.Items.Single(item => item.Key == 2);

            if (fault == ModelFaultKind.BlobDownload)
            {
                ops.AddRange(Failing(ModelFaultKind.BlobDownload));
            }

            // 入れ替えは一意な値（code）も入れ替わるので、上げる 2 つの更新は両方とも断られる。写しは送る前に済んでいる
            ops.AddRange([
                Plain(D1, policy: policy),
                Expect
                    .Result(D1)
                    .Contains(
                        $"conflict model_items key={from} op=Upsert reason=UniqueValueTakenOnServer *",
                        "conflict model_items key=2 op=Upsert reason=UniqueValueTakenOnServer *"
                    )
                    .Absent("* reason=ModifiedOnServer *"),
                Expect
                    .Pending(D1)
                    .Exact([
                        .. new[] { from, 2 }.Order().Select(key => $"{I} key={key} row=present"),
                    ]),
                Expect.Rows(SV, I).Contains([.. blob.Items.Select(item => item.Row)]),
                new ExpectPayloadOp(SV, from, blob.ServerBlob),
                new ExpectPayloadOp(SV, 2, other.Blob),
                new ExpectPayloadOp(D1, from, other.Blob),
                new ExpectPayloadOp(D1, 2, blob.ServerBlob),
                new UpdateItemOp(D1, Direct, from, 1, null, "c-2x", "t2"),
            ]);

            if (fault == ModelFaultKind.BlobUpload)
            {
                ops.AddRange(Failing(ModelFaultKind.BlobUpload));
            }

            ops.AddRange([
                Plain(D1, policy: policy),
                Expect.Result(D1).Absent("conflict *"),
                Expect.Pending(D1).Empty(),
                Expect
                    .Rows(SV, I)
                    .Contains(
                        $"{from} folder=1 label=null code=c-2x title=t2",
                        $"2 folder=1 label=null code=c-{from} title=t{from}"
                    )
                    .Absent($"{RekeyFreshKey} *"),
                new ExpectPayloadOp(SV, from, other.Blob),
                new ExpectPayloadOp(SV, 2, blob.ServerBlob),
                new ExpectPayloadOp(D1, from, other.Blob),
                new ExpectPayloadOp(D1, 2, blob.ServerBlob),
            ]);
        }
        else
        {
            if (fault is { } kind)
            {
                ops.AddRange(Failing(kind));
            }

            ops.AddRange([
                Plain(D1, policy: policy),
                Expect.Result(D1).Absent("conflict *"),
                Expect.Pending(D1).Empty(),
                Expect
                    .Rows(SV, I)
                    .Contains($"{shape.Target} folder=1 label=null code=c-{from} title=t{from}")
                    .Absent($"{from} *"),
                new ExpectPayloadOp(SV, shape.Target, blob.ServerBlob),
                new ExpectPayloadOp(D1, shape.Target, blob.ServerBlob),
            ]);
        }

        return Moved(name, [Folder(SV, 1), Item(SV, 1, folder: 1), Item(SV, 2, folder: 1)], ops);
    }

    /// <summary>
    /// 付け替えて ServerWins で戻す行：Versioned の ServerWins は何も送らずに未送信を捨て、端末の行と blob をサーバーの行と blob
    /// （前段の後のまま）に戻す
    /// </summary>
    private static SyncModelScenario RekeyRestores(
        string name,
        RekeyShapeCase shape,
        RekeyBlobCase blob
    )
    {
        var rows = blob.Items.Select(item => item.Row).ToArray();

        return Moved(
            name,
            [Folder(SV, 1), Item(SV, 1, folder: 1), Item(SV, 2, folder: 1)],
            [
                .. blob.Prefix,
                shape.Op(blob.From),
                Plain(D1, policy: SyncConflictPolicy.ServerWins),
                Expect.Pending(D1).Empty(),
                Expect.Rows(SV, I).Contains(rows).Absent($"{RekeyFreshKey} *"),
                Expect.Rows(D1, I).Contains(rows).Absent($"{RekeyFreshKey} *"),
                .. blob.Items.SelectMany(item =>
                    new ModelOp[]
                    {
                        new ExpectPayloadOp(SV, item.Key, item.Blob),
                        new ExpectPayloadOp(D1, item.Key, item.Blob),
                    }
                ),
            ]
        );
    }
}
