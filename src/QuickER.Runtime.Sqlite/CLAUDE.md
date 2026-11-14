# QuickER.Runtime.Sqlite

SQLite 方言エンジン（QuickER 版 Repository の ADO 実装）の固定コードの規則。
方言に依らない共通の規則は `src/QuickER.Runtime/CLAUDE.md` にある。

## 接続ファクトリ（外部キーと busy_timeout）

### Foreign Keys

- 生成 `SqlConnectionFactory` は、接続文字列に `Foreign Keys` キーワードが無いときだけ `True` を補う。`SqliteConnectionStringBuilder.ForeignKeys` が `bool?` なので、null で「未指定」を判別する。明示指定は `False` も含めてそのまま尊重する。
- SQLite は既定で FK を検査しない。補わないと、生成 DDL が宣言した FK が黙って無効化される（存在しない親を参照する子行が入り、親の削除で子が残る）。
- この既定にサンプル（PRAGMA を撤去済み）と統合テストが依存している。外すと「FK 違反が例外にならない」形で静かに回帰する。
- ガード：`SqliteForeignKeyDefaultRuntimeTests`。

### busy_timeout

- 同じファクトリは、接続を開くたびに（プールから戻った接続も含めて）`PRAGMA busy_timeout` を、接続文字列の `Default Timeout`×1000 ms に合わせる（`ApplyBusyTimeout`＝`StateChange` で Open を見る）。
  - `Default Timeout=0` はプロバイダの意味では上限なしなので、`int.MaxValue` に合わせる（SQLite の 0 は「待たない」なので素通しにしない）。
  - Microsoft.Data.Sqlite はロックで待つやり直しを、各文の最初の step（`NextResult`）でしか行わない。`Read()` の後続の step ではやり直さないので、自動コミットの `INSERT` / `UPDATE` / `DELETE` … `RETURNING` は確定の step で、別の接続が読み取り（共有のロック）を持っているだけで即座に「database is locked」になる（ジャーナルの方式に依らない。失敗した文は戻る）。
  - 同期のジャーナルの記録（`SyncJournal.RecordManyAsync` の `RETURNING id`）と、生 SQL のメソッドへ渡した RETURNING がこの形になる。
  - 効く範囲を生 SQL の実行器に絞らない。busy timeout は接続に残る設定で、プールから戻った接続を次に使う経路へ持ち越されるので、絞ると効くかどうかが接続の巡り合わせで決まる。
  - SQLite が待つ間は CancellationToken を見ない。自前の接続ファクトリには効かない。後から流した PRAGMA が勝つ（docs/code-generation に明記）。
- EF Core 版 Repository も同じ待ちを持つ（公開の `SqliteBusyTimeoutInterceptor`）。詳細は `src/QuickER.Runtime.EntityFrameworkCore/CLAUDE.md`。
- ガード：`SqliteBusyTimeoutDefaultRuntimeTests`（既定・`Default Timeout=5`・`0`・プールから戻った接続）、`SyncRuntimeTestsBase` の `JournaledWrite_WaitsForReaderOnAnotherConnection` / `RawSqlReturning_WaitsForReaderOnAnotherConnection`（別の接続の読み取りを持ったまま書き込みが待ち、読み取りの後に成功する。直結と HTTP）、`SqliteRepositoryDialectTests`（生成テキスト）。mutation 実証済み。

## 複数の文を 1 つのトランザクションで流す口（ISqlBatchExecutor）

- SQLite の `SqlExecutor` だけが Core の公開の口 `ISqlBatchExecutor.ExecuteSqlBatchAsync` を実装する（`_04_RepositoryContractCore.scriban` の `repository_dialect == "sqlite"` の分岐）。1 つの接続で `BeginTransactionAsync` → 文ごとに `ExecuteNonQueryAsync` → `CommitAsync`。どれかの文かコミットが失敗したら `SqlTransactions.RollbackQuietlyAsync` と `await using` で巻き戻してから接続を返す。
- 生の `BEGIN … COMMIT` を 1 本の文字列で流すと、失敗したときトランザクションが開いたまま接続がプールへ返る（Microsoft.Data.Sqlite はプールへ返す接続を巻き戻さない）。次にその接続を引いた呼び出しがトランザクションの中で動き、別の接続の書き込みは locked になる。口はこれを避けるためにある。
- 使うのは同期の洗い替えの、表どうしの循環のある範囲の全消しだけ（→ `src/QuickER.Runtime.Sync/CLAUDE.md`「洗い替え」）。パラメータは持たない。
- ガード：`refresh-failed-wipe-in-a-foreign-key-cycle-deletes-nothing`（巻き戻しを外す壊し方で赤）・`refresh-empties-tables-in-a-foreign-key-cycle`。公開 API 面は `QuickER.Runtime.approved.txt`・`QuickER.Runtime.Sqlite.approved.txt`。

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
- ガード：`BinaryColumnAdoRuntimeTests.Stream_Read_Standalone_DoesNotReadBlobOfRowThatReusedRowid`（WAL モードで照会の直後に profile フックから行を入れ替える。rollback ジャーナルでは照会側の共有ロックが残っていて差が出ない）、`SaveHookBinaryWriteSqliteRuntimeTests`（失敗した書き込み 2 通り＝短い Stream と途中で失敗する Stream。成功した書き込みがコミットされることは実装先の一致のシナリオ `hook-after-writes-blobs`）。mutation 実証済み。

