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
    - `tests/QuickER.Tests/TestSupport/EngineRegistry.cs`：Dsl/SqliteMySql・Name/Entrances・DiagramEdit/ToolArgument・DiagramEdit/Undo・Dialect/Types・Dialect/Identifiers・Dialect/DdlText・Dialect/ImportWarnings・DocumentFile/Scenarios・DocumentFile/Random・DocumentFile/Format・DocumentFile/Settings・FormatRoundTrip/RoundTrip・FormatRoundTrip/Omissions・FormatRoundTrip/Rules・FormatRoundTrip/Merge・Ai/ChatEngines・Ai/Drivers・Ai/Attachments・Ai/ConnectionSettings・Ai/ApiKeyStore・Ai/MarkdownRender・Ai/ChatDialog・OsBoundary/ProcessLaunch・OsBoundary/ShellPermission・OsBoundary/PathEntrances・OsBoundary/AtomicFile・OsBoundary/Containment・OsBoundary/Segments・OsBoundary/Lifecycle・OsBoundary/PostExitExhaustive・OsBoundary/ShutdownChainExhaustive・OsBoundary/FeatureModules・OsBoundary/ProfileStore・Ai/MockAgents・Ai/MockRunner・Ai/MockBuildBoundary・Ai/MockScaffold・Ai/MockFolder・Ai/MockOutputs・Ai/MockDialog・Ai/DbConnectionDialog・Ai/DbImportSync・Guard/SourceRules・Guard/Parity・Guard/Phrases。
    - `tests/QuickER.Tests.Integration/EngineRegistry.cs`：SyncModel/Random・SyncModel/Outcomes・SyncModel/Config・SyncPlan/Fuzz・SyncPlan/CapabilityNet・SyncPlan/ShapeRows・SyncPlan/HistoryRows・GeneratedRuntime/Parity・GeneratedRuntime/ParityOutcomes・Dsl/SqlServer・Name/DdlRoundTrip・Dialect/TypesRealDb。Guard は QuickER.Tests の台帳に載せる（Integration にはガードが無い）。
  - 因子の棚卸し：`FactorInventoryGuard` は、製品の仕様の軸の全項目が、そのアセンブリの登録簿の次元表の Source に載っているか、理由つきの除外（担当のエンジン ID・区切り・空や仮置きでない理由）の**ちょうど一方**であることを検査する。
    14 の軸（設計 2.9 の 13 軸と P3-8 の `ExportOmissionKind`）をどちらのアセンブリが見るかは `FactorInventoryAssignment` が決め、各ガードは自分の軸の並びが割り当てと一致することを表明する。
  - 行列の軸：`MatrixAxis<TAdapter>` は、正本の値ごとにアダプタがちょうど 1 つあるか、理由つきで外してあるかを構築時に検査する。`[MatrixAxisValue]` と `MatrixAxisGuard.DerivedClassesCover`（基底か目印のインターフェースの具象の派生と正本の照合）、`DeclaredValuesMatch`（派生の名乗る値と実際の値の照合）で固める。
    値ごとの違いはアダプタの抽象メンバーにし、アダプタの中で値を名指しして分岐しない。
- 各エンジンの次元表・一覧・記述は、エンジンの隣の `*Dimensions.cs` に置く：`SyncModelDimensions`・`SyncModelOutcomeDimensions`・`SyncConfigDimensions`・`SyncPlanFuzzDimensions`・`BackendParityDimensions`・`NameFuzzDdlRoundTripDimensions`・`DslFuzzDimensions`・`NameFuzzDimensions`・`ToolArgumentFuzzDimensions`・`RelationshipTypeChangeDimensions`・`DialectTypeDimensions`・`DialectTypeRealDbDimensions`・`DialectIdentifierDimensions`・`DialectDdlDimensions`・`DialectImportWarningDimensions`・`DocumentFileDimensions`・`DocumentFormatDimensions`・`SettingsStoreDimensions`・`FormatRoundTripDimensions`・`ChatEngineDimensions`・`ChatDriverDimensions`・`AttachmentListDimensions`・`ConnectionSettingsDimensions`・`ApiKeyStoreDimensions`・`ChatMarkdownDimensions`・`ChatDialogDimensions`・`ProcessLaunchDimensions`・`ShellPermissionDimensions`・`PathEntranceDimensions`・`AtomicFileDimensions`・`PathContainmentDimensions`・`PathSegmentsDimensions`・`LifecycleDimensions`・`PostExitExhaustiveDimensions`・`ShutdownChainExhaustiveDimensions`・`FeatureModuleContractDimensions`・`ProfileStoreDimensions`・`MockAgentDimensions`・`MockRunnerDimensions`・`MockBuildBoundaryDimensions`・`MockScaffoldDimensions`・`MockFolderDimensions`・`MockOutputsDimensions`・`MockDialogDimensions`・`ConnectionDialogDimensions`・`ImportSyncDimensions`・`GuardDimensions`・`GuardTextDimensions`。
- 乱数で操作の列を作るエンジン（同期のモデル・同期計画・実装先の一致）の `Observe` は、生成器が返し終えた値と、コンストラクタで決まる公開プロパティだけを読む（乱数を引かない・生成器の中に置かない。種の指紋が守る）。
- 行列の軸として登録したもの：同期計画の総当たりと名前の DDL の往復の方言の派生 5 つずつ（正本 `DialectNames.All`）・同期のモデルの転送経路（正本 `SyncModelTransport`・目印 `ISyncModelTransportTests`）・実装先の一致（`BackendParityAxis`＝`ParityScenario.Backends` の並びの出どころ）・簡易 DSL の実装先（`DslFuzzBackends.Names` と構成ごとの軸）・方言の型の表のアダプタ 5 つ（`DialectTypeAxis`・正本 `DialectNames.All`）と実 DB の段の方言の派生 5 つ（`DialectTypeRealDbTestsBase`・正本 `DialectNames.All`）・方言の識別子と DDL の表のアダプタ 5 つ（`DialectDdlAxis`・正本 `DialectNames.All`）・形式の往復のアダプタ 5 つ（`FormatAxis`・正本は製品の `DiagramExportFormat` ＋ C# リバース。画像・SQL・スキーマ JSON は理由つきの除外）と形の行の形式の派生 5 つ（`FormatShapeTestsBase`・同じ正本）・チャットエンジンのアダプタ 4 つ（`ChatEngineAxis`・正本は製品の `ErChatBackendKind`。除外なし）・API キー接続のドライバのアダプタ 2 つ（`ChatDriverAxis`・正本は QuickER.AI の `IChatTurnDriver` の具象。振り分けるだけの `ProviderRoutingTurnDriver` は理由つきの除外）・機能モジュールの契約のアダプタ 4 つ（`FeatureModuleAxis.Axis`・正本は製品の `FeatureModuleCatalog.CreateModules()` の Id。除外なし。モジュールごとの違い〔解決できるサービス・ツールバーの並び・終了失敗の記録先の要否・ホストのイベントへの追従〕はアダプタの宣言。`FeatureModuleContractTests.Axis_CoversCatalog` が正本との一致を固定）・モックプロジェクト生成の実行器のアダプタ 4 つ（`MockAgentAxis`・正本は QuickER.AI.Mock の `IMockProjectAgent` の具象の型の名前。除外なし。`MockAgentContractTests.Axis_CoversAuthoritativeValues` が固定）。
- 方言の DDL の表（`DialectDdlRows`）は、DDL・同期スクリプトの出力をテスト側の読み取り器（`DialectDdlReader`＝アダプタの区切り文字で識別子を読み戻し、製品のクォートの関数を使わない）で構造の行へ写して照合する。全行に 3 つの照合を自動で掛ける：書式のカルチャで出力が変わらず DDL の生成日時の行が西暦であること・入力に CJK が無ければ出力に CJK が無いこと・`Treats` を書いた行は手で書いた扱いが `UnsupportedTreatments` と一致すること（扱いの表はすべてのマスを持つので、台帳に登録の無い行も照合される）。網羅の照合（`DialectDdlTableTests.Coverage_MatchesDeclaredGaps`）は、行が宣言した観点に加えて、自動の照合が実際に走った観点（カルチャ・英語）も数える。数えるのは照合の経路（`DialectDdlRunner.Check` の `autoCheckRan` の通知）で、英語の照合は入力に CJK が無いときだけ走る。照合の呼び出しを外せばその組が欠けに戻って赤になる（全行に掛かる「はず」で数えない）ので、網羅の照合は行を実際に流す。ガード：`DialectDdlTableTests`。
- 方言の型の表（`DialectTypeRows`）の網羅は、宣言のセル（`DialectTypeRows.Matrix.cs` の `MatrixCells`＝全正規型 × 5 方言の書き出しの表記と読み戻しの種類・再書き出しの表記・取込の表記）から期待を導く生成の行（`matrix-<観点>-*`）が持つ。行は手で書かず、観点ごとの生成関数（`DialectTypeRows.Matrix{Text,Canonical,Derived}.cs`）が作る。表記を入力にする観点は（方言, 表記）ごとの行、正規型・型トークンを入力にする観点と方言切替は 1 行を 5 方言で流す。解析できない表記・読めない引数は、解析の行（期待 `fail`）の入力から導く（一覧を 2 回書かない）。
  - 宣言のセルから期待を導く行は、製品が宣言どおりに動くかを確かめるだけで、宣言そのものの誤りは捕まえない（同語反復になる面がある）。宣言の正しさは docs の方言の表の照合（`DialectDocsTableTests`）と実 DB の段（`DialectTypeRealDbTestsBase`）が受け持つ。マッパーの行の「種類 → C# の型」の表（`ClrTypeOf`）は 5 方言の独立した実装を同じ表へ突き合わせるので、同語反復にならない。
  - 網羅しない組は受け入れ 1 組だけ（取込の表記 × 読めない引数：取込の入力は DB のカタログの値で、読めない引数を作る入力ではない）。ガード：`DialectTypeTableTests`（`Coverage_MatchesDeclaredGaps`・`ParsedKinds_MatchCatalog`）。
  名乗りの照合は Docker が無くても流れる（ガードの Fact はコレクションにもスキップにも入れない）。
- 運用：
  1. 一覧に載っている組が出るようになったら赤になるので、その項目を一覧から消す。除外も同じ（次元表に載った項目は除外から消す）。
  2. 乱数のエンジンの一覧は、既定の種の決まった列での網羅に合わせて書いてある。種の指紋が変わったら（生成器を変えたら）、指紋と一緒に一覧も書き直す。
  3. 列挙を全数で流すエンジン（水準もテストのループも同じ `Enum.GetValues` から作る）では、列挙に足した値は自動で水準になって実際に流れるので棚卸しは緑のまま。その値の組が出たかは網羅の計数が見る。水準を全数から作ってよいのは、ループや生成器の選び方も同じ列挙から作るときに限る。
  4. 網羅しない組を埋める行の期待は、製品の XmlDoc・CLAUDE.md から導く。文書に定めが無く今の振る舞いを写すだけの行は、行の説明の先頭に「特性の固定（文書に定めが無い今の振る舞い）：」と書き、根拠の実装のファイル:行をコメントに添える（文書に定めができたら書き直す。方針を黙って決めてしまう振る舞いは特性として固定せず、理由つきで残す）。
