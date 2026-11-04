# QuickER.Gui/Views

MainWindow などの View の規則：全画面表示、パネルを畳むときのフォーカス、DataGrid の行の追加・削除。

## 全画面表示
### 通常サイズを経由してから最大化する
- `MainWindow.ApplyFullScreen` は `WindowState = Normal` → `WindowStyle = None` → `WindowState = Maximized` の順で当てる。
  - 最大化のまま枠だけ外すとタスクバーが前面に残る（クライアント領域が作業領域どまりの 2562x1394 になり、経由ありの 2560x1440 と食い違う）。
  - 最終状態はどちらも `None` / `Maximized` で同じなので、状態だけを見る表明では経由の有無を見分けられない。この 1 行を消してもテストは緑のままタスクバーが残る。そこで `Window.StateChanged` の発火列（`(Normal, 元の枠)` → `(Maximized, None)`）で順序そのものを固定する。経由を省くとこのイベントは 1 度も起きない。画面解像度に依存しないので CI でも成立する。
- 全画面の状態は永続化しない（枠の無い状態で起動すると原因が分からない）。

### 外部要因で最大化が解けたら枠を戻す
- 全画面中は `StateChanged` を購読し、通常サイズへ戻されたら（Win+Down・システムメニューの `SC_RESTORE`）枠だけ戻して `IsFullScreen` を false にする。放置するとタイトルバーが無く掴んで動かせないウィンドウが残る。状態は利用者の操作結果（通常サイズ）を尊重し、退避した最大化へは戻さない。
- 条件は「最大化でない」ではなく「通常サイズになった」。前者だと最小化まで拾い、Win+D で一度デスクトップを見て戻っただけで全画面が解ける。最小化は「いったん引っ込める」意味で、引っ込んでいる間は掴んで動かせないウィンドウも残らない。
- 自分が起こす途中の Normal を外部要因と取り違えないよう、適用中は `_applyingFullScreen` で購読側を黙らせる。

### 周辺
- 切替時に「表示」グループのポップアップを閉じる。ポップアップは開いた時点の画面座標に留まりウィンドウへ追従しないので、閉じないとツールバー経由で全画面にした直後にポップアップだけが画面の真ん中へ取り残される。他の 5 トグルは連続操作のため開いたままにするので、ここだけ意図的に非対称。
- 全画面のときだけ出す終了ボタンは `IsTabStop="False"`。`DockPanel` の最初の子なので外すとタブ順の先頭になり、確認を出さない設計と相まって「ツールバーへ Tab を入れて Space」でアプリが終わる（代わりを務めるタイトルバーの × はクライアント領域のタブ順に入らず、マウスでは右端配置で誤クリックを避けているのにキーボードでは最短距離、という非対称になる）。
- ガード：`PanelVisibilityTests` の `FullScreen_FromMaximized_GoesThroughNormalState`・`FullScreen_WhenRestoredExternally_ExitsAndRestoresChrome`・`FullScreen_WhenMinimized_StaysFullScreen`・`FullScreen_ClosesViewGroupPopup`・`FullScreen_ExitButton_IsShownAndClosesWindow`。mutation 実証済み。

## パネルを畳むときは、その中に論理フォーカスを残さない
- プロパティパネルの入力欄 7 本は `UpdateSourceTrigger=LostFocus` で、確定はフォーカスが外れた瞬間に起きる。ところが `Visibility=Collapsed` にしても、キーボードフォーカスがウィンドウへ移るだけで論理フォーカス（`FocusManager.GetFocusedElement`）は入力欄に残る。`LostFocus` が発火せず、入力途中の値が束縛へ渡らない。畳んで戻すと「パネルには打ち替えた文字列・モデルは旧値」の食い違いが残り、別のエンティティを選んだ時点で入力が黙って消える（保存されるのは旧値）。
- 左右パネルの Border は `MainWindow.CollapsiblePanel_IsVisibleChanged` を配線し、非表示化のときに論理フォーカスが自分の配下なら `Keyboard.Focus(DiagramScrollViewer)` でパネルの外へ出す。
- 入力欄を持たないツールボックス側にも同じ配線をする（後から入力欄を足したときに静かに復活させないため）。ツールバー経由の畳みはボタンへフォーカスが移って先に確定するので、これが要るのは F9 / F10 の経路だけだが、経路で分岐しない。
- `UpdateSourceTrigger=PropertyChanged` へ逃げてはいけない。Undo の粒度が 1 打鍵単位に化ける。
- ガード：`PanelVisibilityTests` の `CollapsingPropertyPanel_CommitsPendingEdit`（単票の入力欄）・`CollapsingPropertyPanel_CommitsPendingCellEdit`（列グリッドのセル編集中。`DataGrid` 自身のセル確定も噛む経路）。mutation 実証済み。

## DataGrid の行の追加・削除
- DataGrid は既定で利用者の行の追加・削除を受け付け、Delete キーで ItemsSource のコレクションから行を直接取り除く。QuickER の DataGrid は正本の ViewModel のコレクション（列一覧＝`SelectedEntity.Columns`）を ItemsSource にするので、既定のままだと列の削除が履歴にも未保存の判定にも残らず（外部変更の自動再読込で確認なしに失われる）、`RemoveColumnCommand` の後始末（その列を含む一意制約の削除・列ペアのクリア）も通らない。
- src の XAML の DataGrid はすべて `CanUserAddRows="False"` と `CanUserDeleteRows="False"` を明示する。
- 列一覧（`MainWindow.xaml` の `ColumnsDataGrid`）は `DataGrid.InputBindings` の `KeyBinding Key="Delete"` を、行の削除ボタンと同じ `RemoveColumnCommand`（引数＝`SelectedColumn`・null なら何もしない）へ結ぶ。
  - インスタンスの KeyBinding は CanExecute が偽でもキーを処理済みにする（実測）。列を選んでいないときの Delete も、窓の `DeleteSelected`（テーブルの削除）へは倒れない。
  - セルの編集中の Delete は入力欄（TextBox・編集できる ComboBox）が先に処理するので KeyBinding へは届かない。
- ガード：`ColumnGridDeleteKeyTests`（実ウィンドウ・`WindowActivationTestCollection`。一意制約の構成列・主キーの列・リレーションの子列を選んだ Delete が行の削除ボタンと同じ図になり Undo 1 回で戻り未保存になる／列を選んでいない Delete は何も消さない／名前と型のセルの編集中は文字を消す）・`DataGridRowEditGuardTests`（src の全 XAML の DataGrid に 2 属性の明示を求める。属性が無いのも赤）。KeyBinding が先に処理するので `CanUserDeleteRows` を外しても挙動のテストは緑のままで、ガードが網になっている。mutation 実証済み。
