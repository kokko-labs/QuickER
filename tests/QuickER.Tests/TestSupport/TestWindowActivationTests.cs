using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AwesomeAssertions;
using static QuickER.Tests.TestSupport.WpfApplicationTestSupport;

namespace QuickER.Tests.TestSupport;

/// <summary>
/// <see cref="TestWindowActivation"/>（前面を奪わずに同じスレッドの中だけで窓をアクティブにする）が、窓のアクティブの
/// 行き来とフォーカスの移りを起こし、前面のウィンドウのプロセスを変えないことを、本物の窓で検証するテストクラス
/// </summary>
/// <remarks>
/// 窓のアクティブとフォーカスに左右されるので、並列に走らせない（<see cref="WindowActivationTestCollection"/>）。
/// </remarks>
[Collection(WindowActivationTestCollection.Name)]
public sealed class TestWindowActivationTests
{
    /// <summary>
    /// 2 つの窓の間でアクティブを行き来させると、行き来のたびに相手の窓が非アクティブになってこちらがアクティブになり、
    /// キーボードのフォーカスも移る。前面のウィンドウのプロセスは変わらない（このプロセスが前面でなければ前面は変わらず、
    /// 前面なら同じプロセスの中で移るだけ）。表示していない窓は断る
    /// </summary>
    [Fact(
        DisplayName = "スレッドの中だけのアクティブ化は窓とフォーカスを行き来させ、前面のプロセスを変えない"
    )]
    public void ActivateWithinThread_SwitchesWindowsWithoutTakingForeground()
    {
        RunSta(() =>
        {
            var events = new List<string>();
            var first = CreateWindow("first", events);
            var second = CreateWindow("second", events);
            var hidden = new Window();

            try
            {
                first.Show();
                second.Show();
                DoEvents();
                var foregroundBefore = ForegroundProcessIsOwn();

                // 始めの状態（表示しただけでどちらかがアクティブになるか）に依らないよう、先に 2 つめをアクティブにしておく
                TestWindowActivation.ActivateWithinThread(second);
                DoEvents();
                second.IsActive.Should().BeTrue("アクティブにした窓がアクティブになる");
                events.Clear();

                TestWindowActivation.ActivateWithinThread(first);
                DoEvents();
                first.IsActive.Should().BeTrue("アクティブにした窓がアクティブになる");
                second.IsActive.Should().BeFalse("相手の窓は非アクティブになる");
                Keyboard.FocusedElement.Should().BeSameAs(first.Content, "フォーカスも窓へ入る");

                TestWindowActivation.ActivateWithinThread(second);
                DoEvents();
                TestWindowActivation.ActivateWithinThread(first);
                DoEvents();

                events
                    .Should()
                    .Equal(
                        ["second-", "first+", "first-", "second+", "second-", "first+"],
                        "行き来のたびに相手が非アクティブになり、こちらがアクティブになる"
                    );
                first.IsActive.Should().BeTrue();
                second.IsActive.Should().BeFalse();
                Keyboard
                    .FocusedElement.Should()
                    .BeSameAs(first.Content, "戻った窓のフォーカスへ戻る");
                ForegroundProcessIsOwn()
                    .Should()
                    .Be(
                        foregroundBefore,
                        "前面のウィンドウのプロセスを変えない（前面を取りに行かない）"
                    );

                var act = () => TestWindowActivation.ActivateWithinThread(hidden);
                act.Should()
                    .Throw<InvalidOperationException>("表示していない窓はアクティブにできない");
            }
            finally
            {
                first.Close();
                second.Close();
                DoEvents();
            }
        });
    }

    /// <summary>画面の外に、アクティブにせずに表示する窓（中身はフォーカスを持てるボタン 1 つ）</summary>
    private static Window CreateWindow(string name, List<string> events)
    {
        var window = new Window
        {
            Content = new Button { Content = name },
            Width = 100,
            Height = 100,
            ShowInTaskbar = false,
            ShowActivated = false,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -10000,
            Top = -10000,
        };
        window.Activated += (_, _) => events.Add(name + "+");
        window.Deactivated += (_, _) => events.Add(name + "-");
        window.Loaded += (_, _) => FocusManager.SetFocusedElement(window, (Button)window.Content);
        GuiFailureDiagnostics.Track(window);
        return window;
    }

    /// <summary>前面のウィンドウがこのプロセスのものか</summary>
    private static bool ForegroundProcessIsOwn()
    {
        _ = GetWindowThreadProcessId(GetForegroundWindow(), out var processId);
        return processId == (uint)Environment.ProcessId;
    }

    [DllImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
}
