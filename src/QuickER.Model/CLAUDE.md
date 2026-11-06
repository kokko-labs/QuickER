# QuickER.Model

意味モデル（`ErDiagram`・`Entity`・`Column`・`Relationship`・`UniqueConstraint`）と、名前・制約名の規則の正本。
座標・色などの視覚情報は持たない。方言の宣言は `src/QuickER.Dialects/CLAUDE.md` を参照する。

## 名前の同一性（SchemaNames）

図に保存する名前の同一性は `SchemaNames` だけが決める。

- 対象はテーブル名・列名・制約名（外部キー・主キー・一意制約）・名前付きクエリの名前・パラメータ名・射影のフィールド名・結果の型名。
- 規則は「前後の空白を除き、`OrdinalIgnoreCase` で一致すれば同じ名前」。
  保存する形は前後の空白を除いた形で、綴り（大文字小文字）は書き換えない。PostgreSQL・Oracle の引用識別子では綴りが別の表を指すため。図と DB の照合は `SchemaDiffService` が組んだ相手の DB の綴りへ揃える。
- 空白はモデルの setter が除く（`Entity.TableName`・`Column.Name`・`UniqueConstraint.Name`・`Relationship.ConstraintName`・`QueryDefinition.Name` / `ResultTypeName`・`QueryParameter.Name`・`ProjectionField.Name`）。
  入口ごとに `Trim` を書かない。null はそのまま通し、読込の必須名の検査が場所を名指しして断る。
- 重複の判定・名前での引き当ては `SchemaNames.AreSame` / `Comparer` / `ToStoredForm` を通す。
- 照合のキー・シグネチャ（辞書のキー、列名を並べた文字列）は `SchemaNames.Key` で作る。`Key` が一致する ⇔ `AreSame`。`OrdinalIgnoreCase` が同じとみなす文字の代表へ 1 文字ずつ畳む。プロセス内の比較専用で、保存も表示もしない。
- キーを `ToLowerInvariant` / `ToUpperInvariant` で作らない。ケルビン記号 U+212A と `k` のように小文字化だけで同じになる名前や、大文字化だけで食い違う文字（Garay 文字など）で、`OrdinalIgnoreCase` で組んだ表・列と答えが割れる（同期では、列とそろわない一意制約・外部キーの差分と、取り違えた外部キーの外し直しの警告になる）。

### 素の比較を残す例外

次の 4 種類だけ。理由つきで `SourceRuleTests` の規則 `schema-name-comparisons` の既知の一覧に載せる。

- DB 取込のカタログの照合（`SchemaTableEntry.TryGetExact` / `TryGetColumnExact`）。DB には `Dup` と `dup` が共存し得るので序数一致で引く。衝突の判定は `KeyComparer` / `ColumnsByName` が `SchemaNames` の規則で行い、後着を `TableNameCollision` / `ColumnNameCollision` で捨てる。
- 生 SQL のパラメータの束縛（`RawSqlAnalyzer` の Ordinal）。実行時のバインダと IN 展開が区別する。`DECLARE` したローカル変数は束縛と無関係なので区別しない。
- 生成する C# の識別子そのもの。言語が区別する。大小だけ違う 2 つの名前が同じ識別子になる衝突は生成時エラーで名指しする。
- 綴りの判定。`SchemaNames` が同じとみなす相手と綴りまで一致するかを見る判定（DB の綴りへ揃えるかどうか、`NameLookup` / `NameMatching` の序数一致の段＝同じ名前の候補が片側に複数あるときに綴りまで一致する 1 件を選ぶ優先順位）。同一性は決めない。

データの値の比較（主キーの値・varchar の照合順序）は名前ではないので対象外。

### 入口の挙動

- ツール（両ホスト）・DBML / Mermaid / Excel の取込・C# 生成は重複を断り、DB 取込は後着を捨てて警告する。
- プロパティパネルの VM（`EntityViewModel.TableName` / `ColumnViewModel.Name`）は重複する書き換えを受け付けず、元の名前へ戻して `NameChangeRejected` で知らせる。Undo / Redo の適用は `ApplyTableName` / `ApplyName` で検査を通さない（履歴の往復を壊さない）。
- 図のファイルの読込は `SchemaNames` で衝突する名前を断らない。手で編集したファイルは重複を断る入口を通らない。そうした図との同期は、綴りまで一致する相手を優先して組み、組めない名前を追加・削除として出す。読込で断る案は、開けなくなる仕様変更になるため見送り。

### ガード

