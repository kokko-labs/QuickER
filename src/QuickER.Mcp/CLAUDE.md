# QuickER.Mcp

MCP ツール定義の正本（`ToolDefinition`・`ErDiagramToolCatalog`）、入口の引数検査、stdio サーバホストを持つ。
ファイルベースの実行ホストは `src/QuickER.Mcp.Tools/CLAUDE.md`、GUI 側のミラー（`ErDiagramDynamicTools`）は `src/QuickER.Gui/ViewModels/CLAUDE.md` を参照する。

## 外部 MCP サーバ

CLI `quicker mcp`（stdio）で、外部 AI エージェント向けに次のツールを公開する。

- ER 図編集 13 ツール（カタログ正本の 12 ＋ Mcp.Tools 側が足す `create_diagram`）
- 名前付きクエリ 3 ツール（`set_query` / `list_queries` / `remove_query`）
- コード生成 3 ツール（`generate_csharp` / `generate_ddl` ＋ 情報系の `get_generation_config_schema`）

`get_generation_config_schema` は quicker.json の全キーを機械可読 JSON で返す自己記述カタログ（`GenerationConfigSchema`）。
docs 不在の外部エージェントが config を自己発見的に書ける。

初期化時に設計既定ルールの instructions（`ErDiagramToolCatalog.ServerInstructions`・英語）を返し、`add_entity` のツール説明にも命名既定を記載する。
内容は命名（パスカルケース単数形）・既存様式追従・単一 PK・FK 手順など。**誘導のみで強制しない**（ルール外の図も扱える柔軟性を保つ）。

## ツール定義の正本と両ホストのパリティ

- ツール定義の正本は `QuickER.Mcp.ErDiagramToolCatalog`（**英語**）。内蔵チャットと共有する。
  - AI.Chat の `ErDesignProfile` がカタログ定義を直接束ね、LLM SDK 形式への変換は QuickER.AI の `ChatToolConverter`。CJK 混入は英語ガードテストが検知する。
- 意味論は GUI 側 `ErDiagramDynamicTools` のミラー。パリティテストが結果 ErDiagram の一致を検証する。
- 方言の名前の正本は `DialectNames`（QuickER.Dialects）、ツール名の正本は `ErDiagramToolNames`。ツールの説明の enum・説明文と検証・振り分けが同じ定数を読む。ガード：`McpToolStructureGuardTests`。
- 入力検証と応答の注意文は両ホスト共通。
  - 入力検証：空・大小無視で重複するテーブル名／同じテーブル内の列名・空の型・未知の `relationship_type`・同じ親子で同じ列ペア集合の FK の重複。
  - 応答の注意文：要約先頭の対象 DBMS・主キー列への `is_nullable=true` の無視・列ペア推論の不足。
  - **検証はどの変更よりも前に置く**。GUI 側は変更をその場で VM へ反映するので、途中で弾くと一部だけ反映されて MCP と食い違う。
  - ガード：`ToolScenarioTests`（`ToolScenarioRows` の行＝入力の検証 `validation-*`・注意文 `note-*` ほか。1 行を両ホストのセッションで流し、拒否した呼び出しが図を変えないことも見る）。
- `remove_entity` はそのテーブルの名前付きクエリも消す。GUI は `RemoveEntityCommand` / `GroupRemoveEntitiesCommand` が共有の `EntityQueryRemoval` で退避し Undo で戻す（MCP は戻らない）。
- `remove_column` は名前付きクエリを書き換えず、削除した列を参照するクエリを応答の英語の注意文で名指しする。両ホストが削除の前に `QueryColumnReferences.Find` で求め、文は `QueryColumnReferences.DescribeRemovedColumn`（`src/QuickER.CodeGen.CSharp/Queries/CLAUDE.md`「列の削除とクエリの参照」）。ガード：`ToolScenarioTests`（行 `column-remove-referenced-by-queries-names-each-query`・`column-remove-condition-reference-is-case-insensitive`・`column-remove-not-referenced-keeps-previous-message`）。

