# QuickER.Provider.Oracle

このファイルの範囲は、Oracle 方言だけの規則（取込のカタログの読み方・型表記と型マッパー・同期スクリプトと SQL*Plus）。
方言横断の規則は `src/QuickER.Provider/CLAUDE.md` を読む。

## 取込

### 実在する制約と設計の表だけ

- 取り込む範囲は接続ユーザーの自スキーマで、素の名前だけを返す（取込が `.` を含む表を返さないことを統合テストが固定する）。
- PK・UNIQUE・FK と範囲外の外部キーの列挙の 4 クエリは `status = 'ENABLED'` で絞る（DISABLE された制約は何も強制しないので、取り込むと図が DB に無い保証を宣言する）。
  - 無効化された制約は告知専用の `DisabledConstraintsSql` で拾い、`DisabledConstraintExcluded`（`status = 'DISABLED'`）で告げる。
- `TablesSql` は `temporary = 'N'` で絞り、一時表（`temporary = 'Y'`）・MV コンテナ表（`user_mviews`）・MV ログ表（`MLOG$_…`＝`user_mview_logs.log_table`）を除外する（「ビューは取り込まない」宣言と実装を一致させる）。
- パーティション親は `user_tables.partitioned = 'YES'` で判定する。
- 計算列は `user_tab_cols` を `user_generated = 'YES'` で絞り、`virtual_column='YES'` の列とする（`user_tab_columns` に virtual_column 列は無い）。
  - `user_generated` の条件が関数インデックスの `SYS_NC…$`（`user_generated='NO'`）を除き、利用者の INVISIBLE 列は残す（`hidden_column='NO'` で絞ると INVISIBLE 列まで落ちる）。
  - 式の `data_default` は LONG 型なので `InitialLONGFetchSize = -1` が必須。
- 引用識別子で `"Dup"` と `dup` が共存するので、生き残る側を決める並びは `NLSSORT(..., 'NLS_SORT=BINARY')` にする。
- ドットを含む表への参照は、外した表の集合を外部キーの読み込みへ渡して `ForeignKeyToExcludedTable` で告げる。
- 固定は `OracleImportFidelityIntegrationTests` の `Import_DisabledConstraints_AreNotImported`／`Import_ExcludesTemporaryTablesAndMaterializedViews`／`Import_ExcludesMaterializedViewLogTables`。mutation 実証済み。

### 既定値の NULL

- Oracle は空文字列が NULL なので、`DEFAULT ''`・`DEFAULT N''` と括弧で包んだ `NULL`／`''`（`DEFAULT (NULL)`・`DEFAULT ( '' )`）も NULL とみなす（`OracleSchemaImporter.IsNullDefault`＝外側の括弧と空白を外してから比べる）。
  - 共有の `SchemaImportWarnings.IsNullDefault` はほかの方言の意味を変えないよう触らない。
- 固定は `OracleImportFidelityIntegrationTests.Import_EmptyStringAndParenthesizedNullDefaults_AreNotDefaults`（空白 1 文字の `' '` は告げる対照つき）。mutation 実証済み。

### 別スキーマを使うテスト

- Oracle の別スキーマを要するテストは `OracleContainerFixture.ExecuteAsSystemAsync` を使う（アプリユーザーは `CREATE USER` できない＝MySQL のフィクスチャの `RootConnectionString` と同じ逃がし口）。
- `ResetSchemaAsync` は自スキーマしか消さないので、作った別スキーマはテスト側で撤去する。

## 型表記

### 取込の表記と型カタログ

- 長さの単位語：`char_used = 'C'` のとき `VARCHAR2(50 CHAR)` を出す。N 系は常に文字単位だが、構文が単位語を受けないので付けない。
  - 正規型に単位のスロットが無いので、保存は `CanonicalTypeTokenAttacher` の verbatim 併記が担う（`NLS_LENGTH_SEMANTICS=CHAR` の DB では全文字列列に verbatim が付く）。
