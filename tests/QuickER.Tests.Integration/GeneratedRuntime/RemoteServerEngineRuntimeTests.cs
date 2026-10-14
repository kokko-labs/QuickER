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
    /// <para>
    /// Kestrel はこのトークンをスレッドプールへ予約して後から立てるため、本文の読み取りの例外より先に立つ保証がない
    /// （空いているワーカーが多い機械では先に立ち、CI のように混んでいる機械では後になる）。
    /// <see cref="Late"/> と <see cref="Early"/> は要求の寿命の機能を差し替えて、その両極をテストが決める。
    /// </para>
    /// <para>
    /// FIN でも Kestrel はこのトークンを立てる。本文の読み取りが本文の終わりを先に見れば
    /// <see cref="Microsoft.AspNetCore.Http.BadHttpRequestException"/>（400）、トークンを先に見れば取り消し（切断）になり、
    /// <see cref="Kestrel"/> ではどちらになるかが実行環境で決まる。<see cref="HostKnowsFirst"/> は後者を実 Kestrel のまま決める。
    /// </para>
    /// </remarks>
    public enum AbortTiming
    {
        /// <summary>Kestrel のトークンのまま（順序は実行環境次第）</summary>
        Kestrel,

        /// <summary>分類が終わるまで立たない（混んでいる機械の順序）</summary>
        Late,

        /// <summary>本文の読み取りが失敗した直後、分類の前に立つ（空いている機械の順序）</summary>
        Early,

        /// <summary>
        /// Kestrel のトークンのまま、ホストが切断を知ってトークンが立つまで本文の読み取りを始めない
        /// （負荷でエンドポイントが本文の読み取りへ入るのが遅れた形）
        /// </summary>
        HostKnowsFirst,
    }

    /// <summary>
    /// 要求の寿命の機能を包み、接続の中断の依頼を記録して元の機能へ渡す。
    /// <paramref name="controlled"/> のときは RequestAborted をテストの持つトークンへ差し替える（そうでなければ Kestrel のトークンのまま）。
    /// </summary>
    private sealed class ControlledLifetime(IHttpRequestLifetimeFeature inner, bool controlled)
        : IHttpRequestLifetimeFeature
    {
        private readonly CancellationTokenSource _source = new();

        /// <summary>エンジンが接続の中断を頼んだか</summary>
        public bool AbortRequested { get; private set; }

        /// <summary>Kestrel 自身のトークン（ホストが切断を知ると立つ）</summary>
        public CancellationToken HostToken => inner.RequestAborted;

        public CancellationToken RequestAborted
        {
            get => controlled ? _source.Token : inner.RequestAborted;
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

    /// <summary>
    /// 1 件の要求の観測点。差し替えた要求の寿命・切断してよい合図・フックの呼び出し回数と、失敗したときにだけ書き出す記録を持つ。
    /// </summary>
    private sealed class LifetimeProbe
    {
        private int _hookCalls;

        public ControlledLifetime? Lifetime { get; set; }

        /// <summary>クライアントが接続を切ってよい合図（サーバーの本文の読み取りが本文の到着を待っている）</summary>
        public TaskCompletionSource ReadyToDisconnect { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>OnServerError のフックが呼ばれた回数</summary>
        public int HookCalls => Volatile.Read(ref _hookCalls);

        /// <summary>失敗したときにだけ書き出す記録（待ちの途中では書き出さない）</summary>
        public ConcurrentQueue<string> Notes { get; } = new();

        public void RecordHook(Exception ex)
        {
            Interlocked.Increment(ref _hookCalls);
            Notes.Enqueue($"hook: {Chain(ex)}");
        }

        /// <summary>失敗した表明と、ためた記録・Error のログをテストの出力へ書き出す</summary>
        public void WriteOnFailure(Exception failure, ErrorLog log)
        {
            var output = TestContext.Current.TestOutputHelper;

            if (output is null)
            {
                return;
            }

            output.WriteLine($"failure: {Chain(failure)}");
            output.WriteLine(
                $"runtime: {System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}; Kestrel {System.Diagnostics.FileVersionInfo.GetVersionInfo(typeof(KestrelServerOptions).Assembly.Location).ProductVersion}"
            );

            foreach (var note in Notes)
            {
                output.WriteLine(note);
            }

            foreach (var line in log.Lines)
            {
                output.WriteLine($"error log: {line}");
            }
        }

        /// <summary>例外の型とメッセージを、内側の例外まで連ねた 1 行</summary>
        public static string Chain(Exception? ex)
        {
            var text = new StringBuilder();

            for (var current = ex; current is not null; current = current.InnerException)
            {
                text.Append(current.GetType().FullName)
                    .Append(": ")
                    .Append(current.Message)
                    .Append(" | ");
            }

            return text.ToString();
        }
    }

    /// <summary>
    /// 読み取りが本文の到着を待つところまで進んだこと（すぐに返らなかった読み取り）を知らせ、あとは元の本文へ渡す要求の本文
    /// </summary>
    private sealed class SignalingRequestBody(Stream inner, TaskCompletionSource waiting) : Stream
    {
        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            waiting.TrySetResult();
            return inner.Read(buffer, offset, count);
        }

        public override Task<int> ReadAsync(
            byte[] buffer,
            int offset,
            int count,
            CancellationToken cancellationToken
        )
        {
            var read = inner.ReadAsync(buffer, offset, count, cancellationToken);

            if (!read.IsCompleted)
            {
                waiting.TrySetResult();
            }

            return read;
        }

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default
        )
        {
            var read = inner.ReadAsync(buffer, cancellationToken);

            if (!read.IsCompleted)
            {
                waiting.TrySetResult();
            }

            return read;
        }

        public override void Flush() { }

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();
    }

    /// <summary>
    /// エンドポイントの先頭で呼び、本文を読み取りの待ちを知らせる形へ包み、要求の寿命を <paramref name="timing"/> に従って差し替える
    /// （<see cref="AbortTiming.Kestrel"/> と <see cref="AbortTiming.HostKnowsFirst"/> は Kestrel のトークンのまま、中断の依頼だけを記録する）。
    /// </summary>
    private static void PrepareRequest(HttpContext context, AbortTiming timing, LifetimeProbe probe)
    {
        context.Request.Body = new SignalingRequestBody(
            context.Request.Body,
            probe.ReadyToDisconnect
        );
        var lifetime = new ControlledLifetime(
            context.Features.GetRequiredFeature<IHttpRequestLifetimeFeature>(),
            controlled: timing is AbortTiming.Late or AbortTiming.Early
        );
        context.Features.Set<IHttpRequestLifetimeFeature>(lifetime);
        probe.Lifetime = lifetime;
    }

    /// <summary>
    /// 本文を読む処理を <paramref name="timing"/> に従って包んで返す（<see cref="AbortTiming.Early"/> では、その処理が失敗した直後に
    /// ホストが切断を知った状態にし、<see cref="AbortTiming.HostKnowsFirst"/> では、ホストのトークンが立つまで読み取りを始めない）。
    /// </summary>
    private static Func<Task<T>> ControlAbortTiming<T>(
        AbortTiming timing,
        LifetimeProbe probe,
        Func<Task<T>> read
    ) =>
        async () =>
        {
            if (timing == AbortTiming.HostKnowsFirst)
            {
                // 本文の読み取りより前に、クライアントに切ってもらう
                probe.ReadyToDisconnect.TrySetResult();
                await WhenCanceledAsync(probe.Lifetime!.HostToken);
            }

            try
            {
                return await read();
            }
            catch (Exception ex)
            {
                probe.Notes.Enqueue($"read threw: {LifetimeProbe.Chain(ex)}");

                if (timing == AbortTiming.Early)
                {
                    probe.Lifetime!.SignalHostKnows();
                }

                throw;
            }
        };

    /// <summary><paramref name="token"/> が立つまで待つ（10 秒で打ち切る）</summary>
    private static async Task WhenCanceledAsync(CancellationToken token)
    {
        var canceled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        using (token.Register(() => canceled.TrySetResult()))
        {
            await canceled.Task.WaitAsync(TimeSpan.FromSeconds(10), Ct);
        }
    }

    /// <summary>
    /// エンジンが接続の中断をホストへ頼んだかを確かめる。頼むのは、ホストがまだ知らない切断（RST）を見つけたときだけ
    /// （FIN は Kestrel が例外を投げる前に自分で中断済み、<see cref="AbortTiming.Early"/> と
    /// <see cref="AbortTiming.HostKnowsFirst"/> はホストが知っている）。
    /// </summary>
    private static void AssertAbortRequest(LifetimeProbe probe, bool reset, AbortTiming timing)
    {
        if (timing == AbortTiming.Kestrel && reset)
        {
            // 実際の Kestrel の RST では、ホストが先に知るかどうかが実行環境次第なので表明しない
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
    /// 本文を途中まで送り、サーバーの本文の読み取りが待ちに入った合図（<paramref name="readyToDisconnect"/>）を待ってから、FIN または RST で接続を切る。
    /// FIN では送信側だけを閉じて応答を最後まで読み、受け取ったバイト数を返す（RST では 0）。
    /// </summary>
    private static async Task<int> SendPartialBodyAndDisconnectAsync(
        Uri baseUri,
        string method,
        string path,
        bool reset,
        Task readyToDisconnect
    )
    {
        using var client = new TcpClient();
        await client.ConnectAsync(baseUri.Host, baseUri.Port, Ct);
        var stream = client.GetStream();
        var head =
            $"{method} {path} HTTP/1.1\r\nHost: test\r\nContent-Type: application/json\r\nContent-Length: 1000\r\n\r\n{{\"a\":";
        await stream.WriteAsync(Encoding.ASCII.GetBytes(head), Ct);
        await stream.FlushAsync(Ct);

        // サーバーの本文の読み取りが、続きの到着を待つところまで進むのを待つ
        await readyToDisconnect.WaitAsync(TimeSpan.FromSeconds(10), Ct);

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

    /// <summary>アップロードの本文を最後まで読み捨てる書き込み処理（<paramref name="timing"/> に従って包む）</summary>
    private static Func<Stream, long, Task<bool>> DrainBody(
        HttpContext context,
        AbortTiming timing,
        LifetimeProbe probe
    ) =>
        (body, _) =>
            ControlAbortTiming(
                timing,
                probe,
                async () =>
                {
                    await body.CopyToAsync(Stream.Null, context.RequestAborted);
                    return true;
                }
            )();

    /// <summary>
    /// 切断の種類と時期ごとに許す結果。RST は無応答。FIN は本文の読み取りが本文の終わりを先に見れば Kestrel の分類どおり 400、
    /// 要求のトークンを先に見れば切断（無応答・StatusCode は 200 のまま）で、実 Kestrel のままではどちらになるかが実行環境で決まる。
    /// </summary>
    private static string[] ExpectedOutcomes(bool reset, AbortTiming timing) =>
        (reset, timing) switch
        {
            (true, _) => ["returned:200"],
            (false, AbortTiming.Kestrel) => ["returned:400", "returned:200"],
            (false, AbortTiming.HostKnowsFirst) => ["returned:200"],
            (false, _) => ["returned:400"],
        };

    /// <summary>500 の応答の拡張点を観測するためのエンドポイントのメタデータ（呼ばれたらフックの回数を数える）</summary>
    private static RemoteErrorDetailPolicy CountHooks(LifetimeProbe probe) =>
        new(expose: false, onServerError: (_, ex) => probe.RecordHook(ex));

    [Theory(
        DisplayName = "[RemoteServerEngine] 本文の途中で切れた要求は、FIN なら 400 か切断（本文の終わりとトークンのどちらを先に見たか）・RST なら無応答で終え、回線にもログにもフックにも出さない"
    )]
    [InlineData(false, AbortTiming.Kestrel)]
    [InlineData(true, AbortTiming.Kestrel)]
    [InlineData(false, AbortTiming.Late)]
    [InlineData(true, AbortTiming.Late)]
    [InlineData(false, AbortTiming.Early)]
    [InlineData(true, AbortTiming.Early)]
    [InlineData(false, AbortTiming.HostKnowsFirst)]
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
                            PrepareRequest(context, timing, probe);

                            try
                            {
                                var read = ControlAbortTiming<object?>(
                                    timing,
                                    probe,
                                    async () =>
                                        await RemoteServerEngine.ReadRequestAsync<
                                            Dictionary<string, int>
                                        >(context)
                                );
                                await RemoteServerEngine.ExecuteAsync(context, read);
                                probe.Notes.Enqueue(
                                    $"returned: status={context.Response.StatusCode} started={context.Response.HasStarted} abortRequested={probe.Lifetime!.AbortRequested}"
                                );
                                outcome.TrySetResult($"returned:{context.Response.StatusCode}");
                            }
                            catch (Exception ex)
                            {
                                probe.Notes.Enqueue($"endpoint threw: {LifetimeProbe.Chain(ex)}");
                                outcome.TrySetResult($"threw:{ex.GetType().Name}");
                                throw;
                            }
                        }
                    )
                    .WithMetadata(CountHooks(probe)),
            log
        );

        try
        {
            var received = await SendPartialBodyAndDisconnectAsync(
                new Uri(app.Urls.First()),
                "POST",
                "/json",
                reset,
                probe.ReadyToDisconnect.Task
            );

            (await outcome.Task.WaitAsync(TimeSpan.FromSeconds(10), Ct))
                .Should()
                .BeOneOf(
                    ExpectedOutcomes(reset, timing),
                    "RST は応答を書かず、FIN は 400 か切断とし、どちらも例外を漏らさない"
                );
            received
                .Should()
                .Be(0, "Kestrel は本文の途中の FIN で応答の側を閉じるので、400 も回線には出ない");
            log.Lines.Should().BeEmpty("切断はサーバーの失敗としてログに残さない");
            probe.HookCalls.Should().Be(0, "切断は 500 の応答の拡張点を呼ばない");
            AssertAbortRequest(probe, reset, timing);
        }
        catch (Exception ex)
        {
            probe.WriteOnFailure(ex, log);
            throw;
        }
    }

    [Theory(
        DisplayName = "[RemoteServerEngine] blob のアップロードの途中で切れた要求は、FIN なら 400 か切断（本文の終わりとトークンのどちらを先に見たか）・RST なら無応答で終え、回線にもログにもフックにも出さない"
    )]
    [InlineData(false, AbortTiming.Kestrel)]
    [InlineData(true, AbortTiming.Kestrel)]
    [InlineData(false, AbortTiming.Late)]
    [InlineData(true, AbortTiming.Late)]
    [InlineData(false, AbortTiming.Early)]
    [InlineData(true, AbortTiming.Early)]
    [InlineData(false, AbortTiming.HostKnowsFirst)]
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
                            PrepareRequest(context, timing, probe);

                            try
                            {
                                await RemoteServerEngine.ExecuteUploadAsync(
                                    context,
                                    DrainBody(context, timing, probe)
                                );
                                probe.Notes.Enqueue(
                                    $"returned: status={context.Response.StatusCode} started={context.Response.HasStarted} abortRequested={probe.Lifetime!.AbortRequested}"
                                );
                                outcome.TrySetResult($"returned:{context.Response.StatusCode}");
                            }
                            catch (Exception ex)
                            {
                                probe.Notes.Enqueue($"endpoint threw: {LifetimeProbe.Chain(ex)}");
                                outcome.TrySetResult($"threw:{ex.GetType().Name}");
                                throw;
                            }
                        }
                    )
                    .WithMetadata(CountHooks(probe)),
            log
        );

        try
        {
            var received = await SendPartialBodyAndDisconnectAsync(
                new Uri(app.Urls.First()),
                "PUT",
                "/blob",
                reset,
                probe.ReadyToDisconnect.Task
            );

            (await outcome.Task.WaitAsync(TimeSpan.FromSeconds(10), Ct))
                .Should()
                .BeOneOf(
                    ExpectedOutcomes(reset, timing),
                    "RST は応答を書かず、FIN は 400 か切断とし、どちらも例外を漏らさない"
                );
            received
                .Should()
                .Be(0, "Kestrel は本文の途中の FIN で応答の側を閉じるので、400 も回線には出ない");
            log.Lines.Should().BeEmpty("切断はサーバーの失敗としてログに残さない");
            probe.HookCalls.Should().Be(0, "切断は 500 の応答の拡張点を呼ばない");
            AssertAbortRequest(probe, reset, timing);
        }
        catch (Exception ex)
        {
            probe.WriteOnFailure(ex, log);
            throw;
        }
    }

    /// <summary>すでに時間切れになった、サーバーのタイムアウトの機能（RequestTimeouts が置くものの代わり）</summary>
    private sealed class ExpiredTimeout
        : Microsoft.AspNetCore.Http.Timeouts.IHttpRequestTimeoutFeature
    {
        public CancellationToken RequestTimeoutToken { get; } = new(canceled: true);

        public void DisableTimeout() { }
    }

    /// <summary>
    /// サーバーのタイムアウトは、要求のトークンが立つ時期に依らず、取り消しとしてミドルウェアへ返る。
    /// </summary>
    /// <remarks>
    /// RequestTimeouts は、自分のトークン（<c>RequestTimeoutToken</c>）を先に立て、それに連結した要求のトークンが後に続く。
    /// 本文の読み取りが待っているところでタイムアウトが来ると、Kestrel の本文の読み取りの例外が要求のトークンより先に
    /// 上がり得るので、判定はタイムアウトの機能のトークンだけを見る。ここでは要求のトークンを一度も立てずに、
    /// タイムアウトの機能だけを時間切れにして、エンジンが失敗を取り消しとして外へ渡すこと（RequestTimeouts が 504 で
    /// 答えられる形）と、サーバーの障害として記録しないことを確かめる。
    /// </remarks>
    [Theory(
        DisplayName = "[RemoteServerEngine] サーバーのタイムアウトは、要求のトークンが立っていなくても、取り消しとしてミドルウェアへ返る"
    )]
    [InlineData("io")]
    [InlineData("cancel")]
    public async Task ServerTimeout_IsHandedBackAsCancellation_WhateverTheRequestToken(
        string failure
    )
    {
        var log = new ErrorLog();
        Exception? handedBack = null;
        var requestAbortedAtClassification = (bool?)null;

        await using var app = await StartAsync(
            app =>
            {
                // RequestTimeouts の代わり: タイムアウトの機能を置き、外へ返った失敗を受け取って 504 で答える
                app.Use(
                    async (context, next) =>
                    {
                        context.Features.Set<Microsoft.AspNetCore.Http.Timeouts.IHttpRequestTimeoutFeature>(
                            new ExpiredTimeout()
                        );

                        try
                        {
                            await next(context);
                        }
                        catch (Exception ex)
                        {
                            handedBack = ex;
                            context.Response.StatusCode = StatusCodes.Status504GatewayTimeout;
                        }
                    }
                );
                app.MapPost(
                    "/timed-out",
                    (HttpContext context) =>
                        RemoteServerEngine.ExecuteAsync(
                            context,
                            () =>
                            {
                                requestAbortedAtClassification = context
                                    .RequestAborted
                                    .IsCancellationRequested;

                                // 本文の読み取りが先に投げる形（IOException）と、取り消しそのもの
                                throw failure == "io"
                                    ? new IOException("The request body read failed.")
                                    : new OperationCanceledException("The read was cancelled.");
                            }
                        )
                );
            },
            log
        );

        using var client = new HttpClient();
        using var response = await client.PostAsync(
            $"{app.Urls.First()}/timed-out",
            new StringContent("{}"),
            Ct
        );

        requestAbortedAtClassification.Should().BeFalse("要求のトークンは立っていない");
        response.StatusCode.Should().Be(HttpStatusCode.GatewayTimeout);
        handedBack.Should().BeAssignableTo<OperationCanceledException>();

        if (failure == "io")
        {
            handedBack!.InnerException.Should().BeOfType<IOException>("元の失敗は内側に残す");
        }

        log.Lines.Should().BeEmpty("サーバーのタイムアウトはサーバーの障害として記録しない");
    }
}
