# QuickER.Runtime

生成コードのランタイム（スキーマに依存しない固定コード）のうち、方言に依らない共通の規則を書く。
ADO の 2 方言が共有する `EntitySaveMetadata` の規則もここに置く。
方言ごとの細部は各ランタイムの CLAUDE.md に分ける。

## 目次

- 「ランタイムの CLAUDE.md 一覧」：7 パッケージの CLAUDE.md の置き場
- 「生成コードの汎用性」「BulkInsert の契約」：生成コード全体の前提と一括挿入の共通契約
- 「EditModel のロード意味論と検証経路」：確定値が正本・2 ストアのエラー・列テーブル
- 「rowversion による楽観排他」：ConcurrencyMode と版の扱いの骨格
- 「Save フック」「グラフ取得（IncludeGraph）」：フックの発火規則と Include ツリー
- 「拡張シム層」：`*Core` と拡張面の 2 層・値オブジェクトの鎖・文字列一致の翻訳判定
- 「式の翻訳と NULL 補償」「識別子の引用（生成ランタイム）」「生 SQL バインダ」：SQL への翻訳規則
- 「無制限バイナリ列の除外」：列集合の分割・EditModel・Stream アクセサ
- 「パッケージの依存集合と公開 API 面」「トランザクション失敗経路」：パッケージ契約と Rollback
- 「グラフ保存」：書く順・削除した子の外れ方・整合の検査・AcceptSaved・blob 書き込み後の版
- 「重複の事前チェックの NULL の規則」「Include とカスケード削除のキー照合」「更新できる列が無い表の UPDATE」「ドライバが返した値の型変換」：個別の共通規則
- 「値オブジェクト」：Create / 検証規則 / 型独自入力 / 定義済みインスタンス / GuidKey

## ランタイムの CLAUDE.md 一覧

- `src/QuickER.Runtime/CLAUDE.md`：共通（このファイル）
- `src/QuickER.Runtime.SqlServer/CLAUDE.md`：SQL Server のエンジン
- `src/QuickER.Runtime.Sqlite/CLAUDE.md`：SQLite のエンジン
- `src/QuickER.Runtime.EntityFrameworkCore/CLAUDE.md`：EF Core 共通部品
- `src/QuickER.Runtime.InMemory/CLAUDE.md`：インメモリ基盤
- `src/QuickER.Runtime.AspNetCore/CLAUDE.md`：リモートサーバーの固定エンジン
- `src/QuickER.Runtime.Sync/CLAUDE.md`：双方向同期エンジン

ランタイムの正本は `src/QuickER.CodeGen.CSharp/Templates/CSharpRuntime/*.scriban`（部品 `_00_HeaderAttributes` 〜 `_10_Sync`）で、`src/QuickER.Runtime*/Runtime*.g.cs` はそこから再生成するパッケージ用ソース（手編集禁止）。
部品とパッケージのフォルダが 1:1 に対応するので、規則は対応するフォルダの CLAUDE.md に書く。

## 生成コードの汎用性

- 生成される C# は CommunityToolkit 等の UI フレームワークに依存させない（WPF 以外でも使える設計）。
- Repository 生成は単一キー・アプリ側採番前提（複合キー・DB 自動採番は対象外）。

## BulkInsert の契約

- `BulkInsertAsync` の契約は 4 実装（SqlServer / Sqlite / EF Core / InMemory）で共通にする。
  - null 要素はスキップする（グラフ保存のリスト内 null と同じ流儀）。
  - 戻り値は実際に挿入した行数（スキップ分は数えない）。
  - 空コレクションは接続を開かず 0 を返す。
  - 冒頭で `cancellationToken.ThrowIfCancellationRequested()` を呼ぶ。
  - 各行は持っている外部キーのとおりに書き、呼び出し側のナビゲーションと外部キーを変えない（EF Core は行のナビゲーションを空にして保存し、成功・失敗とも戻す＝`src/QuickER.Runtime.EntityFrameworkCore/CLAUDE.md`「渡したエンティティのナビゲーションを変えない」）。
  - 並べ替えない。QuickER 版は渡した順に挿入するので、自己参照の子が先だと外部キーに断られる（EF Core は依存の順に並べ、インメモリは外部キーを検査しない。docs の BulkInsert の契約が「親を先に渡す」と書く）。
- 正本は `IRepositoryCore.BulkInsertAsync` の XmlDoc。
- バッチ全体の主キー事前検証は InMemory だけの上乗せ。
- ガード：`GeneratedRuntimeParityTestsBase`（SqlServer / EF Core）、実装先の一致のシナリオ `bulk-insert-contract`・`bulk-insert-duplicate-rejects-whole-batch`・`bulk-insert-self-reference-parent-first`・`bulk-insert-navigation-without-foreign-key`・`bulk-insert-navigation-other-foreign-key`・`bulk-insert-navigation-rejected-keeps-navigations`（QuickER 版〔SQLite〕・EF Core・インメモリ）。
- SQL Server の `SqlBulkCopyOptions` は `src/QuickER.Runtime.SqlServer/CLAUDE.md`「BulkInsert のオプション」、SQLite のコマンドの使い回しは `src/QuickER.Runtime.Sqlite/CLAUDE.md`「INSERT コマンドの使い回し」。

## EditModel のロード意味論と検証経路

### 確定値が正本

- 確定値が正本で、`BindingXxx` はその表示投影。
  - Mapper の `ApplyToEditModel` は Entity の値を確定値プロパティへ直接代入する（そのため確定値の setter は `private` でなく `internal`）。
  - `RevertInput()` が入力文字列を確定値から導出する。入力文字列の再パースを経由しないので、`DateTime` の秒未満や `Kind` のように表示形式が表せないものが落ちない（無損失ロード）。
- 行編集のキャンセル（`CancelEdit`）も確定値を正本にする。
  - `BeginEditCore` が確定値を基底の `_valueSnapshot`（`object?[]`・列テーブル順）へ、RowState を `_rowStateSnapshot` へ退避する。
  - `CancelEditCore` は確定値を内部セッターで戻し、`ExecuteRevert(RevertCore)` で入力文字列を導出し直す。取り消した入力が生んだエラーも `RevertCore` の `SetError(null)` 経路で消える。
  - 復元の順は restore → `ExecuteRevert` → `ClearDuplicateErrors(DuplicateErrorSource.Database)` → `OnCancelEditCore` → RowState。
  - バインディング文字列を戻してセッターに再パースさせると、無損失ロードで保たれた秒未満・Kind を落とす。RowState は Unchanged のままなので、劣化値が黙って保存され得る。
  - 復元はロード（`ExecuteLoad`）の中で走るため、確定値セッターの `!IsLoading` ブロックが動かない。`Database` 枠のクリアを呼ばないと、DB 照合の重複エラーがキャンセル後も残り `Validate()` が永続的に false になる。

### カスケード子と削除行

- カスケード子の登録（`ChildLink`）は単一子・コレクションとも遅延アクセサ形にする（`AddChild("X", () => X)` / `AddChildren("Orders", () => Orders)`）。登録時のインスタンスを捕捉しない。
  - `ApplyToEditModel` がコレクションを丸ごと差し替えても、Validate / CollectErrors / HasGraphChanges / AcceptChanges は現在のコレクションを見る。
- コレクション ChildLink の検証は `EditModelCollection<T>.Validate` へ委譲する。親の `Validate(includeChildren: true)` だけで兄弟間の重複検出（`EditModelUniquenessValidator`）まで走る。
- `EditModelCollection` は、削除した同一インスタンスを戻すと削除追跡を解除して RowState を削除前へ復元する（`_removed` は削除前の状態を併記して保持。戻した行を「削除＋挿入」で二重保存しない）。
- `RemoveAll` は先頭から削除し、`RemovedItems` がコレクション順を保つ。
- `IsLoading` / `IsReverting` はカウンタにする。フック等からの再入で内側の完了が外側のフラグを解かない。
- 削除マーク行（`IsRemoved`）は検証・エラー集約の対象外にする（読み側フィルタ）。
  - `EditModelBaseCore.Validate` は先頭で true を返す（子への再帰も省く）。`EditModelCollection<T>.Validate` と `CollectErrors` 系は削除行の部分木をスキップする。
  - DB 照合糖衣の共有ボディ `EditModelBaseCore.CheckDatabaseUniquenessAsync` も先頭で true を返す。漏れると `ValidateUniqueAsync` は false なのに `CollectErrors` は空、という自己矛盾になる。per-EditModel 側は無変更で、ガードは共有ボディ 1 箇所。
  - エラーストアには触れない。行自身の `HasErrors` / `GetErrors` は残り、削除を取り消して行が復活すればエラーも検証対象へ戻る。マーク時にクリアする方式は、変換エラーがバインディング setter からしか再生成されないため「不正入力が画面に残ったまま Validate が true」の罠を再現する。
  - 保存に使われるのはキーだけなので、削除予定行の変換エラーが保存を恒久ブロックしてはならない（`EditModelUniquenessValidator` の `!IsRemoved` 除外と対称）。
  - ガード：`EditModelRemovedValidationTests`。

### エラーは 2 ストア・チェックごとに分離

- エラーは 2 ストアに分ける。`_errors`（必須・変換・VO・OnValidate。`SetError` / `ClearErrors` の担当）と `_duplicateErrors`（重複値。`SetDuplicateError` / `ClearDuplicateErrors` の担当）。互いに触らず、`HasErrors` / `GetErrors` / `CollectOwnErrors` が合成する。
  - 合成結果は必ずリスト化して返す。遅延クエリを返すと、バインディング層が後で列挙する時点でストアが書き換わっており、`Clear` 中の辞書を列挙し得る。
  - ストアを分けるのは、同一プロパティで変換エラーと重複エラーを共存させ、重複解消後の再検証が変換エラーを巻き添えで消さないため。変換エラーはバインディングの setter からしか再生成されないので、消すと不正入力が画面に残ったまま `Validate` が true になり、旧確定値が黙って保存される。
- ストア内はさらに「登録したチェック」で分離し、各チェックは自分が付けたエラーだけを付け外しする。
  - `_errors` の項目は「必須チェック由来か」を持つ。必須チェック（生成 `ValidateSelf`）は他の入力エラーがある欄には `SetRequiredError` で上書きしない（変換できない文字列が入っている欄を「必須です」で塗り潰さない）。値が入れば `ClearRequiredError` で自分の分だけ消す（確定値へ直接代入されたケースでも消える）。
  - `_duplicateErrors` の項目はチェックごとのスロット（`DuplicateErrorSource`＝`Siblings`〔コレクション要素どうし〕/ `Database`〔DB 照合〕の 2 枠を 1 プロパティに持つ record struct `DuplicateErrors`）。`SetDuplicateError` は自分の枠だけを書き、`ClearDuplicateErrors(source)` は自分の枠だけを消す。両枠が空になった時点でエントリごと除去する（`Count` が「有無」の判定であり続ける）。
    - 1 プロパティ 1 スロットだと、後から走ったチェックが先の所見を上書きし、取り下げた時点で「もう一方は依然として重複している」事実ごと消える。片方を解消しただけで `Validate` が真になり得る。一括クリアだと、保存直前の `Validate` が直前の `ValidateUniqueAsync` の結果を消す。
    - 枠の引き当ては switch 式＋既定 throw にする。未定義の `DuplicateErrorSource` は `ArgumentOutOfRangeException`（`ConcurrencyModes.Validated` と同じ流儀）。二分岐の三項演算子では `(DuplicateErrorSource)99` が無警告で Database 枠へ落ちる。
  - 引数なしの `ClearDuplicateErrors()` は全ソース削除で、Mapper のロードだけが呼ぶ（判定対象の値そのものが入れ替わるため）。`RevertInput` は入力エラーのみ担当。
  - 確定値が変われば `Database` 枠はモデル全体で取り下げる（確定値セッターの `!IsLoading` ブロックが `ClearDuplicateErrors(DuplicateErrorSource.Database)` を呼ぶ）。照合したのは編集前の値で、複合制約は構成列すべての組で判定しているため、1 列でも変われば同じモデルの DB 由来所見はすべて無効。`Siblings` 枠は次の検証が判断するので触らない。
  - 照会中に確定値が変わった DB 照合は結果を捨てて false を返す。`EditModelBaseCore` の世代カウンタ `_databaseCheckGeneration` を、`Database` 枠の取り下げ（確定値の変更・ロード・CancelEdit・別の照合の開始）のたびに進め、照合は開始時の値と比べて違えば何も登録しない。進める位置は `ClearDuplicateErrorsCore` の早期 return（`_duplicateErrors.Count == 0`）より前にする（まだ何も登録していない照合の途中の編集を拾うため）。ガード：`EditModelValidateUniqueStaleTests`。
  - `OnValidate` が登録したカスタムエラーはフック側の持ち物で、生成コードは触らない。
  - ガード：`EditModelLifecycleTests`・`EditModelUniquenessTests` 系（Lifecycle / Uniqueness / RemovedValidation / RoundTrip / BinaryVo 往復）。