- `SourceRuleTests` の規則 `schema-name-comparisons`：`src/` の素の比較・変換を構文木で全数拾い、理由つきの既知の一覧と件数まで厳密に一致させる。拾う単位は文で、何かは文のトークンを空白なしで連結した先頭 90 字（一覧はファイル・先頭 90 字・件数）。未移行の置き場は持たない（新しい出現は直すか、理由つきの例外として一覧に載せる）。比較の指定は修飾を除いた右端の 2 つの名前で見る（`System.StringComparison.Ordinal`・`global::System.StringComparer.Ordinal` の綴りでも拾う）。
- `SourceRuleTests` の規則 `sync-name-comparison-tokens`：同期の差分と計画の 4 ファイル（`SchemaDiffService` / `SyncPlanner` / `NameMatching` / `UniqueConstraintNaming`）に、`Trim` 系・`ToLower` / `ToUpper` 系・大小を無視する比較の指定が識別子として現れないこと。比較器をフィールドに入れて使う形や、引数名がモデルの名前でないキーの関数は本体のガードが拾えないための補助。Ordinal は綴りの判定とキーどうしの比較にだけ使ってよい。
- `SchemaNamesKeyTests`：Unicode の全スカラー値で `Key` の一致が `AreSame` と同じ。前後の空白の扱い・対になっていないサロゲートも固定する。
- mutation 実証済み。

### 名前の総当たり

- `NameIdentity*FuzzTests`：ツール両ホスト・VM・取込 3 形式・クエリ定義ダイアログ・SQLite の DB 取込・C# 生成へ名前の組を流して、重複の判定と保存形を表明する。
- `NameFuzzCompilationTests`：生成→実コンパイルでエラー 0・警告 0。
- Integration の `NameFuzzDdlRoundTripIntegrationTests.cs`（方言ごとの派生と共通の基底）：5 方言で DDL を適用して取り込み直した差分が空。
- 期待値の正本は `NameIdentityRule`。既知の失敗は `NameFuzzKnownFailures` に指摘の番号と直す区切りつきで載せる。一覧に無い失敗も、載っているのに起きない失敗も赤。`QUICKER_FUZZ_SEEDS` でシードを増やせる。
- 網羅の組：`NameFuzz.CoveragePairs`（`Pairs` の外なので種の指紋の要約に出ない）を `NameFuzzRunner.Run` が全入口で種の前に流す（単独の種では流さない）。
  - 大文字化（OrdinalIgnoreCase）では同じで小文字化（ToLowerInvariant）では違う組（CaseUpperFoldOnly）を Plain と WithWhitespace で 1 つずつ。ギリシャ文字の σ（U+03C3）と語末の ς（U+03C2）はどちらも Σ に大文字化される（Unicode 全体でこの性質の組は 44 組）。
  - 組が宣言した違い・形と分類が食い違う場合や、入口が受け付けずに飛ばした組は、入口と組を名指しして赤（`ledger.DeferCoverageCheck`）。
  - 名前から C# の識別子を作る入口（`derivedName`）では、作る識別子が大文字小文字を無視して同じ組は、飛ばさずに「後から入れた名前を断る」のが答えとして流す。小文字化だけで同じになる組（CaseLowerFoldOnly、Unicode 全体で 9 組）は、テーブル・列・クエリ名の 5 入口で生成時エラーか定義時の拒否で一貫して断られる。
  - C# 生成の射影のフィールドは名前をそのまま DTO のプロパティ名にする（変換しない）ので、識別子は名前そのもの。
- DDL の往復は、どの性質も持たない網羅の名前 `NameFuzzDiagrams.CoverageNames`（`Orders`）を `NamesForRun` が代表の名前の前に足す（単独の種では足さない）。性質が Plain でない、またはどれかの置き場所で図に入らずに飛ばされたら、名前と置き場所を名指しして赤。
- 網羅しない組の一覧は、名前の総当たりでは空、DDL の往復ではドットを含むテーブル名の受け入れだけ。

## 制約名の組み立て（ConstraintNames）

制約名の安全化と、名前を持たない制約の既定名の組み立ては `ConstraintNames` だけで行う。

- `SafeName` は前後の空白を先に除いてから `.` と空白を `_` へ置き換える。同期の計画はテーブル名を `Trim` してから名前を組み立てるので、除かないと前後に空白のあるテーブル名で DDL（`FK__Orders_…`）と同期（`FK_Orders_…`）の名前が食い違い、存在しない名前の制約を外そうとして失敗する。
- 規則を写さない。既定名は `ConstraintNames.ForeignKey` / `PrimaryKey` で組み立て、`$"FK_{…}"` / `$"PK_{…}"` の補間や `"FK_" + …` の連結を呼び出し側で書かない（例外は `SqliteSchemaImporter` の取込時の識別用の名前で、`id` を含む別の規則）。
- 「名前が空なら既定名」の分岐も呼び出し側で書かず `ConstraintNames.EffectiveForeignKey` を通す。DDL・同期スクリプト・同期の差分のシグネチャが同じ関数を通る。差分だけが素の名前で比べると、名前の無いリレーションと DDL が既定名で作った live の外部キーが別物に見え、何も変えていなくても追加と削除が出て、既定のまま実行すると名前の重複で失敗する。
- 名前の無いリレーションの名前は図全体から `ConstraintNames.ResolveForeignKeyNames` で求める。同じテーブルの組に名前の無いリレーションが 2 本あると既定名が重なり DDL の 2 本目が失敗するため、2 本目に子の列名を足す。取込が図へ書き込む `NameUnnamedForeignKeys` はこの関数の上に作った同じ規則。図は書き換えない。
- 外部キーを作らない名前の無い関係（多対多・列ペア 0 件・列を解決できない＝`ConstraintNames.DefinesForeignKey` が偽）は既定名を取らない。DDL はその関係に外部キーを作らないので、取らせると同じ組の本物の外部キーが `FK_{子}_{親}_{列}` へずれ、既定名で作った DB との同期で外部キーの追加と削除が出る。`CollectExisting` も同じ判定で、名前を持つ関係は外部キーを作らなくても予約する。判定は Provider の `ForeignKeyColumnPairResolver` と同じ規則の写しで、一致は `ForeignKeyDefinitionParityTests` が固定する。
- DDL 2 か所（基底・SQLite）と差分のシグネチャがこの関数を通す。差分は求めた名前を AddForeignKey / AlterForeignKey の `ForeignKeyName` に載せ、同期スクリプト 4 方言と SQLite の再構築は `SyncScriptBuilderHelper.ForeignKeyNameToAdd` でそれを優先する。どれか 1 つでもリレーションの名前から求め直すと、2 本目だけが DDL と違う名前になる（DDL だけ直すと差分が既定名で探して偽の追加・削除を出し続ける）。
- GUI の手作成・内蔵チャット・MCP のツールもこれを呼ぶので、`dbo.Orders` のようなスキーマ付きの名前でも作成経路で制約名が割れない。

