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
- 同期側は型を書き出す項目（表の追加・列の追加と変更・再構築の合成後定義・列順の移動）だけを検証する。削除する表・列の live の型まで見ると、取込が持ち帰った安全でない型の表を同期で消せない。
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
  - 既定の対応では書けない列（PostgreSQL の `date`・`timestamp`・`timetz`・`json`・`jsonb`・`xml` とその配列）は、マッパーが EF Core に教える列の型（`CSharpTypeInfo.EfCoreColumnType`）を入れ、生成 DbContext がその列にだけ `HasColumnType` を出す（`src/QuickER.CodeGen.CSharp/CLAUDE.md`「EF Core に教える列の型」）。
- 確かめた EF Core のプロバイダは、Integration が持つ `UseOracle`（Oracle.EntityFrameworkCore）・`UseMySQL`（MySql.EntityFrameworkCore＝ドライバは MySql.Data）・`UseNpgsql`・`UseSqlServer`・`UseSqlite`。MySQL の Pomelo（ドライバは MySqlConnector）は確かめていない。
- MySQL の `timestamp` は `DateTime`、`char(36)` は `Guid` へ写す（両ドライバの型。`DateTimeOffset` で読むと MySql.Data が小数秒を落とし、`char(36)` を `string` で読むと UUID の値の行が読めない）。どちらも正規型（DateTimeOffset・fixedstring(36)）と違う C# 型なので、型の表の宣言のセルの例外として宣言する（`DialectTypeRows.MatrixShared.cs` の `MapTypeExceptions`。docs の csharp-types の表と同じ行）。
- 方言ごとの写し方は各方言の CLAUDE.md「型表記」。固定は `DialectTypeTableTests` のマッパーの行（`map-*`・宣言のセルの全表記を流す `matrix-map-*`＝5 方言のマッパーが同じ「種類 → C# の型」の表に従う）と、同じ行を実 DB で流す `*DialectTypeRealDbTests`（手順は `DialectTypeRealDbRows`。ドライバの手順＝`GetFieldType` とマッパーの一致、EF Core の手順＝生成 DbContext と同じ構成のモデルでマッパーの型として読み、書いて読み戻す。列の型を教える列は同じ値を `HasColumnType` で当てる。読み書きできない組を残すときは `GapReason` と今の症状を書き、症状が出なくなると赤になる。今は残っていない）。DB に触れない宣言の照合は `DialectTypeRealDbTableTests`。

## 差分と同期

### 2 段構造と方言の宣言

- スキーマ同期は「`SchemaDiffService.Compute`（差分検出・方言中立）→ `SyncPlanner.BuildPlan`（選択済み差分を固定順のセクションへ＝方言中立の `SyncPlan`）→ `ISyncScriptBuilder.Build`（方言別の SQL）」の 2 段。
- 方言差は `SyncDialectCapabilities` でプランナーと差分へ宣言する。全プロパティは `required` で既定値を持たない（足した能力を書き忘れた方言が旧来の値で黙って動かないため）。
  - 主なもの：`SupportsAlterColumn`・`SupportsForeignKeyAlter`・`SupportsDescriptions`・`PersistsForeignKeyConstraintNames`・`ColumnReorder`・`AlterColumnRequiresForeignKeyRebuild`・`AlterColumnRequiresKeyConstraintRebuild`・`ForeignKeyRequiresLeadingIndex`・`TransactionalDdl`・`ReferencedKeyRequiresSameColumnOrder`・`SetDefaultRequiresColumnDefault`・`SupportsComputedColumnTypeChange`・`SupportsUniqueConstraintOnPrimaryKey`・`SupportsOnUpdateAction`・`SupportsSetDefaultAction`・`ChecksDefaultOnColumnTypeChange`・`SupportsSchemaQualifiedTableNames`・`ForeignKeyAllowsStringLengthDifference`・`ForeignKeyAllowsIntegerWidthDifference`・`TypeKindChangeRequiresEmptyColumn`。
  - 能力の値と扱いの表のマスの一致は `CapabilityTreatmentParityTests` が照合する。方言ごとの値の一覧は `docs/database.md`（.ja）の方言の表。
- 逐次 DDL の方言（SQL Server・PostgreSQL・MySQL・Oracle）は差分をそのままセクション化する。SQLite はテーブル再構築方式（「テーブル再構築の合成」）。
- 列順の同期は SQLite（再構築へ畳む）と MySQL（`SyncPlan.Reorders`）だけ。選択可能な `ReorderColumns` 項目（既定未選択）として出し、ほかの 3 方言（`ColumnReorder=None`）は選択不可の案内項目だけ。
  - 検出は追加・削除を除いた共通列の相対順序の比較。真ん中への列追加だけでは差分にならず、列は末尾に付いて次の同期で並びの差分が出る。
- 実行失敗後は差分を自動再計算する（部分適用の後に古い差分を表示し続けない）。

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
- 表せない文字の正本も QuickER.Dialects の宣言（`IdentifierLengthLimit.RepresentsSupplementaryCharacters`）。MySQL の帰結は `src/QuickER.Provider.MySql/CLAUDE.md`「識別子と説明の文字」。
- 名前を拾う場所は `SqlNameText` の `DiagramNames`／`PlanNames` の 2 つだけ。1 つ拾い忘れると、その種類の名前だけが黙って DB に届く。
- ガードは 2 つ。
  - `OutputIdentifierLimitGuardTests`：上限ちょうどの名前と上限を超える既定名の図から DDL と同期を作り、方言の区切りで囲まれた識別子を全文（動的 SQL のリテラルの中も）から取り出して、すべて `Accepts` を通ることと種類ごとの期待する識別子が現れることを 5 方言で表明する。
  - `IdentifierLimitEntryTests`：名前の種類 × 5 方言の表で、DDL と同期の両方の入口が断る／受け付けるを書き下した期待値で表明する。
- 実 DB では `NameFuzzDdlRoundTripIntegrationTests.cs` の方言ごとの派生が、宣言が断る名前は入口で断られ（断られなければ `not-rejected`）、受け付ける名前は適用できて取り込み直すと差分が出ない（断られれば `ddl-rejected`）ことを 5 方言で流す。mutation 実証済み。

### 差分の名前の綴り

