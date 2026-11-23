# QuickER.Runtime.Sync

双方向同期支援（`GenerateSyncSupport`）の実装の骨格と、壊すと静かに回帰する判断を置く。
固定のエンジン（このパッケージ）と生成器の側（対象テーブル・診断・記述子・DI 登録の生成）の両方を扱う。
利用者向けの意味論（競合ポリシー 3 択・削除伝搬・洗い替えの使い分け・blob の既定・後勝ちの取引・既知の割り切り）の正本は `docs/code-generation.md`（.ja）の「双方向同期の支援」節で、ここでは再述しない。
同期のテストのエンジン（同期のモデル・構成の行列・割り込みの点・網羅の行・シナリオの置き場と Theory の分割・サーバー役）の仕組みは `tests/QuickER.Tests.Integration/GeneratedRuntime/CLAUDE.md`「双方向同期のテスト」にある。

## 正本と生成物の置き場

- `Runtime.Sync.g.cs` は `src/QuickER.CodeGen.CSharp/Templates/CSharpRuntime/_10_Sync.scriban` から再生成するパッケージ用ソースで、手編集しない。
- 固定のエンジンはこのパッケージ（Core への ProjectReference のみ・PackageReference ゼロ）。分割生成では `Runtime.Sync.g.cs`。
  - 公開の型：同期のエンジン `SyncEngine`（`SyncAsync`・`UploadAsync`・`DownloadAsync`・`RefreshAsync`・`GetPendingChangesAsync`・`DiscardPendingChangesAsync` の 2 つ・`DiscardAllPendingChangesAsync`・定数 `LocalRowChangesDataKey`）・`SyncTable`（抽象の基底・外のアセンブリから派生できない）・`SyncTable<TEntity, TKey>`・記述子 `SyncTableDescriptor<,>` と `ISyncTableDescriptor`・差分ソース `ISyncServerSource<,>` と `DirectSyncSource<,>` / `VersionlessDirectSyncSource<,>` / `HttpSyncServerSource<,>`（リモートサービス併用時）・記録の部品 `SyncLocalTracking` / `SyncLocalTrackingGuard`・結果と例外の型。
  - 状態の表の読み書きは internal の `SyncState` の 1 か所にある（エンジンの本体と表の側に SQL の写しを置かない）。未送信の行は `SyncUnsentRow`、行の状態は `SyncRowState`、適用の計画は `SyncApplyPlan`、捨てる行は `SyncDiscard`、取得の道は `SyncDownloadRoute`（どれも internal）。
- 表ごとのコード（記述子・直結／HTTP 差分ソース・DI 登録）は生成側の `Repositories.Sync.g.cs`。
- 固定型は記述子を受け取る形にしてある：表ごとの生成物は `SyncTableDescriptor<TEntity,TKey>`（テーブル名・ルート名・SQL・キー／版の読み書き・バイナリ列・親の参照）をデータとして持ち、振る舞いは sealed の `SyncTable<,>` と差分ソースの固定クラスにある。
- ローカルの変更はローカルの SQLite の中のトリガーが記録する（「ローカルの変更の記録（SyncLocalTracking）」）。どの入口（Repository・生 SQL）の書き込みも同じに記録される。

## 生成の条件と診断

- bool オプション `GenerateSyncSupport`（既定 false。CLI `--generate-sync-support`／GUI「双方向同期の支援コードを生成する」＝QuickER 版 Repository を生成し、対象 DB が両方言のときだけ表示する）。
- 対象は Repository 契約を持つ全テーブル（単一主キー）。rowversion の有無は対象でなくモードを決める（版あり＝増分＋競合検出／版なし＝後勝ち・全量スキャン）。
- 生成時エラーは 3 つ：実効方言が sqlserver+sqlite のちょうど 2 つでない／`GenerateRepositories` が false／同期できる表が 0 件（`CodeGen_Error_SyncSupportRequiresEligibleTables`）。ガード：`CodegenCaseTests` の行 `diag-sync-requires-both-dialects-sqlite`・`diag-sync-requires-both-dialects-sqlserver`・`diag-sync-requires-repository-implementations`・`diag-sync-requires-an-eligible-table`。
- 対象一覧は Info 診断 `CodeGen_Info_SyncSupportTables` で FK 順のまま通知する（ガード：`CodegenCaseTests` の行 `diag-sync-target-tables-info-in-foreign-key-order`）。
- **名前がスキーマ修飾の表（`SchemaQualifiedName.TrySplit` が真）は同期の対象から外し、Warning `CodeGen_Warning_SyncSchemaQualifiedTables` で名指しする**。SQLite ではその表が別の DB の表になり、`main` の状態の表へ書くトリガーを付けられないため。参照元（`LocalReferences`・`UnlistedReferencingTableNames`）にも数えない。同期できる表が 1 つも残らなければ、上の「0 件」の生成時エラー。
  - 判定の正本は `CSharpGenerationModelBuilder.IsSchemaQualifiedTable`（同期できる表の判定もこれを通す）。
  - 外さないと、トリガーを張れない表を記述子に持つエンジンが生成され、実行時に初めて止まる。
  - ガード：`CodegenCaseTests` の行 `diag-sync-schema-qualified-table-excluded-with-warning`・`diag-sync-only-schema-qualified-tables-is-error`・`diag-sync-schema-qualified-table-without-sync-support-no-warning`。
- 主キーの列に NULL 許容の印がある表も対象にする。C# 生成は主キーの列を NOT NULL とみなすので、記述子の `ReadKey` はほかの表と同じく NULL を許さないキーの型を読む（正本は `src/QuickER.CodeGen.CSharp/CLAUDE.md`「主キーの列の NULL 許容」。ガード：`CodegenCaseTests` の行 `compile-nullable-primary-key-sync` / `compile-nullable-primary-key-sync-string-value-objects`）。
- 同期対象の表の主キーが固定長の文字列（nchar / char）なら `CodeGen_Warning_SyncFixedLengthStringPrimaryKeys` で「表.列（型）」を名指しし、可変長の型を勧める。
  - 理由：SQL Server は値を宣言長まで空白で詰めて返すので、ローカルで作った行のキーが同期の後に空白詰めのキーへ置き換わり、詰めていないキーを参照する子の行があると 2 行のまま収束しない（キーの序数照合では直せない）。
  - 判定は型名の文字列でなく正規型の種類（`FixedString` / `AnsiFixedString`）。`CanonicalTypeTokenAttacher` が `CSharpTypeInfo.IsFixedLengthString` を立てる。正本は `CSharpCodeGenerationService.BuildSyncFixedLengthKeyLines`。
  - ガード：`CodegenCaseTests` の行 `diag-sync-fixed-length-key-warning-nchar` / `diag-sync-fixed-length-key-warning-char` / `diag-sync-fixed-length-key-variable-nvarchar-no-warning` / `diag-sync-fixed-length-key-variable-varchar-no-warning` / `diag-sync-fixed-length-key-without-sync-support-no-warning` / `diag-sync-fixed-length-non-key-column-no-warning`（mutation 実証済み）。
- サーバー側の追加スキーマはゼロ。ローカルには `SyncLocalTracking` が状態の表 3 つ（`quicker_sync_state`・`quicker_sync_row`・`quicker_sync_table`）とトリガーを作る（「ローカルの変更の記録（SyncLocalTracking）」）。
  - エンジンが端末の DB に作る物（表・一時表・トリガー）の名前は `quicker_sync_` で始める（スキーマ同期の SQLite の取込がこの前置きで補助の表を外す。前置きの正本は `SyncAuxiliaryNames`・`src/QuickER.Dialects/CLAUDE.md`「双方向同期の補助の物の名前（SyncAuxiliaryNames）」）。テンプレートの名前は `SourceRuleTests` の規則 `sync-auxiliary-names-in-template`（表・索引）と `sync-auxiliary-trigger-names-in-template`（トリガー）が固定する。読めた `CREATE` の数の下限は規則ごとに持つので、種類ごとに規則を分けてある（合算すると、トリガーを読めなくなっても表の数で満たしてしまう）。
- 生成の固定：`SyncSupportGenerationTests`（経路の文字列・差分 SQL の方言ごとのクォート・混在図の版なしの表の生成形・除外列のコピー面）と `CodegenCaseTests` の行 `code-sync-on-emits-engine-and-per-entity-code`・`code-sync-off-emits-no-sync-types`・`exec-sync-descriptors-carry-parent-references-and-referencing-tables`・`exec-sync-parent-references-self-reference-and-unkeyable-skipped`。

## FK 順

- テーブルは図から求めたトポロジカル順（親→子）。送信の挿入・更新は親から、削除は子から送る。
- 循環参照で行き詰まったら、循環に属するテーブルを宣言順で 1 つだけ先に出して続ける（循環の外の子は親の後ろに保たれる）。
- ガード：`CodegenCaseTests` の行 `code-sync-tables-registered-in-foreign-key-order`・`code-sync-cycle-keeps-outside-child-after-parent`。

## ローカルの変更の記録（SyncLocalTracking）

- ローカルの SQLite の DB の中に、同期する表の変更を記録する表とトリガーを作る部品。同期の固定のエンジンにあり、`System.Data.Common` だけで書く（ADO のドライバを参照しない）。DI の登録（`AddGeneratedSyncEngine`）が、開くたびの確認でこの部品を置く（「生成と DI」）。
- 状態の表は 3 つ（`SyncAuxiliaryNames` の前置きで始まる）
  - `quicker_sync_state`：1 行。書式の版 `format`・DB ごとの印 `db_id`・適用中の印 `applying`・次の変更の連番 `next_seq`。
  - `quicker_sync_row`：行ごと。トリガーが書く列は `table_name`・型なしの `"key"`（元の表のキーの格納値をそのまま持つ）・`dirty`・`deleted`・`blob_columns`・`change_seq`。エンジンだけが書く列は `base_version`（BLOB・NULL 可）・`known`（既定 0）・`sw_seq`（NULL 可）・`blob_owed`（既定 空文字）・`applied_version`（BLOB・NULL 可。「古い読みの書き込みの見分け」）。
  - `quicker_sync_table`：同期する表ごとに 1 行。`table_name` が主キー、`anchor`（BLOB・NULL 可）が表ごとの再開点。定数名は `SyncLocalTracking.AnchorTableName`。
- トリガーは表ごとに挿入（`_ai`）・更新（`_au`）・削除（`_ad`）と、除外した無制限バイナリ列ごとの `_b{n}`（その列の `UPDATE OF`）。名前は `quicker_sync_trg_{表}_…`。
  - どのトリガーも適用中の印が 0 のときだけ記録する。エンジンが書く行（サーバーの行の適用）を、ローカルの変更として記録しないため。
  - `ON CONFLICT` の更新は名指しした列だけを書く。エンジンだけが書く 5 列は名指ししないので、行の更新・削除・同じキーでの入れ直し・主キーの変更（古いキーの行）・無制限バイナリ列の書き込みをまたいで値を保つ。上書きすると、サーバーが知っている行を知らない行として送り直し、基準の版や戻せなかった決定の印が消える。
  - 挿入は、除外した無制限バイナリ列の名前を全部付けて記録する（同じキーで消して入れ直した行も）。
  - 主キーを変える更新は、更新後のキーの行と、古いキーの行（削除）の両方を記録する。
  - 無制限バイナリ列の更新は、その列の名前を重複なく `blob_columns` へ足す。記録のたびに連番が進む。
- `blob_columns` に入れる名前は、アクセサの名前（記述子の `UnboundedBinaryColumnNames`＝プロパティの名前）。トリガーの `UPDATE OF` に使うのは DB の列名（`LocalUnboundedBinaryColumnNames`）。2 つは同じ並び・同じ数で、数が違えば `TriggersFor` が断る。
- 記述子の `Local*` の 3 メンバー（`LocalTableName`・`LocalKeyColumnName`・`LocalUnboundedBinaryColumnNames`）は引用していない生の名前で、トリガーの SQL が自分でクォートする。同期する表は `main` にある（スキーマ修飾の表は生成時に外す＝「生成の条件と診断」）。
- `Ensure` は 1 つの `BEGIN IMMEDIATE` の中で、状態の表を無ければ作り、表ごとに今のトリガー（その表に付いた、名前が前置きで始まるもの）を期待と比べ、違えば落として張り直す。DB に無い表は飛ばす。一覧に無い表のトリガーには触らない。
  - **張る前に、キーの列と除外した無制限バイナリ列が表にあることを確かめる**（`pragma_table_info` で `main` の表の列を読む）。無ければ何も変えずに、表・無い列・戻し方を名指しした例外にする。SQLite の `CREATE TRIGGER` は列を検査しないので、確かめずに張ると、張るときは成功し、その表への書き込みがすべて `no such column` で失敗する。
  - 状態がそろっているかの読み取りだけの判定（`HasExpectedState`）は、3 つの表・状態の表の 1 行・各表の期待のトリガーを見る。
  - 状態の表の列は足さない（列の足りない表を持つ DB の振る舞いは「既知の限界」）。
