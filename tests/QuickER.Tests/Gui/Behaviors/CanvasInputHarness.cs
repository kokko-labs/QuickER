using System.Globalization;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using QuickER.Behaviors;
using QuickER.Model;
using QuickER.Services;
using QuickER.Tests.TestSupport.Engines;
using QuickER.UndoRedo;
using QuickER.ViewModels;
using QuickER.Views;
using static QuickER.Tests.TestSupport.WpfApplicationTestSupport;

namespace QuickER.Tests.Gui.Behaviors;

/// <summary>入力の列のエンジンのハーネス：本物の MainWindow へ手を流し、各手の後に不変条件を表明する</summary>
/// <remarks>
/// <para><b>何を本物で流し、何を流さないか</b>（<c>tests/QuickER.Tests/TestSupport/CLAUDE.md</c>「入力の列のエンジン」）：</para>
/// <list type="bullet">
///   <item>キーは <see cref="InputManager.ProcessInput"/> で流す（ウィンドウのトンネル → フォーカス要素 → バブル・クラスハンドラ・
///     KeyBinding・Tab のフォーカス移動まで製品と同じ経路）。自動リピートは <see cref="KeyEventArgs"/> の internal の
///     <c>SetRepeat</c> を反射で立てる</item>
///   <item>文字入力：Space の KeyDown がプレビューでもバブルでも処理済みにならなかったときだけ、" " を
///     <see cref="TextCompositionManager"/> で流す（WPF は処理済みのキーのメッセージを TranslateMessage へ渡さないので、
///     処理済みのキーからは WM_CHAR が生まれない、の写し）</item>
///   <item>マウスは仮想のカーソル（ウィンドウの座標）を持ち、キャプチャの要素か当たり判定の要素を発生元にして
///     プレビューを <see cref="InputManager.ProcessInput"/> で流し、処理済みでなければバブルを続けて流す
///     （作った <see cref="MouseButtonEventArgs"/> を流しても WPF はバブルへ昇格させない。プレビューが処理済みならバブルを
///     流さないのは WPF の入力のイベントの対の決まり）。ビヘイビアが読むボタン・位置・修飾キーは
///     <see cref="BehaviorInput"/> の差し替えで仮想の値を返す</item>
///   <item>流さないもの：<c>Mouse.DirectlyOver</c>・<c>IsMouseOver</c> の追跡、WPF のコントロールが自分で読む実際のボタンの
///     状態（ボタンをマウスで押す手はフォーカスが移るところまで）、OLE のドラッグ＆ドロップのループ</item>
///   <item>アクティブの行き来は本物（別のウィンドウの <see cref="Window.Activate"/> と戻り）</item>
/// </list>
/// <para>
/// 窓は 1 つを全種で使い回す。種の始めに図を差し替え・ズームとスクロールを戻し・先頭のテーブルのカードをクリックし、
/// 前の種の状態（進行中の状態・キャプチャ・フォーカス・選択・Undo の履歴・ズーム・スクロール）が残っていないことを表明する。
/// </para>
/// </remarks>
internal sealed class CanvasInputHarness : IBehaviorInputSource, IDisposable
{
    /// <summary>カードの右端のつかみの幅（<see cref="DragBehavior"/> の仕様の写し。境目の両側を押し・ホバーする）</summary>
    private const double CardGripWidth = 8;

    private static readonly FieldInfo IsPanningField = typeof(CanvasViewportBehavior).GetField(
        "_isPanning",
        BindingFlags.Static | BindingFlags.NonPublic
    )!;

    private static readonly MethodInfo? SetRepeatMethod = typeof(KeyEventArgs).GetMethod(
        "SetRepeat",
        BindingFlags.Instance | BindingFlags.NonPublic
    );

    private readonly MainViewModel _vm;
    private readonly MainWindow _window;
    private readonly ScrollViewer _canvas;
    private readonly FrameworkElement _surface;
    private readonly DataGrid _grid;
    private readonly Button _button;
    private readonly ToggleButton _toggle;
    private readonly Window _other;
    private readonly IDisposable _inputScope;
    private readonly List<string> _activations = [];

    /// <summary>反応ごとの発生元（<see cref="_activations"/> と同じ並び）</summary>
    private readonly List<object?> _activationSources = [];
    private readonly Dictionary<CanvasTarget, UIElement> _predecessors = [];

    private Point _pointer;
    private MouseButtonState _left;
    private MouseButtonState _middle;
    private ModifierKeys _modifiers;
    private bool? _keyDownHandled;
    private bool _otherShown;

    /// <summary>窓を開いたときのミニマップの表示（トグルの対象の手が切り替えるので、種の始めに戻す）</summary>
    private readonly bool _initialMiniMap;

    /// <summary>いまの左ボタンの押下（押していなければ null）</summary>
    private PressState? _press;

    /// <summary>OLE のドラッグ＆ドロップの代わりのドラッグの状態（ドラッグ中でなければ null）</summary>
    private (DependencyObject Source, object Data)? _oleDrag;

    /// <summary>手の途中（読み口の差し替えの呼び出しの中）で見つけた違反</summary>
    private readonly List<(string Invariant, string Detail)> _pending = [];

    /// <summary>窓の部品を引き、読み口の差し替えと観測のハンドラを張る</summary>
    public CanvasInputHarness(MainViewModel vm, MainWindow window)
    {
        _vm = vm;
        _window = window;
        _canvas = (ScrollViewer)window.FindName("DiagramScrollViewer")!;
        _surface = (FrameworkElement)window.FindName("DiagramCanvas")!;
        _grid = (DataGrid)window.FindName("ColumnsDataGrid")!;
        _button = Descendants<Button>(window).Single(b => b.Command == vm.ResetZoomCommand);
        _toggle = Descendants<ToggleButton>(window)
            .Single(t =>
                BindingOperations.GetBinding(t, ToggleButton.IsCheckedProperty)?.Path.Path
                == nameof(MainViewModel.IsMiniMapEnabled)
            );
        _other = new Window
        {
            Width = 100,
            Height = 100,
            ShowInTaskbar = false,
            ShowActivated = false,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -10000,
            Top = -10000,
        };

        // アクティブの行き来の相手も、失敗の記録の時系列へ載せる
        TestSupport.GuiFailureDiagnostics.Track(_other);

        window.AddHandler(ButtonBase.ClickEvent, new RoutedEventHandler(OnClick), true);
        window.AddHandler(ToggleButton.CheckedEvent, new RoutedEventHandler(OnChecked), true);
        window.AddHandler(ToggleButton.UncheckedEvent, new RoutedEventHandler(OnChecked), true);
        window.AddHandler(
            TextBoxBase.TextChangedEvent,
            new TextChangedEventHandler(OnTextChanged),
            true
        );
        _grid.BeginningEdit += OnBeginningEdit;
        InputManager.Current.PostProcessInput += OnPostProcessInput;
        _initialMiniMap = vm.IsMiniMapEnabled;
        _inputScope = BehaviorInput.OverrideForTests(this);
        _savedMouseButtons = TestSupport.MouseButtonKeyState.Capture();
    }

    // ---------- 読み口の差し替え（IBehaviorInputSource） ----------

    public MouseButtonState LeftButton => _left;

    public MouseButtonState MiddleButton => _middle;

    public ModifierKeys Modifiers => _modifiers;

    public Point GetPosition(IInputElement? relativeTo) =>
        relativeTo switch
        {
            null => _pointer,
            UIElement element => _window.TranslatePoint(_pointer, element),
            _ => throw new NotSupportedException(
                $"位置の基準に UIElement でない要素を渡した: {relativeTo.GetType().Name}"
            ),
        };

    /// <summary>
    /// ドラッグ＆ドロップの開始を受け、I7（いまの押下が行ヘッダーで始まり離されていないときだけ、押下 1 回に 1 回、その行のカラムで
    /// 始まる）を確かめてから、OLE のループの代わりのドラッグの状態へ入る（以後の移動はイベントを流さず、解放で Drop を流す）
    /// </summary>
    public DragDropEffects DoDragDrop(
        DependencyObject dragSource,
        object data,
        DragDropEffects allowedEffects
    )
    {
        if (
            _press is not { Spot: CanvasSpot.RowHeader or CanvasSpot.RowOther } press
            || _left != MouseButtonState.Pressed
            || press.DragDropCalls > 0
            || !ReferenceEquals(press.RowItem, data)
        )
        {
            _pending.Add(
                (
                    "I7",
                    $"並べ替えのドラッグが、行ヘッダーで始まった今の押下でないところから始まった（押下 {_press?.Spot.ToString() ?? "なし"}・左 {_left}・この押下で {_press?.DragDropCalls ?? 0} 回目・データが押下の行のカラムか {ReferenceEquals(_press?.RowItem, data)}）"
                )
            );
        }
        else
        {
            Exercised.Add("I7:並べ替えの開始");
        }

        if (_press is not null)
        {
            _press.DragDropCalls++;
        }

        _oleDrag = (dragSource, data);
        return DragDropEffects.None;
    }

    /// <summary>実際に走った表明の枝の名前（決まった形の行が、狙った枝を空振りしていないことを数える）</summary>
    public HashSet<string> Exercised { get; } = new(StringComparer.Ordinal);

    /// <summary>決まった形の行で必ず走る表明の枝</summary>
    public static IReadOnlyList<string> RequiredExercises { get; } =
    [
        "I2:パンの実行中",
        "I3:キーボードで移ったボタン類の解放",
        "I3:パン待機のカーソル",
        "I3:パンの実行中のカーソル",
        "I6:押下の無い取り消し",
        "I6:グループのドラッグ",
        "I6:グループのクリック",
        "I6:グループの取り消し",
        "I5:押下の後のパン待機の移動",
        "I6:ドラッグ",
        "I6:クリック",
        "I6:リサイズ",
        "I6:ドラッグの取り消し",
        "I6:押下の取り消し",
        "I6:並べ替えの落とし込み",
        "I7:ラバーバンドの開始",
        "I7:並べ替えの開始",
        "I8:ズーム",
        "I9:つかみのホバー",
        "I9:内側のホバー",
        "I9:パン待機のホバー",
        "I10:ラバーバンドの置き換え",
        "I10:ラバーバンドの追加",
        "I10:余白のクリックの解除",
        "I10:余白の Ctrl クリック",
        "I10:カードのクリック",
        "I10:カードの Ctrl クリック",
        "I10:カードのドラッグ",
    ];

    /// <summary>失敗の記録へ載せる直前の手の数</summary>
    private const int RecentStepCapacity = 30;

    /// <summary>直前の手ごとの状態（古い順。違反を見つけたら失敗の記録へ載せる）</summary>
    private readonly Queue<string> _recentSteps = new();

    /// <summary>ハーネスを作る前の、キーの状態の表のマウスのボタン（破棄で戻す）</summary>
    private readonly TestSupport.MouseButtonKeyState.Saved _savedMouseButtons;