### 生成 ValidateUniqueAsync

- 生成 `ValidateUniqueAsync` は EditModel の全列を Entity へ写し、値を運べない列を `uncheckedPropertyNames` で渡す（共有ボディの 4 引数の多重定義が、その列を名指しする違反を捨てる）。
  - 運べない列は、未入力の値型の列（実行時）と EditModel に載らない列（配列・inet＝静的）。
  - 構成列と主キーだけを写すと、カスタムチェックが残りの列を既定値で判定する。未入力の値型の構成列が 1 つでもあるとメソッド全体を true で打ち切り、無関係な制約の照合と DB 照合の所見まで消える。
  - ガード：`EditModelValidateUniqueExecutionTests`（インメモリで生成→実行）、`UniquenessGenerationTests`、`CodegenCaseTests` の行 `code-postgresql-array-columns-validate-unique-passes-unchecked`（出力の形）。

### 列テーブルと基底ループ

- 生成 EditModel は列ごとの `EditModelColumn<TSelf>` 表（表示名・Binding 名・IsRequired・確定値アクセサ・入力文字列化デリゲート）を持つ。`ValidateSelf` の必須ループ・`RevertCore`・`BeginEditCore` / `CancelEditCore`（基底の `object?[]` スナップショット＝モデルごと再利用・null 許容値型はボクシング）・`RegisterDuplicateError` は、基底実装が表を駆動する。
  - 上記の意味論（SetRequiredError の非上書き・ClearRequiredError の自己分離・CancelEdit の復元順）は、基底へ移っても不変。
  - ストレージモデル（確定値プロパティ・フィールド・Binding setter 本体）は意図的に不変（列テーブルのラムダが実在メンバへアクセスする形。序数配列化は第 2 期候補）。
- Mapper も外枠（ExecuteLoad 開閉・RevertInput・ClearDuplicateErrors・RowState 復元）は基底のテンプレートメソッドで、生成側は `LoadColumns` / `CompleteLoad` の 2 abstract だけを持つ。`RevertInput` と `ClearDuplicateErrors` が列コピーと子ロードの間に挟まる順序を保つため、1 本化しない。
- EditModel の文言フック 4 種（CustomizeRequired/Parse/DuplicateErrorMessage・CustomizePropertyDisplayName）は廃止済みで、`EditModelMessages` リゾルバへ安定キー（propertyName 等）で寄せる。意味系フック（OnValidate / OnBeginEdit / OnCancelEdit / OnEditModelLoaded 等）は存続する。`OnCancelEditCore`（protected virtual）は、生成クラスが partial フックを復元ロードの内側へ差し込むための内部配線で、利用者向け拡張点ではない。

### そのほかのロード規則

- バイナリ列のロードは防御的コピーにする。`byte[]` は `(byte[]?)entity.X?.Clone()`、VO 列は `entity.X?.CopyValue()`（`ValueObjectBinaryBase.CopyValue()` が写した配列で同値の VO を作る。VO クラス名を書かないので、分割生成の Mapper が値オブジェクト名前空間へ依存しない）。確定値の直接代入にしたので、Entity と配列を共有しないようにする。
- 日付のみ（canonical トークン `date`）の列は `ToString("d")` で表示する（既定の `ToString()` が付ける "0:00:00" を出さない。VO 列は `X?.Value.ToString("d")`。入力側は無変更）。
- EditModel の親参照ナビは NULL 許容で初期化子なしにする（生成コードが一度も代入しないので `= null!` の「初期化済みのふり」をしない）。カスケード親の実体は `ParentModel`。

## rowversion による楽観排他

利用者向けの意味論は `docs/code-generation.md`（.ja）の「rowversion による楽観排他」節。ここには実装の骨格と、壊すと静かに回帰する判断だけを書く。

- rowversion 列の有無がそのままポリシー（オプトインなし）。列があれば全バックエンドで版チェックが効き、無い図の生成物・挙動は不変。
- `UpdateAsync` / `SaveAsync` は省略可能引数 `ConcurrencyMode`（`Optimistic` 既定 / `ForceOverwrite`＝明示的 last-write-wins）を受ける。
- 未定義値は入口で fail-fast にする。全 12 実装（ADO / リモートクライアント / InMemory / EF Core の各 Update＋Save×2）が先頭で共有ヘルパー `ConcurrencyModes.Validated` を通し `ArgumentOutOfRangeException` を投げる。内部の 2 値分岐は「Optimistic なら版ガード・さもなくば強制」のままなので、検証がないと `(ConcurrencyMode)99` が黙って強制上書き側へ落ちる。
- 単一 `UpdateAsync` の「行なし＝false」と「版が古い＝`SaveConflictException`」は、実在確認で区別する。`insertWhenUpdateMissing` も同じ線引きにする（行なし→INSERT 切替。版違いは競合。INSERT へ倒すと主キー重複に化ける）。
- `SaveConflictException` は再試行の材料を構造化して持つ。`Reason`（`SaveConflictReason.NotFound` / `Modified` / メッセージのみのコンストラクタ経由は `Unknown`）・`EntityTypeName`・`Key`（2 ファクトリが充填。メッセージ解析を不要にする）。リモートでの復元は `src/QuickER.Runtime.AspNetCore/CLAUDE.md`「クライアントの分類」を参照。
- Save フックの After はコミット前なので旧版が見える。
- `DeleteAsync(id)` は版で守られない（利用者向けの意味論は上記 docs）。
- 同一エンティティに rowversion 列が 2 本以上ある図は生成時の診断エラーにする。版の読み書きは全経路が `FirstOrDefault` で「1 型につき 1 本」を前提にしており、SQL Server も 1 テーブル 1 本しか許さない。黙って生成すると「どちらが版か」が暗黙に決まり、DDL 適用で初めて落ちる。判定は `CSharpTypeInfo.IsRowVersion`（`[StoreGeneratedColumn]` の付与条件と同一）で、列型辞書が確定した後に検証する。
- 主キー列が rowversion の図も同じ検証で生成時エラーにする。DB 採番で INSERT / UPDATE の対象から外れるため、生成 INSERT がキー列を送らず、行の同一性が更新のたびに変わる値に乗り、インメモリ辞書のキーも壊れる。
- VO 併用時（GenerateValueObjects=true）は版プロパティが VO 型（例 `RowVerValue : ValueObjectBinaryBase`）になるため、リフレクション経由の版の読み書きは全経路で開き直す。
  - 書き戻しは `SqlValueObjectActivator.Wrap`（ADO の `OUTPUT` 戻り・`RowVersionCollector.Apply`・InMemory の `StampRowVersion`・`RemoteEntityGraph.WriteRowVersion`）。
  - 読み出しは `SqlParameterValue.Unwrap`（InMemory の版比較・`RemoteEntityGraph.ReadRowVersion`。転送は常に生 byte[]）。
- DB 採番列は EditModel の入力必須にしない。rowversion 列は `IsRequired=false` で、Mapper の Entity へのコピーは「入力があるときだけ代入」（未入力は現在値維持）。さもないと新規行の EditModel 保存が `"{列} has no input value."` で必ず失敗する。
- 既知の制限：他方言に `rowversion` 型が無く対象外／`SqlBulkCopy` は生成値を返せず版未反映／`OUTPUT` 句はトリガーのあるテーブルで使えない（QuickER の DDL はトリガー非生成）／「行なし削除」は ADO 黙認・EF グラフ例外の既存非対称／rowversion 列は `[DbColumnMeta]` トークンなしで C# リバース対象外／生 SQL・バイナリ Stream アクセサは非対象。
- 実装先ごとの細部：QuickER 版 ADO（SQL Server のみ）は `src/QuickER.Runtime.SqlServer/CLAUDE.md`「行バージョンの OUTPUT と版の回収」、インメモリは `src/QuickER.Runtime.InMemory/CLAUDE.md`「保存単位のステージング」、EF Core は `src/QuickER.Runtime.EntityFrameworkCore/CLAUDE.md`「保存のやり直しと実行戦略」、リモートは `src/QuickER.Runtime.AspNetCore/CLAUDE.md`「保存の応答は列の値を運び位置で突き合わせる」。
- ガード（実 DB）：`SqlServerConcurrencyRuntimeTests`・`EfCoreConcurrencyRuntimeTests`（Docker）、`InMemoryConcurrencyRuntimeTests`・`RemoteConcurrencyRuntimeTests`（CI 常時。リモートはインメモリ実体を Kestrel で公開）。VO 併用は `ConcurrencyVo{SqlServer,EfCore}RuntimeTests`（Docker）・`ConcurrencyVo{InMemory,Remote}RuntimeTests`（CI 常時）。

## Save フック

利用者向けの意味論は `docs/code-generation.md`（.ja）の「Save フック」節。

- グラフ保存（`SaveAsync` の単一 / 複数の 2 形態のみ）の各操作の前後に処理を差し込む機構。直接 Insert / Update / Delete と BulkInsert は素通り。オプションなし・常時生成で、未登録なら完全 no-op。
- 契約は Core（`ISaveHook<TEntity>`＝両メソッド既定実装付き・`SaveOperation`・`ISaveHookContext`・`ISaveHookRegistry` / `ISaveHookInvoker`）。
- DI 登録は `ISaveHook<T>`（0..N）＋各 `AddGenerated*` の `TryAddScoped<ISaveHookRegistry>`（keyed 版はファクトリで `GetService` を明示）。Repository 基底 / 具象に省略可能な第 2 引数 `ISaveHookRegistry? saveHooks = null`（手動 new は不変）。
- DI なし経路も公式サポートする。`SaveHookRegistry`（`new SaveHookRegistry().Add<T>(hook)` の連鎖でリポジトリのコンストラクタへ渡す）は、追加順に発火して DI 版と同じ意味論にする。型消去した辞書＋型付き `SaveHookInvoker<T>` でリフレクションなし。組み立てはスレッドセーフでないので、リポジトリへ渡す前に全部追加し、以後は読み取り専用にする。
- DI 経路の登録糖衣 `AddSaveHook(services, object hook)` は、インスタンスが実装する `ISaveHook<TEntity>` を全部走査して singleton 登録する（エンティティ型ごとの分岐を登録コードから消す）。1 つも実装していなければ、黙って何も登録しないのを避けるため `ArgumentException`。
- Before は `EntityGraphSaver` の RowState 分岐で発火する（ExecuteAsync 内でないので、update→insert 切替の二重発火を避けられる）。登録順で最初の `false` で短絡し、その 1 件を単独スキップする（`HashSet<EntityBaseCore>(ReferenceEquality)` の skip 集合を `AcceptChanges` へ渡し、RowState を据え置く）。整合はフック実装者の責任（削除は子から順なので、「root だけ false」は子が消え root が残る。矛盾スキップは FK 制約→例外→全ロールバック）。
- After はコミット前で、同一トランザクションに参加する `ISaveHookContext`（`WriteBinaryColumnAsync`＋ファイル糖衣・`ExecuteSqlAsync`）を受ける。Save と blob / 監査のアトミック性を保つ。context の型は After 対象エンティティ型に束縛する。除外列なし図の WriteBinary は NotSupported。`insertWhenUpdateMissing` は Before=Update / After=実操作 Insert。
- 実装先の差：ADO は各 DML 直後に After（`SqlSaveHookContext`＝進行中の (connection, transaction) 共有）。EF Core は SaveChanges 後に一括。インメモリは擬似。リモートはサーバー側 DI で発火。
  - EF Core の細部は `src/QuickER.Runtime.EntityFrameworkCore/CLAUDE.md`「削除の振る舞いとフックの順」、インメモリは `src/QuickER.Runtime.InMemory/CLAUDE.md`「Save フックのインメモリの差分」、リモート（スキップの応答への伝搬）は `src/QuickER.Runtime.AspNetCore/CLAUDE.md`「保存の応答は列の値を運び位置で突き合わせる」。
