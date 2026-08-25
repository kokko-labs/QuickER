using System.Net.Http;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using QuickER.Tests.GeneratedSyncFixture;
using QuickER.Tests.Integration;
using Xunit;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// 同期支援のパリティスイートを <b>HTTP</b> の差分ソースで流す派生（サーバー役を in-process Kestrel の向こうへ置く）。
/// </summary>
/// <remarks>
/// <para>
/// サーバー側は生成された <c>MapGeneratedRemoteEndpoints</c>（同期専用エンドポイント 3 本を含む）で、その背後に
/// 直結テストとまったく同じ差分ソースを DI 登録する。クライアント側は生成された
/// <c>AddGeneratedHttpSyncSources</c> の <c>Http{Entity}SyncSource</c> だけを使う。したがって基底の全シナリオが
/// 「同じサーバー実体・同じ期待値」のまま HTTP 越しに走り、直結との差が出ればそれは転送の欠陥である。
/// </para>
/// <para>
/// アップロードは新設のエンドポイントではなく既存の CRUD／保存エンドポイントを通る（版の採番と版ガードは
/// サーバー側リポジトリが担う）。基底の競合シナリオが HTTP でも同じ結論になることが、409 の型復元まで含めた
/// 経路の検証になっている。
/// </para>
/// </remarks>
[Trait("Category", "Integration")]
public sealed class SyncHttpRuntimeTests : SyncRuntimeTestsBase
{
    private InProcessRemoteServer? _remoteServer;
    private ServiceProvider? _clientProvider;

    /// <summary>クライアント側（HTTP）の注文差分ソース</summary>
    private ISyncServerSource<SyncOrderEntity, int> HttpOrderSource =>
        _clientProvider!.GetRequiredService<ISyncServerSource<SyncOrderEntity, int>>();

    /// <inheritdoc />
    protected override async Task<(
        ISyncServerSource<SyncOrderEntity, int> Orders,
        ISyncServerSource<SyncOrderLineEntity, int> Lines,
        ISyncServerSource<SyncNoteEntity, int> Notes
    )> CreateServerSourcesAsync()
    {
        var serverOrderSource = CreateOrderTestSource();
        var serverLineSource = CreateLineTestSource();
        var serverNoteSource = CreateNoteTestSource();

        _remoteServer = await InProcessRemoteServer.StartAsync(
            services =>
            {
                // 同期エンドポイントが答える実体＝直結テストと同一の差分ソース
                services.AddSingleton<ISyncServerSource<SyncOrderEntity, int>>(serverOrderSource);
                services.AddSingleton<ISyncServerSource<SyncOrderLineEntity, int>>(
                    serverLineSource
                );
                services.AddSingleton<ISyncServerSource<SyncNoteEntity, int>>(serverNoteSource);

                // アップロード（既存の CRUD／保存エンドポイント）が使う面。版採番ラッパーを噛ませる
                // （版なしのメモは採番なし＝素のリポジトリへ委譲するだけのアダプタ）
                services.AddScoped<ISyncOrderRemoteRepository>(
                    _ => new SyncTestOrderRemoteRepository(ServerOrders, ServerOrderBlobs)
                );
                services.AddScoped<ISyncOrderLineRemoteRepository>(
                    _ => new SyncTestOrderLineRemoteRepository(ServerLines)
                );
                services.AddScoped<ISyncNoteRemoteRepository>(_ => new SyncTestNoteRemoteRepository(
                    ServerNotes
                ));
            },
            app => app.MapGeneratedRemoteEndpoints(RemoteAccess.AllowAnonymous),
            Ct
        );

        _clientProvider = new ServiceCollection()
            .AddGeneratedHttpSyncSources(_remoteServer.BaseAddress(RemotePaths.DefaultPrefix))
            .BuildServiceProvider();

        return (
            _clientProvider.GetRequiredService<ISyncServerSource<SyncOrderEntity, int>>(),
            _clientProvider.GetRequiredService<ISyncServerSource<SyncOrderLineEntity, int>>(),
            _clientProvider.GetRequiredService<ISyncServerSource<SyncNoteEntity, int>>()
        );
    }

    /// <inheritdoc />
    protected override async ValueTask DisposeTransportAsync()
    {
        _clientProvider?.Dispose();

        if (_remoteServer is not null)
        {
            await _remoteServer.DisposeAsync();
        }
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
                    + "実 SQL Server の MIN_ACTIVE_ROWVERSION() は SyncSqlServerHttpRuntimeTests が見る"
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
    /// 生成クライアントは HttpClient の例外をそのまま通す（GetById 等と同じ挙動＝同期だけの特別扱いはない）。
    /// </remarks>
    [Fact(
        DisplayName = "[Sync/HTTP] サーバー不在では HTTP 例外がそのまま伝わる（分類は既存と同じ）"
    )]
    public async Task ServerDown_SurfacesAsHttpRequestException()
    {
        await _remoteServer!.StopAsync(Ct);

        var act = async () => await Engine.SyncAsync(cancellationToken: Ct);

        await act.Should().ThrowAsync<HttpRequestException>();
    }
}