    /// <summary>
    /// 入力を流す前に、このスレッドのキーの状態の表のマウスのボタンを「離れている」に固定するか（既定は固定する。
    /// 固定の自己検査が、外すと利用者の押下が混ざることを確かめるためだけに false にする）
    /// </summary>
    internal bool PinsMouseButtons { get; set; } = true;

    /// <summary>
    /// 種の始めと手の始めに、実際のマウスのボタンが離れるまで待つ処理（既定は
    /// <see cref="TestSupport.PhysicalMouseButtons.WaitUntilReleased()"/>。固定の自己検査が、待つ回数を数えるためだけに差し替える）
    /// </summary>
    internal Action WaitForPhysicalRelease { get; set; } =
        TestSupport.PhysicalMouseButtons.WaitUntilReleased;

    /// <summary>
    /// 手を流した直後に実際のマウスのボタンが押されているかを読む処理（既定は
    /// <see cref="TestSupport.GuiFailureDiagnostics.AnyPhysicalButtonDown"/>。名指しの自己検査が、押されていた形を作るためだけに差し替える）
    /// </summary>
    internal Func<bool> PhysicalButtonDown { get; set; } =
        TestSupport.GuiFailureDiagnostics.AnyPhysicalButtonDown;

    /// <summary>
    /// このスレッドのキーの状態の表のマウスのボタンを「離れている」にする。WPF のコントロール（ButtonBase・DataGrid）は
    /// ビヘイビアと違って読み口の差し替えを通らず、実際のボタンをこの表から読むので、利用者がほかのアプリで押したボタンが
    /// 合成の入力に混ざる。エンジンの前提（人が触らない状態）へ戻すために、入力を流すたびに直前で固定する
    /// </summary>
    private void PinMouseButtons()
    {
        if (PinsMouseButtons)
        {
            TestSupport.MouseButtonKeyState.Release();
        }
    }

    /// <summary>流した手の数（このハーネスを作ってから）</summary>
    public int StepCount { get; private set; }

    /// <summary>手を流した直後に、実際のマウスのボタンが押されていた手の数（利用者の本物の操作が混ざったかを数で見る）</summary>
    public int PhysicalButtonSteps { get; private set; }

    // ---------- 1 種の実行 ----------

    /// <summary>手の列を流し、最初に破れた不変条件を返す（破れなければ null）</summary>
    /// <remarks>
    /// 違反はすべてここを通り、後始末の前（窓が開いていて押下・キャプチャが残っている瞬間）に失敗の記録を書く
    /// （<see cref="TestSupport.GuiFailureDiagnostics.CaptureViolation"/>。違反は値で返り、テストが表明するのは窓を閉じた後なので、
    /// 例外フィルターの記録を通らない）。
    /// </remarks>
    public CanvasViolation? Run(IReadOnlyList<CanvasStep> steps)
    {
        _recentSteps.Clear();
        TestSupport.GuiFailureDiagnostics.Note("種の始め");
        _foreignForegroundAtStart = TestSupport.GuiFailureDiagnostics.ForeignForegroundChanges;
        _physicalButtonStepsAtStart = PhysicalButtonSteps;
        var model = new CanvasInputModel();

        // 種の始めのクリックの前にも、実際のボタンが離れるまで待つ（押されていなければ待たない）
        WaitForPhysicalRelease();

        if (BeginSeed() is { } start)
        {
            return WithState(start);
        }

        if (RunSteps(steps, model) is not { } violation)
        {
            return null;
        }

        var recorded = WithState(violation);
        ReleaseAll(model);
        return recorded;
    }

    /// <summary>種の始めの後の手を流し、最初に破れた不変条件を返す</summary>
    private CanvasViolation? RunSteps(IReadOnlyList<CanvasStep> steps, CanvasInputModel model)
    {
        for (var index = 0; index < steps.Count; index++)
        {
            if (Execute(index, steps[index], model) is { } violation)
            {
                return violation;
            }
        }

        return model.Quiescent
            ? null
            : new(
                "Shape",
                -1,
                null,
                model.Focus,
                "手の列がキーかボタンを押したまま終わった（生成器の誤り）"
            );
    }

    /// <summary>違反を見つけた瞬間の状態を記録し、違反に持たせる（例外を出さない）</summary>
    private CanvasViolation WithState(CanvasViolation violation)
    {
        var named = violation with { Disturbance = Disturbance() };
        return named with
        {
            StateReport = TestSupport.GuiFailureDiagnostics.CaptureViolation(
                $"{named}\n署名 {named.Signature}・ハーネスのキャプチャ {Describe(Mouse.Captured)}・左 {_left}・中 {_middle}・修飾 {_modifiers}",
                _recentSteps.ToList()
            ),
        };
    }

    /// <summary>この手の列の始めの、前面が別のプロセスへ移った回数</summary>
    private int _foreignForegroundAtStart;

    /// <summary>この手の列の始めの、実際のボタンが押されていた手の数</summary>
    private int _physicalButtonStepsAtStart;

    /// <summary>
    /// この手の列を流す間に見た外からの乱れ（前面が別のプロセスへ移った・実際のボタンが押されていた）を名指しする
    /// （無ければ null）。乱れがあっても違反は赤のまま残し、流し直さない。ボタンは表で「離れている」に固定しているので
    /// WPF のコントロールには見えないが、前面の移りは Windows のキャプチャの取り消しを招き、固定では防げない
    /// </summary>
    private string? Disturbance() =>
        DescribeDisturbance(
            TestSupport.GuiFailureDiagnostics.ForeignForegroundChanges - _foreignForegroundAtStart,
            PhysicalButtonSteps - _physicalButtonStepsAtStart,
            PinsMouseButtons
        );

    /// <summary>外からの乱れの数から、名指しの文を組む（無ければ null。自己検査が文の形を確かめる）</summary>
    /// <param name="foreground">前面が別のプロセスのウィンドウへ移った回数</param>
    /// <param name="physical">実際のマウスのボタンが押されていた手の数</param>
    /// <param name="pinned">キーの状態の表のボタンを離れているに固定していたか</param>
    internal static string? DescribeDisturbance(int foreground, int physical, bool pinned)
    {
        var parts = new List<string>();

        if (foreground > 0)
        {
            parts.Add($"手の列の間に前面が別のプロセスのウィンドウへ {foreground} 回移った");
        }

        if (physical > 0)
        {
            parts.Add(
                $"実際のマウスのボタンが押されていた手 {physical} 個（表は離れているに固定{(pinned ? "" : "していない")}）"
            );
        }

        return parts.Count == 0 ? null : string.Join("・", parts);
    }

    /// <summary>
    /// 既知の失敗の一覧と照合した結果のうち、赤にする失敗（新しい失敗・理由の食い違い）の違反の瞬間の状態を、
    /// 出力先へ書く（テストのスレッドで、表明の前に呼ぶ。既知として数えた失敗は書かない）
    /// </summary>
    public static void PublishStates(KnownReport report)
    {
        foreach (
            var failure in report.Unknown.Concat(
                report.ReasonMismatch.Select(match => match.Failure)
            )
        )
        {
            if (failure.Source is CanvasViolation { StateReport: { } state })
            {
                TestSupport.GuiFailureDiagnostics.Publish(state);
            }
        }
    }

    /// <summary>種の始め：前の種の残りを表明し、図・ズーム・スクロールを戻し、先頭のテーブルのカードをクリックする</summary>
    private CanvasViolation? BeginSeed()
    {
        if (Residue() is { } residue)
        {
            return new("I4", -1, null, CanvasTarget.Canvas, $"前の種の状態が残っている: {residue}");
        }

        _left = MouseButtonState.Released;
        _middle = MouseButtonState.Released;
        _modifiers = ModifierKeys.None;
        _press = null;
        _oleDrag = null;
        _pending.Clear();
        _vm.ReplaceDiagramFromModule(Diagram());
        _vm.Entities[0].X = 40;
        _vm.Entities[0].Y = 40;
        _vm.Entities[1].X = 40;
        _vm.Entities[1].Y = 260;
        _vm.Entities[2].X = 320;
        _vm.Entities[2].Y = 260;
        _vm.ZoomLevel = 1.0;
        _vm.IsMiniMapEnabled = _initialMiniMap;
        _window.UpdateLayout();
        _canvas.ScrollToHorizontalOffset(0);
        _canvas.ScrollToVerticalOffset(0);
        _window.UpdateLayout();
        ClickCard();
        DoEvents();

        var problems = new List<string>();

        if (Residue() is { } after)
        {
            problems.Add(after);
        }

        if (!ReferenceEquals(Keyboard.FocusedElement, _canvas))
        {
            problems.Add($"フォーカスがキャンバスでない（{Describe(Keyboard.FocusedElement)}）");
        }

        if (!ReferenceEquals(_vm.SelectedEntity, _vm.Entities[0]))
        {
            problems.Add("先頭のテーブルが選択されていない");
        }

        if (_vm.UndoRedo.CanUndo || _vm.UndoRedo.CanRedo)
        {
            problems.Add("Undo の履歴が残っている");
        }

        if (_vm.IsMiniMapEnabled != _initialMiniMap)
        {
            problems.Add("ミニマップの表示が戻っていない");
        }

        if (_vm.ZoomLevel != 1.0 || _canvas.HorizontalOffset != 0 || _canvas.VerticalOffset != 0)
        {
            problems.Add(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"ズーム・スクロールが戻っていない（{_vm.ZoomLevel}・{_canvas.HorizontalOffset},{_canvas.VerticalOffset}）"
                )
            );
        }

