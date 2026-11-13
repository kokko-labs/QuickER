using System.Collections.Generic;
using QuickER.Tests.GeneratedSyncModelFixture;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// SyncRuntimeTestsBase（SyncFixture の図）から移したシナリオ。表は次のように写す（対応表 p3-2-mapping が持つ）：
/// sync_orders → model_folders（版ありの親）、sync_order_lines → model_items（版ありの子）、sync_notes → model_labels
/// （版なしの子）、sync_orders.attachment → model_items.payload。同期は blob を運ぶ（<see cref="Plain"/>）：
/// 元のテストの既定は運ばないが、明細の表は blob を持たず、注文の blob は書いていない（運んでも元の表明は変わらない）。
/// モデルの品目は NOT NULL の blob を持ち、運ぶかを端末の中で切り替えると文書の既知の割り切り（O1＝切り替えたら洗い替え）
/// に当たって収束しないため、端末ごとに運ぶ側へそろえる。転送はすべて（<see cref="Moved"/>）。
/// </summary>
internal static partial class SyncModelScenarios
{
    private const string F = ModelTables.Folders;
    private const string I = ModelTables.Items;
    private const string L = ModelTables.Labels;

    /// <summary>
    /// 移したシナリオ（すべての転送で流す。元の SyncSqlServerRuntimeTests が守っていた本物の rowversion・
    /// MIN_ACTIVE_ROWVERSION() の上限・外部キーエラーの分類・TOP のページングは、実 SQL Server の転送でも期待の手で表明する）
    /// </summary>
    private static SyncModelScenario Moved(
        string name,
        IReadOnlyList<ModelWriteOp> setup,
        IReadOnlyList<ModelOp> ops
    ) => new(name, setup, ops);

    /// <summary>元のテストの既定の同期（blob は運ぶ＝型の注を見る）</summary>
    private static ModelSyncOp Plain(
        ModelActor device,
        SyncMode mode = SyncMode.Versioned,
        SyncConflictPolicy policy = SyncConflictPolicy.Collect,
        IReadOnlyList<string>? excluded = null,
        int batch = 500,
        bool propagateDeletes = true
    ) => Sync(device, mode, policy, excluded, batch, propagateDeletes);

    /// <summary>元のテストの既定の UploadAsync / DownloadAsync だけ（blob は運ぶ）</summary>
    private static ModelSyncOp PlainOnly(
        ModelActor device,
        ModelSyncKind kind,
        SyncConflictPolicy policy = SyncConflictPolicy.Collect,
        int batch = 500,
        bool propagateDeletes = true
    ) => new(device, kind, SyncMode.Versioned, policy, [], true, batch, propagateDeletes);

    /// <summary>注文の明細に当たる品目（code は c-{キー}・題名は商品名）</summary>
    private static AddItemOp Line(ModelActor actor, int id, int folder, string title) =>
        new(actor, Direct, id, folder, null, $"c-{id}", title, $"0A0B{id:X4}");

    /// <summary>フォルダーの名前だけを変える</summary>
    private static UpdateFolderOp Rename(ModelActor actor, int id, string name) =>
        new(actor, Direct, id, null, name);

    /// <summary>SeedServerAsync(1, "alice", 11, "widget") の写し</summary>
    private static ModelWriteOp[] Seed(int folder, string name, int line, string title) =>
        [Folder(SV, folder, name: name), Line(SV, line, folder, title)];

