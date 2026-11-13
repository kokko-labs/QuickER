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

## Repository の無い表のクエリ

- 名前付きクエリは表の Repository のメソッドとして出るので、Repository を作らない表（主キーが 1 列でない＝複合主キー・主キーなし。判定は `HasSinglePrimaryKey`）のクエリは生成されない
- その表のクエリは、表の警告 `CodeGen_Warning_RepositorySingleKeyOnly` の直後に、警告 `CodeGen_Query_SkippedWithoutRepository` で 1 件ずつ名指しする（`CSharpGenerationModelBuilder.WarnQueriesWithoutRepository`）。生成は止めない
  - 形はクエリを飛ばすほかの警告（参照の切れ・生 SQL の未宣言・型整合）と同じ「クエリ 1 件＝診断 1 件」。表の警告だけでは、利用者がクエリの消えたことに気づけない
  - 対象は参照の検証を通った有効なクエリだけ（参照の切れたクエリは `CollectValidQueries` が別の警告で名指し済みなので重ねない）
- Repository 契約を生成しない構成（`GeneratesRepositoryContract` が偽）では、表の警告と同じくクエリの警告も出さない
  - その構成ではどの表にも Repository が無く、Repository の全メンバー（CRUD・`Query()`）が無い。クエリだけが黙って消えるのではなく、構成として Repository を作らないことを選んでいる。表の警告・採番の主キーの警告 `CodeGen_Warning_IdentityPrimaryKeyColumn` と同じ条件に揃える
  - 出すと、エンティティ・EditModel だけを生成する使い方で、クエリを持つ図は毎回クエリの数だけ警告が並び、本当の欠落が埋もれる
- 定義の時点（GUI のクエリ定義ダイアログ・MCP の `set_query`）では知らせない。主キーは定義の後でも変わるので、いつも当たるのは生成の時点だけ。生成の 3 つの入口（GUI・CLI・MCP の `generate_csharp`）は `DiagramCodeGenerator.Generate` → `CSharpCodeGenerationService.Generate` に合流するので、3 つとも同じ警告が出る
- ガード：`CodegenCaseTests` の行 `diag-repository-single-key-composite-query-warning` / `diag-repository-single-key-keyless-query-warning`（簡易 DSL・生 SQL・手動実装のクエリを 1 本ずつ名指しし、主キーが 1 列の表のクエリは警告しない。Exact）・`diag-repository-single-key-query-without-contract-no-warning`（契約なしでは出ない）・網羅の配列の診断の規則（図の形 `composite-only` / `keyless-only` × クエリ × 契約。`Judges/DiagnosticRules.cs` の B 段）。mutation 実証済み（警告を外す・規則を外す・契約なしまで広げる、の各回で赤）

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
- ガード：`DslConditionFuzzTests`（SQLite の ADO・EF Core・インメモリ × VO の有無で、docs の意味のオラクルと照合。NOT の段数と NOT が包む合成の種類は次元表の因子 NotNesting で数え、網羅の単位 `DslFuzzUnits.Coverage`〔VO あり〕・`DslFuzzUnits.PlainCoverage`〔VO なし〕の決まった形が NOT NOT・NOT NOT NOT・NOT (AND 群)・NOT (AND (比較, IS NULL)) を毎回流す）・`QueryConditionCSharpEmitterTests`（エミッタの出力の綴りの表）。mutation 実証済み

## 文字列のリテラルの持ち上げ

- 簡易 DSL の文字列のリテラル（比較の右辺・LIKE の分解・CONTAINS などの引数）は、メソッド冒頭の局所変数 `var literal{番号} = "...";` へ持ち上げ、ラムダはその変数を参照する（`QueryConditionCSharpEmitter.Context.LiftStringLiteral`）。同じ値は 1 つの変数を共有し、名前はメソッド引数・ほかの持ち上げ変数とぶつかると `_` を後置する。VO の列は `Vo.Create(literal1)`
  - 理由：ラムダの中の定数は、EF Core が SQL のリテラルへ書き、`IsUnicode(false)` の列と比べると `N` を付けないので、ASCII の外の文字が `?` になって化けた行に一致する。変数にすると EF Core はパラメータにし、`SqlServerQueryParameterInterceptor` が値の型で束縛する（`src/QuickER.Runtime.EntityFrameworkCore/CLAUDE.md`「SQL Server のクエリのパラメータの型」）
  - QuickER 版 Repository とインメモリは、変数でも定数でも値として扱うので答えは変わらない。数値のリテラルは持ち上げない（EF Core のリテラルは値の型のまま書かれ、C# と同じ答えになる）
