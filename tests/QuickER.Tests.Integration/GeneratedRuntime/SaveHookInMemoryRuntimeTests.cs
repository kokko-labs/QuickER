using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using QuickER.Tests.GeneratedBinaryFixture;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// Save フック（<see cref="ISaveHook{TEntity}"/>）の複数フックの順序と短絡（基底 <see cref="SaveHookRuntimeTestsBase"/>）を、
/// <b>インメモリ Repository</b>（<see cref="InMemoryDataStore"/> 共有）で流す。実 DB を使わないため Docker 不要＝CI 常時実行。
/// </summary>
/// <remarks>
/// インメモリ固有だった検証（skip 時の Put 抑止・After の除外列書き込みがストアへ反映・After 例外時は blob 書き込みも巻き戻る・
/// context の生 SQL は NotSupported）は、実装先の一致のシナリオ（<c>hook-skip-update-keeps-stored-row</c>・
/// <c>hook-after-writes-blobs</c>・<c>hook-after-writes-blob-then-later-after-throws</c>・<c>hook-after-executes-sql</c>）が持つ。
/// </remarks>
public sealed class SaveHookInMemoryRuntimeTests : SaveHookRuntimeTestsBase, IDisposable
{
    /// <summary>全リポジトリで共有するインメモリストア（実 DB のファイルに相当する永続点）</summary>
    private readonly InMemoryDataStore _store = new();
    private readonly List<ServiceProvider> _providers = [];

    /// <summary>指定フック群から Save フックのレジストリを構築する（フックなしは null＝完全 no-op）</summary>
    private ISaveHookRegistry? BuildRegistry(object[] hooks)
    {
        if (hooks.Length == 0)
        {
            return null;
        }

        var services = new ServiceCollection();

        foreach (var hook in hooks)
        {
            services.AddSaveHook(hook);
        }

        var provider = services.BuildServiceProvider();
        _providers.Add(provider);
        return new ServiceProviderSaveHookRegistry(provider);
    }

    protected override IDocumentRepository Documents(params object[] hooks) =>
        new InMemoryDocumentRepository(_store, BuildRegistry(hooks));

    protected override IDocumentNoteRepository Notes(params object[] hooks) =>
        new InMemoryDocumentNoteRepository(_store, BuildRegistry(hooks));

    /// <summary>ストアを空にして共通シードを投入する（フックなしのリポジトリ経由）</summary>
    protected override async Task ResetAndSeedAsync()
    {
        _store.Clear();

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
    }
}
