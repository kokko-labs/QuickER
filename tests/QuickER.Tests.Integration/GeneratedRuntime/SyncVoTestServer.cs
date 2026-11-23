using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using QuickER.Tests.GeneratedSyncVoFixture;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// SyncVoFixture のサーバー役（<see cref="SyncVoFixtureTestServer"/>）の書き込み面。版を採番し、楽観排他を
/// <c>rowversion</c> と同じ意味論で肩代わりする。
/// </summary>
/// <remarks>
/// 同期エンジンが差分ソース越しに使うのはリモート面（<see cref="IRemoteRepository{TEntity, TKey}"/>）だけ
/// なので、サーバー役もその面だけで足りる。版が VO 型でも扱いは変わらない＝生成側と同じく
/// <c>?.Value</c> で読み、<c>Create</c> で書く。
/// </remarks>
internal sealed class SyncVoTestServerWriter(ISyncvoOrderRepository inner)
    : IRemoteRepository<SyncvoOrderEntity, OrderIdValue>
{
    private long _counter;

    /// <summary>次の版（8 バイト big-endian＝バイト列の辞書順が数値順と一致する）を採番して書く</summary>
    private void Stamp(SyncvoOrderEntity entity)
    {
        var bytes = BitConverter.GetBytes(Interlocked.Increment(ref _counter));

        if (BitConverter.IsLittleEndian)
        {
            Array.Reverse(bytes);
        }

        entity.RowVer = RowVerValue.Create(bytes);
    }

    public Task<SyncvoOrderEntity?> GetByIdAsync(
        OrderIdValue id,
        CancellationToken cancellationToken = default
    ) => inner.GetByIdAsync(id, cancellationToken);

    public Task<IReadOnlyList<SyncvoOrderEntity>> GetAllAsync(
        CancellationToken cancellationToken = default
    ) => inner.GetAllAsync(cancellationToken);

    /// <summary>重複事前チェックは版採番と無関係なので素通しする</summary>
    public Task<IReadOnlyList<UniquenessViolation>> CheckUniquenessAsync(
        SyncvoOrderEntity entity,
        CancellationToken cancellationToken = default
    ) => inner.CheckUniquenessAsync(entity, cancellationToken);

    public async Task InsertAsync(
        SyncvoOrderEntity entity,
        CancellationToken cancellationToken = default
    )
    {
        Stamp(entity);
        await inner.InsertAsync(entity, cancellationToken);
    }

    public async Task<bool> UpdateAsync(
        SyncvoOrderEntity entity,
        ConcurrencyMode mode = ConcurrencyMode.Optimistic,
        CancellationToken cancellationToken = default
    )
    {
        var current = await inner.GetByIdAsync(entity.OrderId, cancellationToken);

        if (current is null)
        {
            return false;
        }

        if (
            mode == ConcurrencyMode.Optimistic
            && !(entity.RowVer?.Value ?? []).SequenceEqual(current.RowVer?.Value ?? [])
        )
        {
            throw SaveConflictException.Modified(typeof(SyncvoOrderEntity), entity.OrderId, "save");
        }

        Stamp(entity);

        return await inner.UpdateAsync(entity, ConcurrencyMode.ForceOverwrite, cancellationToken);
    }

    public Task<bool> DeleteAsync(OrderIdValue id, CancellationToken cancellationToken = default) =>
        inner.DeleteAsync(id, cancellationToken);

    public async Task<int> SaveAsync(
        SyncvoOrderEntity entity,
        bool cascadeSave = true,
        bool cascadeDelete = true,
        bool insertWhenUpdateMissing = false,
        ConcurrencyMode mode = ConcurrencyMode.Optimistic,
        CancellationToken cancellationToken = default
    )
    {
        // 同期エンジンが使うのは「版ガード付きの削除」だけ（RowState=Removed のスタブ 1 件）
        if (entity.RowState == RowState.Removed)
        {
            return await inner.DeleteAsync(entity.OrderId, cancellationToken) ? 1 : 0;
        }

        Stamp(entity);

        return await inner.SaveAsync(
            entity,
            cascadeSave,
            cascadeDelete,
            insertWhenUpdateMissing,
            ConcurrencyMode.ForceOverwrite,
            cancellationToken
        );
    }

    public Task<int> SaveAsync(
        IEnumerable<SyncvoOrderEntity> entities,
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

/// <summary>
/// SyncVoFixture のサーバー役（2 つ目の SQLite DB）に対する差分ソース。生成される直結実装の SQL Server 版と同じ意味論を
/// SQLite の語彙で表現する（キーが VO 型でも投影の扱いは生成側と同一）。
/// </summary>
internal sealed class SyncVoTestServerSource(
    ISqlExecutor serverSqlExecutor,
    IRemoteRepository<SyncvoOrderEntity, OrderIdValue> writer
) : ISyncServerSource<SyncvoOrderEntity, OrderIdValue>
{
    // 生成された差分ソースと同じく、除外列（photo）を SELECT に含めない（行の転送は除外列を運ばない）
    private const string ChangesSql =
        "SELECT \"order_id\", \"title\", \"row_ver\" FROM \"syncvo_orders\" WHERE (@anchor IS NULL OR \"row_ver\" > @anchor) "
        + "AND (@ceiling IS NULL OR \"row_ver\" < @ceiling) ORDER BY \"row_ver\" LIMIT @batchSize";

    private const string KeysSql = "SELECT \"order_id\" FROM \"syncvo_orders\"";

    public IRemoteRepositoryCore<SyncvoOrderEntity, OrderIdValue> Writer => writer;

    public ISyncBinaryColumns<OrderIdValue>? BinaryColumns => null;

    public Task<byte[]?> GetChangeCeilingAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<byte[]?>(null);

    public async Task<SyncChangeBatch<SyncvoOrderEntity>> GetChangesAsync(
        byte[]? anchor,
        byte[]? ceiling,
        int batchSize,
        CancellationToken cancellationToken = default
    )
    {
        var rows = await serverSqlExecutor.QueryBySqlAsync<SyncvoOrderEntity>(
            ChangesSql,
            new
            {
                anchor,
                ceiling,
                batchSize,
            },
            cancellationToken
        );

        return new SyncChangeBatch<SyncvoOrderEntity>(rows, rows.Count >= batchSize);
    }

    public Task<IReadOnlyList<OrderIdValue>> GetAllKeysAsync(
        CancellationToken cancellationToken = default
    ) =>
        serverSqlExecutor.QueryProjectionBySqlAsync<OrderIdValue>(KeysSql, null, cancellationToken);
}
