# QuickER.AI.Mock

ER 図から Web モック画面を起こす AI チャット機能（モックフォルダ方式のセッション・ツール・ストア・プレビュー・単一 HTML 出力・モックプロジェクト生成）と `MockGenerationFeatureModule`。
Copilot のシェル許可の判定は `src/QuickER.AI/CLAUDE.md`「Copilot のシェル許可」、エンジンの破棄の作法は `src/QuickER.AI.UI/CLAUDE.md`「エンジンの破棄の作法」を参照する。

## AI モック生成

### 保存単位とフォルダ

- **保存単位はフォルダ**。`mock.json` マニフェスト（画面一覧・遷移・改訂履歴・スキーマスナップショット）＋画面ごとの `*.html` ＋共有 `style.css` のフラット構成（`MockFolderStore` / `MockManifest`）。
- 書き込みはすべて `AtomicFile`（文字コードは BOM なし UTF-8 を明示）で行う。素の書き込みは src 全体のソースの規則（`SourceRuleTests` の規則 `raw-file-writes`）が拒否する。
- **変更はマニフェストの複製へ適用し、ファイル → `mock.json` の書き込みが成功してから保持中のマニフェストを差し替える。** 書けなかった変更がメモリに残って次の保存へ紛れ込まないため。ガード：`MockFolderTests`（行 `save-screen-manifest-write-failure-does-not-leak-into-later-save`・`*-manifest-write-failure-*`）。
  - `RemoveScreen` だけは HTML の削除が先。`mock.json` で失敗しても remove のやり直しで自己回復する。
  - 失敗で残る一時の食い違いは 3 つだけ：`SaveScreen` が `mock.json` で失敗して残る「宣言の無い HTML」、`RemoveScreen` が `mock.json` で失敗して残る「HTML の無い宣言」、`SaveStylesheet` が `mock.json` で失敗して残る「新しい CSS」（CSS を先に書き、マニフェストは差し替えない＝上の順と合う）。どれもその画面・CSS への次の保存の成功で解ける。
  - 保存単位の操作の列（`MockFolderTests`）は、組（`MockFolderHarness`）がどの手の後にも見張る：失敗した手は `mock.json` のバイト列もメモリのマニフェストも変えない・メモリは `mock.json` と一緒にしか変わらない・一時ファイルが残らない・宣言と HTML の整合と `style.css` の内容（上の 3 つだけを許す）・`SourceSchema` は初回ターンの成功でだけ変わる・エンジンの完了 1 回につき `TurnCompleted` 1 回・保存の通知は成功したツールのときだけ。
    開く手（`Open`）と画面を読む手（`GetScreenHtml`）も同じ見張りを通る（失敗した Open は前のストアのまま。行 `open-refused-newer-format-keeps-manifest-and-current-store`・`read-invalid-screen-file-name-is-refused-without-reading`、別のプロセスが掴んだ `mock.json`・画面の HTML の行 `open-manifest-held-by-another-process-throws-io-and-keeps-manifest`・`read-screen-held-by-another-process-throws-io-and-keeps-manifest`）。
- 新規作成の `mock.json` は置き換えない書き込み（`AtomicFile.CreateText`）で作る。存在の確認の後に別のプロセスが作った `mock.json` を上書きせず、事前の確認と同じ例外にする。ガード：`MockFolderTests`（行 `create-new-keeps-foreign-manifest-written-after-check`）。
- **手で書いた `mock.json` の不正な形は、`Open` が破損として断る**（JSON として読めない・型の合わない要素・新しい版・不正な画面のファイル名・同じ画面のファイルの重複）。黙って直すのは、情報を持たない `null`（文字列は空へ、リストの要素の `null` は捨てる）と、画面の `entities` だけ。
  - **画面の `entities` は、断らずに保存の経路と同じ `MockFolderStore.NormalizeEntities` に通して直す**（名前の前後の空白を除く・名前が空の宣言を捨てる・同じ名前は操作の和で 1 つにまとめる・操作を C → R → U → D・有効な操作の無い宣言を捨てる・何も残らなければ宣言なし＝null）。警告は出さない（`Open` に知らせる経路が無い）。`Open` は書かないので、`mock.json` は次の保存で直った形になる。
    宣言の意味は正規化した後の形で決まる。直さないと、設計書の CRUD 表で綴り違いの列に別の宣言の操作が入り、再開のプロンプトに名前の無い宣言や `urc` のような操作がそのまま AI へ届き、別の画面を保存しても書いたまま残る。CRUD の申告 1 つの書き損じでフォルダごと開けなくするのは重すぎるので、断らない。
  - ガード：`MockOutputsTests`（行 `load-hand-written-entities-are-normalized`＝開いた後のマニフェスト・設計書の CRUD 表・再開のプロンプト・`Open` の前後で `mock.json` のバイト列が変わらないこと）・`MockFolderTests`（行 `open-hand-edited-nulls-are-normalized-for-later-steps`）。mutation 実証済み（読み込みの正規化を外す・同じ名前のまとめを外す・名前の比較を大文字小文字を区別する形にする、のどれも赤）。
  - 画面の重複は、ファイル名を `OrdinalIgnoreCase` で比べて判定する（ストアの画面の同定と同じ比較。ファイル名の検証の後に見る）。開くと保存（upsert）は先頭だけを更新し、削除は両方を消し、単一 HTML・設計書・ランナーの画面の数にも 2 回入る。どちらを残すかは決められないので、片方を捨てずに断る。文言は `Mock_ManifestDuplicateScreenFileFormat`（UI の言語に従う）で、2 件目のファイル名を名指しする。
  - ガード：`MockOutputsTests`（行 `load-duplicate-screen-file-is-refused-*`＝同じ綴り・大文字小文字だけの違い × UI の言語 2 つ）。mutation 実証済み。
- 会話開始時にフォルダを指定する（空フォルダ＝新規 `CreateNew`／既存モックフォルダ＝再開）。以降は 1 ターンごとにライブ保存する。
- **再開は状態再開。** 会話履歴は保存せず、フォルダの現状（画面一覧・遷移・改訂・共有 CSS・最新スキーマ）をプロンプトへ注入して続きから作る。バックエンド非依存で、API キー / Codex / Claude Code で同じ再開ができる。