- ガード：`QueryConditionCSharpEmitterTests`（行 `string-literal-*`・`value-object-string-literal` と、LIKE・CONTAINS の行の前置文）・`Dsl/SqlServer`（単位 sqlserver-ansi の Unicode でない列と ASCII の外の文字のリテラルの比較・LIKE・文字列一致）。mutation 実証済み（持ち上げを戻すと総当たりの EF Core のリテラルの形だけが 42 件赤）

## VO × IN の持ち上げ

- VO × IN は、メソッド冒頭でリストを持ち上げる
- 持ち上げ変数は初期化式ごとに 1 つ。同じリストを VO クラスの違う列へ使うと `{p}Values_` の別名にする（同名の 2 回宣言は CS0128）
- 要素型が VO の内包型と違うときだけ `Select(e => Vo.Create(e))` を使う（メソッドグループは暗黙の数値変換が効かず CS0123）

## 型整合検査

- `QueryConditionTypeChecker` は整数リテラルの範囲も見る。ulong 超と、列の型（VO なら内包型）の範囲外は警告で飛ばす
  - 判定は `IntegerRange` の 1 本で、VO と素の列が共通。素の列の範囲外を通すと、C# は常に偽（CS0652）になる
  - 手書き LINQ の同形は検査しない（既知の割り切り）
- 整数の列と小数のリテラルは、素の列なら幅に依らず受理して数として比べ（C# が列を double へ拡大する）、整数を包む VO の列は警告で飛ばす（`Create` が整数しか受け取らず CS1503）
  - エミッタは小数のリテラルにサフィックスを付けない（long の列に `L` を付けると `1.5L` の字句エラー）
  - SQL Server の QuickER 版 Repository は、列の型に入らない値を値の型で束縛する（`src/QuickER.Runtime.SqlServer/CLAUDE.md`「SqlParameter の明示型」）。列の型で束縛すると値が切り捨てられて誤った行を返す
- GuidKey の VO 列（FK 列が親の GuidKey VO を共有する場合を含む）への文字列一致は、完全一致の LIKE と `=` 以外を同じく警告で飛ばす
  - `ValueObjectGuidKeyBase` は `IStringMatchValueObject` を持たず、そのまま生成すると生成ファイル全体が CS1929 になる
- DateTime と DateTimeOffset の混在は、コンパイルが通っても警告で飛ばす（型の検査の「コンパイルの通る形を断らない」の唯一の例外。違反の種類 `TimeZoneDependentOperand`・文面 `CodeGen_Query_ConditionTimeZoneDependentOperand`）
  - 対象：DateTimeOffset の列 × DateTime のパラメータ（型トークン・列で型付け）の比較と、その逆向き、IN のリストの要素の同じ組、値オブジェクトの列（内包型で見る）。判定は `MixesDateTimeAndOffset` の 1 か所で、比較・IN の型の判定より先に当てる。暗黙変換の表（`ImplicitConversions`）と `Comparable` は DateTime → DateTimeOffset を通さない
  - 理由：C# の暗黙変換が DateTime（Kind が Unspecified・Local）を実行する機械の時間帯の時差で DateTimeOffset にするので、同じクエリの答えが機械で変わる。docs は「簡易 DSL と名前付きクエリの制約」に、手書きの LINQ は検査しないことを既知の割り切りに書く
  - 重さは既存の型の違反と同じ Warning でそのクエリだけ飛ばす（そのクエリの中で完結する問題。Error にすると、こういうクエリを 1 本持つだけで生成全体が止まる）
  - 値オブジェクトで型付けしたパラメータは値オブジェクトの一致で判定するので対象にしない（違う値オブジェクトは既存の `OperandTypeMismatch`）
- ガード：`CodegenCaseTests` の行 `diag-query-type-check-rejects-*`（断る組み合わせ 1 行 1 入力：警告の種類とクエリ名・そのクエリのメソッドだけが無い・ほかのクエリは生成してコンパイルできる）・`CodegenCaseTests` の行 `compile-query-type-check-accepts-widening-forms`（int・long の列×小数リテラルを含む）・`DslConditionFuzzTests`（受理する形は警告 0 のコンパイルと実行、断るリテラルは警告で飛ばされること。整数の列×小数リテラルは次元表の形 `compare-*-literal-fractional`。DateTime と DateTimeOffset の混在のパラメータは DateTimeOffset の単位の断られる原子＝形 `compare-*-param-rejected`・`in-rejected`）・`Dsl/SqlServer`・`Dsl/PostgreSql`・`Dsl/Oracle`（同じ形を実 DB で）。混在の 6 行（`diag-query-type-check-rejects-bad-offset-*`・`-bad-datetime-*`・`-bad-vo-offset-*`）は文面のキーまで照合する（判定を外すと既存の `OperandTypeMismatch` の文面で断られて赤）。mutation 実証済み

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
  - ガード：`CodegenCaseTests` の行 `code-query-raw-sql-undeclared-parameter-in-unselected-dialect-keeps-query` / `code-query-raw-sql-undeclared-parameter-without-repositories-keeps-contract` / `code-query-raw-sql-undeclared-parameter-in-unused-dialect-keeps-query`・`CodegenCaseTests` の行 `diag-query-raw-sql-undeclared-parameter-warns-and-skips`
