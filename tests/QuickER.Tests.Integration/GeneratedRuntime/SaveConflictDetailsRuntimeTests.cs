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
using QuickER.Tests.TestSupport;
using Xunit;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// <c>SaveConflictException</c> の内訳（<c>Reason</c> / <c>EntityTypeName</c> / <c>Key</c>）のうち、
/// 楽観排他の共通基底が持たない形を検証する。
/// </summary>
/// <remarks>
/// <para>
/// 版が古い更新・削除の Modified と、版を持つ行なしのグラフ更新の NotFound の内訳は、共通基底
/// <see cref="ConcurrencyRuntimeTestsBase{TEntity, TConflictException}"/>（Fact 2・5・7・8・8b）が全実装先と
/// リモートの各サーバーで表明する。ここに残すのは、版を持たない行なしのグラフ更新（基底の 8b は版を入れて送る）・
/// メッセージだけの生成・旧ボディの退化の 3 つ。
/// </para>
/// <para>
/// 内訳フィールドはワイヤ上は省略可能なので、それらを持たない旧ボディ（旧サーバー）でも
/// <c>SaveConflictException</c> の型とメッセージは保たれ、内訳だけが <c>Unknown</c> / null に退化する。
/// この後方互換は、レガシー応答だけを返す最小サーバーを立てて固定する。
/// </para>
/// <para>
/// 実 DB を使わない（直結はインメモリ Repository・リモートは旧ボディだけを返すリポジトリなしのスタブ）ため
/// Docker 不要＝CI 常時実行。
/// </para>
/// </remarks>
[Trait("Category", "Integration")]
[RuntimeCell(RuntimeFeatures.Concurrency, RuntimeTarget.InMemory)]
[RuntimeCell(RuntimeFeatures.Concurrency, RuntimeTarget.Remote, ServerNone = true)]
public sealed class SaveConflictDetailsRuntimeTests : IAsyncLifetime
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private WebApplication? _legacyApp;
    private ServiceProvider? _legacyClientProvider;

    /// <summary>旧ボディだけを返すレガシーサーバーを起動する</summary>
    public async ValueTask InitializeAsync()
    {
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
        _legacyClientProvider?.Dispose();

        if (_legacyApp is not null)
        {
            await _legacyApp.DisposeAsync();
        }
    }

    private IDocumentRemoteRepository LegacyRemoteDocuments =>
        _legacyClientProvider!.GetRequiredService<IDocumentRemoteRepository>();

    // ── 直結（インメモリ実体） ──

    /// <summary>
    /// 版を持たない（取得していない）行なしのグラフ更新は Reason=NotFound を伴う
    /// （基底の 8b は版を入れて送るので、版が null の入力はここだけが持つ）。
    /// </summary>
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