- 表の領域を述語で定義するとき（例：生成が通る構成だけを網羅する表）は、述語を正本の規則（テスト側の予測の規則）から参照し、領域の外の組を別の表で網羅する。isValid の原則（原理的に起こり得ない組だけ）とは別の仕組みで、isValid の意味は変えない。
  - 例：生成の構成のエンジン（CodegenConfig）は、全組を網羅して診断の予測を照合する Errors の表と、生成が通る構成だけを網羅してコンパイル・意味・探針・関係を数える Success の表に分ける。Success の領域の述語（`CodegenSuccessDomain`）は診断の規則（`DiagnosticRules`）のエラーの規則を参照する。ガード：`CodegenSuccessMatrixTests.Coverage_MatchesGaps`・`CodegenSuccessMatrixTests.DomainPredicate_ReadsOnlyRuleFactors`。
    - 図の形のキー（`Key`）は 6 水準：int・composite・string・int-nullable（NULL 許容の印のある主キー）・composite-only（どの表も複合主キー）・keyless-only（どの表も主キーなし）。後の 2 つは Repository を作れる表が無い図で、同期の「対象の表が 1 つ以上」のエラーがキーの形を読むので、領域の述語が読む因子（`CodegenSuccessDomain.RuleFactors`）に `Key` を含む。int-nullable の行には関係 PrimaryKeyNotNull を掛ける。
  - 生成の構成のエンジンの決まった行（`CodegenCases*.cs`・`CodegenCaseTests`）は、生成テキストの部分文字列・正規表現・行で判定しない。判定の部品は `CodegenConfig/Judges/` にある：宣言（`CodeRule`＝型・メンバーのシグネチャ・属性と引数・using の集合・意味モデルの参照と呼び出しと引数・`new` 式・Fluent の鎖・XmlDoc の入力由来の文・2 つのメソッドの本体の構文の等価 `SameBody`）・参照を絞ったコンパイル（`ReducedReferences`）・EF Core のモデル（`EfCoreModelFact`）・実行の探針（`ExecCheck`・`ProbeKit`。SQLite の実行は `ProbeKit.WithSqlite`）・製品の解析器を通す判定（`ParsedCheck`）・API リファレンスの Markdown の構文木（`MarkdownRule`）・2 つの生成物の関係（`Relations`）。
    - 全行に掛かる診断の文の性質（どの診断の文にも対になっていないサロゲートが無い）は `CodegenRunner.LoneSurrogateProblems` が診断の段で見る。
    - 主キーの列に NULL 許容の印がある図を持つのは、関係 PrimaryKeyNotNull を掛けた行だけ（`Column.IsNullable` の既定は true なので、書き忘れた図が生成の入口の読み替えの経路を黙って通る）。ガード：`CodegenCaseTests.NullablePrimaryKeyDiagrams_AreOnlyInPrimaryKeyNotNullRows`。
    - 簡易 DSL の条件の訳し方は P3-6 で総当たりへ寄せた：意味は `DslConditionFuzzTests`、エミッタの綴りは `QueryConditionCSharpEmitterTests` の表（エミッタの直接の出力）、動作に出ない形の表明だけを元のメソッドに「残す理由（P3-6）：…」で残す。
    - 構造で言えない表明（式本体・SQL Server の SQL の断片・例外の文言・生成器の固定の英文・書式・簡易 DSL の条件の訳し方）は、元のテストのメソッドに文のまま残し、メソッドの前に「残す理由（P3-4b）：…」のコメントで理由と移した先の行を書く。重なる先へ任せて消すのは、同じ入力・同じか強い期待で見ている先だけ（チェックイン済みのフィクスチャで動く Integration のテストは、テンプレートを壊しても再生成するまで赤にならないので重なる先にしない）。
- ガード：TestSupport 直下の `DimensionTableTests` / `CoveringArrayTests` / `CoverageCounterTests` / `ScenarioTableTests` / `MatrixAxisTests` / `EngineRegistryChecksTests` / `FactorInventoryTests`、両アセンブリの `EngineRegistryTests` / `FactorInventoryGuardTests` / `MatrixAxisGuardTests`。mutation 実証済み（理由つきで外した値は緑のまま）。

## 共有の土台

同じことを別々に書いていたテストの土台は、共有の部品を通す。置き場所は `tests/QuickER.Tests` の中。Integration は WPF に依存しない部品だけを Compile のリンクで使う。

1. 生成コードのコンパイル：`CodeGen.CSharp/GeneratedCodeCompiler`（`Compile` / `CompileProject` / `CreateCompilation` / `CompileAndLoad` と、依存を絞った参照集合でコンパイルする `Emit`。構文木のパースは中の 1 か所だけ）と `CodeGen.CSharp/RuntimeReferenceSet`。
   - 構文木はリストへ確定してから `CSharpCompilation.Create` へ渡す（`CreateCompilationCore`）。`Create` は受け取った列挙を中で 3 回なめるので、遅延の `Select` のまま渡すと同じファイルを 3 回パースする（生成の構成のエンジンでパースの時間が約 1.8 倍）。結果は変わらず遅くなるだけなので、ガードは置かない。
   - `RuntimeReferenceSet` は、TPA から ADO・EF Core・DI（指定があれば配布ランタイムの `QuickER.Runtime*`）を外し、許す依存だけを戻す参照集合。許さない ADO・EF Core は、その名前空間（`Microsoft.Data.SqlClient`・`Microsoft.Data.Sqlite`・`Microsoft.EntityFrameworkCore`）に型を宣言するアセンブリをメタデータで見つけて全部外す（`Microsoft.Data.SqlClient.Extensions.Abstractions`・`Npgsql.EntityFrameworkCore.PostgreSQL` などの名前の違う衛星が同じ名前空間に型を置くので、ファイル名だけで外すと許さない依存の `using` が解決されてしまう）。ガード：`RuntimePackageSourceRendererTests` の `Render*_Compiles*`（パッケージ用ソースへ許さない依存の `using` を足すと赤）
   - `Full` は何も外さない集合で、GeneratedCodeCompiler が使う。生成の構成のエンジンのパッケージ参照のコンパイル（`CodegenConfig/Judges/CompileJudge.cs`。`CodegenCaseTests` の行 `compile-package-*`）は 9 つの構成を使う（`QuickER.Runtime*` も外す＝その場でコンパイルした同じ型と CS0433 で衝突させない）。`RuntimePackageSourceRendererTests` は `Create(…, excludeRuntimePackages: false)` を使う。Integration は 2 つともリンクする。
2. 2 つのツールホスト：`TestSupport/TwoHostToolHarness`（WPF 依存なので Engines/ の外）。
   - ホストを行列の軸 `MatrixAxis<IToolHostAdapter>` として登録する。正本の列挙は `ToolHostKind { Gui, File }`。
   - ホストごとに違う部分（ツールの実行・現在の図・図への直接の書き込み）は `ToolHostSession` の抽象メンバー。GUI のセッションは方言を指定せず（MainViewModel の既定）、File は呼び出し側の方言で図ファイルを作る。
   - 使うのは `ToolScenarioTests`（`OpenAligned`＝GUI も 5 方言のレジストリと画面を出さないダイアログで行の方言の図を開き、開けなければ例外・`ToolHostSession.WroteDiagram`＝GUI は Undo の世代と未保存の状態・File はファイルのバイトと、呼び出しの前に過去の固定の値へ戻した更新日時で「図を書いたか」を見る＝同じ内容の書き直しも書いたとみなす）・`NameIdentityToolFuzzTests`・`ErDiagramToolHostParityTests`・`ConstraintNameCreationPathTests`。Parity は 2 つのホストを同時に持って 1 手ずつ比べる。CNCP は DDL と同じ名前を独立の期待値として照合する。ホストどうしを比べるだけでは両方が同時に退行しても緑のままなので、作成経路の照合を Parity へ載せない。
   - `TestSupport/ToolHostAxisGuardTests` は、`ToolHostKind` を引数に取る Theory を反射で集め、ほかの引数の組ごとに軸の全ホストの行（InlineData・MemberData・ClassData）があることを照合する。
   - 前提の図ファイルは `TestSupport/DiagramFiles.Create`（WPF 非依存・create_diagram を実行して成功を表明する）で作る。create_diagram そのものを確かめるテストは使わない。
3. 引数の形の総当たり：`TestSupport/ToolArgumentShapeFuzzRunner`（WPF 非依存）。順は「ベースライン → 崩したケース → `KnownCaseLedger` → 3 つの表明 → 台帳の古い項目 → 網羅の計数 → 最低件数」。3 つの入口は `Entrance` の抽象メンバーだけを渡す。
4. ツール定義の英語のガード：`PhraseTests`（英語の行 `tool-definitions-*`＝ツール定義の出どころごとの行。文言の表 `TestSupport/Guards/EnglishTextRows.Mcp.cs`）。
5. 生成オプション・設定のキー：`TestSupport/OptionKeys`（`Writable` / `IsWritable`＝公開の setter か init を持つ公開のインスタンスのプロパティ）。設定 JSON の照合・CLI のフラグ表・GUI の設定の照合と、因子の棚卸しの `CodeGenerationOptions` の軸が使う。
6. テストの一時フォルダ：`TestSupport/TestTempFolder`（TEMP 直下の固定名の親フォルダ＝`TestTempFolder.Root` の下に guid の名前のフォルダを作り、破棄で中身ごと消す。消せないときは黙って残す）。Integration も同じ 1 ファイルを Compile リンクで使う。
   - テストは一時フォルダを自分で組まない（親の名前のリテラル・`Directory.CreateTempSubdirectory` を書かない）。写すと消し忘れと失敗の経路での消し損ねも写り、%TEMP% に残りが溜まる。作るフォルダはインスタンス（クラスのフィールド＋ Dispose か `using var`）で作る。作らない偽のパス（存在の判定を差し込むもの）だけは `TestTempFolder.Root` から組む。ガード：`SourceRuleTests`（規則 `test-temp-folder-root-copies`。tests の全体の、親の名前を含む文字列リテラル・補間の文字の部分と `CreateTempSubdirectory` の呼び出しを構文木で数える＝条件コンパイルのどの分岐も読む）。
   - TEMP 直下に同じ前置きの名前を並べると 8.3 の短い名前がぶつかり、フォルダ 1 つの作成に数百 ms かかるので、直下には固定名の親だけを置く。
   - ファイルを掴むもの（テストが開いた SQLite の接続のプールなど）は作ったテストの側で放す（例：`SqliteTempDatabase` の Dispose）。製品が SQLite のファイルを開く接続はプールしない（`src/QuickER.Provider.Sqlite/CLAUDE.md`「ファイルを掴まない接続」）ので、製品の操作の後に放す手当ては要らない。
   - TEMP を空のフォルダへ向けた全件の実行で、残るのは次のものだけ（中身のある残りは 0）。
     - 空の親フォルダ（`TestTempFolder.Root` ほか、テストが固定名で作る親）。並列の実行が共有する根なので、テストの側で消すと他の実行の作成と競う。
     - dotnet CLI が `dotnet test` のたびに書くワークロードのログ（`Microsoft.NET.Workload_<pid>_<時刻>.log`）。テストのものではない。
     - まれに `MockPreviewPanelRuntimeTests` の WebView2 のユーザーデータ（`userdata\EBWebView`）。ブラウザプロセスの終了を最大 30 秒待ち、削除を 10 秒やり直しても、負荷の高い全件の中では描画・GPU の子プロセスがまだ掴んでいることがある。上限を延ばしても負荷しだいで超える点は変わらず、テストの終わりが遅れるだけなので、消せないものとして残す。
   - 古い版のコードを流した実行（計測で直す前の版を流すなど）は、その版の残りを本物の %TEMP% に残す。古い版は直せないので、次の実行の始めに古い残りを消す掃除は入れていない（消すかどうかは利用者が決める）。
7. ソースの走査のガード：`TestSupport/GuardKnownList.AssertMatches`（見つかった出現と理由つきの既知の一覧を件数まで 2 方向で照合する）と `TestSupport/SourceFiles`（ガードが読むファイルの列挙と、bin / obj の判定 `IsBuildOutput` の唯一の場所。パースはしない）。ソースの規則は規則の表（`TestSupport/Guards/`・「ソースを走査するガード」）の行にし、走査・照合・報告は表の 1 か所が持つ。表へ移していないガードは、条件コンパイルの全分岐を読む `ConditionalSourceParse` / `SourceSymbolScan` を自分で呼ぶ。

