using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using QuickER.Tests.GeneratedSyncFixture;
using QuickER.Tests.GeneratedSyncFixture.Repositories.Sqlite;
using QuickER.Tests.Integration;
using Xunit;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// 生成した同期支援を端から端まで動かすパリティスイートの共通基底（転送経路だけを派生が差し替える）。
/// </summary>
/// <remarks>
/// <para>
/// サーバー役は 2 つ目の SQLite DB で、版の採番と楽観排他は <see cref="SyncTestServerRepository{TEntity, TKey}"/> が
/// SQL Server の <c>rowversion</c> と同じ意味論で肩代わりする（インメモリ実装が SQL Server の代役を務めるのと
/// 同じ流儀）。これにより Docker 不在の CI でも、差分ダウンロード・アンカー導出・バッチ継続・削除伝搬・
/// ジャーナル再生・競合の 4 象限・ループ防止まで全経路が走る。
/// </para>
/// <para>
/// 派生が差し替えるのは<b>差分ソースの作り方</b>だけ＝直結（<see cref="SyncSqliteRuntimeTests"/>）と
/// HTTP（<see cref="SyncHttpRuntimeTests"/>）が<b>同一のシナリオ</b>を共有する。共通シナリオを基底の
/// <c>[Fact]</c> が持つ形は、Concurrency / SaveHook / NamedQuery と同じ構成規則（機能 × 実装先の網に
/// 穴を開けない）に従う。
/// </para>
/// <para>
/// 実 SQL Server との噛み合わせ（本物の <c>rowversion</c>・<c>MIN_ACTIVE_ROWVERSION()</c>）は
/// <c>SyncSqlServerRuntimeTests</c>（直結）と <c>SyncSqlServerHttpRuntimeTests</c>（HTTP）が別に見る。
/// </para>
/// </remarks>
[Trait("Category", "Integration")]
public abstract class SyncRuntimeTestsBase : IAsyncLifetime
{
    /// <summary>テスト全体で使うキャンセルトークン</summary>
    protected static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    /// <summary>サーバー役の一時ファイル SQLite DB</summary>
    private readonly SqliteTempDatabase _server = SqliteTempDatabase.Create();

    /// <summary>ローカル（オフライン編集側）の一時ファイル SQLite DB</summary>
    private readonly SqliteTempDatabase _local = SqliteTempDatabase.Create();

    /// <summary>サーバー役の生 SQL 実行器</summary>
    protected ISqlExecutor ServerSql { get; private set; } = null!;

    /// <summary>ローカルの生 SQL 実行器</summary>
    protected ISqlExecutor LocalSql { get; private set; } = null!;

    /// <summary>サーバー役の注文リポジトリ（版を採番し楽観排他を掛ける）</summary>
    protected IRepository<SyncOrderEntity, int> ServerOrders { get; private set; } = null!;

    /// <summary>サーバー役の明細リポジトリ</summary>
    protected IRepository<SyncOrderLineEntity, int> ServerLines { get; private set; } = null!;

    /// <summary>サーバー役の素の注文リポジトリ（除外列の Stream アクセサを直接叩く観測用）</summary>
    protected ISyncOrderRepository ServerOrdersRaw { get; private set; } = null!;

    /// <summary>サーバー役の除外列アクセサ（blob 書き込みで版を進める＝実 SQL Server の代役）</summary>
    protected ISyncBinaryColumns<int> ServerOrderBlobs { get; private set; } = null!;

    /// <summary>ローカルの素の注文リポジトリ（ジャーナル記録を通らない観測用）</summary>
    protected ISyncOrderRepository LocalOrdersRaw { get; private set; } = null!;

    /// <summary>ローカルの素の明細リポジトリ</summary>
    protected ISyncOrderLineRepository LocalLinesRaw { get; private set; } = null!;

    /// <summary>ローカルのジャーナル記録デコレータ（アプリが触る側）</summary>
    protected ISyncOrderRepository LocalOrders { get; private set; } = null!;

    /// <summary>ローカルのジャーナル記録デコレータ（明細）</summary>
    protected ISyncOrderLineRepository LocalLines { get; private set; } = null!;

    /// <summary>ローカルのジャーナル</summary>
    protected SyncJournal Journal { get; private set; } = null!;

    /// <summary>同期エンジン</summary>
    protected SyncEngine Engine { get; private set; } = null!;

    /// <summary>
    /// このテストクラスが使う差分ソースを作る（直結ならそのまま・HTTP なら in-process サーバーを起こして
    /// その向こう側へ同じソースを置く）。
    /// </summary>
    protected abstract Task<(
        ISyncServerSource<SyncOrderEntity, int> Orders,
        ISyncServerSource<SyncOrderLineEntity, int> Lines
    )> CreateServerSourcesAsync();

    /// <summary>転送経路の後始末（HTTP 派生が Kestrel とクライアントを畳む）</summary>
    protected virtual ValueTask DisposeTransportAsync() => ValueTask.CompletedTask;

    /// <summary>両 DB のスキーマを作り、同期の構成部品を手で組み立てる（DI 登録の中身と同じ組み方）</summary>
    public async ValueTask InitializeAsync()
    {
        // 版の採番列はこのテストインスタンス専用（クラス間の並列実行で版が混ざらない）
        var versions = new SyncTestVersionSequence();

        // サーバー役・ローカルとも SQLite なので、方言変換した図（rowversion → BLOB）からスキーマを作る
        var mirror = SyncFixtureDefinition.BuildSqliteMirror();
        await _server.ApplyDdlAsync(mirror, Ct);
        await _local.ApplyDdlAsync(SyncFixtureDefinition.BuildSqliteMirror(), Ct);

        var serverFactory = new SqlConnectionFactory(_server.ReadWriteCreateConnectionString);
        var localFactory = new SqlConnectionFactory(_local.ReadWriteCreateConnectionString);
        ServerSql = new SqlExecutor(serverFactory);
        LocalSql = new SqlExecutor(localFactory);

        ServerOrdersRaw = new SyncOrderRepository(serverFactory);
        ServerOrderBlobs = new SyncTestOrderBinaryColumns(ServerOrdersRaw, ServerSql, versions);

        ServerOrders = new SyncTestServerRepository<SyncOrderEntity, int>(
            ServerOrdersRaw,
            versions,
            entity => entity.OrderId,
            entity => entity.RowVer,
            (entity, version) => entity.RowVer = version
        );
        ServerLines = new SyncTestServerRepository<SyncOrderLineEntity, int>(
            new SyncOrderLineRepository(serverFactory),
            versions,
            entity => entity.LineId,
            entity => entity.RowVer,
            (entity, version) => entity.RowVer = version
        );

        LocalOrdersRaw = new SyncOrderRepository(localFactory);
        LocalLinesRaw = new SyncOrderLineRepository(localFactory);

        Journal = new SyncJournal(LocalSql);
        await Journal.EnsureCreatedAsync(Ct);

        LocalOrders = new JournalingSyncOrderRepository(LocalOrdersRaw, Journal);
        LocalLines = new JournalingSyncOrderLineRepository(LocalLinesRaw, Journal);

        var sources = await CreateServerSourcesAsync();

        // 記述子はデコレータ越しのローカルリポジトリを持つ（エンジンの書き込みは SyncSession で抑制される）
        Engine = new SyncEngine(
            [
                new SyncOrderSyncTable(LocalOrders, LocalSql, sources.Orders),
                new SyncOrderLineSyncTable(LocalLines, LocalSql, sources.Lines),
            ],
            Journal
        );
    }

