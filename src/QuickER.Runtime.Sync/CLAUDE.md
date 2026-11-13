# QuickER.Runtime.Sync

双方向同期支援（`GenerateSyncSupport`）の実装の骨格と、壊すと静かに回帰する判断を置く。
固定エンジン（このパッケージ）と生成器の側（対象テーブル・診断・記述子・DI 登録の生成）の両方を扱う。
利用者向けの意味論（競合ポリシー 3 択・削除伝搬・洗い替えの使い分け・blob の既定・後勝ちの取引・既知の割り切り）の正本は `docs/code-generation.md`（.ja）の「双方向同期の支援」節で、ここでは再述しない。
同期のテストのエンジン（同期のモデル・構成の行列・割り込みの点・網羅の行・シナリオの置き場と Theory の分割・サーバー役）の仕組みは `tests/QuickER.Tests.Integration/GeneratedRuntime/CLAUDE.md`「双方向同期のテスト」にある。

## 正本と生成物の置き場

- `Runtime.Sync.g.cs` は `src/QuickER.CodeGen.CSharp/Templates/CSharpRuntime/_10_Sync.scriban` から再生成するパッケージ用ソースで、手編集しない。
- 固定エンジン（`SyncEngine` / `SyncJournal` / `SyncTableBase` 階層 / `SyncSession` / 結果・競合のレコード、リモートサービス併用時は同期エンベロープと `HttpSyncServerSource<,>`）はこのパッケージ（Core への ProjectReference のみ・PackageReference ゼロ）。分割生成では `Runtime.Sync.g.cs`。
- per-entity（記述子・ジャーナル記録デコレータ・直結／HTTP 差分ソース・DI 登録）は生成側の `Repositories.Sync.g.cs`。
- 固定型は descriptor 化してある＝per-entity は `SyncTableDescriptor<TEntity,TKey>`（テーブル名・ルート名・SQL・キー／版の Func・バイナリ列）を生成データとして持ち、版の有無で sealed の `SyncTable<,>` / `VersionlessSyncTable<,>` を選ぶ。差分ソースも sealed の `DirectSyncSource<,>` / `VersionlessDirectSyncSource<,>` / `HttpSyncServerSource<,>`。
- ジャーナル記録デコレータの CRUD / Save / CheckUniqueness の転送は基底へ集約し、per-entity には named query・blob アクセサの転送と記録だけが残る。

## 生成の条件と診断

- bool オプション `GenerateSyncSupport`（既定 false。CLI `--generate-sync-support`／GUI「双方向同期の支援コードを生成する」＝QuickER 版 Repository を生成し、対象 DB が両方言のときだけ表示する）。
- 対象は Repository 契約を持つ全テーブル（単一主キー）。rowversion の有無は対象でなくモードを決める（版あり＝増分＋競合検出／版なし＝後勝ち・全量スキャン）。
- 生成時エラーは 3 つ：実効方言が sqlserver+sqlite のちょうど 2 つでない／`GenerateRepositories` が false／同期可能テーブルが 0 件（`CodeGen_Error_SyncSupportRequiresEligibleTables`）。
- 対象一覧は Info 診断 `CodeGen_Info_SyncSupportTables` で FK 順のまま通知する。
- 主キーの列に NULL 許容の印がある表も対象にする。C# 生成は主キーの列を NOT NULL とみなすので、記述子の `ReadKey` はほかの表と同じく NULL を許さないキーの型を読む（正本は `src/QuickER.CodeGen.CSharp/CLAUDE.md`「主キーの列の NULL 許容」。ガード：`CodegenCaseTests` の行 `compile-nullable-primary-key-sync` / `compile-nullable-primary-key-sync-string-value-objects`）。
- 同期対象の表の主キーが固定長の文字列（nchar / char）なら `CodeGen_Warning_SyncFixedLengthStringPrimaryKeys` で「表.列（型）」を名指しし、可変長の型を勧める。
  - 理由：SQL Server は値を宣言長まで空白で詰めて返すので、ローカルで作った行のキーが同期の後に空白詰めのキーへ置き換わり、詰めていないキーを参照する子の行があると 2 行のまま収束しない（キーの序数照合では直せない）。
  - 判定は型名の文字列でなく正規型の種類（`FixedString` / `AnsiFixedString`）。`CanonicalTypeTokenAttacher` が `CSharpTypeInfo.IsFixedLengthString` を立てる。正本は `CSharpCodeGenerationService.BuildSyncFixedLengthKeyLines`。
  - ガード：`CodegenCaseTests` の行 `diag-sync-fixed-length-key-warning-nchar` / `diag-sync-fixed-length-key-warning-char` / `diag-sync-fixed-length-key-variable-nvarchar-no-warning` / `diag-sync-fixed-length-key-variable-varchar-no-warning` / `diag-sync-fixed-length-key-without-sync-support-no-warning` / `diag-sync-fixed-length-non-key-column-no-warning`（mutation 実証済み）。
- サーバー側の追加スキーマはゼロ。ローカルにだけ共有テーブル 3 つを `SyncJournal.EnsureCreatedAsync` が実行時に作る＝`quicker_sync_journal`（オフライン編集）・`quicker_sync_blob_pending`（ダウンロードが負う blob のコピー）・`quicker_sync_ack`（`(table_name, key_text)` 主キー・`server_row_version BLOB NOT NULL`＝サーバーが受理したアップロードに採番した版の行単位の受理記録）。
- 生成の固定：`SyncSupportGenerationTests`（親参照と被参照テーブル名・対象外の外部キー・一意制約参照の表が `UnlistedReferencingTableNames` に載る・decimal 主キーの共有書式・循環参照の FK 順）。

## アンカーと受理記録

### アンカーは保存せず導出する

- 再開点はローカルのミラー版列の `MAX`。
- 正しさの前提は 3 点：サーバーの変更を rowversion 昇順で取得する／バッチをローカル 1 トランザクション（`SaveAsync(IEnumerable)`）で適用する／**ミラー版を書くのはダウンロードだけ**。
  - 途中で落ちてもローカルは順序付きストリームの接頭辞を保持し、その最大値が次の再開点になる。1 つでも崩すと再開点が嘘になる。
  - 3 点目が抜けると、順序付きストリーム外の版が未取得行を追い越して恒久的に取りこぼす（アップロードの版をミラーへ書き戻さない理由）。
- 上限は `MIN_ACTIVE_ROWVERSION()`（未コミットのトランザクションを跨いだ読み飛ばしを防ぐ・1 パス 1 回）。
- 差分 SQL の `@anchor` / `@ceiling` は NULL を取り得るため `(@anchor IS NULL OR ...)` で「制約なし」を明示し、比較相手は `CAST(@anchor AS binary(8))` へ明示キャストする。
  - 理由：生 SQL のパラメータは型なしで、NULL が nvarchar と推論され `Implicit conversion from data type nvarchar to timestamp is not allowed.` で落ちる。

### 受理記録（ack）はアンカーではない

- ack は行単位の記録で、そこから再開点を導く処理はどこにも無い（だからミラーへ書けない版をここへは書ける）。
- 成功したアップロードの版はミラーへ書かず ack へ記録する（`SyncUploadOutcome.ServerRowVersion` / `UploadedWith` が engine へ運び、engine が `RecordAckAsync`）。ミラーへ書き戻すと上の 3 点目が破れる。
- 版ありの original は **max(ミラー版, ack 版)**（`SyncTableBase.MaxRowVersion`＝big-endian 符号なし比較＝先頭ゼロを除いた有効バイト長→辞書式。null と空はどちらも「版なし」）。
  - ack 優先固定は不可：掃除漏れでミラー > ack が残ると偽競合へ戻る。
  - max 規則の防御は単独の mutation では観測できない（ack 優先固定へ変えても掃除 (i) が ack を消すため差が出ない）＝理屈上の二重化として保持する。

### settle の順序

- settle はエントリ単位で即時・ack 先行。upsert は `RecordAckAsync`（`ServerRowVersion` が非空のときだけ）→`RemoveAsync`、delete は `RemoveAcksAsync`→`RemoveAsync`。どちらも「補助記録が先・エントリが後」。
  - 両方残ったまま落ちても無害（次ランが ack 込みの original で同内容を再送／delete の再送は行なし削除＝no-op）。
  - 逆順にすると「ack なし・ミラー旧」が残って偽競合へ戻る。
  - 閉じられない窓は「サーバー受理直後・ack 書き込み前」だけで、docs の既知の制限に書いてある。
- `SyncJournal.RemoveAsync` は 500 件ずつに分ける（`SyncInClause.ChunkSize`＝方言エンジンの IncludeLoader の `InClauseChunkSize` と同値）。ガード：シナリオ `ul-settles-more-journal-entries-than-one-in-clause`。

### ack の掃除

- 主に 5 点：
  - (i) テーブルのダウンロード完了後に、版を書いたキーのうち ack を持つもの（`SyncDownloadOutcome.SettledAckKeyTexts`＝エコー・通常適用の両方）を**コミット後に**消す。コミット前に消してコミットが失敗すると「ack なし・ミラー旧」の窓が戻る。逆窓（コミット後・削除前に落ちる）は ack が残るだけで次ランが自己回復する。
  - (ii) 削除エントリの settle 成功時。
  - (iii) 削除伝搬が消したキー（`PropagateDeletesAsync` が件数でなく `IReadOnlyList<string>` を返し engine が `RemoveAcksAsync`）。放置すると「サーバー削除→同キー再作成」が死んだ版を original に採って偽 `MissingOnServer` になる。
  - (iv) `RefreshAsync` がスコープ内テーブルの ack を全削除（全部の表の全消しが成功してから＝「洗い替え」）。
- (v) 親の解決役・一意な値の持ち主の読み直しが書いた受理記録は、同じ実行がその行へバッチの古い写しを内容として書いたら、その書き込みの単位のコミットの直後にその場で消す（ダウンロードの flush も洗い替えのバッチも、`SyncParentResolver.EndWriteUnit` が返した行を `RemoveAcksAsync`）。表のダウンロードの終わりへ積まない（同じ表の後のバッチの取得の失敗・取り消しで捨てられ、受理記録の版がミラー版より新しいまま残る）。書き込みの後にもう一度読み直した行の受理記録は残す（バッチの終わりの `ReconcileSettledReceipts` が `SettledAckKeyTexts` から外す）。下の「親をそろえる解決役」。
- ほかに ServerWins の先消しとキーのテキストの書き直しでも消える。
- `SyncJournal.RemoveTableAsync` / `RemoveAllAsync` は ack に触れない（未送信のローカル変更を捨てることは、サーバーが受理した内容について何も言っていない）。実行中に呼ばない。

## FK 順

- テーブルは図から求めたトポロジカル順（親→子）。ダウンロードは親から、削除伝搬とジャーナルの削除再生は逆順（子→親）。
- 循環参照で行き詰まったら、循環に属するテーブルを宣言順で 1 つだけ先に出して続ける（循環の外の子は親の後ろに保たれる）。ガード：`SyncSupportGenerationTests` の「循環参照のある図でも循環の外の子は親の後ろへ並ぶ」。

## ローカル変更の記録

### デコレータ方式と journal-first

- ローカル変更はデコレータ（`Journaling{Entity}Repository`）で追跡する。`ISaveHook` はグラフ保存でしか発火せず直接 CRUD を素通しするため、フックでは穴が開く。
- 全書き込み入口（Insert / Update / Delete / BulkInsert / SaveAsync×2、除外列がある図では `Write{列}Async`）が記録する。
  - 入れる行の無い BulkInsert（空・null の要素だけ）は記録する行が無いので、ロックも `EnsureCreatedAsync` も通らずに内側へ渡す（ジャーナルの表をまだ持たない DB に表を作らない）。ガード：`SyncRuntimeTestsBase.EveryWriteEntryPoint_IsJournaled`（mutation 実証済み）。