- 差分は、組になった live と大文字小文字だけ違う図の表・列を、live の綴りへ揃えた複製で組む。揃えないと、引用識別子が大文字小文字を区別する方言でスクリプトが組んだ相手を指せない（Oracle・PostgreSQL）。
- `SchemaDiffService.Compute` の入口の `MatchSchemas`（`NameMatching`）で組んだ表・列だけを揃える。表と列の組み合わせは 1 度だけ行い、差分・列順の案内・綴りの揃えが同じ組を使う（`SchemaDiffServiceMatchingCountTests`）。
- 複製は Id を保った `Entity.Clone(preserveId: true)` で作り、図そのものは書き換えない。揃える組が無い表は元のオブジェクトのまま返す（出力はバイト不変）。
- 図にしか無い表・列は図の綴りで作る。
- 固定は期待の行 `diff-case-only-matched-names-use-live-spelling`・`SchemaDiffServiceTests.Items_CarryDiagramObjects`・実 DB の行 `realdb-oracle-case-only-names-sync-to-uppercase-table`／`realdb-pg-case-only-names-sync-to-lowercase-table`。mutation 実証済み。

### 複合外部キーの列ペアの並び

- 差分は 5 方言とも複合外部キーの列ペアを並びを見ずに比べる（列ペアは対応の集合で並びは意味を持たない。Oracle のカタログは宣言の並びを残さない）。
- `ReferencedKeyRequiresSameColumnOrder` が true の方言（SQL Server・MySQL）の DDL は、列ペアを参照先のキー（主キーの実効順、合わなければ一意制約の宣言順）の並びへ並べ替えて出す（`ForeignKeyColumnOrder.ArrangeToReferencedKey`・扱いの表の DDL のマスが Reorder かで判定）。
  - DDL だけを並べ替えて差分が並びを比べると、取り込み直した live がキーの並びになり、図と永久に食い違う。
- 同期も同じ方言で、外部キーの追加（明示・作り直しの展開・暗黙の付け直し）を参照先の同期後のキー（主キーの変更を選んだ表は新しい主キー・追加を選んだ表は図の主キー・それ以外は live の主キー、続いて同期後の一意制約＝`BuildPostSyncUniqueKeys`）の並びへ並べ替える（`SyncPlanner.ArrangeForeignKeysToPostSyncKeys`・規則の本体は DDL と同じ `ForeignKeyColumnOrder.ArrangeToKey`）。
  - 図や live の並びのまま付け直すと、参照される複合主キーの並びだけの変更と、並びの違う外部キーの作り直しが必ず失敗する（暗黙コミットの方言では外部キーが消えたまま残る）。
  - 主キーと同じ列集合の一意制約があっても主キーを先に試す。どのキーとも列の集合が一致しない外部キーは並べ替えない。
- 並べ替えた項目は `SchemaDiffItem.WithForeignKeyColumnPairs`（メンバーごとの複製＋列ペアの差し替え・変更通知の購読は写さない）で作り、プロパティを手で写さない。`SchemaDiffItemTests` が反射で全プロパティを照合する。
- 複合 FK はフル対応で、FK の差分・暗黙の付け直し・候補キーの証明は列ペアのリスト（参照先の列は集合照合）で動く。
- 固定は期待の行 `diff-fk-composite-pair-order-is-not-a-difference`・`plan-arrange-*`・`plan-composite-fk-reordered-parent-pk-rebuilds-all-pairs`・`ForeignKeyColumnOrderTests`・`SyncPlannerTests.AddForeignKey_PrimaryKeyAndUniqueOnSameColumns_FollowsPrimaryKey`・実 DB の行（`realdb-fk-composite-other-order-ddl-applies-no-diff`／`realdb-mysql-fk-composite-other-order-ddl-applies-no-diff`・`realdb-fk-reorder-referenced-composite-pk-rebuilds-in-new-order`／`realdb-mysql-fk-reorder-referenced-composite-pk-rebuilds-in-new-order`・`realdb-fk-alter-in-other-order-rebuilt-in-key-order`／`realdb-mysql-fk-alter-in-other-order-rebuilt-in-key-order`・`realdb-oracle-fk-composite-declared-other-order-no-diff`）・同期計画の総当たり。mutation 実証済み。

### 外部キーの作り直しの畳み込み

- 参照アクション・列ペアの順序・参照先だけが変わった外部キーは `SchemaDiffKind.AlterForeignKey` 1 項目へ畳み、`SyncPlanner.BuildPlan` の入口（`ExpandAlters`）で DROP＋ADD へ展開する。
  - 2 項目のままだと、名前を保存する方言では既定どおり追加だけを選ぶと名前の重複で失敗し、SQLite では旧と新が同じ列へ 2 本並ぶ。
  - 対応づけの鍵は、名前を保存する方言が子表と実効名、SQLite が子表と子列の集合。
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
- `SyncPlanner.ExpandAlters` が `AlterForeignKey` と一緒に削除＋追加へ展開する。`SectionOrder` にも方言のレンダラーにも作り直しの種別は無いので、展開を外すと選んだ作り直しが黙って消える。依存の除外（`ExcludeItemsRequiringUnselected`）は展開の前。
- 合成名は、DDL と差分が 1 回の実行につき 1 回だけ `ConstraintNames.ResolveUniqueConstraintNames` で図全体から解決する（ぶつかる合成名に番号を足す規則は Model の担当）。差分は追加・作り直しの項目へ解決した名前（`SchemaDiffItem.UniqueConstraintNames`／`UniqueConstraintName`）を載せる。
- 固定は期待の行 `diff-uq-name-step-*`・`diff-uq-same-name-column-change-is-single-alter`・`diff-uq-synthesized-name-matching-live-is-alter`・`diff-uq-different-names-are-not-paired`・`diff-uq-unnamed-live-is-not-paired`・`plan-alter-expansion-*`・`plan-dependency-unique-alter-on-new-column-is-excluded-*`・実 DB の行 `realdb-uq-name-step-change-named-and-add-unnamed-on-old-columns`（5 方言）・`realdb-mysql-uq-columns-change-default-selection-changes-nothing`／`realdb-mysql-uq-columns-change-selected-rebuilds-same-name`・`realdb-oracle-uq-columns-change-default-selection-changes-nothing`／`realdb-oracle-uq-columns-change-selected-rebuilds-same-name`。mutation 実証済み。

### 一意制約の照合キーと付け直し

