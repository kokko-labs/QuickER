using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using QuickER.Tests.GeneratedSqlServerBinaryFixture;
using QuickER.Tests.Integration;
using Xunit;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// 楽観排他のランタイムスイートを<b>QuickER 版 Repository（SQL Server 方言）</b>で実 SQL Server
/// （Testcontainers・Docker 依存）に流す派生。
/// </summary>
/// <remarks>
/// <para>
/// 入力は <see cref="SqlServerBinaryFixtureDefinition"/>（<c>documents</c>＝rowversion <c>row_ver</c> を持つ・
/// 子 <c>document_notes</c> は 1対多カスケード）。DB が実際に <c>rowversion</c> を採番し、生成 SQL の
/// <c>WHERE ... AND [row_ver] = @original</c>＋<c>OUTPUT INSERTED</c> による版の回収が効く唯一の構成。
/// バックエンド非依存のシナリオは基底 <see cref="ConcurrencyRuntimeTestsBase{TEntity, TConflictException}"/> が持つ。
/// </para>
/// <para>
/// 「他者による更新」は生 SQL（<c>ExecuteSqlAsync</c>）で直接 UPDATE して作る＝Repository を経由しないため
/// 手元のエンティティの <c>RowVer</c> は古いまま残り、実際の競合と同じ状態を決定的に再現できる。
/// Docker 不在時は <see cref="SqlServerContainerFixture"/> の検出でスキップされる（CI では常にスキップ）。
/// </para>
/// </remarks>
[Collection(SqlServerContainerCollection.Name)]
[Trait("RequiresDocker", "true")]
public sealed class SqlServerConcurrencyRuntimeTests(SqlServerContainerFixture fixture)
    : ConcurrencyRuntimeTestsBase<DocumentEntity, SaveConflictException>,
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

    /// <summary>文書リポジトリを解決する</summary>
    private IDocumentRepository Documents() => _provider.GetRequiredService<IDocumentRepository>();

    /// <summary>メモリポジトリを解決する</summary>
    private IDocumentNoteRepository Notes() =>
        _provider.GetRequiredService<IDocumentNoteRepository>();

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
        await _fixture.ApplyDdlAsync(SqlServerBinaryFixtureDefinition.Build(), Ct);

        await Documents().InsertAsync(NewEntity(SeededRootId, "alpha"), Ct);
        await Documents().InsertAsync(NewEntity(SeededChildlessRootId, "beta"), Ct);
        await Notes()
            .InsertAsync(
                new DocumentNoteEntity
                {
                    NoteId = SeededChildId,
                    DocumentId = SeededRootId,
                    Note = "first",
                },
                Ct
            );
    }

    /// <summary>除外列（payload / thumb）は未取得状態のまま組み立てる（値を持つと UPDATE が拒否される既存仕様）</summary>
    protected override DocumentEntity NewEntity(int id, string title) =>
        new()
        {
            DocumentId = id,
            Title = title,
            Thumb = [],
        };

    protected override Task InsertAsync(DocumentEntity entity) =>
        Documents().InsertAsync(entity, Ct);

    protected override Task<DocumentEntity?> GetAsync(int id) => Documents().GetByIdAsync(id, Ct);

    protected override Task<DocumentEntity?> GetWithChildrenAsync(int id) =>
        Documents()
            .Query()
            .Where(d => d.DocumentId == id)
            .Include(d => d.DocumentNotes)
            .FirstOrDefaultAsync(Ct);

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
    ) => Documents().UpdateAsync(entity, Translate(mode), Ct);

    protected override Task<int> SaveAsync(
        DocumentEntity entity,
        ConcurrencyChoice mode = ConcurrencyChoice.Optimistic,
        bool insertWhenUpdateMissing = false
    ) =>
        Documents()
            .SaveAsync(
                entity,
                insertWhenUpdateMissing: insertWhenUpdateMissing,
                mode: Translate(mode),
                cancellationToken: Ct
            );

    protected override Task BumpByAnotherUserAsync(int id, string title) =>
        Documents()
            .ExecuteSqlAsync(
                "UPDATE documents SET title = @title WHERE document_id = @id",
                new { title, id },
                Ct
            );

    protected override void EditFirstChild(DocumentEntity root, string note)
    {
        var child = root.DocumentNotes.First();
        child.Note = note;
        child.MarkUpdated();
    }

    protected override async Task<string?> ReadChildNoteAsync(int noteId) =>
        (await Notes().GetByIdAsync(noteId, Ct))?.Note;
}
