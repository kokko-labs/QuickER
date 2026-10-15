using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.Data.Sqlite;
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

    /// <summary>ローカル DB の接続文字列（接続の設定を変えた部品を組むシナリオ用）</summary>
    protected string LocalConnectionString => _local.ReadWriteCreateConnectionString;

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

        var serverFactory = new LightJournalSyncConnectionFactory(
            _server.ReadWriteCreateConnectionString
        );
        var localFactory = new LightJournalSyncConnectionFactory(
            _local.ReadWriteCreateConnectionString
        );
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
        CreateEngineWithOrderTable(
            new SyncTable<SyncOrderEntity, int>(
                LocalOrders,
                LocalSql,
                orders,
                GeneratedSyncTables.SyncOrder
            )
        );

    /// <summary>
    /// 注文テーブルの部品（差分ソース・ローカルの生 SQL 実行器）を丸ごと差し替えた同期エンジンを組み立てる
    /// （ジャーナルと他のテーブルは本体の <see cref="Engine"/> と同じ＝ロックも同じものを共有する）。
    /// </summary>
    protected SyncEngine CreateEngineWithOrderTable(ISyncTable orders) =>
        new(
            [
                orders,
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

    /// <summary>
    /// NULL 許容の計算列を持つテーブルでもダウンロードが成立し、ミラー側の値は NULL のままになる。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 計算列は差分 SQL の SELECT には載る（行としては転送される）が、生成コードの INSERT からは
    /// 方言に依らず外れるため、式を持たないローカルへは書かれない。つまりミラーの値は NULL のまま——
    /// これは<b>NULL 許容だから成立する</b>のであって、同じ列が NOT NULL なら送られない列の
    /// NOT NULL 制約でダウンロードが丸ごと落ちる。「NOT NULL の計算列は図で NULL 許容にする」という
    /// 逃げ道が実際に機能することを、ここが端から端まで押さえる
    /// （生成時 Warning <c>CodeGen_Warning_NotNullComputedColumns</c> が案内している逃げ道そのもの）。
    /// </para>
    /// <para>
    /// サーバー側の値は生 SQL で入れる（リポジトリからは書けない列なので、書ける唯一の経路）。
    /// SQL は SQLite / SQL Server のどちらのサーバー役でもそのまま通る形にしてある。
    /// </para>
    /// </remarks>
    [Fact(DisplayName = "[Sync] NULL 許容の計算列は転送されても、ミラー側は NULL のまま取り込める")]
    public async Task Download_WithNullableComputedColumn_SucceedsAndKeepsMirrorNull()
    {
        await SeedServerAsync(1, "alice", 11, "widget");
        await ServerSql.ExecuteSqlAsync(
            "UPDATE sync_orders SET summary = 'server-side summary' WHERE order_id = 1",
            null,
            Ct
        );

        var result = await Engine.SyncAsync(cancellationToken: Ct);

        result.Conflicts.Should().BeEmpty();
        result.Downloaded.Should().Be(2, "注文 1 件＋明細 1 件が降りる");

        var order = await LocalOrdersRaw.GetByIdAsync(1, Ct);
        order.Should().NotBeNull();
        order!.CustomerName.Should().Be("alice");
        order.Summary.Should().BeNull("式を持たないミラーへは計算列が書かれない（NULL のまま）");

        // サーバー側には値が在ったこと（＝転送すべき値が無かったから NULL なのではない）
        (
            await ServerSql.ExecuteScalarSqlAsync<string>(
                "SELECT summary FROM sync_orders WHERE order_id = 1",
                null,
                Ct
            )
        )
            .Should()
            .Be("server-side summary");
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

    /// <summary>削除伝搬を切ると走査ごとスキップされる（どの表のローカルの行も残る）</summary>
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
        (await LocalLinesRaw.GetAllAsync(Ct)).Should().HaveCount(1);
    }

    /// <summary>
    /// 削除伝搬は、ローカルのどの行からも参照されている行を消さない。候補が 1 回の IN 展開の上限を超えても、
    /// 境界（500 件）の前後どちらの行への参照も見落とさない。
    /// </summary>
    /// <remarks>
    /// 参照する行は、このランの範囲外の表（明細）に置く。範囲外の表には削除伝搬が触れないので、その行が参照する
    /// 注文は残らなければならない（消すとローカルの外部キーで失敗する）。参照されていない注文は消える。
    /// </remarks>
    [Fact(
        DisplayName = "[Sync] 削除伝搬はローカルで参照されている行を残す（候補が 500 件を超えても）"
    )]
    public async Task DeletePropagation_KeepsLocallyReferencedRows_AcrossInClauseChunks()
    {
        const int Count = 600;
        int[] referenced = [1, 500, 501, Count];

        // 素のリポジトリ＝ジャーナルを通らない（未送信として守られる行ではない）
        await LocalOrdersRaw.BulkInsertAsync(
            Enumerable
                .Range(1, Count)
                .Select(id => new SyncOrderEntity { OrderId = id, CustomerName = $"local-{id}" })
                .ToList(),
            Ct
        );

        foreach (var id in referenced)
        {
            await LocalLinesRaw.InsertAsync(
                new SyncOrderLineEntity
                {
                    LineId = 10_000 + id,
                    OrderId = id,
                    Product = "local",
                },
                Ct
            );
        }

        var result = await Engine.SyncAsync(
            new SyncOptions { ExcludedEntityTypes = [typeof(SyncOrderLineEntity)] },
            Ct
        );

        result.DeletedLocally.Should().Be(Count - referenced.Length);
        (await LocalOrdersRaw.GetAllAsync(Ct))
            .Select(order => order.OrderId)
            .Should()
            .BeEquivalentTo(referenced, "範囲外の表の行が参照する注文は残す");
        (await LocalLinesRaw.GetAllAsync(Ct)).Should().HaveCount(referenced.Length);
    }

    /// <summary>
    /// 削除伝搬は、ローカル DB に無い子の表を照会しない（同期対象の表だけでローカル DB を組む構成でも止まらない）。
    /// </summary>
    /// <remarks>
    /// メモの表（版なし・注文を参照する）をローカル DB から消してから、サーバーで消えた注文を伝搬させる。参照の検査が
    /// 無い表を照会すると "no such table" で同期全体が止まる。表が無ければ参照する行も無いので、注文は消える。
    /// </remarks>
    [Fact(DisplayName = "[Sync] 削除伝搬はローカル DB に無い子の表を照会しない")]
    public async Task DeletePropagation_SkipsReferringTablesMissingLocally()
    {
        await ServerOrders.InsertAsync(
            new SyncOrderEntity { OrderId = 1, CustomerName = "alice" },
            Ct
        );
        await Engine.SyncAsync(cancellationToken: Ct);
        await LocalSql.ExecuteSqlAsync("DROP TABLE \"sync_notes\"", null, Ct);

        await ServerOrders.DeleteAsync(1, Ct);
        var result = await Engine.SyncAsync(cancellationToken: Ct);

        result.DeletedLocally.Should().Be(1);
        (await LocalOrdersRaw.GetAllAsync(Ct)).Should().BeEmpty();
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
    /// 業務削除が着地しないまま残った削除エントリは、サーバーへ送られず破棄される（journal-first の無害化）。
    /// </summary>
    /// <remarks>
    /// 削除は「送る内容」を持たないため、upsert のようにローカルの現在行を読み直すだけでは無害化されない。
    /// ローカル行がまだ在る＝業務削除が着地しなかったということなので、そのままサーバーを消すと
    /// 「失敗した操作が全体としては完遂される」うえ、続く削除伝搬がローカル行まで道連れにする。
    /// 業務削除が例外で終わった記録はデコレータが取り消すので（#16）、残るのは記録の後でプロセスごと落ちた場合。
    /// それをジャーナルへの直接の記録で作る（デコレータの記録の範囲の外＝取り消されない）。
    /// </remarks>
    [Fact(
        DisplayName = "[Sync] 業務削除が着地しないまま残った削除エントリは送られず破棄される（journal-first の無害化）"
    )]
    public async Task FailedLocalDelete_LeavesEntryThatIsDiscarded()
    {
        await SeedServerAsync(1, "alice", 11, "widget");
        await Engine.SyncAsync(cancellationToken: Ct);

        // 子（明細）が残っている親の削除は FK 違反で失敗し、デコレータはその記録を取り消す
        var deleting = async () => await LocalOrders.DeleteAsync(1, Ct);
        await deleting.Should().ThrowAsync<Exception>();
        (await Journal.ReadAllAsync(Ct)).Should().BeEmpty("例外で終わった書き込みの記録は取り消す");

        // 記録の後でプロセスごと落ちた形＝記録だけが残り、業務削除は着地していない
        await Journal.RecordAsync("sync_orders", "1", SyncJournalOperation.Delete, null, Ct);

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
    /// 未解決の競合はそのテーブルのダウンロードを打ち切るが、後続テーブルは続けて降ろす。
    /// </summary>
    /// <remarks>
    /// 打ち切りの手前までしか適用しないので、打ち切り点より後ろの<b>新しい親</b>はそのテーブルのダウンロードでは
    /// 来ない。その親を参照する子は、子テーブルのダウンロードが親をサーバーから先に読み直してから入れる（外部キー
    /// 違反にしない）。止まるのは打ち切ったテーブルだけで、<c>TruncatedByTableName</c> は常に null。
    /// </remarks>
    [Fact(
        DisplayName = "[Sync] 競合で打ち切ったテーブルの後続テーブルも降ろし、打ち切り点より後ろの親は先に読み直す"
    )]
    public async Task CollectedConflict_DownloadsFollowingTablesAndBringsParentsIn()
    {
        await ArrangeOrderConflictWithNewerServerRowsAsync();

        var result = await Engine.SyncAsync(cancellationToken: Ct);

        result.Conflicts.Should().ContainSingle().Which.TableName.Should().Be("sync_orders");

        var ordersStop = result
            .Truncations.Should()
            .ContainSingle(truncation => truncation.TableName == "sync_orders")
            .Subject;
        ordersStop.PendingKeyText.Should().Be("1", "打ち切った行のキーを名指しする");
        ordersStop.TruncatedByTableName.Should().BeNull("打ち切りは後ろの表へ連動させない");
        result
            .Truncations.Should()
            .ContainSingle("止まるのは打ち切ったテーブルだけ（後ろの表は続ける）");

        (await LocalOrdersRaw.GetByIdAsync(2, Ct))
            .Should()
            .NotBeNull("打ち切り点より後ろの親は、それを参照する子のために先に読み直す");
        (await LocalLinesRaw.GetByIdAsync(12, Ct)).Should().NotBeNull("後ろの表は続けて降ろす");
        (await LocalOrdersRaw.GetByIdAsync(1, Ct))!
            .CustomerName.Should()
            .Be("local-edit", "競合した行は打ち切ったまま触らない");
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

    // ---- 再生する操作の決め方（いまのローカルの行と、その行の記録全体）----

    /// <summary>
    /// 編集のあとに外部キーで拒否された削除があっても、編集はサーバーへ届く。
    /// </summary>
    /// <remarks>
    /// ジャーナルは業務書き込みの前に書くため、最新の記録が「着地しなかった削除」になり得る。最新の記録だけで操作を
    /// 決めると、行が残っている（削除は着地しなかった）ことを理由に、その行の記録が全部捨てられ、着地していた
    /// 編集まで二度と届かなくなる。行が残っていて追加・更新の記録を含むなら、いまの内容を送る。
    /// </remarks>
    [Fact(DisplayName = "[Sync/再生] 編集のあと外部キーで拒否された削除があっても編集は届く")]
    public async Task EditThenRefusedDelete_StillUploadsTheEdit()
    {
        await SeedServerAsync(1, "alice", 11, "widget");
        await Engine.SyncAsync(cancellationToken: Ct);

        var local = await LocalOrders.GetByIdAsync(1, Ct);
        local!.CustomerName = "alice-edited";
        await LocalOrders.UpdateAsync(local, cancellationToken: Ct);

        // 明細 11 が注文 1 を参照しているため、ローカルの外部キーが削除を拒否する
        var delete = async () => await LocalOrders.DeleteAsync(1, Ct);
        await delete.Should().ThrowAsync<Exception>();
        (await LocalOrdersRaw.GetByIdAsync(1, Ct)).Should().NotBeNull();

        var result = await Engine.SyncAsync(cancellationToken: Ct);

        result.Uploaded.Should().Be(1, "行が残り、更新の記録を含む＝いまの内容を送る");
        (await ServerOrders.GetByIdAsync(1, Ct))!.CustomerName.Should().Be("alice-edited");
        (await Journal.CountPendingAsync(Ct)).Should().Be(0);
    }

    /// <summary>
    /// 削除が着地したあと同じキーの再挿入が外部キーで失敗したら、サーバーからも消える。
    /// </summary>
    /// <remarks>
    /// 最新の記録（着地しなかった挿入）だけで決めると「送るものが無い」として捨てられ、サーバーに行が残り
    /// ローカルには無い状態が続く。行が無く削除の記録を含み、サーバーにあった版が分かるなら、その版で削除する。
    /// </remarks>
    [Fact(DisplayName = "[Sync/再生] 削除のあと再挿入が外部キーで失敗した行はサーバーからも消える")]
    public async Task DeleteThenRefusedReinsert_DeletesOnServer()
    {
        await SeedServerAsync(1, "alice", 11, "widget");
        await Engine.SyncAsync(cancellationToken: Ct);

        (await LocalLines.DeleteAsync(11, Ct)).Should().BeTrue();

        var reinsert = async () =>
            await LocalLines.InsertAsync(
                new SyncOrderLineEntity
                {
                    LineId = 11,
                    OrderId = 999,
                    Product = "orphan",
                },
                Ct
            );
        await reinsert.Should().ThrowAsync<Exception>("注文 999 は無い＝外部キーが拒否する");

        var result = await Engine.SyncAsync(cancellationToken: Ct);

        result.Uploaded.Should().Be(1, "行が無く削除の記録を含む＝その版で削除する");
        (await ServerLines.GetByIdAsync(11, Ct)).Should().BeNull();
        (await LocalLinesRaw.GetByIdAsync(11, Ct)).Should().BeNull();
        (await Journal.CountPendingAsync(Ct)).Should().Be(0);
    }

    /// <summary>オフラインで作って消した行は、サーバーへ一度も届いていないので何も送らずに捨てる。</summary>
    [Fact(DisplayName = "[Sync/再生] オフラインで作って消した行は送らずに捨てる")]
    public async Task OfflineInsertThenDelete_IsDiscarded()
    {
        await LocalOrders.InsertAsync(
            new SyncOrderEntity { OrderId = 50, CustomerName = "short-lived" },
            Ct
        );
        (await LocalOrders.DeleteAsync(50, Ct)).Should().BeTrue();

        var result = await Engine.SyncAsync(cancellationToken: Ct);

        result.Uploaded.Should().Be(0);
        result.Discarded.Should().Be(1);
        result.Conflicts.Should().BeEmpty();
        (await ServerOrders.GetByIdAsync(50, Ct)).Should().BeNull();
        (await Journal.CountPendingAsync(Ct)).Should().Be(0);
    }

    /// <summary>
    /// 版なしテーブルでも、削除が着地したあとの再挿入が失敗した行は、後勝ちの実行でサーバーから消える。
    /// </summary>
    [Fact(
        DisplayName = "[Sync/再生] 版なしテーブルで削除後の再挿入が失敗した行は後勝ちの実行で消える"
    )]
    public async Task VersionlessDeleteThenRefusedReinsert_DeletesOnServer()
    {
        await SeedServerAsync(1, "alice", 11, "widget");
        await SeedServerNoteAsync(5, 1, "note");
        var lww = new SyncOptions { Mode = SyncMode.LastWriteWins };
        await Engine.SyncAsync(lww, Ct);

        (await LocalNotes.DeleteAsync(5, Ct)).Should().BeTrue();

        var reinsert = async () =>
            await LocalNotes.InsertAsync(
                new SyncNoteEntity
                {
                    NoteId = 5,
                    OrderId = 999,
                    Body = "orphan",
                },
                Ct
            );
        await reinsert.Should().ThrowAsync<Exception>();

        await Engine.SyncAsync(lww, Ct);

        (await ServerNotes.GetByIdAsync(5, Ct))
            .Should()
            .BeNull("版なし＝行が無く削除を含むなら無条件に削除");
        (await Journal.CountPendingAsync(Ct)).Should().Be(0);
    }

    /// <summary>
    /// 記録を書いたが業務の挿入がまだ着地していない書き込みがあっても、その記録はアップロードの判定で捨てられない。
    /// </summary>
    /// <remarks>
    /// アップロードはジャーナルをロックの外で読むため、途中の書き込みの記録を読み得る。そのまま行の有無を見ると
    /// 「行が無い（挿入はまだ）」と判定して記録を捨て、直後に着地した行は二度と送られない。判定と破棄はロックの
    /// 内側で行うので、途中の書き込みが終わるのを待ってから判定する。割り込みは、ジャーナルを記録する Repository の
    /// Save フックで業務の書き込みを止め（記録は済み・ロックは保持）、その間に同期を走らせることで作る。
    /// </remarks>
    [Fact(
        DisplayName = "[Sync/並行] 記録と業務の挿入の間にある書き込みの記録はアップロードの判定で捨てられない"
    )]
    public async Task InFlightInsert_IsNotDiscardedByTheUpload()
    {
        var hook = new PausingSaveHook();
        var orders = CreateHookedOrders(hook);
        var order = new SyncOrderEntity { OrderId = 60, CustomerName = "in-flight" };
        order.MarkAdded();

        await RunSyncWhileSaveIsPausedAsync(
            hook,
            () => orders.SaveAsync(order, cancellationToken: Ct)
        );
        await Engine.SyncAsync(cancellationToken: Ct);

        var server = await ServerOrders.GetByIdAsync(60, Ct);
        server.Should().NotBeNull("記録は捨てられず、着地した行が送られる");
        server!.CustomerName.Should().Be("in-flight");
        (await Journal.CountPendingAsync(Ct)).Should().Be(0);
    }

    /// <summary>
    /// 削除の記録を書いたが業務の削除がまだ着地していない書き込みがあっても、その記録はアップロードの判定で
    /// 捨てられない（行が残っているのを「削除は着地しなかった」と読み違えない）。
    /// </summary>
    [Fact(
        DisplayName = "[Sync/並行] 記録と業務の削除の間にある書き込みの記録はアップロードの判定で捨てられない"
    )]
    public async Task InFlightDelete_IsNotDiscardedByTheUpload()
    {
        await ServerOrders.InsertAsync(
            new SyncOrderEntity { OrderId = 70, CustomerName = "to-delete" },
            Ct
        );
        await Engine.SyncAsync(cancellationToken: Ct);

        var hook = new PausingSaveHook();
        var orders = CreateHookedOrders(hook);
        var order = await LocalOrdersRaw.GetByIdAsync(70, Ct);
        order!.MarkRemoved();

        await RunSyncWhileSaveIsPausedAsync(
            hook,
            () => orders.SaveAsync(order, cancellationToken: Ct)
        );
        await Engine.SyncAsync(cancellationToken: Ct);

        (await ServerOrders.GetByIdAsync(70, Ct))
            .Should()
            .BeNull("記録は捨てられず、削除が送られる");
        (await Journal.CountPendingAsync(Ct)).Should().Be(0);
    }

    /// <summary>Save フックを持つローカルの注文 Repository をジャーナル記録で包む（ジャーナル＝ロックは本体と共有）。</summary>
    private JournalingSyncOrderRepository CreateHookedOrders(ISaveHook<SyncOrderEntity> hook) =>
        new(
            new SyncOrderRepository(
                new LightJournalSyncConnectionFactory(LocalConnectionString),
                new SaveHookRegistry().Add(hook)
            ),
            Journal
        );

    /// <summary>
    /// 保存を Save フックで止めている間（記録は済み・業務の書き込みはまだ）に同期を始め、少し待ってから保存を進める。
    /// </summary>
    private async Task RunSyncWhileSaveIsPausedAsync(PausingSaveHook hook, Func<Task<int>> save)
    {
        Task<int> saving;
        Task<SyncResult> syncing;

        using (ExecutionContext.SuppressFlow())
        {
            saving = Task.Run(save, Ct);
        }

        await hook.Entered.Task;

        using (ExecutionContext.SuppressFlow())
        {
            syncing = Task.Run(() => Engine.SyncAsync(cancellationToken: Ct), Ct);
        }

        // 同期がジャーナルを読み、判定へ進む時間を与える（ロックがあれば判定は保存の後まで待つ）
        await Task.Delay(500, Ct);
        hook.Release();
        await saving;
        await syncing;
    }

    /// <summary>エンジンが注文の行を書いたら印を立てる Save フック（エンジンの書き込みでもフックは走る）。</summary>
    private sealed class BatchWrittenHook : ISaveHook<SyncOrderEntity>
    {
        private volatile bool _written;

        /// <summary>エンジンが行を書いたか</summary>
        public bool Written => _written;

        public Task<bool> BeforeSaveAsync(
            SyncOrderEntity entity,
            SaveOperation operation,
            CancellationToken cancellationToken = default
        )
        {
            if (SyncSession.IsSuppressed)
            {
                _written = true;
            }

            return Task.FromResult(true);
        }
    }

    /// <summary>最初の保存の業務書き込みを、解放されるまで止める Save フック。</summary>
    private sealed class PausingSaveHook : ISaveHook<SyncOrderEntity>
    {
        private readonly TaskCompletionSource _release = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );

        /// <summary>保存がフックへ入った（記録は済み・業務の書き込みはまだ）</summary>
        public TaskCompletionSource Entered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>止めている保存を進める</summary>
        public void Release() => _release.TrySetResult();

        public async Task<bool> BeforeSaveAsync(
            SyncOrderEntity entity,
            SaveOperation operation,
            CancellationToken cancellationToken = default
        )
        {
            Entered.TrySetResult();
            await _release.Task;

            return true;
        }
    }

    /// <summary>ダウンロードのバッチサイズの誤りは、アップロードが何かを変える前に拒否される。</summary>
    [Fact(DisplayName = "[Sync] DownloadBatchSize が 0 以下なら何も変えずに拒否する")]
    public async Task NonPositiveDownloadBatchSize_ThrowsBeforeTheUploadChangesAnything()
    {
        await SeedServerAsync(1, "alice", 11, "widget");
        await Engine.SyncAsync(cancellationToken: Ct);

        var local = await LocalOrders.GetByIdAsync(1, Ct);
        local!.CustomerName = "alice-local";
        await LocalOrders.UpdateAsync(local, cancellationToken: Ct);

        var act = async () =>
            await Engine.SyncAsync(
                new SyncOptions
                {
                    ConflictPolicy = SyncConflictPolicy.ServerWins,
                    DownloadBatchSize = 0,
                },
                Ct
            );

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
        (await LocalOrdersRaw.GetByIdAsync(1, Ct))!
            .CustomerName.Should()
            .Be("alice-local", "ServerWins の適用より前に止まる");
        (await Journal.CountPendingAsync(Ct)).Should().Be(1);
    }

    // ---- 外部キーで止まらない（祖先を残す削除伝搬・ServerWins の適用順・サーバーの拒否の報告）----

    /// <summary>
    /// ServerWins は、サーバーで新しい親へ付け替えられた行も 1 回の同期で戻す（親が先に降りてから適用する）。
    /// </summary>
    /// <remarks>
    /// サーバーの行を適用するのがダウンロードより前だと、まだ降りていない親（サーバーで新しく作られた注文 2）を
    /// 参照する明細をローカルへ書くことになり、ローカルの外部キーで毎回止まる。適用はそのテーブルのダウンロードの
    /// 直後（親→子）に行うので、親は先に降りている。
    /// </remarks>
    [Fact(
        DisplayName = "[Sync/外部キー] ServerWins はサーバーで新しい親へ付け替えた行も 1 回で戻す"
    )]
    public async Task ServerWins_RestoresRowMovedToANewParentOnServer()
    {
        await SeedServerAsync(1, "alice", 11, "widget");
        await Engine.SyncAsync(cancellationToken: Ct);

        var local = await LocalLines.GetByIdAsync(11, Ct);
        local!.Product = "widget-local";
        await LocalLines.UpdateAsync(local, cancellationToken: Ct);

        await ServerOrders.InsertAsync(
            new SyncOrderEntity { OrderId = 2, CustomerName = "bob" },
            Ct
        );
        var server = await ServerLines.GetByIdAsync(11, Ct);
        server!.OrderId = 2;
        await ServerLines.UpdateAsync(server, cancellationToken: Ct);

        var result = await Engine.SyncAsync(
            new SyncOptions { ConflictPolicy = SyncConflictPolicy.ServerWins },
            Ct
        );

        result.Conflicts.Should().BeEmpty();
        var restored = await LocalLinesRaw.GetByIdAsync(11, Ct);
        restored!.OrderId.Should().Be(2, "サーバーの付け替えが戻る");
        restored.Product.Should().Be("widget", "ローカルの編集は捨てられる");
        (await Journal.CountPendingAsync(Ct)).Should().Be(0);
    }

    /// <summary>
    /// 未送信の行を削除伝搬が残すとき、その親も残す（サーバーで親子とも消えても同期は止まらない）。
    /// </summary>
    /// <remarks>
    /// 削除伝搬は未送信の行（ここでは編集した明細）を残すが、親（注文）は消そうとしていた。親を消すとローカルの
    /// 外部キーで毎回止まり（CASCADE なら残した明細ごと消える）、集めた競合も返らない。残す行が参照する親も残す。
    /// </remarks>
    [Fact(DisplayName = "[Sync/外部キー] 未送信の行を残すときは親も残し、同期は止まらない")]
    public async Task DeletePropagation_KeepsTheParentsOfTheRowsItKeeps()
    {
        await ArrangeEditedLineWhoseOrderWasDeletedOnServerAsync();

        for (var run = 0; run < 2; run++)
        {
            var result = await Engine.SyncAsync(cancellationToken: Ct);

            result
                .Conflicts.Should()
                .ContainSingle()
                .Which.Reason.Should()
                .Be(SyncConflictReason.MissingOnServer);
            result.DeletedLocally.Should().Be(0);
        }

        (await LocalLinesRaw.GetByIdAsync(11, Ct)).Should().NotBeNull("未送信の明細は残る");
        (await LocalOrdersRaw.GetByIdAsync(1, Ct)).Should().NotBeNull("残した明細の親も残る");
    }

    /// <summary>
    /// サーバーで親が消えた行を LocalWins・後勝ちで戻そうとして断られたら、ParentMissingOnServer として報告し、
    /// 同期は止まらない（親は復活させない）。
    /// </summary>
    [Theory(
        DisplayName = "[Sync/外部キー] 親がサーバーに無い行の書き込みは ParentMissingOnServer で報告し止まらない"
    )]
    [InlineData(SyncMode.Versioned, SyncConflictPolicy.LocalWins)]
    [InlineData(SyncMode.LastWriteWins, SyncConflictPolicy.Collect)]
    public async Task WriteOfRowWhoseParentIsGoneOnServer_IsReportedAsParentMissing(
        SyncMode mode,
        SyncConflictPolicy policy
    )
    {
        await ArrangeEditedLineWhoseOrderWasDeletedOnServerAsync();
        var options = new SyncOptions { Mode = mode, ConflictPolicy = policy };

        for (var run = 0; run < 2; run++)
        {
            var result = await Engine.SyncAsync(options, Ct);

            var conflict = result.Conflicts.Should().ContainSingle().Which;
            conflict.Reason.Should().Be(SyncConflictReason.ParentMissingOnServer);
            conflict.TableName.Should().Be("sync_order_lines");
            conflict.Operation.Should().Be(SyncJournalOperation.Upsert);
        }

        (await ServerOrders.GetByIdAsync(1, Ct))
            .Should()
            .BeNull("サーバーが消した親は復活させない");
        (await Journal.CountPendingAsync(Ct)).Should().Be(1, "記録は残る");
    }

    /// <summary>
    /// 後勝ちのランでも ServerWins なら、親がサーバーに無い行は競合として返さず、記録を捨ててサーバーの状態で決着する。
    /// </summary>
    /// <remarks>
    /// サーバーにはその行も無いので戻すものは無く、ローカルの行と親は削除伝搬が片づける（親は復活させない）。
    /// </remarks>
    [Fact(
        DisplayName = "[Sync/外部キー] 後勝ち＋ServerWins は親がサーバーに無い行の記録を捨ててサーバーの状態で決着させる"
    )]
    public async Task LastWriteWinsServerWins_SettlesParentMissingOnServer()
    {
        await ArrangeEditedLineWhoseOrderWasDeletedOnServerAsync();

        var result = await Engine.SyncAsync(
            new SyncOptions
            {
                Mode = SyncMode.LastWriteWins,
                ConflictPolicy = SyncConflictPolicy.ServerWins,
            },
            Ct
        );

        result.Conflicts.Should().BeEmpty();
        result.Discarded.Should().Be(1);
        (await Journal.CountPendingAsync(Ct)).Should().Be(0);
        (await ServerOrders.GetByIdAsync(1, Ct))
            .Should()
            .BeNull("サーバーが消した親は復活させない");
        (await LocalLinesRaw.GetByIdAsync(11, Ct))
            .Should()
            .BeNull("サーバーに無い行は削除伝搬が片づける");
        (await LocalOrdersRaw.GetByIdAsync(1, Ct)).Should().BeNull();
    }

    /// <summary>
    /// 後勝ちのランでも ServerWins なら、サーバーに断られた削除は競合として返さず、記録を捨てて行を戻す。
    /// </summary>
    [Fact(
        DisplayName = "[Sync/外部キー] 後勝ち＋ServerWins はサーバーに断られた削除の記録を捨てて親を戻し子も降りる"
    )]
    public async Task LastWriteWinsServerWins_SettlesDeleteRefusedOnServer()
    {
        await ArrangeLocallyDeletedOrderWithChildAddedOnServerAsync();

        var result = await Engine.SyncAsync(
            new SyncOptions
            {
                Mode = SyncMode.LastWriteWins,
                ConflictPolicy = SyncConflictPolicy.ServerWins,
            },
            Ct
        );

        result.Conflicts.Should().BeEmpty();
        result.HasTruncations.Should().BeFalse();
        result.Discarded.Should().Be(1);
        (await LocalOrdersRaw.GetByIdAsync(1, Ct)).Should().NotBeNull("サーバーの行が戻る");
        (await LocalLinesRaw.GetByIdAsync(21, Ct)).Should().NotBeNull("戻った親の下へ子が降りる");
        (await Journal.CountPendingAsync(Ct)).Should().Be(0);
    }

    /// <summary>
    /// 後勝ちのランの ServerWins が決着させるのは、外部キーに由来する 2 つの競合だけ。ほかの書き込みは今までどおり
    /// サーバーへ上がる（ポリシーを見ない）。
    /// </summary>
    [Fact(
        DisplayName = "[Sync/外部キー] 後勝ち＋ServerWins でも外部キーに由来しない変更はそのまま上がる"
    )]
    public async Task LastWriteWinsServerWins_UploadsOrdinaryChanges()
    {
        await SeedServerAsync(1, "alice", 11, "widget");
        await Engine.SyncAsync(cancellationToken: Ct);
        await EditLocalOrderAsync(1, "local-edit");

        var server = await ServerOrders.GetByIdAsync(1, Ct);
        server!.CustomerName = "server-edit";
        await ServerOrders.UpdateAsync(server, cancellationToken: Ct);

        var result = await Engine.SyncAsync(
            new SyncOptions
            {
                Mode = SyncMode.LastWriteWins,
                ConflictPolicy = SyncConflictPolicy.ServerWins,
            },
            Ct
        );

        result.Conflicts.Should().BeEmpty();
        result.Uploaded.Should().Be(1);
        (await ServerOrders.GetByIdAsync(1, Ct))!
            .CustomerName.Should()
            .Be("local-edit", "後勝ちは版を見ない（ServerWins でも外部キーの競合でなければ同じ）");
    }

    /// <summary>
    /// 版なしの表で保留した行（未送信の変更）は飛ばしてほかの行を適用し、走査の後で、最初に保留した行で打ち切りとして
    /// 報告する。
    /// </summary>
    [Fact(
        DisplayName = "[Sync/外部キー] 版なしの表で保留した行は飛ばして続け、走査の後で打ち切りとして報告する"
    )]
    public async Task VersionlessDownload_ReportsTheRowItHeldBack()
    {
        await ServerOrders.InsertAsync(
            new SyncOrderEntity { OrderId = 1, CustomerName = "alice" },
            Ct
        );
        await ServerOrders.InsertAsync(
            new SyncOrderEntity { OrderId = 2, CustomerName = "bob" },
            Ct
        );
        await SeedServerNoteAsync(101, 1, "a");
        await SeedServerNoteAsync(102, 1, "b");
        var lastWriteWins = new SyncOptions { Mode = SyncMode.LastWriteWins };
        await Engine.SyncAsync(lastWriteWins, Ct);

        // ローカルではメモ 101 を注文 2 へ付け替え、サーバーでは注文 2 を消す（付け替えは ParentMissingOnServer で残る）
        var note = await LocalNotes.GetByIdAsync(101, Ct);
        note!.OrderId = 2;
        await LocalNotes.UpdateAsync(note, cancellationToken: Ct);
        (await ServerOrders.DeleteAsync(2, Ct)).Should().BeTrue();
        var server = await ServerNotes.GetByIdAsync(102, Ct);
        server!.Body = "b2";
        await ServerNotes.UpdateAsync(server, cancellationToken: Ct);

        var result = await Engine.SyncAsync(lastWriteWins, Ct);

        result
            .Conflicts.Should()
            .ContainSingle()
            .Which.Reason.Should()
            .Be(SyncConflictReason.ParentMissingOnServer);
        var truncation = result
            .Truncations.Should()
            .ContainSingle(truncation => truncation.TableName == "sync_notes")
            .Which;
        truncation.PendingKeyText.Should().Be("101");
        truncation.MissingParentTableName.Should().BeNull();
        (await LocalNotesRaw.GetByIdAsync(102, Ct))!
            .Body.Should()
            .Be("b2", "保留した行のほかは適用する");
    }

    /// <summary>
    /// 親の表をランから外すと、ローカルに無い親を参照する子でダウンロードを打ち切り、親の表を名指しする（範囲外の表には
    /// 書かない）。外さないランで親も子も降りる。
    /// </summary>
    [Fact(
        DisplayName = "[Sync/外部キー] 親の表をランから外すと、ローカルに無い親を参照する子で打ち切り親の表を名指しする"
    )]
    public async Task Download_ChildOfExcludedParentTable_StopsAndNamesTheParentTable()
    {
        await SeedServerAsync(1, "alice", 11, "widget");

        var result = await Engine.SyncAsync(
            new SyncOptions { ExcludedEntityTypes = [typeof(SyncOrderEntity)] },
            Ct
        );

        var truncation = result
            .Truncations.Should()
            .ContainSingle(truncation => truncation.TableName == "sync_order_lines")
            .Which;
        truncation.PendingKeyText.Should().Be("11");
        truncation.TruncatedByTableName.Should().BeNull();
        truncation.MissingParentTableName.Should().Be("sync_orders");
        (await LocalOrdersRaw.GetByIdAsync(1, Ct)).Should().BeNull("範囲外の表には書かない");
        (await LocalLinesRaw.GetByIdAsync(11, Ct)).Should().BeNull();

        var next = await Engine.SyncAsync(cancellationToken: Ct);

        next.HasTruncations.Should().BeFalse();
        (await LocalLinesRaw.GetByIdAsync(11, Ct)).Should().NotBeNull();
    }

    /// <summary>
    /// ローカルで削除した親を、サーバーで別端末の子が参照していれば、削除は DeleteRefusedOnServer として報告し、
    /// その子のダウンロードは打ち切る（Collect・LocalWins とも止まらない）。
    /// </summary>
    [Theory(
        DisplayName = "[Sync/外部キー] サーバーで子が足された親の削除は DeleteRefusedOnServer で報告し子で打ち切る"
    )]
    [InlineData(SyncConflictPolicy.Collect)]
    [InlineData(SyncConflictPolicy.LocalWins)]
    public async Task DeleteOfParentWithChildAddedOnServer_IsReportedAndTheChildIsHeldBack(
        SyncConflictPolicy policy
    )
    {
        await ArrangeLocallyDeletedOrderWithChildAddedOnServerAsync();

        var result = await Engine.SyncAsync(new SyncOptions { ConflictPolicy = policy }, Ct);

        var conflict = result.Conflicts.Should().ContainSingle().Which;
        conflict.Reason.Should().Be(SyncConflictReason.DeleteRefusedOnServer);
        conflict.TableName.Should().Be("sync_orders");
        conflict.Operation.Should().Be(SyncJournalOperation.Delete);
        conflict.ServerEntity.Should().NotBeNull();
        result
            .Truncations.Should()
            .Contain(truncation =>
                truncation.TableName == "sync_order_lines"
                && truncation.PendingKeyText == "21"
                && truncation.MissingParentTableName == "sync_orders"
            );
        (await LocalLinesRaw.GetByIdAsync(21, Ct))
            .Should()
            .BeNull("親が無いローカルへは降ろさない");
        (await ServerOrders.GetByIdAsync(1, Ct)).Should().NotBeNull();
        (await Journal.CountPendingAsync(Ct)).Should().Be(1, "記録は残る");
    }

    /// <summary>
    /// 同じ配置を ServerWins で回すと、1 回で親が戻り、サーバーで足された子も降りる。
    /// </summary>
    [Fact(
        DisplayName = "[Sync/外部キー] サーバーで子が足された親の削除は ServerWins で 1 回で親が戻り子も降りる"
    )]
    public async Task DeleteOfParentWithChildAddedOnServer_IsSettledByServerWins()
    {
        await ArrangeLocallyDeletedOrderWithChildAddedOnServerAsync();

        var result = await Engine.SyncAsync(
            new SyncOptions { ConflictPolicy = SyncConflictPolicy.ServerWins },
            Ct
        );

        result.Conflicts.Should().BeEmpty();
        result.HasTruncations.Should().BeFalse();
        (await LocalOrdersRaw.GetByIdAsync(1, Ct)).Should().NotBeNull("ServerWins で親が戻る");
        (await LocalLinesRaw.GetByIdAsync(21, Ct)).Should().NotBeNull("戻った親の下へ子が降りる");
        (await Journal.CountPendingAsync(Ct)).Should().Be(0);
    }

    /// <summary>
    /// ServerWins のランの最中に、捨てる予定の行をローカルで編集すると、その編集は残る（新しい記録も残る）。
    /// </summary>
    /// <remarks>
    /// ServerWins が捨てるのはランの冒頭に読んだ記録だけ。適用の直前に記録を読み直し、新しい記録が付いたキーには
    /// サーバーの行を書かない（新しい編集は捨てるよう頼まれたものではない）。古い記録は捨てる。割り込みは注文
    /// テーブルの最初の差分取得（＝そのテーブルの適用より前）でローカルを編集して作る。
    /// </remarks>
    [Fact(
        DisplayName = "[Sync/外部キー] ServerWins の最中に編集した行は適用されず編集と新しい記録が残る"
    )]
    public async Task ServerWins_LeavesARowEditedDuringTheRun()
    {
        await SeedServerAsync(1, "alice", 11, "widget");
        await Engine.SyncAsync(cancellationToken: Ct);
        await EditLocalOrderAsync(1, "alice-before");
        var oldIds = (await Journal.ReadAllAsync(Ct)).Select(entry => entry.Id).ToList();

        var source = new LocalWriteOnFirstChangesOrderSource(
            _sources.Orders,
            async cancellationToken =>
            {
                var local = await LocalOrders.GetByIdAsync(1, cancellationToken);
                local!.CustomerName = "alice-during";
                await LocalOrders.UpdateAsync(local, cancellationToken: cancellationToken);
            }
        );

        await CreateEngineWithOrderSource(source)
            .SyncAsync(new SyncOptions { ConflictPolicy = SyncConflictPolicy.ServerWins }, Ct);

        (await LocalOrdersRaw.GetByIdAsync(1, Ct))!
            .CustomerName.Should()
            .Be("alice-during", "ランの最中の編集はサーバーの行で上書きされない");
        var remaining = await Journal.ReadAllAsync(Ct);
        remaining.Should().ContainSingle("新しい記録は残り、古い記録は捨てられる");
        oldIds.Should().NotContain(remaining[0].Id);
    }

    /// <summary>
    /// ServerWins で捨てる予定の行がサーバー側でも変わっていても、そのテーブルのダウンロードはその行で止まらず、
    /// 1 回のランで全テーブルが降りて全記録が決着する。
    /// </summary>
    /// <remarks>
    /// 捨てる予定の記録をダウンロード中も未送信に数えると、サーバーでも変わった行でそのテーブルが打ち切られ、
    /// その後ろの変更と後ろのテーブル（とその ServerWins の記録）がその回に取り残される。最終的な内容だけを見ると
    /// 次の回で追いついて区別できないため、1 回のランで打ち切りが無く全部が済むことを表明する。
    /// </remarks>
    [Fact(
        DisplayName = "[Sync/外部キー] ServerWins で捨てる行がサーバーでも変わっていてもダウンロードは止まらず 1 回で済む"
    )]
    public async Task ServerWins_DoesNotStopTheDownloadAtRowsItDrops()
    {
        await SeedServerAsync(1, "alice", 11, "widget");
        await SeedServerAsync(2, "bob", 12, "gadget");
        await Engine.SyncAsync(cancellationToken: Ct);

        // 捨てる予定の記録（注文 1 と明細 12）
        await EditLocalOrderAsync(1, "alice-local");
        var localLine = await LocalLines.GetByIdAsync(12, Ct);
        localLine!.Product = "gadget-local";
        await LocalLines.UpdateAsync(localLine, cancellationToken: Ct);

        // サーバー側の変更: 注文 1（捨てる行そのもの）→ それより版が上の注文 2 → 後ろのテーブルの明細 11
        var serverOrder = await ServerOrders.GetByIdAsync(1, Ct);
        serverOrder!.CustomerName = "alice-server";
        await ServerOrders.UpdateAsync(serverOrder, cancellationToken: Ct);
        var laterOrder = await ServerOrders.GetByIdAsync(2, Ct);
        laterOrder!.CustomerName = "bob-server";
        await ServerOrders.UpdateAsync(laterOrder, cancellationToken: Ct);
        var serverLine = await ServerLines.GetByIdAsync(11, Ct);
        serverLine!.Product = "widget-server";
        await ServerLines.UpdateAsync(serverLine, cancellationToken: Ct);

        var result = await Engine.SyncAsync(
            new SyncOptions { ConflictPolicy = SyncConflictPolicy.ServerWins },
            Ct
        );

        result.Truncations.Should().BeEmpty("捨てる予定の行はダウンロードを止めない");
        (await LocalOrdersRaw.GetByIdAsync(2, Ct))!
            .CustomerName.Should()
            .Be("bob-server", "その行より版が上の変更も同じランで降りる");
        (await LocalLinesRaw.GetByIdAsync(11, Ct))!
            .Product.Should()
            .Be("widget-server", "後ろのテーブルの変更も同じランで降りる");
        (await Journal.CountPendingAsync(Ct)).Should().Be(0, "後ろのテーブルの記録まで決着する");
        (await LocalOrdersRaw.GetByIdAsync(1, Ct))!.CustomerName.Should().Be("alice-server");
        (await LocalLinesRaw.GetByIdAsync(12, Ct))!.Product.Should().Be("gadget");
    }

    /// <summary>明細 11 をローカルで編集し、サーバーでは明細 11 と注文 1 を消した配置を作る。</summary>
    private async Task ArrangeEditedLineWhoseOrderWasDeletedOnServerAsync()
    {
        await SeedServerAsync(1, "alice", 11, "widget");
        await Engine.SyncAsync(cancellationToken: Ct);

        var local = await LocalLines.GetByIdAsync(11, Ct);
        local!.Product = "widget-local";
        await LocalLines.UpdateAsync(local, cancellationToken: Ct);

        (await ServerLines.DeleteAsync(11, Ct)).Should().BeTrue();
        (await ServerOrders.DeleteAsync(1, Ct)).Should().BeTrue();
    }

    /// <summary>ローカルで注文 1 を削除し、サーバーでは別端末が注文 1 に明細 21 を足した配置を作る。</summary>
    /// <remarks>
    /// 注文 2 は版の高い行としてローカルに残し、注文テーブルの再開点を注文 1 より上に保つ（注文 1 しか無いと、
    /// 削除で再開点が消えて注文 1 が降り直し、注文テーブル自身がそこで打ち切られて明細まで届かない）。
    /// </remarks>
    private async Task ArrangeLocallyDeletedOrderWithChildAddedOnServerAsync()
    {
        await ServerOrders.InsertAsync(
            new SyncOrderEntity { OrderId = 1, CustomerName = "alice" },
            Ct
        );
        await ServerOrders.InsertAsync(
            new SyncOrderEntity { OrderId = 2, CustomerName = "bob" },
            Ct
        );
        await Engine.SyncAsync(cancellationToken: Ct);

        (await LocalOrders.DeleteAsync(1, Ct)).Should().BeTrue();

        await ServerLines.InsertAsync(
            new SyncOrderLineEntity
            {
                LineId = 21,
                OrderId = 1,
                Product = "added elsewhere",
            },
            Ct
        );
    }

    // ---- 同期中のローカル書き込み（ロックとジャーナルの読み直し）----

    /// <summary>
    /// ダウンロードの最中にローカルで作った行は、同じランの削除伝搬で消えない。
    /// </summary>
    /// <remarks>
    /// 削除伝搬は「サーバーに無いキー」を消すが、未送信のジャーナルがあるキーは残す。そのジャーナルをランの冒頭で
    /// 1 回だけ読むと、ダウンロード中に作った行は「未送信でもなくサーバーにも無い行」に見えて消され、次のランでは
    /// ジャーナルの記録も「行が無い」として捨てられる＝両側から消える。割り込みは差分ソースを包み、最初の差分取得
    /// （アップロードの後・削除伝搬の前）でローカルへ書くことで作る。
    /// </remarks>
    [Fact(DisplayName = "[Sync/並行] ダウンロード中にローカルで作った行は削除伝搬で消えない")]
    public async Task LocalInsertDuringDownload_IsNotDeletedByPropagation()
    {
        await SeedServerAsync(1, "alice", 11, "widget");
        await Engine.SyncAsync(cancellationToken: Ct);

        var source = new LocalWriteOnFirstChangesOrderSource(
            _sources.Orders,
            cancellationToken =>
                LocalOrders.InsertAsync(
                    new SyncOrderEntity { OrderId = 99, CustomerName = "made-during-sync" },
                    cancellationToken
                )
        );

        var result = await CreateEngineWithOrderSource(source).SyncAsync(cancellationToken: Ct);

        result.DeletedLocally.Should().Be(0, "ダウンロード中に作った行は未送信のジャーナルを持つ");
        (await LocalOrdersRaw.GetByIdAsync(99, Ct)).Should().NotBeNull();

        var next = await Engine.SyncAsync(cancellationToken: Ct);

        next.Uploaded.Should().Be(1, "次のランで送られる");
        (await ServerOrders.GetByIdAsync(99, Ct))!.CustomerName.Should().Be("made-during-sync");
    }

    /// <summary>
    /// ダウンロードの最中にローカルで編集した行は、同じランのバッチ適用で上書きされない。
    /// </summary>
    /// <remarks>
    /// 未送信のキーをランの冒頭で読むと、その後に編集した行はバッチにサーバーの内容で上書きされ、ジャーナルには
    /// 記録だけが残る（次のランはサーバーの内容を送り直す＝編集が消える）。未送信のキーをバッチの直前に読み直せば、
    /// 編集した行で打ち切られて編集は残る。
    /// </remarks>
    [Fact(DisplayName = "[Sync/並行] ダウンロード中のローカル編集はバッチ適用に上書きされない")]
    public async Task LocalEditDuringDownload_IsNotOverwrittenByBatch()
    {
        await SeedServerAsync(1, "alice", 11, "widget");
        await Engine.SyncAsync(cancellationToken: Ct);

        var server = await ServerOrders.GetByIdAsync(1, Ct);
        server!.CustomerName = "alice-server";
        await ServerOrders.UpdateAsync(server, cancellationToken: Ct);

        var source = new LocalWriteOnFirstChangesOrderSource(
            _sources.Orders,
            async cancellationToken =>
            {
                var local = await LocalOrders.GetByIdAsync(1, cancellationToken);
                local!.CustomerName = "alice-local";
                await LocalOrders.UpdateAsync(local, cancellationToken: cancellationToken);
            }
        );

        var result = await CreateEngineWithOrderSource(source).SyncAsync(cancellationToken: Ct);

        (await LocalOrdersRaw.GetByIdAsync(1, Ct))!
            .CustomerName.Should()
            .Be("alice-local", "編集した行はバッチの直前に未送信として読まれ、上書きされない");
        result.Truncations.Should().ContainSingle(truncation => truncation.PendingKeyText == "1");
    }

    /// <summary>
    /// エンジンがバッチを適用している間、別の流れからのローカル書き込みはロックで待たされ、適用の後に着地する。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 未送信のキーを読んでからバッチを書き終えるまでの間に別の画面が同じ行を書くと、その書き込みはキーの読み取りに
    /// 間に合わず、バッチに上書きされる。デコレータとエンジンが同じロックを取ることで、その書き込みはバッチの後へ回る。
    /// </para>
    /// <para>
    /// 割り込みは、注文テーブルのローカル生 SQL 実行器を包み、バッチ適用の最初の問い合わせ（既存キーの照会）の
    /// 直前に、実行コンテキストを引き継がない別タスクで編集を始めることで作る。順序は時間でなく、エンジンが
    /// バッチを書くときに走る Save フックの印で見る（編集が終わった時点で、バッチはもう書かれていなければならない）。
    /// 編集する行は同期の前に読んでおく＝編集が読む版がバッチの前のものに決まり、次のランの競合が時間に左右されない。
    /// </para>
    /// </remarks>
    [Fact(
        DisplayName = "[Sync/並行] バッチ適用中の別の流れのローカル書き込みはロックで待ち、適用後に着地する"
    )]
    public async Task LocalWriteFromAnotherFlow_WaitsForTheBatch()
    {
        await SeedServerAsync(1, "alice", 11, "widget");
        await Engine.SyncAsync(cancellationToken: Ct);

        var server = await ServerOrders.GetByIdAsync(1, Ct);
        server!.CustomerName = "alice-server";
        await ServerOrders.UpdateAsync(server, cancellationToken: Ct);

        // バッチの前の版で編集する（読みをバッチより前に決めておく）
        var editing = (await LocalOrders.GetByIdAsync(1, Ct))!;
        editing.CustomerName = "alice-local";

        Task? edit = null;
        var batchWritten = new BatchWrittenHook();
        var editLandedAfterBatch = false;
        var localSql = new InterceptingSqlExecutor(
            LocalSql,
            async (sql, cancellationToken) =>
            {
                if (edit is not null || sql != GeneratedSyncTables.SyncOrder.LocalExistingKeysSql)
                {
                    return;
                }

                using (ExecutionContext.SuppressFlow())
                {
                    edit = Task.Run(
                        async () =>
                        {
                            await LocalOrders.UpdateAsync(editing, cancellationToken: Ct);
                            editLandedAfterBatch = batchWritten.Written;
                        },
                        Ct
                    );
                }

                // ロックが無いときに、編集がバッチより先に着地する機会を与えるだけ（ロックがあれば編集は待ち続ける＝
                // 判定は時間でなく、着地の時点でバッチの印が立っているかで行う）
                await Task.WhenAny(edit, Task.Delay(200, cancellationToken));
            }
        );
        var engine = CreateEngineWithOrderTable(
            new SyncTable<SyncOrderEntity, int>(
                CreateHookedOrders(batchWritten),
                localSql,
                _sources.Orders,
                GeneratedSyncTables.SyncOrder
            )
        );

        await engine.SyncAsync(cancellationToken: Ct);
        await edit!;

        batchWritten.Written.Should().BeTrue("バッチは書かれる");
        editLandedAfterBatch
            .Should()
            .BeTrue("ロックを持つバッチの間、ローカル書き込みは待ち、バッチの後に着地する");
        (await LocalOrdersRaw.GetByIdAsync(1, Ct))!
            .CustomerName.Should()
            .Be("alice-local", "待った書き込みはバッチの後に着地する");
        (await Journal.CountPendingAsync(Ct)).Should().Be(1);

        // 編集はバッチより前に読んだ行（旧い版）へ書かれたもの＝サーバーの変更を見ずに上書きした編集なので、次の
        // ランは黙って送らず更新競合として報告する（編集は消えず、判断は呼び出し側へ渡る）
        var next = await Engine.SyncAsync(cancellationToken: Ct);

        next.Conflicts.Should()
            .ContainSingle()
            .Which.Reason.Should()
            .Be(SyncConflictReason.ModifiedOnServer);
        (await LocalOrdersRaw.GetByIdAsync(1, Ct))!.CustomerName.Should().Be("alice-local");
    }

    /// <summary>
    /// ジャーナルに記録する書き込みは、同じローカル DB を別の接続が読んでいる間に記録の確定が来ても、読み取りが
    /// 終わるのを待ってから成功する。
    /// </summary>
    /// <remarks>
    /// 記録は 1 文の <c>INSERT … RETURNING id</c> で、自動コミットの確定は最初の行を返した後の step で起きる。
    /// ロールバックジャーナルの確定には排他のロックが要るので、別の接続が読み取り（共有のロック）を持っている間は
    /// 確定できない。読み取りを持ったまま書き込みを始め、上限の時間まで終わらない（待っている）ことと、読み取りを
    /// 終えた後に成功することを見る。
    /// </remarks>
    [Fact(
        DisplayName = "[Sync/並行] 別の接続の読み取りと重なったジャーナル記録の書き込みは、読み取りの後に成功する"
    )]
    public async Task JournaledWrite_WaitsForReaderOnAnotherConnection()
    {
        await using var reader = new SqliteConnection(LocalConnectionString);
        await reader.OpenAsync(Ct);
        var readTransaction = reader.BeginTransaction(deferred: true);

        await using (var read = new SqliteCommand("SELECT count(*) FROM sync_orders", reader))
        {
            read.Transaction = readTransaction;
            await read.ExecuteScalarAsync(Ct);
        }

        // SQLite の待ちは呼び出したスレッドを止める（プロバイダの処理は同期）ので、書き込みは別のタスクで始める
        var insert = Task.Run(
            () =>
                LocalOrders.InsertAsync(
                    new SyncOrderEntity { OrderId = 5, CustomerName = "while-read" },
                    Ct
                ),
            Ct
        );
        var first = await Task.WhenAny(insert, Task.Delay(TimeSpan.FromSeconds(2), Ct));

        first.Should().NotBeSameAs(insert, "読み取りが終わるまで記録は確定できず、待っている");

        readTransaction.Commit();
        readTransaction.Dispose();
        await insert;

        (await LocalOrdersRaw.GetByIdAsync(5, Ct)).Should().NotBeNull();
        (await Journal.CountPendingAsync(Ct)).Should().Be(1);
    }

    /// <summary>
    /// 生 SQL の実行器へ渡した <c>INSERT … RETURNING</c> も、別の接続の読み取りが終わるのを待ってから成功する。
    /// </summary>
    [Fact(
        DisplayName = "[Sync/並行] 生 SQL の INSERT … RETURNING は、別の接続の読み取りの後に成功する"
    )]
    public async Task RawSqlReturning_WaitsForReaderOnAnotherConnection()
    {
        await using var reader = new SqliteConnection(LocalConnectionString);
        await reader.OpenAsync(Ct);
        var readTransaction = reader.BeginTransaction(deferred: true);

        await using (var read = new SqliteCommand("SELECT count(*) FROM sync_orders", reader))
        {
            read.Transaction = readTransaction;
            await read.ExecuteScalarAsync(Ct);
        }

        // SQLite の待ちは呼び出したスレッドを止める（プロバイダの処理は同期）ので、書き込みは別のタスクで始める
        var insert = Task.Run(
            () =>
                LocalSql.QueryProjectionBySqlAsync<long>(
                    "INSERT INTO sync_orders (order_id, customer_name) VALUES (7, 'raw') RETURNING order_id",
                    null,
                    Ct
                ),
            Ct
        );
        var first = await Task.WhenAny(insert, Task.Delay(TimeSpan.FromSeconds(2), Ct));

        first.Should().NotBeSameAs(insert, "読み取りが終わるまで書き込みは確定できず、待っている");

        readTransaction.Commit();
        readTransaction.Dispose();

        (await insert).Should().Equal(7L);
        (await LocalOrdersRaw.GetByIdAsync(7, Ct)).Should().NotBeNull();
    }

    /// <summary>同じローカル DB に対する 2 つのランは重ならない（後から来たほうが待つ）。</summary>
    [Fact(DisplayName = "[Sync/並行] 同じローカル DB への SyncAsync の重なりは後着が待つ")]
    public async Task OverlappingRuns_AreSerialised()
    {
        await SeedServerAsync(1, "alice", 11, "widget");

        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var source = new LocalWriteOnFirstChangesOrderSource(
            _sources.Orders,
            async _ =>
            {
                entered.SetResult();
                await release.Task;
            }
        );

        var first = CreateEngineWithOrderSource(source).SyncAsync(cancellationToken: Ct);
        await entered.Task;

        Task<SyncResult> second;

        using (ExecutionContext.SuppressFlow())
        {
            second = Task.Run(() => Engine.SyncAsync(cancellationToken: Ct), Ct);
        }

        var secondFinishedFirst = await Task.WhenAny(second, Task.Delay(500, Ct)) == second;
        release.SetResult();
        await first;
        await second;

        secondFinishedFirst.Should().BeFalse("先のランが終わるまで後のランは始まらない");
    }

    /// <summary>
    /// ローカルの業務の書き込みが例外で終わったら、その呼び出しが付けた記録を取り消す（#16）。
    /// </summary>
    /// <remarks>
    /// 残すと、着地しなかった書き込みが後のランで上がり、LocalWins・後勝ちの再生が古い内容でサーバーの他者の更新を
    /// 戻したり、中身の変わらない書き込みが版を進めて別の端末に偽の競合を出したりする。外部キーで拒否される更新で作る。
    /// </remarks>
    [Fact(DisplayName = "[Sync/記録] 失敗した更新の記録は残らない")]
    public async Task FailedUpdate_LeavesNoJournalEntry()
    {
        await SeedServerAsync(1, "alice", 11, "widget");
        await Engine.SyncAsync(cancellationToken: Ct);

        var line = (await LocalLines.GetByIdAsync(11, Ct))!;
        line.OrderId = 999;
        line.Product = "orphan";

        var update = () => LocalLines.UpdateAsync(line, cancellationToken: Ct);

        await update
            .Should()
            .ThrowAsync<Exception>("存在しない注文を参照する更新はローカルの外部キーで拒否される");
        (await Journal.ReadAllAsync(Ct)).Should().BeEmpty("着地しなかった書き込みの記録は取り消す");
    }

    /// <summary>
    /// 失敗した blob の書き込み（ストリームが宣言した長さに足りない）の記録も残らない（#16）。
    /// </summary>
    [Fact(DisplayName = "[Sync/記録] 失敗した blob の書き込みの記録は残らない")]
    public async Task FailedBlobWrite_LeavesNoJournalEntry()
    {
        await SeedServerAsync(1, "alice", 11, "widget");
        await Engine.SyncAsync(cancellationToken: Ct);

        using var source = new MemoryStream([1, 2, 3]);
        var write = () => LocalOrders.WriteAttachmentAsync(1, source, 10, Ct);

        await write
            .Should()
            .ThrowAsync<ArgumentException>("ストリームが宣言した長さより先に終わる");
        (await Journal.ReadAllAsync(Ct)).Should().BeEmpty("着地しなかった書き込みの記録は取り消す");
    }

    /// <summary>
    /// 取り消すのはその呼び出しが付けた記録だけで、同じ行の前からある記録は残る（#16）。
    /// </summary>
    [Fact(DisplayName = "[Sync/記録] 失敗した書き込みは同じ行の前からある記録を消さない")]
    public async Task FailedUpdate_KeepsEarlierEntriesOfTheSameRow()
    {
        await SeedServerAsync(1, "alice", 11, "widget");
        await Engine.SyncAsync(cancellationToken: Ct);

        var line = (await LocalLines.GetByIdAsync(11, Ct))!;
        line.Product = "edited";
        await LocalLines.UpdateAsync(line, cancellationToken: Ct);

        line.OrderId = 999;
        var update = () => LocalLines.UpdateAsync(line, cancellationToken: Ct);

        await update.Should().ThrowAsync<Exception>();

        var entry = (await Journal.ReadAllAsync(Ct))
            .Should()
            .ContainSingle("前の編集の記録は残す")
            .Subject;
        entry.TableName.Should().Be("sync_order_lines");
        entry.KeyText.Should().Be("11");

        var result = await Engine.SyncAsync(cancellationToken: Ct);

        result.Conflicts.Should().BeEmpty();
        (await ServerLines.GetByIdAsync(11, Ct))!
            .Product.Should()
            .Be("edited", "着地した編集はそのまま上がる");
    }

    /// <summary>
    /// 記録の範囲が集める (id, 記録) の組は、1 文の複数行の INSERT で id の昇順が VALUES の並びどおりであることに
    /// 頼っている（RETURNING の出力順は不定のため昇順に並べ替えて対応づける）。同梱の SQLite でその前提を固定する。
    /// </summary>
    /// <remarks>
    /// 崩れると、書き込みの後の突き合わせ（#28 のキーの書き換え・N10 の取り消し）が別の行の記録を書き換える。キーの
    /// テキストを VALUES の並びと無関係な順にし、文の境界（100 行）をまたぐ件数で入れる。
    /// </remarks>
    [Fact(DisplayName = "[Sync/記録] 1 文の複数行の記録は、id の昇順が記録の並びどおりに振られる")]
    public async Task RecordedIds_FollowTheOrderOfTheRecords()
    {
        var records = Enumerable
            .Range(0, 250)
            .Select(index => new SyncJournalRecord(
                "sync_orders",
                ((index * 37) % 250).ToString(System.Globalization.CultureInfo.InvariantCulture),
                SyncJournalOperation.Upsert,
                null
            ))
            .ToList();
        var scope = new SyncRecordingScope();

        using (scope.Begin())
        {
            await Journal.RecordManyAsync(records, Ct);
        }

        scope.Recorded.Select(pair => pair.Record).Should().Equal(records);

        var stored = (await Journal.ReadAllAsync(Ct)).ToDictionary(
            entry => entry.Id,
            entry => entry.KeyText
        );

        foreach (var (id, record) in scope.Recorded)
        {
            stored[id].Should().Be(record.KeyText, "集めた id はその記録の行を指す");
        }
    }

    /// <summary>
    /// グラフ保存の Before フックが、同じ仮のキー（0）を持つ新しい行 2 つにそれぞれ別のキーを決めても、両方の記録が
    /// 正しいキーへ移り、同期で上がる（#28）。
    /// </summary>
    [Fact(
        DisplayName = "[Sync/記録] フックが同じ仮のキーの新しい行 2 つに別々のキーを決めても、記録はそれぞれのキーへ移る"
    )]
    public async Task HookAssignedKeys_OfTwoNewRows_AreRecordedEach()
    {
        var hook = new LineKeyAssigningHook(101);
        var orders = new JournalingSyncOrderRepository(
            new SyncOrderRepository(
                new LightJournalSyncConnectionFactory(LocalConnectionString),
                new SaveHookRegistry().Add<SyncOrderLineEntity>(hook)
            ),
            Journal
        );
        var order = new SyncOrderEntity { OrderId = 9, CustomerName = "nine" };
        var first = new SyncOrderLineEntity
        {
            LineId = 0,
            OrderId = 9,
            Product = "a",
        };
        var second = new SyncOrderLineEntity
        {
            LineId = 0,
            OrderId = 9,
            Product = "b",
        };
        order.MarkAdded();
        first.MarkAdded();
        second.MarkAdded();
        order.SyncOrderLines.Add(first);
        order.SyncOrderLines.Add(second);

        await orders.SaveAsync(order, cancellationToken: Ct);

        (await Journal.ReadAllAsync(Ct))
            .Where(entry => entry.TableName == "sync_order_lines")
            .Select(entry => entry.KeyText)
            .Should()
            .BeEquivalentTo(["101", "102"], "フックが決めたキーへ移る（仮のキー 0 は残らない）");

        var result = await Engine.SyncAsync(cancellationToken: Ct);

        result.Conflicts.Should().BeEmpty();
        (await ServerLines.GetByIdAsync(101, Ct))!.Product.Should().Be("a");
        (await ServerLines.GetByIdAsync(102, Ct))!.Product.Should().Be("b");
        (await LocalLinesRaw.GetByIdAsync(101, Ct))
            .Should()
            .NotBeNull("同期は自分で入れた行を消さない");
    }

    /// <summary>挿入する明細に、決めた順にキーを振る Save フック。</summary>
    private sealed class LineKeyAssigningHook(int first) : ISaveHook<SyncOrderLineEntity>
    {
        private int _next = first;

        public Task<bool> BeforeSaveAsync(
            SyncOrderLineEntity entity,
            SaveOperation operation,
            CancellationToken cancellationToken = default
        )
        {
            if (operation == SaveOperation.Insert)
            {
                entity.LineId = _next++;
            }

            return Task.FromResult(true);
        }
    }

    /// <summary>
    /// BulkInsert の記録は行ごとの文でなく、複数行の INSERT（1 文 100 行）でジャーナルへ書かれる。
    /// </summary>
    /// <remarks>
    /// 行ごとに自動コミットの INSERT を出すと、1 万行で素の BulkInsert の数百倍かかっていた。件数と順序（id が
    /// 行の並びどおりに振られる）を確かめ、文の数はジャーナルの生 SQL 実行器を包んで数える（時間は表明しない）。
    /// </remarks>
    [Fact(DisplayName = "[Sync/記録] BulkInsert の記録は複数行の INSERT でまとめて書かれる")]
    public async Task BulkInsert_IsJournaledInMultiRowStatements()
    {
        var counting = new InterceptingSqlExecutor(LocalSql, (_, _) => Task.CompletedTask);
        var journal = new SyncJournal(counting);
        await journal.EnsureCreatedAsync(Ct);
        var orders = new JournalingSyncOrderRepository(LocalOrdersRaw, journal);
        var rows = Enumerable
            .Range(1, 10_000)
            .Select(id => new SyncOrderEntity { OrderId = id, CustomerName = $"c{id}" })
            .ToList();

        counting.Executed.Clear();
        await orders.BulkInsertAsync(rows, Ct);

        counting
            .Executed.Count(sql =>
                sql.StartsWith("INSERT INTO quicker_sync_journal", StringComparison.Ordinal)
            )
            .Should()
            .Be(100, "1 文 100 行");

        var entries = await Journal.ReadAllAsync(Ct);
        entries
            .Select(entry => entry.KeyText)
            .Should()
            .Equal(
                rows.Select(row =>
                    row.OrderId.ToString(System.Globalization.CultureInfo.InvariantCulture)
                )
            );
        entries
            .Should()
            .OnlyContain(entry => entry.Operation == nameof(SyncJournalOperation.Upsert));
    }

    /// <summary>複数行の記録は文の境界をまたいでも、版（BLOB）と NULL を行ごとに取り違えない。</summary>
    [Fact(
        DisplayName = "[Sync/記録] RecordManyAsync は文の境界をまたいでも版と NULL を行ごとに保つ"
    )]
    public async Task RecordMany_KeepsEachRowsValuesAcrossStatementBoundaries()
    {
        var records = Enumerable
            .Range(0, 101)
            .Select(index => new SyncJournalRecord(
                index % 2 == 0 ? "sync_orders" : "sync_order_lines",
                $"k{index}",
                index % 3 == 0 ? SyncJournalOperation.Delete : SyncJournalOperation.Upsert,
                index % 3 == 0 ? [(byte)index, 0xFF] : null
            ))
            .ToList();

        await Journal.RecordManyAsync(records, Ct);

        var entries = await Journal.ReadAllAsync(Ct);
        entries
            .Select(entry =>
                (entry.TableName, entry.KeyText, entry.Operation, entry.OriginalRowVersion)
            )
            .Should()
            .BeEquivalentTo(
                records.Select(record =>
                    (
                        record.TableName,
                        record.KeyText,
                        record.Operation.ToString(),
                        record.OriginalRowVersion
                    )
                ),
                options => options.WithStrictOrdering()
            );
    }

    /// <summary>最初の差分取得の直前に一度だけローカルへ書く差分ソース（ダウンロードの最中へ割り込むテストダブル）。</summary>
    /// <remarks>
    /// 上限（ceiling）の取得では発火しない。上限はダウンロードの先頭で取られるため、そこで書くと「ダウンロードの前」
    /// になってしまい、ランの冒頭でジャーナルを 1 回だけ読む実装と区別できない。
    /// </remarks>
    private sealed class LocalWriteOnFirstChangesOrderSource(
        ISyncServerSource<SyncOrderEntity, int> inner,
        Func<CancellationToken, Task> onFirstChanges
    ) : ISyncServerSource<SyncOrderEntity, int>
    {
        private int _fired;

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
            if (Interlocked.Exchange(ref _fired, 1) == 0)
            {
                await onFirstChanges(cancellationToken);
            }

            return await inner.GetChangesAsync(anchor, ceiling, batchSize, cancellationToken);
        }

        public Task<IReadOnlyList<int>> GetAllKeysAsync(
            CancellationToken cancellationToken = default
        ) => inner.GetAllKeysAsync(cancellationToken);
    }

    /// <summary>
    /// 生 SQL 実行器を包み、問い合わせの直前に割り込み、実行した文を数えるテストダブル。
    /// </summary>
    private sealed class InterceptingSqlExecutor(
        ISqlExecutor inner,
        Func<string, CancellationToken, Task> beforeQuery
    ) : ISqlExecutor
    {
        /// <summary>
        /// ExecuteSqlAsync と QueryProjectionBySqlAsync で実行した文（ロックの外から並行に書かれることは無い前提の
        /// 観測用。デコレータの記録は、取り消せるように id を読み戻す形＝QueryProjectionBySqlAsync で書く）
        /// </summary>
        public List<string> Executed { get; } = [];

        public async Task<IReadOnlyList<TEntity>> QueryBySqlAsync<TEntity>(
            string sql,
            object? parameters = null,
            CancellationToken cancellationToken = default
        )
            where TEntity : EntityBaseCore, new()
        {
            await beforeQuery(sql, cancellationToken);

            return await inner.QueryBySqlAsync<TEntity>(sql, parameters, cancellationToken);
        }

        public async Task<IReadOnlyList<TResult>> QueryProjectionBySqlAsync<TResult>(
            string sql,
            object? parameters = null,
            CancellationToken cancellationToken = default
        )
        {
            await beforeQuery(sql, cancellationToken);
            Executed.Add(sql);

            return await inner.QueryProjectionBySqlAsync<TResult>(
                sql,
                parameters,
                cancellationToken
            );
        }

        public Task<int> ExecuteSqlAsync(
            string sql,
            object? parameters = null,
            CancellationToken cancellationToken = default
        )
        {
            Executed.Add(sql);

            return inner.ExecuteSqlAsync(sql, parameters, cancellationToken);
        }

        public Task<TResult?> ExecuteScalarSqlAsync<TResult>(
            string sql,
            object? parameters = null,
            CancellationToken cancellationToken = default
        ) => inner.ExecuteScalarSqlAsync<TResult>(sql, parameters, cancellationToken);
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
    /// ServerWins で適用した行にダウンロードが届いていなくても、再編集が偽の更新競合にならない。
    /// </summary>
    /// <remarks>
    /// 適用はサーバーの内容だけを書いてミラー版を据え置くため、ダウンロードがその行へ届いていないと（同じテーブルの
    /// 打ち切り・差分ソースの遅れなど）ミラーは旧版のまま残る。受理記録が無ければ次の再生は旧版を original に採り、
    /// 誰とも競合していないのに ModifiedOnServer になる（打ち切り経路と同型の穴が ServerWins 側にも開く）。
    /// 「届いていない」は、最初の差分取得だけ空を返す差分ソースで作る。
    /// </remarks>
    [Fact(
        DisplayName = "[Sync/ack] ServerWins で適用した行にダウンロードが届いていなくても再編集は偽競合にならない"
    )]
    public async Task ServerWinsApplied_WithoutDownload_ThenLocalEdit_DoesNotReportFalseModifiedConflict()
    {
        await SeedServerAsync(1, "alice", 11, "widget");
        await Engine.SyncAsync(cancellationToken: Ct);

        // ローカルの編集と、他者によるサーバー側の更新（＝サーバー版がミラーより進む）
        await EditLocalOrderAsync(1, "alice-local");
        var server = await ServerOrders.GetByIdAsync(1, Ct);
        server!.CustomerName = "alice-server";
        await ServerOrders.UpdateAsync(server, cancellationToken: Ct);

        // ServerWins の適用は通し、ダウンロードはその行へ届かせない
        var lagging = new EmptyFirstChangesOrderSource(_sources.Orders);
        await CreateEngineWithOrderSource(lagging)
            .SyncAsync(new SyncOptions { ConflictPolicy = SyncConflictPolicy.ServerWins }, Ct);
        (await LocalOrdersRaw.GetByIdAsync(1, Ct))!
            .CustomerName.Should()
            .Be("alice-server", "ダウンロードは届かなかったが、適用は済んでいる");

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

    /// <summary>最初の差分取得だけ空を返す差分ソース（ダウンロードが行へ届かなかったランを作るテストダブル）</summary>
    private sealed class EmptyFirstChangesOrderSource(ISyncServerSource<SyncOrderEntity, int> inner)
        : ISyncServerSource<SyncOrderEntity, int>
    {
        private int _fired;

        public IRemoteRepositoryCore<SyncOrderEntity, int> Writer => inner.Writer;

        public ISyncBinaryColumns<int>? BinaryColumns => inner.BinaryColumns;

        public Task<byte[]?> GetChangeCeilingAsync(CancellationToken cancellationToken = default) =>
            inner.GetChangeCeilingAsync(cancellationToken);

        public async Task<SyncChangeBatch<SyncOrderEntity>> GetChangesAsync(
            byte[]? anchor,
            byte[]? ceiling,
            int batchSize,
            CancellationToken cancellationToken = default
        ) =>
            Interlocked.Exchange(ref _fired, 1) == 0
                ? new SyncChangeBatch<SyncOrderEntity>([], HasMore: false)
                : await inner.GetChangesAsync(anchor, ceiling, batchSize, cancellationToken);

        public Task<IReadOnlyList<int>> GetAllKeysAsync(
            CancellationToken cancellationToken = default
        ) => inner.GetAllKeysAsync(cancellationToken);
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
    /// blob を運ぶ回でも、この端末が blob を書いていない行の編集はサーバーの blob を消さない。
    /// </summary>
    /// <remarks>
    /// 既定の回で降りた行はローカルに blob を持たない。その行の名前だけを編集して blob を運ぶ回で上げると、
    /// 行の全除外列をコピーしていた頃は空の列がサーバーの blob を NULL で上書きした。送るのはその行の記録が
    /// 「値を書いた」と言う列（挿入・Write アクセサ）だけで、更新は列を書かない。
    /// </remarks>
    [Fact(
        DisplayName = "[Sync/blob] blob を書いていない行の編集は blob を運ぶ回でもサーバーの blob を消さない"
    )]
    public async Task EditOfRowWithoutLocalBlob_DoesNotClearTheServerBlob()
    {
        await SeedServerAsync(1, "alice", 11, "widget");
        await WriteServerBlobAsync(1, [1, 2, 3]);
        await Engine.SyncAsync(cancellationToken: Ct);

        await EditLocalOrderAsync(1, "alice-local");

        var result = await Engine.SyncAsync(BlobOptions, Ct);

        result.Uploaded.Should().Be(1);
        (await ServerOrders.GetByIdAsync(1, Ct))!.CustomerName.Should().Be("alice-local");
        (await ReadServerBlobAsync(1))
            .Should()
            .Equal([1, 2, 3], "この端末が書いていない列は送らない");
    }

    /// <summary>
    /// 挿入は全除外列を書いたものとして記録され、blob を運ぶ回でそのまま上がる。
    /// </summary>
    [Fact(DisplayName = "[Sync/blob] 挿入した行の blob は記録され blob を運ぶ回で上がる")]
    public async Task InsertedRowsBlob_IsRecordedAndUploaded()
    {
        await LocalOrders.InsertAsync(
            new SyncOrderEntity
            {
                OrderId = 6,
                CustomerName = "erin",
                Attachment = [6, 6],
            },
            Ct
        );

        (await Journal.ReadAllAsync(Ct))
            .Should()
            .ContainSingle()
            .Which.UnboundedBinaryColumns.Should()
            .Be("Attachment");

        await Engine.SyncAsync(BlobOptions, Ct);

        (await ReadServerBlobAsync(6)).Should().Equal([6, 6]);
    }

    /// <summary>
    /// ダウンロードの blob のコピーが一度失敗しても、次に blob を運ぶ回で取り直す。
    /// </summary>
    /// <remarks>
    /// 行のバッチはコピーより先にコミットされ、ミラー版が再開点をその行より先へ進める。コピーが失敗すると、
    /// 以後のダウンロードはその行を二度と降ろさず、blob は空のまま残っていた。コピー待ちの記録を行の適用の前に
    /// 残し、残った分は次の回の最初にコピーする。
    /// </remarks>
    [Fact(
        DisplayName = "[Sync/blob] ダウンロードの blob のコピーが失敗しても次に blob を運ぶ回で取り直す"
    )]
    public async Task FailedBlobDownload_IsRetriedByTheNextRun()
    {
        await SeedServerAsync(1, "alice", 11, "widget");
        await WriteServerBlobAsync(1, [1, 2, 3]);

        var failing = CreateEngineWithOrderSource(
            new BlobFailingOrderSource(_sources.Orders, failRead: true, failWrite: false)
        );
        var act = async () => await failing.SyncAsync(BlobOptions, Ct);
        await act.Should().ThrowAsync<InvalidOperationException>();
        (await LocalOrdersRaw.GetByIdAsync(1, Ct)).Should().NotBeNull("行は先に入っている");

        await Engine.SyncAsync(BlobOptions, Ct);

        (await ReadLocalBlobAsync(1)).Should().Equal([1, 2, 3], "コピー待ちの記録から取り直す");
    }

    /// <summary>洗い替えの blob のコピーが一度失敗しても、次に blob を運ぶ回で取り直す。</summary>
    [Fact(
        DisplayName = "[Sync/blob] 洗い替えの blob のコピーが失敗しても次に blob を運ぶ回で取り直す"
    )]
    public async Task FailedBlobRefresh_IsRetriedByTheNextRun()
    {
        await SeedServerAsync(1, "alice", 11, "widget");
        await WriteServerBlobAsync(1, [4, 5]);

        var failing = CreateEngineWithOrderSource(
            new BlobFailingOrderSource(_sources.Orders, failRead: true, failWrite: false)
        );
        var act = async () =>
            await failing.RefreshAsync(
                new SyncRefreshOptions { IncludeUnboundedBinary = true },
                Ct
            );
        await act.Should().ThrowAsync<InvalidOperationException>();

        await Engine.SyncAsync(BlobOptions, Ct);

        (await ReadLocalBlobAsync(1)).Should().Equal([4, 5]);
    }

    /// <summary>
    /// 取り直しは、その列を書いた未送信の記録がある行の blob をサーバーの blob で上書きしない（記録は捨てる）。
    /// </summary>
    [Fact(DisplayName = "[Sync/blob] コピー待ちの取り直しはその列を書いた未送信の行を上書きしない")]
    public async Task OwedBlobCopy_DoesNotOverwriteAnUnsentLocalBlob()
    {
        await SeedServerAsync(1, "alice", 11, "widget");
        await WriteServerBlobAsync(1, [1, 2, 3]);

        var failing = CreateEngineWithOrderSource(
            new BlobFailingOrderSource(_sources.Orders, failRead: true, failWrite: false)
        );
        var act = async () => await failing.SyncAsync(BlobOptions, Ct);
        await act.Should().ThrowAsync<InvalidOperationException>();

        // コピー待ちが残っている行へ、この端末で blob を書く（未送信の記録が付く）
        await WriteLocalBlobAsync(1, [9, 9]);

        // アップロードを伴わないダウンロードだけを流す＝未送信の記録が残ったまま取り直しが走る
        await Engine.DownloadAsync(BlobOptions, Ct);

        (await ReadLocalBlobAsync(1))
            .Should()
            .Equal([9, 9], "未送信の書き込みはサーバーの blob で上書きしない");

        await Engine.SyncAsync(BlobOptions, Ct);

        (await ReadServerBlobAsync(1)).Should().Equal([9, 9], "次のランでこの端末の blob が上がる");
    }

    /// <summary>
    /// 取り直しは、blob の列を書いていない未送信の記録（名前だけの編集）がある行でも、サーバーの blob を降ろす。
    /// </summary>
    /// <remarks>
    /// 未送信の記録があるキーを一律に飛ばすと、名前だけを編集した行のローカルの blob は降りてきていないまま
    /// （空）なのに、コピー待ちの記録だけが消え、その blob は二度と取り直されない。飛ばすのは、その列を書いた
    /// 記録があるときだけ。
    /// </remarks>
    [Fact(
        DisplayName = "[Sync/blob] コピー待ちの取り直しは名前だけ編集した未送信の行でも blob を降ろす"
    )]
    public async Task OwedBlobCopy_IsMadeForARowEditedOnlyInOrdinaryColumns()
    {
        await SeedServerAsync(1, "alice", 11, "widget");
        await WriteServerBlobAsync(1, [1, 2, 3]);

        var failing = CreateEngineWithOrderSource(
            new BlobFailingOrderSource(_sources.Orders, failRead: true, failWrite: false)
        );
        var act = async () => await failing.SyncAsync(BlobOptions, Ct);
        await act.Should().ThrowAsync<InvalidOperationException>();

        // コピー待ちが残っている行の名前だけを編集する（blob の列は書かない未送信の記録）
        await EditLocalOrderAsync(1, "alice-local");

        await Engine.DownloadAsync(BlobOptions, Ct);

        (await ReadLocalBlobAsync(1))
            .Should()
            .Equal([1, 2, 3], "この端末は blob を書いていない＝サーバーの blob を降ろす");
        (await LocalOrdersRaw.GetByIdAsync(1, Ct))!
            .CustomerName.Should()
            .Be("alice-local", "名前の編集は残る");
    }

    /// <summary>
    /// blob のアップロードが一度失敗しても、次の回でこの端末自身の行に対する偽の更新競合にならない。
    /// </summary>
    /// <remarks>
    /// 行の書き込みはサーバーに受理され版が進んでいるが、受理記録は blob の後でしか書いていなかったため、blob の
    /// 失敗でジャーナルは残りミラーは旧版のまま＝次の回の再生が自分の書いた版を他者の変更と取り違えた。行の
    /// 書き込みが返した版で先に受理記録を書く。
    /// </remarks>
    [Fact(
        DisplayName = "[Sync/blob] blob のアップロードが失敗しても次の回で偽の更新競合にならない"
    )]
    public async Task FailedBlobUpload_DoesNotReportAFalseConflictNextRun()
    {
        await SeedServerAsync(1, "alice", 11, "widget");
        await Engine.SyncAsync(BlobOptions, Ct);

        await EditLocalOrderAsync(1, "alice-local");
        await WriteLocalBlobAsync(1, [7, 7]);

        var failing = CreateEngineWithOrderSource(
            new BlobFailingOrderSource(_sources.Orders, failRead: false, failWrite: true)
        );
        var act = async () => await failing.SyncAsync(BlobOptions, Ct);
        await act.Should().ThrowAsync<InvalidOperationException>();

        var result = await Engine.SyncAsync(BlobOptions, Ct);

        result.Conflicts.Should().BeEmpty("受理記録が行の書き込みの版を覚えている");
        result.Uploaded.Should().Be(1);
        (await ServerOrders.GetByIdAsync(1, Ct))!.CustomerName.Should().Be("alice-local");
        (await ReadServerBlobAsync(1)).Should().Equal([7, 7]);
    }

    /// <summary>サーバー側の blob の読み書きを、それぞれ最初の 1 回だけ失敗させる差分ソース</summary>
    private sealed class BlobFailingOrderSource(
        ISyncServerSource<SyncOrderEntity, int> inner,
        bool failRead,
        bool failWrite
    ) : ISyncServerSource<SyncOrderEntity, int>
    {
        private readonly FailOnceBinaryColumns _binaryColumns = new(
            inner.BinaryColumns!,
            failRead,
            failWrite
        );

        public IRemoteRepositoryCore<SyncOrderEntity, int> Writer => inner.Writer;

        public ISyncBinaryColumns<int>? BinaryColumns => _binaryColumns;

        public Task<byte[]?> GetChangeCeilingAsync(CancellationToken cancellationToken = default) =>
            inner.GetChangeCeilingAsync(cancellationToken);

        public Task<SyncChangeBatch<SyncOrderEntity>> GetChangesAsync(
            byte[]? anchor,
            byte[]? ceiling,
            int batchSize,
            CancellationToken cancellationToken = default
        ) => inner.GetChangesAsync(anchor, ceiling, batchSize, cancellationToken);

        public Task<IReadOnlyList<int>> GetAllKeysAsync(
            CancellationToken cancellationToken = default
        ) => inner.GetAllKeysAsync(cancellationToken);
    }

    /// <summary>blob の読み書きを最初の 1 回だけ失敗させる（失敗は書き込みの前＝サーバーは何も変わらない）</summary>
    private sealed class FailOnceBinaryColumns(
        ISyncBinaryColumns<int> inner,
        bool failRead,
        bool failWrite
    ) : ISyncBinaryColumns<int>
    {
        private int _readFired;
        private int _writeFired;

        public IReadOnlyList<string> UnboundedBinaryColumnNames => inner.UnboundedBinaryColumnNames;

        public Task<bool> ReadUnboundedBinaryAsync(
            string columnName,
            int id,
            Stream destination,
            CancellationToken cancellationToken = default
        ) =>
            failRead && Interlocked.Exchange(ref _readFired, 1) == 0
                ? throw new InvalidOperationException("blob read failed on purpose")
                : inner.ReadUnboundedBinaryAsync(columnName, id, destination, cancellationToken);

        public Task<bool> WriteUnboundedBinaryAsync(
            string columnName,
            int id,
            Stream? source,
            long? length,
            CancellationToken cancellationToken = default
        ) =>
            failWrite && Interlocked.Exchange(ref _writeFired, 1) == 0
                ? throw new InvalidOperationException("blob write failed on purpose")
                : inner.WriteUnboundedBinaryAsync(
                    columnName,
                    id,
                    source,
                    length,
                    cancellationToken
                );
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
