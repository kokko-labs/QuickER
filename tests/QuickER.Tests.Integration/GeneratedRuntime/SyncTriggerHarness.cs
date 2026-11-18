using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using QuickER.Model;
using QuickER.Provider.Sqlite;
using M = QuickER.Tests.GeneratedSyncModelFixture;
using MS = QuickER.Tests.GeneratedSyncModelFixture.Repositories.Sqlite;
using S = QuickER.Tests.GeneratedSyncFixture;
using SS = QuickER.Tests.GeneratedSyncFixture.Repositories.Sqlite;
using V = QuickER.Tests.GeneratedSyncVoFixture;
using VS = QuickER.Tests.GeneratedSyncVoFixture.Repositories.Sqlite;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>同期のトリガーの記録の表が書き込む先のフィクスチャ</summary>
internal enum SyncTriggerFixture
{
    /// <summary>同期のモデルの図（int・文字列・datetime2 のキー、版なしの表、NOT NULL の除外 blob）</summary>
    SyncModel,

    /// <summary>同期のパリティの図（NULL 許容の除外 blob、版なしの子の表）</summary>
    Sync,

    /// <summary>値オブジェクトの図（値オブジェクトのキー、NOT NULL の除外 blob）</summary>
    SyncVo,
}

/// <summary>状態の表（<c>quicker_sync_state</c>）の 1 行</summary>
/// <param name="Format">形式の版</param>
/// <param name="DbId">DB の識別子</param>
/// <param name="Applying">適用中の印</param>
/// <param name="NextSeq">次の変更の番号</param>
internal sealed record SyncTriggerState(long Format, string DbId, long Applying, long NextSeq);

/// <summary>行の状態の表（<c>quicker_sync_row</c>）の 1 行</summary>
/// <param name="Table">表の名前</param>
/// <param name="KeyType">キーの格納の型（SQLite の <c>typeof</c>）</param>
/// <param name="KeyText">キーの値のテキスト</param>
/// <param name="Dirty">送る印</param>
/// <param name="Deleted">削除の印</param>
/// <param name="BlobColumns">書いた除外 blob の列（アクセサの名前の <c>,</c> 区切り）</param>
/// <param name="ChangeSeq">変更の番号</param>
internal sealed record SyncTriggerRowState(
    string Table,
    string KeyType,
    string KeyText,
    long Dirty,
    long Deleted,
    string BlobColumns,
    long ChangeSeq
);

/// <summary>同期のモデルの図の部品（記録デコレータで包まない素の生成 Repository）</summary>
/// <param name="Folders">フォルダー</param>
/// <param name="Labels">ラベル（版なし）</param>
/// <param name="Items">品目（NOT NULL の除外 blob <c>payload</c>）</param>
/// <param name="Events">出来事（datetime2 のキー）</param>
/// <param name="Sql">生 SQL の実行器</param>
/// <param name="ItemHook">品目の Save フック（行が保存の後の処理を差し込む）</param>
internal sealed record SyncTriggerModelParts(
    MS.ModelFolderRepository Folders,
    MS.ModelLabelRepository Labels,
    MS.ModelItemRepository Items,
    MS.ModelEventRepository Events,
    MS.SqlExecutor Sql,
    SyncTriggerModelItemHook ItemHook
);

/// <summary>同期のパリティの図の部品</summary>
/// <param name="Orders">注文（NULL 許容の除外 blob <c>attachment</c>）</param>
/// <param name="Lines">注文の明細</param>
/// <param name="Notes">注文のメモ（版なし）</param>
/// <param name="Sql">生 SQL の実行器</param>
internal sealed record SyncTriggerSyncParts(
    SS.SyncOrderRepository Orders,
    SS.SyncOrderLineRepository Lines,
    SS.SyncNoteRepository Notes,
    SS.SqlExecutor Sql
);

/// <summary>値オブジェクトの図の部品</summary>
/// <param name="Orders">注文（値オブジェクトのキー・NOT NULL の除外 blob <c>photo</c>）</param>
/// <param name="Lines">注文の明細</param>
/// <param name="Sql">生 SQL の実行器</param>
internal sealed record SyncTriggerVoParts(
    VS.SyncvoOrderRepository Orders,
    VS.SyncvoLineRepository Lines,
    VS.SqlExecutor Sql
);

/// <summary>品目の Save フック（保存の後の処理を行が差し込む。差し込まなければ何もしない）</summary>
internal sealed class SyncTriggerModelItemHook : M.ISaveHook<M.ModelItemEntity>
{
    /// <summary>保存の後の処理（null なら何もしない）</summary>
    public Func<
        M.ModelItemEntity,
        M.SaveOperation,
        M.ISaveHookContext,
        CancellationToken,
        Task
    >? After { get; set; }

    public Task AfterSaveAsync(
        M.ModelItemEntity entity,
        M.SaveOperation operation,
        M.ISaveHookContext context,
        CancellationToken cancellationToken = default
    ) =>
        After is { } after
            ? after(entity, operation, context, cancellationToken)
            : Task.CompletedTask;
}

