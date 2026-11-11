using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using QuickER.Tests.GeneratedSyncModelFixture;
using QuickER.Tests.GeneratedSyncModelFixture.Repositories.Sqlite;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>1 つの DB の内容（モデルの照合と、次の操作の生成に使う）</summary>
/// <remarks>
/// 照合は格納値どうしで行う（行バージョン列は比べない＝ミラーの版は端末ごとに正当に違い得る）。除外列（<c>payload</c>）は
/// 行とは別に 16 進で持つ。
/// </remarks>
internal sealed class ModelDbState
{
    /// <summary>フォルダー（キー→親・名前・ピン留めした品目＝表どうしの循環の列）</summary>
    public Dictionary<int, (int? ParentId, string Name, int? PinnedItemId)> Folders { get; } = [];

    /// <summary>ラベル（キー→フォルダー・見出し）</summary>
    public Dictionary<string, (int FolderId, string Caption)> Labels { get; } =
        new(StringComparer.Ordinal);

    /// <summary>品目（キー→フォルダー・ラベル・コード・題名・payload の 16 進）</summary>
    public Dictionary<
        int,
        (int FolderId, string? LabelCode, string Code, string Title, string PayloadHex)
    > Items { get; } = [];

    /// <summary>イベント（格納値のキー文字列→日時・品目・メモ）</summary>
    public Dictionary<string, (DateTime At, int ItemId, string Memo)> Events { get; } =
        new(StringComparer.Ordinal);

    /// <summary>表ごとの「キー→行の正規の文字列」（payload を含まない）</summary>
    public IReadOnlyDictionary<string, string> Rows(string table) =>
        table switch
        {
            ModelTables.Folders => Folders.ToDictionary(
                p => Key(p.Key),
                p =>
                    $"parent={Show(p.Value.ParentId)} name={p.Value.Name}{Pinned(p.Value.PinnedItemId)}",
                StringComparer.Ordinal
            ),
            ModelTables.Labels => Labels.ToDictionary(
                p => p.Key,
                p => $"folder={Key(p.Value.FolderId)} caption={p.Value.Caption}",
                StringComparer.Ordinal
            ),
            ModelTables.Items => Items.ToDictionary(
                p => Key(p.Key),
                p =>
                    $"folder={Key(p.Value.FolderId)} label={p.Value.LabelCode ?? "null"} "
                    + $"code={p.Value.Code} title={p.Value.Title}",
                StringComparer.Ordinal
            ),
            ModelTables.Events => Events.ToDictionary(
                p => p.Key,
                p => $"item={Key(p.Value.ItemId)} memo={p.Value.Memo}",
                StringComparer.Ordinal
            ),
            _ => throw new ArgumentOutOfRangeException(nameof(table), table, null),
        };

    /// <summary>品目のキー→payload の 16 進</summary>
    public IReadOnlyDictionary<string, string> Payloads() =>
        Items.ToDictionary(p => Key(p.Key), p => p.Value.PayloadHex, StringComparer.Ordinal);

    /// <summary>行の正規の文字列（無ければ null）</summary>
    public string? Row(string table, string keyText) =>
        Rows(table).TryGetValue(keyText, out var row) ? row : null;

    /// <summary>整数キーの文字列</summary>
    public static string Key(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Show(int? value) => value is { } v ? Key(v) : "null";

    /// <summary>ピン留めの列（NULL のときは何も書かない＝この列を書かない行の文字列は列を足す前と同じ）</summary>
    internal static string Pinned(int? value) => value is { } v ? $" pinned={Key(v)}" : "";

    /// <summary>SQLite の DB ファイルから内容を読む</summary>
    public static async Task<ModelDbState> ReadAsync(string connectionString, CancellationToken ct)
    {
        var state = new ModelDbState();

        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(ct);

        // 決定的なシナリオがローカルの表を一時的に消すことがある（DropLocalTableOp）。無い表は空として読む
        var tables = new HashSet<string>(StringComparer.Ordinal);

        await foreach (
            var r in QueryAsync(
                connection,
                "SELECT name FROM sqlite_master WHERE type = 'table'",
                ct
            )
        )
        {
            tables.Add(r.GetString(0));
        }

        await foreach (
            var r in QueryAsync(
                connection,
                "SELECT folder_id, parent_id, name, pinned_item_id FROM model_folders",
                ct
            )
        )
        {
            state.Folders[r.GetInt32(0)] = (
                r.IsDBNull(1) ? null : r.GetInt32(1),
                r.GetString(2),
                r.IsDBNull(3) ? null : r.GetInt32(3)
            );
        }

        await foreach (
            var r in QueryAsync(
                connection,
                tables.Contains(ModelTables.Labels)
                    ? "SELECT label_code, folder_id, caption FROM model_labels"
                    : "SELECT NULL, NULL, NULL WHERE 0",
                ct
            )
        )
        {
            state.Labels[r.GetString(0)] = (r.GetInt32(1), r.GetString(2));
        }

        await foreach (
            var r in QueryAsync(
                connection,
                "SELECT item_id, folder_id, label_code, code, title, hex(payload) FROM model_items",
                ct
            )
        )
        {
            state.Items[r.GetInt32(0)] = (
                r.GetInt32(1),
                r.IsDBNull(2) ? null : r.GetString(2),
                r.GetString(3),
                r.GetString(4),
                r.IsDBNull(5) ? "NULL" : r.GetString(5)
            );
        }

        await foreach (
            var r in QueryAsync(connection, "SELECT event_at, item_id, memo FROM model_events", ct)
        )
        {
            var text = r.GetString(0);
            state.Events[text] = (
                DateTime.Parse(text, CultureInfo.InvariantCulture),
                r.GetInt32(1),
                r.GetString(2)
            );
        }

        return state;
    }

    private static async IAsyncEnumerable<SqliteDataReader> QueryAsync(
        SqliteConnection connection,
        string sql,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct
    )
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await using var reader = await command.ExecuteReaderAsync(ct);

        while (await reader.ReadAsync(ct))
        {
            yield return reader;
        }
    }
}

