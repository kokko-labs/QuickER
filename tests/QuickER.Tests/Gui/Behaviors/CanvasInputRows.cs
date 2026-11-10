using QuickER.Tests.TestSupport.Engines;
using S = QuickER.Tests.Gui.Behaviors.CanvasStep;
using T = QuickER.Tests.Gui.Behaviors.CanvasTarget;

namespace QuickER.Tests.Gui.Behaviors;

/// <summary>入力の列のエンジンの決まった形の行（回帰の網）と網羅の行</summary>
/// <remarks>
/// <para>
/// 決まった形の行は、指摘と Space の横取りの線引きの観点を最小の手の列にしたもの（網羅の計数に数えない）。
/// 網羅の行（<c>coverage-</c> で始まる）は、フォーカスの対象 × 来方 × Space の押し方の全組を決定的に流し、
/// 残りの因子（重なる手・Ctrl）を順に割り当てて 2 因子の組も埋める（網羅の計数に数える）。
/// </para>
/// </remarks>
internal static class CanvasInputRows
{
    /// <summary>網羅の行の Id の接頭辞</summary>
    public const string CoveragePrefix = "coverage-";

    /// <summary>決まった形の行</summary>
    public static ScenarioTable<IReadOnlyList<CanvasStep>> Table { get; } =
        new(
            EngineIds.DiagramEdit,
            [
                Row(
                    "g1-repeat-on-mouse-focused-button",
                    "G1",
                    "マウスでクリックしたボタンの上で Space を押しっぱなしにしても（自動リピート）ボタンは押されない",
                    S.ClickFocus(T.Button),
                    S.SpaceDown,
                    S.Repeat(2),
                    S.SpaceUp
                ),
                Row(
                    "g1-repeat-on-mouse-focused-toggle",
                    "G1",
                    "マウスでクリックしたトグルの上で Space を押しっぱなしにしても切り替わらない",
                    S.ClickFocus(T.Toggle),
                    S.SpaceDown,
                    S.Repeat(1),
                    S.SpaceUp
                ),
                Row(
                    "g1-repeat-on-mouse-focused-pk-checkbox",
                    "G1",
                    "マウスでクリックした PK のチェックボックスの上で Space を押しっぱなしにしても切り替わらない",
                    S.ClickFocus(T.PkCheckBox),
                    S.SpaceDown,
                    S.Repeat(1),
                    S.SpaceUp
                ),
                Row(
                    "g1-repeat-on-mouse-focused-grid-cell",
                    "G1",
                    "マウスでクリックした列名のセルの上で Space を押しっぱなしにしても編集が始まらない",
                    S.ClickFocus(T.GridCell),
                    S.SpaceDown,
                    S.Repeat(1),
                    S.SpaceUp
                ),
                Row(
                    "g1-repeat-on-restored-button",
                    "G1",
                    "別のウィンドウから戻った直後のボタンの上で Space を押しっぱなしにしてもボタンは押されない",
                    S.TabTo(T.Button),
                    S.Reactivate,
                    S.SpaceDown,
                    S.Repeat(2),
                    S.SpaceUp
                ),
                Row(
                    "g1-repeat-after-tab-during-pan",
                    "G1",
                    "Space＋左ドラッグのパンの最中に Tab でボタンへ移ってもリピートはボタンへ届かず、パンは切れない",
                    S.ClickFocus(T.Canvas),
                    S.SpaceDown,
                    S.LeftDown(CanvasSpot.Blank),
                    S.TabTo(T.Button),
                    S.Repeat(1),
                    S.Move(20, 10),
                    S.LeftUp,
                    S.SpaceUp
                ),
                Row(
                    "keyboard-button-space-clicks-once",
                    null,
                    "Tab で移ったボタンは Space で押せる（解放で 1 回）",
                    S.TabTo(T.Button),
                    S.SpaceDown,
                    S.SpaceUp
                ),
                Row(
                    "keyboard-button-space-repeat-clicks-once",
                    null,
                    "Tab で移ったボタンで Space を押しっぱなしにしても、解放で 1 回だけ押される（リピートで横取りしない）",
                    S.TabTo(T.Button),
                    S.SpaceDown,
                    S.Repeat(3),
                    S.SpaceUp
                ),
                Row(
                    "keyboard-toggle-space-repeat-toggles-once",
                    null,
                    "Tab で移ったトグルは Space を押しっぱなしにしても 1 回だけ切り替わる",
                    S.TabTo(T.Toggle),
                    S.SpaceDown,
                    S.Repeat(2),
                    S.SpaceUp
                ),
                Row(
                    "keyboard-pk-checkbox-space-toggles",
                    null,
                    "列一覧で Tab で移った PK のチェックボックスは Space で切り替わる",
                    S.TabTo(T.PkCheckBox),
                    S.SpaceDown,
                    S.Repeat(1),
                    S.SpaceUp
                ),
                Row(
                    "mouse-after-keyboard-button-pans",
                    null,
                    "Tab で移ったボタンをマウスでクリックした後の Space はパンになり、押されない",
                    S.TabTo(T.Button),
                    S.ClickFocus(T.Button),
                    S.SpaceDown,
                    S.SpaceUp
                ),
                Row(
                    "restored-keyboard-button-pans",
                    null,
                    "別のウィンドウから戻った直後は、キーボードで移っていたボタンでも Space はパンになる",
                    S.TabTo(T.Button),
                    S.Reactivate,
                    S.SpaceDown,
                    S.SpaceUp
                ),
                Row(
                    "textbox-space-is-text",
                    null,
                    "テキスト入力の Space は横取りしない（リピートしてもパンにならない）",
                    S.ClickFocus(T.TextBox),
                    S.SpaceDown,
                    S.Repeat(2),
                    S.SpaceUp
                ),
                Row(
                    "middle-pan-keeps-capture",
                    null,
                    "中ボタンのドラッグのパンの間はキャンバスがキャプチャを持ち、解放で終わる",
                    S.MiddleDown(CanvasSpot.Blank),
                    S.Move(30, 20),
                    S.Move(-10, 5),
                    S.MiddleUp
                ),
                Row(
                    "space-left-pan-with-repeats",
                    null,
                    "Space＋左ドラッグのパンの最中のリピートでパンが切れない",
                    S.ClickFocus(T.Canvas),
                    S.SpaceDown,
                    S.LeftDown(CanvasSpot.Blank),
                    S.Move(20, 10),
                    S.Repeat(2),
                    S.Move(30, 0),
                    S.LeftUp,
                    S.SpaceUp
                ),
                Row(
                    "wheel-zoom-with-ctrl-only",
                    null,
                    "Ctrl＋ホイールはズームし、修飾なしのホイールはズームを変えない",
                    S.Wheel(1),
                    S.CtrlDown,
                    S.Wheel(1),
                    S.Wheel(-1),
                    S.CtrlUp
                ),
                Row(
                    "keyboard-button-mouse-press-then-repeat-clicks-on-release",
                    null,
                    "キーボードで移ったボタンが Space を持ったままマウスを押すと、その後のリピートでパン待機になっても押下はボタンのもので、解放でボタンが押される",
                    S.TabTo(T.Button),
                    S.SpaceDown,
                    S.LeftDown(CanvasSpot.Blank),
                    S.LeftUp,
                    S.Repeat(1),
                    S.SpaceUp
                ),
                Row(
                    "g12-space-after-card-press-moves-nothing",
                    "G12",
                    "カードを押した後に Space を押すと、パン待機の間の移動でカードが動かない",
                    S.ClickFocus(T.Canvas),
                    S.LeftDown(CanvasSpot.Card),
                    S.Move(10, 5),
                    S.SpaceDown,
                    S.Move(30, 20),
                    S.SpaceUp,
                    S.LeftUp
                ),
                Row(
                    "g12-space-after-blank-press-starts-no-rubber-band",
                    "G12",
                    "余白を押した後に Space を押すと、パン待機の間の移動でラバーバンドが始まらない",
                    S.ClickFocus(T.Canvas),
                    S.LeftDown(CanvasSpot.Blank),
                    S.SpaceDown,
                    S.Move(20, 15),
                    S.SpaceUp,
                    S.LeftUp
                ),
                Row(
                    "g2-row-header-click-leaves-no-drag-source",
                    "G2",
                    "行ヘッダーをクリックした後、列一覧の外で押して行へ持ち込んでも並べ替えのドラッグが始まらない",
                    S.LeftDown(CanvasSpot.RowHeader),
                    S.LeftUp,
                    S.LeftDown(CanvasSpot.Outside),
                    S.MoveTo(CanvasSpot.RowOther),
                    S.LeftUp,
                    S.ClickFocus(T.Canvas)
                ),
                Row(
                    "g23-release-outside-leaves-no-pending",
                    "G23",
                    "余白で押してキャンバスの外で離すと、ラバーバンドの保留が残らない",
                    S.ClickFocus(T.Canvas),
                    S.LeftDown(CanvasSpot.Blank),
                    S.MoveTo(CanvasSpot.Outside),
                    S.LeftUp
                ),
                Row(
                    "g23-drag-in-from-outside-starts-no-rubber-band",
                    "G23",
                    "キャンバスの外で離した後、キャンバスの外で押してキャンバスへ持ち込んでも、古い起点からラバーバンドが始まらない（Ctrl を押したままにして、外での解放の残りの表明で止めずに害まで流す）",
                    S.ClickFocus(T.Canvas),
                    S.CtrlDown,
                    S.LeftDown(CanvasSpot.Blank),
                    S.MoveTo(CanvasSpot.Outside),
                    S.LeftUp,
                    S.LeftDown(CanvasSpot.Outside),
                    S.MoveTo(CanvasSpot.Blank),
                    S.Move(20, 15),
                    S.LeftUp,
                    S.CtrlUp,
                    S.ClickFocus(T.Canvas)
                ),
                Row(
                    "g24-space-during-middle-pan-keeps-scroll-cursor",
                    "G24",
                    "中ボタンのパンの実行中に Space を押しても、カーソルはパンの実行中（全方向）のまま。Space を離してもパンは続く",
                    S.ClickFocus(T.Canvas),
                    S.MiddleDown(CanvasSpot.Blank),
                    S.Move(10, 5),
                    S.SpaceDown,
                    S.Move(10, 5),
                    S.SpaceUp,
                    S.Move(5, 5),
                    S.MiddleUp
                ),
                Row(
                    "g24-middle-release-with-space-held-returns-to-pan-wait",
                    "G24",
                    "中ボタンのパンの実行中に Space を押し、Space を押したまま中ボタンを離すと、パン待機（手）へ戻る",
                    S.ClickFocus(T.Canvas),
                    S.MiddleDown(CanvasSpot.Blank),
                    S.Move(10, 5),
                    S.SpaceDown,
                    S.Repeat(1),
                    S.MiddleUp,
                    S.Move(5, 5),
                    S.SpaceUp
                ),
                Row(
                    "g1-middle-pan-tab-to-button-repeat-goes-to-pan",
                    "G1",
                    "テキストボックスで押した Space のまま中ボタンでパンし、パンの最中に Tab でボタンへ移っても、リピートはパンのもので、ボタンはキャプチャを奪わず押されない",
                    S.TabTo(T.TextBox),
                    S.SpaceDown,
                    S.MiddleDown(CanvasSpot.Blank),
                    S.TabTo(T.Button),
                    S.Move(10, 5),
                    S.Repeat(1),
                    S.Move(10, 5),
                    S.MiddleUp,
                    S.SpaceUp
                ),
                Row(
                    "g1-middle-pan-tab-to-button-space-goes-to-pan",
                    "G1",
                    "中ボタンのパンの最中に Tab でボタンへ移って Space を押しても、Space はパンのもので、ボタンはキャプチャを奪わず押されない",
                    S.ClickFocus(T.Canvas),
                    S.MiddleDown(CanvasSpot.Blank),
                    S.Move(10, 5),
                    S.TabTo(T.Button),
                    S.SpaceDown,
                    S.Move(10, 5),
                    S.SpaceUp,
                    S.MiddleUp
                ),
                Row(
                    "middle-pan-tab-to-textbox-space-is-text",
                    null,
                    "中ボタンのパンの最中に Tab でテキストボックスへ移って押した Space は、文字入力のまま（横取りしない）。パンは続く",
                    S.ClickFocus(T.Canvas),
                    S.MiddleDown(CanvasSpot.Blank),
                    S.Move(10, 5),
                    S.TabTo(T.TextBox),
                    S.SpaceDown,
                    S.Move(10, 5),
                    S.SpaceUp,
                    S.MiddleUp
                ),
                Row(
                    "keyboard-button-press-cancelled-by-tab-after-pan-repeat",
                    null,
                    "キーボードで移ったトグルが Space を持ったままマウスを押し、リピートでパン待機になった後に Tab を押すと、トグル自身の押下が取り消される（押されない・切り替わらない）",
                    S.TabTo(T.Toggle),
                    S.SpaceDown,
                    S.MiddleDown(CanvasSpot.Blank),
                    S.Repeat(1),
                    S.TabTo(T.Toggle),
                    S.MiddleUp,
                    S.SpaceUp
                ),
                Row(
                    "card-spot-after-wide-resize-scrolls-into-view",
                    null,
                    "カードのつかみをキャンバスの外まで引いて幅を大きく広げた後も、つかみの左・つかみの場所をスクロールで見せて押せる",
                    S.ClickFocus(T.Canvas),
                    S.LeftDown(CanvasSpot.CardGrip),
                    S.MoveTo(CanvasSpot.Outside),
                    S.LeftUp,
                    S.MoveTo(CanvasSpot.Card),
                    S.LeftDown(CanvasSpot.Card),
                    S.Move(5, 5),
                    S.LeftUp,
                    S.MoveTo(CanvasSpot.CardGrip)
                ),
                Row(
                    "g3-delete-during-drag-pushes-no-removed-move",
                    "G3",
                    "カードのドラッグ中に Delete でそのテーブルを消すと、解放で消したテーブルの移動を履歴に積まない",
                    S.ClickFocus(T.Canvas),
                    S.LeftDown(CanvasSpot.Card),
                    S.Move(20, 10),
                    S.Delete,
                    S.Move(20, 10),
                    S.LeftUp
                ),
                Row(
                    "g5-reload-during-space-restores-cursor",
                    "G5",
                    "Space の押下中にキャンバスが外れて戻っても、解放の後にキャンバスのカーソルが手のまま残らない",
                    S.ClickFocus(T.Canvas),
                    S.SpaceDown,
                    S.Reload,
                    S.SpaceUp
                ),
                Row(
                    "reorder-drop-onto-other-row-pushes-one-command",
                    null,
                    "行ヘッダーから 2 行目へ落とすと、並べ替えを 1 つ履歴に積む",
                    S.LeftDown(CanvasSpot.RowHeader),
                    S.MoveTo(CanvasSpot.RowOther),
                    S.LeftUp,
                    S.ClickFocus(T.Canvas)
                ),
                Row(
                    "card-drag-pushes-one-move",
                    null,
                    "カードを閾値を超えて動かして離すと、移動を 1 つ履歴に積む",
                    S.LeftDown(CanvasSpot.Card),
                    S.Move(30, 20),
                    S.LeftUp
                ),
                Row(
                    "card-click-restores-and-pushes-nothing",
                    null,
                    "カードを閾値以内で離すと（クリック）、位置を戻して履歴に積まない",
                    S.LeftDown(CanvasSpot.Card),
                    S.Move(1, 1),
                    S.LeftUp
                ),
                Row(
                    "card-resize-pushes-nothing",
                    null,
                    "右端のつかみで幅を変えて離しても、履歴に積まない",
                    S.LeftDown(CanvasSpot.CardGrip),
                    S.Move(30, 0),
                    S.LeftUp
                ),
                Row(
                    "external-cancel-restores-drag",
                    null,
                    "ドラッグの途中で外部変更の打ち切りが入ると、開始時点へ戻し、履歴に積まず、進行中の状態を残さない",
                    S.LeftDown(CanvasSpot.Card),
                    S.Move(30, 20),
                    S.ExternalCancel,
                    S.Move(10, 10),
                    S.LeftUp
                ),
                Row(
                    "external-cancel-restores-resize",
                    null,
                    "リサイズの途中で外部変更の打ち切りが入ると、幅を開始時点へ戻す",
                    S.LeftDown(CanvasSpot.CardGrip),
                    S.Move(40, 0),
                    S.ExternalCancel,
                    S.LeftUp
                ),
                Row(
                    "external-cancel-clears-rubber-band",
                    null,
                    "ラバーバンドの途中で外部変更の打ち切りが入ると、矩形を消して選択を確定しない",
                    S.ClickFocus(T.Canvas),
                    S.LeftDown(CanvasSpot.Blank),
                    S.Move(20, 15),
                    S.ExternalCancel,
                    S.LeftUp
                ),
                Row(
                    "hover-grip-shows-resize-cursor-except-while-panning",
                    null,
                    "押していないときカードの右端のつかみはリサイズのカーソル、内側は既定、パン待機の間は右端でも既定",
                    S.MoveTo(CanvasSpot.CardGrip),
                    S.MoveTo(CanvasSpot.Card),
                    S.MoveTo(CanvasSpot.CardGrip),
                    S.SpaceDown,
                    S.MoveTo(CanvasSpot.CardGrip),
                    S.SpaceUp
                ),
                Row(
                    "group-drag-pushes-one-group-move",
                    null,
                    "Ctrl のクリックで 2 つ選んでから選択中のカードを Ctrl なしでドラッグすると、グループ移動を 1 つ履歴に積む",
                    S.ClickFocus(T.Canvas),
                    S.CtrlDown,
                    S.LeftDown(CanvasSpot.Card2),
                    S.LeftUp,
                    S.CtrlUp,
                    S.LeftDown(CanvasSpot.Card),
                    S.Move(30, 20),
                    S.LeftUp
                ),
                Row(
                    "group-click-restores-members",
                    null,
                    "2 つ選んでから選択中のカードを閾値以内で離すと（クリック）、全メンバーの位置を戻して履歴に積まない",
                    S.ClickFocus(T.Canvas),
                    S.CtrlDown,
                    S.LeftDown(CanvasSpot.Card2),
                    S.LeftUp,
                    S.CtrlUp,
                    S.LeftDown(CanvasSpot.Card),
                    S.Move(2, 1),
                    S.LeftUp
                ),
                Row(
                    "external-cancel-restores-group",
                    null,
                    "グループ移動の途中で外部変更の打ち切りが入ると、全メンバーを開始時点へ戻す",
                    S.ClickFocus(T.Canvas),
                    S.CtrlDown,
                    S.LeftDown(CanvasSpot.Card2),
                    S.LeftUp,
                    S.CtrlUp,
                    S.LeftDown(CanvasSpot.Card),
                    S.Move(30, 20),
                    S.ExternalCancel,
                    S.LeftUp
                ),
                Row(
                    "external-cancel-without-press-changes-nothing",
                    null,
                    "押していないときの外部変更の打ち切りは、位置・幅・履歴・選択を変えない",
                    S.ExternalCancel
                ),
                Row(
                    "card-resize-back-to-start-advances-nothing",
                    null,
                    "右端のつかみで幅を変えて元の幅へ戻して離すと、変更世代を進めない",
                    S.LeftDown(CanvasSpot.CardGrip),
                    S.Move(20, 0),
                    S.Move(-20, 0),
                    S.LeftUp
                ),
                Row(
                    "reload-keeps-pan-working",
                    null,
                    "キャンバスが外れて戻った後も、Space のパン待機でキャンバスのカーソルが手になり、中ボタンでパンできる",
                    S.ClickFocus(T.Canvas),
                    S.Reload,
                    S.SpaceDown,
                    S.SpaceUp,
                    S.MiddleDown(CanvasSpot.Blank),
                    S.Move(20, 10),
                    S.MiddleUp
                ),
                Row(
                    "line-press-starts-no-rubber-band",
                    null,
                    "リレーションの線を押して動かしても、ラバーバンドは始まらない",
                    S.LeftDown(CanvasSpot.Line),
                    S.Move(20, 15),
                    S.LeftUp
                ),
            ]
        );

