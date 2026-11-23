using System.Collections.Generic;
using QuickER.Tests.GeneratedSyncModelFixture;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// SyncRuntimeTestsBase から移したシナリオ（洗い替え・blob を運ぶかの既定）。表の写し方は <see cref="EngineBasicScenarios"/>
/// の型の注と同じ。元の洗い替えの既定（<c>DiscardLocalUnboundedBinaries=true</c>・Mode は後勝ち）は、Plain の規則に合わせて
/// blob を運ぶ洗い替えで書く（blob の損失ガードはどちらでも満たす）。blob を運ぶかそのものを見るシナリオだけが
/// blob を運ばない同期を使い、静止化の前に端末を blob を運ぶ洗い替えでそろえ直す（文書の割り切り O1）。
/// </summary>
internal static partial class SyncModelScenarios
{
    /// <summary>元の RefreshDefaults の写し（後勝ちのモード・blob は運ぶ）</summary>
    private static RefreshWithOptionsOp RefreshAll(
        ModelActor device,
        SyncMode mode = SyncMode.LastWriteWins,
        bool force = false,
        IReadOnlyList<string>? excluded = null,
        int batchSize = 2000
    ) => new(device, mode, true, false, force, excluded ?? [], batchSize);

    /// <summary>
    /// 表どうしの循環を使うサーバーの行：フォルダー 710 と品目 711、711 をピン留めするフォルダー 700（行は循環しない。
    /// 700 を作ってから 711 を作り、700 を更新してピン留めするので、ダウンロードの流れでは 700 が 711 より後ろの版になる）
    /// </summary>
    private static ModelWriteOp[] CyclePinned() =>
        [
            Folder(SV, 700),
            Folder(SV, 710),
            Line(SV, 711, 710, "pinned"),
            new PinFolderOp(SV, Direct, 700, 711),
        ];

