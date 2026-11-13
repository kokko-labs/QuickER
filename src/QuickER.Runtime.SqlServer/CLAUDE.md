# QuickER.Runtime.SqlServer

SQL Server 方言エンジン（QuickER 版 Repository の ADO 実装）の固定コードの規則。
方言に依らない共通の規則は `src/QuickER.Runtime/CLAUDE.md` にある。

## SqlParameter の明示型

- 生成ランタイムの SqlParameter は `[SqlColumnType]` 属性由来の明示型にする。
- 文字列 Size の「宣言長超過なら値長」ガードを固定 Size にしてはいけない。サイレントなデータ破損を招く。
- 式の翻訳の WHERE の値（`EntitySaveMetadata.AddQueryParameter` → `AddQueryColumnParameter`）は、値が列の SQL の型にそのまま入るときだけ列の型で束縛し、入らないときは値の型で束縛する（判定は `QueryValueFits` の 1 か所・値そのもので見る）。
  - 入らない値：整数の列（tinyint・smallint・int・bigint）と整数でない数・範囲の外の整数（整数ちょうどの double は入る。NaN・無限大は入らない）、decimal(p,s) の列と末尾の 0 を除いた小数の桁が s を超える値・整数部の桁が p−s を超える値、date の列と時刻つきの DateTime、Unicode でない文字列の列（varchar・char・text）と ASCII の外の文字を含む値。ほかの型と null は列の型のまま。
  - 理由：比較の値が列の型に入らないことがある（C# は整数の列を double・long へ拡大して比べる。手書きの LINQ の `x.Tiny < 300`・簡易 DSL の `qty < 1.5`・int の列と long のリストの IN）。列の型で束縛すると SqlClient かサーバーが値を先に変換し（切り捨て・丸め・`?` への置き換え）、C#・EF Core・インメモリと違う行を返すか、オーバーフローの例外になる。値の型で束縛すると、サーバーが列の側を変換して値のまま比べる。
  - DateTime の値は datetime2 で束縛する（AddWithValue は datetime を選び、1/300 秒に丸める）。符号なし整数と sbyte の値は bigint（範囲の外は decimal）で束縛する（SqlClient はこの型を対応付けられず ArgumentException になる）。
  - インデックス：入る値は今までどおり列の型で束縛するので、列の側の変換は起きず、インデックスの使われ方は変わらない。入らない値だけ、サーバーが列の側を暗黙に変換し（CONVERT_IMPLICIT）、seek が scan になり得る（そうした比較は以前は誤答か例外だった）。
  - datetime2(n)・time(n) の列は、属性が小数秒の桁を持たず、値の精度のまま束縛されるので割れない（式の翻訳の表の行が回帰の網として持つ）。
  - EF Core 版は `SqlServerQueryParameterInterceptor` が同じ規則で decimal と非 Unicode の文字列を束縛し直す（`src/QuickER.Runtime.EntityFrameworkCore/CLAUDE.md`「SQL Server のクエリのパラメータの型」。判定の写しを片方だけ変えない）。
  - ガード：`table:TranslatorParityPredicates`（`TranslatorParityPredicateRows` の行 `int-column-*`・`int-key-*`・`in-long-list-beyond-int`・`in-uint-list-beyond-int`・`decimal-*-beyond-scale`・`date-*-time-of-day`・`varchar-*`・`datetime2-scaled-*`・`time-scaled-*`）・`Dsl/SqlServer`（形 `compare-*-literal-fractional`）。直す前の束縛で SQL Server の 13 行と総当たりの 4 件が赤になることを確かめた。

## BulkInsert のオプション

`BulkInsertAsync` の共通の契約は `src/QuickER.Runtime/CLAUDE.md`「BulkInsert の契約」。