- EF Core・InMemory・マルチターゲット・UseRuntimePackages と直交する。
- ガード：実装先の一致のシナリオ（`BackendParityScenarios.SaveHook.cs` の `hook-*`、CI 常時）、SQL Server の 2 者比較（`BackendParitySqlServerRuntimeTests` の `hook-*` のケース）、`SaveHook{Ado,EfCore,InMemory,SqlServer,Remote}RuntimeTests`。

## グラフ取得（IncludeGraph）

利用者向けの意味論は `docs/code-generation.md`（.ja）の「グラフ取得（IncludeGraph）」節。

- `Query().IncludeGraph()` は、グラフ保存が辿るのと同じ子方向カスケード閉包を Include ツリーへ展開する、per-entity 生成の拡張メソッド（静的クラス `SqlQueryExtensions`）。契約 interface は不変・オプションなし常時生成（Repository 契約が出る構成のみ）。カスケード子を持たない葉は `=> query;` の no-op。
- Include ツリーは「1 ナビゲーション 1 ノード」を不変条件にする。実行器は子コレクションを置換でバインドし（SQLite IncludeLoader / InMemory IncludeAttacher）、SQL Server は同名 JSON プロパティが衝突するため、同一ナビの重複ノードは先行の孫を静かに消すか実行時エラーになる。
  - Include（2 形）/ ThenInclude（2 形）/ AddIncludeNodes の全経路が `SqlQuery<T>.MergeInclude`（internal 静的。判定の単一正本。PropertyInfo 照合は参照一致＋Name×DeclaringType フォールバック）を通って既存ノードへマージする。EF Core 流の分岐 Include（`Include(A).ThenInclude(B)`＋`Include(A).ThenInclude(C)`）が 4 実行器で成立する。
  - `AddIncludeNodes` はツリーをディープコピーして取り込む。デデュープは既存ノードの再利用なので、コピーしないと `IncludeGraph().Include(...).ThenInclude(...)` が生成拡張の static 共有ツリーを変異させ、以後の全クエリを汚染する。
- 閉包計算（`CSharpGenerationModelBuilder.IncludeGraph.cs`）が組んだツリーを `SqlQuery<T>.AddIncludeNodes`（infra_visibility）へ直接渡す構造は不変。閉包はナビ宣言順 DFS の edge-skip で、パス上に既出のテーブルへ向かう辺は展開しない。スキップした辺は Info 診断 `CodeGen_Info_IncludeGraphSkippedNavigations` で名指しする。自己参照・相互参照ナビは空のまま返る。
- 実行器 4 実装は無変更。
- ガード：`IncludeGraph{SqlServer,Ado,EfCore,InMemory}RuntimeTests`（3 階層チェーン＋取得→編集→保存の往復。パリティは `IncludeGraphRuntimeTestsBase`）、自己参照 edge-skip の `IncludeGraphSelfReference{SqlServer,Ado,InMemory,EfCore}RuntimeTests`（重複 Include マージ / 兄弟分岐 / 共有ツリー不変性のシナリオを含む）。

## 拡張シム層

利用者向けの拡張レシピ（マーカーへのインターフェイス注入・ルートへのメンバー追加・Repository 契約の既定実装・実装側 protected ヘルパ・拡張メソッド・モード移植性の保証・インターフェイスの線引き）は `docs/code-generation.md`（.ja）の「生成される基底クラスの拡張」節。

### 2 層構造

- 生成コードの基底は、例外ゼロで 2 層にする。実装の置き場が固定ランタイムの `*Core`、利用者が拡張する面が「現行名を名乗る生成 partial」。
  - `*Core`：`EntityBaseCore` / `EditModelBaseCore` / `EditModelBaseCore<TSelf>` / `ValueObjectBaseCore<TSelf, TValue>` / `IValueObjectCore` / `IRemoteRepositoryCore<TEntity, TKey>` / `IRepositoryCore<TEntity, TKey>` / `SqlServerRepositoryCore` / `SqliteRepositoryCore` / `EfCoreRepositoryCore<TEntity, TKey, TContext>` / `InMemoryRepositoryCore` / `HttpRemoteRepositoryCore` / `MapperBaseCore<TEntity, TEditModel>`。
  - 拡張面：`EntityBase` / `EditModelBase<TSelf>` / VO 基底 7 本 / 非ジェネリックマーカー `IValueObject` / `IRemoteRepository<,>` / `IRepository<,>` / バックエンド実装基底 5 本 / `MapperBase<,>`。
- 拡張面はスキーマ依存側のバケット（Entity / ValueObject / EditModel / Repository 契約 / 各バックエンド実装 / Mapper）へ、パッケージ参照モードでも必ず出す。コンパイル済みパッケージのクラスへは partial を書けず、`UseRuntimePackages` の切り替えで利用者の拡張コードが書き直しになるため（層の存在理由）。
- per-型の宣言句は現行名のままにする（`: EntityBase` / `: EditModelBase<X>` / `: ValueObjectStringBase<X>` / `I{E}Repository : IRepository<E, K>` / `{E}Repository : SqlServerRepository<E, K>(…)` / `{E}Mapper : MapperBase<E, M>`）。拡張面を経由して Core へ着地する。
- `*Core` 型は 1 つも `partial` を名乗らない（11 宣言）。拡張面はシム側だけという線引きを言語レベルで担保する。インラインモードで利用者が Core のインターフェイスへ既定実装を書くと、シム側の既定実装とダイヤモンドになり CS0121 を招く。
- 固定ランタイム内の参照は 1 つ残らず Core 名にする（`is IValueObjectCore` 判定・`EntitySaveMetadata` 等の引数型を含む）。固定コードが生成型を参照すると、パッケージ側から生成側の型を見ることになり成立しない。XmlDoc の cref も同じ理由で生成型を指せない（文言化するか新設ヘルパーへ向ける）。
- 固定ランタイム内の派生はシムを通らず Core 直付けにする（`HttpSyncServerSource` → `HttpRemoteRepositoryCore`）。ランタイムは生成型を参照できない構造的必然で、ユーザー partial がそこへ届かないのは正しい意味論。
- 拡張面（`EntityBase`）へ足した読み書きプロパティは列ではない。`[Column]` 許可リスト（列判定）により SQL・`HasSameValues`・列コピーの対象外で、JSON 経路（`ToJson` / `Clone`・リモート転送）にだけ載る。
- `DbContext` は外部型なのでシムを置かない（拡張点は生成される `QuickErDbContext` 自身。元から partial）。
- 層別出力での配置は Entity / VO＝domain、EditModel＝presentation（per-型コードと同じバケットの帰結）。

### 値オブジェクトの鎖

- VO は「共通ルート 1 本＋系統別 6 本」の単一鎖にする。`ValueObjectBase<TSelf, TValue>`（`ValueObjectBaseCore` を継承）を全 VO が必ず通る。
  - 系統別は `ValueObjectOrderedBase` / `ValueObjectStringBase` / `ValueObjectBooleanBase` / `ValueObjectBinaryBase` / `ValueObjectGuidKeyBase`（ルート直下）と、`ValueObjectOrderedBase` 派生の `ValueObjectDateTimeBase`。
  - 系統別 6 本は実装ごと生成側にあり、クラス本体が系統別の拡張点を兼ねる。マーカー `IValueObject` を実装するのはルート 1 本だけ（系統別へは継承で波及）。ルートへ足したメンバーが値の形に依らず全 VO へ届く。
  - 系統別クラスの emit ガードは per-VO コードと同じ `generate_value_objects && render_value_objects`。Runtime 系ファイル・ランタイムパッケージには 1 本も出ない。
- VO の拡張面だけは Core が値引数コンストラクタしか持たないため、転送コンストラクタが構造上必須（空ボディの原則に対する唯一の例外）。
- ランタイム側へ寄せた共有部は 3 型：`ValueObjectComparisons`（非ジェネリック `IComparable.CompareTo` の型不一致 throw と、Ordered 演算子の null 順序比較〔null 最小・両 null 等値〕。生成側の 3 系統は 1 行転送）、`ValueObjectBinaryOperations`（`AreEqual`＝null 扱い＋`SequenceEqual`、`ComputeHashCode`＝`HashCode.AddBytes` で全バイト混合）、`IStringMatchValueObject<TSelf>`（Contains / StartsWith / EndsWith の string 版・TSelf 版 計 6 メンバー。拡張面ではなく機構の契約で、Ordinal 比較と `ArgumentNullException` 契約の正本）。
- TSelf 束縛の static ファクトリ（Boolean の `True` / `False`・DateTime の `Now` / `Today`・GuidKey の `Create()`）はクラスに置くしかない。継承 static だからこそ `IsActiveValue.True` の呼び形が成立する。
- ジェネリック契約 `IValueObject<TSelf>` / `IValueObject<TSelf, TValue>` は名前不変で、拡張面ではない（静的ファクトリ契約でランタイム在住。アリティ 1 の継承元だけ `IValueObjectCore`）。

### 文字列一致の翻訳判定

- 文字列一致の翻訳判定はインターフェイスマップで引く。4 エンジン（Core の `SqlQuery` null 引数ガード・SqlServer / Sqlite の `SqlExpressionTranslator`・EF Core の `ValueObjectStringMethodTranslator`）が共有する。
  - 判定：`call.Method.DeclaringType` が `IStringMatchValueObject<>` を実装し、かつ `GetInterfaceMap(...).TargetMethods` に `call.Method` が含まれる。含まれるかはメソッドハンドルで照合する。
  - 参照の一致にすると、具象 VO 型から引いた MethodInfo（ReflectedType が違う）を認識せず、手で組んだ式木が翻訳できず null の引数も先に断れない。写しは 3 か所（`_05` の 2 つ＝Core の null ガードと `SqlExpressionTranslator`・`_08` の 1 つ）で照合をそろえる。
  - `IsAssignableFrom`＋名前一致にしてはいけない。具象 VO も面を継承実装しているので、利用者が per-VO partial へ書いた自前の `Contains` が中身を一度も実行されないまま列の LIKE へ化ける。
  - TSelf 版オーバーロードも契約に含めるのが必須（含めないと VO 引数の `Contains` が翻訳対象から外れ、実 DB テストが割れる）。EF Core 側は判定が返す閉じた契約の型引数から TSelf を取る。
  - ガード：`StringMatchMethod{Ado,SqlServer,EfCore,InMemory}RuntimeTests`（同じ式を 4 つの実装先へ流す。null の引数は変数で渡す＝定数の null は翻訳器が自分で断るのでガードの写しが見えない）、`SqlExpressionTranslatorStringMatchTests`（両アーム）。

### Repository 契約シムと実装側

- Repository 契約シムの価値は「既定実装が DI 越しに届く」こと。`IRepository<TEntity, TKey>` の partial へ既定実装付きのメンバーを足すと、DI で受け取った `I{Entity}Repository` 参照から（全エンティティ・全バックエンド・デコレータ越しでも）直接呼べる。インターフェイスの既定実装は派生インターフェイス型の参照から見え、`this` へのインターフェイス呼び出しは仮想ディスパッチでデコレータの実装へ落ちる。
  - 追加は既定実装付きに限る。本体のないメンバーは生成リポジトリ全部を壊す（QuickER はその実装を出さない）。
  - シムの `IRepository<,>` は Core だけでなく、シムの `IRemoteRepository<,>` も継承する。リモート面へ足した既定実装が全機能面の参照からも見える（この 2 本目の継承辺が層の要）。
