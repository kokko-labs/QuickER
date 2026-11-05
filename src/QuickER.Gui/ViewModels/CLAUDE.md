# QuickER.Gui/ViewModels

MainViewModel と図の ViewModel の規則：文書の保存・外部変更検知、図の一括置換、Undo の追跡、選択候補の同期、一意制約・主キー・リレーションの GUI。
アプリ全体（配布・終了の連鎖・通知）は `src/QuickER.Gui/CLAUDE.md`。ドラッグなどのビヘイビアは `src/QuickER.Gui/Behaviors/CLAUDE.md`。

## 文書と外部変更検知
- 「現在のファイル」フルパスは `MainViewModel.CurrentFilePath`。遷移するのは開く／名前を付けて保存・新規作成（null）・起動時の作業状態復元だけで、インポートや図置換ではパスを維持する。
- Ctrl+S 上書き保存／Ctrl+Shift+S 名前を付けて保存。タイトルは「ファイル名\*」の未保存マーク（終了確認なし）。
- 自動保存（AppData）は復旧専用のまま分離する。現在ファイルへの書き込みは Ctrl+S のみ。自動保存を現在ファイルへ直結すると、編集のたびにユーザーファイルを上書きし、MCP の同時編集と衝突する。
- 復元メタは gui-settings.json の CurrentDocument セクション（FilePath / LastKnownHash / IsDirty / AutoSaveHash）。起動時に「前回のファイル＋未保存変更」を復元する。
- ダーティ判定は `UndoRedoManager` の変更世代カウンタ（読込/保存時点の世代と比較）。Undo で保存時点へ戻っても安全側の「変更あり」になる。
- 外部変更は `DocumentFileWatcher`（WPF 非依存）で検知する。
  - 400ms デバウンス・SHA-256 ハッシュ比較・保存前後の Suspend/Resume による自己書き込み抑制の二重化・書き込み途中の読込リトライ。
  - 監視を張れないときはパスを保持して最長 30 秒間隔で張り直しを試み続け、中断と再開を 1 回ずつ通知する。
- **クリーンなら無確認で自動再読込**（ビューポート維持＝fit-to-window 抑制・履歴クリア・ステータスバーの一時通知）。**ダーティなら確認ダイアログ**。
  - 「続行」選択後は同一ハッシュの再確認を抑止する。抑止は保存・開く・再読込の共通出口 `UpdateDocumentIdentity` で解く。残すと、同じ内容が後から書き戻されたとき確認も上書き前の照合も素通りする。
  - ガード：`DocumentFileTests`（行 `guard-ignored-forgotten-after-save-external` / `guard-ignored-forgotten-after-save-overwrite`）。
- 破損/新フォーマット文書は取り込まず現状維持＋通知する。起動時の復元後も同じ規則で外部変更をチェックする。
- 文書の保存・読込・外部変更・自動保存は、状態機械のエンジン `DocumentFileTests` がまとめて確かめる。
  - 手の列を VM と参照モデル `DocumentFileModel` で流して毎手で突き合わせ、モデルと独立の不変条件 5 つ（`DocumentFileInvariants`：見ていない版・将来版を黙って書き潰さない／最終既知ハッシュは読み書きした内容／クリーンは自動再読込・ダーティは確認／メタが食い違えば前のパスで復元しない／読込の検査は拒否だけ）を当てる。
  - 決まった形のシナリオ（`DocumentFileScenarios`＝元の単発のテストの入力と期待）と、種を使う乱数の手の列（`DocumentFileGenerator`・普段 150 種・夜間 1500 種・`QUICKER_DOCFILE_SEEDS` / `QUICKER_DOCFILE_SEED`）。最終既知ハッシュと無視したハッシュは反射で読む（`DocumentFileProbe`。読み方は `DocumentFileTests.Probe_ReadsTheRecordedHashes` が固定）。
  - 監視器そのもの（実 FileSystemWatcher・デバウンス・張り直し）は時計の差し込み点が無いのでモデルの外に置き、`DocumentFileWatcherTests` と `MainViewModelLoadHashRaceTests` が受け持つ。

### 外部変更の処理とドラッグの打ち切り
- `HandleExternalModificationCore` は冒頭で `CancelActiveCanvasInteractions`（`DragBehavior.CancelActiveDrag`＋`RubberBandBehavior.CancelActiveSelection`）を呼ぶ。クリーン／ダーティ分岐より前に、座標・幅をドラッグ開始時点へ戻してから静的状態を落としてキャプチャを解放する。
  - コミット方式（EndDrag 相当で `MoveEntityCommand` を積む）へ変えてはいけない。クリーンな文書がコミットでダーティへ変わり、外部変更の分岐が自動再読込から確認ダイアログへ化ける。
  - 復元しない素の状態リセットも不可。ダーティ→「続行」の経路で「位置は動いたのに Undo 履歴に無い」変更が図に残る。
  - 既知の制限（機能モジュールのモーダルは閉じない）は docs/mcp.md の注意節に明記。
