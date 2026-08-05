using System.IO;
using AwesomeAssertions;
using QuickER.Settings;

namespace QuickER.Tests.Settings;

/// <summary><see cref="AtomicFile"/> の原子的書き込み（新規作成・置換・失敗時の巻き戻り）を検証するテストクラス</summary>
/// <remarks>
/// 図ファイル・設定ファイル・接続プロファイルの 3 経路が共有する単一正本のため、
/// 「保存先を壊さない」「一時ファイルを残さない」「失敗を握り潰さない」をここで直接押さえる。
/// 各ストア側のテスト（JsonStorageServiceTests / GuiAppSettingsStoreTests /
/// SqlConnectionProfileStoreTests）は、経路ごとに委譲が繋がっていることの保証として併存させる。
/// </remarks>
public class AtomicFileTests : IDisposable
{
    /// <summary>テスト用の一時保存先フォルダ</summary>
    private readonly string _folder;

    /// <summary>一時保存先フォルダを作成する</summary>
    public AtomicFileTests()
    {
        _folder = Path.Combine(Path.GetTempPath(), "QuickERTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_folder);
    }

    /// <summary>一時保存先フォルダを削除する</summary>
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
            // テスト後の後始末はベストエフォートとする
        }
    }

    /// <summary>保存先が存在しない場合に新規作成し、一時ファイルを残さないことを検証する</summary>
    [Fact(DisplayName = "WriteAllText: 保存先が無ければ新規作成し .tmp を残さない")]
    public void WriteAllText_NewFile_CreatesFileWithoutTemporaryLeftover()
    {
        var path = Path.Combine(_folder, "new.json");

        // 非 ASCII を含めて、既定の文字コード（BOM なし UTF-8）で往復できることも併せて確認する
        AtomicFile.WriteAllText(path, "{ \"名前\": \"顧客\" }");

        File.Exists(path).Should().BeTrue();
        File.ReadAllText(path).Should().Be("{ \"名前\": \"顧客\" }");
        FindTemporaryLeftovers().Should().BeEmpty("一時ファイルは差し替え後に残らない");
    }

    /// <summary>既存ファイルを置換し、一時ファイルを残さないことを検証する</summary>
    [Fact(DisplayName = "WriteAllText: 既存ファイルを置換し .tmp を残さない")]
    public void WriteAllText_ExistingFile_ReplacesContentWithoutTemporaryLeftover()
    {
        var path = Path.Combine(_folder, "existing.json");
        File.WriteAllText(path, "old");

        AtomicFile.WriteAllText(path, "new");

        File.ReadAllText(path).Should().Be("new");
        FindTemporaryLeftovers().Should().BeEmpty("一時ファイルは差し替え後に残らない");
    }

    /// <summary>短い内容で上書きしても前の内容が末尾に残らない（切り詰めではなく差し替え）ことを検証する</summary>
    /// <remarks>
    /// 「一時ファイルへ書いて差し替える」実装では自明だが、うっかり保存先へ直接追記・部分上書きする
    /// 実装へ退行すると、旧内容の尻尾が残って壊れた JSON になる。その退行を検知する。
    /// </remarks>
    [Fact(DisplayName = "WriteAllText: 短い内容で上書きしても旧内容が残らない")]
    public void WriteAllText_ShorterContent_LeavesNoTrailingRemnant()
    {
        var path = Path.Combine(_folder, "shrink.json");
        File.WriteAllText(path, new string('x', 4096));

        AtomicFile.WriteAllText(path, "{}");

        File.ReadAllText(path).Should().Be("{}");
    }

    /// <summary>差し替えに失敗した場合、元ファイルを無傷のまま残し、一時ファイルを掃除して例外を伝えることを検証する</summary>
    /// <remarks>
    /// 保存先を <see cref="FileShare.None"/> で開いたままにして、置換（<see cref="File.Replace"/>）と
    /// そのフォールバック（<see cref="File.Move(string, string, bool)"/>）の双方を確実に失敗させる。
    /// 原子的書き込みの本命は「失敗しても保存先が書き込み前のまま」であることなので、ここが要。
    /// </remarks>
    [Fact(DisplayName = "WriteAllText: 差し替え失敗時は元ファイル無傷・.tmp なし・例外を伝播")]
    public void WriteAllText_ReplaceFailure_KeepsOriginalAndRemovesTemporary()
    {
        var path = Path.Combine(_folder, "locked.json");
        File.WriteAllText(path, "original");

        using (
            var lockedFile = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None)
        )
        {
            var act = () => AtomicFile.WriteAllText(path, "replacement");

            // File.Replace は IOException・フォールバックの File.Move は UnauthorizedAccessException を
            // 投げる（OS/ランタイム依存）ため、どちらの型でも「伝播していること」を検証する
            var thrown = act.Should().Throw<Exception>("差し替えの失敗を握り潰さない").Which;
            (thrown is IOException or UnauthorizedAccessException)
                .Should()
                .BeTrue($"IO 系の例外であること（実際: {thrown.GetType().Name}）");
            FindTemporaryLeftovers().Should().BeEmpty("失敗時に一時ファイルを残さない");
        }

        File.ReadAllText(path).Should().Be("original", "失敗しても保存先は書き込み前のまま");
    }

    /// <summary>保存先がディレクトリ等で書き込めない場合も一時ファイルを残さないことを検証する</summary>
    /// <remarks>
    /// 例外型は差し替え手段（<see cref="File.Replace"/> / <see cref="File.Move(string, string, bool)"/>）と
    /// OS 依存で <see cref="IOException"/> / <see cref="UnauthorizedAccessException"/> のどちらにもなる。
    /// ここで押さえたいのは「握り潰さず伝播し、一時ファイルを残さない」ことなので型は両方許容する。
    /// </remarks>
    [Fact(DisplayName = "WriteAllText: 保存先がディレクトリなら .tmp を残さず例外を投げる")]
    public void WriteAllText_PathIsDirectory_RemovesTemporaryAndThrows()
    {
        var path = Path.Combine(_folder, "as-directory");
        Directory.CreateDirectory(path);

        var act = () => AtomicFile.WriteAllText(path, "{}");

        var thrown = act.Should().Throw<Exception>("書き込みの失敗を握り潰さない").Which;
        (thrown is IOException or UnauthorizedAccessException)
            .Should()
            .BeTrue($"IO 系の例外であること（実際: {thrown.GetType().Name}）");
        FindTemporaryLeftovers().Should().BeEmpty("失敗時に一時ファイルを残さない");
    }

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
        var path = Path.Combine(_folder, "concurrent.json");

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

    /// <summary>一時ファイル名に固定名 <c>{path}.tmp</c> を使わない（GUID を挟む）ことを検証する</summary>
    /// <remarks>
    /// 固定名へ退行すると、同じファイルを同時保存した 2 プロセスが同じ一時ファイルを奪い合い
    /// 「書き途中の混線した内容を本体へ差し替える」破損へ昇格する。実際にこのドリフト（1 箇所だけ
    /// 固定名のまま取り残される）が起きた経緯があるため、固定名の一時ファイルをあらかじめ置いて
    /// 「他プロセスの一時ファイルを踏まない」ことを直接確かめる。
    /// </remarks>
    [Fact(DisplayName = "WriteAllText: 固定名 {path}.tmp を使わない")]
    public void WriteAllText_DoesNotUseFixedTemporaryName()
    {
        var path = Path.Combine(_folder, "guid.json");
        var fixedTemporaryPath = path + ".tmp";
        File.WriteAllText(fixedTemporaryPath, "他プロセスが使用中の一時ファイル");

        AtomicFile.WriteAllText(path, "{}");

        File.ReadAllText(path).Should().Be("{}");
        File.ReadAllText(fixedTemporaryPath)
            .Should()
            .Be("他プロセスが使用中の一時ファイル", "一時ファイル名には GUID が挟まる");
    }

    /// <summary>一時保存先フォルダに残った AtomicFile 自身の一時ファイル（<c>{path}.{GUID}.tmp</c>）を列挙する</summary>
    /// <remarks>
    /// 一時ファイル名には GUID が挟まるため、固定名ではなくワイルドカードで探す。
    /// Windows の ReplaceFile API は競合時に <c>{name}~RF{hex}.TMP</c> という OS 側の作業ファイルを
    /// 残すことがあり、これは AtomicFile の管理外（finally の掃除対象にできない）のため検出から除外する。
    /// </remarks>
    private string[] FindTemporaryLeftovers() =>
        Directory
            .GetFiles(_folder, "*.tmp")
            .Where(f => !Path.GetFileName(f).Contains("~RF", StringComparison.OrdinalIgnoreCase))
            .ToArray();
}
