using System.Collections.Generic;
using QuickER.Tests.GeneratedSyncModelFixture;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// SyncRuntimeTestsBase から移したシナリオ（競合の収集・ServerWins・LocalWins・着地しなかった削除の記録・掃除の件数・
/// 1 回のランの中のアップロードとダウンロードの噛み合わせ）。表の写し方は <see cref="EngineBasicScenarios"/> の型の注と同じ。
/// </summary>
internal static partial class SyncModelScenarios
{
    private static IEnumerable<SyncModelScenario> EngineConflictsScenarios() =>
        [
            MovedAll(
                "conflict-collected-by-default-with-both-rows",
                [.. Seed(1, "alice", 11, "widget")],
                [
                    Rename(D1, 1, "local-wins?"),
                    Rename(SV, 1, "server-wins?"),
                    Plain(D1),
                    Expect
                        .Result(D1)
                        .Count("conflict *", 1)
                        .Single(
                            $"conflict {F} key=1 op=Upsert reason=ModifiedOnServer "
                                + "local=parent=* name=* server=parent=null name=server-wins?"
                        ),
                    Expect.Journal(D1).Count("*", 1),
                ]
            ),
            Moved(
                "sw-discards-local-change",
                [.. Seed(1, "alice", 11, "widget")],
                [
                    Rename(D1, 1, "local-wins?"),
                    Rename(SV, 1, "server-wins"),
                    Plain(D1, policy: SyncConflictPolicy.ServerWins),
                    Expect.Result(D1).Contains("discarded=*").Absent("conflict *", "discarded=0"),
                    Expect.Rows(D1, F).Contains("1 parent=null name=server-wins"),
                    Expect.Journal(D1).Empty(),
                ]
            ),
            Moved(
                "sw-removes-local-insert-server-never-received",
                [.. Seed(1, "alice", 11, "widget")],
                [
                    Folder(D1, 6, name: "erin"),
                    Plain(D1, policy: SyncConflictPolicy.ServerWins),
                    Expect
                        .Result(D1)
                        .Contains("discarded=*", "deleted-locally=1")
                        .Absent("conflict *", "discarded=0"),
                    Expect.Rows(D1, F).Absent("6 *"),
                    Expect.Journal(D1).Empty(),
                ]
            ),
            Moved(
                "sw-restores-row-the-server-has-not-changed",
                [.. Seed(1, "alice", 11, "widget")],
                [
                    Rename(D1, 1, "local-edit"),
                    Plain(D1, policy: SyncConflictPolicy.ServerWins),
                    Expect.Result(D1).Contains("discarded=1").Absent("conflict *"),
                    Expect.Rows(D1, F).Contains("1 parent=null name=alice"),
                    Expect.Rows(SV, F).Contains("1 parent=null name=alice"),
                    Expect.Journal(D1).Empty(),
                ]
            ),
            Moved(
                "sw-restores-row-deleted-locally",
                [.. Seed(1, "alice", 11, "widget"), Line(SV, 12, 1, "gadget")],
                [
                    new DeleteItemOp(D1, Direct, 11),
                    Plain(D1, policy: SyncConflictPolicy.ServerWins),
                    Expect.Result(D1).Absent("conflict *"),
                    Expect.Rows(D1, I).Contains("11 folder=1 label=null code=c-11 title=widget"),
                    Expect.Rows(SV, I).Contains("11 *"),
                    Expect.Journal(D1).Empty(),
                ]
            ),
            Moved(
                "sw-removes-row-deleted-on-server",
                [.. Seed(1, "alice", 11, "widget")],
                [
                    new UpdateItemOp(D1, Direct, 11, 1, null, "c-11", "local-edit"),
                    new DeleteItemOp(SV, Direct, 11),
                    Plain(D1, policy: SyncConflictPolicy.ServerWins),
                    Expect.Result(D1).Absent("conflict *"),
                    Expect.Rows(D1, I).Absent("11 *"),
                    Expect.Journal(D1).Empty(),
                ]
            ),
            Moved(
                "sw-restore-does-not-advance-anchor",
                [.. Seed(1, "alice", 11, "widget"), .. Seed(2, "bob", 12, "gadget")],
                [
                    Rename(SV, 1, "server-p"),
                    Rename(SV, 2, "server-x"),
                    Rename(D1, 2, "local-x"),
                    new CaptureMirrorOp(D1, F, "2", "before"),
                    PlainOnly(D1, ModelSyncKind.Upload, policy: SyncConflictPolicy.ServerWins),
                    Expect.Result(D1).Contains("uploaded=0", "discarded=1").Absent("conflict *"),
                    Expect.Rows(D1, F).Contains("2 parent=null name=server-x"),
                    Expect.Mirror(D1, F).Contains("key=2 mirror=other before=same"),
                    PlainOnly(D1, ModelSyncKind.Download, policy: SyncConflictPolicy.ServerWins),
                    Expect.Result(D1).Contains("downloaded=1"),
                    Expect
                        .Rows(D1, F)
                        .Contains("1 parent=null name=server-p", "2 parent=null name=server-x"),
                    Expect.Mirror(D1, F).Contains("key=2 mirror=server *"),
                ]
            ),
            Moved(
                "lw-overwrites-server-row",
                [.. Seed(1, "alice", 11, "widget")],
                [
                    Rename(D1, 1, "local-wins"),
                    Rename(SV, 1, "server-loses"),
                    Plain(D1, policy: SyncConflictPolicy.LocalWins),
                    Expect.Result(D1).Contains("uploaded=1").Absent("conflict *"),
                    Expect.Rows(SV, F).Contains("1 parent=null name=local-wins"),
                ]
            ),
            Moved(
                "lw-overwrites-duplicate-key-on-server",
                [],
                [
                    Folder(SV, 7, name: "server-side"),
                    Folder(D1, 7, name: "local-side"),
                    Plain(D1, policy: SyncConflictPolicy.LocalWins),
                    Expect.Result(D1).Contains("uploaded=1").Absent("conflict *"),
                    Expect.Rows(SV, F).Contains("7 parent=null name=local-side"),
                    Expect.Journal(D1).Empty(),
                ]
            ),
            Moved(
                "lw-resurrects-row-missing-on-server",
                [.. Seed(1, "alice", 11, "widget")],
                [
                    new DeleteItemOp(SV, Direct, 11),
                    new DeleteFolderOp(SV, Direct, 1),
                    Rename(D1, 1, "alice-local"),
                    Plain(D1, policy: SyncConflictPolicy.LocalWins),
                    Expect
                        .Result(D1)
                        .Contains("uploaded=1", "deleted-locally=1")
                        .Absent("conflict *"),
                    Expect.Rows(SV, F).Contains("1 parent=null name=alice-local"),
                    Expect.Rows(D1, F).Contains("1 *"),
                    Expect.Journal(D1).Empty(),
                    Plain(D1, policy: SyncConflictPolicy.LocalWins),
                    Expect
                        .Result(D1)
                        .Contains("uploaded=0", "downloaded=0", "deleted-locally=0")
                        .Absent("conflict *"),
                ]
            ),
            Moved(
                "replay-unlanded-delete-entry-is-discarded",
                [.. Seed(1, "alice", 11, "widget")],
                [
                    new DeleteFolderOp(D1, Direct, 1) { ExpectFailure = true },
                    Expect.Journal(D1).Empty(),
                    new RecordJournalOp(D1, F, "1", SyncJournalOperation.Delete),
                    Expect.Rows(D1, F).Contains("1 *"),
                    Expect.Journal(D1).Exact($"{F} key=1 op=Delete *"),
                    Plain(D1),
                    Expect.Result(D1).Contains("uploaded=0", "discarded=1").Absent("conflict *"),
                    Expect.Rows(SV, F).Contains("1 *"),
                    Expect.Rows(D1, F).Contains("1 *"),
                    Expect.Journal(D1).Empty(),
                ]
            ),
            // 同じ形を外部の ISyncTable の代役で：最新の記録で計画する表でも、着地しなかった削除の記録は 1 回で捨てる
            // （行が残り、計画の後に書かれた記録も無い削除の記録を残すと、毎回同じ削除を計画して行を見つけ続ける）
            Moved(
                "replay-unlanded-delete-entry-is-discarded-external-table",
                [.. Seed(1, "alice", 11, "widget")],
                [
                    new DeleteFolderOp(D1, Direct, 1) { ExpectFailure = true },
                    new RecordJournalOp(D1, F, "1", SyncJournalOperation.Delete),
                    Expect.Journal(D1).Exact($"{F} key=1 op=Delete *"),
                    Plain(D1),
                    Expect.Result(D1).Contains("uploaded=0", "discarded=1").Absent("conflict *"),
                    Expect.Rows(SV, F).Contains("1 *"),
                    Expect.Journal(D1).Empty(),
                ]
            ) with
            {
                ExternalTables = [D1],
            },
            // P5-2 の S1 を外部の ISyncTable の代役で：計画した削除を送る前に同じキーで作り直すと、削除の記録（版つき）は
            // 作り直しの記録と一緒に残り、次の回が版つきの更新として送る（捨てると挿入として送って偽の DuplicateOnServer）
            Moved(
                "replay-recreated-pending-delete-is-sent-as-update-external-table",
                [Folder(SV, 700), Item(SV, 701, folder: 700), Item(SV, 702, folder: 700)],
                [
                    new UpdateItemOp(D1, Direct, 701, 700, null, "c-701", "a-edited"),
                    new DeleteItemOp(D1, Direct, 702),
                    new ArmInterruptOp(
                        D1,
                        ModelInterruptPoint.BeforeServerWrite,
                        new AddItemOp(D1, Direct, 702, 700, null, "c-702", "b-recreated", "0A0B0702")
                    ),
                    Plain(D1),
                    Expect
                        .Journal(D1)
                        .Count("*", 2)
                        .Single($"{I} key=702 op=Delete ver=set * age=old")
                        .Single($"{I} key=702 op=Upsert * age=new"),
                    Plain(D1),
                    Expect.Result(D1).Absent("conflict *"),
                    Expect.Journal(D1).Empty(),
                    Expect.Rows(SV, I).Contains("702 folder=700 label=null code=c-702 title=b-recreated"),
                ]
            ) with
            {
                ExternalTables = [D1],
            },
            Moved(
                "ul-settles-more-journal-entries-than-one-in-clause",
                [],
                [
                    new BulkAddFoldersOp(
                        D1,
                        ModelBulkMode.Journaled,
                        ModelIdRanges.Range(1, 600),
                        "customer-"
                    ),
                    Expect.Journal(D1).Count("*", 600),
                    Plain(D1),
                    Expect.Result(D1).Contains("uploaded=600").Absent("conflict *"),
                    Expect.Journal(D1).Empty(),
                    Expect.Rows(SV, F).Count("*", 601),
                ]
            ),
            Moved(
                "conflict-delete-of-row-updated-on-server",
                [.. Seed(1, "alice", 11, "widget")],
                [
                    new DeleteItemOp(D1, Direct, 11),
                    new UpdateItemOp(SV, Direct, 11, 1, null, "c-11", "widget-v2"),
                    Plain(D1),
                    Expect
                        .Result(D1)
                        .Count("conflict * op=Delete *", 1)
                        .Contains($"conflict {I} key=11 op=Delete reason=ModifiedOnServer *"),
                    Expect.Rows(SV, I).Contains("11 *"),
                ]
            ),
            Moved(
                "conflict-duplicate-key-on-server-is-reported",
                [],
                [
                    Folder(SV, 7, name: "server-side"),
                    Folder(D1, 7, name: "local-side"),
                    PlainOnly(D1, ModelSyncKind.Upload),
                    Expect
                        .Result(D1)
                        .Count("conflict *", 1)
                        .Single($"conflict {F} key=7 * reason=DuplicateOnServer *"),
                    Expect.Rows(SV, F).Contains("7 parent=null name=server-side"),
                ]
            ),
            Moved(
                "ul-pending-edit-does-not-hide-other-rows-server-change",
                [.. Seed(1, "alice", 11, "widget"), .. Seed(2, "bob", 12, "gadget")],
                [
                    Rename(SV, 2, "bob-server"),
                    Rename(D1, 1, "alice-local"),
                    Plain(D1),
                    Expect.Result(D1).Contains("uploaded=1").Absent("conflict *"),
                    Expect.Rows(SV, F).Contains("1 parent=null name=alice-local"),
                    Expect.Rows(D1, F).Contains("2 parent=null name=bob-server"),
                ]
            ),
            Moved(
                "conflict-collected-row-keeps-local-content",
                [.. Seed(1, "alice", 11, "widget")],
                [
                    Rename(D1, 1, "local-edit"),
                    Rename(SV, 1, "server-edit"),
                    Plain(D1),
                    Expect
                        .Result(D1)
                        .Count("conflict *", 1)
                        .Single($"conflict {F} key=1 * reason=ModifiedOnServer *"),
                    Expect.Journal(D1).Count("*", 1),
                    Expect.Rows(D1, F).Contains("1 parent=null name=local-edit"),
                ]
            ),
            Moved(
                "conflict-truncation-continues-following-tables-and-reads-parents",
                [.. Seed(1, "alice", 11, "widget")],
                [
                    .. ConflictWithNewerServerRows(),
                    Plain(D1),
                    Expect
                        .Result(D1)
                        .Count("conflict *", 1)
                        .Single($"conflict {F} *")
                        .Count("truncation *", 1)
                        .Single($"truncation {F} pending=1 * truncated-by=-"),
                    Expect.Rows(D1, F).Contains("1 parent=null name=local-edit", "2 *"),
                    Expect.Rows(D1, I).Contains("12 *"),
                ]
            ),
            Moved(
                "conflict-discarded-lets-next-run-catch-up",
                [.. Seed(1, "alice", 11, "widget")],
                [
                    .. ConflictWithNewerServerRows(),
                    Plain(D1),
                    new ClearJournalOp(D1, F),
                    Plain(D1),
                    Expect.Result(D1).Absent("conflict *", "truncation *"),
                    Expect.Rows(D1, F).Contains("1 parent=null name=server-edit", "2 *"),
                    Expect.Rows(D1, I).Contains("12 *"),
                ]
            ),
            Moved(
                "conflict-delete-is-not-resurrected-by-same-run-download",
                [.. Seed(1, "alice", 11, "widget")],
                [
                    new DeleteItemOp(D1, Direct, 11),
                    new UpdateItemOp(SV, Direct, 11, 1, null, "c-11", "widget-v2"),
                    Plain(D1),
                    Expect
                        .Result(D1)
                        .Count("conflict *", 1)
                        .Single("conflict * op=Delete *")
                        .Count($"truncation {I} *", 1)
                        .Single($"truncation {I} pending=11 * truncated-by=-"),
                    Expect.Rows(D1, I).Absent("11 *"),
                ]
            ),
            Moved(
                "dl-server-update-after-upload-is-still-downloaded",
                [.. Seed(1, "alice", 11, "widget")],
                [
                    Rename(D1, 1, "alice-local"),
                    new ArmServerInterruptOp(
                        D1,
                        ModelInterruptPoint.BeforeServerFetch,
                        Rename(SV, 1, "alice-server-later")
                    ),
                    Plain(D1),
                    Expect.Result(D1).Contains("uploaded=1"),
                    Expect.Rows(D1, F).Contains("1 parent=null name=alice-server-later"),
                ]
            ),
        ];

    /// <summary>
    /// ArrangeOrderConflictWithNewerServerRowsAsync の写し：フォルダー 1 に競合を作り、そのサーバー版より後ろに新しい親と
    /// その親を参照する子を置く
    /// </summary>
    private static ModelWriteOp[] ConflictWithNewerServerRows() =>
        [
            Rename(D1, 1, "local-edit"),
            Rename(SV, 1, "server-edit"),
            Folder(SV, 2, name: "bob"),
            Line(SV, 12, 2, "gizmo"),
        ];
}
