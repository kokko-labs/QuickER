# GeneratedRuntime（生成コードのランタイムテスト）

`tests/QuickER.Tests.Integration/GeneratedRuntime/` の規則。実装先どうしの一致（`BackendParity*.cs`）、共通基底＋派生の形、双方向同期の検証（同期のモデル・構成の行列・`SyncRuntimeTestsBase`）を扱う。
エンジンの共通の枠組み（種・既知の失敗・網羅の計数・次元表・登録簿）は `tests/QuickER.Tests/TestSupport/CLAUDE.md`「エンジンの核（Engines/）」を参照する。
同期エンジンの規則そのものは `src/QuickER.Runtime.Sync/CLAUDE.md`「ガードテストの一覧」を参照する。

## ランタイムテストの形

- 生成コードのランタイムテストは「共通基底 1＋バックエンド派生 N＋派生固有 Fact」に揃える。
  - バックエンド非依存のシナリオは基底の `[Fact]` が持つ。派生はリポジトリ生成・シード・「他者による更新」の作り方だけを差し込む。
  - 基底は `SaveHookRuntimeTestsBase` / `ConcurrencyRuntimeTestsBase<TEntity, TConflictException>` / `NamedQueryRuntimeTestsBase` / `UniquenessCheckRuntimeTestsBase<TOrder>`。
- 生成物はフィクスチャごとに別 namespace へ出る。`SaveConflictException` も `ConcurrencyMode` も `OrderEntity` も基底からは名指しできない。
  - エンティティ型・競合例外型だけを型引数で受け、値の読み書きは派生のアダプタへ委ねる。VO 有効の図と無効の図が同じシナリオを共有できる。
- バックエンドによって存在しない面（リモートの `Query()`・インメモリの生 SQL）は、条件スキップでなくサブクラス階層で分ける（スキップ 0 の原則）。
  - `NamedQueryRawSqlRuntimeTestsBase` / `UniquenessCheckLocalRuntimeTestsBase<TOrder>`。
- 新しいバックエンドを足すときは派生を 1 つ書くだけで、全共通シナリオが自動的に掛かる。機能×実装の網に穴が開かない。

## 機能 × 実装先の行列

生成コードのランタイムのテストを「機能 × 実装先」の行列として宣言し、照合し、生成物 `runtime-matrix.md`（このフォルダ・手で直さない・eol=lf）として固定する。空欄はバグの保管庫なので、宣言しない限り赤になる網にする。

- 実装先の正本は `RuntimeTarget`（`tests/QuickER.Tests/TestSupport/RuntimeTargets.cs`・WPF 非依存で Integration へ Compile のリンク）の 9 値：`AdoSqlServer`・`AdoSqlite`・`EfCoreSqlServer`・`EfCoreSqlite`・`EfCorePostgreSql`・`EfCoreMySql`・`EfCoreOracle`・`InMemory`・`Remote`。`Ado`＝QuickER 版 Repository（ADO.NET を直接使う生成の実装）。
  - 方言の付いた値の集合は製品の正本と一対一（QuickER 版＝`CodeGenerationOptions.SupportedRepositoryDialects`・EF Core 版＝`DialectNames.All`。`RuntimeTargets.ProductProblems`）。製品が方言を足すと、値を足すまで赤になる。
- 担当の宣言：テストのクラスに `[RuntimeCell(RuntimeFeatures.X, RuntimeTarget.Y)]`（複数可・`Inherited = false`）。機能の名前は `RuntimeFeatures` の定数。
  - リモートの担当は、HTTP クライアントの裏のサーバーの実装先を `Server = RuntimeTarget.Z` か `ServerNone = true`（リポジトリを持たない転送だけのスタブ・HTTP を使わない検査）のちょうど一方で名乗る。リモート以外はどちらも書かない。
- 実装先を 1 つのテストの中で比べるエンジンは属性を手で並べず、エンジンの実装先の軸から導く（`RuntimeMatrixEngines`）。
  - 実装先の一致（`BackendParityRuntimeTests`）：軸のアダプタ `ParityBackendAdapter` が実装先とサーバーを持ち、登録（機能・シナリオの群）の群のシナリオが流れる実装先の和を担当にする。比べる相手として流れるだけの実装先を数えないときは登録の `Engine` で絞る（EF Core の実行戦略）。
  - 簡易 DSL の総当たり（`DslConditionFuzzSqlServerRuntimeTests`）：構成の方言と実装先の軸の名前（`DslFuzzBackends`）から実装先を引く（`RuntimeMatrixEngines.DslTarget`）。
  - 軸を持たないエンジンの派生（`TranslatorParity*`・`BackendParitySqlServerRuntimeTests`）は普通のクラスと同じく属性で書く。
- 担当でないセルは行の表 `RuntimeMatrixRows` にだけ宣言する（担当は書かない）。種類は 3 つ。
  - 空き（`Gap`）：まだ検証していない。直す区切りの名前が必須（決まっていなければ `RuntimeMatrixRows.Trigger`＝きっかけ待ち）。
  - 割り切り（`Accepted`）：意図して検証しない。根拠（`リポジトリの相対パス「見出し」`）が必須で、docs か CLAUDE.md の見出しの実在を照合する。根拠の無い「代表させている」は空きにする。
  - 非該当（`NotApplicable`）：その組み合わせが原理的に成り立たない。
  - リモートの担当のうち、まだ流していないサーバーは `MissingServers`（理由・区切り）に書く（今は楽観排他 × QuickER 版 SQL Server のサーバーの 1 件）。担当のサーバーに現れたら赤になるので消す。
- 照合（`RuntimeTestMatrixTests`）：
  - どの機能 × どの実装先も、担当か宣言のちょうど一方（両方なら「埋まったので宣言を消す」、どちらも無ければ「担当も宣言も無い」で名指し）。実装先を足すと全機能のその列が赤になる。
  - 走査の規約（`RuntimeMatrix` の remarks・このフォルダの名前空間の全部と、`QuickER.Tests.Generated…` の `*RuntimeTests`）に合うクラスは、どれも属性を持つかエンジンの登録にある。担当の機能は行の表にある。リモートのサーバーの名乗り方。
  - 宣言の理由は具体的に書く（`FactorInventoryGuard.IsVagueReason`）。空きは区切り、割り切りは根拠を必ず持ち、ほかの種類は持たない。
  - 成り立つセル（担当・空き・割り切り）のうち担当が過半。非該当は分母に入れない（EF Core を方言ごとに分けると非該当の列が増え、網の質と関係なく割れるため）。
  - 行列の生成物は宣言から作り直した内容と同じ（`RuntimeTestMatrixTests.RuntimeMatrixDrift_MatchesRecord`・`GeneratedTextFile.Verify`）。書き直しは `QUICKER_REGEN_FIXTURES` を立てて `FullyQualifiedName~RuntimeMatrixDrift`（ルートの CLAUDE.md の書き直しの段のコマンドを Integration に向けても書き直される）。
  - 照合の関数の自己検査は `RuntimeTestMatrixTests.Checks_NameEveryProblemShape`（合成の宣言で問題の形を 1 つずつ名指しする）。
- 名前の規則（新しく足すクラス・`RuntimeTestNaming`）：機能と実装先は属性の名前で書き、接尾辞は `*RuntimeTests`。
  - 実装先が 1 つ：機能の名前・実装先の名前の順に並べる（例 `SaveHookInMemoryRuntimeTests`・`TranslatorParityEfCoreSqlServerRuntimeTests`）。リモートは実装先の名前の代わりに Remote とサーバーの実装先の名前を並べ、リポジトリなしは Remote だけにする。
  - 実装先が 2 つ以上かエンジン：機能の名前で始める（例 `BackendParitySqlServerRuntimeTests`・`MultiTargetRepositoryRuntimeTests`）。間は自由。
  - 規則に合わない既存のクラスは既知の一覧 `RuntimeTestNamingKnown`（理由つき）に載せ、`GuardKnownList` で 2 方向に照合する（一覧に無い違反も、無くなった・合うようになったクラスも赤）。改名は後の区切りで行い、改名したら一覧から消す。ガード：`RuntimeTestMatrixTests.NewRuntimeTestClasses_FollowNamingRule`。
- 運用：
  - テストのクラスを足すときは属性を書き、そのセルの空きの宣言を消して生成物を書き直す。例：区切り D3 が TranslatorParity の EF Core の PostgreSQL・MySQL・Oracle の派生を足して `[RuntimeCell(RuntimeFeatures.TranslatorParity, RuntimeTarget.EfCorePostgreSql)]` などを付けると、その列は「空き（区切り D3）」と担当の両方で赤になり、`RuntimeMatrixRows` の宣言を消すと緑になる（続けて生成物を書き直す）。
  - ガード：`RuntimeTestMatrixTests`（`Targets_MatchProductDialects`・`EveryRuntimeTestClass_ShouldAppearInMatrix`・`EveryMatrixCell_ShouldBeCoveredOrDeclaredAsGap`・`Matrix_ShouldBeMostlyCovered`・`RuntimeMatrixDrift_MatchesRecord`・`NewRuntimeTestClasses_FollowNamingRule`・`Checks_NameEveryProblemShape`）。

## 実装先どうしの一致

`tests/QuickER.Tests.Integration/GeneratedRuntime/BackendParity*.cs` が、同じエンティティ型のまま操作の列を複数の実装先へ流して比べる。

### 実装先

