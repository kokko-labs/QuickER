# QuickER.Provider.PostgreSql

このファイルの範囲は、PostgreSQL 方言だけの規則（取込のカタログの読み方・型表記と型マッパー・同期スクリプトの書き方）。
方言横断の規則は `src/QuickER.Provider/CLAUDE.md` を読む。

## 取込

### 列の読み方

- 列は `pg_catalog.pg_attribute` ＋ `format_type(atttypid, atttypmod)` から引く。
  - `information_schema.columns` は「接続ロールがその列に何らかの権限を持つ行」しか返さないので、表は見えるのに列ゼロで取り込まれる構成が実在する（実 PG 16 で確認）。
- `format_type` は情報源として採り、表記としては採らない（「型表記」）。修飾子と要素型の長さも持ち帰る（`bit(8)`・`bit varying(16)`・`integer[]`・`varchar(20)[]`。udt_name の `_int4`・`_varchar` は要素型の長さを持たない）。
- 生成列は `attgenerated <> ''` で計算列とする。
- ドメイン型は基底型へ平坦化し（再帰 CTE でドメインのドメインも終端まで・ドメイン配列も要素を平坦化）、警告を 1 件出す。

### 取り込む表

- 取込対象は `relkind IN ('r','p') AND NOT relispartition`＝パーティション親を取り込み子は除外する。親は `relkind = 'p'` で `PartitionDefinitionLost` を告げる。
- 拡張が所有する表（`pg_depend.deptype = 'e'`＝PostGIS の `spatial_ref_sys` など）は除外する。
- 引用識別子で `"Dup"` と `dup` が共存するので、生き残る側を決める並びは `ORDER BY relname COLLATE "C"` にする。
- 範囲外を参照する外部キーは除外して `ForeignKeyOutsideScope` で告げる。ドットを含む表への参照は、辞書に無い参照先として `ForeignKeyToExcludedTable` の経路に乗る。
- 固定は `PostgreSqlImportFidelityIntegrationTests`・`PostGisImportIntegrationTests`。

## 型表記

### 短縮語彙への正規化

- 取込の表記は `PostgreSqlTypeNotation.Normalize` が正本で、取込の `PostgreSqlSchemaImporter.NormalizeFormatType` は 1 行の委譲。差分の比較も同じ関数を通す。
- 規則：`character varying(n)`→`varchar(n)`・`character(n)`→`char(n)`・`timestamp[(n)] without time zone`→`timestamp(n|6)`・`timestamp[(n)] with time zone`→`timestamptz(n|6)`・`time[(n)] without time zone`→`time(n|6)`・`numeric(p,0)`→`numeric(p)`、と配列の要素型への再帰。`time with time zone` 側は従来語彙のまま。
  - 図の短縮語彙（素の `timestamp`／`timestamptz`／`time`→`(6)`・`numeric(18,0)`→`numeric(18)`）もここで寄せる。
- 正準表記を素通しにしてはいけない（既存図の `varchar(50)` と live の `character varying(50)` が食い違う）。`ALTER TABLE ... TYPE character varying(50)` を当てても `format_type` の出力は変わらないので、実行しても消えない偽 AlterColumn になる（実 PG 16 で確認）。規則を 1 本でも外すと、その型の列だけが正準表記のまま図へ入る。
- 規則が「同じ意味の別表記」であることと、正規化後の表記が DDL として通り再取込で同じ表記へ戻る（不動点）ことを固定する。
  - 分類表は「正規型へ往復する／verbatim 併記でしか保てない」を明示列挙するので、新しい型が「解析はできるが別の表記へ化ける」状態で静かに入り込めない。
- 固定は `PostgreSqlSchemaImporterTests`（表記の表＋冪等性）・`PostgreSqlImportFidelityIntegrationTests.TypeClosure_ImportedNotationsMatchTableAndClassification`・`ImportedNotations_AreSafeForDdl`（実取込の表記が `SqlTypeText` を通る）・`PostgreSqlTypeNotationIntegrationTests`。mutation 実証済み。

### 型カタログ

- 負のスケール（`numeric(10,-2)`）を読み書きする。`PostgreSqlTypeCatalog.TryParseScaleArg` は符号を許す 3 箇所の 1 つ（`src/QuickER.Provider/CLAUDE.md`「スケールの符号」）。
  - `format_type` を通さない生の表記 `numeric(10,2046)` は PG 自身が DDL として拒否する。
