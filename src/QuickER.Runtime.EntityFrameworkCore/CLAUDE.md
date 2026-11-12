# QuickER.Runtime.EntityFrameworkCore

EF Core モード（`GenerateEfCoreRepositories`）の固定ランタイムと、対応するテンプレート（`_08_EfCore.scriban`）の規則を置く。
共通規則は `src/QuickER.Runtime/CLAUDE.md`「グラフ保存」「Save フック」「rowversion による楽観排他」「無制限バイナリ列の除外」を参照する。

## EF Core モードの前提

- 既存 Entity をそのまま EF Core に乗せる `QuickErDbContext` と EF Core 版 Repository を生成する。方言は利用者の `Use*` が選ぶ。
- DI 登録の差し替え（`AddGenerated{方言}Repositories` と `AddGeneratedEfCoreRepositories`）だけで QuickER 版 Repository と交換できる。
- `GenerateEfCoreRepositories=false` のとき、生成物に EF Core への依存は一切出ない（using を含む）。
- スキーマ作成は DDL 生成の責務で、EF Core は既存スキーマへの接続専用とする（Migrations は範囲外）。
  - 列の型（`HasColumnType`）は、プロバイダの既定の対応では書けない列（PostgreSQL の `date`・`timestamp`・`timetz`・`json`・`jsonb`・`xml` とその配列）にだけ出す。DB には何も作らず、パラメータの型を決めるためだけに使う。判定は型マッパー（`src/QuickER.CodeGen.CSharp/CLAUDE.md`「EF Core に教える列の型」）。
- EF Core 単独のスコープ（EF Core のランタイムパッケージを含む）は、行の組み立て器を出さない。
  - 対象は `SelectMaterializer`・`MapEntity`・型ごとの読み取り・値オブジェクトの速い経路。QuickER 版 Repository のあるスコープだけに出す。
  - EF Core は自分で行を組み立て、生 SQL のエンティティ取得は列ごとの写し `SetColumnValue` で行うので使われない。生 SQL の列の解決の `SelectOrdinals` は残る。
  - ガード：`EfCoreEntitySaveMetadataTests`（パッケージの型に組み立て器のメンバーが無いこと）・`CodegenCaseTests` の行 `code-efcore-save-metadata-no-materializer-efcore-only` / `code-efcore-save-metadata-materializer-quicker-only` / `code-efcore-save-metadata-materializer-quicker-and-efcore`（インラインの生成物が EF Core 単独では出さず QuickER 版 Repository があれば出すこと）。

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
- 一括挿入（`BulkInsertAsync`）は、追跡する前にバッチの行だけのナビゲーションを写して空にし（`NavigationSnapshot.TakeRows`＋`Clear`。参照は null・コレクションは同じインスタンスを空にする）、保存の後に成功・失敗とも戻す。
  - 空にしないと、EF Core が同じバッチの行どうしのナビゲーションで外部キーを上書きして保存し（ナビゲーションが勝つ）、呼び出し側の外部キーも書き換える。戻さないと、登録の fix-up が親のコレクションへ子を足したまま残る。
  - 単独の `InsertAsync` は 1 行だけを追跡するので fix-up の相手がいない（触らない）。
  - ガード：`BackendParityRuntimeTests` の `bulk-insert-self-reference-parent-first`（戻さないと親のコレクションに子が残る）・`bulk-insert-navigation-without-foreign-key`・`bulk-insert-navigation-other-foreign-key`（空にしないと外部キーが上書きされる）・`bulk-insert-navigation-rejected-keeps-navigations`（失敗の後に戻す）。mutation 実証済み。

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
EF Core で 3 段に従うのは記録の順（＝フックの順）だけで、文の順は 1 回の SaveChanges の中で EF Core が決める。次の 2 つで補う（削除する行の値を代替キーで取る行だけは、3 段目を 2 つめの SaveChanges に分ける＝「変わった代替キーを自分で書く」）。

- 外部キーの保存先の値を読む（`AdoptStoredForeignKeysAsync`）。
  - 削除を含む保存は、試行ごとに SaveChanges の直前・同じトランザクションの中で、Modified のエントリの外部キー（`entry.Metadata.GetForeignKeys()` から一般に求める。自己参照・複数の外部キーを含む）の保存先の値を射影 1 クエリ（除外 blob は読まない）で読み、`OriginalValue` へ入れる。
  - コンテキストの外で組み立てたエンティティは元の値が今の値なので、EF Core は付け替えを知らず元の親を先に消すため。実行戦略のやり直しは変更追跡を空にして登録し直すので、読むのは試行ごと。
  - 削除と更新を両方含む保存は、フックが無くても明示のトランザクションの経路を通る。