/// <summary>
/// 同期のトリガーの記録の端末：一時の SQLite の DB に図の DDL を当て、素の生成 Repository を組み、
/// <c>SyncLocalTracking.Ensure</c> でトリガーを張り、状態の表を読む
/// </summary>
/// <remarks>
/// 接続はテストの DB の開き方の約束（<see cref="SqliteLightJournal"/>）で開く。Repository は記録デコレータで包まない
/// （記録はトリガーだけが行う）。フィクスチャの生成物は名前空間ごとに別の型なので、トリガーの部品とキーの列はフィクスチャで
/// 振り分ける。
/// </remarks>
internal sealed class SyncTriggerHarness : IDisposable
{
    private readonly SqliteTempDatabase _db;
    private readonly Func<SqliteConnection> _connect;
    private readonly SyncTriggerModelParts? _model;
    private readonly SyncTriggerSyncParts? _sync;
    private readonly SyncTriggerVoParts? _vo;
    private readonly List<string> _extraConnectionStrings = [];

    private SyncTriggerHarness(
        SyncTriggerFixture fixture,
        SqliteTempDatabase db,
        string ddl,
        Func<SqliteConnection> connect,
        SyncTriggerModelParts? model,
        SyncTriggerSyncParts? sync,
        SyncTriggerVoParts? vo
    )
    {
        Fixture = fixture;
        _db = db;
        Ddl = ddl;
        _connect = connect;
        _model = model;
        _sync = sync;
        _vo = vo;
    }

    /// <summary>端末の DB に当てた図の DDL</summary>
    public string Ddl { get; }

    /// <summary>端末の DB のファイルのパス</summary>
    public string DatabasePath => _db.FilePath;

    /// <summary>Repository が使う接続文字列（生成の接続ファクトリへ渡す前の形）</summary>
    public string ConnectionString => _db.ReadWriteCreateConnectionString;

    /// <summary>フィクスチャ</summary>
    public SyncTriggerFixture Fixture { get; }

    /// <summary>同期のモデルの図の部品</summary>
    public SyncTriggerModelParts Model =>
        _model
        ?? throw new InvalidOperationException($"{Fixture} の端末に同期のモデルの部品は無い");

    /// <summary>同期のパリティの図の部品</summary>
    public SyncTriggerSyncParts Sync =>
        _sync
        ?? throw new InvalidOperationException($"{Fixture} の端末に同期のパリティの部品は無い");

    /// <summary>値オブジェクトの図の部品</summary>
    public SyncTriggerVoParts Vo =>
        _vo ?? throw new InvalidOperationException($"{Fixture} の端末に値オブジェクトの部品は無い");

    /// <summary>フィクスチャの同期する表の名前（外部キーの順）</summary>
    public IReadOnlyList<string> AllTableNames =>
        Fixture switch
        {
            SyncTriggerFixture.SyncModel => M
                .GeneratedSyncTables.All.Select(t => t.TableName)
                .ToList(),
            SyncTriggerFixture.Sync => S.GeneratedSyncTables.All.Select(t => t.TableName).ToList(),
            _ => V.GeneratedSyncTables.All.Select(t => t.TableName).ToList(),
        };

    /// <summary>端末を作る（図の DDL を当て、素の Repository を組む）</summary>
    /// <param name="fixture">フィクスチャ</param>
    /// <param name="alterDiagram">DDL を作る前に SQLite 方言の図の写しを変える処理（null なら変えない）</param>
    /// <param name="ct">キャンセルトークン</param>
    public static async Task<SyncTriggerHarness> CreateAsync(
        SyncTriggerFixture fixture,
        Action<ErDiagram>? alterDiagram,
        CancellationToken ct
    )
    {
        var db = SqliteTempDatabase.Create();

        try
        {
            var diagram = fixture switch
            {
                SyncTriggerFixture.SyncModel => M.SyncModelFixtureDefinition.BuildSqliteMirror(),
                SyncTriggerFixture.Sync => S.SyncFixtureDefinition.BuildSqliteMirror(),
                _ => V.SyncVoFixtureDefinition.BuildSqliteMirror(),
            };

            alterDiagram?.Invoke(diagram);
            var ddl = new SqliteDdlGenerator().Build(diagram);
            await db.ApplyDdlAsync(ddl, ct);

            var connectionString = db.ReadWriteCreateConnectionString;

            switch (fixture)
            {
                case SyncTriggerFixture.SyncModel:
                {
                    var factory = new LightJournalSyncModelConnectionFactory(connectionString);
                    var hook = new SyncTriggerModelItemHook();
                    var hooks = new M.SaveHookRegistry().Add(hook);
                    var model = new SyncTriggerModelParts(
                        new MS.ModelFolderRepository(factory, hooks),
                        new MS.ModelLabelRepository(factory, hooks),
                        new MS.ModelItemRepository(factory, hooks),
                        new MS.ModelEventRepository(factory, hooks),
                        new MS.SqlExecutor(factory),
                        hook
                    );

                    return new(fixture, db, ddl, factory.CreateConnection, model, null, null);
                }

                case SyncTriggerFixture.Sync:
                {
                    var factory = new LightJournalSyncConnectionFactory(connectionString);
                    var sync = new SyncTriggerSyncParts(
                        new SS.SyncOrderRepository(factory),
                        new SS.SyncOrderLineRepository(factory),
                        new SS.SyncNoteRepository(factory),
                        new SS.SqlExecutor(factory)
                    );

                    return new(fixture, db, ddl, factory.CreateConnection, null, sync, null);
                }

                default:
                {
                    var factory = new LightJournalSyncVoConnectionFactory(connectionString);
                    var vo = new SyncTriggerVoParts(
                        new VS.SyncvoOrderRepository(factory),
                        new VS.SyncvoLineRepository(factory),
                        new VS.SqlExecutor(factory)
                    );

                    return new(fixture, db, ddl, factory.CreateConnection, null, null, vo);
                }
            }
        }
        catch
        {
            db.Dispose();
            throw;
        }
    }

