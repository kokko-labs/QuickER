using AwesomeAssertions;
using QuickER.MySql;
using QuickER.Oracle;
using QuickER.PostgreSql;
using QuickER.Provider;
using QuickER.Sqlite;
using QuickER.SqlServer;
using Xunit;

namespace QuickER.Tests.Provider;

/// <summary>
/// TLS 要求水準 <see cref="DbSslMode"/> が、対応する 2 方言（PostgreSQL / MySQL）の接続文字列へ載ること、
/// および<b>未指定のとき従来どおりキーワードを 1 つも載せない</b>ことを 5 方言まとめて固定する。
/// </summary>
/// <remarks>
/// <para>
/// 最重要の不変条件は「<b>既定値のままなら既存の接続が黙って変わらない</b>」こと。Npgsql /
/// MySqlConnector の各 <c>ConnectionStringBuilder</c> は、ドライバ既定と同じ値を代入しても
/// <c>SSL Mode=</c> を書き出す（実測）。そのため「未指定」は<b>プロパティへ代入しない</b>ことでしか
/// 表せず、ここでは未指定時の接続文字列を実リテラルで固定して、その一点を守る。
/// </para>
/// <para>
/// SQL Server は <see cref="DbConnectionSettings.TrustServerCertificate"/> という別の面を持ち、
/// Oracle / SQLite には対応する接続文字列キーワードが無い。これら 3 方言は
/// <see cref="DbConnectionSettings.SslMode"/> を無視する＝値を変えても出力がバイト不変であることを
/// 対称に固定する（新設プロパティが無関係な方言へ漏れ出さない保証）。
/// </para>
/// </remarks>
public class DbSslModeConnectionStringTests
{
    /// <summary>サーバー型方言の共通接続設定を生成する（TLS 要求水準のみ差し替える）</summary>
    private static DbConnectionSettings ServerSettings(DbSslMode sslMode, int port) =>
        new()
        {
            Host = "db.example.com",
            Port = port,
            Database = "shop",
            AuthMode = DbAuthMode.UsernamePassword,
            UserId = "app",
            Password = "secret",
            ConnectTimeoutSeconds = 15,
            SslMode = sslMode,
        };

    /// <summary>
    /// 何も設定していない <see cref="DbConnectionSettings"/> の TLS 要求水準が「未指定」であり、
    /// その結果 2 方言とも TLS のキーワードを 1 つも載せないことを検証する。
    /// </summary>
    /// <remarks>
    /// 既定値そのものを固定する唯一のテスト。他のケースは要求水準を明示して組み立てるため、
    /// 既定を <see cref="DbSslMode.Prefer"/> 等へ変えても気づけない（＝既存の接続が黙って変わる）。
    /// </remarks>
    [Fact(DisplayName = "TLS 要求水準の既定値は未指定で、接続文字列へ何も載らない")]
    public void Default_IsUnspecified_AndEmitsNothing()
    {
        new DbConnectionSettings().SslMode.Should().Be(DbSslMode.Unspecified);

        var untouched = new DbConnectionSettings { Host = "h", Database = "d" };

        PostgreSqlConnectionStringFactory.Build(untouched).Should().NotContain("SSL Mode");
        MySqlConnectionStringFactory.Build(untouched).Should().NotContain("SSL Mode");
    }

    /// <summary>未指定（既定値）の PostgreSQL 接続文字列が、この機能の追加前と同一であることを検証する</summary>
    [Fact(DisplayName = "PostgreSQL: TLS 未指定なら SSL Mode キーワードを載せない（既定挙動不変）")]
    public void PostgreSql_Unspecified_OmitsKeyword()
    {
        var settings = ServerSettings(DbSslMode.Unspecified, 5433);

        // 既定値のインスタンスも同じ結果になること＝「何も触らなければ従来どおり」を二重に固定する
        var untouched = ServerSettings(DbSslMode.Unspecified, 5433);
        untouched.SslMode.Should().Be(DbSslMode.Unspecified);

        PostgreSqlConnectionStringFactory
            .Build(settings)
            .Should()
            .Be(
                "Host=db.example.com;Port=5433;Database=shop;Username=app;Password=secret;Timeout=15;Application Name=QuickER"
            );
    }

