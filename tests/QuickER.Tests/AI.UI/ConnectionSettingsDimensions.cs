using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.AI.UI;

/// <summary>接続設定の表の次元表・網羅しない組の一覧・台帳の行</summary>
/// <remarks>行が宣言する（触る面, 手の種類）を数える。網羅すべき組は面 × 手の種類の全部。</remarks>
internal static class ConnectionSettingsDimensions
{
    /// <summary>網羅しない組の照合の範囲</summary>
    public const string Scope = "connection-settings";

    /// <summary>触る面</summary>
    public const string Area = "Area";

    /// <summary>手の種類</summary>
    public const string Op = "Op";

    /// <summary>次元表</summary>
    public static DimensionTable Table =>
        new(
            EngineIds.Ai,
            [
                new Factor(
                    Area,
                    Enum.GetValues<ConnectionArea>()
                        .Select(area => new Level(area.ToString()))
                        .ToList(),
                    "接続設定の面（接続方式ごと）"
                ),
                new Factor(
                    Op,
                    Enum.GetValues<ConnectionOp>().Select(op => new Level(op.ToString())).ToList(),
                    "手の種類"
                ),
            ]
        );

    /// <summary>網羅しない組の一覧（手で書く）</summary>
    public static CoverageGapSet Gaps =>
        CoverageGap.Set(
            EngineIds.Ai,
            [
                .. Accepted(
                    ConnectionArea.Common,
                    "モデルの履歴・API キー・接続の組み立ては接続方式ごとの設定に対する手で、方式に依らない面には無い（src/QuickER.AI.UI/ChatConnectionSettingsViewModel.cs の RecordSuccessfulModel・Remove*History・CommitApiKey・Build*Connection はどれも方式の設定を引く）",
                    ConnectionOp.Record,
                    ConnectionOp.Remove,
                    ConnectionOp.CommitKey,
                    ConnectionOp.Build
                ),
                .. Accepted(
                    ConnectionArea.ClaudeCode,
                    "Claude Code はモデルの履歴を持たない（src/QuickER.AI/AiSettings.cs の履歴は ApiModelHistory・CodexModelHistory・CopilotModelHistory の 3 つ）",
                    ConnectionOp.Record,
                    ConnectionOp.Remove
                ),
                .. Accepted(
                    ConnectionArea.ClaudeCode,
                    "API キーと接続の組み立ては API キー接続だけ（src/QuickER.AI.UI/ChatConnectionSettingsViewModel.cs の CommitApiKey・BuildOpenAiConnection・BuildAnthropicConnection・CreateApiKeyEngine）",
                    ConnectionOp.CommitKey,
                    ConnectionOp.Build
                ),
                .. Accepted(
                    ConnectionArea.Codex,
                    "API キーと接続の組み立ては API キー接続だけ（同上）",
                    ConnectionOp.CommitKey,
                    ConnectionOp.Build
                ),
                .. Accepted(
                    ConnectionArea.Copilot,
                    "API キーと接続の組み立ては API キー接続だけ（同上）",
                    ConnectionOp.CommitKey,
                    ConnectionOp.Build
                ),
            ]
        );

    /// <summary>製品の事実で起こり得ない（面, 手の種類）の項目</summary>
    private static IEnumerable<CoverageGapEntry> Accepted(
        ConnectionArea area,
        string reason,
        params ConnectionOp[] ops
    ) =>
        ops.Select(op =>
            CoverageGap.AcceptedTuple(
                Scope,
                "P3-11a",
                reason,
                (Area, area.ToString()),
                (Op, op.ToString())
            )
        );

    /// <summary>行の触る組を数える</summary>
    public static void Observe(CoverageCounter counter, ConnectionArea area, ConnectionOp op) =>
        counter.Observe(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [Area] = area.ToString(),
                [Op] = op.ToString(),
            }
        );

    /// <summary>台帳の行</summary>
    public static EngineDescriptor Descriptor =>
        new(
            EngineIds.Ai,
            "ConnectionSettings",
            "接続設定の操作の列の表（面 × 手の種類・不変条件 4 つ）",
            Table,
            ConnectionSettingsRows.Table.Count,
            [
                "tests/QuickER.Tests/AI.UI/ConnectionSettingsDimensions.cs",
                "tests/QuickER.Tests/AI.UI/ConnectionSettingsHarness.cs",
                "tests/QuickER.Tests/AI.UI/ConnectionSettingsRows.cs",
                "tests/QuickER.Tests/AI.UI/ConnectionSettingsRows.ApiKey.cs",
                "tests/QuickER.Tests/AI.UI/ConnectionSettingsRows.Codex.cs",
                "tests/QuickER.Tests/AI.UI/ConnectionSettingsRows.Copilot.cs",
                "tests/QuickER.Tests/AI.UI/ConnectionSettingsRows.Failure.cs",
                "tests/QuickER.Tests/AI.UI/ConnectionSettingsRows.Persistence.cs",
                "tests/QuickER.Tests/AI.UI/ConnectionSettingsRows.Tabs.cs",
                "tests/QuickER.Tests/AI.UI/ConnectionSettingsTests.cs",
            ],
            [
                "src/QuickER.AI.UI/ChatConnectionSettingsViewModel.cs",
                "src/QuickER.AI.UI/BackendTabSelection.cs",
                "src/QuickER.AI.UI/BackendConnectionChecks.cs",
            ]
        );
}
