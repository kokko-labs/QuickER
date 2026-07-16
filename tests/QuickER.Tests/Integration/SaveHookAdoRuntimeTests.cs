using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using QuickER.Sqlite;
using QuickER.Tests.GeneratedBinaryFixture;
using Xunit;

namespace QuickER.Tests.Integration;

/// <summary>
/// Save フック（<see cref="ISaveHook{TEntity}"/>）を<b>QuickER 版 Repository の SQLite 実装</b>（<c>AddGeneratedSqliteRepositories</c>）で
/// 実 SQLite（一時ファイル DB・Docker 不要＝CI 常時実行）に流して検証する。
/// </summary>
/// <remarks>
/// <para>
/// バックエンド非依存のシナリオ（スキップ・短絡順序・insertWhenUpdateMissing・サブツリー削除・素通り・no-op・
/// IEnumerable 形態・After 例外時の残留）は基底 <see cref="SaveHookRuntimeTestsBase"/> が持ち、本クラスは
/// QuickER 版 Repository 固有の 1 トランザクション原子性（FK 違反ロールバック・After の同一トランザクション書き込み＝
/// 除外列 blob と生 SQL 監査行のアトミック性）を検証する。
/// </para>
/// <para>
/// FK 制約を Repository の接続でも効かせるため、接続文字列で <c>Foreign Keys=True</c> を明示する
/// （Microsoft.Data.Sqlite は既定で FK を有効化しないため）。
/// </para>
/// </remarks>
public sealed class SaveHookAdoRuntimeTests : SaveHookRuntimeTestsBase, IDisposable
{
    private readonly SqliteTempDatabase _db = SqliteTempDatabase.Create();
    private readonly List<ServiceProvider> _providers = [];

    /// <summary>Repository の接続でも FK を効かせた書き込み可能接続文字列</summary>
    private string ConnectionString =>
        new SqliteConnectionStringBuilder(_db.ReadWriteCreateConnectionString)
        {
            ForeignKeys = true,
        }.ConnectionString;

    /// <summary>QuickER 版 Repository は 1 トランザクションのため After 例外で保存変更は残らない</summary>
    protected override bool AfterExceptionLeavesResidue => false;

    /// <summary>指定した Save フック群を登録した DI プロバイダを構築する（テスト終了時にまとめて破棄）</summary>
    private ServiceProvider BuildProvider(params object[] hooks)
    {
        var services = new ServiceCollection().AddGeneratedSqliteRepositories(ConnectionString);
        RegisterHooks(services, hooks);

        var provider = services.BuildServiceProvider();
        _providers.Add(provider);
        return provider;
    }

