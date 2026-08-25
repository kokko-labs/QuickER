using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using QuickER.Tests.GeneratedBinaryFixture;
using Xunit;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// <c>SaveConflictException</c> が競合の内訳（<c>Reason</c> / <c>EntityTypeName</c> / <c>Key</c>）を持ち、
/// リモート（HTTP 409）越しでも同じ内訳が復元されることを検証する。
/// </summary>
/// <remarks>
/// <para>
/// 例外メッセージは「再取得してやり直せ」と指示するのに、やり直しループを組む材料（理由・型・キー）が
/// API から取れないと呼び出し側はメッセージを文字列解析するしかない。ここでは直結（インメモリ実体）と
/// リモート（実 HTTP）の双方で、同じプロパティが同じ値になることを固定する。
/// </para>
/// <para>
/// 内訳フィールドはワイヤ上は省略可能なので、それらを持たない旧ボディ（旧サーバー）でも
/// <c>SaveConflictException</c> の型とメッセージは保たれ、内訳だけが <c>Unknown</c> / null に退化する。
/// この後方互換は、レガシー応答だけを返す最小サーバーを立てて固定する。
/// </para>
/// <para>実 DB を使わない（サーバー実体はインメモリ Repository）ため Docker 不要＝CI 常時実行。</para>
/// </remarks>
[Trait("Category", "Integration")]
public sealed class SaveConflictDetailsRuntimeTests : IAsyncLifetime
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private WebApplication? _app;
    private WebApplication? _legacyApp;
    private ServiceProvider? _clientProvider;
    private ServiceProvider? _legacyClientProvider;

    /// <summary>生成サーバー（インメモリ実体）と、旧ボディだけを返すレガシーサーバーを両方起動する</summary>
    public async ValueTask InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddGeneratedInMemoryRepositories(seedSampleData: false);

        _app = builder.Build();
        _app.MapGeneratedRemoteEndpoints(RemoteAccess.AllowAnonymous);
        await _app.StartAsync(Ct);

        _clientProvider = new ServiceCollection()
            .AddGeneratedHttpRemoteRepositories($"{_app.Urls.First()}/quicker")
            .BuildServiceProvider();

        // 内訳フィールドを持たない旧ボディ（Type / Message だけ）を返すだけの最小サーバー
        var legacyBuilder = WebApplication.CreateBuilder();
        legacyBuilder.Logging.ClearProviders();
        legacyBuilder.WebHost.UseUrls("http://127.0.0.1:0");

        _legacyApp = legacyBuilder.Build();
        _legacyApp.MapPost(
            "/quicker/Document/Update",
            async (HttpContext context) =>
            {
                context.Response.StatusCode = StatusCodes.Status409Conflict;
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync(
                    """{"Type":"SaveConflict","Message":"legacy conflict"}""",
                    context.RequestAborted
                );
            }
        );
        await _legacyApp.StartAsync(Ct);

        _legacyClientProvider = new ServiceCollection()
            .AddGeneratedHttpRemoteRepositories($"{_legacyApp.Urls.First()}/quicker")
            .BuildServiceProvider();
    }

    public async ValueTask DisposeAsync()
    {
        _clientProvider?.Dispose();
        _legacyClientProvider?.Dispose();

        if (_app is not null)
        {
            await _app.DisposeAsync();
        }

        if (_legacyApp is not null)
        {
            await _legacyApp.DisposeAsync();
        }
    }

    private IDocumentRemoteRepository RemoteDocuments =>
        _clientProvider!.GetRequiredService<IDocumentRemoteRepository>();

    private IDocumentRemoteRepository LegacyRemoteDocuments =>
        _legacyClientProvider!.GetRequiredService<IDocumentRemoteRepository>();

    // ── 直結（インメモリ実体） ──

    /// <summary>版が古い更新は Reason=Modified・型名・キーを伴う</summary>
    [Fact(DisplayName = "[SaveConflict] 直結: 版が古い更新は Modified と型名・キーを伴う")]
    public async Task DirectUpdate_Conflict_CarriesModifiedReasonWithTypeAndKey()
    {
        var store = new InMemoryDataStore();
        var documents = new InMemoryDocumentRepository(store);

        var entity = new DocumentEntity
        {
            DocumentId = 1,
            Title = "alpha",
            Thumb = [],
        };
        await documents.InsertAsync(entity, Ct);

        var stale = await documents.GetByIdAsync(1, Ct);
        entity.Title = "by-first";
        await documents.UpdateAsync(entity, cancellationToken: Ct);

        stale!.Title = "by-second";
        var act = async () => await documents.UpdateAsync(stale, cancellationToken: Ct);

        var thrown = (await act.Should().ThrowAsync<SaveConflictException>()).Which;
        thrown.Reason.Should().Be(SaveConflictReason.Modified);
        thrown.EntityTypeName.Should().Be(nameof(DocumentEntity));
        thrown.Key.Should().Be("1", "主キーは表示用の文字列として載る");
        thrown.Message.Should().Contain("modified by another user", "メッセージ本文は変換されない");
    }

    /// <summary>存在しない行のグラフ更新は Reason=NotFound を伴う</summary>
    [Fact(DisplayName = "[SaveConflict] 直結: 行なしのグラフ更新は NotFound を伴う")]
    public async Task DirectGraphSave_MissingRow_CarriesNotFoundReason()
    {
        var store = new InMemoryDataStore();
        var documents = new InMemoryDocumentRepository(store);

        var ghost = new DocumentEntity
        {
            DocumentId = 999,
            Title = "ghost",
            Thumb = [],
        };
        ghost.MarkUpdated();

        var act = async () => await documents.SaveAsync(ghost, cancellationToken: Ct);

        var thrown = (await act.Should().ThrowAsync<SaveConflictException>()).Which;
        thrown.Reason.Should().Be(SaveConflictReason.NotFound);
        thrown.EntityTypeName.Should().Be(nameof(DocumentEntity));
        thrown.Key.Should().Be("999");
    }

    /// <summary>メッセージだけのコンストラクタは内訳なし（Unknown / null）に留まる</summary>
    [Fact(DisplayName = "[SaveConflict] メッセージのみの生成は Unknown / null に留まる")]
    public void MessageOnlyConstructor_LeavesDetailsUnset()
    {
        var exception = new SaveConflictException("boom");

        exception.Reason.Should().Be(SaveConflictReason.Unknown);
        exception.EntityTypeName.Should().BeNull();
        exception.Key.Should().BeNull();
    }

    // ── リモート（実 HTTP 409） ──

    /// <summary>409 経由でも直結と同じ内訳が復元される</summary>
    [Fact(DisplayName = "[SaveConflict] リモート: 409 経由でも同じ内訳が復元される")]
    public async Task RemoteUpdate_Conflict_RestoresTheSameDetails()
    {
        var entity = new DocumentEntity { DocumentId = 1, Title = "alpha" };
        await RemoteDocuments.InsertAsync(entity, Ct);

        var stale = await RemoteDocuments.GetByIdAsync(1, Ct);
        entity.Title = "by-first";
        await RemoteDocuments.UpdateAsync(entity, cancellationToken: Ct);

        stale!.Title = "by-second";
        var act = async () => await RemoteDocuments.UpdateAsync(stale, cancellationToken: Ct);

        var thrown = (await act.Should().ThrowAsync<SaveConflictException>()).Which;
        thrown.Reason.Should().Be(SaveConflictReason.Modified, "理由がワイヤを越えて復元される");
        thrown.EntityTypeName.Should().Be(nameof(DocumentEntity));
        thrown.Key.Should().Be("1");
    }

    /// <summary>内訳を持たない旧ボディは型・メッセージそのままで内訳だけ Unknown / null へ退化する</summary>
    [Fact(DisplayName = "[SaveConflict] リモート: 内訳のない旧ボディは Unknown / null へ退化する")]
    public async Task RemoteUpdate_LegacyConflictBody_DegradesToUnknown()
    {
        var entity = new DocumentEntity { DocumentId = 1, Title = "alpha" };
        var act = async () =>
            await LegacyRemoteDocuments.UpdateAsync(entity, cancellationToken: Ct);

        var thrown = (await act.Should().ThrowAsync<SaveConflictException>()).Which;
        thrown.Message.Should().Be("legacy conflict", "メッセージは元の本文のまま復元される");
        thrown
            .Reason.Should()
            .Be(SaveConflictReason.Unknown, "内訳のない旧ボディは Unknown へ退化");
        thrown.EntityTypeName.Should().BeNull();
        thrown.Key.Should().BeNull();
    }
}
