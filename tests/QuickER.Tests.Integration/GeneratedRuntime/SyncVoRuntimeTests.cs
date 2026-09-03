using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using QuickER.Tests.GeneratedSyncVoFixture;
using QuickER.Tests.GeneratedSyncVoFixture.Repositories.Sqlite;
using QuickER.Tests.Integration;
using Xunit;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// 同期支援 × 値オブジェクト生成の交差を実行時に通すスイート（サーバー役も SQLite＝Docker 不要）。
/// </summary>
/// <remarks>
/// <para>
/// 焦点は「ミラー版が VO 型のとき、その読み出しが実行時 null に耐えること」の 1 点。図の行バージョン列は
/// <b>NOT NULL</b> 宣言だが、同期の文脈では実体が null になる経路が 2 つ構造的に存在する:
/// </para>
/// <list type="number">
///   <item>ローカル（SQLite）側のミラー列は方言変換で NULL 許容へ落ちる＝まだ一度も上げていない行の版は空</item>
///   <item>グラフ削除の記録は DB を経由せずインメモリ実体から読む＝キーだけのスタブでは未設定</item>
/// </list>
/// <para>
/// どちらも「宣言が NOT NULL なら <c>.Value</c> で読める」という前提を裏切るため、生成式が列の NULL 許容宣言で
/// 出し分けていると NullReferenceException になる。VO 無効の <see cref="SyncRuntimeTestsBase"/> はこの形を
/// 一度も通らない（素の <c>byte[]</c> は型なりに null を運べる）。
/// </para>
/// <para>
/// 同期エンジンそのもの（差分ダウンロード・削除伝搬・競合の 4 象限・洗い替え）は VO と直交で、共通基底の
/// パリティスイートが押さえている。ここは交差の 2 経路だけを見る小さな独立クラスに留める。
/// </para>
/// </remarks>
[Trait("Category", "Integration")]
public sealed class SyncVoRuntimeTests : IAsyncLifetime
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private readonly SqliteTempDatabase _server = SqliteTempDatabase.Create();
    private readonly SqliteTempDatabase _local = SqliteTempDatabase.Create();

    private ISyncvoOrderRepository _serverOrdersRaw = null!;
    private SyncVoTestServerWriter _serverWriter = null!;
    private ISyncvoOrderRepository _localOrdersRaw = null!;
    private ISyncvoOrderRepository _localOrders = null!;
    private SyncJournal _journal = null!;
    private SyncEngine _engine = null!;

    /// <summary>両 DB のスキーマを作り、同期の構成部品を手で組み立てる（DI 登録の中身と同じ組み方）</summary>
    public async ValueTask InitializeAsync()
    {
        await _server.ApplyDdlAsync(SyncVoFixtureDefinition.BuildSqliteMirror(), Ct);
        await _local.ApplyDdlAsync(SyncVoFixtureDefinition.BuildSqliteMirror(), Ct);

        var serverFactory = new SqlConnectionFactory(_server.ReadWriteCreateConnectionString);
        var localFactory = new SqlConnectionFactory(_local.ReadWriteCreateConnectionString);
        var serverSql = new SqlExecutor(serverFactory);
        var localSql = new SqlExecutor(localFactory);

        _serverOrdersRaw = new SyncvoOrderRepository(serverFactory);
        _serverWriter = new SyncVoTestServerWriter(_serverOrdersRaw);

        _localOrdersRaw = new SyncvoOrderRepository(localFactory);
        _journal = new SyncJournal(localSql);
        await _journal.EnsureCreatedAsync(Ct);
        _localOrders = new JournalingSyncvoOrderRepository(_localOrdersRaw, _journal);

        var source = new SyncVoTestServerSource(serverSql, _serverWriter);
        _engine = new SyncEngine(
            [new SyncvoOrderSyncTable(_localOrders, localSql, source)],
            _journal
        );
    }

    /// <summary>一時 DB を破棄する</summary>
    public ValueTask DisposeAsync()
    {
        _server.Dispose();
        _local.Dispose();

        return ValueTask.CompletedTask;
    }

    private static SyncvoOrderEntity NewOrder(int id, string title) =>
        new() { OrderId = OrderIdValue.Create(id), Title = TitleValue.Create(title) };

    /// <summary>
    /// ミラー版が空のローカル新規行は、版の読み出しで落ちずに INSERT として送られる。
    /// </summary>
    /// <remarks>
    /// アップロードは最初に「ミラー版があるか」を見て挿入と更新を分ける。VO 経路の読み出しが
    /// <c>entity.RowVer.Value</c> だと、その分岐へ入る前に NullReferenceException になり、
    /// <b>オフラインで作った行が一切アップロードできない</b>（＝同期が全く機能しない）。
    /// </remarks>
    [Fact(
        DisplayName = "[Sync/VO] ミラー版が空のローカル新規行がアップロードされる（版の読み出しが null に耐える）"
    )]
    public async Task LocalInsertWithoutMirroredVersion_IsUploaded()
    {
        await _localOrders.InsertAsync(NewOrder(5, "dave"), Ct);
        (await _localOrdersRaw.GetByIdAsync(OrderIdValue.Create(5), Ct))!
            .RowVer.Should()
            .BeNull("まだ一度も上げていない行にミラーすべきサーバー版は無い");

        var result = await _engine.SyncAsync(cancellationToken: Ct);

        result.Conflicts.Should().BeEmpty();
        result.Uploaded.Should().Be(1);
        (await _serverOrdersRaw.GetByIdAsync(OrderIdValue.Create(5), Ct))!
            .Title.Value.Should()
            .Be("dave");
        (await _journal.CountPendingAsync(Ct)).Should().Be(0);
    }

    /// <summary>
    /// キーだけのスタブによるグラフ削除も、版の読み出しで落ちずに記録される。
    /// </summary>
    /// <remarks>
    /// グラフ削除の記録は DB を経由せず渡された実体から版を読むため、列宣言が NOT NULL でも
    /// 「まだ何も入っていない版プロパティ」が普通に現れる。ここで落ちると<b>削除が保存ごと失敗する</b>
    /// （記録は業務書き込みより先に走る＝ローカルの削除自体が例外で止まる）。
    /// </remarks>
    [Fact(
        DisplayName = "[Sync/VO] キーだけのスタブによるグラフ削除が記録される（版の読み出しが null に耐える）"
    )]
    public async Task GraphDeleteOfKeyOnlyStub_IsRecorded()
    {
        await _localOrders.InsertAsync(NewOrder(1, "alice"), Ct);
        await _journal.RemoveAllAsync(Ct);

        // 版もその他の列も持たないスタブ（アプリが「このキーを消す」とだけ言う形）
        var stub = new SyncvoOrderEntity { OrderId = OrderIdValue.Create(1) };
        stub.MarkRemoved();

        await _localOrders.SaveAsync(stub, cancellationToken: Ct);

        var entry = (await _journal.ReadAllAsync(Ct)).Should().ContainSingle().Subject;
        entry.TableName.Should().Be("syncvo_orders");
        entry.Operation.Should().Be(nameof(SyncJournalOperation.Delete));
        entry.KeyText.Should().Be("1");
        entry
            .OriginalRowVersion.Should()
            .BeNull("スタブは版を持たない＝版ガード無しの削除として記録される");
        (await _localOrdersRaw.GetByIdAsync(OrderIdValue.Create(1), Ct))
            .Should()
            .BeNull("記録のあとで業務削除も走っている");
    }

    /// <summary>
    /// アップロードで採番されたサーバー版が VO 型のミラー列へ書き戻り、次のランがそれを再開点にする。
    /// </summary>
    /// <remarks>
    /// 書き戻しが効いていなければアンカーが進まず、同じ行を毎回取り戻し続ける（往復が定常にならない）。
    /// </remarks>
    [Fact(DisplayName = "[Sync/VO] サーバー採番の版が VO 型のミラー列へ書き戻り往復が定常になる")]
    public async Task ServerVersion_IsMirroredBackThroughValueObject()
    {
        await _localOrders.InsertAsync(NewOrder(5, "dave"), Ct);
        await _engine.SyncAsync(cancellationToken: Ct);

        var key = OrderIdValue.Create(5);
        var mirrored = await _localOrdersRaw.GetByIdAsync(key, Ct);
        var stored = await _serverOrdersRaw.GetByIdAsync(key, Ct);
        mirrored!.RowVer.Should().NotBeNull("採番された版を書き戻さないとアンカーが進まない");
        mirrored.RowVer.Value.Should().Equal(stored!.RowVer.Value);

        // 版を持つ行の更新（版ガード付きのアップロード経路）も VO 型のまま往復する
        mirrored.Title = TitleValue.Create("dave-updated");
        await _localOrders.UpdateAsync(mirrored, cancellationToken: Ct);

        var second = await _engine.SyncAsync(cancellationToken: Ct);

        second.Conflicts.Should().BeEmpty();
        second.Uploaded.Should().Be(1);
        (await _serverOrdersRaw.GetByIdAsync(key, Ct))!.Title.Value.Should().Be("dave-updated");

        var third = await _engine.SyncAsync(cancellationToken: Ct);

        third.Uploaded.Should().Be(0);
        third.Downloaded.Should().Be(0, "自分の変更を取り戻さない（アンカーが進んでいる）");
        third.DeletedLocally.Should().Be(0);
    }

    /// <summary>
    /// サーバー役の書き込み面。版を採番し、楽観排他を <c>rowversion</c> と同じ意味論で肩代わりする。
    /// </summary>
    /// <remarks>
    /// 同期エンジンが差分ソース越しに使うのはリモート面（<see cref="IRemoteRepository{TEntity, TKey}"/>）だけ
    /// なので、サーバー役もその面だけで足りる。版が VO 型でも扱いは変わらない＝生成側と同じく
    /// <c>?.Value</c> で読み、<c>Create</c> で書く。
    /// </remarks>
    private sealed class SyncVoTestServerWriter(ISyncvoOrderRepository inner)
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
                throw SaveConflictException.Modified(
                    typeof(SyncvoOrderEntity),
                    entity.OrderId,
                    "save"
                );
            }

            Stamp(entity);

            return await inner.UpdateAsync(
                entity,
                ConcurrencyMode.ForceOverwrite,
                cancellationToken
            );
        }

        public Task<bool> DeleteAsync(
            OrderIdValue id,
            CancellationToken cancellationToken = default
        ) => inner.DeleteAsync(id, cancellationToken);

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
    /// サーバー役（2 つ目の SQLite DB）に対する差分ソース。生成される直結実装の SQL Server 版と同じ意味論を
    /// SQLite の語彙で表現する（キーが VO 型でも投影の扱いは生成側と同一）。
    /// </summary>
    private sealed class SyncVoTestServerSource(
        ISqlExecutor serverSqlExecutor,
        IRemoteRepository<SyncvoOrderEntity, OrderIdValue> writer
    ) : ISyncServerSource<SyncvoOrderEntity, OrderIdValue>
    {
        private const string ChangesSql =
            "SELECT * FROM \"syncvo_orders\" WHERE (@anchor IS NULL OR \"row_ver\" > @anchor) "
            + "AND (@ceiling IS NULL OR \"row_ver\" < @ceiling) ORDER BY \"row_ver\" LIMIT @batchSize";

        private const string KeysSql = "SELECT \"order_id\" FROM \"syncvo_orders\"";

        public IRemoteRepository<SyncvoOrderEntity, OrderIdValue> Writer => writer;

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
            serverSqlExecutor.QueryProjectionBySqlAsync<OrderIdValue>(
                KeysSql,
                null,
                cancellationToken
            );
    }
}