    /// <summary>フック群を対象エンティティ型ごとに DI へ登録する（派生で共有する登録規約）</summary>
    internal static void RegisterHooks(IServiceCollection services, object[] hooks)
    {
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
            else
            {
                throw new InvalidOperationException($"未知のフック型: {hook.GetType()}");
            }
        }
    }

    protected override IDocumentRepository Documents(params object[] hooks) =>
        BuildProvider(hooks).GetRequiredService<IDocumentRepository>();

    protected override IDocumentNoteRepository Notes(params object[] hooks) =>
        BuildProvider(hooks).GetRequiredService<IDocumentNoteRepository>();

    /// <summary>スキーマ（＋監査テーブル）を作成し、共通シードを投入する（フックなしのプロバイダ経由）</summary>
    protected override async Task ResetAndSeedAsync()
    {
        await using (var conn = new SqliteConnection(ConnectionString))
        {
            await conn.OpenAsync(Ct);

            await using var drop = conn.CreateCommand();
            drop.CommandText =
                "DROP TABLE IF EXISTS \"document_notes\"; DROP TABLE IF EXISTS \"documents\"; DROP TABLE IF EXISTS \"audit\";";
            await drop.ExecuteNonQueryAsync(Ct);
        }

        var ddl = new SqliteDdlGenerator().Build(BinaryFixtureDefinition.Build());
        await _db.ApplyDdlAsync(ddl, Ct);

        // After フックの生 SQL（ExecuteSqlAsync）のアトミック性を検証するための監査テーブル
        await using (var conn = new SqliteConnection(ConnectionString))
        {
            await conn.OpenAsync(Ct);

            await using var create = conn.CreateCommand();
            create.CommandText =
                "CREATE TABLE \"audit\" (\"audit_id\" INTEGER PRIMARY KEY AUTOINCREMENT, \"note\" TEXT NOT NULL);";
            await create.ExecuteNonQueryAsync(Ct);
        }

        var documents = Documents();
        var notes = Notes();

        await documents.InsertAsync(NewDocument(1, "alpha", Doc1Payload, [9, 9]), Ct);
        await documents.InsertAsync(NewDocument(2, "beta", null, [8]), Ct);
        await documents.InsertAsync(NewDocument(3, "gamma", [5, 6], [6]), Ct);
        await notes.InsertAsync(NewNote(100, 1, "note-a"), Ct);
        await notes.InsertAsync(NewNote(101, 1, "note-b"), Ct);
    }

    /// <summary>行の存在を数える生 SQL ヘルパー（QuickER 版 Repository の生 SQL 経路）</summary>
    private async Task<long> ScalarAsync(string sql, object? parameters = null) =>
        await Documents().ExecuteScalarSqlAsync<long>(sql, parameters, Ct);

    // ── 2. 新規親スキップ × 新規子保存 → FK → 全ロールバック ──

    /// <summary>2. 新規親をスキップしつつ新規子を保存しようとすると FK 違反 → 例外 → 全体ロールバック（SQLite FK 有効）</summary>
    [Fact(DisplayName = "[SaveHook/Ado] 2: 親スキップ×子保存は FK 違反で全体ロールバックする")]
    public async Task Parent_Skipped_ChildSaved_ForeignKeyRollsBackAll()
    {
        await ResetAndSeedAsync();

        var hook = new RecordingHook<DocumentEntity>("h", [], _ => 0)
        {
            // 親（新規）の挿入をスキップする
            BeforePredicate = (_, op) => op != SaveOperation.Insert,
        };
        var documents = Documents(hook);

        var parent = NewDocument(50, "orphan-parent", null, [1]);
        parent.MarkAdded();
        var child = NewNote(500, 50, "orphan-child");
        child.MarkAdded();
        parent.DocumentNotes.Add(child);

        // 親がスキップされ、子の INSERT が存在しない親を参照して FK 違反 → 例外
        var act = () => documents.SaveAsync(parent, cancellationToken: Ct);
        await act.Should().ThrowAsync<SqliteException>();

        // 全体ロールバックのため、親も子も DB に残らない
        (await DocumentExistsAsync(50))
            .Should()
            .BeFalse();
        (await ScalarAsync("SELECT COUNT(*) FROM document_notes WHERE note_id = 500"))
            .Should()
            .Be(0);
    }

    // ── 3. After の WriteBinaryColumnAsync ＋ファイル糖衣 → Save 後に blob が読める ──

    /// <summary>3. After が context.WriteBinaryColumnAsync で除外列 blob を書くと、コミット後に読める（同一トランザクション）</summary>
    [Fact(
        DisplayName = "[SaveHook/Ado] 3: After の WriteBinaryColumnAsync＋ファイル糖衣が Save 後に読める"
    )]
    public async Task After_WritesBinaryColumn_VisibleAfterCommit()
    {
        await ResetAndSeedAsync();

        var newPayload = new byte[64 * 1024];
        new Random(7).NextBytes(newPayload);

        var directory = Directory.CreateTempSubdirectory("quicker-savehook");

        try
        {
            var thumbPath = Path.Combine(directory.FullName, "thumb.bin");
            var thumbBytes = new byte[2048];
            new Random(8).NextBytes(thumbBytes);
            await File.WriteAllBytesAsync(thumbPath, thumbBytes, Ct);

            var hook = new RecordingHook<DocumentEntity>("h", [], e => e.DocumentId)
            {
                AfterAction = async (entity, _, context) =>
                {
                    // Stream 版で payload を、ファイル糖衣で thumb を、同一トランザクション内に書き込む
                    await context.WriteBinaryColumnAsync(
                        nameof(DocumentEntity.Payload),
                        entity.DocumentId,
                        new MemoryStream(newPayload),
                        cancellationToken: Ct
                    );
                    await context.WriteBinaryColumnFromFileAsync(
                        nameof(DocumentEntity.Thumb),
                        entity.DocumentId,
                        thumbPath,
                        Ct
                    );
                },
            };
            var documents = Documents(hook);

            // 文書 1 を更新（除外列は未取得状態のまま）→ After が blob を書く
            var doc = await documents.GetByIdAsync(1, Ct);
            doc!.Title = "alpha-hooked";
            doc.MarkUpdated();
            (await documents.SaveAsync(doc, cancellationToken: Ct)).Should().BeGreaterThan(0);

            // コミット後、After が書いた blob が読める
            var readBack = await Documents()
                .Query()
                .Where(d => d.DocumentId == 1)
                .WithUnboundedBinary()
                .FirstOrDefaultAsync(Ct);
            readBack!.Payload.Should().Equal(newPayload, "After が書いた payload が読める");
            readBack.Thumb.Should().Equal(thumbBytes, "ファイル糖衣で書いた thumb が読める");
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    // ── 4. After 例外 → 行・blob・監査行が残らない（原子性） ──

    /// <summary>4. After が（生 SQL の監査挿入・blob 書き込みの後で）例外を投げると、Save 全体がロールバックし何も残らない</summary>
    [Fact(DisplayName = "[SaveHook/Ado] 4: After 例外で行・blob・監査行がすべてロールバックされる")]
    public async Task After_Throws_RollsBackRowBlobAndAudit()
    {
        await ResetAndSeedAsync();

        var hook = new RecordingHook<DocumentEntity>("h", [], e => e.DocumentId)
        {
            AfterAction = async (entity, _, context) =>
            {
                // 同一トランザクションで監査行を挿入し blob を書いた後に例外を投げる
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
        var documents = Documents(hook);

        var doc = await documents.GetByIdAsync(1, Ct);
        doc!.Title = "alpha-doomed";
        doc.MarkUpdated();

        var act = () => documents.SaveAsync(doc, cancellationToken: Ct);
        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage("*after-boom*");

        // 行の変更・監査行・blob 変更のいずれも残らない（原子性）
        (await ScalarAsync("SELECT COUNT(*) FROM documents WHERE title = 'alpha-doomed'"))
            .Should()
            .Be(0, "タイトル更新はロールバックされた");
        (await ScalarAsync("SELECT COUNT(*) FROM audit"))
            .Should()
            .Be(0, "監査行はロールバックされた");
        (await ScalarAsync("SELECT length(payload) FROM documents WHERE document_id = 1"))
            .Should()
            .Be(Doc1Payload.Length, "payload の blob は元のまま（書き込みはロールバック）");
    }

    public void Dispose()
    {
        foreach (var provider in _providers)
        {
            provider.Dispose();
        }

        _db.Dispose();
    }
}
