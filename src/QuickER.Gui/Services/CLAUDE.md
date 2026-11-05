# QuickER.Gui/Services

エクスポート／インポートの実行本体（`DiagramExportService` / `DiagramImportService`）と、Mermaid・DBML・Excel/HTML 定義書の入出力の規則。
完了通知の提示先は `src/QuickER.Gui/CLAUDE.md`「完了通知の提示先」。

## 形式の往復のエンジン
- 形式（DBML・Mermaid・Excel 定義書・HTML 定義書・C# リバース）の入出力は、`tests/QuickER.Tests/Gui/Services/FormatRoundTrip/` のエンジンで確かめる。単発のテストを足さず、表の行へ寄せる。
  - 形の行（`FormatShapeRows`）：図を全形式で出力→取込し、共通の表明を流す。形式ごとに出るべき記法・告知・取込の結果は、行の期待（形式を値に持つデータ）で書く。
  - 規則の行（`FormatRuleRows`）：形式の固有の入力（手書きの DBML・Mermaid・C# のソース・Excel のブック）の取込の受理・拒否・警告。受理した行には不動点（取り込んだ図を出力して取り込み直しても運ぶ面が変わらない）を当てる。外すのは理由のあるときだけ。
  - マージの行（`FormatMergeRows`）：現在の図と取込の図からマージ後の図・Id の引き継ぎ・クエリ。経路は Id の引き継ぎだけ・Excel・C#（アダプタの `Merge`＝製品の取込と同じ並び）。
- 共通の表明（`FormatRoundTripRunner`）：
  - 往復：面（`Facet`）ごとに元と取込が一致するか、違えば「その形式が告知した」か「運ばない面として宣言した」のどちらか。表・列の増減（`Structure`）は告知も宣言も理由にならない。
  - 告知と出力の食い違いが無い：告知した種類は、その形式が告知しうる種類で、対応する面が実際に違う。取込が推論し直す種類（`Reinferred`）だけは、元の図に中身があれば同じ値に戻っても告知してよい。
  - 注入が無い：形式が素で書けない文字を含む名前・説明が出力にそのまま出ない（`MustEscape`）。
  - カルチャ不変：出力と取込の結果がどのカルチャでもインバリアントと同じ。数値・日時・大文字小文字を持つ代表の形（`CultureProbe`）だけで流す。
  - マージで戻る：マージ取込のある形式は、運ばない面のうちマージが戻す面（`MergeRestores`）も元と一致し、表・列の Id が元のまま。
- 形式の違いはアダプタ（`FormatAdapter`）の宣言だけが持ち、行と表明の中で形式を名指しして分岐しない。宣言の規則：
  - 運ばない面（`NotCarried`）は根拠（docs・CLAUDE.md・製品のコード。コードのときは「docs 未記載」と書く）つきで、根拠のファイルが実在する。
  - その形式が告知しうる種類が説明できる面は、宣言で黙らせない（告知が無ければ取りこぼしとして赤にする）。
  - 面は狭く分ける（名前は「付いていた名前」と「無名に名前が付いた」、印は「子の列に付け直した」、補った列は「元が列 0 本の表」、畳み込みは「制御文字が空白へ畳まれた違いだけ」など）。存在の面の欠け・余りと属性の違いは、元の図の形で振り分ける。
  - 取込のできる形式は、製品の `DiagramImportFormat` と C# リバースにちょうど一致する。
- 宣言で受けられない違い（表・列の増減・取込と生成の例外・製品の修正を決めた違い）は既知の失敗の一覧（`FormatRoundTripKnownFailures`。ケース ID は「形の行|形式」）に、直す区切り（Pending）か受け入れた理由（Accepted）つきで載せる。
- 次元表と網羅の計数は `FormatRoundTripDimensions`（形式 × 図の特徴・告知する形式 × 告知の種類・取込のできる形式 × 規則の種類・マージの経路 × 取込の図の出どころ）。図の特徴（`FormatShapeFeatures`）は図の中身から機械で決める。
- 流すクラス：形の行は形式ごとの派生 `FormatShape{Dbml,Mermaid,Excel,Html,CSharpReverse}Tests`（基底 `FormatShapeTestsBase`・クラスどうしが並列に流れる）、規則の行は `FormatRuleTests`、マージの行は `FormatMergeTests`、カルチャの代表の形と軸・宣言・網羅のガードは `FormatRoundTripTests`。CLAUDE.md の各所の `FormatRoundTripTests`（形の行 `…` / 規則の行 `…` / マージの行 `…`）の書き方は、このエンジンの行の名指し。
- ガード：`FormatRoundTripTests`（`CultureProbe_IsInvariant`・`NotCarriedDeclarations_HaveEvidence`・`ImportableAdapters_MatchProductImportFormats`・`ShapeTestClasses_CoverFormatAxis`・`FacetMap_CoversEveryOmissionKind`・`Axis_CoversAuthoritativeValues`・`Coverage_MatchesGaps`）・`FormatShapeTestsBase.Shape_KeepsCommonProperties`・`FormatRuleTests.Rule_Matches`・`FormatMergeTests.Merge_Matches`。mutation 実証済み（往復の取りこぼし・告知の食い違い・注入・取込の規則・マージ・警告）。

