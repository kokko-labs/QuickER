# QuickER.CodeGen.CSharp

C# コード生成の生成器の側（オプション・モデルビルダー・診断・ファイル構成・名前・テンプレート）の規則。
名前付きクエリの生成は `src/QuickER.CodeGen.CSharp/Queries/CLAUDE.md`、生成ダイアログは `src/QuickER.CodeGen.UI/CLAUDE.md` を見る。
生成されたコードの実行時の振る舞い（Repository・EF Core・インメモリ・リモート・同期のエンジン）は `src/QuickER.Runtime/CLAUDE.md`・`src/QuickER.Runtime.Sync/CLAUDE.md` などを見る。

## DB 非依存

- 型解決（DB 型→C# 型）はプロバイダ側の責務。CodeGen.CSharp は解決済みの `CSharpTypeInfo` の辞書を入力に受け取る
- この契約のため Provider は CodeGen.CSharp をプロジェクト参照する。Scriban が上位層の依存クロージャへ推移的に乗るが、Provider 自身は Scriban の型を使わない
- `CSharpTypeInfo` を中立アセンブリへ分離して参照を切る案は検討のうえ見送り
  - 実利が「`IncludeCodeGenModules=false` 構成から Scriban.dll が 1 本消える」だけで、契約 1 型のためのアセンブリ新設は投機的な間接層

## 生成の単発オプションのケース

- 単発オプションのケース（`CodegenCaseTests` の行 `compile-single-option-*`）は、行ごとに「そのオプションを既定へ戻した比較用のオプション」と図を持つ
  - 関係 OptionEffect が、同じ図の比較用の生成物と同じなら赤にする（空振りの検査）
  - GuidKey の行は文字列の主キーの表・その列を列名の違う外部キーで持つ子・2 つの列への完全一致と部分一致のクエリを持つ `CompileDiagrams.StringKey` を使う
    - 主キーがすべて int の図では GuidKey の値オブジェクトが 1 つも出ず、空振りになる
    - 部分一致の 2 本が警告で飛ばされることは同じ行の診断の期待が、GuidKey の値オブジェクト・外部キーの型の共有・飛ばしたメソッドが出ないことは `CodegenCaseTests` の行 `code-guid-key-value-object-shared-by-foreign-key` が宣言のシンボルで表明する
  - 対照（同じオプションで生成し直した生成物とは同じ）も関係 OptionEffect が見る。mutation 実証済み
- 生成コードの Roslyn コンパイル検証（`CodegenCaseTests` の行 `compile-*`）は Docker 不要で常時実行される

## DB が値を作る列の書き込み除外

DB が値を生成する列は、生成 Entity にマーカー属性を刻み、`EntitySaveMetadata` が QuickER 版 Repository の INSERT / BulkInsert / UPDATE から外す。SELECT では読む。
`EntitySaveMetadata` の振る舞いは `src/QuickER.Runtime/CLAUDE.md` を見る。

マーカーは 3 種類あり、**互いに共用しない**。

| 種類 | マーカー | 正本 | 方言によるゲート |
| --- | --- | --- | --- |
| 行バージョン（rowversion / timestamp） | `[StoreGeneratedColumn]` | `CSharpTypeInfo.IsRowVersion` | あり（`dialect_assigns_store_generated`） |
| 計算列・生成列 | `[ComputedColumn]` | `Column.IsComputed` | なし |
| DB が値を拒む採番列 | `[IdentityColumn]` | `Column.IsIdentity` | なし |

### 行バージョン列（store-generated）

- `EntitySaveMetadata` 側では `InsertProperties`（全列−store-generated）と `NonKeyProperties` から外す。SELECT 側の `SelectProperties` / `AllProperties` は変えない
- 属性の付与はオプションに依らず無条件。属性定義は `[UnboundedBinaryColumn]` と同型の emit ゲートで常時出力する（固定 infra が参照するため）
- 除外は「その方言の DB が値を採番するか」の能力フラグ `dialect_assigns_store_generated` と `repositories` が両方立つときだけ
  - 採番するのは SQL Server だけ。他方言のエンジンでは通常列として書き込み対象にする
  - フラグの正本は `RepositoryDialectVariables.AssignsStoreGenerated`
  - 方言を持たないスコープ（InMemory / EF Core の固定部。`repository_dialect` は単に `RepositoryDialects[0]`）では意味を持たない。参照箇所は必ず `repositories` と併せて判定する
- `RowVersionProperty` の XmlDoc は `repositories && 能力`／`repositories`／`in_memory`／EF Core の 4 アームで、`StoreGeneratedCapabilityGenerationTests` が固定する
  - SQL Server の DB が採番するため、明示挿入は実行時エラーになる（`Cannot insert an explicit value into a timestamp column.`）。除外はこれを防ぐ
  - EF Core は Fluent の `IsRowVersion()` が既に store-generated として扱うため非適用。InMemory は SQL を使わず無関係
- SQLite 単独生成では図の `timestamp` が日時に解決され、属性なしの通常列になる。意味が変わるのは、マルチターゲットで属性付き Entity を SQLite エンジンが扱うケースだけ

### 計算列・生成列

- 正本は `Column.IsComputed`（意味モデル。式は載らない。取込警告 `ComputedColumnExpressionLost` が式の喪失を告げる）
- `EntitySaveMetadata.Build` が INSERT / BulkInsert / UPDATE から方言に依らず無条件で外す。計算列であることは列自身の事実で、方言能力ではないため `dialect_assigns_store_generated` のゲートを掛けない
- `[StoreGeneratedColumn]` と共用しない。版の解決（`RowVersionProperty`）が「その属性の先頭 1 本＝版」を前提にしており、共用すると楽観排他・同期の全経路が計算列を版と誤認する
- 取込の判定は各プロバイダの責務。詳細は `src/QuickER.Provider/CLAUDE.md`「計算列と生成列」（方言ごとの判定は各方言の CLAUDE.md の「取込」）
  - 要点：SQL Server は `sys.computed_columns`、MySQL は `GENERATION_EXPRESSION` が空でないこと（`EXTRA` の部分一致は式の既定値の `DEFAULT_GENERATED` を誤検出し、完全一致は `VIRTUAL GENERATED INVISIBLE` を取りこぼす）、PostgreSQL は `attgenerated <> ''`、Oracle は `user_tab_cols` を `user_generated = 'YES'` で絞り `virtual_column='YES'` を計算列とする（式の `data_default` は LONG 型で `InitialLONGFetchSize = -1` が必須）、SQLite は `PRAGMA table_xinfo`（`table_info` は生成列を返さない）
- DDL・同期（AddTable / AddColumn / SQLite 再構築）は列定義行の直前に英語の注意コメントを出す
  - 正本は `ComputedColumnComment`。列名は `SqlComment.Sanitize` を通す。`IsComputed` の無い図は出力がバイト不変
- 実装先ごとの扱い
  - EF Core は Fluent へ `ValueGeneratedOnAddOrUpdate()`。式が無いので `HasComputedColumnSql` は出せない
  - InMemory は通常列のまま（式を評価できない）
  - EditModel / Mapper は rowversion と同じ「必須にしない＋入力があるときだけ代入」
- 主キーが計算列の図は生成時エラー。生成時 Info 診断が除外列を名指しする
- C# リバースは `[ComputedColumn]` で往復する。DBML / Mermaid / Excel は運ばない（DBML / Mermaid は出力時に欠落告知、Excel のマージ取込は一致した列の指定を現在図から引き継ぐ）
- GUI は `ColumnViewModel` のパススルーのみ（編集 UI なし）。図ファイルは v1 のまま。マルチターゲットの SQLite ミラーへは同期でも値が運ばれない（docs の既知の割り切り）
- ガード：`CodegenCaseTests` の行 `code-computed-column-*`・`CodegenCaseTests` の行 `model-computed-column-*`・`CodegenCaseTests` の行 `exec-computed-column-*`・`DialectDdlTableTests`（行 `ddl-computed-column-*`・`sync-*-computed-column-*`）・`{Sqlite,SqlServer}ComputedColumnRuntimeTests`・5 方言の `*ImportFidelityIntegrationTests`・`DocumentFormatTests`（行 `*-is-computed-*`・`roundtrip-computed-column-flag-preserved`）。mutation 実証済み

### DB が値を拒む採番列

- 正本は `Column.IsIdentity`（意味モデル。開始値・増分は載らない。SQL Server の取込は `sys.columns.is_identity`。取込警告 `IdentityColumnMarked` が名指しする）
- 取込が印を付けるのは「指定した値を拒む」採番だけ
  - SQL Server の `IDENTITY`・PostgreSQL の `attidentity = 'a'`・Oracle の `generation_type = 'ALWAYS'`
  - BY DEFAULT・`serial`・`AUTO_INCREMENT`・`AUTOINCREMENT` は値を受け付ける普通の列なので印を付けず、`AutoIncrementLost` で告げるだけ
- 計算列と同型で、生成 Entity に `[IdentityColumn]` を刻み、`EntitySaveMetadata.Build` が INSERT / BulkInsert / UPDATE から方言に依らず外す。EF Core は `ValueGeneratedOnAddOrUpdate()`、EditModel は必須にしない
- **主キーの採番列だけは外さず、印も刻まない**
  - 判定の正本は `CSharpGenerationModelBuilder.IsWriteExcludedIdentity`（`IsIdentity && !IsPrimaryKey`）
  - 生成される Repository はキーをアプリが決めて書き込む前提で、外すとキー無しの INSERT になる
  - 代わりに生成時 Warning `CodeGen_Warning_IdentityPrimaryKeyColumn` で名指しする。Repository 契約（`GeneratesRepositoryContract`）を生成するときだけ。キーを書き込む生成コードが無い構成では前提が効かない