- 打ち切りより前に捨てるものが 2 つある。捨てるだけのイベントで利用者のドラッグを壊さないため。再読込・確認へ分岐する前に打ち切る順序は変わらない。
  - 内容ハッシュが最終既知ハッシュと一致する変更（既に反映済み）。
  - 処理中の再入（確認ダイアログの入れ子のメッセージループの最中に届いた変更）。保留を立てて戻り、処理の後に `CheckExternalChangeAgainstDisk` で最新のディスクと 1 回だけ照らし合わせる。畳まないと確認ダイアログが 2 枚重なる。
- ガード：`DocumentFileTests`（行 `drag-*`・再入の行 `ext-reentry-deferred-after-continue` / `ext-reentry-absorbed-by-reload`・反映済みの行 `ext-already-reflected-ignored`）・`DragBehaviorCancelTests`。mutation 実証済み。

## 最終既知ハッシュ
`SaveToPath`・`Open`・`ReloadFromDisk` の直後の最終既知ハッシュは「書き出した内容／読み込んだ内容そのもの」から採る。ディスクの読み直しで採ってはいけない。

- 読み書きから採取までの隙間に外部プロセスが書くと、外部内容のハッシュを「自分が読み書きした内容」として記録して `MarkClean` する。その書き込みの FSW イベントは期待ハッシュと一致して捨てられ（保存中は Suspend でそもそも捨てられ、デバウンスはワンショット）、以後どの検知経路もメモリとディスクの乖離を見つけられず、次の Ctrl+S は上書き前の照合を素通りする。
- 保存：`JsonStorageService.SaveAtomic` が書いた JSON 文字列を返し、`DocumentContentHash.ComputeForText`（符号化は `AtomicFile` の既定＝BOM なし UTF-8 と一致必須）でハッシュ化する。`Resume()` 直後に `CheckExternalChangeAgainstDisk` で 1 回再照合する。
- 読込：`TryLoad` の 5 引数版が返す**読み取ったバイト列**を `ComputeForBytes` でハッシュ化する。復号済みの文字列から採らない（復号は BOM を剥がすので `TryCompute` と一致せず、開くたびに再読込が暴発する）。
- 再照合は `Open` だけが記録直後に行う（監視の開始が読み取りより後なので、隙間の書き込みはイベントすら起きない）。`ReloadFromDisk` は再照合しない。監視が最初から動いていて、正しいハッシュを記録すれば隙間の書き込みのイベントが捨てられずに届く。同期で再照合すると、外部が書き続ける間「再読込→再照合→再読込…」が再帰で積み上がる。
- ガード：`DocumentFileTests`（行 `race-*`＝保存と開くの割り込み・BOM 付きファイル・再読込の割り込みが次の保存で確認になる。不変条件 (2) が毎手で最終既知ハッシュを照らし合わせる）・`MainViewModelLoadHashRaceTests`（実監視経由の追従）・`DocumentContentHashTests`（書いた文字列・読んだバイト列のハッシュがファイルのハッシュと一致）。mutation 実証済み。

## 上書き保存の確認
- `SaveToPath` 冒頭の `ConfirmOverwriteBeforeSave` が唯一の入口で、`_fileWatcher.Suspend()` より前・書き込みより前に置く。保存後の再照合は自分が書いた内容と一致するので、外部内容を上書きしたことは原理的に検知できない。
- 判定材料はすべてディスク上のファイルから採り、VM に状態を持たない。「将来版を開いた」ことをフラグで覚える方式は、次の 3 つが同時に開く。
  - 図の全置換では `CurrentFilePath` が遷移しない（DB 取込・AI 生成はパス維持）ので、置換後の保存で確認が消える。
  - 起動時の作業状態復元でフラグが戻らない。
  - 別セッション・別プロセスが将来版で書いたファイルには最初から効かない。
- 規則は 2 つで、効く範囲が違う。
  - **将来版の書き戻しは保存先がどのファイルでも確認する**（相手にだけ残っている未対応データを書き潰すため）。判定は `JsonStorageService.IsNewerFormatFile`（内部で `TryReadFormatVersion`）＝ルートの `Version` だけを読む軽い読み取りで、逆直列化も Id 重複検査も通さない。版を名乗っていないファイル（不在・破損・非 JSON・キー無し）は「将来版でない」とし、名乗っているのに解釈できない値（`"2"`・`2.0` 等）は確認する側へ倒す。エクスポートと CLI の書き込みも同じ判定を共有する。
  - **外部変更の上書きは「いま開いているファイル」のときだけ確認する**（比較対象の最終既知ハッシュを持つのがそれだけ）。同一ファイル判定はフルパス正規化＋大文字小文字非依存（`IsCurrentDocumentPath`。「名前を付けて保存」で同じファイルを選び直す経路を落とさない）。乖離はディスクのハッシュが `_lastKnownFileHash` とも `_ignoredExternalHash` とも違うとき。
