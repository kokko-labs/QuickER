# QuickER.Provider

このファイルの範囲は、DB 取込・型表記・差分と同期のうち、方言をまたいで守る規則。
方言ごとの事実（カタログの読み方・同期スクリプトの書き方・型カタログと型マッパーの値）は各方言の CLAUDE.md（`src/QuickER.Provider.SqlServer/CLAUDE.md` など 5 つ）に書く。

目次（大見出しは 3 つ）：

- 取込：DB のカタログから図を作るときの規則（取込警告・名前の衝突・取込の範囲の外の物・接続とコマンド）
- 型表記：型の綴りの正規化と比較、`SqlTypeText` の安全パターン、`TryFormat` が書き出さない型、方言切替の読み替え
- 差分と同期：`SchemaDiffService` → `SyncPlanner` → 方言の `ISyncScriptBuilder` の 2 段構造と、計画が守る順序・依存・警告、SQL の信頼境界

## 取込

### プロバイダの対称性

- 5 方言は同じ構造を持つ（SchemaImporter／`DdlGeneratorBase` 派生／型マップ／実 DB 統合テスト）。
  - 統合テストは SQL Server・PostgreSQL・MySQL・Oracle が Testcontainers、SQLite は実ファイル DB。
- 方言 SQL の説明コメントは SQL Server 版と同水準に揃える。
- スキーマインポータの基底クラス化は意図的に見送る（方言の差が大きい）。
- 共有部品は `ForeignKeyRelationshipBuilder`／`UniqueConstraintImportBuilder`／`SchemaTableEntry`。

### 取込の原則と取込の範囲

- 取込は「DB が宣言したとおりの型・制約だけを持ち帰り、持ち帰れなかったものは黙らせない」。
- 取込の範囲（方言ごとにどのスキーマを読むか）の正本は `docs/database.md`（.ja）の取込の範囲の節。
- 意味モデルが持たない物（パーティション・ドメイン型など）は、基底の形へ落として取り込み、取込警告で告げる。
- パーティション親（`PartitionDefinitionLost`）は SQL Server・PostgreSQL・MySQL・Oracle が出す。判定はどれもカタログの事実で行い、オプション文字列の部分一致にしない（方言ごとの判定は各方言の CLAUDE.md「取込」）。
- 無効化された制約は取り込まず、方言中立の `DisabledConstraintExcluded` で告げる（出すのは SQL Server と Oracle）。
  - 既存クエリの意味は変えず、告知専用の軽いクエリを分ける（除外の実効フィルタは既存クエリ側に残し、名指しだけを別クエリで拾う）。
  - 無効化された制約・テンポラル表・SQLite の PRAGMA の固定は `{SqlServer,Oracle,Sqlite}ImportFidelityIntegrationTests`、パーティションと SQLite の主キーの補正の固定は `{MySql,Sqlite}ImportFidelityIntegrationTests`。
- 型カタログが解析できない DB 型（`geometry`・`hierarchyid`・`sql_variant` など）は 5 方言とも `_ => Reference("string")` の安全側フォールバックへ落とし、`CSharpTypeInfo.IsFallbackType` を立てて生成時 Info 診断で名指しする。
  - 取込警告にはしない（PostGIS の `geometry` のように承知で使う型が取り込むたびに鳴って形骸化するため）。
  - 固定は `DialectTypeTableTests` のマッパーの行（`map-*`）・`CodegenCaseTests` の行 `code-fallback-type-column-is-string-property`・`CodegenCaseTests` の行 `diag-fallback-type-info-names-column`（Info 診断）。

### 計算列と生成列

- 計算列・生成列は `Column.IsComputed` を立てて取り込む。意味モデルは式を持たないので、式の喪失を `ComputedColumnExpressionLost` で告げる。
- 判定はカタログの事実で行い、語彙の部分一致にしない。方言ごとの判定は各方言の CLAUDE.md「取込」（SQL Server・PostgreSQL・MySQL・Oracle・SQLite）。
- 生成コードの書き込み除外と DDL の注意コメントは `src/QuickER.CodeGen.CSharp/CLAUDE.md`「計算列・生成列」。
- 固定は 5 方言の `*ImportFidelityIntegrationTests`。mutation 実証済み。

### 取込警告

- 器は `SchemaImportResult.Warnings`（`SchemaImportWarning`＝Kind / TableName / Subject / Detail の言語中立 record）。種別は `SchemaImportWarningKind`。
- 取り込んだ列の既定値の事実は `SchemaImportResult.ColumnDefaults` に持ち、図には保存しない。
- 文言は Provider の `SchemaImportWarningText` だけが持つ。
  - 取込は箇所ごとに 1 件を返し、`Group` が種別ごとに件数入りの見出しと箇所の項目へまとめる。
  - 図に入らないオブジェクトを外した種別（`SchemaImportWarningKinds.IsAggregated`）の項目は名前だけ、それ以外は箇所ごとの 1 文。
  - 表示側は飾りだけを付ける。GUI の `DbImportCommandService` は `ShowInformationDetails` に見出しと「  • 」の項目（警告ゼロなら `ShowInformation` の単文）、CLI の `quicker scaffold` は stderr へ見出しを `Cli_ImportWarningLine`（「Warning: 」）で包み、項目を「  - 」で字下げする。
  - 未知の種別は `ArgumentOutOfRangeException`。
- Provider の外に種別での分岐が無いことと、全種別 × 英日の文言は `SchemaImportWarningTextTests`（種別での分岐の走査ほか）と `DialectImportWarningTableTests`（全種別の文と見出し）が固定する。

### 範囲外を参照する外部キー

- 範囲外を参照する外部キーは親が図に無いのでリレーションにできない。除外して `ForeignKeyOutsideScope` で告げる。
- 範囲外の外部キーの告知は、取り込んだ子表の分だけにする（捨てた表の外部キーを名指ししない＝`TryGetExact` で引く）。
- 方言ごとの判定（MySQL の `= DATABASE()`・SQLite の参照先不在）は各方言の CLAUDE.md「取込」。

### 大文字小文字だけ違うテーブル名

- テーブル辞書は 5 方言共通で大文字小文字を区別しない（`SchemaTableEntry.KeyComparer`＝`SchemaNames` の規則に前後の空白の除去と SQL Server の `[schema].[name]` の角括弧の内側の空白を足したもの）。
  - 前後の空白だけ違うテーブル名も衝突として捨てる。
- 引用識別子などで `"Dup"` と `dup` が共存する DB では、後着を捨てて `TableNameCollision` で告げる。
  - 辞書の照合を序数一致へは変えない（`UniqueConstraintImportBuilder` が返す辞書も `OrdinalIgnoreCase` で、Ordinal 化しても一意制約の付け替えが残るため片肺になる）。
- 捨てるだけでは足りない。衝突して捨てた表の行（列・PK・説明・一意制約・FK）を素の `TryGetValue` で引くと、採用した表へ吸い寄せられて列が混ざる。
  - そのため全解決点（列・PK・説明・一意制約の `builder.Add` 前・FK の両端）で共有の `SchemaTableEntry.TryGetExact`（序数一致）を通す。
  - カタログが返す名前はどのクエリでも同じ実体名なので、序数一致まで求めても正当な行は落ちない。
- 生き残る側は並び順で決まるので、テーブルの列挙は照合順序に依らない並びにする（方言ごとの書き方は各方言の CLAUDE.md「取込」）。
- 固定は `{PostgreSql,Oracle,MySql,SqlServer,Sqlite}ImportFidelityIntegrationTests.Import_TableNamesDifferingOnlyInCase_KeepsOneAndWarns` と、捨てた表の範囲外の外部キー（Oracle・MySQL の `Import_OutOfScopeForeignKeyOfDiscardedTable_IsNotReported`・PG の `Import_OutOfScopeForeignKeys_AreReportedPerImportedTable`）。mutation 実証済み。

### 大文字小文字だけ違う列名

- 同じ表の中で大文字小文字か前後の空白だけ違う列名は、テーブル名と同じく後着を捨てて `ColumnNameCollision` で告げる。
  - `SchemaTableEntry.TryBeginColumn` が `SchemaNames` の規則で衝突を判定する。
- カタログの行から列を引く箇所は、すべて綴りまで一致の `TryGetColumnExact` を使う。
  - 4 方言（SQLite 以外）の PK・説明、5 方言が共有する一意制約（`UniqueConstraintImportBuilder.Attach`）と外部キーの両端（`ForeignKeyRelationshipBuilder.Build`＝子の列の外部キーの印も引き当てた列に立てる）。
  - 捨てた列の印・説明・制約は採った列へ混ざらない。捨てた列を含む一意制約・外部キーは作らずに黙る（捨てた列は `ColumnNameCollision` が名指しする）。
- カタログは構成列を表の列の綴りで返す。例外は SQLite の外部キーの親の列（`src/QuickER.Provider.Sqlite/CLAUDE.md`「取込」）。
- `ColumnsByName` は読み取り専用（書き込みは `AddColumn` だけ＝採用した列の綴りの記録を迂回させない）。
- 固定は `{PostgreSql,Oracle}ImportFidelityIntegrationTests.Import_ColumnNamesDifferingOnlyInCase_KeepsOneAndWarns`・`{Sqlite,PostgreSql,Oracle,SqlServer}ImportFidelityIntegrationTests.Import_ColumnNamesDifferingOnlyInCase_DoNotMoveConstraints`（共有の `CollidedColumnImportAssertions`）・対照の `SqliteImportFidelityIntegrationTests.Import_ForeignKeyParentColumnInOtherSpelling_IsImported` / `MySqlImportFidelityIntegrationTests.Import_ConstraintColumnsInOtherCase_AreImported`・`SchemaTableEntryNameTests`・`NameIdentitySqliteImportFuzzTests`。mutation 実証済み。

### ドットを含むテーブル名

- 名前を `SchemaQualifiedName.Split` で分けたときに元へ戻らないテーブルは取り込まない（図は最初の `.` でスキーマと表に分けて読むので、別の表として読まれる）。
- 判定は `SchemaImportTableNames` の 1 つだけ。方言が図に入れる名前を作る関数の結果と DB の（スキーマ, 表）を受けて判定する（方言の名前の規則を写さない＝名前を作る関数と判定が食い違わない）。
- 戻らない表は辞書に入れず `DottedTableNameExcluded` で名指しし、衝突の判定より先に見る。
  - `DefinitionKind` は null（名前の形の問題で、`TableNameCollision` と同じく図で表せない定義の種類に入れない）。
  - Subject は SQL Server だけスキーマ名で、ほかは空（空のスキーマ名を書かない文面へ振り分ける）。
- その表を参照する外部キーは `ForeignKeyToExcludedTable` で名指しする。
- 図で `スキーマ.表` と書いた表は同期の制約名の逆引きへ届くので、逆引きは表の所有者で絞る（「SQL の信頼境界」）。
- 固定は 5 方言の `*ImportFidelityIntegrationTests.Import_DottedTableName_IsExcludedAndReferencingForeignKeysAreReported`（共有の `DottedTableNameImportAssertions`）・`SqlServerImportFidelityIntegrationTests.Import_DottedNamesAcrossSchemas_ExcludeOnlyNamesThatReadAsAnotherTable`・`SqliteImportFidelityIntegrationTests.Import_TableNameContainingDot_DoesNotThrow`・`SchemaImportTableNamesTests`。mutation 実証済み。

### 出せない型表記の告知

- 取込は `SqlTypeText.IsSafe` を通らない表記（`"od;d"`・`weird;type`）を持ち帰り得る。取込は成功させたまま `ColumnTypeNotEmittable` で名指しする。
  - 黙って入れると、後で DDL・同期を叩いた瞬間に関係ない全テーブルごと止まる（入口の検証は図単位）。一番早く分かる場所は取込完了時。
- 検知は方言中立の `SchemaImportWarnings.DetectUnemittableColumnTypes` で、5 方言すべての取込の最後に掛ける。
  - どれか 1 方言で忘れると、その方言だけ遅れて露見する。SQLite は宣言型が任意のテキストで返る（`weird;type`・`a--b`）ので、PG 限定にすると最も起きやすい方言が漏れる。
- 固定は `PostgreSqlImportFidelityIntegrationTests.Import_ColumnTypeNotEmittable_IsReported` と `PostgreSqlSchemaImporterTests` の引用型名テスト。mutation 実証済み。

### 取り込んだ表記の再適用

- 取込が持ち帰った型表記は、その方言の DDL として再適用できなければならない（PostgreSQL の生の `numeric(10,2046)` のように再適用できない表記は、その図を DDL にも差分同期にも使えなくする）。
- 5 方言の `*DdlRoundTripIntegrationTests.ImportedSchema_RegeneratedDdl_CanBeReapplied` が「生 DDL を適用 → 取込 → DDL 再生成 → 再適用 → 再取込が 1 回目と一致（不動点）」を固定する（比較は共有の `SchemaReapplyAssertions`）。
  - 各方言の起点 DDL に型の癖（PG の負スケール・ビット長・配列・区間、Oracle の `NUMBER(10,-2)` と `VARCHAR2(50 CHAR)`、MySQL の値リスト型、SQL Server の max 長、SQLite の非数値引数型）を含める。
- 不動点の表明だけでは、取込が情報を落としても 1 回目と 2 回目がそろって落ちて一致してしまう。起点 DDL の宣言と突き合わせる表明（`SchemaReapplyAssertions.ShouldHaveColumnTypes`・主キーの順序の `ShouldHavePrimaryKeyOrder`）を併用する。

### コマンドのタイムアウト

- 取込・同期のコマンドは必ず `DbCommands.Create` で作る（5 方言の `*SchemaImporter`／`*SchemaSyncExecutor`）。
  - 生成箇所ごとに `CommandTimeout` を書く運用では、クエリを 1 本足したときに設定が静かに漏れる。
- タイムアウトの正本は `DbConnectionSettings.CommandTimeoutSeconds`（既定 60・`0` は無制限・負値は `ArgumentOutOfRangeException`）。GUI の接続プロファイルと CLI `scaffold --command-timeout` から流れる。
- 素の `connection.CreateCommand()`／`new XxxCommand(...)` が 1 つでも現れたら `SourceRuleTests` の規則 `schema-command-bypasses` が落ちる（10 ファイルが `DbCommands.Create` を使っていることは規則 `schema-command-helper-uses`）。

### 接続文字列の TLS 要求水準

- `DbSslMode` の「未指定」は接続文字列ビルダーへ代入しないことでしか表せない。
  - `NpgsqlConnectionStringBuilder`・`MySqlConnectionStringBuilder` はドライバ既定と同じ値を代入しても `SSL Mode=` を書き出す（実測）。既定値の代入で実装すると既存の接続文字列が黙って変わる。
  - PG・MySQL のファクトリは `ToXxxSslMode(...) is { } mode`（null＝代入しない）の形を崩さない。
- 未定義の列挙値は既定へ落とさず `ArgumentOutOfRangeException`（黙って null へ倒すと、検証を要求したつもりの設定が無検証へ降格する）。
- プロファイル JSON への永続化は名前（`JsonStringEnumConverter<DbSslMode>`）。整数で保存すると、列挙の途中へメンバーを足した瞬間に保存済みの値が別の水準へ読み替わる。旧整数 JSON は読める。
- 固定は `DbSslModeConnectionStringTests`（`Default_IsUnspecified_AndEmitsNothing`・SQL Server／Oracle／SQLite のバイト不変）と `ProfileStoreTests` の行 `tls-ssl-mode-is-persisted-by-name`（名前保存）・`tls-ssl-mode-integer-spelling-is-readable`（整数互換）。mutation 実証済み。

## 型表記

### 型表記の比較は取込の表記へ寄せる

- DB は宣言の綴りをそのまま覚えない（型の選択肢の `numeric(18,0)`〔SQL Server・PostgreSQL〕・素の `timestamp`〔PostgreSQL〕・`TIMESTAMP`〔Oracle〕・`TryFormat` の `decimal(p)`〔MySQL〕）。図・型の選択肢・`TryFormat` の綴りと取込の綴りを素で比べると、同期しても消えない ALTER COLUMN が出続ける。
- 方言ごとの `{SqlServer,PostgreSql,MySql,Oracle}TypeNotation.Normalize` が「その表記で宣言した列を取り込むと返る表記」の単一の正本。
  - 取込もこの関数を通す（取込の出力は不動点）。
  - 型の選択肢と `TryFormat` の綴りは変えない（UI と変換結果の表記を変えない）。SQLite は宣言を verbatim に返すので恒等。
- 差分（`SchemaDiffService`）と同期計画（外部キー両端の型照合＝`SyncPlanner.HasPostSyncTypeMismatch`）は `SyncDialectCapabilities.AreSameType`（`NormalizeTypeNotation` を両側へ通して `DbTypeText.AreEquivalent`）で比べる。
  - 差分・同期計画から `DbTypeText.AreEquivalent` を直接呼ばない。素の比較だと消えない ALTER COLUMN が戻り、外部キーでは綴りだけ違う同じ型を「食い違う」と判定して外し直しを止める。
- 方言ごとの規則は各方言の CLAUDE.md「型表記」。
- 固定は 4 方言の `*TypeNotationIntegrationTests`（共有の `TypeNotationClosure`＝選択肢・`TryFormat` の代表出力・指摘で食い違った綴りで同期→再取込→「正規化した図の表記＝取込の表記」→AlterColumn 0）と各 `*TypeNotationTests`、同期計画の期待の行 `diff-type-normalized-per-dialect-*`・`diff-type-different-after-normalization-is-alter-column-*`・`plan-type-mismatch-equivalent-notation-uses-dialect-normalization`。mutation 実証済み。

### SqlTypeText の安全パターン

- 型は識別子でも文字列リテラルでもなく素通しで補間するしかない。`SqlTypeText.IsSafe`／`Validate(ErDiagram)`／`Validate(SyncPlan)` を 5 入口（`DdlGeneratorBase.Build`・`SqliteDdlGenerator.Build`・`SyncScriptBuilderBase.Build`・`OracleSyncScriptBuilder.Build`・`SqliteSyncScriptBuilder.Build`）で通し、不一致は「テーブル.列 = 型」を名指しして `InvalidOperationException`。
- 同期側は型を書き出す文（表を作る文の定義・列を足す文と変える文の新しい定義・再構築の合成後定義）だけを検証する。削除する表・列の live の型まで見ると、取込が持ち帰った安全でない型の表を同期で消せない。MySQL の列順の移動（`SyncPlan.Reorders`）は実行時に live の列定義を読み、型を書き出さないので検証しない（見ると、安全でない型の列を持つ表の並べ替えを止める）。
- ホワイトリストは構造的に作る（キーワード許可集合にしない）。
  - `;`・引用符・バッククォート・`\`・コメント記号・改行を通さない。語の並び＋括弧引数（`(max)`・`(n)`・`(p,s)`・Oracle の単位語）＋後続語＋末尾 `[]`＋MySQL の値リスト（`enum('a','b')` だけ引用符を許す）を通す。
  - キーワード許可集合にすると、取込が verbatim で持ち帰る実在型（`LONG RAW`・`SDO_GEOMETRY`・`VARYING CHARACTER(255)`・別名型の非 ASCII 名）を弾き、実 DB でのデータ欠落へ化ける。
- 括弧引数は「数値・語・`*`」を通す。語は `[\p{L}_][\p{L}\p{Nd}_]*` で、通す文字の集合は「語・数字・カンマ・`*`」で閉じる。
  - 数値だけに絞ると PostGIS の `geometry(Point,4326)`・`geography(MultiPolygon)` と Oracle の `NUMBER(*,2)` が弾かれ、その 1 列のせいで図全体の DDL・同期が止まる。
  - 配列表記 `[]` は多次元のため繰り返しを許す（`integer[][]`）。
- 語の構成文字から `#` を外す（MySQL の `#` は行末コメント＝`int#` が通ると `MODIFY COLUMN` の後続句が黙って消える）。
- 前後に空白や改行が付いた表記は拒否する（`IsSafe` が Trim 後に判定し生成側が生を補間する不一致を塞ぐ。`"\nGO\n"` は Trim 後に `GO` に見えて通り、SQL Server のバッチ分割 `^\s*GO\s*$` を誤作動させ得る）。空・空白のみ（未設定）は safe。
- 2 語目以降の列定義句キーワード（`NOT`・`NULL`・`DEFAULT`・`PRIMARY`・`KEY`・`REFERENCES`・`CHECK`・`GENERATED`・`IDENTITY`・`COLLATE`・`UNIQUE`・`ON`・`AS`・`AUTO_INCREMENT`・`AUTOINCREMENT`・`INVISIBLE`・`VISIBLE`・`CONSTRAINT`）を拒否する。判定は括弧の中を除いた語単位。
  - 型は列定義の途中へ補間されるので、`int NOT NULL` や `int AS (price)` は構文エラーにならず図の宣言と静かに食い違う。
- 長さの上限 200 は値リスト型（MySQL の `enum`／`set`）に掛けない（十数個の値の実在の enum で超え、その 1 列のせいで図全体が止まる）。
  - `IsSafe` の順は「前後の空白の拒否 → 値リストなら true → 長さ上限 → 安全パターン＋句の密輸判定」。`ValueListTypePattern` は線形の正規表現なので長さで先に切らなくてよい。
- 固定は `DialectTypeTableTests`（安全性の行 `safe-*`＝取込の実在表記の全数照合・PostGIS の表記・`NUMBER(*,2)`・`integer[][]`・句の密輸、5 方言カタログの全表記の `catalog-safe-all-data-types`）・`SqlTypeTextTests`（長い enum）・`DialectDdlTableTests`（行 `ddl-type-unsafe-data-type-is-rejected`・`sync-add-table-unsafe-data-type-is-rejected`・`sync-other-alter-column-unsafe-data-type-is-rejected`・`sync-other-drop-table-with-unsafe-data-type-is-allowed`＝DDL と同期の入口・型を書き出さない表の削除）と `PostGisImportIntegrationTests`（実 PostGIS コンテナで取込→図全体の DDL 生成→再適用）。mutation 実証済み。

### TryFormat が書き出さない型

- `TryFormat` が true を返すと、対象 DBMS の切替が「DDL の適用で失敗するか意味の違う型」を変換済みとして黙って作る。書き出せない型は false を返して変換不能一覧に載せる。読む側（`TryParse`）と書く側は対で考える。
- 方言をまたぐ規則は次の 4 つ。各方言の上限の値は各方言の CLAUDE.md「型表記」。
  - 負のスケールは PostgreSQL・Oracle だけが書き出す（ほかの 3 方言の `TryFormat` は `Scale < 0` で false）。固定は `DialectTypeTableTests` の `format-token-negative-scale-decimal`（5 方言）。
  - 長さを要する型（`CanonicalTypeToken.HasLengthArgument` が真の種別）で `Length` が null の正規型は、SQL Server・MySQL・Oracle が書き出さない（PostgreSQL・SQLite は長さ無しが正当）。種別の集合は `CanonicalTypeToken.LengthKinds` が単一の正本で、方言側へ書き写さない。
  - 精度なし・スケールありの Decimal（`decimal(*,s)`）は Oracle だけが書き出す。精度なしの Decimal(null,null) は SQL Server・MySQL が書き出さない。
  - 方言の上限を超える長さ・精度は書き出さない（SQL Server でよく使う `nvarchar(4000)` が Oracle の `NVARCHAR2(4000)` になると ORA-00910）。
- 長さ無しの副作用 2 つは受け入れる：`CanonicalTypeTokenAttacher` が verbatim 併記を付ける（往復は保たれる）・`CSharpReverseParser` が警告つきでトークン文字列を列の型に採る。
- 型トークンは `decimal(*,s)` で精度なしを運ぶ（`decimal(*)` は拒否）。スケールを落として `decimal` と刻むと、C# リバースで素の `NUMBER` へ戻り整数の制約が消える。`*` は 38 ではないので、`TryFormat`・型トークン・C# リバース・クエリのパラメータで Decimal(38,s) へ読み替えない。
- 固定は `DialectTypeTableTests`（5 方言 × 種別の書き出しの行 `matrix-format-*`・パラメータの型の書き出しの行 `matrix-format-param-*`（上限ちょうどは `matrix-format-param-at-limit-from-*`）・`format-token-precisionless-scaled-decimal`・長さ無しの `format-token-lengthless-*`・各方言の上限の対〔網羅の照合が、上限を持つ種類 × 方言ごとに「ちょうど」と「1 つ超え」を必須にする〕）・`LengthlessTypeFormattingTests`（長さ無しの verbatim 併記）・`FormatRoundTripTests`（規則の行 `csharp-rule-lengthless-token-*`・`csharp-rule-precisionless-scaled-token-*`／形の行 `csharp-bare-decimal-*`・`csharp-oracle-precisionless-*`）・`CSharpReverseBareDecimalTests`・`{SqlServer,MySql,PostgreSql,Oracle}DialectTypeRealDbTests`（上限の対を実 DB で適用して取り込み直す。SQLite は上限を持たない）。mutation 実証済み。

