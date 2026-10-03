using System.Windows;
using AwesomeAssertions;
using QuickER.Gui.Common;
using QuickER.Tests.TestSupport;

namespace QuickER.Tests.Gui.Common;

/// <summary>
/// <see cref="WindowActivation.ShowAndActivate"/> が、最小化したウィンドウを最小化する前の大きさへ戻すことを検証するテストクラス。
/// </summary>
public class WindowActivationTests
{
    /// <summary>画面外・非アクティブのウィンドウを表示して検証を実行し、最後に閉じる</summary>
    private static void WithShownWindow(Action<Window> assert)
    {
        WpfApplicationTestSupport.RunSta(() =>
        {
            var window = new Window
            {
                Width = 300,
                Height = 200,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -4000,
                Top = -4000,
                ShowActivated = false,
                ShowInTaskbar = false,
            };
            window.Show();

            try
            {
                WpfApplicationTestSupport.DoEvents();
                assert(window);
            }
            finally
            {
                window.Close();
            }
        });
    }

    /// <summary>通常の大きさから最小化したウィンドウは、開き直すと通常の大きさへ戻ることを検証する</summary>
    [Fact(DisplayName = "最小化したウィンドウを開き直すと通常の大きさへ戻る")]
    public void ShowAndActivate_Minimized_RestoresToNormal()
    {
        WithShownWindow(window =>
        {
            window.WindowState = WindowState.Minimized;
            WpfApplicationTestSupport.DoEvents();
            window.WindowState.Should().Be(WindowState.Minimized, "最小化した状態を作る");

            WindowActivation.ShowAndActivate(window);
            WpfApplicationTestSupport.DoEvents();

            window.WindowState.Should().Be(WindowState.Normal);
        });
    }

    /// <summary>最大化してから最小化したウィンドウは、開き直すと最大化へ戻ることを検証する</summary>
    [Fact(DisplayName = "最大化してから最小化したウィンドウを開き直すと最大化へ戻る")]
    public void ShowAndActivate_MinimizedFromMaximized_RestoresToMaximized()
    {
        WithShownWindow(window =>
        {
            window.WindowState = WindowState.Maximized;
            WpfApplicationTestSupport.DoEvents();
            window.WindowState.Should().Be(WindowState.Maximized, "最大化した状態を作る");
            window.WindowState = WindowState.Minimized;
            WpfApplicationTestSupport.DoEvents();
            window.WindowState.Should().Be(WindowState.Minimized, "最小化した状態を作る");

            WindowActivation.ShowAndActivate(window);
            WpfApplicationTestSupport.DoEvents();

            window.WindowState.Should().Be(WindowState.Maximized);
        });
    }

    /// <summary>最小化していないウィンドウの状態は変えないことを検証する</summary>
    [Fact(DisplayName = "最小化していないウィンドウの状態は変えない")]
    public void ShowAndActivate_NotMinimized_KeepsState()
    {
        WithShownWindow(window =>
        {
            WindowActivation.ShowAndActivate(window);
            WpfApplicationTestSupport.DoEvents();

            window.WindowState.Should().Be(WindowState.Normal);
            window.IsVisible.Should().BeTrue();
        });
    }
}