- 実装側は独立 5 枝（`SqlServerRepository` / `SqliteRepository` / `EfCoreRepository` / `InMemoryRepository` / `HttpRemoteRepository`。共通の親を持たない）。用途は per-entity partial の手書き実装が使う `protected` 共有ヘルパの置き場。コンストラクタ引数を正確に転送し、省略可能引数の既定値も写して手動 new の書き味を不変に保つ（VO シムと同じ「空ボディ原則の例外」）。
- インターフェイスを持つ拡張面は Repository 契約と VO マーカーだけ。前者は利用側のアクセス経路がそもそもインターフェイスだから、後者は開ジェネリックの共通ルート `ValueObjectBase<TSelf, TValue>` を非ジェネリック 1 型で束ねる必要があるから。Entity / EditModel / Mapper は具象クラスで持つ単一実装で、対応するインターフェイスは設けない。
- ガード：`CodegenCaseTests` の行 `code-extension-shim-*`（emit 先と per-型宣言句を、インライン / 分割 / パッケージ / 層別 / マルチ方言で、宣言のシンボルとコンパイルで固定）、`ExtensionShimAcceptanceTests`・`ExtensionShimBinaryAcceptanceTests`・`ExtensionShimRepositoryAcceptanceTests`（実フィクスチャ上で partial 拡張が全 Entity・全 VO・全系統・全リポジトリ・全 Mapper へ届く。Repository は InMemory フィクスチャ＝DB 不要で、既定実装がデコレータ越しに転送されることも実測）。
- `Column` 判定のガード：`ExtensionShimAcceptanceTests` の 2 本（シム追加プロパティは列でない／`[Column]` 付き手書きプロパティは列になる）。

## 式の翻訳と NULL 補償

`Query()` の式木を SQL へ翻訳する `SqlExpressionTranslator`（SqlServer / Sqlite の 2 実装。EF Core は独自の結合へ翻訳）の共通規則。
NULL の扱いは「C# の意味論と同じ」に揃える。揃える相手は EF Core と InMemory（式木をコンパイルして実評価する）。

- 値の側が null になる `==` / `!=` は `IS [NOT] NULL` へ補償する（`SqlExpressionTranslator.VisitComparison`）。リテラル null だけでなく、変数・式の評価結果が null の場合も対象。VO は Unwrap 後で判定する。
  - 補償しないと評価済み null が `col = @p`（@p=NULL）にバインドされ、3 値論理で全行 UNKNOWN になる。実害は一意性チェックの自分自身除外で、主キー未設定の新規行の重複を静かに見逃す。
- 列の側も補償する。非 null 値との `!=` は `(col <> @p OR col IS NULL)` を出力する。素の `col <> @p` は列が NULL の行で UNKNOWN になり静かに脱落するが、C# も EF Core も「NULL は非 null 値と等しくない＝一致」と扱う。列の NULL 許容性は式木から確実に判定できないので無条件補償にする（非 NULL 列では追加した選言が成立しないだけで意味不変）。`==` 側は補償しない。
- 列同士の `!=` は `(a <> b OR (a IS NULL AND b IS NOT NULL) OR (a IS NOT NULL AND b IS NULL))`（EF Core と同形）。C# の「片側だけ NULL は不一致／両側 NULL は一致」に揃える。
- 列同士の `==` も対称に補償する。`(a = b OR (a IS NULL AND b IS NULL))`（EF Core と同形）。素の `a = b` は両側 NULL の行を UNKNOWN で落とすが、C# も EF Core も「両側 NULL は一致」と扱う。
- 列 vs 値の `==` は無補償のまま。「NULL 列は非 null 値と一致しない」で SQL と C# が実際に揃う。
- 否定 `!(==)` / `!(!=)` は演算子を反転して同じ経路へ流す（`VisitComparison(binary, parameters, negate: true)` が先頭で NodeType を入れ替える）。`NOT (...)` で包むと補償の外側に出て、`!(x.Col == v)` だけが NULL 行を落とし `x.Col != v` と結果が割れる。等値以外の否定は従来どおり `NOT (...)`。
- 二重否定は単項 Not の入口で畳み込む。オペランドがさらに単項 Not なら、その被演算子をそのまま翻訳して両方の否定を消す。畳まないと `!(!(x != v))` の外側が「オペランドが等値比較でない」ため `NOT (...)` 枝へ落ち、内側の反転と合成されて `NOT ([col] = @p)`（NULL 行脱落）になる。列同士も同様。再帰的に効くので三重以上も畳む。
- IN（コレクション Contains）も補償する。
  - null 要素はパラメータ化せず列の `IS NULL` へ畳む。混在リストは `(col IN (@…) OR col IS NULL)`、その否定は `(col NOT IN (@…) AND col IS NOT NULL)`。
  - NOT IN は null 要素が無くても列側を無条件補償する：`(col NOT IN (@…) OR col IS NULL)`（素の `NOT (col IN …)` は NULL 列の行を UNKNOWN で落とすが、C# も EF Core も「null は非 null 値のリストに含まれない＝否定で真」と扱う。`!=` の列側無条件補償と同じ線引き）。
  - 否定は単項 Not の入口で等値反転と同列に `BuildInClause(negate: true)` へ畳む。
  - 空リストは IN=`1 = 0` / NOT IN=`1 = 1`、全 null リストは `IS [NOT] NULL` 単独。いずれも C# の `Contains` と同結論。
- 関係演算子（`<` `<=` `>` `>=`）は null 対応の SQL 対応物が無いので補償せず、パラメータのまま（意図的な線引き）。
- 列を含まない bool の部分式は、翻訳の前に評価して `1 = 1` / `1 = 0` へ畳む（`SqlExpressionTranslator.Visit` の入口の `TryFold`）。対象は型が bool で、ラムダの自由な引数を参照しない式。
  - `&&` / `||` は C# と同じく左辺で結果が決まれば右辺を評価せず、定数になった側は簡約する。
  - 畳まないと値どうしの比較が `@p0 = @p1` として送られ、null で全行 UNKNOWN になる。「null なら絞らない」`filter == null || x.Col == filter` が違う行を返す。
  - 畳み込みは列を含む比較より前段で働くだけで、上の NULL 補償は変わらない。
  - 「自由な引数」の判定は内側のラムダが束縛した引数を除く（`ids.Any(i => i > 0)` は値）。
  - `QueryValueEvaluator` は演算子メソッドを持たない `==` `!=` `!` `&&` `||`（と string の `==` `!=`）を直接評価し、この書き方が呼ぶたびに式木をコンパイルしないようにする。
- `Nullable<T>.Value` は `TryColumnName` が列へ剥がす。char を受ける文字列一致は 1 文字のパターンにする。
- LIKE のパターンは列の型の可変長形で束縛する（`SqlQueryParameter.VariableLength`＝nchar→nvarchar・char→varchar）。固定長で束縛するとパターン自体が空白で詰められて一致しない。等号は列の型のまま。
- 日付部品（`x.Col.Year` 等）への変換は、読み出し元の宣言型が `DateTime` / `DateOnly` / `DateTimeOffset`（と各 nullable）のときだけにする。メンバー名だけを見ていると、VO の partial 実装などで `Year` を足した瞬間に日付でない列が `YEAR([col])` へ化ける。該当しない参照は既存の値評価経路へ落ちて `NotSupportedException` になる（明示的に失敗）。
- ナビゲーションプロパティは列でないため、翻訳不能として弾く。`IsColumn` が `[NavigationReference]` 付きプロパティを検出して `NotSupportedException` を投げる（外部キー列で絞るよう案内）。ラムダ引数直下のプロパティ参照だけで列判定すると、`Where(x => x.Customer == null)` が存在しない列 `[Customer] IS NULL` の SQL になり、実行時 DB エラーになる。属性判定は ConcurrentDictionary でキャッシュする。
  - インメモリも同じく断る（`src/QuickER.Runtime.InMemory/CLAUDE.md`「ナビゲーションを断る」）。EF Core は独自の結合へ翻訳するので断らない。
- 文字列一致（Contains / StartsWith / EndsWith）の null 引数は、全バックエンドで `ArgumentNullException` にする（`SqlQuery.Where` の共有ガードで fail-fast 統一）。
- 手書き LINQ の翻訳器はド・モルガン展開しないまま（docs の既知の割り切り）。
- 方言固有の部分（空・空白の判定、日付部品、固定長の `EndsWith`）は `src/QuickER.Runtime.SqlServer/CLAUDE.md`「式の翻訳（SQL Server）」・`src/QuickER.Runtime.Sqlite/CLAUDE.md`「式の翻訳（SQLite）」。
- ガード：`TranslatorParity{SqlServer,Sqlite,InMemory}RuntimeTests`（期待値は同じ実装先から読み戻した全行へ C# のラムダを当てた結果。「ナビゲーションを使う条件・並び替えは NotSupportedException で断る」を含む）とその述語の表 `TranslatorParityPredicateRows`（値の側が null の `==` / `!=`・列側の `!=`・列どうし・否定の反転と深さ 1〜4・IN / NOT IN の null の要素と空・bool 列の短縮形・NULL 許容の decimal の行）、値オブジェクトの列の IN / null 変数は `BackendParityRuntimeTests`（シナリオ `query-null-comparison-and-ordering`）と `BackendParitySqlServerRuntimeTests`（ケース `value-object-in-null-semantics`・`value-object-in-all-null-elements`）、SQL の断片でしか言えない表明（パラメータの `ColumnName`・IN の null の要素をパラメータにしない数・等値以外の否定を `NOT (...)` で包む形）は `SqlExpressionTranslatorInClauseTests`・`SqlExpressionTranslatorNullComparisonTests`、`SqlExpressionTranslatorFoldingTests`。mutation 実証済み。

## 識別子の引用（生成ランタイム）

- 生成ランタイムの識別子の引用は `EntitySaveMetadata.QuoteIdentifier` / `QuoteTableName`（テンプレート `_06` に在る。`internal` なので公開 API 面は変わらない）
  - テーブル名は最初の `.` で分割して引用する。DDL 側と揃える（まとめて引用すると、dbo 以外のスキーマの図で全 CRUD が実行時エラーになる）。分け方の写しの一致は `src/QuickER.Dialects/CLAUDE.md`「スキーマ修飾名の分け方（SchemaQualifiedName）」を見る
  - 列名は終端の引用文字を二重化する。`SqlExpressionTranslator.RawColumnName` は二重化を解いて返す（解かないと `[SqlColumnType]` の逆引きが外れ、型付きパラメータが黙って落ちる）
- `QuoteIdentifier` の emit ガードは `repositories`。EF Core 単独のスコープでは出さず、`ColumnList` は引用なしの列名にする
  - EF Core は自分で SQL を組むので、`ColumnList` は例外文の表記にしか使わない。ある方言の引用符で囲むと、ほかの方言で列名を取り違えて見せる
- SQL Server の FOR JSON（`JsonQueryPlanner`）も同じヘルパーを通す（列名・列の別名〔プロパティ名〕・Include の別名〔ナビゲーション名〕・相関の列名）
  - 素のまま書くと、`group` の列を持つ表の `Query()` や `Include(x => x.Order)` が構文エラーになり、`]` を含む列名で文が壊れる。FOR JSON PATH は角括弧を外した別名をキーにするので、JSON の形は変わらない
- ガード：`GeneratedIdentifierQuotingTests`（ランタイムパッケージを実行して引用を見る）・`EfCoreEntitySaveMetadataTests.ColumnList_IsUnquoted`・`TranslatorParitySqlServerRuntimeTests` の予約語の 2 件・`SqlServerPlainQueryRuntimeTests`（`group`・`x]y` の列の値の比較）。mutation 実証済み

## 生 SQL バインダ

- 生 SQL バインダはコレクション値を `@名0,@名1,...` へ IN 展開する。置換の境界は左右両方にする：`(?<![@0-9A-Za-z_])@名(?![0-9A-Za-z_])`。右だけだと `@@ROWCOUNT` のようなシステム変数の末尾まで置換して SQL が壊れる。
- テキスト置換なので、文字列リテラル / コメント内の同名も置換される（docs に明記）。
- 空リストは `(NULL)` 展開で、`IN` は不一致（正）、`NOT IN` は全行 UNKNOWN で「全件除外」になる。空なら SQL を分岐する（docs に明記）。
- 生成前の静的検証（`RawSqlAnalyzer`）は生成器側の規則。

## 無制限バイナリ列の除外

利用者向けの意味論は `docs/code-generation.md`（.ja）の「無制限バイナリ列の除外」節。
`ExcludeUnboundedBinaryColumns`（CLI `--exclude-unbounded-binary-columns`・GUI チェック）は、`varbinary(max)` / `image` / 長さ宣言なし BLOB / `bytea` 等の無制限バイナリ列を、QuickER 版 Repository の SELECT / UPDATE 対象から外す。