    /// <summary>転送経路と一時 DB を破棄する</summary>
    public async ValueTask DisposeAsync()
    {
        await DisposeTransportAsync();
        _server.Dispose();
        _local.Dispose();
    }

    /// <summary>
    /// blob を扱わない洗い替えシナリオの既定オプション（除外列の損失を明示的に承諾する）。
    /// </summary>
    /// <remarks>
    /// この図の <c>sync_orders</c> は除外列（<c>attachment</c>）を持つため、洗い替えは既定では
    /// <c>SyncUnboundedBinaryLossException</c> で拒否される。行の作り直しだけを見るシナリオでは
    /// 「blob は捨ててよい」と明示して先へ進める（拒否そのものは専用のシナリオが確かめる）。
    /// </remarks>
    protected static SyncRefreshOptions RefreshDefaults =>
        new() { DiscardLocalUnboundedBinaries = true };

    /// <summary>サーバー役の注文差分ソース（SQLite 版）を組み立てる（直結の実体・HTTP のサーバー側登録の両方で使う）</summary>
    protected ISyncServerSource<SyncOrderEntity, int> CreateOrderTestSource() =>
        SyncTestServerSources.CreateOrders(ServerSql, ServerOrders, ServerOrderBlobs);

    /// <summary>サーバー役の明細差分ソース（SQLite 版）を組み立てる</summary>
    protected ISyncServerSource<SyncOrderLineEntity, int> CreateLineTestSource() =>
        SyncTestServerSources.CreateLines(ServerSql, ServerLines);

    /// <summary>除外列（blob）まで運ぶ同期オプション</summary>
    protected static SyncOptions BlobOptions => new() { IncludeUnboundedBinary = true };

    /// <summary>サーバー役の行へ blob を書く（実 SQL Server と同じく行の版も進む）</summary>
    protected async Task WriteServerBlobAsync(int orderId, byte[] content)
    {
        using var source = new MemoryStream(content);
        var written = await ServerOrderBlobs.WriteUnboundedBinaryAsync(
            "Attachment",
            orderId,
            source,
            content.Length,
            Ct
        );
        written.Should().BeTrue();
    }

    /// <summary>ローカルの行へ blob を書く（デコレータ経由＝ジャーナルへ記録される）</summary>
    protected async Task WriteLocalBlobAsync(int orderId, byte[] content)
    {
        using var source = new MemoryStream(content);
        var written = await LocalOrders.WriteAttachmentAsync(orderId, source, content.Length, Ct);
        written.Should().BeTrue();
    }

    /// <summary>ローカルの行へ blob を書く（素のリポジトリ経由＝ジャーナルへ記録されない観測用）</summary>
    protected async Task WriteLocalBlobUnjournaledAsync(int orderId, byte[] content)
    {
        using var source = new MemoryStream(content);
        var written = await LocalOrdersRaw.WriteAttachmentAsync(
            orderId,
            source,
            content.Length,
            Ct
        );
        written.Should().BeTrue();
    }

    /// <summary>サーバー役の blob を読む（行なし・NULL は null）</summary>
    protected async Task<byte[]?> ReadServerBlobAsync(int orderId)
    {
        using var destination = new MemoryStream();
        var present = await ServerOrderBlobs.ReadUnboundedBinaryAsync(
            "Attachment",
            orderId,
            destination,
            Ct
        );

        return present ? destination.ToArray() : null;
    }

    /// <summary>ローカルの blob を読む（行なし・NULL は null）</summary>
    protected async Task<byte[]?> ReadLocalBlobAsync(int orderId)
    {
        using var destination = new MemoryStream();
        var present = await LocalOrdersRaw.ReadAttachmentAsync(orderId, destination, Ct);

        return present ? destination.ToArray() : null;
    }

    /// <summary>サーバー役へ注文とその明細を 1 件ずつ入れる（版はラッパーが採番する）</summary>
    protected async Task SeedServerAsync(int orderId, string customer, int lineId, string product)
    {
        await ServerOrders.InsertAsync(
            new SyncOrderEntity { OrderId = orderId, CustomerName = customer },
            Ct
        );
        await ServerLines.InsertAsync(
            new SyncOrderLineEntity
            {
                LineId = lineId,
                OrderId = orderId,
                Product = product,
            },
            Ct
        );
    }

    // ---- ダウンロード ----

    /// <summary>初回はミラー列が全 NULL のため全量が降りてくる（アンカーなし＝上限まで全部）</summary>
    [Fact(DisplayName = "[Sync] 初回同期はサーバーの全行をローカルへ取り込む")]
    public async Task InitialSync_DownloadsEverything()
    {
        await SeedServerAsync(1, "alice", 11, "widget");
        await SeedServerAsync(2, "bob", 12, "gadget");

        var result = await Engine.SyncAsync(cancellationToken: Ct);

        result.Downloaded.Should().Be(4);
        result.Conflicts.Should().BeEmpty();
        (await LocalOrdersRaw.GetAllAsync(Ct)).Should().HaveCount(2);
        (await LocalLinesRaw.GetAllAsync(Ct)).Should().HaveCount(2);

        var order = await LocalOrdersRaw.GetByIdAsync(1, Ct);
        order!.CustomerName.Should().Be("alice");
        order
            .RowVer.Should()
            .NotBeNull("サーバーの版がミラー列へそのまま入る（次回のアンカーになる）");
    }

    /// <summary>2 回目以降はミラー列の MAX から導出したアンカーより新しい行だけが降りてくる</summary>
    [Fact(DisplayName = "[Sync] 2 回目はアンカー（ミラー MAX）より新しい行だけを取り込む")]
    public async Task IncrementalSync_DownloadsOnlyNewerRows()
    {
        await SeedServerAsync(1, "alice", 11, "widget");
        await Engine.SyncAsync(cancellationToken: Ct);

        // サーバー側で 1 件更新・1 件追加
        var stored = await ServerOrders.GetByIdAsync(1, Ct);
        stored!.CustomerName = "alice-updated";
        await ServerOrders.UpdateAsync(stored, cancellationToken: Ct);
        await SeedServerAsync(2, "bob", 12, "gadget");

        var result = await Engine.SyncAsync(cancellationToken: Ct);

        result
            .Downloaded.Should()
            .Be(3, "更新 1 件（注文）＋追加 2 件（注文と明細）だけが対象になる");
        (await LocalOrdersRaw.GetByIdAsync(1, Ct))!.CustomerName.Should().Be("alice-updated");
        (await LocalOrdersRaw.GetByIdAsync(2, Ct)).Should().NotBeNull();
    }

    /// <summary>何も変わっていなければ 2 回目の同期は 0 件（アンカー導出が自分の適用結果を再取得しない）</summary>
    [Fact(DisplayName = "[Sync] 変更が無ければ再同期は 0 件（アンカー導出の再開点が正しい）")]
    public async Task RepeatedSync_WithoutChanges_DownloadsNothing()
    {
        await SeedServerAsync(1, "alice", 11, "widget");
        await Engine.SyncAsync(cancellationToken: Ct);

        var second = await Engine.SyncAsync(cancellationToken: Ct);

        second.Downloaded.Should().Be(0);
        second.Uploaded.Should().Be(0);
    }

