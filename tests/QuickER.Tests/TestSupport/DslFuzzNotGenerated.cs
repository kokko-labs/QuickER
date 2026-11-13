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
                    "列の型の族（float・DateTimeOffset・TimeSpan）",
                    "族ごとの区切り（TimeSpan → DateTimeOffset → float）",
                    "生成器の列の型（DslClr）にこれらの族が無い。族ごとの実装先の意味（SQLite の文字列の比較・SQL Server の time の範囲・NaN・PostgreSQL の時差の制限など。区切り TF のフェーズ 1 の分析）を docs に書いてから足す",
                    "`CodegenConfig/Cases`（行 `diag-query-type-check-rejects-*`）"
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
                    "型の検査が断る整数のリテラル（列の型の範囲の外と ulong を超える値）を合成の条件に混ぜる",
                    "断るリテラルはクエリごと警告で飛ばすので、合成の条件に混ぜるとほかの原子を照合しなくなる。原子のクエリでだけ作り、飛ばされることを表明する",
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
