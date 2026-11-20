using System;
using System.Collections.Generic;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// 新しいエンジンの端末の決定的なシナリオの控え（<c>sync-engine-scenario-ledger.md</c>）で、結果が <c>differs</c> の行の理由の宣言
/// （シナリオの名前 → 理由の 1 行）。
/// </summary>
/// <remarks>
/// 控えの <c>differs</c> の行の集合と 2 方向に照合する（宣言の無い <c>differs</c> の行も、<c>differs</c> でない行の宣言も赤。
/// <see cref="SyncSupportAdoSqliteEngineRuntimeTests.ScenarioLedgerDrift_MatchesRecord"/>）。理由は、新しいエンジンが今の
/// エンジンと違う答えを出すことを決めた根拠（docs・CLAUDE.md の見出しか、作り直しの計画の決定）を書く。
/// </remarks>
internal static class SyncModelEngineScenarioDifferences
{
    /// <summary>シナリオの名前 → 理由</summary>
    public static IReadOnlyDictionary<string, string> All { get; } =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            // 取得が途中で失敗する割り込み（ServerInterruptFault.NextFetchFails）
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

            // 実行が変えた行の一覧の、今のエンジンの解決役に固有の理由
            ["holder-referenced-by-other-table-stale-copy-settles"] =
                "ExpectLocalRowChangeOp が今のエンジンの解決役に固有の理由 RemovedForUniqueValue（持ち主を引き留める行の削除）を期待する。新しいエンジンは集合の外の行を消す解決役を持たず、削除伝搬を書き込みの前に流して同じ形を収束させる",
            ["n8-unique-value-moved-then-updated"] =
                "ExpectLocalRowChangeOp が今のエンジンの解決役に固有の理由 ReloadedForUniqueValue（持ち主のキーでの読み直し）を期待する。新しいエンジンは集合の外の行をキーで読みに行かず、取得した集合の中で後回しと回転で書く",

            ["holder-referenced-by-other-table-serverwins-settles-in-one-sync"] =
                "ExpectLocalRowChangeOp が今のエンジンの解決役に固有の理由 RemovedForUniqueValue（持ち主を引き留める品目 1782 の削除）を期待する。新しいエンジンは集合の外の行を消す解決役を持たず、ServerWins が捨てた 1781・1782 を DiscardedByServerWins で報告する（同じ行の ExpectLocalOp 3 つは通る）",
            ["holder-referenced-by-other-table-lww-serverwins-settles-in-one-sync"] =
                "ExpectLocalRowChangeOp が今のエンジンの解決役に固有の理由 RemovedForUniqueValue（持ち主を引き留める品目 1782 の削除）を期待する。新しいエンジンは集合の外の行を消す解決役を持たず、後勝ち＋ServerWins が捨てた 1781・1782 を DiscardedByServerWins で報告する（同じ行の ExpectLocalOp 3 つは通る）",
            ["n786-thrown-serverwins-removal-then-record-dropped"] =
                "ExpectLocalRowChangeOp が今のエンジンの段に固有の理由 RemovedBeforeDownload（ダウンロードの前に、サーバーに無い 784 を消す段）を期待する。新しいエンジンはその段を持たず、784 は同じ適用の削除伝搬で消え、DiscardedByServerWins だけを報告する（784 が端末に無い ExpectLocalOp は通る）",
            ["n2-serverwins-unique-delete-then-insert"] =
                "ExpectLocalRowChangeOp が今のエンジンの段に固有の理由 RemovedBeforeDownload（ダウンロードの前に、サーバーに無い 142 を消す段）を期待する。新しいエンジンはその段を持たず、142 は同じ適用の削除伝搬で消え、DiscardedByServerWins だけを報告する（141 が戻り 142 が無い ExpectLocalOp と、2 つの DiscardedByServerWins は通る）",

