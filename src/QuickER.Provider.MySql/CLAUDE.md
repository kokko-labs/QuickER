# QuickER.Provider.MySql

このファイルの範囲は、MySQL 方言だけの規則（取込のカタログの読み方・型表記と型マッパー・同期スクリプトの書き方）。
方言横断の規則は `src/QuickER.Provider/CLAUDE.md` を読む。

## 取込

### 範囲外を参照する外部キー

- 範囲は接続先の DB。範囲外を参照する外部キーは `REFERENCED_TABLE_SCHEMA` を見て除外し、`ForeignKeyOutsideScope` で告げる（見ないと同名テーブルへ誤って結ぶ）。
  - 判定は SQL 側の `= DATABASE()` で行う（DB 名の照合順序が環境に依るので C# の文字列比較にしない）。
- 接続先 DB に無い表を参照する外部キー（`FOREIGN_KEY_CHECKS = 0` で作った・参照先を後から落とした）も `ForeignKeyOutsideScope`（Detail＝`スキーマ.テーブル`）で告げる。
  - 判定は「テーブル辞書に辞書の照合でも無い」。衝突して捨てた表への参照は辞書の照合では見つかるので告げず（`TableNameCollision` が告げる）、子が捨てた表なら告げない。
- ドットを含む表への参照は、外した表の集合を外部キーの読み込みへ渡して `ForeignKeyToExcludedTable` で告げる。
- 固定は `MySqlImportFidelityIntegrationTests` の `Import_ForeignKeyToMissingTable_IsExcludedWithWarning`／`Import_ForeignKeyToCollidedTable_IsNotReportedAsMissing`。mutation 実証済み。

### 表と制約の読み方

- `lower_case_table_names = 0`（Linux の既定・mysql:8.4 で確認）では `Dup` と `dup` が共存する。この値が 1 の環境では共存しないので、統合テストは値を見て skip する。
- 生き残る側を決める並びは `ORDER BY BINARY TABLE_NAME`（information_schema の既定照合順序は大文字小文字を区別しない）。
- 一意制約はインデックスと区別できないので、全一意インデックスを取り込む（プレフィックス・関数インデックスは除外）。
- 生成列は `GENERATION_EXPRESSION` が空でないことで計算列とする。`EXTRA` の語彙で判定しない（部分一致は式の既定値の `DEFAULT_GENERATED` を誤検出し、完全一致は `VIRTUAL GENERATED INVISIBLE` を取りこぼす）。
- 構成列はカタログが実名で返す（`REFERENCED_COLUMN_NAME`・`STATISTICS.COLUMN_NAME`＝実コンテナで確認）。
- パーティション親は `information_schema.PARTITIONS` の `PARTITION_NAME IS NOT NULL` で判定する（`TABLES.CREATE_OPTIONS` の `partitioned` 部分一致は将来のオプション追加に弱いので採らない）。
- 固定は `MySqlImportFidelityIntegrationTests.Import_ConstraintColumnsInOtherCase_AreImported`。

## 型表記

### 取込の表記の正規化

- `MySqlTypeNotation.Normalize` が正本。`decimal(12)` は `decimal(12,0)`、`datetime(0)`／`time(0)`／`timestamp(0)` は素の綴りで返る。
- 固定は `MySqlTypeNotationTests` の表・`MySqlTypeNotationIntegrationTests` の閉包・同期計画の行 `mysql-77-respell-datetime`。mutation 実証済み。

### 型カタログと型マッパー

- `TryFormat` の上限：char／binary 255・varchar 16,383（`utf8mb4` の文字数）・varbinary 65,532・小数秒の精度 6・decimal の精度 65 とスケール 30。
  - 行全体の上限 65,535 バイトは判定しない（ほかの列との合計で決まるので適用のときに MySQL が判定する。`id int` が並ぶだけで `varchar(16383)` も断られる＝実測）。