    /// <summary>網羅の行（フォーカスの対象 × 来方 × Space の押し方の全組。重なる手と Ctrl は順に割り当てる）</summary>
    public static IReadOnlyList<(string Id, IReadOnlyList<CanvasStep> Steps)> Coverage { get; } =
        BuildCoverage();

    /// <summary>
    /// 押下の網羅の行（押下の場所 × 押下の間の手の全組。パン待機は順に割り当てる）
    /// </summary>
    /// <remarks>
    /// カードの押下の間の Delete だけは作らない：押したカードのテーブルを消すと、解放で消したテーブルの移動を積む（G3・きっかけ待ち）
    /// ので網羅の行が常に赤になる。その組は乱数の手の列（ドラッグ中の Delete）が数える。
    /// </remarks>
    public static IReadOnlyList<(
        string Id,
        IReadOnlyList<CanvasStep> Steps
    )> PressCoverage { get; } = BuildPressCoverage();

    private static List<(string, IReadOnlyList<CanvasStep>)> BuildPressCoverage()
    {
        var rows = new List<(string, IReadOnlyList<CanvasStep>)>();
        var index = 0;

        foreach (var spot in CanvasInputDimensions.PressSpots)
        {
            foreach (var during in CanvasInputDimensions.DuringLevels)
            {
                if (spot == CanvasSpot.Card && during == "delete")
                {
                    continue;
                }

                // パン待機の押下の間に Space は挟めない（もう押している）
                var pan = index++ % 2 == 1 && during != "space";
                var steps = new List<CanvasStep> { S.ClickFocus(T.Canvas) };

                if (pan)
                {
                    steps.Add(S.SpaceDown);
                }

                steps.Add(S.LeftDown(spot));
                steps.AddRange(
                    during switch
                    {
                        "space" => [S.SpaceDown, S.Move(15, 10), S.SpaceUp],
                        "ctrl" => [S.CtrlDown, S.Move(15, 10), S.CtrlUp],
                        "delete" => [S.Move(5, 5), S.Delete, S.Move(15, 10)],
                        "cancel" => [S.Move(15, 10), S.ExternalCancel, S.Move(5, 5)],
                        "move-out" =>
                        [
                            S.MoveTo(
                                CanvasSpots.OnCanvas(spot) ? CanvasSpot.Outside : CanvasSpot.Blank
                            ),
                        ],
                        _ => [S.Move(15, 10)],
                    }
                );
                steps.Add(S.LeftUp);

                if (pan)
                {
                    steps.Add(S.SpaceUp);
                }

                rows.Add(
                    (
                        $"{CoveragePrefix}press-{spot}-{during}-{(pan ? "wait" : "none")}".ToLowerInvariant(),
                        steps
                    )
                );
            }
        }

        return rows;
    }

