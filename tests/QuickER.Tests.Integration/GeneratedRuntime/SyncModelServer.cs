using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using QuickER.Tests.GeneratedSyncModelFixture;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// 同期のモデルベーステストのサーバー役（SQLite の 3 つ目のデータベース）が、書き込みのたびに版を採番し、更新に版ガードを掛ける
/// ための薄いラッパー。
/// </summary>
/// <remarks>
/// <para>
/// 意味論は <c>SyncTestServerRepository</c>（<c>SyncFixture</c> 用）と同じ。生成物はフィクスチャごとに別の名前空間へ
/// 出るため（<c>IRepository</c> も <c>SaveConflictException</c> も別の型になる）、同じ形をこのフィクスチャの型で持つ。
/// </para>
/// <para>
/// 版はモデルの世界ごとの採番列から取る（世界をまたいで共有すると、並列に走る種どうしが互いの版を進め合う）。
/// </para>
/// </remarks>
/// <typeparam name="TEntity">エンティティ型</typeparam>
/// <typeparam name="TKey">主キー型</typeparam>
internal sealed class SyncModelServerRepository<TEntity, TKey>(
    IRepository<TEntity, TKey> inner,
    SyncTestVersionSequence versions,
    Func<TEntity, TKey> readKey,
    Func<TEntity, byte[]?> readVersion,
    Action<TEntity, byte[]?> writeVersion,
    SyncModelFaults faults
) : IRepository<TEntity, TKey>
    where TEntity : EntityBase, new()
{
    /// <summary>版を採番して書き込む（DB が採番する列の代役）</summary>
    private void Stamp(TEntity entity) => writeVersion(entity, versions.Next());

    /// <summary>更新・削除の前に「読んだときの版」と現在の版が一致することを確かめる</summary>
    private async Task GuardAsync(TEntity entity, CancellationToken cancellationToken)
    {
        var key = readKey(entity);
        var current = await inner.GetByIdAsync(key, cancellationToken).ConfigureAwait(false);

        if (current is null)
        {
            throw SaveConflictException.NotFound(typeof(TEntity), key);
        }

        var expected = readVersion(entity);
        var actual = readVersion(current);

        if (expected is null || actual is null || !expected.SequenceEqual(actual))
        {
            throw SaveConflictException.Modified(typeof(TEntity), key, "save");
        }
    }

    public Task<TEntity?> GetByIdAsync(TKey id, CancellationToken cancellationToken = default) =>
        inner.GetByIdAsync(id, cancellationToken);

    public Task<IReadOnlyList<TEntity>> GetAllAsync(
        CancellationToken cancellationToken = default
    ) => inner.GetAllAsync(cancellationToken);

    public Task<IReadOnlyList<UniquenessViolation>> CheckUniquenessAsync(
        TEntity entity,
        CancellationToken cancellationToken = default
    ) => inner.CheckUniquenessAsync(entity, cancellationToken);

    public async Task InsertAsync(TEntity entity, CancellationToken cancellationToken = default)
    {
        await faults.InterruptAsync(ModelInterruptPoint.BeforeServerWrite).ConfigureAwait(false);
        faults.ThrowOnServerWrite(typeof(TEntity), readKey(entity)!);
        Stamp(entity);
        await inner.InsertAsync(entity, cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> UpdateAsync(
        TEntity entity,
        ConcurrencyMode mode = ConcurrencyMode.Optimistic,
        CancellationToken cancellationToken = default
    )
    {
        await faults.InterruptAsync(ModelInterruptPoint.BeforeServerWrite).ConfigureAwait(false);
        faults.ThrowOnServerWrite(typeof(TEntity), readKey(entity)!);
        var key = readKey(entity);

        if (await inner.GetByIdAsync(key, cancellationToken).ConfigureAwait(false) is null)
        {
            return false;
        }

        if (mode == ConcurrencyMode.Optimistic)
        {
            await GuardAsync(entity, cancellationToken).ConfigureAwait(false);
        }

        Stamp(entity);

        return await inner
            .UpdateAsync(entity, ConcurrencyMode.ForceOverwrite, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<bool> DeleteAsync(TKey id, CancellationToken cancellationToken = default)
    {
        await faults.InterruptAsync(ModelInterruptPoint.BeforeServerWrite).ConfigureAwait(false);
        faults.ThrowOnServerWrite(typeof(TEntity), id!);

        return await inner.DeleteAsync(id, cancellationToken).ConfigureAwait(false);
    }

    public async Task<int> SaveAsync(
        TEntity entity,
        bool cascadeSave = true,
        bool cascadeDelete = true,
        bool insertWhenUpdateMissing = false,
        ConcurrencyMode mode = ConcurrencyMode.Optimistic,
        CancellationToken cancellationToken = default
    )
    {
        await faults.InterruptAsync(ModelInterruptPoint.BeforeServerWrite).ConfigureAwait(false);
        faults.ThrowOnServerWrite(typeof(TEntity), readKey(entity)!);

        // 同期エンジンが使うのは「版ガード付きの削除」だけ（RowState=Removed のスタブ 1 件）
        if (entity.RowState == RowState.Removed)
        {
            var key = readKey(entity);

            if (await inner.GetByIdAsync(key, cancellationToken).ConfigureAwait(false) is null)
            {
                return 0;
            }

            if (mode == ConcurrencyMode.Optimistic)
            {
                await GuardAsync(entity, cancellationToken).ConfigureAwait(false);
            }

            await inner.DeleteAsync(key, cancellationToken).ConfigureAwait(false);
            entity.MarkUnchanged();

            return 1;
        }

        if (entity.RowState == RowState.Added)
        {
            Stamp(entity);
        }

        return await inner
            .SaveAsync(
                entity,
                cascadeSave,
                cascadeDelete,
                insertWhenUpdateMissing,
                ConcurrencyMode.ForceOverwrite,
                cancellationToken
            )
            .ConfigureAwait(false);
    }

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

    public Task<int> BulkInsertAsync(
        IEnumerable<TEntity> entities,
        CancellationToken cancellationToken = default
    ) => inner.BulkInsertAsync(entities, cancellationToken);

    public SqlQuery<TEntity> Query() => inner.Query();

    public Task<IReadOnlyList<TEntity>> QueryBySqlAsync(
        string sql,
        object? parameters = null,
        CancellationToken cancellationToken = default
    ) => inner.QueryBySqlAsync(sql, parameters, cancellationToken);

    public Task<int> ExecuteSqlAsync(
        string sql,
        object? parameters = null,
        CancellationToken cancellationToken = default
    ) => inner.ExecuteSqlAsync(sql, parameters, cancellationToken);

    public Task<TResult?> ExecuteScalarSqlAsync<TResult>(
        string sql,
        object? parameters = null,
        CancellationToken cancellationToken = default
    ) => inner.ExecuteScalarSqlAsync<TResult>(sql, parameters, cancellationToken);
}

/// <summary>
/// サーバー役（SQLite）に対する版ありの表の差分ソース（生成される直結実装の SQL Server 版と同じ意味論を SQLite の語彙で書いたもの）。
/// </summary>
/// <remarks>
/// 上限（<c>MIN_ACTIVE_ROWVERSION()</c> 相当）は null。モデルの世界は 1 つの非同期フローで操作を 1 つずつ流すため、
/// 未コミットの書き込みが同期と重ならない。
/// </remarks>
/// <typeparam name="TEntity">エンティティ型</typeparam>
/// <typeparam name="TKey">主キー型</typeparam>
internal sealed class SyncModelServerSource<TEntity, TKey>(
    ISqlExecutor serverSqlExecutor,
    IRepository<TEntity, TKey> writer,
    string changesSql,
    string keysSql,
    SyncModelFaults faults,
    ISyncBinaryColumns<TKey>? binaryColumns = null
) : ISyncServerSource<TEntity, TKey>
    where TEntity : EntityBase, new()
{
    public IRemoteRepositoryCore<TEntity, TKey> Writer => writer;

    public ISyncBinaryColumns<TKey>? BinaryColumns => binaryColumns;

    public Task<byte[]?> GetChangeCeilingAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<byte[]?>(null);

    public async Task<SyncChangeBatch<TEntity>> GetChangesAsync(
        byte[]? anchor,
        byte[]? ceiling,
        int batchSize,
        CancellationToken cancellationToken = default
    )
    {
        await faults.InterruptAsync(ModelInterruptPoint.BeforeServerFetch).ConfigureAwait(false);
        faults.ThrowOnServer(ModelFaultKind.ServerFetch);
        var rows = await serverSqlExecutor.QueryBySqlAsync<TEntity>(
            changesSql,
            new
            {
                anchor,
                ceiling,
                batchSize,
            },
            cancellationToken
        );

        return await faults.AfterFetchAsync(rows, batchSize).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<TKey>> GetAllKeysAsync(
        CancellationToken cancellationToken = default
    )
    {
        await faults.InterruptAsync(ModelInterruptPoint.BeforeServerFetch).ConfigureAwait(false);
        faults.ThrowOnServer(ModelFaultKind.ServerFetch);

        return await serverSqlExecutor
            .QueryProjectionBySqlAsync<TKey>(keysSql, null, cancellationToken)
            .ConfigureAwait(false);
    }
}

/// <summary>サーバー役（SQLite）に対する版なしの表の差分ソース（キー順ページング）</summary>
/// <typeparam name="TEntity">エンティティ型</typeparam>
/// <typeparam name="TKey">主キー型</typeparam>
internal sealed class SyncModelVersionlessServerSource<TEntity, TKey>(
    ISqlExecutor serverSqlExecutor,
    IRepository<TEntity, TKey> writer,
    string firstPageSql,
    string pageAfterSql,
    string keysSql,
    SyncModelFaults faults
) : ISyncServerSource<TEntity, TKey>
    where TEntity : EntityBase, new()
{
    public IRemoteRepositoryCore<TEntity, TKey> Writer => writer;

    public Task<byte[]?> GetChangeCeilingAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<byte[]?>(null);

    public Task<SyncChangeBatch<TEntity>> GetChangesAsync(
        byte[]? anchor,
        byte[]? ceiling,
        int batchSize,
        CancellationToken cancellationToken = default
    ) =>
        throw new NotSupportedException(
            "The table has no version column; its download pages by primary key."
        );

    public async Task<SyncChangeBatch<TEntity>> GetFirstPageAsync(
        int batchSize,
        CancellationToken cancellationToken = default
    )
    {
        await faults.InterruptAsync(ModelInterruptPoint.BeforeServerFetch).ConfigureAwait(false);
        faults.ThrowOnServer(ModelFaultKind.ServerFetch);
        var rows = await serverSqlExecutor.QueryBySqlAsync<TEntity>(
            firstPageSql,
            new { batchSize },
            cancellationToken
        );

        return await faults.AfterFetchAsync(rows, batchSize).ConfigureAwait(false);
    }

    public async Task<SyncChangeBatch<TEntity>> GetPageAfterAsync(
        TKey afterKey,
        int batchSize,
        CancellationToken cancellationToken = default
    )
    {
        await faults.InterruptAsync(ModelInterruptPoint.BeforeServerFetch).ConfigureAwait(false);
        faults.ThrowOnServer(ModelFaultKind.ServerFetch);
        var rows = await serverSqlExecutor.QueryBySqlAsync<TEntity>(
            pageAfterSql,
            new { afterKey, batchSize },
            cancellationToken
        );

        return await faults.AfterFetchAsync(rows, batchSize).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<TKey>> GetAllKeysAsync(
        CancellationToken cancellationToken = default
    )
    {
        await faults.InterruptAsync(ModelInterruptPoint.BeforeServerFetch).ConfigureAwait(false);
        faults.ThrowOnServer(ModelFaultKind.ServerFetch);

        return await serverSqlExecutor
            .QueryProjectionBySqlAsync<TKey>(keysSql, null, cancellationToken)
            .ConfigureAwait(false);
    }
}

/// <summary>
/// サーバー役の品目の除外列（<c>payload</c>）アクセサ。書き込みが成功したら行の版も進める（実 SQL Server で
/// <c>varbinary(max)</c> を書くと <c>rowversion</c> が進むことの代役）。
/// </summary>
internal sealed class SyncModelItemBinaryColumns(
    IModelItemRepository repository,
    ISqlExecutor serverSqlExecutor,
    SyncTestVersionSequence versions,
    SyncModelFaults faults
) : ISyncBinaryColumns<int>
{
    private const string StampSql =
        "UPDATE \"model_items\" SET \"row_ver\" = @version WHERE \"item_id\" = @id";

    public IReadOnlyList<string> UnboundedBinaryColumnNames => ["Payload"];

    public Task<bool> ReadUnboundedBinaryAsync(
        string columnName,
        int id,
        Stream destination,
        CancellationToken cancellationToken = default
    )
    {
        faults.ThrowOnServer(ModelFaultKind.BlobDownload);

        return repository.ReadPayloadAsync(id, destination, cancellationToken);
    }

    public async Task<bool> WriteUnboundedBinaryAsync(
        string columnName,
        int id,
        Stream? source,
        long? length,
        CancellationToken cancellationToken = default
    )
    {
        faults.ThrowOnServer(ModelFaultKind.BlobUpload);
        var written = await repository
            .WritePayloadAsync(id, source, length, cancellationToken)
            .ConfigureAwait(false);

        if (written)
        {
            await serverSqlExecutor
                .ExecuteSqlAsync(StampSql, new { version = versions.Next(), id }, cancellationToken)
                .ConfigureAwait(false);
        }

        return written;
    }
}

/// <summary>サーバー役（SQLite）の差分 SQL（生成される SQL Server 版と同じ意味論を SQLite の語彙で書いたもの）</summary>
internal static class SyncModelServerSql
{
    /// <summary>版ありの表の差分取得 SQL を組み立てる（<paramref name="columns"/> は除外列を落とした列の並び）</summary>
    public static string Changes(string table, string columns) =>
        $"SELECT {columns} FROM \"{table}\" WHERE (@anchor IS NULL OR \"row_ver\" > @anchor) "
        + "AND (@ceiling IS NULL OR \"row_ver\" < @ceiling) ORDER BY \"row_ver\" LIMIT @batchSize";

    /// <summary>キーの一覧を取る SQL</summary>
    public static string Keys(string table, string keyColumn) =>
        $"SELECT \"{keyColumn}\" FROM \"{table}\"";

    public static readonly string FolderChanges = Changes("model_folders", "*");

    public static readonly string FolderKeys = Keys("model_folders", "folder_id");

    // 除外列（payload）を明示的に落とす＝生成される差分 SQL と同じ（"*" だと blob まで行に載る）
    public static readonly string ItemChanges = Changes(
        "model_items",
        "\"item_id\", \"folder_id\", \"label_code\", \"code\", \"title\", \"row_ver\""
    );

    public static readonly string ItemKeys = Keys("model_items", "item_id");

    public static readonly string EventChanges = Changes("model_events", "*");

    public static readonly string EventKeys = Keys("model_events", "event_at");

    public const string LabelFirstPage =
        "SELECT * FROM \"model_labels\" ORDER BY \"label_code\" LIMIT @batchSize";

    public const string LabelPageAfter =
        "SELECT * FROM \"model_labels\" WHERE \"label_code\" > @afterKey "
        + "ORDER BY \"label_code\" LIMIT @batchSize";

    public static readonly string LabelKeys = Keys("model_labels", "label_code");
}
