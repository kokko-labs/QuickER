using Microsoft.Data.SqlClient;
using QuickER.Provider;

namespace QuickER.SqlServer;

/// <summary>共通接続設定 <see cref="DbConnectionSettings"/> から SQL Server の接続文字列を構築する</summary>
/// <remarks>
/// ポート指定時は <c>Host,Port</c> 形式の DataSource を用いる（省略時はインスタンス名運用を許容する）
/// 認証は <see cref="DbAuthMode"/> に応じて統合認証 / SQL 認証 / Azure AD 認証へ振り分ける
/// </remarks>
public static class SqlServerConnectionStringFactory
{
    /// <summary>共通接続設定から ADO.NET の接続文字列を構築する</summary>
    public static string Build(DbConnectionSettings settings)
    {
        // ポートが指定された場合のみ "Host,Port" 形式にする（SQL Server はインスタンス名運用が一般的なため）
        var dataSource = settings.Port is int port ? $"{settings.Host},{port}" : settings.Host;

        var b = new SqlConnectionStringBuilder
        {
            DataSource = dataSource,
            InitialCatalog = settings.Database,
            TrustServerCertificate = settings.TrustServerCertificate,
            ConnectTimeout = settings.ConnectTimeoutSeconds,
            ApplicationName = "QuickER",
        };

        switch (settings.AuthMode)
        {
            case DbAuthMode.Windows:
                b.IntegratedSecurity = true;
                break;

            case DbAuthMode.UsernamePassword:
                b.UserID = settings.UserId;
                b.Password = settings.Password;
                break;

            case DbAuthMode.AzureAd:

                if (!string.IsNullOrWhiteSpace(settings.UserId))
                {
                    // 非推奨の ActiveDirectoryPassword は使わず、対話式サインインへ誘導する
                    b.Authentication = SqlAuthenticationMethod.ActiveDirectoryInteractive;
                    b.UserID = settings.UserId;
                }
                else
                {
                    // ユーザー未指定時は既定資格情報チェーン（環境変数・マネージド ID 等）に委ねる
                    b.Authentication = SqlAuthenticationMethod.ActiveDirectoryDefault;
                }

                break;
        }

        return b.ConnectionString;
    }
}
