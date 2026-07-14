using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using QuickER.Sqlite;
using QuickER.Tests.GeneratedBinaryFixture;
using Xunit;

namespace QuickER.Tests.Integration;

/// <summary>
/// 無制限バイナリ除外の生成物を、実 HTTP（Kestrel を 127.0.0.1 の空きポートで in-process 起動）＋実 SQLite の
/// 3 階層構成で検証する。サーバー実体は自作 <c>SqliteRepository</c>（除外が効く側）で、クライアントは
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

    /// <summary>スキーマ作成 → Kestrel 起動（空きポート・サーバー実体は自作 SqliteRepository）→ HTTP クライアント DI 構築</summary>
    public async ValueTask InitializeAsync()
    {
        var ddl = new SqliteDdlGenerator().Build(BinaryFixtureDefinition.Build());
        await _db.ApplyDdlAsync(ddl, Ct);

        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddGeneratedRepositories(_db.ReadWriteCreateConnectionString);

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
