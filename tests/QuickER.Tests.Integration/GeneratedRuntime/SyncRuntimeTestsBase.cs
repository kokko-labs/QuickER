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

    /// <summary>
    /// サーバー役のマーカー（素のリポジトリ）。この基底のエンジンは注文・明細・メモの 3 表だけで組むので、マーカーは
    /// HTTP の同期エンドポイントが全表の差分ソースを求める分を満たすためだけにある
    /// </summary>
    protected ISyncMarkerRepository ServerMarkers { get; private set; } = null!;

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
        ServerMarkers = new SyncMarkerRepository(serverFactory);

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

    /// <summary>サーバー役の注文差分ソース（SQLite 版）を組み立てる（直結の実体・HTTP のサーバー側登録の両方で使う）</summary>
    protected ISyncServerSource<SyncOrderEntity, int> CreateOrderTestSource() =>
        SyncTestServerSources.CreateOrders(ServerSql, ServerOrders, ServerOrderBlobs);

    /// <summary>サーバー役の明細差分ソース（SQLite 版）を組み立てる</summary>
    protected ISyncServerSource<SyncOrderLineEntity, int> CreateLineTestSource() =>
        SyncTestServerSources.CreateLines(ServerSql, ServerLines);

    /// <summary>サーバー役のメモ（版なしテーブル）差分ソース（SQLite 版）を組み立てる</summary>
    protected ISyncServerSource<SyncNoteEntity, int> CreateNoteTestSource() =>
        SyncTestServerSources.CreateNotes(ServerSql, ServerNotes);

    /// <summary>サーバー役のマーカーの差分ソース（SQLite 版。HTTP のサーバー側登録を満たすためだけに使う）</summary>
    protected ISyncServerSource<SyncMarkerEntity, int> CreateMarkerTestSource() =>
        SyncTestServerSources.CreateMarkers(ServerSql, ServerMarkers);

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

    // ---- ループ防止 ----

    // ---- アップロード ----

    // ---- カスケードグラフの記録（SyncGraphRecorder） ----

    // ---- 後勝ちモード（版なしテーブル・SyncMode.LastWriteWins） ----

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

    // ---- 再生する操作の決め方（いまのローカルの行と、その行の記録全体）----

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
    /// 記録を書いた後で Before フックが新しい行のキーを決める書き込みがあっても、書き換えた記録はアップロードの判定で
    /// 捨てられない（ロックの外で読んだ古いキーで判定しない）。
    /// </summary>
    /// <remarks>
    /// 記録はキーが決まる前（キー 0）に作り、書き込みの後の突き合わせが決まったキーへ書き換える（同じロックの中）。
    /// アップロードはジャーナルをロックの外で読むので、読んだのは書き換える前の記録になり得る。その古いキーで行の有無を
    /// 見ると「行が無い」と判定し、id で記録を消すので、書き換え済みの記録まで消える。
    /// </remarks>
    [Fact(
        DisplayName = "[Sync/並行] 記録の後で Before フックがキーを決める書き込みの記録はアップロードの判定で捨てられない"
    )]
    public async Task InFlightInsertRekeyedByHook_IsNotDiscardedByTheUpload()
    {
        var hook = new PausingSaveHook(entity => entity.OrderId = 61);
        var orders = CreateHookedOrders(hook);
        var order = new SyncOrderEntity { CustomerName = "rekeyed" };
        order.MarkAdded();

        await RunSyncWhileSaveIsPausedAsync(
            hook,
            () => orders.SaveAsync(order, cancellationToken: Ct)
        );
        await Engine.SyncAsync(cancellationToken: Ct);

        var server = await ServerOrders.GetByIdAsync(61, Ct);
        server.Should().NotBeNull("書き換えた記録は捨てられず、着地した行が送られる");
        server!.CustomerName.Should().Be("rekeyed");
        (await LocalOrdersRaw.GetByIdAsync(61, Ct)).Should().NotBeNull();
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
    /// <param name="onRelease">解放の後、保存を進める前に行へ当てる変更（キーを決めるなど）</param>
    private sealed class PausingSaveHook(Action<SyncOrderEntity>? onRelease = null)
        : ISaveHook<SyncOrderEntity>
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
            onRelease?.Invoke(entity);

            return true;
        }
    }

    // ---- 同期中のローカル書き込み（ロックとジャーナルの読み直し）----

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

    /// <summary>注文の Before か After で、渡した処理を実行する Save フック。</summary>
    private sealed class ActionHook(bool inBefore, Func<CancellationToken, Task> action)
        : ISaveHook<SyncOrderEntity>
    {
        public async Task<bool> BeforeSaveAsync(
            SyncOrderEntity entity,
            SaveOperation operation,
            CancellationToken cancellationToken = default
        )
        {
            if (inBefore)
            {
                await action(cancellationToken);
            }

            return true;
        }

        public async Task AfterSaveAsync(
            SyncOrderEntity entity,
            SaveOperation operation,
            ISaveHookContext context,
            CancellationToken cancellationToken = default
        )
        {
            if (!inBefore)
            {
                await action(cancellationToken);
            }
        }
    }

    /// <summary>
    /// 注文の After で、context を通して blob を書く Save フック（書く行は保存した行か、指定したキーの行。キーは
    /// フックが渡すとおりの型で渡す）。
    /// </summary>
    private sealed class BlobWritingHook(object? key, byte[] content, bool throwAfter = false)
        : ISaveHook<SyncOrderEntity>
    {
        public bool? LastWritten { get; private set; }

        public async Task AfterSaveAsync(
            SyncOrderEntity entity,
            SaveOperation operation,
            ISaveHookContext context,
            CancellationToken cancellationToken = default
        )
        {
            using var source = new MemoryStream(content);
            LastWritten = await context.WriteBinaryColumnAsync(
                nameof(SyncOrderEntity.Attachment),
                key ?? entity.OrderId,
                source,
                cancellationToken: cancellationToken
            );

            if (throwAfter)
            {
                throw new InvalidOperationException("The hook failed after writing the blob.");
            }
        }
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

    // ---- 洗い替え（高速リフレッシュ） ----

    /// <summary>
    /// <see cref="SyncTableDescriptor{TEntity, TKey}.LocalReferences"/> に載らない外部キー（一意制約を参照するもの・複数列の
    /// もの）で範囲内の表を参照する範囲外の表は、ローカルに在れば行を見ずに断ることを検証する（在るだけで断り、
    /// ローカルに無ければ通る）。
    /// </summary>
    /// <remarks>
    /// SyncFixture の図にはそうした外部キーが無いので、注文の記述子を複製し、メモの表からの参照を
    /// <c>UnlistedReferencingTableNames</c> へ移したもので組む。
    /// </remarks>
    [Fact(
        DisplayName = "[Sync/洗い替え] 一覧に載らない外部キーで参照する範囲外の表は、ローカルに在れば行を見ずに断る"
    )]
    public async Task Refresh_UnlistedReferringTable_RefusesWhilePresentLocally()
    {
        await SeedServerAsync(1, "alice", 11, "widget");
        await Engine.SyncAsync(cancellationToken: Ct);
        var descriptor =
            (SyncTableDescriptor<SyncOrderEntity, int>)
                typeof(object)
                    .GetMethod(
                        "MemberwiseClone",
                        System.Reflection.BindingFlags.NonPublic
                            | System.Reflection.BindingFlags.Instance
                    )!
                    .Invoke(GeneratedSyncTables.SyncOrder, null)!;
        var type = typeof(SyncTableDescriptor<SyncOrderEntity, int>);
        type.GetProperty(nameof(descriptor.LocalReferences))!
            .SetValue(
                descriptor,
                descriptor
                    .LocalReferences.Where(reference => reference.TableName != "sync_notes")
                    .ToList()
            );
        type.GetProperty(nameof(descriptor.UnlistedReferencingTableNames))!
            .SetValue(descriptor, new List<string> { "sync_notes" });
        var engine = CreateEngineWithOrderTable(
            new SyncTable<SyncOrderEntity, int>(LocalOrders, LocalSql, _sources.Orders, descriptor)
        );
        var versioned = new SyncRefreshOptions
        {
            Mode = SyncMode.Versioned,
            DiscardLocalUnboundedBinaries = true,
        };

        // メモの行は無い（行で判定すれば通る）が、照会できないので表が在るだけで断る
        (await LocalNotesRaw.GetAllAsync(Ct))
            .Should()
            .BeEmpty();
        var act = async () => await engine.RefreshAsync(versioned, Ct);
        var refused = (await act.Should().ThrowAsync<SyncRefreshScopeException>()).Which;
        refused
            .References.Should()
            .Equal(new SyncRefreshScopeReference("sync_notes", "sync_orders"));
        (await LocalOrdersRaw.GetAllAsync(Ct)).Should().ContainSingle();

        await LocalSql.ExecuteSqlAsync("DROP TABLE sync_notes", null, Ct);
        (await engine.RefreshAsync(versioned, Ct))
            .Tables.Select(table => table.TableName)
            .Should()
            .Equal("sync_orders", "sync_order_lines");
    }

    // ---- 除外列: IncludeUnboundedBinary＝運ぶ ----

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

    /// <summary>
    /// ローカルの Before / After フックがほかの DB（サーバー）のリポジトリで保存し、その保存のフックが blob を書いても、
    /// ローカルのジャーナルの記録とは扱わない（サーバーの DB へ記録を書きに行かず、サーバーの保存は成功する）。
    /// </summary>
    [Theory(
        DisplayName = "[Sync/blob] ローカルのフックから始めた別の DB の保存でフックが書いた blob は、ローカルの記録にしない"
    )]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BlobWrittenByHookOfSaveStartedInLocalHook_IsNotRecorded(bool fromBefore)
    {
        await SeedServerAsync(1, "alice", 11, "widget");
        await SeedServerAsync(2, "dave", 21, "gadget");
        await Engine.SyncAsync(BlobOptions, Ct);

        var serverOrders = new SyncOrderRepository(
            new LightJournalSyncConnectionFactory(_server.ReadWriteCreateConnectionString),
            new SaveHookRegistry().Add(new BlobWritingHook(key: null, [3, 3]))
        );
        var hooked = CreateHookedOrders(
            new ActionHook(
                fromBefore,
                async cancellationToken =>
                {
                    var server = (await serverOrders.GetByIdAsync(2, cancellationToken))!;
                    server.CustomerName = "erin";
                    server.MarkUpdated();
                    await serverOrders.SaveAsync(server, cancellationToken: cancellationToken);
                }
            )
        );
        var local = (await hooked.GetByIdAsync(1, Ct))!;
        local.CustomerName = "bob";
        local.MarkUpdated();
        await hooked.SaveAsync(local, cancellationToken: Ct);

        (await ReadServerBlobAsync(2)).Should().Equal([3, 3]);
        (await serverOrders.GetByIdAsync(2, Ct))!.CustomerName.Should().Be("erin");
        (await Journal.ReadAllAsync(Ct))
            .Should()
            .ContainSingle("ローカルの更新の記録だけ")
            .Which.KeyText.Should()
            .Be("1");
    }

    // ---- ダウンロードの適用とローカルの Save フック ----

    /// <summary>
    /// フックの登録簿を渡さずに組んだ表で、ローカルの Before フックが適用の行を飛ばすと、黙って先へ進まず例外にする
    /// （1 回の保存で書いた後ろの行が再開点を飛ばした行の先へ進めるため）。
    /// </summary>
    [Fact(
        DisplayName = "[Sync/フック] 登録簿を渡さずに組んだ表でローカルの Before フックが行を飛ばすと、例外にする"
    )]
    public async Task Download_RowSkippedByHookOfTableBuiltWithoutRegistry_Throws()
    {
        foreach (var (id, name) in new[] { (1, "alice"), (2, "bob"), (3, "carol") })
        {
            await ServerOrders.InsertAsync(
                new SyncOrderEntity { OrderId = id, CustomerName = name },
                Ct
            );
        }

        var skip = new SkipRowHook<SyncOrderEntity>(order => order.OrderId == 2);
        var engine = CreateEngineWithLocalHooks(
            new SaveHookRegistry().Add(skip),
            passToTables: false
        );

        var sync = () => engine.SyncAsync(cancellationToken: Ct);

        (await sync.Should().ThrowAsync<InvalidOperationException>())
            .Which.Message.Should()
            .Contain("sync_orders")
            .And.Contain("save hook");
    }

    /// <summary>
    /// ローカルの 3 つの表のリポジトリへ同じ Save フックの登録簿を渡して、ジャーナルを共有するエンジンを組む
    /// （<paramref name="passToTables"/> が false なら、表へは登録簿を渡さない）。
    /// </summary>
    private SyncEngine CreateEngineWithLocalHooks(SaveHookRegistry hooks, bool passToTables = true)
    {
        var factory = new LightJournalSyncConnectionFactory(LocalConnectionString);
        var registry = passToTables ? hooks : null;
        var orders = new JournalingSyncOrderRepository(
            new SyncOrderRepository(factory, hooks),
            Journal
        );
        var lines = new JournalingSyncOrderLineRepository(
            new SyncOrderLineRepository(factory, hooks),
            Journal
        );
        var notes = new JournalingSyncNoteRepository(
            new SyncNoteRepository(factory, hooks),
            Journal
        );

        return new SyncEngine(
            [
                new SyncTable<SyncOrderEntity, int>(
                    orders,
                    LocalSql,
                    _sources.Orders,
                    GeneratedSyncTables.SyncOrder,
                    registry
                ),
                new SyncTable<SyncOrderLineEntity, int>(
                    lines,
                    LocalSql,
                    _sources.Lines,
                    GeneratedSyncTables.SyncOrderLine,
                    registry
                ),
                new VersionlessSyncTable<SyncNoteEntity, int>(
                    notes,
                    LocalSql,
                    _sources.Notes,
                    GeneratedSyncTables.SyncNote,
                    registry
                ),
            ],
            Journal
        );
    }

    /// <summary>条件に合う行の保存を Before で飛ばす Save フック（<see cref="Skipping"/> が false の間は飛ばさない）。</summary>
    private sealed class SkipRowHook<TEntity>(Func<TEntity, bool> match) : ISaveHook<TEntity>
        where TEntity : EntityBaseCore
    {
        public bool Skipping { get; set; } = true;

        public Task<bool> BeforeSaveAsync(
            TEntity entity,
            SaveOperation operation,
            CancellationToken cancellationToken = default
        ) => Task.FromResult(!(Skipping && match(entity)));
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