8. テストホストの GC：`QuickER.Tests` の csproj は `ServerGarbageCollection` を true にする（runtimeconfig の `System.GC.Server` 経由で testhost に効く）。
   - 理由：並列の全件実行では、生成コードのコンパイル・emit と総当たりが大量に割り当てる。既定のワークステーション GC では CPU の使用率が 3 割前後のまま GC の待ちで止まり、1 行の時間が単独で流したときの 2〜8 倍に伸びる。サーバー GC にすると全件の壁時計がおよそ半分になる（24 論理コアの開発機で 281 秒 → 139 秒）。testhost のメモリの最大は 322 MB から 535 MB に増える。
   - CPU の数で効き方が変わる（コアの少ない CI では差が小さくなる見込み）。
   - ガード：`SourceRuleTests`（規則 `test-host-server-gc`）。csproj を XML として読み、コメントの中の表記・条件つきの設定・true でない値は数えない（自己検査の行 `test-host-server-gc-forms`）。設定を消しても false にしても赤になる。

統合の前後で、該当テストのケースの数と合否は同じ（`-list full -preEnumerateTheories` と実行結果の照合）。mutation 実証済み（obj に置いた禁止の呼び出しは bin / obj の判定を外したときだけ赤）。

## MTP の実行器

- テストは Microsoft.Testing.Platform（MTP）で動かす。`global.json` の `test.runner` が `Microsoft.Testing.Platform`、2 つのテストのプロジェクトが `xunit.v3.mtp-v2` と `UseMicrosoftTestingPlatformRunner`（xunit の入口をこの値で MTP にする）と `OutputType` Exe と `IsTestProject`（以前は Microsoft.NET.Test.Sdk が付けていた印。Stryker.NET はこれでテストのプロジェクトを見分け、無いと初回のテスト実行が 0 件で止まる）を持つ。Microsoft.NET.Test.Sdk・xunit.runner.visualstudio・coverlet.collector（VSTest の部品）は持たない。
  - 混在させない：`dotnet test` を MTP にすると、VSTest のままのプロジェクトは `dotnet test QuickER.slnx` から黙って外れ、件数にも出ない（切り替えのときに実際に Integration が消えた）。
  - Stryker.NET の MTP の実行器もこの形を前提にする（VSTest の実行器は、xunit v3 がテストを別のプロセスで走らせるので変異が効かない）。
- 書き方：絞り込みは VSTest の書式の `--filter` のまま（xunit v3 が MTP でも受ける。`RegenerationRequest.Command` が組む再生成のコマンドも同じ）。VSTest だけのオプション（`--logger`・`--settings`・`--collect`）は MTP の `dotnet test` が受けず、終了コード 5 でテストを 1 件も流さずに止まる。コンソールの詳細は `--output detailed`、TRX は `--report-trx --report-trx-filename <名前>`（Microsoft.Testing.Extensions.TrxReport）、カバレッジは coverlet.MTP の `--coverlet --coverlet-output-format cobertura`。
- 流れたテストが 0 件なら MTP は終了コード 8 で失敗にする（スキップは流れた数に入る）。ワークフロー・スクリプトの `dotnet test` はどれも 0 件にならない絞り込みだけを使い、0 件を許す `--ignore-exit-code 8` は付けない（0 件は絞り込みの誤り）。
- ワークフローは最上位の env に `TESTINGPLATFORM_TELEMETRY_OPTOUT: "1"` を持つ（MTP は既定で利用状況を送る。`dotnet stryker` も中で MTP のテストを流すので同じ）。
- CI の Windows のテストは TRX も書き（`--report-trx --results-directory "${{ runner.temp }}/test-results"`）、`if: always()` の成果物（名前は build-and-test-trx）に 14 日残す。MTP のコンソールは表示名だけなので、テストのクラスごとの時間は TRX の完全修飾名で読む。`QuickER.slnx` は 2 つのプロジェクトを流すので、TRX のファイル名は指定しない（指定するとプロジェクトどうしで同じ名前に上書きする。指定しなければアセンブリ名_TFM_アーキテクチャ.trx）。ガード：`WorkflowGuardTests.WindowsTestStep_KeepsTestResultsAsArtifacts`。
- ガード：`ParityTests`（行 `test-projects-run-on-mtp`・`workflow-test-commands-use-mtp-options`・`workflow-test-telemetry-opt-out`）。3 行とも一時的に壊して赤になることを確かめた（Integration の `UseMicrosoftTestingPlatformRunner` を false・ci.yml に `--logger`・env の消去）。

## 夜間の深い実行

- `.github/workflows/nightly.yml`：トリガーは schedule の `0 18 * * *`（日本時間 3:00）と workflow_dispatch だけ。`permissions: contents: read`。前の実行は取り消さずに待つ。
  - 2 ジョブとも `timeout-minutes: 60`。ubuntu が `QuickER.Tests.Integration`（厳格モード）、windows が `QuickER.Tests`（TEMP / TMP はランナーの一時領域）を、`QUICKER_ENGINE_PROFILE=nightly` と `--filter "NightlyEngine=true"` で流す。
  - アクションは ci.yml か release.yml と同じ commit SHA で固定する。
  - schedule は既定のブランチでだけ動き、リポジトリに 60 日間活動が無いと GitHub が止める。
  - 普段の CI（ci.yml）は変えない。
- テストの結果は TRX で残す：両ジョブのテストの手順が、コンソールの詳細（`--output detailed`）に加えて `--report-trx --report-trx-filename <ジョブ名>.trx`（MTP の TrxReport 拡張。テストの出力は各結果の StdOut に入り、VSTest の logger の TRX と同じ形。xunit の `--report-xunit-trx` は出力を TextMessages に分けて書くので使わない）と `--results-directory "${{ runner.temp }}/nightly-results"` を持つ。続く `if: always()` の `actions/upload-artifact` が、ジョブごとに別の名前（`integration-nightly-trx` / `unit-nightly-trx`）で `retention-days: 14` の成果物として保存する（失敗した夜にも残す）。
  - コンソールのログに出ないテストの出力（`ITestOutputHelper` に書く網羅の要約・夜間なら埋まる組・種の数の警告）と、方言の派生ごとの時間（同期計画の総当たり・名前の DDL の往復の派生は同じ表示名なので、TRX のクラス名で見分ける）はこれで読む。
  - ガード：`WorkflowGuardTests.Nightly_JobsKeepTestResultsAsArtifacts`（TRX とコンソールの引数・結果のフォルダ・保存の手順が 1 つ・`if: always()`・path・保持日数・名前の重複なし）と `ParityTests`（行 `nightly-actions-pinned-like-other-workflows`）。
- 夜間の種の数は各エンジンの `SeedSettings` の第 3 引数（`NightlyCount`＝普段の数以上・普段の種を先頭に含む）に置く。ここに表は持たない。
- 選び漏れを防ぐ：`SeedSource.Resolve` は、プロセスの環境変数で種を決めるとき、いま走っているテストが `[Trait("NightlyEngine", "true")]`（夜間に流す）か `"false"`（種を使うが夜間に流さない・理由のコメントを添える）の**ちょうど一方**を宣言していなければ、テストの名前と直し方を書いた `XunitException` で赤にする（`NightlyEngineTrait`。テストの外と、環境の読み取りを差し替えた呼び出しでは検査しない）。
  - どのテストが種を使うかを人が一覧で持たないので、補助を経由する使い方も拾える。種の指紋のドリフトテストを別のカルチャで流し直す `CultureInvariantGenerationTests.DriftTests_PassUnderCulture` の付け忘れも、これで見つかった（`"false"` と理由を付けた）。
- 網羅しない組の夜間の扱い：`CoverageReport.AssertGaps` は、夜間の既定の種の数では「一覧にあるのに網羅した組」を赤にせず、「夜間なら埋まる組」としてテストの出力へ書く（普段の既定の種の数では欠けるので一覧に残す）。一覧に無い欠けは夜間でも赤。
- 既知の失敗の夜間の扱い：乱数の項目の `KnownReproduction` で、夜間に見つけた失敗は `NightlySeeds` で載せる（普段の CI では「起きない」を数えない）。
  夜間の実行で見つけた同期計画の総当たりの失敗は `SyncPlanFuzzKnownFailures` に載せる（中身はその一覧のコメント）。
- 同期計画の総当たりの表明 1b（失敗の後に制約が消えていない）の規則 1〜5（`SyncPlanFuzzRunner.FailedSyncConstraintLosses`）、注 `warned-alter-foreign-key-exempt:{own-failure|collateral}`、違反の文の `(rule n; warned=…; dropped-before-failure=…)`、規則を固定する `SyncPlanFuzzOracleTests`（DB なし・1 規則 1 行の表）は `tests/QuickER.Tests.Integration/Dialects/SyncPlanFuzz/CLAUDE.md`「表明 1b の規則」を参照する。
  1 種の処理の予期しない例外を `seed threw:` として記録して次の種へ進める `SyncPlanFuzzRunner.RunGuardedAsync`（例外でテストごと止まると、それより前の種の違反が隠れる。取り消しは止める）も `tests/QuickER.Tests.Integration/Dialects/SyncPlanFuzz/CLAUDE.md`「1 種の例外」を参照する。
- 同期計画の総当たりは、必須の形の表明と網羅しない組の照合を、普段の既定の種の数が 50 以上の方言だけで行う（夜間に種が増えた PostgreSQL・Oracle では行わない）。必須の形の表明は、流す種の数が普段の既定の数以上のときだけ行う（夜間の数は既定以上なので表明する。網も同じ）。既知の失敗の照合と必須の形の表明は両方を集めて 1 つの失敗にする（どちらかの赤がもう一方を隠さない）。失敗した種の警告の内訳と「失敗を予告しない種類の警告しか無いのに失敗した種」は出力する（測るだけ）。
- ガード：`SeedSourceTests`（目印の判定と `Resolve` の検査）・`KnownFailureSetTests.NightlyOnlyRandomEntry_NotObservedOnlyAtNightlyDefaultCount`・`CoverageCounterTests` の夜間の 4 件・`SyncPlanFuzzRunnerGuardTests`（Integration・Docker 不要）・`WorkflowGuardTests` の `Nightly_*` 6 件・`ParityTests`（行 `nightly-actions-pinned-like-other-workflows`・`nightly-jobs`・`nightly-job-test-commands`・`nightly-job-engine-profile`）。`SchemaNamesTests` の目印の付け忘れも `SeedSource` が拾う。mutation 実証済み。

## 変異テスト（Stryker.NET）

- 道具は dotnet-stryker（版は `.config/dotnet-tools.json`。`dotnet tool restore` で入れる）。設定は `stryker/` の範囲ごとの JSON：`model.json`（毎晩）・Provider の 5 本（週 1 回：`provider-sync-planner.json`・`provider-diff.json`・`provider-rest.json`・`provider-type-token.json`・`provider-type-text.json`）・休止中の CodeGen の 3 本（`codegen-diagnostics.json`・`codegen-queries.json`・`codegen-escapes.json`。下の「CodeGen の休止」）。
  - 起動は `scripts/run-mutation.ps1` の 1 か所だけ（mutation.yml も手元の実行も呼ぶ。例：`./scripts/run-mutation.ps1 -Target model -OutputDirectory <結果のフォルダ> -Concurrency 8`）。Stryker は設定の `solution` を読まず、渡したソリューションの中のパスが `../` で始まるとテストを 0 件と数えて止まる（絶対パスやルートからの相対パスなら 14,559 件を見つける。2026-11 に 1 つずつ戻して切り分けた）。そこでスクリプトが範囲のテストだけのソリューションの先頭の `../` を外した写し `QuickER.TestsOnly.stryker.slnx` をルートに作り、ルートで `dotnet stryker -f stryker/<範囲>.json -s <写し> -O <結果> -c <並列度> --skip-version-check -L` を呼び、成否に依らず写しを消し、Stryker の失敗を終了コードで伝える（`pwsh -File` はネイティブコマンドの非 0 を終了コードにしない）。
    - 写しをルートに残すと、ルートで引数なしの `dotnet build` / `dotnet test` が MSB1011（ソリューションが 2 つ）で止まる。だから写しを正本にせず、`.gitignore` にも入れる。並列度は既定値を持たせない（ホストのランナーと手元で適した値が違う）。
    - 範囲ごとのソリューションと環境変数は、スクリプトの表 `$targetSettings` の 1 か所だけが持つ（ワークフローには書かない）。表に無い範囲は `stryker/QuickER.TestsOnly.slnx` で環境をそのまま流す。表の変数は Stryker を呼ぶ間だけ立て（外し）、終わったら成否に依らず元へ戻す。範囲・ソリューション・立てた変数・外した変数を結果のフォルダの `run-environment.json` に書く（要約が読む）。
  - テストだけのソリューション `stryker/QuickER.TestsOnly.slnx` は src の全プロジェクトと QuickER.Tests・偽の app-server を持つ（Stryker は変異させるプロジェクトをソリューションから探す。QuickER.Tests は Model などを推移的にしか参照しない）。2 本目の `stryker/QuickER.TestsWithIntegration.slnx` はそれに Integration を足したもので、表が provider-sync-planner に使う（下の「同期の計画の範囲」）。実行器は MTP（「MTP の実行器」）。
  - 上限（`additional-timeout`）は Model 120 秒・Provider の 5 本 300 秒。Provider の計装の下で最も重いカバレッジの取得は `DslConditionFuzzTests` の 1 件 54 秒（手元。CI はおよそ 3 倍）で、300 秒で取得の時間切れは出ない（2026-11-09 の実測 4 本で 0）。static の変異は 15 分前後かけて殺せるので、上限を下げると本当の Killed が Timeout に変わるだけになる。
  - 結果は報告するだけ：どの設定も `thresholds.break` が 0（点数では赤にしない）で、`break-on-initial-test-failure` が true（初回のテストの失敗では止める＝Stryker が動かなかったときだけ赤）。
  - 除外は `ArgumentNullException.ThrowIfNull` と例外のコンストラクターの引数（`ignore-methods` の `*.ThrowIfNull`・`*Exception.ctor`）だけ。
  - `mutate` はファイル単位（`**/` で始まる後ろの一致と、`!` を付けたファイル単位の除外）だけで書く。文字の範囲（`{a..b}`）は使わない：Stryker 5 は全部の変異をまとめてコンパイルしてから範囲で印を付けるので、範囲ではコンパイルの失敗（Safe Mode）を避けられず、位置はソースが動くと黙ってずれる。