### 判定と列集合

- 判定は宣言ベースで、プロバイダの型マッパー（`CSharpTypeInfo.IsUnboundedBinary`）が行う。`varbinary(max)` / `image` / 長さなし BLOB は真、`rowversion` / `timestamp` / `binary(n)` / `varbinary(n)` は偽。
- 生成 Entity のプロパティへマーカー属性 `[UnboundedBinaryColumn]` を刻み、除外列一覧は Info 診断で通知する。
- ランタイム（`EntitySaveMetadata`）は属性をリフレクションで読み、列集合を分割する。
  - INSERT / BulkInsert は全列（初回書き込みで blob を渡せる）。
  - SELECT は縮小列（除外列は未取得状態＝nullable は null・非 nullable は空配列）。
  - UPDATE は縮小列（除外列に触れず既存 blob を温存）。
- 除外列に「null でも空でもない」値を残したまま UPDATE / SaveAsync（グラフカスケード）/ InMemory 更新 / リモートクライアント送信を試みると `InvalidOperationException`。サイレントなデータ取りこぼしを防ぐ。除外列の更新は生 SQL `ExecuteSqlAsync` へ誘導する。

### EditModel / Mapper

- EditModel / Mapper は除外列へ rowversion とほぼ同じ規則を適用する。
  - 常時必須にはしない（`IsRequired` へ `IsExcludedUnboundedBinary`〔オプションとの AND 済みフラグ。正本は BuildEditModelProperty / BuildValueObjectEditModelProperty〕を AND）。
  - Mapper の `ApplyToEntity` は、入力が非 null のときだけ代入する（未入力は初期化子のまま＝未取得状態を維持し、`IsUnset` ガードを素通りして UPDATE が既存 blob を温存）。
  - さもないと、VO 有効 × NOT NULL 除外列（初期化子 `= null!`）で「通常フェッチ→通常列だけ編集→保存」が必須エラーと `?? throw` の 2 段で必ず落ちる。
  - 帰結：Mapper 経由では NULL 許容除外列を NULL へ戻せない（生 SQL / `Write{列}Async(null)` へ誘導）。
- NOT NULL の除外列は新規行（RowState=Added）でのみ必須にする（`IsRequiredWhenAdded`＝列テーブルの独立フラグ）。
  - VO×NOT NULL では null の INSERT は DB の NOT NULL 違反で必ず落ちる。正当な 2 段方式（INSERT→Write）は空の値（VO は `Create([])`・非 VO は空配列＝初期値で自然成立）を入れる形でしか成立しないので、この検証は DB 行き確定の null だけを列名つきで止め、正当な経路を塞がない。
  - 判定は基底 ValidateSelf ループの `IsRequired || (IsRequiredWhenAdded && RowState==Added)`。Added を抜けた行の必須エラーは else 枝の `ClearRequiredError` が取り下げる。
  - ガード：`CodegenCaseTests` の行 `exec-excluded-binary-required-when-added-*`、`BinaryVoEditModelRuntimeTests`（2 段方式の実測）。
- WithUnboundedBinary 取得後の Mapper 経由保存が UPDATE ガードで拒否されるのは設計どおり。組合せは `BinaryVoFixture` ＋ `BinaryVoEditModelRuntimeTests` が固定する。

### 取得・実装先ごとの扱い

- 生 SQL のエンティティ取得は縮小列が必須だが、SELECT に除外列を含めれば opportunistic にマップされる。名前付きクエリの DSL 射影が除外列を参照していれば、サーバー側刈り込み SELECT に含まれ値が取れる（WHERE 参照も可）。
- EF Core（DbSet 経由）は非適用（EF Core の列選択は EF Core の責務なので全列読み書き）。
- InMemory は読み取り・更新・射影で同じに動く：読み取り複製の除外列 strip・更新ガード・射影は完全クローンから。更新は除外列の blob を温存する（`src/QuickER.Runtime.InMemory/CLAUDE.md`「無制限バイナリ列（インメモリ）」）。
  - NOT NULL は検査しない（docs のインメモリの節「実 DB との既知の乖離」）。NOT NULL の除外列への Stream の `Write{列}Async(id, null)` と、NOT NULL の列が null の行の挿入・グラフ保存は、DB が断るのにインメモリは保存する。実装先の一致はこれを受け入れた手の差（NOT NULL の列への null の書き込み）として、インメモリだけをその手から後の比較から外す。ガード：`GeneratedRuntime/Parity`（行 `not-null-stream-set-null-excluded-blob-is-refused`・`not-null-insert-null-thumb-is-refused`・`not-null-insert-null-amount-is-refused`）。
- 読み取りオプトイン `SqlQuery<T>.WithUnboundedBinary()`（`SqlQueryPlan` の bool フラグ。API は常時生成・除外列なしは no-op）で、呼び出しごとに blob 込みで取得できる。
  - ON かつ Include なしのとき、SQL Server も SQLite も全列プレーン SELECT。OFF の Include なしの取得も縮小列のプレーン SELECT。SQL Server で FOR JSON を通るのは Include のある取得だけ。
  - InMemory は strip なしの完全クローンを返す（RowState=Unchanged の正当なエンティティ）。EF Core は元々全列のため no-op。
  - Include との併用は終端で `InvalidOperationException`（両順序を BuildPlan で検出）。
  - 効果はエンティティ形の取得（ToListAsync / FirstOrDefaultAsync）と射影のフォールバック経路（列抽出不能なセレクタ）。件数・存在確認・刈り込みが成立する射影には影響しない。取得後の除外列更新は既存 UPDATE ガードで throw する（生 SQL へ誘導）。
- VO・マルチターゲット・EF Core・InMemory・リモートと直交する。双方向同期とも併用可（行の転送からは外れ、`SyncOptions.IncludeUnboundedBinary` のときだけ列単位でコピーされる。`src/QuickER.Runtime.Sync/CLAUDE.md`「無制限バイナリ列」）。

### Stream アクセサ

- 除外列ごとにストリーミング Stream アクセサ `Read/Write{Column}Async`（＋ファイル糖衣 `Read/Write{Column}ToFile/FromFileAsync`）を追加生成する（`GenerateRepositories && ExcludeUnboundedBinaryColumns` かつ除外列があるときのみ）。O(チャンク) で、blob 全量をメモリに載せない唯一の手段。
- 契約の挿入先はリモート契約の有無で変わる。OFF なら全機能面 `I{Entity}Repository`。ON（GenerateRemoteContracts / Services）ならリモート面 `I{Entity}RemoteRepository` へ移設する（全機能面はリモート面を継承するので純粋に追加的。ファイル糖衣の `this` 対象も追従）。ランタイム共通の基底 `IRemoteRepositoryCore` には載せない（エンティティ固有のため per-entity 面のみ）。
- 契約・薄い委譲実装（QuickER 2 方言＋InMemory は固定 infra のエンジンへ委譲。列名は `EntitySaveMetadata` 経由で焼き込まない）・EF Core 版 Repository は `NotSupportedException`・ファイル糖衣は拡張メソッド静的クラス（Stream 版へ 1 行委譲）を `CSharpGenerationModelBuilder.BinaryStream.cs` が整形済みブロックとして組み立てる。
- エンジンの細部は `src/QuickER.Runtime.SqlServer/CLAUDE.md`「無制限バイナリ列のストリーミング」と `src/QuickER.Runtime.Sqlite/CLAUDE.md`「無制限バイナリ列のストリーミング」。
- `CanSeek` でない Stream は `length` 必須（方言中立に統一。欠落は `ArgumentException`）。
- Stream はちょうど `length` バイトを返さなければならない。`UnboundedBinaryColumns.ResolveWriteLength` がシーク可能な Stream の残りと違う明示の `length` を断り、エンジンは Stream を `UnboundedBinaryColumns.WithExactLength` 越しに読む（SQL Server は SqlParameter へ渡す値・SQLite は blob へのコピー元・インメモリは MemoryStream へのコピー元）。外すと、SQL Server は `length` を無視して Stream の中身をすべて保存し、SQLite は先に確保した zeroblob の残りを 0 で埋めたまま true でコミットする（どちらも例外にならない）。
- `source=null` で SET NULL、行なし / NULL は false。INSERT 専用メソッドはなく「INSERT→Write の 2 段」。楽観排他はスコープ外。
- リモート時の HTTP でのストリーミング転送は `src/QuickER.Runtime.AspNetCore/CLAUDE.md`「バイナリ転送エンドポイント」。
- ガード：`BinaryColumn{Ado,EfCore,Remote}RuntimeTests`・`BinaryInMemoryFixtureRuntimeTests`（WithUnboundedBinary の取得・Include 併用例外・取得後 Update ガード・Stream の往復 / SET NULL / 行なし / 非シーク length 必須 / ファイル糖衣）、`SqlServerBinaryColumnRuntimeTests`、実装先の一致のシナリオ `blob-stream-contract`（QuickER 版〔SQLite〕・インメモリ・リモート）、`BinaryRemoteLengthRuntimeTests`。mutation 実証済み。

## パッケージの依存集合と公開 API 面

- ランタイムパッケージ 7 csproj の依存集合は完全一致で固定する。
  - Core：PackageReference ゼロ。
  - SqlServer：Microsoft.Data.SqlClient＋System.Security.Cryptography.Pkcs ピンのみ。
  - Sqlite：Microsoft.Data.Sqlite＋SQLitePCLRaw ピンのみ。
  - EntityFrameworkCore：Microsoft.EntityFrameworkCore.Relational のみ。
  - InMemory：PackageReference ゼロ（Core への ProjectReference のみ）。
  - Sync：PackageReference ゼロ（Core への ProjectReference のみ）。
  - AspNetCore：PackageReference ゼロ＋FrameworkReference は `Microsoft.AspNetCore.App` の 1 つだけ（他 6 つは FrameworkReference を持たない逆表明も固定。共有フレームワーク参照は参照側プロジェクトの種別を縛るため）。
  - DI 系・ADO・他方言を足すとガードが落ちる。依存を足す前に「それは本当にパッケージの責務か（スキーマ依存物ではないか）」を疑う。
  - ガード：`RuntimePackageProjectDependencyGuardTests`。
- ランタイムパッケージ 7 種の公開 API 面はスナップショットで固定する（`tests/QuickER.Tests/PublicApi/{アセンブリ名}.approved.txt`）。
  - リフレクションで型・基底 / 実装インターフェイス・public / protected メンバーのシグネチャ・省略可能引数の既定値・定数値・列挙体の値を正規化した CRLF テキスト。属性・XmlDoc・NULL 許容注釈・コンパイラ生成物は対象外。正規化仕様の正本は `PublicApiSnapshot` の XmlDoc。
  - ロックステップ版で「同一メジャー内は互換」を SemVer で宣言している以上、メンバーの削除・シグネチャ変更は利用者のコンパイル済みコードを黙って壊す。依存集合・型集合（`CodegenCaseTests` の行 `code-split-runtime-files-hold-no-schema-dependent-types`）に加えて面そのものを固定する。
  - 承認ファイルの更新は既存フィクスチャと同じ 1 コマンド（`QUICKER_REGEN_FIXTURES=1` ＋ `--filter "FullyQualifiedName~Drift"`）。再生成は承認であって検証ではない。差分が出たら先に「利用者を壊す変更か」を判断する。
  - スナップショットはコンパイル済みアセンブリを読むため、テンプレート変更がパッケージの公開 API 面を変えるときは「regen → 再ビルド → regen」の 2 回が要る。1 回目は旧アセンブリの面で承認してしまい、環境変数なしの検証実行でスナップショットだけが赤になるのが正常な合図。2 回目の承認前に差分の目視判断を飛ばさない。

## トランザクション失敗経路

