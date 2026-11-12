using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using QuickER.Tests.GeneratedBinaryFixture;
using QuickER.Tests.Integration;
using QuickER.Tests.TestSupport;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// Save フック（<see cref="ISaveHook{TEntity}"/>）の複数フックの順序と短絡（基底 <see cref="SaveHookRuntimeTestsBase"/>）を、
/// <b>QuickER 版 Repository の SQLite 実装</b>（<c>AddGeneratedSqliteRepositories</c>）で実 SQLite（一時ファイル DB・Docker 不要＝
/// CI 常時実行）に流す。
/// </summary>
/// <remarks>
/// QuickER 版 Repository 固有だった検証（FK 違反での全体ロールバック・After の同一トランザクション書き込み＝除外列 blob と生 SQL の
/// アトミック性）は、実装先の一致のシナリオ（<c>hook-skip-parent-orphans-child</c>・<c>hook-after-writes-blobs</c>・
/// <c>hook-after-writes-blob-then-later-after-throws</c>・<c>hook-after-sql-then-later-after-throws</c>）が持つ。
/// FK 制約を Repository の接続でも効かせるため、接続文字列で <c>Foreign Keys=True</c> を明示する。
/// </remarks>
[RuntimeCell(RuntimeFeatures.SaveHook, RuntimeTarget.AdoSqlite)]
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

    /// <summary>指定した Save フック群を登録した DI プロバイダを構築する（テスト終了時にまとめて破棄）</summary>
    private ServiceProvider BuildProvider(params object[] hooks)
    {
        var services = new ServiceCollection().AddGeneratedSqliteRepositories(ConnectionString);

        foreach (var hook in hooks)
        {
            services.AddSaveHook(hook);
        }

        var provider = services.BuildServiceProvider();
        _providers.Add(provider);
        return provider;
    }

    protected override IDocumentRepository Documents(params object[] hooks) =>
        BuildProvider(hooks).GetRequiredService<IDocumentRepository>();

    protected override IDocumentNoteRepository Notes(params object[] hooks) =>
        BuildProvider(hooks).GetRequiredService<IDocumentNoteRepository>();

    /// <summary>スキーマを作成し、共通シードを投入する（フックなしのプロバイダ経由）</summary>
    protected override async Task ResetAndSeedAsync()
    {
        await _db.ResetSchemaAsync(Ct);
        await _db.ApplyDdlAsync(BinaryFixtureDefinition.Build(), Ct);

        var documents = Documents();
        var notes = Notes();

        await documents.InsertAsync(NewDocument(1, "alpha", Doc1Payload, [9, 9]), Ct);
        await documents.InsertAsync(NewDocument(2, "beta", null, [8]), Ct);
        await documents.InsertAsync(NewDocument(3, "gamma", [5, 6], [6]), Ct);
        await notes.InsertAsync(NewNote(100, 1, "note-a"), Ct);
        await notes.InsertAsync(NewNote(101, 1, "note-b"), Ct);
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
