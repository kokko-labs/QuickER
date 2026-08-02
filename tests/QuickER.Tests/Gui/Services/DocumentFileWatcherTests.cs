using System.Diagnostics;
using System.IO;
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
/// </remarks>
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

    private DocumentFileWatcher CreateWatcher(Func<string?> expectedHash, int debounceMs = 150)
    {
        var watcher = new DocumentFileWatcher(debounceMs) { ExpectedHashProvider = expectedHash };
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
    private static bool WaitUntil(Func<bool> predicate, int timeoutMs = 5000)
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

        WaitUntil(() => EventCount() >= 1).Should().BeTrue("内容変更が通知されるはず");

        var last = Snapshot()[^1];
        last.Kind.Should().Be(DocumentFileChangeKind.Modified);
        last.Path.Should().Be(path);
        last.ContentHash.Should().Be(newHash);
    }

    /// <summary>連続書き込みのバーストがデバウンスで合流し、最終ハッシュの通知に収束することを検証する</summary>
    [Fact(DisplayName = "デバウンス: 連続書き込みが 1〜2 件へ合流する")]
    public void RapidWrites_AreCoalescedByDebounce()
    {
        var path = Path.Combine(_folder, "doc.json");
        File.WriteAllText(path, "A");
        var expected = HashOf(path);

        using var watcher = CreateWatcher(() => expected, debounceMs: 250);
        watcher.Watch(path);

        // デバウンス幅より短い間隔で連続書き込みし、最終内容を D にする
        foreach (var content in new[] { "B", "CC", "DDD" })
        {
            File.WriteAllText(path, content);
            Thread.Sleep(30);
        }

        var finalHash = HashOf(path);

        WaitUntil(() => EventCount() >= 1).Should().BeTrue();

        // 合流後に追加が来ないことを見届ける（デバウンス幅の 2 倍ほど待つ）
        Thread.Sleep(600);

        var snapshot = Snapshot();
        snapshot
            .Count.Should()
            .BeLessThanOrEqualTo(
                2,
                "デバウンスにより 3 回の書き込み（本来 6 件超の FS 通知）が合流するはず"
            );
        snapshot[^1].ContentHash.Should().Be(finalHash, "最終内容のハッシュへ収束する");
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

    /// <summary>削除が Deleted 種別で通知されることを検証する</summary>
    [Fact(DisplayName = "削除検知: 削除を Deleted で通知する")]
    public void Deleted_IsNotified()
    {
        var path = Path.Combine(_folder, "doc.json");
        File.WriteAllText(path, "A");
        var expected = HashOf(path);

        using var watcher = CreateWatcher(() => expected);
        watcher.Watch(path);

        File.Delete(path);

        WaitUntil(() => Snapshot().Any(e => e.Kind == DocumentFileChangeKind.Deleted))
            .Should()
            .BeTrue("削除が Deleted 種別で通知されるはず");
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
        WaitUntil(() => EventCount() >= 1).Should().BeTrue("再開後は通常どおり検知する");
    }
}
