using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.Db.UI;

/// <summary>接続プロファイルの操作列の表の次元表・網羅しない組の一覧・台帳の行</summary>
/// <remarks>
/// QuickER.Db.UI の SqlConnectionProfileStore（connections.json と DPAPI の暗号ファイル）を見る。
/// 製品の手（9 つの入口）を 1 ケースとして、手の種類 × 手の前の connections.json の状態 × 手の対象の暗号ファイルの状態 ×
/// 暗号フォルダの状態を 2 因子の組で数える。状態は手の列を流して実際のファイルから読むので、数えるときは行を流す
/// （ファイル数個の読み書きだけで軽い）。
/// </remarks>
internal static class ProfileStoreDimensions
{
    /// <summary>網羅しない組の照合の範囲</summary>
    public const string Scope = "os-boundary-profile-store";

    /// <summary>手の種類</summary>
    public const string Op = "Op";

    /// <summary>手の前の connections.json の状態</summary>
    public const string Json = "Json";

    /// <summary>手の対象の暗号ファイルの状態</summary>
    public const string Secret = "Secret";

    /// <summary>手の前の暗号フォルダの状態</summary>
    public const string Folder = "Folder";

    /// <summary>次元表</summary>
    public static DimensionTable Table { get; } =
        new(
            EngineIds.OsBoundary,
            [
                new Factor(
                    Op,
                    Enum.GetValues<ProfileStepKind>()
                        .Where(kind => kind <= ProfileStepKind.Delete)
                        .Select(kind => new Level(kind.ToString()))
                        .ToList(),
                    "接続プロファイルのストアの入口"
                ),
                new Factor(
                    Json,
                    Enum.GetValues<ProfileJsonState>()
                        .Select(s => new Level(s.ToString()))
                        .ToList(),
                    "手の前の connections.json の状態"
                ),
                new Factor(
                    Secret,
                    Enum.GetValues<ProfileSecretState>()
                        .Select(s => new Level(s.ToString()))
                        .ToList(),
                    "手の対象の暗号ファイルの状態"
                ),
                new Factor(
                    Folder,
                    Enum.GetValues<ProfileSecretFolderState>()
                        .Select(s => new Level(s.ToString()))
                        .ToList(),
                    "手の前の暗号フォルダの状態"
                ),
            ],
            isValid: IsValid
        );

    /// <summary>網羅しない組の一覧（空。次元表のすべての組を行が流す）</summary>
    /// <remarks>
    /// 以前あった項目（書く入口 × connections.json の状態・読む入口・暗号ファイルの状態・暗号フォルダの拒否など）は、
    /// 行 <c>legacy-sweep-*</c> と <c>pairing-failed-upsert-replacing-deny-create-secret</c> が流すので消した。
    /// 組が出なくなる状態を足すときは、理由つきで <see cref="CoverageGap"/> に書く。
    /// </remarks>
    public static CoverageGapSet Gaps => CoverageGap.Set(EngineIds.OsBoundary, []);

    /// <summary>台帳の行</summary>
    public static EngineDescriptor Descriptor =>
        new(
            EngineIds.OsBoundary,
            "ProfileStore",
            "接続プロファイルの保存の表（入口 × connections.json の状態 × 暗号ファイルの状態 × 暗号フォルダの状態。QuickER.Db.UI の SqlConnectionProfileStore）",
            Table,
            ProfileStoreRows.Table.Count,
            [
                "tests/QuickER.Tests/Db.UI/ProfileStoreDimensions.cs",
                "tests/QuickER.Tests/Db.UI/ProfileStoreRows.cs",
                "tests/QuickER.Tests/Db.UI/ProfileStoreRows.Basic.cs",
                "tests/QuickER.Tests/Db.UI/ProfileStoreRows.Legacy.cs",
                "tests/QuickER.Tests/Db.UI/ProfileStoreRows.Tls.cs",
                "tests/QuickER.Tests/Db.UI/ProfileStoreRows.Unreadable.cs",
                "tests/QuickER.Tests/Db.UI/ProfileStoreRows.Pairing.cs",
                "tests/QuickER.Tests/Db.UI/ProfileStoreRows.Sweep.cs",
                "tests/QuickER.Tests/Db.UI/ProfileStoreTests.cs",
            ],
            ["src/QuickER.Db.UI/SqlConnectionProfileStore.cs"]
        );

    /// <summary>1 行を流して観測した手を数える</summary>
    public static void Observe(CoverageCounter counter, IReadOnlyList<ProfileObserved> observed)
    {
        foreach (var step in observed)
        {
            counter.Observe(
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    [Op] = step.Op.ToString(),
                    [Json] = step.Json.ToString(),
                    [Secret] = step.Secret.ToString(),
                    [Folder] = step.Folder.ToString(),
                }
            );
        }
    }

    /// <summary>
    /// 原理的に起こり得ない組：LoadAll は暗号ファイルを対象に持たないので、対象の暗号ファイルがある状態は無い
    /// </summary>
    /// <remarks>
    /// 読む手と暗号フォルダの作成の拒否は、読む手が暗号フォルダへ書かないだけで、拒否が掛かったまま読む状態は起こり得る
    /// （起こり得ない組ではないので <see cref="Gaps"/> に書く）。
    /// </remarks>
    private static bool IsValid(IReadOnlyDictionary<string, string> assignment) =>
        !(
            assignment.TryGetValue(Op, out var op)
            && op == nameof(ProfileStepKind.LoadAll)
            && assignment.TryGetValue(Secret, out var secret)
            && secret != nameof(ProfileSecretState.None)
        );
}
