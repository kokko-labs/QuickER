using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Oracle.ManagedDataAccess.Client;
using QuickER.Provider.Oracle;
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
    /// <remarks>
    /// あわせて、ファクトリの接続が ODP.NET の文のキャッシュを使わないことを、接続文字列（<c>Self Tuning=false</c>）と振る舞い
    /// （違う文を流した後もセッションの開いたカーソルが溜まらない）の両方で確かめる（src/QuickER.Provider.Oracle/CLAUDE.md「文のキャッシュを使わない」）
    /// </remarks>
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

        // 文のキャッシュを使わない：接続文字列の設定と、違う文を流した後のセッションの開いたカーソルの数
        new OracleConnectionStringBuilder(connectionString)
            .SelfTuning.Should()
            .BeFalse(
                "取込・同期の接続は 1 回きりの文しか流さず、キャッシュは上限の手前までカーソルを溜めるだけ"
            );

        for (var i = 0; i < DistinctStatements; i++)
        {
            await using var distinct = new OracleCommand(
                string.Create(CultureInfo.InvariantCulture, $"SELECT {i} FROM DUAL"),
                conn
            );
            await distinct.ExecuteScalarAsync(Ct);
        }

        string sid;

        await using (
            var sidCommand = new OracleCommand(
                "SELECT SYS_CONTEXT('USERENV','SID') FROM DUAL",
                conn
            )
        )
        {
            sid = System.Convert.ToString(
                await sidCommand.ExecuteScalarAsync(Ct),
                CultureInfo.InvariantCulture
            )!;
        }

        await using var sys = new OracleConnection(fixture.SystemConnectionString);
        await sys.OpenAsync(Ct);
        await using var cursors = new OracleCommand(
            """
            SELECT st.value
            FROM v$sesstat st
            JOIN v$statname nm ON nm.statistic# = st.statistic#
            WHERE nm.name = 'opened cursors current' AND st.sid = :sid
            """,
            sys
        );
        cursors.Parameters.Add(new OracleParameter("sid", sid));
        var open = System.Convert.ToInt64(
            await cursors.ExecuteScalarAsync(Ct),
            CultureInfo.InvariantCulture
        );

        open.Should()
            .BeLessThanOrEqualTo(
                MaxOpenCursorsWithoutCache,
                $"違う文を {DistinctStatements} 本流した後も、文のキャッシュを使わないセッションはカーソルを溜めない（キャッシュが効くと本数の分だけ開いたまま残る）"
            );
    }

    /// <summary>流す違う文の数</summary>
    private const int DistinctStatements = 60;

    /// <summary>キャッシュを使わないセッションの開いたカーソルの上限（実測は 10 前後の内側。キャッシュが効くと <see cref="DistinctStatements"/> を超える）</summary>
    private const int MaxOpenCursorsWithoutCache = 20;
}
