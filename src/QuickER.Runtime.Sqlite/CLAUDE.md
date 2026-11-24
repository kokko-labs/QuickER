# QuickER.Runtime.Sqlite

SQLite 方言エンジン（QuickER 版 Repository の ADO 実装）の固定コードの規則。
方言に依らない共通の規則は `src/QuickER.Runtime/CLAUDE.md` にある。

## 接続ファクトリ（外部キーと busy_timeout）

### Foreign Keys

- 生成 `SqlConnectionFactory` は、接続文字列に `Foreign Keys` キーワードが無いときだけ `True` を補う。`SqliteConnectionStringBuilder.ForeignKeys` が `bool?` なので、null で「未指定」を判別する。明示指定は `False` も含めてそのまま尊重する。
- SQLite は既定で FK を検査しない。補わないと、生成 DDL が宣言した FK が黙って無効化される（存在しない親を参照する子行が入り、親の削除で子が残る）。
- この既定にサンプル（PRAGMA を撤去済み）と統合テストが依存している。外すと「FK 違反が例外にならない」形で静かに回帰する。
- ガード：`ForeignKeyDefaultAdoSqliteRuntimeTests`。

### busy_timeout

- 同じファクトリは、接続を開くたびに（プールから戻った接続も含めて）`PRAGMA busy_timeout` を、接続文字列の `Default Timeout`×1000 ms に合わせる（`ApplyBusyTimeout`＝`StateChange` で Open を見る）。
  - `Default Timeout=0` はプロバイダの意味では上限なしなので、`int.MaxValue` に合わせる（SQLite の 0 は「待たない」なので素通しにしない）。
  - Microsoft.Data.Sqlite はロックで待つやり直しを、各文の最初の step（`NextResult`）でしか行わない。`Read()` の後続の step ではやり直さないので、自動コミットの `INSERT` / `UPDATE` / `DELETE` … `RETURNING` は確定の step で、別の接続が読み取り（共有のロック）を持っているだけで即座に「database is locked」になる（ジャーナルの方式に依らない。失敗した文は戻る）。
  - 生 SQL のメソッドへ渡した RETURNING がこの形になる。
  - 効く範囲を生 SQL の実行器に絞らない。busy timeout は接続に残る設定で、プールから戻った接続を次に使う経路へ持ち越されるので、絞ると効くかどうかが接続の巡り合わせで決まる。
  - SQLite が待つ間は CancellationToken を見ない。自前の接続ファクトリには効かない。後から流した PRAGMA が勝つ（docs/code-generation に明記）。
- EF Core 版 Repository も同じ待ちを持つ（公開の `SqliteBusyTimeoutInterceptor`）。詳細は `src/QuickER.Runtime.EntityFrameworkCore/CLAUDE.md`。
- ガード：`BusyTimeoutDefaultAdoSqliteRuntimeTests`（既定・`Default Timeout=5`・`0`・プールから戻った接続）、`BusyTimeoutDefaultAdoSqliteRuntimeTests.RawSqlReturning_WaitsForReaderOnAnotherConnection`（別の接続の読み取りを持ったまま生 SQL の RETURNING が待ち、読み取りの後に成功する）、`SqliteRepositoryDialectTests`（生成テキスト）。mutation 実証済み。

### 接続ファクトリを包む型（ObservedSqlConnectionFactory）

- `ObservedSqlConnectionFactory` は、内側の `ISqlConnectionFactory` が返す接続の `StateChange` にハンドラを足して返す、同期を知らない汎用の包み。ハンドラは内側が自分で足した購読（上の `busy_timeout`）の後に走り、接続を開く間にハンドラが投げた例外は `Open` から出る。
  - 同期の開くたびの確認（`SyncLocalTrackingGuard`）は `new ObservedSqlConnectionFactory(inner, new SyncLocalTrackingGuard(tables).OnStateChange)` の形で使う。生成の `AddGeneratedSyncEngine` が、ローカルの接続ファクトリの登録をこの形の包みに置き換える（→ `src/QuickER.Runtime.Sync/CLAUDE.md`「開くたびの確認（SyncLocalTrackingGuard）」「生成と DI」）。
- 置き場の理由：ドライバの型（`SqliteConnection`）を名指しするコードは SQLite のエンジンの側に置く。同期の固定のエンジンは ADO のドライバに依存せず、パッケージ参照の形の生成コードは ADO のドライバを直接参照しないため。
- 出す条件は `sync_support || runtime_package_export`（同期を生成する構成か、パッケージ用ソース）。同期を使わない構成の生成物には出ない。
- ガード：`SyncSupportAdoSqliteTriggerRuntimeTests` が流す `table:SyncTriggerGuard`（行 `busy-timeout-is-set-before-the-check`・`custom-connection-factory-is-wrapped`）、パッケージ参照の形の生成の行 `compile-package-sync-support-*`。

