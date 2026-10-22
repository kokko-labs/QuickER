using System.Collections;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using QuickER.Behaviors;

namespace QuickER.Tests.TestSupport;

/// <summary>
/// GUI テストが STA スレッド上で失敗した瞬間の状態（フォーカス・ウィンドウ・前面のウィンドウ・
/// ビヘイビアの静的状態・直前のイベントの時系列）を記録する、観測専用の診断。
/// </summary>
/// <remarks>
/// <para>
/// フォーカスに左右される GUI テストが、全件実行でだけまれに落ちて単独では合格する。原因の見当が
/// 付かないので、落ちた瞬間の状態を残す（直すためのものではない＝結果には一切影響させない）。
/// </para>
/// <para>
/// 呼び出し側は例外フィルター（<c>catch (Exception ex) when (Capture(ex))</c>）から呼ぶ。フィルターは
/// 例外処理の 1 回目の走査で、内側の finally（テストがウィンドウを閉じる処理）より前に、
/// 失敗したスレッドの上で動くので、ウィンドウが開いたままの状態を見られる。
/// </para>
/// <para>
/// <see cref="Capture(Exception)"/> は必ず false を返し、例外を外へ出さない（true を返すとフィルターの
/// catch が元の失敗を飲み込み、例外を出すと元の失敗が別の例外に置き換わる）。
/// </para>
/// <para>
/// イベントの購読（<see cref="Track(Window)"/>）は受け身で、Handled も変えない。
/// </para>
/// </remarks>
internal static class GuiFailureDiagnostics
{
    /// <summary>記録を追記するファイル（%TEMP% 直下）</summary>
    internal static readonly string LogPath = Path.Combine(
        Path.GetTempPath(),
        "QuickER.Tests-gui-failure-state.log"
    );

    /// <summary>時系列として残すイベントの件数の上限（古いものから捨てる）</summary>
    private const int TimelineCapacity = 200;

    /// <summary>記録に載せる時系列の件数（直前のものから）</summary>
    private const int TimelineReportCount = 60;

    /// <summary>値の表記を切り詰める長さ</summary>
    private const int ValueTextLimit = 80;

    /// <summary>静的フィールドを記録するビヘイビア（製品は変えず、リフレクションで読むだけ）</summary>
    private static readonly Type[] BehaviorTypes =
    [
        typeof(CanvasViewportBehavior),
        typeof(DragBehavior),
        typeof(RubberBandBehavior),
    ];

    /// <summary>このスレッドの実行の文脈（<see cref="Enter"/> で設定）</summary>
    [ThreadStatic]
    private static RunContext? _current;

    /// <summary>このスレッドが最後に始めた実行の文脈（<see cref="BeginRun"/> の呼び出し元のスレッド）</summary>
    [ThreadStatic]
    private static RunContext? _lastRunOnCaller;

    /// <summary>このスレッドで次に始める実行の記録を、出力先へ書かずに残すか（自己テスト用）</summary>
    [ThreadStatic]
    private static bool _suppressSinksOnCaller;

    /// <summary>このスレッドで最後に作った記録</summary>
    [ThreadStatic]
    private static string? _lastReport;

    /// <summary>このスレッドのイベントの時系列（リングバッファ）</summary>
    [ThreadStatic]
    private static Queue<string>? _timeline;

    /// <summary>このスレッドで追跡しているウィンドウ</summary>
    [ThreadStatic]
    private static List<TrackedWindow>? _trackedWindows;

    /// <summary>時系列の起点（<see cref="Stopwatch.GetTimestamp"/>）</summary>
    [ThreadStatic]
    private static long _origin;

    /// <summary>区画を 1 つ足す（自己テストが、例外を投げる区画を差し込むために使う）</summary>
    [ThreadStatic]
    internal static Action<StringBuilder>? ExtraSectionForTests;

    /// <summary>このスレッドで最後に作った記録</summary>
    internal static string? LastReport => _lastReport;

    /// <summary>このスレッドが最後に始めた実行（STA スレッド側）が作った記録</summary>
    internal static string? LastRunReport => _lastRunOnCaller?.Report;