## 出力形式の情報欠落告知（Mermaid / DBML）
- 形式が構造的に表現できない情報を、出力完了時に内訳として告知する。
- 判定は各エクスポータの `DetectOmissions`（出力ロジックと同居させ、「出力を変えたのに検出が古いまま」の乖離を防ぐ）が**図の実際の中身から**行う（説明が空の図で「説明が落ちる」とは言わない）。言語中立の `ExportOmissionKind` を返し、件数は持たない。
- 表示側 `DiagramExportService.DescribeOmission` が resx で文言化する（`SyncPlan.Warnings` と同じ流儀）。
- エクスポート／インポートのコマンド実行本体は `DiagramExportService` / `DiagramImportService`。VM は 1 行委譲と `IDiagramTransferHost` の実装だけを持つ。
- 提示は `ShowInformationDetails`（要約＝完了文＋導入文・詳細＝項目の箇条書き。型変換警告と同じ形式）で、**形式ごとにセッション 1 回**だけ。Mermaid は NOT NULL 列がある限りほぼ必ず立ち、毎回出すと通知が形骸化する（`_omissionNotifiedFormats`。未対応方言フォールバックの `_fallbackWarningShown` と同じ流儀）。
- **SQL DDL・Excel/HTML 定義書・画像は告知しない**。落ちるものが「その形式の目的上、出さないのが正しいもの」ばかりで、告知すると本当に危険な欠落が埋もれる。