    /// <summary>開いた接続を返す（Repository と同じ接続ファクトリから作る。呼び出し側で破棄する）</summary>
    /// <param name="ct">キャンセルトークン</param>
    public async Task<SqliteConnection> OpenAsync(CancellationToken ct)
    {
        var connection = _connect();
        await connection.OpenAsync(ct);

        return connection;
    }

    /// <summary>名前の表のトリガーを張る（<c>SyncLocalTracking.Ensure</c>）</summary>
    /// <param name="tableNames">同期する表の名前（記述子の <c>TableName</c>）</param>
    /// <returns>トリガーを作った・作り直した表の名前</returns>
    public IReadOnlyList<string> Ensure(IReadOnlyList<string> tableNames)
    {
        using var connection = _connect();
        connection.Open();

        return Fixture switch
        {
            SyncTriggerFixture.SyncModel => M.SyncLocalTracking.Ensure(
                connection,
                Pick(M.GeneratedSyncTables.All, t => t.TableName, tableNames)
            ),
            SyncTriggerFixture.Sync => S.SyncLocalTracking.Ensure(
                connection,
                Pick(S.GeneratedSyncTables.All, t => t.TableName, tableNames)
            ),
            _ => V.SyncLocalTracking.Ensure(
                connection,
                Pick(V.GeneratedSyncTables.All, t => t.TableName, tableNames)
            ),
        };
    }

    /// <summary>表に張るはずのトリガーの名前（<c>SyncLocalTracking.TriggersFor</c>）</summary>
    /// <param name="tableName">表の名前</param>
    public IReadOnlyList<string> ExpectedTriggerNames(string tableName) =>
        Fixture switch
        {
            SyncTriggerFixture.SyncModel => M
                .SyncLocalTracking.TriggersFor(
                    One(M.GeneratedSyncTables.All, t => t.TableName, tableName)
                )
                .Select(t => t.Name)
                .ToList(),
            SyncTriggerFixture.Sync => S
                .SyncLocalTracking.TriggersFor(
                    One(S.GeneratedSyncTables.All, t => t.TableName, tableName)
                )
                .Select(t => t.Name)
                .ToList(),
            _ => V
                .SyncLocalTracking.TriggersFor(
                    One(V.GeneratedSyncTables.All, t => t.TableName, tableName)
                )
                .Select(t => t.Name)
                .ToList(),
        };

    /// <summary>表の主キーの列の名前（記述子の <c>LocalKeyColumnName</c>）</summary>
    /// <param name="tableName">表の名前</param>
    public string KeyColumn(string tableName) =>
        Fixture switch
        {
            SyncTriggerFixture.SyncModel => One(
                M.GeneratedSyncTables.All,
                t => t.TableName,
                tableName
            ).LocalKeyColumnName,
            SyncTriggerFixture.Sync => One(
                S.GeneratedSyncTables.All,
                t => t.TableName,
                tableName
            ).LocalKeyColumnName,
            _ => One(V.GeneratedSyncTables.All, t => t.TableName, tableName).LocalKeyColumnName,
        };

    /// <summary>表に今あるトリガーの名前（名前の順）</summary>
    /// <param name="tableName">表の名前</param>
    /// <param name="ct">キャンセルトークン</param>
    public async Task<IReadOnlyList<string>> ReadTriggerNamesAsync(
        string tableName,
        CancellationToken ct
    )
    {
        await using var connection = await OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT name FROM sqlite_master WHERE type = 'trigger' AND tbl_name = @table ORDER BY name";
        command.Parameters.AddWithValue("@table", tableName);

        var names = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(ct);

        while (await reader.ReadAsync(ct))
        {
            names.Add(reader.GetString(0));
        }

        return names;
    }

    /// <summary>状態の表の行を読む（1 行でなければ例外）</summary>
    /// <param name="ct">キャンセルトークン</param>
    public async Task<SyncTriggerState> ReadStateAsync(CancellationToken ct)
    {
        await using var connection = await OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT format, db_id, applying, next_seq FROM quicker_sync_state";

        var states = new List<SyncTriggerState>();
        await using var reader = await command.ExecuteReaderAsync(ct);

        while (await reader.ReadAsync(ct))
        {
            states.Add(
                new SyncTriggerState(
                    reader.GetInt64(0),
                    reader.GetString(1),
                    reader.GetInt64(2),
                    reader.GetInt64(3)
                )
            );
        }

        return states.Count == 1
            ? states[0]
            : throw new InvalidOperationException(
                $"状態の表の行が 1 行でない（{states.Count.ToString(CultureInfo.InvariantCulture)} 行）"
            );
    }