## ローカルの行を読み書きする口（SqliteLocalRowStore）

- Core の契約 `ILocalRowStore`（→ `src/QuickER.Runtime/CLAUDE.md`「接続を指定して行を読み書きする契約（ILocalRowStore）」）の SQLite の実装。同期のエンジンがローカルの行を読み書きする（生成の `AddGeneratedSyncEngine` が、包んだ接続ファクトリでこれを作ってエンジンを組む）。ドライバの型（`SqliteConnection`・`SqliteException`）を名指しするので SQLite のエンジンの側に置く（`ObservedSqlConnectionFactory` と同じ理由）。
- エンジン用の接続（`CreateDedicatedConnection`）は、接続ファクトリが返した開く前の接続の接続文字列を `Pooling=False` に書き換えて作る。
  - ファクトリを通すので、ファクトリが `StateChange` に足した購読（`busy_timeout`・`ObservedSqlConnectionFactory` の開くたびの確認）が効く。ファクトリの外で作ると効かない。
  - プールしないので、閉じるとファイルを放す。
  - ファクトリが開いた接続を返したら、破棄して `InvalidOperationException`。
- 一意な値の重なり（`IsUniqueViolation`）は、エラーコード 19（制約）で、拡張のコードが主キー 1555 か一意制約 2067 のときだけ真。NOT NULL 1299・CHECK 275・外部キー 787 と、ほかのエラーは偽。
- 行の操作は `EntitySaveMetadata` の SQL と束縛（`SelectByIdSql`・`ExistsByIdSql`・`InsertSql`・`UpdateSql`・`DeleteSql`・`BindKeyParameter` ほか）をそのまま使い、行の読みは `MapEntity` を使う。トランザクションのオブジェクトは使わず、呼び出し側が SQL の文で始めた中に入る。SQLite 以外の接続は `ArgumentException`。
  - 挿入は除外した無制限バイナリ列も含めて全列を書く。NOT NULL の除外列が null の挿入は NOT NULL 違反になる（呼び出し側が先に埋める）。
  - 更新は除外した無制限バイナリ列に触れない。その列に空でない値を持つエンティティの更新は、何も書かずに `InvalidOperationException`。
  - 行バージョンの列は SQLite では普通の列で、挿入も更新もエンティティの値を書く。計算列・Repository が外す採番列は書かない。
- 除外した無制限バイナリ列の書き（`WriteBinaryAsync`）は、セーブポイントの中で `zeroblob` で長さちょうどの領域を取り、`SqliteBlob` で区切りごとに写す。失敗したらセーブポイントだけを巻き戻し（列は元のまま・呼び出し側のトランザクションは続く）、長さの規則は Repository の Stream アクセサと同じ。読み（`ReadBinaryAsync`）と長さ（`ReadBinaryLengthAsync`）は呼び出し側のトランザクションの中で読む。キーの列挙（`ListKeysAsync`）と生のキーの変換（`ConvertKey`）は行の読みと同じ変換を使う。
- 出す条件は `sync_support || runtime_package_export`（`Runtime.Sqlite.g.cs`）。同期を使わない構成の生成物には出ない。
- ガード：`CodegenConfig/Cases` の行 `exec-sync-local-row-store-on-sqlite`（実際に SQLite で流す）・`exec-sync-quoted-names-local-row-store-on-sqlite`（引用符を含む名前）・`exec-sync-local-row-store-value-object-key-on-sqlite`・`exec-sync-local-row-store-string-key-on-sqlite`・`exec-sync-local-row-store-without-excluded-columns-on-sqlite`。公開 API 面は `QuickER.Runtime.Sqlite.approved.txt`。mutation 実証済み（`Pooling=False` にしない・ファクトリを通さずに同じ接続文字列で接続を作る・主キーの重なりを一意な値の重なりと答えない・NOT NULL の違反を一意な値の重なりと答える、のどれでも生成の行が赤）。

## 複数の文を 1 つのトランザクションで流す口（ISqlBatchExecutor）

