# TestSupport（tests/QuickER.Tests/TestSupport）

テストの土台（エンジンの共通の枠組み・共有の部品・横断のガード）の規則を置く。
`QuickER.Tests` と `QuickER.Tests.Integration` の両方が使う部品は、WPF に依存しないものだけを Compile のリンクで共有する。
同期計画の総当たりの規則（表明 1b・1 種の例外の記録など）は `tests/QuickER.Tests.Integration/Dialects/SyncPlanFuzz/CLAUDE.md`「総当たりの表明と運用」を、名前の総当たり（NameFuzz）は `src/QuickER.Model/CLAUDE.md`「名前の総当たり」を参照する。

## エンジンの核（Engines/）

- 置き場所は `tests/QuickER.Tests/TestSupport/Engines/`（名前空間 `QuickER.Tests.TestSupport.Engines`）。
  BCL と xUnit だけに依存する。Integration が `Compile Include="..\QuickER.Tests\TestSupport\Engines\**\*.cs"` の glob で同じファイルをコンパイルするので、WPF に依存すると Integration のビルドが止まる。
- 部品は 7 つ。
  - `SeedSource` / `SeedSettings` / `SeedPlan`：種の数の環境変数の解析を 1 つにする。
    - 優先は「単独の種の変数 → 個別の数の変数 → プロファイルの既定」。
    - `int.TryParse(NumberStyles.None, InvariantCulture)` で読み、数が読めない・0 以下なら既定へ倒す。単独の種が読めなければ無視する。どちらも `Warnings` に残す。
    - `QUICKER_ENGINE_PROFILE=nightly` で夜間の既定の数になる（普段の数以上・同じ開始番号から連番＝普段の種を先頭に含む）。
    - `IsDefaultCount` は、数をプロファイルの既定から決めたときだけ真。環境の読み取りは引数で差し替えられる。
  - `KnownFailureSet`：既知の失敗の一覧。
    - `EngineFailure`（範囲・署名・詳細・種・`Source`）と `KnownEntry` で組む。`Source` はエンジンの元の結果で、述語の項目が構造で判定するために持つ（照合と報告は読まない）。
    - `KnownKind` の Pending は `FixedIn`、Accepted は `Reason` が必須。照合は `KnownMatch` の 4 つ。乱数の項目は `RandomSeeds`。
    - 構築時に整合を検査し、`Classify` が「載っていない」「載っているのに起きない」「理由の断片の食い違い」の 3 つを返す。
    - 「起きない」を数えるのは、決定的なシナリオの項目は常に。乱数の項目は `Reproduction`（`KnownReproduction`）が言う既定の種の数で流したときだけ。決定的な項目は `RegularSeeds` だけ。
      - `RegularSeeds`：普段と夜間の既定の種の数で数える（既定）。
      - `NightlySeeds`：夜間の既定の種の数でだけ数える（夜間に見つけた失敗）。
      - `NotGuaranteed`：数えない。
    - 種を持たないエンジンは `Classify(observed, ownsScope)`（受け持つ範囲に乱数の項目があれば断る）を使う。問題の文だけが要るときは `KnownReport.Problem`。
  - `KnownCaseLedger`：ケース ID ごとに検証本体を流し、例外を「範囲＝ケース ID」の失敗にして `KnownFailureSet` へ渡すだけの台帳。照合の規則は持たない。項目は `KnownCaseLedger.Case`（ケース ID・指摘・直す区切り・期待する理由の断片）。
  - `EngineReport`：先頭 8 件（エンジンが指定）を再現の手順つきで詳しく書き、残りは範囲ごとの件数にする。
  - `CoverageGuard`：`AtLeast` / `ContainsAll`。黙った空振りを止める。
  - `SeedFingerprint` / `FingerprintFile`：種ごとの生成器の入力の要約の指紋の記録と照合。
    照合は、普段の既定の種の数なら全数・夜間の既定の種の数なら記録にある種の全部・個別の指定なら重なりだけ。書き直しは普段の既定の種の数のときだけ。
  - `SeedSummary`：要約の書き方。値は型名つき・インバリアント、文字列は引用して ASCII 以外を `\uXXXX`、日時は往復の書式、バイト列は 16 進、一覧は要素を展開、オブジェクトは公開プロパティを名前の序数順に展開する。
    Kind が Local の日時は、機械の時差を付けず `DateTime:Local:` と時差なしの時刻を書く。読むだけで乱数を引かない。
- ガード：TestSupport 直下の `SeedSourceTests` / `KnownFailureSetTests` / `KnownCaseLedgerTests` / `EngineReportTests` / `CoverageGuardTests` / `SeedFingerprintTests`。mutation 実証済み。

## 既存エンジンの載せ替え

- 既存の 7 エンジン（同期のモデル・同期計画・実装先の一致・簡易 DSL・名前・ツールの引数・Undo）は、核の部品を通す。変数名・既定の種の数・開始番号は変えていない。
- 種の解析は `SeedSource`。各エンジンが `SeedSettings` を持つ（`SyncModelRuntimeTests.Seeds`・`SyncModelSignatures.Seeds`・`SyncPlanFuzzTestsBase.SeedSettingsFor`・`BackendParityRuntimeTests.Seeds`・`NameFuzz.SeedSettings`〔簡易 DSL と共有〕）。
- 既知の失敗は `KnownFailureSet`。一覧を持つクラスの名前は残した。
  - `SyncModelKnownFailures.Set`：決定的な項目 `Scenarios` の Family から乱数の項目を作る。`SyncModelSqlServerRuntimeTests.SqlServerOnly` は SQL Server でだけ起きる失敗。
  - `SyncPlanFuzzKnownFailures.Set`：範囲は方言。述語は `Source` の `FuzzSeedOutcome` を読む。
  - `BackendParityKnownDifferences.Set`：範囲はシナリオ、署名は「実装先:観点」。受け入れた差は Accepted。
  - `NameFuzzKnownFailures.Set` / `DslFuzzKnownFailures.Set`：観測は `FuzzFailureLog` が集め、項目は `FuzzFailureKey.Known`。
  - `UndoKnownFailures` / `ToolArgumentShapeFuzzKnownFailures` とほかのガードは `KnownCaseLedger`。
- 報告は `EngineReport`、最低件数は `CoverageGuard`（簡易 DSL の 100 件の下限は失敗のキーのまま）。
- 実装先の一致の「受け入れた手の差」は `AcceptedDeviations`（実装先 × 手の種類）。
  - インメモリが外部キーを検査しない差が該当する。`DeviatingOp` で印を付けた手から後は、その実装先だけを比べない（打ち切りは `ParityRunResult.Cuts`）。
  - 手を全実装先から作らない理由：作らないと、ほかの実装先を見る機会まで消えるため。
  - 生成器は列の末尾にだけ置く（`AppendTail`・種の 3 分の 2）。次元表の因子 `Deviation` と、「インメモリ・EF Core・リモート・EF Core 単独のサーバーが比べた手はそれぞれ 9 割以上」の CoverageGuard が黙った減少を止める。
  - 直す区切りのある差は `PendingDeviations`（`tests/QuickER.Tests.Integration/GeneratedRuntime/CLAUDE.md`「直す区切りのある手の差」）。
  - 宣言を全実装先へ広げる退行（EF Core・リモートの比較まで黙って打ち切る）は、乱数の種では「打ち切りは宣言の一覧の（実装先・手の種類）にだけ起きる」の表明が、仕組みは `BackendParityRuntimeTests.AcceptedDeviation_CutsOnlyTheDeclaredBackend` が捕まえる。mutation 実証済み。