- `SqlBulkCopyOptions.CheckConstraints` を常時付ける。既定では FK / CHECK が検査されず、行単位 INSERT が弾く行を一括追加だけが通す非対称になる。`FireTriggers` は付けない（QuickER の DDL はトリガー非生成）。
- `KeepNulls` も常時付ける。外すと、DEFAULT のある列へ null を一括追加したときだけ既定値が入る（行単位 INSERT は NULL）。
- `BulkCopyTimeout` は接続の `CommandTimeout`（接続文字列の `Command Timeout`・既定 30）を渡す。外すと接続文字列の値が届かず、常に 30 秒で打ち切られる。
- ガード：`GeneratedRuntimeAdoParityTests` の `BulkInsert_NullIntoColumnWithDefault_StoresNull` / `BulkInsert_FollowsConnectionStringCommandTimeout`（Docker）。mutation 実証済み。

### SqlBulkCopy へ渡す値をサーバーの形へ合わせる

行単位の INSERT のパラメータはサーバーが列へ合わせ、SqlBulkCopy はクライアントの側で合わせる。`EntityDataReader.GetValue`（`_06`）の `AsServerStores` が、SqlBulkCopy へ渡す値だけをサーバーの形へ直す（パラメータの束縛は変えない）。

- 小数秒の桁を持つ列（`[SqlColumnType]` の DbType が DateTime2 / Time / DateTimeOffset で Scale < 7）：小数秒を Scale 桁へ四捨五入する（ちょうど半分は上げる・日付も繰り上がる）。丸めると型の範囲を出る値は、範囲の中のその桁の最後の値で止める（サーバーも例外にせずそこで止める）。DateTimeOffset はローカルの時刻を丸め、UTC が範囲を出ない最後の値で止める。time の 0 未満・24 時間以上は直さずに渡す（どの経路も SqlClient が断る）。外すと SqlBulkCopy が切り捨て、同じ値を InsertAsync と違う値（日付の境目では日付まで）で保存する。
- 文字列の列（NVarChar / NChar / VarChar / Char・Size > 0）：長さを超えた分が U+0020 だけなら Size 文字へ切る。バイナリの列（VarBinary / Binary）：超えた分が 0x00 だけなら Size バイトへ切る。どちらもサーバーが INSERT で黙って落とす形で、外すと SqlBulkCopy がコピー全体を断る。それ以外の超過は直さず、どの経路も断る（例外の型は経路で違う＝docs の一括追加の節）。
  - Unicode でない列で、超えた分がコードページで空白に置き換わる文字（U+3000 など）は直さない。サーバーは置き換えた後の空白を落とすが、置き換えの表を写さないと合わせられない（docs に書いた割り切り・値の往復の生成器の作らない形の宣言にも Accepted で載せる）。
- 小数秒の桁は型マッパー（`SqlServerCSharpTypeMapper`）が `CSharpTypeInfo.SqlFractionalSecondsScale` で返し、生成器が `[SqlColumnType]` の Scale へ出す。datetime2・time・datetimeoffset の列にはいつも出す（指定なしは 7）。属性の Scale は byte なので、出さないと `datetime2(0)` の 0 と区別できない。decimal の `Precision` / `Scale` には入れない（EF Core の HasPrecision・値オブジェクトの検証・サンプル値へ波及する）。マルチターゲットは `MultiDialectTypeReconciler.SupplementSqlColumnTypes` が sqlserver の辞書から補う。
- パラメータの束縛（`AddColumnParameter`）は Precision のある列にだけ Scale を当てる。日時の列に Scale を当てると SqlClient が送る前に値を切る（mutation で確かめた）。
- ガード：`GeneratedRuntime/ParityValuesSqlServer`（`BackendParitySqlServerValueRuntimeTests`＝QuickER 版と EF Core 版の 5 つの書く経路の値の往復・docs の写しの期待・Docker）・`DialectTypeTableTests`（行 `map-sqlserver-fractional-seconds-scale-*` と行列の項目 `frac`）。mutation 実証済み。

## 行バージョンの OUTPUT と版の回収

楽観排他の共通の骨格は `src/QuickER.Runtime/CLAUDE.md`「rowversion による楽観排他」。

