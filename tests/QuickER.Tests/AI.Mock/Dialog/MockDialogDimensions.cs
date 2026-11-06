using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.AI.Mock.Dialog;

/// <summary>モック生成のダイアログの表の次元表・網羅しない組の一覧・台帳の行</summary>
/// <remarks>行が宣言する（手を打つときの状態, 手の種類）を数える。網羅すべき組は状態 × 手の種類の全部。</remarks>
internal static class MockDialogDimensions
{
    /// <summary>網羅しない組の照合の範囲</summary>
    public const string Scope = "mock-dialog";

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
                    Enum.GetValues<MockDialogState>()
                        .Select(state => new Level(state.ToString()))
                        .ToList(),
                    "ダイアログの状態（フォルダ・会話・ターン・生成）"
                ),
                new Factor(
                    Op,
                    Enum.GetValues<MockDialogOp>().Select(op => new Level(op.ToString())).ToList(),
                    "手の種類"
                ),
            ]
        );

    /// <summary>網羅しない組の一覧（手で書いた静的な一覧）</summary>
    public static CoverageGapSet Gaps =>
        CoverageGap.Set(
            EngineIds.Ai,
            [
                // 画面のイベントはセッションのエンジンがターンの中でツールを実行したときにだけ届く。
                // セッションは会話の開始でだけ作られ、開始にはフォルダが要り（:220-226）、フォルダの変更は会話をリセットする（:721-724）
                Accepted(
                    MockDialogState.Idle,
                    MockDialogOp.Screen,
                    "モックフォルダが無いと会話を開始できず、画面のイベントを届けるセッションが無い（src/QuickER.AI.Mock/MockGenerationDialogViewModel.cs:220-226）"
                ),
                Accepted(
                    MockDialogState.Blocked,
                    MockDialogOp.Screen,
                    "使えないフォルダでは会話を開始できず（:226）、フォルダを変えると会話をリセットするので（:721-724）、画面のイベントを届けるセッションが無い"
                ),
                Accepted(
                    MockDialogState.Ready,
                    MockDialogOp.Screen,
                    "会話の前は画面のイベントを届けるセッションが無い（セッションは会話の開始でだけ作る。:935-987）"
                ),
                Accepted(
                    MockDialogState.GenRun,
                    MockDialogOp.Screen,
                    "生成中は送信できず（:235-240）、ターンの実行中は生成を始められない（:389）ので、生成中にツールを実行するターンは無い"
                ),
                Pending(
                    MockDialogState.Idle,
                    MockDialogOp.Converse,
                    "フォルダが無いときの開始・送信の可否は、フォルダの行（folder-required-to-start）が開始の可否だけを見る。送信の可否の行は元に無い"
                ),
                Pending(
                    MockDialogState.Idle,
                    MockDialogOp.Attach,
                    "フォルダを選ぶ前の添付の追加の行は元に無い"
                ),
                Pending(
                    MockDialogState.Blocked,
                    MockDialogOp.Converse,
                    "使えないフォルダのまま開始のコマンドを直接実行する経路（CanExecute の門だけで本体に防御が無い。:936-956）の行は元に無い。画面からは届かない"
                ),
                Pending(
                    MockDialogState.Blocked,
                    MockDialogOp.Attach,
                    "使えないフォルダでの添付の行は元に無い"
                ),
                Pending(
                    MockDialogState.Blocked,
                    MockDialogOp.Export,
                    "使えないフォルダでの出力の可否の行は元に無い"
                ),
                Pending(
                    MockDialogState.Blocked,
                    MockDialogOp.Generate,
                    "使えないフォルダでの生成の可否の行は元に無い"
                ),
                Pending(
                    MockDialogState.Blocked,
                    MockDialogOp.Persist,
                    "使えないフォルダでの設定の保存の行は元に無い"
                ),
                Pending(
                    MockDialogState.Blocked,
                    MockDialogOp.Shutdown,
                    "使えないフォルダでの終了の経路の行は元に無い"
                ),
                Pending(
                    MockDialogState.Ready,
                    MockDialogOp.Persist,
                    "フォルダを選んだ後・会話の前の設定の保存の行は元に無い"
                ),
                Pending(
                    MockDialogState.Conversation,
                    MockDialogOp.Export,
                    "会話中の出力の行は元に無い（元の出力の行は会話の前）"
                ),
                Pending(
                    MockDialogState.Turn,
                    MockDialogOp.Export,
                    "ターンの実行中の出力の行は元に無い"
                ),
                Pending(
                    MockDialogState.Turn,
                    MockDialogOp.Persist,
                    "ターンの実行中に × で閉じる（設定の保存と隠す）行は元に無い"
                ),
                Pending(
                    MockDialogState.Turn,
                    MockDialogOp.Reset,
                    "ターンの実行中はクリアできない（CanClear が偽。:1195-1196）。クリアのコマンドを直接実行する経路は本体に防御が無く（:1203-1230）、画面から届かないので行にしていない"
                ),
                Pending(
                    MockDialogState.GenRun,
                    MockDialogOp.SetFolder,
                    "生成中もモックフォルダを変えられる（CanEditMockFolder はターンだけを見る。:164）。生成は開始時の値を控えて進むが、仕様の記述が無いので行にしていない"
                ),
                Pending(MockDialogState.GenRun, MockDialogOp.Attach, "生成中の添付の行は元に無い"),
                Pending(MockDialogState.GenRun, MockDialogOp.Export, "生成中の出力の行は元に無い"),
                Pending(
                    MockDialogState.GenRun,
                    MockDialogOp.Persist,
                    "生成中に × で閉じる行は元に無い（実ウィンドウの ForceClose は MockGenerationDialogTests が見る）"
                ),
                Pending(
                    MockDialogState.GenRun,
                    MockDialogOp.Reset,
                    "生成中はクリアできない（CanClear が偽。:1195-1196）行は元に無い"
                ),
                Pending(
                    MockDialogState.GenDone,
                    MockDialogOp.SetFolder,
                    "生成の後のフォルダの変更（フォルダを開くの表示は戻さない。:1224-1227 はクリアだけ）の行は元に無い"
                ),
                Pending(
                    MockDialogState.GenDone,
                    MockDialogOp.Backend,
                    "生成の後の接続方式の切替の行は元に無い"
                ),
                Pending(
                    MockDialogState.GenDone,
                    MockDialogOp.Converse,
                    "生成の後の会話の行は元に無い"
                ),
                Pending(
                    MockDialogState.GenDone,
                    MockDialogOp.Screen,
                    "生成の後の画面の保存の行は元に無い"
                ),
                Pending(
                    MockDialogState.GenDone,
                    MockDialogOp.Attach,
                    "生成の後の添付の行は元に無い"
                ),
                Pending(
                    MockDialogState.GenDone,
                    MockDialogOp.Export,
                    "生成の後の出力の行は元に無い"
                ),
                Pending(
                    MockDialogState.GenDone,
                    MockDialogOp.Persist,
                    "生成の後の設定の保存の行は元に無い"
                ),
                Pending(
                    MockDialogState.GenDone,
                    MockDialogOp.Shutdown,
                    "生成の後の終了の経路の行は元に無い"
                ),
            ]
        );

    /// <summary>原理的に起こり得ない（状態, 手の種類）の項目</summary>
    private static CoverageGapEntry Accepted(
        MockDialogState state,
        MockDialogOp op,
        string reason
    ) =>
        CoverageGap.AcceptedTuple(
            Scope,
            "P3-11b",
            reason,
            (State, state.ToString()),
            (Op, op.ToString())
        );

    /// <summary>まだ行の無い（状態, 手の種類）の項目（直す区切りは P4）</summary>
    private static CoverageGapEntry Pending(
        MockDialogState state,
        MockDialogOp op,
        string reason
    ) =>
        CoverageGap.Tuple(
            Scope,
            "P3-11b",
            "P4",
            reason,
            (State, state.ToString()),
            (Op, op.ToString())
        );

    /// <summary>行の触る組を数える</summary>
    public static void Observe(CoverageCounter counter, MockDialogState state, MockDialogOp op) =>
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
            "MockDialog",
            "モック生成のダイアログの操作の列の表（状態 × 手の種類・どの手の後にも不変条件 11 個を見張る）",
            Table,
            MockDialogRows.Table.Count,
            [
                "tests/QuickER.Tests/AI.Mock/Dialog/MockDialogDimensions.cs",
                "tests/QuickER.Tests/AI.Mock/Dialog/MockDialogFakes.cs",
                "tests/QuickER.Tests/AI.Mock/Dialog/MockDialogHarness.cs",
                "tests/QuickER.Tests/AI.Mock/Dialog/MockDialogRows.cs",
                "tests/QuickER.Tests/AI.Mock/Dialog/MockDialogRows.Backend.cs",
                "tests/QuickER.Tests/AI.Mock/Dialog/MockDialogRows.Conversation.cs",
                "tests/QuickER.Tests/AI.Mock/Dialog/MockDialogRows.Folder.cs",
                "tests/QuickER.Tests/AI.Mock/Dialog/MockDialogRows.Generate.cs",
                "tests/QuickER.Tests/AI.Mock/Dialog/MockDialogRows.Screen.cs",
                "tests/QuickER.Tests/AI.Mock/Dialog/MockDialogRows.Shutdown.cs",
                "tests/QuickER.Tests/AI.Mock/Dialog/MockDialogTests.cs",
            ],
            ["src/QuickER.AI.Mock/MockGenerationDialogViewModel.cs"]
        );
}
