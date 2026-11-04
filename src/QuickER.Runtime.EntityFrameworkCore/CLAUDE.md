# QuickER.Runtime.EntityFrameworkCore

EF Core モード（`GenerateEfCoreRepositories`）の固定ランタイムと、対応するテンプレート（`_08_EfCore.scriban`）の規則を置く。
共通規則は `src/QuickER.Runtime/CLAUDE.md`「グラフ保存」「Save フック」「rowversion による楽観排他」「無制限バイナリ列の除外」を参照する。

## EF Core モードの前提

- 既存 Entity をそのまま EF Core に乗せる方言非依存の `QuickErDbContext` と EF Core 版 Repository を生成する。
- DI 登録の差し替え（`AddGenerated{方言}Repositories` と `AddGeneratedEfCoreRepositories`）だけで QuickER 版 Repository と交換できる。
- `GenerateEfCoreRepositories=false` のとき、生成物に EF Core への依存は一切出ない（using を含む）。
- スキーマ作成は DDL 生成の責務で、EF Core は既存スキーマへの接続専用とする（Migrations と `HasColumnType` は範囲外）。
- EF Core 単独のスコープ（EF Core のランタイムパッケージを含む）は、行の組み立て器を出さない。
  - 対象は `SelectMaterializer`・`MapEntity`・型ごとの読み取り・値オブジェクトの速い経路。QuickER 版 Repository のあるスコープだけに出す。
  - EF Core は自分で行を組み立て、生 SQL のエンティティ取得は列ごとの写し `SetColumnValue` で行うので使われない。生 SQL の列の解決の `SelectOrdinals` は残る。
  - ガード：`EfCoreEntitySaveMetadataTests`（パッケージの型に組み立て器のメンバーが無いこと、インラインの生成物が EF Core 単独では出さず QuickER 版 Repository があれば出すこと）。

## グラフ保存の追跡

- `ChangeTracker.TrackGraph` を使わない。
  - `TrackGraph` は親への逆参照を含む全ナビゲーションを辿るため、親と一緒に読んだ子を 1 件削除すると親と兄弟まで Deleted になる。別インスタンスの同じ親を 2 つ持つグラフは追跡の衝突で失敗し、保存を頼んでいない親を書いて RowState も戻さない。
  - 登録は `EntityBaseCore.EnumerateCascadeChildren` の閉包を `Entry(...).State` で 1 件ずつ行う（QuickER 版の `EntityGraphSaver` と同じ走査）。`Entry(...).State` はナビゲーションの先を追跡せず、追跡外の親は SaveChanges の変更検出でも拾われないことを実 DB で確認済み。
  - 削除の根の子孫は、`cascadeDelete` のとき `cascadeSave` に依らず削除対象とする（ADO と同じ）。
- ガード：`BackendParityRuntimeTests` の `graph-include-*`・`graph-delete-root-without-cascade-save-attached`、`BackendParitySqlServerRuntimeTests` の `include-child-delete-keeps-parent-and-siblings`。mutation 実証済み。

## 渡したエンティティのナビゲーションを変えない

- EF Core は追跡したエンティティの参照とコレクションを書き換える（fix-up）。削除したエントリを外すときに単一の子のナビゲーションを null にし、別の親に持った子を消す親のコレクションへ足す。
- そのためグラフ保存は、追跡する前にカスケードの閉包の全ナビゲーション（両方向）を `NavigationSnapshot` に写し、試行の初め・成功の後（`RemoveDeletedChildren` の前）・失敗の後に戻す。
  - 試行の初めに戻さないと、実行戦略のやり直しが前の試行の fix-up で足された行を子としてたどり、消す親の下なら消す。
- 既知の制限：保存の途中で Save フックが変えたナビゲーションも戻る（fix-up とフックの変更を見分けられない。QuickER 版では残る）。docs の EF Core モードの「保存」に明記。
- 削除した子を残った親のカスケードのコレクションから外す共通規則は `src/QuickER.Runtime/CLAUDE.md`「削除した子の外れ方」を参照する（EF Core は `EntityGraphSaver.AcceptChanges` から呼ぶ）。
- ガード：`BackendParityRuntimeTests` の `graph-remove-child-through-root`・`graph-remove-children-one-skipped-by-hook`・`graph-remove-root-with-children`、`BackendParitySqlServerRuntimeTests.DeletedSingleChild_StaysOnItsNavigation`。mutation 実証済み。

## 削除の振る舞いとフックの順