### クエリのパラメータの型解決

- `QueryParameterTypeResolver` は列を作らないので列の制約を当てない。
- 可変長 3 種別（String・AnsiString・Binary）の長さ無しは「無制限（-1）」として解決し直す（巻き込むと動いているクエリが生成時エラーへ落ちる）。
  - 固定長 3 種別を同じフォールバックへ入れない。`FormatLength(-1)` は機械的に `(max)` を付けるので、SQL Server が実在しない `nchar(max)` を返し、Oracle は長さ無しの `NCHAR`、MySQL は固定長が `longtext` に化ける（実測）。
- 列の上限は `ITypeCatalog.TryFormatParameterType`（既定は `TryFormat`）で外す。MySQL・PostgreSQL だけが上限の判定を外し、長さ無し・負のスケール・精度なしの規則は残す。SQL Server・Oracle は上限の判定が固定長の max を書かない規則と一体なので分けない。
  - 外すと、いま解決できている `string(20000)`（MySQL）・`decimal(1001,0)`（PostgreSQL）のトークンが生成時エラーへ落ちる。
- 固定は `LengthlessTypeTokenQueryGenerationTests`・`DialectTypeTableTests`（パラメータの書き出しの行 `format-param-*` と型解決の行 `param-*`）。mutation 実証済み。

### スケールの符号

- スケール引数の符号許可は 3 箇所同時（`PostgreSqlTypeCatalog.TryParseScaleArg`・`OracleTypeCatalog.TryParseScaleArg`・`CanonicalTypeToken.TryParseScaleArg` だけが `NumberStyles.AllowLeadingSign`）。長さ・精度を読む `TryParseTypeArg`／`TryParseArg` は `NumberStyles.None` のまま（`varchar(-5)`／`NUMBER(-5)` は変換不能）。
  - 片肺にすると「取込は通るがトークンの読み戻しで落ちる」形で壊れる（`numeric(10,-2)` を取り込んだ列の `[DbColumnMeta("decimal(10,-2)")]` を C# リバースが解釈できない）。
- 型マッパー 5 方言は負のスケールを意図的に読まない（`PrecisionScaleRegex` は符号なし）。精度・スケールとも null として扱い、生成時 Info（`CodeGen_Info_NegativeScaleColumns`）で名指しする。
  - 値オブジェクトの桁数検証を付けないのは、負のスケールが「DB に丸めさせる」ために書く型だから（重大度も Info）。
  - 型マッパーへ符号を足すなら、Oracle の `MapNumber` も同時に直す（`src/QuickER.Provider.Oracle/CLAUDE.md`「型表記」）。
  - 検出は型名を見ず、括弧の第 2 引数が負であることだけで判定する。
- 固定は `DialectTypeTableTests`（PostgreSQL・Oracle の負のスケールの解析と往復の行・型トークンの `decimal(10,-2)` の行）・`CodegenCaseTests` の行 `code-negative-scale-column-is-decimal-property`。mutation 実証済み。

### 方言切替の精度の読み替え

- 精度なしの Decimal を 38 桁へ読み替えるのは方言切替だけ。この読み替えを `TryFormat` へ移さない（型トークン・C# リバース・パラメータの解決まで黙って 38 桁へ化ける）。
- `DiagramTypeConverter.CreatePlan` は、スケールが 0 の精度なしの Decimal（Oracle の `INTEGER`／`SMALLINT`／`NUMBER(*,0)`）を移行先が `TryFormat` できないときに限り、`DiagramTypeConverter.CappedPrecision`（38）桁の Decimal として書き出し直し、`ColumnTypeConversion.PrecisionCapped` を立てる（SQL Server・MySQL は `decimal(38,0)`・PostgreSQL は `numeric(38,0)`・SQLite は `DECIMAL` のままで印なし）。
  - 0 以外のスケールの `NUMBER(*,s)` と精度もスケールも無い `NUMBER` には桁を足さない。
  - GUI の切替は印の列を確認ダイアログの「注意つきで変換した列」の節（`TypeConversion_PrecisionCappedHeader`・変換できない列の節の次）で名指しし、この節だけでも確認を出す（Undo は型の変換と同じ 1 履歴）。
- 固定は `DialectTypeTableTests` の方言切替の行（`convert-x-oracle-*`＝精度の上限なしの整数と NUMBER の桁）・`DiagramTypeConverterTests.CreatePlan_PrecisionCap_OnlyForPrecisionlessIntegerDecimal`・`MainViewModelTargetDbmsTests` の `ChangeDbms_OracleUnlimitedInteger_ConfirmsCappedColumnAndUndoes`／`ChangeDbms_PrecisionCappedSection_FollowsUnconvertedSection`／`ChangeDbms_OracleUnlimitedInteger_CancelKeepsType`・docs の `unconvertible-types` の表の照合・`{SqlServer,MySql,PostgreSql}TypeNotationIntegrationTests`（変換の出力 `TypeNotationClosure.ConvertedOracleInteger` で同期→再取込→差分 0）。mutation 実証済み。

### 型マッパーは読み取る経路が読み書きできる型へ写す

- 型マッパーは、その列を読み取る経路が読み書きできる CLR 型へ写す。違う型で受けると読み取り自体が `InvalidCastException` になり、行の組み立ては全列をまとめて読むので、その列を持つ表が丸ごと使えない。
- 読み取る経路は方言で違う。
  - SQL Server・SQLite：QuickER 版 Repository（ADO）がドライバから読むので、ドライバがその列を返す CLR 型（`GetFieldType`）へ写す。SQLite はストレージクラスで返るが、Microsoft.Data.Sqlite がマッパーの型へ変換して読む（REAL アフィニティだけは倍精度の `double` に限る）。
  - PostgreSQL・MySQL・Oracle：QuickER 版 Repository を持たず、生成した EF Core のコードとそのプロバイダが読む。プロバイダがマッパーの型から選ぶ既定の対応で読み書きできる型へ写す。ドライバの型と違ってよい（Oracle の整数の `NUMBER(p)`・PostgreSQL の `time`・`timestamptz` など）。
  - 既定の対応では書けない列（PostgreSQL の `date`・`timestamp`・`timetz`・`json`・`jsonb`・`xml`・`money` とその配列、SQL Server の `money`・`smallmoney`）は、マッパーが EF Core に教える列の型（`CSharpTypeInfo.EfCoreColumnType`）を入れ、生成 DbContext がその列にだけ `HasColumnType` を出す（`src/QuickER.CodeGen.CSharp/CLAUDE.md`「EF Core に教える列の型」）。
- 確かめた EF Core のプロバイダは、Integration が持つ `UseOracle`（Oracle.EntityFrameworkCore）・`UseMySQL`（MySql.EntityFrameworkCore＝ドライバは MySql.Data）・`UseNpgsql`・`UseSqlServer`・`UseSqlite`。MySQL の Pomelo（ドライバは MySqlConnector）は確かめていない。
- MySQL の `timestamp` は `DateTime`、`char(36)` は `Guid` へ写す（両ドライバの型。`DateTimeOffset` で読むと MySql.Data が小数秒を落とし、`char(36)` を `string` で読むと UUID の値の行が読めない）。どちらも正規型（DateTimeOffset・fixedstring(36)）と違う C# 型なので、型の表の宣言のセルの例外として宣言する（`DialectTypeRows.MatrixShared.cs` の `MapTypeExceptions`。docs の csharp-types の表と同じ行）。
- 方言ごとの写し方は各方言の CLAUDE.md「型表記」。固定は `DialectTypeTableTests` のマッパーの行（`map-*`・宣言のセルの全表記を流す `matrix-map-*`＝5 方言のマッパーが同じ「種類 → C# の型」の表に従う）と、同じ行を実 DB で流す `*DialectTypeRealDbTests`（手順は `DialectTypeRealDbRows`。ドライバの手順＝`GetFieldType` とマッパーの一致、EF Core の手順＝生成 DbContext と同じ構成のモデルでマッパーの型として読み、書いて読み戻す。列の型を教える列は同じ値を `HasColumnType` で当てる。読み書きできない組を残すときは `GapReason` と今の症状を書き、症状が出なくなると赤になる。今は残っていない）。DB に触れない宣言の照合は `DialectTypeRealDbTableTests`。

## 差分と同期

### 2 段構造と方言の宣言

- スキーマ同期は「`SchemaDiffService.Compute`（差分検出・方言中立）→ `SyncPlanner.BuildPlan`（選択済み差分から方言中立の `SyncPlan`）→ `ISyncScriptBuilder.Build`（方言別の SQL）」の 2 段。
- `SyncPlanner.BuildPlan` は、計画器（`Sync/StepPlanner.cs` の `StepPlanner.Plan`）が作った文の並びの計画（`SyncPlan`）をそのまま返す（「逐次 DDL の組み立て」「計画の文と由来と対」）。方言ビルダーと入口の検証は、その文を読む（「文を読む方言ビルダー」）。
- 文で表せない項目（差分が必ず持たせるメンバーの無い項目）を渡すと、`BuildPlan` は項目の種類と無いメンバーを名指しした `InvalidOperationException` で断る（差分〔`SchemaDiffService.Compute`〕が作る項目はどれも文で表せる）。
  - 壊すと：項目が黙って計画から落ちるか、方言ビルダーの奥で別の例外になる。
  - ガード：期待の行 `plan-item-without-required-member-is-refused`・能力の組の網の関係 N4（製品が文で表せない計画を理由つきで断る＝違反の頭 `relation N4: product plan:`。`tests/QuickER.Tests.Integration/Dialects/SyncPlanFuzz/CLAUDE.md`「能力の組の網」）。
- 方言差は `SyncDialectCapabilities` でプランナーと差分へ宣言する。全プロパティは `required` で既定値を持たない（足した能力を書き忘れた方言が旧来の値で黙って動かないため）。
  - 主なもの：`SupportsAlterColumn`・`SupportsForeignKeyAlter`・`SupportsDescriptions`・`PersistsForeignKeyConstraintNames`・`ColumnReorder`・`AlterColumnRequiresForeignKeyRebuild`・`AlterColumnRequiresKeyConstraintRebuild`・`ForeignKeyRequiresLeadingIndex`・`TransactionalDdl`・`ReferencedKeyRequiresSameColumnOrder`・`SetDefaultRequiresColumnDefault`・`SupportsComputedColumnTypeChange`・`SupportsUniqueConstraintOnPrimaryKey`・`SupportsOnUpdateAction`・`SupportsSetDefaultAction`・`ChecksDefaultOnColumnTypeChange`・`SupportsSchemaQualifiedTableNames`・`ForeignKeyAllowsStringLengthDifference`・`ForeignKeyAllowsIntegerWidthDifference`・`TypeKindChangeRequiresEmptyColumn`・`PrecisionDecreaseRequiresEmptyColumn`・`AddNotNullColumnRequiresEmptyTable`・`AlterColumnBlockedByComputedColumn`・`ConstraintNameScope`・`AllowsDuplicateForeignKeyDefinitions`。
  - 能力の値と扱いの表のマスの一致は `CapabilityTreatmentParityTests` が照合する。方言ごとの値の一覧は `docs/database.md`（.ja）の方言の表。
- 逐次 DDL の方言（SQL Server・PostgreSQL・MySQL・Oracle）は差分をそのままセクション化する。SQLite はテーブル再構築方式（「テーブル再構築の合成」）。
- 列順の同期は SQLite（再構築へ畳む）と MySQL（`SyncPlan.Reorders`）だけ。選択可能な `ReorderColumns` 項目（既定未選択）として出し、ほかの 3 方言（`ColumnReorder=None`）は選択不可の案内項目だけ。
  - 検出は追加・削除を除いた共通列の相対順序の比較。真ん中への列追加だけでは差分にならず、列は末尾に付いて次の同期で並びの差分が出る。
- 実行失敗後は差分を自動再計算する（部分適用の後に古い差分を表示し続けない）。

### live の状態と識別の型

置き場所は `src/QuickER.Provider/Sync/State/`（名前空間 `QuickER.Provider.Sync.State`・internal）。

- 同期の計画（`SyncPlanner`）は、DB の今の図（live の表・リレーション）を `LiveSchema` からだけ読む。
  - `LiveSchema.From(context)` を `BuildPlan` の入口で 1 回だけ呼び、各段へ渡す。持つのは、取込の並びの表とリレーション（`Entities`・`Relationships`）・表を名前で引く索引（`Tables`＝差分と同じ `NameLookup`）・親子の表と列ペアまで解決できた外部キー（`ForeignKeys`＝`ForeignKeyColumnPairResolver.ResolveAll`）・その組の集合（`ForeignKeyPairs`）・外部キーの実効名（`ForeignKeyNames(limit)`＝`ConstraintNames.ResolveForeignKeyNames`。上限ごとに覚える）。
  - 計画の中で文脈の `LiveEntities`・`LiveRelationships` を直接読まず、`ForeignKeyColumnPairResolver.ResolveAll` を呼び直さない。段ごとに索引・一覧を作り直すと、表の引き方・解決できない外部キーの落とし方・実効名の求め方が段ごとに割れる。
  - live の表・リレーションから決まらない入力（索引・トリガー・`CREATE TABLE` の原文・列の既定値・取込警告・ビューなどの判定器・作り直しを止める物）は `SyncPlanContext` から読む。
- 計画が表・列・制約を照合する識別は `SchemaIds.cs` の型で、各型の `Of` で作る（位置指定の record のコンストラクターは受け取った値を畳まずに持つので、畳む前の名前を直接渡さない）。名前はどれも `SchemaNames.Key` で畳んで序数で比べる。
  - `ColumnId`：表と列。成分ごとに持つので、表名と列名の境目をずらした名前どうしは等しくならない。
  - `ForeignKeyPairId`：子の列と親の列の 1 組（向きを見る）。
  - `UniqueConstraintId`：表・DB の名前・列集合（列の並びは見ない）。名前の無い制約（null）は空文字列として持つ（「一意制約の照合キーと付け直し」）。
  - `ForeignKeyId`：子の表・親の表・列ペアの並び（宣言順）・実効名・参照アクション（「外部キーの暗黙の外し直し」）。
  - `SyncPlanner` の `ColumnKey`・`ForeignKeyPairKey`・`UniqueConstraintKey`・`ForeignKeyKey` は、項目・モデルからこれらの型を作る入口。
- ガード：`SourceRuleTests` の規則 `sync-live-schema-single-source`（`SyncPlanner.cs` と `Sync/` のフォルダ〔識別の型・同期後の状態・選んだ項目を外す規則・計画の文〕の、`LiveEntities`・`LiveRelationships` のメンバーの読みと `ForeignKeyColumnPairResolver.ResolveAll` の呼び出し。例外は無い。`LiveSchema.cs` は範囲から除く（文脈から読む 1 か所）。自己検査の行 `sync-live-schema-single-source-forms`・`sync-live-schema-single-source-inactive-branch`）。識別の等値と live の状態の中身は、同期計画の期待の行・能力の組の網・総当たりが計画の結果として見る。読みの限界：live の表の一覧を別の名前の引数で受け取って索引を作り直す形は規則が拾わない。

### 同期後の状態と時点

置き場所は `src/QuickER.Provider/Sync/State/PostSyncSchema.cs`（internal）。

- 同期の計画が同期後の事実（live の図に、項目の並びを当てた結果）を求める場所は `PostSyncSchema` の 1 か所。`PostSyncSchema.From(時点, 項目の並び, liveSchema, capabilities)` で作り、材料は初めて読まれたときに求めて覚える。
  - 材料：消える表・列（`Dropped`）・足す表（`AddedTables`）・主キーの変更（`PrimaryKeyChanges`）・同期後の主キー（`PrimaryKeyOf`）・外す一意制約（`DroppedUniqueConstraints`）・同期後の一意制約（`UniqueKeys`＝構成列の並びつき、`UniqueColumnSets`＝列集合）・同期後の NULL 許容（`AllowsNull`）・型が決まる列（`TypeDecidingColumns`）・項目が外す外部キー（`DroppedForeignKeys`）・列の名前の集合が変わる表の同期後の列名（`ChangedTableColumnNames`）。
  - 受けた項目の並びは、作るときに写して持つ。作った後に元の並びへ項目を足しても中身は変わらない。項目を足した後の事実が要る段は、足した後の並びから作り直す。
  - 計画の前半（`SyncPlanner.PrepareSelection`＝除外の周・作り直しの展開・同期後の状態の差の項目）が、選んだ項目の最終形（`PlanStage.Selected`）の `PostSyncSchema` を 1 つ作り、計画の事実（`AssessmentFacts.PostSync`）として組み立て・評価・前寄せと前後の名指しが共有する。作った後に選んだ項目の最終形へ項目を足さない（外し直しの文は組み立てが文として足す）。
- 計画器の「同期後」は 1 つの時点ではない。時点（`PlanStage`）は計算を変えない名札で、どの段まで項目を足した並びから求めたかを呼び出しに書く。
  - 時点は 6 つ：除外の周（`ExclusionRound`）・その作り直しを展開した並び（`ExclusionRoundExpanded`）・展開と主キーの NOT NULL 化の後（`Expanded`）・端が消える外部キーの削除を足した後（`WithVanishingForeignKeyDrops`）・構成列が消える一意制約の削除を足した後（`WithVanishingUniqueDrops`）・選んだ項目の最終形（`Selected`）。
  - 除外の周の 2 つの時点（`ExclusionRound`・`ExclusionRoundExpanded`）の状態は、周の状態（`Sync/Selection/` の `SelectionState`）が並びごとに 1 つずつ作り、選んだ項目を外す規則が共有する。規則は自分で `PostSyncSchema.From` を呼ばない（「除外の周」）。
  - 時点で答えが変わる材料は 3 つ。外す一意制約は、同期後の状態の差の項目を足す各段が自分の足す前の並びを見る。同期後の一意制約は、依存の除外だけが暗黙の一意制約の削除を足す前の並びから求める。項目が外す外部キーは、選んだ項目の最終形の並びでは、選んだ削除・作り直しの展開と端が消える外部キーの暗黙の削除を含む（組み立てと評価の規則は「同じ計画が明示に外す外部キー」の意味で読む。外し直しの外部キーは組み立てが文で足すので含まない）。
  - ほかの材料（消える表・列・足す表・主キーの変更・NULL 許容・型が決まる列）は、どの時点の並びから求めても同じ答えになる（後の段が足す項目がこれらを変えない）。
  - 時点を 1 つへそろえない。そろえると、上の 3 つの材料を読む段の答えが変わる。
- `PostSyncSchema` へ寄せていない求め方が、依存の除外（`ExcludeItemsRequiringUnselected`）のローカルに 4 つある：主キーの変更の辞書（構成列が同期後に在る変更だけ・同じ表は先の項目）・足す表の集合（`Entity` の有無を問わない）・`RequireColumns`（選んだ削除を引かない）・`RemainingLiveUniquesOn`（消える表・列を引かない）。「前提が同期後に在るか」を問う別の求め方で、正本へ寄せると計画が変わりうる。再構築の方言の表の合成（「テーブル再構築の合成」）も `PostSyncSchema` を通さず、作り直す表ごとに live の表へ項目を当てる。
- 計画の文は、共有する状態が持つ項目の並び（`PostSyncSchema.Items`）から組む（逐次 DDL の組み立て `SequentialScheduler`・再構築の合成）。状態を作る位置を前へ動かすと、その後に足した項目が計画から消える。主キーが置き換える一意制約の削除（Oracle）がこの形で消えると、警告だけが残り主キーの付与が断られる。
- ガード：`SourceRuleTests` の規則 `sync-post-sync-schema-single-source`（Provider のプロジェクトの全体の `PostSyncSchema.From` の呼び出しを「含むメソッドかプロパティ: 時点」、ほかの静的なメソッドの直接の呼び出しを「含むメソッド: メソッド名」で集め、理由つきの一覧と照合する。一覧に無い時点から求め直すと赤。除外の周の 2 つの時点は `SelectionState` の 2 つのプロパティだけを一覧に載せるので、規則のファイルで求め直しても赤。時点を `PlanStage.X` の形で書かない呼び出しは必ず名指しする。自己検査の行 `sync-post-sync-schema-single-source-forms`・`sync-post-sync-schema-single-source-inactive-branch`）。材料の中身は、同期計画の期待の行・能力の組の網・総当たりが計画の結果として見る。共有する状態を作る位置は、同期計画の総当たりの静的な規則 W2（`SyncPlan/CapabilityNet`。主キーが置き換えると名指しした一意制約が主キーの付与より前に外されている＝`tests/QuickER.Tests.Integration/Dialects/SyncPlanFuzz/CLAUDE.md`「静的な規則」）が DB なしで見る。読みの限界：`using static`・別名の `using` で受け手の名前を変えた呼び出しは規則が拾わない。

### 生成 SQL の固定文

- DDL 出力の先頭コメントは `-- DDL auto-generated by QuickER`（`DdlGeneratorBase`／`SqliteDdlGenerator` で同一文言）。
- 生成 SQL に乗る固定文（DDL ヘッダ・生成日時行・多対多コメント・注意コメント・同期スクリプトのセクション見出し・スキップコメント）は英語が正本。`DialectDdlTableTests` の全行に掛かる自動の英語の照合（入力に CJK が無ければ出力に CJK が無い）が混入を検知する。

### 制約名の正本と識別子の分割クォート

- 制約名の安全化と、名前を持たない FK・PK の既定名（`FK_{子}_{親}`／`PK_{テーブル}`）の正本は Model の `ConstraintNames`。方言側に安全化の写しを置かず、DDL・同期・`TableConstraintLineBuilder`・`UniqueConstraintNaming` が直接呼ぶ。
  - Model に置くのは、Provider を参照しない MCP のツールホストと共有するため。クォートは方言ごとに本当に違うので各方言に残す。
- 名前の無い外部キーの名前は、Model の `ConstraintNames.ResolveForeignKeyNames` が図全体から決める（同じ組の 2 本目は子の列名を足す）。DDL（基底と SQLite）・差分のシグネチャ（`ConstraintNames.EffectiveForeignKey`）・同期スクリプト（`SyncScriptBuilderHelper.ForeignKeyNameToAdd` が差分の載せた名前を優先）が同じ名前を使う。規則の本体は `src/QuickER.Model/CLAUDE.md`「制約名の組み立て（ConstraintNames）」。
  - どれか 1 つでもリレーションの名前から求め直すと、2 本目だけが DDL と違う名前になり、差分が偽の追加・削除を出し続ける。
  - 固定は `UnnamedForeignKeyNamingTests`（5 方言の DDL・同期スクリプト・作り直し）・期待の行 `diff-fk-unnamed-matches-default-named-live`・`diff-unnamed-fk-same-pair-twice-matches-live-named-by-ddl`・`diff-unnamed-fk-second-referential-action-change-replaces-under-same-name`・`diff-unnamed-fk-many-to-many-first-matches-live-with-default-name`・実 SQL Server の行 `realdb-fk-unnamed-relationship-no-diff-after-ddl`・`realdb-fk-unnamed-same-pair-ddl-applies-no-diff`・`realdb-fk-unnamed-many-to-many-first-ddl-uses-default-name`・`realdb-fk-unnamed-many-to-many-first-no-diff-with-default-named-db`。
- 各方言の Identifier はドット付きテーブル名を 5 方言対称に `"a"."b"` へ分割クォートする。SQLite での帰結は `src/QuickER.Provider.Sqlite/CLAUDE.md`「既知の割り切り」。

### SQL の信頼境界

- テーブル名・列名・制約名と `Column.DataType` は任意の文字列（GUI 入力・取込・AI 経由）。方針は「生成前診断（名前の改行・制御文字のみ Error）＋出力側は方言ヘルパーへ集約」。C# 側のエスケープは CodeGen.CSharp の担当。
- 動的 SQL の二段エスケープ：制約名不明の PK・FK の削除は動的 SQL の文字列リテラルの中（`sp_executesql` へ渡す `N'…'`・`DO $$ … EXECUTE '…'`・`PREPARE`＋`CONCAT`・`EXECUTE IMMEDIATE '…'`）で組み立てるので、クォートだけでは名前の `'` が外側のリテラルを閉じる。`{SqlIdentifier,PgIdentifier,MySqlIdentifier,OracleIdentifier}.QuoteForDynamicSql`（クォート＋リテラル用 `'` 二重化）を通す。
  - SQLite は再構築方式で動的 SQL を持たないので、このヘルパーを持たない（対称性の意図的な欠落）。
