# GeneratedRuntime（生成コードのランタイムテスト）

`tests/QuickER.Tests.Integration/GeneratedRuntime/` の規則。実装先どうしの一致（`BackendParity*.cs`）、共通基底＋派生の形、双方向同期の検証（同期のモデル・構成の行列・同期のエンジンの決まった形の行・トリガーの表）を扱う。
エンジンの共通の枠組み（種・既知の失敗・網羅の計数・次元表・登録簿）は `tests/QuickER.Tests/TestSupport/CLAUDE.md`「エンジンの核（Engines/）」を参照する。
同期エンジンの規則そのものは `src/QuickER.Runtime.Sync/CLAUDE.md`「ガードテストの一覧」を参照する。

## ランタイムテストの形

- 生成コードのランタイムテストは「共通基底 1＋バックエンド派生 N＋派生固有 Fact」に揃える。
  - バックエンド非依存のシナリオは基底の `[Fact]` が持つ。派生はリポジトリ生成・シード・「他者による更新」の作り方だけを差し込む。
  - 基底は `SaveHookRuntimeTestsBase` / `ConcurrencyRuntimeTestsBase<TEntity, TConflictException>` / `NamedQueryRuntimeTestsBase` / `UniquenessCheckRuntimeTestsBase<TOrder>`。
  - 楽観排他のリモートの派生は中間基底 `ConcurrencyRemoteRuntimeTestsBase<TEntity, TConflictException>` を通す。Kestrel とクライアントの生成・破棄を 1 か所に持つ。版は応答の列の値だけが運ぶので、基底の版の書き戻しのシナリオ（テスト 1・10・11）がそのまま列の値の経路の守りになる（`src/QuickER.Runtime.AspNetCore/CLAUDE.md`「保存の応答は列の値を運び位置で突き合わせる」）。
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
  - 簡易 DSL の総当たり（`NamedQueryDslConditionFuzzSqlServerRuntimeTests`）：構成の方言と実装先の軸の名前（`DslFuzzBackends`）から実装先を引く（`RuntimeMatrixEngines.DslTarget`）。
  - 軸を持たないエンジンの派生（`TranslatorParity*`・`BackendParitySqlServerRuntimeTests`）は普通のクラスと同じく属性で書く。
- 担当でないセルは行の表 `RuntimeMatrixRows` にだけ宣言する（担当は書かない）。種類は 3 つ。
  - 空き（`Gap`）：まだ検証していない。直す区切りの名前が必須（決まっていなければ `RuntimeMatrixRows.Trigger`＝きっかけ待ち）。
  - 割り切り（`Accepted`）：意図して検証しない。根拠（`リポジトリの相対パス「見出し」`）が必須で、docs か CLAUDE.md の見出しの実在を照合する。根拠の無い「代表させている」は空きにする。
  - 非該当（`NotApplicable`）：その組み合わせが原理的に成り立たない。
  - リモートの担当のうち、まだ流していないサーバーは `MissingServers`（理由・区切り）に書く（今は 1 件＝CrudParity の QuickER 版 SQL Server。CRUD の照合のシナリオの群は RemoteServiceFixture〔SQLite 方言だけ〕に固定のため）。担当のサーバーに現れたら赤になるので消す。
- 照合（`RuntimeTestMatrixTests`）：
  - どの機能 × どの実装先も、担当か宣言のちょうど一方（両方なら「埋まったので宣言を消す」、どちらも無ければ「担当も宣言も無い」で名指し）。実装先を足すと全機能のその列が赤になる。
  - 走査の規約（`RuntimeMatrix` の remarks・このフォルダの名前空間の全部と、`QuickER.Tests.Generated…` の `*RuntimeTests`）に合うクラスは、どれも属性を持つかエンジンの登録にある。担当の機能は行の表にある。リモートのサーバーの名乗り方。
  - 宣言の理由は具体的に書く（`FactorInventoryGuard.IsVagueReason`）。空きは区切り、割り切りは根拠を必ず持ち、ほかの種類は持たない。
  - 成り立つセル（担当・空き・割り切り）のうち担当が過半。非該当は分母に入れない（EF Core を方言ごとに分けると非該当の列が増え、網の質と関係なく割れるため）。
  - 行列の生成物は宣言から作り直した内容と同じ（`RuntimeTestMatrixTests.RuntimeMatrixDrift_MatchesRecord`・`GeneratedTextFile.Verify`）。書き直しは `QUICKER_REGEN_FIXTURES` を立てて `FullyQualifiedName~RuntimeMatrixDrift`（ルートの CLAUDE.md の書き直しの段のコマンドを Integration に向けても書き直される）。
  - 照合の関数の自己検査は `RuntimeTestMatrixTests.Checks_NameEveryProblemShape`（合成の宣言で問題の形を 1 つずつ名指しする）。
- 名前の規則（`RuntimeTestNaming`）：機能と実装先は属性の名前で書き、接尾辞は `*RuntimeTests`。
  - 実装先が 1 つ：機能の名前・実装先の名前の順に並べる（例 `SaveHookInMemoryRuntimeTests`・`TranslatorParityEfCoreSqlServerRuntimeTests`）。リモートは実装先の名前の代わりに Remote とサーバーの実装先の名前を並べ、リポジトリなしは Remote だけにする。
  - 同じ機能 × 実装先を担当するクラスが 2 本以上あるときは、実装先の名前と接尾辞の間に区別の語を挟む（例 `SaveHookAdoSqliteBinaryWriteRuntimeTests`・`RemoteTransportRemoteServerEngineRuntimeTests`）。区別の語は大文字で始め、実装先の名前（`RuntimeTarget` の値の名前）で始めない（リポジトリなしのクラスがサーバーを名乗る名前や、別の実装先を担当する名前に読めないようにする）。区別の語は名前がぶつかるときだけ使い、組の主（ほかの実装先に同じ基底の兄弟があるクラス）は区別なしにする。
  - 実装先が 2 つ以上かエンジン：機能の名前で始める（例 `BackendParitySqlServerRuntimeTests`・`MultiTargetRepositoryRuntimeTests`）。間は自由。
  - 規則に合わない既存のクラスは既知の一覧 `RuntimeTestNamingKnown`（理由つき）に載せ、`GuardKnownList` で 2 方向に照合する（一覧に無い違反も、無くなった・合うようになったクラスも赤）。今の一覧は双方向同期のクラス 14 本だけで、どの行も理由は「名乗る機能と実装先の名前へ、同期のテストの構成と一緒に付け直すまで残す」。改名したら一覧から消す。基底クラス（`*RuntimeTestsBase`）は抽象で走査の外なので規則を掛けない。ガード：`RuntimeTestMatrixTests.NewRuntimeTestClasses_FollowNamingRule`。
- 運用：
  - テストのクラスを足すときは属性を書き、そのセルの空きの宣言を消して生成物を書き直す。空きの宣言を残したまま属性を付けると、その列は空きと担当の両方で赤になり、`RuntimeMatrixRows` の宣言を消すと緑になる（続けて生成物を書き直す）。
  - ガード：`RuntimeTestMatrixTests`（`Targets_MatchProductDialects`・`EveryRuntimeTestClass_ShouldAppearInMatrix`・`EveryMatrixCell_ShouldBeCoveredOrDeclaredAsGap`・`Matrix_ShouldBeMostlyCovered`・`RuntimeMatrixDrift_MatchesRecord`・`NewRuntimeTestClasses_FollowNamingRule`・`Checks_NameEveryProblemShape`）。

## 述語の表の EF Core の派生

`TranslatorParityEfCoreRuntimeTestsBase` は、述語の表（`TranslatorParityPredicateRows`）を EF Core 版 Repository の 5 方言へ流す共通基底。

- 生成物は束縛（`TranslatorParityEfCoreBinding`）で差し込む。エンティティとリポジトリの型を名前で引き、リポジトリを反射で呼ぶ（生成物の型は名前空間ごとに別で、基底から名指しできないため）。
  - SQL Server・SQLite の派生：チェックイン済みのフィクスチャ（`TranslatorParityEfCoreFixture`＝SQL Server 方言で生成）。SQLite の型マッパーで生成した物と Fluent が同じ（違いは `[SqlColumnType]` の有無だけ）。
  - PostgreSQL・MySQL・Oracle の派生：述語の表の図を方言切替（`DiagramTypeConverter`）でその方言へ写し、その方言の型マッパーで実行時に生成・コンパイルした物（`TranslatorParityEfCoreGenerated`・方言ごとにプロセスで 1 回）。利用者が方言を切り替えて生成したときのコードそのものを流す（PostgreSQL は日時の列に `HasColumnType` が付き、MySQL は `at_offset` が DateTime になる）。テンプレートと型マッパーの変化は、チェックイン済みのフィクスチャの再生成を待たずに同じコミットで表に流れる。
  - 実行時の生成の図から外すもの：rowversion の表（どの方言にも写せず、表のどの行も使わない）と、方言切替が写せない列（Oracle の `clock`＝Oracle の型マッパーに TimeSpan に写す型が無い）。
- 派生の宣言は 3 つ。
  - `Known`：EF Core の答えが C# と割れる行（docs の割り切りを出典にする。一致したら赤）。
  - `NotApplicable`：生成物にその行の列が無いか型が違うので流せない行（製品の事実）。付け替えられない行の集合とちょうど同じでなければ赤（`NotApplicable_IsExactlyTheRowsThatCannotBeRetargeted`）。
  - `SeedValue`：シードの値を方言のプロバイダの規則に合わせる差し込み口（PostgreSQL は DateTimeOffset を UTC へ・MySQL は `at_offset` の日時の部分）。
- 列の側の符号だけの変換の行（`int-column-sign-cast-*`・`in-uint-*-sign-cast-column`・`*-ulong-cast-greater`・区切り a の XT4 の札）は、EF Core 版では C# のように負の値を折り返さないので `Known` で受ける（指摘 `efcore-narrowing-column-cast`・Pending・きっかけ待ち。理由の後ろの共通の注記は基底の `ColumnCastFindingNote`）。5 方言とも符号だけの変換の 4 行は例外にならずに黙って割れ、ulong への変換の 2 行は SQLite だけが UInt64 の大小の比較を翻訳できずに `InvalidOperationException` で断り（プロバイダの文書の Query limitations）、ほかの 4 方言は黙って割れる（2026-11-27 の Docker の実行で確かめた。MySQL の 64 ビットの折り返しも起きない）。
- 述語の表の行の入力は、述語（probes・small_child・big_parent・整数部の大きい probes）のほかに、並び替え（`OrderedProbePredicate`＝述語で絞った probes をキーで並べ、同じキーは probe_id の昇順。並びまで C# の OrderBy・ThenBy と比べる）を持つ。QuickER 版の基底は `ShouldOrderLikeCSharpAsync`、EF Core 版の基底は束縛の `QueryOrderedAsync` で流す（NULL 許容の bool の列の `.Value` の行＝3 回目の効果測定の XT5）。
- バイナリの行の期待は、SQL Server の派生だけ長さを宣言した列（`BinaryColumnLength`＝`binary(4)` の bin_fixed と `varbinary(4)` の bin）と比べる値を、その列へ保存したときの値に直して作る（`TranslatorParityRuntimeTestsBase.SqlServerBinaryColumnLengths`。可変長の列は超えた分が 0x00 だけの値を切るだけ＝XB4）。
- 述語の表の QuickER 版の基底（`TranslatorParityRuntimeTestsBase`）は、`Known`（probes の行で C# と割れる行）のほかに `KnownRefusals`（翻訳器が `NotSupportedException` で断ると分かっている行・どの入力の表の行でもよい・断らなければ赤）を持つ。今は SQL Server と SQLite の派生が、列の側の ulong への変換の 2 行（docs の既知の割り切り）を載せる。インメモリは断らずに C# と比べる。

## 値の束縛の生成の表

`TranslatorParityBindingRows`（登録簿の部分 `GeneratedRuntime/TranslatorParityBindings`）は、比べる値の束縛を、分岐の軸の台帳の値の束縛の標準の軸（`BranchAxisLedgerEntries` の value-binding）と 1:1 の因子で組んだ生成の表。述語の表の基底 2 つ（`TranslatorParityRuntimeTestsBase`・`TranslatorParityEfCoreRuntimeTestsBase`）の Theory `Binding_MatchesCSharp` が、述語の表の派生 8 つ（QuickER 版の SQL Server・SQLite・インメモリと EF Core 版の 5 方言）で流す。

- 因子と水準：列の型（date・datetime2・datetime・smalldatetime・int・bigint・decimal・bit＝述語の表の図の列。bit は NULL 許容の bool の probes.flag）・列の側の形（素の列・値を保つ変換＝int → long・bigint → decimal・日付部品＝日時の列の `.Value.Date`〔NULL の行は `!= null` で外す〕・値を変える明示の変換＝int → byte・bigint → short・decimal → int）・比べ方（==・!=・<・>=・<=・>・IN・NOT IN）・値の数（単一・List・配列）・値の池（値の池の名前そのもの＝`tests/QuickER.Tests/TestSupport/CLAUDE.md`「値の池（ValuePools/）」。整数の値の decimal と、縮める変換の元の値の 3 つを含む）・Kind（Unspecified・Utc・Local・日時でない値は none）・値の形（捕捉した変数・定数・メソッド呼び出し・ラムダを含む式＝単一の値は `Select(p => p).First()`・リスト・配列は `Select(p => p).ToList()`・`ToArray()`・値の側の明示の変換＝double の値を int・long へ・char の暗黙の変換＝捕捉した char を整数の列と比べる・捕捉した `ImmutableArray<T>` のインスタンスの `Contains`）・経路（Query・ExecuteDelete・EF Core 版の QuickErDbContext の ExecuteUpdate）。
  - 区切り a のフェーズ 2 で足した水準（日付部品・値を変える明示の変換〔列の側・値の側〕・<=・>・整数の値の decimal・縮める変換の元の値・Utc・Local・ラムダを含む式）と必ず含む行は、分岐の軸の台帳の項目の札（`RowTag.Ledger`）を持つ。
  - 区切り a のフェーズ 4 で足した水準（列の型 bit と値の池の bool-true・bool-false＝XT5、値の形 implicit-char＝XB3、値の形 immutable-array＝HA3）は、低の指摘の札（`RowTag.Reason` の `ReviewFinding`）を持つ。
  - 起こり得ない組（`isValid`）：値の池・Kind・列の側と値の側の形が決める列の型の系統（日時・数・bool）の食い違い（系統の違う列と値は C# で比べられない・日付部品は日時の列だけ・縮める変換と char の暗黙の変換は数の列だけ）・bool の順序の比べ方・char の暗黙の変換の int・bigint の列の外・行に当たる整数の外・リスト・縮める変換の列との組・ImmutableArray の List の IN の外・decimal の列の値を保つ変換・単一の値と IN（リストと演算子）・縮める変換の元の値の池と縮める変換の有無の食い違い（池は縮める変換の元の値という定義）。
  - 3 因子の組は (列の型, 列の側, 値の池)・(列の型, 値の数, 値の池)・(列の型, Kind, 値の池)・(列の型, 値の形, 値の池)。網羅の配列（`CoveringArray`・種は固定）と必ず含む行で 273 行（ExecuteUpdate の行は EF Core 版だけが流すので、QuickER 版とインメモリは 186 行。フェーズ 4 の水準を足す前は 218 行・156 行）。全積はフェーズ 2 の水準で 21,480 行（フェーズ 1 の水準だけなら 2,808 行。フェーズ 4 の水準を足した後は数え直していない）。
  - 行の Id は水準の名前から作る（`vb-{列の型}-{列の側}-{比べ方}-{値の数}-{値の池}-{Kind}-{値の形}-{経路}`）。比べる値と列の型が違うときは、C# の 2 項の昇格の写しで片側を変換する（水準「素の列」はこの暗黙の変換を含む。byte・short は int へ昇格する）。
  - 必ず含む行は、分岐の軸の台帳の ha1・ha2・ha4・xb1・xt4 の指摘のセル（3 回目の効果測定の再現の入力）と、隣の行の 14 本。隣の行は、網羅の配列が持たない ha1（ExecuteDelete）・ha4（単一の値）の組、日付部品 × 値の数 × 値の形の組を QuickER 版が流す経路にそろえる xb1 の隣（配列 × メソッド呼び出し）、今は緑の IN の並びの要素の値の側の変換を見張る xt4 の隣。作らない形（QuickER 版の ExecuteUpdate・負の値を持つ列 × 符号だけの変換〔値の池の正本の区切り。それまでは手書きの述語の表の行が持つ〕・捕捉したコレクションの集計で作る単一の値・.Date 以外の日付部品）は `TranslatorParityBindingRowsNotGenerated` に宣言する。
- 判定：同じ実装先から読み戻した全行へ述語を当てた C# の評価との一致（例外は不一致）。Query は当たる行のキー、ExecuteDelete は消した数と残った行のキー（C# の補集合）、ExecuteUpdate は WHERE が当てた行の数（SET は列を自分自身へ代入して値を変えない）を比べる。結果は 1 行の記録 `BindingRunOutcome`（一致したか・例外の型と文・束縛したパラメータ・コマンドの文）に残し、テストの出力へ述語と一緒に書く。
- 既知の失敗は `TranslatorParityBindingKnownFailures`（範囲は `{実装先}/binding`・署名は行の Id・行ごとにその行の項目だけで厳密に照合する）。どの行が割れるかは、docs の割り切りか直す前の製品の振る舞いを C# へ写した変え方（DB が受け取った値・変換を外した式）で、同じシードへ当てた C# の答えが変わる行として決める（行を手で並べない）。値に依らず必ず例外になる形は形だけで決める。実装先ごとの規則の並びの前の規則が先に行を取る。
  - EF Core 版の SQL Server（Accepted）：datetime・smalldatetime の列と比べる定数（EF Core は行に依らないメソッド呼び出しの値も定数として SQL に書く）と ExecuteUpdate の WHERE（日付部品と比べるパラメータも元の列の型で送られる）は列の型で送られ、列の刻みへ丸めて比べる・範囲の外は断られる。date の列と日付部品と比べる定数は date のリテラルになり時刻が落ちる。
  - EF Core 版の SQLite（Accepted）：ExecuteUpdate の WHERE の整数と decimal の等値・IN は書き換えられず文字列で比べる（書き換えは Query() の条件だけ）。
  - EF Core 版の 5 方言（Pending・きっかけ待ち）：`efcore-char-value-sent-as-text`。整数の列と比べる捕捉した char の値を、EF Core が整数へ広げずに char のまま（文字列として）送る（区切り a のフェーズ 4 の値の形 implicit-char で見つけた）。当てる行は方言の扱いの規則で決める（`CharValueSentAsText`）：SQLite は ExecuteUpdate の WHERE で TEXT と比べて黙って割れる行、PostgreSQL は ExecuteUpdate（char を integer のパラメータに書けない）と列の側に値を保つ変換を書いた形（22P02）、SQL Server・Oracle はその変換の形（文字列を数へ変換できない）、MySQL はその変換の形の書き込みの文（厳格のモードの例外）と、Query() で値を 0 として比べて答えが変わる行（黙って割れる。今のシードでは起きない）。docs の EF Core 版の Known limitation に回避（`(int)c` か int の変数）を書いた。
  - EF Core 版の PostgreSQL（Accepted）：date の列と比べる定数は date のリテラルになり時刻が落ちる。timestamp の列と比べる Kind=Utc の DateTime は、ExecuteUpdate の WHERE（インターセプターが値を変えない・docs の EF Core モードの PostgreSQL の列型の項）と定数・メソッド呼び出しの値（EF Core が書くリテラルを Npgsql が作れない・同じ項と Known limitation）で Npgsql が断る。パラメータで送る Query・ExecuteDelete はインターセプターが Kind を外して送るので割れない（ha1・区切り a で直した）。timestamp の列と比べる定数のリスト・配列は Pending（きっかけ待ち）。
  - 台帳の 5 項目（ha1＝PostgreSQL の EF Core 版の Kind=Utc の値・ha2＝PostgreSQL の EF Core 版の date の列へのリスト・ha4＝SQLite の EF Core 版のラムダを含む値の式・xb1・xt4）と台帳の外の `quicker-in-over-date-part` は区切り a のフェーズ 3 で直し、規則を消した。
  - QuickER 版の SQLite・SQL Server（Accepted）：`quicker-narrowing-column-cast`。列の側の値を変える明示の変換は、翻訳器が変換を名指しした NotSupportedException で断る（docs/code-generation.md の Known limitation。形だけで決める）。
  - 台帳の外の項目（フェーズ 2 の水準で見つけた）：`efcore-narrowing-column-cast`（Pending・きっかけ待ち。EF Core 版の列の側の値を変える明示の変換：SQLite は `CAST(列 AS INTEGER)`、MySQL は変換を書かずに黙って割れ、PostgreSQL・Oracle は bigint の列の範囲の外の行のある表で必ず例外、SQL Server もほとんどの形で例外。符号だけの変換〔フェーズ 3 の述語の表の行〕はどの方言も折り返さずに黙って割れる。項目の理由に実装先ごとの振る舞いを書き、docs/code-generation.md の EF Core 版の Known limitation に回避〔列は変換せず値の側を広げる〕を書いた）。
  - 項目の実在と範囲は `BindingKnown_NamesRowsTheTargetRuns` が見る。