- 種の解析を 1 つにしたことで変わった挙動（変数を指定しない普段の実行の件数は変わらない）：
  - 同期計画の `QUICKER_SYNC_FUZZ_SEEDS` / `QUICKER_SYNC_FUZZ_SEED` が `int.Parse` の例外にならない（読めない数は既定・読めない単独の種は無視、どちらも警告を出力へ書く）。
  - 実装先の一致の `QUICKER_PARITY_SEEDS` の 0 以下と読めない値が既定になり、解析がインバリアントになる。
  - `QUICKER_FUZZ_SEEDS` の「既定（2）以下の指定は無視」がなくなる（1 なら 1 種だけ流す）。
  - 同期計画の `QUICKER_SYNC_FUZZ_SEEDS=0` が 0 種でなく既定の種の数になる。
  - 同期計画の読めない単独の種が例外でなく無視になる。
- 簡易 DSL の SQL Server（`DslConditionFuzzSqlServerRuntimeTests`）も種の解析を `SeedSource` へ載せた。読めない `QUICKER_FUZZ_SEEDS` は「既定の 1 種・網羅の照合あり」になる。
- 夜間の種の数（`NightlyCount`）は各エンジンの `SeedSettings` の第 3 引数に置く（「夜間の深い実行」参照）。
- 載せ替えで生成器の入力が変わらないことは種の指紋が、流れるケースの数が変わらないことは `-list full -preEnumerateTheories` の照合が示す。

## 次元表と網羅

エンジンは「何の次元を、どの水準まで流しているか」を宣言として持つ。同じ `Engines/` に置く。

- 部品は 6 つ。
  - `DimensionTable`：`Factor` / `Level`、それが製品のどの軸の項目かを示す `AxisMember`（`Level.Source`〔列挙値〕・`Factor.Source`〔プロパティ〕）、3 条件で網羅する `FactorGroup`、組み合わせられない組の `isValid`、`RequiredPairs` / `RequiredTriples`（`fixedLevels` でその実行で固定した因子を扱う）。
    - `isValid` は部分の割り当てを受け、**原理的に起こり得ない組だけ**を規則ごとに理由のコメントつきで書く。エンジンがまだ出していないだけの組は書かない（書くと、埋めるべき穴が目録から消える）。
  - `CoveringArray.Generate(table, seed)`：2 条件と指定の組の 3 条件を網羅する行を決定的な貪欲法で作る。行き詰まれば総当たり（上限 `MaxExhaustiveTrials`）で探し、無ければ組を名指しして断る。既存のエンジンには使っていない。
  - `CoverageCounter` / `CoverageReport`：1 ケースで決まった水準を `Observe` で数える（スレッドセーフ）。
    - 宣言に無い水準・因子の欠け・`isValid` 違反・固定と食い違う値は `CoverageDeclarationException`。最初の 1 件を覚えて `Report()` が投げ直すので、並列の種の例外に紛れても宣言の誤りとして赤になり、既知の失敗の照合に混ざらない。
    - 網羅すべき組は構築時に因子の組ごとの索引（水準の番号の直積を平らにした配列）へ入れ、`Observe` は宣言の検査を通った割り当ての水準の番号から当たる組を直接引く。全部なめて `IsContainedIn` で数えるのと同じ結果であることを `CoverageCounterTests.IndexedCounting_MatchesNaiveScan`（乱数の次元表・3 因子の集まり・`isValid`・固定・宣言の誤りを含む列で、素朴な数え方と `Report` の全部の値を比べる）が固定する。
    - `WriteSummary` / `WriteGapUsage` が因子 × 水準の回数と一覧の当たり方を出力へ書く。
  - 網羅しない組の一覧：`CoverageGap.Tuple` / `Matching` は Pending（直す区切りと理由が必須）、`AcceptedTuple` / `AcceptedMatching` は Accepted（理由が必須）。
    - Accepted は**製品・プロバイダ・ツールのスキーマの事実でエンジンを直しても埋まらない欠けだけ**。述語は 1 つの因子の組の中だけに当たる。
    - 照合は `CoverageReport.AssertGaps(CoverageGapSet, scope, SeedPlan?)` が `KnownFailureSet` の規則のまま行う。一覧に無い欠けも、一覧にあるのに網羅した組も赤。照合は既定の種の数で流したときだけ。一覧の項目が次元表の組を指していないことは、種の数に依らず宣言の誤りにする。
  - `EngineIds`（13 の ID）と `EngineDescriptor`（ID・部分・次元表・シナリオの数・ソース・守る src の領域）と、アセンブリごとの登録簿。
    - `tests/QuickER.Tests/TestSupport/EngineRegistry.cs`：Dsl/SqliteMySql・Name/Entrances・DiagramEdit/ToolArgument・DiagramEdit/Undo・Dialect/Types・Dialect/Identifiers・Dialect/DdlText・Dialect/ImportWarnings・DocumentFile/Scenarios・DocumentFile/Random・DocumentFile/Format・DocumentFile/Settings・FormatRoundTrip/RoundTrip・FormatRoundTrip/Omissions・FormatRoundTrip/Rules・FormatRoundTrip/Merge・Ai/ChatEngines・Ai/Drivers・Ai/Attachments・Ai/ConnectionSettings・Ai/ChatDialog・OsBoundary/ProcessLaunch・OsBoundary/ShellPermission・OsBoundary/PathEntrances・OsBoundary/AtomicFile・OsBoundary/Containment・OsBoundary/Segments・OsBoundary/Lifecycle・OsBoundary/PostExitExhaustive・OsBoundary/ShutdownChainExhaustive・OsBoundary/FeatureModules・OsBoundary/ProfileStore。
    - `tests/QuickER.Tests.Integration/EngineRegistry.cs`：SyncModel/Random・SyncModel/Outcomes・SyncModel/Config・SyncPlan/Fuzz・SyncPlan/CapabilityNet・SyncPlan/ShapeRows・SyncPlan/HistoryRows・GeneratedRuntime/Parity・GeneratedRuntime/ParityOutcomes・Dsl/SqlServer・Name/DdlRoundTrip・Dialect/TypesRealDb。Guard は登録しない。
  - 因子の棚卸し：`FactorInventoryGuard` は、製品の仕様の軸の全項目が、そのアセンブリの登録簿の次元表の Source に載っているか、理由つきの除外（担当のエンジン ID・区切り・空や仮置きでない理由）の**ちょうど一方**であることを検査する。
    14 の軸（設計 2.9 の 13 軸と P3-8 の `ExportOmissionKind`）をどちらのアセンブリが見るかは `FactorInventoryAssignment` が決め、各ガードは自分の軸の並びが割り当てと一致することを表明する。
  - 行列の軸：`MatrixAxis<TAdapter>` は、正本の値ごとにアダプタがちょうど 1 つあるか、理由つきで外してあるかを構築時に検査する。`[MatrixAxisValue]` と `MatrixAxisGuard.DerivedClassesCover`（基底か目印のインターフェースの具象の派生と正本の照合）、`DeclaredValuesMatch`（派生の名乗る値と実際の値の照合）で固める。
    値ごとの違いはアダプタの抽象メンバーにし、アダプタの中で値を名指しして分岐しない。
