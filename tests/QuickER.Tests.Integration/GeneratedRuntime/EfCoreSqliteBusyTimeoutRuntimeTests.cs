using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using QuickER.Tests.GeneratedQueryFixture;
using QuickER.Tests.TestSupport;
using Xunit;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// EF Core 版リポジトリを SQLite で使うとき、更新・削除・<c>RETURNING</c> つきの生 SQL が、別の接続の読み取りの
/// ロックで即座に失敗せず、接続の待ち時間まで待つことを、実 SQLite（一時ファイル DB・Docker 不要）で検証する。
/// </summary>
/// <remarks>
/// <para>
/// EF Core の SQLite プロバイダは更新と削除を <c>UPDATE … RETURNING 1</c> / <c>DELETE … RETURNING 1</c> で流す。
/// Microsoft.Data.Sqlite はロックで待つやり直しを各文の最初の step でしか行わず、自動コミットの文の確定は後の step で
/// 起きるので、別の接続が読み取りのロックを持っているだけで「database is locked」になる。生成の DI 登録は、開いた接続の
/// SQLite の busy timeout を待ち時間へ合わせる <c>SqliteBusyTimeoutInterceptor</c> を足す（QuickER 版の
/// <c>SqlConnectionFactory</c> と同じ規則＝<c>SqliteBusyTimeoutDefaultRuntimeTests</c>）。
/// </para>
/// <para>
/// 読み取りのロックは、別の接続で始めたトランザクションの中の <c>SELECT</c> が持ち、指定した時間の後に <c>COMMIT</c> で放す。
/// </para>
/// </remarks>
[Trait("Category", "Integration")]
public sealed class EfCoreSqliteBusyTimeoutRuntimeTests : IDisposable
{
    /// <summary>テスト全体で使うキャンセルトークン</summary>
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    /// <summary>待った後に通る操作で、読み取りのロックを持ち続ける時間</summary>
    private static readonly TimeSpan ShortLock = TimeSpan.FromMilliseconds(1000);

    /// <summary>待った証拠とみなす経過時間の下限（ロックを持つ時間より少し短く取る）</summary>
    private static readonly TimeSpan WaitedAtLeast = TimeSpan.FromMilliseconds(700);

    /// <summary>各テストが読み書きする一時ファイル DB</summary>
    private readonly SqliteTempDatabase _db = SqliteTempDatabase.Create();

    /// <summary>テストが組んだ DI コンテナ</summary>
    private readonly List<ServiceProvider> _providers = [];

    /// <summary>既定と違う接続文字列（このテストが使ったプール）</summary>
    private readonly List<string> _extraConnectionStrings = [];

    public void Dispose()
    {
        foreach (var provider in _providers)
        {
            provider.Dispose();
        }

        foreach (var connectionString in _extraConnectionStrings)
        {
            using var connection = new SqliteConnection(connectionString);
            SqliteConnection.ClearPool(connection);
        }

        _db.Dispose();
    }

    [Fact(
        DisplayName = "[EF Core SQLite busy] UpdateAsync は別の接続の読み取りのロックを待って通る"
    )]
    public async Task UpdateAsync_WaitsForReaderOnAnotherConnection()
    {
        var provider = Provider(_db.ReadWriteCreateConnectionString);
        await SeedAsync(provider);
        var repository = provider.GetRequiredService<IOrderRepository>();
        var order = (await repository.GetByIdAsync(OrderIdValue.Create(10), Ct))!;
        order.Memo = MemoValue.Create("updated");

        var elapsed = await WhileReadLockHeldAsync(
            ShortLock,
            async () =>
                (await repository.UpdateAsync(order, cancellationToken: Ct)).Should().BeTrue()
        );

        elapsed.Should().BeGreaterThanOrEqualTo(WaitedAtLeast, "ロックが放されるまで待つ");
        (await MemoAsync()).Should().Be("updated");
    }

    [Fact(
        DisplayName = "[EF Core SQLite busy] グラフ保存の更新は別の接続の読み取りのロックを待って通る"
    )]
    public async Task GraphSaveUpdate_WaitsForReaderOnAnotherConnection()
    {
        var provider = Provider(_db.ReadWriteCreateConnectionString);
        await SeedAsync(provider);
        var repository = provider.GetRequiredService<IOrderRepository>();
        var order = (await repository.GetByIdAsync(OrderIdValue.Create(10), Ct))!;
        order.Memo = MemoValue.Create("saved");
        order.MarkUpdated();

        var elapsed = await WhileReadLockHeldAsync(
            ShortLock,
            () => repository.SaveAsync(order, cancellationToken: Ct)
        );

        elapsed.Should().BeGreaterThanOrEqualTo(WaitedAtLeast, "ロックが放されるまで待つ");
        (await MemoAsync()).Should().Be("saved");
    }