- 利用者向けの口。SQLite の `SqlExecutor` だけが Core の公開の口 `ISqlBatchExecutor.ExecuteSqlBatchAsync` を実装する（`_04_RepositoryContractCore.scriban` の `repository_dialect == "sqlite"` の分岐。包んだ実行器は呼び出しを自分で転送しない限り実装しない）。
- 複数の文を、1 つの接続の 1 つのトランザクションで順に流す：プロバイダのトランザクション（`BeginTransactionAsync`）を張り、文ごとに `ExecuteNonQueryAsync` で流す。全部通れば最後に 1 回 `CommitAsync` して文ごとの影響行数を返し、どれかの文かコミット（遅延した外部キーの検査など）が失敗したら `SqlTransactions.RollbackQuietlyAsync` で全部を巻き戻してから投げ直し、`await using` で接続を返す。文の並びが空なら `ArgumentException`。パラメータは持たない（決まった文のための口）。
- 生の `BEGIN … COMMIT` を `ExecuteSqlAsync` に渡す形にしない：失敗したときトランザクションが開いたまま接続がプールへ返る（Microsoft.Data.Sqlite はプールへ返す接続を巻き戻さない）。
- 壊すと：生の `BEGIN … COMMIT` で流して巻き戻さないと、失敗の後に接続がトランザクションとロックを持ったままプールへ戻り、ほかの接続の書き込みが `database is locked` になり、次にその接続を引いた呼び出しがそのトランザクションの中で動く。catch で巻き戻さずにコミットすると、途中の文の失敗で前の文の書き込みが残る。
- catch の明示の巻き戻し（`SqlTransactions.RollbackQuietlyAsync`）は、トランザクションの失敗経路はこの関数だけを呼ぶという規則（`src/QuickER.Runtime/CLAUDE.md`「トランザクション失敗経路」）を保つために残す。外しても、今の Microsoft.Data.Sqlite では `await using` の破棄と接続を閉じる処理がトランザクションを巻き戻すので、下の行は赤にならない。
- ガード：`CodegenCaseTests` の行 `exec-sqlite-batch-executor-rolls-back-and-leaves-no-transaction`（途中の文が失敗すると前の文の書き込みも残らず、プールへ戻した接続にトランザクションが残らない。全部の文が通るバッチは全部の書き込みを残す。赤になる壊し方：生の `BEGIN … COMMIT` で流して巻き戻さない・catch で巻き戻さずにコミットする）。公開 API 面は `QuickER.Runtime.approved.txt`・`QuickER.Runtime.Sqlite.approved.txt`。

## INSERT コマンドの使い回し

- SQLite 版の BulkInsert はコマンドを 1 回だけ用意して、値だけ差し替える（`EntitySaveMetadata.RebindInsertParameters`）。行ごとに `new SqliteCommand` すると毎行 parse / plan になり、洗い替えの速度が落ちる。
- 性能の doc は挙動の doc より検証されにくく、静かに嘘になる。XmlDoc の主張（コマンドを使い回す）と実装を一致させ続ける。
- 共通の契約は `src/QuickER.Runtime/CLAUDE.md`「BulkInsert の契約」。

## 無制限バイナリ列のストリーミング

共通の契約（Stream アクセサ・長さの契約）は `src/QuickER.Runtime/CLAUDE.md`「無制限バイナリ列の除外」。

- エンジンは `SqliteBlob`＋`zeroblob` で書く（rowid 前提。QuickER の SQLite DDL は WITHOUT ROWID を生成しない）。
- Stream の長さの検査を外すと、先に確保した zeroblob の残りを 0 で埋めたまま true でコミットする（例外にならない）。
- 単独の Read は、rowid の照会から blob の読み取りまでを 1 つの読み取りトランザクション（deferred）で行う。外すと、その間に別の接続が最大 rowid の行を消して挿入した行（同じ rowid を再利用する）の blob を返す。
- 参加モード（Save フックの context）の書き込みはセーブポイント（`quicker_unbounded_binary_write`）の中で行い、失敗したらそこまで戻して解放する（`UnboundedBinaryColumnEngine.RollbackEnlistedWriteQuietlyAsync`）。宣言の長さの zeroblob を確保してから写すので、短い・途中で失敗する Stream では 0 と途中のバイトが残る。フックが例外を握りつぶすと、保存がそのままコミットしてしまうため。
- `SqliteBlob` には DB 名と表名を分けて渡す（`SqliteBlob(connection, db, table, …)`。分け方は `EntitySaveMetadata.SplitTableName`＝`QuoteTableName` と同じ写しで、スキーマの無い名前は `main`）。分けない名前を渡すと、`sales.regions` を「`main` の中の `sales.regions` という名前の表」と探して no such table で必ず失敗する（ほかの文は `"sales"."regions"` を添付した DB の表として読む）。ガード：実装先どうしの一致のシナリオ `key-shape-dotted-blob-stream`（表 `main.regions`。QuickER 版とリモート〔QuickER 版のサーバー〕）。mutation 実証済み。
- ガード：`BinaryColumnAdoSqliteRuntimeTests.Stream_Read_Standalone_DoesNotReadBlobOfRowThatReusedRowid`（WAL モードで照会の直後に profile フックから行を入れ替える。rollback ジャーナルでは照会側の共有ロックが残っていて差が出ない）、`SaveHookAdoSqliteBinaryWriteRuntimeTests`（失敗した書き込み 2 通り＝短い Stream と途中で失敗する Stream。成功した書き込みがコミットされることは実装先の一致のシナリオ `hook-after-writes-blobs`）。mutation 実証済み。