- 生成 Repository のトランザクション失敗経路は、catch から共有ヘルパー `SqlTransactions.RollbackQuietlyAsync` だけを呼ぶ（Save 単一 / 複数・SQLite の BulkInsert / blob write・ExecuteDelete の 5 箇所）。コミット後処理（`versions.Apply()` / `AcceptChanges`）は try の外に置く。
- ヘルパーは「完了済み（`Connection is null`）なら何もしない・Rollback 自身の例外は握り潰す・常に `CancellationToken.None`」。完了済みトランザクションへの Rollback は `InvalidOperationException` になり、catch から出た例外が伝播中の元の例外を置き換えて、失敗原因が完全に消える（ビルドでも型検査でも出ない）。
- ガード：`SqlTransactionRollbackTests`（実 SQLite の挙動と生成コードの構造）。

## グラフ保存

### 書く順

- グラフ保存の書く順は `GraphSaveOrder`（Core）の規則 1 つで、4 つの実装先が同じ順に保存し、Save フックも同じ順に走る。
  - 行を削除しない保存はグラフの順（各ルートをたどり、新しい行と更新する行は子より先）。
  - 行を削除する保存は 3 段：①削除しない書き込みをグラフの順に（削除する行と後回しの行は、その下の行ごと飛ばす）②削除をたどった順に（サブツリーは子から）③後回しの行を、その下の行と一緒にたどった順に。
  - `GraphSaveOrder.For` が根を 1 回たどって `StartingPoints`（削除も後回しもしない根 → `Deletions` → `HeldBack`）を作る。歩く側はその始点から順に歩き、子のループで `Defers(child)` の子を飛ばす（後回しの行の下の削除も 2 段目へ回し、3 段目の歩きでは飛ばす。後回しの行の下の行は個別には後回しにしない）。
- 後回しにするのは、同じ保存で削除する行がまだ持つ値を取る行だけ。
  - 削除する行の主キーか一意な値（`[Key]` と `[UniqueConstraint]`）を取る新しい行と、一意な値（主キーを除く）を取る更新する行。
  - 値は削除する行と、削除が連鎖するときはその下の既存の行から集める（新しい行の値は集めない）。`IncludeKeys.Normalize` の形で、文字列は大文字小文字を区別せずに比べ、null を含む値は何にも重ならない。`[UniqueConstraint]` は属性型を出す構成でだけ読む。
- 削除を後ろへ回すので、子を別の親へ移して元の親を消す保存が根の並びに依らず通る。削除が先に走ると、外部キーに断られるか、CASCADE が移した行まで消して続く更新が偽の NotFound になる。
- 段の中はグラフの順なので、新しい行を参照する行はその後に書く。値が重なる新しい行を、それより前に書く行が参照する形は 1 回では通らない（docs の既知の制限＝3 回の保存に分ける）。2 行の一意な値の入れ替えも DB が最初の更新で断る（インメモリは通す）。挿入する行（新しい行・insertWhenUpdateMissing の挿入）が、後ろに渡した更新する行の手放す代替キーを取る形も、QuickER 版は挿入で断られ、EF Core 版（代替キーを先に書く）とインメモリは通す（docs に書いた受け入れた差。手放す行を先に渡せばどれも通る）。逆に、代替キーを手放す行を参照する行を同じ保存で別の行へ向け直す形は、QuickER 版は向け直す行を先に渡せば通り、EF Core 版は渡す順に依らず外部キーに断られる（docs に書いた受け入れた差。`src/QuickER.Runtime.EntityFrameworkCore/CLAUDE.md`「変わった代替キーを自分で書く」）。
- キーも同じで、同じ保存で行を削除し同じキーの行を足すと置き換わる（DB に無いキーでは新しい行が残る）。EF Core は同じキーの削除と挿入を 1 つの UPDATE に畳むので、戻り値の行数だけが 1 になる（BackendParity の既知の違い）。
- 実装：QuickER 版は `EntityGraphSaver.SaveGraphsAsync`（Repository の単一・複数の入口が通る。既存の `SaveAsync` はグラフの順のまま）、インメモリは Before の走査と書き込みの走査が同じ計画を歩く。
- EF Core で 3 段に従うのは記録の順（＝フックの順）だけで、文の順は 1 回の SaveChanges の中で EF Core が決める。補いは `src/QuickER.Runtime.EntityFrameworkCore/CLAUDE.md`「書く順への対応」「変わった代替キーを自分で書く」（試行ごとの外部キーの読み取りと、一意制約ごとの `HasIndex(…).IsUnique()`。削除する行の値を代替キーで取る行があるときは、3 段目を 2 つめの文脈の SaveChanges で書く。分ける判定は `GraphSaveOrder.TakesDeletedValue`＝後回しの判定の本体を列で絞ったもの）。
- ガード：`GraphSaveOrderTests`、`BackendParityRuntimeTests` のシナリオ（`graph-delete-then-add-same-unique-value-*`・`graph-remove-and-add-same-key-*`・`graph-move-*`・J3 の `graph-updated-row-takes-deleted-unique-value-*` と、子を持つ形の `graph-updated-row-takes-unique-value-of-deleted-row-with-children-*`、新しい行を後回しにしない `graph-new-row-referenced-by-another-root-*`・`graph-save-new-row-without-shared-value-keeps-order`、順序を固定する `graph-save-*-keeps-order` など）、`GraphSaveEfCoreSqlServerRuntimeTests`、`GraphSaveSqliteFileRuntimeTestsBase.NullUniqueValues_DoNotOrderTheSave`、`BackendParitySqlServerRuntimeTests`（`move-stale-note-and-delete-old-gadget`）、`InMemoryConcurrencyRuntimeTests.SaveAsync_RemoveAndAddSameKeyInOneSave_KeepsNewRow`。mutation 実証済み。

### 削除した子の外れ方

- グラフ保存が成功したら、削除した子は残った親のカスケードのコレクションから外れる（4 つの実装先で同じ）。
  - 子の RowState は Removed のまま。Save フックが飛ばした子は残す。単一のカスケードの子（1 対 1）は外さない。削除したルートの子孫には触れない（`EditModelCollection` の `AcceptChanges` と同じ規則）。
  - 規則の本体は Core の `CascadeCollections.RemoveDeletedChildren` 1 つ。ADO の 2 方言・インメモリ・EF Core の `EntityGraphSaver.AcceptChanges` と、リモートのクライアントの `AcceptChanges` が呼ぶ。
- ガード：`BackendParityRuntimeTests` の `graph-remove-child-through-root`・`graph-remove-children-one-skipped-by-hook`・`graph-remove-root-with-children`、`BackendParitySqlServerRuntimeTests.DeletedSingleChild_StaysOnItsNavigation`。mutation 実証済み。
- EF Core 側の補い（保存が渡したエンティティのナビゲーションを変えない・`ClientNoAction`・削除フックの順）は `src/QuickER.Runtime.EntityFrameworkCore/CLAUDE.md`「渡したエンティティのナビゲーションを変えない」「削除の振る舞いとフックの順」、リモートが飛ばした行を応答に載せる規則は `src/QuickER.Runtime.AspNetCore/CLAUDE.md`「保存の応答は列の値を運び位置で突き合わせる」。

### 整合の検査

- グラフ保存は、外部キーとナビゲーションが食い違うグラフと、同じ行を 2 回書くグラフを、書く前に 4 つの実装先で同じ規則で断る。規則の本体は Core の `GraphConsistency.Check` 1 つ。
  - 入口：QuickER 版の `EntityGraphSaver.SaveGraphsAsync`・インメモリの `SaveRootsAsync`・EF Core のグラフ保存（DbContext を作る前）・リモートのクライアントの `SaveAsync` の単一と複数（送る前＝サーバーまで届けて 500 にしない）。
  - たどるのは保存がたどる行だけ（削除する行は `cascadeDelete` のときだけ下へ、ほかは `cascadeSave` のときだけ下へ）。
- 外部キーとナビゲーションの食い違い：親が持つナビゲーション（単一・コレクション）の子の外部キーが親の主キーと違えば `InvalidOperationException` で断る（削除する子は見ない。変更の無い子も見る）。
  - 新しい子で外部キーが既定値のものだけは、断らずに親のキーで埋める（`Fill`）。埋めるのは子の Before のフックの直前なので、親の Before のフックが決めたキーが子へ届く（QuickER 版は `SaveNodeAsync` の挿入の枝・インメモリは Before の走査・EF Core は Before のループの挿入。フックが無ければ書く前にまとめて `FillAll`）。
  - 2 つめの別の親に持たれた新しい子は断る。
- 同じ行を 2 回書く：同じ (型, 正規化したキー) の行に複数のインスタンスで届くとき、書き込み（更新・新しい行）が 2 つ以上、削除が 2 つ以上、削除と更新の両方、のどれかなら断る。
  - 削除と新しい行の組（置き換え）は断らない（`GraphSaveOrder` の後回しが置き換えにする）。
  - どのインスタンスも変更を持たなければ、2 つめ以降を EF Core が追跡しない（`IsUntracked`。追跡すると同じキーの 2 つのインスタンスで EF Core が断る）。キーが未設定の行は数えない。
- 同じインスタンスに 2 つの道で届く：保存の 2 つのコレクションに持った同じインスタンスや、複数のルートの保存の一覧に 2 回渡した同じ根が、自分の変更（新しい行・更新・削除）を持てば、`Collect` が 2 度目に届いたところで断る（キーが未設定の新しい行も）。
  - 変更の無いインスタンスへの 2 度目は今どおり通す（削除の下の変更の無い行も、書くのは削除 1 回だけ）。
  - 外すと、QuickER 版とインメモリは道ごとに書き（2 回の INSERT は主キーで断られ、2 回の UPDATE は 2 行と数える）、EF Core は 1 回だけ書き、リモートのクライアントは 2 回送ってサーバーに断られる。EF Core で同じ行を持つ 2 つのナビゲーションを Include して編集したグラフがこの形になる（docs のグラフ保存と EF Core モード・CHANGELOG の Breaking）。
  - ガード：`BackendParityRuntimeTests` のシナリオ `n3-same-instance-two-collections-new`・`-update`・`-delete`・`n3-same-instance-twice-in-roots`（5 実装先とも書く前に断り、フックは鳴らない）。mutation 実証済み。
- 外すと、実装先ごとに保存の結果が割れる（QuickER 版とインメモリはナビゲーションを無視して外部キーで書き、EF Core は fix-up でナビゲーションを採り、同じ行の 2 回の書き込みは後勝ちか追跡の衝突になる）。
- 乱数の列では食い違うグラフを作らない（どの実装先も断るので、後の列が何も比べなくなる）。
- ガード：`BackendParityRuntimeTests` のシナリオ `n2-navigation-overrides-foreign-key`・`n2-unchanged-child-foreign-key-differs`・`n2-parent-reference-differs`・`n2-new-children-take-parent-keys`・`n2-hook-assigned-keys-reach-new-children`・`n3-same-row-one-instance-changed`・`n3-same-row-both-instances-changed`、`RemoteSaveResponseClientTests.Save_InconsistentGraph_IsRejectedBeforeSending`。mutation 実証済み。

### AcceptSaved

- EditModel が保存を受け入れるのは Mapper の `AcceptSaved` で、保存の後の Entity の状態を 1 行ずつ見て決める。`editModel.AcceptChanges()` の意味は変えない（保存を見ずに全部を受け入れる）。
  - 生成 `ApplyToEntity` の最後の `RememberApplied` が、EditModel に自分から作った Entity を覚えさせる（Core の internal。読み込み〔`ApplyToEditModel`〕と受け入れで捨てる）。子は位置でなくこの対応でたどる（保存は削除した子をコレクションから外すので、位置ではたどれない）。
  - Entity が Unchanged＝書いた行は、列を読み戻して（`LoadSavedColumns`＝既定は `LoadColumns`。子のコレクションは作り直さない）Unchanged にする。読み戻しを外すと、版が古いままで 2 回目の保存が偽の競合になり、Before フックが書いたキーも戻らない。
  - Added・Updated のまま＝フックが飛ばした行は、状態を残す。
  - Removed＝削除した行。根と単一のカスケード子は、フックが飛ばした削除と見分けられないので削除済みとして扱う（docs の Save フックの節）。コレクションの子の削除は、親の Entity のカスケードの子に残っていればフックが飛ばしたとみなして削除待ちのまま残す（`Remove()` は追跡に、`MarkRemoved()` はコレクションに）。