    /// <summary>そもそもサーバーに 1 行も無ければ、同期は何も動かさずに 0 件で終わる</summary>
    [Fact(DisplayName = "[Sync] 空のサーバーに対する同期は全カウント 0 で完了する")]
    public async Task EmptyServer_SyncsToZero()
    {
        var result = await Engine.SyncAsync(cancellationToken: Ct);

        result.Downloaded.Should().Be(0);
        result.Uploaded.Should().Be(0);
        result.DeletedLocally.Should().Be(0);
        result.Discarded.Should().Be(0);
        result.HasConflicts.Should().BeFalse();
        (await LocalOrdersRaw.GetAllAsync(Ct)).Should().BeEmpty();
    }

    /// <summary>
    /// 1 テーブルの差分がバッチサイズを超えると、続きがある限り繰り返し取得して全件が降りる。
    /// </summary>
    /// <remarks>
    /// 「まだ続きがある」の判定は差分ソースが返す <c>HasMore</c> で、直結では満杯のバッチ・HTTP では
    /// サーバーが応答へ載せた同じ判定が根拠になる。ここが壊れると最初のバッチで打ち切られ、残りは
    /// 「次回以降も降りてこない」わけではないものの、この 1 回の同期では静かに取りこぼされる。
    /// </remarks>
    [Fact(DisplayName = "[Sync] バッチサイズを超える差分は継続取得で全件降りる")]
    public async Task Download_ContinuesAcrossBatches()
    {
        for (var id = 1; id <= 5; id++)
        {
            await ServerOrders.InsertAsync(
                new SyncOrderEntity { OrderId = id, CustomerName = $"customer-{id}" },
                Ct
            );
        }

        var result = await Engine.SyncAsync(
            new SyncOptions { DownloadBatchSize = 2, PropagateDeletes = false },
            Ct
        );

        result.Downloaded.Should().Be(5, "2 件ずつでも続きがある限り取り切る（3 バッチ）");
        (await LocalOrdersRaw.GetAllAsync(Ct)).Should().HaveCount(5);
        (await LocalOrdersRaw.GetByIdAsync(5, Ct))!.CustomerName.Should().Be("customer-5");
    }

    /// <summary>
    /// バッチ途中で中断しても、再開点は「適用済みの最大版」なので取りこぼしも二重取得も起きない。
    /// </summary>
    /// <remarks>
    /// 中断は「1 行ずつのバッチで 1 回だけ回す」ことで再現する（バッチ確定＝ローカル 1 トランザクションの
    /// コミットなので、途中中断はバッチ境界での中断と同じ状態になる）。
    /// </remarks>
    [Fact(DisplayName = "[Sync] バッチ途中で止めても再開点は適用済みの最大版（取りこぼしなし）")]
    public async Task InterruptedDownload_ResumesFromAppliedMaximum()
    {
        await SeedServerAsync(1, "alice", 11, "widget");
        await SeedServerAsync(2, "bob", 12, "gadget");

        // バッチサイズ 1 で 1 テーブル分だけ回した状態＝途中で落ちたのと同じ
        var options = new SyncOptions { DownloadBatchSize = 1, PropagateDeletes = false };
        var partial = await Engine.DownloadAsync(options, Ct);
        partial.Downloaded.Should().Be(4, "DownloadAsync は各テーブルを最後まで drain する");

        // ここで新しい変更を入れ、通常サイズで再開しても取りこぼしが出ない
        await SeedServerAsync(3, "carol", 13, "doodad");
        var resumed = await Engine.SyncAsync(cancellationToken: Ct);

        resumed.Downloaded.Should().Be(2);
        (await LocalOrdersRaw.GetAllAsync(Ct)).Should().HaveCount(3);
    }

    /// <summary>サーバーから消えた行はキー全比較でローカルからも消える（既定 ON）</summary>
    [Fact(DisplayName = "[Sync] サーバーに無いキーの行はローカルからも削除される")]
    public async Task DeletePropagation_RemovesRowsMissingOnServer()
    {
        await SeedServerAsync(1, "alice", 11, "widget");
        await Engine.SyncAsync(cancellationToken: Ct);

        await ServerLines.DeleteAsync(11, Ct);
        await ServerOrders.DeleteAsync(1, Ct);

        var result = await Engine.SyncAsync(cancellationToken: Ct);

        result.DeletedLocally.Should().Be(2);
        (await LocalOrdersRaw.GetAllAsync(Ct)).Should().BeEmpty();
        (await LocalLinesRaw.GetAllAsync(Ct)).Should().BeEmpty();
    }

    /// <summary>削除伝搬を切ると走査ごとスキップされる（ローカルの行は残る）</summary>
    [Fact(DisplayName = "[Sync] 削除伝搬を切るとローカルの行は残る")]
    public async Task DeletePropagation_CanBeTurnedOff()
    {
        await SeedServerAsync(1, "alice", 11, "widget");
        await Engine.SyncAsync(cancellationToken: Ct);

        await ServerLines.DeleteAsync(11, Ct);
        await ServerOrders.DeleteAsync(1, Ct);

        var result = await Engine.SyncAsync(new SyncOptions { PropagateDeletes = false }, Ct);

        result.DeletedLocally.Should().Be(0);
        (await LocalOrdersRaw.GetAllAsync(Ct)).Should().HaveCount(1);
    }

    // ---- ループ防止 ----

    /// <summary>ダウンロードで適用した行はジャーナルへ記録されない（記録されると自分の変更を送り返し続ける）</summary>
    [Fact(DisplayName = "[Sync] ダウンロード適用はジャーナルへ記録されない（ループ防止）")]
    public async Task DownloadedRows_AreNotJournaled()
    {
        await SeedServerAsync(1, "alice", 11, "widget");

        await Engine.SyncAsync(cancellationToken: Ct);

        (await Journal.CountPendingAsync(Ct))
            .Should()
            .Be(0, "同期エンジン自身の書き込みは SyncSession で抑制される");
    }

    // ---- アップロード ----

    /// <summary>ローカルの更新はジャーナルに残り、次の同期でサーバーへ反映される</summary>
    [Fact(DisplayName = "[Sync] ローカルの更新がサーバーへ反映される")]
    public async Task LocalUpdate_IsUploaded()
    {
        await SeedServerAsync(1, "alice", 11, "widget");
        await Engine.SyncAsync(cancellationToken: Ct);

        var local = await LocalOrders.GetByIdAsync(1, Ct);
        local!.CustomerName = "alice-local";
        await LocalOrders.UpdateAsync(local, cancellationToken: Ct);
        (await Journal.CountPendingAsync(Ct)).Should().Be(1);

        var result = await Engine.SyncAsync(cancellationToken: Ct);

        result.Uploaded.Should().Be(1);
        result.Conflicts.Should().BeEmpty();
        (await ServerOrders.GetByIdAsync(1, Ct))!.CustomerName.Should().Be("alice-local");
        (await Journal.CountPendingAsync(Ct)).Should().Be(0, "成功したエントリは掃除される");
    }

