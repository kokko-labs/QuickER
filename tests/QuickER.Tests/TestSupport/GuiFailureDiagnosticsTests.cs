using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AwesomeAssertions;
using static QuickER.Tests.TestSupport.WpfApplicationTestSupport;

namespace QuickER.Tests.TestSupport;

/// <summary>
/// <see cref="GuiFailureDiagnostics"/>（GUI テストが失敗した瞬間の状態の記録）が、元の失敗を変えずに
/// 状態を残すことを検証するテストクラス。
/// </summary>
/// <remarks>
/// ウィンドウを表示してキーを流し、Activated / Deactivated とキーボードフォーカスの記録を見るので、
/// ほかのテストと並列に走らせない（<see cref="WindowActivationTestCollection"/>）。
/// 自己テストの記録は出力先（テストの出力・標準エラー・ファイル）へ書かない
/// （<see cref="GuiFailureDiagnostics.SuppressSinksForTests"/>＝調べる人が読むファイルを汚さない）。
/// </remarks>
[Collection(WindowActivationTestCollection.Name)]
public class GuiFailureDiagnosticsTests
{
    /// <summary>ボタンを 1 つ置いた画面外のウィンドウを表示し、追跡する</summary>
    private static (Window Window, Button Button) ShowTrackedWindow()
    {
        var button = new Button { Content = "Button", Name = "DiagnosticsButton" };
        var window = new Window
        {
            Content = button,
            Width = 200,
            Height = 200,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -10000,
            Top = -10000,
            Title = "DiagnosticsWindow",
        };
        GuiFailureDiagnostics.Track(window);
        window.Show();
        DoEvents();
        return (window, button);
    }