    /// <summary>行の状態の表を変更の番号の順に読む</summary>
    /// <param name="ct">キャンセルトークン</param>
    public async Task<IReadOnlyList<SyncTriggerRowState>> ReadRowsAsync(CancellationToken ct)
    {
        await using var connection = await OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT table_name, \"key\", dirty, deleted, blob_columns, change_seq FROM quicker_sync_row "
            + "ORDER BY change_seq";

        var rows = new List<SyncTriggerRowState>();
        await using var reader = await command.ExecuteReaderAsync(ct);

        while (await reader.ReadAsync(ct))
        {
            var (keyType, keyText) = Describe(reader.GetValue(1));
            rows.Add(
                new SyncTriggerRowState(
                    reader.GetString(0),
                    keyType,
                    keyText,
                    reader.GetInt64(2),
                    reader.GetInt64(3),
                    reader.GetString(4),
                    reader.GetInt64(5)
                )
            );
        }

        return rows;
    }

    /// <summary>
    /// 行の状態の表のエンジンだけが書く列（<c>base_version</c>・<c>known</c>・<c>sw_seq</c>・<c>blob_owed</c>）を、表とキー
    /// （型とテキスト）で引けるテキスト（列ごとに格納の型とテキスト）にして読む
    /// </summary>
    /// <param name="ct">キャンセルトークン</param>
    public async Task<
        IReadOnlyDictionary<(string Table, string KeyType, string KeyText), string>
    > ReadEngineColumnsAsync(CancellationToken ct)
    {
        var columns = new Dictionary<(string, string, string), string>();
        await using var connection = await OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT table_name, \"key\", base_version, known, sw_seq, blob_owed FROM quicker_sync_row";
        await using var reader = await command.ExecuteReaderAsync(ct);

        while (await reader.ReadAsync(ct))
        {
            var (keyType, keyText) = Describe(reader.GetValue(1));
            var text = new StringBuilder();

            for (var i = 2; i < reader.FieldCount; i++)
            {
                var (type, value) = Describe(reader.GetValue(i));
                text.Append(reader.GetName(i))
                    .Append('=')
                    .Append(type)
                    .Append(':')
                    .Append(value)
                    .Append(';');
            }

            columns[(reader.GetString(0), keyType, keyText)] = text.ToString();
        }

        return columns;
    }

    /// <summary>行の状態の表のどの行にも、エンジンだけが書く列の同じ値（既定と違う値）を入れる</summary>
    /// <param name="ct">キャンセルトークン</param>
    public async Task SetEngineColumnsAsync(CancellationToken ct)
    {
        await using var connection = await OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText =
            "UPDATE quicker_sync_row SET base_version = x'0A0B0C', known = 1, sw_seq = 42, blob_owed = 'Payload'";
        await command.ExecuteNonQueryAsync(ct);
    }

