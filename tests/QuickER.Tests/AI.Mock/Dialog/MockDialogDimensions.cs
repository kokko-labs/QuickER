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
                CoverageGap.Tuple(
                    Scope,
                    "P3-11b",
                    "P5",
                    "製品の候補（P5・ユーザーの判断）：生成中も設計書の出力が押せ、モックフォルダへ README.md を書く（ExportDesignDocCommand の CanExecute は CanExportBundle＝画面の有無だけ・src/QuickER.AI.Mock/MockGenerationDialogViewModel.cs:262-267・:1131-1160）。生成中に会話を止める理由（CanSendMessage の remarks『生成はモックフォルダを出力先へ写すので、書き込むと写した内容と食い違い得る』・:231-234）と食い違う。単一 HTML（保存先にだけ書く）の形だけの行で組を埋めると候補が目録から消え、特性として固定すると生成中の書き込みを許す方針を黙って決めるので行にしない",
                    (State, MockDialogState.GenRun.ToString()),
                    (Op, MockDialogOp.Export.ToString())
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
                "tests/QuickER.Tests/AI.Mock/Dialog/MockDialogRows.StateGaps.cs",
                "tests/QuickER.Tests/AI.Mock/Dialog/MockDialogTests.cs",
            ],
            ["src/QuickER.AI.Mock/MockGenerationDialogViewModel.cs"]
        );
}