- 構築時に除外した表（`excludeFromSync`）のリポジトリも、自分の行を記録しない形（`JournalingRepositoryBase.RecordsOwnRows`＝false）で包む。直接の書き込み（Insert / Update / Delete / BulkInsert / `Write{列}Async`）はロックも記録も `EnsureCreatedAsync` もせずに内側へ渡し、グラフ保存（SaveAsync×2）は記録の段（`RecordAndWriteAsync`＝ロック・観測・突き合わせ）を通す。除外した親のグラフ保存がカスケードで書く同期対象の子を記録するため（包まないと記録が無く、次の同期の削除伝搬がサーバーへ送らずにローカルから消す）。除外した表の行は記録器が外すので記録しない（「グラフ保存はカスケード全体を記録する」）。ガード：構成の行列（`SyncConfigMatrixRuntimeTests`）の水準 `GraphViaExcludedParent`（登録の形の検査・直接の書き込みが記録しないこと・子だけが記録され上がること）。
- 記録は journal-first：生成 Repository は接続を内部管理し業務書き込みと同一トランザクションに乗れないため、意図を先に記録する。
- 記録の後でプロセスごと落ちて残ったエントリは、アップロードが「ローカルの現在行を読み直して送る」ため無害化される（行が無ければ `Discarded`・競合ではない）。
- 生 SQL（`ExecuteSqlAsync`）と `Query().ExecuteDeleteAsync` は記録しない（変更行が文・述語から読めない＝docs 明記の割り切り）。
- Save フックは素通し：`ISaveHook<T>` は従来どおり発火し、ダウンロードで適用する行にも発火する。`SyncSession` が抑えるのはジャーナル記録だけ。

### グラフ保存はカスケード全体を記録する

- 固定の汎用走査 `SyncGraphRecorder` が子孫も記録する。保存側と同じ `EntityBaseCore.EnumerateCascadeChildren`（この用途で public）を歩く＝同じ走査の 2 実装を作らない。
- 規則：Removed は cascadeDelete なら部分木を子から順に Delete／Added・Updated は Upsert／Unchanged は記録なしで cascadeSave なら子へ再帰。
- 削除を記録するのは、保存の DELETE が見つける節点だけ（`CollectDeleteGraph` / `CollectDelete`）。
  - 保存側（`DeleteGraphAsync`）は消す部分木を状態に依らず消すので、別の親の下から移してきた保存済みの子は記録する。
  - 部分木の中の Added の節点は、書かれたことが無いので自分の削除を記録しない（子へは降りる）。
  - キーの無い節点（`FormatEntityKey` が `SyncJournal.UnsetKeyText`）は `WHERE key = NULL` が何も消さないので記録しない。MarkRemoved した新しい子・ルートもここで外れる。読み取り側（`RecordCollectedAsync`）に番兵の特例は置かない。
  - 記録すると、キーの無い記録が版の読み取り（`ParseKey`）で `FormatException` になりデコレータ越しのグラフ保存だけが必ず失敗する。版なしの表では、別の端末が同じキーでサーバーに作った行を版を見ない削除の再生が消す。
  - 割り切り：Added のまま既存の行と同じキーを持つ節点（挿入なら失敗する誤用）は、保存側がその行を消しても記録しない（Added の節点ごとにローカルの存否を読む必要があり、版なしの表には読み口が無い）。docs に明記。
  - ガード：構成の行列の水準 `GraphRemoveParentWithNewChild`（VoKey＝キーの無い子・IntKey＝版なしの子がサーバーの同じキーの行を消さない）。mutation 実証済み。
- レジストリはそのエンジンが同期する表の記述子だけ。`AddGeneratedSyncEngine` が除外した表を外した記録器を 1 つ作り、その登録のすべてのデコレータへ渡す（除外が無ければ `GeneratedSyncTables.GraphRecorder`。デコレータの `(inner, journal)` のコンストラクタも全表の静的な記録器を使う）。構築時に除外した表の行は、どのリポジトリのグラフ保存からも記録しない（記録するとどのランも送らない未送信として数え続け、洗い替えを断り続ける）。未登録の中間型も降下は続け、記録は登録型のみ。ガード：水準 `GraphViaExcludedParent`（除外した親を書き換えるグラフ保存が子だけを記録する。mutation 実証済み）。
- ルートの RowState しか見ない実装は 3 形の実バグになる：カスケード子が上がらない／同じランの削除伝搬が未記録のローカル子行を消す（同期 1 回で子データが両側から喪失）／カスケード削除の再生がサーバー FK に阻まれ恒久ハード失敗。ガード：シナリオ `graph-cascade-save-uploads-parent-and-child`・`graph-child-edit-under-unchanged-root-uploads`・`graph-cascade-delete-replays-child-first`（mutation 実証済み）。
- 一括記録は `SyncJournal.RecordManyAsync`（1 文 100 行の複数行 `VALUES`）。BulkInsert とグラフ保存（`SyncGraphRecorder.CollectSave` で集めて 1 回）が使う。
  - 行ごとの値は列ごとのコレクション引数として渡し、本文に `@tableName0` … を直接書く＝バインダーの「要素 i は `@name`i で束縛・本文の置換は素の `@name` だけ」に乗る。
  - json_each / unhex は SQLite 3.41+ を前提にするため使わない。
- 記録の範囲の (id, 記録) の組は「1 文の複数行 INSERT は id の昇順が VALUES の並びどおり」に頼る。ガード：`SyncRuntimeTestsBase.RecordedIds_FollowTheOrderOfTheRecords`（同梱の e_sqlite3・250 行＝文の境界をまたぐ）、`HookAssignedKeys_OfTwoNewRows_AreRecordedEach`。

### ループ防止

- エンジン自身の書き込み（ダウンロード適用・ジャーナル掃除・版の書き戻し）は `SyncSession.Suppress()`（`AsyncLocal<int>` のカウンタ）で記録を抑える。
- 抑えないと降ろした行が即座にローカル変更として記録され、同じ行が 2 つの DB を往復し続ける。

### 失敗した書き込みの記録を取り消す

- `JournalingRepositoryBase.RecordAndWriteAsync` は記録の段の間だけ内部の `SyncRecordingScope`（AsyncLocal）を立て、`SyncJournal.RecordManyAsync` が書いた記録の id を `INSERT … RETURNING id`（`QueryProjectionBySqlAsync<long>`）で集める。
- 業務の書き込み（または記録の段）が例外で終わったら、書き込みのロックを持ったまま集めた id だけを `RemoveAsync`（`CancellationToken.None`）で消してから `throw;` で投げ直す。
- 同じ行の前からある記録は消さない。範囲の外で直接 `RecordAsync` を呼んだ記録は対象外。
- 消すこと自体の失敗は元の例外の `Exception.Data[JournalCleanupFailureKey]` に入れる。
- 前提は「例外は書き込みが着地していない」：ローカルの SQLite のリポジトリはコミットを最後に行い、コミットの後は `AcceptChanges`（メモリだけ）しか無い。`DbTransaction.CommitAsync` / `DbCommand.ExecuteNonQueryAsync` は Microsoft.Data.Sqlite が上書きしておらず、基底は取り消しをコミット・実行の前にしか見ない。
- デコレータの後ろに置いた自前のリポジトリがコミット後に例外を投げると、着地した書き込みの記録を取り消す（docs に明記）。
- ガード：シナリオ `rec-failed-update-leaves-no-journal-entry`・`rec-failed-blob-write-leaves-no-journal-entry`・`rec-failed-update-keeps-earlier-entries-of-same-row`・`16-failed-*`（mutation 実証済み）。

### 書き込みの後の突き合わせ

- 書き込みが成功したら、同じロックの中で記録と結果を突き合わせる（`JournalingRepositoryBase.SettleRecordedAsync`）。`SyncJournalRecord` の内部プロパティ `Entity` / `Descriptor` / `SavedByGraph` と、記録の範囲が `RETURNING id` で集めた (id, 記録) の組を使い、id の昇順で対応づける。
- グラフ保存の記録で、保存後も `Unchanged` にならなかった行（Before フックが飛ばした行＝`AcceptChanges` が飛ばす）は記録を取り消す。削除の飛ばしは対象外（行が残るので再生の probe が捨てる）。直接の書き込みはフックが走らないので飛ばしの判定をしない。
- 記録のキーとエンティティの今のキーが違えば記録を新しいキーへ書き換える（`SyncJournal.RewriteKeyTextsAsync`＝挿入の記録とグラフ保存の Added の行。受理記録もコピー待ちもまだ無いので記録だけで足りる）。
- キーがまだ無い行（null を取り得るキーで、Before フックがキーを決める新しい行）は、記述子の `FormatEntityKey` が固定の文字列 `SyncJournal.UnsetKeyText`（internal）で記録し、同じ書き換えで行のキーへ移す。
  - 直接の Update の記録（`RecordUpsertAsync`）も、キーを挿入と同じ `FormatEntityKey` で組む（キーが null の行をキーの書式で落とさない＝文字列のキーは key_text の NOT NULL、値オブジェクトのキーは `NullReferenceException`）。書き込みの結果は内側のリポジトリが決める。ガード：`SyncNullKeyRuntimeTests.NullKey_UpdateIsNotBrokenByTheKeyText`（mutation 実証済み）。
  - この文字列の記録は書き込みのロックの中にしか無いので、エンジンの読み取り（`ReadAllAsync`・`CountPendingAsync`・`ReadTableAsync`）は返さない。
  - 落ちて残ったものはアップロードがその表を判定するとき（ロックの中）に消す（`RemoveUnsetKeyEntriesAsync`）。
- 突き合わせは最善努力（書き込みは着地済みで、失敗を例外にすると着地していないと伝えてしまう）。失敗と落ちは記録を残す＝docs の既知の制限。

### Save フックが context で書いた無制限バイナリ列

- 記録はグラフ保存の前にそのときのグラフから作るので、After フックが `WriteBinaryColumnAsync` で書いた列は記録に載らず、サーバーへ送られないまま次のダウンロードがサーバーの値で上書きする。これを塞ぐ仕組みが 3 つ：
  - (a) Core の `SaveHookContextWrites`（`AsyncLocal` の観測口・`Observe` は入れ子で内側が勝ち破棄で外側へ戻る・`Detach` は観測を取り外して呼び出し元へ渡す）と `SaveHookBinaryColumnWrite`（エンティティ型・プロパティ名・キー・context）。
  - (b) `SaveHookSession` は Before と After のフックを `Detach` した状態で呼び、After の `WriteNotingContext` が書き込みが `true`（行に当たった）を返したときだけ、フックへ戻る前に取り外した観測を直接呼ぶ（`false` と例外では呼ばない）。
  - (c) `JournalingRepositoryBase.RecordAndWriteAsync` が業務の書き込みの間だけ観測し、`RecordHookBinaryWriteAsync` が同期対象の表の除外列なら、context の `ExecuteSqlAsync`＝**保存と同じトランザクション**で記録を入れる（`SyncJournal.RecordWithinSaveAsync`・Upsert・列の名前つき）。
- フックを観測の付いたまま呼ばない：フックがほかの DB（サーバー）のリポジトリで始めた保存にも観測が流れ、その保存のフックの書き込みで無関係な DB の context へ `INSERT INTO quicker_sync_journal` が走る（表が無ければその保存が失敗する）。Before も外す（Before で始めた保存の After が観測を拾うため）。
- 記録をジャーナル自身の接続で入れない：保存が失敗しても記録が残り、保存が持つ SQLite の書き込みロックとも競合する。そのためジャーナルの表は書き込みの前に作っておく（`EnsureCreatedAsync` を先に呼ぶ）。
- キーは記述子の `ISyncHookKeyFormatter`（internal・主キーの型ならそのまま、違えば値オブジェクトの `Wrap` か `ConvertRaw`）で記録のテキストにし、読めなければ `InvalidOperationException` で保存を失敗させる（黙って記録を落とさない）。
- `SyncSession` の抑止中（ダウンロードの適用）は観測しないので、そこで走るフックの書き込みは記録しない（docs に明記）。
- ガード：シナリオ `blob-after-hook-write-on-update-is-uploaded`・`blob-after-hook-write-on-another-row-is-uploaded`・`blob-after-hook-write-with-other-key-type-uses-row-key`・`blob-after-hook-write-rolled-back-with-failed-save`・`blob-after-hook-write-matching-no-row-is-not-recorded`、`SyncRuntimeTestsBase` の「ローカルのフックから始めた別の DB の保存でフックが書いた blob は、ローカルの記録にしない」（mutation 実証済み）。

## アップロード

### 送る操作の決め方

