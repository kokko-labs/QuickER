using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.Gui.Behaviors;

/// <summary>入力の列のエンジンの次元表・網羅の数え方・既知の失敗・台帳の行</summary>
/// <remarks>
/// <para>
/// 1 回の Space の押下（押下から解放まで）を 1 ケースとして、押下の時のフォーカスの対象・その来方・押し方（リピートの回数）・
/// 押下の間に重なった最初の手・Ctrl を押したかの 5 因子を数える。対象 × 来方 × 押し方は 3 因子の組まで網羅する。
/// </para>
/// <para>
/// 数え方は仕様のモデル（<see cref="CanvasInputModel"/>）だけで手の列をなぞる（窓を使わない・乱数を引かない）。
/// </para>
/// </remarks>
internal static class CanvasInputDimensions
{
    /// <summary>押下の時のフォーカスの対象</summary>
    public const string Target = "FocusTarget";

    /// <summary>フォーカスの来方</summary>
    public const string Arrival = "FocusArrival";

    /// <summary>押し方（リピートの回数）</summary>
    public const string Pattern = "SpacePattern";

    /// <summary>押下の間に重なった最初の手</summary>
    public const string Gesture = "Gesture";

    /// <summary>押下の間に Ctrl を押したか</summary>
    public const string Ctrl = "Ctrl";

    /// <summary>乱数の手の列の範囲（既知の失敗の Scope）</summary>
    public const string RandomScope = "canvas-input-random";

    /// <summary>網羅の行の範囲（既知の失敗の Scope）</summary>
    public const string CoverageScope = "canvas-input-coverage";

    /// <summary>次元表</summary>
    public static DimensionTable Table { get; } =
        new(
            EngineIds.DiagramEdit,
            [
                new Factor(
                    Target,
                    Enum.GetValues<CanvasTarget>().Select(t => new Level(t.ToString())).ToList(),
                    "Space を押した時のフォーカスの対象"
                ),
                new Factor(
                    Arrival,
                    Enum.GetValues<FocusArrival>().Select(a => new Level(a.ToString())).ToList(),
                    "フォーカスの来方（キーボード・マウス・別のウィンドウからの復元）"
                ),
                new Factor(
                    Pattern,
                    [new Level("single"), new Level("repeat-1"), new Level("repeat-n")],
                    "押し方（自動リピートの回数）"
                ),
                new Factor(
                    Gesture,
                    [
                        new Level("none"),
                        new Level("middle-drag"),
                        new Level("left-drag"),
                        new Level("wheel"),
                        new Level("tab"),
                    ],
                    "押下の間に重なった最初の手"
                ),
                new Factor(Ctrl, [new Level("off"), new Level("on")], "押下の間に Ctrl を押したか"),
            ],
            [new FactorGroup(Target, Arrival, Pattern)]
        );

    /// <summary>網羅しない組の一覧（網羅の行がほかの全組を流す）</summary>
    public static CoverageGapSet Gaps =>
        CoverageGap.Set(
            EngineIds.DiagramEdit,
            [
                CoverageGap.AcceptedTuple(
                    RandomScope,
                    "canvas-tab-order",
                    CanvasNotInTabOrder,
                    (Target, nameof(CanvasTarget.Canvas)),
                    (Arrival, nameof(FocusArrival.Keyboard))
                ),
                .. new[] { "single", "repeat-1", "repeat-n" }.Select(pattern =>
                    CoverageGap.AcceptedTuple(
                        RandomScope,
                        "canvas-tab-order",
                        CanvasNotInTabOrder,
                        (Target, nameof(CanvasTarget.Canvas)),
                        (Arrival, nameof(FocusArrival.Keyboard)),
                        (Pattern, pattern)
                    )
                ),
            ]
        );

    private const string CanvasNotInTabOrder =
        "MainWindow のタブ順にキャンバス（DiagramScrollViewer）が入らない（どの要素から Tab を押してもキャンバスへ移らないことを実測した）。キーボードの操作でキャンバスへフォーカスが来る経路が製品に無い";