- 期待の定義のハッシュ（開くたびの確認の鍵）は、状態の表 3 つの `CREATE` の文・初期の 1 行の文・期待のトリガーの文から作る。状態の表の形が変わると鍵が変わり、覚えた結果が古い形のまま使われない。
- 記録しないもの：`REPLACE`（`INSERT OR REPLACE`・`UPDATE OR REPLACE`）が一意制約で消す別の行の削除。`recursive_triggers` が切のとき、SQLite が削除のトリガーを発火させない。
- トリガーの記録では、保存が DB から消した行だけが削除として記録に残る（グラフ保存の、書かれなかった新しい子の DELETE はどの行にも当たらず、トリガーが発火しない）。
- ガード：`SyncSupportAdoSqliteTriggerRuntimeTests` が行の表 `SyncTriggerRecordingRows`（影響の地図の名前 `table:SyncTriggerRecording`）を流す。主な行は `int-key-insert`・`primary-key-change-int`・エンジンの列を保つ行 `engine-columns-kept-by-update`・`engine-columns-kept-by-delete-then-reinsert`・`engine-columns-kept-on-old-key-by-primary-key-change`・`engine-columns-kept-by-blob-write`・`stream-write-names-blob-column`・`delete-then-reinsert-without-blob-write-names-all-blob-columns`・`applying-within-transaction-is-not-recorded`・`cascade-delete-and-set-null`・`graph-save-removed-parent-with-new-children-records-only-deleted-rows`・`graph-save-through-untracked-parent-records-only-tracked-child`。張る前の列の確かめは `table:SyncTriggerGuard`（行 `key-column-renamed-refuses-open-until-renamed-back`）。生成の行は `CodegenCaseTests` の行 `exec-sync-quoted-names-local-tracking-records-on-sqlite`（引用符を含む名前でも記録する）。名前の前置きは規則 `sync-auxiliary-trigger-names-in-template`。mutation 実証済み（適用中の印の条件を外す・連番を進めない・主キーを変える更新の古いキーを削除として記録しない・挿入し直しで無制限バイナリ列の名前を付け直さない・張る前に列を確かめない・トリガーの `ON CONFLICT` がエンジンの列を上書きする）。

## 記録させずに書く（SyncLocalTracking.WriteUntrackedAsync）

- `SyncLocalTracking.WriteUntrackedAsync(connection, write, cancellationToken)` は、開いた接続（トランザクションの外）を受け取り、その接続の 1 つのトランザクションの中で、適用中の印を立てる → `write(connection, ct)` → 印を戻す → コミット、を流す。トリガーは印が立っている間は記録しないので、`write` が書いた行は未送信にならず、前からある記録（未送信の行・エンジンの列・変更の連番）も変わらない。
  - 最初の文は `BEGIN`（ロックを取らず待たない）で、次の「印を立てる `UPDATE`」が書き込みのロックを取る（ほかの書き手を接続の `busy_timeout` まで待つ）。`BEGIN` が失敗するのは接続がすでにトランザクションの中のときだけなので、入れ子と利用者のトランザクションの中の呼び出しを、ドライバの例外の文を読まずに `InvalidOperationException` で名指しできる（同期の固定の側は ADO のドライバの型を参照しない）。このとき利用者のトランザクションには触らない。
  - 印はコミットの前に戻すので、ほかの接続から印が 1 に見えることは無い（ほかの接続の読み取りは待たずにコミットの前の内容を読み、書き込みはコミットまで待ってから通り、記録される）。
  - 入口は、印が 0 でないことを理由に断らない。書き込みのロックを持った後に見える印の 1 は、コミットされて残ったものだけなので、立てて戻せば直る。
- **どの出口でもトランザクションを終わらせる**：`write` の例外・取り消し・印を立てる文の失敗（ロック待ちの時間切れ）・コミットの失敗のどれでも、`ROLLBACK` を試みてから元の例外を投げ直す（`ROLLBACK` の失敗は元の例外を隠さない）。印を戻す文・`COMMIT`・`ROLLBACK` は `CancellationToken.None` で流す。
  - 理由：Microsoft.Data.Sqlite は、SQL の `BEGIN` で始めたトランザクションを、接続を閉じても巻き戻さない。プールの接続は、トランザクションと書き込みのロックを持ったままプールへ戻り、ほかの接続の書き込みが `database is locked` で失敗し、次にその接続を借りた処理はそのトランザクションの中で動く。
  - 残る限界：`ROLLBACK` 自体が失敗したプールの接続と、`write` の中で閉じられたプールの接続は、トランザクションとロックを持ったままプールへ戻る。`write` が接続を閉じたら `InvalidOperationException` で知らせる。その接続を次に開くのが開くたびの確認で包んだ接続ファクトリなら、確認が巻き戻す（「開くたびの確認（SyncLocalTrackingGuard）」）。プールしない接続（`ILocalRowStore.CreateDedicatedConnection`）は、閉じればロックを手放す。
- `write` の中の制約（XmlDoc に書いてある）：渡された接続だけで書く（SQL か `ILocalRowStore`）。生成の Repository・`ISqlExecutor` は自分の接続を開くので、ロック待ちで失敗する。トランザクションを始めない・終わらせない（セーブポイントは可）・接続を閉じない・この API と同期を呼ばない。外部キーの検査は渡した接続の設定のまま。
- `DbTransaction`（`connection.BeginTransaction()`）の形にしない：中のコマンドがどれもトランザクションを持つ必要があり、接続だけを受け取る `ILocalRowStore` と、`write` の中の素の SQL が動かない。
- ガード：`SyncEngineFixedRows`（影響の地図の名前 `table:SyncEngineFixed`）の行 `untracked-write-is-not-recorded-and-keeps-earlier-records`・`untracked-write-that-throws-leaves-nothing-and-no-transaction`・`untracked-write-cancelled-leaves-nothing-and-no-transaction`・`untracked-write-holds-off-other-writers-until-commit`・`untracked-write-through-repository-fails-on-lock-wait`・`untracked-write-refuses-connection-in-transaction-and-nested-call`・`untracked-write-refuses-closed-connection-and-missing-tracking`・`untracked-write-clears-applying-flag-left-set`・`untracked-write-closed-pooled-connection-is-rolled-back-on-next-open`。生成の行は `CodegenCaseTests` の行 `exec-sync-quoted-names-local-tracking-records-on-sqlite`（生成し直したコードの API が、書いた行を記録せず、印を 0 に戻す）。同期のモデルの端末は、記録しない書き込みをこの API で流す（`tests/QuickER.Tests.Integration/GeneratedRuntime/CLAUDE.md`「判定役の継ぎ目・台帳・実行ごとの照合」）。mutation 実証済み（失敗の出口で巻き戻さない・巻き戻しを呼び出し側の取り消しのトークンで流す・印を立てない・印を戻さずにコミットする・トランザクションの中の接続を名指しで断らない・関数の中で閉じた接続を確かめない・状態の表の行を確かめない）。

## 開くたびの確認（SyncLocalTrackingGuard）

- 接続が開いたときに、トリガーがそろっているかを確かめ、違えば張り直す部品。`ObservedSqlConnectionFactory(inner, new SyncLocalTrackingGuard(tables).OnStateChange)` の形で組む（`ObservedSqlConnectionFactory` は SQLite の固定の側の汎用の包みで、同期を知らない。→ `src/QuickER.Runtime.Sqlite/CLAUDE.md`「接続ファクトリを包む型（ObservedSqlConnectionFactory）」）。DI の登録（`AddGeneratedSyncEngine`）がこの形でローカルの接続ファクトリを包む（「生成と DI」）。
- 3 段で確かめる
  - 速い道：1 文で `schema_version` と `db_id` を読み、覚えた組と同じなら終わる。
  - 2 段目：読み取りのトランザクションで、`schema_version` を最初に読んでから比べる（読んだ版と比べた状態を同じスナップショットにする）。比べるのは、状態の表 3 つ（再開点の表 `quicker_sync_table` を含む）・状態の表の 1 行・各表の期待のトリガー。書き込みのロックを取らないので、読み取り専用の接続も、そろっていれば通る。
  - 3 段目：`Ensure` と同じ張り直し。コミットの前に組を読んで覚える。
- 覚える場所は型の静的な表で、鍵は開いた後の `DataSource` と期待の定義のハッシュ。`DataSource` が空（メモリの DB）は覚えず、開くたびに 2 段目へ入る。
  - 鍵に `db_id` の組を使う理由：`schema_version` だけだと、同じパスの DB のファイルが別の物に差し替わっても同じ値になりうる。
  - 鍵に定義のハッシュを含める理由：同じ DB に、同期する表の集合が違う 2 つのガードが向きうる。
  - インスタンスでなく静的に覚える理由：包む接続ファクトリが scoped・transient で登録されると、インスタンスごとに確かめ直すことになる。
- 失敗したら、**接続を閉じてから**例外を投げ、覚えた値は進めない（`StateChange` のハンドラが投げるだけだと、例外が出たのに接続が開いたまま残る）。`DbException` は `InvalidOperationException` で包む。
- 残った適用中の印を戻す：確認がそろったと答えた後（3 段のどれでも）、状態の表の印が 0 でなければ `UPDATE … SET applying = 0 WHERE applying <> 0` で戻す。
  - エンジンと `WriteUntrackedAsync` は印を 1 つのトランザクションの中で立てて戻し、開いているトランザクションの印はほかの接続からは見えない（戻さないし、書き込みのロックも待たない）。開いたばかりの接続から印が 1 に見えるのは 2 つの場合だけ。
    - 印を戻さずにコミットされた（`write` が自分でコミットした・生成の外の何かが立てた）。
    - プールの接続が、そのトランザクションの途中で閉じられてプールへ戻っていた（`write` が接続を閉じた・`ROLLBACK` が失敗した）。印はその接続の残ったトランザクションの中にあり、トランザクションは書き込みのロックを持ったまま。
  - 印を見たら、先に `ROLLBACK` を試み（残ったトランザクションが無ければ失敗して何も変わらない）、読み直してまだ 1 のときだけ戻す文を流す。`ROLLBACK` を挟まないと、残ったトランザクションの中で印を 0 に書くだけになり、トランザクションとロックが残り続ける。
  - 残っていた間の書き込みは記録されておらず、取り戻せない。戻した後の書き込みは記録される。
  - 印は速い道の 1 文で一緒に読む（文の数は増えない）。戻す文は書き込みのロックが要るので、張り直しと同じ形で失敗する（接続を閉じてから `InvalidOperationException`。読み取り専用の接続も同じ）。
  - 戻さないと、端末のどの書き込みも記録されないまま、同期が何も送らずに成功し続ける。
  - ガード：行の表 `SyncTriggerGuardRows` の行 `applying-flag-left-set-is-cleared-on-fast-path-open`・`applying-flag-left-set-is-cleared-after-full-check`・`applying-flag-left-set-is-cleared-after-repair`・`applying-flag-left-set-lock-timeout-closes-connection-and-is-cleared-after-rollback`・`applying-flag-left-set-refuses-read-only-open`・`applying-flag-inside-open-transaction-does-not-block-open` と、`SyncEngineFixedRows` の行 `untracked-write-closed-pooled-connection-is-rolled-back-on-next-open`（プールに残ったトランザクションの巻き戻し）。mutation 実証済み（印を戻さない・速い道でしか戻さない・戻す文を開くたびに流す・プールに残ったトランザクションを巻き戻さない）。
