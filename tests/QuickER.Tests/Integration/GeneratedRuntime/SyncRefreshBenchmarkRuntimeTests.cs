using System;
using System.Collections.Generic;
using System.Diagnostics;
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
/// 洗い替え（<c>RefreshAsync</c>）と通常の全量ダウンロード経路を、同一データセットに対して交互に計測する。
/// </summary>
/// <remarks>
/// <para>
/// 洗い替えは「通常の同期より速いこと」が存在理由なので、その主張を実測で支える場所を 1 つ置く。ただし
/// <b>時間はいっさい表明しない</b>: CI の負荷変動で赤くなるテストは、主張の裏付けではなく主張の妨げになる。
/// 表明するのは「どちらの経路を通ってもローカルの中身が完全に一致すること」で、所要時間は
/// <see cref="ITestOutputHelper"/> へ出す（比較したい人が実行して読む）。
/// </para>
/// <para>
/// 計測は 3 経路 × 3 回で、毎回まっさらなローカル DB を作って同じ終状態を作る:
/// </para>
/// <list type="bullet">
///   <item><b>SyncAsync</b>: 利用者が実際に叩く通常の同期（ジャーナル再生＋全量ダウンロード＋削除伝搬）</item>
///   <item><b>DownloadAsync</b>: そのうちダウンロードだけ（削除伝搬なし）＝洗い替えとの差を「行の適用方法」だけに絞った対照</item>
///   <item><b>RefreshAsync</b>: 洗い替え</item>
/// </list>
/// <para>
/// 行数は既定 4,000 行（注文 2,000＋明細 2,000）で、CI でも数秒に収まる規模にしてある。報告用の実測は
/// 環境変数 <c>QUICKER_SYNC_BENCH_ORDERS</c> で件数を上げて（例: 10000＝合計 2 万行）実行する。
/// </para>
/// <para>
/// バッチサイズは 3 経路とも同じ値（既定 500・<c>QUICKER_SYNC_BENCH_BATCH</c> で変更可）にする。洗い替えの
/// 既定は 2,000 だが、それをそのまま使うと「バッチ粒度の差」と「行の適用方法の差」が混ざるため、ここでは
/// 揃えて後者だけを見る（出荷時の既定どうしの比較は、両方の値で 1 回ずつ回して突き合わせる）。
/// </para>
/// </remarks>
[Trait("Category", "Integration")]
public sealed class SyncRefreshBenchmarkRuntimeTests(ITestOutputHelper output) : IAsyncLifetime
{
    /// <summary>テスト全体で使うキャンセルトークン</summary>
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    /// <summary>1 経路あたりの計測回数（中央値を採る）</summary>
    private const int Iterations = 3;

    /// <summary>サーバー役の一時ファイル SQLite DB</summary>
    private readonly SqliteTempDatabase _server = SqliteTempDatabase.Create();

    /// <summary>計測ごとに作るローカル DB（後始末のために保持する）</summary>
    private readonly List<SqliteTempDatabase> _locals = [];

    private ISqlExecutor _serverSql = null!;
    private ISyncServerSource<SyncOrderEntity, int> _orderSource = null!;
    private ISyncServerSource<SyncOrderLineEntity, int> _lineSource = null!;

    /// <summary>注文の件数（明細も同数＝合計はこの 2 倍）</summary>
    private static int OrderCount => Configured("QUICKER_SYNC_BENCH_ORDERS", 2_000);

    /// <summary>バッチサイズ（3 経路とも同じ値で回す＝バッチ粒度の差を比較へ持ち込まない）</summary>
    private static int BatchSize => Configured("QUICKER_SYNC_BENCH_BATCH", 500);

    /// <summary>環境変数の正の整数、無ければ既定値</summary>
    private static int Configured(string variable, int fallback) =>
        int.TryParse(Environment.GetEnvironmentVariable(variable), out var value) && value > 0
            ? value
            : fallback;