/// <summary>同期 1 回の結果（モデルの照合と、シナリオの結果の面に使う分だけ）</summary>
internal sealed record ModelSyncOutcome(
    IReadOnlyList<SyncConflict> Conflicts,
    IReadOnlyList<SyncDownloadTruncation> Truncations
)
{
    /// <summary>同期（SyncAsync）が自分から変えたローカルの行（SyncResult.LocalRowChanges）</summary>
    public IReadOnlyList<SyncLocalRowChange> LocalRowChanges { get; init; } = [];

    /// <summary>上げた件数（SyncAsync と UploadAsync が返す。DownloadAsync は null）</summary>
    public int? Uploaded { get; init; }

    /// <summary>降ろした件数（SyncAsync と DownloadAsync が返す。UploadAsync は null）</summary>
    public int? Downloaded { get; init; }

    /// <summary>削除伝搬で消した件数（SyncAsync と DownloadAsync が返す）</summary>
    public int? DeletedLocally { get; init; }

    /// <summary>送らずに捨てた記録の件数（SyncAsync と UploadAsync が返す）</summary>
    public int? Discarded { get; init; }

    /// <summary>結果が名乗る「競合がある」「打ち切りがある」（SyncAsync の SyncResult だけが持つ。ほかは null）</summary>
    public (bool Conflicts, bool Truncations)? ReportedFlags { get; init; }
}

/// <summary>端末 1 台（ローカル SQLite・ジャーナル・ジャーナル記録デコレータ・同期エンジン）</summary>
internal sealed class SyncModelDevice : IDisposable
{
    private readonly SqliteTempDatabase _db;

    private SyncModelDevice(ModelActor actor, SqliteTempDatabase db)
    {
        Actor = actor;
        _db = db;
    }

    public ModelActor Actor { get; }

    public string ConnectionString => _db.ReadWriteCreateConnectionString;

    public ISqlExecutor Sql { get; private set; } = null!;

    public SyncJournal Journal { get; private set; } = null!;

    public IModelFolderRepository Folders { get; private set; } = null!;

    public IModelLabelRepository Labels { get; private set; } = null!;

    public IModelItemRepository Items { get; private set; } = null!;

    public IModelEventRepository Events { get; private set; } = null!;

    /// <summary>記録しないフォルダーのリポジトリ（ジャーナル記録デコレータの内側。記録しない書き込みの手が使う）</summary>
    public IModelFolderRepository RawFolders { get; private set; } = null!;

    /// <summary>記録しない品目のリポジトリ</summary>
    public IModelItemRepository RawItems { get; private set; } = null!;

    /// <summary>記録しないラベルのリポジトリ</summary>
    public IModelLabelRepository RawLabels { get; private set; } = null!;

    public SyncEngine Engine { get; private set; } = null!;

    /// <summary>
    /// この端末が挿入したときの実体（表とキーで引く）。同期の後もメモリに持っていた実体で削除する操作
    /// （<see cref="DeleteHeldOp"/>）が使う。
    /// </summary>
    public Dictionary<(string Table, string Key), EntityBase> Held { get; } = [];

