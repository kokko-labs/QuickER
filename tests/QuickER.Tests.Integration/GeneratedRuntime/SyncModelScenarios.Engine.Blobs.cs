using System.Collections.Generic;
using QuickER.Tests.GeneratedSyncModelFixture;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// SyncRuntimeTestsBase から移したシナリオ（blob の運び方・blob を書く After フック・適用でローカルの Before フックが
/// 行を飛ばす形）。表の写し方は <see cref="EngineBasicScenarios"/> の型の注と同じで、注文の blob（NULL 許容）は品目の
/// payload（NOT NULL）へ写す＝blob の無い行は空の payload。blob を運ばない回を使うシナリオは、静止化の前に端末を
/// blob を運ぶ洗い替えでそろえ直す（文書の割り切り O1）。
/// </summary>
internal static partial class SyncModelScenarios
{
    private static IEnumerable<SyncModelScenario> EngineBlobsScenarios() =>
        [
            Moved(
                "blob-include-uploads-local-blob",
                [],
                [
                    Line(D1, 5, 999, "dave"),
                    new WritePayloadOp(D1, 5, "090807"),
                    Plain(D1),
                    Expect.Result(D1).Absent("conflict *"),
                    new ExpectPayloadOp(SV, 5, "090807"),
                ]
            ),
            Moved(
                "blob-only-edit-is-journaled-and-uploaded",
                [.. Seed(1, "alice", 11, "widget")],
                [
                    Expect.Pending(D1).Empty(),
                    new WritePayloadOp(D1, 11, "0402"),
                    Expect.Pending(D1).Exact($"{I} key=11 row=present"),
                    Plain(D1),
                    Expect.Result(D1).Contains("uploaded=1").Absent("conflict *"),
                    new ExpectPayloadOp(SV, 11, "0402"),
                    Expect.Pending(D1).Empty(),
                ]
            ),
            Moved(
                "blob-edit-without-local-blob-keeps-server-blob",
                [],
                [
                    .. Seed(1, "alice", 11, "widget"),
                    new WritePayloadOp(SV, 11, "010203"),
                    Sync(D1, blobs: false),
                    new ExpectPayloadOp(D1, 11, ""),
                    new UpdateItemOp(D1, Direct, 11, 1, null, "c-11", "widget-local"),
                    Plain(D1),
                    Expect.Result(D1).Contains("uploaded=1"),
                    Expect
                        .Rows(SV, I)
                        .Contains("11 folder=1 label=null code=c-11 title=widget-local"),
                    new ExpectPayloadOp(SV, 11, "010203"),
                    new ModelRefreshOp(D1, SyncMode.LastWriteWins, IncludeBlobs: true),
                ]
            ),
            Moved(
                "blob-after-hook-write-on-update-is-uploaded",
                [.. Seed(1, "alice", 11, "widget")],
                [
                    new WritePayloadOp(SV, 11, "01"),
                    Plain(D1),
                    new ExpectPayloadOp(D1, 11, "01"),
                    new ArmHookBlobWriteOp(D1, null, "0707"),
                    new UpdateItemOp(D1, ModelWriteVia.Graph, 11, 1, null, "c-11", "bob"),
                    Expect.Hook(D1).Exact("blob-write key=11 returned=true"),
                    new ExpectPayloadOp(D1, 11, "0707"),
                    Plain(D1),
                    new ExpectPayloadOp(SV, 11, "0707"),
                    new UpdateItemOp(SV, Direct, 11, 1, null, "c-11", "carol"),
                    Plain(D1),
                    new ExpectPayloadOp(D1, 11, "0707"),
                    Expect.Rows(D1, I).Contains("11 folder=1 label=null code=c-11 title=carol"),
                ]
            ),
            Moved(
                "blob-after-hook-write-on-another-row-is-uploaded",
                [.. Seed(1, "alice", 11, "widget"), .. Seed(2, "dave", 21, "gadget")],
                [
                    new ArmHookBlobWriteOp(D1, 21, "0505"),
                    new UpdateItemOp(D1, ModelWriteVia.Graph, 11, 1, null, "c-11", "bob"),
                    Plain(D1),
                    new ExpectPayloadOp(SV, 21, "0505"),
                    Expect.Rows(SV, I).Contains("21 folder=2 label=null code=c-21 title=gadget"),
                    Expect.Pending(D1).Empty(),
                ]
            ),
            Moved(
                "blob-after-hook-write-with-other-key-type-uses-row-key",
                [.. Seed(1, "alice", 11, "widget"), .. Seed(2, "dave", 21, "gadget")],
                [
                    new ArmHookBlobWriteOp(D1, 21, "0404", KeyAsInt64: true),
                    new UpdateItemOp(D1, ModelWriteVia.Graph, 11, 1, null, "c-11", "bob"),
                    Expect.Pending(D1).Contains($"{I} key=21 row=present"),
                    Plain(D1),
                    new ExpectPayloadOp(SV, 21, "0404"),
                    Expect.Pending(D1).Empty(),
                ]
            ),
            Moved(
                "blob-after-hook-write-rolled-back-with-failed-save",
                [.. Seed(1, "alice", 11, "widget")],
                [
                    new WritePayloadOp(SV, 11, "01"),
                    Plain(D1),
                    new ArmHookBlobWriteOp(D1, null, "0707", ThrowAfter: true),
                    new UpdateItemOp(D1, ModelWriteVia.Graph, 11, 1, null, "c-11", "bob")
                    {
                        ExpectFailure = true,
                    },
                    Expect.Pending(D1).Empty(),
                    new ExpectPayloadOp(D1, 11, "01"),
                    Plain(D1),
                    new ExpectPayloadOp(SV, 11, "01"),
                ]
            ),
            Moved(
                "blob-after-hook-write-matching-no-row-is-not-recorded",
                [.. Seed(1, "alice", 11, "widget")],
                [
                    new ArmHookBlobWriteOp(D1, 99, "0505"),
                    new UpdateItemOp(D1, ModelWriteVia.Graph, 11, 1, null, "c-11", "bob"),
                    Expect.Hook(D1).Exact("blob-write key=99 returned=false"),
                    Expect.Pending(D1).Exact($"{I} key=11 row=present"),
                ]
            ),
            Moved(
                "blob-inserted-row-blob-is-recorded-and-uploaded",
                [],
                [
                    new AddItemOp(D1, Direct, 6, 999, null, "c-6", "erin", "0606"),
                    Expect.Pending(D1).Exact($"{I} key=6 row=present"),
                    Plain(D1),
                    new ExpectPayloadOp(SV, 6, "0606"),
                ]
            ),
            Moved(
                "blob-failed-download-copy-is-retried",
                [],
                [
                    .. Seed(1, "alice", 11, "widget"),
                    new WritePayloadOp(SV, 11, "010203"),
                    new ArmFaultOp(D1, ModelFaultKind.BlobDownload),
                    new ExpectThrowOp(D1),
                    Plain(D1),
                    Expect.Result(D1).Contains("exception *"),
                    Expect.Rows(D1, I).Contains("11 *"),
                    Plain(D1),
                    new ExpectPayloadOp(D1, 11, "010203"),
                ]
            ),
            Moved(
                "blob-failed-refresh-copy-is-retried",
                [],
                [
                    .. Seed(1, "alice", 11, "widget"),
                    new WritePayloadOp(SV, 11, "0405"),
                    new ArmFaultOp(D1, ModelFaultKind.BlobDownload),
                    new ExpectThrowOp(D1),
                    RefreshAll(D1),
                    Expect.Result(D1).Contains("exception *"),
                    Plain(D1),
                    new ExpectPayloadOp(D1, 11, "0405"),
                ]
            ),
            Moved(
                "blob-owed-copy-keeps-unsent-local-blob",
                [],
                [
                    .. Seed(1, "alice", 11, "widget"),
                    new WritePayloadOp(SV, 11, "010203"),
                    new ArmFaultOp(D1, ModelFaultKind.BlobDownload),
                    new ExpectThrowOp(D1),
                    Plain(D1),
                    Expect.Result(D1).Contains("exception *"),
                    new WritePayloadOp(D1, 11, "0909"),
                    PlainOnly(D1, ModelSyncKind.Download),
                    new ExpectPayloadOp(D1, 11, "0909"),
                    Plain(D1),
                    new ExpectPayloadOp(SV, 11, "0909"),
                ]
            ),
            // 例外で終わった ServerWins の同期の後、ServerWins でない同期が、端末の古い blob でサーバーの他者の新しい blob を
            // 競合なしに上書きする。持ち越しを宣言しないエンジンでは数えるだけ（照合 5 の数えるだけの種類）。持ち越しを宣言する
            // エンジン（適用が全部か無しか）では、捨てた記録と行の書き込みが同じ適用でコミットされ、例外の一覧に品目 11 が載り、
            // 次の同期は写し待ちの blob を降ろす（サーバーの新しい blob を上書きしない）
            Moved(
                "blob-thrown-serverwins-then-collect-overwrites-newer-server-blob",
                [.. Seed(1, "alice", 11, "widget")],
                [
                    new WritePayloadOp(D1, 11, "0D0D"),
                    new UpdateItemOp(SV, Direct, 11, 1, null, "c-11", "server-new"),
                    new WritePayloadOp(SV, 11, "0E0E"),
                    new ArmFaultOp(D1, ModelFaultKind.BlobDownload),
                    new ExpectThrowOp(D1),
                    Plain(D1, policy: SyncConflictPolicy.ServerWins),
                    new WhenEngineDeclaresOp(
                        D1,
                        EngineDeclaration.CarriesServerWinsDecision,
                        [
                            Expect
                                .Result(D1)
                                .Contains("exception *")
                                .Contains($"local-change {I} key=11 reason=DiscardedByServerWins"),
                            Expect
                                .Rows(D1, I)
                                .Contains("11 folder=1 label=null code=c-11 title=server-new"),
                            new ExpectPayloadOp(D1, 11, "0D0D"),
                            Plain(D1),
                            Expect.Result(D1).Absent("conflict *"),
                            new ExpectPayloadOp(SV, 11, "0E0E"),
                            new ExpectPayloadOp(D1, 11, "0E0E"),
                        ],
                        [
                            Expect.Result(D1).Contains("exception *"),
                            Expect
                                .Rows(D1, I)
                                .Contains("11 folder=1 label=null code=c-11 title=server-new"),
                            new ExpectPayloadOp(D1, 11, "0D0D"),
                            Plain(D1),
                            Expect.Result(D1).Absent("conflict *"),
                            new ExpectPayloadOp(SV, 11, "0D0D"),
                        ]
                    ),
                ]
            ),
            // 持ち越しを宣言しないエンジンでは、例外で終わった ServerWins の同期が戻した行が、例外の LocalRowChanges に載らない
            // （照合 3 の数えるだけの種類）。持ち越しを宣言するエンジン（適用が全部か無しか）では、戻した行が一覧に載る（または
            // 戻らない）形へ期待を書き直す。
            // この行の窓は、表のダウンロードの直後に走る ServerWins の決着（捨てる記録の行をサーバーから読み直して書く）：行を書いた
            // 後の blob のコピーで例外になると、記録を捨てる前・一覧に載せる前に止まる。もう一方の窓はダウンロードの側で、捨てる
            // 未送信を持つ行のサーバーの版が進んでいるとき、ダウンロードがその行を書いた後の blob のコピーで例外になると、決着の前に
            // 止まる（行は戻り、記録は残り、一覧に載らない）。フォルダーの編集は、例外が一覧を運ぶ（先に捨てた行が一覧にある）ように
            // するため
            Moved(
                "thrown-serverwins-sync-leaves-restored-row-out-of-local-row-changes",
                [.. Seed(1, "alice", 11, "widget")],
                [
                    new UpdateFolderOp(D1, Direct, 1, null, "alice-local"),
                    new DeleteItemOp(D1, Direct, 11),
                    new ArmFaultOp(D1, ModelFaultKind.BlobDownload),
                    new ExpectThrowOp(D1),
                    Plain(D1, policy: SyncConflictPolicy.ServerWins),
                    // 持ち越しを宣言するエンジンは、戻した 2 行を同じ適用でコミットしてから blob を写すので、例外の一覧に両方が載る
                    new WhenEngineDeclaresOp(
                        D1,
                        EngineDeclaration.CarriesServerWinsDecision,
                        [
                            Expect
                                .Result(D1)
                                .Contains("exception *")
                                .Contains(
                                    "local-change model_folders key=1 reason=DiscardedByServerWins"
                                )
                                .Contains($"local-change {I} key=11 reason=DiscardedByServerWins"),
                            Expect.Rows(D1, F).Contains("1 parent=null name=alice"),
                            Expect
                                .Rows(D1, I)
                                .Contains("11 folder=1 label=null code=c-11 title=widget"),
                        ],
                        [
                            Expect
                                .Result(D1)
                                .Contains("exception *")
                                .Contains(
                                    "local-change model_folders key=1 reason=DiscardedByServerWins"
                                )
                                .Absent($"local-change {I} key=11 *"),
                            Expect
                                .Rows(D1, I)
                                .Contains("11 folder=1 label=null code=c-11 title=widget"),
                        ]
                    ),
                    Plain(D1),
                    Expect.Rows(D1, I).Contains("11 *"),
                    Expect.Rows(SV, I).Contains("11 *"),
                ]
            ),
            // 持ち越しを宣言しないエンジンでは、例外で終わった ServerWins の実行が戻した行の記録が残り、後の実行がその行を端末の
            // 編集として送る。持ち越しを宣言するエンジン（適用が全部か無しか）では起きない形なので、そのエンジンでは期待を書き直す。
            // 単独の UploadAsync の ServerWins が捨てる記録の行をサーバーから読み直して書いた後、その行の blob のコピーで例外に
            // なる。後勝ちの同期は、残った記録で戻した古い写しを送り、サーバーの他者の更新を上書きする（照合 1 の数えるだけの種類）
            Moved(
                "thrown-serverwins-upload-then-lww-sends-restored-row-as-edit",
                [.. Seed(1, "alice", 11, "widget")],
                [
                    new UpdateItemOp(D1, Direct, 11, 1, null, "c-11", "widget-local"),
                    new ArmFaultOp(D1, ModelFaultKind.BlobDownload),
                    new ExpectThrowOp(D1),
                    PlainOnly(D1, ModelSyncKind.Upload, policy: SyncConflictPolicy.ServerWins),
                    Expect.Result(D1).Contains("exception *"),
                    Expect.Rows(D1, I).Contains("11 folder=1 label=null code=c-11 title=widget"),
                    new UpdateItemOp(SV, Direct, 11, 1, null, "c-11", "server-new"),
                    Plain(D1, SyncMode.LastWriteWins),
                    // 持ち越しを宣言するエンジンは、捨てた記録と戻した行を同じ適用でコミットするので、後勝ちの同期は何も送らず、
                    // サーバーの他者の更新を降ろす
                    new WhenEngineDeclaresOp(
                        D1,
                        EngineDeclaration.CarriesServerWinsDecision,
                        [
                            Expect.Result(D1).Contains("uploaded=0").Absent("conflict *"),
                            Expect
                                .Rows(SV, I)
                                .Contains("11 folder=1 label=null code=c-11 title=server-new"),
                            Expect
                                .Rows(D1, I)
                                .Contains("11 folder=1 label=null code=c-11 title=server-new"),
                        ],
                        [
                            Expect
                                .Rows(SV, I)
                                .Contains("11 folder=1 label=null code=c-11 title=widget"),
                        ]
                    ),
                ]
            ),
            // 端末が作った品目（サーバーは知らない）の記録を、削除伝搬なしの Versioned × ServerWins が捨てたと報告し、行は端末に
            // 残る。その後に blob だけを書くと、次の同期は行を blob と一緒にサーバーへ上げる（後の書き込みが blob だけでも、
            // 捨てた決定の後の端末の編集として送る）
            Moved(
                "blob-only-edit-after-serverwins-discarded-unknown-insert-uploads-row-and-blob",
                [.. Seed(1, "alice", 11, "widget")],
                [
                    Line(D1, 41, 1, "local-41"),
                    Plain(D1, policy: SyncConflictPolicy.ServerWins, propagateDeletes: false),
                    Expect
                        .Result(D1)
                        .Contains($"local-change {I} key=41 reason=DiscardedByServerWins"),
                    Expect.Rows(SV, I).Absent("41 *"),
                    Expect.Rows(D1, I).Contains("41 folder=1 label=null code=c-41 title=local-41"),
                    new WritePayloadOp(D1, 41, "0D41"),
                    Plain(D1),
                    Expect.Result(D1).Absent("conflict *"),
                    Expect.Rows(SV, I).Contains("41 folder=1 label=null code=c-41 title=local-41"),
                    new ExpectPayloadOp(SV, 41, "0D41"),
                    new ExpectPayloadOp(D1, 41, "0D41"),
                ]
            ),
            Moved(
                "blob-owed-copy-made-for-ordinary-edit",
                [],
                [
                    .. Seed(1, "alice", 11, "widget"),
                    new WritePayloadOp(SV, 11, "010203"),
                    new ArmFaultOp(D1, ModelFaultKind.BlobDownload),
                    new ExpectThrowOp(D1),
                    Plain(D1),
                    Expect.Result(D1).Contains("exception *"),
                    new UpdateItemOp(D1, Direct, 11, 1, null, "c-11", "widget-local"),
                    PlainOnly(D1, ModelSyncKind.Download),
                    new ExpectPayloadOp(D1, 11, "010203"),
                    Expect
                        .Rows(D1, I)
                        .Contains("11 folder=1 label=null code=c-11 title=widget-local"),
                ]
            ),
            Moved(
                "blob-failed-upload-no-false-conflict",
                [.. Seed(1, "alice", 11, "widget")],
                [
                    new UpdateItemOp(D1, Direct, 11, 1, null, "c-11", "widget-local"),
                    new WritePayloadOp(D1, 11, "0707"),
                    new ArmFaultOp(D1, ModelFaultKind.BlobUpload),
                    new ExpectThrowOp(D1),
                    Plain(D1),
                    Expect.Result(D1).Contains("exception *"),
                    Plain(D1),
                    Expect.Result(D1).Contains("uploaded=1").Absent("conflict *"),
                    Expect
                        .Rows(SV, I)
                        .Contains("11 folder=1 label=null code=c-11 title=widget-local"),
                    new ExpectPayloadOp(SV, 11, "0707"),
                ]
            ),
            Moved(
                "blob-uploaded-blob-does-not-echo",
                [],
                [
                    Line(D1, 5, 999, "dave"),
                    new WritePayloadOp(D1, 5, "090807"),
                    Plain(D1),
                    Expect.Result(D1).Contains("downloaded=0"),
                    Plain(D1),
                    Expect.Result(D1).Contains("downloaded=0", "uploaded=0"),
                    new ExpectPayloadOp(SV, 5, "090807"),
                    new ExpectPayloadOp(D1, 5, "090807"),
                ]
            ),
            Moved(
                "blob-refresh-refuses-when-blobs-would-be-lost",
                [],
                [
                    .. Seed(1, "alice", 11, "widget"),
                    Sync(D1, blobs: false),
                    new LocalSqlOp(D1, $"UPDATE {I} SET payload = X'030303' WHERE item_id = 11")
                    {
                        Untracked = true,
                    },
                    new ExpectThrowOp(D1),
                    RefreshWithOptionsOp.Defaults(D1),
                    Expect
                        .Result(D1)
                        .Contains(
                            "exception SyncUnboundedBinaryLossException: *Payload*",
                            $"blob-loss {I} cols=(Payload)"
                        )
                        .Count("blob-loss *", 1),
                    Expect.Rows(D1, I).Exact("11 *", "999 *"),
                    new ExpectPayloadOp(D1, 11, "030303"),
                    new ModelRefreshOp(D1, SyncMode.LastWriteWins, IncludeBlobs: true),
                ]
            ),
            Moved(
                "blob-refresh-discard-drops-local-blobs",
                [],
                [
                    .. Seed(1, "alice", 11, "widget"),
                    Sync(D1, blobs: false),
                    new LocalSqlOp(D1, $"UPDATE {I} SET payload = X'030303' WHERE item_id = 11")
                    {
                        Untracked = true,
                    },
                    new RefreshWithOptionsOp(D1, SyncMode.LastWriteWins, false, true, false, []),
                    Expect.Result(D1).Contains("inserted=5"),
                    Expect.Rows(D1, F).Contains("1 parent=null name=alice"),
                    new ExpectPayloadOp(D1, 11, ""),
                    new ModelRefreshOp(D1, SyncMode.LastWriteWins, IncludeBlobs: true),
                ]
            ),
            // blob を捨てる洗い替えの後、端末の blob は空で、次の blob を運ぶ同期はその空の blob をサーバーへ送り返さない
            // （洗い替えが空にした列は端末の書き込みでないので、送る列に載らない）
            Moved(
                "blob-refresh-discard-then-sync-does-not-send-empty-blob-back",
                [],
                [
                    .. Seed(1, "alice", 11, "widget"),
                    new WritePayloadOp(SV, 11, "0B0B11"),
                    Sync(D1, blobs: true),
                    new ExpectPayloadOp(D1, 11, "0B0B11"),
                    new RefreshWithOptionsOp(D1, SyncMode.LastWriteWins, false, true, false, []),
                    new ExpectPayloadOp(D1, 11, ""),
                    Sync(D1, blobs: true),
                    Expect.Result(D1).Contains("uploaded=0").Absent("conflict *"),
                    new ExpectPayloadOp(SV, 11, "0B0B11"),
                    new ModelRefreshOp(D1, SyncMode.LastWriteWins, IncludeBlobs: true),
                    new ExpectPayloadOp(D1, 11, "0B0B11"),
                ]
            ),
            // blob を捨てる洗い替えで、取得の途中にサーバーの他者が新しいフォルダーを作って品目を移す。取得の上限を固定しない
            // サーバー役では、品目の行は親の無い行になる（洗い替えが取り直すか、親を読み直して書く）。取得の上限を実行の始めに
            // 固定するサーバー役では、移した版はその洗い替えに入らず（端末に移した後の行は無い）、次の同期で降りる。どの経路で
            // 書いた行も、端末の blob は空（次の blob を運ばない同期の後も空）
            Moved(
                "blob-refresh-discard-empties-blob-of-row-written-after-retry",
                [Folder(SV, 830), Item(SV, 831, folder: 830)],
                [
                    Sync(D1, blobs: true),
                    new ExpectPayloadOp(D1, 831, "0A0B033F"),
                    new ArmInterruptOp(
                        D1,
                        ModelInterruptPoint.AfterServerFetchBeforeApply,
                        [
                            Folder(SV, 832),
                            new UpdateItemOp(SV, Direct, 831, 832, null, "c-831", "t831"),
                        ]
                    ),
                    new RefreshWithOptionsOp(D1, SyncMode.LastWriteWins, false, true, false, []),
                    Expect.Result(D1).Absent("exception *"),
                    new WhenServerFixesCeilingOp(
                        [Expect.Rows(D1, I).Absent("831 folder=832 *")],
                        [
                            Expect.Rows(D1, I).Contains("831 folder=832 *"),
                            new ExpectPayloadOp(D1, 831, ""),
                        ]
                    ),
                    Sync(D1, blobs: false),
                    Expect.Rows(D1, I).Contains("831 folder=832 *"),
                    new ExpectPayloadOp(D1, 831, ""),
                    new ModelRefreshOp(D1, SyncMode.LastWriteWins, IncludeBlobs: true),
                ]
            ),
            // blob を捨てる洗い替えで、取得の直前にサーバーの他者が品目の内容を変える。取得の上限を実行の始めに固定するサーバー役
            // では、変わった行はその洗い替えに入らず（端末にその内容は無い）、次の同期で内容が降りる。固定しないサーバー役では
            // 洗い替えで内容が降りる。どちらでも、端末にその品目の blob は残らない（次の blob を運ばない同期の後も空）
            Moved(
                "blob-refresh-discard-leaves-no-blob-on-row-changed-after-ceiling",
                [Folder(SV, 840), Item(SV, 841, folder: 840)],
                [
                    Sync(D1, blobs: true),
                    new ExpectPayloadOp(D1, 841, "0A0B0349"),
                    new ArmInterruptOp(
                        D1,
                        ModelInterruptPoint.BeforeServerFetch,
                        new UpdateItemOp(SV, Direct, 841, 840, null, "c-841", "t841-after-ceiling")
                    ),
                    new RefreshWithOptionsOp(D1, SyncMode.LastWriteWins, false, true, false, []),
                    Expect.Result(D1).Absent("exception *"),
                    new WhenServerFixesCeilingOp(
                        [Expect.Rows(D1, I).Absent("841 * title=t841-after-ceiling")],
                        [
                            Expect
                                .Rows(D1, I)
                                .Contains("841 folder=840 * title=t841-after-ceiling"),
                            new ExpectPayloadOp(D1, 841, ""),
                        ]
                    ),
                    Sync(D1, blobs: false),
                    Expect.Rows(D1, I).Contains("841 folder=840 * title=t841-after-ceiling"),
                    new ExpectPayloadOp(D1, 841, ""),
                    new ModelRefreshOp(D1, SyncMode.LastWriteWins, IncludeBlobs: true),
                ]
            ),
            Moved(
                "blob-refresh-include-reloads-blobs",
                [],
                [
                    .. Seed(1, "alice", 11, "widget"),
                    new WritePayloadOp(SV, 11, "010203"),
                    Folder(SV, 2, name: "bob"),
                    new AddItemOp(SV, Direct, 12, 2, null, "c-12", "gadget", ""),
                    RefreshAll(D1),
                    Expect.Result(D1).Contains("inserted=7"),
                    new ExpectPayloadOp(D1, 11, "010203"),
                    new ExpectPayloadOp(D1, 12, ""),
                    Plain(D1),
                    Expect.Result(D1).Contains("downloaded=0"),
                    new WritePayloadOp(SV, 12, "0C"),
                ]
            ),
        ];
}
