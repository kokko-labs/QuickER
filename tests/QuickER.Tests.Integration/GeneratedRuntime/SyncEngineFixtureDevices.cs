using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using S = QuickER.Tests.GeneratedSyncFixture;
using SS = QuickER.Tests.GeneratedSyncFixture.Repositories.Sqlite;
using V = QuickER.Tests.GeneratedSyncVoFixture;
using VS = QuickER.Tests.GeneratedSyncVoFixture.Repositories.Sqlite;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>SyncFixture の同期する 4 表のサーバーの差分ソース（記述子の外部キーの順）</summary>
/// <param name="Orders">注文</param>
/// <param name="Markers">マーカー（NULL を許す一意制約）</param>
/// <param name="Lines">注文の明細</param>
/// <param name="Notes">注文のメモ（版なし）</param>
internal sealed record SyncFixtureServerSources(
    S.ISyncServerSource<S.SyncOrderEntity, int> Orders,
    S.ISyncServerSource<S.SyncMarkerEntity, int> Markers,
    S.ISyncServerSource<S.SyncOrderLineEntity, int> Lines,
    S.ISyncServerSource<S.SyncNoteEntity, int> Notes
)
{
    /// <summary>
    /// DI に登録した差分ソース（<c>AddGeneratedDirectSyncSources</c> か <c>AddGeneratedHttpSyncSources</c>）を 4 表とも引く
    /// </summary>
    /// <param name="provider">差分ソースを登録したサービスプロバイダ</param>
    public static SyncFixtureServerSources Resolve(IServiceProvider provider) =>
        new(
            provider.GetRequiredService<S.ISyncServerSource<S.SyncOrderEntity, int>>(),
            provider.GetRequiredService<S.ISyncServerSource<S.SyncMarkerEntity, int>>(),
            provider.GetRequiredService<S.ISyncServerSource<S.SyncOrderLineEntity, int>>(),
            provider.GetRequiredService<S.ISyncServerSource<S.SyncNoteEntity, int>>()
        );
}

/// <summary>
/// SyncFixture の端末で新しいエンジン（<c>TrackedSyncEngine</c>）を組む口。端末の DB は同期のトリガーの端末
/// （<see cref="SyncTriggerHarness"/>）で作り、4 表に <c>SyncLocalTracking.Ensure</c> でトリガーと状態の表を張る。
/// </summary>
/// <remarks>
/// 組み方は同期のモデルの新しいエンジンの端末（<see cref="SyncEngineFixedHarness"/>）と同じ：開くたびの確認
/// （<c>SyncLocalTrackingGuard</c>）で包んだ接続ファクトリ（<c>ObservedSqlConnectionFactory</c>）から
/// <c>SqliteLocalRowStore</c> とリポジトリを作り、4 表の <c>TrackedSyncTable</c> でエンジンを組む。リポジトリは記録の
/// デコレータで包まない（記録はトリガーだけが行う）。サーバー役は呼び出し側が差分ソースで渡す（SQLite の代役・実 SQL Server の
/// 直結・HTTP のどれでもよい）。
/// </remarks>
internal sealed class SyncFixtureEngineDevice : IDisposable
{
    private SyncFixtureEngineDevice(SyncTriggerHarness local)
    {
        Local = local;
    }

    /// <summary>端末の DB（トリガーの端末。行の状態の表を読む）</summary>
    public SyncTriggerHarness Local { get; }

    /// <summary>端末のローカルの行を読み書きする口</summary>
    public SS.SqliteLocalRowStore Rows { get; private set; } = null!;

    /// <summary>端末の注文のリポジトリ（書き込みはトリガーが記録する）</summary>
    public SS.SyncOrderRepository Orders { get; private set; } = null!;

    /// <summary>端末のマーカーのリポジトリ</summary>
    public SS.SyncMarkerRepository Markers { get; private set; } = null!;

    /// <summary>端末の明細のリポジトリ</summary>
    public SS.SyncOrderLineRepository Lines { get; private set; } = null!;

