using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using QuickER.Tests.GeneratedBackendParityFixture;
using QuickER.Tests.TestSupport;
using Xunit;

namespace QuickER.Tests.Integration;

/// <summary>
/// <see cref="SqliteLightJournal"/> の接続ファクトリとインターセプターが、開いた接続へジャーナルの設定を当て、
/// 生成された接続ファクトリの外部キー検査の既定を保つことを確かめる。
/// </summary>
/// <remarks>
/// 同期とバックエンドの一致のテストは、外部キー違反を例外として見る（親の削除の順序の誤りを黙った喪失でなく例外で
/// 表に出す）。包みが接続文字列の外部キーの既定を落とすと、それらのテストは違反を見逃したまま緑になる。
/// </remarks>
[Trait("Category", "Integration")]
public sealed class SqliteLightJournalTests : IDisposable
{
    private readonly SqliteTempDatabase _db = SqliteTempDatabase.Create();

    public void Dispose()
    {
        // 包みは接続文字列へ外部キーの指定を足すので、一時 DB の後始末が解放するプールとは別のプールになる
        using (
            var connection = new LightJournalSyncConnectionFactory(
                _db.ReadWriteCreateConnectionString
            ).CreateConnection()
        )
        {
            SqliteConnection.ClearPool(connection);
        }

        _db.Dispose();
    }

    public static IEnumerable<object[]> Factories() =>
        [
            ["SyncModel"],
            ["Sync"],
            ["BackendParity"],
        ];

    [Theory(
        DisplayName = "包んだ接続ファクトリは、開くたびに軽いジャーナルにし、外部キー検査を既定で有効にする"
    )]
    [MemberData(nameof(Factories))]
    public async Task Factory_AppliesJournalSettingsAndKeepsForeignKeys(string fixture)
    {
        var connectionString = _db.ReadWriteCreateConnectionString;
        Func<SqliteConnection> create = fixture switch
        {
            "SyncModel" => new LightJournalSyncModelConnectionFactory(
                connectionString
            ).CreateConnection,
            "Sync" => new LightJournalSyncConnectionFactory(connectionString).CreateConnection,
            "BackendParity" => new LightJournalBackendParityConnectionFactory(
                connectionString
            ).CreateConnection,
            _ => throw new ArgumentOutOfRangeException(nameof(fixture)),
        };

        // 2 回目はプールから戻った同じハンドルを開く（設定はハンドルに残るが、開くたびに当てる）
        for (var i = 0; i < 2; i++)
        {
            await using var connection = create();
            await connection.OpenAsync(TestContext.Current.CancellationToken);

            (await ReadAsync(connection, "journal_mode")).Should().Be("memory");
            (await ReadAsync(connection, "synchronous")).Should().Be("0");
            new SqliteConnectionStringBuilder(connection.ConnectionString)
                .ForeignKeys.Should()
                .BeTrue("生成された接続ファクトリの既定（Foreign Keys=True）を保つ");
            (await ReadAsync(connection, "foreign_keys")).Should().Be("1");
        }
    }

    [Fact(
        DisplayName = "軽いジャーナルの接続が開いている DB へ、既定のジャーナルの接続が書き込んでコミットできる"
    )]
    public async Task DefaultJournalWriter_CommitsWhileLightConnectionIsOpen()
    {
        var ct = TestContext.Current.CancellationToken;
        var connectionString = _db.ReadWriteCreateConnectionString;

        await using (var setup = new SqliteConnection(connectionString))
        {
            await setup.OpenAsync(ct);
            await ExecuteAsync(setup, "CREATE TABLE t (id INTEGER PRIMARY KEY, v TEXT);");
        }

        // 軽い接続で書き込みのトランザクションを 1 つ終え、接続は開いたままにする
        await using var light = new LightJournalSyncConnectionFactory(
            connectionString
        ).CreateConnection();
        await light.OpenAsync(ct);
        await using (var transaction = light.BeginTransaction())
        {
            await ExecuteAsync(light, "INSERT INTO t (id, v) VALUES (1, 'light');");
            transaction.Commit();
        }

        // 既定のジャーナル（DELETE）の接続は、コミットで自分のジャーナルのファイルを消す
        await using var plain = new SqliteConnection(connectionString);
        await plain.OpenAsync(ct);
        await using (var transaction = plain.BeginTransaction())
        {
            await ExecuteAsync(plain, "INSERT INTO t (id, v) VALUES (2, 'plain');");
            transaction.Commit();
        }

        await ExecuteAsync(light, "INSERT INTO t (id, v) VALUES (3, 'light again');");
        (await ReadAsync(plain, "journal_mode")).Should().Be("delete");
        await using var count = plain.CreateCommand();
        count.CommandText = "SELECT COUNT(*) FROM t;";
        Convert.ToInt64(await count.ExecuteScalarAsync(ct)).Should().Be(3);
    }

    [Fact(DisplayName = "EF Core のインターセプターは、EF Core が開いた接続を軽いジャーナルにする")]
    public async Task EfCoreInterceptor_AppliesJournalSettings()
    {
        var options = new DbContextOptionsBuilder<QuickErDbContext>()
            .UseSqlite(_db.ReadWriteCreateConnectionString)
            .IgnoreServiceProviderCacheLimit()
            .AddInterceptors(SqliteLightJournal.EfCoreInterceptor)
            .Options;
        await using var context = new QuickErDbContext(options);

        await context.Database.OpenConnectionAsync(TestContext.Current.CancellationToken);
        var connection = (SqliteConnection)context.Database.GetDbConnection();

        (await ReadAsync(connection, "journal_mode")).Should().Be("memory");
        (await ReadAsync(connection, "synchronous")).Should().Be("0");
    }

    private static async Task ExecuteAsync(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(CancellationToken.None);
    }

    private static async Task<string?> ReadAsync(SqliteConnection connection, string pragma)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA {pragma};";

        return Convert.ToString(
            await command.ExecuteScalarAsync(CancellationToken.None),
            System.Globalization.CultureInfo.InvariantCulture
        );
    }
}
