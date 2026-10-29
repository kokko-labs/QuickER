using System;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using QuickER.Model;
using QuickER.Tests.GeneratedSyncFixture;
using QuickER.Tests.GeneratedSyncFixture.Repositories.Sqlite;
using QuickER.Tests.GeneratedSyncFixture.Repositories.SqlServer;

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
        string connectionString
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

    public void AddSyncSupport(IServiceCollection services, object? serverKey, object? localKey) =>
        services.AddGeneratedSyncSupport(serverKey, localKey);

    public void AddDirectSources(IServiceCollection services, object? serverKey) =>
        services.AddGeneratedDirectSyncSources(serverKey);

    public void AddHttpSources(IServiceCollection services, string baseAddress) =>
        services.AddGeneratedHttpSyncSources(baseAddress);

    public void AddHttpSources(
        IServiceCollection services,
        Func<IServiceProvider, HttpClient> factory
    ) => services.AddGeneratedHttpSyncSources(factory);

    public void AddSyncEngine(IServiceCollection services, object? localKey) =>
        services.AddGeneratedSyncEngine(localKey);

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

    public async Task<string?> SyncAsync(IServiceProvider provider, CancellationToken ct)
    {
        var result = await provider
            .GetRequiredService<SyncEngine>()
            .SyncAsync(cancellationToken: ct);

        return result.Conflicts.Count == 0 && result.Truncations.Count == 0
            ? null
            : $"conflicts=[{string.Join(", ", result.Conflicts.Select(c => $"{c.TableName}:{c.KeyText}:{c.Reason}"))}] "
                + $"truncations=[{string.Join(", ", result.Truncations.Select(t => t.TableName))}]";
    }

    private static ISyncOrderRepository Repository(IServiceProvider provider, object? key) =>
        key is null
            ? provider.GetRequiredService<ISyncOrderRepository>()
            : provider.GetRequiredKeyedService<ISyncOrderRepository>(key);

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
