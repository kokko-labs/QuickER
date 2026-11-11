# SyncPlanFuzz（同期計画の総当たりと期待の行）

`tests/QuickER.Tests.Integration/Dialects/SyncPlanFuzz/` のテストのエンジンの規則。
実 DB の総当たり（`SyncPlanFuzzTestsBase`）に、DB を使わない網と決定的な行を足し、同じランナー（`SyncPlanFuzzRunner`）と既知の失敗の一覧（`SyncPlanFuzzKnownFailures`）で照合する。
エンジンの共通の枠組み（種・既知の失敗・網羅の計数・次元表・登録簿・夜間の目印）は `tests/QuickER.Tests/TestSupport/CLAUDE.md`「エンジンの核（Engines/）」を参照する。
同期計画の製品の規則（`SyncPlanner`・`SchemaDiffService`）は `src/QuickER.Provider/CLAUDE.md`「差分と同期」と、同期の総当たりの節 `src/QuickER.Provider/CLAUDE.md`「同期の総当たりと期待の行」を参照する。

## 静的な規則

- `SyncPlanStaticRules.Evaluate` は、逐次 DDL の 4 方言だけを対象に、実際の計画の文を順に DB 側の図へ当てて DB が断る最初の文を予言する。再構築の方言は判定しない。
- 規則は 11 個。
  - R1：参照されているキー・表を、外部キーが残ったまま外す（MySQL は先頭の列の索引も）。
  - R2：SQL Server で、一意制約・主キー・外部キーが使う列の型を変える。
  - R3：MySQL で、外部キーの先頭の列を支える索引を外す。
  - R4：方言の名前の範囲で名前が衝突する。
  - R5：候補キーの無い参照の外部キーを足す。
  - R6：SQL Server・MySQL・Oracle で表の最後の列を消す。
  - R7：列の型を変えた後、残す外部キーのその列を含む組の両端が、方言の宣言（`AreForeignKeyCompatibleTypes`）で並べられない（PostgreSQL 42804・Oracle ORA-02267。外し直しの方言は R2 が先に当たる）。宣言は「真なら DB が必ず受け入れる」側なので、偽の組を DB が受け入れる形では予言が外れうる。生成器と形の行の外部キーの列は整数か文字列で、その範囲の宣言（整数の幅・文字列の長さ）は実 DB の期待の行と形の行が固定する。R2 が外し直しの方言しか見ないため、PostgreSQL・Oracle の互換でない型への変更を予言できなかった（P5-2 の試運転の 2 回目の V4）。文を順に当てた途中の状態の型で比べるので、外部キーの両端を同じ別の型へ 1 列ずつ変える途中の状態も予言する（修正 AC2 の V8）。
  - R8：外部キーを足す文で、組の両端のその時点の型が方言の宣言で並べられない（MySQL 3780・SQL Server 1778 / 1753・PostgreSQL 42804・Oracle ORA-02267）。DB 側の図の外部キーが同じ型のまま持っていた組（外して付け直す文）は、DB が受け入れたので数えない（状態の読み込みで組と両端の型を記録する）。宣言の扱いは R7 と同じ（足す外部キーの列は、生成器・形の行とも整数か文字列）。選んだ外部キーの追加の両端の型を見る規則が無かったため、live の列どうしの型の違う外部キーの追加が警告なしに断られるのを予言できなかった（修正 AC2）。
  - R9：同じ列の並びに一意制約を 2 つ張れない方言（`SupportsUniqueConstraintOnPrimaryKey` が偽の逐次 DDL の方言＝Oracle の ORA-02261）で、足す一意制約の列集合が、その時点で残る主キーか一意制約と同じ。
  - R10：ケースが宣言した既存の行（とモデルの外の依存）で断られる文（`FuzzLiveSchema.DataFailures`）。照合は種類ごと（外部キーの追加は子の表と実効名・列の変更は表と列・一意制約の追加は表と実効名・主キーの付与は表・列の追加は表と列）。宣言した方言の事実で断られない文（`AddNotNullColumnRequiresEmptyTable`・`AlterColumnBlockedByComputedColumn` が偽の方言の列の追加・計算列の参照する列の変更）は予言しない。計画は値を見ないので、R10 の予言には予告の警告を求めない（実 DB の表明 1 と同じ）が、D9 は掛ける。網は生成器の宣言で D9 を 5000 種で見るので、断られうる文の名指し（`src/QuickER.Provider/CLAUDE.md`「既存の行で断られうる文の名指し」）を DB なしに確かめる。実 DB でも同じ宣言を渡すので、R10 が予言したのにコミットすれば違反になる（宣言の誤りを捕まえる）。
  - R11：MySQL で、残す SET NULL の外部キー（方言が書く参照アクション）の子の列を NOT NULL にする（1830）。SQL Server も `SetNullRequiresNullableChildColumn` を宣言するが、検査は外部キーを作るときだけで、NULL 許容だけの列の変更は外部キーを張ったまま通る（期待の行 `realdb-fk-set-null-child-not-null-keeps-that-fk`）。製品はこの文を計画の先頭へ移す。規則が無いと、先頭の文が実際には断られるのに、後ろの別の予言の失敗（R2 など）の前で外した制約を D9 が数える（行の水準 `set-null-child-not-null` を足した網の MySQL の網の種 4527 で見つかった）。