### Mermaid の欠落（17 種）
- テーブル説明／メモ／列の説明／NULL 許可／複合 UNIQUE 制約／UNIQUE 制約名／FK 列ペア／参照アクション／名前付きクエリ。
- 列名の書き換え（属性名に使えない文字を畳んだとき）。
- 複合主キーの順序（列の並びと食い違うとき。構文が無いため告知する）。
- 計算列／DB が採番する列。
- 型の書き換え：取込の復元 `MermaidImporter.DenormalizeDataType` で元へ戻らないとき。判定は書き出しと復元を実際に往復させる。
- テーブル名の書き換え：引用の中にも書けない `"` `%` `\` を畳んだとき。素で書けない名前は引用して書く。
- 外部キー列の単一列 UNIQUE：キー標識は 1 つで FK が優先。判定は出力と `KeyMarker` を共有する。
- リレーションの制約名：素で書けない名前は関係行のラベルを引用して書き、取込は引用ラベルを採らない。判定は出力を `MermaidImporter.Parse` で実際に取り込み直して、書いた順に名前を突き合わせる。ガード：`FormatRoundTripTests`（形の行 `mermaid-quoted-constraint-label-*`・`mermaid-plain-constraint-label-*`・`mermaid-plain-constraint-name-not-read-back-*`・`mermaid-unnamed-relationships-not-reported`）。

### DBML の欠落（6 種）
- DBML はテーブル説明を標準 `Note:` 行、制約名を名前付き Ref（`Ref 名前: …`）、参照アクションを Ref 行の端点の後ろの設定ブロック（`delete` / `update`）で往復対応している。
  - 既定 NoAction は出力を省略し、DBML の `restrict` は取込で NoAction へ倒れる。
  - 出力は DBML 標準の記法だけ（`@dbml/core` が読める）。素の名前は ASCII の英数字・アンダースコアだけで、それ以外は日本語も引用する。
- 欠落になるのは次の 6 種だけ。
  - メモ：`Note` を説明に使うので、独自拡張なしには載せられない割り切り。
  - 名前付きクエリ。
  - どのリレーションの子側の列でもない列の外部キー印：DBML に列単独で印を付ける設定が無い。判定は `DbmlExporter.HasForeignKeyMarkerOutsideRelationships`。
  - 計算列。
  - DB が採番する列。
  - 端点の列を書けないため `Ref` 行を出さないリレーション：列ペアの無い 1 対 1・1 対多（先頭列で代用すると、取込が定義していない外部キーを作る）、列の無いテーブルにつながる多対多。出すかの判定は出力と告知が `DbmlExporter.ResolveEndpointColumns` を共有する。

## 一意制約・主キーの順序・列ペアの入出力
モデルの規則は `src/QuickER.Model/CLAUDE.md`「一意制約」「主キーの順序の出力判定」「リレーションの列ペア」。ここでは各形式での扱いだけを置く。

### 一意制約
- DBML：単一列無名は列設定 `unique`、複合／名前付きは `Indexes { … [unique, name: …] }`。往復できる。
- Mermaid：**単一列制約のみ** UK。複合は構文が無く非出力の割り切り。
- Excel/HTML 定義書：キー列 `UQ{n}`、同番＝同一制約で複合を表現する。Excel 取込は制約名が落ちて合成名になり、構成列は列の並び順になる。マージ取込では、構成列の集合が同じ現在図の制約から順序と名前を引き継ぐ（`TableDefinitionMergePostProcessor`）。

### 主キーの順序
- DBML：順序が列の並びと食い違うテーブルだけ、主キーを列の `pk` でなく `Indexes` 先頭の `(列, …) [pk]` で書く。取込は `[pk]` 索引を構成列と順序として読む（一意制約は作らない・索引の名前は捨てる）。列の `pk` と集合が違う／`[pk]` 索引が 2 つ／索引内の同じ列の重複は、行を名指しして断る。
- Mermaid：順序の構文が無いので `ExportOmissionKind.PrimaryKeyOrder` で告知する。
- Excel：順序を運ばない（docs へ明記）。マージ取込は C# リバースと同じ規則で現在図の順序を引き継ぐ（`TableDefinitionMergePostProcessor`）。
- 順序を載せるか欠落として告知するかの判定は `Entity.GetReorderedPrimaryKeyColumnNames()` だけ（C# 生成の `[DbTableMeta].PrimaryKeyOrder`・DBML の `[pk]` 索引・Mermaid の告知の 3 箇所が共有する）。ガード：`FormatRoundTripTests`（DBML の往復＝形の行 `dbml-reordered-primary-key`・列順どおりは出力不変＝形の行 `dbml-primary-key-in-column-order-*`・他ツール形式＝規則の行 `dbml-rule-primary-key-index-only`・`dbml-rule-primary-key-same-set-in-both-places-uses-index-order`・断る 4 条件＝規則の行 `dbml-rule-primary-key-conflicting-sets-are-rejected`・`dbml-rule-two-primary-key-indexes-are-rejected`・`dbml-rule-duplicate-column-in-primary-key-index-is-rejected`・`dbml-rule-primary-key-index-unknown-column-is-rejected`・Mermaid の告知＝形の行 `mermaid-primary-key-order-*`）。

### 複合外部キー・制約名
- DBML：複合 Ref 構文 `Ref: p.(a,b) < c.(x,y)` を出力し、取込む。取込の列再推論は廃止で、Ref 行の列名が正本。不整合行はペアだけ破棄する。
- Excel/HTML 定義書：列セルをカンマ区切りの複数列表記にする。`FK{n}` は複合の全構成子列に同番（`UQ{n}` と同流儀）。
- Mermaid：列構文が無いので出力は現状維持。取込推論は PK 全列ペア化へ統一。必須のラベルは制約名を運ぶ。
  - 名前の無いリレーションの出力値は `relates`（`MermaidExporter.UnnamedRelationshipLabel`）。
  - 引用つきや複数語・図の中で重複するもの・主キー/一意制約名と重なるものは採らない。採らなかった関係は、既定名が空いていれば名前なし、ふさがっていれば `UniqueForeignKey`。ガード：`FormatRoundTripTests`（形の行 `mermaid-unnamed-relationships-relates`／規則の行 `mermaid-rule-repeated-label-is-not-adopted` ほか `mermaid-rule-*` のラベルの行）。
- 「名前の無い関係の既定名がふさがっていれば一意化」は `ConstraintNames.NameUnnamedForeignKeys` が正本。Mermaid・DBML・Excel 定義書の 3 取込と C# リバースが共有する。明示された名前は重複していても変えない（PostgreSQL では外部キー名がテーブル単位で一意なため）。ガード：`FormatRoundTripTests`（形の行 `dbml-unnamed-relationships`／規則の行 `dbml-rule-same-pair-twice-without-names-second-gets-unique-name`・`dbml-rule-single-unnamed-ref-stays-unnamed`・`dbml-rule-named-ref*`・`excel-rule-hand-written-empty-names-second-gets-unique-name`／形の行 `excel-unnamed-same-pair`・`excel-single-unnamed`・`excel-named-row-takes-default`）。

## DBML の取込：テーブルブロック内の行をキーワードだけでブロックと判定しない
- `records int` / `checks int` / `note varchar` という**列**が実在する。`^(checks|records)\b` や `^note\b` で判定すると、前者はブロックとみなされて**列が無言で消え**、後者は複数行 Note とみなされて**取込全体が失敗**する。
- エクスポートは列名を素の識別子で書く（これらの名前は引用符を要しない）ので、誤ると自分が書き出したファイルを読み戻せない。往復が壊れる。
- 見分け方は構文で行う。
  - **ブロック**は「キーワードのあと、設定の `[` より前に `{` がある」。`^(checks|records)\b[^\[\r\n]*\{` が、`records t(id) {` のように名前や引数を挟む形と、1 行で閉じる `checks { … }` の両方を拾う。列定義は `名前 型 [設定]` なので、`{` は `[` より後ろにしか来ない。
  - **複数行 Note** はキーワードの直後が `:` か `{`（`^note\s*[:{]`）。1 行の `Note: '…'` は手前の `TableNoteRegex` が先に食べる。
- 「行が `{` で終わる」で判定してはいけない。1 行で閉じるブロックを取りこぼして幻の列ができる。
- トップレベルの `^ref\b` はテーブルの外でしか当たらないので、同じ問題を持たない。
- ガード：`FormatRoundTripTests`（形の行 `dbml-keyword-column-names`／規則の行 `dbml-rule-single-line-skippable-block-is-not-a-column-*`）。mutation 実証済み。

## Excel 定義書の取込：名前が空の行
- `TableDefinitionDocumentImporter.ReadColumns` は、カラム名が空の行で、説明・データ型・必須・キー・参照先・備考（3 列目〜`TableDefinitionDocumentLayout.DetailColumnCount`）のどれかに値があれば `TableDoc_ColumnMissingName`（テーブルとシートの行番号）で断る。すべて空なら表の終わりにする。No. の列は数えない（行の番号を振っただけ）。
- 確かめずに終わりにすると、名前を消した行・カラムの下に名前なしで入力した行で、その行と後ろのカラムが黙って消える（出力は変えない）。
- 列数の定数は出力の表の列数と同じでなければならない。列を足して定数を直し忘れると、足した列だけの行を見落とす。
- ガード：`FormatRoundTripTests` の規則の行 `excel-rule-row-without-column-name-but-other-content-*`（3〜8 列目の 6 行）・`excel-rule-middle-row-without-column-name`・`excel-rule-row-with-only-number-ends-table` / `excel-rule-empty-row-ends-table`（No. だけの行とすべて空の行・その下の行は読まない）と、形の行 `excel-exported-workbook` の Excel の出力の期待（出力の見出しの最後の列が定数と一致）。mutation 実証済み。

## 出力のエスケープ
- Mermaid / DBML の識別子・ラベルは `ExportTextSanitizer.Sanitize` を通す（`SqlComment.Sanitize` と同じ実装への 1 行委譲）。ガード：`FormatRoundTripTests`（形の行 `injection-names-with-new-line`・`injection-data-type-with-new-line`・`injection-description-with-new-line`・`injection-dbml-description-escape-*`・`dbml-backslash-constraint-names`）。
- DBML のリテラルは `DbmlLiteral.Escape` / `Unescape` の対で扱う。
  - `\` 二重化 → `'` エスケープ。復元は左から 1 パスで行う（逐次 `Replace` にすると、解いた結果がもう一方のエスケープに見えて二重に解ける）。
  - 改行は空白へ畳む。複数行 Note の `'''` ブロックは `DbmlImporter` が行単位解析で非対応のため。

## 自動整列の共通の性質
- 4 つの整列（`AutoLayoutService` の `LayoutGrid`〔リレーション考慮〕・`LayoutTree`・`LayoutForceDirected`・`LayoutAppend`）は、図の形に依らず次を満たす。
  - 例外を出さない（空の図も）。
  - 決定的（同じ入力を 2 回流すと座標が同じ）。乱数を使わない。
  - 表の矩形（`Width` × `DisplayHeight`・間隔を足さない）が重ならない。
  - 置いた表は配置前の座標から動く（追記は新しい表）。追記は固定の表を動かさない。
  - 追記は新しい表を固定の群の下か右（占有面積の小さい側）へまとめて置き、固定の群に食い込ませない。
- 壊すと：同じ図を開くたびに配置が変わる、表が重なって読めない、追記で既存の配置が崩れる。どれもビルドは通る。
- ガード：`AutoLayoutScenarioTests`。
  - `Combo_KeepsCommonProperties`：図の形 12（`LayoutShapes`）× 整列の種類 4 の全組に共通の性質を当てる。追記の組は、形ごとに宣言した置き場所（`LayoutShapes.AppendPlacementOf`）と実際の置き場所を照合する（下を選ぶ形と右を選ぶ形の両方を持つ）。
  - `Row_MeetsMetrics`：元のテストの図と閾値の行（交差数・占有面積・軸整列・中心の距離・Margin）。
- 座標そのものが仕様のもの（格子の並び・旧実装との一致・階層の深さと中央寄せ・追記の近傍・説明込みの高さ・最適化のダイジェスト）は、個別のテスト（`AutoLayoutServiceTests`・`AutoLayoutAppendTests`・`AutoLayoutOptimizationTests`）が持つ。
