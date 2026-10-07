using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using QuickER.Tests.GeneratedBinaryFixture;
using Xunit;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// リモートサーバーの固定エンジン（<c>RemoteServerEngine</c> と <c>DeferredOctetStreamBody</c>）を、生成エンドポイントを通さずに
/// 最小の Kestrel ホストへ直接張って検証する。
/// </summary>
/// <remarks>
/// <para>
/// 生成エンドポイントの読み取り処理は、遅延した応答の本文へ非同期に書くだけなので、ここで見る経路
/// （書き込み前の FlushAsync・同期の Write・本文の途中での切断）は手書きの読み取り処理と、壊れた接続でしか踏まれない。
/// そのため処理をテストの側で書き、エンジンの振る舞いだけを見る。
/// </para>
/// <para>切断は生のソケットで作る（FIN＝送信側を閉じる・RST＝linger 0 で閉じる）。Docker 不要（CI 常時実行）。</para>
/// </remarks>
public sealed class RemoteServerEngineRuntimeTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    /// <summary>Error 以上のログを溜めるテスト用プロバイダ</summary>
    private sealed class ErrorLog : ILoggerProvider
    {
        public ConcurrentQueue<string> Lines { get; } = new();

        public ILogger CreateLogger(string categoryName) => new Logger(this, categoryName);

        public void Dispose() { }

        private sealed class Logger(ErrorLog owner, string category) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Error;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter
            )
            {
                if (IsEnabled(logLevel))
                {
                    owner.Lines.Enqueue(
                        $"{category}: {formatter(state, exception)} {exception?.GetType().Name}"
                    );
                }
            }
        }
    }

    /// <summary>1 本のエンドポイントだけを持つ最小のホストを起動する</summary>
    private static async Task<WebApplication> StartAsync(
        Action<WebApplication> map,
        ErrorLog? log = null,
        bool allowSynchronousIO = false
    )
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();

        if (log is not null)
        {
            builder.Logging.AddProvider(log);
        }

        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.Configure<KestrelServerOptions>(options =>
            options.AllowSynchronousIO = allowSynchronousIO
        );

        var app = builder.Build();
        map(app);
        await app.StartAsync(Ct);
        return app;
    }

    [Fact(
        DisplayName = "[RemoteServerEngine] 書き込み前の FlushAsync は応答を始めず、行なしは NotFound 印つきの 404 になる"
    )]
    public async Task Download_FlushBeforeWrite_StillResolvesToNotFound()
    {
        await using var app = await StartAsync(app =>
            app.MapGet(
                "/blob",
                (HttpContext context) =>
                    RemoteServerEngine.ExecuteDownloadAsync(
                        context,
                        async stream =>
                        {
                            await stream.FlushAsync(Ct);
                            return false;
                        }
                    )
            )
        );

        using var client = new HttpClient();
        using var response = await client.GetAsync($"{app.Urls.First()}/blob", Ct);

        response
            .StatusCode.Should()
            .Be(HttpStatusCode.NotFound, "まだ何も書いていないので 404 へ切り替えられる");
        (await response.Content.ReadFromJsonAsync<RemoteError>(Ct))!.Type.Should().Be("NotFound");
    }

    [Theory(
        DisplayName = "[RemoteServerEngine] 同期の Write はホストの AllowSynchronousIO に従う（既定では 500・許可すれば 200）"
    )]
    [InlineData(false, HttpStatusCode.InternalServerError)]
    [InlineData(true, HttpStatusCode.OK)]
    public async Task Download_SynchronousWrite_FollowsAllowSynchronousIO(
        bool allowSynchronousIO,
        HttpStatusCode expected
    )
    {
        await using var app = await StartAsync(
            app =>
                app.MapGet(
                    "/blob",
                    (HttpContext context) =>
                        RemoteServerEngine.ExecuteDownloadAsync(context, WriteSynchronously)
                ),
            allowSynchronousIO: allowSynchronousIO
        );

        using var client = new HttpClient();
        using var response = await client.GetAsync($"{app.Urls.First()}/blob", Ct);

        response.StatusCode.Should().Be(expected);

        if (allowSynchronousIO)
        {
            (await response.Content.ReadAsByteArrayAsync(Ct)).Should().Equal(1, 2, 3);
        }
    }

    /// <summary>応答の本文へ同期で書く読み取り処理</summary>
    private static Task<bool> WriteSynchronously(Stream stream)
    {
        stream.Write([1, 2, 3], 0, 3);
        return Task.FromResult(true);
    }

    /// <summary>
    /// 切断を知らせる <see cref="HttpContext.RequestAborted"/> が、分類から見ていつ立っているか。
    /// </summary>
    /// <remarks>
    /// Kestrel はこのトークンをスレッドプールへ予約して後から立てるため、本文の読み取りの例外より先に立つ保証がない
    /// （空いているワーカーが多い機械では先に立ち、CI のように混んでいる機械では後になる）。
    /// <see cref="Late"/> と <see cref="Early"/> は要求の寿命の機能を差し替えて、その両極をテストが決める。
    /// </remarks>
    public enum AbortTiming
    {
        /// <summary>Kestrel のトークンのまま（順序は実行環境次第）</summary>
        Kestrel,

        /// <summary>分類が終わるまで立たない（混んでいる機械の順序）</summary>
        Late,

        /// <summary>本文の読み取りが失敗した直後、分類の前に立つ（空いている機械の順序）</summary>
        Early,
    }

    /// <summary>RequestAborted だけをテストの持つトークンへ差し替え、接続の中断は記録して元の機能へ渡す要求の寿命</summary>
    private sealed class ControlledLifetime(IHttpRequestLifetimeFeature inner)
        : IHttpRequestLifetimeFeature
    {
        private readonly CancellationTokenSource _source = new();

        /// <summary>エンジンが接続の中断を頼んだか</summary>
        public bool AbortRequested { get; private set; }

        public CancellationToken RequestAborted
        {
            get => _source.Token;
            set { }
        }

        public void Abort()
        {
            AbortRequested = true;
            inner.Abort();
        }

        /// <summary>ホストが切断を知った状態にする（実際の Kestrel はトークンを立てる前に接続を中断済みとして記録する）</summary>
        public void SignalHostKnows()
        {
            inner.Abort();
            _source.Cancel();
        }
    }

    /// <summary>1 件の要求で差し替えた要求の寿命を、テストの表明まで運ぶ入れ物</summary>
    private sealed class LifetimeProbe
    {
        public ControlledLifetime? Lifetime { get; set; }
    }

    /// <summary>
    /// <paramref name="timing"/> に従って要求の寿命を差し替え、本文を読む処理を包んで返す
    /// （<see cref="AbortTiming.Early"/> では、その処理が失敗した直後にホストが切断を知った状態にする）。
    /// </summary>
    private static Func<Task<T>> ControlAbortTiming<T>(
        HttpContext context,
        AbortTiming timing,
        LifetimeProbe probe,
        Func<Task<T>> read
    )
    {
        if (timing == AbortTiming.Kestrel)
        {
            return read;
        }

        var lifetime = new ControlledLifetime(
            context.Features.GetRequiredFeature<IHttpRequestLifetimeFeature>()
        );
        context.Features.Set<IHttpRequestLifetimeFeature>(lifetime);
        probe.Lifetime = lifetime;

        return async () =>
        {
            try
            {
                return await read();
            }
            catch when (timing == AbortTiming.Early)
            {
                lifetime.SignalHostKnows();
                throw;
            }
        };
    }

    /// <summary>
    /// エンジンが接続の中断をホストへ頼んだかを確かめる。頼むのは、ホストがまだ知らない切断（RST）を見つけたときだけ
    /// （FIN は Kestrel が例外を投げる前に自分で中断済み、<see cref="AbortTiming.Early"/> はホストが知っている）。
    /// </summary>
    private static void AssertAbortRequest(LifetimeProbe probe, bool reset, AbortTiming timing)
    {
        if (timing == AbortTiming.Kestrel)
        {
            // 実際の Kestrel では、ホストが先に知るかどうかが実行環境次第なので表明しない
            return;
        }

        probe
            .Lifetime!.AbortRequested.Should()
            .Be(
                reset && timing == AbortTiming.Late,
                "ホストがまだ知らない切断だけを、エンジンがホストへ知らせる"
            );
    }

    /// <summary>
    /// 本文を途中まで送ったあと、FIN または RST で接続を切る。
    /// FIN では送信側だけを閉じて応答を最後まで読み、受け取ったバイト数を返す（RST では 0）。
    /// </summary>
    private static async Task<int> SendPartialBodyAndDisconnectAsync(
        Uri baseUri,
        string method,
        string path,
        bool reset
    )
    {
        using var client = new TcpClient();
        await client.ConnectAsync(baseUri.Host, baseUri.Port, Ct);
        var stream = client.GetStream();
        var head =
            $"{method} {path} HTTP/1.1\r\nHost: test\r\nContent-Type: application/json\r\nContent-Length: 1000\r\n\r\n{{\"a\":";
        await stream.WriteAsync(Encoding.ASCII.GetBytes(head), Ct);
        await stream.FlushAsync(Ct);

        // サーバーが本文の読み取りに入るまで待つ
        await Task.Delay(300, Ct);

        if (reset)
        {
            client.Client.LingerState = new LingerOption(true, 0);
            client.Client.Close();
            return 0;
        }

        client.Client.Shutdown(SocketShutdown.Send);

        // 半閉じのクライアントは応答を読める立場にある。サーバーが何かを返せば、ここで受け取る
        var buffer = new byte[4096];
        var received = 0;

        try
        {
            int read;

            while (
                (
                    read = await stream
                        .ReadAsync(buffer, Ct)
                        .AsTask()
                        .WaitAsync(TimeSpan.FromSeconds(10), Ct)
                ) > 0
            )
            {
                received += read;
            }
        }
        catch (IOException)
        {
            // サーバーが接続を中断した（RST）。それまでに受け取った分だけを数える
        }

        return received;
    }

    /// <summary>アップロードの本文を最後まで読み捨てる書き込み処理（<paramref name="timing"/> に従って要求の寿命を差し替える）</summary>
    private static Func<Stream, long, Task<bool>> DrainBody(
        HttpContext context,
        AbortTiming timing,
        LifetimeProbe probe
    ) =>
        (body, _) =>
            ControlAbortTiming(
                context,
                timing,
                probe,
                async () =>
                {
                    await body.CopyToAsync(Stream.Null, context.RequestAborted);
                    return true;
                }
            )();

    /// <summary>切断の種類ごとの期待値（FIN は Kestrel の分類どおり 400、RST は無応答）</summary>
    private static string ExpectedOutcome(bool reset) => reset ? "returned:200" : "returned:400";

    [Theory(
        DisplayName = "[RemoteServerEngine] 本文の途中で切れた要求は、FIN なら 400（回線には何も出ない）・RST なら無応答で終え、ログも例外も出さない"
    )]
    [InlineData(false, AbortTiming.Kestrel)]
    [InlineData(true, AbortTiming.Kestrel)]
    [InlineData(false, AbortTiming.Late)]
    [InlineData(true, AbortTiming.Late)]
    [InlineData(false, AbortTiming.Early)]
    [InlineData(true, AbortTiming.Early)]
    public async Task Json_ClientDisconnectsMidBody_IsNotAnsweredAsFault(
        bool reset,
        AbortTiming timing
    )
    {
        var outcome = new TaskCompletionSource<string>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var log = new ErrorLog();
        var probe = new LifetimeProbe();

        await using var app = await StartAsync(
            app =>
                app.MapPost(
                    "/json",
                    async (HttpContext context) =>
                    {
                        try
                        {
                            var read = ControlAbortTiming<object?>(
                                context,
                                timing,
                                probe,
                                async () =>
                                    await RemoteServerEngine.ReadRequestAsync<
                                        Dictionary<string, int>
                                    >(context)
                            );
                            await RemoteServerEngine.ExecuteAsync(context, read);
                            outcome.TrySetResult($"returned:{context.Response.StatusCode}");
                        }
                        catch (Exception ex)
                        {
                            outcome.TrySetResult($"threw:{ex.GetType().Name}");
                            throw;
                        }
                    }
                ),
            log
        );

        var received = await SendPartialBodyAndDisconnectAsync(
            new Uri(app.Urls.First()),
            "POST",
            "/json",
            reset
        );

        (await outcome.Task.WaitAsync(TimeSpan.FromSeconds(10), Ct))
            .Should()
            .Be(
                ExpectedOutcome(reset),
                "RST は応答を書かず、FIN は Kestrel の分類どおり 400 とし、どちらも例外を漏らさない"
            );
        received
            .Should()
            .Be(0, "Kestrel は本文の途中の FIN で応答の側を閉じるので、400 も回線には出ない");
        log.Lines.Should().BeEmpty("切断はサーバーの失敗としてログに残さない");
        AssertAbortRequest(probe, reset, timing);
    }

    [Theory(
        DisplayName = "[RemoteServerEngine] blob のアップロードの途中で切れた要求は、FIN なら 400（回線には何も出ない）・RST なら無応答で終え、500 にもログにもしない"
    )]
    [InlineData(false, AbortTiming.Kestrel)]
    [InlineData(true, AbortTiming.Kestrel)]
    [InlineData(false, AbortTiming.Late)]
    [InlineData(true, AbortTiming.Late)]
    [InlineData(false, AbortTiming.Early)]
    [InlineData(true, AbortTiming.Early)]
    public async Task Upload_ClientDisconnectsMidBody_IsNotAnsweredAsFault(
        bool reset,
        AbortTiming timing
    )
    {
        var outcome = new TaskCompletionSource<string>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var log = new ErrorLog();
        var probe = new LifetimeProbe();

        await using var app = await StartAsync(
            app =>
                app.MapPut(
                    "/blob",
                    async (HttpContext context) =>
                    {
                        try
                        {
                            await RemoteServerEngine.ExecuteUploadAsync(
                                context,
                                DrainBody(context, timing, probe)
                            );
                            outcome.TrySetResult($"returned:{context.Response.StatusCode}");
                        }
                        catch (Exception ex)
                        {
                            outcome.TrySetResult($"threw:{ex.GetType().Name}");
                            throw;
                        }
                    }
                ),
            log
        );

        var received = await SendPartialBodyAndDisconnectAsync(
            new Uri(app.Urls.First()),
            "PUT",
            "/blob",
            reset
        );

        (await outcome.Task.WaitAsync(TimeSpan.FromSeconds(10), Ct))
            .Should()
            .Be(
                ExpectedOutcome(reset),
                "RST は応答を書かず、FIN は Kestrel の分類どおり 400 とし、どちらも例外を漏らさない"
            );
        received
            .Should()
            .Be(0, "Kestrel は本文の途中の FIN で応答の側を閉じるので、400 も回線には出ない");
        log.Lines.Should().BeEmpty("切断はサーバーの失敗としてログに残さない");
        AssertAbortRequest(probe, reset, timing);
    }
}