- 実行時にカタログから読んで動的 SQL へ埋める値（旧主キー名・名前の分からない外部キー名）は図の外から来るので、必ず DB 側の識別子クォート関数を通す。括弧・引用符で挟むだけの連結へ戻すと、`]` や `"` を含む正当な名前で同期が失敗する（QuickER 自身も表 `a"b` の主キーを `PK_a"b` と名付ける）。
- 制約名の逆引きは、ALTER TABLE と同じ 1 つの表（所有者）に絞る。テーブル名だけで引くと別スキーマの同名テーブルの制約名を拾う。表が無ければ何もしない。
- 方言ごとの書き方（SQL Server の `QUOTENAME`・PostgreSQL の `quote_ident` とドルタグ・MySQL の 3 つの面・Oracle の所有者）は各方言の CLAUDE.md「同期スクリプト」。
- `--` コメント行のサニタイズ：コメント行へ埋める名前は `SqlComment.Sanitize`（改行・制御文字 U+0000〜U+001F・U+007F・行区切り U+0085／U+2028／U+2029 を空白 1 つへ・長さ不変）を通す。
  - 経由するのは FK・一意制約のスキップコメント（`SyncScriptBuilderHelper`）・主キー非対応コメント（`SyncScriptBuilderBase`）・SQLite の対象外コメントと説明コメントと再構築の見出し・MySQL の列順見出し・多対多コメント（`DdlGeneratorBase` と `SqliteDdlGenerator` の両方）。Gui の `ExportTextSanitizer.Sanitize` は同じ実装へ委譲する。
  - 埋め込み箇所の漏れは `SourceRuleTests` の規則 `sql-comment-unsanitized-holes` がソース上で全数検査する（`--` で始まる文字列リテラルを含む文の補間穴が `SqlComment.Sanitize` か、理由つきの既知の一覧〔ファイル・式・件数〕のどれか）。文は「リテラル・コメントの外の `;`」で区切り、穴はリテラルの中身だけから抜く。
- 名前の入口検証：`SqlNameText.IsSafe`／`Validate(ErDiagram)`／`Validate(SyncPlan)` を `SqlTypeText.Validate` と同じ 5 入口へ並べ、テーブル名・列名・制約名の改行・制御文字を場所つき（`TABLE 'x'`・`COLUMN 'x'.'y'`・`UNIQUE 'x'.'y'`・`FOREIGN KEY 'x'`）で拒否する。
  - 説明は対象外（改行が正当。コメント側の `Sanitize` が唯一の防壁なので上のガードが要る）。
  - 判定は `SqlComment.ContainsControlCharacter` と共有する。C# 生成側の同じ規則（`ValidateNameControlCharacters`）は依存方向のため意図的に二重定義している。片方だけ変えない。
- 合成する制約名のクォート：`PK_{SafeName(テーブル名)}` は `SafeName` が終端クォート文字を二重化しないので、埋め込み側で必ず方言の `Escape` を通す（SQLite の DDL と逐次 4 方言の同期の `CREATE TABLE`／`ADD CONSTRAINT`）。
- 固定は `SqlTypeTextTests`・`SqlNameTextTests`（`IsSafe` の判定）・`DialectDdlTableTests` の行 `ddl-*-name-with-newline-is-rejected`（表・列・一意制約・外部キーの名前）・`sync-*-name-with-newline-is-rejected`（入口検証を 5 方言の DDL・同期へ同一ケースで。SQLite の再構築・列の変更の項目 1 つも）・`ddl-quote-apostrophe-table-and-double-quote-column-accepted`（引用符だけを含む名前は通す）・`SqlCommentTests`・`SqlCommentInjectionTests`・`SourceRuleTests` の規則 `sql-comment-unsanitized-holes`・`DialectDdlTableTests` の行 `ddl-primary-key-name-escaped`・`sync-*-primary-key-name-escaped`（合成 PK 名を 5 方言まとめて）・`DialectIdentifierTableTests`（5 方言へ同じ危険な文字の行）・`{SqlServer,PostgreSql}RuntimeConstraintNameIntegrationTests`（`]` `'` `"` を含む正当な名前・別スキーマの同名表・表が無いときの no-op）・4 方言の `*SyncScriptBuilderTests` の動的 SQL のケース・`ImportSyncTests`（行 `sync-dialog-preview-generation-failure-reports-status`。プレビュー生成の失敗はステータス行へ出し、未処理例外にしない）。mutation 実証済み。

### 利用者が付けた名前の上限

- 利用者が付けた名前は縮めずに、DDL・同期の入口で上限と表せない文字を名指しして断る。
- `SqlNameText.Validate(diagram / plan, limit)`（5 つの入口）が、制御文字の検査の後に、テーブル名は `IdentifierLengthLimit.AcceptsTableName`（最初の `.` で分けて部分ごと）、列名・制約名は `Accepts` で判定し、場所と理由（`DescribeViolation`＝英語）を並べて `InvalidOperationException` にする。上限は方言の `Limit`。
- 同期スクリプトの入口（`Validate(plan, limit)`）が見るのは、スクリプトに書く名前（文の名前を運ぶメンバーの全部）だけ。DDL 生成の入口（`Validate(diagram, limit)`）は図の全部の名前を見る。
  - 表の定義の全部（表名・列名・一意制約名）を見るのは、表を作る文と再構築の合成後定義だけ。差分の項目が持つ表の定義（`SchemaDiffItem.Entity`）は読まない。主キーを付ける文は主キーの列名だけ、外部キーの文は文の名前（差分が求めた名前か、リレーションの名前）だけを見る。
  - 選んだ変更が書かない名前（選択から外した列の追加の列名・取り込んだ表のほかの列や一意制約の名前）では断らない。選んだ変更が書く名前は断る。
  - テーブル再構築の方言（SQLite）は、作り直す表の合成後定義の全部の列と一意制約を書くので、その名前は書く名前になる（作り直しを伴う変更では、表に残るどの名前も検証される。消す列の名前は書かないので検証されない）。
  - 壊すと：書かない名前で断ると、上限を超える名前の列の追加を選択から外しても同じ表のほかの変更を同期できず、制御文字の名前の列を持つ取り込んだ表を同期で消せない。
  - ガード：`DialectDdlTableTests` の行 `sync-add-column-unwritten-overlong-column-in-definition-is-accepted`・`sync-other-alter-column-unwritten-newline-names-in-definition-are-accepted`（SQLite は作り直しが書くので断る）・`sync-other-drop-table-unwritten-newline-column-in-definition-is-accepted` と、対照の `sync-add-column-overlong-column-written-is-rejected`・`sync-other-drop-column-newline-name-written-is-rejected`（項目を手で組み、`SyncPlanner.BuildPlan` から方言ビルダーまで通す）。
- 表せない文字の正本も QuickER.Dialects の宣言（`IdentifierLengthLimit.RepresentsSupplementaryCharacters`）。MySQL の帰結は `src/QuickER.Provider.MySql/CLAUDE.md`「識別子と説明の文字」。
- 名前を拾う場所は `SqlNameText` の `DiagramNames`／`PlanNames` の 2 つだけ。`PlanNames` は、文の型ごとの腕で文の名前を運ぶメンバーを拾い、再構築と列順は `plan.Rebuilds`・`plan.Reorders` のループで拾う。1 つ拾い忘れると、その種類の名前だけが黙って DB に届く。
  - 拾う 1 か所（`names.Add` の 1 本）ごとに、方言の DDL の表の名前の出どころの宣言（`DialectDdlRows.NameSources`）が行を作る。その出どころだけへ改行入りの名前を入れた計画・図を 5 方言が名指しで断ること（行 `sync-plan-name-source-*-newline`・`ddl-name-source-*-newline`）と、表の位置の印（MySQL で文字数は上限内・ファイル名では上限を超える名前を、表の位置は断り列・制約の位置は断らない。行 `sync-plan-name-source-*-table-file-name`・`ddl-name-source-*-table-file-name`）を見る。
  - 宣言と製品の `names.Add`（所属のメソッド・文の型の腕・出どころの式・表の位置か・置き場所）の食い違いは、`SourceRuleTests` の規則 `sql-name-text-name-sources` が両方向で赤にする（足したのに宣言へ載せ忘れる・消したのに宣言に残る）。あるべき出現は宣言から作る（写しを持たない）。見つける形は自己検査の行 `sql-name-text-name-sources-forms`（形の違う `names.Add` を拾い、`names` 以外の受け手を拾わない）・`sql-name-text-name-sources-inactive-branch` が確かめる。mutation 実証済み（文の削除・真偽値の反転・規則の 3 方向）。
  - 文の型（`Sync/Steps/SyncStep.cs`）の、名前を運びうる型のメンバー（文字列・文字列の並び・列・表の定義・列の組・再構築）は、宣言の経路（`DdlNameRoute.StepMember`）か、理由つきの「名前でない」の一覧（説明の本文・表を作る文の差分が解決した一意制約の名前・ループが拾う再構築と列順）のちょうど一方にある。メンバーを足して宣言を忘れると、`ParityTests` の行 `sync-step-name-members-declared` が赤にする（読み取りの自己検査の行 `sync-step-name-members-probe`。名前を運びうる型でないメンバーは型を添えて数えるので、新しい型のメンバーも赤になる）。
- ガードは 2 つ。
  - `OutputIdentifierLimitGuardTests`：上限ちょうどの名前と上限を超える既定名の図から DDL と同期を作り、方言の区切りで囲まれた識別子を全文（動的 SQL のリテラルの中も）から取り出して、すべて `Accepts` を通ることと種類ごとの期待する識別子が現れることを 5 方言で表明する。
  - `IdentifierLimitEntryTests`：名前の種類 × 5 方言の表で、DDL と同期の両方の入口が断る／受け付けるを書き下した期待値で表明する。
- 実 DB では `NameFuzzDdlRoundTripIntegrationTests.cs` の方言ごとの派生が、宣言が断る名前は入口で断られ（断られなければ `not-rejected`）、受け付ける名前は適用できて取り込み直すと差分が出ない（断られれば `ddl-rejected`）ことを 5 方言で流す。mutation 実証済み。

### 差分の名前の綴り

- 差分は、組になった live と大文字小文字だけ違う図の表・列を、live の綴りへ揃えた複製で組む。揃えないと、引用識別子が大文字小文字を区別する方言でスクリプトが組んだ相手を指せない（Oracle・PostgreSQL）。
- `SchemaDiffService.Compute` の入口の `MatchSchemas`（`NameMatching`）で組んだ表・列だけを揃える。表と列の組み合わせは 1 度だけ行い、差分・列順の案内・綴りの揃えが同じ組を使う（`SchemaDiffServiceMatchingCountTests`）。
- 複製は Id を保った `Entity.Clone(preserveId: true)` で作り、図そのものは書き換えない。揃える組が無い表は元のオブジェクトのまま返す（出力はバイト不変）。
- 図にしか無い表・列は図の綴りで作る。
- 外部キーの既定名（`ResolveTargetForeignKeyNames`）と一意制約の合成名（`ResolveUniqueConstraintNames`）は、揃える前の図（DDL と同じ入力＝図の綴り）から求める。一意制約の名前は、複製の表の一意制約（Id を保った別のオブジェクト）へ Id で写す（`OntoMatchedTargets`）。
  - 揃えた複製から求めると、識別子の上限で縮めた既定名の末尾のハッシュが DDL と変わり、DDL が作った DB との差分に偽の作り直しが出る（DB が名前を大文字小文字で畳む・別の道具で作った DB など）。
  - 引き換え：DB を作った後に図の表名を大文字小文字だけ変えると、縮めた既定名の制約だけ名前の変わる作り直しが出る。今の DDL がその図から作る名前と一致するので、正しい差分とみなす。
  - PostgreSQL の旧版の切り詰めの吸収の照合は、揃えた表のまま。
  - ガード：期待の行 `diff-long-default-fk-name-with-case-folded-live-matches-ddl`（上限のある 4 方言）と、能力の組の網の関係 N2（DB 側の図の名前を ASCII の小文字へ畳んでも差分・警告・外した項目が変わらない＝`tests/QuickER.Tests.Integration/Dialects/SyncPlanFuzz/CLAUDE.md`「能力の組の網」）。
- 固定は期待の行 `diff-case-only-matched-names-use-live-spelling`・`SchemaDiffServiceTests.Items_CarryDiagramObjects`・実 DB の行 `realdb-oracle-case-only-names-sync-to-uppercase-table`／`realdb-pg-case-only-names-sync-to-lowercase-table`。mutation 実証済み。

### 複合外部キーの列ペアの並び

- 差分は 5 方言とも複合外部キーの列ペアを並びを見ずに比べる（列ペアは対応の集合で並びは意味を持たない。Oracle のカタログは宣言の並びを残さない）。
- `ReferencedKeyRequiresSameColumnOrder` が true の方言（SQL Server・MySQL）の DDL は、列ペアを参照先のキー（主キーの実効順、合わなければ一意制約の宣言順）の並びへ並べ替えて出す（`ForeignKeyColumnOrder.ArrangeToReferencedKey`・扱いの表の DDL のマスが Reorder かで判定）。
  - DDL だけを並べ替えて差分が並びを比べると、取り込み直した live がキーの並びになり、図と永久に食い違う。
- 同期も同じ方言で、外部キーを付ける文（明示・作り直しの展開・暗黙の付け直し）の列の組を参照先の同期後のキー（主キーの変更を選んだ表は新しい主キー・追加を選んだ表は図の主キー・それ以外は live の主キー、続いて同期後の一意制約＝`PostSyncSchema.UniqueKeys`）の並びへ並べ替える（組み立ての `ForeignKeyPairOrder`・規則の本体は DDL と同じ `ForeignKeyColumnOrder.ArrangeToKey`）。
  - 図や live の並びのまま付け直すと、参照される複合主キーの並びだけの変更と、並びの違う外部キーの作り直しが必ず失敗する（暗黙コミットの方言では外部キーが消えたまま残る）。
  - 主キーと同じ列集合の一意制約があっても主キーを先に試す。どのキーとも列の集合が一致しない外部キーは並べ替えない。
- 並べ替えた列の組は、外部キーを付ける文の値（`AddForeignKeyStep.ColumnPairs`＝句に書く順）として持つ。差分の項目は書き換えない。
- 複合 FK はフル対応で、FK の差分・暗黙の付け直し・候補キーの証明は列ペアのリスト（参照先の列は集合照合）で動く。
- 固定は期待の行 `diff-fk-composite-pair-order-is-not-a-difference`・`plan-arrange-*`・`plan-composite-fk-reordered-parent-pk-rebuilds-all-pairs`・`ForeignKeyColumnOrderTests`・`SyncPlannerTests.AddForeignKey_PrimaryKeyAndUniqueOnSameColumns_FollowsPrimaryKey`・実 DB の行（`realdb-fk-composite-other-order-ddl-applies-no-diff`／`realdb-mysql-fk-composite-other-order-ddl-applies-no-diff`・`realdb-fk-reorder-referenced-composite-pk-rebuilds-in-new-order`／`realdb-mysql-fk-reorder-referenced-composite-pk-rebuilds-in-new-order`・`realdb-fk-alter-in-other-order-rebuilt-in-key-order`／`realdb-mysql-fk-alter-in-other-order-rebuilt-in-key-order`・`realdb-oracle-fk-composite-declared-other-order-no-diff`）・同期計画の総当たり。mutation 実証済み。

### 外部キーの作り直しの畳み込み

- 参照アクション・列ペアの順序・参照先だけが変わった外部キーは `SchemaDiffKind.AlterForeignKey` 1 項目へ畳み、`SyncPlanner.BuildPlan` の入口（`ExpandAlters`）で DROP＋ADD へ展開する。
  - 2 項目のままだと、名前を保存する方言では既定どおり追加だけを選ぶと名前の重複で失敗し、SQLite では旧と新が同じ列へ 2 本並ぶ。
  - 対応づけの鍵は、名前を保存する方言が子表と実効名、SQLite が子表と子列の集合。実効名は追加の候補も削除の候補も DDL と同じ規則（`ConstraintNames.ResolveForeignKeyNames`＝名前の無い同じ組の 2 本目は一意化した名前）で求める（`SchemaDiffService.PairForeignKeyAlters`）。名前の無い DB の外部キーは取込が必ず名前を付けるので利用者の経路に届かず、網の入力（生成した図をそのまま DB 側の図にする）でだけ届く。ガード：期待の行 `diff-unnamed-fk-second-referential-action-change-with-unnamed-live-replaces-under-same-name`・能力の組の網の関係 N2。
  - 既定未選択・`IsDestructive`（暗黙コミットの方言では付け直しの失敗で外部キーが消えたまま残りうる）。
  - 展開した DROP は明示削除として扱う（暗黙の外し直しと重複せず旧定義も付け直さない）。展開を入口に置くので方言ビルダー・Db.UI は種別を知らなくてよい。
- 差分は外部キーを本数で比べる（シグネチャごとの多重集合）。集合で比べると、名前を保存しない SQLite で同じ外部キー 2 本の片方だけを変えた同期の後に 1 本へ減る。
- 固定は期待の行 `diff-fk-referential-action-change-is-single-alter`・`diff-fk-without-persisted-names-pairs-by-count-*`・`diff-fk-different-names-stay-drop-and-add`・`plan-fk-alter-*`・実 DB の行 `realdb-fk-referential-action-change-*`・`realdb-rebuild-alter-fk-replaces-fk`。mutation 実証済み。

### 一意制約の差分

- 差分は制約単位の `AddUniqueConstraint`（既定選択）／`DropUniqueConstraint`（既定未選択・`IsDestructive`）で出す。
- 照合は「名前の段 → 列集合の段」の 2 段。
  - 先に図で名前を付けた制約を DB の同じ名前（`SchemaNames` の規則）の制約と組み、列集合が同じなら差分なし・違えば `AlterUniqueConstraint`。
  - 残りを列集合で組む（名前差だけでは差分を出さない）。
  - 名前の段に合成名を使わない（名前の無い制約は同じ列集合の DB の制約で満たされる。合成名で組むと、合成名と同じ名前の別の列の制約と組んで作り直す）。
  - DB 側に名前の無い制約（SQLite の自動名）と、主キーと同じ列集合を持てない方言の主キーと同じ列集合の制約は名前の段で組まない。
  - 列集合だけで組むと、名前を保って列を変え・元の列に名前の無い制約を足した図で、名前を保った制約の追加が同じ名前で失敗する。
- 名前の段で組まなかった、同じ実効名（図の名前か合成名 vs DB の実名）の追加と削除は `AlterUniqueConstraint`（既定未選択・`IsDestructive`）1 項目へ畳む（`SchemaDiffService.PairUniqueConstraintAlters`）。DB 側に名前の無い制約と選べない案内項目は組まない。
- `SyncPlanner.ExpandAlters` が `AlterForeignKey` と一緒に削除＋追加へ展開する。文の種類（`SyncStep`）にも方言のレンダラーにも作り直しの種別は無いので、展開を外すと選んだ作り直しが黙って消える。依存の除外（`ExcludeItemsRequiringUnselected`）は展開の前。
- 選んだ一意制約の追加・作り直しの列集合が、選んだ削除・作り直し（展開した後の明示の削除）で外れない live の一意制約と同じなら、依存の除外がその項目を外し、`SelectedItemRequiresUnselectedItem`（Detail＝「追加する制約の名前 (残る live の一意制約の名前)」）で名指しする（方言に依らない。照合は `UniqueConstraintKey`）。
  - 名前の段が UQ_x(a)→(b) を作り直し（既定で未選択）にし、列集合の段が (a) への名前の無い一意制約を追加（既定で選択）にする図で、作り直しを選ばないと UQ_x(a) が残ったまま (a) に一意制約が重なる（Oracle は ORA-02261 で断る）。差分の既定の選択は変えない（利用者が追加だけを選べば同じ重なりが出るので、計画の側で外す）。
  - ガード：期待の行 `diff-uq-name-step-unnamed-add-on-old-columns-default-selection-is-excluded-with-warning`・`diff-uq-name-step-unnamed-add-on-old-columns-all-selected-applies-both`・実 DB の行 `realdb-uq-name-step-change-named-and-add-unnamed-on-old-columns-default-selection`（5 方言）・静的な規則 R9・形の行の因子 `ExplicitOp`=RenameKeepAndAddUnnamedOnOld。
- 合成名は、DDL と差分が 1 回の実行につき 1 回だけ `ConstraintNames.ResolveUniqueConstraintNames` で図全体から解決する（ぶつかる合成名に番号を足す規則は Model の担当）。差分は追加・作り直しの項目へ解決した名前（`SchemaDiffItem.UniqueConstraintNames`／`UniqueConstraintName`）を載せる。
- 固定は期待の行 `diff-uq-name-step-*`・`diff-uq-same-name-column-change-is-single-alter`・`diff-uq-synthesized-name-matching-live-is-alter`・`diff-uq-different-names-are-not-paired`・`diff-uq-unnamed-live-is-not-paired`・`plan-alter-expansion-*`・`plan-dependency-unique-alter-on-new-column-is-excluded-*`・実 DB の行 `realdb-uq-name-step-change-named-and-add-unnamed-on-old-columns`（5 方言）・`realdb-mysql-uq-columns-change-default-selection-changes-nothing`／`realdb-mysql-uq-columns-change-selected-rebuilds-same-name`・`realdb-oracle-uq-columns-change-default-selection-changes-nothing`／`realdb-oracle-uq-columns-change-selected-rebuilds-same-name`。mutation 実証済み。

### 一意制約の照合キーと付け直し

- DDL は 5 方言とも PK 制約行の直後に `CONSTRAINT … UNIQUE` 句を出す。CREATE TABLE の制約行（PK→UNIQUE）の組み立ては `TableConstraintLineBuilder` が単一正本で、DDL 生成と逐次 4 方言の同期の表の追加が共有する。どちらか一方に再インライン化すると「同期で作った表だけ UNIQUE が無い」乖離が静かに再発する。
  - 表を作る文が、主キーか先に書いた一意制約と同じ列の並びの一意制約をエラーなしに捨てる方言（宣言 `CreateTableUniqueConstraints.DropsDuplicateColumnLists`＝PostgreSQL。SQLite も捨てるがこのヘルパーを使わない）では、その一意制約を制約行に書かず、表を作る文を閉じた直後の `ALTER TABLE … ADD CONSTRAINT … UNIQUE` で足す（`TableConstraintLines.AfterCreate`・`TableConstraintLineBuilder.AppendAfterCreate`）。分けるのはこのヘルパーの 1 か所で、書き出すのは DDL 生成と PostgreSQL の同期の表の作成。
  - 列の並びは `SchemaNames` の同一性で順に比べる（並びの違う一意制約は DB も別の索引として持つ）。
  - 分けないと、DDL で作った表も同期で作った表も一意制約が図より少なく、次の比較で一意制約の追加が出る（同じ列の 2 つ目は「残る一意制約の前提」の除外で外されて名指しされ、永久に消えない）。
  - ガード：期待の行 `realdb-create-table-*`（5 方言の実 DB。DDL で作って取り込み直す形と同期の表の追加の形）・`DialectDdlTableTests`（行 `ddl-unique-on-same-columns-as-earlier-unique-added-after-create-table`・`sync-add-table-unique-on-same-columns-as-earlier-unique-added-after-create-table` と、主キーと同じ列の一意制約の行 `ddl-unique-on-primary-key-columns-omitted-where-unsupported`・`sync-add-table-unique-on-primary-key-columns-omitted-where-unsupported` の PostgreSQL の期待）・`DialectDocsTableTests.SyncCapabilitiesTable_MatchesDeclarations`。
- DB の一意制約の照合キーは `UniqueConstraintKey`（表＋DB の名前＋列集合＝外部キーの `ForeignKeyKey` と対称・名前の無いものどうしは列集合で組む）。
  - 照合するのは DB の一意制約とそれを外す項目（差分の削除・作り直しを展開した削除）だけで、図の追加はこのキーで照合しない。
  - 列の削除に伴う暗黙の削除・同期後のキーの集合（候補キーの証明）・SQLite の再構築の削除が同じキーを共有する。
  - 名前を外して表＋列集合だけにすると、同じ列集合の一意制約 2 つの片方を外したときに、もう片方まで「外した」ことになる。
  - 計画の照合キーは識別の型（`ColumnId`・`ForeignKeyPairId`・`UniqueConstraintId`・`ForeignKeyId`。「live の状態と識別の型」）で、名前の成分を型のメンバーとして分けて持つ。1 つの文字列へつなぐ所（外部キーの列ペアの並び・差分の署名・一意制約の列集合のシグネチャ）は、部分を `SignatureText.Part`（「長さ:値」）で書いてつなぐ。区切り文字だけでつなぐと、区切り文字を含む名前 1 つと名前 2 つ、表名と列名の境目が同じキーになる。
- `AlterColumnRequiresKeyConstraintRebuild` の方言（SQL Server）は、列の変更が触る一意制約・主キーの構成列を暗黙に外して付け直す（`src/QuickER.Provider.SqlServer/CLAUDE.md`「同期スクリプト」）。明示の削除とは `UniqueConstraintKey` で重複を除く。
- 明示の一意制約の削除が被参照 FK を支えうれば `UniqueConstraintDropMayBreakForeignKey` で警告する（方言中立。支えの判定は「外部キーを支えうるキー」）。
- SQLite は再構築へ畳む。`SchemaAuxiliaryObject` に一意制約の種類は無く（インデックス・トリガーのみ）、再構築の CREATE TABLE は合成後定義から名前付き UNIQUE 句を出す。
- 固定は期待の行 `plan-uq-explicit-drop-is-neither-duplicated-nor-readded-*`・`plan-uq-rebuild-drops-only-same-named-twin`・`plan-candidate-key-add-fk-to-twin-unique-*`・同期計画の総当たり。mutation 実証済み。