- 一意制約ごとの `HasIndex(…).IsUnique()`。Fluent が出す。EF Core はモデルの一意インデックスを見て、一意な値を手放す削除を、その値を取る更新・挿入より先に流す（無いと、削除する行が子を持つときなどに更新が先に走り、一意制約に断られる）。DB には何も作らない（スキーマは DDL 生成が作り、Migrations / EnsureCreated を使わない前提）。
  - 名前（`HasDatabaseName`）は付けない（文の並べ方に名前は関係しない）。構成列は制約の列の並び。構成列を引き当てられない制約は出さない
  - NULL の値どうしは並べ方に影響しない（EF Core は NULL を同じ値とみなさない）。2 行の一意な値の入れ替えは DB が断る（EF Core の循環の例外にはならない）
  - ガード：`CodegenCaseTests` の行 `model-efcore-unique-constraints-one-unique-index-each` / `model-efcore-unique-constraints-one-unique-index-each-vo`（生成した DbContext のモデルで、一意制約ごとの一意の索引・複合の列の並び・制約の無い表に索引が無い・値オブジェクトの列）・実装先の一致のシナリオ `graph-updated-row-takes-unique-value-of-deleted-row-with-children-updated-first` / `-removed-first`・`GraphSaveEfCoreSqlServerRuntimeTests`・`GraphSaveSqliteFileRuntimeTestsBase.NullUniqueValues_DoNotOrderTheSave`

ガード：`GraphSaveEfCoreSqlServerRuntimeTests`（子を持つ形の J3）、`BackendParityRuntimeTests` の `graph-save-*-keeps-order`・`graph-updated-row-takes-unique-value-of-deleted-row-with-children-*`・`graph-new-row-referenced-by-another-root-*`・`graph-save-new-row-without-shared-value-keeps-order`、`BackendParityRuntimeTests` の `ef-retry-move-order-and-delete-old-customer`、`BackendParitySqlServerRuntimeTests` の `move-stale-note-and-delete-old-gadget`。mutation 実証済み。

## 変わった代替キーを自分で書く

- 代替キー（主キーでない一意制約を外部キーが参照する列＝`HasPrincipalKey`）は、EF Core が自分の `UPDATE` で書かず、追跡中に値が変わると変更検出が「part of a key」で例外にする（Save フックの Before が書き換えた場合）。
- そのため `WriteOwnColumnsAsync`（`_08_EfCore.scriban`）が、代替キーを持つ表の更新する行ごとに `SaveChanges` の前で次を行う。
  - 保存先のキーと行バージョンを読む。値が同じなら書かない。行が無い・Optimistic で版が古いなら書かず、下の「最初の当たらなかった行で止める」に従って知らせる。
  - 変わった列だけを `ExecuteUpdate` で保存と同じトランザクションの中で書く（Optimistic は版も条件にする）。値はホルダー経由のパラメータで送り、リテラルを埋め込まない。
  - 書いたら版を読み直して並行性トークンの元の値に入れ、代替キーの元の値を今の値にする（変更検出がキーの変更と見ない）。