    private static ScenarioRow<IReadOnlyList<CanvasStep>> Row(
        string id,
        string? finding,
        string description,
        params CanvasStep[] steps
    ) => new(id, finding, description, steps);

    private static List<(string, IReadOnlyList<CanvasStep>)> BuildCoverage()
    {
        var rows = new List<(string, IReadOnlyList<CanvasStep>)>();
        var index = 0;

        foreach (var target in Enum.GetValues<CanvasTarget>())
        {
            foreach (var arrival in Enum.GetValues<FocusArrival>())
            {
                // キャンバスは MainWindow のタブ順に入らないので、キーボードで来る組は作れない（網羅しない組の受け入れ）
                if (target == CanvasTarget.Canvas && arrival == FocusArrival.Keyboard)
                {
                    continue;
                }

                foreach (var repeats in new[] { 0, 1, 2 })
                {
                    var gesture = index % 5;
                    var ctrl = index / 5 % 2 == 1;
                    index++;

                    var steps = new List<CanvasStep>();
                    steps.AddRange(
                        arrival switch
                        {
                            FocusArrival.Keyboard => [S.TabTo(target)],
                            FocusArrival.Mouse => [S.ClickFocus(target)],
                            _ => [S.ClickFocus(target), S.Reactivate],
                        }
                    );
                    steps.Add(S.SpaceDown);

                    if (ctrl)
                    {
                        steps.Add(S.CtrlDown);
                    }

                    var repeat = repeats == 0 ? null : S.Repeat(repeats);

                    switch (gesture)
                    {
                        case 1:
                            steps.Add(S.MiddleDown(CanvasSpot.Blank));
                            AddIf(steps, repeat);
                            steps.Add(S.Move(15, 10));
                            steps.Add(S.MiddleUp);
                            break;
                        case 2:
                            steps.Add(S.LeftDown(CanvasSpot.Blank));
                            AddIf(steps, repeat);
                            steps.Add(S.Move(15, 10));
                            steps.Add(S.LeftUp);
                            break;
                        case 3:
                            AddIf(steps, repeat);
                            steps.Add(S.Wheel(1));
                            break;
                        case 4:
                            steps.Add(S.TabTo(T.Toggle));
                            AddIf(steps, repeat);
                            break;
                        default:
                            AddIf(steps, repeat);
                            break;
                    }

                    if (ctrl)
                    {
                        steps.Add(S.CtrlUp);
                    }

                    steps.Add(S.SpaceUp);
                    rows.Add(
                        (
                            $"{CoveragePrefix}{target}-{arrival}-{CanvasInputDimensions.PatternLevel(repeats)}".ToLowerInvariant(),
                            steps
                        )
                    );
                }
            }
        }

        return rows;
    }

