using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using QuickER.Tests.GeneratedSyncFixture;
using QuickER.Tests.GeneratedSyncFixture.Repositories.Sqlite;
using Xunit;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// 同期支援のパリティスイートを<b>直結</b>の差分ソースで流す派生（サーバー役も同一プロセスの SQLite）。
/// </summary>
/// <remarks>
/// 差分ソースはテスト側の <see cref="SyncTestServerSource{TEntity, TKey}"/>＝生成される直結実装の SQL Server 版と
/// 同じ意味論を SQLite の語彙で書いたもの。生成された直結実装そのものは <c>SyncSqlServerRuntimeTests</c>（Docker）が
/// 実 SQL Server に対して動かす。共通シナリオに加えて、転送に依らないエンジン自身の安全網もここで見る。
/// </remarks>
[Trait("Category", "Integration")]
public sealed class SyncSqliteRuntimeTests : SyncRuntimeTestsBase
{
    /// <inheritdoc />
    protected override Task<(
        ISyncServerSource<SyncOrderEntity, int> Orders,
        ISyncServerSource<SyncOrderLineEntity, int> Lines,
        ISyncServerSource<SyncNoteEntity, int> Notes
    )> CreateServerSourcesAsync() =>
        Task.FromResult<(
            ISyncServerSource<SyncOrderEntity, int>,
            ISyncServerSource<SyncOrderLineEntity, int>,
            ISyncServerSource<SyncNoteEntity, int>
        )>((CreateOrderTestSource(), CreateLineTestSource(), CreateNoteTestSource()));

    /// <summary>
    /// 再開点が進まないまま「まだ続きがある」と言い続ける差分ソースは、無限ループではなく例外で止まる。
    /// </summary>
    /// <remarks>
    /// <para>
    /// アンカーを無視するソース（受け取った再開点で絞らない実装・転送で anchor を落とすサーバー）や、版列が空の
    /// まま降りてくる行は、どちらも「適用したのに MAX が動かない」という同じ形になる。ダウンロードの終了条件は
    /// アンカーが進むことに依存しているため、これを検出しないと同じバッチを永久に適用し続ける（実際に、
    /// サーバー側で anchor を捨てる mutation を入れるとテストがハングすることを確認した）。
    /// </para>
    /// <para>
    /// 待てば直る状態ではなく実装の欠陥なので、ここは再試行でも打ち切りでもなく即座の失敗にしてある。
    /// </para>
    /// </remarks>
    [Fact(
        DisplayName = "[Sync] 再開点が進まない差分ソースはハングせず例外で止まる（エンジンの安全網）"
    )]
    public async Task NonAdvancingSource_FailsInsteadOfLooping()
    {
        await SeedServerAsync(1, "alice", 11, "widget");
        await SeedServerAsync(2, "bob", 12, "gadget");

        // アンカーを無視して常に先頭バッチを返し、「まだ続きがある」と言い続けるソース
        var stalled = new StalledOrderSource(CreateOrderTestSource());
        var table = new SyncTable<SyncOrderEntity, int>(
            LocalOrders,
            LocalSql,
            stalled,
            GeneratedSyncTables.SyncOrder
        );

        var act = async () => await table.DownloadAsync(null, 1, false, Ct);

        var exception = await act.Should().ThrowAsync<System.InvalidOperationException>();
        exception.Which.Message.Should().Contain("sync_orders");
        exception.Which.Message.Should().Contain("not making progress");
    }

    /// <summary>
    /// 版列が空のまま降りてくる行に対して、洗い替えはハングせず例外で止まる。
    /// </summary>
    /// <remarks>
    /// 洗い替えのカーソルはバッチ末尾の行が持つ版で進むため、版が空の行が返ると再開点が動かず、同じバッチを
    /// 取り続ける形になる（実体は主キー重複で落ちるが、原因の分からない失敗になる）。ダウンロード側の
    /// 進捗ガードと同じ理由で、待てば直る状態ではなく実装の欠陥として即座に失敗させる。
    /// </remarks>
    [Fact(
        DisplayName = "[Sync] 版を持たない行を返す差分ソースでは洗い替えが例外で止まる（安全網）"
    )]
    public async Task Refresh_WithVersionlessRows_FailsInsteadOfLooping()
    {
        await SeedServerAsync(1, "alice", 11, "widget");
        await SeedServerAsync(2, "bob", 12, "gadget");

        var versionless = new VersionlessOrderSource(CreateOrderTestSource());
        var table = new SyncTable<SyncOrderEntity, int>(
            LocalOrders,
            LocalSql,
            versionless,
            GeneratedSyncTables.SyncOrder
        );

        var act = async () => await table.RefreshAsync(null, 1, false, Ct);

        var exception = await act.Should().ThrowAsync<System.InvalidOperationException>();
        exception.Which.Message.Should().Contain("sync_orders");
        exception.Which.Message.Should().Contain("not making progress");
    }

