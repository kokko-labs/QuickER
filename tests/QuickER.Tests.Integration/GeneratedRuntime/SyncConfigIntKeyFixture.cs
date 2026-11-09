using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using QuickER.Model;
using QuickER.Tests.GeneratedSyncFixture;
using QuickER.Tests.GeneratedSyncFixture.Repositories.Sqlite;
using QuickER.Tests.GeneratedSyncFixture.Repositories.SqlServer;
using LocalSqlite = QuickER.Tests.GeneratedSyncFixture.Repositories.Sqlite;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>同期の構成の行列の、整数のキーのフィクスチャ（SyncFixture・リモートサービスあり）</summary>
internal sealed class SyncConfigIntKeyFixture : ISyncConfigFixture
{
    public string AxisValue => SyncConfigDimensions.IntKey;

    public bool HasRemoteServices => true;

    public string RemotePrefix => RemotePaths.DefaultPrefix;

    public ErDiagram ServerDiagram => SyncFixtureDefinition.Build();

    public ErDiagram LocalDiagram => SyncFixtureDefinition.BuildSqliteMirror();

    public void AddLocalRepositories(
        IServiceCollection services,
        object? key,
        string connectionString,
        bool asInstances = false
    )
    {
        if (key is null)
        {
            services.AddGeneratedSqliteRepositories(connectionString);
        }
        else
        {
            services.AddGeneratedSqliteRepositories(key, connectionString);
        }

        if (asInstances)
        {
            var factory = new LocalSqlite.SqlConnectionFactory(connectionString);
            SyncConfigFixtures.ReplaceWithInstance<ISyncOrderRepository>(
                services,
                key,
                new LocalSqlite.SyncOrderRepository(factory)
            );
            SyncConfigFixtures.ReplaceWithInstance<ISyncOrderLineRepository>(
                services,
                key,
                new LocalSqlite.SyncOrderLineRepository(factory)
            );
            SyncConfigFixtures.ReplaceWithInstance<ISyncNoteRepository>(
                services,
                key,
                new LocalSqlite.SyncNoteRepository(factory)
            );
            SyncConfigFixtures.ReplaceWithInstance<ISyncMarkerRepository>(
                services,
                key,
                new LocalSqlite.SyncMarkerRepository(factory)
            );
        }
    }

    public void AddServerRepositories(
        IServiceCollection services,
        object? key,
        string connectionString
    )
    {
        if (key is null)
        {
            services.AddGeneratedSqlServerRepositories(connectionString);
        }
        else
        {
            services.AddGeneratedSqlServerRepositories(key, connectionString);
        }
    }

    public void AddKeyAssigningHook(IServiceCollection services, int key) =>
        services.AddSaveHook(new AssignKeyHook(key));

    public string ParentTableName => "sync_orders";

    public string ChildTableName => "sync_order_lines";

    public bool HasVersionlessChild => true;

    public void AddSyncSupport(
        IServiceCollection services,
        object? serverKey,
        object? localKey,
        bool excludeParent
    ) => services.AddGeneratedSyncSupport(serverKey, localKey, Exclusions(excludeParent));

    public void AddDirectSources(IServiceCollection services, object? serverKey) =>
        services.AddGeneratedDirectSyncSources(serverKey);

    public void AddHttpSources(IServiceCollection services, string baseAddress) =>
        services.AddGeneratedHttpSyncSources(baseAddress);

    public void AddHttpSources(
        IServiceCollection services,
        Func<IServiceProvider, HttpClient> factory
    ) => services.AddGeneratedHttpSyncSources(factory);

    public void AddSyncEngine(IServiceCollection services, object? localKey, bool excludeParent) =>
        services.AddGeneratedSyncEngine(localKey, Exclusions(excludeParent));

    public Task<InProcessRemoteServer> StartRemoteServerAsync(
        string connectionString,
        CancellationToken ct
    ) =>
        InProcessRemoteServer.StartAsync(
            services =>
            {
                services.AddGeneratedSqlServerRepositories(connectionString);
                services.AddGeneratedDirectSyncSources(null);
            },
            app => app.MapGeneratedRemoteEndpoints(RemoteAccess.AllowAnonymous),
            ct
        );

    public async Task WriteLocalAsync(
        IServiceProvider provider,
        object? localKey,
        int key,
        bool keyFromHook,
        bool graph,
        CancellationToken ct
    )
    {
        var order = new SyncOrderEntity
        {
            OrderId = keyFromHook ? 0 : key,
            CustomerName = $"local{key}",
        };
        var repository = Repository(provider, localKey);

        if (graph)
        {
            order.MarkAdded();
            await repository.SaveAsync(order, cancellationToken: ct);
        }
        else
        {
            await repository.InsertAsync(order, ct);
        }
    }

