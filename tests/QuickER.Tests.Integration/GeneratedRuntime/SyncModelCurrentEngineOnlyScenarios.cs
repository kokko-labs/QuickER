using System;
using System.Collections.Generic;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// 今のエンジン（記録のデコレータと <c>SyncEngine</c>）の仕組みそのものを確かめる決定的なシナリオの一覧（シナリオの名前 → 理由）。
/// <see cref="SyncModelScenarios.All"/> が、載っている行に印 <see cref="SyncModelScenario.CurrentEngineOnly"/> を付ける。
/// </summary>
/// <remarks>
/// 載せるのは、行が守る仕組みが新しいエンジン（<c>TrackedSyncEngine</c>）に無い行だけで、理由には、無い根拠
/// （<c>src/QuickER.Runtime.Sync/CLAUDE.md</c> の節か、今のエンジンにだけある型・表の名前）を書く。期待を書き直せば新しい
/// エンジンでも流せる行は載せない。シナリオに無い名前・新しいエンジンの端末で緑になる行・具体的でない理由は赤になる
/// （<see cref="SyncModelScenario.CurrentEngineOnly"/> の remarks）。
/// </remarks>
internal static class SyncModelCurrentEngineOnlyScenarios
{
    /// <summary>シナリオの名前 → 理由</summary>
    public static IReadOnlyDictionary<string, string> All { get; } =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            // 以前の版が書いた形のキーのテキストの書き直し
            ["25-legacy-journal-key-text"] = LegacyKeyText,
            ["25-legacy-ack-key-text"] = LegacyKeyText,

            // 外部の ISyncTable 実装
            ["26-external-table-overwrites-pending-edit"] = ExternalTable,
            ["replay-recreated-pending-delete-is-sent-as-update-external-table"] = ExternalTable,
            ["replay-unlanded-delete-entry-is-discarded-external-table"] = ExternalTable,

            // ローカルの Save フックが適用の行を飛ばしたら止める
            ["apply-skip-stops-table"] = ApplySkip,
            ["hook-download-skip-stops-table-and-retries"] = ApplySkip,
            ["hook-parent-read-back-skip-stops-child-table"] = ApplySkip,
            ["hook-serverwins-read-back-skip-keeps-entry"] = ApplySkip,
            ["hook-versionless-download-skip-stops-table"] = ApplySkip,

            // 記録と業務の書き込みが別のトランザクションであることから生じる記録
            ["e2-leftover-unset-key-entry"] =
                "キーの決まらない記録（SyncJournal.UnsetKeyText）が落ちて残る形を確かめる行。新しいエンジンはトリガーが業務の書き込みと同じトランザクションで、キーの決まった行だけを記録するので、キーの決まらない記録が生じない（ローカルの変更の記録（SyncLocalTracking））。",
            ["replay-unlanded-delete-entry-is-discarded"] =
                "ジャーナルへ直接記録して、業務の書き込みが着地しなかった削除の記録を作る行（RecordJournalOp）。新しいエンジンはトリガーが業務の書き込みと同じトランザクションで記録するので、着地しない記録が生じない（ローカルの変更の記録（SyncLocalTracking））。",

            ["ul-stale-journal-entry-is-discarded"] =
                "ジャーナルへ直接記録して、端末に行の無い古い記録を作る行（RecordJournalOp）。新しいエンジンはトリガーが業務の書き込みと同じトランザクションで記録するので、書き込みの無い記録が生じない（ローカルの変更の記録（SyncLocalTracking））。端末で入れて消した行を送らずに片付けることは、行 replay-offline-insert-then-delete-is-discarded が持つ。",
            ["sw-leaves-row-edited-during-the-run"] =
                "ServerWins の実行の途中で編集された行に、実行が読んだ記録と新しい記録の 2 件が残ることを、記録の古さで確かめる行。新しいエンジンは行ごとの状態を 1 行に持ち、記録を重ねない（ローカルの変更の記録（SyncLocalTracking））。途中で編集された行が戻されず、次の同期がその編集を上げることは、行 sw-leaves-row-edited-during-the-run-then-uploads-it が持つ。",

