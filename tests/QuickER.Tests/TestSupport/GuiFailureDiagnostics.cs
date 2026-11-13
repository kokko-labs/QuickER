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
/// 違反を例外にせず値で返して窓を閉じた後にまとめて表明するエンジン（入力の列のエンジン）は、例外フィルターを通らない。
/// そのエンジンは違反を見つけた瞬間に <see cref="CaptureViolation"/> で状態を取り（区画は同じ・直前の手を足す）、
/// 既知の失敗の一覧と照合して新しい失敗と分かったものだけを <see cref="Publish"/> で出力先へ書く。
/// </para>
/// <para>
/// イベントの購読（<see cref="Track(Window)"/>）と Win32 から届いた入力の観測（<see cref="Enter"/> が張る）は受け身で、
/// Handled も変えず入力も取り消さない。
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

    /// <summary>このスレッドの入力の経路へ、Win32 から届いた入力の観測を張ったか</summary>
    [ThreadStatic]
    private static bool _inputReportsHooked;

    /// <summary>このスレッドに Win32 から届いた入力の直前の部分（リングバッファ）</summary>
    [ThreadStatic]
    private static Queue<string>? _inputReports;

    /// <summary>このスレッドに Win32 から届いた入力の件数（実行の始めから）</summary>
    [ThreadStatic]
    private static int _inputReportCount;

    /// <summary>
    /// 最後に見た前面のウィンドウ（変わったときだけ時系列へ書く）。実行の始めは null＝まだ見ていない。前面が無い（0）
    /// 環境（対話するデスクトップの無い CI のランナー）でも、実行の始めの 1 行は必ず書く
    /// </summary>
    [ThreadStatic]
    private static IntPtr? _lastForeground;

    /// <summary>前面のウィンドウの取得を差し替える（自己検査が、前面の無い環境を作るため。null なら Win32 から読む）</summary>
    [ThreadStatic]
    internal static Func<IntPtr>? ForegroundForTests;

    /// <summary>前面のウィンドウ（差し替えがあればその値）</summary>
    private static IntPtr Foreground() => ForegroundForTests?.Invoke() ?? GetForegroundWindow();

    /// <summary>Win32 から届いた入力を記録に載せる件数（直前のものから）</summary>
    private const int InputReportCapacity = 30;

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
            _inputReports = new Queue<string>();
            _inputReportCount = 0;
            _inputReportKinds = null;
            _lastForeground = null;
            NoteForegroundChange();
            _foreignForegroundChanges = 0;

            if (!_inputReportsHooked)
            {
                // 入力の経路はスレッド（Dispatcher）ごとで、スレッドと一緒に消えるので外さない
                InputManager.Current.PreProcessInput += OnPreProcessInput;
                _inputReportsHooked = true;
            }
        }
        catch
        {
            // 診断の失敗をテストへ持ち込まない
        }
    }

    /// <summary>
    /// 時系列へ 1 行足し、前面のウィンドウが変わっていればそれも書く（エンジンが手ごとに呼ぶ。例外を出さない）
    /// </summary>
    internal static void Note(string text)
    {
        try
        {
            Record("手", text, "");
            NoteForegroundChange();
        }
        catch
        {
            // 診断の失敗をテストへ持ち込まない
        }
    }

    /// <summary>実際のマウスのボタン（<c>GetAsyncKeyState</c> の最上位ビット）を L・R・M の順に短く書く</summary>
    internal static string PhysicalButtons() =>
        $"L={IsPhysicallyDown(VkLButton)} R={IsPhysicallyDown(VkRButton)} M={IsPhysicallyDown(VkMButton)}";

    /// <summary>実際のマウスのボタン（左・右・中）のどれかが押されているか</summary>
    internal static bool AnyPhysicalButtonDown() =>
        IsPhysicallyDown(VkLButton) || IsPhysicallyDown(VkRButton) || IsPhysicallyDown(VkMButton);

    /// <summary>実際のボタンが押されているか</summary>
    private static bool IsPhysicallyDown(int virtualKey) =>
        (GetAsyncKeyState(virtualKey) & 0x8000) != 0;

    /// <summary>前面のウィンドウが前に見たものから変わっていれば時系列へ書く</summary>
    private static void NoteForegroundChange()
    {
        var handle = Foreground();

        if (_lastForeground == handle)
        {
            return;
        }

        _lastForeground = handle;
        var description = DescribeForeground(handle);
        Record("前面", description, "");

        if (description.Contains("このプロセス=False", StringComparison.Ordinal))
        {
            _foreignForegroundChanges++;
        }
    }

    /// <summary>このスレッドで、前面のウィンドウが別のプロセスのウィンドウへ移った回数（実行の始めから）</summary>
    [ThreadStatic]
    private static int _foreignForegroundChanges;

    /// <summary>
    /// このスレッドで、前面のウィンドウが別のプロセスのウィンドウへ移った回数（実行の始めから。<see cref="Note"/> の
    /// たびに見る）。テストの窓が本当の前面でなくなると、Windows がキャプチャを取り消し、合成の入力の結果が変わりうる
    /// </summary>
    internal static int ForeignForegroundChanges => _foreignForegroundChanges;

    /// <summary>
    /// Win32 から届いた入力（WPF の internal の <c>InputReportEventArgs</c> を <c>InputManager.PreviewInputReportEvent</c> で
    /// 流す生のマウス・キーボードの報告）を記録する。テストが流す合成の入力は <see cref="MouseEventArgs"/> /
    /// <see cref="KeyEventArgs"/> を直接流すのでここを通らない。利用者の本物の操作・ウィンドウのアクティブの行き来・
    /// WPF 自身のマウスの同期が通る
    /// </summary>
    private static void OnPreProcessInput(object? sender, PreProcessInputEventArgs e)
    {
        try
        {
            var input = e.StagingItem.Input;

            if (!IsPreviewInputReport(input))
            {
                return;
            }

            var (detail, moveOnly) = DescribeInputReport(
                input
                    .GetType()
                    .GetProperty(
                        "Report",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
                    )
                    ?.GetValue(input)
            );
            var reports = _inputReports ??= new Queue<string>();

            while (reports.Count >= InputReportCapacity)
            {
                reports.Dequeue();
            }

            reports.Enqueue($"[+{ElapsedMilliseconds()}ms] {detail}");
            _inputReportCount++;

            // 移動だけの報告は時系列を埋め尽くすので、区画にだけ残す
            if (!moveOnly)
            {
                Record("Win32", "入力", detail);
            }
        }
        catch
        {
            // 診断の失敗をテストへ持ち込まない
        }
    }

    /// <summary>
    /// 生の入力の報告を短く書き（型・動作・WPF 自身の同期か）、マウスの移動だけの報告か（動作に押下・解放・アクティブ・
    /// ホイールなどを含まない）を返す。キーボードの報告は押したキーを書かない（利用者がほかの入力のつもりで打った文字を
    /// %TEMP% のログや CI の出力へ残さない）
    /// </summary>
    private static (string Detail, bool MoveOnly) DescribeInputReport(object? report)
    {
        if (report is null)
        {
            return ("(報告なし)", false);
        }

        var type = report.GetType();
        var actions = Read(type, report, "Actions");
        var synchronize =
            type.GetField("_isSynchronize", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.GetValue(report)
            is true;
        var moveOnly =
            type.Name == "RawMouseInputReport"
            && actions
                .Split(',', StringSplitOptions.TrimEntries)
                .All(action => action.EndsWith("Move", StringComparison.Ordinal));
        CountInputReport(type.Name, actions, synchronize);
        return (
            $"{type.Name} {actions} mode={Read(type, report, "Mode")}"
                + (synchronize ? " 同期" : ""),
            moveOnly
        );
    }

    /// <summary>このスレッドに Win32 から届いた入力の、種類（報告の型と動作・同期か）ごとの件数</summary>
    [ThreadStatic]
    private static Dictionary<string, int>? _inputReportKinds;

    /// <summary>報告を動作ごとに数える（動作は複数の旗のことがある）</summary>
    private static void CountInputReport(string typeName, string actions, bool synchronize)
    {
        var kinds = _inputReportKinds ??= new Dictionary<string, int>(StringComparer.Ordinal);
        var shortType = typeName.Replace("InputReport", "", StringComparison.Ordinal);

        foreach (var action in actions.Split(',', StringSplitOptions.TrimEntries))
        {
            var key = $"{shortType}.{action}" + (synchronize ? "(同期)" : "");
            kinds[key] = kinds.GetValueOrDefault(key) + 1;
        }
    }

    /// <summary>
    /// このスレッドに実行の始めから Win32 から届いた入力の件数を、種類ごとに 1 行で返す（エンジンが緑の回でもテストの
    /// 出力へ書き、利用者の本物の操作・キャプチャの取り消しが混ざったかを数で見るため）
    /// </summary>
    internal static string InputReportSummary()
    {
        var kinds = _inputReportKinds;

        if (kinds is null || kinds.Count == 0)
        {
            return $"件数={_inputReportCount}";
        }

        return $"件数={_inputReportCount}（"
            + string.Join(
                "・",
                kinds
                    .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                    .Select(pair => $"{pair.Key}={pair.Value}")
            )
            + "）";
    }

    /// <summary>
    /// 入力が生の報告のプレビュー（WPF の internal の <c>InputManager.PreviewInputReportEvent</c>）か。
    /// 型もイベントも internal なので、イベントの名前と持ち主で見分ける
    /// </summary>
    internal static bool IsPreviewInputReport(InputEventArgs? input) =>
        input?.RoutedEvent is { Name: "PreviewInputReport" } routedEvent
        && routedEvent.OwnerType == typeof(InputManager);

    /// <summary>報告のプロパティを文字列で読む（無ければ「?」）</summary>
    private static string Read(Type type, object report, string name) =>
        type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?.GetValue(report)
            ?.ToString()
        ?? "?";

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

            // キャプチャの移り（取った要素・外れた要素。Windows が外したときも LostMouseCapture が来る）
            MouseEventHandler gotCapture = (_, e) =>
                Record(label, "GotMouseCapture", $"src={Describe(e.OriginalSource)}");
            MouseEventHandler lostCapture = (_, e) =>
                Record(
                    label,
                    "LostMouseCapture",
                    $"src={Describe(e.OriginalSource)} now={Describe(Mouse.Captured)}"
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
            window.AddHandler(Mouse.GotMouseCaptureEvent, gotCapture, handledEventsToo: true);
            window.AddHandler(Mouse.LostMouseCaptureEvent, lostCapture, handledEventsToo: true);

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
                window.RemoveHandler(Mouse.GotMouseCaptureEvent, gotCapture);
                window.RemoveHandler(Mouse.LostMouseCaptureEvent, lostCapture);
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
            Report(
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
                },
                recentSteps: null,
                writeToSinks: true
            );
        }
        catch
        {
            // 元の失敗を置き換えない
        }

        return false;
    }

    /// <summary>
    /// 例外にならない表明の失敗（エンジンが違反を値で返し、窓を閉じた後にまとめて表明するもの）の状態を、見つけた瞬間に
    /// その STA スレッドの上で記録して返す（出力先へはまだ書かない）。<b>例外を外へ出さない</b>（記録に失敗したら null）
    /// </summary>
    /// <param name="summary">違反の要約（1 行目が見出しになる）</param>
    /// <param name="recentSteps">直前の手の状態（古い順。無ければ null）</param>
    /// <remarks>
    /// 例外フィルター（<see cref="Capture(Exception)"/>）は窓が開いている間に投げられた例外しか拾わないので、
    /// 値で返す違反は呼び出し側（ハーネス）が、後始末（押しているものを離す・キャプチャを外す）の前にこれを呼ぶ。
    /// 出力先へ書くのは、エンジンが既知の失敗の一覧と照合して新しい失敗と分かったものだけ（<see cref="Publish"/>）。
    /// 既知の失敗（毎回起きる）まで書くと、調べる人が読む記録が埋もれる。
    /// </remarks>
    internal static string? CaptureViolation(
        string summary,
        IReadOnlyCollection<string>? recentSteps
    )
    {
        try
        {
            return Report(
                "表明の失敗",
                builder =>
                {
                    foreach (var line in summary.Split('\n'))
                    {
                        builder.Append("  ").AppendLine(line.TrimEnd('\r'));
                    }
                },
                recentSteps,
                writeToSinks: false
            );
        }
        catch
        {
            // 診断の失敗をテストへ持ち込まない
            return null;
        }
    }

    /// <summary>このスレッドで最後に出力先へ書いた（自己テストでは書く代わりに残した）記録</summary>
    [ThreadStatic]
    private static string? _lastPublished;

    /// <summary>このスレッドで最後に <see cref="Publish"/> した記録</summary>
    internal static string? LastPublished => _lastPublished;

    /// <summary>
    /// <see cref="CaptureViolation"/> が取った記録を、テストのスレッドから出力先（テストの出力・標準エラー・ファイル）へ書く
    /// （<see cref="SuppressSinksForTests"/> の間は書かずに残すだけ。例外を出さない）
    /// </summary>
    internal static void Publish(string report)
    {
        try
        {
            _lastPublished = report;

            if (_suppressSinksOnCaller)
            {
                return;
            }

            Xunit.ITestOutputHelper? output = null;

            try
            {
                output = Xunit.TestContext.Current.TestOutputHelper;
            }
            catch
            {
                // 取れなくてもほかの出力先へは書く
            }

            WriteToSinks(report, new RunContext { Output = output });
        }
        catch
        {
            // 診断の失敗をテストへ持ち込まない
        }
    }

    /// <summary>記録を作り、このスレッドの文脈へ残す（求められれば出力先へも書く）</summary>
    private static string Report(
        string heading,
        Action<StringBuilder> failure,
        IReadOnlyCollection<string>? recentSteps,
        bool writeToSinks
    )
    {
        var report = Describe(heading, failure, recentSteps);
        _lastReport = report;
        var context = _current;

        if (context is not null)
        {
            context.Report = report;
        }

        if (writeToSinks && context?.SuppressSinks != true)
        {
            WriteToSinks(report, context);
        }

        return report;
    }

    /// <summary>失敗した瞬間の状態を文字列にする（区画ごとに失敗を受け止める）</summary>
    private static string Describe(
        string heading,
        Action<StringBuilder> failure,
        IReadOnlyCollection<string>? recentSteps
    )
    {
        var text = new StringBuilder();
        text.AppendLine("==== GUI テストの失敗時の状態 ====");

        Section(
            text,
            heading,
            builder =>
            {
                failure(builder);
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

                // 右・中のボタンも同じく両方で書く（利用者がほかのアプリで押したボタンは、WPF のコントロールが読む状態に混ざる）
                builder.AppendLine(
                    $"  Mouse.RightButton={Mouse.RightButton} Mouse.MiddleButton={Mouse.MiddleButton} 実際のボタン={PhysicalButtons()}"
                );
                builder.AppendLine(
                    $"  InputManager.MostRecentInputDevice={Describe(InputManager.Current.MostRecentInputDevice)}"
                );
            }
        );

        Section(text, "追跡しているウィンドウ", AppendTrackedWindows);
        Section(text, "前面のウィンドウ", AppendForegroundWindow);
        Section(text, "Win32 から届いた入力", AppendInputReports);

        if (recentSteps is not null)
        {
            Section(
                text,
                "直前の手",
                builder =>
                {
                    if (recentSteps.Count == 0)
                    {
                        builder.AppendLine("  (なし)");
                    }

                    foreach (var step in recentSteps)
                    {
                        builder.Append("  ").AppendLine(step);
                    }
                }
            );
        }

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
    private static void AppendForegroundWindow(StringBuilder builder) =>
        builder.Append("  ").AppendLine(DescribeForeground(Foreground()));

    /// <summary>前面のウィンドウを 1 行で書く（タイトルはこのプロセスのウィンドウだけ）</summary>
    private static string DescribeForeground(IntPtr handle)
    {
        if (handle == IntPtr.Zero)
        {
            return "(前面のウィンドウなし)";
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

        return $"hwnd=0x{handle.ToInt64():x} pid={processId} このプロセス={ownProcess} {owner}";
    }

    /// <summary>このスレッドに Win32 から届いた入力の件数と直前の部分を書く</summary>
    private static void AppendInputReports(StringBuilder builder)
    {
        var reports = _inputReports;
        builder.AppendLine($"  {InputReportSummary()}");
        builder.AppendLine($"  直前の {reports?.Count ?? 0} 件:");

        if (reports is null)
        {
            return;
        }

        foreach (var entry in reports)
        {
            builder.Append("  ").AppendLine(entry);
        }
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

    /// <summary>仮想キーコードの右ボタン</summary>
    private const int VkRButton = 0x02;

    /// <summary>仮想キーコードの中ボタン</summary>
    private const int VkMButton = 0x04;

    [DllImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern short GetAsyncKeyState(int vKey);
}