    public Task InsertAsync(IServiceProvider provider, object? key, int id, CancellationToken ct) =>
        Repository(provider, key)
            .InsertAsync(new SyncOrderEntity { OrderId = id, CustomerName = $"server{id}" }, ct);

    public async Task<bool> ExistsAsync(
        IServiceProvider provider,
        object? key,
        int id,
        CancellationToken ct
    ) => await Repository(provider, key).GetByIdAsync(id, ct) is not null;

    public async Task UpdateAsync(
        IServiceProvider provider,
        object? key,
        int id,
        CancellationToken ct
    )
    {
        var repository = Repository(provider, key);
        var order =
            await repository.GetByIdAsync(id, ct)
            ?? throw new InvalidOperationException($"order {id} is not there");
        order.CustomerName = $"updated{id}";
        await repository.UpdateAsync(order, cancellationToken: ct);
    }

    public Task InsertChildAsync(
        IServiceProvider provider,
        object? key,
        int parentId,
        int childId,
        CancellationToken ct
    ) =>
        Resolve<ISyncOrderLineRepository>(provider, key)
            .InsertAsync(
                new SyncOrderLineEntity
                {
                    LineId = childId,
                    OrderId = parentId,
                    Product = $"line{childId}",
                },
                ct
            );

    public async Task<bool> ChildExistsAsync(
        IServiceProvider provider,
        object? key,
        int childId,
        CancellationToken ct
    ) =>
        await Resolve<ISyncOrderLineRepository>(provider, key).GetByIdAsync(childId, ct)
            is not null;

    public async Task<int> ChildCountAsync(
        IServiceProvider provider,
        object? key,
        int parentId,
        CancellationToken ct
    ) =>
        (await Resolve<ISyncOrderLineRepository>(provider, key).GetAllAsync(ct)).Count(line =>
            line.OrderId == parentId
        );

    public async Task SaveNewChildAsync(
        IServiceProvider provider,
        object? localKey,
        int parentId,
        int childId,
        CancellationToken ct
    )
    {
        var repository = Repository(provider, localKey);
        var order = await LoadAsync(repository, parentId, ct);
        order.CustomerName = $"graph{parentId}";
        order.MarkUpdated();
        order.SyncOrderLines.Add(NewLine(parentId, childId));
        await repository.SaveAsync(order, cancellationToken: ct);
    }

    public async Task SaveNewChildRemovedAsync(
        IServiceProvider provider,
        object? localKey,
        int parentId,
        CancellationToken ct
    )
    {
        var repository = Repository(provider, localKey);
        var order = await LoadAsync(repository, parentId, ct);

        // int のキーは「無い」を表せないので 0 のまま（保存側の DELETE はどの行にも当たらない）
        var line = NewLine(parentId, 0);
        line.MarkRemoved();
        order.SyncOrderLines.Add(line);
        await repository.SaveAsync(order, cancellationToken: ct);
    }

    public async Task SaveParentRemovedWithNewChildAsync(
        IServiceProvider provider,
        object? localKey,
        int parentId,
        int? versionlessChildId,
        bool asList,
        CancellationToken ct
    )
    {
        var repository = Repository(provider, localKey);
        var order = await LoadAsync(repository, parentId, ct);
        order.SyncOrderLines.Add(NewLine(parentId, 0));

        if (versionlessChildId is { } noteId)
        {
            var note = new SyncNoteEntity
            {
                NoteId = noteId,
                OrderId = parentId,
                Body = $"note{noteId}",
            };
            note.MarkAdded();
            order.SyncNotes.Add(note);
        }

        order.MarkRemoved();

        if (asList)
        {
            await repository.SaveAsync([order], cancellationToken: ct);
        }
        else
        {
            await repository.SaveAsync(order, cancellationToken: ct);
        }
    }

    public Task InsertVersionlessChildAsync(
        IServiceProvider provider,
        object? key,
        int parentId,
        int childId,
        CancellationToken ct
    ) =>
        Resolve<ISyncNoteRepository>(provider, key)
            .InsertAsync(
                new SyncNoteEntity
                {
                    NoteId = childId,
                    OrderId = parentId,
                    Body = $"server-note{childId}",
                },
                ct
            );

    public async Task<bool> VersionlessChildExistsAsync(
        IServiceProvider provider,
        object? key,
        int childId,
        CancellationToken ct
    ) => await Resolve<ISyncNoteRepository>(provider, key).GetByIdAsync(childId, ct) is not null;