    /// <summary>
    /// ジャーナルを記録する Repository の Save フックの中から同じローカル DB の同期を呼ぶと、その場で
    /// InvalidOperationException になり、保存もロールバックされる。
    /// </summary>
    /// <remarks>
    /// 書き込みはローカルの書き込みロックを持ったまま Save フックを呼ぶ。そこから同期を始めると、同期は実行用
    /// ロックを取ってから書き込みロックを待つことになり、別の同期が走っていれば相手は実行用ロックを持ったまま
    /// 同じ書き込みロックを待つ＝互いに待ち合って、どの待ち時間にも届かず止まる。別の同期が走っているかどうかに
    /// 関係なく同じ例外で断るため、この配置は 1 本でも 2 本でも必ず即座に失敗する（ここでは 1 本の形で固定する）。
    /// </remarks>
    [Fact(
        DisplayName = "[Sync/並行] Save フックの中から呼んだ同期は即座に InvalidOperationException で断られ保存も戻る"
    )]
    public async Task SyncFromInsideSaveHook_IsRefusedAndRollsBack()
    {
        await SeedServerAsync(1, "alice", 11, "widget");

        var hook = new SyncInsideHook(() => Engine);
        var orders = new JournalingSyncOrderRepository(
            new SyncOrderRepository(
                new LightJournalSyncConnectionFactory(LocalConnectionString),
                new SaveHookRegistry().Add<SyncOrderEntity>(hook)
            ),
            Journal
        );

        var order = new SyncOrderEntity { OrderId = 50, CustomerName = "hooked" };
        order.MarkAdded();
        var act = async () => await orders.SaveAsync(order, cancellationToken: Ct);

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .Which.Message.Should()
            .Contain("cannot be started from inside a write");
        (await LocalOrdersRaw.GetByIdAsync(50, Ct)).Should().BeNull("保存はロールバックされる");
    }

    /// <summary>保存の直後（コミット前）に同期を呼ぶ Save フック</summary>
    private sealed class SyncInsideHook(Func<SyncEngine> engine) : ISaveHook<SyncOrderEntity>
    {
        public async Task AfterSaveAsync(
            SyncOrderEntity entity,
            SaveOperation operation,
            ISaveHookContext context,
            CancellationToken cancellationToken = default
        ) => await engine().SyncAsync(cancellationToken: cancellationToken);
    }

    /// <summary>受け取ったアンカーを捨てて常に先頭から返す差分ソース（エンジンの安全網を確かめるための壊れた実装）</summary>
    private sealed class StalledOrderSource(ISyncServerSource<SyncOrderEntity, int> inner)
        : ISyncServerSource<SyncOrderEntity, int>
    {
        public IRemoteRepositoryCore<SyncOrderEntity, int> Writer => inner.Writer;

        public ISyncBinaryColumns<int>? BinaryColumns => inner.BinaryColumns;

        public Task<byte[]?> GetChangeCeilingAsync(CancellationToken cancellationToken = default) =>
            inner.GetChangeCeilingAsync(cancellationToken);

        public async Task<SyncChangeBatch<SyncOrderEntity>> GetChangesAsync(
            byte[]? anchor,
            byte[]? ceiling,
            int batchSize,
            CancellationToken cancellationToken = default
        )
        {
            var batch = await inner.GetChangesAsync(null, ceiling, batchSize, cancellationToken);

            return new SyncChangeBatch<SyncOrderEntity>(batch.Rows, HasMore: true);
        }

        public Task<IReadOnlyList<int>> GetAllKeysAsync(
            CancellationToken cancellationToken = default
        ) => inner.GetAllKeysAsync(cancellationToken);
    }

    /// <summary>版列を落として返す差分ソース（洗い替えのカーソルが進まなくなる形を作る壊れた実装）</summary>
    private sealed class VersionlessOrderSource(ISyncServerSource<SyncOrderEntity, int> inner)
        : ISyncServerSource<SyncOrderEntity, int>
    {
        public IRemoteRepositoryCore<SyncOrderEntity, int> Writer => inner.Writer;

        public ISyncBinaryColumns<int>? BinaryColumns => inner.BinaryColumns;

        public Task<byte[]?> GetChangeCeilingAsync(CancellationToken cancellationToken = default) =>
            inner.GetChangeCeilingAsync(cancellationToken);

        public async Task<SyncChangeBatch<SyncOrderEntity>> GetChangesAsync(
            byte[]? anchor,
            byte[]? ceiling,
            int batchSize,
            CancellationToken cancellationToken = default
        )
        {
            var batch = await inner.GetChangesAsync(anchor, ceiling, batchSize, cancellationToken);

            foreach (var row in batch.Rows)
            {
                row.RowVer = null;
            }

            return batch;
        }

        public Task<IReadOnlyList<int>> GetAllKeysAsync(
            CancellationToken cancellationToken = default
        ) => inner.GetAllKeysAsync(cancellationToken);
    }
}
