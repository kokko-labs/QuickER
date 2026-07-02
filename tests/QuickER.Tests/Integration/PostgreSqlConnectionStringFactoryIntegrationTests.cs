using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Npgsql;
using QuickER.PostgreSql;

namespace QuickER.Tests.Integration;

/// <summary>
/// D: <see cref="PostgreSqlConnectionStringFactory.Build"/> で共通接続設定から組み立てた接続文字列が、
/// 実コンテナへ接続できることを検証する統合テスト。
/// </summary>
[Trait("Category", "Integration")]
[Collection(PostgreSqlContainerCollection.Name)]
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
}