- DDL は 5 方言とも PK 制約行の直後に `CONSTRAINT … UNIQUE` 句を出す。CREATE TABLE の制約行（PK→UNIQUE）の組み立ては `TableConstraintLineBuilder` が単一正本で、DDL 生成と逐次 4 方言の同期の表の追加が共有する。どちらか一方に再インライン化すると「同期で作った表だけ UNIQUE が無い」乖離が静かに再発する。
- DB の一意制約の照合キーは `UniqueConstraintKey`（表＋DB の名前＋列集合＝外部キーの `ForeignKeyKey` と対称・名前の無いものどうしは列集合で組む）。
  - 照合するのは DB の一意制約とそれを外す項目（差分の削除・作り直しを展開した削除）だけで、図の追加はこのキーで照合しない。
  - 列の削除に伴う暗黙の削除・同期後のキーの集合（候補キーの証明）・SQLite の再構築の削除が同じキーを共有する。
  - 名前を外して表＋列集合だけにすると、同じ列集合の一意制約 2 つの片方を外したときに、もう片方まで「外した」ことになる。
- `AlterColumnRequiresKeyConstraintRebuild` の方言（SQL Server）は、列の変更が触る一意制約・主キーの構成列を暗黙に外して付け直す（`src/QuickER.Provider.SqlServer/CLAUDE.md`「同期スクリプト」）。明示の削除とは `UniqueConstraintKey` で重複を除く。
- 明示の一意制約の削除が被参照 FK を支えうれば `UniqueConstraintDropMayBreakForeignKey` で警告する（方言中立。支えの判定は「外部キーを支えうるキー」）。
- SQLite は再構築へ畳む。`SchemaAuxiliaryObject` に一意制約の種類は無く（インデックス・トリガーのみ）、再構築の CREATE TABLE は合成後定義から名前付き UNIQUE 句を出す。
- 固定は期待の行 `plan-uq-explicit-drop-is-neither-duplicated-nor-readded-*`・`plan-uq-rebuild-drops-only-same-named-twin`・`plan-candidate-key-add-fk-to-twin-unique-*`・同期計画の総当たり。mutation 実証済み。

### 主キーの同期

- 主キー同期（`AlterPrimaryKey`）は PK の列構成（順序付きの名前のリスト）の差を表ごとに 1 項目で出す（既定未選択・`IsDestructive`）。
- 旧 PK の制約名は取り込まず、方言のカタログを実行時に逆引きして DROP する（PK なしは no-op・解除だけなら ADD なし）。ADD は `PK_{テーブル名}` の固定命名。
- 逐次方言は 3 フェーズ（`SyncPlanSection.PrimaryKeyPhase`＝Drop → AlterColumn → Add）。旧 PK 列の NULL 許容化を含む変更も 1 回の同期で通る。PK 変更なしの計画は出力バイト不変。SQLite は再構築へ畳む。
  - MySQL で解除と付与を 1 つの ALTER TABLE にまとめる案は採らない（3 フェーズは列の変更に要り、1 文にできるのは列の変更を伴わない場合だけで規則が割れる）。
- 依存 FK の自動の外し直し：選んだ `AlterPrimaryKey` の旧主キーに支えられた被参照 FK（旧主キーと同じ列集合を参照するもの・`ForeignKeyRequiresLeadingIndex` の方言では旧主キーの先頭の列だけを参照するものも）と、FK に関わる列の AlterColumn（`AlterColumnRequiresForeignKeyRebuild`）を、プランナーが live のリレーションから合成項目として Drop／Add セクションへ注入する。
  - 一意制約を参照する FK は主キーの解除に巻き込まれないので外さない（`DependsOnOldPrimaryKey`）。
  - 明示の Drop とはシグネチャで重複を除き、明示の Drop をした FK は付け直さない。
- 主キーの変更で新しく主キーに加わる NULL 許容の列は、選んでいなくても live の型のまま NOT NULL にする（`InjectPrimaryKeyNotNullChanges`＝主キーの変更の意図に含まれる）。
- 固定は期待の行 `plan-implicit-fk-pk-change-skips-fk-on-unique-constraint`・`plan-implicit-fk-pk-change-rebuilds-fk-on-old-pk-prefix-for-mysql-like`・`plan-sections-are-in-fixed-order`・実 DB の行 `realdb-pk-change-only-makes-new-key-column-not-null`。

### 候補キーの証明

- 付け直す外部キーの参照先の列が、同期後の PK とも同期後に在る一意制約とも列の集合で完全一致しなければ、`SyncPlan.Warnings`（候補キー喪失）として実行確認に出す。
  - 外し直しの理由に依らず、暗黙に付け直すすべての外部キー（主キーの変更・列の変更・途中の状態の食い違い・索引の喪失）に掛ける。同期後の主キーは `PostSyncPrimaryKeyOf`（主キーの変更を選んだ表は新しい主キー・追加する表は図の主キー・ほかは live の主キー）で、外部キーの追加を並べる `ArrangeUnprovenForeignKeyAddsLast` と同じ材料を使う。
  - 主キーの変更で付け直す外部キーにだけ掛けていたため、参照先の唯一の一意制約を同じ計画で外しながら列の変更で外部キーを付け直す同期が、名指しされずに付け直しで断られ、暗黙コミットの方言（MySQL・Oracle）で外部キーが消えたまま残った（修正 AC2。Oracle は途中の状態の付け直しで初めてこの経路を通る）。ガード：形の行（`SyncPlan/ShapeRows` の `TypeChange`=BothEndsKindChange × `ReferencedKey`=Unique × 一意制約の削除。網の静的な規則の D9〔断られる文自身が、前の方で外した外部キーの付け直しである形〕と実 DB の表明 1b）。
  - 証明ベース：同期後の一意制約の集合は live − 選んだ削除 ＋ 選んだ追加から厳密に合成する（自然キーの UNIQUE＋代理 PK の定番構成では警告しない）。
  - ⊆ の判定にはしない（`(id)→(id,code)` の拡張で多くの方言が失敗する）。並びまで比べない（並びは付け直しで直すので、比べると通る付け直しを誤って警告する）＝`StaysCandidateKey`。
  - 取込の外の素の一意インデックスで実際には通る構成があるので、実行は止めない。