### 主キーの同期

- 主キー同期（`AlterPrimaryKey`）は PK の列構成（順序付きの名前のリスト）の差を表ごとに 1 項目で出す（既定未選択・`IsDestructive`）。
- 旧 PK の制約名は取り込まず、方言のカタログを実行時に逆引きして DROP する（PK なしは no-op・解除だけなら ADD なし）。ADD は `PK_{テーブル名}` の固定命名。
- 逐次方言は 3 フェーズ（`StepSection.PrimaryKeyPhase`＝Drop → AlterColumn → Add）。旧 PK 列の NULL 許容化を含む変更も 1 回の同期で通る。PK 変更なしの計画は出力バイト不変。SQLite は再構築へ畳む。
  - MySQL で解除と付与を 1 つの ALTER TABLE にまとめる案は採らない（3 フェーズは列の変更に要り、1 文にできるのは列の変更を伴わない場合だけで規則が割れる）。
- 依存 FK の自動の外し直し：選んだ `AlterPrimaryKey` の旧主キーに支えられた被参照 FK（旧主キーと同じ列集合を参照するもの・`ForeignKeyRequiresLeadingIndex` の方言では旧主キーの先頭の列だけを参照するものも）と、FK に関わる列の AlterColumn（`AlterColumnRequiresForeignKeyRebuild`）を、組み立てが外し直しの宣言（`RebuildDeclarations.RequireForeignKeysOffDroppedPrimaryKeys`・`RequireForeignKeysOffAlteredColumns`）から集め、外す文と付け直す文の対にする（「逐次 DDL の組み立て」）。
  - 一意制約を参照する FK は主キーの解除に巻き込まれないので外さない（`DependsOnOldPrimaryKey`）。
  - 同じ計画が明示に外す FK は外し直さず、付け直さない。
- 主キーの変更で新しく主キーに加わる NULL 許容の列は、選んでいなくても live の型のまま NOT NULL にする（`InjectPrimaryKeyNotNullChanges`＝主キーの変更の意図に含まれる）。
- 固定は期待の行 `plan-implicit-fk-pk-change-skips-fk-on-unique-constraint`・`plan-implicit-fk-pk-change-rebuilds-fk-on-old-pk-prefix-for-mysql-like`・`plan-sections-are-in-fixed-order`・実 DB の行 `realdb-pk-change-only-makes-new-key-column-not-null`・`DialectDdlTableTests` の行 `sync-other-primary-key-composite-follows-primary-key-column-ids`（付与の文は、複合の主キーの列を主キーの中の並びの順に書く。逐次 DDL の 4 方言）。

### 候補キーの証明

- 付け直す外部キーの参照先の列が、同期後の PK とも同期後に在る一意制約とも列の集合で完全一致しなければ、`SyncPlan.Warnings`（候補キー喪失）として実行確認に出す。
  - 外し直しの理由に依らず、暗黙に付け直すすべての外部キー（主キーの変更・列の変更・途中の状態の食い違い・索引の喪失）に掛ける。同期後の主キーは `PostSyncSchema.PrimaryKeyOf`（主キーの変更を選んだ表は新しい主キー・追加する表は図の主キー・ほかは live の主キー）で、外部キーを付ける文を並べる `ForeignKeyAddOrder` と同じ材料を使う。判定は評価の規則 `AssessmentRules.WarnDetachedForeignKeysAtRisk`（`ForeignKeyRebuildMayLoseCandidateKey`）。
  - 主キーの変更で付け直す外部キーにだけ掛けていたため、参照先の唯一の一意制約を同じ計画で外しながら列の変更で外部キーを付け直す同期が、名指しされずに付け直しで断られ、暗黙コミットの方言（MySQL・Oracle）で外部キーが消えたまま残った（修正 AC2。Oracle は途中の状態の付け直しで初めてこの経路を通る）。ガード：形の行（`SyncPlan/ShapeRows` の `TypeChange`=BothEndsKindChange × `ReferencedKey`=Unique × 一意制約の削除。網の静的な規則の D9〔断られる文自身が、前の方で外した外部キーの付け直しである形〕と実 DB の表明 1b）。
  - 証明ベース：同期後の一意制約の集合は live − 選んだ削除 ＋ 選んだ追加から厳密に合成する（自然キーの UNIQUE＋代理 PK の定番構成では警告しない）。
  - ⊆ の判定にはしない（`(id)→(id,code)` の拡張で多くの方言が失敗する）。並びまで比べない（並びは付け直しで直すので、比べると通る付け直しを誤って警告する）＝`StaysCandidateKey`。
  - 取込の外の素の一意インデックスで実際には通る構成があるので、実行は止めない。
- 選んだ外部キーの追加・作り直しの参照先が同期後の候補キーでないときも `ForeignKeyNotOnCandidateKey` で警告する（`WarnAddedForeignKeysNotOnCandidateKey`。並べ替える先が無いので図の並びのまま出す）。
- 参照先が図の主キーとだけ一致し、同期後の主キー・一意制約と一致しない外部キーの追加・作り直しは、主キーの変更を前提にする項目として `ExcludeItemsRequiringUnselected` が外す。
- DDL が暗黙コミットされる方言では、証明できない追加を外部キーの追加の最後へ回す（次の節「外部キーの追加の並び」）。外部キーの追加は互いに依存せず後ろに制約の文が無いので、断られてもほかの付け直しは済んでいる。並べないと、警告していない外部キーまで巻き添えで消えたまま残る。
- 固定は期待の行 `plan-candidate-key-*`・`plan-arrange-*`・同期計画の総当たり。mutation 実証済み。

### 外部キーの追加の並び

- DDL が暗黙コミットされる方言（`TransactionalDdl` が偽）では、外部キーを付ける文を断られうる順に安定に並べる（組み立ての `ForeignKeyAddOrder` の 1 か所）：
  1. そのまま付け直す外部キー（外したのと同じ子の表・実効名・列ペアの集合・参照アクション＝もともと DB にあった制約）。
  2. 定義を変えて付け直す外部キー（選んだ作り直しの展開）。
  3. 新しい外部キー。
  4. 候補キーを証明できない外部キー（「候補キーの証明」）。
- 新しい外部キーは既存の行（親の無い値）で断られうる（計画は列の値を見ない）。付け直しを後ろに置くと、新しい外部キーが断られたときに付け直しが走らず、外した外部キーが消えたまま残る。
- トランザクションの方言は並びを変えない（出力はバイト不変）。外す文との照合は、子の表と実効名（文から求める `ForeignKeyStepNames`）で組み、列ペアは並びを見ずに対応の集合で比べる（並びは参照先のキーの並びへ並べ替えるため）。
- ガード：形の行の因子 `AddedForeignKey`=OrphanChild（e に親の無い値の行）と必ず含む行・生成器の水準（行のある子の親の無い値の列への外部キーの追加・必須の形 `orphan-fk-add`）・静的な規則の D9 の並び（前で外した外部キーの付け直しが新しい外部キーの追加より後ろ）・期待の行 `plan-arrange-add-fk-follows-post-sync-pk-order-with-key-change`。同期計画の総当たりは、ケースが宣言した既存の行で断られる文の失敗だけ表明 1 を外し、表明 1b（付け直しが消えない）は掛ける（`tests/QuickER.Tests.Integration/Dialects/SyncPlanFuzz/CLAUDE.md`「総当たりの表明と運用」）。mutation 実証済み。

### 外部キーを支えうるキー

- 親のキー（主キー・一意制約）がその列を参照する外部キーを支えうるか（DB がそのキーの削除を外部キーで断るか）は `SyncPlanner.CanSupportForeignKey` の 1 か所で決める。
  - 参照する列と同じ列の集合を持つキーは、どの方言でも支える。
  - `ForeignKeyRequiresLeadingIndex` の方言（MySQL）では、参照する列を先頭に並べるもっと長いキーも支えうる（8.0 系や `restrict_fk_on_non_standard_key`=OFF で作れる非標準キーの外部キー。ほかに支える索引が無いとキーの削除が 1553 になる）。
- 通すのは 4 か所：前寄せ（`Refusals.FindCertain` のキーの削除の判定）・一意制約の削除の警告（`WarnUniqueConstraintDropsBreakingForeignKeys`）・主キーの変更の外し直しの 2 つの宣言（旧主キーに支えられた外部キー＝`RebuildDeclarations.RequireForeignKeysOffDroppedPrimaryKeys` が通す `DependsOnOldPrimaryKey`、新しい主キーと同じ列を参照し、この計画で外す一意制約に支えられた外部キー＝`RequireForeignKeysOffUniquesReplacedByPrimaryKey`）。
  - 警告は「新しい主キーが参照する列とちょうど同じ外部キー」を、主キーの変更の外し直しが付け直すとみなして飛ばす。だから外し直しの一意制約の宣言と警告は同じ判定でなければならない。
  - 写しが割れると、MySQL で一意制約 UQ(a, b) の先頭の列 a だけを参照する外部キーがあるとき、その一意制約の削除は前寄せされるのに警告が出ずに 1553 で断られる。警告と外し直しの宣言の片方だけが先頭の列を数えると、主キーを (a) へ移しながらその一意制約を外す同期が、警告も外し直しも無いまま断られる。
- 付け直せるか（候補キーの証明＝`StaysCandidateKey`）は別の判定で、列の集合の完全一致のまま（非標準キーへの付け直しは証明しない＝「候補キーの証明」）。
- MySQL 8.4 の既定（`restrict_fk_on_non_standard_key`=ON）では非標準キーの外部キーを作れないので、実 DB の段は持たない。網の静的な規則 R1（MySQL は先頭の列の索引も見る）はこの形の失敗を予言できるが、生成器・形の行は一意制約の全列を参照する外部キーしか作らないので、形は期待の行が受け持つ。
- ガード：期待の行 `plan-implicit-unique-drop-warns-for-fk-on-its-leading-columns-mysql-like`（警告だけを列の集合の完全一致に戻すと赤）・`plan-implicit-fk-pk-change-onto-referenced-prefix-of-dropped-unique-rebuilds-fk-mysql-like`（外し直しの一意制約の宣言だけを戻すと赤）。どちらも SQL Server（先頭の列で支えない）と比べる。mutation 実証済み。

### 外部キーの暗黙の外し直し

外部キーの暗黙の外し直し（組み立ての外し直しの宣言 `RebuildDeclarations`・付け直し `Readds`・付け直せない制約 `UnreaddableConstraints`。「逐次 DDL の組み立て」）は、付け直せない外部キーを外したまま残さない。

- 列定義の変更で同期後に両端の型が一致しなくなる外部キーは注入せず、`SyncPlanWarningKind.ForeignKeyColumnTypeMismatch` で名指しする（外さずに列の変更の時点で何も変えずに失敗させる）。
  - 名指しは評価の規則 `AssessmentRules.WarnLiveForeignKeysWithPostSyncTypeMismatch` の 1 か所で、逐次 DDL の方言に依らない（明示に外さない live の外部キーを全部見る。テーブル再構築の方言では返さない）。列の変更で外部キーを外し直さない方言（PostgreSQL・Oracle）は外部キーを張ったまま列を変えるので、DB が断れば列の変更が失敗する。名指しを外し直しの経路（列の変更で外部キーを外し直す方言の判定）と主キーの変更の経路にだけ置いていたため、PostgreSQL・Oracle で残す外部キーの列を互換でない型へ変える同期が警告なしに失敗し（42804・ORA-02267）、Oracle では同じ同期で外した作り直しの外部キーが消えたまま残った（P5-2 の試運転の 2 回目の V4）。
  - 判定は組単位で、正本は `SyncPlanner.IsMismatchedUnacceptedPair`（組が「DB が受け入れた組」でなく、同期後に両端が並べられない）。`HasPostSyncTypeMismatch`（live の外部キー）と `ExcludeForeignKeysWithPostSyncTypeMismatch`（選んだ追加・作り直し）が通す。「DB が受け入れた組」は、どちらの端の型もこの同期で決まらず、live の外部キーが同じ組（子の表と列・親の表と列＝`ForeignKeyPairKey`）を持つ組で、数えない。外部キー単位で数えると、複合外部キーのほかの組の型を変えたとき、DB が受け入れている型の違う組まで食い違いに数え、外部キーを外し直さずに主キーの解除を必ず失敗させた（2BP01・ORA-02273。V1）。
  - DB にまだ無い組（選んだ外部キーの追加・列ペアを変える作り直しの新しい組）は、両端とも型の決まらない live の列でも数える。「型が決まらないから受け入れている」とみなしていたため、live の列どうしで型の違う外部キーの追加（親 int・子 bigint など）が名指しされずに MySQL（3780）・SQL Server（1778）で断られた（修正 AC2）。ガード：形の行（`SyncPlan/ShapeRows` の因子 `AddedForeignKey` と必ず含む行 2 つ）・静的な規則 R8 と DB に無い外部キーへ広げた W1（`tests/QuickER.Tests.Integration/Dialects/SyncPlanFuzz/CLAUDE.md`「静的な規則」）・生成器の水準（型の違う live の列への外部キーの追加）。
  - 列の変更で外部キーを外し直さない方言でも、組の両端の型がこの同期で決まり、片側だけ変えた途中の状態（新しい子 × 古い親、古い子 × 新しい親）のどちらかが並べられない外部キーは、外して付け直す（`RebuildDeclarations.RequireForeignKeysOffDivergingColumnChanges`・外部キー単位。同期後に食い違う外部キーと明示に外す外部キーは数えない）。外部キーを張ったまま両端を 1 列ずつ変えるので、両端を同じ別の型（int → varchar(10)）へ変える同期が、名指しも外し直しもされずに最初の列の変更で断られた（PostgreSQL 42804・Oracle ORA-02267。修正 AC2 の V8）。宣言は保守側なので、DB が受け入れる途中の状態（PostgreSQL の integer と numeric など）でも付け直す。ガード：形の行（`SyncPlan/ShapeRows` の `TypeChange`=BothEndsKindChange と必ず含む行 V8）・期待の行（`plan-type-mismatch-both-sides-kind-change-rebuilds-and-warns-where-declared`・`plan-type-mismatch-selected-fk-alter-is-excluded-as-a-whole`・`plan-type-mismatch-one-intermediate-state-incompatible-rebuilds-fk`〔途中の状態の片方だけが並べられない形。PostgreSQL で子 char(10) → varchar(10)・親 varchar(10) → varchar(20)。「どちらか」を「両方」にすると赤〕）・静的な規則 R7（途中の状態に掛かる）・生成器の水準（型の種類を変える）。
  - 列が空でないと通らない列の変更（`SyncDialectCapabilities.RequiresEmptyColumn`＝`TypeKindChangeRequiresEmptyColumn` の方言の型の種類の変更〔方言の正規化を通した括弧より前の基本の名前を `AreSameType` で比べる＝`ChangesTypeKind`。既定値のある列の型の種類の変更の警告と共有〕と、`PrecisionDecreaseRequiresEmptyColumn` の方言の精度・スケール・整数部の桁・小数秒を下げる変更〔精度なし → 精度ありを含む＝`DecreasesPrecision`〕。どちらも Oracle＝ORA-01439・ORA-01440・ORA-30082。同じ系統の固定長と可変長の文字列の入れ替え〔`CHAR` と `VARCHAR2`・`NCHAR` と `NVARCHAR2`〕で長さを下げないものは、Oracle が値のある列でも受け付けるので型の種類の変更に数えない＝`StringLengthChange`）を挟んで「前で外して後ろで付け直す」制約（外部キー・一意制約・主キー＝主キーの変更の解除を含む）を、`RefusableChangeNaming.Constraints` が `SyncPlanWarningKind.ConstraintRebuildAroundEmptyColumnChange` で全部名指しする（止めない。列に値があると列の変更が断られ、暗黙コミットで外した制約が付け直されずに残る）。
    - 並べ終えた計画（前寄せの後）を見る。外す理由（外部キーの外し直し・主キーの変更・キーの列の変更）と、挟む変更がどの表かに依らない。外部キーの構成列の変更に限ると、主キー自体や無関係な表の変更を挟む外し直しが名指しされずに消える。
    - 型の決まる列は `PostSyncSchema.TypeDecidingColumns` を通す。Detail は「制約名 (表.列: 旧 → 新, …)」、主キーの制約名は PRIMARY KEY。暗黙コミットの方言だけ（トランザクションの方言は失敗で巻き戻る）。
    - 同期計画の総当たりの表明 1b は、この警告が名指しした制約の消失だけを受け入れる（注 `warned-empty-column-change-exempt`）。
    - ガード：期待の行 `plan-empty-column-change-intermediate-rebuild-names-fk`・`plan-empty-column-change-pk-change-names-primary-key-and-fk`・`plan-type-mismatch-both-sides-kind-change-rebuilds-and-warns-where-declared`・生成器の水準（精度の縮小・必須の形 `precision-decrease`）・`SchemaSyncWarningTextTests`・`DialectDocsTableTests.ForeignKeyRebuildTable_MatchesDeclarations`。
  - 型の比較は `SyncDialectCapabilities.AreForeignKeyCompatibleTypes`（`AreSameType` に加え、`ForeignKeyAllowsStringLengthDifference` の方言では同じ文字列型の長さだけの違いを許す＝`SyncDialectCapabilities.DiffersOnlyInStringLength`。Oracle の単位語 CHAR／BYTE は両端で同じこと。`ForeignKeyAllowsIntegerWidthDifference` の方言＝PostgreSQL・Oracle・SQLite では幅だけ違う整数型を許す＝`SyncDialectCapabilities.DiffersOnlyInIntegerWidth`）。
  - 宣言は「真なら DB が必ず受け入れる」保守側に置く。DB が受け入れるのに宣言が許さない組（PostgreSQL・MySQL・Oracle の小数の精度の違い・PostgreSQL の整数から小数への参照・浮動小数点の幅・PostgreSQL の `char`・`varchar`・`text` のどの組・Oracle の CHAR の単位語の違い）は、既知の割り切りとして食い違いとみなす（名指しする）。外部キーを張ったまま列を変えられる同期は DB が受け入れれば通り、外部キーを外して付け直す必要がある同期（参照する主キーの変更）は外し直さないのでその段で止まる（docs/database.md の約束）。ガード：期待の行 `realdb-fk-integer-child-decimal-parent-is-named-and-commits`・`realdb-fk-fixed-and-variable-string-is-named-and-accepted-only-where-the-database-accepts`・`realdb-pk-change-with-type-difference-accepted-by-database-is-named-and-stops`（実 PostgreSQL・実 Oracle）。総当たりの静的な規則 R7・R8 は、この組を予言しない（`tests/QuickER.Tests.Integration/Dialects/SyncPlanFuzz/CLAUDE.md`「静的な規則」）。
  - ガード：形の行（`SyncPlan/ShapeRows` の `ForeignKeyTypes`=DecimalOtherKeyColumn と必ず含む行 V1・V4）・期待の行（`plan-type-mismatch-composite-undecided-pair-is-not-counted-alter-column`・`plan-type-mismatch-composite-undecided-pair-is-not-counted-pk-change`・`realdb-fk-integer-width-difference-is-accepted-without-warning`）・静的な規則 W1・R7（`tests/QuickER.Tests.Integration/Dialects/SyncPlanFuzz/CLAUDE.md`「静的な規則」）・`CapabilityTreatmentParityTests`・`DialectDocsTableTests.ForeignKeyRebuildTable_MatchesDeclarations`・`SyncPlannerTests`（幅の違いの表）。
  - 主キーの変更で付け直す外部キーも、構成列の型を変えて両端が食い違うなら外さずに名指しする。
  - SQL Server（長さも一致が要る）の DDL は長さだけ違う外部キーの前に注記を出す（`ForeignKeyStatementNotes`）。
  - 「この同期で型が決まる列」は `PostSyncSchema.TypeDecidingColumns` の 1 か所が決める（live に無い列か、live の型と `AreSameType` で同じでない型へ変える列の変更）。NULL 許容だけの列の変更は型を決めない。外部キーの型の照合（`HasPostSyncTypeMismatch`・`ExcludeForeignKeysWithPostSyncTypeMismatch`・前寄せの型の判定）はどれもこの関数を通す。
    - 数えると、DB が受け入れている型の違う外部キー（PostgreSQL の integer → bigint など）を NULL 許容の変更だけで「付け直せない」と名指しして外し直さず、その外部キーに阻まれる主キーの解除が必ず失敗した（PostgreSQL 2BP01・Oracle ORA-02273）。3 か所がそれぞれ列の変更の新しい型を辞書へ写していたので、誤りも 3 か所に写っていた。
    - ガード：形の行（`SyncPlanShapeRows` の因子 `ForeignKeyTypes`=Wider × `TypeChange`=ChildNullability と必ず含む行 L4）・静的な規則 W1（`tests/QuickER.Tests.Integration/Dialects/SyncPlanFuzz/CLAUDE.md`「静的な規則」）・`SourceRuleTests`（規則 `sync-type-deciding-column-copies`＝差分の項目の列の新しい型を直接読む箇所をメソッドで数え、理由つきの一覧と照合する）。
- 選んだ外部キーの追加・作り直しを警告で名指しする名前は、スクリプトが付ける名前（`SyncPlanner.SelectedForeignKeyName`＝`SyncScriptBuilderHelper.ForeignKeyNameToAdd`）の 1 か所で作る（型の食い違いの除外・`ForeignKeyNotOnCandidateKey`・SET NULL の除外・SET DEFAULT の 4 つの警告と、選んでいない変更を前提にする項目の除外の名指し）。
  - リレーションの名前から規約名を作り直すと、名前の無い同じ組の 2 本目が 1 本目と同じ `FK_{子}_{親}` になり、警告が DB にある無関係な外部キーを名指しした。
  - ガード：期待の行 `plan-warning-names-added-fk-type-mismatch`・`plan-warning-names-added-fk-not-on-candidate-key`・`plan-warning-names-added-fk-set-null`・`plan-warning-names-added-fk-set-default`・`SourceRuleTests`（規則 `sync-selected-foreign-key-names`＝差分の項目のリレーションから `ResolveForeignKeyName` で名前を作り直す呼び出しと、差分の項目の名前から `ConstraintNames.EffectiveForeignKey` で組む呼び出しを、理由つきの一覧と照合する）。
- 付け直せない外部キー（同期後に両端の型が食い違う外部キー・子の列が同期後に NOT NULL になる SET NULL の外部キー）は、外す理由（主キーの変更・列の変更・途中の状態・索引の喪失）に依らず、その 1 本だけを外さない。ほかの外部キーは普通に外して付け直す。
  - 付け直しは必ず失敗するので、外すと暗黙コミットの方言で外部キーが消えたまま残る。残した外部キーに阻まれて必ず断られる文は、暗黙コミットの方言では次の項目の前寄せが計画の先頭へ移すので、ほかの外部キーが外れる前に失敗する。トランザクションの方言は失敗で巻き戻る。
  - 前寄せだけが「失敗の前に何も外れない」を保証する。前寄せが外れると、同じ列・同じ親の列のほかの外部キーが、失敗する列の変更より前に外れて消えたまま残る（総当たりの静的な規則の D9 と実 DB の表明 1b が名指しする）。
  - 以前は、付け直せない外部キーが 1 本でもあれば、ほかの外部キーも 1 本も外し直さなかった（全体の抑止）。前寄せがあれば要らず、後ろの段の結論を前の段へ戻す線が 1 本減るので、1 本ごとの規則にした。同期が失敗して DB が変わらないことは同じで、スクリプトに実行されない外し直しの文が並ぶ点だけが変わる。
  - ガード：期待の行 `plan-type-mismatch-same-column-mismatched-sibling-rebuilds-only-the-matching-fk`・`plan-type-mismatch-mismatched-fk-stays-and-other-fks-are-rebuilt`（トランザクションと暗黙コミットの能力）・`plan-type-mismatch-pk-change-rebuilt-fk-warns-without-rebuild-with-sibling`・`plan-type-mismatch-selected-fk-alter-is-excluded-as-a-whole`・`plan-set-null-alter-child-to-not-null-with-other-rebuild`・実 MySQL の行 `realdb-mysql-fk-same-column-one-mismatched-changes-nothing`・`realdb-mysql-fk-set-null-child-not-null-names-fk-and-changes-nothing`（DB が変わらないこと）・静的な規則 D9・M1。
