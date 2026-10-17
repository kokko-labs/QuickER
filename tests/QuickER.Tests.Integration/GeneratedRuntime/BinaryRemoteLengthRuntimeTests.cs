using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using QuickER.Tests.GeneratedBinaryFixture;
using QuickER.Tests.Integration;
using Xunit;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// リモートの blob の書き込みも、直結と同じく Stream がちょうど <c>length</c> バイトを返すことを求め、
/// 違えば <see cref="ArgumentException"/>（ParamName=length）で失敗してサーバーの blob を変えないことを固定する。
/// </summary>
/// <remarks>
/// 長すぎる Stream は、宣言した長さの本文を送り終えてからでは間に合わない（サーバーは受け取った分で書き込みを
/// 確定する）。そのため長さの検査（<c>UnboundedBinaryColumns.WithExactLength</c>）は、最後のかたまりを返す前に
/// 1 バイト先を読んで、長すぎる Stream をその時点で断る。長さちょうどの Stream では、その先読みが終端（0 バイト）を
/// 見るだけで、呼び出し側の Stream から余分なバイトを読まない。
/// </remarks>
[Trait("Category", "Integration")]
public sealed class BinaryRemoteLengthRuntimeTests : IAsyncLifetime
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly byte[] Stored = [7, 7, 7];

    private readonly SqliteTempDatabase _db = SqliteTempDatabase.Create();
    private InProcessRemoteServer? _server;
    private ServiceProvider? _client;

    public async ValueTask InitializeAsync()
    {
        await _db.ApplyDdlAsync(BinaryFixtureDefinition.Build(), Ct);
        _server = await InProcessRemoteServer.StartAsync(
            services =>
                services.AddGeneratedSqliteRepositories(_db.ReadWriteCreateConnectionString),
            app => app.MapGeneratedRemoteEndpoints(RemoteAccess.AllowAnonymous),
            Ct
        );
        _client = new ServiceCollection()
            .AddGeneratedHttpRemoteRepositories(_server.BaseAddress(RemotePaths.DefaultPrefix))
            .BuildServiceProvider();

        await Documents.InsertAsync(
            new DocumentEntity
            {
                DocumentId = 1,
                Title = "alpha",
                Payload = Stored,
                Thumb = [1],
            },
            Ct
        );
    }

    public async ValueTask DisposeAsync()
    {
        if (_client is not null)
        {
            await _client.DisposeAsync();
        }

        if (_server is not null)
        {
            await _server.DisposeAsync();
        }

        _db.Dispose();
    }

    private IDocumentRemoteRepository Documents =>
        _client!.GetRequiredService<IDocumentRemoteRepository>();

    private async Task<byte[]> ReadPayloadAsync()
    {
        var destination = new MemoryStream();
        (await Documents.ReadPayloadAsync(1, destination, Ct)).Should().BeTrue();
        return destination.ToArray();
    }

    [Fact(
        DisplayName = "[Binary/Remote] Stream が length どおりのバイト数を出さない書き込みは ArgumentException（length）で失敗し、blob を変えない"
    )]
    public async Task Write_LengthMismatch_ThrowsAndKeepsBlob()
    {
        foreach (var (label, source, length) in BinaryLengthMismatchCases.Create())
        {
            var act = () => Documents.WritePayloadAsync(1, source, length, Ct);

            (await act.Should().ThrowAsync<ArgumentException>(label))
                .Which.ParamName.Should()
                .Be("length", label);
            (await ReadPayloadAsync()).Should().Equal(Stored, label);
        }
    }

    [Fact(
        DisplayName = "[Binary/Remote] 長さちょうどのシークできない Stream は書き込めて、余分なバイトを読まない"
    )]
    public async Task Write_ExactNonSeekable_SucceedsWithoutReadingPastTheEnd()
    {
        var source = new CountingNonSeekableStream([4, 5, 6, 7, 8]);

        (await Documents.WritePayloadAsync(1, source, 5, Ct)).Should().BeTrue();

        (await ReadPayloadAsync()).Should().Equal(4, 5, 6, 7, 8);
        source.BytesRead.Should().Be(5, "終端の確認は 0 バイトの読み取りで、データを読まない");
    }

    [Fact(
        DisplayName = "[Binary/Remote] 転送されて本文を送り直すときは、シークできる Stream を最初の送信の開始位置へ戻して送る"
    )]
    public async Task Write_SeekableStream_IsResentFromItsStartAfterRedirect()
    {
        // 本文を読み切ってから、同じ要求を本物のエンドポイントへ 307 で転送する中継（本文は 2 回送られる）
        await using var server = await InProcessRemoteServer.StartAsync(
            services =>
                services.AddGeneratedSqliteRepositories(_db.ReadWriteCreateConnectionString),
            app =>
            {
                app.Use(
                    async (context, next) =>
                    {
                        if (context.Request.Path.StartsWithSegments("/moved", out var rest))
                        {
                            await context.Request.Body.CopyToAsync(Stream.Null);
                            context.Response.StatusCode = StatusCodes.Status307TemporaryRedirect;
                            context.Response.Headers.Location =
                                $"{RemotePaths.DefaultPrefix}{rest}{context.Request.QueryString}";
                            return;
                        }

                        await next();
                    }
                );
                app.MapGeneratedRemoteEndpoints(RemoteAccess.AllowAnonymous);
            },
            Ct
        );
        await using var client = new ServiceCollection()
            .AddGeneratedHttpRemoteRepositories(server.BaseAddress("/moved"))
            .BuildServiceProvider();
        var source = new MemoryStream([9, 1, 2, 3]) { Position = 1 };

        (
            await client
                .GetRequiredService<IDocumentRemoteRepository>()
                .WritePayloadAsync(1, source, cancellationToken: Ct)
        )
            .Should()
            .BeTrue();

        (await ReadPayloadAsync()).Should().Equal(1, 2, 3);
    }

    [Theory(
        DisplayName = "長さの検査は長すぎる Stream を最後のかたまりを渡す前に断り、長さちょうどなら終端の 0 だけを見る"
    )]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WithExactLength_RejectsTooLongBeforeTheLastChunk(bool synchronous)
    {
        // 同期の Read と非同期の ReadAsync は別の実装なので、両方を通す
        Task Copy(Stream source, long length, Stream destination)
        {
            var checkedSource = UnboundedBinaryColumns.WithExactLength(source, length);

            if (synchronous)
            {
                checkedSource.CopyTo(destination);
                return Task.CompletedTask;
            }

            return checkedSource.CopyToAsync(destination, Ct);
        }

        var tooLong = new MemoryStream(new byte[20]);
        var received = new MemoryStream();

        var act = () => Copy(tooLong, 10, received);

        (await act.Should().ThrowAsync<ArgumentException>()).Which.ParamName.Should().Be("length");
        received.Length.Should().BeLessThan(10, "宣言した長さを渡し切る前に断る");

        var exact = new CountingNonSeekableStream([1, 2, 3]);
        var copied = new MemoryStream();
        await Copy(exact, 3, copied);
        copied.ToArray().Should().Equal(1, 2, 3);
        exact.BytesRead.Should().Be(3);
        exact.ReadCalls.Should().BeGreaterThan(1, "終端を確かめる読み取りがある");
    }

    /// <summary>読んだバイト数と読み取りの回数を数える、シークできない読み取り専用ストリーム</summary>
    private sealed class CountingNonSeekableStream(byte[] data) : Stream
    {
        private readonly MemoryStream _inner = new(data);

        public long BytesRead { get; private set; }

        public int ReadCalls { get; private set; }

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
            ReadCalls++;
            var read = _inner.Read(buffer, offset, count);
            BytesRead += read;
            return read;
        }

        public override void Flush() { }

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();
    }
}
