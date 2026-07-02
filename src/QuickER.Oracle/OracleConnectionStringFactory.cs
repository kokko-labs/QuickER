using Oracle.ManagedDataAccess.Client;
using QuickER.Provider;

namespace QuickER.Oracle;

/// <summary>共通接続設定 <see cref="DbConnectionSettings"/> から Oracle の接続文字列を構築する</summary>
/// <remarks>
/// <para>
/// Oracle の認証は常にユーザー名 / パスワードとして扱う（<see cref="DbAuthMode.Windows"/> /
/// <see cref="DbAuthMode.AzureAd"/> が来ても同様）。
/// </para>
/// <para>
/// <c>DataSource</c> は EZConnect 形式 <c>host:port/service</c> で組み立てる。ポート未指定時は
/// Oracle の既定ポート 1521 を用いる。サービス名は <see cref="DbConnectionSettings.ServiceName"/> が
/// 非空ならそれを、空なら <see cref="DbConnectionSettings.Database"/> を用いる。
/// </para>
/// </remarks>
public static class OracleConnectionStringFactory
{
    /// <summary>共通接続設定から ADO.NET の接続文字列を構築する</summary>
    public static string Build(DbConnectionSettings settings)
    {
        var port = settings.Port ?? 1521;
        // サービス名は ServiceName 優先、空なら Database 名で代用する
        var service = string.IsNullOrWhiteSpace(settings.ServiceName)
            ? settings.Database
            : settings.ServiceName;

        var b = new OracleConnectionStringBuilder
        {
            DataSource = $"{settings.Host}:{port}/{service}",
            UserID = settings.UserId,
            Password = settings.Password,
            ConnectionTimeout = settings.ConnectTimeoutSeconds,
        };

        return b.ConnectionString;
    }
}