- 取込は精度なし・スケール 0（`INTEGER`／`SMALLINT`／`NUMBER(*,0)`）を `NUMBER(*,0)` で書く（素の `NUMBER` にすると作り直した列が小数を受ける）。型カタログと型マッパーも `INTEGER`／`SMALLINT` を `NUMBER(*,0)` と同じ型として読む。
- `TIMESTAMP` は `TIMESTAMP(6)` で返る（`OracleTypeNotation.Normalize`）。
- `OracleTypeCatalog.TryParseScaleArg` は符号を許す 3 箇所の 1 つ（`src/QuickER.Provider/CLAUDE.md`「スケールの符号」）。`OracleTypeCatalog.TryParseNumber` は `scale is not null and not 0` で Decimal 側へ倒す（`is > 0` だと `NUMBER(10,-2)` が Int32 に化ける）。
- 精度なし・スケールありの `NUMBER(*,s)` を書き出せるのは Oracle だけ。`*` は 38 ではない（実 Oracle で `NUMBER(*,2)` が 39 桁の整数を受けた）。
- `TryFormat` の上限：`MAX_STRING_SIZE = STANDARD` の値（NVARCHAR2 2000・VARCHAR2 4000・NCHAR 1000・CHAR／RAW 2000）・NUMBER の精度 38・スケール -84〜127・TIMESTAMP の精度 9。接続先の設定を知らないので EXTENDED の DB でも同じ上限（安全側）。長さ無しの長さを要する型は書き出さない（構文エラー）。パラメータの型解決でも上限を外さない。
- 固定は `OracleTypeCatalogTests`（`TryFormat_Respects*Limits`・負スケール往復）・`OracleTypeNotationTests`・`OracleTypeNotationIntegrationTests`。

### 型マッパー

- `TIMESTAMP WITH LOCAL TIME ZONE` は `DateTime`（ODP.NET は DateTime で返し、DateTimeOffset で受けると `InvalidCastException`）。
- 長さは単位語つき（`VARCHAR2(50 CHAR)`／`CHAR(10 BYTE)`）からも読む（読めないと `[MaxLength]`・`HasMaxLength`・値オブジェクトの長さ検証が全部消える）。
- `INTEGER`／`SMALLINT` は `NUMBER(*,0)` と同じ `decimal`（ODP.NET は decimal で返す）。
- `MapNumber` は `scale is > 0` で判定している。今は `PrecisionScaleRegex` が負のスケールに一致しないので害は無いが、正規表現に符号を足すと `NUMBER(10,-2)` が `int` へ落ちるので同時に直す。
- 既知の残件：`NUMBER(*,s)` の `*` を読まず、値オブジェクトの小数の検証は付かない（直すにはテンプレートとランタイムの規則まで要る）。
- 固定は `OracleCSharpTypeMapperTests`（`Map_AnsiInteger_IsDecimalLikeNumberStarZero`）・`OracleColumnClrTypeIntegrationTests`。mutation 実証済み。

## 同期スクリプト

### 制約名の逆引き

- 旧主キー・名前の分からない外部キーは `all_constraints` を表の所有者で絞って逆引きする PL/SQL ブロックで外す（`OracleSyncScriptBuilder.OwnerPredicate`）。動的 SQL の名前は `OracleIdentifier.QuoteForDynamicSql` を通す。
  - 修飾した表名はスキーマの部分で絞る（引用識別子で作るので大文字小文字を保つ・リテラルは `EscapeStringLiteral`）。修飾しない表名は `SYS_CONTEXT('USERENV', 'CURRENT_SCHEMA')`（修飾しない `ALTER TABLE "T"` と同じ名前の解決）。
  - 外部キーは子と親の所有者を別々に絞る。
  - 表名だけで `user_constraints` を引くと、自スキーマの同じ名前の表の制約を拾い（ORA-02443）、無ければ旧主キーを外さずに新しい主キーの追加が ORA-02260 で失敗し、現在のスキーマを変えたセッション（`ALTER SESSION SET CURRENT_SCHEMA`）では別の表の制約を引く。
