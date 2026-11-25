using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using QuickER.Tests.GeneratedSyncFixture;
using QuickER.Tests.GeneratedSyncFixture.Repositories.Sqlite;
using QuickER.Tests.GeneratedSyncFixture.Repositories.SqlServer;
using QuickER.Tests.Integration;
using QuickER.Tests.TestSupport;
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
/// <see cref="SyncSupportRemoteAdoSqliteHttpSourceRuntimeTests"/> が Docker 不在の CI でも常時通す。DI の組み上がり（HTTP 差分ソースと、開くたびの確認で
/// 包んだローカルの接続ファクトリ）と、初回の全件と 2 回目の差分（アンカー）は、同期の構成の行列（<see cref="SyncSupportConfigMatrixRuntimeTests"/>）の
/// HTTP の行が見る。ここに残すのは、上限の値そのものの往復・409 の競合・NULL の一意な値の 3 つ（構成の行列は競合と上限を表明しない）。
/// </para>
/// <para>
/// サーバープロセスの DI は実運用と同じ形＝リポジトリを素直に登録し、<c>AddGeneratedDirectSyncSources(null)</c> で
/// 差分ソースを載せ、<c>MapGeneratedRemoteEndpoints(RemoteAccess.AllowAnonymous)</c> を張るだけ。クライアントは
/// <c>AddGeneratedHttpSyncSources</c> ＋ 端末の SQLite で新しいエンジン（<see cref="SyncFixtureEngineDevice"/>・トリガーで記録する）を組む。
/// </para>
/// </remarks>
[Trait("Category", "Integration")]
[Collection(SqlServerContainerCollection.Name)]
[Trait("RequiresDocker", "true")]
[RuntimeCell(
    RuntimeFeatures.SyncSupport,
    RuntimeTarget.Remote,
    Server = RuntimeTarget.AdoSqlServer
)]
public sealed class SyncSupportRemoteAdoSqlServerFixtureRuntimeTests(
    SqlServerContainerFixture fixture
) : IAsyncLifetime
{
    private readonly SqlServerContainerFixture _fixture = fixture;

    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private InProcessRemoteServer _server = null!;
    private ServiceProvider _serverSideProvider = null!;
    private ServiceProvider _clientProvider = null!;

    /// <summary>サーバー側スキーマ（rowversion）を作り、Kestrel とクライアント DI（HTTP の差分ソース）を組む</summary>
    public async ValueTask InitializeAsync()
    {
        Assert.SkipUnless(_fixture.IsAvailable, _fixture.UnavailableReason);

        await _fixture.ResetSchemaAsync(Ct);
        await _fixture.ApplyDdlAsync(SyncFixtureDefinition.Build(), Ct);

        _server = await InProcessRemoteServer.StartAsync(
            services =>
            {
                services.AddGeneratedSqlServerRepositories(_fixture.ConnectionString);
                services.AddGeneratedDirectSyncSources(null);
            },
            app => app.MapGeneratedRemoteEndpoints(RemoteAccess.AllowAnonymous),
            Ct
        );

        // テストがサーバー側 DB を直接動かすための経路（「他者による更新」を作るのに使う）
        var serverSideServices = new ServiceCollection();
        serverSideServices.AddGeneratedSqlServerRepositories(_fixture.ConnectionString);
        _serverSideProvider = serverSideServices.BuildServiceProvider();

        var clientServices = new ServiceCollection();
        clientServices.AddGeneratedHttpSyncSources(_server.BaseAddress(RemotePaths.DefaultPrefix));
        _clientProvider = clientServices.BuildServiceProvider();
    }

    /// <summary>Kestrel・DI コンテナを破棄する</summary>
    public async ValueTask DisposeAsync()
    {
        _clientProvider?.Dispose();
        _serverSideProvider?.Dispose();

        if (_server is not null)
        {
            await _server.DisposeAsync();
        }
    }

    /// <summary>サーバー側（SQL Server）の注文リポジトリ＝テストが直接叩く</summary>
    private ISyncOrderRepository ServerOrders =>
        _serverSideProvider.GetRequiredService<ISyncOrderRepository>();

    /// <summary>HTTP の差分ソースで新しいエンジンの端末を作る</summary>
    private Task<SyncFixtureEngineDevice> CreateDeviceAsync() =>
        SyncFixtureEngineDevice.CreateAsync(SyncFixtureServerSources.Resolve(_clientProvider), Ct);

    /// <summary>HTTP 差分ソース（上限の取得を直接確かめるのに使う）</summary>
    private ISyncServerSource<SyncOrderEntity, int> OrderSource =>
        _clientProvider.GetRequiredService<ISyncServerSource<SyncOrderEntity, int>>();

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
    /// ローカル編集のアップロードは既存のリモートエンドポイントを通り、新しいサーバー版がミラーへ書き戻る。
    /// サーバーで先に更新された行は本物の版ガードが 409 として拒み、競合として収集される。
    /// </summary>
    [Fact(
        DisplayName = "[Sync/SqlServer/HTTP] アップロードで版が書き戻り、版ガードの競合は 409 経由で収集される"
    )]
    public async Task Upload_WritesBackVersionAndCollectsConflictOverHttp()
    {
        using var device = await CreateDeviceAsync();
        await ServerOrders.InsertAsync(
            new SyncOrderEntity { OrderId = 1, CustomerName = "alice" },
            Ct
        );
        await device.Engine.SyncAsync(null, Ct);

        var before = (await device.Orders.GetByIdAsync(1, Ct))!.RowVer;

        var edited = await device.Orders.GetByIdAsync(1, Ct);
        edited!.CustomerName = "alice-offline";
        await device.Orders.UpdateAsync(edited, cancellationToken: Ct);

        var uploaded = await device.Engine.SyncAsync(null, Ct);

        uploaded.Uploaded.Should().Be(1);
        uploaded.Conflicts.Should().BeEmpty();
        var stored = await ServerOrders.GetByIdAsync(1, Ct);
        stored!.CustomerName.Should().Be("alice-offline");
        var after = (await device.Orders.GetByIdAsync(1, Ct))!.RowVer;
        after!.SequenceEqual(before!).Should().BeFalse("サーバーが採番し直した版が端末へ入る");
        after.Should().Equal(stored.RowVer, "書き戻した版はサーバーの版そのもの");

        // ここから競合: 端末を編集し、サーバー側の版だけを Repository を経由せず進める
        var conflicting = await device.Orders.GetByIdAsync(1, Ct);
        conflicting!.CustomerName = "offline-edit";
        await device.Orders.UpdateAsync(conflicting, cancellationToken: Ct);

        await ServerOrders.ExecuteSqlAsync(
            "UPDATE [sync_orders] SET [customer_name] = @n WHERE [order_id] = 1;",
            new { n = "server-edit" },
            Ct
        );

        var result = await device.Engine.SyncAsync(null, Ct);

        var conflict = result.Conflicts.Should().ContainSingle().Subject;
        conflict.Reason.Should().Be(SyncConflictReason.ModifiedOnServer);
        ((SyncOrderEntity)conflict.ServerEntity!).CustomerName.Should().Be("server-edit");
        (await device.ReadUnsentAsync(Ct))
            .Should()
            .ContainSingle("解決していない変更は未送信のまま残る")
            .Which.KeyText.Should()
            .Be("1");
    }

    /// <summary>
    /// サーバーに NULL の値の行があるとき、ローカルで作った 2 行目の NULL の行は、HTTP 越しでも UniqueValueTakenOnServer として
    /// 報告されて記録が残り、同期は止まらない（サーバーの事前チェックのエンドポイントが SQL Server の規則で答える）。
    /// </summary>
    [Fact(
        DisplayName = "[Sync/SqlServer/HTTP] NULL の一意な値がサーバーで埋まっていると、HTTP 越しでも報告されて止まらない"
    )]
    public async Task NullUniqueValueTakenOnServer_IsReportedOverHttp()
    {
        using var device = await CreateDeviceAsync();
        var serverMarkers = _serverSideProvider.GetRequiredService<ISyncMarkerRepository>();
        await serverMarkers.InsertAsync(new SyncMarkerEntity { MarkerId = 1, Code = null }, Ct);
        await device.Engine.SyncAsync(null, Ct);
        await device.Markers.InsertAsync(new SyncMarkerEntity { MarkerId = 2, Code = null }, Ct);

        var result = await device.Engine.SyncAsync(null, Ct);

        result
            .Conflicts.Should()
            .ContainSingle()
            .Which.Reason.Should()
            .Be(SyncConflictReason.UniqueValueTakenOnServer);
        (await device.ReadUnsentAsync(Ct))
            .Should()
            .ContainSingle(row => row.Table == "sync_markers" && row.KeyText == "2");
        (await serverMarkers.GetAllAsync(Ct)).Select(marker => marker.MarkerId).Should().Equal(1);
    }
}