- 各エンジンの次元表・一覧・記述は、エンジンの隣の `*Dimensions.cs` に置く：`SyncModelDimensions`・`SyncModelOutcomeDimensions`・`SyncConfigDimensions`・`SyncPlanFuzzDimensions`・`BackendParityDimensions`・`NameFuzzDdlRoundTripDimensions`・`DslFuzzDimensions`・`NameFuzzDimensions`・`ToolArgumentFuzzDimensions`・`RelationshipTypeChangeDimensions`・`DialectTypeDimensions`・`DialectTypeRealDbDimensions`・`DialectIdentifierDimensions`・`DialectDdlDimensions`・`DialectImportWarningDimensions`・`DocumentFileDimensions`・`DocumentFormatDimensions`・`SettingsStoreDimensions`・`FormatRoundTripDimensions`・`ChatEngineDimensions`・`ChatDriverDimensions`・`AttachmentListDimensions`・`ConnectionSettingsDimensions`・`ChatDialogDimensions`・`ProcessLaunchDimensions`・`ShellPermissionDimensions`・`PathEntranceDimensions`・`AtomicFileDimensions`・`PathContainmentDimensions`・`PathSegmentsDimensions`・`LifecycleDimensions`・`PostExitExhaustiveDimensions`・`ShutdownChainExhaustiveDimensions`・`FeatureModuleContractDimensions`・`ProfileStoreDimensions`。
- 乱数で操作の列を作るエンジン（同期のモデル・同期計画・実装先の一致）の `Observe` は、生成器が返し終えた値と、コンストラクタで決まる公開プロパティだけを読む（乱数を引かない・生成器の中に置かない。種の指紋が守る）。
- 行列の軸として登録したもの：同期計画の総当たりと名前の DDL の往復の方言の派生 5 つずつ（正本 `DialectNames.All`）・同期のモデルの転送経路（正本 `SyncModelTransport`・目印 `ISyncModelTransportTests`）・実装先の一致（`BackendParityAxis`＝`ParityScenario.Backends` の並びの出どころ）・簡易 DSL の実装先（`DslFuzzBackends.Names` と構成ごとの軸）・方言の型の表のアダプタ 5 つ（`DialectTypeAxis`・正本 `DialectNames.All`）と実 DB の段の方言の派生 5 つ（`DialectTypeRealDbTestsBase`・正本 `DialectNames.All`）・方言の識別子と DDL の表のアダプタ 5 つ（`DialectDdlAxis`・正本 `DialectNames.All`）・形式の往復のアダプタ 5 つ（`FormatAxis`・正本は製品の `DiagramExportFormat` ＋ C# リバース。画像・SQL・スキーマ JSON は理由つきの除外）と形の行の形式の派生 5 つ（`FormatShapeTestsBase`・同じ正本）・チャットエンジンのアダプタ 4 つ（`ChatEngineAxis`・正本は製品の `ErChatBackendKind`。除外なし）・API キー接続のドライバのアダプタ 2 つ（`ChatDriverAxis`・正本は QuickER.AI の `IChatTurnDriver` の具象。振り分けるだけの `ProviderRoutingTurnDriver` は理由つきの除外）・機能モジュールの契約のアダプタ 2 つ（`FeatureModuleContractRows.Axis`・正本は製品の `FeatureModuleCatalog.CreateModules()` の Id。code-generation と db-tools は P3-10 の受け持ちの外として理由つきの除外。`FeatureModuleContractTests.Axis_CoversCatalog` が正本との一致を固定）。
- 方言の DDL の表（`DialectDdlRows`）は、DDL・同期スクリプトの出力をテスト側の読み取り器（`DialectDdlReader`＝アダプタの区切り文字で識別子を読み戻し、製品のクォートの関数を使わない）で構造の行へ写して照合する。全行に 3 つの照合を自動で掛ける：書式のカルチャで出力が変わらず DDL の生成日時の行が西暦であること・入力に CJK が無ければ出力に CJK が無いこと・`Treats` を書いた行は手で書いた扱いが `UnsupportedTreatments` と一致すること（扱いの表はすべてのマスを持つので、台帳に登録の無い行も照合される）。ガード：`DialectDdlTableTests`。
  名乗りの照合は Docker が無くても流れる（ガードの Fact はコレクションにもスキップにも入れない）。
- 運用：
  1. 一覧に載っている組が出るようになったら赤になるので、その項目を一覧から消す。除外も同じ（次元表に載った項目は除外から消す）。
  2. 乱数のエンジンの一覧は、既定の種の決まった列での網羅に合わせて書いてある。種の指紋が変わったら（生成器を変えたら）、指紋と一緒に一覧も書き直す。
  3. 列挙を全数で流すエンジン（水準もテストのループも同じ `Enum.GetValues` から作る）では、列挙に足した値は自動で水準になって実際に流れるので棚卸しは緑のまま。その値の組が出たかは網羅の計数が見る。水準を全数から作ってよいのは、ループや生成器の選び方も同じ列挙から作るときに限る。
- 表の領域を述語で定義するとき（例：生成が通る構成だけを網羅する表）は、述語を正本の規則（テスト側の予測の規則）から参照し、領域の外の組を別の表で網羅する。isValid の原則（原理的に起こり得ない組だけ）とは別の仕組みで、isValid の意味は変えない。
  - 例：生成の構成のエンジン（CodegenConfig）は、全組を網羅して診断の予測を照合する Errors の表と、生成が通る構成だけを網羅してコンパイル・意味・探針・関係を数える Success の表に分ける。Success の領域の述語（`CodegenSuccessDomain`）は診断の規則（`DiagnosticRules`）のエラーの規則を参照する。ガード：`CodegenSuccessMatrixTests.Coverage_MatchesGaps`・`CodegenSuccessMatrixTests.DomainPredicate_ReadsOnlyRuleFactors`。
    - 図の形のキー（`Key`）は 6 水準：int・composite・string・int-nullable（NULL 許容の印のある主キー）・composite-only（どの表も複合主キー）・keyless-only（どの表も主キーなし）。後の 2 つは Repository を作れる表が無い図で、同期の「対象の表が 1 つ以上」のエラーがキーの形を読むので、領域の述語が読む因子（`CodegenSuccessDomain.RuleFactors`）に `Key` を含む。int-nullable の行には関係 PrimaryKeyNotNull を掛ける。
  - 生成の構成のエンジンの決まった行（`CodegenCases*.cs`・`CodegenCaseTests`）は、生成テキストの部分文字列・正規表現・行で判定しない。判定の部品は `CodegenConfig/Judges/` にある：宣言（`CodeRule`＝型・メンバーのシグネチャ・属性と引数・using の集合・意味モデルの参照と呼び出しと引数・`new` 式・Fluent の鎖・XmlDoc の入力由来の文・2 つのメソッドの本体の構文の等価 `SameBody`）・参照を絞ったコンパイル（`ReducedReferences`）・EF Core のモデル（`EfCoreModelFact`）・実行の探針（`ExecCheck`・`ProbeKit`。SQLite の実行は `ProbeKit.WithSqlite`）・製品の解析器を通す判定（`ParsedCheck`）・API リファレンスの Markdown の構文木（`MarkdownRule`）・2 つの生成物の関係（`Relations`）。
    - 全行に掛かる診断の文の性質（どの診断の文にも対になっていないサロゲートが無い）は `CodegenRunner.LoneSurrogateProblems` が診断の段で見る。
    - 簡易 DSL の条件の訳し方は P3-6 で総当たりへ寄せた：意味は `DslConditionFuzzTests`、エミッタの綴りは `QueryConditionCSharpEmitterTests` の表（エミッタの直接の出力）、動作に出ない形の表明だけを元のメソッドに「残す理由（P3-6）：…」で残す。
    - 構造で言えない表明（式本体・SQL Server の SQL の断片・例外の文言・生成器の固定の英文・書式・簡易 DSL の条件の訳し方）は、元のテストのメソッドに文のまま残し、メソッドの前に「残す理由（P3-4b）：…」のコメントで理由と移した先の行を書く。重なる先へ任せて消すのは、同じ入力・同じか強い期待で見ている先だけ（チェックイン済みのフィクスチャで動く Integration のテストは、テンプレートを壊しても再生成するまで赤にならないので重なる先にしない）。
- ガード：TestSupport 直下の `DimensionTableTests` / `CoveringArrayTests` / `CoverageCounterTests` / `ScenarioTableTests` / `MatrixAxisTests` / `EngineRegistryChecksTests` / `FactorInventoryTests`、両アセンブリの `EngineRegistryTests` / `FactorInventoryGuardTests` / `MatrixAxisGuardTests`。mutation 実証済み（理由つきで外した値は緑のまま）。

## 共有の土台