- 負のスケールは書き出さない（`ERROR 1064`）。長さを要する型の長さ無しは書き出さない（構文エラー）。固定長の長さ無しを `(max)` 扱いにすると `longtext` に化ける。
- 精度なしの Decimal は書き出さない。素の `decimal`／`numeric` は (10,0) と読む。
- パラメータの型解決では上限の判定だけを外す（`TryFormatParameterType`）。
- 型マッパーは `tinytext` を `string` へ写す（フォールバックの型にしない）。
- 固定は `MySqlTypeCatalogTests`（`TryFormat_Respects*Limits`・`TryFormatParameterType_IgnoresColumnLimits`）・`MySqlTypeLimitsIntegrationTests`・`MySqlCSharpTypeMapperTests.Map_TinyText_IsStringWithoutFallback`・`MySqlColumnClrTypeIntegrationTests.Map_TinyTextAndTinyBlob_MatchDriverClrTypes`。mutation 実証済み。

## 識別子と説明の文字

- MySQL は BMP の外の文字（絵文字など）を断らずに 1 文字ずつ `?` へ置き換えて保存する（サロゲート対は 1 つの `?`）。宣言は `IdentifierLengthLimit.RepresentsSupplementaryCharacters` が false。
  - 識別子は DDL・同期の入口で断る（`MySqlIdentifierLengthLimitIntegrationTests.SupplementaryCharacter_IsReplacedWithQuestionMark`）。
  - 説明は差分が保存する形（`StoredText`）で比べ、計画と DDL が名指しする（`src/QuickER.Provider/CLAUDE.md`「説明の空欄と表せない文字」）。
- 表の名前はファイル名での 251 バイトの上限も持つ（`AcceptsTableName`）。
- 列の追加（`BuildColumnDefinition`）は COMMENT を書かない（表の COMMENT と `MySqlIdentifier.ColumnCommentClause` は説明の空欄の判定 `DescriptionText` を通す）。説明は説明の同期の項目（SetColumnDescription＝ライブの再構成の MODIFY）だけが書く。固定は実 MySQL の行 `realdb-mysql-add-column-writes-description-only-through-selected-item`。

## 同期スクリプト

### 実行時に読んだ値を動的 SQL へ埋める

3 つの面にそれぞれ別の規則が要る。

