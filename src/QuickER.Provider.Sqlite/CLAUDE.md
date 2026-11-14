# QuickER.Provider.Sqlite

このファイルの範囲は、SQLite 方言だけの規則（取込の PRAGMA の読み方・型の扱い・再構築の同期スクリプトと実行器・ファイルを開く接続）。
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

- rowid 表は INTEGER でない主キー列に NULL を許す。取込は NOT NULL へ補正したうえで `PrimaryKeyNullabilityAdjusted` で列ごとに告げる（補正を外すとモデル・GUI が表せない図になる。既存行に NULL の主キーがあると、再構築のデータ移送は NOT NULL か、rowid の別名になる列の NULL のガード〔「rowid の別名へ写す列の NULL」〕で断られて巻き戻る）。
- rowid の別名かの判定（単一列・宣言の型が大文字小文字を問わずちょうど `INTEGER`）は `SqliteRowidAlias.IsAliasType` の 1 か所で、同期の宣言 `SyncDialectCapabilities.SingleColumnPrimaryKeyReplacesNull` と共有する。
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
- `DECIMAL(p,s)` の列の値は、QuickER 版 Repository（`AddWithValue`）も EF Core も文字列で送り、SQLite が NUMERIC アフィニティで整数か倍精度へ変えて持つ。だから有効桁がおよそ 15 桁を超える値は宣言の範囲の中でも丸めて保存され、宣言のスケール・精度は当ても検査もされず、decimal の上限付近の値は保存できて読むと `OverflowException` になる（docs の割り切り：database「SQLite の decimal の列」。製品は変えない＝区切り DW の決定）。DDL の宣言をアフィニティの違う型へ変えると答えが変わる。
  - 固定は `Dialect/TypesRealDb`（行 `matrix-map-sqlite-decimal-12-3-upper`・`map-sqlite-decimal-28-10` の書き込みの範囲と QuickER 版の束縛の手順。期待は DB が変えて保存する値と読めない値で、根拠に docs の見出しを名指す）・`DialectTypeRealDbTableTests.WriteBases_PointAtExistingDocsHeadings`（根拠の見出しが英日の docs にある）。
- 固定は `CodegenCaseTests` の行 `code-sqlite-real-double-with-verbatim-spelling`・`CodegenCaseTests` の行 `code-sqlite-float-double-without-verbatim`・`SqliteDialectTypeRealDbTests`・`DialectTypeTableTests` の REAL アフィニティの行（`map-sqlite-real-affinity-*`・`roundtrip-sqlite-real-affinity-*`）。mutation 実証済み。

## 同期

### 再構築の同期スクリプト

- 同期は再構築で行う（合成は `SyncPlanner`）。再構築は新しい表を作り→データを移し→DROP→RENAME→補助オブジェクト（インデックス・トリガーを `SchemaAuxiliaryObject` の CREATE 文全文で）を作り直す。暗黙の rowid 別名（`INTEGER` 主キー）は保たれる。
- 列・制約の句・テーブルオプションのうち意味モデルに無いもの（`AUTOINCREMENT`・`DEFAULT`・`CHECK`・`COLLATE`・生成列・`ON CONFLICT`・`DEFERRABLE INITIALLY DEFERRED`・`WITHOUT ROWID`・`STRICT`）は再構築で落ち、計画が警告する。固定は実 SQLite の行 `realdb-rebuild-warns-and-drops-column-attributes`／`realdb-rebuild-warns-table-options-and-constraint-clauses`。
- 写す列が無くても行の数を保つ。`SqliteSyncScriptBuilder` は `CopyColumns` が空のとき新しい定義の最初の列へ NULL を入れる `INSERT … SELECT NULL FROM …` を出す（逐次 DDL と同じく新しい列は NULL・NOT NULL の新しい列は行があれば SQLite が断って巻き戻る・新しい INTEGER PRIMARY KEY は採番で埋まる）。出さないと、元の列をすべて消して列を足す同期が警告なしに全行を消す。
  - 固定は実 SQLite の行 `realdb-sqlite-script-rebuild-without-copy-columns-keeps-rows-with-nulls`／`-not-null-first-column-fails`・同期計画の総当たりの行 `sqlite-795-replace-all-columns`。mutation 実証済み。
- 再構築は `PRAGMA legacy_alter_table` を ON にして RENAME のスキーマ再検証を避ける（作り直す表を参照するビューで失敗しないため）。
- スクリプトの順序は独自（新しい表を作る再構築が先・表の削除が最後）。一意制約の名前はスキーマの中の名前を取らないので、表の名前を変える同期でもぶつからない。
- 固定は `SqliteSyncScriptBuilderTests`（legacy の 1 件ほか）。