### 初回ターン

- **初回ターン（会話の開始）は成功で終わったときだけ済みにする。** エンジンの多くは失敗・中断を例外でなく `TurnCompleted(false)` で返す。送信の戻りで済みにすると、開始のプロンプト（スキーマ／再開情報）が AI へ届かないまま次の送信をフィードバックとして送り、再開の比べる基準（`SourceSchema`）も「伝えた」ことになって次の再開が「変更なし」になる。
- VM は送る前に `_firstTurnPending` を立て、`ApplyTurnCompleted` で `_firstMessageSent = result.Success` にする（送信の例外では立てない）。
- セッションは送る前に保存するスキーマを控え（`_pendingSourceSchema`＝新規はスナップショットが空のときだけ）、エンジンの完了で成否を問わず取り除き、成功のときだけ `UpdateSourceSchema` する。**完了は送信の戻りより先に届くことがある**ので、控えは送る前に置く。
- 書けなければそのターンを失敗（`Mock_SourceSchemaSaveFailedFormat`）として転送する（VM は初回をやり直す）。
- ガード：`MockFolderTests`（行 `session-start-new-turn-*-does-not-snapshot-until-success`・`session-start-resume-turn-*-keeps-schema-changed-marker`・`session-feedback-after-failed-initial-turn-does-not-snapshot`・`session-feedback-after-initial-send-exception-does-not-snapshot`・`session-start-snapshot-write-failure-forwards-failed-turn`。組がどの手の後にも「`SourceSchema` は初回ターンの成功でだけ、控えたスキーマへ変わる」と「エンジンの完了 1 回につき `TurnCompleted` 1 回」を見張る）・`MockDialogTests`（ダイアログの側。行 `converse-first-turn-*-retries-as-initial-prompt`・`converse-first-send-exception-retries-as-initial-prompt`。組がどの行でも「初回ターンの済みは成功で終わったときだけ立つ」を見張る）。mutation 実証済み。
  VM の送信の例外で印を戻す 1 行は外しても緑のまま（次の送信が印を立て直すので二重の守り）。

### ツールと検証

- ツールは英語正本の 4 つ（`save_screen`＝完全 HTML の upsert・遷移宣言・改訂メモ／`remove_screen`／`save_stylesheet`／`get_screen`）。定義は `MockFolderDesignTools`（`ErDiagramToolCatalog` と同流儀）。
- **検証は警告のみ。** `MockContentValidator` は外部参照・リンク切れ・style.css 未参照を警告にし、拒否は空と非 HTML（`<html>` 不含）だけ。
- **`save_screen` の `entities`・`transitions` の壊れた要素は、要素ごとに警告して読み飛ばす**（JSON のオブジェクトでない要素・名前が空のエンティティ）。黙って捨てると、AI は宣言が記録されなかったことを知らずに進む。ER 図のツールのように `ToolArgumentShape` で呼び出しごと断る形にはしない（上の「警告のみ」の流儀。空でない `entities` の要素が 1 つも有効でなければ、既存の宣言を残して総括の警告も出す）。
  - ガード：`MockFolderTests`（行 `session-tool-save-screen-non-object-entities-warn-and-skip`・`session-tool-save-screen-non-object-transitions-warn-and-skip`・`session-tool-save-screen-nameless-entity-warns-and-skips`・`session-tool-save-screen-all-invalid-entities-keep-declarations-*`）。mutation 実証済み。
- **同じ画面の同じ名前（`SchemaNames` の規則＝大文字小文字だけの違いを含む）の宣言は、操作の和で 1 つにまとめる**（綴りと位置は初出。`MockFolderStore.NormalizeEntities` が持ち、保存の経路と `Open` が共有する）。片方を捨てると利用者の申告が消え、まとめないと設計書の CRUD 表は 1 列に先頭の操作だけを出し、再開のプロンプトには 2 回出る。
  - `save_screen` では、まとめた名前ごとに英語の警告を 1 件出す（上の「警告のみ」の流儀）。手で書いた `mock.json` を `Open` で直すときは出さない。
  - ガード：`MockFolderTests`（行 `session-tool-save-screen-duplicate-entity-names-are-merged-with-warning`）・`MockOutputsTests`（行 `load-hand-written-entities-are-normalized`）。mutation 実証済み（まとめを外す・名前の比較を大文字小文字を区別する形にする・警告を外す、のどれも赤）。
- **ツールの応答の改行は LF だけで組む**（警告の連結 `AppendWarnings` は `AppendLine` を使わない。ER 図のツールの決まり〔`src/QuickER.Mcp/CLAUDE.md`「ツールの応答の改行」〕にそろえる）。`get_screen` は画面の HTML をそのまま返すので、改行に手を付けない。
  - ガード：`MockFolderTests`（組 `MockFolderHarness` の不変条件 9＝どの行のどのツールの応答にも CR が無い〔`get_screen` を除く〕・行 `session-tool-save-screen-non-object-entities-warn-and-skip` が警告つきの応答が複数行で LF だけであることを表明する）。mutation 実証済み（`AppendLine` へ戻すと警告の出る 10 行が赤）。

### プレビュー

- プレビュー（`MockPreviewPanel`＝WebView2）は、遷移を**フォルダ直下の `*.html` だけ許可**する（許可フォルダ付きプレビュー要求）。画面間リンク遷移に追従し、サイドバー選択と相互同期する。
- サブリソース（img / script / css / fetch / WebSocket）は `WebResourceRequested` で**既定拒否**する。`IsResourceAllowed` は許可フォルダ内なら拡張子不問。
  - 遮断は無言。外部参照は保存時に `MockContentValidator` が警告済み。
  - フィルタは要求元すべて（別オリジンの iframe を含む）の 3 引数版で登録し、Runtime 122 未満でラッパーが `NotImplementedException` を投げたときだけ 2 引数版へ落とす。
  - iframe の遷移は許可・遮断だけで、現在ファイルを追跡しない。
  - 拒否の応答は初期化のときに 1 つ作って使い回す（使い回しても要求はネットワークへ出ないことを実ランタイムで確認済み）。作れなければプレビューを使えない扱いにし、要求の途中で応答を付けられなければプレビューを止める（閉じる側に倒す）。