- 最初の当たらなかった行で止める：行を保存の順（記録の順＝`GraphSaveOrder` の順）に書き、行が無い・Optimistic で版が古い行が最初に出たところで、その 1 行の `OwnWriteMissedException` を投げて後ろの行の代替キーを書かない（`SaveChanges` が書く行かに依らない）。呼び出し側は `SaveChanges` の `DbUpdateConcurrencyException` と同じに扱う（ロールバック → 試行の記録を戻す → 分類）。投げる前に、その行と書いていない後ろの行の代替キーの元の値を今の値にする（分類が走らせる変更検出がキーの変更と見ない）。
  - 理由：代替キーの書き込みは `SaveChanges` のどの文よりも先に走る。当たらなかった行を飛ばして続けると、その行がまだ持つ代替キーを後ろの行が取る書き込みが先に一意制約に断られ、同じ順に書く QuickER 版なら前の行の `SaveConflictException`（NotFound / Modified）を報告する保存が DB の例外になる（docs の EF Core モードの「版が古ければ書かずに SaveConflictException」に反する）。
  - 消えた行を insertWhenUpdateMissing で挿入へ切り替える行が 2 行以上あると、1 回の試行で 1 行ずつ切り替える。やり直しの上限は行ごとに数えるので掛からない（`SaveChanges` も当たらなかった文を 1 つずつ報告する）。
  - 代替キーを持たない表の行は、この書き込みを通らないので、`SaveChanges` の順で報告される。そうした行が前にあって後ろの代替キーの書き込みが断られる形（どの実装先でも断られる順の保存に、他者が消した行がある）は、QuickER 版が前の行の競合を、EF Core 版が代替キーの断りを報告する。docs に受け入れた差として書き、決まった形のケース `alternate-key-gone-part-then-taker-first`（kit_parts の消えた行）が両方の値を固定する。実装先の一致の乱数の保存はこの形を作らない（作らない形の宣言 `BackendParitySqlServerSaveNotGenerated`）。
  - ガード：`BackendParitySqlServerRuntimeTests` の `alternate-key-taken-from-released-with-stale-optimistic`（手放す行の版が古いと、取る行より先に競合を報告する）と、`own-write-*-gone-graph-save-insert-when-missing-11-rows`（書く列の無い 3 つの表の形で、消えた 11 行を 1 試行 1 行ずつ挿入へ切り替え、全行が挿入される）、夜間の `GeneratedRuntime/ParitySqlServerSaves`（手放しと取りの役）。mutation 実証済み（`SaveChanges` が書く行の当たらなかった行を黙らせる・最後まで回してから投げる、のどちらでも、そのケースと夜間の 6 種が赤）。
- 読み直した版は、その試行のトランザクションの中にしか無い。そのため書き換える前の版を試行の記録（`OwnWriteAttempt`）に積み、試行が 0 行でロールバックしたら、分類（`IsRowGoneAsync`・`ResolveConcurrencyFailureAsync`）の前に逆順で元の版へ戻す（グラフ保存の while と `UpdateWritingOwnColumnsAsync` の while。1 つめと 2 つめの文脈で 1 つの記録を共有する）。コミットしたら記録を捨てる。
  - 戻さないと、同じ保存のほかの行の理由（消えた行の insertWhenUpdateMissing の挿入への切り替えなど）でやり直した試行が、DB に無い版を元の値として「版が進んだ」と判断して代替キーを書かず、`WHERE version = 読み直した版` が 0 行になって偽の競合（Modified）になる。
  - 分類の前に戻すのは、ForceOverwrite の保存先の値の採用（`OriginalValues.SetValues`）を後勝ちにするため。記録に入るのは、その試行で代替キーの UPDATE が通った（行のロックを持つ）行だけなので、同じ試行でその行が採用に回る組は作れず、順序の違いは観測できない（設計の理由で、守りは置かない）。
  - 代替キーの元の値（今の値にした印）は戻さない。次の試行の書き込みは、書くかどうかを保存先の値（ロールバックで戻った値）と今の値で決めて書き直し、変更検出の前に印を今の値にし直すので、戻しても上書きされて意味が無い。
  - 実行戦略の再試行は `ChangeTracker.Clear()` から登録し直すので、記録の外で元に戻る。