    /// <summary>実行の文脈（呼び出し元のスレッドで取った出力先と、STA スレッドが残した記録）</summary>
    internal sealed class RunContext
    {
        /// <summary>テストの出力先（STA スレッドへは流れないので、呼び出し元のスレッドで取る）</summary>
        public Xunit.ITestOutputHelper? Output { get; init; }

        /// <summary>テストの表示名</summary>
        public string? TestName { get; init; }

        /// <summary>出力先へ書かずに記録だけを残すか</summary>
        public bool SuppressSinks { get; init; }

        /// <summary>STA スレッドが最後に作った記録</summary>
        public volatile string? Report;
    }

    /// <summary>追跡しているウィンドウ（参照は弱く持つ）</summary>
    private sealed class TrackedWindow
    {
        public required WeakReference<Window> Window { get; init; }

        public required string Label { get; init; }

        public bool Closed { get; set; }
    }

    /// <summary>
    /// 自己テスト用に、このスレッドで次に始める実行の記録を出力先（テストの出力・標準エラー・ファイル）へ
    /// 書かないようにする。破棄で元へ戻す
    /// </summary>
    internal static IDisposable SuppressSinksForTests()
    {
        var previous = _suppressSinksOnCaller;
        _suppressSinksOnCaller = true;
        return new Restore(() => _suppressSinksOnCaller = previous);
    }

    /// <summary>
    /// 実行を始める（STA スレッドを起こす前に、呼び出し元のスレッドで呼ぶ）。テストの出力先と表示名を取る
    /// </summary>
    internal static RunContext BeginRun()
    {
        Xunit.ITestOutputHelper? output = null;
        string? testName = null;

        try
        {
            output = Xunit.TestContext.Current.TestOutputHelper;
            testName = Xunit.TestContext.Current.Test?.TestDisplayName;
        }
        catch
        {
            // 取れなくても記録は残す
        }

        var context = new RunContext
        {
            Output = output,
            TestName = testName,
            SuppressSinks = _suppressSinksOnCaller,
        };
        _lastRunOnCaller = context;
        return context;
    }

    /// <summary>STA スレッドの先頭で呼び、このスレッドの文脈・時系列・追跡を新しくする（例外を出さない）</summary>
    internal static void Enter(RunContext context)
    {
        try
        {
            _current = context;
            _timeline = new Queue<string>();
            _trackedWindows = [];
            _origin = Stopwatch.GetTimestamp();
            _lastReport = null;
        }
        catch
        {
            // 診断の失敗をテストへ持ち込まない
        }
    }

    /// <summary>
    /// ウィンドウのアクティブ・キー・マウス・フォーカスのイベントを、このスレッドの時系列へ受け身で記録する
    /// （例外を出さない）
    /// </summary>
    internal static void Track(Window window)
    {
        try
        {
            var tracked = _trackedWindows ??= [];
            var entry = new TrackedWindow
            {
                Window = new WeakReference<Window>(window),
                Label = $"W{tracked.Count + 1}:{window.GetType().Name}",
            };
            tracked.Add(entry);

            var label = entry.Label;
            EventHandler activated = (_, _) => Record(label, "Activated", "");
            EventHandler deactivated = (_, _) => Record(label, "Deactivated", "");
            KeyEventHandler keyDown = (_, e) => Record(label, "PreviewKeyDown", DescribeKey(e));
            KeyEventHandler keyUp = (_, e) => Record(label, "PreviewKeyUp", DescribeKey(e));
            MouseButtonEventHandler mouseDown = (_, e) =>
                Record(label, "PreviewMouseDown", $"{e.ChangedButton} src={Describe(e.Source)}");
            KeyboardFocusChangedEventHandler gotFocus = (_, e) =>
                Record(
                    label,
                    "PreviewGotKeyboardFocus",
                    $"{Describe(e.OldFocus)} -> {Describe(e.NewFocus)}"
                );

            window.Activated += activated;
            window.Deactivated += deactivated;
            window.AddHandler(UIElement.PreviewKeyDownEvent, keyDown, handledEventsToo: true);
            window.AddHandler(UIElement.PreviewKeyUpEvent, keyUp, handledEventsToo: true);
            window.AddHandler(UIElement.PreviewMouseDownEvent, mouseDown, handledEventsToo: true);
            window.AddHandler(
                UIElement.PreviewGotKeyboardFocusEvent,
                gotFocus,
                handledEventsToo: true
            );

            EventHandler? closed = null;
            closed = (_, _) =>
            {
                Record(label, "Closed", "");
                entry.Closed = true;
                window.Activated -= activated;
                window.Deactivated -= deactivated;
                window.RemoveHandler(UIElement.PreviewKeyDownEvent, keyDown);
                window.RemoveHandler(UIElement.PreviewKeyUpEvent, keyUp);
                window.RemoveHandler(UIElement.PreviewMouseDownEvent, mouseDown);
                window.RemoveHandler(UIElement.PreviewGotKeyboardFocusEvent, gotFocus);
                window.Closed -= closed;
            };
            window.Closed += closed;

            Record(label, "Track", $"IsActive={window.IsActive} IsVisible={window.IsVisible}");
        }
        catch
        {
            // 診断の失敗をテストへ持ち込まない
        }
    }