- Safe Mode：変異が CS0165（`out var` を `||` でつないだ条件の変異や、その if の `continue` の削除で、後ろの変数が確実に代入されなくなる）になると、Stryker はそのメソッドの変異を全部 CompileError にして外す。Model の `ConstraintNames.ResolveForeignKeyNamesCore`（41 件）・Provider の 8 か所・CodeGen の 14 か所・方言の各 1 か所が当たる。設定では外せない（上の理由）ので受け入れ、外れたメソッドと数を夜間の出力に毎回出す。直すなら製品のコードを CS0165 にならない形へ書き直す修正の区切りになる。
- 範囲の大きさ（Stryker が検査する数・2026-11）：Model 425・Provider の同期の計画 885／差分 489（static 25）／残り 547／型の 2 本 188（CanonicalTypeToken.cs〔static 26〕と、SqlTypeText.cs〔static 18〕・SchemaTableEntry.cs〔2〕・QueryParameterTypeResolver.cs〔1〕）・休止中の CodeGen の診断 250・クエリの型の検査とエミッタ 404・エスケープ 56（`GeneratedLiteralText` だけ。XmlDoc の名前のエスケープはビルダーの中のメソッドなので、ファイル単位では入れていない）。方言のプロバイダ 5 本は 4,407 で、当面は入れない（1 本 739〜1,026 件で、下の固定の費用が方言ごとにかかる）。入れるなら 1 週に 1 方言ずつ回す。
  - Provider の 5 本の mutate は、Provider の手で書いたソースをちょうど 1 回ずつ覆うように分ける（残りは `**/*.cs` からほかの 4 本のファイルを `!` で外す）。漏れは要約の「範囲の外」に出る。
- 時間（2026-11・手元の 24 論理コア・並列度 8）：Model は 74 分（ビルドと初回のテスト実行 4.4 分・テスト 1 件ずつのカバレッジの取得 29.7 分・変異の検査 39.8 分＝1 件 5.7 秒）、エスケープは 53 分（変異の検査 1 件 17.6 秒）。カバレッジの取得はテストの数で決まり、範囲の大きさに依らない固定の費用（範囲を 1 つ割るたびに 1 回増える）。
  - ホストのランナー（4 vCPU）では Stryker の並列度の既定がコアの半分（2）になり、Model でも上限に届く見込みなので、mutation.yml は並列度 4 を明示する。Model は初回の CI（2026-11-08）で 3 時間 34 分。
  - 週 1 回の各行の CI の見込み（2026-11-09 の手元の実測から。ジョブの上限は 330 分）：固定の費用＝ジョブの開始・ビルド・初回のテスト約 20 分＋カバレッジの取得約 100 分（初回の CI の Provider の実測 96〜103 分。手元は 17.7〜35 分で揺れた）≈ 2 時間。変異の検査の CI の壁時計＝手元の実行器の合計 × 1.25 ÷ 4（手元の並列度 8 で 27 分の範囲が CI で 68 分だった比）。同期の計画 約 3.2 時間・差分 約 4.2 時間（static 25 件が実行器の合計 362 分）・残り 約 3.4 時間・型のトークン 3.2〜4.0 時間・型の表記 3.0〜3.6 時間。マージの後の workflow_dispatch の weekly で実測し、5 時間に近い行が出たらさらに割る（行列に max-parallel は無いので、週 1 回の壁時計は最も遅い行で決まり、行を足しても延びない）。
  - Timeout は Stryker が殺したと数える。1,000 件以上のテストが通る行の Timeout は「落ちるのに時間がかかった」か「生き残り」かが分からないので、要約の Timeout の列は Killed と分けて読む。
  - GUI のテスト（108 件）だけが殺した変異は、Model・エスケープとも 0 件だった（2026-11）。
- 上流の不具合（Stryker.NET 5.0.0 の MTP の実行器・どちらも 2026-11 に OPEN）：
  - #3871（直す PR は #3916）：カバレッジの取得でテストが 1 件時間切れになると、実行器がテストのホストを強制的に止め、カバレッジの受け渡しのファイル（%TEMP% の stryker-epoch で始まるもの）が残って、後に続くテストが受け渡しを待ちきれず次々に Dubious になる。Dubious のテストは全部の変異に足されるので、変異の検査が全テストで遅くなる。上限を取得の最も重いテストより長くして時間切れそのものを出さない（上の 300 秒）。
  - #3915：変異の有無の判定（`IsActive`）が呼ばれるたびに共有の写像のビューから変異の番号を読み、並列のテストのスレッドがそこで競合する。計装の点が多い範囲（CodeGen 8,093・Provider 3,386）で多くのテストが通る変異は、変異の下のテストが極端に遅くなり上限に当たる。設定では避けられない。
  - static の変異（静的な初期化子の中の変異）は Stryker が全テストで検査する（`will be tested against ("all")`）。#3915 の下では 1 件の検査に手元で 9〜17 分かかる。差分の TableRebuildAttributeDetector.cs の 25 件は Killed 21・Timeout 4 で、大半は上限の前に殺せていて中身のある結果（2026-11-09）。static の変異を持つファイルは行を分けて費用を抑える（型の 2 本）。`ignore-methods` は呼び出しと `new` の式にしか当たらず、静的な初期化子を設定で外す手段は無い。要約は static の変異の数とそのうちの Timeout を範囲ごとに出す。
- 同期の計画の範囲（provider-sync-planner）は Integration を足したソリューションで流す。SyncPlanner の変異を殺すテストの多くは Integration の Docker の要らないテスト（同期計画の総当たり・期待の行・能力の組の網）にあり、QuickER.Tests だけでは 40.33%（NoCoverage 184）だったのが 80.11%（NoCoverage 35）になる（2026-11-09）。
  - コンテナを起動しない：起動スクリプトの表が `QUICKER_SKIP_DOCKER=1` を立て、厳格モードの宣言 `QUICKER_REQUIRE_DOCKER` を外す。Testcontainers を使うフィクスチャは、どれもコンテナを起動する前に `DockerRequirement.SkipReason` を呼び、変数が立っていれば起動を試みずに使えない扱いになる（`DOCKER_HOST` を届かない値にしても Testcontainers は次の候補の名前付きパイプへ落ちて本物の Docker を見つけるので、使えない）。CI の windows-latest では変数が無くてもコンテナは自動でスキップになるが、Docker が動く手元（厳格モード）で実 DB の総当たりが流れると範囲が 4 時間を超える。変数で手元と CI の流すテストをそろえる。普段の `dotnet test`・CI・夜間では立てない。厳格モードと両方立っていたら `SkipReason` が断る（手で両方を立てたときの守り）。
  - 種を減らす：同じ表が `QUICKER_SYNC_FUZZ_SEEDS=100`（SQLite の総当たり。既定 2,000）と `QUICKER_SYNC_CAPNET_SEEDS=500`（能力の組の網。既定 5,000）を立てる。既定の数のままだと、SQLite の総当たり（1 回約 100 秒）がほぼ全部の変異を通り、失敗を最後にまとめて報告するので打ち切りが効かず、変異 1 回の検査が中央 100 秒になる（CI で約 10 時間の見込み）。減らすと中央 3.1 秒。既定の数で流した 242 件と同じ変異で比べた損失は、Killed 207 件のうち 2 件（1%）だった。
    - 減らした数で外れる表明は、網・SQLite の総当たりとも同じ 3 つ。
      - 既知の失敗の「載っているのに起きない」（乱数の項目）：`KnownFailureSet.Classify` が既定の数（`SeedPlan.IsDefaultCount`）でだけ数える。
      - 網羅しない組の照合：`CoverageReport.AssertGaps` が既定の数でだけ行う。
      - 必須の形の表明：種の数が普段の既定の数以上のときだけ行う（`tests/QuickER.Tests.Integration/Dialects/SyncPlanFuzz/CLAUDE.md`「能力の組の網」「照合のまとめ方」）。この門が無かったため、修正 AC2 が網の必須の形を足した後、500 種の網で SQLite の形が欠け（出るのは種 501〜600）、週 1 回の最初のテストの実行が落ちた。
      - 既定の数で必須の形の表明が効いていること（門を常に偽にする退行を止めること）は、壊し方の抜き取りでだけ確かめた。下の CI の手順は、減らした数で落ちないことしか守らない。
    - 種の指紋は、重なる種だけを照合する（同期計画の指紋は `QUICKER_SYNC_FUZZ_SEEDS` で決まる）。
    - 流れ続けるもの：一覧に無い失敗の照合・決定的な行（合格側・形の行・来歴の行・網羅の行）・期待の行・網羅の宣言の誤り。
  - PR で捕まえる：起動スクリプトの `-TestsOnly` は、Stryker を呼ばずに、同じ手順で表の変数を立てて（外して）、表の `TestProject` を `TestFilter` の絞り込みで `dotnet test --no-build` で流す。変数の立て外しと finally での戻し・`run-environment.json` は Stryker の経路と共有し、ソリューションの写しは作らない。provider-sync-planner の表は Integration の `FullyQualifiedName~QuickER.Tests.Integration.Dialects.SyncPlanFuzz`（手元で 43 件・約 6 秒）。表に `TestProject` の無い範囲は、変数を変える前に断る（CI の範囲の取り違えを「何もしないで緑」にしない）。
    - ci.yml の build-and-test は、全件のテストの後に、この呼び出しの手順（「変異テストの環境で同期の計画のテストを流す」）を持つ。失敗で CI が赤になる。Stryker の最初のテストの実行と同じ環境でだけ落ちるテストを、週 1 回まで持ち越さないため（必須の形の門が無かったとき、この手順は網の SQLite の欠けで赤になった）。
