# QuickER.Mcp.Tools

ファイルベースの ER 図ツール実行ホスト（`DocumentErDiagramToolHost`）。ステートレスに「読込→変更→保存」する。
ツール定義と引数検査は `src/QuickER.Mcp/CLAUDE.md`「ツール定義の正本と両ホストのパリティ」「ツールの引数の形の検査」を参照する。

## 列の NULL 許容の印を直接読まない

- このプロジェクトは、列が NULL を持てるかを `ColumnNullability.AllowsNull`（`src/QuickER.Model/CLAUDE.md`「列の NULL 許容（ColumnNullability）」）で読み、NULL 許容の印（`Column.IsNullable`）を直接読まない。書き込み（ツールが印を決める代入）は対象外。
- 図は主キーの列に印を持てる（図のファイルの手書き）。印をそのまま読むと、`get_diagram_summary` が主キーの列に `NOT NULL` を付けず、読込で実効値へそろえる GUI（`ColumnViewModel`）の要約と食い違う。
- ガード：`SourceRuleTests` の規則 `mcp-tools-column-nullability-raw-reads`（見つける形は規則 `provider-column-nullability-raw-reads` と同じ・既知の一覧なし）・`ToolScenarioTests`（行 `summary-primary-key-column-flagged-nullable-shows-not-null`＝両ホストの要約が `NOT NULL` を示す）。

## 呼び出しの直列化

- 同じプロセス内の呼び出しは、プロセス全体で 1 つのロック `DiagramFileGate` で 1 本ずつに並べる。並列に届いた変更系が互いの更新を消さないため。
- 読み取り系と生成ツールの図の読み取りも同じロックを通す。保存の差し替えと読み取りが重なると、読み取りが共有違反で失敗するため。
- ガード：`DocumentErDiagramToolConcurrencyTests`・`CodeGenToolSetConcurrencyTests`。

## 何も変えない呼び出しで書かない

- 変更系ツールは、何も変えない呼び出しで図のファイルを書かない。書くと更新日時が動き、その図を開いている GUI が再読込や外部変更の確認を出す。
- 判定は `DocumentErDiagramToolHost.Mutate` が変更の前後を保存する形（`JsonStorageService.ToJson`）で比べる。ツールごとに「変えたか」を返させる形より取りこぼしが無い。
- `create_diagram` は置き換えない書き込み（`JsonStorageService.SaveNew`＝`AtomicFile.CreateText`。一時ファイルを `overwrite: false` の Move で置く）で作る。
- 図の読み込み（`JsonStorageService.TryLoad`）は `AtomicFile.ReadAllBytes`（`TryReadAllText` と同じ読み直しの本体 `ReadWithRetry`）を通す。別プロセスの保存と重なった一瞬の共有違反で失敗しないため。
- ガード：`DocumentErDiagramFileHandlingTests`（書き直さない 5 ケースを内容と更新日時で、共有違反の読み込みが 60ms 以上粘ること、`create_diagram` の書き込みの手段をソースで確かめる）。
  - 粘りを測るのはロックを通らない `TryLoad`。ツール越しだと、並列のテストがプロセス共通のロックを握る待ちで、粘らない実装でも下限を越える。
- 置き換えない書き込みそのもの：`AtomicFileTableTests`（行 `atomic-create-text-existing-file-throws-and-keeps-content`）・`DocumentFormatTests`（行 `savenew-existing-file-throws-and-keeps-existing-diagram`＝既存の内容が変わらず一時ファイルも残らない）。mutation 実証済み。