- 診断の値（`FullCheckCount`・`RepairCount`）は internal で、公開の面に出さない。
- 限界
  - 確認が走るのは、包んだ接続ファクトリが返した接続だけ。ほかの道で開いた接続（自前の `ISqlExecutor` が開く接続・生成の外の接続）では走らない（トリガーが張ってあれば記録はされる）。
  - 表を作り直した後、次に包んだ接続を開くまでの間の外の書き込みは記録されない。
  - 同じ DB から分かれた 2 つの写しが、同じ `schema_version` まで進んで入れ替わると、プロセスを起動し直すまで見つけない。
- ガード：`SyncSupportAdoSqliteTriggerRuntimeTests` が行の表 `SyncTriggerGuardRows`（影響の地図の名前 `table:SyncTriggerGuard`）を流す。主な行は `nothing-happens-hundred-opens-stay-on-fast-path`・`one-trigger-dropped-is-repaired`・`database-file-replaced-is-found-and-put-in-place`・`database-file-replaced-by-database-with-state-tables-is-found-by-database-id`（置き換えを DB ごとの印だけで見分ける形）・`anchor-table-dropped-is-recreated`（再開点の表だけ落とすと 2 段目で足りないと答えて作り直す）・`guards-with-different-table-sets-keep-each-other`・`lock-timeout-closes-connection-and-recovers-after-rollback`・`read-only-connection-opens-when-in-place`・`in-memory-database-is-checked-every-open`・`key-column-renamed-refuses-open-until-renamed-back`。mutation 実証済み（速い道が DB ごとの印を見ない・失敗のときに接続を閉じない・読むだけの確かめが比べない・覚える鍵に定義のハッシュを入れない）。

## 生成と DI

- DI は「ローカル半分」と「サーバー半分」に分かれ、転送経路の選択は 1 行の差：`AddGeneratedSyncEngine(localKey, excludeFromSync:)`＋`AddGeneratedDirectSyncSources(serverKey)` または `AddGeneratedHttpSyncSources(baseAddress ｜ HttpClient ファクトリ)`（`AddGeneratedSyncSupport` は全直結構成のための合成）。キー引数の `null` は非 keyed 登録。
- **`AddGeneratedSyncEngine` がエンジンを組む**：
  - 除外していない記述子で `SyncLocalTrackingGuard` を 1 つ作る。
  - ローカルの `ISqlConnectionFactory` の最後の登録（そのキーで）を、開くたびの確認つきの `ObservedSqlConnectionFactory` で包む登録に置き換える（`ObserveLocalConnections`）。内側は元の登録の作り方のまま作る（`LocalConnectionFactoryOf`＝factory の登録はその factory・インスタンスの登録はそのインスタンス・型の登録は登録時に 1 回作る `ActivatorUtilities.CreateFactory`）。寿命も元の登録のまま。ローカルの接続ファクトリの登録が無ければ、その場で名指しの `InvalidOperationException`。
  - スコープの `SyncEngine` を、包んだ接続ファクトリの `SqliteLocalRowStore` と、外部キーの順の `SyncTable<,>`（記述子と DI から引く差分ソース）で組む。差分ソースの登録を忘れると、エンジンの解決が無い差分ソースで失敗する。
  - **`AddGeneratedSyncEngine` の後でローカルの接続ファクトリを登録し直した構成は、エンジンを組むときに名指しの `InvalidOperationException`**（`ObservedLocalConnectionFactory`）。登録し直した接続ファクトリの接続は確認を通らず、ローカルの変更が記録されないまま同期が成功し続けるため。
  - 構築時の除外（`excludeFromSync`）はローカル専用の宣言：その表はエンジンの表に入らず（どの実行も降ろさず・削除を伝搬せず・洗い替えで置き換えない）、トリガーを張らないので行は記録されない。前の登録が張ったトリガーは外さない（記録は続くが送られない）。同期しない型の名前と、全部の表を除外する指定は、登録時に `ArgumentException` で断る。ラン単位の除外は `SyncOptions.ExcludedEntityTypes` で別物。
- keyed の Repository の登録（`AddGenerated{方言}Repositories` の keyed 版）の `SqlExecutor` と Repository は、接続ファクトリを DI から引く（`GetRequiredKeyedService<ISqlConnectionFactory>(key)`）。包んだ接続ファクトリが keyed の Repository の書き込みにも効くための前提（`_06` の側の事実）。ガード：`CodegenCaseTests` の行 `exec-keyed-repositories-use-connection-factory-registered-later`。
- 表ごとの DI は型引数付きヘルパー（`AddDirectSource` / `AddHttpSource` の 1 行／エンティティ）。
- ガード：`table:SyncTriggerGuard`（行 `di-unkeyed-registration-records-repository-writes`・`di-keyed-registration-records-repository-writes`・`di-instance-registered-connection-factory-is-wrapped`・`di-connection-factory-registered-after-sync-engine-is-refused`・`di-no-local-connection-factory-is-refused`・`di-excluded-table-gets-no-trigger-and-is-not-recorded`・`di-excluded-table-rows-referring-to-scope-refuse-refresh`・`di-excluding-everything-is-refused`・`di-unknown-exclusion-is-refused`・`di-tables-created-on-first-connection-are-recorded-from-next-connection`・`di-sync-through-registered-engine-uploads-and-downloads`）と、構成の行列（`SyncConfigMatrixRuntimeTests`・影響の地図の名前 `SyncModel/Config`＝非 keyed と keyed・インスタンスの登録を含む登録の形と除外の組み合わせ）。

## 段の順と適用

- 1 回の同期の段は、送信 → 取得 → 適用 → blob の写しの順。取得の上限は 1 回の取得ごとに 1 つ取り、範囲の表どうしで共有する（表ごとに取ると、前の表の上限の後に書かれた親が外れたまま、後ろの表の上限の下の子だけが降りる）。
- 適用は、外部キーの検査を切った実行の接続（「実行の接続」）の 1 つの `BEGIN IMMEDIATE` のトランザクションで、適用中の印を立てて書く（トリガーが自分の書き込みを記録しない）。中では、全部の表の削除伝搬を書き込みの前に行い、行を書き、外部キーを確かめ、表ごとの再開点を保存する（全部か無しか）。
  - 削除伝搬を書き込みの前に置かないと、消える行が持つ一意な値を書き込みが取れず、同じ適用で収束しない。
  - 一意な値の重なりは、取得した集合の中で後回しと回転（削除して入れ直す）で解く。集合の外の行をキーで読みに行く仕組みは持たない。
  - 外部キーの違反は、試行を巻き戻してから、適用の前からある違反（巻き戻した後の DB で読み直す）を除き、書いた子を保留にするか消した親を残す（主キーでない列への外部キーは、その列の値で親を引き直す）。どれにも当たらない違反が残れば説明できない違反として `InvalidOperationException`。試行の数には上限がある。
  - 保留した行（外部キーと一意な値で保留した行）がある取得は打ち切りを報告し（`SyncDownloadTruncation`・親が無いときは `MissingParentTableName`）、打ち切りがあれば、上限を取り直して再開点からもう 1 回だけ取得・適用する（2 回目は捨てる記録を渡さない）。2 回目の打ち切りは結果に残る。
  - 再開点は保留した最初の行の手前までにする。保留の行を越えて進めると、その行が二度と降りない。
- 適用はローカルの Save フックを呼ばない（`ILocalRowStore` で書く）。
- 再開点は表ごとに状態の表（`quicker_sync_table`）に持ち、ローカルの行の版の列からは導かない。洗い替えも同期も再開点を書く。
- 未送信の行で表の取得を打ち切らない。未送信の行はサーバーの行で上書きせずにローカルの内容を残す（ローカルの行が無く、サーバーがそのキーを知らないときだけサーバーの行が入る）。サーバーが知らないキーで端末が作った行は、その行の手前で表の再開点を止める（越えると、ローカルの行が消えた後にサーバーの行が二度と降りない）。
- 差分 SQL の `@anchor` / `@ceiling` は NULL を取り得るため `(@anchor IS NULL OR ...)` で「制約なし」を明示し、比較相手は `CAST(@anchor AS binary(8))` へ明示キャストする（生 SQL のパラメータは型なしで、NULL が nvarchar と推論され `Implicit conversion from data type nvarchar to timestamp is not allowed.` で落ちる）。上限はサーバーの `MIN_ACTIVE_ROWVERSION()`（未コミットのトランザクションをまたいだ読み飛ばしを防ぐ）。
- ガード：決定的なシナリオ（`SyncModelRuntimeTests.Scenario_FailsExactlyAsListed`。もう 1 回の取得は行 `fk-parent-created-between-table-fetches-arrives-in-same-sync`）・乱数の種（`SyncSupportAdoSqliteEngineRuntimeTests.RandomSeeds_ConvergeWithoutLoss`）・`SyncEngineFixedRows`（行 `refresh-and-sync-leave-nothing-to-fetch-again`・`unsent-insert-then-delete-of-unknown-key-takes-server-row-on-download`・適用が Save フックを呼ばないことは `download-does-not-run-local-save-hooks`＝行を飛ばすフックを登録しても降りた行が入り、フックの呼ばれた数が増えない）。

## 削除伝搬

- キー全比較（サーバーの全キー集合とローカルの突合＝O(テーブル)・既定 ON・`SyncOptions.PropagateDeletes` で off）。サーバーの全キーは、削除伝搬を使う実行だけが取る。
- 適用のトランザクションの中で、全部の表について書き込みの前に流す。未送信の変更のある行と、外部キーの違反で残すと決めた親は消さない（消すとローカルの外部キーの違反になる子が残る形は、試行を巻き戻して親を残す＝「段の順と適用」）。
- ServerWins が捨てる行のうちサーバーに行の無いものは、変更だけを失い、行は削除伝搬に任せる（削除伝搬を切った実行では行が残る）。
- ガード：シナリオ `12-selfref-propagation`・`12-selfref-propagation-completes-in-one-run`・`prop-keeps-locally-referenced-rows-across-in-clause-chunks`・`dl-propagation-can-be-turned-off`・`fk-propagation-keeps-parents-of-rows-it-keeps`。

## 送信

- 送り方：版で守る更新・後勝ちの上書き・LocalWins の強制・Versioned＋ServerWins は何も送らずに範囲の未送信を捨てる。
  - 版で守る削除は、キーと行の状態の基準の版（`base_version`）を載せた `RowState=Removed` のスタブを `SaveAsync` へ渡す（キーだけの `DeleteAsync` は無条件削除になるため使わない）。サーバーが一度も知らなかった行（`known` が 0）の削除は送らずに捨てる。
  - 後勝ちの実行と版なしの表は、`Update(ForceOverwrite)`→行なしなら `Insert`・削除は無条件（`ConflictPolicy` は見ない）。
  - `LocalWins` は版の競合を強制の上書きで解く（サーバーが断った書き込みはこの形で解かない）。
- 送る順は、挿入・更新を親から（同じ表の中は変更の順）、削除を子から。
- 巡に分けて送り、外部キーか一意制約で断られた書き込みは、その巡でどれか 1 件でも送れていれば次の巡へ回す。進展が無くなった残りを報告・決着させる（`UploadCoreAsync`）。
  - 理由：表の中だけで回すと、同じ表の後の行の更新が空ける一意な値・後回しの段の付け替えを待つ削除・一時的な失敗を次のランまで持ち越し、後勝ち＋ServerWins は利用者の変更を捨てる。
- サーバーが断った理由は、書き込みが例外になったときだけサーバーの状態で確かめる（`ExplainRefusalAsync`。成功経路の呼び出しは増えない）。
  - 挿入・更新で親がサーバーに無ければ `SyncConflictReason.ParentMissingOnServer`。ローカルの行でサーバーの一意性を確かめ、別の行が値を持っていれば `UniqueValueTakenOnServer`。
  - 削除で行がサーバーに残り、エンジンのどれかの表のサーバーの行がその行を参照していれば `DeleteRefusedOnServer`。
  - どれにも当たらなければ元の例外を `ExceptionDispatchInfo` で投げ直す（通信の失敗を競合に化けさせない）。確かめる処理自体の失敗は、元の例外と確かめの失敗をまとめた `AggregateException`（取り消しは除く）。
  - 後勝ち＋ServerWins の実行は、断られた残りを報告せずに捨てて、サーバーの行で戻す（版なしの表のその状態の行を決着させる道はこれだけ）。