- QuickER 版（基準）・EF Core・インメモリ・リモート（サーバーは QuickER 版）・RemoteEfCoreOnly。
- RemoteEfCoreOnly のサーバーは `BackendParityEfCoreOnlyFixture`（EF Core 単独で生成したエンドポイントと EF Core 版 Repository）。
  - 全機能面（Include・一括削除・生 SQL）と除外 blob の Stream は持たない。`ParityBackendKinds.IsRemote` / `HasBlobStream` で流さない。
  - EF Core 単独で生成物が変わる生 SQL のエンティティ取得の変換の枝には届かない。`EfCoreRawSqlValueConversionTests` が単発で持つ。

### 生成器の手

`BackendParityGenerator` と `BackendParityGenerator.Customers.cs`。

- 注文のスロットに加えて顧客のスロットを持つ（根の顧客と、根を指す外部キーで持つ連絡先・紹介先）。
- 手の種類：連絡先の挿入・紹介者の付け替えと外し・連絡先の外部キーの付け替えと外し・参照されていない根の削除・顧客の単数と複数のグラフ保存・UpdateAsync・Full の範囲で顧客を連絡先と紹介先ごと Include して読む（保存しない）。
- 外部キーとナビゲーションが食い違うグラフは乱数の列で作らない。どの実装先も保存の前に断るので、作ると保存の手が断られるだけで後の列が何も比べなくなる。
  - 顧客の保存の直前に `Consistent()` を通し、満たさなければ保存の手を出さない。
  - `Consistent()` の条件：スロットどうしで行を共有しない／どの連絡先も同じ保存の顧客を指す外部キーがちょうど 1 本／根の紹介者が同じ保存の顧客を指さない／外部キーが消えた顧客を指さない。
- 列の末尾にだけ置く引き金の手（`AppendTail`）：
  - 子を持つ親の DeleteAsync。
  - 親の無い行の挿入。
  - 参照されている顧客のグラフ削除（依存行を同じ保存に持ち、編集すれば `DependentHeldParentDelete`）。
  - 代替キー（customers.code）の更新（参照あり `ReferencedKeyUpdate`・なし `AlternateKeyUpdate`）。
  - Save フックの規則：新しい顧客の更新・削除・挿入に Skip / EditThenSkip / EditThenContinue / ThrowBefore / ThrowAfter を当てる（`HookEditsSkippedRow` / `HookEditsDeletedRow`）。
- 列の最後にだけ置く NOT NULL の手（`AppendNullIntoNotNullTail`・受け入れた手の差 `NullIntoNotNull`）：種の 4 分の 1 で、値の幅と同じ別の乱数の列（`ValueSeed`）から、本流の列を引き終えた後に引く。除外 blob の thumb への Stream の SET NULL（Blob の範囲だけ）・thumb が null の注文の挿入・金額が null の注文の挿入（NOT NULL の普通の列）のどれか。DB は断り、インメモリは書く（docs のインメモリの節「実 DB との既知の乖離」）ので、インメモリだけをその手から後の比較から外し、ほかの実装先は `store-rejected` の一致を比べ続ける。決まった形の行 `not-null-stream-set-null-excluded-blob-is-refused`・`not-null-insert-null-thumb-is-refused`・`not-null-insert-null-amount-is-refused` も持つ。
- 決定的な用途だけの手とフックの振る舞い（`AfterWriteBlob`・`AfterExecuteSql`・`EditBlobsThenContinue`）は乱数の生成器に混ぜず、次元表の水準から外す（`AssignKey` と同じ）。
- 値の幅（`BackendParityGenerator.Values.cs`）：本流の乱数の列が引いた値を、別の乱数の列（`ValueSeed`）で低い確率で端の値に置き換える（操作の列は変えない）。日時の Kind（Unspecified・Utc・Local）と端数（7 桁の tick）・decimal の精度の上下限と最小の正の値と 0・blob の空と 4 KB・文字列の非 ASCII とサロゲートペア。一意な列（顧客の名前・code）は置き換えない。比べ方は今のまま（日時は tick・decimal は値）で、Kind と decimal のスケールは比べない（実装先ごとの返し方は docs のインメモリの節「実 DB との既知の乖離」）。

### 直す区切りのある手の差

- `BackendParityKnownDifferences.PendingDeviations` は実装先×手の種類・指摘・直す区切りを持つ。今は空。
- `ParityScenario.CutPendingDeviations`（乱数の列だけ真）のときだけ、その実装先をその手から後の比較から外す（`ParityCut.Pending`）。決まった形のシナリオは打ち切らず、`All` の Pending の項目で実際に赤を照合する。
- 乱数の列の表明：
  - 打ち切りが宣言の一覧（Accepted か Pending）の (実装先・手の種類) にだけ起きること。判定の関数でなく一覧と照らす。
  - Pending を宣言した実装先で Pending の打ち切りが 1 回以上起きること。一覧が空の間は何も確かめない。
- 仕組みは `PendingDeviation_CutsOnlyTheDeclaredBackendInRandomScenarios` が固定する。`ParityScenario.PendingDeclarations` で自前の宣言（EF Core×AlternateKeyUpdate）を渡し、その打ち切りが実際に起きることを表明する。

### Save フックの規則

- `ParityHookRule`（`ParityBackendOptions.HookRules`）は、呼び出し `操作:表/キー` か表の全行に当てる。
- 振る舞いは `ParityHookRecorder` が持つ。QuickER 版・EF Core・インメモリ・リモートのサーバーと EF Core 単独のサーバーのフックが共有する。
- 投げた規則は `Threw:{段}:{呼び出し}` を記録する（`ParityStep.HookThrew`）。

### 比べ方の例外

`ParityStep.SameHookFailure` が次の 2 つだけを同じとみなす。制約違反で終わった保存の比較は等しいことを求めたまま。

- 戻り値：基準が `throws ParityHookException` でフックが投げ、実装先がリモート（Remote・RemoteEfCoreOnly）で `throws store-rejected`（500）かつその手でもフックが投げたとき。
- Before の列：両方の手でフックが投げて終わった保存だけ。基準の列が空でなく実際の列の先頭とちょうど一致し、後ろが基準に無い別々の呼び出しなら同じとみなす。
  - QuickER 版は行ごとに Before→DML→After、EF Core・インメモリは Before を先に全部流す（実装先差分は Save フックの節に書いてある）。
- 固定は `HookFailure_RemoteOutcome_IsMatchedOnlyWhenTheHookThrew` / `HookFailure_BeforeHooks_MatchOnlyAsPrefixOfAHookFailure`。

3 つ目は SQL Server の系統の乱数の保存（下の節）の `BackendParitySqlServerSaveComparison` が持つ。

- 結果の行（先頭）の競合の理由とキーが、基準も実装先も「単独でも競合になる行」（候補）のどれかとその行の役に合う理由の組なら同じとみなす。候補は生成器が引いた行の役だけから決める（Before のフックが飛ばさない行のうち、insertWhenUpdateMissing なしで消えた行＝NotFound と、Optimistic で版の古い行＝Modified）。観測した結果からは決めない（決めると K1 のような偽の競合まで吸い込む）。
- 違う組が両方とも候補に入るのは候補が 2 つ以上ある保存だけなので、例外が効くのはその保存に限られる（候補の数の条件は照らし合わせがあれば冗長で、置かない）。結果の行の後ろ（行ごとの版・保存先）は等しさを求めたまま。
- 理由：どの行の競合を報告するかは文の順で決まり、QuickER 版はグラフ保存の書く順、EF Core は 1 回の SaveChanges の中で自分で決めた順で、最初に当たらなかった行を報告する（docs の EF Core 節）。
- 固定は `BackendParityRuntimeTests.TwoIndependentConflicts_ReportedDifferently_AreTheSame` / `BackendParityRuntimeTests.SingleIndependentConflict_ReportedDifferently_Differs` / `BackendParityRuntimeTests.OutsideTheCandidates_Differs` / `BackendParityRuntimeTests.TwoIndependentConflicts_DifferentState_Differs` / `BackendParityRuntimeTests.InsertWhenMissingAndForceOverwrite_AreNotCandidates`（DB を使わない自己検査。比べ方の例外の自己検査は 3 つとも `BackendParityRuntimeTests` に置く）。mutation 実証済み（候補の照らし合わせを外す・例外のときの後ろの行の比較を外す・飛ばした行を候補から外す条件を外す）。

### SQL Server の系統の決まった形のケースの軸

`BackendParitySqlServerRuntimeTests` のケースのうち、次の 3 つは軸から組み立てる（`Cases()` に流す名前を手で並べない。全組が流れることは `Known_NamesExistingCases` の `CoverageGuard.ContainsAll` が表明する）。

