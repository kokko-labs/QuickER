using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.AI.UI;

/// <summary>保存した API キーの表の次元表・網羅しない組の一覧・台帳の行</summary>
/// <remarks>
/// QuickER.AI.UI の <c>ApiKeyStore</c>（DPAPI の暗号ファイル）と、それを読み書きする接続設定の VM の保存のオン／オフを見る。
/// 1 行＝入口 1 つ × 入口の前のファイルの状態 1 つ。状態は行が作ったファイルを入口の直前に実際に読んで数えるので、
/// 数えるときは行を流す（ファイル 1 つの読み書きだけで軽い）。
/// </remarks>
internal static class ApiKeyStoreDimensions
{
    /// <summary>網羅しない組の照合の範囲</summary>
    public const string Scope = "ai-api-key-store";

    /// <summary>入口</summary>
    public const string Entrance = "Entrance";

    /// <summary>入口の前のファイルの状態</summary>
    public const string File = "File";

    /// <summary>次元表</summary>
    public static DimensionTable Table { get; } =
        new(
            EngineIds.Ai,
            [
                new Factor(
                    Entrance,
                    Enum.GetValues<ApiKeyEntrance>().Select(e => new Level(e.ToString())).ToList(),
                    "保存した API キーの入口（ストアの読む・書く・消すと、接続設定の VM の保存のオン／オフ）"
                ),
                new Factor(
                    File,
                    Enum.GetValues<ApiKeyFileState>().Select(s => new Level(s.ToString())).ToList(),
                    "入口の前の暗号ファイルの状態"
                ),
            ]
        );

    /// <summary>網羅しない組の一覧（空。入口 × ファイルの状態のすべての組を行が流す）</summary>
    public static CoverageGapSet Gaps => CoverageGap.Set(EngineIds.Ai, []);

    /// <summary>1 行が流した（入口, 状態）を数える</summary>
    public static void Observe(
        CoverageCounter counter,
        ApiKeyEntrance entrance,
        ApiKeyFileState state
    ) =>
        counter.Observe(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [Entrance] = entrance.ToString(),
                [File] = state.ToString(),
            }
        );

    /// <summary>台帳の行</summary>
    public static EngineDescriptor Descriptor =>
        new(
            EngineIds.Ai,
            "ApiKeyStore",
            "保存した API キーの表（入口 × 暗号ファイルの状態。QuickER.AI.UI の ApiKeyStore と接続設定の VM の保存のオン／オフ）",
            Table,
            ApiKeyStoreRows.Table.Count,
            [
                "tests/QuickER.Tests/AI.UI/ApiKeyStoreDimensions.cs",
                "tests/QuickER.Tests/AI.UI/ApiKeyStoreRows.cs",
                "tests/QuickER.Tests/AI.UI/ApiKeyStoreTests.cs",
            ],
            [
                "src/QuickER.AI.UI/ApiKeyStore.cs",
                "src/QuickER.AI.UI/SavedApiKey.cs",
                "src/QuickER.AI.UI/ChatConnectionSettingsViewModel.cs",
            ]
        );
}
