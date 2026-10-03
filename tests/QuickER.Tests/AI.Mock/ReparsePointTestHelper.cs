using System.IO;
using System.Runtime.InteropServices;
using AwesomeAssertions;
using Microsoft.Win32.SafeHandles;

namespace QuickER.Tests.AI.Mock;

/// <summary>
/// モックプロジェクトの最終ビルド前処理（変更検知・obj / bin の削除）のテストで使う、reparse point と
/// ハンドルの共有ヘルパー。
/// </summary>
/// <remarks>
/// <para>
/// 「リンクでない reparse フォルダ」は、OneDrive の Files On-Demand のフォルダと同じ形
/// （属性は <see cref="FileAttributes.ReparsePoint"/> 付き・<see cref="FileSystemInfo.LinkTarget"/> は null・
/// 中身は普通に読み書きできる）を OneDrive なしで作るためのもの。サードパーティのタグにディレクトリビット
/// （0x10000000）を付けると、空でないフォルダにも管理者権限なしで設定でき、中身はそのまま読み書きできる
/// （実測で確認）。ディレクトリビットの無いタグは空のフォルダにしか設定できず、処理するフィルタが居ないので
/// 中を列挙できない＝「列挙できない reparse フォルダ」の再現に使う。
/// </para>
/// <para>
/// 後始末は <see cref="DeleteTree"/> を使う。reparse point を外してから消さないと、列挙できないフォルダは
/// <see cref="Directory.Delete(string, bool)"/> で消せず、次のテストへ残骸を持ち越す。
/// </para>
/// </remarks>
internal static class ReparsePointTestHelper
{
    /// <summary>中身を読み書きできる「リンクでない reparse フォルダ」のタグ（サードパーティ・ディレクトリビット付き）</summary>
    private const uint ReadableNonLinkTag = 0x10000099;

    /// <summary>中を列挙できない reparse フォルダのタグ（サードパーティ・ディレクトリビットなし）</summary>
    private const uint UnlistableTag = 0x00000099;

    /// <summary>サードパーティのタグに付ける GUID（設定と削除で一致している必要がある）</summary>
    private static readonly Guid TagGuid = new("8f3f6c3e-2b5d-4d0e-9a3c-6f1d2e4b7a91");

    private const uint FsctlSetReparsePoint = 0x000900A4;
    private const uint FsctlDeleteReparsePoint = 0x000900AC;
    private const uint GenericRead = 0x80000000;
    private const uint GenericWrite = 0x40000000;
    private const uint FileShareRead = 0x1;
    private const uint FileShareWrite = 0x2;
    private const uint FileShareDelete = 0x4;
    private const uint OpenExisting = 3;
    private const uint FileFlagBackupSemantics = 0x02000000;
    private const uint FileFlagOpenReparsePoint = 0x00200000;

    /// <summary>REPARSE_GUID_DATA_BUFFER のヘッダー長（タグ 4・データ長 2・予約 2・GUID 16）</summary>
    private const int GuidHeaderSize = 24;

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern SafeFileHandle CreateFileW(
        string fileName,
        uint desiredAccess,
        uint shareMode,
        IntPtr securityAttributes,
        uint creationDisposition,
        uint flagsAndAttributes,
        IntPtr templateFile
    );

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(
        SafeFileHandle device,
        uint ioControlCode,
        byte[] inBuffer,
        int inBufferSize,
        IntPtr outBuffer,
        int outBufferSize,
        out int bytesReturned,
        IntPtr overlapped
    );

    /// <summary>
    /// フォルダを「リンクでない reparse フォルダ」にする（中身はそのまま読み書きできる・空でなくてよい）。
    /// </summary>
    internal static void MakeReadableNonLinkReparseDirectory(string directory)
    {
        SetReparsePoint(directory, ReadableNonLinkTag);
        var info = new DirectoryInfo(directory);
        info.Attributes.HasFlag(FileAttributes.ReparsePoint).Should().BeTrue();
        info.LinkTarget.Should().BeNull("リンクでない reparse point であること");
    }

    /// <summary>空のフォルダを、中を列挙できない reparse フォルダにする</summary>
    internal static void MakeUnlistableReparseDirectory(string directory)
    {
        Directory.EnumerateFileSystemEntries(directory).Should().BeEmpty();
        SetReparsePoint(directory, UnlistableTag);
        var listing = () => Directory.EnumerateFileSystemEntries(directory).ToList();
        listing.Should().Throw<IOException>("処理するフィルタの居ないタグなので中を列挙できない");
    }

    /// <summary>
    /// 削除の共有を許さずにファイルまたはフォルダを開く（破棄するまで削除が失敗する）。
    /// フォルダのリンクはリンク自体を開く（リンク先を辿らない）。
    /// </summary>
    internal static SafeFileHandle OpenWithoutDeleteSharing(string path)
    {
        var handle = CreateFileW(
            path,
            GenericRead,
            FileShareRead | FileShareWrite,
            IntPtr.Zero,
            OpenExisting,
            FileFlagBackupSemantics | FileFlagOpenReparsePoint,
            IntPtr.Zero
        );
        handle
            .IsInvalid.Should()
            .BeFalse(
                $"テスト前提のハンドルを開ける必要がある（Win32 {Marshal.GetLastWin32Error()}）"
            );
        return handle;
    }