- 生 SQL のバインダ（コレクション値の IN 展開・置換の境界）の規則は `src/QuickER.Runtime/CLAUDE.md` を見る

## パラメータ名の予約と this 修飾

- パラメータ名の予約は、`await` と、先頭を大文字にするとリモートのリクエストの record が持てないメンバー名になる 9 名（`QueryDefinitionRules.IsReservedParameterName`）
  - async の生成メソッドの中で `await` は識別子にならない。record はパラメータ名を PascalCase にしたメンバーを持ち、`ToString` などと同じ名前は CS8866 / CS0102 になる
  - リモートを生成しない構成でも予約する
  - 簡易 DSL の射影では、選択式を置く静的フィールドと同じ名前（`QueryDefinitionRules.ProjectionSelectorFieldName`。生成器のフィールド名もこの関数で作る）も断る。静的フィールドは `this.` で修飾できない
- **生成本体が呼ぶメンバー（`Query()`・`QueryBySqlAsync` などの生 SQL の API・同期のデコレータの `_inner`・リモートのクライアントの `InvokeAsync`）は `this.` で修飾する**
  - 外すと同名のパラメータがメンバーを隠し、予約名を増やすしかなくなる。`Query` と `QueryBySqlAsync` の予約はこの修飾で外してある
- 予約は「外すとコンパイルできない名前だけ」にする
- ガード：`QueryReservedParameterNameTests`（`QueryReservedParameterNameTests.ReservedName_IsActuallyUncompilable`＝予約から外すとコンパイルできない／`QueryReservedParameterNameTests.SelectorFieldName_IsActuallyUncompilable`＝選択式のフィールド名も同じ／`await` と選択式のフィールド名を保存前の規則がちょうど 1 件の違反で断る）・`QueryParameterNameSafetyTests`（record・object のメンバー名と生成本体が `this.` で呼ぶ名前と Roslyn の文脈キーワードの全数を、生 SQL・簡易 DSL の全戻り形と NULL 許容列への IN のリスト・値オブジェクトのあり／なし・リモート＋同期つきで生成器と保存前の規則の両方へ通し、断られる名前の集合がちょうど `await` と record のメンバーの 9 名〔先頭の大小の両方〕であることと、それ以外の名前が全部の形でコンパイルできることを見る）・`CodegenCaseTests` の行 `diag-query-reserved-parameter-name-*`（生成器の診断。選択式のフィールド名・生 SQL の単一戻りの `await` を含む）

## 列の削除とクエリの参照

- 列を削除しても（列の追加の Undo で列が外れても）、名前付きクエリは書き換えない。削除した列を参照するクエリを名指しして知らせる
  - 消さない理由：きれいに消せるのは並び順だけ。パラメータを消すとメソッドの引数が変わって条件・生 SQL の `@名前` が宙に浮き、射影フィールドを消すと DTO の形が変わり、条件から列を外すと意味が変わる（AND の片側を消すと結果が広がる）。どれも利用者の契約を黙って変える。名前の変更の追従が「条件式で読める新名のときだけ書き換える」のと同じ考え方
  - 書き換えないので、削除を Undo すれば列が同じ Id・同じ名前で戻り、参照もそのまま生き返る
  - 切れた参照は、クエリ定義ダイアログが名指しして OK を止め（条件のエラーの文もクエリ名を出す）、生成は Id の参照でも条件の列名でも警告してそのクエリだけ飛ばす（条件は下の「条件の検証の失敗はそのクエリだけ飛ばす」）
- 判定の正本は `QueryColumnReferences`（このフォルダ）
  - `Find`：そのテーブルのクエリの、列を参照する箇所（パラメータ・並び順・射影フィールドは列の Id、条件は列名）。ほかのテーブルのクエリは同じ名前の列があっても数えない。条件は実装方式が簡易 DSL のときだけ数える（生 SQL・手動実装では条件を使わず、切れても検証も生成も止まらないので、数えると知らせすぎになる）
  - `FindInCondition`：条件の中の列名の読み方（`QueryConditionParser.Parse` の列参照を `SchemaNames.AreSame` で照合。パラメータ・文字列リテラルの同じ綴りは数えない。パース不能は null）。**名前の変更の追従 `QueryConditionRenamer` も同じ関数を呼ぶ**（写しを作ると、追従が書き換える参照と削除で名指しする参照が食い違う）
  - `DescribeRemovedColumn`：ツールの応答に足す英語の注意文。MCP の `DocumentErDiagramToolHost` と内蔵チャットの `ErDiagramDynamicTools` が同じ文を返す