- 前寄せの規則 M1（`SyncPlanStaticRules.MovedFirstNotFailing`・違反の頭 `static M1:`）：計画が前寄せしたセクション（印 `SyncPlanSection.IsMovedFirst`）の最後の文が、計画の順のその位置で断られると予言される文でなければならない（前寄せの最後がキーの削除か型を変える列の変更か、R11 の方言の NOT NULL 化のときだけ判定）。前寄せした後ろへ予言された失敗が進むなら、前寄せが通る文を移し、断られる文が元の位置に残っている。実 DB の総当たりの結果にも加える（W1 と同じ）。
- 予言したのに予告の警告が無ければ違反にする。`NonPredictingWarnings` 以外の警告を予告とみなし、断られる文と警告は結び付けない（実 DB の表明 1 と同じ粒度）。
- 警告の正しさの規則 W1（`SyncPlanStaticRules.SpuriousTypeMismatchWarnings`）は、予言の有無に依らず掛ける：`ForeignKeyColumnTypeMismatch` が DB にある外部キーを名指しするなら、その外部キーに「端の型を計画が変え（`AreSameType` で比べる）、同期後にその組の両端が `AreForeignKeyCompatibleTypes` で並べられない」組がある。
  - DB は今その外部キーを両端の型のまま受け入れているので、計画が型を変えない組の違いを数えて「付け直せない」と言うのは偽の警告。
  - 警告が選んだ外部キーの追加・作り直し（スクリプトが付ける名前）を名指しするなら、その項目の組で判定する：「DB が受け入れた組」（両端とも計画が型を変えず、DB 側の図の外部キーが同じ組を持つ）でなく、同期後に両端が合わない組がある。作り直しは DB にある外部キーと同じ名前を持つので項目を先に見る。製品が受け入れた組の作り直しまで外しすぎる誤りを捕まえる（修正 AC2。壊し方の抜き取りで、DecimalOtherKeyColumn の作り直しを外しすぎる形を名指しした）。
  - 組単位にしたのは、外部キー単位（構成列のどれかの型を変える）だと、複合外部キーのほかの組の型を変えたときに、DB が受け入れている組の違いまで数えた偽の警告が予言された失敗として通っていたため（P5-2 の試運転の 2 回目の V1）。型を変えた組の互換は製品と同じ宣言で判定する。
  - 予告の警告があれば合格にする上の判定は警告そのものの正しさを見ないので、偽の警告が「予言された失敗」として通っていた（P5-2 の試運転の L4）。W1 は、警告が DB にある別の外部キーの名前を名指しする誤り（足す外部キーの名前の取り違え）も捕まえた。
  - 実 DB の総当たりも、DB に依らないこの規則の違反（頭 `static W1:`）だけは結果へ加える（DB の断る文の並びの違反は実 DB の表明 1・1b が見るので加えない）。
- DDL を暗黙コミットする方言では、次を D9 の違反にする。
  - 断られる文より前に外して後ろで付け直す制約。
  - 断られる文そのものが、同じ計画の前の方で外した制約の付け直し（外部キー・作り直す一意制約・主キーの変更の付与）であるとき、その制約（付け直しが断られて消えたまま残る）。前の 2 つの形だけでは、参照先の唯一の一意制約を同じ計画で外しながら列の変更で外部キーを付け直す同期（最後の付け直しが断られる）を、予告の警告があるので合格にしていた（修正 AC2 の d4-mysql-26・d4-oracle-26。実 DB の表明 1b の規則 5 だけが捕まえていた）。
  - 断られる文より後ろの表・列の削除のために先に外す制約。
  - 候補キーの警告を出した外部キー自身は除く（表明 1b と同じ）。
  - 列が空でないと通らない変更・既存の行で断られうる文の警告（`ConstraintRebuildAroundEmptyColumnChange`・`ConstraintRebuildAroundRowDependentChange`）が名指しした制約も除く（製品の契約＝名指しして部分適用を受け入れる。表明 1b の例外と同じ）。警告は表と名前だけを持つので、外部キーと一意制約の両方の身元で照合する。除くのは予言した失敗が R10（データで断られる文）のときだけ。構造の規則（R1〜R9・R11）の予言はデータに依らず必ず断られるので、名指しでは足りず、計画はその文を先頭へ移すか外し直さないで済ませる（狭めないと、SET NULL の子の列の NOT NULL 化を前寄せしない計画を、新しい警告の名指しで網の乱数の種が見逃した）。
- 同じ方言で、外部キーの追加の並びも D9 の違反にする（違反の頭 `static D9 (order):`・`SyncPlanStaticRules.ReAddsAfterNewAdds`）：前で外した外部キーの付け直しが、新しい外部キーの追加より後ろにある（候補キーの警告の外部キーは除く）。新しい外部キーは既存の行で断られうるので、後ろの付け直しが走らずに消えたまま残る（`src/QuickER.Provider/CLAUDE.md`「外部キーの追加の並び」）。実 DB の結果にも加える。

## 能力の組の網

- `SyncPlanCapabilityNetTestsBase`（5 方言の派生・DB なし）が、同じ生成器の種で目標の図と計画を作り、静的な規則だけで照合する。
  - 種の数は普段 5000・夜間 20000（`QUICKER_SYNC_CAPNET_SEEDS` / `QUICKER_SYNC_CAPNET_SEED`・夜間の目印つき）。
  - 必須の形の表明（`FuzzShape.RequiredFor`）は、流す種の数が普段の既定の数以上のときだけ行う（種は開始番号からの連番なので、既定の種を全部含むときだけ＝既定で全部出る形はそれより多い種でも必ず出る）。既定より少ない数を明示した実行では表明せず、そのことを出力に 1 行書く。後から足した水準の形は確率でしか出ず、2026-11 の測定では SQLite の `both-ends-kind-change` が種 501〜600 で初めて出た（500 種の変異テストの最初のテストの実行が落ちた）。
  - 網の種 N と実 DB の種 N は別のケース（実 DB は取り込んだ図の並びで変更の乱数を引く）。種の番号には「網の種」「実 DB の種」を添える。
  - 関係 N1（`SyncPlanCapabilityNetRunner.PrimaryKeyNullableFlagProblems`・違反の頭 `relation N1:`）：同じケースの目標の図の主キーの列に NULL 許容の印を立て、差分と計画を作り直して、差分の項目（種類と説明の並び）・計画の警告・外した項目・同期スクリプトが元と同じことを表明する。乱数の種・決定的な行（形の行・来歴の行を含む）・網羅の行のすべてに掛ける（期待の行は手で組む項目を持つので掛けない）。
    - 主キーの列は印に依らず NOT NULL なので、印を読む差分・計画・同期スクリプトの写しを捕まえる（`src/QuickER.Provider/CLAUDE.md`「主キーの列の NULL 許容」）。
    - 選び方は元のケースのものを差分の位置で写す（生成器の選び方は乱数を引くので、2 回呼ぶと種の列が変わる＝種の指紋は変わらない）。
    - DB 側の図には立てない。取込が主キーの列の印を false にそろえ、その印は主キーから外れる列の SQLite の再構築の NOT NULL として意味を持つ（立てると製品の契約の外の入力になる）。
    - 網の時間は差分と計画をもう 1 回作る分だけ伸びる（2026-11-09 の手元で、5 方言のテストの時間の合計が 23.6 秒から 29.6 秒）。
  - 関係 N2（`SyncPlanCapabilityNetRunner.CaseFoldedLiveProblems`・違反の頭 `relation N2:`）：DB 側の図の名前の無い制約に実効名を付けてから、表名・列名を ASCII の小文字へ畳んでも（`FoldLiveNames`）、差分・計画の警告・外した項目が大文字小文字を無視して元と同じことを表明する。差分の名前を図の綴りから求める規則（`src/QuickER.Provider/CLAUDE.md`「差分の名前の綴り」）の一般の回帰の網。
    - 掛けるのは決定的な行・網羅の行の全部と、乱数の種は普段 4 つに 1 つ（種の番号が 4 の倍数）・夜間は全部（`CaseFoldRelationApplies`）。出力に「relation N2 on X of Y seeds」を書く。
    - 同期スクリプトは比べない（名前の無い外部キーを実行時に逆引きして外す文と、名前で外す文が違う）。
    - 縮めた既定名の食い違いは、生成器の長い名前の水準（下の「生成器と方言の宣言」）が作る形で捕まえる。
  - N1 について、種を減らすと見逃しうる：N1 で捕まえる形のうち、同期後の NULL 許容（`PostSyncNullability`）は既定の 5000 種のうち Oracle の網の種 2269 だけ、SQLite の再構築の列の追加の写しは SQLite の網の種 6 つ（632・825・3749・4276・4277・4665）だけが捕まえる（2026-11-09 の測定。どちらも種を 500 に減らす変異テストの環境では捕まえない）。こうした形は期待の行（`plan-set-null-post-sync-nullability-alter-to-key-column-flagged-nullable`・`plan-rebuild-column-changes-of-key-columns-flagged-nullable-stay-not-null`）が受け持つ。N1 だけに守りを任せる形は、網の既定の種で何種が捕まえるかを確かめて、少なければ行を足す。
