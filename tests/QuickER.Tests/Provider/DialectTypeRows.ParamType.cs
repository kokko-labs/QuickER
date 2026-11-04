using QuickER.Dialects;
using QuickER.Provider;
using static QuickER.Tests.Provider.DialectTypeRow;

namespace QuickER.Tests.Provider;

/// <summary>クエリのパラメータの型解決（<see cref="QueryParameterTypeResolver"/>）の行</summary>
internal static partial class DialectTypeRows
{
    private static IEnumerable<DialectTypeRow> ParamTypeRows() =>
        [.. ParamTypeCaseVariantRows(), .. ParamTypeBeyondLimitRows()];

    /// <summary>大文字を含む解析できないトークンが先に来ても、小文字のトークンの解決を巻き込まない</summary>
    private static IEnumerable<DialectTypeRow> ParamTypeCaseVariantRows()
    {
        const string description =
            "大文字を含むトークンは解決せず、並び順に依らず小文字のトークンだけを解決する";
        (string Order, string First, string Second)[] orders =
        [
            ("upper-first", "String(50)", "string(50)"),
            ("lower-first", "string(50)", "String(50)"),
        ];

        foreach (var (order, first, second) in orders)
        {
            var tokens = new[] { first, second };

            yield return ParamType(
                    $"param-sqlserver-case-variant-{order}-lower-resolves",
                    description,
                    new ParamTypeInput(tokens, "string(50)")
                )
                .Expect(DialectNames.SqlServer, "ok type=string");

            yield return ParamType(
                    $"param-sqlserver-case-variant-{order}-upper-absent",
                    description,
                    new ParamTypeInput(tokens, "String(50)")
                )
                .Expect(DialectNames.SqlServer, "absent");
        }
    }

    /// <summary>列の上限を超える長さ・精度のトークンも、上限の内側と同じ C# 型へ解決される（相対→絶対）</summary>
    private static IEnumerable<DialectTypeRow> ParamTypeBeyondLimitRows()
    {
        const string description =
            "列の上限を超える長さ・精度のパラメータのトークンも、上限の内側と同じ C# 型へ解決される";
        (string Id, string Dialect, string Beyond, string Within, string TypeName)[] rows =
        [
            ("mysql-string", DialectNames.MySql, "string(20000)", "string(100)", "string"),
            (
                "mysql-fixedstring",
                DialectNames.MySql,
                "fixedstring(300)",
                "fixedstring(10)",
                "string"
            ),
            ("mysql-binary", DialectNames.MySql, "binary(70000)", "binary(100)", "byte[]"),
            (
                "mysql-fixedbinary",
                DialectNames.MySql,
                "fixedbinary(300)",
                "fixedbinary(16)",
                "byte[]"
            ),
            (
                "postgresql-decimal",
                DialectNames.PostgreSql,
                "decimal(1001,0)",
                "decimal(10,0)",
                "decimal"
            ),
            (
                "postgresql-datetime",
                DialectNames.PostgreSql,
                "datetime(7)",
                "datetime(3)",
                "DateTime"
            ),
            (
                "postgresql-string",
                DialectNames.PostgreSql,
                "string(10485761)",
                "string(100)",
                "string"
            ),
        ];

        foreach (var (id, dialect, beyond, within, typeName) in rows)
        {
            var tokens = new[] { beyond, within };

            // 列の上限はパラメータに当てない（解決でき、フォールバックの型でもない）
            yield return ParamType(
                    $"param-{id}-beyond-limit",
                    description,
                    new ParamTypeInput(tokens, beyond)
                )
                .Expect(dialect, $"ok type={typeName} fallback=0");

            yield return ParamType(
                    $"param-{id}-within-limit",
                    description,
                    new ParamTypeInput(tokens, within)
                )
                .Expect(dialect, $"ok type={typeName}");
        }
    }
}
