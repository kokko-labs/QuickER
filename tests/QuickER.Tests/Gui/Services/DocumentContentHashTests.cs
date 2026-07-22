using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;
using FluentAssertions;
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
}