ガード：

- `SourceRuleTests` の規則 `constraint-name-safe-rule-copies`・`constraint-name-default-compositions`：`src/` を走査して置換規則の写しと既定名の組み立てを検出する。`string.Concat` 等の別の形はすり抜ける（検出する形は規則の表のコメントに明記）。
- `DialectDdlTableTests` の行 `ddl-*-trimmed-table`・`sync-*-trimmed-table`：前後に空白のあるテーブル名で DDL と同期の FK 名・PK 名が 5 方言とも一致する。
- `ConstraintNameCreationPathTests`：3 つの作成経路が同じ FK 名・要約の一意制約名で DDL と一致する。作成経路の照合を `ErDiagramToolHostParityTests` へ載せないのは、2 つのツールホストどうしを比べるテストは両方が同時に退行しても緑のままになるため。
- `ConstraintNamesTests`（`ResolveUniqueConstraintNames_NumbersOnlyCollidingSynthesizedNames` を含む）・`UnnamedForeignKeyNamingTests`（5 方言の DDL・同期スクリプト・作り直しの同期スクリプト）
- `DialectDdlTableTests` の行 `ddl-constraint-names-schema-qualified-trimmed-table`（スキーマ付きで前後に空白のある表名の FK 名・PK 名）・`ddl-unique-unnamed-synthesized-name-is-safe`（一意制約の合成名の安全化）・`ddl-fk-unnamed-non-fk-relationship-first-*`（外部キーを作らない関係が先に並ぶ 3 種）・`ddl-fk-named-many-to-many-reserves-name`・`ddl-fk-unnamed-avoids-unique-constraint-name`・`ddl-fk-unnamed-same-pair-twice-with-named-third`：5 方言の DDL が付ける外部キー名・主キー名・合成名
- 期待の行・実 DB の行の Id（DDL・差分の外部キー名。詳細は `src/QuickER.Provider/CLAUDE.md`「制約名の正本と識別子の分割クォート」）：`diff-fk-unnamed-matches-default-named-live`・`realdb-fk-unnamed-relationship-no-diff-after-ddl`（名前の無いリレーションと DDL が既定名で作った live の外部キーに差分が出ない）・`diff-unnamed-fk-many-to-many-first-matches-live-with-default-name`・`realdb-fk-unnamed-many-to-many-first-ddl-uses-default-name`・`realdb-fk-unnamed-many-to-many-first-no-diff-with-default-named-db`（多対多が先に並んでも外部キーは既定名）・`diff-unnamed-fk-same-pair-twice-matches-live-named-by-ddl`・`diff-unnamed-fk-second-referential-action-change-replaces-under-same-name`・`realdb-fk-unnamed-same-pair-ddl-applies-no-diff`（名前の無い同じ組の外部キー 2 本）・`UnnamedForeignKeyNamingTests.Ddl_ManyToManyFirst_DoesNotShiftForeignKeyNames`（多対多が先に並んでも外部キーは既定名のまま）。
- mutation 実証済み。

## 識別子の長さの上限

QuickER が付ける既定の制約名は識別子の長さの上限に収め、上限は引数で受ける。経路は 2 つだけ。

