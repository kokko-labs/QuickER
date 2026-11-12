using System.Linq;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using QuickER.Tests.GeneratedConcurrencyFixture;
using Xunit;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// 楽観排他のランタイムスイートを <b>rowversion 列 × 値オブジェクト</b>の図で、<b>実 HTTP のリモート 3 階層</b>へ
/// 流す派生の共通部（Kestrel を 127.0.0.1 の空きポートで in-process 起動し、クライアントは生成された HTTP リモート実装だけを使う）。
/// </summary>
/// <remarks>
/// <para>
/// 版対応表（<c>RemoteRowVersionEntry</c>）が運ぶのは生の <c>byte[]</c> なので、VO 有効時は
/// <b>サーバー側の収集</b>（VO を素値へ開く）と<b>クライアント側の書き戻し</b>（素値を VO へ包む）の両方で
/// 変換が要る。基底 <see cref="ConcurrencyRuntimeTestsBase{TEntity, TConflictException}"/> のシナリオが緑になること自体が
/// 両側の変換の実証で、このクラスは加えて親子グラフの版対応表を VO 固有の観点として持つ。
/// </para>
/// <para>
/// 派生は、サーバーの DI（どの実装先のリポジトリを置くか）と、保存先を空にする処理だけを差し込む。
/// 「他者による更新」は、別に取得した最新のインスタンスをリモート越しに更新して作る（手元のインスタンスの版は古いまま残る）。
/// </para>
/// </remarks>
public abstract class ConcurrencyVoRemoteRuntimeTestsBase
    : ConcurrencyRuntimeTestsBase<GadgetEntity, SaveConflictException>,
        IAsyncLifetime
{
    private InProcessRemoteServer? _server;
    private ServiceProvider? _clientProvider;

    /// <summary>サーバーの起動の前に行う準備（実 DB の派生は Docker の有無を判定する）</summary>
    protected virtual void BeforeServerStart() { }

    /// <summary>サーバー側の DI に、リモート面を転送するリポジトリを登録する</summary>
    protected abstract void RegisterServerServices(IServiceCollection services);

    /// <summary>保存先を空にする（テストごとに空で起動するサーバーなら何もしない）</summary>
    protected abstract Task ClearStoreAsync();

    /// <summary>Kestrel を起動し、HTTP クライアントの DI を組み立てる</summary>
    public async ValueTask InitializeAsync()
    {
        BeforeServerStart();

        _server = await InProcessRemoteServer.StartAsync(
            RegisterServerServices,
            app => app.MapGeneratedRemoteEndpoints(RemoteAccess.AllowAnonymous),
            Ct
        );

        _clientProvider = new ServiceCollection()
            .AddGeneratedHttpRemoteRepositories(_server.BaseAddress(RemotePaths.DefaultPrefix))
            .BuildServiceProvider();
    }

    /// <summary>使い終えたクライアント DI・サーバーを破棄する</summary>
    public async ValueTask DisposeAsync()
    {
        _clientProvider?.Dispose();

        if (_server is not null)
        {
            await _server.DisposeAsync();
        }
    }

    /// <summary>クライアント側の gadget リモート面を解決する</summary>
    private IGadgetRemoteRepository Gadgets =>
        _clientProvider!.GetRequiredService<IGadgetRemoteRepository>();

    /// <summary>クライアント側のメモリモート面を解決する</summary>
    private IGadgetNoteRemoteRepository Notes =>
        _clientProvider!.GetRequiredService<IGadgetNoteRemoteRepository>();

    /// <summary>中立表現の楽観排他ポリシーを、このフィクスチャの <c>ConcurrencyMode</c> へ翻訳する</summary>
    private static ConcurrencyMode Translate(ConcurrencyChoice choice) =>
        choice switch
        {
            ConcurrencyChoice.Optimistic => ConcurrencyMode.Optimistic,
            ConcurrencyChoice.ForceOverwrite => ConcurrencyMode.ForceOverwrite,
            _ => (ConcurrencyMode)99,
        };

    /// <summary>保存先を空にしてから、リモート経由でシードを投入する</summary>
    protected override async Task ResetAndSeedAsync()
    {
        await ClearStoreAsync();
        await Gadgets.InsertAsync(NewEntity(SeededRootId, "alpha"), Ct);
        await Gadgets.InsertAsync(NewEntity(SeededChildlessRootId, "beta"), Ct);
        await Notes.InsertAsync(NewNote(SeededChildId, SeededRootId, "first"), Ct);
    }

    /// <summary>gadget を組み立てる（キー・名前とも VO へ包む）</summary>
    protected override GadgetEntity NewEntity(int id, string title) =>
        new() { GadgetId = GadgetIdValue.Create(id), Name = NameValue.Create(title) };

    /// <summary>メモ（子）を組み立てる</summary>
    private static GadgetNoteEntity NewNote(int noteId, int gadgetId, string note) =>
        new()
        {
            NoteId = NoteIdValue.Create(noteId),
            GadgetId = GadgetIdValue.Create(gadgetId),
            Note = NoteValue.Create(note),
        };

    protected override Task InsertAsync(GadgetEntity entity) => Gadgets.InsertAsync(entity, Ct);

    protected override Task<GadgetEntity?> GetAsync(int id) =>
        Gadgets.GetByIdAsync(GadgetIdValue.Create(id), Ct);

    /// <summary>リモート面は <c>Query()</c> を持たないため、親と子を別々に取得してグラフを組み立てる</summary>
    protected override async Task<GadgetEntity?> GetWithChildrenAsync(int id)
    {
        var root = await GetAsync(id);

        if (root is null)
        {
            return null;
        }

        var child = await Notes.GetByIdAsync(NoteIdValue.Create(SeededChildId), Ct);

        if (child is not null)
        {
            root.GadgetNotes.Add(child);
        }

        return root;
    }

    protected override string GetTitle(GadgetEntity entity) => entity.Name.Value;

    protected override void SetTitle(GadgetEntity entity, string title) =>
        entity.Name = NameValue.Create(title);

    protected override byte[]? GetRowVersion(GadgetEntity entity) => entity.RowVer?.Value;

    protected override void SetRowVersion(GadgetEntity entity, byte[] rowVersion) =>
        entity.RowVer = RowVerValue.Create(rowVersion);

    protected override void MarkAdded(GadgetEntity entity) => entity.MarkAdded();

    protected override void MarkUpdated(GadgetEntity entity) => entity.MarkUpdated();

    protected override void MarkRemoved(GadgetEntity entity) => entity.MarkRemoved();

    protected override Task<bool> UpdateAsync(
        GadgetEntity entity,
        ConcurrencyChoice mode = ConcurrencyChoice.Optimistic
    ) => Gadgets.UpdateAsync(entity, Translate(mode), Ct);

    protected override Task<int> SaveAsync(
        GadgetEntity entity,
        ConcurrencyChoice mode = ConcurrencyChoice.Optimistic,
        bool insertWhenUpdateMissing = false
    ) =>
        Gadgets.SaveAsync(
            entity,
            insertWhenUpdateMissing: insertWhenUpdateMissing,
            mode: Translate(mode),
            cancellationToken: Ct
        );

    /// <summary>別途取得した最新インスタンス経由で更新する（手元のインスタンスの版は古いまま残る）</summary>
    protected override async Task BumpByAnotherUserAsync(int id, string title)
    {
        var fresh = await GetAsync(id);
        fresh.Should().NotBeNull();

        fresh!.Name = NameValue.Create(title);
        (await Gadgets.UpdateAsync(fresh, cancellationToken: Ct)).Should().BeTrue();
    }

    protected override void EditFirstChild(GadgetEntity root, string note)
    {
        var child = root.GadgetNotes.First();
        child.Note = NoteValue.Create(note);
        child.MarkUpdated();
    }

    protected override (string Reason, string? EntityTypeName, string? Key) Describe(
        SaveConflictException exception
    ) => (exception.Reason.ToString(), exception.EntityTypeName, exception.Key);

    protected override async Task<string?> ReadChildNoteAsync(int noteId) =>
        (await Notes.GetByIdAsync(NoteIdValue.Create(noteId), Ct))?.Note.Value;

    // ── VO 固有: 親子グラフの版対応表 ──

    /// <summary>グラフ保存の応答は親子それぞれの版を VO 型で運び、同じグラフをそのまま 2 回目も保存できる</summary>
    [Fact(
        DisplayName = "[Concurrency/VO/Remote] SaveAsync は親子の版を VO 型で書き戻し続けて保存できる"
    )]
    public async Task SaveAsync_WritesBackRowVersion_AndAllowsConsecutiveSaves()
    {
        await ClearStoreAsync();

        var gadget = NewEntity(SeededRootId, "alpha");
        gadget.MarkAdded();

        var note = NewNote(SeededChildId, SeededRootId, "first");
        note.MarkAdded();
        gadget.GadgetNotes.Add(note);

        (await Gadgets.SaveAsync(gadget, cancellationToken: Ct))
            .Should()
            .Be(2, "親 1 件＋子 1 件が保存される");

        gadget
            .RowVer.Should()
            .NotBeNull("親の版が書き戻る（対応表の収集がサーバー側で VO を開けている証明）");
        gadget
            .RowVer.Value.Length.Should()
            .Be(8, "版は rowversion（インメモリは擬似版）の 8 バイト");
        note.RowVer.Should().NotBeNull("子の版も書き戻る");
        gadget.RowState.Should().Be(RowState.Unchanged, "保存後の状態確定はカスケードで行われる");
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
}