            // 記録を消す公開の口
            ["rec-journal-cleanup-by-table-and-wholesale"] = JournalCleanup,
            ["conflict-discarded-lets-next-run-catch-up"] = JournalCleanup,

            // 記録を捨ててから行を消す順
            ["holder-referenced-by-other-table-entries-go-before-rows"] =
                "引き留める行を消すときに、記録を捨ててから行を消す順を、記録の削除を断るトリガーで確かめる行（RefuseJournalRemovalOp）。新しいエンジンは記録の表を持たず、行ごとの状態と行を同じトランザクションで書く（新しいエンジンの段の順と適用）。",

            // 親・一意な値の持ち主をキーで読み直す解決役
            ["thrown-resolver-row-delete-is-dropped-and-row-comes-back"] =
                "親の解決役が持ち込んだ行の削除が、例外で終わった実行の後に捨てられる今のエンジンの振る舞いを固定する行。新しいエンジンは解決役を持たず、適用を 1 つのトランザクションで行うので、行を書いた後・帳簿を書く前に例外になる窓が無い（新しいエンジンの段の順と適用）。",

            // 一意な値の入れ替え・回転を降ろせないこと
            ["swap-without-refresh-does-not-settle"] = SwapDoesNotSettle,
            ["swap-folder-names-without-refresh-does-not-settle"] = SwapDoesNotSettle,
            ["rotate-three-codes-without-refresh-does-not-settle"] = SwapDoesNotSettle,
            ["n8-true-swap-stops"] = SwapDoesNotSettle,

            // 洗い替えが範囲の表を全部消すこと
            ["refresh-failed-wipe-keeps-acks"] = RefreshWipes,
            ["refresh-failed-wipe-in-a-foreign-key-cycle-deletes-nothing"] = RefreshWipes,

            // バッチごとに確定するダウンロードの、取得の途中の失敗
            [
                "interrupt-server-AfterServerFetchBeforeApply-parent-after-child-cross-table-update-edit-sync-fetch-fails"
            ] = FetchFailsAfterBatchCommit,
            [
                "interrupt-server-AfterServerFetchBeforeApply-parent-after-child-cross-table-update-edit-sync-fetch-fails-serverwins"
            ] = FetchFailsAfterBatchCommit,
            [
                "interrupt-server-AfterServerFetchBeforeApply-parent-after-child-selfref-update-edit-sync-fetch-fails"
            ] = FetchFailsAfterBatchCommit,
            [
                "interrupt-server-AfterServerFetchBeforeApply-parent-after-child-selfref-update-edit-sync-fetch-fails-serverwins"
            ] = FetchFailsAfterBatchCommit,