- 決着は、書き手が受け入れた行に書き戻した版を基準の版にする。サーバーを読み直すのは、書き手が版を書き戻さなかったときと、無制限バイナリ列を送った後（列の書き込みも版を進める）だけ。書き込みの後に読み直した版は、後から書いたほかの書き手の版でありうるので、基準にすると、その書き手の内容を競合なしに上書きする。
- 行の変わる印（連番）が送る前に読んだものと同じときだけ未送信を外し、ローカルの行の版の列を基準の版に合わせる（適用中の印を立てて書く。行の内容と除外した無制限バイナリ列には触らない）。送る間にアプリが書いた変更は未送信のまま残る。
- ServerWins が戻した行の版の列は、戻したサーバーの行の版になる。
- ServerWins の実行が戻せなかった行の決定は、行の状態に印（`sw_seq`）を残して後の実行へ持ち越す（印の後に編集が無ければ、後の実行がその行を送らずにサーバーの行で戻す）。
- 持ち越した決定は、後の書き込み（無制限バイナリ列だけの書き込みを含む）が行の連番を進めれば行ごと外れ、次の同期は行を列と一緒に送る（サーバーが知らない行なら挿入として全列を送る）。
- 版の列の読み出しの生成式（`BuildRowVersionRead`）は値オブジェクトなら無条件 `?.Value`。列の NULL 許容宣言で分岐しない（マルチターゲット変換がローカル rowversion 列を NULL 許容へ落とす／削除はキーだけのスタブから読む、の両方で「NOT NULL 宣言でも実行時 null」が成り立つ）。ガード：`table:SyncEngineFixed`（行 `sync-vo-local-insert-without-version-uploads`）。
- ガード：決定的なシナリオと乱数の種（実行ごとの照合 `5/carry:*`・`5/stale-overwrite`・`5/local-discard`）・シナリオ `lw-overwrites-duplicate-key-on-server`・`lw-resurrects-row-missing-on-server`・`fk-lww-serverwins-settles-parent-missing-on-server`・`fk-lww-serverwins-settles-delete-refused-on-server`・`fk-lww-serverwins-uploads-ordinary-changes`・`n6-*`・`repro-d2-*`・`d2-delete-refused-by-referencing-row`・`blob-only-edit-after-serverwins-discarded-unknown-insert-uploads-row-and-blob`・`SyncEngineFixedRows`（行 `failure-after-server-write-before-settle-is-reported-until-local-wins`・戻した行の版の列は `sync-vo-serverwins-restores-row-with-server-version`）。

## 古い読みの書き込みの見分け

- 何を：同期が他者の内容を降ろす前に読んだ実体を、降ろした後に SQLite の Repository で保存すると、端末の行の版の列は古い版へ戻る（SQLite の Repository の UPDATE は版を照合せず、版の列を普通の列として書く）。版で守る送信がこれを見分けて、他者の内容を競合なしに上書きしない。
- 行の状態の表のエンジンの列 `applied_version` は、エンジンがその行へ最後に入れた他者の内容の版。
  - 書くのは `SyncState.WriteAppliedRowAsync` の 1 か所（取得の適用・ServerWins が戻した行・洗い替え）。その時の `base_version` と同じ版の書き直し（洗い替えが全行を書き直す・ServerWins がサーバーで変わっていない行を戻す）では進めない。
  - 強制の上書き（LocalWins・後勝ち）の決着で NULL にする（`SyncState.SettleSentAsync`）。
  - トリガーの `ON CONFLICT` は名指ししない（「ローカルの変更の記録（SyncLocalTracking）」）。
- 版で守る送信の段（`SyncTable<,>.SendAsync` の、サーバーが知っている行の更新の枝）は、端末の行が持つ版が `applied_version` より古ければ、`base_version` でなく行が持つ版を元の版にして送る。サーバーの版の照合が `ModifiedOnServer` を返す。
  - 比べ方は `SyncState.IsOlderVersion`（先頭の 0 のバイトを除いた長さ → バイトの辞書式＝big-endian の符号なし整数として比べる）。
- 見分けない形：削除・後勝ちの実行・版なしの表・版を持たない実体での保存（端末で作って持ち続けた実体・キーだけで組んだ実体。行が持つ版が空で、競合なしに上がる）。
- 残る形：サーバーへの書き込みの後・決着の前に落ちた実行の後に ServerWins が戻した行の、その前の実体の保存は、サーバーの内容が端末自身のものでも `ModifiedOnServer` になる。
- 壊すと：見分けないと、他者の内容を競合なしに上書きする。同じ版の書き直しで `applied_version` を進めると、編集 → 同期 → 同じ実体の再保存や、洗い替えの後の保存が偽の競合になる。
- ガード：`table:SyncEngineFixed`（行 `stale-entity-saved-after-download-collect-reports-modified-on-server`・`stale-entity-saved-after-download-serverwins-restores-server-content`・`stale-entity-saved-after-download-localwins-uploads-then-resave-is-not-a-conflict`・`stale-entity-saved-after-download-last-write-wins-uploads-without-conflict`・`stale-entity-saved-after-download-value-object-version-collect-reports-modified-on-server`・`stale-entity-saved-after-own-upload-is-not-a-conflict`・`stale-entity-saved-after-refresh-rewrote-own-upload-is-not-a-conflict`・`stale-entity-saved-after-serverwins-put-back-own-upload-is-not-a-conflict`・`stale-entity-read-between-own-uploads-is-not-a-conflict`・`stale-entity-saved-after-failed-settle-and-serverwins-put-back-collect-reports-modified-on-server`・`stale-entity-without-version-saved-after-download-is-not-distinguished-and-uploads`・`stale-entity-delete-after-download-is-not-distinguished-and-deletes-server-row`・`stale-entity-version-order-reads-versions-as-big-endian-numbers` ほか `stale-entity-*` の行）・`table:SyncTriggerRecording`（行 `engine-columns-kept-by-update`・`engine-columns-kept-by-delete-then-reinsert`）。

## 未送信の一覧と捨てる口

- 一覧（`SyncEngine.GetPendingChangesAsync`）は、エンジンの表の未送信の行を変更の順に `SyncPendingRow`（表の名前・キーのテキスト・削除か・サーバーが知っているか・捨てる印があるか・読んだときの行の連番）で返す。エンジンに渡していない表の行は返さない。
  - 実行のロックを取らない（専用の接続で読むだけ）。実行の最中に読んだ一覧は、その時点までにコミットされた状態を映す。ロックを待つ形にすると、長い同期の間、画面が一覧を読めない。
- 捨てる口（`DiscardPendingChangesAsync` の行を渡す版と型を渡す版・`DiscardAllPendingChangesAsync`）は、行に持ち越した ServerWins の決定と同じ印（`sw_seq` を行の連番にする。「送信」）を立てるだけで、行の内容と未送信は変えない。
  - 捨てた行は未送信のまま残り、その表を範囲に含む次の実行（同期・送信だけ・受信だけ）が、ポリシーに依らず、その行を送らずにサーバーの行で戻す（取得した差分に無ければサーバーからキーで読む。サーバーに行が無ければ変更を捨て、行は削除伝搬が消す）。同期の結果の `LocalRowChanges` には `DiscardedByServerWins` で載る。表を範囲から外した実行は、印のある行をそのまま残す。
  - 捨てた後にその行をもう一度書くと連番が進み、印は効かなくなる（書いた内容が送られる）。
  - 未送信だけを外す形にしない。端末の内容がサーバーと食い違ったまま残り、その後の同期が追いつくかが場合による。
  - 行を渡す版は、今も未送信で、連番が渡した行の連番のままの行にだけ印を立てる（一覧の後に書かれた編集を捨てない）。戻り値は、呼び出しの後に印が立っている渡した行の数（すでに立っていた行も数える）。
  - 3 つとも、実行のロックの中の 1 つの `BEGIN IMMEDIATE` のトランザクションで、未送信を読んでから印を立てる（連番の確かめは、そのトランザクションの中で読んだ値で行う）。実行の途中で印を立てると、その実行が読み終えた後の印になり、捨てたはずの行が送られる。
  - エンジンの表でない表の行・型は、何も変えずに `ArgumentException` で断る。
- 洗い替えは、捨てる印が今の連番と同じ行を未送信に数えない。`Force` の有無に依らず、その行で断らずに、`Force` で捨てる行と同じ扱いでサーバーの行に置き換える（サーバーに無い行は端末から消す。取得の上限の未送信の数え方・塊ごとに確定する経路・blob を捨てる洗い替えも同じ扱い）。
  - 印を立てた後に書き直した行（連番がずれた行）は未送信に数え、`Force` なしなら `SyncPendingChangesException` で断る。印の無い未送信がほかにあれば断り、例外の件数は印の無い行だけ。
  - 判定は `RefreshCoreAsync` の 1 か所（未送信＝印が今の連番と同じでない行）。印のある行も数えると、利用者が捨てると決めた変更のために洗い替えが断られ、`Force` を付けると捨てるつもりのない未送信まで捨てることになる。連番を見ずに印の有無だけで外すと、捨てた後に書き直した内容を洗い替えが黙って捨てる。
- 一覧の「捨てる印があるか」は、ServerWins の実行が戻せずに持ち越した決定の行でも真になる（同じ印）。
- ガード：`SyncEngineFixedRows` の `pending-*` の行（`pending-list-names-unsent-rows`・`pending-list-and-discard-cover-only-engine-tables`・`pending-list-does-not-wait-for-run-and-discard-does`・`pending-discard-settles-collect-conflict-with-server-row`・サーバーで変わっていない行をキーで読み直す `pending-discard-row-unchanged-on-server-is-read-back-*` の 3 行＝同期・送信だけ・受信だけ・`pending-discard-then-rewrite-is-sent`・`pending-discard-insert-unknown-to-server-removes-row`・`pending-discard-delete-brings-row-back`・`pending-discard-with-stale-row-is-not-discarded`・`pending-discard-by-table-and-all`・`pending-discard-is-kept-for-run-that-excludes-table`・`pending-discard-refuses-unknown-table-and-type`・洗い替えは `pending-discard-is-replaced-by-refresh-without-force`・`pending-discard-rewritten-row-still-refuses-refresh`・`pending-discard-does-not-hide-other-unsent-rows-from-refresh`・`pending-discard-is-replaced-by-chunked-refresh`・`pending-discard-is-not-counted-for-refresh-row-limit`・`pending-discard-rewritten-during-fetch-is-counted-for-refresh-row-limit`）。mutation 実証済み（洗い替えが印のある行も未送信に数えると、置き換える行・塊の経路の行・上限の 2 行・件数の行が赤。連番を見ずに印の有無だけで外すと `pending-discard-rewritten-row-still-refuses-refresh` だけが赤。例外の件数を範囲の未送信の全部から作ると `pending-discard-does-not-hide-other-unsent-rows-from-refresh` だけが赤。印を立てない・連番を確かめずに立てる・型を渡す版が全部の表に立てる・一覧がエンジンの表でない行も返す・一覧の捨てる印をいつも偽にする・一覧を実行のロックの中へ戻す・捨てる口から実行のロックを外す、のそれぞれで対応する行が赤）。

## 無制限バイナリ列

