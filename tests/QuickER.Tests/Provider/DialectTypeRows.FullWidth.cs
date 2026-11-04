using QuickER.Dialects;
using QuickER.Provider;
using static QuickER.Tests.Provider.DialectTypeRow;

namespace QuickER.Tests.Provider;

/// <summary>全角などの Unicode の数字を含む型の表記は、どの入口も例外にせず数字として読まない行</summary>
internal static partial class DialectTypeRows
{
    private const string FullWidthFifty = "５０";
    private const string FullWidthTen = "１０";
    private const string FullWidthTwo = "２";
    private const string FullWidthThree = "３";

    private static IEnumerable<DialectTypeRow> FullWidthRows()
    {
        const string description =
            "全角の数字を含む型の表記は、型カタログが解析せず・型マッパーが長さと精度を読まず・正規化が書き換えず・安全な表記とみなさない";
        (string Id, string Dialect, string Type)[] types =
        [
            ("nvarchar-length", DialectNames.SqlServer, $"nvarchar({FullWidthFifty})"),
            (
                "decimal-precision-scale",
                DialectNames.SqlServer,
                $"decimal({FullWidthTen},{FullWidthTwo})"
            ),
            ("datetime2-precision", DialectNames.SqlServer, $"datetime2({FullWidthThree})"),
            ("float-bits", DialectNames.SqlServer, $"float({FullWidthTwo}4)"),
            ("varchar-length", DialectNames.MySql, $"varchar({FullWidthFifty})"),
            (
                "decimal-precision-scale",
                DialectNames.MySql,
                $"decimal({FullWidthTen},{FullWidthTwo})"
            ),
            ("decimal-precision", DialectNames.MySql, $"decimal({FullWidthTen})"),
            ("varchar2-length", DialectNames.Oracle, $"VARCHAR2({FullWidthFifty} CHAR)"),
            (
                "number-precision-scale",
                DialectNames.Oracle,
                $"NUMBER({FullWidthTen},{FullWidthTwo})"
            ),
            ("timestamp-precision", DialectNames.Oracle, $"TIMESTAMP({FullWidthThree})"),
            (
                "character-varying-length",
                DialectNames.PostgreSql,
                $"character varying({FullWidthFifty})"
            ),
            ("numeric-precision", DialectNames.PostgreSql, $"numeric({FullWidthTen},0)"),
            (
                "time-precision-tz",
                DialectNames.PostgreSql,
                $"time({FullWidthThree}) with time zone"
            ),
            ("varchar-length", DialectNames.Sqlite, $"VARCHAR({FullWidthFifty})"),
            (
                "decimal-precision-scale",
                DialectNames.Sqlite,
                $"DECIMAL({FullWidthTen},{FullWidthTwo})"
            ),
        ];

        foreach (var (id, dialect, type) in types)
        {
            var name = $"fullwidth-{dialect}-{id}";

            // 型カタログは解析しない
            yield return Parse($"{name}-parse", description, type)
                .Boundary(DialectTypeBoundaries.InvalidArg)
                .Expect(dialect, "fail");

            // 型マッパーは長さと精度を読まない
            yield return Map($"{name}-map", description, type)
                .Boundary(DialectTypeBoundaries.InvalidArg)
                .Expect(dialect, "ok maxlen=- p=- s=-");

            // 正規化は書き換えない
            yield return Normalize($"{name}-normalize", description, type)
                .Boundary(DialectTypeBoundaries.InvalidArg)
                .Expect(dialect, $"ok text=\"{type}\"");

            // DB は全角の数字の長さ・精度を読めないので安全な表記とみなさない
            yield return SafeText($"{name}-safe", description, type)
                .Boundary(DialectTypeBoundaries.InvalidArg)
                .Expect("unsafe");
        }
    }
}
