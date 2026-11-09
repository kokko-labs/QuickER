using System.Globalization;

namespace QuickER.Tests.Gui.Behaviors;

/// <summary>入力の列の手の種類</summary>
internal enum CanvasOp
{
    /// <summary>Space の押下（自動リピートでない最初の押下）</summary>
    SpaceDown,

    /// <summary>Space の自動リピート（<see cref="CanvasStep.Count"/> 回）</summary>
    SpaceRepeat,

    /// <summary>Space の解放</summary>
    SpaceUp,

    /// <summary>Ctrl の押下</summary>
    CtrlDown,

    /// <summary>Ctrl の解放</summary>
    CtrlUp,

    /// <summary>キーボード（Tab）でフォーカスを対象へ移す</summary>
    TabTo,

    /// <summary>マウスのクリック（押下と解放）でフォーカスを対象へ移す</summary>
    ClickFocus,

    /// <summary>別のウィンドウへ移って戻る（戻ると WPF がフォーカスを復元する）</summary>
    Reactivate,

    /// <summary>中ボタンの押下（キャンバスの上の場所）</summary>
    MiddleDown,

    /// <summary>中ボタンの解放</summary>
    MiddleUp,

    /// <summary>左ボタンの押下（キャンバスの上の場所）</summary>
    LeftDown,

    /// <summary>左ボタンの解放</summary>
    LeftUp,

    /// <summary>マウスの移動（<see cref="CanvasStep.Dx"/>・<see cref="CanvasStep.Dy"/>）</summary>
    Move,

    /// <summary>ホイール（<see cref="CanvasStep.Count"/> が回転の向きと量。正は奥へ）</summary>
    Wheel,

    /// <summary>マウスを場所へ動かす（<see cref="CanvasStep.Spot"/>）</summary>
    MoveTo,

    /// <summary>Delete キーの押下と解放（キャンバスにフォーカスがあれば選択中のテーブルを消す）</summary>
    Delete,

    /// <summary>外部変更の入口と同じ打ち切り（DragBehavior.CancelActiveDrag と RubberBandBehavior.CancelActiveSelection）</summary>
    ExternalCancel,

    /// <summary>キャンバス（DiagramScrollViewer）を親から外して戻す（Unloaded → Loaded）</summary>
    Reload,
}

/// <summary>フォーカスを移す対象（MainWindow の部品）</summary>
internal enum CanvasTarget
{
    /// <summary>図のキャンバス（DiagramScrollViewer）</summary>
    Canvas,

    /// <summary>ツールバーのボタン（ズームを 100% へ戻す）</summary>
    Button,

    /// <summary>ツールバーのトグル（ミニマップの表示）</summary>
    Toggle,

    /// <summary>列一覧の先頭の行の名前のセル</summary>
    GridCell,

    /// <summary>列一覧の先頭の行の PK のチェックボックス</summary>
    PkCheckBox,

    /// <summary>プロパティパネルのテーブル名の入力欄</summary>
    TextBox,
}

/// <summary>キャンバスの上でボタンを押す場所</summary>
internal enum CanvasSpot
{
    /// <summary>テーブルの無い余白</summary>
    Blank,

    /// <summary>先頭のテーブルのカードの内側（右端のつかみの外）</summary>
    Card,

    /// <summary>先頭のテーブルのカードの右端のつかみ（リサイズ）</summary>
    CardGrip,

    /// <summary>2 番目のテーブルのカードの内側（Ctrl のクリックで選択に足して、グループ移動を作る）</summary>
    Card2,

    /// <summary>リレーションの線（当たり判定の太い線）</summary>
    Line,

    /// <summary>列一覧の先頭の行の行ヘッダー（並べ替えのドラッグ元）</summary>
    RowHeader,

    /// <summary>列一覧の 2 行目の行ヘッダー（並べ替えの落とし先）</summary>
    RowOther,

    /// <summary>キャンバスの外で、列一覧の行でもない場所（列一覧のすぐ上のプロパティパネル）</summary>
    Outside,
}

