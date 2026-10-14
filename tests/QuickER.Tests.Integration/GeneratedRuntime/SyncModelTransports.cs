using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using QuickER.Provider.SqlServer;
using QuickER.Tests.GeneratedSyncModelFixture;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>端末の同期エンジンがつなぐ差分ソース 4 つ</summary>
internal interface ISyncModelSources
{
    ISyncServerSource<ModelFolderEntity, int> FolderSource { get; }

    ISyncServerSource<ModelLabelEntity, string> LabelSource { get; }

    ISyncServerSource<ModelItemEntity, int> ItemSource { get; }

    ISyncServerSource<ModelEventEntity, DateTime> EventSource { get; }
}

/// <summary>
/// サーバー役（差分ソースと、サーバーへ直接書く他者のリポジトリと、内容の読み出し）。SQLite の代役と実 SQL Server の 2 つ。
/// </summary>
internal interface ISyncModelServerRole : ISyncModelSources, IAsyncDisposable
{
    IRepository<ModelFolderEntity, int> Folders { get; }

    IRepository<ModelLabelEntity, string> Labels { get; }

    IRepository<ModelItemEntity, int> Items { get; }

    IRepository<ModelEventEntity, DateTime> Events { get; }

    /// <summary>品目の除外列（サーバーの他者の blob の書き込みと、HTTP のバイナリのエンドポイントが使う）</summary>
    ISyncBinaryColumns<int> ItemBlobs { get; }

    /// <summary>SQLite の代役なら接続文字列（外部キーの検査に使う）。実 SQL Server は null（外部キーは DB が強制する）</summary>
    string? SqliteConnectionString { get; }

    /// <summary>内容を読む</summary>
    Task<ModelDbState> ReadStateAsync(CancellationToken ct);
}

/// <summary>モデルの世界の組み方（サーバー役・転送経路・外部の ISyncTable の代役）</summary>
internal sealed record SyncModelWorldOptions
{
    /// <summary>直結・SQLite のサーバー役（既定）</summary>
    public static SyncModelWorldOptions Direct { get; } = new();

    /// <summary>サーバー役を作る（既定は SQLite の代役）</summary>
    public Func<
        SyncModelFaults,
        CancellationToken,
        Task<ISyncModelServerRole>
    > CreateServer { get; init; } =
        async (faults, ct) => await SyncModelServer.CreateAsync(faults, ct);

    /// <summary>端末の差分ソースを HTTP 越し（in-process の Kestrel）にするか</summary>
    public bool Http { get; init; }

    /// <summary>表を外部の ISyncTable 実装の代役で包む端末</summary>
    public IReadOnlyCollection<ModelActor> ExternalTables { get; init; } = [];

    /// <summary>この組み方の短い名前（出力用）</summary>
    public string Name { get; init; } = "direct";
}

/// <summary>サーバー役をリモートのエンドポイントの背後へ置くための、リモート面だけの薄いアダプタ</summary>
/// <typeparam name="TEntity">エンティティ型</typeparam>
/// <typeparam name="TKey">主キー型</typeparam>
internal abstract class SyncModelRemoteRepository<TEntity, TKey>(IRepository<TEntity, TKey> inner)
    : IRemoteRepository<TEntity, TKey>
    where TEntity : EntityBase, new()
{
    public Task<TEntity?> GetByIdAsync(TKey id, CancellationToken cancellationToken = default) =>
        inner.GetByIdAsync(id, cancellationToken);

    public Task<IReadOnlyList<TEntity>> GetAllAsync(
        CancellationToken cancellationToken = default
    ) => inner.GetAllAsync(cancellationToken);

    public Task<IReadOnlyList<UniquenessViolation>> CheckUniquenessAsync(
        TEntity entity,
        CancellationToken cancellationToken = default
    ) => inner.CheckUniquenessAsync(entity, cancellationToken);

    public Task InsertAsync(TEntity entity, CancellationToken cancellationToken = default) =>
        inner.InsertAsync(entity, cancellationToken);

    public Task<bool> UpdateAsync(
        TEntity entity,
        ConcurrencyMode mode = ConcurrencyMode.Optimistic,
        CancellationToken cancellationToken = default
    ) => inner.UpdateAsync(entity, mode, cancellationToken);

    public Task<bool> DeleteAsync(TKey id, CancellationToken cancellationToken = default) =>
        inner.DeleteAsync(id, cancellationToken);

    public Task<int> SaveAsync(
        TEntity entity,
        bool cascadeSave = true,
        bool cascadeDelete = true,
        bool insertWhenUpdateMissing = false,
        ConcurrencyMode mode = ConcurrencyMode.Optimistic,
        CancellationToken cancellationToken = default
    ) =>
        inner.SaveAsync(
            entity,
            cascadeSave,
            cascadeDelete,
            insertWhenUpdateMissing,
            mode,
            cancellationToken
        );

    public Task<int> SaveAsync(
        IEnumerable<TEntity> entities,
        bool cascadeSave = true,
        bool cascadeDelete = true,
        bool insertWhenUpdateMissing = false,
        ConcurrencyMode mode = ConcurrencyMode.Optimistic,
        CancellationToken cancellationToken = default
    ) =>
        inner.SaveAsync(
            entities,
            cascadeSave,
            cascadeDelete,
            insertWhenUpdateMissing,
            mode,
            cancellationToken
        );
}

