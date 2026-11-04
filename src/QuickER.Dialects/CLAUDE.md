# QuickER.Dialects

方言の宣言（`DialectNames`・`IdentifierLengthLimits`・MySQL のファイル名の符号化・`UnsupportedDefinitionKind` と `DialectDeclarations` と `UnsupportedTreatments`・`SchemaQualifiedName`）。依存ゼロ・net10.0。Model と Mcp が参照し、他は推移的に見る。
方言ごとの違いはここと各プロバイダの能力宣言だけが持ち、宣言は既定値を持たない。

## 宣言は既定値を持たない

- `SyncDialectCapabilities` の全プロパティは `required` で初期化子を持たない。5 方言のプロバイダが全プロパティを明示する。`SchemaDiffService.Compute` の capabilities も必須で、null の経路を持たない。テストは `TestDialectCapabilities.Neutral` が旧既定値を明示で再現する。
- 既定値を足すと、能力を足したときに書き忘れた方言が旧来の値で黙って動く。
- ガード：`SyncDialectCapabilitiesNoDefaultsTests`（RequiredMemberAttribute と構文木の初期化子）。

## 方言名の引き当て

- `IdentifierLengthLimits.ForDbms` は知らない方言名を `ArgumentException` で断る。
- 図から取った方言名（手で編集した図は知らない名前を持ち得る）は `ForDiagramTarget`（知らない名前なら上限を検査しない）を通す。GUI は図を開くとき方言をプロバイダへ解決し直すので、プロバイダ名で `ForDbms` を引く。
- ガード：`IdentifierLengthLimitTests`・`DocumentErDiagramUnknownTargetDbmsTests`・`UnknownTargetDbmsTests`。

## 方言名の文字列リテラル

- 方言名と大文字小文字を無視して完全に一致する文字列リテラルは、`src/` の `QuickER.Dialects` の外では、理由つきの既知の一覧（ファイル・値・件数）にあるものだけ。
- 方言名で分岐する出現は、方言の宣言へ置き換えたら一覧から消す。
- ガード：`DialectNameLiteralGuardTests`（部分一致は拾わない。件数まで厳密に照合する）。

## 図で表せない定義の種類

図で表せない定義の種類（`UnsupportedDefinitionKind`）× 5 方言の宣言（あり得るか・検出するか・同期が落とすか・検出しない理由＝`DialectDeclarations`）と、種類 × 機能 8 つ × 方言の扱い（`UnsupportedTreatments`）は、このプロジェクトのデータ。

- `UnsupportedDefinitionExhaustiveTests`：全セル・機能ごとに許される値・宣言の組み合わせの決まりを見て、欠けを止める。
- `UnsupportedCoverageTests`：「検出する」セルと「なし」以外の扱いのマスを、固定するテストの名前か未実装の一覧のどちらか一方に結び付ける（名前の実在は `tests/` を構文木で照合）。実装との食い違いを止める。
- `CapabilityTreatmentParityTests`：能力の値と扱いのマスの一致。
- `SchemaImportWarningKindMapTests`：取込警告の種別と種類の写像。取込で告知する種類には警告の種別があること、まとめて告げる種別（`IsAggregated`）と扱いの表の「外す＋まとめて告知」が一致すること。
- 未実装の一覧（`PendingDetections`・`PendingTreatments`・`CapabilityTreatmentParityTests.Pending`）は空のまま保つ。検出しない理由の null は型が止める。
- mutation 実証済み。
- docs の方言の表との照合は `tests/QuickER.Tests/Docs/CLAUDE.md`「docs の方言の表」。

## 識別子の長さの上限

- 上限の正本は `IdentifierLengthLimits`（方言名の表。実 DB で確かめた値）。各方言の識別子クラスの `Limit` と `SyncDialectCapabilities.IdentifierLengthLimit` はそれを指すだけ。
- `IdentifierLengthLimit` は Unit / Max / TableFileNameMax（MySQL の表の名前のファイル名での 251 バイト）/ `RepresentsSupplementaryCharacters` を持ち、`Fit` / `Truncate` / `Accepts` / `AcceptsTableName` / `DescribeViolation` / `CanRepresent` / `StoredText` を提供する。
- `RepresentsSupplementaryCharacters` は MySQL だけ false。MySQL は識別子・説明の BMP の外の文字を断らずに黙って `?` へ置き換えて保存する（実測：サロゲート対は 1 つの `?`、対になっていないサロゲートはクライアントが U+FFFD にする。規則の正本は `IdentifierLengthLimit.StoredText`）。方言名で分岐せず、この宣言で扱う。
- 既定の制約名を上限に収める経路（省略できない引数・図の対象 DBMS か DDL・同期の方言か）は `src/QuickER.Model/CLAUDE.md`「識別子の長さの上限」。
- 利用者が付けた名前は縮めずに、DDL・同期の入口（`SqlNameText.Validate`）が上限と表せない文字を名指しして断る。ガードは `src/QuickER.Provider/CLAUDE.md`「利用者が付けた名前の上限」を参照する。
- 通知（GUI の名前の確定・ツールの応答・方言切替の確認）は `src/QuickER.Model/CLAUDE.md`「名前の上限の通知（NameLimitNotes）」。

## スキーマ修飾名の分け方（SchemaQualifiedName）

図のテーブル名を最初の `.` でスキーマと表に分ける規則は `SchemaQualifiedName` だけが持つ（`Split` / `TrySplit` / `IsQualified`）。2 つ目以降の `.` は表の名前の一部。前後が空でもそのまま。

- 呼び出すのは、5 方言の識別子クラス・SQL Server の説明の既定スキーマの判定・同期の計画・修飾名の注記・識別子の長さの判定・生成コードの同期 SQL・EF Core の Fluent 構成。
- 別々の規則（`Split('.', 2)` と `LastIndexOf` など）で分けると、`a.b.c` のような名前で DDL と CRUD が別々の表を指す。
- EF Core の Fluent 構成は、生成モデルの側で分けて `ToTable("表", "スキーマ")` を出す。EF Core の `ToTable(name)` は名前を分けないので、分けずに渡すと EF Core だけが既定のスキーマの `[sales.Orders]` を指す。テンプレートは分けた値を受け取るだけで、分け方の写しを持たない。スキーマの無い表の出力は不変。
- 生成ランタイムの `EntitySaveMetadata.QuoteTableName` は Dialects を参照できないので写しを持つ。一致は `SchemaQualifiedNameParityTests` が固定する。
- ガード：`SchemaQualifiedNameGuardTests`（Dialects・Model・Provider*・CodeGen.CSharp の .cs〔.g.cs を除く〕で、第 1 引数が `.` の `Split` / `IndexOf` / `LastIndexOf` / `Contains` を構文木で数え、理由つきの既知の一覧と厳密に一致させる）・`SchemaQualifiedNameParityTests`（規則の表と、SQL Server・SQLite の生成ランタイムの引用が方言の識別子クラスと同じこと。多ドット・空の部分・終端の引用符を含む名前）・`EfCoreSchemaQualifiedTableTests`（生成テキスト・両引数のエスケープ・SQL Server プロバイダの `ToQueryString`）・実 SQL Server の `SchemaQualifiedTableRuntimeTests`（EF Core 版と QuickER 版 Repository が同じ `[sales].[orders]` を読み書きする）。mutation 実証済み。