- ジャーナルを (テーブル, キー) ごとに畳み、送る操作は最新の記録でなく「いまのローカルの行の有無」と「その行の記録全体」で決める（`SyncEngine.PlanReplayAsync` / `Decide`）。
  - 行あり＋追加・更新を含む＝いまの内容を upsert／行あり＋削除だけ＝破棄／行なし＋削除を含みサーバーにあった版（削除記録の版すべてと ack の max）が分かる＝その版で削除／それ以外＝破棄。版なしテーブルは行なし＋削除を含めば無条件削除。
  - 理由：journal-first なので最新の記録は着地しなかった書き込みであり得る。最新だけで決めると「編集→外部キーで拒否された削除」で編集が、「削除→失敗した再挿入」でサーバーの削除が消える。
- 行の有無は internal `ISyncTableRunSupport.FindLocalKeyTextsAsync` でテーブルごとにまとめて引く。持たない `ISyncTable` は従来の最新記録で決める。
- 再生の中の読み直し（upsert で行が無ければ破棄・delete で行が残っていれば破棄）は同期中の書き込みへの二重化として残す。
  - ただし upsert で行が無く、計画した記録から削除になる行なら、破棄せずにその削除を送る（`PlanReplayAsync` が `TableReplayPlan.DeletesIfGone` に控え、`ReplayPassAsync` が送り直す）。計画の後にアプリが作り直した行をもう一度消すと、作り直しの記録は捨てられ新しい削除の記録は版を持たないので、サーバーに行が残るため。
  - 逆に delete で行が戻っていて、そのキーに実行が読んでいない記録（計画の後に書かれた作り直しの記録）があれば、記録を消さずに残す（`ReplayPassAsync` の `HasEntriesRecordedSinceAsync`）。残した削除の記録の版で、次の実行が作り直した行を版つきの更新として送る（消すと作り直しの記録だけが残り、挿入として送って偽の DuplicateOnServer が毎回出る）。新しい記録が無いとき（業務の削除が着地しなかった記録）は今までどおり消す（残すと、最新の記録で計画する外部の `ISyncTable` が毎回同じ削除を計画して行を見つけ続ける）。判定は表の種類に依らない。
- 削除再生は冒頭でローカル行を probe する（`LocalRowSurvivedDeleteAsync`＝行がまだ在れば業務削除は着地しなかったとして Discarded・版あり／LWW の両経路）。無いと、FK 違反などで失敗したローカル削除を次の同期がサーバーで完遂し、削除伝搬が生き残ったローカル行まで消す。
- ガード：シナリオ `replay-edit-then-refused-delete-still-uploads-the-edit`・`replay-delete-then-refused-reinsert-deletes-on-server`・`replay-offline-insert-then-delete-is-discarded`・`replay-versionless-delete-then-refused-reinsert-deletes-on-server`・`replay-unlanded-delete-entry-is-discarded`・`interrupt-BeforeServerWrite-delete-recreated`・`interrupt-BeforeServerWrite-recreate-deleted`・`replay-recreated-pending-delete-is-sent-as-update-external-table`・`replay-unlanded-delete-entry-is-discarded-external-table`・`d1-folder-recreate-delete`。

### 版ありの送り方

- max が null＝オフライン挿入としてサーバー存在確認後に `InsertAsync`（既にあれば `DuplicateOnServer`）。
- 非 null は `WriteRowVersion(local, max)` してから `UpdateAsync(entity, Optimistic)`。ack があればミラー版なしでも Update 経路（エコー未着のオフライン挿入が偽 `DuplicateOnServer` にならない）。ガードはエンティティから版を読むので書いてから送る。
- 削除は max(`entry.OriginalRowVersion`, ack) を載せた `RowState=Removed` のスタブを `SaveAsync` へ渡す（キーだけの `DeleteAsync` は無条件削除になるため使えない）。
- ミラー版の読み出し生成式（`BuildRowVersionRead`）は VO なら無条件 `?.Value`。列の NULL 許容宣言で分岐しない（マルチターゲット変換がローカル rowversion 列を NULL 許容へ落とす／削除記録はキーだけのスタブから読む、の両方で「NOT NULL 宣言でも実行時 null」が成り立つ）。ガード：`SyncVoRuntimeTests`。

### 削除の記録の版と作り直し

- グラフ保存の削除の記録は、版のある表なら記録の前にローカルの行のミラー版を読み、エンティティの版と大きいほうを付ける（`SyncGraphRecorder.RecordCollectedAsync`。記述子の `LocalRowVersionSql` を内部の `ISyncLocalRowVersionReader` 経由で `ExecuteScalarSqlAsync<byte[]>`。読むのはロックの中）。キーだけの `DeleteAsync(id)` は元から行を読んで版を付けている。
  - 理由：キーだけのスタブや、ダウンロード前から持っていた実体で消した削除は版を持たず、再生の判定で「サーバーに届いていない行」として黙って捨てられる。
- 同じキーで消して作り直した行は、upsert のエントリに削除の記録の版（`PendingRow.DeletedRowVersion`）を載せ、`UploadUpsertAsync` の original を max(ミラー版, 受理記録, エントリの版) にして Update 経路へ回す（新しい行はミラー版を持たず Insert 経路に倒れ、偽の DuplicateOnServer になるため）。
- サーバーに一度も届いていない「作る→消す→作る」は削除の記録の版が null なので Insert のまま。
- 再生が渡す upsert のエントリも版を持つ（`SyncJournalRow.OriginalRowVersion` の XmlDoc に書いてある）。
- ガード：シナリオ `27-key-only-stub-delete`・`27-held-object-delete`・`27-stub-delete-after-blob-write`・`n5-versionless-delete-refused-after-stub`・`rs6-offline-delete-reinsert`・`rs6-offline-insert-delete-insert-stays-insert`・`rs6-reinsert-after-server-update-conflicts`。

### 競合ポリシー

- `LocalWins` は 3 種の競合すべて（ModifiedOnServer・DuplicateOnServer・MissingOnServer）を解決する。実体は `SyncTableBase.ForceOverwriteOrInsertAsync`（private protected）で、LWW 再生と同じ本体を共有する＝「ローカルが勝つ」の意味が版あり／なしで割れない。MissingOnServer×LocalWins は `MarkAdded`＋Insert でサーバーが消した行を復活させる。
- `ServerWins` は落とす前にサーバー行を読み直して適用する。ジャーナルを落とすだけでは「サーバーが変えていない行」のローカル編集が残り、エントリも消えているため以後どのランも直さない。
  - 捨てる各キーを `Writer.GetByIdAsync` で読み、版プロパティをローカル行の現在値（ローカル行なしは null）へ差し替えてから `SyncTableBase.ApplyServerRowsAsync`（ダウンロードと同じ `ApplyBatchAsync`）で適用する。サーバーの版をミラーへ書くとアンカーの 3 点目が破れて恒久取りこぼしになる。
  - 適用はエントリ削除より前（逆順は「編集が残りエントリだけ消える」窓を作る）。順は apply→ack→drop。
  - FK 順は親→子。サーバーに行が無ければ何もせず削除伝搬へ委ねる（`PropagateDeletes=false` なら残る）。
  - 適用した行は ack へ記録する（`ApplyServerRowsAsync` が版差し替えの**前に**読んだサーバー版を `SyncAppliedServerRow` で返し、engine が非空版だけ `RecordAckAsync`）。記録しないと、復活行はサーバー版がアンカー以下でダウンロードが二度と来ず再編集が偽 `DuplicateOnServer`、適用後にダウンロードが落ちたケースは偽 `ModifiedOnServer` になる。
  - 副作用として同ランのダウンロードは適用行をエコーと判定し、`Downloaded` に数えない。
  - `ISyncTable.ApplyServerRowsAsync` の既定実装は空リストを返す（外部実装の互換）。
- ServerWins の適用の位置：`SyncAsync` では**そのテーブルのダウンロードの直後・親→子**。アップロード段で適用すると、まだ降りていない親を参照する行をローカルへ書いて外部キーで毎回止まる。打ち切ったテーブルも含めて必ず適用する（`DownloadCoreAsync` の `SettleServerWinsAsync`）。単独の `UploadAsync` はアップロード段で適用する。
- Versioned＋ServerWins が捨てる予定の id は、そのテーブルのダウンロードで未送信に数えない。ランの範囲の表の行だけに絞る（範囲外の表の未送信の削除は親の削除として数え続ける）。
- ServerWins の適用はロックの中でその表の記録を読み直し（`SyncJournal.ReadTableAsync`）、ロックの外で読んだ id を今のキーで組み直す（`PlanReplayAsync` と同じ形。実行が途中で捨てた id は読んだキーのまま）。読んだときに書き込みの途中だった、Before フックがキーを決める新しい行の記録は、決まったキーへ書き換わっている。古いキーのまま適用すると、その行を戻さずに記録を捨て、実在しないキーを `DiscardedByServerWins` として報告する。ガード：`SyncRuntimeTestsBase.InFlightInsertRekeyedByHook_IsNotDiscardedByTheUpload`（ServerWins の行。mutation 実証済み）。
- ServerWins の最中に別の記録を持ったキーは適用しない（ジャーナルを読み直し、ランの冒頭に読んだ id 以外の記録を持つキー）。
  - 適用しなかったキーは、ランの冒頭に読んだ記録も捨てない（`ApplyServerWinsToTableAsync` の `dropRows` から外す・`DiscardedByServerWins` にも載せない）。行の内容は実行前の編集（書いた blob・サーバーが持つ行の版つきの削除）を持ったままなので、記録だけ捨てると内容と記録が食い違ったまま残る（blob が永久に届かない・作り直した行が挿入として送られ毎回重複で断られる）。次の実行が新しい記録と一緒に送る。
- ガード：シナリオ `lw-overwrites-duplicate-key-on-server`・`lw-resurrects-row-missing-on-server`・`sw-leaves-row-edited-during-the-run`・`interrupt-*-update-blob-written-serverwins`・`interrupt-*-recreate-deleted-serverwins`・`interrupt-*-delete-recreated-serverwins`・`sw-does-not-stop-download-at-rows-it-drops`・`fk-serverwins-restores-row-moved-to-new-parent-on-server`、`SyncVoRuntimeTests`。

### 後勝ち（LWW）の再生

- 全テーブル一律「`Update(ForceOverwrite)`→行なしなら `Insert`・削除は無条件」（版の読取も実在確認もしない・`ConflictPolicy` は見ない）。
- 例外：後勝ちのランでも `ConflictPolicy=ServerWins` なら、サーバーが書き込みを断った 3 つの理由（ParentMissingOnServer・DeleteRefusedOnServer・UniqueValueTakenOnServer）に限って記録を捨ててサーバーの行で戻す（`UploadCoreAsync` の `SettleOrCollect`・その表のダウンロードの直後・親から順・`Discarded` に数える）。版なしの表のその状態の行を決着させる道はこれだけ。
- ガード：シナリオ `fk-lww-serverwins-settles-parent-missing-on-server`・`fk-lww-serverwins-settles-delete-refused-on-server`・`fk-lww-serverwins-uploads-ordinary-changes`・`n5-*`。

### アップロードの巡

- アップロードは巡に分けて送る（`UploadCoreAsync` の `RunRoundAsync`）。1 巡は「upsert（親から）→ 削除（子から）→ 後回しの upsert」の 3 段。
- 外部キーか一意制約で断られた書き込み（ParentMissingOnServer / DeleteRefusedOnServer / UniqueValueTakenOnServer＝`RefusedWrite`）を集め、その巡でどの表でも 1 件でも送れていれば、集めたものだけで次の巡を回す（巡の数は記録の数を超えない）。進展が無くなった残りだけを報告・決着させる。
  - 理由：表の中だけで巡を回すと、同じ表の後の行の更新が空ける一意な値・後回しの段の付け替えを待つ削除・一時的な失敗を次のランまで持ち越し、後勝ち＋ServerWins は利用者の変更を捨てる。決着を偽の競合に掛けて付け替えを捨てないため。
- 断られた書き込みは行の書き込みで止まり、受理記録も blob も残さない。
- ガード：シナリオ `n6-*`・`repro-d3-*`・`repro-d4-*`・`repro-d2-upsert-*`。

