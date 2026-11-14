using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace QuickER.Tests.TestSupport;

/// <summary>
/// テストの窓を、本当の前面を利用者から奪わずに、同じスレッドの中だけでアクティブにする
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Window.Activate"/> は <c>SetForegroundWindow</c> を呼ぶ。Windows は、利用者が
/// <c>ForegroundLockTimeout</c>（既定 200 秒）より長く入力していないときと、呼び出し元がすでに前面のプロセスのときに、
/// これを通す。全件の実行の途中で利用者が離れると、テストの窓が本当の前面を取り、以後のテストの <c>Activate</c> も
/// 通り続ける。戻った利用者がほかのアプリをクリックした瞬間、その時点のテストの窓が非アクティブになってフォーカスが消え、
/// 入力の列のエンジンが全件でだけ落ちる。前面を取っている間は、利用者の本物のキー入力もテストの窓へ届く。
/// </para>
/// <para>
/// ここでは <c>SetActiveWindow</c> で、窓を作ったスレッドの中のアクティブな窓だけを替える。テストのプロセスが前面で
/// なければ前面は変わらず、前面なら同じプロセスの中で移るだけ。普段の実行（テストの窓は前面でなく、
/// <see cref="Window.Activate"/> は前面を取れない）と同じ、窓のアクティブ化・非アクティブ化のイベントとフォーカスの移りになる。
/// tests/QuickER.Tests は <see cref="Window.Activate"/> と <c>SetForegroundWindow</c> を呼ばない
/// （規則 <c>gui-test-foreground-activation</c>）。
/// </para>
/// </remarks>
internal static class TestWindowActivation
{
    /// <summary>窓を、同じスレッドの中だけでアクティブにする（前面を取りに行かない）</summary>
    /// <param name="window">表示済みの窓（窓を作ったスレッドで呼ぶ）</param>
    /// <exception cref="InvalidOperationException">窓のハンドルがまだ無い（表示していない）</exception>
    public static void ActivateWithinThread(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;

        if (handle == IntPtr.Zero)
        {
            throw new InvalidOperationException(
                "表示していない窓はアクティブにできない（先に Show する）"
            );
        }

        SetActiveWindow(handle);
    }

    [DllImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern IntPtr SetActiveWindow(IntPtr hWnd);
}