    /// <summary>ミラー版を持たないローカル新規行は INSERT として送られ、サーバー採番の版が戻る</summary>
    [Fact(DisplayName = "[Sync] ローカル新規行は INSERT で送られサーバー版がミラーへ入る")]
    public async Task LocalInsert_IsUploadedAndMirrorsServerVersion()
    {
        await LocalOrders.InsertAsync(
            new SyncOrderEntity { OrderId = 5, CustomerName = "dave" },
            Ct
        );

        var result = await Engine.SyncAsync(cancellationToken: Ct);

        result.Uploaded.Should().Be(1);
        (await ServerOrders.GetByIdAsync(5, Ct))!.CustomerName.Should().Be("dave");
        (await LocalOrdersRaw.GetByIdAsync(5, Ct))!
            .RowVer.Should()
            .NotBeNull("採番された版をローカルへ書き戻さないとアンカーが進まない");
    }

    /// <summary>ローカルの削除は「削除時のミラー版」を根拠にサーバーでも版ガード付きで実行される</summary>
    [Fact(DisplayName = "[Sync] ローカルの削除がサーバーへ反映される（版ガード付き）")]
    public async Task LocalDelete_IsUploaded()
    {
        await SeedServerAsync(1, "alice", 11, "widget");
        await Engine.SyncAsync(cancellationToken: Ct);

        await LocalLines.DeleteAsync(11, Ct);

        var result = await Engine.SyncAsync(cancellationToken: Ct);

        result.Conflicts.Should().BeEmpty();
        (await ServerLines.GetByIdAsync(11, Ct)).Should().BeNull();
    }

    /// <summary>
    /// journal-first の副作用（業務書き込みが失敗して残った意図）はアップロード時に無害化される。
    /// </summary>
    /// <remarks>
    /// ジャーナルは「どの行が変わったか」しか持たず、送る内容は毎回ローカルの現在行から読み直す。そのため
    /// 実体の無い行のエントリは「送るものが無い」として黙って捨てられる（＝競合ではない）。
    /// </remarks>
    [Fact(
        DisplayName = "[Sync] 実体の無いジャーナルエントリは送られず破棄される（journal-first の無害化）"
    )]
    public async Task StaleJournalEntry_IsDiscarded()
    {
        // 業務書き込みが失敗した状況＝ジャーナルだけが残っている状態を直接作る
        await Journal.RecordAsync("sync_orders", "99", SyncJournalOperation.Upsert, null, Ct);
        (await Journal.CountPendingAsync(Ct)).Should().Be(1);

        var result = await Engine.SyncAsync(cancellationToken: Ct);

        result.Uploaded.Should().Be(0);
        result.Conflicts.Should().BeEmpty();
        (await ServerOrders.GetByIdAsync(99, Ct)).Should().BeNull();
        (await Journal.CountPendingAsync(Ct)).Should().Be(0, "送るものが無いエントリは掃除される");
    }

    // ---- 競合 ----

    /// <summary>既定（収集）では競合をジャーナルへ残し、両者の値を添えて報告する</summary>
    [Fact(DisplayName = "[Sync] 競合は既定で自動解決せずジャーナルへ残り構造化報告される")]
    public async Task Conflict_IsCollectedByDefault()
    {
        await SeedServerAsync(1, "alice", 11, "widget");
        await Engine.SyncAsync(cancellationToken: Ct);

        // ローカルとサーバーの双方が同じ行を編集する
        var local = await LocalOrders.GetByIdAsync(1, Ct);
        local!.CustomerName = "local-wins?";
        await LocalOrders.UpdateAsync(local, cancellationToken: Ct);

        var server = await ServerOrders.GetByIdAsync(1, Ct);
        server!.CustomerName = "server-wins?";
        await ServerOrders.UpdateAsync(server, cancellationToken: Ct);

        var result = await Engine.SyncAsync(cancellationToken: Ct);

        result.HasConflicts.Should().BeTrue();
        var conflict = result.Conflicts.Should().ContainSingle().Subject;
        conflict.TableName.Should().Be("sync_orders");
        conflict.KeyText.Should().Be("1");
        conflict.Operation.Should().Be(SyncJournalOperation.Upsert);
        conflict.Reason.Should().Be(SyncConflictReason.ModifiedOnServer);
        conflict.LocalEntity.Should().BeOfType<SyncOrderEntity>();
        conflict.ServerEntity.Should().BeOfType<SyncOrderEntity>();
        ((SyncOrderEntity)conflict.ServerEntity!).CustomerName.Should().Be("server-wins?");

        (await Journal.CountPendingAsync(Ct))
            .Should()
            .Be(1, "解決しなかったエントリはジャーナルに残る");
    }

    /// <summary>ServerWins はジャーナルを捨て、ダウンロードがサーバー行でローカルを上書きする</summary>
    [Fact(DisplayName = "[Sync] ServerWins はローカル変更を捨ててサーバー行で上書きする")]
    public async Task ServerWins_DiscardsLocalChange()
    {
        await SeedServerAsync(1, "alice", 11, "widget");
        await Engine.SyncAsync(cancellationToken: Ct);

        var local = await LocalOrders.GetByIdAsync(1, Ct);
        local!.CustomerName = "local-wins?";
        await LocalOrders.UpdateAsync(local, cancellationToken: Ct);

        var server = await ServerOrders.GetByIdAsync(1, Ct);
        server!.CustomerName = "server-wins";
        await ServerOrders.UpdateAsync(server, cancellationToken: Ct);

        var result = await Engine.SyncAsync(
            new SyncOptions { ConflictPolicy = SyncConflictPolicy.ServerWins },
            Ct
        );

        result.Conflicts.Should().BeEmpty();
        result.Discarded.Should().BeGreaterThan(0);
        (await LocalOrdersRaw.GetByIdAsync(1, Ct))!.CustomerName.Should().Be("server-wins");
        (await Journal.CountPendingAsync(Ct)).Should().Be(0);
    }

    /// <summary>LocalWins は版比較を免除して再送し、サーバー行を上書きする</summary>
    [Fact(DisplayName = "[Sync] LocalWins は版比較を免除してサーバー行を上書きする")]
    public async Task LocalWins_OverwritesServerRow()
    {
        await SeedServerAsync(1, "alice", 11, "widget");
        await Engine.SyncAsync(cancellationToken: Ct);

        var local = await LocalOrders.GetByIdAsync(1, Ct);
        local!.CustomerName = "local-wins";
        await LocalOrders.UpdateAsync(local, cancellationToken: Ct);

        var server = await ServerOrders.GetByIdAsync(1, Ct);
        server!.CustomerName = "server-loses";
        await ServerOrders.UpdateAsync(server, cancellationToken: Ct);

        var result = await Engine.SyncAsync(
            new SyncOptions { ConflictPolicy = SyncConflictPolicy.LocalWins },
            Ct
        );

        result.Conflicts.Should().BeEmpty();
        result.Uploaded.Should().Be(1);
        (await ServerOrders.GetByIdAsync(1, Ct))!.CustomerName.Should().Be("local-wins");
    }