- 次元表 `SyncPlanCapabilityNetDimensions` の中身は次のとおり。
  - 選び方・変更の種類・同期計画の形を変える `SyncDialectCapabilities` の 24 項目。派生ごとの固定の水準で、書き下ろしの表とプロバイダの値の食い違いは宣言の誤り。能力どうしの組は数えない。
  - 変更 1 つにだけ値を持つ 3 因子。生成器が変更の文に `{type=…}` `{refs=…}` の札を書く。
    - `AlteredType`：型の変更の行き先。方言の上限ちょうどは `AtLimit`。
    - `ReferencingFks`：主キーの変更で参照する外部キーの数。
    - `ReferentialAction`：参照アクションの変更の行き先。
- 因子の棚卸しの同期計画の能力の除外 17 件と、名前の DDL の往復の識別子の上限・`TruncatesLongIdentifiers` の除外は、この次元表の Source になっている。

## 実 DB の突き合わせ

- 実 DB の総当たりも同じ規則を計画に当てる。
  - 予言したのにコミットしたら違反（規則か計画の読み方の誤り）。
  - 予言して断られたら注 `static-rule-predicted`。
  - 予言せずに断られたら注 `static-rule-missed`。
  - D9 を予言した失敗は `static-d9:observed` / `not-observed` を数える。

## 決定的な行

- `SyncPlanScenarios.Table`：範囲は `方言/scenarios`。種の前に流し、単独の種では流さない。
- 指摘の形を最小にした行と、指摘を持たず Id が `pass-` の合格側の行（製品が警告して止まる形）を持つ。
- 合格側の行は網と実 DB の両方で「規則が失敗を予言し、違反が 0」を表明する（`SyncPlanScenarios.PassRowProblems`）。

## 形の行

