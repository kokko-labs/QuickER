# QuickER.Provider.Sqlite

このファイルの範囲は、SQLite 方言だけの規則（取込の PRAGMA の読み方・型の扱い・再構築の同期スクリプトと実行器）。
方言横断の規則は `src/QuickER.Provider/CLAUDE.md` を読む（再構築の合成の規則は同じファイルの「テーブル再構築の合成」）。

## 取込

### 表の判別と PRAGMA

- FTS5／R*Tree の仮想テーブル本体と付属表（シャドウテーブル）は `sqlite_master` では通常テーブルと同じ `type='table'` で現れる。`PRAGMA table_list`（SQLite 3.37+）の種別で判別し、`virtual` は `VirtualTableExcluded` 警告つきで、`shadow` は無言で除外する（付属表の警告は本体 1 件に代表させる）。付属表の判定を名前の推測でやらない。
- PRAGMA の括弧内は `SqliteIdentifier.QuoteSimple`（単一名のクォート）を使う。`Quote` はドットをスキーマ修飾として分割するので、ドットを含む表名で PRAGMA が構文エラーになり取込全体が失敗する。
  - ドットを含む表は PRAGMA を読む前に外す（外した表の一覧へ入れて `ForeignKeyToExcludedTable` の経路に乗る）ので、`QuoteSimple` は二重の守り。DDL・同期スクリプト側の `Quote` は分割が正しいので据え置く。
- 生成列は `PRAGMA table_xinfo` の hidden 2・3 で計算列とする（`table_info` は生成列を 1 行も返さない）。式は取れないので Detail は空。
- 再構築で失う属性を告げるため、`sqlite_master` の `type='table'` の `sql` を `SchemaImportResult.TableCreateSql` として持ち帰る（使い方は `src/QuickER.Provider/CLAUDE.md`「テーブル再構築の合成」）。

### 外部キーと名前の照合

- 参照先が無い外部キーは `ForeignKeyOutsideScope` で告げる（複合 FK は `id` 単位で 1 件）。
- SQLite は ASCII の英字しか畳まないので `Äpfel` と `äpfel` が共存する。表の列挙は `ORDER BY name`（BINARY）。
- `foreign_key_list` の参照先は `REFERENCES` に書いた綴りのまま返る（`REFERENCES ORDERS` は表 `orders`）。外部キーの親と索引の表は、序数一致でなく SQLite と同じ照合（ASCII だけ畳む）で引く（`SqliteSchemaImporter.TryGetTable`）。
  - 序数一致にすると正当な外部キーを落とし、辞書の照合のままだと捨てた表への参照が残った表へ結ばれる。
- 構成列はカタログが実名で返す（`index_info.name`・`foreign_key_list.from`）。外部キーの親の列（`foreign_key_list.to`）だけは REFERENCES の綴りで返るので、`SqliteSchemaImporter.ResolveReferencedColumnName` が同じ照合で採用した列のカタログ上の名前へ直してから渡す（`SchemaTableEntry.TryFindColumnName`）。暗黙の主キー参照（`REFERENCES t`）も `Column.Name` でなくカタログ上の名前を渡す。
- 固定は `SqliteImportFidelityIntegrationTests`（`Import_TableNamesDifferingOnlyInCase_KeepsOneAndWarns`・`Import_ForeignKeyParentColumnInOtherSpelling_IsImported`・`Import_TableNameContainingDot_DoesNotThrow`）。mutation 実証済み。

### 型を宣言していない列

- 型を宣言していない列（生成列を含む）は `BLOB` として取り込み、`UndeclaredColumnType`（Subject＝列・Detail＝`BLOB`）で列ごとに名指しする。SQLite は型なしの列にどの型の値も入れるが、生成コードは `byte[]` で読むので BLOB 以外の値の行は読めない。
- 図で表せない定義の種類 `UndeclaredColumnType` は SQLite だけ `Detected`（ほかは `Impossible`）、扱いの表は取込の SQLite だけ `Notice`。
- 固定は `SqliteImportFidelityIntegrationTests.Import_UndeclaredColumnType_IsImportedAsBlobAndReported` と網羅の表のテスト。mutation 実証済み。

### NULL を許す主キーの補正

- rowid 表は INTEGER でない主キー列に NULL を許す。取込は NOT NULL へ補正したうえで `PrimaryKeyNullabilityAdjusted` で列ごとに告げる（補正を外すとモデル・GUI が表せない図になる。既存行に NULL の主キーがあると再構築のデータ移送が失敗し得る）。
- 告げない条件は 3 つ。
  - `WITHOUT ROWID` 表（判定は `PRAGMA table_list` の `wr` 列。`CREATE TABLE` の文字列走査を新設しない）。
  - 単一列の `INTEGER`（rowid 別名。完全一致で、`INT`／`BIGINT`／`INTEGER(10)` は別名にならないので告げる側）。`INTEGER PRIMARY KEY DESC` は SQL 文を見ないと区別できないので別名側へ倒す。
  - その列が元々 `NOT NULL` と宣言されていた（`table_xinfo` の `notnull` を主キー列ごとに持ち回る。補正後の `Column.IsNullable` からは元の宣言を復元できない）。形だけで判定すると、他の DB から移した `PRIMARY KEY (a, b)`＋各列 `NOT NULL` のごく普通の表で毎回鳴って警告の意味が失われる。
- 固定は `SqliteImportFidelityIntegrationTests`。mutation 実証済み。

## 型表記