    /// <summary>サーバー役の DB を作り、版を採番済みの行で埋める</summary>
    public async ValueTask InitializeAsync()
    {
        await _server.ApplyDdlAsync(SyncFixtureDefinition.BuildSqliteMirror(), Ct);

        var serverFactory = new SqlConnectionFactory(_server.ReadWriteCreateConnectionString);
        _serverSql = new SqlExecutor(serverFactory);

        var orders = new SyncOrderRepository(serverFactory);
        var lines = new SyncOrderLineRepository(serverFactory);

        // 版はここで直接与える（サーバー役ラッパーの 1 行ずつの採番はシード時間を支配してしまうため）。
        // 実 SQL Server の rowversion と同じく「単調増加・昇順で並べられる」ことだけが要件。
        var version = 0L;
        await orders.BulkInsertAsync(
            Enumerable
                .Range(1, OrderCount)
                .Select(id => new SyncOrderEntity
                {
                    OrderId = id,
                    CustomerName = $"customer-{id:D6}",
                    RowVer = Version(++version),
                })
                .ToList(),
            Ct
        );
        await lines.BulkInsertAsync(
            Enumerable
                .Range(1, OrderCount)
                .Select(id => new SyncOrderLineEntity
                {
                    LineId = id,
                    OrderId = id,
                    Product = $"product-{id:D6}",
                    RowVer = Version(++version),
                })
                .ToList(),
            Ct
        );

        _orderSource = SyncTestServerSources.CreateOrders(_serverSql, orders);
        _lineSource = SyncTestServerSources.CreateLines(_serverSql, lines);
    }

    /// <summary>一時 DB をすべて破棄する</summary>
    public ValueTask DisposeAsync()
    {
        foreach (var local in _locals)
        {
            local.Dispose();
        }

        _server.Dispose();

        return ValueTask.CompletedTask;
    }

    /// <summary>8 バイト big-endian の版（バイト列の辞書順が数値順と一致する）</summary>
    private static byte[] Version(long value)
    {
        var bytes = BitConverter.GetBytes(value);

        if (BitConverter.IsLittleEndian)
        {
            Array.Reverse(bytes);
        }

        return bytes;
    }

    /// <summary>
    /// 洗い替えと通常のダウンロードは同じローカル内容を作る（表明はこの同値性だけ・時間は出力する）。
    /// </summary>
    [Fact(
        DisplayName = "[Sync] 洗い替えと全量ダウンロードは同じローカル内容を作る（所要時間は出力のみ）"
    )]
    public async Task Refresh_ProducesTheSameLocalContentAsAFullDownload()
    {
        var rows = OrderCount * 2;
        output.WriteLine(
            $"データセット: 注文 {OrderCount:N0} 件 ＋ 明細 {OrderCount:N0} 件 = {rows:N0} 行"
                + $"（バッチ {BatchSize:N0}）"
        );

        var syncTimes = new List<TimeSpan>();
        var downloadTimes = new List<TimeSpan>();
        var refreshTimes = new List<TimeSpan>();
        var digests = new List<string>();

        // 経路を交互に回して、実行順の温まり方が特定の経路へ偏らないようにする
        for (var iteration = 0; iteration < Iterations; iteration++)
        {
            var sync = await MeasureAsync(engine =>
                engine.SyncAsync(new SyncOptions { DownloadBatchSize = BatchSize }, Ct)
            );
            var download = await MeasureAsync(engine =>
                engine.DownloadAsync(
                    new SyncOptions { DownloadBatchSize = BatchSize, PropagateDeletes = false },
                    Ct
                )
            );
            var refresh = await MeasureAsync(engine =>
                engine.RefreshAsync(
                    new SyncRefreshOptions
                    {
                        BatchSize = BatchSize,
                        DiscardLocalUnboundedBinaries = true,
                    },
                    Ct
                )
            );

            syncTimes.Add(sync.Elapsed);
            downloadTimes.Add(download.Elapsed);
            refreshTimes.Add(refresh.Elapsed);
            digests.Add(sync.Digest);
            digests.Add(download.Digest);
            digests.Add(refresh.Digest);
        }

        var syncMedian = Median(syncTimes);
        var downloadMedian = Median(downloadTimes);
        var refreshMedian = Median(refreshTimes);

        output.WriteLine($"SyncAsync     中央値 {syncMedian.TotalMilliseconds, 9:F1} ms");
        output.WriteLine($"DownloadAsync 中央値 {downloadMedian.TotalMilliseconds, 9:F1} ms");
        output.WriteLine($"RefreshAsync  中央値 {refreshMedian.TotalMilliseconds, 9:F1} ms");
        output.WriteLine(
            $"倍率: SyncAsync/Refresh = {syncMedian / refreshMedian:F2}x, "
                + $"DownloadAsync/Refresh = {downloadMedian / refreshMedian:F2}x"
        );
        output.WriteLine(
            $"全計測: sync=[{Describe(syncTimes)}] download=[{Describe(downloadTimes)}] "
                + $"refresh=[{Describe(refreshTimes)}]"
        );

        // 表明するのはここだけ: どの経路を何回通っても、ローカルの中身は 1 バイトも変わらない
        digests.Should().HaveCount(Iterations * 3);
        digests
            .Distinct(StringComparer.Ordinal)
            .Should()
            .ContainSingle("経路が違っても取り込む内容は同じでなければならない");
    }