internal sealed class SyncModelFolderRemoteRepository(IRepository<ModelFolderEntity, int> inner)
    : SyncModelRemoteRepository<ModelFolderEntity, int>(inner),
        IModelFolderRemoteRepository { }

internal sealed class SyncModelLabelRemoteRepository(IRepository<ModelLabelEntity, string> inner)
    : SyncModelRemoteRepository<ModelLabelEntity, string>(inner),
        IModelLabelRemoteRepository { }

internal sealed class SyncModelEventRemoteRepository(IRepository<ModelEventEntity, DateTime> inner)
    : SyncModelRemoteRepository<ModelEventEntity, DateTime>(inner),
        IModelEventRemoteRepository { }

/// <summary>品目のリモート面アダプタ（blob の読み書きはサーバー役の除外列のアクセサへ通す＝版の採番と失敗の注入が効く）</summary>
internal sealed class SyncModelItemRemoteRepository(
    IRepository<ModelItemEntity, int> inner,
    ISyncBinaryColumns<int> blobs
) : SyncModelRemoteRepository<ModelItemEntity, int>(inner), IModelItemRemoteRepository
{
    public Task<bool> ReadPayloadAsync(
        int id,
        Stream destination,
        CancellationToken cancellationToken = default
    ) => blobs.ReadUnboundedBinaryAsync("Payload", id, destination, cancellationToken);

    public Task<bool> WritePayloadAsync(
        int id,
        Stream? source,
        long? length = null,
        CancellationToken cancellationToken = default
    ) => blobs.WriteUnboundedBinaryAsync("Payload", id, source, length, cancellationToken);
}

/// <summary>
/// 差分ソースを HTTP 越しにする（サーバー役の差分ソースとリポジトリを in-process の Kestrel の背後へ置き、端末は生成された
/// HTTP の差分ソースでつなぐ）。
/// </summary>
/// <remarks>
/// サーバー側の失敗の注入は、Kestrel の中で例外になり、端末には 500 の <c>RemoteRepositoryException</c> として届く。
/// 注入した失敗かどうかは、実行の前後で失敗が起きた回数が変わったかで見分ける（<see cref="SyncModelRun"/>）。
/// エラーの詳細は公開する（<c>exposeErrorDetails</c>）＝サーバー側の例外の種類と文言が署名へ載る。
/// </remarks>
internal sealed class SyncModelHttpSources : ISyncModelSources, IAsyncDisposable
{
    private readonly InProcessRemoteServer _server;
    private readonly ServiceProvider _client;

    private SyncModelHttpSources(InProcessRemoteServer server, ServiceProvider client)
    {
        _server = server;
        _client = client;
    }

    public ISyncServerSource<ModelFolderEntity, int> FolderSource =>
        _client.GetRequiredService<ISyncServerSource<ModelFolderEntity, int>>();

    public ISyncServerSource<ModelLabelEntity, string> LabelSource =>
        _client.GetRequiredService<ISyncServerSource<ModelLabelEntity, string>>();

    public ISyncServerSource<ModelItemEntity, int> ItemSource =>
        _client.GetRequiredService<ISyncServerSource<ModelItemEntity, int>>();

    public ISyncServerSource<ModelEventEntity, DateTime> EventSource =>
        _client.GetRequiredService<ISyncServerSource<ModelEventEntity, DateTime>>();

