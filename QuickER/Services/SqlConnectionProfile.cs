namespace QuickER.Services;

/// <summary>名前を付けて保存可能な SQL Server 接続プロファイル</summary>
/// <remarks>
/// パスワード以外を JSON に永続化する パスワードは <see cref="SqlConnectionProfileStore"/> が
/// DPAPI で別途暗号化保存する
/// </remarks>
public class SqlConnectionProfile
{
    /// <summary>プロファイルの一意識別子</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>一覧での選択ラベルとなる表示名</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>サーバー名</summary>
    public string Server { get; set; } = string.Empty;

    /// <summary>データベース名</summary>
    public string Database { get; set; } = string.Empty;

    /// <summary>認証方式</summary>
    public SqlAuthMode AuthMode { get; set; } = SqlAuthMode.Windows;

    /// <summary>SQL / Azure AD 認証時のユーザー名</summary>
    public string UserId { get; set; } = string.Empty;

    /// <summary>サーバー証明書を信頼するかどうか</summary>
    public bool TrustServerCertificate { get; set; } = true;

    /// <summary>パスワードを暗号化保存するかどうか</summary>
    public bool SavePassword { get; set; }

    /// <summary>プロファイルから接続設定を構築する（パスワードは引数で別途指定する）</summary>
    public SqlConnectionSettings ToSettings(string password) =>
        new()
        {
            Server = Server,
            Database = Database,
            AuthMode = AuthMode,
            UserId = UserId,
            Password = password,
            TrustServerCertificate = TrustServerCertificate,
        };
}