    /// <summary>端末のエンジン</summary>
    public S.TrackedSyncEngine Engine { get; private set; } = null!;

    /// <summary>端末を作り、渡した差分ソースで 4 表のエンジンを組む</summary>
    /// <param name="sources">サーバーの差分ソース</param>
    /// <param name="ct">キャンセルトークン</param>
    public static async Task<SyncFixtureEngineDevice> CreateAsync(
        SyncFixtureServerSources sources,
        CancellationToken ct
    )
    {
        var local = await SyncTriggerHarness.CreateAsync(SyncTriggerFixture.Sync, null, ct);

        try
        {
            _ = local.Ensure(local.AllTableNames);
            var guard = new S.SyncLocalTrackingGuard(S.GeneratedSyncTables.All);
            var factory = new SS.ObservedSqlConnectionFactory(
                new LightJournalSyncConnectionFactory(local.ConnectionString),
                guard.OnStateChange
            );
            var device = new SyncFixtureEngineDevice(local)
            {
                Rows = new SS.SqliteLocalRowStore(factory),
                Orders = new SS.SyncOrderRepository(factory),
                Markers = new SS.SyncMarkerRepository(factory),
                Lines = new SS.SyncOrderLineRepository(factory),
            };
            device.Engine = new S.TrackedSyncEngine([
                new S.TrackedSyncTable<S.SyncOrderEntity, int>(
                    S.GeneratedSyncTables.SyncOrder,
                    sources.Orders,
                    device.Rows
                ),
                new S.TrackedSyncTable<S.SyncMarkerEntity, int>(
                    S.GeneratedSyncTables.SyncMarker,
                    sources.Markers,
                    device.Rows
                ),
                new S.TrackedSyncTable<S.SyncOrderLineEntity, int>(
                    S.GeneratedSyncTables.SyncOrderLine,
                    sources.Lines,
                    device.Rows
                ),
                new S.TrackedSyncTable<S.SyncNoteEntity, int>(
                    S.GeneratedSyncTables.SyncNote,
                    sources.Notes,
                    device.Rows
                ),
            ]);

            return device;
        }
        catch
        {
            local.Dispose();
            throw;
        }
    }

    /// <summary>未送信の行（行の状態の表の送る印が立っている行）</summary>
    /// <param name="ct">キャンセルトークン</param>
    public async Task<IReadOnlyList<SyncTriggerRowState>> ReadUnsentAsync(CancellationToken ct) =>
        (await Local.ReadRowsAsync(ct)).Where(row => row.Dirty == 1).ToList();

    /// <summary>接続のプールを放し、端末の DB を消す</summary>
    public void Dispose() => Local.Dispose();
}

/// <summary>
/// SyncVoFixture の端末で新しいエンジン（<c>TrackedSyncEngine</c>）を組む口。組み方は <see cref="SyncFixtureEngineDevice"/>
/// と同じで、エンジンは注文の表だけで組む（明細の表もトリガーは張る）。
/// </summary>
internal sealed class SyncVoFixtureEngineDevice : IDisposable
{
    private SyncVoFixtureEngineDevice(SyncTriggerHarness local)
    {
        Local = local;
    }

    /// <summary>端末の DB（トリガーの端末。行の状態の表を読む）</summary>
    public SyncTriggerHarness Local { get; }

    /// <summary>端末の注文のリポジトリ（書き込みはトリガーが記録する）</summary>
    public VS.SyncvoOrderRepository Orders { get; private set; } = null!;

    /// <summary>端末のエンジン（注文の表だけ）</summary>
    public V.TrackedSyncEngine Engine { get; private set; } = null!;