- 上限の正本は QuickER.Dialects の `IdentifierLengthLimits`（`src/QuickER.Dialects/CLAUDE.md`「識別子の長さの上限」）。各方言の識別子クラスの `Limit` と `SyncDialectCapabilities.IdentifierLengthLimit` はそれを指すだけ（`IdentifierLengthLimitDeclarationTests` が 5 方言で同じインスタンスであることを固定する）。
- 既定名（`ConstraintNames.PrimaryKey` / `ForeignKey` / `UniqueForeignKey` / `ResolveForeignKeyNames` / `NameUnnamedForeignKeys`・`UniqueConstraint.SynthesizeName`・`UniqueConstraintNaming`・`TableConstraintLineBuilder`）は上限を省略できない引数で受け、`IdentifierLengthLimit.Fit` で収める。上限内ならそのまま、超えるときだけ先頭の部分＋`_`＋縮める前の名前の SHA-256 の先頭 8 桁に置き換える。
- 省略可能にしない。既定値の上限なしで黙って縮めない呼び出しが残り、SQL Server・MySQL・Oracle では DDL の適用が断られ、PostgreSQL では黙って切り詰められて取り込み直すと別の名前になる。
- 経路 (1)：図へ保存しない名前（DDL・同期スクリプト・差分・同期の計画）は DDL・同期の方言の上限。`DdlGeneratorBase.IdentifierLimit` / `SyncScriptBuilderBase.IdentifierLimit`（抽象で、方言が宣言を忘れるとビルドで止まる）、差分と計画は `SyncDialectCapabilities.IdentifierLengthLimit`。DDL の方言は図の対象 DBMS と食い違い得るので図からは引かない。
- 経路 (2)：図へ保存する名前（ツール・GUI の手作成の外部キー名・取込と C# リバースの `NameUnnamedForeignKeys`）と、図の名前として見せる合成名（C# 生成の `[UniqueConstraint(Name = …)]`・ツールの要約・プロパティパネルの一意制約名）は図の対象 DBMS の上限（`IdentifierLengthLimits.ForDiagramTarget(TargetDbms)`）。
  - GUI は `MainViewModel.CurrentIdentifierLimit()`。図を受け取る `CollectExisting` / `IsTaken` / `ResolveForeignKeyNamesById` と `NameLimitNotes.Find` も上限を引数で受ける。Model は方言名から上限を引かない（`SourceRuleTests` の規則 `model-dialect-lookups`＝Model のソースの `IdentifierLengthLimits` の参照を構文木で数える。既知の一覧なし）。
  - DBML・Mermaid の取込は対象 DBMS を引数で受けて返す図の `TargetDbms` にする。Excel 定義書は宣言が無ければ取込先の図の対象 DBMS。
  - 経路の正本は `ConstraintNames` の XmlDoc。
- PostgreSQL の既存 DB の吸収：長い既定名をそのまま DDL へ書くと PostgreSQL が 63 バイトで切り詰めて保存する。差分は、名前の無いリレーションに限り、live の名前が「縮める前の名前を `IdentifierLengthLimit.Truncate` で切ったもの」なら同じ外部キーとみなす。
  - 宣言は `SyncDialectCapabilities.TruncatesLongIdentifiers`（PostgreSQL だけ）、判定は `SchemaDiffService.ResolveTargetForeignKeyNames`。主キー・一意制約は差分が名前で突き合わせないので要らない。
  - 吸収するのは live の外部キーと同じ子・同じ親の表の外部キーだけで、live の外部キー 1 本につき 1 本（候補が複数なら列ペアが一致するもの、無ければ図の並びの先）。子の表名が長いと、親の違う外部キーや同じ親への 2 本目の縮める前の名前も切り詰めると同じ名前になる。子と名前だけで吸収すると、後から図に足した 2 本目にも DB の 1 本目と同じ名前を与えて 42710 で断られる。「同じ親だけ」と「列ペアの一致を先に」は互いを補う二重の守りで、片方だけを外しても来歴の行は緑のままになる。
- 差分の live 側の上限は外しても緑のまま。取込の外部キーは必ず DB の名前を持つので、名前を求め直さない。

ガード：

- `LongDefaultConstraintNameTests`（4 方言の既定名の関数が上限以内で互いに違う名前を返す）・`DialectDdlTableTests` の行 `ddl-long-default-names-are-shortened`・`sync-add-table-long-default-names-are-shortened`（DDL・同期スクリプトが同じ縮めた名前を使い、縮める前の名前を出さない。SQLite は縮めない）・`SyncPlanExpectationRows` の行 `diff-long-default-fk-names-match-shortened-live`（4 方言の差分が縮めた名前の live を同じ外部キーとみなす）・`diff-long-default-pg-legacy-truncated-fk-name-is-absorbed`（PostgreSQL の吸収と、合成の能力 `PostgreSql-NoTruncation` で吸収しない対照）・`diff-pg-user-named-fk-equal-to-shortened-default-is-not-absorbed`（利用者の名前は吸収しない）。期待の行は DB を使わない。
- `PostgreSqlLongDefaultConstraintNameIntegrationTests`（能力を差し替えて吸収しない対照）。実 DB の期待の行 `realdb-long-default-names-apply-and-round-trip-no-diff` / `realdb-long-default-pg-legacy-truncated-fk-name-is-absorbed`（`SyncPlanExpectationRows.RealCrossDialect.cs`）。同期計画の来歴の行 `SyncPlanHistoryRows`。
- `ToolScenarioTests`（行 `long-default-name-*`＝両ホストの保存名と要約・縮めた既定名との衝突・名前の無い外部キーの要約と削除と重複の名指し）・`MainViewModelRelationshipConstraintNameTests`（GUI の手作成）・`FormatRoundTripTests`（DBML・Mermaid の取込＝規則の行 `dbml-rule-long-default-name-*`・`mermaid-rule-carries-target-dbms`、形の行 `dbml-long-default-name-*`）。
- `CodegenCaseTests` の行 `code-unique-constraint-name-*`・`UniqueConstraintResolvedNameLimitTests`（方言の切替で表示し直す。`ResolvedName_FollowsCollisionsAcrossTables`）・`IdentifierLengthLimitTests`（`Fit` / `Truncate`）。
- mutation 実証済み。

