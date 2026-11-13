using System.Collections.Generic;
using QuickER.Tests.GeneratedSyncModelFixture;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// SyncRuntimeTestsBase から移したシナリオ（外部キーで同期が止まらないこと＝祖先を残す削除伝搬・ServerWins の適用順・
/// サーバーの拒否の報告と後勝ち＋ServerWins の決着、同期の実行中のローカルの書き込み、失敗した書き込みの記録の取り消し）。
/// 表の写し方は <see cref="EngineBasicScenarios"/> の型の注と同じ。
/// </summary>
internal static partial class SyncModelScenarios
{
    private static IEnumerable<SyncModelScenario> EngineForeignKeysScenarios() =>
        [
            Moved(
                "dl-non-positive-batch-size-throws-before-upload-changes-anything",
                [.. Seed(1, "alice", 11, "widget")],
                [
                    Rename(D1, 1, "alice-local"),
                    new ExpectThrowOp(D1),
                    Plain(D1, policy: SyncConflictPolicy.ServerWins, batch: 0),
                    Expect.Result(D1).Contains("exception ArgumentOutOfRangeException: *"),
                    Expect.Rows(D1, F).Contains("1 parent=null name=alice-local"),
                    Expect.Journal(D1).Count("*", 1),
                ]
            ),
            Moved(
                "fk-serverwins-restores-row-moved-to-new-parent-on-server",
                [.. Seed(1, "alice", 11, "widget")],
                [
                    new UpdateItemOp(D1, Direct, 11, 1, null, "c-11", "widget-local"),
                    Folder(SV, 2, name: "bob"),
                    new UpdateItemOp(SV, Direct, 11, 2, null, "c-11", "widget"),
                    Plain(D1, policy: SyncConflictPolicy.ServerWins),
                    Expect.Result(D1).Absent("conflict *"),
                    Expect.Rows(D1, I).Contains("11 folder=2 label=null code=c-11 title=widget"),
                    Expect.Journal(D1).Empty(),
                ]
            ),
            Moved(
                "fk-propagation-keeps-parents-of-rows-it-keeps",
                [.. Seed(1, "alice", 11, "widget")],
                [
                    .. EditedItemWhoseFolderWasDeletedOnServer(),
                    Plain(D1),
                    Expect
                        .Result(D1)
                        .Contains("deleted-locally=0")
                        .Count("conflict *", 1)
                        .Single("conflict * reason=MissingOnServer *"),
                    Plain(D1),
                    Expect
                        .Result(D1)
                        .Contains("deleted-locally=0")
                        .Count("conflict *", 1)
                        .Single("conflict * reason=MissingOnServer *"),
                    Expect.Rows(D1, I).Contains("11 *"),
                    Expect.Rows(D1, F).Contains("1 *"),
                ]
            ),
            .. ParentMissingReportedScenarios(),
            Moved(
                "fk-lww-serverwins-settles-parent-missing-on-server",
                [.. Seed(1, "alice", 11, "widget")],
                [
                    .. EditedItemWhoseFolderWasDeletedOnServer(),
                    Plain(D1, mode: SyncMode.LastWriteWins, policy: SyncConflictPolicy.ServerWins),
                    Expect.Result(D1).Contains("discarded=1").Absent("conflict *"),
                    Expect.Journal(D1).Empty(),
                    Expect.Rows(SV, F).Absent("1 *"),
                    Expect.Rows(D1, I).Absent("11 *"),
                    Expect.Rows(D1, F).Absent("1 *"),
                ]
            ),
            Moved(
                "fk-lww-serverwins-settles-delete-refused-on-server",
                [Folder(SV, 1, name: "alice"), Folder(SV, 2, name: "bob")],
                [
                    .. LocallyDeletedFolderWithItemAddedOnServer(),
                    Plain(D1, mode: SyncMode.LastWriteWins, policy: SyncConflictPolicy.ServerWins),
                    Expect.Result(D1).Contains("discarded=1").Absent("conflict *", "truncation *"),
                    Expect.Rows(D1, F).Contains("1 *"),
                    Expect.Rows(D1, I).Contains("21 *"),
                    Expect.Journal(D1).Empty(),
                ]
            ),
            Moved(
                "fk-lww-serverwins-uploads-ordinary-changes",
                [.. Seed(1, "alice", 11, "widget")],
                [
                    Rename(D1, 1, "local-edit"),
                    Rename(SV, 1, "server-edit"),
                    Plain(D1, mode: SyncMode.LastWriteWins, policy: SyncConflictPolicy.ServerWins),
                    Expect.Result(D1).Contains("uploaded=1").Absent("conflict *"),
                    Expect.Rows(SV, F).Contains("1 parent=null name=local-edit"),
                ]
            ),
            Moved(
                "fk-versionless-download-reports-held-back-row-after-scan",
                [
                    Folder(SV, 1, name: "alice"),
                    Folder(SV, 2, name: "bob"),
                    new AddLabelOp(SV, Direct, "L101", 1, "a"),
                    new AddLabelOp(SV, Direct, "L102", 1, "b"),
                ],
                [
                    new UpdateLabelOp(D1, Direct, "L101", 2, "a"),
                    new DeleteFolderOp(SV, Direct, 2),
                    new UpdateLabelOp(SV, Direct, "L102", 1, "b2"),
                    Plain(D1, mode: SyncMode.LastWriteWins),
                    Expect
                        .Result(D1)
                        .Count("conflict *", 1)
                        .Single("conflict * reason=ParentMissingOnServer *")
                        .Count($"truncation {L} *", 1)
                        .Single($"truncation {L} pending=L101 missing-parent=- *"),
                    Expect.Rows(D1, L).Contains("L102 folder=1 caption=b2"),
                ]
            ),
            Moved(
                "fk-child-of-excluded-parent-table-stops-and-names-parent-table",
                [],
                [
                    .. Seed(1, "alice", 11, "widget"),
                    Plain(D1, excluded: [F]),
                    Expect
                        .Result(D1)
                        .Count($"truncation {I} *", 1)
                        .Single(
                            $"truncation {I} pending=11 missing-parent={F} skipped-by-hook=* truncated-by=-"
                        ),
                    Expect.Rows(D1, F).Absent("1 *"),
                    Expect.Rows(D1, I).Absent("11 *"),
                    Plain(D1),
                    Expect.Result(D1).Absent("truncation *"),
                    Expect.Rows(D1, I).Contains("11 *"),
                ]
            ),
            .. DeleteRefusedReportedScenarios(),
            Moved(
                "fk-delete-refused-on-server-is-settled-by-serverwins",
                [Folder(SV, 1, name: "alice"), Folder(SV, 2, name: "bob")],
                [
                    .. LocallyDeletedFolderWithItemAddedOnServer(),
                    Plain(D1, policy: SyncConflictPolicy.ServerWins),
                    Expect.Result(D1).Absent("conflict *", "truncation *"),
                    Expect.Rows(D1, F).Contains("1 *"),
                    Expect.Rows(D1, I).Contains("21 *"),
                    Expect.Journal(D1).Empty(),
                ]
            ),
            Moved(
                "sw-leaves-row-edited-during-the-run",
                [.. Seed(1, "alice", 11, "widget")],
                [
                    Rename(D1, 1, "alice-before"),
                    new ArmInterruptOp(
                        D1,
                        ModelInterruptPoint.BeforeServerFetch,
                        Rename(D1, 1, "alice-during")
                    ),
                    Plain(D1, policy: SyncConflictPolicy.ServerWins),
                    Expect.Rows(D1, F).Contains("1 parent=null name=alice-during"),
                    // 内容を戻さなかった行は、実行が読んだ記録（old）も新しい記録（new）と一緒に残る（P5-2 の S2）
                    Expect.Journal(D1).Count("*", 2).Single("* age=new").Single("* age=old"),
                    Plain(D1),
                    Expect.Journal(D1).Empty(),
                    Expect.Rows(SV, F).Contains("1 parent=null name=alice-during"),
                ]
            ),
            Moved(
                "sw-does-not-stop-download-at-rows-it-drops",
                [.. Seed(1, "alice", 11, "widget"), .. Seed(2, "bob", 12, "gadget")],
                [
                    Rename(D1, 1, "alice-local"),
                    new UpdateItemOp(D1, Direct, 12, 2, null, "c-12", "gadget-local"),
                    Rename(SV, 1, "alice-server"),
                    Rename(SV, 2, "bob-server"),
                    new UpdateItemOp(SV, Direct, 11, 1, null, "c-11", "widget-server"),
                    Plain(D1, policy: SyncConflictPolicy.ServerWins),
                    Expect.Result(D1).Absent("truncation *"),
                    Expect
                        .Rows(D1, F)
                        .Contains(
                            "1 parent=null name=alice-server",
                            "2 parent=null name=bob-server"
                        ),
                    Expect
                        .Rows(D1, I)
                        .Contains(
                            "11 folder=1 label=null code=c-11 title=widget-server",
                            "12 folder=2 label=null code=c-12 title=gadget"
                        ),
                    Expect.Journal(D1).Empty(),
                ]
            ),
            Moved(
                "conc-local-insert-during-download-is-not-deleted-by-propagation",
                [.. Seed(1, "alice", 11, "widget")],
                [
                    new ArmInterruptOp(
                        D1,
                        ModelInterruptPoint.BeforeServerFetch,
                        Folder(D1, 99, name: "made-during-sync")
                    ),
                    Plain(D1),
                    Expect.Result(D1).Contains("deleted-locally=0"),
                    Expect.Rows(D1, F).Contains("99 *"),
                    Plain(D1),
                    Expect.Result(D1).Contains("uploaded=1"),
                    Expect.Rows(SV, F).Contains("99 parent=null name=made-during-sync"),
                ]
            ),
            Moved(
                "conc-local-edit-during-download-is-not-overwritten-by-batch",
                [.. Seed(1, "alice", 11, "widget")],
                [
                    Rename(SV, 1, "alice-server"),
                    new ArmInterruptOp(
                        D1,
                        ModelInterruptPoint.AfterServerFetchBeforeApply,
                        Rename(D1, 1, "alice-local")
                    ),
                    Plain(D1),
                    Expect.Rows(D1, F).Contains("1 parent=null name=alice-local"),
                    Expect.Result(D1).Count("truncation * pending=1 *", 1),
                ]
            ),
            Moved(
                "rec-failed-update-leaves-no-journal-entry",
                [.. Seed(1, "alice", 11, "widget")],
                [
                    new UpdateItemOp(D1, Direct, 11, 9999, null, "c-11", "orphan")
                    {
                        ExpectFailure = true,
                    },
                    Expect.Journal(D1).Empty(),
                ]
            ),
            Moved(
                "rec-failed-blob-write-leaves-no-journal-entry",
                [.. Seed(1, "alice", 11, "widget")],
                [
                    new WritePayloadOp(D1, 11, "0A0B0C") { ExpectFailure = true },
                    Expect.Journal(D1).Empty(),
                ]
            ),
            Moved(
                "rec-failed-update-keeps-earlier-entries-of-same-row",
                [.. Seed(1, "alice", 11, "widget")],
                [
                    new UpdateItemOp(D1, Direct, 11, 1, null, "c-11", "edited"),
                    new UpdateItemOp(D1, Direct, 11, 9999, null, "c-11", "edited")
                    {
                        ExpectFailure = true,
                    },
                    Expect.Journal(D1).Exact($"{I} key=11 *"),
                    Plain(D1),
                    Expect.Result(D1).Absent("conflict *"),
                    Expect.Rows(SV, I).Contains("11 folder=1 label=null code=c-11 title=edited"),
                ]
            ),
            Moved(
                "ack-serverwins-applied-without-download-then-edit-is-not-a-false-modified-conflict",
                [.. Seed(1, "alice", 11, "widget")],
                [
                    Rename(D1, 1, "alice-local"),
                    Rename(SV, 1, "alice-server"),
                    new ArmEmptyFetchOp(D1, F),
                    Plain(D1, policy: SyncConflictPolicy.ServerWins),
                    Expect.Rows(D1, F).Contains("1 parent=null name=alice-server"),
                    Rename(D1, 1, "alice-2"),
                    Plain(D1),
                    Expect.Result(D1).Contains("uploaded=1").Absent("conflict *"),
                    Expect.Rows(SV, F).Contains("1 parent=null name=alice-2"),
                ]
            ),
        ];