- 生成する `DbContext` は、`ON DELETE CASCADE` 以外の削除の振る舞いを `ClientNoAction` にする。
  - `NoAction` / `SetNull` だと EF Core は追跡中の子の外部キーを null にし、任意の NO ACTION では DB が断るはずの削除を UPDATE で通すため。
  - ガード：`BackendParityRuntimeTests` の `h5-*`・`contact-owner-delete-dependent-modified`・`contact-backup-delete-dependent-modified`・`contact-fallback-delete-dependent-modified`。
- Save フックの文脈は `EfCoreSaveHookContext`（バイナリの書き込みは NotSupported・`ExecuteSql` は対応）。再試行する実行戦略では Before / After が試行の回数だけ走る。
- Before が false を返した行は、Entry の元の値を今の値にそろえてから Unchanged にする。
  - 素の Unchanged は書き換えを元の値へ戻し、飛ばした削除・挿入を変更検出が UPDATE にする（挿入は NotFound）。Detached にすると、EF Core が飛ばした子を親のコレクションから外す。
  - ガード：`BackendParityRuntimeTests` の `h3-edit-then-skip-{update,delete,insert}`（EfCore・RemoteEfCoreOnly）・`graph-remove-children-one-skipped-by-hook`。mutation 実証済み。
- EF Core の削除フックは子孫から順に走り、Added の子孫にも走る。`TrackDeletedNode` が後順で記録し、Added の子孫はエントリを持たないフックだけの記録とする（QuickER 版の DELETE が 0 行で走らせるのと同じ）。

## 書く順への対応

書く順の規則本体（`GraphSaveOrder`）は `src/QuickER.Runtime/CLAUDE.md`「書く順」を参照する。
EF Core で 3 段に従うのは記録の順（＝フックの順）だけで、文の順は 1 回の SaveChanges の中で EF Core が決める。次の 2 つで補う。

- 外部キーの保存先の値を読む（`AdoptStoredForeignKeysAsync`）。
  - 削除を含む保存は、試行ごとに SaveChanges の直前・同じトランザクションの中で、Modified のエントリの外部キー（`entry.Metadata.GetForeignKeys()` から一般に求める。自己参照・複数の外部キーを含む）の保存先の値を射影 1 クエリ（除外 blob は読まない）で読み、`OriginalValue` へ入れる。
  - コンテキストの外で組み立てたエンティティは元の値が今の値なので、EF Core は付け替えを知らず元の親を先に消すため。実行戦略のやり直しは変更追跡を空にして登録し直すので、読むのは試行ごと。
  - 削除と更新を両方含む保存は、フックが無くても明示のトランザクションの経路を通る。
- 一意制約ごとの `HasIndex(…).IsUnique()`。Fluent が出す。EF Core はモデルの一意インデックスを見て、一意な値を手放す削除を、その値を取る更新・挿入より先に流す（無いと、削除する行が子を持つときなどに更新が先に走り、一意制約に断られる）。DB には何も作らない（スキーマは DDL 生成が作り、Migrations / EnsureCreated を使わない前提）。
  - 名前（`HasDatabaseName`）は付けない（文の並べ方に名前は関係しない）。構成列は制約の列の並び。構成列を引き当てられない制約は出さない
  - NULL の値どうしは並べ方に影響しない（EF Core は NULL を同じ値とみなさない）。2 行の一意な値の入れ替えは DB が断る（EF Core の循環の例外にはならない）
  - ガード：`EfCoreUniqueIndexGenerationTests`（一意制約ごと・複合の列の並び・制約の無い表・値オブジェクトの列）・実装先の一致のシナリオ `graph-updated-row-takes-unique-value-of-deleted-row-with-children-updated-first` / `-removed-first`・`GraphSaveEfCoreSqlServerRuntimeTests`・`GraphSaveSqliteFileRuntimeTestsBase.NullUniqueValues_DoNotOrderTheSave`

ガード：`GraphSaveEfCoreSqlServerRuntimeTests`（子を持つ形の J3）、`BackendParityRuntimeTests` の `graph-save-*-keeps-order`・`graph-updated-row-takes-unique-value-of-deleted-row-with-children-*`・`graph-new-row-referenced-by-another-root-*`・`graph-save-new-row-without-shared-value-keeps-order`、`BackendParityRuntimeTests` の `ef-retry-move-order-and-delete-old-customer`、`BackendParitySqlServerRuntimeTests` の `move-stale-note-and-delete-old-gadget`。mutation 実証済み。

## 変わった代替キーを自分で書く

