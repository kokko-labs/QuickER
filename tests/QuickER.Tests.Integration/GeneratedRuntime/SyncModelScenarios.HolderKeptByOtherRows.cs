using System;
using System.Collections.Generic;
using System.Linq;
using QuickER.Tests.GeneratedSyncModelFixture;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// 一意な値の持ち主（サーバーに無いローカルの行）を、別の表の送るものの無い行が引き留める形。引き留める行のサーバーの行は、
/// 持ち主の値を待つ行の下にあるので先に入れられず、持ち主も消せない。解決役は、引き留める行がどれも送るものを持たない
/// （未送信の変更が無いか、その実行が記録を捨てる）とき、それらを子から消し、持ち主を消して、待っていた行を入れた後に
/// サーバーの写しで入れ直す（<c>SyncParentResolver.RemoveRowsKeepingHoldersAsync</c>）。
/// </summary>
internal static partial class SyncModelScenarios
{
    /// <summary>フォルダーの形の手（端末 2 台・最初の行なし）</summary>
    /// <remarks>
    /// 端末が作ったフォルダー 1781 の名前を、サーバーの他者が先にフォルダー 3781 で使い、端末の 1781 の下の品目 1782 と
    /// 同じキーの品目を、サーバーでは 3781 の子 3782 の下に作る。端末の同期（Collect）は、フォルダー 1781 を
    /// UniqueValueTakenOnServer・品目 1782 を DuplicateOnServer で報告し、フォルダーの表は 3781 で止まる。
    /// </remarks>
    private static IReadOnlyList<ModelOp> HolderKeptByItemOps() =>
        [
            Folder(D1, 1781, name: "h1781"),
            Item(D1, 1782, 1781),
            Folder(SV, 3781, name: "h1781"),
            Folder(SV, 3782, 3781),
            Item(SV, 1782, 3782, code: "c-sv-1782"),
            Sync(D1),
        ];

    /// <summary>端末の品目 1782 の行（端末が作った内容）</summary>
    private const string LocalHolderReferrer = "folder=1781 label=null code=c-1782 title=t1782";

    /// <summary>サーバーの品目 1782 の行</summary>
    private const string ServerHolderReferrer = "folder=3782 label=null code=c-sv-1782 title=t1782";

    /// <summary>ServerWins の同期 1 回で決着したこと（持ち主が消え、待っていたフォルダーと品目のサーバーの写しが入る）</summary>
    private static IEnumerable<ModelOp> ExpectHolderKeptByItemSettled() =>
        [
            new ExpectLocalRowChangeOp(
                D1,
                ModelTables.Items,
                "1782",
                SyncLocalRowChangeReason.RemovedForUniqueValue
            ),
            new ExpectLocalOp(D1, ModelTables.Folders, "1781", null),
            new ExpectLocalOp(D1, ModelTables.Folders, "3781", "parent=null name=h1781"),
            new ExpectLocalOp(D1, ModelTables.Items, "1782", ServerHolderReferrer),
        ];

