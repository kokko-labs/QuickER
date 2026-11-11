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

    // ---------- Space の持ち主の判定の次元表 ----------

    /// <summary>判定の時のフォーカスの対象</summary>
    public const string DecisionTarget = "DecisionTarget";

    /// <summary>判定の種類（新しい押下・判定し直すリピート）</summary>
    public const string DecisionKind = "DecisionKind";

    /// <summary>判定の時にパンの実行中だったか</summary>
    public const string PanningAtDecision = "PanningAtDecision";

    /// <summary>Space の持ち主の判定の次元表（判定 1 回を 1 ケース）</summary>
    /// <remarks>
    /// <para>
    /// ビヘイビアが Space の持ち主を決めるのは、新しい押下と、パンが持っていない押下のリピート（判定し直す）。
    /// Space の押下の次元表は押下の時の対象と押下の後に重なった手を数えるので、「パンが先に始まり、その最中に Tab で
    /// 移った対象で持ち主を決める」順序を数えない（夜間の種でだけ出た形）。この表は判定ごとに、その時の対象・種類・
    /// パンの実行中かを数え、3 因子の組まで網羅する。
    /// </para>
    /// <para>
    /// 組はすべて作れる（パンの実行中の対象はキャンバスなら中ボタンの押下で、ほかは Tab で移る。判定し直すリピートは
    /// テキストボックスで押した Space で作る）ので、網羅しない組の一覧は空。
    /// </para>
    /// </remarks>
    public static DimensionTable DecisionTable { get; } =
        new(
            EngineIds.DiagramEdit,
            [
                new Factor(
                    DecisionTarget,
                    Enum.GetValues<CanvasTarget>().Select(t => new Level(t.ToString())).ToList(),
                    "Space の持ち主を決めた時のフォーカスの対象"
                ),
                new Factor(
                    DecisionKind,
                    [new Level("press"), new Level("repeat")],
                    "判定の種類（新しい押下・パンが持っていない押下のリピート）"
                ),
                new Factor(
                    PanningAtDecision,
                    [new Level("none"), new Level("panning")],
                    "判定の時にパンの実行中（マウスでドラッグしている最中）だったか"
                ),
            ],
            [new FactorGroup(DecisionTarget, DecisionKind, PanningAtDecision)]
        );

    /// <summary>判定の網羅しない組の一覧（網羅の行が全組を流す）</summary>
    public static CoverageGapSet DecisionGaps => CoverageGap.Set(EngineIds.DiagramEdit, []);

    /// <summary>手の列を仕様のモデルでなぞり、Space の持ち主の判定ごとに数える（対象の言えないフォーカスは数えない）</summary>
    public static void ObserveDecisions(CoverageCounter counter, IReadOnlyList<CanvasStep> steps)
    {
        var model = new CanvasInputModel();

        foreach (var step in steps)
        {
            var kind = step.Op switch
            {
                CanvasOp.SpaceDown => "press",
                CanvasOp.SpaceRepeat when model.Owner != SpaceOwner.Pan => "repeat",
                _ => null,
            };

            if (kind is not null && model.Focus is { } target)
            {
                counter.Observe(
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        [DecisionTarget] = target.ToString(),
                        [DecisionKind] = kind,
                        [PanningAtDecision] = model.Panning ? "panning" : "none",
                    }
                );
            }

            model.Apply(step);
        }
    }

    // ---------- 左ボタンの押下の次元表 ----------

    /// <summary>押下の場所</summary>
    public const string PressSpot = "PressSpot";

    /// <summary>押下の時のパン待機（Space をパンが持って押しているか）</summary>
    public const string PanAtPress = "PanAtPress";

    /// <summary>押下の間に挟んだ最初の手（Ctrl の押下・解放は数えない。Ctrl は <see cref="PressCtrl"/> で数える）</summary>
    public const string During = "During";

    /// <summary>押下の時と解放の時の Ctrl（押下の間に Ctrl を押したか）</summary>
    public const string PressCtrl = "PressCtrl";

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
    ["none", "space", "delete", "cancel", "move-out"];

    /// <summary>押下の Ctrl の水準</summary>
    /// <remarks>
    /// <list type="bullet">
    ///   <item>none：押下の時も解放の時も離していて、押下の間にも押さない</item>
    ///   <item>blip：押下の時も解放の時も離していて、押下の間に押して離す</item>
    ///   <item>held：押下の時も解放の時も押している（余白なら追加のラバーバンド、カードなら Ctrl のクリック）</item>
    ///   <item>released：押下の時は押していて、解放の時は離している</item>
    ///   <item>late：押下の時は離していて、解放の時は押している</item>
    /// </list>
    /// ビヘイビアは Ctrl を押下の時（<c>RubberBandBehavior</c> の追加選択・<c>DragBehavior</c> のグループ移動）と
    /// 解放の時（<c>DragBehavior</c> のクリックの切り替え）に読むので、2 つの時点の組を水準にする。
    /// </remarks>
    public static IReadOnlyList<string> CtrlLevels { get; } =
    ["none", "blip", "held", "released", "late"];

    /// <summary>押下の Ctrl の水準（押下の時・解放の時・押下の間に押したか）</summary>
    public static string CtrlLevel(bool atPress, bool atRelease, bool pressedDuring) =>
        (atPress, atRelease) switch
        {
            (true, true) => "held",
            (true, false) => "released",
            (false, true) => "late",
            _ => pressedDuring ? "blip" : "none",
        };

    /// <summary>左ボタンの押下の次元表（押下 1 回を 1 ケース）</summary>
    /// <remarks>
    /// 4 因子の 2 因子の組を網羅する。組はパン待機の押下の間の Space の押下を除いてすべて作れる
    /// （Ctrl の水準はどの場所・パン待機・押下の間の手とも重ねられる）。
    /// </remarks>
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
                new Factor(
                    PressCtrl,
                    CtrlLevels.Select(c => new Level(c)).ToList(),
                    "押下の時と解放の時の Ctrl（押下の間に Ctrl を押したか）"
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
        var ctrlAtPress = false;
        var ctrlDuring = false;

        foreach (var step in steps)
        {
            if (step.Op == CanvasOp.LeftDown && !model.ControlHoldsCapture)
            {
                spot = step.Spot;
                pan = model.SpaceDown && model.Owner == SpaceOwner.Pan ? "wait" : "none";
                during = null;
                ctrlAtPress = model.CtrlDown;
                ctrlDuring = false;
            }

            model.Apply(step);

            if (spot is not { } pressed)
            {
                continue;
            }

            if (step.Op == CanvasOp.CtrlDown)
            {
                ctrlDuring = true;
            }

            during ??= step.Op switch
            {
                CanvasOp.SpaceDown => "space",
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
                        [PressCtrl] = CtrlLevel(ctrlAtPress, model.CtrlDown, ctrlDuring),
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
                + CanvasInputRows.PressCoverage.Count
                + CanvasInputRows.DecisionCoverage.Count,
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
