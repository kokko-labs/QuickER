using System;
using System.Collections.Generic;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// 今のエンジンの端末の決定的なシナリオの控え（<c>sync-current-engine-scenario-ledger.md</c>）で、結果が <c>differs</c> の行の
/// 理由の宣言（シナリオの名前 → 理由の 1 行）。
/// </summary>
/// <remarks>
/// 控えの <c>differs</c> の行の集合と 2 方向に照合する（宣言の無い <c>differs</c> の行も、<c>differs</c> でない行の宣言も赤。
/// <see cref="SyncSupportAdoSqliteCurrentEngineRuntimeTests.ScenarioLedgerDrift_MatchesRecord"/>）。載せるのは、行の期待を新しい
/// エンジン（<c>TrackedSyncEngine</c>）の答えで書いた行で、理由には、今のエンジンがその期待と違う答えを出す仕組み
/// （解決役の読み直し・未送信の行での打ち切りなど）を書く。
/// </remarks>
internal static class SyncModelCurrentEngineScenarioDifferences
{
    /// <summary>シナリオの名前 → 理由</summary>
    public static IReadOnlyDictionary<string, string> All { get; } =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            // 未送信の行での打ち切り
            ["ack-truncated-echo-then-edit-is-not-a-false-modified-conflict"] =
                TruncatesAtUnsentRow,
            ["conc-local-edit-during-download-is-not-overwritten-by-batch"] = TruncatesAtUnsentRow,
            ["conflict-delete-is-not-resurrected-by-same-run-download"] = TruncatesAtUnsentRow,
            ["conflict-truncation-continues-following-tables-and-reads-parents"] =
                TruncatesAtUnsentRow,
            ["fk-versionless-download-reports-held-back-row-after-scan"] = TruncatesAtUnsentRow,

            // 洗い替えの件数の数え方
            ["refresh-rebuilds-local-from-server"] = RefreshWipesAndCounts,
            ["refresh-honors-foreign-key-order"] = RefreshWipesAndCounts,

            // 実行が変えた行の一覧の理由
            ["n786-thrown-serverwins-removal-then-record-dropped"] =
                "行は、ServerWins が捨てたサーバーに無い品目 784 が、実行が変えた行の一覧に DiscardedByServerWins で載ることを期待する。今のエンジンは、ダウンロードの前にサーバーに無い行を消す段がその行を消し、RemovedBeforeDownload で載せる",
        };

    /// <summary>未送信の行で打ち切らないことを期待する 5 行の理由</summary>
    private const string TruncatesAtUnsentRow =
        "行は、未送信の行がローカルの内容のまま残り、同期の結果に打ち切り（truncation）が載らないことを期待する。今のエンジンは、サーバーが知っている未送信の行でその表のダウンロードを打ち切り、保留として結果に載せる";

    /// <summary>洗い替えの消した件数の 2 行の理由</summary>
    private const string RefreshWipesAndCounts =
        "行は、洗い替えの deleted がサーバーに無いので消した行だけを数えることを期待する。今のエンジンの洗い替えは範囲の表を全部消してから入れ直し、消した行の全部を deleted に数える";
}
