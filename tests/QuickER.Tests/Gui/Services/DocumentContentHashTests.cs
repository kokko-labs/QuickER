using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using AwesomeAssertions;
using QuickER.Documents;
using QuickER.Platform;
using QuickER.Services;

namespace QuickER.Tests.Gui.Services;

/// <summary>
/// ファイル内容ハッシュ算出ヘルパ <see cref="DocumentContentHash"/> の挙動を検証するテストクラス。
/// </summary>
/// <remarks>実ファイル（一時ディレクトリ）を使い、書き込み途中の共有違反に対するリトライ挙動も検証する。</remarks>
public sealed class DocumentContentHashTests : IDisposable
{
    private readonly string _folder = Path.Combine(
        Path.GetTempPath(),
        "quicker-hash-" + Guid.NewGuid().ToString("N")
    );

    public DocumentContentHashTests() => Directory.CreateDirectory(_folder);

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

    /// <summary>算出したハッシュが標準の SHA-256（16 進）と一致することを検証する</summary>
    [Fact(DisplayName = "TryCompute: 内容の SHA-256（16 進）を返す")]
    public void TryCompute_ReturnsSha256Hex()
    {
        var path = Path.Combine(_folder, "a.txt");
        File.WriteAllText(path, "hello");

        var expected = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

        DocumentContentHash.TryCompute(path).Should().Be(expected);
    }

    /// <summary>存在しないファイルは null を返すことを検証する</summary>
    [Fact(DisplayName = "TryCompute: 不在ファイルは null")]
    public void TryCompute_MissingFile_ReturnsNull()
    {
        DocumentContentHash.TryCompute(Path.Combine(_folder, "nope.txt")).Should().BeNull();
    }

    /// <summary>不在ファイルではリトライ版も即座に null を返すことを検証する</summary>
    [Fact(DisplayName = "TryComputeWithRetry: 不在ファイルは即 null")]
    public void TryComputeWithRetry_MissingFile_ReturnsNull()
    {
        DocumentContentHash
            .TryComputeWithRetry(Path.Combine(_folder, "nope.txt"))
            .Should()
            .BeNull();
    }

    /// <summary>排他ロック中でもリトライで最終的にハッシュを算出できることを検証する（書き込み途中対策）</summary>
    [Fact(DisplayName = "TryComputeWithRetry: 排他ロック解放後にリトライで算出する")]
    public void TryComputeWithRetry_RetriesPastExclusiveLock()
    {
        var path = Path.Combine(_folder, "locked.txt");
        File.WriteAllText(path, "payload-under-write");
        var expected = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

        // 別スレッドで一時的に排他ロック（FileShare.None）して書き込み途中を模す
        var locker = new Thread(() =>
        {
            using var exclusive = new FileStream(
                path,
                FileMode.Open,
                FileAccess.ReadWrite,
                FileShare.None
            );
            Thread.Sleep(150);
        });
        locker.Start();

        // ロックが確実に握られてからリトライ算出を開始する（ロック中スタート→解放待ちを検証）
        Thread.Sleep(30);
        var hash = DocumentContentHash.TryComputeWithRetry(
            path,
            attempts: 20,
            delayMilliseconds: 30
        );

        locker.Join();
        hash.Should().Be(expected, "排他ロック解放後にリトライで正しく算出される");
    }

    /// <summary>書き出した内容から求めたハッシュが、実ファイルのハッシュと一致することを検証する</summary>
    /// <remarks>
    /// 一致しなければ、保存直後の最終既知ハッシュが常に「外部変更あり」に見えて再読込が暴発する。
    /// 符号化（BOM なし UTF-8）が <see cref="AtomicFile.WriteAllText(string, string)"/> と揃っていることの固定。
    /// </remarks>
    [Fact(DisplayName = "書き出した内容のハッシュはファイル内容のハッシュと一致する")]
    public void ComputeForText_MatchesFileHash()
    {
        var path = Path.Combine(_folder, "Text.json");
        const string contents = "{\n  \"Version\": 1,\n  \"日本語\": \"ü\"\n}";

        AtomicFile.WriteAllText(path, contents);

        DocumentContentHash
            .ComputeForText(contents)
            .Should()
            .Be(DocumentContentHash.TryCompute(path));
    }

    /// <summary>読み込んだバイト列から求めたハッシュが、実ファイルのハッシュと一致することを検証する（BOM 付き）</summary>
    /// <remarks>
    /// 復号済みの文字列から採ると BOM が剥がれて一致せず、開くたびに再読込が暴発する。読み取りに成功すれば、
    /// 図としての検証の成否に依らずバイト列を返すことも固定する。
    /// </remarks>
    [Fact(DisplayName = "読み込んだバイト列のハッシュはファイル内容のハッシュと一致する")]
    public void ComputeForBytes_MatchesFileHash()
    {
        var path = Path.Combine(_folder, "Bytes.json");
        File.WriteAllText(path, "{ \"日本語\": \"ü\" }", new UTF8Encoding(true));

        JsonStorageService.TryLoad(path, out _, out _, out _, out var rawContents);

        rawContents
            .Should()
            .Equal(File.ReadAllBytes(path), "読み取り失敗以外は検証の成否に依らず返す");
        DocumentContentHash
            .ComputeForBytes(rawContents)
            .Should()
            .Be(DocumentContentHash.TryCompute(path));
    }
}
