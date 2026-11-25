using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using QuickER.Tests.GeneratedSyncFixture;
using QuickER.Tests.Integration;
using QuickER.Tests.TestSupport;
using Xunit;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// 同期の差分ソースの <b>HTTP</b> の往復を確かめる（サーバー役を in-process Kestrel の向こうへ置く）。
/// </summary>
/// <remarks>
/// <para>
/// サーバー側は生成された <c>MapGeneratedRemoteEndpoints</c>（同期専用エンドポイントを含む）で、その背後に
/// SyncFixture の SQLite のサーバー役（<see cref="SyncFixtureTestServer"/>）の差分ソースを DI 登録する。クライアント側は生成された
/// <c>AddGeneratedHttpSyncSources</c> の <c>Http{Entity}SyncSource</c> だけを使い、アンカーの Base64 の往復・続きの有無・
/// 上限・全キー・400 の拒否を差分ソースの面で直接見る。同期のエンジン越しの HTTP の振る舞いは、同期のモデルの HTTP の転送
/// （<see cref="SyncModelHttpRuntimeTests"/>）が持つ。
/// </para>
/// <para>
/// アップロードが使う既存の CRUD／保存エンドポイントの面には、版を採番するサーバー役のリポジトリを登録する
/// （版なしのメモは採番なし＝素のリポジトリへ委譲するだけのアダプタ）。
/// </para>
/// </remarks>
[Trait("Category", "Integration")]
[RuntimeCell(RuntimeFeatures.SyncSupport, RuntimeTarget.Remote, Server = RuntimeTarget.AdoSqlite)]
public sealed class SyncSupportRemoteAdoSqliteHttpSourceRuntimeTests : IAsyncLifetime
{
    /// <summary>テスト全体で使うキャンセルトークン</summary>
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private SyncFixtureTestServer? _server;
    private InProcessRemoteServer? _remoteServer;
    private ServiceProvider? _clientProvider;

    /// <summary>クライアント側（HTTP）の注文差分ソース</summary>
    private ISyncServerSource<SyncOrderEntity, int> HttpOrderSource =>
        _clientProvider!.GetRequiredService<ISyncServerSource<SyncOrderEntity, int>>();

    /// <summary>サーバー役を作り、Kestrel の向こうへ差分ソースとリモート面を置き、クライアントの HTTP の差分ソースを組む</summary>
    public async ValueTask InitializeAsync()
    {
        var server = await SyncFixtureTestServer.CreateAsync(Ct);
        _server = server;

        _remoteServer = await InProcessRemoteServer.StartAsync(
            services =>
            {
                // 同期エンドポイントが答える実体＝サーバー役の差分ソース
                services.AddSingleton(server.Sources.Orders);
                services.AddSingleton(server.Sources.Lines);
                services.AddSingleton(server.Sources.Notes);
                services.AddSingleton(server.Sources.Markers);

                // アップロード（既存の CRUD／保存エンドポイント）が使う面。版採番ラッパーを噛ませる
                services.AddScoped<ISyncOrderRemoteRepository>(
                    _ => new SyncTestOrderRemoteRepository(
                        server.Orders,
                        server.OrdersRaw,
                        server.OrderBlobs
                    )
                );
                services.AddScoped<ISyncOrderLineRemoteRepository>(
                    _ => new SyncTestOrderLineRemoteRepository(server.Lines)
                );
                services.AddScoped<ISyncNoteRemoteRepository>(_ => new SyncTestNoteRemoteRepository(
                    server.Notes
                ));
            },
            app => app.MapGeneratedRemoteEndpoints(RemoteAccess.AllowAnonymous),
            Ct
        );

        _clientProvider = new ServiceCollection()
            .AddGeneratedHttpSyncSources(_remoteServer.BaseAddress(RemotePaths.DefaultPrefix))
            .BuildServiceProvider();
    }

    /// <summary>クライアント・Kestrel・サーバー役の一時 DB を破棄する</summary>
    public async ValueTask DisposeAsync()
    {
        _clientProvider?.Dispose();

        if (_remoteServer is not null)
        {
            await _remoteServer.DisposeAsync();
        }

        _server?.Dispose();
    }

    /// <summary>サーバー役へ注文とその明細を 1 件ずつ入れる（版はラッパーが採番する）</summary>
    private async Task SeedServerAsync(int orderId, string customer, int lineId, string product)
    {
        await _server!.Orders.InsertAsync(
            new SyncOrderEntity { OrderId = orderId, CustomerName = customer },
            Ct
        );
        await _server.Lines.InsertAsync(
            new SyncOrderLineEntity
            {
                LineId = lineId,
                OrderId = orderId,
                Product = product,
            },
            Ct
        );
    }