- 代替キー（主キーでない一意制約を外部キーが参照する列＝`HasPrincipalKey`）は、EF Core が自分の `UPDATE` で書かず、追跡中に値が変わると変更検出が「part of a key」で例外にする（Save フックの Before が書き換えた場合）。
- そのため `WriteAlternateKeysAsync`（`_08_EfCore.scriban`）が、代替キーを持つ表の更新する行ごとに `SaveChanges` の前で次を行う。
  - 保存先のキーと行バージョンを読む。行が無い・Optimistic で版が古い・値が同じ、のどれかなら書かない（失敗の報告は `SaveChanges` に任せる）。
  - 変わった列だけを `ExecuteUpdate` で保存と同じトランザクションの中で書く（Optimistic は版も条件にする）。
  - 書いたら版を読み直して並行性トークンの元の値に入れ、代替キーの元の値を今の値にする（変更検出がキーの変更と見ない）。
- 呼ぶ位置：グラフ保存では Before のフックの後・明示のトランザクションの試行ごとの最初（`KeepStoredUnboundedBinaries` と `AdoptStoredForeignKeysAsync` は変更検出を走らせるので、その前）。`UpdateAsync` では代替キーを持つ表のときだけ、実行戦略の中の試行ごとのトランザクション（`UpdateWithAlternateKeysAsync`）。
- ほかの行が参照している値は DB が断る（`NO ACTION`。QuickER 版と同じ）。
- 値が同じなら書かない判定は、外しても緑のまま（同じ値の書き込みは DB が断らず、版を読み直すので観測できない）。
- ガード：`BackendParityRuntimeTests` の `h1-*` の 7 シナリオ（参照あり・なし × `UpdateAsync`・グラフ保存・Before のフックの書き換え〔`ParityHookBehavior.EditKeyThenContinue`＝決まった形のシナリオだけが使う〕・同じ保存に古いコードを参照する依存行を持つ形）、`BackendParitySqlServerRuntimeTests` の `alternate-key-*`（ConcurrencyFixture の kits / kit_parts、実 SQL Server）。mutation 実証済み。

## 保存のやり直しと実行戦略

- グラフ保存とカスケードの `ExecuteDeleteAsync` は `Database.CreateExecutionStrategy().ExecuteAsync` の中で動く。再試行する実行戦略（`EnableRetryOnFailure`）は、戦略の外で開いたトランザクションを拒否するため。
  - グラフ保存は試行ごとに `ChangeTracker.Clear()` してから登録し直し、Save フックのセッションも作り直す。帰結としてフックは試行の回数だけ走る（docs の EF Core 節と Save フック節に制限として明記）。
  - 失敗した試行は、読み込み時の版をエンティティへ戻してから例外を流す。
- 並行性トークンを持たない entry の 0 行は「行なし」とする（`GetDatabaseValuesAsync` で読み直さない。ADO が版の無い表で実在確認しないのと同じ）。単一 `UpdateAsync` は false、グラフ保存は NotFound または `insertWhenUpdateMissing` の INSERT 切替。
- 競合の分類は `DbUpdateConcurrencyException` を `GetDatabaseValuesAsync` で行い、同じ契約（`SaveConflictException`）へ変換する。`ForceOverwrite` は `OriginalValues.SetValues(stored)` で再試行する。やり直しは `MaxConcurrencyAttempts`（10）回で打ち切り、`SaveConflictException`（Unknown）にする。

### やり直しの数え方

- 1 行も当たらなかった文の分類（行が消えたか・版が進んだか）は `IsRowGoneAsync` 1 つで、`UpdateAsync` とグラフ保存（`ResolveConcurrencyFailureAsync`）が共有する。
- 上限は「同じエントリが保存先の値を取り込んでも当たらなかった回数」で数え、判定は分類の後に置く。EF Core は失敗した文を 1 つずつ報告するので、保存全体の回数で数えると、解決の要る行が 10 行以上ある保存が必ず失敗し、最後の試行で行が消えていても NotFound でなく Unknown になる。

### トランザクションと採番値

- フックありの経路は、試行ごとに新しいトランザクションで `SaveChanges` を流し、当たらなかったらロールバックしてから分類して流し直す。1 つのトランザクションの中でやり直すと EF Core の自動セーブポイントに頼ることになり、SQL Server の MARS や `AutoSavepointsEnabled=false` では流れ済みの文が二重になる。
- フックありの経路は `SaveChangesAsync(acceptAllChangesOnSuccess: false)` で流し、採番値（版・計算列）はコミットの後の `AcceptAllChanges` でエンティティへ書く。EF Core は `acceptAllChangesOnSuccess=false` の間、採番値をエンティティへ書かないことを実 SQL Server で確認済み。
  - After は読んだ版を見て、After の例外・コミットの失敗・やり直しのどれでも DB に無い版が残らず、同一インスタンスの再保存が偽競合にならない。

