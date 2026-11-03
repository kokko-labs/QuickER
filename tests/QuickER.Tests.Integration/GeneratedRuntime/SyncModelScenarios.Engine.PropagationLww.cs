using System.Collections.Generic;
using QuickER.Tests.GeneratedSyncModelFixture;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// SyncRuntimeTestsBase から移したシナリオ（削除伝搬・カスケードのグラフ保存・後勝ちのモード・ラン単位の除外・洗い替えの範囲・
/// ジャーナルの掃除・ServerWins の破棄の範囲）。表の写し方は <see cref="EngineBasicScenarios"/> の型の注と同じ。
/// </summary>
internal static partial class SyncModelScenarios
{
    private static IEnumerable<SyncModelScenario> EnginePropagationLwwScenarios() =>
        [
            Moved(
                "prop-keeps-locally-referenced-rows-across-in-clause-chunks",
                [],
                [
                    new BulkAddFoldersOp(
                        D1,
                        ModelBulkMode.Raw,
                        ModelIdRanges.Range(1, 600),
                        "local-"
                    ),
                    new BulkAddItemsOp(
                        D1,
                        ModelBulkMode.Raw,
                        [(10001, 1), (10500, 500), (10501, 501), (10600, 600)],
                        "local-"
                    ),
                    Plain(D1, excluded: [I]),
                    Expect.Result(D1).Contains("deleted-locally=596"),
                    Expect.Rows(D1, F).Exact("1 *", "500 *", "501 *", "600 *", "999 *"),
                    Expect.Rows(D1, I).Exact("999 *", "10001 *", "10500 *", "10501 *", "10600 *"),
                ]
            ),
            Moved(
                "prop-skips-referring-tables-missing-locally",
                [Folder(SV, 1, name: "alice")],
                [
                    new DropLocalTableOp(D1, L),
                    new DeleteFolderOp(SV, Direct, 1),
                    Plain(D1),
                    Expect.Result(D1).Contains("deleted-locally=1"),
                    Expect.Rows(D1, F).Exact("999 *"),
                    new RestoreLocalTableOp(D1, L),
                ]
            ),
            Moved(
                "prop-spares-row-with-pending-journal-entry",
                [.. Seed(1, "alice", 11, "widget")],
                [
                    new DeleteItemOp(SV, Direct, 11),
                    new DeleteFolderOp(SV, Direct, 1),
                    Rename(D1, 1, "alice-local"),
                    Plain(D1),
                    Expect
                        .Result(D1)
                        .Contains("deleted-locally=1")
                        .Count("conflict *", 1)
                        .Single($"conflict {F} key=1 * reason=MissingOnServer *"),
                    Expect.Rows(D1, F).Contains("1 parent=null name=alice-local"),
                    Expect.Journal(D1).Count("*", 1),
                    Expect.Rows(D1, I).Exact("999 *"),
                ]
            ),
            Moved(
                "prop-resumes-once-journal-entry-is-settled",
                [.. Seed(1, "alice", 11, "widget")],
                [
                    new DeleteItemOp(SV, Direct, 11),
                    new DeleteFolderOp(SV, Direct, 1),
                    Rename(D1, 1, "alice-local"),
                    Plain(D1),
                    Expect.Rows(D1, F).Contains("1 *"),
                    Plain(D1, policy: SyncConflictPolicy.ServerWins),
                    Expect.Result(D1).Contains("deleted-locally=1").Absent("conflict *"),
                    Expect.Rows(D1, F).Absent("1 *"),
                    Expect.Journal(D1).Empty(),
                ]
            ),
            Moved(
                "prop-spares-local-insert-not-yet-uploaded",
                [],
                [
                    Folder(D1, 5, name: "dave"),
                    PlainOnly(D1, ModelSyncKind.Download),
                    Expect.Result(D1).Contains("deleted-locally=0"),
                    Expect.Rows(D1, F).Contains("5 *"),
                ]
            ),
            Moved(
                "graph-cascade-save-uploads-parent-and-child",
                [],
                [
                    new SaveFolderGraphOp(
                        D1,
                        new GraphFolder(1, ModelRowState.Added, "alice"),
                        [new GraphItem(11, ModelRowState.Added, "widget")],
                        []
                    ),
                    Expect.Journal(D1).Contains($"{F} key=1 *", $"{I} key=11 *"),
                    Plain(D1),
                    Expect
                        .Result(D1)
                        .Contains("uploaded=2", "deleted-locally=0")
                        .Absent("conflict *"),
                    Expect.Rows(SV, F).Contains("1 *"),
                    Expect.Rows(SV, I).Contains("11 *"),
                    Expect.Rows(D1, I).Contains("11 *"),
                    Expect.Mirror(D1, I).Contains("key=11 mirror=server"),
                    Plain(D1),
                    Expect.Result(D1).Contains("uploaded=0", "downloaded=0"),
                ]
            ),
            Moved(
                "graph-child-edit-under-unchanged-root-uploads",
                [.. Seed(1, "alice", 11, "widget")],
                [
                    new SaveFolderGraphOp(
                        D1,
                        new GraphFolder(1, ModelRowState.Unchanged),
                        [new GraphItem(11, ModelRowState.Updated, "widget-v2")],
                        []
                    ),
                    Expect.Journal(D1).Single($"{I} key=11 *").Absent($"{F} *"),
                    Plain(D1),
                    Expect.Result(D1).Contains("uploaded=1"),
                    Expect.Rows(SV, I).Contains("11 folder=1 label=null code=c-11 title=widget-v2"),
                ]
            ),
            Moved(
                "graph-cascade-delete-replays-child-first",
                [.. Seed(1, "alice", 11, "widget")],
                [
                    new SaveFolderGraphOp(
                        D1,
                        new GraphFolder(1, ModelRowState.Removed),
                        [new GraphItem(11, ModelRowState.Unchanged)],
                        []
                    ),
                    Expect.Rows(D1, I).Absent("11 *"),
                    Expect
                        .Journal(D1)
                        .Unordered($"{I} key=11 op=Delete *", $"{F} key=1 op=Delete *"),
                    Plain(D1),
                    Expect.Result(D1).Contains("uploaded=2").Absent("conflict *"),
                    Expect.Rows(SV, F).Absent("1 *"),
                    Expect.Rows(SV, I).Absent("11 *"),
                    Expect.Journal(D1).Empty(),
                ]
            ),
            Moved(
                "lww-versioned-run-leaves-versionless-table-alone",
                [],
                [
                    .. Seed(1, "alice", 11, "widget"),
                    new AddLabelOp(SV, Direct, "L101", 1, "server note"),
                    Plain(D1),
                    Expect.Result(D1).Contains("downloaded=2"),
                    Expect.Rows(D1, L).Empty(),
                    new AddLabelOp(D1, Direct, "L102", 1, "local note"),
                    Plain(D1),
                    Expect.Result(D1).Contains("uploaded=0"),
                    Expect.Rows(SV, L).Absent("L102 *"),
                    Expect.Rows(D1, L).Contains("L102 *"),
                    Expect.Journal(D1).Count("*", 1),
                ]
            ),
            Moved(
                "lww-downloads-versionless-table-every-run",
                [],
                [
                    .. Seed(1, "alice", 11, "widget"),
                    new AddLabelOp(SV, Direct, "L101", 1, "server note"),
                    Plain(D1, mode: SyncMode.LastWriteWins),
                    Expect.Result(D1).Contains("downloaded=3").Absent("conflict *"),
                    Expect.Rows(D1, L).Contains("L101 folder=1 caption=server note"),
                    Plain(D1, mode: SyncMode.LastWriteWins),
                    Expect.Result(D1).Contains("downloaded=1"),
                ]
            ),
            Moved(
                "lww-uploads-versionless-edits",
                [
                    .. Seed(1, "alice", 11, "widget"),
                    new AddLabelOp(SV, Direct, "L101", 1, "original"),
                ],
                [
                    new UpdateLabelOp(D1, Direct, "L101", 1, "edited locally"),
                    new AddLabelOp(D1, Direct, "L102", 1, "new local"),
                    Plain(D1, mode: SyncMode.LastWriteWins),
                    Expect.Result(D1).Contains("uploaded=2").Absent("conflict *"),
                    Expect
                        .Rows(SV, L)
                        .Contains(
                            "L101 folder=1 caption=edited locally",
                            "L102 folder=1 caption=new local"
                        ),
                    Expect.Rows(D1, L).Contains("L102 *"),
                    new DeleteLabelOp(D1, Direct, "L101"),
                    Plain(D1, mode: SyncMode.LastWriteWins),
                    Expect.Result(D1).Contains("uploaded=1"),
                    Expect.Rows(SV, L).Absent("L101 *"),
                    Expect.Journal(D1).Empty(),
                ]
            ),
            Moved(
                "lww-overwrites-and-resurrects-without-conflict",
                [
                    .. Seed(1, "alice", 11, "widget"),
                    new AddLabelOp(SV, Direct, "L101", 1, "original"),
                    new AddLabelOp(SV, Direct, "L103", 1, "doomed"),
                ],
                [
                    new UpdateLabelOp(SV, Direct, "L101", 1, "changed on server"),
                    new UpdateLabelOp(D1, Direct, "L101", 1, "changed locally"),
                    new DeleteLabelOp(SV, Direct, "L103"),
                    new UpdateLabelOp(D1, Direct, "L103", 1, "revived"),
                    Plain(D1, mode: SyncMode.LastWriteWins),
                    Expect.Result(D1).Absent("conflict *"),
                    Expect
                        .Rows(SV, L)
                        .Contains(
                            "L101 folder=1 caption=changed locally",
                            "L103 folder=1 caption=revived"
                        ),
                ]
            ),
            Moved(
                "lww-bypasses-version-guard-on-versioned-tables",
                [.. Seed(1, "alice", 11, "widget")],
                [
                    Rename(SV, 1, "server-edit"),
                    Rename(D1, 1, "local-edit"),
                    Plain(D1, mode: SyncMode.LastWriteWins),
                    Expect.Result(D1).Contains("uploaded=1").Absent("conflict *"),
                    Expect.Rows(SV, F).Contains("1 parent=null name=local-edit"),
                    Plain(D1, mode: SyncMode.LastWriteWins),
                    Expect.Result(D1).Contains("uploaded=0", "downloaded=0"),
                    Expect.Rows(D1, F).Contains("1 parent=null name=local-edit"),
                ]
            ),
            Moved(
                "lww-cascade-save-with-versionless-child-uploads",
                [],
                [
                    new SaveFolderGraphOp(
                        D1,
                        new GraphFolder(1, ModelRowState.Added, "alice"),
                        [],
                        [new GraphLabel("L101", ModelRowState.Added, "memo")]
                    ),
                    Plain(D1, mode: SyncMode.LastWriteWins),
                    Expect.Result(D1).Contains("uploaded=2").Absent("conflict *"),
                    Expect.Rows(SV, L).Contains("L101 folder=1 caption=memo"),
                ]
            ),
            Moved(
                "lww-run-exclusion-skips-table-for-one-run",
                [
                    .. Seed(1, "alice", 11, "widget"),
                    new AddLabelOp(SV, Direct, "L101", 1, "server note"),
                ],
                [
                    new UpdateLabelOp(D1, Direct, "L101", 1, "edited locally"),
                    Plain(D1, mode: SyncMode.LastWriteWins, excluded: [L]),
                    Expect.Result(D1).Contains("uploaded=0"),
                    Expect.Rows(SV, L).Contains("L101 folder=1 caption=server note"),
                    Expect.Journal(D1).Count("*", 1),
                    Plain(D1, mode: SyncMode.LastWriteWins),
                    Expect.Result(D1).Contains("uploaded=1"),
                    Expect.Rows(SV, L).Contains("L101 folder=1 caption=edited locally"),
                ]
            ),
            Moved(
                "refresh-scopes-versionless-tables-by-mode",
                [.. Seed(1, "alice", 11, "widget")],
                [
                    new AddLabelOp(SV, Direct, "L101", 1, "server note"),
                    new RefreshWithOptionsOp(D1, SyncMode.Versioned, true, false, false, []),
                    Expect
                        .Result(D1)
                        .Count("table *", 3)
                        .Contains($"table {F} *", $"table {I} *", $"table {ModelTables.Events} *"),
                    Expect.Rows(D1, L).Empty(),
                    Plain(D1, mode: SyncMode.LastWriteWins),
                    new AddLabelOp(D1, Direct, "L102", 1, "pending"),
                    new ExpectThrowOp(D1),
                    new RefreshWithOptionsOp(D1, SyncMode.LastWriteWins, true, false, false, []),
                    Expect.Result(D1).Contains("exception SyncPendingChangesException: *"),
                    new RefreshWithOptionsOp(D1, SyncMode.LastWriteWins, true, false, true, []),
                    Expect
                        .Result(D1)
                        .Contains("discarded-changes=1")
                        .Count("table *", 4)
                        .Contains(
                            $"table {F} *",
                            $"table {I} *",
                            $"table {L} *",
                            $"table {ModelTables.Events} *"
                        ),
                    Expect.Rows(D1, L).Absent("L102 *").Contains("L101 *"),
                ]
            ),
            Moved(
                "rec-journal-cleanup-by-table-and-wholesale",
                [],
                [
                    Folder(D1, 1, name: "a"),
                    new AddLabelOp(D1, Direct, "L101", 1, "b"),
                    Expect.Journal(D1).Count("*", 2),
                    new ClearJournalOp(D1, L),
                    Expect.Journal(D1).Exact($"{F} key=1 *"),
                    new ClearJournalOp(D1, null),
                    Expect.Journal(D1).Empty(),
                ]
            ),
            Moved(
                "sw-drops-only-scoped-entries",
                [.. Seed(1, "alice", 11, "widget")],
                [
                    Rename(D1, 1, "local-edit"),
                    new AddLabelOp(D1, Direct, "L102", 1, "pending"),
                    Plain(D1, policy: SyncConflictPolicy.ServerWins),
                    Expect.Result(D1).Contains("discarded=1"),
                    Expect.Journal(D1).Count("*", 1),
                ]
            ),
        ];
}
