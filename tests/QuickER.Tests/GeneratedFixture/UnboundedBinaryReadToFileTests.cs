using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using QuickER.Platform;
using Xunit;
using RuntimeBinaryColumns = QuickER.Runtime.UnboundedBinaryColumns;

namespace QuickER.Tests.GeneratedFixture;

/// <summary>
/// 生成ランタイムの <c>UnboundedBinaryColumns.ReadToFileAsync</c>（生成 <c>Read{列}ToFileAsync</c> の本体）が、
/// 読み取りが値を書いたときだけ宛先を置き換え、行なし・NULL・例外・キャンセルでは既存のファイルに触れず一時ファイルも残さないことを固定する単体テスト。
/// </summary>
/// <remarks>
/// 宛先を直接開くと、行が見つかる前にファイルが切り詰められ、行なしでも空のファイルが残っていた。
/// 生成コードは同じテンプレートから出るので、ランタイムパッケージの型で確かめる。
/// </remarks>
public sealed class UnboundedBinaryReadToFileTests : IDisposable
{
    /// <summary>テストごとの作業フォルダ</summary>
    private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory(
        "quicker-read-to-file-"
    );

    /// <summary>宛先のパス（既存の内容 "old"）</summary>
    private string Destination => Path.Combine(_directory.FullName, "out.bin");

    /// <summary>作業フォルダを用意し、宛先へ既存の内容を書く</summary>
    public UnboundedBinaryReadToFileTests()
    {
        File.WriteAllBytes(Destination, "old"u8.ToArray());
    }

    /// <summary>作業フォルダを片づける</summary>
    public void Dispose() => _directory.Delete(recursive: true);

    /// <summary>作業フォルダに宛先以外のファイル（一時ファイルの残り）が無いことを確かめる</summary>
    private void ShouldLeaveOnlyDestination() =>
        _directory.GetFiles().Select(file => file.Name).Should().Equal("out.bin");

    [Fact(DisplayName = "読み取りが値を書いたときだけ宛先を置き換える")]
    public async Task Written_ReplacesDestination()
    {
        var result = await RuntimeBinaryColumns.ReadToFileAsync(
            Destination,
            async stream =>
            {
                await stream.WriteAsync("new value"u8.ToArray());
                return true;
            }
        );

        result.Should().BeTrue();
        File.ReadAllBytes(Destination).Should().Equal("new value"u8.ToArray());
        ShouldLeaveOnlyDestination();
    }

    [Fact(DisplayName = "行なし・NULL（false）では既存のファイルに触れない")]
    public async Task NotWritten_LeavesExistingFile()
    {
        var result = await RuntimeBinaryColumns.ReadToFileAsync(
            Destination,
            async stream =>
            {
                await stream.WriteAsync("partial"u8.ToArray());
                return false;
            }
        );

        result.Should().BeFalse();
        File.ReadAllBytes(Destination).Should().Equal("old"u8.ToArray());
        ShouldLeaveOnlyDestination();
    }

    [Fact(DisplayName = "宛先が無いとき false ならファイルを作らない")]
    public async Task NotWritten_DoesNotCreateDestination()
    {
        File.Delete(Destination);

        var result = await RuntimeBinaryColumns.ReadToFileAsync(
            Destination,
            _ => Task.FromResult(false)
        );

        result.Should().BeFalse();
        _directory.GetFiles().Should().BeEmpty();
    }

    [Fact(
        DisplayName = "読み取りの例外は呼び出し元へ届き、既存のファイルに触れず一時ファイルも残さない"
    )]
    public async Task Failure_LeavesExistingFile()
    {
        var act = () =>
            RuntimeBinaryColumns.ReadToFileAsync(
                Destination,
                async stream =>
                {
                    await stream.WriteAsync("partial"u8.ToArray());
                    throw new IOException("read failed");
                }
            );

        await act.Should().ThrowAsync<IOException>().WithMessage("read failed");
        File.ReadAllBytes(Destination).Should().Equal("old"u8.ToArray());
        ShouldLeaveOnlyDestination();
    }

    [Fact(DisplayName = "キャンセルでも既存のファイルに触れず一時ファイルも残さない")]
    public async Task Cancellation_LeavesExistingFile()
    {
        using var cancellation = new CancellationTokenSource();

        var act = () =>
            RuntimeBinaryColumns.ReadToFileAsync(
                Destination,
                async stream =>
                {
                    await stream.WriteAsync("partial"u8.ToArray());
                    await cancellation.CancelAsync();
                    cancellation.Token.ThrowIfCancellationRequested();
                    return true;
                }
            );

        await act.Should().ThrowAsync<OperationCanceledException>();
        File.ReadAllBytes(Destination).Should().Equal("old"u8.ToArray());
        ShouldLeaveOnlyDestination();
    }

    /// <remarks>
    /// ランタイムのパッケージは QuickER.Platform を参照できないので、一時ファイルの名前の規則を写しで持つ。
    /// アプリの原子的な書き込み（<see cref="AtomicFile.TemporaryPathFor"/>）と同じ形であることを、読み取りの途中に
    /// できる一時ファイルの名前で確かめる（同じ判定で残骸として扱える）。
    /// </remarks>
    [Theory(
        DisplayName = "一時ファイルの名前は、アプリの原子的な書き込みと同じ形で、長い名前でも 255 文字に収まる"
    )]
    [InlineData("out.bin")]
    [InlineData(null)]
    public async Task TemporaryFile_FollowsAtomicFileNaming(string? fileName)
    {
        var destination = Path.Combine(
            _directory.FullName,
            fileName ?? new string('x', 250) + ".bin"
        );
        string? temporaryName = null;

        await RuntimeBinaryColumns.ReadToFileAsync(
            destination,
            async stream =>
            {
                await stream.WriteAsync("v"u8.ToArray());
                temporaryName = _directory
                    .GetFiles()
                    .Select(file => file.Name)
                    .Single(name => name != "out.bin" && name != Path.GetFileName(destination));
                return true;
            }
        );

        temporaryName.Should().NotBeNull();
        AtomicFile.IsTemporaryFileName(temporaryName!).Should().BeTrue();
        temporaryName!.Length.Should().BeLessThanOrEqualTo(255);
        File.ReadAllBytes(destination).Should().Equal("v"u8.ToArray());
    }

    [Fact(DisplayName = "隠し属性の宛先を置き換えても、隠し属性は残る")]
    public async Task HiddenDestination_KeepsHidden()
    {
        File.SetAttributes(Destination, FileAttributes.Hidden);

        await RuntimeBinaryColumns.ReadToFileAsync(
            Destination,
            async stream =>
            {
                await stream.WriteAsync("new"u8.ToArray());
                return true;
            }
        );

        File.ReadAllBytes(Destination).Should().Equal("new"u8.ToArray());
        File.GetAttributes(Destination).Should().HaveFlag(FileAttributes.Hidden);
        ShouldLeaveOnlyDestination();
    }
}
