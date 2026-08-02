using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Oracle.ManagedDataAccess.Client;
using QuickER.Oracle;
using QuickER.Tests.Integration;

namespace QuickER.Tests.Integration.Dialects;

/// <summary>
/// D: <see cref="OracleConnectionStringFactory.Build"/> で共通接続設定から組み立てた接続文字列が、
/// 実コンテナへ接続できることを検証する統合テスト。
/// </summary>
[Trait("Category", "Integration")]
[Collection(OracleContainerCollection.Name)]
[Trait("RequiresDocker", "true")]
public sealed class OracleConnectionStringFactoryIntegrationTests(OracleContainerFixture fixture)
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    /// <summary>
    /// コンテナの接続情報から組んだ <see cref="QuickER.Provider.DbConnectionSettings"/> から
    /// ファクトリで接続文字列を構築し、実際に接続・簡易クエリが成功することを検証する。
    /// </summary>
    [Fact(DisplayName = "[Integration] D: 接続文字列ファクトリの出力でコンテナへ実接続できる")]
    public async Task Build_ConnectsToContainer()
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.UnavailableReason);

        var settings = fixture.ToDbConnectionSettings();
        var connectionString = OracleConnectionStringFactory.Build(settings);

        await using var conn = new OracleConnection(connectionString);
        await conn.OpenAsync(Ct);

        await using var cmd = new OracleCommand("SELECT 1 FROM DUAL", conn);
        var scalar = await cmd.ExecuteScalarAsync(Ct);

        scalar.Should().NotBeNull();
        System.Convert.ToInt32(scalar).Should().Be(1);
        conn.State.Should().Be(System.Data.ConnectionState.Open);
    }
}