- `alternate-key-{形}-with-{ほかの行}-{扱い}`：代替キーの行の形（plain／taken-from-deleted＝2 つめの文脈／taken-from-released＝同じ保存の更新する行が手放す code を取る。ほかの行は手放す行に置く）× 同じ保存のほかの行（gone／gone-insert-when-missing／gone-insert-when-missing-hooks／stale／hook-skipped）× Optimistic／ForceOverwrite の全積。代替キーを書いた後に、同じ保存のほかの行の理由で試行をやり直す経路（K1）の入力。
- `own-write-{形}-{他者の変更}-{経路}`：SaveChanges が書く列を持たない表の形（tags＝代替キーと版・stamps＝キーと版・markers＝キーだけ）× 読んだ後の他者の変更（fresh／stale／gone）× 保存の経路（UpdateAsync／グラフ保存／insertWhenUpdateMissing のグラフ保存）。キーだけの表の stale は原理的に起こり得ないので外す。表の形ごとに、消えた 11 行を insertWhenUpdateMissing でまとめて保存する `own-write-{形}-gone-graph-save-insert-when-missing-11-rows` も持つ（EF Core 版は 1 試行 1 行ずつ挿入へ切り替え、やり直しの上限に掛からない）。
- `alternate-key-taken-from-deleted-twice-{作り方}`：DbContext のファクトリの作り方（接続文字列で構成した文脈を使い回す＝プールの形／実行時に付けた接続／オプションで渡した接続＝対照）で、2 つめの文脈の保存を 2 回続け、その間に読む。値オブジェクトありの生成コードは EF Core の本物のプールを断るので、破棄を飛ばして同じインスタンスを貸し出し直す派生（`ReusedDbContext`）でプールの観測できる振る舞いを作る。
- `alternate-key-swap-{扱い}`・`alternate-key-new-takes-released-{順}`・`alternate-key-gone-insert-takes-released-{順}`・`alternate-key-part-moved-off-released-{順}`・`alternate-key-gone-part-then-taker-first`（`HandoffCase`）：2 行の code の入れ替え（docs のとおりどの実装先も最初の更新で断る）と、更新する行が手放す code を挿入する行（新しい行・insertWhenUpdateMissing の挿入）が取る形と、手放す code を参照する部品を別の kit へ付け替える形を、渡す順（releaser-first／taker-first。付け替えの取る行は部品を持たせる kit）で流し、他者が消した部品の後ろでどの実装先でも断られる取りの順を 1 つ流す。リモートの EF Core のサーバーでも流す。new-takes-released と gone-insert-takes-released の taker-first は受け入れた差（G3-B）、part-moved-off-released の taker-first は H2-D1、gone-part-then-taker-first は H2-D2 で、基準の値を `ExpectedLines`、EF Core とリモートの値を `ExpectedEfCoreLines` で固定し、既知の違いの一覧 `Known` に docs を出典にした Accepted として載せる（載っていて一致すれば赤）。
- 前の 2 つは、サーバーが EF Core 版 Repository のリモート（ConcurrencyFixture の RemoteServer）でも流し、QuickER 版と比べる（失敗の署名 `remote-efcore-differs`）。リモートでサーバーの DB が断った保存は `RemoteRepositoryException`（500）で届くので、`OutcomeAsync` が `throws store-rejected` にまとめる（`ParityFormat` と同じ扱い）。

### SQL Server の系統の乱数の保存

- `BackendParitySqlServerRuntimeTests.RandomSaves_MatchBaseline`（夜間の目印・登録簿の部分 GeneratedRuntime/ParitySqlServerSaves）：生成器 `BackendParitySqlServerSaveGenerator` が種ごとに kits 3〜6 行の役（Rename / ChangeCode / TakeDeletedCode / Delete / New）・他者の変更・Before のフックの飛ばし・渡す順を乱数で引き、その後に別の乱数の列（`HandoffSeed`）で code の手放しと取り（TakeReleasedCode＝連鎖 / SwapCode / NewTakesReleasedCode＝新しい kit が無ければ 1 行足す）を重ねて、挿入する取る行（新しい kit・insertWhenUpdateMissing の消えた kit）がまだ code を持つ手放す行より前に来たら手放す行の直後へ移し（受け入れた差 G3-B の順を作らない）、グラフ保存 1 回と保存し直しを QuickER 版・EF Core・リモート（EF Core のサーバー）へ流す。保存の引数（4 通り）とフックの有無は種の番号から決める（普段の 8 種で全組）。
- 部品（kit_parts）の書き込みは、kits の行を並べ終えた後に 3 本目の乱数の列（`PartSeed`）で重ねる（本流と `HandoffSeed` の列は変えない）。役は、消す kit のコレクションに残してカスケードで消す・自分の kit のまま更新・単独の削除・別の kit への付け替え・新しい部品（外部キーを書くか、既定値のまま親のキーで埋めさせる）。部品を持たせる既存の kit は、code を変えない kit と後回しの kit（消す kit の code を取る）と消す kit だけで、1 段目で code を手放す kit には持たせない（受け入れた差 H2-D1）。他者が消した部品は、どの実装先でも断られる順を含む保存に置かない（受け入れた差 H2-D2・`BackendParitySqlServerSaveGenerator.RefusedEverywhere`）。比べ方の結果の行と候補は型の名前（KitEntity／KitPartEntity）を持ち、更新・付け替えで他者が消した部品（insertWhenUpdateMissing なし）を NotFound の候補にする。
- 種は `QUICKER_PARITY_SQLSERVER_SAVE_SEEDS`（普段 8・夜間 400）／1 種だけは `QUICKER_PARITY_SQLSERVER_SAVE_SEED`。既知の失敗の一覧は空。夜間を 400 にするのは、部品の因子の組（まれな kit の役を書く先にする付け替えなど）が 200 種では埋まらないため。
- 作らない形は `BackendParitySqlServerSaveNotGenerated.cs` に宣言する（`tests/QuickER.Tests/TestSupport/CLAUDE.md`「作らない形の宣言」）。
- 次元表 `BackendParitySqlServerSaveDimensions`：行ごとに 役 × 他者の変更 × 飛ばし × 同じ保存のほかの行が消えたか × 保存の引数、手放す code を取る役の行の渡す順（手放す行の前か後か）× 手放す行の他者の変更、3 因子の組（役 × ほかの行が消えたか × 保存の引数・役 × 手放す行の他者の変更 × 渡す順）。部品 1 つも 1 ケースで、部品の因子（役・他者の変更・初めの kit・書く先と初めの kit の渡す順・新しい部品の外部キーの書き方・持たせる kit の役と他者の変更と飛ばし）を持ち、kits の行の因子は None（kits の行のケースは部品の因子が None）。3 因子の組に、部品の役 × 持たせる kit の役 × 保存の引数・部品の役 × 初めの kit × 渡す順・部品の役 × 部品の他者の変更 × 保存の引数を足す。網羅しない組は Accepted の Delete × Gone（F4）と、NewTakesReleasedCode × TakerFirst × 手放す行が Fresh / Stale（G3-B）と、部品の他者が消した行の削除（F4）・1 段目で code を手放す kit の部品の単独の削除（H2-D1）・入れ替える kit への他者が消した部品（H2-D2）・飛ばす kit のうち code を変えるか新しい kit を書く先にする部品（H2）（どれも生成器が作らない受け入れた差）。照合は夜間の既定の種の数でだけ行う（普段の 8 種は数えて書くだけで、K1 の組は決まった形のケースが普段の網で持つ）。
- K1 の修正を外すと夜間の種で赤になる（mutation 実証済み）。

### 結果の表

- `BackendParityOutcomeDimensions`（登録簿の部分 GeneratedRuntime/ParityOutcomes）は、基準の記録で Save フックが受けた段（Before / After）× 操作（`SaveOperation` の 3 値。因子の棚卸しの Source）× 当たった規則の振る舞い（After × 飛ばす系）を持つ。Before で投げるのは原理的に起きない組。
- mutation 実証済み（紹介者の付け替え・代替キーの更新・連絡先の customer_code・Save フックの規則の末尾の手・EF Core 単独のサーバーのフック登録・比べ方の条件）。

### 値の表

- `BackendParityValueDimensions`（登録簿の部分 GeneratedRuntime/ParityValues）は、乱数の列の操作が書く値 1 つを 1 ケースとし、範囲 × 日時の Kind × 日時の端数 × decimal の端 × blob の長さ × 文字列の文字を持つ。1 ケースは値の系統を 1 つだけ持つ（ほかは None）。網羅しない組は無く、既定の種の数（200）で全組が出る。
- mutation 実証済み（QuickER 版の SQLite の束縛で、Utc の日時をローカルへ換算する・端数をミリ秒へ切る・decimal を float 経由にする・非 ASCII を ? にする・空の blob を NULL にする、のどれも値の幅があると赤、値の幅を外すと違いが出ない）。

### SQL Server の系統の値の往復

- `BackendParitySqlServerValueRuntimeTests`（登録簿の部分 GeneratedRuntime/ParityValuesSqlServer・Docker）：乱数の操作の列とは別に、値の往復だけを QuickER 版 SQL Server と EF Core 版 SQL Server へ流す。図は `BackendParitySqlServerFixture`（値オブジェクトなし＝範囲の外の値もパラメータと SqlBulkCopy まで届く。DDL は SQL Server が断る SET DEFAULT の 2 本目を NO ACTION にした `BuildForSqlServerDdl` から作る）の表 `value_probes`。
- 書く経路 5 つ（InsertAsync・UpdateAsync・SaveAsync の挿入と更新・BulkInsertAsync）で書き、GetById・Include の無い Query・最後に 1 回の customers からの Include（QuickER 版は FOR JSON）で読む。比べるのは、期待（`BackendParitySqlServerValueExpectations`＝docs の写し）・2 つの実装先どうし・読む経路どうし・断られた書き込みが何も残さないこと（BulkInsert は 1 行でも断られればどの行も無い・更新は元の値のまま）。例外の型と文は比べない（docs の一括追加の節）。除外 blob は生の SELECT で読む。
- 次元表 `BackendParitySqlServerValueDimensions`：実装先 × 書く経路 × 列 × 値の形（列の型に無い形は `isValid`）。網羅しない組は、除外 blob の列と長い blob の形 × 更新の 2 経路の 4 組（除外列は UPDATE の値で書けない）だけ。
- `FixedCases_MatchDocsOnBothBackends`（普段・種なし）が成り立つ組を 1 行ずつ流して全組を出し、網羅しない組を照合する。`RandomSeeds_MatchDocsOnBothBackends`（`NightlyEngine`・`QUICKER_PARITY_SQLSERVER_VALUE_SEEDS` 普段 8・夜間 200）は 1 行に 2〜4 列の値を混ぜ、BulkInsert は 2〜5 行、2 回に 1 回 1 つの値を断られる形（列と形の組から一様に）へ置き換える。網羅しない組の照合は夜間の既定の種の数でだけ行う（普段の 8 種は書く経路 × 断られる形の組が欠ける）。
- 作らない形は `BackendParitySqlServerValueNotGenerated.cs`。乱数の本流（`BackendParityGenerator`）と別の種の式で引くので、本流の種の指紋は変わらない（記録 `BackendParitySqlServerValues`）。
- 時間：決まった形 4 秒前後・普段の 8 種 1〜2 秒・夜間の 200 種 13 秒前後。
- mutation 実証済み（SqlBulkCopy へ渡す値の丸めを外す・切り捨てに戻す・範囲の端で止めない・空白と 0x00 の切りを外す・空白以外や 0x00 以外も切る・属性の Scale を出さない、の各回で赤）。

