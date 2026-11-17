using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using AwesomeAssertions;
using QuickER.Services;

namespace QuickER.Tests.Gui.Services;

/// <summary>
/// 外部変更監視サービス <see cref="DocumentFileWatcher"/> の挙動を検証するテストクラス。
/// </summary>
/// <remarks>
/// 実 <see cref="FileSystemWatcher"/> と一時ファイルを使う。タイミング依存はポーリング＋タイムアウトで
/// 頑健化し、固定 <c>Sleep</c> による決め打ち待ちを避ける。デバウンス幅は短め（150ms）にして所要時間を抑える。
/// <para>
/// 通知を待つ上限（<see cref="WaitUntil"/> の既定）は 30 秒にする。監視器のコールバックもデバウンスのタイマも
/// スレッドプールで走るため、並列実行の負荷でスレッドが空かないと通知は数秒遅れて届く（全件実行の開始直後に
/// 走るときが最も遅く、5 秒を超え得る）。製品はこの遅れの上限を約束しておらず、待つのは「最終的に届くこと」なので、
/// 上限を伸ばしても時間がかかるのは本当に届かなかったときだけになる。
/// </para>
/// <para>
/// それでも並列の負荷では 30 秒を超えて待たされることがあるので、クラスは <see cref="TestSupport.ThreadPoolDeliveryTestCollection"/>
/// で単独に流す。上限を割ったときは、スレッドプールの待ち・監視器の状態・ファイルを読めるかを表明の理由に添える
/// （<see cref="AssertEventually"/>）。
/// </para>
/// </remarks>
[Collection(TestSupport.ThreadPoolDeliveryTestCollection.Name)]
public sealed class DocumentFileWatcherTests : IDisposable
{
    private readonly string _folder = Path.Combine(
        Path.GetTempPath(),
        "quicker-watch-" + Guid.NewGuid().ToString("N")
    );

    /// <summary>捕捉した通知（スレッドプール発火のためロックで保護する）</summary>
    private readonly List<DocumentFileChangedEventArgs> _events = new();
    private readonly object _eventsLock = new();

