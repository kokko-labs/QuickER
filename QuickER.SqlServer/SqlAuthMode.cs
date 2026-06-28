namespace QuickER.SqlServer;

/// <summary>SQL Server への接続認証方式</summary>
public enum SqlAuthMode
{
    /// <summary>Windows 統合認証</summary>
    Windows,

    /// <summary>SQL Server 認証（ユーザー名 / パスワード）</summary>
    SqlServer,

    /// <summary>Azure AD 認証（Default / Interactive）</summary>
    AzureAd,
}