- CodeGen の休止：CodeGen の 3 範囲は週 1 回の一覧から外し、設定は残す（照合の行 `mutation-targets-match-stryker-configs` の理由つきの休止の一覧 `PausedStrykerTargets`。設定の実在・一覧に無いこと・理由が具体的なことをその行が照合し、`stryker-mutate-globs-match-files`・`stryker-configs-report-only` は休止中の設定にも掛け続ける）。理由は #3915：CodeGen の生成を回すテストが変異 1 回ごとに 231〜356 件通り、変異 1 回が上限（10 分）に当たって、1 範囲が CI で 14 時間以上の見込みになる（2026-11-09 の手元の実測。初回の CI の codegen-escapes の 100% も Timeout の水増し）。
  - 再開の合図：Stryker.NET が #3915 と #3871（#3916）を直した版を出したら、`.config/dotnet-tools.json` を上げ、休止の一覧から外して weekly の一覧へ戻し、手元で codegen-queries を測り直す（上限 600 秒）。そのときカバレッジの取得の時間切れが出たら、重いテストを Stryker の下でだけ飛ばす仕組みを作り直す。CodeGen の計装の下の取得の時間（手元・2026-11-09）：`NameIdentityGenerationFuzzTests` 1,201 秒（普段の 31 秒の約 39 倍）・`QueryParameterNameSafetyTests` 537 秒（110 秒の約 5 倍）・`CultureInvariantGenerationTests` 1 行 109〜120 秒・`DslConditionFuzzTests` 228 秒。
  - あわせて、MTP の実行器の test-case-filter（#3848。2026-11 時点で未リリース）を含む版が出たら、GUI のテストを Stryker の下で外すかを見直す（下の GUI のテストの項）。
- 夜間の実行は `.github/workflows/mutation.yml`（nightly.yml と別のファイル。nightly.yml のガードは「ジョブ＝テストのプロジェクトごとの 2 つ・60 分以内」で、数時間の Stryker と合わないため）。
  - トリガーは schedule の 2 つ（毎晩 `30 18 * * *`＝Model・土曜 `0 20 * * 6`＝Provider の 5 本）と workflow_dispatch（`scope`：nightly / weekly / all）だけ。`permissions: contents: read`。同時実行の組は `mutation-` に定時か範囲を続けたもので、取り消さない（週 1 回の長い実行が毎晩の実行を待たせない）。
  - 小さなジョブ `plan` が範囲の一覧（stryker の設定のファイル名）から行列を決め、`mutation` ジョブ（windows-latest・`timeout-minutes: 330`）が `dotnet tool restore` → `./scripts/run-mutation.ps1 -Target <範囲> -OutputDirectory <ランナーの一時領域> -Concurrency 4` を流し（ワークフローは `dotnet stryker` を直に呼ばない）、結果（HTML・JSON・ログ）を `if: always()` の成果物に 14 日残す。点数で赤にする引数（`--break-at` など）をコマンドに足さない。
  - GUI のテストも Stryker の初回のテスト実行に入る（MTP の実行器は Stryker の test-case-filter を持たない。ホストのランナーでは CI の全件が緑）。GUI のテストが 1 件でも落ちると Stryker 全体が止まる（`break-on-initial-test-failure`）ので、夜間の実行で GUI のテストの一時的な失敗が出たら、外す仕掛けを入れるかを見直す。手元では 2026-11-09 に、画面の状態で `CanvasViewportBehaviorSpaceKeyTests` の 1 件が単独でも 1 時間半落ち続け、Stryker が 2 回止まった（ホストのランナーでは起きていない）。
- 要約：Stryker の後、`mutation` ジョブは前回の要約をキャッシュ（`actions/cache/restore`・鍵は範囲ごとの前置き）から戻し、QuickER.Tests の入口 `MutationSummaryTests.WriteSummary_FromEnvironment` を環境変数（`QUICKER_MUTATION_REPORT_DIR`＝Stryker の `-O`・`QUICKER_MUTATION_TARGET`＝範囲・`QUICKER_MUTATION_SUMMARY_DIR`）つきの `dotnet test` で呼び、要約を `actions/cache/save` で次の実行へ渡す。要約は Step Summary と `summary.md`・`summary.json`（成果物にも入る）。
  - 中身（本体は `TestSupport/MutationSummary.cs` の `MutationSummaries`）：影響の地図の単位ごとの変異の数・Killed・Survived・NoCoverage・Timeout・Dubious（Dubious のテストが通る流した変異）・スコアと前回からの増減、合計の CompileError・Ignored、Safe Mode で外れたメソッドとその中の CompileError の数、Dubious のテストの数（多ければ取り直しを検討する）、どの単位にも入らないファイル。
    - カバレッジの取得の時間切れ（ログの「Test run timed out while capturing per-test coverage」のテストを、結果の JSON の testFiles の名前に引く）と、続いて受け渡しを待ちきれなかったテストの数（#3871 の連鎖）。どちらも 0 が正常。
    - static の変異の数とそのうちの Timeout（範囲の中の流した変異のうち結果の JSON の static が真のもの）。
    - 実行の環境（結果のフォルダの `run-environment.json`：ソリューション・立てた変数・外した変数。無ければ「記録なし」）。`QUICKER_SKIP_DOCKER=1` には「Docker の要るテストは流さない」、種の数の変数には「網羅しない組の照合などが外れる」を添える。飛ばしたテストの件数は Stryker の結果にもログにも無いので数えない（どのフィクスチャも飛ぶことは照合の行 `stryker-docker-fixtures-honor-skip` が固定する）。
    - 足したフィールドは既定値を持ち、前の版の `summary.json` も「前回」として読み戻せる。
  - 単位は地図と同じ宣言から作った `ImpactMap.UnitOf`（写しを作らない）。範囲は設定の `mutate`（`StrykerMutateScope`＝照合の行 `stryker-mutate-globs-match-files` と同じ読み方）で決め、範囲の外のファイルは数えない（Stryker 5 は全部の変異をコンパイルしてから範囲で印を付けるので、範囲の外のファイルにも CompileError・除外の Ignored が残る）。
  - Safe Mode と Dubious は JSON に書かれないので、ログ（`-L` のファイル）から読む（Safe Mode は「An unidentified mutation in {ファイル}」の直後の「Safe Mode! … in {メソッド}」の組・Dubious は「marking as Dubious」の行のテストの ID）。
  - 入口は環境変数が無ければ何もしない（普段の実行では 1 件の合格。xunit の Explicit は普段の実行でスキップに数えられ、`dotnet test` 越しに `--explicit only` が効かないので使わない）。前回の要約はキャッシュが 7 日使われないと消えるので、週 1 回の範囲は「前回なし」になり得る。
- 変異の数だけを数えるときは、テスト 1 件だけの小さなテストのプロジェクトを参照させると、初回のテスト実行がほぼ 0 秒になり、全件の負荷なしで 1 本 30〜50 秒で数えられる（変異の数・CompileError・Safe Mode はテストに依らない）。
- ガード：`ParityTests`（行 `stryker-tests-only-solution-projects`〔2 本のソリューション〕・`stryker-mutate-globs-match-files`・`stryker-configs-report-only`・`mutation-targets-match-stryker-configs`〔休止の一覧を含む〕・`stryker-docker-fixtures-honor-skip`・`mutation-script-variables-match-test-constants`・`mutation-actions-pinned-like-other-workflows`・`mutation-actions-pinned-by-sha`・`workflow-test-telemetry-opt-out`）と `WorkflowGuardTests` の `Mutation_*` 5 件（トリガーと 2 つの cron・読み取り専用と取り消さない組・明示の時間の上限・tool restore の Stryker を起動スクリプトから範囲の設定と並列度 4 で呼び点数で赤にせず結果を残す〔どのワークフローにも起動スクリプトの表の変数を書かない〕〔ci.yml の build-and-test は全件の後に `-TestsOnly` の provider-sync-planner の手順をちょうど 1 つ持ち、pwsh・TEMP はランナーの一時領域・`continue-on-error` なし〕・前回の要約を戻し要約して次へ渡す）と `MutationScript_*`（起動スクリプトを偽の dotnet で動かす：範囲ごとの行で、範囲のソリューションの写しを作ってからルートで呼ぶ・引数・写しのパスが実在のプロジェクトを指す・成否に依らず写しを消す・失敗を伝える・写しの名前が .gitignore にある・表の変数を Stryker の間だけ立て外し呼び出し元の環境へ戻す〔呼び出し元の厳格モードを Stryker から隠す・種の数を上書きする〕・`run-environment.json` の中身・無い範囲は呼ぶ前に断り環境を変えない・`-TestsOnly` の行は写しを作らずに表のプロジェクトを表の絞り込みで `--no-build` で流し、変数・戻し・失敗の伝達・実行の環境は Stryker の行と同じ・表に流すテストの無い範囲を `-TestsOnly` で呼ぶと dotnet を呼ばずに断る）。期待の変数の値はスクリプトの表から読まずテストに書く。要約の数え方は `MutationSummaryTests` の決まった形の行（単位ごとの数とスコア・いちばん深い単位・ランナーのチェックアウトのパス・Safe Mode・範囲の外・範囲の除外・Dubious・前回からの増減・前回なし・static の変異・カバレッジの取得の時間切れ・実行の環境・実行の環境なし）。どれも一時的に壊して赤になることを確かめた（.slnx からプロジェクトを 1 つ消す・mutate のファイル名を変える・break を 50 にする・範囲の一覧から 1 つ消す・cron を変える・取り消す・時間の上限を消す・env を消す・`--break-at` を足す・`dotnet tool install` に替える・ワークフローから `dotnet stryker` を直に呼ぶ・並列度 4 を外す・スクリプトの写しの後片付け／`../` の書き換え／終了コードの確認／ルートへの移動／並列度・ログ・写しの引数を外す・.gitignore の行を消す）。2026-11-09 に足した分も同じく確かめた（フィクスチャの `SkipReason` の呼び出しを外す・表の変数の名前を変える・厳格モードを外さない・環境を戻さない・`run-environment.json` を書かない・範囲のソリューションを取り違える・ワークフローに `QUICKER_SKIP_DOCKER` を書く・休止中の範囲を一覧へ戻す・休止の一覧に存在しない名前を足す・2 本目のソリューションから Integration を消す・要約の static の数え方と取得の時間切れの名前の引き当てを外す）。`-TestsOnly` も同じく確かめた（ci.yml の手順を消す・スイッチを外す・`continue-on-error` を足す・スクリプトでスイッチの枝だけ変数を立てない／戻さない／終了コードを見ない・絞り込みを取り違える・表に流すテストの無い範囲を断らない）。Provider の 5 本の mutate が重ならないこと（例：残りから型のファイルの除外を 1 つ消す）は照合しない（重なった変異は 2 つの範囲で検査されるだけで、結果は壊れない）。

## 種の指紋

- 指紋の要約は機械の環境に依らない：記録は日本語・日本時間の機械で作り、CI は英語・UTC で動く。両アセンブリの指紋のドリフトテストは `SeedFingerprint.Compute` を通し、要約を `SeedSummary.UnderInvariantCulture`（要約を作る間だけ CurrentCulture と CurrentUICulture をインバリアントにする）の中で作る。差分の説明のような resx の文が入っても言語で変わらない。
- ガード：両アセンブリの `EngineSummaryEnvironmentTests`（共有の部品は `SummaryEnvironmentGuard`）。ドリフトテストのクラスに置かない（`CultureInvariantGenerationTests` がドリフトテストのクラスの引数の無いテストを流し直すため）。
  - `Summary_DoesNotDependOnCulture`：全エンジンの先頭 10 種の要約を、外側のカルチャを ja-JP と en-US に切り替えて作り、一致を表明する。切り替わったカルチャの名前も表明する（ICU の無い環境で比べたつもりにならない）。
  - `Summary_HasNoUtcOffset`：要約に `[+-]hh:mm` が無い。タイムゾーンはプロセスの中で切り替えられないので、赤になるのは時差のある機械だけ。
  - `SeedFingerprintTests` の Local の日時と `UnderInvariantCulture` の 2 件。
  - mutation 実証済み。

## 影響の地図

src のどこを、どのテストのエンジン・表・規則が守っているかを引く地図。生成物 `tests/QuickER.Tests/TestSupport/impact-map.md`（手で直さない・eol=lf）として置き、宣言から作り直した内容と照合する。