同じことを別々に書いていたテストの土台は、共有の部品を通す。置き場所は `tests/QuickER.Tests` の中。Integration は WPF に依存しない部品だけを Compile のリンクで使う。

1. 生成コードのコンパイル：`CodeGen.CSharp/GeneratedCodeCompiler`（`Compile` / `CompileProject` / `CreateCompilation` / `CompileAndLoad` と、依存を絞った参照集合でコンパイルする `Emit`。構文木のパースは中の 1 か所だけ）と `CodeGen.CSharp/RuntimeReferenceSet`。
   - `RuntimeReferenceSet` は、TPA から ADO・EF Core・DI（指定があれば配布ランタイムの `QuickER.Runtime*`）を外し、許す依存だけを戻す参照集合。許さない ADO・EF Core は、その名前空間（`Microsoft.Data.SqlClient`・`Microsoft.Data.Sqlite`・`Microsoft.EntityFrameworkCore`）に型を宣言するアセンブリをメタデータで見つけて全部外す（`Microsoft.Data.SqlClient.Extensions.Abstractions`・`Npgsql.EntityFrameworkCore.PostgreSQL` などの名前の違う衛星が同じ名前空間に型を置くので、ファイル名だけで外すと許さない依存の `using` が解決されてしまう）。ガード：`RuntimePackageSourceRendererTests` の `Render*_Compiles*`（パッケージ用ソースへ許さない依存の `using` を足すと赤）
   - `Full` は何も外さない集合で、GeneratedCodeCompiler が使う。生成の構成のエンジンのパッケージ参照のコンパイル（`CodegenConfig/Judges/CompileJudge.cs`。`CodegenCaseTests` の行 `compile-package-*`）は 9 つの構成を使う（`QuickER.Runtime*` も外す＝その場でコンパイルした同じ型と CS0433 で衝突させない）。`RuntimePackageSourceRendererTests` は `Create(…, excludeRuntimePackages: false)` を使う。Integration は 2 つともリンクする。
2. 2 つのツールホスト：`TestSupport/TwoHostToolHarness`（WPF 依存なので Engines/ の外）。
   - ホストを行列の軸 `MatrixAxis<IToolHostAdapter>` として登録する。正本の列挙は `ToolHostKind { Gui, File }`。
   - ホストごとに違う部分（ツールの実行・現在の図・図への直接の書き込み）は `ToolHostSession` の抽象メンバー。GUI のセッションは方言を指定せず（MainViewModel の既定）、File は呼び出し側の方言で図ファイルを作る。
   - 使うのは `ToolScenarioTests`（`OpenAligned`＝GUI も 5 方言のレジストリと画面を出さないダイアログで行の方言の図を開き、開けなければ例外・`ToolHostSession.WroteDiagram`＝GUI は Undo の世代と未保存の状態・File はファイルのバイトと、呼び出しの前に過去の固定の値へ戻した更新日時で「図を書いたか」を見る＝同じ内容の書き直しも書いたとみなす）・`NameIdentityToolFuzzTests`・`ErDiagramToolHostParityTests`・`ConstraintNameCreationPathTests`。Parity は 2 つのホストを同時に持って 1 手ずつ比べる。CNCP は DDL と同じ名前を独立の期待値として照合する。ホストどうしを比べるだけでは両方が同時に退行しても緑のままなので、作成経路の照合を Parity へ載せない。
   - `TestSupport/ToolHostAxisGuardTests` は、`ToolHostKind` を引数に取る Theory を反射で集め、ほかの引数の組ごとに軸の全ホストの行（InlineData・MemberData・ClassData）があることを照合する。
   - 前提の図ファイルは `TestSupport/DiagramFiles.Create`（WPF 非依存・create_diagram を実行して成功を表明する）で作る。create_diagram そのものを確かめるテストは使わない。
3. 引数の形の総当たり：`TestSupport/ToolArgumentShapeFuzzRunner`（WPF 非依存）。順は「ベースライン → 崩したケース → `KnownCaseLedger` → 3 つの表明 → 台帳の古い項目 → 網羅の計数 → 最低件数」。3 つの入口は `Entrance` の抽象メンバーだけを渡す。
4. ツール定義の英語のガード：`Mcp/ToolDefinitionEnglishGuardTests`（1 つの Theory・行＝ツール定義の出どころ）。
5. 生成オプション・設定のキー：`TestSupport/OptionKeys`（`Writable` / `IsWritable`＝公開の setter か init を持つ公開のインスタンスのプロパティ）。設定 JSON の照合・CLI のフラグ表・GUI の設定の照合と、因子の棚卸しの `CodeGenerationOptions` の軸が使う。
6. テストの一時フォルダ：`TestSupport/TestTempFolder`（TEMP 直下の固定名の親フォルダの下に guid の名前のフォルダを作り、破棄で中身ごと消す。消せないときは黙って残す）。TEMP 直下に同じ前置きの名前を並べると 8.3 の短い名前がぶつかり、フォルダ 1 つの作成に数百 ms かかるので、直下には固定名の親だけを置く。ファイルを掴むもの（SQLite の接続のプールなど）は作ったテストの側で放す（例：`DbConnectionDialogViewModelTests` の `ReleaseCreatedDatabase`）。
7. ソースの走査のガード：`TestSupport/GuardKnownList.AssertMatches`（見つかった出現と理由つきの既知の一覧を件数まで 2 方向で照合する）と `TestSupport/SourceFiles`（ガードが読むファイルの列挙と、bin / obj の判定 `IsBuildOutput` の唯一の場所。パースはしない）。条件コンパイルの全分岐を読む `ConditionalSourceParse` / `SourceSymbolScan` は各ガードが呼ぶ。

統合の前後で、該当テストのケースの数と合否は同じ（`-list full -preEnumerateTheories` と実行結果の照合）。mutation 実証済み（obj に置いた禁止の呼び出しは bin / obj の判定を外したときだけ赤）。

## 夜間の深い実行

- `.github/workflows/nightly.yml`：トリガーは schedule の `0 18 * * *`（日本時間 3:00）と workflow_dispatch だけ。`permissions: contents: read`。前の実行は取り消さずに待つ。
  - 2 ジョブとも `timeout-minutes: 60`。ubuntu が `QuickER.Tests.Integration`（厳格モード）、windows が `QuickER.Tests`（TEMP / TMP はランナーの一時領域）を、`QUICKER_ENGINE_PROFILE=nightly` と `--filter "NightlyEngine=true"` で流す。
  - アクションは ci.yml か release.yml と同じ commit SHA で固定する。
  - schedule は既定のブランチでだけ動き、リポジトリに 60 日間活動が無いと GitHub が止める。
  - 普段の CI（ci.yml）は変えない。
- テストの結果は TRX で残す：両ジョブのテストの手順が、console の logger に加えて `--logger "trx;LogFileName=<ジョブ名>.trx"` と `--results-directory "${{ runner.temp }}/nightly-results"` を持つ。続く `if: always()` の `actions/upload-artifact` が、ジョブごとに別の名前（`integration-nightly-trx` / `unit-nightly-trx`）で `retention-days: 14` の成果物として保存する（失敗した夜にも残す）。
  - コンソールのログに出ないテストの出力（`ITestOutputHelper` に書く網羅の要約・夜間なら埋まる組・種の数の警告）と、方言の派生ごとの時間（同期計画の総当たり・名前の DDL の往復の派生は同じ表示名なので、TRX のクラス名で見分ける）はこれで読む。
  - ガード：`WorkflowGuardTests.Nightly_JobsKeepTestResultsAsArtifacts`（logger・結果のフォルダ・保存の手順が 1 つ・`if: always()`・path・保持日数・名前の重複なし）と `Nightly_PinsActionsToSameShasAsOtherWorkflows`。