- 識別子は `"` を持てないので、`"` を含む名前の扱いは対象外。
- 固定は `OracleRuntimeConstraintNameIntegrationTests` と `OracleSyncScriptBuilderTests` の `RuntimeLookup_QualifiedTableName_IsScopedByOwner`／`RuntimeLookup_OwnerLiteral_IsEscaped`。mutation 実証済み。

### 説明と SQL*Plus

- `COMMENT ON … IS` は文字列リテラルしか受けず、SQL*Plus はリテラルの中でも行単位で読み（行の区切りは LF だけ＝実測）、`&` を置換変数として扱う。
- LF・CR・`&` を含む説明だけを `OracleCommentStatement` が `BEGIN EXECUTE IMMEDIATE … END;` で包み、それらを `CHR(n)` で連結する（説明の `'` は 2 段で二重化・文字列部分は 200 文字ごとに行を分ける）。含まない説明の出力はバイト不変。DDL 生成と同期スクリプトの両方がこのヘルパーを通る。
- `/` 行は PL/SQL ブロックの後ろにだけ置き、通常の文は `;` で終える（SQL*Plus の `/` は直前の文をもう一度実行するので、CREATE／ALTER が 2 回走って ORA-00955／ORA-01430 で止まる）。
- 実行器 `OracleSchemaSyncExecutor.SplitStatements` も同じ規則で分割する（通常の文は「リテラル・コメントの外で行末が `;`」・`DECLARE`／`BEGIN` で始まるブロックは `/` 行・空のバッファの `/` 行は何もしない）。統合テストのフィクスチャも同じ分割を使う（写しを持たない）。
- 固定は `OracleCommentStatementTests`・`OracleMultilineCommentIntegrationTests`・`OracleSqlPlusScriptIntegrationTests`（コンテナ内の実 sqlplus で同期スクリプトと DDL を流して終了コード 0）・`OracleSchemaSyncExecutorTests`。mutation 実証済み。

### 方言の事実

- DDL は暗黙コミットされる（`TransactionalDdl` が偽）。
- 外部キーの `ON UPDATE` と `ON DELETE SET DEFAULT` を持たない。DDL・同期は注意コメントを出して句を省き（`OracleReferentialAction.AppendNotes`＝DDL・同期の共有）、比較は実効値へ畳む（畳まないと既定の追加だけで ORA-02275）。
  - 固定は `OracleTypeNotationIntegrationTests.UnsupportedReferentialActions_LeaveNoForeignKeyDiff`・`OracleTypeNotationTests` の配線テスト・`Oracle{DdlGenerator,SyncScriptBuilder}Tests` の ON DELETE SET DEFAULT の注記。
- 主キーと同じ列集合の一意制約を作れない（ORA-02261）。DB にある一意制約と同じ列へ主キーを付け替える同期は、その一意制約を暗黙に外して名指しする（`src/QuickER.Provider/CLAUDE.md`「計算列と主キーと同じ列集合の一意制約」）。固定は同期計画の総当たりの行 `oracle-100-111-move-pk-onto-unique`・`oracle-move-pk-onto-referenced-unique`。
- 既定値のある列の型の種類の変更は警告する（実 Oracle の行 `realdb-oracle-column-type-kind-change-with-default-warns`）。
- 列の変更では外部キーを外し直さない。外部キーの両端は同じ文字列型の長さだけの違いを許す（単位語 CHAR／BYTE は両端で同じこと）。固定は実 Oracle の行 `realdb-oracle-pk-change-with-string-length-only-fk-keeps-fk`。
- 引用符なしで作った表は `ORDERS` で持つので、差分は live の綴りへ揃えて組む（揃えないと図の `"Orders"` が ORA-00942）。固定は実 Oracle の行 `realdb-oracle-case-only-names-sync-to-uppercase-table`。