## 名前の上限の通知（NameLimitNotes）

名前の入力・ツール・方言の切替は、受け付けない名前を入口と同じ判定で知らせる。

- 判定は DDL・同期の入口と同じ `IdentifierLengthLimit.DescribeViolation`。写しを作らない。
- (1) GUI の名前の確定：`DiagramChangeTracker.NameEdited`（履歴へ積んだ編集でだけ発火。Undo / Redo・一括置換では発火しない。対象はテーブル名・カラム名・外部キー名・一意制約名）を `MainViewModel.OnNameEdited` が受け、図の対象 DBMS の上限で超過なら `NotifyStatus` で警告する（値は受け付ける）。
- (2) ツールの応答：`NameLimitNotes`（Model）が変更の前後の違反を比べ、その呼び出しで新しく生じたものだけを英語の注意文にする。MCP のツールホストは `Mutate`、内蔵チャットのツールは `ErDiagramDynamicTools.Execute` が同じ関数を通る。
- (3) 対象 DBMS の切替：`MainViewModel.CollectNamesRejectedBy` が切替先の上限で保存された名前を判定し、型の変換と同じ `ConfirmWarningDetails` に場所と理由つきで全件並べる。これだけでも確認のトリガーになる。
- ガード：`NameLimitStatusWarningTests`（4 種類の名前・MySQL の表せない文字とファイル名・Undo / Redo と丸ごと置換で警告しない・切替の確認の一覧とキャンセル）・`ToolScenarioTests`（行 `name-limit-*`＝両ホストで 5 つのツールと、以前からの違反を繰り返さないこと）・`NameLimitNotesTests`。mutation 実証済み。

## 一意制約

一意制約の正本は `Entity.UniqueConstraints`（`UniqueConstraint`＝Id / Name?〔null は生成時合成〕/ ColumnIds〔列 Guid・宣言順〕）。

- 取込は 5 方言とも真の UNIQUE 制約のみ（SQL Server=`is_unique_constraint=1`・SQLite=`origin='u'`・PostgreSQL=`contype='u'`・Oracle=`'U'`・MySQL=区別不能のため全一意インデックス。プレフィックス／関数インデックスは除外）。制約名は実名を永続化する（SQLite の自動名 `sqlite_autoindex_*` のみ null）。
- 未設定名は生成時に `UQ_{テーブル}_{列連結}` を合成する。規則の正本は `UniqueConstraint.SynthesizeName`（Provider を参照しない MCP ツールホストと共有するため Model に置く）。テーブル名・列名の安全化も `SynthesizeName` 自身が `ConstraintNames.SafeName` で行う。生成コードの `[UniqueConstraint(Name = …)]`・ツールの要約表示・DDL が同じ名前を作る。`UniqueConstraintNaming` は方言の安全化関数を受け取らずにこれを呼ぶ。
- 合成名がほかの制約の名前とぶつかるときは番号を足す。
  - `ConstraintNames.ResolveUniqueConstraintNames`：図全体を対象にする。予約するのは主キーの既定名・保存された一意制約名・保存された外部キー名・決めた順に加える合成名。ぶつかれば `_2` から空いた番号を `Fit` で上限に収める。
  - `ResolveUniqueConstraintName`：1 つの制約だけ（差分を通らない項目・SQLite の再構築の定義）。
  - `SynthesizeName` を直接呼ぶのは中の `SynthesizeFreeName` だけ。
  - DDL 生成・C# 生成・差分（`MatchSchemas` の後の複製の表で 1 回。`SchemaDiffItem.UniqueConstraintNames`〔表の追加〕と追加・作り直しの項目の `UniqueConstraintName`）・ツールの要約・GUI（`MainViewModel` が図の世代と方言の切替で捨てるキャッシュ）は、1 回の実行につき 1 回だけ解決する。
  - 名前の段の照合には使わない（図で名前を付けた制約だけを組む）。外部キー名の解決と `CollectExisting` もこの名前を避ける・数える。
  - ぶつかると、制約名・索引名がスキーマ単位の SQL Server・PostgreSQL・Oracle と、外部キーの索引名を持つ MySQL で、DDL・同期が名前の重複で失敗する。
  - 同期計画の総当たりの行：`{方言}-386-synthesized-unique-name`・`{方言}-synthesized-unique-name-cross-table`・`{方言}-synthesized-unique-name-foreign-key`。
  - ガード：`ConstraintNamesTests.ResolveUniqueConstraintNames_NumbersOnlyCollidingSynthesizedNames`・`SourceRuleTests` の規則 `unique-constraint-synthesize-name-calls`（構文木で数える。自己検査の行 `unique-constraint-synthesize-name-calls-forms`）・`ConstraintNameCreationPathTests` / `CodegenCaseTests` の行 `code-unique-constraint-name-*` のぶつかる行・`UniqueConstraintResolvedNameLimitTests.ResolvedName_FollowsCollisionsAcrossTables`。mutation 実証済み。