## Include のローダ（IncludeLoader）

- Include とカスケード削除の CLR 側のキー照合は `IncludeKeys.Normalize` を通す（`src/QuickER.Runtime/CLAUDE.md`「Include とカスケード削除のキー照合」）。
- SQLite の `IncludeLoader` の 3 か所（親キーの収集・子の辞書・親からの引き当て）は必ずそろえる。1 か所だけ素の値へ戻すと、型が同じ組まで含めて SQLite の Include が全部空になる（片側だけ long になるため）。
- 子の IN 句へ束縛するのは元の値にする。`binary` のキーだけはバイト列、それ以外は従来どおり正規化した値。正規化後の 16 進文字列を束縛すると、列のどの値にも一致しない。
- ガード：`TranslatorParityAdoSqliteRuntimeTests` の `IncludeAcrossKeyTypes_AssemblesRelations`、`BinaryKeyExecutionTests`（binary(4) の主キーの実ファイル SQLite）。mutation 実証済み。

## 式の翻訳（SQLite）

共通の NULL 補償などは `src/QuickER.Runtime/CLAUDE.md`「式の翻訳と NULL 補償」。

- 空・空白の判定は `col = ''` と `trim(col, char(…))`。
- 日付部品：`.Date` は `datetime(date(col))` にする（パラメータの `'YYYY-MM-DD HH:MM:SS'` とそろえる）。`DateTimeOffset` の列は、格納した文字列の位置から取り出す（strftime は UTC へ換算するため）。
- 整数の列と浮動小数点：C# が float・double へ変換して比べる整数の列（式の木で列の側が浮動小数点への変換に包まれたもの。比較の両辺と IN の項目）は `CAST(col AS REAL)` で比べる（`AsFloatingColumn`。EF Core の SQLite のプロバイダと同じ形）。SQLite は INTEGER と REAL を丸めずに比べるので、外すと 2^53 を超える整数の行（2^53+1 と 2^53）が C# と違う答えになる。SQLite に単精度の型は無いので float で比べる式も double で比べる（名前付きクエリでは型トークン float32 も double なので起きない＝docs の既知の割り切り）。束縛は変わらない（SQLite はどの値も AddWithValue で束縛し、列の名前を使わない）。
  - 値の側も同じ：C# が浮動小数点へ変換して比べる整数の値（浮動小数点の列と比べる long・int のパラメータ）は double へ変換してから束縛する（`AsFloatingValue`）。翻訳器は値の側の変換も `Unwrap` で外してから評価するので、外すと long のまま INTEGER で束縛され、2^53 を超える値が REAL の列と丸めずに比べられる。IN は値の側に浮動小数点への変換が無い（簡易 DSL は浮動小数点の列と整数のリストの IN を型の検査が断る）ので掛からない。
  - ガード：`Dsl/SqliteMySql`（単位 sqlite-floating と網羅の単位 coverage-sqlite-floating-plain の整数 × 浮動小数点の原子・NOT NULL の bigint の列の double のリストの IN・網羅の単位の鏡の形＝double の列と int64 の型トークンのパラメータ 2^53+1）。直す前に列の側の比較の 5 件と値の側の 4 件、IN 側だけを外す mutation で IN の 4 件、値の側を外す mutation で 4 件が赤になることを確かめた。値の側は夜間の種（36・76）のパラメータの使い回しで見つかった。
- ガード：`TranslatorParityAdoSqliteRuntimeTests`（述語の表 `TranslatorParityPredicateRows` を実 SQLite で C# の意味論と突き合わせる）。mutation 実証済み（`.Date` と時差の分岐・空と空白の判定）。