- 除外した無制限バイナリ列がある図は、EF Core のリポジトリを生成しない構成でだけ、生成 Mapper の `LoadSavedColumns` が読み戻しの前に Entity の除外列を未取得の値へ戻す（NOT NULL の非 VO＝空配列・NULL 許容＝null・NOT NULL の VO＝`null!`）。外すと、保存した Entity を使い回した次の保存が更新ガードで止まる。
  - EF Core を生成する構成（QuickER 版と併せる構成も）では出さない。EF Core は全列を読み書きするので、戻すと次の保存が空の値で blob を黙って消す（併せる構成で blob を持つ EditModel を QuickER 版で保存すると更新ガードが断る＝docs に明記）。
- Added のまま削除の印を付けた行（`IsDiscardedNewRow`＝RowState の setter が Added から Removed へ変わったときだけ立てる）は、Entity のグラフに入れない（コレクションは `CreateEntities` の絞り込み・単一の子は `IsLeftOutOfSave`）。入れると、キーが未入力の行は `Required` で例外になり、キーのある行は存在しない行の削除になる。受け入れはその行をコレクションから外す（挿入し直さない。単一の子はナビゲーションに残る）。
- ガード：`EditModelSaveRuntimeTestsBase`（ConcurrencyFixture・7 シナリオ × インメモリ・リモート・SQL Server・EF Core の派生）、`BinaryVoEditModelRuntimeTests.AcceptSaved_NewRowWithBlobs_NextSaveKeepsStoredBlobs`、`BinaryColumnEfCoreRuntimeTests.AcceptSaved_ThenSaveAgain_KeepsStoredBlobs`、`CodegenCaseTests` の行 `exec-accept-saved-excluded-binary-override-*`（QuickER 版だけ・インメモリだけでは出て、EF Core だけ・QuickER 版と併せる構成では出ない）、`InMemoryFixtureRuntimeTests.EditModel_SingleChildAddedThenMarkedRemoved_IsLeftOutOfTheSave`。mutation 実証済み。

### Save フックが無制限バイナリ列を書いたとき

- Save フックが無制限バイナリ列を書いたら、保存が書いた行の版をコミットの前に読み直し、コミットの後に反映する。
  - `SaveHookSession` は After に渡す context を内側の型 `WriteNotingContext` で包み、`WriteBinaryColumnAsync` が呼ばれたら（成否に依らず）`BinaryColumnWritten` を立てる（ファイル糖衣はインターフェイスの既定実装が包みの `WriteBinaryColumnAsync` を呼ぶので同じく立つ）。
  - blob の書き込みは UPDATE が触らない列なので、行のほかの値はエンティティのままで正しく、版だけが進む。保存の文の `OUTPUT` が返した版のままでは、同じインスタンスの次の保存が偽の競合になる。
- `ExecuteSqlAsync` では立てない。SQL はどの列でも変えられるので、版だけを読み直すと、フックが書いた列を次の保存が古い値で黙って上書きする。行の全列の読み直しは、監査ログの行の追加というよくある使い方の保存を毎回重くするので採らない。SQL で保存した行を書くと、次の保存は従来どおり競合になる（docs で、Before でプロパティに入れるよう案内）。
- 読み直す範囲を変更のない行へ広げてはいけない。自分の UPDATE で守られていない行の版を取ると、読んだ後にほかの利用者がした変更を取り込み、次の保存がそれを黙って上書きする。
- 読み直した版をコミットの前にエンティティへ書いてはいけない（コミットが失敗すると DB に無い版が残る）。
- EF Core は blob を書けない（`WriteBinaryColumnAsync` は NotSupported）ので読み直しを持たない。インメモリは元から公開後のスナップショットから版を読み直す。
- SQL Server の読み直しは `src/QuickER.Runtime.SqlServer/CLAUDE.md`「Save フックが書いた blob の版の読み直し」、SQLite のセーブポイントは `src/QuickER.Runtime.Sqlite/CLAUDE.md`「無制限バイナリ列のストリーミング」。
- ガード：SQL Server の 2 者比較 `BackendParitySqlServerRuntimeTests` のケース（`hook-sql-stamps-same-row`・`hook-sql-stamps-saved-parent`・`hook-without-writes-no-reread`・`hook-audit-row-no-reread`）、`SaveHookBinaryWriteSqlServerRuntimeTests`、`SaveHookBinaryWriteSqliteRuntimeTests`。mutation 実証済み。

## 重複の事前チェックの NULL の規則

- 重複の事前チェックの NULL の規則は、方言の宣言を QuickER 版 Repository が共有の照合へ渡す。
  - `UniquenessChecker.CheckAsync` の `nullValuesCollide` の多重定義が本体。真なら NULL を含む組も照合する（等値の条件は、値の側の null を `IS NULL` へ補償する翻訳を通るので、同じ構成列に NULL を持つ行が見つかる）。
  - 方言の規則は `RepositoryDialectVariables.UniqueConstraintNullsCollide`（SQL Server＝true・SQLite＝false。テンプレート変数 `dialect_unique_nulls_collide`）で、方言の Repository の `CheckUniquenessAsync` だけが渡す。EF Core とインメモリは引数の無い多重定義（NULL を含む組を外す）のまま（EF Core は SQL Server でも外す＝docs の実装先の違い）。
  - 外すと、SQL Server の UNIQUE 制約が断る 2 行目の NULL を事前チェックが見落とし、同期の `ExplainServerRefusalAsync` が断られた書き込みの理由を見つけられず、元の例外（2627）を投げ直して毎回止まる（Versioned＋ServerWins だけは送らずに捨てるので止まらない）。EditModel の `ValidateUniqueAsync` も同じ照合を通る。
- ガード：`SyncSqlServerRuntimeTests.NullUniqueValueTakenOnServer_IsReportedOrSettled`・`ServerCheckUniqueness_ComparesNullsAsValues`、`SyncSqlServerHttpRuntimeTests.NullUniqueValueTakenOnServer_IsReportedOverHttp`、`UniquenessCheckRuntimeTestsBase` の 3（期待は SQL Server の派生 `UniquenessCheckSqlServerRuntimeTests` が上書きする＝条件スキップでなくサブクラスで分ける）と同じ派生の `ValidateUnique_SecondNull_IsReported`、`UniquenessGenerationTests.CheckUniqueness_PassesDialectNullRule`。図は SyncFixture の `sync_markers`（NULL を許す単一列と 2 列の複合の一意制約）。mutation 実証済み。
- 重複事前チェックの契約と実装は基底に 1 回だけ置く（宣言はランタイム共通の `IRemoteRepositoryCore<TEntity,TKey>`、実装は 4 つのリポジトリ基底と `HttpRemoteRepositoryCore`）。per-entity に残るのは `UniquenessConstraintSet<TEntity>` と `CollectCustomUniquenessChecks` partial フックへの橋渡しのみ。基底の実行順は「制約テーブル→カスタムフック」。

## Include とカスケード削除のキー照合

- Include とカスケード削除の CLR 側のキー照合は `IncludeKeys.Normalize` を通す。主キーと外部キーの CLR の型は一致するとは限らない（SQLite の `INTEGER` 主キー＝long を `INT` の外部キー＝int が参照する・両端の値オブジェクトの型が違う）。箱詰めした値は `(object)1L` と `(object)1` が等しくならないため、素の値で辞書を引くとナビゲーションがエラーなしに空になり、インメモリのカスケード削除は子を消し残す。
  - 正規化は、値オブジェクトを開き、long に収まる整数を long にそろえ、`byte[]` をバイト列の 16 進文字列にする（配列は参照で比べられ、同じキーの 2 回の読み出しが一致しない）。Core 在住。
  - 正規化した値は突き合わせ専用。SQLite の `IncludeLoader` が子の IN 句へ束縛するのは元の値（`binary` のキーだけはバイト列、それ以外は従来どおり正規化した値）。16 進文字列を束縛すると列のどの値にも一致しない。
  - SQLite の `IncludeLoader` の 3 か所とインメモリの `IncludeAttacher`・`InMemoryCascade` が共有する。SQL Server は FOR JSON の相関で SQL 側が比べるので該当しない。
- SQLite の 3 か所の扱いは `src/QuickER.Runtime.Sqlite/CLAUDE.md`「Include のローダ（IncludeLoader）」、インメモリは `src/QuickER.Runtime.InMemory/CLAUDE.md`「ストアのキー比較」。
- ガード：`TranslatorParity{SqlServer,Sqlite,InMemory}RuntimeTests` の `IncludeAcrossKeyTypes_AssemblesRelations` / `CascadeDeleteAcrossKeyTypes_RemovesChildren`、`IncludeKeysTests`。mutation 実証済み。

## 更新できる列が無い表の UPDATE

- 更新できる列が 1 本も無い表の UPDATE は、キー列を自分自身へ代入する（`EntitySaveMetadata.Build` の `updateSetClause`）。
  - キー列と、更新から外れる列（行バージョン・計算列・除外したバイナリ列）だけの表で、代入を素のまま `string.Join` すると `UPDATE t SET WHERE …` になる。その表の `UpdateAsync`・グラフ保存の更新・同期の後勝ちの再生が必ず構文エラーになる。SQLite では主キーだけの `tags(name TEXT PRIMARY KEY)` というありふれた形。
  - 自己代入にしたのは、影響行数・版の照合（`AND [row_ver] = @originalRowVersion`）・`OUTPUT INSERTED` を読む全経路をそのまま使えるため（SELECT による照合へ切り替えると、影響行数を読む非版の経路が成り立たない）。
  - 実 SQL Server で確かめた前提：同じ値への主キーの更新は `ON UPDATE CASCADE` を実行しない（子の行は書き換わらず、子の rowversion も進まず、子の UPDATE トリガーも動かない。値を変える更新では子の版が進みトリガーが動くことを対照として確認）。NO ACTION の外部キーに参照された主キーでも拒否されない。その表自身の行は更新されるので、版つきの表では版が進む。
  - 既知の制限：主キーが IDENTITY の取込表は自己代入を拒否されるので更新できない（DB の自動採番は対象外）。
- ガード：`UpdateSqlWithoutAssignableColumnsTests`（両方言の文・版つきの照合と OUTPUT・列のある表は不変）、`TranslatorParity{SqlServer,Sqlite,InMemory}RuntimeTests` の `UpdateOfKeyOnlyTable_ReportsWhetherTheRowExists` / `UpdateOfVersionOnlyTable_Succeeds`、SQL Server だけの `VersionOnlyTable_GuardsTheVersion`。mutation 実証済み。

## ドライバが返した値の型変換

- ドライバが返した生の値の型変換は `Convert.ChangeType` 単独に任せない。`TimeSpan` / `Guid` / `DateTimeOffset` / `DateOnly` / `TimeOnly` は `IConvertible` を実装しないので、これらを TEXT で返すドライバ（Microsoft.Data.Sqlite は 5 種すべて TEXT 格納）では、値の中身に関係なく必ず `InvalidCastException` になる。SQL Server の ADO は型付きで返すので型一致で素通りし、単体テストでも露見しない。
- 共有変換は `RawValueConverter.ConvertRaw`（Core パッケージ収載）。次の経路が通る：
  - 値オブジェクトの再ラップ（`SqlValueObjectActivator.ResolveFactory`）。
  - 生 SQL スカラー（`RawSqlMapper.ConvertSingleValue`）。
  - 生 SQL 射影 DTO（`RawSqlMapper.CoerceProjectionValue`）。
  - EF Core 版の生 SQL のエンティティ取得（`EntitySaveMetadata.SetColumnValue` の EF Core のスコープ＝`ef_core` かつ SQLite の QuickER 版 Repository を同居させない構成。値オブジェクトでない列を `ConvertRaw` へ通す）。
  - 同期のキーのテキストの読み取り。
  - 方言エンジン側の `EntitySaveMetadata.CoerceScalar`（SQLite 専用・素の列用）も `ConvertRaw` への委譲。変換失敗だけを SQLite の文言の `InvalidOperationException` へ包む。
