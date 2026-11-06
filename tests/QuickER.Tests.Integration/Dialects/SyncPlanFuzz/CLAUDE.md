# SyncPlanFuzz（同期計画の総当たりと期待の行）

`tests/QuickER.Tests.Integration/Dialects/SyncPlanFuzz/` のテストのエンジンの規則。
実 DB の総当たり（`SyncPlanFuzzTestsBase`）に、DB を使わない網と決定的な行を足し、同じランナー（`SyncPlanFuzzRunner`）と既知の失敗の一覧（`SyncPlanFuzzKnownFailures`）で照合する。
エンジンの共通の枠組み（種・既知の失敗・網羅の計数・次元表・登録簿・夜間の目印）は `tests/QuickER.Tests/TestSupport/CLAUDE.md`「エンジンの核（Engines/）」を参照する。
同期計画の製品の規則（`SyncPlanner`・`SchemaDiffService`）は `src/QuickER.Provider/CLAUDE.md`「差分と同期」と、同期の総当たりの節 `src/QuickER.Provider/CLAUDE.md`「同期の総当たりと期待の行」を参照する。

## 静的な規則

- `SyncPlanStaticRules.Evaluate` は、逐次 DDL の 4 方言だけを対象に、実際の計画の文を順に DB 側の図へ当てて DB が断る最初の文を予言する。再構築の方言は判定しない。
- 規則は 6 つ。
  - R1：参照されているキー・表を、外部キーが残ったまま外す（MySQL は先頭の列の索引も）。
  - R2：SQL Server で、一意制約・主キー・外部キーが使う列の型を変える。
  - R3：MySQL で、外部キーの先頭の列を支える索引を外す。
  - R4：方言の名前の範囲で名前が衝突する。
  - R5：候補キーの無い参照の外部キーを足す。
  - R6：SQL Server・MySQL・Oracle で表の最後の列を消す。
- 予言したのに予告の警告が無ければ違反にする。`NonPredictingWarnings` 以外の警告を予告とみなし、断られる文と警告は結び付けない（実 DB の表明 1 と同じ粒度）。
- DDL を暗黙コミットする方言では、次を D9 の違反にする。
  - 断られる文より前に外して後ろで付け直す制約。
  - 断られる文より後ろの表・列の削除のために先に外す制約。
  - 候補キーの警告を出した外部キー自身は除く（表明 1b と同じ）。

## 能力の組の網

- `SyncPlanCapabilityNetTestsBase`（5 方言の派生・DB なし）が、同じ生成器の種で目標の図と計画を作り、静的な規則だけで照合する。
  - 種の数は普段 5000・夜間 20000（`QUICKER_SYNC_CAPNET_SEEDS` / `QUICKER_SYNC_CAPNET_SEED`・夜間の目印つき）。
  - 網の種 N と実 DB の種 N は別のケース（実 DB は取り込んだ図の並びで変更の乱数を引く）。種の番号には「網の種」「実 DB の種」を添える。
- 次元表 `SyncPlanCapabilityNetDimensions` の中身は次のとおり。
  - 選び方・変更の種類・同期計画の形を変える `SyncDialectCapabilities` の 17 項目。派生ごとの固定の水準で、書き下ろしの表とプロバイダの値の食い違いは宣言の誤り。能力どうしの組は数えない。
  - 変更 1 つにだけ値を持つ 3 因子。生成器が変更の文に `{type=…}` `{refs=…}` の札を書く。
    - `AlteredType`：型の変更の行き先。方言の上限ちょうどは `AtLimit`。
    - `ReferencingFks`：主キーの変更で参照する外部キーの数。
    - `ReferentialAction`：参照アクションの変更の行き先。
- 因子の棚卸しの同期計画の能力の除外 17 件と、名前の DDL の往復の識別子の上限・`TruncatesLongIdentifiers` の除外は、この次元表の Source になっている。

## 実 DB の突き合わせ

- 実 DB の総当たりも同じ規則を計画に当てる。
  - 予言したのにコミットしたら違反（規則か計画の読み方の誤り）。
  - 予言して断られたら注 `static-rule-predicted`。
  - 予言せずに断られたら注 `static-rule-missed`。
  - D9 を予言した失敗は `static-d9:observed` / `not-observed` を数える。

## 決定的な行

- `SyncPlanScenarios.Table`：範囲は `方言/scenarios`。種の前に流し、単独の種では流さない。
- 指摘の形を最小にした行と、指摘を持たず Id が `pass-` の合格側の行（製品が警告して止まる形）を持つ。
- 合格側の行は網と実 DB の両方で「規則が失敗を予言し、違反が 0」を表明する（`SyncPlanScenarios.PassRowProblems`）。