- QuickER 版 ADO の版チェックは `sqlserver` 方言のみ。`WHERE ... AND [row_ver] = @original` と `OUTPUT INSERTED` で版を回収する。
- 回収した版は `RowVersionCollector` がコミット後に適用する。ロールバック時に幻の版を残さないため。
- UPDATE が 0 件のときは `RowVersionConcurrency.RowExistsAsync` で実在確認し、「行なし」と「版が古い」を分ける。問いは存在の有無だけなので、専用の `EntitySaveMetadata.ExistsByIdSql`（`SELECT CASE WHEN EXISTS (SELECT 1 …) THEN 1 ELSE 0 END`）を使う（全列 SELECT して捨てない）。
- ガード：`SqlServerConcurrencyRuntimeTests`（Docker）。

## 無制限バイナリ列のストリーミング

共通の契約（Stream アクセサ・長さの契約）は `src/QuickER.Runtime/CLAUDE.md`「無制限バイナリ列の除外」。

- エンジンは `SequentialAccess`＋`GetStream` で読み、Stream 値の `SqlParameter`（`VarBinary`, -1）で書く。
- Stream の長さの検査を外すと、SQL Server は `length` を無視して Stream の中身をすべて保存する（例外にならない）。
- ラッパーの `ArgumentException` は SqlClient に包まれずにそのまま出る。このとき UPDATE は適用されない（実測）。
- ガード：`SqlServerBinaryColumnRuntimeTests.Stream_Write_LengthMismatch_Throws_AndKeepsBlob`（入力は共有の `BinaryLengthMismatchCases`。Include のある取得でも除外列は未取得状態、を含む）。

## Include の無い取得と FOR JSON

- Include の無い取得（`Query()` の `ToListAsync` / `FirstOrDefaultAsync` と射影のフォールバック）は、FOR JSON でなく `MaterializeRootsAsync` で読む。`SELECT {ColumnList}`＋`SelectMaterializer` で、`GetAllAsync` と同じ列・同じ組み立て器を使う。
  - FOR JSON は Include の子を 1 文で同じ時点のデータとして組み立てるためのもの。Include の無い取得では JSON の組み立てと解析の分だけ遅く（24 列・20 万行で約 2.9 倍の時間・約 3.9 倍の割り当て）、値も `GetAllAsync` と食い違う。
  - 食い違い：FOR JSON は `float` を有効数字 16 桁・`real` を 8 桁で書く（`0.1 + 0.2` が `0.3`・`double` の最大値が無限大・`10.0001545` の real が `10.000154`）。NULL の列をキーごと出さない（NOT NULL 宣言の列に DB の NULL があると初期化子が残る）。
  - 旧 datetime・money・datetimeoffset・time・負のゼロは FOR JSON も同じ値を書く（実測）。
  - `MaterializeRootsAsync` は SQL Server と SQLite の共有で、方言で違うのは行の組み立ての 1 行だけ（SQL Server＝`MapEntity`、SQLite＝`MapEntityObject`〔VO ありは `SetColumnValue` のループ〕）。
- Include のある取得（FOR JSON のまま）も同じ値を読む。
  - `JsonQueryPlanner.BuildProjection` が float / real の列だけを `CONVERT(varchar(30), 列, 3)`（SQL Server の可逆な書式＝両型とも 17 桁）で書く。
  - 実行器の FOR JSON 専用の `JsonOptions` が `NumberHandling = AllowReadingFromString` で読む（値オブジェクトの内側の数値も同じ options）。`AllowReadingFromString` を `RemoteJson.Options` など他の options へ広げない（FOR JSON の読み取りでだけ数値が文字列で来る）。
  - 列の判定は `[SqlColumnType]` が Float / Real か、属性が無ければ CLR の型（Nullable と値オブジェクトの内側を剥がして double / float）。属性を先に見るのは、double のプロパティが decimal の列に付いた図で varchar への変換が桁あふれしないため（style 3 は decimal・int・money では無視されエラーにもならない＝実測）。
  - NULL は resolver modifier `ReadMissingColumnsAsNull` が `JsonTypeInfo.OnDeserializing` で SELECT 列（`SelectProperties`）だけを既定値へ戻してからキーを読ませる。除外したバイナリ列（未取得状態）・ナビゲーション・`RowState` には触れない。
  - SQL 側の `INCLUDE_NULL_VALUES` を採らないのは、子が 0 件の Include が null のナビゲーションになり、NOT NULL 宣言の値型の列の NULL が System.Text.Json の例外になるため。