- `SyncPlanShapeRows`（登録簿の部分 `ShapeRows`）は、主キーの変更 7 × 参照する外部キーの数 3 × 型の変更 10 × 明示の操作 8 × 一意制約の数 3 × 参照先のキー 2 × 外部キーの両端の型 3 × 巻き添えの外部キーの変更 2 × 主キーと同じ列の一意制約 2 × 足す外部キー 5 の網羅の配列（必ず含む行を足して 365 行）。P1 と Q6 の 3 つ組を含むことを網が表明する。
  - 型の変更の `DivergentEnds` は両端を別々の型へ変える：最初の子 c1 を int → bigint（宣言の範囲で互換）、参照される親の列を int → varchar(10)（互換でない）にし、c1 の表を目標の図の先頭へ移す（実 DB の取込の並びと同じく子の変更が先＝網の図では親の表が先に並ぶので、移さないと形が出ない）。前寄せが先頭で断られる文を選ぶこと（静的な規則 M1）を見る。
  - 足す外部キーの `OrphanChild` は、表 e に親に無い値（900 番台）の行を入れて外部キーを足す（既存の行で断られる文として `FuzzLiveSchema.DataFailures` に宣言する）。外部キーの追加の並び（静的な規則の D9 の並び・表明 1b）を見る。
  - 明示の操作の `RenameKeepAndAddUnnamedOnOld` は、名前を保って一意制約の列を u から w へ変え、元の列 u に名前の無い一意制約を足す（一意制約 1 つの行だけ）。一意制約の差分の名前の段と列集合の段が重なる形（`src/QuickER.Provider/CLAUDE.md`「一意制約の差分」）。
  - 明示の操作の `MoveOntoAddedUnique` は、名前を保った一意制約を u から w へ移し、w に名前の無い一意制約を足す（目標の図は w に一意制約を 2 つ持つ。一意制約 1 つの行だけ・Oracle と SQLite は作らない）。両方を選んで計画がどちらも作り、`src/QuickER.Provider/CLAUDE.md`「一意制約の差分」の残る一意制約の前提の除外が外さないことを見る。
  - 明示の操作の `ChangeFirstDropOthers` は、最初の一意制約の列を u から w へ変え（作り直し）、ほかの u の一意制約を外す。`ChangeAllUniques` は u の一意制約を両方とも作り直す（(w) と (u, w)。SQLite と PostgreSQL は作らない＝同じ列の UNIQUE を 1 つにまとめて取り込む）。どちらも一意制約 2 つの行だけで、u を参照する外部キーがあれば支えうるキーが全部外れて削除の組が前寄せされる。組の並べ替え（付け直す削除を最後へ）と、付け直すキーが 2 つのときの作り直しの除外（`src/QuickER.Provider/CLAUDE.md`「外部キーの暗黙の外し直し」）を、網の D9 と実 DB の表明 1b の規則 2 で見る。
  - 一意制約を外す・変える操作（`DropUnique`・`DropAllUniques`・`RenameKeepAndAddUnnamedOnOld`・`ChangeFirstDropOthers`・`ChangeAllUniques`）は名前で引き、その名前が無ければ同じ列集合の名前の無い一意制約を引く（`ScenarioTarget.DropUniqueOn` / `ChangeUniqueOn`）。SQLite は取込が一意制約の名前を持たないので、名前で引くと何も外さなかった（SQLite の 177 行のうち一意制約を外す 58 行。普段の実 DB で流す 22 行を含む）。同じ列集合の一意制約を 1 つの索引にまとめる DB では、`DropAllUniques` の 2 つ目は何も外さない。
  - 型の変更の `BothEndsKindChange` は、参照される親の列（主キーを参照すれば `p.id`、一意制約なら `p.u`）と、それを参照する子の列すべて（c1..cN と、主キーを参照するなら `d`）を varchar(10) へ変える（親・子・d は行 0）。子を 1 つでも残すとその外部キーが同期後に食い違って名指しされ、予告の警告が途中の状態の失敗を隠すので全部を変える。
  - 足す外部キー（`AddedForeignKey`）は、外部キーの無い表 `e`（行 0）を置き、目標の図で `e(p_ref_0, p_ref_1)` から `p(id, k)` への外部キーを足す。水準は最初の列の型（`IntChild`・`BigIntChild`・`VarCharChild`）で、2 列目は `p.k` と同じ型（外部キーの両端の型の水準が作る k の型の違いを持ち込まない＝宣言の外で DB が受け入れる組を R8 に予言させない）。
  - 参照先のキー（`ReferencedKey`）：子の外部キーが親の主キーか `p.u` の一意制約を参照する。外部キーの両端の型（`ForeignKeyTypes`）：Wider は外部キーを張った後に参照される親の列を bigint にする（子の列は int のまま＝DB が受け入れる型の違う外部キー）。主キーの変更の `MoveToUniqueColumn` は主キーを `p.u` へ移し、型の変更の `ChildNullability` は最初の子の外部キーの列の NULL 許容だけを変える。
  - 外部キーの両端の型の `DecimalOtherKeyColumn` は親の主キーの 2 列目 `k` だけを小数にする（子の `p_ref_1` は int のまま。型の変更の水準は `k` を変えないので、複合外部キーの型の決まらない組が DB の受け入れている違いを持つ。bigint にしないのは、整数の幅の宣言で互換になり形が消えるため）。型の変更の `ChildKindChange` は最初の子の外部キーの列を int → varchar(10) にする（`c1` は行 0。Oracle は行のある列の型の種類を変えられない＝ORA-01439）。明示の削除の `DropAllUniques` は `p.u` の一意制約を全部外す（一意制約 2 つの行だけ）。
  - 巻き添えの外部キーの変更（`CollateralFk`）の `AlterOtherForeignKey` は親の主キーを参照する表 `d` を足し、その外部キーの ON DELETE を CASCADE へ変える（作り直しの外部キーが計画の前の方で外れる）。主キーと同じ列の一意制約（`PrimaryKeyTwin`）は `uq_p_pk(id, k)` を張る。
  - 網羅の配列の後ろに、指摘の形の完全な行（`SyncPlanShapeRows.RequiredRows`＝P5-2 の試運転の L1・L4・2 回目の V1・V3・V4・V6、修正 AC2 の V8〔両端の種類の変更 × d の作り直し〕・型の違う外部キーの追加 2 つ〔BigIntChild・VarCharChild〕、修正 AH の前寄せ〔DivergentEnds × 参照 1 × d の作り直し〕・外部キーの追加の並び〔主キーの並べ替え × 参照 1 × OrphanChild〕、修正 AH2 の前寄せの組の並び〔一意制約 2 つ × 参照先 Unique × 参照 1 × ChangeFirstDropOthers〕と付け直すキーが 2 つの組〔同じく ChangeAllUniques〕の 13 行）を、含まれていなければ足す（全因子の起こり得る割り当てでなければ例外）。この行は普段の実 DB にも入れる。
  - Oracle は一意制約 2 つの行を、SQL Server・MySQL は両端の型の違う外部キーを作れない（Msg 1778・エラー 3780＝DB の事実）ので両端の型が Same でない行を、`SupportsUniqueConstraintOnPrimaryKey` が偽の方言（Oracle・SQLite）は主キーと同じ列の一意制約の行を作らない。行の数は PostgreSQL 348・SQLite 174・Oracle 107・SQL Server・MySQL 139。
- 網と夜間の実 DB は全行を流す。普段の実 DB は 2 因子の網羅に要る行（PostgreSQL 76 行・SQLite 76 行・Oracle 71 行・SQL Server・MySQL 77 行）と必ず含む行だけを流す。
  - 2 因子の網羅に要る行は方言ごとに、その方言が作る行で作れる組を、その方言の行から選ぶ（`SyncPlanShapeRows.PairwiseRowIndexesFor`）。全方言で 1 つの部分を選ぶと、方言が作らない行の分だけその方言の組が普段の実行から抜ける。
- 夜間だけの行の項目は範囲 `方言/scenarios/nightly`。実 DB の総当たりは夜間の実行でだけ受け持ち、網は常に受け持つ。

## 生成器と方言の宣言

- 生成器は方言の宣言で DB が断る形を避ける。
  - `AllowDuplicateForeignKeys`：ORA-02275。
  - `AllowDuplicateUniqueConstraints`：ORA-02261。
  - `DistinguishesSameSetConstraints`：SQLite は同じ列の並びの制約を名前で見分けないので、目標の図の変更で重ねない。
  - `LimitType`：方言の上限ちょうどの型。
  - `RespelledTypeName`：同じ型の別の綴り。
  - `SymbolCharacter`：識別子の引用の中で特別な文字。
  - `OtherSchema`：SQL Server の別スキーマの同じ名前の表。