- DDL が暗黙コミットされる方言（`TransactionalDdl` が偽）では、計画が残す外部キーに阻まれて必ず断られる文のうち計画の順で最初のもの（キーの削除は組）を、計画の先頭へ移す（判定は `Refusals.FindCertain`、移すのは `MoveFirst.Apply`＝何も変えずに失敗させる。主キーの変更は外す文だけを移す）。トランザクションの方言は並びを変えない。
  - 対象は、キーの削除（主キーの解除・一意制約の削除。主キーが置き換える一意制約の暗黙の削除を含む）で残す外部キーを支え得る親のキー（参照する列と同じ列の集合を持つ live の主キー・一意制約。`ForeignKeyRequiresLeadingIndex` の方言ではその列を先頭に持つキーも）がこの計画ですべて外れるとき・残す外部キーの両端の型を食い違わせる列の型の変更（`AlterColumnRequiresForeignKeyRebuild` の方言は外部キー単位で同期後の両端、ほかの方言はその列を含む組で、その列の新しい型と相手の端の live の型＝先頭へ移した位置の状態）・`SetNullRequiresNullableChildColumn` の方言で残す SET NULL の外部キーの子の列の NOT NULL 化。SET NULL の子の列の NOT NULL 化の前寄せのガード：期待の行 `plan-set-null-pk-change-and-alter-making-child-not-null-moves-alter-first-*`・網の静的な規則 R11（前寄せしないと、後ろの断られる文として予言して D9 が名指しの外の外した制約を数える）と M1・生成器の行の水準 `set-null-child-not-null`。
  - 外し直さない方言の型の判定を同期後の両端で行うと、両端を別々の型へ変える同期で、先頭では通る側の変更（長さを広げるなど）を移し、断られる側の変更が元の位置に残って、その間に外した制約が消えたまま残る。組の変更のどれも単独では先頭で断られない（DB が受け入れている型の違う組の両端を互いに並べられない型へ寄せる）ときは、組の変更を計画の順に移す（`Refusals.FindCertain` の両端の変更の組）。
  - 移した文の数は `SyncPlan.MovedFirstCount` で、その文は前寄せの印（`StepSection.IsMovedFirst`。internal・レンダラーは見ない）のセクションになる。印は同期計画の総当たりの静的な規則 M1（移したセクションの後ろへ予言された失敗が進まない）が読む。ガード：静的な規則 M1・形の行の因子 `TypeChange`=DivergentEnds と必ず含む行。
  - 支え得るキーを全部外すときは、最後の削除だけが断られ前の削除は通るので、外す文の組を先頭へ移す。組の中は、後ろで付け直すキーの削除（一意制約の作り直しを展開した削除・付与の段を持つ主キーの解除）を最後へ回し、前にはただの削除（消えてよい）を置く（`Refusals.FindCertain` が並べ、`MoveFirst.Apply` は移す文の並びのまま先頭に置く）。付け直すキーの削除を組の前に置いていたため、MySQL で外部キーが参照する列の一意制約の双子の片方の列を変え、もう片方を外す同期が、作り直しの削除が通った後にただの削除を 1553 で断られ、作り直す一意制約が消えたまま残った（夜間 CI の網の種 8169）。ガード：形の行（`SyncPlan/ShapeRows` の `ExplicitOp`=ChangeFirstDropOthers と必ず含む行。網の静的な規則の D9）・期待の行 `plan-move-first-key-drop-group-readded-drop-last-mysql-like`（実 DB）。組の中に付け直すキーの削除が 2 つ以上あると、どう並べても前の方の付け直すキーが消えるので、一意制約の作り直し 1 つ（組の最初の付け直す一意制約）を計画から外して `SyncPlanWarningKind.UniqueConstraintRebuildKeptForForeignKey`（Detail は「残した一意制約の名前 (支える外部キーの名前)」）で名指しし、取り下げた項目を除いて選択からやり直す（`UniqueRebuildWithdrawal.Find` と計画器の輪。判定は前寄せの組と同じ `Refusals.FindCertain`・`CanSupportForeignKey`。「位置の状態と歩き」）。外した一意制約が古い形で外部キーを支え続けるので、ほかの削除は通り、同期は消える物なしに進む。前寄せと同じく暗黙コミットの方言だけ（トランザクションの方言は失敗で巻き戻る。生成器はこの形を作らない＝`ChangeUniqueColumns` はいつも最初の名前つきの一意制約を移す）。ガード：形の行（`ExplicitOp`=ChangeAllUniques と必ず含む行。網の D9・実 DB の表明 1b の規則 2）・期待の行 `plan-move-first-key-drop-group-two-readded-keeps-one-rebuild-mysql-like`（実 DB・Detail の名指し）。ほかのキーを live だけで数えていたため、MySQL で一意制約の双子を両方外す同期が前寄せされず 2 つ目の削除が 1553 で断られて作り直しの外部キーが消えたまま残り（V3）、主キーと同じ列の一意制約が外部キーを支えるのに主キーの解除を必ず断られると先頭へ移して、通った解除の後の列の変更の失敗で主キーが消えたまま残った（3780。V6）。ガード：形の行（`SyncPlan/ShapeRows` の `ExplicitOp`=DropAllUniques・因子 `CollateralFk`・因子 `PrimaryKeyTwin` と必ず含む行 V3・V6。網の静的な規則の D9 と実 DB の表明 1b）。
  - 一意制約の削除を対象に入れていなかったため、Oracle で外部キー 2 本が参照する一意制約の列へ主キーを付け替え、片方の子の列の型を変える同期が、兄弟の外部キーを外した後に ORA-02273 で断られ、兄弟の外部キーが消えたまま残った。ガード：形の行の必ず含む行 L1（`SyncPlanShapeRows` の `PkChange`=MoveToUniqueColumn × `ReferencedKey`=Unique × 参照 2 本 × 子だけの型の変更。網の静的な規則の D9 と実 DB の表明 1b）。
- 子表・親表・構成列が同じ計画で削除される外部キーは、外すだけで付け直さない。この「外すだけ」は端が消える live の外部キーすべてに掛ける（`SyncPlanner.InjectVanishingForeignKeyDrops`＝計画の入口で暗黙の DropForeignKey を足す）。
  - 外部キーの削除は既定で未選択なので、足さないと逐次 DDL では参照されている表・列の削除が拒否され、SQLite では存在しない表・列を指す外部キーが残る。
- `ForeignKeyRequiresLeadingIndex` の方言では、削除する一意制約の索引の先頭に子列が乗る外部キーも外して付け直す（`RebuildDeclarations.RequireForeignKeysOffLostIndexes`）。
- 外部キーの照合キー（`ForeignKeyKey`）は列ペアに加えて実効名と参照アクションを含める（同じ列ペアの 2 本の片方を外したとき、もう 1 本まで「外した」扱いにしない）。再構築の合成（`TableRebuildForeignKeys.Synthesize`）も削除 1 件につき 1 本だけ外す。
- 構成列・表が同じ計画で消える一意制約の暗黙の付け直しも、外すだけで付け直さない。
- 警告や付け直しに出す外部キーの名前は `LiveSchema.ForeignKeyNames(limit)`（図全体から求めた実効名）を通す。SQLite の再構築の合成が作り直す外部キー（`TableRebuildForeignKeys.Synthesize`）の名前も同じ（名前の無い同じ組の 2 本目が 1 本目と同じ名前にならない）。
  - 外す文を付け直す文と組むときの名前（前後の名指しの歩きの身元＝`PlanWalk`・`ForeignKeyAddOrder` の順位）も、外す文が DB の名前を持たなければこの実効名で求める（`ForeignKeyStepNames.Dropped`）。付ける文の名前の求め方（`ForeignKeyStepNames.Added`）で求めると、名前の無い同じ組の 2 本目が 1 本目の名前になり、組めずに 1 本目を名指しし、付け直しを新しい外部キーの順位へ回す。取込は 4 方言で外部キーに必ず名前を付けるので利用者の経路には届かず、網の入力でだけ届く。ガード：期待の行 `plan-unnamed-live-fk-second-rebuild-around-row-dependent-change-names-its-own-name`・能力の組の網の関係 N2。
- 固定は期待の行 `plan-type-mismatch-*`・`plan-implicit-fk-*`・`plan-implicit-unique-rebuild-with-dropped-column-drops-only`・`plan-vanishing-endpoint-*`・`plan-fk-alter-one-of-duplicated-rebuild-dialect-keeps-the-other`・`SyncPlannerTests`「外部キーの両端の型: 長さの違いを許すのは同じ文字列型どうしだけ」・`DialectDdlTableTests`（行 `ddl-fk-string-length-mismatch-note`・`ddl-fk-type-mismatch-*`）・実 DB の行（`realdb-fk-type-mismatch-one-end-warns-and-fails`・`realdb-fk-drop-one-of-same-pair-keeps-the-other`・`realdb-drop-parent-and-child-tables-without-fk-drop`・`realdb-pk-change-with-child-drop-table`・`realdb-rebuild-alter-one-of-duplicated-fks-keeps-other`・`realdb-drop-referenced-end-without-fk-drop-table`／`realdb-drop-referenced-end-without-fk-drop-column`・`realdb-drop-parent-only-rebuilds-child-without-fk-with-rows`／`realdb-drop-parent-only-rebuilds-child-without-fk-without-rows`・MySQL の `realdb-mysql-fk-type-mismatch-one-end-warns-and-keeps-fk`・`realdb-mysql-fk-alter-to-other-column`・`realdb-mysql-fk-same-column-one-mismatched-changes-nothing`・`realdb-mysql-drop-referenced-end-without-fk-drop-table`／`realdb-mysql-drop-referenced-end-without-fk-drop-column`）・同期計画の総当たり。mutation 実証済み。

### 既存の行で断られうる文の名指し

- 計画は列の値・表の行の有無を見ないので、既存の行（とモデルの外の依存）で断られうる文を止められない。DDL が暗黙コミットされる方言では、その文の前で外して後ろで付け直す制約・後の変更のために外す制約が、断られると外れたまま残る（部分適用）。`RefusableChangeNaming.Constraints` が前寄せを当てた後の文の並びを歩いてそれを全部名指しし、止めない（docs/database.md「Running the script」の約束）。
  - 断られうる文（`Refusals.FindRefusable`）：主キーの付与・一意制約の追加（作り直しの追加を含む）・NULL を許す列の NOT NULL 化（主キーの変更が足す NOT NULL 化を含む）・型を狭める列の変更（`SyncDialectCapabilities.NarrowsType`＝広げるか同じと証明できる変更だけを偽にする保守側。同じ系統の文字列は固定長と可変長の入れ替えも長さで比べる＝`StringLengthChange`）・`AddNotNullColumnRequiresEmptyTable` の方言の NOT NULL の列の追加（図は既定値を持たない）・`AlterColumnBlockedByComputedColumn` の方言の、DB に計算列のある表の普通の列の変更（式の依存は図に無いので表の単位）。
  - 主キーの付与と一意制約の追加は、新しい列集合が live の主キーか一意制約の列集合を含み、その列がどれも live にあって型を変えないときだけ断られないと証明する（主キーの並べ替えは名指ししない）。同じ計画でその live のキーを外していても、計画は値を変えないので証明に使う。主キーの付与の NULL は、主キーの変更が先に NOT NULL 化を足すのでそちらが名指しされる。
  - 名指しは `SyncPlanWarningKind.ConstraintRebuildAroundRowDependentChange`（制約ごとに 1 件・Detail は「制約名 (断られうる文, …)」）。同じ文が列が空でないと通らない変更（`RequiresEmptyColumn`）にも当たるときは、`ConstraintRebuildAroundEmptyColumnChange` だけで名指しする（1 回の走査で 2 種類を分ける）。
  - 断られうる文そのものが付け直す制約（作り直す一意制約・主キーの変更の主キー）も名指しする（その文が断られると旧の形も残らない）。付け直さない暗黙の削除（端が消える外部キー・構成列が消える一意制約・主キーが置き換える一意制約）も、後ろの断られうる文があれば名指しする（消すはずの表・列や置き換える主キーが来ない）。利用者が選んだ削除は消えてよいので数えない。2 つの警告とも同じ規則。
  - 暗黙コミットの方言だけ（トランザクションの方言は失敗で巻き戻る）。
- 壊すと：重複・NULL・長すぎる値・行のある表・計算列で文が断られたとき、外部キー・主キー・一意制約が知らされずに消えたまま残る（MySQL・Oracle）。
- ガード：期待の行 `plan-row-dependent-change-pk-onto-duplicates-names-primary-key`・`plan-row-dependent-change-unique-rebuild-onto-duplicates-names-unique`・`plan-row-dependent-change-not-null-on-fk-column-names-fk`・`plan-row-dependent-change-add-unique-names-collateral-fk`・`plan-row-dependent-change-add-not-null-column-names-collateral-fk`・`plan-row-dependent-change-computed-source-names-collateral-fk`・`plan-row-dependent-change-pk-reorder-is-not-named`（5 方言の実 DB・暗黙コミットの方言は名指しした制約だけが外れたまま残る）・`plan-empty-column-change-intermediate-rebuild-names-fk`（同じ文は列が空でないと通らない変更の警告だけ）・生成器の行の水準（必須の形 `duplicate-data`・`null-data`・`shorten-below-data`・`not-null-add-on-rows`・`computed-source-change`）・静的な規則 R10 と D9（`tests/QuickER.Tests.Integration/Dialects/SyncPlanFuzz/CLAUDE.md`「静的な規則」）・表明 1b の受け入れ（注 `warned-row-dependent-change-exempt`）・`SchemaSyncWarningTextTests`・`DialectDocsTableTests.ForeignKeyRebuildTable_MatchesDeclarations`。

### 選んでいない変更を前提にする項目

- 外部キー・一意制約・主キー・説明の追加が選んでいない表・列の追加を、列の NULL 許容化が選んでいない主キーの変更を前提にするとき、`SelectionRules.ExcludeItemsRequiringUnselected` がその項目を外し、`SyncPlanWarningKind.SelectedItemRequiresUnselectedItem`（Detail＝「対象 (足りないもの)」）で実行確認に出す。
  - そのまま実行すると、逐次 DDL は存在しない表・列を参照して失敗し、SQLite の再構築は部分的な主キー・存在しない表を指す外部キーのまま成功する。
- 一意制約の追加・作り直しが、同期後も残る live の一意制約と同じ列集合なら、その一意制約の変更を前提にするものとして外す（「一意制約の差分」）。
- 依存先を自動で選ばない（利用者が選ばなかった変更、特に破壊的な変更を入れることになる）。
- 外部キーの作り直しは展開する前に判定する（削除と追加をまとめて外す＝削除だけが走らない）。
- 型の変更も含む列の NULL 許容化は、NULL 許容化だけを外して型の変更は残す。
- 外した選んだ項目は警告と一緒に `SyncPlan.ExcludedItems` へ積む（積むのは取り下げを当てる `SelectionWithdrawals.Apply` と、前寄せの組から取り下げた項目を末尾に足す計画器）。同期計画の総当たりは外した項目をこの一覧で引く。
- 選んだ外部キー・一意制約の追加・作り直しの名前が、同期後も残る DB の制約（選んだ削除・作り直しで外れず、表・構成列も消えないもの）と、方言の名前の範囲（`SyncDialectCapabilities.ConstraintNameScope`＝`ConstraintNamesCollide`）で同じ（`SchemaNames` の規則）なら、その制約の削除を前提にするものとして外す。同じ定義の外部キーを 2 本持てない方言（`AllowsDuplicateForeignKeyDefinitions` が偽＝Oracle の ORA-02275）では、子の表・親の表・列の組の集合が同じ残る外部キーも前提にする。Detail の括弧は「残る制約の表.名前」。判定は `RemainingLiveConstraints` の 1 か所で、一意制約の列集合の重なり（`RemainingLiveUniquesOn`）と同じ流れに置く。
  - 差分の既定の選び方は追加を選び、削除を選ばない。名前を保って制約を別の表へ移した図（Oracle では名前だけを変えた外部キー）をそのまま流すと、追加が名前の重なり（SQL Server 2714・MySQL 1826・PostgreSQL 42P07・Oracle ORA-02264）か定義の重なり（ORA-02275）で断られ、暗黙コミットの方言では前の文が適用されたまま残る。
  - 追加する表の一意制約も同じ判定で数え、重なれば表の追加を外す（`SelectionRules.ExcludeTablesWithCollidingConstraintNames`＝列の無い表の追加の除外と同じく依存の除外より前の段。外した表を前提にする項目は依存の除外が外す。主キーと同じ列の一意制約を持てない方言で表の作成が省く一意制約は数えない）。表の名前を変えて古い表の一意制約の名前を引き継ぐ図を、古い表の削除を選ばずに流す形。
  - 主キーの名前は取り込まない（同期は実行時に逆引きする）ので数えない。
  - MySQL（`ConstraintNameScope.ForeignKeysInSchemaAnyInTable`）は、同じ表の外部キーと一意制約の名前を重なるとみなす。外部キーの列に索引が無ければ DB が外部キーと同じ名前の索引を作り、一意制約の索引の名前とぶつかる（1061）。限界：外部キーの列を先頭に持つ索引が既にあれば DB は索引を作らず名前を共有できるが、取込が一意でない索引を持たないので判定できず、実際には通る同期（外部キーの列に一意制約がある表への同じ名前の外部キーの追加など）も外して名指しする。ガード：期待の行 `realdb-name-collision-fk-named-like-unique-on-other-column-of-same-table-default-selection`・`realdb-name-collision-unique-with-excluded-key-column-drop`（MySQL の実 DB）。
  - 名前の重なる表の追加と依存の除外は、ほかの除外と一緒に、何も外れなくなるまで繰り返す（`SyncPlanner.ExcludeUntilStable`。次の節「除外の周」）。名前の重なりの判定は選んだ表・列の削除で消える制約を数えないが、その削除自身が依存の除外で外されうる（主キーの変更を選ばない主キーの列の削除）。1 回で止めると、外された削除の制約が残るのに、その名前と重なる追加が外されずに断られる。周の数が選んだ項目の数を超えたら例外。ガード：期待の行 `realdb-name-collision-new-table-with-excluded-key-column-drop`・`realdb-name-collision-unique-with-excluded-key-column-drop`（網と 5 方言の実 DB）。
  - ガード：期待の行 `plan-name-collision-*`（網・5 方言の能力）・`realdb-name-collision-*`（5 方言の実 DB）・静的な規則 R4・R12（`tests/QuickER.Tests.Integration/Dialects/SyncPlanFuzz/CLAUDE.md`「静的な規則」）・生成器の水準（必須の形 `fk-moved-to-other-child`・`fk-rename`・`unique-moved-to-other-table`・`new-table-takes-released-name`）・`DialectDocsTableTests.SyncCapabilitiesTable_MatchesDeclarations`。
- 固定は期待の行 `plan-dependency-*`・`plan-rebuild-fk-to-unselected-new-table-is-excluded-with-warning`・実 DB の行（`realdb-fk-add-on-unselected-new-column-skipped-with-warning`・SQLite の `realdb-pk-on-new-column-without-add-column-is-excluded`／`realdb-pk-on-new-column-with-add-column-applies`・`realdb-fk-to-unselected-new-table-skipped-with-warning`）・`SchemaSyncWarningTextTests`（警告の全種別に実行確認の文言がある）。mutation 実証済み。

### 除外の周

- 計画が選んだ項目を外す規則（`Sync/Selection/` の partial クラス `SelectionRules` の `Exclude*`・1 規則 1 ファイル＝`SelectionRules.{規則}.cs`）は、何も外れなくなるまで繰り返す周（`SyncPlanner.ExcludeUntilStable`）の中からだけ流す。
  - 規則はどれも同じ形（`SelectionRule`）：いま選んでいる項目の並びとその同期後の事実（`SelectionState`）と、選んだ項目に依らない文脈（`SelectionContext`＝方言の宣言・計画の入力・live の状態・再構築の方言か・作り直さない表の一覧 2 つ）を受け取り、取り下げ（`Withdrawal`＝外す項目・名指しの警告の並び・あれば代わりの項目）の並びを返す。規則は受け取った並びも警告・外した項目の一覧も書き換えない。掛けるかどうかの門（方言・一覧が空か）は規則の中に置く。
  - 規則の並びは規則の表（`SelectionRules.cs` の静的な配列 `SelectionRules.BeforeRounds`・`SelectionRules.EachRound`）が宣言する。周は表を順に回し、規則が返した取り下げを次の規則の前に当てる（後ろの規則は前の規則が外した後の並びを受ける）。表を読むのは周だけ。
  - 当て方は `SelectionWithdrawals.Apply` の 1 か所：取り下げの順に、警告を並びの順に積み（同じ値の警告が既に積まれていれば積まない）、項目を外した項目の一覧へ積み、並びからその項目を落とす（代わりの項目があれば元の位置へ入れる）。警告の並びと外した項目の並びは、規則が返した取り下げの並びで決まる（列が残らない表の規則だけ表ごとにまとめた順・ほかは項目の順）。警告の無い取り下げは断る。
  - 代わりの項目を返すのは、選んでいない変更を前提にする項目の規則の 1 か所だけ（型の変更も含む列の変更は、元の項目を外し、NULL 許容化だけを外した複製を元の位置に残す）。
  - 列の無い表の追加（`ExcludeTablesWithoutColumns`）は項目だけで決まるので、周の前に 1 回だけ外す（`SelectionRules.BeforeRounds`）。
  - 周の中の順（`SelectionRules.EachRound`）：名前の重なる表の追加 → DB のビュー・トリガー・索引を壊す変更 → 選んでいない変更を前提にする項目 → 列が残らない表の列の削除 → 取込が捨てた物のある表の作り直し → DB の外の物で作り直せない表の作り直し（この 2 つは再構築の方言だけ。本体は `ExcludeRebuildsOfProtectedTables`）→ 同期後に両端の型が食い違う外部キー（逐次 DDL の方言だけ）→ NOT NULL の列への SET NULL（作れない方言だけ）。
  - 同期後の事実は、周が状態（`SelectionState`）として規則へ渡し、規則は自分で求め直さない（`PostSyncSchema.From` を呼ばない）。状態は 1 つの並びに 1 つで、作り直しを展開しない並びの事実（`PostSync`＝`PlanStage.ExclusionRound`）・削除と追加へ展開した並びの事実（`ExpandedPostSync`＝`PlanStage.ExclusionRoundExpanded`）・同期後も残る DB の制約（`RemainingConstraints`＝展開した並びから）を、初めて読まれたときに作って覚える。周は、取り下げを当てて並びが変わるたびに状態を作り直す（取り下げが無ければ次の規則も同じ状態を読む）。周が止まったときの状態は、周の後の警告（`WarnSetNullForeignKeysOnNotNullColumns`）も読む。周ごとに 1 つへ寄せない（同じ周の後ろの規則が、前の規則が外す前の並びか事実で判定すると、外す項目が変わる。普段の網の 25,996 ケースのうち 20 ケースで計画が変わった）。取り下げのたびに表の先頭へ戻る形にもしない（名指しの理由が入れ替わるケースが出る）。
  - 規則の並びは仕様。1 つの項目が 2 つの規則に当たるときは、規則の表で前の規則が名指しし、後ろの規則はその項目を外した後の並びを受ける。警告と外した項目は、項目の並びではなく規則の表の順に並ぶ。
    - 前提の欠け → 列が残らない表：主キーの列ともう 1 本の列だけの表で両方の列の削除を選び、主キーの変更を選ばないと、主キーの列の削除だけが前提の欠けで外れ、もう 1 本の列の削除は残る（入れ替えると両方が「列が残らない表」で外れる）。
    - 前提の欠け → 型の食い違い → SET NULL：選んでいない変更を前提にする外部キーの追加は、両端の型が食い違っていても、子の列が NOT NULL の SET NULL でも、前提の欠けで名指しする。型の食い違いと SET NULL の両方に当たる外部キーの追加は、型の食い違いで名指しする。
    - 名前の重なる表の追加 → 前提の欠け：両方が同じ同期にあれば、警告と外した項目は名前の重なりが先。
    - SQLite で掛かる規則の並び（DB のビュー・トリガー・索引を壊す変更 → 前提の欠け → 列が残らない表 → 取込が捨てた物のある表 → DB の外の物で作り直せない表）は、1 つの項目が 2 つの規則に当たる実 SQLite の期待の行が固定する（取込警告・作り直しを止める物・判定器は実 DB の取込からしか作れない）。DB のビュー・トリガー・索引を壊す変更の 1 つの取り下げが持つ警告の並び（索引 → ビュー → トリガー）も行が固定する。
  - 1 周目の呼び出しの並びは、除外を 1 回ずつ流すときと同じ。2 周目に入らない入力では、計画・警告・外した項目・並びは変わらない。
- 理由：どの除外の判定も、ほかの選んだ項目を材料にする（名前の重なりは選んだ削除と作り直しで消える制約を「残らない」と数え、依存の除外は選んだ追加と主キーの変更を「同期後に在る」と数える）。周の外で項目を外すと、その項目を数えた判定が古いまま残り、追加が名指しなしに DB で断られる。
  - 列が残らない表として外れた列の削除の一意制約と、同じ名前の一意制約の追加（SQL Server 2714・PostgreSQL 42P07・Oracle ORA-02264）。
  - 作り直さない表の主キーの変更が外れた後の、その新しい主キーを参照する別の表の外部キーの追加（SQLite の `foreign key mismatch`）。
  - 型の食い違いで外れた外部キーの作り直しの古い外部キーと、同じ定義の外部キーの追加（Oracle ORA-02275）。
