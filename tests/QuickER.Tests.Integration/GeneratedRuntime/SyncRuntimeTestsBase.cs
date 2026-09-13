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

    /// <summary>サーバー役のメモ（版なしテーブル）リポジトリ（版採番なし＝素のリポジトリそのまま）</summary>
    protected ISyncNoteRepository ServerNotes { get; private set; } = null!;

    /// <summary>ローカルの素の注文リポジトリ（ジャーナル記録を通らない観測用）</summary>
    protected ISyncOrderRepository LocalOrdersRaw { get; private set; } = null!;

    /// <summary>ローカルの素の明細リポジトリ</summary>
    protected ISyncOrderLineRepository LocalLinesRaw { get; private set; } = null!;

    /// <summary>ローカルの素のメモリポジトリ</summary>
    protected ISyncNoteRepository LocalNotesRaw { get; private set; } = null!;

    /// <summary>ローカルのジャーナル記録デコレータ（アプリが触る側）</summary>
    protected ISyncOrderRepository LocalOrders { get; private set; } = null!;

    /// <summary>ローカルのジャーナル記録デコレータ（明細）</summary>
    protected ISyncOrderLineRepository LocalLines { get; private set; } = null!;

    /// <summary>ローカルのジャーナル記録デコレータ（メモ＝版なしテーブル）</summary>
    protected ISyncNoteRepository LocalNotes { get; private set; } = null!;

    /// <summary>ローカルのジャーナル</summary>
    protected SyncJournal Journal { get; private set; } = null!;

    /// <summary>同期エンジン</summary>
    protected SyncEngine Engine { get; private set; } = null!;

    /// <summary>このテストインスタンスが使っている差分ソース（ラン内へ割り込むシナリオがデコレートする）</summary>
    private (
        ISyncServerSource<SyncOrderEntity, int> Orders,
        ISyncServerSource<SyncOrderLineEntity, int> Lines,
        ISyncServerSource<SyncNoteEntity, int> Notes
    ) _sources;

    /// <summary>
    /// このテストクラスが使う差分ソースを作る（直結ならそのまま・HTTP なら in-process サーバーを起こして
    /// その向こう側へ同じソースを置く）。
    /// </summary>
    protected abstract Task<(
        ISyncServerSource<SyncOrderEntity, int> Orders,
        ISyncServerSource<SyncOrderLineEntity, int> Lines,
        ISyncServerSource<SyncNoteEntity, int> Notes
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

        // 版なしテーブルのサーバー役は素のリポジトリ（版の採番も版ガードも存在しないのが本物の意味論）
        ServerNotes = new SyncNoteRepository(serverFactory);

        LocalOrdersRaw = new SyncOrderRepository(localFactory);
        LocalLinesRaw = new SyncOrderLineRepository(localFactory);
        LocalNotesRaw = new SyncNoteRepository(localFactory);

        Journal = new SyncJournal(LocalSql);
        await Journal.EnsureCreatedAsync(Ct);

        LocalOrders = new JournalingSyncOrderRepository(LocalOrdersRaw, Journal);
        LocalLines = new JournalingSyncOrderLineRepository(LocalLinesRaw, Journal);
        LocalNotes = new JournalingSyncNoteRepository(LocalNotesRaw, Journal);

        var sources = await CreateServerSourcesAsync();
        _sources = sources;

        // 記述子はデコレータ越しのローカルリポジトリを持つ（エンジンの書き込みは SyncSession で抑制される）。
        // 版なしテーブル（sync_notes）も同じエンジンへ登録する＝既定（Versioned）のランはそれを対象外にする
        // こと自体が既存シナリオの互換の主張になっている
        Engine = new SyncEngine(
            [
                new SyncTable<SyncOrderEntity, int>(
                    LocalOrders,
                    LocalSql,
                    sources.Orders,
                    GeneratedSyncTables.SyncOrder
                ),
                new SyncTable<SyncOrderLineEntity, int>(
                    LocalLines,
                    LocalSql,
                    sources.Lines,
                    GeneratedSyncTables.SyncOrderLine
                ),
                new VersionlessSyncTable<SyncNoteEntity, int>(
                    LocalNotes,
                    LocalSql,
                    sources.Notes,
                    GeneratedSyncTables.SyncNote
                ),
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

    /// <summary>サーバー役のメモ（版なしテーブル）差分ソース（SQLite 版）を組み立てる</summary>
    protected ISyncServerSource<SyncNoteEntity, int> CreateNoteTestSource() =>
        SyncTestServerSources.CreateNotes(ServerSql, ServerNotes);

    /// <summary>
    /// 注文の差分ソースだけを差し替えた同期エンジンを組み立てる（他の部品は本体の <see cref="Engine"/> と同じ）。
    /// </summary>
    /// <remarks>
    /// 1 回のラン（<c>SyncAsync</c>）の内側＝アップロードとダウンロードの<b>あいだ</b>へ割り込むための足場。
    /// <c>UploadAsync</c> / <c>DownloadAsync</c> を別々に呼ぶ形では「1 回のランとして見たときの噛み合わせ」を
    /// 見られない（ラン単位で持ち回る状態があれば、それが 2 回の呼び出しでは共有されないため）。
    /// </remarks>
    protected SyncEngine CreateEngineWithOrderSource(
        ISyncServerSource<SyncOrderEntity, int> orders
    ) =>
        new(
            [
                new SyncTable<SyncOrderEntity, int>(
                    LocalOrders,
                    LocalSql,
                    orders,
                    GeneratedSyncTables.SyncOrder
                ),
                new SyncTable<SyncOrderLineEntity, int>(
                    LocalLines,
                    LocalSql,
                    _sources.Lines,
                    GeneratedSyncTables.SyncOrderLine
                ),
                new VersionlessSyncTable<SyncNoteEntity, int>(
                    LocalNotes,
                    LocalSql,
                    _sources.Notes,
                    GeneratedSyncTables.SyncNote
                ),
            ],
            Journal
        );

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

    /// <summary>サーバー役へメモ（版なしテーブル）を 1 件入れる（版の採番は存在しない）</summary>
    protected async Task SeedServerNoteAsync(int noteId, int orderId, string body) =>
        await ServerNotes.InsertAsync(
            new SyncNoteEntity
            {
                NoteId = noteId,
                OrderId = orderId,
                Body = body,
            },
            Ct
        );

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
    /// 1 バッチが 1 回の IN 展開の上限を超える件数でも、既存／新規の判定を取りこぼさない。
    /// </summary>
    /// <remarks>
    /// バッチサイズは呼び出し側が自由に決めるため、適用前の「ローカルに既にあるキー」照会はキーの数だけ
    /// バインド変数を積む。照会はチャンク分割されており、境界（500 件）の前後どちらに落ちたキーも既存と
    /// 判定される（取りこぼすと既存行を挿入扱いして主キー重複で落ちる）。
    /// </remarks>
    [Fact(DisplayName = "[Sync] 1 バッチが 500 件を超えても既存/新規の判定を取りこぼさない")]
    public async Task DownloadBatch_HandlesMoreRowsThanOneInClause()
    {
        const int Count = 600;

        for (var id = 1; id <= Count; id++)
        {
            await ServerOrders.InsertAsync(
                new SyncOrderEntity { OrderId = id, CustomerName = $"server-{id}" },
                Ct
            );
        }

        // チャンク境界（500 件）を跨ぐ位置へローカル既存行を仕込む
        // （素のリポジトリ＝ジャーナルを通らないのでアップロード対象にはならない）
        int[] preexisting = [1, 499, 500, 501, Count];

        foreach (var id in preexisting)
        {
            await LocalOrdersRaw.InsertAsync(
                new SyncOrderEntity { OrderId = id, CustomerName = "stale" },
                Ct
            );
        }

        var result = await Engine.SyncAsync(
            new SyncOptions { DownloadBatchSize = Count, PropagateDeletes = false },
            Ct
        );

        result.Conflicts.Should().BeEmpty();
        result.Downloaded.Should().Be(Count);
        (await LocalOrdersRaw.GetAllAsync(Ct))
            .Should()
            .HaveCount(Count, "既存行は更新・残りは挿入＝行が増えも減りもしない");

        foreach (var id in preexisting)
        {
            (await LocalOrdersRaw.GetByIdAsync(id, Ct))!
                .CustomerName.Should()
                .Be($"server-{id}", "既存と判定された行はサーバーの内容で更新される");
        }
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

    /// <summary>
    /// 競合として保留した行は、同じ実行の削除伝搬に消されない（未送信ジャーナルが守る）。
    /// </summary>
    /// <remarks>
    /// サーバーで消えた行をローカルが編集した状態は、アップロードでは <see cref="SyncConflictReason.MissingOnServer"/>
    /// 競合として保留されるのに、続くダウンロードの削除伝搬から見ると「サーバーのキー集合に無い行」でもある。
    /// ジャーナルを参照しないと、既定の収集ポリシーが宣言どおり保留したはずの行を同じ実行が黙って消し、
    /// <c>Collect</c> がこの競合クラスに限って <c>ServerWins</c> と同じ結果になる。
    /// </remarks>
    [Fact(DisplayName = "[Sync] 競合で保留した行は削除伝搬の対象外（未送信ジャーナルが守る）")]
    public async Task DeletePropagation_SparesRowWithPendingJournalEntry()
    {
        await SeedServerAsync(1, "alice", 11, "widget");
        await Engine.SyncAsync(cancellationToken: Ct);

        // サーバー側から行が消えたあとにローカルが同じ行を編集する（＝アップロードは MissingOnServer 競合）
        await ServerLines.DeleteAsync(11, Ct);
        await ServerOrders.DeleteAsync(1, Ct);

        var local = await LocalOrders.GetByIdAsync(1, Ct);
        local!.CustomerName = "alice-local";
        await LocalOrders.UpdateAsync(local, cancellationToken: Ct);

        var result = await Engine.SyncAsync(cancellationToken: Ct);

        var conflict = result.Conflicts.Should().ContainSingle().Subject;
        conflict.TableName.Should().Be("sync_orders");
        conflict.Reason.Should().Be(SyncConflictReason.MissingOnServer);

        var survivor = await LocalOrdersRaw.GetByIdAsync(1, Ct);
        survivor.Should().NotBeNull("競合として保留した行を同じ実行の削除伝搬が消してはいけない");
        survivor!.CustomerName.Should().Be("alice-local", "ローカルの編集内容も残る");
        (await Journal.CountPendingAsync(Ct)).Should().Be(1, "エントリは未解決のまま残る");

        result.DeletedLocally.Should().Be(1, "ジャーナルに載っていない明細行は消える");
        (await LocalLinesRaw.GetAllAsync(Ct)).Should().BeEmpty();
    }

    /// <summary>エントリが解決されれば、以降の実行では削除が伝搬される（恒久的な削除漏れではない）</summary>
    [Fact(DisplayName = "[Sync] 競合を解決した実行では削除が伝搬される")]
    public async Task DeletePropagation_ResumesOnceJournalEntryIsSettled()
    {
        await SeedServerAsync(1, "alice", 11, "widget");
        await Engine.SyncAsync(cancellationToken: Ct);

        await ServerLines.DeleteAsync(11, Ct);
        await ServerOrders.DeleteAsync(1, Ct);

        var local = await LocalOrders.GetByIdAsync(1, Ct);
        local!.CustomerName = "alice-local";
        await LocalOrders.UpdateAsync(local, cancellationToken: Ct);

        await Engine.SyncAsync(cancellationToken: Ct);
        (await LocalOrdersRaw.GetByIdAsync(1, Ct)).Should().NotBeNull("保留中は守られる");

        // ServerWins はジャーナルを捨てる＝エントリが解決された状態。守る理由が無くなるので伝搬が効く
        var resolved = await Engine.SyncAsync(
            new SyncOptions { ConflictPolicy = SyncConflictPolicy.ServerWins },
            Ct
        );

        resolved.Conflicts.Should().BeEmpty();
        resolved.DeletedLocally.Should().Be(1);
        (await LocalOrdersRaw.GetByIdAsync(1, Ct))
            .Should()
            .BeNull("解決済みのキーは守られない＝削除漏れが恒久化しない");
        (await Journal.CountPendingAsync(Ct)).Should().Be(0);
    }

    /// <summary>まだアップロードを試みていないローカル新規行も、ジャーナルにある限り削除伝搬から守られる</summary>
    /// <remarks>
    /// 競合キーを Upload から Download へ引き回す実装では救えない側＝ジャーナルを見る実装であることの直接の証拠。
    /// ダウンロードだけを回す（アップロードを挟まない）と、この行はサーバーのキー集合に無いまま伝搬に晒される。
    /// </remarks>
    [Fact(DisplayName = "[Sync] 未アップロードのローカル新規行は削除伝搬で消えない")]
    public async Task DeletePropagation_SparesLocalInsertNotYetUploaded()
    {
        await LocalOrders.InsertAsync(
            new SyncOrderEntity { OrderId = 5, CustomerName = "dave" },
            Ct
        );

        var result = await Engine.DownloadAsync(new SyncOptions(), Ct);

        result.DeletedLocally.Should().Be(0);
        (await LocalOrdersRaw.GetByIdAsync(5, Ct))
            .Should()
            .NotBeNull("未送信の意図が残っている行はサーバーが消した行ではない");
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
            .NotBeNull(
                "採番された版は同じランのダウンロードが行のエコーとして受け取ってミラーへ書く（アップロードは書かない）"
            );
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

    // ---- カスケードグラフの記録（SyncGraphRecorder） ----

    /// <summary>
    /// カスケード保存（グラフ保存）で書かれた子行もジャーナルへ載り、同期でサーバーへ届く。
    /// </summary>
    /// <remarks>
    /// 記録がルートしか見ない旧実装では、①子がアップロードされず、②同じランの削除伝搬が「サーバーに無い
    /// キー」としてローカルの子行を消す＝同期 1 回で子データが両側から失われていた（2026-08-19 実測）。
    /// このテストは両方の穴を固定する。
    /// </remarks>
    [Fact(
        DisplayName = "[Sync] カスケード保存の親子グラフは両方ジャーナルへ載り同期でサーバーへ届く"
    )]
    public async Task CascadeGraphSave_UploadsParentAndChild()
    {
        var order = new SyncOrderEntity { OrderId = 1, CustomerName = "alice" };
        order.MarkAdded();
        var line = new SyncOrderLineEntity
        {
            LineId = 11,
            OrderId = 1,
            Product = "widget",
        };
        line.MarkAdded();
        order.SyncOrderLines.Add(line);

        await LocalOrders.SaveAsync(order, cancellationToken: Ct);

        var entries = await Journal.ReadAllAsync(Ct);
        entries
            .Select(entry => entry.TableName)
            .Should()
            .Contain(["sync_orders", "sync_order_lines"]);

        var result = await Engine.SyncAsync(cancellationToken: Ct);

        result.Conflicts.Should().BeEmpty();
        result.Uploaded.Should().Be(2, "親子 2 行が送られる");
        result.DeletedLocally.Should().Be(0, "記録済みの子行は削除伝搬に消されない");
        (await ServerOrders.GetByIdAsync(1, Ct)).Should().NotBeNull();
        (await ServerLines.GetByIdAsync(11, Ct)).Should().NotBeNull();

        var localLine = await LocalLinesRaw.GetByIdAsync(11, Ct);
        localLine.Should().NotBeNull("ローカルの子行が同期後も残っている");
        localLine!.RowVer.Should().NotBeNullOrEmpty("サーバーが採番した版がミラーへ書き戻る");

        var second = await Engine.SyncAsync(cancellationToken: Ct);
        second.Uploaded.Should().Be(0);
        second.Downloaded.Should().Be(0, "自分の変更を取り戻さない（アンカーが進んでいる）");
    }

    /// <summary>Unchanged のルート配下で子だけを編集したグラフ保存も、子の変更が記録され届く</summary>
    /// <remarks>
    /// 保存側の変更検出（HasChanges）はカスケード全体を見るためこの保存は実行される。記録も同じ走査で
    /// 「ルートは記録なし・子は Upsert」にならなければ、片方だけが真実になる。
    /// </remarks>
    [Fact(DisplayName = "[Sync] Unchanged ルート下の子だけ編集したグラフ保存も子の変更が届く")]
    public async Task CascadeGraphSave_ChildEditUnderUnchangedRoot_Uploads()
    {
        await SeedServerAsync(1, "alice", 11, "widget");
        await Engine.SyncAsync(cancellationToken: Ct);

        var order = await LocalOrdersRaw.GetByIdAsync(1, Ct);
        var line = await LocalLinesRaw.GetByIdAsync(11, Ct);
        line!.Product = "widget-v2";
        line.MarkUpdated();
        order!.SyncOrderLines.Add(line);

        await LocalOrders.SaveAsync(order, cancellationToken: Ct);

        var entries = await Journal.ReadAllAsync(Ct);
        entries.Should().ContainSingle(entry => entry.TableName == "sync_order_lines");
        entries
            .Should()
            .NotContain(
                entry => entry.TableName == "sync_orders",
                "Unchanged のルートは記録されない"
            );

        var result = await Engine.SyncAsync(cancellationToken: Ct);

        result.Uploaded.Should().Be(1);
        (await ServerLines.GetByIdAsync(11, Ct))!.Product.Should().Be("widget-v2");
    }

    /// <summary>
    /// カスケード削除は子のエントリも記録され、再生は子→親の順でサーバーの FK を壊さない。
    /// </summary>
    /// <remarks>
    /// 旧実装では親の Delete だけが残り、再生がサーバー側の子の FK 制約に阻まれて同期が例外で恒久失敗
    /// していた（エントリが残留し以後のランも同じ例外・2026-08-19 実測）。
    /// </remarks>
    [Fact(DisplayName = "[Sync] カスケード削除は子から順に再生されサーバーの FK を壊さない")]
    public async Task CascadeGraphDelete_ReplaysChildFirst()
    {
        await SeedServerAsync(1, "alice", 11, "widget");
        await Engine.SyncAsync(cancellationToken: Ct);

        var order = await LocalOrdersRaw.GetByIdAsync(1, Ct);
        var line = await LocalLinesRaw.GetByIdAsync(11, Ct);
        order!.SyncOrderLines.Add(line!);
        order.MarkRemoved();

        await LocalOrders.SaveAsync(order, cancellationToken: Ct);

        (await LocalLinesRaw.GetByIdAsync(11, Ct))
            .Should()
            .BeNull("カスケード削除で子もローカルから消えている");

        var entries = await Journal.ReadAllAsync(Ct);
        entries
            .Select(entry => (entry.TableName, entry.Operation))
            .Should()
            .BeEquivalentTo(
                [("sync_order_lines", "Delete"), ("sync_orders", "Delete")],
                "子の削除もミラー版付きで記録される"
            );

        var result = await Engine.SyncAsync(cancellationToken: Ct);

        result.Conflicts.Should().BeEmpty();
        result.Uploaded.Should().Be(2);
        (await ServerOrders.GetByIdAsync(1, Ct)).Should().BeNull();
        (await ServerLines.GetByIdAsync(11, Ct)).Should().BeNull();
        (await Journal.CountPendingAsync(Ct)).Should().Be(0, "全エントリが決着して掃除される");
    }

    // ---- 後勝ちモード（版なしテーブル・SyncMode.LastWriteWins） ----

    /// <summary>既定（Versioned）のランは版なしテーブルに一切触れない（実行時挙動の完全互換）</summary>
    [Fact(DisplayName = "[Sync/LWW] 既定（Versioned）のランは版なしテーブルに触れない")]
    public async Task VersionedRun_LeavesVersionlessTableAlone()
    {
        await SeedServerAsync(1, "alice", 11, "widget");
        await SeedServerNoteAsync(101, 1, "server note");

        var result = await Engine.SyncAsync(cancellationToken: Ct);

        result.Downloaded.Should().Be(2, "注文と明細だけが降り、メモは対象外");
        (await LocalNotesRaw.GetAllAsync(Ct)).Should().BeEmpty();

        // ローカルのメモ書き込みは記録されるが、Versioned のランでは回収も削除伝搬もされない
        await LocalNotes.InsertAsync(
            new SyncNoteEntity
            {
                NoteId = 102,
                OrderId = 1,
                Body = "local note",
            },
            Ct
        );
        var second = await Engine.SyncAsync(cancellationToken: Ct);

        second.Uploaded.Should().Be(0);
        (await ServerNotes.GetByIdAsync(102, Ct)).Should().BeNull();
        (await LocalNotesRaw.GetByIdAsync(102, Ct)).Should().NotBeNull();
        (await Journal.CountPendingAsync(Ct)).Should().Be(1, "エントリは次の後勝ちランまで残る");
    }

    /// <summary>後勝ちランは版なしテーブルをキー順の全量スキャンで降ろし、FK 順（親→子）を守る</summary>
    [Fact(DisplayName = "[Sync/LWW] 後勝ちランは版なしテーブルを全量ダウンロードする（毎回）")]
    public async Task LastWriteWins_DownloadsVersionlessTable()
    {
        await SeedServerAsync(1, "alice", 11, "widget");
        await SeedServerNoteAsync(101, 1, "server note");

        var options = new SyncOptions { Mode = SyncMode.LastWriteWins };
        var result = await Engine.SyncAsync(options, Ct);

        result.Conflicts.Should().BeEmpty();
        result.Downloaded.Should().Be(3, "注文・明細＝増分 2 行＋メモ＝全量 1 行");
        (await LocalNotesRaw.GetByIdAsync(101, Ct))!.Body.Should().Be("server note");

        // 変更が無くても版なしテーブルは毎回全量降りる（O(テーブル)＝設計どおりのコスト）。版ありは増分＝0
        var second = await Engine.SyncAsync(options, Ct);

        second.Downloaded.Should().Be(1, "メモの全量スキャン 1 行だけが再適用される");
    }

    /// <summary>版なしテーブルのローカル編集（追加・更新・削除）が後勝ちで届き、ジャーナルが決着する</summary>
    [Fact(
        DisplayName = "[Sync/LWW] 版なしテーブルのローカル編集（追加・更新・削除）が後勝ちで届く"
    )]
    public async Task LastWriteWins_UploadsVersionlessEdits()
    {
        await SeedServerAsync(1, "alice", 11, "widget");
        await SeedServerNoteAsync(101, 1, "original");
        var options = new SyncOptions { Mode = SyncMode.LastWriteWins };
        await Engine.SyncAsync(options, Ct);

        var note = await LocalNotesRaw.GetByIdAsync(101, Ct);
        note!.Body = "edited locally";
        await LocalNotes.UpdateAsync(note, cancellationToken: Ct);
        await LocalNotes.InsertAsync(
            new SyncNoteEntity
            {
                NoteId = 102,
                OrderId = 1,
                Body = "new local",
            },
            Ct
        );

        var result = await Engine.SyncAsync(options, Ct);

        result.Conflicts.Should().BeEmpty();
        result.Uploaded.Should().Be(2);
        (await ServerNotes.GetByIdAsync(101, Ct))!.Body.Should().Be("edited locally");
        (await ServerNotes.GetByIdAsync(102, Ct))!.Body.Should().Be("new local");
        (await LocalNotesRaw.GetByIdAsync(102, Ct))
            .Should()
            .NotBeNull("アップロード済みの新規行は削除伝搬に消されない");

        await LocalNotes.DeleteAsync(101, Ct);
        var third = await Engine.SyncAsync(options, Ct);

        third.Uploaded.Should().Be(1);
        (await ServerNotes.GetByIdAsync(101, Ct)).Should().BeNull();
        (await Journal.CountPendingAsync(Ct)).Should().Be(0);
    }

    /// <summary>
    /// 後勝ちの意味論そのもの＝すれ違いは後からアップロードした側が黙って勝ち、削除とのすれ違いは行を復活させる。
    /// </summary>
    /// <remarks>
    /// どちらも検出も報告もされない（<c>Conflicts</c> は常に空）。これが後勝ちモードの名づけた取引で、
    /// docs が明記する意味論をそのまま固定する。
    /// </remarks>
    [Fact(
        DisplayName = "[Sync/LWW] すれ違いは後勝ちで黙って上書きされ、削除とのすれ違いは行が復活する"
    )]
    public async Task LastWriteWins_OverwritesAndResurrectsWithoutConflict()
    {
        await SeedServerAsync(1, "alice", 11, "widget");
        await SeedServerNoteAsync(101, 1, "original");
        await SeedServerNoteAsync(103, 1, "doomed");
        var options = new SyncOptions { Mode = SyncMode.LastWriteWins };
        await Engine.SyncAsync(options, Ct);

        // 101: 両側更新 → ローカルが勝つ
        var server = await ServerNotes.GetByIdAsync(101, Ct);
        server!.Body = "changed on server";
        await ServerNotes.UpdateAsync(server, cancellationToken: Ct);
        var local = await LocalNotesRaw.GetByIdAsync(101, Ct);
        local!.Body = "changed locally";
        await LocalNotes.UpdateAsync(local, cancellationToken: Ct);

        // 103: サーバー削除 × ローカル編集 → アップロード（update→false→insert）が復活させる
        await ServerNotes.DeleteAsync(103, Ct);
        var doomed = await LocalNotesRaw.GetByIdAsync(103, Ct);
        doomed!.Body = "revived";
        await LocalNotes.UpdateAsync(doomed, cancellationToken: Ct);

        var result = await Engine.SyncAsync(options, Ct);

        result.Conflicts.Should().BeEmpty();
        (await ServerNotes.GetByIdAsync(101, Ct))!.Body.Should().Be("changed locally");
        (await ServerNotes.GetByIdAsync(103, Ct))!
            .Body.Should()
            .Be("revived", "後勝ちは削除とのすれ違いで行を復活させる（docs 明記の意味論）");
    }

    /// <summary>版ありテーブルも後勝ちランでは版ガードなしで上書きし、ミラー版の書き戻しでエコーしない</summary>
    [Fact(
        DisplayName = "[Sync/LWW] 版ありテーブルも後勝ちランでは版ガードなしで上書きし競合を報告しない"
    )]
    public async Task LastWriteWins_BypassesVersionGuardOnVersionedTables()
    {
        await SeedServerAsync(1, "alice", 11, "widget");
        await Engine.SyncAsync(cancellationToken: Ct);

        // すれ違い: サーバーとローカルの両方で注文を更新（Versioned なら ModifiedOnServer 競合になる形）
        var server = await ServerOrders.GetByIdAsync(1, Ct);
        server!.CustomerName = "server-edit";
        await ServerOrders.UpdateAsync(server, cancellationToken: Ct);
        var local = await LocalOrdersRaw.GetByIdAsync(1, Ct);
        local!.CustomerName = "local-edit";
        await LocalOrders.UpdateAsync(local, ConcurrencyMode.ForceOverwrite, Ct);

        var lww = new SyncOptions { Mode = SyncMode.LastWriteWins };
        var result = await Engine.SyncAsync(lww, Ct);

        result.Conflicts.Should().BeEmpty();
        result.Uploaded.Should().Be(1);
        (await ServerOrders.GetByIdAsync(1, Ct))!.CustomerName.Should().Be("local-edit");

        // ミラー版が書き戻り、次のランが自分の変更を取り戻さない
        var second = await Engine.SyncAsync(lww, Ct);

        second.Uploaded.Should().Be(0);
        second.Downloaded.Should().Be(0, "版ありは増分＝エコーなし（版なしテーブルは空）");
        (await LocalOrdersRaw.GetByIdAsync(1, Ct))!.CustomerName.Should().Be("local-edit");
    }

    /// <summary>版なしの子（sync_notes はカスケードナビゲーション）を含むグラフ保存も記録され、後勝ちで届く</summary>
    [Fact(DisplayName = "[Sync/LWW] 版なしの子を含むカスケード保存も記録され後勝ちランで届く")]
    public async Task LastWriteWins_CascadeSaveWithVersionlessChild_Uploads()
    {
        var order = new SyncOrderEntity { OrderId = 1, CustomerName = "alice" };
        order.MarkAdded();
        var note = new SyncNoteEntity
        {
            NoteId = 101,
            OrderId = 1,
            Body = "memo",
        };
        note.MarkAdded();
        order.SyncNotes.Add(note);

        await LocalOrders.SaveAsync(order, cancellationToken: Ct);

        var result = await Engine.SyncAsync(new SyncOptions { Mode = SyncMode.LastWriteWins }, Ct);

        result.Conflicts.Should().BeEmpty();
        result.Uploaded.Should().Be(2, "注文＋版なしの子メモの両方が送られる");
        (await ServerNotes.GetByIdAsync(101, Ct))!.Body.Should().Be("memo");
    }

    /// <summary>ラン単位の除外は今回のランから外すだけで、記録は続き次の対象ランで回収される</summary>
    [Fact(DisplayName = "[Sync/LWW] ラン単位の除外は今回のランから外すだけで記録は残る")]
    public async Task ExcludedEntityTypes_SkipTablesForOneRun()
    {
        await SeedServerAsync(1, "alice", 11, "widget");
        await SeedServerNoteAsync(101, 1, "server note");
        var lww = new SyncOptions { Mode = SyncMode.LastWriteWins };
        await Engine.SyncAsync(lww, Ct);

        var note = await LocalNotesRaw.GetByIdAsync(101, Ct);
        note!.Body = "edited locally";
        await LocalNotes.UpdateAsync(note, cancellationToken: Ct);

        var excluded = new SyncOptions
        {
            Mode = SyncMode.LastWriteWins,
            ExcludedEntityTypes = [typeof(SyncNoteEntity)],
        };
        var result = await Engine.SyncAsync(excluded, Ct);

        result.Uploaded.Should().Be(0);
        (await ServerNotes.GetByIdAsync(101, Ct))!.Body.Should().Be("server note");
        (await Journal.CountPendingAsync(Ct)).Should().Be(1);

        var inclusive = await Engine.SyncAsync(lww, Ct);

        inclusive.Uploaded.Should().Be(1);
        (await ServerNotes.GetByIdAsync(101, Ct))!.Body.Should().Be("edited locally");
    }

    /// <summary>エンジンが同期しない型の除外指定は黙殺せず拒否する（タイポ・構築時除外済みの型）</summary>
    [Fact(
        DisplayName = "[Sync/LWW] エンジンが知らない型の除外指定は ArgumentException で拒否される"
    )]
    public async Task UnknownExcludedType_IsRejected()
    {
        var act = async () =>
            await Engine.SyncAsync(new SyncOptions { ExcludedEntityTypes = [typeof(string)] }, Ct);

        await act.Should().ThrowAsync<System.ArgumentException>();
    }

    /// <summary>
    /// 洗い替えの既定スコープは全テーブル（版なしテーブルも作り直す）＝「ローカルをサーバーの姿にする」の意味論。
    /// Versioned への絞り込みは明示指定で、未送信の拒否・破棄もスコープ内のエントリだけを数える。
    /// </summary>
    /// <remarks>
    /// 既定を Versioned にすると、版なし子テーブル（sync_notes）が版あり親（sync_orders）を参照する図で
    /// 「親の全消しが版なし子の FK に阻まれる」＝既定の洗い替えが構造的に失敗する（実装時に実測で発覚し、
    /// 既定を全テーブルへ補正した）。版ありだけの図では両者のスコープが一致するため挙動差はない。
    /// </remarks>
    [Fact(
        DisplayName = "[Sync/LWW] 洗い替えの既定は全テーブル・Versioned 絞り込みは明示指定でスコープされる"
    )]
    public async Task Refresh_ScopesVersionlessTablesByMode()
    {
        await SeedServerAsync(1, "alice", 11, "widget");
        await SeedServerNoteAsync(101, 1, "server note");

        // Versioned スコープ（明示）＝メモに触れない（ローカルのメモが空なら親の全消しも FK に触れない）
        var versionedScope = await Engine.RefreshAsync(
            new SyncRefreshOptions
            {
                Mode = SyncMode.Versioned,
                DiscardLocalUnboundedBinaries = true,
            },
            Ct
        );

        versionedScope
            .Tables.Select(table => table.TableName)
            .Should()
            .BeEquivalentTo(["sync_orders", "sync_order_lines"]);
        (await LocalNotesRaw.GetAllAsync(Ct)).Should().BeEmpty("版なしテーブルは対象外のまま");

        // 既定（Mode 未指定＝全テーブル）で版なしも降ろし、未送信エントリを作ってから既定の洗い替えへ
        await Engine.SyncAsync(new SyncOptions { Mode = SyncMode.LastWriteWins }, Ct);
        await LocalNotes.InsertAsync(
            new SyncNoteEntity
            {
                NoteId = 102,
                OrderId = 1,
                Body = "pending",
            },
            Ct
        );

        // 既定スコープは版なしを含む＝その未送信エントリが拒否の対象になる
        var refuse = async () => await Engine.RefreshAsync(RefreshDefaults, Ct);
        await refuse.Should().ThrowAsync<SyncPendingChangesException>();

        var forced = await Engine.RefreshAsync(
            new SyncRefreshOptions { DiscardLocalUnboundedBinaries = true, Force = true },
            Ct
        );

        forced.DiscardedChanges.Should().Be(1);
        forced
            .Tables.Select(table => table.TableName)
            .Should()
            .BeEquivalentTo(["sync_orders", "sync_order_lines", "sync_notes"]);
        (await LocalNotesRaw.GetByIdAsync(102, Ct))
            .Should()
            .BeNull("洗い替えでサーバーの姿へ揃った（未送信は明示破棄済み）");
        (await LocalNotesRaw.GetByIdAsync(101, Ct)).Should().NotBeNull();
    }

    /// <summary>ジャーナルの掃除＝RemoveTableAsync は 1 テーブル・RemoveAllAsync は全部を破棄する</summary>
    [Fact(
        DisplayName = "[Sync] ジャーナル掃除は RemoveTableAsync＝1 テーブル・RemoveAllAsync＝全部"
    )]
    public async Task JournalCleanup_RemovesEntriesByTableAndWholesale()
    {
        await LocalOrders.InsertAsync(new SyncOrderEntity { OrderId = 1, CustomerName = "a" }, Ct);
        await LocalNotes.InsertAsync(
            new SyncNoteEntity
            {
                NoteId = 101,
                OrderId = 1,
                Body = "b",
            },
            Ct
        );
        (await Journal.CountPendingAsync(Ct)).Should().Be(2);

        await Journal.RemoveTableAsync("sync_notes", Ct);

        var entries = await Journal.ReadAllAsync(Ct);
        entries.Should().ContainSingle(entry => entry.TableName == "sync_orders");

        await Journal.RemoveAllAsync(Ct);

        (await Journal.CountPendingAsync(Ct)).Should().Be(0);
    }

    /// <summary>ServerWins の全破棄はランのスコープ内だけ＝版なしテーブルの未送信は残る</summary>
    [Fact(DisplayName = "[Sync] ServerWins の破棄はランのスコープ内だけ＝版なしの未送信は残る")]
    public async Task ServerWins_DropsOnlyScopedEntries()
    {
        await SeedServerAsync(1, "alice", 11, "widget");
        await Engine.SyncAsync(cancellationToken: Ct);

        var order = await LocalOrdersRaw.GetByIdAsync(1, Ct);
        order!.CustomerName = "local-edit";
        await LocalOrders.UpdateAsync(order, ConcurrencyMode.ForceOverwrite, Ct);
        await LocalNotes.InsertAsync(
            new SyncNoteEntity
            {
                NoteId = 102,
                OrderId = 1,
                Body = "pending",
            },
            Ct
        );

        var result = await Engine.SyncAsync(
            new SyncOptions { ConflictPolicy = SyncConflictPolicy.ServerWins },
            Ct
        );

        result.Discarded.Should().Be(1, "捨てられるのはスコープ内（版あり）のエントリだけ");
        (await Journal.CountPendingAsync(Ct))
            .Should()
            .Be(1, "版なしのエントリは後勝ちランまで残る");
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

    /// <summary>ServerWins はサーバーに無いローカル新規行を「上書き」でなく削除で解決する</summary>
    /// <remarks>
    /// ServerWins はジャーナルを丸ごと捨てるため保留キー集合が空になり、サーバーに一度も上がっていない行は
    /// 削除伝搬の対象になる＝「サーバーの状態が勝つ」の削除側。XmlDoc の主張（上書き・削除の両面）を固定する。
    /// </remarks>
    [Fact(DisplayName = "[Sync] ServerWins はサーバーに無いローカル新規行を削除で解決する")]
    public async Task ServerWins_RemovesLocalInsertServerNeverReceived()
    {
        await SeedServerAsync(1, "alice", 11, "widget");
        await Engine.SyncAsync(cancellationToken: Ct);

        await LocalOrders.InsertAsync(
            new SyncOrderEntity { OrderId = 6, CustomerName = "erin" },
            Ct
        );

        var result = await Engine.SyncAsync(
            new SyncOptions { ConflictPolicy = SyncConflictPolicy.ServerWins },
            Ct
        );

        result.Conflicts.Should().BeEmpty();
        result.Discarded.Should().BeGreaterThan(0, "未送信の挿入意図はジャーナルごと破棄される");
        result.DeletedLocally.Should().Be(1, "サーバーに無い行は上書きでなく削除伝搬で消える");
        (await LocalOrdersRaw.GetByIdAsync(6, Ct))
            .Should()
            .BeNull("ServerWins はサーバーの状態＝行なしへ揃える");
        (await Journal.CountPendingAsync(Ct)).Should().Be(0);
    }

    /// <summary>ServerWins はサーバーが変えていない行でも、ローカル編集をサーバー内容へ戻す</summary>
    /// <remarks>
    /// ダウンロードが降ろすのは「前回ミラーしてから版が進んだ行」だけなので、サーバー側が未変更の行は
    /// ジャーナルを捨てるだけでは<b>ローカル編集が残ったまま</b>になる（エントリも消えているので以後どの
    /// ランも直さない）。ServerWins が字義どおりであることを固定する。
    /// </remarks>
    [Fact(
        DisplayName = "[Sync] ServerWins はサーバー未変更の行でもローカル編集をサーバー内容へ戻す"
    )]
    public async Task ServerWins_RestoresRowTheServerHasNotChanged()
    {
        await SeedServerAsync(1, "alice", 11, "widget");
        await Engine.SyncAsync(cancellationToken: Ct);

        var local = await LocalOrders.GetByIdAsync(1, Ct);
        local!.CustomerName = "local-edit";
        await LocalOrders.UpdateAsync(local, cancellationToken: Ct);

        var result = await Engine.SyncAsync(
            new SyncOptions { ConflictPolicy = SyncConflictPolicy.ServerWins },
            Ct
        );

        result.Conflicts.Should().BeEmpty();
        result.Discarded.Should().Be(1);
        (await LocalOrdersRaw.GetByIdAsync(1, Ct))!
            .CustomerName.Should()
            .Be("alice", "サーバーが変えていない行はダウンロードが降ろさない＝読み直して適用する");
        (await ServerOrders.GetByIdAsync(1, Ct))!
            .CustomerName.Should()
            .Be("alice", "ServerWins は何も送らない");
        (await Journal.CountPendingAsync(Ct)).Should().Be(0);
    }

    /// <summary>ServerWins はローカルで削除した行をサーバーから読み直して復活させる</summary>
    /// <remarks>
    /// 削除した行の版がアンカーの下に潜る形（2 本目の明細が後から入って版を進めている）を作る。こうしないと
    /// 消えた行の分だけアンカーが下がってダウンロードが勝手に取り戻してしまい、読み直し適用を見たことに
    /// ならない。
    /// </remarks>
    [Fact(DisplayName = "[Sync] ServerWins はローカルで削除した行をサーバーから復活させる")]
    public async Task ServerWins_RestoresRowDeletedLocally()
    {
        await SeedServerAsync(1, "alice", 11, "widget");
        await ServerLines.InsertAsync(
            new SyncOrderLineEntity
            {
                LineId = 12,
                OrderId = 1,
                Product = "gadget",
            },
            Ct
        );
        await Engine.SyncAsync(cancellationToken: Ct);

        (await LocalLines.DeleteAsync(11, Ct)).Should().BeTrue();

        var result = await Engine.SyncAsync(
            new SyncOptions { ConflictPolicy = SyncConflictPolicy.ServerWins },
            Ct
        );

        result.Conflicts.Should().BeEmpty();
        var restored = await LocalLinesRaw.GetByIdAsync(11, Ct);
        restored.Should().NotBeNull("サーバーがまだ持っている行は戻る＝削除もサーバーが勝つ");
        restored!.Product.Should().Be("widget");
        (await ServerLines.GetByIdAsync(11, Ct)).Should().NotBeNull("削除は送られない");
        (await Journal.CountPendingAsync(Ct)).Should().Be(0);
    }

    /// <summary>ServerWins でサーバーが消していた行は、読み直せないので削除伝搬が決着させる</summary>
    [Fact(DisplayName = "[Sync] ServerWins でサーバーが消した行はローカルからも消える")]
    public async Task ServerWins_RemovesRowDeletedOnServer()
    {
        await SeedServerAsync(1, "alice", 11, "widget");
        await Engine.SyncAsync(cancellationToken: Ct);

        var local = await LocalLines.GetByIdAsync(11, Ct);
        local!.Product = "local-edit";
        await LocalLines.UpdateAsync(local, cancellationToken: Ct);

        (await ServerLines.DeleteAsync(11, Ct)).Should().BeTrue();

        var result = await Engine.SyncAsync(
            new SyncOptions { ConflictPolicy = SyncConflictPolicy.ServerWins },
            Ct
        );

        result.Conflicts.Should().BeEmpty();
        (await LocalLinesRaw.GetByIdAsync(11, Ct))
            .Should()
            .BeNull("読み直せる行が無い＝削除伝搬が決着させる");
        (await Journal.CountPendingAsync(Ct)).Should().Be(0);
    }

    /// <summary>ServerWins の読み直し適用はミラー版を進めない＝未取得の他者更新を追い越さない</summary>
    /// <remarks>
    /// サーバーから読んだ行はサーバーの版を連れてくる。それをそのままミラーへ書くと導出アンカーがそこまで
    /// 跳び、あいだの版を持つ<b>未取得の行が恒久的に読み飛ばされる</b>（Phase 1 で潰した B1 と同型）。
    /// アップロードとダウンロードを別々に呼び、適用直後のミラー版と、その後のダウンロードが取りこぼさない
    /// ことの両方を見る。
    /// </remarks>
    [Fact(
        DisplayName = "[Sync] ServerWins の読み直し適用はアンカーを進めず他者更新を取りこぼさない"
    )]
    public async Task ServerWins_RestoreDoesNotAdvanceAnchor()
    {
        await SeedServerAsync(1, "alice", 11, "widget");
        await SeedServerAsync(2, "bob", 12, "gadget");
        await Engine.SyncAsync(cancellationToken: Ct);

        // 他者がサーバーの 2 行を更新する（版は P → X の順に進む）
        var serverP = await ServerOrders.GetByIdAsync(1, Ct);
        serverP!.CustomerName = "server-p";
        await ServerOrders.UpdateAsync(serverP, cancellationToken: Ct);

        var serverX = await ServerOrders.GetByIdAsync(2, Ct);
        serverX!.CustomerName = "server-x";
        await ServerOrders.UpdateAsync(serverX, cancellationToken: Ct);

        // ローカルは X だけを編集する（P には触らない＝降りてくるのを待っている行）
        var local = await LocalOrders.GetByIdAsync(2, Ct);
        local!.CustomerName = "local-x";
        await LocalOrders.UpdateAsync(local, cancellationToken: Ct);

        var mirrorBefore = (await LocalOrdersRaw.GetByIdAsync(2, Ct))!.RowVer;

        var options = new SyncOptions { ConflictPolicy = SyncConflictPolicy.ServerWins };
        var conflicts = new List<SyncConflict>();
        var (uploaded, discarded) = await Engine.UploadAsync(options, conflicts, Ct);

        uploaded.Should().Be(0);
        discarded.Should().Be(1);
        conflicts.Should().BeEmpty();

        var restored = await LocalOrdersRaw.GetByIdAsync(2, Ct);
        restored!.CustomerName.Should().Be("server-x", "サーバー内容が適用される");
        restored
            .RowVer.Should()
            .Equal(mirrorBefore, "ミラー版は据え置き＝アンカーが未取得の行を追い越さない");

        // 追い越していなければ、P も X も正規の経路（版昇順のダウンロード）で降りてくる
        var (downloaded, _) = await Engine.DownloadAsync(options, Ct);

        downloaded
            .Should()
            .Be(
                1,
                "P（中間の版）を取りこぼさない。X は適用時の受理記録と版が一致する＝内容は既にサーバーのもの"
                    + "なので、エコーとして版だけ書かれる（アンカーを進めていれば P ごと 0 件になる）"
            );
        (await LocalOrdersRaw.GetByIdAsync(1, Ct))!.CustomerName.Should().Be("server-p");
        var localX = await LocalOrdersRaw.GetByIdAsync(2, Ct);
        localX!.CustomerName.Should().Be("server-x");
        localX
            .RowVer.Should()
            .Equal(
                (await ServerOrders.GetByIdAsync(2, Ct))!.RowVer,
                "ダウンロードが X まで届いてミラー版を追いつかせる"
            );
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

    /// <summary>
    /// LocalWins はサーバーに同じキーの行が既にあっても、ローカルのオフライン新規行で上書きする。
    /// </summary>
    /// <remarks>
    /// 重複（<see cref="SyncConflictReason.DuplicateOnServer"/>）も版ガード競合と同じ「サーバーと衝突した
    /// ローカル変更」で、ポリシーが LocalWins なら宣言どおりローカルが勝たなければならない。ここを
    /// 無条件の競合にしていると、LocalWins を選んでいるのに競合として保留され続ける。
    /// </remarks>
    [Fact(DisplayName = "[Sync] LocalWins はサーバーに同キー行があるローカル新規行でも上書きする")]
    public async Task LocalWins_OverwritesDuplicateKeyOnServer()
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

        var result = await Engine.SyncAsync(
            new SyncOptions { ConflictPolicy = SyncConflictPolicy.LocalWins },
            Ct
        );

        result.Conflicts.Should().BeEmpty();
        result.Uploaded.Should().Be(1);
        (await ServerOrders.GetByIdAsync(7, Ct))!
            .CustomerName.Should()
            .Be("local-side", "LocalWins はサーバーの行をローカルの内容で置き換える");
        (await Journal.CountPendingAsync(Ct)).Should().Be(0, "決着したエントリは掃除される");
    }

    /// <summary>
    /// LocalWins はサーバーで削除された行を、ローカルの編集で復活させる（後勝ちと同じ意味論）。
    /// </summary>
    /// <remarks>
    /// これを競合のまま残すと、LocalWins のランは何度回しても決着しない＝エントリがジャーナルに残り続け、
    /// そのキーは削除伝搬からも守られ続ける（恒久的に settle しない状態になる）。
    /// </remarks>
    [Fact(DisplayName = "[Sync] LocalWins はサーバーで削除された行をローカルの編集で復活させる")]
    public async Task LocalWins_ResurrectsRowMissingOnServer()
    {
        await SeedServerAsync(1, "alice", 11, "widget");
        await Engine.SyncAsync(cancellationToken: Ct);

        // サーバー側から行が消えたあとにローカルが同じ行を編集する（＝既定なら MissingOnServer 競合）
        await ServerLines.DeleteAsync(11, Ct);
        await ServerOrders.DeleteAsync(1, Ct);

        var local = await LocalOrders.GetByIdAsync(1, Ct);
        local!.CustomerName = "alice-local";
        await LocalOrders.UpdateAsync(local, cancellationToken: Ct);

        var localWins = new SyncOptions { ConflictPolicy = SyncConflictPolicy.LocalWins };
        var result = await Engine.SyncAsync(localWins, Ct);

        result.Conflicts.Should().BeEmpty();
        result.Uploaded.Should().Be(1);
        (await ServerOrders.GetByIdAsync(1, Ct))!
            .CustomerName.Should()
            .Be("alice-local", "サーバーに行が無ければ挿入し直す（復活）");
        (await LocalOrdersRaw.GetByIdAsync(1, Ct)).Should().NotBeNull();
        (await Journal.CountPendingAsync(Ct)).Should().Be(0, "決着したエントリは掃除される");
        result
            .DeletedLocally.Should()
            .Be(1, "ジャーナルに載っていない明細行はサーバーに無いので消える");

        // 決着しているので以降のランは定常（同じ変更を送り返しも取り戻しもしない）
        var second = await Engine.SyncAsync(localWins, Ct);

        second.Uploaded.Should().Be(0);
        second.Downloaded.Should().Be(0);
        second.DeletedLocally.Should().Be(0);
        second.Conflicts.Should().BeEmpty();
    }

    /// <summary>
    /// 業務削除が失敗して残ったジャーナルエントリは、サーバーへ送られず破棄される（journal-first の無害化）。
    /// </summary>
    /// <remarks>
    /// 削除は「送る内容」を持たないため、upsert のようにローカルの現在行を読み直すだけでは無害化されない。
    /// ローカル行がまだ在る＝業務削除が着地しなかったということなので、そのままサーバーを消すと
    /// 「失敗した操作が全体としては完遂される」うえ、続く削除伝搬がローカル行まで道連れにする。
    /// 失敗は FK 違反で作る（子が残っている親を直接削除する＝記録の後に業務削除だけが落ちる）。
    /// </remarks>
    [Fact(
        DisplayName = "[Sync] 業務削除が失敗して残った削除エントリは送られず破棄される（journal-first の無害化）"
    )]
    public async Task FailedLocalDelete_LeavesEntryThatIsDiscarded()
    {
        await SeedServerAsync(1, "alice", 11, "widget");
        await Engine.SyncAsync(cancellationToken: Ct);

        // 子（明細）が残っている親を直接削除する＝ジャーナルへ記録された後に FK 違反で業務削除が失敗する
        var deleting = async () => await LocalOrders.DeleteAsync(1, Ct);
        await deleting.Should().ThrowAsync<Exception>();

        (await LocalOrdersRaw.GetByIdAsync(1, Ct)).Should().NotBeNull("業務削除は着地していない");
        var entry = (await Journal.ReadAllAsync(Ct)).Should().ContainSingle().Subject;
        entry.TableName.Should().Be("sync_orders");
        entry.Operation.Should().Be(nameof(SyncJournalOperation.Delete));

        var result = await Engine.SyncAsync(cancellationToken: Ct);

        result.Conflicts.Should().BeEmpty("着地しなかった意図は競合ではない");
        result.Uploaded.Should().Be(0);
        result.Discarded.Should().Be(1);
        (await ServerOrders.GetByIdAsync(1, Ct))
            .Should()
            .NotBeNull("失敗した削除をサーバーが完遂してはならない");
        (await LocalOrdersRaw.GetByIdAsync(1, Ct))
            .Should()
            .NotBeNull("同じランの削除伝搬がローカル行を道連れにしてもならない");
        (await Journal.CountPendingAsync(Ct)).Should().Be(0, "破棄されたエントリは掃除される");
    }

    /// <summary>
    /// 決着したエントリが 1 回の IN 展開の上限を超える件数でも、1 回の同期で掃除される。
    /// </summary>
    /// <remarks>
    /// ジャーナルの掃除はランの全テーブル分をまとめて 1 回で行うため、大量の一括追加や長期オフラインでは
    /// 件数がバインド変数の上限へ届き得る。掃除はチャンク分割されており、境界（500 件）を跨いでも
    /// 取りこぼしが出ない。
    /// </remarks>
    [Fact(DisplayName = "[Sync] 決着したエントリが 500 件を超えても 1 回の同期で掃除される")]
    public async Task JournalSettle_HandlesMoreEntriesThanOneInClause()
    {
        const int Count = 600;

        await LocalOrders.BulkInsertAsync(
            Enumerable
                .Range(1, Count)
                .Select(id => new SyncOrderEntity { OrderId = id, CustomerName = $"customer-{id}" })
                .ToList(),
            Ct
        );
        (await Journal.CountPendingAsync(Ct)).Should().Be(Count);

        var result = await Engine.SyncAsync(cancellationToken: Ct);

        result.Conflicts.Should().BeEmpty();
        result.Uploaded.Should().Be(Count);
        (await Journal.CountPendingAsync(Ct))
            .Should()
            .Be(0, "チャンク分割しても掃除の取りこぼしは出ない");
        (await ServerOrders.GetAllAsync(Ct)).Should().HaveCount(Count);
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

    // ---- アップロードとダウンロードの噛み合わせ（1 回のランの内側） ----

    /// <summary>
    /// 未送信のローカル編集を含むランでも、別の行のサーバー変更は同じランで降りてくる。
    /// </summary>
    /// <remarks>
    /// ダウンロードの再開点はミラー版列の<b>テーブル全体の MAX</b> なので、アップロードで採番された新しい版を
    /// ミラーへ書き戻すと、その値が「まだ降ろしていない別の行のサーバー版」を追い越し得る。追い越された行の
    /// 変更はアンカーより下に沈むため、次のランでも、その次のランでも降りてこない（恒久的な取りこぼし）。
    /// </remarks>
    [Fact(
        DisplayName = "[Sync] 未送信のローカル編集があるランでも他の行のサーバー変更を取りこぼさない"
    )]
    public async Task PendingLocalEditUpload_DoesNotHideOtherRowsServerChange()
    {
        await SeedServerAsync(1, "alice", 11, "widget");
        await SeedServerAsync(2, "bob", 12, "gadget");
        await Engine.SyncAsync(cancellationToken: Ct);

        // (1) 先にサーバー側で行 B を更新する（これがローカルへ降りてくるべき変更）
        var serverB = await ServerOrders.GetByIdAsync(2, Ct);
        serverB!.CustomerName = "bob-server";
        await ServerOrders.UpdateAsync(serverB, cancellationToken: Ct);

        // (2) 次にローカルで行 A を編集する（ジャーナルへ記録＝このあとのランでアップロードされる）
        var localA = await LocalOrders.GetByIdAsync(1, Ct);
        localA!.CustomerName = "alice-local";
        await LocalOrders.UpdateAsync(localA, cancellationToken: Ct);

        // (3) 1 回のランでアップロード→ダウンロードが続けて走る
        var result = await Engine.SyncAsync(cancellationToken: Ct);

        result.Uploaded.Should().Be(1);
        result.Conflicts.Should().BeEmpty();
        (await ServerOrders.GetByIdAsync(1, Ct))!.CustomerName.Should().Be("alice-local");
        (await LocalOrdersRaw.GetByIdAsync(2, Ct))!
            .CustomerName.Should()
            .Be(
                "bob-server",
                "自分がアップロードした行の新しい版が、まだ降ろしていない行のサーバー版を追い越してはならない"
            );
    }

    /// <summary>
    /// 収集された競合のローカル行は、同じランのダウンロードでサーバー内容に上書きされない。
    /// </summary>
    /// <remarks>
    /// 既定のポリシー（収集）は「どちらが勝つかを engine は決めない」という宣言なので、報告したその足で
    /// ダウンロードがローカルの編集を消してしまっては報告が無意味になる（ジャーナルには残るが、送るべき
    /// 内容そのものはサーバー行に化けているため、次のランでサーバーの値をサーバーへ送り返すだけになる）。
    /// </remarks>
    [Fact(DisplayName = "[Sync] 収集した競合のローカル行は同じランのダウンロードで上書きされない")]
    public async Task CollectedConflict_PreservesLocalRowContent()
    {
        await SeedServerAsync(1, "alice", 11, "widget");
        await Engine.SyncAsync(cancellationToken: Ct);

        // ローカルが先に編集し、そのあとサーバーが同じ行を編集する（＝サーバー側が新しい版）
        var local = await LocalOrders.GetByIdAsync(1, Ct);
        local!.CustomerName = "local-edit";
        await LocalOrders.UpdateAsync(local, cancellationToken: Ct);

        var server = await ServerOrders.GetByIdAsync(1, Ct);
        server!.CustomerName = "server-edit";
        await ServerOrders.UpdateAsync(server, cancellationToken: Ct);

        var result = await Engine.SyncAsync(cancellationToken: Ct);

        var conflict = result.Conflicts.Should().ContainSingle().Subject;
        conflict.TableName.Should().Be("sync_orders");
        conflict.Reason.Should().Be(SyncConflictReason.ModifiedOnServer);
        (await Journal.CountPendingAsync(Ct))
            .Should()
            .Be(1, "解決しなかったエントリはジャーナルに残る");
        (await LocalOrdersRaw.GetByIdAsync(1, Ct))!
            .CustomerName.Should()
            .Be(
                "local-edit",
                "未解決の競合はローカルの編集を保持する（同じランのダウンロードが黙って上書きしない）"
            );
    }

    /// <summary>
    /// 未解決の競合はそのテーブルのダウンロードを打ち切り、後続テーブルも同じランでは降ろさない。
    /// </summary>
    /// <remarks>
    /// 打ち切りの手前までしか適用しないということは、打ち切り点より後ろの<b>新しい親</b>もローカルへ来ないという
    /// こと。その親を参照する子を子テーブル側が降ろすと外部キー違反でランごと落ちるため、後続テーブルは
    /// まとめて見送る（どの子が「降ろさなかった親」を指すかは、読まなかったバッチを読まない限り分からない）。
    /// </remarks>
    [Fact(DisplayName = "[Sync] 競合で打ち切ったテーブルの後続テーブルは同じランでは降ろさない")]
    public async Task CollectedConflict_TruncatesFollowingTablesInsteadOfBreakingForeignKeys()
    {
        await ArrangeOrderConflictWithNewerServerRowsAsync();

        var result = await Engine.SyncAsync(cancellationToken: Ct);

        result.Conflicts.Should().ContainSingle().Which.TableName.Should().Be("sync_orders");

        var ordersStop = result
            .Truncations.Should()
            .ContainSingle(truncation => truncation.TableName == "sync_orders")
            .Subject;
        ordersStop.PendingKeyText.Should().Be("1", "打ち切った行のキーを名指しする");
        ordersStop.TruncatedByTableName.Should().BeNull("自分の未送信の変更で止まった");

        var linesStop = result
            .Truncations.Should()
            .ContainSingle(truncation => truncation.TableName == "sync_order_lines")
            .Subject;
        linesStop.PendingKeyText.Should().BeNull();
        linesStop.TruncatedByTableName.Should().Be("sync_orders", "止めたテーブルを名指しする");

        (await LocalOrdersRaw.GetByIdAsync(2, Ct))
            .Should()
            .BeNull("打ち切り点より後ろの行は適用しない");
        (await LocalLinesRaw.GetByIdAsync(12, Ct))
            .Should()
            .BeNull("降ろしていない親を参照する子も降ろさない");
    }

    /// <summary>
    /// 競合を捨てた次のランでは、競合行のサーバー内容も打ち切りで滞留していた後続分もまとめて降りてくる。
    /// </summary>
    /// <remarks>
    /// 打ち切りは「解決するまで待つ」であって「二度と降ろさない」ではない。止めていたエントリが無くなれば、
    /// 再開点（ミラー版の最大値）は打ち切り点の手前のままなので、そこから続きが読まれる。
    /// </remarks>
    [Fact(DisplayName = "[Sync] 競合を捨てた次のランで滞留していた変更がまとめて降りてくる")]
    public async Task DiscardingTheConflict_LetsTheNextRunCatchUp()
    {
        await ArrangeOrderConflictWithNewerServerRowsAsync();
        await Engine.SyncAsync(cancellationToken: Ct);

        // 競合したローカル編集を捨てる（サーバーを正とする、という利用者の判断）
        await Journal.RemoveTableAsync("sync_orders", Ct);

        var result = await Engine.SyncAsync(cancellationToken: Ct);

        result.Conflicts.Should().BeEmpty();
        result.Truncations.Should().BeEmpty("止めていたエントリが無くなれば打ち切る理由も無い");
        (await LocalOrdersRaw.GetByIdAsync(1, Ct))!
            .CustomerName.Should()
            .Be("server-edit", "競合行のサーバー内容が降りてくる");
        (await LocalOrdersRaw.GetByIdAsync(2, Ct))
            .Should()
            .NotBeNull("打ち切りで滞留していた新しい親も降りてくる");
        (await LocalLinesRaw.GetByIdAsync(12, Ct))
            .Should()
            .NotBeNull("後続テーブルの分も同じランで追いつく");
    }

    /// <summary>
    /// 削除の競合が残っている行を、同じランのダウンロードが降ろし直して復活させない。
    /// </summary>
    /// <remarks>
    /// ローカルで消した行はサーバーで更新されていて競合になる。ジャーナルに残った削除の意図を
    /// 打ち消す形でサーバー行を書き戻すと、利用者が決める前に「削除は無かったこと」になる。
    /// </remarks>
    [Fact(DisplayName = "[Sync] 削除の競合が残っている行はダウンロードで復活しない")]
    public async Task DeleteConflict_IsNotResurrectedByTheSameRunsDownload()
    {
        await SeedServerAsync(1, "alice", 11, "widget");
        await Engine.SyncAsync(cancellationToken: Ct);

        await LocalLines.DeleteAsync(11, Ct);

        var server = await ServerLines.GetByIdAsync(11, Ct);
        server!.Product = "widget-v2";
        await ServerLines.UpdateAsync(server, cancellationToken: Ct);

        var result = await Engine.SyncAsync(cancellationToken: Ct);

        result
            .Conflicts.Should()
            .ContainSingle()
            .Which.Operation.Should()
            .Be(SyncJournalOperation.Delete);
        (await LocalLinesRaw.GetByIdAsync(11, Ct))
            .Should()
            .BeNull("未解決の削除を同じランのダウンロードが取り消してはならない");

        var stop = result
            .Truncations.Should()
            .ContainSingle(truncation => truncation.TableName == "sync_order_lines")
            .Subject;
        stop.PendingKeyText.Should().Be("11");
        stop.TruncatedByTableName.Should().BeNull();
    }

    /// <summary>
    /// sync_orders に未解決の競合を作り、そのサーバー版より<b>後ろ</b>に新しい親と、その親を参照する子を置く。
    /// </summary>
    private async Task ArrangeOrderConflictWithNewerServerRowsAsync()
    {
        await SeedServerAsync(1, "alice", 11, "widget");
        await Engine.SyncAsync(cancellationToken: Ct);

        var local = await LocalOrders.GetByIdAsync(1, Ct);
        local!.CustomerName = "local-edit";
        await LocalOrders.UpdateAsync(local, cancellationToken: Ct);

        var server = await ServerOrders.GetByIdAsync(1, Ct);
        server!.CustomerName = "server-edit";
        await ServerOrders.UpdateAsync(server, cancellationToken: Ct);

        // 競合行より新しい版を持つ親と、その親を参照する子（＝打ち切り点より後ろにある）
        await ServerOrders.InsertAsync(
            new SyncOrderEntity { OrderId = 2, CustomerName = "bob" },
            Ct
        );
        await ServerLines.InsertAsync(
            new SyncOrderLineEntity
            {
                LineId = 12,
                OrderId = 2,
                Product = "gizmo",
            },
            Ct
        );
    }

    /// <summary>
    /// アップロードのあとに他者が同じ行を更新していれば、その内容は同じランのダウンロードで降りてくる。
    /// </summary>
    /// <remarks>
    /// 「自分が上げた行は降ろし直さない」という最適化を、キーの一致だけで判断すると成り立たない網。
    /// アップロード直後に別の書き手が同じ行を触っていれば、降りてきた行は<b>自分が上げた版とは違う</b>ため
    /// 通常どおり適用しなければならない。割り込みは差分ソースをデコレートして、ダウンロードが最初にサーバーへ
    /// 問い合わせる時点（＝アップロード完了後・行の適用前）にサーバーを更新することで作る。
    /// </remarks>
    [Fact(
        DisplayName = "[Sync] アップロード後に他者が更新した同じ行は同じランのダウンロードで降りてくる"
    )]
    public async Task ServerUpdateOfSameRowAfterUpload_IsStillDownloaded()
    {
        await SeedServerAsync(1, "alice", 11, "widget");
        await Engine.SyncAsync(cancellationToken: Ct);

        var local = await LocalOrders.GetByIdAsync(1, Ct);
        local!.CustomerName = "alice-local";
        await LocalOrders.UpdateAsync(local, cancellationToken: Ct);

        var interleaved = new ServerUpdateOnFirstFetchOrderSource(
            _sources.Orders,
            async cancellationToken =>
            {
                var server = await ServerOrders.GetByIdAsync(1, cancellationToken);
                server!.CustomerName = "alice-server-later";
                await ServerOrders.UpdateAsync(server, cancellationToken: cancellationToken);
            }
        );

        var result = await CreateEngineWithOrderSource(interleaved)
            .SyncAsync(cancellationToken: Ct);

        result.Uploaded.Should().Be(1, "ローカルの編集は通常どおり送られる");
        (await LocalOrdersRaw.GetByIdAsync(1, Ct))!
            .CustomerName.Should()
            .Be(
                "alice-server-later",
                "アップロード後に他者が同じ行を更新していれば、その版は自分が上げた版と異なる＝通常どおり適用される"
            );
    }

    /// <summary>
    /// ダウンロードが最初にサーバーへ問い合わせる直前に、一度だけサーバーを触る差分ソース
    /// （ランの内側＝アップロード完了後・ダウンロード開始時へ割り込むためのテストダブル）。
    /// </summary>
    /// <remarks>
    /// 割り込みは<b>上限（ceiling）の取得</b>にも掛ける。上限はダウンロードの先頭で 1 回だけ取られ、実 SQL Server
    /// では <c>MIN_ACTIVE_ROWVERSION()</c> がその時点の値を返すため、上限を取ったあとに書いた行は上限より上＝
    /// その回の対象外になる。「アップロードの直後・ダウンロードが読む前」を作るには上限より先に書く必要がある。
    /// </remarks>
    private sealed class ServerUpdateOnFirstFetchOrderSource(
        ISyncServerSource<SyncOrderEntity, int> inner,
        Func<CancellationToken, Task> onFirstFetch
    ) : ISyncServerSource<SyncOrderEntity, int>
    {
        private int _fired;

        public IRemoteRepositoryCore<SyncOrderEntity, int> Writer => inner.Writer;

        public ISyncBinaryColumns<int>? BinaryColumns => inner.BinaryColumns;

        public async Task<byte[]?> GetChangeCeilingAsync(
            CancellationToken cancellationToken = default
        )
        {
            await FireOnceAsync(cancellationToken);

            return await inner.GetChangeCeilingAsync(cancellationToken);
        }

        public async Task<SyncChangeBatch<SyncOrderEntity>> GetChangesAsync(
            byte[]? anchor,
            byte[]? ceiling,
            int batchSize,
            CancellationToken cancellationToken = default
        )
        {
            await FireOnceAsync(cancellationToken);

            return await inner.GetChangesAsync(anchor, ceiling, batchSize, cancellationToken);
        }

        /// <summary>割り込みは 1 回だけ（毎回サーバーを進めると再開点が追いつかずダウンロードが終わらない）</summary>
        private async Task FireOnceAsync(CancellationToken cancellationToken)
        {
            if (Interlocked.Exchange(ref _fired, 1) == 0)
            {
                await onFirstFetch(cancellationToken);
            }
        }

        public Task<IReadOnlyList<int>> GetAllKeysAsync(
            CancellationToken cancellationToken = default
        ) => inner.GetAllKeysAsync(cancellationToken);
    }

    // ---- 受理記録（ack）＝ダウンロードが自分の行へ届かなかったランの後始末 ----

    /// <summary>
    /// 打ち切りの裏に回ってエコーが降りてこなかった行を再編集しても、偽の更新競合にならない。
    /// </summary>
    /// <remarks>
    /// アップロードはサーバーが採番した版をミラーへ書かない（ミラーを動かしてよいのはダウンロードだけ）。
    /// そのため「同じテーブルの別の行が未送信で打ち切られた」ランでは、自分が上げた行のエコーが打ち切り点の
    /// 裏に回ってミラーが旧版のまま残る。次に同じ行を編集すると、旧ミラーを original にした Optimistic 更新が
    /// 「自分が上げた版」と食い違う＝誰とも競合していないのに ModifiedOnServer になる。受理記録（ack）は
    /// この隙間を埋めるためのもので、再生は max(ミラー版, ack 版) を original に採る。
    /// </remarks>
    [Fact(
        DisplayName = "[Sync/ack] 打ち切りでエコーが降りなかった行の再編集は偽の更新競合にならない"
    )]
    public async Task TruncatedEcho_ThenLocalEdit_DoesNotReportFalseModifiedConflict()
    {
        await SeedServerAsync(1, "alice", 11, "widget");
        await ServerOrders.InsertAsync(
            new SyncOrderEntity { OrderId = 3, CustomerName = "carol" },
            Ct
        );
        await Engine.SyncAsync(cancellationToken: Ct);

        // 行 1 に未解決の競合を作る（＝以降のランはこの行でダウンロードを打ち切る）
        await ArrangeOrderConflictOnRowOneAsync();

        // 行 3 を編集して 1 回ラン：アップロードは通るが、エコーは打ち切り点（行 1）の裏に回る
        await EditLocalOrderAsync(3, "carol-1");
        var blocked = await Engine.SyncAsync(cancellationToken: Ct);
        blocked.Uploaded.Should().Be(1, "行 3 のアップロードそのものは成功する");
        blocked
            .Truncations.Should()
            .Contain(truncation =>
                truncation.TableName == "sync_orders" && truncation.PendingKeyText == "1"
            );

        // 同じ行をもう一度編集する（ミラーは旧版のまま＝ここが偽競合の入口）
        await EditLocalOrderAsync(3, "carol-2");

        var result = await Engine.SyncAsync(cancellationToken: Ct);

        result.Uploaded.Should().Be(1, "再編集も通常どおり送られる");
        result
            .Conflicts.Should()
            .ContainSingle("競合しているのは行 1 だけ")
            .Which.KeyText.Should()
            .Be("1");
        (await ServerOrders.GetByIdAsync(3, Ct))!.CustomerName.Should().Be("carol-2");
    }

    /// <summary>
    /// アップロード成功後にダウンロードが落ちたランの続きでも、再編集が偽の更新競合にならない。
    /// </summary>
    /// <remarks>
    /// 打ち切りと並ぶもう 1 つの「エコーが届かない」経路＝ダウンロードの例外。受理記録があれば次のランの
    /// 再生は正しい original を使い、かつ降りてきた自分の行をエコーと判定できる（＝内容適用ゼロ）。
    /// </remarks>
    [Fact(
        DisplayName = "[Sync/ack] ダウンロードが落ちたランの後でも再編集は偽競合にならずエコーも効く"
    )]
    public async Task FailedDownload_ThenLocalEdit_UploadsAndStillDetectsEcho()
    {
        await SeedServerAsync(1, "alice", 11, "widget");
        await Engine.SyncAsync(cancellationToken: Ct);

        await EditLocalOrderAsync(1, "alice-1");

        // アップロードは通し、ダウンロードの最初のサーバー問い合わせだけを 1 回落とす
        var failing = new FailOnFirstFetchOrderSource(_sources.Orders);
        var act = async () =>
            await CreateEngineWithOrderSource(failing).SyncAsync(cancellationToken: Ct);
        await act.Should().ThrowAsync<InvalidOperationException>();
        (await ServerOrders.GetByIdAsync(1, Ct))!
            .CustomerName.Should()
            .Be("alice-1", "落ちたのはダウンロード側＝アップロードは届いている");

        await EditLocalOrderAsync(1, "alice-2");

        var result = await Engine.SyncAsync(cancellationToken: Ct);

        result.Uploaded.Should().Be(1);
        result.Conflicts.Should().BeEmpty("誰とも競合していない");
        result
            .Downloaded.Should()
            .Be(0, "自分が上げた行は受理記録と版が一致する＝エコーとして版だけ書く");
        (await ServerOrders.GetByIdAsync(1, Ct))!.CustomerName.Should().Be("alice-2");
    }

    /// <summary>
    /// 打ち切りの裏に回ったオフライン挿入を再編集しても、偽の重複競合にならない。
    /// </summary>
    /// <remarks>
    /// ミラー版なし＝「サーバーに出たことがない行」という判定は、受理記録が無い前提でしか成り立たない。
    /// 一度アップロードした行のエコーが降りていなければミラーは空のままなので、受理記録を見ない再生は
    /// 自分が挿入した行を「サーバーに同じキーがある」＝ DuplicateOnServer として報告してしまう。
    /// </remarks>
    [Fact(
        DisplayName = "[Sync/ack] 打ち切りの裏に回ったオフライン挿入の再編集は偽の重複競合にならない"
    )]
    public async Task TruncatedEchoOfOfflineInsert_ThenLocalEdit_DoesNotReportFalseDuplicate()
    {
        await SeedServerAsync(1, "alice", 11, "widget");
        await Engine.SyncAsync(cancellationToken: Ct);

        await ArrangeOrderConflictOnRowOneAsync();

        // オフラインで作った行（ミラー版なし）を 1 回のランで送る＝エコーは打ち切りの裏
        await LocalOrders.InsertAsync(
            new SyncOrderEntity { OrderId = 4, CustomerName = "dave" },
            Ct
        );
        (await Engine.SyncAsync(cancellationToken: Ct)).Uploaded.Should().Be(1);

        await EditLocalOrderAsync(4, "dave-2");

        var result = await Engine.SyncAsync(cancellationToken: Ct);

        result.Uploaded.Should().Be(1);
        result
            .Conflicts.Should()
            .ContainSingle("競合しているのは行 1 だけ")
            .Which.KeyText.Should()
            .Be("1");
        (await ServerOrders.GetByIdAsync(4, Ct))!.CustomerName.Should().Be("dave-2");
    }

    /// <summary>
    /// 打ち切りの裏に回った行をローカルで削除しても、偽の削除競合にならない。
    /// </summary>
    /// <remarks>
    /// 削除の再生はジャーナルが記録した削除時点のミラー版を original に載せる。エコーが届いていなければ
    /// その値は旧版なので、受理記録を見ないと「サーバーで更新された」と読み違える。
    /// </remarks>
    [Fact(DisplayName = "[Sync/ack] 打ち切りの裏に回った行のローカル削除は偽の削除競合にならない")]
    public async Task TruncatedEcho_ThenLocalDelete_DoesNotReportFalseDeleteConflict()
    {
        await SeedServerAsync(1, "alice", 11, "widget");
        await ServerOrders.InsertAsync(
            new SyncOrderEntity { OrderId = 3, CustomerName = "carol" },
            Ct
        );
        await Engine.SyncAsync(cancellationToken: Ct);

        await ArrangeOrderConflictOnRowOneAsync();

        await EditLocalOrderAsync(3, "carol-1");
        (await Engine.SyncAsync(cancellationToken: Ct)).Uploaded.Should().Be(1);

        // エコーが届いていない行を削除する（ジャーナルには旧ミラー版が載る）
        await LocalOrders.DeleteAsync(3, Ct);

        var result = await Engine.SyncAsync(cancellationToken: Ct);

        result.Uploaded.Should().Be(1, "削除はサーバーへ届く");
        result
            .Conflicts.Should()
            .ContainSingle("競合しているのは行 1 だけ")
            .Which.KeyText.Should()
            .Be("1");
        (await ServerOrders.GetByIdAsync(3, Ct)).Should().BeNull();
    }

    /// <summary>
    /// 削除伝搬で消えた行の受理記録は残さない（同じキーの作り直しが偽の不在競合にならない）。
    /// </summary>
    /// <remarks>
    /// 受理記録は「この行のサーバー版はこれ」という行単位の事実なので、その行がサーバーから消えた時点で
    /// 嘘になる。掃除しないと、同じキーでオフライン挿入した行の再生が死んだ版を original に採り、
    /// サーバーに無い行を更新しようとして MissingOnServer を報告する。
    /// </remarks>
    [Fact(
        DisplayName = "[Sync/ack] 削除伝搬で消えた行の受理記録は残らない（同キー再作成が競合しない）"
    )]
    public async Task DeletePropagation_DropsAcknowledgement_SoReinsertOfSameKeyIsClean()
    {
        await SeedServerAsync(1, "alice", 11, "widget");
        await ServerOrders.InsertAsync(
            new SyncOrderEntity { OrderId = 2, CustomerName = "bob" },
            Ct
        );
        await Engine.SyncAsync(cancellationToken: Ct);

        // アップロードだけを走らせる＝受理記録は残り、エコーは降りてこない
        await EditLocalOrderAsync(2, "bob-local");
        var uploadConflicts = new List<SyncConflict>();
        await Engine.UploadAsync(new SyncOptions(), uploadConflicts, Ct);
        uploadConflicts.Should().BeEmpty();

        // サーバーがその行を消す → 次のランの削除伝搬でローカルからも消える
        await ServerOrders.DeleteAsync(2, Ct);
        await Engine.SyncAsync(cancellationToken: Ct);
        (await LocalOrdersRaw.GetByIdAsync(2, Ct))
            .Should()
            .BeNull("サーバーに無いキーは削除伝搬で消える");

        // 同じキーをローカルで作り直す（オフライン挿入＝ミラー版なし）
        await LocalOrders.InsertAsync(
            new SyncOrderEntity { OrderId = 2, CustomerName = "bob-again" },
            Ct
        );

        var result = await Engine.SyncAsync(cancellationToken: Ct);

        result.Uploaded.Should().Be(1);
        result.Conflicts.Should().BeEmpty("死んだ受理記録が残っていなければ普通の INSERT になる");
        (await ServerOrders.GetByIdAsync(2, Ct))!.CustomerName.Should().Be("bob-again");
    }

    /// <summary>
    /// ServerWins が復活させた行を再編集しても、偽の重複競合にならない。
    /// </summary>
    /// <remarks>
    /// 読み直し適用はミラー版を据え置く（＝接頭辞不変条件）。復活した行にはミラー版が無く、その行のサーバー版が
    /// すでにアンカーの下にあれば<b>ダウンロードは二度とその行へ届かない</b>＝ミラーは空のまま残る。受理記録が
    /// 無いと、次の編集の再生が「サーバーに出たことがない行」と読んでオフライン挿入へ倒れ、サーバーに同じキーが
    /// ある＝ DuplicateOnServer を報告する（既定の Collect ではサーバーがその行を変えるまで毎ラン続く）。
    /// 適用した行の受理記録がこの穴を塞ぐ。
    /// </remarks>
    [Fact(DisplayName = "[Sync/ack] ServerWins が復活させた行の再編集は偽の重複競合にならない")]
    public async Task ServerWinsRestoredRow_ThenLocalEdit_DoesNotReportFalseDuplicate()
    {
        // 削除する行（11）の版がアンカーの下に潜る形を作る（12 が後から入って版を進める）＝
        // こうしないとダウンロードが勝手に取り戻してミラー版が入り、穴が塞がって見えてしまう
        await SeedServerAsync(1, "alice", 11, "widget");
        await ServerLines.InsertAsync(
            new SyncOrderLineEntity
            {
                LineId = 12,
                OrderId = 1,
                Product = "gadget",
            },
            Ct
        );
        await Engine.SyncAsync(cancellationToken: Ct);

        (await LocalLines.DeleteAsync(11, Ct)).Should().BeTrue();
        await Engine.SyncAsync(
            new SyncOptions { ConflictPolicy = SyncConflictPolicy.ServerWins },
            Ct
        );

        var restored = await LocalLinesRaw.GetByIdAsync(11, Ct);
        restored.Should().NotBeNull("ServerWins は削除した行をサーバーから読み直して戻す");
        restored!.RowVer.Should().BeNull("復活行にミラー版は入らない＝据え置きの帰結");

        // 復活した行を編集する（ミラー版なし＝ここが偽の重複競合の入口）
        var edited = await LocalLines.GetByIdAsync(11, Ct);
        edited!.Product = "widget-2";
        await LocalLines.UpdateAsync(edited, cancellationToken: Ct);

        var result = await Engine.SyncAsync(cancellationToken: Ct);

        result
            .Conflicts.Should()
            .BeEmpty("適用時のサーバー版が受理記録に残っている＝普通の更新として送られる");
        result.Uploaded.Should().Be(1);
        (await ServerLines.GetByIdAsync(11, Ct))!.Product.Should().Be("widget-2");
    }

    /// <summary>
    /// ServerWins の適用後にダウンロードが落ちたランの続きでも、再編集が偽の更新競合にならない。
    /// </summary>
    /// <remarks>
    /// 適用はサーバーの内容だけを書いてミラー版を据え置くため、ダウンロードがその行へ届く前に落ちると
    /// ミラーは旧版のまま残る。受理記録が無ければ次の再生は旧版を original に採り、誰とも競合していないのに
    /// ModifiedOnServer になる（打ち切り経路と同型の穴が ServerWins 側にも開く）。
    /// </remarks>
    [Fact(
        DisplayName = "[Sync/ack] ServerWins の適用後にダウンロードが落ちても再編集は偽競合にならない"
    )]
    public async Task ServerWinsApplied_ThenFailedDownload_ThenLocalEdit_DoesNotReportFalseModifiedConflict()
    {
        await SeedServerAsync(1, "alice", 11, "widget");
        await Engine.SyncAsync(cancellationToken: Ct);

        // ローカルの編集と、他者によるサーバー側の更新（＝サーバー版がミラーより進む）
        await EditLocalOrderAsync(1, "alice-local");
        var server = await ServerOrders.GetByIdAsync(1, Ct);
        server!.CustomerName = "alice-server";
        await ServerOrders.UpdateAsync(server, cancellationToken: Ct);

        // ServerWins の適用は通し、そのあとのダウンロードだけを 1 回落とす
        var failing = new FailOnFirstFetchOrderSource(_sources.Orders);
        var act = async () =>
            await CreateEngineWithOrderSource(failing)
                .SyncAsync(new SyncOptions { ConflictPolicy = SyncConflictPolicy.ServerWins }, Ct);
        await act.Should().ThrowAsync<InvalidOperationException>();
        (await LocalOrdersRaw.GetByIdAsync(1, Ct))!
            .CustomerName.Should()
            .Be("alice-server", "落ちたのはダウンロード側＝適用は済んでいる");

        // 適用された行を編集する（ミラーは旧版のまま＝ここが偽の更新競合の入口）
        await EditLocalOrderAsync(1, "alice-2");

        var result = await Engine.SyncAsync(cancellationToken: Ct);

        result
            .Conflicts.Should()
            .BeEmpty("受理記録が適用時のサーバー版を覚えている＝誰とも競合していない");
        result.Uploaded.Should().Be(1);
        (await ServerOrders.GetByIdAsync(1, Ct))!.CustomerName.Should().Be("alice-2");
    }

    /// <summary>行 1 に未解決の更新競合を作る（以降のランはこの行でダウンロードを打ち切る）</summary>
    private async Task ArrangeOrderConflictOnRowOneAsync()
    {
        await EditLocalOrderAsync(1, "alice-local");

        var server = await ServerOrders.GetByIdAsync(1, Ct);
        server!.CustomerName = "alice-server";
        await ServerOrders.UpdateAsync(server, cancellationToken: Ct);
    }

    /// <summary>ローカルの注文を 1 行編集する（デコレータ経由＝ジャーナルへ記録される）</summary>
    private async Task EditLocalOrderAsync(int orderId, string customerName)
    {
        var local = await LocalOrders.GetByIdAsync(orderId, Ct);
        local!.CustomerName = customerName;
        await LocalOrders.UpdateAsync(local, cancellationToken: Ct);
    }

    /// <summary>
    /// ダウンロードが最初にサーバーへ問い合わせた 1 回だけ落ちる差分ソース
    /// （アップロード成功後・ダウンロード未了のランを作るためのテストダブル）。
    /// </summary>
    private sealed class FailOnFirstFetchOrderSource(ISyncServerSource<SyncOrderEntity, int> inner)
        : ISyncServerSource<SyncOrderEntity, int>
    {
        private int _fired;

        public IRemoteRepositoryCore<SyncOrderEntity, int> Writer => inner.Writer;

        public ISyncBinaryColumns<int>? BinaryColumns => inner.BinaryColumns;

        public async Task<byte[]?> GetChangeCeilingAsync(
            CancellationToken cancellationToken = default
        )
        {
            FailOnce();

            return await inner.GetChangeCeilingAsync(cancellationToken);
        }

        public async Task<SyncChangeBatch<SyncOrderEntity>> GetChangesAsync(
            byte[]? anchor,
            byte[]? ceiling,
            int batchSize,
            CancellationToken cancellationToken = default
        )
        {
            FailOnce();

            return await inner.GetChangesAsync(anchor, ceiling, batchSize, cancellationToken);
        }

        public Task<IReadOnlyList<int>> GetAllKeysAsync(
            CancellationToken cancellationToken = default
        ) => inner.GetAllKeysAsync(cancellationToken);

        /// <summary>1 回だけ落ちる（2 回目以降は素通し＝後続のランは通常どおり走る）</summary>
        private void FailOnce()
        {
            if (Interlocked.Exchange(ref _fired, 1) == 0)
            {
                throw new InvalidOperationException("download failed on purpose");
            }
        }
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
            .Equal("sync_orders", "sync_order_lines", "sync_notes");
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
