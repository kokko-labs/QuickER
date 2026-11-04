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
- 決定的な用途だけの手とフックの振る舞い（`AfterWriteBlob`・`AfterExecuteSql`・`EditBlobsThenContinue`）は乱数の生成器に混ぜず、次元表の水準から外す（`AssignKey` と同じ）。

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

### 結果の表

- `BackendParityOutcomeDimensions`（登録簿の部分 GeneratedRuntime/ParityOutcomes）は、基準の記録で Save フックが受けた段（Before / After）× 操作（`SaveOperation` の 3 値。因子の棚卸しの Source）× 当たった規則の振る舞い（After × 飛ばす系）を持つ。Before で投げるのは原理的に起きない組。
- mutation 実証済み（紹介者の付け替え・代替キーの更新・連絡先の customer_code・Save フックの規則の末尾の手・EF Core 単独のサーバーのフック登録・比べ方の条件）。

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
- `RuntimeTestMatrixTests` の行列の登録は、担当のクラスに加えて実装先の一致のシナリオの一覧（`Scenarios`）を持てる。`BackendParityRegistrations_NameScenariosRunningOnTheColumn` が、一覧のシナリオが実在し、その列の実装先で流れることを照合する。
- EF Core の代替キーの書き込みが同じ保存の削除より先に走る違いは、シナリオ `graph-updated-row-takes-alternate-key-of-deleted-row` と既知の違いの一覧の Pending 8 項目（EfCore・RemoteEfCoreOnly × 4 観点）で照合する。

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
- 次元表 `SyncModelDimensions`：
  - OpFamily に 4 つの形、因子 UniqueTable・PropagateDeletes・Excluded・Batch・Blobs（後ろ 4 つは SyncOptions の Source）・FaultKind。
  - 3 因子の組 (Policy, OpFamily, PropagateDeletes)・(Actor, OpFamily, UniqueTable)。
  - Blobs=False は受け入れの組（乱数は常に blob を運ぶ。docs の「運ぶかは端末ごとに一貫させる」）。
- 実行の結果の表 `SyncModelOutcomeDimensions`（登録簿 SyncModel/Outcomes）：SyncConflictReason と SyncLocalRowChangeReason の全値×Mode×Policy。実行の後の結果だけを数え、乱数を引かない。
- 網羅の行 `SyncModelCoverageRows`（Id は `coverage-`・10 行）：
  - 乱数の種で作れない組を持つ。サーバーの他者の Extras の操作 4 つ（作り直し・失敗する書き込み・キーだけのスタブ・持っていた実体での削除）・グラフ保存の失敗する書き込み・DuplicateOnServer。
  - 既定の 60 種でたまたま欠ける組も持つ。表を除外したダウンロード・後勝ちの RemovedBeforeDownload・Collect と LocalWins の ReloadedForUniqueValue。
  - 直結の乱数のテスト `RandomSeeds_ConvergeWithoutLoss` が種の前に並列に流す（単独の種では流さない）。操作を 1 歩ずつ次元表で、同期の結果を結果の表で数える。
  - 行は生成器の代わりにモード・ポリシー・種の組を宣言する（例：`coverage-server-delete-stub`・`coverage-duplicate-on-server`）（`SyncModelDimensions.Observe` の多重定義）。行の同期が宣言と違えば構築で断る。
  - 決定的なシナリオ（`SyncModelScenarios`）は別の Theory で流れて網羅の計数に入らないが、網羅の行は数える。行は期待の操作を持たず、次元表に無い操作の型は宣言の誤り。
  - 行の違反は既知の失敗の一覧で外さず、すべて新しい失敗として赤にする。
  - 残る網羅しない組は Blobs=False の受け入れだけ。
  - mutation 実証済み。
- 静止化が終わらないときは、最後の周の打ち切りと競合を分類して署名を細かくする（`SyncModelRun.ClassifyStuckTruncationsAsync`）。
  - 一意な値の厳密な入れ替え：`1/no-quiesce:unique-swap:{表}`（docs の既知の割り切り＝受け入れ）。
  - 値を取る行自身の古い参照が持ち主を残す：`1/no-quiesce:holder-referenced-by-taker:{表}`。
  - 端末側の一意な値の循環：`1/no-quiesce:pending-unique-cycle:{表}`。
  - 入れ替えと、値を取る行自身の古い参照の場合は打ち切った行と持ち主の行の、循環は集まりの行の競合を説明済みにする。
  - どれにも当たらない打ち切りか説明の付かない競合が残れば、粗い署名 `1/no-quiesce:conflicts-or-truncations` もあわせて出す。分類できた分が隠さない。
  - 固定は `swap-folder-names-without-refresh-does-not-settle`・`swap-without-refresh-does-not-settle`・`n15-pending-unique-cycle`。mutation 実証済み。
