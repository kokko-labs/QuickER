# QuickER.Gui

WPF アプリ本体（合成ルート）の規則：完了通知の提示先・配布と自動更新・再起動と単一インスタンス・終了の連鎖。
領域ごとの規則は下位の CLAUDE.md に置く。

## 下位の CLAUDE.md
- `src/QuickER.Gui/ViewModels/CLAUDE.md`：文書の保存・外部変更検知、図の一括置換、Undo の追跡、選択候補の同期、一意制約・主キー・リレーションの GUI
- `src/QuickER.Gui/Behaviors/CLAUDE.md`：キャンバスの進行中操作の静的状態、Space のパン
- `src/QuickER.Gui/Views/CLAUDE.md`：全画面表示、パネルを畳むときのフォーカス、DataGrid の行の追加・削除
- `src/QuickER.Gui/Services/CLAUDE.md`：エクスポートの欠落告知、DBML / Excel 定義書の取込
- 機能モジュールの契約は `src/QuickER.Extensibility/CLAUDE.md`、汎用 WPF 部品は `src/QuickER.Gui.Common/CLAUDE.md`

## 完了通知の提示先
- 使い分けは「外部とのやり取り＝モーダル／ER 図ファイル自身の読み書き＝ステータスバーの一時通知」。
  - 外部形式の入出力（エクスポート／インポート）・DB 取込・C# コード取込は `IDialogService.ShowInformation`。
  - 上書き保存／名前を付けて保存／開くは `MainViewModel.NotifyStatus`（5 秒で「準備完了」へ戻る・外部変更検知と同じ経路）。ファイル名はタイトルバーに出るため文言へ入れない。
- 失敗の報告は提示先に依らず常にモーダル（原因の例外文を連結）。
- 完了に添える内訳・警告があるときは提示先に依らず `ShowInformationDetails`（単文の完了に大型ダイアログは出さない。コード生成の診断提示・エクスポートの欠落告知・コード取込の解析警告が同じ規則）。
- 印刷は無通知。
- 規則の正本は `IDialogService` の XmlDoc（アイコンの使い分け規約と同居）。この線引きなら機能モジュールからメインのステータスバーへ触る必要が無いので、`IErDiagramHost` に通知メソッドは持たせない。
- DB 同期の完了は「ダイアログ内ステータス＝簡潔な状態／モーダル＝結果の要約（実行バッチ数）」と文言の役割を分ける（同じ文字列を 2 箇所へ出すと重複した通知に見える）。

### 項目一覧と確認
- 標準 MessageBox の本文へ載せる項目一覧は `DialogItemList.Format`（Gui.Abstractions）へ集約する。
  - 上限 30 件で「他 N 件」の 1 行へ畳む。文言は呼び出し側の `Common_MoreItems` を渡す。
  - DB 取込・コード取込・ファイル取込の「取込で消える名前付きクエリ」告知が共有する。
  - 標準 MessageBox はスクロールしないので、件数が多いとボタンが画面外へ出る。
- マージ取込 3 経路（DB 取込・C# コード取込・Excel 定義書）の置換確認は、省略の判定を Provider の `DiagramReplacementCheck`、内訳文を Gui.Abstractions の `ReplacementLossMessage` で共有する（文言だけが各機能の resx）。
- 確認に判断材料の一覧を添えるときは `ConfirmWarningDetails`（`InformationDetailsDialog` の警告確認モード＝スクロール可能な詳細領域＋OK／キャンセル）。
  - 一覧は畳まず全件。対象 DBMS 切替の事前確認が使う。
  - 実行可否を判断させる画面で「他 N 件」の中身を確かめられないのは判断材料として不十分で、二節構成は 30 件上限でも MessageBox からあふれ得る。

## 配布と自動更新
Velopack・2 チャンネル。release.yml（手動トリガ）が win-full（self-contained）/ win-lite（フレームワーク依存）で Setup.exe＋Portable zip を生成する。

### エントリポイントとフィード
- エントリポイントは `Program.Main`（`VelopackApp.Build().Run()` を WPF 初期化より前に実行）。csproj の `StartupObject`＋`EnableDefaultApplicationDefinition=false` とセットで崩さない。
- 起動時自動更新のフィードは `UpdateFeed.GitHubRepositoryUrl`（`https://github.com/kokko-labs/QuickER` の GitHub Releases）。空文字にするとチェック無効。
- 環境変数 `QUICKER_UPDATE_FEED` による差し替えは `#if DEBUG` 限定で、配布ビルドでは分岐ごとコンパイルされない。任意のフィードを指せると更新の受け口がそのまま任意コードの配布経路になる。テストも `#if DEBUG` / `#else` で両アームを固定する。
- 利用者のオプトアウトは gui-settings.json の `CheckForUpdatesOnStartup`（既定 true。キーを持たない旧ファイルも「確認する」で読む）。`UpdateService` は設定を見る `Func<bool>` を受け、フィード解決より前に短絡する。off ならネットワークへ一切出ない。
- UI はツールバーの設定ボタン（⚙・`Settings_Caption`）の ContextMenu。言語切替はその下のサブメニュー（`Language_*` の resx キーは不変で、`StringsLocalizationTests` が値を固定する）。
- 配布物は Authenticode 未署名（SmartScreen が発行元不明の警告を出す）。この非署名と更新チェックの開示は README の Install 節が担う。
- Velopack 依存は `IAppUpdater` seam の裏に隔離し、`UpdateService` は単体テスト済み。

