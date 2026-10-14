using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using QuickER.Tests.GeneratedBinaryFixture;
using QuickER.Tests.Integration;
using Xunit;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// 生成したリモートサーバーを、ホストの構成（サーバーのタイムアウト・本文のバッファリング・HTTP/2・
/// エンドポイントのメタデータ）を変えて実 HTTP（in-process の Kestrel）で流すテスト。
/// </summary>
/// <remarks>
/// <para>
/// 既存のリモートのテストは、既定の構成のホストでしか流していない。ホストが足すミドルウェアや設定は、
/// 要求のトークン・本文のストリーム・例外の型を変えるので、既定の構成で緑でも、利用者の構成では黙って成功を
/// 返したり（タイムアウト）、すべての要求が失敗したり（バッファリング）する。
/// </para>
/// <para>
/// 図は無制限バイナリ列の除外が効く BinaryFixture（SQLite の QuickER 版 Repository）で、クライアントは
/// 生成された HTTP のリモート実装を使う。500 のフックは、生成したグループへ後から
/// <see cref="RemoteErrorDetailPolicy"/> を付けて観測する（エンドポイントのメタデータは最後のものが使われる）。
/// </para>
/// </remarks>
[Trait("Category", "Integration")]
public sealed class RemoteHostConfigurationRuntimeTests(ITestOutputHelper output) : IDisposable
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private readonly SqliteTempDatabase _db = SqliteTempDatabase.Create();

    public void Dispose() => _db.Dispose();

    /// <summary>要求の本文の上限を表すエンドポイントのメタデータ（グループへ付ける）</summary>
    private sealed class SizeLimit(long limit) : IRequestSizeLimitMetadata
    {
        public long? MaxRequestBodySize => limit;
    }

    /// <summary>
    /// 先頭だけを返して止まる、シークできないストリーム（送信が途中で止まるクライアント）。
    /// <paramref name="resumeAfter"/> を渡すと、その時間の後に残り（<paramref name="total"/> まで）を返す（遅いクライアント）。
    /// </summary>
    private sealed class StallingStream(int head, TimeSpan? resumeAfter = null, int total = 0)
        : Stream
    {
        private int _remaining = head;
        private int _rest = Math.Max(0, total - head);
        private bool _resumed;

        /// <summary>先頭を返し終えた（サーバーが本文を読み始めた）ことの合図</summary>
        public TaskCompletionSource HeadSent { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default
        )
        {
            if (_remaining > 0)
            {
                var count = Math.Min(_remaining, buffer.Length);
                buffer.Span[..count].Fill(7);
                _remaining -= count;
                return count;
            }

            HeadSent.TrySetResult();

            if (resumeAfter is null)
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
                return 0;
            }

            if (!_resumed)
            {
                await Task.Delay(resumeAfter.Value, cancellationToken);
                _resumed = true;
            }

            if (_rest == 0)
            {
                return 0;
            }

            var rest = Math.Min(_rest, buffer.Length);
            buffer.Span[..rest].Fill(8);
            _rest -= rest;
            return rest;
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

        public override void Flush() { }

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();
    }

    /// <summary>決まった中身を返す、シークできないストリーム（長さを宣言しない送信＝chunked）</summary>
    private sealed class ForwardOnlyStream(byte[] data) : MemoryStream(data)
    {
        public override bool CanSeek => false;
    }

    /// <summary>要求を HTTP/2 で送る（HttpClient は自分で作る要求の版を既定にしないので、ここで指定する）</summary>
    private sealed class Http2Handler() : DelegatingHandler(new SocketsHttpHandler())
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            request.Version = HttpVersion.Version20;
            request.VersionPolicy = HttpVersionPolicy.RequestVersionExact;
            return base.SendAsync(request, cancellationToken);
        }
    }

    /// <summary>
    /// 要求の寿命の差し替え：要求のトークンを自分で持ち、テストが決めた時点で立てる（区切り 21 の
    /// RemoteServerEngineRuntimeTests の ControlledLifetime と同じ形）。
    /// </summary>
    private sealed class EarlyAbortLifetime(IHttpRequestLifetimeFeature inner)
        : IHttpRequestLifetimeFeature
    {
        private readonly CancellationTokenSource _source = new();

        public CancellationToken RequestAborted
        {
            get => _source.Token;
            set { }
        }

        public void Abort() => inner.Abort();

        /// <summary>ホストが切断を知った状態にする（接続を中断済みとして記録してから、トークンを立てる）</summary>
        public void SignalHostKnows()
        {
            inner.Abort();
            _source.Cancel();
        }
    }

    /// <summary>
    /// 本文の読み取りで投げた例外を記録し、<paramref name="onFailure"/> を呼んでから、そのまま流す
    /// （HTTP/2 のケースでは、ここで要求のトークンを立てて「分類の前にトークンが立っている」状態を作る）。
    /// </summary>
    private sealed class RecordingBody(
        Stream inner,
        ConcurrentQueue<Exception> thrown,
        Action? onFailure = null
    ) : Stream
    {
        public override bool CanRead => inner.CanRead;

        public override bool CanSeek => inner.CanSeek;

        public override bool CanWrite => false;

        public override long Length => inner.Length;

        public override long Position
        {
            get => inner.Position;
            set => inner.Position = value;
        }

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default
        )
        {
            try
            {
                return await inner.ReadAsync(buffer, cancellationToken);
            }
            catch (Exception ex)
            {
                thrown.Enqueue(ex);
                onFailure?.Invoke();
                throw;
            }
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

        public override void Flush() { }

        public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();
    }

    /// <summary>ホストの構成</summary>
    private sealed record HostOptions
    {
        public TimeSpan? RequestTimeout { get; init; }

        public Action<WebApplication>? Middleware { get; init; }

        public Action<RouteGroupBuilder>? Group { get; init; }

        public ISaveHook<DocumentEntity>? SaveHook { get; init; }

        public Action<HttpContext, Exception>? OnServerError { get; init; }

        public bool Http2 { get; init; }
    }

    /// <summary>起動したホストとクライアント</summary>
    private sealed class Host : IAsyncDisposable
    {
        public required InProcessRemoteServer Server { get; init; }

        public required ServiceProvider Client { get; init; }

        public required HttpClient Raw { get; init; }

        public required RecordingLoggerProvider Log { get; init; }

        public required ConcurrentQueue<Exception> Hooks { get; init; }

        public required SemaphoreSlim Completed { get; init; }

        public IDocumentRemoteRepository Documents =>
            Client.GetRequiredService<IDocumentRemoteRepository>();

        /// <summary>サーバーが要求を処理し終えるのを待つ</summary>
        public async Task WaitCompletedAsync()
        {
            (await Completed.WaitAsync(TimeSpan.FromSeconds(15), Ct))
                .Should()
                .BeTrue("サーバーが要求を処理し終える");
        }

        public async ValueTask DisposeAsync()
        {
            Raw.Dispose();
            await Client.DisposeAsync();
            await Server.DisposeAsync();
        }
    }

    /// <summary>ホストを起動する</summary>
    private async Task<Host> StartAsync(HostOptions options)
    {
        var log = new RecordingLoggerProvider();
        var hooks = new ConcurrentQueue<Exception>();
        var completed = new SemaphoreSlim(0);

        await _db.ApplyDdlAsync(BinaryFixtureDefinition.Build(), Ct);

        var server = await InProcessRemoteServer.StartAsync(
            services =>
            {
                services.AddGeneratedSqliteRepositories(_db.ReadWriteCreateConnectionString);

                if (options.SaveHook is not null)
                {
                    services.AddSingleton(options.SaveHook);
                }

                if (options.RequestTimeout is not null)
                {
                    services.AddRequestTimeouts();
                }
            },
            app =>
            {
                // 要求を処理し終えたことを、ほかのミドルウェアより外側で数える
                app.Use(
                    async (context, next) =>
                    {
                        try
                        {
                            await next(context);
                        }
                        finally
                        {
                            completed.Release();
                        }
                    }
                );
                options.Middleware?.Invoke(app);

                if (options.RequestTimeout is not null)
                {
                    app.UseRequestTimeouts();
                }

                var group = app.MapGeneratedRemoteEndpoints(RemoteAccess.AllowAnonymous);
                group.WithMetadata(
                    new RemoteErrorDetailPolicy(
                        false,
                        options.OnServerError ?? ((_, ex) => hooks.Enqueue(ex))
                    )
                );

                if (options.RequestTimeout is { } timeout)
                {
                    group.WithRequestTimeout(timeout);
                }

                options.Group?.Invoke(group);
            },
            Ct,
            builder =>
            {
                builder.Logging.AddProvider(log);
                builder.Logging.SetMinimumLevel(LogLevel.Debug);

                if (options.Http2)
                {
                    builder.WebHost.ConfigureKestrel(kestrel =>
                        kestrel.ConfigureEndpointDefaults(listen =>
                            listen.Protocols = HttpProtocols.Http2
                        )
                    );
                }
            }
        );

        var baseAddress = server.BaseAddress(RemotePaths.DefaultPrefix);
        var client = new ServiceCollection()
            .AddGeneratedHttpRemoteRepositories(_ =>
                options.Http2
                    ? new HttpClient(new Http2Handler())
                    {
                        BaseAddress = new Uri(baseAddress + "/"),
                    }
                    : new HttpClient { BaseAddress = new Uri(baseAddress + "/") }
            )
            .BuildServiceProvider();

        return new Host
        {
            Server = server,
            Client = client,
            Raw = new HttpClient { BaseAddress = new Uri(baseAddress + "/") },
            Log = log,
            Hooks = hooks,
            Completed = completed,
        };
    }

    /// <summary>文書 1 を payload つきで入れる（サーバーが処理し終えるのを待ち、その完了の数を使い切る）</summary>
    /// <remarks>
    /// 完了は最も外側のミドルウェアの finally で数えるので、クライアントが応答を受け取った後に数えられることがある。
    /// ここで待って使い切らないと、後で調べたい要求の完了を待つつもりの <see cref="Host.WaitCompletedAsync"/> が、
    /// この挿入の完了で先に戻ってしまう。
    /// </remarks>
    private static async Task InsertDocumentAsync(Host host, byte[] payload)
    {
        await host.Documents.InsertAsync(
            new DocumentEntity
            {
                DocumentId = 1,
                Title = "alpha",
                Payload = payload,
                Thumb = [],
            },
            Ct
        );
        await host.WaitCompletedAsync();
    }

    /// <summary>グラフ保存で挿入する文書を作る（新しいエンティティの既定の RowState は Unchanged なので Added にする）</summary>
    private static DocumentEntity NewDocument(int id, string title)
    {
        var document = new DocumentEntity
        {
            DocumentId = id,
            Title = title,
            Thumb = [],
        };
        document.MarkAdded();
        return document;
    }

    /// <summary>サーバーに残っている payload を読む</summary>
    private static async Task<byte[]> ReadPayloadAsync(Host host)
    {
        using var destination = new MemoryStream();
        (await host.Documents.ReadPayloadAsync(1, destination, Ct)).Should().BeTrue();
        return destination.ToArray();
    }

    /// <summary>サーバーのエンジンが Error で記録したログを出力へ書く（報告用）</summary>
    private void Describe(Host host, string label, Exception? failure, bool? result)
    {
        output.WriteLine(
            $"{label}: result={result?.ToString() ?? "-"} failure={failure?.GetType().Name ?? "-"}"
                + (failure is RemoteRepositoryException remote ? $"({remote.StatusCode})" : "")
                + $" message={failure?.Message}"
        );

        foreach (var entry in host.Log.Entries.Where(e => e.Level >= LogLevel.Warning))
        {
            output.WriteLine(
                $"  log {entry.Level} {entry.Category}: {entry.Exception?.GetType().FullName} {entry.Message}"
            );
        }

        foreach (var hook in host.Hooks)
        {
            output.WriteLine($"  hook {hook.GetType().FullName}: {hook.Message}");
        }
    }

    /// <remarks>
    /// クライアントは先頭を送った後に止まり、サーバーのタイムアウト（500ms）より後に残りを送る（遅いクライアント）。
    /// HTTP/1.1 のクライアントは本文を送り終えるまで応答を読まないので、送り続けて初めて 504 を受け取る。
    /// 取り消された本文の読み取りの後始末として Kestrel が「automatic draining of the request body failed」を Error で
    /// 記録するのは、RequestTimeouts と Kestrel の組み合わせの振る舞い（エンジンの外）なので、ここでは見ない。
    /// </remarks>
    [Fact(
        DisplayName = "[RemoteHost] タイムアウト: 本文の送信が止まった blob の書き込みは、成功と報告せず blob も変えない"
    )]
    public async Task RequestTimeout_StalledUpload_IsNotReportedAsWritten()
    {
        await using var host = await StartAsync(
            new HostOptions { RequestTimeout = TimeSpan.FromMilliseconds(500) }
        );
        await InsertDocumentAsync(host, [1, 2, 3]);

        using var limit = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        limit.CancelAfter(TimeSpan.FromSeconds(10));
        var body = new StallingStream(head: 64, TimeSpan.FromSeconds(2), total: 1_000);
        Exception? failure = null;
        bool? result = null;

        try
        {
            result = await host.Documents.WritePayloadAsync(1, body, 1_000, limit.Token);
        }
        catch (Exception ex)
        {
            failure = ex;
        }

        await host.WaitCompletedAsync();
        Describe(host, "stalled upload", failure, result);

        result.Should().NotBe(true, "書き込まれていない blob を成功と報告しない");
        failure
            .Should()
            .BeOfType<RemoteRepositoryException>(
                "サーバーのタイムアウトは 504 で答える（クライアントの待ちの上限には届かない）"
            )
            .Which.StatusCode.Should()
            .Be(504);
        (await ReadPayloadAsync(host)).Should().Equal([1, 2, 3]);
        host.Log.Engine(LogLevel.Error)
            .Should()
            .BeEmpty("サーバーのタイムアウトはサーバーの障害ではない");
        host.Hooks.Should().BeEmpty();
    }

    [Fact(DisplayName = "[RemoteHost] タイムアウト: 時間切れの JSON の要求は 504 で答える")]
    public async Task RequestTimeout_SlowSave_IsAnswered504()
    {
        await using var host = await StartAsync(
            new HostOptions
            {
                RequestTimeout = TimeSpan.FromMilliseconds(500),
                SaveHook = new DelayingHook(),
            }
        );

        Exception? failure = null;

        try
        {
            await host.Documents.SaveAsync(NewDocument(2, "slow"), cancellationToken: Ct);
        }
        catch (Exception ex)
        {
            failure = ex;
        }

        await host.WaitCompletedAsync();
        Describe(host, "slow save", failure, null);

        failure.Should().BeOfType<RemoteRepositoryException>().Which.StatusCode.Should().Be(504);
        host.Log.Engine(LogLevel.Error).Should().BeEmpty();
        host.Hooks.Should().BeEmpty();
    }

    [Fact(DisplayName = "[RemoteHost] タイムアウト: 時間内の要求は従来どおり成功する")]
    public async Task RequestTimeout_FastRequests_Succeed()
    {
        await using var host = await StartAsync(
            new HostOptions { RequestTimeout = TimeSpan.FromSeconds(30) }
        );
        await InsertDocumentAsync(host, [1, 2, 3]);

        (
            await host.Documents.WritePayloadAsync(
                1,
                new MemoryStream([4, 5, 6, 7]),
                cancellationToken: Ct
            )
        )
            .Should()
            .BeTrue();
        (await ReadPayloadAsync(host)).Should().Equal([4, 5, 6, 7]);
    }

    /// <summary>Before で、要求のトークンが取り消されるまで待つ Save フック</summary>
    private sealed class DelayingHook : ISaveHook<DocumentEntity>
    {
        public async Task<bool> BeforeSaveAsync(
            DocumentEntity entity,
            SaveOperation operation,
            CancellationToken cancellationToken = default
        )
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return true;
        }
    }

    [Theory(
        DisplayName = "[RemoteHost] バッファリング: EnableBuffering のホストでも blob のアップロードが通る"
    )]
    [InlineData(0)]
    [InlineData(8)]
    [InlineData(int.MaxValue)]
    public async Task EnableBuffering_Upload_Succeeds(int readBefore)
    {
        await using var host = await StartAsync(
            new HostOptions
            {
                Middleware = app =>
                    app.Use(
                        async (context, next) =>
                        {
                            context.Request.EnableBuffering();

                            if (readBefore > 0)
                            {
                                // 先頭（または全部）を読んでから、先頭へ戻す（署名の検査やログの典型の形）
                                var buffer = new byte[8192];
                                var read = 0;

                                while (read < readBefore)
                                {
                                    var n = await context.Request.Body.ReadAsync(
                                        buffer.AsMemory(
                                            0,
                                            Math.Min(buffer.Length, readBefore - read)
                                        )
                                    );

                                    if (n == 0)
                                    {
                                        break;
                                    }

                                    read += n;
                                }

                                context.Request.Body.Position = 0;
                            }

                            await next(context);
                        }
                    ),
            }
        );
        await InsertDocumentAsync(host, [1, 2, 3]);
        var data = Enumerable.Range(0, 10_000).Select(i => (byte)i).ToArray();
        Exception? failure = null;
        bool? result = null;

        try
        {
            result = await host.Documents.WritePayloadAsync(
                1,
                new MemoryStream(data),
                cancellationToken: Ct
            );
        }
        catch (Exception ex)
        {
            failure = ex;
        }

        Describe(host, $"buffered upload (read {readBefore})", failure, result);

        result.Should().BeTrue();
        (await ReadPayloadAsync(host)).Should().Equal(data);
    }

    [Fact(DisplayName = "[RemoteHost] HTTP/2: blob の読み書きが往復する")]
    public async Task Http2_BlobRoundTrips()
    {
        await using var host = await StartAsync(new HostOptions { Http2 = true });
        await InsertDocumentAsync(host, [1, 2, 3]);

        (
            await host.Documents.WritePayloadAsync(
                1,
                new MemoryStream([9, 8, 7]),
                cancellationToken: Ct
            )
        )
            .Should()
            .BeTrue();
        (await ReadPayloadAsync(host)).Should().Equal([9, 8, 7]);
    }

    /// <summary>HTTP/2 で本文の途中にクライアントが取り消した要求は、分類の前に要求のトークンが立っていれば障害として記録しない。</summary>
    /// <remarks>
    /// HTTP/2 のストリームのリセットは、本文の読み取りで型を持たない <see cref="IOException"/>（「The client reset the
    /// request stream.」・InnerException なし）として上がり、型では切断と見分けられない。切断と分かるのは、分類の時点で
    /// 要求のトークンが立っているときだけ。Kestrel がトークンを立てる時期は負荷で変わるので、要求の寿命を差し替え、
    /// 本文の読み取りが投げた直後にトークンを立てて「分類の前にトークンが立っている」状態を決定的に作る（区切り 21 の
    /// Early の形）。トークンが立つ前に分類される側は、型を持たないこの限界を見送ったので（docs の既知の限界）作らない。
    /// </remarks>
    [Fact(
        DisplayName = "[RemoteHost] HTTP/2: 本文の途中でクライアントが取り消した要求は、サーバーの障害として記録しない"
    )]
    public async Task Http2_ClientResetMidBody_IsNotAFault()
    {
        var thrown = new ConcurrentQueue<Exception>();
        await using var host = await StartAsync(
            new HostOptions
            {
                Http2 = true,
                Middleware = app =>
                    app.Use(
                        (context, next) =>
                        {
                            var lifetime = new EarlyAbortLifetime(
                                context.Features.GetRequiredFeature<IHttpRequestLifetimeFeature>()
                            );
                            context.Features.Set<IHttpRequestLifetimeFeature>(lifetime);
                            context.Request.Body = new RecordingBody(
                                context.Request.Body,
                                thrown,
                                lifetime.SignalHostKnows
                            );
                            return next(context);
                        }
                    ),
            }
        );
        await InsertDocumentAsync(host, [1, 2, 3]);

        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        var body = new StallingStream(head: 64);
        var upload = host.Documents.WritePayloadAsync(1, body, 1_000, cancel.Token);
        await body.HeadSent.Task.WaitAsync(TimeSpan.FromSeconds(10), Ct);
        await Task.Delay(300, Ct);
        await cancel.CancelAsync();

        Exception? failure = null;

        try
        {
            await upload;
        }
        catch (Exception ex)
        {
            failure = ex;
        }

        await host.WaitCompletedAsync();
        Describe(host, "http2 reset", failure, null);

        foreach (var ex in thrown)
        {
            output.WriteLine($"  body read threw: {Chain(ex)}");
        }

        thrown
            .Should()
            .ContainSingle("本文の読み取りがリセットで失敗した")
            .Which.Should()
            .BeOfType<IOException>("HTTP/2 のリセットは型を持たない IOException で来る");
        host.Log.Engine(LogLevel.Error).Should().BeEmpty();
        host.Hooks.Should().BeEmpty();
        (await ReadPayloadAsync(host)).Should().Equal([1, 2, 3]);
    }

    /// <summary>例外の鎖を型名で並べる</summary>
    private static string Chain(Exception ex)
    {
        var names = new System.Collections.Generic.List<string>();

        for (Exception? current = ex; current is not null; current = current.InnerException)
        {
            names.Add($"{current.GetType().FullName}(\"{current.Message}\")");
        }

        return string.Join(" <- ", names);
    }

    [Fact(
        DisplayName = "[RemoteHost] フック: 500 のフックが投げても、500 の本文（相関 ID つき）は届き、元の例外は記録される"
    )]
    public async Task ThrowingServerErrorHook_StillWritesTheErrorBody()
    {
        await using var host = await StartAsync(
            new HostOptions
            {
                SaveHook = new FailingHook(),
                OnServerError = (_, _) => throw new InvalidOperationException("hook failed"),
            }
        );

        Exception? failure = null;

        try
        {
            await host.Documents.SaveAsync(NewDocument(3, "boom"), cancellationToken: Ct);
        }
        catch (Exception ex)
        {
            failure = ex;
        }

        await host.WaitCompletedAsync();
        Describe(host, "throwing hook", failure, null);

        var remote = failure.Should().BeOfType<RemoteRepositoryException>().Subject;
        remote.StatusCode.Should().Be(500);
        remote.CorrelationId.Should().NotBeNullOrEmpty("500 の本文が書かれている");
        host.Log.Engine(LogLevel.Error)
            .Select(entry => entry.Exception?.Message)
            .Should()
            .Contain("save failed");
    }

    /// <summary>Before で投げる Save フック（500 を起こす）</summary>
    private sealed class FailingHook : ISaveHook<DocumentEntity>
    {
        public Task<bool> BeforeSaveAsync(
            DocumentEntity entity,
            SaveOperation operation,
            CancellationToken cancellationToken = default
        ) => throw new InvalidOperationException("save failed");
    }

    [Fact(DisplayName = "[RemoteHost] キー: 長さの無い PUT でも、読めないキーは 400 で答える")]
    public async Task ChunkedUploadWithUnreadableKey_IsAnswered400()
    {
        await using var host = await StartAsync(new HostOptions());

        using var content = new StreamContent(new ForwardOnlyStream([1, 2, 3]));
        using var response = await host.Raw.PutAsync("Document/Payload?id=notjson", content, Ct);
        output.WriteLine($"chunked + bad key: {(int)response.StatusCode}");

        content.Headers.ContentLength.Should().BeNull("長さを宣言しない（chunked）要求として送る");
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact(DisplayName = "[RemoteHost] キー: 上限を超える PUT でも、読めないキーは 400 で答える")]
    public async Task OversizedUploadWithUnreadableKey_IsAnswered400()
    {
        await using var host = await StartAsync(
            new HostOptions { Group = group => group.WithMetadata(new SizeLimit(1_000)) }
        );

        using var request = new HttpRequestMessage(HttpMethod.Put, "Document/Payload?id=notjson")
        {
            Content = new ByteArrayContent(new byte[2_000]),
        };
        request.Headers.ExpectContinue = true;
        using var response = await host.Raw.SendAsync(request, Ct);
        output.WriteLine($"oversized + bad key: {(int)response.StatusCode}");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact(
        DisplayName = "[RemoteHost] 上限: 解除のオプトインをしないグループへ付けた上限は、blob の PUT に効く"
    )]
    public async Task GroupSizeLimit_WithoutOptIn_CapsTheUpload()
    {
        await using var host = await StartAsync(
            new HostOptions { Group = group => group.WithMetadata(new SizeLimit(1_000)) }
        );
        await InsertDocumentAsync(host, [1, 2, 3]);

        using var request = new HttpRequestMessage(HttpMethod.Put, "Document/Payload?id=1")
        {
            Content = new ByteArrayContent(new byte[2_000]),
        };
        request.Headers.ExpectContinue = true;
        using var response = await host.Raw.SendAsync(request, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge);
        (await ReadPayloadAsync(host)).Should().Equal([1, 2, 3]);
    }
}