- 止まる理由：どの除外も項目を外すだけで戻さない。周ごとに少なくとも 1 つ外したときだけ次の周へ進み、外すほど残る制約と足りない前提は増えるだけ。除外を足すときは、前の周の判定が後の周の除外で「外さなくてよかった」に変わらないことを確かめる。
- 周の中で積む警告は、外した項目の名指し（取り下げの警告）だけにする。項目を外さずに積む警告は、周の後に、外し終えた項目で 1 回だけ決める。
  - NOT NULL の列への SET NULL は関数を 2 つに分ける。作れない方言（`SetNullRequiresNullableChildColumn`）で外す `ExcludeSetNullForeignKeysOnNotNullColumns` は周の中、作れる方言で警告だけを積む `WarnSetNullForeignKeysOnNotNullColumns` は周の後（展開の前）。判定の本体は `FindSetNullForeignKeysOnNotNullColumns` の 1 か所。
  - 警告だけの側を周の中へ置くと、警告した外部キーが後の周で別の理由で外れたとき、計画に無い外部キーの警告が残る。
  - 外す関数は `Exclude`、警告だけの関数は `Warn` で始める（ソースの規則がこの名前で見分ける）。
- 周の約束そのもの（外した項目を最初から選ばずに計画を作り直しても、新しく外れる項目が無い）は、能力の組の網の関係 N3 が、同期計画の生成器の除外の連鎖の水準（9 本目の乱数の列）の種と期待の行で見る（`tests/QuickER.Tests.Integration/Dialects/SyncPlanFuzz/CLAUDE.md`「能力の組の網」「生成器と方言の宣言」）。
  - 前寄せの中の取り下げ（`UniqueRebuildWithdrawal`）は周の外で起きるが、計画器は取り下げた項目を除いて選択（除外の周）からやり直すので、取り下げの前に決めた判定（警告・外部キーの追加の並び）は残らない（「位置の状態と歩き」）。除外の不動点（関係 N3）は取り下げのあるケースを含む製品の計画に掛かり、網の既知の失敗の一覧は空。
- ガード：能力の組の網の関係 N3（`SyncPlanCapabilityNetRunner.ExclusionFixedPointProblems`）・`SourceRuleTests` の規則 `sync-exclusions-inside-stable-loop`（範囲は Provider のプロジェクトの全体。`SelectionRules` の `Exclude*` への参照＝呼び出しとメソッドグループのうち、規則の表 2 つの初期化子の中でも `Exclude*` の中でもないものと、周の外の規則の表の読み。周そのもの（`ExcludeUntilStable`）は数えない。自己検査の行 `sync-exclusions-inside-stable-loop-forms`・`sync-exclusions-inside-stable-loop-inactive-branch`）・`ParityTests` の行 `sync-selection-rules-table-complete`（`SelectionRules` の規則の形の `Exclude*` の宣言と、規則の表 2 つの和の要素が、ファイルをまたいで多重集合で一致する。表に無い規則・2 回ある規則・規則の形でない要素を名指しする。読み取りの自己検査の行 `sync-selection-rules-declarations-probe`・`sync-selection-rules-entries-probe`）・規則 `sync-post-sync-schema-single-source`（規則のファイルで `PostSyncSchema.From` を呼ぶと、一覧に無い出現として赤。状態の 2 か所だけを一覧に載せる）・規則の並びと取り下げの当て方の期待の行（網で流れる）`plan-selection-order-pk-column-drop-is-named-as-missing-pk-change-and-other-drop-stays`・`plan-selection-order-fk-add-missing-column-and-type-mismatch-is-named-as-missing-column`・`plan-selection-order-fk-add-missing-prerequisite-and-set-null-on-not-null-is-named-as-missing-prerequisite`・`plan-selection-order-colliding-table-is-named-before-missing-prerequisite`・`plan-selection-order-fk-add-type-mismatch-and-set-null-on-not-null-is-named-as-type-mismatch`・`plan-selection-withdrawals-keep-item-order`（1 つの規則の取り下げは取り下げの順に当てる）・`plan-selection-replacement-keeps-its-position`（代わりの項目は元の位置に入る）・状態の作り直しの期待の行（網で流れる）`plan-selection-facts-follow-withdrawal-pk-change-excluded-keeps-set-null-fk-add`（外れた主キーの変更を、同じ周の SET NULL の規則も周の後の SET NULL の警告も NOT NULL 化に数えない）・`plan-selection-facts-follow-replacement-kept-not-null-excludes-set-null-fk-add`（代わりの項目に差し替えた後の並びの事実で判定する）・SQLite の規則の並びの期待の行（実 SQLite）`realdb-selection-order-view-broken-by-key-column-drop-is-named-as-view`・`realdb-selection-order-key-column-drop-on-import-discarded-table-is-named-as-missing-pk-change`・`realdb-selection-order-all-column-drops-on-import-discarded-table-are-named-as-left-without-columns`・`realdb-selection-order-rebuild-of-table-discarded-and-blocked-is-named-as-import-discarded`・`realdb-selection-warnings-view-then-trigger-for-one-item`・期待の行 `realdb-name-collision-unique-with-excluded-last-column-drop`（網と 5 方言の実 DB）・`realdb-rebuild-skipped-when-import-discarded-excludes-fk-requiring-its-pk-change`（実 SQLite）・`plan-type-mismatch-excluded-alter-keeps-live-fk-with-same-definition-as-added-fk`・`plan-type-mismatch-excluded-add-with-same-definition-is-not-warned-for-set-null`（網と実 Oracle）。mutation 実証済み（規則の表の並びの入れ替え・状態を作り直さない・周ごとにしか作り直さない・展開の取り違え・規則のファイルで同期後の事実を求め直す）。

### 計画の文と由来と対

- 計画の 1 文（DB へ流す変更 1 つ）の型は `Sync/Steps/` の `SyncStep`（閉じた階層・派生 15：表の追加と削除・列の追加と変更と削除・主キーを外すと付ける・一意制約を外すと付ける・外部キーを外すと付ける・表と列の説明・表の作り直し・列を動かす）。文は方言の SQL を組み立てるのに要る値だけを持つ。文の並びの計画は `SyncPlan`（文の並び `Steps`・先頭から前寄せした文の数 `MovedFirstCount`・警告・外した項目。`Rebuilds`・`Reorders` は文の並びから導く読み取り専用のメンバー）。文の型は public（方言ビルダーが別のアセンブリにあるため）。
  - 由来（`StepOrigin`）は 3 つ：選んだ項目から来た文（`SelectedOrigin`。作り直しを展開した削除と追加は元の作り直しの項目、主キーを外す文と付ける文は元の主キーの変更の項目を持つ）・計画器が足した文（`ImplicitOrigin`＝理由 `ImplicitStepReason` の 7 つと、足した側が組み立てた説明）・複数の項目をまとめた文（`MergedOrigin`＝表の作り直し）。
  - 対（`ReaddPair`）は、制約を外す文と付け直す文が共有する参照の印。対になるのは、外部キー・一意制約の作り直しを展開した削除と追加、外部キー・一意制約・主キーの暗黙の外し直し、主キーの変更の解除と付与。利用者が別々に選んだ削除と追加は、同じ名前でも対にしない。
  - 名前の無いこともある制約の名前（`null`）は、差分の項目と同じ意味（外すときは同期スクリプトが DB から引き、付けるときは作る）。
- 計画は文の並びとして作り（文を作るのは組み立て＝`Sync/Scheduling/` と `Sync/TableRebuilds/TableRebuildScheduler`）、方言ビルダーと入口の検証は文を読む（「文を読む方言ビルダー」）。画面が読むのは警告と `Rebuilds` だけ。
  - セクションと差分の項目で表した形（`plan.Sections`）は製品に無い。同期計画のテストが読む面で、Integration のテスト側の写し（`tests/QuickER.Tests.Integration/Dialects/SyncPlanFuzz/SyncPlanSections.cs`）が文の並びから導く（`tests/QuickER.Tests.Integration/Dialects/SyncPlanFuzz/CLAUDE.md`「能力の組の網」）。製品のコードは、文から差分の項目を作らない。
- 計画器は、選択の前半（`SyncPlanner.PrepareSelection`）で自分で作った差分の項目に印（`SchemaDiffItem.PlanMark`＝`PlanItemMark`。internal）を付ける：計画器が足した項目の理由、作り直しを展開した項目の元の項目、外す項目と付け直す項目の対。利用者が選んだ項目と、選んだ項目の一部だけを外して残した代わりの項目は印を持たない。
  - 付けるのは、作り直しの展開と同期後の状態の差の項目を足す関数が項目を作る所だけ（`new SchemaDiffItem { … PlanMark = … }` の 8 か所＝`ExpandAlters` の 4・`InjectPrimaryKeyNotNullChanges`・`InjectVanishingUniqueConstraintDrops`・`InjectUniqueConstraintsReplacedByPrimaryKey`・`BuildImplicitForeignKeyDrop` の 1 つずつ）。印を持つ項目の複製は印をそのまま持つ。
  - 計画・同期スクリプト・警告・画面は印を読まない。読むのは、組み立てが項目を文へ写す `SelectedSteps` の 2 か所（文の由来と、展開した項目の対）だけ。
- 理由：由来と対は作った側しか知らないので、作る所で記録する（後から識別の型の集合・参照の等価・名前・後ろの項目の走査で推定すると、段ごとに推定が割れる）。方言ビルダーが文のほかの形を読むと、計画器の作った文と違う物から SQL を組む経路ができる。印を付け忘れると計画器が足した項目が利用者の選んだ項目に見え、計画・画面が印に頼ると印を持たない項目と振る舞いが割れる。
- ガード：能力の組の網の関係 N4（`SyncPlanCapabilityNetRunner.StepPlanProblems`＝由来と対の形・前寄せの印・テスト側の写しの形の自己検査ほか。`tests/QuickER.Tests.Integration/Dialects/SyncPlanFuzz/CLAUDE.md`「能力の組の網」）・期待の行 `plan-implicit-unique-rebuild-on-dropped-table-drops-only`（外すだけの暗黙の一意制約の外し直しは対を持たない）・`SourceRuleTests` の規則 `sync-plan-mark-owners`（`src/QuickER.Provider` の `PlanMark` の参照を「含むメソッド: init / read」で集め、付ける所と読む所の一覧と件数まで照合する。付ける所の下限 8・読みは `SelectedSteps` の 2 か所だけ。自己検査の行 `sync-plan-mark-owners-forms`・`sync-plan-mark-owners-inactive-branch`）。

### 文を読む方言ビルダー

- 方言ビルダー（基底 `SyncScriptBuilderBase` と 5 方言）と入口の検証 3 つ（`SqlTypeText`・`SqlNameText`・`SqlTableShape` の `Validate(SyncPlan)`）は、計画の文（`SyncPlan.Steps`）だけを読む。差分の項目は読まない。
- 文をセクションへまとめる所は `Sync/Steps/StepSections.cs` の `StepSections.Of(plan)` の 1 か所：文を順に見て、隣り合う文を「種類・主キーのフェーズ・前寄せか（文の位置が `SyncPlan.MovedFirstCount` より前か）」が同じ間 1 つの `StepSection` にまとめる。主キーを外す文は解除のフェーズ、付ける文は付与のフェーズ。表を作り直す文（`SyncPlan.Rebuilds`）と列を動かす文（`SyncPlan.Reorders`）はセクションに入れない。
  - 見出し（`-- ===== {種類} ({n} items) =====`・主キーは `{種類}: {フェーズ}`）は、セクションの種類とフェーズの列挙の名前と、文の数から作る。
- 基底は、セクションの文を文の型で `Append*`（引数は文の型）へ振り分ける（`AppendStep`）。Oracle は基底を継がず、自前の `AppendStep` を持つ。SQLite は `Build` を上書きし、種類でセクションを選んで再構築と織り交ぜる。
- 文の型で振り分ける所は 4 つ（`StepSections.SectionOf`・基底の `AppendStep`・Oracle の `AppendStep`・`SqlNameText.PlanNames`）。どれも、セクションに入る文の型の全部を腕に持ち、知らない型は型の名前を名指しした `InvalidOperationException` にする（黙って飛ばさない）。
  - 壊すと：文の型を足したのに振り分けへ足し忘れると、その文が SQL に出ないか、その名前が検証されない。
  - ガード：`ParityTests` の行 `sync-step-switches-cover-every-step-type`（`Sync/Steps/SyncStep.cs` の派生の宣言から、セクションに入らない 2 つ〔表の作り直し・列を動かす。理由つきで宣言〕を除いた型と、4 つの振り分けの腕の型が、多重集合で一致する。足りない型・余る型・2 回ある型を名指しする。読み取りの自己検査の行 `sync-step-switches-declarations-probe`・`sync-step-switches-entries-probe`）。セクションのまとめ方は、期待の行 `plan-sections-are-in-fixed-order`（主キーのフェーズ）と能力の組の網の関係 N4 の前寄せの印が見る。mutation 実証済み（基底・Oracle の腕を 1 つ落とす・鍵から主キーのフェーズを外す・鍵から前寄せを外す）。読みの限界：腕に条件（`when`）を足して実質当たらなくした形は、照合の行は拾わない（方言の DDL の表の行が拾う）。
- 付ける外部キーの名前は、文の名前（差分が求めた名前か、リレーションの名前）か、子と親の表名からの既定名（`SyncScriptBuilderHelper.ForeignKeyNameToAdd`＝`ForeignKeyStepNames.Added`。計画器の歩きと同じ求め方）。項目から求める名前（`SyncPlanner.SelectedForeignKeyName`＝選択と評価の警告）と同じ名前になる。
- 計画を手で組むテストは、共有の補助（`tests/QuickER.Tests/Provider/HandBuiltSyncPlans.cs`）で文から組む（項目から組むときは、製品の `SelectedSteps.From` で文へ写す）。

### 逐次 DDL の組み立て

置き場所は `src/QuickER.Provider/Sync/Scheduling/`（名前空間 `QuickER.Provider.Sync.Scheduling`・internal）。

- この節は、逐次 DDL の方言（SQL Server・PostgreSQL・MySQL・Oracle）の「文の並びを組み立てる段」と、計画器の入口（逐次 DDL の方言とテーブル再構築の方言の両方）を書く。再構築の方言の組み立て（`TableRebuildScheduler`）は「テーブル再構築の合成」。
  - 入口は `SequentialScheduler.Schedule(postSync, live, capabilities)`。受けるのは選んだ項目の最終形（`PlanStage.Selected` の `PostSyncSchema`）・`LiveSchema`・方言の宣言で、同期後の事実を作り直さない。
  - 結果（`StepSchedule`＝2 つの方言の組み立てが共有する型）は、文の並びか、文で表せない理由（文に要るメンバーの無い項目）のどちらかと、外し直しの宣言の答え（`Required`。再構築の方言は null）と、選んだ項目の最終形のうち一意制約の削除の項目を項目の順のまま写した文（`SelectedUniqueConstraintDrops`。「計画の評価の規則」）。警告・外した項目・必ず断られる文の前寄せは持たない。
  - 組み立ては文（`SyncStep`）だけを組み、差分の項目（`SchemaDiffItem`）も警告（`SyncPlanWarning`）も作らない（再構築の合成も同じ規則の範囲）。
- 組み立ての段：
  1. 選んだ項目の最終形を、項目の順のまま文へ写す（`SelectedSteps`）。主キーの変更は解除の文と付与の文になり、新しい主キーの列を持つときは同じ対を持つ。作り直しの展開と、同期後の状態の差として足す項目（主キーの列の NOT NULL 化・端が消える外部キー・列と消える一意制約・主キーが置き換える一意制約）は、選択の前半（`SyncPlanner.PrepareSelection`）が作った項目を写す。文の由来と展開した項目の対は、項目の印（`PlanMark`）から決める。
  2. 外し直しの宣言から、選んだ文を流す間外れていなければならない制約を集める（`RebuildDeclarations.Collect`）。
  3. 同じ計画が明示に外す制約（選んだ削除・作り直しの展開・同期後の状態の差の削除）と、付け直せない制約（`UnreaddableConstraints`）を除く。
  4. 同期後に在れば外す文と付け直す文の対を、無ければ外す文だけを作る（`Readds`）。
  5. `[一意制約を外す文, 外部キーを外す文, 選んだ文, 外部キーを付け直す文, 一意制約を付け直す文, 主キーの外す文と付け直す文]` の順に並べる（外す文は選んだ文より前、付け直す文は後ろ）。
  6. `ReferencedKeyRequiresSameColumnOrder` の方言では、外部キーを付ける文の列の組を参照先の同期後のキーの並びへ並べ替える（`ForeignKeyPairOrder`）。
  7. 種類の固定の順に、安定に並べる（`StepOrder`。順は「セクションの順序」と同じ）。
  8. 暗黙コミットの方言では、外部キーを付ける文を断られにくい順に並べる（`ForeignKeyAddOrder`。順位は「外部キーの追加の並び」と同じ）。
  9. 列順を同期する方言では、列を動かす文を足す（`SyncPlanner.BuildReorderPlans` を共有）。
- 外し直しの宣言（`RebuildDeclarations` の partial・1 宣言 1 ファイル＝`RebuildDeclarations.{宣言}.cs`）は、どれも同じ形（`RebuildDeclaration`）：選んだ文・同期後の事実・DB の今の図・方言の宣言（`SchedulingInput`）を受け、外れていなければならない DB の今の制約（`LiveConstraint`＝外部キー・一意制約・主キー）を返す。掛けるかどうかの門（方言の宣言）は宣言の中に置く。宣言の並びは宣言の表（`RebuildDeclarations.All`）が持つ。
  - `RequireForeignKeysOffDroppedPrimaryKeys`：主キーを変える表を参照し、DB の今の主キーが支えうる外部キー（どの方言でも）。
  - `RequireForeignKeysOffUniquesReplacedByPrimaryKey`：参照する列が新しい主キーと同じ列の集合で、同じ計画が外す一意制約が支えうる外部キー（どの方言でも）。
  - `RequireForeignKeysOffAlteredColumns`：変える列を端に持つ外部キー（`AlterColumnRequiresForeignKeyRebuild` の方言）。
  - `RequireForeignKeysOffDivergingColumnChanges`：組の両端の型がこの同期で決まり、片側だけ変えた途中の状態のどちらかが並べられない外部キー（`AlterColumnRequiresForeignKeyRebuild` が偽の方言）。
  - `RequireForeignKeysOffLostIndexes`：外す主キー・一意制約の索引の先頭に、子の列が乗る外部キー（`ForeignKeyRequiresLeadingIndex` の方言）。
  - `RequireUniqueConstraintsOffAlteredColumns`・`RequirePrimaryKeysOffAlteredColumns`：変える列を構成列に持つ一意制約・主キー（`AlterColumnRequiresKeyConstraintRebuild` の方言）。
  - 宣言の答えは集合として合わせ、外す文の並びは DB の今の図の制約の並びで決める。宣言の表の並びは結果に効かない（選んだ項目を外す規則の表と違う点）。
- 付け直せない制約（同期後に両端の型が食い違う外部キー・子の列が同期後に NOT NULL になる SET NULL の外部キー）は、外す理由に依らず外さない。判定は述語（`HasPostSyncTypeMismatch`・`BlocksSetNullRebuild`）を通す。
- 制約 1 つと状態で決まる事実の述語は、`SyncPlanner` の internal static に在り、選択・組み立て・評価が共有する（`SyncPlanner.` で呼ぶ。`CanSupportForeignKey`・`DependsOnOldPrimaryKey`・`IsLeadingPrefix`・`StaysCandidateKey`・`HasPostSyncTypeMismatch`・`BlocksSetNullRebuild`・`ResolveForeignKeyName`・`HasNewPrimaryKeyColumns`）。集める・足す・並べるロジックは組み立ての側（`RebuildDeclarations`・`Readds`・`ForeignKeyPairOrder`・`StepOrder`・`ForeignKeyAddOrder`）に書く。
- 外部キーの名前の求め方は 2 つ。項目から求める `SyncPlanner.SelectedForeignKeyName`（選んだ項目を外す規則と評価の規則が、警告で名指しする名前）と、文から求める `Sync/Steps/ForeignKeyStepNames.cs`（組み立てと歩き：付ける文は文の名前か既定名＝`ConstraintNames.EffectiveForeignKey`、外す文は DB の名前か `LiveSchema.ForeignKeyNames` の実効名）。外部キーの追加の並び（`ForeignKeyAddOrder`）と歩きの身元（次の節「位置の状態と歩き」）が文の側を通す。どちらもソースの規則 `sync-selected-foreign-key-names` の範囲（`SyncPlanner.cs` と `Sync/`）にある。
- 組み立てが項目の印を読むのは `SelectedSteps` の 2 か所だけ（文の由来・展開した項目の対）。
- 計画器の入口は `Sync/StepPlanner.cs` の `StepPlanner.Plan(items, capabilities, context)` で、呼ぶのは製品の入口 `SyncPlanner.BuildPlan` の 1 か所だけ。最終の計画を組む：選択（`SyncPlanner.PrepareSelection`＝除外の周・作り直しの展開・同期後の状態の差の項目・同期後の事実）→ 組み立て（`SequentialScheduler`）→ 評価（「計画の評価の規則」）→ 暗黙コミットの方言で DB の今の図があるときだけ、前寄せと取り下げの輪 → 前後の名指し（次の節「位置の状態と歩き」）。結果（`StepPlannerResult`）は、文の並びの計画（`SyncPlan`）か文で表せない理由と、最後の周の計画の事実（`Facts`）と、取り下げた項目の並び（`Withdrawn`）。
  - テーブル再構築の方言（`TableRebuildDialects.Requires`）は private の `PlanTableRebuilds` が組む：選択（同じ `PrepareSelection`）→ 組み立て（`TableRebuildScheduler`。「テーブル再構築の合成」）→ 評価。前寄せ・取り下げ・前後の名指しは無く、周は 1 回だけ。計画の入力か DB の今の図が無ければ `InvalidOperationException`。
- 壊すと：組み立てを変えると、同期スクリプトの文の並び・前寄せ・外し直し・警告が黙って変わる。表から抜けた宣言は一度も流れず、外れていなければならない制約を外さない計画が DB で断られる。入口をほかの所から呼ぶと、製品の計画と別の経路で計画を作る所が増える。
- ガード：期待の行（`plan-implicit-fk-*`・`plan-arrange-*`・`plan-type-mismatch-one-intermediate-state-incompatible-rebuilds-fk`〔途中の状態の宣言の「どちらか」を「両方」にすると赤〕ほか）・同期計画の総当たりの静的な規則（D9・M1 ほか）と実 DB の総当たり・能力の組の網の関係 N3・N4（`tests/QuickER.Tests.Integration/Dialects/SyncPlanFuzz/CLAUDE.md`「能力の組の網」）・`SourceRuleTests` の規則 `sync-step-planner-callers`（`src` の全体で、`StepPlanner.Plan` を呼ぶのは `BuildPlan` の 1 か所。自己検査の行 `sync-step-planner-callers-forms`・`sync-step-planner-callers-inactive-branch`）・規則 `sync-scheduling-builds-steps-only`（`Sync/Scheduling/` と `Sync/TableRebuilds/` に、差分の項目・警告の生成と項目の複製が無い。自己検査の行 `sync-scheduling-builds-steps-only-forms`・`sync-scheduling-builds-steps-only-inactive-branch`）・`ParityTests` の行 `sync-rebuild-declarations-table-complete`（`RebuildDeclarations` の宣言の形の `Require*` の宣言と、宣言の表の要素が、ファイルをまたいで多重集合で一致する。読み取りの自己検査の行 `sync-rebuild-declarations-declarations-probe`・`sync-rebuild-declarations-entries-probe`）・規則 `sync-plan-mark-owners`（印の読みは `SelectedSteps` の 2 か所だけ）。mutation 実証済み（宣言を 1 つずつ表から外す・付け直せない制約の判定を止める・明示に外す制約も外し直す・対を付けない・外す文と付け直す文の置き場を入れ替える・種類の順と外部キーの追加の順位を変える）。読みの限界：規則 2 つは型を書かない `new()` を拾わない。

### 位置の状態と歩き

置き場所は `src/QuickER.Provider/Sync/State/ConstraintState.cs` と `src/QuickER.Provider/Sync/Assessment/`（`PlanWalk`・`WalkPosition`・`ConstraintIdentity`・`Refusals`・`MoveFirst`・`RefusableChangeNaming`・`UniqueRebuildWithdrawal`）。どれも internal で、計画器（前の節）が使う。