- GUI のコード取込は主キーの印を現在図から引き継ぐ（`ReverseMergePostProcessor.ApplyPrimaryKeyIdentityMarks`）
  - 主キーの列にはコードに印が残らず、引き継がないと署名の `*IDENTITY` の差で置換の確認が出る。主キーでない列の印はコードの `[IdentityColumn]` が正本
- DDL・同期の注記は `IdentityColumnComment` が単一の正本
  - 出すかは扱いの表 `UnsupportedTreatments.For(IdentityRejectingValues, Ddl / Sync, 方言)` が `Annotation` かで決める。呼び出し側は方言名で分岐しない
  - 方言名は `DdlGeneratorBase` / `SyncScriptBuilderBase` の既定値の無い抽象 `DialectName`（基底を継がない Oracle の同期は自前の定数）
- 図ファイルは v1 のまま（欠落＝false で失われるのは書き込み除外だけ。計算列と同じ前方互換の判定）。`SchemaSignature` に `*IDENTITY` を含む
- この種類の列を持てない方言（宣言が `Impossible`＝MySQL・SQLite）では、印が値の書き手を消す。DDL は普通の列を作り、生成コードはその列を書かない
  - 判定の共有部品は `IdentityColumnMarks`（Provider。宣言を引き、方言名で分岐しない）
  - GUI の対象 DBMS の切替は、印の列を確認の一覧に挙げて印を外す。`ChangeTargetDbmsCommand` が型・NULL 許容と同じ 1 履歴で戻す（`ColumnViewModel.IsIdentity` の setter は internal）
  - 図と違う方言で生成する CLI の `--provider` と MCP の `generate_csharp` / `generate_ddl` の `provider` は、方言違いの警告と同じ行で印の列を名指しする
  - QuickER 版 Repository の実効方言のどれかが持てない方言（SQLite）なら、単一方言でもマルチターゲットでも、主キーでない NOT NULL の印の列を生成時 Warning `CodeGen_Warning_NotNullIdentityColumns` で名指しする（`AddNotNullIdentityColumnWarning`）
    - 計算列の `AddNotNullComputedColumnWarning` と同じ乖離。方言の数では絞らない
  - CodeGen.CSharp は Provider を参照できないので、持てるかの判定の正本は QuickER.Dialects の `DialectDeclarations.CanOccur` に置き、`IdentityColumnMarks.CanBeHeldBy` はそれへ委譲する
- 実 DB の書き込み除外は IdentityColumnFixture（SQL Server 方言の QuickER 版 Repository＋EF Core・非主キーの採番列 1 本）で固定する
  - SqlBulkCopy は KeepIdentity なしでは送った値を無視して採番するので、BulkInsert の除外は実 DB では観測できない（回帰の網としてだけ持つ）
- ガード：`DialectDdlTableTests`（行 `ddl-identity-column-*`・`sync-*-identity-column-*`。注記を出す方言を書き下ろした期待で照合し、扱いの表との一致は行の Treats の自動の照合）・`CodegenCaseTests` の行 `code-identity-column-*`・`CodegenCaseTests` の行 `model-identity-column-*`・`CodegenCaseTests` の行 `exec-identity-column-*`・`IdentityColumnSaveMetadataTests`・`{SqlServer,PostgreSql,Oracle}ImportFidelityIntegrationTests` の `Import_*Identity*`・`{SqlServer,PostgreSql}IdentityColumnRuntimeTests`・`DocumentFormatTests`（行 `*-is-identity-*`・`roundtrip-identity-column-flag-preserved`）・`SchemaSignatureTests` の `Compute_IdentityFlag_ChangesSignature`
- 切替・CLI・MCP・コード取込側のガード：`MainViewModelTargetDbmsTests` の「採番の…」3 件・`UndoCommandRoundTripTests` の `ChangeTargetDbmsCommand`・`ProviderMismatchWarningTests` の採番の 4 件・`CSharpReverseRoundTripTests` の `Merge_IdentityPrimaryKey_KeepsMarkAndNeedsNoReplacementConfirmation`・`CodeReverseCommandServiceTests` の `Run_IdentityPrimaryKey_NoConfirmAndKeepsMark`
- mutation 実証済み

## マルチターゲット × rowversion

サーバー＝SQL Server・ローカル＝SQLite のハイブリッド構成を組めるようにする「生成対応のみ」の対応。同期ロジックはアプリ側。

1. **型統合**
   - マルチ辞書のいずれかの方言解決が `IsRowVersion=true` の列は型不一致エラーにせず、その解決（`byte[]` ＋ `IsRowVersion`）で主辞書を統一する
   - 正本は `MultiDialectTypeReconciler.ReconcileRowVersionTypes`。実効方言の指定順で先に行バージョンと解決した方言を採る。`DiagnoseTypeMismatches` は統一済み列 ID をスキップする
   - 統一した列は Info 診断 `CodeGen_Info_MultiTargetRowVersionColumns` で通知する
     - 同じ表記でも綴りで食い違いの中身が変わる。`timestamp` は sqlite で `DateTime`（日時の別名）、`rowversion` は sqlite で `string`（未知の型）。統一先はどちらも同じ
2. **書き込み除外の方言ゲート**：前節の store-generated を参照
3. **DDL 型変換**
   - `CanonicalTypeKind.RowVersion` を持つ。SQL Server が `rowversion` / `timestamp` を解析し（`TryFormat` は代表表記 `rowversion`）、Sqlite の `TryFormat` は `BLOB` へ落とす。他 3 方言は `default: return false` のまま（変換不能一覧に載る）
   - 落とすとき `ColumnTypeConversion.MakeNullable` が立ち、NOT NULL を解除する（ローカルは採番しないので未同期行が空になる）
     - 判定は「変換先を読み直しても RowVersion のままか」で、方言名で分岐しない
     - GUI は `ChangeTargetDbmsCommand` が NULL 許容の変更も同じ 1 履歴で巻き戻す
   - **`CanonicalTypeTokenAttacher` は行バージョン列にトークンを刻まない**（`IsRowVersion` でスキップ）
     - 中立トークンは「DB が採番する」を運べない。刻むと C# リバースがただのバイナリ列として復元して、版ガードが黙って消える
4. **契約 doc の非対称告知**
   - 実効方言に「採番するエンジン」と「しないエンジン」が混在するとき（`store_generated_dialects_differ`）だけ、共有契約の `mode` param doc 3 箇所と `ConcurrencyMode` enum へ「値を採番しないエンジンはこの引数を無視する」の 1 文を足す
   - 契約は 1 回しか出ないため実装側 doc では補えない。単一方言の出力はバイト不変
5. **既知の乖離**
   - SQLite 側は版ガードなし（`ForceOverwrite` は no-op）。ミラーの鮮度は誰も保証しない。`BLOB` を SQL Server へ戻しても `varbinary(max)`（非可逆）
   - InMemory は SQL Server の代役（擬似版採番）。EF Core はマルチターゲットと排他。リモートは現状維持

- フィクスチャは `MultiTargetRowVersionFixture.g.cs`（1 テーブル `sync_items`・sqlserver/sqlite）
- 実 DB 検証：`MultiTargetRowVersionRuntimeTests`（Docker。版ガード維持＋keyed DI での同期最小形）・`MultiTargetRowVersionSqliteRuntimeTests`（CI 常時。ミラー列の読み書き・変換 DDL の BLOB / NULL 許容）

## FK 列の値オブジェクト型統一

`GenerateValueObjects=true` のとき、リレーションの子側列は親側列の VO 型を共有する（`ship_customer_id` → `CustomerIdValue`＝「同じ識別子は同じ値型」）。利用者向けの規則は `docs/code-generation.md`（.ja）の値オブジェクト節。

- 動機は EF Core の制約。モデル検証が FK と参照先 PK の CLR 型一致を要求し、HasConversion は判定に関与しない
  - 列名ごとの別型では、FK 列名≠PK 列名の図（自己参照では必然）で生成 DbContext がモデル検証ごと落ち、EF Core 経路が丸ごと使用不能になる
  - Fluent 側では原理的に直せない。生成時の型解決だけが修正面。シャドウ FK 化は実プロパティへ値が入らずパリティを壊すため不可
- 実装は「列 Id → VO キー」の単一マップ（`CSharpGenerationModelBuilder.ValueObjects.cs` の `BuildValueObjectKeys`）
  - VO を使う全経路（Entity プロパティ・EditModel・EF Fluent・DSL クエリ束縛・同期記述子）は `ResolveValueObject` 経由で自動追従する。**列名→VO 型の独自解決を下流に作らない**
- チェーン（FK の FK）は不動点まで解く。循環は「循環に属する列の親辺を全部落としてから解決」し、走査順に依存しない
- 同一子列が異なる型へ解決される親を複数参照すると生成時エラー（`CodeGen_Error_ValueObjectForeignKeyTypeConflict`）。下地の C# 型が食い違うペアは統一しない
- 統一列は Info 診断（`CodeGen_Info_ValueObjectForeignKeyUnified`）で名指しする（型名が変わる破壊的変更のため）
- グループの定義の正（型・長さ・PK 優先判定）は、自列名でそのキーを名乗る native 列に限る（寄ってきた FK 列が PK のとき VO 名が FK 列名に化けるのを防ぐ）
- FK 列名＝PK 列名の図と VO 無効の図は出力がバイト不変
- ガード：`CodegenCaseTests` の行 `code-vo-foreign-key-*` / `model-vo-foreign-key-*`（統一・チェーン・循環。宣言のシンボルと EF Core のモデルで見る）・`CodegenCaseTests` の行 `diag-vo-foreign-key-*`（退化エラー・Info）・`CodegenCaseTests` の行 `probe-efcore-model-vo-foreign-key-name-mismatch`（生成→コンパイル→実 `DbContext.Model` でモデル検証が通る）

