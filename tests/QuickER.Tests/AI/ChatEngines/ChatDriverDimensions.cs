using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.AI.ChatEngines;

/// <summary>API キー接続のドライバの表の次元表・台帳の行</summary>
/// <remarks>1 ケース＝（ドライバ, 送る形）。網羅すべき組はドライバ × 形の全部（網羅しない組は無い）。</remarks>
internal static class ChatDriverDimensions
{
    /// <summary>網羅しない組の照合の範囲</summary>
    public const string Scope = "chat-drivers";

    /// <summary>ドライバ</summary>
    public const string Driver = "Driver";

    /// <summary>送る形（利用者の発話の添付の組み合わせ・ツールの往復）</summary>
    public const string Shape = "Shape";

    /// <summary>次元表</summary>
    public static DimensionTable Table =>
        new(
            EngineIds.Ai,
            [
                new Factor(
                    Driver,
                    ChatDriverAxis
                        .Axis.Adapters.Select(adapter => new Level(adapter.AxisValue))
                        .ToList(),
                    "API キー接続のドライバ"
                ),
                new Factor(
                    Shape,
                    ChatDriverRows.Table.Select(row => new Level(row.Id)).ToList(),
                    "送る形（利用者の発話の添付の組み合わせ・ツールの往復）"
                ),
            ]
        );

    /// <summary>行 × ドライバ（流すケース）</summary>
    public static IReadOnlyList<(string Id, string Driver)> CaseList() =>
        (
            from row in ChatDriverRows.Table
            from adapter in ChatDriverAxis.Axis.Adapters
            select (row.Id, adapter.AxisValue)
        ).ToList();

    /// <summary>流すケースを数える</summary>
    public static void Observe(CoverageCounter counter, string rowId, string driver) =>
        counter.Observe(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [Driver] = driver,
                [Shape] = rowId,
            }
        );

    /// <summary>台帳の行</summary>
    public static EngineDescriptor Descriptor =>
        new(
            EngineIds.Ai,
            "Drivers",
            "API キー接続のドライバの表（ドライバ 2 つ × 送る形〔発話の添付の組み合わせ・ツールの呼び出しの ID の往復〕）",
            Table,
            ChatDriverRows.Table.Count,
            [
                "tests/QuickER.Tests/AI/ChatEngines/ChatDriverAdapters.cs",
                "tests/QuickER.Tests/AI/ChatEngines/ChatDriverDimensions.cs",
                "tests/QuickER.Tests/AI/ChatEngines/ChatDriverRows.cs",
                "tests/QuickER.Tests/AI/ChatEngines/ChatDriverTests.cs",
                "tests/QuickER.Tests/AI/ChatEngines/ChatDriverToolRoundTrip.cs",
            ],
            ["src/QuickER.AI/AnthropicChatTurnDriver.cs", "src/QuickER.AI/OpenAiTurnDriver.cs"]
        );
}