## 形の行

- `SyncPlanShapeRows`（登録簿の部分 `ShapeRows`）は、主キーの変更 6 × 参照する外部キーの数 3 × 型の変更 6 × 明示の削除 3 × 一意制約の数 3 の網羅の配列 86 行。P1 と Q6 の 3 つ組を含むことを網が表明する。Oracle は一意制約 2 つの行を除いて 47 行。
- 網と夜間の実 DB は全行を流す。普段の実 DB は 2 因子の網羅に要る 38 行（Oracle 24 行）だけを流す。
- 夜間だけの行の項目は範囲 `方言/scenarios/nightly`。実 DB の総当たりは夜間の実行でだけ受け持ち、網は常に受け持つ。

## 生成器と方言の宣言

- 生成器は方言の宣言で DB が断る形を避ける。
  - `AllowDuplicateForeignKeys`：ORA-02275。
  - `AllowDuplicateUniqueConstraints`：ORA-02261。
  - `DistinguishesSameSetConstraints`：SQLite は同じ列の並びの制約を名前で見分けないので、目標の図の変更で重ねない。
  - `AllowsDateTimePrecisionDecreaseWithData`：ORA-30082。
  - `LimitType`：方言の上限ちょうどの型。
  - `RespelledTypeName`：同じ型の別の綴り。
  - `SymbolCharacter`：識別子の引用の中で特別な文字。
  - `OtherSchema`：SQL Server の別スキーマの同じ名前の表。
- 必須の形は方言ごとに決める（`FuzzShape.RequiredFor`）。実 DB では「同じ列の並びの一意制約 2 つの片方の削除と型の変更」を求めない。
  - SQLite は DB が同じ列の並びの UNIQUE を 1 つの索引にまとめる。
  - SQL Server は普段 100 種で出ず、形の行の Q6 の 3 つ組が決定的に担う。

## キーの照合

- 外部キーの列ペアは対応の集合で比べる。`SyncPlanFuzzRunner.PairSetText` が「親の列>子の列」の組を並べ替える。製品の差分は並びを見ずに比べ、参照先のキーの並びを要する方言は付け直しをキーの並びにするため。
- 規則表 `SyncPlanFuzzOracleTests` の `rule5-composite-foreign-key-reordered-pairs-remain` / `rule5-composite-foreign-key-swapped-pairs-lost` が固定する。

## 来歴の行

- `SyncPlanHistoryRows`（PostgreSQL・登録簿の部分 `HistoryRows`）は、DB の来歴 × 子の表名の長さ × 目標の図で足す 2 本目の名前の無い外部キー（足さない・別の親・同じ親）の全積 12 行。
  - `SyncPlanHistory.Current`：今の DDL。
  - `SyncPlanHistory.PostgreSqlUnshortened`：旧版の DDL。名前の無い外部キーの縮める前の既定名を PostgreSQL が 63 バイトで切り詰めた名前になる（既定名を上限に収める規則は後の版から）。
- 来歴の規則は版ごとの規則を関数で持つ（公開版の DDL 生成器は使わない）。
  - `SyncPlanHistories.Apply`：DB 側の図の名前の無い外部キーを名付けてから DB を作る。
  - `Forget`：目標の図ではその名前を外す（利用者の図は名前を付けていない）。
- 旧版の DDL の来歴でだけ起きる吸収（名前の無い外部キーを縮める前の名前から取り込む形）は、行 `history-PostgreSqlUnshortened-long-added` が網の静的な規則 R4 と実 PG の既定の同期の両方で固定する。
- 来歴は現実にあり得る状態だけを作る。旧版の DB には外部キーが 1 本だけで、2 本目は後で図に足す。
- 12 行とも、差分が来歴どおりで（図を変えない行は差分なし・2 本目の行は外部キーの追加だけ）、網では規則が失敗を予言せず、実 PG では既定の同期がコミットして取り込み直した差分が空になる。
  - 合格側の `pass-` の行と期待が逆なので `NetProblems` / `RealProblems` は別の関数。
  - 網は行が実際に作った状態（来歴・縮める前の既定名が上限を超えたか・2 本目を足したか）を `CoverageCounter` で数えて全組を表明する。
- 目標の図で名前を戻すことは、図を変えない行が吸収を確かめるために要る。吸収を外すうえで戻さないと、その行も緑になる。
- mutation 実証済み。

## 網羅の行

