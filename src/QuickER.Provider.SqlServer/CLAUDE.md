# QuickER.Provider.SqlServer

このファイルの範囲は、SQL Server 方言だけの規則（取込のカタログの読み方・型表記・同期スクリプトの書き方）。
方言横断の規則は `src/QuickER.Provider/CLAUDE.md` を読む。

## 取込

### スキーマの畳み方と既定スキーマ

- 取込の範囲は全スキーマで、`dbo` だけを素の名前へ畳む（`DisplayTableName`）。
- スキーマ修飾の無い表名は接続ユーザーの既定スキーマを指す（`CREATE TABLE [Widget]` と同じ。説明の文も `SqlServerExtendedProperty` が `SCHEMA_NAME()` で同じ表を指す＝`SqlServerDefaultSchemaDescriptionIntegrationTests`）。
- 既知の残件：既定スキーマが `dbo` でないユーザーが修飾の無い図を使うと、`Widget` と `app.Widget` が別の表になる。取込を既定スキーマで畳むと、そうしたユーザーの保存済みの図が全表の追加＋削除に化けるので見送る（`docs/database.md` の説明の同期の節で案内）。
- ドットを含む名前で外すのは `dbo` のテーブル名の `.` とスキーマ名の `.` だけ。`dbo` 以外のスキーマのテーブル名の `.` は読み戻せるので取り込む（`a.b.t`＝スキーマ `a` の `b.t`）（判定は `src/QuickER.Provider/CLAUDE.md`「ドットを含むテーブル名」）。

### 大文字小文字だけ違うテーブル名

- 大文字小文字を区別する照合順序（例 `Latin1_General_100_CS_AS`）の DB で同名の表が共存する。キーは `[schema].[name]` の複合で、比較はキー全体・警告へは表示名を出す。
- 生き残る側を決める並びは `COLLATE Latin1_General_BIN2` で照合順序に依らなくする。

### パーティション・無効化された制約・テンポラル表

- 計算列は `sys.computed_columns` に行があるかで判定する（式もここから読む）。テンポラル表の期間列も計算列として扱う（下）。
- パーティション親：ヒープかクラスタ化インデックス（`index_id IN (0, 1)`）のデータ領域がパーティション構成（`sys.partition_schemes`）なら `PartitionDefinitionLost`。
- 無効化された FK（`is_disabled = 1`＝`NOCHECK CONSTRAINT`）は取り込まず `DisabledConstraintExcluded` で告げる。告知は専用の `DisabledForeignKeysSql` で拾い、除外の実効フィルタは既存クエリの `is_disabled = 0` に残す。
  - `is_not_trusted = 1` かつ `is_disabled = 0` は取り込む（`WITH NOCHECK` で追加されても以後の書き込みには強制が効く）。
  - PK・UNIQUE は無効化する構文が無いので対象は FK だけ。
- テンポラル履歴表（`temporal_type = 1`）は `TemporalHistoryTableExcluded`（Detail＝本表名）で除外し、本表の期間列（`generated_always_type IN (1, 2)`）は計算列と同じ `Column.IsComputed` へ相乗りさせる（Detail は `GENERATED ALWAYS AS ROW START/END`）。
  - `HIDDEN` の期間列も `INFORMATION_SCHEMA`／`sys.columns` には現れ、生成コードの SELECT は明示列挙なので読める。
  - `temporal_type`／`generated_always_type` は SQL Server 2016 で追加された列で、docs の対応 DBMS 表の「2016 以降」が前提。`ledger_type`（2022）は参照しない（台帳テーブルの履歴表は未対応）。
- 固定は `SqlServerImportFidelityIntegrationTests`。mutation 実証済み。

## 型表記

### 取込の表記の正規化

- `SqlServerTypeNotation.Normalize` が正本（規則は `src/QuickER.Provider/CLAUDE.md`「型表記の比較は取込の表記へ寄せる」）。`numeric(18,0)` は `numeric(18)` で返る。
- `float(1)`〜`float(24)` は `real`、`float(25)`〜`float(53)` は `float` へ寄せる（DB は n を覚えない）。型マッパー・型カタログも同じ判定（`SqlServerTypeNotation.IsSinglePrecisionFloat`）で単精度（C# は `float`・正規型 Float32）とする。
  - `double` にすると速い読み取りの `GetDouble` も EF Core も `InvalidCastException`（`SqlServerDialectTypeRealDbTests`）。
- 取込は `DATETIME_PRECISION` を読み、既定の 7 は括弧を省く（読まないと `datetime2(3)` が `datetime2` で入り、同期が精度を黙って 7 へ変える）。
- 固定は `SqlServerTypeNotationIntegrationTests`・`SqlServerDdlRoundTripIntegrationTests`（起点 DDL の表記との照合）。

### TryFormat の上限と書き出さない型

