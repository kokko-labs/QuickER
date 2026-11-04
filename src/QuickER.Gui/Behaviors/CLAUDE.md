# QuickER.Gui/Behaviors

キャンバスのビヘイビア（`DragBehavior` / `RubberBandBehavior` / `CanvasViewportBehavior`）の規則：進行中操作の静的状態、後始末、Space のパン。
外部変更でドラッグを打ち切る側の規則は `src/QuickER.Gui/ViewModels/CLAUDE.md`「外部変更の処理とドラッグの打ち切り」。

## 外部変更によるドラッグの打ち切り
- `DragBehavior.CancelActiveDrag` は座標・幅をドラッグ開始時点へ復元してから静的状態を落とし、キャプチャを解放する。`RubberBandBehavior.CancelActiveSelection` も同じ入口から呼ばれる。
- コミット方式へ変えてはいけない（理由と呼び出し側の規則は ViewModels 側）。
- ガード：`DragBehaviorCancelTests`。

## 進行中操作の静的状態は [ThreadStatic]
- `DragBehavior` / `RubberBandBehavior` / `CanvasViewportBehavior` の進行中状態は静的フィールドで、すべて `[ThreadStatic]` にする。
  - 本番はルーティングイベントも `CancelActiveCanvasInteractions` も単一 UI スレッドが読み書きするので、付けても意味は変わらない。`CancelActiveCanvasInteractions` の入口は、外部変更の監視が `_uiPost` の Dispatcher 経由で、起動時チェック・上書き保存直後の再照合・開いた直後の再照合・再入の保留の再評価が同期呼び出しで、いずれも呼び出し元が UI スレッド。`_uiPost` の配線だけを見て確認を終えない。
  - 外すと、並列実行されるテストがプロセス全体で状態を奪い合う。外部変更を処理するテスト（`HandleExternalModification` を通るものすべて。実ファイル監視・起動時チェック経由も含み対象が増え続ける）のキャンセルが、別クラスのドラッグを開始直後に消し、消えたことは後続の表明でしか分からないので散発的な失敗に化ける。
  - xunit のコレクションで直列化する方式は、取り消し側の一覧を人が維持し続ける必要があり、静かに再発する。
- `[ThreadStatic]` のフィールド初期化子は最初のスレッドでしか走らない。`_groupMembers` は null 許容にして `GroupMembers` プロパティで「null＝空」を吸収する（初期化子へ戻すと他スレッドで `NullReferenceException`）。
- 隔離テストは値 1 つしか見ないので、`CanvasViewportBehavior` は状態を足したときの付け忘れを、書き換わる静的フィールド（読み取り専用と定数を除く）をリフレクションで全数数えるテストが拾う。
- ガード：`DragBehaviorCancelTests` の「ドラッグの進行中状態はスレッドごとに独立する」「ラバーバンドの進行中状態はスレッドごとに独立する」、`CanvasViewportBehaviorDetachTests` の「ビューポートの静的状態はスレッドごとに独立する」「ビューポートの書き換わる静的フィールドはすべて [ThreadStatic]」。mutation 実証済み。

## CanvasViewportBehavior の後始末
- 無効化時と `Unloaded` 時に共有の `Detach` を通す。ScrollViewer 自身の購読だけでなく、`OnScrollViewerLoaded` が**ウィンドウへ**張った 5 つ（`PreviewKeyDown` / `PreviewKeyUp` / `PreviewMouseDown` / `PreviewGotKeyboardFocus` / `Deactivated`）も外して、対象の参照を捨てる。外さないと、キャンバスを無効化・破棄したあともウィンドウのキー入力を拾い続け、参照が視覚ツリーを掴んだまま残る。外す処理は二重登録の防止と共有の `UnsubscribeWindow`。
- 外す相手は `_hostWindow` として覚えておく。後始末の時点では ScrollViewer が視覚ツリーから外れていて `Window.GetWindow` が null を返す。
- 捨てた対象は `OnScrollViewerLoaded` が `GetIsEnabled` を見て戻す。`Unloaded` はウィンドウを閉じるとき以外（親の付け替え・テンプレートの再適用）にも起きるので、戻さないと「添付プロパティは有効なのに対象が null」になり、パンとズームがエラーも出さずに効かなくなる。
- 対象が戻ったことを `IsPanActive` で表明してはいけない（ウィンドウ側の購読だけで立つので、戻す処理を外しても緑のまま）。対象にだけ効くカーソルの変化（`Cursors.Hand`）で見る。
- ガード：`CanvasViewportBehaviorDetachTests` の「無効化するとウィンドウ側の Space 監視も外れる」「画面から外れるとウィンドウ側の Space 監視も外れる」「外して戻したあともパンは効き続ける」。mutation 実証済み。

## Space のパンは、キーボードで移ったボタン類の Space を横取りしない
- `CanvasViewportBehavior` はウィンドウ全体の Space を監視する（キャンバスにフォーカスが無くてもパンを効かせるため）。横取りしないのは次の 2 つだけ。
  - テキスト入力（`TextBoxBase` / `PasswordBox`）。
  - Space を押下に使うコントロール（`ButtonBase`・`ComboBox`・`ListBoxItem`・`TreeViewItem`・`DataGridCell`）に、**キーボード操作で**フォーカスが移ってきたとき。
- キーボードで来たかは、ウィンドウの `PreviewKeyDown`（直前＝キーボード）/ `PreviewMouseDown`（直前＝マウス）/ `PreviewGotKeyboardFocus`（その時点の直前の入力を記録）で自前に覚える。
  - `InputManager.MostRecentInputDevice` を使ってはいけない。`ProcessInput` で流す擬似入力では値が入らず（null のまま）、判定をテストで駆動できない。
- マウス押下は「キーボードで来た」記録も落とす。既にフォーカスのあるボタンのクリックや余白のクリックではフォーカス移動が起きないので、落とさないと Tab のときの記録が残り、マウスでクリックした直後のボタンの上の Space がそのボタンの押下になる。
- 別のウィンドウから戻った直後はパン側へ倒す。`Deactivated` で「経緯不明」を立て、戻ってから最初のキーかマウスの入力までに移ったフォーカス（WPF の復元）は「キーボードで来なかった」として記録する（復元の `OldFocus` は null でなく Window なので `OldFocus` では判別できない）。マウスでクリックした直後とあわせてパンを優先するのは、ツールバーのボタンを押したあと Space＋ドラッグで見回す操作で、そのボタンをもう一度押さないため。
- 新しい状態 3 つも `[ThreadStatic]`。
- ガード：`CanvasViewportBehaviorSpaceKeyTests`（Tab で移ったボタンとチェックボックスは押せる／マウスでクリックした直後はパン／Tab で移ったボタンをマウスでクリックした後はパン／キャンバスはパン／別のウィンドウから戻った直後はパン／メインウィンドウの列一覧の PK のチェックボックスは Tab で移れば切り替わる／無効化したウィンドウのマウス押下・フォーカス移動の購読は外れる）。mutation 実証済み。
- このクラスは判断が `Deactivated` に左右されるので、ほかのテストのウィンドウにアクティブを奪われないよう `WindowActivationTestCollection`（`DisableParallelization`）に入れてある。