- `SyncPlanCoverageRows`：Id は `coverage-` で始める。失敗の範囲は決定的な行と同じ `方言/scenarios`。種の前に流し、単独の種では流さない。
- 決定的な行（静的な規則の合格側・形の行・来歴の行）は網羅の計数に数えないが、網羅の行は数える。
  - 決定的な行は回帰の網で、選び方を行の述語で決める。
  - 網羅の行は変更の種類と差分の選び方の組を実際に流すための行で、選び方を本物の 5 通りの意味のまま強制する。包み `SelectionOverrideCase`、選び方の本体は生成器の `Select` から切り出した `SyncPlanFuzzGenerator.ApplySelection`（引く順と回数は同じなので、選んだ項目まで含む種の指紋は 5 方言とも不変）。
- 形は 2 つ。
  - 種の行：実 DB の種 N の DB と変更をそのまま使い、選び方だけを強制する。選び方は 1 ケースの最後に引く乱数なので DB と変更は種 N と同じ。実 DB でだけ流す。MySQL 17 行・SQL Server 26 行。夜間の種の数で `QUICKER_SYNC_FUZZ_LOG` 付きで流した記録から、欠けを多く埋める種を貪欲に選んだ。種の番号と埋める組は行の表に書く。自由な変数が種で次元表の因子でないので `CoveringArray` は使わない。
  - 手で書いた行：SQL Server の参照アクションの変更。互いに独立した親子 8 組の ON DELETE / ON UPDATE を NO ACTION・CASCADE・SET NULL・SET DEFAULT へ変える形 × 選び方 5 通り。生成器は SQL Server で参照アクションを変えない（`AllowCascade` が偽）。実 DB と網の両方で流す。
- 網羅しない組の一覧に残るのは受け入れ（製品・DB の事実）だけ。
- 行が出すはずの変更の種類と強制した選び方を出さなくなったら、`SyncPlanCoverageRows.MissingKindProblems` が行を名指しして赤にする。生成器を変えて種の指紋が変わったら、指紋と一緒に種の行を選び直す。
- 網羅の照合は普段の既定の種の数でだけ行うので、網羅の行を夜間だけに回してはいけない。普段の実行で一覧に無い欠けになる。
- mutation 実証済み。

## 期待の行と実 DB の段

同期の差分と計画の単体テストと、実 DB の同期の統合テストは、期待の行 `SyncPlanExpectationRows`（`SyncPlanExpectationRows.*.cs`・行は `ExpectationRow`・表は `ScenarioTable`）に寄せてある。

### 行の構成と網での照合

- 行は次を持つ。
  - DB 側の図：`Live`（QuickER の DDL で作る）か `LiveSql`（生の SQL）。
  - 目標の図：`Target` / `TargetFromLive`。
  - 選び方。
  - 行を流す方言（能力）。
- 網（DB を使わない）では `SyncPlanCapabilityNetTestsBase.ExpectationRows_Match` が、差分と計画の期待を面ごとに照合する。面は `diff` / `items` / `sections` / `warnings` / `excluded` / `rebuilds` / `reorders` / `exception`。

### 実 DB の段

- `.OnRealDatabase(r => …)` で実 DB の段を持つ行は、方言ごとの `SyncPlanFuzzTestsBase.ExpectationRows_MatchOnRealDatabase`（5 方言の派生・`SyncPlanRealStage.cs`）が実 DB でも流す。
  - 流れは「DB 側の図を作る → 取込 → 目標の図との差分 → 選び方 → 計画 → 実行 → 取り込み直し」。
  - 目標の図は既定で別の Guid 空間。`PreserveIds(理由)` は例外で、今は使う行が無い。
- 総当たりと同じランナー（`SyncPlanFuzzRunner.RunCaseAsync`）の汎用の表明 1〜5 を照合する。
  - 表明 1：失敗は計画の警告が予告する。
  - 表明 1b：失敗で消えてよい制約だけが消える。
  - 表明 2 / 2b / 3：選んだ項目が消え、新しい差分が出ず、選ばなかった項目が残る。
  - 表明 4：選んだ変更の外の列属性が変わらない。
  - 表明 5：行数と取込の警告。
- 行の実 DB の期待の面は `outcome` / `error` / `script` / `before` / `after` / `afterdiff` / `probe:名前` / `probe-before:名前`。前後が同じことは `Unchanged()` / `ProbeUnchanged()`。
- 既知の失敗の一覧は通さない。違反はすべて赤にする。
- ランナーの計画の文脈には取込の `ColumnDefaults` も渡す（画面と同じ。既定値のある列の型の種類の変更を J2 の警告で名指しする。総当たりの種も同じ文脈で計画する）。
- 実 DB の段の Fact は種を使わないので、夜間の目印を付けずに普段の CI で流し、網羅の計数に数えない（決定的な行と同じ）。