- 夜間の種の数は各エンジンの `SeedSettings` の第 3 引数（`NightlyCount`＝普段の数以上・普段の種を先頭に含む）に置く。ここに表は持たない。
- 選び漏れを防ぐ：`SeedSource.Resolve` は、プロセスの環境変数で種を決めるとき、いま走っているテストが `[Trait("NightlyEngine", "true")]`（夜間に流す）か `"false"`（種を使うが夜間に流さない・理由のコメントを添える）の**ちょうど一方**を宣言していなければ、テストの名前と直し方を書いた `XunitException` で赤にする（`NightlyEngineTrait`。テストの外と、環境の読み取りを差し替えた呼び出しでは検査しない）。
  - どのテストが種を使うかを人が一覧で持たないので、補助を経由する使い方も拾える。種の指紋のドリフトテストを別のカルチャで流し直す `CultureInvariantGenerationTests.DriftTests_PassUnderCulture` の付け忘れも、これで見つかった（`"false"` と理由を付けた）。
- 網羅しない組の夜間の扱い：`CoverageReport.AssertGaps` は、夜間の既定の種の数では「一覧にあるのに網羅した組」を赤にせず、「夜間なら埋まる組」としてテストの出力へ書く（普段の既定の種の数では欠けるので一覧に残す）。一覧に無い欠けは夜間でも赤。
- 既知の失敗の夜間の扱い：乱数の項目の `KnownReproduction` で、夜間に見つけた失敗は `NightlySeeds` で載せる（普段の CI では「起きない」を数えない）。
  夜間の実行で見つけた同期計画の総当たりの失敗は `SyncPlanFuzzKnownFailures` に載せる（中身はその一覧のコメント）。
- 同期計画の総当たりの表明 1b（失敗の後に制約が消えていない）の規則 1〜5（`SyncPlanFuzzRunner.FailedSyncConstraintLosses`）、注 `warned-alter-foreign-key-exempt:{own-failure|collateral}`、違反の文の `(rule n; warned=…; dropped-before-failure=…)`、規則を固定する `SyncPlanFuzzOracleTests`（DB なし・1 規則 1 行の表）は `tests/QuickER.Tests.Integration/Dialects/SyncPlanFuzz/CLAUDE.md`「表明 1b の規則」を参照する。
  1 種の処理の予期しない例外を `seed threw:` として記録して次の種へ進める `SyncPlanFuzzRunner.RunGuardedAsync`（例外でテストごと止まると、それより前の種の違反が隠れる。取り消しは止める）も `tests/QuickER.Tests.Integration/Dialects/SyncPlanFuzz/CLAUDE.md`「1 種の例外」を参照する。
- 同期計画の総当たりは、必須の形の表明と網羅しない組の照合を、普段の既定の種の数が 50 以上の方言だけで行う（夜間に種が増えた PostgreSQL・Oracle では行わない）。既知の失敗の照合と必須の形の表明は両方を集めて 1 つの失敗にする（どちらかの赤がもう一方を隠さない）。失敗した種の警告の内訳と「失敗を予告しない種類の警告しか無いのに失敗した種」は出力する（測るだけ）。
- ガード：`SeedSourceTests`（目印の判定と `Resolve` の検査）・`KnownFailureSetTests.NightlyOnlyRandomEntry_NotObservedOnlyAtNightlyDefaultCount`・`CoverageCounterTests` の夜間の 4 件・`SyncPlanFuzzRunnerGuardTests`（Integration・Docker 不要）・`WorkflowGuardTests` の `Nightly_*` 8 件。`SchemaNamesTests` の目印の付け忘れも `SeedSource` が拾う。mutation 実証済み。

## 種の指紋

- 指紋の要約は機械の環境に依らない：記録は日本語・日本時間の機械で作り、CI は英語・UTC で動く。両アセンブリの指紋のドリフトテストは `SeedFingerprint.Compute` を通し、要約を `SeedSummary.UnderInvariantCulture`（要約を作る間だけ CurrentCulture と CurrentUICulture をインバリアントにする）の中で作る。差分の説明のような resx の文が入っても言語で変わらない。
- ガード：両アセンブリの `EngineSummaryEnvironmentTests`（共有の部品は `SummaryEnvironmentGuard`）。ドリフトテストのクラスに置かない（`CultureInvariantGenerationTests` がドリフトテストのクラスの引数の無いテストを流し直すため）。
  - `Summary_DoesNotDependOnCulture`：全エンジンの先頭 10 種の要約を、外側のカルチャを ja-JP と en-US に切り替えて作り、一致を表明する。切り替わったカルチャの名前も表明する（ICU の無い環境で比べたつもりにならない）。
  - `Summary_HasNoUtcOffset`：要約に `[+-]hh:mm` が無い。タイムゾーンはプロセスの中で切り替えられないので、赤になるのは時差のある機械だけ。
  - `SeedFingerprintTests` の Local の日時と `UnderInvariantCulture` の 2 件。
  - mutation 実証済み。

## GUI テストの土台

- キーボードフォーカス・流したキー入力・ウィンドウの Activated / Deactivated に結果が左右される GUI テストのクラスは `[Collection(TestSupport.WindowActivationTestCollection.Name)]` に入れる（`DisableParallelization`＝並列のテストが全部終わってから単独で走る）。
  - 並列で流すと、ほかのテストが表示したウィンドウにアクティブを奪われ、フォーカスが入らない・Space の判断が変わる形で、全件実行でだけ時々落ちる（単独では合格するので原因に気付けない）。
  - 守る側だけを入れれば足りる（奪う側を入れる必要はない）。ShowActivated=false で表示するだけでフォーカスに触らないテストは入れない。
  - ガード：`WindowActivationCollectionGuardTests` が、目印の API（`IsKeyboardFocus…`・`Keyboard.PrimaryDevice`・`Keyboard.Focus(`・`MoveFocus(`・`Deactivated`）を使うテストソースに属性が付いていることを全数検査する。
- GUI テストが失敗した瞬間の状態は `TestSupport.GuiFailureDiagnostics` が書き出す（全件実行でだけまれに落ちる GUI テストの原因を測るためで、結果は変えない）。
  - `WpfApplicationTestSupport.RunSta` / `RunInIsolatedWindow` が本体を `catch (Exception ex) when (GuiFailureDiagnostics.Capture(ex))` の層で囲む。フィルターは例外処理の 1 回目の走査で、テストがウィンドウを閉じる finally より前に、所有するスレッドで動く。
  - **`Capture` は必ず false を返し、例外を外へ出さない**。true を返すとこの catch が元の失敗を飲み込み、投げると元の失敗が別の例外に置き換わる。区画ごと・出力先ごとに失敗を受け止め「記録に失敗」と書く。
  - 書き出すもの：入力（フォーカス・キャプチャ・左ボタンの状態＝WPF の `Mouse.LeftButton` と実際の `GetAsyncKeyState(VK_LBUTTON)` の両方）、`Track` したウィンドウ、前面のウィンドウ、`CanvasViewportBehavior` / `DragBehavior` / `RubberBandBehavior` の静的フィールド（反射で全部）、`Track` したウィンドウのイベントの時系列。出力先はテストの出力・標準エラー・`%TEMP%\QuickER.Tests-gui-failure-state.log`。
  - 前面のウィンドウが別のプロセスなら、タイトルを書かずプロセス名だけにする（利用者のほかのアプリのタイトルを %TEMP% のログや CI の出力へ残さない）。
  - `GuiFailureDiagnostics.cs` は目印の API を使うがテストを持たないので、`WindowActivationCollectionGuardTests` の除外の一覧に理由つきで載せる。
  - ガード：`GuiFailureDiagnosticsTests`（false を返す・区画が投げても false で例外を出さない・RunSta / RunInIsolatedWindow の失敗が元の例外のまま・閉じる前の状態と時系列・前面の別のプロセスのタイトルを書かない）。mutation 実証済み（前面のタイトルは前面が別のプロセスのときだけ観測できる）。
