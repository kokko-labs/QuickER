using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using QuickER.Tests.GeneratedConcurrencyFixture;
using Xunit;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// rowversion 列 <b>× 値オブジェクト</b>の楽観排他が、実 HTTP のリモート 3 階層でも直結と同じ意味論になることを
/// 検証する（Kestrel を 127.0.0.1 の空きポートで in-process 起動・Docker 不要＝CI 常時実行）。
/// </summary>
/// <remarks>
/// <para>
/// 版対応表（<c>RemoteRowVersionEntry</c>）が運ぶのは生の <c>byte[]</c> なので、VO 有効時は
/// <b>サーバー側の収集</b>（VO を素値へ開く）と<b>クライアント側の書き戻し</b>（素値を VO へ包む）の両方で
/// 変換が要る。旧実装は収集側が常に <c>null</c>（VO は <c>byte[]</c> にキャストできない）で対応表が空になり、
/// 書き戻し側は生 <c>byte[]</c> の <c>SetValue</c> で例外になっていた。
/// </para>
/// <para>
/// サーバー実体は本フィクスチャの<b>インメモリ Repository</b>（<c>AddGeneratedInMemoryRepositories</c> は
/// リモート面への転送登録も行う）。クライアントは生成された HTTP リモート実装のみを使う。
/// </para>
/// </remarks>
[Trait("Category", "Integration")]
public sealed class ConcurrencyVoRemoteRuntimeTests : IAsyncLifetime
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private WebApplication? _app;
    private ServiceProvider? _clientProvider;

    /// <summary>Kestrel 起動（空きポート・サーバー実体はインメモリ Repository）→ HTTP クライアント DI 構築</summary>
    public async ValueTask InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddGeneratedInMemoryRepositories(seedSampleData: false);

        _app = builder.Build();
        _app.MapGeneratedRemoteEndpoints();
        await _app.StartAsync(Ct);

        _clientProvider = new ServiceCollection()
            .AddGeneratedHttpRemoteRepositories($"{_app.Urls.First()}/quicker")
            .BuildServiceProvider();
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

    /// <summary>クライアント側の gadget リモート面を解決する</summary>
    private IGadgetRemoteRepository Gadgets =>
        _clientProvider!.GetRequiredService<IGadgetRemoteRepository>();

    /// <summary>gadget 1 件をリモート経由で挿入し、版が書き戻されたインスタンスを返す</summary>
    private async Task<GadgetEntity> InsertedAsync(int id, string name)
    {
        var entity = new GadgetEntity
        {
            GadgetId = GadgetIdValue.Create(id),
            Name = NameValue.Create(name),
        };
        await Gadgets.InsertAsync(entity, Ct);

        return entity;
    }

    // ── 版の往復 ──

    /// <summary>1. 挿入の応答が版を運び、VO 型として書き戻る（再取得なしで続けて更新できる）</summary>
    [Fact(DisplayName = "[Concurrency/VO/Remote] 1: InsertAsync が VO 型の版を応答で書き戻す")]
    public async Task InsertAsync_WritesBackRowVersion_AsValueObject()
    {
        var gadget = await InsertedAsync(1, "alpha");

        gadget.RowVer.Should().NotBeNull("応答の版対応表から VO として書き戻される");
        gadget
            .RowVer.Value.Length.Should()
            .Be(8, "インメモリの擬似版も rowversion と同じ 8 バイト");

        gadget.Name = NameValue.Create("updated");
        (await Gadgets.UpdateAsync(gadget, cancellationToken: Ct))
            .Should()
            .BeTrue("再取得なしで更新できる＝直結と同じ挙動");
    }

    /// <summary>2. グラフ保存の応答は親子それぞれの版を運び、同じグラフをそのまま 2 回目も保存できる</summary>
    [Fact(
        DisplayName = "[Concurrency/VO/Remote] 2: SaveAsync は親子の版を書き戻し続けて保存できる"
    )]
    public async Task SaveAsync_WritesBackRowVersion_AndAllowsConsecutiveSaves()
    {
        var gadget = new GadgetEntity
        {
            GadgetId = GadgetIdValue.Create(1),
            Name = NameValue.Create("alpha"),
        };
        gadget.MarkAdded();

        var note = new GadgetNoteEntity
        {
            NoteId = NoteIdValue.Create(100),
            GadgetId = GadgetIdValue.Create(1),
            Note = NoteValue.Create("first"),
        };
        note.MarkAdded();
        gadget.GadgetNotes.Add(note);

        (await Gadgets.SaveAsync(gadget, cancellationToken: Ct))
            .Should()
            .Be(2, "親 1 件＋子 1 件が保存される");

        gadget
            .RowVer.Should()
            .NotBeNull("親の版が書き戻る（対応表の収集がサーバー側で VO を開けている証明）");
        note.RowVer.Should().NotBeNull("子の版も書き戻る");
        gadget.RowState.Should().Be(RowState.Unchanged, "保存後の状態確定は従来どおり");
        note.RowState.Should().Be(RowState.Unchanged);
        var afterInsert = gadget.RowVer;

        // 2 回目: 再取得せずそのまま保存できる＝書き戻された版がサーバーの現在値と一致している
        gadget.Name = NameValue.Create("beta");
        gadget.MarkUpdated();
        note.Note = NoteValue.Create("second");
        note.MarkUpdated();

        (await Gadgets.SaveAsync(gadget, cancellationToken: Ct))
            .Should()
            .Be(2, "書き戻された版で版チェックが通る");
        gadget.RowVer.Should().NotBe(afterInsert, "保存のたびに新しい版が反映される");
    }

    // ── 競合 ──

    /// <summary>3. 古い版のままの更新は HTTP 409 経由で SaveConflictException として復元される</summary>
    [Fact(
        DisplayName = "[Concurrency/VO/Remote] 3: 版が古い更新は 409 経由の SaveConflictException になる"
    )]
    public async Task UpdateAsync_Throws_WhenRowVersionIsStale()
    {
        var gadget = await InsertedAsync(1, "alpha");
        var stale = await Gadgets.GetByIdAsync(GadgetIdValue.Create(1), Ct);
        stale!.RowVer.Should().Be(gadget.RowVer, "同時点の取得は同じ版を持つ");

        gadget.Name = NameValue.Create("by-first");
        (await Gadgets.UpdateAsync(gadget, cancellationToken: Ct)).Should().BeTrue();

        stale.Name = NameValue.Create("by-second");
        var conflict = async () => await Gadgets.UpdateAsync(stale, cancellationToken: Ct);

        await conflict
            .Should()
            .ThrowAsync<SaveConflictException>()
            .WithMessage("*modified by another user*");

        (await Gadgets.GetByIdAsync(GadgetIdValue.Create(1), Ct))!
            .Name.Value.Should()
            .Be("by-first", "競合した更新は適用されない（先勝ち）");
    }

    /// <summary>4. ForceOverwrite はリクエストへ載り、古い版のままでも上書きできる</summary>
    [Fact(
        DisplayName = "[Concurrency/VO/Remote] 4: ForceOverwrite が転送され古い版でも上書きできる"
    )]
    public async Task UpdateAsync_ForceOverwrite_IsCarriedOverTheWire()
    {
        var gadget = await InsertedAsync(1, "alpha");
        var stale = await Gadgets.GetByIdAsync(GadgetIdValue.Create(1), Ct);
        var staleVersion = stale!.RowVer;

        gadget.Name = NameValue.Create("by-first");
        await Gadgets.UpdateAsync(gadget, cancellationToken: Ct);

        stale.Name = NameValue.Create("forced");
        (await Gadgets.UpdateAsync(stale, ConcurrencyMode.ForceOverwrite, Ct))
            .Should()
            .BeTrue("ポリシーが転送されるので版条件が外れる");

        (await Gadgets.GetByIdAsync(GadgetIdValue.Create(1), Ct))!.Name.Value.Should().Be("forced");
        stale.RowVer.Should().NotBe(staleVersion, "上書き後の新しい版も書き戻される");
    }
}