### 更新の適用は通常の終了経路の後だけ
- ダウンロード後に「今すぐ再起動するか」を確認し、どちらでも `PostExitActions`（`WpfApplicationExit.Pending`）へ登録して、`App.OnExit` で `WaitExitThenApplyUpdates` を呼ぶ。
  - `OnExit` は Closing の自動保存・終了連鎖の後で、終了のキャンセルとクラッシュ経路では呼ばれない。
  - その場でプロセスを終わらせる `ApplyUpdatesAndRestart` は Closing を飛ばすので使わない。
- 言語切替の再起動も同じ登録先を使う。両方あれば再起動は更新プログラムに任せる（適用の前にインストール先で動く QuickER を止めるため）。適用の段が失敗したときだけ自分で再起動する。
- `App.OnExit` は `PostExitActions.CompleteExit` を通す。DI の破棄も `ShutdownSteps.Run` の 1 段で、破棄が投げても適用・再起動へ届く。
- ガード：`LifecycleTests`（OS との境界のエンジンのアプリの一生の表。行 `update-feed-*`＝`#if DEBUG` / `#else` の両アーム・行 `update-startup-check-*`・行 `post-exit-*`）・`PostExitExhaustiveTests`（下の「再起動と単一インスタンス」）。

## 再起動と単一インスタンス
- 再起動の要求と起動処理は分けて持つ。
  - 要求は言語切替の `PostExitActions.RequestRelaunch` と更新の「今すぐ再起動」（`ScheduleUpdateApply` の `restart`）。
  - 新しいインスタンスを起動する処理は `Program.Main` が `RelaunchWith(WpfApplicationRestartService.StartNewInstance)` で 1 回だけ登録する。
  - どちらの要求でも適用の段が失敗すれば同じ起動処理で自分で再起動する。起動処理を要求側が持つと、更新の「今すぐ再起動」だけのときに適用が失敗した場合に代わりに起動する手段が無く、QuickER が閉じたまま戻らない。
  - ガード：`LifecycleTests` の行 `post-exit-apply-failure-with-update-restart-falls-back-to-relaunch-apply-fails`、`PostExitExhaustiveTests`（更新の有無と種類・再起動の要求・起動処理・座の手放しの全 60 組を流し、文書から導いたオラクル〔両方あれば更新プログラムに任せる・適用が失敗したときだけ自分で起動・起動の直前にだけ手放す・失敗した段だけを記録・2 回目は何もしない〕と照合する）、`SourceRuleTests` の規則 `program-single-instance-uses`・`program-single-instance-bypasses`（Program の配線のソース検査）。
- 自分で再起動するときは、その直前に単一インスタンスの座を手放す（`PostExitActions.ReleaseSingleInstanceBeforeRelaunch`＝`Program.Main` が `singleInstance.Dispose` を登録）。
  - 再起動は終了処理の途中で起動するので、座を持ったままだと新しいインスタンスが「既に起動中」と判断して黙って終わる。
  - ガード：`SingleInstanceGuardTests` の「再起動の直前に座を手放すので、新しいインスタンスが座を取れる」、`PostExitExhaustiveTests`・`LifecycleTests` の行 `post-exit-relaunch-releases-single-instance-first-none`。
- 単一インスタンス制御は、権限の違うインスタンス（管理者として実行）の名前付きオブジェクトを開けないとき案内を出して終わる（制御なしでは起動しない。同じ自動保存・設定を書き合うため）。
- 2 つ目の起動はアクティブ化を頼む前に前面化の権利を渡す（`AllowSetForegroundWindow(ASFW_ANY)`。渡さないと既存のウィンドウは点滅するだけ）。

## 終了の連鎖
終了処理は「中断→設定保存→エンジンの破棄→クローズ」の順で、後段ほど取り返しがつかない後始末（常駐する子プロセスの停止）が並ぶ。
前段の失敗で後段へ届かないと子プロセスが孤児として残る。終了時の設定保存はディスクフル・権限・保存先のパスに同名ファイルがある等で実際に落ち、そこで連鎖が止まると codex app-server・copilot ランタイム・claude / dotnet の子プロセスが理由も残さず居残る。

