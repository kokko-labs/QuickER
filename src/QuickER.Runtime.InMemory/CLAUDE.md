# QuickER.Runtime.InMemory

インメモリ Repository の基盤（SQL を使わない擬似実装）の固定コードの規則。
方言に依らない共通の規則は `src/QuickER.Runtime/CLAUDE.md` にある。

## 直接 CRUD は RowState を触らない

- インメモリ Repository の直接 CRUD（Insert / BulkInsert / Update）は、引数の `RowState` を触らない。他 3 実装（SqlServer / EF Core / HTTP）と同じ契約で、確定させるのはグラフ保存成功後の `SaveAsync` だけ。
- 触ると、Added のまま Insert したエンティティが続く `SaveAsync` で「変更なし」に化ける。実 DB なら主キー重複で失敗するケースが、インメモリだけ黙って通る。
- `Store.Insert` の版書き戻しは別機構なので無関係。
- ガード：実装先の一致のシナリオ `direct-crud-leaves-row-state`（5 実装先）。mutation 実証済み。

## ストアのキー比較

- ストアは主キーを `InMemoryKeyComparer` で比べる。`binary(n)` の主キー（`byte[]`）は既定の等値では参照で比べられ、ストアは読み出しのたびに配列を複製して渡すので、読み戻したキーで自分の行を二度と見つけられない（`GetByIdAsync` が null・同じキーの 2 回目の挿入が通る・読み戻したエンティティの更新が行なし）。
- 比較器はバイト列を中身で比べ、それ以外は各型の等値のまま。ストアのキーそのものは変えない（`SaveConflictException.Key` などに出るキーの型も変わらない）。
- 使う場所：表の辞書、ステージングの overlay と touched（`(Type, object)` の組）、`ComposeView` の複製、バッチのキー集合、一意制約の自分自身の除外（`KeysEqual`）。キーで引く集合を足すときは必ずこの比較器を渡す（`new Dictionary<object, …>(Table(…))` の複製は比較器を引き継がない）。
- 述語の書き換え（`PredicateComparisonRewriter`）は、`byte[]` の `==` / `!=` に加えて、`byte[]` のコレクションに対する `Contains`（IN）もバイト列で比べる。
- ステージングの touched の比較器は、外しても観測できない（同じ行を別の配列で 2 度触れても、基準の行の照合〔`ReferenceEquals`〕が同じ結果になる）。理屈上の二重化として持つ。
- ガード：`BinaryKeyExecutionTests`（binary(4) の主キー＋UNIQUE の name の図を生成し、静的に型付けした検証コードと一緒にコンパイルして、インメモリと実ファイルの SQLite で取得・重複・更新・Include の両方向・IN・グラフ保存・一括挿入の同じキーの拒否・行数を同じ期待値で表明する）。mutation 実証済み。

## Include とカスケード削除

- Include のキー照合は `IncludeKeys.Normalize` を通す（`src/QuickER.Runtime/CLAUDE.md`「Include とカスケード削除のキー照合」）。`IncludeAttacher` と `InMemoryCascade` が共有する。
- `IncludeAttacher.AttachAll` は、Include ノードごとに関連テーブルを 1 回だけ読んで索引にする（エンティティごとの全走査をしない）。属性は `EntitySaveMetadata.NavigationAttribute` のキャッシュを使い、`Attach` は 1 件の `AttachAll` への委譲にする。
- ガード：`TranslatorParityInMemoryRuntimeTests` の `IncludeAcrossKeyTypes_AssemblesRelations` / `CascadeDeleteAcrossKeyTypes_RemovesChildren`。

## 保存単位のステージング

楽観排他の共通の骨格は `src/QuickER.Runtime/CLAUDE.md`「rowversion による楽観排他」。

### 版と擬似版

- インメモリは単調増加 8 バイトの擬似版（big-endian）で、`InMemorySaveStaging` の copy-on-write により保存単位を all-or-nothing にする。
- 版カウンタは戻さず `Interlocked.Increment` で進める。
- 版スタンプは版の値でなく行を記録し、公開後のスナップショットから読み直す。After の `WriteBinaryColumnAsync` が版をもう一段進めても、呼び出し元には最終版が渡る（値を配ると、成功直後の再保存が偽競合になる）。
- 単一 Update / Insert / BulkInsert / Seed は staging なしで、ストアへ直接書き、即時スタンプする。

### ステージング

