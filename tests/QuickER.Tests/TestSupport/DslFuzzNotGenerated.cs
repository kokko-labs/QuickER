using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.TestSupport;

/// <summary>
/// 簡易 DSL の条件の総当たりの生成器（<see cref="DslFuzzGenerator"/>）が作らない形の宣言。SQLite・MySQL と SQL Server の実装先が同じ生成器の
/// 入力を使う
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
            ],
            [
                NotGenerated.Pending(
                    "column-type-families",
                    "列の型の族（float・bool・DateOnly・TimeOnly・DateTimeOffset・TimeSpan・Binary）",
                    "型の族の区切り（未起動・bool から）",
                    "生成器の列の型（DslClr）にこれらの族が無い。族ごとの実装先の意味が docs に無く、足すと族ごとに仕様の判断が要る",
                    "`CodegenConfig/Cases`（行 `diag-query-type-check-rejects-*`）"
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
                NotGenerated.Pending(
                    "same-parameter-twice",
                    "同じパラメータを 2 回参照する条件",
                    "簡易 DSL の残りの区切り（未起動）",
                    "生成器は条件の値ごとに別の名前のパラメータを作るので、1 つのパラメータを条件の 2 か所から参照する形を総当たりが通らない",
                    NotGenerated.Unguarded
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
                NotGenerated.Pending(
                    "multi-column-order",
                    "複数列の並び替え",
                    "簡易 DSL の残りの区切り（未起動）",
                    "並び替えはいつも 1 列で、2 列目以降の並び替えの生成コードを総当たりが通らない",
                    NotGenerated.Unguarded
                ),
                NotGenerated.Pending(
                    "postgresql-oracle-backends",
                    "PostgreSQL と Oracle の構成の実装先",
                    "簡易 DSL の残りの区切り（未起動）",
                    "QuickER 版 Repository は SQL Server と SQLite の方言しか生成しないので、PostgreSQL・Oracle の構成の実装先は EF Core 版とインメモリだけになる。EF Core 版は実 DB で流す実装先で、総当たりは PostgreSQL・Oracle の実 DB を持たないため、両方言の EF Core のプロバイダが条件を訳した結果を照合していない",
                    NotGenerated.Unguarded
                ),
            ]
        );
}