    public static async Task<SyncModelHttpSources> StartAsync(
        ISyncModelServerRole role,
        CancellationToken ct
    )
    {
        var server = await InProcessRemoteServer.StartAsync(
            services =>
            {
                services.AddSingleton(role.FolderSource);
                services.AddSingleton(role.LabelSource);
                services.AddSingleton(role.ItemSource);
                services.AddSingleton(role.EventSource);
                services.AddScoped<IModelFolderRemoteRepository>(
                    _ => new SyncModelFolderRemoteRepository(role.Folders)
                );
                services.AddScoped<IModelLabelRemoteRepository>(
                    _ => new SyncModelLabelRemoteRepository(role.Labels)
                );
                services.AddScoped<IModelItemRemoteRepository>(
                    _ => new SyncModelItemRemoteRepository(role.Items, role.ItemBlobs)
                );
                services.AddScoped<IModelEventRemoteRepository>(
                    _ => new SyncModelEventRemoteRepository(role.Events)
                );
            },
            app =>
                app.MapGeneratedRemoteEndpoints(
                    RemoteAccess.AllowAnonymous,
                    exposeErrorDetails: true
                ),
            ct
        );

        var client = new ServiceCollection()
            .AddGeneratedHttpSyncSources(server.BaseAddress(RemotePaths.DefaultPrefix))
            .BuildServiceProvider();

        return new SyncModelHttpSources(server, client);
    }

    public async ValueTask DisposeAsync()
    {
        await _client.DisposeAsync();
        await _server.DisposeAsync();
    }
}

/// <summary>
/// 実 SQL Server のサーバー役（生成された SQL Server 方言のリポジトリと直結の差分ソース・本物の rowversion と
/// <c>MIN_ACTIVE_ROWVERSION()</c>）。世界ごとにデータベースを 1 つ作り、破棄で消す。
/// </summary>
/// <remarks>
/// サーバー側の失敗の注入はつながない（生成された差分ソースとリポジトリをそのまま使うため）。このサーバー役で流すのは、
/// サーバー側の失敗を仕掛けない種とシナリオだけ。
/// </remarks>
internal sealed class SyncModelSqlServerServer : ISyncModelServerRole
{
    private readonly string _masterConnectionString;
    private readonly string _database;
    private readonly string _connectionString;

    private SyncModelSqlServerServer(string masterConnectionString, string database)
    {
        _masterConnectionString = masterConnectionString;
        _database = database;
        _connectionString = new SqlConnectionStringBuilder(masterConnectionString)
        {
            InitialCatalog = database,
        }.ConnectionString;
    }

    public IRepository<ModelFolderEntity, int> Folders { get; private set; } = null!;

    public IRepository<ModelLabelEntity, string> Labels { get; private set; } = null!;

    public IRepository<ModelItemEntity, int> Items { get; private set; } = null!;

    public IRepository<ModelEventEntity, DateTime> Events { get; private set; } = null!;

    public ISyncBinaryColumns<int> ItemBlobs { get; private set; } = null!;

    public string? SqliteConnectionString => null;

    public ISyncServerSource<ModelFolderEntity, int> FolderSource { get; private set; } = null!;

    public ISyncServerSource<ModelLabelEntity, string> LabelSource { get; private set; } = null!;

    public ISyncServerSource<ModelItemEntity, int> ItemSource { get; private set; } = null!;

    public ISyncServerSource<ModelEventEntity, DateTime> EventSource { get; private set; } = null!;