## DB 定義メタ属性

`[DbColumnMeta]` / `[DbTableMeta]` は、生成 Entity を「DB 定義の自己記述ドキュメント」にする定義用メタ（C# リバースの布石）。列に方言中立の型トークンと説明、テーブルに説明を刻む。

- 型トークン（`CanonicalTypeToken`）の例は `string(50)` / `decimal(10,2)` / `int32`。小文字、`-1` は max
- 付与は `IncludeDataAnnotations` ON かつ Entity 生成時のみで、対象 DB・Repository / EF Core 設定に依らない
- トークンは図の方言の `ITypeCatalog.TryParse` → `CanonicalTypeToken.Format` を `DiagramCodeGenerator` の後処理（`CanonicalTypeTokenAttacher`）で主辞書へ付加する
  - マッパ実装は変えない。解析不能な自由記述型はトークン null（属性省略）
- 可搬図では方言に依らず同一トークンになる（`CodegenCaseTests` の行 `code-portable-fixture-identical-across-postgresql-mysql-oracle`・`CodegenCaseTests` の行 `code-portable-fixture-sqlserver-adds-sql-column-type`）
  - 可搬フィクスチャの文字列は Unicode で統一する（Ansi / Unicode の差でトークンが割れるため）
- 実行時型付けの `[SqlColumnType]`（SQL Server 専用・Size ガード）とは責務分離で無関係

### Column 属性は常時付与

- `[Column]` は `IncludeDataAnnotations` の対象外で、常時付与する。永続化の構造マッピングであるため
- 生成ランタイムの列判定 2 箇所は `[Column]` を許可リストとして読む。これが正本
  - `EntityBaseCore.GetValueProperties`（`HasSameValues` / `GetValueHashCode` が読む値プロパティ集合）
  - `EntitySaveMetadata.Build`（INSERT / SELECT / UPDATE の SQL と InMemory の列コピーが読む列集合）
  - 除外リスト方式だと、拡張シム `EntityBase` や具象エンティティ partial へ足した setter 付きプロパティが、実在しない SQL 列に化ける（InMemory は緑のまま、実 DB だけが Invalid column name で落ちる）
- 帰結：手書きプロパティは `[Column("実在列名")]` を付けたときだけ列になる（オプトイン）。`[Table]` / `[Key]` は従来どおり `IncludeDataAnnotations` 側（Repository 契約生成時の必須条件も従来どおり）
- using は Entity / Runtime / Repository / EfCore / InMemory の各バケットで `System.ComponentModel.DataAnnotations.Schema` を無条件要求する（`GeneratedFileUsings`）
- ガード：`ExtensionShimAcceptanceTests` の 2 本（シム追加プロパティは列でない／`[Column]` 付き手書きプロパティは列になる）と、annotations OFF の生成テスト

### 同義の型表記の保存（verbatim 併記）

- トークンは型の「意味」しか運ばないため、同義の複数表記（`numeric` / `decimal`・`ntext` / `nvarchar(max)`・`datetime` / `datetime2`・`smallmoney` / `money`）は代表表記へ畳まれる
  - そのままリバースすると図の型表記が黙って変わり、次の同期が該当列全部へ `ALTER COLUMN` を出す
- 対策は生成側の verbatim 併記。**トークンを同じ方言へ書き戻すと綴りが揃わない列だけ**、`[DbColumnMeta].NativeType` へ元の表記を刻む（`CSharpTypeInfo.VerbatimDbType`）
  - リバース側は「その表記を対象方言で解析した canonical がトークンの canonical と一致する」ときだけ採用する。不一致（手編集・別方言リバース）はトークンを採って警告する
- 綴り一致の判定は `DbTypeText.AreEquivalent`（Trim ＋大小無視）。`SchemaDiffService` の `ALTER COLUMN` 判定が方言の正規化の後に使う比較（`AreSameType`）と同じ規則にする
  - 厳密一致にすると、`TryFormat` が大文字を返す SQLite 方言の図で全列に verbatim が出て、生成物が総入れ替えになる
  - 大小だけ違う綴りはコードから読めないので、C# のマージ取込が現在図の同じ列の綴りへ戻す（`ReverseMergePostProcessor.ApplyTypeSpellings`。`AreEquivalent` で同じときだけ）。戻さないと SQLite の図で取り込むたびに `int` が `INT` へ変わり、構造署名の差として置換の確認が出る。ガード：`FormatRoundTripTests`（形の行 `dbml-long-default-name-sqlite` の C# のマージの表明。エンジンは大小だけの違いを運ばない面 `TypeCase` に宣言し、マージが戻す面に入れる）
- 対象列は生成時 Info 診断（`CodeGen_Info_NonCanonicalTypeSpellingColumns`）で名指しする
- 行バージョン列はトークンごと不出力なので verbatim も出ない
- ガード：`CSharpReverseVerbatimTypeTests`（大小差の非出力・行バージョンの非出力）・`FormatRoundTripTests`（7 表記の往復＝形の行 `csharp-verbatim-type-*`・不一致の警告＝規則の行 `csharp-rule-verbatim-*`）

### DbTableMeta の PrimaryKeyOrder

- `[DbTableMeta]` は説明に加えて `PrimaryKeyOrder`（主キーのプロパティ名を主キーの順に並べた `string[]`）を持つ
  - `[UniqueConstraint]` の構成列と同じ形で、リバースは同じ「プロパティ名 → `[Column]`」の逆写像で解決する
  - 主キーの順序が宣言順と食い違うテーブルにだけ出る。出すかの判定の規則は `src/QuickER.Model/CLAUDE.md`「主キーの順序の出力判定」を見る

## 層別フォルダ出力と出力先

### 層別フォルダ出力（LayeredOutput）

分割生成のファイルを DDD 風の層フォルダへ振り分ける bool オプション（既定 false）。CLI `--layered-output` ＋層フォルダ 4 フラグ、GUI「層別フォルダへ振り分けて出力する」チェック＋パス 4 欄。

- ON は `SplitFilesByCategory` を自動含意する。生成側の分割判定は `CodeGenerationOptions.EffectiveSplitFilesByCategory` に一本化する
  - GUI のサーバー欄はリモートサービス ON のときだけ表示する。含意は両方向で、ラジオは常時操作可（「1 ファイルにまとめる」を選ぶと層別チェックが外れる）
- バケット→層の対応は固定
  - domain：Entity / VO / 契約 `Repositories.g.cs` / インライン時の `Runtime.g.cs`
    - 契約は DDD のポートとして domain に置く。EditModel→契約依存が「プレゼン→ドメイン」参照に収まり、Runtime コア同居はパッケージモードで domain csproj が QuickER.Runtime を参照する構造と対称
  - presentation：EditModel / Mapper
  - infrastructure：方言実装 / EntityFrameworkCore / InMemory / Sync / Http と、対応する `Runtime.{...}` 固定 infra
  - server：`RemoteServer.g.cs` ＋ `Runtime.AspNetCore.g.cs`（FrameworkReference 制約で通常のクラスライブラリにできない）
  - `*.g.md` は out_dir 直下（どの csproj にも属さない）
- 参照方向は presentation→domain←infrastructure←server
- 層判定の正本は `GeneratedFilePlanner.LayerOf`。Buckets ＋ ContractOnly ＋ MultiDialect から導出する
  - ContractOnly 単独では、EF Core / インメモリ単独構成の契約ファイルが方言実装レイアウトを通らず ContractOnly=false で出るため誤分類する。MultiDialect 条件が必須
- フォルダは層単位の相対パス 4 オプション（`DomainLayerDirectory` など。既定 Domain / Presentation / Infrastructure / Server。複数階層可：`MyApp.Domain/Generated`。空白は既定へフォールバック）
  - 検証は生成前診断と `GeneratedFileWriter` の防御が `LayerDirectoryValidator`（判定の正本は `PathSegments.CheckRelativePath`）を共有する。診断は使われる層のみ（Plan から導出）
- モード切替で旧位置のファイルは削除しない（docs 明記）
- サンプル（`ec-order` / `ec-order-remote`）は層別出力を使わず、`--out` を `Generated/` へ向ける従来構成のまま

#### 名前空間の既定

- 名前空間の既定は層フォルダに追従する
  - 層ルート＝フォルダパスの `/` → `.` 変換（`LayerNamespaceRoot`。`MyApp.Domain/Generated` → `MyApp.Domain.Generated`）
  - 各バケットは `{層ルート}.{DefaultSuffix}`：domain は `.Entities` / `.ValueObjects` / `.Repositories` / `.Runtime`、presentation は `.EditModels` / `.Mappers`、server は `.RemoteServer` / `.AspNetCore`
  - インフラ層は固定 infra と per-entity を系統ごとに同一サブ名前空間へ統合する（`{I}.SqlServer` ＝ `Runtime.SqlServer.g.cs` ＋ `Repositories.SqlServer.g.cs`）
    - 方言間は SqlExecutor / EntitySaveMetadata などの同名型があるため、単一名前空間化は構造的に不可
  - `RootNamespace` は導出既定に現れない。明示の名前空間オプションは常に最優先
