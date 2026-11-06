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

    /// <summary>網羅しない組の一覧</summary>
    /// <remarks>
    /// どれも元の単発のテストが持たない組で、P3-10 では行を足していない（期待を製品の XmlDoc から導けない組、
    /// または元のテストの入力を変えずに足せない組）。足すなら表の行で足し、ここから消す。
    /// 次元表は 2 因子の組で数えるので、項目も因子の組ごとに書く。
    /// </remarks>
    public static CoverageGapSet Gaps =>
        CoverageGap.Set(
            EngineIds.OsBoundary,
            [
                Pair(
                    Op,
                    Json,
                    "書く入口の読み込み（LoadDataForWrite）の分類は Upsert と SaveLastUsed の行が代表で見ている。"
                        + "Delete・Rename・置き換えつきの 2 入口で同じ状態を流す行は元のテストが持たない",
                    (
                        ProfileStepKind.Delete,
                        [
                            ProfileJsonState.Corrupt,
                            ProfileJsonState.LegacyArray,
                            ProfileJsonState.Missing,
                            ProfileJsonState.NullElements,
                        ]
                    ),
                    (
                        ProfileStepKind.Rename,
                        [
                            ProfileJsonState.Corrupt,
                            ProfileJsonState.LegacyArray,
                            ProfileJsonState.Missing,
                            ProfileJsonState.NullElements,
                        ]
                    ),
                    (
                        ProfileStepKind.RenameReplacing,
                        [
                            ProfileJsonState.Corrupt,
                            ProfileJsonState.LegacyArray,
                            ProfileJsonState.Missing,
                            ProfileJsonState.NullElements,
                        ]
                    ),
                    (
                        ProfileStepKind.UpsertReplacing,
                        [
                            ProfileJsonState.Corrupt,
                            ProfileJsonState.LegacyArray,
                            ProfileJsonState.Missing,
                            ProfileJsonState.NullElements,
                        ]
                    )
                ),
                Pair(
                    Op,
                    Json,
                    "Upsert と SaveLastUsed は、元のテストが持つ JSON の状態（Missing・Corrupt・NullElements・Locked と Healthy）の一部だけを流す。"
                        + "旧配列形式と破損した JSON の上での SaveLastUsed・null の要素を持つ JSON の上での SaveLastUsed は持たない",
                    (ProfileStepKind.Upsert, [ProfileJsonState.LegacyArray]),
                    (
                        ProfileStepKind.SaveLastUsed,
                        [
                            ProfileJsonState.Corrupt,
                            ProfileJsonState.LegacyArray,
                            ProfileJsonState.NullElements,
                        ]
                    )
                ),
                Pair(
                    Op,
                    Json,
                    "読む入口は同じ読み込み（LoadData）を通り、LoadAll の行が代表で見ている。"
                        + "LoadLastUsed・LoadPassword を壊れた・旧形式・null の要素・無い JSON の上で流す行は元のテストが持たない"
                        + "（破損した JSON の LoadAll も持たない）",
                    (ProfileStepKind.LoadAll, [ProfileJsonState.Corrupt]),
                    (
                        ProfileStepKind.LoadLastUsed,
                        [
                            ProfileJsonState.Corrupt,
                            ProfileJsonState.LegacyArray,
                            ProfileJsonState.Missing,
                            ProfileJsonState.NullElements,
                        ]
                    ),
                    (
                        ProfileStepKind.LoadPassword,
                        [
                            ProfileJsonState.Corrupt,
                            ProfileJsonState.LegacyArray,
                            ProfileJsonState.Missing,
                            ProfileJsonState.NullElements,
                        ]
                    )
                ),
                Pair(
                    Op,
                    Secret,
                    "復号できない暗号ファイルの上での操作は、読む LoadPassword と名前だけの変更 Rename の 2 行だけを元のテストが持つ。"
                        + "空で上書きされず暗号ファイルが残ること（設計の狙い）を Upsert・SaveLastUsed・Delete・置き換えつきの入口・"
                        + "LoadLastUsed で確かめる行は持たない",
                    (ProfileStepKind.Delete, [ProfileSecretState.Unreadable]),
                    (ProfileStepKind.LoadLastUsed, [ProfileSecretState.Unreadable]),
                    (ProfileStepKind.RenameReplacing, [ProfileSecretState.Unreadable]),
                    (ProfileStepKind.SaveLastUsed, [ProfileSecretState.Unreadable]),
                    (ProfileStepKind.Upsert, [ProfileSecretState.Unreadable]),
                    (ProfileStepKind.UpsertReplacing, [ProfileSecretState.Unreadable])
                ),
                Pair(
                    Op,
                    Secret,
                    "暗号ファイルを掴まれた上での操作は、LoadPassword と、書く入口・Delete・置き換えつきの入口（掴み方は FileShare.Read）だけを"
                        + "元のテストが持つ。LoadLastUsed と名前だけの変更 Rename は持たない",
                    (ProfileStepKind.LoadLastUsed, [ProfileSecretState.Locked]),
                    (ProfileStepKind.Rename, [ProfileSecretState.Locked])
                ),
                Pair(
                    Op,
                    Secret,
                    "置き換えつきの 2 入口は、置き換え先の暗号ファイルがある行だけを元のテストが持つ（置き換え先に暗号ファイルが無い行は持たない）",
                    (ProfileStepKind.RenameReplacing, [ProfileSecretState.None]),
                    (ProfileStepKind.UpsertReplacing, [ProfileSecretState.None])
                ),
                Pair(
                    Op,
                    Folder,
                    "読む入口は暗号フォルダへ書かないので、作成の拒否が掛かったまま読む状態は起こり得るが、元のテストは持たない"
                        + "（拒否は書く入口の失敗を作る仕掛けとしてだけ使っている）",
                    (ProfileStepKind.LoadAll, [ProfileSecretFolderState.CreateDenied]),
                    (ProfileStepKind.LoadLastUsed, [ProfileSecretFolderState.CreateDenied]),
                    (ProfileStepKind.LoadPassword, [ProfileSecretFolderState.CreateDenied])
                ),
                Pair(
                    Op,
                    Folder,
                    "作成の拒否は、新しい暗号ファイルを書く入口（パスワードを保存する SaveLastUsed と Upsert）だけを元のテストが持つ。"
                        + "Delete と Rename は新しい暗号ファイルを書かず、置き換えつきの Upsert は書くが、元のテストは持たない",
                    (ProfileStepKind.Delete, [ProfileSecretFolderState.CreateDenied]),
                    (ProfileStepKind.Rename, [ProfileSecretFolderState.CreateDenied]),
                    (ProfileStepKind.RenameReplacing, [ProfileSecretFolderState.CreateDenied]),
                    (ProfileStepKind.UpsertReplacing, [ProfileSecretFolderState.CreateDenied])
                ),
                Pair(
                    Json,
                    Secret,
                    "暗号ファイルの状態を作る行（復号できない・掴まれた・読める）は、connections.json が読める行（Healthy）から始まる。"
                        + "connections.json が無い・旧形式・破損・null の要素の上に暗号ファイルがある行は元のテストが持たない",
                    (
                        ProfileJsonState.Missing,
                        [
                            ProfileSecretState.Readable,
                            ProfileSecretState.Unreadable,
                            ProfileSecretState.Locked,
                        ]
                    ),
                    (
                        ProfileJsonState.LegacyArray,
                        [
                            ProfileSecretState.Readable,
                            ProfileSecretState.Unreadable,
                            ProfileSecretState.Locked,
                        ]
                    ),
                    (
                        ProfileJsonState.Corrupt,
                        [
                            ProfileSecretState.Readable,
                            ProfileSecretState.Unreadable,
                            ProfileSecretState.Locked,
                        ]
                    ),
                    (
                        ProfileJsonState.NullElements,
                        [
                            ProfileSecretState.Readable,
                            ProfileSecretState.Unreadable,
                            ProfileSecretState.Locked,
                        ]
                    )
                ),
                Pair(
                    Json,
                    Secret,
                    "connections.json を掴んだ行は暗号ファイルを掴まず、復号できない暗号ファイルも作らない（掴む対象は片方ずつ）",
                    (
                        ProfileJsonState.Locked,
                        [ProfileSecretState.Unreadable, ProfileSecretState.Locked]
                    )
                ),
                Pair(
                    Json,
                    Folder,
                    "暗号フォルダの作成の拒否は、connections.json が読める行（Healthy）の上でだけ元のテストが使う",
                    (ProfileJsonState.Missing, [ProfileSecretFolderState.CreateDenied]),
                    (ProfileJsonState.LegacyArray, [ProfileSecretFolderState.CreateDenied]),
                    (ProfileJsonState.Corrupt, [ProfileSecretFolderState.CreateDenied]),
                    (ProfileJsonState.NullElements, [ProfileSecretFolderState.CreateDenied]),
                    (ProfileJsonState.Locked, [ProfileSecretFolderState.CreateDenied])
                ),
                Pair(
                    Secret,
                    Folder,
                    "暗号フォルダの作成の拒否は、手の対象の暗号ファイルが読める行（前回接続・置き換え先を持つ失敗の行）の上でだけ元のテストが使う",
                    (ProfileSecretState.None, [ProfileSecretFolderState.CreateDenied]),
                    (ProfileSecretState.Unreadable, [ProfileSecretFolderState.CreateDenied]),
                    (ProfileSecretState.Locked, [ProfileSecretFolderState.CreateDenied])
                ),
            ]
        );

    /// <summary>因子の組ごとに、値を名指しした組をまとめて 1 つの項目にする（理由が同じ組）</summary>
    private static CoverageGapEntry Pair(
        string first,
        string second,
        string reason,
        params (object First, object[] Seconds)[] pairs
    )
    {
        var set = pairs
            .SelectMany(p => p.Seconds.Select(s => (p.First.ToString()!, s.ToString()!)))
            .ToHashSet();
        return CoverageGap.Matching(
            Scope,
            "P3-10",
            "P4",
            reason,
            first,
            second,
            (a, b) => set.Contains((a, b))
        );
    }

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