- GUI の画面は `MainViewModel.OnEntityColumnsChangedForQueryReferences`（列のコレクションの購読）がステータスバーで名指しする（→ `src/QuickER.Gui/ViewModels/CLAUDE.md`「列の削除とクエリの参照の通知」）
- ガード：`ToolScenarioTests`（行 `column-remove-referenced-by-queries-names-each-query`・`column-remove-condition-reference-is-case-insensitive`・`column-remove-not-referenced-keeps-previous-message`。両ホスト）・`UndoOperationTests`（行 `column-remove-referenced-by-query-warns-and-undo-revives-references`・`column-add-undo-referenced-by-query-warns`・`column-remove-not-referenced-does-not-warn`・`column-query-references-table-removal-does-not-warn`）・`QueryDefinitionDialogViewModelTests.Condition_ValidatesImmediately`（条件のエラーの文のクエリ名）・`QueryConditionRenamerTests`（追従の大小無視。共有の照合を大小を区別する形へ壊すと追従のテストも赤）。mutation 実証済み（条件の照合・大小無視・簡易 DSL の分岐・MCP 側の注意文だけ・GUI の購読・参照が無くても知らせる・ダイアログのクエリ名、の各回で赤）

## 条件の検証の失敗はそのクエリだけ飛ばす

- 簡易 DSL の条件が `QueryConditionParser.ParseAndValidate` に失敗したら、診断ごとに Warning（`CodeGen_Query_ConditionInvalid`。クエリ名とパーサの診断の文）を出し、そのクエリだけ飛ばす（`PlanQueryMethod` の `hasError`。射影の DTO の型名も返す）。ほかのクエリと生成全体は止めない
  - 対象はパーサの診断すべて（構文・未知の列とパラメータ・IN とリストの整合・NULL 非許容の列への IS NULL）。どれもそのクエリの中で完結する。線引きは型整合の違反（`QueryConditionTypeChecker`）・生 SQL の未宣言パラメータ・Id の参照の切れ（`CollectValidQueries`）と同じ「そのクエリの中だけで完結する問題はそのクエリだけ飛ばす」
  - Error のまま残すのは `QueryDefinitionRules` の違反（メソッド名・戻り形・パラメータ名・射影の型名）と型トークンの解決の失敗。ほかのクエリ・メンバーとの名前の重なりや出力の形に関わる
  - Error にすると、列 1 本の削除や列を NOT NULL にしただけで生成全体が止まる（CLI は終了コード 1 で何も書かず、GUI はエラーダイアログ）。診断の種類で Warning と Error を振り分ける一覧は作らない（パーサに診断を足すと黙って片側へ倒れる）
  - **`hasError` は必ず立てる**。立てずに続けると条件の無いメソッド（全件取得）が出て、壊れた条件が黙って広いデータを返す
- 影響：CLI の `generate` / `scaffold` は警告を標準エラーへ出して終了コード 0、GUI の生成ダイアログは書き出して完了の詳細に警告を並べる、MCP の `generate_csharp` は警告つきで成功する
- ガード：`CodegenCaseTests` の行 `diag-query-condition-invalid-warns-and-skips-only-that-query`（削除した列の名前・構文エラー・未宣言のパラメータ・条件の壊れた射影を名指しする Warning・メソッドが無い・対照のクエリと、飛ばした射影と同じ DTO 名の正当な射影はコンパイルできる）・`diag-query-null-check-on-non-nullable-column-warns-and-skips`（Warning がちょうど 1 件・飛ばしたメソッドが無い）・`diag-query-invalid-queries-are-errors`（規則の違反は Error のまま）。mutation 実証済み（Error に戻す・`hasError` を立てない＝全件取得のメソッドが出る・警告からクエリ名を外す・DTO の型名を返さない、の各回で赤）

## 検証

- 文字列一致（Contains / StartsWith / EndsWith）の null 引数は、全バックエンドで `ArgumentNullException`（`SqlQuery.Where` の共有ガードで fail-fast に統一）
- 一意性チェックの自分自身除外は、主キーが null を取り得る型のときだけ条件付きで生成する（防御の二重化。非 NULL 値型 PK の図の生成物はバイト不変）