- 存在しない保存先は誰の内容も消さないので即 true。ハッシュ算出不能も保存を妨げない。
- クリーン時の自動再読込は失敗し得る（破損 JSON・将来版・型不一致：一時通知だけを残し、文書はクリーン・`_lastKnownFileHash` は旧値のまま）。この照合が無いと次の Ctrl+S が外部の内容を黙って消す。
- 確認は多くとも 1 回。両方立つときは失うものが大きい将来版の文言だけを出す（2 つ続けると両方「はい」で流される）。
- キャンセルは保存しない（ダーティ維持・`UpdateDocumentIdentity` を呼ばない・通知も出さない）。照合から書き込みまでの TOCTOU 窓は保存後の再照合が拾うので許容する。
- ガード：`DocumentFileTests`（行 `guard-*`＝大小違いの同一パスを含む・行 `newer-*`＝置換後・新規作成後・一度上書きした後の再保存を含む。乱数の手の列と不変条件 (1)）。mutation 実証済み。

## 自動保存の文書メタ
- 自動保存の文書メタは、一緒に書いた図の内容のハッシュを持ち、起動時に照らし合わせる。
  - 図（last_diagram.json）とメタ（gui-settings.json の `CurrentDocument`）は別々のファイルへ書くので、メタだけ書けない・2 つの書き込みの間で落ちると、別の図の内容に前のファイルのパスが付いてクリーンで復元される。上書き保存が前のファイルを確認なしに別の図で上書きする（`ConfirmOverwriteBeforeSave` は「ディスクのハッシュ＝最終既知ハッシュ」なので素通りする）。
  - `CurrentDocumentSettings.AutoSaveHash`（`SaveAtomic` が返した JSON の `DocumentContentHash.ComputeForText`）を `RestoreLastDiagram` が `TryLoad` の読んだバイト列のハッシュと照合する。食い違えば図は残してパスを捨て、無題・未保存として開いてステータスバーで知らせる（上書き保存は名前を付けて保存になる）。
- ハッシュを持たない旧版のメタは信じる（疑うと、更新した最初の起動で全員の図がファイルから外れる）。
- 1 つのファイルにまとめる案は採らない。last_diagram.json が利用者のファイル形式と同じ型で、包むと形式検証の経路と旧形式の読み込みが要る。
- ガード：`DocumentFileTests`（行 `meta-*`＝食い違い・両方書けた・旧メタ。乱数の手の列と不変条件 (4)）。mutation 実証済み。

## 確認の間に変わった図
- 確認ダイアログの前に図から作った結果は、確認の後で図の世代を比べ、変わっていれば当てずに取りやめる。
  - 確認は入れ子のメッセージループを回すので、その間に外部変更の自動再読込（`ReplaceDiagram`）や内蔵チャットのツールの編集が走り得る。
  - 確認の前に読んだ図から作った結果をそのまま当てると、対象 DBMS の切替は置き換わった図の方言だけを変えて列を古い方言の型のまま残す。取込は古い図から作ったマージ結果で新しい図を置き換え、その間の変更を捨てる（自動再読込の後なら、ディスクとの照合も通るので次の上書き保存が外部の変更を消す）。
- 世代は `MainViewModel.DiagramRevision`（`UndoRedoManager.ChangeGeneration` が動くたびと `ReplaceDiagram` のたびに進む。値に意味は無い）。機能モジュールは `IErDiagramHost.DiagramRevision`、ファイルの取込は `IDiagramTransferHost.DiagramRevision` から読む。世代は図を読む直前に控える（DB 取込は非同期の取得の後）。
- 比べる入口は 5 つ。
  - 対象 DBMS の切替（`ChangeTargetDbms`。取りやめはステータスバー `TypeConversion_DiagramChangedDuringConfirm`）。
  - ファイルの丸ごと置換とマージ取込（`DiagramImportService.AbandonIfChangedDuringConfirm`）。
  - DB 取込（`DbImportCommandService`）。
  - C# コード取込（`CodeReverseCommandService`）。取込の取りやめはダイアログで知らせる。