### rowid の別名へ写す列の NULL

- rowid の別名（宣言の型 `INTEGER` の単一列の主キー）の列は NULL を持てず、NOT NULL を宣言しても、入れた NULL を断らずに採番の値へ置き換える。再構築で別名でなかった列の値をこの列へ写すと、NULL が黙って採番の値に変わってコミットする（主キーの移動・宣言の型の `INTEGER` への変更・複合の主キーの縮小・主キーの付与）。
  - 計画は列の値を見ないので、再構築の合成が `TableRebuildPlan.RowidAliasNullGuardColumn` で「合成後に別名になり、live では別名でなかった写す列」を印す（判定は宣言 `SyncDialectCapabilities.SingleColumnPrimaryKeyReplacesNull`。live で別名の列は NULL を持てないので印さない＝SQLite から取り込んだ INTEGER の主キーの表のスクリプトは変わらない）。
  - `SqliteSyncScriptBuilder` は移送の直前に、英語の注釈の行と、一時表 `quicker_rowid_null_guard` の NOT NULL の列（名前は `{表}.{列}` の 1 つの識別子）へ NULL の行を 1 件入れて断らせる文を出す（CREATE TEMP TABLE → `INSERT INTO temp.… SELECT … WHERE 列 IS NULL LIMIT 1` → DROP）。エラー文は「NOT NULL constraint failed: quicker_rowid_null_guard.{表}.{列}」。失敗の経路は実行器のトランザクションの巻き戻しが一時表も消す。
  - 新しい表の CHECK や BEFORE INSERT のトリガーでは止められない（別名の列は制約の検査の前に採番の値へ置き換わり、トリガーの `NEW` は -1 で SQLite の文書は未定義とする）。
- 壊すと：NULL が採番の値に変わってコミットし、その列の外部キーが無関係な親を指す（実行器の外部キーの前後の比べ方は、採番の値が既存の親に当たると気づかない）。ほかの方言は NOT NULL で断る。
- ガード：期待の行 `plan-row-dependent-change-pk-onto-null-names-primary-key`（5 方言）・`realdb-sqlite-rowid-alias-type-change-of-key-with-null-fails`・`realdb-sqlite-rowid-alias-composite-key-shrink-onto-null-fails`・`realdb-sqlite-rowid-alias-fk-child-onto-null-fails`・`realdb-sqlite-rowid-alias-key-move-without-null-commits`・`realdb-rebuild-keeps-data-and-auxiliary-objects`（live で別名の表はガードを出さない）・同期計画の総当たりの表明 5b と行の水準 `null-data` の主キーの移動（形 `null-data-key-move`）・`SyncPlanFuzzOracleTests`（行 `sqlite-rowid-alias-guard-*`）・`DialectDocsTableTests.SyncCapabilitiesTable_MatchesDeclarations`。

### 同期実行器

- 外部キー違反とビューの破損を同期の前後で比べ、増えたときだけ失敗にする。DB 全体の結果で判定すると、既存の孤児行や壊れたビュー（CREATE VIEW は参照先を検証しない）で無関係な同期まで失敗する。
  - 比べる単位は、外部キー違反が（子表, 親表）ごとの件数、ビューが名前。外部キーの検査は `foreign_key_check` を実行側で行い、PRAGMA はトランザクションの外で流す。
- 実行器はトランザクションの前に `foreign_keys=OFF` と `legacy_alter_table=ON` を流し、元へ戻さない。接続はプールしない（「ファイルを掴まない接続」）ので、切り替えた PRAGMA は接続を閉じると一緒に消え、後から開いた接続へ残らない。
- 実行器はスクリプトを 1 回のコマンドで流し、結果を最後まで読み切る（`SqliteScripts.RunToEndAsync`＝`ExecuteReaderAsync` の後に `NextResultAsync` を最後まで）。QuickER 自身が SQLite で結果を読まない文（DDL・PRAGMA）を実行するのは、この 1 か所だけ（取込などの問い合わせは `ExecuteReader` で読む）。
  - Microsoft.Data.Sqlite の `ExecuteNonQuery` は、行の無い表への `ADD COLUMN … NOT NULL`（SQLite は行があれば断る検査の問い合わせを文の中に持つ）の後ろの文が断られると、例外を出さずにその文から後ろを実行しない。実行器はトランザクションの中で、断られた文の前までをコミットし、後ろの文（外部キーの検査を含む）を黙って飛ばしていた。読み切ると、断られた文の例外が届いてスクリプト全体を巻き戻す。
  - スクリプトが複数の文かは静的に見分けられないので、1 文でも部品を通す。
  - ガード：`SourceRuleTests` の規則 `sqlite-script-executions`（QuickER.Provider.Sqlite・Db.UI・Cli・Gui の `ExecuteNonQuery(Async)` の呼び出しを断る。自己検査の行 `sqlite-script-executions-forms`・`sqlite-script-executions-inactive-branch`）・実 SQLite の期待の行 `realdb-sqlite-script-refused-statement-after-not-null-add-rolls-back`・同期計画の総当たりの行の水準（行のある表への NOT NULL の列の追加。本流の生成器が行の無い表へ NOT NULL の列を足す）。mutation 実証済み（読み切りを `ExecuteNonQueryAsync` へ戻すと、期待の行と総当たりの種が赤）。
