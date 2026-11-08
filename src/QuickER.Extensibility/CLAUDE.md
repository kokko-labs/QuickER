# QuickER.Extensibility

フィーチャーモジュール契約（`IFeatureModule` / `FeatureToolbarItem` / `IErDiagramHost`。WPF 非依存・net10.0）と、Gui への結線の規則。

## フィーチャーモジュール方式
- DB 取込・DB 同期・AI チャット・AI モック生成・C# コード生成・クエリ定義は、この契約を介した静的モジュール合成で Gui へ結線する（着脱可能なプラグイン）。
- 依存方向は「プラグイン（Db.UI / AI.Chat / AI.Mock / CodeGen.UI の各 FeatureModule）→ 契約 ← ホスト（Gui）」。**Gui は機能の型を知らない**（機能プロジェクト参照はカタログのためだけ）。
- `IFeatureModule`：Id / ConfigureServices / Initialize（既定 no-op・Build 直後）/ CreateToolbarItems / OnMainWindowClosing。
- AI エンジン `QuickER.AI` は Extensibility を知らない。`IErDiagramToolHost` はプラグイン内アダプタで包む（層の純度維持）。
- 切り離し後の `IAppDialogService` は印刷オプションとバージョン情報の 2 メソッドのみ。
- ランチャー・アダプタ・実行サービスは各機能プロジェクト側にある。
  - DB：`DbImportCommandService`（SchemaSignature 比較の置換確認込み）/ `DbSyncCommandService`（CanRun・ツールチップは TargetDbmsChanged 購読で再評価）
  - AI：`ErDiagramHostChatAdapter` / `ErDiagramHostMockDiagramSource`
  - コード生成：`CSharpGenerationCommandService`（CurrentProvider は GetDiagram().TargetDbms から解決）/ `QueryDefinitionCommandService` / `QueryConditionRenameFollower`
  - ダイアログ提示は presenter シーム。
- カタログの 4 モジュールは、同じ契約の表で確かめる（解決できるサービス・Id・ツールバーの並び・終了時の後始末・終了失敗の記録先の要否・ホストのイベントへの追従）。モジュールごとの違いはアダプタの宣言に置き、カタログにモジュールを足すとアダプタが無い限り赤になる。ガード：`FeatureModuleContractTests`（`FeatureModuleContractTests.Axis_CoversCatalog`・行 `module-*`）。

## FeatureToolbarItem
- `INotifyPropertyChanged` 実装クラス。**実行中に切り替えて効くのは Tooltip だけ**（対象 DBMS 切替に追従してツールチップを差し替える機構。現在は全方言が DB 同期対応で実質定数だが機構は維持する）。
- **BeginsGroup は起動時の組み立てでのみ反映**される。折返しのグループ分割は `FeatureToolbarItems` の代入時に 1 回だけ走り（`MainViewModel.SplitToolbarGroups`）、ビューは分割済みのグループへ束縛するので、実行中に変えても（通知は出るが）折返しは変わらない。
- DB ツール・AI チャット・コード生成の先頭アイテムは BeginsGroup=true。モック生成は AI チャットと同じグループに載せるため false。集約後の全体先頭のみ App が false へ矯正する。
- ガード：`FeatureToolbarGroupTests`（設定後に BeginsGroup を変えても折返しが変わらない・Tooltip は実行中に切り替わる）。
- ツールバーのモジュールボタンは MainWindow の WrapPanel＋`MainViewModel.FeatureToolbarItems`／`FeatureToolbarItemGroups`。App が起動時に集約設定し、BeginsGroup 境界のグループごとにコードビハインドが ItemsControl を生成して WrapPanel へ挿入する（ウィンドウ幅不足時はくくり単位で折返し・モジュール 0 個ならセパレータごと畳む）。

## IErDiagramHost
ホスト能力は `MainViewModelErDiagramHost`（Gui）が提供する。

- IsEmpty
- **IsDirty**：未保存変更の有無。図を失う操作の確認水準の選択用（ダーティなら ConfirmWarning・クリーンなら Confirm＝`DialogServiceExtensions.ConfirmDiscard`）。
- **DiagramRevision**：図の世代。確認の前後で図が変わったかの比較用（`src/QuickER.Gui/ViewModels/CLAUDE.md`「確認の間に変わった図」）。
- GetDiagram / Providers / **TargetDbms** / AutoArrangeNewDiagram / ExecuteTool
- **ReplaceDiagram**：方言採用＋履歴なし置換＋fit。DB 取込用。現在図と同一 Id のエンティティが無ければ全体を自動整列し、あれば（マージ取込）一致分のレイアウトを引き継いで新規分だけ `LayoutAppend` で追記配置する。
- **ReplaceQueries**：差し替え＋AutoSave。
- **ColumnRenamed** / **TargetDbmsChanged** イベント。

## 列リネームのクエリ条件追従
- `QueryConditionRenameFollower` が列リネーム時の DSL 条件を追従させる。Initialize で購読し、条件を実際に書き換えたときだけ `ReplaceQueries` を呼ぶ。購読者はこのフォロワーだけ。
- 列名の Undo / Redo でも `ColumnRenamed` が戻る・進む向きで発火し、条件が列名と一緒に戻る（`DiagramChangeTracker.CreateAfterPropertyApplyAction`。向きは適用後の現在の名前で決める）。クエリ自体は Undo 履歴の外のまま。
- 新名が条件式で読めない名前（空白・記号・数字始まり・キーワード）なら書き換えない（`QueryConditionParser.IsReadableColumnName`）。差し込むと構文エラーになり、元に戻しても直らない。
- 条件の中の列名の読み方は、列の削除でクエリを名指しする判定と共有する（`QueryColumnReferences.FindInCondition`。`src/QuickER.CodeGen.CSharp/Queries/CLAUDE.md`「列の削除とクエリの参照」）。列の削除はクエリを書き換えず知らせるだけで、追従と違いホスト（GUI・MCP）側にあるのでモジュールを抜いた構成でも止まらない。
- ガード：`MainViewModelErDiagramHostTests` の Undo / Redo の 1 件・`QueryConditionRenameFollowerTests`・`QueryConditionRenamerTests`。
- モジュールを抜いた構成では列リネームのクエリ条件追従も止まる。生成前の Guid 参照検証・DSL パースが壊れを警告するので許容する。

## 同梱構成
- 同梱一覧は `FeatureModuleCatalog`（Gui）。ビルドプロパティで独立に外せる（csproj の条件付き ProjectReference と `QUICKER_DBTOOL_MODULES` / `QUICKER_AI_MODULES` / `QUICKER_CODEGEN_MODULES` 定数が連動）。
  - **`IncludeDbToolModules=false`**：DB ツールなし構成（Db.UI が外れる）。スキーマ取込・差分・同期エンジンはプロバイダ層にあり、CLI の scaffold と共有なので残る。
  - **`IncludeAiModules=false`**：AI なし構成（出力に QuickER.AI*.dll が一切出ない）。
  - **`IncludeCodeGenModules=false`**：コード生成 UI なし構成（CodeGen.UI が外れる）。エンジン CodeGen.CSharp は型解決のためプロバイダ層が参照しており残る。
- ツールバー文言・機能内メッセージの resx は各機能プロジェクト側（Db.UI / AI.Chat / AI.Mock / CodeGen.UI）に置く。