- `ExcludeUnboundedBinaryColumns` と併用可で、運ぶかは実行時引数 `SyncOptions.IncludeUnboundedBinary`（既定 false）が決める。トリガーの記録は運ぶかに依らない。
- 行の転送は除外列を含まない：差分走査の SELECT は除外列があるとき `*` から明示列挙へ切り替える。列のコピーの面は `ISyncBinaryColumns<TKey>`（ローカルとサーバー直結＝固定型 `SyncRepositoryBinaryColumns<,>`・サーバー HTTP＝`HttpSyncServerSource<,>` 自身。ガード：`CodegenCaseTests` の行 `code-sync-binary-columns-implemented-on-three-surfaces`）。
- 送る列は、サーバーが挿入として受けた行なら全列、それ以外は端末が書いた列（`blob_columns`）。列を送る前に行の書き込みの版を仮に記録し、列の送信が失敗しても次の実行が自分の書き込みを競合と取り違えない。列は一時ファイルを通して 1 本ずつ送る。
- 版のガードを外した上書き（LocalWins・後勝ち）は、サーバーの送らなかった列に触れないので、その行の列を写し待ちにする。
- 降ろした行の列は写し待ち（`blob_owed`）に記録し、列を運ぶ実行（`IncludeUnboundedBinary`）が、取得の前（前の実行の残り）と適用の後に、一時ファイルを通して行ごとに短いトランザクションで写す（内容をメモリに持たない）。その後に端末が書いた未送信の列は写さず、サーバーで NULL の列はローカルの列も空にし、サーバーかローカルに行が無ければ写し待ちだけを外す。列を運ばない実行は写し待ちを残す。
- 記述子の `FillUnsetUnboundedBinaries`（NOT NULL の除外列だけ・VO は `Create([])`・非 VO は `[]`）を、ローカルへ入れる行とサーバーへ挿入する行に当てる。
- 一意な値の回転で入れ直した行は、端末の blob を保つ。
- ガード：決定的なシナリオの `blob-*` の行（写さない列は `blob-owed-copy-keeps-unsent-local-blob`）・`SyncEngineFixedRows`（行 `primary-key-changed-row-sends-its-blob`・`unique-value-rotation-keeps-local-blobs`・`sync-vo-new-row-with-excluded-not-null-blob-downloads`・`sync-fixture-blob-cleared-on-server-is-cleared-locally`）・`SyncModelRuntimeTests.EmptyBlobWithoutConflict_IsStillAViolation`。

## 洗い替え

- 拒否の順は、引数（取得の大きさ・降ろす行の上限・除外の型）→ blob の損失（`SyncUnboundedBinaryLossException`。記述子だけを見て DB を読まない）→ 範囲の外からの参照（`SyncRefreshScopeException`）→ 未送信（`SyncPendingChangesException`。`Force` で捨てる。捨てる印が今の連番と同じ行は未送信に数えず、`Force` で捨てる行と同じ扱いで置き換える＝「未送信の一覧と捨てる口」）。`Force` で抜けられない拒否を先に出し、どれも何も変えない。同期の外で空にされた表は断らない（洗い替えがそれを作り直す）。
- 範囲の外は `Mode`（Versioned は版なしを外す。既定は `LastWriteWins`＝全テーブル）・`ExcludedEntityTypes`・構築時の除外・エンジンが同期しない表。範囲の外の表の行が範囲の表の行を参照していれば断る（記述子の `LocalReferences` で行を照会する。載らない外部キー＝`UnlistedReferencingTableNames` の表はローカルに在れば行を見ずに断る）。
- 表を空にせず、上限までのサーバーの行で置き換える（同じ適用でサーバーに無い行を消す）。件数（`SyncRefreshTableResult`）は、消した行（サーバーに無かった行）と書いた行を数える。表どうしの外部キーの循環も、外部キーの検査を切った 1 つの適用なので順に依らず置き換わる。
- **`SyncRefreshResult.DiscardedChanges` は、適用が実際に捨てた行の数**（サーバーの行で置き換えた行と、サーバーに無くて変更だけを捨てた行）。数えるのは `ApplyAttempt.NoteDiscarded` の 1 か所。
  - 取得の前に捨てると決めても、途中に端末で書き直されて残った行（連番が進んだ行）は数えない。
  - 1 回で全部の道と塊の道で同じ数え方（塊の道は、最後の段が未送信を外した行も数える）。
  - 捨てると決めた数を返すと、途中に書かれて残った変更を捨てたと報告する。
  - ガード：同期のモデルの決定的なシナリオ（割り込みの網の行 `interrupt-*-refresh-force`＝`discarded-changes=1`）・`SyncEngineFixedRows`（行 `chunk-refresh-force-discard-gone-after-keys-only-loses-change`）。
- 洗い替えの途中に端末が書いた行の扱い：`Force` が捨てるのは、連番が捨てると決めたときのままの行だけ。途中に書かれた未送信の行は残す。サーバーに無い未送信の新しい行は消さない。両方に無い行の状態を消す処理は、未送信の行を飛ばす。ガード：同期のモデルの割り込みの網の行 `interrupt-*-refresh-force`・`interrupt-*-clean-refresh`（`tests/QuickER.Tests.Integration/GeneratedRuntime/CLAUDE.md`「割り込みの点」）。
- blob を捨てる洗い替えは、範囲の表の、未送信の無い全行の除外した無制限バイナリ列を適用の中で空にし（NOT NULL の列は空の値、ほかは NULL）、写し待ちを消す。未送信の行は、行の状態の表の印で文の中から外す（未送信の行の数をパラメータに束縛しない）。
  - 未送信の行を外す条件は `SyncEngine.NotUnsentCondition` の 1 か所が組む：`<表のキーの列> NOT IN (SELECT s."key" FROM quicker_sync_row AS s WHERE s.table_name = @table AND s.dirty = 1 AND s."key" IS NOT NULL)`。
    - 行の状態の表のキーは、トリガーが表のキーの列の値をそのまま写した値（キーのテキストではない）なので、格納の型ごと同じ値どうしの比較になる。比較の左辺は表のキーの列で、親和性と照合順序は表の列のもの。
    - 副問い合わせは表の行に依らない形にする（1 回だけ流れて、表の行ごとには結果を引くだけ）。表のキーの列と状態の表のキーを行ごとに比べる形（`NOT EXISTS (… <表のキーの列> = s."key")`）にしない。状態の表のキーの列は型の宣言が無く、比較のたびに数の親和性が当たるので、状態の表の主キーの索引をキーで引けない。表の行ごとにその表の状態の行を全部読み、10 万行の表で未送信が 1 行でもあると洗い替えが数分かかって、その間ほかの接続の書き込みが待ち切れずに失敗する。
    - `IS NOT NULL` は外さない（`NOT IN` の相手に NULL が混ざると、どの行にも真にならない）。QuickER の DDL は主キーの列を NOT NULL で作るので、状態の表のキーが NULL になるのは、主キーに NULL を許す手書きの表のときだけ。
    - この条件で記録した送る行だけが残ることは、`table:SyncTriggerRecording` の全行に共通の表明（`SyncTriggerHarness.CountRowsKeptAsUnsentAsync`＝製品の条件をそのまま使う）が、整数・テキスト（文字列・日時）・値オブジェクト・実数（行 `real-stored-key-insert-and-update`）・blob（行 `blob-stored-key-insert-and-update`）で格納するキーについて見る。条件の形（表の行ごとに流し直す副問い合わせが無い）は、`SyncEngineFixedRows` の行 `unsent-rows-condition-reads-state-table-once` が実行計画で見る。
  - 洗い替えが捨てない未送信の行（洗い替えの間に書かれた行）は、blob を残す。
  - `Force` で捨てる行のうち、サーバーに行の無いものは、先に未送信を外しているので一緒に空になる。サーバーに行のあるものは、サーバーの行を置けるかがこの時点ではまだ分からないので残し、その適用がサーバーの行に置き換えた時点で、置き換えた行の除外列だけを空にする（`WriteRowsAsync` の終わり。回転で入れ直した行にも効くよう回転の後）。
  - 置けなかった捨てる行（そのサーバーの一意な値を、洗い替えが置き換えない端末の行が持つ）は、行も blob も触らず、未送信のまま残す。先に空にすると、未送信の記録（blob の書き込みを含む）だけが残り、その行を端末がもう一度書いた後の同期が、空の blob をサーバーへ送ってサーバーの blob を消す。
- 端末とサーバーの両方に無い行の状態を消す（残すと、同じキーで作った行を更新として送り、MissingOnServer になる）。
- ここまでの「1 つの適用で全部」「取得の失敗は端末を前のまま残す」は、1 回で全部の道（降ろす行が塊の閾値以下か、捨てる行以外の未送信がある洗い替え）の性質。降ろす行が閾値を超える洗い替えは塊ごとに確定し、全部か無しかでない（「塊ごとに確定する経路」）。
- 保留で取り残した行は、もう 1 回の取得で取り直す。取り直しても残れば `InvalidOperationException`。取得の失敗は、端末・未送信・行の状態の表を洗い替えの前のまま残す。もう 1 回の取得は上限を取り直すので、上限を実行の始めに固定するサーバーでも、最初の取得の後に変わった保留の相手の行が同じ洗い替えに入る（ガード：`SyncModelSqlServerRuntimeTests.Scenario_MatchesDirect` の割り込みの網の行 `interrupt-server-BeforeServerFetch-unique-holder-after-taker-update-*-refresh` の族）。
- `BatchSize` の既定は 2000（差分同期の `DownloadBatchSize` の 500 と別）。
- ガード：決定的なシナリオの `refresh-*`・`blob-refresh-*` の行（`refresh-refusal-order-outside-reference-before-pending`・`refresh-refusal-order-blob-loss-before-outside-reference`・`refresh-force-forgets-row-gone-on-both-sides-and-same-key-is-inserted`・`blob-refresh-discard-leaves-no-blob-on-row-changed-after-ceiling`・`refresh-refuses-when-rows-outside-refer-inside`・`refresh-empties-tables-in-a-foreign-key-cycle` ほか）・`SyncEngineFixedRows`（行 `refresh-fetch-failure-leaves-device-as-before`・`invalid-arguments-refuse-before-anything`・`refresh-force-discard-blobs-discard-held-back-is-not-left-unsent-with-empty-blob`・`unsent-rows-condition-reads-state-table-once`・`refresh-unlisted-referring-table-refuses-while-present-locally`）・`table:SyncTriggerGuard`（行 `di-excluded-table-rows-referring-to-scope-refuse-refresh`）。blob を空にする処理は mutation 実証済み（表の全体の処理で、置けるかが分かる前に捨てる行の blob も空にする・1 回で全部の道で置き換えた捨てる行の blob を空にしない・未送信の行の blob も空にする・行の状態の表の未送信の印を見ずに状態の行があれば残す・未送信の行を外す条件を行ごとに状態の表を引く形にする、のそれぞれで対応する行が赤。条件の `IS NOT NULL` を外す変異は赤にならない＝状態の表のキーが NULL になる表を QuickER の DDL は作らない）。

## 取得と降ろす行の上限

- 取得は表ごとにページを 1 つずつ流す（`SyncTable.FetchPagesAsync`）。サーバーの全キーは別の呼び出し（`FetchServerKeysAsync`）で、削除伝搬を使う実行だけが取る。
  - サーバーの差分ソースへの呼び出しの順は「表ごとに、行のページを全部取ってから、その表のキー」。同期のモデルの割り込みの点と失敗の注入がこの順に依るので、変えない。
  - 差分ソースが「続きがある」と答えたのに再開点が進まないときは、表を名指しした `InvalidOperationException` で止める（守りは `SyncTable` の取得の 1 か所で、同期・受信だけ・洗い替えのどれも通る）。版ありの表は、最後の行の版が無い・空・そのバッチに渡した再開点と同じとき。版なしの表は、ページの最後の行のキーが渡したキーと同じ（キーのテキストで比べる）とき。守りが無いと、利用者が差し替えた差分ソースの誤りで、同期と洗い替えが終わらずに回り続ける。
- 1 回の取得と適用（`DownloadOnceAsync`）ごとに、取得した行を範囲の表の全部を通して数える（版なしの表の行も数える）。
- 合計が `DownloadRowLimit`（`SyncOptions`・`SyncRefreshOptions`。既定 300,000）を初めて超えた時点で 1 回だけ、範囲の表の未送信の行を読む（`ReadUnsentInScopeAsync`）。未送信があれば、適用の前に `SyncDownloadTooLargeException`（取得した行数・上限・表ごとの未送信の行の数）で断る。
  - 断った実行は取得した行を捨て、端末の行・行の状態の表・再開点を変えない。取得の前の blob の写しと、同期の送信の段は、断る前に流れている。
  - 同期と受信だけの実行は、その実行が ServerWins で捨てる行も未送信に数える。洗い替えは `Force` で捨てる行を数えない（数えると、例外の文が抜け出す道に挙げる「`Force` の洗い替え」が同じ例外で断られる）。洗い替えで数えられるのは、洗い替えの途中に端末で書かれた未送信だけ。
  - 洗い替えが捨てる行を外すのは、行の連番が捨てると決めたときのまま（`SyncDiscard.Sequence` と同じ）のときだけ。捨てると決めた後に端末で書き直された行は、適用も捨てずに残す（連番で見分ける）ので、未送信に数える。キーだけで外すと、その書き直しがあっても上限を超えた洗い替えが断られない。
  - 範囲の外の表の未送信は数えない。上限ちょうどは断らない（断るのは超えたとき）。
  - 未送信が無ければ、塊ごとに確定する経路へ入る（「塊ごとに確定する経路」）。