- 分岐の軸の台帳の指摘のセル：結び済みの項目の指摘のセルの行を、述語の表の基底 2 つの Fact `LedgerCells_FollowStatus` が範囲の実装先の派生で流し、`TranslatorParityBindingEvidence.CellProblems` が照合する（規則は形の行の `SyncPlanBranchEvidence.NetProblems` と同じ：セルが範囲の実装先で流れなければ赤・未修正はセルの割れが項目の Pending〔指摘の名前＝項目の Id・直す区切り＝項目の直す区切り〕に当たり、C# と一致したら赤・修正済みはセルで証拠が出る）。証拠は `TranslatorParityBindingEvidence.ByName`（行と結果から判定）：`date-part-value-bound-as-datetime2`（xb1）・`narrowing-cast-honoured`（xt4。値の側は C# と一致し捕まえたパラメータが変換した値・列の側は変換を名指しした NotSupportedException）・`utc-date-parameter-sent-without-kind`（ha1）・`date-list-sent-as-timestamp-array`（ha2）・`row-free-lambda-rewritten`（ha4。コマンドの文が文字列の比較でない）。自己検査は `BranchAxisLedgerTests.BindingCellProblems_FollowStatus`。
- パラメータの捕まえ方（製品に観測の口を足さない）：SQL Server の QuickER 版は SqlClient の診断の通知（`WriteCommandBefore`）、EF Core 版は派生が DbContext の構成に足すテストの側のインターセプター（`BindingParameterCapture.EfCoreInterceptor`・コマンドを流した後に読む）。パラメータと一緒にコマンドの文も集める。どちらも `AsyncLocal` の範囲の中のコマンドだけを集め、並列の流れが混ざらないことは `BindingCapture_KeepsParallelRunsApart` が見る。SQLite の QuickER 版とインメモリは捕まえない。
- 手書きの述語の表（`TranslatorParityPredicateRows`）は残す。重なる行（`date-*-time-of-day`・`legacy-*`・`small-*`・`in-decimal-*`・`in-uint-list-beyond-int` など）は移さない（移すのは値の池の正本の区切り）。
- 札の対象の表（`RowBaselineTables`）に水準と必ず含む行を載せ、台帳に結べる表（`BranchAxisLedgerTests` の結べる表）に実装先の一覧（`RuntimeTarget` の名前 8 つ）と水準が出る実装先の関数（`TranslatorParityBindingRows.CreatesLevels`＝ExecuteUpdate の水準は EF Core 版だけ）と証拠の名前を載せる。

## 簡易 DSL の総当たりの EF Core だけの方言

- `NamedQueryDslConditionFuzz{PostgreSql,MySql,Oracle}RuntimeTests` は、簡易 DSL の総当たりを実コンテナの EF Core 版 Repository で流す（手順は `DslConditionFuzzEfCore` の 1 か所・SQL Server の派生と同じ）。QuickER 版 Repository はこの 3 方言を生成せず、インメモリは方言に依らないので、実装先の軸は EF Core だけ（ほかは理由つきの除外）。
- 文字列の扱いは `DslCaseRule` の方言の値（docs の「大文字小文字・空白・日付部品」の方言の行の写し）、NULL の位置は PostgreSQL・Oracle が昇順で末尾。種は普段 1・夜間 16。
- 実装先は単位ごとにコンテナの 1 つのスキーマを空にして DDL を当てるので、`DslFuzzBackend.Exclusive` で開いてから閉じるまでを 1 本にする（単位の生成・コンパイルは並ぶ。流し方は `tests/QuickER.Tests/TestSupport/CLAUDE.md`「簡易 DSL の総当たり」）。SQL Server の派生は単位・実装先ごとにデータベースを作って捨てるので排他を持たず、単位ごと並ぶ。
- 行列の担当は軸から導く（`RuntimeMatrixEngines` の `DslFuzz` の登録）。値オブジェクトの文字列一致（`ValueObjectStringMatch`）の 3 方言は `ValueObjectStringMatchEfCore{PostgreSql,MySql,Oracle}RuntimeTests`（クエリフィクスチャの生成物を、方言切替で写した DDL のスキーマで流す。図の列は int・nvarchar・decimal だけで、生成の Fluent は方言に依らない）。

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

`BackendParitySqlServerRuntimeTests` のケースのうち、次の 5 系統は軸から組み立てる（`Cases()` に流す名前を手で並べない。全組が流れることは `Known_NamesExistingCases` の `CoverageGuard.ContainsAll` が表明する）。

- `alternate-key-{形}-with-{ほかの行}-{扱い}`：代替キーの行の形（plain／taken-from-deleted＝2 つめの文脈／taken-from-released＝同じ保存の更新する行が手放す code を取る。ほかの行は手放す行に置く）× 同じ保存のほかの行（gone／gone-insert-when-missing／gone-insert-when-missing-hooks／stale／hook-skipped）× Optimistic／ForceOverwrite の全積。代替キーを書いた後に、同じ保存のほかの行の理由で試行をやり直す経路（K1）の入力。
- `own-write-{形}-{他者の変更}-{経路}`：SaveChanges が書く列を持たない表の形（tags＝代替キーと版・stamps＝キーと版・markers＝キーだけ）× 読んだ後の他者の変更（fresh／stale／gone）× 保存の経路（UpdateAsync／グラフ保存／insertWhenUpdateMissing のグラフ保存）。キーだけの表の stale は原理的に起こり得ないので外す。表の形ごとに、消えた 11 行を insertWhenUpdateMissing でまとめて保存する `own-write-{形}-gone-graph-save-insert-when-missing-11-rows` も持つ（EF Core 版は 1 試行 1 行ずつ挿入へ切り替え、やり直しの上限に掛からない）。
- `alternate-key-taken-from-deleted-twice-{作り方}`：DbContext のファクトリの作り方（接続文字列で構成した文脈を使い回す＝プールの形／接続なしで構成して実行時に接続文字列を付けた文脈を使い回す＝3 回目の効果測定の HB2／実行時に付けた接続／オプションで渡した接続＝対照）で、2 つめの文脈の保存を 2 回続け、その間に読む。値オブジェクトありの生成コードは EF Core の本物のプールを断るので、破棄を飛ばして同じインスタンスを貸し出し直す派生（`ReusedDbContext`）でプールの観測できる振る舞いを作る。
- `alternate-key-offset-only-{経路}`：日時のオフセットを持つ代替キー（ConcurrencyFixture の `slots.starts_at`＝datetimeoffset(7)。子の `slot_bookings.slot_starts_at` が参照する）のオフセットだけを変える保存を、UpdateAsync とグラフ保存で流し、基準の値（新しいオフセットで保存される）を `ExpectedLines` で固定する（3 回目の効果測定の HB6）。値オブジェクトの列なので、値オブジェクトの中の `DateTimeOffset` の比べ方も同じケースが見る。リモートでは流さない。
- `alternate-key-swap-{扱い}`・`alternate-key-new-takes-released-{順}`・`alternate-key-gone-insert-takes-released-{順}`・`alternate-key-part-moved-off-released-{順}`・`alternate-key-gone-part-then-taker-first`（`HandoffCase`）：2 行の code の入れ替え（docs のとおりどの実装先も最初の更新で断る）と、更新する行が手放す code を挿入する行（新しい行・insertWhenUpdateMissing の挿入）が取る形と、手放す code を参照する部品を別の kit へ付け替える形を、渡す順（releaser-first／taker-first。付け替えの取る行は部品を持たせる kit）で流し、他者が消した部品の後ろでどの実装先でも断られる取りの順を 1 つ流す。リモートの EF Core のサーバーでも流す。new-takes-released と gone-insert-takes-released の taker-first は受け入れた差（G3-B）、part-moved-off-released の taker-first は H2-D1、gone-part-then-taker-first は H2-D2 で、基準の値を `ExpectedLines`、EF Core とリモートの値を `ExpectedEfCoreLines` で固定し、既知の違いの一覧 `Known` に docs を出典にした Accepted として載せる（載っていて一致すれば赤）。
- `key-shape-{guidkey|binary|dotted}[-full|-cascade]`・`key-shape-binary-short-key-full`（`SqlServerKeyShapeCase`）：ConcurrencyFixture の親子 3 組（GuidKey の `vouchers`・`binary(4)` の `badges`・既定のスキーマで修飾した `dbo.zones`）へ実装先どうしの一致の `key-shape-*` と同じ手を流し、QuickER 版・EF Core 版・インメモリ（リモート面と cascade はリモートの EF Core のサーバーも）を比べる（失敗の署名 `differs`・`remote-efcore-differs`・`inmemory-differs`）。短い binary のキーは SQL Server が 0x00 で詰める（docs のインメモリの節「実 DB との既知の乖離」）ので、インメモリとの差を `Known` に Accepted で載せる。
- `alternate-key-{形}-with-*`・`own-write-*`・`HandoffCase` の行・`key-shape-*`（`-full` を除く）は、サーバーが EF Core 版 Repository のリモートと QuickER 版 Repository（SQL Server）のリモート（どちらも ConcurrencyFixture の RemoteServer）でも流し、QuickER 版と比べる（`alternate-key-taken-from-deleted-twice-*` はリモートで流さない）（失敗の署名 `remote-efcore-differs`・`remote-ado-differs`）。QuickER 版のサーバーは受け入れた差（G3-B・H2-D1・H2-D2）を持たないので、いつも基準と同じになる（サーバーを EF Core 版へ取り違えると、この 4 ケースが `remote-ado-differs` で赤になる＝mutation 実証済み）。行列では、この系統の機能（実装先どうしの一致・グラフ保存・Save フック）のリモートのサーバー QuickER 版 SQL Server の担当になる（CRUD の照合のシナリオの群とは別物なので CrudParity は名乗らない）。リモートでサーバーの DB が断った保存は `RemoteRepositoryException`（500）で届くので、`OutcomeAsync` が `throws store-rejected` にまとめる（`ParityFormat` と同じ扱い）。

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
- 列は datetime2・time・datetimeoffset・date・decimal・varchar・nchar・nvarchar・varbinary・除外 blob と、datetime・smalldatetime・money・smallmoney（期待は 1/300 秒・分・小数 4 桁への丸めと型の範囲）。
- 作らない形は `BackendParitySqlServerValueNotGenerated.cs`（Accepted の 2 つだけ）。smalldatetime の秒が 29.999 以上 30.000 以下の値（決まった形は RoundUp 29.999 秒・CarryDay 30.000 秒・範囲の外は最後の分の 29.999 秒）と、datetime の 9999-12-31 23:59:59.997 を超えて丸めると範囲の外になる値（決まった形の OutOfRange）も作る。EF Core 版は生成 DbContext が datetime・smalldatetime に `HasColumnType` を出すので、SqlClient が QuickER 版と同じく丸め・断る（教えないと赤＝mutation 実証済み）。乱数の本流（`BackendParityGenerator`）と別の種の式で引くので、本流の種の指紋は変わらない（記録 `BackendParitySqlServerValues`）。
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
- キーと表名の形（GuidKey の主キー `tokens`・`binary(4)` の主キー `blobs`・ドットを含む表名 `main.regions`。図の親子 3 組）は、シナリオ `key-shape-{guidkey|binary|dotted}`（リモート面＝5 実装先）・`key-shape-{…}-full`（全機能面）・`key-shape-{…}-cascade`（親を子ごと消すグラフ保存）・`key-shape-dotted-blob-stream`（除外 blob の Stream）が持つ。手は 1 つの型 `KeyShapeOp` と形ごとの束縛 `KeyShapeBinding` で流し、キーは手のたびに新しいインスタンスで作る（GuidKey は同じ文字列の別の値オブジェクト、binary は同じバイト列の別の配列＝`FB FF BF n` は base64 で `+` `/` を含む）。読んだエンティティはスロットに置かず、保存先の状態（`ReadStateAsync`）に 6 表を含める。`key-shape-*` の最後の親の無い子の挿入は受け入れた手の差 `OrphanInsert` の印を付け、DB の 4 実装先が外部キーで断ることを期待の手で表明する（ドットの表では `main.region_notes` の外部キーが実際に作られていることの裏付け）。SQL Server の系統は下の「SQL Server の系統の決まった形のケースの軸」の `key-shape-*`。
- 図に載らない形（EditModel・名前付きクエリ・有界バイナリ・素の int の主キー・HTTP 固有・rowversion・SQL Server の採番列）は元のクラスに残す。
- 機能 × 実装先の行列（下の「機能 × 実装先の行列」）で、実装先の一致が受け持つセルは、シナリオの群（`RuntimeMatrixEngines.ScenarioGroups`）のシナリオが流れる実装先から導く。群のシナリオが実在することは `RuntimeTestMatrixTests.EveryRuntimeTestClass_ShouldAppearInMatrix` が照合する。
- 削除する行の値を代替キーで取る保存（EF Core が後回しの行を 2 つめの文脈で書く形）は、シナリオ `graph-updated-row-takes-alternate-key-of-deleted-row`・`-with-new-dependent`・`-user-connections`（`ParityScenario.EfCoreUserConnections`＝EF Core の DbContext を利用者が持つ 2 本の接続から交互に作る構成。接続は破棄の後に開くと断る）・`ef-retry-updated-row-takes-alternate-key-of-deleted-row` と SQL Server のケース `alternate-key-taken-from-deleted-kit-*` が 5 実装先（SQL Server は 2 者）の一致で持つ。
- 変更の無い同じインスタンスに 2 つ以上の道で届き、その下に変更を持つ行がある保存は、軸から組んだシナリオ `n3-unchanged-instance-{道の数}-{経路}-{変更の位置}`（`BackendParityScenarios.UnchangedTwice.cs`＝道の数 2・3 × 単数の SaveAsync〔根の紹介先のコレクションに同じ顧客を道の数だけ置く〕・複数形の SaveAsync〔同じ根を道の数だけ渡す〕× 変更の位置＝子・孫・下に変更が無い対照の 12 本）が持つ。どの実装先も書く前に `InvalidOperationException` で断り、フックは鳴らない（対照は何も書かずに成功する。3 回目の効果測定の HB8）。削除の道を含む組は `n3-unchanged-instance-{deleted-twice|deleted-then-saved|saved-then-deleted}-{none|unchanged|added}`（同じファイル＝削除する根の紹介先のコレクションに同じ顧客を 2 回置く・削除する根と顧客の根を複数形の SaveAsync へ 2 つの順で渡す × 下の行＝無い・変更の無い注文・新しい注文の 9 本）が持つ。削除の道が 2 つなら下に依らず断り、削除の道と保存の道なら新しい注文があるときだけ断る（ほかは削除の道が 1 回だけ消して成功する）。
- Before のフックが新しい行のキーを決める保存（`ParityHookBehavior.AssignKey`）は、シナリオ `n2-hook-assigned-keys-reach-new-children`（親のキーが新しい子へ届く）と、軸から組んだシナリオ `n2-hook-assigned-keys-{型}-{数}-{置き場所}-{経路}`（`BackendParityScenarios.HookAssignedKeys.cs`＝新しい行の数 1・2・3 × 根の顧客・注文・明細 × 同じ親・別の親〔根の顧客は根〕× 単数の SaveAsync・複数形の SaveAsync の 24 本）が持つ。フックは保存ごとの通し番号で違うキーを決める（`ParityHookRecorder.NextKey`・最初のキーは `ParityHookRecorder.AssignedCustomerKey` などのまま。グラフ保存の手が保存の前に `ParityHookRecorder.BeginSave` で番号を戻す）。同じ型の新しい行が 2 つ以上の 18 本は 3 回目の効果測定の HB1 の受け入れで、EF Core 版は新しい行を Before のフックの後に追跡へ入れる（`src/QuickER.Runtime.EntityFrameworkCore/CLAUDE.md`「グラフ保存の追跡」）ので、どの実装先でも同じ結果になる（既知の違いの宣言は無い）。フックが飛ばすキーの未設定の新しい行が同じ型に 2 つある保存（シナリオ `n2-hook-skipped-new-rows-{型}-2-same-{経路}`＝注文・明細 × 単数・複数形の SaveAsync の 4 本）も、どの実装先も何も書かずに成功する。作らない組は `BackendParityHookAssignedKeysNotGenerated` に宣言する。
- 代替キーでない一意な値の J3 を SQL Server の EF Core で見るのは `GraphSaveEfCoreSqlServerRuntimeTests` だけ（SQL Server のケースの図の一意な列は代替キーだけで、EF Core は代替キーを `HasIndex` で並べないので、`HasIndex` を外す壊し方はケースの図では赤にならない）。

## 双方向同期のテスト

### フィクスチャ

- `SyncFixture`：親子 2 テーブル＋版なし子 `sync_notes`（版あり親を FK 参照する混在トポロジ）＋独立の表 `sync_markers`。親テーブルに除外列 `attachment` を持ち、リモートサービス併用の 2 ファイル構成。

### 決定的なシナリオと Theory

- 同期のエンジン（`SyncEngine`）の振る舞いの決定的なシナリオは `SyncModelScenarios.cs` と `SyncModelScenarios.Engine*.cs`・`SyncModelScenarios.HolderKeptByOtherRows.cs`・`SyncModelScenarios.Judge.cs` にある。対象はダウンロード・アップロードの基本・送る操作の決め方・削除伝搬・グラフ保存・後勝ち・競合と ServerWins / LocalWins・外部キー・一意な値の付け替え・割り込み・洗い替え・blob・フック・塊をまたぐ大きい取得・判定役の対照・主キーの付け替え（`SyncModelScenarios.Engine.Rekey.cs`＝`rekey-*`。付け替え先 3 通り × 動かす行の blob の状態 3 通り〔端末が持っている・写し待ちの新しい行・写し待ちの書き直された行〕×〔Collect・LocalWins の上げ・ServerWins の戻し・付け替えの後の写しの失敗〔写し待ちの行だけ〕・送信の失敗〕と、写し待ちを運ぶ腕ごとの行）・適用の試行の上限を超える深い鎖（`SyncModelScenarios.Engine.DeepChain.cs`＝`deep-*`。根を後で書き直す・根が一意な値で保留される・サーバーが鎖を消す・表をまたぐ鎖）。
  - 写し待ちの行の付け替え（`rekey-*-owed-*`）は、前段で写し待ちを作る（新しい行はサーバーの品目 3 を降ろす同期で写しを落とし、書き直された行はサーバーが品目 1 の blob を書き直してから写しを落とす）。前段の終わりに端末の代わりの値（空・前の blob）を表明し、付け替え先の blob が動かした行の本当の blob（サーバーの元のキーの blob）になることを表明する。端末が持っている行 × 写しの失敗は作らない（写すものが無く、仕掛けた失敗が起きない）。腕ごとの行：写す元が無い・届けた後の上書き・`Force` の洗い替え・blob を運ばない ServerWins・blob を捨てる `Force` の洗い替え・写しが試されなかった行・写す元の書き直し・写した後の付け替え・付け替えの前の端末の書き込み・上書きの後の写し待ち（行の名前は `src/QuickER.Runtime.Sync/CLAUDE.md`「無制限バイナリ列」）。乱数の列ではまれにしか出ない（作らない形の宣言 rekey-rows-with-owed-blobs）。
  - 主キーの付け替えの入れ替え（`rekey-swap-through-temporary-key-*` の Collect・LocalWins）は、上げる 2 つの更新が一意な値を入れ替えるので、最初の同期で 2 行とも UniqueValueTakenOnServer を報告し（ModifiedOnServer は出ない）、端末が片方の code を空けた次の同期で行と blob が入れ替わって届くことを表明する。
- 同期のモデルの 4 つの転送（直結・HTTP・実 SQL Server の 3 つの Theory と、HTTP × 実 SQL Server の夜間だけの Fact）が、同じ宣言 `SyncModelScenario.Transports` から行を作り、エンジンの端末（`SyncModelEngineDevice`）で流す。対照の行 `judge-*`（直結だけ。下の「判定役の継ぎ目・台帳・実行ごとの照合」）を除き、どのシナリオも全転送で流れる（サーバー役の口は SQLite の代役も実 SQL Server も同じ点で持つ）。
  - 直結の Theory（`SyncModelRuntimeTests.Scenario_FailsExactlyAsListed`）は、既知の失敗の一覧 `SyncModelKnownFailures.EngineSet`（空）のその行の署名の組どおりに失敗することを表明する。転送の Theory（`SyncModelHttpRuntimeTests.Scenario_MatchesDirect`・`SyncModelSqlServerRuntimeTests.Scenario_MatchesDirect`）と夜間の Fact は、直結の結果と同じ違反（正規化した署名）になることを比べる。
- 直結の結果の使い回し（`SyncModelDirectRuns`）：直結・エンジンの端末で流した結果を、違反の無いものだけ、プロセスの中でシナリオごとに 1 回だけ流し、直結の Theory と転送の Theory が使い回す（エンジンの端末の世界は、同時に並べる数が増えるほど 1 つあたりが遅くなるので、流す世界の数を減らす）。
  - 使い回すのは例外なく終わって違反が 1 つも無い結果だけ。違反のある結果（既知の失敗の行・判定役の対照の行・退行）と例外で終わった実行は使い回さず、呼んだ行ごとに自分で流す（1 つの行の失敗がほかの行の比べ方へ混ざらない）。
  - 鍵は `SyncModelDirectRuns.KeyOf` の 1 か所（世界の組み方の名乗り `SyncModelWorldOptions.Name` とシナリオの名前）。組み方は `SyncModelDirectRuns.Options` の 1 つだけで、シナリオが決めるもの（端末の数・判定役の包み・手の列）は名前で決まる。名前の一覧（`SyncModelScenarios.All`）に無い複製のシナリオは使い回さない。
  - 取り出した結果が頼まれたシナリオのものであることを、結果に持たせたシナリオの参照で確かめ、違えば `InvalidOperationException` で行を赤にする（鍵を誤ると、流していないシナリオの結果で行が緑になる）。
