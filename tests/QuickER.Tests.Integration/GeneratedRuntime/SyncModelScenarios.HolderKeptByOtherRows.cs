using System;
using System.Collections.Generic;
using System.Linq;
using QuickER.Tests.GeneratedSyncModelFixture;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// 一意な値の持ち主（サーバーに無いローカルの行）を、別の表の行が引き留める形。引き留める行のサーバーの行は、持ち主の値を待つ
/// 行の下にある。同期のエンジンは 1 つの適用の中で、全部の表の削除伝搬を行を書く前に流し、外部キーの検査を切ったまま行を書いて
/// から外部キーを確かめる（違反があれば巻き戻し、書いた子を保留にするか消した親を残してやり直す）。引き留める行がどれも送る
/// ものを持たない（未送信の変更が無いか、その実行が変更を捨てる）とき、削除伝搬が持ち主を消し、待っていた行と引き留める行の
/// サーバーの行を同じ適用で書く。引き留める行に未送信の変更が残るときは、その行は端末の内容のまま残り、持ち主を消すと外部キーの
/// 違反になるので持ち主も残し（持ち主自身に未送信の変更があれば削除伝搬が初めから消さない）、待っていた行は保留になる。
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
    /// <param name="referrerReason">持ち主を引き留めていた品目 1782 が、実行が変えた行の一覧に載る理由</param>
    private static IEnumerable<ModelOp> ExpectHolderKeptByItemSettled(
        SyncLocalRowChangeReason referrerReason
    ) =>
        [
            new ExpectLocalRowChangeOp(D1, ModelTables.Items, "1782", referrerReason),
            new ExpectLocalOp(D1, ModelTables.Folders, "1781", null),
            new ExpectLocalOp(D1, ModelTables.Folders, "3781", "parent=null name=h1781"),
            new ExpectLocalOp(D1, ModelTables.Items, "1782", ServerHolderReferrer),
        ];

    private static IEnumerable<SyncModelScenario> HolderKeptByOtherRowsScenarios()
    {
        // 広い世界の種 120022 の形を縮めたもの：静止化の ServerWins の周が品目 1782 と持ち主 1781 の変更を捨て、同じ適用で
        // 削除伝搬が持ち主 1781 を消し、3781・3782 と品目 1782 のサーバーの行を書く
        yield return new("holder-referenced-by-other-table-settles", [], HolderKeptByItemOps());

        // ServerWins の同期 1 回で、その同期の中で決着する（後勝ち＋ServerWins も同じ）。品目 1782 の未送信の挿入は
        // ServerWins が捨てた変更として載る
        yield return new(
            "holder-referenced-by-other-table-serverwins-settles-in-one-sync",
            [],
            [
                .. HolderKeptByItemOps(),
                Sync(D1, policy: SyncConflictPolicy.ServerWins),
                .. ExpectHolderKeptByItemSettled(SyncLocalRowChangeReason.DiscardedByServerWins),
            ]
        );
        yield return new(
            "holder-referenced-by-other-table-lww-serverwins-settles-in-one-sync",
            [],
            [
                .. HolderKeptByItemOps(),
                Sync(D1, SyncMode.LastWriteWins, SyncConflictPolicy.ServerWins),
                .. ExpectHolderKeptByItemSettled(SyncLocalRowChangeReason.DiscardedByServerWins),
            ]
        );

        // Collect は、引き留める行に送るものがある（品目 1782 の未送信の挿入）ので消さず、競合を報告し続ける（docs の「競合」の節の
        // 解決の道は、値を変えるか ServerWins で回す）
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

        // 削除を伝搬しない実行は、持ち主を消さないのと同じく、引き留める行も消さない（docs の「競合」の節：PropagateDeletes を
        // 切っていると、サーバーに無い行はそのまま残る）
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
        // ServerWins の周が品目とラベルの変更を捨て、削除伝搬がラベルと持ち主を消し、品目をサーバーの行で置き換える）
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
        // （Collect）1 回で、削除伝搬が 810 を消し、同じ適用で 813 と品目 811 のサーバーの行を書く（古い写しは置き換わる）
        yield return new(
            "holder-referenced-by-other-table-stale-copy-settles",
            [Folder(SV, 810, name: "h810"), Item(SV, 811, 810)],
            [
                .. StaleCopyOps(),
                Sync(D1),
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

    /// <summary>SK-1 の形の手（持ち主 1781 を引き留める品目 1782 の下へ、サーバーの子のイベントが最新の写しとして降りる）</summary>
    /// <remarks>
    /// 端末が作った品目 1782 と同じキーのサーバーの品目 1782 の子のイベントが、キーでつながって端末の品目 1782 の下へ最新の
    /// 写しとして降り、同じ表にそれより後ろの版のイベント（品目 795 の下）も降りて、イベントの表の再開点はその後ろへ進む。
    /// 決着する実行は品目 1782 を消さずにサーバーの行で置き換えるので、降りたイベントは端末に残る
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
        ];

    /// <summary>
    /// 引き留める行の下に最新の写しの子の行がある形（区切り SR）：エンジンは引き留める行を消さずにサーバーの行で置き換え、
    /// 再開点は表ごとに状態の表に持つ（ローカルの行から導かない）ので、子の行は端末に残り、降ろし直す必要が無い
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
                    .. ExpectHolderKeptByItemSettled(
                        SyncLocalRowChangeReason.DiscardedByServerWins
                    ),
                    new ExpectLocalOp(
                        D1,
                        ModelTables.Events,
                        CurrentChildKey,
                        "item=1782 memo=server-child"
                    ),
                ]
            );
        }

        // Collect・LocalWins は、引き留める品目 1782 が送るもの（未送信の挿入）を持つので消さない
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
                [.. StaleCopyOps(), Sync(D1, mode, policy), .. ExpectStaleCopyWithChildSettled()]
            );
        }

        // 降りた子のイベントの行の状態は、降ろしたときのサーバーの版を基準の版に持つ：端末が編集して上げると、その版で守る
        // 更新が競合なしにサーバーへ届く
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
