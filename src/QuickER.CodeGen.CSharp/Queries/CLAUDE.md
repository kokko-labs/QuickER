# QuickER.CodeGen.CSharp / Queries

名前付きクエリ（`ErDiagram.Queries`）から Repository メソッドを生成する側の規則（定義・簡易 DSL のパーサとエミッタ・型整合検査・生 SQL の静的検証・パラメータ名・実装の出し分け）。
式木を SQL へ翻訳する実行時の側（`SqlExpressionTranslator` の NULL 補償・畳み込み・LIKE の束縛・日付部品・ナビゲーションの拒否・生 SQL のバインダ）は `src/QuickER.Runtime/CLAUDE.md` を見る。
利用者向けの NULL 補償の表と簡易 DSL の制約は `docs/code-generation.md`（.ja）の「クエリ（式木 → SQL 変換）」節。
C# リテラルのエスケープの規則（`GeneratedLiteralText`）は `src/QuickER.CodeGen.CSharp/CLAUDE.md`「文字列リテラルへ書く規則の正本」を見る。

## 構造

- 図に保存するクエリ定義（`QueryDefinition`。エンティティ・列は Guid 参照）から Repository メソッドを生成する
- 条件は簡易 DSL（UI 表記も「簡易 DSL」）
  - 比較・AND / OR / NOT・括弧・`IS [NOT] NULL`・`[NOT] LIKE`・`[NOT] IN`・CONTAINS / STARTSWITH / ENDSWITH
  - パーサは `QueryConditionParser`。診断はローカライズ済み
- 条件を単一の C# ラムダ式へエミットし（`QueryConditionCSharpEmitter`）、既存の `Query()` パイプライン（式木→ `SqlExpressionTranslator`／EF Core は Where 直適用）に流す
  - 方言 SQL への翻訳は実行時に行う。エミッタに方言分岐は無い
- VO 列は `Vo.Create(値)` で比較する
- 戻り形は 一覧／単一／件数／射影（DTO 生成・寛容マッパー互換）＋スカラー（生 SQL・手動実装専用）
- 実装方式の UI 表記は「簡易 DSL」「生 SQL」「手動実装 (partial クラス)」。enum は `Dsl` / `Sql` / `Manual` のまま
- 実装の出し分けの統一規則：**SQL（または DSL 共有本体）が与えられていない実装先は手動実装（manual）**
  - 契約宣言のみ生成し、実装は利用者が partial クラスへ通常メソッドで書く（漏れはコンパイルエラー）
  - EF Core × 生 SQL・SQL 辞書にない方言・インメモリがすべて同じ規則に落ちる
- 型トークンの解決は列型と同じくプロバイダ層（`QueryParameterTypeResolver`。列と同一経路）
- LIKE の意味論はランタイムのエスケープ設計に合わせる：リテラルは `%` 位置で分解し、`LIKE @p` は部分一致（値リテラル扱い）。生 LIKE パターンは生 SQL の担当
- EF Core の SQLite は decimal のサーバーサイド比較・並び替え非対応。SQLite × EF Core で使う図の DSL 条件・並び順は整数キーなどに限る
  - 実 DB パリティ：`NamedQuery{Ado,EfCore}RuntimeTests`（CI 常時実行）

## NULL 許容列の文字列一致

- NULL 許容列の文字列一致は「列が NULL でないこと」を AND した形へエミットする：`(x.Memo != null && x.Memo!.Contains(p))`。否定 `NOT LIKE` も同じ前提の内側で、NULL 行はどちらの向きでも一致しない
- SQL の LIKE は NULL 行を UNKNOWN で落とすため SQL 側では意味が変わらない
- インメモリ実行器は式木をコンパイルして実際に評価するので、前提を書かないと NULL 行で `NullReferenceException` になり、実装先の観測結果が割れる
- NULL 非許容列の生成物はバイト不変
- ガード：`NamedQueryInMemoryRuntimeTests`

## NOT の押し下げ

簡易 DSL の NOT は原子まで押し下げる（`QueryConditionCSharpEmitter.Visit` の `negate`）。

- ド・モルガンで AND / OR を入れ替える。等値は演算子の反転、NULL 判定は向きの反転、IN は NOT IN、文字列一致は既存の Negated へ畳む
- NULL 許容列の順序比較の否定は `(e.Col == null || !(e.Col < x))` を出す
- 規則：NOT と `<>` は C# の否定と同じ（NULL の行も一致）。文字列一致だけは NULL の行がどちら向きでも一致しない
- `!(...)` で包むと SQL の `NOT (UNKNOWN)` が行を落とし、C# の否定（インメモリ）と割れる
- 手書き LINQ の翻訳器はド・モルガン展開しないまま（docs の既知の割り切り）
- ガード：`QueryNegationExecutionTests`（SQLite の ADO・EF Core・インメモリ × VO の有無で、C# の意味の手計算と照合）・`QueryConditionCSharpEmitterTests` の `Emit_Not_*`。mutation 実証済み

## VO × IN の持ち上げ

- VO × IN は、メソッド冒頭でリストを持ち上げる
- 持ち上げ変数は初期化式ごとに 1 つ。同じリストを VO クラスの違う列へ使うと `{p}Values_` の別名にする（同名の 2 回宣言は CS0128）
- 要素型が VO の内包型と違うときだけ `Select(e => Vo.Create(e))` を使う（メソッドグループは暗黙の数値変換が効かず CS0123）

## 型整合検査