- ダウンロード（`download` 属性・スクリプトが作った blob / data）は `DownloadStarting` ですべて取り消す。AI の HTML がダウンロードフォルダへファイルを置き、WebView2 の UI から開かれるのを防ぐ。
- ガード：`MockPreviewPanelRuntimeTests`（ダウンロードの取り消し・応答を作れないときの扱い・`Preview_BlockedResponseMissingMidRequest_StopsPreview`＝使えない表示への切り替えと、以後の表示の要求を受け付けないこと）。mutation 実証済み。

### 出力と設計書

- 単一 HTML 出力は `MockBundleExporter`。決定的なハッシュルーター結合で、各画面 body を `<section data-screen>` へ連結し、`href="Foo.html"` → `#Foo`（フラグメントは捨てる）、共有 CSS をインライン化する。相対リンクの抽出と画面 ID の規則は `MockScreenLinks` を検証・設計書と共有する。
- 設計書出力は `MockDesignDocExporter`（決定的・AI 不使用）。モックフォルダ直下の `README.md` に、画面一覧・Mermaid 遷移図・**画面 × エンティティの CRUD 表**・regex による画面項目抽出を書く。
  - 見出しは UI 言語追従（resx `MockDoc_*`）、改行は LF 固定。
  - 初回は「設計書出力」ボタンのオプトインで書き出し、以降は README.md が在る限り画面の save / remove に無音で自動追従して再生成する。ボタンはモックプロジェクトの生成中は押せない（「ダイアログの状態の不変条件」）。`save_stylesheet`・スキーマ更新では再生成しない（内容に影響しないため）。
  - CRUD 表は `save_screen` の `entities` 申告（省略＝既存維持・空配列＝消去・正規化は C → R → U → D・図に無い名前は警告のみ）を、遷移図の直後に載せる。
    行は全画面（マニフェスト順・未宣言画面は空セル行）、列は宣言エンティティ名の初出順、セルは正規化済み操作文字列。宣言ゼロならセクションを省略する。
- ガード：`MockOutputsTests`（リンクの表記の行 `link-*`＝同じリンクを `MockScreenLinks`・検証器・単一 HTML へ流し、検証が解決済みとしたリンクは単一 HTML で `#画面ID` へ書き換わる・画面 ID の行 `id-*`＝画面 ID・設計書のノード ID・単一 HTML の section・行 `doc-*`・`bundle-*`。単一 HTML と設計書を出すどの行にも、決定的であること・外部参照の無い入力から外部参照を出さないこと・設計書に CR と U+2028 が無いことを掛ける）。

### モックプロジェクト生成

- ターゲットは `MockProjectTarget` の 2 種（blazor 既定＝Blazor Web App・グローバル InteractiveServer・モック HTML / style.css を可能な限り移植／wpf）。差分は `MockProjectTargetProfile`（csproj / README・プロンプトフラグメント・UI 成果物パターン `*.xaml` / `*.razor`）へ集約する。プロンプトは `MockProjectPromptBuilder` が単一正本（フラグメント合成）。UI は ComboBox で選ぶ。
- **プロンプト・`README-QuickER.md`・ER スキーマ本文（`MockSchemaSerializer`）は英語固定。** 「対話チャット＝UI 言語追従・ヘッドレス実行と機械向け診断＝英語固定」の線引き。警告・検証エラーも英語固定で、成功文は UI 追従。CJK 混入は英語ガードテスト群が検知する。
- 流れ：決定的スキャフォールド（`MockProjectScaffoldService`＝データ層コード ＋ csproj ＋ README ＋ モックフォルダを `design/mock/` へ同梱）→ 実行器抽象 `IMockProjectAgent`（選択中バックエンドに連動・追加指示欄を任意で連結）で UI 層を生成 → 共有オーケストレータ `MockProjectAgentRunner`。
- `MockProjectAgentRunner` は全体タイムアウト（既定 30 分）・成果物検証・最終ビルド前の変更検知・最終 `dotnet build` の独立検証を行う（実行器の自己申告を信じない）。
- 最終ビルドには専用タイムアウト `DefaultBuildTimeout`（10 分）を掛け、超過は専用文言でタイムアウト報告する。全体タイムアウトはエージェント実行までで解けており、外部トークンには時間制限が無い。値の根拠は、パッケージ・HTTP 両キャッシュを空にした完全コールドの同形 Blazor ソリューションが 6.8 秒という実測の約 88 倍。
- 出力フォルダの上書きは `MockProjectOverwriteScanner` が既存物（自分の .sln・直下の別 sln / slnx / csproj・プロジェクトフォルダとその 4 生成物）を列挙し、VM が `ConfirmWarningDetails` で確認する。キャンセルなら出力フォルダ不変。
  - **直下の別ソリューション・別プロジェクトは、隠し属性・システム属性でも出す**（列挙は `AttributesToSkip = 0`）。属性で確認から外れる同居物を作らない（自分のものは `File.Exists` なので、もともと隠しでも出る）。
  - **出力フォルダの中を列挙できない（一覧の読み取りを拒否する ACL・中を列挙できない reparse フォルダ）ときは、例外にせず、出力フォルダ自身を `.\` の 1 件として一覧に出す**（読めた分は捨てる）。
    走査は生成コマンドの try の外から呼ばれるので、投げると未処理例外になる。`IgnoreInaccessible = true` で黙って空にすると、中の別プロジェクトを見落としたまま確認なしに進む（フェイルオープン）。中を確かめられないものは確認に出す（「最終ビルド前の変更検知」が一覧を読めないフォルダを毎回出すのと同じ向き）。
    続行すると、列挙できない reparse フォルダでは書き込みもできないので、スキャフォールドが try の中で失敗として報告される。
  - ガード：`MockBuildBoundaryTests`（入口 D の行 `overwrite-hidden-foreign-solutions-are-listed`・`overwrite-acl-denied-output-folder-is-listed-as-itself`・`overwrite-unlistable-reparse-output-folder-is-listed-as-itself`＝素の列挙が実際に投げることも同じ行で確かめる。ほかの種別も入口 D の行 `overwrite-*` が見る）・`MockBuildBoundaryTests.Coverage_MatchesGaps`（入口 D の網羅しない組は無い）。mutation 実証済み（失敗を空として返す・`IgnoreInaccessible` を true へ戻す・`AttributesToSkip` を既定へ戻す、のどれも赤）。

### 実行器 4 種

- **Claude Code**：ヘッドレス・自己修正。許可ツール `Edit` / `Write` / `MultiEdit` / `Bash` ＋ `acceptEdits`。**Bash は無制限。**
- **Codex**：既存 app-server 統合を Cwd = 出力先・approval never・workspace-write で駆動。自己修正。dynamicTools は使わない。
- **Copilot**：copilot CLI をヘッドレス実行。自己修正。ER 設計ツールは非公開で、組込みのファイル編集 / シェル実行に委ねる。`AllowWorkspaceTools` は出力フォルダ配下のみ自動承認する。
  判定の正本 `CopilotWorkspacePermissionPolicy` は `src/QuickER.AI/CLAUDE.md`「Copilot のシェル許可」を参照する。
- エージェント型 3 実装はプロンプト正本 `MockProjectPromptBuilder` を共有する。
- **追加の指示は、4 実行器とも 1 つのプロンプトの末尾へ 1 回だけ、`MockProjectPromptBuilder` の 1 つの関数（見出し `# Additional instructions`・空白だけなら付けない・前後の空白を除く）で連結する。** エージェント型は作業全体を依頼する初回のプロンプト、API キーはシステムのプロンプトに入れる。
  - API キーで実行全体の規則を持つのはシステムのプロンプトだけで、共通部・画面・修正のプロンプトはそのターンに閉じた依頼（その末尾に付けると共通部だけの指示に読める）。エンジン（`ChatTurnEngine`）がシステムのプロンプトを履歴の先頭に置いて毎ターン全履歴を送り直すので、1 回でどのターンにも届く。ターンごとに入れるとトークンが画面の数だけ重なるだけ。
  - ガード：`MockAgentContractTests`（行 `prompt-additional-instructions-appended-after-heading`＝前後に空白・改行を付けた入力が、除かれた形で宣言 `AdditionalInstructionsPrompt` のプロンプトの末尾に載り、見出しはシステムと全ターンを通して 1 回・行 `prompt-blank-additional-instructions-add-no-heading`）。mutation 実証済み（API キーで渡さない・宣言を初回のプロンプトにする・見出しを 2 回付ける・前後の空白を除かない、のどれも赤）。