        return problems.Count == 0
            ? null
            : new(
                "I4",
                -1,
                null,
                CanvasTarget.Canvas,
                "種の始めの状態: " + string.Join("・", problems)
            );
    }

    /// <summary>手を 1 つ流して不変条件を表明する</summary>
    private CanvasViolation? Execute(int index, CanvasStep step, CanvasInputModel model)
    {
        _window.UpdateLayout();
        var panBefore = model.PanActive;
        var ownerBefore = model.Owner;
        var controlHeldBefore = model.ControlHoldsCapture;
        var pressOnControlBefore = model.PressStartedOnControl;
        var zoomBefore = _vm.ZoomLevel;
        var focusedBefore = Keyboard.FocusedElement as ButtonBase;
        var pressedBefore = focusedBefore?.IsPressed;
        var checkedBefore = (focusedBefore as ToggleButton)?.IsChecked;
        var activationCountBefore = _activations.Count;
        var geometryBefore = Geometry();
        var rubberBefore = _vm.IsRubberBandVisible;
        var selectionBefore = _vm.SelectedEntities.ToList();
        var undoBefore = UndoCount();
        var oleBefore = _oleDrag;
        var dropIndexes =
            step.Op == CanvasOp.LeftUp && _oleDrag is { } drag ? DropIndexes(drag.Data) : null;
        var ctrlBefore = (_modifiers & ModifierKeys.Control) == ModifierKeys.Control;
        var bandBefore = (
            _vm.RubberBandX,
            _vm.RubberBandY,
            _vm.RubberBandWidth,
            _vm.RubberBandHeight
        );
        _pending.Clear();

        // I10：押下の間に、選択の期待をモデルで言えない手が重なったら、その押下では選択を表明しない
        if (_press is { } held && step.Op != CanvasOp.LeftDown && !KeepsSelectionPredictable(step))
        {
            held.Disturbed = true;
        }

        // 失敗の記録の時系列へ、手の区切りを書く（手が流すイベントの前に。前面のウィンドウの移りもここで拾う）
        TestSupport.GuiFailureDiagnostics.Note(
            string.Create(CultureInfo.InvariantCulture, $"{index} {step}")
        );

        // 実際のボタンが離れるまで待ってから（押されていなければ待たない）、手の始めにも固定する。表は実際のボタンが
        // 変わると Windows が同期し直すので、押されている間に書いても漏れる。手の中の DoEvents で WPF が読む分も含め、
        // 入力を流す箇所でも毎回固定する
        WaitForPhysicalRelease();
        PinMouseButtons();

        if (Perform(step) is { } shape)
        {
            return new("Shape", index, step, model.Focus, shape);
        }

        model.Apply(step);

        if (step.Op == CanvasOp.LeftDown && _press is { } pressed)
        {
            pressed.CtrlAtPress = ctrlBefore;
            pressed.SelectionBefore = selectionBefore;
            pressed.Disturbed =
                panBefore || controlHeldBefore || model.MiddleDown || _vm.IsRelationshipMode;
        }

        // I10：余白の押下の間の移動が選択面へ届いたら、押下の位置からの距離を控える（ラバーバンドの成立の期待）
        if (
            step.Op is CanvasOp.Move or CanvasOp.MoveTo
            && _press is { Spot: CanvasSpot.Blank } blank
            && HitTarget() is DependencyObject reached
            && IsSelfOrDescendant(reached, _surface)
        )
        {
            blank.SurfaceReach = Math.Max(
                blank.SurfaceReach,
                (_window.TranslatePoint(_pointer, _surface) - blank.SurfaceStart).Length
            );
        }

        // 手ごとの状態は常に直前の分だけ持ち、違反の記録へ載せる（全件でだけまれに落ちる形を、その 1 回で調べられるように）
        while (_recentSteps.Count >= RecentStepCapacity)
        {
            _recentSteps.Dequeue();
        }

        StepCount++;

        if (PhysicalButtonDown())
        {
            PhysicalButtonSteps++;
        }

        _recentSteps.Enqueue(
            string.Create(
                CultureInfo.InvariantCulture,
                $"{index} {step}: pan={CanvasViewportBehavior.IsPanActive} panning={(bool)IsPanningField.GetValue(null)!} drag={DragBehavior.IsInteractionActiveForTests} rubber={RubberBandBehavior.IsSelectionActiveForTests}/{_vm.IsRubberBandVisible} capture={Describe(Mouse.Captured)} focus={Describe(Keyboard.FocusedElement)} undo={UndoCount()} top={TopUndo()?.GetType().Name} sel={string.Join(",", _vm.SelectedEntities.Select(e => e.TableName))} 実際のボタン={TestSupport.GuiFailureDiagnostics.PhysicalButtons()}"
            )
        );

        CanvasViolation Violation(string id, string detail) =>
            new(id, index, step, model.Focus, detail);

        if (_pending.Count > 0)
        {
            return Violation(_pending[0].Invariant, _pending[0].Detail);
        }

        if (
            CheckPointer(
                step,
                panBefore,
                geometryBefore,
                rubberBefore,
                undoBefore,
                oleBefore,
                dropIndexes,
                selectionBefore
            ) is
            { } pointer
        )
        {
            return Violation(pointer.Invariant, pointer.Detail);
        }

        if (
            step.Op == CanvasOp.LeftUp
            && CheckSelection(rubberBefore, bandBefore, ctrlBefore) is { } selection
        )
        {
            return Violation("I10", selection);
        }

        var newActivations = _activations.Skip(activationCountBefore).ToList();

        // 手が狙った形を作ったか（フォーカスの行き先）
        if (step.Op is CanvasOp.TabTo or CanvasOp.ClickFocus && !IsFocused(step.Target!.Value))
        {
            return Violation(
                "Shape",
                $"{step.Target} へフォーカスが移らなかった（{Describe(Keyboard.FocusedElement)}）"
            );
        }

        // I1：パンの間は、フォーカス要素（とほかのコントロール）が押されず、切り替わらず、編集・入力されない
        // Space をコントロール・テキストが持つ押下は、そのコントロールが反応してよい（解放は押していた間の持ち主で見る）。
        // キーボードで移ったボタン類が持つ押下として始まった Space は、マウスの押下の後のリピートでビヘイビアがパン待機へ
        // 入っても（持っていない押下はリピートでも判定し直す）、押下はキャプチャを持つボタン類のもの。ほかの経路のリピートで
        // パン待機になったときは例外にしない
        var owner = step.Op == CanvasOp.SpaceUp ? ownerBefore : model.Owner;
        var spaceToOthers =
            step.IsSpaceKey
            && (
                owner is SpaceOwner.Control or SpaceOwner.Text
                || (controlHeldBefore && pressOnControlBefore)
            );

        if ((panBefore || model.PanActive) && !spaceToOthers && !MouseOnControl(step))
        {
            if (newActivations.Count > 0)
            {
                return Violation(
                    "I1",
                    "パンの間にコントロールが反応した: " + string.Join("・", newActivations)
                );
            }

            // キーボードで移ったボタン自身の押下が Space 以外のキーで取り消され、IsPressed が戻るだけの変化は例外にする
            if (
                focusedBefore is not null
                && ReferenceEquals(focusedBefore, Keyboard.FocusedElement)
                && focusedBefore.IsPressed != pressedBefore
                && !IsCancelledControlPress(
                    controlHeldBefore && pressOnControlBefore,
                    step.IsSpaceKey,
                    controlHeldBefore && !model.ControlHoldsCapture,
                    pressedBefore == true && !focusedBefore.IsPressed,
                    newActivations.Count,
                    (focusedBefore as ToggleButton)?.IsChecked != checkedBefore
                )
            )
            {
                return Violation(
                    "I1",
                    $"パンの間にフォーカスのある {Describe(focusedBefore)} の IsPressed が {pressedBefore} → {focusedBefore.IsPressed}"
                );
            }
        }

        // I2：パンの実行中は ScrollViewer がキャプチャを持ち続け、パンはモデルの言うとおりに続く
        var panning = (bool)IsPanningField.GetValue(null)!;

        if (panning != model.Panning)
        {
            return Violation(
                "I2",
                $"パンの実行中が {panning}（期待 {model.Panning}・キャプチャ {Describe(Mouse.Captured)}）"
            );
        }

        if (panning && !ReferenceEquals(Mouse.Captured, _canvas))
        {
            return Violation("I2", $"パンの実行中にキャプチャが {Describe(Mouse.Captured)}");
        }

        if (panning)
        {
            Exercised.Add("I2:パンの実行中");
        }

        // I3：Space の持ち主が線引きどおり（IsPanActive が期待どおり・キーボードで移ったボタン類は解放で 1 回押される）
        if (CanvasViewportBehavior.IsPanActive != model.PanActive)
        {
            return Violation(
                "I3",
                $"IsPanActive が {CanvasViewportBehavior.IsPanActive}（期待 {model.PanActive}・持ち主 {model.Owner}・来方 {model.Arrival}）"
            );
        }

        // I3：パン待機の間はキャンバスのカーソルが手、パンを始めた手の後は全方向（対象の ScrollViewer にだけ効く変化なので、
        // 付け外しの後に対象が戻ったことも見る）。パンの実行中の Space の押下・解放でもカーソルは全方向のまま
        if (model.PanActive)
        {
            var expectedCursor = model.Panning ? Cursors.ScrollAll : Cursors.Hand;

            if (_canvas.Cursor != expectedCursor)
            {
                return Violation(
                    "I3",
                    $"{(model.Panning ? "パンの実行中" : "パン待機")}のキャンバスのカーソルが {_canvas.Cursor?.ToString() ?? "既定"}（期待 {expectedCursor}）"
                );
            }

            Exercised.Add(model.Panning ? "I3:パンの実行中のカーソル" : "I3:パン待機のカーソル");
        }

        if (
            step.Op == CanvasOp.SpaceUp
            && _expectControlActivation is { } expected
            && !model.Reactivated
        )
        {
            // 数えるのは押していたコントロール自身の反応だけ（PK のチェックの連動で NULL のチェックが外れるなどは数えない）
            var actual = Enumerable
                .Range(activationCountBefore, _activations.Count - activationCountBefore)
                .Count(i =>
                    _activations[i].EndsWith(expected, StringComparison.Ordinal)
                    && ReferenceEquals(_activationSources[i], focusedBefore)
                );
            Exercised.Add("I3:キーボードで移ったボタン類の解放");

            if (actual != 1)
            {
                return Violation(
                    "I3",
                    $"キーボードで移った {expected} が Space の解放で {actual} 回反応した（期待 1）: {string.Join("・", newActivations)}"
                );
            }
        }

        _expectControlActivation =
            model.SpaceDown && model.Owner == SpaceOwner.Control && model.ControlPressIntact
                ? ActivationName(model.Focus)
                : null;

        // I8：Ctrl＋ホイールはズーム、修飾なしのホイールはズームを変えない
        if (step.Op == CanvasOp.Wheel)
        {
            if (model.WheelZooms)
            {
                Exercised.Add("I8:ズーム");
            }

            var expectedZoom = model.WheelZooms
                ? (
                    step.Count > 0
                        ? ViewportCalculator.ZoomInStep(zoomBefore)
                        : ViewportCalculator.ZoomOutStep(zoomBefore)
                )
                : zoomBefore;

            if (_vm.ZoomLevel != expectedZoom)
            {
                return Violation(
                    "I8",
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"ホイールの後のズームが {_vm.ZoomLevel}（期待 {expectedZoom}・Ctrl {model.CtrlDown}）"
                    )
                );
            }
        }

        // I4：操作の終わり（何も押していない）に進行中の状態・キャプチャ・カーソルが残らない
        if (model.Quiescent && Residue() is { } residue)
        {
            return Violation("I4", residue);
        }

        return null;
    }

    /// <summary>
    /// キャンバスの外のコントロールをマウスで押す・離す手か（I1 の対象外：I1 が守るのは Space がコントロールへ届かないことで、
    /// Space を押したままのマウスのクリックでコントロールが反応するのは利用者の操作）
    /// </summary>
    private bool MouseOnControl(CanvasStep step) =>
        step.Op == CanvasOp.ClickFocus
        || (step.Op == CanvasOp.LeftDown && !CanvasSpots.OnCanvas(step.Spot!.Value))
        || (
            step.Op == CanvasOp.LeftUp
            && _lastPress is { } press
            && !CanvasSpots.OnCanvas(press.Spot)
        );

    /// <summary>キーボードで移ったコントロールが Space の解放で反応したときの観測の名前（反応を数えないものは null）</summary>
    private string? _expectControlActivation;

    private static string? ActivationName(CanvasTarget? target) =>
        target switch
        {
            CanvasTarget.Button => "Click:Button",
            CanvasTarget.Toggle => "Checked:ToggleButton",
            CanvasTarget.PkCheckBox => "Checked:CheckBox",
            _ => null,
        };

    // ---------- ポインターの不変条件（I5・I6・I7・I9） ----------

    /// <summary>テーブルのカードの位置・幅</summary>
    private Dictionary<EntityViewModel, (double X, double Y, double Width)> Geometry() =>
        _vm.Entities.ToDictionary(entity => entity, entity => (entity.X, entity.Y, entity.Width));

    /// <summary>ドラッグ・ラバーバンド・並べ替えの手の後の不変条件（破れなければ null）</summary>
    private (string Invariant, string Detail)? CheckPointer(
        CanvasStep step,
        bool panBefore,
        Dictionary<EntityViewModel, (double X, double Y, double Width)> geometryBefore,
        bool rubberBefore,
        int undoBefore,
        (DependencyObject Source, object Data)? oleBefore,
        (int Source, int Target)? dropIndexes,
        IReadOnlyList<EntityViewModel> selectionBefore
    )
    {
        var geometry = Geometry();

        // I5：パンの間（Space の押下中・パンの実行中）は、移動でカードの位置・幅が変わらず、ラバーバンドが始まらない
        if (panBefore && step.Op is CanvasOp.Move or CanvasOp.MoveTo)
        {
            if (_press is not null)
            {
                Exercised.Add("I5:押下の後のパン待機の移動");
            }

            if (
                geometryBefore.FirstOrDefault(pair =>
                    geometry.TryGetValue(pair.Key, out var now) && now != pair.Value
                ) is
                { Key: { } moved } changed
            )
            {
                return (
                    "I5",
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"パンの間の移動で {moved.TableName} が {changed.Value} → {geometry[moved]} に動いた"
                    )
                );
            }

            if (!rubberBefore && _vm.IsRubberBandVisible)
            {
                return ("I5", "パンの間の移動でラバーバンドが始まった");
            }
        }

        // I7：ラバーバンドは、選択面の余白で始まった今の押下からだけ始まる（押下 1 回で 1 回）
        if (!rubberBefore && _vm.IsRubberBandVisible && _press is not { Spot: CanvasSpot.Blank })
        {
            return (
                "I7",
                $"ラバーバンドが、余白で始まった今の押下でないところから始まった（押下 {_press?.Spot.ToString() ?? "なし"}）"
            );
        }

        if (!rubberBefore && _vm.IsRubberBandVisible)
        {
            Exercised.Add("I7:ラバーバンドの開始");
        }

        var undo = UndoCount();

        // 押下の間のキーの手（Delete で別のテーブルを消す・Space でチェックを切り替えるなど）が積んだ履歴は、ドラッグの
        // 積み方に数えない（基準を取り直す）
        if (
            _press is { } held
            && step.Op
                is not (
                    CanvasOp.Move
                    or CanvasOp.MoveTo
                    or CanvasOp.LeftDown
                    or CanvasOp.LeftUp
                    or CanvasOp.ExternalCancel
                )
        )
        {
            held.UndoCount = undo;
            held.Generation = _vm.UndoRedo.ChangeGeneration;
        }

        // I6：取り除いたテーブルの移動を履歴に積まない
        if (
            undo > undoBefore
            && TopUndoEntities() is { } entities
            && entities.FirstOrDefault(e => !_vm.Entities.Contains(e)) is { } removed
        )
        {
            return ("I6", $"図から取り除いた {removed.TableName} の移動を履歴に積んだ");
        }

        // I6：外部の取り消しは、ドラッグの開始時点へ戻し、履歴に積まず、進行中の状態とラバーバンドを残さない
        // I6：外部の取り消しは選択を変えない（範囲選択の途中でも確定しない）。押していないときは何も変えない
        if (step.Op == CanvasOp.ExternalCancel)
        {
            if (!_vm.SelectedEntities.SequenceEqual(selectionBefore))
            {
                return ("I6", "取り消しで選択が変わった");
            }

            if (_press is null)
            {
                Exercised.Add("I6:押下の無い取り消し");

                if (
                    geometry.Count != geometryBefore.Count
                    || geometry.Any(pair =>
                        !geometryBefore.TryGetValue(pair.Key, out var was) || was != pair.Value
                    )
                    || undo != undoBefore
                )
                {
                    return ("I6", "押していないときの取り消しで、位置・幅か履歴が変わった");
                }
            }
        }

        if (step.Op == CanvasOp.ExternalCancel && _press is { } canceled)
        {
            canceled.Canceled = true;
            Exercised.Add(
                canceled.DragBegan
                    ? (canceled.Group ? "I6:グループの取り消し" : "I6:ドラッグの取り消し")
                    : "I6:押下の取り消し"
            );

            if (
                canceled.Start.FirstOrDefault(pair =>
                    geometry.TryGetValue(pair.Key, out var now) && now != pair.Value
                ) is
                { Key: { } notRestored } entry
            )
            {
                return (
                    "I6",
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"取り消しで {notRestored.TableName} が開始時点 {entry.Value} へ戻らない（{geometry[notRestored]}）"
                    )
                );
            }

            if (undo != canceled.UndoCount)
            {
                return ("I6", $"取り消しで履歴が {canceled.UndoCount} → {undo} 件になった");
            }

            if (
                DragBehavior.IsInteractionActiveForTests
                || RubberBandBehavior.IsSelectionActiveForTests
                || _vm.IsRubberBandVisible
            )
            {
                return ("I6", "取り消しの後にドラッグかラバーバンドが残った");
            }
        }

        // I6：並べ替えの落とし込みは、別の行へ落としたときだけ MoveColumnOrderCommand を 1 つ積む
        if (step.Op == CanvasOp.LeftUp && oleBefore is not null && dropIndexes is { } drop)
        {
            var expected =
                drop.Source >= 0 && drop.Target >= 0 && drop.Target != drop.Source ? 1 : 0;
            Exercised.Add(
                expected == 1 ? "I6:並べ替えの落とし込み" : "I6:並べ替えの落とし込み（動かない）"
            );

            if (
                undo - undoBefore != expected
                || (expected == 1 && TopUndo() is not MoveColumnOrderCommand)
            )
            {
                return (
                    "I6",
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"行 {drop.Source} を行 {drop.Target} へ落として履歴が {undo - undoBefore} 件増えた（期待 {expected}・先頭 {TopUndo()?.GetType().Name}）"
                    )
                );
            }
        }

        // I6：カードのドラッグの解放は、閾値以内なら位置を戻して積まず、超えて動いたら 1 つ積み、リサイズは積まない
        if (
            step.Op == CanvasOp.LeftUp
            && oleBefore is null
            && _lastPress is { DragBegan: true, Canceled: false, Entity: { } dragged } press
            && _vm.Entities.Contains(dragged)
        )
        {
            var moved = press.Start.Any(pair =>
                geometry.TryGetValue(pair.Key, out var now)
                && (now.X, now.Y) != (pair.Value.X, pair.Value.Y)
            );
            var added = undo - press.UndoCount;

            Exercised.Add(
                press.Group
                    ? (
                        press.Distance(this) <= 3
                            ? "I6:グループのクリック"
                            : "I6:グループのドラッグ"
                    )
                : press.Spot == CanvasSpot.CardGrip ? "I6:リサイズ"
                : press.Distance(this) <= 3 ? "I6:クリック"
                : moved ? "I6:ドラッグ"
                : "I6:動かないドラッグ"
            );

            if (press.Spot == CanvasSpot.CardGrip)
            {
                if (added != 0)
                {
                    return ("I6", $"リサイズの解放で履歴が {added} 件増えた（期待 0）");
                }

                // 幅は保存の対象なので、変わったら変更世代だけを進める（履歴には積まない）
                var widthChanged = geometry[dragged].Width != press.Start[dragged].Width;
                var advanced = _vm.UndoRedo.ChangeGeneration != press.Generation;

                if (widthChanged != advanced)
                {
                    return (
                        "I6",
                        $"リサイズの解放で、幅が変わったか {widthChanged}・変更世代が進んだか {advanced}"
                    );
                }
            }
            else if (press.Distance(this) <= 3)
            {
                if (added != 0 || moved)
                {
                    return (
                        "I6",
                        $"閾値以内の解放（クリック）で履歴が {added} 件増え、位置が戻ったか {!moved}"
                    );
                }
            }
            else if (
                added != (moved ? 1 : 0)
                || (moved && TopUndo() is not (MoveEntityCommand or GroupMoveEntitiesCommand))
            )
            {
                return (
                    "I6",
                    $"ドラッグの解放で履歴が {added} 件増えた（期待 {(moved ? 1 : 0)}・先頭 {TopUndo()?.GetType().Name}）"
                );
            }
        }

        // I9：押していないときのカードの上のカーソル（右端のつかみはリサイズ・内側は既定・パンの間は既定）
        if (
            step.Op == CanvasOp.MoveTo
            && step.Spot is CanvasSpot.Card or CanvasSpot.CardGrip
            && _left == MouseButtonState.Released
            && _middle == MouseButtonState.Released
            && Card() is { } card
        )
        {
            var expectedCursor =
                step.Spot == CanvasSpot.CardGrip && !CanvasViewportBehavior.IsPanActive
                    ? Cursors.SizeWE
                    : null;
            Exercised.Add(
                CanvasViewportBehavior.IsPanActive ? "I9:パン待機のホバー"
                : step.Spot == CanvasSpot.CardGrip ? "I9:つかみのホバー"
                : "I9:内側のホバー"
            );

            if (card.Cursor != expectedCursor)
            {
                return (
                    "I9",
                    $"{step.Spot} の上のカーソルが {card.Cursor?.ToString() ?? "既定"}（期待 {expectedCursor?.ToString() ?? "既定"}）"
                );
            }
        }

        return null;
    }

    // ---------- 選択の不変条件（I10） ----------

    /// <summary>
    /// 押下の間に重なっても、選択の期待をモデルで言える手か（移動・キャンバスの上の場所への移動・Ctrl・解放）
    /// </summary>
    private static bool KeepsSelectionPredictable(CanvasStep step) =>
        step.Op is CanvasOp.Move or CanvasOp.CtrlDown or CanvasOp.CtrlUp or CanvasOp.LeftUp
        || (step.Op == CanvasOp.MoveTo && CanvasSpots.OnCanvas(step.Spot!.Value));

    /// <summary>
    /// I10：左ボタンの解放の後の選択が、押下の前の選択・押下の時と解放の時の Ctrl・閾値から決まる期待と一致する（破れなければ null）
    /// </summary>
    /// <remarks>
    /// <para>期待は製品の文書から書く（選択の処理を写さない）：</para>
    /// <list type="bullet">
    ///   <item>余白の押下で閾値を超えて動いた（ラバーバンドが成立した）：押下の時に Ctrl なら押下の前の選択 ∪ 矩形に触れたテーブル、
    ///     Ctrl でなければ矩形に触れたテーブルだけ（<c>RubberBandBehavior</c> の remarks「Ctrl 押下の追加選択時のみ Handled に
    ///     し、CanvasClickCommand の全解除から既存選択を保護する」・解放で <c>ApplyRubberBandSelection</c> が交差選択を確定する。
    ///     <c>MainViewModel.ApplyRubberBandSelection</c> の XmlDoc「additive なら既存選択へ追加、false なら置換」
    ///     「外接矩形（X / Y / Width / DisplayHeight）との交差＝触れたら選択。完全包含は要求しない」）</item>
    ///   <item>余白の押下で閾値以内に離した（クリック）：押下の時に Ctrl なら選択は変わらず、Ctrl でなければすべて解除
    ///     （同じ remarks「閾値未満のクリックは CanvasClickCommand の全選択解除に委ねる」・Ctrl のときは全解除から保護する）</item>
    ///   <item>カードの押下で閾値以内に離した（クリック）：解放の時に Ctrl ならそのテーブルの選択を切り替え、Ctrl でなければそのテーブル
    ///     だけを選ぶ（<c>DragBehavior.EndDrag</c> の「Ctrl 押下時はトグル追加／除外、それ以外は単一選択へ委譲する（グループのメンバーを
    ///     Ctrl なしでクリックしたらその 1 個へ絞る）」・<c>MainViewModel.OnEntityClicked</c> の「通常時は単一選択」）</item>
    ///   <item>カードの押下で閾値を超えて動いた（移動）：選択は変わらない（<c>DragBehavior</c> の remarks「移動量が閾値未満ならクリック
    ///     とみなし…選択状態へ切り替える」の裏。移動は履歴を積むだけ）</item>
    /// </list>
    /// <para>
    /// ラバーバンドの成立の期待は、押下の間に選択面へ届いた移動の押下の位置からの距離が閾値（3 px）以上か（<c>RubberBandBehavior</c>
    /// の remarks「閾値を超えて動いた時のみラバーバンドを開始する」）。矩形は解放の直前の VM の矩形を読む。
    /// </para>
    /// <para>
    /// 表明しない押下：<see cref="PressState.Disturbed"/>（パン待機・押下の間の Space・Delete・外部の取り消し・付け外し・
    /// キャンバスの外への移動など）・余白とカード（先頭・2 番目）以外の場所・リサイズ・取り除かれたテーブル。
    /// </para>
    /// </remarks>
    private string? CheckSelection(
        bool rubberBefore,
        (double X, double Y, double Width, double Height) band,
        bool ctrlAtRelease
    )
    {
        if (_lastPress is not { Disturbed: false } press)
        {
            return null;
        }

        var before = press.SelectionBefore;
        IReadOnlyList<EntityViewModel> expected;
        string branch;

        if (press.Spot == CanvasSpot.Blank)
        {
            var expectActive = press.SurfaceReach >= 3;

            if (rubberBefore != expectActive)
            {
                return string.Create(
                    CultureInfo.InvariantCulture,
                    $"余白の押下の解放の前にラバーバンドが成立していたか {rubberBefore}（期待 {expectActive}・押下の位置からの最大の距離 {press.SurfaceReach:F2}）"
                );
            }

            if (expectActive)
            {
                // 「触れたら選択」：外接矩形と矩形が辺で接するだけでも触れたとみなす
                var hit = _vm
                    .Entities.Where(entity =>
                        band.X <= entity.X + entity.Width
                        && band.X + band.Width >= entity.X
                        && band.Y <= entity.Y + entity.DisplayHeight
                        && band.Y + band.Height >= entity.Y
                    )
                    .ToList();
                expected = press.CtrlAtPress ? [.. before.Union(hit)] : hit;
                branch = press.CtrlAtPress
                    ? "I10:ラバーバンドの追加"
                    : "I10:ラバーバンドの置き換え";
            }
            else
            {
                expected = press.CtrlAtPress ? before : [];
                branch = press.CtrlAtPress
                    ? "I10:余白の Ctrl クリック"
                    : "I10:余白のクリックの解除";
            }
        }
        else if (
            press.Spot is CanvasSpot.Card or CanvasSpot.Card2
            && press is { DragBegan: true, Canceled: false, Entity: { } clicked }
            && _vm.Entities.Contains(clicked)
        )
        {
            if (press.Distance(this) <= 3)
            {
                expected = ctrlAtRelease
                    ? before.Contains(clicked)
                        ? [.. before.Where(other => !ReferenceEquals(other, clicked))]
                        : [.. before, clicked]
                    : [clicked];
                branch = ctrlAtRelease ? "I10:カードの Ctrl クリック" : "I10:カードのクリック";
            }
            else
            {
                expected = before;
                branch = "I10:カードのドラッグ";
            }
        }
        else
        {
            return null;
        }

        Exercised.Add(branch);
        var actual = _vm.SelectedEntities;

        if (actual.Count == expected.Count && expected.All(actual.Contains))
        {
            return null;
        }

        static string Names(IEnumerable<EntityViewModel> entities) =>
            "{" + string.Join(",", entities.Select(e => e.TableName)) + "}";

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{branch[4..]}（押下 {press.Spot}・押下の時の Ctrl {press.CtrlAtPress}・解放の時の Ctrl {ctrlAtRelease}）の後の選択が {Names(actual)}（期待 {Names(expected)}・押下の前 {Names(before)}）"
        );
    }

    /// <summary>落とし込みの元と先の行の番号（先が行でなければ -1。列一覧が無ければ null）</summary>
    private (int Source, int Target)? DropIndexes(object data)
    {
        if (_grid.ItemsSource is not System.Collections.IList items)
        {
            return null;
        }

        var target =
            _window.InputHitTest(_pointer) is DependencyObject hit
            && Ancestor<DataGridRow>(hit) is { } row
                ? items.IndexOf(row.Item)
                : -1;
        return (items.IndexOf(data), target);
    }

    private static readonly FieldInfo UndoStackField = typeof(UndoRedoManager).GetField(
        "_undo",
        BindingFlags.Instance | BindingFlags.NonPublic
    )!;

    private Stack<IUndoableCommand> UndoStack() =>
        (Stack<IUndoableCommand>)UndoStackField.GetValue(_vm.UndoRedo)!;

    private int UndoCount() => UndoStack().Count;

    private IUndoableCommand? TopUndo() => UndoStack().TryPeek(out var top) ? top : null;

    /// <summary>履歴の先頭が移動のコマンドなら、移動したテーブル（反射で読む）</summary>
    private IReadOnlyList<EntityViewModel>? TopUndoEntities() =>
        TopUndo() switch
        {
            MoveEntityCommand move =>
            [
                (EntityViewModel)
                    typeof(MoveEntityCommand)
                        .GetField("_entity", BindingFlags.Instance | BindingFlags.NonPublic)!
                        .GetValue(move)!,
            ],
            GroupMoveEntitiesCommand group => (
                (System.Collections.IEnumerable)
                    typeof(GroupMoveEntitiesCommand)
                        .GetField("_moves", BindingFlags.Instance | BindingFlags.NonPublic)!
                        .GetValue(group)!
            )
                .Cast<object>()
                .Select(item => (EntityViewModel)item.GetType().GetField("Item1")!.GetValue(item)!)
                .ToList(),
            _ => null,
        };

    /// <summary>左ボタンの押下 1 回の記録</summary>
    private sealed class PressState
    {
        public required CanvasSpot Spot { get; init; }

        /// <summary>押したカードのテーブル（カードの外なら null）</summary>
        public EntityViewModel? Entity { get; init; }

        /// <summary>押した行のカラム（行ヘッダーの押下）</summary>
        public object? RowItem { get; init; }

        public required Dictionary<
            EntityViewModel,
            (double X, double Y, double Width)
        > Start { get; init; }

        public required Point PointerStart { get; init; }

        /// <summary>押下を流した直後の履歴の件数（押下でフォーカスが移って確定した編集の分を含む）</summary>
        public int UndoCount { get; set; }

        /// <summary>押下を流した直後の変更世代</summary>
        public int Generation { get; set; }

        public required Canvas? CardCanvas { get; init; }

        /// <summary>押下で DragBehavior の移動・リサイズが始まったか</summary>
        public bool DragBegan { get; set; }

        public bool Canceled { get; set; }

        /// <summary>複数選択の中のカードを Ctrl なしで押した（グループ移動の）押下か</summary>
        public bool Group { get; init; }

        public int DragDropCalls { get; set; }

        /// <summary>押下の時に Ctrl を押していたか（I10）</summary>
        public bool CtrlAtPress { get; set; }

        /// <summary>押下の直前の選択（I10）</summary>
        public IReadOnlyList<EntityViewModel> SelectionBefore { get; set; } = [];

        /// <summary>
        /// 選択の期待をモデルで言えない手が重なった押下か（I10 を表明しない：パン待機・パンの実行中・ボタン類のキャプチャ・
        /// リレーションの作成中の押下と、押下の間の Space・Delete・外部の取り消し・付け外し・キャンバスの外への移動など）
        /// </summary>
        public bool Disturbed { get; set; }

        /// <summary>押下の位置の選択面（DiagramCanvas）の座標（余白の押下の I10）</summary>
        public Point SurfaceStart { get; init; }

        /// <summary>押下の間に選択面へ届いた移動の、押下の位置からの最大の距離（選択面の座標・余白の押下の I10）</summary>
        public double SurfaceReach { get; set; }

        /// <summary>押下から今のマウスの位置までの、カードのキャンバスの座標での距離</summary>
        public double Distance(CanvasInputHarness harness) =>
            CardCanvas is null
                ? double.PositiveInfinity
                : (
                    harness._window.TranslatePoint(harness._pointer, CardCanvas)
                    - harness._window.TranslatePoint(PointerStart, CardCanvas)
                ).Length;
    }

    /// <summary>直前に離した押下（解放の手の後の表明が読む）</summary>
    private PressState? _lastPress;

    /// <summary>手を実際の入力として流す（手が組めなければ理由を返す）</summary>
    private string? Perform(CanvasStep step)
    {
        switch (step.Op)
        {
            case CanvasOp.SpaceDown:
                SendKey(Key.Space, down: true, repeat: false);
                return null;
            case CanvasOp.SpaceRepeat:
                for (var count = 0; count < step.Count; count++)
                {
                    SendKey(Key.Space, down: true, repeat: true);
                }

                return null;
            case CanvasOp.SpaceUp:
                SendKey(Key.Space, down: false, repeat: false);
                return null;
            case CanvasOp.CtrlDown:
                _modifiers |= ModifierKeys.Control;
                SendKey(Key.LeftCtrl, down: true, repeat: false);
                return null;
            case CanvasOp.CtrlUp:
                SendKey(Key.LeftCtrl, down: false, repeat: false);
                _modifiers &= ~ModifierKeys.Control;
                return null;
            case CanvasOp.TabTo:
                return TabTo(step.Target!.Value);
            case CanvasOp.ClickFocus:
                return ClickFocus(step.Target!.Value);
            case CanvasOp.Reactivate:
                if (!_otherShown)
                {
                    _other.Show();
                    _otherShown = true;
                }

                _other.Activate();
                DoEvents();
                _window.Activate();
                DoEvents();
                return null;
            case CanvasOp.MiddleDown:
                if (SpotPoint(step.Spot!.Value) is not { } middle)
                {
                    return $"{step.Spot} の場所が見つからない";
                }

                _pointer = middle;
                _middle = MouseButtonState.Pressed;
                MouseButton(System.Windows.Input.MouseButton.Middle, down: true);
                return null;
            case CanvasOp.LeftDown:
                return LeftDown(step.Spot!.Value);
            case CanvasOp.LeftUp:
                _left = MouseButtonState.Released;
                _lastPress = _press;
                _press = null;

                // OLE のドラッグの最中は、解放はマウスのイベントにならず落とし込みになる
                if (_oleDrag is { } drag)
                {
                    _oleDrag = null;
                    Drop(drag.Data);
                    return null;
                }

                MouseButton(System.Windows.Input.MouseButton.Left, down: false);
                return null;
            case CanvasOp.MiddleUp:
                _middle = MouseButtonState.Released;
                MouseButton(System.Windows.Input.MouseButton.Middle, down: false);
                return null;
            case CanvasOp.Move:
                _pointer = new Point(_pointer.X + step.Dx, _pointer.Y + step.Dy);

                if (_oleDrag is null)
                {
                    MouseMove();
                }

                return null;
            case CanvasOp.MoveTo:
                // ボタンを押していない移動（ホバー）は、押下の手と同じく見えない場所をスクロールで見せる。
                // 押している間（ドラッグ・パン・ドラッグ＆ドロップ）はスクロールさせない
                if (
                    SpotPoint(
                        step.Spot!.Value,
                        bringIntoView: _left == MouseButtonState.Released
                            && _middle == MouseButtonState.Released
                            && _oleDrag is null
                    )
                    is not { } target
                )
                {
                    return $"{step.Spot} の場所が見つからない";
                }

                _pointer = target;

                if (_oleDrag is null)
                {
                    MouseMove();
                }

                return null;
            case CanvasOp.Delete:
                SendKey(Key.Delete, down: true, repeat: false);
                SendKey(Key.Delete, down: false, repeat: false);
                return null;
            case CanvasOp.ExternalCancel:
                // 外部変更の入口（MainViewModel.CancelActiveCanvasInteractions）と同じ 2 つ
                DragBehavior.CancelActiveDrag();
                RubberBandBehavior.CancelActiveSelection();
                return null;
            case CanvasOp.Reload:
                return Reload();
            case CanvasOp.Wheel:
                // ホイールはキャンバスの余白の上で回す（ボタンを押していなければ、そこまでマウスを動かす）
                if (_left == MouseButtonState.Released && _middle == MouseButtonState.Released)
                {
                    if (SpotPoint(CanvasSpot.Blank) is not { } blank)
                    {
                        return "ホイールを回す余白が見つからない";
                    }

                    _pointer = blank;
                    MouseMove();
                }

                MouseWheel(step.Count * Mouse.MouseWheelDeltaForOneLine);
                return null;
            default:
                throw new ArgumentOutOfRangeException(nameof(step), step.Op, null);
        }
    }

    // ---------- ポインターの手 ----------

    /// <summary>左ボタンを場所で押す（押下の記録を取る。列一覧の行は先頭のテーブルを選んでいないと無いので、無ければ選ぶ）</summary>
    private string? LeftDown(CanvasSpot spot)
    {
        if (spot is CanvasSpot.RowHeader or CanvasSpot.RowOther && RowHeader(spot) is null)
        {
            if (_modifiers != ModifierKeys.None || CanvasViewportBehavior.IsPanActive)
            {
                return $"{spot} が無い（テーブルを選び直すクリックはキーを押していないときだけ）";
            }

            ClickCard();
        }

        if (SpotPoint(spot) is not { } point)
        {
            return $"{spot} の場所が見つからない";
        }

        _pointer = point;
        var card = spot switch
        {
            CanvasSpot.Card or CanvasSpot.CardGrip => Card(),
            CanvasSpot.Card2 => Card(1),
            _ => null,
        };
        _press = new PressState
        {
            Spot = spot,
            Entity = card?.DataContext as EntityViewModel,
            Group =
                card?.DataContext is EntityViewModel { IsSelected: true }
                && _vm.SelectedEntities.Count >= 2
                && (_modifiers & ModifierKeys.Control) == 0,
            RowItem = spot is CanvasSpot.RowHeader or CanvasSpot.RowOther
                ? Ancestor<DataGridRow>(RowHeader(spot)!)?.Item
                : null,
            Start = Geometry(),
            PointerStart = point,
            SurfaceStart = _window.TranslatePoint(point, _surface),
            CardCanvas = card is null ? null : Ancestor<Canvas>(card),
        };
        _left = MouseButtonState.Pressed;
        MouseButton(System.Windows.Input.MouseButton.Left, down: true);
        _press.DragBegan = DragBehavior.IsInteractionActiveForTests;
        _press.UndoCount = UndoCount();
        _press.Generation = _vm.UndoRedo.ChangeGeneration;
        return null;
    }

    /// <summary>OLE のドラッグの解放：マウスの下の要素へ Drop を流す（WPF の DragEventArgs は internal のコンストラクタで作る）</summary>
    private void Drop(object data)
    {
        if (
            _window.InputHitTest(_pointer) is not UIElement target
            || Ancestor<DataGrid>(target) is not { AllowDrop: true }
        )
        {
            return;
        }

        var args = (DragEventArgs)
            Activator.CreateInstance(
                typeof(DragEventArgs),
                BindingFlags.Instance | BindingFlags.NonPublic,
                binder: null,
                [
                    new DataObject(data.GetType(), data),
                    DragDropKeyStates.None,
                    DragDropEffects.Move,
                    target,
                    _window.TranslatePoint(_pointer, target),
                ],
                CultureInfo.InvariantCulture
            )!;
        PinMouseButtons();
        args.RoutedEvent = DragDrop.PreviewDropEvent;
        target.RaiseEvent(args);

        if (!args.Handled)
        {
            args.RoutedEvent = DragDrop.DropEvent;
            target.RaiseEvent(args);
        }
    }

    /// <summary>キャンバスを親から外して戻す（Unloaded → Loaded）</summary>
    private string? Reload()
    {
        if (_canvas.Parent is not Panel parent)
        {
            return "キャンバスの親がパネルでない";
        }

        var index = parent.Children.IndexOf(_canvas);
        parent.Children.RemoveAt(index);
        _window.UpdateLayout();
        DoEvents();
        parent.Children.Insert(index, _canvas);
        _window.UpdateLayout();
        DoEvents();
        return null;
    }

    // ---------- フォーカスの手 ----------

    /// <summary>直前の要素から Tab を押して対象へ入る（直前の要素は、対象の近くから順に試して見つける）</summary>
    private string? TabTo(CanvasTarget target)
    {
        if (EnsureTarget(target) is not { } element)
        {
            return $"{target} が見つからない";
        }

        IEnumerable<UIElement> candidates = _predecessors.TryGetValue(target, out var cached)
            ? [cached, .. PredecessorCandidates(element)]
            : PredecessorCandidates(element);

        foreach (var candidate in candidates)
        {
            if (!candidate.IsVisible || !Keyboard.Focus(candidate).Equals(candidate))
            {
                continue;
            }

            SendKey(Key.Tab, down: true, repeat: false);
            SendKey(Key.Tab, down: false, repeat: false);

            if (IsFocused(target))
            {
                _predecessors[target] = candidate;
                return null;
            }
        }

        return $"Tab で {target} へ入れる直前の要素が見つからない";
    }

    /// <summary>対象の近くのフォーカスを持てる要素を、木の順で対象の手前から遠い順に並べる</summary>
    private static IEnumerable<UIElement> PredecessorCandidates(UIElement target)
    {
        var tried = new HashSet<UIElement>();
        DependencyObject? scope = VisualTreeHelper.GetParent(target);

        while (scope is not null)
        {
            var focusables = Descendants<UIElement>(scope)
                .Where(e => e.Focusable && e.IsEnabled && e.IsVisible)
                .ToList();
            var index = focusables.IndexOf(target);
            var before = index < 0 ? focusables : focusables.Take(index);

            foreach (var candidate in before.Reverse())
            {
                if (!ReferenceEquals(candidate, target) && tried.Add(candidate))
                {
                    yield return candidate;
                }
            }

            scope = VisualTreeHelper.GetParent(scope);
        }
    }

    /// <summary>マウスのクリックでフォーカスを移す（キャンバスは先頭のテーブルのカードをクリックする）</summary>
    private string? ClickFocus(CanvasTarget target)
    {
        if (target == CanvasTarget.Canvas)
        {
            return ClickCard();
        }

        if (EnsureTarget(target) is not { } element)
        {
            return $"{target} が見つからない";
        }

        var center = element.TranslatePoint(
            new Point(element.RenderSize.Width / 2, element.RenderSize.Height / 2),
            _window
        );

        if (
            _window.InputHitTest(center) is not DependencyObject hit
            || !IsSelfOrDescendant(hit, element)
        )
        {
            return $"{target} の中央を押せない（{Describe(_window.InputHitTest(center))}）";
        }

        _pointer = center;
        _left = MouseButtonState.Pressed;
        MouseButton(System.Windows.Input.MouseButton.Left, down: true);

        // 押下でフォーカスを取るかを、コントロール自身が実際のボタンの状態で決めるもの（TextBox）は、押下の後に移す
        // （WPF がクリックで行う「ウィンドウのトンネル → フォーカス移動」の順は保つ）
        if (!IsFocused(target))
        {
            element.Focus();
        }

        _left = MouseButtonState.Released;
        MouseButton(System.Windows.Input.MouseButton.Left, down: false);
        return null;
    }

    /// <summary>先頭のテーブルのカードをクリックする（選択とキャンバスへのフォーカス）</summary>
    private string? ClickCard()
    {
        if (SpotPoint(CanvasSpot.Card) is not { } point)
        {
            return "先頭のテーブルのカードが見つからない";
        }

        _pointer = point;
        _left = MouseButtonState.Pressed;
        MouseButton(System.Windows.Input.MouseButton.Left, down: true);
        _left = MouseButtonState.Released;
        MouseButton(System.Windows.Input.MouseButton.Left, down: false);
        _window.UpdateLayout();
        return null;
    }

    /// <summary>対象の要素を引く（列一覧と入力欄は先頭のテーブルを選んでいないと無いので、カードをクリックして選ぶ）</summary>
    private UIElement? EnsureTarget(CanvasTarget target)
    {
        if (
            target is CanvasTarget.GridCell or CanvasTarget.PkCheckBox or CanvasTarget.TextBox
            && (!ReferenceEquals(_vm.SelectedEntity, _vm.Entities[0]) || Element(target) is null)
        )
        {
            ClickCard();
        }

        return Element(target);
    }

    /// <summary>対象の今の要素（無ければ null）</summary>
    private UIElement? Element(CanvasTarget target)
    {
        _window.UpdateLayout();

        return target switch
        {
            CanvasTarget.Canvas => _canvas,
            CanvasTarget.Button => _button,
            CanvasTarget.Toggle => _toggle,
            CanvasTarget.GridCell => FirstRowCells()
                .FirstOrDefault(cell => cell.Column is DataGridTextColumn),
            CanvasTarget.PkCheckBox => FirstRowCells()
                .Where(cell => cell.Column?.DisplayIndex == 0)
                .SelectMany(Descendants<CheckBox>)
                .FirstOrDefault(),
            CanvasTarget.TextBox => Descendants<TextBox>(_window)
                .FirstOrDefault(box =>
                    box.IsVisible
                    && BindingOperations.GetBinding(box, TextBox.TextProperty)?.Path.Path
                        == "SelectedEntity.TableName"
                ),
            _ => throw new ArgumentOutOfRangeException(nameof(target), target, null),
        };
    }

    private IEnumerable<DataGridCell> FirstRowCells() =>
        _grid.ItemContainerGenerator.ContainerFromIndex(0) is DataGridRow row
            ? Descendants<DataGridCell>(row)
            : [];

    /// <summary>フォーカスが対象にあるか（キャンバスは ScrollViewer 自身）</summary>
    private bool IsFocused(CanvasTarget target) =>
        Element(target) is { } element && ReferenceEquals(Keyboard.FocusedElement, element);

    // ---------- 入力の送り方 ----------

    private void SendKey(Key key, bool down, bool repeat)
    {
        var source = PresentationSource.FromVisual(_window)!;
        var args = new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, key)
        {
            RoutedEvent = down ? Keyboard.PreviewKeyDownEvent : Keyboard.PreviewKeyUpEvent,
        };

        if (repeat)
        {
            SetRepeat(args);
        }

        _keyDownHandled = null;
        PinMouseButtons();
        InputManager.Current.ProcessInput(args);

        // 処理済みにならなかった Space の押下だけが文字を生む（WM_CHAR の写し）
        if (
            down
            && key == System.Windows.Input.Key.Space
            && !args.Handled
            && _keyDownHandled != true
            && Keyboard.FocusedElement is { } focused
        )
        {
            TextCompositionManager.StartComposition(
                new TextComposition(InputManager.Current, focused, " ")
            );
        }
    }

    private static void SetRepeat(KeyEventArgs args)
    {
        if (SetRepeatMethod is not null)
        {
            SetRepeatMethod.Invoke(args, [true]);
        }
        else
        {
            typeof(KeyEventArgs)
                .GetField("_isRepeat", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(args, true);
        }

        if (!args.IsRepeat)
        {
            throw new InvalidOperationException("自動リピートの印を立てられない");
        }
    }

    private void OnPostProcessInput(object? sender, ProcessInputEventArgs e)
    {
        if (e.StagingItem.Input.RoutedEvent == Keyboard.KeyDownEvent)
        {
            _keyDownHandled = e.StagingItem.Input.Handled;
        }
    }

    private IInputElement HitTarget() =>
        Mouse.Captured ?? _window.InputHitTest(_pointer) ?? _window;

    private void MouseButton(MouseButton button, bool down)
    {
        PinMouseButtons();
        var source = HitTarget();
        var preview = new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, button)
        {
            RoutedEvent = down ? Mouse.PreviewMouseDownEvent : Mouse.PreviewMouseUpEvent,
            Source = source,
        };
        InputManager.Current.ProcessInput(preview);

        if (!preview.Handled)
        {
            InputManager.Current.ProcessInput(
                new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, button)
                {
                    RoutedEvent = down ? Mouse.MouseDownEvent : Mouse.MouseUpEvent,
                    Source = source,
                }
            );
        }
    }

    private void MouseMove()
    {
        PinMouseButtons();
        var source = HitTarget();
        var preview = new MouseEventArgs(Mouse.PrimaryDevice, Environment.TickCount)
        {
            RoutedEvent = Mouse.PreviewMouseMoveEvent,
            Source = source,
        };
        InputManager.Current.ProcessInput(preview);

        if (!preview.Handled)
        {
            InputManager.Current.ProcessInput(
                new MouseEventArgs(Mouse.PrimaryDevice, Environment.TickCount)
                {
                    RoutedEvent = Mouse.MouseMoveEvent,
                    Source = source,
                }
            );
        }
    }

    private void MouseWheel(int delta)
    {
        PinMouseButtons();
        var source = HitTarget();
        var preview = new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, delta)
        {
            RoutedEvent = Mouse.PreviewMouseWheelEvent,
            Source = source,
        };
        InputManager.Current.ProcessInput(preview);

        if (!preview.Handled)
        {
            InputManager.Current.ProcessInput(
                new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, delta)
                {
                    RoutedEvent = Mouse.MouseWheelEvent,
                    Source = source,
                }
            );
        }
    }

    // ---------- 場所 ----------

    /// <summary>キャンバスの上の場所（ウィンドウの座標。見えなければ null）</summary>
    private Point? SpotPoint(CanvasSpot spot, bool bringIntoView = true)
    {
        _window.UpdateLayout();
        var viewport = _canvas
            .TransformToAncestor(_window)
            .TransformBounds(new Rect(0, 0, _canvas.ViewportWidth, _canvas.ViewportHeight));

        switch (spot)
        {
            case CanvasSpot.Card:
            case CanvasSpot.CardGrip:
            case CanvasSpot.Card2:
                if (Card(spot == CanvasSpot.Card2 ? 1 : 0) is not { } card)
                {
                    return null;
                }

                // つかみ（右端の 8 px）の内側と、そのすぐ左（境目の左はリサイズでなく移動。ホバーのカーソルも既定）
                Point Local() =>
                    new(
                        card.ActualWidth - (spot == CanvasSpot.CardGrip ? 3 : CardGripWidth + 3),
                        12
                    );

                var inner = card.TranslatePoint(Local(), _window);

                // 押下の場所が見えていなければスクロールで見せる（押していないときだけ。押下の最中にスクロールさせない）
                if (!viewport.Contains(inner) && bringIntoView)
                {
                    card.BringIntoView(new Rect(Local().X - 20, 0, 40, 30));
                    _window.UpdateLayout();
                    inner = card.TranslatePoint(Local(), _window);
                }

                return HitsWithin(inner, card) ? inner : null;
            case CanvasSpot.Line:
                return Descendants<System.Windows.Shapes.Line>(_surface)
                    .Where(line =>
                        line
                            is {
                                IsVisible: true,
                                IsHitTestVisible: true,
                                DataContext: RelationshipViewModel
                            }
                    )
                    // 線の真ん中にはラベルが重なるので、線の上の何か所かを試す
                    .SelectMany(line =>
                        new[] { 0.3, 0.4, 0.6, 0.7 }.Select(t =>
                            (
                                Line: line,
                                Point: line.TranslatePoint(
                                    new Point(
                                        line.X1 + ((line.X2 - line.X1) * t),
                                        line.Y1 + ((line.Y2 - line.Y1) * t)
                                    ),
                                    _window
                                )
                            )
                        )
                    )
                    .Where(pair =>
                        viewport.Contains(pair.Point) && HitsWithin(pair.Point, pair.Line)
                    )
                    .Select(pair => (Point?)pair.Point)
                    .FirstOrDefault();
            case CanvasSpot.RowHeader:
            case CanvasSpot.RowOther:
                return
                    RowHeader(spot) is { } header
                    && header.TranslatePoint(
                        new Point(header.ActualWidth / 2, header.ActualHeight / 2),
                        _window
                    )
                        is var center
                    && HitsWithin(center, header)
                    ? center
                    : null;
            case CanvasSpot.Outside:
                // 列一覧のすぐ上（プロパティパネルの、列一覧の行でもキャンバスでもない所）
                var above = _grid.TranslatePoint(new Point(30, -6), _window);
                return
                    _window.InputHitTest(above) is DependencyObject outside
                    && !IsSelfOrDescendant(outside, _grid)
                    && !IsSelfOrDescendant(outside, _canvas)
                    && outside is not (ButtonBase or TextBoxBase)
                    ? above
                    : null;
        }

        var surface = _surface
            .TransformToAncestor(_window)
            .TransformBounds(new Rect(_surface.RenderSize));
        var visible = Rect.Intersect(viewport, surface);

        if (visible.IsEmpty)
        {
            return null;
        }

        // 余白は、見えている選択面の内側から探す（端にはミニマップなどの重ね物がある）。網羅の行の移動（右下へ 15〜20 px）の
        // 先も余白であることを確かめる（離す場所が選択面の外になると、別の形＝外での解放になる）
        foreach (var fy in new[] { 0.5, 0.4, 0.6, 0.3, 0.7 })
        {
            foreach (var fx in new[] { 0.6, 0.5, 0.7, 0.4, 0.8 })
            {
                var point = new Point(
                    visible.Left + (visible.Width * fx),
                    visible.Top + (visible.Height * fy)
                );

                if (
                    ReferenceEquals(_window.InputHitTest(point), _surface)
                    && ReferenceEquals(_window.InputHitTest(point + new Vector(20, 15)), _surface)
                )
                {
                    return point;
                }
            }
        }

        return null;
    }

    /// <summary>並びの index 番目のテーブルのカード（先頭が消えたら、残ったテーブルで数える）</summary>
    private EntityCardBorder? Card(int index = 0) =>
        _vm.Entities.Skip(index).FirstOrDefault() is { } entity
            ? Descendants<EntityCardBorder>(_surface)
                .FirstOrDefault(card => ReferenceEquals(card.DataContext, entity))
            : null;

    /// <summary>列一覧の行ヘッダー（RowHeader は先頭の行・RowOther は 2 行目）</summary>
    private DataGridRowHeader? RowHeader(CanvasSpot spot)
    {
        _window.UpdateLayout();
        return
            _grid.ItemContainerGenerator.ContainerFromIndex(spot == CanvasSpot.RowHeader ? 0 : 1)
                is DataGridRow row
            ? Descendants<DataGridRowHeader>(row).FirstOrDefault()
            : null;
    }

    /// <summary>点の当たり判定がその要素の中に落ちるか</summary>
    /// <summary>
    /// I1 の例外：パンの間のフォーカス要素の IsPressed の変化が、キーボードで移ったボタン自身の押下の取り消しか
    /// </summary>
    /// <remarks>
    /// WPF の <see cref="ButtonBase"/> は、Space の押下の間に Space 以外のキー（Tab・Ctrl・Delete）を受けると押下を取り消し、
    /// IsPressed を false へ戻す。これはボタン自身の押下の後始末で、パンがボタンを押したのではない。例外にするのは
    /// 次がすべて揃うときだけ（Click・切り替えを伴う変化は今どおり I1 の違反）。
    /// </remarks>
    /// <param name="pressOwnedByControl">押下が、キーボードで移ったボタンの押下として始まり、ボタンがキャプチャを持っていた</param>
    /// <param name="spaceKey">手が Space のキーか（Space の手による変化は例外にしない）</param>
    /// <param name="cancelledByModel">仕様のモデルが、この手でボタンの押下が取り消されたと言う</param>
    /// <param name="released">IsPressed が true から false へ戻った</param>
    /// <param name="activations">この手のコントロールの反応（Click・切り替え・入力）の数</param>
    /// <param name="checkedChanged">トグルの IsChecked が変わった</param>
    internal static bool IsCancelledControlPress(
        bool pressOwnedByControl,
        bool spaceKey,
        bool cancelledByModel,
        bool released,
        int activations,
        bool checkedChanged
    ) =>
        pressOwnedByControl
        && !spaceKey
        && cancelledByModel
        && released
        && activations == 0
        && !checkedChanged;

    private bool HitsWithin(Point point, DependencyObject element) =>
        _window.InputHitTest(point) is DependencyObject hit && IsSelfOrDescendant(hit, element);

    private static T? Ancestor<T>(DependencyObject node)
        where T : DependencyObject
    {
        for (DependencyObject? current = node; current is not null; current = Parent(current))
        {
            if (current is T typed)
            {
                return typed;
            }
        }

        return null;
    }

    // ---------- 観測 ----------

    /// <summary>コントロールの反応を発生元と一緒に記録する</summary>
    private void Record(string name, object? source)
    {
        _activations.Add(name);
        _activationSources.Add(source);
    }

    private void OnClick(object sender, RoutedEventArgs e) =>
        Record($"Click:{e.OriginalSource.GetType().Name}", e.OriginalSource);

    private void OnChecked(object sender, RoutedEventArgs e) =>
        Record($"Checked:{e.OriginalSource.GetType().Name}", e.OriginalSource);

    /// <summary>フォーカスのある入力欄への文字入力（フォーカスが離れたときの確定の書き換えは数えない）</summary>
    private void OnTextChanged(object sender, TextChangedEventArgs e)
    {
        if (e.OriginalSource is UIElement { IsKeyboardFocused: true } box)
        {
            Record($"Text:{box.GetType().Name}", box);
        }
    }

    private void OnBeginningEdit(object? sender, DataGridBeginningEditEventArgs e) =>
        Record("Edit:DataGridCell", e.Row);

    /// <summary>進行中の状態・キャプチャ・カーソルの残り（無ければ null）</summary>
    private string? Residue()
    {
        var residue = new List<string>();

        if (CanvasViewportBehavior.IsPanActive)
        {
            residue.Add("IsPanActive");
        }

        if ((bool)IsPanningField.GetValue(null)!)
        {
            residue.Add("パンの実行中");
        }

        if (DragBehavior.IsInteractionActiveForTests)
        {
            residue.Add("ドラッグの進行中");
        }

        if (RubberBandBehavior.IsSelectionActiveForTests)
        {
            residue.Add("ラバーバンドの進行中");
        }

        if (Mouse.Captured is not null)
        {
            residue.Add($"キャプチャ {Describe(Mouse.Captured)}");
        }

        if (_canvas.Cursor is not null)
        {
            residue.Add($"キャンバスのカーソル {_canvas.Cursor}");
        }

        return residue.Count == 0 ? null : string.Join("・", residue);
    }

    /// <summary>破れた後の後始末（押しているものを離し、進行中の操作を打ち切る）</summary>
    private void ReleaseAll(CanvasInputModel model)
    {
        if (model.SpaceDown)
        {
            SendKey(Key.Space, down: false, repeat: false);
        }

        if (model.CtrlDown)
        {
            SendKey(Key.LeftCtrl, down: false, repeat: false);
            _modifiers = ModifierKeys.None;
        }

        if (_left == MouseButtonState.Pressed)
        {
            _left = MouseButtonState.Released;
            MouseButton(System.Windows.Input.MouseButton.Left, down: false);
        }

        if (_middle == MouseButtonState.Pressed)
        {
            _middle = MouseButtonState.Released;
            MouseButton(System.Windows.Input.MouseButton.Middle, down: false);
        }

        _oleDrag = null;
        _canvas.Cursor = null;
        _press = null;
        DragBehavior.CancelActiveDrag();
        RubberBandBehavior.CancelActiveSelection();
        Mouse.Capture(null);
        DoEvents();
    }

    public void Dispose()
    {
        try
        {
            InputManager.Current.PostProcessInput -= OnPostProcessInput;
            _grid.BeginningEdit -= OnBeginningEdit;
            _inputScope.Dispose();

            if (_otherShown)
            {
                _other.Close();
            }
        }
        finally
        {
            // キーの状態の表のマウスのボタンを、ハーネスを作る前の値へ戻す（ほかの後始末が投げても戻す）
            _savedMouseButtons.Restore();
        }
    }

    // ---------- 補助 ----------

    private static ErDiagram Diagram() =>
        new ErDiagram
        {
            Entities =
            {
                new Entity
                {
                    TableName = "Alpha",
                    Columns =
                    {
                        new Column
                        {
                            Name = "Id",
                            DataType = "int",
                            IsPrimaryKey = true,
                            IsNullable = false,
                        },
                        new Column { Name = "Name", DataType = "int" },
                    },
                },
                new Entity
                {
                    TableName = "Beta",
                    Columns =
                    {
                        new Column
                        {
                            Name = "Code",
                            DataType = "int",
                            IsPrimaryKey = true,
                            IsNullable = false,
                        },
                        new Column { Name = "AlphaId", DataType = "int" },
                    },
                },
                new Entity
                {
                    TableName = "Gamma",
                    Columns =
                    {
                        new Column
                        {
                            Name = "Key",
                            DataType = "int",
                            IsPrimaryKey = true,
                            IsNullable = false,
                        },
                        new Column { Name = "BetaCode", DataType = "int" },
                    },
                },
            },
        }.WithRelationships();

    private static bool IsSelfOrDescendant(DependencyObject node, DependencyObject ancestor)
    {
        for (DependencyObject? current = node; current is not null; current = Parent(current))
        {
            if (ReferenceEquals(current, ancestor))
            {
                return true;
            }
        }

        return false;
    }

    private static DependencyObject? Parent(DependencyObject node) =>
        node is Visual or System.Windows.Media.Media3D.Visual3D
            ? VisualTreeHelper.GetParent(node)
            : LogicalTreeHelper.GetParent(node);

    private static IEnumerable<T> Descendants<T>(DependencyObject root)
        where T : DependencyObject
    {
        var count = VisualTreeHelper.GetChildrenCount(root);

        for (var index = 0; index < count; index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);

            if (child is T typed)
            {
                yield return typed;
            }

            foreach (var descendant in Descendants<T>(child))
            {
                yield return descendant;
            }
        }
    }

    private static string Describe(object? element) =>
        element switch
        {
            null => "なし",
            FrameworkElement { Name.Length: > 0 } named => $"{named.GetType().Name}#{named.Name}",
            _ => element.GetType().Name,
        };
}