- 1 対 1 判定はモデルの `UniqueConstraints` を参照する（取込時判定・以降の自動再計算なし）。
- `SchemaSignature` へ含める。UNIQUE だけの差でも置換確認が出る。
- 文書 JSON は v1 のまま後方互換（旧ファイルは空リスト。`src/QuickER.Document/CLAUDE.md`「図ファイル形式の前方互換」）。
- 列削除はその列を含む制約ごと削除する。縮めて別の意味の制約に変質させない（GUI は `RemoveColumnCommand` の 1 Undo 単位、MCP の remove_column も同カスケード）。
- C# リバースは往復対応：`[UniqueConstraint]` をパーサーが読む（プロパティ名→`[Column]` 逆写像）。属性なしは制約なしでコードが正。旧生成コードと区別不能なのでフォールバックしない。FK の ConstraintName / OnDelete / OnUpdate も `[NavigationReference]` の任意名前付き引数として往復する（既定値は出力省略）。

## 主キーの実効順

PK の順序の正本は `Entity.PrimaryKeyColumnIds`（列 Guid・宣言順）。読み手は `Entity.GetPrimaryKeyColumnsInOrder()` だけが決める。

- 実効順は、リスト内位置→列宣言順の安定ソート。リスト外の PK 列は列順で末尾。非 PK 列と解決不能 Guid は無視。空なら列宣言順へフォールバックする。
- どの列が PK か（膜）の正本は `Column.IsPrimaryKey`。リストは順序の上書き情報で、書き手側の保守は要らない（GUI の PK トグル・列削除はリストに触れない。stale Guid は読み手が無視。OFF→ON は旧位置復活で Undo と整合する）。
- 取込は 5 方言とも序数順に常時 populate する（SQL Server / MySQL＝ORDINAL_POSITION・PG＝unnest WITH ORDINALITY・Oracle＝position：いずれも ORDER BY 済みクエリの到着順を信頼する・SQLite＝PRAGMA table_info の pk 値を昇順ソート）。
- 順序が意味を持つ読み手（DDL の PK 句・同期の PK ADD・SQLite 再構築・PK 差分比較・EF Core HasKey・署名・FK 自動ペア化）へ素の `Columns.Where(IsPrimaryKey)` を書かない。リスト空の図では同じ結果になるため、ビルドも既存テストも緑のまま、取込図でだけ列順へ退行する。
  読み手切替済みの面は `TableConstraintLineBuilder`・SQLite の DDL／再構築・4 方言同期の PK ADD・`SchemaDiffService.PrimaryKeyColumnNames`（順序込みの比較。順序だけの差も AlterPrimaryKey）・`SyncPlanner.ApplySelectedPrimaryKeyChange`（target は別 Guid 空間のため名前写像で newDef のリストを丸ごと置換）・EF Core HasKey・`SchemaSignature`（実効順を署名。空リスト vs 同順 populate は署名一致）・`ForeignKeyColumnResolver`（自動ペア化の並び）。
- GUI は `EntityViewModel` が順序を保持する（防御コピー→`ToModel()` で書き戻し。VM に同一 merge 規則のミラーを持つので、相互参照 XmlDoc に従い片方だけ変えない）。パススルーを外すと「取込→保存」で順序が静かに消える。
- 文書 JSON は v1 のまま（旧ファイルは空リスト・明示 null は Normalize が修復する）。Clone / `DiagramMergeReconciler` は columnIdMap で張り替える（`UniqueConstraint.ColumnIds` と同型）。
- 編集経路は 2 つ。MCP／内蔵チャットの `set_primary_key`（テーブル名のみで照合する全体置換。挙げた列は NOT NULL 正規化・外れた列の NULL 許容は据え置き・列は最低 1 つ必要）と、GUI の「主キーの順序」カード（複合 PK のときだけ表示）。詳細は GUI・MCP の領域の CLAUDE.md に従う。一度でも順序を明示すると、その図では順序が固定される。
- 往復統合テストの不動点表明だけでは取込の順序喪失を検出できない（取込が順序を落とすと 1 回目の射影・再生成 DDL・2 回目の射影がそろって列順になり一致する）。5 方言の `*DdlRoundTripIntegrationTests` は起点 DDL の宣言順と突き合わせる `SchemaReapplyAssertions.ShouldHavePrimaryKeyOrder` を併用する。射影側の `PrimaryKey` は join 文字列なので、`BeEquivalentTo` がコレクション順序を既定で無視する List では検出できない。
- ガード：`EntityPrimaryKeyOrderTests`（merge 規則・Clone 張り替え）・`SchemaSignatureTests` の順序系・`EntityViewModelTests`（パススルー・並び替え行の実効順と行インスタンス使い回し）・`FormatRoundTripTests`（マージの行 `merge-reconcile-primary-key-order-follows-column-ids`・`merge-excel-*-primary-key-*`・`merge-excel-*-order-*`）・`DocumentFormatTests`（行 `*-primary-key-*`＝往復・欠落・明示 null の読み）・同期計画の期待の行の主キーの順序の行（`diff-pk-order-*`・`plan-rebuild-selected-alter-pk-remaps-pk-order` など）・`DialectDdlTableTests` の行 `ddl-pk-composite-follows-primary-key-column-ids`（DDL の主キーの句の並び）。
- 編集経路のガード：`UndoOperationTests`（行 `primary-key-*`＝Undo 1 回で戻る・NULL 許容の復活・端の行・ピン留めの履歴化）・`PrimaryKeyOrderCardTests`（カードの配線）・`ToolScenarioTests`（行 `primary-key-*`＝両ホストのツール）・`ErDiagramDynamicToolsPrimaryKeyTests`（GUI のツールの Undo）・`ErDiagramToolHostParityTests`（`EntitySnap.PrimaryKeyOrder`＝実効順と `StoredPrimaryKeyColumnIds`＝保存リストの列名写像〔解決不能 Guid は `?`〕の両方を射影し、順序の食い違いを検知する。最後に成功する `set_primary_key` は列宣言順と食い違う並び）。mutation 実証済み。