- ガード：`MainViewModelTargetDbmsTests` の `ChangeDbms_DiagramReplacedDuringConfirm_IsAbandoned` / `ChangeDbms_DiagramEditedDuringConfirm_IsAbandoned`・`DiagramImportServiceTests` の `ImportExcel_` / `ImportDbml_DiagramChangedDuringConfirm_IsAbandoned`・`DbImportCommandServiceTests.RunAsync_DiagramChangedDuringConfirm_IsAbandoned`・`CodeReverseCommandServiceTests.Run_DiagramChangedDuringConfirm_IsAbandoned`・`MainViewModelErDiagramHostTests.DiagramRevision_ChangesWithEveryDiagramChange`。確認の中の処理は `StubDialogService` の `OnConfirm` / `OnConfirmWarning` / `OnConfirmWarningDetails` で模す。mutation 実証済み。
- `ReplaceDiagram` での加算は外しても緑のまま。置き換えの呼び出しはすべて履歴を消して世代も進むので、二重の守りになっている。

## 図の一括置換
### Clear と購読解除の順序
- 図の一括置換は素の `Clear()` でなく `MainViewModel.ClearDiagramCollections` を通し、購読解除・追跡終了は Clear の**後**で行う。
  - `ObservableCollection.Clear()` は Reset 通知で OldItems を持たないので、`CollectionChanged` 側の自動解除がエンティティ・リレーションとも一切効かない。
  - 解除が漏れると、`DiagramChangeTracker` のスナップショット辞書へ旧要素が残り続け（開く・取込・外部変更の自動再読込のたびに図 1 枚分ずつ単調増加）、生き残った購読経由で旧 VM の変更が現在の Undo スタックへ幽霊コマンドを積む。
- 解除を Clear より先にしてはいけない。`Relationships.Clear()` が誘発する `ApplyRelationshipColumnRules` が `RunWithoutTracking` を通り、その後始末（全要素のスナップショット再取得）がまだコレクションに残っている旧要素を拾い直して、解除済みの追跡が復活する。
  - 現状の一括置換（`ReplaceDiagram`）は一括更新の中で呼ぶので Clear は列ルールを誘発しないが、呼び出し側の状況に依存させないため順序は保つ。
- 解除処理の実体は `DetachEntityViewModel` / `DetachRelationshipViewModel` の 2 メソッドだけ。OldItems 分岐と一括置換が共有する（片側にだけ購読が足される乖離を防ぐ）。ガード：`DiagramChangeTrackerDetachTests`。

### RunBulkDiagramUpdate
- 一括置換（`ReplaceDiagram`）は `RunBulkDiagramUpdate` を通し、`RunWithoutTracking` のスナップショット再取得は最外側の終了時だけ行う。どちらかを外すと、リレーションを 1 本足すたびに全列の列ルールの付け直しと全要素の再取得が走り、開く・取込・自動再読込の時間がリレーション数×要素数で伸びる（500 テーブル・800 リレーションで 1.9 秒 → 0.13 秒）。
  - 一括更新中に止めるのはコレクション変更ハンドラの後段（列ルール・関連ハイライト・ミニマップ・キャンバスサイズ・変更通知）だけ。購読と追跡の着脱は要素ごとに行う。
  - 内側の再取得を省いても最終状態が変わらないのは、外側の終了時の全要素の再取得が必ず上書きし、追跡停止中のハンドラはスナップショットを読まないため。
  - ガード：`MainViewModelBulkDiagramUpdateTests`。mutation 実証済み。
- Undo / Redo の入口・整列・幅の自動調整・テーブルの削除も `RunBulkDiagramUpdate` を通す。
  - 位置・幅の変更（`OnEntityPropertyChanged`）・`RefreshCanvasSize`・`ApplyRelationshipColumnRules` は一括更新の中では何もせず、終わりの `RefreshAfterBulkDiagramUpdate` が本体（`…Core`）を 1 回ずつ呼ぶ。コマンドの後処理が列ルールを直接呼んでも二重にならない。外すと 500 テーブルの整列の Undo 1 回でミニマップを 1,000 回作り直し、実ウィンドウで約 18 秒かかる。
  - Undo / Redo は追跡の停止が外側・一括更新が内側。追跡の取り直しは最外側の終わりに 1 回のまま。
- ドラッグの 1 回の移動とグループの元の位置への戻し（クリック扱い・取り消し）は、軽い範囲 `RunDeferringLayoutRefresh` を使う。キャンバスとミニマップだけを範囲の終わりに 1 回更新する（列ルールと追跡の取り直しまでする一括更新はマウスの移動のたびには重すぎる）。
  - 範囲は try / finally で必ず閉じる。深さが残ると以後キャンバスとミニマップが更新されない。
  - 所有する ViewModel は押下の時点で `DragBehavior._owner` に控える。