    /// <summary>リピートの回数の水準</summary>
    public static string PatternLevel(int repeats) =>
        repeats switch
        {
            0 => "single",
            1 => "repeat-1",
            _ => "repeat-n",
        };

    /// <summary>手の列を仕様のモデルでなぞり、Space の押下ごとに数える</summary>
    public static void Observe(CoverageCounter counter, IReadOnlyList<CanvasStep> steps)
    {
        var model = new CanvasInputModel();
        CanvasTarget? target = null;
        var arrival = FocusArrival.Mouse;
        var repeats = 0;
        string? gesture = null;
        var ctrl = false;

        foreach (var step in steps)
        {
            if (step.Op == CanvasOp.SpaceDown)
            {
                target = model.Focus;
                arrival = model.Arrival;
                repeats = 0;
                gesture = null;
                ctrl = model.CtrlDown;
            }

            model.Apply(step);

            if (target is null)
            {
                continue;
            }

            switch (step.Op)
            {
                case CanvasOp.SpaceRepeat:
                    repeats += step.Count;
                    break;
                case CanvasOp.CtrlDown:
                    ctrl = true;
                    break;
                case CanvasOp.MiddleDown:
                    gesture ??= "middle-drag";
                    break;
                case CanvasOp.LeftDown:
                    gesture ??= "left-drag";
                    break;
                case CanvasOp.Wheel:
                    gesture ??= "wheel";
                    break;
                case CanvasOp.TabTo:
                    gesture ??= "tab";
                    break;
                case CanvasOp.SpaceUp:
                    counter.Observe(
                        new Dictionary<string, string>(StringComparer.Ordinal)
                        {
                            [Target] = target.Value.ToString(),
                            [Arrival] = arrival.ToString(),
                            [Pattern] = PatternLevel(repeats),
                            [Gesture] = gesture ?? "none",
                            [Ctrl] = ctrl ? "on" : "off",
                        }
                    );
                    target = null;
                    break;
            }
        }
    }

    // ---------- 左ボタンの押下の次元表 ----------

    /// <summary>押下の場所</summary>
    public const string PressSpot = "PressSpot";

    /// <summary>押下の時のパン待機（Space をパンが持って押しているか）</summary>
    public const string PanAtPress = "PanAtPress";

    /// <summary>押下の間に挟んだ最初の手</summary>
    public const string During = "During";

    /// <summary>左ボタンの押下の場所</summary>
    public static IReadOnlyList<CanvasSpot> PressSpots { get; } =
    [
        CanvasSpot.Blank,
        CanvasSpot.Card,
        CanvasSpot.CardGrip,
        CanvasSpot.Card2,
        CanvasSpot.Line,
        CanvasSpot.RowHeader,
        CanvasSpot.RowOther,
        CanvasSpot.Outside,
    ];

    /// <summary>押下の間に挟む手の水準</summary>
    public static IReadOnlyList<string> DuringLevels { get; } =
    ["none", "space", "ctrl", "delete", "cancel", "move-out"];

    /// <summary>左ボタンの押下の次元表（押下 1 回を 1 ケース）</summary>
    public static DimensionTable PressTable { get; } =
        new(
            EngineIds.DiagramEdit,
            [
                new Factor(
                    PressSpot,
                    PressSpots.Select(s => new Level(s.ToString())).ToList(),
                    "左ボタンの押下の場所"
                ),
                new Factor(
                    PanAtPress,
                    [new Level("none"), new Level("wait")],
                    "押下の時のパン待機"
                ),
                new Factor(
                    During,
                    DuringLevels.Select(d => new Level(d)).ToList(),
                    "押下の間に挟んだ最初の手"
                ),
            ],
            isValid: assignment =>
                // パン待機（Space をパンが持って押している）の押下の間に、Space の押下は挟めない（もう押している）
                !(
                    assignment.TryGetValue(PanAtPress, out var pan)
                    && pan == "wait"
                    && assignment.TryGetValue(During, out var during)
                    && during == "space"
                )
        );