## 個別ツールの規則

### set_primary_key

- テーブル名だけで照合する全体置換。挙げた列が指定順で主キーになり、他の列は外れる。
- 挙げた列は NOT NULL に正規化する。**外れた列の NULL 許容は据え置く**（外したことは「NULL を許してよくなった」を意味しない）。
- 列は最低 1 つ必要。このツールで PK を全解除できない（全解除は GUI の PK チェック）。
- 列解決は UNIQUE ツールと共有の `ResolveConstraintColumns`。
- MCP ホストの `add_column` / `set_column_property` も主キー列を NOT NULL へ正規化する（GUI の `ColumnViewModel` と同じ強制）。
- GUI ミラーを含め、リレーション参加列のロックを持たない。ファイルホストにロックの概念が無く、両ホストのパリティを優先した意図的な非対称。
  ロックされた列をツールで主キーから外すと、グリッドでは再チェックできず Ctrl+Z かリレーション削除でしか戻せない。
- ガード：`ToolScenarioTests`（行 `primary-key-*`）・`ErDiagramDynamicToolsPrimaryKeyTests`（GUI の Undo）・`ErDiagramToolHostParityTests`。
- 主キーの順序モデル全体は `src/QuickER.Gui/ViewModels/CLAUDE.md`「主キーの順序の GUI」を参照する。

### set_unique_constraint と remove_unique_constraint

- `set_unique_constraint` は (テーブル, 列集合) で照合する upsert。既存 Id を温存する。
- どちらも `get_diagram_summary` に反映される。一意制約のモデルは `src/QuickER.Model/CLAUDE.md`「一意制約」を参照する。

### add_relationship と remove_relationship

- `add_relationship` は `source_columns` / `target_columns`（並行配列・同数・宣言順）を受ける。省略時は親 PK 全列を自動ペア化する。
- 明示した親側の列が `Entity.IsCandidateKey`（主キー全列か一意制約の列集合と一致）でなければ、拒否せず注意文を添える（MySQL は一意でない索引列を参照できるため）。ガード：`ToolScenarioTests`（行 `note-add-relationship-explicit-non-candidate-key-is-reported-*`）
- 任意の `constraint_name` は、図の既存の FK・PK・一意制約の名前（`ConstraintNames.CollectExisting`＝DDL が付ける名前）と大文字小文字を無視して衝突すれば拒否する。
- 省略時は `ConstraintNames.UniqueForeignKey` が既定名・子の列名・番号の順に衝突しない名前を付ける。
- `remove_relationship` は複数一致時に `constraint_name` で特定し、無指定なら候補列挙エラーにする（先頭を黙って削除しない）。
- `get_diagram_summary` に列ペアと制約名を表示する。

### set_query

- (テーブル名, クエリ名) 照合の一発定義 upsert。既存 Id を温存し、全戻り形 × 全実装方式に対応する。
- 定義時に次を検証し、失敗は保存しない。
  - DSL 構文（`QueryConditionParser`）・列名・`@パラメータ`の実在
  - 生 SQL（`RawSqlAnalyzer`。未宣言は拒否、未使用・複文は警告のみ）
  - 構造と、生成で必ずエラーになる構造規則（`QueryDefinitionRules.Validate`）
- `QueryDefinitionRules.Validate` は生成器・GUI のクエリ定義ダイアログと判定も文言も共有する。ガード：`QueryToolGenerationRuleTests`（同じ定義を生成器と `set_query` の両方へ流して照合）。
- `order_by` は list / single / projection のみ。implementation が sql 以外のときの `sql` は保存しない（GUI のクエリ定義ダイアログと同じ）。方式に合わない入力と、SQL 無しの sql 方式は警告のみ。
- 型トークンの内容検証は生成時に任せる（方言非依存を保つ）。

## ステートレス設計

