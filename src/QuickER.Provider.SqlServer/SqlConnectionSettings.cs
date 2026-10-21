using QuickER.Provider;

namespace QuickER.Provider.SqlServer;

/// <summary>SQL Server 接続設定を保持し、認証方式に応じた接続文字列を生成するクラス</summary>
public class SqlConnectionSettings
{
    /// <summary>サーバー名（例: <c>localhost</c>, <c>tcp:server.database.windows.net,1433</c>）</summary>
    public string Server { get; set; } = string.Empty;

    /// <summary>データベース名</summary>
    public string Database { get; set; } = string.Empty;

    /// <summary>認証方式</summary>
    public SqlAuthMode AuthMode { get; set; } = SqlAuthMode.Windows;

    /// <summary>
    /// SQL 認証のユーザー名、または Microsoft Entra ID 認証の対話式サインインのユーザー名
    /// （Microsoft Entra ID 認証で空なら既定の資格情報の連鎖を使う）
    /// </summary>
    public string UserId { get; set; } = string.Empty;

    /// <summary>SQL 認証のパスワード（Microsoft Entra ID 認証では使わない）</summary>
    public string Password { get; set; } = string.Empty;

    /// <summary>サーバー証明書を信頼するかどうか</summary>
    public bool TrustServerCertificate { get; set; } = true;

    /// <summary>接続タイムアウト（秒）</summary>
    public int ConnectTimeoutSeconds { get; set; } = 15;

    /// <summary>コマンド実行タイムアウト（秒）</summary>
    /// <remarks>
    /// スキーマ取込の各カタログ照会に適用し、<see cref="ToDbConnectionSettings"/> 経由で同期実行器へも渡る
    /// （<see cref="DbConnectionSettings.CommandTimeoutSeconds"/> と同じ意味論＝<c>0</c> は無制限）。
    /// 接続文字列（<see cref="Build"/>）には載せない。
    /// </remarks>
    public int CommandTimeoutSeconds { get; set; } = DbCommands.DefaultTimeoutSeconds;

    /// <summary>現在の設定値から ADO.NET の接続文字列を構築する</summary>
    /// <remarks>
    /// 組み立ては <see cref="SqlServerConnectionStringFactory.Build"/> が単一の正本（GUI・CLI の接続と同じ文字列になる）。
    /// この型はポートを持たないので、既定以外のポートは <see cref="Server"/> に <c>host,port</c> の形で書く。
    /// </remarks>
    public string Build() => SqlServerConnectionStringFactory.Build(ToDbConnectionSettings());

    /// <summary>この設定を方言中立の <see cref="DbConnectionSettings"/> へ変換する</summary>
    /// <remarks>認証方式は <see cref="SqlAuthMode.SqlServer"/> を <see cref="DbAuthMode.UsernamePassword"/> へ対応付ける</remarks>
    public DbConnectionSettings ToDbConnectionSettings() =>
        new()
        {
            Host = Server,
            Port = null,
            Database = Database,
            AuthMode = AuthMode switch
            {
                SqlAuthMode.Windows => DbAuthMode.Windows,
                SqlAuthMode.SqlServer => DbAuthMode.UsernamePassword,
                SqlAuthMode.EntraId => DbAuthMode.EntraId,
                _ => DbAuthMode.Windows,
            },
            UserId = UserId,
            Password = Password,
            TrustServerCertificate = TrustServerCertificate,
            ConnectTimeoutSeconds = ConnectTimeoutSeconds,
            CommandTimeoutSeconds = CommandTimeoutSeconds,
        };
}
