using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace QuickER.Tests.Integration;

/// <summary>
/// 生成されたリモートエンドポイントを in-process の Kestrel（127.0.0.1 の空きポート）で公開するテスト用サーバー。
/// </summary>
/// <remarks>
/// <para>
/// リモート系の統合テストはどれも「<c>CreateBuilder</c> → ログ抑止 → ポート 0 で <c>UseUrls</c> → サーバー側
/// リポジトリを DI 登録 → <c>Build</c> → <c>MapGeneratedRemoteEndpoints</c> → <c>StartAsync</c> → 実 URL を取得」という
/// 同じ 10 行を書いていたため、その定型だけをここへ集約する。
/// </para>
/// <para>
/// <c>MapGeneratedRemoteEndpoints</c> はフィクスチャごとに別の生成 namespace へ出るため、このヘルパーは
/// 呼び出し側から <c>mapEndpoints</c> デリゲートとして受け取る（ジェネリックにはできない）。
/// Kestrel 自体の設定を変えたいテスト（ボディサイズ上限など）は <c>configure</c> で <see cref="WebApplicationBuilder"/> に触れる。
/// </para>
/// </remarks>
internal sealed class InProcessRemoteServer : IAsyncDisposable
{
    private readonly WebApplication _app;

    private InProcessRemoteServer(WebApplication app, string baseUrl)
    {
        _app = app;
        BaseUrl = baseUrl;
    }

    /// <summary>起動した Kestrel のベース URL（例 <c>http://127.0.0.1:52341</c>・末尾スラッシュなし）</summary>
    public string BaseUrl { get; }

    /// <summary>ルートプレフィックスまで含むクライアント用ベースアドレス（<c>RemotePaths.DefaultPrefix</c> を渡す）</summary>
    public string BaseAddress(string prefix) => $"{BaseUrl}{prefix}";

    /// <summary>サーバー側 DI とエンドポイント登録を受け取り、空きポートで Kestrel を起動する</summary>
    /// <param name="registerServices">サーバー側リポジトリ・Save フックなどの DI 登録</param>
    /// <param name="mapEndpoints">生成された <c>MapGeneratedRemoteEndpoints</c> の呼び出し</param>
    /// <param name="ct">起動を打ち切るトークン</param>
    /// <param name="configure">Kestrel 等の追加設定（省略可）</param>
    public static async Task<InProcessRemoteServer> StartAsync(
        Action<IServiceCollection> registerServices,
        Action<WebApplication> mapEndpoints,
        CancellationToken ct = default,
        Action<WebApplicationBuilder>? configure = null
    )
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        registerServices(builder.Services);
        configure?.Invoke(builder);

        var app = builder.Build();
        mapEndpoints(app);
        await app.StartAsync(ct).ConfigureAwait(false);

        return new InProcessRemoteServer(app, app.Urls.First());
    }

    /// <summary>サーバーを停止する（送信前ガードの検証など「サーバーが居ない」状態を作るために使う）</summary>
    public Task StopAsync(CancellationToken ct = default) => _app.StopAsync(ct);

    /// <summary>サーバーを破棄する</summary>
    public ValueTask DisposeAsync() => _app.DisposeAsync();
}
