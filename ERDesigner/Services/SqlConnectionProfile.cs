using System;

namespace ERDesigner.Services;

/// <summary>
/// 名前を付けて保存可能な SQL Server 接続プロファイル。
/// パスワード以外を JSON に永続化し、パスワードは <see cref="SqlConnectionProfileStore"/> が DPAPI で別途暗号化保存します。
/// </summary>
public class SqlConnectionProfile
{
    /// <summary>プロファイルを一意に識別する ID。</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>表示名 (一覧で選ぶラベル)。</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>サーバ名。</summary>
    public string Server { get; set; } = string.Empty;

    /// <summary>データベース名。</summary>
    public string Database { get; set; } = string.Empty;

    /// <summary>認証方式。</summary>
    public SqlAuthMode AuthMode { get; set; } = SqlAuthMode.Windows;

    /// <summary>SQL/Azure AD 認証時のユーザー名。</summary>
    public string UserId { get; set; } = string.Empty;

    /// <summary>サーバ証明書を信頼するか。</summary>
    public bool TrustServerCertificate { get; set; } = true;

    /// <summary>パスワードを暗号化保存するか。</summary>
    public bool SavePassword { get; set; }

    /// <summary>このプロファイルから接続設定を構築します (パスワードは別途指定)。</summary>
    public SqlConnectionSettings ToSettings(string password) => new()
    {
        Server = Server,
        Database = Database,
        AuthMode = AuthMode,
        UserId = UserId,
        Password = password,
        TrustServerCertificate = TrustServerCertificate
    };
}