    /// <summary>WriteOfRowWhoseParentIsGoneOnServer_IsReportedAsParentMissing の 2 行</summary>
    private static IEnumerable<SyncModelScenario> ParentMissingReportedScenarios() =>
        [
            ParentMissingReported(
                "versioned-localwins",
                SyncMode.Versioned,
                SyncConflictPolicy.LocalWins
            ),
            ParentMissingReported(
                "lww-collect",
                SyncMode.LastWriteWins,
                SyncConflictPolicy.Collect
            ),
        ];

    private static SyncModelScenario ParentMissingReported(
        string shape,
        SyncMode mode,
        SyncConflictPolicy policy
    ) =>
        Moved(
            $"fk-write-of-row-whose-parent-is-gone-is-reported-as-parent-missing-{shape}",
            [.. Seed(1, "alice", 11, "widget")],
            [
                .. EditedItemWhoseFolderWasDeletedOnServer(),
                Plain(D1, mode: mode, policy: policy),
                Expect
                    .Result(D1)
                    .Count("conflict *", 1)
                    .Single($"conflict {I} key=11 op=Upsert reason=ParentMissingOnServer *"),
                Plain(D1, mode: mode, policy: policy),
                Expect
                    .Result(D1)
                    .Count("conflict *", 1)
                    .Single($"conflict {I} key=11 op=Upsert reason=ParentMissingOnServer *"),
                Expect.Rows(SV, F).Absent("1 *"),
                Expect.Journal(D1).Count("*", 1),
            ]
        );