- 単位：src のプロジェクト（csproj を持つ src の直下のフォルダ）と、その直下のフォルダのうち手で書いたソース（`.cs`〔`*.Designer.cs`・`*.g.cs` を除く〕か `.scriban`）を持つもの（`ImpactUnits.Enumerate`）。
  - `Resources/`（生成の Designer.cs と resx だけ）・`Themes/`（XAML だけ）は単位にしない。
  - src にファイルを足しただけでは単位も地図も変わらない。変わるのは、プロジェクト・直下のフォルダ・宣言を足したとき。
- 守りの名前（P4-4・P4-5 が参照する）：エンジンの部分は `{エンジンの ID}/{部分}`（例 `Dialect/DdlText`）、単体の表は `table:{名前}`、ソースの規則は `rule:{Id}`、照合の表は `parity:{Id}`、英語の文言の表は `english:{Id}`、必須の文言の表は `phrase:{Id}`。形の正本は `ProtectionNames`（`Engines/ImpactMap.cs`）の 1 か所で、地図・台帳の検査・CLAUDE.md の照合が同じものを使う。規則・照合・文言の表の行は、範囲を src の中に絞った規則が「守りの一覧」に、ほかは「横断の守り」に、どれも同じ名前の形で全部載る。
- 出どころ（手で書いた対応表を正本にしない。集めるのは `ImpactMapSources`）：
  - エンジンの台帳の `EngineDescriptor.Covers`。
  - 単体の表の台帳 `TableRegistry`（エンジンの台帳に載らない、決まった形の行の表。`TableDescriptor`）。宣言は台帳の 1 ファイルにまとめ、表のファイルには置かない（表のファイルは行を足す区切りが並行して直すので、同じファイルに置くとぶつかる）。
  - ソースの規則の範囲（`SourceRule.Scope`）。
  - Integration の台帳の写し `tests/QuickER.Tests.Integration/protection-registry.json`（QuickER.Tests は Integration を参照できないため。`ProtectionSnapshot`）。
- 単位の守りに数える範囲は、src のプロジェクトの中（`src/` の下）を指すものだけ。
  - Guard の部分の Covers（`src`・`tests`・`docs` など）と、`src` 全体か src の外を走査する規則・照合と文言の表の行は、地図の「横断の守り」の節に出し、単位の守りに数えない。数えると全単位が守りありになり、照合が空になる（最初の生成で実際に全単位が「全体」になった）。
  - 範囲を src の中に絞った規則（範囲の根が `src/` の下か、明示のファイルがすべて `src/` の下か、`src` を根にして読む領域〔`SourceScope.Within`〕を限ったもの＝領域の接頭辞で始まるプロジェクトを守る）は単位の守りに数える。`src/QuickER.Gui.Common` はエンジン・表を持たず、この規則 3 本だけで「一部」になっている。
  - 範囲が単位そのものか祖先なら「全体」、単位の中のファイル・フォルダだけなら「一部」。
- 地図に数を入れない（シナリオの数・ファイルの数・テストの数）。行やファイルを足すたびに地図が変わるのを避ける。守りの無い領域の手書きのテストの数は、照合のテストの出力に書く。
- 行 Id の接頭辞は地図に載せない。
  - テストのソースの文字列リテラルから読むと、値のデータ（`alice`・`ffffffff` など）まで拾い、行やデータを足すたびに地図が変わる。
  - 台帳のソースは partial の本体だけを書き、部分ファイル（`DialectDdlRows.Names.cs` など）を書かないことがあり、判定の部品をフォルダで書くこともある（`CodeGen.CSharp/CodegenConfig/Judges`）。そのため登録の照合・行 Id の持ち主は、同じフォルダの `{名前}.*.cs` とフォルダの下の C# のソースまで広げたソースで読む（`ImpactMapSources.ExpandPartials`）。
  - 表の行の名前は「守りの名前＋行 Id」（例 `Dialect/DdlText` の行 `ddl-*-trimmed-table`）。行 Id がその守りのものかは `ImpactMapSources.OwnsRowId`（守りの広げたソースの本文だけに CLAUDE.md の照合と同じ判定 `ClaudeMdReferenceTests.RowIdExists` を当てる）の 1 か所が決める。CLAUDE.md 群が地図の名前に続けて書いた行 Id は、`ClaudeMdReferenceTests.MapNames_ExistInImpactMap` がこれを呼んで照合する。テストのクラスの名前に続けて書いた行 Id も、そのクラスを宣言するファイルを含む守りごとにこれを呼ぶ（どれかの守りが持てば合格・守りに属さないクラスは宣言するファイルだけ。`tests/QuickER.Tests/Docs/CLAUDE.md`「CLAUDE.md の照合」）。
- エンジンの部分のテストのソースには、その部分の行を流すテストのクラスのファイルだけを書く（同じエンジンの部分どうしで共有するのは次元表・実行と判定の部品）。例：生成の構成のエンジンは、網羅の配列（Errors・Success）に `CodegenMatrixTests.cs`、決まった行（Cases）に `CodegenCases.cs`（行の表）と `CodegenCaseTests.cs` を書く。テストのクラスがどの守りのものかを地図で引けるようにするため（ルートの CLAUDE.md の索引の「ガード：」が地図の名前で書けるのは、そのクラスが 1 つの守りにだけ属するとき）。
- 変異テストの結果を領域ごとにまとめるときは、`ImpactMap.UnitOf`（パスの属するいちばん深い単位）と `ImpactMap.ProtectionsOf`（パスを範囲に含む守り）で引くか、地図の「範囲の表」をパスの最長一致で読む。夜間の変異テストの要約（`MutationSummaries`）は `ImpactMapSources.Build` の地図の `UnitOf` を使う（「変異テスト（Stryker.NET）」）。
- 守りの無い領域：`ImpactMapDeclarations.Unprotected`（単位・分類・理由）。
  - 分類は Pending（エンジン・表で埋める候補）と Accepted（エンジン・表で守る対象でないと判断したもの）。理由は因子の棚卸しと同じ判定（`FactorInventoryGuard.IsVagueReason`＝仮置きの文か 10 文字未満を断る）。
  - プロジェクトを載せたら、その下のフォルダは書かない。
  - 地図は、ミラーのフォルダ（`src/QuickER.X/Y` → `tests/QuickER.Tests/X/Y`）に、どの守りのソースでもないテストがあれば「単体の手書きのテストだけ」、無ければ「無い」と書く（本当に守りが無いのか、手書きのテストしか無いのかを分ける）。
- 照合：src のすべての単位が、どれかの守りの範囲にあるか、理由つきの守りの無い領域にあるかのちょうど一方（`ImpactMapGuard`・因子の棚卸しと同じ形）。
  - 赤にする問題：宣言の無い単位（新しいプロジェクト・直下のフォルダを足すと赤）・守りと守りの無い領域の両方にある単位・存在しないパスや単位でないパスの宣言・宣言の重複・プロジェクトごと載せた単位の下のフォルダの重ね書き・空か具体的でない理由・知らない分類・守りの名前の重複・存在しない範囲・src のプロジェクトの中を指さない範囲。
  - 決まった形の行の表（`ScenarioTable<T>`）を使うテストのソースは、エンジンの部分のソースか単体の表の台帳に入れる。使うかどうかは総称の型の名前の節で数え（`ImpactMapSources.UsesScenarioTable`・条件コンパイルのどの分岐も読む）、コメント・XmlDoc・文字列の中の綴りは数えない。登録しなくてよいソースは理由つきの一覧（`ImpactMapSources.ScenarioTableExemptions`）で、2 方向で照合する。
- 書き直し：生成フィクスチャと同じ環境変数 `QUICKER_REGEN_FIXTURES`。変数の名前・読み方・案内のコマンドの正本は `RegenerationRequest`（`TestSupport/Engines/GeneratedTextFile.cs`）。生成物の照合と書き直しは `GeneratedTextFile.Verify`。
  - Integration の台帳（エンジン・単体の表）を変えたら、先に Integration の写しを、続けて地図を書き直す（ルートの `CLAUDE.md`「生成テンプレートと固定フィクスチャ」の書き直しの段）。写しを書き直し忘れると Integration の `ProtectionSnapshotDriftTests` が赤になる（Integration だけを流す ubuntu のジョブでも捕まる）。
  - ほかの区切りのマージで台帳・規則が変わると地図も変わる。マージの後に書き直してそろえる。書き直しは承認であって検証ではない（地図の差分で守りの範囲の変化を確かめる）。
- ガード：`ImpactMapGuardTests.EveryUnit_IsProtectedOrDeclaredUnprotected`・`ImpactMapGuardTests.ScenarioTableSources_AreRegistered`・`ImpactMapDriftTests`・Integration の `ProtectionSnapshotDriftTests`・両アセンブリの `EngineRegistryTests.TableRegistry_IsWellFormed`。自己検査は `ImpactMapTests`（照合の問題の形ごとの行・単位の列挙・表の型の使い方の判定）と `EngineRegistryChecksTests.TableProblems_AreNamed`。

## 単発のテストの報告

単発のテストの増え方を CI で見える化する（P4-5）。赤にしない。

- 単発のテスト：`tests/QuickER.Tests` と `tests/QuickER.Tests.Integration` の C# のソース（`bin` / `obj` を除く）のうち、影響の地図のどの守りのテストのソースにも入らないファイルの `[Fact]` / `[Theory]` のメソッド（数えるのは `OneOffTestCensus`）。
  - 守りのテストのソースは地図と同じ関数で決める：`ImpactMapSources.Protections` の各守り（Integration の守りは写し `protection-registry.json`）を `ImpactMapSources.ExpandPartials` で広げたもの。写しを作らない。
  - テストの土台の自己検査（`ImpactMapSources.ScenarioTableExemptions`＝表の型・変異テストの要約・この報告の自己検査と入口）は数えない。
  - メソッドで数える（Theory のケースは数えない＝ビルドだけで決まる数）。属性の名前の最後の部分が Fact・FactAttribute・Theory・TheoryAttribute のメソッドを、条件コンパイルのどの分岐も読んで数える（コメント・文字列の中の綴りは数えない）。
  - 数の大半は単発になる（2026-11 で単発 3,380・守りのソースのメソッド 268）。エンジンは少ないメソッドに多くの行を載せるため。見るのは合計でなく増減。
  - 漏れる例：エンジンのソースのファイルへ足した手書きの `[Fact]` は、ファイルごと守りのソースなので数えない。入りすぎる例：テストの土台の自己検査（TestSupport 直下）・生成フィクスチャのドリフトテスト・Integration の「共通基底＋派生」のランタイムテスト・Docs のガードは数える（地図の守りの外なので、地図へ載せれば減る）。
- CI：ci.yml の build-and-test が、ビルドの後・テストの前に 3 つの手順を持つ。
  - 基点の数を `actions/cache/restore` で戻す（パス `${{ runner.temp }}/oneoff-tests`・鍵 `oneoff-tests-<基点>`）。基点は PR が向き先の先端（`github.event.pull_request.base.sha`＝チェックアウトしたマージのコミットの第 1 親なので、比べると PR の変更だけになる）、main への push が直前のコミット（`github.event.before`）。
  - 入口 `OneOffTestCensusTests.WriteSummary_FromEnvironment` を `dotnet test --no-build --filter` で呼ぶ（環境変数 `QUICKER_ONEOFF_DIR`・`QUICKER_ONEOFF_COMMIT`・`QUICKER_ONEOFF_BASE_COMMIT`。無ければ何もしない＝普段の実行では 1 件の合格）。基点の `census.json` と比べた報告を Step Summary と `summary.md` に書き、今回の数で `census.json` を置き換える。
  - main への push のときだけ `actions/cache/save`（鍵 `oneoff-tests-<このコミット>`）。PR の数は main から読めないので残さない。
