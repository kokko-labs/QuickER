using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.Db.UI.ImportSync;

/// <summary>取込と同期の表の次元表・網羅しない組の一覧・台帳の行</summary>
/// <remarks>行が宣言する（手を打つときの状態, 手の種類）を数える。網羅すべき組は状態 × 手の種類の全部。</remarks>
internal static class ImportSyncDimensions
{
    /// <summary>網羅しない組の照合の範囲</summary>
    public const string Scope = "db-import-sync";

    /// <summary>手を打つときの状態</summary>
    public const string State = "State";

    /// <summary>手の種類</summary>
    public const string Op = "Op";

    /// <summary>次元表</summary>
    public static DimensionTable Table =>
        new(
            EngineIds.Ai,
            [
                new Factor(
                    State,
                    Enum.GetValues<ImportSyncState>()
                        .Select(state => new Level(state.ToString()))
                        .ToList(),
                    "取込の図の状態・同期のコマンド・同期のダイアログの状態"
                ),
                new Factor(
                    Op,
                    Enum.GetValues<ImportSyncOp>().Select(op => new Level(op.ToString())).ToList(),
                    "手の種類"
                ),
            ]
        );

    /// <summary>網羅しない組の一覧（手で書いた静的な一覧）</summary>
    public static CoverageGapSet Gaps =>
        CoverageGap.Set(
            EngineIds.Ai,
            [
                .. Accepted(
                    [
                        ImportSyncState.ImportEmpty,
                        ImportSyncState.ImportClean,
                        ImportSyncState.ImportDirty,
                    ],
                    [ImportSyncOp.Select, ImportSyncOp.Refresh, ImportSyncOp.Close],
                    "DB 取込のコマンドは 1 回の取込（接続 → 取込 → 確認 → 置き換え → 通知）だけで、差分の選択・再計算・閉じるの手を持たない（src/QuickER.Db.UI/DbImportCommandService.cs:43-134）"
                ),
                .. Accepted(
                    [ImportSyncState.ImportEmpty],
                    [ImportSyncOp.Confirm],
                    "失うものが無い図（エンティティもクエリも無く未保存でない）は、壊れるクエリも上書きする説明も無いので確認を出さない（src/QuickER.Provider/DiagramReplacementCheck.cs:30-31・:56-64）"
                ),
                .. Accepted(
                    [ImportSyncState.SyncCommand],
                    [
                        ImportSyncOp.Confirm,
                        ImportSyncOp.Fail,
                        ImportSyncOp.Select,
                        ImportSyncOp.Refresh,
                        ImportSyncOp.Close,
                    ],
                    "DB 同期のコマンドは接続ダイアログを開き、確定したら図を 1 回読んで同期のダイアログを開くだけで、確認・失敗の経路・差分の手を持たない（src/QuickER.Db.UI/DbSyncCommandService.cs:45-73）"
                ),
                .. Accepted(
                    [ImportSyncState.SyncFresh, ImportSyncState.SyncDiff, ImportSyncState.SyncBusy],
                    [ImportSyncOp.Pick],
                    "同期のダイアログは接続を確定した後に開くので、接続ダイアログの手を持たない（接続は DbSyncCommandService の側。src/QuickER.Db.UI/SchemaSyncDialogViewModel.cs:120-133 は確定した接続設定を受け取る）"
                ),
                .. Accepted(
                    [ImportSyncState.SyncBusy],
                    [ImportSyncOp.Confirm],
                    "再計算・実行の最中の実行は確認の前に抜ける（src/QuickER.Db.UI/SchemaSyncDialogViewModel.cs:359-362）。抜けることは sync-dialog-refresh-in-progress-disables-operations-but-not-close が見る"
                ),
            ]
        );

    /// <summary>原理的に起こり得ない（状態, 手の種類）の項目（状態と手の種類の組ごとに 1 項目）</summary>
    private static IEnumerable<CoverageGapEntry> Accepted(
        ImportSyncState[] states,
        ImportSyncOp[] ops,
        string reason
    ) =>
        states.SelectMany(state =>
            ops.Select(op =>
                CoverageGap.AcceptedTuple(
                    Scope,
                    "P3-11b",
                    reason,
                    (State, state.ToString()),
                    (Op, op.ToString())
                )
            )
        );

    /// <summary>台帳の行</summary>
    public static EngineDescriptor Descriptor =>
        new(
            EngineIds.Ai,
            "DbImportSync",
            "DB 取込・DB 同期のコマンドと同期のダイアログの操作の列の表（状態 × 手の種類・どの手の後にも不変条件 14 個を見張る）",
            Table,
            ImportSyncRows.Table.Count,
            [
                "tests/QuickER.Tests/Db.UI/ImportSync/ImportSyncDimensions.cs",
                "tests/QuickER.Tests/Db.UI/ImportSync/ImportSyncFakes.cs",
                "tests/QuickER.Tests/Db.UI/ImportSync/ImportSyncHarness.cs",
                "tests/QuickER.Tests/Db.UI/ImportSync/ImportSyncRows.cs",
                "tests/QuickER.Tests/Db.UI/ImportSync/ImportSyncRows.Import.cs",
                "tests/QuickER.Tests/Db.UI/ImportSync/ImportSyncRows.SyncCommand.cs",
                "tests/QuickER.Tests/Db.UI/ImportSync/ImportSyncRows.SyncExecute.cs",
                "tests/QuickER.Tests/Db.UI/ImportSync/ImportSyncRows.SyncPreview.cs",
                "tests/QuickER.Tests/Db.UI/ImportSync/ImportSyncTests.cs",
            ],
            [
                "src/QuickER.Db.UI/DbImportCommandService.cs",
                "src/QuickER.Db.UI/DbSyncCommandService.cs",
                "src/QuickER.Db.UI/SchemaSyncDialogViewModel.cs",
            ]
        );

    /// <summary>行の触る組を数える</summary>
    public static void Observe(CoverageCounter counter, ImportSyncState state, ImportSyncOp op) =>
        counter.Observe(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [State] = state.ToString(),
                [Op] = op.ToString(),
            }
        );
}