    /// <summary>
    /// アンカー（<c>byte[]</c>）が JSON の Base64 を往復し、サーバー側で版の比較に使われる。
    /// </summary>
    /// <remarks>
    /// 版はバイト列なので、転送で 1 バイトでも崩れれば「アンカーより新しい行」の判定が静かにずれる。
    /// ここでは 1 行目の版をアンカーとして明示的に渡し、それより後の行だけが返ることで往復を確かめる
    /// （エンジン経由では「同期が成立した」ことしか見えず、アンカーが素通しされていても気づけない）。
    /// </remarks>
    [Fact(DisplayName = "[Sync/HTTP] アンカー（byte[]）が往復し、それより新しい行だけが返る")]
    public async Task Anchor_RoundTripsAsBase64()
    {
        await SeedServerAsync(1, "alice", 11, "widget");
        await SeedServerAsync(2, "bob", 12, "gadget");
        await SeedServerAsync(3, "carol", 13, "doodad");

        var all = await HttpOrderSource.GetChangesAsync(null, null, 10, Ct);
        all.Rows.Should().HaveCount(3);
        all.HasMore.Should().BeFalse("10 件要求して 3 件なら続きはない");

        var firstVersion = all.Rows[0].RowVer;
        firstVersion.Should().NotBeNull();

        var rest = await HttpOrderSource.GetChangesAsync(firstVersion, null, 10, Ct);

        rest.Rows.Should().HaveCount(2, "アンカーが崩れていれば 3 件（素通し）か 0 件になる");
        rest.Rows.Select(row => row.OrderId).Should().Equal([2, 3]);
    }

    /// <summary>満杯のバッチには「続きがある」が立ち、応答を通してクライアントへ届く</summary>
    [Fact(DisplayName = "[Sync/HTTP] 満杯のバッチは HasMore=true が応答に載って返る")]
    public async Task FullBatch_ReportsHasMoreOverTheWire()
    {
        await SeedServerAsync(1, "alice", 11, "widget");
        await SeedServerAsync(2, "bob", 12, "gadget");

        var batch = await HttpOrderSource.GetChangesAsync(null, null, 1, Ct);

        batch.Rows.Should().ContainSingle();
        batch.HasMore.Should().BeTrue("バッチが満杯なら続きがあり得る＝継続の根拠になる");
    }

    /// <summary>上限（ceiling）の取得はサーバーの答えをそのまま返す（本テスト構成では上限なし＝null）</summary>
    [Fact(DisplayName = "[Sync/HTTP] 上限の取得は null（上限なし）も含めて往復する")]
    public async Task ChangeCeiling_RoundTrips()
    {
        var ceiling = await HttpOrderSource.GetChangeCeilingAsync(Ct);

        ceiling
            .Should()
            .BeNull(
                "サーバー役は SQLite で未コミット書き込みの概念を持たないため上限なし。"
                    + "実 SQL Server の MIN_ACTIVE_ROWVERSION() は SyncSupportRemoteAdoSqlServerFixtureRuntimeTests が見る"
            );
    }

    /// <summary>キー全比較の入力（キーのみ）が HTTP 越しに全件返る</summary>
    [Fact(DisplayName = "[Sync/HTTP] 全キー取得が HTTP 越しに全件返る")]
    public async Task AllKeys_AreReturnedOverHttp()
    {
        await SeedServerAsync(1, "alice", 11, "widget");
        await SeedServerAsync(2, "bob", 12, "gadget");

        var keys = await HttpOrderSource.GetAllKeysAsync(Ct);

        keys.Should().BeEquivalentTo([1, 2]);
    }

    /// <summary>
    /// 0 以下のバッチサイズはサーバーが 400 として拒否し、クライアントは既存の分類で例外化する。
    /// </summary>
    /// <remarks>
    /// 生成クライアント経由の同期では <c>SyncTable</c> が先に弾くため到達しないが、値は wire から来るので
    /// 手書きクライアントは送れてしまう。エンドポイントは「解釈できない要求」として 400 を返し、
    /// リモート面と同じ <see cref="RemoteRepositoryException"/> になる。
    /// </remarks>
    [Fact(DisplayName = "[Sync/HTTP] バッチサイズ 0 は 400 として拒否される")]
    public async Task NonPositiveBatchSize_IsRejectedAsBadRequest()
    {
        var act = async () => await HttpOrderSource.GetChangesAsync(null, null, 0, Ct);

        var exception = await act.Should().ThrowAsync<RemoteRepositoryException>();
        exception.Which.StatusCode.Should().Be(400);
    }

    /// <summary>
    /// サーバーが居なければ、同期はリモート面のどの操作とも同じ形（<see cref="HttpRequestException"/>）で失敗する。
    /// </summary>
    /// <remarks>
    /// 接続そのものが張れない失敗は応答を持たないため <see cref="RemoteError"/> による分類の対象外で、
    /// 生成クライアントは HttpClient の例外をそのまま通す（GetById 等と同じ挙動＝同期だけの特別扱いはない）。端末は新しいエンジン
    /// （<see cref="SyncFixtureEngineDevice"/>・HTTP の差分ソース）で、エンジンもその例外を包まずに伝える。
    /// </remarks>
    [Fact(
        DisplayName = "[Sync/HTTP] サーバー不在では HTTP 例外がそのまま伝わる（分類は既存と同じ）"
    )]
    public async Task ServerDown_SurfacesAsHttpRequestException()
    {
        using var device = await SyncFixtureEngineDevice.CreateAsync(
            SyncFixtureServerSources.Resolve(_clientProvider!),
            Ct
        );
        await _remoteServer!.StopAsync(Ct);

        var act = async () => await device.Engine.SyncAsync(null, Ct);

        await act.Should().ThrowAsync<HttpRequestException>();
    }
}
