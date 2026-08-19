using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using QuickER.Tests.GeneratedSyncFixture;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// 同期テストの「サーバー役」を SQLite の 2 つ目のデータベースで務めるための土台（版の採番列）。
/// </summary>
/// <remarks>
/// <para>
/// SQL Server の <c>rowversion</c> は DB が単調増加の版を採番し、更新時にその版で楽観排他を掛ける。SQLite には
/// その仕組みが無いため、ここでは同じ意味論（挿入・更新のたびに 8 バイト big-endian の版を採番し、
/// 更新は「読んだときの版」と一致しなければ <c>SaveConflictException</c>）をリポジトリの薄いラッパーで再現する。
/// インメモリ実装が SQL Server の代役を務めているのと同じ流儀で、Docker 不在の CI でも同期エンジンの全経路
/// （差分走査・アンカー導出・版ガード・競合分類）を通せるようにするためのもの。
/// </para>
/// <para>
/// 実 SQL Server に対する検証は <c>SyncSqlServerRuntimeTests</c>（Docker）が別に担う。こちらは「エンジンの筋」を、
/// あちらは「実際の rowversion / MIN_ACTIVE_ROWVERSION との噛み合わせ」を見る。
/// </para>
/// <para>
/// 採番列はテストクラスのインスタンスごとに持つ（静的カウンタにすると、直結と HTTP のスイートが並列に走ったとき
/// 互いの版を進め合い、版の順序に依存するシナリオが実装と無関係に落ちる）。
/// </para>
/// </remarks>
internal sealed class SyncTestVersionSequence
{
    private long _counter;

    /// <summary>次の版（8 バイト big-endian＝バイト列の辞書順が数値順と一致する）を採番する</summary>
    public byte[] Next()
    {
        var value = Interlocked.Increment(ref _counter);
        var bytes = BitConverter.GetBytes(value);

        if (BitConverter.IsLittleEndian)
        {
            Array.Reverse(bytes);
        }

        return bytes;
    }
}