- **API キー**：`emit_file` 固定パイプライン（共通部 → 各画面の決定的一発生成）。読取 / ビルドツールは与えない。中間ビルド失敗時のみ修正 1 回。UI に「自己修正なし」注記。
  - パス保護は**拡張子ホワイトリスト**（プロファイル宣言〔WPF = .xaml / .cs、Blazor = .razor / .css / .cs〕∩ 中央上限集合 `MockProjectEmitTools.SupportedEmitExtensions` ＝実効の許可集合。拒否メッセージへ許可拡張子を列挙する）。
  - 加えて保護フォルダ（Generated / design / **obj / bin**）、README / .sln / .csproj / NuGet.Config、`..` / 絶対パス / `:` を拒否する。詳細は「提出パスの判定」。
- **エージェント型の実行権限は QuickER 側で絞らない。** ユーザーが導入した CLI をその CLI 自身の権限モデルで動かす方針で、QuickER が設計した境界は API キー方式だけ（Claude Code は `Bash` から任意コマンドを実行できる＝承知のうえの非対称）。4 バックエンドの権限差の正本は `docs/ai-chat.md`（.ja）の「注意」節の表。
- 実行器 4 種の共通の契約（結果の写し方・未ログイン・進捗・取り消しと中断・続行の促し・破棄・起動の設定・プロンプト・追加の指示）は、実行器の網が同じ行で全実行器へ流す。軸の正本は `IMockProjectAgent` の具象の型で、実装を足すとアダプタが無いことで赤になる。
  - 実行器ごとの違いはアダプタの性質と値（`BackendName`・`FinalBuildEscapesSandbox`・`AdditionalInstructionsPrompt`）の宣言が `src/…:行` の出典つきで持つ。アダプタと行の中で実行器を名指しして分岐しない。
  - 持たない性質の組（Accepted）と、直す区切りの決まった不具合の組（Pending。いまは空）は、宣言から導かず手で書いた一覧に置く。宣言だけを書き換えると網羅の照合が赤になる。
  - 起動の設定（許可・サンドボックス・ツール・作業フォルダ・システムプロンプト）と最終ビルドの境界越えの宣言は、アダプタの期待との一致で固定する。
- ガード：`MockAgentContractTests`（`MockAgentContractTests.Row_Holds` の行 `launch-options-match-declaration-*`・`declaration-final-build-escapes-sandbox`・`MockAgentContractTests.Coverage_MatchesGaps`・`MockAgentContractTests.Declarations_CoverEveryCapabilityWithEvidence`・`MockAgentContractTests.Axis_CoversAuthoritativeValues`）。ランナーの流れ（成果物・画面の数・最終ビルド・結果の分類）は `MockRunnerTests`（行ごとにランの結果を分類して行の宣言と照合する。ターゲットに依らない流れの行は WPF と Blazor の両方で流す＝Id は幹の末尾に `-wpf` / `-blazor`。幹 `agent-not-logged-in-reports-not-logged-in`・`boundary-declined-confirmation-skips-build-as-not-verified` ほか）。mutation 実証済み。

### 最終ビルド