/// <summary>場所の分類</summary>
internal static class CanvasSpots
{
    /// <summary>キャンバスの上の場所か（押下でキャンバスへフォーカスが移り、パンの対象になる）</summary>
    public static bool OnCanvas(CanvasSpot spot) =>
        spot
            is CanvasSpot.Blank
                or CanvasSpot.Card
                or CanvasSpot.CardGrip
                or CanvasSpot.Card2
                or CanvasSpot.Line;
}

/// <summary>入力の列の 1 手</summary>
/// <param name="Op">手の種類</param>
/// <param name="Target">フォーカスを移す対象（TabTo・ClickFocus）</param>
/// <param name="Spot">ボタンを押す場所（LeftDown・MiddleDown）</param>
/// <param name="Count">リピートの回数（SpaceRepeat）・ホイールのノッチ（Wheel・正負）</param>
/// <param name="Dx">移動の横の量（Move・装置に依らない px）</param>
/// <param name="Dy">移動の縦の量（Move）</param>
internal sealed record CanvasStep(
    CanvasOp Op,
    CanvasTarget? Target = null,
    CanvasSpot? Spot = null,
    int Count = 0,
    int Dx = 0,
    int Dy = 0
)
{
    public static CanvasStep SpaceDown { get; } = new(CanvasOp.SpaceDown);

    public static CanvasStep SpaceUp { get; } = new(CanvasOp.SpaceUp);

    public static CanvasStep CtrlDown { get; } = new(CanvasOp.CtrlDown);

    public static CanvasStep CtrlUp { get; } = new(CanvasOp.CtrlUp);

    public static CanvasStep Reactivate { get; } = new(CanvasOp.Reactivate);

    public static CanvasStep MiddleUp { get; } = new(CanvasOp.MiddleUp);

    public static CanvasStep LeftUp { get; } = new(CanvasOp.LeftUp);

    public static CanvasStep Repeat(int count) => new(CanvasOp.SpaceRepeat, Count: count);

    public static CanvasStep TabTo(CanvasTarget target) => new(CanvasOp.TabTo, target);

    public static CanvasStep ClickFocus(CanvasTarget target) => new(CanvasOp.ClickFocus, target);

    public static CanvasStep MiddleDown(CanvasSpot spot) => new(CanvasOp.MiddleDown, Spot: spot);

    public static CanvasStep LeftDown(CanvasSpot spot) => new(CanvasOp.LeftDown, Spot: spot);

    public static CanvasStep Move(int dx, int dy) => new(CanvasOp.Move, Dx: dx, Dy: dy);

    public static CanvasStep Wheel(int notches) => new(CanvasOp.Wheel, Count: notches);

    public static CanvasStep MoveTo(CanvasSpot spot) => new(CanvasOp.MoveTo, Spot: spot);

    public static CanvasStep Delete { get; } = new(CanvasOp.Delete);

    public static CanvasStep ExternalCancel { get; } = new(CanvasOp.ExternalCancel);

    public static CanvasStep Reload { get; } = new(CanvasOp.Reload);

    /// <summary>Space のキーの手か</summary>
    public bool IsSpaceKey => Op is CanvasOp.SpaceDown or CanvasOp.SpaceRepeat or CanvasOp.SpaceUp;

    /// <summary>報告と種の要約の表記（カルチャに依らない）</summary>
    public override string ToString() =>
        Op switch
        {
            CanvasOp.TabTo or CanvasOp.ClickFocus => $"{Op}({Target})",
            CanvasOp.LeftDown or CanvasOp.MiddleDown or CanvasOp.MoveTo => $"{Op}({Spot})",
            CanvasOp.SpaceRepeat or CanvasOp.Wheel => string.Create(
                CultureInfo.InvariantCulture,
                $"{Op}({Count})"
            ),
            CanvasOp.Move => string.Create(CultureInfo.InvariantCulture, $"Move({Dx},{Dy})"),
            _ => Op.ToString(),
        };
}

