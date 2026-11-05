using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.Settings;

/// <summary>設定の操作列の表の次元表・網羅しない組の一覧・台帳の行</summary>
/// <remarks>
/// 製品の手（Load・Save・SaveTo・TryLoadFrom）を 1 ケースとして、手の種類 × 手の前の設定ファイルの状態を数える。
/// 状態は手の列を流して実際のファイルから読むので、数えるときは行を流す（ファイル 1 つの読み書きだけで軽い）。
/// </remarks>
internal static class SettingsStoreDimensions
{
    /// <summary>網羅しない組の照合の範囲</summary>
    public const string Scope = "settings-store";

    /// <summary>手の種類</summary>
    public const string Op = "Op";

    /// <summary>手の前の設定ファイルの状態</summary>
    public const string State = "State";

    /// <summary>次元表</summary>
    public static DimensionTable Table { get; } =
        new(
            EngineIds.DocumentFile,
            [
                new Factor(
                    Op,
                    [
                        new Level(nameof(SettingsOpKind.Load)),
                        new Level(nameof(SettingsOpKind.Save)),
                        new Level(nameof(SettingsOpKind.SaveTo)),
                        new Level(nameof(SettingsOpKind.TryLoadFrom)),
                    ],
                    "設定 JSON ストアの手"
                ),
                new Factor(
                    State,
                    Enum.GetValues<SettingsFileState>()
                        .Select(s => new Level(s.ToString()))
                        .ToList(),
                    "手の前の設定ファイルの状態"
                ),
            ]
        );

    /// <summary>網羅しない組の一覧</summary>
    /// <remarks>
    /// SaveTo と TryLoadFrom は設定ファイルとは別のファイルを読み書きするので、元の単発のテストは設定ファイルの状態との組を
    /// 1 つの行（tryloadfrom-and-saveto-unaffected-by-suppression＝ロック中の SaveTo・ロックを外した後の TryLoadFrom）と、
    /// 設定ファイル自身を読む TryLoadFrom の行しか持たない。足すなら表の行で足す。
    /// </remarks>
    public static CoverageGapSet Gaps =>
        CoverageGap.Set(
            EngineIds.DocumentFile,
            [
                CoverageGap.Matching(
                    Scope,
                    "P3-9",
                    "P4",
                    "ロック中でない設定ファイルの状態での SaveTo の行が無い",
                    Op,
                    State,
                    (op, state) =>
                        op == nameof(SettingsOpKind.SaveTo)
                        && state != nameof(SettingsFileState.Locked)
                ),
                CoverageGap.Matching(
                    Scope,
                    "P3-9",
                    "P4",
                    "設定ファイルが無い・ロック中の TryLoadFrom の行が無い",
                    Op,
                    State,
                    (op, state) =>
                        op == nameof(SettingsOpKind.TryLoadFrom)
                        && state
                            is nameof(SettingsFileState.Missing)
                                or nameof(SettingsFileState.Locked)
                ),
            ]
        );

    /// <summary>台帳の行</summary>
    public static EngineDescriptor Descriptor =>
        new(
            EngineIds.DocumentFile,
            "Settings",
            "設定 JSON ストアの操作列の表（手 × 手の前のファイルの状態）",
            Table,
            SettingsStoreRows.Table.Count,
            [
                "tests/QuickER.Tests/Settings/SettingsStoreDimensions.cs",
                "tests/QuickER.Tests/Settings/SettingsStoreRows.cs",
                "tests/QuickER.Tests/Settings/SettingsStoreRows.NullAndCase.cs",
                "tests/QuickER.Tests/Settings/SettingsStoreRows.Recovery.cs",
                "tests/QuickER.Tests/Settings/SettingsStoreRows.Unavailable.cs",
                "tests/QuickER.Tests/Settings/SettingsStoreRows.UnknownKeys.cs",
                "tests/QuickER.Tests/Settings/SettingsStoreTests.cs",
            ],
            ["src/QuickER.Settings"]
        );

    /// <summary>1 行を流して観測した手を数える</summary>
    public static void Observe(
        CoverageCounter counter,
        IReadOnlyList<(SettingsOpKind Op, SettingsFileState State)> observed
    )
    {
        foreach (var (op, state) in observed)
        {
            counter.Observe(
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    [Op] = op.ToString(),
                    [State] = state.ToString(),
                }
            );
        }
    }
}