### サーバーが断った理由の確認

- サーバーへの書き込みが例外になったときだけ `ExplainServerRefusalAsync` が状態を確かめる（成功経路の呼び出しは増えない）。
  - upsert で親がサーバーに無ければ `SyncConflictReason.ParentMissingOnServer`。
  - delete で行がサーバーに残り、サーバーのどれかの行がその行を参照していれば `DeleteRefusedOnServer`。参照の確認は `IsReferencedOnServerAsync`（その行を主キーで参照する同期する表〔`ISyncTableRunSupport.ReferencingTableNames`〕のサーバーの行を `HasServerRowReferringToAsync`＝版ありは差分の流れを上限なしで・版なしはキー順のページ〔`ScanServerRowsAsync`・バッチは `DownloadBatchSize`〕で読む）。確かめられない参照（エンジンに無い表・`UnlistedReferencingTableNames`）は参照ありとする。参照する表があるだけで決めると、一時的な失敗が偽の競合になり後勝ち＋ServerWins が利用者の削除を捨てる。
  - ローカルの行でサーバーの一意性を確かめ、別の行が値を持っていれば `UniqueValueTakenOnServer`。
  - それ以外は元の例外を `ExceptionDispatchInfo` で投げ直す（通信の失敗を競合に化けさせない）。
- 確認自体の失敗（参照する行の読み取り・`ReadServerRowAsync`・親の確認・`CollidesOnServerAsync`・ローカルの読み取り）は、元の例外と確認の失敗をまとめた `AggregateException` を投げ、記録を残す。読み取りの失敗を参照ありに倒すと、一時的な失敗で後勝ち＋ServerWins が削除を捨てる。
- 受け止める範囲は `table.UploadAsync` の呼び出しだけ。ack の記録やジャーナルの片づけの失敗はローカルの失敗なのでそのまま投げる。
- LocalWins / LWW も同じ経路で報告し、サーバーが消した親や子を復活・削除しない。LocalWins も他者の一意な値を奪わない。
- ガード：シナリオ `repro-d2-*`・`reach-after-fault-delete-*`・`d2-delete-refused-by-referencing-row`・`d2-delete-check-fails-lww-serverwins`・`fk-write-of-row-whose-parent-is-gone-is-reported-as-parent-missing-*`・`fk-delete-refused-on-server-holds-back-child-*`・`fk-delete-refused-on-server-is-settled-by-serverwins`、`SyncKeptRowsTests`。

## 一意な値

### 削除→同じ値で挿入

- そのランで削除を送る表に限り、各 upsert をサーバーの `CheckUniquenessAsync`（ローカルの行で・`ISyncTableRunSupport.CollidesOnServerAsync`）に通し、ぶつかる upsert とそれを参照する upsert（同じ表・別の表・推移的）を削除の段の後へ回す（`FindUpsertsToDeferAsync`）。
- 回した upsert が待った削除が通らなかったときも `UniqueValueTakenOnServer` で報告する。
- ガード：シナリオ `24-unique-delete-then-insert`・`24-deferred-upsert-carries-children`。

### ダウンロードの前の削除伝搬とぶつかりの検出

- 削除伝搬をダウンロードの前にも走らせる（`PropagateDeletesAcrossAsync`・後の伝搬はキー集合を取り直す）。ServerWins のランが捨てる記録は未送信に数えない（ローカルでだけ作った行が同じ値のサーバーの行を邪魔しない）。
- ダウンロード前の削除伝搬が消した「捨てる記録の行」は、その行の記録をその場で捨てる（実行の初めに読んだ id だけ）。表のダウンロード後の決着まで残すと、その間に実行が例外で終わったとき記録が残り、次の実行が「行なし＋追加だけ」として何も残さず捨てる。
- それでもバッチの適用が失敗したら、そのときだけ行をローカルの `CheckUniquenessAsync` に通し、ぶつかる行で打ち切る（`ApplyBatchOrFindCollisionsAsync`＝流れの順の経路は最初の行で止めて手前を適用・版なしと ServerWins はぶつかる行を飛ばす。止めた行から後ろの受理記録は掃除しない）。削除伝搬を切ったときも同じく打ち切る。
- ServerWins の先消し：`SyncAsync` の ServerWins は削除伝搬が有効なとき、捨てるキーのうちサーバーに無いローカルの行をサーバーの行を戻す前に消す（`ApplyServerRowsAsync` の `removeLocalRowsMissingOnServer`・参照されていない行だけ）。単独の `UploadAsync` は後に削除伝搬が続かないので消さない。
- 一意な値を回した（入れ替えた）行は適用できず毎回打ち切るか断られる＝docs の既知の割り切り（洗い替えか、仮の値を書いた後に一度同期する）。
- ガード：シナリオ `n1-*`・`n2-*`・`n7-*`・`n1-serverwins-truncation-does-not-strand-children`・`n786-thrown-serverwins-removal-then-record-dropped`・`n15-pending-unique-cycle`。

### 一意な値の持ち主を新しくする

- ダウンロードは各行を最新の版でしか見ないので、一意な値を手放した後にもう一度変更された行は値を取った行より後ろに来て、まだ値を持つローカルの行が新しい行を拒む。
- `ApplyBatchOrFindCollisionsAsync` は失敗経路だけで `TryResolveCollisionAsync` を呼び、記述子の `LocalUniqueLookups`（一意制約ごとの値の読み出しと、その値を持つローカルの行のキーを返す SELECT＝`SyncUniqueLookup<TEntity>`）で持ち主を引き当て、`SyncParentResolver.BringHoldersUpToDateAsync` がサーバーから読み直して先に適用する（ミラー版は書かず受理記録・サーバーに無ければ参照されていない限り消す＝削除伝搬を切ったランでは消さない）。
- やり直しはバッチの行数まで。やり直すたびに追加か更新かをローカルの今の行から決め直す（`ApplyBatchCoreAsync` は `MarkUnchanged` してから `MarkUpdated`＝`MarkUpdated` は追加の印を残すので、失敗した試行で追加と決めた行をもう一度挿入してしまう）。
- 読み直さない条件：持ち主に未送信の変更がある（ServerWins が捨てる記録なら読み直す＝`SyncPendingSnapshot.KeyTextsByTable`）／読み直す途中で同じ行に戻る（入れ替え）／持ち主のサーバーの行がまだ同じ値を持つ（二重の守り）。
- 持ち主を参照する行を先に入れる：サーバーに無い持ち主がローカルの行に参照されて消せず、参照する行が同じバッチでサーバーが別の親へ移した行なら、`MoveOffHoldersAsync` がその行をすべてサーバーの行の内容とローカルの一意な値（記述子の `CopyUniqueValues`）で先に入れ、持ち主を消してからやり直す。消せなければ入れた行を元に戻す（`PutBackAsync`＝混ざった内容を上げない）。
  - null を返すときは、どの枝（入らない行・途中で待つ行がその形でないと判った）でも先に入れた行を戻してから返す。
  - 戻す書き込みが断られたら、戻せなかった行のキーを名指しした `InvalidOperationException` で実行を止める（戻せる行はすべて戻してから）。混ざった行を黙って残すと、後のアップロードが端末の変更として上げる。
  - `CopyUniqueValues` は null（未設定）を取り、未設定なら `MoveOffHoldersAsync` も `ReleaseUniqueValuesAsync` も行を入れない（何もしない既定のデリゲートだと、一意な値を写さないまま入れる）。生成の記述子は一意制約のある表で必ず設定する。
  - 一意な値の比べ方は `SameUniqueValues`（`StructuralComparisons.StructuralEqualityComparer`＝バイナリはバイトで比べる）の 1 つ。
  - ガードなし：戻す前に返す枝は、先に入れた行が同じバッチの行で、流れの順の経路はぶつかった行の手前を、まとめて適用する経路はぶつからない行をその後でサーバーの写しで書くので、どの決定的な形でも観測できない。戻す書き込みの失敗・手で組んだ記述子・バイナリの一意な列は、作る口が無いか図に無い。
- 持ち主を引き留める送るものの無い行を消す：サーバーに無い持ち主を今の手（持ち主を消す・`MoveOffHoldersAsync`）で消せなかったとき、`SyncParentResolver.RemoveRowsKeepingHoldersAsync` が、持ち主を（推移的に）参照するローカルの行を集める（同じ表も別の表も。子の表の行は失敗の経路でだけ表ごとに読み、記述子の親の参照で絞る＝`ISyncTableRunSupport.FindLocalRowsReferringAsync`。公開 API は変えない）。どれもが次を満たすときだけ、子から消してから持ち主を消す（`BringHoldersUpToDateAsync`）。
  - 送るものが無い（`HoldsUnsentChange` が偽＝未送信の変更が無いか、ランが捨てる記録しか無い）・読み直しの途中の行でない。
  - ランの範囲の固定クラスの表の行で、ほかの行（範囲の外の表・同期しない表・記述子に無い外部キー）に参照されていない（`IsReferencedOutsideAsync`）。循環が無い。
  - サーバーにあり、版が消した後の残りの行の導く再開点以下の行（`FindRowsNotBroughtBackAsync`＝ダウンロードがもう一度は降ろさない最新の写し）も消すが、消す前に降ろし直す約束を残す（「降ろし直す約束（quicker_sync_row_pending）」）。
  - 呼ぶのはランが削除を伝搬するときだけ（`TryResolveCollisionAsync` の門。持ち主を消す規則と同じ）。止める条件はどれも、消す前にすべて確かめる。
  - 理由：引き留める行のサーバーの行は値を待つ行の下にあり（端末が作った同じキーの行・サーバーが移した行の古い写し）、外部キーは文ごとに検査するので、サーバーの内容で先に入れられない（`MoveOffHoldersAsync` は移り先がローカルにあるときだけ効く）。消さないと、どのポリシーでも毎回止まり、洗い替えでしか抜けない（未送信の無い古い写しの形は、サーバーの操作だけで全端末が止まる）。
  - 順：ランが捨てる記録（解決役が受け取る `discarding`＝実行の初めに読んだ id）を先に捨て、降ろし直す約束を書き、それから行を消す（受理記録も消す）。逆順だと、途中で落ちたときに作り直した行の削除の記録が残り、次の実行がサーバーの行を消し得る。この順なら残るのは記録の無い行で、次の実行が同じ手で消す。
  - 消した行は `SyncLocalRowChangeReason.RemovedForUniqueValue` で載せ、捨てた記録の行は `DiscardedByServerWins` でも載せる。サーバーにある行はダウンロード（表が値を待つ行の表より後ろなら同じ実行）か、ServerWins の適用で入る。
  - ガード：シナリオ `holder-referenced-by-other-table-settles`・`holder-referenced-by-other-table-serverwins-settles-in-one-sync`・`holder-referenced-by-other-table-lww-serverwins-settles-in-one-sync`・`holder-referenced-by-other-table-collect-keeps-unsent-referrer`・`holder-referenced-by-other-table-propagation-off-keeps-referrer`・`holder-referenced-by-other-table-entries-go-before-rows`・`holder-referenced-by-other-table-items-settles`・`holder-referenced-by-other-table-through-label-settles`・`holder-referenced-by-other-table-stale-copy-settles`・`holder-referenced-by-other-table-edited-stale-copy-is-kept`・`holder-referenced-by-other-table-stale-copy-in-excluded-table-is-kept`、網羅の行（`SyncModelCoverageRows` の RemovedForUniqueValue の 3 行）、乱数の種（種 400・広い世界の種）。mutation 実証済み（段を外す・引き留める行の未送信の確認・呼び出し側の伝搬の門・範囲の条件・記録と行の順。最新の写しの約束の mutation は「降ろし直す約束（quicker_sync_row_pending）」）。
- 一意な値を手放す：持ち主のサーバーの行を入れられない理由が、新しい親が読み直しの途中の行（`SyncParentResolver.IsInProgress`）であることだけのとき、`ISyncTableRunSupport.ReleaseUniqueValuesAsync` がローカルの持ち主にサーバーの一意な値だけを書く（親とほかの内容・ミラー版は残す）。
- ガード：シナリオ `n8-unique-value-moved-then-updated`・`n8-holder-with-unsent-change-stops`・`n8-serverwins-holder-is-read-back`・`n8-chain`・`n8-true-swap-stops`・`n14-holder-moved-under-new-parent-in-batch`・`n13-holder-referenced-by-taker`・`serverwins-holder-moves-under-taker`・`serverwins-holder-chain-moves-under-taker`・`holder-held-for-another-reason-keeps-its-values`。

