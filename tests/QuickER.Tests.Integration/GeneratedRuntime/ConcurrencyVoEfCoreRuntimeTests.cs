using System.Linq;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuickER.Tests.GeneratedConcurrencyFixture;
using QuickER.Tests.Integration;
using Xunit;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// 楽観排他のランタイムスイートを <b>rowversion 列 × 値オブジェクト</b>の図で、<b>EF Core 版 Repository</b>から
/// 実 SQL Server（Testcontainers・Docker 依存）へ流す派生。
/// </summary>
/// <remarks>
/// <para>
/// Fluent 構成が <c>.IsRowVersion().HasConversion(v =&gt; v!.Value, v =&gt; RowVerValue.Create(v!))</c> と
/// 併記される唯一の構成（<see cref="EfCoreConcurrencyRuntimeTests"/> は VO なしの素の <c>byte[]</c>）。
/// 「並行性トークン × 値コンバータ」が実 DB で成立すること＝EF Core が DB 採番の版を VO へ復元し、
/// 版比較（<c>WHERE row_ver = @original</c> 相当）が効くことを、基底
/// <see cref="ConcurrencyRuntimeTestsBase{TEntity, TConflictException}"/> の全シナリオで実証する。
/// </para>
/// <para>
/// EF Core はリフレクション代入を通らない（変更追跡が型付きで行う）ため、本テストは書き戻し不具合そのものの
/// 回帰ではなく「VO × rowversion で EF Core の並行性機構が壊れていない」ことの担保。
/// 「他者による更新」は生 SQL で直接 UPDATE して作る。Docker 不在時はスキップされる。
/// </para>
/// </remarks>
[Collection(SqlServerContainerCollection.Name)]
[Trait("RequiresDocker", "true")]
public sealed class ConcurrencyVoEfCoreRuntimeTests(SqlServerContainerFixture fixture)
    : ConcurrencyRuntimeTestsBase<GadgetEntity, SaveConflictException>,
        IAsyncLifetime
{
    /// <summary>共有する SQL Server コンテナ</summary>
    private readonly SqlServerContainerFixture _fixture = fixture;

    /// <summary>EF Core 版リポジトリ群を登録した DI コンテナ（UseSqlServer）</summary>
    private ServiceProvider _provider = null!;

    /// <summary>Docker の有無を判定し、リポジトリ DI を構築する</summary>
    public ValueTask InitializeAsync()
    {
        Assert.SkipUnless(_fixture.IsAvailable, _fixture.UnavailableReason);

        _provider = new ServiceCollection()
            .AddGeneratedEfCoreRepositories(options =>
                options.UseSqlServer(_fixture.ConnectionString)
            )
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
        _fixture.ExecuteAsync($"UPDATE gadgets SET name = '{title}' WHERE gadget_id = {id}", Ct);

    protected override void EditFirstChild(GadgetEntity root, string note)
    {
        var child = root.GadgetNotes.First();
        child.Note = NoteValue.Create(note);
        child.MarkUpdated();
    }

    protected override async Task<string?> ReadChildNoteAsync(int noteId) =>
        (await Notes().GetByIdAsync(NoteIdValue.Create(noteId), Ct))?.Note.Value;

    // ── VO 固有: 値コンバータ越しの版の復元 ──

    /// <summary>DB 採番の版が値コンバータ経由で <c>RowVerValue</c> として復元され、カスケード子の版も反映される</summary>
    [Fact(DisplayName = "[Concurrency/VO/EFCore] 版反映: 版が VO へ復元され子の版も反映される")]
    public async Task RowVersion_IsMaterializedAsValueObject_ForRootAndChildren()
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

        gadget.RowVer.Should().NotBeNull("DB 採番の版が VO へ復元される（値コンバータ経由）");
        gadget.RowVer.Value.Length.Should().Be(8, "SQL Server の rowversion は 8 バイト");
        note.RowVer.Should().NotBeNull("子の版も反映される");

        gadget.Name = NameValue.Create("parent-2");
        gadget.MarkUpdated();
        note.Note = NoteValue.Create("child-2");
        note.MarkUpdated();

        (await Gadgets().SaveAsync(gadget, cancellationToken: Ct))
            .Should()
            .Be(2, "反映された版で版チェックが通る");
    }
}