### 網羅の行

- `BackendParityCoverageRows`：Id は `coverage-`・4 行。乱数の列のテスト `RandomSequences_MatchBaseline` が種の前に流し（単独の種では流さない）、同じ次元表と結果の表で数える。
- 違反は既知の失敗の一覧で外さず、新しい失敗として赤にする。受け入れた手の差の打ち切りは乱数の列と同じに効く。
- 形は 2 つ。
  - 種の行：夜間の範囲の種のシナリオをそのまま流す。生成器を種 1〜2000 で走らせて欠ける組を作る最小の種を選んだ（種 210＝Common×ReferencedKeyUpdate・218＝Blob×CustomerCode・286＝Full×ThrowBefore）。
  - 種の行＋引数の上書き：種 30 の SaveManyOrders に ForceOverwrite を渡す。生成器は SaveMany に引数を渡さない。表が行バージョンを持たないので、確かめるのは 5 つの実装先が同じに受け付けること。版の後勝ちの意味は ConcurrencyFixture の系統が見る。
- シナリオの名前は範囲で終える（Observe が範囲を名前の末尾で読む）。
- 行は出すはずの組を宣言し、出さなければ `BackendParityCoverageRows.MissingTupleProblems` が行を名指しして赤にする。割り当ては Observe と共有の `BackendParityDimensions.Assignments`。生成器を変えて種の指紋が変わったら、指紋と一緒に種を選び直す。
- Mode×OpKind（Full×blob の Stream・Blob×Include／一括削除）は受け入れ。EF Core は除外 blob の Stream を持たず、リモート面は Include と一括削除を持たないため（docs）、混ぜた列は QuickER 版とインメモリでしか流せない。網羅しない組の一覧に残るのはこの 1 項目だけ。
- mutation 実証済み。

### 決まった形のシナリオ

- 次の領域は `BackendParityScenarios.{GraphSave,EfCore,Crud,SaveHook,Blob,Remote}.cs` の決まった形のシナリオと、SQL Server の 2 者比較 `BackendParitySqlServerRuntimeTests` のケース（値は `ExpectedLines` で固定）が持つ。
  - グラフ保存の書く順と Include して読んだグラフの保存。
  - EF Core の実行戦略と 1 行も当たらない文。
  - 直接 CRUD と BulkInsert と一括削除の契約。
  - Save フック。
  - 除外 blob と Stream の契約。
  - リモートの CRUD と保存の応答の書き戻し。
- 値そのもの（行の数・フックの順・戻り値）は期待の手（`ParityExpect`）で書く。
- 図に載らない形（EditModel・名前付きクエリ・有界バイナリ・素の int の主キー・HTTP 固有・rowversion・SQL Server の採番列）は元のクラスに残す。
- 機能 × 実装先の行列（下の「機能 × 実装先の行列」）で、実装先の一致が受け持つセルは、シナリオの群（`RuntimeMatrixEngines.ScenarioGroups`）のシナリオが流れる実装先から導く。群のシナリオが実在することは `RuntimeTestMatrixTests.EveryRuntimeTestClass_ShouldAppearInMatrix` が照合する。
- 削除する行の値を代替キーで取る保存（EF Core が後回しの行を 2 つめの文脈で書く形）は、シナリオ `graph-updated-row-takes-alternate-key-of-deleted-row`・`-with-new-dependent`・`-user-connections`（`ParityScenario.EfCoreUserConnections`＝EF Core の DbContext を利用者が持つ 2 本の接続から交互に作る構成。接続は破棄の後に開くと断る）・`ef-retry-updated-row-takes-alternate-key-of-deleted-row` と SQL Server のケース `alternate-key-taken-from-deleted-kit-*` が 5 実装先（SQL Server は 2 者）の一致で持つ。
- 代替キーでない一意な値の J3 を SQL Server の EF Core で見るのは `GraphSaveEfCoreSqlServerRuntimeTests` だけ（SQL Server のケースの図の一意な列は代替キーだけで、EF Core は代替キーを `HasIndex` で並べないので、`HasIndex` を外す壊し方はケースの図では赤にならない）。

## 双方向同期のテスト

### フィクスチャ

- `SyncFixture`：親子 2 テーブル＋版なし子 `sync_notes`（版あり親を FK 参照する混在トポロジ）＋独立の表 `sync_markers`。親テーブルに除外列 `attachment` を持ち、リモートサービス併用の 2 ファイル構成。

### 決定的なシナリオと Theory

- エンジンの振る舞いの決定的なシナリオは `SyncModelScenarios.Engine*.cs` にある。対象はダウンロード・アップロードの基本・再生の判定・ack・削除伝搬・グラフ保存・後勝ち・競合と ServerWins / LocalWins・外部キー・割り込み・記録の取り消し・洗い替え・blob・フック。
- 同期のモデルの 3 つの Theory（直結・HTTP・実 SQL Server）が、同じ宣言 `SyncModelScenario.Transports` から行を作って流す。
- 直結と HTTP の Theory は `SyncModelScenarioParts` が行の番号の余りで 2 つ・4 つのクラスへ分けて並列に流す。xUnit は同じクラスの行を並列に流さず、HTTP の行を 1 クラスに置くと直列が同期の Theory の時間を決めるため。
  - 分けたクラス（`SyncModelRuntimeTestsPart2`・`SyncModelHttpRuntimeTestsPart2`〜`4`）は転送の軸の目印（`ISyncModelTransportTests`・`MatrixAxisValue`）を持たない。軸の値ごとのクラスは本体の 1 つだけ。
  - 実 SQL Server の Theory はコンテナのコレクションで 1 本ずつ流れるので分けない。
  - 分ける数とその数のクラスは別々の場所にあるので、`SyncModelRuntimeTests.ScenarioParts_CoverEveryScenarioOncePerTransport` が `SyncModelScenarioParts.Names` を呼ぶ MemberData を反射で集め、転送ごとの行の和が流すシナリオとちょうど一致し重ならないことを表明する。mutation 実証済み。
- 実 SQL Server でも流すシナリオ（`AllTransports`）：本物の rowversion の版ガード・`MIN_ACTIVE_ROWVERSION()` の上限のアンカー・TOP のページング・外部キーエラーの分類・実 `varbinary(max)`。
  - SQL Server に固有の事実を期待の手（結果・行・ジャーナル・受理記録・ミラー版の面）で表明し、Theory は「直結と同じ違反になる」を比べる。
  - SQL Server だけに効く壊れ方は実 SQL Server の Theory が、全転送に効く壊れ方は直結の Theory が拾う。mutation 実証済み。

### SyncRuntimeTestsBase に残したもの

エンジンの外の仕組みを確かめる 19 メソッド。

- 並行のロックと割り込み（記録と業務の書き込みの間・バッチの適用中・実行の重なり・別の接続の読み取り）。
- 記録の文の形（複数行の INSERT・id の並び・フックが決めたキー）。
- 全書き込み入口の記録と、生 SQL を記録しないこと。
- 登録簿なしの例外。
- 別の DB の保存のフック。
- NULL 許容の計算列。
- サーバーで NULL になった blob。
- 知らない除外の型。
- 一覧に載らない参照。

転送別の派生は次の 2 つ。

- `SyncSqliteRuntimeTests`：直結・CI 常時。サーバー役も SQLite で版採番と版ガードを代役し、差分ソースを差し替える安全網。
- `SyncHttpRuntimeTests`：HTTP・CI 常時。in-process Kestrel・アンカーの Base64 往復・400 拒否・サーバー不在。

実 SQL Server の固有は次の 2 つだけ。

- `SyncSqlServerRuntimeTests`：NULL を許す一意制約の 2 件（`sync_markers` は SyncFixture にしか無い）と、洗い替えの内容の一致の計測。
- `SyncSqlServerHttpRuntimeTests`：上限の値の HTTP の往復・版の書き戻しと 409 の競合の収集・NULL の一意な値。

そのほかの同期のテスト：

- `SyncConstructionExclusionTests`：構築時除外・登録時 fail-fast。
- `SyncRefreshBenchmarkRuntimeTests`：時間は表明せず `ITestOutputHelper` へ出力し、表明は 3 経路の内容ダイジェスト一致のみ。
- 外部の `ISyncTable` 実装の代役（公開の面だけを転送）はシナリオだけで使う。内部の面を持たない実装の差は不具合ではないため。

### テストのサーバー役