- 逆引きした制約名 `@fk` は `CONCAT` のバッククォートへ入るので、SQL 側で `` REPLACE(@fk, '`', '``') `` を通す（生成時の `Escape` では届かない。`@pk` は存在判定にしか使わず埋めない）。
- `information_schema` を引くスキーマは操作対象と一致させる（`MySqlIdentifier.SchemaNameOnly` が非 null ならその名前・null なら `DATABASE()`＝`SchemaPredicate`）。`DATABASE()` 固定だと、修飾名 `other.t` の説明・列順の同期がカレント DB の同名テーブルの定義を焼き付ける。
- 実行器 `MySqlSchemaSyncExecutor` はスクリプトの前にセッションの `sql_mode` から `NO_BACKSLASH_ESCAPES` を外す（`EscapeStringLiteral` 全体がバックスラッシュエスケープ前提。外さないと説明の `\` が無言で二重化され、`'` を含む生成列式は構文エラーで部分適用になる）。
- 動的 SQL の名前は `MySqlIdentifier.QuoteForDynamicSql`（`'` に加えて `\` も二重化）を通す。主キーの解除は information_schema の逆引き＋プリペアド文（素の `DROP PRIMARY KEY` は PK が無いとエラー）。

### ライブの再構成

- 説明・列順の同期と列の変更は、列の定義をカタログから組み直す（`AppendLiveColumnDefinitionLookup`）。
- 「列の不在」と「再構成の失敗」を `@exists`／`@def` の 2 変数で分ける。`@def` の NULL だけで `DO 0` へ倒すと、列順の同期で移動列の 1 本が畳まれても中途半端な列順のまま成功を報告する。
  - 在るのに組めないときは PREPARE が落ちる文を組み立てて必ず失敗させる（`SIGNAL` はプリペアド文で使えない）。
  - 列の不在は、説明・列順の同期では `DO 0`、列の変更では失敗（`AppendModifyColumnStatement` の `missingColumnFails`）。
- 列の変更（`MODIFY`）は型（`typeOverride`）と NULL 許容（`nullableOverride`）だけを図の値へ差し替え、DEFAULT・AUTO_INCREMENT・照合順序と文字集合・ON UPDATE・INVISIBLE・SRID・生成列の式と種別・COMMENT を保つ。
  - 照合順序は新しい型が文字列型のときだけ書く（`IsStringType`。JSON は `COLLATE` を断り、数値型は黙って無視する＝実測）。
  - 保った属性が新しい型に合わないと失敗する（暗黙コミットで前の文は残る）。合わない既定値はエラー 1067 なので、計画が型の種類の変更を警告する。
  - 生成列は式と種別（`VIRTUAL`／`STORED`）を書き直すので、生成列の型の変更を選べる（式を書かない MODIFY なら `STORED` は普通の列になり、`VIRTUAL` は 3106 で断られる）。
- 列順の同期はネイティブの `ALTER TABLE ... MODIFY ... AFTER`（`SyncPlan.Reorders`／`TableReorderPlan`・最長増加部分列を不動として移動列を最小化）。
- 固定は `MySqlColumnAttributeSyncIntegrationTests` の `AlterColumn_ChangesOnlyTypeAndNullability`／`AlterColumn_MissingColumn_Fails` と実 MySQL の行（`realdb-mysql-generated-column-alter-keeps-expression`・`realdb-mysql-column-type-kind-change-incompatible-default-warns-and-fails`・`realdb-mysql-computed-column-type-change-from-diff-keeps-expression-and-kind`・`realdb-mysql-computed-fk-column-type-change-rebuilds-fk`）。mutation 実証済み。

### 外部キーと索引

- DDL は暗黙コミットされる（`TransactionalDdl` が偽）。
- 外部キーは子列を先頭に持つ索引を要する（`ForeignKeyRequiresLeadingIndex`）。削除する一意制約・旧主キーの索引に乗る外部キーは外して付け直す（外さないと `DROP INDEX` がエラー 1553）。
- 外部キーの両端の型の変更は拒否される（`AlterColumnRequiresForeignKeyRebuild`）。同じ文字列型の長さだけの違いは許す（許さないと 255 バイトの境界を越える `MODIFY` がエラー 1832）。
- 参照する列の並びは参照先のキーの並びと一致させる（`ReferencedKeyRequiresSameColumnOrder`）。
- 外部キーの削除は、MySQL が外部キーのために作った同じ名前の索引が残っていれば続けて外す（`MySqlSyncScriptBuilder.AppendDropForeignKeyIndex`＝一意でなく、ほかの外部キーが先頭の列に使っていない索引だけ）。
  - 同じ列の外部キー 2 本は自動索引を共有し、名前を後に張ったほうへ付け替えるので NOT EXISTS の条件が要る。残すと同じ名前の外部キーの付け直しがエラー 1061 で外部キーが消えたまま残る。
  - 手で作った同じ名前の索引も区別できず外れる（docs に明記）。
  - 同じ子表の外部キーを続けて外すときは、先に外した外部キーの名前の索引を、外部キーを外すセクションの終わりにもう一度確かめる（`MySqlSyncScriptBuilder.AppendSectionEnd`）。
- 固定は `MySqlSyncScriptBuilderTests`（`DropForeignKey_RechecksLeftoverIndexAfterLaterDropOnSameTable` ほか）と実 MySQL の行（`realdb-mysql-fk-two-on-same-columns-reorder-referenced-pk-rebuilds-both`・`realdb-mysql-fk-drop-keeps-index-used-by-other-fk`・`realdb-mysql-uq-drop-supporting-fk-keeps-fk`・`realdb-mysql-fk-string-length-change-one-end-keeps-fk`）。mutation 実証済み。

### SET DEFAULT

- InnoDB は `SET DEFAULT` を宣言として保存するが効かせない（親を消すと外部キーエラー＝`NO ACTION` と同じ・実 MySQL で確認）。
- DDL・同期は `MySqlReferentialAction` で句を省いて注意コメントを出し、比較は ON DELETE・ON UPDATE とも `NO ACTION` へ畳む（`SupportsSetDefaultAction` が偽）。
- 固定は `MySqlReferentialActionIntegrationTests.SetDefault_DdlOmitsClauseWithNoteAndApplies` と実 MySQL の行 `realdb-mysql-set-default-is-omitted-and-leaves-no-fk-diff`。