- 選んだ外部キーの追加・作り直しの参照先が同期後の候補キーでないときも `ForeignKeyNotOnCandidateKey` で警告する（`WarnAddedForeignKeysNotOnCandidateKey`。並べ替える先が無いので図の並びのまま出す）。
- 参照先が図の主キーとだけ一致し、同期後の主キー・一意制約と一致しない外部キーの追加・作り直しは、主キーの変更を前提にする項目として `ExcludeItemsRequiringUnselected` が外す。
- DDL が暗黙コミットされる方言では、証明できない追加を外部キーの追加の最後へ回す（`SyncPlanner.ArrangeUnprovenForeignKeyAddsLast`）。外部キーの追加は互いに依存せず後ろに制約の文が無いので、断られてもほかの付け直しは済んでいる。並べないと、警告していない外部キーまで巻き添えで消えたまま残る。
- 固定は期待の行 `plan-candidate-key-*`・`plan-arrange-*`・同期計画の総当たり。mutation 実証済み。

### 外部キーを支えうるキー

- 親のキー（主キー・一意制約）がその列を参照する外部キーを支えうるか（DB がそのキーの削除を外部キーで断るか）は `SyncPlanner.CanSupportForeignKey` の 1 か所で決める。
  - 参照する列と同じ列の集合を持つキーは、どの方言でも支える。
  - `ForeignKeyRequiresLeadingIndex` の方言（MySQL）では、参照する列を先頭に並べるもっと長いキーも支えうる（8.0 系や `restrict_fk_on_non_standard_key`=OFF で作れる非標準キーの外部キー。ほかに支える索引が無いとキーの削除が 1553 になる）。
- 通すのは 4 か所：前寄せ（`MoveCertainFailureFirst` の `BlockedKeyDrops`）・一意制約の削除の警告（`WarnUniqueConstraintDropsBreakingForeignKeys`）・主キーの変更の外し直しの 2 枝（旧主キーに支えられた外部キー＝`DependsOnOldPrimaryKey`、新しい主キーと同じ列を参照し、この計画で外す一意制約に支えられた外部キー）。
  - 警告は「新しい主キーが参照する列とちょうど同じ外部キー」を、主キーの変更の外し直しが付け直すとみなして飛ばす。だから外し直しの一意制約の枝と警告は同じ判定でなければならない。
  - 写しが割れると、MySQL で一意制約 UQ(a, b) の先頭の列 a だけを参照する外部キーがあるとき、その一意制約の削除は前寄せされるのに警告が出ずに 1553 で断られる。警告と外し直しの枝の片方だけが先頭の列を数えると、主キーを (a) へ移しながらその一意制約を外す同期が、警告も外し直しも無いまま断られる。
- 付け直せるか（候補キーの証明＝`StaysCandidateKey`）は別の判定で、列の集合の完全一致のまま（非標準キーへの付け直しは証明しない＝「候補キーの証明」）。
- MySQL 8.4 の既定（`restrict_fk_on_non_standard_key`=ON）では非標準キーの外部キーを作れないので、実 DB の段は持たない。網の静的な規則 R1（MySQL は先頭の列の索引も見る）はこの形の失敗を予言できるが、生成器・形の行は一意制約の全列を参照する外部キーしか作らないので、形は期待の行が受け持つ。
- ガード：期待の行 `plan-implicit-unique-drop-warns-for-fk-on-its-leading-columns-mysql-like`（警告だけを列の集合の完全一致に戻すと赤）・`plan-implicit-fk-pk-change-onto-referenced-prefix-of-dropped-unique-rebuilds-fk-mysql-like`（外し直しの一意制約の枝だけを戻すと赤）。どちらも SQL Server（先頭の列で支えない）と比べる。mutation 実証済み。

### 外部キーの暗黙の外し直し

`SyncPlanner.InjectImplicitForeignKeyRebuilds` は、付け直せない外部キーを外したまま残さない。