            // 降ろし直す約束の表
            ["holder-referenced-by-other-table-current-child-legacy-local-schema"] =
                OwedRowTable
                + "この行は、約束の表が無い以前の版の端末の DB に、最初の使用で表が作られることを確かめる。",
            ["holder-referenced-by-other-table-current-child-owed-row-recorded-before-removal"] =
                OwedRowTable
                + "この行は、約束を行の削除より先に書く順を、約束の表への書き込みを失敗させて確かめる。",
            [
                "holder-referenced-by-other-table-current-child-owed-row-dropped-when-row-is-still-there"
            ] = OwedRowLifecycle,
            ["holder-referenced-by-other-table-current-child-owed-row-survives-failed-run"] =
                OwedRowLifecycle,
            ["holder-referenced-by-other-table-current-child-owed-row-gone-on-server"] =
                OwedRowLifecycle,
            [
                "holder-referenced-by-other-table-current-child-owed-row-waits-for-parent-with-unsent-delete"
            ] = OwedRowLifecycle,
            ["holder-referenced-by-other-table-current-child-owed-row-key-created-on-device"] =
                OwedRowLifecycle,
            ["holder-referenced-by-other-table-current-child-owed-row-waits-for-unsent-entry"] =
                OwedRowLifecycle,
            ["holder-referenced-by-other-table-current-child-owed-row-cleared-by-refresh"] =
                OwedRowLifecycle,
        };

    private const string LegacyKeyText =
        "以前の版が書いた形のキーのテキスト（記録・受理記録）を、起動時に今の書式へ書き直すことを確かめる行（SyncJournal.NormalizeKeyTextsAsync）。新しいエンジンはキーのテキストを端末の DB に保存しない（行ごとの状態の表は表が格納する生の値をキーにする）。";

    private const string ExternalTable =
        "外部の ISyncTable 実装（公開の面だけを持つ表）を今のエンジンが受け付けるときの振る舞いを確かめる行。新しいエンジンは ISyncTable を受け取らず、利用側が TrackedSyncTable と ILocalRowStore で組む（新しいエンジンの位置づけ）。";

    private const string ApplySkip =
        "サーバーの行の適用がローカルの Save フックを通り、フックが行を飛ばしたらその表を止めることを確かめる行（Save フックが行を飛ばしたら止める）。新しいエンジンの適用は Save フックを通らない（エンジンの宣言 AppliesThroughSaveHooks が偽）。";

    private const string JournalCleanup =
        "記録を表ごと・全部消す公開の口（SyncJournal の RemoveTableAsync・RemoveAllAsync）の行（消した後の同期がサーバーへ追いつくこと・受理記録に触れないこと）。新しいエンジンは記録の表も受理記録も持たず、行ごとの状態を quicker_sync_row の 1 表に持ち、未送信を捨てる公開の口を持たない（ローカルの変更の記録（SyncLocalTracking））。";

    private const string SwapDoesNotSettle =
        "サーバーでの一意な値の入れ替え・回転を、今のエンジンが降ろせずに打ち切り続けること（静止化しない・その後の端末の書き込みが古い値を前提にできること）を固定する行。新しいエンジンは入れ替えと回転を同じ適用の中で降ろす（新しいエンジンの段の順と適用。行 chunk-unique-swap-across-chunks と table:SyncEngineFixed の行 unique-value-rotation-keeps-local-blobs が持つ）。";

    private const string RefreshWipes =
        "洗い替えが範囲の表の行を全部消すことを前提に、端末の行の削除を断るトリガーで洗い替えを失敗させる行。新しいエンジンの洗い替えは、サーバーに無い行だけを消してほかはサーバーの内容へ置き換える（新しいエンジンの洗い替え。取得の失敗が端末を前のまま残すことは table:SyncEngineFixed の行 refresh-fetch-failure-leaves-device-as-before が持つ）。";

    private const string FetchFailsAfterBatchCommit =
        "割り込まれた同期が、取得の後の点のバッチを確定してから同じ表の次の取得で失敗する窓を前提に、後の手（降りたフォルダーの端末での編集）を置く行。新しいエンジンの 1 回で全部の道は、全部を取得してから 1 つのトランザクションで適用するので、取得の失敗で何も降りず、この窓が無い（新しいエンジンの段の順と適用）。塊ごとに確定する経路には、塊を確定した後の取得の失敗という同じ窓があり、table:SyncEngineFixed の行 chunk-failure-between-chunks-keeps-first-chunk-and-next-sync-resumes・chunk-failure-after-carry-keeps-resume-point-before-carried-row と小さい塊の端末が持つ（新しいエンジンの塊ごとに確定する経路）。";

    private const string OwedRowLifecycle =
        OwedRowTable
        + "この行は、解決役が子の行を消した後の読み直しを失敗させて残した約束が、後の実行で果たされるか・消えるかを確かめる。新しいエンジンは子の行を消さないので、読み直しの失敗の例外も約束も生じない。";

    private const string OwedRowTable =
        "降ろし直す約束の表（quicker_sync_row_pending）は今のエンジンの解決役が使う補助の表で、新しいエンジンは解決役も約束の表も持たず、取得した集合の中の後回しと取り直しで収束させる（降ろし直す約束（quicker_sync_row_pending））。";
}