## ダウンロード

### 未送信の行との突き合わせ

- 未送信のキーは、固定クラスの表ではバッチごとにロックの中で読み直す。外部の `ISyncTable` には開始時点の写しを渡す（「同期の実行中の書き込み」）。
- ack はテーブルごとに `ReadAcksAsync` で読んで `SyncDownloadContext.AcknowledgedRowVersions` へ渡す。単独呼び出しでも ack は DB から読む。
- バッチ（版昇順）の行ごとに：
  - (1) pending なら打ち切り：その行を含め以降を適用しない。止まるのはそのテーブルだけで、後ろのテーブルは続ける（下の「打ち切りを後ろへ連動させない」）。打ち切りは `SyncResult.Truncations`（`SyncDownloadTruncation`＝PendingKeyText）で必ず露出する。公開済みの `TruncatedByTableName` は互換のため残し常に null。先頭が pending なら applied=0 の明示分岐で正常終了＝進捗ガードの網に入れない。
  - (2) ack 一致（キー一致 **かつ** 版 `SequenceEqual`）なら内容適用と blob コピーを飛ばし、版だけ Suppress 内でミラーへ書く（`Downloaded` に数えない）。キーだけの照合は不可（アップロード後に他者が触った行を取りこぼす）。ローカル行が消えていたら通常適用へ（アンカーが進まず進捗ガードが誤爆するため）。
  - (3) それ以外は ForceOverwrite 適用。
- 適用は到着順の連続ラン単位で流す（content をため、エコー行の版書き込みの前に flush＝どのコミット境界でも接頭辞性が保たれる）。
- 版なしテーブルは打ち切らずスキップして続行（アンカー非依存）。保留・そろえられない行を飛ばして走査を続け、走査の後で最初に飛ばした行で打ち切りを報告する。
- 親の未送信の削除：行の親が「未送信の削除キー」なら未送信の行と同じく打ち切る（`SyncPendingSnapshot.DeletedKeyTextsByTable`・判定は `HoldOf`）。この保留も `MissingParentTableName` に親の表を入れる。解決役の判定と二重の守り。
- engine の構成：private `DownloadCoreAsync` が打ち切りを運び public `DownloadAsync` は従来のタプルへ 1 行委譲。private `UploadCoreAsync` を public `UploadAsync` と `SyncAsync` が共有する。

### 進捗ガード

- バッチを適用したのに再導出したアンカー（版なしはキー）が動かなければ `InvalidOperationException`。アンカーを無視するソースと版列が空の行は同じ形になり、放置すると同じバッチを永久に適用し続ける。

### 打ち切りを後ろへ連動させない

- 打ち切りは後ろのテーブルへ連動させない。連動させると「子を別の親へ付け替え・元の親を削除・同じ一意な値で新しい親を作る」形で、古い子が元の親を参照して残り、新しい親がぶつかって親の表で打ち切り、子の表が見送られて古い子が直らない行き詰まりがランごとに繰り返す。
- 打ち切り点より後ろの親を参照する後ろのテーブルの行は、親の解決役がサーバーから読み直すか、読み直せなければその行のテーブルで止まるので、外部キーは守られる。
- ガード：シナリオ `n9-stale-child-keeps-holder`・`truncated-parent-read-back-across-tables`・`truncated-parent-read-back-selfref-chain`・`truncated-parent-held-stops-child-table`・`conflict-truncation-continues-following-tables-and-reads-parents`。

### Save フックが行を飛ばしたら止める

- エンジンはローカルのリポジトリの Save フックを走らせたまま適用する（飛ばしたかは保存の後に行が `Unchanged` にならないことで見る＝`SyncTableBase.FindSkippedRow`）。
- 飛ばした行の後ろを書かない：再開点はミラー版の最大値なので、後ろの行が入ると飛ばした行を越え、その行が二度と降りない。
- `ApplyBatchCoreAsync` は、表が受け取ったフックの登録簿（`SyncTable` / `VersionlessSyncTable` の省略可能な `localSaveHooks`・生成の `AddSyncTable` が `GetService<ISaveHookRegistry>()` を渡す）がその表のエンティティにフックを持つときだけ、行を 1 行ずつ別の保存で書き、飛ばした行で止めてその位置を返す（飛ばした行の blob はコピーせず、コピー待ちの記録は消す）。フックを持たない表は従来どおり 1 回の保存（1 バッチ＝1 コミットが速さの源）。
- 登録簿を受け取らずに組んだ表でフックが飛ばしたら例外にする（後ろの行は書かれた後で、止めても再開点は戻らない）。
- 止め方は経路ごとに 1 つ：版あり・版なしのダウンロードはその表を止めて `SyncDownloadTruncation.SkippedBySaveHook` を立てる（版なしは走査も止める＝`ScanByKeyAsync` の `onBatch` が false）。ServerWins・親・一意な値の持ち主の読み直しは、飛ばした行から後ろを保留にする（記録は残し受理記録も書かない・親を持てない子は `MissingParentTableName` で止まる）。protected の `ApplyBatchAsync` は飛ばしたら例外（洗い替えの読み直しの行もこれで書く）。
- ガード：シナリオ `hook-download-skip-stops-table-and-retries`・`hook-parent-read-back-skip-stops-child-table`・`hook-serverwins-read-back-skip-keeps-entry`・`hook-versionless-download-skip-stops-table`・`apply-skip-stops-table`（失敗の種類 `ApplySkip`・既存の種の列を変えないよう乱数の操作の列には混ぜない・モデルの端末の表は登録簿を受け取る）、`SyncRuntimeTestsBase` の登録簿なしの例外、`SyncLocalSaveHookRegistrationTests`（mutation 実証済み）。

### 降ろし直す約束（quicker_sync_row_pending）

- 何を：一意な値の持ち主を引き留める行を消すとき（`SyncParentResolver.RemoveRowsKeepingHoldersAsync`）、消す行のうち、サーバーにあり版が残る行の導く再開点以下の行（この端末で最新のままの写しで、同じ表に後ろの版の行が降りている）は、ダウンロードの流れではもう一度は降りない。その行の（表・キー）を、消す前にローカルの表 `quicker_sync_row_pending`（`SyncJournal.RecordOwedRowsAsync`）へ記録する。
  - 引き金の形：サーバーが子を持つ行を新しい親へ移し、元の親を消して、その一意な値を新しい親へ付ける（フォルダーの作り直し）。移した行の子が端末にもう降りていれば、引き留める古い写しを消すと子も消える。端末が同じキーの行を作った形（キーの重複）でも同じ。
- 順：ランが捨てる記録を消す → 約束を書く → 行を子から消す。
  - 約束の後・行の前に落ちる：行と約束が残り、戻す段が「同じキーの行がある」で約束を消す（次の実行の解決役が同じ手でまた消して記録する）。
  - 行の後・読み直しの前に落ちる：約束が残るので、次の実行が読み直す。
  - 逆の順（行の後に約束）は、間で落ちると最新の写しが端末から消えたまま戻らない。
- 戻す：版を持つ表の `DownloadAsync` の先頭（`CopyOwedBlobsAsync` の前・何かを取得する前）で、`SyncTableBase.BringOwedRowsBackAsync` がバッチと同じく書き込みロックの中で未送信を読み直し、解決役の `SyncParentResolver.BringOwedRowsBackAsync` に行ごとに決めさせる。
  - ローカルに同じキーの行がある：約束を消す（その後に書いた行の方が新しい。アプリが書いた行を上書きしない）。
  - そのキーに未送信の変更がある（ランが捨てない）：約束を残す。
  - それ以外：`ApplyServerRowsAsync` でサーバーの行を読んで入れる（親は解決役が先に読み直す）。入ったら受理記録を書いて約束を消す。サーバーに無ければ約束を消す（削除伝搬と同じ結果）。入らない（親がそろわない・一意な値を別のローカルの行が持つ）なら約束を残す。
  - 残った約束はダウンロードを止めない（打ち切りにしない）。後の実行が試し直す。
  - エンジンが動かさないダウンロード（解決役が無い直接の呼び出し）は何もせず、約束を残す。
- ミラー版と受理記録：読み直した行は、親を読み直した行と同じく内容だけを入れてミラー版を書かず（「アンカーは保存せず導出する」「ミラー版を書くのはダウンロードだけ」を崩さない）、サーバーの版を受理記録に書く（解決役の `RecordAckAsync`）。戻した行への後の編集は、この受理記録を original にして版で守られる（`ApplyServerRowsAsync` の remarks の「再開点以下の版で戻した行」）。
  - 同じ実行で古い写しがこの受理記録を上書きすることは無い：表のダウンロードのバッチはこの段の後に取得するので、読み直した版か、それより後ろの版を持つ（「ack の掃除」の解決役の受理記録の規則に当たらない）。
- 洗い替え：版を持つ表の洗い替えは、その表の約束を消す（行はほかの行と同じく洗い替えで入る）。
- キーのテキストの書き直し（`NormalizeKeyTextsAsync`）は、約束の表も書き直す。
- 表の作成：`SyncJournal.EnsureCreatedAsync` が 4 つの表をそれぞれ `CREATE TABLE IF NOT EXISTS` で作る。以前の版のローカルの DB（3 つの表だけ）でも、新しいインスタンスの最初の使用で作られる。
- ガード：シナリオ `holder-referenced-by-other-table-current-child-settles`・`holder-referenced-by-other-table-current-child-serverwins-settles-in-one-sync`・`holder-referenced-by-other-table-current-child-lww-serverwins-settles-in-one-sync`・`holder-referenced-by-other-table-current-child-collect-keeps-unsent-referrer`・`holder-referenced-by-other-table-current-child-localwins-keeps-unsent-referrer`・`holder-referenced-by-other-table-stale-copy-with-current-child-settles-collect`・`holder-referenced-by-other-table-stale-copy-with-current-child-settles-localwins`・`holder-referenced-by-other-table-stale-copy-with-current-child-settles-serverwins`・`holder-referenced-by-other-table-stale-copy-with-current-child-settles-lww-serverwins`・`holder-referenced-by-other-table-current-child-read-back-row-uploads-its-edit`・`holder-referenced-by-other-table-current-child-owed-row-recorded-before-removal`・`holder-referenced-by-other-table-current-child-owed-row-dropped-when-row-is-still-there`・`holder-referenced-by-other-table-current-child-owed-row-survives-failed-run`・`holder-referenced-by-other-table-current-child-owed-row-gone-on-server`・`holder-referenced-by-other-table-current-child-owed-row-waits-for-parent-with-unsent-delete`・`holder-referenced-by-other-table-current-child-owed-row-waits-for-unsent-entry`・`holder-referenced-by-other-table-current-child-owed-row-key-created-on-device`・`holder-referenced-by-other-table-current-child-owed-row-cleared-by-refresh`・`holder-referenced-by-other-table-current-child-legacy-local-schema`、乱数の種（広い世界の種 120195）。mutation 実証済み（約束を書かない・約束と行の順を逆にする・戻す段を外す・同じキーの行があっても約束を残す・サーバーに無い行の約束を残す・入らない行の約束を消す・未送信の確認を外す・洗い替えで消さない・古い DB に表を足さない・受理記録を書かない）。

## 親をそろえる解決役

