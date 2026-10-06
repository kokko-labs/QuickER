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

    /// <summary>本文を途中まで送ったあと、FIN または RST で接続を切る</summary>
    private static async Task SendPartialBodyAndDisconnectAsync(
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
        }
        else
        {
            client.Client.Shutdown(SocketShutdown.Send);
        }

        client.Client.Close();
    }

    [Theory(
        DisplayName = "[RemoteServerEngine] 本文の途中で切れた要求は 400 に包まず、応答も書かずに終える"
    )]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Json_ClientDisconnectsMidBody_IsLeftWithoutResponse(bool reset)
    {
        var outcome = new TaskCompletionSource<string>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var log = new ErrorLog();

        await using var app = await StartAsync(
            app =>
                app.MapPost(
                    "/json",
                    async (HttpContext context) =>
                    {
                        try
                        {
                            await RemoteServerEngine.ExecuteAsync(
                                context,
                                async () =>
                                    await RemoteServerEngine.ReadRequestAsync<
                                        Dictionary<string, int>
                                    >(context)
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

        await SendPartialBodyAndDisconnectAsync(new Uri(app.Urls.First()), "POST", "/json", reset);

        (await outcome.Task.WaitAsync(TimeSpan.FromSeconds(10), Ct))
            .Should()
            .Be("returned:200", "切れた接続へ 400 を書こうとせず、例外も漏らさない");
        log.Lines.Should().BeEmpty("切断はサーバーの失敗としてログに残さない");
    }

    [Theory(
        DisplayName = "[RemoteServerEngine] blob のアップロードの途中で切れた要求は 500 にせず、ログにも残さない"
    )]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Upload_ClientDisconnectsMidBody_IsLeftWithoutResponse(bool reset)
    {
        var outcome = new TaskCompletionSource<string>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var log = new ErrorLog();

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
                                async (body, _) =>
                                {
                                    await body.CopyToAsync(Stream.Null, context.RequestAborted);
                                    return true;
                                }
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

        await SendPartialBodyAndDisconnectAsync(new Uri(app.Urls.First()), "PUT", "/blob", reset);

        (await outcome.Task.WaitAsync(TimeSpan.FromSeconds(10), Ct))
            .Should()
            .Be("returned:200", "切れた接続へ応答を書こうとせず、例外も漏らさない");
        log.Lines.Should().BeEmpty("切断はサーバーの失敗としてログに残さない");
    }
}
