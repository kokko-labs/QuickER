using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using QuickER.Sqlite;
using QuickER.Tests.GeneratedBinaryFixture;
using QuickER.Tests.Integration;
using Xunit;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// 無制限バイナリ除外の生成物を、実 HTTP（Kestrel を 127.0.0.1 の空きポートで in-process 起動）＋実 SQLite の
/// 3 階層構成で検証する。サーバー実体はQuickER の <c>SqliteRepository</c>（除外が効く側）で、クライアントは
/// 生成された HTTP リモート実装のみを使う。
/// </summary>
/// <remarks>
/// 柱: (1) クライアント経由の Insert→GetById でも除外列は未取得状態で返る、(2) 除外列に値を残した UpdateAsync は
/// <b>HTTP 送信前</b>にクライアント側ガードで弾かれる、(3) 射影（GetPayloads）は除外列 payload を Base64 経由で取れる。
/// </remarks>
[Trait("Category", "Integration")]
public sealed class BinaryColumnRemoteRuntimeTests : IAsyncLifetime
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private readonly SqliteTempDatabase _db = SqliteTempDatabase.Create();
    private WebApplication? _app;
    private ServiceProvider? _clientProvider;

    private static readonly byte[] Doc1Payload = [1, 2, 3, 4];
    private static readonly byte[] Doc1Thumb = [9, 9];

    /// <summary>スキーマ作成 → Kestrel 起動（空きポート・サーバー実体はQuickER の SqliteRepository）→ HTTP クライアント DI 構築</summary>
    public async ValueTask InitializeAsync()
    {
        var ddl = new SqliteDdlGenerator().Build(BinaryFixtureDefinition.Build());
        await _db.ApplyDdlAsync(ddl, Ct);

        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddGeneratedSqliteRepositories(_db.ReadWriteCreateConnectionString);

        _app = builder.Build();
        _app.MapGeneratedRemoteEndpoints();
        await _app.StartAsync(Ct);

        var baseUrl = _app.Urls.First();
        _clientProvider = new ServiceCollection()
            .AddGeneratedHttpRemoteRepositories($"{baseUrl}/quicker")
            .BuildServiceProvider();
    }

    /// <summary>クライアント側の文書リモート面を解決する</summary>
    private IDocumentRemoteRepository Documents =>
        _clientProvider!.GetRequiredService<IDocumentRemoteRepository>();

    /// <summary>1. クライアント経由の Insert→GetById でも除外列は未取得状態で返る</summary>
    [Fact(DisplayName = "[Binary/Remote] 1: Insert→GetById で除外列は未取得状態で返る")]
    public async Task Crud_ExcludesUnboundedBinaryOverHttp()
    {
        await Documents.InsertAsync(
            new DocumentEntity
            {
                DocumentId = 1,
                Title = "alpha",
                Payload = Doc1Payload,
                Thumb = Doc1Thumb,
                Checksum = [7, 7],
            },
            Ct
        );

        var loaded = await Documents.GetByIdAsync(1, Ct);
        loaded.Should().NotBeNull();
        loaded!.Title.Should().Be("alpha");
        loaded.Payload.Should().BeNull("除外列 payload はサーバーの SELECT で外れる");
        loaded.Thumb.Should().BeEmpty("除外列 thumb はサーバーの SELECT で外れる");
        loaded.Checksum.Should().Equal([7, 7], "有界バイナリ checksum は除外対象外＝取れる");
    }

    /// <summary>2. 除外列に値を残した UpdateAsync は HTTP 送信前にクライアント側ガードで弾かれる</summary>
    [Fact(DisplayName = "[Binary/Remote] 2: 除外列非空の UpdateAsync は送信前ガードで例外になる")]
    public async Task Update_WithAssignedExcludedColumn_ThrowsBeforeSend()
    {
        await Documents.InsertAsync(
            new DocumentEntity
            {
                DocumentId = 1,
                Title = "alpha",
                Thumb = Doc1Thumb,
            },
            Ct
        );

        // サーバーを止めても例外になる＝HTTP を投げる前にクライアント側ガードで弾かれていることを示す
        await _app!.StopAsync(Ct);

        var doc = new DocumentEntity
        {
            DocumentId = 1,
            Title = "alpha",
            Thumb = Doc1Thumb,
            Payload = [5, 5],
        };

        var act = () => Documents.UpdateAsync(doc, Ct);

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .Which.Message.Should()
            .Contain("Payload");
    }

    /// <summary>3. 射影（GetPayloads）は除外列 payload を Base64 経由で取得できる</summary>
    [Fact(DisplayName = "[Binary/Remote] 3: 射影は除外列 payload を HTTP 越しに取得できる")]
    public async Task Projection_TransfersExcludedColumnOverHttp()
    {
        await Documents.InsertAsync(
            new DocumentEntity
            {
                DocumentId = 1,
                Title = "alpha",
                Payload = Doc1Payload,
                Thumb = Doc1Thumb,
            },
            Ct
        );

        var rows = await Documents.GetPayloadsAsync(Ct);
        rows.Should().ContainSingle();
        rows[0]
            .Payload.Should()
            .Equal(Doc1Payload, "射影は除外列を SELECT に含め Base64 で転送する");
    }

    // ── Stream アクセサのリモート転送（GET/PUT/DELETE の専用バイナリエンドポイント）──

    /// <summary>行を 1 件挿入する（除外列は INSERT で全列書き込みのため payload/thumb もここで入る）</summary>
    private async Task SeedAsync(int id, byte[]? payload, byte[] thumb)
    {
        await Documents.InsertAsync(
            new DocumentEntity
            {
                DocumentId = id,
                Title = $"doc-{id}",
                Payload = payload,
                Thumb = thumb,
            },
            Ct
        );
    }

    /// <summary>4. クライアント経由で数 MB を Write→Read 往復し一致する（ファイル糖衣でも 1 往復）</summary>
    [Fact(
        DisplayName = "[Binary/Remote] 4: 数 MB の Write→Read 往復が一致（Stream・ファイル糖衣）"
    )]
    public async Task Stream_WriteThenRead_RoundTripsLargeData()
    {
        await SeedAsync(1, null, Doc1Thumb);

        var large = new byte[3 * 1024 * 1024];
        new Random(20260715).NextBytes(large);

        // Stream 版の往復
        (await Documents.WritePayloadAsync(1, new MemoryStream(large), cancellationToken: Ct))
            .Should()
            .BeTrue();

        using var destination = new MemoryStream();
        (await Documents.ReadPayloadAsync(1, destination, Ct)).Should().BeTrue();
        destination.ToArray().Should().Equal(large, "HTTP 越しの Stream 往復で blob が一致する");

        // ファイル糖衣の往復（拡張メソッドはリモート面を対象にするため HTTP クライアントでも動く）
        var sourcePath = Path.GetTempFileName();
        var destinationPath = Path.GetTempFileName();

        try
        {
            await File.WriteAllBytesAsync(sourcePath, large, Ct);
            (await Documents.WritePayloadFromFileAsync(1, sourcePath, Ct)).Should().BeTrue();
            (await Documents.ReadPayloadToFileAsync(1, destinationPath, Ct)).Should().BeTrue();
            (await File.ReadAllBytesAsync(destinationPath, Ct))
                .Should()
                .Equal(large, "ファイル糖衣でも往復で一致する");
        }
        finally
        {
            File.Delete(sourcePath);
            File.Delete(destinationPath);
        }
    }

    /// <summary>5. 行なし/NULL の Read は false（HTTP 404 経由）・行なしの Write/Delete も false</summary>
    [Fact(DisplayName = "[Binary/Remote] 5: 行なし/NULL の Read/Write/Delete は false（404 経由）")]
    public async Task Stream_MissingRowOrNull_ReturnsFalse()
    {
        await SeedAsync(1, null, Doc1Thumb);

        // 行なしの Read
        using var noRow = new MemoryStream();
        (await Documents.ReadPayloadAsync(999, noRow, Ct)).Should().BeFalse("行なしは 404→false");

        // 列 NULL の Read（payload を NULL で挿入した行）
        using var nullColumn = new MemoryStream();
        (await Documents.ReadPayloadAsync(1, nullColumn, Ct))
            .Should()
            .BeFalse("列 NULL は 404→false");
        nullColumn.Length.Should().Be(0, "false のとき宛先へ何も書かない");

        // 行なしの Write / Delete
        (
            await Documents.WritePayloadAsync(
                999,
                new MemoryStream([1, 2, 3]),
                cancellationToken: Ct
            )
        )
            .Should()
            .BeFalse("行なしの Write は 404→false");
        (await Documents.WritePayloadAsync(999, null, cancellationToken: Ct))
            .Should()
            .BeFalse("行なしの Delete は 404→false");
    }

    /// <summary>6. DELETE（source=null）で DB が NULL になる（行は残る）</summary>
    [Fact(DisplayName = "[Binary/Remote] 6: DELETE（source=null）で列が NULL になる")]
    public async Task Stream_Delete_SetsColumnNull()
    {
        await SeedAsync(1, Doc1Payload, Doc1Thumb);

        // 事前: payload は読める
        using (var before = new MemoryStream())
        {
            (await Documents.ReadPayloadAsync(1, before, Ct)).Should().BeTrue();
        }

        // DELETE で NULL 化
        (await Documents.WritePayloadAsync(1, null, cancellationToken: Ct))
            .Should()
            .BeTrue();

        // 事後: Read は false（NULL）だが行は残っている
        using var after = new MemoryStream();
        (await Documents.ReadPayloadAsync(1, after, Ct)).Should().BeFalse("DELETE 後は列 NULL");
        (await Documents.GetByIdAsync(1, Ct)).Should().NotBeNull("行自体は削除されない");
    }

    /// <summary>7. PUT 空ボディ（空 MemoryStream）は 0 バイト書き込み（NULL と区別される）</summary>
    [Fact(DisplayName = "[Binary/Remote] 7: PUT 空ボディは 0 バイト書き込み（NULL と区別）")]
    public async Task Stream_EmptyBody_WritesZeroBytesNotNull()
    {
        await SeedAsync(1, Doc1Payload, Doc1Thumb);

        // 空 Stream の PUT ＝ 0 バイトの blob（NULL 化ではない）
        (await Documents.WritePayloadAsync(1, new MemoryStream([]), cancellationToken: Ct))
            .Should()
            .BeTrue();

        using var destination = new MemoryStream();
        (await Documents.ReadPayloadAsync(1, destination, Ct))
            .Should()
            .BeTrue("0 バイトの blob は存在する（true）＝NULL（false）と区別される");
        destination.Length.Should().Be(0, "書き込んだ blob は空");
    }

    /// <summary>8. 非シーク Stream＋length なしはクライアント側で送信前に ArgumentException</summary>
    [Fact(
        DisplayName = "[Binary/Remote] 8: 非シーク Stream＋length なしは送信前に ArgumentException"
    )]
    public async Task Stream_NonSeekableWithoutLength_ThrowsBeforeSend()
    {
        await SeedAsync(1, null, Doc1Thumb);

        // サーバーを止めても例外になる＝HTTP を投げる前にクライアント側の長さ検証で弾かれている
        await _app!.StopAsync(Ct);

        var act = () =>
            Documents.WritePayloadAsync(1, new NonSeekableStream([1, 2, 3]), cancellationToken: Ct);

        (await act.Should().ThrowAsync<ArgumentException>())
            .Which.Message.Should()
            .Contain("length");
    }

    /// <summary>
    /// 9. バイナリ PUT はリクエストサイズ制限が解除されている（既定 30MB 超の 31MB を実転送して成功）。
    /// 既定の Kestrel 上限（約 30MB）を超える転送が 413 にならず往復することで、メタデータ解除が実効していることを示す。
    /// </summary>
    [Fact(
        DisplayName = "[Binary/Remote] 9: バイナリ PUT は 31MB（>30MB 既定）でも成功＝サイズ制限解除"
    )]
    public async Task Stream_Put_ExceedsDefaultRequestSizeLimit()
    {
        await SeedAsync(1, null, Doc1Thumb);

        // 既定の Kestrel MaxRequestBodySize（30_000_000）を確実に超える 31MiB
        var payload = new byte[31 * 1024 * 1024];
        new Random(31).NextBytes(payload);

        (await Documents.WritePayloadAsync(1, new MemoryStream(payload), cancellationToken: Ct))
            .Should()
            .BeTrue("サイズ制限解除メタデータにより 30MB 超の PUT も受理される");

        using var destination = new MemoryStream();
        (await Documents.ReadPayloadAsync(1, destination, Ct)).Should().BeTrue();
        destination.Length.Should().Be(payload.Length, "31MB が往復する");
        destination.ToArray()[^1].Should().Be(payload[^1], "末尾バイトまで転送される");
    }

    /// <summary>
    /// 10. Thumb 列（第 2 の除外列）も HTTP 越しに Write→Read 往復し、再書き込みで全置換される。
    /// Thumb は非 nullable 列（BinaryFixtureDefinition の設計）のため SET NULL（DELETE）は制約違反となる。
    /// NULL 化系の検証は nullable な Payload 側のテストが担う。
    /// </summary>
    [Fact(DisplayName = "[Binary/Remote] 10: Thumb 列の Write→Read 往復と再書き込みによる全置換")]
    public async Task Stream_Thumb_WriteReadOverwriteRoundTrips()
    {
        await SeedAsync(1, null, Doc1Thumb);

        var data = new byte[64 * 1024];
        new Random(20260719).NextBytes(data);

        // Thumb 列（Payload とは別の除外列）へ Stream で書き込み、読み戻して一致する
        (await Documents.WriteThumbAsync(1, new MemoryStream(data), cancellationToken: Ct))
            .Should()
            .BeTrue();

        using var destination = new MemoryStream();
        (await Documents.ReadThumbAsync(1, destination, Ct)).Should().BeTrue();
        destination.ToArray().Should().Equal(data, "Thumb 列も HTTP 越しの Stream 往復で一致する");

        // より小さいデータで再書き込み → 旧データが残らず全置換される
        var smaller = new byte[1024];
        new Random(20260720).NextBytes(smaller);
        (await Documents.WriteThumbAsync(1, new MemoryStream(smaller), cancellationToken: Ct))
            .Should()
            .BeTrue();

        using var after = new MemoryStream();
        (await Documents.ReadThumbAsync(1, after, Ct)).Should().BeTrue();
        after.ToArray().Should().Equal(smaller, "再書き込みは旧データを残さず全置換する");
    }

    /// <summary>11. バイナリ図の名前付きクエリ（GetByTitle・CountWithPayload）が HTTP 越しに機能する</summary>
    [Fact(
        DisplayName = "[Binary/Remote] 11: 名前付きクエリ（GetByTitle・CountWithPayload）が HTTP 越しに機能する"
    )]
    public async Task NamedQueries_WorkOverHttp()
    {
        await SeedAsync(1, Doc1Payload, Doc1Thumb); // payload あり
        await SeedAsync(2, null, Doc1Thumb); // payload なし

        // タイトル完全一致（除外列 payload はサーバー SELECT で外れる）
        var byTitle = await Documents.GetByTitleAsync("doc-1", Ct);
        byTitle.Should().ContainSingle();
        byTitle[0].DocumentId.Should().Be(1);
        byTitle[0]
            .Payload.Should()
            .BeNull("GetByTitle でも除外列 payload はサーバーの SELECT で外れる");

        // payload が存在する文書の件数（WHERE は除外列を参照できる）＝ doc-1 のみ
        (await Documents.CountWithPayloadAsync(Ct))
            .Should()
            .Be(1);
    }

    /// <summary>
    /// 12. 生成サーバーのバイナリ PUT は Content-Length 欠落（chunked 転送）で 411 を返す。生成クライアントは送信前に弾く
    /// （length 必須）ため、素の <see cref="HttpClient"/> で chunked PUT を直接送り、サーバー側の 411 分岐を観測する。
    /// </summary>
    [Fact(
        DisplayName = "[Binary/Remote] 12: chunked PUT（Content-Length 欠落）はサーバーが 411 を返す"
    )]
    public async Task Stream_ChunkedPut_ReturnsLengthRequired()
    {
        await SeedAsync(1, null, Doc1Thumb);

        var baseUrl = _app!.Urls.First();

        using var raw = new HttpClient();
        using var content = new StreamContent(new NonSeekableStream([1, 2, 3]))
        {
            Headers = { ContentType = new MediaTypeHeaderValue("application/octet-stream") },
        };

        // 生成クライアント（Http{Entity}RemoteRepository）は非シーク＋length なしを送信前に弾くため、素の HttpClient で送る。
        // TransferEncoding: chunked を明示し Content-Length を送らない＝サーバーの 411 分岐（length is null）を踏む
        using var request = new HttpRequestMessage(
            HttpMethod.Put,
            $"{baseUrl}/quicker/Document/Payload?id=1"
        )
        {
            Content = content,
        };
        request.Headers.TransferEncodingChunked = true;

        using var response = await raw.SendAsync(request, Ct);

        response
            .StatusCode.Should()
            .Be(
                HttpStatusCode.LengthRequired,
                "Content-Length 欠落（chunked）の PUT はサーバーが 411 を返す"
            );

        // 411 なので DB は変化せず、payload は依然 NULL（Read は false）
        using var afterPut = new MemoryStream();
        (await Documents.ReadPayloadAsync(1, afterPut, Ct))
            .Should()
            .BeFalse("411 で拒否されたため payload は書き込まれない");
    }

    /// <summary>CanSeek でない Stream（length を渡さないと長さ不明）＝クライアント側検証の再現用</summary>
    private sealed class NonSeekableStream(byte[] data) : Stream
    {
        private readonly MemoryStream _inner = new(data);

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            _inner.Read(buffer, offset, count);

        public override void Flush() { }

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _inner.Dispose();
            }

            base.Dispose(disposing);
        }
    }

    /// <summary>使い終えたクライアント DI・サーバー・一時 DB を破棄する</summary>
    public async ValueTask DisposeAsync()
    {
        _clientProvider?.Dispose();

        if (_app is not null)
        {
            await _app.DisposeAsync();
        }

        _db.Dispose();
    }
}