- ダウンロード・ServerWins の読み直し・洗い替えで、適用する行の親（記述子の `ParentReferences`）がローカルに無いとき、内部クラス `SyncParentResolver`（バッチ・ServerWins の 1 表・洗い替え全体ごとに 1 つ＝同じ親を 2 回読まない）がサーバーから親を読み直して先に適用し、親の親も連鎖する（`ISyncTableRunSupport.ApplyServerRowsAsync` の resolver つきの内部の口）。
- 読み直した行はミラー版を書かず受理記録を残す（書くとアンカーが流れを追い越す・記録しないと後の編集が偽の DuplicateOnServer）。
- 読み直した後に同じ実行がその行へバッチの古い写し（読み直しより前に取得したもの）を内容として書いたら、その受理記録を書き込みの単位（ダウンロードの flush・洗い替えのバッチ）のコミットの直後に消す（解決役が読まなかったときと同じ＝古い内容・ミラー版・受理記録なし。次の実行が新しい版を内容ごと降ろす）。表のダウンロードの終わりまで積むと、同じ表の後のバッチの取得の失敗で捨てられて残る。残すと次の実行が新しい版を折り返しとみなして版だけを写し、端末は古い内容のまま残り、その後の編集はサーバーの新しい内容を競合なしに上書きする。流れの順の適用と接頭辞性には触らない（行を飛ばさない）。一意な値の持ち主の読み直しも同じ。ServerWins の適用は、並びにある親を読み直さず、並びにある持ち主は適用の後で並びの写しの版を受理記録へ書き直し、表のダウンロードの後に流すので、この形にならない。ガード：割り込みの網のサーバーの書き手の行 `interrupt-server-AfterServerFetchBeforeApply-parent-after-child-selfref-update-*`・`interrupt-server-AfterServerFetchBeforeApply-unique-holder-after-taker-update-*`（そのうち `*-fetch-fails*` は、上書きしたバッチの後の同じ表の次の取得が失敗する形。mutation 実証済み）。
- そろえない条件 3 つ：親の表がランの範囲外／親がローカルで未送信の削除で、そのランがその削除を捨てない（戻すと利用者の削除を黙って取り消す。Versioned＋ServerWins が捨てる削除の親は戻す）／サーバーにも親が無い。
- そろえられない行は、ダウンロードではその行で打ち切り `SyncDownloadTruncation.MissingParentTableName`（init のみ・既定 null・`SyncDownloadOutcome` にも同名）に親の表を入れる。ServerWins の読み直しでは、その行を戻さずに記録を残す。
- 流れの順に適用する経路（ダウンロード・洗い替え）は「同じ表の前の行」だけを親とみなし、後ろの行の親は読み直す（並べ替えるとコミット境界で接頭辞性が崩れる）。まとめて適用する経路（ServerWins・読み直し）は並びのどこにある親も可とし、`ParentsFirst` で親を先にしてから適用する。
- 洗い替えは同じ解決役を全体で 1 つ使い、先に読み直した行がそのバッチに来たら上書きする（`InsertRefreshedRowsAsync` の `again`）。上書きしたバッチのコミットの後に、その行の受理記録を消す（上の規則）。
  - 洗い替えの取得の後にサーバーの他者が親を消すと、バッチの古い写しの子が消えた親を指したまま入り、ローカルの外部キーで例外になる（洗い替えは失敗として止まり、次の洗い替えで取り直せる）。直していない（同期のモデルの既知の失敗の一覧に Pending で載せ、きっかけを待つ）。
- まとめて適用する経路でぶつかって外した行がローカルに無いとき、同じ表でその下にある行（推移的）も一緒に外す（`TryApplyBatchAsync` の `WithRowsBelowAbsentAsync`）。外した行がローカルにあれば下の行は適用する。子だけを戻すとローカルの外部キーで毎回止まる。
- ガード：シナリオ `17-serverwins-deleted-parent`・`parent-readback-collect-keeps-delete`・`parent-readback-collect-keeps-grandparent-delete`・`parent-readback-selfref-chain`・`parent-readback-refresh-across-batches`・`parent-readback-ack-guards-later-edit`・`13-propagation-pending-label-parent`・`18-serverwins-restore-subtree`・`20-*`・`21-*`・`n3-*`・`n4-*`・`c2-*`・`fk-child-of-excluded-parent-table-stops-and-names-parent-table`・`fk-versionless-download-reports-held-back-row-after-scan`・`repro-b3`・`repro-b3-grandchild`・`repro-b3-grandchild-under-present-child`。

## 削除伝搬

- キー全比較（サーバーの全キー集合とローカルの突合＝O(テーブル)・既定 ON・`SyncOptions.PropagateDeletes` で off）。
- ローカルのどの行からも参照されていない行だけを消す。記述子の `LocalReferences`＝図の全リレーションのうちこの表の主キーを列ペア 1 組で参照するもの（同期対象か・ランの範囲内かを問わず、自己参照を含む）ごとの `SyncLocalReference`（子の表の名前と `SELECT DISTINCT 子の列 FROM 子の表 WHERE 子の列 IN (@keys)`）。
  - 候補を `SyncInClause.ChunkSize`（500）ごとに照会し、消せる行が無くなるまで繰り返す（同じ表の親が次のランまで残らない）。
  - ローカル DB に無い子の表は、その表の伝搬の最初に `sqlite_master` を 1 回読んで照会から外す（同期対象の表だけでローカル DB を組む構成で "no such table" が止めないため）。
  - 外すと、同じ表の親子・未送信の子・打ち切りで古いまま残った子・範囲外の表の子のどれかがある限り、ローカルの外部キーで例外になり同期が止まり続ける。
  - 互いを循環して参照する候補は消えずに残る＝docs の既知の割り切り。
- 残す行の祖先も残す：子→親の順に、サーバーに無いのに残す行（未送信＋子から頼まれたキー）の親を辿る。同じテーブルは `SyncKeptRows.CloseAsync`（internal・テーブルに触れない純粋な閉包）で閉じ、他テーブルの親はエンジンが親テーブルの「残す集合」へ足す。失敗の受け止めでは直らない（CASCADE なら残した子ごと消える）。
- 1 テーブルの削除伝搬はローカルのキー→未送信キーの順に読む（journal-first なので後から読む未送信が取りこぼさない）。
- ガード：シナリオ `12-selfref-propagation`・`12-selfref-propagation-completes-in-one-run`・`13-propagation-*`・`15-propagation-*`・`prop-keeps-locally-referenced-rows-across-in-clause-chunks`・`prop-skips-referring-tables-missing-locally`・`dl-propagation-can-be-turned-off`・`fk-propagation-keeps-parents-of-rows-it-keeps`、`SyncKeptRowsTests`。

## 同期の実行中の書き込み

- ローカル書き込みは許す。ローカルの生 SQL 実行器ごとのロック 2 つ（`SyncLocalGate`＝internal・`ConditionalWeakTable<ISqlExecutor, …>`）を、ジャーナル記録デコレータ（`JournalingRepositoryBase.RecordAndWriteAsync`＝記録＋業務書き込みを 1 組で）とエンジンが共有する。DI の ISqlExecutor はシングルトンでスコープをまたぐので、ジャーナル単位にすると画面と同期のスコープで別物になる。
- エンジンが書き込みロックを取るのは「未送信を読んでから書く」区間だけ：
  - 1 テーブルのアップロードの判定（`PlanReplayAsync`）。判定はロックの中でその表の記録を読み直し（`SyncJournal.ReadTableAsync`）、ロックの外で読んだ id だけを今のキーで組み直す。読んだときのキーで組むと、書き込みの突き合わせが記録を行のキーへ移した後に古いキーで「行なし」と判定し、移した記録まで消す。
  - ダウンロードの 1 バッチ（`RunBatchSectionAsync`＝未送信キーの読み直し→打ち切り判定→適用。サーバー取得はロック外）。
  - 1 テーブルの削除伝搬（`ISyncTableRunSupport.PropagateDeletesAsync`）。
  - ServerWins の 1 テーブル。
  - `RefreshAsync` 全体。
- 再生の中の読み直しによる破棄はロックなしで安全（捨てるのは判定がロック内で見た記録だけ）。
- 取り方はコールバック形 `WriteExclusiveAsync(body)`：async メソッドの中で立てた AsyncLocal は呼び出し元へ戻らないため、印は本体の内側で立てて流す。再入は「同じゲート＋未解放のリース」のときだけ素通り（解放済みフラグ＝放置された fire-and-forget が印を使い回せない）。`SyncSession.Suppress` 中はロックも記録もしない。
- 実行用ロック（`RunExclusiveAsync`）は `SyncAsync` / `RefreshAsync` / `UploadAsync` / `DownloadAsync` の呼び出し全体で、重なりは後着が待つ。
- 実行用ロックの入口は、実行用リースか書き込みリースを持つ流れを即座に `InvalidOperationException` で断る（実行用ロックは再入しない）。
  - 書き込みリースだけを持つ流れ（ジャーナル記録の書き込みの Save フックから同期を呼ぶ形）は、別の同期と互いに待ち合いどの待ち時間にも届かない。重なりの有無で挙動を変えないため常に断る。
  - 実行用リースを持つ流れ（同期の実行が適用で呼ぶローカルの Save フックから同期を呼ぶ形）は、通すと 1 つめの実行の中で 2 つめの実行が始まり、`LocalRowChanges` の記録を差し替え、適用中のバッチの開いたトランザクションとロックを取り合って busy_timeout の後に「database is locked」で落ちる。エンジン自身は公開の口を中から呼ばない（中は `*CoreAsync`）ので、断っても正当な経路は無い。
- `DownloadAsync(context)` を外から呼んだときは渡されたキーで動きロックを取らない（`SyncDownloadContext` の internal な読み直し口が null）。
- 外部の `ISyncTable`（内部の面を持たない実装）には、ダウンロード開始時点の未送信キーを `SyncDownloadContext.PendingKeyTexts` に入れて渡す（写しであることを XmlDoc と docs に書いた）。
- ガード：シナリオ `conc-local-insert-during-download-is-not-deleted-by-propagation`・`conc-local-edit-during-download-is-not-overwritten-by-batch`・`n10-*`・`28-hook-assigns-key`・`26-external-table-overwrites-pending-edit`・`hook-skip-changes-nothing`・`e2-leftover-unset-key-entry`、`SyncRuntimeTestsBase` の「[Sync/並行]」「[Sync/記録]」の各テスト（`LocalWriteFromAnotherFlow_WaitsForTheBatch` は時間でなく順序で判定する＝編集する行を同期の前に読み、着地の時点でエンジンのバッチの Save フックの印が立っているかで見る）、`SyncSqliteRuntimeTests` の `SyncFromInsideSaveHook_IsRefusedAndRollsBack`（行 journaled-write・engine-apply。mutation 実証済み）。

## 外部キー

- 記述子に `ParentReferences`（`SyncParentReference<TEntity>`＝親テーブル名＋子の行から親キーのテキストを読む関数。対象は「列ペア 1 組・親の主キーを参照・親も同期対象」で自己参照を含む。一意制約参照・複数列は対象外＝docs の既知の制限。キーのテキストは子の外部キー列へ同じ `BuildFormatKey` を当てる）と `ReferencingTableNames`（図の全リレーションでこの表を参照する子テーブル・同期対象かを問わない）を生成する。
- エンジンがこれを使う 3 か所：削除伝搬の祖先の保持（「削除伝搬」）・ダウンロードの親の未送信削除での打ち切り（「ダウンロード」）・サーバーへの書き込みの失敗の理由（「サーバーが断った理由の確認」）。加えて親をそろえる解決役（「親をそろえる解決役」）。

## 無制限バイナリ列

### 行の転送とコピー

- `ExcludeUnboundedBinaryColumns` と併用可で、運ぶかは実行時引数 `SyncOptions.IncludeUnboundedBinary`（既定 false）が決める。記録は `IncludeUnboundedBinary` に依らず常時（何を送るかは送信側の判断で生成時には決まらない）。
- 行の転送は除外列を含まない：差分走査の SELECT は除外列があるとき `*` から明示列挙へ切り替える（生 SQL のエンティティ取得は SELECT に含まれた列を opportunistic にマップするので、`*` のままだと除外列まで降りてローカル UPDATE のガードで落ちる）。
- ON では行の転送後に列を 1 本ずつコピーする。面は `ISyncBinaryColumns<TKey>`（ローカルとサーバー直結＝固定型 `SyncRepositoryBinaryColumns<,>`・サーバー HTTP＝`HttpSyncServerSource<,>` 自身）で、エンジンはこの面しか見ない。`ISyncTable.UnboundedBinaryColumnNames` と `ISyncBinaryColumns.UnboundedBinaryColumnNames` は記述子上で同じ 1 つのプロパティ（エンジンが読む名前とコピーが回す名前がずれない）。
- コピーは一時ファイル経由（読みは押し出し・書きは引き出し、書き込みは長さを前もって要求する＝O(チャンク) を保って繋ぐのがファイル）。HTTP は既存のバイナリエンドポイントを再利用する。
- アップロード後はサーバー行を読み直してその版をランへ報告する（`ReadServerRowVersionAsync`）。blob の書き込みも版を進めるので、挿入・更新が返した版を報告すると ack 照合が外れ、同じランのダウンロードが自分の行を blob ごと取り戻す。
- アップロードは行の書き込みが返した版で先に ack を記録し（`RecordInterimAckAsync`）、エンジンが blob の後に読み直した版で上書きする（blob の送信失敗の後の偽の更新競合を防ぐ）。