- 呼ぶ位置：グラフ保存では Before のフックの後・明示のトランザクションの試行ごとの最初（`KeepStoredUnboundedBinaries` と `AdoptStoredForeignKeysAsync` は変更検出を走らせるので、その前）。`UpdateAsync` では代替キーを持つ表のときだけ、実行戦略の中の試行ごとのトランザクション（`UpdateWithAlternateKeysAsync`）。
- 同じ保存で消す行がまだ持つ値を代替キーで取る更新は、削除の後に書く（`HeldBackContextAsync`）。
  - SaveChanges の前に書くと、消す前の行とぶつかって一意制約に断られる。1 回の SaveChanges の中では文の間に何も挟めず、一意インデックスの並べ替えはキーの列に効かない。
  - そのため、3 段目（`GraphSaveOrder.HeldBack` から歩く行）を、接続とトランザクションを共有する 2 つめの文脈（`SetDbConnection`＋試行のトランザクションごとの `UseTransaction`）で追跡し、1 つめの SaveChanges の後に、代替キー → SaveChanges の順で書く。3 段目の新しい子が新しい値を参照しても通る。
  - 2 つめの文脈（`HeldBackContext`）は、破棄の前にトランザクションから外し、自分の接続を戻す。戻し方は接続の出どころ（オプションの `RelationalOptionsExtension`）で 3 つに分ける。ファクトリが両方の文脈に同じ接続を渡すなら差し替えない。
    - 接続文字列で構成した文脈（`Connection` が null で `ConnectionString` がある）：`SetDbConnection(null, true)` を挟んでから `SetConnectionString` で接続文字列を戻す。EF Core は別の接続を付けた時点で自分が作った接続を破棄し接続文字列も消すので（SQL Server の `SqlConnection` は破棄で接続文字列を失う。EF Core 10 で実測）、自分の接続を付け直すとプールの文脈が次の貸し出しで開けない。`SetConnectionString` だけを呼ぶと、その時点の接続＝共有の接続の文字列を書き換えようとする（開いていれば例外、閉じていれば利用者の接続を書き換える）。
    - オプションで渡した接続（`Connection` がある）：`IsConnectionOwned` のとおりの所有で戻す。`contextOwnsConnection: true` で渡した接続は差し替えた時点で EF Core が破棄するので戻す側では救えない（この保存の後に文脈ごと破棄されるので、使い回さない限り観測できない。XmlDoc に書く）。
    - どちらも無い（ファクトリが実行時に `SetDbConnection` で付けた接続）：持ち主でないとして戻す。持ち主かは公開の API で読めないので、利用者の接続を破棄しない側に倒す（`contextOwnsConnection: true` で付けた接続は、付けた側が破棄する）。持ち主として戻すと、文脈の破棄が利用者の接続を破棄し、その接続を渡された次の文脈が開けない。
    - 作った文脈をコンストラクタが投げたら、破棄して投げ直す（プールへ戻らなくなる）。
    - ガード：`BackendParityRuntimeTests` の `graph-updated-row-takes-alternate-key-of-deleted-row-user-connections`（`ParityScenario.EfCoreUserConnections`＝利用者が持つ 2 本の接続を交互に渡すファクトリ）と、`BackendParitySqlServerRuntimeTests` の `alternate-key-taken-from-deleted-twice-*`（接続文字列の文脈を使い回すファクトリ＝プールの形・実行時に付けた接続・オプションで渡した接続の対照。値オブジェクトありの生成コードは本物のプールを断るので、破棄を飛ばして同じインスタンスを貸し出し直す派生で作る）。mutation 実証済み（接続文字列の文脈を今の戻し方へ戻すと接続文字列の値だけが、実行時に付けた接続を持ち主として戻すと実行時に付けた接続の値だけが赤）。
  - 分けるかの判定は `GraphSaveOrder.TakesDeletedValue`（後回しの判定と同じ本体を、モデルの代替キーの列だけに絞る）。代替キーでない一意な値の後回しは分けず、1 回の SaveChanges の中で `HasIndex` が並べる（分けると下の `HasIndex` のガードが効かなくなる）。
  - 両方の文脈のエントリはコミットまで保留のまま（`acceptAllChangesOnSuccess: false`）。そのため、0 行の文の分類とやり直し（`IsRowGoneAsync`）・実行戦略の試行の組み直し・採番値のコミット後の反映は、1 つの文脈のときと同じに効く。競合のロールバックの後は、2 つめの文脈をトランザクションから外してから保存先を読む。
- 代替キーを `SaveChanges` の前に書くので、保存が挿入する行（新しい行・insertWhenUpdateMissing の挿入）は、後ろに渡した更新する行が手放す代替キーを取れる。グラフの順に書く QuickER 版は断る。docs に受け入れた差として書き、実装先の一致の乱数の保存はこの順を作らず、決まった形のケース `alternate-key-new-takes-released-taker-first`・`alternate-key-gone-insert-takes-released-taker-first` が両方の値を固定する（既知の違いの一覧の Accepted・G3-B）。
- ほかの行が参照している値は DB が断る（`NO ACTION`。QuickER 版と同じ）。同じ保存で参照する行を別の行へ向け直しても、代替キーの書き込みがその行より先に走るので断られる。QuickER 版はグラフの順に書くので、向け直す行を先に渡せば通る。docs に受け入れた差として書き、決まった形のケース `alternate-key-part-moved-off-released-taker-first`・`alternate-key-part-moved-off-released-releaser-first` が両方の値を固定する（既知の違いの一覧 `Known` の Accepted・H2-D1）。実装先の一致の乱数の保存は、1 段目で code を手放す kit に部品を持たせない。
- 値が同じなら書かない判定は、外しても緑のまま（同じ値の書き込みは DB が断らず、版を読み直すので観測できない）。
- ガード：`BackendParityRuntimeTests` の `h1-*` の 10 シナリオ（参照あり・なし × `UpdateAsync`・グラフ保存・Before のフックの書き換え〔`ParityHookBehavior.EditKeyThenContinue`＝決まった形のシナリオだけが使う〕・同じ保存に古いコードを参照する依存行を持つ形・同じ保存の紹介先の顧客〔消えた・消えた＋insertWhenUpdateMissing・Before が飛ばす〕）、`BackendParitySqlServerRuntimeTests` の `alternate-key-*`（ConcurrencyFixture の kits / kit_parts、実 SQL Server）と `alternate-key-*-with-*`（代替キーの行の形 × 同じ保存のほかの行 × 並行性の扱いの全積。リモートの EF Core のサーバーでも流す）、夜間の `GeneratedRuntime/ParitySqlServerSaves`（乱数の保存）。mutation 実証済み（試行の記録の復元を外すと、Optimistic × insertWhenUpdateMissing の 4 ケースが EF Core とリモートで赤。2 つめの文脈の記録を捨てると taken-from-deleted の 2 ケースだけが赤）。