- 列定義の変更で同期後に両端の型が一致しなくなる外部キーは注入せず、`SyncPlanWarningKind.ForeignKeyColumnTypeMismatch` で名指しする（外さずに列の変更の時点で何も変えずに失敗させる）。
  - 名指しは `InjectImplicitForeignKeyRebuilds` の入口の 1 か所で、方言に依らない（明示に外さない live の外部キーを全部見る）。列の変更で外部キーを外し直さない方言（PostgreSQL・Oracle）は外部キーを張ったまま列を変えるので、DB が断れば列の変更が失敗する。名指しを外し直しの経路（`doomedColumns`）と主キーの変更の経路にだけ置いていたため、PostgreSQL・Oracle で残す外部キーの列を互換でない型へ変える同期が警告なしに失敗し（42804・ORA-02267）、Oracle では同じ同期で外した作り直しの外部キーが消えたまま残った（P5-2 の試運転の 2 回目の V4）。
  - 判定は組単位で、正本は `SyncPlanner.IsMismatchedUnacceptedPair`（組が「DB が受け入れた組」でなく、同期後に両端が並べられない）。`HasPostSyncTypeMismatch`（live の外部キー）と `ExcludeForeignKeysWithPostSyncTypeMismatch`（選んだ追加・作り直し）が通す。「DB が受け入れた組」は、どちらの端の型もこの同期で決まらず、live の外部キーが同じ組（子の表と列・親の表と列＝`ForeignKeyPairKey`）を持つ組で、数えない。外部キー単位で数えると、複合外部キーのほかの組の型を変えたとき、DB が受け入れている型の違う組まで食い違いに数え、外部キーを外し直さずに主キーの解除を必ず失敗させた（2BP01・ORA-02273。V1）。
  - DB にまだ無い組（選んだ外部キーの追加・列ペアを変える作り直しの新しい組）は、両端とも型の決まらない live の列でも数える。「型が決まらないから受け入れている」とみなしていたため、live の列どうしで型の違う外部キーの追加（親 int・子 bigint など）が名指しされずに MySQL（3780）・SQL Server（1778）で断られた（修正 AC2）。ガード：形の行（`SyncPlan/ShapeRows` の因子 `AddedForeignKey` と必ず含む行 2 つ）・静的な規則 R8 と DB に無い外部キーへ広げた W1（`tests/QuickER.Tests.Integration/Dialects/SyncPlanFuzz/CLAUDE.md`「静的な規則」）・生成器の水準（型の違う live の列への外部キーの追加）。
  - 列の変更で外部キーを外し直さない方言でも、組の両端の型がこの同期で決まり、片側だけ変えた途中の状態（新しい子 × 古い親、古い子 × 新しい親）のどちらかが並べられない外部キーは、外して付け直す（`SyncPlanner.CollectForeignKeysBlockedInIntermediateState`・外部キー単位。同期後に食い違う外部キーと明示に外す外部キーは数えない）。外部キーを張ったまま両端を 1 列ずつ変えるので、両端を同じ別の型（int → varchar(10)）へ変える同期が、名指しも外し直しもされずに最初の列の変更で断られた（PostgreSQL 42804・Oracle ORA-02267。修正 AC2 の V8）。宣言は保守側なので、DB が受け入れる途中の状態（PostgreSQL の integer と numeric など）でも付け直す。ガード：形の行（`SyncPlan/ShapeRows` の `TypeChange`=BothEndsKindChange と必ず含む行 V8）・期待の行（`plan-type-mismatch-both-sides-kind-change-rebuilds-and-warns-where-declared`・`plan-type-mismatch-selected-fk-alter-is-excluded-as-a-whole`）・静的な規則 R7（途中の状態に掛かる）・生成器の水準（型の種類を変える）。
  - 列が空でないと型の種類を変えられない方言（`TypeKindChangeRequiresEmptyColumn`＝Oracle の ORA-01439）では、暗黙に付け直す外部キーの構成列の型の種類（方言の正規化を通した括弧より前の基本の名前＝`ChangesTypeKind`。既定値のある列の型の種類の変更の警告と共有）を変える同期を `SyncPlanWarningKind.ForeignKeyRebuildAroundTypeKindChange` で名指しする（止めない。列に値があると列の変更が断られ、暗黙コミットで外した外部キーが付け直されずに残る）。利用者が選んだ作り直し（`AlterForeignKey` の展開）は対象外。ガード：期待の行 `plan-type-mismatch-both-sides-kind-change-rebuilds-and-warns-where-declared`・`SchemaSyncWarningTextTests`・`DialectDocsTableTests.ForeignKeyRebuildTable_MatchesDeclarations`。
  - 型の比較は `SyncDialectCapabilities.AreForeignKeyCompatibleTypes`（`AreSameType` に加え、`ForeignKeyAllowsStringLengthDifference` の方言では同じ文字列型の長さだけの違いを許す＝`SyncDialectCapabilities.DiffersOnlyInStringLength`。Oracle の単位語 CHAR／BYTE は両端で同じこと。`ForeignKeyAllowsIntegerWidthDifference` の方言＝PostgreSQL・Oracle・SQLite では幅だけ違う整数型を許す＝`SyncDialectCapabilities.DiffersOnlyInIntegerWidth`）。
  - 宣言は「真なら DB が必ず受け入れる」保守側に置く。DB が受け入れるのに宣言が許さない組（PostgreSQL・MySQL・Oracle の小数の精度の違い・PostgreSQL の整数から小数への参照・浮動小数点の幅）は、既知の割り切りとして食い違いとみなす（名指しはするが、DB が受け入れれば同期は通る）。
  - ガード：形の行（`SyncPlan/ShapeRows` の `ForeignKeyTypes`=DecimalOtherKeyColumn と必ず含む行 V1・V4）・期待の行（`plan-type-mismatch-composite-undecided-pair-is-not-counted-alter-column`・`plan-type-mismatch-composite-undecided-pair-is-not-counted-pk-change`・`realdb-fk-integer-width-difference-is-accepted-without-warning`）・静的な規則 W1・R7（`tests/QuickER.Tests.Integration/Dialects/SyncPlanFuzz/CLAUDE.md`「静的な規則」）・`CapabilityTreatmentParityTests`・`DialectDocsTableTests.ForeignKeyRebuildTable_MatchesDeclarations`・`SyncPlannerTests`（幅の違いの表）。
  - 主キーの変更で付け直す外部キーも、構成列の型を変えて両端が食い違うなら外さずに名指しする。
  - SQL Server（長さも一致が要る）の DDL は長さだけ違う外部キーの前に注記を出す（`ForeignKeyStatementNotes`）。
  - 「この同期で型が決まる列」は `SyncPlanner.CollectTypeDecidingColumns` の 1 か所が決める（live に無い列か、live の型と `AreSameType` で同じでない型へ変える列の変更）。NULL 許容だけの列の変更は型を決めない。外部キーの型の照合（`HasPostSyncTypeMismatch`・`ExcludeForeignKeysWithPostSyncTypeMismatch`・前寄せの型の判定）はどれもこの関数を通す。
    - 数えると、DB が受け入れている型の違う外部キー（PostgreSQL の integer → bigint など）を NULL 許容の変更だけで「付け直せない」と名指しして外し直さず、その外部キーに阻まれる主キーの解除が必ず失敗した（PostgreSQL 2BP01・Oracle ORA-02273）。3 か所がそれぞれ列の変更の新しい型を辞書へ写していたので、誤りも 3 か所に写っていた。
    - ガード：形の行（`SyncPlanShapeRows` の因子 `ForeignKeyTypes`=Wider × `TypeChange`=ChildNullability と必ず含む行 L4）・静的な規則 W1（`tests/QuickER.Tests.Integration/Dialects/SyncPlanFuzz/CLAUDE.md`「静的な規則」）・`SourceRuleTests`（規則 `sync-type-deciding-column-copies`＝差分の項目の列の新しい型を直接読む箇所をメソッドで数え、理由つきの一覧と照合する）。
- 選んだ外部キーの追加・作り直しを警告で名指しする名前は、スクリプトが付ける名前（`SyncPlanner.SelectedForeignKeyName`＝`SyncScriptBuilderHelper.ForeignKeyNameToAdd`）の 1 か所で作る（型の食い違いの除外・`ForeignKeyNotOnCandidateKey`・SET NULL の除外・SET DEFAULT の 4 つの警告と、選んでいない変更を前提にする項目の除外の名指し）。
  - リレーションの名前から規約名を作り直すと、名前の無い同じ組の 2 本目が 1 本目と同じ `FK_{子}_{親}` になり、警告が DB にある無関係な外部キーを名指しした。
  - ガード：期待の行 `plan-warning-names-added-fk-type-mismatch`・`plan-warning-names-added-fk-not-on-candidate-key`・`plan-warning-names-added-fk-set-null`・`plan-warning-names-added-fk-set-default`・`SourceRuleTests`（規則 `sync-selected-foreign-key-names`＝差分の項目のリレーションから `ResolveForeignKeyName` で名前を作り直す呼び出しと、差分の項目の名前から `ConstraintNames.EffectiveForeignKey` で組む呼び出しを、理由つきの一覧と照合する）。