    /// <summary>
    /// 失敗した瞬間の状態を記録して出力先へ書く。<b>必ず false を返し、例外を外へ出さない</b>
    /// （例外フィルターから呼ぶ前提）
    /// </summary>
    internal static bool Capture(Exception ex)
    {
        try
        {
            var report = Describe(ex);
            _lastReport = report;
            var context = _current;

            if (context is not null)
            {
                context.Report = report;
            }

            if (context?.SuppressSinks != true)
            {
                WriteToSinks(report, context);
            }
        }
        catch
        {
            // 元の失敗を置き換えない
        }

        return false;
    }

    /// <summary>失敗した瞬間の状態を文字列にする（区画ごとに失敗を受け止める）</summary>
    private static string Describe(Exception ex)
    {
        var text = new StringBuilder();
        text.AppendLine("==== GUI テストの失敗時の状態 ====");

        Section(
            text,
            "例外",
            builder =>
            {
                var message = ex.Message;
                var newLine = message.IndexOfAny(['\r', '\n']);

                if (newLine >= 0)
                {
                    message = message[..newLine];
                }

                builder.AppendLine($"  {ex.GetType().FullName}: {message}");
                builder.AppendLine(
                    $"  テスト: {_current?.TestName ?? Xunit.TestContext.Current.Test?.TestDisplayName ?? "(不明)"}"
                );
            }
        );

        Section(
            text,
            "スレッド",
            builder =>
            {
                var thread = Thread.CurrentThread;
                builder.AppendLine(
                    $"  id={thread.ManagedThreadId} apartment={thread.GetApartmentState()} "
                        + $"time={DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} elapsed={ElapsedMilliseconds()}ms"
                );
            }
        );

        Section(
            text,
            "入力",
            builder =>
            {
                var focused = Keyboard.FocusedElement;
                var focusedKeyboard = focused is UIElement element
                    ? element.IsKeyboardFocused.ToString()
                    : "-";
                builder.AppendLine(
                    $"  Keyboard.FocusedElement={Describe(focused)} IsKeyboardFocused={focusedKeyboard}"
                );
                builder.AppendLine(
                    $"  Keyboard.PrimaryDevice.ActiveSource={Describe(Keyboard.PrimaryDevice.ActiveSource)}"
                );
                builder.AppendLine($"  Mouse.Captured={Describe(Mouse.Captured)}");

                // 左ボタンの状態を WPF の見え方と実際の状態の両方で書く（ButtonBase は Space の KeyUp で左ボタンが
                // 離れているかを見て Click を出すので、実際に押されていると Click が出ずにキャプチャが残る＝その推測を確かめるため）
                builder.AppendLine(
                    $"  Mouse.LeftButton={Mouse.LeftButton} GetAsyncKeyState(VK_LBUTTON)の最上位ビット={(GetAsyncKeyState(VkLButton) & 0x8000) != 0}"
                );
                builder.AppendLine(
                    $"  InputManager.MostRecentInputDevice={Describe(InputManager.Current.MostRecentInputDevice)}"
                );
            }
        );

        Section(text, "追跡しているウィンドウ", AppendTrackedWindows);
        Section(text, "前面のウィンドウ", AppendForegroundWindow);

        foreach (var type in BehaviorTypes)
        {
            Section(text, $"{type.Name} の静的状態", builder => AppendStaticFields(builder, type));
        }

        Section(text, "イベントの時系列", AppendTimeline);

        var extra = ExtraSectionForTests;

        if (extra is not null)
        {
            Section(text, "追加の区画", extra);
        }

        return text.ToString();
    }