## SaveChanges が書く列を持たない行

- EF Core は、書ける列（キーでなく `AfterSaveBehavior` が `Save` の列。リモートの要求の中では除外した無制限バイナリ列も除く＝`SaveChangesWritesColumn`）を持たない Modified のエントリに `UPDATE` を送らない。キーだけの表・キーと版だけの表・代替キーと版だけの表が当たる。
- そうした行も `WriteOwnColumnsAsync` が書く（`WritesOwnColumns`＝代替キーがあるか書ける列が無い）。変わった代替キーが無ければ主キーを自分自身へ代入する（QuickER 版の `SET k = k` と同じ。版のある表では版が進む）。
  - 保存先の行が無い・Optimistic で版が古い・文が 0 行のときは、`OwnWriteMissedException` で知らせ、呼び出し側は `SaveChanges` の `DbUpdateConcurrencyException` と同じに扱う（ロールバック → 試行の記録を戻す → 分類。`UpdateAsync` は false か競合、グラフ保存は NotFound・挿入への切り替え・競合・ForceOverwrite の採用）。`DbUpdateConcurrencyException` を自分で作るには EF Core の内部 API（`IUpdateEntry`）が要るので、専用の例外にした。
  - 読み直した版は試行の記録に「エンティティへ渡す」印つきで積み、コミットの後（`AcceptAllChanges` の前）にエンティティへ書く（`SaveChanges` が書く行の版は `AcceptAllChanges` が渡す）。
  - 自分で書いた行はグラフ保存の戻り値に数える（`SaveChanges` は数えない）。
- 書かないと、消えた行の `UpdateAsync` が `true` を返し、古い版を見逃し、グラフ保存が NotFound も挿入への切り替えもせず、代替キーを書いた版がエンティティへ届かず次の保存が偽の競合になる。
- ガード：`BackendParitySqlServerRuntimeTests` の `own-write-*`（tags・stamps・markers × fresh／stale／gone × UpdateAsync／グラフ保存／insertWhenUpdateMissing のグラフ保存。リモートの EF Core のサーバーでも流す）。mutation 実証済み（書ける列の無い行を自分で書かないと stamps・markers の 13 ケースだけが、版をエンティティへ渡さないと、版を持つ tags・stamps の行を書く 8 ケース〔fresh の 3 経路と gone の insertWhenUpdateMissing〕だけが赤）。
- 削除の後に書くガード：`BackendParityRuntimeTests` の `graph-updated-row-takes-alternate-key-of-deleted-row`・`-with-new-dependent`（後から書くと新しい子の挿入が先に走る）・`ef-retry-updated-row-takes-alternate-key-of-deleted-row`（実行戦略の試行の組み直し）、`BackendParitySqlServerRuntimeTests` の `alternate-key-taken-from-deleted-kit-*`（部品を持つ kit の削除・2 つめの文脈の 0 行の競合の Optimistic と ForceOverwrite）。mutation 実証済み。

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
- ガード：`CodegenCaseTests` の行 `model-efcore-non-unicode-*`（生成した DbContext のモデルの `IsUnicode`）・`code-efcore-non-unicode-follows-max-length*`（Fluent の鎖で `HasMaxLength` の次に `IsUnicode(false)`）、`GeneratedFixtureEfCoreNonUnicodeTests`（生成物のままの DbContext の `ToQueryString` で、varchar の列の比較と LIKE が varchar／`N` なし）。mutation 実証済み。

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