- `SyncTestServer*` は SyncFixture・SyncModelFixture・SyncVoFixture で 1 つにまとめられない。版の付与とガード・差分の SQL は同じ意味でも、生成型（`IRepository<,>`・`EntityBaseCore`・`SaveConflictException`）がフィクスチャごとの名前空間で別の型になるため。
- 共有しているのは、型引数で共有できる採番の規則（`SyncTestVersionSequence`）と版を付ける包み（`SyncTestServerRepository`）だけ。

### 同期のモデル（SyncModelRuntimeTests）

端末 2 台＋サーバー役（SQLite）＋サーバーへ直接書く他者に、乱数の操作の列と、指摘ごとの決定的なシナリオを流すモデルベースのテスト。

- 世界の形（`SyncModelWorldShape`）は種の番号から決め、乱数を引かない。
  - 既存の範囲の種（`SyncModelWorldShape.WideSeedBase`＝120000 より小さい種）・決定的なシナリオ・網羅の行は、端末 1・2 が空から始める世界。
  - 広い世界の種（120000 から）は端末 1〜3 の世界にし、端末 i の最初の形（`ModelPreload`）を `Kinds[(種 / 4 + i) % 4]` で回す（種の組＝余りと独立に回し、モード×ポリシーとは続いた 48 種でどの組にもすべての形が出る）。範囲を分けるので、既存の種の操作の列と種の指紋は変わらない。端末 1・2 の世界の端末の選び方は、端末 3 を足す前の引き方のまま。
  - 端末 3 は列挙の末尾（値 3）に足し、キーは 4000 台・ラベルの印は `r`。
  - 端末を 3 台までにする根拠は `SyncModelWorldShape` の remarks（エンジンが分岐に読むのはその端末の手元の記録とサーバーの行と版だけ。3 台でどの端末にもほかの端末が 2 台そろい、4 台目で新しく読むものは無い）。
- 最初の形（1 歩目の前に端末がどう行を持つか）：
  - None：空から始め、後勝ちの静かな同期 1 回で初期データへそろえる。
  - Refresh：洗い替え（blob を運ぶ）1 回で作る（docs の洗い替えの用途の初回構築）。
  - Journaled：最初の同期の前に Repository 越しに書く（通常のオフライン編集。一部は初期データのキー・一意な値とぶつける）。書き手の操作として履歴へ積む。
  - Untracked：最初の同期の前に Repository を通らずに入れる（記録されない・ミラー版も無い・サーバーに無いキーだけ＝端末のキーの範囲の基点＋800 の帯）。誰の書き込みでもないので履歴へ積まず、消失なしの表明は「無い」を期待する（docs：上がらず削除伝搬で消える）。
  - 中身は 3 本目の乱数（`SyncModelGenerator.PreloadSeed`）だけで引く。追跡しない帯のキーと値は、ほかの書き手の同じキー・別表記の候補にしない。
  - 初期データへそろったかの照合は None・Refresh の端末だけに掛ける。
  - 最初の同期をしない端末（Journaled・Untracked）から見ると、初期データはまだ受け取っていないほかの書き手の内容。その端末がそのキーをサーバーと同じ内容（品目は blob も）で持つまで、版の競合の根拠の判定と消失なしの表明は、初期データをそのキーの書き手に数える。
  - 追跡しない行に端末が初めて書いたら、その行の今の内容と blob をその端末の書き込みとして履歴へ積む（Repository 越しの更新・blob の書き込みは、サーバーへの挿入として全列を送る）。
  - 作らない形（サーバーにあるキーの追跡しない行・ミラー版つきの行の持ち込み・別の端末の DB の写し・端末 4 台以上）は `SyncModelNotGenerated` に宣言する。
- 空振りの守り：広い世界の種が既定の種の数のとき、最初の形ごとに実際に効いたことを `CoverageGuard` で表明する（`SyncModelRun.PreloadEffects`＝洗い替えで作った端末がそろった・初期データのキーへの記録が DuplicateOnServer になった・最初の同期の前の記録が UniqueValueTakenOnServer で断られた・どの書き込みも触れなかった追跡しない行が静止化の後にどの端末にも無い）。
- 3 台目があって初めて起きる順序は、決まった形の行 `three-devices-change-reaches-the-other-two-in-different-orders`（全転送・3 台の世界）が持つ：端末 1 が UploadAsync だけで上げて受理記録を残し、端末 2 がその版を降ろしてから編集して上げ、端末 3 は端末 2 の版だけを降ろす。端末 1 は受理記録のキーに来た端末 2 の版を自分のエコーとみなさずに降ろす（エコーの判定をキーだけにする壊し方で赤）。

- 表明：
  - 止まらない：静止化が、例外・未送信・競合・打ち切りを残さず終わる。
  - 収束：3 者の全表と blob が一致する。
  - 消失なし：最終値は誰かが書いた値。書き手が 1 人で競合も ServerWins の破棄も無いキーはその値。外部キー（`foreign_key_check`）も表明する。
  - 書き手が 2 人のキーの期待は、ポリシー×ぶつかり方の決定的なシナリオが持つ。
- 根拠のない競合：
  - 書き手がその端末だけのキーへの版の競合は、根拠なしとして違反にする。
  - サーバーが書き込みを断った理由の競合（DeleteRefusedOnServer・UniqueValueTakenOnServer・ParentMissingOnServer）と、後勝ち＋ServerWins の決着は、実行の直後のサーバーに根拠が無ければ `3/spurious:{表}:{理由}:{形}` にする（`SyncModelRun.CheckRefusalBasisAsync`）。
  - 根拠はサーバーの状態からだけ取る。DeleteRefused＝参照する行がある／UniqueValueTaken＝同じ一意な値の別の行がある／ParentMissing＝親が無い。
  - 参照する行・持ち主が同じ端末の未送信で付け替わる／値を手放すなら、その表がその実行の範囲にあり、その行がその実行で届かなかったのでない限り数えない。
  - 注入した失敗は根拠にしない。当たったかは形 `after-fault` を分けるのにだけ使う（`SyncModelFaults.ThrowOnServerWrite` が当たった端末・表・キーを控える）。
  - Versioned＋ServerWins が捨てた記録は確かめない。理由で分類せず範囲の未送信をすべて捨てるのが、利用者が頼んだ操作のため。
  - 根拠の違反だけは静止化が失敗しても報告する。各実行の直後に確定し、静止化の後段の表明とは独立のため。
  - 既定の種の数で、理由ごとに「確かめた・根拠あり」が 1 件以上あることを `CoverageGuard` で表明する。
  - 決定的なシナリオは `repro-d2-*`・`reach-after-fault-delete-*`・`reach-after-fault-update-*`。
- 失敗の注入と混ぜ方：
  - 注入の種類：サーバーへの書き込み・取得・blob のコピーの失敗／ローカルの After フックの例外／Before フックのスキップと主キーの書き換え／途中で失敗するストリーム／一意な値の重複による挿入の失敗／キーの無い新しい行に Before フックがキーを決める（HookAssignKey＝int キーの表のグラフ保存の挿入だけ）。
  - あわせてキーだけのスタブ・挿入したときの実体での削除・同じキーでの作り直しも混ぜる。
  - 種を 4 で割った余りで組を分けて混ぜる（余り 0 は失敗の注入なし）。
- 同期の設定と一意な値の形：
  - 種ごとの Mode×Policy は乱数を引かず `seed/4 % 6` で 6 組へ配る。種の組と合わせて 24 種ごとに全積が出る。
  - 同期の操作ごとに PropagateDeletes を乱数で切る（`SyncModelGenerator.PropagateDeletesOffRate`＝0.25）。
  - 書き込みの case 15（`TryUniqueShape`）が、一意な値の付け替えの形を 4 つ作る（`ModelShapeStep`）。品目の code・フォルダーの name・端末とサーバーの他者で作る。
    - release-take：値を空けて別の行が取る。半分は取る側を先に触る。
    - release-take-chain：3 行。
    - delete-take：消した行の値を取る。
    - delete-take-reparent：消した行の値を取って別の親へ付け替え、元の親を消す。
- 2 本目の乱数（`SyncModelGenerator.SideSeed`）：本流が選んだ手のキー・一意な値・blob の大きさ・除外する表を、低い確率で置き換える（本流の乱数の引き方は変えない。置き換えた種は実行の結果が変わるので、後の手も変わる）。
  - 同じキー（`StolenKeyRate`）：フォルダー・品目・イベントの挿入で、ほかの書き手の範囲のキーのうち、ほかの書き手の DB にあって自分の DB に無いものを選ぶ（持ち主の採番はもう越えているので、持ち主の後の挿入は自分の DB で失敗しない）。サーバーで DuplicateOnServer になる。
    - 品目の挿入の既定の code は `SyncModelGenerator.FreshItemCode` が作る。盗んだキーは同じ書き手がもう一度選べる（自分で消した後にまた候補になる）ので、すでに出した code なら通し番号を足す。外すと、同じキーを盗み直した手が、削除して同じ code で入れ直した自分の行とぶつかり、書き手の DB で断られる。