- `TryFormat` の上限：numeric の精度 1〜1000・スケール -1000〜1000・小数秒の精度 6（7 以上は断らずに 6 へ丸める＝変換の結果が元の精度と違う）・varchar／char 10,485,760（bytea は上限なし）。
- `numeric` は精度なしでスケールだけを宣言できないので、精度なし・スケールありの Decimal は書き出さない（素の `numeric` へ写すと丸めの宣言が黙って落ちる）。
- パラメータの型解決では上限の判定だけを外す（`TryFormatParameterType`）。
- 固定は `DialectTypeTableTests`（上限の対・`format-param-postgresql-*`・負のスケールの往復の行）・`PostgreSqlDialectTypeRealDbTests`（上限の対の DDL・小数秒の 6 への丸め）。

### 型マッパー

- 基本型名の取り出し（`PostgreSqlCSharpTypeMapper.GetBaseType`）は括弧の部分だけを除去する。括弧より後ろを切り捨てると、PostgreSQL が括弧の後ろへ置く時間帯の語ごと落ち、`timestamp(6) with time zone`／`time(3) with time zone` の列が黙って `DateTime`／`TimeSpan` に化ける。長さ・精度は元の表記から読む。
- ドライバ（Npgsql）が返す CLR 型へ写す：`timetz` は `DateTimeOffset`（`TimeSpan` では読み取り自体が `InvalidCastException`）・`interval`（範囲の修飾つきも）は `TimeSpan`・`cidr` は `System.Net.IPNetwork`・`inet` は `System.Net.IPAddress`（ネットマスクは落ちる＝Npgsql の既定）。文字列へ落とすと Npgsql が読めず表ごと使えない。
- 時間帯なしの `time` は `TimeSpan`・`timestamptz` は `DateTimeOffset`（Npgsql の `GetFieldType` は `TimeOnly`／`DateTime`）。PostgreSQL の列を読むのは生成 EF Core のコードだけで、EF Core はこの型のまま読み書きできる（`timestamptz` の +00:00 以外の値は Npgsql が断る＝`PostgreSqlColumnClrTypeIntegrationTests`）。
- EF Core に教える列の型（`CSharpTypeInfo.EfCoreColumnType`）：`date`→`date`・`timestamp`（精度つき・`without time zone` も）→`timestamp without time zone`・`time`→`time`・`timetz`→`time with time zone`・`json`／`jsonb`／`xml`→同じ名前。配列は要素の値に `[]` を付けて引き継ぐ（`Array`）。
  - 教えないと Npgsql の EF Core のプロバイダが `DateTime` を timestamptz で送って Kind=Unspecified を断り、`DateTimeOffset` を timestamptz で送って UTC 以外のオフセットを断り、`string` を text で送って PostgreSQL が 42804 で断る（配列も同じ）。
  - `time`（精度つき・`without time zone` も）→`time`：教えないと Npgsql の EF Core のプロバイダは `TimeSpan` を interval で送り、PostgreSQL は interval から time への代入で範囲（0〜24:00:00）の外の値の日の部分を黙って落とす（挿入・更新・一括挿入・ExecuteUpdate の SET・`time[]` のどれでも。`1.01:00` は `01:00`、`-01:00` は `23:00`）。教えると DB が 22008 で断る。範囲の外の値との比較は `PostgreSqlQueryParameterInterceptor` が LinqQuery でだけ interval へ戻す（`src/QuickER.Runtime.EntityFrameworkCore/CLAUDE.md`「PostgreSQL のクエリのパラメータの型」）。ガード：`PostgreSqlDialectTypeRealDbTests`（書き込みの範囲の手順）。
  - `money`→`money`：教えないと EF Core は decimal を numeric で送り、PostgreSQL が `money` と `numeric` の比較を断る（42883。条件・重複の事前チェックが失敗する）。教えると比べる値は `money` で送られ、小数 3 桁以上は丸められる（docs の既知の割り切り。`money` と `numeric` の比較が無いので値の型では送れない）。
  - `timestamptz`・`text`・`integer` などの既定の対応で書ける列には入れない（可搬図の出力を変えない）。値は図の綴りでなくマッパーの固定の綴り。