- ガード：同期のモデルの決定的なシナリオ（`SyncModelRuntimeTests` の行 `dl-row-limit-exceeded-with-unsent-change-refuses-and-applies-nothing`・`dl-row-limit-exceeded-after-upload-applies-everything`・`dl-row-limit-equal-to-fetched-rows-is-not-refused`・`refresh-force-over-row-limit-discards-unsent-and-is-not-refused`）・`SyncEngineFixedRows`（行 `refresh-over-row-limit-with-change-written-during-fetch-is-refused`・`refresh-force-over-row-limit-with-discarded-key-rewritten-during-fetch-is-refused`・進まない差分ソースは `non-advancing-source-*` の 4 行＝同期と洗い替え × 版ありと版なしが例外で終わり、端末・状態の表・再開点・サーバーが前のまま）。mutation 実証済み（進まない差分ソースの守りを版あり・版なしで 1 つずつ外すと、対応する 2 行が赤。未送信があっても断らない・未送信が無くても断る・「超えたら」を「以上なら」にする・洗い替えでも捨てる行を未送信に数える・洗い替えのときは断らない・洗い替えが捨てる行をキーだけで外す・行を表ごとに数え直す、のそれぞれで対応する行が赤）。

## 塊ごとに確定する経路

同期・受信だけの実行・洗い替えは、降ろす行が多く、範囲の表に未送信が無いとき、適用を 1 つのトランザクションにせず、塊ごとに確定する。1 回で全部の適用は、行の数に比例してメモリを持ち、その間ずっと書き込みのロックを持つため。

- 道の決め方（1 回の取得と適用ごと。`DownloadOnceAsync`）：
  - 取得した行の合計が閾値（`SyncEngine.ChunkThreshold`・internal・既定 20,000）を初めて超えた時点で、範囲の表の未送信を 1 回読む（上限の判定と同じ `ReadUnsentInScopeAsync`。同期はその実行が ServerWins で捨てる行も未送信に数え、洗い替えは `Force` で捨てる行を、連番が捨てると決めたときのままなら数えない）。無ければ塊の経路へ入る。洗い替えの 2 回目の取得（保留が残ったときの再開点からの取得）も同じ入口を通る。
  - 未送信があれば、行をためて 1 回で全部の道を続ける。上限（`DownloadRowLimit`）を超えた時点でもう 1 回読み、あれば断り、無ければそこで塊の経路へ入る。
  - 未送信のある同期は塊に入らないので、未送信の行の規則（ServerWins が捨てる記録・戻せなかった行の決定の持ち越し）は同期の塊の経路に現れない。塊の経路が捨てる行を持つのは、`Force` の洗い替えだけ。
  - 閾値・塊の大きさ（`ChunkSize`・既定 10,000）・トランザクションの間の隙間（`ChunkWriteGap`・既定 170 ms）と時計（`TimeProvider`）・道を知らせる口（`DownloadRouteObserved`）・最後の段の持ち越しの行数を知らせる口（`CarriedRowsApplied`）は internal で、テストだけが使う。公開の面には無い。
  - 閾値の既定の 2 万行は、1 回で全部の道の 1 トランザクション（書き込みのロックを持つ時間が行の数に比例する）を、遅い機械でも 2 秒の内に収める値：i7-13700K の P コアで 1 行あたり約 37 µs なので約 0.75 秒、遅い機械を 2 倍とみても約 1.5 秒（3 万行だと 2 倍で約 2.2 秒になり外れる）。5 万行では閾値の少し下で 1.7〜1.9 秒を実測した（2026-11-23）。
- 進み方：
  - 入口（`EnterChunkedDownloadAsync`）：まだキーを取っていない表（いま行を取っている表とその後ろの表）のサーバーのキーを表の順に取り、削除の段を流し、それまでにためた行を表の順に塊として流す。
  - 削除の段：範囲の全部の表を「行なし・キーつき」の計画にして、1 つのトランザクションで適用（`ApplyAsync`）に通す（削除伝搬と、外部キーの違反なら親を残すやり直し）。行は書かず、再開点は進めない。削除伝搬を使わない実行では流さない。
  - 塊の段（`ChunkedDownload`）：表ごとに、塊の大きさの行がたまるか表の行を取り終えたら、その行だけの計画を `ApplyAsync` に 1 回通す（1 塊＝1 トランザクション。塊は表をまたがない）。行の状態は塊の行のキーの分だけ読み（`SyncState.ReadRowsByKeyAsync`）、表の全行・全キーを読まない。表の大きさに比例するのは、DB 全体の外部キーの確かめだけ。
  - 持ち越し：塊の適用が保留にした行（親が無い・一意な値が空かない・サーバーが知らないキーの未送信のローカル行）は、書かずにメモリへ持ち越す。行は「取得の順でその手前の版」を連れる。
  - 塊ごとの再開点：その表に持ち越しが無い間は、塊の最後の行（か最初に保留した行の手前）まで進める。持ち越しがある表は、最初に持ち越した行の手前で止めたままにする（`SyncApplyPlan.AdvancesAnchor`）。後ろの塊を書いても進めない。
  - 最後の段：持ち越した行の全部を、表の順の計画にして `ApplyAsync` に 1 回通す（1 回で全部の適用と同じ部品。塊をまたぐ循環と一意な値の入れ替えは、両方の行がこのトランザクションに入るので解ける）。保留が残らなかった表の再開点は最後に取得した行まで、残った表は残った最初の行の手前まで（`BeforeVersions`・`ThroughVersion`）。残った保留だけを打ち切りとして報告する。持ち越しが無ければ流さない。
  - 未送信・エコー・一意な値の後回しと回転・外部キーの扱いは、1 回で全部の適用の部品（`ApplyAsync`・`WriteRowsAsync`・`SaveResumePointAsync`）をそのまま使い、写しを持たない。段ごとの違い（状態の読み方・再開点の決め方）は計画（`SyncApplyPlan`）の値で渡す。
  - トランザクションの間の隙間：塊の経路のトランザクション（削除の段・塊・最後の段）は、同じ塊の経路の前のトランザクションのコミットから `ChunkWriteGap` がたっていなければ、残りの時間だけ待ってから始まる（`ChunkedDownload.ApplyAfterGapAsync` の 1 か所。最初のトランザクションと、間の取得がそれより長かったときは待たない）。待つ間は実行の接続に文もトランザクションも無い（「実行の接続」）。取り消しは待ちの中でも効き、途中で落ちた実行と同じに確定した段を残して終わる。
    - 理由：ほかの接続の書き込みは、ロックを常に試すのでなく、間を置いて取り直す。SQLite の busy handler（`busy_timeout` を当てた接続＝QuickER の生成の接続ファクトリ・EF Core 版のインターセプター）は眠りを 1・2・5・10・15・20・25・25・25・50・50 ms と伸ばし、328 ms を過ぎると 100 ms おき。Microsoft.Data.Sqlite（`busy_timeout` を当てない接続）は自前で `Thread.Sleep(150)` して取り直す（出典：dotnet/efcore release/10.0 の `src/Microsoft.Data.Sqlite.Core/SqliteDataReader.cs` の `NextResult` と `SqliteCommand.cs` の `PrepareAndEnumerateStatements`。`SqliteConnection.Open` は `busy_timeout` を当てない）。入口で取得済みの行を塊として続けて流すと、トランザクションの間は 0.3〜2 ms しか空かず、待っている書き込みはほぼ入れずに、続く区間（10 万行の洗い替えで 2.0〜2.6 秒）を全部待つ。
    - 値の規則は「待っている書き手の取り直しの最長の間隔 ＋ Windows の眠りの粒度（約 15.6 ms）を超える値」。既定の 170 ms は 150 ＋ 15.6 を超える。i7-13700K の P コア・10 万行で、ほかの接続の書き込みの待ちの最大は、隙間なしで 1.7〜4.6 秒、170 ms で 0.35〜0.49 秒（1 つのトランザクションの長さ）、120 ms では `busy_timeout` を当てない書き手が最大 2.36 秒（2026-11-23 の実測）。代わりに大きい洗い替え・同期はトランザクションの数 × 隙間ほど長くなる（10 万行の洗い替えで約 +1.6〜1.9 秒）。
    - 壊すと起きること：隙間を外す（または 150 ms 以下にする）と、塊の経路の間、ほかの接続の書き込みが塊 1 つ分を超えて、入口の区間の全部を待つ。いつも隙間の全部を眠ると、取得の長い塊の経路（HTTP）まで無駄に長くなる。
    - 小さい塊の端末と、決まった形の行の `SmallChunks` は隙間を 0 にする（2 行の塊はトランザクションが多く、照合が長くなる。隙間は結果を変えない）。
- 洗い替えの塊の経路（同期と同じ進み方に、洗い替えの分を足す）：
  - 削除の段（計画の印 `KeepsDiscardsTheServerHas`）：削除伝搬に加えて、両方に無い行の状態を消し、blob を捨てる洗い替えなら範囲の表の未送信の無い全行の除外列を空にして写し待ちを消し（1 回で全部の道と同じ処理）、`Force` で捨てる行のうちサーバーに行の無いもの（キーがサーバーのキーの集合に無い）を片付ける。範囲の表の再開点を「何も取っていない」（null）に戻す。
    - 再開点を戻すのは、どこで落ちても、次の同期が洗い替えの止まった所の続きから降ろせるようにするため。前の再開点を残すと、書いていない行の先に再開点が残りうる。
    - `Force` で捨てる行のうちサーバーにまだ行があるものは、削除の段では触らない（未送信の印も blob も）。その行を持ってくる塊がサーバーの行に置き換える。
  - 塊の段（計画の印 `RunsTableWideSteps` が偽）：エコーを飛ばさずに全行を書き直す。`Force` で捨てる行（連番が捨てると決めたときのままの行）は、その行の塊の中でサーバーの行に置き換えて未送信を外し、blob を捨てる洗い替えならその行の除外列だけを空にする。連番の変わった行と、途中に端末で書かれた未送信の行は、ローカルの内容を blob ごと残す。表の全体に掛かる段（両方に無い行の状態を消す・全行の blob を空にする・削除伝搬）は塊ごとに繰り返さない。
  - 最後の段：持ち越した行に加えて、削除の段の時点でサーバーにあったのに取得が持ってこなかった捨てる行（取得の上限の後にサーバーで書き直された行・キーを取った後にサーバーで消えた行）を、変更だけを失う形で片付ける。blob を捨てる洗い替えなら、未送信を外すのと同じ所でその行の除外列も空にする（削除の段はこの行の blob を残し、どの塊もこの行を書かないので、ここで空にしないと残る）。
  - 途中で落ちた洗い替えは、そこまでに確定した段を残して例外で終わる（捨てたとは報告しない）。流し直せば 0 から全部を書き直してそろう。その後の同期は続きから降ろし、まだ置き換えていなかった捨てる行は、行も blob も端末の内容のまま未送信で残って、その同期が普通に送る。
- 塊の経路だけの規則は 3 つ：保留の持ち越しと最後の段／塊ごとの再開点（最初に持ち越した行の手前で止める。洗い替えは削除の段で戻してから進める）／途中で落ちた実行の再開（前の 2 つから出る）。増やすときは、1 回で全部の道と結果が割れないことを、小さい塊の端末の照合で確かめる。
- 1 回で全部の道と違うこと：
  - 途中の状態がほかの接続から見える（削除の段の後・塊の間）。どの時点でも外部キーの違反は作らない。
  - 途中で失敗した実行は、そこまでの塊を残す。次の実行は再開点から続ける。再開点より後ろで書き済みの行は取り直されるが、基準の版が同じ行はエコーとして飛ばされ（行の状態・未送信の印を変えない）、その間に端末で編集された行は未送信としてローカルの内容が残る。
  - 塊の途中に端末で書かれた未送信の行は、塊のトランザクションの中で行の状態を読むので、1 回で全部の道の「取得と適用の間の編集」と同じに扱う（ローカルの内容を残す）。