- 書き手の DB で成功する手だけを選ぶ約束のうち、主キーと一意な値（序数）の分は、生成器が手を返す前に `SyncModelGenerator.CheckWriterContract` が手の列を順に当てて確かめる。破れば生成器の誤りとして `gen/writer-contract:{手}:{key|unique}:{表}` で名指しし（`RunSeedAsync` が受けてその種の残りの手を作らない）、書き込みの失敗 `op/failed` と分ける。失敗を期待する手（`ExpectFailure`）は当てない。
  - 一意な値の別表記（`SpellingRate`・`UniqueSpelling`）：端末がフォルダーの name・品目の code を新しく書く手で、Exact（ほかの書き手の値そのもの）・AsciiCase・NonAscii（非 ASCII で始まる新しい値）・NonAsciiCase・TrailingSpace・Empty へ置き換える。自分の DB に序数で同じ値が無いものだけ。サーバーの他者は NonAscii だけ（ほかはサーバー役の照合順序で成否が割れる＝網羅しない組の Accepted）。非 ASCII の大文字小文字の土台として、初期データの 2 番目のフォルダーの name と品目の code は非 ASCII で始める。
  - 大きな blob（`LargePayloadRate`・`LargePayloadBytes`＝ストリームのコピーの区切りの 2 つ分を越える 163,847 バイト）。
  - 2〜3 表の除外（`MultipleExclusionRate`）：本流が何も除外しなかった同期の一部（本流の 1 表の除外は変えない）。
  - 印（`ModelWriteOp.StolenKey`・`Spelling`）は internal で、種の指紋の要約（公開のプロパティ）に載らない。
- 一意な値の同じさはサーバー役が持つ（`ISyncModelServerRole.UniqueValues`）：SQLite の代役は序数、実 SQL Server は末尾の空白を除いて大文字小文字を無視する写し（`SyncModelSqlServerUniqueValues`）。
  - 根拠の判定（`CheckRefusalBasisAsync` の持ち主と値の手放し）と静止化の分類（`IsPendingCycle`・`StuckShape`・`OtherTableHolder`）がこれを使う。序数のままだと、SQL Server が別表記を正しく断った競合を根拠なし（`3/spurious:*:UniqueValueTakenOnServer:no-basis`）にする。
  - 写しが実際の照合順序とずれないよう、SQL Server の世界を作るたびに `VerifyAsync` が一意な列 2 本の照合順序で、生成器の別表記の関数から作った値の組を実 DB の `=` と突き合わせ、食い違えば例外にする。
  - 決まった形の行 `n7s-{ascii-case|non-ascii-case|trailing-space}-{collect|localwins|lww-serverwins}`（全転送）は、期待をサーバー役の同じさで選ぶ手 `WhenServerSameUniqueOp` で書く：同じ値なら UniqueValueTakenOnServer で断られ、Collect と LocalWins は次の同期でも報告し続け、その後の ServerWins の同期で揃う。後勝ち＋ServerWins はその同期で捨てる。別の値なら上がる。
  - 実 SQL Server の乱数の種は、AsciiCase・NonAsciiCase・TrailingSpace の別表記がそれぞれ 1 回以上サーバーに断られたことを `CoverageGuard` で表明する（`SyncModelRun.SpellingRefusals`。同じ値の Exact・Empty はどのサーバー役でも断られ、直結の種の網羅が受け持つ）。
- DuplicateOnServer の決着の中身は決まった形の行が持つ：Collect＝`conflict-duplicate-key-on-server-is-reported`・LocalWins＝`lw-overwrites-duplicate-key-on-server`・ServerWins＝`sw-duplicate-key-on-server-reloads-server-row`（同期＝同じ実行のダウンロードが揃える）と `sw-duplicate-key-on-server-upload-only-reloads-server-row`（単独の UploadAsync＝ServerWins が自分で読み直す）・後勝ち＝`lww-duplicate-key-on-server-overwrites`。モデルは書き手が 2 人のキーの決着を表明しない（報告せずに上書きする壊し方は、乱数の種では結果の表の網羅だけが捕まえる）。
- 表どうしの循環の列（`pinned_item_id`）は乱数の列が書かない（並行の書き手が行の循環を作るのを生成器は防げず、サーバーで消えた循環の行は端末に残る＝docs の既知の割り切り）。洗い替え以外の経路は決まった形の行が持つ：`cycle-pin-to-new-item-uploads-in-rounds`（新しい品目へのピン留めが最初の巡で断られ次の巡で通る）・`cycle-pinned-item-is-read-back-before-folder`（ダウンロードで解決役がピン留めの先を読み直す）・`cycle-serverwins-restores-folder-pinned-to-new-item`（ServerWins の読み直しで同じ）。解決役がフォルダー→品目の辺を辿らない壊し方で 3 行とも赤になる（mutation 実証済み）。
- 次元表 `SyncModelDimensions`：
  - OpFamily に 4 つの形、因子 UniqueTable・PropagateDeletes・Excluded（2 表・3 表の水準 two・three を含む）・Batch・Blobs（後ろ 4 つは SyncOptions の Source）・FaultKind・KeyOrigin（own・stolen・not-insert）・Spelling（UniqueSpelling の値と none）・PayloadSize（small・large・none）。
  - 世界の形の因子 Devices（2・3）・Preload（操作の書き手の最初の形＝`ModelPreload` の値。サーバーの他者は None）。2 台の世界の最初の形が None 以外の組・端末 3 が 2 台の世界にいる組・サーバーの他者が None 以外の組は、定義から起こり得ない組として外す。
  - 3 因子の組 (Policy, OpFamily, PropagateDeletes)・(Actor, OpFamily, UniqueTable)・(Preload, Mode, Policy)。
  - Blobs=False は受け入れの組（乱数は常に blob を運ぶ。docs の「運ぶかは端末ごとに一貫させる」）。
  - サーバーの他者（Actor=Server・Via=None）× 衝突し得る別表記は受け入れの組。
- 実行の結果の表 `SyncModelOutcomeDimensions`（登録簿 SyncModel/Outcomes）：SyncConflictReason と SyncLocalRowChangeReason の全値×Mode×Policy。実行の後の結果だけを数え、乱数を引かない。
- 網羅の行 `SyncModelCoverageRows`（Id は `coverage-`・14 行）：
  - 乱数の種で作れない組を持つ。サーバーの他者の Extras の操作 4 つ（作り直し・失敗する書き込み・キーだけのスタブ・持っていた実体での削除）・グラフ保存の失敗する書き込み。
  - DuplicateOnServer は乱数の種が同じキーの挿入で作る（行を持たない）。
  - 既定の 60 種でたまたま欠ける組も持つ。表を除外したダウンロード・後勝ちの RemovedBeforeDownload・Collect と LocalWins の ReloadedForUniqueValue・Collect と LocalWins と後勝ちの RemovedForUniqueValue（未送信の無い古い写しの形）。
  - 広い世界の既定の種でたまたま欠ける組も持つ：端末 3 のフォルダーの delete-take-reparent（`coverage-device3-delete-take-reparent-folders`・3 台の世界）と、追跡しない行を入れた端末のラベルを除外した同期（`coverage-untracked-device-sync-excluding-labels`・行の入力の `Preloads` で最初の形を宣言する）。
  - 直結の乱数のテスト `RandomSeeds_ConvergeWithoutLoss` が種の前に並列に流す（単独の種では流さない）。操作を 1 歩ずつ次元表で、同期の結果を結果の表で数える。
  - 行は生成器の代わりにモード・ポリシー・種の組を宣言する（例：`coverage-server-delete-stub`）（`SyncModelDimensions.Observe` の多重定義）。行の同期が宣言と違えば構築で断る。
  - 決定的なシナリオ（`SyncModelScenarios`）は別の Theory で流れて網羅の計数に入らないが、網羅の行は数える。行は期待の操作を持たず、次元表に無い操作の型は宣言の誤り。
  - 行の違反は既知の失敗の一覧で外さず、すべて新しい失敗として赤にする。
  - 残る網羅しない組は Blobs=False の受け入れだけ。
  - mutation 実証済み。
- 静止化が終わらないときは、最後の周の打ち切りと競合を分類して署名を細かくする（`SyncModelRun.ClassifyStuckTruncationsAsync`）。
  - 一意な値の回転：`1/no-quiesce:unique-swap:{表}`（docs の既知の割り切り＝受け入れ）。打ち切った行のサーバーの値をローカルで持つ行、その行のサーバーの値をローカルで持つ行…と辿って元の行へ戻る形で、2 行の厳密な入れ替えはその最短の形（判定は `SyncModelRun.UniqueRotation`。回転の途中に未送信の行・古い写しの行が混ざっても同じ）。2 行の入れ替えは打ち切りの分類の最初に、3 行以上の回転はほかの形（端末側の循環を含む）が説明しなかった打ち切りにだけ最後に当てる。2 行に限ると 3 行以上の回転が粗い署名に落ち、最初に当てると端末だけで回した循環を `pending-unique-cycle` から奪う。
  - 値を取る行自身の古い参照が持ち主を残す：`1/no-quiesce:holder-referenced-by-taker:{表}`。
  - 持ち主がサーバーに無く、別の表のローカルの行が持ち主を（推移的に）参照し、その行のサーバーの行が値を取る行の下にある：`1/no-quiesce:holder-referenced-by-other-table:{表}`。引き留める行がどれも送るものを持たなければエンジンが消して抜ける（`src/QuickER.Runtime.Sync/CLAUDE.md`「一意な値の持ち主を新しくする」）ので、この署名が残るのは、エンジンが消さない形だけ。そのうち、参照する行の中にローカルの行がサーバーの行と同じ最新の写しがある形（消すとダウンロードが降ろし直さないので消さない）は `1/no-quiesce:holder-referenced-by-other-table-current-child:{表}` に分ける（`SyncModelRun.HasCurrentReferrer`）。既知の失敗の一覧に Pending（直す区切りは `SyncModelKnownFailures.StuckSyncRemainderFixedIn`）で載せ、決まった形の行 `holder-referenced-by-other-table-current-child-does-not-settle` が受け持つ。
  - 端末側の一意な値の循環：`1/no-quiesce:pending-unique-cycle:{表}`。
  - 回転の場合は回転の行すべての、値を取る行自身の古い参照の場合は打ち切った行と持ち主の行の、別の表の参照の場合は打ち切った行・持ち主・参照する行と、サーバーで値を取る行の下にあるために止まった打ち切りの行の、循環は集まりの行の競合を説明済みにする。
  - どれにも当たらない打ち切りか説明の付かない競合が残れば、粗い署名 `1/no-quiesce:conflicts-or-truncations` もあわせて出す。分類できた分が隠さない。
  - 固定は `swap-folder-names-without-refresh-does-not-settle`・`swap-without-refresh-does-not-settle`・`rotate-three-codes-without-refresh-does-not-settle`（3 行の回転）・`n15-pending-unique-cycle`・`holder-referenced-by-other-table-current-child-does-not-settle`（静止化の失敗の署名は表の名前を残すので表ごとに 1 行）。mutation 実証済み。
  - 別の表の参照の判定が狭い（持ち主がサーバーにある・参照する行のサーバーの行が値を取る行の下に無い・参照する行がサーバーに無い・同じ表の参照、のどれも数えない）ことは、DB を使わない対照 `SyncModelRuntimeTests.OtherTableHolderShape_CountsOnlyTheNarrowShape` が表明する（条件を 1 つずつ外すと赤になる）。一意な値の比べ方をサーバー役のものから序数へ戻すと、持ち主の名前が大文字と末尾の空白だけ違う形（実 SQL Server の比べ方では当たり、序数では当たらない）が赤になる（`StuckShape` も同じ対照で見る）。最新の写しの形を分ける判定（`HasCurrentReferrer`）も同じ対照が見る。
