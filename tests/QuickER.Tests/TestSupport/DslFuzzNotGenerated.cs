using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.TestSupport;

/// <summary>
/// 簡易 DSL の条件の総当たりの生成器（<see cref="DslFuzzGenerator"/>）が作らない形の宣言。SQLite・MySQL（QuickER.Tests）と、SQL Server・
/// PostgreSQL・MySQL・Oracle（Integration の実 DB）の実装先が同じ生成器の入力を使う
/// </summary>
/// <remarks>
/// 純粋なデータ（Engines の型と BCL だけを参照する）。Integration にも <c>DslFuzz*.cs</c> の Compile のリンクで入る。
/// </remarks>
internal static class DslFuzzNotGenerated
{
    /// <summary>宣言</summary>
    public static NotGeneratedDeclaration Declaration { get; } =
        new(
            "DslFuzzGenerator",
            [
                ProtectionNames.EnginePart(EngineIds.Dsl, "SqliteMySql"),
                ProtectionNames.EnginePart(EngineIds.Dsl, "SqlServer"),
                ProtectionNames.EnginePart(EngineIds.Dsl, "PostgreSql"),
                ProtectionNames.EnginePart(EngineIds.Dsl, "MySql"),
                ProtectionNames.EnginePart(EngineIds.Dsl, "Oracle"),
            ],
            [
                NotGenerated.Pending(
                    "column-type-families",
                    "列の型の族（float）",
                    "float の区切り（未起動）",
                    "生成器の列の型（DslClr）に float の族が無い。族の実装先の意味（NaN・±∞・単精度の丸めなど。区切り TF のフェーズ 1 の分析）を docs に書いてから足す",
                    "`CodegenConfig/Cases`（行 `diag-query-type-check-rejects-*`）"
                ),
                NotGenerated.Accepted(
                    "mysql-datetimeoffset-columns",
                    "MySQL の単位の DateTimeOffset の列",
                    "MySQL の型マッパーは DateTimeOffset へ写す型を持たない（timestamp・datetime は DateTime へ写す）ので、MySQL の生成物に DateTimeOffset の列が現れない",
                    "`Dialect/Types`"
                ),
                NotGenerated.Accepted(
                    "datetimeoffset-offsets-beyond-dialect",
                    "SQLite の単位の時差の違う DateTimeOffset の値と、PostgreSQL の単位の時差 0 でない DateTimeOffset の値",
                    "docs の DateTimeOffset の既知の割り切り：SQLite は DateTimeOffset を TEXT で持ち、同じ時差の値どうしでだけ TEXT の比較が瞬間の比較と一致する。PostgreSQL の timestamptz は Npgsql が時差 0 でない値を書く・パラメータにするのを断る。SQLite の単位は時差をそろえた値、PostgreSQL の単位は時差 0 の値だけを作る（時差の混ざった値は SQL Server・Oracle の単位とインメモリが流す）",
                    "`Dsl/SqliteMySql`・`Dsl/PostgreSql`・`table:TranslatorParityPredicates`"
                ),
                NotGenerated.Accepted(
                    "efcore-sqlite-datetimeoffset-ordering",
                    "EF Core（SQLite）での DateTimeOffset の列の順序比較と、DateTimeOffset の列での並び替え",
                    "EF Core の SQLite プロバイダは DateTimeOffset の大小の比較と並び替えを翻訳できず例外にする（Microsoft Learn の「SQLite Database Provider - Limitations」の Query limitations。docs の DateTimeOffset の既知の割り切り）ので、DslConditionFuzzTests の ExcludedOnEfCoreSqlite が EF Core（SQLite）で流さない。QuickER 版とインメモリは網羅の単位 coverage-sqlite-datetimeoffset-ordered で流す",
                    "`Dsl/SqliteMySql`"
                ),
                NotGenerated.Accepted(
                    "postgresql-timetz-columns",
                    "PostgreSQL の単位の timetz（time with time zone）の列",
                    "docs の DateTimeOffset の既知の割り切り：timetz は日付を持たず（Npgsql は日付を書かず、読み戻した値の日付は 0001-01-02）、PostgreSQL は timetz を UTC の時刻で比べてから時差で比べるので、時差の違う同じ瞬間を等しいとしない。瞬間で比べる C# の意味と合わないので、総当たりの PostgreSQL の単位は timestamptz だけを持つ",
                    "`Dialect/Types`"
                ),
                NotGenerated.Accepted(
                    "oracle-timespan-columns",
                    "Oracle の単位の TimeSpan の列",
                    "Oracle の型マッパーは TimeSpan へ写す型を持たない（TIME の型が無く、INTERVAL DAY TO SECOND も TimeSpan に写さない）ので、Oracle の生成物に TimeSpan の列が現れない",
                    "`Dialect/Types`"
                ),
                NotGenerated.Accepted(
                    "timespan-beyond-column-range",
                    "TimeSpan の列の型の範囲の外の値との比較（SQLite のすべての実装先・SQL Server の EF Core 版・MySQL）と、PostgreSQL の time の列への範囲の外の値の書き込み",
                    "docs の TimeSpan の既知の割り切り：SQLite は TimeSpan を文字列で持ち 0〜24 時間未満の値どうしの順序比較だけが数の順と一致し、SQL Server の EF Core 版は time の列の範囲の外の値を送れず例外にし、MySQL は ±838:59:59 の外の値を端へ丸めて比べる。PostgreSQL の time の列は 0〜24:00:00 しか持たず、EF Core 版は範囲の外の値の書き込みを断る（docs の TimeSpan の既知の割り切り）ので行に書かない（範囲の外の値との比較は作る）。SQL Server の範囲の外の値は QuickER 版だけの網羅の単位 coverage-sqlserver-timespan-out-of-day が作る",
                    "`Dsl/SqliteMySql`・`Dsl/SqlServer`・`Dsl/MySql`・`Dsl/PostgreSql`"
                ),
                NotGenerated.Accepted(
                    "efcore-sqlite-timespan-ordering",
                    "EF Core（SQLite）での TimeSpan の列の順序比較と、TimeSpan の列での並び替え",
                    "EF Core の SQLite プロバイダは TimeSpan の大小の比較と並び替えを翻訳できず例外にする（Microsoft Learn の「SQLite Database Provider - Limitations」の Query limitations。docs の TimeSpan の既知の割り切り）ので、DslConditionFuzzTests の ExcludedOnEfCoreSqlite が EF Core（SQLite）で流さない。QuickER 版とインメモリは網羅の単位 coverage-sqlite-timespan-ordered で流す",
                    "`Dsl/SqliteMySql`"
                ),
                NotGenerated.Accepted(
                    "date-time-only-families",
                    "DateOnly・TimeOnly の列",
                    "型マッパーは DateOnly・TimeOnly を作らない（5 方言とも date は DateTime、time は TimeSpan。Oracle は TIME を持たない）。型の検査の族は寛容側の受け皿で、生成される列にもパラメータの型トークン（列と同じマッパーを通る）にも現れない",
                    "`Dialect/Types`"
                ),
                NotGenerated.Accepted(
                    "binary-ordering-columns",
                    "バイナリの列での並び替え（主キーより前の並び）",
                    "docs は実装先をまたぐバイナリの並びを約束していない（SQL Server は短い側を 0x00 で詰めて比べる）",
                    "`Dsl/SqlServer`"
                ),
                NotGenerated.Accepted(
                    "docs-unwritable-forms",
                    "文字列・Guid の順序比較・日時のリテラル・LIKE のリテラルの途中の % と _",
                    "docs と型の検査が書けないとする形で、総当たりは書ける組み合わせだけを作る（書けない形は生成時の診断が受け持つ）",
                    "`QueryConditionParserTests.Parse_LikeLiteral_Decomposes`・`CodegenConfig/Cases`（行 `diag-query-type-check-rejects-*`）"
                ),
                NotGenerated.Accepted(
                    "rejected-integer-literal-in-composite",
                    "型の検査が断る原子（列の型の範囲の外と ulong を超える整数のリテラル・DateTime と DateTimeOffset の混在のパラメータ）を合成の条件に混ぜる",
                    "断る原子はクエリごと警告で飛ばすので、合成の条件に混ぜるとほかの原子を照合しなくなる。原子のクエリでだけ作り、飛ばされることを表明する",
                    "`Dsl/SqliteMySql`"
                ),
                NotGenerated.Accepted(
                    "scalar-return",
                    "Scalar の戻り形",
                    "製品が簡易 DSL の Scalar を断る（クエリの定義の規則）。断ることは生成器と set_query の共有の規則が受け持つ",
                    "`QueryToolGenerationRuleTests.Violation_IsRejectedByGeneratorAndSetQuery`"
                ),
                NotGenerated.Accepted(
                    "name-variety",
                    "列名・表名の多様性",
                    "名前の形の網羅は名前の総当たりが受け持つ。総当たりの照合の対象は条件の意味で、名前の綴りに依らない",
                    "`Name/Entrances`"
                ),
                NotGenerated.Accepted(
                    "string-order-columns",
                    "文字列の列での並び替え（主キーより前の並び）",
                    "docs は実装先をまたぐ文字列の並びを約束していない（インメモリは序数・SQL Server は照合順序＝docs のインメモリの節「実 DB との既知の乖離」）。主キーの GuidKey の並びは序数で比べる",
                    "`Dsl/SqliteMySql`"
                ),
                NotGenerated.Accepted(
                    "out-of-type-values-on-value-objects",
                    "値オブジェクトの列と、列の型に入らない値（精度・スケールの外の decimal・整数を包む値オブジェクトへの decimal）",
                    "値オブジェクトの Create は精度・スケールの外の値を検証エラーにし（docs の値オブジェクトの節）、整数を包む値オブジェクトの列への decimal のパラメータは型の検査が断る。どちらも比べる前に断られるので、列の型に入らない値は値オブジェクトなしの単位でだけ作る",
                    "`table:ValueObjectScenarios`・`CodegenConfig/Cases`（行 `diag-query-type-check-rejects-*`）"
                ),
                NotGenerated.Accepted(
                    "oracle-empty-string-in-not-null-columns",
                    "Oracle の単位の NOT NULL の文字列の列に入れる空文字列",
                    "Oracle は空文字列を NULL として持ち、NOT NULL の列への空文字列の挿入を断る（ORA-01400。docs の「大文字小文字・空白・日付部品」の節の Oracle の段落）。単位の行が入らず照合が始まらないので、NOT NULL の列の値の池から空文字列を外す。NULL を許す列は空文字列を持ち、読み戻すと NULL になる形を照合する",
                    "`Dsl/Oracle`"
                ),
                NotGenerated.Accepted(
                    "oracle-empty-binary-in-not-null-columns",
                    "Oracle の単位の NOT NULL のバイナリの列に入れる空の配列",
                    "Oracle は空のバイナリを NULL として持ち、NOT NULL の列への空の挿入を断る（docs の「大文字小文字・空白・日付部品」の節のバイナリの段落。空文字列と同じ）。単位の行が入らず照合が始まらないので、NOT NULL の列の値の池から空の配列を外す。NULL を許す列は空の配列を持ち、読み戻すと NULL になる形を照合する",
                    "`Dsl/Oracle`"
                ),
                NotGenerated.Accepted(
                    "postgresql-money-negative-literal",
                    "PostgreSQL の money の列と比べる負の数のリテラル",
                    "Npgsql の EF Core のプロバイダは負の定数を `-1::money` と書き、PostgreSQL は money の単項の負号を持たないので断る（42883。docs の「大文字小文字・空白・日付部品」の節の既知の割り切り）。負の値はパラメータで比べる（行の値・パラメータの値の池は負の値を持つ）",
                    "`Dsl/PostgreSql`"
                ),
            ]
        );
}
