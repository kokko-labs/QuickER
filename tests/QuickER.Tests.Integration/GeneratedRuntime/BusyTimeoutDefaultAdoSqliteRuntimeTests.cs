using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.Data.Sqlite;
using QuickER.Tests.GeneratedSyncFixture.Repositories.Sqlite;
using QuickER.Tests.Integration;
using QuickER.Tests.TestSupport;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// SQLite 方言の生成 <c>SqlConnectionFactory</c> が、開くたびに SQLite の busy timeout を接続文字列の
/// <c>Default Timeout</c> へ合わせることを、実 SQLite（一時ファイル DB・Docker 不要）で検証する。
/// </summary>
/// <remarks>
/// <para>
/// Microsoft.Data.Sqlite はロックで待つやり直しを各文の最初の step でしか行わない。自動コミットの
/// <c>INSERT … RETURNING</c> は確定が後の step で起きるので、別の接続が読んでいると待たずに失敗する。busy timeout を
/// 当てると SQLite 自身がどの step でも待つ（待ちそのものは <c>RawSqlReturning_WaitsForReaderOnAnotherConnection</c>
/// が固定する）。
/// </para>
/// <para>
/// busy timeout は接続に残る設定で、プールへ戻った接続を次に使う経路にも持ち越される。開くたびに当てることで、
/// どの接続でも値が同じになる。
/// </para>
/// </remarks>
[Trait("Category", "Integration")]
[RuntimeCell(RuntimeFeatures.BusyTimeoutDefault, RuntimeTarget.AdoSqlite)]
public sealed class BusyTimeoutDefaultAdoSqliteRuntimeTests : IDisposable
{
    /// <summary>テスト全体で使うキャンセルトークン</summary>
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    /// <summary>各テストが開く一時ファイル DB</summary>
    private readonly SqliteTempDatabase _db = SqliteTempDatabase.Create();

    /// <summary>生成のファクトリが足す <c>Foreign Keys</c> の分も含め、このテストが使った接続のプールを解放してから消す</summary>
    public void Dispose()
    {
        foreach (var timeout in new int?[] { null, 0, 5 })
        {
            using var connection = new SqlConnectionFactory(
                ConnectionString(timeout)
            ).CreateConnection();
            SqliteConnection.ClearPool(connection);
        }

        _db.Dispose();
    }

    [Theory(
        DisplayName = "[SQLite busy] 開いた接続の busy timeout は Default Timeout に合わせる（0 は上限なし）"
    )]
    [InlineData(null, 30_000)]
    [InlineData(5, 5_000)]
    [InlineData(0, int.MaxValue)]
    public async Task OpenedConnection_HasBusyTimeoutFromDefaultTimeout(
        int? defaultTimeout,
        int expected
    )
    {
        await using var connection = new SqlConnectionFactory(
            ConnectionString(defaultTimeout)
        ).CreateConnection();
        await connection.OpenAsync(Ct);

        (await BusyTimeoutAsync(connection)).Should().Be(expected);
    }

    [Fact(DisplayName = "[SQLite busy] プールから戻った接続を開き直しても busy timeout を当て直す")]
    public async Task PooledConnection_GetsBusyTimeoutAgain()
    {
        var factory = new SqlConnectionFactory(ConnectionString(null));

        await using (var first = factory.CreateConnection())
        {
            await first.OpenAsync(Ct);
            await ExecuteAsync(first, "CREATE TEMP TABLE pooled_marker (x INTEGER)");
            await ExecuteAsync(first, "PRAGMA busy_timeout = 1");
        }

        await using var second = factory.CreateConnection();
        await second.OpenAsync(Ct);

        // 一時テーブルは接続ごとのもの＝見えれば、プールから同じ接続が戻っている
        (await TempTableExistsAsync(second))
            .Should()
            .BeTrue("プールから同じ接続が戻っている");
        (await BusyTimeoutAsync(second)).Should().Be(30_000, "開き直すたびに当て直す");
    }

    /// <summary>
    /// 生 SQL の実行器へ渡した <c>INSERT … RETURNING</c> も、別の接続の読み取りが終わるのを待ってから成功する。
    /// </summary>
    /// <remarks>
    /// 自動コミットの確定は最初の行を返した後の step で起きる。ロールバックジャーナルの確定には排他のロックが要るので、
    /// 別の接続が読み取り（共有のロック）を持っている間は確定できない。読み取りを持ったまま書き込みを始め、上限の時間まで
    /// 終わらない（待っている）ことと、読み取りを終えた後に成功することを見る。
    /// </remarks>
    [Fact(
        DisplayName = "[SQLite busy] 生 SQL の INSERT … RETURNING は、別の接続の読み取りの後に成功する"
    )]
    public async Task RawSqlReturning_WaitsForReaderOnAnotherConnection()
    {
        var factory = new SqlConnectionFactory(ConnectionString(null));
        var sql = new SqlExecutor(factory);
        await sql.ExecuteSqlAsync(
            "CREATE TABLE busy_probe (id INTEGER PRIMARY KEY, name TEXT NOT NULL)",
            null,
            Ct
        );

        await using var reader = new SqliteConnection(_db.ReadWriteCreateConnectionString);
        await reader.OpenAsync(Ct);
        var readTransaction = reader.BeginTransaction(deferred: true);

        await using (var read = new SqliteCommand("SELECT count(*) FROM busy_probe", reader))
        {
            read.Transaction = readTransaction;
            await read.ExecuteScalarAsync(Ct);
        }

        // SQLite の待ちは呼び出したスレッドを止める（プロバイダの処理は同期）ので、書き込みは別のタスクで始める
        var insert = Task.Run(
            () =>
                sql.QueryProjectionBySqlAsync<long>(
                    "INSERT INTO busy_probe (id, name) VALUES (7, 'raw') RETURNING id",
                    null,
                    Ct
                ),
            Ct
        );
        var first = await Task.WhenAny(insert, Task.Delay(TimeSpan.FromSeconds(2), Ct));

        first.Should().NotBeSameAs(insert, "読み取りが終わるまで書き込みは確定できず、待っている");

        readTransaction.Commit();
        readTransaction.Dispose();

        (await insert).Should().Equal(7L);
        (await sql.ExecuteScalarSqlAsync<long>("SELECT count(*) FROM busy_probe", null, Ct))
            .Should()
            .Be(1);
    }

    private string ConnectionString(int? defaultTimeout)
    {
        var builder = new SqliteConnectionStringBuilder(_db.ReadWriteCreateConnectionString);

        if (defaultTimeout is { } seconds)
        {
            builder.DefaultTimeout = seconds;
        }

        return builder.ConnectionString;
    }

    private static async Task<int> BusyTimeoutAsync(SqliteConnection connection)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA busy_timeout";

        return Convert.ToInt32(await command.ExecuteScalarAsync(Ct), CultureInfo.InvariantCulture);
    }

    private static async Task<bool> TempTableExistsAsync(SqliteConnection connection)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT count(*) FROM sqlite_temp_master WHERE type = 'table' AND name = 'pooled_marker'";

        return Convert.ToInt64(await command.ExecuteScalarAsync(Ct), CultureInfo.InvariantCulture)
            == 1;
    }

    private static async Task ExecuteAsync(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(Ct);
    }
}
