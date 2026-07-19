using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuickER.Sqlite;
using QuickER.Tests.GeneratedBinaryFixture;
using QuickER.Tests.Integration;
using Xunit;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// Save フック（<see cref="ISaveHook{TEntity}"/>）を<b>EF Core 版リポジトリ</b>（<c>AddGeneratedEfCoreRepositories</c>・
/// UseSqlite）で実 SQLite（一時ファイル DB・Docker 不要＝CI 常時実行）に流して検証する。
/// </summary>
/// <remarks>
/// バックエンド非依存のシナリオは基底 <see cref="SaveHookRuntimeTestsBase"/> が持ち、本クラスは EF Core 固有の検証を行う:
/// TrackGraph 記録に基づく Before/After 発火・明示トランザクション内での After（生 SQL の監査行が After 例外でロールバック）・
/// context の除外列書き込みが <see cref="NotSupportedException"/>・FK 違反での全体ロールバック。
/// insertWhenUpdateMissing の再試行×After=Insert（DbUpdateConcurrencyException→Added 切替）は基底シナリオ 6 が EF Core 経路で担保し、
/// フック未登録時に明示トランザクションを張らない（意味的同一）ことは既存 EF Core テスト群と基底シナリオ 9 の全緑で担保する。
/// EF Core の SQLite プロバイダは既定で <c>PRAGMA foreign_keys=ON</c> を送るため FK が有効になる。
/// </remarks>
public sealed class SaveHookEfCoreRuntimeTests : SaveHookRuntimeTestsBase, IDisposable
{
    private readonly SqliteTempDatabase _db = SqliteTempDatabase.Create();
    private ServiceProvider? _hooklessProvider;
    private readonly List<ServiceProvider> _providers = [];

    /// <summary>EF Core は SaveChanges＋After を 1 つの明示トランザクションで囲うため After 例外で保存変更は残らない</summary>
    protected override bool AfterExceptionLeavesResidue => false;

    /// <summary>EF Core 版リポジトリ群とフックを登録した DI プロバイダを構築する（フックなしは状態確認用に使い回す）</summary>
    private ServiceProvider Provider(object[] hooks)
    {
        // フックなし（状態確認・シード）は使い回す。フック指定があるたびに専用プロバイダを構築する
        if (hooks.Length == 0)
        {
            return _hooklessProvider ??= BuildProvider(hooks);
        }

        return BuildProvider(hooks);
    }

    private ServiceProvider BuildProvider(object[] hooks)
    {
        var services = new ServiceCollection().AddGeneratedEfCoreRepositories(options =>
            options.UseSqlite(_db.ReadWriteCreateConnectionString)
        );
        SaveHookAdoRuntimeTests.RegisterHooks(services, hooks);

        var provider = services.BuildServiceProvider();
        _providers.Add(provider);
        return provider;
    }

    protected override IDocumentRepository Documents(params object[] hooks) =>
        Provider(hooks).GetRequiredService<IDocumentRepository>();

    protected override IDocumentNoteRepository Notes(params object[] hooks) =>
        Provider(hooks).GetRequiredService<IDocumentNoteRepository>();

