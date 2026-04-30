using ERDesigner.Services;
using FluentAssertions;

namespace ERDesigner.Tests.Services;

/// <summary>
/// <see cref="SqlConnectionSettings.Build"/> が認証方式に応じた接続文字列を構築することを確認するテスト。
/// </summary>
public class SqlConnectionSettingsTests
{
    [Fact(DisplayName = "Windows 認証で IntegratedSecurity が有効になる")]
    public void Build_Windows_SetsIntegratedSecurity()
    {
        var s = new SqlConnectionSettings { Server = "localhost", Database = "Db", AuthMode = SqlAuthMode.Windows };
        var cs = s.Build();
        cs.Should().Contain("Integrated Security=True");
        cs.Should().Contain("Initial Catalog=Db");
    }

    [Fact(DisplayName = "SQL 認証で User ID/Password が含まれる")]
    public void Build_SqlServer_IncludesCredentials()
    {
        var s = new SqlConnectionSettings
        {
            Server = "srv", Database = "Db",
            AuthMode = SqlAuthMode.SqlServer,
            UserId = "sa", Password = "p@ss"
        };
        var cs = s.Build();
        cs.Should().Contain("User ID=sa");
        cs.Should().Contain("p@ss");
    }

    [Fact(DisplayName = "Azure AD (UserId 空) で Default 認証になる")]
    public void Build_AzureAd_Default()
    {
        var s = new SqlConnectionSettings { Server = "srv", Database = "Db", AuthMode = SqlAuthMode.AzureAd };
        var cs = s.Build();
        cs.Should().Contain("Authentication=ActiveDirectoryDefault");
    }

    [Fact(DisplayName = "Azure AD (UserId あり) で Password 認証になる")]
    public void Build_AzureAd_Password()
    {
        var s = new SqlConnectionSettings
        {
            Server = "srv", Database = "Db", AuthMode = SqlAuthMode.AzureAd,
            UserId = "user@contoso.com", Password = "pwd"
        };
        var cs = s.Build();
        cs.Should().Contain("Authentication=ActiveDirectoryPassword");
        cs.Should().Contain("User ID=user@contoso.com");
    }
}