    /// <summary>区画を 1 つ書く。区画の中の失敗は受け止めて「記録に失敗」と書く</summary>
    private static void Section(StringBuilder text, string heading, Action<StringBuilder> body)
    {
        text.AppendLine($"[{heading}]");

        try
        {
            var builder = new StringBuilder();
            body(builder);
            text.Append(builder);
        }
        catch (Exception ex)
        {
            text.AppendLine($"  記録に失敗: {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>このスレッドで追跡しているウィンドウの状態を書く</summary>
    private static void AppendTrackedWindows(StringBuilder builder)
    {
        var tracked = _trackedWindows;

        if (tracked is null || tracked.Count == 0)
        {
            builder.AppendLine("  (なし)");
            return;
        }

        foreach (var entry in tracked)
        {
            if (!entry.Window.TryGetTarget(out var window))
            {
                builder.AppendLine($"  {entry.Label}: (回収済み)");
                continue;
            }

            if (!window.CheckAccess())
            {
                builder.AppendLine($"  {entry.Label}: (別のスレッドのウィンドウ)");
                continue;
            }

            builder.AppendLine(
                $"  {entry.Label}: Title='{window.Title}' IsActive={window.IsActive} "
                    + $"IsVisible={window.IsVisible} IsKeyboardFocusWithin={window.IsKeyboardFocusWithin} "
                    + $"WindowState={window.WindowState} ShowActivated={window.ShowActivated} "
                    + $"IsLoaded={window.IsLoaded} Closed={entry.Closed} "
                    + $"hwnd=0x{new WindowInteropHelper(window).Handle.ToInt64():x}"
            );
        }
    }

    /// <summary>
    /// Win32 の前面のウィンドウを書く。タイトルを書くのはこのプロセスのウィンドウだけで、別のプロセスなら
    /// プロセス名だけを書く（利用者のほかのアプリのタイトル＝ブラウザのタブ名などを、%TEMP% のログや CI の出力へ残さない）
    /// </summary>
    private static void AppendForegroundWindow(StringBuilder builder)
    {
        var handle = GetForegroundWindow();

        if (handle == IntPtr.Zero)
        {
            builder.AppendLine("  (前面のウィンドウなし)");
            return;
        }

        _ = GetWindowThreadProcessId(handle, out var processId);
        var ownProcess = processId == (uint)Environment.ProcessId;
        string owner;

        if (ownProcess)
        {
            var buffer = new char[256];
            var length = GetWindowTextW(handle, buffer, buffer.Length);
            owner = $"title='{new string(buffer, 0, Math.Max(0, length))}'";
        }
        else
        {
            owner = $"process={ProcessNameOf(processId)}";
        }

        builder.AppendLine(
            $"  hwnd=0x{handle.ToInt64():x} pid={processId} このプロセス={ownProcess} {owner}"
        );
    }

    /// <summary>プロセス名を返す（終わっている・読めないプロセスは「不明」）</summary>
    private static string ProcessNameOf(uint processId)
    {
        try
        {
            using var process = Process.GetProcessById((int)processId);
            return process.ProcessName;
        }
        catch
        {
            return "(不明)";
        }
    }

    /// <summary>型の静的フィールドを名前=値で書く（このスレッドの [ThreadStatic] の値を読む）</summary>
    private static void AppendStaticFields(StringBuilder builder, Type type)
    {
        var fields = type.GetFields(
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic
        );
        var parts = new List<string>();

        foreach (var field in fields)
        {
            if (
                field.IsLiteral
                || field.FieldType == typeof(DependencyProperty)
                || field.FieldType == typeof(RoutedEvent)
            )
            {
                continue;
            }

            string value;

            try
            {
                value = Describe(field.GetValue(null));
            }
            catch (Exception ex)
            {
                value = $"(記録に失敗: {ex.GetType().Name})";
            }

            parts.Add($"{field.Name}={value}");
        }

        builder.AppendLine("  " + string.Join(" ", parts));
    }

    /// <summary>このスレッドの時系列の直前の部分を書く</summary>
    private static void AppendTimeline(StringBuilder builder)
    {
        var timeline = _timeline;

        if (timeline is null || timeline.Count == 0)
        {
            builder.AppendLine("  (なし)");
            return;
        }

        foreach (var entry in timeline.Skip(Math.Max(0, timeline.Count - TimelineReportCount)))
        {
            builder.Append("  ").AppendLine(entry);
        }
    }

    /// <summary>時系列へ 1 件足す（上限を超えたら古いものから捨てる）</summary>
    private static void Record(string window, string eventName, string detail)
    {
        try
        {
            var timeline = _timeline ??= new Queue<string>();

            while (timeline.Count >= TimelineCapacity)
            {
                timeline.Dequeue();
            }

            timeline.Enqueue(
                $"[+{ElapsedMilliseconds()}ms] {window}: {eventName} {detail}".TrimEnd()
            );
        }
        catch
        {
            // 診断の失敗をテストへ持ち込まない
        }
    }

    /// <summary>時系列の起点からの経過ミリ秒</summary>
    private static long ElapsedMilliseconds() =>
        _origin == 0 ? 0 : (long)Stopwatch.GetElapsedTime(_origin).TotalMilliseconds;

    /// <summary>キーのイベントを短く書く</summary>
    private static string DescribeKey(KeyEventArgs e) =>
        $"{e.Key} handled={e.Handled} repeat={e.IsRepeat} src={Describe(e.Source)}";

    /// <summary>値を短く書く（要素は型と Name・別のスレッドの要素は型だけ）</summary>
    private static string Describe(object? value)
    {
        try
        {
            switch (value)
            {
                case null:
                    return "null";
                case FrameworkElement element when element.CheckAccess():
                    return string.IsNullOrEmpty(element.Name)
                        ? element.GetType().Name
                        : $"{element.GetType().Name}({element.Name})";
                case System.Windows.Threading.DispatcherObject dispatcherObject:
                    return dispatcherObject.GetType().Name;
                case string text:
                    return Truncate($"'{text}'");
                case IFormattable formattable:
                    return Truncate(formattable.ToString(null, CultureInfo.InvariantCulture));
                case ICollection collection:
                    return $"{value.GetType().Name}(Count={collection.Count})";
                case bool flag:
                    return flag.ToString();
                default:
                    return value.GetType().Name;
            }
        }
        catch (Exception ex)
        {
            return $"(記録に失敗: {ex.GetType().Name})";
        }
    }

    /// <summary>長い表記を切り詰める</summary>
    private static string Truncate(string text) =>
        text.Length <= ValueTextLimit ? text : text[..ValueTextLimit] + "…";

    /// <summary>テストの出力・標準エラー・ファイルへ書く（出力先ごとに失敗を受け止める）</summary>
    private static void WriteToSinks(string report, RunContext? context)
    {
        try
        {
            context?.Output?.WriteLine(report);
        }
        catch
        {
            // テストが終わった後の出力先などは書けない
        }

        try
        {
            Console.Error.WriteLine(report);
        }
        catch
        {
            // 標準エラーへ書けなくてもほかの出力先へは書く
        }

        try
        {
            using var stream = new FileStream(
                LogPath,
                FileMode.Append,
                FileAccess.Write,
                FileShare.ReadWrite
            );
            using var writer = new StreamWriter(stream);
            writer.WriteLine(report);
        }
        catch
        {
            // ファイルへ書けなくてもテストの結果には影響させない
        }
    }

    /// <summary>破棄で元へ戻す</summary>
    private sealed class Restore(Action restore) : IDisposable
    {
        public void Dispose() => restore();
    }

    [DllImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int GetWindowTextW(IntPtr hWnd, [Out] char[] buffer, int maxCount);

    /// <summary>仮想キーコードの左ボタン</summary>
    private const int VkLButton = 0x01;

    [DllImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern short GetAsyncKeyState(int vKey);
}
