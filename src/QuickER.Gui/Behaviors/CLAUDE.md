# QuickER.Gui/Behaviors

キャンバスのビヘイビア（`DragBehavior` / `RubberBandBehavior` / `CanvasViewportBehavior`）と列一覧の並べ替え（`DataGridRowReorderBehavior`）の規則：入力の読み口、進行中操作の静的状態、後始末、Space のパン、押下の間の手。
外部変更でドラッグを打ち切る側の規則は `src/QuickER.Gui/ViewModels/CLAUDE.md`「外部変更の処理とドラッグの打ち切り」。
入力の列のエンジン（このフォルダの振る舞いを本物の MainWindow で確かめる）の作りは `tests/QuickER.Tests/TestSupport/CLAUDE.md`「入力の列のエンジン」。

## 入力の読み口（BehaviorInput）と祖先の辿り方（VisualAncestors）
- このフォルダのビヘイビアは、マウスのボタン（`LeftButton` / `MiddleButton`）・位置（`GetPosition`）・修飾キー（`Keyboard.Modifiers`）を読むときと、ドラッグ＆ドロップを始めるとき（`DragDrop.DoDragDrop`）に、必ず `BehaviorInput` を通す。
  - 差し替えが無いとき（製品）は WPF の値をそのまま返すので、振る舞いは変わらない。
  - WPF はこれらを実際の機器とカーソルから読むので、擬似の入力を流すテストでは変えられない。エンジンは呼び出したスレッドの間だけ差し替え（`OverrideForTests`・破棄で元へ戻す・`[ThreadStatic]`）、仮想のボタン・カーソルの値を返す。
  - 迂回して読むと、エンジンが見る値と製品が読む値が食い違い、エンジンが緑のまま製品だけ壊れる。
- 視覚ツリーの祖先を辿るときは `VisualAncestors`（`SelfAndAncestors` / `FirstOrSelf<T>`）を通し、`VisualTreeHelper.GetParent` のループを書かない（同じ辿り方の写しが 4 か所に分かれていた）。
- ガード：`rule:behavior-input-reads`・`rule:behavior-visual-ancestor-copies`（条件コンパイルの外れる分岐も読む）・`CanvasInputTests.InputOverride_IsThreadLocalAndRestored`。

## 外部変更によるドラッグの打ち切り
- `DragBehavior.CancelActiveDrag` は座標・幅をドラッグ開始時点へ復元してから静的状態を落とし、キャプチャを解放する。グループのドラッグは全部のメンバーを戻す。`RubberBandBehavior.CancelActiveSelection` も同じ入口から呼ばれ、ラバーバンドを隠して選択を確定しない。
- 押していないときの取り消しは何も変えない。
- コミット方式へ変えてはいけない（理由と呼び出し側の規則は ViewModels 側）。
- ガード：`DiagramEdit/CanvasInput`（行 `external-cancel-restores-drag`・`external-cancel-restores-resize`・`external-cancel-restores-group`・`external-cancel-clears-rubber-band`・`external-cancel-without-press-changes-nothing`）。mutation 実証済み。

## 進行中操作の静的状態は [ThreadStatic]
- `DragBehavior` / `RubberBandBehavior` / `CanvasViewportBehavior` の進行中状態は静的フィールドで、すべて `[ThreadStatic]` にする。`BehaviorInput` の差し替え先も同じ。
  - 本番はルーティングイベントも `CancelActiveCanvasInteractions` も単一 UI スレッドが読み書きするので、付けても意味は変わらない。`CancelActiveCanvasInteractions` の入口は、外部変更の監視が `_uiPost` の Dispatcher 経由で、起動時チェック・上書き保存直後の再照合・開いた直後の再照合・再入の保留の再評価が同期呼び出しで、いずれも呼び出し元が UI スレッド。`_uiPost` の配線だけを見て確認を終えない。
  - 外すと、並列実行されるテストがプロセス全体で状態を奪い合う。外部変更を処理するテスト（`HandleExternalModification` を通るものすべて。実ファイル監視・起動時チェック経由も含み対象が増え続ける）のキャンセルが、別クラスのドラッグを開始直後に消し、消えたことは後続の表明でしか分からないので散発的な失敗に化ける。
  - xunit のコレクションで直列化する方式は、取り消し側の一覧を人が維持し続ける必要があり、静かに再発する。
- `[ThreadStatic]` のフィールド初期化子は最初のスレッドでしか走らない。`_groupMembers` は null 許容にして `GroupMembers` プロパティで「null＝空」を吸収する（初期化子へ戻すと他スレッドで `NullReferenceException`）。
- 隔離テストは値 1 つしか見ないので、`CanvasViewportBehavior` は状態を足したときの付け忘れを、書き換わる静的フィールド（読み取り専用と定数を除く）をリフレクションで全数数えるテストが拾う。
- ガード：`DragBehaviorCancelTests` の「ドラッグの進行中状態はスレッドごとに独立する」「ラバーバンドの進行中状態はスレッドごとに独立する」、`CanvasViewportBehaviorDetachTests` の「ビューポートの静的状態はスレッドごとに独立する」「ビューポートの書き換わる静的フィールドはすべて [ThreadStatic]」、`CanvasInputTests.InputOverride_IsThreadLocalAndRestored`。mutation 実証済み。