- `file`（図 JSON パス）は `FileParameterInjector` がツール単位で機械注入する。図を対象にするツールだけで、引数不要の `get_generation_config_schema` は対象外。
- 毎呼び出しが「読込→変更→保存」で完結する。実行ホストと同一プロセス内の直列化は `src/QuickER.Mcp.Tools/CLAUDE.md`「呼び出しの直列化」を参照する。
- 安全策：変更系はファイル不在（`create_diagram` へ案内）・非 DiagramDocument JSON・新フォーマット版（`IsNewerFormat`）を拒否する。`create_diagram` は既存ファイルを上書きしない。
- **レイアウトはサーバが書かない**。新規作成はスキーマのみの文書（GUI で開くと全体自動整列）。既存ファイルへの追加分は GUI 読込時に `AutoLayoutService.LayoutAppend` が配置する（既存不動・空き領域へ追記。マージ取込の新規テーブルも同経路）。
- GUI で開いている図は外部変更検知で追従する。

## コード生成ツール

- コード生成ツールは合成ルート CLI 側の `CodeGenToolSet`。`GenerationExecutor` の ParseResult 非依存コアを流用し、`quicker generate` の挙動はバイト不変。
- **結果テキストは英語固定**。生成コアが CLI と resx を共有するため、`CodeGenToolSet.Dispatch` が実行中だけ `CurrentUICulture` を InvariantCulture にして finally で戻す。`CurrentCulture` は触らない（生成物の書式を CLI・GUI と同じに保つ）。ガード：`CodeGenToolSetLanguageTests`（並行呼び出しでの非干渉まで固定）。
- 図の対象 DBMS と違う `provider` は受け付けて結果の先頭で警告する。判定は CLI の `--provider` と共有の `GenerationExecutor.ProviderDiffersFromDiagram`。
- 図の読込失敗の文言は ER 図ツールと共有の `DiagramLoadErrors`（Mcp.Tools）。

## stdio の純度

- **stdout は JSON-RPC 専用チャネル**。mcp 起動時に `Console.Out` を stderr へ退避する。ガード：stdio E2E テスト（実プロセス起動）がプロトコル純度を最終検証する。
- 汎用ホストは `Host.CreateEmptyApplicationBuilder` で組む。既定の構成は作業フォルダ（エージェントが開いている利用者のプロジェクト）の `appsettings.json` を読んで監視を張り、壊れたファイルでサーバが起動しなくなる。ガード：`McpServerE2ETests.McpServer_IgnoresAppSettingsInWorkingDirectory`。

## ツールの引数の形の検査

- ツールの引数の形は、ツールを実行する前に入口で 1 回だけ検査する。`ToolArgumentShape` を、MCP の `McpToolSet.Execute`（全 19 ツール）と内蔵チャットの `ErDiagramHostChatAdapter` のツール実行口（全チャットエンジンが共有）が呼ぶ。
- 規則：
  - 必須の欠落・`null` は断る
  - 省略できる引数の `null` は省略と同じ
  - JSON の種類違い（配列の要素・辞書の値を含む）は断る
  - `enum` の値とスキーマに無い引数は見ない
  - 引数の全体がオブジェクトでない（配列・文字列・数・真偽値・`null`）呼び出しは、`ToolArgumentShape.Check` の WrongType（`(arguments)`）で断る。2 つの入口は JSON として読めた引数をすべて `Check` へ渡し、読めない引数（と内蔵チャットの空・空白＝`{}`）だけをツールの処理に任せる。外すと、引数を読まないツール（`get_diagram_summary`・`list_queries`）が成功で答え、ほかのツールはツールごとの文言で失敗して、2 つのホストの答えが割れる
- 外すと、ホストが既定値で黙って進め（`table_name` → `NewTable`）、型違いを読み捨て、`set_query` の配列のオブジェクトでない要素が生の例外になる。
  直接呼ぶ経路のために、ホストの黙った既定値も消してある（`relationship_type` の省略も 2 つのホストが断る）。
