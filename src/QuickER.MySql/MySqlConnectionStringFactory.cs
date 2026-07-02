using MySqlConnector;
using QuickER.Provider;

namespace QuickER.MySql;

/// <summary>共通接続設定 <see cref="DbConnectionSettings"/> から MySQL の接続文字列を構築する</summary>
/// <remarks>
/// MySQL の認証は常にユーザー名 / パスワードとして扱う（<see cref="DbAuthMode.Windows"/> /
/// <see cref="DbAuthMode.AzureAd"/> が来ても同様）。SQL Server 固有の <c>TrustServerCertificate</c> /
/// Oracle 固有の <c>ServiceName</c> は無視する。
/// </remarks>
public static class MySqlConnectionStringFactory
{
    /// <summary>共通接続設定から ADO.NET の接続文字列を構築する</summary>
    /// <param name="settings">共通接続設定</param>
    /// <param name="allowUserVariables">
    /// <c>@fk</c> 等のユーザー変数を使うプリペアド動的 SQL を実行する場合は <c>true</c>。
    /// スキーマ同期の Executor から呼ぶ場合のみ有効化する。
    /// </param>
    public static string Build(DbConnectionSettings settings, bool allowUserVariables = false)
    {
        var b = new MySqlConnectionStringBuilder
        {
            Server = settings.Host,
            // ポート未指定時は MySQL の既定ポート 3306 を用いる
            Port = (uint)(settings.Port ?? 3306),
            Database = settings.Database,
            UserID = settings.UserId,
            Password = settings.Password,
            ConnectionTimeout = (uint)settings.ConnectTimeoutSeconds,
            ApplicationName = "QuickER",
            AllowUserVariables = allowUserVariables,
        };

        return b.ConnectionString;
    }
}