- `DotnetBuildRunner` は次を守る。
  - **(a) 自分の `.sln` をフルパスで明示する。** フォルダ指定だと別ソリューション同居で MSB1011 になる（対象が一意に決まらない）。
  - **(b) MSBuild の自動 import を 5 プロパティで無効化する。** `ImportDirectory{Build,Solution}{Props,Targets}` と `ImportDirectoryPackagesProps`。実測でソリューション・プロジェクト双方に有効。
  - **(c) 隔離引数 3 つ。**
    - `-noAutoResponse`：`Directory.Build.rsp` の `-logger:<DLL>` が採用され、任意アセンブリをビルドプロセスへ読み込ませられるのを塞ぐ（実測で確認）。5 プロパティ自体は rsp から打ち消せない（同じ `-p` は後勝ちでこちらが後ろに来る）。塞いでいるのは加算される switch の経路。
    - `-nodeReuse:false` / `-p:UseSharedCompilation=false`：AI が書いたタスク DLL・アナライザを常駐プロセスへ残さない。
- **取り消したら、ビルドのプロセスツリー（dotnet と、それが起こした子）ごと止めてから抜ける。** 読み取りは取り消しのトークンで先に終わるので、止めなくても速やかに抜けてしまい、子のプロセスがビルドを続けたまま残る。
  - ガード：`DotnetBuildRunnerCancellationTests.RunAsync_CanceledWhileRunning_KillsAndThrows`（孫が開いたまま持つ印のファイルを、取り消しの前は排他で開けず・後は開けることで、子も孫も残らないことを見る）。mutation 実証済み（`TryKill` を消す・`entireProcessTree: false` のどちらも赤）。
- **祖先の `global.json` / `NuGet.Config` は無効化しない。** SDK のピン・社内フィード喪失の副作用が大きい。「置き場所から独立」は (b)(c) の範囲。正本は `docs/ai-chat.md`（.ja）の「注意」節。
- **最終ビルドは、直前に `obj` / `bin` を削除する。** `obj/{プロジェクト}.csproj.*.targets` / `*.props` は `Microsoft.Common.targets` のワイルドカード import で読み込まれる（実測で確認）ので、生成中に AI が `obj` へ書いたターゲットは検証ビルドで実行される。`nuget.g.targets` だけは復元が作り直すが、同じワイルドカードに当たる別名は残る。
  - 削除は `MockProjectBuildBoundary.ClearBuildOutputs` が行い、**読み取り専用の属性を外してから消す**（Windows の `Directory.Delete(recursive: true)` は外さないので、読み取り専用のファイル 1 つで削除が失敗する）。
  - リンクならリンクだけを外す。判定は `MockProjectFiles.IsLink`（`LinkTarget`）。`Directory.Delete(recursive: true)` 自身もリンクを辿らないが、明示チェックで二重化する。
  - **消せずに残ったものは黙って進まず、変更検知と同じ確認一覧へ回す**（フェイルクローズ）。変更検知は obj / bin をフォルダ自身も含めて見ないので、拾えるのはこの残存検査だけ。
    一覧は差分と同じ表記で重複なく 1 つにまとめる。確認手段が null ならビルドしない（`BuildDeclined`）。境界越えを宣言しない実行器ではログへ残すだけ。