    /// <summary>端末を作り、渡した注文の差分ソースでエンジンを組む</summary>
    /// <param name="orders">サーバーの注文の差分ソース</param>
    /// <param name="ct">キャンセルトークン</param>
    public static async Task<SyncVoFixtureEngineDevice> CreateAsync(
        V.ISyncServerSource<V.SyncvoOrderEntity, V.OrderIdValue> orders,
        CancellationToken ct
    )
    {
        var local = await SyncTriggerHarness.CreateAsync(SyncTriggerFixture.SyncVo, null, ct);

        try
        {
            _ = local.Ensure(local.AllTableNames);
            var guard = new V.SyncLocalTrackingGuard(V.GeneratedSyncTables.All);
            var factory = new VS.ObservedSqlConnectionFactory(
                new LightJournalSyncVoConnectionFactory(local.ConnectionString),
                guard.OnStateChange
            );
            var rows = new VS.SqliteLocalRowStore(factory);
            var device = new SyncVoFixtureEngineDevice(local)
            {
                Orders = new VS.SyncvoOrderRepository(factory),
            };
            device.Engine = new V.TrackedSyncEngine([
                new V.TrackedSyncTable<V.SyncvoOrderEntity, V.OrderIdValue>(
                    V.GeneratedSyncTables.SyncvoOrder,
                    orders,
                    rows
                ),
            ]);

            return device;
        }
        catch
        {
            local.Dispose();
            throw;
        }
    }

    /// <summary>未送信の行（行の状態の表の送る印が立っている行）</summary>
    /// <param name="ct">キャンセルトークン</param>
    public async Task<IReadOnlyList<SyncTriggerRowState>> ReadUnsentAsync(CancellationToken ct) =>
        (await Local.ReadRowsAsync(ct)).Where(row => row.Dirty == 1).ToList();

    /// <summary>接続のプールを放し、端末の DB を消す</summary>
    public void Dispose() => Local.Dispose();
}

/// <summary>
/// SyncFixture のサーバー役（2 つ目の SQLite の DB）。版の採番と版ガードは <see cref="SyncTestServerRepository{TEntity, TKey}"/>、
/// blob の書き込みで版を進めるのは <see cref="SyncTestOrderBinaryColumns"/>、差分ソースは <see cref="SyncTestServerSources"/> の
/// 組み立てを使う（<see cref="SyncRuntimeTestsBase"/> のサーバー役と同じ部品）。
/// </summary>
internal sealed class SyncFixtureTestServer : IDisposable
{
    private readonly SqliteTempDatabase _db;

    private SyncFixtureTestServer(SqliteTempDatabase db)
    {
        _db = db;
    }

    /// <summary>サーバー役の生 SQL の実行器</summary>
    public S.ISqlExecutor Sql { get; private set; } = null!;

    /// <summary>サーバー役の注文のリポジトリ（版を採番し楽観排他を掛ける）</summary>
    public S.IRepository<S.SyncOrderEntity, int> Orders { get; private set; } = null!;

    /// <summary>サーバー役の明細のリポジトリ（版を採番し楽観排他を掛ける）</summary>
    public S.IRepository<S.SyncOrderLineEntity, int> Lines { get; private set; } = null!;

    /// <summary>サーバー役の注文の除外列のアクセサ（blob の書き込みで版を進める）</summary>
    public S.ISyncBinaryColumns<int> OrderBlobs { get; private set; } = null!;

    /// <summary>4 表の差分ソース</summary>
    public SyncFixtureServerSources Sources { get; private set; } = null!;