- 位置の状態（`ConstraintState`）は、文の並びのある位置に在る制約だけを持つ：主キー（表 → 列の並び）・一意制約（`UniqueConstraintId`・表・DB の名前・列の並び）・外部キー（`ForeignKeyId`＝実効名つき・子と親の表・列の組・名前・参照アクション・DB から来たものは解決済みの外部キー）。列・型・NULL 許容は持たない（同期後の事実の正本は `PostSyncSchema` のまま）。
  - `From(live, capabilities)` が DB の今の図の制約から作り、`Apply(文)` が次の状態を返す（元は変えない）。当てるのは、主キーを外す・付ける文、一意制約を外す（識別が同じものを 1 つ）・付ける文、外部キーを外す（`FindDropped` が選んだものを 1 つ）・付ける文、表を作る文（その表の主キーと一意制約）・表を消す文（その表の主キーと一意制約）だけ。端・構成列が消える制約は、組み立てが先に外す文にしている。
  - 状態は逐次 DDL の方言の文の並びのためのもので、表の作り直しの文（`RebuildTableStep`）は当てない（同じ状態を返す）。テーブル再構築の方言の計画は歩かない（前寄せも前後の名指しも無い）ので、歩きの終わりの状態の照合も掛けない。作り直した表が同期後の図と合うことは実 SQLite の総当たりが確かめる。
  - `FindDropped` は同期スクリプトが外す外部キーと同じ選び方：文が DB の名前を持てば子の表と実効名、持たなければ親の表・列の組の集合・参照アクションが同じもののうち DB の名前の無いものを先に（列も参照アクションも同じで名前の無い 2 本は見分けられず、状態の並びの先頭を選ぶ）。
- 歩き（`PlanWalk.Over(steps, live, capabilities)`）は文の並びを 1 回当て、位置ごとに文・当てる前の状態（`Before`）・その文が外す／付ける制約の身元（`ConstraintIdentity`）と、全部当てた後の状態（`End`）を返す。身元は、外部キーは子の表と実効名（`ForeignKeyStepNames`。外す文はその位置の状態から引いたリレーションで求める）、一意制約は表と `UniqueConstraintNaming.Resolve` の実効名、主キーは表（名前は `PRIMARY KEY`）。
- 逐次 DDL の方言の文の並びを歩いた終わりの状態は、同期後に在る制約（`PostSyncSchema` の主キー・一意制約と、DB の外部キー − 外す ＋ 足す）と合う。
- 前寄せ・取り下げ・前後の名指しの定義（この節と「外部キーの暗黙の外し直し」「既存の行で断られうる文の名指し」）は文で書く。制約 1 つと事実で決まる述語は共有する（`CanSupportForeignKey`・`HasPostSyncTypeMismatch`・`UsesSetNull`・`ColumnTypeOf`・`HoldsInLiveRows`・`ResolveForeignKeyName`）。
  - 必ず断られる文（`Refusals.FindCertain`）：残す外部キー（どの外す文も外さない DB の外部キー）があるとき、文の並びの順に最初の 1 件。キーの削除（支えうるキーがこの計画で全部外れる外部キーがあり、この文のキーがその 1 つ。移すのは支えうるキーを外す文の全部を、付け直す削除を最後へ安定に並べたもの）・型の変更（外し直しの方言は残す外部キーの同期後の両端、ほかの方言は相手の端の DB の今の型と並べられない）・SET NULL の子の列の NOT NULL 化（型の変更が当たらない列を変える文で続けて見る）・どれも無く外し直さない方言なら両端の変更の組。型の決まる列は計画の事実の `PostSync`（`PlanStage.Selected`）から読む。
  - 前寄せ（`MoveFirst.Apply`）は移す文を判定の順に先頭へ置き、残りは元の順。先頭へ移した数が `SyncPlan.MovedFirstCount` で、その文が前寄せのセクション（`StepSection.IsMovedFirst`）になる。
  - 取り下げ（`UniqueRebuildWithdrawal.Find`）：キーの削除の組に後ろで付け直す削除が 2 つ以上あり、組の最初の付け直す一意制約の削除の由来が選んだ一意制約の作り直し（`SelectedOrigin` で項目の種類が `AlterUniqueConstraint`）なら、その項目を取り下げる。名指しは `UniqueConstraintRebuildKeptForForeignKey`（表は外す文の表名のまま・Detail は「一意制約の名前 (阻む外部キーの実効名)」）。
  - 取り下げと輪：取り下げた項目は周をまたいで持ち、次の周の選択から除いて選択からやり直す（周の数は選んだ項目の数 + 1 までで、超えたら例外）。警告の並びは選択の名指し → 評価の規則 → 取り下げの名指し（取り下げた順）→ 前後の名指し、外した項目の並びは選択が外した項目 → 取り下げた項目。
  - 前後の名指し（`RefusableChangeNaming.Constraints`）は、前寄せを当てた後の並びを歩き直して、既存の行で断られうる文（`Refusals.FindRefusable`＝列が空でないと通らない変更と、ほかの断られうる文。同じ位置は前者だけ）の前後で外れている制約を名指しする。範囲は外す文の位置から同じ身元を付ける最初の文まで（列が空でないと通らない変更は両端を含まず、ほかの断られうる文は付ける文を含む）。付ける文が無ければ、利用者が選んだ削除（`SelectedOrigin` で項目が作り直しでない）でない限り後ろの全部。警告は外す文の位置の順に、制約ごとに 2 種別の順。規則の表（`AssessmentRules.All`）には入れない。
- 壊すと：位置の状態の当て方を誤ると、身元と残す外部キーが割れて前寄せと名指しが誤った答えになる。取り下げた後に選択からやり直さないと、取り下げる前に決めた判定（警告・外部キーの追加の並び）が見直されず、外した項目を最初から選ばない計画と食い違う（除外の不動点が破れる）。
- ガード：能力の組の網の関係 N4（歩きの終わりの状態＝違反の頭 `relation N4: end state:`・取り下げの名指しと位置＝`relation N4: withdrawal:`・前寄せの印・空振りの検査＝前寄せのある計画と前後の名指しの 2 種別を持つ計画と、決定的な行の取り下げのあるケース）と関係 N3（取り下げのあるケースを含む除外の不動点。`tests/QuickER.Tests.Integration/Dialects/SyncPlanFuzz/CLAUDE.md`「能力の組の網」）・期待の行 `plan-move-first-pair-changes-of-both-ends-oracle-like`・`plan-move-first-not-null-on-child-of-retained-non-set-null-fk-stays-mysql`・`plan-move-first-key-drop-group-two-readded-keeps-one-rebuild-mysql-like`・規則 `sync-assessment-builds-warnings-only`（`Sync/Assessment/`・`Sync/StepPlanner.cs`・`Sync/State/ConstraintState.cs` に差分の項目の生成と複製が無い）・規則 `sync-post-sync-schema-single-source`・規則 `sync-live-schema-single-source`・規則 `mutation-safe-mode-jump-guards`。mutation 実証済み（断られ方の 4 種と条件・組・名指しの範囲と枝・取り下げと輪）。

### 計画の評価の規則

置き場所は `src/QuickER.Provider/Sync/Assessment/`（名前空間 `QuickER.Provider.Sync.Assessment`・internal）。

- 項目を外さずに積む警告は、評価の規則（partial クラス `AssessmentRules` の `Warn*`・1 規則 1 ファイル＝`AssessmentRules.{規則}.cs`）が決める。規則は警告の並びを返すだけで、受け取ったものも警告を積む先も書き換えず、同期後の事実を自分で求め直さず（`PostSyncSchema.From` を呼ばない）、差分の項目を作らない。
  - 規則は 14 で、逐次 DDL の方言とテーブル再構築の方言の両方の計画に流す（方言で返すかどうかの門は規則の中。再構築の方言かは `AssessmentFacts.IsTableRebuildDialect`＝`TableRebuildDialects.Requires`）。
  - 計画の事実（`AssessmentFacts`＝周が止まったときの状態・選んだ項目の最終形の同期後の事実・DB の今の図・方言の宣言・計画の入力）だけを読む規則は 10：SET NULL を作れる方言の、NOT NULL の子の列への SET NULL の外部キーの追加（`WarnSetNullForeignKeysOnNotNullColumns`。読むのは周が止まったときの状態）・DB にある SET NULL の外部キーの子の列の NOT NULL 化・一意制約の削除が外部キーを壊しうる・足す外部キーの参照先が候補キーでない・SET DEFAULT と NOT NULL の子の列・ドットを含む表名・既定値のある列の型の種類の変更・説明の表せない文字・同期後に両端の型が並べられなくなる DB の外部キー（`WarnLiveForeignKeysWithPostSyncTypeMismatch`。再構築の方言では返さない＝外部キーの宣言を残したまま表ごと作り直すので、外し直さない外部キーとして名指しする対象が無い）・テーブル再構築で候補キーを失う DB の外部キー（`WarnForeignKeysLosingCandidateKeyByTableRebuild`。主キーを変える表を参照し、参照先の列が同期後に候補キーでなくなる外部キー。再構築の方言でだけ返す＝逐次 DDL の方言は同じ外部キーを外して付け直し、その見込みを `WarnDetachedForeignKeysAtRisk` が名指しする）。
  - 組み立ての結果も読む規則（`PlanAssessment`＝計画の事実に `StepSchedule` を添えたもの）は 4 つ：列と一緒に消える一意制約と、主キーが置き換える一意制約（暗黙の理由がその 2 つの、選んだ項目の最終形の一意制約の削除の項目を写した文 `StepSchedule.SelectedUniqueConstraintDrops` 1 つにつき 1 件。表・名前・構成列は文の値。組み立てた文の並びを読まないのは、再構築の方言ではその削除が再構築へ畳まれて文の並びに残らないため）・外れていなければならない DB の外部キーの付け直しの見込み（`WarnDetachedForeignKeysAtRisk`＝外し直しの宣言が答えた外部キーを DB の今の図の並びで見て、付け直せない SET NULL は `SetNullOnNotNullColumn`、同期後に型が食い違うものは何も返さず、候補キーを証明できないものは `ForeignKeyRebuildMayLoseCandidateKey`。再構築の方言は宣言を集めない＝`Required` が null なので何も返さない）・テーブル再構築が落とす属性（`WarnTableRebuildsDroppingColumnAttributes`＝組み立てた文のうち表を作るだけでない再構築の文と、文脈の `CREATE TABLE` の原文から）。宣言の答えは組み立ての結果の `StepSchedule.Required` に載る。
- 規則の並びは規則の表（`AssessmentRules.All`）が持ち、規則を呼ぶのは規則の表を順に流す `AssessmentRules.Assess` だけ。並びは仕様で、警告の並びは表の順と、規則が返した並びで決まる。周の直後の SET NULL の警告は、列と一緒に消える一意制約の警告より前に積む。再構築の 2 つの規則は表の末尾に「落ちる属性 → 候補キー」の順で置く。
- 計画の警告の並びは、選択の名指し（選んだ項目を外す規則の取り下げ）→ 評価の規則（表の順）→ 取り下げの名指し → 断られうる文の前後の名指し。前後の名指しは規則の表に入れない別の関数（`RefusableChangeNaming.Constraints`）で、前寄せを当てた後の並びの歩きを受ける（1 回の表の通しに乗らない。「位置の状態と歩き」）。
- 積むのは `AssessmentRules.Add` の 1 か所。同じ値の警告を重ねるかは警告の種別で決まる：`SetNullOnNotNullColumn`・`ForeignKeyColumnTypeMismatch`・断られうる文の前後の名指しの 2 種別・`UniqueConstraintRebuildKeptForForeignKey`・`UniqueConstraintDropMayBreakForeignKey` は、どの規則（選んだ項目を外す規則を含む）が積んだものでも同じ値が既に在れば積まない（同じ外部キーを支えうる一意制約を 2 つ外しても、その外部キーの `UniqueConstraintDropMayBreakForeignKey` は 1 件。実行の確認の箇条書きに同じ行を 2 回出さない）。ほかの種別は、規則が返したとおりにそのまま積む。
- 壊すと：表から抜けた規則は一度も流れず、その警告が黙って消える。規則を呼ぶ所が増えると同じ警告を 2 回積み、積む順が割れる。重ね方を変えると、同じ値の警告が増えるか減る。
- ガード：`ParityTests` の行 `sync-assessment-rules-table-complete`（規則の形の `Warn*` の宣言〔引数は `PlanAssessment` か基底の `AssessmentFacts`〕と、規則の表の要素が、ファイルをまたいで多重集合で一致する。自己検査の行 `sync-assessment-rules-declarations-probe`・`sync-assessment-rules-entries-probe`）・`SourceRuleTests` の規則 `sync-assessment-rule-callers`（`AssessmentRules.Warn*` への参照は規則の表の初期化子だけ。例外の一覧は空。自己検査の行 `sync-assessment-rule-callers-forms`・`sync-assessment-rule-callers-inactive-branch`）・規則 `sync-assessment-builds-warnings-only`（`Sync/Assessment/` と `Sync/StepPlanner.cs` に、差分の項目の生成と複製が無い。自己検査の行 `sync-assessment-builds-warnings-only-forms`・`sync-assessment-builds-warnings-only-inactive-branch`）・規則 `sync-post-sync-schema-single-source`（評価のフォルダで同期後の事実を求め直すと赤）・期待の行 `plan-warning-order-set-null-add-before-unique-dropped-with-column`（周の直後の SET NULL の警告が、列と一緒に消える一意制約の警告より前）・`plan-warning-unique-drop-may-break-fk-named-once-for-two-dropped-uniques`（重ねない種別。同じ値の 2 件目を積まない）・`plan-warning-order-rebuild-dropped-attributes-before-candidate-key-loss`（再構築の 2 つの規則の表の順）・`plan-rebuild-attribute-warns-dropped-column-attributes`・`plan-rebuild-attribute-no-warning-for-create-only`（落ちる属性の規則。種の網は `CREATE TABLE` の原文を持たないので、この 2 つの行だけが守る）。

### 構成列の削除と列が残らない表

- 主キー・一意制約の構成列の削除は、それを前提にする変更を方言に依らず同じ形で扱う。
- 同期後も主キーに入っている列の削除（`DropColumn`）は、その列を外す主キーの変更を選んだときだけ残し、選ばなければ `ExcludeItemsRequiringUnselected` が外して `SelectedItemRequiresUnselectedItem`（Detail＝「列 (PRIMARY KEY)」）で名指しする。
  - 同期後の主キーに数える主キーの変更は、この段で外れないものだけ。
- 選んだ列の削除で構成列が消える live の一意制約は、`SyncPlanner.InjectVanishingUniqueConstraintDrops` が暗黙の `DropUniqueConstraint` として足し、評価の規則 `AssessmentRules.WarnUniqueConstraintsDroppedWithColumns` が `SyncPlanWarningKind.UniqueConstraintDroppedWithColumn` で名指しする（付け直さない・同じ削除を選んでいれば足さない・消す表は対象外）。
  - 外部キーの消える端は名指ししないのに一意制約は名指しする非対称は意図したもの（一意制約は残りの列で作り直せるので判断を利用者に委ねる）。
  - 外さないと、方言ごとに列の削除が断られる・制約が黙って消える・残りの列へ縮むと結果が割れる。
- SQLite の再構築の合成は、一意制約の削除を列の変更より前に当てる（`TableRebuildDefinition.ApplyUniqueConstraintDrops`）。
- 選んだ列の削除で同期後に表の列が 1 本も残らないときは、その表の列の削除をすべて外す（`SelectionRules.ExcludeDropsLeavingNoColumns`・`SyncPlanWarningKind.TableLeftWithoutColumns`・方言に依らない）。
  - 同期後の列は「DB の列 − 選んだ列の削除 ＋ 選んだ列の追加」で数える（表の削除を選んだ表は数えない）。
  - 除外の周の中で依存の除外の直後に置く（「除外の周」）。`InjectVanishing*` より前（外した列を前提にした暗黙の削除を足さない）。主キーの解除・一意制約の削除は残す。
  - 外さないと、SQL Server は 4923 で断り、MySQL・Oracle は前の制約と列の削除が暗黙コミットで残り、PostgreSQL は列の無い表を作って取り込み直すと `TableColumnsUnavailable` になり、SQLite は列の無い再構築の表が構文エラーになる。
  - 図で列の無い表（DB に無い表）の追加は、次の節「列の無い表」が扱う。
- 固定は期待の行 `plan-drop-member-column-*`・`SyncPlannerDropMemberColumnTests.DropColumnsOnly_DropsUniqueConstraintBeforeColumnInScript`・実 DB の行 `realdb-drop-member-column-*`（5 方言）・同期計画の総当たり（列が残らない表の行 `sqlite-empty-rebuild`・`{sqlserver,mysql,oracle,postgresql}-drop-all-columns`・既知の失敗は `SyncPlanFuzzKnownFailures`）・`SchemaSyncWarningTextTests`。mutation 実証済み。

### 列の無い表

- 図で列を 1 本も持たない表は `CREATE TABLE t ( );` になり、SQL Server・MySQL・Oracle・SQLite は構文エラーで断り、PostgreSQL は取り込み直すと `TableColumnsUnavailable` になる列の無い表を作る。5 方言とも同じ扱いにする（方言の宣言に差を持たせない＝PostgreSQL も断る）。
- 判定は `SqlTableShape`（`HasNoColumns`・差分の項目は `IsAddTableWithoutColumns`）の 1 か所だけで、次の 3 つの経路が共有する。
  - DDL：5 つの入口が `SqlNameText.Validate` の直後に `SqlTableShape.Validate(diagram)` を通し、表を名指しして図全体を止める（`InvalidOperationException`・英語・`TABLE 'x'`）。
  - 同期の計画：`SelectionRules.ExcludeTablesWithoutColumns` が列の無い表の追加を外し、`SyncPlanWarningKind.TableWithoutColumns`（Detail は空）で名指しする（`ExcludedItems` へ積む・方言に依らない）。ほかの変更は実行できるので、隣の「列が残らない表」と同じく外して名指しする。
    - 依存の除外（`ExcludeItemsRequiringUnselected`）の入力の段に置く。外した表の追加を前提にする項目（その表の説明）は、依存の除外が `SelectedItemRequiresUnselectedItem` で外す。後ろへ置くと、存在しない表への説明の文が残る。
  - 同期スクリプト：5 つの入口が `SqlTableShape.Validate(plan)` で、表の追加の項目と再構築の合成後の定義（SQLite の作るだけの再構築）を見て断る（計画を経ずに組んだ計画への二重の守り）。
- 図には列の無い表が入り得る（MCP の `add_entity` の直後・GUI で全列を消した表・列を復元できなかった C# リバース・列の行の無い Excel 定義書のシート）。図の側の入口では知らせず、DDL と同期の入口で名指しする。
- 固定は `DialectDdlTableTests` の行 `ddl-table-without-columns-is-rejected`・`sync-add-table-without-columns-is-excluded-with-warning`（スクリプトの文の面）・`sync-add-table-without-columns-plan-is-rejected`（手で組んだ計画）・期待の行 `plan-add-table-without-columns-is-excluded-with-warning`（計画の面・中立と 5 方言）・実 DB の行 `realdb-add-table-without-columns-is-excluded-and-other-tables-are-created`（5 方言）・`SchemaSyncWarningTextTests`。mutation 実証済み（DDL の判定・計画の除外・スクリプトの入口・除外の位置）。

### 同じ名前の表と列

- 図の中で同じ名前（`SchemaNames` の規則）の表が 2 つ以上、または 1 つの表に同じ名前の列が 2 つ以上あると、DDL は同じ表・列を 2 回作って DB に途中で断られ、差分は名前で表を組めずに（`NameLookup` は片側の 2 件以上を見つからない扱いにする）同じ表の追加を 2 つ出す。
- 判定は Model の `DiagramNameDuplicates` の 1 か所、例外文は `SqlNameDuplicates.Validate`（英語・`TABLE 'x' and TABLE 'X'`・`COLUMN 'x'.'y' and COLUMN 'x'.'Y'`）で、次の 3 つの入口が通す（5 方言とも同じ扱い）。
  - DDL：基底（`DdlGeneratorBase.Build`）と SQLite（`SqliteDdlGenerator.Build`）の `SqlTableShape.Validate(diagram)` の直後。名指しして図全体を止める。
  - 差分：`SchemaDiffService.Compute` の冒頭（図の側だけを見る）。差分ごと止め、同期のダイアログはステータス行へ出す。計画と同期スクリプトの入口には置かない（差分を経ない計画へは図が届かない）。
  - DB の側の同じ名前（引用識別子で共存する `Dup` と `dup`）は断らず、`NameLookup` が綴りまで一致する 1 件を選ぶ。
- 図には手で編集したファイル（読込検査は名前の重複を断らない）と、同じ名前の表を警告つきで受け入れる C# リバースから入り得る。図の側の入口では知らせず、DDL と差分の入口で名指しする（列の無い表と同じ）。
- 固定は名前の総当たりの入口 `NameIdentityDdlFuzzTests`（`ddl:{方言}:table`・`ddl:{方言}:column`・`diff:{方言}:table`・`diff:{方言}:column`＝同じ名前の組を受け付けたら `duplicate-accepted`、違う名前の組を断ったら `distinct-rejected`）。mutation 実証済み（3 つの入口・列の判定・比べ方）。

### セクションの順序

- 文の種類の順（組み立ての `StepOrder`）は DropFK → DropUnique → DropTable → AddTable → AddColumn → PK:Drop → AlterColumn → PK:Add → DropColumn → AddUnique → AddFK → SetTableDescription → SetColumnDescription。セクションは、`StepSections.Of` がこの順の文をまとめたもの（「文を読む方言ビルダー」）。
- 表の削除を表の追加より前に置く。表の名前を変える同期で新しい表が古い表の一意制約の名前を引き継ぐと、制約名がスキーマの中で一意な方言では、追加が先だと同じ名前とぶつかって失敗する。古い表の削除を選ばなければ、表の追加は名前の重なりで外して名指しする（「選んでいない変更を前提にする項目」）。
  - 削除する表を参照する外部キーは、その前の FK 解除で外れている（`InjectVanishingForeignKeyDrops`）。
  - 暗黙コミットの方言では、削除の後の作成が失敗すると古い表は消えたまま残る（docs に明記）。
- SQLite の同期スクリプトは独自の順序のまま（一意制約の名前がスキーマの中の名前を取らない）。
- 固定は期待の行 `plan-sections-are-in-fixed-order` と実 DB の行 `realdb-rename-table-keeping-unique-name-drops-old-table-first`（5 方言）。mutation 実証済み。

### 主キーの列の NULL 許容

- DDL・同期スクリプト・差分・同期計画が列を NOT NULL とみなすかは、Model の `ColumnNullability.AllowsNull`（NULL 許容の印があり、主キーの列でない）だけで決める。方言の DDL・同期スクリプトに `IsPrimaryKey || !IsNullable` の写しを置かない。
  - 写しが割れると、DDL が NOT NULL で作った列を差分が NULL 許容とみなし、適用した直後に偽の ALTER COLUMN が出る。
- 規則の本体・例外の 3 か所（主キーでない NOT NULL の列の判定）と読みの限界は `src/QuickER.Model/CLAUDE.md`「列の NULL 許容（ColumnNullability）」。
- QuickER.Provider*（取込を除く）は、NULL 許容の印 `Column.IsNullable` を直接読まない（写しの式だけでなく、印 1 つの読みも禁じる）。
  - 差分の項目の列（`SchemaDiffItem.Column`）と、外部キーの項目の子の表（`ChildEntity`）は図の列で、主キーの列に印が立ちうる。印を読むと、主キーの列に印を持つ図で、型だけの変更が「主キーに残る列の NULL 許容化」として外され、SET NULL の子の列の NOT NULL 化が前寄せされず、同期後の NULL 許容・SET DEFAULT の警告・DDL の注記が図の印のままになる。
  - live の列は取込が主キーの列の印を false にそろえる（5 方言の SchemaImporter）ので、`AllowsNull` と印が同じ値になる。図の列と見分けずに「読まない」と書けるのはこのため。
  - 書き込み（複製の列へ値を写す）の右辺も `AllowsNull` で読む。SQLite の再構築で列の追加の列を主キーから外すときは、外す前に実効の NULL 許容を写す（印のまま外すと、図の主キーの列が再構築の表だけ NULL 許容になる。逐次 DDL の方言の列の追加は NOT NULL で作る）。
  - 例外は理由つきの既知の一覧の 3 か所（図の構造の指紋が印そのものを記録する `SchemaSignature`・主キーでない列の判定の 2 か所）。取込のファイルには読み取りが無い（印を書くだけ）ので除外していない。