    /// <summary>端末を作り、サーバーの差分ソースへつないだ同期エンジンを組む（DI 登録の中身と同じ組み方）</summary>
    /// <param name="actor">端末</param>
    /// <param name="server">サーバー役</param>
    /// <param name="faults">失敗の注入（ローカルの Save フックが見る）</param>
    /// <param name="externalTables">表を外部の <see cref="ISyncTable"/> 実装の代役で包むか</param>
    /// <param name="ct">キャンセルトークン</param>
    public static async Task<SyncModelDevice> CreateAsync(
        ModelActor actor,
        ISyncModelSources server,
        SyncModelFaults faults,
        bool externalTables,
        CancellationToken ct
    )
    {
        var device = new SyncModelDevice(actor, SqliteTempDatabase.Create());
        await device._db.ApplyDdlAsync(SyncModelFixtureDefinition.BuildSqliteMirror(), ct);

        var factory = new LightJournalSyncModelConnectionFactory(
            device._db.ReadWriteCreateConnectionString
        );
        device._factory = factory;
        device.Sql = new SqlExecutor(factory);
        device.Journal = new SyncJournal(device.Sql);
        await device.Journal.EnsureCreatedAsync(ct);

        // 失敗の注入の口（Save フック）。キーを決めるフックは整数キーの表だけ（挿入する行の主キーを別の値へ変える）
        var hooks = new SaveHookRegistry()
            .Add(
                new SyncModelHook<ModelFolderEntity>(
                    faults,
                    e => e.FolderId += RekeyOffset,
                    e => e.FolderId == 0,
                    (e, key) => e.FolderId = key
                )
            )
            .Add(new SyncModelHook<ModelLabelEntity>(faults, null))
            .Add(
                new SyncModelHook<ModelItemEntity>(
                    faults,
                    e => e.ItemId += RekeyOffset,
                    e => e.ItemId == 0,
                    (e, key) => e.ItemId = key
                )
            )
            .Add(new SyncModelHook<ModelEventEntity>(faults, null));

        device.RawFolders = new ModelFolderRepository(factory, hooks);
        device.RawLabels = new ModelLabelRepository(factory, hooks);
        device.RawItems = new ModelItemRepository(factory, hooks);
        device.Folders = new JournalingModelFolderRepository(device.RawFolders, device.Journal);
        device.Labels = new JournalingModelLabelRepository(device.RawLabels, device.Journal);
        device.Items = new JournalingModelItemRepository(device.RawItems, device.Journal);
        device.Events = new JournalingModelEventRepository(
            new ModelEventRepository(factory, hooks),
            device.Journal
        );

        ISyncTable[] tables =
        [
            new SyncTable<ModelFolderEntity, int>(
                device.Folders,
                device.Sql,
                server.FolderSource,
                GeneratedSyncTables.ModelFolder,
                hooks
            ),
            new VersionlessSyncTable<ModelLabelEntity, string>(
                device.Labels,
                device.Sql,
                server.LabelSource,
                GeneratedSyncTables.ModelLabel,
                hooks
            ),
            new SyncTable<ModelItemEntity, int>(
                device.Items,
                device.Sql,
                server.ItemSource,
                GeneratedSyncTables.ModelItem,
                hooks
            ),
            new SyncTable<ModelEventEntity, DateTime>(
                device.Events,
                device.Sql,
                server.EventSource,
                GeneratedSyncTables.ModelEvent,
                hooks
            ),
        ];

        device.Engine = new SyncEngine(
            externalTables ? tables.Select(t => (ISyncTable)new SyncModelExternalTable(t)) : tables,
            device.Journal
        );

        return device;
    }

    /// <summary>キーを決めるフックが挿入する行の主キーへ足す値</summary>
    public const int RekeyOffset = 100000;

    /// <summary>未送信のジャーナルの件数</summary>
    public async Task<int> PendingCountAsync(CancellationToken ct) =>
        (await Journal.ReadAllAsync(ct)).Count;

    private ISqlConnectionFactory? _factory;

    public void Dispose()
    {
        SyncModelPools.Clear(_factory);
        _db.Dispose();
    }
}

/// <summary>生成された接続ファクトリの接続プールを解放する</summary>
/// <remarks>
/// 生成された SQLite の接続ファクトリは接続文字列へ <c>Foreign Keys=True</c> を足すため、一時 DB の後始末
/// （<see cref="SqliteTempDatabase.Dispose"/>）が解放する接続文字列とプールが別になる。解放しないと、種ごとに開いた
/// ファイルのプールが溜まり、種を増やすほど遅くなる（600 種で 1 種あたりの時間が 4 倍まで伸びた）。
/// </remarks>
internal static class SyncModelPools
{
    public static void Clear(ISqlConnectionFactory? factory)
    {
        if (factory is null)
        {
            return;
        }

        using var connection = factory.CreateConnection();
        SqliteConnection.ClearPool(connection);
    }
}

/// <summary>サーバー役（SQLite・版の採番・差分ソース）</summary>
internal sealed class SyncModelServer : ISyncModelServerRole
{
    private readonly SqliteTempDatabase _db = SqliteTempDatabase.Create();

    private SyncModelServer() { }

    public string ConnectionString => _db.ReadWriteCreateConnectionString;

    public string? SqliteConnectionString => ConnectionString;

    /// <summary>SQLite の一意制約は既定の照合順序（BINARY）＝序数で比べる（図の DDL は列に COLLATE を付けない）</summary>
    public IEqualityComparer<string> UniqueValues => StringComparer.Ordinal;

    public Task<ModelDbState> ReadStateAsync(CancellationToken ct) =>
        ModelDbState.ReadAsync(ConnectionString, ct);

    public IRepository<ModelFolderEntity, int> Folders { get; private set; } = null!;

    public IRepository<ModelLabelEntity, string> Labels { get; private set; } = null!;

    public IRepository<ModelItemEntity, int> Items { get; private set; } = null!;

    public IRepository<ModelEventEntity, DateTime> Events { get; private set; } = null!;

    public ISyncBinaryColumns<int> ItemBlobs { get; private set; } = null!;

    public ISyncServerSource<ModelFolderEntity, int> FolderSource { get; private set; } = null!;

    public ISyncServerSource<ModelLabelEntity, string> LabelSource { get; private set; } = null!;

    public ISyncServerSource<ModelItemEntity, int> ItemSource { get; private set; } = null!;

    public ISyncServerSource<ModelEventEntity, DateTime> EventSource { get; private set; } = null!;