- 直結・HTTP・実 SQL Server の Theory は `SyncModelScenarioParts` が行の番号の余りで 2 つ・4 つ・2 つのクラスへ分けて並列に流す。xUnit は同じクラスの行を並列に流さず、HTTP の行を 1 クラスに置くと直列が同期の Theory の時間を決めるため。
  - 分けたクラス（`SyncSupportAdoSqliteScenarioPart2RuntimeTests`・`SyncSupportRemoteAdoSqliteScenarioPart{2,3,4}RuntimeTests`・`SyncSupportAdoSqlServerScenarioPart2RuntimeTests`）は転送の軸の目印（`ISyncModelTransportTests`・`MatrixAxisValue`）を持たない。軸の値ごとのクラスは本体の 1 つだけ。
  - 実 SQL Server の 2 つの分は、SQL Server の別々のコンテナのコレクション（本体は `SqlServerSecondContainerCollection`・2 つ目は `SqlServerContainerCollection`）に置く。同じコレクションのテストは 1 本ずつ流れるので、同じコレクションに置くと分けた意味が無い（普段の SQL Server のテストの時間の半分以上をこの Theory が占める）。
  - HTTP × 実 SQL Server の決定的なシナリオは、普段の CI で流さず夜間だけ全部流す（普段は実 SQL Server の直結が失敗の注入の行も持つ）。行ごとの Theory でなく、夜間の目印（`NightlyEngine`）を持つ 1 つの Fact `SyncSupportRemoteAdoSqlServerRuntimeTests.Scenarios_MatchDirect_Nightly` が、種の決め方（`SeedSource.Resolve`）のプロファイルが夜間のときだけ全シナリオを並列度 2 で流し、失敗をまとめて報告する。普段のプロファイルで行を出さない Theory はスキップ（スキップ 0 の原則に当たる）になるため。
  - 分ける数とその数のクラスは別々の場所にあるので、`SyncModelRuntimeTests.ScenarioParts_CoverEveryScenarioOncePerTransport` が `SyncModelScenarioParts.Names` を呼ぶ MemberData を反射で集め、転送ごとの行の和が流すシナリオとちょうど一致し重ならないことと、コレクションを持つ分（実 SQL Server）の分けたクラスが別々のコレクションにあることを表明する。mutation 実証済み（実 SQL Server を分ける数を 3 にする・2 つの分を同じコレクションに置く、のそれぞれで赤）。
- 実 SQL Server の転送が見るもの：本物の rowversion の版ガード・`MIN_ACTIVE_ROWVERSION()` の取得の上限・TOP のページング・外部キーエラーの分類・実 `varbinary(max)`・照合順序、それらと失敗の注入・割り込みの点・HTTP のシリアライズ（版の Base64・400 / 409 / 500 の分類）との組み合わせ。
  - SQL Server に固有の事実を期待の手（結果・行・未送信の行の面など）で表明し、Theory は「直結と同じ違反になる」を比べる。
  - SQL Server だけに効く壊れ方は実 SQL Server の Theory が、全転送に効く壊れ方は直結の Theory が拾う。mutation 実証済み。

### 差分ソースの HTTP と実 SQL Server の固有のテスト

- `SyncSupportRemoteAdoSqliteHttpSourceRuntimeTests`：HTTP・CI 常時。SyncFixture の SQLite のサーバー役（`SyncFixtureTestServer`）を in-process Kestrel の向こうへ置き、差分ソースの面でアンカーの Base64 往復・続きの有無・上限・全キー・400 拒否を見る。`ServerDown_SurfacesAsHttpRequestException` はエンジンの端末で、エンジンも HTTP の例外を包まずに伝えることを見る。
- `SyncSupportAdoSqlServerFixtureRuntimeTests`：NULL を許す一意制約の 2 件（`sync_markers` は SyncFixture にしか無い）。
- `SyncSupportAdoSqlServerKeyPagingRuntimeTests`（Docker・行の表 `SyncKeyPagingRows`＝影響の地図の名前 `table:SyncKeyPaging`・図 `SyncKeyFixture`＝版なしの表だけ）：版なしの表のキー順ページングを、主キーの型（datetime2(7)・datetime・date・datetimeoffset(7)・uniqueidentifier・bigint・decimal(18,4)・varchar(20)・nvarchar(20)）× 境目の値の行で、生成の `VersionlessDirectSyncSource` に最初のページから最後まで読ませ、サーバーの全行を `ORDER BY` の順にちょうど 1 回ずつ読むことを見る（batchSize は 1。黙って読み飛ばす形を出す `datetime2-ticks-between-legacy-datetime-steps-in-pairs` だけ 2）。値オブジェクトのキーと HTTP の差分ソース（サーバーで同じ直結のソースを呼ぶ）は作らない（理由はクラスの XmlDoc）。
- `SyncSupportRemoteAdoSqlServerFixtureRuntimeTests`：上限の値の HTTP の往復・版の書き戻しと 409 の競合の収集・NULL の一意な値。
- 端末は `SyncEngineFixtureDevices.cs` の `SyncFixtureEngineDevice`（SyncFixture の 4 表にトリガーを張り、`SqliteLocalRowStore` と `SyncTable` でエンジンを組む口）。サーバーの差分ソースは DI に登録した直結か HTTP の生成の差分ソース。未送信は端末の行の状態の表で読む。
- 生 SQL の `INSERT … RETURNING` が別の接続の読み取りを待つことは `BusyTimeoutDefaultAdoSqliteRuntimeTests.RawSqlReturning_WaitsForReaderOnAnotherConnection` が見る。
- 生成の DI の登録（`AddGeneratedSyncEngine`）の構築時の除外と登録時の断りは、トリガーの確認の表の DI の行（`table:SyncTriggerGuard` の行 `di-*`。下の「同期のトリガーのテスト」）が見る。

### テストのサーバー役

- サーバー役（SyncFixture の `SyncTestServer*`・`SyncFixtureTestServer`、SyncModelFixture の `SyncModelServer*`、SyncVoFixture の `SyncVoTestServer*`・`SyncVoFixtureTestServer`）はフィクスチャをまたいで 1 つにまとめられない。版の付与とガード・差分の SQL は同じ意味でも、生成型（`IRepository<,>`・`EntityBaseCore`・`SaveConflictException`）がフィクスチャごとの名前空間で別の型になるため。
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
  - Journaled：最初の同期の前に Repository 越しに書く（トリガーが記録する通常のオフライン編集。一部は初期データのキー・一意な値とぶつける）。書き手の操作として履歴へ積む。
  - Untracked：最初の同期の前に記録させずに入れる（端末の口 `SyncModelDevice.WriteUntrackedAsync`。行の状態も無い・サーバーに無いキーだけ＝端末のキーの範囲の基点＋800 の帯）。誰の書き込みでもないので履歴へ積まず、消失なしの表明は「無い」を期待する（docs：上がらず削除伝搬で消える）。
  - 中身は 3 本目の乱数（`SyncModelGenerator.PreloadSeed`）だけで引く。追跡しない帯のキーと値は、ほかの書き手の同じキー・別表記の候補にしない。
  - 初期データへそろったかの照合は None・Refresh の端末だけに掛ける。
  - 最初の同期をしない端末（Journaled・Untracked）から見ると、初期データはまだ受け取っていないほかの書き手の内容。その端末がそのキーをサーバーと同じ内容（品目は blob も）で持つまで、版の競合の根拠の判定と消失なしの表明は、初期データをそのキーの書き手に数える。
  - 追跡しない行に端末が初めて書いたら、その行の今の内容と blob をその端末の書き込みとして履歴へ積む（Repository 越しの更新・blob の書き込みは、サーバーへの挿入として全列を送る）。
  - 作らない形は `SyncModelNotGenerated` に宣言する。Accepted はサーバーにあるキーの追跡しない行・別の端末の DB の写し・端末 4 台以上・小さい取得の行数の上限・古い読みの保存〔同期が降ろす前に読んだ実体を降ろした後に保存する書き込み。項目 stale-entity-saves。見分ける形と見分けない形は同期のエンジンの決まった形の行が持つ〕・サーバーの今より古い版の持ち込み（imported-stale-mirror-versions）・イベントの行の持ち込み（mirror-import-events）ほか。Pending は無い。
- 空振りの守り：広い世界の種が既定の種の数のとき、最初の形ごとに実際に効いたことを `CoverageGuard` で表明する（`SyncModelRun.PreloadEffects`＝洗い替えで作った端末がそろった・初期データのキーへの記録が DuplicateOnServer になった・最初の同期の前の記録が UniqueValueTakenOnServer で断られた・どの書き込みも触れなかった追跡しない行が静止化の後にどの端末にも無い）。
- 3 台目があって初めて起きる順序は、決まった形の行 `three-devices-change-reaches-the-other-two-in-different-orders`（全転送・3 台の世界）が持つ：端末 1 が UploadAsync だけで上げ（まだ降ろさない）、端末 2 がその版を降ろしてから編集して上げ、端末 3 は端末 2 の版だけを降ろす。端末 1 は自分が上げたキーに来た端末 2 の版を自分のエコーとみなさずに降ろし、端末 3 は端末 2 の版を元にした編集を競合なしに上げる。

- 表明：
  - 止まらない：静止化が、例外・未送信・競合・打ち切りを残さず終わる。
  - 収束：3 者の全表と blob が一致する。
  - 消失なし：最終値は誰かが書いた値。書き手が 1 人で競合も ServerWins の破棄も無いキーはその値。
  - 外部キー：書き込みの手・実行の前後で `foreign_key_check` の違反の集合を比べ、新しく出来た違反だけを数える（下の「判定役の継ぎ目・台帳・実行ごとの照合」）。
  - 実行ごとの照合（署名 `5/` の族）：実行 1 回がサーバーと端末に起こした変化を、モデルの台帳と照らす（同じ節）。
  - 書き手が 2 人のキーの期待は、ポリシー×ぶつかり方の決定的なシナリオが持つ。
- 根拠のない競合：
  - 書き手がその端末だけのキーへの版の競合は、根拠なしとして違反にする。
  - サーバーが書き込みを断った理由の競合（DeleteRefusedOnServer・UniqueValueTakenOnServer・ParentMissingOnServer）と、後勝ち＋ServerWins の決着は、実行の直後のサーバーに根拠が無ければ `3/spurious:{表}:{理由}:{形}` にする（`SyncModelRun.CheckRefusalBasisAsync`）。
  - 根拠はサーバーの状態からだけ取る。DeleteRefused＝参照する行がある／UniqueValueTaken＝同じ一意な値の別の行がある／ParentMissing＝親が無い。
  - 端末のエンジンが決定の持ち越しを宣言し（`CarriesServerWinsDecision` が真）、台帳が実行の前から戻せなかった行の決定を持ち、その実行が DiscardedByServerWins を報告した後勝ち＋ServerWins の決着は、決定を果たした決着として根拠の確認に掛けない（期待値からは外す。決定の果たし方は実行ごとの照合の `5/carry:*` が照らす）。台帳の決定は宣言に依らず作るので、宣言が偽の端末（判定役の包みが宣言を偽にした端末）の決着は根拠を確かめる。台帳はテスト側の記録だけを読む。決まった形の行は `judge-carry-fulfilled-by-lww-serverwins-is-not-spurious`・`judge-undeclared-carry-lww-serverwins-settlement-without-basis-is-spurious`・`judge-lww-serverwins-settlement-without-basis-is-spurious`。
  - 決定を果たした行は送られていない（エンジンはその行を送らずにサーバーの行で戻す）ので、同じ実行のほかの行の拒否の根拠を確かめるとき、その行を届かなかった行に数え、その行の未送信の付け替え・値の手放しで根拠を打ち消さない（`RecordLastWriteWinsSettlementsAsync` が決着と別に返し、`CheckRefusalBasisAsync` へ届かなかった行として渡す）。決まった形の行は `fk-lww-serverwins-label-delete-refused-while-item-reparent-is-held`（全転送。決定を果たした品目の付け替えの間に断られたラベルの削除は根拠あり）、対照は `judge-unfulfilled-reparent-delete-refusal-without-basis-is-spurious`（競合にも決着にも果たした決定にも入らない付け替えは、今までどおり届いたとみなして `pending-reparent` で名指しする）。
  - 参照する行・持ち主が同じ端末の未送信で付け替わる／値を手放すなら、その表がその実行の範囲にあり、その行がその実行で届かなかったのでない限り数えない。
    - 届かなかったかは、報告された競合・決着に加え、値を手放す書き込み自身に実行の後のサーバーで断られる根拠（親が無い・手放されていない別の行が値を持つ）があるかで決める（判定は 1 段。ServerWins が戻せずに決着を保留した行は報告されないため）。決まった形の行は `repro-d2-upsert-unique-lww-serverwins-held-holder-is-basis`。
    - 割り引きそのもの（判定役が緩む向き）は対照の行が守る：付け替えは `judge-unfulfilled-reparent-delete-refusal-without-basis-is-spurious`、値の手放しは `judge-unfulfilled-release-unique-refusal-without-basis-is-spurious`（何も送らない実行が値を取る行の UniqueValueTakenOnServer を報告し、未送信の手放しを割り引いて `pending-release` で名指しする。割り引き `IsReleased` を外すと持ち主が根拠に数えられて違反が出なくなり赤）。
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
  - 書き込みの case 15（`TryUniqueShape`）が、一意な値の付け替えの形を 6 つ作る（`ModelShapeStep`）。品目の code・フォルダーの name・端末とサーバーの他者で作る。
    - release-take：値を空けて別の行が取る。半分は取る側を先に触る。
    - release-take-chain：3 行。
    - delete-take：消した行の値を取る。
    - delete-take-reparent：消した行の値を取って別の親へ付け替え、元の親を消す。
    - exact-swap・rotation：release-take・release-take-chain を 4 本目の乱数で閉じた形（先頭の行が仮の値を経て最後の行の元の値を取る）。端末で回した循環も 1 回の同期で上がり（n15 の 2 行と同じ）、サーバーで回した循環は 1 回の適用で降りる（docs の「競合」の節）。
- 4 本目の乱数（`SyncModelGenerator.ShapeSeed`）：本流がその歩の手を引き終えた後にだけ引き、本流の手を置き換える（本流の乱数の引き方は変えない。置き換えた手で書き手の DB が変わるので、後の手の対象・値は変わりうる）。
  - 閉じた入れ替え・回転（`UniqueCycleRate`）：上の 2 つ。
  - ピン留め（`PinRate`）：本流が選んだフォルダーの更新を `PinFolderOp` へ。ピン留めがあれば半分で null へ戻す（循環を外す手）。ピン留めされた品目は消す候補にしない（`SyncModelGenerator.IsDeletableItem`）ので、循環に入った品目は、外す手が来るまで書き手の DB で消されない。書き込みの約束（`CheckWriterContract`）は、ピン留めが書き手の DB にある品目を指すことと、ピン留めされた品目を消さないことも確かめる（`gen/writer-contract:*:pinned-item-missing|pinned:*`）。
  - ミラー版の持ち込み（`MirrorRate`）：本流が選んだ端末のフォルダー・品目の更新を `MirrorServerRowOp` へ。端末とサーバーの両方にある行を、記録させずに（`SyncModelDevice.WriteUntrackedAsync`）サーバーの今の内容と版の列で書き換える（blob は書かない）。写す内容が端末の DB で成り立つ（参照先が端末にある・親が自分の子孫でない・一意な値をほかの行が持たない）ときだけ作る。台帳は記録しない書き込みとして扱う（持ち分があるキーの内容だけを置き換える）。
- 5 本目の乱数（`SyncModelGenerator.RekeySeed`）：本流の手が端末の品目の更新か削除 1 つのときだけ、本流が引き終えた後に引き、`RekeyRate`（0.3）の割合でその手を主キーの付け替え（`RekeyItemOp`・経路 `Via=Sql`＝端末の生の SQL。トリガーは記録する）へ置き換える。形は新しいキー（付け替えの帯 `RekeyKeyOffset`＝端末の基点＋600 から。本流の採番を進めない）・端末が消したキー（同じ手で端末の別の品目を消してそのキーへ）・仮のキー経由の入れ替え。動かす行・消す行・入れ替える行は子の無い品目だけ（作らない形の宣言 rekey-rows-with-children）。割合を 0 にすると種の指紋の記録は付け替えを足す前と同じ（ほかの 4 本の乱数の引き方を変えていないことの確かめ方）。台帳は付け替えを古いキーの削除と新しいキーの書き込み（行と blob）として持ち分を立て、入れ替えの仮のキーは記録が残りうるキーに控える（`SyncModelRun.NoteWrite`）。
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
- 取得の上限の固定もサーバー役が持つ（`ISyncModelServerRole.FixesFetchCeilingAtRunStart`）：SQLite の代役は上限を持たない（false）、実 SQL Server は `MIN_ACTIVE_ROWVERSION()` を実行ごとに 1 回取る（true）。実行の途中にサーバーへ書かれた行は、固定するサーバー役ではその実行で降りず、次の実行で降りる（docs の洗い替えの節と上限の説明）。
  - 決まった形の行は、期待を転送の名前でなくこの性質で選ぶ手 `WhenServerFixesCeilingOp(IfFixed, IfOpen)` で書く。固定する側（IfFixed）では「その実行の直後は降りていない → 次の同期で降りる」を表明する（取りこぼさないことの裏付け）。
  - 使う行は `dl-server-update-after-upload-is-still-downloaded`（取得の直前に書かれた版）と、割り込みの網の洗い替え × 持ち主 × 更新 × 取得の前の点の 4 行（下の「割り込みの点」）と、実行の途中にサーバーの他者が書く洗い替え・同期の行 3 本（`fk-parent-created-between-table-fetches-arrives-in-same-sync`・`blob-refresh-discard-leaves-no-blob-on-row-changed-after-ceiling`・`blob-refresh-discard-empties-blob-of-row-written-after-retry`）。ほかの行は、どのサーバー役でも同じ期待で書いてある（実 SQL Server の Theory が直結と同じ違反になることで確かめる）。
  - 実行の途中にサーバーの他者が書く行（`ArmInterruptOp` の書き手がサーバーの他者）を足すときは、書かれた版がその実行に入ることを期待に置くなら、この手で枝を分ける。分けない行は、実 SQL Server の転送でだけ赤になる。
- DuplicateOnServer の決着の中身は決まった形の行が持つ：Collect＝`conflict-duplicate-key-on-server-is-reported`・LocalWins＝`lw-overwrites-duplicate-key-on-server`・ServerWins＝`sw-duplicate-key-on-server-reloads-server-row`（同期＝同じ実行のダウンロードが揃える）と `sw-duplicate-key-on-server-upload-only-reloads-server-row`（単独の UploadAsync＝ServerWins が自分で読み直す）・後勝ち＝`lww-duplicate-key-on-server-overwrites`。モデルは書き手が 2 人のキーの決着を表明しない（報告せずに上書きする壊し方は、乱数の種では結果の表の網羅だけが捕まえる）。
- 表どうしの循環の列（`pinned_item_id`）は乱数の列がピン留めの手で書く（上の 4 本目の乱数）。決まった形の行も経路ごとの期待を持つ：`cycle-pin-to-new-item-uploads-in-rounds`（新しい品目へのピン留めが最初の巡で断られ次の巡で通る）・`cycle-pinned-item-is-read-back-before-folder`（ダウンロードがピン留めの先の品目のまだ無いフォルダーを打ち切らずに入れる）・`cycle-serverwins-restores-folder-pinned-to-new-item`（単独の UploadAsync の ServerWins。端末に無い品目をピン留めするサーバーの行はその実行では戻さず、フォルダーは端末の内容のまま未送信で残り、次の ServerWins の同期で品目と一緒に戻る。枝はエンジンの宣言 `CarriesServerWinsDecision` で選ぶ＝`WhenEngineDeclaresOp`）。
- 次元表 `SyncModelDimensions`：
  - OpFamily に 6 つの一意な値の形と pin・mirror-import・rekey（mirror-import は書き込みの経路を持たない端末の手＝Via=None、rekey は生の SQL の経路の端末の手＝Via=Sql で、一意な値の形・挿入のキー・別表記・blob の大きさを持たない）、因子 UniqueTable・PropagateDeletes・Excluded（2 表・3 表の水準 two・three を含む）・Batch・Blobs（後ろ 4 つは SyncOptions の Source）・FaultKind・KeyOrigin（own・stolen・not-insert）・Spelling（UniqueSpelling の値と none）・PayloadSize（small・large・none）。
  - 世界の形の因子 Devices（2・3）・Preload（操作の書き手の最初の形＝`ModelPreload` の値。サーバーの他者は None）。2 台の世界の最初の形が None 以外の組・端末 3 が 2 台の世界にいる組・サーバーの他者が None 以外の組は、定義から起こり得ない組として外す。
  - 3 因子の組 (Policy, OpFamily, PropagateDeletes)・(Actor, OpFamily, UniqueTable)・(Preload, Mode, Policy)。
  - Blobs=False は受け入れの組（乱数は常に blob を運ぶ。docs の「運ぶかは端末ごとに一貫させる」）。
  - サーバーの他者（Actor=Server・Via=None）× 衝突し得る別表記は受け入れの組。
- 実行の結果の表 `SyncModelOutcomeDimensions`（登録簿 SyncModel/Outcomes）：SyncConflictReason と SyncLocalRowChangeReason（値は `DiscardedByServerWins` だけ）の全値×Mode×Policy。実行の後の結果だけを数え、乱数を引かない。網羅しない組の一覧 `SyncModelOutcomeDimensions.EngineGaps` は空。
- 網羅の行 `SyncModelCoverageRows`（Id は `coverage-`・12 行）：
  - 乱数の種で作れない組を持つ。サーバーの他者の Extras の操作 4 つ（作り直し・失敗する書き込み・キーだけのスタブ・持っていた実体での削除）・グラフ保存の失敗する書き込み。
  - DuplicateOnServer は乱数の種が同じキーの挿入で作る（行を持たない）。
  - 既定の 60 種でたまたま欠ける組も持つ：フォルダー・イベントをそれぞれ除外したダウンロード（`coverage-download-excluding-folders-and-events`）。
  - 広い世界の既定の種でたまたま欠ける組も持つ：端末 3 のフォルダーの delete-take-reparent（`coverage-device3-delete-take-reparent-folders`・3 台の世界）と、追跡しない行を入れた端末のラベルを除外した同期（`coverage-untracked-device-sync-excluding-labels`・行の入力の `Preloads` で最初の形を宣言する）。
  - release-take の形の半分を閉じた入れ替え・回転へ回すので、既定の種でたまたま欠ける一意な値の形の組も持つ：サーバーの他者の品目の release-take-chain（`coverage-server-release-take-chain-items`）・サーバーの他者のフォルダーの exact-swap（`coverage-server-exact-swap-folders`）・端末 3 の品目の release-take-chain（`coverage-device3-release-take-chain-items`）・最初の形 Journaled の端末の rotation（`coverage-journaled-device-rotation-folders`）。
  - 12 行の内訳：サーバーの他者の Extras の 4 行（`coverage-server-reinsert`・`coverage-server-failing-write`・`coverage-server-delete-stub`・`coverage-server-delete-held`）・`coverage-graph-failing-write`・上の 3 行と 4 行。
  - 直結の乱数のテスト `SyncSupportAdoSqliteEngineRuntimeTests.RandomSeeds_ConvergeWithoutLoss` が種の前に並列に流す（単独の種では流さない）。操作を 1 歩ずつ次元表で、同期の結果を結果の表で数える。
  - 行は生成器の代わりにモード・ポリシー・種の組を宣言する（例：`coverage-server-delete-stub`）（`SyncModelDimensions.Observe` の多重定義）。行の同期が宣言と違えば構築で断る。
  - 行は出すはずの組（`SyncModelCoverageInput.ExpectedTuples`）を宣言し、出さなければ `SyncModelCoverageRows.MissingTupleProblems` が行を名指しして、種と行を流す前に赤にする（実装先どうしの一致の網羅の行と同じ形）。割り当ては Observe と共有の `SyncModelDimensions.Assignments` で、流さずに判定する。
  - 決定的なシナリオ（`SyncModelScenarios`）は別の Theory で流れて網羅の計数に入らないが、網羅の行は数える。行は期待の操作を持たず、次元表に無い操作の型は宣言の誤り。
  - 行の違反は既知の失敗の一覧で外さず、すべて新しい失敗として赤にする。
  - 残る網羅しない組は受け入れの組だけ（`SyncModelDimensions.EngineGaps`＝Blobs=False と、サーバーの他者 × ぶつかり得る別表記。結果の表の `SyncModelOutcomeDimensions.EngineGaps` は空）。
  - mutation 実証済み。