- Space で押せることを見るテストは、先に `TestSupport.PhysicalMouseButtons.WaitUntilReleased` を通す。
  - 理由：この記録で、実行中に実際のマウスのボタンが押されたままだったために失敗していたと分かった（`Mouse.LeftButton=Pressed`・`GetAsyncKeyState(VK_LBUTTON)` の最上位ビットが立ち、キャプチャがボタンに残る）。WPF の `ButtonBase` は左右のボタンが離されていないと Space の解放で Click しない。製品の不具合ではない。Enter は押下で Click するので関係しない。
  - 動作：左右のボタンが離されるまで約 30 ms ごとにメッセージを処理しながら待つ。約 10 秒で「実際のマウスのボタンが押されたまま」と押されているボタンを名指しして失敗する（スキップしない）。離されていれば待たない。使うのは `CanvasViewportBehaviorSpaceKeyTests` の `SendKey`。
  - ガード：`PhysicalMouseButtonsTests`（ボタンの状態と待ち方を差し替え、押されている間は待って離されたら進む・離されていれば待たない・上限で名指しして失敗する）。mutation 実証済み。

## ソースを走査するガード

- ソースを走査するガードは、条件コンパイル（`#if` / `#elif` / `#else`）のどの分岐も読む。1 通りのプリプロセッサの記号でパースすると、外れる分岐（`#if !DEBUG` の中・`#if QUICKER_*` の `#else`）は無効なテキスト（トリビア）になり、そこに書いた禁止の呼び出しをどのガードも見落とす。
- 構文のガード 13 か所（AppDataPath・PathValidation・Utf8Encoding・SchemaNames・ModelDialectLookup・SchemaQualifiedName・RegexCulture 2・CjkPattern・DialectNameLiteral・SchemaImportWarningText・SyncNameComparison・SyncDialectCapabilitiesNoDefaults・UnsupportedCoverage）は、`TestSupport.ConditionalSourceParse.Parse` と `DescendantNodes` / `DescendantTokens` を通す。
  - `Parse`：ファイルの `#if` / `#elif` の条件に現れる記号を構文から集め、定義の有無のすべての組み合わせで 1 本ずつ構文木を作る。先頭は基準の設定の構文木。記号の無いファイルは 1 本だけ。
  - `DescendantNodes` / `DescendantTokens`：組み合わせをまたいで種類・範囲・トークンの並びが同じ節を 1 回だけにする（分岐の外の呼び出しを組み合わせの数だけ数えない）。範囲だけで比べないのは、分岐を含む節は範囲が同じでも子が違うため。
- シンボルのガード（PathPrefix・ProcessLaunch・RawFileWrite・ToolArgumentParsing・NumericInterpolationCulture）：`SourceSymbolScan` が、条件コンパイルを持つファイルのあるプロジェクト（いまは QuickER.Gui だけ＝`DEBUG` と `QUICKER_*` の 3 つで 16 通り）に、そのファイルの構文木だけを差し替えた変種のコンパイル（`SourceProject.Variants`）を作る。`SourceProject.ScanNodes` が同じ規則で重ねずに返す。既知の一覧の件数は変わらない。変種のコンパイルも BCL の呼び出しを解決できることを `RawFileWriteGuardTests` が表明する。
- 記号は 1 つのファイル（変種はプロジェクト）で 6 個 = 64 通りまで。超えると黙って間引かず、ファイル名を添えて例外にする。
- テストが Roslyn のパース（`ParseText` / `ParseSyntaxTree`）を直接呼ぶのは、`SourceParseEntryGuardTests` の理由つきの一覧（生成器・レンダラーの出力を 1 つの構成でコンパイル・読み取りするもの・入口そのもの・自己検査の対照とプローブ）にあるものだけ。tests/ の全 `.cs` をファイルと件数で厳密に照合する。
- `SourceSymbolScan` は QuickER.Gui の暗黙の using を読む（ファイル名は AssemblyName でなく .csproj の名前で付く）。
- ガード：`ConditionalSourceParseTests`（`#if !DEBUG` と `QUICKER_*` の `#else` の中の呼び出しが、1 通りのパースでは見えず全組み合わせでは見える〔構文とシンボルの両方〕・実ファイルの組み合わせの数＝UpdateFeed.cs 2・FeatureModuleCatalog.cs 8・条件コンパイルの無いファイル 1・分岐の外を重ねない・入れ子の条件・上限・QuickER.Gui の 15 の変種〔エラーなし〕）。mutation 実証済み。

## EF Core のテストの構成

- テストで DbContext のプロバイダを指定する呼び出し（`UseSqlite(` / `UseSqlServer(` / `UseNpgsql(` / `UseOracle(` / `UseMySQL(`）の直後には、必ず `.IgnoreServiceProviderCacheLimit()` を続ける。置き場は `tests/QuickER.Tests/TestSupport/EfCoreTestOptions.cs`（Integration は Compile のリンクで同じ 1 ファイルを使う）。
- 理由：EF Core は構成が違うたびに内部のサービスプロバイダを作ってプロセス全体でキャッシュし、20 を超えると ManyServiceProvidersCreatedWarning（既定でエラー）で、たまたま 21 個目を作った DbContext を失敗させる。テストはフィクスチャごと（値オブジェクトの翻訳の拡張の型が違う）・プロバイダごとに構成が違うので、全件実行で順序しだいに関係の無いテストが落ちる。製品・生成コードは変えず、テストの構成でこの警告だけを無視する。
- ガード：`EfCoreServiceProviderLimitGuardTests`。テストのソースの全呼び出しの直後にこれが続くこと（括弧の対応を読んで検査）と、内部のサービスプロバイダを 25 個作っても失敗しないこと（インスタンスごとにプロバイダが別になるシングルトンのインターセプター＝`IMaterializationInterceptor` で作る。コマンドのインターセプターでは増えない）を固定する。mutation 実証済み。

## SQLite のテストの DB

- テストが自分で作る同期・実装先の一致の SQLite の DB（同期のローカル・サーバー役、BackendParity の SQLite）は、接続を `SqliteLightJournal` で開く。
  - `journal_mode=MEMORY`・`synchronous=OFF`：トランザクションごとのジャーナルのファイルの作成・削除と fsync を省く。ロールバックジャーナルのままなので、ロックの意味は既定と同じ。
  - 生成された接続ファクトリを包んで外部キーの既定を保つ。EF Core は `SqliteLightJournal.EfCoreInterceptor`。
- **`PERSIST` にしない**：Windows ではジャーナルのファイルを開いたまま持ち、同じ DB を既定の接続が書き込むとコミットで消せずに `disk I/O error` になる。
- ガード：`SqliteLightJournalTests`（設定・外部キーの既定・既定の接続との混在）。mutation 実証済み。

## 簡易 DSL の総当たり

- 簡易 DSL の条件の意味は、総当たりで docs の意味と照合する。`DslConditionFuzzTests`（QuickER 版 Repository・EF Core〔いずれも SQLite〕とインメモリ。MySQL の符号なし整数の図はインメモリだけ）と、Integration の `DslConditionFuzzSqlServerRuntimeTests`（実コンテナの QuickER 版 Repository・EF Core〔SQL Server〕・既定はシード 1 つ）が流す。
  - 列ごとに書ける全ての形の原子（演算子・リテラルかパラメータか・否定の有無。値だけ乱数）とその NOT、乱数で組んだ合成の条件を、生成 → 実コンパイル（エラー 0・警告 0）→ NULL を含むデータで実行し、同じ実装先から読み戻した全行へオラクル `DslFuzz.Evaluate` を当てた結果と照合する。
  - 本体は `DslFuzzRunner`、実行部は `DslFuzzExecution`（QuickER.Tests の CodeGen.CSharp。Integration は TestSupport の `DslFuzz*.cs` と一緒に Compile リンクする）。