    public DocumentFileWatcherTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_folder))
            {
                Directory.Delete(_folder, recursive: true);
            }
        }
        catch
        {
            // 後始末失敗はテスト結果に影響させない
        }
    }

    private DocumentFileWatcher CreateWatcher(
        Func<string?> expectedHash,
        int debounceMs = 150,
        int retryInitialMs = 2000,
        int retryMaxMs = 30000
    )
    {
        var watcher = new DocumentFileWatcher(debounceMs, retryInitialMs, retryMaxMs)
        {
            ExpectedHashProvider = expectedHash,
        };
        watcher.FileChanged += (_, e) =>
        {
            lock (_eventsLock)
            {
                _events.Add(e);
            }
        };
        return watcher;
    }

    private int EventCount()
    {
        lock (_eventsLock)
        {
            return _events.Count;
        }
    }

    private List<DocumentFileChangedEventArgs> Snapshot()
    {
        lock (_eventsLock)
        {
            return _events.ToList();
        }
    }

    /// <summary>述語が真になるまで、または timeout まで短間隔でポーリングする</summary>
    private static bool WaitUntil(Func<bool> predicate, int timeoutMs = 30000)
    {
        var sw = Stopwatch.StartNew();

        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            if (predicate())
            {
                return true;
            }

            Thread.Sleep(20);
        }

        return predicate();
    }

    /// <summary>
    /// 述語が真になるまで待ち、上限を割ったら、届かなかった理由を見分ける材料を添えて失敗させる
    /// </summary>
    /// <remarks>
    /// 材料は上限を割ったときだけ組み立てる（通る回の時間を増やさない）。材料の取得に失敗しても、元の表明の失敗は残す。
    /// </remarks>
    private static void AssertEventually(
        Func<bool> predicate,
        string because,
        DocumentFileWatcher? watcher = null,
        string? path = null
    )
    {
        if (WaitUntil(predicate))
        {
            return;
        }

        var diagnostics = DescribeDelivery(watcher, path);
        predicate().Should().BeTrue("{0}（上限を割った時点の様子: {1}）", because, diagnostics);
    }

    /// <summary>
    /// 通知が届かなかった理由を見分ける材料（スレッドプールの待ち・監視器の状態・ファイルを読めるか）を 1 行にする
    /// </summary>
    /// <remarks>
    /// 監視の通知は、FS の通知の処理・デバウンスのタイマ・照合のどれもスレッドプールの空きを待つ。並列のテストの負荷で
    /// 仕事が待たされたのか、照合の読み取りが失敗したのか、監視が止まったのかを、1 回の失敗で見分けるために書く。
    /// どの材料も取得の失敗を文字列にして返し、例外を投げない。
    /// </remarks>
    private static string DescribeDelivery(DocumentFileWatcher? watcher, string? path)
    {
        var parts = new List<string>();
        AddDescription(
            parts,
            () =>
                $"スレッドプール: スレッド={ThreadPool.ThreadCount} 積まれた仕事={ThreadPool.PendingWorkItemCount}"
        );
        AddDescription(parts, () => $"仕事 1 つの待ち={MeasureThreadPoolLatency()}");
        if (watcher is not null)
        {
            AddDescription(
                parts,
                () =>
                    $"監視器={(watcher.CurrentWatcher is null ? "なし" : "あり")} 張り直しの予約={watcher.IsRetryScheduledForTests}"
            );
        }

        if (path is not null)
        {
            AddDescription(parts, () => DescribeReadability(path));
        }

        return string.Join(" / ", parts);
    }

    /// <summary>材料を 1 つ足す（取得に失敗したら失敗を書く）</summary>
    private static void AddDescription(List<string> parts, Func<string> describe)
    {
        try
        {
            parts.Add(describe());
        }
        catch (Exception ex)
        {
            parts.Add($"（材料の取得に失敗: {ex.GetType().Name}）");
        }
    }

    /// <summary>スレッドプールへ積んだ仕事 1 つが走り出すまでの時間（5 秒で打ち切る）</summary>
    private static string MeasureThreadPoolLatency()
    {
        using var started = new ManualResetEventSlim();
        var stopwatch = Stopwatch.StartNew();
        ThreadPool.QueueUserWorkItem(_ => started.Set());

        return started.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken)
            ? $"{stopwatch.ElapsedMilliseconds} ms"
            : "5 秒を超えた";
    }

    /// <summary>照合と同じ開き方でファイルを読めるか</summary>
    private static string DescribeReadability(string path)
    {
        if (!File.Exists(path))
        {
            return "ファイル=無い";
        }

        try
        {
            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite
            );
            return "ファイル=読める";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return $"ファイル=読めない（{ex.GetType().Name} 0x{ex.HResult:X8}）";
        }
    }

    /// <summary>ファイルを共有なしで開く（照合の読み取りが重なっている間は、少し待って開き直す）</summary>
    private static FileStream OpenExclusively(string path)
    {
        var stopwatch = Stopwatch.StartNew();

        while (true)
        {
            try
            {
                return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
            }
            catch (IOException) when (stopwatch.ElapsedMilliseconds < 5000)
            {
                Thread.Sleep(10);
            }
        }
    }

    /// <summary>連続書き込みの行で、照合をどう止めるか</summary>
    public enum ComparisonStall
    {
        /// <summary>止めない</summary>
        None,

        /// <summary>途中の内容を読んだ照合を、期待ハッシュの手前で止める</summary>
        Intermediate,

        /// <summary>途中の内容を読んだ照合を止め、最終内容の照合の読み取りを共有なしで開いて失敗させる</summary>
        IntermediateAndFinalUnreadable,
    }

    private static string HashOf(string path) =>
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    /// <summary>内容変更が Modified 種別＋新しいハッシュ付きで通知されることを検証する</summary>
    [Fact(DisplayName = "変更検知: 内容変更を Modified で通知する")]
    public void Modified_IsDetectedWithNewHash()
    {
        var path = Path.Combine(_folder, "doc.json");
        File.WriteAllText(path, "A");
        var expected = HashOf(path);

        using var watcher = CreateWatcher(() => expected);
        watcher.Watch(path);

        File.WriteAllText(path, "BB");
        var newHash = HashOf(path);

        AssertEventually(() => EventCount() >= 1, "内容変更が通知されるはず", watcher, path);

        var last = Snapshot()[^1];
        last.Kind.Should().Be(DocumentFileChangeKind.Modified);
        last.Path.Should().Be(path);
        last.ContentHash.Should().Be(newHash);
    }

    /// <summary>
    /// 照合（内容を読んで期待ハッシュと比べる）の 1 回目を、期待ハッシュを読む手前で止める
    /// （照合が内容を読んだ後に、全件の負荷でスレッドが横取りされた代役。止めないときは素通しする）
    /// </summary>
    /// <remarks>
    /// 期待ハッシュの供給は「内容を読む → 比べる → 通知する」の間で呼ばれるので、製品に差し込み口を足さずに
    /// 「読んだ後に遅れた照合」を作れる。解放を忘れても止めたままにしないよう、破棄で解放する。
    /// </remarks>
    private sealed class FirstComparisonStall(string expectedHash, bool enabled) : IDisposable
    {
        private readonly ManualResetEventSlim _entered = new();
        private readonly ManualResetEventSlim _release = new();
        private int _calls;

        /// <summary>期待ハッシュの供給（止めるときは 1 回目だけ、解放されるまで返らない）</summary>
        public string? Provide()
        {
            if (enabled && Interlocked.Increment(ref _calls) == 1)
            {
                _entered.Set();
                _release.Wait(TimeSpan.FromSeconds(30));
            }

            return expectedHash;
        }

        /// <summary>1 回目の照合が内容を読んで止まるまで待つ</summary>
        public bool WaitEntered() => _entered.Wait(TimeSpan.FromSeconds(30));

        /// <summary>止めた照合を続けさせる</summary>
        public void Release() => _release.Set();

        public void Dispose() => _release.Set();
    }

    /// <summary>
    /// 連続書き込みのバーストがデバウンスで合流し、最後の通知が最終内容のハッシュになることを検証する
    /// </summary>
    /// <remarks>
    /// <para>
    /// 届くことは固定の時間でなく通知そのものを待って確かめる。最終内容の通知が届いた時点で件数を数え（デバウンスが
    /// 効かなければ書き込みごとの通知がその時点までに届いている）、合流の後に通知が増えないことと最後の通知は、観測の窓の後に
    /// 番兵の内容を書いてその通知が届くまでに並んだ通知で見届ける（後から届く余分な通知は、番兵の通知より前に並ぶ）。
    /// </para>
    /// <para>
    /// 止める行は、途中の内容を読んだ照合を期待ハッシュの手前で止め、その間に最終内容を書いて最終内容の通知が届いてから
    /// 続けさせる。デバウンスの満了はスレッドプールで重なって走るので、捨てないと途中の内容の通知が最終内容の通知より
    /// 後に届く（全件の負荷でスレッドが横取りされると起きる）。
    /// </para>
    /// <para>
    /// 読めない行は、止める行に加えて、最終内容を書いた直後にファイルを共有なしで開いて持ち、最終内容の照合の読み取りを
    /// 失敗させてから閉じ、止めた照合を続けさせる（ほかのプロセスが保存の直後に共有なしで開く代役）。読めなかった照合は
    /// 決着に数えないので、先に読んだ照合の通知は捨てない。閉じても書き込みの通知は来ないので、最終内容の通知は届かない
    /// （最後の通知が最終内容であることは表明しない）。開いて持つ間に最終内容の照合が走らなかった回（負荷で遅れた回）は、
    /// 照合が閉じた後に最終内容を読んで通知するので、検出が弱まるだけで赤にはならない。
    /// </para>
    /// </remarks>
    [Theory(DisplayName = "デバウンス: 連続書き込みが 1〜2 件へ合流し、最後の通知は最終内容")]
    [InlineData(ComparisonStall.None)]
    [InlineData(ComparisonStall.Intermediate)]
    [InlineData(ComparisonStall.IntermediateAndFinalUnreadable)]
    public void RapidWrites_AreCoalescedByDebounce(ComparisonStall stallKind)
    {
        var path = Path.Combine(_folder, "doc.json");
        File.WriteAllText(path, "A");
        var expected = HashOf(path);

        using var stall = new FirstComparisonStall(expected, stallKind != ComparisonStall.None);
        using var watcher = CreateWatcher(stall.Provide, debounceMs: 250);
        watcher.Watch(path);

        // デバウンス幅より短い間隔で連続書き込みし、最終内容を DDD にする
        foreach (var content in new[] { "B", "CC" })
        {
            File.WriteAllText(path, content);
            Thread.Sleep(30);
        }

        if (stallKind != ComparisonStall.None)
        {
            // 途中の内容を読んだ照合が止まってから、最終内容を書く
            stall.WaitEntered().Should().BeTrue("途中の内容を読んだ照合が止まるはず");
        }

        File.WriteAllText(path, "DDD");
        var finalHash = HashOf(path);

        if (stallKind == ComparisonStall.IntermediateAndFinalUnreadable)
        {
            // 最終内容の照合（デバウンス幅＋5 回の読み取り）が走り終えるまで、共有なしで開いて持つ
            using (OpenExclusively(path))
            {
                Thread.Sleep(1500);
            }

            stall.Release();
        }
        else
        {
            AssertEventually(
                () => Snapshot().Any(e => e.ContentHash == finalHash),
                "最終内容のハッシュの通知が届くはず",
                watcher,
                path
            );
            EventCount()
                .Should()
                .BeLessThanOrEqualTo(
                    2,
                    "デバウンスにより 3 回の書き込み（本来 6 件超の FS 通知）が合流するはず"
                );
            stall.Release();
        }

        // 合流の後に通知が増えないことを見届ける。増えないことは待たないと示せないので、デバウンス幅の 2 倍の観測の
        // 窓を置いてから番兵を書き、番兵の通知が届くまでに並んだ件数で数える（負荷で遅れた余分な通知も番兵の前に並ぶ。
        // 窓が負荷で短く見えても検出が弱まるだけで、届いた通知を取りこぼして赤になることはない）
        Thread.Sleep(500);
        File.WriteAllText(path, "SENTINEL");
        var sentinelHash = HashOf(path);

        AssertEventually(
            () => Snapshot().Any(e => e.ContentHash == sentinelHash),
            "番兵の内容の通知が届くはず",
            watcher,
            path
        );
        var burst = Snapshot().TakeWhile(e => e.ContentHash != sentinelHash).ToList();
        burst
            .Should()
            .HaveCountLessThanOrEqualTo(
                2,
                "最終内容の通知の後に、連続書き込みの分の通知が増えない"
            );

        if (stallKind == ComparisonStall.IntermediateAndFinalUnreadable)
        {
            burst
                .Should()
                .NotBeEmpty(
                    "読めなかった照合は決着に数えないので、先に内容を読んだ照合の通知は捨てない"
                );
            return;
        }

        burst[^1]
            .ContentHash.Should()
            .Be(
                finalHash,
                "最後の通知は最終内容（途中の内容を読んだ照合が遅れても、その通知は捨てる）"
            );
    }

    /// <summary>最終既知ハッシュと同一内容の書き込みは通知されない（自己書き込み抑制）ことを検証する</summary>
    [Fact(DisplayName = "同一ハッシュ抑制: 内容が同じ書き込みは通知しない")]
    public void SameHash_IsSuppressed()
    {
        var path = Path.Combine(_folder, "doc.json");
        File.WriteAllText(path, "SAME");
        var expected = HashOf(path);

        using var watcher = CreateWatcher(() => expected);
        watcher.Watch(path);

        // 同一内容を再書き込み（タイムスタンプは変わるが内容ハッシュは不変）
        File.WriteAllText(path, "SAME");

        // デバウンス幅を十分に超えて待っても通知は 0 件
        Thread.Sleep(500);
        EventCount().Should().Be(0, "内容が同一なら自己書き込みとして抑制される");
    }

    /// <summary>
    /// 削除が Deleted 種別で通知され、削除より前に内容を読んだ照合の通知が削除の後に届かないことを検証する
    /// </summary>
    /// <remarks>
    /// 止める行は、書き込みの内容を読んだ照合を期待ハッシュの手前で止め、その間にファイルを消して削除の通知が届いてから
    /// 続けさせる。削除の後に届くことを見届けるため、同じ名前で番兵の内容を作り直し、その通知が届くまでに並んだ通知を見る。
    /// </remarks>
    [Theory(
        DisplayName = "削除検知: 削除を Deleted で通知し、削除の前に読んだ内容を後から通知しない"
    )]
    [InlineData(false)]
    [InlineData(true)]
    public void Deleted_IsNotified(bool stallComparisonBeforeDelete)
    {
        var path = Path.Combine(_folder, "doc.json");
        File.WriteAllText(path, "A");
        var expected = HashOf(path);

        using var stall = new FirstComparisonStall(expected, stallComparisonBeforeDelete);
        using var watcher = CreateWatcher(stall.Provide);
        watcher.Watch(path);

        if (stallComparisonBeforeDelete)
        {
            File.WriteAllText(path, "B");
            stall.WaitEntered().Should().BeTrue("書き込みの内容を読んだ照合が止まるはず");
        }

        File.Delete(path);

        AssertEventually(
            () => Snapshot().Any(e => e.Kind == DocumentFileChangeKind.Deleted),
            "削除が Deleted 種別で通知されるはず",
            watcher,
            path
        );

        if (!stallComparisonBeforeDelete)
        {
            return;
        }

        stall.Release();
        File.WriteAllText(path, "SENTINEL");
        var sentinelHash = HashOf(path);

        AssertEventually(
            () => Snapshot().Any(e => e.ContentHash == sentinelHash),
            "作り直した番兵の内容の通知が届くはず",
            watcher,
            path
        );
        Snapshot()
            .SkipWhile(e => e.Kind != DocumentFileChangeKind.Deleted)
            .Skip(1)
            .TakeWhile(e => e.ContentHash != sentinelHash)
            .Should()
            .BeEmpty("削除の後に、削除より前に読んだ内容の通知が届かない");
    }

    /// <summary>一時停止中は自己書き込みが通知されず、再開後は通常どおり検知することを検証する</summary>
    [Fact(DisplayName = "一時停止: 停止中の書き込みは通知しない")]
    public void Suspend_SuppressesEvents()
    {
        var path = Path.Combine(_folder, "doc.json");
        File.WriteAllText(path, "A");
        var expected = HashOf(path);

        using var watcher = CreateWatcher(() => expected);
        watcher.Watch(path);

        watcher.Suspend();
        File.WriteAllText(path, "written-while-suspended");
        Thread.Sleep(400);
        EventCount().Should().Be(0, "一時停止中の書き込みは通知しない");

        // 再開後は期待ハッシュと異なる書き込みを検知する
        watcher.Resume();
        File.WriteAllText(path, "written-after-resume");
        AssertEventually(() => EventCount() >= 1, "再開後は通常どおり検知する", watcher, path);
    }

    /// <summary>
    /// 内部の <see cref="FileSystemWatcher"/> へ Error イベントを発生させる
    /// （内部バッファ溢れ等で OS 側の監視が落ちたときの実挙動の代役）。
    /// </summary>
    /// <remarks>
    /// <see cref="FileSystemWatcher"/> は Error を外部から発生させる公開 API を持たないため、
    /// protected な <c>OnError</c> をリフレクションで呼ぶ。これにより「Error を購読しているか」まで
    /// 含めて実経路で検証できる（復帰メソッドを直接呼ぶ形では購読漏れを検知できない）。
    /// </remarks>
    private static void RaiseInternalError(FileSystemWatcher inner)
    {
        var onError = typeof(FileSystemWatcher).GetMethod(
            "OnError",
            BindingFlags.NonPublic | BindingFlags.Instance
        );

        onError.Should().NotBeNull("FileSystemWatcher.OnError は protected メソッドとして存在する");
        onError!.Invoke(
            inner,
            new object[] { new ErrorEventArgs(new IOException("simulated buffer overflow")) }
        );
    }

    /// <summary>Error 後に監視を張り直し、停止していた間に起きた変更を拾うことを検証する</summary>
    [Fact(DisplayName = "Error 復帰: 停止中に起きた変更を張り直し直後に拾う")]
    public void WatcherError_RestartsAndDetectsChangeMadeWhileStopped()
    {
        var path = Path.Combine(_folder, "doc.json");
        File.WriteAllText(path, "A");
        var expected = HashOf(path);

        using var watcher = CreateWatcher(() => expected);
        watcher.Watch(path);

        var inner = watcher.CurrentWatcher;
        inner.Should().NotBeNull("監視開始後は内部監視器が存在する");

        // バッファ溢れ後の実挙動（以後の通知が届かない）を再現してから Error を発生させる
        inner!.EnableRaisingEvents = false;
        File.WriteAllText(path, "changed-while-stopped");
        var changedHash = HashOf(path);

        RaiseInternalError(inner);

        AssertEventually(
            () => EventCount() >= 1,
            "張り直し直後のハッシュ比較で、停止中に起きた変更を拾うはず",
            watcher,
            path
        );
        Snapshot()[^1].ContentHash.Should().Be(changedHash);
    }

    /// <summary>Error 後に監視器が作り直され、以後の外部変更も通常どおり検知することを検証する</summary>
    [Fact(DisplayName = "Error 復帰: 張り直し後も通常の変更検知が続く")]
    public void WatcherError_ResumesNormalDetectionAfterRestart()
    {
        var path = Path.Combine(_folder, "doc.json");
        File.WriteAllText(path, "A");
        var expected = HashOf(path);

        using var watcher = CreateWatcher(() => expected);
        watcher.Watch(path);

        var inner = watcher.CurrentWatcher;
        inner.Should().NotBeNull();

        // 内容は最終既知ハッシュのまま Error を起こす（張り直し直後の比較では通知が出ない状況）
        RaiseInternalError(inner!);

        AssertEventually(
            () => !ReferenceEquals(watcher.CurrentWatcher, inner),
            "Error 後は監視器が作り直されるはず",
            watcher,
            path
        );
        watcher.CurrentWatcher.Should().NotBeNull("張り直しに成功していること");
        EventCount().Should().Be(0, "内容が変わっていなければ張り直し直後の比較でも通知しない");

        // 張り直した監視器で以後の外部変更を検知する
        File.WriteAllText(path, "written-after-restart");
        AssertEventually(() => EventCount() >= 1, "張り直し後も通常どおり検知する", watcher, path);
    }

    private int CountOf(DocumentFileChangeKind kind) => Snapshot().Count(e => e.Kind == kind);

    /// <summary>
    /// 張り直しに失敗してもパスを保持して試み続け、フォルダが戻ったら監視を再開し、止まっていた間の変更を拾う。
    /// 中断・再開の通知は 1 回ずつで、再試行のたびには通知しない
    /// </summary>
    [Fact(
        DisplayName = "張り直しの失敗: フォルダが戻るまで試み続け、再開後に止まっていた間の変更を拾う"
    )]
    public void RestartFailure_RetriesUntilFolderReturns_AndPicksUpChange()
    {
        var subFolder = Path.Combine(_folder, "share");
        Directory.CreateDirectory(subFolder);
        var path = Path.Combine(subFolder, "doc.json");
        File.WriteAllText(path, "A");
        var expected = HashOf(path);

        using var watcher = CreateWatcher(() => expected, retryInitialMs: 50, retryMaxMs: 100);
        watcher.Watch(path);
        var inner = watcher.CurrentWatcher;
        inner.Should().NotBeNull();

        // ネットワーク共有・USB の切断を模す（監視器を止めてからフォルダごと消す）
        inner!.EnableRaisingEvents = false;
        Directory.Delete(subFolder, recursive: true);

        watcher.RestartAfterError();

        watcher.CurrentWatcher.Should().BeNull("フォルダが無いので張れない");
        watcher.IsRetryScheduledForTests.Should().BeTrue("パスを保持して張り直しを試み続ける");
        CountOf(DocumentFileChangeKind.WatchInterrupted).Should().Be(1);

        // 何回か再試行が空振りしても、中断の通知は増えない
        Thread.Sleep(400);
        CountOf(DocumentFileChangeKind.WatchInterrupted)
            .Should()
            .Be(1, "再試行のたびには通知しない");

        // フォルダが戻り、止まっていた間に内容も変わった
        Directory.CreateDirectory(subFolder);
        File.WriteAllText(path, "changed-while-unreachable");
        var changedHash = HashOf(path);

        AssertEventually(
            () => CountOf(DocumentFileChangeKind.Modified) >= 1,
            "再開直後の照合で止まっていた間の変更を拾う",
            watcher,
            path
        );
        watcher.CurrentWatcher.Should().NotBeNull("張り直しに成功している");
        watcher.IsRetryScheduledForTests.Should().BeFalse();
        CountOf(DocumentFileChangeKind.WatchResumed).Should().Be(1);
        CountOf(DocumentFileChangeKind.WatchInterrupted).Should().Be(1);
        Snapshot()
            .Last(e => e.Kind == DocumentFileChangeKind.Modified)
            .ContentHash.Should()
            .Be(changedHash);
    }

    /// <summary>監視開始の時点でフォルダが無くても中断を 1 回通知して試み続け、Stop で張り直しをやめる</summary>
    [Fact(DisplayName = "監視開始の失敗: 中断を通知して試み続け、Stop で張り直しをやめる")]
    public void InitialWatchFailure_RetriesUntilStopped()
    {
        var subFolder = Path.Combine(_folder, "usb");
        var path = Path.Combine(subFolder, "doc.json");

        using var watcher = CreateWatcher(() => null, retryInitialMs: 50, retryMaxMs: 100);
        watcher.Watch(path);

        watcher.CurrentWatcher.Should().BeNull();
        watcher.IsRetryScheduledForTests.Should().BeTrue();
        CountOf(DocumentFileChangeKind.WatchInterrupted).Should().Be(1);
        Snapshot()[0].Path.Should().Be(path);

        watcher.Stop();
        watcher.IsRetryScheduledForTests.Should().BeFalse("Stop で張り直しの予約も止める");

        // 止めた後にフォルダが現れても、張り直さない
        Directory.CreateDirectory(subFolder);
        Thread.Sleep(300);
        watcher.CurrentWatcher.Should().BeNull();
        CountOf(DocumentFileChangeKind.WatchResumed).Should().Be(0);
    }
}