- 固定は入力スキーマから崩したケースを自動で作る総当たり（共有部品 `ToolArgumentFuzz`、流れと表明は `ToolArgumentShapeFuzzRunner`）の 3 クラス。
  - `ToolArgumentShapeFuzzTests`（MCP の ER 図操作）
  - `ChatToolArgumentShapeFuzzTests`（内蔵チャット）
  - `CodeGenToolArgumentShapeFuzzTests`（MCP のコード生成）
  - ホストの `relationship_type` の必須は `ToolScenarioTests` の行 `validation-relationship-type-missing-is-reported-as-required` が見る。
  - 引数の全体は、ツールごとに根を 5 種（配列・文字列・数・真偽値・`null`）へ置き換えたケース（`ToolArgumentFuzz.RootCases`・崩し方 `non-object-root`）で、入口ごとの形の検査の文（`Entrance.RootRefusal`）との一致まで見る（ツールの処理が別の理由で失敗しても合格にしない）。mutation 実証済み（2 つの入口）。
- 引数の値の読み取りは `ToolArguments`（`GetString` / `GetBool`）、`relationship_type` の名前の解釈は `RelationshipTypeNames.TryParse`（Model）、列ペアの比較は `RelationshipColumnPair.HaveSamePairs` の 1 か所だけで、ほかに写さない。
- ガード：`SourceRuleTests` の規則 `tool-argument-parsing-copies`（正本自身が拾えることは規則 `tool-argument-parsing-owners`）。
  - `SourceSymbolScan` で src の `#if` のどの分岐もシンボルで読み、次を正本の外では理由つきの一覧（別の書式を読む Mermaid と Excel 定義書の取込の `ParseRelationshipType`）とファイル・件数で厳密に照合する。
    - 第 1 引数が `JsonElement` / `JsonObject` / `JsonNode` の `GetString` / `GetBool` の宣言
    - `RelationshipType` を列挙型にする `Enum.Parse` / `TryParse` / `GetValues`（`GetNames` は拾わない）
    - `ParseRelationshipType` / `HasSameColumnPairs` の宣言
    - `JsonElement.TryGetProperty` の out で受けた値の `ValueKind` を、同じ式の中で `JsonValueKind.String` / `True` / `False` と比べる形（`==` / `!=` / `is` のパターン。単発の値の読み取りの写し）。Array / Object / Null との比較（配列・オブジェクトの読み取りと形の検査）と、`TryGetProperty` と別の文の比較は拾わない。ツールの引数でない JSON（AI の API のエラー本文・claude CLI と codex app-server の出力）を読む QuickER.AI の箇所は理由つきの一覧に載せる
  - 自己検査の行 `tool-argument-parsing-copies-forms`・`tool-argument-parsing-copies-inactive-branch`・`tool-argument-parsing-copies-out-value-kind-forms`・`tool-argument-parsing-copies-out-value-kind-inactive-branch` と、正本自身が拾えること（規則 `tool-argument-parsing-owners`）も確かめる。

## ツールの応答の改行

- ツールの複数行の応答は、改行を LF だけで組む。`AppendLine`（Windows で CRLF）を使わず `.Append('\n')` で組み、1 つの応答で改行を混ぜず、同じツールの応答の改行がホスト間で食い違わないようにする。
- 要約（`get_diagram_summary`）は「1 行が 1 件の記録」の形で、表の説明は見出しの下の 1 行、列の説明は列の行の末尾に置く。説明の改行（CR LF の組は 1 つ・CR・LF・U+0085・U+2028・U+2029）は空白 1 つへ畳む。畳まないと説明の続きが行頭に出て、偽の `[表]` 見出しや偽の列の行に読める。畳む関数は `ToolResponseText.ToSingleLine`（QuickER.Mcp）の 1 つで、両ホストの表・列の説明の 4 か所が通す。
- ガード：`ToolScenarioTests`（行 `line-ending-summary-uses-lf-only`＝両ホストの `get_diagram_summary`・`line-ending-summary-folds-description-line-breaks`＝説明の改行を畳み、偽の見出し・列の行と `\r` を含まない）・`QueryToolFormatterTests`（全行に共通の「`\r` を含まない」と行 `formatter-line-ending-*`＝英語の整形とチャットの整形〔en・ja〕）。