- **オラクルは docs の写しで、実装の挙動を写さない**：「クエリ（式木 → SQL 変換）」節の NULL の規則（NOT と `<>` は C# の否定と同じ・文字列一致は NULL の行がどちら向きでも一致しない）と、「大文字小文字・空白・日付部品」の表（`DslCaseRule`）。
  - 大文字小文字の扱い：インメモリは序数、SQLite の QuickER 版 Repository は ASCII の英字だけを畳む、EF Core（SQLite）は格納先次第、SQL Server は照合順序。
  - 大文字小文字か末尾の空白だけで結果が変わる行は判定しない。NULL の行と、照合順序に依らず決まる行は判定する。
- 型の検査が断ると決まっているリテラル（ulong を超える値・素の ulong の列に負の値・符号付きの列に long を超える値・値オブジェクトの内包型の範囲外）は、「警告で飛ばされること」を表明する。
- EF Core（SQLite）の decimal の順序比較だけは、プロバイダの制限として `ExcludedOnEfCoreSqlite` の 1 か所で流さない。
- 結果は既知の失敗の一覧 `DslFuzzKnownFailures` と厳密に照合する（一覧に無い失敗も、載っているのに起きない失敗も赤）。一覧は空で保つ。直せない失敗を受け入れるときだけ足す。名前の総当たりの一覧 `NameFuzzKnownFailures` も空。
- キーがシードで揺れないよう、原子の形は列ごとに全て作り、範囲外のリテラルは合成の条件へ混ぜない。
- 黙って空振りしないよう、実装先・単位ごとに照合したクエリが 100 未満なら赤（`DslFuzzRunner.MinimumCheckedQueries`）。
- 網羅の単位：`DslFuzzUnits.Coverage(方言)`（SQLite・SQL Server・MySQL に 1 つずつ・種に依らない＝種の指紋の要約の外。名前は `coverage-{方言}`）を、両テストが種の前に流す（単独の種では流さない）。
  - 単位は主キーを GuidKey にし（値オブジェクトあり）、主キーを参照する NULL 許容の自己参照の外部キー列 `parent` を持つ（`DslFuzzUnit.SelfReference`＝BuildDiagram が自己参照のリレーションを足し、外部キーの値オブジェクトの統一で GuidKey になる。行の parent はその行より前の行のキーか NULL）。
  - 列は種の単位と同じ。MySQL は種の単位に無い固定長の文字列・符号つき整数・decimal・日時も持つ。
  - クエリは `DslFuzzGenerator.CoverageQueries`（乱数はこの中だけで引き、`Queries` の引き方に触れない）。網羅の単位で初めて現れる列の種類の列の原子のクエリと、戻り形 4 つと射影の非 NULL 指定のそれぞれで、原子の形・自身の否定・列の種類のまだ出ていない水準を足す原子を貪欲に選んで 6 つずつ OR でまとめ、1 つおきに NOT で包んだ合成のクエリ。型の検査が断る原子と EF Core（SQLite）が流さない decimal の順序比較は使わない。射影の非 NULL 指定は、射影する列の IS NOT NULL と AND でつなぐ。
  - 単位のクエリは、型の検査が断ると決まっている原子のものを除き、構成のどの実装先でも照合したクエリにならなければ、`DslFuzzCoverage.UncheckedProblems` が単位・範囲・クエリを名指しして赤にする。そのため網羅の単位には `MinimumCheckedQueries` を掛けない（`DslFuzzRunner.RunAsync` の `minimumChecked`）。
  - NOT の入れ子：次元表の因子 NotNesting（none・single・double・triple・group-and・group-or＝原子の直上の NOT の連鎖の段数か、直上に NOT が無ければ最も近い NOT が包む AND / OR）を `DslFuzzDimensions.Observe` が構文木から数える。`isValid` は「NOT が無ければ NOT の数は偶数」「原子だけのクエリ（原子 1 つかその NOT 1 つ）は 2 段以上・合成を包む NOT を持たない」の 2 つだけ。網羅の単位は、形・否定・列の種類の全水準を足す原子を 6 水準ごとにその形で包んだ一覧のクエリ（1 つおきに外側をもう 1 つの NOT で包む）と、一覧以外の戻り形・射影の非 NULL 指定ごとに 6 水準を 1 つずつ持つクエリで、種に依らず全組を埋める。
  - 決まった形のクエリ（`DslFuzzGenerator.FixedQueries`）：NOT NOT 原子・NOT NOT NOT 原子・NOT (AND (比較, IS NULL))・NOT (LIKE OR 比較)・肯定の LIKE と同じ LIKE と IS NULL の OR の NOT・前置 NOT と NOT LIKE の重ね・整数の列の範囲の両端のリテラル。値オブジェクトなしの経路は SQLite の `DslFuzzUnits.PlainCoverage`（種の単位と同じ列・決まった形のクエリだけ）が流す。
  - 特殊な文字列とカルチャ：SQLite の 2 単位（`Coverage`・`PlainCoverage`）だけ、列 `s` に合成済みの「が」・分解形の「か＋濁点」・ソフトハイフンを無視すると a で始まる値を入れ、前方・後方・部分一致とソフトハイフンの LIKE を足す（SQL Server は照合順序で合成済みと分解形が等しくなるので足さない）。網羅の単位は書式のカルチャをインバリアントにして流し、SQLite の 2 単位の序数のクエリ（`DslFuzzGenerator.OrdinalQueryPrefix`＝KO0〜KO3）だけをインメモリの範囲で ja-JP・tr-TR でも流し直す（インメモリは序数＝オラクルは `DslCaseRule.Ordinal` のまま。カルチャで結果が変わり得るのはインメモリの文字列一致の比べ方だけなので、ほかのクエリは周回しない）。
  - 網羅しない組の一覧は、受け入れ（方言が列の型として持てない種類・EF Core の SQLite プロバイダの decimal の順序比較）だけ。
- mutation 実証済み。

## カルチャのガード

規則：書き出す文字列（DDL・同期スクリプト・型の表記・生成コード・エクスポート・ツールの応答）は、書式のカルチャに依らず同じ。

- 数値・日時をそのまま補間すると、スウェーデン語（sv-SE）の負号 U+2212・タイ語（th-TH）の仏暦がそのまま出る（負のスケールの `numeric(10,−2)` はデータベースが断る・DDL の生成日時が 2569 年になる）。書き出す数値・日時は `string.Create(CultureInfo.InvariantCulture, …)` か `ToString(…, CultureInfo.InvariantCulture)` で書く。
- 大文字小文字を無視する正規表現は `RegexOptions.CultureInvariant` を併せて付ける。`new Regex` の静的なフィールドは型を初めて使った時点のカルチャで作られ、トルコ語の Windows では `AUTOINCREMENT` が `autoincrement` に一致しない（SQLite の再構築の警告が `AUTOINCREMENT`・`DEFERRABLE`・`WITHOUT ROWID`・`STRICT` を落とし、モックの検証が `@IMPORT` を見落とす）。`[GeneratedRegex]` は実測では tr-TR でも一致したが、同じ規則に含める。
- アナライザの設定：CA1304・CA1309・CA1310・CA1311 を警告（＝エラー）にする。CA1305・CA1307 は入れない（理由は `.editorconfig` のコメント）。
  - CA1305 を src で有効にすると診断が多く出るうえ、文字列への素の補間（`$"…"`）と連結を診断しない（診断するのは `string.Format`・`ToString()`・`Convert` と `StringBuilder.Append` / `AppendLine` へ渡した補間など）。入れずに `NumericInterpolationCultureGuardTests` で数える。
  - テストで翻訳器が受け付ける・断る呼び出しの形そのもの（式木の `Equals(string)`・`ToUpper()`）を確かめる箇所だけ、理由つきの `#pragma` で抑える。