- 固定は実 SQLite の期待の行（ビューの `realdb-rebuild-with-dependent-view-succeeds`／`realdb-rebuild-breaking-view-rolls-back`・既存の違反の `realdb-existing-fk-violations-unrelated-add-column-succeeds`・`realdb-existing-fk-violations-child-rebuild-succeeds`・`realdb-existing-fk-violations-new-violation-fails`）。PRAGMA が残らないことは、接続をプールしないことの規則（`SourceRuleTests` の規則 `sqlite-file-connection-creations`・`sqlite-file-connection-pooling-off`）が守る。mutation 実証済み。

## ファイルを掴まない接続

- QuickER 自身が SQLite のファイルを開く接続（新規作成 `SqliteDatabaseFile.CreateEmpty`・取込 `SqliteSchemaImporter.ImportAsync(string, …)`・同期 `SqliteSchemaSyncExecutor`）は、`SqliteFileConnections.Create` だけが作り、接続文字列に `Pooling=False` を立てる（呼び出し側の `Pooling` の指定も上書きする）。
  - Microsoft.Data.Sqlite は既定で接続をプールし、閉じた接続もファイルを開いたままにする。SQLite は Windows でファイルを削除の共有なしで開くので、操作の直後に利用者がファイルを移動・名前の変更・削除すると共有違反（0x80070020）で断られる。接続ダイアログの接続テストも取込の経路を通る。
  - どの入口も操作 1 回で接続を 1 本しか開かないので、プールを切っても性能は変わらない。`ClearPool` で後から放す形は選ばない（例外の経路で呼び忘れ、同じ接続文字列のほかの接続まで放す）。
  - `ImportAsync(SqliteConnection, …)` は呼び出し側が開いた接続を使う（テスト・接続の再利用向け）ので対象の外。生成コードのランタイム（QuickER.Runtime.Sqlite）は利用者のアプリの接続なので対象の外。
- ガード：
  - `SourceRuleTests` の規則 `sqlite-file-connection-creations`（src の `SqliteConnection` の生成は `SqliteFileConnections.cs` の 1 件だけ。QuickER.Runtime.Sqlite は範囲の外で、Witness がそこの実在の生成を拾う）と `sqlite-file-connection-pooling-off`（その作り手が `Pooling = false` を立てる）。自己検査の行は `sqlite-file-connection-creations-forms`・`sqlite-file-connection-creations-inactive-branch`・`sqlite-file-connection-pooling-off-forms`。
  - 新規作成：`ConnectionDialogTests`（組 `ConnectionDialogHarness` の不変条件 12＝新規作成で作ったファイルを手の後と行の終わりに排他で開ける）。
  - 取込・同期：`SyncPlanFuzzTestsBase.ExpectationRows_MatchOnRealDatabase` の SQLite の派生 `SqliteSyncPlanFuzzTests`（期待の行の実 DB の段の汎用の表明 6＝行の後に DB のファイルを排他で開ける。行は製品の取込 → 実行器 → 取り込み直しを流す）。
  - 排他で開けるかは、Windows で移動・削除と同じ答えになる（再現の実測で全組が一致）。Linux の排他（flock）は SQLite のロック（fcntl）と関係しないので常に開け、受け入れの表明は Windows のジョブでだけ効く。
  - mutation 実証済み（3 入口それぞれで `Pooling=False` を外すと、該当の行が赤になる）。

## スキーマをまたぐ外部キー

- SQLite の外部キーの句（`REFERENCES`）は表名にスキーマを書けず、親は子と同じ DB の中で解決する。DDL 生成・同期の表の追加・再構築の外部キーの制約行は `SqliteDdlGenerator.BuildForeignKeyConstraintLine` の 1 か所で組み、親の表名は `SqliteIdentifier.QuoteReferencedTable` が決める。
  - 子と親の DB 名は `SchemaQualifiedName.Split` で取り、スキーマの無い名前は `main`（スキーマを付けない `CREATE TABLE` は main に作られる）とみなし、SQLite の照合（`SqliteIdentifier.NameEquals`＝ASCII だけ大文字小文字を無視。取込と共有）で比べる。
  - 同じ DB なら親の表の部分だけを書く（`REFERENCES "regions"`）。違う DB なら制約行を出さない。
