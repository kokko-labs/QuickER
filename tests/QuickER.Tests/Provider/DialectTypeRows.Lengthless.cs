using QuickER.Dialects;
using QuickER.Provider;
using static QuickER.Tests.Provider.DialectTypeRow;

namespace QuickER.Tests.Provider;

/// <summary>長さを要する型の長さ無し・max（-1）の波及先（書き出しと方言切替）の行</summary>
internal static partial class DialectTypeRows
{
    private static IEnumerable<DialectTypeRow> LengthlessRows() =>
        [.. LengthlessFixedWithMaxRows(), .. LengthlessVarcharConvertRows()];

    /// <summary>固定長へ max（-1）を渡したときの書き出し（MySQL は LOB 型へ倒れ、SQL Server と Oracle は書き出さない）</summary>
    private static IEnumerable<DialectTypeRow> LengthlessFixedWithMaxRows()
    {
        const string unusable =
            "固定長へ max（-1）を渡すと実在しない／別種別の表記が返る（対象外の根拠）";
        const string refused = "SQL Server と Oracle は固定長の文字列の max（-1）を書き出さない";

        yield return Format(
                "lengthless-mysql-fixedstring-max-longtext",
                unusable,
                Ct(CanonicalTypeKind.FixedString, -1)
            )
            .Expect(DialectNames.MySql, "ok longtext");

        yield return Format(
                "lengthless-mysql-fixedbinary-max-longblob",
                unusable,
                Ct(CanonicalTypeKind.FixedBinary, -1)
            )
            .Expect(DialectNames.MySql, "ok longblob");

        yield return Format(
                "lengthless-fixedstring-max-not-formattable",
                refused,
                Ct(CanonicalTypeKind.FixedString, -1)
            )
            .Expect(DialectNames.SqlServer, "fail")
            .Expect(DialectNames.Oracle, "fail");

        yield return Format(
                "lengthless-ansifixedstring-max-not-formattable",
                refused,
                Ct(CanonicalTypeKind.AnsiFixedString, -1)
            )
            .Expect(DialectNames.SqlServer, "fail")
            .Expect(DialectNames.Oracle, "fail");

        yield return Format(
                "lengthless-sqlserver-fixedbinary-max-not-formattable",
                refused,
                Ct(CanonicalTypeKind.FixedBinary, -1)
            )
            .Expect(DialectNames.SqlServer, "fail");
    }

    /// <summary>PostgreSQL の長さ無し varchar の方言切替（長さを要する 3 方言では変換不能・長さ付きは変換できる）</summary>
    private static IEnumerable<DialectTypeRow> LengthlessVarcharConvertRows()
    {
        yield return Convert(
                "lengthless-convert-postgresql-varchar",
                "長さ無し varchar の図は SQL Server / MySQL / Oracle への切替で変換不能一覧に載る",
                new ConvertInput(DialectNames.PostgreSql, "varchar")
            )
            .Expect(DialectNames.SqlServer, "unconverted old=varchar")
            .Expect(DialectNames.MySql, "unconverted old=varchar")
            .Expect(DialectNames.Oracle, "unconverted old=varchar");

        // 長さを与えれば変換できる（長さ無しだけを弾いていることの対照。PostgreSQL の varchar は Unicode 可変長＝nvarchar）
        yield return Convert(
                "lengthless-convert-postgresql-varchar-with-length",
                "長さ付き varchar(30) は SQL Server への切替で従来どおり変換される",
                new ConvertInput(DialectNames.PostgreSql, "varchar(30)")
            )
            .Expect(DialectNames.SqlServer, "converted type=\"nvarchar(30)\"");
    }
}