/// <summary>ハーネスの図の補助</summary>
internal static class CanvasInputDiagram
{
    /// <summary>テーブルを並びの順に 1 本ずつリレーションでつなぐ（線の当たり判定と、先頭のテーブルを消した後にも線が残る形）</summary>
    public static ErDiagram WithRelationships(this ErDiagram diagram)
    {
        for (var index = 0; index + 1 < diagram.Entities.Count; index++)
        {
            diagram.Relationships.Add(
                new Relationship
                {
                    SourceEntityId = diagram.Entities[index].Id,
                    TargetEntityId = diagram.Entities[index + 1].Id,
                }
            );
        }

        return diagram;
    }
}

/// <summary>破れた不変条件</summary>
/// <param name="Invariant">不変条件の Id（I1〜I10・手が組めなかった Shape）</param>
/// <param name="Index">手の番号（種の始めは -1）</param>
/// <param name="Step">手（種の始めは null）</param>
/// <param name="Focus">モデルのフォーカスの対象</param>
/// <param name="Detail">中身</param>
internal sealed record CanvasViolation(
    string Invariant,
    int Index,
    CanvasStep? Step,
    CanvasTarget? Focus,
    string Detail
)
{
    /// <summary>
    /// 違反を見つけた瞬間（後始末の前）の窓と入力の状態の記録（<see cref="TestSupport.GuiFailureDiagnostics.CaptureViolation"/>。
    /// 記録に失敗したら null）。新しい失敗と分かったときだけ出力先へ書く（<see cref="CanvasInputHarness.PublishStates"/>）
    /// </summary>
    public string? StateReport { get; init; }

    /// <summary>
    /// 手の列を流す間に見た外からの乱れ（前面の移り・実際のボタンの押下。無ければ null）。違反は赤のまま残し、
    /// 失敗の文に名指しする（署名には入れない）
    /// </summary>
    public string? Disturbance { get; init; }

    /// <summary>既知の失敗の署名（種の番号でなく形で名指す：不変条件・手の種類・フォーカスの対象）</summary>
    public string Signature =>
        $"{Invariant}:{Step?.Op.ToString() ?? "Start"}:{Focus?.ToString() ?? "Other"}";

    public override string ToString() =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"[{Invariant}] 手 {Index} {Step}: {Detail}{(Disturbance is null ? "" : $"（外からの乱れ: {Disturbance}）")}"
        );
}
