using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using QuickER.Tests.GeneratedSyncModelFixture;
using QuickER.Tests.GeneratedSyncModelFixture.Repositories.Sqlite;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// 同期のモデルの新しいエンジンの端末（トリガーで変更を記録し、行の状態の表を状態の面にする端末）の作り方。
/// </summary>
/// <remarks>
/// <para>
/// 組み立て：殻（<see cref="SyncModelDevice.OpenAsync"/>）→ 同期する 4 表に <c>SyncLocalTracking.Ensure</c> でトリガーと状態の表を
/// 張る → 開くたびの確認（<c>SyncLocalTrackingGuard</c>）で包んだ接続ファクトリ（<c>ObservedSqlConnectionFactory</c>）へ殻を
/// 載せ替える → そのファクトリで素の生成 Repository 4 つ（記録のデコレータで包まない。殻の Save フックの登録簿は渡す）と
/// <c>SqliteLocalRowStore</c> を作る → <see cref="SyncModelDevice.Install"/>。
/// </para>
/// <para>
/// エンジンの面（<see cref="EngineModelDeviceSync"/>）は、4 表の <c>TrackedSyncTable</c>（記述子 <c>GeneratedSyncTables.*</c>・
/// 端末の注文の差分ソース <c>request.Server</c> と blob の面・<c>SqliteLocalRowStore</c>）で組んだ <c>TrackedSyncEngine</c> を呼ぶ。外部の <c>ISyncTable</c>
/// の代役で包む端末（<see cref="SyncModelDeviceRequest.ExternalTables"/>）は受け持たず、「流せない」
/// （<see cref="SyncModelDevice.Unsupported"/>）で断る。
/// </para>
/// </remarks>
internal static class SyncModelEngineDevice
{
    /// <summary>世界の組み方の端末の作り方を、この端末の作り方へ差し替える（名前に <c>+engine</c> を足す）</summary>
    /// <param name="options">元の組み方（サーバー役・転送経路はそのまま）</param>
    public static SyncModelWorldOptions On(SyncModelWorldOptions options) =>
        options with
        {
            CreateDevice = CreateAsync,
            Name = options.Name + "+engine",
        };

    /// <summary>端末 1 台を作る（<see cref="SyncModelWorldOptions.CreateDevice"/> へ渡す形）</summary>
    /// <param name="request">端末の注文</param>
    /// <param name="ct">キャンセルトークン</param>
    public static async Task<SyncModelDevice> CreateAsync(
        SyncModelDeviceRequest request,
        CancellationToken ct
    )
    {
        if (request.ExternalTables)
        {
            throw SyncModelDevice.Unsupported(
                nameof(SyncModelDeviceRequest.ExternalTables),
                "engine"
            );
        }

        var device = await SyncModelDevice.OpenAsync(request.Actor, request.Faults, ct);

        try
        {
            var plain = new LightJournalSyncModelConnectionFactory(device.ConnectionString);

            await using (var connection = plain.CreateConnection())
            {
                await connection.OpenAsync(ct);
                _ = SyncLocalTracking.Ensure(connection, GeneratedSyncTables.All);
            }

            var guard = new SyncLocalTrackingGuard(GeneratedSyncTables.All);
            var factory = new ObservedSqlConnectionFactory(plain, guard.OnStateChange);
            device.UseConnectionFactory(factory);

            var rowStore = new SqliteLocalRowStore(factory);
            var state = new EngineModelDeviceSyncState(factory, rowStore);
            var server = request.Server;
            var engine = new TrackedSyncEngine([
                new TrackedSyncTable<ModelFolderEntity, int>(
                    GeneratedSyncTables.ModelFolder,
                    server.FolderSource,
                    rowStore
                ),
                new TrackedSyncTable<ModelLabelEntity, string>(
                    GeneratedSyncTables.ModelLabel,
                    server.LabelSource,
                    rowStore
                ),
                new TrackedSyncTable<ModelItemEntity, int>(
                    GeneratedSyncTables.ModelItem,
                    server.ItemSource,
                    rowStore
                ),
                new TrackedSyncTable<ModelEventEntity, DateTime>(
                    GeneratedSyncTables.ModelEvent,
                    server.EventSource,
                    rowStore
                ),
            ]);

            device.Install(
                new SyncModelDeviceParts
                {
                    Folders = new ModelFolderRepository(factory, device.Hooks),
                    Labels = new ModelLabelRepository(factory, device.Hooks),
                    Items = new ModelItemRepository(factory, device.Hooks),
                    Events = new ModelEventRepository(factory, device.Hooks),
                    Sync = new EngineModelDeviceSync(engine, rowStore),
                    State = state,
                    WriteUntrackedAsync = state.WriteUntrackedAsync,
                    ClearPendingRecordsAsync = state.ClearPendingAsync,
                }
            );

            return device;
        }
        catch
        {
            device.Dispose();
            throw;
        }
    }
}

