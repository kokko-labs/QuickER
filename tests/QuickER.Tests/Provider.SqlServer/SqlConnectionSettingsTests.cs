using AwesomeAssertions;
using QuickER.Provider.SqlServer;
using QuickER.Services;

namespace QuickER.Tests.Provider.SqlServer;

/// <summary><see cref="SqlConnectionSettings.Build"/> が認証方式に応じた接続文字列を構築することを検証するテストクラス</summary>
public class SqlConnectionSettingsTests
{
    /// <summary>Windows 認証で IntegratedSecurity が有効になることを検証する</summary>
    [Fact(DisplayName = "Windows 認証で IntegratedSecurity が有効になる")]
    public void Build_Windows_SetsIntegratedSecurity()
    {
        var s = new SqlConnectionSettings
        {
            Server = "localhost",
            Database = "Db",
            AuthMode = SqlAuthMode.Windows,
        };

        var cs = s.Build();
        cs.Should().Contain("Integrated Security=True");
        cs.Should().Contain("Initial Catalog=Db");
    }

    /// <summary>SQL 認証で接続文字列にユーザー ID とパスワードが含まれることを検証する</summary>
    [Fact(DisplayName = "SQL 認証で User ID/Password が含まれる")]
    public void Build_SqlServer_IncludesCredentials()
    {
        var s = new SqlConnectionSettings
        {
            Server = "srv",
            Database = "Db",
            AuthMode = SqlAuthMode.SqlServer,
            UserId = "sa",
            Password = "p@ss",
        };

        var cs = s.Build();
        cs.Should().Contain("User ID=sa");
        cs.Should().Contain("p@ss");
    }

    /// <summary>Microsoft Entra ID でユーザー ID 未指定なら Default 認証になることを検証する</summary>
    [Fact(DisplayName = "Microsoft Entra ID (UserId 空) で Default 認証になる")]
    public void Build_EntraId_Default()
    {
        var s = new SqlConnectionSettings
        {
            Server = "srv",
            Database = "Db",
            AuthMode = SqlAuthMode.EntraId,
        };

        var cs = s.Build();
        cs.Should().Contain("Authentication=ActiveDirectoryDefault");
    }

    /// <summary>Microsoft Entra ID でユーザー ID 指定時は Interactive 認証になることを検証する</summary>
    [Fact(DisplayName = "Microsoft Entra ID (UserId あり) で Interactive 認証になる")]
    public void Build_EntraId_Interactive()
    {
        var s = new SqlConnectionSettings
        {
            Server = "srv",
            Database = "Db",
            AuthMode = SqlAuthMode.EntraId,
            UserId = "user@contoso.com",
        };

        var cs = s.Build();

        cs.Should().Contain("Authentication=ActiveDirectoryInteractive");
        cs.Should().Contain("User ID=user@contoso.com");
    }

    /// <summary>
    /// <see cref="SqlConnectionSettings.Build"/> の結果が、GUI・CLI の接続が使う
    /// <see cref="SqlServerConnectionStringFactory.Build"/> と同じ接続文字列になることを検証する（組み立ての写しを持たない）。
    /// </summary>
    [Theory(DisplayName = "Build はどの認証方式でも共通の接続文字列ファクトリと同じ文字列を返す")]
    [InlineData(SqlAuthMode.Windows, "")]
    [InlineData(SqlAuthMode.SqlServer, "sa")]
    [InlineData(SqlAuthMode.EntraId, "")]
    [InlineData(SqlAuthMode.EntraId, "user@contoso.com")]
    public void Build_MatchesConnectionStringFactory(SqlAuthMode authMode, string userId)
    {
        var s = new SqlConnectionSettings
        {
            Server = "srv,14330",
            Database = "Db",
            AuthMode = authMode,
            UserId = userId,
            Password = "p@ss",
            TrustServerCertificate = false,
            ConnectTimeoutSeconds = 7,
        };

        s.Build().Should().Be(SqlServerConnectionStringFactory.Build(s.ToDbConnectionSettings()));
    }
}