/// <summary>Space の押下を誰が受け取るか（仕様のモデルの判定）</summary>
internal enum SpaceOwner
{
    /// <summary>押していない</summary>
    None,

    /// <summary>キャンバスのパン（待機に入る）</summary>
    Pan,

    /// <summary>キーボードで移ったボタン類（押下に使う）</summary>
    Control,

    /// <summary>テキスト入力（文字として入る）</summary>
    Text,

    /// <summary>押したまま別のウィンドウへ移った（ビヘイビアは押下を忘れた）</summary>
    Lost,
}

/// <summary>フォーカスがどの操作で来たか</summary>
internal enum FocusArrival
{
    /// <summary>キーボード（Tab）</summary>
    Keyboard,

    /// <summary>マウスの押下</summary>
    Mouse,

    /// <summary>別のウィンドウから戻ったときの復元（どちらの操作か分からない）</summary>
    Restored,
}

/// <summary>入力の列の仕様のモデル（手の列だけから、フォーカス・Space の持ち主・パンの期待を決める）</summary>
/// <remarks>
/// <para>
/// 規則は <c>src/QuickER.Gui/Behaviors/CLAUDE.md</c>「Space のパンは、キーボードで移ったボタン類の Space を横取りしない」と
/// <c>CanvasViewportBehavior</c> の remarks から書く（実装を写さない）：
/// </para>
/// <list type="bullet">
///   <item>Space を横取りしないのは、テキスト入力と、キーボード操作でフォーカスが移ってきた Space で押すコントロールだけ。ほかはパン</item>
///   <item>キーの入力は「直前はキーボード」、マウスの押下は「直前はマウス」で、キーボードで来たという記録も落とす</item>
///   <item>フォーカスが移ったら「直前がキーボードで、経緯が分かっている」ときだけキーボードで来たとする</item>
///   <item>別のウィンドウへ移ると押下を忘れ（パンの実行中でなければパン待機も解く）、戻ってから最初の入力までの復元は経緯不明</item>
///   <item>パンは中ボタンの押下か、パンの持つ Space を押したままの左の押下で始まり、どのボタンの解放でも終わる</item>
///   <item>IsPanActive は「パンの持つ Space を押している間、またはパンの実行中」</item>
/// </list>
/// <para>
/// キャンバスの上の押下はキャンバスへフォーカスを引き取る（<c>CanvasViewportBehavior.OnPreviewMouseDown</c> の remarks）。
/// </para>
/// </remarks>
internal sealed class CanvasInputModel
{
    /// <summary>種の始め（先頭のテーブルのカードをクリックした後）の状態を作る</summary>
    public CanvasInputModel()
    {
        Focus = CanvasTarget.Canvas;
        Arrival = FocusArrival.Mouse;
    }

    /// <summary>フォーカスのある対象（対象の外の要素・フォーカスが外れたときは null）</summary>
    public CanvasTarget? Focus { get; private set; }

    /// <summary>今のフォーカスがどの操作で来たか</summary>
    public FocusArrival Arrival { get; private set; }

    /// <summary>Space を物理的に押しているか</summary>
    public bool SpaceDown { get; private set; }

    /// <summary>押している Space の持ち主</summary>
    public SpaceOwner Owner { get; private set; }

    /// <summary>Ctrl を押しているか</summary>
    public bool CtrlDown { get; private set; }

    /// <summary>左ボタンを押しているか</summary>
    public bool LeftDown { get; private set; }

    /// <summary>中ボタンを押しているか</summary>
    public bool MiddleDown { get; private set; }

    /// <summary>パンの実行中か</summary>
    public bool Panning { get; private set; }

    /// <summary>IsPanActive の期待</summary>
    public bool PanActive => Panning || (SpaceDown && Owner == SpaceOwner.Pan);

    /// <summary>この種で別のウィンドウを行き来したか</summary>
    public bool Reactivated { get; private set; }