    /// <summary>まっさらなローカル DB を作り、渡された経路を 1 回だけ計測してローカルの内容を要約する</summary>
    private async Task<(TimeSpan Elapsed, string Digest)> MeasureAsync(Func<SyncEngine, Task> run)
    {
        var local = SqliteTempDatabase.Create();
        _locals.Add(local);
        await local.ApplyDdlAsync(SyncFixtureDefinition.BuildSqliteMirror(), Ct);

        var localFactory = new SqlConnectionFactory(local.ReadWriteCreateConnectionString);
        var localSql = new SqlExecutor(localFactory);
        var journal = new SyncJournal(localSql);
        await journal.EnsureCreatedAsync(Ct);

        // アプリが触るのと同じ形（ジャーナル記録デコレータ越し）で組む
        var orders = new JournalingSyncOrderRepository(
            new SyncOrderRepository(localFactory),
            journal
        );
        var lines = new JournalingSyncOrderLineRepository(
            new SyncOrderLineRepository(localFactory),
            journal
        );
        var engine = new SyncEngine(
            [
                new SyncOrderSyncTable(orders, localSql, _orderSource),
                new SyncOrderLineSyncTable(lines, localSql, _lineSource),
            ],
            journal
        );

        var stopwatch = Stopwatch.StartNew();
        await run(engine);
        stopwatch.Stop();

        return (stopwatch.Elapsed, await DigestAsync(orders, lines));
    }

    /// <summary>ローカルの全行（キー・内容・ミラー版）を 1 本の文字列へ畳む</summary>
    private static async Task<string> DigestAsync(
        ISyncOrderRepository orders,
        ISyncOrderLineRepository lines
    )
    {
        var orderRows = await orders.GetAllAsync(Ct);
        var lineRows = await lines.GetAllAsync(Ct);

        return string.Join(
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
    }

    /// <summary>中央値（計測回数は奇数なので真ん中の 1 つ）</summary>
    private static TimeSpan Median(IReadOnlyList<TimeSpan> samples) =>
        samples.OrderBy(sample => sample).ElementAt(samples.Count / 2);

    /// <summary>全計測値をミリ秒で並べる</summary>
    private static string Describe(IEnumerable<TimeSpan> samples) =>
        string.Join(", ", samples.Select(sample => $"{sample.TotalMilliseconds:F1}"));
}