    private static IEnumerable<SyncModelScenario> EngineRefreshScenarios() =>
        [
            Moved(
                "refresh-rebuilds-local-from-server",
                [],
                [
                    .. Seed(1, "alice", 11, "widget"),
                    .. Seed(2, "bob", 12, "gadget"),
                    new BulkAddFoldersOp(D1, ModelBulkMode.Raw, [1], "stale-"),
                    new BulkAddFoldersOp(D1, ModelBulkMode.Raw, [99], "orphan-"),
                    RefreshAll(D1),
                    Expect
                        .Result(D1)
                        .Exact(
                            "deleted=1",
                            "inserted=7",
                            "discarded-changes=0",
                            "elapsed>0",
                            $"table {F} *",
                            $"table {L} *",
                            $"table {I} *",
                            $"table {ModelTables.Events} *"
                        ),
                    Expect
                        .Rows(D1, F)
                        .Exact("1 parent=null name=alice", "2 parent=null name=bob", "999 *"),
                    Expect.Rows(D1, I).Exact("11 *", "12 *", "999 *"),
                    Expect.Mirror(D1, F).Contains("key=1 mirror=server"),
                ]
            ),
            Moved(
                "refresh-leaves-anchor-next-sync-resumes-from",
                [.. Seed(1, "alice", 11, "widget")],
                [
                    RefreshAll(D1),
                    .. Seed(2, "bob", 12, "gadget"),
                    Plain(D1),
                    Expect.Result(D1).Contains("downloaded=2"),
                    Expect.Rows(D1, F).Exact("1 *", "2 *", "999 *"),
                ]
            ),
            Moved(
                "refresh-refuses-when-local-changes-are-pending",
                [.. Seed(1, "alice", 11, "widget")],
                [
                    Rename(D1, 1, "offline-edit"),
                    new DeleteItemOp(D1, Direct, 11),
                    new ExpectThrowOp(D1),
                    RefreshAll(D1),
                    Expect
                        .Result(D1)
                        .Contains(
                            $"exception SyncPendingChangesException: *{F}*",
                            "pending-count=2",
                            $"pending {F} count=1",
                            $"pending {I} count=1"
                        )
                        .Count("pending *", 2),
                    Expect.Rows(D1, F).Contains("1 parent=null name=offline-edit"),
                    Expect.Rows(D1, I).Exact("999 *"),
                    Expect.Pending(D1).Unordered($"{F} key=1 row=present", $"{I} key=11 row=gone"),
                ]
            ),
            Moved(
                "refresh-force-discards-pending-changes",
                [.. Seed(1, "alice", 11, "widget")],
                [
                    Rename(D1, 1, "offline-edit"),
                    RefreshAll(D1, force: true),
                    Expect.Result(D1).Contains("discarded-changes=1", "inserted=5"),
                    Expect.Pending(D1).Empty(),
                    Expect.Rows(D1, F).Contains("1 parent=null name=alice"),
                    Expect.Rows(SV, F).Contains("1 parent=null name=alice"),
                ]
            ),
            Moved(
                "refresh-does-not-journal-its-own-writes",
                [],
                [
                    .. Seed(1, "alice", 11, "widget"),
                    .. Seed(2, "bob", 12, "gadget"),
                    RefreshAll(D1),
                    Expect.Pending(D1).Empty(),
                    Plain(D1),
                    Expect.Result(D1).Contains("uploaded=0"),
                ]
            ),
            Moved(
                "refresh-empty-server-empties-local",
                [.. Seed(1, "alice", 11, "widget")],
                [
                    RefreshAll(D1),
                    new DeleteItemOp(SV, Direct, 11),
                    new DeleteFolderOp(SV, Direct, 1),
                    RefreshAll(D1),
                    Expect.Result(D1).Contains("deleted=2", "inserted=0"),
                    Expect.Rows(D1, F).Empty(),
                    Expect.Rows(D1, I).Empty(),
                ]
            ) with
            {
                OmitsTail = true,
            },
            Moved(
                "refresh-refuses-when-rows-outside-refer-inside",
                [.. Seed(1, "alice", 11, "widget"), new AddLabelOp(SV, Direct, "L101", 1, "memo")],
                [
                    Folder(D1, 2, name: "bob"),
                    PlainOnly(D1, ModelSyncKind.Upload),
                    Rename(D1, 1, "edited"),
                    Expect.Pending(D1).Exact($"{F} key=1 row=present"),
                    new ExpectThrowOp(D1),
                    RefreshAll(D1, mode: SyncMode.Versioned, force: true),
                    Expect
                        .Result(D1)
                        .Contains("exception SyncRefreshScopeException: *")
                        .Count("scope-reference *", 1)
                        .Contains($"scope-reference {L}->{F}"),
                    Expect
                        .Rows(D1, F)
                        .Exact("1 parent=null name=edited", "2 parent=null name=bob", "999 *"),
                    Expect.Rows(D1, I).Exact("11 *", "999 *"),
                    Expect.Rows(D1, L).Exact("L101 *"),
                    Expect.Pending(D1).Exact($"{F} key=1 row=present"),
                    // 断られた洗い替えは、上げ済みの行（フォルダー 2）の送信の基準を変えない：次の編集は偽の競合にならずに上がる
                    Rename(D1, 2, "bob-edited"),
                    Plain(D1),
                    Expect.Result(D1).Contains("uploaded=2").Absent("conflict *"),
                    Expect.Rows(SV, F).Contains("2 parent=null name=bob-edited"),
                ]
            ),
            Moved(
                "refresh-excluding-referencing-child-refuses-under-cascade",
                [.. Seed(1, "alice", 11, "widget")],
                [
                    new MakeLocalCascadeOp(D1, I, F),
                    new UpdateItemOp(D1, Direct, 11, 1, null, "c-11", "edited-offline"),
                    new ExpectThrowOp(D1),
                    RefreshAll(D1, excluded: [I]),
                    Expect
                        .Result(D1)
                        .Contains("exception SyncRefreshScopeException: *")
                        .Count("scope-reference *", 1)
                        .Contains($"scope-reference {I}->{F}"),
                    Expect
                        .Rows(D1, I)
                        .Contains("11 folder=1 label=null code=c-11 title=edited-offline"),
                    Expect.Pending(D1).Exact($"{I} key=11 row=present"),
                    Plain(D1),
                    new DeleteItemOp(D1, Direct, 11),
                    Plain(D1),
                    RefreshAll(D1, excluded: [I]),
                    Expect
                        .Result(D1)
                        .Absent("exception *")
                        .Count("table *", 3)
                        .Contains($"table {F} *", $"table {L} *", $"table {ModelTables.Events} *"),
                ]
            ) with
            {
                OmitsTail = true,
            },
            Moved(
                "refresh-skips-referring-tables-missing-locally",
                [.. Seed(1, "alice", 11, "widget")],
                [
                    new DropLocalTableOp(D1, ModelTables.Events),
                    RefreshAll(D1, mode: SyncMode.Versioned, excluded: [ModelTables.Events]),
                    Expect
                        .Result(D1)
                        .Absent("exception *")
                        .Count("table *", 2)
                        .Contains($"table {F} *", $"table {I} *"),
                    new RestoreLocalTableOp(D1, ModelTables.Events),
                    // 作り直した空の表：同期が空にされた表として断るので、その表を範囲に含む洗い替えで作り直す
                    new ExpectThrowOp(D1),
                    Plain(D1),
                    Expect
                        .Result(D1)
                        .Contains(
                            $"exception SyncLocalTableEmptiedException: *{ModelTables.Events}*"
                        ),
                    RefreshAll(D1),
                    Expect.Result(D1).Absent("exception *"),
                    Expect.Rows(D1, ModelTables.Events).Contains("* item=999 memo=tail"),
                ]
            ),
            // P5-2 の S3：表どうしの外部キーの循環（品目→フォルダー・フォルダーのピン留め→品目）。行は循環しない（フォルダー 700
            // が別のフォルダー 710 の品目 711 をピン留めする）。1 表ずつの全消しは、どの順でも最初の DELETE がもう一方の表の行に
            // 参照されて断られた（Force では未送信の記録だけが先に消えた）
            Moved(
                "refresh-empties-tables-in-a-foreign-key-cycle",
                [],
                [
                    .. CyclePinned(),
                    Plain(D1),
                    Expect.Rows(D1, F).Contains("700 parent=null name=f700 pinned=711"),
                    RefreshAll(D1),
                    Expect.Result(D1).Absent("exception *").Contains("inserted=6"),
                    Expect
                        .Rows(D1, F)
                        .Exact("700 parent=null name=f700 pinned=711", "710 *", "999 *"),
                    Expect.Rows(D1, I).Exact("711 folder=710 *", "999 *"),
                ]
            ),
            Moved(
                "refresh-force-in-a-foreign-key-cycle-replaces-pending-changes",
                [],
                [
                    .. CyclePinned(),
                    Plain(D1),
                    Rename(D1, 710, "offline-edit"),
                    RefreshAll(D1, force: true),
                    Expect.Result(D1).Absent("exception *").Contains("discarded-changes=1"),
                    Expect.Pending(D1).Empty(),
                    Expect.Rows(D1, F).Contains("710 parent=null name=f710"),
                ]
            ),
            // 以下の 5 行は、端末の状態の表を読まずに、洗い替えの振る舞いを実行の結果・例外・
            // 端末とサーバーの行・その後の同期が送るか送らないかで見る（どちらの端末でも流れる）
            Moved(
                "refresh-cycle-force-replaces-pending-and-next-sync-sends-nothing",
                [],
                [
                    .. CyclePinned(),
                    Plain(D1),
                    Rename(D1, 710, "offline-edit"),
                    RefreshAll(D1, force: true),
                    Expect.Result(D1).Absent("exception *").Contains("discarded-changes=1"),
                    Expect
                        .Rows(D1, F)
                        .Contains(
                            "710 parent=null name=f710",
                            "700 parent=null name=f700 pinned=711"
                        ),
                    Plain(D1),
                    Expect.Result(D1).Contains("uploaded=0").Absent("conflict *"),
                    Expect.Rows(SV, F).Contains("710 parent=null name=f710"),
                ]
            ),
            Moved(
                "refresh-pending-refused-keeps-edit-that-next-sync-sends",
                [.. Seed(1, "alice", 11, "widget")],
                [
                    Rename(D1, 1, "offline-edit"),
                    new ExpectThrowOp(D1),
                    RefreshAll(D1),
                    Expect.Result(D1).Contains($"exception SyncPendingChangesException: *{F}*"),
                    Expect.Rows(D1, F).Contains("1 parent=null name=offline-edit"),
                    Plain(D1),
                    Expect.Result(D1).Contains("uploaded=1").Absent("conflict *"),
                    Expect.Rows(SV, F).Contains("1 parent=null name=offline-edit"),
                ]
            ),
            Moved(
                "refresh-force-discards-pending-and-next-sync-sends-nothing",
                [.. Seed(1, "alice", 11, "widget")],
                [
                    Rename(D1, 1, "offline-edit"),
                    RefreshAll(D1, force: true),
                    Expect.Result(D1).Contains("discarded-changes=1"),
                    Expect.Rows(D1, F).Contains("1 parent=null name=alice"),
                    Plain(D1),
                    Expect.Result(D1).Contains("uploaded=0").Absent("conflict *"),
                    Expect.Rows(SV, F).Contains("1 parent=null name=alice"),
                ]
            ),
            // 端末が消した（未送信の）行をサーバーの他者も消し、Force の洗い替えがその削除を捨てる。その後に端末が同じキーで
            // 作った行は、サーバーに一度も無かった行として挿入で上がる（洗い替えが、両方に無い行の跡を残さない）
            Moved(
                "refresh-force-forgets-row-gone-on-both-sides-and-same-key-is-inserted",
                [new AddFolderOp(SV, Direct, 5, null, "e5")],
                [
                    new DeleteFolderOp(D1, Direct, 5),
                    new DeleteFolderOp(SV, Direct, 5),
                    RefreshAll(D1, force: true),
                    Expect.Result(D1).Contains("discarded-changes=1"),
                    Expect.Rows(D1, F).Absent("5 *"),
                    new AddFolderOp(D1, Direct, 5, null, "again"),
                    Plain(D1),
                    Expect.Result(D1).Contains("uploaded=1").Absent("conflict *"),
                    Expect.Rows(SV, F).Contains("5 parent=null name=again"),
                ]
            ),
            // 拒否の順：範囲の中に未送信（フォルダーの編集）があり、範囲の外の表（イベント）の行が範囲の中の行（品目 999）を
            // 参照すると、未送信の例外より先に範囲の外の参照で断る（Force で抜けられない拒否を先に出す）。端末は変わらず、
            // 次の同期が編集を送る
            Moved(
                "refresh-refusal-order-outside-reference-before-pending",
                [.. Seed(1, "alice", 11, "widget")],
                [
                    Rename(D1, 1, "offline-edit"),
                    new ExpectThrowOp(D1),
                    RefreshAll(D1, excluded: [ModelTables.Events]),
                    Expect
                        .Result(D1)
                        .Contains(
                            "exception SyncRefreshScopeException: *",
                            $"scope-reference {ModelTables.Events}->{I}"
                        ),
                    Expect.Rows(D1, F).Contains("1 parent=null name=offline-edit"),
                    Expect.Rows(D1, I).Contains("11 *", "999 *"),
                    Plain(D1),
                    Expect.Result(D1).Contains("uploaded=1").Absent("conflict *"),
                    Expect.Rows(SV, F).Contains("1 parent=null name=offline-edit"),
                ]
            ),
            // 拒否の順：上と同じ形で blob の指定が無い（範囲の品目の表に除外した無制限バイナリ列がある）と、範囲の外の参照より
            // 先に blob の損失で断る。端末は変わらない
            Moved(
                "refresh-refusal-order-blob-loss-before-outside-reference",
                [.. Seed(1, "alice", 11, "widget")],
                [
                    Rename(D1, 1, "offline-edit"),
                    new ExpectThrowOp(D1),
                    new RefreshWithOptionsOp(
                        D1,
                        SyncMode.LastWriteWins,
                        false,
                        false,
                        false,
                        [ModelTables.Events]
                    ),
                    Expect
                        .Result(D1)
                        .Contains(
                            "exception SyncUnboundedBinaryLossException: *Payload*",
                            $"blob-loss {I} cols=(Payload)"
                        ),
                    Expect.Rows(D1, F).Contains("1 parent=null name=offline-edit"),
                    Expect.Rows(D1, I).Contains("11 *", "999 *"),
                    Plain(D1),
                    Expect.Result(D1).Contains("uploaded=1").Absent("conflict *"),
                ]
            ),
            Moved(
                "refresh-written-rows-are-not-sent-by-next-sync",
                [],
                [
                    .. Seed(1, "alice", 11, "widget"),
                    .. Seed(2, "bob", 12, "gadget"),
                    RefreshAll(D1),
                    Expect
                        .Rows(D1, F)
                        .Contains("1 parent=null name=alice", "2 parent=null name=bob"),
                    Plain(D1),
                    Expect.Result(D1).Contains("uploaded=0").Absent("conflict *"),
                    Expect
                        .Rows(SV, F)
                        .Exact("1 parent=null name=alice", "2 parent=null name=bob", "999 *"),
                ]
            ),
            Moved(
                "refresh-excluding-referring-child-refuses-and-next-sync-sends-edit",
                [.. Seed(1, "alice", 11, "widget")],
                [
                    new MakeLocalCascadeOp(D1, I, F),
                    new UpdateItemOp(D1, Direct, 11, 1, null, "c-11", "edited-offline"),
                    new ExpectThrowOp(D1),
                    RefreshAll(D1, excluded: [I]),
                    Expect
                        .Result(D1)
                        .Contains(
                            "exception SyncRefreshScopeException: *",
                            $"scope-reference {I}->{F}"
                        ),
                    Expect.Rows(D1, F).Contains("1 parent=null name=alice"),
                    Expect
                        .Rows(D1, I)
                        .Contains("11 folder=1 label=null code=c-11 title=edited-offline"),
                    Plain(D1),
                    Expect.Result(D1).Contains("uploaded=1").Absent("conflict *"),
                    Expect
                        .Rows(SV, I)
                        .Contains("11 folder=1 label=null code=c-11 title=edited-offline"),
                ]
            ) with
            {
                OmitsTail = true,
            },
            Moved(
                "refresh-honors-foreign-key-order",
                [.. Seed(1, "alice", 11, "widget")],
                [
                    Expect.Rows(D1, I).Exact("11 *", "999 *"),
                    RefreshAll(D1),
                    Expect.Result(D1).Contains("deleted=0", "inserted=5"),
                    Expect.Rows(D1, I).Contains("11 folder=1 *"),
                ]
            ),
            Moved(
                "refresh-leaves-local-only-tables-alone",
                [],
                [
                    new LocalSqlOp(
                        D1,
                        "CREATE TABLE local_notes (note_id INTEGER PRIMARY KEY, body TEXT NOT NULL)"
                    ),
                    new LocalSqlOp(
                        D1,
                        "INSERT INTO local_notes (note_id, body) VALUES (1, 'kept')"
                    ),
                    .. Seed(1, "alice", 11, "widget"),
                    RefreshAll(D1),
                    new ExpectLocalSqlOp(D1, "SELECT COUNT(*) FROM local_notes", "1"),
                ]
            ),
            Moved(
                "refresh-continues-across-batches",
                [],
                [
                    Folder(SV, 1, name: "customer-1"),
                    Folder(SV, 2, name: "customer-2"),
                    Folder(SV, 3, name: "customer-3"),
                    Folder(SV, 4, name: "customer-4"),
                    Folder(SV, 5, name: "customer-5"),
                    RefreshAll(D1, batchSize: 2),
                    Expect.Result(D1).Contains("inserted=8"),
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
                "refresh-non-positive-batch-size-throws-before-deleting",
                [.. Seed(1, "alice", 11, "widget")],
                [
                    new ExpectThrowOp(D1),
                    RefreshAll(D1, batchSize: 0),
                    Expect.Result(D1).Contains("exception ArgumentOutOfRangeException: *"),
                    Expect.Rows(D1, F).Exact("1 *", "999 *"),
                ]
            ),
            Moved(
                "blob-default-does-not-download",
                [],
                [
                    .. Seed(1, "alice", 11, "widget"),
                    new WritePayloadOp(SV, 11, "01020304"),
                    Sync(D1, blobs: false),
                    Expect.Result(D1).Absent("conflict *"),
                    Expect.Rows(D1, F).Contains("1 parent=null name=alice"),
                    new ExpectPayloadOp(D1, 11, ""),
                    new ModelRefreshOp(D1, SyncMode.LastWriteWins, IncludeBlobs: true),
                ]
            ),
            Moved(
                "blob-default-does-not-upload",
                [],
                [
                    Line(D1, 5, 999, "dave"),
                    new WritePayloadOp(D1, 5, "090909"),
                    Sync(D1, blobs: false),
                    Expect.Result(D1).Contains("uploaded=1"),
                    Expect.Rows(SV, I).Contains("5 folder=999 label=null code=c-5 title=dave"),
                    new ExpectPayloadOp(SV, 5, ""),
                    new WritePayloadOp(D1, 5, "090909"),
                    Plain(D1),
                    new ExpectPayloadOp(SV, 5, "090909"),
                ]
            ),
            Moved(
                "blob-default-preserves-existing-local-blobs-new-rows-arrive-empty",
                [.. Seed(1, "alice", 11, "widget")],
                [
                    new LocalSqlOp(D1, $"UPDATE {I} SET payload = X'0707' WHERE item_id = 11")
                    {
                        Untracked = true,
                    },
                    new UpdateItemOp(SV, Direct, 11, 1, null, "c-11", "widget-updated"),
                    new WritePayloadOp(SV, 11, "010101"),
                    .. Seed(2, "bob", 12, "gadget"),
                    new WritePayloadOp(SV, 12, "0202"),
                    Sync(D1, blobs: false),
                    Expect
                        .Rows(D1, I)
                        .Contains("11 folder=1 label=null code=c-11 title=widget-updated"),
                    new ExpectPayloadOp(D1, 11, "0707"),
                    new ExpectPayloadOp(D1, 12, ""),
                    new ModelRefreshOp(D1, SyncMode.LastWriteWins, IncludeBlobs: true),
                ]
            ),
            Moved(
                "blob-include-downloads-blobs",
                [],
                [
                    .. Seed(1, "alice", 11, "widget"),
                    new WritePayloadOp(SV, 11, "01020304"),
                    Plain(D1),
                    new ExpectPayloadOp(D1, 11, "01020304"),
                    new WritePayloadOp(SV, 11, "0506"),
                    Plain(D1),
                    new ExpectPayloadOp(D1, 11, "0506"),
                ]
            ),
        ];
}