- 連動変更の記録（`DiagramChangeTracker.PushGroupedPropertyChanges`：PK の切り替え・リレーションの種別変更）も、後処理（列ルールの再適用）を `RunWithoutTracking` の中で実行し、再取得はその終了時の 1 回だけにする。記録の側で別に取り直すと、1 回の操作で全要素を 2 回なめる。ガード：`MainViewModelBulkDiagramUpdateTests` の `GroupedPropertyChange_RefreshesOnce_AndUndoesInOneStep`。
- ガード：`LayoutRefreshCountTests`（整列 3 種・整列の Undo / Redo・幅の自動調整・グループのドラッグの移動と取り消し・複数テーブルの削除とその Undo / Redo で「ミニマップの作り直し 1 回・キャンバスの通知 1 回」を 20 と 40 テーブルで、列ルールの適用回数、範囲が例外でも閉じること、NULL 許容の表示の切り替え・列名の変更・列の追加・KeyMark・追跡の割り当て）。mutation 実証済み。

### 選択・検索結果・作成中リレーションの解除
- 作成中のリレーションの始点は `DetachEntityViewModel` が解く（始点が図から外れたら作成モードを解除。削除・Undo・一括置換・ツールボックスを畳むの全経路）。ガード：`SelectionInvariantTests`（行 `relationship-mode-*`・`selection-pending-relationship-source-*`）。
- 選択（`SelectedEntity` / `SelectedColumn` / `SelectedRelationship`）と検索結果（`SearchResults`）も同じ解除の箇所で外す。図に居る要素だけを指す。
  - 列だけが外れる経路（列の追加の Undo）は `OnEntityColumnsChangedForSelection`。Reset は選択中の列が図のどのテーブルにも無くなったときだけ外す。並べ替え（Move）では外さない。
  - 外さないと、プロパティパネルが追跡の外れた要素を編集し（履歴にも未保存にもならない）、Redo で同じインスタンスが記録のない編集ごと戻る。古い検索結果から選んで削除すると、`EntityQueryRemoval` が Id で取り除くため同じ Id の生きている表の名前付きクエリまで消える。
- 検索結果の取り除きはフォーカス位置を詰め（`PruneSearchResults`）、Enter は現在の図で結果を取り直してから進む（`RefreshSearchResults`）。`RemoveSelectedEntity` は図に居ない選択に何もせず履歴も積まない（防御の二重化）。
- ガード：`SelectionInvariantTests`（不変条件を全コマンドの Undo / Redo・検索・置換の後に表明）・実ウィンドウの `PendingEditOnDetachTests`（入力途中の追加の Undo で、外れたテーブルへ確定待ちの入力が書き込まれない）。mutation 実証済み。

## Undo の追跡宣言
### 全プロパティの宣言
- 図の ViewModel の公開・internal の読み書きプロパティは、すべて「Undo で追跡する」か「履歴の外（分類と理由つき）」のどちらかに宣言する。
  - 宣言表は `TrackedPropertyUndoExhaustiveTests` が持ち、反射で全数列挙した読み書きプロパティ（setter が public か internal）と突き合わせる。表に無い新しいプロパティはその場で赤になる。
  - 対象は図の ViewModel 4 型（エンティティ・カラム・リレーション・一意制約）・行の ViewModel 3 型（一意制約の構成列・列ペア・主キーの順序）・`MainViewModel`。
  - 追跡する宣言は `DiagramChangeTracker` の `Tracked*Properties` と一致しなければならない。追跡するプロパティごとに「変更→履歴 1 件・未保存→Undo 1 回で図全体（意味モデル＋レイアウト）が元と一致→Redo」を確かめる。
- 除外の分類は 2 つ。
  - 専用のコマンドがある：X・Y（Move 系）・行の選択（`ChangeUniqueConstraintColumnsCommand` / `ChangeRelationshipColumnPairsCommand`）・対象 DBMS と採番の印（`ChangeTargetDbmsCommand`）。
  - 履歴の外：表示だけの状態・導出値・Width・選択・検索・ビューポート・文書の識別。
- 既知の失敗の台帳 `UndoKnownFailures` は空で保つ（台帳に戻すときは期待する失敗の理由の断片も書く）。
- XAML の束縛（列一覧の NULL のチェックボックス）は実ウィンドウの `ColumnGridUndoBindingTests` が固定する。mutation 実証済み。

### 連動変更は 1 履歴に畳む
- 主キーの切り替え（NULL 許容が連動）と種別の変更（参照アクション・列ペアが連動）は、`IsPrimaryKeyChanging`〜`IsPrimaryKeyChangeCompleted` / `TypeChanging`〜`TypeChangeCompleted` の間だけ単発の記録を止める（`_columnsChangingPrimaryKey` / `_relationshipsChangingType`）。
  - 止める範囲を広げると、切り替えの外の NULL 許容が履歴に残らない（外部変更の自動再読込で黙って消える）。
  - 止めないと、連動の変更が別の履歴になり、Undo 1 回で存在しなかった状態が残る。
  - Changing と Completed は必ず対で呼ばれる前提。