- 違う DB の外部キーは作らず、その表の `CREATE TABLE` の前に `CrossSchemaForeignKeyNote` が注記する（図で表せない定義の種類 `CrossSchemaForeignKey`＝E7。扱いの表の SQLite の DDL・同期のマスが `OmitWithAnnotation` かで決め、方言名で分岐しない）。
  - 同期の計画は、外部キーの両端のドットの表を `SyncPlanWarningKind.SchemaQualifiedTableName` で表ごとに名指しする（専用の警告の種類は持たない）。
  - 差分は畳まない：SQLite の取込はドットを含む表を持ち帰らない（docs/database.md）ので、DB の側にドットの表が現れず、差分は表の追加として出続ける（下の「既知の割り切り」）。
- 壊すと：`REFERENCES "sales"."regions"` は DB を添付しても構文エラーになり、ドットの表を親に持つ図の DDL・同期が流せない。違う DB の外部キーを書いても同じ。
- ガード：`DialectDdlTableTests`（行 `ddl-fk-to-schema-qualified-parent-in-same-schema-references-table-part`・`ddl-fk-to-main-qualified-parent-from-unqualified-child-references-table-part`・`ddl-fk-across-schemas-omitted-with-note-sqlite`・`sync-add-table-fk-to-schema-qualified-parent-in-same-schema-references-table-part`・`sync-add-table-fk-across-schemas-omitted-with-note-sqlite`・`sync-rebuild-fk-across-schemas-omitted-with-note-sqlite`）・期待の行 `plan-cross-schema-fk-names-both-dotted-ends-only-where-declared`・実装先どうしの一致のシナリオ `key-shape-dotted`（`main.region_notes` の外部キーが実 SQLite で親の無い子を断る）・`UnsupportedCoverageTests`・`DialectDocsTableTests.SyncCapabilitiesTable_MatchesDeclarations`。mutation 実証済み。

## 既知の割り切り

- 各方言の Identifier はドット付きテーブル名を 5 方言対称に `"a"."b"` へ分割クォートする（`DialectIdentifierTableTests` の行 `quote-dot-*` が固定）。SQLite では `"a"."b"` が「アタッチ済み DB a のテーブル b」を意味するので、dbo 以外のスキーマを含む図を sqlite へ切り替えて DDL・同期を実行すると `unknown database` エラーになり得る。対称性を優先して直さない。
- その代わり `SyncDialectCapabilities.SupportsSchemaQualifiedTableNames` が false で、計画は `SyncPlanWarningKind.SchemaQualifiedTableName`（選んだ項目が触れる表・外部キーの親を表ごとに 1 件）を出し、DDL は `SchemaQualifiedTableNameNote` が注記する（扱いの表のマスで判定）。
  - 固定は `DialectDdlTableTests` の行 `ddl-schema-qualified-dotted-table-name-note`（5 方言の DDL）・期待の行 `plan-dotted-table-name-warns-only-where-declared`（5 方言の計画）・実 SQLite の行 `realdb-dotted-table-name-warns-and-fails`。mutation 実証済み。
- 再構築もほかの文と同じ規則で `a.b` を添付 DB `a` の表 `b` として扱う。一時表は同じスキーマに作り（`"a"."b_quicker_rebuild"`）、移送・DROP も `"a"."b"` を指し、`RENAME TO` は表の部分だけ（`"b"`。SQLite の `RENAME TO` はスキーマを書けず、同じ DB の中で名前を変える）を書く。分け方は `SchemaQualifiedName.Split`。
  - 一時表を main の 1 つの識別子にすると、CREATE と RENAME が main を、移送と DROP が添付 DB を指し、添付した DB へ手で流すと添付 DB の表を消して main へ移す。製品の経路では、取込がドット付きの表を持ち帰らず実行器は ATTACH しないので、既存の表の再構築にドット付きの名前は届かない（届いても実行器が最初の文の `unknown database` で巻き戻す）。
  - 固定は `DialectDdlTableTests` の行 `sync-rebuild-schema-qualified-table-stays-in-its-schema`。実ファイルの段の行は持たない（live を製品の取込で作るので、ドット付きの既存の表を置けない）。mutation 実証済み。
- 主キーと同じ列集合の一意制約は取込に現れず差分が永久に消えないので作らない（`SupportsUniqueConstraintOnPrimaryKey` が偽＝`src/QuickER.Provider/CLAUDE.md`「計算列と主キーと同じ列集合の一意制約」）。
