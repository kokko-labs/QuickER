using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuickER.SqlServer;
using QuickER.Tests.GeneratedBinaryFixture;
using Xunit;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// 楽観排他のランタイムスイートを<b>EF Core 版 Repository</b>で実 SQL Server（Testcontainers・Docker 依存）に流す派生。
/// </summary>
/// <remarks>
/// <para>
/// EF Core は方言非依存のため、バイナリフィクスチャ（<see cref="BinaryFixtureDefinition"/>＝SQLite 方言のQuickER 版
/// Repository と併存生成）の EF Core リポジトリをそのまま SQL Server へ接続して使う。スキーマは同じ図から
/// <see cref="SqlServerDdlGenerator"/> で作る＝<c>row_ver</c> が実際に <c>rowversion</c> として自動採番され、
/// EF Core の <c>IsRowVersion()</c> による並行性トークンが実際に効く唯一の構成（既存の
/// <c>BinaryColumnEfCoreRuntimeTests</c> は SQLite のため <c>row_ver</c> が常に NULL のまま）。
/// </para>
/// <para>
/// EF Core は「影響行数 0」を <c>DbUpdateConcurrencyException</c> として 1 つにまとめて報告するため、
/// 生成コードは報告されたエントリごとに DB の現在値を読み直して「行が消えている（従来契約）」と
/// 「行は在るが版が進んでいる（競合）」を区別する。バックエンド非依存のシナリオは基底
/// <see cref="ConcurrencyRuntimeTestsBase{TEntity, TConflictException}"/> が持ち、本クラスは EF Core 固有の
/// 「フックあり経路で新しい版をコミット後まで反映しない」を検証する。
/// </para>
/// <para>
/// 「他者による更新」は生 SQL で直接 UPDATE して作る（EF Core を経由しないため手元のエンティティの
/// <c>RowVer</c> は古いまま残り、実際の競合と同じ状態を決定的に再現できる）。
/// Docker 不在時は <see cref="SqlServerContainerFixture"/> の検出でスキップされる。
/// </para>
/// </remarks>
[Collection(SqlServerContainerCollection.Name)]
[Trait("RequiresDocker", "true")]
public sealed class EfCoreConcurrencyRuntimeTests(SqlServerContainerFixture fixture)
    : ConcurrencyRuntimeTestsBase<DocumentEntity, SaveConflictException>,
        IAsyncLifetime
{
    /// <summary>共有する SQL Server コンテナ</summary>
    private readonly SqlServerContainerFixture _fixture = fixture;

    /// <summary>EF Core 版リポジトリ群を登録した DI コンテナ（UseSqlServer）</summary>
    private ServiceProvider _provider = null!;

    /// <summary>Save フックを登録した専用 DI コンテナ（テストごとに 1 つ作り、最後にまとめて破棄する）</summary>
    private readonly List<ServiceProvider> _hookProviders = [];

    /// <summary>Docker の有無を判定し、リポジトリ DI を構築する</summary>
    public ValueTask InitializeAsync()
    {
        Assert.SkipUnless(_fixture.IsAvailable, _fixture.UnavailableReason);

        _provider = BuildProvider();

        return ValueTask.CompletedTask;
    }

    /// <summary>DI コンテナを破棄する</summary>
    public ValueTask DisposeAsync()
    {
        foreach (var provider in _hookProviders)
        {
            provider.Dispose();
        }

        _provider?.Dispose();

        return ValueTask.CompletedTask;
    }

    /// <summary>EF Core 版リポジトリ群（UseSqlServer）の DI コンテナを構築する</summary>
    private ServiceProvider BuildProvider() =>
        new ServiceCollection()
            .AddGeneratedEfCoreRepositories(options =>
                options.UseSqlServer(_fixture.ConnectionString)
            )
            .BuildServiceProvider();

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
        await _fixture.ApplyDdlAsync(BinaryFixtureDefinition.Build(), Ct);

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
        _fixture.ExecuteAsync(
            $"UPDATE documents SET title = '{title}' WHERE document_id = {id}",
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

    // ── EF Core 固有: Save フック × 版の反映タイミング ──

    /// <summary>Save フックを 1 つ登録した EF Core 版文書リポジトリを解決する</summary>
    private IDocumentRepository DocumentsWithHook(ISaveHook<DocumentEntity> hook)
    {
        var provider = new ServiceCollection()
            .AddGeneratedEfCoreRepositories(options =>
                options.UseSqlServer(_fixture.ConnectionString)
            )
            .AddSingleton(hook)
            .BuildServiceProvider();

        _hookProviders.Add(provider);
        return provider.GetRequiredService<IDocumentRepository>();
    }

    /// <summary>
    /// フックあり経路では、EF が SaveChanges で書いた新しい版をコミット後まで反映しない。
    /// After はコミット前に走るので保存前の版を見て、After 例外でロールバックしてもエンティティには
    /// 「DB に存在しない版」が残らない（残ると同一インスタンスの再保存が偽の競合になる）。
    /// </summary>
    [Fact(
        DisplayName = "[Concurrency/EFCore] SaveAsync: After は旧版を見る・例外でロールバックしても幻の版が残らない"
    )]
    public async Task SaveAsync_WithHook_KeepsRowVersionUntilCommit()
    {
        await ResetAndSeedAsync();

        var hook = new RowVersionCapturingHook();
        var documents = DocumentsWithHook(hook);

        var document = await documents.GetByIdAsync(SeededRootId, Ct);
        var beforeSave = document!.RowVer;
        beforeSave.Should().NotBeNull("SQL Server の rowversion は取得時点で読める");

        document.Title = "by-me";
        document.MarkUpdated();

        var act = async () => await documents.SaveAsync(document, cancellationToken: Ct);
        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage("*after-boom*");

        hook.SeenRowVersion.Should()
            .Equal(beforeSave, "After はコミット前に走るので保存前の版が見える");
        document
            .RowVer.Should()
            .Equal(beforeSave, "ロールバックされたので DB に存在しない版は残らない");
        (await ReadTitleAsync(SeededRootId)).Should().Be("alpha", "行更新もロールバックされている");

        // 幻の版が残っていれば、同一インスタンスのこの再保存は偽の競合になる
        hook.ThrowOnAfter = false;
        (await documents.SaveAsync(document, cancellationToken: Ct)).Should().Be(1);

        document.RowVer.Should().NotEqual(beforeSave, "コミット成功後は新しい版が反映される");
        (await ReadTitleAsync(SeededRootId)).Should().Be("by-me");
    }

    /// <summary>After が見た版を記録し、任意で例外を投げる Save フック（版の反映タイミング検証用）</summary>
    private sealed class RowVersionCapturingHook : ISaveHook<DocumentEntity>
    {
        /// <summary>After で例外を投げるか（true の間は保存が丸ごとロールバックされる）</summary>
        public bool ThrowOnAfter { get; set; } = true;

        /// <summary>After が呼ばれた時点でエンティティが持っていた版</summary>
        public byte[]? SeenRowVersion { get; private set; }

        public Task AfterSaveAsync(
            DocumentEntity entity,
            SaveOperation operation,
            ISaveHookContext context,
            CancellationToken cancellationToken = default
        )
        {
            SeenRowVersion = entity.RowVer;

            if (ThrowOnAfter)
            {
                throw new InvalidOperationException("after-boom");
            }

            return Task.CompletedTask;
        }
    }
}