- 既知の失敗の一覧の乱数の項目のうち RegularSeeds / NightlySeeds の項目は、全部の種をまとめて照合して「起きない」も赤にする。種ごとの照合は「載っていない」しか見ないため。
- 乱数の種の照合は、静止化で起きた例外を途中の同期の例外と同じに扱い、表明 2・3 の署名から表の名前を外して行う（`SyncModelRuntimeTests.Family`）。
- 種の数は `QUICKER_SYNC_MODEL_SEEDS`（既定 60）と、広い世界の種の `QUICKER_SYNC_MODEL_WIDE_SEEDS`（既定 48・夜間 240）。広い世界の種は同じテストの中で既存の種の後に流し、種ごとの照合と再現の手順はその範囲の決め方で、既知の失敗の「起きない」の照合は既存の範囲の種だけで、網羅しない組の照合は 2 つの範囲がどちらも既定の種の数のときだけ行う。種の指紋は範囲ごとに別の記録（`SyncModel`・`SyncModelWide`）。時刻と乱数の出どころは種だけ。

### シナリオの操作とモデルの期待

- 決定的なシナリオは、エンジンの事実を期待の操作で表明する（操作は次元表に載せず、乱数の操作の列には混ぜない）
  - `ExpectPayloadOp` / `ExpectNoAckOp`：サーバーが断った書き込みが、行の書き込みで止まり受理記録も blob も残さない（`n6-*`）
  - `ExpectLocalRowChangeOp`：実行が自分から変えた行の一覧（`SyncResult.LocalRowChanges`）に、表・キー・理由が載る（`thrown-serverwins-run-hides-settlement` など）
  - `RefuseJournalRemovalOp`：端末のローカル DB に、1 つのキーの記録の削除を断るトリガーを仕掛ける・外す（記録を捨ててから行を消す順を、途中で失敗させて確かめる＝`holder-referenced-by-other-table-entries-go-before-rows`）
  - `ModelRefreshOp.ExpectScopeRefusal`：範囲外の行が範囲内の行を参照する洗い替えが断られ、端末の DB が変わらない（`42-refresh-refused-by-rows-outside`）
  - `LegacyKeyTextsOp`：以前の版が書いた形のキーのテキストを作る（`25-legacy-journal-key-text` / `25-legacy-ack-key-text`）
  - `RecordJournalOp`：ジャーナルへ直接記録して、プロセスごと落ちて残った記録を作る（`replay-unlanded-delete-entry-is-discarded`）
  - `LeftoverUnsetKeyEntryOp` / `ExpectNoUnsetKeyEntryOp`：キーの決まらない記録が落ちて残った形と、アップロードの判定がそれを消すこと（`e2-leftover-unset-key-entry`）
  - サーバーの他者の操作は、ハーネスの `ApplyOnServerAsync` がサーバー役のリポジトリのグラフ保存での削除も行う（網羅の行のキーだけのスタブ・持っていた実体での削除）
- 例外で終わった後勝ち＋ServerWins の実行でも、モデルは実行前の未送信の記録と突き合わせて捨てた行を推定する（`SyncModelRun.SyncAsync` の注入した失敗の catch から `RecordLastWriteWinsSettlementsAsync`）
  - 推定するのは、記録が消え、送るものがあり、サーバーの値が送った値でなく、ServerWins がローカルを戻したか、サーバーに行の無い upsert か、実行が DiscardedByServerWins と報告した行だけ（書き込みの履歴の最後の値で比べると、blob だけを書いた品目を「削除を書いた」と取り違える）
  - 送るものが無いのは、行ありで削除だけ・行なしで削除なし・行なしで削除ありでもサーバーの版が分からない行（版を持つ表で、削除の記録のどれも版を持たず、実行の前の受理記録も無い。docs の「送る操作の決め方」の表の Gone｜Otherwise＝行はサーバーに届いていない。判定は `SyncModelRun.NeverReachedServer`）。最後の形を推定に入れると、同じ実行のダウンロードが同じキーのほかの書き手の行を降ろしたのを決着と取り違え、根拠のない `3/spurious:*:DeleteRefusedOnServer:no-basis` になる。決まった形の行は `lww-serverwins-unuploaded-insert-delete-on-taken-key-is-not-a-settlement`
- 空の blob の期待（N12）
  - 空の blob を見逃すのは、空でない blob を書いた端末と、消された品目を上げ直した端末（同期の前にサーバーに無く後にある＝挿入の経路・`_recreators`）がすべて、その品目の競合を報告されたか ServerWins に変更を捨てられたときだけ（`_conflictsTo`）
  - blob を書いた後でその品目が削除され作り直されたら、期待値を決めない（`_blobHistoryCut`）
  - 対照：`SyncModelRuntimeTests.EmptyBlobWithoutConflict_IsStillAViolation`（blob を運ばない実行で上げた新しい行の空の blob は違反）・`EmptyBlobOfRecreatedRowWithoutConflict_IsStillAViolation`（blob を運ばない後勝ちで作り直した行）

### HTTP・実 SQL Server の派生

- `SyncModelHttpRuntimeTests`：in-process の Kestrel・生成された HTTP の差分ソースと同期専用のエンドポイント。サーバー側の注入した失敗は 500 で届くので、実行中に失敗が起きたかで見分ける。
- `SyncModelSqlServerRuntimeTests`（Docker）：世界ごとにデータベースを作る実 SQL Server のサーバー役。生成された直結の差分ソース・本物の rowversion。サーバー側の失敗を仕掛けるシナリオと組は流さない。
- どちらも次を確かめる。
  - 決定的なシナリオが直結と同じ違反になること。例外は入口・違反の種類・推定した形へ正規化して比べる（`SyncModelSignatures`）。
  - 乱数の種が既知の失敗にしか当たらないこと。種の数は `QUICKER_SYNC_MODEL_TRANSPORT_SEEDS`（既定 10）と、広い世界の種の `QUICKER_SYNC_MODEL_TRANSPORT_WIDE_SEEDS`（既定 4・夜間 40）。実 SQL Server は、どちらの範囲も種の組の余り 0・1 の種だけを流す。
- SQL Server でだけ起きる既知の失敗は `SyncModelSqlServerRuntimeTests.SqlServerOnly` に置く。
- 既知の失敗は `SyncModelKnownFailures` の厳格な一覧（載っているシナリオはその署名の組どおりに失敗しなければ赤・載っていない失敗も赤）で一時的に外し、直したら消す。受け入れの項目（サーバーでの一意な値の入れ替え 2 本）は、理由に docs の既知の割り切りの文を名指しする。

### 同期の構成の行列

- `SyncConfigMatrixRuntimeTests`（登録簿 SyncModel/Config・`SyncConfigDimensions`・Docker）は、組める組の全積 172 行を流す。軸は次のとおり。
  - 主キーの型：行列の軸 `SyncConfigFixtures`（IntKey＝SyncFixture・VoKey＝SyncVoFixture）。
  - ローカルの登録のキーの有無と、キーなしの登録の同期する表のリポジトリをインスタンスの登録（シングルトン）に置き換えた形（`UnkeyedInstance`）。インスタンスの登録はコンテナの前に作るのでコンテナに登録した Save フックを受け取れず、キーを決めるフックとは組まない。
  - サーバーの差分ソース：直結の keyed / 非 keyed・HTTP の baseAddress / HttpClient ファクトリ。
  - AddGeneratedSyncSupport か分けた登録。
  - キーを決める Before フックの有無。
  - 書き込みの経路（因子 Via）：直接の挿入・グラフ保存・`GraphRemoveParentWithNewChild`・`GraphViaExcludedParent`。
