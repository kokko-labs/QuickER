using System;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using QuickER.Model;
using QuickER.Tests.GeneratedSyncVoFixture;
using QuickER.Tests.GeneratedSyncVoFixture.Repositories.Sqlite;
using QuickER.Tests.GeneratedSyncVoFixture.Repositories.SqlServer;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>同期の構成の行列の、値オブジェクトのキーのフィクスチャ（SyncVoFixture・リモートサービスなし）</summary>
internal sealed class SyncConfigVoKeyFixture : ISyncConfigFixture
{
    public string AxisValue => SyncConfigDimensions.VoKey;

    public bool HasRemoteServices => false;

    public string RemotePrefix =>
        throw new NotSupportedException("SyncVoFixture has no remote services.");

    public ErDiagram ServerDiagram => SyncVoFixtureDefinition.Build();

    public ErDiagram LocalDiagram => SyncVoFixtureDefinition.BuildSqliteMirror();

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
        throw new NotSupportedException("SyncVoFixture has no remote services.");

    public void AddHttpSources(
        IServiceCollection services,
        Func<IServiceProvider, HttpClient> factory
    ) => throw new NotSupportedException("SyncVoFixture has no remote services.");

    public void AddSyncEngine(IServiceCollection services, object? localKey) =>
        services.AddGeneratedSyncEngine(localKey);

    public Task<InProcessRemoteServer> StartRemoteServerAsync(
        string connectionString,
        CancellationToken ct
    ) => throw new NotSupportedException("SyncVoFixture has no remote services.");

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

    private static ISyncvoOrderRepository Repository(IServiceProvider provider, object? key) =>
        key is null
            ? provider.GetRequiredService<ISyncvoOrderRepository>()
            : provider.GetRequiredKeyedService<ISyncvoOrderRepository>(key);

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
