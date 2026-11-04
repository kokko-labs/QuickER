using QuickER.Dialects;
using QuickER.Provider;
using static QuickER.Tests.Provider.DialectTypeRow;

namespace QuickER.Tests.Provider;

/// <summary>型トークン（<see cref="CanonicalTypeToken"/>）の書き出し・解析と、5 方言の書き出しの境界の行</summary>
internal static partial class DialectTypeRows
{
    private static IEnumerable<DialectTypeRow> TokenRows() =>
        [
            .. TokenSimpleRows(),
            .. TokenLengthRows(),
            .. TokenDecimalRows(),
            .. TokenPrecisionOnlyRows(),
            .. TokenAllKindsRows(),
            .. TokenInvalidRows(),
            .. TokenDialectFormatRows(),
        ];

    /// <summary>引数なし種別のトークン正書法と往復</summary>
    private static IEnumerable<DialectTypeRow> TokenSimpleRows()
    {
        const string description = "引数なし種別のトークン正書法と往復";
        (CanonicalTypeKind Kind, string Token)[] kinds =
        [
            (CanonicalTypeKind.Boolean, "boolean"),
            (CanonicalTypeKind.TinyInt, "tinyint"),
            (CanonicalTypeKind.SmallInt, "smallint"),
            (CanonicalTypeKind.Int32, "int32"),
            (CanonicalTypeKind.Int64, "int64"),
            (CanonicalTypeKind.Float32, "float32"),
            (CanonicalTypeKind.Float64, "float64"),
            (CanonicalTypeKind.Money, "money"),
            (CanonicalTypeKind.Date, "date"),
            (CanonicalTypeKind.Guid, "guid"),
            (CanonicalTypeKind.Xml, "xml"),
            (CanonicalTypeKind.Json, "json"),
            (CanonicalTypeKind.RowVersion, "rowversion"),
        ];

        foreach (var (kind, token) in kinds)
        {
            yield return TokenFormat($"token-simple-{token}", description, Ct(kind))
                .Expect($"ok text={token} roundtrip=1");
        }
    }

    /// <summary>長さ引数種別のトークン正書法と往復</summary>
    private static IEnumerable<DialectTypeRow> TokenLengthRows()
    {
        const string description = "長さ引数種別のトークン正書法と往復";
        (CanonicalTypeKind Kind, int? Length, string Token, string Id)[] rows =
        [
            (CanonicalTypeKind.String, 50, "string(50)", "string-50"),
            (CanonicalTypeKind.String, -1, "string(max)", "string-max"),
            (CanonicalTypeKind.String, null, "string", "string-none"),
            (CanonicalTypeKind.AnsiString, 100, "ansistring(100)", "ansistring-100"),
            (CanonicalTypeKind.AnsiString, -1, "ansistring(max)", "ansistring-max"),
            (CanonicalTypeKind.FixedString, 10, "fixedstring(10)", "fixedstring-10"),
            (CanonicalTypeKind.AnsiFixedString, 5, "ansifixedstring(5)", "ansifixedstring-5"),
            (CanonicalTypeKind.Binary, 256, "binary(256)", "binary-256"),
            (CanonicalTypeKind.Binary, -1, "binary(max)", "binary-max"),
            (CanonicalTypeKind.FixedBinary, 16, "fixedbinary(16)", "fixedbinary-16"),
        ];

        foreach (var (kind, length, token, id) in rows)
        {
            var row = TokenFormat($"token-length-{id}", description, Ct(kind, length))
                .Expect($"ok text=\"{token}\" roundtrip=1");

            // 長さの無い文字列・バイナリは長さを要する種類の境界
            yield return length is null ? row.Boundary(DialectTypeBoundaries.Lengthless) : row;
        }
    }

    /// <summary>decimal の精度・スケール（p,s / p / 無指定）の正書法と往復</summary>
    private static IEnumerable<DialectTypeRow> TokenDecimalRows()
    {
        const string description = "decimal の精度スケールのトークン正書法と往復";
        (int? Precision, int? Scale, string Token, string Id)[] rows =
        [
            (10, 2, "decimal(10,2)", "10-2"),
            (18, 0, "decimal(18,0)", "18-0"),
            (10, null, "decimal(10)", "10-none"),
            (null, null, "decimal", "none-none"),
            (10, -2, "decimal(10,-2)", "10-neg2"),
            (5, -1, "decimal(5,-1)", "5-neg1"),
            (null, 2, "decimal(*,2)", "none-2"),
            (null, 0, "decimal(*,0)", "none-0"),
            (null, -2, "decimal(*,-2)", "none-neg2"),
        ];

        foreach (var (precision, scale, token, id) in rows)
        {
            var row = TokenFormat(
                    $"token-decimal-{id}",
                    description,
                    Ct(CanonicalTypeKind.Decimal, precision: precision, scale: scale)
                )
                .Expect($"ok text=\"{token}\" roundtrip=1");

            if (scale is < 0)
            {
                row.Boundary(DialectTypeBoundaries.NegativeScale);
            }
            else if (precision is null)
            {
                row.Boundary(DialectTypeBoundaries.Precisionless);
            }

            yield return row;
        }
    }

