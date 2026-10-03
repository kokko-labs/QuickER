using System.Runtime.InteropServices;

namespace QuickER.Services;

/// <summary>Win32 API の宣言（GUI が直接呼ぶものだけを集める）</summary>
internal static class NativeMethods
{
    /// <summary><see cref="AllowSetForegroundWindow"/> へ渡す「どのプロセスにも許す」（ASFW_ANY = (DWORD)-1）</summary>
    internal const uint AllowSetForegroundAnyProcess = uint.MaxValue;

    /// <summary>
    /// 指定プロセス（<see cref="AllowSetForegroundAnyProcess"/> ならどのプロセスでも）が、次にウィンドウを前面へ出す
    /// ことを許す（user32 の AllowSetForegroundWindow）
    /// </summary>
    /// <param name="dwProcessId">許すプロセスの ID（DWORD）</param>
    /// <returns>
    /// 成功したら true。呼び出し元が前面化の権利を持たないときなどは false（理由は <see cref="Marshal.GetLastPInvokeError"/>）
    /// </returns>
    [DllImport("user32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool AllowSetForegroundWindow(uint dwProcessId);
}