- 静止化が終わらないときは、最後の周の打ち切りと競合を分類して署名を細かくする（`SyncModelRun.ClassifyStuckTruncationsAsync`）。
  - 一意な値の回転：`1/no-quiesce:unique-swap:{表}`（今のエンジンは同じ適用で降ろすので、出たら違反。docs の「競合」の節）。打ち切った行のサーバーの値をローカルで持つ行、その行のサーバーの値をローカルで持つ行…と辿って元の行へ戻る形で、2 行の厳密な入れ替えはその最短の形（判定は `SyncModelRun.UniqueRotation`。回転の途中に未送信の行・古い写しの行が混ざっても同じ）。2 行の入れ替えは打ち切りの分類の最初に、3 行以上の回転はほかの形（端末側の循環を含む）が説明しなかった打ち切りにだけ最後に当てる。2 行に限ると 3 行以上の回転が粗い署名に落ち、最初に当てると端末だけで回した循環を `pending-unique-cycle` から奪う。
  - 値を取る行自身の古い参照が持ち主を残す：`1/no-quiesce:holder-referenced-by-taker:{表}`。
  - 持ち主がサーバーに無く、別の表のローカルの行が持ち主を（推移的に）参照し、その行のサーバーの行が値を取る行の下にある：`1/no-quiesce:holder-referenced-by-other-table:{表}`。そのうち、参照する行の中にローカルの行がサーバーの行と同じ最新の写しがある形は `1/no-quiesce:holder-referenced-by-other-table-current-child:{表}` に分ける（`SyncModelRun.HasCurrentReferrer`）。引き留める行の形ごとの期待は決定的なシナリオ（`SyncModelScenarios.HolderKeptByOtherRows.cs`）が持つ。
  - 端末側の一意な値の循環：`1/no-quiesce:pending-unique-cycle:{表}`。
  - 回転の場合は回転の行すべての、値を取る行自身の古い参照の場合は打ち切った行と持ち主の行の、別の表の参照の場合は打ち切った行・持ち主・参照する行と、サーバーで値を取る行の下にあるために止まった打ち切りの行の、循環は集まりの行の競合を説明済みにする。
  - どれにも当たらない打ち切りか説明の付かない競合が残れば、粗い署名 `1/no-quiesce:conflicts-or-truncations` もあわせて出す。分類できた分が隠さない。
  - 署名が名指す形の行：`swap-folder-names-settles-in-one-sync`・`swap-codes-settles-in-one-sync`・`rotate-three-codes-settles-in-one-sync`（3 行の回転）・`n15-pending-unique-cycle`・`n15-pending-unique-cycle-three-items`（静止化の失敗の署名は表の名前を残すので表ごとに 1 行）。
    - 同期のエンジンは入れ替えと回転を同じ適用で降ろすので、入れ替え・回転の 3 行は「1 回の同期が打ち切りも競合も残さずに降ろし、端末の行がサーバーと同じ値になる」を表明して緑になる。端末で回した循環の 2 行も、直結の Theory で緑（既知の失敗の一覧 `SyncModelKnownFailures.EngineSet` は空）。署名と分類は、乱数の種でこの形が出たときに細かく名指しするために残す。
  - 別の表の参照の判定が狭い（持ち主がサーバーにある・参照する行のサーバーの行が値を取る行の下に無い・参照する行がサーバーに無い・同じ表の参照、のどれも数えない）ことは、DB を使わない対照 `SyncModelRuntimeTests.OtherTableHolderShape_CountsOnlyTheNarrowShape` が表明する（条件を 1 つずつ外すと赤になる）。一意な値の比べ方をサーバー役のものから序数へ戻すと、持ち主の名前が大文字と末尾の空白だけ違う形（実 SQL Server の比べ方では当たり、序数では当たらない）が赤になる（`StuckShape` も同じ対照で見る）。最新の写しの形を分ける判定（`HasCurrentReferrer`）も同じ対照が見る。
- 既知の失敗の一覧は、直結の組（決定的なシナリオの直結の Theory と直結の乱数の種）が `SyncModelKnownFailures.EngineSet`（空）、ほかの転送の乱数の種が `SyncModelEngineTransports.Known`（空）。
- 既知の失敗の一覧の乱数の項目のうち RegularSeeds / NightlySeeds の項目は、全部の種をまとめて照合して「起きない」も赤にする。種ごとの照合は「載っていない」しか見ないため。
- 乱数の種の照合は、静止化で起きた例外を途中の同期の例外と同じに扱い、表明 2・3 の署名から表の名前を外して行う（`SyncModelRuntimeTests.Family`）。
- 種の数は `QUICKER_SYNC_MODEL_SEEDS`（既定 60）と、広い世界の種の `QUICKER_SYNC_MODEL_WIDE_SEEDS`（既定 48）。夜間の数は、直結のエンジンの端末の組が 1200＋広い世界 240（下の「エンジンの端末」）、小さい塊の組が 240＋広い世界 48（下の「小さい塊の端末」）。広い世界の種は同じテストの中で既存の種の後に流し、種ごとの照合と再現の手順はその範囲の決め方で、既知の失敗の「起きない」の照合は既存の範囲の種だけで、網羅しない組の照合は 2 つの範囲がどちらも既定の種の数のときだけ行う。種の指紋は範囲ごとに別の記録（`SyncModel`・`SyncModelWide`）。時刻と乱数の出どころは種だけ。
- 1 種だけ流すときは `QUICKER_SYNC_MODEL_SEED`（直結）か `QUICKER_SYNC_MODEL_TRANSPORT_SEED`（HTTP・実 SQL Server）を使う。
- 広い世界の種（120000 以上）もこの変数で流せ、種の番号から世界の形が決まるので、番号の範囲が合う側の計画にだけ載せて 1 回だけ流す（`SyncModelSignatures.ResolvePlans`）。
- 単独の種のとき、実 SQL Server は種を 4 で割った余りで失敗の注入なし（0・1）かあり（2・3）かが決まり、受け持つ側のテストだけが流す。
- もう一方のテストは「もう一方が受け持つ」と出力へ 1 行書いて何も流さず、失敗にしない。
- 単独の種のとき、既定の種の数を前提にした表明（網羅しない組の照合・空振りの守り）は外れる。

### 判定役の継ぎ目・台帳・実行ごとの照合

判定役（`SyncModelRun`）は、端末のエンジンを面越しに呼び、エンジンの自己申告に依らない台帳で実行 1 回ごとの結果を照らす。規則の正本は `SyncModelRun.Ledger.cs` の先頭の remarks。

- 継ぎ目（`SyncModelDeviceFacets.cs`）：
  - 端末のエンジンの面 `IModelDeviceSync`（`RunAsync`・`RefreshAsync`）と、状態の面 `IModelDeviceSyncState`（未送信の行ごとの `ModelPendingRow`＝表・キー・追加か更新を含むか・削除を含むか・サーバーがその行を知っているか・変わる印）。エンジンの端末の実装は `EngineModelDeviceSync`（`SyncEngine` を呼ぶ）・`EngineModelDeviceSyncState`（行の状態の表の未送信の行から作る）。
  - 端末の作り方は `SyncModelWorldOptions.CreateDevice`（既定は `SyncModelEngineDevice.CreateAsync`）で差し替える。判定役は、端末の状態の表を直接読まずに、状態の面を読む。
  - エンジンの宣言 `IModelDeviceSync.CarriesServerWinsDecision`：ServerWins の実行が戻せなかった行の決定を、後の実行へ持ち越すか。エンジンの端末は真。判定役の包み（`SyncModelJudgeBreaks.cs`）は名乗る値を包みの宣言 `SyncModelJudgeDevice.CarriesServerWinsDecision` で決める（持ち越しを照らす対照の行は真、ほかの多くの対照の行は偽）。宣言が偽の端末は対照の行の包みだけなので、下の照合のうち「宣言が偽の端末」の枝は対照の行が守る。
  - 宣言で振る舞いが変わる決まった形の行は、期待の手 `WhenEngineDeclaresOp`（端末・宣言 `EngineDeclaration`・真の手・偽の手）で枝を選ぶ。宣言は `CarriesServerWinsDecision` の 1 つだけ。エンジンや転送の名前で選ばず、どちらの枝も結果を行ごとの値で表明する（使う行は例外で終わった ServerWins の 3 行・`fk-lww-serverwins-label-delete-refused-while-item-reparent-is-held`・`cycle-serverwins-restores-folder-pinned-to-new-item`）。エンジンの端末が通るのは真の枝。
  - 降ろす行の上限は、同期の手 `ModelSyncOp.DownloadRowLimit` と洗い替えの手 `RefreshWithOptionsOp.DownloadRowLimit`（どちらも internal・null は製品の既定）で渡す。決まった形の行だけが設定し、生成器は設定しない（internal のプロパティは種の指紋の要約に載らない）。
  - 端末の部品 `SyncModelDeviceParts` の口と、端末（`SyncModelDevice`）の読み口：
    - `WriteUntrackedAsync`（記録しない書き込みを流す口）：記録しない書き込み（最初の形 Untracked の行・一括の Raw の挿入・`LocalSqlOp` の `Untracked`・判定役の包みの生の SQL）を、流す間は端末の未送信の記録が残らないように流す。前からある未送信の記録は変えない。同期のモデルは記録しない書き込みをすべて `SyncModelDevice.WriteUntrackedAsync(write, ct, withoutForeignKeys)` に通す。
      - 口が接続を開いて製品の `SyncLocalTracking.WriteUntrackedAsync` を呼び、書き込みの関数（`Func<DbConnection, Task>`）はその接続だけで書く（行は `SyncModelDevice.LocalRows`＝`ILocalRowStore.InsertAsync`、SQL はその接続のコマンド）。接続は、外部キーありが `ILocalRowStore.CreateDedicatedConnection`、外部キーなし（`withoutForeignKeys`）が外部キーの検査を切ったプールしない接続。
      - 関数が渡された接続でなく生成の Repository・`device.Sql`（別の接続）で書くと、API が持つ書き込みのロックを待って失敗する。
    - `ClearPendingRecordsAsync`：1 行の未送信を外す（エンジンの端末は行の状態の表のその行の未送信を外す）。判定役の対照の行の包みが、エンジンが記録を捨てた結果を作るのに使う（記録しない書き込みとは別の明示の手順）。
    - `CurrentParts()`：その端末がいま載せている部品を返す。部品の一部だけ差し替えて `Install` で載せ直す包み（判定役の包み）が使う（ほかの部品・口は今のまま持ち越す）。
  - 生の SQL の手 `LocalSqlOp` の指定 `Untracked`（既定は偽）：真なら SQL を `SyncModelDevice.WriteUntrackedAsync` を通して流す（外部キーの検査を切った接続）。端末が前から持っていた状態・壊れた状態を作る手（利用者が送る編集でない業務の表の書き込み）に付け、偽の手はトリガーが利用者の書き込みとして記録する。業務の表を書かない手（DDL・補助の表だけ・トリガーを足す・落とす）には付けない。
- 台帳「端末の持ち分」（`SyncModelRun.Ledger.cs`）：（端末・表・キー）→ その端末が自分で書いてまだ届いていない最後の内容。品目の blob は行と別の品目。
  - 作るのは、書き込みの手の前後の端末の DB の差分・実行の前後の端末とサーバーの DB・実行の公開の結果（競合・`LocalRowChanges`）・失敗の注入の控えだけ。状態の面・端末の状態の表は読まない（エンジンが印を間違えたときに、判定が一緒に間違えないため）。状態の面を読むのは、比べる相手にする照合（`5/facet:server-knows`）だけ。
- 実行ごとの照合（署名は `5/` の族。根拠の判定と同じく実行の直後に確定し、静止化が失敗しても報告する）：
  - `5/stale-overwrite`：実行が、端末の持ち分の無い行の古い内容で、サーバーの内容を変えた。
  - `5/unreported-overwrite`：版で守る実行が、端末が最後に見た後のほかの書き手の内容を、競合を報告せずに端末の内容で上書きした。
  - `5/local-discard`：端末の持ち分の内容が、報告なしに端末からもサーバーからも消えた（Collect の競合の報告だけでは許さない）。
  - `5/carry:overwrote`・`5/carry:edit-lost`：戻せなかった行の決定の内容をサーバーへ書いた（宣言が真のエンジンだけ違反）・決定の後の編集が報告なしに消えた（宣言に依らず違反）。宣言が真のエンジンでは決定を行の単位で持つ：行の決定が決定の後のその行の編集で外れたら同じ行の blob の決定も外し、実行の間に行（blob を除く列）が変わった品目には blob の決定を作らない（編集した行の記録は blob の列ごと送る）。決定の後に編集の無い行の blob の決定は照らす。逆向きに、決定の後に端末が記録つきの書き込みで blob を書いた品目は行の決定も外す（エンジンの決定の印は行の連番に付き、blob だけの書き込みも連番を進めるので、後の実行が行を blob と一緒に送る）。blob の内容では決めない（エンジンがサーバーの blob を端末へ写しても端末の書き込みではなく、行の決定は外れない）。宣言が偽のエンジンでは台帳の作り方を変えない（数えるだけの件数も同じ）。対照の行は `judge-carry-blob-overwrite-is-a-violation`（編集の無い行の blob の上書きは違反）・`judge-carry-item-row-overwrite-is-a-violation`（行も blob も編集の無い品目の行の上書きは違反）・`judge-carry-item-row-overwrite-after-engine-copied-blob-is-a-violation`（エンジンが blob を写しただけの品目の行の上書きは違反）・`judge-carry-blob-decision-voided-by-row-edit-is-not-overwrite`（行を編集した後に blob を送っても上書きにしない）・`judge-carry-row-decision-voided-by-blob-edit-is-not-overwrite`（blob を編集した後に行を送っても上書きにしない）。決まった形の行は `blob-only-edit-after-serverwins-discarded-unknown-insert-uploads-row-and-blob`。
  - `5/out-of-scope-write`・`5/serverwins-wrote`：実行が範囲の外の表のサーバーの行を変えた・Versioned＋ServerWins の実行がサーバーを変えた。
  - `5/unowned-send`：追加・更新の競合か DiscardedByServerWins を報告したキーに、端末の書き込みが無かった。
  - `5/facet:server-knows`：状態の面の「サーバーが知っているか」が、モデルの観測と食い違う。
  - `5/ledger-invariant`：台帳の見張り（削除の blob の持ち分は行の持ち分に従う）。
- 乱数の種が既定の種の数のとき、照合ごとに確かめた件数が 1 件以上あることを `CoverageGuard` で表明する（`SyncModelRuntimeTests` の `GuardedRunChecks`。空振りの守り）。
  - 守る種類は、流した端末のエンジンの宣言（`CarriesServerWinsDecision`）から決める。宣言の片方のエンジンでだけ数える種類（`SyncModelRun.RunCheckCountedOnlyWhenCarrying`＝種類 → 数えるエンジンの宣言の値）は、もう片方のエンジンでは定義から 0 件なので、`SyncModelRun.GuardedRunChecksFor` が並びから除く。組の名前では除かない。
  - 宣言の値は、種ごとに世界の全端末から集め、1 回の実行で 1 種類であることを表明してから使う。
  - 宣言が偽の端末なら 11 種類、宣言が真の端末（エンジンの端末）は `carry-overwrote`・`local-discard-after-unreported-serverwins` を除いた 9 種類（`carry-held`・`carry-fulfilled` はどちらでも守る）。
- 違反にせず数えるだけにする種類（`SyncModelRun.RunCheckCountedOnlyWhenCarrying` の 7 種類。どれも消失なしの期待に触れない）：
  - 宣言が真の端末で数えるだけなのは `local-discard-after-thrown-serverwins`（例外で終わった ServerWins の実行が戻せなかった行の内容が、後の実行で報告なしに消えた。例外で送る前に止まった行が後で普通に届いた形も含む粗い目安）だけ。
  - ほかの 6 種類（`carry-overwrote`・`local-discard-after-unreported-serverwins`・`unreported-overwrite-after-thrown-serverwins`・`local-discard-unreported-in-thrown-serverwins`・`resend-after-thrown-serverwins`・`local-discard-of-delete-on-row-brought-in-by-thrown-run`）は、宣言が偽の端末でだけ数えるだけにし、宣言が真の端末では元の照合の違反になる。偽の端末で数えるだけにするのは、戻せなかった行の決定の後の形と、例外で終わった ServerWins の実行が行を書いた後で例外になる窓（その実行でその行の blob の読み出しの失敗が当たった・同期で端末が最後に見た後にほかの書き手がその行を変えていた）に当たる形だけで、窓の外の同じ形は違反のまま。
  - 例外で終わった ServerWins の実行の後の振る舞いは、決まった形の行が `WhenEngineDeclaresOp` の真の枝で表明する：`blob-thrown-serverwins-then-collect-overwrites-newer-server-blob`（例外の一覧に品目が DiscardedByServerWins で載り、次の同期は写し待ちの blob を降ろしてサーバーの新しい blob を上書きしない）・`thrown-serverwins-sync-leaves-restored-row-out-of-local-row-changes`（戻した 2 行が例外の一覧に両方載る）・`thrown-serverwins-upload-then-lww-sends-restored-row-as-edit`（続く後勝ちの同期は何も送らず、サーバーの他者の更新を降ろす）。偽の枝は宣言が偽の端末の期待で、エンジンの端末は通らない。
- 台帳の規則の守りの行：`sw-discards-insert-then-delete-on-key-taken-on-server`（ほかの書き手と同じキーで入れてすぐ消した行の記録を ServerWins が捨てたと報告する）・`sw-discards-delete-left-by-failed-run-after-recreate-elsewhere`（例外で送る前に止まった削除の記録が、後で競合と破棄として報告される）。どちらも docs どおりの報告で、`5/unowned-send` にならない。
- 外部キーの表明（`4/foreign-key:{子の表}`）：書き込みの手・一括挿入・実行（例外で終わった実行・洗い替えを含む）の前後で `PRAGMA foreign_key_check` の違反の集合を比べ、新しく出来た違反だけを数える。違反の同一性は（子の表・子の行の主キーの値・親の表・外部キーの番号）で、rowid では比べない。利用者の手（`LocalSqlOp`＝外部キーを切った専用の接続）が作った違反は数えない。前からある違反を作る手（親の無いラベル）は `LocalSqlOp` の `Untracked` を付けて流し、端末の記録に残さない。
- 対照の行 `judge-*`（`SyncModelScenarios.Judge.cs`・32 本・直結だけ）：端末のエンジンを外から壊す包み（`SyncModelJudgeBreaks.cs`）で違反の形を作り、判定役が宣言した署名の集合どおりに名指しすることを確かめる。
  - 行は `JudgeDevice`（包みと宣言）と `ExpectedJudgeSignatures`（空の集合は違反なし）を持ち、既知の失敗の一覧を通さずに、出た署名の集合と宣言の一致で判定する。起きなかった壊し方は行の誤りとして赤にする。
  - 製品の本当の振る舞いや利用者の正当な手では作らない（その形を受け入れると決まっても、対照の行が嘘にならないようにする）。
  - 照合ごとに 1 本以上：`judge-stale-overwrite-is-a-violation`・`judge-unreported-overwrite-is-a-violation`・`judge-local-discard-is-a-violation`・`judge-thrown-run-restore-is-a-violation`・`judge-carry-overwrite-is-a-violation`・`judge-carry-blob-overwrite-is-a-violation`・`judge-carry-item-row-overwrite-is-a-violation`・`judge-carry-item-row-overwrite-after-engine-copied-blob-is-a-violation`・`judge-carry-blob-decision-voided-by-row-edit-is-not-overwrite`・`judge-carry-row-decision-voided-by-blob-edit-is-not-overwrite`・`judge-carry-fulfilled-is-not-lost`・`judge-carry-edit-lost-is-a-violation`・`judge-undeclared-carry-discard-is-lost`・`judge-out-of-scope-write-is-a-violation`・`judge-serverwins-wrote-is-a-violation`・`judge-unowned-send-is-a-violation`・`judge-server-knows-facet-is-compared`・`judge-moved-behind-blob-sent-from-device-is-invented`（遅れている blob の付け替え。壊し方 `WriteLocalBeforeRun`＝エンジンを呼ぶ前に端末のローカル DB へ記録させずに文を流し、付け替え先の写し待ちを忘れさせる）。
  - 数えるだけの範囲と控えの守り：`judge-unowned-send-after-settled-record-is-a-violation`・`judge-local-discard-of-edit-on-row-brought-in-by-thrown-run-is-a-violation`・`judge-local-discard-of-delete-on-row-brought-in-without-blob-failure-is-a-violation`・`judge-resend-after-thrown-serverwins-restore-without-blob-failure-is-a-violation`。
  - 根拠の確認と持ち越し：`judge-carry-fulfilled-by-lww-serverwins-is-not-spurious`（持ち越しを宣言したエンジンが決定を果たした後勝ち＋ServerWins の決着は根拠の確認に掛けない）・`judge-undeclared-carry-lww-serverwins-settlement-without-basis-is-spurious`（対照：宣言の無いエンジンの決着は、台帳に決定があっても根拠なしなら `3/spurious`）・`judge-lww-serverwins-settlement-without-basis-is-spurious`（対照：決定の無い決着は根拠なしなら `3/spurious`）・`judge-unfulfilled-reparent-delete-refusal-without-basis-is-spurious`（対照：果たした決定に入らない付け替えは届いたとみなし、根拠の無い削除の拒否は `pending-reparent`。包みの `ReportsDeleteRefused` で削除の競合を作る）・`judge-unfulfilled-release-unique-refusal-without-basis-is-spurious`（対照：何も送らない実行が報告した一意な値の競合は、持ち主の未送信の手放しを届いたとみなして割り引き、根拠なしの `pending-release`。包みの `ReportsConflict` で競合を作る）。
  - 外部キー：`judge-foreign-key-existing-violation-is-not-counted`・`judge-foreign-key-new-violation-is-counted`・`judge-foreign-key-new-violation-in-thrown-run-is-counted`・`judge-foreign-key-swapped-violation-is-counted`・`judge-foreign-key-reinserted-violation-is-not-counted`。