- 生成器を作る所は `SyncPlanFuzzGenerator.ForSeed` の 1 か所（実 DB のランナー・網・網羅の行・指紋の要約）。後から足した水準は本流と別の乱数の列で引く。
  - 水準は 4 つ：外部キーを親の一意制約の列へ付け替える（子の値を写し直す）・主キーと同じ列の一意制約（持てる方言だけ）・整数の列の型の種類を文字列へ変える（変える列と揃える相手の値が全部 NULL か行 0 のときだけ＝Oracle の ORA-01439。親が行 0 なら外部キーの両端をそろえて変える形が出る）・行 0 の子の型の違う live の文字列の列へ外部キーを足す。
  - 別の列にしたのは、本流の乱数の引き方・回数を変えないため。水準が出なかった種は足す前と同じケースで、種の指紋は水準が出た種だけ変わる（`ForSeed(seed, dialect, levels: false)` で足す前のケースを作れる）。別の列の種は決定的な式で作る（`HashCode.Combine` はプロセスごとに値が変わるので使わない）。
  - 生成器の穴（その形の入力を作らない）が 3 回続いた（修正 AA・AC・AC2）ので足した。
- 生成器は目標の図を方言の宣言に照らして自己検査する（`CheckDeclaredTargetShapes`・`Mutate` の最後）。同じ列の並びに一意制約を 2 つ張れない方言（`AllowDuplicateUniqueConstraints` が偽＝Oracle の ORA-02261）で同じ列集合の一意制約を 2 つ以上持つ表を作ったら、その種の失敗（`seed threw: … generator declaration: …`）にする。
  - 一意制約の列を変える変更（`ChangeUniqueColumns`）が、足す変更（`AddUnique`）と違ってほかの一意制約が乗る列へ移せたので、Oracle の目標の図が DB の持てない形になり、夜間の網の R9 だけが捕まえていた（普段の種では重複した一意制約を両方は選ばないので予言しない）。自己検査は普段の網の種でもこの形を捕まえる。
  - 宣言は Oracle の事実だけに合わせる（SQLite は同じ列集合の一意制約を持て、壊れる形も無い。`AddUnique` の `DistinguishesSameSetConstraints` まで掛けると、SQLite の総当たりの網羅の組が消える）。
- 既存の行で断られる文と、名前・計算列の形の水準は、さらに別の乱数の列（`DataLevelSeed`）で引く（先に足した水準の引き方も変えない）。水準は 4 つ：
  - 親の無い値の列への外部キーの追加（行のある子の、親に無い値を持つ live の整数の列へ外部キーを張る。必須の形 `orphan-fk-add`）。
  - 精度の縮小（小数の精度を 2 つ・bigint を int へ下げる。必須の形 `precision-decrease`）。日時の小数秒の縮小は、製品の宣言 `PrecisionDecreaseRequiresEmptyColumn` の方言（Oracle）の行のある表でもこの水準のときだけ作る。
  - 図でだけ計算列にする（DB の普通の整数の列を目標の図だけで計算列にし、型か NULL 許容を変える。必須の形 `diagram-only-computed`。変更の文は既存の `computed type` / `computed nullable` の頭で、次元表は変えない）。
  - 長い名前（表名を識別子の上限の半分を超える長さへ伸ばして大文字を混ぜる＝名前の無い外部キーの既定名が上限で縮められる。上限のある方言だけ・種の 6 つに 1 つ。必須の形 `long-default-name`）。生成器が明示の制約名に埋める表名・列名は先頭の 16 文字だけにする（明示の名前は利用者が付けた名前なので製品は縮めずに上限で断る。既定名だけを縮める形にする）。
  - 既存の行で断られると決まっている文（親の無い値の外部キーの追加・列が空でないと通らない変更を行のある列へ当てる文と、下の行の水準の文）は `FuzzLiveSchema.DataFailures` に宣言する。実 DB の表明 1 は、宣言した文そのものが断られた失敗だけを外す。表明 1b は掛ける。
    - 見分け方は `SyncPlanFuzzRunner.IsDeclaredDataFailure` の 1 か所。文ごとに流す方言（DDL を暗黙コミットする MySQL・Oracle）は失敗したバッチの文の形と宣言の名前で見る（MySQL の列の変更は動的 SQL なので、断られた `EXECUTE` の前の最後の `SET @sql` の文で見る）。
    - ほかの方言はエラー文の理由の語（重複・NULL・長さ・式の依存）と宣言の名前で見る（SQLite は再構築の表の列 `{表}_quicker_rebuild.{列}`・外部キー違反の表がすべて宣言の子の表。PostgreSQL の式の依存のエラー文は名前を持たないので理由の語だけ）。
    - 名前だけが偶然一致する別の理由の失敗を一致させないことを、規則表 `SyncPlanFuzzOracleTests` の自己検査の行が固定する。
  - 4 つの形は網でだけ必須（実 DB の普段の種で出ない種の集合がありうる）。2026-11 の種の指紋の書き直しで変わった種は、どれもこの 4 つの水準が出た種だけ（SQLite 188／2000・MySQL 37／150・SQL Server 25／100・PostgreSQL 2／15・Oracle 0／4）。