    /// <summary>DeleteOfParentWithChildAddedOnServer_IsReportedAndTheChildIsHeldBack の 2 行</summary>
    private static IEnumerable<SyncModelScenario> DeleteRefusedReportedScenarios() =>
        [
            DeleteRefusedReported("collect", SyncConflictPolicy.Collect),
            DeleteRefusedReported("localwins", SyncConflictPolicy.LocalWins),
        ];

    private static SyncModelScenario DeleteRefusedReported(
        string shape,
        SyncConflictPolicy policy
    ) =>
        Moved(
            $"fk-delete-refused-on-server-holds-back-child-{shape}",
            [Folder(SV, 1, name: "alice"), Folder(SV, 2, name: "bob")],
            [
                .. LocallyDeletedFolderWithItemAddedOnServer(),
                Plain(D1, policy: policy),
                Expect
                    .Result(D1)
                    .Count("conflict *", 1)
                    .Single(
                        $"conflict {F} key=1 op=Delete reason=DeleteRefusedOnServer local=* "
                            + "server=parent=null name=alice"
                    )
                    .Contains($"truncation {I} pending=21 missing-parent={F} *"),
                Expect.Rows(D1, I).Absent("21 *"),
                Expect.Rows(SV, F).Contains("1 *"),
                Expect.Journal(D1).Count("*", 1),
            ]
        );

