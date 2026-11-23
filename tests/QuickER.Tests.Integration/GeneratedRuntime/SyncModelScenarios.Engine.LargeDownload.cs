using System.Collections.Generic;
using QuickER.Tests.GeneratedSyncModelFixture;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// 降ろす行の上限（<c>SyncOptions.DownloadRowLimit</c>）のシナリオ。サーバーの他者がフォルダー 2 つと品目 2 つ（4 行）を作り、
/// 端末はサーバーが知らないフォルダー 50 を未送信のまま持つ（受信だけと Force の洗い替え）。転送はすべて
/// （<see cref="Moved"/>）。
/// </summary>
internal static partial class SyncModelScenarios
{
    /// <summary>端末で作る、サーバーが知らないフォルダー（未送信のまま受信の段へ進む）</summary>
    private const int UnsentFolder = 50;

    /// <summary>上限を渡した受信だけ（blob は運ぶ）</summary>
    private static ModelSyncOp RowLimited(ModelActor device, int limit) =>
        PlainOnly(device, ModelSyncKind.Download) with
        {
            DownloadRowLimit = limit,
        };

    /// <summary>サーバーの他者のフォルダー 2 つと品目 2 つ（取得は 4 行）と、端末の未送信のフォルダー</summary>
    private static ModelWriteOp[] FourServerRowsAndUnsentFolder() =>
        [
            .. Seed(1, "alice", 11, "widget"),
            .. Seed(2, "bob", 12, "gadget"),
            Folder(D1, UnsentFolder, name: "unsent"),
        ];

    private static IEnumerable<SyncModelScenario> EngineLargeDownloadScenarios() =>
        [
            // 上限を超え、範囲の表に未送信がある受信は、何も適用せずに断る（端末の行も未送信もそのまま）。上限を上げた同じ
            // 受信が 4 行を全部降ろすことで、断った実行が再開点を進めていないことを見る
            Moved(
                "dl-row-limit-exceeded-with-unsent-change-refuses-and-applies-nothing",
                [],
                [
                    .. FourServerRowsAndUnsentFolder(),
                    new ExpectThrowOp(D1),
                    RowLimited(D1, 3),
                    Expect
                        .Result(D1)
                        .Contains(
                            "exception SyncDownloadTooLargeException: *",
                            "row-limit fetched=4 limit=3 pending-count=1",
                            $"pending {F} count=1"
                        ),
                    Expect.Rows(D1, F).Exact($"{UnsentFolder} parent=null name=unsent", "999 *"),
                    Expect.Rows(D1, I).Exact("999 *"),
                    RowLimited(D1, 100),
                    Expect.Result(D1).Contains("downloaded=4"),
                    Expect
                        .Rows(D1, F)
                        .Exact(
                            "1 parent=null name=alice",
                            "2 parent=null name=bob",
                            $"{UnsentFolder} parent=null name=unsent",
                            "999 *"
                        ),
                    Expect.Rows(D1, I).Exact("11 *", "12 *", "999 *"),
                ]
            ),
            // 先に送信だけを流して未送信を無くせば、同じ小さい上限の受信は断られずに全部を降ろす
            Moved(
                "dl-row-limit-exceeded-after-upload-applies-everything",
                [],
                [
                    .. FourServerRowsAndUnsentFolder(),
                    PlainOnly(D1, ModelSyncKind.Upload),
                    Expect.Result(D1).Contains("uploaded=1").Absent("conflict *"),
                    RowLimited(D1, 3),
                    Expect.Result(D1).Absent("exception *").Contains("downloaded=4"),
                    Expect
                        .Rows(D1, F)
                        .Exact(
                            "1 parent=null name=alice",
                            "2 parent=null name=bob",
                            $"{UnsentFolder} parent=null name=unsent",
                            "999 *"
                        ),
                    Expect.Rows(D1, I).Exact("11 *", "12 *", "999 *"),
                ]
            ),
            // Force の洗い替えは、上限を超えても、捨てる未送信を数えずに断らない。未送信を捨てて端末をサーバーの内容に
            // そろえ、次の同期は何も送らない（上限を読まないエンジンも同じ答え）
            Moved(
                "refresh-force-over-row-limit-discards-unsent-and-is-not-refused",
                [],
                [
                    .. FourServerRowsAndUnsentFolder(),
                    RefreshAll(D1, force: true) with
                    {
                        DownloadRowLimit = 3,
                    },
                    Expect.Result(D1).Absent("exception *").Contains("discarded-changes=1"),
                    Expect
                        .Rows(D1, F)
                        .Exact("1 parent=null name=alice", "2 parent=null name=bob", "999 *"),
                    Expect.Rows(D1, I).Exact("11 *", "12 *", "999 *"),
                    Plain(D1),
                    Expect.Result(D1).Contains("uploaded=0").Absent("conflict *"),
                    Expect.Rows(SV, F).Absent($"{UnsentFolder} *"),
                ]
            ),
            // 取得の合計が上限と同じなら、未送信があっても断らない（断るのは上限を超えたときだけ）
            Moved(
                "dl-row-limit-equal-to-fetched-rows-is-not-refused",
                [],
                [
                    .. FourServerRowsAndUnsentFolder(),
                    RowLimited(D1, 4),
                    Expect.Result(D1).Absent("exception *").Contains("downloaded=4"),
                    Expect
                        .Rows(D1, F)
                        .Exact(
                            "1 parent=null name=alice",
                            "2 parent=null name=bob",
                            $"{UnsentFolder} parent=null name=unsent",
                            "999 *"
                        ),
                    Expect.Rows(D1, I).Exact("11 *", "12 *", "999 *"),
                ]
            ),
            .. ChunkBoundaryScenarios(),
        ];