- 既存の行（とモデルの外の依存）で断られる文の残りと、参照アクションと子の列の NULL 許容の組は、4 本目の乱数の列（`RowLevelSeed`＝`DataLevelSeed` から決定的に作る）で 1 種に 1 回だけ引く。データの値を DB 側の図を作る段で変えるので、`DataLevelSeed` の列で引くと後の `Mutate` で引く既存の水準の引き方が全部の種で変わる。水準は 7 つ（どれも 20 分の 1・網でだけ必須の形）。最初の 5 つは行のある表だけ：
  - 重複（`duplicate-data`）：鍵でない整数か文字列の列の 2 行目を 1 行目と同じ値にし、目標の図でその列へ名前つきの一意制約を足す・主キーを移す・名前つきの一意制約を移す。
  - NULL（`null-data`）：参照されない NULL 許容の整数か文字列の列（外部キーの子の列を含む・SET NULL の子は除く）の 1 行目を NULL にし、NOT NULL にするか主キーへ足す。
  - データより短い長さ（`shorten-below-data`）：鍵でない文字列の列の値を 12 文字にし、長さ 5 へ縮める。
  - 行のある表への NOT NULL の列（`not-null-add-on-rows`）。
  - 計算列の参照する列の変更（`computed-source-change`）：計算列の式が参照する整数の列を 64 ビット整数へ広げる（データに依らず、式の依存で断る方言がある）。
  - SET NULL の子の列の NOT NULL 化（`set-null-child-not-null`）：SET NULL の外部キーの子の列を NOT NULL にするか主キーへ足す。値に NULL の無い列か行 0 の表だけ（データで断らせない＝製品の SET NULL の分岐だけを見る）。`SetNullRequiresNullableChildColumn` の方言（MySQL）は計画がその変更を先頭へ移して警告し（静的な規則 R11・M1）、ほかの方言は通して警告する。SET NULL の外部キーは参照アクションを変える方言（`AllowCascade`）でだけ出る。
  - SET DEFAULT × NOT NULL の子（`set-default-not-null-child`）：子の列がどれも NOT NULL の外部キーの ON DELETE（3 分の 1 は ON UPDATE）を SET DEFAULT へ変える。図は既定値を持たないので子の列に既定値が無い。参照アクションを変える方言だけで、`SetDefaultRequiresColumnDefault` の方言（SQL Server）は網羅の行の手で書いた行が受け持つ。
  - 変更の文は本流と同じ頭で書く（網羅の計数の変更の種類を増やさない）。水準を足したときの種の指紋の書き直しで変わった種は、行の水準を引いた種だけ（SQLite 379／2000・MySQL 30／150・SQL Server 20／100・PostgreSQL 5／15・Oracle 1／4。行の水準の列だけを止めると記録と全種一致することを確かめた）。そのうち水準の形の印が付かなかった種（データの値は変えたが目標の図の変更の候補が無かった）は SQLite 27・MySQL・SQL Server・Oracle 各 1。SET NULL・SET DEFAULT の 2 つの水準で変わった種は SQLite 63（SET DEFAULT 57・SET NULL 6）・MySQL 4（SET DEFAULT）で、ほかの方言の普段の種には出ない。
- 必須の形は方言ごとに決める（`FuzzShape.RequiredFor`）。実 DB では「同じ列の並びの一意制約 2 つの片方の削除と型の変更」を求めない。
  - 後から足した水準の形（`fk-on-unique`・`pk-twin-unique`・`kind-change`・`both-ends-kind-change`・`mismatched-fk-add`）は網では全部求める（`pk-twin-unique` は持てる方言だけ）。実 DB では DB 側の図で出る `fk-on-unique`・`pk-twin-unique` だけを求める。目標の図の変更で出る 3 つは網の 5000 種で 0.4〜3% しか出ず、普段の実 DB の種で出ない種の集合がありうるので、実 DB は形の行が受け持つ。
  - SQLite は DB が同じ列の並びの UNIQUE を 1 つの索引にまとめる。
  - SQL Server は普段 100 種で出ず、形の行の Q6 の 3 つ組が決定的に担う。

## キーの照合

- 外部キーの列ペアは対応の集合で比べる。`SyncPlanFuzzRunner.PairSetText` が「親の列>子の列」の組を並べ替える。製品の差分は並びを見ずに比べ、参照先のキーの並びを要する方言は付け直しをキーの並びにするため。
- 規則表 `SyncPlanFuzzOracleTests` の `rule5-composite-foreign-key-reordered-pairs-remain` / `rule5-composite-foreign-key-swapped-pairs-lost` が固定する。

## 来歴の行

- `SyncPlanHistoryRows`（PostgreSQL・登録簿の部分 `HistoryRows`）は、DB の来歴 × 子の表名の長さ × 目標の図で足す 2 本目の名前の無い外部キー（足さない・別の親・同じ親）の全積 12 行。
  - `SyncPlanHistory.Current`：今の DDL。
  - `SyncPlanHistory.PostgreSqlUnshortened`：旧版の DDL。名前の無い外部キーの縮める前の既定名を PostgreSQL が 63 バイトで切り詰めた名前になる（既定名を上限に収める規則は後の版から）。
- 来歴の規則は版ごとの規則を関数で持つ（公開版の DDL 生成器は使わない）。
  - `SyncPlanHistories.Apply`：DB 側の図の名前の無い外部キーを名付けてから DB を作る。
  - `Forget`：目標の図ではその名前を外す（利用者の図は名前を付けていない）。
- 旧版の DDL の来歴でだけ起きる吸収（名前の無い外部キーを縮める前の名前から取り込む形）は、行 `history-PostgreSqlUnshortened-long-added` が網の静的な規則 R4 と実 PG の既定の同期の両方で固定する。
- 来歴は現実にあり得る状態だけを作る。旧版の DB には外部キーが 1 本だけで、2 本目は後で図に足す。
- 12 行とも、差分が来歴どおりで（図を変えない行は差分なし・2 本目の行は外部キーの追加だけ）、網では規則が失敗を予言せず、実 PG では既定の同期がコミットして取り込み直した差分が空になる。
  - 合格側の `pass-` の行と期待が逆なので `NetProblems` / `RealProblems` は別の関数。
  - 網は行が実際に作った状態（来歴・縮める前の既定名が上限を超えたか・2 本目を足したか）を `CoverageCounter` で数えて全組を表明する。
- 目標の図で名前を戻すことは、図を変えない行が吸収を確かめるために要る。吸収を外すうえで戻さないと、その行も緑になる。
- mutation 実証済み。

## 網羅の行

- `SyncPlanCoverageRows`：Id は `coverage-` で始める。失敗の範囲は決定的な行と同じ `方言/scenarios`。種の前に流し、単独の種では流さない。
- 決定的な行（静的な規則の合格側・形の行・来歴の行）は網羅の計数に数えないが、網羅の行は数える。
  - 決定的な行は回帰の網で、選び方を行の述語で決める。
  - 網羅の行は変更の種類と差分の選び方の組を実際に流すための行で、選び方を本物の 5 通りの意味のまま強制する。包み `SelectionOverrideCase`、選び方の本体は生成器の `Select` から切り出した `SyncPlanFuzzGenerator.ApplySelection`（引く順と回数は同じなので、選んだ項目まで含む種の指紋は 5 方言とも不変）。