## Include のローダ（IncludeLoader）

- Include とカスケード削除の CLR 側のキー照合は `IncludeKeys.Normalize` を通す（`src/QuickER.Runtime/CLAUDE.md`「Include とカスケード削除のキー照合」）。
- SQLite の `IncludeLoader` の 3 か所（親キーの収集・子の辞書・親からの引き当て）は必ずそろえる。1 か所だけ素の値へ戻すと、型が同じ組まで含めて SQLite の Include が全部空になる（片側だけ long になるため）。
- 子の IN 句へ束縛するのは元の値にする。`binary` のキーだけはバイト列、それ以外は従来どおり正規化した値。正規化後の 16 進文字列を束縛すると、列のどの値にも一致しない。
- ガード：`TranslatorParitySqliteRuntimeTests` の `IncludeAcrossKeyTypes_AssemblesRelations`、`BinaryKeyExecutionTests`（binary(4) の主キーの実ファイル SQLite）。mutation 実証済み。

## 式の翻訳（SQLite）

共通の NULL 補償などは `src/QuickER.Runtime/CLAUDE.md`「式の翻訳と NULL 補償」。

- 空・空白の判定は `col = ''` と `trim(col, char(…))`。
- 日付部品：`.Date` は `datetime(date(col))` にする（パラメータの `'YYYY-MM-DD HH:MM:SS'` とそろえる）。`DateTimeOffset` の列は、格納した文字列の位置から取り出す（strftime は UTC へ換算するため）。
- 整数の列と浮動小数点：C# が float・double へ変換して比べる整数の列（式の木で列の側が浮動小数点への変換に包まれたもの。比較の両辺と IN の項目）は `CAST(col AS REAL)` で比べる（`AsFloatingColumn`。EF Core の SQLite のプロバイダと同じ形）。SQLite は INTEGER と REAL を丸めずに比べるので、外すと 2^53 を超える整数の行（2^53+1 と 2^53）が C# と違う答えになる。SQLite に単精度の型は無いので float で比べる式も double で比べる（名前付きクエリでは型トークン float32 も double なので起きない＝docs の既知の割り切り）。束縛は変わらない（SQLite はどの値も AddWithValue で束縛し、列の名前を使わない）。
  - 値の側も同じ：C# が浮動小数点へ変換して比べる整数の値（浮動小数点の列と比べる long・int のパラメータ）は double へ変換してから束縛する（`AsFloatingValue`）。翻訳器は値の側の変換も `Unwrap` で外してから評価するので、外すと long のまま INTEGER で束縛され、2^53 を超える値が REAL の列と丸めずに比べられる。IN は値の側に浮動小数点への変換が無い（簡易 DSL は浮動小数点の列と整数のリストの IN を型の検査が断る）ので掛からない。
  - ガード：`Dsl/SqliteMySql`（単位 sqlite-floating と網羅の単位 coverage-sqlite-floating-plain の整数 × 浮動小数点の原子・NOT NULL の bigint の列の double のリストの IN・網羅の単位の鏡の形＝double の列と int64 の型トークンのパラメータ 2^53+1）。直す前に列の側の比較の 5 件と値の側の 4 件、IN 側だけを外す mutation で IN の 4 件、値の側を外す mutation で 4 件が赤になることを確かめた。値の側は夜間の種（36・76）のパラメータの使い回しで見つかった。
- ガード：`TranslatorParitySqliteRuntimeTests`（述語の表 `TranslatorParityPredicateRows` を実 SQLite で C# の意味論と突き合わせる）。mutation 実証済み（`.Date` と時差の分岐・空と空白の判定）。