### DB の前提の作り方

- `Prepare`：取込の前の文。
- `LiveSql`：`RealOnly` を含む（網では流さない）。
- `Setup`：取込の後の行の投入。
- `RawSetup`：実行器の検査を通さない文。SQLite で孤児行やビューを作る行が自分で `PRAGMA foreign_keys` を切り替える。
- `Teardown`。
- 管理者の接続：`AdminSetup` / `AdminTeardown` / `AdminQuery`（Oracle の SYSTEM・MySQL の root。使う行は Oracle の 1 行）。
- カタログは探針 `Probe`（`SyncPlanCatalogProbes`＝方言ごとの決まった問い合わせ）と行ごとの `Query` で読む。

### 計画が警告しない失敗

- 計画が警告しない失敗は `ExpectsUnwarnedFailure(理由)` の行でだけ受ける。汎用の表明 1 を外すのはこの行だけで、表明 1b は外さない。
- 理由は「データの都合：」（計画は列の値を見ない）か「モデルの外の物：」（計画が見ないビューなど）で始める。
- `Outcome` に `failed:` と断片を書き、`Unchanged()` で DB が変わらないことを表明する。

### 行の書き方の照合

- `SyncPlanExpectationRows.Table` が表を作るときに `RealStageProblems` が照合する。見る形は次のとおり。
  - 実 DB の段の期待が無い。
  - 手で組む項目の行。
  - `Live` と `LiveSql` のちょうど一方でない。
  - 網で流さない行に網の期待がある。
  - 行の能力に無い方言。
  - 宣言していない探針。
  - 方言に無い探針。
  - `ExpectsUnwarnedFailure` の理由・`Outcome`・`Unchanged()` の欠け。
- 方言ごとの行の数の下限（`MinimumRealRowsFor`＝`Real{方言}RowCount`＋方言をまたぐ行）を、実 DB の Fact の `CoverageGuard.AtLeast` が照合する。
- DDL の注記は `*DdlNoteTests` に残してある。
- mutation 実証済み（src の `SyncPlanner`・`SchemaDiffService`・方言の同期スクリプト・能力と識別子の宣言を書き換えると、移した行がそろって赤になる）。

## 総当たりの表明と運用

### 表明 1b の規則

- `SyncPlanFuzzRunner.FailedSyncConstraintLosses` の規則 1〜5：
  1. 削除を選んだ制約は消えてよい。
  2. 作り直しを選んだ制約は旧か新の形で残る。
  3. 主キーの変更を選んだ表の主キーは旧か新の形で残る。
  4. 表と列の削除で消えてよいのは、その表・列が実際に消えたときだけ。
  5. ほかは残る。
- 候補キーを証明できないと警告した外部キー自身の消失だけは受け入れる。作り直しに重なったものは注 `warned-alter-foreign-key-exempt:{own-failure|collateral}` として数える。
- 違反の文に `(rule n; warned=…; dropped-before-failure=…)` を添える。
- 規則は `SyncPlanFuzzOracleTests`（DB なし・1 規則 1 行の表）が固定する。mutation 実証済み。
- 失敗した種の警告の内訳と、「失敗を予告しない種類の警告しか無いのに失敗した種」を出力する（測るだけ）。

### 照合のまとめ方

- 必須の形の表明と網羅しない組の照合は、普段の既定の種の数が 50 以上の方言だけで行う。夜間に種が増えた PostgreSQL・Oracle では行わない。
- 既知の失敗の照合と必須の形の表明は、両方を集めて 1 つの失敗にする。どちらかの赤がもう一方を隠さないため。
- 夜間に見つけた失敗は `SyncPlanFuzzKnownFailures` に載せる。項目の名前・乱数の項目の数え方・決定的な行は、その一覧のコメントに書く。
- 直した形の項目（`4m-p6-doomed-rebuild`・`candidate-key-re-add-collateral`・`nightly-oracle-100-111`）は一覧から消してあり、行は回帰の網として残る。

### 1 種の例外

- 1 種の処理（生成器・ランナー）の予期しない例外は、その種の失敗（`seed threw:`）として記録して次の種へ進む（`SyncPlanFuzzRunner.RunGuardedAsync`）。取り消しは止める。
- 例外でテストごと止まると、それより前の種の違反が報告されずに隠れるため。
- 固定は `SyncPlanFuzzRunnerGuardTests`（Docker 不要）。