## CanvasViewportBehavior の後始末
- 無効化時と `Unloaded` 時に共有の `Detach` を通す。ScrollViewer 自身の購読だけでなく、`OnScrollViewerLoaded` が**ウィンドウへ**張った 5 つ（`PreviewKeyDown` / `PreviewKeyUp` / `PreviewMouseDown` / `PreviewGotKeyboardFocus` / `Deactivated`）も外して、対象の参照を捨てる。外さないと、キャンバスを無効化・破棄したあともウィンドウのキー入力を拾い続け、参照が視覚ツリーを掴んだまま残る。外す処理は二重登録の防止と共有の `UnsubscribeWindow`。
- 外す相手は `_hostWindow` として覚えておく。後始末の時点では ScrollViewer が視覚ツリーから外れていて `Window.GetWindow` が null を返す。
- 捨てた対象は `OnScrollViewerLoaded` が `GetIsEnabled` を見て戻す。`Unloaded` はウィンドウを閉じるとき以外（親の付け替え・テンプレートの再適用）にも起きるので、戻さないと「添付プロパティは有効なのに対象が null」になり、パンとズームがエラーも出さずに効かなくなる。
- 対象が戻ったことを `IsPanActive` で表明してはいけない（ウィンドウ側の購読だけで立つので、戻す処理を外しても緑のまま）。対象にだけ効くカーソルの変化（パン待機の `Cursors.Hand`）で見る。
- ガード：`CanvasViewportBehaviorDetachTests` の「無効化するとウィンドウ側の Space 監視も外れる」「画面から外れるとウィンドウ側の Space 監視も外れる」、`DiagramEdit/CanvasInput`（行 `reload-keeps-pan-working`。外して戻す手の後のパン待機のカーソルを見る）。mutation 実証済み。

## Space のパンは、キーボードで移ったボタン類の Space を横取りしない
- `CanvasViewportBehavior` はウィンドウ全体の Space を監視する（キャンバスにフォーカスが無くてもパンを効かせるため）。横取りしないのは次の 2 つだけ。
  - テキスト入力（`TextBoxBase` / `PasswordBox`）。
  - Space を押下に使うコントロール（`ButtonBase`・`ComboBox`・`ListBoxItem`・`TreeViewItem`・`DataGridCell`）に、**キーボード操作で**フォーカスが移ってきたとき。ただしパンの実行中（中ボタンか Space＋左ボタンでドラッグしている最中）は除く。
    - 理由：パンの実行中の Space はパンに使っている最中で、ボタン類へ回すと、ボタンが Space の押下で ScrollViewer からキャプチャを奪ってパンが切れ、Space の解放でボタンが押される（見回してもボタンを押さない、という約束と食い違う）。新しい押下も、パンが持っていない押下のリピートの判定し直しも同じ。
    - テキスト入力はパンの実行中でも横取りしない（テキストボックスはキャプチャを奪わず、パンは続く）。列一覧のセルの編集中はフォーカスが編集の TextBox にあるので、同じくテキスト入力として扱う。
- キーボードで来たかは、ウィンドウの `PreviewKeyDown`（直前＝キーボード）/ `PreviewMouseDown`（直前＝マウス）/ `PreviewGotKeyboardFocus`（その時点の直前の入力を記録）で自前に覚える。
  - `InputManager.MostRecentInputDevice` を使ってはいけない。`ProcessInput` で流す擬似入力では値が入らず（null のまま）、判定をテストで駆動できない。