/// <summary>
/// サーバー役リポジトリの薄いラッパー。書き込みのたびに版を採番し、更新・削除に版ガードを掛ける。
/// </summary>
/// <typeparam name="TEntity">エンティティ型</typeparam>
/// <typeparam name="TKey">主キー型</typeparam>
internal sealed class SyncTestServerRepository<TEntity, TKey>(
    IRepository<TEntity, TKey> inner,
    SyncTestVersionSequence versions,
    Func<TEntity, TKey> readKey,
    Func<TEntity, byte[]?> readVersion,
    Action<TEntity, byte[]?> writeVersion
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

    public async Task InsertAsync(TEntity entity, CancellationToken cancellationToken = default)
    {
        Stamp(entity);
        await inner.InsertAsync(entity, cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> UpdateAsync(
        TEntity entity,
        ConcurrencyMode mode = ConcurrencyMode.Optimistic,
        CancellationToken cancellationToken = default
    )
    {
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

    public Task<bool> DeleteAsync(TKey id, CancellationToken cancellationToken = default) =>
        inner.DeleteAsync(id, cancellationToken);

    public async Task<int> SaveAsync(
        TEntity entity,
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
            var key = readKey(entity);

            if (await inner.GetByIdAsync(key, cancellationToken).ConfigureAwait(false) is null)
            {
                // 既に無い行の削除は実 DB でも no-op（削除の意図は既に満たされている）
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
/// サーバー役（2 つ目の SQLite DB）に対する差分ソース。生成される直結実装の SQL Server 版と同じ意味論を、
/// SQLite の語彙で表現する。
/// </summary>
/// <remarks>
/// 変更の上限（<c>MIN_ACTIVE_ROWVERSION()</c> 相当）は、テストが単一スレッドで走り未コミットの書き込みが
/// 存在しないため <c>null</c>（＝上限なし）とする。「上限が効くこと」自体は実 SQL Server 側のテストが見る。
/// </remarks>
/// <typeparam name="TEntity">エンティティ型</typeparam>
/// <typeparam name="TKey">主キー型</typeparam>
internal sealed class SyncTestServerSource<TEntity, TKey>(
    ISqlExecutor serverSqlExecutor,
    IRepository<TEntity, TKey> writer,
    string changesSql,
    string keysSql,
    ISyncBinaryColumns<TKey>? binaryColumns = null
) : ISyncServerSource<TEntity, TKey>
    where TEntity : EntityBase, new()
{
    public IRemoteRepository<TEntity, TKey> Writer => writer;

    /// <summary>サーバー役の除外列アクセサ（除外列を持たないテーブルでは null＝生成された直結実装と同じ形）</summary>
    public ISyncBinaryColumns<TKey>? BinaryColumns => binaryColumns;

    public Task<byte[]?> GetChangeCeilingAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<byte[]?>(null);

    /// <summary>継続フラグの決め方は生成される直結実装と同じ（満杯のバッチ＝まだ続きがあり得る）</summary>
    public async Task<SyncChangeBatch<TEntity>> GetChangesAsync(
        byte[]? anchor,
        byte[]? ceiling,
        int batchSize,
        CancellationToken cancellationToken = default
    )
    {
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

        return new SyncChangeBatch<TEntity>(rows, rows.Count >= batchSize);
    }

    public Task<IReadOnlyList<TKey>> GetAllKeysAsync(
        CancellationToken cancellationToken = default
    ) => serverSqlExecutor.QueryProjectionBySqlAsync<TKey>(keysSql, null, cancellationToken);
}

/// <summary>
/// サーバー役（2 つ目の SQLite DB）に対する<b>版なしテーブル</b>の差分ソース。生成される直結実装の
/// SQL Server 版（キー順ページング）と同じ意味論を SQLite の語彙で表現する。
/// </summary>
/// <remarks>
/// 版なしテーブルに版の change stream は無いため、<c>GetChangesAsync</c> は生成側と同じく
/// <see cref="NotSupportedException"/>・上限（ceiling）は常に null。ダウンロードはキー昇順の全量走査で、
/// 継続フラグの決め方（満杯のバッチ＝まだ続きがあり得る）も生成側と同じ。
/// </remarks>
/// <typeparam name="TEntity">エンティティ型</typeparam>
/// <typeparam name="TKey">主キー型</typeparam>
internal sealed class SyncTestVersionlessServerSource<TEntity, TKey>(
    ISqlExecutor serverSqlExecutor,
    IRepository<TEntity, TKey> writer,
    string firstPageSql,
    string pageAfterSql,
    string keysSql
) : ISyncServerSource<TEntity, TKey>
    where TEntity : EntityBase, new()
{
    public IRemoteRepository<TEntity, TKey> Writer => writer;

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
        var rows = await serverSqlExecutor.QueryBySqlAsync<TEntity>(
            firstPageSql,
            new { batchSize },
            cancellationToken
        );

        return new SyncChangeBatch<TEntity>(rows, rows.Count >= batchSize);
    }

    public async Task<SyncChangeBatch<TEntity>> GetPageAfterAsync(
        TKey afterKey,
        int batchSize,
        CancellationToken cancellationToken = default
    )
    {
        var rows = await serverSqlExecutor.QueryBySqlAsync<TEntity>(
            pageAfterSql,
            new { afterKey, batchSize },
            cancellationToken
        );

        return new SyncChangeBatch<TEntity>(rows, rows.Count >= batchSize);
    }

    public Task<IReadOnlyList<TKey>> GetAllKeysAsync(
        CancellationToken cancellationToken = default
    ) => serverSqlExecutor.QueryProjectionBySqlAsync<TKey>(keysSql, null, cancellationToken);
}

/// <summary>
/// サーバー役をリモートエンドポイントの背後へ置くための、リモート面だけの薄いアダプタ。
/// </summary>
/// <remarks>
/// 生成されたエンドポイント（<c>MapCrud</c>）が解決するのは <c>I{Entity}RemoteRepository</c> であって
/// <c>IRepository</c> ではないため、版採番ラッパー（<see cref="SyncTestServerRepository{TEntity, TKey}"/>）を
/// そのまま DI へ載せることはできない。ここで面だけを合わせ、実体はラッパーへ委譲する
/// （＝HTTP 経由のアップロードでも版が採番され、版ガードが同じように効く）。
/// </remarks>
/// <typeparam name="TEntity">エンティティ型</typeparam>
/// <typeparam name="TKey">主キー型</typeparam>
internal abstract class SyncTestRemoteServerRepository<TEntity, TKey>(
    IRepository<TEntity, TKey> inner
) : IRemoteRepository<TEntity, TKey>
    where TEntity : EntityBase, new()
{
    public Task<TEntity?> GetByIdAsync(TKey id, CancellationToken cancellationToken = default) =>
        inner.GetByIdAsync(id, cancellationToken);

    public Task<IReadOnlyList<TEntity>> GetAllAsync(
        CancellationToken cancellationToken = default
    ) => inner.GetAllAsync(cancellationToken);

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

/// <summary>
/// サーバー役（SQLite）の差分ソースを組み立てる唯一の場所（SQL 文とその組み合わせ方の正本）。
/// </summary>
/// <remarks>
/// パリティスイートの基底（<see cref="SyncRuntimeTestsBase"/>）とベンチマーク（
/// <see cref="SyncRefreshBenchmarkRuntimeTests"/>）が同じサーバー役を使うため、SQL をどちらかへ書くと
/// 「同じサーバー役のつもりで別物を測る」ことになる。組み立てはここへ 1 本化する。
/// </remarks>
internal static class SyncTestServerSources
{
    /// <summary>注文の差分取得 SQL（生成される SQL Server 版と同じ意味論を SQLite の語彙で書いたもの）</summary>
    /// <remarks>
    /// 除外列（<c>attachment</c>）を明示的に落とす＝生成側と同じ。"*" のままだと生 SQL の
    /// opportunistic マップで blob まで降りてきてしまい、「行の転送には載らない」という意味論が崩れる。
    /// </remarks>
    public const string OrderChangesSql =
        "SELECT \"order_id\", \"customer_name\", \"row_ver\" FROM \"sync_orders\" "
        + "WHERE (@anchor IS NULL OR \"row_ver\" > @anchor) "
        + "AND (@ceiling IS NULL OR \"row_ver\" < @ceiling) ORDER BY \"row_ver\" LIMIT @batchSize";

    /// <summary>注文のキー取得 SQL</summary>
    public const string OrderKeysSql = "SELECT \"order_id\" FROM \"sync_orders\"";

    /// <summary>明細の差分取得 SQL</summary>
    public const string LineChangesSql =
        "SELECT * FROM \"sync_order_lines\" WHERE (@anchor IS NULL OR \"row_ver\" > @anchor) "
        + "AND (@ceiling IS NULL OR \"row_ver\" < @ceiling) ORDER BY \"row_ver\" LIMIT @batchSize";

    /// <summary>明細のキー取得 SQL</summary>
    public const string LineKeysSql = "SELECT \"line_id\" FROM \"sync_order_lines\"";

    /// <summary>注文の差分ソースを組み立てる</summary>
    /// <param name="serverSql">サーバー役の生 SQL 実行器</param>
    /// <param name="writer">サーバー役のリポジトリ（版採番ラッパー）</param>
    /// <param name="binaryColumns">サーバー役の除外列アクセサ（省略時は「除外列を運ばないソース」＝null）</param>
    public static ISyncServerSource<SyncOrderEntity, int> CreateOrders(
        ISqlExecutor serverSql,
        IRepository<SyncOrderEntity, int> writer,
        ISyncBinaryColumns<int>? binaryColumns = null
    ) =>
        new SyncTestServerSource<SyncOrderEntity, int>(
            serverSql,
            writer,
            OrderChangesSql,
            OrderKeysSql,
            binaryColumns
        );

    /// <summary>明細の差分ソースを組み立てる</summary>
    public static ISyncServerSource<SyncOrderLineEntity, int> CreateLines(
        ISqlExecutor serverSql,
        IRepository<SyncOrderLineEntity, int> writer
    ) =>
        new SyncTestServerSource<SyncOrderLineEntity, int>(
            serverSql,
            writer,
            LineChangesSql,
            LineKeysSql
        );

    /// <summary>メモ（版なしテーブル）の先頭ページ取得 SQL（キー昇順＝生成される SQL Server 版と同じ意味論）</summary>
    public const string NoteFirstPageSql =
        "SELECT * FROM \"sync_notes\" ORDER BY \"note_id\" LIMIT @batchSize";

    /// <summary>メモの続きページ取得 SQL（<c>@afterKey</c> より上のキーだけ）</summary>
    public const string NotePageAfterSql =
        "SELECT * FROM \"sync_notes\" WHERE \"note_id\" > @afterKey "
        + "ORDER BY \"note_id\" LIMIT @batchSize";

    /// <summary>メモのキー取得 SQL</summary>
    public const string NoteKeysSql = "SELECT \"note_id\" FROM \"sync_notes\"";

    /// <summary>メモ（版なしテーブル）の差分ソースを組み立てる（版採番ラッパーは要らない＝素のリポジトリが writer）</summary>
    public static ISyncServerSource<SyncNoteEntity, int> CreateNotes(
        ISqlExecutor serverSql,
        IRepository<SyncNoteEntity, int> writer
    ) =>
        new SyncTestVersionlessServerSource<SyncNoteEntity, int>(
            serverSql,
            writer,
            NoteFirstPageSql,
            NotePageAfterSql,
            NoteKeysSql
        );
}

/// <summary>
/// サーバー役の除外列（無制限バイナリ列）アクセサ。読みは素通し、書きは「blob の書き込みも行の版を進める」
/// という実 SQL Server の意味論を代役する。
/// </summary>
/// <remarks>
/// <para>
/// SQL Server では <c>varbinary(max)</c> 列を書けば、その行の <c>rowversion</c> も進む。SQLite にはその機構が
/// 無いため、書き込み成功のたびに版を採番して同じ行へ書き戻す（版採番ラッパーが挿入・更新でしていることの、
/// blob 版）。これが無いと「アップロード後にサーバー版を読み直す」エコー対策が何も変えないまま通ってしまい、
/// 実 SQL Server でだけ同じ行を取り直し続ける形の欠陥を CI が見逃す。
/// </para>
/// <para>
/// 直結（差分ソースの <c>BinaryColumns</c>）と HTTP（リモート面アダプタが張るバイナリエンドポイント）の
/// どちらの経路も、このクラス 1 つを通る。
/// </para>
/// </remarks>
internal sealed class SyncTestOrderBinaryColumns(
    ISyncOrderRepository repository,
    ISqlExecutor serverSqlExecutor,
    SyncTestVersionSequence versions
) : ISyncBinaryColumns<int>
{
    /// <summary>版を進める SQL（除外列は UPDATE 対象外なので、この文が blob へ触ることはない）</summary>
    private const string StampSql =
        "UPDATE \"sync_orders\" SET \"row_ver\" = @version WHERE \"order_id\" = @id";

    public IReadOnlyList<string> UnboundedBinaryColumnNames => ["Attachment"];

    public Task<bool> ReadUnboundedBinaryAsync(
        string columnName,
        int id,
        Stream destination,
        CancellationToken cancellationToken = default
    ) => repository.ReadAttachmentAsync(id, destination, cancellationToken);

    public async Task<bool> WriteUnboundedBinaryAsync(
        string columnName,
        int id,
        Stream? source,
        long? length,
        CancellationToken cancellationToken = default
    )
    {
        var written = await repository
            .WriteAttachmentAsync(id, source, length, cancellationToken)
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

/// <summary>注文テーブルのリモート面アダプタ（一意制約は図に無いため常に違反なしを返す）</summary>
/// <remarks>
/// 除外列のストリーミングエンドポイント（<c>GET/PUT/DELETE {prefix}/SyncOrder/Attachment</c>）はこの面を解決して
/// 呼ぶため、blob の読み書きも版採番つきのアクセサ（<see cref="SyncTestOrderBinaryColumns"/>）へ通す。
/// </remarks>
internal sealed class SyncTestOrderRemoteRepository(
    IRepository<SyncOrderEntity, int> inner,
    ISyncBinaryColumns<int> binaryColumns
) : SyncTestRemoteServerRepository<SyncOrderEntity, int>(inner), ISyncOrderRemoteRepository
{
    public Task<IReadOnlyList<UniquenessViolation>> CheckUniquenessAsync(
        SyncOrderEntity entity,
        CancellationToken cancellationToken = default
    ) => Task.FromResult<IReadOnlyList<UniquenessViolation>>([]);

    public Task<bool> ReadAttachmentAsync(
        int id,
        Stream destination,
        CancellationToken cancellationToken = default
    ) => binaryColumns.ReadUnboundedBinaryAsync("Attachment", id, destination, cancellationToken);

    public Task<bool> WriteAttachmentAsync(
        int id,
        Stream? source,
        long? length = null,
        CancellationToken cancellationToken = default
    ) =>
        binaryColumns.WriteUnboundedBinaryAsync(
            "Attachment",
            id,
            source,
            length,
            cancellationToken
        );
}

/// <summary>明細テーブルのリモート面アダプタ</summary>
internal sealed class SyncTestOrderLineRemoteRepository(IRepository<SyncOrderLineEntity, int> inner)
    : SyncTestRemoteServerRepository<SyncOrderLineEntity, int>(inner),
        ISyncOrderLineRemoteRepository
{
    public Task<IReadOnlyList<UniquenessViolation>> CheckUniquenessAsync(
        SyncOrderLineEntity entity,
        CancellationToken cancellationToken = default
    ) => Task.FromResult<IReadOnlyList<UniquenessViolation>>([]);
}

/// <summary>メモ（版なし）テーブルのリモート面アダプタ（版採番なし＝素のリポジトリへ委譲するだけ）</summary>
internal sealed class SyncTestNoteRemoteRepository(IRepository<SyncNoteEntity, int> inner)
    : SyncTestRemoteServerRepository<SyncNoteEntity, int>(inner),
        ISyncNoteRemoteRepository
{
    public Task<IReadOnlyList<UniquenessViolation>> CheckUniquenessAsync(
        SyncNoteEntity entity,
        CancellationToken cancellationToken = default
    ) => Task.FromResult<IReadOnlyList<UniquenessViolation>>([]);
}
