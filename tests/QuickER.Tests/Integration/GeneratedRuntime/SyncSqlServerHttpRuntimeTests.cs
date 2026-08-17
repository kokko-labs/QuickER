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
/// 生成した同期支援を「サーバー＝実 SQL Server を HTTP の向こう側・ローカル＝実ファイル SQLite」で動かす統合テスト。
/// </summary>
/// <remarks>
/// <para>
/// ここでしか確かめられないのは「<b>生成された</b>直結差分ソースが、<b>生成された</b>同期エンドポイント越しに
/// 同じ答えを返すか」＝本物の <c>rowversion</c>・<c>MIN_ACTIVE_ROWVERSION()</c> による上限・<c>OUTPUT INSERTED</c> で
/// 回収した版の書き戻しが、HTTP を挟んでも成立するか。エンジンの筋（バッチ継続・アンカー導出・削除伝搬・競合分類）は
/// <see cref="SyncHttpRuntimeTests"/> が Docker 不在の CI でも常時通す。
/// </para>
/// <para>
/// サーバープロセスの DI は実運用と同じ形＝リポジトリを素直に登録し、<c>AddGeneratedDirectSyncSources(null)</c> で
/// 差分ソースを載せ、<c>MapGeneratedRemoteEndpoints()</c> を張るだけ。クライアントは
/// <c>AddGeneratedHttpSyncSources</c> ＋ ローカル SQLite の keyed 登録で組む。
/// </para>
/// </remarks>
[Trait("Category", "Integration")]
[Collection(SqlServerContainerCollection.Name)]
[Trait("RequiresDocker", "true")]
public sealed class SyncSqlServerHttpRuntimeTests(SqlServerContainerFixture fixture)
    : IAsyncLifetime
{
    private readonly SqlServerContainerFixture _fixture = fixture;
    private readonly SqliteTempDatabase _sqlite = SqliteTempDatabase.Create();

    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    /// <summary>ローカル側キー（SQLite）</summary>
    private const string LocalKey = "local";

    private InProcessRemoteServer _server = null!;
    private ServiceProvider _serverSideProvider = null!;
    private ServiceProvider _clientProvider = null!;

    /// <summary>サーバー側スキーマ（rowversion）とローカル側スキーマ（BLOB）を作り、Kestrel とクライアント DI を組む</summary>
    public async ValueTask InitializeAsync()
    {
        Assert.SkipUnless(_fixture.IsAvailable, _fixture.UnavailableReason);

        await _fixture.ResetSchemaAsync(Ct);
        await _fixture.ApplyDdlAsync(SyncFixtureDefinition.Build(), Ct);
        await _sqlite.ApplyDdlAsync(SyncFixtureDefinition.BuildSqliteMirror(), Ct);

        _server = await InProcessRemoteServer.StartAsync(
            services =>
            {
                services.AddGeneratedSqlServerRepositories(_fixture.ConnectionString);
                services.AddGeneratedDirectSyncSources(null);
            },
            app => app.MapGeneratedRemoteEndpoints(),
            Ct
        );

        // テストがサーバー側 DB を直接動かすための経路（「他者による更新」を作るのに使う）
        var serverSideServices = new ServiceCollection();
        serverSideServices.AddGeneratedSqlServerRepositories(_fixture.ConnectionString);
        _serverSideProvider = serverSideServices.BuildServiceProvider();

        var clientServices = new ServiceCollection();
        clientServices.AddGeneratedSqliteRepositories(
            LocalKey,
            _sqlite.ReadWriteCreateConnectionString
        );
        clientServices.AddGeneratedHttpSyncSources(_server.BaseAddress(RemotePaths.DefaultPrefix));
        clientServices.AddGeneratedSyncEngine(LocalKey);
        _clientProvider = clientServices.BuildServiceProvider();
    }

    /// <summary>Kestrel・DI コンテナ・一時 DB を破棄する</summary>
    public async ValueTask DisposeAsync()
    {
        _clientProvider?.Dispose();
        _serverSideProvider?.Dispose();

        if (_server is not null)
        {
            await _server.DisposeAsync();
        }

        _sqlite.Dispose();
    }

    /// <summary>サーバー側（SQL Server）の注文リポジトリ＝テストが直接叩く</summary>
    private ISyncOrderRepository ServerOrders =>
        _serverSideProvider.GetRequiredService<ISyncOrderRepository>();

    /// <summary>ローカル側（SQLite）の注文リポジトリ＝ジャーナル記録デコレータが被さっている</summary>
    private ISyncOrderRepository LocalOrders =>
        _clientProvider.GetRequiredKeyedService<ISyncOrderRepository>(LocalKey);

    /// <summary>同期エンジン</summary>
    private SyncEngine Engine => _clientProvider.GetRequiredService<SyncEngine>();

    /// <summary>ジャーナル</summary>
    private SyncJournal Journal => _clientProvider.GetRequiredService<SyncJournal>();

    /// <summary>HTTP 差分ソース（上限の取得を直接確かめるのに使う）</summary>
    private ISyncServerSource<SyncOrderEntity, int> OrderSource =>
        _clientProvider.GetRequiredService<ISyncServerSource<SyncOrderEntity, int>>();

    /// <summary>DI 構成だけで転送経路が HTTP になる（ローカルのデコレータ差し替えは直結構成と同じ）</summary>
    [Fact(
        DisplayName = "[Sync/SqlServer/HTTP] DI は HTTP 差分ソース＋ローカルのジャーナルデコレータで組み上がる"
    )]
    public void Registration_UsesHttpSourceAndJournalingLocalRepositories()
    {
        OrderSource.Should().BeOfType<HttpSyncOrderSyncSource>();
        LocalOrders.Should().BeOfType<JournalingSyncOrderRepository>();
    }

    /// <summary>
    /// 実 <c>MIN_ACTIVE_ROWVERSION()</c> の値が HTTP 越しに（Base64 の byte[] として）そのまま届く。
    /// </summary>
    [Fact(DisplayName = "[Sync/SqlServer/HTTP] MIN_ACTIVE_ROWVERSION() が HTTP 越しに届く")]
    public async Task ChangeCeiling_TravelsOverHttp()
    {
        var ceiling = await OrderSource.GetChangeCeilingAsync(Ct);

        ceiling
            .Should()
            .NotBeNull(
                "SQL Server は常に「実行中トランザクションの最小版」を返す（上限なしにはならない）"
            );
        ceiling!.Length.Should().Be(8, "rowversion は 8 バイト＝転送で崩れていない");
    }

    /// <summary>
    /// 初回同期と差分同期が HTTP 越しに成立し、DB 採番の版がミラー列へ入って次回のアンカーになる。
    /// </summary>
    [Fact(
        DisplayName = "[Sync/SqlServer/HTTP] 初回全量と 2 回目の差分が HTTP 越しに成立する（本物の rowversion）"
    )]
    public async Task Sync_DownloadsAndResumesOverHttp()
    {
        await ServerOrders.InsertAsync(
            new SyncOrderEntity { OrderId = 1, CustomerName = "alice" },
            Ct
        );

        var first = await Engine.SyncAsync(cancellationToken: Ct);

        first.Downloaded.Should().Be(1);
        var local = await LocalOrders.GetByIdAsync(1, Ct);
        local!.RowVer.Should().NotBeNullOrEmpty("SQL Server が採番した版がそのままミラーされる");
        (await Journal.CountPendingAsync(Ct)).Should().Be(0, "適用はジャーナルへ記録されない");

        await ServerOrders.InsertAsync(
            new SyncOrderEntity { OrderId = 2, CustomerName = "bob" },
            Ct
        );

        var second = await Engine.SyncAsync(cancellationToken: Ct);

        second
            .Downloaded.Should()
            .Be(
                1,
                "アンカー（ミラー MAX）より下の行は再取得されない＝アンカーが正しく往復している"
            );
        (await LocalOrders.GetByIdAsync(2, Ct)).Should().NotBeNull();
    }

    /// <summary>
    /// ローカル編集のアップロードは既存のリモートエンドポイントを通り、新しいサーバー版がミラーへ書き戻る。
    /// サーバーで先に更新された行は本物の版ガードが 409 として拒み、競合として収集される。
    /// </summary>
    [Fact(
        DisplayName = "[Sync/SqlServer/HTTP] アップロードで版が書き戻り、版ガードの競合は 409 経由で収集される"
    )]
    public async Task Upload_WritesBackVersionAndCollectsConflictOverHttp()
    {
        await ServerOrders.InsertAsync(
            new SyncOrderEntity { OrderId = 1, CustomerName = "alice" },
            Ct
        );
        await Engine.SyncAsync(cancellationToken: Ct);

        var before = (await LocalOrders.GetByIdAsync(1, Ct))!.RowVer;

        var edited = await LocalOrders.GetByIdAsync(1, Ct);
        edited!.CustomerName = "alice-offline";
        await LocalOrders.UpdateAsync(edited, cancellationToken: Ct);

        var uploaded = await Engine.SyncAsync(cancellationToken: Ct);

        uploaded.Uploaded.Should().Be(1);
        uploaded.Conflicts.Should().BeEmpty();
        (await ServerOrders.GetByIdAsync(1, Ct))!.CustomerName.Should().Be("alice-offline");
        var after = (await LocalOrders.GetByIdAsync(1, Ct))!.RowVer;
        after!.SequenceEqual(before!).Should().BeFalse("サーバーが採番し直した版がミラーへ入る");

        // ここから競合: ローカルを編集し、サーバー側の版だけを Repository を経由せず進める
        var conflicting = await LocalOrders.GetByIdAsync(1, Ct);
        conflicting!.CustomerName = "offline-edit";
        await LocalOrders.UpdateAsync(conflicting, cancellationToken: Ct);

        await ServerOrders.ExecuteSqlAsync(
            "UPDATE [sync_orders] SET [customer_name] = @n WHERE [order_id] = 1;",
            new { n = "server-edit" },
            Ct
        );

        var result = await Engine.SyncAsync(cancellationToken: Ct);

        var conflict = result.Conflicts.Should().ContainSingle().Subject;
        conflict.Reason.Should().Be(SyncConflictReason.ModifiedOnServer);
        ((SyncOrderEntity)conflict.ServerEntity!).CustomerName.Should().Be("server-edit");
        (await Journal.CountPendingAsync(Ct))
            .Should()
            .Be(1, "解決していないエントリはジャーナルに残る");
    }
}
