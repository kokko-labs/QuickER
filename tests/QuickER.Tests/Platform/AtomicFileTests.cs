using System.IO;
using AwesomeAssertions;
using QuickER.Platform;
using QuickER.Tests.TestSupport;

namespace QuickER.Tests.Platform;

/// <summary><see cref="AtomicFile"/> の並行・時間・ハンドルの振る舞い（同時保存・読み取りの粘り・ファイルを掴み続けないこと）を検証するテストクラス</summary>
/// <remarks>
/// 書く形 × 保存先 × 失敗の段・読み取りの分類の行は <c>Platform/OsBoundary/AtomicFileRows*.cs</c>（<c>AtomicFileTableTests</c>）へ移した。
/// ここに残すのは並行・時間・ハンドルの OS の振る舞い。
/// </remarks>
public class AtomicFileTests : IDisposable
{
    /// <summary>テスト用の一時保存先フォルダ（破棄で中身ごと消す）</summary>
    private readonly TestTempFolder _temp = new();

    /// <summary>一時保存先フォルダを消す</summary>
    public void Dispose() => _temp.Dispose();

    /// <summary>2 スレッドの同時保存でも保存先が「誰かの完全な 1 回分」にしかならないことを検証する</summary>
    /// <remarks>
    /// <para>
    /// 主張の中心は<b>破損なし</b>（＝途中まで書かれた内容・混線した内容が保存先に現れない）。
    /// 並行保存の調停はしない設計（後勝ち）なので、衝突による保存の失敗そのものは <b>0 件を要求しない</b>。
    /// </para>
    /// <para>
    /// 閾値は「差し替えリトライが機能していれば実測 0 件・リトライなしでは 3 割超が失敗する」という
    /// 実測を踏まえ、全試行の 1/4 未満とした（フレークを避けつつ、リトライを外した退行は検知できる幅）。
    /// 試行回数は 2 スレッド × 50 回＝100 回で、実行時間は 1 秒級に収まる。
    /// </para>
    /// </remarks>
    [Fact(DisplayName = "WriteAllText: 同時保存でも保存先は完全な 1 回分（破損なし）")]
    public void WriteAllText_ConcurrentWriters_NeverLeavesTornContent()
    {
        const int writerCount = 2;
        const int iterationsPerWriter = 50;
        var path = Path.Combine(_temp.Path, "concurrent.json");

        var failureCount = 0;
        var tornSamples = new List<string>();
        var tornLock = new object();

        var threads = Enumerable
            .Range(0, writerCount)
            .Select(writer => new Thread(() =>
            {
                for (var iteration = 0; iteration < iterationsPerWriter; iteration++)
                {
                    try
                    {
                        AtomicFile.WriteAllText(path, Payload(writer, iteration));
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        // 後勝ちの調停はしない設計のため、まれな衝突失敗は許容して件数だけ数える
                        Interlocked.Increment(ref failureCount);
                    }

                    // 書き込み直後に見える内容は、常に「誰かの完全な 1 回分」でなければならない
                    var observed = TryReadAllText(path);

                    if (observed is not null && !IsCompletePayload(observed))
                    {
                        lock (tornLock)
                        {
                            tornSamples.Add(observed);
                        }
                    }
                }
            }))
            .ToList();

        foreach (var thread in threads)
        {
            thread.Start();
        }

        foreach (var thread in threads)
        {
            thread.Join();
        }

        tornSamples.Should().BeEmpty("同時保存でも書き途中・混線した内容は保存先に現れない");
        IsCompletePayload(File.ReadAllText(path))
            .Should()
            .BeTrue("最終内容は完全な 1 回分（後勝ちのいずれか）");
        FindTemporaryLeftovers().Should().BeEmpty("同時保存でも一時ファイルを残さない");

        // 失敗 0 件は要求しない（調停はしていない）。リトライが効いていれば十分小さい件数に収まる
        failureCount
            .Should()
            .BeLessThan(
                writerCount * iterationsPerWriter / 4,
                "差し替えリトライにより衝突失敗はごく少数に収まる"
            );
    }

    /// <summary>同時保存テスト用の書き込み内容（先頭・末尾の目印と固定長で「完全な 1 回分」かを判定できる）</summary>
    /// <remarks>書き手・回数は固定桁で埋め、どの回の内容も同じ長さになるようにする（長さで切り詰めを検知する）</remarks>
    private static string Payload(int writer, int iteration) =>
        "BEGIN|" + writer.ToString("D2") + "|" + iteration.ToString("D4") + "|" + Filler + "|END";