    [Fact(
        DisplayName = "[EF Core SQLite busy] グラフ保存の削除は別の接続の読み取りのロックを待って通る"
    )]
    public async Task GraphSaveDelete_WaitsForReaderOnAnotherConnection()
    {
        var provider = Provider(_db.ReadWriteCreateConnectionString);
        await SeedAsync(provider);
        var repository = provider.GetRequiredService<IOrderRepository>();
        var order = (await repository.GetByIdAsync(OrderIdValue.Create(10), Ct))!;
        order.MarkRemoved();

        var elapsed = await WhileReadLockHeldAsync(
            ShortLock,
            () => repository.SaveAsync(order, cancellationToken: Ct)
        );

        elapsed.Should().BeGreaterThanOrEqualTo(WaitedAtLeast, "ロックが放されるまで待つ");
        (await repository.GetByIdAsync(OrderIdValue.Create(10), Ct)).Should().BeNull();
    }

    [Fact(
        DisplayName = "[EF Core SQLite busy] RETURNING つきの生 SQL は別の接続の読み取りのロックを待って通る"
    )]
    public async Task RawSqlReturning_WaitsForReaderOnAnotherConnection()
    {
        var provider = Provider(_db.ReadWriteCreateConnectionString);
        await SeedAsync(provider);
        var executor = provider.GetRequiredService<ISqlExecutor>();

        var elapsed = await WhileReadLockHeldAsync(
            ShortLock,
            () =>
                executor.ExecuteSqlAsync(
                    "UPDATE orders SET memo = 'raw' WHERE order_id = 10 RETURNING order_id",
                    cancellationToken: Ct
                )
        );

        elapsed.Should().BeGreaterThanOrEqualTo(WaitedAtLeast, "ロックが放されるまで待つ");
        (await MemoAsync()).Should().Be("raw");
    }