    public static async Task<SyncModelServer> CreateAsync(
        SyncModelFaults faults,
        CancellationToken ct
    )
    {
        var server = new SyncModelServer();
        await server._db.ApplyDdlAsync(SyncModelFixtureDefinition.BuildSqliteMirror(), ct);

        var versions = new SyncTestVersionSequence();
        var factory = new LightJournalSyncModelConnectionFactory(
            server._db.ReadWriteCreateConnectionString
        );
        server._factory = factory;
        var sql = new SqlExecutor(factory);

        server.Folders = new SyncModelServerRepository<ModelFolderEntity, int>(
            new ModelFolderRepository(factory),
            versions,
            e => e.FolderId,
            e => e.RowVer,
            (e, v) => e.RowVer = v,
            faults
        );

        // 版なしの表は素のリポジトリ（版の採番も版ガードも無いのが本物の意味論）
        server.Labels = new ModelLabelRepository(factory);

        var rawItems = new ModelItemRepository(factory);
        server.Items = new SyncModelServerRepository<ModelItemEntity, int>(
            rawItems,
            versions,
            e => e.ItemId,
            e => e.RowVer,
            (e, v) => e.RowVer = v,
            faults
        );
        server.ItemBlobs = new SyncModelItemBinaryColumns(rawItems, sql, versions, faults);

        server.Events = new SyncModelServerRepository<ModelEventEntity, DateTime>(
            new ModelEventRepository(factory),
            versions,
            e => e.EventAt,
            e => e.RowVer,
            (e, v) => e.RowVer = v,
            faults
        );

        server.FolderSource = new SyncModelServerSource<ModelFolderEntity, int>(
            sql,
            server.Folders,
            SyncModelServerSql.FolderChanges,
            SyncModelServerSql.FolderKeys,
            faults
        );
        server.LabelSource = new SyncModelVersionlessServerSource<ModelLabelEntity, string>(
            sql,
            server.Labels,
            SyncModelServerSql.LabelFirstPage,
            SyncModelServerSql.LabelPageAfter,
            SyncModelServerSql.LabelKeys,
            faults
        );
        server.ItemSource = new SyncModelServerSource<ModelItemEntity, int>(
            sql,
            server.Items,
            SyncModelServerSql.ItemChanges,
            SyncModelServerSql.ItemKeys,
            faults,
            server.ItemBlobs
        );
        server.EventSource = new SyncModelServerSource<ModelEventEntity, DateTime>(
            sql,
            server.Events,
            SyncModelServerSql.EventChanges,
            SyncModelServerSql.EventKeys,
            faults
        );

        return server;
    }

    private ISqlConnectionFactory? _factory;

    public ValueTask DisposeAsync()
    {
        SyncModelPools.Clear(_factory);
        _db.Dispose();

        return ValueTask.CompletedTask;
    }
}

/// <summary>モデルの世界（サーバー役 1 つと端末 2 台）。操作を 1 つずつ適用する</summary>
internal sealed class SyncModelWorld : IAsyncDisposable
{
    private readonly SyncModelHttpSources? _http;

    private SyncModelWorld(
        ISyncModelServerRole server,
        SyncModelHttpSources? http,
        SyncModelDevice device1,
        SyncModelDevice device2,
        SyncModelFaults faults
    )
    {
        Server = server;
        _http = http;
        Device1 = device1;
        Device2 = device2;
        Faults = faults;
    }

    /// <summary>失敗の注入</summary>
    public SyncModelFaults Faults { get; }

    /// <summary>端末の差分ソースが HTTP 越しか</summary>
    public bool Http => _http is not null;

    public ISyncModelServerRole Server { get; }

    public SyncModelDevice Device1 { get; }

    public SyncModelDevice Device2 { get; }

    public IEnumerable<SyncModelDevice> Devices => [Device1, Device2];

    /// <param name="options">サーバー役・転送経路・外部の ISyncTable の代役</param>
    /// <param name="ct">キャンセルトークン</param>
    public static async Task<SyncModelWorld> CreateAsync(
        SyncModelWorldOptions options,
        CancellationToken ct
    )
    {
        var faults = new SyncModelFaults();
        var server = await options.CreateServer(faults, ct);
        var http = options.Http ? await SyncModelHttpSources.StartAsync(server, ct) : null;
        ISyncModelSources sources = http is null ? server : http;
        var device1 = await SyncModelDevice.CreateAsync(
            ModelActor.Device1,
            sources,
            faults,
            options.ExternalTables.Contains(ModelActor.Device1),
            ct
        );
        var device2 = await SyncModelDevice.CreateAsync(
            ModelActor.Device2,
            sources,
            faults,
            options.ExternalTables.Contains(ModelActor.Device2),
            ct
        );

        return new SyncModelWorld(server, http, device1, device2, faults);
    }

    /// <summary>端末を引く</summary>
    public SyncModelDevice Device(ModelActor actor) =>
        actor switch
        {
            ModelActor.Device1 => Device1,
            ModelActor.Device2 => Device2,
            _ => throw new ArgumentOutOfRangeException(nameof(actor), actor, null),
        };

    /// <summary>書き手の DB の内容を読む</summary>
    public Task<ModelDbState> ReadStateAsync(ModelActor actor, CancellationToken ct) =>
        actor == ModelActor.Server
            ? Server.ReadStateAsync(ct)
            : ModelDbState.ReadAsync(Device(actor).ConnectionString, ct);

    /// <summary>同期を 1 回流す</summary>
    public async Task<ModelSyncOutcome> SyncAsync(ModelSyncOp op, CancellationToken ct)
    {
        Faults.Syncing = op.Device;

        try
        {
            return await SyncCoreAsync(op, ct);
        }
        finally
        {
            Faults.Syncing = null;
        }
    }