/// <summary>
/// 新しいエンジンの端末のエンジンの面。<c>TrackedSyncEngine</c> を呼び、結果を <see cref="ModelSyncOutcome"/> へ写す
/// （<see cref="ProductModelDeviceSync"/> と同じ形）。エンジンの例外はそのまま通す。
/// </summary>
/// <param name="engine">端末のエンジン</param>
/// <param name="rows">端末のローカルの行を読み書きする口（エンジンが使う）</param>
internal sealed class EngineModelDeviceSync(TrackedSyncEngine engine, ILocalRowStore rows)
    : IModelDeviceSync
{
    /// <summary>端末のローカルの行を読み書きする口</summary>
    public ILocalRowStore Rows { get; } = rows;

    /// <summary>新しいエンジンは、ServerWins の実行が戻せなかった行の決定を後の実行へ持ち越す</summary>
    public bool CarriesServerWinsDecision => true;

    /// <summary>新しいエンジンの適用は、ローカルの Save フックを呼ばない（ローカルの行の口で書く）</summary>
    public bool AppliesThroughSaveHooks => false;

    /// <summary>新しいエンジンは、再開点を状態の表（表ごとの再開点）に持ち、ローカルの行の版の列から導かない</summary>
    public bool DerivesResumePointFromLocalVersions => false;

    /// <summary>
    /// 新しいエンジンは、同期と洗い替えの取得で保留した行があれば、上限を取り直してもう 1 回だけ取得する（同じ実行の中）
    /// </summary>
    public bool RefetchesHeldRowsInSameRun => true;

    public async Task<ModelSyncOutcome> RunAsync(
        ModelSyncKind kind,
        SyncOptions options,
        CancellationToken ct
    )
    {
        switch (kind)
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
                throw new ArgumentOutOfRangeException(nameof(kind), kind, null);
        }
    }

    public Task<SyncRefreshResult> RefreshAsync(
        SyncRefreshOptions? options,
        CancellationToken ct
    ) => engine.RefreshAsync(options, ct);
}