    /// <summary>スキーマ（＋監査テーブル）を作成し、共通シードを EF Core リポジトリ経由で投入する</summary>
    protected override async Task ResetAndSeedAsync()
    {
        await using (var conn = new SqliteConnection(_db.ReadWriteCreateConnectionString))
        {
            await conn.OpenAsync(Ct);

            await using var drop = conn.CreateCommand();
            drop.CommandText =
                "DROP TABLE IF EXISTS \"document_notes\"; DROP TABLE IF EXISTS \"documents\"; DROP TABLE IF EXISTS \"audit\";";
            await drop.ExecuteNonQueryAsync(Ct);
        }

        var ddl = new SqliteDdlGenerator().Build(BinaryFixtureDefinition.Build());
        await _db.ApplyDdlAsync(ddl, Ct);

        await using (var conn = new SqliteConnection(_db.ReadWriteCreateConnectionString))
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

    // ── EF Core 固有 1: FK 違反での全体ロールバック ──

    /// <summary>新規親をスキップしつつ新規子を保存すると FK 違反 → 例外 → 全体ロールバック（EF Core は明示トランザクション）</summary>
    [Fact(DisplayName = "[SaveHook/EF Core] 親スキップ×子保存は FK 違反で全体ロールバックする")]
    public async Task Parent_Skipped_ChildSaved_ForeignKeyRollsBackAll()
    {
        await ResetAndSeedAsync();

        var hook = new RecordingHook<DocumentEntity>("h", [], _ => 0)
        {
            BeforePredicate = (_, op) => op != SaveOperation.Insert,
        };
        var documents = Documents(hook);

        var parent = NewDocument(50, "orphan-parent", null, [1]);
        parent.MarkAdded();
        var child = NewNote(500, 50, "orphan-child");
        child.MarkAdded();
        parent.DocumentNotes.Add(child);

        var act = () => documents.SaveAsync(parent, cancellationToken: Ct);
        await act.Should().ThrowAsync<DbUpdateException>();

        (await DocumentExistsAsync(50)).Should().BeFalse();
        (await NoteCountAsync(50)).Should().Be(0);
    }

    // ── EF Core 固有 2: After の生 SQL が同一トランザクションに参加し、例外でロールバックされる ──

    /// <summary>After の context.ExecuteSqlAsync（監査行）は保存中トランザクションに参加し、After 例外で行更新とともにロールバックされる</summary>
    [Fact(
        DisplayName = "[SaveHook/EF Core] After の生 SQL 監査行が After 例外で行更新とともにロールバックされる"
    )]
    public async Task After_ExecuteSql_RollsBackWithRowOnException()
    {
        await ResetAndSeedAsync();

        var hook = new RecordingHook<DocumentEntity>("h", [], e => e.DocumentId)
        {
            AfterAction = async (_, _, context) =>
            {
                await context.ExecuteSqlAsync(
                    "INSERT INTO audit (note) VALUES (@note)",
                    new { note = "should-not-persist" },
                    Ct
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

        // 行更新・監査行のいずれも残らない（After は明示トランザクション内で走り、例外で一括ロールバック）
        (await Documents().GetByIdAsync(1, Ct))!
            .Title.Should()
            .Be("alpha", "行更新はロールバックされた");
        (await Documents().ExecuteScalarSqlAsync<long>("SELECT COUNT(*) FROM audit", null, Ct))
            .Should()
            .Be(0, "監査行はロールバックされた");
    }

    // ── EF Core 固有 3: context の除外列書き込みは NotSupported ──

    /// <summary>EF Core モードでは After の context.WriteBinaryColumnAsync は NotSupportedException を投げる</summary>
    [Fact(
        DisplayName = "[SaveHook/EF Core] After の WriteBinaryColumnAsync は NotSupportedException"
    )]
    public async Task After_WriteBinaryColumn_ThrowsNotSupported()
    {
        await ResetAndSeedAsync();

        var hook = new RecordingHook<DocumentEntity>("h", [], e => e.DocumentId)
        {
            AfterAction = async (entity, _, context) =>
                await context.WriteBinaryColumnAsync(
                    nameof(DocumentEntity.Payload),
                    entity.DocumentId,
                    new MemoryStream([1, 2, 3]),
                    cancellationToken: Ct
                ),
        };
        var documents = Documents(hook);

        var doc = await documents.GetByIdAsync(1, Ct);
        doc!.Title = "alpha-ef";
        doc.MarkUpdated();

        var act = () => documents.SaveAsync(doc, cancellationToken: Ct);
        (await act.Should().ThrowAsync<NotSupportedException>())
            .Which.Message.Should()
            .Contain("EF Core");
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
