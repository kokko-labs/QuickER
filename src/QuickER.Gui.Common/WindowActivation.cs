using System.Windows;

namespace QuickER.Gui.Common;

/// <summary>使い回すモードレスのウィンドウ（AI チャット・AI モック生成）を、開き直すときに前面へ出す共有ヘルパー</summary>
public static class WindowActivation
{
    /// <summary>ウィンドウを表示して前面へ出す。最小化されていれば、最小化する前の大きさへ戻す</summary>
    /// <remarks>
    /// <see cref="Window.Show"/> と <see cref="Window.Activate"/> だけでは、最小化したウィンドウは
    /// タスクバーに収まったまま戻らない。戻すのは <see cref="SystemCommands.RestoreWindow"/>（Windows の「元に戻す」）で、
    /// 最小化の前に最大化していたウィンドウは最大化へ戻る。
    /// </remarks>
    /// <param name="window">前面へ出すウィンドウ</param>
    public static void ShowAndActivate(Window window)
    {
        window.Show();

        if (window.WindowState == WindowState.Minimized)
        {
            SystemCommands.RestoreWindow(window);
        }

        window.Activate();
    }
}
