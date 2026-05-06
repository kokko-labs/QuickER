using Microsoft.Data.SqlClient;

namespace ERDesigner.Services;

/// <summary>
/// SQL Server 接続設定を保持し、認証方式に応じた接続文字列を生成するクラス。
/// </summary>
public class SqlConnectionSettings
{
    /// <summary>サーバー名 (例: <c>localhost</c>, <c>tcp:server.database.windows.net,1433</c>)。</summary>
    public string Server { get; set; } = string.Empty;

    /// <summary>データベース名。</summary>
    public string Database { get; set; } = string.Empty;

    /// <summary>認証方式。</summary>
    public SqlAuthMode AuthMode { get; set; } = SqlAuthMode.Windows;

    /// <summary>SQL 認証または Azure AD Password 認証時のユーザー名。</summary>
    public string UserId { get; set; } = string.Empty;

    /// <summary>SQL 認証または Azure AD Password 認証時のパスワード。</summary>
    public string Password { get; set; } = string.Empty;

    /// <summary>サーバー証明書を信頼するか。</summary>
    public bool TrustServerCertificate { get; set; } = true;

    /// <summary>接続タイムアウト (秒)。</summary>
    public int ConnectTimeoutSeconds { get; set; } = 15;

    /// <summary>現在の設定値から ADO.NET の接続文字列を構築します。</summary>
    public string Build()
    {
        var b = new SqlConnectionStringBuilder
        {
            DataSource = Server,
            InitialCatalog = Database,
            TrustServerCertificate = TrustServerCertificate,
            ConnectTimeout = ConnectTimeoutSeconds,
            ApplicationName = "ERDesigner",
        };

        switch (AuthMode)
        {
            case SqlAuthMode.Windows:
                b.IntegratedSecurity = true;
                break;

            case SqlAuthMode.SqlServer:
                b.UserID = UserId;
                b.Password = Password;
                break;

            case SqlAuthMode.AzureAd:

                if (!string.IsNullOrWhiteSpace(UserId))
                {
                    // 非推奨の ActiveDirectoryPassword は使わず、対話式サインインへ誘導します。
                    b.Authentication = SqlAuthenticationMethod.ActiveDirectoryInteractive;
                    b.UserID = UserId;
                }
                else
                {
                    b.Authentication = SqlAuthenticationMethod.ActiveDirectoryDefault;
                }

                break;
        }

        return b.ConnectionString;
    }
}