- 形は 2 つ。
  - 種の行：実 DB の種 N の DB と変更をそのまま使い、選び方だけを強制する。選び方は 1 ケースの最後に引く乱数なので DB と変更は種 N と同じ。実 DB でだけ流す。MySQL 18 行・SQL Server 28 行。夜間の種の数で `QUICKER_SYNC_FUZZ_LOG` 付きで流した記録から、欠けを多く埋める種を貪欲に選んだ。種の番号と埋める組は行の表に書く。自由な変数が種で次元表の因子でないので `CoveringArray` は使わない。
    - SQL Server の 2 行（種 56）と MySQL の 1 行（種 16）は、生成器に水準を足して普段の種のケースが変わり、普段の種から欠けた組（SQL Server 3 組・MySQL 1 組）を埋める行（水準を足した後の普段の種の記録から選んだ。今の生成器のまま流す）。水準を足しても既存の種の行は崩れなかった。
  - 手で書いた行：SQL Server の参照アクションの変更。互いに独立した親子 8 組の ON DELETE / ON UPDATE を NO ACTION・CASCADE・SET NULL・SET DEFAULT へ変える形 × 選び方 5 通り。生成器は SQL Server で参照アクションを変えない（`AllowCascade` が偽）。実 DB と網の両方で流す。
    - 子の列が NOT NULL の親子 2 組ずつを SET NULL・SET DEFAULT へ変える行（接尾辞 `fk-actions-not-null-child-set-null`・`fk-actions-not-null-child-set-default`・選び方 5 通り）を別に持つ。SET NULL は計画が外して名指しし（`SetNullRequiresNullableChildColumn`）、SET DEFAULT は列の DEFAULT 制約が要ると計画が警告して DB が断る（`SetDefaultRequiresColumnDefault`）。NULL を許す組と同じ行に入れると、断られる SET DEFAULT の巻き戻しがほかの組の適用まで消すので行を分ける。SET NULL と SET DEFAULT も行を分ける（同じ行だと SET NULL の警告が SET DEFAULT の失敗の予告に数えられ、SET DEFAULT の警告を外しても表明 1 が赤にならなかった）。生成器の行の水準 `set-null-child-not-null`・`set-default-not-null-child` の SQL Server の分を受け持つ。
- 網羅しない組の一覧に残るのは受け入れ（製品・DB の事実）だけ。
- 行が出すはずの変更の種類と強制した選び方を出さなくなったら、`SyncPlanCoverageRows.MissingKindProblems` が行を名指しして赤にする。生成器を変えて種の指紋が変わったら、指紋と一緒に種の行を選び直す。
- 網羅の照合は普段の既定の種の数でだけ行うので、網羅の行を夜間だけに回してはいけない。普段の実行で一覧に無い欠けになる。
- mutation 実証済み。

## 期待の行と実 DB の段

同期の差分と計画の単体テストと、実 DB の同期の統合テストは、期待の行 `SyncPlanExpectationRows`（`SyncPlanExpectationRows.*.cs`・行は `ExpectationRow`・表は `ScenarioTable`）に寄せてある。

### 行の構成と網での照合

- 行は次を持つ。
  - DB 側の図：`Live`（QuickER の DDL で作る）か `LiveSql`（生の SQL）。
  - 目標の図：`Target` / `TargetFromLive`。
  - 選び方。
  - 行を流す方言（能力）。
- 網（DB を使わない）では `SyncPlanCapabilityNetTestsBase.ExpectationRows_Match` が、差分と計画の期待を面ごとに照合する。面は `diff` / `items` / `sections` / `warnings` / `excluded` / `rebuilds` / `reorders` / `exception`。

### 実 DB の段

- `.OnRealDatabase(r => …)` で実 DB の段を持つ行は、方言ごとの `SyncPlanFuzzTestsBase.ExpectationRows_MatchOnRealDatabase`（5 方言の派生・`SyncPlanRealStage.cs`）が実 DB でも流す。
  - 流れは「DB 側の図を作る → 取込 → 目標の図との差分 → 選び方 → 計画 → 実行 → 取り込み直し」。
  - 目標の図は既定で別の Guid 空間。`PreserveIds(理由)` は例外で、今は使う行が無い。
- 総当たりと同じランナー（`SyncPlanFuzzRunner.RunCaseAsync`）の汎用の表明 1〜5 を照合する。
  - 表明 1：失敗は計画の警告が予告する。
  - 表明 1b：失敗で消えてよい制約だけが消える。
  - 表明 2 / 2b / 3：選んだ項目が消え、新しい差分が出ず、選ばなかった項目が残る。
  - 表明 4：選んだ変更の外の列属性が変わらない。
  - 表明 5：行数と取込の警告。新しく出た取込警告に加え、同期の前の取込警告が同期の後に消えたら、選んだ項目で説明できない限り違反にする（`SyncPlanFuzzRunner.LostImportWarnings`・違反の頭 `import warning lost:`）。取込が表の中の物を捨てた種別（`SchemaImportWarningKinds.RebuildLosesTableContent`）は、その表の削除か名指しした列の削除を選んだときだけ説明でき、ほかの種別はその表に触れる選んだ項目か、計画の `TableRebuildDropsColumnAttribute` の警告で説明できる。外部キーの警告は Subject が列でないので、その外部キーの子の列の削除では説明できない（今は生成器・期待の行がその形を作らない。限界は関数の remarks）。
- 実 DB の段だけの汎用の表明 6：行の後に、製品の取込・実行器が DB のファイルを掴んでいない（`SyncPlanFuzzDialect.HeldFileProblem`。ファイルの DB の SQLite だけが実装し、DB のファイルを排他で開けるかで見る）。
  - SQLite の方言の自分の接続（行数・探針・問い合わせ・空のファイルの作成）はプールしない。製品と同じ組み方の接続文字列でプールすると、製品の掴みとテストの接続を区別できない。取込の接続文字列は画面と同じ `SqliteConnectionStringFactory.Build`。
  - Linux の排他は SQLite のロックと関係しないので、Windows のジョブでだけ効く（→ `src/QuickER.Provider.Sqlite/CLAUDE.md`「ファイルを掴まない接続」）。総当たりの種には掛けない。
- 行の実 DB の期待の面は `outcome` / `error` / `script` / `before` / `after` / `afterdiff` / `probe:名前` / `probe-before:名前`。前後が同じことは `Unchanged()` / `ProbeUnchanged()`。
- 既知の失敗の一覧は通さない。違反はすべて赤にする。
- ランナーの計画の文脈には取込の `ColumnDefaults` も渡す（画面と同じ。既定値のある列の型の種類の変更を J2 の警告で名指しする。総当たりの種も同じ文脈で計画する）。
- 実 DB の段の Fact は種を使わないので、夜間の目印を付けずに普段の CI で流し、網羅の計数に数えない（決定的な行と同じ）。

### DB の前提の作り方