- 列挙型と、ドライバが `DateTime` / `TimeSpan` で返す `DateOnly` / `TimeOnly` も `ChangeType` の手前で扱う。列挙型は `Type.GetTypeCode` が基底の整数型を返すので、`ConvertInput` の数値の解析より前に分岐する。時刻を持つ `DateTime` と 1 日の範囲外の `TimeSpan` は、切り詰めずに拒否する。
- `ConvertRaw`（DB から来た値）だけは、日時の TEXT を `AdjustToUniversal`（オフセットつきは UTC・なしは Unspecified）で読み、16 バイトの `byte[]` を `Guid` として読む。Microsoft.Data.Sqlite の `GetDateTime` / `GetGuid`（GetById・GetAll の速い読み取り）と同じ読み方にそろえる。外すと、同じ格納値が `Query()`・値オブジェクトの再ラップ・生 SQL では Local の日時になり、BLOB の Guid は読めない。`ConvertInput` は人が書いた値なので対象外。
- 行マッピング `SetColumnValue` は「VO は `Wrap`・それ以外は `CoerceScalar`」で分岐する（SQLite の値オブジェクトありの `MapEntityObject` は列ごとにこれを呼ぶ）。VO 有効時に無条件 `Wrap` にすると、`ISqlExecutor.QueryBySqlAsync<TEntity>` へ渡された手書きエンティティの素の列に格納値の文字列がそのまま渡り `ArgumentException` になる。
- 格納値を値オブジェクトが拒んだときは、どの読み取り経路でも列を名指しする。文言の正本は `EntitySaveMetadata.ColumnMappingFailure`（`InvalidOperationException`・内側が `ValueObjectValidationException`）。経路は 3 つ：`SetColumnValue`、速い読み取り（`BuildColumnAssign` の値オブジェクトの分岐が `Create` を式木の `TryCatch` で包む）、SQL Server の FOR JSON（`src/QuickER.Runtime.SqlServer/CLAUDE.md`「Include の無い取得と FOR JSON」）。
- ガード：`RawValueConverterTests`、`SqliteValueConversionRuntimeTests`（CI 常時。「値オブジェクトが拒む格納値はどの経路でも列名つきの例外になる」を含む）、`TranslatorParitySqliteRuntimeTests.RawSqlEntity_MalformedStoredText_ThrowsInvalidOperationWithFormatInner`、`EfCoreRawSqlValueConversionTests`、`IncludeGraphSqlServerRuntimeTests`。mutation 実証済み。

## 値オブジェクト

利用者向けの意味論と手書きの値オブジェクトのレシピは `docs/code-generation.md`（.ja）の「値オブジェクト」の各節。

### Create / TryCreate / Validate

- 値オブジェクトの `Create` / `TryCreate` / `Validate` の本体は `ValueObjectBaseCore<TSelf, TValue>` に 1 回だけ置く。各型は `New`＋`ValidateCore` の明示的実装と partial フック宣言（`OnValidate`・`GetDefinedInstance` / `ConvertCustomInput`・`ConvertAbsentInput` のブリッジ込み）だけを持つ。基底クラスから継承した静的メソッドが `static abstract` インターフェイスメンバを満たすことに依存する。
- この構造のため、`Create` をリフレクションで名前解決する箇所（`SqlValueObjectActivator.ResolveFactory`・materializer の `ResolveValueObjectReader`＝SqlServer / Sqlite の 2 エンジンに展開）は `BindingFlags.FlattenHierarchy` 必須。外してもビルドは通るが、基底宣言の静的メンバが見えなくなり、`Wrap` は生値を素通しし、高速経路は静かに `SetColumnValue` フォールバックへ落ちる。
- ガード：`HandwrittenValueObjectTests`（A-1 の 4 ケース）。mutation 実証済み。

### 検証規則

- 検証規則の本体は Runtime の共有ルールクラス 4 つに 1 回だけ置く：`ValueObjectRules`（必須チェック。bool 返しで後続規則を打ち切る）、`ValueObjectStringRules`（MaxLength・半角英数＋許可記号・メール実用検証）、`ValueObjectNumberRules`（整数桁数・範囲）、`ValueObjectDecimalRules`（precision / scale・Pow10 テーブル比較）。
  - per-VO の `ValidateCore` は、共有ヘルパ呼び出し（定数は引数渡し＝総称ディクショナリ参照を増やさない）＋ `OnValidate` の定型のみ。
  - 文言フック 5 種（`Customize{MaxLength,Scale,Precision,ValueRequired}ErrorMessage`＋`CustomizeDisplayName`）は代替の拡張点を新設せず廃止した。文言の正本は `ValueObjectValidationMessages`（全エントリの第 1 引数が displayName。既定文面には埋め込まない。差し替え側が分岐・埋め込みに使う材料として渡す）。
  - 型ごとの文言・表示名は `displayName == XxxValue.DisplayName` 比較 / `GeneratedDisplayNames.Resolve` の `nameof(Entity.Prop)` 比較で行う（型・プロパティが消えるとビルドエラーになるのでコンパイル安全）。
- 拡張点の一般基準は「他の手段で実現不能な生成パイプラインへの挿入のみ。ただし上位基準は利用者の使いやすさ」。意味系フック（`OnValidate` / `GetDefinedInstance` / `ConvertCustomInput`）は存続する。汎用検証ヘルパ 4 種（桁数・範囲・半角英数・メール〔'@' 1 つ・前後非空・空白なしの実用最小で、RFC 完全検証は意図的にしない〕）は、利用者の `OnValidate` から呼ぶ public API。
- 成功経路の割り当てゼロ（`ref List<string>?` 素通し）は共有後も維持する（割り当て実測テストが固定）。

### 生値変換の型独自入力

- 生値変換の型独自入力（`TryConvertCustomInput`）は、基底の `TryCreateFrom` が `TSelf.` 経由で照会する。具象型名で 4 引数 `TryCreateFrom` を呼んでもフックが効くのはこの照会があるから。per-VO 側の実装だけで受ける形へ変えると、型名呼びが基底の継承メソッドへ静的束縛されて迂回が再発する。
- 利用者拡張は生成 VO では partial フック（`GetDefinedInstance` / `ConvertCustomInput`）経由にする。`TryGetDefined` / `TryConvertCustomInput` の明示的実装を利用者 partial に直書きすると、生成ブリッジと衝突してコンパイルエラーになる（意図的。シグネチャ間違いが静かな no-op にならない）。
- フックの中から `Create` / `TryCreate` / `TryCreateFrom` / `CreateFrom` を呼ぶと再帰になり、catch 不能の StackOverflow を起こす（XmlDoc と docs に明記）。
- 空欄入力の型引き取り（`TryConvertAbsentInput` / partial フック `ConvertAbsentInput`）も同じ構造にする。基底 `TryCreateFrom` の空欄早期リターンの内側で `TSelf.TryConvertAbsentInput` を照会する。`ConvertCustomInput` の契約「raw は非空欄」は不変（空欄をそちらへ流す形に変えない）。
  - 効くのは `TryCreateFrom` / `CreateFrom` だけで、EditModel の空欄入力（`TryCreate` 経路）と DB の NULL には効かせない（意図的な非対称。docs の値オブジェクト節に明記）。
- ガード：`ValueObjectScenarioTests` の行（vo-same の `convert-hook-*`〔全呼び形〕・`bridge-generated-trycreatefrom-four-args-custom`〔実生成 VO のブリッジ〕・`readcell-defined-name-*`〔名前からの生成〕・`absent-hook-*`・`absent-bridge-generated-*`・`absent-handwritten-*`、vo-call の `trycreatefrom-blank-*`〔未実装型の挙動不変〕）、`ValueObjectAbsentInputTests`（割り当てゼロ）。mutation 実証済み。

### 定義済みインスタンス（[DeclaredInstance]）

- 定義済みインスタンスの宣言的定義（`[DeclaredInstance]`）は、フックの差し込み口を消費しない別駅として基底に実装する。実体は固定ランタイムの `DeclaredInstanceRegistry<TSelf, TValue>`（internal）で、属性付き public static readonly 自型フィールドを初回 1 回リフレクションして線形走査表にする。属性なし型はフラグ 1 つで素通りする。
- 挙動に関わる照会は基底の 2 箇所：`Materialize` の「フック→レジストリ→new」の 3 段と、`RunValidation`（`ValidateCore`＋メンバーシップ検査。Create / TryCreate / Validate の共有ボディ）。ほかに `GetDeclaredInstances` / `DeclaredDisplayName` の読み出し口がある。
- per-VO 生成出力はこの機能に 1 バイトも関与しない。新しい partial フックを足さない（機構をフック実装として per-VO へ移すと、利用者の同名フックとコンパイル衝突する。ソースジェネレータ案を退けた理由「OnValidate / GetDefinedInstance の実装は 1 つ制約」を構造的に回避する設計）。
- 壊すと静かに回帰する判断 4 つ：
  1. 安全弁：走査中に null のフィールドを見たら、キャッシュせず null を返す。フィールドを `Create` で初期化した型の静的初期化中に走った走査が不完全な表を固定すると、以後その型の集合が永久に欠ける。
  2. `ValidateMember` の null ガードは `_valueIsNullable &&` が必須。非制約ジェネリックの `value is null` は Debug / tier0 で値型をボックス化し、生成ホットパスの割り当てゼロが破れる。
  3. 検査は `ValidateCore` の後に追加で走る（置き換えると OnValidate との合成が壊れる）。引き当てはフック優先。
  4. byte[] は走査時 fail-fast（`EqualityComparer<byte[]>` は参照等値なので、集合が静かに壊れる）。不正な形（非 readonly 等）・重複値も初回使用時に fail-fast（黙って無視しない）。
- 既知の検出不能境界：初期化子なしの属性フィールド（恒久 null）は、「cctor 実行中の null」と原理的に区別できない（静的コンストラクタの完了状態を問う公開 API が無く、`RunClassConstructor` は再入でも完了済みでも同じ no-op）。fail-fast 対象に含めず、docs / 属性 XmlDoc へ明記し、挙動（初期化子なしのフィールドは検出されず、集合は無音で無効のまま）をテストで固定する。コンパイラの CS8618 / CS0649 が利用者側の検出網。
- 入力表記の追補（`InputText` / `ClaimsAbsent`）：基底 `TryCreateFrom` の 2 駅を拡張する（空欄：フック→`ClaimsAbsent`→null／文字列入力：フック→`InputText`→通常変換。いずれもフック優先を維持）。
  - 照合は Ordinal 完全一致・Trim なし・provider 不参照・string 入力のみ。非文字列は通常変換→Materialize のレジストリで同じインスタンスへ解決する。
  - `[Display]` の名前は入力として受けない（表示ラベルと入力記法の分離）。将来の自動パース混入を vo-call の行 `trycreatefrom-display-name-not-input` が検知する。
  - fail-fast は 3 種追加：`InputText` 重複・`ClaimsAbsent` 複数・空文字 `InputText`（空欄述語に先取りされ絶対に一致しない設定）。
- ガード：`ValueObjectScenarioTests` の宣言済みの行（vo-call・vo-same・vo-first-use と、入力表記の `inputtext-*`・`claimsabsent-*`・`hook-precedes-*`）、`ValueObjectDeclaredInstanceTests`（一覧・内部配列・静的初期化中の走査・初期化子なし・割り当てゼロ）、`ValueObjectDeclaredInputTests`（割り当てゼロ）。mutation 実証済み（`RunValidation` の検査除去と `Materialize` のレジストリ照会除去は独立の両アーム）。

### GuidKey の長さ検証

- GuidKey 値オブジェクトの長さ検証は、素通しした MaxLength が駆動する。GuidKey のとき MaxLength を無条件 null 化すると、「長さ検証は VO が担うから Entity へ `[MaxLength]` を出さない」という内部契約が破れ、検証がスタック全体から消える（宣言幅超過が INSERT の切り詰めエラーで初めて露見する）。`_01_ValueObjects.scriban` の既存 emission（`vo.max_length` 駆動）がそのまま効く。
- 列幅が正の値かつ 36 未満は生成時 Warning（`CodeGen_Warning_GuidKeyColumnShorterThanGuid`）で名指しする（引数なし `Create()` の採番 36 文字が必ず検証例外になるため。生成は続行）。Error にしない：`UseGuidKeyForStringPrimaryKey` は図の全 string PK に効くグローバル指定で、GUID を意図しない短い string PK（コード表等）が混在する図で機能ごと使えなくなる。手動キー供給の運用は正当。
- ガード：`CodegenCaseTests` の行 `probe-guidkey-create-generates-unique-guid` と生成テスト 3 本。mutation 実証済み。