### 送る列

- 送るのはこの端末が値を書いた列だけ：ジャーナル表の `unbounded_binary_columns`（プロパティ名のカンマ区切り・移行処理なし＝古い表のローカル DB は記録時に SQLite のエラー＝CHANGELOG の Breaking で表の削除を案内）に、Insert・BulkInsert・グラフ保存の Added ノードは全除外列、`Write{列}Async`（`RecordUnboundedBinaryWriteAsync`）はその列を記録し、Update は記録しない。再生は行の全記録の和集合だけをコピーする（`UnboundedBinaryColumnsOf`）。
- サーバーへの挿入になる経路はローカルの除外列を全部送る（`ColumnsToSend`。版ありの original が null の挿入・LocalWins の復活・`ForceOverwriteOrInsertAsync` の行なし→挿入〔戻り値で挿入かを返す〕）。記録の列だけだと、LocalWins の復活と ServerWins が挿入の記録を捨てた後に編集した行がサーバーで blob の空な行として作られる。
- 挿入したら blob を送る前に記録の列を全列へ広げる（`KeepInsertedBlobsOwedAsync` → `SyncJournal.WidenUnboundedBinaryColumnsAsync`）。blob の送信が失敗すると次のランは更新の経路になり、記録の列しか送らず blob が空のままになるため。
- 版のガードを外した上書き（LocalWins・後勝ち）で送らなかった除外列は、コピー待ちへ登録する（`OweUnsentBlobCopiesAsync`・受理記録は残す・列を運ばないランでも登録する）。上書きはサーバーの blob に触れず、降りてくる行は自分のエコーで版しか取らないため、他者の blob が恒久的に降りない。

### ダウンロードのコピーと取り直し

- 順は「コピー待ちの記録→行の書き込み→コピー→記録を消す」（`WriteRowsAndBlobsAsync`・ローカル表 `quicker_sync_blob_pending`・自分のローカル実行器から作った `SyncJournal` で同じ DB に読み書き）。
  - 行の書き込みが例外で終わったら、記録を消してから投げ直す（フックが行を飛ばしたときと同じ・最善努力）。残すと、バッチが断られて古い内容のまま残った行へ、次の実行がサーバーの blob を写す。ガード：シナリオ `n8-true-swap-stops`（入れ替えで断られたバッチの後にコピー待ちが空。mutation 実証済み）。
- 残りは列を運ぶ次のランのそのテーブルの最初に `CopyOwedBlobsAsync` がロック内で**行と列ごとに**決める：ローカルに行が無い→記録だけ捨てる／その列を書いた未送信の記録がある（`SyncPendingSnapshot.BinaryColumnsByKeyText`）→コピーせず記録だけ捨てる／それ以外→コピーして捨てる。キー単位で一律に飛ばさない（名前だけ編集した行の blob が二度と降りない）。
- 元の行がサーバーに無いコピー待ちは記録を捨てる（`CopyOwedBlobsCoreAsync` が行ごとに 1 回 `Writer.GetByIdAsync`。読み出しの「無い」と「NULL」は区別できず、NOT NULL の除外列へ NULL を書いて毎回止まるため。行は削除伝搬に任せる）。
- 洗い替えはテーブルの記録を捨てる。
- 記述子の `FillUnsetUnboundedBinaries`（NOT NULL の除外列だけ・VO は `Create([])`・非 VO は `[]`）を、ダウンロードの Added 行・洗い替えの行・アップロードで読んだローカル行（サーバーへの挿入用）へ当てる。
- ガード：シナリオ `blob-edit-without-local-blob-keeps-server-blob`・`blob-inserted-row-blob-is-recorded-and-uploaded`・`blob-failed-download-copy-is-retried`・`blob-failed-refresh-copy-is-retried`・`blob-owed-copy-keeps-unsent-local-blob`・`blob-owed-copy-made-for-ordinary-edit`・`blob-failed-upload-no-false-conflict`・`14-owed-blob-source-gone`・`22-forced-overwrite-hides-server-blob`・`23-localwins-resurrect-without-blob`・`23-insert-after-discarded-blob-record`・`23-inserted-row-blob-retry`・`n12-*`、`SyncVoRuntimeTests` の `NewRowWithExcludedNotNullBlob_IsDownloaded`（SyncVoFixture の NOT NULL の除外列 `photo`）、`SyncModelRuntimeTests.EmptyBlobWithoutConflict_IsStillAViolation`・`EmptyBlobOfRecreatedRowWithoutConflict_IsStillAViolation`。
- 運ぶかを端末ごとに一貫させる・行の単位で勝ち負けが決まる、は docs に書いた規則でコードは持たない。

## 洗い替え

- `RefreshAsync` は同期対象テーブルを全消し（子→親）→サーバー全行の流し込み（親→子）。
- 適用子は新設せず中立面だけで構成する（削除は `ISqlExecutor.ExecuteSqlAsync`・書き込みは `IRepositoryCore.BulkInsertAsync`）。カーソルは差分ソースの `GetChangesAsync` をバッチ末尾行の版で回すため追加エンドポイントなしで両経路が動く。
- クラッシュ安全は per-table / per-batch トランザクション（表どうしの循環のある範囲の全消しだけは 1 つのトランザクション＝下の項目）：生成 Repository は外部トランザクションに乗れないので単一トランザクション化は不可（ADO 型を Sync へ持ち込むことになる）。「削除は子→親・書き戻しは親→子・版の昇順」でどのコミット境界も有効な再開点になる。
- `BatchSize` の既定は 2000（差分同期の 500 と別＝1 バッチ＝1 コミットが速さの源）。
- `SyncRefreshOptions.Mode` の既定は `LastWriteWins`（全テーブル）。版なし子が版あり親を FK 参照する図では `Versioned` 既定だと親の全消しが FK に阻まれ構造的に失敗する。
- 拒否の順：引数（`BatchSize`）の検査の後、除外列があり `IncludeUnboundedBinary` も `DiscardLocalUnboundedBinaries` も立っていなければ `SyncUnboundedBinaryLossException`（ロックを取る前・記述子だけを見て DB を読まない）／範囲外の行が範囲内の行を参照していれば `SyncRefreshScopeException`（両方のロックの中・何も読み書きする前）／未送信ジャーナルが残っていれば `SyncPendingChangesException`（`Force` のときだけ破棄）。`Force` で抜けられない拒否（前の 2 つ）を先に出す。docs は範囲外の拒否を「何も読み書きする前」、ほかの 2 つを「何も消す前」と約束し、互いの順は約束しない。
- 範囲外の参照の判定は行で行う（表どうしの参照で断ると、子の行が無い Versioned の洗い替えまで断る）。
  - 範囲外は `Mode`（Versioned は版なしを外す）・`ExcludedEntityTypes`・構築時除外・エンジンが同期しない表。
  - 範囲内の各表について記述子の `LocalReferences` のうち範囲外の表からの参照を、その表のローカルの全キーで 500 件ずつ照会する（`ISyncTableRunSupport.FindReferencesFromOutsideAsync`）。ローカルに無い表は照会から外す。
  - `LocalReferences` に載らない外部キー（一意制約参照・複数列＝記述子の `UnlistedReferencingTableNames`）の表は、ローカルに在れば行を見ずに断る。外部の `ISyncTable` 実装は判定しない。
  - 拒否は両方のロックの中・`PrepareJournalAsync` より前。構築時除外の表のリポジトリはロックを取らないので、その表への同時の書き込みは防げない。
- 表どうしの外部キーの循環：1 表ずつではどの順でも最初の DELETE が断られる。範囲の表がすべて内部の面を持ち、同じローカルの実行器を使い、それが `ISqlBatchExecutor`（Core の公開の口・生成の SQLite の `SqlExecutor` だけが実装）で、範囲の表どうしに循環がある（`ReferencingTableNames` を範囲の表に絞る・自己参照は除く）ときだけ、`PRAGMA defer_foreign_keys = ON`＋子→親の DELETE を 1 つのトランザクションで流す（`SyncEngine.EmptyLocalTablesAsync`・`CycleBatchExecutor`）。失敗は口の実装が巻き戻し、どの表も元のまま残る。
  - 生の `BEGIN … COMMIT` を `ExecuteSqlAsync` に渡す形は採らない：失敗するとトランザクションが開いたまま接続がプールへ返り（Microsoft.Data.Sqlite は巻き戻さない）、次の書き込みがその中に入るか別の接続の書き込みが locked になる。外部キーの検査を切る形（`PRAGMA foreign_keys = OFF`）も採らない：接続文字列に `Foreign Keys` の指定の無い自前のファクトリでは、途中の失敗で検査の切れた接続がプールに残る。
  - 循環の判定は結果では見分けられない（木の図でも口の経路で消して成功の結果は同じ）。木の図を今の 1 表ずつの経路に保つのは変更の範囲を絞るための条件で、テストの無い分岐ではない（判定を外す壊し方はどの行も赤にしない＝表明していないのは意図どおり）。
  - 包んだ実行器・外部の `ISyncTable`・実行器の違う表が範囲にあるときは今の経路に戻り、循環は今までどおり断られる（docs の既知の割り切り）。行どうしの循環のデータは全消しでは消えるが、読み込みが 1 行ずつ戻せずに止まる（直さない・docs の既知の割り切り）。
- 受理記録は全部の表の全消しが成功してから消す（全消しの前に消すと、全消しの失敗で行とミラーが残ったまま受理記録だけが消え、再編集が偽の競合になる）。全消しの後・受理記録の削除の前に落ちる窓は、サーバーにも行が無いときだけ後の同じキーの作成が偽の MissingOnServer になり得る（受け入れてコメントに書いた）。
- ガード：シナリオ `refresh-refuses-when-rows-outside-refer-inside`・`refresh-excluding-referencing-child-refuses-under-cascade`・`refresh-skips-referring-tables-missing-locally`・`refresh-failed-wipe-keeps-acks`・`refresh-empties-tables-in-a-foreign-key-cycle`・`refresh-force-in-a-foreign-key-cycle-replaces-pending-changes`・`refresh-failed-wipe-in-a-foreign-key-cycle-deletes-nothing`・`42-refresh-refused-by-rows-outside`、`SyncRuntimeTestsBase` の「[Sync/洗い替え] 一覧に載らない外部キーで参照する範囲外の表は…」、`SyncConstructionExclusionTests.ExcludeFromSync_ReferringLocalRows_RefuseRefresh`、`SyncSupportGenerationTests`、`SyncRefreshBenchmarkRuntimeTests`（時間は表明せず出力し、表明は 3 経路の内容ダイジェスト一致のみ）（mutation 実証済み）。

## 版なしテーブルと後勝ちモード

- `SyncOptions.Mode`：`Versioned` 既定＝版ありだけを同期し版なしには一切触れない（記録は続く）／`LastWriteWins`＝全テーブル対象・ラン全体で意味論を 1 つに統一。
- 版なしダウンロードは毎回キー昇順の全量スキャン（`SELECT TOP … WHERE key > @afterKey ORDER BY key`）。版ありは後勝ちランでも増分ダウンロードを維持する。
- ceiling はスコープ内の版ありテーブルにだけ問う（全版なしなら null）。
- 固定基底はサブクラス階層：`SyncTableBase`（共有＝バッチ適用・削除伝搬・blob コピー・LWW 再生 `UploadLastWriteWinsAsync`・サーバー版の読み出し `ReadServerRowVersionAsync`）← `SyncTable`（版あり）／`VersionlessSyncTable`（版なし）。
- 除外は 2 層：ラン単位 `SyncOptions.ExcludedEntityTypes`（記録は継続・未知型は ArgumentException で黙殺しない）と、構築時 `AddGeneratedSyncEngine(…, excludeFromSync:)`（ローカル専用宣言＝記述子を登録しない＝全ラン対象外。その表の行はどのリポジトリから書いても記録しない。デコレータは自分の行を記録しない形で包み、直接の書き込みは素通し・グラフ保存は同期対象の子だけを記録する＝「デコレータ方式と journal-first」）。ローカル専用は版列なしテーブルへ、という旧イディオムでなく構築時除外が正。
- 未送信拒否・Force 破棄・blob 損失ガード・ServerWins の全破棄はいずれもランのスコープ内のみ集計する。
- ガード：シナリオ `lww-downloads-versionless-table-every-run`・`lww-cascade-save-with-versionless-child-uploads`、`SyncConstructionExclusionTests`（構築時除外・登録時 fail-fast）。

