using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using MySqlConnector;
using QuickER.MySql;

namespace QuickER.Tests.Integration;

/// <summary>
/// D: <see cref="MySqlConnectionStringFactory.Build"/> で共通接続設定から組み立てた接続文字列が、
/// 実コンテナへ接続できることを検証する統合テスト。
/// </summary>
[Trait("Category", "Integration")]
[Collection(MySqlContainerCollection.Name)]
public sealed class MySqlConnectionStringFactoryIntegrationTests(MySqlContainerFixture fixture)
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
        var connectionString = MySqlConnectionStringFactory.Build(settings);

        await using var conn = new MySqlConnection(connectionString);
        await conn.OpenAsync(Ct);

        await using var cmd = new MySqlCommand("SELECT 1;", conn);
        var scalar = await cmd.ExecuteScalarAsync(Ct);

        System.Convert.ToInt32(scalar).Should().Be(1);
        conn.State.Should().Be(System.Data.ConnectionState.Open);
    }
}