- `InMemoryWriteScope` の Put / Insert / Remove と、After 経路の `WriteColumnValue` は、overlay（(型, キー)→スナップショット。null＝削除マーク）へステージングするだけで、ストアを書かない。
- 読みは overlay 優先→ストアの合成なので、保存は自分の書き込みだけを見る。同一保存単位内の二重更新競合・同キー二重 INSERT の DuplicateKeyError・Removed 後の Exists=false は、従来どおり成り立つ。
- first-touch でストアの行参照をベースラインへ記録し、全フェーズ成功後の `Store.Publish(staging, mode)` が lock 内で ①検証（ベースラインと現在行を `ReferenceEquals` 比較）→②一括適用→③版スタンプ解決 を行う。
- Phase 2 の例外も Phase 3（After）の例外も、ステージングを捨てるだけ。ストアを 1 度も書いていないので巻き戻す対象が無く、After 待機中に割り込んだ他者の更新を消さない（undo ジャーナル方式は無条件の書き戻しで他者の更新を消す）。

### 検証の判定順

- ①の判定順は (a) 重複キー → (b) 存否 → (c) 版で、存否が版より先。無くなった行に対する版比較は何も語れず、版が先だと rowversion を持つ型が (c) で止まって「行が消えている」事実に到達できない。SQL Server の UPDATE 0 行を `ExistsByIdSql` で「消えた」と「変わった」に分ける二分と同一の意味論。
  - (a)「ベース null＝この保存が挿入した行なのに現在は存在」は、mode に依らず `DuplicateKeyError`。
  - (b) は mode にも rowversion の有無にも依らず適用する。
  - (c) は残っている行にだけ効く。rowversion を持つ型かつ Optimistic のときだけ `SaveConflictException.Modified`（版なし型と ForceOverwrite は検証なしで後勝ち）。
- 後勝ちは復活までは含まない。更新として開始した行を他者が削除していれば `SaveConflictException.NotFound` で失敗する。さもなければ削除済みの行を UPDATE が蘇らせる。黙って捨てるのも不可で、rows は保存フェーズで計上済み・`AcceptChanges` も無条件なので、「戻り値 1 件・RowState=Unchanged・ストアに行なし」になる（実測）。
- staged が削除マークなら競合にしない（既に無い行の削除は実 DB でも no-op。版の有無に依らない）。
- 取り込み時に行が無かった削除（基準 null・staged 削除）は、検証も適用もしない。公開時にそのキーにある行は After 待機中に他者が入れた行で、この保存が見た行ではない。適用すると他者の行を消し、版あり×Optimistic では偽の Modified になる。判定は `InMemorySaveStaging.StartedWithoutRow`。
  - 同じ保存で同じキーを削除して足すと、削除が先に走る（`GraphSaveOrder`：削除する行のキーを取る新しい行は削除の後に書く）。取り込み時に行が無ければ削除は何もせず、新しい行が残る。並びに依らない。
- ガード：`InMemoryConcurrencyRuntimeTests` の `SaveAsync_DeleteOfAbsentRow_KeepsRowInsertedDuringAfter` / `SaveAsync_VersionedDeleteOfAbsentRow_IsNotAConflict` / `SaveAsync_RemoveAndAddSameKeyInOneSave_KeepsNewRow`。mutation 実証済み。

### ExecuteDeleteAsync

- `ExecuteDeleteAsync` も staging を使う。カスケード走査が途中で失敗しても部分削除を残さない（実 DB は文が丸ごとロールバックする）。公開は `ForceOverwrite`（一括削除は条件を述べるだけで特定行の版を主張しないので、版検証はしない）。
- 循環カスケードは、行を読む前に `InMemoryCascade.ThrowIfCascadeIsCyclic` が型のメタデータだけで `NotSupportedException` にする。一致件数に依らない（SQL 側の計画と同じ）。走査中の検出は持たない。事前判定を外すと自己参照の行で走査が止まらない。
- ガード：実装先の一致のシナリオ `execute-delete-cyclic-cascade`（一致する行があっても断り、何も消さない）・`execute-delete-cyclic-cascade-matches-nothing`。

## 列コピーと読み取り