    /// <summary>削除の競合（削除しようとした行がサーバーで更新されていた）も収集される</summary>
    [Fact(DisplayName = "[Sync] 削除しようとした行がサーバーで更新されていれば競合になる")]
    public async Task DeleteConflict_IsCollected()
    {
        await SeedServerAsync(1, "alice", 11, "widget");
        await Engine.SyncAsync(cancellationToken: Ct);

        await LocalLines.DeleteAsync(11, Ct);

        var server = await ServerLines.GetByIdAsync(11, Ct);
        server!.Product = "widget-v2";
        await ServerLines.UpdateAsync(server, cancellationToken: Ct);

        var result = await Engine.SyncAsync(cancellationToken: Ct);

        var conflict = result
            .Conflicts.Should()
            .ContainSingle(c => c.Operation == SyncJournalOperation.Delete)
            .Subject;
        conflict.TableName.Should().Be("sync_order_lines");
        conflict.Reason.Should().Be(SyncConflictReason.ModifiedOnServer);
        (await ServerLines.GetByIdAsync(11, Ct)).Should().NotBeNull("競合した削除は実行されない");
    }

    /// <summary>ローカルで作った行と同じキーがサーバーに既にあるときは重複として報告する</summary>
    [Fact(DisplayName = "[Sync] ローカル新規と同じキーがサーバーにあれば重複競合として報告する")]
    public async Task DuplicateKeyOnServer_IsReportedAsConflict()
    {
        await ServerOrders.InsertAsync(
            new SyncOrderEntity { OrderId = 7, CustomerName = "server-side" },
            Ct
        );

        // ローカルでも同じキーの行を作る（ミラー版なし＝未アップロード扱い）
        await LocalOrders.InsertAsync(
            new SyncOrderEntity { OrderId = 7, CustomerName = "local-side" },
            Ct
        );

        var conflicts = new List<SyncConflict>();
        await Engine.UploadAsync(new SyncOptions(), conflicts, Ct);

        var conflict = conflicts.Should().ContainSingle().Subject;
        conflict.Reason.Should().Be(SyncConflictReason.DuplicateOnServer);
        (await ServerOrders.GetByIdAsync(7, Ct))!
            .CustomerName.Should()
            .Be("server-side", "重複は上書きせず報告に留める");
    }

    // ---- 洗い替え（高速リフレッシュ） ----

    /// <summary>洗い替えはローカルを捨ててサーバーの全行で作り直す（ミラー版も入る）</summary>
    [Fact(DisplayName = "[Sync] 洗い替えはローカルをサーバーの全行で作り直す（ミラー版込み）")]
    public async Task Refresh_RebuildsLocalFromServer()
    {
        await SeedServerAsync(1, "alice", 11, "widget");
        await SeedServerAsync(2, "bob", 12, "gadget");

        // ローカルには「内容が食い違う行」と「サーバーに無い行」を置いておく（どちらも作り直しで消える）
        await LocalOrdersRaw.InsertAsync(
            new SyncOrderEntity { OrderId = 1, CustomerName = "stale" },
            Ct
        );
        await LocalOrdersRaw.InsertAsync(
            new SyncOrderEntity { OrderId = 99, CustomerName = "orphan" },
            Ct
        );

        var result = await Engine.RefreshAsync(RefreshDefaults, Ct);

        result.Deleted.Should().Be(2);
        result.Inserted.Should().Be(4);
        result.DiscardedChanges.Should().Be(0);
        result
            .Tables.Select(table => table.TableName)
            .Should()
            .Equal("sync_orders", "sync_order_lines");
        result.Elapsed.Should().BeGreaterThan(TimeSpan.Zero);

        var orders = await LocalOrdersRaw.GetAllAsync(Ct);
        orders.Select(order => order.OrderId).Should().BeEquivalentTo([1, 2]);
        orders.Single(order => order.OrderId == 1).CustomerName.Should().Be("alice");
        (await LocalLinesRaw.GetAllAsync(Ct)).Should().HaveCount(2);

        var server = await ServerOrders.GetByIdAsync(1, Ct);
        orders
            .Single(order => order.OrderId == 1)
            .RowVer.Should()
            .Equal(server!.RowVer, "サーバーの版がそのままミラー列へ入る");
    }

    /// <summary>洗い替えが残すミラー版がそのまま次回の再開点になる（差分同期が正しく継続する）</summary>
    [Fact(DisplayName = "[Sync] 洗い替え直後の差分同期はアンカーが導出され差分だけを取り込む")]
    public async Task Refresh_LeavesAnchorTheNextSyncResumesFrom()
    {
        await SeedServerAsync(1, "alice", 11, "widget");

        await Engine.RefreshAsync(RefreshDefaults, Ct);

        await SeedServerAsync(2, "bob", 12, "gadget");

        var result = await Engine.SyncAsync(cancellationToken: Ct);

        result.Downloaded.Should().Be(2, "洗い替えで入った版より新しい行だけが降りる");
        (await LocalOrdersRaw.GetAllAsync(Ct)).Should().HaveCount(2);
    }

    /// <summary>未送信のローカル変更があるときは既定で拒否し、件数とテーブルを構造化して報告する</summary>
    [Fact(
        DisplayName = "[Sync] 未送信のローカル変更があると洗い替えは既定で拒否される（構造化報告）"
    )]
    public async Task Refresh_RefusesWhenLocalChangesArePending()
    {
        await SeedServerAsync(1, "alice", 11, "widget");
        await Engine.SyncAsync(cancellationToken: Ct);

        var local = await LocalOrders.GetByIdAsync(1, Ct);
        local!.CustomerName = "offline-edit";
        await LocalOrders.UpdateAsync(local, cancellationToken: Ct);
        await LocalLines.DeleteAsync(11, Ct);

        var act = async () => await Engine.RefreshAsync(RefreshDefaults, Ct);

        var exception = await act.Should().ThrowAsync<SyncPendingChangesException>();
        exception.Which.PendingCount.Should().Be(2);
        exception
            .Which.PendingChanges.Should()
            .BeEquivalentTo([
                new SyncPendingChange("sync_orders", 1),
                new SyncPendingChange("sync_order_lines", 1),
            ]);
        exception.Which.Message.Should().Contain("sync_orders");

        // 拒否は「何も消す前」に起きる＝ローカルもジャーナルも手つかず
        (await LocalOrdersRaw.GetByIdAsync(1, Ct))!
            .CustomerName.Should()
            .Be("offline-edit");
        (await LocalLinesRaw.GetAllAsync(Ct)).Should().BeEmpty();
        (await Journal.CountPendingAsync(Ct)).Should().Be(2);
    }

    /// <summary>force を指定したときだけ未送信の変更を破棄して洗い替える</summary>
    [Fact(DisplayName = "[Sync] force は未送信のローカル変更を破棄して洗い替える")]
    public async Task Refresh_WithForce_DiscardsPendingChanges()
    {
        await SeedServerAsync(1, "alice", 11, "widget");
        await Engine.SyncAsync(cancellationToken: Ct);

        var local = await LocalOrders.GetByIdAsync(1, Ct);
        local!.CustomerName = "offline-edit";
        await LocalOrders.UpdateAsync(local, cancellationToken: Ct);

        var result = await Engine.RefreshAsync(
            new SyncRefreshOptions { Force = true, DiscardLocalUnboundedBinaries = true },
            Ct
        );

        result.DiscardedChanges.Should().Be(1);
        result.Inserted.Should().Be(2);
        (await Journal.CountPendingAsync(Ct)).Should().Be(0);
        (await LocalOrdersRaw.GetByIdAsync(1, Ct))!
            .CustomerName.Should()
            .Be("alice", "ローカルはサーバーの内容で作り直される");
        (await ServerOrders.GetByIdAsync(1, Ct))!
            .CustomerName.Should()
            .Be("alice", "破棄した変更はサーバーへ送られない");
    }

