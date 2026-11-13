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
}
