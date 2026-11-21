using System;
using System.Net.Http;
using System.Threading.Tasks;
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
/// 同じ型と内訳のまま復元される」の証明になる。応答は版を列の値（<c>Values</c>・<c>Saved</c>）だけで運ぶので、
/// 版の書き戻しのシナリオ（基底の Fact 1・10・11）が緑になることが、列の値の経路が版を届けることの証明になる。
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

    /// <summary>Kestrel を起動し、HTTP クライアントの DI を組み立てる</summary>
    public async ValueTask InitializeAsync()
    {
        BeforeServerStart();

        _server = await InProcessRemoteServer.StartAsync(
            RegisterServerServices,
            MapRemoteEndpoints,
            Ct
        );

        // 末尾の / は相対パス（"Document/Save" など）の解決に要る
        _httpClient = new HttpClient
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

        if (_server is not null)
        {
            await _server.DisposeAsync();
        }
    }
}
