using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
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
    private InProcessRemoteServer? _server;
    private ServiceProvider? _clientProvider;

    private static readonly byte[] Doc1Payload = [1, 2, 3, 4];
    private static readonly byte[] Doc1Thumb = [9, 9];

    /// <summary>スキーマ作成 → Kestrel 起動（空きポート・サーバー実体はQuickER の SqliteRepository）→ HTTP クライアント DI 構築</summary>
    public async ValueTask InitializeAsync()
    {
        await _db.ApplyDdlAsync(BinaryFixtureDefinition.Build(), Ct);

        _server = await InProcessRemoteServer.StartAsync(
            services =>
                services.AddGeneratedSqliteRepositories(_db.ReadWriteCreateConnectionString),
            app => app.MapGeneratedRemoteEndpoints(),
            Ct
        );

        _clientProvider = new ServiceCollection()
            .AddGeneratedHttpRemoteRepositories(_server.BaseAddress(RemotePaths.DefaultPrefix))
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
        await _server!.StopAsync(Ct);

        var doc = new DocumentEntity
        {
            DocumentId = 1,
            Title = "alpha",
            Thumb = Doc1Thumb,
            Payload = [5, 5],
        };

        var act = () => Documents.UpdateAsync(doc, cancellationToken: Ct);

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
        await _server!.StopAsync(Ct);

        var act = () =>
            Documents.WritePayloadAsync(1, new NonSeekableStream([1, 2, 3]), cancellationToken: Ct);

        (await act.Should().ThrowAsync<ArgumentException>())
            .Which.Message.Should()
            .Contain("length");
    }

    /// <summary>
    /// 9. <c>allowUnboundedUploads: true</c> でマップしたグループのバイナリ PUT はリクエストサイズ制限が
    /// 解除されている（既定 30MB 超の 31MB を実転送して成功）。
    /// </summary>
    /// <remarks>
    /// 解除は<b>オプトイン</b>なので、共有サーバー（既定＝解除なし）ではなくこのテスト専用のサーバーを立てる。
    /// 既定側の対照は <see cref="Stream_Put_WithoutOptIn_IsRejectedByDefaultRequestSizeLimit"/>。
    /// </remarks>
    [Fact(
        DisplayName = "[Binary/Remote] 9: allowUnboundedUploads:true のバイナリ PUT は 31MB（>30MB 既定）でも成功"
    )]
    public async Task Stream_Put_WithOptIn_ExceedsDefaultRequestSizeLimit()
    {
        await using var server = await InProcessRemoteServer.StartAsync(
            services =>
                services.AddGeneratedSqliteRepositories(_db.ReadWriteCreateConnectionString),
            app => app.MapGeneratedRemoteEndpoints(allowUnboundedUploads: true),
            Ct
        );
        await using var clientProvider = new ServiceCollection()
            .AddGeneratedHttpRemoteRepositories(server.BaseAddress(RemotePaths.DefaultPrefix))
            .BuildServiceProvider();
        var documents = clientProvider.GetRequiredService<IDocumentRemoteRepository>();

        await documents.InsertAsync(
            new DocumentEntity
            {
                DocumentId = 1,
                Title = "doc-1",
                Payload = null,
                Thumb = Doc1Thumb,
            },
            Ct
        );

        // 既定の Kestrel MaxRequestBodySize（30_000_000）を確実に超える 31MiB
        var payload = new byte[31 * 1024 * 1024];
        new Random(31).NextBytes(payload);

        (await documents.WritePayloadAsync(1, new MemoryStream(payload), cancellationToken: Ct))
            .Should()
            .BeTrue("サイズ制限解除メタデータにより 30MB 超の PUT も受理される");

        using var destination = new MemoryStream();
        (await documents.ReadPayloadAsync(1, destination, Ct)).Should().BeTrue();
        destination.Length.Should().Be(payload.Length, "31MB が往復する");
        destination.ToArray()[^1].Should().Be(payload[^1], "末尾バイトまで転送される");
    }

    /// <summary>
    /// 9b. 対照: 既定（オプトインなし）のバイナリ PUT はホストのリクエストサイズ制限が効き、30MB 超は 413 になる。
    /// </summary>
    /// <remarks>
    /// 共有サーバーは <c>MapGeneratedRemoteEndpoints()</c>（既定引数＝解除なし）で起動しているため、そのまま使える。
    /// 413 は Kestrel の <c>BadHttpRequestException</c> がステータス素通しで分類された結果で、クライアントは
    /// <see cref="RemoteRepositoryException"/> としてその状態コードを保って復元する。応答には他の分類済み拒否と
    /// 同じ <c>RemoteError</c> 本文が載るため、応答を読めた場合は「本文の文言が復元されていること」まで固定する。
    /// ただし Kestrel は上限超過検知時に本文の受信を打ち切るため、フルスイート並列実行などタイミング次第では
    /// クライアントが 413 を読む前に接続リセット（<see cref="HttpRequestException"/>）を観測し得る
    /// （タイミング依存の正常系＝単独実行の実測では 10/10 で 413 だが、負荷並列時に窓が開くことが実測されている）。
    /// どちらの形でも「既定では 31MB の書き込みが拒否される」契約は成立しているため、両方を合格とする。
    /// </remarks>
    [Fact(
        DisplayName = "[Binary/Remote] 9b: 既定（オプトインなし）のバイナリ PUT は 31MB が拒否される（413 または送信中断）"
    )]
    public async Task Stream_Put_WithoutOptIn_IsRejectedByDefaultRequestSizeLimit()
    {
        await SeedAsync(1, null, Doc1Thumb);

        var payload = new byte[31 * 1024 * 1024];
        new Random(31).NextBytes(payload);

        var act = async () =>
            await Documents.WritePayloadAsync(1, new MemoryStream(payload), cancellationToken: Ct);

        var thrown = (await act.Should().ThrowAsync<Exception>()).Which;

        if (thrown is RemoteRepositoryException remote)
        {
            remote.StatusCode.Should().Be(413, "既定ではホストのボディサイズ上限がそのまま効く");
            remote
                .Message.Should()
                .NotBe(
                    "The remote call failed (HTTP 413).",
                    "413 にも RemoteError 本文が載るため、汎用の代替文言にはならない"
                );
            remote.Message.Should().NotBeEmpty();
            remote.CorrelationId.Should().BeNull("相関 ID は詳細を伏せた 500 だけに載る");
        }
        else
        {
            // 413 が読めない場合は送信中断＝接続リセット。負荷並列時の観測形は HttpRequestException と
            // IOException（HTTP スタックが下位のソケット例外をそのまま伝播する枝）の 2 通りがあり、
            // どちらも「拒否されたこと自体は成立している」という同じ結論を運ぶ
            thrown.Should().BeAssignableTo<Exception>();
            thrown
                .Should()
                .Match(
                    e => e is HttpRequestException || e is IOException,
                    "413 が読めない場合は送信中断＝接続リセット（HttpRequestException / IOException）になる"
                );
        }
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

        var baseUrl = _server!.BaseUrl;

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

        // 411 も他の分類済み失敗と同じく RemoteError 本文を伴う（拒否理由が本文から読める）
        var error = await response.Content.ReadFromJsonAsync<RemoteError>(Ct);
        error.Should().NotBeNull();
        error!.Type.Should().Be("BadRequest");
        error.Message.Should().Contain("Content-Length");

        // 411 なので DB は変化せず、payload は依然 NULL（Read は false）
        using var afterPut = new MemoryStream();
        (await Documents.ReadPayloadAsync(1, afterPut, Ct))
            .Should()
            .BeFalse("411 で拒否されたため payload は書き込まれない");
    }

    /// <summary>
    /// 13. バイナリエンドポイントの <c>id</c> 欠落はリクエスト解釈の失敗＝400（従来は 500 だった経路の回帰防止）。
    /// 生成クライアントは必ず id を付けるため、素の <see cref="HttpClient"/> で直接送って観測する。
    /// </summary>
    [Fact(DisplayName = "[Binary/Remote] 13: id 欠落の GET/DELETE は 400（BadRequest）になる")]
    public async Task BinaryEndpoints_MissingId_ReturnsBadRequest()
    {
        await SeedAsync(1, Doc1Payload, Doc1Thumb);

        var baseUrl = _server!.BaseUrl;
        using var raw = new HttpClient();

        using var getResponse = await raw.GetAsync($"{baseUrl}/quicker/Document/Payload", Ct);
        getResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await getResponse.Content.ReadFromJsonAsync<RemoteError>(Ct))!
            .Type.Should()
            .Be("BadRequest");

        using var deleteResponse = await raw.DeleteAsync($"{baseUrl}/quicker/Document/Payload", Ct);
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // 400 で終わっているため列は書き換わっていない
        using var destination = new MemoryStream();
        (await Documents.ReadPayloadAsync(1, destination, Ct)).Should().BeTrue();
    }

    /// <summary>14. 復元できない <c>id</c>（不正 JSON）も 400 になる</summary>
    [Fact(DisplayName = "[Binary/Remote] 14: 不正 JSON の id は 400（BadRequest）になる")]
    public async Task BinaryEndpoints_MalformedId_ReturnsBadRequest()
    {
        await SeedAsync(1, Doc1Payload, Doc1Thumb);

        var baseUrl = _server!.BaseUrl;
        using var raw = new HttpClient();

        // 主キーは int。JSON として復元できない値はリクエスト解釈の失敗として 400 になる
        using var getResponse = await raw.GetAsync(
            $"{baseUrl}/quicker/Document/Payload?id=not-json",
            Ct
        );
        getResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await getResponse.Content.ReadFromJsonAsync<RemoteError>(Ct))!
            .Type.Should()
            .Be("BadRequest");

        using var deleteResponse = await raw.DeleteAsync(
            $"{baseUrl}/quicker/Document/Payload?id=not-json",
            Ct
        );
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    /// <summary>
    /// 15. 行なし 404 は marker（<c>RemoteError{Type:"NotFound"}</c>）を伴い、marker のない 404
    /// （プレフィックス誤りなどでエンドポイントに届かなかった応答）は例外になる。
    /// </summary>
    /// <remarks>
    /// 前者は既存の「404→false」契約そのもの。後者を false に畳むと、ベースアドレスの設定ミスが
    /// 「データがない」と区別できなくなり無言で握り潰される。
    /// </remarks>
    [Fact(
        DisplayName = "[Binary/Remote] 15: marker つき 404 は false・marker なし 404 は例外になる"
    )]
    public async Task Stream_UnmarkedNotFound_Throws()
    {
        await SeedAsync(1, Doc1Payload, Doc1Thumb);

        // 行なしの 404 には marker が載る（生の HttpClient で本文まで確認する）
        using var raw = new HttpClient();
        using var markedResponse = await raw.GetAsync(
            $"{_server!.BaseUrl}/quicker/Document/Payload?id=999",
            Ct
        );
        markedResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await markedResponse.Content.ReadFromJsonAsync<RemoteError>(Ct))!
            .Type.Should()
            .Be("NotFound", "エンドポイント自身が返す 404 は marker で識別できる");

        // プレフィックスを間違えたクライアント＝ルーティングに届かず本文なしの 404 になる
        await using var strayProvider = new ServiceCollection()
            .AddGeneratedHttpRemoteRepositories($"{_server.BaseUrl}/wrong-prefix")
            .BuildServiceProvider();
        var stray = strayProvider.GetRequiredService<IDocumentRemoteRepository>();

        using var destination = new MemoryStream();
        var read = async () => await stray.ReadPayloadAsync(1, destination, Ct);
        (await read.Should().ThrowAsync<RemoteRepositoryException>())
            .Which.StatusCode.Should()
            .Be(404, "marker のない 404 は設定ミスなので false へ畳まず送出する");

        var write = async () =>
            await stray.WritePayloadAsync(1, new MemoryStream([1, 2, 3]), cancellationToken: Ct);
        (await write.Should().ThrowAsync<RemoteRepositoryException>())
            .Which.StatusCode.Should()
            .Be(404);

        var delete = async () => await stray.WritePayloadAsync(1, null, cancellationToken: Ct);
        (await delete.Should().ThrowAsync<RemoteRepositoryException>())
            .Which.StatusCode.Should()
            .Be(404);
    }

    /// <summary>
    /// 16. リモートの Write も渡された Stream を閉じない（直結実装＝<c>BinaryColumnAdoRuntimeTests</c> と同じ所有権契約）。
    /// </summary>
    [Fact(
        DisplayName = "[Binary/Remote] 16: Write は渡された source Stream を閉じない（直結と対称）"
    )]
    public async Task Stream_Write_LeavesSourceStreamOpen()
    {
        await SeedAsync(1, null, Doc1Thumb);

        var payload = new byte[4096];
        new Random(77).NextBytes(payload);

        using var source = new MemoryStream(payload);
        (await Documents.WritePayloadAsync(1, source, cancellationToken: Ct)).Should().BeTrue();

        // 破棄済み MemoryStream は CanRead=false になる＝HTTP レイヤのコンテンツ破棄が呼び出し側 Stream へ波及していない
        source
            .CanRead.Should()
            .BeTrue("Stream の所有権は呼び出し側にあり、リモート実装も閉じない");
        source.Position.Should().Be(payload.Length, "読み切られてはいるが破棄はされていない");

        // 同じ Stream を巻き戻して再送できる＝閉じられていないことの実用的な確認
        source.Position = 0;
        (await Documents.WriteThumbAsync(1, source, cancellationToken: Ct)).Should().BeTrue();
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

        if (_server is not null)
        {
            await _server.DisposeAsync();
        }

        _db.Dispose();
    }
}
