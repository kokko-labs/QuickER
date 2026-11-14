using System;
using System.Net.Http;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// 楽観排他のランタイムスイートを<b>実 HTTP のリモート 3 階層</b>で流す派生の共通部
/// （Kestrel を 127.0.0.1 の空きポートで in-process 起動し、クライアントは生成された HTTP リモート実装だけを使う）。
/// </summary>
/// <remarks>
/// <para>
/// 基底 <see cref="ConcurrencyRuntimeTestsBase{TEntity, TConflictException}"/> のシナリオが緑になること自体が、
/// 「<c>ConcurrencyMode</c> がリクエストへ載る」「応答の版が手元のグラフへ書き戻る」「サーバーの競合が HTTP 409 経由で
/// 同じ型と内訳のまま復元される」の証明になる。
/// </para>
/// <para>
/// このクラスは加えて、版の書き戻しのシナリオ（基底の Fact 1・10・11 の本体）を<b>応答の経路</b>ごとに流す
/// （<see cref="RemoteResponseChannel"/>）。応答は版を版の表と列の値の 2 経路で運び、通常の応答ではどちらか片方を外しても
/// 答えが変わらないので、クライアントの HTTP に <see cref="RemoteResponseChannelHandler"/> を挟んで片方だけを読ませ、
/// どちらの経路も単独で版を届けることを確かめる。そのまま読む水準は基底の Fact が受け持つ。
/// </para>
/// <para>
/// 派生は、サーバーの DI（どの実装先のリポジトリを置くか）・エンドポイントのマップ・クライアントの登録と、
/// 基底のアダプタだけを差し込む（生成物は図ごとに別の名前空間へ出るため、生成の拡張メソッドは派生が呼ぶ）。
/// </para>
/// </remarks>
/// <typeparam name="TEntity">ルートエンティティ型（フィクスチャごとに別型）</typeparam>
/// <typeparam name="TConflictException">そのフィクスチャの <c>SaveConflictException</c> 型</typeparam>
public abstract class ConcurrencyRemoteRuntimeTestsBase<TEntity, TConflictException>
    : ConcurrencyRuntimeTestsBase<TEntity, TConflictException>,
        IAsyncLifetime
    where TEntity : class
    where TConflictException : Exception
{
    /// <summary>クライアントの応答から片方の経路を外すハンドラー（テストごとに 1 つ）</summary>
    private readonly RemoteResponseChannelHandler _channel = new();

    private InProcessRemoteServer? _server;
    private HttpClient? _httpClient;
    private ServiceProvider? _clientProvider;

    /// <summary>サーバーの起動の前に行う準備（実 DB の派生は Docker の有無を判定する）</summary>
    protected virtual void BeforeServerStart() { }

    /// <summary>サーバー側の DI に、リモート面を転送するリポジトリを登録する</summary>
    protected abstract void RegisterServerServices(IServiceCollection services);

    /// <summary>生成のリモートのエンドポイントをマップする（<c>MapGeneratedRemoteEndpoints</c>）</summary>
    protected abstract void MapRemoteEndpoints(WebApplication app);

    /// <summary>エンドポイントの前置き（<c>RemotePaths.DefaultPrefix</c>）</summary>
    protected abstract string RemotePathPrefix { get; }

    /// <summary>クライアント側の DI に、渡した HttpClient の生成で HTTP リモート実装を登録する（<c>AddGeneratedHttpRemoteRepositories</c> の HttpClient ファクトリ版）</summary>
    protected abstract void RegisterClientServices(
        IServiceCollection services,
        Func<IServiceProvider, HttpClient> httpClientFactory
    );

    /// <summary>サーバーの基底の URL（末尾の区切りなし・前置きを含まない）</summary>
    protected string BaseUrl => _server!.BaseUrl;

    /// <summary>クライアント側のサービスを解決する</summary>
    protected TService Client<TService>()
        where TService : notnull => _clientProvider!.GetRequiredService<TService>();

    /// <summary>Kestrel を起動し、応答の経路のハンドラーを挟んだ HTTP クライアントの DI を組み立てる</summary>
    public async ValueTask InitializeAsync()
    {
        BeforeServerStart();

        _server = await InProcessRemoteServer.StartAsync(
            RegisterServerServices,
            MapRemoteEndpoints,
            Ct
        );

        // 末尾の / は相対パス（"Document/Save" など）の解決に要る
        _httpClient = new HttpClient(_channel, disposeHandler: false)
        {
            BaseAddress = new Uri(_server.BaseAddress(RemotePathPrefix) + "/"),
        };

        var services = new ServiceCollection();
        var httpClient = _httpClient;
        RegisterClientServices(services, _ => httpClient);
        _clientProvider = services.BuildServiceProvider();
    }

    /// <summary>使い終えたクライアント DI・HTTP クライアント・サーバーを破棄する</summary>
    public async ValueTask DisposeAsync()
    {
        _clientProvider?.Dispose();
        _httpClient?.Dispose();
        _channel.Dispose();

        if (_server is not null)
        {
            await _server.DisposeAsync();
        }
    }

    // ── リモート固有: 応答の経路 ──

    /// <summary>
    /// 応答の片方の経路（版の表か列の値）だけを読ませても、版の書き戻しのシナリオがそのまま成り立つ
    /// （版の表だけ＝以前のサーバーの応答を新しいクライアントが読む形・列の値だけ＝列の値が版を運ぶこと）。
    /// </summary>
    [Theory(
        DisplayName = "[Concurrency/Remote] 応答の経路: 版の表と列の値のどちらか片方だけでも版が書き戻る"
    )]
    [InlineData(RemoteResponseChannel.VersionTableOnly, RowVersionWriteBack.SingleRow)]
    [InlineData(RemoteResponseChannel.VersionTableOnly, RowVersionWriteBack.Graph)]
    [InlineData(RemoteResponseChannel.VersionTableOnly, RowVersionWriteBack.ManyRoots)]
    [InlineData(RemoteResponseChannel.ValuesOnly, RowVersionWriteBack.SingleRow)]
    [InlineData(RemoteResponseChannel.ValuesOnly, RowVersionWriteBack.Graph)]
    [InlineData(RemoteResponseChannel.ValuesOnly, RowVersionWriteBack.ManyRoots)]
    public async Task RowVersion_IsWrittenBack_ThroughEachResponseChannelAlone(
        RemoteResponseChannel channel,
        RowVersionWriteBack scenario
    )
    {
        _channel.Channel = channel;

        await RunRowVersionWriteBackAsync(scenario);

        _channel
            .Rewritten.Should()
            .BeGreaterThan(
                0,
                "書き込みの応答から、読ませない経路の欄を実際に外している（空振りでない）"
            );
    }
}