    /// <summary>洗い替え自身の書き込みはジャーナルへ記録されない（記録すると自分の行を送り返す）</summary>
    [Fact(DisplayName = "[Sync] 洗い替えの書き込みはジャーナルへ記録されない（ループ防止）")]
    public async Task Refresh_DoesNotJournalItsOwnWrites()
    {
        await SeedServerAsync(1, "alice", 11, "widget");
        await SeedServerAsync(2, "bob", 12, "gadget");

        await Engine.RefreshAsync(RefreshDefaults, Ct);

        (await Journal.CountPendingAsync(Ct)).Should().Be(0);

        var result = await Engine.SyncAsync(cancellationToken: Ct);
        result.Uploaded.Should().Be(0, "記録が残っていれば取り込んだ行を送り返してしまう");
    }

    /// <summary>サーバーが空なら洗い替えはローカルを空にする（作り直しの結果がそのまま出る）</summary>
    [Fact(DisplayName = "[Sync] サーバーが空なら洗い替えはローカルを空にする")]
    public async Task Refresh_WithEmptyServer_EmptiesLocal()
    {
        await SeedServerAsync(1, "alice", 11, "widget");
        await Engine.RefreshAsync(RefreshDefaults, Ct);

        await ServerLines.DeleteAsync(11, Ct);
        await ServerOrders.DeleteAsync(1, Ct);

        var result = await Engine.RefreshAsync(RefreshDefaults, Ct);

        result.Deleted.Should().Be(2);
        result.Inserted.Should().Be(0);
        (await LocalOrdersRaw.GetAllAsync(Ct)).Should().BeEmpty();
        (await LocalLinesRaw.GetAllAsync(Ct)).Should().BeEmpty();
    }

    /// <summary>
    /// 削除は子→親・書き込みは親→子。順序が逆なら SQLite の外部キー制約がその場で拒否する。
    /// </summary>
    /// <remarks>
    /// ローカル接続は FK 強制が既定 ON（生成 <c>SqlConnectionFactory</c> の既定）なので、この 1 本が
    /// 両方向の順序を同時に固定する＝親を先に消せば「子が参照している」で、子を先に入れれば
    /// 「親が居ない」で落ちる。
    /// </remarks>
    [Fact(DisplayName = "[Sync] 洗い替えは FK 順を守る（削除は子→親・書き込みは親→子）")]
    public async Task Refresh_HonorsForeignKeyOrder()
    {
        await SeedServerAsync(1, "alice", 11, "widget");
        await Engine.SyncAsync(cancellationToken: Ct);
        (await LocalLinesRaw.GetAllAsync(Ct))
            .Should()
            .HaveCount(1, "親を消す前に、それを参照する子が居る状態を作る");

        var result = await Engine.RefreshAsync(RefreshDefaults, Ct);

        result.Deleted.Should().Be(2);
        result.Inserted.Should().Be(2);
        (await LocalLinesRaw.GetByIdAsync(11, Ct))!.OrderId.Should().Be(1);
    }

    /// <summary>同期対象でないローカル専用テーブルには触れない</summary>
    [Fact(DisplayName = "[Sync] 洗い替えは同期対象外のローカル専用テーブルへ触れない")]
    public async Task Refresh_LeavesLocalOnlyTablesAlone()
    {
        await LocalSql.ExecuteSqlAsync(
            "CREATE TABLE local_notes (note_id INTEGER PRIMARY KEY, body TEXT NOT NULL)",
            null,
            Ct
        );
        await LocalSql.ExecuteSqlAsync(
            "INSERT INTO local_notes (note_id, body) VALUES (1, 'kept')",
            null,
            Ct
        );
        await SeedServerAsync(1, "alice", 11, "widget");

        await Engine.RefreshAsync(RefreshDefaults, Ct);

        (await LocalSql.ExecuteScalarSqlAsync<int>("SELECT COUNT(*) FROM local_notes", null, Ct))
            .Should()
            .Be(1, "同期対象は行バージョン列を持つテーブルだけ");
    }

    /// <summary>バッチサイズを超える行数も継続取得で取り切る（カーソルはバッチ末尾の版で進む）</summary>
    [Fact(DisplayName = "[Sync] 洗い替えはバッチサイズを超える行数も継続取得で取り切る")]
    public async Task Refresh_ContinuesAcrossBatches()
    {
        for (var id = 1; id <= 5; id++)
        {
            await ServerOrders.InsertAsync(
                new SyncOrderEntity { OrderId = id, CustomerName = $"customer-{id}" },
                Ct
            );
        }

        var result = await Engine.RefreshAsync(
            new SyncRefreshOptions { BatchSize = 2, DiscardLocalUnboundedBinaries = true },
            Ct
        );

        result.Inserted.Should().Be(5, "2 件ずつでも続きがある限り取り切る（3 バッチ）");
        (await LocalOrdersRaw.GetAllAsync(Ct)).Should().HaveCount(5);
        (await LocalOrdersRaw.GetByIdAsync(5, Ct))!.CustomerName.Should().Be("customer-5");
    }

    /// <summary>
    /// バッチサイズ 0 以下は「何も消す前」に拒否する（テーブルごとの検証だけでは全消し後に落ちる）。
    /// </summary>
    [Fact(DisplayName = "[Sync] 洗い替えのバッチサイズ 0 以下は全消しの前に拒否される")]
    public async Task Refresh_WithNonPositiveBatchSize_ThrowsBeforeDeletingAnything()
    {
        await SeedServerAsync(1, "alice", 11, "widget");
        await Engine.SyncAsync(cancellationToken: Ct);

        var act = async () =>
            await Engine.RefreshAsync(
                new SyncRefreshOptions { BatchSize = 0, DiscardLocalUnboundedBinaries = true },
                Ct
            );

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
        (await LocalOrdersRaw.GetAllAsync(Ct))
            .Should()
            .HaveCount(1, "引数の不正でローカルを空にしてはならない");
    }

    // ---- 除外列（無制限バイナリ列）: 既定＝運ばない ----

    /// <summary>既定ではサーバーの blob は降りてこない（行だけが降りる）</summary>
    [Fact(DisplayName = "[Sync/blob] 既定ではサーバーの blob は降りてこない（行だけが降りる）")]
    public async Task Default_DoesNotDownloadBlobs()
    {
        await SeedServerAsync(1, "alice", 11, "widget");
        await WriteServerBlobAsync(1, [1, 2, 3, 4]);

        var result = await Engine.SyncAsync(cancellationToken: Ct);

        result.Conflicts.Should().BeEmpty();
        (await LocalOrdersRaw.GetByIdAsync(1, Ct))!.CustomerName.Should().Be("alice");
        (await ReadLocalBlobAsync(1))
            .Should()
            .BeNull("行は降りるが、除外列は行の転送に載っていない");
    }