- 既知の失敗の一覧の乱数の項目のうち RegularSeeds / NightlySeeds の項目は、全部の種をまとめて照合して「起きない」も赤にする。種ごとの照合は「載っていない」しか見ないため。
- 乱数の種の照合は、静止化で起きた例外を途中の同期の例外と同じに扱い、表明 2・3 の署名から表の名前を外して行う（`SyncModelRuntimeTests.Family`）。
- 種の数は `QUICKER_SYNC_MODEL_SEEDS`（既定 60）。時刻と乱数の出どころは種だけ。

### シナリオの操作とモデルの期待

- 決定的なシナリオは、エンジンの事実を期待の操作で表明する（操作は次元表に載せず、乱数の操作の列には混ぜない）
  - `ExpectPayloadOp` / `ExpectNoAckOp`：サーバーが断った書き込みが、行の書き込みで止まり受理記録も blob も残さない（`n6-*`）
  - `ExpectLocalRowChangeOp`：実行が自分から変えた行の一覧（`SyncResult.LocalRowChanges`）に、表・キー・理由が載る（`thrown-serverwins-run-hides-settlement` など）
  - `ModelRefreshOp.ExpectScopeRefusal`：範囲外の行が範囲内の行を参照する洗い替えが断られ、端末の DB が変わらない（`42-refresh-refused-by-rows-outside`）
  - `LegacyKeyTextsOp`：以前の版が書いた形のキーのテキストを作る（`25-legacy-journal-key-text` / `25-legacy-ack-key-text`）
  - `RecordJournalOp`：ジャーナルへ直接記録して、プロセスごと落ちて残った記録を作る（`replay-unlanded-delete-entry-is-discarded`）
  - `LeftoverUnsetKeyEntryOp` / `ExpectNoUnsetKeyEntryOp`：キーの決まらない記録が落ちて残った形と、アップロードの判定がそれを消すこと（`e2-leftover-unset-key-entry`）
  - サーバーの他者の操作は、ハーネスの `ApplyOnServerAsync` がサーバー役のリポジトリのグラフ保存での削除も行う（網羅の行のキーだけのスタブ・持っていた実体での削除）
- 例外で終わった後勝ち＋ServerWins の実行でも、モデルは実行前の未送信の記録と突き合わせて捨てた行を推定する（`SyncModelRun.SyncAsync` の注入した失敗の catch から `RecordLastWriteWinsSettlementsAsync`）
  - 推定するのは、記録が消え、送るものがあり、サーバーの値が送った値でなく、ServerWins がローカルを戻したか、サーバーに行の無い upsert か、実行が DiscardedByServerWins と報告した行だけ（書き込みの履歴の最後の値で比べると、blob だけを書いた品目を「削除を書いた」と取り違える）
- 空の blob の期待（N12）
  - 空の blob を見逃すのは、空でない blob を書いた端末と、消された品目を上げ直した端末（同期の前にサーバーに無く後にある＝挿入の経路・`_recreators`）がすべて、その品目の競合を報告されたか ServerWins に変更を捨てられたときだけ（`_conflictsTo`）
  - blob を書いた後でその品目が削除され作り直されたら、期待値を決めない（`_blobHistoryCut`）
  - 対照：`SyncModelRuntimeTests.EmptyBlobWithoutConflict_IsStillAViolation`（blob を運ばない実行で上げた新しい行の空の blob は違反）・`EmptyBlobOfRecreatedRowWithoutConflict_IsStillAViolation`（blob を運ばない後勝ちで作り直した行）

### HTTP・実 SQL Server の派生

- `SyncModelHttpRuntimeTests`：in-process の Kestrel・生成された HTTP の差分ソースと同期専用のエンドポイント。サーバー側の注入した失敗は 500 で届くので、実行中に失敗が起きたかで見分ける。
- `SyncModelSqlServerRuntimeTests`（Docker）：世界ごとにデータベースを作る実 SQL Server のサーバー役。生成された直結の差分ソース・本物の rowversion。サーバー側の失敗を仕掛けるシナリオと組は流さない。
- どちらも次を確かめる。
  - 決定的なシナリオが直結と同じ違反になること。例外は入口・違反の種類・推定した形へ正規化して比べる（`SyncModelSignatures`）。
  - 乱数の種が既知の失敗にしか当たらないこと。種の数は `QUICKER_SYNC_MODEL_TRANSPORT_SEEDS`（既定 10）。