    /// <summary>
    /// Space の持ち主の判定の網羅の行（判定の時の対象 × 判定の種類 × パンの実行中かの全組）
    /// </summary>
    /// <remarks>
    /// パンの実行中の対象は、キャンバスなら中ボタンの押下（キャンバスへフォーカスが移る）で、ほかは Tab で移る。
    /// 判定し直すリピートは、テキストボックスで押した Space（持ち主はテキスト）のまま対象へ移って作る。
    /// </remarks>
    public static IReadOnlyList<(
        string Id,
        IReadOnlyList<CanvasStep> Steps
    )> DecisionCoverage { get; } = BuildDecisionCoverage();

    private static List<(string, IReadOnlyList<CanvasStep>)> BuildDecisionCoverage()
    {
        var rows = new List<(string, IReadOnlyList<CanvasStep>)>();

        foreach (var target in Enum.GetValues<CanvasTarget>())
        {
            foreach (var repeat in new[] { false, true })
            {
                foreach (var panning in new[] { false, true })
                {
                    var steps = new List<CanvasStep>();

                    // 対象へ移る（キャンバスはタブ順に入らないので、パンの外ではクリックで、パンの最中は中ボタンの押下で来る）
                    void MoveToTarget()
                    {
                        if (target != T.Canvas)
                        {
                            steps.Add(S.TabTo(target));
                        }
                        else if (!panning)
                        {
                            steps.Add(S.ClickFocus(T.Canvas));
                        }
                    }

                    if (repeat)
                    {
                        steps.Add(S.TabTo(T.TextBox));
                        steps.Add(S.SpaceDown);
                    }

                    if (panning)
                    {
                        if (!repeat)
                        {
                            steps.Add(S.ClickFocus(T.Canvas));
                        }

                        steps.Add(S.MiddleDown(CanvasSpot.Blank));
                        steps.Add(S.Move(10, 5));
                    }

                    MoveToTarget();
                    steps.Add(repeat ? S.Repeat(1) : S.SpaceDown);

                    if (panning)
                    {
                        steps.Add(S.Move(10, 5));

                        if (!repeat)
                        {
                            steps.Add(S.SpaceUp);
                        }

                        steps.Add(S.MiddleUp);
                    }

                    if (repeat || !panning)
                    {
                        steps.Add(S.SpaceUp);
                    }

                    rows.Add(
                        (
                            $"{CoveragePrefix}decision-{target}-{(repeat ? "repeat" : "press")}-{(panning ? "panning" : "none")}".ToLowerInvariant(),
                            steps
                        )
                    );
                }
            }
        }

        return rows;
    }

    private static void AddIf(List<CanvasStep> steps, CanvasStep? step)
    {
        if (step is not null)
        {
            steps.Add(step);
        }
    }
}