    private static IEnumerable<SyncModelScenario> HolderKeptByOtherRowsScenarios()
    {
        // 広い世界の種 120022 の形を縮めたもの：静止化の ServerWins の周が、品目 1782（記録を捨てる行）を消して持ち主 1781 を
        // 消し、3781・3782 を入れてから品目 1782 をサーバーの写しで入れ直す
        yield return new("holder-referenced-by-other-table-settles", [], HolderKeptByItemOps());

        // ServerWins の同期 1 回で、その同期の中で決着する（後勝ち＋ServerWins も同じ）
        yield return new(
            "holder-referenced-by-other-table-serverwins-settles-in-one-sync",
            [],
            [
                .. HolderKeptByItemOps(),
                Sync(D1, policy: SyncConflictPolicy.ServerWins),
                .. ExpectHolderKeptByItemSettled(),
            ]
        );
        yield return new(
            "holder-referenced-by-other-table-lww-serverwins-settles-in-one-sync",
            [],
            [
                .. HolderKeptByItemOps(),
                Sync(D1, SyncMode.LastWriteWins, SyncConflictPolicy.ServerWins),
                .. ExpectHolderKeptByItemSettled(),
            ]
        );

        // Collect は、引き留める行に送るものがある（品目 1782 の未送信の挿入）ので消さず、競合を報告し続ける（docs の抜け方は
        // 値を変えるか ServerWins）
        yield return new(
            "holder-referenced-by-other-table-collect-keeps-unsent-referrer",
            [],
            [
                .. HolderKeptByItemOps(),
                Sync(D1),
                new ExpectLocalOp(D1, ModelTables.Items, "1782", LocalHolderReferrer),
                new ExpectLocalOp(D1, ModelTables.Folders, "1781", "parent=null name=h1781"),
            ]
        );

        // 削除を伝搬しない実行は、持ち主を消さないのと同じく、引き留める行も消さない（docs：サーバーに無い行は伝搬が有効な
        // ときに消える）
        yield return new(
            "holder-referenced-by-other-table-propagation-off-keeps-referrer",
            [],
            [
                .. HolderKeptByItemOps(),
                Sync(D1, policy: SyncConflictPolicy.ServerWins, propagateDeletes: false),
                new ExpectLocalOp(D1, ModelTables.Items, "1782", LocalHolderReferrer),
                new ExpectLocalOp(D1, ModelTables.Folders, "1781", "parent=null name=h1781"),
            ]
        );

        // 記録を捨ててから行を消す：記録の削除が断られた実行は、行を消さずに例外で終わる（逆の順なら行だけが消え、作り直した
        // 行の削除の記録が残り得る）。断りを外した次の ServerWins の同期で決着する
        yield return new(
            "holder-referenced-by-other-table-entries-go-before-rows",
            [],
            [
                .. HolderKeptByItemOps(),
                new RefuseJournalRemovalOp(D1, ModelTables.Items, "1782", Refuse: true),
                new ExpectThrowOp(D1),
                Sync(D1, policy: SyncConflictPolicy.ServerWins),
                new ExpectLocalOp(D1, ModelTables.Items, "1782", LocalHolderReferrer),
                new RefuseJournalRemovalOp(D1, ModelTables.Items, "1782", Refuse: false),
                Sync(D1, policy: SyncConflictPolicy.ServerWins),
                .. ExpectHolderKeptByItemSettled(),
            ]
        );

        // 夜間の種 400 の形を縮めたもの（品目の表）：端末が作った品目 1791 の code を、サーバーの他者が先に品目 3791 で使い、
        // 端末の 1791 を参照するイベントと同じキーのイベントを、サーバーでは 3791 を参照して作る
        var eventAt = new DateTime(2026, 6, 1, 9, 0, 0, DateTimeKind.Unspecified);
        yield return new(
            "holder-referenced-by-other-table-items-settles",
            [Folder(SV, 790)],
            [
                Item(D1, 1791, 790, code: "h1791"),
                new AddEventOp(D1, Direct, eventAt, 1791, "local"),
                Item(SV, 3791, 790, code: "h1791"),
                new AddEventOp(SV, Direct, eventAt, 3791, "server"),
                Sync(D1),
            ]
        );

        // 広い世界の種 120055 の形を縮めたもの：持ち主 1781 を直接引き留めるのはラベル（未送信・サーバーに無い）で、その
        // ラベルを未送信の品目 1782 が参照する。ラベルの表は版を持たないので、後勝ちの実行の範囲にだけ入る（静止化の後勝ち＋
        // ServerWins の周が、品目とラベルを消して持ち主を消す）
        yield return new(
            "holder-referenced-by-other-table-through-label-settles",
            [Folder(SV, 790)],
            [
                Folder(D1, 1781, name: "h1781"),
                new AddLabelOp(D1, Direct, "lab", 1781, "kept"),
                Item(D1, 1782, 790, label: "lab"),
                Folder(SV, 3781, name: "h1781"),
                Folder(SV, 3782, 3781),
                Item(SV, 1782, 3782, code: "c-sv-1782"),
                Sync(D1),
            ]
        );

        // 未送信の無い古い写しが持ち主を引き留める形（サーバーの操作だけで起きる）：サーバーで品目 811 を新しいフォルダー 813
        // へ移し、元のフォルダー 810 を消し、813 が 810 の名前を取る。端末は品目 811 を 810 の下の古い写しのまま持つ。同期
        // （Collect）1 回で、古い写しを消して 810 を消し、813 と品目 811 のサーバーの写しが入る
        yield return new(
            "holder-referenced-by-other-table-stale-copy-settles",
            [Folder(SV, 810, name: "h810"), Item(SV, 811, 810)],
            [
                .. StaleCopyOps(),
                Sync(D1),
                new ExpectLocalRowChangeOp(
                    D1,
                    ModelTables.Items,
                    "811",
                    SyncLocalRowChangeReason.RemovedForUniqueValue
                ),
                new ExpectLocalOp(D1, ModelTables.Folders, "810", null),
                new ExpectLocalOp(D1, ModelTables.Folders, "813", "parent=null name=h810"),
                new ExpectLocalOp(
                    D1,
                    ModelTables.Items,
                    "811",
                    "folder=813 label=null code=c-811 title=t811"
                ),
            ]
        );

        // 古い写しを端末が編集していれば（未送信の更新）、持ち主に未送信の変更が無くても消さない。Collect の同期は編集を
        // ModifiedOnServer で報告して残し、静止化の ServerWins の周が編集を捨てて決着する
        yield return new(
            "holder-referenced-by-other-table-edited-stale-copy-is-kept",
            [Folder(SV, 810, name: "h810"), Item(SV, 811, 810)],
            [
                .. StaleCopyOps(),
                new UpdateItemOp(D1, Direct, 811, 810, null, "c-811", "edited"),
                Sync(D1),
                new ExpectLocalOp(
                    D1,
                    ModelTables.Items,
                    "811",
                    "folder=810 label=null code=c-811 title=edited"
                ),
                new ExpectLocalOp(D1, ModelTables.Folders, "810", "parent=null name=h810"),
            ]
        );

        // その実行で除外した表の行は消さない（範囲の外の表の行が持ち主を引き留めると、何も消さない）。除外をやめた同期で決着する
        yield return new(
            "holder-referenced-by-other-table-stale-copy-in-excluded-table-is-kept",
            [Folder(SV, 810, name: "h810"), Item(SV, 811, 810)],
            [
                .. StaleCopyOps(),
                Sync(D1, excluded: [ModelTables.Items]),
                new ExpectLocalOp(
                    D1,
                    ModelTables.Items,
                    "811",
                    "folder=810 label=null code=c-811 title=t811"
                ),
                new ExpectLocalOp(D1, ModelTables.Folders, "810", "parent=null name=h810"),
            ]
        );

        yield return CurrentChildScenario();
    }

