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
                .. Pending(
                    ConnectionDialogState.Fresh,
                    "空のストアでの削除の行は元に無い（未選択の削除は delete-without-selection-asks-to-select が保存済みのプロファイルがある状態で見る）",
                    ConnectionDialogOp.Delete
                ),
                .. Pending(
                    ConnectionDialogState.Fresh,
                    "SQL Server を選んだままの参照の行は無い（参照の欄は SQLite のときだけ出る :249・新規作成は SQLite のときだけ実行できる :255・:886）",
                    ConnectionDialogOp.Browse
                ),
                .. Pending(
                    ConnectionDialogState.Profiles,
                    "保存済みのプロファイルがあって未選択のときの入力・接続テスト・参照・OK・取り消しの行は元に無い（元の行は選択か保存を伴う）",
                    ConnectionDialogOp.Edit,
                    ConnectionDialogOp.Test,
                    ConnectionDialogOp.Browse,
                    ConnectionDialogOp.Ok,
                    ConnectionDialogOp.Cancel
                ),
                .. Pending(
                    ConnectionDialogState.Selected,
                    "プロファイルを選んだ後の選び直し・入力だけの手・接続テスト・参照・取り消しの行は元に無い",
                    ConnectionDialogOp.Select,
                    ConnectionDialogOp.Edit,
                    ConnectionDialogOp.Test,
                    ConnectionDialogOp.Browse,
                    ConnectionDialogOp.Cancel
                ),
                .. Pending(
                    ConnectionDialogState.Sync,
                    "同期モード（SQL Server 固定）でのプロファイルの保存・名前の変更・削除・接続テスト・取り消しの行は元に無い（元の同期モードの行は開く・方言固定・SQLite の新規作成だけ）",
                    ConnectionDialogOp.Save,
                    ConnectionDialogOp.Rename,
                    ConnectionDialogOp.Delete,
                    ConnectionDialogOp.Test,
                    ConnectionDialogOp.Cancel
                ),
                .. Pending(
                    ConnectionDialogState.Sqlite,
                    "SQLite を選んだ状態での開く（前回接続の SQLite の復元は open-import-mode-restores-dialect-of-last-connection が保存済みの状態として見る）・選択・名前の変更・削除・接続テスト・取り消しの行は元に無い",
                    ConnectionDialogOp.Open,
                    ConnectionDialogOp.Select,
                    ConnectionDialogOp.Rename,
                    ConnectionDialogOp.Delete,
                    ConnectionDialogOp.Test,
                    ConnectionDialogOp.Cancel
                ),
                .. Pending(
                    ConnectionDialogState.StoreLocked,
                    "connections.json を掴まれた間の開く・選択・入力・接続テスト・参照・取り消し（ストアへ書かない手）の行は元に無い",
                    ConnectionDialogOp.Open,
                    ConnectionDialogOp.Select,
                    ConnectionDialogOp.Edit,
                    ConnectionDialogOp.Test,
                    ConnectionDialogOp.Browse,
                    ConnectionDialogOp.Cancel
                ),
                .. Pending(
                    ConnectionDialogState.WriteFails,
                    "暗号ファイルを書けない・消せない間の開く・選択・入力・接続テスト・参照・取り消し（暗号ファイルを書かない手）の行は元に無い",
                    ConnectionDialogOp.Open,
                    ConnectionDialogOp.Select,
                    ConnectionDialogOp.Edit,
                    ConnectionDialogOp.Test,
                    ConnectionDialogOp.Browse,
                    ConnectionDialogOp.Cancel
                ),
                .. Pending(
                    ConnectionDialogState.Unreadable,
                    "読めないパスワードのプロファイルを選んだ後の入力・削除・接続テスト・参照・OK・取り消しの行は元に無い（削除で読めなかった印が消える経路 :541・:867 も行が無い）",
                    ConnectionDialogOp.Edit,
                    ConnectionDialogOp.Delete,
                    ConnectionDialogOp.Test,
                    ConnectionDialogOp.Browse,
                    ConnectionDialogOp.Ok,
                    ConnectionDialogOp.Cancel
                ),
                .. Pending(
                    ConnectionDialogState.Testing,
                    "接続テストの実行中の選択・入力・保存・名前の変更・削除・二重の接続テスト・参照の行は元に無い（実行中に止めるのは OK だけ :326・:910）",
                    ConnectionDialogOp.Select,
                    ConnectionDialogOp.Edit,
                    ConnectionDialogOp.Save,
                    ConnectionDialogOp.Rename,
                    ConnectionDialogOp.Delete,
                    ConnectionDialogOp.Test,
                    ConnectionDialogOp.Browse
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

    /// <summary>まだ行の無い（状態, 手の種類）の項目（直す区切りは P4）。手の種類ごとに 1 項目</summary>
    private static IEnumerable<CoverageGapEntry> Pending(
        ConnectionDialogState state,
        string reason,
        params ConnectionDialogOp[] ops
    ) =>
        ops.Select(op =>
            CoverageGap.Tuple(
                Scope,
                "P3-11b",
                "P4",
                reason,
                (State, state.ToString()),
                (Op, op.ToString())
            )
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
            "接続ダイアログの操作の列の表（状態 × 手の種類・どの手の後にも不変条件 12 個を見張る）",
            Table,
            ConnectionDialogRows.Table.Count,
            [
                "tests/QuickER.Tests/Db.UI/ConnectionDialog/ConnectionDialogDimensions.cs",
                "tests/QuickER.Tests/Db.UI/ConnectionDialog/ConnectionDialogFakes.cs",
                "tests/QuickER.Tests/Db.UI/ConnectionDialog/ConnectionDialogHarness.cs",
                "tests/QuickER.Tests/Db.UI/ConnectionDialog/ConnectionDialogRows.cs",
                "tests/QuickER.Tests/Db.UI/ConnectionDialog/ConnectionDialogRows.Failure.cs",
                "tests/QuickER.Tests/Db.UI/ConnectionDialog/ConnectionDialogRows.Ok.cs",
                "tests/QuickER.Tests/Db.UI/ConnectionDialog/ConnectionDialogRows.Open.cs",
                "tests/QuickER.Tests/Db.UI/ConnectionDialog/ConnectionDialogRows.Profile.cs",
                "tests/QuickER.Tests/Db.UI/ConnectionDialog/ConnectionDialogRows.Sqlite.cs",
                "tests/QuickER.Tests/Db.UI/ConnectionDialog/ConnectionDialogTests.cs",
            ],
            ["src/QuickER.Db.UI/DbConnectionDialogViewModel.cs"]
        );
}