- 食い違う外部キーが 1 本でもあれば、ほかの外部キーも 1 本も外し直さない（`doomedColumns`）。列の変更の段で必ず失敗し、付け直しはその後にしか来ないので、外すと暗黙コミットの方言で消えたまま残る。同じ列の外部キーだけを残すのでは足りない。
- DDL が暗黙コミットされる方言（`TransactionalDdl` が偽）では、計画が残す外部キーに阻まれて必ず断られる文のうち計画の順で最初のもの（キーの削除は組）を、計画の先頭の独立したセクションへ移す（`SyncPlanner.MoveCertainFailureFirst`＝何も変えずに失敗させる。主キーの変更は Drop の段だけを移す）。トランザクションの方言は並びを変えない。
  - 対象は、キーの削除（主キーの解除・一意制約の削除。主キーが置き換える一意制約の暗黙の削除を含む）で残す外部キーを支え得る親のキー（参照する列と同じ列の集合を持つ live の主キー・一意制約。`ForeignKeyRequiresLeadingIndex` の方言ではその列を先頭に持つキーも）がこの計画ですべて外れるとき・残す外部キーの両端の型を食い違わせる列の型の変更（`AlterColumnRequiresForeignKeyRebuild` の方言は外部キー単位、ほかの方言はその列を含む組が食い違うとき）・`SetNullRequiresNullableChildColumn` の方言で残す SET NULL の外部キーの子の列の NOT NULL 化。
  - 支え得るキーを全部外すときは、最後の削除だけが断られ前の削除は通るので、外す文の組を計画の順のまま先頭へ移す（組の前の方は利用者が選んだ一意制約の削除。主キーの解除は固定順で組の最後になる）。ほかのキーを live だけで数えていたため、MySQL で一意制約の双子を両方外す同期が前寄せされず 2 つ目の削除が 1553 で断られて作り直しの外部キーが消えたまま残り（V3）、主キーと同じ列の一意制約が外部キーを支えるのに主キーの解除を必ず断られると先頭へ移して、通った解除の後の列の変更の失敗で主キーが消えたまま残った（3780。V6）。ガード：形の行（`SyncPlan/ShapeRows` の `ExplicitOp`=DropAllUniques・因子 `CollateralFk`・因子 `PrimaryKeyTwin` と必ず含む行 V3・V6。網の静的な規則の D9 と実 DB の表明 1b）。
  - 一意制約の削除を対象に入れていなかったため、Oracle で外部キー 2 本が参照する一意制約の列へ主キーを付け替え、片方の子の列の型を変える同期が、兄弟の外部キーを外した後に ORA-02273 で断られ、兄弟の外部キーが消えたまま残った。ガード：形の行の必ず含む行 L1（`SyncPlanShapeRows` の `PkChange`=MoveToUniqueColumn × `ReferencedKey`=Unique × 参照 2 本 × 子だけの型の変更。網の静的な規則の D9 と実 DB の表明 1b）。
- 子表・親表・構成列が同じ計画で削除される外部キーは、外すだけで付け直さない。この「外すだけ」は端が消える live の外部キーすべてに掛ける（`SyncPlanner.InjectVanishingForeignKeyDrops`＝計画の入口で暗黙の DropForeignKey を足す）。
  - 外部キーの削除は既定で未選択なので、足さないと逐次 DDL では参照されている表・列の削除が拒否され、SQLite では存在しない表・列を指す外部キーが残る。
- `ForeignKeyRequiresLeadingIndex` の方言では、削除する一意制約の索引の先頭に子列が乗る外部キーも外して付け直す（`CollectLostIndexes`）。
- 外部キーの照合キー（`ForeignKeyKey`）は列ペアに加えて実効名と参照アクションを含める（同じ列ペアの 2 本の片方を外したとき、もう 1 本まで「外した」扱いにしない）。再構築の合成（`SynthesizeForeignKeys`）も削除 1 件につき 1 本だけ外す。
- 構成列・表が同じ計画で消える一意制約の暗黙の付け直しも、外すだけで付け直さない。
- 警告や付け直しに出す外部キーの名前は `SyncPlanContext.LiveForeignKeyNames(limit)`（図全体から求めた実効名）を通す。
- 固定は期待の行 `plan-type-mismatch-*`・`plan-implicit-fk-*`・`plan-implicit-unique-rebuild-with-dropped-column-drops-only`・`plan-vanishing-endpoint-*`・`plan-fk-alter-one-of-duplicated-rebuild-dialect-keeps-the-other`・`SyncPlannerTests`「外部キーの両端の型: 長さの違いを許すのは同じ文字列型どうしだけ」・`DialectDdlTableTests`（行 `ddl-fk-string-length-mismatch-note`・`ddl-fk-type-mismatch-*`）・実 DB の行（`realdb-fk-type-mismatch-one-end-warns-and-fails`・`realdb-fk-drop-one-of-same-pair-keeps-the-other`・`realdb-drop-parent-and-child-tables-without-fk-drop`・`realdb-pk-change-with-child-drop-table`・`realdb-rebuild-alter-one-of-duplicated-fks-keeps-other`・`realdb-drop-referenced-end-without-fk-drop-table`／`realdb-drop-referenced-end-without-fk-drop-column`・`realdb-drop-parent-only-rebuilds-child-without-fk-with-rows`／`realdb-drop-parent-only-rebuilds-child-without-fk-without-rows`・MySQL の `realdb-mysql-fk-type-mismatch-one-end-warns-and-keeps-fk`・`realdb-mysql-fk-alter-to-other-column`・`realdb-mysql-fk-same-column-one-mismatched-changes-nothing`・`realdb-mysql-drop-referenced-end-without-fk-drop-table`／`realdb-mysql-drop-referenced-end-without-fk-drop-column`）・同期計画の総当たり。mutation 実証済み。

### 選んでいない変更を前提にする項目