    /// <summary>
    /// 塊をまたぐ形のシナリオ。どのエンジン・どの道でも同じ答えになる期待で書き、小さい塊の端末
    /// （<see cref="SyncModelEngineDevice.OnSmallChunks"/>＝閾値 2・塊 2）では形の両端が別の塊に入るよう、取得の順（サーバーの版の順＝
    /// 書いた順）の前に詰め物の行を置く。同期のバッチは 500 なので、表の行は 1 ページで届き、取得の合計が 2 を超えたページで塊の
    /// 経路へ入る（表の行は 2 行ずつの塊に切られる）。<see cref="SyncModelScenario.CarriesAcrossChunks"/> の行は、小さい塊の端末で
    /// 持ち越しが起きたことを控えとの照合の Fact が照らす。
    /// </summary>
    private static IEnumerable<SyncModelScenario> ChunkBoundaryScenarios() =>
        [
            // (a) 自己参照の親が後ろの塊にある：取得の順は 詰め物 3001・子 3003・親 3002（子を作った後に親を書き直すので、親の版が
            // 新しい）。塊は [3001, 3003] と [3002] で、子は親の無い行として持ち越され、最後の段で入る
            Moved(
                "chunk-selfref-parent-in-later-chunk",
                [],
                [
                    Folder(SV, 3001, name: "filler"),
                    Folder(SV, 3002, name: "parent"),
                    Folder(SV, 3003, parent: 3002, name: "child"),
                    Rename(SV, 3002, "parent-renamed"),
                    Plain(D1),
                    Expect.Result(D1).Absent("truncation *", "conflict *"),
                    Expect
                        .Rows(D1, F)
                        .Contains(
                            "3001 parent=null name=filler",
                            "3002 parent=null name=parent-renamed",
                            "3003 parent=3002 name=child"
                        ),
                ]
            ) with
            {
                CarriesAcrossChunks = true,
            },
            // (b) 表どうしの循環で両方の行が新しい：フォルダー 3011 は品目 3012 をピン留めし、品目 3012 は 3011 の下にある。
            // フォルダーの表の塊 [3011] はピン留めの先が無く、品目の表の塊 [3012, 3013] の 3012 は親が無いので、どちらも持ち越され、
            // 最後の段の 1 つのトランザクションで入る（品目 3013 は塊の経路へ入るための詰め物）
            Moved(
                "chunk-cross-table-cycle-both-new",
                [],
                [
                    Folder(SV, 3011),
                    Line(SV, 3012, 3011, "pinned"),
                    new PinFolderOp(SV, Direct, 3011, 3012),
                    Line(SV, 3013, 999, "filler"),
                    Plain(D1),
                    Expect.Result(D1).Absent("truncation *", "conflict *"),
                    Expect.Rows(D1, F).Contains("3011 parent=null name=f3011 pinned=3012"),
                    Expect.Rows(D1, I).Contains("3012 folder=3011 *", "3013 folder=999 *"),
                ]
            ) with
            {
                CarriesAcrossChunks = true,
            },
            // (c) 同じ表の自己参照の循環が別の塊：3022 の親は 3023、3023 の親は 3022。取得の順は 詰め物 3021・3023・3022 で、塊は
            // [3021, 3023] と [3022]。どちらも親の無い行として持ち越され、最後の段で入る
            Moved(
                "chunk-selfref-cycle-across-chunks",
                [],
                [
                    Folder(SV, 3021, name: "filler"),
                    Folder(SV, 3022),
                    Folder(SV, 3023, parent: 3022),
                    new UpdateFolderOp(SV, Direct, 3022, 3023, "f3022"),
                    Plain(D1),
                    Expect.Result(D1).Absent("truncation *", "conflict *"),
                    Expect
                        .Rows(D1, F)
                        .Contains("3022 parent=3023 name=f3022", "3023 parent=3022 name=f3023"),
                ]
            ) with
            {
                CarriesAcrossChunks = true,
            },
            // (d) 一意な値の入れ替えが別の塊：端末の 3031（sx）と 3032（sy）の code をサーバーで入れ替える。取得の順は 詰め物 3033・
            // 3032（sx）・3031（sy）で、塊は [3033, 3032] と [3031]。どちらも端末の相手の行が値を持つので持ち越され、最後の段の回転で入る
            Moved(
                "chunk-unique-swap-across-chunks",
                [
                    Folder(SV, 3030),
                    Item(SV, 3031, folder: 3030, code: "sx"),
                    Item(SV, 3032, folder: 3030, code: "sy"),
                ],
                [
                    Item(SV, 3033, folder: 3030),
                    new UpdateItemOp(SV, Direct, 3031, 3030, null, "swap-tmp", "t3031"),
                    new UpdateItemOp(SV, Direct, 3032, 3030, null, "sx", "t3032"),
                    new UpdateItemOp(SV, Direct, 3031, 3030, null, "sy", "t3031"),
                    Plain(D1),
                    Expect.Result(D1).Absent("truncation *", "conflict *"),
                    Expect
                        .Rows(D1, I)
                        .Contains("3031 folder=3030 * code=sy *", "3032 folder=3030 * code=sx *"),
                ]
            ) with
            {
                CarriesAcrossChunks = true,
            },
            // (e) 後ろの塊の行が手放す一意な値を、前の塊の行が取る：サーバーで 3041 が code を手放し、新しい 3043 がその値を取り、
            // 3041 をもう一度書き直す（3041 の版が 3043 より新しくなる）。取得の順は 詰め物 3042・3043・3041 で、塊は [3042, 3043] と
            // [3041]。3043 は端末の 3041 がまだ値を持つので持ち越され、3041 の塊の後の最後の段で入る
            Moved(
                "chunk-taker-before-releaser",
                [Folder(SV, 3040), Item(SV, 3041, folder: 3040, code: "taken")],
                [
                    Item(SV, 3042, folder: 3040),
                    new UpdateItemOp(SV, Direct, 3041, 3040, null, "released", "t3041"),
                    Item(SV, 3043, folder: 3040, code: "taken"),
                    new UpdateItemOp(SV, Direct, 3041, 3040, null, "released", "again"),
                    Plain(D1),
                    Expect.Result(D1).Absent("truncation *", "conflict *"),
                    Expect
                        .Rows(D1, I)
                        .Contains(
                            "3041 folder=3040 * code=released title=again",
                            "3043 folder=3040 * code=taken *"
                        ),
                ]
            ) with
            {
                CarriesAcrossChunks = true,
            },
            // (f) 削除の段で残る親：端末に親 3050 と子 3052 がある。サーバーで 3052 を 3051 へ付け替えてから 3050 を消す（フォルダーの
            // 詰め物 2 つで、品目の表の取得の合計が 2 を超え、品目の表で塊の経路へ入る）。1 回で全部の道は同じ適用で付け替えと削除を
            // 流すので 3050 はその同期で消える。塊の経路の削除の段は付け替えより前に流れ、端末の 3052 がまだ 3050 を指すので 3050 を残す
            // （SyncModelSmallChunkScenarioDifferences）。どちらの道でも、次の同期の後には 3050 が無い
            Moved(
                "chunk-delete-stage-parent-gone-after-next-sync",
                [Folder(SV, 3050), Folder(SV, 3051), Item(SV, 3052, folder: 3050)],
                [
                    Folder(SV, 3053),
                    Folder(SV, 3054),
                    new UpdateItemOp(SV, Direct, 3052, 3051, null, "c-3052", "moved"),
                    new DeleteFolderOp(SV, Direct, 3050),
                    Plain(D1),
                    Expect.Result(D1).Absent("truncation *", "conflict *"),
                    // 1 回で全部の道だけが通る期待（塊の道はこの期待だけが違い、署名 5/expect-rows:model_folders で控えと違う）
                    Expect.Rows(D1, F).Absent("3050 *"),
                    Expect.Rows(D1, I).Contains("3052 folder=3051 *"),
                    Plain(D1),
                    new ExpectLocalOp(D1, F, "3050", null),
                    Expect.Rows(D1, I).Contains("3052 folder=3051 *"),
                ]
            ),
        ];
}