    /// <summary>押下の網羅しない組の一覧（網羅の行が全組を流す）</summary>
    public static CoverageGapSet PressGaps => CoverageGap.Set(EngineIds.DiagramEdit, []);

    /// <summary>手の列を仕様のモデルでなぞり、左ボタンの押下ごとに数える（ボタン類がキャプチャを持つ押下は数えない）</summary>
    public static void ObservePresses(CoverageCounter counter, IReadOnlyList<CanvasStep> steps)
    {
        var model = new CanvasInputModel();
        CanvasSpot? spot = null;
        var pan = "none";
        string? during = null;

        foreach (var step in steps)
        {
            if (step.Op == CanvasOp.LeftDown && !model.ControlHoldsCapture)
            {
                spot = step.Spot;
                pan = model.SpaceDown && model.Owner == SpaceOwner.Pan ? "wait" : "none";
                during = null;
            }

            model.Apply(step);

            if (spot is not { } pressed)
            {
                continue;
            }

            during ??= step.Op switch
            {
                CanvasOp.SpaceDown => "space",
                CanvasOp.CtrlDown => "ctrl",
                CanvasOp.Delete => "delete",
                CanvasOp.ExternalCancel => "cancel",
                CanvasOp.MoveTo
                    when CanvasSpots.OnCanvas(step.Spot!.Value) != CanvasSpots.OnCanvas(pressed) =>
                    "move-out",
                _ => null,
            };

            if (step.Op == CanvasOp.LeftUp)
            {
                counter.Observe(
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        [PressSpot] = pressed.ToString(),
                        [PanAtPress] = pan,
                        [During] = during ?? "none",
                    }
                );
                spot = null;
            }
        }
    }

    /// <summary>既知の失敗の一覧</summary>
    public static KnownFailureSet KnownFailures { get; } =
        new(EngineIds.DiagramEdit, CanvasInputKnownFailures.Entries);

    /// <summary>台帳の行</summary>
    public static EngineDescriptor Descriptor =>
        new(
            EngineIds.DiagramEdit,
            "CanvasInput",
            "キャンバスの入力の列の状態機械（本物の MainWindow へキー・マウスの手の列を流し、各手の後に不変条件を表明する。Space の押下の次元表と左ボタンの押下の次元表）",
            Table,
            CanvasInputRows.Table.Count
                + CanvasInputRows.Coverage.Count
                + CanvasInputRows.PressCoverage.Count,
            [
                "tests/QuickER.Tests/Gui/Behaviors/CanvasInputDimensions.cs",
                "tests/QuickER.Tests/Gui/Behaviors/CanvasInputGenerator.cs",
                "tests/QuickER.Tests/Gui/Behaviors/CanvasInputHarness.cs",
                "tests/QuickER.Tests/Gui/Behaviors/CanvasInputKnownFailures.cs",
                "tests/QuickER.Tests/Gui/Behaviors/CanvasInputRows.cs",
                "tests/QuickER.Tests/Gui/Behaviors/CanvasInputSteps.cs",
                "tests/QuickER.Tests/Gui/Behaviors/CanvasInputTests.cs",
            ],
            [
                "src/QuickER.Gui/Behaviors/BehaviorInput.cs",
                "src/QuickER.Gui/Behaviors/CanvasViewportBehavior.cs",
                "src/QuickER.Gui/Behaviors/DataGridRowReorderBehavior.cs",
                "src/QuickER.Gui/Behaviors/DragBehavior.cs",
                "src/QuickER.Gui/Behaviors/RubberBandBehavior.cs",
                "src/QuickER.Gui/Behaviors/VisualAncestors.cs",
            ]
        );
}