    /// <summary>
    /// 待つのは待ち時間まで：DbContext のコマンドタイムアウト（設定したとき）か、接続文字列の <c>Default Timeout</c>
    /// （設定しないとき）を 1 秒にすると、3 秒持つロックの前で、すぐにではなく 1 秒ほど待ってから失敗する。
    /// </summary>
    [Theory(
        DisplayName = "[EF Core SQLite busy] 待ち時間を過ぎたら失敗する（コマンドタイムアウト・Default Timeout）"
    )]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Update_FailsAfterTimeout(bool useCommandTimeout)
    {
        var connectionString = useCommandTimeout
            ? _db.ReadWriteCreateConnectionString
            : ConnectionStringWithDefaultTimeout(1);
        var provider = useCommandTimeout
            ? Provider(
                connectionString,
                options =>
                    options
                        .UseSqlite(connectionString, sqlite => sqlite.CommandTimeout(1))
                        .IgnoreServiceProviderCacheLimit()
            )
            : Provider(connectionString);
        await SeedAsync(provider);
        var repository = provider.GetRequiredService<IOrderRepository>();
        var order = (await repository.GetByIdAsync(OrderIdValue.Create(10), Ct))!;
        order.Memo = MemoValue.Create("late");
        Exception? thrown = null;

        var elapsed = await WhileReadLockHeldAsync(
            TimeSpan.FromSeconds(3),
            async () =>
            {
                try
                {
                    await repository.UpdateAsync(order, cancellationToken: Ct);
                }
                catch (Exception ex)
                {
                    thrown = ex;
                }
            }
        );

        thrown.Should().NotBeNull("ロックは待ち時間より長く続く");
        elapsed.Should().BeGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(700), "すぐには諦めない");
        elapsed.Should().BeLessThan(TimeSpan.FromMilliseconds(2500), "待ち時間を過ぎたら諦める");
    }

    [Fact(
        DisplayName = "[EF Core SQLite busy] 自分で組んだオプションに公開のインターセプターを足しても待つ"
    )]
    public async Task ManuallyBuiltOptions_WithInterceptor_Wait()
    {
        var provider = Provider(_db.ReadWriteCreateConnectionString);
        await SeedAsync(provider);
        var options = new DbContextOptionsBuilder<QuickErDbContext>()
            .UseSqlite(_db.ReadWriteCreateConnectionString)
            .IgnoreServiceProviderCacheLimit()
            .AddInterceptors(SqliteBusyTimeoutInterceptor.Instance)
            .Options;
        await using var context = new QuickErDbContext(options);
        var order = await context.Orders.SingleAsync(Ct);
        order.Memo = MemoValue.Create("manual");

        var elapsed = await WhileReadLockHeldAsync(ShortLock, () => context.SaveChangesAsync(Ct));

        elapsed.Should().BeGreaterThanOrEqualTo(WaitedAtLeast, "ロックが放されるまで待つ");
        (await MemoAsync()).Should().Be("manual");
    }

    [Fact(
        DisplayName = "[EF Core SQLite busy] configureDbContext で足した利用者のインターセプターも並んで動く"
    )]
    public async Task UserInterceptor_StillRuns()
    {
        var connectionString = _db.ReadWriteCreateConnectionString;
        var userInterceptor = new CountingConnectionInterceptor();
        var provider = Provider(
            connectionString,
            options =>
                options
                    .UseSqlite(connectionString)
                    .IgnoreServiceProviderCacheLimit()
                    .AddInterceptors(userInterceptor)
        );
        await SeedAsync(provider);
        var repository = provider.GetRequiredService<IOrderRepository>();
        var order = (await repository.GetByIdAsync(OrderIdValue.Create(10), Ct))!;
        order.Memo = MemoValue.Create("both");
        var openedBefore = userInterceptor.Opened;

        var elapsed = await WhileReadLockHeldAsync(
            ShortLock,
            async () =>
                (await repository.UpdateAsync(order, cancellationToken: Ct)).Should().BeTrue()
        );

        elapsed.Should().BeGreaterThanOrEqualTo(WaitedAtLeast, "ロックが放されるまで待つ");
        userInterceptor
            .Opened.Should()
            .BeGreaterThan(openedBefore, "利用者のインターセプターも呼ばれる");
        (await MemoAsync()).Should().Be("both");
    }

    /// <summary>生成の DI 登録で EF Core 版リポジトリを組む（既定は接続文字列だけを渡す）</summary>
    private ServiceProvider Provider(
        string connectionString,
        Action<DbContextOptionsBuilder>? configure = null
    )
    {
        var provider = new ServiceCollection()
            .AddGeneratedEfCoreRepositories(
                configure
                    ?? (
                        options =>
                            options.UseSqlite(connectionString).IgnoreServiceProviderCacheLimit()
                    )
            )
            .BuildServiceProvider();
        _providers.Add(provider);
        return provider;
    }

    /// <summary>スキーマを作り、顧客 1 件と注文 1 件を入れる（ロックの無いうちに）</summary>
    private async Task SeedAsync(ServiceProvider provider)
    {
        await _db.ResetSchemaAsync(Ct);
        await _db.ApplyDdlAsync(QueryFixtureDefinition.Build(), Ct);

        await provider
            .GetRequiredService<ICustomerRepository>()
            .InsertAsync(
                new CustomerEntity
                {
                    CustomerId = CustomerIdValue.Create(1),
                    Name = NameValue.Create("Alice"),
                },
                Ct
            );
        await provider
            .GetRequiredService<IOrderRepository>()
            .InsertAsync(
                new OrderEntity
                {
                    OrderId = OrderIdValue.Create(10),
                    CustomerId = CustomerIdValue.Create(1),
                    Amount = AmountValue.Create(10m),
                    Memo = MemoValue.Create("original"),
                },
                Ct
            );
    }

    /// <summary>
    /// 別の接続で読み取りのロックを取り、<paramref name="hold"/> の後に放す間に <paramref name="action"/> を流し、
    /// その所要時間を返す
    /// </summary>
    private async Task<TimeSpan> WhileReadLockHeldAsync(TimeSpan hold, Func<Task> action)
    {
        var reader = new SqliteConnection(_db.ReadWriteCreateConnectionString);
        await reader.OpenAsync(Ct);
        await ExecuteAsync(reader, "BEGIN");
        await ExecuteAsync(reader, "SELECT count(*) FROM orders");

        var release = Task.Run(
            async () =>
            {
                await Task.Delay(hold, Ct);
                await ExecuteAsync(reader, "COMMIT");
                await reader.DisposeAsync();
            },
            Ct
        );

        var stopwatch = Stopwatch.StartNew();

        try
        {
            await action();
        }
        finally
        {
            stopwatch.Stop();
            await release;
        }

        return stopwatch.Elapsed;
    }

    /// <summary>注文 10 のメモを、生成コードを通さずに読む</summary>
    private async Task<string?> MemoAsync()
    {
        await using var connection = new SqliteConnection(_db.ReadWriteCreateConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT memo FROM orders WHERE order_id = 10";

        return (string?)await command.ExecuteScalarAsync(Ct);
    }

    private string ConnectionStringWithDefaultTimeout(int seconds)
    {
        var connectionString = new SqliteConnectionStringBuilder(
            _db.ReadWriteCreateConnectionString
        )
        {
            DefaultTimeout = seconds,
        }.ConnectionString;
        _extraConnectionStrings.Add(connectionString);
        return connectionString;
    }

    private static async Task ExecuteAsync(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(Ct);
    }

    /// <summary>接続を開いた回数を数える利用者のインターセプター</summary>
    private sealed class CountingConnectionInterceptor : DbConnectionInterceptor
    {
        private int _opened;

        public int Opened => Volatile.Read(ref _opened);

        public override void ConnectionOpened(
            DbConnection connection,
            ConnectionEndEventData eventData
        ) => Interlocked.Increment(ref _opened);

        public override Task ConnectionOpenedAsync(
            DbConnection connection,
            ConnectionEndEventData eventData,
            CancellationToken cancellationToken = default
        )
        {
            Interlocked.Increment(ref _opened);
            return Task.CompletedTask;
        }
    }
}
