using QuickER.Dialects;
using QuickER.Provider;
using static QuickER.Tests.Provider.DialectTypeRow;

namespace QuickER.Tests.Provider;

/// <summary>方言切替（<see cref="DiagramTypeConverter"/>）の行（期待の方言＝移行先）</summary>
internal static partial class DialectTypeRows
{
    private static IEnumerable<DialectTypeRow> ConvertRows() =>
        [
            .. ConvertRowVersionRows(),
            .. ConvertOracleUnlimitedIntegerRows(),
            .. ConvertOracleNumberRows(),
        ];

    /// <summary>同一カタログ・行バージョン列・BLOB の戻し</summary>
    private static IEnumerable<DialectTypeRow> ConvertRowVersionRows()
    {
        // 移行元＝移行先の同じ方言なら空の計画（変換の項目が無い）
        yield return Convert(
                "convert-x-same-catalog",
                "from と to が同一カタログなら空の計画を返す",
                new ConvertInput(DialectNames.SqlServer, "nvarchar(100)")
            )
            .Expect(DialectNames.SqlServer, "none");

        yield return Convert(
                "convert-x-sqlserver-rowversion-to-sqlite-blob-nullable",
                "SQL Server の rowversion は SQLite では BLOB かつ NULL 許容へ変換される",
                new ConvertInput(
                    DialectNames.SqlServer,
                    "rowversion",
                    IsNullable: false,
                    Apply: true
                )
            )
            .Expect(
                DialectNames.Sqlite,
                "converted type=BLOB nullable=1 applied-type=BLOB applied-null=1"
            );

        yield return Convert(
                "convert-x-sqlserver-nullable-rowversion-to-sqlite",
                "元から NULL 許容の行バージョン列は MakeNullable が立たない",
                new ConvertInput(DialectNames.SqlServer, "rowversion", IsNullable: true)
            )
            .Expect(DialectNames.Sqlite, "converted nullable=0");

        yield return Convert(
                "convert-x-sqlserver-pk-rowversion-to-sqlite",
                "主キーの行バージョン列は BLOB へ変換されるが MakeNullable は立たない",
                new ConvertInput(
                    DialectNames.SqlServer,
                    "rowversion",
                    IsNullable: false,
                    IsPrimaryKey: true,
                    Apply: true
                )
            )
            .Expect(
                DialectNames.Sqlite,
                "converted type=BLOB nullable=0 applied-type=BLOB applied-null=0"
            );

        yield return Convert(
                "convert-x-sqlite-blob-to-sqlserver",
                "SQLite の BLOB は SQL Server へ戻すと varbinary(max) になる（非可逆）",
                new ConvertInput(DialectNames.Sqlite, "BLOB")
            )
            .Expect(DialectNames.SqlServer, "converted type=\"varbinary(max)\"");
    }

    /// <summary>Oracle の精度なし・スケール 0 の整数は、精度を書く方言へ 38 桁の decimal として注意つきで変換される</summary>
    private static IEnumerable<DialectTypeRow> ConvertOracleUnlimitedIntegerRows()
    {
        const string description =
            "Oracle の精度なし・スケール 0 の整数は、精度を書く方言へ 38 桁の decimal として注意つきで変換される";
        (string Id, string OracleType, string Target, string Expected, bool Capped)[] rows =
        [
            ("integer-to-sqlserver", "INTEGER", DialectNames.SqlServer, "decimal(38,0)", true),
            ("smallint-to-sqlserver", "SMALLINT", DialectNames.SqlServer, "decimal(38,0)", true),
            (
                "number-star-0-to-sqlserver",
                "NUMBER(*,0)",
                DialectNames.SqlServer,
                "decimal(38,0)",
                true
            ),
            ("integer-to-postgresql", "INTEGER", DialectNames.PostgreSql, "numeric(38,0)", true),
            (
                "number-star-0-to-postgresql",
                "NUMBER(*,0)",
                DialectNames.PostgreSql,
                "numeric(38,0)",
                true
            ),
            ("integer-to-mysql", "INTEGER", DialectNames.MySql, "decimal(38,0)", true),
            ("number-star-0-to-mysql", "NUMBER(*,0)", DialectNames.MySql, "decimal(38,0)", true),
            ("integer-to-sqlite", "INTEGER", DialectNames.Sqlite, "DECIMAL", false),
            ("number-star-0-to-sqlite", "NUMBER(*,0)", DialectNames.Sqlite, "DECIMAL", false),
        ];

        foreach (var (id, oracleType, target, expected, capped) in rows)
        {
            yield return Convert(
                    $"convert-x-oracle-{id}",
                    description,
                    new ConvertInput(DialectNames.Oracle, oracleType, Apply: true)
                )
                .Expect(
                    target,
                    $"converted type=\"{expected}\" nullable=0 capped={(capped ? 1 : 0)} applied-type=\"{expected}\""
                );
        }
    }

    /// <summary>Oracle の NUMBER と 0 以外のスケールの NUMBER(*,s) は 38 桁へ倒さない</summary>
    private static IEnumerable<DialectTypeRow> ConvertOracleNumberRows()
    {
        const string description =
            "Oracle の NUMBER と 0 以外のスケールの NUMBER(*,s) は 38 桁へ倒さない";
        (string Id, string OracleType, string Target, string? Expected)[] rows =
        [
            ("number-to-sqlserver", "NUMBER", DialectNames.SqlServer, null),
            ("number-to-mysql", "NUMBER", DialectNames.MySql, null),
            ("number-to-postgresql", "NUMBER", DialectNames.PostgreSql, "numeric"),
            ("number-star-2-to-sqlserver", "NUMBER(*,2)", DialectNames.SqlServer, null),
            ("number-star-2-to-postgresql", "NUMBER(*,2)", DialectNames.PostgreSql, null),
            ("number-star-2-to-mysql", "NUMBER(*,2)", DialectNames.MySql, null),
            ("number-star-2-to-sqlite", "NUMBER(*,2)", DialectNames.Sqlite, "DECIMAL"),
        ];

        foreach (var (id, oracleType, target, expected) in rows)
        {
            yield return Convert(
                    $"convert-x-oracle-{id}",
                    description,
                    new ConvertInput(DialectNames.Oracle, oracleType)
                )
                .Expect(
                    target,
                    expected is null ? "unconverted" : $"converted type={expected} capped=0"
                );
        }
    }
}