    /// <summary>
    /// ジャンクション（ディレクトリの reparse point）を作る。
    /// </summary>
    /// <remarks>
    /// ジャンクションの作成に公開 API は無く、シンボリックリンクは特権（開発者モード）を要するため、
    /// 特権不要な <c>mklink /J</c> を使う。テストは Windows 専用プロジェクトにある。
    /// </remarks>
    internal static void CreateJunction(string linkPath, string targetPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(linkPath)!);

        using var process = System.Diagnostics.Process.Start(
            new System.Diagnostics.ProcessStartInfo
            {
                FileName = "cmd.exe",
                ArgumentList = { "/c", "mklink", "/J", linkPath, targetPath },
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            }
        );
        process!.WaitForExit();
        process.ExitCode.Should().Be(0, "テスト前提のジャンクションを作成できる必要がある");
        new DirectoryInfo(linkPath)
            .Attributes.HasFlag(FileAttributes.ReparsePoint)
            .Should()
            .BeTrue();
    }

    /// <summary>
    /// テスト用の一時フォルダを消す。このヘルパーが付けた reparse point を外し、リンクはリンクだけを外し、
    /// 読み取り専用の属性を外してから削除する（リンク先の中身には触れない）。
    /// </summary>
    internal static void DeleteTree(string root)
    {
        if (!Directory.Exists(root))
        {
            return;
        }

        var directory = new DirectoryInfo(root);

        if (directory.LinkTarget is not null)
        {
            directory.Delete(recursive: false);
            return;
        }

        if (directory.Attributes.HasFlag(FileAttributes.ReparsePoint))
        {
            TryDeleteReparsePoint(root);
        }

        foreach (
            var child in directory.EnumerateFileSystemInfos(
                "*",
                new EnumerationOptions { AttributesToSkip = 0 }
            )
        )
        {
            if (child is DirectoryInfo childDirectory)
            {
                DeleteTree(childDirectory.FullName);
                continue;
            }

            child.Attributes &= ~FileAttributes.ReadOnly;
            child.Delete();
        }

        directory.Attributes &= ~FileAttributes.ReadOnly;
        directory.Delete(recursive: false);
    }

    private static void SetReparsePoint(string directory, uint tag)
    {
        using var handle = OpenForReparseControl(directory);
        var buffer = BuildGuidBuffer(tag, dataLength: 4);

        DeviceIoControl(
                handle,
                FsctlSetReparsePoint,
                buffer,
                buffer.Length,
                IntPtr.Zero,
                0,
                out _,
                IntPtr.Zero
            )
            .Should()
            .BeTrue(
                $"テスト前提の reparse point を設定できる必要がある（Win32 {Marshal.GetLastWin32Error()}）"
            );
    }

    /// <summary>このヘルパーが付けたタグの reparse point を外す（どちらのタグか分からないので両方試す）</summary>
    private static void TryDeleteReparsePoint(string directory)
    {
        using var handle = OpenForReparseControl(directory);

        foreach (var tag in new[] { ReadableNonLinkTag, UnlistableTag })
        {
            var buffer = BuildGuidBuffer(tag, dataLength: 0);

            if (
                DeviceIoControl(
                    handle,
                    FsctlDeleteReparsePoint,
                    buffer,
                    buffer.Length,
                    IntPtr.Zero,
                    0,
                    out _,
                    IntPtr.Zero
                )
            )
            {
                return;
            }
        }
    }

    private static SafeFileHandle OpenForReparseControl(string directory)
    {
        var handle = CreateFileW(
            directory,
            GenericRead | GenericWrite,
            FileShareRead | FileShareWrite | FileShareDelete,
            IntPtr.Zero,
            OpenExisting,
            FileFlagBackupSemantics | FileFlagOpenReparsePoint,
            IntPtr.Zero
        );
        handle
            .IsInvalid.Should()
            .BeFalse(
                $"reparse point を操作するハンドルを開ける必要がある（Win32 {Marshal.GetLastWin32Error()}）"
            );
        return handle;
    }

    /// <summary>REPARSE_GUID_DATA_BUFFER を組み立てる（データ部はゼロ埋め）</summary>
    private static byte[] BuildGuidBuffer(uint tag, ushort dataLength)
    {
        var buffer = new byte[GuidHeaderSize + dataLength];
        BitConverter.GetBytes(tag).CopyTo(buffer, 0);
        BitConverter.GetBytes(dataLength).CopyTo(buffer, 4);
        TagGuid.ToByteArray().CopyTo(buffer, 8);
        return buffer;
    }
}