- 壊すと起きること：
  - 持ち越しを捨てる・最後の段を行ごとの別のトランザクションにすると、塊をまたぐ循環と入れ替えが降りない。
  - 再開点を持ち越し中の行の先へ進めると、途中で落ちた実行の後、持ち越していた行が二度と降りない（最後まで流れた実行では最後の段が再開点を書き直すので見えない）。
  - 削除の段を塊の後へ回すと、消える行が持つ一意な値を塊が取れない。入口の後ろの表のキーを取らないと、その表の削除伝搬が流れない。
  - 未送信があるのに塊へ入ると、未送信の行の規則が塊の経路に持ち込まれる。
  - 洗い替えが削除の段で再開点を戻さないと、途中で落ちた洗い替えの後の同期が、まだ書き直していない表を前の再開点から降ろす。
  - 削除の段で、サーバーにまだ行がある捨てる行の blob まで空にすると、その行を置き換える前に落ちた洗い替えの後、未送信の記録（blob の書き込みを含む）だけが残って、次の同期が空の blob をサーバーへ送る。
  - 最後の段で、取得が持ってこなかった捨てる行の blob を空にしないと、blob を捨てる洗い替えの後も、利用者が捨てると選んだ blob が未送信の印の無い行に残る。blob を運ばない同期はその行を書き直しても除外列に触らないので、残り続ける（1 回で全部の道では空になる）。
  - 表の全体に掛かる段を塊ごとに繰り返しても結果は変わらない（性能の退行で、行では捕まえられない。塊のトランザクションの時間は大きさの実測で見る）。
- ガード：`SyncSupportAdoSqliteEngineRuntimeTests.SmallChunkScenarios_MatchDirect`（直結の Theory が流す行を小さい塊の端末で流し、1 回で全部の道の結果＝緑と行ごとに照らす）・`SyncSupportAdoSqliteEngineRuntimeTests.SmallChunkRandomSeeds_ConvergeWithoutLoss`・同期のモデルの決定的なシナリオ（`SyncModelRuntimeTests` の行 `chunk-selfref-parent-in-later-chunk`・`chunk-cross-table-cycle-both-new`・`chunk-selfref-cycle-across-chunks`・`chunk-unique-swap-across-chunks`・`chunk-taker-before-releaser`・`chunk-delete-stage-parent-gone-after-next-sync`）・`SyncEngineFixedRows`（行 `chunk-failure-between-chunks-keeps-first-chunk-and-next-sync-resumes`・`chunk-failure-after-carry-keeps-resume-point-before-carried-row`・`chunk-unsent-edit-between-chunks-kept-then-serverwins-restores`・`chunk-unsent-edit-between-chunks-kept-then-localwins-uploads`・`chunk-row-gone-after-keys-stays-until-next-sync`・`chunk-download-route-cells`・`chunk-write-transactions-leave-gap-for-other-writers`（トランザクションの間の隙間：偽の時計で待ちの回数と頼んだ長さを数え、隙間の有無で結果が同じ。既定の閾値と隙間の値も表明する。mutation 実証済み＝待たない・いつも隙間の全部を眠る・既定の隙間を 0 にする・削除の段だけ／最後の段だけ隙間を通さない・待つ長さを半分にする・既定の閾値を 5 万へ戻す、のそれぞれでこの行だけが赤）・途中で落ちた洗い替えの `chunk-refresh-*-failure-then-*` の 6 行・`chunk-refresh-force-discard-gone-after-keys-only-loses-change`・`chunk-refresh-force-discard-blobs-failure-keeps-blob-of-unreplaced-discard`・`chunk-refresh-force-discard-blobs-completes-with-every-blob-empty`・`chunk-refresh-force-discard-blobs-empties-blob-of-discard-changed-after-ceiling`）。仕組みは `tests/QuickER.Tests.Integration/GeneratedRuntime/CLAUDE.md`「小さい塊の端末」。mutation 実証済み（持ち越しを捨てる・最後の段を行ごとの別のトランザクションにする・再開点を持ち越し中の行の先へ進める・削除の段を最後の段の前へ回す・削除の段を外す・閾値の時点の未送信を無視する・入口の後ろの表のキーを取らない・塊の計画で行の状態を読まない・入口までにためた行を塊へ流さない。洗い替え：削除の段で再開点を戻さない・塊で捨てる行を置き換えない・塊の洗い替えでエコーを飛ばす・削除の段の blob を空にする処理を外す・削除の段から両方に無い行の状態を消す処理を外す・最後の段で取得が持ってこなかった捨てる行を片付けない・削除の段で捨てる行の blob も空にする・置き換えの時点で blob を空にしない・最後の段で取得が持ってこなかった捨てる行の blob を空にしない、のそれぞれで対応する行が赤）。

## 実行の入口

- 同じ DB への実行は、プロセスの中で DB のファイルごとのロックで 1 本ずつ流す（メモリの DB はエンジンごと）。
- 実行の前に、同期する表が同期の外で空にされていないかを確かめ、空なら `SyncLocalTableEmptiedException` で断る（再開点が進んだまま空の表を受け入れると、サーバーの行が端末へ二度と降りない。洗い替えで抜ける）。
- 引数（取得の大きさが 0 以下・降ろす行の上限が 0 以下・同期しない型の除外）は、同期・送信だけ・受信だけ・洗い替えの全部の入口で、何かをする前に断る。
- ガード：`SyncEngineFixedRows`（行 `runs-on-same-database-are-serialized`・`emptied-table-refuses-every-entry`・`sync-inside-save-hook-fails-on-lock-wait-and-leaves-nothing`・`invalid-arguments-refuse-before-anything`）。

## 実行の接続

- 1 回の実行（同期・送信だけ・受信だけ・洗い替え・捨てる口）は、専用の接続（`ILocalRowStore.CreateDedicatedConnection`＝プールしない・外部キーの検査を切る）を実行の入口で 1 本だけ開き、実行の中の読み書きは全部その接続を借りる（`SyncEngine.RunConnection`）。
  - `RunExclusiveAsync` が接続を開き、開いた後の `DataSource` から実行のロックの鍵を作り、ロックを取った後にその接続を実行の本体へ渡す。本体が終わったら（正常・例外・取り消し。ロックを待っている間の取り消しも）接続を閉じてから、ロックを手放す。実行の後に接続は残らない。
  - 接続はエンジンのフィールドに持つ（ロックを取った実行だけが設定して外す。同じ DB への実行は 1 本ずつなので、1 つのエンジンで同時に走る実行は 1 つ）。`AsyncLocal` にしない（利用者の差分ソースの中へ値を流さない）。
  - 借りる口は、実行の外で呼ばれたら名指しの `InvalidOperationException`。
- 専用の接続を開く所は 2 か所だけ：実行の入口と、未送信の一覧（`GetPendingChangesAsync`＝実行のロックを取らずに実行と同時に走るので、自分の接続を開いて閉じる。実行の接続を借りると、1 本の接続を 2 つの流れが同時に使う）。
  - 開く所を増やすと、1 回の実行が開く接続が段と送る行の数に比例して増える。開くたびに、接続を開く処理・開くたびの確認の 1 文・`busy_timeout` と `foreign_keys` の PRAGMA が付き、多くの端末を並べる実行（テスト・CI）で開閉の取り合いになる。
- トランザクションの区切りは接続の持ち方に依らない：適用は 1 つの `BEGIN IMMEDIATE`、送った行ごとの決着・blob の行ごとの写し・捨てる印は、それぞれ短い `BEGIN IMMEDIATE`。
- トランザクションの外では、実行の接続に文もトランザクションも開いたままにしない（Reader・Command・`SqliteBlob` は使った範囲で破棄する）。開いているだけの接続は SQLite のロックを持たないので、実行がサーバーを待っている間、ほかの接続の書き込みは待たない。開いたままサーバーを待つと、アプリの書き込みが待ち切れずに失敗する。
- 接続をまたいで残るもの：
  - 設定：`foreign_keys = OFF` と `busy_timeout` は実行の間そのまま。エンジンはほかの接続ごとの設定を変えない。
  - 一時表：キーの表（`quicker_sync_stored_keys`）は使うたびに先に落としてから作り、使い終わったら落とす。回転の表（`quicker_sync_rotate`）は `IF NOT EXISTS` で作って接続に残し、中身は回転の終わりに表ごとに消す。どちらも適用のトランザクションの中で作るので、巻き戻せば一緒に消える。
  - 適用中の印：どれもトランザクションの中で立てて、コミットの前に戻す（例外の経路では巻き戻しで戻る）。
  - `last_insert_rowid()`：接続ごとの値で、トランザクションをまたいで残る。読むのは挿入の直後だけ（`ApplyAttempt.RawKeyAsync`）なので、前の段の値を読むことは無い。
  - 適用の試行のやり直し（外部キーの違反の後）は、巻き戻した同じ接続でトランザクションを張り直す。
- `ROLLBACK` の失敗は握りつぶして元の例外を投げ直す（適用・送信の決着・blob の写し・捨てる印）。その例外を捕まえて実行を続ける所は無いので、実行が終わって接続を閉じた時点で、残ったトランザクションは消える。実行の中で例外を捕まえて続ける所（サーバーの書き込みの失敗と競合の分類・一意な値の重なりのセーブポイントの巻き戻し）は、どれも `BEGIN IMMEDIATE` のトランザクションを開いたまま残さない。開いたまま次の `BEGIN IMMEDIATE` に届けば、SQLite が断って実行が失敗する。
- 開くたびの確認（「開くたびの確認（SyncLocalTrackingGuard）」）は、実行ごとに 1 回（実行の始め）だけ走る。実行の途中にスキーマが変わっても、その実行の中では確かめ直さない。エンジンの書き込みは適用中の印でトリガーに記録させないので、エンジン自身はトリガーに依らない。アプリの接続は開くたびに確かめるので、実行の途中に業務の表を作る・トリガーを落とすといった変更は、その後にアプリが開く接続が拾う。
- ガード：`SyncEngineFixedRows`（行 `run-connection-is-opened-once-per-run`＝同期・送信だけ・受信だけ・洗い替え・塊ごとに確定・競合の 10 の形で開く回数がちょうど 1・`run-connection-is-closed-after-failed-run`・`run-connection-list-opens-its-own-and-discard-opens-one`・`run-connection-does-not-block-other-writers-between-transactions`・`run-connection-temp-table-rotation-in-two-chunks`）・`SourceRuleTests`（規則 `sync-dedicated-connection-openers`＝テンプレートの中で専用の接続を開くメソッドの呼び出しが 2 か所・`CreateDedicatedConnection` の呼び出しが 1 か所）。mutation 実証済み（実行の終わりに接続を閉じない・未送信の一覧が実行の接続を借りる・送信の決着をコミットしない・実行の中の 1 か所を開き直す形へ戻す、のそれぞれで対応する行が赤。未送信の一覧の読み取りを破棄せずに開いたままサーバーへ進むと、`run-connection-does-not-block-other-writers-between-transactions` が、別の接続の書き込みの `database is locked` で赤）。

## 版なしテーブルと後勝ちモード

- `SyncOptions.Mode`：`Versioned` 既定＝版ありだけを同期し版なしには触れない（トリガーの記録は続く）／`LastWriteWins`＝全テーブル対象・ラン全体で意味論を 1 つに統一。
- 版なしダウンロードは毎回キー昇順の全量スキャン（`SELECT TOP … WHERE key > @afterKey ORDER BY key`）。版ありは後勝ちランでも増分ダウンロードを維持する。
- 除外は 2 層：ラン単位 `SyncOptions.ExcludedEntityTypes`（記録は継続・同期しない型は `ArgumentException` で黙殺しない）と、構築時 `AddGeneratedSyncEngine(…, excludeFromSync:)`（「生成と DI」）。
- 未送信の拒否・`Force` の破棄・blob の損失の拒否・ServerWins の全破棄はいずれもランの範囲の表だけで数える。
- ガード：シナリオ `lww-downloads-versionless-table-every-run`・`lww-versioned-run-leaves-versionless-table-alone`・`lww-run-exclusion-skips-table-for-one-run`・`CodegenCaseTests` の行 `code-sync-mixed-diagram-versionless-shapes`・`exec-sync-all-versionless-descriptors`・`table:SyncTriggerGuard`（行 `di-excluded-table-gets-no-trigger-and-is-not-recorded`）。

