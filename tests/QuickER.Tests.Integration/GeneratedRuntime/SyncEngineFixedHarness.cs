using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using QuickER.Tests.GeneratedSyncModelFixture;
using QuickER.Tests.GeneratedSyncModelFixture.Repositories.Sqlite;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// 新しいエンジンの決まった形の行（<see cref="SyncEngineFixedRows"/>）の端末とサーバー役。
/// </summary>
/// <remarks>
/// <para>
/// 端末は同期のモデルの新しいエンジンの端末（<see cref="SyncModelEngineDevice"/>）と同じ部品で組む：ローカルの一時 DB にモデルの図の
/// DDL を当て、同期する 4 表に <c>SyncLocalTracking.Ensure</c> でトリガーと状態の表を張り、開くたびの確認
/// （<c>SyncLocalTrackingGuard</c>）で包んだ接続ファクトリ（<c>ObservedSqlConnectionFactory</c>）から <c>SqliteLocalRowStore</c> と
/// リポジトリを作り、4 表の <c>TrackedSyncTable</c> で <c>TrackedSyncEngine</c> を組む。違いは 2 つ：接続文字列の
/// <c>Default Timeout</c> を 1 秒にする（ロック待ちで終わる行が長く待たない）、フォルダーのリポジトリに行ごとの Save フックの口
/// （<see cref="AfterFolderSave"/>）を持つ。
/// </para>
/// <para>
/// サーバー役は同期のモデルの SQLite の代役（<see cref="SyncModelServer"/>）。割り込みの点ごとの観測
/// （<see cref="SyncModelFaults.ObservePoint"/>）と割り込み（<see cref="SyncModelFaults.ArmInterrupt"/>）を使う行は、
/// 同期中の端末の印（<see cref="SyncModelFaults.Syncing"/>）を <see cref="SyncAsync"/> が立てる。
/// </para>
/// </remarks>
internal sealed class SyncEngineFixedHarness : IAsyncDisposable
{
    private readonly SqliteTempDatabase _db;
    private readonly LightJournalSyncModelConnectionFactory _plain;

    private SyncEngineFixedHarness(
        SqliteTempDatabase db,
        LightJournalSyncModelConnectionFactory plain,
        string connectionString,
        SyncModelFaults faults,
        SyncModelServer server
    )
    {
        _db = db;
        _plain = plain;
        ConnectionString = connectionString;
        Faults = faults;
        Server = server;
    }

    /// <summary>端末の DB の接続文字列（<c>Default Timeout</c> は 1 秒）</summary>
    public string ConnectionString { get; }

    /// <summary>サーバー役の失敗の注入・割り込み・観測</summary>
    public SyncModelFaults Faults { get; }

    /// <summary>サーバー役（SQLite の代役）</summary>
    public SyncModelServer Server { get; }

    /// <summary>開くたびの確認で包んだ端末の接続ファクトリ</summary>
    public ISqlConnectionFactory Factory { get; private set; } = null!;

    /// <summary>端末のローカルの行を読み書きする口</summary>
    public SqliteLocalRowStore Rows { get; private set; } = null!;

    /// <summary>端末のフォルダーのリポジトリ（Save フックの口 <see cref="AfterFolderSave"/> を持つ）</summary>
    public ModelFolderRepository Folders { get; private set; } = null!;

    /// <summary>端末の品目のリポジトリ</summary>
    public ModelItemRepository Items { get; private set; } = null!;

    /// <summary>端末のエンジン</summary>
    public TrackedSyncEngine Engine { get; private set; } = null!;

    /// <summary>フォルダーのグラフ保存の After フックで呼ぶ関数（null なら何もしない）</summary>
    public Func<ModelFolderEntity, CancellationToken, Task>? AfterFolderSave { get; set; }

    /// <summary>端末とサーバー役を作る</summary>
    public static async Task<SyncEngineFixedHarness> CreateAsync(CancellationToken ct)
    {
        var faults = new SyncModelFaults();
        var server = await SyncModelServer.CreateAsync(faults, ct);
        var db = SqliteTempDatabase.Create();

        try
        {
            await db.ApplyDdlAsync(SyncModelFixtureDefinition.BuildSqliteMirror(), ct);
            var connectionString = new SqliteConnectionStringBuilder(
                db.ReadWriteCreateConnectionString
            )
            {
                DefaultTimeout = 1,
            }.ToString();
            var plain = new LightJournalSyncModelConnectionFactory(connectionString);
            var harness = new SyncEngineFixedHarness(db, plain, connectionString, faults, server);

            await using (var connection = plain.CreateConnection())
            {
                await connection.OpenAsync(ct);
                _ = SyncLocalTracking.Ensure(connection, GeneratedSyncTables.All);
            }

            var guard = new SyncLocalTrackingGuard(GeneratedSyncTables.All);
            harness.Factory = new ObservedSqlConnectionFactory(plain, guard.OnStateChange);
            harness.Rows = new SqliteLocalRowStore(harness.Factory);
            var hooks = new SaveHookRegistry().Add(new FolderHook(harness));
            harness.Folders = new ModelFolderRepository(harness.Factory, hooks);
            harness.Items = new ModelItemRepository(harness.Factory);
            harness.Engine = harness.CreateEngine();

            return harness;
        }
        catch
        {
            db.Dispose();
            await server.DisposeAsync();
            throw;
        }
    }