- マウス押下は「キーボードで来た」記録も落とす。既にフォーカスのあるボタンのクリックや余白のクリックではフォーカス移動が起きないので、落とさないと Tab のときの記録が残り、マウスでクリックした直後のボタンの上の Space がそのボタンの押下になる。
- 別のウィンドウから戻った直後はパン側へ倒す。`Deactivated` で「経緯不明」を立て、戻ってから最初のキーかマウスの入力までに移ったフォーカス（WPF の復元）は「キーボードで来なかった」として記録する（復元の `OldFocus` は null でなく Window なので `OldFocus` では判別できない）。マウスでクリックした直後とあわせてパンを優先するのは、ツールバーのボタンを押したあと Space＋ドラッグで見回す操作で、そのボタンをもう一度押さないため。
- パンが Space を持っている間の自動リピートは、判定をやり直さずに処理済みにする。やり直すと、パンの途中でフォーカスが移ったボタン（Tab で移る・マウスでクリックしてキーボードの記録が落ちる前のボタン）へリピートが届き、パンを続けたままボタンが押されたりチェックが切り替わったりする。
- キーボードで移ったボタンが Space を持ったまま（ボタンがキャプチャを持つ）マウスを押すと、その後のリピートがパン待機の判定になっても、Space の解放はボタンを押す（WPF の `ButtonBase` が Space の押下でキャプチャを取り、解放で Click する仕様）。エンジンはこの押下をパンの間の「押されない」の例外として扱う。
- 中ボタンのパンの実行中に Space を押しても、カーソルはパンの実行中（`Cursors.ScrollAll`）のままにする（パン待機の手へ替えない）。中ボタンを離した時点で Space を押していれば、パン待機の手へ戻る。
- 新しい状態 3 つも `[ThreadStatic]`。
- ガード：`DiagramEdit/CanvasInput`（行 `keyboard-button-space-clicks-once`・`keyboard-button-space-repeat-clicks-once`・`keyboard-toggle-space-repeat-toggles-once`・`keyboard-pk-checkbox-space-toggles`・`mouse-after-keyboard-button-pans`・`restored-keyboard-button-pans`・`textbox-space-is-text`・`keyboard-button-mouse-press-then-repeat-clicks-on-release`・`keyboard-button-press-cancelled-by-tab-after-pan-repeat`・`g1-repeat-*`・`g1-middle-pan-*`・`middle-pan-tab-to-textbox-space-is-text`・`g24-*`）、Space の持ち主の判定の網羅の行（`CanvasInputRows.DecisionCoverage`）、`CanvasViewportBehaviorSpaceKeyTests`（キーボードでフォーカスしたキャンバスはパン／無効化したウィンドウのマウス押下・フォーカス移動の購読は外れる）。mutation 実証済み。
- `CanvasViewportBehaviorSpaceKeyTests` は判断が `Deactivated` に左右されるので、ほかのテストのウィンドウにアクティブを奪われないよう `WindowActivationTestCollection`（`DisableParallelization`）に入れてある。

## 押下の間の手（パン待機・押下の外での解放・列一覧の行ヘッダー）
- 左ボタンの押下の後に Space を押したパン待機の間（`IsPanActive`）は、`DragBehavior` が移動・リサイズを当てず、`RubberBandBehavior` がラバーバンドを始めない。当てると、押したカードがパンの見回しと一緒に動き、余白の押下から範囲選択が始まる。
- `RubberBandBehavior` は余白の押下でラバーバンドを保留にしたら、キャプチャを取る前（閾値まで）の間だけウィンドウの `PreviewMouseUp` / `PreviewMouseDown`（処理済みのイベントも）を監視し、キャンバスの外での解放とほかの押下で保留を捨てて監視を外す。捨てないと、外で離した保留が残り、次にキャンバスの外で押して持ち込んだドラッグからラバーバンドが始まる。監視は押下・解放・キャプチャの喪失・取り消しのどれでも外す。監視先の窓も `[ThreadStatic]`。
- 余白のクリックの選択解除は `CanvasClickCommand` の `MouseBinding`（押下で発火する）が行う。解放で `Handled` を立てても解除は止まらない。
- `DataGridRowReorderBehavior` はドラッグ元を行ヘッダーの押下だけで記録し、左ボタンの解放（処理済みのイベントも）で必ず消す。行ヘッダーは `ButtonBase` で解放を処理済みにするので、処理済みを受けないとクリックの後にドラッグ元が残り、列一覧の外で押して行へ持ち込んだドラッグが並べ替えを始める。
- ガード：`DiagramEdit/CanvasInput`（行 `g12-space-after-card-press-moves-nothing`・`g12-space-after-blank-press-starts-no-rubber-band`・`g23-release-outside-leaves-no-pending`・`g23-drag-in-from-outside-starts-no-rubber-band`・`g2-row-header-click-leaves-no-drag-source`・`reorder-drop-onto-other-row-pushes-one-command`、網羅の行の押下の場所 × パン待機 × 押下の間の手）。mutation 実証済み。

## カードのドラッグの履歴とホバーのカーソル
- カードのドラッグの解放は、閾値以内なら位置を戻して履歴に積まず、超えて動いたら `MoveEntityCommand`（グループは `GroupMoveEntitiesCommand`）を 1 つ積む。リサイズは履歴に積まず、幅が変わったときだけ変更の世代を進める（`MarkWidthChanged`）。
- 押していないときのカードの右端のつかみの上はリサイズのカーソル、内側は既定。パンの間は既定にする（カードのカーソルは ScrollViewer に設定したパンのカーソルより優先されるため）。
- ガード：`DiagramEdit/CanvasInput`（行 `card-drag-pushes-one-move`・`card-click-restores-and-pushes-nothing`・`card-resize-pushes-nothing`・`card-resize-back-to-start-advances-nothing`・`group-drag-pushes-one-group-move`・`group-click-restores-members`・`hover-grip-shows-resize-cursor-except-while-panning`）、実際の入力で押下から解放までを通す `DragBehaviorTests.RealInputPress_StartsDragAndEndsWhenButtonIsUp`。mutation 実証済み。