## 主キーの順序の出力判定

主キーの順序を生成物・DBML へ載せるか、欠落として告知するかの判定は `Entity.GetReorderedPrimaryKeyColumnNames()` だけ。

- MCP・内蔵チャットの `get_diagram_summary` は、主キーの順序が列の宣言順と食い違うときだけ `Primary key order:` 行を出す（判定は同じ関数）。
- 使う 3 箇所：C# 生成の `[DbTableMeta].PrimaryKeyOrder`（`CSharpGenerationModelBuilder.BuildPrimaryKeyOrderArgument`）・DBML の `[pk]` 索引（`DbmlExporter`）・Mermaid の欠落告知（`MermaidExporter.DetectOmissions`）。
- 独自に「主キーが 2 列以上なら出す」などへ変えると、順序を明示していない図・宣言順どおりの図まで生成物と DBML 出力が変わる。今は食い違う図でだけ変わる（全フィクスチャ・サンプルの Entity 宣言部はバイト不変）。
- リバース側は主キー列とちょうど一致する指定だけを採り、部分的な指定を併合規則で「それらしく」受けない。生成側は常に主キー全列を実効順で書くので、欠けていれば手で壊した印。
- C# は順序が宣言順と食い違うテーブルにだけ `[DbTableMeta(PrimaryKeyOrder = new[] { … })]` をプロパティ名で出し、リバースが読む（食い違わない図の生成物はバイト不変）。キー列とちょうど一致しない指定（未知の名前・キーでない列・重複・過不足）は採らずに警告して宣言順にする。
- GUI のマージ取込は、コードが引数を書いていない（古い生成コード）テーブルに限り、主キー列の集合が列名で一致すれば現在図の順序を引き継ぐ（`ReverseMergePostProcessor.ApplyPrimaryKeyOrder`。規則の本体は Excel 定義書のマージ取込と共有する `Entity.InheritPrimaryKeyOrderFrom`。引数があれば、不整合で採らなかった場合もコードが勝つ＝`CodeReverseResult.TablesWithPrimaryKeyOrder`）。
- DBML は食い違うテーブルだけ主キーを列の `pk` でなく `Indexes` 先頭の `(列, …) [pk]` で書き、取込は `[pk]` 索引を構成列と順序として読む（一意制約は作らない・索引の名前は捨てる。列の `pk` と集合が違う／`[pk]` 索引が 2 つ／索引内の同じ列の重複は行を名指しして断る）。
- Mermaid は順序の構文が無いので `ExportOmissionKind.PrimaryKeyOrder` で告知する。Excel は順序を運ばない（docs に明記）。
- ガード：`CSharpReversePrimaryKeyOrderTests`（無明示と宣言順どおりの明示で生成物がバイト一致）・`FormatRoundTripTests`（C# の往復＝形の行 `csharp-reordered-primary-key`、不整合 6 種＝規則の行 `csharp-rule-primary-key-order-mismatch-*`、古い生成コードのマージで順序が保たれる＝マージの行 `merge-csharp-old-generated-code-keeps-current-key-order`、DBML の往復・列順どおりは出力不変＝形の行 `dbml-reordered-primary-key`・`dbml-primary-key-in-column-order-*`、他ツール形式・断る 4 条件＝規則の行 `dbml-rule-primary-key-*`・`dbml-rule-two-primary-key-indexes-are-rejected`・`dbml-rule-duplicate-column-in-primary-key-index-is-rejected`、Mermaid の告知＝形の行 `mermaid-primary-key-order-*`）。mutation 実証済み。

## リレーションの列ペア

リレーションの列対応の正本は `Relationship.ColumnPairs`（`RelationshipColumnPair`＝親列 Guid×子列 Guid・宣言順）。旧 `SourceColumnId` / `TargetColumnId` は後方互換なしで廃止した（旧文書 JSON の列ペア情報は読み捨て。Version 1 のまま）。