    /// <summary>同じ端末の DB を使う、別のエンジンのインスタンスを作る</summary>
    public TrackedSyncEngine CreateEngine() =>
        new([
            new TrackedSyncTable<ModelFolderEntity, int>(
                GeneratedSyncTables.ModelFolder,
                Server.FolderSource,
                Rows
            ),
            new TrackedSyncTable<ModelLabelEntity, string>(
                GeneratedSyncTables.ModelLabel,
                Server.LabelSource,
                Rows
            ),
            new TrackedSyncTable<ModelItemEntity, int>(
                GeneratedSyncTables.ModelItem,
                Server.ItemSource,
                Rows
            ),
            new TrackedSyncTable<ModelEventEntity, DateTime>(
                GeneratedSyncTables.ModelEvent,
                Server.EventSource,
                Rows
            ),
        ]);

    /// <summary>同期を 1 回流す（同期中の端末の印を立てる。<paramref name="engine"/> が null なら端末のエンジン）</summary>
    public async Task<SyncResult> SyncAsync(
        SyncOptions? options,
        CancellationToken ct,
        TrackedSyncEngine? engine = null
    )
    {
        Faults.Syncing = ModelActor.Device1;

        try
        {
            return await (engine ?? Engine).SyncAsync(options, ct);
        }
        finally
        {
            Faults.Syncing = null;
        }
    }

    /// <summary>洗い替えを 1 回流す（同期中の端末の印を立てる）</summary>
    public async Task<SyncRefreshResult> RefreshAsync(
        SyncRefreshOptions? options,
        CancellationToken ct
    )
    {
        Faults.Syncing = ModelActor.Device1;
        Faults.Refreshing = true;

        try
        {
            return await Engine.RefreshAsync(options, ct);
        }
        finally
        {
            Faults.Syncing = null;
            Faults.Refreshing = false;
        }
    }

    /// <summary>端末の DB の内容</summary>
    public Task<ModelDbState> ReadLocalAsync(CancellationToken ct) =>
        ModelDbState.ReadAsync(ConnectionString, ct);

    /// <summary>サーバーの内容</summary>
    public Task<ModelDbState> ReadServerAsync(CancellationToken ct) => Server.ReadStateAsync(ct);

    /// <summary>端末の DB へ文を流す（包んでいない接続。トリガーは記録する＝利用者の書き込み）</summary>
    public async Task ExecuteLocalAsync(string sql, CancellationToken ct)
    {
        await using var connection = _plain.CreateConnection();
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// 端末の DB へ文を、状態の表の適用中の印を立てた間に流す（トリガーは記録しない＝アプリの外で表を作り直した形）
    /// </summary>
    public Task ExecuteUntrackedAsync(string sql, CancellationToken ct) =>
        ExecuteLocalAsync(
            $"UPDATE {SyncLocalTracking.StateTableName} SET applying = 1; {sql}; "
                + $"UPDATE {SyncLocalTracking.StateTableName} SET applying = 0",
            ct
        );

    /// <summary>端末の DB で 1 つの値を読む（文字列へ。NULL は null）</summary>
    public async Task<string?> ScalarAsync(string sql, CancellationToken ct)
    {
        await using var connection = _plain.CreateConnection();
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        var value = await command.ExecuteScalarAsync(ct);

        return value is null or DBNull
            ? null
            : Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>行の状態の表と再開点の表の全部（行を序数の順に並べた文字列。途中の状態が残っていないことを比べる）</summary>
    public async Task<IReadOnlyList<string>> ReadStateTablesAsync(CancellationToken ct)
    {
        var lines = new List<string>();

        await using var connection = _plain.CreateConnection();
        await connection.OpenAsync(ct);

        foreach (
            var table in new[]
            {
                SyncLocalTracking.RowTableName,
                SyncLocalTracking.StateTableName,
                "quicker_sync_table",
            }
        )
        {
            await using var command = connection.CreateCommand();
            command.CommandText = $"SELECT * FROM {table}";
            await using var reader = await command.ExecuteReaderAsync(ct);

            while (await reader.ReadAsync(ct))
            {
                var values = new string[reader.FieldCount];

                for (var i = 0; i < reader.FieldCount; i++)
                {
                    values[i] =
                        reader.IsDBNull(i) ? "NULL"
                        : reader.GetValue(i) is byte[] bytes ? Convert.ToHexString(bytes)
                        : Convert.ToString(
                            reader.GetValue(i),
                            System.Globalization.CultureInfo.InvariantCulture
                        ) ?? "";
                }

                lines.Add(table + "|" + string.Join("|", values));
            }
        }

        lines.Sort(StringComparer.Ordinal);

        return lines;
    }

    public async ValueTask DisposeAsync()
    {
        SyncModelPools.Clear(_plain);
        _db.Dispose();
        await Server.DisposeAsync();
    }

    /// <summary>フォルダーの Save フック（After で <see cref="AfterFolderSave"/> を呼ぶ）</summary>
    private sealed class FolderHook(SyncEngineFixedHarness harness) : ISaveHook<ModelFolderEntity>
    {
        public async Task AfterSaveAsync(
            ModelFolderEntity entity,
            SaveOperation operation,
            ISaveHookContext context,
            CancellationToken cancellationToken = default
        )
        {
            if (harness.AfterFolderSave is { } after)
            {
                await after(entity, cancellationToken);
            }
        }
    }
}
