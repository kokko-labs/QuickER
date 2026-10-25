using System.Reflection;

namespace QuickER.AI.Mock;

/// <summary>
/// モックプロジェクト生成が書き出す csproj の PackageReference の版を返す。
/// </summary>
/// <remarks>
/// <para>
/// 版の正本はリポジトリの <c>Directory.Packages.props</c>（中央パッケージ管理）で、ビルド時に
/// <c>QuickER.AI.Mock.csproj</c> が <see cref="AssemblyMetadataAttribute"/>（キー＝<see cref="MetadataKeyPrefix"/>＋パッケージ ID）
/// としてこのアセンブリへ写す。版の数字を C# に書かないのは、QuickER 本体が脆弱性対策で上げた版と生成物の版が
/// 2 か所に書かれて食い違う形を残さないため（生成物だけ古い版のまま、という食い違いが実際にあった）。
/// </para>
/// <para>
/// メタデータが無い・値が空（props から PackageVersion が消えた・csproj の写しを書き忘れた）ときは、
/// 黙って版なしの参照を書かずに例外で止める。
/// </para>
/// </remarks>
internal static class MockProjectPackageVersions
{
    /// <summary>アセンブリメタデータのキーの接頭辞（csproj の AssemblyAttribute と揃える）</summary>
    internal const string MetadataKeyPrefix = "QuickER.MockProjectPackageVersion:";

    /// <summary>WPF ターゲットの MVVM 基盤</summary>
    internal const string CommunityToolkitMvvm = "CommunityToolkit.Mvvm";

    /// <summary>WPF ターゲットの DI コンテナ</summary>
    internal const string DependencyInjection = "Microsoft.Extensions.DependencyInjection";

    /// <summary>SQL Server の ADO.NET プロバイダ</summary>
    internal const string SqlClient = "Microsoft.Data.SqlClient";

    /// <summary>SQLite の ADO.NET プロバイダ</summary>
    internal const string Sqlite = "Microsoft.Data.Sqlite";

    /// <summary>
    /// SQLite のネイティブ層（<see cref="Sqlite"/> が推移的に持ち込む 2.1 系でなく、QuickER と同じ 3.0 系の版を明示参照して SQLite の版をそろえる）
    /// </summary>
    internal const string SqliteNativeBundle = "SQLitePCLRaw.bundle_e_sqlite3";

    /// <summary>
    /// 暗号の PKCS ライブラリ（<see cref="SqlClient"/> が推移的に持ち込む版に既知の脆弱性があるため、
    /// 共有フレームワークが刈り込まないターゲットでは修正済みの版を明示参照する）
    /// </summary>
    internal const string CryptographyPkcs = "System.Security.Cryptography.Pkcs";

    /// <summary>このアセンブリへ版を写しているパッケージ ID の一覧（csproj の AssemblyAttribute と 1:1）</summary>
    internal static IReadOnlyList<string> PackageIds { get; } =
    [
        CommunityToolkitMvvm,
        DependencyInjection,
        SqlClient,
        Sqlite,
        SqliteNativeBundle,
        CryptographyPkcs,
    ];

    /// <summary>アセンブリメタデータから読み出した版（パッケージ ID → 版。空の値も含めて保持する）</summary>
    private static readonly Lazy<IReadOnlyDictionary<string, string>> Versions = new(LoadVersions);

    /// <summary>パッケージの版を返す</summary>
    /// <param name="packageId">パッケージ ID（<see cref="PackageIds"/> のいずれか）</param>
    /// <exception cref="InvalidOperationException">版がアセンブリへ写されていない・空の場合</exception>
    internal static string Get(string packageId)
    {
        if (Versions.Value.TryGetValue(packageId, out var version) && version.Length > 0)
        {
            return version;
        }

        throw new InvalidOperationException(
            $"The package version of '{packageId}' is not embedded in {typeof(MockProjectPackageVersions).Assembly.GetName().Name}. "
                + "Declare it as a PackageVersion in Directory.Packages.props and map it with an AssemblyMetadata attribute in QuickER.AI.Mock.csproj."
        );
    }

    /// <summary>アセンブリメタデータのうち、キーが <see cref="MetadataKeyPrefix"/> で始まるものを集める</summary>
    private static IReadOnlyDictionary<string, string> LoadVersions() =>
        typeof(MockProjectPackageVersions)
            .Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .Where(attribute =>
                attribute.Key.StartsWith(MetadataKeyPrefix, StringComparison.Ordinal)
            )
            .ToDictionary(
                attribute => attribute.Key[MetadataKeyPrefix.Length..],
                attribute => attribute.Value?.Trim() ?? string.Empty,
                StringComparer.Ordinal
            );
}