ガード：`BackendParitySqlServerRuntimeTests` の `force-overwrite-10-rows*`・`attempt-limit-then-row-gone-*`・`mars-*-then-missing`、`BackendParityRuntimeTests` の `insert-when-missing-10-rows*`・`deleted-then-missing-without-savepoints`（SQLite で自動セーブポイントを切る）・`ef-retry-*`・`contact-backup-delete-retried-commit`（`ParityBackendOptions.RetryEfCoreFirstCommit`＝再試行する実行戦略と、DbContext ごとに最初のコミットを一時的な失敗にする設定）・`ignored-write-*`（SQLite の `RAISE(IGNORE)` トリガー）、`EfCoreConcurrencyRuntimeTests`（上限のテスト〔UPDATE を必ず 0 行にするインターセプター〕と「SaveAsync: After は旧版を見る・例外でロールバックしても幻の版が残らない」）。mutation 実証済み。

## カスケードの ExecuteDeleteAsync

- 条件に合う根のキーを先に読み（`DeleteByRootKeysAsync`。試行のトランザクションの中で読む）、そのキーで子孫から根まで消す。
  - 子孫を消した後で根の条件を評価し直すと、ナビゲーションを読む条件（`o => o.OrderLines.Any()`）で子孫だけが消えて根が残る。
  - キーは 500 件ずつ、パラメータの OR（偏りの無い木）で送る。1 つの `Contains` にすると、EF Core 10 は既定で 1 つずつのパラメータへ展開して SQL Server の 2,100 の上限を超え、設定や版によっては OPENJSON（互換性レベル 130 以上）を使う。
- 循環するカスケードは、トランザクションを開く前にメタデータだけで断る（`ThrowIfCascadeIsCyclic`。走査の途中の判定は安全網として残す）。
- ガード：`BackendParityRuntimeTests` の `execute-delete-cascade-navigation-predicate`・`execute-delete-cyclic-cascade`・`ef-retry-execute-delete-cascade`、`EfCoreExecuteDeleteRuntimeTests`（循環を断る前に DELETE が 0 本・根 1,200 件）、`BackendParitySqlServerRuntimeTests` の「根がパラメータの上限より多くても、互換性レベル 120 の DB でも動く」（根 2,500 件）。mutation 実証済み。

## 値オブジェクトの列と式の翻訳

- 行バージョンの列が値オブジェクト型のときも、EF Core は反射代入を通らず、Fluent の `.IsRowVersion().HasConversion(...)` の併記のまま動く。
- 値オブジェクトの列にも Fluent の `HasMaxLength` / `HasPrecision` を出す。検証は VO の `Create` が行うが、列のストア型は EF Core がパラメータと `.Value` の CAST に使うため。出さないと既定の `nvarchar(max)`・`decimal(18,2)` になる。
- `.Value` の翻訳（`ValueObjectValueMemberTranslator`）は、列自身のストア型へ CAST する。ガード：`QueryFixtureEfCoreValueCastTests`（SQL Server プロバイダの `ToQueryString`）。mutation 実証済み。

### Unicode でない文字列の列

- Fluent に `IsUnicode(false)` を出す。
  - 判定の正本は正規型の種類（`AnsiString` / `AnsiFixedString`）で、`CanonicalTypeTokenAttacher` が `CSharpTypeInfo.IsNonUnicodeString` を立てる。方言の型表記は読み直さず、型カタログが解析できない列は false。
  - 出す位置は `HasMaxLength` の直後で、値オブジェクトの列にも出す。出さないと EF Core は nvarchar の列と見なし、パラメータと定数を Unicode で送るので、SQL Server は列を変換してから比べる。
  - 対象は SQL Server・Oracle（SQLite の varchar も立つが効果は無い）。MySQL・PostgreSQL の文字列は Unicode の種類なので出ない。
  - 図の方言で決まるので、可搬の図は文字列を Unicode にそろえる運用（docs）。
- ガード：`EfCoreNonUnicodeColumnTests`、`GeneratedFixtureEfCoreNonUnicodeTests`（生成物のままの DbContext の `ToQueryString` で、varchar の列の比較と LIKE が varchar／`N` なし）。mutation 実証済み。

### 値オブジェクトの文字列一致の LIKE