- 生成コード（ランタイムのパッケージ用ソースとフィクスチャ）は、先頭の `// <auto-generated />` と `.g.cs` の名前でビルドのアナライザから外れる。`GeneratedCodeCultureAnalyzerTests` が同じ 4 規則を `generated_code = false` の設定で掛け、診断を理由つきの既知の一覧（いまは空）と厳密に照合する。アナライザは src と同じ SDK 同梱の DLL で、テストの csproj が `@(Analyzer)` の場所を AssemblyMetadata で渡す。
- これらの規則は式木の中の呼び出しを診断しない（翻訳される比較なので多重定義を変えられない）。簡易 DSL と手書きの LINQ の条件のインメモリでの比べ方は、実行器の `PredicateComparisonRewriter` が `string.StartsWith(string)` / `EndsWith(string)` を序数の多重定義へ書き換えて揃える。`Contains(string)` と値オブジェクトの文字列一致は元から序数（docs の「インメモリは序数」）。
- 正規表現の数字は `[0-9]` で書き、`\d` は `RegexOptions.ECMAScript` を付けたときだけ使う。.NET の `\d` は全角などの Unicode の数字にも一致し、インバリアントの数値の解析は ASCII しか読まない。SQL Server の型の表記の正規化と型カタログが `FormatException` で落ち、`SqlTypeText.IsSafe` は DB が断る `varchar(５０)` を安全とみなす。
- Excel 定義書の `UQ{n}` は、後ろに Unicode の数字が続けば一致させない（`UQ1２` を `UQ1` と読まない）。
- 名前の並びで決める値（`SchemaSignature` のテーブルとリレーション）は序数で並べる。カルチャが等しいとみなす合成済みの文字と分解形・ソフトハイフンの有無だけが違う名前は、既定の比較では入力の順が残って置き換えの確認が余計に出る。
- 数値の補間のガード `NumericInterpolationCultureGuardTests`：`QuickER.Provider*` と `QuickER.Dialects` を `SourceSymbolScan` で組み、`ScanNodes` で条件コンパイルのどの分岐も読む。補間の穴と文字列の連結の被演算子のうち、型が数値（整数・decimal・double・float）か日時（DateTime・DateTimeOffset・DateOnly・TimeOnly・TimeSpan。Nullable も）のものをシンボルで数え、第 1 引数が `CultureInfo.InvariantCulture` の `string.Create` の中は数えない。ファイルと件数で理由つきの既知の一覧と厳密に照合する。
  - 理由の分類は「利用者向けの文言」「例外のメッセージ」「負にならない値」。いまは「負にならない値」の 3 件だけ（プロセスの外へ出ない照合のキー＝`SchemaDiffService` の外部キーの署名・`UniqueConstraintNaming` の列の集合の署名・`QueryParameterTypeResolver` の仮の列名）。
  - 書き出す型の表記（取込の `FormatDataType` と 5 方言の型カタログ・SQL Server の型の表記の正規化）・同期スクリプトの見出し・接続文字列のポート・SQLite の取込の外部キー名・Oracle の説明の `CHR(n)`・識別子の上限の英語の表記（例外文と MCP の注意文）は、一覧に載せず `string.Create` へ直した。
  - 整数の書式がカルチャで変わるのは負号だけ（.NET は数字を置き換えず、既定の書式は桁区切りを入れない）。直した箇所はどれも負の値でだけ食い違う。カタログは負の長さを返さない。
- その他のガード：
  - `CultureInvariantDialectOutputTests`：5 方言の型の解析と変換と正規化を、`CultureScope.Cultures`（tr-TR・de-DE・ja-JP・sv-SE・th-TH）でインバリアントと照合する。DDL・同期スクリプトは `DialectDdlTableTests` の全行に掛かる自動の照合が、同じカルチャでインバリアントと同じこと・DDL の生成日時の行があって西暦であることを確かめる（固定フィクスチャの定義と方言の型の選択肢を載せた図は `DialectDdlRows.Culture.cs` の行）。`CultureInvariantDialectOutputTests.ImportedTypeSpelling_IsCultureInvariant` は、取込が DB のカタログから型の表記を組み立てる関数（SQL Server・Oracle の `FormatDataType`・PostgreSQL の `NormalizeFormatType`・MySQL の `MySqlTypeNotation.Normalize`）へ、負の値を含む長さ・精度・スケールの組み合わせを総当たりで流す（SQLite は宣言の型をそのまま入れるので対象に無い）。
  - `CultureInvariantGenerationTests`：生成物の全ドリフトテストを各カルチャで流し直す。
  - `CultureInvariantExportTests`：SVG の出力。Mermaid・DBML・HTML・Excel の出力と、Mermaid・DBML・Excel・C# リバースの取込は、形式の往復のエンジンのカルチャの代表の形（`FormatRoundTripTests.CultureProbe_IsInvariant`・形の行 `fixture-generated`・`culture-turkish-i`）。
  - `CultureInvariantToolOutputTests`：MCP の要約・変更系ツールが保存する図。名前付きクエリの一覧は `QueryToolFormatterTests` の行 `formatter-listing-is-culture-invariant-english`。
  - `RegexCultureGuardTests`：src の `RegexOptions.IgnoreCase` を含むビット和に `CultureInvariant` があることとインラインの `(?i` が無いことの構文木の全数走査と、文字列リテラルの `\d`（奇数個の `\` に続く `d`）が `ECMAScript` の付いた呼び出し・属性の引数の外に無いこと。
  - 全角の数字：`DialectTypeTableTests` の `fullwidth-*` の行が、全角の数字を含む型の表記を 5 方言の型カタログ・型マッパー・正規化と `SqlTypeText.IsSafe` へ流し、例外にならず数字として読まないことを確かめる。`FullWidthDigitTypeNotationTests` が型トークン・長さだけの違いの判定・Mermaid の型の復元・Excel の `UQ` 番号で同じことを確かめる。
  - `GeneratedCodeCultureAnalyzerTests`：ランタイム 7 本と全フィクスチャ。自己検査は、auto-generated の印のあるソースで 4 規則が出て式木の中では出ないこと。
  - `InMemoryStringMatchOrdinalTests`：値オブジェクトの有無 × カルチャで、手書きの LINQ の前方・後方一致を SQLite の QuickER 版 Repository とインメモリで照合する。簡易 DSL の LIKE は `DslConditionFuzzTests`（網羅の単位 `DslFuzzUnits.Coverage`・`DslFuzzUnits.PlainCoverage` の序数の値と、序数のクエリ KO0〜KO3 だけをインメモリの範囲で ja-JP・tr-TR にも周回する）。
  - `SchemaSignatureTests` の並び順の 2 件。`MockResumePromptTests` と `ClipboardImageAttachmentReaderTests` の各カルチャの日時。
  - `TableRebuildAttributeDetectorCultureTests`：`QuickER.Provider.dll` を新しい読み込みコンテキストへ読み込み直し、tr-TR の下で型を初期化した実挙動を見る。読み込み直したアセンブリは回収までほかのテストから同じ名前の写しとして見えるので、非並列のコレクション `IsolatedAssemblyLoadTestCollection` で走らせ、回収を確かめてから戻る。WPF を含むアセンブリは読み込み直さない（pack URI が写しへ解決され、全件実行でだけウィンドウの `InitializeComponent` が失敗する）。
  - `CultureInvariantRuntimeTextTests`：同期のキーのテキストの decimal（`SyncKeyText.FormatDecimal` / `ParseDecimal` / `WithoutDecimalExponent`）と SQLite の `PRAGMA busy_timeout`（`SqlConnectionFactory`・`SqliteBusyTimeoutInterceptor`）を各カルチャで。組み立てる側で InvariantCulture を渡しているのを固定するだけ。
- src に `ToUpper()`・`EndsWith(string)`・`InvariantCulture` の比較を書くと、4 規則ともビルドが止まる。mutation 実証済み。
