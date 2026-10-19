using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using QuickER.Tests.GeneratedSyncFixture;
using QuickER.Tests.GeneratedSyncFixture.Repositories.Sqlite;
using QuickER.Tests.GeneratedSyncFixture.Repositories.SqlServer;
using QuickER.Tests.Integration;
using Xunit;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// 生成した同期支援を、実 SQL Server（サーバー）と実ファイル SQLite（ローカル）の間で動かす統合テスト。
/// </summary>
/// <remarks>
/// <para>
/// ここでしか確かめられないのは「本物の <c>rowversion</c> との噛み合わせ」＝DB が採番する版・
/// <c>MIN_ACTIVE_ROWVERSION()</c> による上限・<c>OUTPUT INSERTED</c> で回収した版の書き戻し・
/// 版ガードによる競合検出の 4 点。エンジンの筋（バッチ・アンカー導出・削除伝搬・競合の分類・ループ防止）は
/// <see cref="SyncSqliteRuntimeTests"/> が Docker 不在の CI でも常時通す。
/// </para>
/// <para>
/// あわせて、DI 登録糖衣 <c>AddGeneratedSyncSupport</c> が keyed 登録された 2 エンジンの上に正しく組み上がること
/// （ローカルのリポジトリがジャーナル記録デコレータへ差し替わること）も、この構成でしか確かめられない。
/// </para>
/// </remarks>
[Trait("Category", "Integration")]
[Collection(SqlServerContainerCollection.Name)]
[Trait("RequiresDocker", "true")]
public sealed class SyncSqlServerRuntimeTests(
    SqlServerContainerFixture fixture,
    ITestOutputHelper output
) : IAsyncLifetime
{
    private readonly SqlServerContainerFixture _fixture = fixture;
    private readonly SqliteTempDatabase _sqlite = SqliteTempDatabase.Create();
    private ServiceProvider _provider = null!;

    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    /// <summary>サーバー側キー（SQL Server）</summary>
    private const string ServerKey = "server";

    /// <summary>ローカル側キー（SQLite）</summary>
    private const string LocalKey = "local";

    /// <summary>サーバーは元の図（rowversion）、ローカルは方言変換した図（BLOB）でスキーマを作り DI を組む</summary>
    public async ValueTask InitializeAsync()
    {
        Assert.SkipUnless(_fixture.IsAvailable, _fixture.UnavailableReason);

        await _fixture.ResetSchemaAsync(Ct);
        await _fixture.ApplyDdlAsync(SyncFixtureDefinition.Build(), Ct);
        await _sqlite.ApplyDdlAsync(SyncFixtureDefinition.BuildSqliteMirror(), Ct);

        var services = new ServiceCollection();
        services.AddGeneratedSqlServerRepositories(ServerKey, _fixture.ConnectionString);
        services.AddGeneratedSqliteRepositories(LocalKey, _sqlite.ReadWriteCreateConnectionString);
        services.AddGeneratedSyncSupport(ServerKey, LocalKey);
        _provider = services.BuildServiceProvider();
    }

    /// <summary>DI コンテナと一時 DB を破棄する</summary>
    public ValueTask DisposeAsync()
    {
        _provider?.Dispose();
        _sqlite.Dispose();

        return ValueTask.CompletedTask;
    }

    /// <summary>サーバー側（SQL Server）の注文リポジトリ</summary>
    private ISyncOrderRepository ServerOrders() =>
        _provider.GetRequiredKeyedService<ISyncOrderRepository>(ServerKey);

    /// <summary>ローカル側（SQLite）の注文リポジトリ＝ジャーナル記録デコレータが被さっている</summary>
    private ISyncOrderRepository LocalOrders() =>
        _provider.GetRequiredKeyedService<ISyncOrderRepository>(LocalKey);

    /// <summary>ローカル側（SQLite）の明細リポジトリ</summary>
    private ISyncOrderLineRepository LocalLines() =>
        _provider.GetRequiredKeyedService<ISyncOrderLineRepository>(LocalKey);

    /// <summary>同期エンジン</summary>
    private SyncEngine Engine() => _provider.GetRequiredService<SyncEngine>();

    /// <summary>ジャーナル</summary>
    private SyncJournal Journal() => _provider.GetRequiredService<SyncJournal>();

    /// <summary>DI 糖衣がローカルのリポジトリをジャーナル記録デコレータへ差し替えていることを確認する</summary>
    [Fact(
        DisplayName = "[Sync/SqlServer] AddGeneratedSyncSupport がローカルの keyed 登録をデコレータへ差し替える"
    )]
    public void AddGeneratedSyncSupport_DecoratesLocalRepositories()
    {
        LocalOrders().Should().BeOfType<JournalingSyncOrderRepository>();
        LocalLines().Should().BeOfType<JournalingSyncOrderLineRepository>();

        // サーバー側は素のまま（記録するのはローカルの編集だけ）
        _provider
            .GetRequiredKeyedService<ISyncOrderRepository>(ServerKey)
            .Should()
            .BeOfType<QuickER.Tests.GeneratedSyncFixture.Repositories.SqlServer.SyncOrderRepository>();
    }

    /// <summary>
    /// 実 SQL Server 上の版なしテーブル＝後勝ちランの全量ダウンロード（TOP＋キー順ページング）とアップロード。
    /// </summary>
    /// <remarks>
    /// CI 常時の SQLite サーバー役はキー順ページングを LIMIT で代役するため、生成された
    /// <c>SELECT TOP (@batchSize) ... WHERE [note_id] &gt; @afterKey ORDER BY [note_id]</c> が本物の
    /// SQL Server で動くことはここでしか確かめられない。
    /// </remarks>
    [Fact(
        DisplayName = "[Sync/SqlServer] 版なしテーブルは後勝ちランで実サーバーと往復する（キー順 TOP ページング）"
    )]
    public async Task LastWriteWins_RoundTripsVersionlessTableAgainstRealServer()
    {
        var serverNotes = _provider.GetRequiredKeyedService<ISyncNoteRepository>(ServerKey);
        var localNotes = _provider.GetRequiredKeyedService<ISyncNoteRepository>(LocalKey);
        await ServerOrders()
            .InsertAsync(new SyncOrderEntity { OrderId = 1, CustomerName = "alice" }, Ct);
        await serverNotes.InsertAsync(
            new SyncNoteEntity
            {
                NoteId = 101,
                OrderId = 1,
                Body = "server note",
            },
            Ct
        );

        var lww = new SyncOptions { Mode = SyncMode.LastWriteWins };
        var result = await Engine().SyncAsync(lww, Ct);

        result.Conflicts.Should().BeEmpty();
        (await localNotes.GetByIdAsync(101, Ct))!.Body.Should().Be("server note");

        // ローカル編集（更新＋追加）が後勝ちで実サーバーへ届き、ジャーナルが決着する
        var local = await localNotes.GetByIdAsync(101, Ct);
        local!.Body = "edited locally";
        await localNotes.UpdateAsync(local, cancellationToken: Ct);
        await localNotes.InsertAsync(
            new SyncNoteEntity
            {
                NoteId = 102,
                OrderId = 1,
                Body = "new local",
            },
            Ct
        );

        var second = await Engine().SyncAsync(lww, Ct);

        second.Uploaded.Should().Be(2);
        (await serverNotes.GetByIdAsync(101, Ct))!.Body.Should().Be("edited locally");
        (await serverNotes.GetByIdAsync(102, Ct))!.Body.Should().Be("new local");
        (await Journal().CountPendingAsync(Ct)).Should().Be(0);

        // バッチ 1 でも全量が取り切れる＝実 SQL Server の TOP＋キー比較＋継続判定の実地検証
        var paged = await Engine()
            .SyncAsync(
                new SyncOptions { Mode = SyncMode.LastWriteWins, DownloadBatchSize = 1 },
                Ct
            );

        paged.Downloaded.Should().Be(2, "メモ全量（2 行）が 1 行バッチの継続取得で降りる");
    }

    /// <summary>実 rowversion の版ガードを後勝ちランは通過し、書き戻った版で次のランがエコーしない</summary>
    [Fact(
        DisplayName = "[Sync/SqlServer] 後勝ちランは実 rowversion の版ガードなしで上書きしエコーもしない"
    )]
    public async Task LastWriteWins_OverwritesDespiteRealRowVersion()
    {
        await ServerOrders()
            .InsertAsync(new SyncOrderEntity { OrderId = 1, CustomerName = "alice" }, Ct);
        await Engine().SyncAsync(cancellationToken: Ct);

        // すれ違い（Versioned なら ModifiedOnServer 競合になる形）
        var server = await ServerOrders().GetByIdAsync(1, Ct);
        server!.CustomerName = "server-edit";
        await ServerOrders().UpdateAsync(server, cancellationToken: Ct);
        var local = await LocalOrders().GetByIdAsync(1, Ct);
        local!.CustomerName = "local-edit";
        await LocalOrders().UpdateAsync(local, ConcurrencyMode.ForceOverwrite, Ct);

        var lww = new SyncOptions { Mode = SyncMode.LastWriteWins };
        var result = await Engine().SyncAsync(lww, Ct);

        result.Conflicts.Should().BeEmpty();
        result.Uploaded.Should().Be(1);
        (await ServerOrders().GetByIdAsync(1, Ct))!.CustomerName.Should().Be("local-edit");

        var second = await Engine().SyncAsync(lww, Ct);

        second.Uploaded.Should().Be(0);
        second
            .Downloaded.Should()
            .Be(0, "実 rowversion の書き戻しでアンカーが進んでいる＝自分の変更を取り戻さない");
    }

    /// <summary>
    /// 実 rowversion に対する初回同期＝サーバーの全行がローカルへ降り、DB 採番の版がミラー列へ入る。
    /// </summary>
    [Fact(
        DisplayName = "[Sync/SqlServer] 初回同期で全行が降り、DB 採番の rowversion がミラー列へ入る"
    )]
    public async Task InitialSync_MirrorsServerAssignedRowVersions()
    {
        await ServerOrders()
            .InsertAsync(new SyncOrderEntity { OrderId = 1, CustomerName = "alice" }, Ct);

        var result = await Engine().SyncAsync(cancellationToken: Ct);

        result.Downloaded.Should().Be(1);
        var local = await LocalOrders().GetByIdAsync(1, Ct);
        local.Should().NotBeNull();
        local!.CustomerName.Should().Be("alice");
        local.RowVer.Should().NotBeNullOrEmpty("SQL Server が採番した版がそのままミラーされる");

        // ダウンロード適用はジャーナルへ記録されない（ループ防止）
        (await Journal().CountPendingAsync(Ct))
            .Should()
            .Be(0);
    }

    /// <summary>
    /// 実 rowversion に対する差分同期＝アンカー（ローカルのミラー MAX）より新しい行だけが降りる。
    /// </summary>
    [Fact(
        DisplayName = "[Sync/SqlServer] 2 回目は rowversion がアンカーより新しい行だけを取り込む"
    )]
    public async Task IncrementalSync_UsesDerivedAnchor()
    {
        await ServerOrders()
            .InsertAsync(new SyncOrderEntity { OrderId = 1, CustomerName = "alice" }, Ct);
        await Engine().SyncAsync(cancellationToken: Ct);

        await ServerOrders()
            .InsertAsync(new SyncOrderEntity { OrderId = 2, CustomerName = "bob" }, Ct);

        var result = await Engine().SyncAsync(cancellationToken: Ct);

        result.Downloaded.Should().Be(1, "既に取り込んだ行はアンカーより下なので再取得されない");
        (await LocalOrders().GetByIdAsync(2, Ct)).Should().NotBeNull();
    }

    /// <summary>
    /// ローカルの編集をアップロードすると、サーバーが採番した新しい版がローカルのミラーへ書き戻る。
    /// </summary>
    [Fact(
        DisplayName = "[Sync/SqlServer] ローカル編集のアップロードで新しいサーバー版がミラーへ書き戻る"
    )]
    public async Task Upload_WritesBackNewServerVersion()
    {
        await ServerOrders()
            .InsertAsync(new SyncOrderEntity { OrderId = 1, CustomerName = "alice" }, Ct);
        await Engine().SyncAsync(cancellationToken: Ct);

        var before = (await LocalOrders().GetByIdAsync(1, Ct))!.RowVer;

        var local = await LocalOrders().GetByIdAsync(1, Ct);
        local!.CustomerName = "alice-offline";
        await LocalOrders().UpdateAsync(local, cancellationToken: Ct);

        var result = await Engine().SyncAsync(cancellationToken: Ct);

        result.Uploaded.Should().Be(1);
        result.Conflicts.Should().BeEmpty();
        (await ServerOrders().GetByIdAsync(1, Ct))!.CustomerName.Should().Be("alice-offline");

        var after = (await LocalOrders().GetByIdAsync(1, Ct))!.RowVer;
        after.Should().NotBeNull();
        after!.SequenceEqual(before!).Should().BeFalse("サーバーが採番し直した版がミラーへ入る");
    }

    /// <summary>
    /// サーバーで先に更新された行へローカル編集を送ると、本物の rowversion 版ガードが競合を検出する。
    /// </summary>
    [Fact(DisplayName = "[Sync/SqlServer] 本物の版ガードが競合を検出しジャーナルへ残す")]
    public async Task Conflict_IsDetectedByRealRowVersionGuard()
    {
        await ServerOrders()
            .InsertAsync(new SyncOrderEntity { OrderId = 1, CustomerName = "alice" }, Ct);
        await Engine().SyncAsync(cancellationToken: Ct);

        var local = await LocalOrders().GetByIdAsync(1, Ct);
        local!.CustomerName = "offline-edit";
        await LocalOrders().UpdateAsync(local, cancellationToken: Ct);

        // Repository を経由しない「他者による更新」でサーバー側の版だけを進める
        await ServerOrders()
            .ExecuteSqlAsync(
                "UPDATE [sync_orders] SET [customer_name] = @n WHERE [order_id] = 1;",
                new { n = "server-edit" },
                Ct
            );

        var result = await Engine().SyncAsync(cancellationToken: Ct);

        var conflict = result.Conflicts.Should().ContainSingle().Subject;
        conflict.TableName.Should().Be("sync_orders");
        conflict.KeyText.Should().Be("1");
        conflict.Reason.Should().Be(SyncConflictReason.ModifiedOnServer);
        ((SyncOrderEntity)conflict.ServerEntity!).CustomerName.Should().Be("server-edit");

        (await Journal().CountPendingAsync(Ct))
            .Should()
            .Be(1, "解決していないエントリはジャーナルに残る");
    }

    /// <summary>
    /// サーバーから消えた行をローカルが編集していた場合、競合として保留され、同じ実行の削除伝搬にも消されない。
    /// </summary>
    /// <remarks>
    /// 実 SQL Server では削除の検出も差分走査の相手も本物（キー集合は実テーブルの SELECT・アンカーは
    /// <c>MIN_ACTIVE_ROWVERSION()</c> 下の実 rowversion）なので、代役 SQLite で通っているガードが
    /// 本物の構成でも同じ順序（アップロード → ダウンロード → 伝搬）で効くことをここで確かめる。
    /// </remarks>
    [Fact(
        DisplayName = "[Sync/SqlServer] サーバーで消えた行のローカル編集は競合として保留され削除伝搬に消されない"
    )]
    public async Task MissingOnServerConflict_SurvivesDeletePropagation()
    {
        await ServerOrders()
            .InsertAsync(new SyncOrderEntity { OrderId = 1, CustomerName = "alice" }, Ct);
        await Engine().SyncAsync(cancellationToken: Ct);

        await ServerOrders().DeleteAsync(1, Ct);

        var local = await LocalOrders().GetByIdAsync(1, Ct);
        local!.CustomerName = "offline-edit";
        await LocalOrders().UpdateAsync(local, cancellationToken: Ct);

        var result = await Engine().SyncAsync(cancellationToken: Ct);

        var conflict = result.Conflicts.Should().ContainSingle().Subject;
        conflict.Reason.Should().Be(SyncConflictReason.MissingOnServer);
        result.DeletedLocally.Should().Be(0, "守られた行以外に消す対象が無い");

        // 読み取りはデコレータを素通しするので、ジャーナルへ余計なエントリを足さずに現状を見られる
        var survivor = await LocalOrders().GetByIdAsync(1, Ct);
        survivor.Should().NotBeNull("保留した競合の行を同じ実行が消してはいけない");
        survivor!.CustomerName.Should().Be("offline-edit");
        (await Journal().CountPendingAsync(Ct)).Should().Be(1);
    }

    /// <summary>
    /// 実 SQL Server に対する除外列（<c>varbinary(max)</c>）の往復＝含めるモードで両方向へ運ばれる。
    /// </summary>
    /// <remarks>
    /// <para>
    /// ここでしか確かめられないのは「本物の <c>rowversion</c> は blob の書き込みでも進む」という前提の上で
    /// エコー対策が成立すること。SQLite のサーバー役はその挙動を明示的に代役しているが、代役が実物と
    /// 食い違っていればここで落ちる（アップロード後にサーバー版を読み直さないと、上げた直後の同期が
    /// 自分の行を取り戻す）。
    /// </para>
    /// <para>
    /// あわせて、SQL Server 側のストリーミングエンジン（<c>SequentialAccess</c>＋<c>GetStream</c> と
    /// Stream 値の <c>SqlParameter</c>）と SQLite 側（<c>SqliteBlob</c>＋<c>zeroblob</c>）が、同期エンジンの
    /// 一時ファイル経由のコピーで噛み合うことも通る。
    /// </para>
    /// </remarks>
    [Fact(
        DisplayName = "[Sync/SqlServer] 含めるモードは実 varbinary(max) を両方向へ運び、上げた行を取り直さない"
    )]
    public async Task IncludeUnboundedBinary_RoundTripsAgainstRealSqlServer()
    {
        // サーバー側に blob つきの行を用意する（INSERT 後に Stream アクセサで書く＝2 段）
        await ServerOrders()
            .InsertAsync(new SyncOrderEntity { OrderId = 1, CustomerName = "alice" }, Ct);
        await WriteBlobAsync(ServerOrders(), 1, [1, 2, 3, 4, 5]);

        var download = await Engine()
            .SyncAsync(new SyncOptions { IncludeUnboundedBinary = true }, Ct);

        download.Conflicts.Should().BeEmpty();
        (await ReadBlobAsync(LocalOrders(), 1)).Should().Equal([1, 2, 3, 4, 5]);

        // ローカルで blob だけを差し替える（行の通常列は触らない＝Write アクセサの記録が唯一の手がかり）
        await WriteBlobAsync(LocalOrders(), 1, [9, 9]);
        (await Journal().CountPendingAsync(Ct)).Should().Be(1);

        var upload = await Engine()
            .SyncAsync(new SyncOptions { IncludeUnboundedBinary = true }, Ct);

        upload.Uploaded.Should().Be(1);
        upload.Conflicts.Should().BeEmpty();
        upload
            .Downloaded.Should()
            .Be(0, "blob の書き込みで進んだサーバー版を読み直さないと、自分の行が降りてくる");
        (await ReadBlobAsync(ServerOrders(), 1)).Should().Equal([9, 9]);

        var settled = await Engine()
            .SyncAsync(new SyncOptions { IncludeUnboundedBinary = true }, Ct);
        settled.Downloaded.Should().Be(0);
        settled.Uploaded.Should().Be(0);
    }

    /// <summary>
    /// 除外列を持つ図の洗い替えは、行き先を明示しない限り実 DB でも「何も消す前に」拒否される。
    /// </summary>
    [Fact(
        DisplayName = "[Sync/SqlServer] 除外列があると洗い替えは既定で拒否され、含めるモードでは blob ごと作り直す"
    )]
    public async Task Refresh_UnboundedBinaryGuard_AppliesAgainstRealSqlServer()
    {
        await ServerOrders()
            .InsertAsync(new SyncOrderEntity { OrderId = 1, CustomerName = "alice" }, Ct);
        await WriteBlobAsync(ServerOrders(), 1, [7, 7, 7]);

        var refused = async () => await Engine().RefreshAsync(cancellationToken: Ct);
        var exception = await refused.Should().ThrowAsync<SyncUnboundedBinaryLossException>();
        exception.Which.Columns.Should().ContainSingle().Which.TableName.Should().Be("sync_orders");
        (await LocalOrders().GetAllAsync(Ct)).Should().BeEmpty("拒否は何も消す前に起きる");

        var result = await Engine()
            .RefreshAsync(new SyncRefreshOptions { IncludeUnboundedBinary = true }, Ct);

        result.Inserted.Should().Be(1);
        (await ReadBlobAsync(LocalOrders(), 1)).Should().Equal([7, 7, 7]);
    }

    /// <summary>指定の注文行の除外列へ内容を書く（生成された Stream アクセサ経由）</summary>
    private static async Task WriteBlobAsync(
        ISyncOrderRepository repository,
        int orderId,
        byte[] content
    )
    {
        using var source = new System.IO.MemoryStream(content);
        var written = await repository.WriteAttachmentAsync(orderId, source, content.Length, Ct);
        written.Should().BeTrue();
    }

    /// <summary>指定の注文行の除外列を読む（行なし・NULL は null）</summary>
    private static async Task<byte[]?> ReadBlobAsync(ISyncOrderRepository repository, int orderId)
    {
        using var destination = new System.IO.MemoryStream();
        var present = await repository.ReadAttachmentAsync(orderId, destination, Ct);

        return present ? destination.ToArray() : null;
    }

    /// <summary>
    /// 実 rowversion に対する洗い替え＝ローカルを捨ててサーバー全行で作り直し、直後の差分同期が継続する。
    /// </summary>
    /// <remarks>
    /// ここでしか確かめられないのは「本物の版で作り直したローカルから導出したアンカーが、実
    /// <c>MIN_ACTIVE_ROWVERSION()</c> の上限と噛み合って正しく再開できる」こと。エンジンの筋
    /// （拒否・force・FK 順・バッチ継続）は <see cref="SyncSqliteRuntimeTests"/> 側の共通シナリオが通す。
    /// </remarks>
    [Fact(
        DisplayName = "[Sync/SqlServer] 洗い替えは実 rowversion で作り直し、直後の差分同期が継続する"
    )]
    public async Task Refresh_RebuildsLocalAndLeavesAResumableAnchor()
    {
        await ServerOrders()
            .InsertAsync(new SyncOrderEntity { OrderId = 1, CustomerName = "alice" }, Ct);
        await _provider
            .GetRequiredKeyedService<ISyncOrderLineRepository>(ServerKey)
            .InsertAsync(
                new SyncOrderLineEntity
                {
                    LineId = 11,
                    OrderId = 1,
                    Product = "widget",
                },
                Ct
            );

        // ローカルには古い内容を置いておく（作り直しで消える側）
        await LocalOrders()
            .InsertAsync(new SyncOrderEntity { OrderId = 99, CustomerName = "stale" }, Ct);

        var result = await Engine()
            .RefreshAsync(
                new SyncRefreshOptions { Force = true, DiscardLocalUnboundedBinaries = true },
                Ct
            );

        result.Deleted.Should().Be(1);
        result.Inserted.Should().Be(2);
        (await LocalOrders().GetAllAsync(Ct)).Select(order => order.OrderId).Should().Equal(1);

        var server = await ServerOrders().GetByIdAsync(1, Ct);
        (await LocalOrders().GetByIdAsync(1, Ct))!
            .RowVer.Should()
            .Equal(server!.RowVer, "DB が採番した版がそのままミラー列へ入る");

        // 作り直したローカルから導出したアンカーが次回の再開点として成立する
        await ServerOrders()
            .InsertAsync(new SyncOrderEntity { OrderId = 2, CustomerName = "bob" }, Ct);

        var next = await Engine().SyncAsync(cancellationToken: Ct);

        next.Downloaded.Should().Be(1, "洗い替えで入った版より新しい行だけが降りる");
        (await Journal().CountPendingAsync(Ct)).Should().Be(0);
    }

    /// <summary>
    /// 実 SQL Server を相手にした洗い替えと全量ダウンロードの所要時間を 1 回ずつ測る（表明は同値性だけ）。
    /// </summary>
    /// <remarks>
    /// SQLite 同士の計測（<see cref="SyncRefreshBenchmarkRuntimeTests"/>）と同じ方針で、時間は
    /// <see cref="ITestOutputHelper"/> へ出すだけにする。行数は環境変数
    /// <c>QUICKER_SYNC_BENCH_ORDERS</c> で上げられる（既定 1,000 件＝合計 2,000 行）。
    /// </remarks>
    [Fact(
        DisplayName = "[Sync/SqlServer] 洗い替えと全量ダウンロードは同じローカル内容を作る（所要時間は出力のみ）"
    )]
    public async Task Refresh_AgainstRealSqlServer_MatchesFullDownloadContent()
    {
        var orderCount = BenchmarkOrderCount;
        await SeedServerForBenchmarkAsync(orderCount);

        var batchSize = BenchmarkBatchSize;
        var download = await MeasureAgainstFreshLocalAsync(engine =>
            engine.DownloadAsync(
                new SyncOptions { DownloadBatchSize = batchSize, PropagateDeletes = false },
                Ct
            )
        );
        var refresh = await MeasureAgainstFreshLocalAsync(engine =>
            engine.RefreshAsync(
                new SyncRefreshOptions
                {
                    BatchSize = batchSize,
                    DiscardLocalUnboundedBinaries = true,
                },
                Ct
            )
        );

        output.WriteLine(
            $"データセット: 注文 {orderCount:N0} 件 ＋ 明細 {orderCount:N0} 件 = {orderCount * 2:N0} 行"
                + $"（バッチ {batchSize:N0}・サーバー＝実 SQL Server）"
        );
        output.WriteLine($"DownloadAsync {download.Elapsed.TotalMilliseconds, 9:F1} ms");
        output.WriteLine($"RefreshAsync  {refresh.Elapsed.TotalMilliseconds, 9:F1} ms");
        output.WriteLine($"倍率: DownloadAsync/Refresh = {download.Elapsed / refresh.Elapsed:F2}x");

        refresh
            .Digest.Should()
            .Be(download.Digest, "経路が違っても取り込む内容は同じでなければならない");
    }

    /// <summary>ベンチマークの注文件数（明細も同数）</summary>
    private static int BenchmarkOrderCount => Configured("QUICKER_SYNC_BENCH_ORDERS", 1_000);

    /// <summary>ベンチマークのバッチサイズ（両経路とも同じ値で回す）</summary>
    private static int BenchmarkBatchSize => Configured("QUICKER_SYNC_BENCH_BATCH", 500);

    /// <summary>環境変数の正の整数、無ければ既定値</summary>
    private static int Configured(string variable, int fallback) =>
        int.TryParse(Environment.GetEnvironmentVariable(variable), out var value) && value > 0
            ? value
            : fallback;

    /// <summary>ベンチマーク用にサーバー（SQL Server）を埋める（版は DB が採番する）</summary>
    private async Task SeedServerForBenchmarkAsync(int orderCount)
    {
        await ServerOrders()
            .BulkInsertAsync(
                Enumerable
                    .Range(1, orderCount)
                    .Select(id => new SyncOrderEntity
                    {
                        OrderId = id,
                        CustomerName = $"customer-{id:D6}",
                    })
                    .ToList(),
                Ct
            );
        await _provider
            .GetRequiredKeyedService<ISyncOrderLineRepository>(ServerKey)
            .BulkInsertAsync(
                Enumerable
                    .Range(1, orderCount)
                    .Select(id => new SyncOrderLineEntity
                    {
                        LineId = id,
                        OrderId = id,
                        Product = $"product-{id:D6}",
                    })
                    .ToList(),
                Ct
            );
    }

    /// <summary>まっさらなローカル SQLite を作り、渡された経路を 1 回計測してローカルの内容を要約する</summary>
    private async Task<(TimeSpan Elapsed, string Digest)> MeasureAgainstFreshLocalAsync(
        Func<SyncEngine, Task> run
    )
    {
        using var local = SqliteTempDatabase.Create();
        await local.ApplyDdlAsync(SyncFixtureDefinition.BuildSqliteMirror(), Ct);

        var localFactory =
            new QuickER.Tests.GeneratedSyncFixture.Repositories.Sqlite.SqlConnectionFactory(
                local.ReadWriteCreateConnectionString
            );
        var localSql = new QuickER.Tests.GeneratedSyncFixture.Repositories.Sqlite.SqlExecutor(
            localFactory
        );
        var journal = new SyncJournal(localSql);
        await journal.EnsureCreatedAsync(Ct);

        var orders = new JournalingSyncOrderRepository(
            new QuickER.Tests.GeneratedSyncFixture.Repositories.Sqlite.SyncOrderRepository(
                localFactory
            ),
            journal
        );
        var lines = new JournalingSyncOrderLineRepository(
            new QuickER.Tests.GeneratedSyncFixture.Repositories.Sqlite.SyncOrderLineRepository(
                localFactory
            ),
            journal
        );

        // 差分ソースは生成された直結実装そのもの（サーバー＝実 SQL Server）
        var engine = new SyncEngine(
            [
                new SyncTable<SyncOrderEntity, int>(
                    orders,
                    localSql,
                    new DirectSyncSource<SyncOrderEntity, int>(
                        _provider.GetRequiredKeyedService<ISqlExecutor>(ServerKey),
                        ServerOrders(),
                        GeneratedSyncTables.SyncOrder
                    ),
                    GeneratedSyncTables.SyncOrder
                ),
                new SyncTable<SyncOrderLineEntity, int>(
                    lines,
                    localSql,
                    new DirectSyncSource<SyncOrderLineEntity, int>(
                        _provider.GetRequiredKeyedService<ISqlExecutor>(ServerKey),
                        _provider.GetRequiredKeyedService<ISyncOrderLineRepository>(ServerKey),
                        GeneratedSyncTables.SyncOrderLine
                    ),
                    GeneratedSyncTables.SyncOrderLine
                ),
            ],
            journal
        );

        var stopwatch = Stopwatch.StartNew();
        await run(engine);
        stopwatch.Stop();

        var orderRows = await orders.GetAllAsync(Ct);
        var lineRows = await lines.GetAllAsync(Ct);
        var digest =
            string.Join(
                "|",
                orderRows
                    .OrderBy(order => order.OrderId)
                    .Select(order =>
                        $"{order.OrderId}:{order.CustomerName}:{Convert.ToHexString(order.RowVer ?? [])}"
                    )
            )
            + "#"
            + string.Join(
                "|",
                lineRows
                    .OrderBy(line => line.LineId)
                    .Select(line =>
                        $"{line.LineId}:{line.OrderId}:{line.Product}:{Convert.ToHexString(line.RowVer ?? [])}"
                    )
            );

        return (stopwatch.Elapsed, digest);
    }

    /// <summary>
    /// FK 順（親→子）で降ろすため、明細だけが先に届いて外部キー制約に触れることがない。
    /// </summary>
    [Fact(DisplayName = "[Sync/SqlServer] 親→子の順で適用され FK 制約に触れない")]
    public async Task Download_AppliesParentBeforeChild()
    {
        await ServerOrders()
            .InsertAsync(new SyncOrderEntity { OrderId = 1, CustomerName = "alice" }, Ct);
        await _provider
            .GetRequiredKeyedService<ISyncOrderLineRepository>(ServerKey)
            .InsertAsync(
                new SyncOrderLineEntity
                {
                    LineId = 11,
                    OrderId = 1,
                    Product = "widget",
                },
                Ct
            );

        var result = await Engine().SyncAsync(cancellationToken: Ct);

        result.Downloaded.Should().Be(2);
        (await LocalLines().GetByIdAsync(11, Ct)).Should().NotBeNull();
    }

    /// <summary>サーバー側（SQL Server）の明細リポジトリ</summary>
    private ISyncOrderLineRepository ServerLines() =>
        _provider.GetRequiredKeyedService<ISyncOrderLineRepository>(ServerKey);

    /// <summary>
    /// 実 SQL Server の外部キーエラー（SqlException）でも、親が消えた行の LocalWins の書き込みは
    /// ParentMissingOnServer として報告され、同期は止まらない。
    /// </summary>
    [Fact(
        DisplayName = "[Sync/SqlServer] 親が消えた行の LocalWins は実サーバーの外部キーエラーを ParentMissingOnServer で報告する"
    )]
    public async Task LocalWins_RowWhoseParentIsGone_IsReportedAsParentMissing()
    {
        await ServerOrders()
            .InsertAsync(new SyncOrderEntity { OrderId = 1, CustomerName = "alice" }, Ct);
        await ServerLines()
            .InsertAsync(
                new SyncOrderLineEntity
                {
                    LineId = 11,
                    OrderId = 1,
                    Product = "widget",
                },
                Ct
            );
        await Engine().SyncAsync(cancellationToken: Ct);

        var local = await LocalLines().GetByIdAsync(11, Ct);
        local!.Product = "widget-local";
        await LocalLines().UpdateAsync(local, cancellationToken: Ct);
        (await ServerLines().DeleteAsync(11, Ct)).Should().BeTrue();
        (await ServerOrders().DeleteAsync(1, Ct)).Should().BeTrue();

        var result = await Engine()
            .SyncAsync(new SyncOptions { ConflictPolicy = SyncConflictPolicy.LocalWins }, Ct);

        result
            .Conflicts.Should()
            .ContainSingle()
            .Which.Reason.Should()
            .Be(SyncConflictReason.ParentMissingOnServer);
        (await LocalOrders().GetByIdAsync(1, Ct)).Should().NotBeNull("残した明細の親も残る");
    }

    /// <summary>
    /// 実 SQL Server で、別端末が子を足した親のローカル削除は DeleteRefusedOnServer として報告され、
    /// その子のダウンロードは打ち切られる（同期は止まらない）。
    /// </summary>
    [Fact(
        DisplayName = "[Sync/SqlServer] 子が足された親の削除は実サーバーで DeleteRefusedOnServer と打ち切りになる"
    )]
    public async Task DeleteOfParentWithChildAddedOnServer_IsReportedAsRefused()
    {
        await ServerOrders()
            .InsertAsync(new SyncOrderEntity { OrderId = 1, CustomerName = "alice" }, Ct);
        await ServerOrders()
            .InsertAsync(new SyncOrderEntity { OrderId = 2, CustomerName = "bob" }, Ct);
        await Engine().SyncAsync(cancellationToken: Ct);

        (await LocalOrders().DeleteAsync(1, Ct)).Should().BeTrue();
        await ServerLines()
            .InsertAsync(
                new SyncOrderLineEntity
                {
                    LineId = 21,
                    OrderId = 1,
                    Product = "added elsewhere",
                },
                Ct
            );

        var result = await Engine().SyncAsync(cancellationToken: Ct);

        result
            .Conflicts.Should()
            .ContainSingle()
            .Which.Reason.Should()
            .Be(SyncConflictReason.DeleteRefusedOnServer);
        result
            .Truncations.Should()
            .Contain(truncation =>
                truncation.TableName == "sync_order_lines" && truncation.PendingKeyText == "21"
            );
        (await LocalLines().GetByIdAsync(21, Ct)).Should().BeNull();
    }

    /// <summary>サーバー側（SQL Server）のマーカーリポジトリ</summary>
    private ISyncMarkerRepository ServerMarkers() =>
        _provider.GetRequiredKeyedService<ISyncMarkerRepository>(ServerKey);

    /// <summary>ローカル側（SQLite）のマーカーリポジトリ＝ジャーナル記録デコレータが被さっている</summary>
    private ISyncMarkerRepository LocalMarkers() =>
        _provider.GetRequiredKeyedService<ISyncMarkerRepository>(LocalKey);

    /// <summary>
    /// サーバーに NULL の値の行があるとき、ローカルで作った 2 行目の NULL の行は、Collect・LocalWins では
    /// UniqueValueTakenOnServer として報告されて記録が残り、ServerWins では決着する（どのポリシー・モードでも止まらない）。
    /// </summary>
    /// <remarks>
    /// SQL Server の UNIQUE 制約は NULL を値として比べ、同じ組の 2 行目の NULL を断る（エラー 2627）。サーバーの事前チェック
    /// （QuickER 版 Repository の <c>CheckUniquenessAsync</c>）が NULL を照合から外していると、エンジンは断られた書き込みの
    /// 理由を見つけられずに元の例外を投げ直し、同期が毎回止まる。単一列の制約と、NULL を含む複合の制約の両方で確かめる。
    /// </remarks>
    [Theory(
        DisplayName = "[Sync/SqlServer] NULL の一意な値がサーバーで埋まっていると、どのポリシーとモードでも同期は止まらない"
    )]
    [InlineData(SyncConflictPolicy.Collect, SyncMode.Versioned, false)]
    [InlineData(SyncConflictPolicy.LocalWins, SyncMode.Versioned, false)]
    [InlineData(SyncConflictPolicy.ServerWins, SyncMode.Versioned, false)]
    [InlineData(SyncConflictPolicy.Collect, SyncMode.LastWriteWins, false)]
    [InlineData(SyncConflictPolicy.LocalWins, SyncMode.LastWriteWins, false)]
    [InlineData(SyncConflictPolicy.ServerWins, SyncMode.LastWriteWins, false)]
    [InlineData(SyncConflictPolicy.Collect, SyncMode.Versioned, true)]
    [InlineData(SyncConflictPolicy.ServerWins, SyncMode.LastWriteWins, true)]
    public async Task NullUniqueValueTakenOnServer_IsReportedOrSettled(
        SyncConflictPolicy policy,
        SyncMode mode,
        bool composite
    )
    {
        await ServerMarkers().InsertAsync(NewMarker(1, composite), Ct);
        var options = new SyncOptions { ConflictPolicy = policy, Mode = mode };
        await Engine().SyncAsync(options, Ct);
        await LocalMarkers().InsertAsync(NewMarker(2, composite), Ct);

        var result = await Engine().SyncAsync(options, Ct);

        if (policy == SyncConflictPolicy.ServerWins)
        {
            result.Conflicts.Should().BeEmpty();
            (await Journal().CountPendingAsync(Ct)).Should().Be(0);
            (await LocalMarkers().GetAllAsync(Ct))
                .Select(marker => marker.MarkerId)
                .Should()
                .Equal(1);
        }
        else
        {
            result
                .Conflicts.Should()
                .ContainSingle()
                .Which.Should()
                .Match<SyncConflict>(conflict =>
                    conflict.TableName == "sync_markers"
                    && conflict.KeyText == "2"
                    && conflict.Reason == SyncConflictReason.UniqueValueTakenOnServer
                );
            (await Journal().ReadAllAsync(Ct))
                .Should()
                .ContainSingle(entry => entry.KeyText == "2");
            (await LocalMarkers().GetByIdAsync(2, Ct)).Should().NotBeNull();
        }

        (await ServerMarkers().GetAllAsync(Ct)).Select(marker => marker.MarkerId).Should().Equal(1);
    }

    /// <summary>
    /// SQL Server の QuickER 版 Repository の事前チェックは、UNIQUE 制約と同じく NULL を値として比べる
    /// （単一列の NULL・複合の制約の NULL を含む組・全部が NULL の組は重複、NULL 以外の値が違えば重複ではない）。
    /// </summary>
    [Fact(
        DisplayName = "[Sync/SqlServer] SQL Server の CheckUniquenessAsync は NULL を値として比べる（単一列・複合）"
    )]
    public async Task ServerCheckUniqueness_ComparesNullsAsValues()
    {
        await ServerMarkers()
            .InsertAsync(
                new SyncMarkerEntity
                {
                    MarkerId = 1,
                    GroupName = null,
                    Slot = 1,
                },
                Ct
            );
        await ServerMarkers()
            .InsertAsync(
                new SyncMarkerEntity
                {
                    MarkerId = 2,
                    Code = "a",
                    GroupName = "g",
                },
                Ct
            );

        async Task<string[]> Check(SyncMarkerEntity marker) =>
            (await ServerMarkers().CheckUniquenessAsync(marker, Ct))
                .Select(violation => violation.ConstraintName)
                .ToArray();

        // 行 1 の code は NULL＝NULL の code は重複・(NULL, 1) も重複
        (
            await Check(
                new SyncMarkerEntity
                {
                    MarkerId = 9,
                    Code = null,
                    GroupName = "x",
                    Slot = 9,
                }
            )
        )
            .Should()
            .Equal("UQ_sync_markers_code");
        (
            await Check(
                new SyncMarkerEntity
                {
                    MarkerId = 9,
                    Code = "b",
                    GroupName = null,
                    Slot = 1,
                }
            )
        ).Should().Equal("UQ_sync_markers_group_name_slot");

        // 行 2 の (g, NULL)＝NULL を含む組でも NULL 以外の列が一致すれば重複・違えば重複ではない
        (
            await Check(
                new SyncMarkerEntity
                {
                    MarkerId = 9,
                    Code = "b",
                    GroupName = "g",
                    Slot = null,
                }
            )
        )
            .Should()
            .Equal("UQ_sync_markers_group_name_slot");
        (
            await Check(
                new SyncMarkerEntity
                {
                    MarkerId = 9,
                    Code = "b",
                    GroupName = null,
                    Slot = 2,
                }
            )
        ).Should().BeEmpty();

        // 自分自身は除外する（行 1 を同じ値で確かめても重複にならない）
        (await Check((await ServerMarkers().GetByIdAsync(1, Ct))!))
            .Should()
            .BeEmpty();
    }

    /// <summary>マーカーを組み立てる（composite なら複合の制約だけがぶつかる値、そうでなければ code だけが NULL でぶつかる値）</summary>
    private static SyncMarkerEntity NewMarker(int id, bool composite) =>
        composite
            ? new SyncMarkerEntity
            {
                MarkerId = id,
                Code = $"c{id}",
                GroupName = null,
                Slot = 1,
            }
            : new SyncMarkerEntity
            {
                MarkerId = id,
                Code = null,
                GroupName = $"g{id}",
                Slot = id,
            };
}