    /// <summary>
    /// ArrangeEditedLineWhoseOrderWasDeletedOnServerAsync の写し：品目 11 を端末で編集し、サーバーでは品目 11 と
    /// フォルダー 1 を消す
    /// </summary>
    private static ModelWriteOp[] EditedItemWhoseFolderWasDeletedOnServer() =>
        [
            new UpdateItemOp(D1, Direct, 11, 1, null, "c-11", "widget-local"),
            new DeleteItemOp(SV, Direct, 11),
            new DeleteFolderOp(SV, Direct, 1),
        ];

    /// <summary>
    /// ArrangeLocallyDeletedOrderWithChildAddedOnServerAsync の写し：端末でフォルダー 1 を消し、サーバーでは別端末が
    /// フォルダー 1 に品目 21 を足す（フォルダー 2 は版の高い行として再開点をフォルダー 1 より上に保つ＝Setup）
    /// </summary>
    private static ModelWriteOp[] LocallyDeletedFolderWithItemAddedOnServer() =>
        [new DeleteFolderOp(D1, Direct, 1), Line(SV, 21, 1, "added elsewhere")];

    /// <summary>
    /// 表どうしの循環（フォルダーが品目を指す列 pinned_item_id）を、洗い替え以外の経路で通る行。表の順ではフォルダーが品目より
    /// 先（循環に属する表は宣言順で 1 つ先に出す）なので、フォルダーが新しい品目を指すと、その辺は表の順と逆向きになる。
    /// 行どうしは循環させない（品目はピン留めするフォルダーの下にあっても、品目がフォルダーを指すだけ）
    /// </summary>
    private static IEnumerable<SyncModelScenario> CrossTableCycleScenarios() =>
        [
            // アップロード：フォルダーの更新（新しい品目へのピン留め）は表の順で品目より先に送られ、品目がまだ無いサーバーの
            // 外部キーに断られる。品目が通った次の巡で送り直されて通る（競合にしない）
            Moved(
                "cycle-pin-to-new-item-uploads-in-rounds",
                [Folder(SV, 760)],
                [
                    Line(D1, 761, 760, "pinned"),
                    new PinFolderOp(D1, Direct, 760, 761),
                    Plain(D1),
                    Expect.Result(D1).Contains("uploaded=2").Absent("conflict *"),
                    Expect.Rows(SV, F).Contains("760 parent=null name=f760 pinned=761"),
                    Expect.Journal(D1).Empty(),
                ]
            ),
            // ダウンロード：フォルダーの表を先に降ろすとき、ピン留めの先の品目がまだローカルに無いので、親をそろえる解決役が
            // その品目をサーバーから読み直して先に入れる（打ち切らない）
            Moved(
                "cycle-pinned-item-is-read-back-before-folder",
                [Folder(SV, 770)],
                [
                    Line(SV, 771, 770, "pinned"),
                    new PinFolderOp(SV, Direct, 770, 771),
                    Plain(D1),
                    Expect.Result(D1).Absent("truncation *", "conflict *"),
                    Expect.Rows(D1, F).Contains("770 parent=null name=f770 pinned=771"),
                    Expect.Rows(D1, I).Contains("771 *"),
                ]
            ),
            // ServerWins の読み直し（単独の UploadAsync＝後にダウンロードが続かないので、捨てる行を自分で読み直す）：読み直した
            // フォルダーが指す品目がまだローカルに無いので、解決役が先に入れる
            Moved(
                "cycle-serverwins-restores-folder-pinned-to-new-item",
                [Folder(SV, 780)],
                [
                    Line(SV, 782, 780, "pinned"),
                    new PinFolderOp(SV, Direct, 780, 782),
                    Rename(D1, 780, "local"),
                    PlainOnly(D1, ModelSyncKind.Upload, policy: SyncConflictPolicy.ServerWins),
                    Expect.Result(D1).Absent("conflict *"),
                    Expect.Rows(D1, F).Contains("780 parent=null name=f780 pinned=782"),
                    Expect.Rows(D1, I).Contains("782 *"),
                    Expect.Journal(D1).Empty(),
                ]
            ),
        ];
}