- 上限：nvarchar／nchar 4000・varchar／char／binary／varbinary 8000・decimal の精度 38 とスケール ≦ 精度・時刻系の精度 7。固定長の max（`nchar(max)` は存在しない表記）は書き出さない。
- 負のスケールは書き出さない（`Msg 1002`）。長さを要する型の長さ無しは書き出さない（黙って 1 文字の列になる）。
- 精度なしの Decimal は書き出さない。素の `decimal`／`numeric` は (18,0) と読む（精度なしと読むと C# リバースが偽の警告を出し、切替も意味の違う型になる。素の decimal の列は `[DbColumnMeta("decimal(18,0)", NativeType = "decimal")]` と生成時 Info の対象）。
- パラメータの型解決でも上限の判定を外さない（固定長の max を書かない規則と一体）。
- 固定は `DialectTypeTableTests` の上限の行（`format-sqlserver-limit-*`）と型トークンの書き出しの行（`format-token-*`）。

## 同期スクリプト

### 実行時にカタログから読む名前

- 旧主キー名・名前の分からない外部キー名は、`QUOTENAME` を通した文を `nvarchar(max)` の変数に組み立てて `sp_executesql` で流す。
  - `EXEC( … )` の中には関数呼び出しを書けない。`SET @pk = QUOTENAME(@pk)` は `]` の二重化で最大 258 文字になり、sysname へ戻すと切り詰められる。
  - 括弧で挟むだけの連結（`'[' + @pk + ']'`）へ戻すと `]` を含む正当な名前で失敗する。
- 逆引きは `OBJECT_ID(N'<QuoteForDynamicSql(表名)>')` で ALTER TABLE と同じ 1 つの表に絞る（表名だけで引くと別スキーマの同名テーブルの制約名を拾う）。
- 固定は `SqlServerSyncScriptBuilderTests` の `RuntimeConstraintName_IsAlwaysQuotenamed`／`AlterPrimaryKey_Lookup_IsScopedByObjectId`／`DropForeignKey_Lookup_IsScopedByObjectId` と `SqlServerRuntimeConstraintNameIntegrationTests`。mutation 実証済み。

### 列の変更と列の削除

- 列の変更は、新しい型が文字列型なら `sys.columns.collation_name` を読んで `COLLATE` を書き添える（書かないと DB の既定へ戻る）。
- 型の表記が変わるときは `sys.default_constraints` から制約名と定義を読み、外して `ALTER COLUMN` してから同じ名前・同じ定義で付け直す（残すと 5074）。NULL 許容だけの変更は触らない。
  - 名前は `QUOTENAME`、文は `nvarchar(max)` の変数＋`sp_executesql`、表は `OBJECT_ID` で絞る。
  - 定義はカタログが SQL の式の形で持つので、文字列リテラルへ埋めずに変数のまま連結する（二重化すると式が変わる）。
  - SQL Server は付け直しで既定値を確かめない（`('abc')` を int の列へ付け直しても通り、既定値を使う追加で初めて失敗する＝実測）。
- 列の削除でも、列の DEFAULT 制約を同じ逆引き（`SqlServerSyncScriptBuilder.AppendDropDefaultConstraint`＝列の変更と共有）で先に外す（残すと 5074。付け直さない）。CHECK 制約・利用者の索引は外さず、残っていれば削除は断られてトランザクションごと戻る（docs の既知の限界）。
- `AlterColumnRequiresKeyConstraintRebuild`：列の変更が一意制約・主キーの構成列に触れると Msg 5074 になるので、プランナーが同じ構成のまま外して付け直す（照合キーは `src/QuickER.Provider/CLAUDE.md`「一意制約の照合キーと付け直し」）。
- `AlterColumnRequiresForeignKeyRebuild`：FK に関わる列の変更は Msg 5074 になるので、プランナーが外部キーを外して付け直す。
- 固定は `SqlServerSyncScriptBuilderTests` と実 SQL Server の期待の行（`realdb-column-alter-keeps-collation`・`realdb-column-alter-readds-default-varchar-to-nvarchar`／`realdb-column-alter-readds-default-int-to-bigint`〔`]` と `'` を含む制約名・`'` を含む定義〕・`realdb-column-alter-incompatible-default-commits-insert-fails`・`realdb-column-type-kind-change-with-default-warns`・`realdb-column-drop-with-default-constraint`／`realdb-column-drop-without-default-constraint`・`realdb-script-drop-column-default-with-quoted-names`）。mutation 実証済み。

### 外部キーの事実

- 外部キーは参照する列の並びが参照先のキーの並びと一致しないと作れない（Msg 1776・`ReferencedKeyRequiresSameColumnOrder`）。
- 外部キーの両端は文字列の長さも一致が要る（1753）。長さだけ違う外部キーは DDL が `ForeignKeyStatementNotes` で注記する。
- SET DEFAULT は子の列に DEFAULT 制約が無いと作れない（1762）。NOT NULL の子の列への SET DEFAULT は計画が警告し、実行は止めない。固定は実 SQL Server の行 `realdb-fk-set-default-on-not-null-warns-without-default-fails`／`realdb-fk-set-default-on-not-null-warns-with-default-commits`。