    private async Task<ModelSyncOutcome> SyncCoreAsync(ModelSyncOp op, CancellationToken ct)
    {
        var engine = Device(op.Device).Engine;
        var options = op.ToOptions();

        switch (op.Kind)
        {
            case ModelSyncKind.Sync:
            {
                var result = await engine.SyncAsync(options, ct);

                return new ModelSyncOutcome(result.Conflicts, result.Truncations)
                {
                    LocalRowChanges = result.LocalRowChanges,
                    Uploaded = result.Uploaded,
                    Downloaded = result.Downloaded,
                    DeletedLocally = result.DeletedLocally,
                    Discarded = result.Discarded,
                    ReportedFlags = (result.HasConflicts, result.HasTruncations),
                };
            }

            case ModelSyncKind.Upload:
            {
                var conflicts = new List<SyncConflict>();
                var (uploaded, discarded) = await engine.UploadAsync(options, conflicts, ct);

                return new ModelSyncOutcome(conflicts, [])
                {
                    Uploaded = uploaded,
                    Discarded = discarded,
                };
            }

            case ModelSyncKind.Download:
            {
                var (downloaded, deletedLocally) = await engine.DownloadAsync(options, ct);

                return new ModelSyncOutcome([], [])
                {
                    Downloaded = downloaded,
                    DeletedLocally = deletedLocally,
                };
            }

            default:
                throw new ArgumentOutOfRangeException(nameof(op), op.Kind, null);
        }
    }

    /// <summary>洗い替えを 1 回流す</summary>
    public Task<SyncRefreshResult> RefreshAsync(ModelRefreshOp op, CancellationToken ct) =>
        RefreshAsync(op.Device, op.ToOptions(), ct);

    /// <summary>洗い替えを、オプションを指定して 1 回流す（モデルの手が持たないオプション＝Force・除外を使うシナリオの手）</summary>
    public async Task<SyncRefreshResult> RefreshAsync(
        ModelActor device,
        SyncRefreshOptions? options,
        CancellationToken ct
    )
    {
        Faults.Syncing = device;
        Faults.Refreshing = true;

        try
        {
            return await Device(device).Engine.RefreshAsync(options, ct);
        }
        finally
        {
            Faults.Syncing = null;
            Faults.Refreshing = false;
        }
    }

    /// <summary>書き込みの操作を書き手の DB へ適用する</summary>
    public async Task ApplyAsync(ModelWriteOp op, CancellationToken ct)
    {
        if (op.Actor == ModelActor.Server)
        {
            await ApplyOnServerAsync(op, ct);

            return;
        }

        Faults.Writing = op.Actor;

        try
        {
            await ApplyOnDeviceAsync(op, ct);
        }
        finally
        {
            Faults.Writing = null;
            Faults.KeyToAssign = null;
        }
    }

    /// <summary>
    /// HookAssignKey が仕掛けてあり、グラフ保存の挿入なら、キーを入れずに（0 で）実体を作り、フックが入れるキーを控える
    /// （直接の挿入はフックが走らないので、そのままのキーで作る）
    /// </summary>
    private int KeyFor(ModelWriteOp op, int key)
    {
        if (op.Via == ModelWriteVia.Graph && Faults.IsArmed(ModelFaultKind.HookAssignKey, op.Actor))
        {
            Faults.KeyToAssign = key;

            return 0;
        }

        return key;
    }