- 列ペアが唯一の正本で、推測フォールバックを持たない。列名解決は `ForeignKeyColumnPairResolver`（Provider）に 1 本化し、ペア 0 件（多対多・未設定）・解決不能な列を含む FK は、DDL の FK 句・FK 差分の対象外として明示スキップする。
- 取込は 5 方言とも全ペアを宣言順で正本化する（複合 FK に由来する取込警告は無い）。1 対 1 判定は列集合ベース。
- 作成時は親 PK 全列を自動ペア化する。列ごとに `ForeignKeyColumnResolver` の命名マッチを使い、重複子列を排除し、引き当て不能列はペアなしにする。
  子の主キー列は「①親テーブル名由来の名前一致」と「複合主キーの親の同名前段」だけを候補にする（識別関係を拾う）。単一主キーの親の同名一致（単独の `id` の誤爆）・FK 印・後方一致・自己参照では外す。複合主キーの親の同名前段でも、名前が単独の `id` の子の主キー列は外す（`id` はそれぞれの表自身の代理キー。`Projects(TenantId, Id)`→`Tasks(TenantId, Id, ProjectId)` の `Id` は①の `ProjectId` で解く）。ガード：`ForeignKeyColumnResolverTests`。
- 制約名は AI ツールと同じ `ConstraintNames.UniqueForeignKey`（衝突しなければ既定名のまま）。
- 列削除は当該リレーションの全ペアをクリアする（線・種別・制約名は残す。1 Undo 単位。MCP remove_column とパリティ）。
- MCP / 内蔵チャットの add_relationship：`source_columns` / `target_columns`（並行配列・同数・宣言順・省略時は親 PK 全列自動ペア化）と任意の `constraint_name`。
  - 明示した親側の列が `Entity.IsCandidateKey`（主キー全列か一意制約の列集合と一致）でなければ、拒否せず注意文を添える（MySQL は一意でない索引列を参照できるため）。`ToolScenarioTests`（行 `note-add-relationship-explicit-non-candidate-key-is-reported-*`）が両ホストで固定する。
  - `constraint_name` は、図の既存の FK・PK・一意制約の名前（`ConstraintNames.CollectExisting`＝DDL が付ける名前）と大文字小文字を無視して衝突すれば拒否する。省略時は `ConstraintNames.UniqueForeignKey` が既定名・子の列名・番号の順に衝突しない名前を付ける。
  - remove_relationship は複数一致時に `constraint_name` で特定する（無指定なら候補列挙エラー）。get_diagram_summary に列ペアと制約名を表示する。
- DBML は複合 Ref 構文 `Ref: p.(a,b) < c.(x,y)` の出力＋取込（取込の列再推論は無く、Ref 行の列名が正本。不整合行はペアだけ破棄）。Excel/HTML 定義書は列セルをカンマ区切り複数列表記（`FK{n}` は複合の全構成子列に同番。`UQ{n}` と同流儀）。Mermaid は列構文が無いので出力現状維持、取込推論は PK 全列ペア化。
- Mermaid の必須のラベルは制約名を運ぶ。名前の無いリレーションの出力値は `relates`（`MermaidExporter.UnnamedRelationshipLabel`）。引用つきや複数語・図の中で重複するもの・主キー/一意制約名と重なるものは採らず、採らなかった関係は、既定名が空いていれば名前なし・ふさがっていれば `UniqueForeignKey`。ガード：`FormatRoundTripTests`（形の行 `mermaid-unnamed-relationships-relates`／規則の行 `mermaid-rule-repeated-label-is-not-adopted` ほか `mermaid-rule-*` のラベルの行）。
- 名前の無い関係の既定名がふさがっていれば一意化する規則は `ConstraintNames.NameUnnamedForeignKeys` が正本で、Mermaid・DBML・Excel 定義書の 3 取込と C# リバースが共有する。C# リバースは、解析の最後と GUI マージの補完（`ReverseMergePostProcessor.Apply`）の後の 2 回呼ぶ（補完が現在図の null で名前を戻すため）。明示された名前は重複していても変えない（PostgreSQL では外部キー名がテーブル単位で一意なため）。ガード：`FormatRoundTripTests`（形の行 `dbml-unnamed-relationships`・`excel-unnamed-same-pair`・`excel-single-unnamed`・`excel-named-row-takes-default`／規則の行 `dbml-rule-same-pair-twice-without-names-second-gets-unique-name`・`dbml-rule-single-unnamed-ref-stays-unnamed`・`dbml-rule-named-ref*`・`excel-rule-hand-written-empty-names-second-gets-unique-name`・`csharp-rule-two-unnamed-references-get-distinct-names-in-ddl`・`csharp-rule-single-unnamed-reference-stays-unnamed`・`csharp-rule-explicit-names-are-kept`／マージの行 `merge-csharp-current-with-duplicate-unnamed-references-gets-distinct-names`・`merge-csharp-names-supplied-by-current-diagram-are-kept`）。
- 生成コード（C#）はペア 2 組以上を対象外とする（`CodeGen_Warning_RelationshipCompositeSkipped` でナビゲーション / EF Core Fluent をスキップ。単一ペアは従来どおり）。C# リバースも単一列のみ。