- `QueryConditionTypeChecker` は整数リテラルの範囲も見る。ulong 超と、列の型（VO なら内包型）の範囲外は警告で飛ばす
  - 判定は `IntegerRange` の 1 本で、VO と素の列が共通。素の列の範囲外を通すと、C# は常に偽（CS0652）になり、型付きで束縛する SQL Server は変換・算術オーバーフローの例外になる
  - 手書き LINQ の同形は検査しない（既知の割り切り）
- GuidKey の VO 列（FK 列が親の GuidKey VO を共有する場合を含む）への文字列一致は、完全一致の LIKE と `=` 以外を同じく警告で飛ばす
  - `ValueObjectGuidKeyBase` は `IStringMatchValueObject` を持たず、そのまま生成すると生成ファイル全体が CS1929 になる

## 射影 DTO の型名

- 射影 DTO の型名は、生成コードの型名と衝突すると `QueryRuleKind.ReservedResultTypeName` で断る
  - 生成コードの型名とは、図から決まる名前（`GeneratedTypeNames.Collect`）・固定の生成物・ランタイム・単純名で使う外部の型（`GeneratedFixedTypeNames`）
- GUI と `set_query` は `Validate` に図のテーブルを渡す。省略すると図由来の衝突を見逃す
- 名簿の更新を強制するガードは `src/QuickER.CodeGen.CSharp/CLAUDE.md`「型名の名簿」を見る

## 生 SQL の埋め込みと静的検証

- 生 SQL の埋め込みは、改行を含むときだけ「行ごとの逐語リテラル＋元の改行文字をエスケープで書いた通常リテラル」の連結にする（`RenderSqlLiteral`）
  - 逐語リテラルに改行を生のまま書くと、描画後の `ReplaceLineEndings` が OS の改行や U+2028 などに書き換え、SQL の文字列リテラルの値が変わる
  - 1 行の SQL は逐語リテラル 1 つで、バイト不変
  - ガード：`QueryGenerationTests` の `Generate_RawSqlWithLineBreaks_KeepsExactValue`
- 生 SQL は生成前に静的検証する（レベル 1・方言非依存の `RawSqlAnalyzer`）
  - 文字列／コメント／`[..]`／`".."` 内と `@@` システム変数はパラメータ扱いしない 1 パス走査
  - **未宣言パラメータ＝警告＋該当クエリのみ生成スキップ**（実行時に必ず失敗するため。Guid 参照検証と同方針）
    - スキップするのは、この生成で実装を出す方言（`GenerateRepositories` のときの実効方言。偽なら無し）の SQL の未宣言だけ。ほかの方言の SQL は警告のみ
  - **未使用パラメータ／複文（`;` の後にコード）＝警告のみで生成継続**。GUI / CLI 双方に効く。ダイアログは SQL 入力欄近くにインライン表示し、未宣言は OK をブロックする
  - ガード：`QueryCodegenDiagnosticsTests` の `UndeclaredParameterInUnselectedDialect_DoesNotSkipQuery` / `UndeclaredParameterWithoutRepositories_KeepsContract`
- 生 SQL のバインダ（コレクション値の IN 展開・置換の境界）の規則は `src/QuickER.Runtime/CLAUDE.md` を見る

## パラメータ名の予約と this 修飾

- パラメータ名の予約は、`await` と、先頭を大文字にするとリモートのリクエストの record が持てないメンバー名になる 9 名（`QueryDefinitionRules.IsReservedParameterName`）
  - async の生成メソッドの中で `await` は識別子にならない。record はパラメータ名を PascalCase にしたメンバーを持ち、`ToString` などと同じ名前は CS8866 / CS0102 になる
  - リモートを生成しない構成でも予約する
  - 簡易 DSL の射影では、選択式を置く静的フィールドと同じ名前（`QueryDefinitionRules.ProjectionSelectorFieldName`。生成器のフィールド名もこの関数で作る）も断る。静的フィールドは `this.` で修飾できない
- **生成本体が呼ぶメンバー（`Query()`・`QueryBySqlAsync` などの生 SQL の API・同期のデコレータの `_inner`・リモートのクライアントの `InvokeAsync`）は `this.` で修飾する**
  - 外すと同名のパラメータがメンバーを隠し、予約名を増やすしかなくなる。`Query` と `QueryBySqlAsync` の予約はこの修飾で外してある
- 予約は「外すとコンパイルできない名前だけ」にする
- ガード：`QueryReservedParameterNameTests`（`QueryReservedParameterNameTests.ReservedName_IsActuallyUncompilable`＝予約から外すとコンパイルできない／`MemberNamesCalledWithThis_AreNotReservedAndCompile`＝`this.` で呼ぶメンバーと同名でもコンパイルできる／ほかの文脈キーワードが全形で通ることを Roslyn の全数で固定）・`QueryParameterNameSafetyTests`（record・object のメンバー名と生成本体が呼ぶ名前の全部を、リモート＋同期つきで生成器と保存前の規則の両方へ通し、断られるかコンパイルできるかを名前ごとに見る）

## 検証

- 文字列一致（Contains / StartsWith / EndsWith）の null 引数は、全バックエンドで `ArgumentNullException`（`SqlQuery.Where` の共有ガードで fail-fast に統一）
- 一意性チェックの自分自身除外は、主キーが null を取り得る型のときだけ条件付きで生成する（防御の二重化。非 NULL 値型 PK の図の生成物はバイト不変）
