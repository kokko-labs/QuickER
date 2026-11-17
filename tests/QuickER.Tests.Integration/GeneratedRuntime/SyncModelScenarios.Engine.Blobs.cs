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
                    Expect.Journal(D1).Empty(),
                    new WritePayloadOp(D1, 11, "0402"),
                    Expect.Journal(D1).Exact($"{I} key=11 op=Upsert * blobs=Payload *"),
                    Plain(D1),
                    Expect.Result(D1).Contains("uploaded=1").Absent("conflict *"),
                    new ExpectPayloadOp(SV, 11, "0402"),
                    Expect.Journal(D1).Empty(),
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
                    Expect.Journal(D1).Empty(),
                ]
            ),
            Moved(
                "blob-after-hook-write-with-other-key-type-uses-row-key",
                [.. Seed(1, "alice", 11, "widget"), .. Seed(2, "dave", 21, "gadget")],
                [
                    new ArmHookBlobWriteOp(D1, 21, "0404", KeyAsInt64: true),
                    new UpdateItemOp(D1, ModelWriteVia.Graph, 11, 1, null, "c-11", "bob"),
                    Expect.Journal(D1).Contains($"{I} key=21 *"),
                    Plain(D1),
                    new ExpectPayloadOp(SV, 21, "0404"),
                    Expect.Journal(D1).Empty(),
                ]
            ),
            Moved(
                "hook-download-skip-stops-table-and-retries",
                [],
                [
                    Folder(SV, 1, name: "alice"),
                    Folder(SV, 2, name: "bob"),
                    Folder(SV, 3, name: "carol"),
                    new ArmApplySkipOp(D1, F, "2"),
                    Plain(D1),
                    Expect
                        .Result(D1)
                        .Single($"truncation {F} *")
                        .Contains($"truncation {F} pending=2 missing-parent=- skipped-by-hook=1 *"),
                    Expect.Rows(D1, F).Contains("1 *").Absent("2 *", "3 *"),
                    Plain(D1),
                    Expect.Result(D1).Absent("truncation *"),
                    Expect
                        .Rows(D1, F)
                        .Contains("2 parent=null name=bob", "3 parent=null name=carol"),
                ]
            ),
            Moved(
                "hook-parent-read-back-skip-stops-child-table",
                [],
                [
                    .. Seed(2, "bob", 12, "gadget"),
                    new ArmFaultOp(D1, ModelFaultKind.ApplySkip),
                    new ArmApplySkipOp(D1, F, "2"),
                    Plain(D1),
                    Expect
                        .Result(D1)
                        .Single($"truncation {F} *", $"truncation {I} *")
                        .Contains(
                            $"truncation {F} * skipped-by-hook=1 *",
                            $"truncation {I} pending=12 missing-parent={F} *"
                        ),
                    Expect.Rows(D1, I).Absent("12 *"),
                    Plain(D1),
                    Expect.Result(D1).Absent("truncation *"),
                    Expect.Rows(D1, I).Contains("12 *"),
                ]
            ),
            Moved(
                "hook-serverwins-read-back-skip-keeps-entry",
                [Folder(SV, 1, name: "alice")],
                [
                    Rename(D1, 1, "local"),
                    Rename(SV, 1, "server"),
                    new ArmFaultOp(D1, ModelFaultKind.ApplySkip),
                    new ArmApplySkipOp(D1, F, "1"),
                    Plain(D1, policy: SyncConflictPolicy.ServerWins),
                    Expect.Journal(D1).Count("*", 1),
                    Expect.Rows(D1, F).Contains("1 parent=null name=local"),
                    Plain(D1, policy: SyncConflictPolicy.ServerWins),
                    Expect.Journal(D1).Empty(),
                    Expect.Rows(D1, F).Contains("1 parent=null name=server"),
                ]
            ),
            Moved(
                "hook-versionless-download-skip-stops-table",
                [],
                [
                    Folder(SV, 1, name: "alice"),
                    new AddLabelOp(SV, Direct, "L1", 1, "first"),
                    new AddLabelOp(SV, Direct, "L2", 1, "second"),
                    new AddLabelOp(SV, Direct, "L3", 1, "third"),
                    new ArmApplySkipOp(D1, L, "L2"),
                    Plain(D1, SyncMode.LastWriteWins, batch: 1),
                    Expect
                        .Result(D1)
                        .Single($"truncation {L} *")
                        .Contains($"truncation {L} pending=L2 * skipped-by-hook=1 *"),
                    Expect.Rows(D1, L).Contains("L1 *").Absent("L2 *", "L3 *"),
                    Plain(D1, SyncMode.LastWriteWins, batch: 1),
                    Expect.Result(D1).Absent("truncation *"),
                    Expect.Rows(D1, L).Contains("L3 folder=1 caption=third"),
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
                    Expect.Journal(D1).Empty(),
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
                    Expect.Journal(D1).Exact($"{I} key=11 op=Upsert *"),
                ]
            ),
            Moved(
                "blob-inserted-row-blob-is-recorded-and-uploaded",
                [],
                [
                    new AddItemOp(D1, Direct, 6, 999, null, "c-6", "erin", "0606"),
                    Expect.Journal(D1).Exact($"{I} key=6 op=Upsert * blobs=Payload *"),
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
                    Expect.BlobPending(D1, I).Exact("key=11 col=Payload"),
                    Plain(D1),
                    new ExpectPayloadOp(D1, 11, "010203"),
                    Expect.BlobPending(D1, I).Empty(),
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
            // エンジン（適用が全部か無しか）では違反になる形なので、そのエンジンではこの行の期待を「競合として報告される」へ書き直す
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
                    Expect
                        .Result(D1)
                        .Contains("exception *")
                        .Contains("local-change model_folders key=1 reason=DiscardedByServerWins")
                        .Absent($"local-change {I} key=11 *"),
                    Expect.Rows(D1, I).Contains("11 folder=1 label=null code=c-11 title=widget"),
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
                    Expect.Rows(SV, I).Contains("11 folder=1 label=null code=c-11 title=widget"),
                ]
            ),
            // 持ち越しを宣言しないエンジンでは、例外で終わった実行の解決役が持ち込んだ行の削除が、サーバーに一度も届いていない行の
            // 削除として黙って捨てられ、後の同期がサーバーの行を端末へ戻す（照合 3 の数えるだけの種類）。持ち越しを宣言するエンジン
            // （適用が全部か無しか）では違反になる形なので、そのエンジンの受け入れでこの行の期待を書き直す。
            // 品目の表のダウンロードは、Versioned が触れない版なしの表（ラベル）を参照する品目 21 で打ち切られ、イベントの
            // ダウンロードの親の解決役が品目 22 をサーバーから読み直して書く。その blob のコピーで例外になると、品目 22 はミラー版
            // も受理記録も無い行として残る
            Moved(
                "thrown-resolver-row-delete-is-dropped-and-row-comes-back",
                [.. Seed(1, "alice", 11, "widget")],
                [
                    new AddLabelOp(SV, Direct, "L9", 1, "server-only"),
                    new AddItemOp(SV, Direct, 21, 1, "L9", "c-21", "labelled", "0A0B0015"),
                    Line(SV, 22, 1, "resolved"),
                    new AddEventOp(SV, Direct, ResolverEventAt, 22, "needs-22"),
                    new ArmFaultOp(D1, ModelFaultKind.BlobDownload),
                    new ExpectThrowOp(D1),
                    Plain(D1),
                    Expect.Result(D1).Contains("exception *"),
                    Expect.Rows(D1, I).Contains("22 folder=1 label=null code=c-22 title=resolved"),
                    Expect.Rows(D1, I).Absent("21 *"),
                    Expect.Mirror(D1, I).Contains("key=22 mirror=null"),
                    Expect.Acks(D1, I).Absent("key=22 *"),
                    new DeleteItemOp(D1, Direct, 22),
                    Plain(D1),
                    Expect.Result(D1).Absent("conflict *", "local-change *"),
                    Expect.Rows(D1, I).Contains("22 folder=1 label=null code=c-22 title=resolved"),
                    Expect.Rows(SV, I).Contains("22 folder=1 label=null code=c-22 title=resolved"),
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
                    new LocalSqlOp(D1, $"UPDATE {I} SET payload = X'030303' WHERE item_id = 11"),
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
                    new LocalSqlOp(D1, $"UPDATE {I} SET payload = X'030303' WHERE item_id = 11"),
                    new RefreshWithOptionsOp(D1, SyncMode.LastWriteWins, false, true, false, []),
                    Expect.Result(D1).Contains("inserted=5"),
                    Expect.Rows(D1, F).Contains("1 parent=null name=alice"),
                    new ExpectPayloadOp(D1, 11, ""),
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

    /// <summary>解決役が親の品目を読み直すイベントの時刻（thrown-resolver-row-delete-is-dropped-and-row-comes-back）</summary>
    private static DateTime ResolverEventAt => new(2026, 6, 2, 9, 0, 0, DateTimeKind.Unspecified);
}