- 主キーの間の NULL 許容の切り替えは VM が握り潰し、履歴も未保存も動かない。
- 全組合せ（主キー 3 通り・種別の全遷移×参照アクション全値）と、切り替えの後の同じ要素の変更が別の 1 履歴になることも、`TrackedPropertyUndoExhaustiveTests` が固定する。

### 削除コマンドの位置の復元
- 削除コマンドは位置の記録と復元を `IndexedRemoval` に任せる。リレーションが末尾へ戻ると、名前の無い外部キーの既定名が入れ替わる。
- ガード：`UndoOperationTests`（行 `removal-order-*`＝削除の操作ごとに、Undo 後の図が並びまで削除前と一致）・`RemovalOrderRoundTripTests`（`Remove` を呼ぶ `*Remove*Command.cs` が `IndexedRemoval.RemoveWhere` を使うことのソース走査）。

### 子列の外部キーの印
- 列を子列にする操作（リレーションの追加・列ペアの変更）は、実行した時点の子列の外部キーの印を控えて Undo で戻す（`ForeignKeyMarkSnapshot` を `AddRelationshipCommand` と `ChangeRelationshipColumnPairsCommand` が共有）。
  - 子列になった列の印はリレーションの列のルールが管理に移すので、戻さないと手で付けた印が Undo で消え、保存内容が元と一致しない。
  - リレーションの削除で管理下の印を落とす意味は変えない（MCP のホストと同じ）。
  - Redo は一括更新の中で走り、終わりのルールの再適用が印を決める。控えた値は Redo に効かない。
- ガード：`ManualForeignKeyMark_SurvivesUndoOf*`（初期図に手で印を付けた列 Child.OtherId と親 Other を足し、画面の作成・内蔵チャットのツール・列ペアの行の選択の 3 経路を固定）。mutation 実証済み。

## 選択候補コレクションの同期
### Move で並びを合わせる
- 選択候補コレクションの同期は `CandidateListSync.SyncTo` を通し、選択中の項目を ItemsSource から外さない。
  - 一意制約の構成列の行（`UniqueConstraintMemberViewModel`）と列ペアの行（`RelationshipColumnPairViewModel`）のコンボボックスの候補は、Remove→Insert でなく Move で並びを合わせる。削除と挿入だけで追うと、列を並べ替えるたびに同じ列が候補に増える。
  - 行が選んでいる親列は、候補キー（主キー・一意制約）でなくなっても、その行の候補に残す（`RelationshipViewModel.RefreshColumnPairCandidates`。列が存在する限りペアを残す `EnsureColumnSelectionConsistency` と同じ線引き）。候補キーでない列は新しく足す行の候補には出ない。
  - 選択中の項目が ItemsSource から一度でも消えると、ComboBox が `null` を書き戻し、それが利用者の編集として正本（列ペア）を変える。
  - 一意制約の削除の Undo / Redo の最中だと、`RefreshColumnPairCandidates` の行の列挙の途中で `SetColumnPairs` が行コレクションを書き換えて `InvalidOperationException` になり、アプリが落ちる（実ウィンドウで再現済み）。行の列挙はコピーに対して回すが、これは保険で単独の mutation では赤にできない。
- `CanAddColumnPair` は件数の比較でなく「どの行も使っていない候補が残っているか」で判定する（選択が候補キーでなくても残るため、件数は対応しない）。
- ガード：`CandidateListSyncTests`（6 件）・`RelationshipViewModelTests` の 3 件（一意制約が無くなった親列・主キーでなくなった親列・並べ替え）・`EntityViewModelUniqueConstraintTests` の並べ替え 1 件・`RelationshipColumnPairCardTests` の実ウィンドウ 2 件（「参照先の一意制約を Redo で消しても落ちず、ペアと選択と履歴が残る」「選択中の列を並べ替えて Undo しても選択が落ちず候補が重複しない」）。mutation 実証済み。

### 選択を当て直す順
- 正本から行の選択を当て直すときは「当てる列を候補へ足す → 選択を当てる → 候補を絞る」の順にする（`RelationshipViewModel.SyncColumnPairRows` / `UniqueConstraintViewModel.SyncMembers`）。行を外して残りが繰り上がる・並びを入れ替える・列が消える、のどれでも、候補に無い項目を選択に当てない。
- 列の増減では、消えた列を指すペアを先に落としてから候補を作り直す。
- 外す行は、行リストから削除する前に切り離す（削除の通知の最中の後片付けの書き込みで、残りの行がまだ古い選択を持つ間に正本を古い並びへ戻さない）。
- 図から外れた要素（`Detach` 後のリレーション・`IsDetachedFromOwner` の一意制約・`IsDetachedFromDiagram` のテーブルの一意制約）の行の書き込みは、正本を変えず表示を正本へ戻す（Undo で戻る要素を欠けさせない）。図に居る間は、受け手がいなくても正本へ反映する（単独の VM の経路）。
- ガード：`CandidateRowReconcileTests`（選択と候補の順序を観測点 `SelectionCandidateProbe` で数える 13 件）・`CandidateSelectionOrderWindowTests` の実ウィンドウ 2 件。mutation 実証済み。