## キーのテキスト

- 日時の主キーは Kind を落とした往復書式：`BuildFormatKey` が `DateTime.SpecifyKind(…, Unspecified).ToString("o")`、`BuildParseKey` が `SyncKeyText.WithoutDateTimeKind` を通してから読む（公開の静的クラス `SyncKeyText`）。どちらの DB も Kind を持たず読み戻した行は Unspecified なので、Kind つきのテキストは同じ行のテキストと一致しない。`DateTimeOffset` はオフセットが値の一部なのでそのまま。
- decimal の主キーは指数表記を使わない：整形・読み取りは `SyncKeyText.FormatDecimal`（`0.############################`）/ `ParseDecimal`（`NumberStyles.Float`）の 1 か所で、生成物の記述子はそれを呼ぶだけ（書式の写しを生成側に持たない）。`G29` は絶対値 1e-5 未満を `1E-06` と書き、`decimal.Parse`（`NumberStyles.Number`）が `FormatException` で止まる。
- 版なしの表のキー順ページング：`@afterKey` は型なし（文字列は nvarchar）で束縛されるので、文字列の主キーは `BuildServerAfterKey` が列と同じ型へ CAST する（SQL Server の型マッパーが渡す `SqlDbTypeName` と `SqlDeclaredLength`）。varchar を nvarchar と比べると SQL_* 照合順序で ORDER BY と比較の規則が食い違い、ページの境目で行を読み飛ばす。
- 固定長の文字列の主キーは生成時に警告する（「生成の条件と診断」）。
- ガード：`SyncKeyTextRuntimeTests`（Kind ごとの整形・decimal の往復）・`CodegenCaseTests` の行 `probe-sync-key-text-decimal-key`（decimal の主キーの図を生成→実コンパイル→記述子を実行）・`probe-sync-key-text-decimal-order-key-scale`・`probe-sync-key-text-timespan-key`、シナリオ `25-datetime-key-utc`・`25-datetime-key-local`・`19-varchar-key-paging-skips-rows`（`SyncModelSqlServerRuntimeTests`）（mutation 実証済み）。

## 実行が変えた行の一覧

- `SyncResult.LocalRowChanges`（`SyncLocalRowChange`＝表・キーのテキスト・理由の record）。理由は `SyncLocalRowChangeReason.DiscardedByServerWins` の 1 つ（ServerWins・捨てる口・持ち越した決定が変更を捨てた行）。(表, キー, 理由) ごとに 1 件・追加順。
- 例外で終わった `SyncAsync` は、それまでの一覧を例外の `Data[SyncEngine.LocalRowChangesDataKey]` に入れて投げ直す（例外の型は変えない）。
- ガード：シナリオ `thrown-serverwins-run-hides-settlement`・`thrown-serverwins-run-hides-settled-insert`。

## 差分ソースと HTTP

- エンジンは `ISyncServerSource<TEntity, TKey>`（再開点の後の行を `SyncChangeBatch<TEntity>`〔行＋`HasMore`〕で返す・全キー列挙・上限取得・アップロード先 `Writer`・版なし用のキー順ページング）に対してだけ動く。
- 直結実装は既存の生 SQL 面と既存 Repository だけで組む（新しい ADO コードを書かない）。
- `Writer` の型はリモート面 `IRemoteRepositoryCore<,>`（送信が使うのはネットワークを越えられる操作だけ＝直結リポジトリと HTTP クライアントが同じ位置に立てる）。
- 継続の判定はソースが返す `HasMore`（行数とバッチサイズの比較を呼び出し側で行わない）。
- HTTP 経路（`GenerateSyncSupport && GenerateRemoteServices`）：サーバー側は既存のエンドポイントグループへ同期専用の 4 本（`SyncCeiling` / `SyncChanges` / `SyncKeys`＋版なしテーブルにのみマップする `SyncPage`・操作名の正本は `RemoteSyncOperations`）。クライアントは `HttpSyncServerSource<,>`（`HttpRemoteRepositoryCore` 派生なので `Writer` は自分自身）。
  - エンドポイントは差分ソースの薄い remoting：ハンドラは DI から `ISyncServerSource<,>` を解決して呼ぶだけで意味論の実装は 1 つ。アップロードは既存の CRUD／保存エンドポイントを通る。ステートレス（再開点は毎リクエストの anchor、上限は ceiling として送られる）。
  - 上限は呼び出し側の値：`SyncCeiling` が返した値を同じパスで送り返す限りにおいてのみ再開点の保証が成立する（大きな ceiling を送ると、その下で実行中のトランザクションの行を恒久的に読み飛ばす）。
  - バッチサイズは 0 以下を 400 で拒む（上限は設けない＝取り放題を止めるのはグループの認可）。
- ガード：`SyncHttpRuntimeTests`（アンカーの Base64 往復・400 拒否・サーバー不在）・`SyncSqlServerHttpRuntimeTests`（上限の値の HTTP の往復・版の書き戻しと 409 の競合）・`CodegenCaseTests` の行 `code-sync-remote-services-emit-endpoints-and-http-sources`・`code-sync-endpoints-check-source-registration-at-mapping-time`。

## 既知の限界

- 同じ DB へ 2 つのプロセスから同時に同期する形は直列にしない（ロックはプロセスの中だけ）。
- エンジンの列の足りない状態の表（`quicker_sync_row`）を持つ DB には、列を足さない。エンジンの最初の読み取りが SQLite の `no such column` の例外で止まる（適用は巻き戻る）。
- 1 回で全部の道（取得した行が閾値以下か、範囲の表に未送信がある実行）は、適用を 1 つのトランザクションで流し、その間書き込みのロックを持つ。ロックの時間は降ろす行の数に比例する（i7-13700K の P コアで 1 行あたり約 35 µs。閾値の 2 万行で約 0.75 秒）。未送信のある同期は降ろす行の上限（`DownloadRowLimit`・既定 30 万行）まで塊に入らないので、上限近くでは約 10 秒ロックを持ち、ほかの接続の書き込みはその間待つ（25 万行で 8.4〜8.8 秒を実測。失敗にはならない）。隙間（「塊ごとに確定する経路」の `ChunkWriteGap`）はこの道に効かない。
- 塊ごとに確定する経路は、入口の時点で行を取り終えていない表のサーバーのキーを、行より先に取る（削除の段が全部の表のキーを使うため）。キーを取った後・その表の行を取る前にサーバーで消えた行は、その実行では端末に残り、次の実行の削除伝搬で消える。ガード：`SyncEngineFixedRows`（行 `chunk-row-gone-after-keys-stays-until-next-sync`）。
- 塊ごとに確定する経路の削除の段は、塊より前に流れる。サーバーに無い親を消すと、端末の子（サーバーでは別の親へ付け替わっているが、その行は後の塊で降りる）が外部キーの違反になる形では、親を残す。1 回で全部の道なら同じ適用で消える親が、その実行の後も端末に残り、次の実行の削除伝搬で消える。ガード：同期のモデルの決定的なシナリオ（`SyncModelRuntimeTests` の行 `chunk-delete-stage-parent-gone-after-next-sync`。小さい塊の端末と直結の Theory の違いは `SyncModelSmallChunkScenarioDifferences` が理由つきで宣言する）。
- 塊ごとに確定する経路の持ち越しの行は、最後の段まで全部メモリに持つ（数の上限は無い）。
- 記録の仕組みを通らずに入った行（端末にもサーバーにも同じ行があるが、行の状態の表に状態が無い行）は、最初の同期か洗い替えが状態を付ける前に書くと、送信が正しく決まらない。更新は、サーバーが知らない行の未送信として記録され、Collect の同期が毎回 `DuplicateOnServer` の競合を報告する。削除は送られず、次の同期でサーバーの行が端末へ戻る（競合の報告は無い）。書く前に同期か洗い替えを 1 回流せば、その後の編集は競合なしに上がる。ガード：`SyncEngineFixedRows`（行 `stateless-row-update-before-first-sync-is-reported-as-duplicate-on-server`・`stateless-row-delete-before-first-sync-comes-back`・`stateless-rows-take-state-from-first-sync-then-edit-uploads-*` の 2 行＝同期と洗い替え）。
- サーバーへの書き込みが通った後に決着が失敗すると、次の Collect は自分が書いた行を相手に ModifiedOnServer を報告し続け、LocalWins の同期で収束する（docs の既知の制限）。ガード：`SyncEngineFixedRows`（行 `failure-after-server-write-before-settle-is-reported-until-local-wins`）。

## ガードテストの一覧

- 決定的なシナリオは同期のモデル（`SyncModelRuntimeTests`＝直結・`SyncModelHttpRuntimeTests`＝HTTP・`SyncModelSqlServerRuntimeTests`＝実 SQL Server・`SyncSupportRemoteAdoSqlServerRuntimeTests`＝HTTP × 実 SQL Server、SQL Server だけの既知の失敗は `SyncModelEngineTransports.SqlServerOnly`）が流す。転送の軸の目印は `ISyncModelTransportTests`。行の分割は `SyncModelRuntimeTests.ScenarioParts_CoverEveryScenarioOncePerTransport`、割り込みのシナリオの全積は `SyncModelRuntimeTests.InterruptScenarios_CoverEveryPointAndShape`、乱数の種の照合の正規化は `SyncModelRuntimeTests.Family`。仕組みは `tests/QuickER.Tests.Integration/GeneratedRuntime/CLAUDE.md`「同期のモデル（SyncModelRuntimeTests）」。
- 直結の乱数の種・小さい塊の端末・決まった形の行の表 `SyncEngineFixedRows`（影響の地図の名前 `table:SyncEngineFixed`）は `SyncSupportAdoSqliteEngineRuntimeTests`（`tests/QuickER.Tests.Integration/GeneratedRuntime/CLAUDE.md`「同期のエンジンの決まった形の行」）。
- 差分ソースの HTTP の往復（アンカーの Base64・続きの有無・上限・全キー・400 の拒否・サーバー不在）は `SyncHttpRuntimeTests`（in-process Kestrel）。NULL 許容の計算列・サーバーで NULL になった blob・一覧に載らない参照は `SyncEngineFixedRows` の行 `sync-fixture-nullable-computed-column-downloads-and-stays-null`・`sync-fixture-blob-cleared-on-server-is-cleared-locally`・`refresh-unlisted-referring-table-refuses-while-present-locally`。
- 実 SQL Server の固有は `SyncSqlServerRuntimeTests`（NULL を許す一意制約）と `SyncSqlServerHttpRuntimeTests`。
- 値オブジェクト×同期は `SyncEngineFixedRows` の行 `sync-vo-new-row-with-excluded-not-null-blob-downloads`・`sync-vo-local-insert-without-version-uploads`・`sync-vo-server-version-written-back-through-value-object`・`sync-vo-serverwins-restores-row-with-server-version`（SyncVoFixture の端末）と、`table:SyncTriggerRecording`（行 `graph-save-delete-closure-value-object-key`＝キーだけのスタブのグラフ削除の記録）。
- エンジンがローカルの行を読み書きする口 `ILocalRowStore`（Core）と SQLite の実装 `SqliteLocalRowStore` は `CodegenCaseTests` の行 `exec-sync-local-row-store-*`（`src/QuickER.Runtime.Sqlite/CLAUDE.md`「ローカルの行を読み書きする口（SqliteLocalRowStore）」）。
- ローカルの変更の記録と開くたびの確認と DI の登録は `SyncSupportAdoSqliteTriggerRuntimeTests`（行の表 `SyncTriggerRecordingRows`・`SyncTriggerGuardRows`。コンテナなし。端末の組み立ては `SyncTriggerHarness`。仕組みは `tests/QuickER.Tests.Integration/GeneratedRuntime/CLAUDE.md`「同期のトリガーのテスト」）。
- 構成の行列は `SyncConfigMatrixRuntimeTests`（`tests/QuickER.Tests.Integration/GeneratedRuntime/CLAUDE.md`「同期の構成の行列」）、キーのテキストは `SyncKeyTextRuntimeTests`・`CodegenCaseTests` の行 `probe-sync-key-text-decimal-key`、生成は `SyncSupportGenerationTests`・`CodegenCaseTests` の行 `diag-sync-*`・`code-sync-*`。
