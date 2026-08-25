using System.Linq;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using QuickER.Tests.GeneratedConcurrencyFixture;
using QuickER.Tests.Integration;
using Xunit;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// 楽観排他のランタイムスイートを <b>rowversion 列 × 値オブジェクト</b>の図で、QuickER 版 Repository
/// （SQL Server 方言）から実 SQL Server（Testcontainers・Docker 依存）へ流す派生。
/// </summary>
/// <remarks>
/// <para>
/// VO 有効時の rowversion プロパティは <c>RowVerValue</c> になる。DB が <c>OUTPUT INSERTED</c> で返すのは生の
/// <c>byte[]</c> なので、書き戻しの <c>PropertyInfo.SetValue</c> は VO へ包み直さなければ <c>ArgumentException</c> に
/// なる（実バグ＝UPDATE はコミット済みなのに保存が例外・手元の版は古いまま＝次回保存が偽の競合）。
/// 基底 <see cref="ConcurrencyRuntimeTestsBase{TEntity, TConflictException}"/> のシナリオはアダプタで VO を開閉するため、
/// それが緑になること自体が包み直しの実証になる。本クラスは加えて「版が VO 型として載る」ことと、
/// カスケード子の版も書き戻ることを VO 固有の観点として持つ。
/// </para>
/// <para>
/// 「他者による更新」は生 SQL（<c>ExecuteSqlAsync</c>）で直接 UPDATE して作る＝Repository を経由しないため
/// 手元のエンティティの <c>RowVer</c> は古いまま残る。Docker 不在時は
/// <see cref="SqlServerContainerFixture"/> の検出でスキップされる。
/// </para>
/// </remarks>
[Collection(SqlServerContainerCollection.Name)]
[Trait("RequiresDocker", "true")]
public sealed class ConcurrencyVoSqlServerRuntimeTests(SqlServerContainerFixture fixture)
    : ConcurrencyRuntimeTestsBase<GadgetEntity, SaveConflictException>,
        IAsyncLifetime
{
    /// <summary>共有する SQL Server コンテナ</summary>
    private readonly SqlServerContainerFixture _fixture = fixture;

    /// <summary>QuickER の SQL Server リポジトリ群を登録した DI コンテナ</summary>
    private ServiceProvider _provider = null!;

    /// <summary>Docker の有無を判定し、リポジトリ DI を構築する</summary>
    public ValueTask InitializeAsync()
    {
        Assert.SkipUnless(_fixture.IsAvailable, _fixture.UnavailableReason);

        _provider = new ServiceCollection()
            .AddGeneratedSqlServerRepositories(_fixture.ConnectionString)
            .BuildServiceProvider();

        return ValueTask.CompletedTask;
    }

    /// <summary>DI コンテナを破棄する</summary>
    public ValueTask DisposeAsync()
    {
        _provider?.Dispose();

        return ValueTask.CompletedTask;
    }

    /// <summary>gadget リポジトリを解決する</summary>
    private IGadgetRepository Gadgets() => _provider.GetRequiredService<IGadgetRepository>();

    /// <summary>メモリポジトリを解決する</summary>
    private IGadgetNoteRepository Notes() => _provider.GetRequiredService<IGadgetNoteRepository>();

    /// <summary>中立表現の楽観排他ポリシーを、このフィクスチャの <c>ConcurrencyMode</c> へ翻訳する</summary>
    private static ConcurrencyMode Translate(ConcurrencyChoice choice) =>
        choice switch
        {
            ConcurrencyChoice.Optimistic => ConcurrencyMode.Optimistic,
            ConcurrencyChoice.ForceOverwrite => ConcurrencyMode.ForceOverwrite,
            _ => (ConcurrencyMode)99,
        };

    protected override async Task ResetAndSeedAsync()
    {
        await _fixture.ResetSchemaAsync(Ct);
        await _fixture.ApplyDdlAsync(ConcurrencyFixtureDefinition.Build(), Ct);

        await Gadgets().InsertAsync(NewEntity(SeededRootId, "alpha"), Ct);
        await Gadgets().InsertAsync(NewEntity(SeededChildlessRootId, "beta"), Ct);
        await Notes().InsertAsync(NewNote(SeededChildId, SeededRootId, "first"), Ct);
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

    protected override Task InsertAsync(GadgetEntity entity) => Gadgets().InsertAsync(entity, Ct);

    protected override Task<GadgetEntity?> GetAsync(int id) =>
        Gadgets().GetByIdAsync(GadgetIdValue.Create(id), Ct);

    protected override Task<GadgetEntity?> GetWithChildrenAsync(int id)
    {
        var key = GadgetIdValue.Create(id);

        return Gadgets()
            .Query()
            .Where(g => g.GadgetId == key)
            .Include(g => g.GadgetNotes)
            .FirstOrDefaultAsync(Ct);
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
    ) => Gadgets().UpdateAsync(entity, Translate(mode), Ct);

    protected override Task<int> SaveAsync(
        GadgetEntity entity,
        ConcurrencyChoice mode = ConcurrencyChoice.Optimistic,
        bool insertWhenUpdateMissing = false
    ) =>
        Gadgets()
            .SaveAsync(
                entity,
                insertWhenUpdateMissing: insertWhenUpdateMissing,
                mode: Translate(mode),
                cancellationToken: Ct
            );

    protected override Task BumpByAnotherUserAsync(int id, string title) =>
        Gadgets()
            .ExecuteSqlAsync(
                "UPDATE gadgets SET name = @name WHERE gadget_id = @id",
                new { name = title, id },
                Ct
            );

    protected override void EditFirstChild(GadgetEntity root, string note)
    {
        var child = root.GadgetNotes.First();
        child.Note = NoteValue.Create(note);
        child.MarkUpdated();
    }

    protected override async Task<string?> ReadChildNoteAsync(int noteId) =>
        (await Notes().GetByIdAsync(NoteIdValue.Create(noteId), Ct))?.Note.Value;

    // ── VO 固有 1: 版が VO 型として載る ──

    /// <summary>DB 採番の版が生の <c>byte[]</c> ではなく <c>RowVerValue</c> として同一インスタンスへ書き戻る</summary>
    /// <remarks>旧実装は INSERT の <c>OUTPUT INSERTED</c> 戻り値を包み直さず <c>ArgumentException</c> になっていた。</remarks>
    [Fact(DisplayName = "[Concurrency/VO/SqlServer] 版反映: 採番された版が VO 型で書き戻る")]
    public async Task RowVersion_IsWrittenBack_AsValueObject()
    {
        await ResetAndSeedAsync();

        var entity = NewEntity(7, "inserted");
        await InsertAsync(entity);

        entity.RowVer.Should().NotBeNull("OUTPUT INSERTED で採番された版が VO として入る");
        entity.RowVer.Value.Length.Should().Be(8, "SQL Server の rowversion は 8 バイト");
    }

    // ── VO 固有 2: カスケード子の版 ──

    /// <summary>グラフ保存では子（カスケード）の版も VO 型で書き戻り、そのまま続けて保存できる</summary>
    [Fact(DisplayName = "[Concurrency/VO/SqlServer] 版反映: グラフ保存は子の版も書き戻す")]
    public async Task SaveAsync_WritesBackRowVersion_ForCascadedChildren()
    {
        await ResetAndSeedAsync();

        var gadget = NewEntity(8, "parent");
        gadget.MarkAdded();

        var note = NewNote(80, 8, "child");
        note.MarkAdded();
        gadget.GadgetNotes.Add(note);

        (await Gadgets().SaveAsync(gadget, cancellationToken: Ct))
            .Should()
            .Be(2, "親 1 件＋子 1 件が保存される");

        gadget.RowVer.Should().NotBeNull("親の版が書き戻る");
        note.RowVer.Should().NotBeNull("子の版も書き戻る");

        // 2 回目: 再読込せずそのまま保存できる＝親子とも版が DB の現在値と一致している
        gadget.Name = NameValue.Create("parent-2");
        gadget.MarkUpdated();
        note.Note = NoteValue.Create("child-2");
        note.MarkUpdated();

        (await Gadgets().SaveAsync(gadget, cancellationToken: Ct))
            .Should()
            .Be(2, "書き戻された版で版チェックが通る");
    }
}
