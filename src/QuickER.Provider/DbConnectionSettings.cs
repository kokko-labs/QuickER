namespace QuickER.Provider;

/// <summary>DB 接続の認証方式（方言横断の共通表現）</summary>
public enum DbAuthMode
{
    /// <summary>Windows 統合認証（SQL Server のみ）</summary>
    Windows,

    /// <summary>ユーザー名 / パスワード認証</summary>
    UsernamePassword,

    /// <summary>Azure AD 認証（SQL Server のみ）</summary>
    AzureAd,
}

/// <summary>DB 接続の共通設定。方言固有フィールドも単純さ優先でここに持つ</summary>
public class DbConnectionSettings
{
    /// <summary>接続先ホスト（SQL Server ではサーバー名。インスタンス表記可）</summary>
    public string Host { get; set; } = "";

    /// <summary>接続ポート。null の場合は方言の既定ポートを用いる</summary>
    public int? Port { get; set; }

    /// <summary>データベース名</summary>
    public string Database { get; set; } = "";

    /// <summary>認証方式（Windows / AzureAd は SQL Server のみ）</summary>
    public DbAuthMode AuthMode { get; set; } = DbAuthMode.Windows;

    /// <summary>ユーザー名（<see cref="DbAuthMode.UsernamePassword"/> 等で使用）</summary>
    public string UserId { get; set; } = "";

    /// <summary>パスワード（<see cref="DbAuthMode.UsernamePassword"/> 等で使用）</summary>
    public string Password { get; set; } = "";

    /// <summary>サーバー証明書を信頼するか（SQL Server 固有）</summary>
    public bool TrustServerCertificate { get; set; } = true;

    /// <summary>接続タイムアウト（秒）</summary>
    public int ConnectTimeoutSeconds { get; set; } = 15;

    /// <summary>サービス名（Oracle 固有・将来使用）</summary>
    public string ServiceName { get; set; } = "";

    /// <summary>データベースファイルのパス（SQLite 固有。ファイル型 DB はサーバー系フィールドの代わりにこれを使う）</summary>
    public string FilePath { get; set; } = "";
}
