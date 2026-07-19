using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using QuickER.SqlServer;
using QuickER.Tests.GeneratedSqlServerBinaryFixture;
using QuickER.Tests.Integration;
using Xunit;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// Save フック（<see cref="ISaveHook{TEntity}"/>）の<b>エンジン方言分岐</b>を、実 SQL Server（Testcontainers・Docker 依存）で
/// 検証する部分集合。SQLite 一時ファイル DB（<see cref="SaveHookAdoRuntimeTests"/>）では確認できない SQL Server 固有の
/// 参加モード書き込み（<c>SqlParameter(VarBinary,-1)</c>＝Stream 値のストリーミング送信）と、FK 制約の既定有効を突く。
/// </summary>
/// <remarks>
/// <para>
/// 入力は SQL Server 方言のバイナリフィクスチャ（<see cref="SqlServerBinaryFixtureDefinition"/>・SQLite 版と同一図）。
/// 検証観点は 3 つ——(3) After の <c>WriteBinaryColumnAsync</c> が同一トランザクションで blob を書きコミット後に読める、
/// (4) After 例外で行・blob・監査行がすべてロールバックされる（アトミック性）、(2) 新規親スキップ×新規子保存の FK 違反で
/// 全体ロールバック——で、いずれも SQL Server のトランザクション文脈でエンジンが正しく動くことを実証する。
/// </para>
/// <para>Docker 不在時は <see cref="SqlServerContainerFixture"/> の検出でスキップされる（CI では常にスキップ）。</para>
/// </remarks>
[Trait("Category", "Integration")]
[Collection(SqlServerContainerCollection.Name)]
[Trait("RequiresDocker", "true")]
public sealed class SaveHookSqlServerRuntimeTests(SqlServerContainerFixture fixture)
    : IAsyncLifetime
{
    private readonly SqlServerContainerFixture _fixture = fixture;
    private readonly List<ServiceProvider> _providers = [];
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private static readonly byte[] Doc1Payload = [1, 2, 3, 4];

    /// <summary>スキーマ（＋監査テーブル）を作成し、Repository の InsertAsync でシードする</summary>
    public async ValueTask InitializeAsync()
    {
        Assert.SkipUnless(_fixture.IsAvailable, _fixture.UnavailableReason);

        await _fixture.ResetSchemaAsync(Ct);
        await _fixture.ExecuteAsync(
            new SqlServerDdlGenerator().Build(SqlServerBinaryFixtureDefinition.Build()),
            Ct
        );
        await _fixture.ExecuteAsync(
            "CREATE TABLE audit (audit_id INT IDENTITY(1,1) PRIMARY KEY, note NVARCHAR(200) NOT NULL);",
            Ct
        );

        var documents = BuildProvider().GetRequiredService<IDocumentRepository>();
        await documents.InsertAsync(NewDocument(1, "alpha", Doc1Payload, [9, 9]), Ct);
        await documents.InsertAsync(NewDocument(2, "beta", null, [8]), Ct);
    }

    public ValueTask DisposeAsync()
    {
        foreach (var provider in _providers)
        {
            provider.Dispose();
        }

        return ValueTask.CompletedTask;
    }

    /// <summary>指定した Save フックを登録した DI プロバイダを構築する</summary>
    private ServiceProvider BuildProvider(params object[] hooks)
    {
        var services = new ServiceCollection().AddGeneratedSqlServerRepositories(
            _fixture.ConnectionString
        );

        foreach (var hook in hooks)
        {
            if (hook is ISaveHook<DocumentEntity> documentHook)
            {
                services.AddSingleton(documentHook);
            }
            else if (hook is ISaveHook<DocumentNoteEntity> noteHook)
            {
                services.AddSingleton(noteHook);
            }
        }

        var provider = services.BuildServiceProvider();
        _providers.Add(provider);
        return provider;
    }

    private static DocumentEntity NewDocument(
        int id,
        string title,
        byte[]? payload,
        byte[] thumb
    ) =>
        new()
        {
            DocumentId = id,
            Title = title,
            Payload = payload,
            Thumb = thumb,
        };

    private static DocumentNoteEntity NewNote(int id, int documentId, string note) =>
        new()
        {
            NoteId = id,
            DocumentId = documentId,
            Note = note,
        };

    private async Task<long> ScalarAsync(string sql) =>
        await BuildProvider()
            .GetRequiredService<IDocumentRepository>()
            .ExecuteScalarSqlAsync<long>(sql, null, Ct);

    /// <summary>3. After の WriteBinaryColumnAsync が同一トランザクションで blob を書き、コミット後に読める（SQL Server 参加モード）</summary>
    [Fact(
        DisplayName = "[SaveHook/SqlServer] 3: After の WriteBinaryColumnAsync が Save 後に読める"
    )]
    public async Task After_WritesBinaryColumn_VisibleAfterCommit()
    {
        var newPayload = new byte[256 * 1024];
        new Random(7).NextBytes(newPayload);

        var hook = new DelegateHook<DocumentEntity>
        {
            AfterAction = async (entity, _, context) =>
                await context.WriteBinaryColumnAsync(
                    nameof(DocumentEntity.Payload),
                    entity.DocumentId,
                    new MemoryStream(newPayload),
                    cancellationToken: Ct
                ),
        };
        var documents = BuildProvider(hook).GetRequiredService<IDocumentRepository>();

        var doc = await documents.GetByIdAsync(1, Ct);
        doc!.Title = "alpha-hooked";
        doc.MarkUpdated();
        (await documents.SaveAsync(doc, cancellationToken: Ct)).Should().BeGreaterThan(0);

        var readBack = await documents
            .Query()
            .Where(d => d.DocumentId == 1)
            .WithUnboundedBinary()
            .FirstOrDefaultAsync(Ct);
        readBack!
            .Payload.Should()
            .Equal(newPayload, "After が同一トランザクションで書いた payload が読める");
    }

    /// <summary>4. After 例外で行・blob・監査行がすべてロールバックされる（SQL Server のトランザクションで原子的）</summary>
    [Fact(DisplayName = "[SaveHook/SqlServer] 4: After 例外で行・blob・監査行がロールバックされる")]
    public async Task After_Throws_RollsBackRowBlobAndAudit()
    {
        var hook = new DelegateHook<DocumentEntity>
        {
            AfterAction = async (entity, _, context) =>
            {
                await context.ExecuteSqlAsync(
                    "INSERT INTO audit (note) VALUES (@note)",
                    new { note = "should-not-persist" },
                    Ct
                );
                await context.WriteBinaryColumnAsync(
                    nameof(DocumentEntity.Payload),
                    entity.DocumentId,
                    new MemoryStream([42, 42, 42, 42, 42]),
                    cancellationToken: Ct
                );
                throw new InvalidOperationException("after-boom");
            },
        };
        var documents = BuildProvider(hook).GetRequiredService<IDocumentRepository>();

        var doc = await documents.GetByIdAsync(1, Ct);
        doc!.Title = "alpha-doomed";
        doc.MarkUpdated();

        var act = () => documents.SaveAsync(doc, cancellationToken: Ct);
        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage("*after-boom*");

        (await ScalarAsync("SELECT COUNT_BIG(*) FROM documents WHERE title = 'alpha-doomed'"))
            .Should()
            .Be(0, "タイトル更新はロールバックされた");
        (await ScalarAsync("SELECT COUNT_BIG(*) FROM audit"))
            .Should()
            .Be(0, "監査行はロールバックされた");
        (await ScalarAsync("SELECT DATALENGTH(payload) FROM documents WHERE document_id = 1"))
            .Should()
            .Be(Doc1Payload.Length, "payload の blob は元のまま（書き込みはロールバック）");
    }

    /// <summary>2. 新規親スキップ×新規子保存は FK 違反で全体ロールバックする（SQL Server は FK 既定有効）</summary>
    [Fact(
        DisplayName = "[SaveHook/SqlServer] 2: 親スキップ×子保存は FK 違反で全体ロールバックする"
    )]
    public async Task Parent_Skipped_ChildSaved_ForeignKeyRollsBackAll()
    {
        var hook = new DelegateHook<DocumentEntity>
        {
            BeforePredicate = (_, op) => op != SaveOperation.Insert,
        };
        var documents = BuildProvider(hook).GetRequiredService<IDocumentRepository>();

        var parent = NewDocument(50, "orphan-parent", null, [1]);
        parent.MarkAdded();
        var child = NewNote(500, 50, "orphan-child");
        child.MarkAdded();
        parent.DocumentNotes.Add(child);

        var act = () => documents.SaveAsync(parent, cancellationToken: Ct);
        await act.Should().ThrowAsync<SqlException>();

        (await ScalarAsync("SELECT COUNT_BIG(*) FROM documents WHERE document_id = 50"))
            .Should()
            .Be(0);
        (await ScalarAsync("SELECT COUNT_BIG(*) FROM document_notes WHERE note_id = 500"))
            .Should()
            .Be(0);
    }

    /// <summary>Before の返り値・After の副作用を差し込めるテスト用フック</summary>
    private sealed class DelegateHook<TEntity> : ISaveHook<TEntity>
        where TEntity : EntityBase
    {
        public Func<TEntity, SaveOperation, bool>? BeforePredicate { get; init; }
        public Func<TEntity, SaveOperation, ISaveHookContext, Task>? AfterAction { get; init; }

        public Task<bool> BeforeSaveAsync(
            TEntity entity,
            SaveOperation operation,
            CancellationToken cancellationToken = default
        ) => Task.FromResult(BeforePredicate?.Invoke(entity, operation) ?? true);

        public async Task AfterSaveAsync(
            TEntity entity,
            SaveOperation operation,
            ISaveHookContext context,
            CancellationToken cancellationToken = default
        )
        {
            if (AfterAction is not null)
            {
                await AfterAction(entity, operation, context);
            }
        }
    }
}