- 名前空間として成立しないフォルダは、その層で導出が実際に使われる場合のみ診断エラーにする（全バケット明示指定なら許容）
  - 判定は、計画が名前空間を決めた出所の記録 `GeneratedFilePlanner.UsedNamespaceSources` から行う。名前空間オプションの検証も同じ記録から「使われる値だけ」を検証する
  - パッケージ参照モードの `RuntimeNamespace`・層別出力の `RootNamespace` は検証しない
  - **計画の中で計算して捨てる名前空間の解決を作らない**。作ると使っていないオプションが記録に載り、使われない値で生成が止まる（通常分割の EF Core / インメモリ / 同期 / HTTP は `ResolveNamespace` を通さず契約の名前空間から組み立てる）
- ガード：`CodegenCaseTests` の行 `relation-unused-invalid-namespace-package-runtime` / `relation-unused-invalid-namespace-layered-root` / `relation-unused-invalid-namespace-split-all-explicit-root`（不正でも使われない値は、正しい値を入れた生成物と全ファイル・診断が同じ）・`CodegenCaseTests` の行 `diag-invalid-used-namespace-inline-runtime` / `diag-invalid-used-namespace-split-root-fallback` / `diag-invalid-used-namespace-split-remote-server-root`・`CodegenCaseTests` の行 `diag-layered-folder-not-namespace` / `diag-layered-folder-not-namespace-allowed-when-all-explicit` / `diag-layered-invalid-layer-directory` / `diag-layered-invalid-layer-directory-layered-off` / `diag-layered-unused-server-layer-directory`。mutation 実証済み

#### 固定 infra の可視性

- 固定 infra の可視性は層別出力で `public` にする
  - 多アセンブリ配置では `internal` な Owner / OwnerModel・IncludeNode・CascadeNavigation などを別層の生成物が参照できず、ビルドエラーになる
  - 既存の `infra_visibility` テンプレート変数を `LayeredOutput ? "public" : "internal"` で切り替える。パッケージ配布と同一規則（同じ型は QuickER.Runtime で既に public）
  - `InternalsVisibleTo` の生成は採らない（アセンブリ名を確実に知り得ない）。型の層移動も成立しない（IncludeNode / CascadeNavigation は domain 側の SqlQuery / EntityGraphSaver 自身が参照する）
- 不変条件：層分けの ON / OFF で、スキーマ依存ファイルは namespace 宣言・using 行と、固定 infra と同じ規則で可視性を切り替える型（`{Repository}UniquenessConstraints`）の可視性以外が一致し、Runtime 系はさらに可視性だけが違う。非層別モードの出力はバイト不変
  - ガード：`CodegenCaseTests` の行 `relation-layered-vs-split-order`・`CodegenSuccessMatrixTests`（層別の行の関係 LayeredVsSplit）・`LayeredOutputTests`。`.g.md` のファイル一覧表は導出後の実名前空間を載せる
- 多アセンブリ検証：生成物を層ごとに別 Roslyn コンパイルし、参照グラフ（Infrastructure→Domain・Presentation→Domain・Server→Domain+Infrastructure）で組む 4 プロジェクト分割のコンパイル（`CodegenCaseTests` の行 `compile-layered-projects-*`）。mutation 実証済み
  - 単一コンパイルでは internal が見えて、境界越えを検出できない（1 アセンブリでしか出ない崩れは `CodegenCaseTests` の行 `compile-layered-one-assembly-*` が見る）

### 出力先のサブフォルダ

- `ApiDocsSubdirectory`：`*.g.md` を `docs/` などのサブフォルダへ移す汎用オプション（CLI `--api-docs-subdir`／GUI「出力先サブフォルダ」欄＝GenerateApiDocs 連動）
  - 層別に依らず全出力モードで有効。検証は `LayerDirectoryValidator` を共有する。エラー文言の resx キーは `CodeGen_Error_InvalidOutputSubdirectory`
- `CodeSubdirectory`：`.g.cs` を層フォルダ／出力ディレクトリのさらに 1 段下へ出す相対パス（既定 null＝オプトイン。CLI `--code-subdir`／GUI「出力先」カードの出力先パス直下の行。出力モード・層別に依らず常時表示）
  - 層別・分割・非分割の全出力モードで有効。生成コードと手書きコードを同一 csproj 内で分けるためのもの（SDK 形式は `**/*.cs` を暗黙 glob するため csproj の変更は不要）
  - **名前空間には追随しない**。追随させると、手書き partial が `.Generated.` 名前空間を名乗る羽目になる
    - `ResolveLayerDirectory`（`LayerNamespaceRoot` が読む）へ混ぜない。合流点は `GeneratedFilePlanner.Plan` の出口 1 箇所（内部は `PlanCore` ＋ `ApplyOutputDirectories`）だけ
    - 非分割は `PlanCore` が早期 return するため、分割パス末尾で付与すると素通りする
    - 名前空間へ出ないので識別子の検証は課さない（`generated-code` も可。「名前空間導出に実際に使われる値だけ検証する」の帰結）
  - `.g.md` は追随しない。置き場の正本は `ApiDocsSubdirectory` だけで、暗黙のフォールバックを作らない
  - 検証は `LayerDirectoryValidator` を共有し、出力モードに依らず効く
  - 不変条件：サブフォルダの有無で生成テキストはバイト不変（配置だけが変わる）。ガード：`CodegenCaseTests` の行 `relation-code-subdirectory-layered`。mutation 実証済み

### HTTP クライアントのバケット分離

- `Http{Entity}RemoteRepository`・`AddGeneratedHttpRemoteRepositories`・OwnedHttpClient は、契約ファイルから独立したバケット Http（テンプレートガード `render_http_client`）へ常時分離する
  - 分割時は `Repositories.Http.g.cs`（層分けでは infrastructure 行き）。非分割時は従来位置に同居し、出力はバイト不変
- 名前空間は契約と同一の `{RepositoryNamespace}` に据え置く
  - EfCore などの導出サブバケットとは意図的に別規則。サブ名前空間にすると、型 FQN と非分割出力まで破壊される
- 契約ファイルの HTTP 系 using は、`GeneratedFileUsings` の Repository ケースの `spec.EmitSharedInfra` ゲートで落とす（固定 infra を持つ Runtime.g.cs・非分割本体・パッケージソースのコアは従来どおり保持）
- クライアント固定基底（`HttpRemoteRepositoryCore` / RemoteJson）は Runtime コアに在る

## ランタイム配布

生成コードのスキーマ非依存な固定コード（ランタイム）は、既定でインライン出力する（自己完結・依存最小）。`UseRuntimePackages`（CLI `--use-runtime-packages`／GUI チェックボックス）で固定コードを出力せず、NuGet パッケージへの参照で賄うモードに切り替わる。

### 7 パッケージ

| パッケージ | 中身 | 依存 |
| --- | --- | --- |
| `QuickER.Runtime` | 共通基盤＋方言中立契約 | なし |
| `QuickER.Runtime.SqlServer` | SQL Server のエンジン | ADO。脆弱性対策の System.Security.Cryptography.Pkcs をピン |
| `QuickER.Runtime.Sqlite` | SQLite のエンジン | ADO。SQLite の版をそろえる SQLitePCLRaw 3.0 系をピン |
| `QuickER.Runtime.EntityFrameworkCore` | EF Core 共通部品 | EF Core Relational。ADO なし。DI 登録拡張なし |
| `QuickER.Runtime.InMemory` | インメモリ基盤 | BCL のみ（Core への ProjectReference だけ） |
| `QuickER.Runtime.AspNetCore` | リモートサーバーの固定エンジン | PackageReference ゼロ＋ `Microsoft.AspNetCore.App` の FrameworkReference のみ |
| `QuickER.Runtime.Sync` | 双方向同期エンジン | BCL のみ（Core への ProjectReference だけ） |

- ADO 依存は方言別に排他
- 依存集合の完全一致のガードは `src/QuickER.Runtime/CLAUDE.md` を見る
- DI 登録拡張（`AddGenerated*Repositories`）はエンティティ別登録を含むスキーマ依存物なので、常に生成側に出す（パッケージには入れない）
- パッケージ用ソース（`src/QuickER.Runtime*/Runtime*.g.cs`・7 本）の正本は Scriban テンプレート
  - `RuntimePackageSourceRenderer` が「空図＋全機能 ON＋固定名前空間＋internal→public」でレンダリングしたものをチェックインする。**手編集禁止**
- PackageReference の届け方は「案内のみ」（生成ヘッダ＋ダイアログ／CLI 出力。csproj には触らない）
  - バージョンはロックステップ（`Directory.Build.props` の `VersionPrefix` ＝パッケージ版＝ツール版）。案内の版解決は `RuntimePackages.ResolveGuidanceVersion`
  - 公開は `.github/workflows/publish.yml`（workflow_dispatch・dry_run 既定 true・認証は Trusted Publishing＝GitHub OIDC の一時キー）

### 分割生成のファイル構成

- 分割生成（`SplitFilesByCategory`）のファイル構成はパッケージと 1:1 対称にする
  - 固定ランタイムは `Runtime.g.cs`（`{Runtime}`）／`Runtime.SqlServer.g.cs`・`Runtime.Sqlite.g.cs`（`{Runtime}.{方言}`）／`Runtime.EntityFrameworkCore.g.cs`／`Runtime.InMemory.g.cs`／`Runtime.AspNetCore.g.cs`／`Runtime.Sync.g.cs` の 7 系統
  - `Repositories*` 系（＋ `RemoteServer.g.cs`）は、per-entity の契約・実装・DI 登録・DbContext・per-entity エンドポイントなどスキーマ依存物だけにする
  - 命名は「ファイル名・名前空間サフィックス＝パッケージ名サフィックス」の単一規則。EfCore 略記は使わない
  - C# の型名は別軸で不変：`EfCore{Entity}Repository`・`QuickErDbContext`・`AddGeneratedEfCoreRepositories`
  - Runtime 系は有効にした機能の分だけ出る。方言ファイルは QuickER 版 Repository 生成時、EF Core / InMemory / AspNetCore / Sync は各オプション（`GenerateEfCoreRepositories` / `GenerateInMemoryRepositories` / `GenerateRemoteServices` / `GenerateSyncSupport`）が有効なとき