- 報告の順：比べた基点 → 増えたファイル（基点・今回・増減。上限 20 で残りは件数）と増えたフォルダ（`tests/{プロジェクト}/{直下のフォルダ}`）→ 減ったファイルとフォルダ → 合計（Fact / Theory・守りのソースのメソッド・増減）。
- 基点の数が無いとき：キャッシュは 7 日使われないと消え、そのコミットで CI が走らないこともある（paths-ignore）。戻しは前置きの鍵 `oneoff-tests-` でいちばん新しい数を戻し、報告は「近似（比べたコミット）」と書く。それも無ければ「なし」と書いて合計だけを出す。
- 赤にしない：入口は数で表明しない（大きく増えても報告するだけ）。赤になるのは数えられないとき（地図を作れない・ソースを読めない・基点の JSON が壊れている）だけで、手順に `continue-on-error` を付けない。
- 時間：手元（24 論理コア）で手順 1 つ 24 秒前後（`dotnet test` の起動と探索が 12 秒・数えが 12 秒）。
- ガード：`WorkflowGuardTests.OneOffTests_ReportedWithoutFailingOnCounts`（手順の順・入口の絞り込み・環境変数・戻しと保存のパスと鍵・保存は push だけ・同じ版の SHA 固定・`continue-on-error` と `--ignore-exit-code` が無い）と `OneOffTestCensusTests` の決まった形の行（Fact / Theory の数え方・両プロジェクト・部分ファイルとフォルダの宣言・条件コンパイル・bin / obj と数えないファイル・増えたものを合計より先に書く・近似の基点・基点なし・大きく増えても報告だけ＝行 `oneoff-report-large-increase-only-reports`）。数え方を壊すと赤になることを確かめた（`TheoryAttribute` を読まない・守りのソースを単発に数える・壊れた基点の JSON で入口が赤）。

## GUI テストの土台

- キーボードフォーカス・流したキー入力・ウィンドウの Activated / Deactivated に結果が左右される GUI テストのクラスは `[Collection(TestSupport.WindowActivationTestCollection.Name)]` に入れる（`DisableParallelization`＝並列のテストが全部終わってから単独で走る）。
  - 並列で流すと、ほかのテストが表示したウィンドウにアクティブを奪われ、フォーカスが入らない・Space の判断が変わる形で、全件実行でだけ時々落ちる（単独では合格するので原因に気付けない）。
  - 守る側だけを入れれば足りる（奪う側を入れる必要はない）。ShowActivated=false で表示するだけでフォーカスに触らないテストは入れない。
  - ガード：`SourceRuleTests` の規則 `window-activation-collection` が、目印の API（`IsKeyboardFocus…`・`Keyboard.PrimaryDevice`・`Keyboard.Focus(`・`MoveFocus(`・`Deactivated`）を使うテストソースに属性が付いていることを全数検査する。
- GUI テストが失敗した瞬間の状態は `TestSupport.GuiFailureDiagnostics` が書き出す（全件実行でだけまれに落ちる GUI テストの原因を測るためで、結果は変えない）。
  - `WpfApplicationTestSupport.RunSta` / `RunInIsolatedWindow` が本体を `catch (Exception ex) when (GuiFailureDiagnostics.Capture(ex))` の層で囲む。フィルターは例外処理の 1 回目の走査で、テストがウィンドウを閉じる finally より前に、所有するスレッドで動く。
  - **`Capture` は必ず false を返し、例外を外へ出さない**。true を返すとこの catch が元の失敗を飲み込み、投げると元の失敗が別の例外に置き換わる。区画ごと・出力先ごとに失敗を受け止め「記録に失敗」と書く。
  - 書き出すもの：入力（フォーカス・キャプチャ・左ボタンの状態＝WPF の `Mouse.LeftButton` と実際の `GetAsyncKeyState(VK_LBUTTON)` の両方）、`Track` したウィンドウ、前面のウィンドウ、`CanvasViewportBehavior` / `DragBehavior` / `RubberBandBehavior` の静的フィールド（反射で全部）、`Track` したウィンドウのイベントの時系列。出力先はテストの出力・標準エラー・`%TEMP%\QuickER.Tests-gui-failure-state.log`。
  - 前面のウィンドウが別のプロセスなら、タイトルを書かずプロセス名だけにする（利用者のほかのアプリのタイトルを %TEMP% のログや CI の出力へ残さない）。
  - `GuiFailureDiagnostics.cs` は目印の API を使うがテストを持たないので、`SourceRuleTests` の規則 `window-activation-collection` の範囲の除外に理由つきで載せる。
  - ガード：`GuiFailureDiagnosticsTests`（false を返す・区画が投げても false で例外を出さない・RunSta / RunInIsolatedWindow の失敗が元の例外のまま・閉じる前の状態と時系列・前面の別のプロセスのタイトルを書かない）。mutation 実証済み（前面のタイトルは前面が別のプロセスのときだけ観測できる）。
- Space で押せることを見るテストは、先に `TestSupport.PhysicalMouseButtons.WaitUntilReleased` を通す。
  - 理由：この記録で、実行中に実際のマウスのボタンが押されたままだったために失敗していたと分かった（`Mouse.LeftButton=Pressed`・`GetAsyncKeyState(VK_LBUTTON)` の最上位ビットが立ち、キャプチャがボタンに残る）。WPF の `ButtonBase` は左右のボタンが離されていないと Space の解放で Click しない。製品の不具合ではない。Enter は押下で Click するので関係しない。
  - 動作：左右のボタンが離されるまで約 30 ms ごとにメッセージを処理しながら待つ。約 10 秒で「実際のマウスのボタンが押されたまま」と押されているボタンを名指しして失敗する（スキップしない）。離されていれば待たない。使うのは `CanvasViewportBehaviorSpaceKeyTests` の `SendKey`。
  - ガード：`PhysicalMouseButtonsTests`（ボタンの状態と待ち方を差し替え、押されている間は待って離されたら進む・離されていれば待たない・上限で名指しして失敗する）。mutation 実証済み。

## ソースを走査するガード

- ソースの規則は、規則の表（`tests/QuickER.Tests/TestSupport/Guards/`・名前空間 `QuickER.Tests.TestSupport.Guards`）の行にする。
  - 行（`SourceRule`）は、Id・守ること・照合の形・走査する範囲（`SourceScope`＝フォルダ・拡張子・明示のファイル・読む領域〔`Within`＝根からの相対パスの接頭辞。`QuickER.Provider` で本体と 5 方言のプロジェクトを 1 つの範囲にする〕・理由つきの除外）・見つける形（`GuardFinder`＝構文の節・トークン・シンボル・ファイル全体の文字列か XML）・既知の一覧（`GuardKnown`＝ファイル・何か・件数・理由）を持つ。行は領域ごとの partial（`SourceRules.{領域}.cs`）に置く。
  - 照合の形：禁止（`SourceRule.Forbid`＝一覧は理由つきの例外）と必須（`SourceRule.Require`＝一覧はあるべき出現）。どちらも「ファイル・何か」ごとの件数まで 2 方向で照合する（`GuardKnownList.AssertMatches`）。
  - 空振りの検知は 3 つ：範囲に入るファイルの最低数（`MinimumFiles`・既定 1）、同じ見つける形を別の範囲に当てた厳密な期待（`Witness`）、範囲の中の候補（規則が調べる対象）の数の下限（`Floor`＝候補を見つける形・下限・名前。同じ 1 回の走査で数える）。拾えることを別の見つける形で確かめるもの（目印が既知のテストを拾う・正本の所有者が見つかる）は、必須の規則として別の行にする。
  - 走査は `GuardScan` の 1 か所：ファイルで読む規則はすべての規則の範囲に入るファイルを 1 回ずつ読み、そのファイルに当たる規則を全部当てる（構文木はファイルごとに 1 回だけ作り、当て終えたら捨てる）。シンボルの規則は `SourceSymbolScan.SrcProjects` の各プロジェクトを `ScanNodes` で 1 回ずつたどる。
  - 自己検査の行（`SourceRuleProbe`・`SourceRuleProbes.{領域}.cs`）は、合成のソースへ規則の見つける形を当て、拾う「何か」の並びをソースの順まで照合する（拾わない書き方を拾えば並びが崩れて赤）。シンボルの規則は合成のソースを 1 プロジェクトとしてコンパイルし（条件コンパイルの変種のコンパイルも作る）、エラーが無いことも確かめる。
  - 網羅：読み方 × 照合の形 × 拾うべき出現の置き場（普通の場所・条件コンパイルの外れる分岐＝自己検査の行 `*-inactive-branch`）の組を、自己検査の行で数える（`GuardDimensions`・台帳の Guard/SourceRules）。XML は分岐を持たないので理由つきの isValid で外す。網羅しない組は 0。構文の節・トークンの必須の規則は `program-single-instance-uses`（節）と `app-ui-crash-handler-uses`（トークンの並び）で、文字列で読む必須の規則と違いコメントの中だけの表記では満たされない（自己検査の行 `*-comment-only`）。
  - ガード：`SourceRuleTests`（`Rule_Holds`＝1 行 1 規則・`Probe_Holds`・`Table_IsWellFormed`＝Id の重複・理由の空・件数・必須の規則の一覧の空・範囲の除外の理由・`SymbolScan_ResolvesBclCallsInEveryProject`・`Coverage_MatchesGaps`）。
  - 規則の表のファイル自身の文字列が tests を走査する規則に写り込むときは、範囲を狭めずに綴りを割って避ける。
- 正本の一覧と写しの一致は照合の表（`ParityRow`・`ParityRows.{領域}.cs`）の行にする。行は正本の関数・写しの関数・比べ方（Set／Multiset／Sequence／CopyWithinCanon／CanonWithinCopy。元の比べ方より弱くしない）・正本の最低数（`MinimumCanon`）を持ち、読み取りの失敗は関数が例外を投げて行の赤になる。使わない比べ方は列挙に残さない。ガード：`ParityTests`（`Parity_Holds`・比べ方の自己検査 `Comparer_DetectsDisagreement`＝比べ方 × 一致・食い違いの網羅・`Table_IsWellFormed`）。台帳は Guard/Parity。
- 英語であること（CJK が無い）は英語の文言の表（`EnglishTextRow`・`EnglishTextRows.{領域}.cs`）、プロンプトが決めた語を含む・含まない・決めた文で終わること（`PhraseRow.EndsWith`＝序数・前後の空白を除かない）は必須の文言の表（`PhraseRow`・`PhraseRows.{領域}.cs`）の行にする。英語の行は出どころの関数が返す「どこか・本文」の並びを `CjkText.Pattern` で走査し、例外を「どこか」ごとの件数まで 2 方向で照合する。UI 言語を切り替えて出力を作る行は `EnglishTextRows.UnderUiCulture` を通す（写しを置かない）。ガード：`PhraseTests`（`English_Holds`・`Phrases_Hold`・判定の自己検査 `Judge_DetectsViolation`＝種類〔英語・含む・末尾〕× 違反の有無の網羅・`Table_IsWellFormed`）。台帳は Guard/Phrases。
- src・`.github/workflows`・`scripts` のコメントがテストのクラス名を名指ししているもの（`ClaudeMdParityTests`・`DocsIndexParityTests`・`ResxKeyParityTests` など）は、表へ移してもクラスを消さない（全部のメソッドが移って空になるなら移さない）。
- ソースを走査するガードは、条件コンパイル（`#if` / `#elif` / `#else`）のどの分岐も読む。1 通りのプリプロセッサの記号でパースすると、外れる分岐（`#if !DEBUG` の中・`#if QUICKER_*` の `#else`）は無効なテキスト（トリビア）になり、そこに書いた禁止の呼び出しをどのガードも見落とす。
- 規則の表へ移していない構文のガード（UnsupportedCoverage）は、`TestSupport.ConditionalSourceParse.Parse` と `DescendantNodes` / `DescendantTokens` を通す。
  - `Parse`：ファイルの `#if` / `#elif` の条件に現れる記号を構文から集め、定義の有無のすべての組み合わせで 1 本ずつ構文木を作る。先頭は基準の設定の構文木。記号の無いファイルは 1 本だけ。
  - `DescendantNodes` / `DescendantTokens`：組み合わせをまたいで種類・範囲・トークンの並びが同じ節を 1 回だけにする（分岐の外の呼び出しを組み合わせの数だけ数えない）。範囲だけで比べないのは、分岐を含む節は範囲が同じでも子が違うため。