    public async Task DeleteServerRowsAsync(
        IServiceProvider serverProvider,
        IReadOnlyList<int> childIds,
        IReadOnlyList<int> versionlessChildIds,
        IReadOnlyList<int> parentIds,
        CancellationToken ct
    )
    {
        foreach (var id in childIds)
        {
            await Resolve<ISyncOrderLineRepository>(serverProvider, null).DeleteAsync(id, ct);
        }

        foreach (var id in versionlessChildIds)
        {
            await Resolve<ISyncNoteRepository>(serverProvider, null).DeleteAsync(id, ct);
        }

        foreach (var id in parentIds)
        {
            await Repository(serverProvider, null).DeleteAsync(id, ct);
        }
    }

    public async Task<SyncConfigJournal> JournalAsync(
        IServiceProvider provider,
        object? localKey,
        CancellationToken ct
    )
    {
        await using var scope = provider.CreateAsyncScope();
        var entries = await scope
            .ServiceProvider.GetRequiredService<SyncJournal>()
            .ReadAllAsync(ct);
        var all = await Resolve<ISqlExecutor>(scope.ServiceProvider, localKey)
            .ExecuteScalarSqlAsync<long>("SELECT COUNT(*) FROM quicker_sync_journal", null, ct);

        return new SyncConfigJournal(
            [.. entries.Select(entry => entry.TableName)],
            (int)all - entries.Count
        );
    }

    public async Task<(string? Problem, int Downloaded)> SyncAsync(
        IServiceProvider provider,
        CancellationToken ct,
        bool lastWriteWins = false
    )
    {
        var result = await provider
            .GetRequiredService<SyncEngine>()
            .SyncAsync(
                lastWriteWins ? new SyncOptions { Mode = SyncMode.LastWriteWins } : null,
                ct
            );

        return (
            result.Conflicts.Count == 0 && result.Truncations.Count == 0
                ? null
                : $"conflicts=[{string.Join(", ", result.Conflicts.Select(c => $"{c.TableName}:{c.KeyText}:{c.Reason}"))}] "
                    + $"truncations=[{string.Join(", ", result.Truncations.Select(t => t.TableName))}]",
            result.Downloaded
        );
    }

    public Task RefreshAsync(IServiceProvider provider, CancellationToken ct) =>
        provider
            .GetRequiredService<SyncEngine>()
            .RefreshAsync(new SyncRefreshOptions { DiscardLocalUnboundedBinaries = true }, ct);

    public IReadOnlyList<string> RegistrationProblems(
        IServiceProvider provider,
        object? localKey,
        object? serverKey,
        bool overHttp,
        bool parentExcluded
    ) =>
        SyncConfigFixtures.RegistrationProblems(
            Repository(provider, localKey),
            Resolve<ISyncOrderLineRepository>(provider, localKey),
            provider.GetRequiredService<ISyncServerSource<SyncOrderEntity, int>>(),
            overHttp || Equals(serverKey, localKey) ? null : Repository(provider, serverKey),
            overHttp,
            parentExcluded
        );

    private static ISyncOrderRepository Repository(IServiceProvider provider, object? key) =>
        Resolve<ISyncOrderRepository>(provider, key);

    private static TService Resolve<TService>(IServiceProvider provider, object? key)
        where TService : notnull =>
        key is null
            ? provider.GetRequiredService<TService>()
            : provider.GetRequiredKeyedService<TService>(key);

    /// <summary>親の表（注文）を除外するときの excludeFromSync（除外しないなら null）</summary>
    private static Type[]? Exclusions(bool excludeParent) =>
        excludeParent ? [typeof(SyncOrderEntity)] : null;

    /// <summary>保存済みの注文を読む（明細は読まない）</summary>
    private static async Task<SyncOrderEntity> LoadAsync(
        ISyncOrderRepository repository,
        int id,
        CancellationToken ct
    ) =>
        await repository.GetByIdAsync(id, ct)
        ?? throw new InvalidOperationException($"order {id} is not there");

    /// <summary>新しい明細（Added）</summary>
    private static SyncOrderLineEntity NewLine(int parentId, int lineId)
    {
        var line = new SyncOrderLineEntity
        {
            LineId = lineId,
            OrderId = parentId,
            Product = $"new-line{lineId}",
        };
        line.MarkAdded();

        return line;
    }

    /// <summary>キーの無い（0 の）新しい注文に、Before でキーを決める</summary>
    private sealed class AssignKeyHook(int key) : ISaveHook<SyncOrderEntity>
    {
        public Task<bool> BeforeSaveAsync(
            SyncOrderEntity entity,
            SaveOperation operation,
            CancellationToken cancellationToken = default
        )
        {
            if (operation == SaveOperation.Insert && entity.OrderId == 0)
            {
                entity.OrderId = key;
            }

            return Task.FromResult(true);
        }
    }
}