## 名前の上限の通知と方言切替の確認
受け付けない名前は、DDL・同期の入口と同じ `IdentifierLengthLimit.DescribeViolation` で知らせる（判定の写しを作らない）。置き場所は 3 つ。

- GUI の名前の確定：`DiagramChangeTracker.NameEdited`（履歴へ積んだ編集でだけ発火。Undo / Redo・一括置換では発火しない。対象はテーブル名・カラム名・外部キー名・一意制約名）を `MainViewModel.OnNameEdited` が受け、図の対象 DBMS の上限を超えたら `NotifyStatus` で警告する（値は受け付ける）。
- ツールの応答：`NameLimitNotes`（Model）が変更の前後の違反を比べ、**その呼び出しで新しく生じたもの**だけを英語の注意文にする。MCP のツールホストは `Mutate`、内蔵チャットのツールは `ErDiagramDynamicTools.Execute` が同じ関数を通す（`src/QuickER.Mcp/CLAUDE.md`「個別ツールの規則」を参照）。
- 対象 DBMS の切替：`MainViewModel.CollectNamesRejectedBy` が切替先の上限で保存された名前を判定し、型の変換と同じ `ConfirmWarningDetails` に場所と理由つきで全件並べる（これだけでも確認のトリガーになる）。

ガード：`NameLimitStatusWarningTests`（4 種類の名前・MySQL の表せない文字とファイル名・Undo / Redo と丸ごと置換で警告しない・切替の確認の一覧とキャンセル）・`ToolScenarioTests`（行 `name-limit-*`＝両ホストで 5 つのツールと、以前からの違反を繰り返さないこと）・`NameLimitNotesTests`。mutation 実証済み。

## 一意制約の GUI
モデルと DDL の規則は `src/QuickER.Model/CLAUDE.md`「一意制約」。ここでは GUI の部分だけを置く。

- プロパティパネルの UNIQUE 制約カード：正本は `ColumnIds`。構成列編集は**列選択コンボボックスの行リスト**で行う。
  - 行の並びは宣言順。候補は「同じ制約の他行が未使用の列＋自行の選択」に絞って、重複選択を構造的に排除する。
  - ＋は未選択の空スロットを 1 つだけ追加するビュー状態でモデル未反映。列を選んだ時点で確定する。×は行を外す。
  - 行 VM（`UniqueConstraintMemberViewModel`）は正本からの導出。選択編集は `EntityViewModel` 経由で MainViewModel へ中継し、`ChangeUniqueConstraintColumnsCommand` 1 回に畳む。
  - 追加/削除/構成列変更は Undo コマンド。制約名は DiagramChangeTracker が追跡する。
- キー欄の UQ マーク：優先度は PK > FK > UQ。判定と配色は `ColumnKeyMarkPalette` へ 1 本化し、画面 XAML／ベクタ印刷／SVG／寸法実測の 4 箇所が共有する。コンパクト表示は PK/FK 行のみ。
- 列削除はその列を含む制約ごと削除する（縮めて別の意味の制約に変質させない）。GUI は `RemoveColumnCommand` の 1 Undo 単位で、MCP の `remove_column` も同じカスケード。
- 合成名の解決（`ConstraintNames.ResolveUniqueConstraintNames` の結果）は、`MainViewModel` が図の世代と方言の切替で捨てるキャッシュで 1 回の実行につき 1 回だけ解決する。ガード：`UniqueConstraintResolvedNameLimitTests`（方言の切替で表示し直す）。

## 主キーの順序の GUI
モデルの規則（実効順・膜の正本）は `src/QuickER.Model/CLAUDE.md`「主キーの実効順」。ここでは GUI の部分だけを置く。

### EntityViewModel の保持
- `EntityViewModel` が順序を保持する。防御コピー→`ToModel()` で書き戻す。パススルー（`ToModel()` の書き戻し）を外すと「取込→保存」で順序が静かに消える。
- VM に Model と同一の merge 規則のミラーを持つ。相互参照 XmlDoc で「片方だけ変えない」。
- GUI の PK トグル・列削除はリストに触れない（stale Guid は読み手が無視する）。