- ガード：`SourceRuleTests` の規則 `column-nullability-copies`（写しの式）と `provider-column-nullability-raw-reads`（印の読み取り。範囲は `src` の `QuickER.Provider` で始まる 6 プロジェクト・自己検査の行 `provider-column-nullability-raw-reads-forms`）。振る舞いは能力の組の網の関係 N1（目標の図の主キーの列に印を立てても、差分・計画の文・警告・外した項目が変わらない。`tests/QuickER.Tests.Integration/Dialects/SyncPlanFuzz/CLAUDE.md`「能力の組の網」）と、網の入力が届かない読みの行（期待の行 `plan-set-null-pk-change-and-alter-making-child-not-null-moves-alter-first-key-flagged-nullable`・`plan-set-null-post-sync-nullability-alter-to-key-column-flagged-nullable`〔同期後の NULL 許容。網では Oracle の網の種 2269 だけが捕まえ、種を 500 に減らす変異テストの環境では捕まえない〕・`plan-rebuild-column-changes-of-key-columns-flagged-nullable-stay-not-null`〔SQLite の再構築の列の変更と列の追加の写し。列の追加は網の種 6 つ〔632〜4665〕だけが捕まえる〕・`plan-set-default-on-primary-key-child-flagged-nullable-warns-like-not-null`、`DialectDdlTableTests` の行 `ddl-fk-set-default-on-primary-key-child-flagged-nullable-note`・`ddl-fk-set-null-on-delete-primary-key-child-flagged-nullable-note`）・`DialectDdlTableTests`・同期計画の期待の行・`SchemaDiffServiceTests`。図の列の読みを 1 か所ずつ戻すと、N1 かこれらの行のどれかが赤になる（live の列の読みは値が変わらないので規則だけが捕まえる）。mutation 実証済み。

### 説明の空欄と表せない文字

- 説明（テーブル・列）の空欄の判定は `DescriptionText`（`DescriptionText.IsPresent`／`Normalize`＝null・空・空白だけは説明なし）だけが持つ。DDL・差分（新しい表・列と既存の比較）・同期スクリプトがこれを通す。
  - 判定を経路ごとに持つと、DDL が書かない空白だけの説明を差分が説明ありとして比べ、適用した直後に偽の差分が出る。
- 説明を書くのは説明の同期の項目だけ（その項目を選ばなければ説明は入らない）。MySQL の帰結は `src/QuickER.Provider.MySql/CLAUDE.md`「識別子と説明の文字」。
- 説明の BMP の外の文字は、識別子と同じ方言の宣言 `IdentifierLengthLimit.RepresentsSupplementaryCharacters` で扱い、方言名で分岐しない（保存する形の正本は `IdentifierLengthLimit.StoredText`）。
  - 差分は両側を `capabilities.IdentifierLengthLimit.StoredText` にしてから比べる（素の比較では同期しても差分が消えない）。
  - 同期の計画は選んだ説明の項目のうち `CanRepresent` が偽のものを `SyncPlanWarningKind.DescriptionCharactersReplaced` で名指しする。
  - DDL（`DdlGeneratorBase`）は `DescriptionCharacterNote` が生成器の方言の上限（`IdentifierLimit`）で判定して、表の文の前・列の定義行の直前に注記する。
  - 扱いの表でなくケーパビリティに置くのは、DB が保存できない文字の事実で、識別子の入口と同じ宣言を共有するため。
- 固定は `DialectDdlTableTests`（行 `ddl-description-blank-is-written-as-none`・`sync-other-description-*`・`sync-add-column-mysql-does-not-write-comment`・`ddl-description-supplementary-character-notes*`）・期待の行 `diff-blank-description-*`・`diff-description-stored-form-*`・`plan-description-characters-replaced-*`・`BlankDescriptionTests`（Provider 系に判定の写し〔説明への `string.IsNullOrEmpty`／`IsNullOrWhiteSpace` と `?? string.Empty`〕が無いことの走査）・`DescriptionStoredTextTests`（保存する形の規則）・実 DB の行（5 方言）`realdb-blank-description-ddl-round-trip-no-diff-blank-target`／`realdb-blank-description-ddl-round-trip-no-diff-empty-target`・`realdb-blank-description-sync-removes-descriptions`・`realdb-description-supplementary-ddl-round-trip-no-diff-emoji`／`realdb-description-supplementary-ddl-round-trip-no-diff-bmp`・`realdb-description-supplementary-sync-adding-warns-no-diff-emoji`／`realdb-description-supplementary-sync-adding-warns-no-diff-bmp`。mutation 実証済み。

### 参照アクションの畳み込み

- 方言が表せない参照アクションは、比べる前に方言の実効値へ畳む。DDL・同期は注意コメントを出して句を省くので、素で比べると取り込み直すたびに作り直しの差分が出て、既定の追加だけで同じ名前の衝突になる。
- 宣言は `SyncDialectCapabilities.SupportsOnUpdateAction`／`SupportsSetDefaultAction`（ON DELETE・ON UPDATE の両方に効く）。畳み込みは `EffectiveOnDelete`／`EffectiveOnUpdate` で、外部キーのシグネチャと AlterForeignKey の変更文言の両方が通る。
- SQL Server は SET DEFAULT を作るのに列の DEFAULT 制約が要るので、`SetDefaultRequiresColumnDefault` で NOT NULL の子の列への SET DEFAULT を `SyncPlanWarningKind.SetDefaultOnNotNullColumn` で名指しする（実行は止めない）。DDL は `ForeignKeyStatementNotes` が注記する。判定は差分の AlterForeignKey を展開した後の追加に掛ける。ガード：期待の行 `plan-set-default-on-not-null-child-warns-only-where-declared`・`plan-alter-foreign-key-to-set-default-on-not-null-child-warns-only-where-declared`・網羅の行（SQL Server の手で書いた行のうち接尾辞 `fk-actions-not-null-child-set-default` の 5 行。実 SQL Server で DB が断るので、警告を外すと表明 1 が赤）・生成器の行の水準 `set-default-not-null-child`（参照アクションを変える方言）。
  - 子の列の NULL 許容は同期後の値で読む（`PostSyncSchema.AllowsNull`＝選んだ主キーの変更で主キーに入る列は NOT NULL、選んだ列の変更があればその値、DB に在る列は DB の値、DB に無い列は図の値。SET NULL の側と同じ読み方）。図の値で読むと、DB では NOT NULL・図では NULL 許容の列へ、NULL 許容化を選ばずに外部キーだけを足す同期が警告なしに断られ、逆向き（DB では NULL 許容・図では NOT NULL）では当てはまらない警告が出る。DDL の注記（`ForeignKeyStatementNotes`）は図が目標なので、図の値のまま読む。ガード：期待の行 `plan-set-default-on-child-kept-not-null-by-unselected-alter-warns`・`plan-set-default-on-child-kept-nullable-by-unselected-alter-does-not-warn`（網と実 SQL Server）・`plan-set-default-on-primary-key-child-flagged-nullable-warns-like-not-null`（DB に無い表の、図の主キーの列の印）。
- 方言ごとの事実は MySQL・Oracle・SQL Server の CLAUDE.md。固定は期待の行 `diff-fk-unsupported-referential-action-is-folded-*`・`diff-fk-alter-describes-effective-actions-only`・`diff-fk-mysql-folds-only-set-default-*`・`DialectDdlTableTests`（行 `ddl-fk-set-default-*`）・期待の行 `plan-set-default-*`。mutation 実証済み。

### 計算列と主キーと同じ列集合の一意制約

- 計算列かは DB の列だけで決める（`SchemaDiffService` の `computed = lcol.IsComputed`）。DB の計算列の NULL 許容は差分に出さない（`compareNullability = !computed`）。
  - 実 DB の計算列の NULL 許容は式が決める。図で NULL 許容にする逃げ道を案内しているので、比べると取込元の DB へ意味のない `ALTER COLUMN` が出る。
  - 図でだけ計算列にした列（DB では普通の列）は、型も NULL 許容も普通の列として比べ、選べる AlterColumn にする。DDL は計算列を普通の列として作る（docs/code-generation.md）ので、図の印は DB の型・NULL 許容を変えない理由にならない。図の印でも計算列とみなすと、NULL 許容の違いが出ず、型の変更が計算列の型を変えられない方言で選べない案内になる。ガード：期待の行 `diff-diagram-only-computed-column-type-change-is-selectable`・`diff-diagram-only-computed-column-nullability-is-alter-column`・生成器の水準（必須の形 `diagram-only-computed`）。
- 計算列の型の差は `BuildComputedColumnTypeChange` が項目にし、項目の列は NULL 許容を DB の値に戻した複製にする（図の列そのものを載せると NOT NULL を黙って外す）。
- 式を保ったまま型を変えられる方言（`SyncDialectCapabilities.SupportsComputedColumnTypeChange`＝PostgreSQL・MySQL）でだけ選べる。ほかの方言では選択できない案内項目にする。
- 主キーと同じ列集合の一意制約を持てない方言（`SupportsUniqueConstraintOnPrimaryKey` が false）では、図の主キーと同じ列集合の一意制約を差分の比較から外す（主キーが一意性を保証する。図側の署名には残すので DB 側の同じ列集合の一意制約の削除も出さない）。
  - DDL と同期の `CREATE TABLE` はその一意制約を省いて注記する（`UniqueConstraintOnPrimaryKeyNote.Split`＝扱いの表のマスで判定。`TableConstraintLineBuilder`・SQLite の DDL・SQLite の同期の表の本体の 3 か所が通る）。注記の行は `TableConstraintLines.Notes` として制約行の直前に置き、末尾カンマの判定には数えない。
  - 選んでいない主キーの変更で live の主キーが残る形はプランナーが外して名指しする。
  - DB にある一意制約と同じ列へ主キーを付け替える同期は、その一意制約を暗黙に外して `SyncPlanWarningKind.UniqueConstraintReplacedByPrimaryKey` で名指しする（`SyncPlanner.InjectUniqueConstraintsReplacedByPrimaryKey`＝持てない逐次 DDL の方言だけ・付け直さない）。その一意制約を参照する外部キーは主キーの変更の外し直しが新しい主キーへ付け直す。型の食い違いで残す外部キーがその一意制約を参照するなら、一意制約の削除が必ず断られるので計画の先頭へ移す（「外部キーの暗黙の外し直し」）。
- 固定は期待の行 `diff-computed-column-*`・`diff-live-only-computed-column-*`・`diff-non-computed-column-nullability-is-alter-column`・`diff-uq-on-primary-key-*`・`plan-dependency-unique-on-staying-pk-*`・`DialectDdlTableTests`（行 `ddl-unique-on-primary-key-columns-omitted-where-unsupported`・`sync-add-table-unique-on-primary-key-columns-omitted-where-unsupported`・`sync-rebuild-unique-that-becomes-primary-key-omitted`）・`OracleDdlRoundTripIntegrationTests.UniqueConstraintOnPrimaryKey_IsOmittedAndApplies`・実 DB の行（PG・MySQL の計算列）・`SchemaSyncWarningTextTests`。mutation 実証済み。

### 列の変更が保つ属性

- 同期の列の変更（AlterColumn）は、図に無い列属性（照合順序・既定値など）を実行時のカタログから読み直して保つ。方言ごとの書き方は各方言の CLAUDE.md「同期スクリプト」。
- 既定値のある列の型の種類（方言の正規化を通した括弧より前の基本の名前）を変える列の変更は、`SyncDialectCapabilities.ChecksDefaultOnColumnTypeChange`（同期が DB の既定値を保つ方言）で `SyncPlanWarningKind.DefaultOnColumnTypeKindChange` として名指しする。
  - 扱いの表の DefaultValue × Sync とのパリティは `CapabilityTreatmentParityTests` の `SetRules`。
  - 既定値は取込の `SchemaImportResult.ColumnDefaults` を同期の画面が `SyncPlanContext.ColumnDefaults` へ渡す（画面と同じ文脈を同期計画の総当たりも使う）。
  - 値が NULL の既定値は告げず `ColumnDefaults` にも入れない（入れると警告が根拠なく出る）。NULL の判定は共有の `SchemaImportWarnings.IsNullDefault`。
- 固定は期待の行 `plan-default-type-kind-change-warns-only-where-declared`／`plan-default-no-warning-*`・`ImportSyncTests`（行 `sync-dialog-type-kind-change-with-default-warns-from-imported-defaults`）・各方言の `*SyncScriptBuilderTests`・方言ごとの実 DB の行（`realdb-column-type-kind-change-with-default-warns`〔SQL Server〕・`realdb-pg-column-type-kind-change-with-default-warns`・`realdb-oracle-column-type-kind-change-with-default-warns`・`realdb-mysql-column-type-kind-change-incompatible-default-warns-and-fails`）・同期計画の総当たりの汎用の表明 5c（コミットした同期の前後で、選んだ表・列の追加と削除の外の列の既定値の式が変わらない。生成器の水準 `column-default`・`column-default-type-change`＝`tests/QuickER.Tests.Integration/Dialects/SyncPlanFuzz/CLAUDE.md`「期待の行と実 DB の段」）。mutation 実証済み。

### テーブル再構築の合成

SQLite の同期は `SyncPlan.Rebuilds`／`TableRebuildPlan` で表を作り直す。スクリプトと実行器の事実は `src/QuickER.Provider.Sqlite/CLAUDE.md`「同期」。

置き場所は `src/QuickER.Provider/Sync/TableRebuilds/`（名前空間 `QuickER.Provider.Sync.TableRebuilds`・internal）。

- 再構築の方言かの判定は `TableRebuildDialects.Requires`（`SupportsAlterColumn` か `SupportsForeignKeyAlter` が偽）の 1 か所で、既存の表を作り直す理由になる差分の種類は `TableRebuildDialects.IsTriggerKind`。
- 合成の入口は `TableRebuildSynthesis.Synthesize(postSync, liveSchema, context, capabilities)` で、結果（`TableRebuildSynthesisResult`）は再構築の計画（`Rebuilds`＝表を作るだけの再構築 → 既存の表の再構築の順）と処理済みの項目（`Folded`＝再構築へ畳んだ項目と、削除を選んだ表の作り直しの項目。参照の同一性で比べる集合）。合成は差分の項目を作らない。
  - 合成を呼ぶのは再構築の方言の組み立て（`TableRebuildScheduler`）の 1 か所。組み立ては、選んだ項目の最終形から `Folded` を除いた並びを文にして種類の順に並べ、末尾へ再構築の文（`RebuildTableStep`・由来は畳んだ項目の並び）を合成の順に置く（写しが前の並びをセクションに、再構築の文を `TableRebuildPlan` にする）。
  - ファイルの受け持ち：`TableRebuildSynthesis`（入口・表を作るだけの再構築・既存の表の再構築・解決できるかの判定）・`TableRebuildDefinition`（live の表の写しへ項目を当てる 5 つの関数）・`TableRebuildForeignKeys`（作り直す表に張る外部キーと、外部キーの項目の解決）・`TableRebuildRowidAlias`（rowid の別名へ写す列の NULL の守り）・`TableRebuildDialects`（判定）・`TableRebuildScheduler`（再構築の方言の組み立て）。
  - 合成の守りは期待の行（`plan-rebuild-*`・`realdb-rebuild-*`ほか）と実 SQLite の総当たり。関係 N4 は、写した計画が製品の計画と同じこと（再構築の面が書かない成分の突き合わせを含む）を見るが、合成の中身の誤りは製品の計画と写しが同じに持つので赤にならない。
- 合成は「live＋選択した差分だけ」で行う。合成の正本は `LiveEntities`（`SyncPlanContext` の live のスキーマ）で、`TableCreateSql` を合成に使わない。
- 合成は live の列オブジェクトを保つ。AlterColumn は型・NULL 許容・計算列だけを写し、AddColumn の列は主キーにしない（主キーの膜と順序を変えるのは選んだ AlterPrimaryKey だけ）。
  - 図は live と別の Guid 空間なので、図側の列へ差し替えると live の `UniqueConstraints`／`PrimaryKeyColumnIds` の列 ID が宙に浮き、一意制約が黙って消え複合主キーの順序が変わる。
  - 目標の図を `Clone(preserveId: true)` で作るとこの種の不具合を検出できない。同期計画の期待の行の実 DB の段は目標の図を既定で別の Guid 空間で作る（`tests/QuickER.Tests.Integration/Dialects/SyncPlanFuzz/CLAUDE.md`「期待の行と実 DB の段」）。
- 再構築は意味モデルに無い属性（列の属性・制約の句・テーブルオプション）を落とす。再構築対象（`CreateOnly=false`）に限って `TableRebuildAttributeDetector` で検出し、`SyncPlanWarningKind.TableRebuildDropsColumnAttribute` を積む（組み立てた再構築の文を読む評価の規則 `WarnTableRebuildsDroppingColumnAttributes`。「計画の評価の規則」）。
  - 検出は文字列レベル（最外括弧の中と後ろを分ける・引用／リテラル／コメントを除去し跡に空白を置く〔隣の語がつながって `DEFAULT'a'COLLATE` を見落とさない〕・語境界・大文字小文字非依存）で、警告のため過剰検出側に倒す。
  - 既定と同じ即時検査（`NOT DEFERRABLE`・`DEFERRABLE INITIALLY IMMEDIATE`）と、再構築で保たれる `NOT DEFERRABLE INITIALLY DEFERRED`・外部キーの `SET DEFAULT` は数えない（数えると本当の喪失が埋もれる）。
- 削除を選んだ表は再構築しない。
- 合成後の主キーが単一列で、宣言 `SyncDialectCapabilities.SingleColumnPrimaryKeyReplacesNull`（入れた NULL を DB が採番の値へ置き換える型＝SQLite の rowid の別名）に当たり、live ではその列が同じ主キーでなかった写す列を `TableRebuildPlan.RowidAliasNullGuardColumn` に印す（`TableRebuildRowidAlias.NullGuardColumn`）。スクリプトが移送の前にその列の NULL を断る（`src/QuickER.Provider.Sqlite/CLAUDE.md`「rowid の別名へ写す列の NULL」）。宣言は既定値を持たず、SQLite 以外は常に偽。
- 取込が表の中の物を捨てた表（`SyncPlanContext.ImportWarnings` のうち `SchemaImportWarningKinds.RebuildLosesTableContent` が真の種別＝大文字小文字・前後の空白だけ違う列名の後着の列・範囲外や除外した表への外部キー・列を読めない表・名前を縮めた物・無効化した制約）は再構築しない（`SelectionRules.ExcludeRebuildsOfProtectedTables` に警告の種類 `TableRebuildLosesImportDiscardedContent` を渡す。再構築の方言だけ・除外の周の中で `ExcludeDropsLeavingNoColumns` の後・暗黙の削除の注入の前。「除外の周」）。
  - 合成の正本は live の図で、捨てた物は図に無いので、作り直した表からデータごと黙って消える。
  - 外すのは、その表を作り直す選んだ項目（列・主キー・外部キー・一意制約・列順の変更と作り直し）と、その表の外部キーの端（親の表・列）を消す表・列の削除。列の追加（作り直さずに足せる）とその表自身の削除は外さない。外した項目は `ExcludedItems` へ積み、`SyncPlanWarningKind.TableRebuildLosesImportDiscardedContent`（表ごと・Detail は取込警告が名指しした物）で名指しする。文言は DB 側で直してから取り込み直すことを案内する。
  - 宣言は既定の腕を持たない網羅の switch（種別を足したら必ず決める）。列の属性・テーブルオプションの喪失は `TableRebuildDropsColumnAttribute` が警告して進めるので偽、表の外の物（ビュー・除外した表そのもの）も偽。
  - ガード：`SchemaImportWarningKindMapTests.EveryWarningKind_DeclaresWhetherRebuildLosesTableContent`・SQLite の期待の行 `realdb-rebuild-skipped-when-import-discarded-*`（捨てた物 3 × 作り直す項目 3）・同期計画の総当たりの汎用の表明 5（消えた取込警告）。
- DB の外で登録した照合順序・関数を使う物（索引・関数の仮想の生成列）があって QuickER の接続では作り直せない表も、同じ関数で作り直す項目を外し、`SyncPlanWarningKind.TableRebuildBlockedByExternalDefinition`（表ごと・Detail は作り直しを止める物の名指し）で名指しする（取込が捨てた物の除外の直後）。
  - 入力は取込が持ち帰る `TableRebuildBlocker`（`SyncPlanContext.RebuildBlockers`＝`SchemaImportResult.RebuildBlockers`。今は SQLite だけが埋める）で、`SyncPlanner.CollectRebuildBlockedTables` が表ごとにまとめる。局所の物はその表だけを、大域の物（照合順序を使う索引＝RENAME がスキーマ全体を読み直すときに断られる）はその索引の表以外の live の表すべてを止める。
  - 外さないと、作り直しが索引を作れず・生成列を読めず、照合順序の索引が 1 つあればどの表の作り直しも、無関係な変更ごと同期全体が巻き戻る。
  - 方言の判定は `src/QuickER.Provider.Sqlite/CLAUDE.md`「DB の外の照合順序・関数」。ガード：SQLite の期待の行 `realdb-external-*`。
- DB のビュー（図に無い）を読めなくする変更と、DB のトリガー（図に無い）を発火できなくする変更は計画から外して名指しする（`SelectionRules.ExcludeChangesBreakingLiveDependents`・`SyncPlanWarningKind.TableChangeBreaksDependentView`／`TableChangeBreaksDependentTrigger`・表ごと・Detail は読めなくなるビュー／発火できなくなるトリガーの名前。原因の表がどちらも壊すなら警告は 2 件で、項目は 1 回だけ外す）。入口は取込が持ち帰る `ILiveDependentCheck`（`SyncPlanContext.LiveDependentCheck`。null なら何もしない＝今は SQLite だけが埋める）。
  - 外すのは、原因の表の列の名前の集合か表の有無を変える項目（列の削除・列の追加・表の削除）だけで、型などの変更は流す。同期後の列名は「live の列 − 選んだ列の削除 ＋ 選んだ列の追加」（列が残らない表は数えない＝`ExcludeDropsLeavingNoColumns` が外す）。
  - トリガーを壊すのもビューと同じ 3 種類の項目だけ（型の変更では壊れない）。作り直しはトリガーの本文を検証しないので、外さないと同期はコミットし、トリガーは発火したときに初めて断られる。
  - まず計画全体の同期後の列名で 1 回試し、壊れるビュー・トリガーが無ければ何も外さない（単独の変更の和で判定すると、列の数を保つ名前の変更に偽の警告を出す）。壊れるときだけ表ごとに試して原因の表を決める。
  - `ExcludeUntilStable` の周の中、名前の重なる表の追加の除外の後・依存の除外の前に置く（外した列の追加を前提にする項目を同じ周の依存の除外が外し、次の周でもう一度全体を試す）。
  - 作り直す表の索引（図に無い）が読む列の削除も外して名指しする（`TableChangeBreaksDependentIndex`・Detail は作り直せなくなる索引の名前）。索引を壊すのは列の削除だけなので、索引だけが原因の表は列の削除の全部（索引が読まない列の削除も含む＝表の単位）を外し、列の追加・型の変更・表の削除は流す（名前の変更は消す側だけが外れる）。外さないと、作り直しの途中で索引を作れずに同期全体が巻き戻る。
  - 方言の判定と守りの無い枝は `src/QuickER.Provider.Sqlite/CLAUDE.md`「計画の段のビュー・トリガー・索引の判定」。
- 取込の結果から計画の入力を作るのは `SyncPlanContext.FromImport` の 1 か所（同期の画面と同期計画の総当たりが通す）。項目を足したら、ここに足す（呼び出し側ごとに写すと、写し忘れた側だけが黙ってその入力の無い計画を作り、画面とテストで警告・除外が割れる）。ガード：期待の行 `realdb-view-*`・`realdb-trigger-*`・`realdb-index-*`（判定器を写さないと赤）・`realdb-external-*`（作り直しを止める物を写さないと赤）。
- 再構築の合成で解決できない項目（同じ計画で消える列の一意制約など）は処理済みにせずセクションへ残してスキップのコメントを出す（`TableRebuildSynthesis` の `IsResolvableInRebuild`＝処理済みにすると何も変えずに成功扱いになる）。作り直す理由になる項目（`TableRebuildDialects.IsTriggerKind` の種類）が 1 つも解決できない表は作り直さず、その表の項目は全部セクションに残る（作り直すと、何も変えない再構築が走る）。
- 固定は `TableRebuildAttributeDetectorTests`・期待の行 `plan-rebuild-*`（`plan-rebuild-alter-column-keeps-live-column-identity`・`plan-rebuild-column-changes-without-alter-pk-keep-live-pk`・`plan-rebuild-dropped-table-is-not-rebuilt`・`plan-rebuild-unresolvable-item-stays-in-section`・`plan-rebuild-no-resolvable-trigger-does-not-rebuild`）・実 SQLite の行 `realdb-rebuild-*`。mutation 実証済み。

### 同期の総当たりと期待の行

- 同期計画の単体テストと実 DB の同期の統合テストは、期待の行（`SyncPlanExpectationRows.*.cs`）と同期計画の総当たりへ寄せてある。行の書き方と照合の仕組みは `tests/QuickER.Tests.Integration/Dialects/SyncPlanFuzz/CLAUDE.md`「期待の行と実 DB の段」。
