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
/// （<see cref="BeforeFolderSave"/>・<see cref="AfterFolderSave"/>）を持つ。
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

    /// <summary>
    /// フォルダーのグラフ保存の Before フックで呼ぶ関数（null なら保存を続ける。false を返せばその行の保存を飛ばす）
    /// </summary>
    public Func<ModelFolderEntity, CancellationToken, Task<bool>>? BeforeFolderSave { get; set; }

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
    public TrackedSyncEngine CreateEngine() => CreateEngine(observe: null);

    /// <summary>
    /// 同じ端末の DB を使い、フォルダーと品目の 2 表だけで組んだエンジンを作る（ラベルとイベントの表はトリガーで記録されるが、
    /// このエンジンの表ではない）
    /// </summary>
    public TrackedSyncEngine CreateFolderAndItemEngine() =>
        new([
            new TrackedSyncTable<ModelFolderEntity, int>(
                GeneratedSyncTables.ModelFolder,
                Server.FolderSource,
                Rows
            ),
            new TrackedSyncTable<ModelItemEntity, int>(
                GeneratedSyncTables.ModelItem,
                Server.ItemSource,
                Rows
            ),
        ]);

    /// <summary>
    /// 同じ端末の DB を使い、サーバーの差分ソースへの行とキーの取得を <paramref name="observe"/> が観測する（呼び出しの前と、行が
    /// 返った後に呼ぶ。投げれば取得の失敗になる）エンジンのインスタンスを作る
    /// </summary>
    /// <param name="observe">取得の観測（null なら観測しない）</param>
    /// <param name="ceiling">
    /// 取得の上限を答える関数（null なら差分ソースの答え＝代役は上限なし）。上限を実行の始めに固定するサーバーの形
    /// （<see cref="SyncModelServer.TakeVersion"/> の版を返すと、その後にサーバーで書かれた行はその実行の取得に入らない）を作る
    /// </param>
    /// <param name="nonAdvancing">
    /// 進まない差分ソースの指定（null なら使わない）。指定した表の差分ソースだけを
    /// <see cref="NonAdvancingSyncServerSource{TEntity, TKey}"/> で包む
    /// </param>
    public TrackedSyncEngine CreateEngine(
        Func<SyncEngineSourceCall, Task>? observe,
        Func<Task<byte[]?>>? ceiling = null,
        NonAdvancingSource? nonAdvancing = null
    ) =>
        new([
            new TrackedSyncTable<ModelFolderEntity, int>(
                GeneratedSyncTables.ModelFolder,
                Observed(Server.FolderSource, ModelTables.Folders, observe, ceiling, nonAdvancing),
                Rows
            ),
            new TrackedSyncTable<ModelLabelEntity, string>(
                GeneratedSyncTables.ModelLabel,
                Observed(Server.LabelSource, ModelTables.Labels, observe, ceiling, nonAdvancing),
                Rows
            ),
            new TrackedSyncTable<ModelItemEntity, int>(
                GeneratedSyncTables.ModelItem,
                Observed(Server.ItemSource, ModelTables.Items, observe, ceiling, nonAdvancing),
                Rows
            ),
            new TrackedSyncTable<ModelEventEntity, DateTime>(
                GeneratedSyncTables.ModelEvent,
                Observed(Server.EventSource, ModelTables.Events, observe, ceiling, nonAdvancing),
                Rows
            ),
        ]);

    /// <summary>
    /// 進まない差分ソースの指定がその表なら進まない包みで包み、観測も上限の関数も無ければそのまま、あれば観測の包みで返す
    /// </summary>
    private static ISyncServerSource<TEntity, TKey> Observed<TEntity, TKey>(
        ISyncServerSource<TEntity, TKey> source,
        string table,
        Func<SyncEngineSourceCall, Task>? observe,
        Func<Task<byte[]?>>? ceiling,
        NonAdvancingSource? nonAdvancing
    )
        where TEntity : EntityBaseCore, new()
    {
        var inner =
            nonAdvancing is not null
            && string.Equals(nonAdvancing.Table, table, StringComparison.Ordinal)
                ? new NonAdvancingSyncServerSource<TEntity, TKey>(source, nonAdvancing)
                : source;

        return observe is null && ceiling is null
            ? inner
            : new ObservedSyncServerSource<TEntity, TKey>(
                inner,
                table,
                observe ?? (_ => Task.CompletedTask),
                ceiling
            );
    }

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

    /// <summary>洗い替えを 1 回流す（同期中の端末の印を立てる。<paramref name="engine"/> が null なら端末のエンジン）</summary>
    public async Task<SyncRefreshResult> RefreshAsync(
        SyncRefreshOptions? options,
        CancellationToken ct,
        TrackedSyncEngine? engine = null
    )
    {
        Faults.Syncing = ModelActor.Device1;
        Faults.Refreshing = true;

        try
        {
            return await (engine ?? Engine).RefreshAsync(options, ct);
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
    /// 端末の DB へ文を、記録しない書き込み（<c>SyncLocalTracking.WriteUntrackedAsync</c>）で流す（トリガーは記録しない＝アプリの
    /// 外で表を作り直した形）
    /// </summary>
    public async Task ExecuteUntrackedAsync(string sql, CancellationToken ct)
    {
        await using var connection = Rows.CreateDedicatedConnection();
        await connection.OpenAsync(ct);
        await SyncLocalTracking.WriteUntrackedAsync(
            connection,
            async (inside, token) =>
            {
                await using var command = inside.CreateCommand();
                command.CommandText = sql;
                await command.ExecuteNonQueryAsync(token);
            },
            ct
        );
    }

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

    /// <summary>
    /// 端末の DB で、文の実行計画（<c>EXPLAIN QUERY PLAN</c> の detail の列）を行ごとに読む。文は表の名前を
    /// <c>@table</c> で束縛できる
    /// </summary>
    /// <param name="sql">計画を読む文</param>
    /// <param name="tableName"><c>@table</c> に束縛する表の名前</param>
    /// <param name="ct">キャンセルトークン</param>
    public async Task<IReadOnlyList<string>> QueryPlanAsync(
        string sql,
        string tableName,
        CancellationToken ct
    )
    {
        var lines = new List<string>();
        await using var connection = _plain.CreateConnection();
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = "EXPLAIN QUERY PLAN " + sql;
        var table = command.CreateParameter();
        table.ParameterName = "@table";
        table.Value = tableName;
        command.Parameters.Add(table);
        await using var reader = await command.ExecuteReaderAsync(ct);

        while (await reader.ReadAsync(ct))
        {
            lines.Add(reader.GetString(reader.GetOrdinal("detail")));
        }

        return lines;
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

    /// <summary>
    /// フォルダーの Save フック（Before で <see cref="BeforeFolderSave"/>、After で <see cref="AfterFolderSave"/> を呼ぶ）
    /// </summary>
    private sealed class FolderHook(SyncEngineFixedHarness harness) : ISaveHook<ModelFolderEntity>
    {
        public Task<bool> BeforeSaveAsync(
            ModelFolderEntity entity,
            SaveOperation operation,
            CancellationToken cancellationToken = default
        ) =>
            harness.BeforeFolderSave is { } before
                ? before(entity, cancellationToken)
                : Task.FromResult(true);

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

/// <summary>サーバーの差分ソースへの取得 1 回の観測</summary>
/// <param name="Table">表の名前</param>
/// <param name="Call">取得の種類（<see cref="RowsBefore"/>・<see cref="RowsReturned"/>・<see cref="KeysBefore"/>）</param>
/// <param name="Rows">返った行の数（<see cref="RowsReturned"/> だけ。ほかは null）</param>
internal sealed record SyncEngineSourceCall(string Table, string Call, int? Rows)
{
    /// <summary>行のページの取得の前</summary>
    public const string RowsBefore = "rows";

    /// <summary>行のページが返った後（エンジンへ渡す前）</summary>
    public const string RowsReturned = "rows-returned";

    /// <summary>全キーの取得の前</summary>
    public const string KeysBefore = "keys";
}

/// <summary>
/// サーバーの差分ソースを包み、行のページとキーの取得を観測へ知らせる（ほかはそのまま転送する）。観測が投げれば、その取得の失敗
/// としてエンジンへ届く。上限の関数があれば、取得の上限はその答えにする
/// </summary>
/// <param name="inner">包む差分ソース</param>
/// <param name="table">表の名前</param>
/// <param name="observe">観測</param>
/// <param name="ceiling">取得の上限を答える関数（null なら包む差分ソースの答え）</param>
internal sealed class ObservedSyncServerSource<TEntity, TKey>(
    ISyncServerSource<TEntity, TKey> inner,
    string table,
    Func<SyncEngineSourceCall, Task> observe,
    Func<Task<byte[]?>>? ceiling = null
) : ISyncServerSource<TEntity, TKey>
    where TEntity : EntityBaseCore, new()
{
    public Task<byte[]?> GetChangeCeilingAsync(CancellationToken cancellationToken = default) =>
        ceiling is null ? inner.GetChangeCeilingAsync(cancellationToken) : ceiling();

    public async Task<SyncChangeBatch<TEntity>> GetChangesAsync(
        byte[]? anchor,
        byte[]? ceiling,
        int batchSize,
        CancellationToken cancellationToken = default
    )
    {
        await observe(new(table, SyncEngineSourceCall.RowsBefore, null));
        var batch = await inner.GetChangesAsync(anchor, ceiling, batchSize, cancellationToken);
        await observe(new(table, SyncEngineSourceCall.RowsReturned, batch.Rows.Count));

        return batch;
    }

    public async Task<SyncChangeBatch<TEntity>> GetFirstPageAsync(
        int batchSize,
        CancellationToken cancellationToken = default
    )
    {
        await observe(new(table, SyncEngineSourceCall.RowsBefore, null));
        var batch = await inner.GetFirstPageAsync(batchSize, cancellationToken);
        await observe(new(table, SyncEngineSourceCall.RowsReturned, batch.Rows.Count));

        return batch;
    }

    public async Task<SyncChangeBatch<TEntity>> GetPageAfterAsync(
        TKey afterKey,
        int batchSize,
        CancellationToken cancellationToken = default
    )
    {
        await observe(new(table, SyncEngineSourceCall.RowsBefore, null));
        var batch = await inner.GetPageAfterAsync(afterKey, batchSize, cancellationToken);
        await observe(new(table, SyncEngineSourceCall.RowsReturned, batch.Rows.Count));

        return batch;
    }

    public async Task<IReadOnlyList<TKey>> GetAllKeysAsync(
        CancellationToken cancellationToken = default
    )
    {
        await observe(new(table, SyncEngineSourceCall.KeysBefore, null));

        return await inner.GetAllKeysAsync(cancellationToken);
    }

    public IRemoteRepositoryCore<TEntity, TKey> Writer => inner.Writer;

    public ISyncBinaryColumns<TKey>? BinaryColumns => inner.BinaryColumns;
}

/// <summary>
/// 進まない差分ソースの指定：包む表の名前と、行の取得の回数（<see cref="NonAdvancingSyncServerSource{TEntity, TKey}"/> が数える）
/// </summary>
/// <param name="table">包む表の名前</param>
/// <param name="limit">
/// 行の取得の回数の上限。超えたら <see cref="NonAdvancingSourceLimitException"/> を投げる（エンジンの守りが無いと実行が
/// 終わらないので、守りを外した壊し方でも行が止まらずに赤で終わるようにする）
/// </param>
internal sealed class NonAdvancingSource(string table, int limit = 20)
{
    private int _calls;

    /// <summary>包む表の名前</summary>
    public string Table { get; } = table;

    /// <summary>行の取得の回数の上限</summary>
    public int Limit { get; } = limit;

    /// <summary>これまでの行の取得の回数</summary>
    public int Calls => Volatile.Read(ref _calls);

    /// <summary>行の取得を 1 回数え、上限を超えたらテスト側の例外を投げる</summary>
    public void Count()
    {
        var calls = Interlocked.Increment(ref _calls);

        if (calls > Limit)
        {
            throw new NonAdvancingSourceLimitException(Table, calls);
        }
    }
}

/// <summary>進まない差分ソースの行の取得が回数の上限を超えた（エンジンが進まない差分ソースで止まらなかった）</summary>
internal sealed class NonAdvancingSourceLimitException(string table, int calls)
    : Exception($"The non-advancing source of '{table}' was asked for rows {calls} times.");

/// <summary>
/// 進まない差分ソース：行の取得の最初の答え（版ありは <c>GetChangesAsync</c>、版なしは <c>GetFirstPageAsync</c>）を包む差分
/// ソースから取って覚え、以後のどの行の取得にも同じ行を「まだ続きがある」（<c>HasMore</c> が真）で返す。行の取得は
/// <see cref="NonAdvancingSource"/> で数える。ほかはそのまま転送する
/// </summary>
/// <param name="inner">包む差分ソース</param>
/// <param name="spec">進まない差分ソースの指定</param>
internal sealed class NonAdvancingSyncServerSource<TEntity, TKey>(
    ISyncServerSource<TEntity, TKey> inner,
    NonAdvancingSource spec
) : ISyncServerSource<TEntity, TKey>
    where TEntity : EntityBaseCore, new()
{
    private IReadOnlyList<TEntity>? _rows;

    public Task<byte[]?> GetChangeCeilingAsync(CancellationToken cancellationToken = default) =>
        inner.GetChangeCeilingAsync(cancellationToken);

    public async Task<SyncChangeBatch<TEntity>> GetChangesAsync(
        byte[]? anchor,
        byte[]? ceiling,
        int batchSize,
        CancellationToken cancellationToken = default
    )
    {
        spec.Count();
        _rows ??= (await inner.GetChangesAsync(anchor, ceiling, batchSize, cancellationToken)).Rows;

        return new SyncChangeBatch<TEntity>(_rows, HasMore: true);
    }

    public async Task<SyncChangeBatch<TEntity>> GetFirstPageAsync(
        int batchSize,
        CancellationToken cancellationToken = default
    )
    {
        spec.Count();
        _rows ??= (await inner.GetFirstPageAsync(batchSize, cancellationToken)).Rows;

        return new SyncChangeBatch<TEntity>(_rows, HasMore: true);
    }

    public async Task<SyncChangeBatch<TEntity>> GetPageAfterAsync(
        TKey afterKey,
        int batchSize,
        CancellationToken cancellationToken = default
    )
    {
        spec.Count();
        _rows ??= (await inner.GetFirstPageAsync(batchSize, cancellationToken)).Rows;

        return new SyncChangeBatch<TEntity>(_rows, HasMore: true);
    }

    public Task<IReadOnlyList<TKey>> GetAllKeysAsync(
        CancellationToken cancellationToken = default
    ) => inner.GetAllKeysAsync(cancellationToken);

    public IRemoteRepositoryCore<TEntity, TKey> Writer => inner.Writer;

    public ISyncBinaryColumns<TKey>? BinaryColumns => inner.BinaryColumns;
}