- 流れは「登録 → 解決 → 登録の形の検査 → 新しい行を同期 1 回で上げる → サーバーの新しい行を 1 回で降ろす → 洗い替え 1 回で両方の行がそろう」。
  - 解決はスコープを検査するプロバイダ（`ValidateScopes = true`）の、行ごとのスコープの中で行う（シングルトンがスコープのサービスを掴む登録の形を断る）。
  - 洗い替えの段は登録の形ごとに全消しと流し込みが通ることを見る（今の 2 つのフィクスチャは木の図。表どうしの循環は同期のモデルの決定的な行 `refresh-*-foreign-key-cycle*` が 3 つの Theory で見る。図の形の軸は足していない）。
  - 登録の形（`SyncConfigFixtures.RegistrationProblems`）：ローカルの注文と明細のリポジトリがジャーナル記録デコレータ（構築時に除外した親の表も）／差分ソースが転送経路の型（HTTP は HttpSyncServerSource・直結は DirectSyncSource）／サーバーのリポジトリは素のまま。
  - 2 回目の同期が降ろすのはその 1 行だけ（ミラー版から導いたアンカー）。
  - `GraphRemoveParentWithNewChild` は、新しい親をグラフ保存で書いて同じ流れを通した後に段を 1 つ足す：上げ下げした 2 つの親に、キーの無い新しい子を足して親を消すグラフ保存（単数と複数の SaveAsync）と、新しい子だけを MarkRemoved したグラフ保存を流し、後勝ちの同期 1 回でサーバーから親が消え、ジャーナルに未送信もキーの無い記録も残らないことを見る。IntKey は消す親の下に、サーバーに同じキーの行がある新しい版なしの子（`sync_notes`）も入れ、サーバーのその行が残ることを見る。
  - `GraphViaExcludedParent` は、親の表を `excludeFromSync` に入れた登録（Support・Split の両方）で、同じ流れを同期する子の表（明細）で流す。親はテストがサーバーとローカルへ直接入れ、除外した親の直接の挿入と更新がジャーナルに何も残さないこと、除外した親を書き換えて子を足すグラフ保存が子の表だけを記録すること、子が同期 1 回で上がりローカルに残ることを見る。
  - サーバーの DB は全部の行で共有するので、この 2 つの水準はサーバーに足した補助の行を行の終わりに消す（後の行の最初の同期と洗い替えが、親の無い行を降ろさないようにする）。キーは行ごとに 3 つ（ローカルの新しい行・サーバーの新しい行・補助の親）。
- 既知の失敗は KnownCaseLedger で、今は空。行の由来の指摘は行に残す。
- 網羅しない組の一覧は空。VoKey×HTTP は SyncVoFixture にリモートサービスを足して流す。
- mutation 実証済み。

### 割り込みの点

- 同期・洗い替えの実行の名前つきの時点（`ModelInterruptPoint`）で、実行と別の流れから書き込みを 1 回差し込む。書き手（`SyncModelScenarios.InterruptWriter`）はその端末（ローカルの書き込み）かサーバーの他者（サーバーへの書き込み）で、1 つの割り込みに書き込みを順に並べられる。操作は `ArmInterruptOp`、仕組みは `SyncModelFaults.InterruptAsync`（呼ぶのは SQLite のサーバー役の包み）。
  - 時点：サーバーへの行の書き込みの直前／差分・ページ・キーの取得の直前／取得が 1 行以上を返した後でエンジンがロックを取って適用する前。
  - 別の流れは `ExecutionContext.SuppressFlow` の下の `Task.Run`。同期の抑止の印も書き込みロックのリースも引き継がない。
- 点はどれも、エンジンがローカルの書き込みロックを持たない時点に置く。端末の割り込みの書き込みはデコレータの書き込みロックを待つので、ロックの中に置くと待ち合って止まる（適用の最中・洗い替えには差し込めない）。上限 30 秒で待つのをやめ、`interrupt/deadlock` の違反にする。サーバーの他者の書き込みはローカルのロックを待たないので、洗い替えの取得の前後にも差し込む。
- 割り込みの書き込みは通常の書き込みと同じ経路（`WriteAsync`）で、書き手の操作として履歴へ積む。
- 仕掛けたのに点に来なかった割り込みは `interrupt/not-fired:{点}` の違反にする（黙った空振りの守り）。
- 端末の書き手の行は点 3 × ポリシー 2（Collect・ServerWins）× 前段 4（更新・削除→作り直し・削除・blob の書き込み）× 割り込みの書き込み 3（更新・削除・作り直し）のうち起こり得る 35 行＋フォルダーの `d1-folder-recreate-delete` の 1 行。
  - 外す組は原理的に起こり得ないものだけ：行が無いのに更新・削除する組・行があるのに作り直す組（`SyncModelScenarios.IsPossibleInterruptShape`）と、ServerWins × サーバーへの書き込みの直前（Versioned＋ServerWins はサーバーへ書かない＝`IsPossibleInterruptPoint`）。
  - Collect の行の名前は 4m-D1 からのまま（`interrupt-{点}-{形}`）、ServerWins の行は `-serverwins` を付ける。ServerWins の行はサーバーの他者が別の品目を変え、取得が 1 行以上を返す点にも来るようにする。
  - `interrupt-BeforeServerWrite-delete-recreated`（4m-D1）・`interrupt-BeforeServerWrite-recreate-deleted`（P5-2 の S1）・`interrupt-*-update-blob-written-serverwins`（S2）・`interrupt-*-recreate-deleted-serverwins`（先行の D5）もこの網の行。
- サーバーの書き手の行（`SyncModelScenarios.ServerInterruptCells`）は点 2（取得の直前・取得の後で適用の前）× ポリシー 2 × 前段 3 × 割り込みの書き込み 2（更新・削除）× 実行の種類 2（同期・洗い替え）× 後段 2（次の同期・編集してから同期）× その後の失敗 2（なし・次の取得の失敗）のうち起こり得る 84 行。名前は `interrupt-server-{点}-{前段}-{書き込み}-{後段}[-fetch-fails][-serverwins][-refresh]`。
  - 前段：`parent-after-child-selfref`（端末に無いフォルダー p とその子 c。バッチで c が p より前に来て、親の解決役が p を読み直す）・`parent-after-child-cross-table`（対照：p と p の下の品目。解決役は動かない）・`unique-holder-after-taker`（端末のフォルダー h が名前を手放し、別のフォルダーが取り、h をもう一度更新する。一意な値の持ち主の読み直しが h を読み直す）。
  - 削除の書き込みは、サーバーの外部キー（NO ACTION）が受け付けるよう、子フォルダーを null の親へ・品目を別のフォルダーへ移してから対象の行を消す（解決役が親を見つけられない分岐を踏む）。
  - 外す組：サーバーへの書き込みの直前の点（端末が送るものの無い形なので来ない）と、削除 × 編集してから同期（消した行は端末で編集できない）。`SyncModelScenarios.IsPossibleServerInterrupt`。
  - 次の取得の失敗（`ServerInterruptFault.NextFetchFails`）は、割り込みの書き込みの後でその端末に取得の失敗を仕掛け（`ArmInterruptOp` の `ThenArm`）、割り込まれた同期のバッチの大きさを 2 にする。同期は取得の後の点のバッチ（解決役が読み直した対象の行を古い写しで上書きする）を書いてから、同じ表の次のバッチの取得で例外になる（`ExpectThrowOp`）。この水準は、書き込みの単位のコミットから表のダウンロードの終わりまでの窓を見るので、窓がある組（取得の後の点・更新・同期）にだけある：取得の前の点は割り込みの後に取得したバッチが新しい写しを持ち、削除は解決役が読み直す行が無く、洗い替えは受理記録をバッチのコミットごとに消す。後段の期待は同期の行と同じで、降ろした数は前段で違うので見ない。
  - 後段の期待は期待の操作で表明する（汎用の「消失なし」は、端末の編集が古い受理記録でサーバーの新しい内容を上書きする形を拾えない）。取得の後の点の更新は、次の同期が内容ごと降ろす（downloaded=1）・Collect の編集は ModifiedOnServer の競合。取得の前の点は、次の同期は何も降ろさない・Collect の編集はそのまま上がる。ServerWins の編集は点に依らず捨てられてサーバーの内容に戻る。削除は次の同期の後に対象の行が端末に無い。
  - 洗い替え × `parent-after-child-selfref` × 削除 × 取得の後の点の 2 行（Collect・ServerWins）は、`SyncModelKnownFailures` の厳格な一覧に Pending（きっかけ待ち）で載っている：洗い替えのバッチの古い写しの子が、取得の後にサーバーで消えた親を指し、ローカルの外部キーで例外になる（洗い替えは失敗として止まり、次の洗い替えで取り直せる）。今の挙動を期待の操作で固定しない。署名は種の正規形の項目を作らない（`SeedOnlySignatures`。洗い替えの取得の後に割り込む書き込みは乱数の種の列に無い）。
  - 書き手ごとの行の宣言が起こり得る組をすべて含み、行の同期がそのポリシーで流れ、サーバーの書き手の行の割り込まれる実行が宣言の種類であることは `SyncModelRuntimeTests.InterruptScenarios_CoverEveryPointAndShape` が表明する（外す規則はテストの中で書き下ろす）。
  - 割り込みは SQLite のサーバー役の包みでしか仕掛けられないので、この網の行は直結と HTTP の 2 つの Theory でだけ流れる（下の「実 SQL Server の転送は…」）。
- 割り込みは乱数の操作の列に入れない。生成器は毎歩実際の DB を読んで操作を選ぶので、派生の乱数で割り込みを選んでも既存の種の列が変わる。入れるなら別の種の列にする。
- 実 SQL Server の転送は包みを持たないので、割り込みのシナリオを流さない（`SyncModelScenario.ArmsServerFaults`）。
- mutation 実証済み。