- ストアが持つのは行であってグラフではない。Put / Insert のスナップショットは列だけを写し取る（`InMemoryDataStore.CopyColumns`）。新規インスタンスへ `EntitySaveMetadata.AllProperties` を代入し、ナビゲーションはコンストラクタ既定のまま（コレクションは空・単一参照は null）。
  - 配列（`byte[]` のバイナリ列と、PostgreSQL の `integer[]` 等の配列列）と、内包 `byte[]` の VO は複製して共有しない。旧 `EntityBaseCore.Clone` の値意味論を列レベルで維持する（共有するとストアと呼び出し側が同じ配列を持ち、呼び出し側の書き換えでストアの行が黙って変わる）。
  - グラフごとの JSON 複製をやめた理由は 2 つ：(a) 子を複製した直後に捨てていたので、n ノードのグラフ保存が各ノードの部分木を複製する超線形コストだった。(b) 落とさないと、Include なし取得が RowState=Added の子を返し（SQL 版はナビ空）、その戻り値の再保存が子を二重 INSERT する。
  - 読みは Include が子行から組み立てるので影響なし。
- 読み取りも同じ列コピーで返す（`InMemoryDataStore.CopyRow`）。除外したバイナリ列は写さず、コンストラクタの値（未取得状態）のまま返す。JSON で丸ごと複製してから捨てると、捨てる blob も Base64 で往復して blob の数倍を割り当てる。
- ガード：`BinaryInMemoryFixtureRuntimeTests.Read_DoesNotCopyExcludedBlob`（割り当て量）。mutation 実証済み。

## 一意制約の索引

- 多くの行をまとめて書く一意制約の検査は索引を引く（`InMemoryUniqueness.BuildIndex`）。一括挿入はストアの行を型ごとに 1 回索引にして、受け入れた行を足す。グラフ保存の公開は、合成した view を型ごとに 1 回索引にする。書く行ごとに全行と比べる形は行数の 2 乗で遅くなる。
- 単独の Insert / Update は 1 回の走査のまま。
- ガード：`InMemoryUniquenessScalingTests`（一意制約の列の getter の読み出し回数で固定）。mutation 実証済み。

## Save フックのインメモリの差分

共通の規則は `src/QuickER.Runtime/CLAUDE.md`「Save フック」。

- インメモリは擬似実装：バイナリはストアに書き、`ExecuteSql` は NotSupported。
- After 例外時もストアの変更は残らない。実トランザクションはないが、`InMemorySaveStaging` の copy-on-write で保存単位を all-or-nothing にする（全書き込みをステージングし、全フェーズ成功後に `Store.Publish` が一括公開するので、失敗した保存は一度もストアへ届かない）。
- After の `WriteBinaryColumnAsync` はステージング済みの行の上に積む。保存フェーズの更新と blob が同じ単位で公開される。
- Save フックが書いた blob の版は、元から公開後のスナップショットから読み直す（`src/QuickER.Runtime/CLAUDE.md`「Save フックが無制限バイナリ列を書いたとき」）。

## 無制限バイナリ列（インメモリ）

共通の規則は `src/QuickER.Runtime/CLAUDE.md`「無制限バイナリ列の除外」。

- InMemory は完全パリティにする：読み取り複製の除外列 strip・更新ガード・射影は完全クローンから。
- 更新は除外列の blob を温存する。行ごと差し替える構造上、更新（＝置換前の行がある書き込み）では、「未取得状態のままの除外列」を前スナップショットから引き継ぐ（`UnboundedBinaryColumns.PreserveUnset`）。挿入は引き継ぎ元がなく全列書き込み（実 INSERT と同じ）。引き継がないと、ガードが通る正常系（GetById→編集→Update）で blob が黙って消える。
- `WithUnboundedBinary()` のときは strip なしの完全クローンを返す。射影のフォールバック経路（列抽出不能なセレクタ）も方言側と揃えてある。
- Stream の `length` の契約は、インメモリでは MemoryStream へのコピー元を `WithExactLength` 越しに読む。

## ナビゲーションを断る

- `InMemoryQueryExecutor` の `NavigationGuard` が、述語と並び替えのキーをコンパイルする前に、ラムダ引数から辿るナビゲーションを `NotSupportedException` で断る。
- ストアは行だけを持ち、保存行のナビゲーションは常に空。評価すると `x.Orders.Any()` が 0 件、`x.Customer == null` が全件を、黙って返してしまう。
- SQL 実行器側の同じ規則は `src/QuickER.Runtime/CLAUDE.md`「式の翻訳と NULL 補償」。
- ガード：`TranslatorParity{SqlServer,Sqlite,InMemory}RuntimeTests` の「ナビゲーションを使う条件・並び替えは NotSupportedException で断る」。mutation 実証済み（ガードを外す・並び替え側だけ外す）。