    /// <summary>時刻・日時系（小数秒桁 Precision のみ）の正書法と往復</summary>
    private static IEnumerable<DialectTypeRow> TokenPrecisionOnlyRows()
    {
        const string description = "時刻・日時系のトークン正書法と往復";
        (CanonicalTypeKind Kind, int? Precision, string Token, string Id)[] rows =
        [
            (CanonicalTypeKind.Time, 7, "time(7)", "time-7"),
            (CanonicalTypeKind.Time, null, "time", "time-none"),
            (CanonicalTypeKind.DateTime, 7, "datetime(7)", "datetime-7"),
            (CanonicalTypeKind.DateTime, 0, "datetime(0)", "datetime-0"),
            (CanonicalTypeKind.DateTime, null, "datetime", "datetime-none"),
            (CanonicalTypeKind.DateTimeOffset, 7, "datetimeoffset(7)", "datetimeoffset-7"),
            (CanonicalTypeKind.DateTimeOffset, null, "datetimeoffset", "datetimeoffset-none"),
        ];

        foreach (var (kind, precision, token, id) in rows)
        {
            yield return TokenFormat(
                    $"token-precision-{id}",
                    description,
                    Ct(kind, precision: precision)
                )
                .Expect($"ok text=\"{token}\" roundtrip=1");
        }
    }

    /// <summary>全 CanonicalTypeKind の代表値が Format→TryParse で完全一致する（種類の網羅漏れ防止）</summary>
    private static IEnumerable<DialectTypeRow> TokenAllKindsRows()
    {
        foreach (var kind in Enum.GetValues<CanonicalTypeKind>())
        {
            yield return TokenFormat(
                    $"token-all-kinds-{kind.ToString().ToLowerInvariant()}",
                    "全 CanonicalTypeKind が Format→TryParse で往復する",
                    RepresentativeSample(kind)
                )
                .Expect("ok roundtrip=1");
        }
    }

    /// <summary>不正・未知のトークンは TryParse が false を返す</summary>
    private static IEnumerable<DialectTypeRow> TokenInvalidRows()
    {
        const string description = "不正トークンは解析に失敗する";
        (string Id, string Token)[] rows =
        [
            ("empty", ""),
            ("blank", "   "),
            ("unknown-kind", "unknownkind"),
            ("int32-with-arg", "int32(5)"),
            ("string-two-args", "string(1,2)"),
            ("time-two-args", "time(1,2)"),
            ("decimal-max", "decimal(max)"),
            ("decimal-star-only", "decimal(*)"),
            ("string-star", "string(*)"),
            ("datetime-star", "datetime(*)"),
            ("string-empty-parens", "string()"),
            ("string-negative-length", "string(-5)"),
        ];

        foreach (var (id, token) in rows)
        {
            var row = TokenParse($"token-invalid-{id}", description, token).Expect("fail");

            // 長さに負数（max 以外の -1 も弾く）は読めない引数
            yield return id == "string-negative-length"
                ? row.Boundary(DialectTypeBoundaries.InvalidArg)
                : row;
        }
    }

    /// <summary>5 方言の書き出しの境界（精度なし・スケールあり・負のスケール・長さ無し）</summary>
    private static IEnumerable<DialectTypeRow> TokenDialectFormatRows()
    {
        // 精度なし・スケールありの decimal を書き出せるのは Oracle だけ（SQLite はアフィニティの宣言なので DECIMAL）
        yield return Format(
                "format-token-precisionless-scaled-decimal",
                "精度なし・スケールありの decimal を書き出せるのは Oracle だけ",
                Ct(CanonicalTypeKind.Decimal, scale: 2)
            )
            .Boundary(DialectTypeBoundaries.Precisionless)
            .Expect(DialectNames.SqlServer, "fail")
            .Expect(DialectNames.MySql, "fail")
            .Expect(DialectNames.PostgreSql, "fail")
            .Expect(DialectNames.Sqlite, "ok DECIMAL")
            .Expect(DialectNames.Oracle, "ok NUMBER(*,2)");

        // 負のスケールを書き出せるのは PostgreSQL / Oracle だけ（スケールの符号を落とさない）
        yield return Format(
                "format-token-negative-scale-decimal",
                "負のスケールを書き出せるのは PostgreSQL / Oracle だけ",
                Ct(CanonicalTypeKind.Decimal, precision: 10, scale: -2)
            )
            .Boundary(DialectTypeBoundaries.NegativeScale)
            .Expect(DialectNames.SqlServer, "fail")
            .Expect(DialectNames.MySql, "fail")
            .Expect(DialectNames.Sqlite, "fail")
            .Expect(DialectNames.PostgreSql, "ok like:*-2*")
            .Expect(DialectNames.Oracle, "ok like:*-2*");

        // 正のスケールは 5 方言とも従来どおり書き出せる（負のスケールだけを弾いていること）
        yield return Format(
                "format-token-positive-scale-decimal",
                "負のスケールを書き出せるのは PostgreSQL / Oracle だけ",
                Ct(CanonicalTypeKind.Decimal, precision: 10, scale: 2)
            )
            .ExpectAll("ok like:*");

        // 長さの無い文字列・バイナリを書き出せないのは SQL Server / MySQL / Oracle だけ
        // （長さ引数を持つ種類は CanonicalTypeToken から導く）
        foreach (
            var kind in Enum.GetValues<CanonicalTypeKind>()
                .Where(CanonicalTypeToken.HasLengthArgument)
        )
        {
            var name = kind.ToString().ToLowerInvariant();
            const string description =
                "長さの無い文字列・バイナリを書き出せないのは SQL Server / MySQL / Oracle だけ";

            yield return Format($"format-token-lengthless-{name}", description, Ct(kind))
                .Boundary(DialectTypeBoundaries.Lengthless)
                .Expect(DialectNames.SqlServer, "fail")
                .Expect(DialectNames.MySql, "fail")
                .Expect(DialectNames.Oracle, "fail")
                .Expect(DialectNames.PostgreSql, "ok like:*")
                .Expect(DialectNames.Sqlite, "ok like:*");

            // 長さを与えれば 5 方言とも書き出せる（長さ無しだけを弾いていること）
            yield return Format($"format-token-length20-{name}", description, Ct(kind, 20))
                .ExpectAll("ok like:*");
        }
    }
}