/// <summary>
/// 新しいエンジンの端末の状態の面。行の状態の表（<c>SyncLocalTracking.RowTableName</c>）の未送信の行（<c>dirty = 1</c>）から
/// <see cref="ModelPendingRow"/> を作る。
/// </summary>
/// <remarks>
/// <para>
/// キーのテキストは、状態の表の生のキーの値を <c>ILocalRowStore.ConvertKey</c> で型つきのキーへ変え、その表の記述子
/// （<c>GeneratedSyncTables.*</c>）の <c>FormatKey</c> で書いたもの。<see cref="ModelPendingRow.HasDelete"/> は削除の印、
/// <see cref="ModelPendingRow.HasUpsert"/> は削除の印の無い未送信、<see cref="ModelPendingRow.ServerKnowsRow"/> は版なしの表
/// （ラベル）なら常に真・ほかは <c>known = 1</c>。変わる印は変更の連番 1 つで、並びは変更の連番の順。
/// </para>
/// <para>
/// 未送信を外す口（<see cref="ClearPendingAsync"/>）は、未送信の印・削除の印・書いた blob の列を初めの値へ戻し、エンジンの列
/// （<c>base_version</c>・<c>known</c>・<c>sw_seq</c>・<c>blob_owed</c>）と変更の連番は消さない。
/// </para>
/// </remarks>
/// <param name="factory">端末の接続ファクトリ（開くたびの確認つき）</param>
/// <param name="rows">生のキーの値を型つきのキーへ変える口</param>
internal sealed class EngineModelDeviceSyncState(ISqlConnectionFactory factory, ILocalRowStore rows)
    : IModelDeviceSyncState
{
    public async Task<IReadOnlyList<ModelPendingRow>> ReadPendingAsync(CancellationToken ct)
    {
        var pending = new List<ModelPendingRow>();
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"SELECT table_name, \"key\", deleted, known, change_seq FROM {SyncLocalTracking.RowTableName} "
            + "WHERE dirty = 1 ORDER BY change_seq";
        await using var reader = await command.ExecuteReaderAsync(ct);

        while (await reader.ReadAsync(ct))
        {
            var table = reader.GetString(0);
            var deleted = reader.GetInt64(2) != 0;
            var seq = reader.GetInt64(4);

            pending.Add(
                new ModelPendingRow(
                    table,
                    KeyText(table, reader.GetValue(1)),
                    !deleted,
                    deleted,
                    table == ModelTables.Labels || reader.GetInt64(3) != 0,
                    [seq]
                )
                {
                    Operations = [deleted ? "Delete" : "Upsert"],
                }
            );
        }

        return pending;
    }

    public async Task<int> CountPendingAsync(CancellationToken ct)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"SELECT count(*) FROM {SyncLocalTracking.RowTableName} WHERE dirty = 1";

        return Convert.ToInt32(await command.ExecuteScalarAsync(ct), CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// 1 行（表の名前と記述子の書式のキーのテキスト）の未送信を外す
    /// </summary>
    /// <param name="table">表の名前</param>
    /// <param name="keyText">キーのテキスト（その表の記述子の <c>FormatKey</c> の書式）</param>
    /// <param name="ct">キャンセルトークン</param>
    public async Task ClearPendingAsync(string table, string keyText, CancellationToken ct)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync(ct);

        // 状態の表は格納のままのキーを持つので、その表の行のキーを読み、記述子の書式に直して当てる
        var rawKeys = new List<object>();

        await using (var read = connection.CreateCommand())
        {
            read.CommandText =
                $"SELECT \"key\" FROM {SyncLocalTracking.RowTableName} WHERE table_name = @table";
            read.Parameters.AddWithValue("@table", table);
            await using var reader = await read.ExecuteReaderAsync(ct);

            while (await reader.ReadAsync(ct))
            {
                var raw = reader.GetValue(0);

                if (string.Equals(KeyText(table, raw), keyText, StringComparison.Ordinal))
                {
                    rawKeys.Add(raw);
                }
            }
        }

        foreach (var raw in rawKeys)
        {
            await using var clear = connection.CreateCommand();
            clear.CommandText =
                $"UPDATE {SyncLocalTracking.RowTableName} SET dirty = 0, deleted = 0, blob_columns = '' "
                + "WHERE table_name = @table AND \"key\" = @key";
            clear.Parameters.AddWithValue("@table", table);
            clear.Parameters.AddWithValue("@key", raw);
            await clear.ExecuteNonQueryAsync(ct);
        }
    }

    /// <summary>
    /// 記録しない書き込みを流す関数を、状態の表の適用中の印（<c>quicker_sync_state.applying</c>）を立てた間に流す（トリガーは印が
    /// 立っている間は記録しないので、書いた行は未送信にならず、前からある未送信の行・エンジンの列・変更の連番も変わらない）
    /// </summary>
    /// <remarks>
    /// 印を立てる・戻す書き込みは、端末の接続ファクトリの普通の接続でコミットする（関数がほかの接続で書いても効く）。立てる前に
    /// 印が 0 であることを確かめ、0 でなければ投げる（エンジンの適用の中で入れ子にしない）。印は関数が例外で終わっても戻す
    /// </remarks>
    /// <param name="write">記録しない書き込みを流す関数</param>
    /// <param name="ct">キャンセルトークン</param>
    /// <exception cref="InvalidOperationException">印がすでに立っている</exception>
    public async Task WriteUntrackedAsync(Func<Task> write, CancellationToken ct)
    {
        await using (var connection = factory.CreateConnection())
        {
            await connection.OpenAsync(ct);
            await using var command = connection.CreateCommand();

            // 印が 0 の行だけを 1 に変える（0 でなければ 1 行も変わらない＝ほかの適用の途中）
            command.CommandText =
                $"UPDATE {SyncLocalTracking.StateTableName} SET applying = 1 WHERE applying = 0";

            if (await command.ExecuteNonQueryAsync(ct) != 1)
            {
                throw new InvalidOperationException(
                    "The applying flag of the change tracking is already set: an untracked write cannot run inside an apply."
                );
            }
        }

        try
        {
            await write();
        }
        finally
        {
            await using var connection = factory.CreateConnection();
            await connection.OpenAsync(CancellationToken.None);
            await using var command = connection.CreateCommand();
            command.CommandText = $"UPDATE {SyncLocalTracking.StateTableName} SET applying = 0";
            await command.ExecuteNonQueryAsync(CancellationToken.None);
        }
    }

    /// <summary>状態の表の生のキーの値を、その表の記述子の書式のキーのテキストへ直す</summary>
    private string KeyText(string table, object raw) =>
        table switch
        {
            ModelTables.Folders => GeneratedSyncTables.ModelFolder.FormatKey(
                (int)rows.ConvertKey<ModelFolderEntity>(raw)
            ),
            ModelTables.Labels => GeneratedSyncTables.ModelLabel.FormatKey(
                (string)rows.ConvertKey<ModelLabelEntity>(raw)
            ),
            ModelTables.Items => GeneratedSyncTables.ModelItem.FormatKey(
                (int)rows.ConvertKey<ModelItemEntity>(raw)
            ),
            ModelTables.Events => GeneratedSyncTables.ModelEvent.FormatKey(
                (DateTime)rows.ConvertKey<ModelEventEntity>(raw)
            ),
            _ => throw new ArgumentOutOfRangeException(nameof(table), table, null),
        };
}