### 主キーの順序カード
- 実効順 2 本以上（複合 PK）のときだけ表示する。行は実効順の列名、↑ / ↓ で入れ替え、端の行はボタン無効。
- **カードに列選択（膜の編集）を置かない**。GUI のプロパティパネル内で膜の編集口を PK チェック 1 つに保ち、パネルからはリレーション参加列のロック（列グリッド側の判定）を迂回できないようにするため。
  - MCP の `set_primary_key` は GUI ミラー含めロックを持たない。ファイルホストにロックの概念が無く、両ホストのパリティを優先した意図的な非対称。ロックされた列をツールで主キーから外すと、グリッドでは再チェックできず、Ctrl+Z かリレーション削除でしか戻せない。
- 行 VM `PrimaryKeyMemberViewModel` は正本からの導出。`SyncPrimaryKeyMembers` は末尾で増減して既存行インスタンスを使い回す。毎回 Clear すると作り直しでキーボードフォーカスがカードの外へ失われる。
- ボタンは列でなく位置に紐づく。行 i は常に実効順で i 番目の列を指すので、同じボタンを続けて押すと押すたびにその位置の（別の）列が動く。
- 帰結：一度でも順序を明示すると（カード・`set_primary_key`・DB 取込）その図では順序が固定される。列 D&D が PK 順を変えない（安全側の挙動変化で CHANGELOG に明記）。以後 PK チェックを付けた列は実効順の末尾に付く（リストに Guid が残る列は旧位置復活）。

### SetPrimaryKeyCommand
- `SetPrimaryKeyCommand` は「膜が動く列の (IsPrimaryKey, IsNullable)＋順序リスト」を 1 履歴で往復する。
  - `IsNullable` を持つ理由：`RunWithoutTracking` が止めるのは変更追跡ハンドラだけで、`ColumnViewModel` の「PK 化したら NULL 許容を落とす」連動はそのまま走る。しかも PK を外しても NULL 許容は戻らない。
  - 適用順が IsPrimaryKey → IsNullable の理由：PK が true の間は `IsNullable` の setter が値を握り潰す。
  - `ApplyRelationshipColumnRules` を呼ばない理由：ロック判定がリレーションと列ペアだけを読み `IsPrimaryKey` を参照しないので、膜が動いても結果が変わらない。
- 「変化なし」判定は実効順でなく保存リストと膜で行う。実効順で比べると、「リスト空の図へ同じ並びを明示＝ピン留め」という実変更を取りこぼす。

ガード：`UndoOperationTests`（行 `primary-key-*`＝Undo 1 回で戻る・NULL 許容の復活・端の行・ピン留めの履歴化）・`PrimaryKeyOrderCardTests`（カードの配線）・`EntityViewModelTests`（行の実効順・行インスタンス使い回し）。ツールは `ToolScenarioTests`（行 `primary-key-*`＝両ホスト）と `ErDiagramDynamicToolsPrimaryKeyTests`（GUI の Undo）、両ホストの一致は `ErDiagramToolHostParityTests`（`EntitySnap` に実効順と保存リストの列名写像を射影し、順序の食い違いを検知する）。mutation 実証済み。

## リレーションの GUI
列ペアの正本・DDL・差分の規則は `src/QuickER.Model/CLAUDE.md`「リレーションの列ペア」。ここでは GUI の部分だけを置く。

- リレーションパネルの**ペア行エディタ**：1 行＝親列＋子列コンボ、両側確定で 1 履歴。親列候補は PK ∪ UNIQUE 制約構成列。ManyToMany は編集不可で、種別切替で全ペアクリア（グループ 1 履歴）。
- 作成時は親 PK 全列を自動ペア化する（列ごとに `ForeignKeyColumnResolver` の命名マッチ・重複子列排除・引き当て不能列はペアなし）。
  - 子の主キー列は、①親テーブル名由来の名前一致と、複合主キーの親の同名前段だけを候補にする。識別関係を拾い、単一主キーの親の同名一致（単独の `id` の誤爆）・FK 印・後方一致・自己参照では外す。
  - 複合主キーの親の同名前段でも、名前が単独の `id` の子の主キー列は外す。`id` はそれぞれの表自身の代理キーなので、`Projects(TenantId, Id)`→`Tasks(TenantId, Id, ProjectId)` の `Id` は①の `ProjectId` で解く。
  - ガード：`ForeignKeyColumnResolverTests`。
- 制約名は AI ツールと同じ `ConstraintNames.UniqueForeignKey`（衝突しなければ既定名のまま）。
- 列削除は当該リレーションの全ペアをクリアする（線・種別・制約名は残す・1 Undo 単位）。MCP の `remove_column` とパリティ。
- 手作成の外部キー名が縮めた既定名を避けることは `MainViewModelRelationshipConstraintNameTests`、GUI と MCP の作成経路の一致は `ConstraintNameCreationPathTests` が固定する（`src/QuickER.Mcp/CLAUDE.md`「add_relationship と remove_relationship」を参照）。