- 格納値を値オブジェクトが拒んだときの列名指しは、FOR JSON でも実行器の resolver modifier `NameRejectedStoredValues` が値オブジェクトのプロパティだけに `ColumnNamingValueObjectConverter<TVo>` を被せて行う（共有の `ValueObjectJsonConverterFactory` は触らないので、リモートの例外分類は変わらない）。経路の一覧は `src/QuickER.Runtime/CLAUDE.md`「ドライバが返した値の型変換」。
- ガード：`SqlServerPlainQueryRuntimeTests`（発行 SQL を SqlClient の DiagnosticListener で捕まえ、Include の無い取得に `FOR JSON` が無いこと。並び替えなしの Take / Skip の `ORDER BY (SELECT NULL)` の補完。型の境界値〔double の最大値・最小の正規化数・負のゼロ・`0.1 + 0.2`・9 桁が要る real・double を包む値オブジェクト〕と NULL で、Include の無い `Query()`＝`GetAllAsync`＝Include のある取得のルートと子の値・`RowState`・Include していないナビゲーションと子が 0 件の Include の初期状態が一致すること）。mutation 実証済み。

## 式の翻訳（SQL Server）

共通の NULL 補償などは `src/QuickER.Runtime/CLAUDE.md`「式の翻訳と NULL 補償」。

- 空・空白の判定は `DATALENGTH(col) = 0`（`= ''` は末尾の空白を無視する）と、`col NOT LIKE N'%[^…]%'`（括弧の中は `char.IsWhiteSpace` の全文字を `NCHAR(n)` で連結）。TRIM の文字指定は 2017 以降で、docs の 2016 宣言に合わないため使わない。
- 固定長の列は値を空白で詰めて返すので、`EndsWith` は詰めた値で比べる（読み戻した値へ C# を当てた結果と同じにする。`RTRIM` は付けない）。
- LIKE のパターンは列の型の可変長形（nchar→nvarchar・char→varchar）で束縛する。固定長で束縛するとパターン自体が空白で詰められて一致しない。
- ガード：`TranslatorParitySqlServerRuntimeTests`（述語の表 `TranslatorParityPredicateRows` を実 SQL Server で C# の意味論と突き合わせる）。値オブジェクトの列の IN は `BackendParitySqlServerRuntimeTests`（ケース `value-object-in-null-semantics`）。mutation 実証済み（可変長束縛・空と空白の判定）。

## Save フックが書いた blob の版の読み直し

共通の規則は `src/QuickER.Runtime/CLAUDE.md`「Save フックが無制限バイナリ列を書いたとき」。

- `BinaryColumnWritten` のときだけ、Save の単一・複数とも `RowVersionCollector.RereadAsync` が、記録した行（保存が書いた行）の版を同じトランザクションで読み直す。`EntitySaveMetadata.SelectRowVersionByIdSql` を使い、行が無ければ文の版のままにする。
- 反映は従来どおりコミット後の `Apply`。
- SQL Server の blob の書き込みは 1 つの UPDATE なので、失敗した書き込みは何も変えず、保存はコミットできる（実測）。
- ガード：`BackendParitySqlServerRuntimeTests` のケース（`hook-sql-stamps-same-row`・`hook-sql-stamps-saved-parent`・`hook-without-writes-no-reread`・`hook-audit-row-no-reread`）、`SaveHookBinaryWriteSqlServerRuntimeTests`（blob を書いた後の版・読み直しの後でコミットを取り消すと版が読んだときのまま・失敗した書き込み 2 通り）。