    /// <summary>書き込み内容の詰め物（1 回の書き込みが複数ブロックにまたがる程度の大きさにする）</summary>
    private static readonly string Filler = new('x', 1024);

    /// <summary>読み出した内容が書き途中でない（先頭・末尾の目印と長さが揃っている）ことを判定する</summary>
    private static bool IsCompletePayload(string content) =>
        content.StartsWith("BEGIN|", StringComparison.Ordinal)
        && content.EndsWith("|END", StringComparison.Ordinal)
        && content.Length == Payload(0, 0).Length;

    /// <summary>
    /// 書き込みを邪魔しない共有指定で読み出す（競合で読めなければ null を返す）。
    /// </summary>
    /// <remarks>
    /// 既定の <see cref="File.ReadAllText(string)"/> は削除・改名を禁じる共有指定で開くため、
    /// 読み取りが差し替えを失敗させて「テストが測りたい失敗率」を歪める。
    /// <see cref="FileShare.ReadWrite"/> ＋ <see cref="FileShare.Delete"/> で開き、
    /// 差し替えと同時に読んでも互いを妨げないようにする。
    /// </remarks>
    private static string? TryReadAllText(string path)
    {
        try
        {
            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete
            );
            using var reader = new StreamReader(stream);

            return reader.ReadToEnd();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>読み取りが差し替えと同じ回数・同じバックオフで粘ることを検証する</summary>
    /// <remarks>
    /// <para>
    /// リトライを外すと、別プロセスの保存と重なった一瞬の失敗がそのまま
    /// <see cref="FileReadStatus.Unavailable"/> になり、保存が理由もなく止まる（＝過剰に厳しい）。
    /// 逆に回数・待ち時間だけを片方で変えると「書けたのに読めない」非対称が静かに生まれる。
    /// </para>
    /// <para>
    /// 判定は時間で行う（合計待ち時間 10+20+30+40＝100ms）。粘らない実装では即座に戻るため、
    /// フレークを避けつつ退行を検知できる下限として 60ms 以上を要求する。
    /// </para>
    /// </remarks>
    [Fact(DisplayName = "TryReadAllText: 読めないときは差し替えと同じだけ粘ってから諦める")]
    public void TryReadAllText_LockedFile_RetriesWithSameBackoffAsReplace()
    {
        var path = Path.Combine(_temp.Path, "retry-read.json");
        File.WriteAllText(path, "{}");

        using var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);

        var readWatch = System.Diagnostics.Stopwatch.StartNew();
        AtomicFile.TryReadAllText(path, out _).Should().Be(FileReadStatus.Unavailable);
        readWatch.Stop();

        readWatch
            .ElapsedMilliseconds.Should()
            .BeGreaterThanOrEqualTo(60, "リトライのバックオフを経てから諦める");
    }

    /// <summary>読み取りの直後に書き込みが成功する（読み取りがファイルを掴み続けない）ことを検証する</summary>
    [Fact(DisplayName = "TryReadAllText: 読み取り後もファイルを掴み続けない")]
    public void TryReadAllText_DoesNotKeepFileHandleOpen()
    {
        var path = Path.Combine(_temp.Path, "handle.json");
        AtomicFile.WriteAllText(path, "before");

        AtomicFile.TryReadAllText(path, out _).Should().Be(FileReadStatus.Loaded);
        AtomicFile.WriteAllText(path, "after");

        File.ReadAllText(path).Should().Be("after");
    }

    /// <summary>一時保存先フォルダに残った AtomicFile 自身の一時ファイル（<c>{path}.{GUID}.tmp</c>）を列挙する</summary>
    /// <remarks>
    /// 一時ファイル名には GUID が挟まるため、固定名ではなくワイルドカードで探す。
    /// Windows の ReplaceFile API は競合時に <c>{name}~RF{hex}.TMP</c> という OS 側の作業ファイルを
    /// 残すことがあり、これは AtomicFile の管理外（finally の掃除対象にできない）のため検出から除外する。
    /// </remarks>
    private string[] FindTemporaryLeftovers() =>
        Directory
            .GetFiles(_temp.Path, "*.tmp")
            .Where(f => !Path.GetFileName(f).Contains("~RF", StringComparison.OrdinalIgnoreCase))
            .ToArray();
}