- 守りの薄い点：
  - 数えるだけの窓（宣言が偽の端末）のうち blob の読み出しの失敗は行の単位で判定する。同じ実行のほかの品目が同じ状態になる形は違反になる（偽の赤の向き）。出たら、表の単位（その実行でその端末の失敗が当たり、行が blob を持つ表の行）へ広げる。
  - ダウンロードの側の窓（ほかの書き手の変更）は、普段の種にも決まった形の行にも例が無い。
- mutation 実証済み（照合と下ろす規則を 1 つずつ壊す・控えの立て方と外し方・数えるだけの窓の条件・外部キーの表明の比べ方。逆向きの規則の端末の書き込みの条件を外して編集の無い品目の行の決定も外すと `judge-carry-item-row-overwrite-is-a-violation`、逆向きの規則を blob の内容の比較で決めると `judge-carry-item-row-overwrite-after-engine-copied-blob-is-a-violation`、決定を果たしていない決着の行も果たした決定に入れると `judge-lww-serverwins-settlement-without-basis-is-spurious`・`judge-undeclared-carry-lww-serverwins-settlement-without-basis-is-spurious`、実行の前の未送信の行をすべて届かなかった行に数えると `judge-unfulfilled-reparent-delete-refusal-without-basis-is-spurious`、値の手放しの割り引き `IsReleased` を常に偽にするか根拠の判定 `HasHolder` だけ割り引きを外すと `judge-unfulfilled-release-unique-refusal-without-basis-is-spurious` が赤）。

### シナリオの操作とモデルの期待

- 決定的なシナリオは、エンジンの事実を期待の操作で表明する（操作は次元表に載せず、乱数の操作の列には混ぜない）
  - `Expect.Pending(端末)`：端末の未送信の行を、状態の面（`IModelDeviceSyncState.ReadPendingAsync`）と端末の表のいまの行から `{表} key={キー} row={present|gone}` の行（表・キーの順）にして表明する（`row` はその行がいま端末の表に在るか）。どの端末のエンジンでも同じ言葉で答えるよう、記録の数・記録の版・書いた blob の列・サーバーがその行を知っているかは載せない。
  - 決定的なシナリオは、端末の状態の表を直接読む期待を持たない。未送信は `Expect.Pending` で、ほかは振る舞いの期待（実行の結果・端末とサーバーの行・その後の同期が送るか）で書く。
  - `ExpectPayloadOp`：書き手（端末かサーバー役）の品目の blob を確かめる（例：サーバーが断った書き込みが行の書き込みで止まり blob を残さない `n6-*`。端末の行が未送信のまま残ることは `Expect.Pending` で表明する）
  - `ExpectLocalRowChangeOp`：実行が自分から変えた行の一覧（`SyncResult.LocalRowChanges`）に、表・キー・理由が載る（`thrown-serverwins-run-hides-settlement` など）
  - 端末の DB の件数やトリガーを見る手は、汎用の `ExpectLocalSqlOp`・`LocalSqlOp` で書く
  - `ModelRefreshOp.ExpectScopeRefusal`：範囲外の行が範囲内の行を参照する洗い替えが断られ、端末の DB が変わらない（`42-refresh-refused-by-rows-outside`）
  - サーバーの他者の操作は、ハーネスの `ApplyOnServerAsync` がサーバー役のリポジトリのグラフ保存での削除も行う（網羅の行のキーだけのスタブ・持っていた実体での削除）
- 例外で終わった後勝ち＋ServerWins の実行でも、モデルは実行前の未送信の行と突き合わせて捨てた行を推定する（`SyncModelRun.SyncAsync` の注入した失敗の catch から `RecordLastWriteWinsSettlementsAsync`）
  - 推定するのは、未送信の変わる印が全部消え、送るものがあり、サーバーの値が送った値でなく、ServerWins がローカルを戻したか、サーバーに行の無い upsert か、実行が DiscardedByServerWins と報告した行だけ（書き込みの履歴の最後の値で比べると、blob だけを書いた品目を「削除を書いた」と取り違える）
  - 送るものが無いのは、行ありで削除だけ・行なしで削除なし・行なしで削除ありでもサーバーがその行を知らない行（状態の面の `ServerKnowsRow` が偽＝行はサーバーに届いていない）。最後の形を推定に入れると、同じ実行のダウンロードが同じキーのほかの書き手の行を降ろしたのを決着と取り違え、根拠のない `3/spurious:*:DeleteRefusedOnServer:no-basis` になる。決まった形の行は `lww-serverwins-unuploaded-insert-delete-on-taken-key-is-not-a-settlement`
- 空の blob の期待（N12）
  - 空の blob を見逃すのは、空でない blob を書いた端末と、消された品目を上げ直した端末（同期の前にサーバーに無く後にある＝挿入の経路・`_recreators`）がすべて、その品目の競合を報告されたか ServerWins に変更を捨てられたときだけ（`_conflictsTo`）
  - blob を書いた後でその品目が削除され作り直されたら、期待値を決めない（`_blobHistoryCut`）
  - 空でない blob の書き手は、書き手がその品目を最後に消した後の書き込みだけから数える（`_blobDeletedAt`。削除の前の書き込み〔初期データを含む〕は削除と一緒に消えた）。上げ直した端末は、上げ直した後のその端末の blob が空でないときだけ数える（写し待ちの代わりの空の値のまま上げ直した端末は書き手でない）。削除の後に空でない書き手が無ければ、空でそろってよい。決まった形の行は `blob-owed-new-row-deleted-on-server-localwins-puts-back-empty-blob`（サーバーの削除で本当の blob が消えた後の LocalWins の上げ直し）。
  - 対照：`SyncModelRuntimeTests.EmptyBlobWithoutConflict_IsStillAViolation`（blob を運ばない実行で上げた新しい行の空の blob は違反）・`EmptyBlobOfRecreatedRowWithoutConflict_IsStillAViolation`（blob を運ばない後勝ちで作り直した行。上げ直した端末の blob は空でないので、数え続ける）
- 遅れている blob と付け替え（`SyncModelRun.BlobBehind.cs`。規則の正本は先頭の remarks）
  - 台帳の控え「遅れている blob」：端末の行はサーバーから届いたが blob の写しがまだの品目。端末の実行の終わりに、端末とサーバーの blob が違い、持ち分が無く、(a) 降ろした行の blob を写す実行（`RunFrame.CopiesBlobs`）の取得がその品目を返し、端末の行がサーバーと同じになったか、(b) 版のガードを外した上書きがその品目の行を届けたときに立てる。取得が返した品目は、サーバー役の取得の口（`SyncModelFaults.AfterFetchAsync`。SQLite の代役・実 SQL Server の包みのどちらも通す）が `SyncModelFaults.FetchedItemsSince` に控える。写しの失敗は最初に読んだ別の品目で当たることがあるので、「その品目の読み出しの失敗」では見落とす（夜間の種 555・763）。状態の表は読まない。
  - 付け替え：遅れている blob の行を動かすと、付け替え先の blob の履歴と持ち分の内容は、付け替えの時点のサーバーの写す元のキーの blob（送るべき blob）にする。その後のほかの書き手の写す元のキーへの書き込み・削除も付け替え先へ写す。照合 3 は、送るべき blob がまだサーバーの写す元のキーにあれば消えたとみなさない（`AwaitsBehindCopy`）。
  - 守り：対照の行 `judge-moved-behind-blob-sent-from-device-is-invented` と、決まった形の行 `rekey-*-owed-*`・腕ごとの行（写した後の付け替え・付け替えの前の端末の書き込み・上書きの後の写し待ち・写しが試されなかった行）。mutation 実証済み（取得の条件・上書きの条件・付け替え先へ端末の値を積む・端末の書き込みで控えを外さない・写す元のキーの書き込みと削除を写さない・持ち分の内容を送るべき blob にしない・実行の終わりにそろっても外さない・照合 3 の免除をいつも真にする〔対照の行 6 本が赤〕・写す元を確かめずに免除する、のそれぞれで赤）。

### HTTP・実 SQL Server の派生

- `SyncModelHttpRuntimeTests`：in-process の Kestrel・生成された HTTP の差分ソースと同期専用のエンドポイント。サーバー側の注入した失敗は 500 で届くので、実行中に失敗が起きたかで見分ける。
- `SyncModelSqlServerRuntimeTests`（Docker）：世界ごとにデータベースを作る実 SQL Server のサーバー役。生成された直結の差分ソース・本物の rowversion。
- `SyncSupportRemoteAdoSqlServerRuntimeTests`（Docker・転送の値 `SqlServerHttp`）：同じ実 SQL Server のサーバー役を in-process の Kestrel の背後へ置き、生成された HTTP の差分ソースでつなぐ。決定的なシナリオは夜間だけ（上の「決定的なシナリオと Theory」）、乱数の種は普段も流す。名前は行列の名前の規則に従う（同期のモデルの仲間の `SyncModel…` は名前の既知の一覧 `RuntimeTestNamingKnown` に載っている）。
- 実 SQL Server のサーバー役は、生成物の外側の包み（`SyncModelFaultingRepository`・`SyncModelFaultingSource`・`SyncModelFaultingBinaryColumns`）で、SQLite の代役と同じ点の失敗の注入と割り込みを起こす（版の採番とガードはしない）。版なしの表（ラベル）の書き手は代役と同じく包まない（書き込みの失敗をラベルの書き込みで使わない）。入口の割り込みと書き込みの失敗は `SyncModelFaults.BeforeServerWriteAsync` の 1 つを両方の包みが呼ぶ。
- どれも端末はエンジンの端末（`SyncModelEngineDevice`）で、次を確かめる。
  - 決定的なシナリオが直結と同じ違反になること。例外は入口・違反の種類・推定した形へ正規化して比べる（`SyncModelSignatures`）。直結の側は `SyncModelDirectRuns` の結果。
  - 乱数の種が既知の失敗（`SyncModelEngineTransports.Known`・空で始める）にしか当たらないこと。種の数は `QUICKER_SYNC_MODEL_TRANSPORT_SEEDS`（既定 10・夜間 200＝`SyncModelSignatures.NightlySeedCount`）と、広い世界の種の `QUICKER_SYNC_MODEL_TRANSPORT_WIDE_SEEDS`（既定 4・夜間 40＝`SyncModelSignatures.NightlyWideSeedCount`）。夜間の数は、100＋20 で流した 3 夜（2026-11-24・25）の実測から倍にした（`tests/QuickER.Tests/TestSupport/CLAUDE.md`「夜間の深い実行」）。乱数の種の Fact はどれも夜間の目印を持つ（`NightlyEngineTrait.Selected`）。網羅は観測と要約だけで、網羅しない組は直結の乱数の種で照合する。実 SQL Server の 2 つのクラスは、どちらの範囲も種の組の余り 0・1 の種（失敗の注入なし）と余り 2・3 の種（失敗の注入の組）を別の Fact で流す（`SyncModelSignatures.SqlServerSeeds`・`SqlServerFaultSeeds`）。
  - 種の番号は続いた番号から取らない（生成器はモード×ポリシーを種を 4 で割った商で 6 組へ順に配るので、続いた 10 種は 3〜5 組しか作らない）。HTTP は開始番号から 5 刻み（`SyncModelSignatures.HttpSeeds`）、実 SQL Server は j 番目が 4j＋(j を 2 で割った余り)（失敗の注入の組は＋2）。`EngineFingerprintDriftTests.SyncModel_TransportSeedsAreRecorded` が、普段と夜間の数のそれぞれで、転送の種が直結の同じプロフィールの種の範囲に入ることと、モード×ポリシーが min(6, 種の数) 組・種の組が取りうる数だけそろうことを、生成器のコンストラクタだけで確かめる。普段の広い世界の 4 種が作らない 2 組は作らない形の宣言（`transport-wide-seed-mode-policy`）。
- SQL Server でだけ起きる既知の失敗は `SyncModelEngineTransports.SqlServerOnly`（空で始める）に置く（HTTP × 実 SQL Server も同じ一覧で比べる）。形は `SyncModelSqlServerRuntimeTests.SqlServerOnly_AreWellFormed` が見る。
- 既知の失敗は `SyncModelKnownFailures` の厳格な一覧（載っているシナリオはその署名の組どおりに失敗しなければ赤・載っていない失敗も赤）で一時的に外し、直したら消す。直結の一覧は `SyncModelKnownFailures.EngineSet`（空。項目を足すときは、エンジンの端末で出た失敗から理由つきで足す）。一覧の形は `SyncModelRuntimeTests.KnownFailures_AreWellFormed` が見る。

### 同期の構成の行列

- `SyncSupportConfigMatrixRuntimeTests`（登録簿 SyncModel/Config・`SyncConfigDimensions`・Docker）は、組める組の全積 238 行を流す（6 因子の水準の全積 480 から、原理的に組めない組＝AddGeneratedSyncSupport × HTTP（HTTP/2 の水準を含む）・キーなしのローカル × キーなしの直結・キーを決めるフック × 直接の挿入を外した数。どちらのフィクスチャもリモートサービスを持つので HTTP の行も両方にある）。軸は次のとおり。
  - 主キーの型：行列の軸 `SyncConfigFixtures`（IntKey＝SyncFixture・VoKey＝SyncVoFixture）。
  - ローカルの登録の形（因子 LocalKey）：キーつき・キーなし・キーなしの登録の後でローカルの接続ファクトリを同じ接続文字列のインスタンスの登録（シングルトン）で登録し直した形（`UnkeyedInstance`。同期の登録より前に登録し直す）。
  - サーバーの差分ソース：直結の keyed / 非 keyed・HTTP の baseAddress / HttpClient ファクトリ / HttpClient ファクトリで HTTPS のサーバーへ HTTP/2 で送る形（`HttpClientFactoryHttp2`）。
  - AddGeneratedSyncSupport か分けた登録（差分ソースと `AddGeneratedSyncEngine`）。
  - キーを決める Before フックの有無。
  - 書き込みの経路（因子 Via）：直接の挿入・グラフ保存・`GraphRemoveParentWithNewChild`・`GraphViaExcludedParent`。
- 同期の登録は、ローカルの接続ファクトリを開くたびの確認つきの包みに差し替えて `SyncEngine` を組む。ローカルの書き込みは同期する表のトリガーが記録し、未送信はエンジンの未送信の一覧で読む。トリガーと状態の表は包んだ接続ファクトリの最初の接続で張られるので、ローカルの DB にはスキーマだけを先に作り、行は包んだ接続（リポジトリ）でだけ書く。
- 流れは「登録 → 解決 → 登録の形の検査 → 新しい行を同期 1 回で上げる → サーバーの新しい行を 1 回で降ろす → 洗い替え 1 回で両方の行がそろう」。
  - 解決はスコープを検査するプロバイダ（`ValidateScopes = true`）の、行ごとのスコープの中で行う（シングルトンがスコープのサービスを掴む登録の形を断る）。
  - 洗い替えの段は登録の形ごとに洗い替えが例外なく通り、両方の行がローカルにそろうことを見る（今の 2 つのフィクスチャは木の図。表どうしの循環は同期のモデルの決定的な行 `refresh-*-foreign-key-cycle*` が 4 つの転送で見る。図の形の軸は足していない）。
  - 登録の形（`SyncConfigFixtures.RegistrationProblems`）：ローカルの接続ファクトリは開くたびの確認つきの包み（`ObservedSqlConnectionFactory`）／ローカルの注文と明細のリポジトリは SQLite の側の生成の Repository の型そのもの（何にも包まない。構築時に除外した親の表も）／差分ソースは転送経路の型（HTTP は HttpSyncServerSource・直結は DirectSyncSource）／サーバーのリポジトリは SQL Server の側の生成の Repository の型そのもの・サーバーの接続ファクトリは包まない／ローカルの接続を 1 回開いた後、トリガーは除外していない同期する表にだけ在る。リポジトリは生成の型と解決した型が同じかで見る（包みで登録すると食い違う）。
  - 2 回目の同期が降ろすのはその 1 行だけ（表ごとの再開点）。
  - `GraphRemoveParentWithNewChild` は、新しい親をグラフ保存で書いて同じ流れを通した後に段を 1 つ足す：上げ下げした 2 つの親に、キーの無い新しい子を足して親を消すグラフ保存（単数と複数の SaveAsync）と、新しい子だけを MarkRemoved したグラフ保存を流し、未送信は 2 つの親の削除だけになり（新しい子は DB のどの行も消さないのでトリガーが記録しない）、後勝ちの同期 1 回でサーバーから親が消えて未送信が残らないことを見る。IntKey は消す親の下に、サーバーに同じキーの行がある新しい版なしの子（`sync_notes`）も入れ、サーバーのその行が残ることを見る。
  - `GraphViaExcludedParent` は、親の表を `excludeFromSync` に入れた登録（Support・Split の両方）で、同じ流れを同期する子の表（明細）で流す。親はテストがサーバーとローカルへ直接入れ、除外した親の直接の挿入と更新が未送信に何も残さないこと、除外した親を書き換えて子を足すグラフ保存が子の表だけを未送信にすること、子が同期 1 回で上がりローカルに残ることを見る（未送信の一覧はエンジンの表の行だけを返すので、除外した表に記録の仕組みが無いことは登録の形の検査のトリガーの表明が受け持つ）。
  - サーバーの DB は全部の行で共有するので、この 2 つの水準はサーバーに足した補助の行を行の終わりに消す（後の行の最初の同期と洗い替えが、親の無い行を降ろさないようにする）。キーは行ごとに 3 つ（ローカルの新しい行・サーバーの新しい行・補助の親）。
  - HTTP/2 の水準（42 行）は、フィクスチャごとに 1 つの HTTPS の Kestrel（`TestHttpsCertificate`＝テストの中で作る自己署名の証明書・Kestrel の既定のまま HTTP/1.1 と HTTP/2 を ALPN で交渉する）を共有し、HttpClient のファクトリが HTTP/2・`RequestVersionOrLower` の HttpClient（`TestHttpsCertificate.CreateHttp2Client`）を返す。洗い替えの後、親を消す段より前に、ローカルの新しい行（ローカルのリポジトリ）とサーバーの新しい行（サーバーのリポジトリ）に親の表の除外した無制限バイナリ列を書き、`IncludeUnboundedBinary` の同期 1 回でそれぞれの側が相手の内容を持つことを見る（親の表を除外した `GraphViaExcludedParent` の 12 行は、同期する表に除外列が無いので流さない）。行の終わりに、その行の間に HTTPS のサーバーへ届いた要求（外側のミドルウェアが記録する `SyncConfigRequestLog`・共有のサーバーなので行の前の件数から後ろだけを見る）がどれも `HTTP/2` で、同期の専用のエンドポイント（`SyncCeiling`・`SyncChanges`・`SyncKeys`）・CRUD と保存の書き込み・（blob の段を流した行では）blob の PUT と GET を含むことを確かめる（`SyncConfigFixtures.Http2Problems`）。
- 既知の失敗は KnownCaseLedger で、今は空。行の由来の指摘は行に残す。
- 網羅しない組の一覧は空。VoKey×HTTP は SyncVoFixture にリモートサービスを足して流す。
- mutation 実証済み。

### 割り込みの点

- 同期・洗い替えの実行の名前つきの時点（`ModelInterruptPoint`）で、実行と別の流れから書き込みを 1 回差し込む。書き手（`SyncModelScenarios.InterruptWriter`）はその端末（ローカルの書き込み）かサーバーの他者（サーバーへの書き込み）で、1 つの割り込みに書き込みを順に並べられる。操作は `ArmInterruptOp`、仕組みは `SyncModelFaults.InterruptAsync`（呼ぶのはサーバー役の包み＝SQLite の代役と実 SQL Server の両方）。
  - 時点：サーバーへの行の書き込みの直前／差分・ページ・キーの取得の直前／取得が 1 行以上を返した後でエンジンがそのバッチを適用する前。
  - 別の流れは `ExecutionContext.SuppressFlow` の下の `Task.Run`。実行の流れの状態を引き継がない（アプリの別の流れの書き込みと同じ）。
