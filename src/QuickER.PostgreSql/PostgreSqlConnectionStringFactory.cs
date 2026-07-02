using Npgsql;
using QuickER.Provider;

namespace QuickER.PostgreSql;

/// <summary>共通接続設定 <see cref="DbConnectionSettings"/> から PostgreSQL の接続文字列を構築する</summary>
/// <remarks>
/// PostgreSQL の認証は常にユーザー名 / パスワードとして扱う（<see cref="DbAuthMode.Windows"/> /
/// <see cref="DbAuthMode.AzureAd"/> が来ても同様）。SQL Server 固有の <c>TrustServerCertificate</c> /
/// <c>ServiceName</c> は無視する。
/// </remarks>
public static class PostgreSqlConnectionStringFactory
{
    /// <summary>共通接続設定から ADO.NET の接続文字列を構築する</summary>
    public static string Build(DbConnectionSettings settings)
    {
        var b = new NpgsqlConnectionStringBuilder
        {
            Host = settings.Host,
            // ポート未指定時は PostgreSQL の既定ポート 5432 を用いる
            Port = settings.Port ?? 5432,
            Database = settings.Database,
            Username = settings.UserId,
            Password = settings.Password,
            Timeout = settings.ConnectTimeoutSeconds,
            ApplicationName = "QuickER",
        };

        return b.ConnectionString;
    }
}