- 外部キー・一意制約・主キー・説明の追加が選んでいない表・列の追加を、列の NULL 許容化が選んでいない主キーの変更を前提にするとき、`SyncPlanner.ExcludeItemsRequiringUnselected` がその項目を外し、`SyncPlanWarningKind.SelectedItemRequiresUnselectedItem`（Detail＝「対象 (足りないもの)」）で実行確認に出す。
  - そのまま実行すると、逐次 DDL は存在しない表・列を参照して失敗し、SQLite の再構築は部分的な主キー・存在しない表を指す外部キーのまま成功する。
- 依存先を自動で選ばない（利用者が選ばなかった変更、特に破壊的な変更を入れることになる）。
- 外部キーの作り直しは展開する前に判定する（削除と追加をまとめて外す＝削除だけが走らない）。
- 型の変更も含む列の NULL 許容化は、NULL 許容化だけを外して型の変更は残す。
- 外した選んだ項目は警告と一緒に `SyncPlan.ExcludedItems` へ積む（前提の欠け・型の食い違い・作れない方言の SET NULL が共有の `ExcludeSelected` を通る）。同期計画の総当たりは外した項目をこの一覧で引く。
- 固定は期待の行 `plan-dependency-*`・`plan-rebuild-fk-to-unselected-new-table-is-excluded-with-warning`・実 DB の行（`realdb-fk-add-on-unselected-new-column-skipped-with-warning`・SQLite の `realdb-pk-on-new-column-without-add-column-is-excluded`／`realdb-pk-on-new-column-with-add-column-applies`・`realdb-fk-to-unselected-new-table-skipped-with-warning`）・`SchemaSyncWarningTextTests`（警告の全種別に実行確認の文言がある）。mutation 実証済み。

### 構成列の削除と列が残らない表

- 主キー・一意制約の構成列の削除は、それを前提にする変更を方言に依らず同じ形で扱う。
- 同期後も主キーに入っている列の削除（`DropColumn`）は、その列を外す主キーの変更を選んだときだけ残し、選ばなければ `ExcludeItemsRequiringUnselected` が外して `SelectedItemRequiresUnselectedItem`（Detail＝「列 (PRIMARY KEY)」）で名指しする。
  - 同期後の主キーに数える主キーの変更は、この段で外れないものだけ。
- 選んだ列の削除で構成列が消える live の一意制約は、`SyncPlanner.InjectVanishingUniqueConstraintDrops` が暗黙の `DropUniqueConstraint` として足し、`SyncPlanWarningKind.UniqueConstraintDroppedWithColumn` で名指しする（付け直さない・同じ削除を選んでいれば足さない・消す表は対象外）。
  - 外部キーの消える端は名指ししないのに一意制約は名指しする非対称は意図したもの（一意制約は残りの列で作り直せるので判断を利用者に委ねる）。
  - 外さないと、方言ごとに列の削除が断られる・制約が黙って消える・残りの列へ縮むと結果が割れる。
- SQLite の再構築の合成は、一意制約の削除を列の変更より前に当てる（`ApplySelectedUniqueConstraintDrops`）。
- 選んだ列の削除で同期後に表の列が 1 本も残らないときは、その表の列の削除をすべて外す（`SyncPlanner.ExcludeDropsLeavingNoColumns`・`SyncPlanWarningKind.TableLeftWithoutColumns`・方言に依らない）。
  - 同期後の列は「DB の列 − 選んだ列の削除 ＋ 選んだ列の追加」で数える（表の削除を選んだ表は数えない）。
  - 依存の除外の直後・`InjectVanishing*` より前に置く（外した列を前提にした暗黙の削除を足さない）。主キーの解除・一意制約の削除は残す。
  - 外さないと、SQL Server は 4923 で断り、MySQL・Oracle は前の制約と列の削除が暗黙コミットで残り、PostgreSQL は列の無い表を作って取り込み直すと `TableColumnsUnavailable` になり、SQLite は列の無い再構築の表が構文エラーになる。
  - 図で列の無い表（DB に無い表）の追加は、次の節「列の無い表」が扱う。
- 固定は期待の行 `plan-drop-member-column-*`・`SyncPlannerDropMemberColumnTests.DropColumnsOnly_DropsUniqueConstraintBeforeColumnInScript`・実 DB の行 `realdb-drop-member-column-*`（5 方言）・同期計画の総当たり（列が残らない表の行 `sqlite-empty-rebuild`・`{sqlserver,mysql,oracle,postgresql}-drop-all-columns`・既知の失敗は `SyncPlanFuzzKnownFailures`）・`SchemaSyncWarningTextTests`。mutation 実証済み。

### 列の無い表

- 図で列を 1 本も持たない表は `CREATE TABLE t ( );` になり、SQL Server・MySQL・Oracle・SQLite は構文エラーで断り、PostgreSQL は取り込み直すと `TableColumnsUnavailable` になる列の無い表を作る。5 方言とも同じ扱いにする（方言の宣言に差を持たせない＝PostgreSQL も断る）。
- 判定は `SqlTableShape`（`HasNoColumns`・差分の項目は `IsAddTableWithoutColumns`）の 1 か所だけで、次の 3 つの経路が共有する。
  - DDL：5 つの入口が `SqlNameText.Validate` の直後に `SqlTableShape.Validate(diagram)` を通し、表を名指しして図全体を止める（`InvalidOperationException`・英語・`TABLE 'x'`）。
  - 同期の計画：`SyncPlanner.ExcludeTablesWithoutColumns` が列の無い表の追加を外し、`SyncPlanWarningKind.TableWithoutColumns`（Detail は空）で名指しする（`ExcludedItems` へ積む・方言に依らない）。ほかの変更は実行できるので、隣の「列が残らない表」と同じく外して名指しする。
    - 依存の除外（`ExcludeItemsRequiringUnselected`）の入力の段に置く。外した表の追加を前提にする項目（その表の説明）は、依存の除外が `SelectedItemRequiresUnselectedItem` で外す。後ろへ置くと、存在しない表への説明の文が残る。
  - 同期スクリプト：5 つの入口が `SqlTableShape.Validate(plan)` で、表の追加の項目と再構築の合成後の定義（SQLite の作るだけの再構築）を見て断る（計画を経ずに組んだ計画への二重の守り）。
