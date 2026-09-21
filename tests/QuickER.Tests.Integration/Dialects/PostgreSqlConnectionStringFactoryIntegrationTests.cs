using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Npgsql;
using QuickER.Provider;
using QuickER.Provider.PostgreSql;
using QuickER.Tests.Integration;

namespace QuickER.Tests.Integration.Dialects;

/// <summary>
/// D: <see cref="PostgreSqlConnectionStringFactory.Build"/> で共通接続設定から組み立てた接続文字列が、
/// 実コンテナへ接続できることを検証する統合テスト。
/// </summary>
[Trait("Category", "Integration")]
[Collection(PostgreSqlContainerCollection.Name)]
[Trait("RequiresDocker", "true")]
public sealed class PostgreSqlConnectionStringFactoryIntegrationTests(
    PostgreSqlContainerFixture fixture
)
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    /// <summary>
    /// コンテナの接続文字列を分解して組んだ <see cref="QuickER.Provider.DbConnectionSettings"/> から
    /// ファクトリで接続文字列を構築し、実際に接続・簡易クエリが成功することを検証する。
    /// </summary>
    [Fact(DisplayName = "[Integration] D: 接続文字列ファクトリの出力でコンテナへ実接続できる")]
    public async Task Build_ConnectsToContainer()
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.UnavailableReason);

        var settings = fixture.ToDbConnectionSettings();
        var connectionString = PostgreSqlConnectionStringFactory.Build(settings);

        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(Ct);

        await using var cmd = new NpgsqlCommand("SELECT 1;", conn);
        var scalar = await cmd.ExecuteScalarAsync(Ct);

        scalar.Should().Be(1);
        conn.State.Should().Be(System.Data.ConnectionState.Open);
    }

    /// <summary>
    /// TLS 要求水準が実際にドライバまで届いていることを、実コンテナへの接続可否の差で検証する。
    /// </summary>
    /// <remarks>
    /// テストコンテナは TLS を構成していない（構成していても証明書は自己署名）ため、
    /// <see cref="DbSslMode.VerifyFull"/> はどちらの構成でも必ず失敗する。一方
    /// <see cref="DbSslMode.Disable"/> は接続できる。この差が出ること自体が
    /// 「接続文字列へ載せたキーワードが無視されていない」ことの実 DB での証明になる
    /// （文字列生成だけの単体テストでは、キーワード名の綴り違い等を捕まえられない）。
    /// </remarks>
    [Fact(DisplayName = "[Integration] D: TLS 要求水準が実接続の可否を変える")]
    public async Task Build_SslMode_ReachesTheDriver()
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.UnavailableReason);

        var disabled = fixture.ToDbConnectionSettings();
        disabled.SslMode = DbSslMode.Disable;

        await using (
            var conn = new NpgsqlConnection(PostgreSqlConnectionStringFactory.Build(disabled))
        )
        {
            await conn.OpenAsync(Ct);
            conn.State.Should().Be(System.Data.ConnectionState.Open);
        }

        var verifyFull = fixture.ToDbConnectionSettings();
        verifyFull.SslMode = DbSslMode.VerifyFull;

        await using var strict = new NpgsqlConnection(
            PostgreSqlConnectionStringFactory.Build(verifyFull)
        );

        // 型はドライバ自身の例外まで絞る（実測: NpgsqlException "SSL connection requested. No SSL
        // enabled connection from this host is configured."）。素の Exception だと NullReference 等の
        // 無関係な失敗でも緑になる。なお「たまたま別の理由で失敗しただけ」でないことは、直前の
        // Disable が同じ設定オブジェクトから接続に成功していること＝差分が SSL Mode だけであることが示す
        await Assert.ThrowsAnyAsync<NpgsqlException>(() => strict.OpenAsync(Ct));
    }
}