    /// <summary>行の状態の表の 1 行のキーで、元の表の行を数える（キーの比較は SQLite の <c>=</c>）</summary>
    /// <param name="tableName">表の名前</param>
    /// <param name="changeSeq">行の状態の表の変更の番号（行を 1 つに決める）</param>
    /// <param name="ct">キャンセルトークン</param>
    public async Task<long> CountRowsByRecordedKeyAsync(
        string tableName,
        long changeSeq,
        CancellationToken ct
    )
    {
        await using var connection = await OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"SELECT count(*) FROM {Quote(tableName)} AS s WHERE s.{Quote(KeyColumn(tableName))} = "
            + "(SELECT r.\"key\" FROM quicker_sync_row AS r WHERE r.table_name = @table AND r.change_seq = @seq)";
        command.Parameters.AddWithValue("@table", tableName);
        command.Parameters.AddWithValue("@seq", changeSeq);

        return Convert.ToInt64(await command.ExecuteScalarAsync(ct), CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// フィクスチャの同期する表の行を、表とキー（型とテキスト）で引ける内容のテキストにして読む
    /// （手の前後で比べ、触っていない行を見分ける）
    /// </summary>
    /// <param name="ct">キャンセルトークン</param>
    public async Task<
        IReadOnlyDictionary<(string Table, string KeyType, string KeyText), string>
    > ReadContentsAsync(CancellationToken ct)
    {
        var contents = new Dictionary<(string, string, string), string>();
        await using var connection = await OpenAsync(ct);

        foreach (var table in AllTableNames)
        {
            var keyColumn = KeyColumn(table);
            await using var command = connection.CreateCommand();
            command.CommandText = $"SELECT * FROM {Quote(table)}";
            await using var reader = await command.ExecuteReaderAsync(ct);
            var keyOrdinal = reader.GetOrdinal(keyColumn);

            while (await reader.ReadAsync(ct))
            {
                var (keyType, keyText) = Describe(reader.GetValue(keyOrdinal));
                var text = new StringBuilder();

                for (var i = 0; i < reader.FieldCount; i++)
                {
                    var (type, value) = Describe(reader.GetValue(i));
                    text.Append(reader.GetName(i))
                        .Append('=')
                        .Append(type)
                        .Append(':')
                        .Append(value)
                        .Append(';');
                }

                contents[(table, keyType, keyText)] = text.ToString();
            }
        }

        return contents;
    }

    /// <summary>
    /// 値を SQLite の格納の型の名前とテキストにする（整数・実数は不変のカルチャ、テキストはそのまま、blob は 16 進）
    /// </summary>
    /// <param name="value">リーダーが返した値</param>
    public static (string Type, string Text) Describe(object value) =>
        value switch
        {
            DBNull => ("null", ""),
            long number => ("integer", number.ToString(CultureInfo.InvariantCulture)),
            double real => ("real", real.ToString("R", CultureInfo.InvariantCulture)),
            string text => ("text", text),
            byte[] bytes => ("blob", Convert.ToHexString(bytes)),
            _ => throw new InvalidOperationException($"知らない格納の値の型 {value.GetType()}"),
        };

    /// <summary>識別子を引用する</summary>
    private static string Quote(string identifier) =>
        "\"" + identifier.Replace("\"", "\"\"") + "\"";

    /// <summary>名前の順に記述子を選ぶ（無い名前は例外）</summary>
    private static List<T> Pick<T>(
        IReadOnlyList<T> all,
        Func<T, string> nameOf,
        IReadOnlyList<string> names
    ) => names.Select(name => One(all, nameOf, name)).ToList();

    /// <summary>名前の記述子を 1 つ選ぶ（無い名前は例外）</summary>
    private static T One<T>(IReadOnlyList<T> all, Func<T, string> nameOf, string name) =>
        all.SingleOrDefault(t => string.Equals(nameOf(t), name, StringComparison.Ordinal))
        ?? throw new InvalidOperationException($"同期する表 {name} が無い");

    /// <summary>
    /// 確認のガード（<c>SyncLocalTrackingGuard</c>）を作り、接続ファクトリを包んだ組を返す
    /// </summary>
    /// <param name="tables">ガードが見る表の名前（null ならフィクスチャの同期する表の全部）</param>
    /// <param name="inner">包む内側の接続ファクトリ</param>
    /// <param name="connectionString">内側の接続ファクトリへ渡す接続文字列（null なら端末の DB の接続文字列）</param>
    public SyncTriggerGuardProbe CreateGuard(
        IReadOnlyList<string>? tables = null,
        SyncTriggerGuardInner inner = SyncTriggerGuardInner.Generated,
        string? connectionString = null
    )
    {
        var names = tables ?? AllTableNames;
        var primary = connectionString ?? ConnectionString;

        if (connectionString is not null)
        {
            _extraConnectionStrings.Add(connectionString);
        }

        if (inner == SyncTriggerGuardInner.Custom && Fixture != SyncTriggerFixture.SyncModel)
        {
            throw new InvalidOperationException(
                $"自前の接続ファクトリは同期のモデルの図にだけある（{Fixture}）"
            );
        }

        switch (Fixture)
        {
            case SyncTriggerFixture.SyncModel:
            {
                var guard = new M.SyncLocalTrackingGuard(
                    Pick(M.GeneratedSyncTables.All, t => t.TableName, names)
                );

                MS.ObservedSqlConnectionFactory Wrap(string cs, StateChangeEventHandler handler) =>
                    new(
                        inner == SyncTriggerGuardInner.Custom
                            ? new SyncTriggerRawConnectionFactory(cs)
                            : new LightJournalSyncModelConnectionFactory(cs),
                        handler
                    );

                var folders = new MS.ModelFolderRepository(Wrap(primary, guard.OnStateChange));

                return new SyncTriggerGuardProbe(
                    names,
                    () => guard.FullCheckCount,
                    () => guard.RepairCount,
                    guard.OnStateChange,
                    (cs, handler) => Wrap(cs, handler).CreateConnection,
                    primary,
                    async (key, ct) =>
                    {
                        await folders.InsertAsync(
                            new M.ModelFolderEntity
                            {
                                FolderId = key,
                                Name = "guard-" + key.ToString(CultureInfo.InvariantCulture),
                            },
                            ct
                        );

                        return "model_folders";
                    }
                );
            }

            case SyncTriggerFixture.Sync:
            {
                var guard = new S.SyncLocalTrackingGuard(
                    Pick(S.GeneratedSyncTables.All, t => t.TableName, names)
                );

                SS.ObservedSqlConnectionFactory Wrap(string cs, StateChangeEventHandler handler) =>
                    new(new LightJournalSyncConnectionFactory(cs), handler);

                var orders = new SS.SyncOrderRepository(Wrap(primary, guard.OnStateChange));

                return new SyncTriggerGuardProbe(
                    names,
                    () => guard.FullCheckCount,
                    () => guard.RepairCount,
                    guard.OnStateChange,
                    (cs, handler) => Wrap(cs, handler).CreateConnection,
                    primary,
                    async (key, ct) =>
                    {
                        await orders.InsertAsync(
                            new S.SyncOrderEntity
                            {
                                OrderId = key,
                                CustomerName =
                                    "guard-" + key.ToString(CultureInfo.InvariantCulture),
                            },
                            ct
                        );

                        return "sync_orders";
                    }
                );
            }

            default:
            {
                var guard = new V.SyncLocalTrackingGuard(
                    Pick(V.GeneratedSyncTables.All, t => t.TableName, names)
                );

                VS.ObservedSqlConnectionFactory Wrap(string cs, StateChangeEventHandler handler) =>
                    new(new LightJournalSyncVoConnectionFactory(cs), handler);

                var orders = new VS.SyncvoOrderRepository(Wrap(primary, guard.OnStateChange));

                return new SyncTriggerGuardProbe(
                    names,
                    () => guard.FullCheckCount,
                    () => guard.RepairCount,
                    guard.OnStateChange,
                    (cs, handler) => Wrap(cs, handler).CreateConnection,
                    primary,
                    async (key, ct) =>
                    {
                        await orders.InsertAsync(
                            new V.SyncvoOrderEntity
                            {
                                OrderId = V.OrderIdValue.Create(key),
                                Title = V.TitleValue.Create(
                                    "guard-" + key.ToString(CultureInfo.InvariantCulture)
                                ),
                                Photo = V.PhotoValue.Create([1, 2, 3]),
                            },
                            ct
                        );

                        return "syncvo_orders";
                    }
                );
            }
        }
    }

    /// <summary>
    /// 包んでいない接続（素の <see cref="SqliteConnection"/>・外部キーの検査なし・既定のジャーナル）を開いて返す
    /// （呼び出し側で破棄する）
    /// </summary>
    /// <param name="ct">キャンセルトークン</param>
    public async Task<SqliteConnection> OpenOutsideAsync(CancellationToken ct)
    {
        var connection = new SqliteConnection(_db.ReadWriteCreateConnectionString);
        await connection.OpenAsync(ct);

        return connection;
    }

    /// <summary>包んでいない接続で文を流す（複数文可）</summary>
    /// <param name="sql">文</param>
    /// <param name="ct">キャンセルトークン</param>
    public async Task ExecuteOutsideAsync(string sql, CancellationToken ct)
    {
        await using var connection = await OpenOutsideAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(ct);
    }

    /// <summary>包んでいない接続で 1 つの値を読む</summary>
    /// <param name="sql">文</param>
    /// <param name="ct">キャンセルトークン</param>
    public async Task<object?> ScalarOutsideAsync(string sql, CancellationToken ct)
    {
        await using var connection = await OpenOutsideAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;

        return await command.ExecuteScalarAsync(ct);
    }

    /// <summary>DB のスキーマ（<c>sqlite_master</c> の全行を <c>type|name|tbl_name|sql</c> にして名前の順）を読む</summary>
    /// <param name="ct">キャンセルトークン</param>
    public async Task<IReadOnlyList<string>> ReadSchemaAsync(CancellationToken ct)
    {
        await using var connection = await OpenOutsideAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT type, name, tbl_name, sql FROM sqlite_master ORDER BY type, name";

        var rows = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(ct);

        while (await reader.ReadAsync(ct))
        {
            rows.Add(
                string.Join(
                    "|",
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.IsDBNull(3) ? "" : reader.GetString(3)
                )
            );
        }

        return rows;
    }

    /// <summary>表に今あるトリガー（名前 → 文）を、包んでいない接続で読む</summary>
    /// <param name="tableName">表の名前（<c>sqlite_master.tbl_name</c> と序数で比べる）</param>
    /// <param name="ct">キャンセルトークン</param>
    public async Task<IReadOnlyDictionary<string, string>> ReadTriggerTextsAsync(
        string tableName,
        CancellationToken ct
    )
    {
        await using var connection = await OpenOutsideAsync(ct);

        return ReadTriggerTexts(connection, tableName);
    }

    /// <summary>表に今あるトリガー（名前 → 文）を、開いた接続で読む</summary>
    /// <param name="connection">開いた接続</param>
    /// <param name="tableName">表の名前（<c>sqlite_master.tbl_name</c> と序数で比べる）</param>
    public static IReadOnlyDictionary<string, string> ReadTriggerTexts(
        SqliteConnection connection,
        string tableName
    )
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT name, sql FROM sqlite_master WHERE type = 'trigger' AND tbl_name = @table";
        command.Parameters.AddWithValue("@table", tableName);

        var triggers = new Dictionary<string, string>(StringComparer.Ordinal);
        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            triggers.Add(reader.GetString(0), reader.GetString(1));
        }

        return triggers;
    }

