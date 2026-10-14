using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace QuickER.Tests.TestSupport;

/// <summary>
/// ファイルへ読み書きハンドルの oplock（Read・Write・Handle）を掛け、ほかのハンドルがそのファイルを開こうとすると、
/// 解放するまでその open を止める（Windows の oplock の解除待ち）。
/// </summary>
/// <remarks>
/// <para>
/// 製品のコードへ待ち合わせの口を足さずに、「ある処理がこのファイルを開く途中で止まっている」状態をテストが決定的に
/// 作るためのもの。止まった処理は、<see cref="Dispose"/> で oplock を手放すまで先へ進まない。
/// </para>
/// <para>
/// ほかの open が始まると oplock の解除が通知され、<see cref="WaitForBreak"/> が真を返す。ウイルス対策などほかの
/// プロセスが先に開いても通知は来るが、手放すまではその後の open もすべて止まる。oplock を掛けられないファイル
/// システムでは <see cref="Acquire"/> が例外を投げる（黙って止めないまま進まない）。
/// </para>
/// </remarks>
public sealed class FileOplock : IDisposable
{
    private const uint GenericRead = 0x80000000;
    private const uint FileShareAll = 0x1 | 0x2 | 0x4;
    private const uint OpenExisting = 3;
    private const uint FileFlagOverlapped = 0x40000000;
    private const uint FsctlRequestOplock = 0x00090240;
    private const int ErrorIoPending = 997;

    // REQUEST_OPLOCK_INPUT_BUFFER（版・長さ・要求する水準 R|W|H・要求の印）と、出力の REQUEST_OPLOCK_OUTPUT_BUFFER
    private const int InputSize = 16;
    private const int OutputSize = 24;

    private readonly SafeFileHandle _handle;
    private readonly ManualResetEvent _completed = new(false);
    private readonly IntPtr _overlapped;
    private readonly IntPtr _buffers;
    private bool _disposed;

    private FileOplock(SafeFileHandle handle)
    {
        _handle = handle;
        _overlapped = Marshal.AllocHGlobal(Marshal.SizeOf<NativeOverlapped>());
        _buffers = Marshal.AllocHGlobal(InputSize + OutputSize);

        var overlapped = new NativeOverlapped
        {
            EventHandle = _completed.SafeWaitHandle.DangerousGetHandle(),
        };
        Marshal.StructureToPtr(overlapped, _overlapped, fDeleteOld: false);

        for (var i = 0; i < InputSize + OutputSize; i++)
        {
            Marshal.WriteByte(_buffers, i, 0);
        }

        Marshal.WriteInt16(_buffers, 0, 1);
        Marshal.WriteInt16(_buffers, 2, InputSize);
        Marshal.WriteInt32(_buffers, 4, 0x1 | 0x2 | 0x4);
        Marshal.WriteInt32(_buffers, 8, 0x1);
        Marshal.WriteInt16(_buffers, InputSize, 1);
        Marshal.WriteInt16(_buffers, InputSize + 2, OutputSize);
    }

    /// <summary>既存のファイルへ oplock を掛ける</summary>
    /// <param name="path">対象のファイル</param>
    public static FileOplock Acquire(string path)
    {
        var handle = CreateFileW(
            path,
            GenericRead,
            FileShareAll,
            IntPtr.Zero,
            OpenExisting,
            FileFlagOverlapped,
            IntPtr.Zero
        );

        if (handle.IsInvalid)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"{path} を開けません");
        }

        var oplock = new FileOplock(handle);
        var granted = DeviceIoControl(
            handle,
            FsctlRequestOplock,
            oplock._buffers,
            InputSize,
            oplock._buffers + InputSize,
            OutputSize,
            IntPtr.Zero,
            oplock._overlapped
        );
        var error = Marshal.GetLastWin32Error();

        // 掛かった oplock は保留中の要求として残り、解除が始まると完了する
        if (granted || error != ErrorIoPending)
        {
            // 保留中の要求が無いので、完了を待たずに解放する
            oplock._disposed = true;
            handle.Dispose();
            Marshal.FreeHGlobal(oplock._overlapped);
            Marshal.FreeHGlobal(oplock._buffers);
            oplock._completed.Dispose();
            throw new Win32Exception(error, $"{path} へ oplock を掛けられません");
        }

        return oplock;
    }

    /// <summary>ほかの open が oplock の解除を始めるまで待つ（時間内に始まれば真）</summary>
    /// <param name="timeout">待つ上限</param>
    public bool WaitForBreak(TimeSpan timeout) => _completed.WaitOne(timeout);

    /// <summary>oplock を手放し、止まっている open を先へ進める</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        // ハンドルを閉じると oplock が外れ、保留中の要求も完了する。完了を待ってから要求の領域を解放する
        // （完了しないまま解放すると、OS が解放済みの領域へ書く）
        _handle.Dispose();

        if (_completed.WaitOne(TimeSpan.FromSeconds(30)))
        {
            Marshal.FreeHGlobal(_overlapped);
            Marshal.FreeHGlobal(_buffers);
            _completed.Dispose();
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
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
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(
        SafeFileHandle device,
        uint ioControlCode,
        IntPtr inBuffer,
        int inBufferSize,
        IntPtr outBuffer,
        int outBufferSize,
        IntPtr bytesReturned,
        IntPtr overlapped
    );
}
