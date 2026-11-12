using System.Linq;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using QuickER.Tests.GeneratedSqlServerBinaryFixture;
using QuickER.Tests.TestSupport;
using Xunit;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// 楽観排他のランタイムスイートを<b>実 HTTP のリモート 3 階層</b>で流す派生のうち、サーバー実体が
/// <b>QuickER 版 Repository（SQL Server 方言）× 実 SQL Server</b>（Testcontainers・Docker 依存）のもの。
/// </summary>
/// <remarks>
/// <para>
/// 図は値オブジェクトなしの SQL Server バイナリフィクスチャ（<see cref="SqlServerBinaryFixtureDefinition"/>）で、版は生の
/// <c>byte[]</c>。DB が採番した実 rowversion が、サーバーの回収（<c>OUTPUT INSERTED</c>・コミット後の適用）→応答の版対応表→
/// クライアントの書き戻しを通ること、版が古い更新・削除が DB の版条件で断られ、行なしと版が古いの区別（実在確認）が
/// HTTP 409 を越えて同じ競合例外に戻ることを、インメモリの擬似の版でなく本物の版で確かめる。
/// </para>
/// <para>
/// 基底 <see cref="ConcurrencyRuntimeTestsBase{TEntity, TConflictException}"/> のシナリオに加えて、グラフ保存の親子の版対応表と
/// 複数ルートの版の書き戻しを持つ。要求の解釈だけで決まるリモートの検証（旧エンベロープ・未定義の Mode の 400）は
/// リポジトリの実装先に依らないので、インメモリのサーバーの <see cref="RemoteConcurrencyRuntimeTests"/> だけが持つ。
/// 除外列（payload / thumb）は、挿入で NOT NULL の thumb に空の値を入れ、取得後は未取得のまま扱う。
/// </para>
/// </remarks>
[Collection(SqlServerContainerCollection.Name)]
[Trait("RequiresDocker", "true")]
[RuntimeCell(
    RuntimeFeatures.Concurrency,
    RuntimeTarget.Remote,
    Server = RuntimeTarget.AdoSqlServer
)]
public sealed class ConcurrencyRemoteAdoSqlServerRuntimeTests(SqlServerContainerFixture fixture)
    : ConcurrencyRuntimeTestsBase<DocumentEntity, SaveConflictException>,
        IAsyncLifetime
{
    /// <summary>共有する SQL Server コンテナ</summary>
    private readonly SqlServerContainerFixture _fixture = fixture;

    private InProcessRemoteServer? _server;
    private ServiceProvider? _clientProvider;

    /// <summary>Docker の有無を判定し、Kestrel（サーバー実体は QuickER 版の SQL Server リポジトリ）と HTTP クライアントを用意する</summary>
    public async ValueTask InitializeAsync()
    {
        Assert.SkipUnless(_fixture.IsAvailable, _fixture.UnavailableReason);

        _server = await InProcessRemoteServer.StartAsync(
            services => services.AddGeneratedSqlServerRepositories(_fixture.ConnectionString),
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

    /// <summary>クライアント側の文書リモート面を解決する</summary>
    private IDocumentRemoteRepository Documents =>
        _clientProvider!.GetRequiredService<IDocumentRemoteRepository>();

    /// <summary>クライアント側のメモリモート面を解決する</summary>
    private IDocumentNoteRemoteRepository Notes =>
        _clientProvider!.GetRequiredService<IDocumentNoteRemoteRepository>();

    /// <summary>中立表現の楽観排他ポリシーを、このフィクスチャの <c>ConcurrencyMode</c> へ翻訳する</summary>
    private static ConcurrencyMode Translate(ConcurrencyChoice choice) =>
        choice switch
        {
            ConcurrencyChoice.Optimistic => ConcurrencyMode.Optimistic,
            ConcurrencyChoice.ForceOverwrite => ConcurrencyMode.ForceOverwrite,
            _ => (ConcurrencyMode)99,
        };

    /// <summary>スキーマを作り直して空にする</summary>
    private async Task ClearStoreAsync()
    {
        await _fixture.ResetSchemaAsync(Ct);
        await _fixture.ApplyDdlAsync(SqlServerBinaryFixtureDefinition.Build(), Ct);
    }

    /// <summary>保存先を空にしてから、リモート経由でシードを投入する</summary>
    protected override async Task ResetAndSeedAsync()
    {
        await ClearStoreAsync();
        await Documents.InsertAsync(NewEntity(SeededRootId, "alpha"), Ct);
        await Documents.InsertAsync(NewEntity(SeededChildlessRootId, "beta"), Ct);
        await Notes.InsertAsync(NewNote(SeededChildId, SeededRootId, "first"), Ct);
    }

    /// <summary>NOT NULL の除外列 thumb に空の値を入れて組み立てる（挿入は全列を書く）</summary>
    protected override DocumentEntity NewEntity(int id, string title) =>
        new()
        {
            DocumentId = id,
            Title = title,
            Thumb = [],
        };

    /// <summary>メモ（子）を組み立てる</summary>
    private static DocumentNoteEntity NewNote(int noteId, int documentId, string note) =>
        new()
        {
            NoteId = noteId,
            DocumentId = documentId,
            Note = note,
        };

    protected override Task InsertAsync(DocumentEntity entity) => Documents.InsertAsync(entity, Ct);

    protected override Task<DocumentEntity?> GetAsync(int id) => Documents.GetByIdAsync(id, Ct);

    /// <summary>リモート面は <c>Query()</c> を持たないため、親と子を別々に取得してグラフを組み立てる</summary>
    protected override async Task<DocumentEntity?> GetWithChildrenAsync(int id)
    {
        var root = await Documents.GetByIdAsync(id, Ct);

        if (root is null)
        {
            return null;
        }

        var child = await Notes.GetByIdAsync(SeededChildId, Ct);

        if (child is not null)
        {
            root.DocumentNotes.Add(child);
        }

        return root;
    }

    protected override string GetTitle(DocumentEntity entity) => entity.Title;

    protected override void SetTitle(DocumentEntity entity, string title) => entity.Title = title;

    protected override byte[]? GetRowVersion(DocumentEntity entity) => entity.RowVer;

    protected override void SetRowVersion(DocumentEntity entity, byte[] rowVersion) =>
        entity.RowVer = rowVersion;

    protected override void MarkAdded(DocumentEntity entity) => entity.MarkAdded();

    protected override void MarkUpdated(DocumentEntity entity) => entity.MarkUpdated();

    protected override void MarkRemoved(DocumentEntity entity) => entity.MarkRemoved();

    protected override Task<bool> UpdateAsync(
        DocumentEntity entity,
        ConcurrencyChoice mode = ConcurrencyChoice.Optimistic
    ) => Documents.UpdateAsync(entity, Translate(mode), Ct);

    protected override Task<int> SaveAsync(
        DocumentEntity entity,
        ConcurrencyChoice mode = ConcurrencyChoice.Optimistic,
        bool insertWhenUpdateMissing = false
    ) =>
        Documents.SaveAsync(
            entity,
            insertWhenUpdateMissing: insertWhenUpdateMissing,
            mode: Translate(mode),
            cancellationToken: Ct
        );

    /// <summary>別途取得した最新インスタンス経由で更新する（手元のインスタンスの版は古いまま残る）</summary>
    protected override async Task BumpByAnotherUserAsync(int id, string title)
    {
        var fresh = await Documents.GetByIdAsync(id, Ct);
        fresh.Should().NotBeNull();

        fresh!.Title = title;
        (await Documents.UpdateAsync(fresh, cancellationToken: Ct)).Should().BeTrue();
    }

    protected override void EditFirstChild(DocumentEntity root, string note)
    {
        var child = root.DocumentNotes.First();
        child.Note = note;
        child.MarkUpdated();
    }

    protected override (string Reason, string? EntityTypeName, string? Key) Describe(
        SaveConflictException exception
    ) => (exception.Reason.ToString(), exception.EntityTypeName, exception.Key);

    protected override async Task<string?> ReadChildNoteAsync(int noteId) =>
        (await Notes.GetByIdAsync(noteId, Ct))?.Note;

    // ── リモート固有: グラフ保存の版対応表 ──

    /// <summary>グラフ保存の応答は親の実 rowversion を運び（子の表は版を持たない）、同じ親子のグラフをそのまま 2 回目も保存できる</summary>
    [Fact(
        DisplayName = "[Concurrency/Remote/SQL Server] SaveAsync の応答で版が書き戻り同じ親子のグラフを続けて保存できる"
    )]
    public async Task SaveAsync_WritesBackRowVersion_AndAllowsConsecutiveSaves()
    {
        await ClearStoreAsync();

        var document = NewEntity(SeededRootId, "alpha");
        document.MarkAdded();

        var note = NewNote(SeededChildId, SeededRootId, "first");
        note.MarkAdded();
        document.DocumentNotes.Add(note);

        (await Documents.SaveAsync(document, cancellationToken: Ct))
            .Should()
            .Be(2, "親 1 件＋子 1 件が保存される");

        document.RowVer.Should().NotBeNull("応答の版対応表から手元のエンティティへ書き戻される");
        document.RowVer!.Length.Should().Be(8, "DB が採番した rowversion");
        document.RowState.Should().Be(RowState.Unchanged, "保存後の状態確定はカスケードで行われる");
        note.RowState.Should().Be(RowState.Unchanged);
        var afterInsert = document.RowVer;

        // 2 回目: 再取得せずそのまま保存できる＝書き戻された版がサーバーの現在値と一致している証明
        document.Title = "beta";
        document.MarkUpdated();
        note.Note = "second";
        note.MarkUpdated();

        (await Documents.SaveAsync(document, cancellationToken: Ct))
            .Should()
            .Be(2, "書き戻された版で版チェックが通る");
        document.RowVer.Should().NotEqual(afterInsert, "保存のたびに新しい版が反映される");
    }

    /// <summary>複数ルートの保存でもルートごとに実 rowversion が書き戻される</summary>
    [Fact(
        DisplayName = "[Concurrency/Remote/SQL Server] SaveMany は複数ルートそれぞれへ版を書き戻す"
    )]
    public async Task SaveManyAsync_WritesBackRowVersionPerRoot()
    {
        await ClearStoreAsync();

        var first = NewEntity(1, "alpha");
        var second = NewEntity(2, "beta");
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
}