    /// <summary>コンテナに新しいデータベースを作り、図の DDL を当てる</summary>
    public static async Task<SyncModelSqlServerServer> CreateAsync(
        string masterConnectionString,
        CancellationToken ct
    )
    {
        var database = "model_" + Guid.NewGuid().ToString("N");
        var server = new SyncModelSqlServerServer(masterConnectionString, database);

        await ExecuteAsync(masterConnectionString, $"CREATE DATABASE [{database}]", ct);
        await ExecuteAsync(
            server._connectionString,
            new SqlServerDdlGenerator().Build(SyncModelFixtureDefinition.Build()),
            ct
        );

        var factory =
            new QuickER.Tests.GeneratedSyncModelFixture.Repositories.SqlServer.SqlConnectionFactory(
                server._connectionString
            );
        var sql = new QuickER.Tests.GeneratedSyncModelFixture.Repositories.SqlServer.SqlExecutor(
            factory
        );
        var folders =
            new QuickER.Tests.GeneratedSyncModelFixture.Repositories.SqlServer.ModelFolderRepository(
                factory
            );
        var labels =
            new QuickER.Tests.GeneratedSyncModelFixture.Repositories.SqlServer.ModelLabelRepository(
                factory
            );
        var items =
            new QuickER.Tests.GeneratedSyncModelFixture.Repositories.SqlServer.ModelItemRepository(
                factory
            );
        var events =
            new QuickER.Tests.GeneratedSyncModelFixture.Repositories.SqlServer.ModelEventRepository(
                factory
            );

        server.Folders = folders;
        server.Labels = labels;
        server.Items = items;
        server.Events = events;
        server.ItemBlobs = new SyncRepositoryBinaryColumns<ModelItemEntity, int>(
            GeneratedSyncTables.ModelItem,
            items
        );
        server.FolderSource = new DirectSyncSource<ModelFolderEntity, int>(
            sql,
            folders,
            GeneratedSyncTables.ModelFolder
        );
        server.LabelSource = new VersionlessDirectSyncSource<ModelLabelEntity, string>(
            sql,
            labels,
            GeneratedSyncTables.ModelLabel
        );
        server.ItemSource = new DirectSyncSource<ModelItemEntity, int>(
            sql,
            items,
            GeneratedSyncTables.ModelItem
        );
        server.EventSource = new DirectSyncSource<ModelEventEntity, DateTime>(
            sql,
            events,
            GeneratedSyncTables.ModelEvent
        );

        return server;
    }

    public Task<ModelDbState> ReadStateAsync(CancellationToken ct) =>
        SyncModelSqlServerState.ReadAsync(_connectionString, ct);

    public async ValueTask DisposeAsync()
    {
        using (var connection = new SqlConnection(_connectionString))
        {
            SqlConnection.ClearPool(connection);
        }

        await ExecuteAsync(
            _masterConnectionString,
            $"ALTER DATABASE [{_database}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{_database}]",
            CancellationToken.None
        );
    }

    private static async Task ExecuteAsync(
        string connectionString,
        string sql,
        CancellationToken ct
    )
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(ct);
        await using var command = new SqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(ct);
    }
}

/// <summary>実 SQL Server の内容の読み出し（SQLite と同じ正規の文字列へそろえる）</summary>
internal static class SyncModelSqlServerState
{
    public static async Task<ModelDbState> ReadAsync(string connectionString, CancellationToken ct)
    {
        var state = new ModelDbState();

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(ct);

        await using (
            var command = new SqlCommand(
                "SELECT folder_id, parent_id, name FROM model_folders",
                connection
            )
        )
        await using (var r = await command.ExecuteReaderAsync(ct))
        {
            while (await r.ReadAsync(ct))
            {
                state.Folders[r.GetInt32(0)] = (
                    r.IsDBNull(1) ? null : r.GetInt32(1),
                    r.GetString(2)
                );
            }
        }

        await using (
            var command = new SqlCommand(
                "SELECT label_code, folder_id, caption FROM model_labels",
                connection
            )
        )
        await using (var r = await command.ExecuteReaderAsync(ct))
        {
            while (await r.ReadAsync(ct))
            {
                state.Labels[r.GetString(0)] = (r.GetInt32(1), r.GetString(2));
            }
        }

        await using (
            var command = new SqlCommand(
                "SELECT item_id, folder_id, label_code, code, title, payload FROM model_items",
                connection
            )
        )
        await using (var r = await command.ExecuteReaderAsync(ct))
        {
            while (await r.ReadAsync(ct))
            {
                state.Items[r.GetInt32(0)] = (
                    r.GetInt32(1),
                    r.IsDBNull(2) ? null : r.GetString(2),
                    r.GetString(3),
                    r.GetString(4),
                    r.IsDBNull(5) ? "NULL" : Convert.ToHexString((byte[])r.GetValue(5))
                );
            }
        }

        await using (
            var command = new SqlCommand(
                "SELECT event_at, item_id, memo FROM model_events",
                connection
            )
        )
        await using (var r = await command.ExecuteReaderAsync(ct))
        {
            while (await r.ReadAsync(ct))
            {
                var at = r.GetDateTime(0);
                state.Events[ModelTables.EventKeyText(at)] = (at, r.GetInt32(1), r.GetString(2));
            }
        }

        return state;
    }
}