- 宣言の型をそのまま返すので、型表記の正規化は恒等。
- REAL アフィニティ（`REAL`・`FLOAT`・`DOUBLE` 等）は Float64／`double`（REAL は 8 バイトの倍精度で、`float` で受けると読み書きのたびに下位の桁が落ちる）。
  - 判定は `SqliteTypeAffinity.IsReal`（SQLite の判定順 INT → CHAR/CLOB/TEXT → BLOB → REAL/FLOA/DOUB）を型カタログと型マッパーが共有する（写しを作らない）。
  - `TryFormat` は Float32→`REAL`・Float64→`FLOAT` のまま据え置くので、`REAL` の列は型トークンで `FLOAT` へ畳まれ、verbatim 併記（`NativeType = "REAL"`）と生成時 Info の対象になる。
- `DECIMAL` は NUMERIC アフィニティの宣言でしかなく丸めの単位を持たないので、負のスケールは書き出さない。精度なしの Decimal は `DECIMAL` のまま。
- 固定は `SqliteRealColumnGenerationTests`・`SqliteColumnClrTypeIntegrationTests`・`SqliteCSharpTypeMapperTests`／`SqliteTypeCatalogTests` の REAL アフィニティの表。mutation 実証済み。

## 同期

### 再構築の同期スクリプト

- 同期は再構築で行う（合成は `SyncPlanner`）。再構築は新しい表を作り→データを移し→DROP→RENAME→補助オブジェクト（インデックス・トリガーを `SchemaAuxiliaryObject` の CREATE 文全文で）を作り直す。暗黙の rowid 別名（`INTEGER` 主キー）は保たれる。
- 列・制約の句・テーブルオプションのうち意味モデルに無いもの（`AUTOINCREMENT`・`DEFAULT`・`CHECK`・`COLLATE`・生成列・`ON CONFLICT`・`DEFERRABLE INITIALLY DEFERRED`・`WITHOUT ROWID`・`STRICT`）は再構築で落ち、計画が警告する。固定は実 SQLite の行 `realdb-rebuild-warns-and-drops-column-attributes`／`realdb-rebuild-warns-table-options-and-constraint-clauses`。
- 写す列が無くても行の数を保つ。`SqliteSyncScriptBuilder` は `CopyColumns` が空のとき新しい定義の最初の列へ NULL を入れる `INSERT … SELECT NULL FROM …` を出す（逐次 DDL と同じく新しい列は NULL・NOT NULL の新しい列は行があれば SQLite が断って巻き戻る・新しい INTEGER PRIMARY KEY は採番で埋まる）。出さないと、元の列をすべて消して列を足す同期が警告なしに全行を消す。
  - 固定は実 SQLite の行 `realdb-sqlite-script-rebuild-without-copy-columns-keeps-rows-with-nulls`／`-not-null-first-column-fails`・同期計画の総当たりの行 `sqlite-795-replace-all-columns`。mutation 実証済み。
- 再構築は `PRAGMA legacy_alter_table` を ON にして RENAME のスキーマ再検証を避ける（作り直す表を参照するビューで失敗しないため）。
- スクリプトの順序は独自（新しい表を作る再構築が先・表の削除が最後）。一意制約の名前はスキーマの中の名前を取らないので、表の名前を変える同期でもぶつからない。
- 固定は `SqliteSyncScriptBuilderTests`（legacy の 1 件ほか）。

### 同期実行器

- 外部キー違反とビューの破損を同期の前後で比べ、増えたときだけ失敗にする。DB 全体の結果で判定すると、既存の孤児行や壊れたビュー（CREATE VIEW は参照先を検証しない）で無関係な同期まで失敗する。
  - 比べる単位は、外部キー違反が（子表, 親表）ごとの件数、ビューが名前。外部キーの検査は `foreign_key_check` を実行側で行い、PRAGMA はトランザクションの外で流す。
- 実行器は最後に必ず `foreign_keys` を同期前の値へ（同梱の e_sqlite3 の既定は ON）、`legacy_alter_table` を OFF へ戻す（接続はプールされるので、戻さないと後から開いた接続が外部キー検査なしになる）。
- 固定は実 SQLite の期待の行（ビューの `realdb-rebuild-with-dependent-view-succeeds`／`realdb-rebuild-breaking-view-rolls-back`・既存の違反の `realdb-existing-fk-violations-unrelated-add-column-succeeds`・`realdb-existing-fk-violations-child-rebuild-succeeds`・`realdb-existing-fk-violations-new-violation-fails`）と `SqliteSchemaSyncIntegrationTests`「同期の後にプールから戻った接続の外部キー検査と RENAME の動作は元のまま」。mutation 実証済み。

## 既知の割り切り

- 各方言の Identifier はドット付きテーブル名を 5 方言対称に `"a"."b"` へ分割クォートする（`SqliteIdentifierTests` が固定）。SQLite では `"a"."b"` が「アタッチ済み DB a のテーブル b」を意味するので、dbo 以外のスキーマを含む図を sqlite へ切り替えて DDL・同期を実行すると `unknown database` エラーになり得る。対称性を優先して直さない。
- その代わり `SyncDialectCapabilities.SupportsSchemaQualifiedTableNames` が false で、計画は `SyncPlanWarningKind.SchemaQualifiedTableName`（選んだ項目が触れる表・外部キーの親を表ごとに 1 件）を出し、DDL は `SchemaQualifiedTableNameNote` が注記する（扱いの表のマスで判定）。
  - 固定は `SchemaQualifiedTableNameTests`（5 方言の DDL と計画）・実 SQLite の行 `realdb-dotted-table-name-warns-and-fails`。mutation 実証済み。
- 主キーと同じ列集合の一意制約は取込に現れず差分が永久に消えないので作らない（`SupportsUniqueConstraintOnPrimaryKey` が偽＝`src/QuickER.Provider/CLAUDE.md`「計算列と主キーと同じ列集合の一意制約」）。