- `UseRuntimePackages` の意味は「Runtime 系ファイルを 1 本も出さず、生成コードの using が固定パッケージ名前空間（`QuickER.Runtime*`）を指す」だけ。`Repositories*` 系はモードの on / off で内容が変わらない
  - ガード：`CodegenCaseTests` の行 `relation-runtime-type-set-*`（Runtime 系ファイルの型集合＝対応パッケージの型集合）・`CodegenCaseTests` の行 `code-split-runtime-files-hold-no-schema-dependent-types`（Runtime 系にスキーマ依存型が混ざらない）・`CodegenCaseTests` の行 `code-split-sync-fixed-engine-and-per-entity-code`（同期の固定部と表ごとのコードの分割）・`CodegenCaseTests` の行 `relation-runtime-packages-split-multi-dialect`（スキーマ依存ファイルはモード間で同一）・`CodegenCaseTests` の行 `files-split-runtime-packages-no-fixed-runtime-file`（パッケージモードで Runtime 系が 1 本も出ない）

### 併用の可否

- インメモリ Repository（`GenerateInMemoryRepositories`）はパッケージ参照モードと併用できる
- EF Core 生成（`GenerateEfCoreRepositories`）もパッケージ参照モードと併用できる
  - EF Core 固定 infra を `TContext : DbContext` ジェネリックにしたため、具象 `QuickErDbContext` への依存が生成側（DI 登録・エンティティ別実装）だけに残り、EF Core エンジンをパッケージへ切り出せる
  - パッケージモードでも `QuickErDbContext`・Fluent 構成・`EfCore{Entity}Repository`・`AddGeneratedEfCoreRepositories` は常に生成側に出力する
  - EF Core パッケージの EntitySaveMetadata は縮小版。方言クォートを焼き込んだ SQL 文メンバー（`TableName` / `*Sql`）は QuickER 版 Repository コンテキスト（`repositories`）に限ってガードし、持たない
  - マルチターゲット × EF Core の排他は別の理由（契約の型同一性）で残る

## リモート契約生成

`GenerateRemoteContracts`（CLI `--generate-remote-contracts`／GUI「リモート対応」チェック＝DB アクセス配下、「なし」のときは非表示）は、リモート操作用インターフェイス `I{Entity}RemoteRepository` を追加生成する bool オプション（既定 false）。

- ランタイム共通部は、オプションに依らず常時出力する基底分割
  - `IRemoteRepositoryCore<TEntity, TKey>`（CRUD ＋ Save×2 ＋ `CheckUniquenessAsync`＝ネットワーク境界を越えられる操作）← `IRepositoryCore`（＋ `Query()`・生 SQL 3 種・BulkInsert）
  - per-entity 契約が名指しするのは拡張シムの `IRemoteRepository<,>` / `IRepository<,>`。既存の利用コードは変更なしでコンパイルできる
- ON のとき `I{Entity}RemoteRepository`（拡張シム `IRemoteRepository` 継承＋名前付きクエリ全部。Dsl / Sql / Manual ともシグネチャはリモート可能）が追加される
  - 既存の `I{Entity}Repository` は、それと拡張シム `IRepository` を継承する全機能面のまま。純粋に追加的で、ON にしても既存コードは壊れない
- 実装クラス・DI 実装登録は従来どおり全機能面基準。間仕切りはインターフェイス水準だけでエンジンに分岐はない。リモート面は同一インスタンスへの転送として追加登録する（keyed 版も同様）
- EF Core・InMemory・マルチターゲット・`UseRuntimePackages` と直交する
- リモート面だけに依存したコードは、実体を Web サービス経由の実装（リモートサービス生成）へ差し替えてもコンパイル時に安全
- **Repository 契約（QuickER 版 / EF Core / インメモリ）が 1 つも無い構成でのリモート指定は生成時エラー**（`CodeGen_Error_RemoteRequiresContract`）
  - リモート面は契約を拡張する形でしか出ないため、止めないと診断ゼロで何も生成されない
  - GUI はチェック欄ごと隠し、非表示時は保存値も落とす。踏むのは CLI / MCP / 手書き config
- リモートサービス生成（HTTP + JSON のクライアント／サーバー実装）の振る舞いは `src/QuickER.Runtime.AspNetCore/CLAUDE.md` を見る
- ガード：`RemoteContractRuntimeTests`（CI 常時実行）

## DB アクセスの排他と単独出力

- Repository バケットは「共通契約」（`GenerateRepositories || GenerateEfCoreRepositories` で出力）と「QuickER 版 Repository 実装」（`GenerateRepositories` 時のみ）に分割されている
  - `GenerateEfCoreRepositories` 単独の生成物には、QuickER 版 Repository 実装の ADO 依存（Microsoft.Data.SqlClient / Microsoft.Data.Sqlite）が一切出ない（ガードテストあり）
- QuickER 版 Repository は `RepositoryDialects` で方言を選ぶ多 DB 対応。未指定は既定 `sqlserver` の単一へフォールバックする
  - 未対応方言を含む指定は、`GenerateRepositories=true` なら検証エラー、`false` なら `sqlserver` へ丸ごとフォールバックする
    - CLI が `--repository-dialects` 未指定時に図の方言名を焼き込むため。これがないと、PostgreSQL / MySQL / Oracle の図で Repository 非生成の generate まで落ちる（GUI の空リスト時と同一出力）
  - `sqlserver` は Include の無い取得がプレーン SELECT・Include のある取得が FOR JSON。`sqlite` はプレーン SELECT ＋ IncludeLoader のマルチクエリ・LIMIT / OFFSET・strftime
  - 単一指定では片方言のみを出力し、方言間の ADO 依存も排他（ガードテスト双方向）
- `RepositoryDialects` に複数方言を指定するとマルチターゲット出力になる
  - 中立契約（`IRepository` / `I{Entity}Repository` / `ISqlExecutor` など）を `RepositoryNamespace` に 1 回、方言別実装を `.SqlServer` / `.Sqlite` の方言別 namespace に出力する
  - 分割時のファイルは `Repositories.g.cs` ＋ `Repositories.SqlServer.g.cs` など。いずれも per-entity 実装と DI だけで、方言エンジンの固定 infra は `Runtime.{方言}.g.cs` 側にある
- EF Core 実装（`GenerateEfCoreRepositories`）も分割時は同じ流儀で、`Repositories.EntityFrameworkCore.g.cs`・`{RepositoryNamespace}.EntityFrameworkCore` に出力する
  - 契約 namespace のサブ名前空間へ導出するのみで、個別の名前空間オプションは持たない。サフィックスはパッケージ名に揃えた `EntityFrameworkCore`
  - インメモリ実装も同流儀で `Repositories.InMemory.g.cs`・`{RepositoryNamespace}.InMemory`
- DI は方言別拡張＋ keyed 版（`AddGeneratedSqlServerRepositories` / `AddGeneratedSqliteRepositories`・`object? serviceKey` 付き）で、単一契約型を keyed 解決して同一プロセスで複数 DB へ書き分けられる
  - `ISqlExecutor` はリポジトリの省略可能な第 3 引数。非 keyed の DI 登録はコンストラクタ注入（`AddScoped`）に任せ、keyed 版だけが `GetKeyedService<ISqlExecutor>(key)` を明示的に渡す
  - 省略時は既定実装を組むので、手動 new は不変。EF Core 版も同型
    - 固定生成（`new SqlExecutor(connectionFactory)`）だと、DI へ登録したラッパー実装が生 SQL 経路に一切効かない
- マルチターゲット（実効方言 2 つ以上）と `GenerateEfCoreRepositories` は排他で、併用指定は診断エラーになる
- GUI の生成ダイアログは DB アクセスをラジオ 3 択（なし＝既定／QuickER 版 Repository／EF Core）で排他選択させる。両方 ON はパリティ検証用に CLI／オプション直指定でのみ可能
  - QuickER 版 Repository は常時選択可。対象 DB チェック群（SQL Server / SQLite）で選ぶ。既定は図の方言、未対応方言の図では空、最低 1 つ必須
- ガード：`MultiTargetRepositoryRuntimeTests`（Docker 依存の実 DB 結合）・`MultiTargetRepositorySqliteKeyedResolutionTests`（keyed 解決の型検証。CI 実行）

## Repository を作れる表が無い図

- 固定 infra（Repository の共通契約・方言エンジン・クエリ / インメモリ / EF Core の土台）はオプションとスペックだけで出し、図の表の主キーの形に依らない。`_04_RepositoryContractCore.scriban` の外側のゲートに `repository_classes` を入れない（パッケージ参照モードで固定 infra が常にパッケージにあるのと同じ形）
  - 入れると、主キーが 1 列の表が無い図（どの表も複合主キーか主キーなし）で部品 04 の契約（`ISqlExecutor`・`IRepositoryCore`・`ISaveHookRegistry`・`IncludeKeys` など）だけが抜ける。図に依らず出るほかの部品の土台（`_05` のクエリ・`_07`・`_08`・`GraphSaveOrder`）がそれを参照し、生成の診断はエラー 0 のまま、QuickER 版単独・EF Core 版・インメモリのどれでもコンパイルできない