    /// <summary>既定ではローカルの blob は上がらない（新規行はサーバーで空のまま）</summary>
    [Fact(DisplayName = "[Sync/blob] 既定ではローカルの blob は上がらない")]
    public async Task Default_DoesNotUploadBlobs()
    {
        await LocalOrders.InsertAsync(
            new SyncOrderEntity { OrderId = 5, CustomerName = "dave" },
            Ct
        );
        await WriteLocalBlobAsync(5, [9, 9, 9]);

        var result = await Engine.SyncAsync(cancellationToken: Ct);

        result.Uploaded.Should().Be(1, "行は 1 件送られる（blob の編集は同じ行へ畳まれる）");
        (await ServerOrders.GetByIdAsync(5, Ct))!.CustomerName.Should().Be("dave");
        (await ReadServerBlobAsync(5)).Should().BeNull("除外列は行と一緒には送られない");
    }

    /// <summary>
    /// 既定の同期はローカルの blob を消さない（更新は除外列に触れない）。
    /// </summary>
    /// <remarks>
    /// 「blob は温存される」は<b>既にある行</b>についてだけ成り立つ主張で、そのローカルに存在しない行
    /// （初回に降りてくる新規行）は温存する対象を持たないため空で届く。両面を 1 本で固定する。
    /// </remarks>
    [Fact(
        DisplayName = "[Sync/blob] 既定の同期は既存行のローカル blob を消さない（新規行は空で届く）"
    )]
    public async Task Default_PreservesExistingLocalBlobs_ButNewRowsArriveEmpty()
    {
        await SeedServerAsync(1, "alice", 11, "widget");
        await Engine.SyncAsync(cancellationToken: Ct);

        // ローカルだけが持つ blob（記録は経ない＝ダウンロード側の挙動を単独で見る）
        await WriteLocalBlobUnjournaledAsync(1, [7, 7]);

        // サーバー側で行を更新（blob も別内容で置く）
        var server = await ServerOrders.GetByIdAsync(1, Ct);
        server!.CustomerName = "alice-updated";
        await ServerOrders.UpdateAsync(server, cancellationToken: Ct);
        await WriteServerBlobAsync(1, [1, 1, 1]);

        await SeedServerAsync(2, "bob", 12, "gadget");
        await WriteServerBlobAsync(2, [2, 2]);

        await Engine.SyncAsync(cancellationToken: Ct);

        (await LocalOrdersRaw.GetByIdAsync(1, Ct))!.CustomerName.Should().Be("alice-updated");
        (await ReadLocalBlobAsync(1))
            .Should()
            .Equal([7, 7], "更新は除外列に触れないので、ローカルにあったものが残る");
        (await ReadLocalBlobAsync(2)).Should().BeNull("初めて降りてきた行には温存する中身が無い");
    }

    // ---- 除外列: IncludeUnboundedBinary＝運ぶ ----

    /// <summary>含めるモードではサーバーの blob がローカルへ降りる（新規行・更新行とも）</summary>
    [Fact(DisplayName = "[Sync/blob] 含めるモードではサーバーの blob がローカルへ降りる")]
    public async Task IncludeUnboundedBinary_DownloadsBlobs()
    {
        await SeedServerAsync(1, "alice", 11, "widget");
        await WriteServerBlobAsync(1, [1, 2, 3, 4]);

        await Engine.SyncAsync(BlobOptions, Ct);

        (await ReadLocalBlobAsync(1)).Should().Equal([1, 2, 3, 4]);

        // 更新行でも運ばれる（行が降りた後に列がコピーされる）
        await WriteServerBlobAsync(1, [5, 6]);
        await Engine.SyncAsync(BlobOptions, Ct);

        (await ReadLocalBlobAsync(1)).Should().Equal([5, 6]);
    }

    /// <summary>サーバーで NULL になった blob は、ローカルでも NULL になる（古い中身が残らない）</summary>
    [Fact(DisplayName = "[Sync/blob] サーバーで NULL になった blob はローカルでも NULL になる")]
    public async Task IncludeUnboundedBinary_ClearsBlobWhenServerHasNone()
    {
        await SeedServerAsync(1, "alice", 11, "widget");
        await WriteServerBlobAsync(1, [1, 2, 3]);
        await Engine.SyncAsync(BlobOptions, Ct);
        (await ReadLocalBlobAsync(1)).Should().Equal([1, 2, 3]);

        // サーバー側で列を NULL 化（行の版も進む）
        var cleared = await ServerOrderBlobs.WriteUnboundedBinaryAsync(
            "Attachment",
            1,
            null,
            null,
            Ct
        );
        cleared.Should().BeTrue();

        await Engine.SyncAsync(BlobOptions, Ct);

        (await ReadLocalBlobAsync(1))
            .Should()
            .BeNull("コピーは「両側を揃える」ものなので、無い側に合わせて消す");
    }

    /// <summary>含めるモードではローカルの blob がサーバーへ上がる（ローカル新規行）</summary>
    [Fact(DisplayName = "[Sync/blob] 含めるモードではローカルの blob がサーバーへ上がる")]
    public async Task IncludeUnboundedBinary_UploadsBlobs()
    {
        await LocalOrders.InsertAsync(
            new SyncOrderEntity { OrderId = 5, CustomerName = "dave" },
            Ct
        );
        await WriteLocalBlobAsync(5, [9, 8, 7]);

        var result = await Engine.SyncAsync(BlobOptions, Ct);

        result.Conflicts.Should().BeEmpty();
        (await ReadServerBlobAsync(5)).Should().Equal([9, 8, 7]);
    }

    /// <summary>
    /// blob だけを差し替えた編集もアップロードされる（Write アクセサのジャーナル化）。
    /// </summary>
    /// <remarks>
    /// この編集は Insert / Update / Save / Delete のどれも通らないため、デコレータが Write アクセサを
    /// 包んでいなければ記録が残らず、サーバーには永久に届かない。
    /// </remarks>
    [Fact(DisplayName = "[Sync/blob] blob だけの編集も記録され、サーバーへ上がる")]
    public async Task BlobOnlyEdit_IsJournaledAndUploaded()
    {
        await SeedServerAsync(1, "alice", 11, "widget");
        await Engine.SyncAsync(BlobOptions, Ct);
        (await Journal.CountPendingAsync(Ct)).Should().Be(0);

        // 行の通常列は一切触らず、blob だけを差し替える
        await WriteLocalBlobAsync(1, [4, 2]);

        (await Journal.CountPendingAsync(Ct))
            .Should()
            .Be(1, "blob の書き込みも journal-first で記録される");

        var result = await Engine.SyncAsync(BlobOptions, Ct);

        result.Uploaded.Should().Be(1);
        result.Conflicts.Should().BeEmpty();
        (await ReadServerBlobAsync(1)).Should().Equal([4, 2]);
        (await Journal.CountPendingAsync(Ct)).Should().Be(0);
    }