- 点はどれも、エンジンが端末の DB のトランザクションを開いていない時点（サーバーを待つ間）に置く。端末の割り込みの書き込みは端末の DB の書き込みのロックを待つので、適用のトランザクションの中に置くと待ち合って止まる。上限 30 秒で待つのをやめ、`interrupt/deadlock` の違反にする。端末の書き手もサーバーの他者も、同期と洗い替えのどちらにも差し込む（洗い替えはサーバーへ書かないので、サーバーへの書き込みの直前の点には来ない）。
- 割り込みの書き込みは通常の書き込みと同じ経路（`SyncModelRun` の `WriteAsync`）で、書き手の操作として履歴へ積む。
- 仕掛けたのに点に来なかった割り込みは `interrupt/not-fired:{点}` の違反にする（黙った空振りの守り）。
- 決まった形のシナリオが仕掛けたのに起きなかった失敗（`ArmFaultOp` の種類・空の取得・フックの blob の書き込み）も、シナリオの終わりに `fault/not-fired:{種類}` の違反にする（`SyncModelFaults.PendingFaults`・`FinishAsync` の `requireArmedFaultsFired`）。失敗の口がサーバー役から外れても結果が同じになる行（失敗の後のやり直し・根拠の判定の after-fault の行など 29 行）が、黙って緑になるのを防ぐ。乱数の種と網羅の行は確率で仕掛けるので対象外。サーバー役の包み（SQLite の代役・実 SQL Server）の入口を外すと、失敗の口を使う行すべてが `interrupt/not-fired` か `fault/not-fired` で赤になる（mutation 実証済み）。
- 端末の書き手の行は点 3 × ポリシー 2（Collect・ServerWins）× 前段 4（更新・削除→作り直し・削除・blob の書き込み）× 割り込みの書き込み 3（更新・削除・作り直し）のうち起こり得る 35 行＋フォルダーの `d1-folder-recreate-delete` の 1 行。
  - 外す組は原理的に起こり得ないものだけ：行が無いのに更新・削除する組・行があるのに作り直す組（`SyncModelScenarios.IsPossibleInterruptShape`）と、ServerWins × サーバーへの書き込みの直前（Versioned＋ServerWins はサーバーへ書かない＝`IsPossibleInterruptPoint`）。
  - Collect の行の名前は `interrupt-{点}-{形}`、ServerWins の行は `-serverwins` を付ける。ServerWins の行はサーバーの他者が別の品目を変え、取得が 1 行以上を返す点にも来るようにする。
  - `interrupt-BeforeServerWrite-delete-recreated`・`interrupt-BeforeServerWrite-recreate-deleted`（計画した削除を送る前の作り直し）・`interrupt-*-update-blob-written-serverwins`（ServerWins の実行中の blob を書いた行の更新）・`interrupt-*-recreate-deleted-serverwins` もこの網の行。
- 端末の書き手の洗い替えの行は 22 行。洗い替えはサーバーへ書かないので、点はサーバーへの書き込みの直前を外した 2 つ（`SyncModelScenarios.IsPossibleRefreshInterruptPoint`）。洗い替えは競合ポリシーを読まないので、ポリシーの軸を持たない。
  - 前段ありの `Force` の洗い替え（宣言 `SyncModelScenarios.RefreshForceInterruptCells`・名前 `interrupt-{点}-{形}-refresh-force`）：同期の行と同じ 7 つの形 × 点 2 ＝ 14 行。前段の未送信があると `Force` なしの洗い替えは取得の前に断るので、割り込まれる実行は `Force` の洗い替え（blob を運ぶ）。品目 701 の編集と品目 702 の前段の後、702 への割り込みの書き込みを点に仕掛ける。701 はサーバーの行で置き換わって未送信が外れ、割り込みの書き込みで連番の進んだ 702 は前段と割り込みの書き込みの後の端末の内容（blob を含む。削除なら行が無い）のまま未送信で残る。洗い替えの結果の捨てた数は実際に捨てた 701 の 1 つだけ（`discarded-changes=1`）を表明する。
  - 前段なしの `Force` なしの洗い替え（宣言 `SyncModelScenarios.RefreshCleanInterruptCells`・割り込みの書き込みは `SyncModelScenarios.InterruptCleanWrite`・名前 `interrupt-{点}-{update|delete|insert|delete-gone-on-server}-clean-refresh`）：4 つの書き込み × 点 2 ＝ 8 行。未送信の無い端末で洗い替えを流し、途中で端末が 702 を更新・削除するか、サーバーに無いキー 705 の品目を作るか、洗い替えの前にサーバーの他者が消しておいた 702 を消す。洗い替えは断らずに終わり（`discarded-changes=0`）、サーバーに無い 705 を消さず、サーバーにも端末にも無い 702 の未送信の削除の状態も消さない。
  - どちらも、洗い替えの直後に途中で端末が書いた品目が端末の内容のまま未送信で残り（`Expect.Pending`・行・blob）、続く Collect の同期がその内容を競合なしに上げて未送信が無くなることを表明する。
  - 小さい塊の端末では、この 22 行の洗い替えは 1 回で全部の道を通る（割り込みが、塊へ入るかの判定より先に未送信を書くため）。
- サーバーの書き手の行（`SyncModelScenarios.ServerInterruptCells`）は点 2（取得の直前・取得の後で適用の前）× ポリシー 2 × 前段 3 × 割り込みの書き込み 2（更新・削除）× 実行の種類 2（同期・洗い替え）× 後段 2（次の同期・編集してから同期）× その後の失敗 2（なし・次の取得の失敗）のうち起こり得る 80 行。名前は `interrupt-server-{点}-{前段}-{書き込み}-{後段}[-fetch-fails][-serverwins][-refresh]`。
  - 前段：`parent-after-child-selfref`（端末に無いフォルダー p とその子 c。バッチで c が p より前に来る）・`parent-after-child-cross-table`（対照：p と p の下の品目。親の表が先に降りる）・`unique-holder-after-taker`（端末のフォルダー h が名前を手放し、別のフォルダーが取り、h をもう一度更新する。バッチで値を取る行が h より前に来る）。
  - 削除の書き込みは、サーバーの外部キー（NO ACTION）が受け付けるよう、子フォルダーを null の親へ・品目を別のフォルダーへ移してから対象の行を消す。
  - 外す組：サーバーへの書き込みの直前の点（端末が送るものの無い形なので来ない）と、削除 × 編集してから同期（消した行は端末で編集できない）と、次の取得の失敗の窓が無い組。`SyncModelScenarios.IsPossibleServerInterrupt`。
  - 次の取得の失敗（`ServerInterruptFault.NextFetchFails`）は、割り込みの書き込みの後でその端末に取得の失敗を仕掛け（`ArmInterruptOp` の `ThenArm`）、割り込まれた同期のバッチの大きさを 2 にする。同期は取得の後の点のバッチ（割り込みより古い写し）を取得してから、同じ表の次のバッチの取得で例外になる（`ExpectThrowOp`）。この水準は窓がある組（取得の後の点・更新・同期）にだけある：取得の前の点は割り込みの後に取得したバッチが新しい写しを持ち、削除は対象の行が残らず、洗い替えは同期と別の経路。
    - 次の取得の失敗 × 前段 `parent-after-child-*` × 編集してから同期も起こり得ない組として外す：対象の行（フォルダー 800）は前段でサーバーに作られて実行の前の端末に無く、取得が失敗した 1 回で全部の道の実行は何も適用しないので、実行の後も端末に無く編集できない（前段 `unique-holder-after-taker` の対象の行 820 は初期データにあって前から端末に在る）。この外し方は「取得が失敗した 1 回で全部の道の実行は何も適用しない」という仕様に依り、仕様は `table:SyncEngineFixed` の行 `sync-fetch-failure-leaves-device-as-before`・`refresh-fetch-failure-leaves-device-as-before` が守る。塊を確定した後に次の取得が失敗する窓は塊ごとに確定する経路にだけあり、`table:SyncEngineFixed` の行 `chunk-failure-between-chunks-keeps-first-chunk-and-next-sync-resumes`・`chunk-failure-after-carry-keeps-resume-point-before-carried-row`・`chunk-stale-row-then-fetch-failure-then-device-edit-collect-reports-conflict`・`chunk-stale-row-then-fetch-failure-then-device-edit-serverwins-restores` が持つ。
  - 後段の期待は期待の操作で表明する（汎用の「消失なし」は、端末の編集がサーバーの新しい内容を競合なしに上書きする形を拾えない）。取得の後の点の更新は、次の同期が内容ごと降ろす（downloaded=1）・Collect の編集は ModifiedOnServer の競合。取得の前の点は、次の同期は何も降ろさない・Collect の編集はそのまま上がる。ServerWins の編集は点に依らず捨てられてサーバーの内容に戻る。削除は次の同期の後に対象の行が端末に無い。次の取得の失敗の行は、降ろした数が前段で違うので、降ろした数を見ずに内容で見る。
  - 書き手ごとの行の宣言（端末の書き手の同期の 35 行・洗い替えの 14 行と 8 行・サーバーの書き手の 80 行）が起こり得る組をすべて含み、行の同期がそのポリシーで流れ、サーバーの書き手の行の割り込まれる実行が宣言の種類であることは `SyncModelRuntimeTests.InterruptScenarios_CoverEveryPointAndShape` が表明する（外す規則はテストの中で書き下ろす）。
  - この網の行は 4 つの転送すべてで流れる（HTTP × 実 SQL Server は夜間）。洗い替え × `unique-holder-after-taker` × 更新 × 取得の前の点の 4 行は、期待の手 `WhenServerFixesCeilingOp` の固定する側の枝で「洗い替えの直後の端末に対象の行の新しい内容がある → 次の同期は何も降ろさない（downloaded=0）」を表明してから、同じ後段へ進む（上限を実行の始めに固定するサーバー役では割り込みの版が洗い替えの最初の取得に入らないが、エンジンは表を空にしないので、値を取る行がまだ値を持つ対象の行で保留され、上限を取り直したもう 1 回の取得が新しい版を同じ洗い替えに入れる）。
- 割り込みは乱数の操作の列に入れない。生成器は毎歩実際の DB を読んで操作を選ぶので、派生の乱数で割り込みを選んでも既存の種の列が変わる。入れるなら別の種の列にする。

### エンジンの端末

- 端末 `SyncModelEngineDevice`：トリガーでローカルの変更を記録し（`SyncLocalTracking.Ensure`）、開くたびの確認（`SyncLocalTrackingGuard`）で包んだ接続ファクトリ（`ObservedSqlConnectionFactory`）から素の生成 Repository 4 つ（何にも包まない）と `SqliteLocalRowStore` を作り、4 表の `SyncTable` で組んだ `SyncEngine` で同期する端末。同期のモデルの端末はこの 1 種類で、世界の組み方の端末の作り方の既定（`SyncModelWorldOptions.CreateDevice`＝`SyncModelEngineDevice.CreateAsync`）。
  - エンジンの宣言 `CarriesServerWinsDecision` は真。
- 決定的なシナリオは、直結・HTTP・実 SQL Server の Theory と HTTP × 実 SQL Server の夜間の Fact がこの端末で流す（上の「決定的なシナリオと Theory」）。
- クラス `SyncSupportAdoSqliteEngineRuntimeTests`（直結・コンテナなし）が流すもの：乱数の種・小さい塊の端末（下の節）・決まった形の行（下の節）。決定的なシナリオを 1 回で全部の道の端末で流すのは直結の Theory（`SyncModelRuntimeTests.Scenario_FailsExactlyAsListed`）。
  - 乱数の種（`SyncSupportAdoSqliteEngineRuntimeTests.RandomSeeds_ConvergeWithoutLoss`）：本体 `SyncModelRuntimeTests.RunRandomSeedsAsync` に、組 `SyncModelRandomSuite`（世界の作り方・種の決め方・既知の失敗の一覧・網羅しない組・空振りの守りの並び）を渡して流す。
    - 直結の組は `EngineSuite`：既知の失敗の一覧は `SyncModelKnownFailures.EngineSet`（空）、網羅しない組は範囲 `SyncModelDimensions.EngineScope` の `SyncModelDimensions.EngineGaps` と `SyncModelOutcomeDimensions.EngineGaps`（空）、空振りの守りは `SyncModelRuntimeTests.GuardedRunChecks` と夜間だけの `carry-edit-lost`。
    - 種の指紋の記録は `SyncModelRuntimeTests.Seeds`・`WideSeeds` の決め方で照合する（指紋は生成器の設定と最初の形の中身から作り、端末に依らない）。
    - 夜間の目印を持つ（`NightlyEngineTrait.Selected`）。種の決め方は組が持ち（`SyncModelRandomSuite.Seeds`・`WideSeeds`）、直結の組は `SyncModelRuntimeTests.Seeds`・`WideSeeds` そのもの（夜間は 1200＋広い世界 240）。
    - 種を全部のコアで並列に流すので、流れている間は同じジョブのほかのテストも遅れる。夜間の 3 夜（2026-11-24・25）は 400＋広い世界 80 の種で 1.1〜1.7 分で、今の数では約 3 倍（遅いランナーで約 5 分）に伸びる。伸びる分とほかのテストの遅れを足しても、integration-nightly-rest の段は目安の 50 分に余裕を残す（`tests/QuickER.Tests/TestSupport/CLAUDE.md`「夜間の深い実行」）。
- 直結のほかの転送の乱数の種は、転送のクラス（`SyncModelTransportRuntimeTests.cs`）が本体 `SyncModelSignatures.RunSeedsAsync`（種の決め方・正規化）にエンジンの端末の世界の作り方を渡して流す（上の「HTTP・実 SQL Server の派生」）。
  - `SyncModelHttpRuntimeTests.RandomSeeds_OnlyKnownFailures`（HTTP・SQLite のサーバー役・コンテナなし）。
  - `SyncModelSqlServerRuntimeTests.RandomSeeds_OnlyKnownFailures`・`SyncModelSqlServerRuntimeTests.RandomFaultSeeds_OnlyKnownFailures`（実 SQL Server・Docker・`SqlServerSecondContainerCollection`。失敗の注入なしの組と注入の組）。`SyncSupportRemoteAdoSqlServerRuntimeTests` の同じ名前の 2 つ（HTTP × 実 SQL Server）。
  - 既知の失敗の一覧は `SyncModelEngineTransports.Known`（空で始める）。夜間の数は 200＋広い世界 40。
- 洗い替えの振る舞いは、端末の状態の表を読まない期待（実行の結果・例外の型・端末とサーバーの行・その後の同期が送るか）で書いた行が受け持つ。
  - `refresh-pending-refused-keeps-edit-that-next-sync-sends`（未送信があると断り、編集は残って次の同期で送られる）
  - `refresh-force-discards-pending-and-next-sync-sends-nothing`・`refresh-cycle-force-replaces-pending-and-next-sync-sends-nothing`（`Force` は未送信を捨て、次の同期は何も送らない。表どうしの循環のある図でも同じ）
  - `refresh-excluding-referring-child-refuses-and-next-sync-sends-edit`（範囲の外の子が範囲の行を参照していれば断る）
  - `refresh-written-rows-are-not-sent-by-next-sync`（洗い替えが書いた行を、次の同期が送らない）
  - `refresh-force-forgets-row-gone-on-both-sides-and-same-key-is-inserted`（端末とサーバーの両方で消えた行のキーで作り直した行が、挿入として上がる）
  - `refresh-refusal-order-outside-reference-before-pending`・`refresh-refusal-order-blob-loss-before-outside-reference`（洗い替えの拒否の順＝blob の損失 → 範囲の外からの参照 → 未送信）
  - `blob-refresh-discard-then-sync-does-not-send-empty-blob-back`・`blob-refresh-discard-empties-blob-of-row-written-after-retry`（blob を捨てる洗い替えの後、端末の blob が残らない。取り直しで書いた行も。後ろの行は、取得の途中にサーバーの他者が品目を新しいフォルダーへ移す形で、上限を固定するサーバー役では移した版が洗い替えに入らず次の同期で降りる＝`WhenServerFixesCeilingOp`）
  - `blob-refresh-discard-leaves-no-blob-on-row-changed-after-ceiling`（取得の直前にサーバーで変わった行：上限を固定するサーバー役では洗い替えに入らず次の同期で内容が降り、固定しないサーバー役では洗い替えで降りる。どちらでも端末の blob は残らない＝`WhenServerFixesCeilingOp`）
- 保留があるときのもう 1 回の取得は、行 `fk-parent-created-between-table-fetches-arrives-in-same-sync`（全転送）が持つ：フォルダーの表の取得の後・品目の表の取得の前にサーバーの他者が新しいフォルダーとその下の品目を作り、取得の上限を固定しないサーバー役ではその 1 回の同期で両方が入って打ち切りが残らない（エンジンは保留した子を同じ実行のもう 1 回の取得で取り直す）。固定するサーバー役では `WhenServerFixesCeilingOp` で「その同期には入らず、次の同期で入る」を表明する。
- mutation 実証済み（洗い替え：未送信の拒否を外す・`Force` で捨てない・範囲の外の参照の拒否を外す・拒否の順を入れ替える・エコーを飛ばす・サーバーに無い行を消さない・両方に無い行の状態を消さない・blob を空にしない・空にした表の確かめを外す、のそれぞれで対応する行が赤。同期：保留があるときのもう 1 回の取得を外すと `fk-parent-created-between-table-fetches-arrives-in-same-sync`、写し待ちの列を端末が書いた後も写す〔写す前と書き込みのロックの中の 2 つの確かめを両方外す〕と `blob-owed-copy-keeps-unsent-local-blob` が赤）。

### 小さい塊の端末

同期のエンジンの塊ごとに確定する経路（仕組みは `src/QuickER.Runtime.Sync/CLAUDE.md`）は、エンジンの internal の値を小さくした端末で、同期のモデルをそのまま流して確かめる。

- 端末：`SyncModelEngineDevice.OnSmallChunks(世界の作り方, 道の数え手)`。エンジンの端末と同じ部品で、塊へ入る閾値を 2 行・塊の大きさを 2 行にする（`SmallChunkThreshold`・`SmallChunkSize`）。塊の経路のトランザクションの間の隙間（`ChunkWriteGap`）は 0 にする（2 行の塊はトランザクションが多く、隙間を置くと照合が長くなる。隙間は結果を変えず、決まった形の行 `chunk-write-transactions-leave-gap-for-other-writers` が見る）。未送信の無い 3 行以上の取得（同期・受信だけ・洗い替え。洗い替えは `Force` で捨てる行を未送信に数えない）が塊の道を通り、未送信のある実行と小さい取得は 1 回で全部の道を通る。
  - 道の数え手 `SyncModelDownloadRoutes` は、エンジンの internal の口（`DownloadRouteObserved`・`CarriedRowsApplied`）につないで、道ごとの実行の数と、最後の段が持ち越した行の数を数える（スレッドをまたいで足す）。
- 1 回で全部の道との照合（共通の本体 `SyncModelSmallChunkScenarioRuns.AssertMatchesDirectAsync`）：転送の Theory が流す行（`SyncModelScenario.RunsOn`）を、その転送の世界の小さい塊の端末で流し（`SyncModelScenarioLedger.RunAsync`・既知の失敗の一覧 `SyncModelKnownFailures.EngineSet`。結果は `green`・`known`・`differs` の 3 つ）、`green`・`known` でない行を、塊の道で結果の違う行とする。直結の Theory は同じ行が 1 回で全部の道の端末で一覧どおり（空＝緑）であることを、転送の Theory は直結と同じ違反になることを表明するので、塊の道の結果が 1 回で全部の道と同じなら違う行は無い。
  - 転送は 3 つ：直結（`SyncSupportAdoSqliteEngineRuntimeTests.SmallChunkScenarios_MatchDirect`）・HTTP × SQLite のサーバー役（`SyncSupportRemoteAdoSqliteSmallChunkRuntimeTests`）・実 SQL Server（`SyncSupportAdoSqlServerSmallChunkRuntimeTests`・Docker・簡易 DSL の総当たりと同じ `SqlServerDslFuzzContainerCollection`＝種を使わず夜間に流れないので、普段の CI で空きのあるコレクションに置く。理由と実測はコレクションの remarks）。塊の経路は取得のページを流しながら塊を確定するので、取得の上限を実行の始めに固定するか（`ISyncModelServerRole.FixesFetchCeilingAtRunStart`）と、HTTP のページ分けと再開点の Base64 の往復に依る（転送に依らない古い読みの見分け・未送信を捨てる口は、作らない形の宣言 SyncModelNotGenerated の項目 stale-entity-saves・pending-discard に根拠を書いた）。HTTP × 実 SQL Server は流さない（実 SQL Server の上限の固定と HTTP のページ分けは、それぞれの転送が持つ）。
  - 違う行は、理由の宣言 `SyncModelSmallChunkScenarioDifferences` と転送ごとに 2 方向に照合する（宣言の無い違う行も、宣言があるのに 1 回で全部の道の実行と同じ結果の行も赤）。どの転送でも違う行は `All`、1 つの転送でだけ違う行は `TransportOnly`。理由には、塊の経路が違う答えを出す仕組み（削除の段が塊より前・キーを行より先に取る順・塊ごとの確定・取得の上限の固定）を書く。今の宣言は `All` の 1 行（`chunk-delete-stage-parent-gone-after-next-sync`＝削除の段で残した親が次の同期まで端末に残る）。
  - 空振りの守り：塊の道を通った実行のある行が 0 行なら赤。印 `SyncModelScenario.CarriesAcrossChunks` のある行は、持ち越しが 1 行以上起きなければ赤。
- 乱数の種（`SyncSupportAdoSqliteEngineRuntimeTests.SmallChunkRandomSeeds_ConvergeWithoutLoss`）：直結の組 `EngineSuite` と同じ本体に、組 `SmallChunkSuite`（端末の作り方・夜間の数・夜間だけの空振りの守りだけが違う）を渡す。生成器・次元表・作らない形の宣言・種の指紋の記録は直結の組と共有する（新しい生成器の部分を持たない）。
  - 普段の数は直結の組と同じ（60＋広い世界 48）。夜間の数は普段の 4 倍（240）で、広い世界の種は普段と同じ 48。
    - 理由：この組の種も並列に流れてランナーを使い切る。夜間の Integration のジョブが目安の 50 分を超えていた間は普段の数のままにし、ジョブを 2 つに分けた後の 3 夜（2026-11-24・25）で integration-nightly-rest の段が目安の内（13.1〜21.1 分）に戻り、この組が 0.5 分だったので上げた（約 1.3 分の見込み）。塊の経路の形は、普段の CI で直結の Theory との照合が全行を流す。
  - 既定の種の数のとき、道 Single と Chunked がどちらも 1 回以上起きたことを `CoverageGuard` で表明する（Refused は乱数の種では起きず、決まった形の行 `chunk-download-route-cells` が持つ）。
  - 夜間だけの空振りの守り（`NightlyGuardedRunChecks`＝戻せなかった行の決定の後の編集の照合）は、この組は持たない。その照合が起きるのは未送信の行がある実行だけで、未送信のある実行は小さい塊の端末でも 1 回で全部の道を通る。守りは直結の組が持つ。