    private static IEnumerable<SyncModelScenario> EngineBasicScenarios() =>
        [
            Moved(
                "dl-initial-downloads-everything",
                [],
                [
                    .. Seed(1, "alice", 11, "widget"),
                    .. Seed(2, "bob", 12, "gadget"),
                    Plain(D1),
                    Expect.Result(D1).Contains("downloaded=4").Absent("conflict *"),
                    Expect
                        .Rows(D1, F)
                        .Exact("1 parent=null name=alice", "2 parent=null name=bob", "999 *"),
                    Expect.Rows(D1, I).Exact("11 *", "12 *", "999 *"),
                    Expect.Mirror(D1, F).Contains("key=1 mirror=server"),
                    Expect.Journal(D1).Empty(),
                ]
            ),
            Moved(
                "dl-incremental-downloads-only-newer-rows",
                [.. Seed(1, "alice", 11, "widget")],
                [
                    Rename(SV, 1, "alice-updated"),
                    .. Seed(2, "bob", 12, "gadget"),
                    Plain(D1),
                    Expect.Result(D1).Contains("downloaded=3"),
                    Expect.Rows(D1, F).Contains("1 parent=null name=alice-updated", "2 *"),
                ]
            ),
            Moved(
                "dl-repeated-sync-downloads-nothing",
                [.. Seed(1, "alice", 11, "widget")],
                [Plain(D1), Expect.Result(D1).Contains("downloaded=0", "uploaded=0")]
            ),
            // 端末 1 の変更が端末 2 と端末 3 へ違う順で届く（3 台目があって初めて起きる順序）。端末 1 は UploadAsync だけで
            // 上げて受理記録を残す。端末 2 は端末 1 の版を降ろしてから編集して上げ、端末 3 は端末 1 の版を見ずに端末 2 の版だけを
            // 降ろす。端末 1 は、自分の受理記録のキーに来た端末 2 の版（端末 1 の版を元にした別の書き込み）を自分のエコーと
            // みなさずに降ろし、端末 3 は端末 2 の版を元にした編集を競合なしに上げる
            Moved(
                "three-devices-change-reaches-the-other-two-in-different-orders",
                [.. Seed(1, "alice", 11, "widget")],
                [
                    Rename(D1, 1, "d1-edit"),
                    PlainOnly(D1, ModelSyncKind.Upload),
                    Expect.Result(D1).Contains("uploaded=1").Absent("conflict *"),
                    Plain(D2),
                    Expect.Result(D2).Contains("uploaded=0", "downloaded=1"),
                    Expect.Rows(D2, F).Contains("1 parent=null name=d1-edit"),
                    Rename(D2, 1, "d2-edit"),
                    Plain(D2),
                    Expect.Result(D2).Contains("uploaded=1", "downloaded=0").Absent("conflict *"),
                    Plain(D3),
                    Expect.Result(D3).Contains("uploaded=0", "downloaded=1"),
                    Expect.Rows(D3, F).Contains("1 parent=null name=d2-edit"),
                    Plain(D1),
                    Expect.Result(D1).Contains("uploaded=0", "downloaded=1"),
                    Expect.Rows(D1, F).Contains("1 parent=null name=d2-edit"),
                    Rename(D3, 1, "d3-edit"),
                    Plain(D3),
                    Expect.Result(D3).Contains("uploaded=1", "downloaded=0").Absent("conflict *"),
                    Plain(D1),
                    Expect.Result(D1).Contains("downloaded=1").Absent("conflict *"),
                    Plain(D2),
                    Expect.Result(D2).Contains("downloaded=1").Absent("conflict *"),
                    Expect.Rows(D1, F).Contains("1 parent=null name=d3-edit"),
                    Expect.Rows(D2, F).Contains("1 parent=null name=d3-edit"),
                ]
            ) with
            {
                Devices = SyncModelWorldShape.ThreeDevices,
            },
            Moved(
                "dl-empty-server-syncs-to-zero",
                [],
                [
                    Plain(D1),
                    Expect
                        .Result(D1)
                        .Exact("uploaded=0", "downloaded=0", "deleted-locally=0", "discarded=0"),
                    Expect.Rows(D1, F).Empty(),
                ]
            ) with
            {
                OmitsTail = true,
            },
            Moved(
                "dl-continues-across-batches",
                [],
                [
                    Folder(SV, 1, name: "customer-1"),
                    Folder(SV, 2, name: "customer-2"),
                    Folder(SV, 3, name: "customer-3"),
                    Folder(SV, 4, name: "customer-4"),
                    Folder(SV, 5, name: "customer-5"),
                    Plain(D1, batch: 2, propagateDeletes: false),
                    Expect.Result(D1).Contains("downloaded=5"),
                    Expect
                        .Rows(D1, F)
                        .Exact(
                            "1 *",
                            "2 *",
                            "3 *",
                            "4 *",
                            "5 parent=null name=customer-5",
                            "999 *"
                        ),
                ]
            ),
            Moved(
                "dl-batch-larger-than-one-in-clause",
                [],
                [
                    new BulkAddFoldersOp(
                        SV,
                        ModelBulkMode.Server,
                        ModelIdRanges.Range(1, 600),
                        "server-"
                    ),
                    new BulkAddFoldersOp(D1, ModelBulkMode.Raw, [1, 499, 500, 501, 600], "stale-"),
                    Plain(D1, batch: 600, propagateDeletes: false),
                    Expect.Result(D1).Contains("downloaded=600").Absent("conflict *"),
                    Expect
                        .Rows(D1, F)
                        .Count("*", 601)
                        .Contains(
                            "1 parent=null name=server-1",
                            "499 parent=null name=server-499",
                            "500 parent=null name=server-500",
                            "501 parent=null name=server-501",
                            "600 parent=null name=server-600"
                        ),
                ]
            ),
            Moved(
                "dl-interrupted-download-resumes-from-applied-maximum",
                [],
                [
                    .. Seed(1, "alice", 11, "widget"),
                    .. Seed(2, "bob", 12, "gadget"),
                    PlainOnly(D1, ModelSyncKind.Download, batch: 1, propagateDeletes: false),
                    Expect.Result(D1).Contains("downloaded=4"),
                    .. Seed(3, "carol", 13, "doodad"),
                    Plain(D1),
                    Expect.Result(D1).Contains("downloaded=2"),
                    Expect.Rows(D1, F).Exact("1 *", "2 *", "3 *", "999 *"),
                ]
            ),
            Moved(
                "dl-propagation-removes-rows-missing-on-server",
                [.. Seed(1, "alice", 11, "widget")],
                [
                    new DeleteItemOp(SV, Direct, 11),
                    new DeleteFolderOp(SV, Direct, 1),
                    Plain(D1),
                    Expect.Result(D1).Contains("deleted-locally=2"),
                    Expect.Rows(D1, F).Exact("999 *"),
                    Expect.Rows(D1, I).Exact("999 *"),
                ]
            ),
            Moved(
                "dl-propagation-can-be-turned-off",
                [.. Seed(1, "alice", 11, "widget")],
                [
                    new DeleteItemOp(SV, Direct, 11),
                    new DeleteFolderOp(SV, Direct, 1),
                    Plain(D1, propagateDeletes: false),
                    Expect.Result(D1).Contains("deleted-locally=0"),
                    Expect.Rows(D1, F).Exact("1 *", "999 *"),
                    Expect.Rows(D1, I).Exact("11 *", "999 *"),
                ]
            ),
            Moved(
                "dl-downloaded-rows-are-not-journaled",
                [],
                [.. Seed(1, "alice", 11, "widget"), Plain(D1), Expect.Journal(D1).Empty()]
            ),
            Moved(
                "ul-local-update-is-uploaded",
                [.. Seed(1, "alice", 11, "widget")],
                [
                    new CaptureMirrorOp(D1, F, "1", "before"),
                    Rename(D1, 1, "alice-local"),
                    Expect.Journal(D1).Count("*", 1),
                    Plain(D1),
                    Expect.Result(D1).Contains("uploaded=1").Absent("conflict *"),
                    Expect.Rows(SV, F).Contains("1 parent=null name=alice-local"),
                    Expect.Journal(D1).Empty(),
                    Expect.Mirror(D1, F).Contains("key=1 mirror=server before=changed"),
                ]
            ),
            Moved(
                "ul-local-insert-is-uploaded-and-mirrors-server-version",
                [],
                [
                    Folder(D1, 5, name: "dave"),
                    Plain(D1),
                    Expect.Result(D1).Contains("uploaded=1"),
                    Expect.Rows(SV, F).Contains("5 parent=null name=dave"),
                    Expect.Mirror(D1, F).Contains("key=5 mirror=server"),
                ]
            ),
            Moved(
                "ul-local-delete-is-uploaded",
                [.. Seed(1, "alice", 11, "widget")],
                [
                    new DeleteItemOp(D1, Direct, 11),
                    Plain(D1),
                    Expect.Result(D1).Absent("conflict *"),
                    Expect.Rows(SV, I).Absent("11 *"),
                ]
            ),
            Moved(
                "ul-stale-journal-entry-is-discarded",
                [],
                [
                    new RecordJournalOp(D1, F, "99", SyncJournalOperation.Upsert),
                    Expect.Journal(D1).Count("*", 1),
                    Plain(D1),
                    Expect.Result(D1).Contains("uploaded=0").Absent("conflict *"),
                    Expect.Rows(SV, F).Absent("99 *"),
                    Expect.Journal(D1).Empty(),
                ]
            ),
            // [Sync/再生]
            Moved(
                "replay-edit-then-refused-delete-still-uploads-the-edit",
                [.. Seed(1, "alice", 11, "widget")],
                [
                    Rename(D1, 1, "alice-edited"),
                    new DeleteFolderOp(D1, Direct, 1) { ExpectFailure = true },
                    Expect.Rows(D1, F).Contains("1 parent=null name=alice-edited"),
                    Plain(D1),
                    Expect.Result(D1).Contains("uploaded=1"),
                    Expect.Rows(SV, F).Contains("1 parent=null name=alice-edited"),
                    Expect.Journal(D1).Empty(),
                ]
            ),
            Moved(
                "replay-delete-then-refused-reinsert-deletes-on-server",
                [.. Seed(1, "alice", 11, "widget")],
                [
                    new DeleteItemOp(D1, Direct, 11),
                    new AddItemOp(D1, Direct, 11, 9999, null, "c-11", "orphan", "0A0B000B")
                    {
                        ExpectFailure = true,
                    },
                    Plain(D1),
                    Expect.Result(D1).Contains("uploaded=1"),
                    Expect.Rows(SV, I).Absent("11 *"),
                    Expect.Rows(D1, I).Absent("11 *"),
                    Expect.Journal(D1).Empty(),
                ]
            ),
            Moved(
                "replay-offline-insert-then-delete-is-discarded",
                [],
                [
                    Folder(D1, 50, name: "short-lived"),
                    new DeleteFolderOp(D1, Direct, 50),
                    Plain(D1),
                    Expect.Result(D1).Contains("uploaded=0", "discarded=1").Absent("conflict *"),
                    Expect.Rows(SV, F).Absent("50 *"),
                    Expect.Journal(D1).Empty(),
                ]
            ),
            Moved(
                "replay-versionless-delete-then-refused-reinsert-deletes-on-server",
                [.. Seed(1, "alice", 11, "widget"), new AddLabelOp(SV, Direct, "L5", 1, "note")],
                [
                    new DeleteLabelOp(D1, Direct, "L5"),
                    new AddLabelOp(D1, Direct, "L5", 9999, "orphan") { ExpectFailure = true },
                    Plain(D1, mode: SyncMode.LastWriteWins),
                    Expect.Rows(SV, L).Absent("L5 *"),
                    Expect.Journal(D1).Empty(),
                ]
            ),
            // [Sync/ack]
            Moved(
                "ack-truncated-echo-then-edit-is-not-a-false-modified-conflict",
                [.. Seed(1, "alice", 11, "widget"), Folder(SV, 3, name: "carol")],
                [
                    Rename(D1, 1, "alice-local"),
                    Rename(SV, 1, "alice-server"),
                    Rename(D1, 3, "carol-1"),
                    Plain(D1),
                    Expect.Result(D1).Contains("uploaded=1", $"truncation {F} pending=1 *"),
                    Rename(D1, 3, "carol-2"),
                    Plain(D1),
                    Expect
                        .Result(D1)
                        .Contains("uploaded=1")
                        .Count("conflict *", 1)
                        .Single($"conflict {F} key=1 *"),
                    Expect.Rows(SV, F).Contains("3 parent=null name=carol-2"),
                ]
            ),
            Moved(
                "ack-failed-download-then-edit-uploads-and-still-detects-echo",
                [.. Seed(1, "alice", 11, "widget")],
                [
                    Rename(D1, 1, "alice-1"),
                    new ArmFaultOp(D1, ModelFaultKind.ServerFetch),
                    new ExpectThrowOp(D1),
                    Plain(D1),
                    Expect.Result(D1).Contains("exception *"),
                    Expect.Rows(SV, F).Contains("1 parent=null name=alice-1"),
                    Rename(D1, 1, "alice-2"),
                    Plain(D1),
                    Expect.Result(D1).Contains("uploaded=1", "downloaded=0").Absent("conflict *"),
                    Expect.Rows(SV, F).Contains("1 parent=null name=alice-2"),
                ]
            ),
            Moved(
                "ack-truncated-echo-of-offline-insert-then-edit-is-not-a-false-duplicate",
                [.. Seed(1, "alice", 11, "widget")],
                [
                    Rename(D1, 1, "alice-local"),
                    Rename(SV, 1, "alice-server"),
                    Folder(D1, 4, name: "dave"),
                    Plain(D1),
                    Expect.Result(D1).Contains("uploaded=1"),
                    Rename(D1, 4, "dave-2"),
                    Plain(D1),
                    Expect
                        .Result(D1)
                        .Contains("uploaded=1")
                        .Count("conflict *", 1)
                        .Single($"conflict {F} key=1 *"),
                    Expect.Rows(SV, F).Contains("4 parent=null name=dave-2"),
                ]
            ),
            Moved(
                "ack-truncated-echo-then-delete-is-not-a-false-delete-conflict",
                [.. Seed(1, "alice", 11, "widget"), Folder(SV, 3, name: "carol")],
                [
                    Rename(D1, 1, "alice-local"),
                    Rename(SV, 1, "alice-server"),
                    Rename(D1, 3, "carol-1"),
                    Plain(D1),
                    Expect.Result(D1).Contains("uploaded=1"),
                    new DeleteFolderOp(D1, Direct, 3),
                    Plain(D1),
                    Expect
                        .Result(D1)
                        .Contains("uploaded=1")
                        .Count("conflict *", 1)
                        .Single($"conflict {F} key=1 *"),
                    Expect.Rows(SV, F).Absent("3 *"),
                ]
            ),
            Moved(
                "ack-propagation-drops-ack-so-reinsert-of-same-key-is-clean",
                [.. Seed(1, "alice", 11, "widget"), Folder(SV, 2, name: "bob")],
                [
                    Rename(D1, 2, "bob-local"),
                    PlainOnly(D1, ModelSyncKind.Upload),
                    Expect.Result(D1).Absent("conflict *"),
                    new DeleteFolderOp(SV, Direct, 2),
                    Plain(D1),
                    Expect.Rows(D1, F).Absent("2 *"),
                    Folder(D1, 2, name: "bob-again"),
                    Plain(D1),
                    Expect.Result(D1).Contains("uploaded=1").Absent("conflict *"),
                    Expect.Rows(SV, F).Contains("2 parent=null name=bob-again"),
                ]
            ),
            Moved(
                "ack-serverwins-restored-row-then-edit-is-not-a-false-duplicate",
                [.. Seed(1, "alice", 11, "widget"), Line(SV, 12, 1, "gadget")],
                [
                    new DeleteItemOp(D1, Direct, 11),
                    Plain(D1, policy: SyncConflictPolicy.ServerWins),
                    Expect.Rows(D1, I).Contains("11 folder=1 label=null code=c-11 title=widget"),
                    Expect.Mirror(D1, I).Contains("key=11 mirror=null"),
                    new UpdateItemOp(D1, Direct, 11, 1, null, "c-11", "widget-2"),
                    Plain(D1),
                    Expect.Result(D1).Contains("uploaded=1").Absent("conflict *"),
                    Expect.Rows(SV, I).Contains("11 folder=1 label=null code=c-11 title=widget-2"),
                ]
            ),
        ];
}
