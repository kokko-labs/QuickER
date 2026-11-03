using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using QuickER.Tests.GeneratedSyncFixture;
using QuickER.Tests.GeneratedSyncFixture.Repositories.Sqlite;
using QuickER.Tests.GeneratedSyncFixture.Repositories.SqlServer;
using QuickER.Tests.Integration;
using Xunit;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// 生成した同期支援を、実 SQL Server（サーバー）と実ファイル SQLite（ローカル）の間で動かす統合テスト。
/// </summary>
/// <remarks>
/// <para>
/// ここに残すのは、SyncFixture にしか無い表（NULL を許す一意制約の <c>sync_markers</c>）を使うものと、時間を出力するだけの
/// 計測の 2 つ。本物の <c>rowversion</c> との噛み合わせ（DB が採番する版・<c>MIN_ACTIVE_ROWVERSION()</c> による上限・版ガードの
/// 競合・外部キーエラーの分類・版なしの表の TOP のページング）は、同期のモデルの決定的なシナリオのうち実 SQL Server の転送でも
/// 流すもの（<see cref="SyncModelSqlServerRuntimeTests"/>）が期待の手で表明し、DI 登録糖衣 <c>AddGeneratedSyncSupport</c> の
/// 組み上がりは同期の構成の行列（<see cref="SyncConfigMatrixRuntimeTests"/>）が見る。
/// </para>
/// </remarks>
[Trait("Category", "Integration")]
[Collection(SqlServerContainerCollection.Name)]
[Trait("RequiresDocker", "true")]
public sealed class SyncSqlServerRuntimeTests(
    SqlServerContainerFixture fixture,
    ITestOutputHelper output
) : IAsyncLifetime
{
    private readonly SqlServerContainerFixture _fixture = fixture;
    private readonly SqliteTempDatabase _sqlite = SqliteTempDatabase.Create();
    private ServiceProvider _provider = null!;

    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    /// <summary>サーバー側キー（SQL Server）</summary>
    private const string ServerKey = "server";

    /// <summary>ローカル側キー（SQLite）</summary>
    private const string LocalKey = "local";

    /// <summary>サーバーは元の図（rowversion）、ローカルは方言変換した図（BLOB）でスキーマを作り DI を組む</summary>
    public async ValueTask InitializeAsync()
    {
        Assert.SkipUnless(_fixture.IsAvailable, _fixture.UnavailableReason);

        await _fixture.ResetSchemaAsync(Ct);
        await _fixture.ApplyDdlAsync(SyncFixtureDefinition.Build(), Ct);
        await _sqlite.ApplyDdlAsync(SyncFixtureDefinition.BuildSqliteMirror(), Ct);

        var services = new ServiceCollection();
        services.AddGeneratedSqlServerRepositories(ServerKey, _fixture.ConnectionString);
        services.AddGeneratedSqliteRepositories(LocalKey, _sqlite.ReadWriteCreateConnectionString);
        services.AddGeneratedSyncSupport(ServerKey, LocalKey);
        _provider = services.BuildServiceProvider();
    }

    /// <summary>DI コンテナと一時 DB を破棄する</summary>
    public ValueTask DisposeAsync()
    {
        _provider?.Dispose();
        _sqlite.Dispose();

        return ValueTask.CompletedTask;
    }

    /// <summary>サーバー側（SQL Server）の注文リポジトリ</summary>
    private ISyncOrderRepository ServerOrders() =>
        _provider.GetRequiredKeyedService<ISyncOrderRepository>(ServerKey);

    /// <summary>同期エンジン</summary>
    private SyncEngine Engine() => _provider.GetRequiredService<SyncEngine>();

    /// <summary>ジャーナル</summary>
    private SyncJournal Journal() => _provider.GetRequiredService<SyncJournal>();

    /// <summary>
    /// 実 SQL Server を相手にした洗い替えと全量ダウンロードの所要時間を 1 回ずつ測る（表明は同値性だけ）。
    /// </summary>
    /// <remarks>
    /// SQLite 同士の計測（<see cref="SyncRefreshBenchmarkRuntimeTests"/>）と同じ方針で、時間は
    /// <see cref="ITestOutputHelper"/> へ出すだけにする。行数は環境変数
    /// <c>QUICKER_SYNC_BENCH_ORDERS</c> で上げられる（既定 1,000 件＝合計 2,000 行）。
    /// </remarks>
    [Fact(
        DisplayName = "[Sync/SqlServer] 洗い替えと全量ダウンロードは同じローカル内容を作る（所要時間は出力のみ）"
    )]
    public async Task Refresh_AgainstRealSqlServer_MatchesFullDownloadContent()
    {
        var orderCount = BenchmarkOrderCount;
        await SeedServerForBenchmarkAsync(orderCount);

        var batchSize = BenchmarkBatchSize;
        var download = await MeasureAgainstFreshLocalAsync(engine =>
            engine.DownloadAsync(
                new SyncOptions { DownloadBatchSize = batchSize, PropagateDeletes = false },
                Ct
            )
        );
        var refresh = await MeasureAgainstFreshLocalAsync(engine =>
            engine.RefreshAsync(
                new SyncRefreshOptions
                {
                    BatchSize = batchSize,
                    DiscardLocalUnboundedBinaries = true,
                },
                Ct
            )
        );

        output.WriteLine(
            $"データセット: 注文 {orderCount:N0} 件 ＋ 明細 {orderCount:N0} 件 = {orderCount * 2:N0} 行"
                + $"（バッチ {batchSize:N0}・サーバー＝実 SQL Server）"
        );
        output.WriteLine($"DownloadAsync {download.Elapsed.TotalMilliseconds, 9:F1} ms");
        output.WriteLine($"RefreshAsync  {refresh.Elapsed.TotalMilliseconds, 9:F1} ms");
        output.WriteLine($"倍率: DownloadAsync/Refresh = {download.Elapsed / refresh.Elapsed:F2}x");

        refresh
            .Digest.Should()
            .Be(download.Digest, "経路が違っても取り込む内容は同じでなければならない");
    }

    /// <summary>ベンチマークの注文件数（明細も同数）</summary>
    private static int BenchmarkOrderCount => Configured("QUICKER_SYNC_BENCH_ORDERS", 1_000);

    /// <summary>ベンチマークのバッチサイズ（両経路とも同じ値で回す）</summary>
    private static int BenchmarkBatchSize => Configured("QUICKER_SYNC_BENCH_BATCH", 500);

    /// <summary>環境変数の正の整数、無ければ既定値</summary>
    private static int Configured(string variable, int fallback) =>
        int.TryParse(Environment.GetEnvironmentVariable(variable), out var value) && value > 0
            ? value
            : fallback;

    /// <summary>ベンチマーク用にサーバー（SQL Server）を埋める（版は DB が採番する）</summary>
    private async Task SeedServerForBenchmarkAsync(int orderCount)
    {
        await ServerOrders()
            .BulkInsertAsync(
                Enumerable
                    .Range(1, orderCount)
                    .Select(id => new SyncOrderEntity
                    {
                        OrderId = id,
                        CustomerName = $"customer-{id:D6}",
                    })
                    .ToList(),
                Ct
            );
        await _provider
            .GetRequiredKeyedService<ISyncOrderLineRepository>(ServerKey)
            .BulkInsertAsync(
                Enumerable
                    .Range(1, orderCount)
                    .Select(id => new SyncOrderLineEntity
                    {
                        LineId = id,
                        OrderId = id,
                        Product = $"product-{id:D6}",
                    })
                    .ToList(),
                Ct
            );
    }

    /// <summary>まっさらなローカル SQLite を作り、渡された経路を 1 回計測してローカルの内容を要約する</summary>
    private async Task<(TimeSpan Elapsed, string Digest)> MeasureAgainstFreshLocalAsync(
        Func<SyncEngine, Task> run
    )
    {
        using var local = SqliteTempDatabase.Create();
        await local.ApplyDdlAsync(SyncFixtureDefinition.BuildSqliteMirror(), Ct);

        var localFactory =
            new QuickER.Tests.GeneratedSyncFixture.Repositories.Sqlite.SqlConnectionFactory(
                local.ReadWriteCreateConnectionString
            );
        var localSql = new QuickER.Tests.GeneratedSyncFixture.Repositories.Sqlite.SqlExecutor(
            localFactory
        );
        var journal = new SyncJournal(localSql);
        await journal.EnsureCreatedAsync(Ct);

        var orders = new JournalingSyncOrderRepository(
            new QuickER.Tests.GeneratedSyncFixture.Repositories.Sqlite.SyncOrderRepository(
                localFactory
            ),
            journal
        );
        var lines = new JournalingSyncOrderLineRepository(
            new QuickER.Tests.GeneratedSyncFixture.Repositories.Sqlite.SyncOrderLineRepository(
                localFactory
            ),
            journal
        );

        // 差分ソースは生成された直結実装そのもの（サーバー＝実 SQL Server）
        var engine = new SyncEngine(
            [
                new SyncTable<SyncOrderEntity, int>(
                    orders,
                    localSql,
                    new DirectSyncSource<SyncOrderEntity, int>(
                        _provider.GetRequiredKeyedService<ISqlExecutor>(ServerKey),
                        ServerOrders(),
                        GeneratedSyncTables.SyncOrder
                    ),
                    GeneratedSyncTables.SyncOrder
                ),
                new SyncTable<SyncOrderLineEntity, int>(
                    lines,
                    localSql,
                    new DirectSyncSource<SyncOrderLineEntity, int>(
                        _provider.GetRequiredKeyedService<ISqlExecutor>(ServerKey),
                        _provider.GetRequiredKeyedService<ISyncOrderLineRepository>(ServerKey),
                        GeneratedSyncTables.SyncOrderLine
                    ),
                    GeneratedSyncTables.SyncOrderLine
                ),
            ],
            journal
        );

        var stopwatch = Stopwatch.StartNew();
        await run(engine);
        stopwatch.Stop();

        var orderRows = await orders.GetAllAsync(Ct);
        var lineRows = await lines.GetAllAsync(Ct);
        var digest =
            string.Join(
                "|",
                orderRows
                    .OrderBy(order => order.OrderId)
                    .Select(order =>
                        $"{order.OrderId}:{order.CustomerName}:{Convert.ToHexString(order.RowVer ?? [])}"
                    )
            )
            + "#"
            + string.Join(
                "|",
                lineRows
                    .OrderBy(line => line.LineId)
                    .Select(line =>
                        $"{line.LineId}:{line.OrderId}:{line.Product}:{Convert.ToHexString(line.RowVer ?? [])}"
                    )
            );

        return (stopwatch.Elapsed, digest);
    }

    /// <summary>サーバー側（SQL Server）のマーカーリポジトリ</summary>
    private ISyncMarkerRepository ServerMarkers() =>
        _provider.GetRequiredKeyedService<ISyncMarkerRepository>(ServerKey);

    /// <summary>ローカル側（SQLite）のマーカーリポジトリ＝ジャーナル記録デコレータが被さっている</summary>
    private ISyncMarkerRepository LocalMarkers() =>
        _provider.GetRequiredKeyedService<ISyncMarkerRepository>(LocalKey);

    /// <summary>
    /// サーバーに NULL の値の行があるとき、ローカルで作った 2 行目の NULL の行は、Collect・LocalWins では
    /// UniqueValueTakenOnServer として報告されて記録が残り、ServerWins では決着する（どのポリシー・モードでも止まらない）。
    /// </summary>
    /// <remarks>
    /// SQL Server の UNIQUE 制約は NULL を値として比べ、同じ組の 2 行目の NULL を断る（エラー 2627）。サーバーの事前チェック
    /// （QuickER 版 Repository の <c>CheckUniquenessAsync</c>）が NULL を照合から外していると、エンジンは断られた書き込みの
    /// 理由を見つけられずに元の例外を投げ直し、同期が毎回止まる。単一列の制約と、NULL を含む複合の制約の両方で確かめる。
    /// </remarks>
    [Theory(
        DisplayName = "[Sync/SqlServer] NULL の一意な値がサーバーで埋まっていると、どのポリシーとモードでも同期は止まらない"
    )]
    [InlineData(SyncConflictPolicy.Collect, SyncMode.Versioned, false)]
    [InlineData(SyncConflictPolicy.LocalWins, SyncMode.Versioned, false)]
    [InlineData(SyncConflictPolicy.ServerWins, SyncMode.Versioned, false)]
    [InlineData(SyncConflictPolicy.Collect, SyncMode.LastWriteWins, false)]
    [InlineData(SyncConflictPolicy.LocalWins, SyncMode.LastWriteWins, false)]
    [InlineData(SyncConflictPolicy.ServerWins, SyncMode.LastWriteWins, false)]
    [InlineData(SyncConflictPolicy.Collect, SyncMode.Versioned, true)]
    [InlineData(SyncConflictPolicy.ServerWins, SyncMode.LastWriteWins, true)]
    public async Task NullUniqueValueTakenOnServer_IsReportedOrSettled(
        SyncConflictPolicy policy,
        SyncMode mode,
        bool composite
    )
    {
        await ServerMarkers().InsertAsync(NewMarker(1, composite), Ct);
        var options = new SyncOptions { ConflictPolicy = policy, Mode = mode };
        await Engine().SyncAsync(options, Ct);
        await LocalMarkers().InsertAsync(NewMarker(2, composite), Ct);

        var result = await Engine().SyncAsync(options, Ct);

        if (policy == SyncConflictPolicy.ServerWins)
        {
            result.Conflicts.Should().BeEmpty();
            (await Journal().CountPendingAsync(Ct)).Should().Be(0);
            (await LocalMarkers().GetAllAsync(Ct))
                .Select(marker => marker.MarkerId)
                .Should()
                .Equal(1);
        }
        else
        {
            result
                .Conflicts.Should()
                .ContainSingle()
                .Which.Should()
                .Match<SyncConflict>(conflict =>
                    conflict.TableName == "sync_markers"
                    && conflict.KeyText == "2"
                    && conflict.Reason == SyncConflictReason.UniqueValueTakenOnServer
                );
            (await Journal().ReadAllAsync(Ct))
                .Should()
                .ContainSingle(entry => entry.KeyText == "2");
            (await LocalMarkers().GetByIdAsync(2, Ct)).Should().NotBeNull();
        }

        (await ServerMarkers().GetAllAsync(Ct)).Select(marker => marker.MarkerId).Should().Equal(1);
    }

    /// <summary>
    /// SQL Server の QuickER 版 Repository の事前チェックは、UNIQUE 制約と同じく NULL を値として比べる
    /// （単一列の NULL・複合の制約の NULL を含む組・全部が NULL の組は重複、NULL 以外の値が違えば重複ではない）。
    /// </summary>
    [Fact(
        DisplayName = "[Sync/SqlServer] SQL Server の CheckUniquenessAsync は NULL を値として比べる（単一列・複合）"
    )]
    public async Task ServerCheckUniqueness_ComparesNullsAsValues()
    {
        await ServerMarkers()
            .InsertAsync(
                new SyncMarkerEntity
                {
                    MarkerId = 1,
                    GroupName = null,
                    Slot = 1,
                },
                Ct
            );
        await ServerMarkers()
            .InsertAsync(
                new SyncMarkerEntity
                {
                    MarkerId = 2,
                    Code = "a",
                    GroupName = "g",
                },
                Ct
            );

        async Task<string[]> Check(SyncMarkerEntity marker) =>
            (await ServerMarkers().CheckUniquenessAsync(marker, Ct))
                .Select(violation => violation.ConstraintName)
                .ToArray();

        // 行 1 の code は NULL＝NULL の code は重複・(NULL, 1) も重複
        (
            await Check(
                new SyncMarkerEntity
                {
                    MarkerId = 9,
                    Code = null,
                    GroupName = "x",
                    Slot = 9,
                }
            )
        )
            .Should()
            .Equal("UQ_sync_markers_code");
        (
            await Check(
                new SyncMarkerEntity
                {
                    MarkerId = 9,
                    Code = "b",
                    GroupName = null,
                    Slot = 1,
                }
            )
        ).Should().Equal("UQ_sync_markers_group_name_slot");

        // 行 2 の (g, NULL)＝NULL を含む組でも NULL 以外の列が一致すれば重複・違えば重複ではない
        (
            await Check(
                new SyncMarkerEntity
                {
                    MarkerId = 9,
                    Code = "b",
                    GroupName = "g",
                    Slot = null,
                }
            )
        )
            .Should()
            .Equal("UQ_sync_markers_group_name_slot");
        (
            await Check(
                new SyncMarkerEntity
                {
                    MarkerId = 9,
                    Code = "b",
                    GroupName = null,
                    Slot = 2,
                }
            )
        ).Should().BeEmpty();

        // 自分自身は除外する（行 1 を同じ値で確かめても重複にならない）
        (await Check((await ServerMarkers().GetByIdAsync(1, Ct))!))
            .Should()
            .BeEmpty();
    }

    /// <summary>マーカーを組み立てる（composite なら複合の制約だけがぶつかる値、そうでなければ code だけが NULL でぶつかる値）</summary>
    private static SyncMarkerEntity NewMarker(int id, bool composite) =>
        composite
            ? new SyncMarkerEntity
            {
                MarkerId = id,
                Code = $"c{id}",
                GroupName = null,
                Slot = 1,
            }
            : new SyncMarkerEntity
            {
                MarkerId = id,
                Code = null,
                GroupName = $"g{id}",
                Slot = id,
            };
}
