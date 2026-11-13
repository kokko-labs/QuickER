using System;
using System.Collections.Generic;
using System.Globalization;
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

        foreach (var scenario in CurrentChildScenarios())
        {
            yield return scenario;
        }
    }

    /// <summary>最新の写しの子のイベントの時刻（キー）</summary>
    /// <remarks>静的なフィールドにしない（シナリオの一覧はほかの部分のファイルの静的な初期化で組まれ、初期化の順が決まらない）</remarks>
    private static DateTime CurrentChildAt => new(2026, 6, 1, 9, 0, 0, DateTimeKind.Unspecified);

    /// <summary>最新の写しの子のイベントのキーのテキスト（モデルの照合の形）</summary>
    private static string CurrentChildKey => ModelTables.EventKeyText(CurrentChildAt);

    /// <summary>最新の写しの子のイベントのキーのテキスト（ジャーナルの形＝<see cref="SyncResult.LocalRowChanges"/> が載せる形）</summary>
    private static string CurrentChildJournalKey =>
        CurrentChildAt.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff", CultureInfo.InvariantCulture);

    /// <summary>子のイベントをその同期が降ろし直す約束つきで消したこと</summary>
    private static ExpectLocalRowChangeOp ExpectChildRemoved() =>
        new(
            D1,
            ModelTables.Events,
            CurrentChildJournalKey,
            SyncLocalRowChangeReason.RemovedForUniqueValue
        );

    /// <summary>端末の降ろし直す約束（quicker_sync_row_pending）の件数を確かめる</summary>
    private static ExpectLocalSqlOp ExpectOwedRows(int count) =>
        new(
            D1,
            "SELECT COUNT(*) FROM quicker_sync_row_pending",
            count.ToString(CultureInfo.InvariantCulture)
        );

    /// <summary>端末のローカル DB の、1 つの文を断るトリガーを仕掛ける（名前と条件はシナリオの定数）</summary>
    private static LocalSqlOp RefuseLocal(string trigger, string when) =>
        new(
            D1,
            $"CREATE TRIGGER {trigger} {when} BEGIN SELECT RAISE(ABORT, 'refused by the scenario'); END"
        );

    /// <summary>仕掛けたトリガーを外す</summary>
    private static LocalSqlOp AllowLocal(string trigger) =>
        new(D1, $"DROP TRIGGER IF EXISTS {trigger}");

    /// <summary>SK-1 の形の手（持ち主 1781 を引き留める品目 1782 の下へ、サーバーの子のイベントが最新の写しとして降りる）</summary>
    /// <remarks>
    /// 端末が作った品目 1782 と同じキーのサーバーの品目 1782 の子のイベントが、キーでつながって端末の品目 1782 の下へ最新の
    /// 写しとして降り、同じ表にそれより後ろの版のイベント（品目 795 の下）が残る。消すイベントの版は残る行が導く再開点より
    /// 下なので、ダウンロードの流れでは二度と降りない
    /// </remarks>
    private static IReadOnlyList<ModelOp> CurrentChildOps() =>
        [
            .. HolderKeptByItemOps().SkipLast(1),
            new AddEventOp(SV, Direct, CurrentChildAt, 1782, "server-child"),
            new AddEventOp(SV, Direct, CurrentChildAt.AddMinutes(1), 795, "later"),
            Sync(D1),
        ];

    /// <summary>新しい形の初期データ：古い写しの形の品目 811 の下に、端末へ降りる子のイベントを置く</summary>
    private static IReadOnlyList<ModelWriteOp> StaleCopyWithChildSetup() =>
        [
            Folder(SV, 810, name: "h810"),
            Item(SV, 811, 810),
            new AddEventOp(SV, Direct, CurrentChildAt, 811, "child"),
        ];

    /// <summary>新しい形が同期 1 回で決着したこと（持ち主 810 が消え、813・品目 811・子のイベントがサーバーの写しで端末にある）</summary>
    private static IEnumerable<ModelOp> ExpectStaleCopyWithChildSettled() =>
        [
            new ExpectLocalOp(D1, ModelTables.Folders, "810", null),
            new ExpectLocalOp(D1, ModelTables.Folders, "813", "parent=null name=h810"),
            new ExpectLocalOp(
                D1,
                ModelTables.Items,
                "811",
                "folder=813 label=null code=c-811 title=t811"
            ),
            new ExpectLocalOp(D1, ModelTables.Events, CurrentChildKey, "item=811 memo=child"),
            ExpectOwedRows(0),
        ];

    /// <summary>
    /// 消すとダウンロードの流れで降りてこない最新の写しの行がある形（区切り SR）：解決役は、その行を降ろし直す約束
    /// （quicker_sync_row_pending）へ記録してから消し、その表のダウンロードの番にサーバーからキーで読み直して戻す
    /// （<c>SyncParentResolver.BringOwedRowsBackAsync</c>）
    /// </summary>
    private static IEnumerable<SyncModelScenario> CurrentChildScenarios()
    {
        // SK-1 の形：静止化の ServerWins の周で決着する（Collect の周は品目 1782 の未送信の挿入があるので消さない）
        yield return new(
            "holder-referenced-by-other-table-current-child-settles",
            [Folder(SV, 790), Item(SV, 795, 790)],
            CurrentChildOps()
        );

        // ServerWins の同期 1 回で、その同期の中で決着し、子のイベントがサーバーの写しで戻る（後勝ち＋ServerWins も同じ）
        foreach (
            var (suffix, mode) in new[]
            {
                ("serverwins", SyncMode.Versioned),
                ("lww-serverwins", SyncMode.LastWriteWins),
            }
        )
        {
            yield return new(
                $"holder-referenced-by-other-table-current-child-{suffix}-settles-in-one-sync",
                [Folder(SV, 790), Item(SV, 795, 790)],
                [
                    .. CurrentChildOps(),
                    Sync(D1, mode, SyncConflictPolicy.ServerWins),
                    .. ExpectHolderKeptByItemSettled(),
                    ExpectChildRemoved(),
                    new ExpectLocalOp(
                        D1,
                        ModelTables.Events,
                        CurrentChildKey,
                        "item=1782 memo=server-child"
                    ),
                    ExpectOwedRows(0),
                ]
            );
        }

        // Collect・LocalWins は、引き留める品目 1782 が送るもの（未送信の挿入）を持つので消さず、約束も作らない
        foreach (
            var (suffix, policy) in new[]
            {
                ("collect", SyncConflictPolicy.Collect),
                ("localwins", SyncConflictPolicy.LocalWins),
            }
        )
        {
            yield return new(
                $"holder-referenced-by-other-table-current-child-{suffix}-keeps-unsent-referrer",
                [Folder(SV, 790), Item(SV, 795, 790)],
                [
                    .. CurrentChildOps(),
                    Sync(D1, policy: policy),
                    new ExpectLocalOp(D1, ModelTables.Items, "1782", LocalHolderReferrer),
                    new ExpectLocalOp(
                        D1,
                        ModelTables.Events,
                        CurrentChildKey,
                        "item=1782 memo=server-child"
                    ),
                    ExpectOwedRows(0),
                ]
            );
        }

        // 新しい形（端末は何もしていない）：サーバーがフォルダーを作り直す手順（品目 811 を新しいフォルダー 813 へ移し、元の
        // 810 を消し、813 が 810 の名前を取る）で、品目 811 の子のイベントが端末へもう降りている。どのポリシーでも同期 1 回で
        // 決着し、子のイベントがサーバーの写しで戻る
        foreach (
            var (suffix, mode, policy) in new[]
            {
                ("collect", SyncMode.Versioned, SyncConflictPolicy.Collect),
                ("localwins", SyncMode.Versioned, SyncConflictPolicy.LocalWins),
                ("serverwins", SyncMode.Versioned, SyncConflictPolicy.ServerWins),
                ("lww-serverwins", SyncMode.LastWriteWins, SyncConflictPolicy.ServerWins),
            }
        )
        {
            yield return new(
                $"holder-referenced-by-other-table-stale-copy-with-current-child-settles-{suffix}",
                StaleCopyWithChildSetup(),
                [
                    .. StaleCopyOps(),
                    Sync(D1, mode, policy),
                    ExpectChildRemoved(),
                    .. ExpectStaleCopyWithChildSettled(),
                ]
            );
        }

        // 戻した子のイベントには受理記録が original として残る：端末が編集して上げると、競合なしにサーバーへ届く
        yield return new(
            "holder-referenced-by-other-table-current-child-read-back-row-uploads-its-edit",
            StaleCopyWithChildSetup(),
            [
                .. StaleCopyOps(),
                Sync(D1),
                .. ExpectStaleCopyWithChildSettled(),
                new UpdateEventOp(D1, Direct, CurrentChildAt, 811, "edited"),
                Sync(D1),
                new ExpectLocalOp(D1, ModelTables.Events, CurrentChildKey, "item=811 memo=edited"),
            ]
        );

        // 約束を書いてから行を消す：約束の書き込みが断られた実行は、行を消さずに例外で終わる（逆の順なら行だけが消え、戻す
        // 約束が無いまま残る）。断りを外した次の同期で決着する
        yield return new(
            "holder-referenced-by-other-table-current-child-owed-row-recorded-before-removal",
            StaleCopyWithChildSetup(),
            [
                .. StaleCopyOps(),
                RefuseLocal("sr_refuse_owed", "BEFORE INSERT ON quicker_sync_row_pending"),
                new ExpectThrowOp(D1),
                Sync(D1),
                new ExpectLocalOp(
                    D1,
                    ModelTables.Items,
                    "811",
                    "folder=810 label=null code=c-811 title=t811"
                ),
                new ExpectLocalOp(D1, ModelTables.Events, CurrentChildKey, "item=811 memo=child"),
                ExpectOwedRows(0),
                AllowLocal("sr_refuse_owed"),
                Sync(D1),
                .. ExpectStaleCopyWithChildSettled(),
            ]
        );

        // 約束を書いた後・行を消す前に止まった形：行と約束が残る。フォルダーの表を除外した次の同期は、イベントの表の番で
        // 「ローカルに同じキーの行がある」により約束だけを消す。除外をやめた同期で決着する
        yield return new(
            "holder-referenced-by-other-table-current-child-owed-row-dropped-when-row-is-still-there",
            StaleCopyWithChildSetup(),
            [
                .. StaleCopyOps(),
                RefuseLocal(
                    "sr_refuse_child_delete",
                    "BEFORE DELETE ON model_events WHEN OLD.memo = 'child'"
                ),
                new ExpectThrowOp(D1),
                Sync(D1),
                new ExpectLocalOp(D1, ModelTables.Events, CurrentChildKey, "item=811 memo=child"),
                ExpectOwedRows(1),
                AllowLocal("sr_refuse_child_delete"),
                Sync(D1, excluded: [ModelTables.Folders]),
                new ExpectLocalOp(D1, ModelTables.Events, CurrentChildKey, "item=811 memo=child"),
                ExpectOwedRows(0),
                Sync(D1),
                .. ExpectStaleCopyWithChildSettled(),
            ]
        );

        // 行を消した後・読み直す前に止まった形：約束が残るので、次の同期がサーバーから読み直して戻す
        yield return new(
            "holder-referenced-by-other-table-current-child-owed-row-survives-failed-run",
            StaleCopyWithChildSetup(),
            [.. FailedReadBackOps(), Sync(D1), .. ExpectStaleCopyWithChildSettled()]
        );

        // 読み直す前にサーバーで消えた行：約束を消し、行は端末に戻らない（削除伝搬と同じ結果）
        yield return new(
            "holder-referenced-by-other-table-current-child-owed-row-gone-on-server",
            StaleCopyWithChildSetup(),
            [
                .. FailedReadBackOps(),
                new DeleteEventOp(SV, Direct, CurrentChildAt),
                Sync(D1),
                new ExpectLocalOp(D1, ModelTables.Events, CurrentChildKey, null),
                ExpectOwedRows(0),
            ]
        );

        // 親がそろわない約束（端末が親の品目 811 を消し、サーバーが断った未送信の削除がある）：約束は残り、イベントの表の
        // ダウンロードは止まらず後ろの行を降ろす。静止化の ServerWins の周が削除を捨てて品目 811 を戻し、次の番で戻る
        yield return new(
            "holder-referenced-by-other-table-current-child-owed-row-waits-for-parent-with-unsent-delete",
            StaleCopyWithChildSetup(),
            [
                .. FailedReadBackOps(),
                new DeleteItemOp(D1, Direct, 811),
                new AddEventOp(SV, Direct, CurrentChildAt.AddMinutes(2), 999, "after-owed"),
                Sync(D1),
                new ExpectLocalOp(D1, ModelTables.Events, CurrentChildKey, null),
                ExpectOwedRows(1),
                new ExpectLocalOp(
                    D1,
                    ModelTables.Events,
                    ModelTables.EventKeyText(CurrentChildAt.AddMinutes(2)),
                    "item=999 memo=after-owed"
                ),
            ]
        );

        // 端末が同じキーで行を作った：約束を消し、端末の行を上書きしない（Collect は DuplicateOnServer を報告して残す。
        // サーバーの他者がその行を先に編集しておく＝競合の根拠。モデルはエンジンが消した行を端末の見た内容から外さない）
        yield return new(
            "holder-referenced-by-other-table-current-child-owed-row-key-created-on-device",
            StaleCopyWithChildSetup(),
            [
                .. FailedReadBackOps(),
                new UpdateEventOp(SV, Direct, CurrentChildAt, 811, "server-edit"),
                new AddEventOp(D1, Direct, CurrentChildAt, 999, "device"),
                Sync(D1),
                new ExpectLocalOp(D1, ModelTables.Events, CurrentChildKey, "item=999 memo=device"),
                ExpectOwedRows(0),
            ]
        );

        // そのキーに未送信の記録が残っている（落ちたプロセスが残した削除の記録）：ダウンロードだけの実行は約束を残し、行を
        // 戻さない。静止化のアップロードが行の無い記録を捨てた後の番で戻る
        yield return new(
            "holder-referenced-by-other-table-current-child-owed-row-waits-for-unsent-entry",
            StaleCopyWithChildSetup(),
            [
                .. FailedReadBackOps(),
                new RecordJournalOp(
                    D1,
                    ModelTables.Events,
                    CurrentChildJournalKey,
                    SyncJournalOperation.Delete
                ),
                PlainOnly(D1, ModelSyncKind.Download),
                new ExpectLocalOp(D1, ModelTables.Events, CurrentChildKey, null),
                ExpectOwedRows(1),
            ]
        );

        // 洗い替えは範囲の表の約束を消し、行はほかの行と同じく洗い替えで入る
        yield return new(
            "holder-referenced-by-other-table-current-child-owed-row-cleared-by-refresh",
            StaleCopyWithChildSetup(),
            [
                .. FailedReadBackOps(),
                new ModelRefreshOp(D1, SyncMode.Versioned, IncludeBlobs: true),
                new ExpectLocalOp(D1, ModelTables.Events, CurrentChildKey, "item=811 memo=child"),
                ExpectOwedRows(0),
            ]
        );

        // 約束の表が無い古い端末の DB：新しいジャーナルのインスタンスが最初の使用で作り、同期が決着する
        yield return new(
            "holder-referenced-by-other-table-current-child-legacy-local-schema",
            StaleCopyWithChildSetup(),
            [
                .. StaleCopyOps(),
                new LocalSqlOp(D1, "DROP TABLE quicker_sync_row_pending"),
                new OpenNewJournalOp(D1),
                Sync(D1),
                .. ExpectStaleCopyWithChildSettled(),
            ]
        );
    }

    /// <summary>
    /// 新しい形で、消した子のイベントの読み直しが断られて実行が止まる手（品目 811 のサーバーの写しは入り、子のイベントは
    /// 端末に無く、約束が 1 件残る）
    /// </summary>
    private static IReadOnlyList<ModelOp> FailedReadBackOps() =>
        [
            .. StaleCopyOps(),
            RefuseLocal(
                "sr_refuse_child_insert",
                "BEFORE INSERT ON model_events WHEN NEW.memo = 'child'"
            ),
            new ExpectThrowOp(D1),
            Sync(D1),
            new ExpectLocalOp(
                D1,
                ModelTables.Items,
                "811",
                "folder=813 label=null code=c-811 title=t811"
            ),
            new ExpectLocalOp(D1, ModelTables.Events, CurrentChildKey, null),
            ExpectOwedRows(1),
            AllowLocal("sr_refuse_child_insert"),
        ];

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