- ガード：`MockBuildBoundaryTests`（最終ビルドの境界の表の入口 E の行：`args-build-targets-own-solution-path`＝パス明示・`args-isolation-arguments-are-passed`＝隔離引数 3 つ・`args-ancestor-imports-are-disabled`＝自動 import 無効化 5 プロパティ）・`MockRunnerTests`（行 `build-targets-own-solution-path`＝自分の sln のパス・`build-timeout-reports-timed-out`＝最終ビルドのタイムアウト（ターゲット 2 つ）・`artifacts-outside-project-folder-not-counted`＝範囲外の csproj では合格しない）・`MockBuildBoundaryTests`（最終ビルドの境界の表の入口 B の行：`clear-obj-and-bin-are-deleted-before-build`＝ビルド直前の obj / bin 削除・`clear-obj-junction-removes-link-and-keeps-target`＝ジャンクションのリンク先温存・`clear-read-only-obj-targets-never-reach-build-unconfirmed-*`＝読み取り専用の obj/*.targets を確認なしにビルドへ渡さない性質・`clear-read-only-obj-targets-are-deleted-before-build`・`clear-read-only-bin-file-is-deleted-before-build`・`clear-undeletable-obj-file-*`＝削除できない obj のファイルは確認一覧・確認手段なしでビルドしない・境界越え非宣言ではログだけ・`clear-undeletable-obj-junction-is-listed-once-without-traversal`・`clear-hidden-obj-entries-are-deleted-before-build`＝隠しの obj も消す・`clear-obj-file-symlink-removes-link-and-keeps-target`・`clear-unlistable-reparse-obj-is-deleted-before-build`・`clear-unlistable-reparse-folder-in-obj-is-listed-once`＝消せない reparse フォルダはそのフォルダを 1 件で確認一覧へ・`clear-acl-denied-obj-is-listed-as-folder`＝読めない obj は obj を 1 件で確認一覧へ・`clear-missing-obj-and-bin-asks-nothing`。ランナー経由の全行に「境界越え非宣言は確認しない・確認を出したら答えのとおりにビルドする・未検証ならビルドしない」の照合が掛かる）。mutation 実証済み。
  読み取り専用の解除を外しても `clear-read-only-obj-targets-are-deleted-before-build` の 1 行だけが赤になる（性質の行は残存検査が拾うので緑のまま）。

### 成果物の探索

- 成果物の探索はプロジェクトフォルダ配下だけを、アクセス拒否を無視して走査する。最終ビルド前の変更検知はこの走査を使わない（アクセス拒否を無視するとフェイルクローズが破れる。「最終ビルド前の変更検知」を参照）。
- 素の `Directory.EnumerateFiles(..., AllDirectories)` は権限のないサブフォルダ 1 つで `UnauthorizedAccessException` を投げ、`MockProjectAgentRunner` の呼び出しは try の外なのでランごと落ちる（ログにも残らない）。
- 範囲を出力フォルダ全体にすると、同居する無関係な `.csproj` / `.xaml` や前回実行の `obj` / `bin` で「AI が生成できた」と誤判定する。範囲は `{出力フォルダ}/{プロジェクト名}/` 配下だけ。
- 共有ヘルパーは `MockProjectFiles`（`IgnoreInaccessible=true`・`obj` / `bin` 除外）1 つ。ランナー・Codex / Copilot の承認待ち判定（`HasAnyInProject`）・API キーのデータ層要約（`SummarizeGeneratedContracts`）の 4 箇所が共有する。1 箇所でも素の列挙へ戻すとその経路だけが静かに落ちる。
- 再帰は、リンクと中を列挙できない reparse フォルダ（`MockProjectFiles.IsOpaqueDirectory`＝変更検知と同じ判定）の中へ入らない。辿ると、外を指すジャンクション 1 つで出力フォルダの外を数え、列挙できないフォルダでは `IOException` でランごと落ちる。
- ガード：`MockBuildBoundaryTests`（最終ビルドの境界の表の入口 C の行 `enumerate-*`＝obj / bin 除外・起点直下だけの除外・範囲・起点が無い・拒否 ACE のフォルダでも落ちない〔素の列挙が実際に投げることも同時に確認〕・列挙できない reparse フォルダでも落ちない・ジャンクションの先を数えない・リンクでない reparse フォルダの中は数える・読み取り専用と掴まれたファイルは数える〔探索はファイルを開かない〕・隠し属性のファイルと隠しのフォルダの中は数えない〔特性の固定。数えない向きは成果物の検証を甘くしない〕）・`MockRunnerTests`（行 `artifacts-outside-project-folder-not-counted`＝範囲外の csproj では合格しない）。mutation 実証済み。

### 最終ビルド前の変更検知

- 最終ビルドは実行器のサンドボックスの外・ユーザー権限で走る。QuickER の処理が CLI の権限モデルを越える唯一の地点なので、`IMockProjectAgent.FinalBuildEscapesSandbox` が true の実行器（Codex / Copilot＝ビルド設定は書けるがサンドボックス外でコマンドを実行できない。Claude Code と API キーは false＝Claude Code は無制限 `Bash`、API キーは提出ホワイトリスト）のランで行う。
- スキャフォールド直後（`RunAsync` の入口）に出力フォルダの内容ハッシュを記録し、**obj / bin 削除 → 差分 → 確認 → ビルド**の順で、UI 層のソース・静的資産以外の追加・変更を `ConfirmWarningDetails` で確認する（`MockProjectBuildBoundary`）。削除する範囲と検知しない範囲を揃えるための順序。
- キャンセルはビルドを実行せず `MockProjectAgentResult.BuildDeclined` で未検証として完了し、成功扱いにしない。
  - **提示は `ShowError` でなく `ShowInformation`。** 実行しなかったのは利用者自身の選択で失敗が起きたわけではなく、Error は「すでに発生した失敗の報告」専用のため。
  - フラグは `MockProjectGenerationResult.BuildDeclined` として VM まで運ぶ。メッセージ文字列で判別しない。ガード：`MockDialogTests`（行 `generate-build-declined-shows-information-not-error`＝検証ビルドの取り消しはエラーでなく情報で案内する・`generate-wires-build-confirmation`＝VM が確認 seam を配線する）。
- 差分の判定は `MockProjectBuildBoundary.DetectUnexpectedChanges`。
- 確認 seam は VM → `IMockProjectGenerator.GenerateAsync(confirmBuild:)` → ランナーへ渡る `Func<IReadOnlyList<string>, bool>`。**null ＝確認手段が無い＝ビルドしない**（配線漏れを黙って承認へ倒さない）。
- `MockProjectBuildBoundary.SafeExtensions` は**通す側の列挙**（UI 層のソース `.cs` / `.razor` / `.xaml` と静的資産 `.css` / `.js` / `.html` / 画像）で、危険なファイル名・拡張子の列挙にしない。
  MSBuild / dotnet が自動で読む入口は版とともに増えるので、名前の列挙では原理的に追いつけない（`MockProjectEmitTools.ResolveEmitPath` と同じ理屈）。
  - 実測：`Microsoft.NET.Sdk.Web` の `dotnet msbuild -pp` の import 実体 301 件は `.targets` 211 / `.props` 86 / `.csproj` 3 / `.pubxml` 1 のみ。`$(MSBuildProjectFullPath).user` の import 宣言が存在し、実際に `.csproj.user` へ置いたターゲットは隔離引数一式を付けたビルドでも実行された。出力フォルダの `global.json` は MSBuild より前に dotnet が読む。
  - `.json` / `.props` / `.targets` / `.user` / `.pubxml` / `.config` / `.rsp` / `.csproj` / `.sln` をホワイトリストへ入れると、その入口が無言で開く。
- **比較は内容の SHA-256 で行う。** 更新日時・サイズはエージェントが書き込み後に元へ戻せる（`File.SetLastWriteTimeUtc`）。スナップショットも `SafeExtensions` のファイルは読まない（比較しないため）。
- **列挙は `AttributesToSkip = 0`。** 既定の `EnumerationOptions` は Hidden / System を飛ばすため、隠し属性を付けた `*.csproj.user` が差分から消える。
- **reparse point を `AttributesToSkip` で飛ばしてはいけない。** 外を指すファイルのシンボリックリンク（`X.csproj.user`）まで列挙から消えて回避路になる。止めるのは再帰だけ。
- **ディレクトリのリンク（ジャンクション・シンボリックリンク）の中へは再帰せず、リンクそのものを 1 件として比較する**（`FileSystemEnumerable` の `ShouldRecursePredicate`）。辿ると `C:\` を指す 1 つでディスク全体をハッシュし続け（走査は中断トークンを見ない）、外のファイルが出力フォルダ内の相対パスで一覧に紛れ込む。値は接頭辞付きのリンク先。**名前の拡張子で免除しない**（`styles.css` という名前のジャンクションも出る）。
- **リンクの判定は `MockProjectFiles.IsLink`（`LinkTarget`）で、属性ビットでは決めない。** OneDrive の Files On-Demand のようなリンクでない reparse フォルダは中身を読み書きでき、属性ビットで止めるとそこへ書かれたファイルが差分から消える（obj / bin の削除も同じ判定を使う）。
- **中を列挙できない reparse フォルダは中へ入らず、毎回「変更」として一覧に出す。** 再帰すると列挙が `IOException` で止まりランごと落ちる。中身を確かめられない以上、変化が無くても出す（フェイルクローズ）。
- **一覧を読めないフォルダ（アクセス拒否・出力フォルダ自身を含む）も、中を読まずにフォルダ自身を毎回「変更」として一覧に出す。**
  変更検知の走査は成果物の探索と分け、`ComparedLevelOptions`（`IgnoreInaccessible = false`）で 1 段ずつ開く `CollectComparedEntries` で行う。開けなかったフォルダ・途中で列挙が失敗したフォルダは、読めた分も捨てて 1 件にする（`Entry.IsUnlistable`・出力フォルダ自身は `.`）。指紋を持たないので記録されず、名前の拡張子でも免除しない。
  エージェントは出力フォルダの ACL を変えて自分に一覧の読み取りだけを拒否でき、ファイルは名前で開けるので MSBuild は `.csproj.user` を読む。アクセス拒否を無視する走査では差分が空になって確認なしにビルドへ進む（実 dotnet build でターゲットの実行を確認済み）。
- **削除は確認に出さない**（記録にあって今は無い項目を走査しない）。削除したファイルはビルドが失敗するだけで、ビルドに何かを実行させはしないため。ガード：`MockBuildBoundaryTests`（行 `change-deleted-file-is-not-confirmed`＝意図として行に明記）。
- ガード：`MockBuildBoundaryTests`（最終ビルドの境界の表の入口 A の行 `change-acl-*`・`change-snapshot-acl-*`＝出力フォルダ・サブフォルダの拒否 × スナップショットとラン・記録の時点から読めないフォルダ・確認の要らない拡張子の名前のフォルダ）。
- ガード：`MockBuildBoundaryTests`（入口 A の行：`change-junction-is-listed-without-traversal`＝外を指すジャンクションは辿らずリンク自体を出す／`change-file-symlink-csproj-user-is-confirmed`／`change-csproj-user-added-is-confirmed`／`change-global-json-at-output-root-is-confirmed`／`change-ui-source-only-is-not-confirmed-*`＝UI ソース・静的資産のみ〔大文字小文字を無視〕／`change-rewritten-csproj-with-restored-timestamp-is-confirmed`／`change-hidden-csproj-user-is-confirmed`／`change-non-escaping-agent-is-not-confirmed`／`change-declined-confirmation-skips-build-as-not-verified`／`change-unwired-confirmation-skips-build`／`change-under-project-obj-is-not-confirmed`・`change-nested-obj-below-project-root-is-confirmed`＝除外は {プロジェクト}/obj だけ／`change-build-input-extension-is-confirmed-*`＝自動で読まれる拡張子は実ファイルを書くと確認に出る／`change-non-link-reparse-folder-*`〔サードパーティのタグにディレクトリビットを付けて管理者権限なしで再現〕／`change-unlistable-reparse-folder-is-always-listed`／`change-read-only-csproj-user-is-confirmed`・`change-locked-csproj-user-added-is-confirmed`＝属性・削除を妨げるハンドルで検知を避けられない／`change-unreadable-existing-file-is-always-listed`＝記録の後に読めなくなった既存のファイルは変わっていなくても毎回出す／`change-shared-locked-existing-file-unchanged-is-not-confirmed`／`change-missing-output-folder-records-nothing-then-lists-additions`。入口 B の行 `clear-obj-non-link-reparse-folder-is-deleted-with-contents`。入口 C の行 `enumerate-is-link-*`＝IsLink はジャンクションとシンボリックリンクだけを真にする）。mutation 実証済み。

### 提出パスの判定

- `MockProjectEmitTools.ResolveEmitPath` は拒否リストへ戻さない。AI が提出したファイルは直後に同じ出力フォルダで `dotnet build` されるため、MSBuild が自動 import するファイル（`Directory.Build.props` / `.targets`・`Directory.Packages.props`・`Directory.Solution.*`・`*.rsp`・`obj/*.nuget.g.props`）を 1 つでも書けると、ビルド検証がそのままユーザー権限の任意コード実行になる。
- 自動 import 面は MSBuild の版で増えるので名前の列挙では原理的に追随できない。「UI 層のソース拡張子だけを通す」ホワイトリストであることが防御の本体。`obj/` / `bin/` のフォルダ拒否は、拡張子集合をいじるたびの再検証を不要にするための二重化。
- 守るべき性質は「ユーザーが明示的にアプリを起動する前に、ビルド検証の瞬間に AI のコードが走らないこと」。`.cs` を許可してよいのはビルドでは実行されないから（ソースジェネレータは csproj 経由でしか追加できず、csproj は保護済み）。
- プロファイルの宣言が中央上限集合の部分集合であることは `MockProjectEmitToolsTests` の全数列挙テストが固定する。
- 拡張子以外の検査（絶対パス・保護フォルダ・別名・空）は両プロファイル共通の経路で、`PathEntranceTests` の Blazor の行（`emit-blazor-*`）は WPF の行と同じ形の写し。片方の行を直したら、もう片方も直す（3 つめのプロファイルが来たら、プロファイルのループへまとめる）。
- **保護フォルダ（`Generated/`・`design/`・`obj/`・`bin/`）の照合は綴りの別名に負けない形で行う。** Windows はセグメント末尾の `.` / 空白を落として開く（`Generated./x.cs` は `Generated/` へ書き込まれる）。8.3 短縮名（`GENERA~1`）でも同じフォルダを指せる。
  - 末尾が `.` / 空白のセグメントと `~` を含むセグメントを、共有の `PathSegments.CheckRelativePath` で拒否する（`src/QuickER.Platform/CLAUDE.md`「パスの名前の判定」を参照）。
  - さらに `Path.GetFullPath` で正規化した後の出力フォルダ相対セグメントでも照合する（`FindBlockedFolderInNormalizedPath`）。`GetFullPath` は末尾の `.` を 1 つしか落とさず空白も落とさないため、正規化後の照合だけでは足りない（実測）。二重化。
- ガード：`PathEntranceTests`（OS との境界のエンジンのパスの入口の表の行 `emit-rejects-protected-folder-alias`・`emit-*-accepts-legitimate-path-after-alias-guards`・`find-blocked-folder-*`＝正規化後の照合を単独で見る）。mutation 実証済み（`Generated./x.cs` / `design./mock/x.cs` は正規化後の照合が拾って、末尾検査だけを外しても緑のまま）。

### ダイアログの状態の不変条件

- モック生成のダイアログ（`MockGenerationDialogViewModel`）は、次を守る。
  1. ターンの実行中でなければ、応答中（`IsStreaming`）の吹き出しを残さない（`IsTurnInProgress` のセッターが通知を出し終えた後に外す）。
  2. ターンの実行中は接続方式もモックフォルダも変えない（`TryChangeBackend`・`MockFolder` のセッターが断り、理由を状態の文へ出す）。変えると会話のリセットでセッションを切り離し、届かなくなったターンの完了を待ってダイアログが固まる。
  3. 作ったエンジンは、切り離しと終了の破棄でちょうど 1 回破棄される（漏れると常駐の子プロセスが残る）。
  4. 初回ターンの済みは成功で終わったときだけ立つ（「初回ターン」）。
  5. 生成の可否（`CanGenerateMockProject`）と無効の理由の文（`MockGenDisabledReason`）は別のコードで、食い違わない。生成中は会話の開始も送信もできず、止めている理由（`ConversationDisabledReason`）を出す。生成中は設計書も出力しない（`CanExportDesignDoc`。生成中はダイアログからモックフォルダへ書かない。単一 HTML は保存先にだけ書くので出せる）。
  6. 押せない手は、直接実行しても何も変えない。CommunityToolkit のコマンドの `Execute` / `ExecuteAsync` は CanExecute を見ないので、CanExecute を持つコマンドの本体はどれも先頭で同じ判定（`CanStartConversation`・`CanSendMessage`・`CanClear`・`CanExportDesignDoc` など）を見て断る。画面の守りだけだと、ターン中のクリアがセッションを切り離して固まり、開始が使えないフォルダ（相対パスなら作業フォルダ、モックでない既存フォルダ）へ mock.json を作る。
- 使えないフォルダ（会話を開始できない）は 5 種：相対パス・モックでない既存フォルダ・壊れた mock.json・画面のファイル名が不正な mock.json・一覧を読めない既存フォルダ。一覧を読めないフォルダは壊れた mock.json と同じく `Mock_MockFolderOpenFailedFormat` で知らせて開始を止める（中身を確かめられないフォルダへ mock.json を作りに行かない。列挙の例外を `OnMockFolderChanged` が受ける）。
- ガード：`MockDialogTests`（どの行でも組 `MockDialogHarness` が上の 6 つと、拒否の後も保留のターン・止めた生成を進めれば実行中が解けること・画面一覧と選択の整合・添付の範囲・フルパスでないフォルダで開始できないことを見張る。行 `backend-switch-during-turn-is-refused-and-turn-still-completes`・`folder-change-during-turn-is-refused-and-turn-still-completes`・`converse-streaming-ends-when-turn-*`・`generate-reason-is-turn-in-progress-during-turn` ほか）。mutation 実証済み。
  - 6 は、組が行の終わり（保留のターン・止めた生成を進める前）に VM のコマンドのプロパティを全部集め、CanExecute が偽のものを直接実行して、状態の写し（VM の値・メッセージ・エンジン・ダイアログ・ファイルダイアログ・生成器・行の一時フォルダのファイルの内容）が変わらないことを見る。コマンドを足すと自動で対象に入り、集め方は `MockDialogTests.RefusedCommands_CollectEveryCommand` が VM の `[RelayCommand]` のメソッドとの 1 対 1 で照合する。
  - 本体の判定を直接実行する行：クリア（`turn-reset-disabled-until-turn-ends`・`generate-run-reset-disabled-until-generation-ends`）・使えないフォルダでの開始（`blocked-converse-start-and-send-disabled-*`）・生成中の設計書（`generate-run-export-design-doc-refused-until-generation-ends`）。
  - 状態 × 手の種類は、原理的に起こり得ない 4 組（受け入れ）を除いて、全部を行が触る。
  - 生成中のモックフォルダの変更は断らず、生成は開始時のフォルダで進んで完了が届く（特性の固定。行 `generate-run-set-folder-keeps-start-folder-and-completes`）。× で隠しても実行中のターン・生成は止めない（行 `persist-save-and-hide-keeps-running-work-*`）。
- 表の組は記録するダイアログ・偽のファイルダイアログ・偽の生成器・4 つの接続方式の偽のエンジンを必ず渡す（省くと失敗の経路で実の MessageBox が出て止まり、PATH の claude / dotnet や実の CLI のプロセスへ行く）。行の終わりに実行中を解き、全エンジンを破棄する。

### ForceClose

- ウィンドウの `ForceClose` は閉じる前に、(1) VM の `RequestInterrupt()` で実行中のターン・生成を打ち切り、(2) VM の `ShutdownEngines(上限)` でエンジンを破棄する。作法と理由は `src/QuickER.AI.UI/CLAUDE.md`「エンジンの破棄の作法」を参照する。
- モック生成はさらに (3) ダイアログが接続状態の確認用に持つプローブ用エンジン 3 つを独立した段で破棄する。AI チャットは VM のエンジンが確認を兼ねるので該当しない。段ごとに上限を待つので、閉じるまでの待ちは最大 6 秒。
- ガード：`MockDialogTests`（行 `shutdown-disposes-session-engine`＝ShutdownEngines は会話中のエンジンを破棄する。組がどの行の終わりにも「作ったエンジンはちょうど 1 回破棄」を見張る）・`MockGenerationDialogTests`（ForceClose はプローブ用エンジン 3 つを破棄する・ForceClose は設定保存が失敗してもプローブ用エンジンを破棄する）。AI チャット側のガードは `src/QuickER.AI.UI/CLAUDE.md` に置く。