    /// <summary>表が DB にあるか（名前は序数で比べる）</summary>
    /// <param name="tableName">表の名前</param>
    /// <param name="ct">キャンセルトークン</param>
    public async Task<bool> TableExistsAsync(string tableName, CancellationToken ct)
    {
        await using var connection = await OpenOutsideAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT count(*) FROM sqlite_master WHERE type = 'table' AND name = @table";
        command.Parameters.AddWithValue("@table", tableName);

        return Convert.ToInt64(await command.ExecuteScalarAsync(ct), CultureInfo.InvariantCulture)
            > 0;
    }

    /// <summary>表に張るはずのトリガー（名前 → 文。<c>SyncLocalTracking.TriggersFor</c>）</summary>
    /// <param name="tableName">表の名前</param>
    public IReadOnlyDictionary<string, string> ExpectedTriggerTexts(string tableName) =>
        (
            Fixture switch
            {
                SyncTriggerFixture.SyncModel => M
                    .SyncLocalTracking.TriggersFor(
                        One(M.GeneratedSyncTables.All, t => t.TableName, tableName)
                    )
                    .Select(t => (t.Name, t.Sql)),
                SyncTriggerFixture.Sync => S
                    .SyncLocalTracking.TriggersFor(
                        One(S.GeneratedSyncTables.All, t => t.TableName, tableName)
                    )
                    .Select(t => (t.Name, t.Sql)),
                _ => V
                    .SyncLocalTracking.TriggersFor(
                        One(V.GeneratedSyncTables.All, t => t.TableName, tableName)
                    )
                    .Select(t => (t.Name, t.Sql)),
            }
        ).ToDictionary(t => t.Name, t => t.Sql, StringComparer.Ordinal);

