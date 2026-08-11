using System.Reflection;

namespace QuickER.CodeGen.CSharp;

/// <summary>
/// QuickER のランタイム（スキーマ非依存の固定コード）を配布する NuGet パッケージの ID を単一ソースとして定義する。
/// </summary>
/// <remarks>
/// <para>
/// 生成コードのうちスキーマに依存しない固定部分（EntityBase・属性・VO 基底・JSON コンバータ・Repository 共通契約・
/// 方言別エンジン・EF Core 共通部品・インメモリ基盤）は <c>Templates/CSharpRuntime/*.scriban</c> から出力される。これを 5 分割の
/// NuGet パッケージ（コア＋QuickER の方言エンジン×方言数＋EF Core＋インメモリ）として配布できるようにするための ID を集約する。
/// </para>
/// <para>
/// パッケージ ID は、パッケージ書き出し時のソースの名前空間と一致させる（<see cref="RuntimePackageSourceRenderer"/> が
/// この定数を名前空間として使う）。方言エンジン・EF Core はコア（<see cref="Core"/>）の共通契約を <c>using</c> で参照する。
/// </para>
/// </remarks>
public static class RuntimePackages
{
    /// <summary>コアパッケージ（共通基盤・共通契約。BCL のみ依存）の ID＝固定名前空間。</summary>
    public const string Core = "QuickER.Runtime";

    /// <summary>QuickER の SQL Server エンジンパッケージ（<c>Microsoft.Data.SqlClient</c> 依存）の ID＝固定名前空間。</summary>
    public const string SqlServer = "QuickER.Runtime.SqlServer";

    /// <summary>QuickER の SQLite エンジンパッケージ（<c>Microsoft.Data.Sqlite</c> 依存）の ID＝固定名前空間。</summary>
    public const string Sqlite = "QuickER.Runtime.Sqlite";

    /// <summary>EF Core 共通部品パッケージ（EF Core 依存）の ID＝固定名前空間。</summary>
    public const string EntityFrameworkCore = "QuickER.Runtime.EntityFrameworkCore";

    /// <summary>インメモリ基盤パッケージ（DB 非依存・BCL のみ依存）の ID＝固定名前空間。</summary>
    public const string InMemory = "QuickER.Runtime.InMemory";

    /// <summary>
    /// パッケージ参照の案内（生成ヘッダ・GUI/CLI）に載せる既定バージョン。
    /// </summary>
    /// <remarks>
    /// 版はツール版とロックステップ（パッケージ版＝ツール版）で運用する。<see cref="ResolveGuidanceVersion"/> が
    /// <c>QuickER.CodeGen.CSharp</c> アセンブリのバージョン情報から解決できなかった場合にのみ用いるフォールバック値。
    /// </remarks>
    public const string DefaultVersion = "0.1.0";

    /// <summary>
    /// パッケージ参照の案内（生成ヘッダ・GUI/CLI）に載せるバージョン文字列を解決する。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>QuickER.CodeGen.CSharp</c> アセンブリの <see cref="AssemblyInformationalVersionAttribute"/>
    /// （<c>Directory.Build.props</c> の <c>VersionPrefix</c> 由来。ロックステップでパッケージ版と一致する）から
    /// 解決する。ビルドメタデータ（コミットハッシュ等。<c>"+"</c> 以降）が付与されている場合は除去する
    /// （<c>0.1.0+abcdef...</c> → <c>0.1.0</c>。NuGet バージョンにビルドメタデータを含めない運用のため）。
    /// </para>
    /// <para>
    /// 属性が取得できない・値が空の場合のみ <see cref="DefaultVersion"/> へフォールバックする。
    /// GUI・CLI の双方から同一の結果を得るための単一の正とする。
    /// </para>
    /// </remarks>
    /// <returns>ビルドメタデータを除いたバージョン文字列（例: <c>0.1.0</c>）</returns>
    public static string ResolveGuidanceVersion()
    {
        var informationalVersion = typeof(RuntimePackages)
            .Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;

        if (string.IsNullOrWhiteSpace(informationalVersion))
        {
            return DefaultVersion;
        }

        var plusIndex = informationalVersion.IndexOf('+');
        var version = plusIndex >= 0 ? informationalVersion[..plusIndex] : informationalVersion;

        return string.IsNullOrWhiteSpace(version) ? DefaultVersion : version;
    }
}