- 塊をまたぐ形の決定的なシナリオ（`SyncModelScenarios.Engine.LargeDownload.cs`・全転送。小さい塊の端末で形の両端が別の塊に入るよう、詰め物の行と版の順を決めてある）：
  - `chunk-selfref-parent-in-later-chunk`（子の版が親より古い）・`chunk-cross-table-cycle-both-new`（フォルダーが品目をピン留めし、品目はそのフォルダーの下）・`chunk-selfref-cycle-across-chunks`（親子の循環）・`chunk-unique-swap-across-chunks`（一意な値の入れ替え）・`chunk-taker-before-releaser`（後ろの塊の行が手放す値を前の塊の行が取る）。この 5 行は印 `CarriesAcrossChunks` を持つ。
  - `chunk-delete-stage-parent-gone-after-next-sync`：端末に親と子があり、サーバーで子を別の親へ付け替えてから親を消す。1 回で全部の道はその同期で親が消え、塊の道は次の同期で消える（行は「最初の同期の直後に無い」と「2 回目の同期の後に無い」の両方を表明し、前者の違いを宣言が受ける）。
  - どの行も、直結の Theory（1 回で全部の道）と小さい塊の端末の照合（塊の道）の両方で、`chunk-delete-stage-parent-gone-after-next-sync` の宣言した違いを除いて緑。
- 流していないもの：小さい塊の端末の転送（HTTP・実 SQL Server）。塊の経路は取得した行の数え方と適用だけで決まり、転送に依らない。塊をまたぐ形の行そのものは全転送で流れる。

### 同期のエンジンの決まった形の行

- 同期のモデルの決定的なシナリオでは書けない、同期のエンジン（`SyncEngine`）の形は、表「同期のエンジンの決まった形の行」（`SyncEngineFixedRows`・`SyncEngineFixedRows.StaleEntity.cs`・影響の地図の名前 `table:SyncEngineFixed`）に置き、`SyncSupportAdoSqliteEngineRuntimeTests.FixedRow_BehavesAsExpected` が 1 行ずつ流す（コンテナなし）。行は 126 本。
  - 端末とサーバー役は `SyncEngineFixedHarness`：端末は `SyncModelEngineDevice` と同じ部品（`SyncLocalTracking.Ensure`・開くたびの確認で包んだ接続ファクトリ・`SqliteLocalRowStore`・4 表の `SyncTable`）で組み、接続文字列の `Default Timeout` を 1 秒にし、フォルダーのリポジトリに Save フックの口を持つ。サーバー役は同期のモデルの SQLite の代役（`SyncModelServer`）。
  - 降ろす行の上限の行は 2 本。どちらも、上限の小さい洗い替えを流し、最初のページを取った後の点（`AfterServerFetchBeforeApply`）で端末へ記録つきの書き込みを 1 つ流す。
    - `refresh-over-row-limit-with-change-written-during-fetch-is-refused`：未送信の無い端末の `Force` なしの洗い替え。途中で新しい行を書くと、洗い替えは `SyncDownloadTooLargeException`（取得した行数・上限・表ごとの未送信の数を表明）で断り、端末は途中で書いた行が足されただけで、サーバー・再開点・前からある行の状態は変わらない。
    - `refresh-force-over-row-limit-with-discarded-key-rewritten-during-fetch-is-refused`：未送信の行がある端末の `Force` の洗い替え。途中でその行を書き直す（連番が進む）と、洗い替えは同じ例外で断り、行は書き直した内容で未送信のまま残る（書き直さなければ断られないことは、決定的なシナリオ `refresh-force-over-row-limit-discards-unsent-and-is-not-refused` が持つ）。
    - この 2 本は降ろす行の上限を小さくした洗い替えで流す（同期のモデルの割り込みの網の洗い替えの行は上限を製品の既定のまま渡す）。上限を超えて未送信がある同期・`Force` の洗い替え・上限ちょうどは、決定的なシナリオ（`dl-row-limit-*`・`refresh-force-over-row-limit-discards-unsent-and-is-not-refused`・全転送）が持つ。
  - 塊ごとに確定する経路の行は 12 本（`chunk-*`。洗い替えの `chunk-refresh-*` は別に数える）。`chunk-deep-selfref-chain-root-in-later-chunk-settles` は、塊の大きさを鎖（210 段）と同じにしたエンジンで、鎖が 1 つの塊・根が後ろの塊に入る形（同期のモデルの端末は塊が 2 行でこの形を作れない）が止まらずにそろうことを見る。行の中でエンジンの internal の値（閾値・塊の大きさ）を小さくし、差分ソースを包む `ObservedSyncServerSource`（行とキーの取得を前後で観測し、何回目の呼び出しかを数える。`SyncEngineFixedHarness.CreateEngine(observe)`）で、当たった時点の端末の状態を控えて、実行の後に表明する。
    - `chunk-failure-between-chunks-keeps-first-chunk-and-next-sync-resumes`：2 ページ目の取得の前で失敗させる。その時点で最初の塊の行が端末にあり、再開点がその塊の最後の行の版と一致する。同期は例外で終わり、次の同期は続きだけを取得してそろう。
    - `chunk-failure-after-carry-keeps-resume-point-before-carried-row`：最初の塊で子が保留（親が後ろの塊）になり、後ろの塊を書いた後で失敗させる。再開点は最初に持ち越した行の手前で止まり（後ろの塊の版まで進まない）、次の同期で持ち越していた行が入ってそろう。
    - `chunk-unsent-edit-between-chunks-kept-then-serverwins-restores`・`chunk-unsent-edit-between-chunks-kept-then-localwins-uploads`：最初の塊のコミットの後（その時点の端末の行で表明）に、まだ書かれていない行を端末で編集する。その実行の後、編集した行はローカルの内容のまま未送信で残り、ほかの行は降りている。続く ServerWins の同期はサーバーの内容に戻し、LocalWins の同期はローカルの内容をサーバーへ上げる。
    - `chunk-row-gone-after-keys-stays-until-next-sync`：入口の後、後ろの表のキーの取得がその表の行の取得より前だったことを呼び出しの順で表明し、その間にサーバーで消えた行が、その実行の後も端末に残って次の同期で消える。
    - `chunk-failure-then-device-deletes-committed-row-next-sync-sends-delete`：塊の間で落ちた同期が端末に残した最初の塊の行を端末で記録つきで消すと、次の同期がその削除をサーバーへ送り（競合なし）、その後の同期でも行は端末に戻らない。
    - `chunk-stale-row-then-fetch-failure-then-device-edit-collect-reports-conflict`・`chunk-stale-row-then-fetch-failure-then-device-edit-serverwins-restores`：取得の後・適用の前にサーバーで書き直された行が古い写しのまま塊で確定し、次の取得で同期が落ちた後に端末がその行を編集すると、Collect の同期は ModifiedOnServer を報告してサーバーの新しい内容を上書きせず（編集は未送信のまま残る）、ServerWins の同期は編集を捨てて端末の行をサーバーの新しい内容に戻す。
    - `chunk-apply-refused-local-write-keeps-committed-chunks-and-next-sync-resumes`：2 つ目の塊の適用の書き込みを端末のトリガーが断ると、同期は例外で終わり、先に確定した最初の塊の行だけが端末にあり、再開点はその塊の最後の行の版で止まる。断りを外した次の同期は続きだけを取得してそろう。
    - `chunk-download-route-cells`：道の決め方の枠（閾値以下・閾値を超え上限以下・上限を超える × 未送信なし・あり、と、上限が閾値以下の設定の 2 枠）を流し、観測した道（Single・Chunked・Refused）と、断る枠が何も適用しないことを表明する。洗い替えの枠（閾値を超えて未送信なし → Chunked・`Force` で捨てる行だけ → Chunked・閾値以下 → Single）も持つ。
    - `chunk-write-transactions-leave-gap-for-other-writers`：塊の経路のトランザクションの間の隙間（`ChunkWriteGap`）。形（同期・`Force` の洗い替え）× 時計（止まった時計・時刻を読むたびに隙間ぶん進む時計・隙間 0）ごとに別の端末で、閾値と塊の大きさを 2 行にしたエンジンで、サーバーのフォルダー（取得の順に 11〔親は 10〕・1・2・3・10）を流す。同期（空の端末）は最初の塊が 11 を持ち越して最後の段で書き、洗い替え（端末は 10 を持つ）は持ち越さない（持ち越しの数を `CarriedRowsApplied` で見る）。時計は偽の `TimeProvider`（`RecordingTimeProvider`＝頼まれた待ちの長さを覚えてすぐに発火する）で、時間は表明しない。止まった時計では待ちが塊の経路のトランザクションの数（同期は削除の段 1・塊 3・最後の段 1、洗い替えは最後の段なし）− 1 回で、どれも既定の 170 ms。ほかの 2 つの時計では待たない。結果（端末・サーバー・行の状態の表と再開点の表。DB ごとの印を持つ `quicker_sync_state` は除く）は 3 つの時計で同じ。既定の閾値（2 万行）と隙間（170 ms）の値も表明する。
  - 洗い替えの塊の経路の行は 10 本（`chunk-refresh-*`）。
    - `chunk-refresh-{plain|discard-blobs|force}-failure-then-{sync|refresh}` の 6 行（1 つの関数が洗い替えの種類 × 次の手から作る）：フォルダーの表の 2 ページ目の取得の前で洗い替えを落とす。その時点で、削除の段が済んでいる（サーバーに無かった行が端末に無い）・フォルダーの再開点が確定した塊の最後の行の版・品目の再開点が null（まだ届いていない表）・blob を捨てる行は全行の blob が空で写し待ちなし・`Force` の行は置き換え済みの行がサーバーの内容で未送信なし、まだの行が端末の内容で未送信、を表明する。次の手が同期なら、続きだけを取得してそろい、`Force` の残りの未送信がサーバーへ上がる。次の手が洗い替えの流し直しなら、そろって未送信が残らない。
    - `chunk-refresh-force-discard-gone-after-keys-only-loses-change`：品目のキーを取った後・行を取る前に、サーバーが捨てる行を消す。洗い替えの後に未送信が無く、その行は端末に残り、次の同期が何も送らずに消してそろう。
    - `chunk-refresh-force-discard-blobs-failure-keeps-blob-of-unreplaced-discard`：`Force` かつ blob を捨てる洗い替えで、端末が blob を記録つきで書いた品目を置き換える塊の前に落とす。その時点で、置き換え済みの捨てる行の blob は空・まだの行は未送信のままで blob が端末で書いた内容のまま。次の同期（blob を運ぶ）で、サーバーの blob が端末で書いた内容になる（空で上書きしない）。
    - `chunk-refresh-force-discard-blobs-completes-with-every-blob-empty`：同じ洗い替えを最後まで流すと、捨てた数・全行の blob が空・未送信なしで、1 回で全部の道と同じ結果になる。
    - `chunk-refresh-force-discard-blobs-empties-blob-of-discard-changed-after-ceiling`：同じ準備で、取得の上限を洗い替えの始めに固定し、その直後にサーバーの他者が捨てる行を書き直す（その行は洗い替えの取得に入らない。取得した品目の行の数で表明する）。洗い替えの後、その行の blob も空で、未送信も写し待ちも無い。続く blob を運ばない同期はサーバーの内容を降ろし、blob は空のまま。
      - 代役のサーバー役は取得の上限を持たないので、この行は `SyncEngineFixedHarness.CreateEngine` の上限の関数で上限を作る。関数はサーバー役の版を 1 つ採って返す（`SyncModelServer.TakeVersion`。その後の書き込みの版はどれもこれより大きい）。
  - 置けなかった捨てる行の行は 1 本（`refresh-force-discard-blobs-discard-held-back-is-not-left-unsent-with-empty-blob`・1 回で全部の道）：端末が blob と code を記録つきで書いた品目のサーバーの code を、`Force` かつ blob を捨てる洗い替えの品目の最初の取得の前に、端末の別の品目が記録つきで取る（洗い替えが捨てない未送信なので、サーバーの行を置けない）。洗い替えの後、その品目は未送信のまま blob も端末で書いた内容のまま。同じ洗い替えで、置き換えた捨てる行と未送信の無い行の blob は空になり、洗い替えの間に書かれた未送信の行の blob は残る（1 回で全部の道のこの 3 つを見る行はほかに無い）。端末が題名だけをもう一度書いてから流す blob を運ぶ同期の後、サーバーの blob は空でない（元の値か端末で書いた値）。
  - 未送信の行を外す条件の形の行は 1 本（`unsent-rows-condition-reads-state-table-once`）：製品の条件（`SyncEngine.NotUnsentCondition`）を品目の表に掛けた文の実行計画（`SyncEngineFixedHarness.QueryPlanAsync`＝`EXPLAIN QUERY PLAN`）に、表の行ごとに流し直す副問い合わせ（`CORRELATED`）が無いことを見る。対照として、行ごとに行の状態の表を引く文の計画には `CORRELATED` が出ることを同じ行で見る（表明が空振りでない）。条件の文字列はテストへ写さず、製品の関数から取る。行が少ないテストでは、この形の退行は結果にも時間にも出ない（10 万行の表で初めて数分かかる）。
    - 同じ条件で記録した送る行だけが残ることは、トリガーの記録の表（`SyncTriggerRecordingRows`）の全行に共通の表明（`SyncTriggerHarness.CountRowsKeptAsUnsentAsync`）が、キーの格納の型（整数・テキスト・実数・blob）ごとに見る。
  - エンジンの行は 13 本：同じ DB の 2 本の実行の直列化（1 本目をサーバー役の待ちの点で止め、2 本目がサーバー役に来る順序を `SyncModelFaults.ObservePoint` で記録する。表明は順序で行う）・保存フックの中の同期（保存の書き込みのロックを待って SQLITE_BUSY で失敗し、行の状態の表・端末・サーバーに途中の状態を残さず、実行のロックを手放す）・空にした表（3 つの入口が `SyncLocalTableEmptiedException` で断り何も変えない）・引数の検査（取得の大きさ 0 以下・降ろす行の上限 0 以下・同期しない型の除外。同期・送信だけ・受信だけ・洗い替えの 4 つの入口）・生の SQL で主キーを変えた行の blob が届く（主キーの変更はトリガーに記録され、新しいキーは挿入として全列を送る）・一意な値の回転が端末の blob を保つ・サーバーへの書き込みの後に送信の決着が失敗した後の同期（docs の既知の制限のとおり、次の Collect は自分が書いた行を相手に ModifiedOnServer を報告し続け、LocalWins で収束する）・洗い替えの取得の失敗は端末・未送信・行の状態の表を洗い替えの前のまま残す（`refresh-fetch-failure-leaves-device-as-before`）・1 回で全部の道の同期が 1 ページ目を取得した後の取得の失敗で終わると、端末・未送信・行の状態の表・再開点を同期の前のまま残す（`sync-fetch-failure-leaves-device-as-before`。割り込みの網が、次の取得の失敗 × 前段 `parent-after-child-*` × 編集してから同期の組を起こり得ないとして外す根拠の仕様）・1 回で全部の道の同期と `Force` の洗い替えの適用の書き込みを端末のトリガーが断ると、例外で終わって適用は何も残さず、断りを外した次の同期でそろう（`apply-refused-local-write-leaves-device-as-before-then-next-sync-settles`・`refresh-apply-refused-local-write-leaves-device-as-before`。同期は先に通る送信の段が決着させた行を除く）・洗い替えの直後の同期と同期の直後の同期がサーバーから 1 行も取得しない（`SyncModelFaults.ObservePoint` で取得が 1 行以上を返した後の点が来ないことを見る。対照としてサーバーの他者が変えた後の同期では点が来る）・端末が作って消したサーバーが知らないキーの未送信の行と同じキーでサーバーの他者が行を作ると、受信だけの実行がサーバーの行を端末に入れる（同期の送信の段はその記録を送らずに片付けるので、受信の段まで未送信が残るのは受信だけの実行のとき）。
  - 主キーの付け替えと古い読みの見分けの比較の 2 本（A2 の直し方を選ぶための行）：降ろす前に読んだ品目 1 の古い実体を保存してから品目 2 を消し、品目 1 の主キーを 2 へ付け替えた Collect の同期は ModifiedOnServer を報告する（`stale-entity-saved-after-download-then-key-moved-onto-locally-deleted-key-collect-reports-modified-on-server`）／降ろした後に読んだ実体で同じことをすると競合なしに上がる（`stale-entity-fresh-read-saved-then-key-moved-onto-locally-deleted-key-is-not-a-conflict`）。新しいキーの `applied_version` を NULL にする案では前の行が赤になり、古いキーの値を写す案で両方が緑（`src/QuickER.Runtime.Sync/CLAUDE.md`「ローカルの変更の記録（SyncLocalTracking）」）。
  - 適用の試行の数の行は 4 本（`apply-attempts-*`）：塊の中で根を持たずに保留する深さ 50・250 の鎖（`apply-attempts-held-chain-in-chunk-depth-{50,250}-stay-constant`）と、1 回で全部の道でサーバーが消した深さ 50・250 の鎖を端末の未送信の子が引き留める形（`apply-attempts-kept-chain-deleted-under-unsent-child-depth-{50,250}-stay-constant`）で、エンジンの internal の口 `ApplyAttemptsObserved` が数えた適用ごとの試行が 2 回以内（鎖の深さに依らない）ことを見る。端末に在るフォルダーが新しい親（端末の未送信の行が名前を持つので保留される）へ移されて保留になっても、その子の更新は同じ実行で降りる行（`apply-held-parent-present-locally-does-not-hold-its-children`）が、保留を子へ広げるのは親が端末に無いときだけであることを見る。
  - 主キーでない一意な列への外部キーの行は 5 本（`unique-reference-*`・`SyncEngineFixedRows.UniqueReference.cs`）：品目の code を参照する子（同期しないローカルの表 `local_notes`・その実行で除外したイベント・受信だけの実行の未送信のイベント。イベントの参照の列は端末の SQLite で `ALTER TABLE … ADD COLUMN … REFERENCES model_items(code)` で足す）× 道（1 回で全部・塊。未送信の子の形は未送信があるので塊を通らない）で、サーバーが参照先の code を変えると、品目を書かずに保留して止まらずに終わり（打ち切りで名指し・`MissingParentTableName` は null）、ほかの行と品目を主キーで参照する新しいイベントは降り、子の行と同期しない表（トリガー・行の状態）には触らず、子が参照を外した次の実行で品目が降りることを見る。生成の記述子は一意な列への外部キーを載せないので、エンジンから見えるものは図にその外部キーがあるときと同じ。
  - 古い読みの保存の行は 20 本（`stale-entity-*`・`SyncEngineFixedRows.StaleEntity.cs`）。同期が他者の内容を降ろす前に読んだ実体を、降ろした後に SQLite の Repository で保存する（Repository の UPDATE は版を照合せず、版の列を普通の列として書くので、端末の行の版の列が古い版へ戻る）。版で守る送信の段は、端末の行が持つ版が、エンジンがその行へ最後に入れた他者の内容の版より古ければ、その版を元の版にして送り、サーバーの版の照合が ModifiedOnServer を返す。端末自身が上げた版・同じ版の書き直し・強制の上書きの後は、他者の内容に数えない。
    - 降ろした後の古い実体の保存 × ポリシー 4 本：Collect は ModifiedOnServer を報告して上書きせず、行は未送信のまま残り次の Collect でも報告する（対照：降ろす前に保存した古い読みの編集も ModifiedOnServer）／ServerWins は捨てて（DiscardedByServerWins）端末の行をサーバーの内容とサーバーの版に戻す／LocalWins は上書きで上げ、その後に同じ古い実体をもう一度保存した Collect は競合なしに上がる／後勝ちは見分けずに上書きする。
    - 他者の内容を入れる適用の経路ごとの 3 本（`stale-entity-saved-after-{chunked-download|refresh-brought-other-content|serverwins-put-back-other-content}-collect-reports-modified-on-server`＝塊ごとに確定する取得・洗い替え・ServerWins の戻し）：どれも Collect が ModifiedOnServer を報告する。
    - 偽の競合にならない形 4 本：自分が上げた後の保存（`stale-entity-saved-after-own-upload-is-not-a-conflict`）・自分の 2 回の上げの間に読んだ実体・自分が上げた版を洗い替えが同じ版で書き直した後・自分が上げた行の次の編集を ServerWins が同じ版で戻した後。
    - 広く見分ける形 2 本：他者の内容の後に自分が上げても、他者の内容より前に読んだ実体の保存は ModifiedOnServer（自分の後の書き込みで他者の内容の記録を忘れない）／送信の決着が失敗した後に ServerWins がサーバーの行（自分の内容）で戻した後の保存も ModifiedOnServer（決着の無い版は他者の内容に数える）。
    - 生 SQL の更新が続いた行 1 本（`stale-entity-saved-then-raw-sql-update-collect-reports-modified-on-server`）：版の列を書かない生の SQL の UPDATE が続いても、行は古い版を持ったままなので ModifiedOnServer。
    - 見分けない形 2 本（今の振る舞いの固定）：古い実体の MarkRemoved のグラフ保存とキーの DeleteAsync は、基準の版で守った削除がそのまま通ってサーバーの行が消える（`stale-entity-delete-after-download-is-not-distinguished-and-deletes-server-row`）／版を持たない実体（端末で作って保存した実体）の保存は競合なしに上がって他者の内容を上書きする（`stale-entity-without-version-saved-after-download-is-not-distinguished-and-uploads`）。
    - 値オブジェクトの版の列 1 本（SyncVoFixture の端末・`stale-entity-saved-after-download-value-object-version-collect-reports-modified-on-server`）と、版の新旧の比べ方の行 1 本（`stale-entity-version-order-reads-versions-as-big-endian-numbers`＝版を big-endian の符号なし整数として読む：先頭の 0 のバイトは数えず、残りが短いほうが古く、同じ長さならバイトの辞書式。同じ値は古くない。サーバー役の版はどれも 8 バイトなので、長さの枝と先頭の 0 は同期を流す行からは見えない）。
  - 記録しない書き込みの API（`SyncLocalTracking.WriteUntrackedAsync`）の行は 9 本（`untracked-write-*`）：関数がプールの接続を閉じると、トランザクションとロックがプールに残り（別の接続の書き込みが SQLITE_BUSY になることで、その形が起きていることを確かめる）、同じプールの次の接続を開くと開くたびの確認が巻き戻す（この行だけ、製品の接続ファクトリを確認で包んだ行の中のファクトリを使う。開く処理が流す文を製品のものだけにするため＝端末の接続ファクトリは、開くたびにテストの DB の設定（`SqliteLightJournal`）の PRAGMA も流す。接続文字列を変えて、端末のほかの接続とプールを分ける）／書いた行は記録されず前からある記録・連番・印は変わらない／プールの接続で関数が例外を投げた後・取り消された後に何も残らず、渡した接続・同じプールの次の接続がトランザクションの外にあり、プールしない別の接続（待ち 1 秒）が書けて記録される／流れている間、別の接続の読み取りは待たずにコミットの前の内容を読み、書き込みはコミットまで進まない（順序で見る）／関数が生成の Repository で書くと SQLITE_BUSY で失敗して全体が巻き戻る／トランザクションの中の接続と入れ子の呼び出しを断り、利用者のトランザクションに触らない／開いていない接続・関数の中で閉じた接続・状態の表に行の無い DB を断る／コミットされて残った印があっても断らずに流れ、印を 0 に戻す。
    - 「トランザクションが残らない」は、渡した接続で `BEGIN IMMEDIATE` が通ることと、同じ接続ファクトリの次の接続（同じプール）で同じことと、接続文字列の違うプールしない接続の書き込みが通ることの 3 つで見る（1 つだけだと、同じプールの接続が残ったトランザクションの中で書いて通ってしまう）。
    - `SyncEngineFixedHarness.ExecuteUntrackedAsync` も同じ API で流す。
  - 観測の口 `SyncModelFaults.ObservePoint` は既定で null で、同期のモデルの結果を変えない。
  - 失敗の途中の道の行は 6 本（`SyncEngineFixedRows.FailurePaths.cs`）：断られた書き込みの理由の照会が頼んでいない取り消し（時間切れの形）で失敗すると、取り消しとして抜けずに書き込みの失敗と一緒の `AggregateException`（`refused-write-explanation-lookup-timeout-is-aggregated-with-write-failure`）・blob を送った直後にサーバーで消えた行は未送信のまま残り次の同期が MissingOnServer（`blob-upload-then-row-deleted-on-server-leaves-change-unsent-and-next-sync-reports-missing`）・塊の Force の洗い替えで捨てる行が行とキーの取得の間に消えても捨てた数は 1（`chunk-refresh-force-discard-gone-between-rows-and-keys-counted-once-{keep-blobs,discard-blobs}`）・Force の洗い替えの 1 回目に置けなかった捨てる行を 2 回目の取得が未送信に数えず塊の道を通る（`refresh-force-second-fetch-does-not-count-unplaced-discard-as-unsent`）・写し待ちの blob の読み取りの失敗で一時ファイルが残らない（`owed-blob-copy-read-failure-closes-temporary-file`＝エンジンの internal の `TemporaryFileFolder` を行の一時フォルダーへ向ける）。サーバー役（SQLite の代役）の口 `SyncModelFaults.FailNextServerRead`（次の行の読み取りで 1 回だけ投げる）と `SyncModelFaults.AfterBlobUpload`（品目の blob を書いた後に呼ぶ）は、決まった形の行だけが使い、既定で何もしない。
  - 記録しない書き込みの API の行のうち `untracked-write-closed-pooled-connection-is-rolled-back-by-full-check-of-other-guard` は、残ったトランザクションを持つプールの接続を、表の集合の違う（まだ何も覚えていない）確認で開くと、全部の確認が自分のトランザクションの前に巻き戻すことを見る。
  - 進まない差分ソースの行は 4 本（`non-advancing-source-{sync|refresh}-{versioned|versionless}-fails-instead-of-looping`・1 つの関数が作る）：対象の表の差分ソースだけを、最初の取得の行を `HasMore` つきで返し続ける包み（`SyncEngineFixedHarness.CreateEngine` の `NonAdvancingSource`）にして、同期と洗い替えが表を名指しした `InvalidOperationException` で終わり、端末・行の状態の表・再開点の表・サーバーが実行の前と同じであることを見る。包みは取得の回数に上限を持ち、守りが無ければテスト側の例外（`NonAdvancingSourceLimitException`）で行を赤にする（止まらないテストにしない）。
  - 未送信の一覧と捨てる口の行は 20 本（`pending-*`）：一覧の内容と順・エンジンの表の行だけを返すこと・捨てた行が次の実行でサーバーの内容に戻ること（Collect の競合を解く形／サーバーで変わっていない行をキーで読み直す形を同期・送信だけ・受信だけの 3 本で／端末で作っただけの行は消える／削除は行が戻る）・捨てた後に書き直した行は送られること・一覧の後に書かれた行は捨てないこと・型を渡す版と全部を捨てる版の範囲と戻り値（すでに印のある行も数える）・表を外した実行は印を残すこと・知らない表と型を何も変えずに断ること・洗い替えが捨てる印のある行を未送信に数えずに置き換えること（`Force` なしの洗い替えが断らない／印の後に書き直した行は断る／印の無い未送信がほかにあれば断り、例外の件数はその行だけ／塊ごとに確定する経路でも置き換える／取得の上限の未送信に数えない／取得の途中に端末で書き直した行は上限の未送信に数えて断る）。
    - `pending-list-does-not-wait-for-run-and-discard-does`：同期をサーバーへの書き込みの直前で止め、その間に一覧は完了し、全部を捨てる口は完了せず、同期を進めると完了することを、記録した順序と完了・未完了で見る（時間の長さでは表明しない）。
  - 記録の仕組みを通らずに入った行（端末とサーバーに同じ行があり、行の状態が無い）の行は 4 本（`stateless-*`）：最初の同期の前の更新は Collect の同期のたびに `DuplicateOnServer` で報告される・最初の同期の前の削除は送られずに行が戻る・書く前に同期か洗い替えを 1 回流せば、その後の編集は競合なしに上がる（`stateless-rows-take-state-from-first-sync-then-edit-uploads-{sync|refresh}`）。今の振る舞いの固定で、docs の既知の割り切りの根拠にする。
  - `download-does-not-run-local-save-hooks`：フォルダーのリポジトリに行を飛ばす Save フック（`SyncEngineFixedHarness.BeforeFolderSave`）を登録する。端末のグラフ保存ではフックが呼ばれて行が飛ばされ（空振りでないことの確かめ）、同期が降ろした新しい行と更新は端末に入って、フックの呼ばれた数は増えない。
  - 実行の接続の行は 5 本（`run-connection-*`）。端末の `ILocalRowStore` を、`CreateDedicatedConnection` の回数を数えて作った接続を覚えるテストの側の包み（`CountingLocalRowStore`・`SyncEngineFixedHarness.CreateCountedEngine`）にして見る。製品に観測の口は足さない。
    - `run-connection-is-opened-once-per-run`：10 の形（何も無い同期・5 行を送る・6 行を降ろす・blob を送って運んで降ろす・送信だけ・受信だけ・blob を運ぶ洗い替え・blob を捨てる洗い替え・塊 4 つの同期・Collect の競合）を順に流し、形ごとに、開いた数がちょうど 1 で実行の後に閉じていることを形の名前つきで表明する。
    - `run-connection-is-closed-after-failed-run`：取得の失敗で例外になった同期の後も、開いた数は 1 で、接続は閉じている。
    - `run-connection-list-opens-its-own-and-discard-opens-one`：未送信の一覧は 1 本開いて閉じ、全部を捨てる口も 1 本。
    - `run-connection-does-not-block-other-writers-between-transactions`：1 回の同期を、サーバーへの書き込みの直前と、取得の後で適用の前の 2 つの点で止め、その間に端末の Repository（待ち 1 秒）の書き込みが例外にならないことを見る。
    - `run-connection-temp-table-rotation-in-two-chunks`：塊ごとに確定する同期で、2 つの塊のそれぞれが一意な値の入れ替えを回転で降ろす（同じ接続で、回転の一時表とキーの一時表を別々のトランザクションから 2 回ずつ通る）。
  - 同期のモデルの図に無い形の行は 7 本。
    - SyncVoFixture の端末の `sync-vo-serverwins-restores-row-with-server-version`：サーバーの他者と端末が同じ行を編集した後の ServerWins の同期で、端末の行がサーバーの内容に戻り、値オブジェクトの版の列がサーバーの版と同じになって、続く同期が何も送らず何も降ろさない。
    - `refresh-unlisted-referring-table-refuses-while-present-locally`：記述子を写して品目の表からの参照を `LocalReferences` から `UnlistedReferencingTableNames` へ移したエンジンの洗い替えは、品目の行が無くても表が在るだけで `SyncRefreshScopeException` で断り、端末と行の状態の表を変えない。対照として、参照が一覧に載るエンジンと、端末に無い表の名前を一覧に載せないエンジンは通る（同期のモデルの図にこの外部キーが無いので、ハーネスのまま記述子を写して組む）。
    - SyncFixture の端末（`SyncFixtureEngineDevice`・サーバー役 `SyncFixtureTestServer`）：`sync-fixture-blob-cleared-on-server-is-cleared-locally`（サーバーで NULL にした blob は、blob を運ぶ同期で端末でも NULL になる）・`sync-fixture-nullable-computed-column-downloads-and-stays-null`（サーバーで値を持つ NULL 許容の計算列のある行を降ろせ、端末の計算列は NULL のまま）。
    - SyncVoFixture の端末（`SyncVoFixtureEngineDevice`・サーバー役 `SyncVoFixtureTestServer`）：`sync-vo-new-row-with-excluded-not-null-blob-downloads`（NOT NULL の除外 blob を持つサーバーの新しい行を blob を運ばない同期が降ろし、端末の blob は空の値）・`sync-vo-local-insert-without-version-uploads`（版の列が空の端末の新しい行が上がる）・`sync-vo-server-version-written-back-through-value-object`（サーバーが採番した版が値オブジェクトの版の列へ書き戻り、往復が定常になる）。
    - 端末を組む口は `SyncEngineFixtureDevices.cs`：端末の DB は同期のトリガーの端末（`SyncTriggerHarness`）で作り、`SyncLocalTracking.Ensure` でトリガーと状態の表を張り、開くたびの確認で包んだ接続ファクトリから `SqliteLocalRowStore` とリポジトリを作って `SyncTable` でエンジンを組む（リポジトリは何にも包まない）。サーバー役は呼び出し側が差分ソースで渡す（SQLite の代役・実 SQL Server の直結・HTTP のどれでもよい）。同じ口を、上の「差分ソースの HTTP と実 SQL Server の固有のテスト」の端末も使う。