- 固定は `DialectTypeTableTests` のマッパーの行（`map-postgresql-timestamp-precision-*`＝精度つきでも時間帯の語を落とさない・`map-postgresql-timetz-*`＝time with time zone は DateTimeOffset・`map-postgresql-*-efcore-column-type` と `map-postgresql-array-*-efcore-column-type`＝教える列の型・宣言のセルの `matrix-map-postgresql-*`）・`PostgreSqlDialectTypeRealDbTests`（ドライバの実測 CLR 型との一致・EF Core の手順＝教える列の型を当てて読み書きする）・`PostgreSqlColumnClrTypeIntegrationTests`（UTC 書き込み・EF Core の読み書き）・`CodegenCaseTests` の行 `model-efcore-column-type-postgresql`。mutation 実証済み。

## 同期スクリプト

### 実行時にカタログから読む名前とドルタグ

- 旧主キー名・名前の分からない外部キー名は DO ブロックで `pg_constraint` から引き、`quote_ident` を通して埋める（`'"' || pk_name || '"'` の連結へ戻すと `"` を含む正当な名前で失敗する）。
- 逆引きは `to_regclass('<QuoteForDynamicSql(表名)>')` で ALTER TABLE と同じ 1 つの表に絞る（ドット付きの表名でも旧主キーを見つける・表が無ければ NULL で何もしない）。
- DO ブロックのドルタグは本文から決定的に選ぶ（`PgIdentifier.DollarQuoteTag(body)`＝本文に現れない限り `$$`、現れるなら `$q$` → `$qq$` …）。ドルクォートはタグ文字列そのもので終端するので、`$$` 固定だと `$$` を含む名前がブロックを閉じる。乱数にしない（出力の決定性）。
- 固定は `PostgreSqlSyncScriptBuilderTests.RuntimeConstraintName_IsAlwaysQuoteIdented`・`PostgreSqlSyncScriptBuilderTests.RuntimeLookup_DottedTableName_UsesRegclassOfQualifiedName`・同クラスのドルタグのケースと `PostgreSqlRuntimeConstraintNameIntegrationTests`（`AlterPrimaryKey_DottedTableName_SucceedsAndKeepsSameNamedTable`・`DropForeignKey_WithoutName_DottedTables_KeepsSameNamedTables`・`DynamicDrop_MissingSchema_IsNoOp`）。mutation 実証済み。

### 表の作成の一意制約

- `CREATE TABLE` は、主キーか先に書いた一意制約と同じ列の並びの一意制約をエラーなしに捨てる（同じ定義の索引を作らない）。`ALTER TABLE … ADD CONSTRAINT … UNIQUE` で足せば別の制約として持つ。
- DDL と同期の表の作成は、その一意制約を表を作る文の直後の `ALTER TABLE` で足す。分け方は方言横断の `TableConstraintLineBuilder`（`src/QuickER.Provider/CLAUDE.md`「一意制約の照合キーと付け直し」）で、この方言は宣言 `CreateTableUniqueConstraints.DropsDuplicateColumnLists` を真にするだけ。
- 固定は期待の行 `realdb-create-table-*` と `DialectDdlTableTests` の行 `*-unique-on-same-columns-as-earlier-unique-added-after-create-table`。

### 列の変更

- 新しい型が文字列型なら DO ブロックで `pg_attribute.attcollation` を読み、型の既定（`typcollation`）と違うときだけ `COLLATE "schema"."name"` を書く（`ALTER COLUMN … TYPE` は COLLATE を省くと新しい型の既定へ戻す）。表は `to_regclass` で絞る。
- 生成列の型は式を保ったまま変えられる（`SupportsComputedColumnTypeChange`）。
- 外部キーの両端は同じ文字列型の長さだけの違いを許す（`ForeignKeyAllowsStringLengthDifference`。許さないと主キーの付け替えに伴う付け直しが止まり、主キーの解除が外部キーに阻まれる）。
- 固定は `PostgreSqlColumnAttributeSyncIntegrationTests`（取込の範囲の外のドット付きの表の照合順序）と実 PG の期待の行（`realdb-pg-column-alter-keeps-non-default-collation-public-table`・`realdb-pg-computed-column-type-change-keeps-expression-and-not-null`・`realdb-pg-pk-change-with-string-length-only-fk-keeps-fk`）。