            // 未送信の行での打ち切り（今のエンジンは、サーバーが知っている未送信の行でその表のダウンロードを打ち切り、保留に数える。
            // 新しいエンジンはその行のローカルの内容を保って先へ進み、保留に数えない）
            ["ack-truncated-echo-then-edit-is-not-a-false-modified-conflict"] =
                UnsentRowIsNotTruncation,
            ["conc-local-edit-during-download-is-not-overwritten-by-batch"] =
                UnsentRowIsNotTruncation,
            ["conflict-delete-is-not-resurrected-by-same-run-download"] = UnsentRowIsNotTruncation,
            ["conflict-truncation-continues-following-tables-and-reads-parents"] =
                UnsentRowIsNotTruncation,
            ["fk-versionless-download-reports-held-back-row-after-scan"] = UnsentRowIsNotTruncation,

            // 洗い替えの件数の数え方（今のエンジンは範囲の表を全部消して全部入れ、消した行と入れた行を数える。新しいエンジンは
            // 範囲の表をサーバーの内容に置き換え、Deleted はサーバーに無いので消した行、Inserted は書いた行〔入れた行と
            // 上書きした行〕を数える）
            ["refresh-rebuilds-local-from-server"] = RefreshCountsReplacedRows,
            ["refresh-honors-foreign-key-order"] = RefreshCountsReplacedRows,

            // 一意な値の入れ替えが収束しないことを前提に置いた後の手
            ["swap-without-refresh-does-not-settle"] =
                "後の手（端末が品目の code を書き換える）が、同期の後も code の入れ替えが端末へ降りていない状態を前提にする。新しいエンジンは入れ替えを回転で適用し（除外 blob 列は temp の表で保つ）、その書き込みが端末の一意制約で失敗する",
            ["rotate-three-codes-without-refresh-does-not-settle"] =
                "後の手（端末が品目の code を書き換える）が、同期の後も 3 行の code の回転が端末へ降りていない状態を前提にする。新しいエンジンは回転をまとめて入れ直して適用し（除外 blob 列は temp の表で保つ）、その書き込みが端末の一意制約で失敗する",
            ["swap-folder-names-without-refresh-does-not-settle"] =
                "後の手（端末がフォルダー 832 の name を w832 へ更新する）が、同期の後も入れ替えが端末へ降りていない状態を前提にする。新しいエンジンは入れ替えを回転で適用するので、端末の 831 が w832 を持ち、その書き込みが端末の一意制約で失敗する",
        };

    /// <summary>未送信の行での打ち切りを期待する 5 行の理由</summary>
    private const string UnsentRowIsNotTruncation =
        "Expect.Result の truncation の行（サーバーが知っている未送信の行で、その表のダウンロードを打ち切った保留）を期待する。新しいエンジンは未送信の行のローカルの内容を保ち、基準版で競合を判定するので、その行で打ち切らず保留に数えない（保留はサーバーが知らない未送信の行・親が無い行・一意な値で書けない行だけ）。同じ行のほかの期待（競合・端末とサーバーの行）は通る";

    /// <summary>洗い替えの件数の数え方の 2 行の理由</summary>
    private const string RefreshCountsReplacedRows =
        "Expect.Result の deleted の件数（洗い替えの前に端末にあった行の全部）を期待する。新しいエンジンの洗い替えは範囲の表を全部消さずにサーバーの内容へ置き換えるので、deleted はサーバーに無いので消した行だけを数える（inserted は書いた行で同じ数、端末の行と版の期待は通る）";

    /// <summary>取得が途中で失敗する割り込みの 4 行の理由</summary>
    private const string FetchFailsAfterBatchCommit =
        "割り込まれた同期が、取得の後の点のバッチをコミットしてから同じ表の次の取得で失敗する形を前提に、後の手（降りたフォルダー p の端末での編集）を置く。新しいエンジンは全部を取得してから適用するので、取得の失敗で何も降りず、p の編集が端末に無い行の更新になる";
}
