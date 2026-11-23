using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using QuickER.Model;
using QuickER.Tests.GeneratedSyncVoFixture;
using QuickER.Tests.GeneratedSyncVoFixture.Repositories.Sqlite;
using QuickER.Tests.GeneratedSyncVoFixture.Repositories.SqlServer;
using LocalSqlite = QuickER.Tests.GeneratedSyncVoFixture.Repositories.Sqlite;
using ServerSqlServer = QuickER.Tests.GeneratedSyncVoFixture.Repositories.SqlServer;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>同期の構成の行列の、値オブジェクトのキーのフィクスチャ（SyncVoFixture・リモートサービスあり）</summary>
internal sealed class SyncConfigVoKeyFixture : ISyncConfigFixture
{
    public string AxisValue => SyncConfigDimensions.VoKey;

    public bool HasRemoteServices => true;

    public string RemotePrefix => RemotePaths.DefaultPrefix;

    public ErDiagram ServerDiagram => SyncVoFixtureDefinition.Build();

    public ErDiagram LocalDiagram => SyncVoFixtureDefinition.BuildSqliteMirror();

    public void AddLocalRepositories(
        IServiceCollection services,
        object? key,
        string connectionString,
        bool factoryAsInstance = false
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

        if (factoryAsInstance)
        {
            // 生成の登録の後に足したインスタンスの登録が最後の登録になり、同期の登録はこれを内側にして包む
            services.AddSingleton<LocalSqlite.ISqlConnectionFactory>(
                new LocalSqlite.SqlConnectionFactory(connectionString)
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

    public string ParentTableName => "syncvo_orders";

    public string ChildTableName => "syncvo_lines";

    public bool HasVersionlessChild => false;

    public IReadOnlyList<string> SyncTableNames { get; } = ["syncvo_orders", "syncvo_lines"];

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

    public IReadOnlyList<string> SyncOperationRoutes { get; } =
    [RemoteSyncOperations.Ceiling, RemoteSyncOperations.Changes, RemoteSyncOperations.Keys];

    public string UnboundedBinaryRoute => "SyncvoOrder/Photo";

    public Task<InProcessRemoteServer> StartRemoteServerAsync(
        string connectionString,
        CancellationToken ct,
        Action<WebApplicationBuilder>? configure = null,
        SyncConfigRequestLog? requests = null
    ) =>
        InProcessRemoteServer.StartAsync(
            services =>
            {
                services.AddGeneratedSqlServerRepositories(connectionString);
                services.AddGeneratedDirectSyncSources(null);
            },
            app =>
            {
                requests?.Attach(app);
                app.MapGeneratedRemoteEndpoints(RemoteAccess.AllowAnonymous);
            },
            ct,
            configure
        );

    public async Task WriteUnboundedBinaryAsync(
        IServiceProvider provider,
        object? key,
        int id,
        byte[] content,
        CancellationToken ct
    )
    {
        if (
            !await Repository(provider, key)
                .WritePhotoAsync(
                    OrderIdValue.Create(id),
                    new MemoryStream(content),
                    cancellationToken: ct
                )
        )
        {
            throw new InvalidOperationException($"order {id} is not there");
        }
    }

    public async Task<byte[]?> ReadUnboundedBinaryAsync(
        IServiceProvider provider,
        object? key,
        int id,
        CancellationToken ct
    )
    {
        using var destination = new MemoryStream();

        return await Repository(provider, key)
            .ReadPhotoAsync(OrderIdValue.Create(id), destination, ct)
            ? destination.ToArray()
            : null;
    }

    public async Task WriteLocalAsync(
        IServiceProvider provider,
        object? localKey,
        int key,
        bool keyFromHook,
        bool graph,
        CancellationToken ct
    )
    {
        var order = new SyncvoOrderEntity
        {
            OrderId = keyFromHook ? null! : OrderIdValue.Create(key),
            Title = TitleValue.Create($"local{key}"),
            Photo = PhotoValue.Create([1, 2, 3]),
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
            .InsertAsync(
                new SyncvoOrderEntity
                {
                    OrderId = OrderIdValue.Create(id),
                    Title = TitleValue.Create($"server{id}"),
                    Photo = PhotoValue.Create([4, 5, 6]),
                },
                ct
            );

    public async Task<bool> ExistsAsync(
        IServiceProvider provider,
        object? key,
        int id,
        CancellationToken ct
    ) => await Repository(provider, key).GetByIdAsync(OrderIdValue.Create(id), ct) is not null;

    public async Task UpdateAsync(
        IServiceProvider provider,
        object? key,
        int id,
        CancellationToken ct
    )
    {
        var repository = Repository(provider, key);
        var order = await LoadAsync(repository, id, ct);
        order.Title = TitleValue.Create($"updated{id}");
        await repository.UpdateAsync(order, cancellationToken: ct);
    }

    public Task InsertChildAsync(
        IServiceProvider provider,
        object? key,
        int parentId,
        int childId,
        CancellationToken ct
    ) =>
        Resolve<ISyncvoLineRepository>(provider, key)
            .InsertAsync(
                new SyncvoLineEntity
                {
                    LineId = LineIdValue.Create(childId),
                    OrderId = OrderIdValue.Create(parentId),
                    Qty = QtyValue.Create(childId % 100),
                },
                ct
            );

    public async Task<bool> ChildExistsAsync(
        IServiceProvider provider,
        object? key,
        int childId,
        CancellationToken ct
    ) =>
        await Resolve<ISyncvoLineRepository>(provider, key)
            .GetByIdAsync(LineIdValue.Create(childId), ct)
            is not null;

    public async Task<int> ChildCountAsync(
        IServiceProvider provider,
        object? key,
        int parentId,
        CancellationToken ct
    )
    {
        var parent = OrderIdValue.Create(parentId);

        return (await Resolve<ISyncvoLineRepository>(provider, key).GetAllAsync(ct)).Count(line =>
            Equals(line.OrderId, parent)
        );
    }

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
        order.Title = TitleValue.Create($"graph{parentId}");
        order.MarkUpdated();
        order.SyncvoLines.Add(NewLine(order, LineIdValue.Create(childId)));
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

        // キーのプロパティを設定しない（null）新しい明細
        var line = NewLine(order, null!);
        line.MarkRemoved();
        order.SyncvoLines.Add(line);
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
        if (versionlessChildId is not null)
        {
            throw new NotSupportedException("SyncVoFixture has no versionless child table");
        }

        var repository = Repository(provider, localKey);
        var order = await LoadAsync(repository, parentId, ct);
        order.SyncvoLines.Add(NewLine(order, null!));
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
    ) => throw new NotSupportedException("SyncVoFixture has no versionless child table");

    public Task<bool> VersionlessChildExistsAsync(
        IServiceProvider provider,
        object? key,
        int childId,
        CancellationToken ct
    ) => throw new NotSupportedException("SyncVoFixture has no versionless child table");

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
            await Resolve<ISyncvoLineRepository>(serverProvider, null)
                .DeleteAsync(LineIdValue.Create(id), ct);
        }

        foreach (var id in parentIds)
        {
            await Repository(serverProvider, null).DeleteAsync(OrderIdValue.Create(id), ct);
        }
    }

    public async Task<IReadOnlyList<SyncConfigPendingRow>> PendingAsync(
        IServiceProvider provider,
        CancellationToken ct
    )
    {
        await using var scope = provider.CreateAsyncScope();
        var rows = await scope
            .ServiceProvider.GetRequiredService<SyncEngine>()
            .GetPendingChangesAsync(ct);

        return
        [
            .. rows.Select(row => new SyncConfigPendingRow(
                row.TableName,
                row.KeyText,
                row.IsDelete
            )),
        ];
    }

    public async Task<(string? Problem, int Downloaded)> SyncAsync(
        IServiceProvider provider,
        CancellationToken ct,
        bool lastWriteWins = false,
        bool includeUnboundedBinary = false
    )
    {
        var result = await provider
            .GetRequiredService<SyncEngine>()
            .SyncAsync(
                new SyncOptions
                {
                    Mode = lastWriteWins ? SyncMode.LastWriteWins : SyncMode.Versioned,
                    IncludeUnboundedBinary = includeUnboundedBinary,
                },
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
    )
    {
        var localFactory = Resolve<LocalSqlite.ISqlConnectionFactory>(provider, localKey);
        var direct = !overHttp && !Equals(serverKey, localKey);

        return SyncConfigFixtures.RegistrationProblems(
            localFactory,
            Repository(provider, localKey),
            Resolve<ISyncvoLineRepository>(provider, localKey),
            provider.GetRequiredService<ISyncServerSource<SyncvoOrderEntity, OrderIdValue>>(),
            direct ? Repository(provider, serverKey) : null,
            direct ? Resolve<ServerSqlServer.ISqlConnectionFactory>(provider, serverKey) : null,
            SyncConfigFixtures.TriggerTables(localFactory.CreateConnection()),
            [.. SyncTableNames.Where(table => !parentExcluded || table != ParentTableName)],
            overHttp,
            parentExcluded,
            typeof(LocalSqlite.SyncvoOrderRepository),
            typeof(LocalSqlite.SyncvoLineRepository),
            typeof(ServerSqlServer.SyncvoOrderRepository)
        );
    }

    private static ISyncvoOrderRepository Repository(IServiceProvider provider, object? key) =>
        Resolve<ISyncvoOrderRepository>(provider, key);

    private static TService Resolve<TService>(IServiceProvider provider, object? key)
        where TService : notnull =>
        key is null
            ? provider.GetRequiredService<TService>()
            : provider.GetRequiredKeyedService<TService>(key);

    /// <summary>親の表（注文）を除外するときの excludeFromSync（除外しないなら null）</summary>
    private static Type[]? Exclusions(bool excludeParent) =>
        excludeParent ? [typeof(SyncvoOrderEntity)] : null;

    /// <summary>保存済みの注文を読む（明細は読まない）</summary>
    private static async Task<SyncvoOrderEntity> LoadAsync(
        ISyncvoOrderRepository repository,
        int id,
        CancellationToken ct
    ) =>
        await repository.GetByIdAsync(OrderIdValue.Create(id), ct)
        ?? throw new InvalidOperationException($"order {id} is not there");

    /// <summary>新しい明細（Added。<paramref name="lineId"/> が null ならキーを設定しない）</summary>
    private static SyncvoLineEntity NewLine(SyncvoOrderEntity order, LineIdValue lineId)
    {
        var line = new SyncvoLineEntity
        {
            LineId = lineId,
            OrderId = order.OrderId,
            Qty = QtyValue.Create(1),
        };
        line.MarkAdded();

        return line;
    }

    /// <summary>キーの無い（null の）新しい注文に、Before でキーを決める</summary>
    private sealed class AssignKeyHook(int key) : ISaveHook<SyncvoOrderEntity>
    {
        public Task<bool> BeforeSaveAsync(
            SyncvoOrderEntity entity,
            SaveOperation operation,
            CancellationToken cancellationToken = default
        )
        {
            if (operation == SaveOperation.Insert && entity.OrderId is null)
            {
                entity.OrderId = OrderIdValue.Create(key);
            }

            return Task.FromResult(true);
        }
    }
}