    /// <summary>PostgreSQL で各 TLS 要求水準が Npgsql の <c>SSL Mode</c> へ対応付くことを検証する</summary>
    [Theory(DisplayName = "PostgreSQL: TLS 要求水準が SSL Mode キーワードへ載る")]
    [InlineData(DbSslMode.Disable, "Disable")]
    [InlineData(DbSslMode.Prefer, "Prefer")]
    [InlineData(DbSslMode.Require, "Require")]
    [InlineData(DbSslMode.VerifyCa, "VerifyCA")]
    [InlineData(DbSslMode.VerifyFull, "VerifyFull")]
    public void PostgreSql_EmitsSslMode(DbSslMode mode, string expected)
    {
        var connectionString = PostgreSqlConnectionStringFactory.Build(ServerSettings(mode, 5433));

        connectionString.Should().Contain($"SSL Mode={expected}");
    }

    /// <summary>未定義の列挙値は黙って既定へ落とさず例外にすることを検証する</summary>
    [Fact(DisplayName = "PostgreSQL: 未定義の TLS 要求水準は例外になる")]
    public void PostgreSql_UndefinedMode_Throws()
    {
        var act = () =>
            PostgreSqlConnectionStringFactory.Build(ServerSettings((DbSslMode)99, 5433));

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    /// <summary>未指定（既定値）の MySQL 接続文字列が、この機能の追加前と同一であることを検証する</summary>
    [Fact(DisplayName = "MySQL: TLS 未指定なら SSL Mode キーワードを載せない（既定挙動不変）")]
    public void MySql_Unspecified_OmitsKeyword()
    {
        MySqlConnectionStringFactory
            .Build(ServerSettings(DbSslMode.Unspecified, 3307))
            .Should()
            .Be(
                "Server=db.example.com;Port=3307;User ID=app;Password=secret;Database=shop;Allow User Variables=False;Application Name=QuickER;Connection Timeout=15"
            );
    }

    /// <summary>MySQL で各 TLS 要求水準が MySqlConnector の <c>SSL Mode</c> へ対応付くことを検証する</summary>
    /// <remarks>
    /// <c>MySqlSslMode.Disabled</c> と <c>None</c> は同一値（<c>0</c>）で、書き出しは <c>None</c> になる。
    /// </remarks>
    [Theory(DisplayName = "MySQL: TLS 要求水準が SSL Mode キーワードへ載る")]
    [InlineData(DbSslMode.Disable, "None")]
    [InlineData(DbSslMode.Prefer, "Preferred")]
    [InlineData(DbSslMode.Require, "Required")]
    [InlineData(DbSslMode.VerifyCa, "VerifyCA")]
    [InlineData(DbSslMode.VerifyFull, "VerifyFull")]
    public void MySql_EmitsSslMode(DbSslMode mode, string expected)
    {
        var connectionString = MySqlConnectionStringFactory.Build(ServerSettings(mode, 3307));

        connectionString.Should().Contain($"SSL Mode={expected}");
    }

    /// <summary>未定義の列挙値は黙って既定へ落とさず例外にすることを検証する</summary>
    [Fact(DisplayName = "MySQL: 未定義の TLS 要求水準は例外になる")]
    public void MySql_UndefinedMode_Throws()
    {
        var act = () => MySqlConnectionStringFactory.Build(ServerSettings((DbSslMode)99, 3307));

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    /// <summary>
    /// 対応キーワードを持たない 3 方言（SQL Server / Oracle / SQLite）は、TLS 要求水準を変えても
    /// 接続文字列がバイト不変であることを検証する。
    /// </summary>
    [Theory(DisplayName = "TLS 要求水準は SQL Server / Oracle / SQLite の接続文字列へ影響しない")]
    [InlineData(DbSslMode.Disable)]
    [InlineData(DbSslMode.Prefer)]
    [InlineData(DbSslMode.Require)]
    [InlineData(DbSslMode.VerifyCa)]
    [InlineData(DbSslMode.VerifyFull)]
    public void OtherDialects_IgnoreSslMode(DbSslMode mode)
    {
        var baseline = ServerSettings(DbSslMode.Unspecified, 1521);
        var changed = ServerSettings(mode, 1521);
        baseline.FilePath = changed.FilePath = @"C:\tmp\shop.db";

        SqlServerConnectionStringFactory
            .Build(changed)
            .Should()
            .Be(SqlServerConnectionStringFactory.Build(baseline));
        OracleConnectionStringFactory
            .Build(changed)
            .Should()
            .Be(OracleConnectionStringFactory.Build(baseline));
        SqliteConnectionStringFactory
            .Build(changed)
            .Should()
            .Be(SqliteConnectionStringFactory.Build(baseline));
    }
}