    /// <summary>キーも Space もボタンも押していない（操作の終わり）</summary>
    public bool Quiescent => !SpaceDown && !CtrlDown && !LeftDown && !MiddleDown;

    /// <summary>
    /// コントロールが持つ今の Space の押下が、押下から今まで同じ対象に留まり、マウスの押下も挟んでいないか
    /// （解放で 1 回だけ押されるはずの押下）
    /// </summary>
    public bool ControlPressIntact { get; private set; }

    /// <summary>
    /// Space の押下を持つボタン類（ButtonBase）がマウスをキャプチャしているか（押下の間のマウスの手はそのボタンへ行き、
    /// キャンバスへ届かない＝パンもフォーカスの引き取りも起きない）
    /// </summary>
    public bool ControlHoldsCapture { get; private set; }

    /// <summary>今の Space の押下が、キーボードで移ったボタン類（ButtonBase）が持つ押下として始まったか</summary>
    public bool PressStartedOnControl { get; private set; }

    /// <summary>ホイールがズームするか（Ctrl を押していて、ホイールがキャンバスへ届く）</summary>
    public bool WheelZooms => CtrlDown && !ControlHoldsCapture;

    /// <summary>直前の入力がキーボードか（ビヘイビアの記録の写し）</summary>
    private bool _lastInputWasKeyboard;

    /// <summary>非アクティブになってから、まだキーもマウスも受けていないか</summary>
    private bool _originUnknown;