- 図には列の無い表が入り得る（MCP の `add_entity` の直後・GUI で全列を消した表・列を復元できなかった C# リバース・列の行の無い Excel 定義書のシート）。図の側の入口では知らせず、DDL と同期の入口で名指しする。
- 固定は `DialectDdlTableTests` の行 `ddl-table-without-columns-is-rejected`・`sync-add-table-without-columns-is-excluded-with-warning`（スクリプトの文の面）・`sync-add-table-without-columns-plan-is-rejected`（手で組んだ計画）・期待の行 `plan-add-table-without-columns-is-excluded-with-warning`（計画の面・中立と 5 方言）・実 DB の行 `realdb-add-table-without-columns-is-excluded-and-other-tables-are-created`（5 方言）・`SchemaSyncWarningTextTests`。mutation 実証済み（DDL の判定・計画の除外・スクリプトの入口・除外の位置）。

### セクションの順序

- `SyncPlanner.SectionOrder` は DropFK → DropUnique → DropTable → AddTable → AddColumn → PK:Drop → AlterColumn → PK:Add → DropColumn → AddUnique → AddFK → SetTableDescription → SetColumnDescription。
- 表の削除を表の追加より前に置く。表の名前を変える同期で新しい表が古い表の一意制約の名前を引き継ぐと、制約名がスキーマの中で一意な方言では、追加が先だと同じ名前とぶつかって失敗する。
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
- SQL Server は SET DEFAULT を作るのに列の DEFAULT 制約が要るので、`SetDefaultRequiresColumnDefault` で NOT NULL の子の列への SET DEFAULT を `SyncPlanWarningKind.SetDefaultOnNotNullColumn` で名指しする（実行は止めない）。DDL は `ForeignKeyStatementNotes` が注記する。判定は差分の AlterForeignKey を展開した後の追加に掛ける。
- 方言ごとの事実は MySQL・Oracle・SQL Server の CLAUDE.md。固定は期待の行 `diff-fk-unsupported-referential-action-is-folded-*`・`diff-fk-alter-describes-effective-actions-only`・`diff-fk-mysql-folds-only-set-default-*`・`DialectDdlTableTests`（行 `ddl-fk-set-default-*`）・期待の行 `plan-set-default-*`。mutation 実証済み。

### 計算列と主キーと同じ列集合の一意制約

- 計算列の NULL 許容は差分に出さない（`SchemaDiffService` は `computed = tcol.IsComputed || lcol.IsComputed` から `compareNullability = !computed` を求める＝図・DB のどちらかが計算列なら比べない）。
  - 実 DB の計算列の NULL 許容は式が決める。図で NULL 許容にする逃げ道を案内しているので、比べると取込元の DB へ意味のない `ALTER COLUMN` が出る。
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
- 固定は期待の行 `plan-default-type-kind-change-warns-only-where-declared`／`plan-default-no-warning-*`・`ImportSyncTests`（行 `sync-dialog-type-kind-change-with-default-warns-from-imported-defaults`）・各方言の `*SyncScriptBuilderTests`・方言ごとの実 DB の行（`realdb-column-type-kind-change-with-default-warns`〔SQL Server〕・`realdb-pg-column-type-kind-change-with-default-warns`・`realdb-oracle-column-type-kind-change-with-default-warns`・`realdb-mysql-column-type-kind-change-incompatible-default-warns-and-fails`）。mutation 実証済み。

### テーブル再構築の合成

SQLite の同期は `SyncPlan.Rebuilds`／`TableRebuildPlan` で表を作り直す。合成は `SyncPlanner` が行い、スクリプトと実行器の事実は `src/QuickER.Provider.Sqlite/CLAUDE.md`「同期」。

- 合成は「live＋選択した差分だけ」で行う。合成の正本は `LiveEntities`（`SyncPlanContext` の live のスキーマ）で、`TableCreateSql` を合成に使わない。
- 合成は live の列オブジェクトを保つ。AlterColumn は型・NULL 許容・計算列だけを写し、AddColumn の列は主キーにしない（主キーの膜と順序を変えるのは選んだ AlterPrimaryKey だけ）。
  - 図は live と別の Guid 空間なので、図側の列へ差し替えると live の `UniqueConstraints`／`PrimaryKeyColumnIds` の列 ID が宙に浮き、一意制約が黙って消え複合主キーの順序が変わる。
  - 目標の図を `Clone(preserveId: true)` で作るとこの種の不具合を検出できない。同期計画の期待の行の実 DB の段は目標の図を既定で別の Guid 空間で作る（`tests/QuickER.Tests.Integration/Dialects/SyncPlanFuzz/CLAUDE.md`「期待の行と実 DB の段」）。
- 再構築は意味モデルに無い属性（列の属性・制約の句・テーブルオプション）を落とす。`SyncPlanner` が再構築対象（`CreateOnly=false`）に限って `TableRebuildAttributeDetector` で検出し、`SyncPlanWarningKind.TableRebuildDropsColumnAttribute` を積む。
  - 検出は文字列レベル（最外括弧の中と後ろを分ける・引用／リテラル／コメントを除去し跡に空白を置く〔隣の語がつながって `DEFAULT'a'COLLATE` を見落とさない〕・語境界・大文字小文字非依存）で、警告のため過剰検出側に倒す。
  - 既定と同じ即時検査（`NOT DEFERRABLE`・`DEFERRABLE INITIALLY IMMEDIATE`）と、再構築で保たれる `NOT DEFERRABLE INITIALLY DEFERRED`・外部キーの `SET DEFAULT` は数えない（数えると本当の喪失が埋もれる）。
- 削除を選んだ表は再構築しない。
- 再構築の合成で解決できない項目（同じ計画で消える列の一意制約など）は処理済みにせずセクションへ残してスキップのコメントを出す（`IsResolvableInRebuild`＝処理済みにすると何も変えずに成功扱いになる）。
- 固定は `TableRebuildAttributeDetectorTests`・期待の行 `plan-rebuild-*`（`plan-rebuild-alter-column-keeps-live-column-identity`・`plan-rebuild-column-changes-without-alter-pk-keep-live-pk`・`plan-rebuild-dropped-table-is-not-rebuilt`・`plan-rebuild-unresolvable-item-stays-in-section`）・実 SQLite の行 `realdb-rebuild-*`。mutation 実証済み。

### 同期の総当たりと期待の行

- 同期計画の単体テストと実 DB の同期の統合テストは、期待の行（`SyncPlanExpectationRows.*.cs`）と同期計画の総当たりへ寄せてある。行の書き方と照合の仕組みは `tests/QuickER.Tests.Integration/Dialects/SyncPlanFuzz/CLAUDE.md`「期待の行と実 DB の段」。