- シンボルの読み方（規則の表の `GuardFinder.Symbols`）：`SourceSymbolScan` が、条件コンパイルを持つファイルのあるプロジェクト（いまは QuickER.Gui だけ＝`DEBUG` と `QUICKER_*` の 3 つで 16 通り）に、そのファイルの構文木だけを差し替えた変種のコンパイル（`SourceProject.Variants`）を作る。`SourceProject.ScanNodes` が同じ規則で重ねずに返す。既知の一覧の件数は変わらない。変種のコンパイルも BCL の呼び出しを解決できることを `SourceRuleTests.SymbolScan_ResolvesBclCallsInEveryProject` が表明する。
- 記号は 1 つのファイル（変種はプロジェクト）で 6 個 = 64 通りまで。超えると黙って間引かず、ファイル名を添えて例外にする。
- テストが Roslyn のパース（`ParseText` / `ParseSyntaxTree`）を直接呼ぶのは、`SourceRuleTests` の規則 `direct-roslyn-parses` の理由つきの一覧（生成器・レンダラーの出力を 1 つの構成でコンパイル・読み取りするもの・入口そのもの・自己検査の対照とプローブ）にあるものだけ。tests/ の全 `.cs` をファイル・メソッド名と件数で厳密に照合する。
- `SourceSymbolScan` は QuickER.Gui の暗黙の using を読む（ファイル名は AssemblyName でなく .csproj の名前で付く）。
- ガード：`ConditionalSourceParseTests`（`#if !DEBUG` と `QUICKER_*` の `#else` の中の呼び出しが、1 通りのパースでは見えず全組み合わせでは見える〔構文とシンボルの両方〕・実ファイルの組み合わせの数＝UpdateFeed.cs 2・FeatureModuleCatalog.cs 8・条件コンパイルの無いファイル 1・分岐の外を重ねない・入れ子の条件・上限・QuickER.Gui の 15 の変種〔エラーなし〕）。mutation 実証済み。

## EF Core のテストの構成

- テストで DbContext のプロバイダを指定する呼び出し（`UseSqlite(` / `UseSqlServer(` / `UseNpgsql(` / `UseOracle(` / `UseMySQL(`）の直後には、必ず `.IgnoreServiceProviderCacheLimit()` を続ける。置き場は `tests/QuickER.Tests/TestSupport/EfCoreTestOptions.cs`（Integration は Compile のリンクで同じ 1 ファイルを使う）。
- 理由：EF Core は構成が違うたびに内部のサービスプロバイダを作ってプロセス全体でキャッシュし、20 を超えると ManyServiceProvidersCreatedWarning（既定でエラー）で、たまたま 21 個目を作った DbContext を失敗させる。テストはフィクスチャごと（値オブジェクトの翻訳の拡張の型が違う）・プロバイダごとに構成が違うので、全件実行で順序しだいに関係の無いテストが落ちる。製品・生成コードは変えず、テストの構成でこの警告だけを無視する。
- ガード：`SourceRuleTests` の規則 `ef-core-provider-cache-limit`（候補の数の下限 41）と `EfCoreServiceProviderLimitGuardTests`。テストのソースの全呼び出しの直後にこれが続くこと（括弧の対応を読んで検査）と、内部のサービスプロバイダを 25 個作っても失敗しないこと（インスタンスごとにプロバイダが別になるシングルトンのインターセプター＝`IMaterializationInterceptor` で作る。コマンドのインターセプターでは増えない）を固定する。mutation 実証済み。

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
  - 列は種の単位と同じ。MySQL は種の単位に無い固定長の文字列・符号つき整数・decimal・日時・Guid（`char(36)`）も持つ。MySQL の主キーと `parent` は `varchar(36)`（固定長の `char(36)` は型マッパーが Guid へ写すので、string の主キーの GuidKey にならない）。
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
  - CA1305 を src で有効にすると診断が多く出るうえ、文字列への素の補間（`$"…"`）と連結を診断しない（診断するのは `string.Format`・`ToString()`・`Convert` と `StringBuilder.Append` / `AppendLine` へ渡した補間など）。入れずに `SourceRuleTests` の規則 `numeric-interpolation-culture` で数える。
  - テストで翻訳器が受け付ける・断る呼び出しの形そのもの（式木の `Equals(string)`・`ToUpper()`）を確かめる箇所だけ、理由つきの `#pragma` で抑える。
- 生成コード（ランタイムのパッケージ用ソースとフィクスチャ）は、先頭の `// <auto-generated />` と `.g.cs` の名前でビルドのアナライザから外れる。`GeneratedCodeCultureAnalyzerTests` が同じ 4 規則を `generated_code = false` の設定で掛け、診断を理由つきの既知の一覧（いまは空）と厳密に照合する。アナライザは src と同じ SDK 同梱の DLL で、テストの csproj が `@(Analyzer)` の場所を AssemblyMetadata で渡す。
- これらの規則は式木の中の呼び出しを診断しない（翻訳される比較なので多重定義を変えられない）。簡易 DSL と手書きの LINQ の条件のインメモリでの比べ方は、実行器の `PredicateComparisonRewriter` が `string.StartsWith(string)` / `EndsWith(string)` を序数の多重定義へ書き換えて揃える。`Contains(string)` と値オブジェクトの文字列一致は元から序数（docs の「インメモリは序数」）。
- 正規表現の数字は `[0-9]` で書き、`\d` は `RegexOptions.ECMAScript` を付けたときだけ使う。.NET の `\d` は全角などの Unicode の数字にも一致し、インバリアントの数値の解析は ASCII しか読まない。SQL Server の型の表記の正規化と型カタログが `FormatException` で落ち、`SqlTypeText.IsSafe` は DB が断る `varchar(５０)` を安全とみなす。
- Excel 定義書の `UQ{n}` は、後ろに Unicode の数字が続けば一致させない（`UQ1２` を `UQ1` と読まない）。
- 名前の並びで決める値（`SchemaSignature` のテーブルとリレーション）は序数で並べる。カルチャが等しいとみなす合成済みの文字と分解形・ソフトハイフンの有無だけが違う名前は、既定の比較では入力の順が残って置き換えの確認が余計に出る。
- 数値の補間のガード（`SourceRuleTests` の規則 `numeric-interpolation-culture`・見つける形は `Guards/Finders/CultureDependentText`）：`QuickER.Provider*` と `QuickER.Dialects` を `SourceSymbolScan` で組み、`ScanNodes` で条件コンパイルのどの分岐も読む。補間の穴と文字列の連結の被演算子のうち、型が数値（整数・decimal・double・float）か日時（DateTime・DateTimeOffset・DateOnly・TimeOnly・TimeSpan。Nullable も）のものをシンボルで数え、第 1 引数が `CultureInfo.InvariantCulture` の `string.Create` の中は数えない。ファイルと件数で理由つきの既知の一覧と厳密に照合する。
  - 理由の分類は「利用者向けの文言」「例外のメッセージ」「負にならない値」。いまは「負にならない値」の 1 件だけ（プロセスの外へ出ない `QueryParameterTypeResolver` の仮の列名）。照合のキーの部分の長さは `SignatureText.Part` が `InvariantCulture` で書く。
  - 書き出す型の表記（取込の `FormatDataType` と 5 方言の型カタログ・SQL Server の型の表記の正規化）・同期スクリプトの見出し・接続文字列のポート・SQLite の取込の外部キー名・Oracle の説明の `CHR(n)`・識別子の上限の英語の表記（例外文と MCP の注意文）は、一覧に載せず `string.Create` へ直した。
  - 整数の書式がカルチャで変わるのは負号だけ（.NET は数字を置き換えず、既定の書式は桁区切りを入れない）。直した箇所はどれも負の値でだけ食い違う。カタログは負の長さを返さない。
- その他のガード：
  - `CultureInvariantDialectOutputTests`：5 方言の型の解析と変換と正規化を、`CultureScope.Cultures`（tr-TR・de-DE・ja-JP・sv-SE・th-TH）でインバリアントと照合する。DDL・同期スクリプトは `DialectDdlTableTests` の全行に掛かる自動の照合が、同じカルチャでインバリアントと同じこと・DDL の生成日時の行があって西暦であることを確かめる（固定フィクスチャの定義と方言の型の選択肢を載せた図は `DialectDdlRows.Culture.cs` の行）。`CultureInvariantDialectOutputTests.ImportedTypeSpelling_IsCultureInvariant` は、取込が DB のカタログから型の表記を組み立てる関数（SQL Server・Oracle の `FormatDataType`・PostgreSQL の `NormalizeFormatType`・MySQL の `MySqlTypeNotation.Normalize`）へ、負の値を含む長さ・精度・スケールの組み合わせを総当たりで流す（SQLite は宣言の型をそのまま入れるので対象に無い）。
  - `CultureInvariantGenerationTests`：生成物の全ドリフトテストを各カルチャで流し直す。
  - `CultureInvariantExportTests`：SVG の出力。Mermaid・DBML・HTML・Excel の出力と、Mermaid・DBML・Excel・C# リバースの取込は、形式の往復のエンジンのカルチャの代表の形（`FormatRoundTripTests.CultureProbe_IsInvariant`・形の行 `fixture-generated`・`culture-turkish-i`）。
  - `CultureInvariantToolOutputTests`：MCP の要約・変更系ツールが保存する図。名前付きクエリの一覧は `QueryToolFormatterTests` の行 `formatter-listing-is-culture-invariant-english`。
  - `SourceRuleTests` の規則 `regex-ignore-case-culture`・`regex-digit-classes`（候補の数の下限 `Floor` つき）：src の `RegexOptions.IgnoreCase` を含むビット和に `CultureInvariant` があることとインラインの `(?i` が無いことの構文木の全数走査と、文字列リテラルの `\d`（奇数個の `\` に続く `d`）が `ECMAScript` の付いた呼び出し・属性の引数の外に無いこと。
  - 全角の数字：`DialectTypeTableTests` の `fullwidth-*` の行が、全角の数字を含む型の表記を 5 方言の型カタログ・型マッパー・正規化と `SqlTypeText.IsSafe` へ流し、例外にならず数字として読まないことを確かめる。`FullWidthDigitTypeNotationTests` が型トークン・長さだけの違いの判定・Mermaid の型の復元・Excel の `UQ` 番号で同じことを確かめる。
  - `GeneratedCodeCultureAnalyzerTests`：ランタイム 7 本と全フィクスチャ。自己検査は、auto-generated の印のあるソースで 4 規則が出て式木の中では出ないこと。
  - `InMemoryStringMatchOrdinalTests`：値オブジェクトの有無 × カルチャで、手書きの LINQ の前方・後方一致を SQLite の QuickER 版 Repository とインメモリで照合する。簡易 DSL の LIKE は `DslConditionFuzzTests`（網羅の単位 `DslFuzzUnits.Coverage`・`DslFuzzUnits.PlainCoverage` の序数の値と、序数のクエリ KO0〜KO3 だけをインメモリの範囲で ja-JP・tr-TR にも周回する）。
  - `SchemaSignatureTests` の並び順の 2 件。`MockOutputsTests`（行 `resume-revision-timestamp-culture-invariant`）と `ClipboardImageAttachmentReaderTests` の各カルチャの日時。
  - `TableRebuildAttributeDetectorCultureTests`：`QuickER.Provider.dll` を新しい読み込みコンテキストへ読み込み直し、tr-TR の下で型を初期化した実挙動を見る。読み込み直したアセンブリは回収までほかのテストから同じ名前の写しとして見えるので、非並列のコレクション `IsolatedAssemblyLoadTestCollection` で走らせ、回収を確かめてから戻る。WPF を含むアセンブリは読み込み直さない（pack URI が写しへ解決され、全件実行でだけウィンドウの `InitializeComponent` が失敗する）。
  - `CultureInvariantRuntimeTextTests`：同期のキーのテキストの decimal（`SyncKeyText.FormatDecimal` / `ParseDecimal` / `WithoutDecimalExponent`）と SQLite の `PRAGMA busy_timeout`（`SqlConnectionFactory`・`SqliteBusyTimeoutInterceptor`）を各カルチャで。組み立てる側で InvariantCulture を渡しているのを固定するだけ。
- src に `ToUpper()`・`EndsWith(string)`・`InvariantCulture` の比較を書くと、4 規則ともビルドが止まる。mutation 実証済み。