- Repository を作る表の判定は `HasSinglePrimaryKey`（テンプレートでは `repository_classes`）だけ。表ごとの「Repository をスキップ」の警告は既存のまま
- DI の登録のローカル関数（`AddRepository` / `AddClient`。`_06` の 4 か所・`_07`・`_08`）は、Repository が 0 件だと使われず CS8321 になるので、既存の判定 `repository_classes.size > 0` のときだけ出す（リモート契約の形は `remote_contracts && repository_classes.size > 0`＝0 件なら else 側の空のループへ進む）
- `_03` の EditModel 基底の一意制約の照会（`repository_classes.size > 0 || runtime_package_export` のゲート）は、呼ぶ側の `ValidateUniqueAsync` も主キーが 1 列の表にしか出ないので、そのまま残している
- ガード：`CodegenCaseTests` の行 `compile-composite-key-only-efcore` / `compile-composite-key-only-efcore-with-repositories`（コンパイル＋EF Core のモデルの主キーが実効順）・`compile-composite-key-only-all-surfaces`・`compile-composite-key-only-multitarget-split`・`compile-composite-key-only-packages`・`compile-keyless-only-all-surfaces`（どれもコンパイルのエラー 0・警告 0）。mutation 実証済み（04 のゲートを戻す・ローカル関数のゲートを 1 つずつ外す・主キーの期待を逆にする、の各回で赤）

## 生成コードのヘッダー

生成 `.g.cs` のヘッダー（版の行・内容ハッシュの行）は、生成サービスの 1 箇所で描画・連結の後に刻む。正規化・計算・照合は `GeneratedContentHash` だけが持つ。

- 刻む場所を描画の途中（テンプレートや連結ヘッダー）へ移すと、その後の加工でハッシュが本文と食い違い、生成直後のファイルが「手で編集された」と判定される
- 正規化は「先頭コメント塊の版の行・ハッシュの行を除く→ BOM と空白を全除去→ `}` `)` `]` 直前の `,` を除去」
  - 版の行を除くのは、版上げでハッシュを変えないため
  - 末尾カンマを除くのは、CSharpier が複数行の初期化子へカンマを付け外しするため（実測：ヘッダーを外した実フィクスチャを整形すると、空白除去後の差は末尾カンマ 2 文字だけ。ヘッダー付きなら CSharpier は 1 バイトも触らない）
- 先頭コメント塊の範囲（コメント行と空白だけの行が続く範囲）は `IsLeadingBlockLine` 1 つを `Normalize` と `FindRecordedHash` が共有する
  - 空白だけの行で塊を打ち切ると、ヘッダーに空行を挟んだだけでハッシュ行を見失い、`NotStamped`（確認なしの上書き）へ倒れる。安全側と逆
  - ガード：`GeneratedContentHashTests` の「先頭のコメント塊に空行が挟まっても…」。mutation 実証済み
- ランタイムパッケージ用ソースは生成サービスを通らない（`RuntimePackageSourceRenderer` がレンダラーを直接呼ぶ）ので刻印されない。刻む処理をレンダラー側へ移すと、7 本すべてが版上げのたびに変わる
- 上書き前チェックは `GeneratedFileWriter.FindModifiedFiles`（パス解決は書き出しと共有の `ResolveTargetPath`）。ハッシュ行のないファイルは判定しない（`NotStamped`＝旧版の生成物で全ファイル警告にしない）
- 入口は 3 つで、判定は全ファイル分を書き出しより前にまとめて行う（一部だけ書くと生成物どうしが食い違う）
  - GUI：`CSharpGenerationCommandService`（`ConfirmWarningDetails`）
  - CLI：`GenerationExecutor.WriteResult`（`--force` なしなら何も書かず `ModifiedFilesExitCode`＝2。generate / scaffold 共通）
  - MCP：`generate_csharp` の `force`（真偽値以外は拒否）
- モックプロジェクト生成は独自の上書き確認があるため照合しない
- ドリフト比較（`FixtureDriftHarness`）は版の値だけを正規化し、再生成は版の行以外に差分があるときだけ書く。`VersionPrefix` を上げてもチェックイン済み生成物は書き換わらない
- ガード：`GeneratedContentHashTests`（正規化 3 段）・`GeneratedHeaderStampTests`（全 5 出力モード・英日 `.g.md`・パッケージ用ソース 7 本）・`GeneratedFileWriterModifiedDetectionTests`・`GenerateOverwriteGuardTests`（CLI / MCP）・`CSharpGenerationCommandServiceTests` の「上書き確認」3 件。つなぎ込みを含め mutation 実証済み

## EditModel と Mapper の生成

### 新しい行の空の値

- 新しい行の EditModel は、値オブジェクトでない列の空文字列と空のバイト配列を未入力（null）として読む
- 規則の置き場は読み込みの式 1 つ（`CSharpGenerationModelBuilder.BuildEditModelLoadExpression`）。生成 `LoadColumns` に `entity.IsAdded && entity.X is "" ? null : entity.X`（バイナリは `is []`）を出す
  - Added の Entity だけが対象。保存済みの行の空の値は値として読む（`LoadSavedColumns` の読み戻しは Unchanged の Entity なので掛からない）
  - NULL 許容の列も同じに扱う（`OnEntityCreated` が入れた空文字列は NULL で保存される）
- **除外した無制限バイナリ列と行バージョン列のバイナリは除く**
  - 前者の空配列は、空で INSERT してから Stream で書く 2 段の書き込みのための値。未入力にすると NOT NULL の列が `IsRequiredWhenAdded` で止まる
  - 後者は Mapper が入力のあるときだけ書き戻すので、変える意味が無い
- 外すと、何も入力していない新しい行が NOT NULL の列の必須チェックを素通りして空の値で保存され、空欄の新しい行どうしが兄弟の重複と報告される（兄弟の検査は null を含む組を飛ばすので、null に読めば自然に消える）
- `ValidateUniqueAsync` は、NULL を許さない文字列・バイナリの列で入力の無いものも照合から外す（`BuildEditModelValidateUniqueMethod` の未入力の列＝値型に加えて Entity 側の型が `string` / `byte[]`）
  - Entity を初期化子から組むので、外すと初期値の空文字列を保存済みの行と照合する。値オブジェクトの初期化子は null なので要らない
- ガード：`EditModelBlankInputTests`（InMemoryFixture の `blank_codes`＝文字列の主キー・一意な NOT NULL の文字列・NULL 許容の文字列と、`blank_probes` の NOT NULL の `binary(8)`）・EditModel の表 `EditModelScenarioTests` の em-blank-required の行 `blank-required-new-row-empty-binary`・`BinaryColumnAdoRuntimeTests.NewEditModel_KeepsEmptyExcludedBlob_AndTwoStepWriteWorks`。mutation 実証済み

### Mapper の 4 分岐の Value

- `_03_EditModelsAndMappers.scriban` の `ApplyToEntity` にある行バージョン・計算列・採番列・除外バイナリの 4 分岐は、値型のとき `.Value` で取り出す
  - EditModel の確定値（値型なら常に `Nullable<T>`）を、Entity（NOT NULL なら非 NULL 許容の値型）へ書き戻すため、`is not null` の内側でも素の代入は CS0266 になる
- 判定の正本は `CSharpMappingPropertyPair.NullableValueNeedsUnwrap`（`EditModelIsNullable && !IsReferenceType && 型名相違`）。**参照型へ広げない**（`string` / `byte[]` / VO が `.Value` を持たず、コンパイル不能になる）
- 通常列は同条件で `Required(...)` を通すので、このフラグを見ない（必須検証を通す列と、未入力が正常な列で意味論が違う）
- 行バージョン列と除外バイナリ列は実装上つねに `byte[]` なので、この欠陥が表面化するのは値型の計算列・採番列だけ
- ガード：`CodegenCaseTests` の行 `compile-*`（共通図 `CompileDiagrams.FullCoverage` の `customers.valid_from`＝NOT NULL の値型計算列と `seq_no`＝主キーでない NOT NULL の値型採番列を全ケースの実コンパイルへ通す）・`CodegenCaseTests` の行 `exec-computed-column-not-null-value-type-mapper-unwraps-nullable`（`.Value` の 1 件）。mutation 実証済み

### 配列列と inet の列

- 配列列（`CSharpTypeInfo.IsArray`）と PostgreSQL の `inet` の列（`CSharpTypeInfo.IsTextInputUnsupported`）は、EditModel・Mapper・値オブジェクトへ載せない
- 判定の単一の述語は `CSharpTypeInfo.IsExcludedFromEditModel`。EditModel の列・Mapper・VO・EditModel の重複検証・生成時 Info の 5 箇所が共有する（写しを作らない）
- 理由
  - EditModel は 1 列＝1 テキスト入力の投影で、配列を表す記法が無い。テンプレートの入力変換は「参照型かつ `byte[]` でない型」を最後の `else`（`プロパティ = normalized`＝string 代入）へ落とすため CS0029 になる
    - `GenerateEditModels` は既定 true なので、型を写すだけだと既定の構成でコンパイルが通らなくなる
    - inet は参照型の `IPAddress` を EditModel が入力文字列のまま代入して CS0029 になる
  - 値オブジェクトは `ValueObjectBase<TSelf, TValue>` の等値が `EqualityComparer<T>.Default` で、配列では参照比較になる。同じ内容の配列を持つ VO が等しくならず、静かに壊れる
- 外した列は生成時 Info で名指しする：配列列は `CodeGen_Info_ArrayColumnsNotEditable`、inet は同じ形の別キー `CodeGen_Info_TextInputUnsupportedColumns`
- NOT NULL の配列列は、Entity の初期化子とインメモリのシード値を空配列にする（EditModel に載らない列が null のまま INSERT へ行かないため）
  - NOT NULL の inet は、Entity を `= null!`、インメモリのシード値を決定的なアドレスにする