    /// <summary>行の状態の表に、表とキー（整数）の送る行が 1 行あるか</summary>
    /// <param name="tableName">表の名前</param>
    /// <param name="key">キー</param>
    /// <param name="ct">キャンセルトークン</param>
    public async Task<long> CountRecordedAsync(string tableName, long key, CancellationToken ct)
    {
        await using var connection = await OpenOutsideAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT count(*) FROM quicker_sync_row WHERE table_name = @table AND \"key\" = @key "
            + "AND dirty = 1 AND deleted = 0";
        command.Parameters.AddWithValue("@table", tableName);
        command.Parameters.AddWithValue("@key", key);

        return Convert.ToInt64(await command.ExecuteScalarAsync(ct), CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// この DB のファイルを掴む接続のプールをすべて放す（ファイルを差し替える・写す前に呼ぶ。開いたままの接続は放さない）
    /// </summary>
    public void ReleaseFile()
    {
        using (var connection = _connect())
        {
            SqliteConnection.ClearPool(connection);
        }

        foreach (
            var connectionString in new[]
            {
                _db.ReadWriteCreateConnectionString,
                _db.ReadWriteConnectionString,
                _db.ReadOnlyConnectionString,
            }.Concat(_extraConnectionStrings)
        )
        {
            using var connection = new SqliteConnection(connectionString);
            SqliteConnection.ClearPool(connection);
        }

        // 生成の接続ファクトリは接続文字列へ外部キーの指定を足すので、足した形のプールも放す
        foreach (var connectionString in _extraConnectionStrings)
        {
            var builder = new SqliteConnectionStringBuilder(connectionString)
            {
                ForeignKeys = true,
            };
            using var connection = new SqliteConnection(builder.ConnectionString);
            SqliteConnection.ClearPool(connection);
        }
    }

    /// <summary>
    /// この DB のファイルを、トリガーの無い別の DB で置き換える（図の DDL だけを当て、無関係な表を足してスキーマの版を
    /// 元の DB と同じ値にそろえる）
    /// </summary>
    /// <param name="ct">キャンセルトークン</param>
    /// <param name="withStateTables">
    /// 別の DB に状態の表（と DB の識別子）だけを作っておくか（同期する表を 1 つも渡さない <c>SyncLocalTracking.Ensure</c>）。
    /// 作ると、速い道の 1 文は例外にならずに読め、置き換えを見分けるのは DB の識別子だけになる
    /// </param>
    public async Task ReplaceWithFreshDatabaseAsync(
        CancellationToken ct,
        bool withStateTables = false
    )
    {
        var version = Convert.ToInt64(
            await ScalarOutsideAsync("PRAGMA schema_version", ct),
            CultureInfo.InvariantCulture
        );

        using var fresh = SqliteTempDatabase.Create();
        await fresh.ApplyDdlAsync(Ddl, ct);

        await using (var connection = new SqliteConnection(fresh.ReadWriteCreateConnectionString))
        {
            await connection.OpenAsync(ct);

            if (withStateTables)
            {
                _ = Fixture switch
                {
                    SyncTriggerFixture.SyncModel => M.SyncLocalTracking.Ensure(connection, []),
                    SyncTriggerFixture.Sync => S.SyncLocalTracking.Ensure(connection, []),
                    _ => V.SyncLocalTracking.Ensure(connection, []),
                };
            }

            for (var i = 0; ; i++)
            {
                await using var read = connection.CreateCommand();
                read.CommandText = "PRAGMA schema_version";
                var current = Convert.ToInt64(
                    await read.ExecuteScalarAsync(ct),
                    CultureInfo.InvariantCulture
                );

                if (current == version)
                {
                    break;
                }

                if (current > version)
                {
                    throw new InvalidOperationException(
                        $"差し替える DB のスキーマの版（{current.ToString(CultureInfo.InvariantCulture)}）が元の DB の版"
                            + $"（{version.ToString(CultureInfo.InvariantCulture)}）を越えた"
                    );
                }

                await using var pad = connection.CreateCommand();
                pad.CommandText =
                    $"CREATE TABLE guard_pad_{i.ToString(CultureInfo.InvariantCulture)} (x INTEGER)";
                await pad.ExecuteNonQueryAsync(ct);
            }
        }

        using (var connection = new SqliteConnection(fresh.ReadWriteCreateConnectionString))
        {
            SqliteConnection.ClearPool(connection);
        }

        ReleaseFile();
        File.Copy(fresh.FilePath, DatabasePath, overwrite: true);
    }

    /// <summary>この DB のファイルの写しを取る（接続のプールを放してから写す）</summary>
    /// <param name="backupPath">写しのパス</param>
    public void CopyFileTo(string backupPath)
    {
        ReleaseFile();
        File.Copy(DatabasePath, backupPath, overwrite: true);
    }

    /// <summary>写しをこの DB のファイルへ戻す（接続のプールを放してから写す）</summary>
    /// <param name="backupPath">写しのパス</param>
    public void RestoreFileFrom(string backupPath)
    {
        ReleaseFile();
        File.Copy(backupPath, DatabasePath, overwrite: true);
    }

    /// <summary>接続のプールを放し、一時の DB を消す</summary>
    public void Dispose()
    {
        // 包んだ接続ファクトリは接続文字列へ外部キーの指定を足すので、一時の DB の後始末が放すプールとは別のプールになる
        ReleaseFile();
        _db.Dispose();
    }
}

/// <summary>確認のガードが包む内側の接続ファクトリ</summary>
internal enum SyncTriggerGuardInner
{
    /// <summary>生成の接続ファクトリ（外部キーの既定・<c>busy_timeout</c>）にテストのジャーナルの設定を足したもの</summary>
    Generated,

    /// <summary>テストの中の自前の接続ファクトリ（<c>new SqliteConnection(接続文字列)</c> を返すだけ。同期のモデルの図だけ）</summary>
    Custom,
}

/// <summary>
/// 確認のガードと、それで包んだ接続ファクトリの組（フィクスチャの生成物の型を消して、回数・接続・書き込みだけを見せる）
/// </summary>
internal sealed class SyncTriggerGuardProbe
{
    private readonly Func<int> _full;
    private readonly Func<int> _repair;
    private readonly Func<string, StateChangeEventHandler, Func<SqliteConnection>> _wrap;
    private readonly Func<SqliteConnection> _create;
    private readonly Func<int, CancellationToken, Task<string>> _write;

    internal SyncTriggerGuardProbe(
        IReadOnlyList<string> tables,
        Func<int> full,
        Func<int> repair,
        StateChangeEventHandler handler,
        Func<string, StateChangeEventHandler, Func<SqliteConnection>> wrap,
        string connectionString,
        Func<int, CancellationToken, Task<string>> write
    )
    {
        Tables = tables;
        ConnectionString = connectionString;
        _full = full;
        _repair = repair;
        Handler = handler;
        _wrap = wrap;
        _create = wrap(connectionString, handler);
        _write = write;
    }

    /// <summary>ガードが見る表の名前</summary>
    public IReadOnlyList<string> Tables { get; }

    /// <summary>包んだ内側の接続ファクトリへ渡した接続文字列</summary>
    public string ConnectionString { get; }

    /// <summary>ガードの <c>OnStateChange</c></summary>
    public StateChangeEventHandler Handler { get; }

    /// <summary>ガードが 2 段目（読み取りだけの確認）に入った回数</summary>
    public int FullCheckCount => _full();

    /// <summary>ガードが張り直しを流した回数</summary>
    public int RepairCount => _repair();

    /// <summary>包んだ接続ファクトリから接続を作る（閉じたまま）</summary>
    public SqliteConnection CreateConnection() => _create();

    /// <summary>
    /// 同じ内側の接続ファクトリの形で、別の接続文字列をこのガードで包む
    /// </summary>
    /// <param name="connectionString">内側の接続ファクトリへ渡す接続文字列</param>
    /// <param name="beforeGuard">ガードの直前に同じ位置で呼ぶ処理（null なら無い）</param>
    public Func<SqliteConnection> Wrap(
        string connectionString,
        StateChangeEventHandler? beforeGuard = null
    ) =>
        _wrap(
            connectionString,
            beforeGuard is null
                ? Handler
                : (sender, e) =>
                {
                    beforeGuard(sender, e);
                    Handler(sender, e);
                }
        );

    /// <summary>包んだ接続ファクトリの Repository で、キーの行を 1 つ挿入する</summary>
    /// <param name="key">キー（整数）</param>
    /// <param name="ct">キャンセルトークン</param>
    /// <returns>挿入した表の名前</returns>
    public Task<string> WriteAsync(int key, CancellationToken ct) => _write(key, ct);
}

/// <summary>テストの中の自前の接続ファクトリ（<c>busy_timeout</c> も外部キーの指定も足さない）</summary>
internal sealed class SyncTriggerRawConnectionFactory(string connectionString)
    : MS.ISqlConnectionFactory
{
    public SqliteConnection CreateConnection() => new(connectionString);
}