- mutation 実証済み（実行のロックを外す・空にした表の確かめを外す・回転で blob を戻さない・挿入した行の blob の列を全部にしない・洗い替えが再開点を書かない・同期が再開点を書かない・洗い替えの取得の大きさの検査を外す・ローカルに無くサーバーが知らない未送信の行にもローカルを残す・降ろす行の上限の検査を外す・洗い替えのときは上限を超えて未送信があっても断らない・洗い替えが捨てる行を連番を見ずにキーだけで未送信から外す、のそれぞれで対応する行が赤。塊ごとに確定する経路：再開点を持ち越し中の行の先へ進める・閾値の時点の未送信を無視する・入口の後ろの表のキーを取らない・塊の計画で行の状態を読まない、のそれぞれで対応する `chunk-*` の行が赤。洗い替えの塊の経路：削除の段で再開点を戻さない・塊で捨てる行を置き換えない・削除の段の blob を空にする処理を外す・最後の段で取得が持ってこなかった捨てる行を片付けない・削除の段で捨てる行の blob も空にする・置き換えの時点で blob を空にしない・最後の段で取得が持ってこなかった捨てる行の blob を空にしない、のそれぞれで対応する `chunk-refresh-*` の行が赤。記録しない書き込みの API：失敗の出口で巻き戻さない・巻き戻しを呼び出し側の取り消しのトークンで流す・印を立てない・印を戻さずにコミットする・トランザクションの中の接続を名指しで断らない・関数の中で閉じた接続を確かめない・状態の表の行を確かめない・開くたびの確認がプールに残ったトランザクションを巻き戻さない、のそれぞれで対応する行が赤。進まない差分ソースの守りを版あり・版なしで 1 つずつ外すと、対応する 2 行が赤。未送信の一覧と捨てる口：印を立てない・連番を確かめずに立てる・型を渡す版が全部の表に立てる・一覧がエンジンの表でない行も返す・一覧の捨てる印をいつも偽にする・一覧を実行のロックの中へ戻す・捨てる口から実行のロックを外す、のそれぞれで対応する行が赤。ServerWins が戻す行の版を、戻す書き込みの前に端末の前の版へ差し替えると、`sync-vo-serverwins-restores-row-with-server-version` だけが赤。実行の接続：実行の終わりに閉じない・未送信の一覧が実行の接続を借りる・送信の決着をコミットしない・実行の中の 1 か所を開き直す形へ戻す、のそれぞれで対応する行が赤）。

### 同期のトリガーのテスト

- ローカルの変更の記録（`SyncLocalTracking`）と開くたびの確認（`SyncLocalTrackingGuard`）は、`SyncSupportAdoSqliteTriggerRuntimeTests`（Theory 2 本・コンテナなし）が 2 つの行の表を 1 行ずつ流して確かめる。仕組みの正本は `src/QuickER.Runtime.Sync/CLAUDE.md`「ローカルの変更の記録（SyncLocalTracking）」「開くたびの確認（SyncLocalTrackingGuard）」。
  - 行の表は「同期のトリガーの記録」（`SyncTriggerRecordingRows`・影響の地図の名前 `table:SyncTriggerRecording`）と「同期のトリガーの確認」（`SyncTriggerGuardRows`・`table:SyncTriggerGuard`）。端末の組み立ては `SyncTriggerHarness`（SyncModelFixture・SyncFixture・SyncVoFixture の 3 つのフィクスチャの SQLite の側）。
  - 記録の表には、除外 blob のある品目の主キーの変更で新しいキーを全部の除外 blob の列つきで記録する行が 3 本ある（`primary-key-change-blob-to-new-key-names-all-blob-columns`・`primary-key-change-blob-onto-locally-deleted-key-names-all-blob-columns`・`primary-key-change-blob-swap-through-temporary-key-names-all-blob-columns`）。写し待ちの blob を持つ品目の主キーの変更の行は 7 本（3 形 × 付け替えの前の端末の書き込みの有無の `primary-key-change-owed-blob-*` と、写し待ちのあるキーへ動かす `primary-key-change-blob-onto-key-owing-its-own-blob-drops-that-owed-copy`）で、写し待ちは記録つきでなく行の状態を直接入れて作り、新しいキーの `blob_columns`・`blob_owed`・`owed_key` を表明する（入れ替えは仮のキーを経ても元のキーを受け取る）。
  - 行の数は記録の表が 65 行・確認の表が 49 行（うち DI の登録の行 `di-*` が 11 行）。速い道の読み取りが DbException 以外で失敗しても接続を閉じる行 `state-flag-of-wrong-type-on-fast-path-closes-connection` がある。
  - 確認の表の DI の登録の行（`SyncTriggerGuardDiRows.cs`）は、生成の `AddGeneratedSyncEngine` を SyncFixture の SQLite の端末で確かめる。行の手 `Open` の中でサービスの登録を組み（プロバイダはスコープを検査する）、そのコンテナで開いた接続・書き込み・エンジンの実行を表明する（行のガードは使わないので回数の増え方は 0 で、Repository の書き込みと速い道の共通の表明は掛けない。DB のファイルの共通の表明は、登録が接続を開いてトリガーを張る行だけに掛ける）。
    - 登録が接続ファクトリを包む：keyed の登録では keyed の接続ファクトリが確認つきの包みになり、keyed の Repository の書き込みと生 SQL の書き込みがどちらも行の状態の表に残る（同じコンテナのサーバーの側の登録は包まない＝`di-keyed-registration-records-repository-writes`）。キーなしの登録も同じ（`di-unkeyed-registration-records-repository-writes`）。Repository の登録の後にインスタンスで登録し直した接続ファクトリは、そのインスタンスを内側にして包まれ、シングルトンのままどのスコープでも同じ包みが返る（`di-instance-registered-connection-factory-is-wrapped`）。
    - 除外：`excludeFromSync` に渡した表にはトリガーを張らず、その表への書き込みは記録されない（`di-excluded-table-gets-no-trigger-and-is-not-recorded`）。除外した表の行が範囲の表の行を参照していると、DI から引いたエンジンの洗い替えは `SyncRefreshScopeException` で断る（`di-excluded-table-rows-referring-to-scope-refuse-refresh`）。
    - 登録の断り：同期する型でない除外（`di-unknown-exclusion-is-refused`）と全部の除外（`di-excluding-everything-is-refused`）は `ArgumentException`、ローカルの接続ファクトリをそのキーで登録する前の呼び出しはキーを名指しした `InvalidOperationException`（`di-no-local-connection-factory-is-refused`）、`AddGeneratedSyncEngine` の後で同じキーの接続ファクトリを登録し直したコンテナからエンジンを引くと名指しの `InvalidOperationException`（`di-connection-factory-registered-after-sync-engine-is-refused`）。
    - 業務の表が 1 つも無い DB では、包んだ接続ファクトリの最初の接続で状態の表だけができ、次に開く接続からトリガーが在って書き込みが記録される（`di-tables-created-on-first-connection-are-recorded-from-next-connection`）。DI から引いたエンジンの同期でローカルの新しい行が上がりサーバーの新しい行が降り、除外した表のサーバーの行は降りず、行を飛ばすローカルの Save フックがあっても降りた行は入りフックは呼ばれない（`di-sync-through-registered-engine-uploads-and-downloads`）。
  - 記録の表には、グラフ保存が DB から消した行だけを削除として記録する行（`graph-save-removed-parent-with-new-children-records-only-deleted-rows`＝消す親の下のキーの無い新しい子と、キーを持つ新しい版なしの子は記録に無い）と、トリガーを張らない親の表のリポジトリのグラフ保存が同期する子だけを記録する行（`graph-save-through-untracked-parent-records-only-tracked-child`）がある。
  - 記録の表には、行ごとの状態の表のエンジンだけが書く 4 列（`base_version`・`known`・`sw_seq`・`blob_owed`）を、更新・削除して入れ直す・主キーの変更（古いキー）・無制限バイナリ列の書き込みの後も保つ行（`engine-columns-kept-by-*`）がある。
  - 確認の表には、再開点の表（`quicker_sync_table`）だけを落としたときに 2 段目で足りないと答えて作り直す行（`anchor-table-dropped-is-recreated`）がある。
  - 確認の表には、コミットされて残った適用中の印の行が 6 本ある（`applying-flag-*`）：速い道・2 段目の後・張り直しの後のどれでも、その開く処理が印を 0 に戻す（`applying-flag-left-set-is-cleared-on-fast-path-open`・`-after-full-check`・`-after-repair`）／書き込みのロックを取れなければ接続を閉じて断り、後で開けば戻す／読み取り専用の接続は断る／開いたままのトランザクションの中の印は、開く処理を待たせない（`applying-flag-inside-open-transaction-does-not-block-open`）。
    - 戻す 3 行は、行のガードで 1 回だけ開いた直後に、包んでいない接続で印を読む（行の手 `Open` を差し替える）。共通の表明（その後の Repository の書き込みが記録される）より後で読むと、その書き込みの接続が速い道で印を戻してしまい、2 段目・張り直しの後に戻さない壊し方が緑のまま通る。
- 記録の表の共通の表明
  - 張った直後：初回の `Ensure` は渡した表を全部返し 2 回目は空・状態の表は 1 行・トリガーは渡した表にだけ付く・記録は空。
  - 手の後：記録が期待の並びと一致する・連番が並びの順に厳密に増えて `next_seq` より小さい・適用中の印は 0 のまま・触らなかった前準備の行は記録に無い・記録したキーで元の表を引き直せる。
- 確認の表の共通の表明
  - 次に開く処理が速い道か 2 段目か・張り直しの回数がどう増えるか。
  - 失敗するとき：例外の型と文の断片・接続が Closed・スキーマが開く前と同じ。
  - 成功するとき：トリガーが `TriggersFor` と一致し状態の表に 1 行・その後の書き込みが記録に残る・もう一度開くと速い道。
- 行に固めていないもの：同時に開く 2 つの接続の回数（範囲で期待する）。
- 観測どおりに固めたもの：カスケードの記録の順（孫の削除 → 子の削除 → 子の NULL 化 → 親の削除）・`VACUUM` の後は 2 段目に入る・`Data Source=:memory:` は開くたびに別の空の DB になる・同期する表の名前を変えるとトリガーが変えた後の名前の表に付け替わり、古い表の名前で記録し続ける。
- 生成の行：`CodegenCaseTests` の行 `diag-sync-schema-qualified-table-excluded-with-warning`・`exec-sync-quoted-names-local-tracking-records-on-sqlite`。分割の Runtime 系ファイルの型の集合の行 `relation-runtime-type-set-split-multi-dialect` は、機能で出し分ける固定の部品（HTTP・同期）を有効にした構成で照らす。
- mutation 実証済み（記録：適用中の印の条件を外す・連番を進めない・主キーを変える更新の古いキーを削除として記録しない・挿入し直しで無制限バイナリ列の名前を付け直さない／確認：速い道が DB ごとの印を見ない・失敗のときに接続を閉じない・読むだけの確かめが比べない・覚える鍵に定義のハッシュを入れない・張る前に列を確かめない・残った適用中の印を戻さない・印を速い道でしか戻さない・印を戻す文を開くたびに流す）。