    /// <summary>
    /// 消すと降りてこない最新の写しの行がある形：持ち主 1781 を引き留める品目 1782（端末が作った同じキーの行）の下へ、
    /// サーバーの品目 1782 の子のイベントがキーでつながって最新の写しとして降り、同じ表にそれより後ろの版のイベント
    /// （品目 795 の下）が残る。消したイベントの版は残る行が導く再開点より下なので二度と降りず、解決役は何も消さない。
    /// 静止化しない（既知の失敗の一覧に Pending で載せる。分類は holder-referenced-by-other-table-current-child）
    /// </summary>
    private static SyncModelScenario CurrentChildScenario()
    {
        var at = new DateTime(2026, 6, 1, 9, 0, 0, DateTimeKind.Unspecified);

        return new(
            "holder-referenced-by-other-table-current-child-does-not-settle",
            [Folder(SV, 790), Item(SV, 795, 790)],
            [
                .. HolderKeptByItemOps().SkipLast(1),
                new AddEventOp(SV, Direct, at, 1782, "server-child"),
                new AddEventOp(SV, Direct, at.AddMinutes(1), 795, "later"),
                Sync(D1),
            ]
        );
    }

    /// <summary>古い写しの形のサーバーの手（端末 1 が初期データをそろえた後）</summary>
    private static IReadOnlyList<ModelOp> StaleCopyOps() =>
        [
            Sync(D1),
            Folder(SV, 813, name: "r813"),
            new UpdateItemOp(SV, Direct, 811, 813, null, "c-811", "t811"),
            new DeleteFolderOp(SV, Direct, 810),
            new UpdateFolderOp(SV, Direct, 813, null, "h810"),
        ];
}