- Entity のプロパティ・EF Core・インメモリ・リモート転送では通常どおり扱う。`HasSameValues` / `GetValueHashCode` は `StructuralComparisons` が配列全般を構造比較するので変更不要
- ガード：`CodegenCaseTests` の行 `diag-postgresql-array-*`（Info の名指し）・`parsed-postgresql-array-columns-have-no-token-and-reverse-skips`（C# リバースがトークンの無い列として警告つきで飛ばす）・`code-postgresql-array-columns-*`・`CodegenCaseTests` の行 `code-postgresql-network-types-inet-excluded-from-editmodel`・`CodegenCaseTests` の行 `compile-postgresql-array-*`（EditModel × VO の 4 通り＋インメモリ併用 2 通り＋UNIQUE 制約 2 通りの実コンパイル）/ `compile-postgresql-network-types-*`（EditModel × VO × インメモリの 6 通りの実コンパイルでエラー・警告 0）。mutation 実証済み

## インメモリのシード値

インメモリのシード値（`CSharpGenerationModelBuilder.SampleData.cs`）は、列の型でなく役割から作る。

- 外部キーの子側の列（リレーションの列ペアの子。参照の連鎖は根まで辿る）は、参照先の列の式を子の型で同じ `index` で作る。子の i 行目は親の i 行目を指す
- 主キー・一意制約の構成列は行ごとに違う値にする
  - 数値は `index`。文字列は生成時に長さへ収め、**末尾の `index` を残して列名の側を切る**（左から切ると 3 行が同じ値になる）
- 3 種類の値を作れない列（NOT NULL の `bool`＝2、既定値へ倒す未知の型と配列＝1）だけでできた主キー・一意制約を持つエンティティは、シード行数を減らす
  - それを参照する子も、親に実在する行だけを指すよう減らす。不動点（行数は減る一方なので循環でも止まる）
- 主キーが 1 列でない表（Repository を作らない表。判定は Repository と同じ `HasSinglePrimaryKey`）はシードしない（0 行）
  - ストアは行を単一主キーで引く（`EntitySaveMetadata.For` が複合主キー・主キーなしで投げる）ので入れられない
  - それを直接・間接に参照する表も、同じ不動点で 0 行になる（外部キーの NULL 許容に依らない）。ストアは外部キーを検査しないので、子だけを入れると実在しない親を指したまま残る
  - 子の行数の上限は、参照の連鎖の根でなく直接の親から取る（値の式は今どおり根から作る）。直接の親の行数はその親で抑えてあるので連鎖全体が整合する。根だけで抑えると、外部キーの列でできた複合主キーの中間表（0 行）を素通りする
  - テンプレートは行数 0 の表の `Seed{Entity}` を出さない（死んだコードを出さないためで、振る舞いは変えない）
- 型だけから作ると、既定の `seedSampleData: true` で一意制約が重複したストアができ、以後その行の更新・保存が必ず失敗する（`Put` / `Seed` は一意制約を検査しない。docs 明記の受容）
- ガード：`CodegenCaseTests` の行 `probe-inmemory-seed-mysql-constraints` / `probe-inmemory-seed-mysql-constraints-vo`（MySQL の図を生成→実コンパイル→シード→`SeededSampleDataInvariants` で主キー・一意制約・外部キーを反射で検査→全行を `UpdateAsync` で保存し直す。VO の有無の 2 通り）・`InMemorySampleSeedExecutionTests` の「既存の全フィクスチャのシーダーも…」（テストアセンブリ内の全 `InMemorySampleData` に同じ検査）・`CodegenCaseTests` の行 `probe-inmemory-seed-row-count-fixed-point-cycles`（シード行数の不動点は自己参照と相互参照でも止まる）・`CodegenCaseTests` の行 `probe-inmemory-seed-composite-key`（複合主キーの中間表・それを NOT NULL / NULL 許容の複合外部キーで参照する子・孫・主キーの無い表は 0 行、主キーが 1 列の表は 3 行で読み書きできる）・網羅の配列の主の表のキーが複合でインメモリを生成する行（探針 InMemorySeed の期待はすべての表が 0 行）。mutation 実証済み

## GuidKey 値オブジェクトの長さ検証

- GuidKey 値オブジェクトの長さ検証は、素通しした MaxLength が駆動する
  - GuidKey のとき MaxLength を無条件に null 化すると、「長さ検証は VO が担うから Entity へ `[MaxLength]` を出さない」という内部契約が破れ、検証がスタック全体から消える（宣言幅超過が INSERT の切り詰めエラーで初めて露見する）
  - 素通しにより `_01_ValueObjects.scriban` の既存 emission（`vo.max_length` 駆動）がそのまま効く
- 列幅が正の値かつ 36 未満は、生成時 Warning（`CodeGen_Warning_GuidKeyColumnShorterThanGuid`）で名指しして生成は続行する。引数なし `Create()` の採番 36 文字が必ず検証例外になるため
- **Error にしない**
  - `UseGuidKeyForStringPrimaryKey` は図の全 string PK に効くグローバル指定。GUID を意図しない短い string PK（コード表など）が混在する図で、機能ごと使えなくなる
  - 手動キー供給の運用は正当で、rowversion 2 本の Error の前例とは「生成物が使用不能」の条件が違う
- ガード：`CodegenCaseTests` の行 `probe-guidkey-create-generates-unique-guid` ＋生成テスト 3 本。mutation 実証済み

## 生成型名・メンバー名のぶつかり

### 型名の名簿

- 射影 DTO の型名が生成コードの型名と衝突する定義を生成前に断る入力は 3 つ
  - `GeneratedFixedTypeNames`（固定の名簿）：`Runtime`（ランタイム 7 パッケージが宣言する型）・`FixedGenerated`（テーブルや列に依らない生成物の型）・`External`（生成コードが単純名で参照するソース外の型。属性は `Attribute` を省いた綴りも）
  - `GeneratedTypeNames.Collect`（図から決まる名前：エンティティ・EditModel・Mapper・Repository 系・値オブジェクト）。オプションに依らず出し得る名前をすべて返す（GUI・MCP がオプションを知らなくても判定が割れない）
- 名簿が実際の出力より狭いと、その名前の DTO は診断ゼロでコンパイルできない出力になる（`Task` のように単純名で使う型を隠すと、無関係なメソッドまで壊れる）
- `GeneratedTypeNameDriftTests` が、名簿と実際の生成出力の一致を実コンパイルの結果で表明する。テンプレートが新しい型を宣言・参照した瞬間に落ちて、名簿の更新を強制する
  1. ランタイムのパッケージ用ソース 7 本が宣言する型名は `Runtime` と完全一致する
  2. コミット済みの全フィクスチャが宣言する型名は、Runtime・FixedGenerated・テーブルと列から派生する名前（射影 DTO を除く）に収まり、FixedGenerated の名前はどれかに実在する
  3. 全フィクスチャの意味モデルで単純名として参照されたソース外の型名は External に収まり、載せた名前は実際に参照されている
- mutation 実証済み

### 型名のぶつかりを生成時に止める

判定の本体は `GeneratedTypeNames.Analyze` の 1 つだけ。名簿は射影 DTO の検査と同じ（`ForTable`＝エンティティ・EditModel・Mapper・Repository 系の 12 名＋列ごとの値オブジェクトの名前、`GeneratedFixedTypeNames.IsReserved`）。サービスの `ValidateGeneratedTypeNames` は診断の文面を組み立てるだけ。

1. 同じ綴りの型名を 2 つ以上の出どころが作る組はエラー
   - 出どころはテーブルごとに 1 つ。値オブジェクトの名前は作る列が何本あっても 1 つ（同じ綴りの列は型を共有する設計）
   - 出どころの組が同じ名前は 1 件にまとめる（`customer` / `customers` は名前を並べた 1 件）
   - `EnsureSuffix` は名前がすでに接尾辞で終わっていれば付け足さない。表 `order_edit_model` は `order` の EditModel と同じ `OrderEditModel` になり、`i_order`・`in_memory_order`・`ef_core_order`・`journaling_order` は `order` の Repository の名前を重ねる
2. 固定の型名と同じ名前はエラー（表 `raw_sql` の `RawSqlMapper`・列 `sql_parameter` の `SqlParameterValue`）
3. リモートのルート名（`ToRemoteRouteName`＝エンティティのクラス名から `Entity` を外したもの）が大文字小文字だけ違う表（`order_item` と `orderitem`）はエラー（ASP.NET のルーティングが大小を区別しない）
4. エラーにならずに残る型名のうち、大文字小文字だけ違う綴りの組は Warning（`CodeGen_Warning_GeneratedTypeNamesDifferOnlyInCase`。止めない）。エラーにした名前と、ルート名でエラーにした表の名前は除く

- 判定は構成と名前空間に依らない。出し得る名前をすべて見るので、オプションや出力の形を切り替えても、生成できていた図が壊れない
  - 代わりに、分割出力の `ef_core_order`・Mapper なしの `raw_sql` のように、その構成ならコンパイルできる組も断る
- **型名は Ordinal で比べ、大小を区別しない比較はルート名にだけ掛ける**
  - 型名をほかに大小を区別せずに読む後段は無い。API リファレンスの見出しはエンティティ名だけで、文書の中のリンクは無い。ファイル名は分類と層の単位。ランタイムの大小無視の辞書は列名とプロパティ名。リモートの版の対応表はエンティティの型名を Ordinal で照合する
  - 大小を区別せずに比べると、表ごとに綴りの違う列（`user_id` と `userid`）の値オブジェクトを理由なく断る
  - ルート名はリモートを生成しない構成でも止める（後からリモートを有効にしても図が壊れない）