- SQL Server でだけ起きる既知の失敗は `SyncModelSqlServerRuntimeTests.SqlServerOnly` に置く。
- 既知の失敗は `SyncModelKnownFailures` の厳格な一覧（載っているシナリオはその署名の組どおりに失敗しなければ赤・載っていない失敗も赤）で一時的に外し、直したら消す。受け入れの項目（サーバーでの一意な値の入れ替え 2 本）は、理由に docs の既知の割り切りの文を名指しする。

### 同期の構成の行列

- `SyncConfigMatrixRuntimeTests`（登録簿 SyncModel/Config・`SyncConfigDimensions`・Docker）は、組める組の全積 60 行を流す。軸は次のとおり。
  - 主キーの型：行列の軸 `SyncConfigFixtures`（IntKey＝SyncFixture・VoKey＝SyncVoFixture）。
  - ローカルの登録のキーの有無。
  - サーバーの差分ソース：直結の keyed / 非 keyed・HTTP の baseAddress / HttpClient ファクトリ。
  - AddGeneratedSyncSupport か分けた登録。
  - キーを決める Before フックの有無。
  - 直接の挿入かグラフ保存。
- 流れは「登録 → 解決 → 登録の形の検査 → 新しい行を同期 1 回で上げる → サーバーの新しい行を 1 回で降ろす」。
  - 登録の形（`SyncConfigFixtures.RegistrationProblems`）：ローカルのリポジトリがジャーナル記録デコレータ／差分ソースが転送経路の型（HTTP は HttpSyncServerSource・直結は DirectSyncSource）／サーバーのリポジトリは素のまま。
  - 2 回目の同期が降ろすのはその 1 行だけ（ミラー版から導いたアンカー）。
- 既知の失敗は KnownCaseLedger で、今は空。行の由来の指摘は行に残す。
- 網羅しない組の一覧は空。VoKey×HTTP は SyncVoFixture にリモートサービスを足して流す。
- mutation 実証済み。

### 割り込みの点

- 同期の実行の名前つきの時点（`ModelInterruptPoint`）で、実行と別の流れからその端末のローカルの書き込みを 1 回差し込む。操作は `ArmInterruptOp`、仕組みは `SyncModelFaults.InterruptAsync`（呼ぶのは SQLite のサーバー役の包み）。
  - 時点：サーバーへの行の書き込みの直前／差分・ページ・キーの取得の直前／取得が 1 行以上を返した後でエンジンがロックを取って適用する前。
  - 別の流れは `ExecutionContext.SuppressFlow` の下の `Task.Run`。同期の抑止の印も書き込みロックのリースも引き継がない。
- 点はどれも、エンジンがローカルの書き込みロックを持たない時点に置く。割り込みの書き込みはデコレータの書き込みロックを待つので、ロックの中に置くと待ち合って止まる（適用の最中・洗い替えには差し込めない）。上限 30 秒で待つのをやめ、`interrupt/deadlock` の違反にする。
- 割り込みの書き込みは通常の書き込みと同じ経路（`WriteAsync`）で、書き手の操作として履歴へ積む。
- 仕掛けたのに点に来なかった割り込みは `interrupt/not-fired:{点}` の違反にする（黙った空振りの守り）。
- 決定的なシナリオは点 3 × 形 3（未送信の行の更新・未送信の行の削除・作り直した行の再削除）の 9 行＋フォルダーの `d1-folder-recreate-delete` の 1 行。`interrupt-BeforeServerWrite-delete-recreated` もこの形。行の宣言が全積を含むことは `SyncModelRuntimeTests.InterruptScenarios_CoverEveryPointAndShape` が表明する。
- 割り込みは乱数の操作の列に入れない。生成器は毎歩実際の DB を読んで操作を選ぶので、派生の乱数で割り込みを選んでも既存の種の列が変わる。入れるなら別の種の列にする。
- 実 SQL Server の転送は包みを持たないので、割り込みのシナリオを流さない（`SyncModelScenario.ArmsServerFaults`）。
- mutation 実証済み。