    /// <summary>
    /// blob を上げた直後の再同期は、その行を取り直さない（エコー対策＝アップロード後のサーバー版の読み直し）。
    /// </summary>
    /// <remarks>
    /// blob の書き込みはサーバーの行の版をさらに進めるため、挿入・更新が返した版をそのままミラーへ書くと
    /// アンカーが行の現在版より下に留まり、次のダウンロードが「サーバー側の変更」として自分の変更を
    /// 取り戻し続ける。アップロードとダウンロードは同じ実行の中で連続するので、1 回目の Downloaded に既に現れる。
    /// </remarks>
    [Fact(DisplayName = "[Sync/blob] blob を上げた行はダウンロードで取り直されない（エコー対策）")]
    public async Task UploadedBlob_DoesNotEchoBackOnDownload()
    {
        await LocalOrders.InsertAsync(
            new SyncOrderEntity { OrderId = 5, CustomerName = "dave" },
            Ct
        );
        await WriteLocalBlobAsync(5, [9, 8, 7]);

        var first = await Engine.SyncAsync(BlobOptions, Ct);

        first
            .Downloaded.Should()
            .Be(0, "自分が上げた行を、同じ実行のダウンロードが取り戻してはならない");

        var second = await Engine.SyncAsync(BlobOptions, Ct);

        second.Downloaded.Should().Be(0);
        second.Uploaded.Should().Be(0);
        (await ReadServerBlobAsync(5)).Should().Equal([9, 8, 7]);
        (await ReadLocalBlobAsync(5)).Should().Equal([9, 8, 7]);
    }

    // ---- 除外列 × 洗い替え ----

    /// <summary>
    /// 除外列を持つ図の洗い替えは、行き先を明示しない限り「何も消す前に」拒否される。
    /// </summary>
    [Fact(DisplayName = "[Sync/blob] 除外列があると洗い替えは既定で拒否される（構造化報告・無傷）")]
    public async Task Refresh_RefusesWhenUnboundedBinaryWouldBeLost()
    {
        await SeedServerAsync(1, "alice", 11, "widget");
        await Engine.SyncAsync(cancellationToken: Ct);
        await WriteLocalBlobUnjournaledAsync(1, [3, 3, 3]);

        var act = async () => await Engine.RefreshAsync(cancellationToken: Ct);

        var exception = await act.Should().ThrowAsync<SyncUnboundedBinaryLossException>();
        var columns = exception.Which.Columns.Should().ContainSingle().Subject;
        columns.TableName.Should().Be("sync_orders");
        columns.ColumnNames.Should().Equal("Attachment");
        exception.Which.Message.Should().Contain("Attachment");

        // 何も消していない
        (await LocalOrdersRaw.GetAllAsync(Ct))
            .Should()
            .HaveCount(1);
        (await ReadLocalBlobAsync(1)).Should().Equal([3, 3, 3]);
    }

    /// <summary>破棄を明示すれば洗い替えは走る（blob は失われる＝それがこのフラグの意味）</summary>
    [Fact(DisplayName = "[Sync/blob] 破棄を明示した洗い替えは走り、ローカルの blob は失われる")]
    public async Task Refresh_WithDiscardFlag_RunsAndDropsBlobs()
    {
        await SeedServerAsync(1, "alice", 11, "widget");
        await Engine.SyncAsync(cancellationToken: Ct);
        await WriteLocalBlobUnjournaledAsync(1, [3, 3, 3]);

        var result = await Engine.RefreshAsync(RefreshDefaults, Ct);

        result.Inserted.Should().Be(2);
        (await LocalOrdersRaw.GetByIdAsync(1, Ct))!.CustomerName.Should().Be("alice");
        (await ReadLocalBlobAsync(1)).Should().BeNull("行ごと作り直すので blob も消える");
    }

    /// <summary>含めるモードの洗い替えは blob ごと作り直す</summary>
    [Fact(DisplayName = "[Sync/blob] 含めるモードの洗い替えは blob ごと作り直す")]
    public async Task Refresh_WithIncludeUnboundedBinary_ReloadsBlobs()
    {
        await SeedServerAsync(1, "alice", 11, "widget");
        await WriteServerBlobAsync(1, [1, 2, 3]);
        await SeedServerAsync(2, "bob", 12, "gadget");

        var result = await Engine.RefreshAsync(
            new SyncRefreshOptions { IncludeUnboundedBinary = true },
            Ct
        );

        result.Inserted.Should().Be(4);
        (await ReadLocalBlobAsync(1)).Should().Equal([1, 2, 3]);
        (await ReadLocalBlobAsync(2)).Should().BeNull("サーバー側が空の行は空のまま");

        // 洗い替えが残したミラー版から、次の差分同期がそのまま継続する
        (await Engine.SyncAsync(BlobOptions, Ct))
            .Downloaded.Should()
            .Be(0);
    }

    // ---- 記録の網羅（対の経路監査） ----

    /// <summary>直接 CRUD・一括追加・グラフ保存のいずれの入口からもジャーナルへ記録される</summary>
    [Fact(DisplayName = "[Sync] 全書き込み入口がジャーナルへ記録される（対の経路監査）")]
    public async Task EveryWriteEntryPoint_IsJournaled()
    {
        // Insert
        await LocalOrders.InsertAsync(new SyncOrderEntity { OrderId = 1, CustomerName = "a" }, Ct);

        // Update
        var loaded = await LocalOrders.GetByIdAsync(1, Ct);
        loaded!.CustomerName = "b";
        await LocalOrders.UpdateAsync(loaded, cancellationToken: Ct);

        // BulkInsert
        await LocalOrders.BulkInsertAsync(
            [new SyncOrderEntity { OrderId = 2, CustomerName = "c" }],
            Ct
        );

        // SaveAsync（単一・グラフ）
        var added = new SyncOrderEntity { OrderId = 3, CustomerName = "d" };
        added.MarkAdded();
        await LocalOrders.SaveAsync(added, cancellationToken: Ct);

        // SaveAsync（複数）
        var many = new SyncOrderEntity { OrderId = 4, CustomerName = "e" };
        many.MarkAdded();
        await LocalOrders.SaveAsync([many], cancellationToken: Ct);

        // Delete
        await LocalOrders.DeleteAsync(2, Ct);

        var entries = await Journal.ReadAllAsync(Ct);
        entries.Should().HaveCount(6, "6 つの書き込み入口すべてが 1 件ずつ記録する");
        entries
            .Select(entry => entry.KeyText)
            .Should()
            .BeEquivalentTo(["1", "1", "2", "3", "4", "2"]);
        entries
            .Should()
            .ContainSingle(entry => entry.Operation == nameof(SyncJournalOperation.Delete));
    }

    /// <summary>生 SQL は記録対象外（デコレータからは「どの行が変わったか」が読めないため）</summary>
    [Fact(DisplayName = "[Sync] 生 SQL による書き込みはジャーナルへ記録されない（既知の割り切り）")]
    public async Task RawSql_IsNotJournaled()
    {
        await LocalOrders.InsertAsync(new SyncOrderEntity { OrderId = 1, CustomerName = "a" }, Ct);
        var before = await Journal.CountPendingAsync(Ct);

        await LocalOrders.ExecuteSqlAsync(
            "UPDATE \"sync_orders\" SET \"customer_name\" = 'raw' WHERE \"order_id\" = 1",
            null,
            Ct
        );

        (await Journal.CountPendingAsync(Ct))
            .Should()
            .Be(before, "文の形はデコレータに読めないため記録できない（docs 明記の割り切り）");
    }
}