- `Prepare`：取込の前の文。
- `LiveSql`：`RealOnly` を含む（網では流さない）。
- `Setup`：取込の後の行の投入。
- `RawSetup`：実行器の検査を通さない文。SQLite で孤児行やビューを作る行が自分で `PRAGMA foreign_keys` を切り替える。
- `Teardown`。
- 管理者の接続：`AdminSetup` / `AdminTeardown` / `AdminQuery`（Oracle の SYSTEM・MySQL の root。使う行は Oracle の 1 行）。
- カタログは探針 `Probe`（`SyncPlanCatalogProbes`＝方言ごとの決まった問い合わせ）と行ごとの `Query` で読む。

### 計画が警告しない失敗

- 計画が警告しない失敗は `ExpectsUnwarnedFailure(理由)` の行でだけ受ける。汎用の表明 1 を外すのはこの行だけで、表明 1b は外さない。
- 理由は「データの都合：」（計画は列の値を見ない）か「モデルの外の物：」（計画が見ないビューなど）で始める。
- `Outcome` に `failed:` と断片を書き、`Unchanged()` で DB が変わらないことを表明する。

### 行の書き方の照合

- `SyncPlanExpectationRows.Table` が表を作るときに `RealStageProblems` が照合する。見る形は次のとおり。
  - 実 DB の段の期待が無い。
  - 手で組む項目の行。
  - `Live` と `LiveSql` のちょうど一方でない。
  - 網で流さない行に網の期待がある。
  - 行の能力に無い方言。
  - 宣言していない探針。
  - 方言に無い探針。
  - `ExpectsUnwarnedFailure` の理由・`Outcome`・`Unchanged()` の欠け。
- 方言ごとの行の数の下限（`MinimumRealRowsFor`＝`Real{方言}RowCount`＋方言をまたぐ行）を、実 DB の Fact の `CoverageGuard.AtLeast` が照合する。
- DDL の注記は `*DdlNoteTests` に残してある。
- mutation 実証済み（src の `SyncPlanner`・`SchemaDiffService`・方言の同期スクリプト・能力と識別子の宣言を書き換えると、移した行がそろって赤になる）。

## 総当たりの表明と運用

### 表明 1b の規則

- `SyncPlanFuzzRunner.FailedSyncConstraintLosses` の規則 1〜5：
  1. 削除を選んだ制約は消えてよい。
  2. 作り直しを選んだ制約は旧か新の形で残る。主キーが置き換える一意制約（計画の警告 `UniqueConstraintReplacedByPrimaryKey` が名指しした表・列集合）も作り直しとみなし、同じ列集合の主キーが残っていればよい（警告の無い一意制約・列集合の違う主キーは数えない。形の行で主キーを一意制約の列へ移す形が付け直しの失敗に重なって見つかった＝製品は意図どおりに一意制約を主キーへ置き換えている）。
  3. 主キーの変更を選んだ表の主キーは旧か新の形で残る。
  4. 表と列の削除で消えてよいのは、その表・列が実際に消えたときだけ。
  5. ほかは残る。
- 候補キーを証明できないと警告した外部キー自身の消失だけは受け入れる。作り直しに重なったものは注 `warned-alter-foreign-key-exempt:{own-failure|collateral}` として数える。
- 列が空でないと通らない変更の前後で外すと警告した制約（`ConstraintRebuildAroundEmptyColumnChange` が名指しした外部キー＝子の表と名前・主キー＝表・一意制約＝計画の削除の項目の名前から照合キー）の消失も受け入れ、注 `warned-empty-column-change-exempt` として数える（docs の約束どおりの部分適用）。名指しの無い制約・別の表の主キーは違反のまま。
- 既存の行で断られうる文の前後で外すと警告した制約（`ConstraintRebuildAroundRowDependentChange`）の消失も同じ照合で受け入れ、注 `warned-row-dependent-change-exempt` として数える。
- 違反の文に `(rule n; warned=…; dropped-before-failure=…)` を添える。
- 規則は `SyncPlanFuzzOracleTests`（DB なし・1 規則 1 行の表）が固定する。mutation 実証済み。
- 失敗した種の警告の内訳と、「失敗を予告しない種類の警告しか無いのに失敗した種」を出力する（測るだけ）。

### 照合のまとめ方

- 必須の形の表明と網羅しない組の照合は、普段の既定の種の数が 50 以上の方言だけで行う。夜間に種が増えた PostgreSQL・Oracle では行わない。
  - 必須の形の表明は、さらに流す種の数が普段の既定の数以上のときだけ行う（網と同じ）。既定より少ない数を明示した実行（変異テストの SQLite の 100 種）では表明せず、出力に 1 行書く。2026-11 の測定では SQLite は 50 種で `drop-pk-column-alone` が欠けて 70 種で揃い、100 種の余裕は 2 倍に満たなかった。
  - 網羅しない組の照合は、`CoverageReport.AssertGaps` が既定の種の数で流したときだけ行う（明示の数では照合しない）。
- 既知の失敗の照合と必須の形の表明は、両方を集めて 1 つの失敗にする。どちらかの赤がもう一方を隠さないため。
- 夜間に見つけた失敗は `SyncPlanFuzzKnownFailures` に載せる。項目の名前・乱数の項目の数え方・決定的な行は、その一覧のコメントに書く。
- 直した形の項目（`4m-p6-doomed-rebuild`・`candidate-key-re-add-collateral`・`nightly-oracle-100-111`）は一覧から消してあり、行は回帰の網として残る。
- 受け入れの項目 `nightly-sqlite-pk-nonunique`（値が一意でない列への主キーの付け替えを DB がデータで断る）は、行 `sqlite-pk-nonunique` の既存の行で断られる文の宣言（`AddPrimaryKey`）へ移して一覧から消した。一覧は今は空。DB の事実で断られる文は、一覧の受け入れより宣言を先に使う（宣言は表明 1 だけを外し、表明 1b は掛ける）。

### 1 種の例外

- 1 種の処理（生成器・ランナー）の予期しない例外は、その種の失敗（`seed threw:`）として記録して次の種へ進む（`SyncPlanFuzzRunner.RunGuardedAsync`）。取り消しは止める。
- 例外でテストごと止まると、それより前の種の違反が報告されずに隠れるため。
- 固定は `SyncPlanFuzzRunnerGuardTests`（Docker 不要）。
