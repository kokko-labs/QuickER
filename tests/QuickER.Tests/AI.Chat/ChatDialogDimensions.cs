using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.AI.Chat;

/// <summary>チャットのダイアログの表の次元表・網羅しない組の一覧・台帳の行</summary>
/// <remarks>行が宣言する（置く状態, 手の種類）を数える。網羅すべき組は状態 × 手の種類の全部。</remarks>
internal static class ChatDialogDimensions
{
    /// <summary>網羅しない組の照合の範囲</summary>
    public const string Scope = "chat-dialog";

    /// <summary>置く状態</summary>
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
                    Enum.GetValues<ChatDialogState>()
                        .Select(state => new Level(state.ToString()))
                        .ToList(),
                    "ダイアログの状態（会話・ターン）"
                ),
                new Factor(
                    Op,
                    Enum.GetValues<ChatDialogOp>().Select(op => new Level(op.ToString())).ToList(),
                    "手の種類"
                ),
            ]
        );

    /// <summary>網羅しない組の一覧（手で書く。状態 × 手の種類の全部を行が触る）</summary>
    public static CoverageGapSet Gaps => CoverageGap.Set(EngineIds.Ai, []);

    /// <summary>行の触る組を数える</summary>
    public static void Observe(CoverageCounter counter, ChatDialogState state, ChatDialogOp op) =>
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
            "ChatDialog",
            "チャットのダイアログの操作の列の表（状態 × 手の種類・不変条件 3 つ）",
            Table,
            ChatDialogRows.Table.Count,
            [
                "tests/QuickER.Tests/AI.Chat/ChatDialogDimensions.cs",
                "tests/QuickER.Tests/AI.Chat/ChatDialogHarness.cs",
                "tests/QuickER.Tests/AI.Chat/ChatDialogRows.cs",
                "tests/QuickER.Tests/AI.Chat/ChatDialogRows.Attachment.cs",
                "tests/QuickER.Tests/AI.Chat/ChatDialogRows.Backend.cs",
                "tests/QuickER.Tests/AI.Chat/ChatDialogRows.Connect.cs",
                "tests/QuickER.Tests/AI.Chat/ChatDialogRows.Persist.cs",
                "tests/QuickER.Tests/AI.Chat/ChatDialogRows.Shutdown.cs",
                "tests/QuickER.Tests/AI.Chat/ChatDialogRows.Turn.cs",
                "tests/QuickER.Tests/AI.Chat/ChatDialogTests.cs",
            ],
            ["src/QuickER.AI.Chat/AiChatDialogViewModel.cs"]
        );
}