- `ValueObjectStringMethodTranslator` は、受け手が型の対応付けを持たない（値オブジェクトのパラメータ・定数。`term.Contains(c.Name)`）ときは、TSelf の引数（列）の対応付けを当てる。素の string の対応付けでは値オブジェクトを送れないため。
- パターン・エスケープ文字・string の引数は、列の Unicode が既定の string の対応付けと違うときだけ Unicode でない string の対応付けで作る。列の格納型そのものは使わない（固定長だとパターンが空白で詰められ、列より長い引数もあり得る）。Unicode の列の SQL はバイト不変。
- `LikeEscapeBehavior.ConcatenationSkipsNull`（Oracle だけ true）のとき、パラメータのパターンに `IS NOT NULL` を添える。Oracle は NULL の連結が残りの文字列になり、null のパターンが `'%%'` で全行に一致するため。null かどうかは EF Core が .NET の値で判断するので、Oracle に NULL で届く空文字列は全行に一致したまま。
- 判別できないプロバイダは ESCAPE を書き、書かないのは Npgsql だけ。基底の `[Dialect] 2c`（空文字列）は `IS NOT NULL` のガードではない（空文字列ではその判定が SQL から消える）。
- ガード：`QueryFixtureEfCoreLikePatternTests`（派生の DbContext で `IsUnicode(false)` にした列・プロバイダ名の表）、`StringMatchMethodEfCoreRuntimeTests` の受け手のケース、`GeneratedEfCoreOracleRuntimeTests` の null のパターンのケース。mutation 実証済み。

## 主キーの引き当て・生 SQL・タイムアウト

- 主キーで引く操作（`GetByIdAsync` / `DeleteAsync`）のキーは、保持オブジェクトのメンバー参照で式木へ渡す（`BuildKeyPredicate` の `KeyHolder`）。定数で渡すと EF Core が SQL へリテラルで書き込み、キーの値ごとにクエリのコンパイルと DB のプランが増える。ガード：`EfCoreKeyParameterRuntimeTests`。
- 生 SQL（`EfCoreSqlExecutor`）と Save フックの `ExecuteSqlAsync` は、DbContext のコマンドタイムアウトを写す。どちらも接続から自分でコマンドを作るので、写さないと接続の既定のまま走る。`GetCommandTimeout()` が null なら写さない。ガード：`EfCoreCommandTimeoutRuntimeTests`（Docker）。mutation 実証済み。
- EF Core の生 SQL のエンティティ取得は、無制限バイナリ列の除外が効く図でも除外した列を必須にする（`RequireUnboundedBinaryColumns`）。EF Core は保存で全列を書くので、読まずに取得したエンティティの次の保存が保存先の blob を上書きするため。QuickER 版は UPDATE で除外列に触れないので任意のまま。ガード：`BackendParityRuntimeTests` の `raw-sql-*-then-update`。mutation 実証済み。
- 無制限バイナリ列の除外は EF Core（DbSet 経由）には適用されず、全列を読み書きする。リモートのサーバー側で除外列を保つ仕組みは `src/QuickER.Runtime.AspNetCore/CLAUDE.md`「除外した無制限バイナリ列を値として運ばない」を参照する。

## SQLite のロック待ち

- EF Core 版 Repository も、QuickER 版と同じ `PRAGMA busy_timeout` の待ちを持つ。EF Core の SQLite プロバイダは更新と削除を `UPDATE … RETURNING 1` / `DELETE … RETURNING 1` で流すので、別の接続の読み取り（共有のロック）だけで即座に「database is locked」になるため。
- 公開の `SqliteBusyTimeoutInterceptor`（`DbConnectionInterceptor`・`Instance`）が、EF Core が開いた接続の型が `Microsoft.Data.Sqlite.SqliteConnection`（か派生）のときだけ、`PRAGMA busy_timeout` を DbContext のコマンドタイムアウト（設定しなければ新しいコマンドの `CommandTimeout`＝接続文字列の `Default Timeout`）×1000 ms に合わせる。0 とミリ秒で表せない値は `int.MaxValue`（QuickER 版と同じ規則）。
- EF Core のパッケージは Microsoft.Data.Sqlite を参照しないので、型は名前で判定する。
- 生成の `AddGeneratedEfCoreRepositories` は `configureDbContext` の後に `AddInterceptors` で足す（利用者のインターセプターと並んで動く）。
- 値は接続を開いたときに決まる（開いている接続で `SetCommandTimeout` を変えると次の接続から）。
- ガード：`EfCoreSqliteBusyTimeoutRuntimeTests`（実 SQLite）、`SqliteBusyTimeoutInterceptorTests`（値の表・派生の接続・SQLite でない接続にコマンドを作らない・生成の DI のテキスト）。mutation 実証済み。