## キーのテキスト

- 日時の主キーは Kind を落とした往復書式：`BuildFormatKey` が `DateTime.SpecifyKind(…, Unspecified).ToString("o")`、`BuildParseKey` が `SyncKeyText.WithoutDateTimeKind` を通してから読む（公開の静的クラス `SyncKeyText`）。どちらの DB も Kind を持たず読み戻した行は Unspecified なので、Kind つきのテキストは同じ行のテキストと一致しない（オフラインの挿入が「行が無い」として捨てられる）。`DateTimeOffset` はオフセットが値の一部なのでそのまま。
- decimal の主キーは指数表記を使わない：整形・読み取り・書き直しは `SyncKeyText.FormatDecimal`（`0.############################`）/ `ParseDecimal`（`NumberStyles.Float`＝以前の形も読む）/ `WithoutDecimalExponent`（指数表記を含むテキストだけを書き直す）の 1 か所で、生成物の記述子はそれを呼ぶだけ（書式の写しを生成側に持たない）。`G29` は絶対値 1e-5 未満を `1E-06` と書き、`decimal.Parse`（`NumberStyles.Number`）が `FormatException` で毎回のアップロードを止める。
- 以前の版が書いた形の書き直し：記述子の `NormalizeKeyText`（日時・decimal の主キーの表だけが持つ＝生成器の `BuildNormalizeKeyText`）を、エンジンが各ランの初め（`PrepareJournalAsync`＝SyncAsync・UploadAsync・DownloadAsync・RefreshAsync）に `SyncJournal.NormalizeKeyTextsAsync` へ渡し、ジャーナル・受理記録・blob のコピー待ちのテキストを書き直す（同じキーになった受理記録は新しいほうの版を残す）。インスタンスごとに 1 回にしない（長く生きるジャーナルのインスタンスが以前の形を後から読み得る）。
  - 書き直しが同じランの記録まで直すので、整形の誤りは同期の結果から見えないことがある＝整形は記述子で直接確かめる。
- 版なしの表のキー順ページング：`@afterKey` は型なし（文字列は nvarchar）で束縛されるので、文字列の主キーは `BuildServerAfterKey` が列と同じ型へ CAST する（SQL Server の型マッパーが渡す `SqlDbTypeName` と `SqlDeclaredLength`）。varchar を nvarchar と比べると SQL_* 照合順序で ORDER BY と比較の規則が食い違い、ページの境目で行を読み飛ばす。
- 固定長の文字列の主キーは生成時に警告する（「生成の条件と診断」）。
- ガード：`SyncKeyTextRuntimeTests`（Kind ごとの整形・古い形の書き直しと読み取り・decimal の往復）・`CodegenCaseTests` の行 `probe-sync-key-text-decimal-key`（decimal の主キーの図を生成→実コンパイル→記述子を実行）・`SyncSupportGenerationTests`、シナリオ `25-datetime-key-utc`・`25-datetime-key-local`・`25-legacy-journal-key-text`・`25-legacy-ack-key-text`・`19-varchar-key-paging-skips-rows`（`SyncModelSqlServerRuntimeTests`）（mutation 実証済み）。

## 実行が変えた行の一覧

- `SyncResult.LocalRowChanges`（`SyncLocalRowChange`＝表・キーのテキスト・理由の record）。理由は `SyncLocalRowChangeReason` の 4 つ：`DiscardedByServerWins`（`ApplyServerWinsToTableAsync` が捨てた行・保留した行は載せない）／`ReloadedForUniqueValue`（`SyncParentResolver.BringHoldersUpToDateAsync` が読み直した・消した持ち主）／`RemovedBeforeDownload`（ServerWins の先消しと、ダウンロード前の削除伝搬が消した「捨てる記録の行」）／`RemovedForUniqueValue`（`SyncParentResolver.RemoveRowsKeepingHoldersAsync` が消した、持ち主を引き留める送るものの無い行）。(表, キー, 理由) ごとに 1 件・追加順。
- 集めるのは `SyncAsync` だけ（実行中の `_changeLog`＝実行用ロックで重ならない）。単独の `UploadAsync` / `DownloadAsync` の戻り値は不変。
- 例外で終わった `SyncAsync` は、それまでの一覧を例外の `Data[SyncEngine.LocalRowChangesDataKey]` に入れて投げ直す（例外の型は変えない）。
- ガード：シナリオ `n2-serverwins-unique-delete-then-insert`・`thrown-serverwins-run-hides-settlement`・`thrown-serverwins-run-hides-settled-insert`（mutation 実証済み）。

## 差分ソースと HTTP

- エンジンは `ISyncServerSource<TEntity, TKey>`（アンカー超の行を `SyncChangeBatch<TEntity>`〔行＋`HasMore`〕で返す・全キー列挙・上限取得・アップロード先 `Writer`・版なし用のキー順ページング `GetFirstPageAsync`/`GetPageAfterAsync`〔既定実装は NotSupported＝版ありソース互換〕）に対してだけ動く。
- 直結実装は既存の生 SQL 面と既存 Repository だけで組む（新しい ADO コードを書かない）。
- `Writer` の型はリモート面 `IRemoteRepositoryCore<,>`（再生が使うのはネットワークを越えられる操作だけ＝直結リポジトリと HTTP クライアントが同じ位置に立てる）。
- 継続の判定はソースが返す `HasMore`（行数とバッチサイズの比較を呼び出し側で行わない）。
- HTTP 経路（`GenerateSyncSupport && GenerateRemoteServices`）：サーバー側は既存のエンドポイントグループへ同期専用の 4 本（`SyncCeiling` / `SyncChanges` / `SyncKeys`＋版なしテーブルにのみマップする `SyncPage`・操作名の正本は `RemoteSyncOperations`）。クライアントは `HttpSyncServerSource<,>`（`HttpRemoteRepositoryCore` 派生なので `Writer` は自分自身＝固定ランタイム内の派生はシムを通らず Core 直付け）。
  - エンドポイントは差分ソースの薄い remoting：ハンドラは DI から `ISyncServerSource<,>` を解決して呼ぶだけで意味論の実装は 1 つ。アップロードは既存の CRUD／保存エンドポイントを通る。ステートレス（再開点は毎リクエストの anchor、上限は ceiling として送られる）。
  - 上限は呼び出し側の値：`SyncCeiling` が返した値を同じパスで送り返す限りにおいてのみ導出アンカーの保証が成立する（大きな ceiling を送ると、その下で実行中のトランザクションの行を恒久的に読み飛ばす）。
  - バッチサイズは 0 以下を 400 で拒む（上限は設けない＝取り放題を止めるのはグループの認可）。
- ガード：`SyncHttpRuntimeTests`（アンカーの Base64 往復・400 拒否・サーバー不在）・`SyncSqlServerHttpRuntimeTests`（上限の値の HTTP の往復・版の書き戻しと 409 の競合）。

## 生成と DI

- DI は「ローカル半分」と「サーバー半分」に分かれ、転送経路の選択は 1 行の差：`AddGeneratedSyncEngine(localKey)`＋`AddGeneratedDirectSyncSources(serverKey)` または `AddGeneratedHttpSyncSources(baseAddress ｜ HttpClient ファクトリ)`（`AddGeneratedSyncSupport` は全直結構成のための合成）。キー引数の `null` は非 keyed 登録。
- per-entity の DI は型引数付きヘルパー `AddSyncTable` / `AddDirectSource` / `AddHttpSource` の 1 行／エンティティ。生成される `AddSyncTable` は `GetService<ISaveHookRegistry>()` をローカルの表へ渡す（「Save フックが行を飛ばしたら止める」）。
- デコレータの差し替えは keyed / 非 keyed とも元の登録の作り方を保つ（`InnerFactory`）：factory 登録はその factory／型の登録（非 keyed の生成の登録＝`AddScoped<I契約, 実装>()`）は登録時に 1 回作る `ActivatorUtilities.CreateFactory`／インスタンスの登録はそのインスタンスで内側を作る。factory 登録だけを受けると `AddGeneratedSyncEngine(null)` が必ず投げる。
- 元の登録がシングルトン（インスタンスの登録を含む）なら、デコレータはスコープで登録し、内側はすべてのスコープで 1 つを共有する（内側は専用のキーのシングルトンとして登録し直し、コンテナがルートから 1 回だけ作って元どおりに持つ。インスタンスならそのインスタンス）。デコレータはスコープの `SyncJournal` に記録するので、シングルトンのままだとスコープを検査するプロバイダがルートからの解決を断り、検査しないプロバイダはすべてのスコープで 1 つのジャーナルを掴む。ガード：構成の行列の因子 `LocalKey` の水準 `UnkeyedInstance`（ローカルの同期する表のリポジトリをインスタンスの登録にした行。行列は `ValidateScopes` つきのプロバイダで解決する。mutation 実証済み）。
- ガード：`SyncConfigMatrixRuntimeTests`（非 keyed の行を含む同期の構成の行列）・`SyncLocalSaveHookRegistrationTests`・`SyncConstructionExclusionTests`。

## ガードテストの一覧

- 決定的なシナリオは同期のモデル（`SyncModelRuntimeTests`＝直結・`SyncModelHttpRuntimeTests`＝HTTP・`SyncModelSqlServerRuntimeTests`＝実 SQL Server・`SyncSupportRemoteAdoSqlServerRuntimeTests`＝HTTP × 実 SQL Server、SQL Server だけの既知の失敗は `SyncModelSqlServerRuntimeTests.SqlServerOnly`）が流す。転送の軸の目印は `ISyncModelTransportTests`。行の分割は `SyncModelRuntimeTests.ScenarioParts_CoverEveryScenarioOncePerTransport`、割り込みのシナリオの全積は `SyncModelRuntimeTests.InterruptScenarios_CoverEveryPointAndShape`、乱数の種の照合の正規化は `SyncModelRuntimeTests.Family`。仕組みは `tests/QuickER.Tests.Integration/GeneratedRuntime/CLAUDE.md`「双方向同期のテスト」。
- エンジンの外の仕組み（並行のロックと割り込み・記録の文の形・全書き込み入口の記録・登録簿なしの例外・別の DB の保存のフック・NULL 許容の計算列・サーバーで NULL になった blob・知らない除外の型・一覧に載らない参照）は `SyncRuntimeTestsBase` と転送別の派生 `SyncSqliteRuntimeTests`（直結・サーバー役も SQLite）・`SyncHttpRuntimeTests`（in-process Kestrel）。
- 実 SQL Server の固有は `SyncSqlServerRuntimeTests`（NULL を許す一意制約・洗い替えの内容の一致の計測）と `SyncSqlServerHttpRuntimeTests`。
- 値オブジェクト×同期は `SyncVoRuntimeTests`（オフライン新規行のアップロード・キーだけのスタブのグラフ削除記録・版書き戻し）。
- 構成の行列は `SyncConfigMatrixRuntimeTests`、構築時除外は `SyncConstructionExclusionTests`、洗い替えの計測は `SyncRefreshBenchmarkRuntimeTests`、閉包は `SyncKeptRowsTests`、キーのテキストは `SyncKeyTextRuntimeTests`・`CodegenCaseTests` の行 `probe-sync-key-text-decimal-key`、生成は `SyncSupportGenerationTests`・`CodegenCaseTests` の行 `diag-sync-fixed-length-key-*`（上の 6 行）、フックの登録簿は `SyncLocalSaveHookRegistrationTests`。
