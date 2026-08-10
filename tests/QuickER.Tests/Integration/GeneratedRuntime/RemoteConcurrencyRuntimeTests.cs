using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using QuickER.Tests.GeneratedBinaryFixture;
using Xunit;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// rowversion 列を持つテーブルの楽観排他（<c>ConcurrencyMode</c>）が、<b>実 HTTP のリモート 3 階層</b>でも
/// 直結と同じ意味論になることを検証する（Kestrel を 127.0.0.1 の空きポートで in-process 起動）。
/// </summary>
/// <remarks>
/// <para>
/// サーバー実体は BinaryFixture の<b>インメモリ Repository</b>（<c>AddGeneratedInMemoryRepositories</c> はリモート面
/// <c>I{Entity}RemoteRepository</c> への転送登録も行う）で、実 DB を使わないため Docker 不要＝CI 常時実行。
/// クライアントは生成された HTTP リモート実装のみを使う。
/// </para>
/// <para>
/// 柱は「転送」と「反映」の 2 点:
/// </para>
/// <list type="bullet">
///   <item><c>ConcurrencyMode</c> がリクエストへ載る＝古い版のままでも <c>ForceOverwrite</c> なら通る</item>
///   <item>Insert / Update / Save の応答が版（対応表）を運び、手元のグラフへ書き戻される＝再取得なしで続けて保存できる</item>
///   <item>競合はサーバーの <c>SaveConflictException</c> が HTTP 409 経由で同じ型のまま復元される</item>
/// </list>
/// <para>
/// 旧エンベロープ（<c>Mode</c> フィールドなし）は既定の <c>Optimistic</c> として読まれることも、生の JSON を直接
/// POST して固定する。除外列（payload / thumb）は値を持ったままだと UPDATE が拒否される既存仕様のため、
/// 本テストは一貫して未取得状態（null / 空配列）のまま扱う。
/// </para>
/// </remarks>
[Trait("Category", "Integration")]
public sealed class RemoteConcurrencyRuntimeTests : IAsyncLifetime
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private WebApplication? _app;
    private ServiceProvider? _clientProvider;
    private string _baseUrl = string.Empty;

    /// <summary>Kestrel 起動（空きポート・サーバー実体はインメモリ Repository・サンプルデータなし）→ HTTP クライアント DI 構築</summary>
    public async ValueTask InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddGeneratedInMemoryRepositories(seedSampleData: false);

        _app = builder.Build();
        _app.MapGeneratedRemoteEndpoints();
        await _app.StartAsync(Ct);

        _baseUrl = _app.Urls.First();
        _clientProvider = new ServiceCollection()
            .AddGeneratedHttpRemoteRepositories($"{_baseUrl}/quicker")
            .BuildServiceProvider();
    }

    /// <summary>クライアント側の文書リモート面を解決する</summary>
    private IDocumentRemoteRepository Documents =>
        _clientProvider!.GetRequiredService<IDocumentRemoteRepository>();

    /// <summary>文書 1 件をリモート経由で挿入し、版が書き戻されたインスタンスを返す</summary>
    private async Task<DocumentEntity> InsertedAsync(int id, string title)
    {
        var entity = new DocumentEntity { DocumentId = id, Title = title };
        await Documents.InsertAsync(entity, Ct);
        return entity;
    }

    // ── 版の書き戻し ──

    /// <summary>1. グラフ保存の応答が版を運び、同じグラフをそのまま 2 回目も保存できる</summary>
    [Fact(
        DisplayName = "[Concurrency/Remote] 1: SaveAsync の応答で版が書き戻り同じグラフを続けて保存できる"
    )]
    public async Task SaveAsync_WritesBackRowVersion_AndAllowsConsecutiveSaves()
    {
        var document = new DocumentEntity { DocumentId = 1, Title = "alpha" };
        document.MarkAdded();

        var note = new DocumentNoteEntity
        {
            NoteId = 100,
            DocumentId = 1,
            Note = "first",
        };
        note.MarkAdded();
        document.DocumentNotes.Add(note);

        (await Documents.SaveAsync(document, cancellationToken: Ct))
            .Should()
            .Be(2, "親 1 件＋子 1 件が保存される");

        document.RowVer.Should().NotBeNull("応答の版対応表から手元のエンティティへ書き戻される");
        document.RowState.Should().Be(RowState.Unchanged, "保存後の状態確定は従来どおり");
        note.RowState.Should().Be(RowState.Unchanged);
        var afterInsert = document.RowVer;

        // 2 回目: 再取得せずそのまま保存できる＝書き戻された版がストアの現在値と一致している証明
        document.Title = "beta";
        document.MarkUpdated();
        note.Note = "second";
        note.MarkUpdated();

        (await Documents.SaveAsync(document, cancellationToken: Ct))
            .Should()
            .Be(2, "書き戻された版で版チェックが通る");
        document.RowVer.Should().NotEqual(afterInsert, "保存のたびに新しい版が反映される");
    }

    /// <summary>2. 挿入の応答も版を運ぶ（挿入直後のインスタンスをそのまま更新できる）</summary>
    [Fact(DisplayName = "[Concurrency/Remote] 2: InsertAsync も応答で版を書き戻す")]
    public async Task InsertAsync_WritesBackRowVersion()
    {
        var document = await InsertedAsync(1, "alpha");

        document.RowVer.Should().NotBeNull("挿入で採番された版が応答で戻る");
        document.RowVer!.Length.Should().Be(8, "インメモリの擬似版も rowversion と同じ 8 バイト");

        document.Title = "updated";
        (await Documents.UpdateAsync(document, cancellationToken: Ct))
            .Should()
            .BeTrue("再取得なしで更新できる＝直結と同じ挙動");
    }

    /// <summary>3. 複数ルートの保存でもルートごとに版が書き戻される</summary>
    [Fact(DisplayName = "[Concurrency/Remote] 3: SaveMany は複数ルートそれぞれへ版を書き戻す")]
    public async Task SaveManyAsync_WritesBackRowVersionPerRoot()
    {
        var first = new DocumentEntity { DocumentId = 1, Title = "alpha" };
        var second = new DocumentEntity { DocumentId = 2, Title = "beta" };
        first.MarkAdded();
        second.MarkAdded();

        (await Documents.SaveAsync([first, second], cancellationToken: Ct)).Should().Be(2);

        first.RowVer.Should().NotBeNull();
        second.RowVer.Should().NotBeNull();
        first.RowVer.Should().NotEqual(second.RowVer, "版はルートごとに別々に対応付けられる");

        first.Title = "alpha2";
        second.Title = "beta2";
        first.MarkUpdated();
        second.MarkUpdated();

        (await Documents.SaveAsync([first, second], cancellationToken: Ct))
            .Should()
            .Be(2, "どちらのルートも書き戻された版で版チェックが通る");
    }

    // ── 単一 UpdateAsync ──

    /// <summary>4. 単一更新は成功で版を書き戻し、古い版は 409 経由の SaveConflictException、行なしは false</summary>
    [Fact(
        DisplayName = "[Concurrency/Remote] 4: UpdateAsync は版を書き戻し・古い版は SaveConflictException・行なしは false"
    )]
    public async Task UpdateAsync_WritesBackRowVersion_AndReportsConflictOrMissingRow()
    {
        var document = await InsertedAsync(1, "alpha");
        var stale = await Documents.GetByIdAsync(1, Ct);
        stale.Should().NotBeNull();
        stale!.RowVer.Should().Equal(document.RowVer, "同時点の取得は同じ版を持つ");

        document.Title = "by-first";
        (await Documents.UpdateAsync(document, cancellationToken: Ct)).Should().BeTrue();
        document.RowVer.Should().NotEqual(stale.RowVer, "更新の応答で新しい版が反映される");

        // 古い版のまま更新すると、サーバー側の SaveConflictException が HTTP 409 経由で同じ型のまま戻る
        stale.Title = "by-second";
        var conflict = async () => await Documents.UpdateAsync(stale, cancellationToken: Ct);

        await conflict
            .Should()
            .ThrowAsync<SaveConflictException>()
            .WithMessage("*modified by another user*");

        (await Documents.GetByIdAsync(1, Ct))!
            .Title.Should()
            .Be("by-first", "競合した更新は適用されない（先勝ち）");

        // 行なしは競合ではなく従来契約の false
        var missing = new DocumentEntity
        {
            DocumentId = 999,
            Title = "ghost",
            RowVer = [0, 0, 0, 0, 0, 0, 0, 1],
        };

        (await Documents.UpdateAsync(missing, cancellationToken: Ct)).Should().BeFalse();
    }

    /// <summary>5. ForceOverwrite がリクエストへ載る（古い版のままでも上書きできる）</summary>
    [Fact(DisplayName = "[Concurrency/Remote] 5: ForceOverwrite が転送され古い版でも上書きできる")]
    public async Task UpdateAsync_ForceOverwrite_IsCarriedOverTheWire()
    {
        var document = await InsertedAsync(1, "alpha");
        var stale = await Documents.GetByIdAsync(1, Ct);
        var staleVersion = stale!.RowVer;

        document.Title = "by-first";
        await Documents.UpdateAsync(document, cancellationToken: Ct);

        stale.Title = "forced";
        (await Documents.UpdateAsync(stale, ConcurrencyMode.ForceOverwrite, Ct))
            .Should()
            .BeTrue("ポリシーが転送されるので版条件が外れる");

        (await Documents.GetByIdAsync(1, Ct))!.Title.Should().Be("forced");
        stale.RowVer.Should().NotEqual(staleVersion, "上書き後の新しい版も書き戻される");
    }

    // ── グラフ保存の競合 ──

    /// <summary>6. グラフ保存の競合も 409 経由で SaveConflictException になる</summary>
    [Fact(DisplayName = "[Concurrency/Remote] 6: グラフ保存の競合は SaveConflictException になる")]
    public async Task SaveAsync_Throws_WhenRowVersionIsStale()
    {
        var document = await InsertedAsync(1, "alpha");
        var stale = await Documents.GetByIdAsync(1, Ct);

        document.Title = "by-first";
        document.MarkUpdated();
        await Documents.SaveAsync(document, cancellationToken: Ct);

        stale!.Title = "by-second";
        stale.MarkUpdated();
        var conflict = async () => await Documents.SaveAsync(stale, cancellationToken: Ct);

        await conflict
            .Should()
            .ThrowAsync<SaveConflictException>()
            .WithMessage("*modified by another user*");
        (await Documents.GetByIdAsync(1, Ct))!.Title.Should().Be("by-first");

        // 版条件を外せば同じ古いインスタンスでも保存できる
        (
            await Documents.SaveAsync(
                stale,
                mode: ConcurrencyMode.ForceOverwrite,
                cancellationToken: Ct
            )
        )
            .Should()
            .Be(1);
        (await Documents.GetByIdAsync(1, Ct))!.Title.Should().Be("by-second");
    }

    // ── 旧エンベロープ互換 ──

    /// <summary>7. Mode を持たない旧エンベロープは既定の Optimistic として読まれる</summary>
    [Fact(
        DisplayName = "[Concurrency/Remote] 7: Mode なしの旧エンベロープは Optimistic として扱われる"
    )]
    public async Task LegacyEnvelopeWithoutMode_IsReadAsOptimistic()
    {
        var document = await InsertedAsync(1, "alpha");
        var stale = await Documents.GetByIdAsync(1, Ct);

        document.Title = "by-first";
        await Documents.UpdateAsync(document, cancellationToken: Ct);

        using var raw = new HttpClient();

        // Mode フィールドを持たない旧エンベロープ（Insert 用の RemoteEntityRequest がそのまま旧 Update の形）
        stale!.Title = "by-second";
        var conflict = await PostUpdateAsync(raw, new RemoteEntityRequest<DocumentEntity>(stale));

        conflict
            .Should()
            .Be(HttpStatusCode.Conflict, "Mode 欠落は既定の Optimistic＝版チェックが効く");

        // 版が最新なら同じ旧エンベロープで成功する（常に失敗しているわけではないことの対照）
        var fresh = await Documents.GetByIdAsync(1, Ct);
        fresh!.Title = "by-legacy";
        var accepted = await PostUpdateAsync(raw, new RemoteEntityRequest<DocumentEntity>(fresh));

        accepted.Should().Be(HttpStatusCode.OK);
        (await Documents.GetByIdAsync(1, Ct))!.Title.Should().Be("by-legacy");
    }

    /// <summary>生の JSON をそのまま Update エンドポイントへ POST し、応答のステータスコードを返す</summary>
    private async Task<HttpStatusCode> PostUpdateAsync(HttpClient client, object payload)
    {
        using var content = new StringContent(
            JsonSerializer.Serialize(payload, RemoteJson.Options),
            Encoding.UTF8,
            "application/json"
        );
        using var response = await client.PostAsync(
            $"{_baseUrl}/quicker/Document/Update",
            content,
            Ct
        );
        return response.StatusCode;
    }

    /// <summary>使い終えたクライアント DI・サーバーを破棄する</summary>
    public async ValueTask DisposeAsync()
    {
        _clientProvider?.Dispose();

        if (_app is not null)
        {
            await _app.DisposeAsync();
        }
    }
}