    /// <summary>手を 1 つ当てる</summary>
    public void Apply(CanvasStep step)
    {
        switch (step.Op)
        {
            case CanvasOp.SpaceDown:
                KeyInput();
                SpaceDown = true;
                Owner = SpaceOwner.None;
                Decide();
                PressStartedOnControl = Owner == SpaceOwner.Control && IsButton(Focus);
                break;
            case CanvasOp.SpaceRepeat:
                KeyInput();

                // ビヘイビアが押下を持っていないリピートは、新しい押下と同じく判定し直す
                if (Owner != SpaceOwner.Pan)
                {
                    Decide();
                }

                break;
            case CanvasOp.SpaceUp:
                KeyInput();
                SpaceDown = false;
                Owner = SpaceOwner.None;
                ControlPressIntact = false;
                ControlHoldsCapture = false;
                PressStartedOnControl = false;
                break;
            case CanvasOp.CtrlDown:
                KeyInput();
                CancelControlPress();
                CtrlDown = true;
                break;
            case CanvasOp.CtrlUp:
                KeyInput();
                CtrlDown = false;
                break;
            case CanvasOp.TabTo:
                // 直前の要素から Tab で入るので、同じ対象へ戻る Tab もキーボードで来たことになる
                KeyInput();
                CancelControlPress();
                Focus = step.Target!.Value;
                Arrival = FocusArrival.Keyboard;
                break;
            case CanvasOp.ClickFocus:
                MouseInput();
                FocusOn(step.Target!.Value);
                break;
            case CanvasOp.Reactivate:
                Reactivated = true;
                ControlPressIntact = false;
                ControlHoldsCapture = false;

                if (SpaceDown && Owner == SpaceOwner.Pan)
                {
                    Owner = SpaceOwner.Lost;
                }

                _originUnknown = true;
                Arrival = FocusArrival.Restored;
                break;
            case CanvasOp.MiddleDown:
                MouseInput();
                MiddleDown = true;

                if (!ControlHoldsCapture)
                {
                    FocusOn(CanvasTarget.Canvas);
                    Panning = true;
                }

                break;
            case CanvasOp.LeftDown:
                MouseInput();
                LeftDown = true;

                if (ControlHoldsCapture)
                {
                    break;
                }

                // キャンバスの外（列一覧・プロパティパネル）の押下は、どこへフォーカスが移るかを対象で言えない
                if (!CanvasSpots.OnCanvas(step.Spot!.Value))
                {
                    Focus = null;
                    break;
                }

                FocusOn(CanvasTarget.Canvas);

                if (SpaceDown && Owner == SpaceOwner.Pan)
                {
                    Panning = true;
                }

                break;
            case CanvasOp.MiddleUp:
                MiddleDown = false;
                Panning = false;
                break;
            case CanvasOp.LeftUp:
                LeftDown = false;
                Panning = false;
                break;
            case CanvasOp.Delete:
                KeyInput();
                CancelControlPress();
                break;
            case CanvasOp.Reload:
                // 後始末（Detach）が押下の状態と来方の記録を捨てる。キャンバスは視覚ツリーから外れてフォーカスを失う
                if (SpaceDown && Owner == SpaceOwner.Pan)
                {
                    Owner = SpaceOwner.Lost;
                }

                _lastInputWasKeyboard = false;
                _originUnknown = false;

                if (Arrival == FocusArrival.Keyboard)
                {
                    Arrival = FocusArrival.Mouse;
                }

                if (Focus == CanvasTarget.Canvas)
                {
                    Focus = null;
                }

                break;
            case CanvasOp.Move:
            case CanvasOp.MoveTo:
            case CanvasOp.Wheel:
            case CanvasOp.ExternalCancel:
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(step), step.Op, null);
        }
    }

    /// <summary>Space を押下に使うコントロールか（ボタン類・グリッドのセル）</summary>
    private static bool IsSpaceActivated(CanvasTarget? target) =>
        target
            is CanvasTarget.Button
                or CanvasTarget.Toggle
                or CanvasTarget.GridCell
                or CanvasTarget.PkCheckBox;

    /// <summary>今の Space の押下の持ち主を決める</summary>
    private void Decide()
    {
        if (Focus == CanvasTarget.TextBox)
        {
            Owner = SpaceOwner.Text;
            return;
        }

        if (IsSpaceActivated(Focus) && Arrival == FocusArrival.Keyboard)
        {
            if (Owner != SpaceOwner.Control)
            {
                ControlPressIntact = true;
            }

            // ボタン類は、マウスをキャプチャしていない Space の押下（最初の押下か、ほかのキーで取り消された後のリピート）で
            // 押下に入り直し、マウスをキャプチャする
            if (IsButton(Focus) && !ControlHoldsCapture)
            {
                ControlHoldsCapture = true;
                ControlPressIntact = true;
            }

            Owner = SpaceOwner.Control;
            return;
        }

        Owner = SpaceOwner.Pan;
    }

    /// <summary>ボタン類（ButtonBase）か</summary>
    private static bool IsButton(CanvasTarget? target) =>
        target is CanvasTarget.Button or CanvasTarget.Toggle or CanvasTarget.PkCheckBox;

    /// <summary>Space 以外のキーの押下は、ボタン類の Space の押下を取り消す（WPF の ButtonBase の決まり）</summary>
    private void CancelControlPress()
    {
        ControlHoldsCapture = false;

        if (Owner == SpaceOwner.Control)
        {
            ControlPressIntact = false;
        }
    }

    private void KeyInput()
    {
        _lastInputWasKeyboard = true;
        _originUnknown = false;
    }

    private void MouseInput()
    {
        _lastInputWasKeyboard = false;
        _originUnknown = false;
        ControlPressIntact = false;

        // マウスの押下は「キーボードで来た」記録を落とす（フォーカスが移らない押下でも）
        Arrival = FocusArrival.Mouse;
    }

    /// <summary>フォーカスを移す（同じ対象のままなら、来方の記録は書き換わらない）</summary>
    private void FocusOn(CanvasTarget target)
    {
        if (target == Focus)
        {
            return;
        }

        Focus = target;
        ControlPressIntact = false;
        Arrival =
            _lastInputWasKeyboard && !_originUnknown ? FocusArrival.Keyboard
            : _originUnknown ? FocusArrival.Restored
            : FocusArrival.Mouse;
    }
}