### 段ごとに ShutdownSteps.Run を通す
- 通す先は 3 つ。
  - モジュールの `OnMainWindowClosing`（`FeatureModuleShutdown.CloseAll` 経由。`App.xaml.cs` を素の foreach へ戻さない）。
  - AI チャット／モック生成の `ForceClose` の全段（AI チャット 4 段・モック生成はプローブ破棄を加えた 5 段）。`ForceClose` が中断とエンジンの破棄の両方を通す理由は `src/QuickER.AI.Mock/CLAUDE.md`「ForceClose」が正本。
  - 終了の後の処理 `PostExitActions` の各段（DI の破棄・更新の適用・単一インスタンスの座の手放し・再起動）。
- 記録先 `IShutdownFailureReporter`（Gui.Abstractions）は必須依存。null 既定も「未登録なら何もしない」も作らない（配線漏れを黙って握り潰さず DI 解決時に失敗させる）。
- 実装は決して例外を投げない。`ShutdownSteps.Run` は記録側の例外も握り潰す（契約・実装・器の三重）。漏らすと「1 つの失敗が他を止めない」という器の目的が破れる。
- 書き出し先は `shutdown-*.log` で `crash-*.log` と分ける。本文の形式は `CrashHandlingService.FormatDetails` を共有し接頭辞だけ変える（落ちていないのに crash- が増えると「落ちた」と読み違える）。段の名前は `ShutdownStepFailedException` で包んで本文の 1 行目へ載せる。
- ガード：`ShutdownChainExhaustiveTests`（4 段の成否 × 記録先の成否の全 32 組で、全段が順に実行され、失敗した段だけがその例外のまま記録され、例外を外へ出さない）・`LifecycleTests`（行 `steps-*`・`modules-*`・`crash-report-*`）・`FeatureModuleContractTests`（行 `module-*-launcher-needs-shutdown-failure-reporter`＝記録先は必須依存）・`SourceRuleTests` の規則 `app-feature-module-shutdown-uses`・`app-feature-module-shutdown-bypasses`（App.xaml.cs の配線）・`AiChatDialogForceCloseTests` の「ForceClose は設定保存が失敗してもエンジン破棄とクローズまで進む」・`MockGenerationDialogTests` の同等 1 件。
- `LifecycleTests` の行 `crash-report-consecutive-reports-do-not-overwrite-same-second` は弱い行。ログの名前はミリ秒まで持ち（`CrashHandlingService` の `yyyyMMdd-HHmmss-fff`）、時刻を差し込めないので、続けて 2 回書いても名前がぶつかる（同じミリ秒になる）ことはほとんど無い。名前がぶつかったときの経路（`FileMode.CreateNew` が断ったら短縮 GUID 付きの名前でやり直す）を壊しても、この行は捕まえない。その経路を確かめるのは、`CrashHandlingServiceTests` の「WriteCrashLog: 同じ名前を選ぶ同時の書き手でも互いを上書きしない」だけ（記録先も同じ書き出しを共有する）。

### 終了条件はメインウィンドウへ紐づける
- `ShutdownMode="OnMainWindowClose"`（App.xaml）と `MainWindow = window;` の明示（App.xaml.cs）は対で保つ。
  - AI チャット／モック生成のウィンドウは × で閉じず `Hide` するため、クローズ段が失敗して閉じ残ると、既定の `OnLastWindowClose` では見えるウィンドウが無いままプロセスが残る。
  - 明示代入が要るのは `Application.MainWindow` に入るのが「最初に構築された `Window`」だから。モジュールの初期化中に別の `Window` 派生（詳細ダイアログ等）が先に作られると、それを閉じた瞬間にアプリが終わる。
  - ガード：`SourceRuleTests` の規則 `app-shutdown-mode-declaration`・`app-shutdown-mode-overrides`・`app-main-window-assignment`。

### クラッシュ経路も同じ後始末を通す
- `Environment.Exit` で終わるクラッシュ経路は `Closing` を通らないので、`App` は `FeatureModuleShutdownOnce`（1 回だけ実行のガード）を `Closing` とクラッシュ経路で共有する。
- `CrashHandlingService.HandleCrash` の段は「緊急保存 → ログ → 機能モジュールの後始末 → 報告ダイアログ」。後始末をダイアログより前に置くのは、ダイアログを読んでいる間に落とされても孤児を残さないため。
- 終了は `HandleUiCrash` が処理を担ったときだけ行う。報告ダイアログの入れ子のメッセージループで起きた 2 回目の UI 例外は終了させない（読んでいる報告を消さない）。
- ガード：`LifecycleTests` の行 `crash-handle-failing-and-hanging-modules-reach-dialog-within-bound-hanging-cleanup`（投げる・ハングする後始末が混ざっても上限で戻りダイアログまで届く）・行 `crash-handle-ui-crash-reentry-during-dialog-exits-once-after-dialog-reentry`（ダイアログ表示中の再入は終了させない）・行 `modules-shutdown-once-second-call-does-nothing-reentry`（1 回だけ実行のガード）、`SourceRuleTests` の規則 `app-ui-crash-handler-uses`・`app-ui-crash-handler-bypasses`（App の UI 例外の受け皿は HandleUiCrash と後始末の共有インスタンスを使う）。