- 名簿 `ForTable` は生成器の命名規則の写しなので、`GeneratedTypeNameDriftTests` が実際の出力と一致し続けることを固定する
- ガード：`GeneratedTypeNameCollisionTests`・`CodegenCaseTests` の行 `diag-type-name-collision-singular-plural`。mutation 実証済み

### エンティティのメンバー名の大文字小文字

大文字小文字だけ違うメンバー名も、構成に依らず生成時のエラーで止める。

- 理由：エンティティを読み書きする JSON の設定（`RemoteJson.Options`・SQL Server の FOR JSON の設定）がどちらも `PropertyNameCaseInsensitive` で、大小だけ違う 2 つのプロパティは、コンパイルが通っても型の情報を組み立てる時点で `The JSON property name ... collides with another property` になる
- 列どうし：`ColumnPropertyNameCollisions.Find`（プロパティ名を OrdinalIgnoreCase でまとめる）が判定の本体
  - 生成前の検証 `ValidateColumnPropertyNameUniqueness` がエラーにする（綴りまで同じなら従来の文面、大小だけ違えば `CodeGen_Error_ColumnPropertyNamesDifferOnlyInCase`）
  - `GeneratedTypeNames.Analyze` が、その列を型名の大小違いの警告の出どころから外す（外さないと、エラーと「生成物はこのままで動く」という警告が同じ列に重なる）
- 列・ナビゲーション・基底の公開プロパティ：ビルダーの `ReportCaseOnlyMemberNames` が、綴りの違う組だけを `CodeGen_Error_GeneratedMemberNamesDifferOnlyInCase` で止める。綴りまで同じものは従来の Ordinal の検査の担当
  - 基底の公開プロパティの名簿は `GeneratedFixedMemberNames.EntityJsonVisibleBase`（`RowState`・`IsAdded`・`IsUpdated`・`IsRemoved`・`HasChanges`・`DisplayName`）
- EditModel は JSON で読み書きしないので Ordinal のまま
- ガード：`GeneratedMemberNameCaseTests`（大小だけ違う列・列とナビゲーション・基底の 3 名 × リモートあり・なしでエラー、同じ表の大小違いの列は警告を重ねない、EditModel だけの大小違いは診断なしでコンパイル、診断の無い図は実際の Kestrel のリモートで全エンティティを書いて読み戻せる。型名側の `sql_parameter` と `sqlparameter` の行も同じ）・`GeneratedTypeNameCollisionTests` の「エラーになる組は重ねて警告しない」・`GeneratedFixedMemberDriftTests`（基底の名簿の一致）・`NameFuzzCompilationTests` の SqlServerFull の実行時の段（`GeneratedRuntimeProbe`＝読み込んだ生成物で全エンティティを両方の JSON の設定で組み立て、エンドポイントを登録して、同じ HTTP メソッドで大小だけ違うパターンを探す。種に依らず `i_d`・`rowstate`・`case_route`・`caseroute` を置く）。mutation 実証済み

### ナビゲーション名と外部キーの重複

- `ResolveNavigationNames` は、2 つのテーブルの間の全リレーション（向きを問わない）ごとに、まず向きごとの規則（同じ（親表, 子表）に 2 本以上なら役割名）で名付ける
  - その名前が同じクラスで重なる（Ordinal。親側の名前は親表、子側の名前は子表のクラス。自己参照は子側を作らない＝`HasCollidingNavigations`）ときだけ、その全リレーションを役割名で名付け直す
  - 例：customers→profiles の 1 対 1 と profiles→customers の 1 対 1 は、customers に `Profile` を 2 つ作ってしまう
- **重ならない組の名前は変えない**。名前が変わるのは、従来メンバー名の重複で生成エラーになっていた図だけ
- 同じ親・子・列ペアの外部キーが 2 本以上（単一列の列ペア・多対多でない＝ナビゲーションを作るもの）は、役割名でも区別できない
  - 生成前の検証 `ValidateDuplicateForeignKeys` が、DDL と同じ実効名（`ConstraintNames.ResolveForeignKeyNames`）で両方の制約名と列を名指しする `CodeGen_Error_DuplicateForeignKey` で止める（ビルダーへ届かないので、メンバー名の重複のエラーは出ない）
- ガード：`NavigationNameCollisionTests`。mutation 実証済み

## 名前・型文字列の信頼境界（C# 生成側）

テーブル名・列名・制約名・クエリ名と `Column.DataType` は、GUI 自由入力・DB 取込・DBML / Excel 取込・MCP / AI 経由の任意文字列。Description と違い、出力側でエスケープされない箇所が層をまたいで散らばりやすい。
方針は「生成前診断（名前の改行・制御文字のみ Error）＋出力側は共通ヘルパーへ集約」。
SQL 側（動的 SQL・`--` コメント・型文字列・名前の入口検証・合成制約名のクォート）は `src/QuickER.Provider/CLAUDE.md`「SQL の信頼境界」を見る。ここは C# 生成側。

### C# リテラルのエスケープ

- 名前・説明を C# の文字列リテラルへ書くヘルパーは 3 種
  - `EscapeNameForCSharpString`（名前用。空白のみを空へ畳まない）
  - `EscapeForCSharpString`（説明用）
  - `EscapeForCSharpInterpolatedString`（`$"…"` のリテラル部。波括弧も二重化）
- 適用面：`[Table]` / `[Column]` / `[NavigationReference]` の 4 引数 / EF Core の `ToTable` / `HasColumnName` / `SyncTableDescriptor.TableName` / `Journal.RecordAsync` / インメモリのサンプル値式
  - 同期 SQL の識別子は `CSharpGenerationModelBuilder.Sync.QuoteIdentifier`（終端クォート二重化）を通す
- 改行の畳み込みは、`ReplaceLineEndings` が改行にする集合全部（CR / LF / NEL U+0085 / FF U+000C / U+2028 / U+2029）を対象にする（`FoldNewLines`）
  - C# 言語仕様の new-line 5 種に絞ると、描画後のレンダラーの `ReplaceLineEndings` が FF を実改行へ変え、説明 1 つで XmlDoc・リテラルが行をまたぐ
  - ガード：`NameTrustBoundaryGenerationTests` の FORM FEED テスト（Roslyn 実コンパイル＋診断＋ XmlDoc の XML エスケープ）

### 文字列リテラルへ書く規則の正本

- 生成コードの文字列リテラルへ文字を書く規則の正本は `GeneratedLiteralText`
  - 改行になる文字の集合（`IsLineBreak`）と、リテラルに生のまま書けない文字（C0 制御文字・DEL・改行になる文字・対になっていないサロゲート＝`MustEscape`）を 1 か所で持つ
  - 条件式の文字列（`RenderStringLiteral`）・生 SQL（`RenderSqlLiteral`＝逐語リテラルに書けない文字は通常のリテラルの断片へ回す）・図の名前と説明（`EscapeCSharpStringCore`。改行は空白へ畳む）・名前の検証（`IsControlCharacter`）がすべてここを通す。写すと 1 か所だけ取り残される
- 対になっていないサロゲートは、生成ファイル（UTF-8）で U+FFFD に置き換わり、比較の相手・SQL・列名が図と黙って食い違う。`\uXXXX` で書く
  - 名前の検証はサロゲートを断らない（エスケープで正しく出せるため）
- ガード：`QueryLiteralEscapeScanTests`（UTF-16 の全コード単位を 1 本の文字列にして、生成物を UTF-8 で書いて読み直したものとして構文解析し、リテラルのトークンの値が元の値と一致することと、生のままの制御文字・対になっていないサロゲートが無いことを見る。DSL・生 SQL・列名の 3 経路）・`QueryCodegenDiagnosticsTests` の「描画後に改行へ変わる文字の集合は、生成器の中で 1 か所にだけ書く」（ソースの走査）。mutation 実証済み

### XmlDoc の名前

- XmlDoc の定型文へ載る名前は、専用の `*XmlDoc` フィールドで XML エスケープする
  - `CSharpClassModel.TableNameXmlDoc` / `CSharpPropertyModel.ColumnNameXmlDoc` / `CSharpValueObjectModel.ColumnNameXmlDoc` / `CSharpEditModelClassModel.TableNameXmlDoc` / `CSharpSyncTableModel.TableNameXmlDoc`
  - テンプレートの `*_xml_doc` 参照サイトは 6 か所（`_01` VO・`_02` Entity とプロパティ・`_03` EditModel・`_07` インメモリシーダー・`_10` 同期記述子）。同期記述子の 1 か所だけは、説明が無いときのフォールバックでなく無条件文
- C# リテラル用のエスケープを XmlDoc へ流用すると、`<` が生のまま残って CS1570 になる。生のまま流すと、U+2028 で `///` 行を抜けて任意の C# を注入できる
- ガード：`NameTrustBoundaryGenerationTests`

### 名前の制御文字の生成前診断

- C# 生成側は `ValidateNameControlCharacters`（生成前診断 Error）で、テーブル名・列名・制約名の改行・制御文字を断る
- `IsControlCharacter` は Provider 側の判定（`SqlComment.ContainsControlCharacter`）と意図的に二重定義している。依存方向が Provider → CodeGen.CSharp のため。片方だけ変えない
- 説明は対象外（改行が正当）
- ガード：`NameTrustBoundaryGenerationTests`（Roslyn 実コンパイル＋診断）