    private async Task ApplyOnDeviceAsync(ModelWriteOp op, CancellationToken ct)
    {
        var device = Device(op.Actor);

        switch (op)
        {
            case AddFolderOp add:
                await AddAsync(
                    device,
                    device.Folders,
                    new ModelFolderEntity
                    {
                        FolderId = KeyFor(op, add.Id),
                        ParentId = add.ParentId,
                        Name = add.Name,
                    },
                    op,
                    ct
                );
                break;

            case UpdateFolderOp update:
            {
                var entity = await Load(device.Folders.GetByIdAsync(update.Id, ct), op);
                entity.ParentId = update.ParentId;
                entity.Name = update.Name;
                await UpdateAsync(device.Folders, entity, op.Via, ct);
                break;
            }

            case PinFolderOp pin:
            {
                var entity = await Load(device.Folders.GetByIdAsync(pin.Id, ct), op);
                entity.PinnedItemId = pin.ItemId;
                await UpdateAsync(device.Folders, entity, op.Via, ct);
                break;
            }

            case DeleteFolderOp delete:
                await DeleteAsync(device.Folders, delete.Id, op, ct);
                break;

            case AddLabelOp add:
                await AddAsync(
                    device,
                    device.Labels,
                    new ModelLabelEntity
                    {
                        LabelCode = add.Code,
                        FolderId = add.FolderId,
                        Caption = add.Caption,
                    },
                    op,
                    ct
                );
                break;

            case UpdateLabelOp update:
            {
                var entity = await Load(device.Labels.GetByIdAsync(update.Code, ct), op);
                entity.FolderId = update.FolderId;
                entity.Caption = update.Caption;
                await UpdateAsync(device.Labels, entity, op.Via, ct);
                break;
            }

            case DeleteLabelOp delete:
                await DeleteAsync(device.Labels, delete.Code, op, ct);
                break;

            case AddItemOp add:
                await AddAsync(
                    device,
                    device.Items,
                    new ModelItemEntity
                    {
                        ItemId = KeyFor(op, add.Id),
                        FolderId = add.FolderId,
                        LabelCode = add.LabelCode,
                        Code = add.Code,
                        Title = add.Title,
                        Payload = Convert.FromHexString(add.PayloadHex),
                    },
                    op,
                    ct
                );
                break;

            case UpdateItemOp update:
            {
                var entity = await Load(device.Items.GetByIdAsync(update.Id, ct), op);
                entity.FolderId = update.FolderId;
                entity.LabelCode = update.LabelCode;
                entity.Code = update.Code;
                entity.Title = update.Title;
                await UpdateAsync(device.Items, entity, op.Via, ct);
                break;
            }

            case DeleteItemOp delete:
                await DeleteAsync(device.Items, delete.Id, op, ct);
                break;

            case WritePayloadOp write:
            {
                var bytes = Convert.FromHexString(write.PayloadHex);

                // 失敗する書き込みは、読み進めると途中で失敗するストリームを渡す（記録の後に業務の書き込みが失敗する）
                using Stream source = write.ExpectFailure
                    ? new SyncModelFailingStream(bytes)
                    : new MemoryStream(bytes);

                if (!await device.Items.WritePayloadAsync(write.Id, source, bytes.Length, ct))
                {
                    throw new InvalidOperationException($"No row to write: {op.Describe()}");
                }

                break;
            }

            case AddEventOp add:
                await AddAsync(
                    device,
                    device.Events,
                    new ModelEventEntity
                    {
                        EventAt = add.At,
                        ItemId = add.ItemId,
                        Memo = add.Memo,
                    },
                    op,
                    ct
                );
                break;

            case UpdateEventOp update:
            {
                var entity = await Load(device.Events.GetByIdAsync(update.At, ct), op);
                entity.ItemId = update.ItemId;
                entity.Memo = update.Memo;
                await UpdateAsync(device.Events, entity, op.Via, ct);
                break;
            }

            case DeleteEventOp delete:
                await DeleteAsync(device.Events, delete.At, op, ct);
                break;

            case DeleteStubOp stub:
                await DeleteStubAsync(
                    ModelRepositories.Of(device),
                    stub.StubTable,
                    stub.StubKeyText,
                    ct
                );
                break;

            case DeleteHeldOp held:
            {
                if (!device.Held.TryGetValue((held.HeldTable, held.HeldKeyText), out var entity))
                {
                    throw new InvalidOperationException($"No held entity: {op.Describe()}");
                }

                entity.MarkRemoved();
                await SaveHeldAsync(ModelRepositories.Of(device), held.HeldTable, entity, ct);
                break;
            }

            case SaveFolderGraphOp graph:
                await SaveFolderGraphAsync(device, graph, ct);
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(op), op.Describe(), null);
        }
    }

    /// <summary>フォルダーを根に、子の品目・ラベルを RowState つきで 1 回の SaveAsync で保存する</summary>
    private static async Task SaveFolderGraphAsync(
        SyncModelDevice device,
        SaveFolderGraphOp op,
        CancellationToken ct
    )
    {
        var root =
            op.Root.State == ModelRowState.Added
                ? new ModelFolderEntity
                {
                    FolderId = op.Root.Id,
                    Name = op.Root.Name ?? $"f{op.Root.Id}",
                }
                : await Load(device.Folders.GetByIdAsync(op.Root.Id, ct), op);

        if (op.Root.State == ModelRowState.Updated && op.Root.Name is { } name)
        {
            root.Name = name;
        }

        Mark(root, op.Root.State);

        foreach (var spec in op.Items)
        {
            var item =
                spec.State == ModelRowState.Added
                    ? new ModelItemEntity
                    {
                        ItemId = spec.Id,
                        FolderId = op.Root.Id,
                        Code = $"c-{spec.Id}",
                        Title = spec.Title ?? $"t{spec.Id}",
                        Payload = Convert.FromHexString($"0A0B{spec.Id:X4}"),
                    }
                    : await Load(device.Items.GetByIdAsync(spec.Id, ct), op);

            if (spec.State == ModelRowState.Updated && spec.Title is { } title)
            {
                item.Title = title;
            }

            Mark(item, spec.State);
            root.ModelItems.Add(item);
        }

        foreach (var spec in op.Labels)
        {
            var label =
                spec.State == ModelRowState.Added
                    ? new ModelLabelEntity
                    {
                        LabelCode = spec.Code,
                        FolderId = op.Root.Id,
                        Caption = spec.Caption ?? spec.Code,
                    }
                    : await Load(device.Labels.GetByIdAsync(spec.Code, ct), op);

            if (spec.State == ModelRowState.Updated && spec.Caption is { } caption)
            {
                label.Caption = caption;
            }

            Mark(label, spec.State);
            root.ModelLabels.Add(label);
        }

        await device.Folders.SaveAsync(root, cancellationToken: ct);

        static void Mark(EntityBase entity, ModelRowState state)
        {
            switch (state)
            {
                case ModelRowState.Added:
                    entity.MarkAdded();
                    break;
                case ModelRowState.Updated:
                    entity.MarkUpdated();
                    break;
                case ModelRowState.Removed:
                    entity.MarkRemoved();
                    break;
                default:
                    entity.MarkUnchanged();
                    break;
            }
        }
    }

    /// <summary>キーだけのスタブ（新しい実体にキーだけ）に RowState=Removed を付けてグラフ保存する</summary>
    private static Task DeleteStubAsync(
        ModelRepositories device,
        string table,
        string keyText,
        CancellationToken ct
    ) =>
        table switch
        {
            ModelTables.Folders => SaveStubAsync(
                device.Folders,
                new ModelFolderEntity { FolderId = ParseInt(keyText) },
                ct
            ),
            ModelTables.Labels => SaveStubAsync(
                device.Labels,
                new ModelLabelEntity { LabelCode = keyText },
                ct
            ),
            ModelTables.Items => SaveStubAsync(
                device.Items,
                new ModelItemEntity { ItemId = ParseInt(keyText) },
                ct
            ),
            ModelTables.Events => SaveStubAsync(
                device.Events,
                new ModelEventEntity
                {
                    EventAt = DateTime.Parse(keyText, CultureInfo.InvariantCulture),
                },
                ct
            ),
            _ => throw new ArgumentOutOfRangeException(nameof(table), table, null),
        };

    private static async Task SaveStubAsync<TEntity, TKey>(
        IRepository<TEntity, TKey> repository,
        TEntity stub,
        CancellationToken ct
    )
        where TEntity : EntityBase, new()
    {
        stub.MarkRemoved();
        await repository.SaveAsync(stub, cancellationToken: ct);
    }

    /// <summary>メモリに持っていた実体をグラフ保存する</summary>
    private static Task SaveHeldAsync(
        ModelRepositories device,
        string table,
        EntityBase entity,
        CancellationToken ct
    ) =>
        table switch
        {
            ModelTables.Folders => device.Folders.SaveAsync(
                (ModelFolderEntity)entity,
                cancellationToken: ct
            ),
            ModelTables.Labels => device.Labels.SaveAsync(
                (ModelLabelEntity)entity,
                cancellationToken: ct
            ),
            ModelTables.Items => device.Items.SaveAsync(
                (ModelItemEntity)entity,
                cancellationToken: ct
            ),
            ModelTables.Events => device.Events.SaveAsync(
                (ModelEventEntity)entity,
                cancellationToken: ct
            ),
            _ => throw new ArgumentOutOfRangeException(nameof(table), table, null),
        };

    private static int ParseInt(string text) => int.Parse(text, CultureInfo.InvariantCulture);

    private async Task ApplyOnServerAsync(ModelWriteOp op, CancellationToken ct)
    {
        switch (op)
        {
            case AddFolderOp add:
                await InsertOnServerAsync(
                    Server.Folders,
                    new ModelFolderEntity
                    {
                        FolderId = add.Id,
                        ParentId = add.ParentId,
                        Name = add.Name,
                    },
                    op,
                    ct
                );
                break;

            case UpdateFolderOp update:
            {
                var entity = await Load(Server.Folders.GetByIdAsync(update.Id, ct), op);
                entity.ParentId = update.ParentId;
                entity.Name = update.Name;
                await Server.Folders.UpdateAsync(entity, ConcurrencyMode.Optimistic, ct);
                break;
            }

            case PinFolderOp pin:
            {
                var entity = await Load(Server.Folders.GetByIdAsync(pin.Id, ct), op);
                entity.PinnedItemId = pin.ItemId;
                await Server.Folders.UpdateAsync(entity, ConcurrencyMode.Optimistic, ct);
                break;
            }

            case DeleteFolderOp delete:
                await Server.Folders.DeleteAsync(delete.Id, ct);
                break;

            case AddLabelOp add:
                await InsertOnServerAsync(
                    Server.Labels,
                    new ModelLabelEntity
                    {
                        LabelCode = add.Code,
                        FolderId = add.FolderId,
                        Caption = add.Caption,
                    },
                    op,
                    ct
                );
                break;

            case UpdateLabelOp update:
            {
                var entity = await Load(Server.Labels.GetByIdAsync(update.Code, ct), op);
                entity.FolderId = update.FolderId;
                entity.Caption = update.Caption;
                await Server.Labels.UpdateAsync(entity, ConcurrencyMode.Optimistic, ct);
                break;
            }

            case DeleteLabelOp delete:
                await Server.Labels.DeleteAsync(delete.Code, ct);
                break;

            case AddItemOp add:
                await InsertOnServerAsync(
                    Server.Items,
                    new ModelItemEntity
                    {
                        ItemId = add.Id,
                        FolderId = add.FolderId,
                        LabelCode = add.LabelCode,
                        Code = add.Code,
                        Title = add.Title,
                        Payload = Convert.FromHexString(add.PayloadHex),
                    },
                    op,
                    ct
                );
                break;

            case UpdateItemOp update:
            {
                var entity = await Load(Server.Items.GetByIdAsync(update.Id, ct), op);
                entity.FolderId = update.FolderId;
                entity.LabelCode = update.LabelCode;
                entity.Code = update.Code;
                entity.Title = update.Title;
                await Server.Items.UpdateAsync(entity, ConcurrencyMode.Optimistic, ct);
                break;
            }

            case DeleteItemOp delete:
                await Server.Items.DeleteAsync(delete.Id, ct);
                break;

            case WritePayloadOp write:
            {
                var bytes = Convert.FromHexString(write.PayloadHex);
                using var source = new MemoryStream(bytes);

                if (
                    !await Server.ItemBlobs.WriteUnboundedBinaryAsync(
                        "Payload",
                        write.Id,
                        source,
                        bytes.Length,
                        ct
                    )
                )
                {
                    throw new InvalidOperationException($"No row to write: {op.Describe()}");
                }

                break;
            }

            case AddEventOp add:
                await InsertOnServerAsync(
                    Server.Events,
                    new ModelEventEntity
                    {
                        EventAt = add.At,
                        ItemId = add.ItemId,
                        Memo = add.Memo,
                    },
                    op,
                    ct
                );
                break;

            case UpdateEventOp update:
            {
                var entity = await Load(Server.Events.GetByIdAsync(update.At, ct), op);
                entity.ItemId = update.ItemId;
                entity.Memo = update.Memo;
                await Server.Events.UpdateAsync(entity, ConcurrencyMode.Optimistic, ct);
                break;
            }

            case DeleteEventOp delete:
                await Server.Events.DeleteAsync(delete.At, ct);
                break;

            // サーバーへ直接書く他者も、キーだけのスタブや持っていた実体をグラフ保存で消せる（サーバー役のリポジトリの SaveAsync）
            case DeleteStubOp stub:
                await DeleteStubAsync(
                    ModelRepositories.Of(Server),
                    stub.StubTable,
                    stub.StubKeyText,
                    ct
                );
                break;

            case DeleteHeldOp held:
            {
                if (!ServerHeld.TryGetValue((held.HeldTable, held.HeldKeyText), out var entity))
                {
                    throw new InvalidOperationException($"No held entity: {op.Describe()}");
                }

                entity.MarkRemoved();
                await SaveHeldAsync(ModelRepositories.Of(Server), held.HeldTable, entity, ct);
                break;
            }

            default:
                throw new ArgumentOutOfRangeException(nameof(op), op.Describe(), null);
        }
    }

    /// <summary>サーバーの他者が挿入したときの実体（表とキーで引く）。サーバーの他者の <see cref="DeleteHeldOp"/> が使う</summary>
    public Dictionary<(string Table, string Key), EntityBase> ServerHeld { get; } = [];

    private async Task InsertOnServerAsync<TEntity, TKey>(
        IRepository<TEntity, TKey> repository,
        TEntity entity,
        ModelWriteOp op,
        CancellationToken ct
    )
        where TEntity : EntityBase, new()
    {
        await repository.InsertAsync(entity, ct);
        ServerHeld[(op.Table, op.KeyText)] = entity;
    }

    private static async Task<T> Load<T>(Task<T?> load, ModelOp op)
        where T : class =>
        await load ?? throw new InvalidOperationException($"No row to change: {op.Describe()}");

    private static async Task AddAsync<TEntity, TKey>(
        SyncModelDevice device,
        IRepository<TEntity, TKey> repository,
        TEntity entity,
        ModelWriteOp op,
        CancellationToken ct
    )
        where TEntity : EntityBase, new()
    {
        if (op.Via == ModelWriteVia.Direct)
        {
            await repository.InsertAsync(entity, ct);
        }
        else
        {
            entity.MarkAdded();
            await repository.SaveAsync(entity, cancellationToken: ct);
        }

        // 挿入に使った実体を持っておく（キーを決めるフックが変えたキーではなく、操作のキーで引く）
        device.Held[(op.Table, op.KeyText)] = entity;
    }

    private static async Task UpdateAsync<TEntity, TKey>(
        IRepository<TEntity, TKey> repository,
        TEntity entity,
        ModelWriteVia via,
        CancellationToken ct
    )
        where TEntity : EntityBase, new()
    {
        if (via == ModelWriteVia.Direct)
        {
            await repository.UpdateAsync(entity, cancellationToken: ct);

            return;
        }

        entity.MarkUpdated();
        await repository.SaveAsync(entity, cancellationToken: ct);
    }

    private static async Task DeleteAsync<TEntity, TKey>(
        IRepository<TEntity, TKey> repository,
        TKey key,
        ModelOp op,
        CancellationToken ct
    )
        where TEntity : EntityBase, new()
    {
        if (((ModelWriteOp)op).Via == ModelWriteVia.Direct)
        {
            await repository.DeleteAsync(key, ct);

            return;
        }

        // グラフ保存の削除は読み込んだ実体で行う（キーだけのスタブは別の操作＝失敗の注入の段で扱う）
        var entity = await Load(repository.GetByIdAsync(key, ct), op);
        entity.MarkRemoved();
        await repository.SaveAsync(entity, cancellationToken: ct);
    }

    public async ValueTask DisposeAsync()
    {
        Device1.Dispose();
        Device2.Dispose();

        if (_http is not null)
        {
            await _http.DisposeAsync();
        }

        await Server.DisposeAsync();
    }
}

/// <summary>グラフ保存の削除に使う 4 つの表のリポジトリ（端末とサーバー役で共有する）</summary>
internal sealed record ModelRepositories(
    IRepository<ModelFolderEntity, int> Folders,
    IRepository<ModelLabelEntity, string> Labels,
    IRepository<ModelItemEntity, int> Items,
    IRepository<ModelEventEntity, DateTime> Events
)
{
    /// <summary>端末のリポジトリ</summary>
    public static ModelRepositories Of(SyncModelDevice device) =>
        new(device.Folders, device.Labels, device.Items, device.Events);

    /// <summary>サーバー役のリポジトリ</summary>
    public static ModelRepositories Of(ISyncModelServerRole server) =>
        new(server.Folders, server.Labels, server.Items, server.Events);
}