    /// <summary>キーの押下か解放を、入力の経路（InputManager）へ流す</summary>
    private static void SendKey(Window window, Key key, bool down)
    {
        var source = PresentationSource.FromVisual(window)!;
        InputManager.Current.ProcessInput(
            new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, key)
            {
                RoutedEvent = down ? Keyboard.PreviewKeyDownEvent : Keyboard.PreviewKeyUpEvent,
            }
        );
    }

    [Fact(
        DisplayName = "Capture は false を返し、入力・ウィンドウ・前面・静的状態・時系列の区画を記録する"
    )]
    public void Capture_ReturnsFalse_AndRecordsEverySection()
    {
        using var _ = GuiFailureDiagnostics.SuppressSinksForTests();
        bool? result = null;
        string? report = null;

        RunSta(() =>
        {
            var (window, button) = ShowTrackedWindow();

            try
            {
                button.Focus();
                result = GuiFailureDiagnostics.Capture(new InvalidOperationException("direct"));
                report = GuiFailureDiagnostics.LastReport;
            }
            finally
            {
                window.Close();
            }
        });

        result.Should().BeFalse("true を返すとフィルターの catch が元の失敗を飲み込む");
        report.Should().NotBeNull();
        report
            .Should()
            .Contain("System.InvalidOperationException: direct")
            .And.Contain("Keyboard.FocusedElement=Button(DiagnosticsButton)")
            .And.Contain("Mouse.Captured=")
            .And.Contain("Mouse.LeftButton=")
            .And.Contain("GetAsyncKeyState(VK_LBUTTON)の最上位ビット=")
            .And.Contain("Mouse.RightButton=")
            .And.Contain("実際のボタン=L=")
            .And.Contain("[追跡しているウィンドウ]")
            .And.Contain("W1:Window: Title='DiagnosticsWindow'")
            .And.Contain("[前面のウィンドウ]")
            .And.Contain("[Win32 から届いた入力]")
            .And.Contain("[CanvasViewportBehavior の静的状態]")
            .And.Contain("_focusArrivedByKeyboard=")
            .And.Contain("_isPanActive=")
            .And.Contain("[DragBehavior の静的状態]")
            .And.Contain("[RubberBandBehavior の静的状態]")
            .And.Contain("[イベントの時系列]")
            .And.NotContain("記録に失敗");

        // 前面が別のプロセスのウィンドウなら、タイトルを書かずにプロセス名だけを書く（利用者のほかのアプリのタイトルを残さない）
        var foregroundLine = report!
            .Split('\n')
            .Select(line => line.TrimEnd('\r'))
            .SkipWhile(line => line != "[前面のウィンドウ]")
            .Skip(1)
            .First();

        if (foregroundLine.Contains("このプロセス=False", StringComparison.Ordinal))
        {
            foregroundLine.Should().Contain("process=").And.NotContain("title=");
        }
        else if (foregroundLine.Contains("このプロセス=True", StringComparison.Ordinal))
        {
            foregroundLine.Should().Contain("title=").And.NotContain("process=");
        }
    }

    [Fact(
        DisplayName = "区画が例外を投げても Capture は false を返して例外を出さず、記録に失敗と書く"
    )]
    public void Capture_SectionThrows_StillReturnsFalseAndNotesFailure()
    {
        using var _ = GuiFailureDiagnostics.SuppressSinksForTests();
        bool? result = null;
        string? report = null;
        Exception? escaped = null;

        RunSta(() =>
        {
            GuiFailureDiagnostics.ExtraSectionForTests = _ =>
                throw new InvalidOperationException("injected-section-failure");

            try
            {
                result = GuiFailureDiagnostics.Capture(new InvalidOperationException("original"));
                report = GuiFailureDiagnostics.LastReport;
            }
            catch (Exception ex)
            {
                escaped = ex;
            }
            finally
            {
                GuiFailureDiagnostics.ExtraSectionForTests = null;
            }
        });

        escaped.Should().BeNull("例外を出すと元の失敗が別の例外に置き換わる");
        result.Should().BeFalse();
        report
            .Should()
            .NotBeNull()
            .And.Contain("記録に失敗: InvalidOperationException: injected-section-failure")
            .And.Contain("[イベントの時系列]", "ほかの区画は記録される");
    }

    [Fact(
        DisplayName = "RunSta の本体が失敗すると、元の例外のまま失敗し、ウィンドウが開いたままの状態と時系列が記録される"
    )]
    public void RunSta_FailingBody_KeepsOriginalFailureAndRecordsState()
    {
        using var _ = GuiFailureDiagnostics.SuppressSinksForTests();
        var marker = "marker-" + Guid.NewGuid().ToString("N");

        var act = () =>
            RunSta(() =>
            {
                var (window, button) = ShowTrackedWindow();

                try
                {
                    button.Focus();
                    SendKey(window, Key.A, down: true);
                    SendKey(window, Key.A, down: false);
                    button.CaptureMouse();
                    button.ReleaseMouseCapture();
                    InjectRawMouseMove(window);
                    throw new InvalidOperationException(marker);
                }
                finally
                {
                    window.Close();
                }
            });

        var thrown = act.Should().Throw<Exception>().Which;
        thrown.Message.Should().Contain(marker).And.Contain("System.InvalidOperationException");

        var report = GuiFailureDiagnostics.LastRunReport;
        report.Should().NotBeNull();
        report!
            .Should()
            .Contain($"System.InvalidOperationException: {marker}")
            .And.Contain("W1:Window: Title='DiagnosticsWindow'")
            .And.Contain("IsLoaded=True Closed=False", "閉じる finally より前に記録する")
            .And.Contain("W1:Window: Track")
            .And.Contain("W1:Window: PreviewKeyDown A")
            .And.Contain("W1:Window: PreviewKeyUp A")
            .And.Contain("PreviewGotKeyboardFocus")
            .And.Contain("W1:Window: GotMouseCapture src=Button(DiagnosticsButton)")
            .And.Contain("W1:Window: LostMouseCapture src=Button(DiagnosticsButton) now=null")
            .And.Contain("前面: hwnd=", "実行の始めの前面のウィンドウを書く")
            .And.NotContain("W1:Window: Closed");

        // Win32 から届いた入力（合成の MouseEventArgs / KeyEventArgs ではない生の報告）は、件数と中身を区画へ書く
        report.Should().MatchRegex(@"件数=[1-9]").And.Contain("RawMouseInputReport");
    }

    /// <summary>
    /// Win32 から届くのと同じ形の生のマウスの移動の報告を、入力の経路へ流す（利用者の本物の操作の代わり。
    /// 報告の型は internal なので反射で作る）
    /// </summary>
    private static void InjectRawMouseMove(Window window)
    {
        const System.Reflection.BindingFlags Any =
            System.Reflection.BindingFlags.Instance
            | System.Reflection.BindingFlags.Public
            | System.Reflection.BindingFlags.NonPublic;
        var assembly = typeof(InputManager).Assembly;
        var argsType = assembly.GetType("System.Windows.Input.InputReportEventArgs", true)!;
        var actionsType = assembly.GetType("System.Windows.Input.RawMouseActions", true)!;
        var reportType = assembly.GetType("System.Windows.Input.RawMouseInputReport", true)!;
        var report = Activator.CreateInstance(
            reportType,
            Any,
            binder: null,
            args:
            [
                InputMode.Foreground,
                Environment.TickCount,
                PresentationSource.FromVisual(window)!,
                Enum.Parse(actionsType, "AbsoluteMove"),
                10,
                10,
                0,
                IntPtr.Zero,
            ],
            culture: null
        )!;
        var args = (InputEventArgs)
            Activator.CreateInstance(
                argsType,
                Any,
                binder: null,
                args: [Mouse.PrimaryDevice, report],
                culture: null
            )!;
        args.RoutedEvent = (RoutedEvent)
            typeof(InputManager)
                .GetField(
                    "PreviewInputReportEvent",
                    System.Reflection.BindingFlags.Static
                        | System.Reflection.BindingFlags.Public
                        | System.Reflection.BindingFlags.NonPublic
                )!
                .GetValue(null)!;
        GuiFailureDiagnostics
            .IsPreviewInputReport(args)
            .Should()
            .BeTrue("見分け方が WPF の実際のイベントに合う");
        InputManager.Current.ProcessInput(args);
        DoEvents();
    }

    [Fact(
        DisplayName = "CaptureViolation は区画が例外を投げても例外を出さず、違反の要約・直前の手・ほかの区画を記録する"
    )]
    public void CaptureViolation_RecordsSummaryAndRecentSteps_WithoutThrowing()
    {
        using var _ = GuiFailureDiagnostics.SuppressSinksForTests();
        string? report = null;
        Exception? escaped = null;
        var marker = "violation-" + Guid.NewGuid().ToString("N");

        RunSta(() =>
        {
            var (window, _) = ShowTrackedWindow();
            GuiFailureDiagnostics.ExtraSectionForTests = _ =>
                throw new InvalidOperationException("injected-section-failure");

            try
            {
                GuiFailureDiagnostics.Note("step-marker");
                GuiFailureDiagnostics.CaptureViolation(
                    $"{marker}\nsecond-line",
                    ["recent-1", "recent-2"]
                );
                report = GuiFailureDiagnostics.LastReport;
            }
            catch (Exception ex)
            {
                escaped = ex;
            }
            finally
            {
                GuiFailureDiagnostics.ExtraSectionForTests = null;
                window.Close();
            }
        });

        escaped.Should().BeNull("記録の失敗をテストへ持ち込まない");
        GuiFailureDiagnostics
            .LastRunReport.Should()
            .Be(report, "実行の文脈へ残す（テストのスレッドから読める）");
        report
            .Should()
            .NotBeNull()
            .And.Contain("[表明の失敗]")
            .And.Contain($"  {marker}")
            .And.Contain("  second-line")
            .And.Contain("[直前の手]")
            .And.Contain("  recent-1")
            .And.Contain("  recent-2")
            .And.Contain("手: step-marker")
            .And.Contain("W1:Window: Title='DiagnosticsWindow'")
            .And.Contain("記録に失敗: InvalidOperationException: injected-section-failure")
            .And.Contain("[イベントの時系列]", "ほかの区画は記録される");
    }

    [Fact(
        DisplayName = "RunInIsolatedWindow の検証が失敗すると、元の例外のまま失敗し、MainWindow の状態が記録される"
    )]
    public void RunInIsolatedWindow_FailingAssert_KeepsOriginalFailureAndRecordsMainWindow()
    {
        using var _ = GuiFailureDiagnostics.SuppressSinksForTests();
        var marker = "marker-" + Guid.NewGuid().ToString("N");

        var act = () =>
            RunInIsolatedWindow(
                (_, _) =>
                {
                    throw new InvalidOperationException(marker);
                }
            );

        var thrown = act.Should().Throw<Exception>().Which;
        thrown.Message.Should().Contain(marker).And.Contain("System.InvalidOperationException");

        var report = GuiFailureDiagnostics.LastRunReport;
        report.Should().NotBeNull();
        report!
            .Should()
            .Contain($"System.InvalidOperationException: {marker}")
            .And.Contain("W1:MainWindow: Title=")
            .And.Contain("Closed=False", "閉じる finally より前に記録する")
            .And.Contain("W1:MainWindow: Track");
    }
}
