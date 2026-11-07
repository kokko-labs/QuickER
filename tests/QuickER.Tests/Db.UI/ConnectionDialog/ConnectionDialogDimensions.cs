using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.Db.UI.ConnectionDialog;

/// <summary>接続ダイアログの表の次元表・網羅しない組の一覧・台帳の行</summary>
/// <remarks>行が宣言する（手を打つときの状態, 手の種類）を数える。網羅すべき組は状態 × 手の種類の全部。</remarks>
internal static class ConnectionDialogDimensions
{
    /// <summary>網羅しない組の照合の範囲</summary>
    public const string Scope = "db-connection-dialog";

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
                    Enum.GetValues<ConnectionDialogState>()
                        .Select(state => new Level(state.ToString()))
                        .ToList(),
                    "ダイアログとストアの状態（モード・方言・選択・ストアの失敗・接続テスト）"
                ),
                new Factor(
                    Op,
                    Enum.GetValues<ConnectionDialogOp>()
                        .Select(op => new Level(op.ToString()))
                        .ToList(),
                    "手の種類"
                ),
            ]
        );

    /// <summary>網羅しない組の一覧（手で書いた静的な一覧）</summary>
    public static CoverageGapSet Gaps =>
        CoverageGap.Set(
            EngineIds.Ai,
            [
                Accepted(
                    ConnectionDialogState.Fresh,
                    ConnectionDialogOp.Select,
                    "空のストアには選べるプロファイルが無い（一覧はストアの中身だけ。src/QuickER.Db.UI/DbConnectionDialogViewModel.cs:299-307）"
                ),
                Accepted(
                    ConnectionDialogState.Fresh,
                    ConnectionDialogOp.Rename,
                    "名前の変更は選択中のプロファイルが要り（:744-747）、空のストアでは選べない"
                ),
                Accepted(
                    ConnectionDialogState.Selected,
                    ConnectionDialogOp.Open,
                    "開いた直後は保存済みのプロファイルを選ばない（:293-295）ので、選んだ状態で開くことは無い"
                ),
                Accepted(
                    ConnectionDialogState.Testing,
                    ConnectionDialogOp.Open,
                    "接続テストは開いた後のダイアログでだけ始まる（:604-637）"
                ),
            ]
        );

    /// <summary>原理的に起こり得ない（状態, 手の種類）の項目</summary>
    private static CoverageGapEntry Accepted(
        ConnectionDialogState state,
        ConnectionDialogOp op,
        string reason
    ) =>
        CoverageGap.AcceptedTuple(
            Scope,
            "P3-11b",
            reason,
            (State, state.ToString()),
            (Op, op.ToString())
        );

    /// <summary>行の触る組を数える</summary>
    public static void Observe(
        CoverageCounter counter,
        ConnectionDialogState state,
        ConnectionDialogOp op
    ) =>
        counter.Observe(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [State] = state.ToString(),
                [Op] = op.ToString(),
            }
        );

    /// <summary>台帳の行</summary>
    public static EngineDescriptor Descriptor =>
        new(
            EngineIds.Ai,
            "DbConnectionDialog",
            "接続ダイアログの操作の列の表（状態 × 手の種類・どの手の後にも不変条件 14 個を見張る）",
            Table,
            ConnectionDialogRows.Table.Count,
            [
                "tests/QuickER.Tests/Db.UI/ConnectionDialog/ConnectionDialogDimensions.cs",
                "tests/QuickER.Tests/Db.UI/ConnectionDialog/ConnectionDialogFakes.cs",
                "tests/QuickER.Tests/Db.UI/ConnectionDialog/ConnectionDialogHarness.cs",
                "tests/QuickER.Tests/Db.UI/ConnectionDialog/ConnectionDialogRows.cs",
                "tests/QuickER.Tests/Db.UI/ConnectionDialog/ConnectionDialogRows.Corrupt.cs",
                "tests/QuickER.Tests/Db.UI/ConnectionDialog/ConnectionDialogRows.Failure.cs",
                "tests/QuickER.Tests/Db.UI/ConnectionDialog/ConnectionDialogRows.Ok.cs",
                "tests/QuickER.Tests/Db.UI/ConnectionDialog/ConnectionDialogRows.Open.cs",
                "tests/QuickER.Tests/Db.UI/ConnectionDialog/ConnectionDialogRows.Profile.cs",
                "tests/QuickER.Tests/Db.UI/ConnectionDialog/ConnectionDialogRows.Sqlite.cs",
                "tests/QuickER.Tests/Db.UI/ConnectionDialog/ConnectionDialogRows.Stale.cs",
                "tests/QuickER.Tests/Db.UI/ConnectionDialog/ConnectionDialogRows.Sweep.cs",
                "tests/QuickER.Tests/Db.UI/ConnectionDialog/ConnectionDialogRows.SweepFailure.cs",
                "tests/QuickER.Tests/Db.UI/ConnectionDialog/ConnectionDialogTests.cs",
            ],
            ["src/QuickER.Db.UI/DbConnectionDialogViewModel.cs"]
        );
}
