using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuickER.Tests.GeneratedBinaryFixture;
using QuickER.Tests.Integration;
using QuickER.Tests.TestSupport;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// Save フック（<see cref="ISaveHook{TEntity}"/>）の複数フックの順序と短絡（基底 <see cref="SaveHookRuntimeTestsBase"/>）を、
/// <b>EF Core 版リポジトリ</b>（<c>AddGeneratedEfCoreRepositories</c>・UseSqlite）で実 SQLite（一時ファイル DB・Docker 不要＝
/// CI 常時実行）に流す。
/// </summary>
/// <remarks>
/// EF Core 固有だった検証（FK 違反での全体ロールバック・After の生 SQL が After 例外で巻き戻る・context の除外列書き込みが
/// <see cref="NotSupportedException"/>）は、実装先の一致のシナリオ（<c>hook-skip-parent-orphans-child</c>・
/// <c>hook-after-sql-then-later-after-throws</c>・<c>hook-after-write-blob-not-supported-on-ef-core</c>）が持つ。
/// </remarks>
public sealed class SaveHookEfCoreRuntimeTests : SaveHookRuntimeTestsBase, IDisposable
{
    private readonly SqliteTempDatabase _db = SqliteTempDatabase.Create();
    private ServiceProvider? _hooklessProvider;
    private readonly List<ServiceProvider> _providers = [];

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
            options.UseSqlite(_db.ReadWriteCreateConnectionString).IgnoreServiceProviderCacheLimit()
        );

        foreach (var hook in hooks)
        {
            services.AddSaveHook(hook);
        }

        var provider = services.BuildServiceProvider();
        _providers.Add(provider);
        return provider;
    }

    protected override IDocumentRepository Documents(params object[] hooks) =>
        Provider(hooks).GetRequiredService<IDocumentRepository>();

    protected override IDocumentNoteRepository Notes(params object[] hooks) =>
        Provider(hooks).GetRequiredService<IDocumentNoteRepository>();

    /// <summary>スキーマを作成し、共通シードを EF Core リポジトリ経由で投入する</summary>
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