    /// <summary>サーバー役を作る（方言変換した図のスキーマを当てる）</summary>
    /// <param name="ct">キャンセルトークン</param>
    public static async Task<SyncFixtureTestServer> CreateAsync(CancellationToken ct)
    {
        var db = SqliteTempDatabase.Create();

        try
        {
            await db.ApplyDdlAsync(S.SyncFixtureDefinition.BuildSqliteMirror(), ct);
            var versions = new SyncTestVersionSequence();
            var factory = new LightJournalSyncConnectionFactory(db.ReadWriteCreateConnectionString);
            var sql = new SS.SqlExecutor(factory);
            var ordersRaw = new SS.SyncOrderRepository(factory);
            var server = new SyncFixtureTestServer(db)
            {
                Sql = sql,
                OrderBlobs = new SyncTestOrderBinaryColumns(ordersRaw, sql, versions),
                Orders = new SyncTestServerRepository<S.SyncOrderEntity, int>(
                    ordersRaw,
                    versions,
                    entity => entity.OrderId,
                    entity => entity.RowVer,
                    (entity, version) => entity.RowVer = version
                ),
                Lines = new SyncTestServerRepository<S.SyncOrderLineEntity, int>(
                    new SS.SyncOrderLineRepository(factory),
                    versions,
                    entity => entity.LineId,
                    entity => entity.RowVer,
                    (entity, version) => entity.RowVer = version
                ),
            };
            var markers = new SyncTestServerRepository<S.SyncMarkerEntity, int>(
                new SS.SyncMarkerRepository(factory),
                versions,
                entity => entity.MarkerId,
                entity => entity.RowVer,
                (entity, version) => entity.RowVer = version
            );
            server.Sources = new SyncFixtureServerSources(
                SyncTestServerSources.CreateOrders(sql, server.Orders, server.OrderBlobs),
                SyncTestServerSources.CreateMarkers(sql, markers),
                SyncTestServerSources.CreateLines(sql, server.Lines),
                SyncTestServerSources.CreateNotes(sql, new SS.SyncNoteRepository(factory))
            );

            return server;
        }
        catch
        {
            db.Dispose();
            throw;
        }
    }

    /// <summary>一時の DB を消す</summary>
    public void Dispose() => _db.Dispose();
}

/// <summary>
/// SyncVoFixture のサーバー役（2 つ目の SQLite の DB）。書き込みの面と差分ソースは <see cref="SyncVoRuntimeTests"/> のサーバー役
/// （<see cref="SyncVoRuntimeTests.SyncVoTestServerWriter"/>・<see cref="SyncVoRuntimeTests.SyncVoTestServerSource"/>）を使う。
/// </summary>
internal sealed class SyncVoFixtureTestServer : IDisposable
{
    private readonly SqliteTempDatabase _db;

    private SyncVoFixtureTestServer(SqliteTempDatabase db)
    {
        _db = db;
    }

    /// <summary>サーバー役の素の注文のリポジトリ（観測用）</summary>
    public V.ISyncvoOrderRepository OrdersRaw { get; private set; } = null!;

    /// <summary>サーバー役の書き込みの面（版を採番し楽観排他を掛ける）</summary>
    public V.IRemoteRepository<V.SyncvoOrderEntity, V.OrderIdValue> Writer { get; private set; } =
        null!;

    /// <summary>注文の差分ソース</summary>
    public V.ISyncServerSource<V.SyncvoOrderEntity, V.OrderIdValue> Orders { get; private set; } =
        null!;

    /// <summary>サーバー役を作る（方言変換した図のスキーマを当てる）</summary>
    /// <param name="ct">キャンセルトークン</param>
    public static async Task<SyncVoFixtureTestServer> CreateAsync(CancellationToken ct)
    {
        var db = SqliteTempDatabase.Create();

        try
        {
            await db.ApplyDdlAsync(V.SyncVoFixtureDefinition.BuildSqliteMirror(), ct);
            var factory = new LightJournalSyncVoConnectionFactory(
                db.ReadWriteCreateConnectionString
            );
            var ordersRaw = new VS.SyncvoOrderRepository(factory);
            var writer = new SyncVoRuntimeTests.SyncVoTestServerWriter(ordersRaw);

            return new SyncVoFixtureTestServer(db)
            {
                OrdersRaw = ordersRaw,
                Writer = writer,
                Orders = new SyncVoRuntimeTests.SyncVoTestServerSource(
                    new VS.SqlExecutor(factory),
                    writer
                ),
            };
        }
        catch
        {
            db.Dispose();
            throw;
        }
    }

    /// <summary>一時の DB を消す</summary>
    public void Dispose() => _db.Dispose();
}
