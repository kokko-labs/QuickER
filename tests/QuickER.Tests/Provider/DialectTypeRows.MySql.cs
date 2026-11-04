using System.Text;
using QuickER.Dialects;
using QuickER.Provider;
using static QuickER.Tests.Provider.DialectTypeRow;

namespace QuickER.Tests.Provider;

/// <summary>方言の型の表：MySQL の行（型カタログ・型マッパー・取込の表記・方言切替）</summary>
internal static partial class DialectTypeRows
{
    private static IEnumerable<DialectTypeRow> MySqlRows() =>
        [
            .. MySqlCatalogRows(),
            .. MySqlParseRows(),
            .. MySqlFormatRows(),
            .. MySqlFormatParamRows(),
            .. MySqlRoundTripRows(),
            .. MySqlNormalizeRows(),
            .. MySqlSameTypeRows(),
            .. MySqlMapRows(),
            .. MySqlConvertRows(),
        ];

    /// <summary>Id の末尾に使う、型の表記の短い形（英小文字・数字・ハイフン。大文字を含めば -upper を足して小文字の同じ表記と分ける）</summary>
    private static string MySqlSlug(string text)
    {
        var builder = new StringBuilder();

        foreach (var c in text.Trim())
        {
            builder.Append(char.IsAsciiLetterOrDigit(c) ? char.ToLowerInvariant(c) : '-');
        }

        var slug = string.Join(
            '-',
            builder.ToString().Split('-', StringSplitOptions.RemoveEmptyEntries)
        );

        return text.Any(char.IsAsciiLetterUpper) ? slug + "-upper" : slug;
    }

    private static IEnumerable<DialectTypeRow> MySqlCatalogRows() =>
        [
            Catalog("catalog-mysql-default-data-type-int", "DefaultDataType は int")
                .Expect(DialectNames.MySql, "ok default=int"),
        ];

    private static IEnumerable<DialectTypeRow> MySqlParseRows()
    {
        const string simple = "主要型を正規型へ解析できる";
        (string Native, string Kind)[] simples =
        [
            ("smallint", "SmallInt"),
            ("int", "Int32"),
            ("bigint", "Int64"),
            ("float", "Float32"),
            ("double", "Float64"),
            ("date", "Date"),
            ("json", "Json"),
        ];

        foreach (var (native, kind) in simples)
        {
            yield return Parse($"parse-mysql-simple-{MySqlSlug(native)}", simple, native)
                .Expect(DialectNames.MySql, $"ok kind={kind}");
        }

        const string alias = "別名を正規化して解析できる";
        (string Native, string Kind)[] aliases =
        [
            ("integer", "Int32"),
            ("numeric", "Decimal"),
            ("dec", "Decimal"),
            ("double precision", "Float64"),
            ("real", "Float64"),
            ("bool", "Boolean"),
            ("boolean", "Boolean"),
        ];

        foreach (var (native, kind) in aliases)
        {
            yield return Parse($"parse-mysql-alias-{MySqlSlug(native)}", alias, native)
                .Expect(DialectNames.MySql, $"ok kind={kind}");
        }

        foreach (var native in new[] { "tinyint(1)", "bool", "boolean", "bit(1)", "bit" })
        {
            yield return Parse(
                    $"parse-mysql-boolean-family-{MySqlSlug(native)}",
                    "tinyint(1) / bool / boolean / bit(1) は Boolean として解析される",
                    native
                )
                .Expect(DialectNames.MySql, "ok kind=Boolean");
        }

        const string integer = "整数型は符号の有無に応じて範囲を収められる正規型として解析される";
        (string Native, string Kind, int? Precision)[] integers =
        [
            ("tinyint unsigned", "TinyInt", null),
            ("tinyint(4) unsigned zerofill", "TinyInt", null),
            ("tinyint", "SmallInt", null),
            ("tinyint(4)", "SmallInt", null),
            ("smallint", "SmallInt", null),
            ("smallint unsigned", "Int32", null),
            ("int", "Int32", null),
            ("int unsigned", "Int64", null),
            ("INT(10) UNSIGNED", "Int64", null),
            ("bigint", "Int64", null),
            ("bigint unsigned", "Decimal", 20),
            ("mediumint", "Int32", null),
            ("mediumint(8)", "Int32", null),
            ("mediumint unsigned", "Int32", null),
            ("year", "Int32", null),
        ];

        foreach (var (native, kind, precision) in integers)
        {
            var spec = precision is null
                ? $"ok kind={kind} p=- s=-"
                : $"ok kind={kind} p={precision} s=0";
            yield return Parse($"parse-mysql-integer-range-{MySqlSlug(native)}", integer, native)
                .Expect(DialectNames.MySql, spec);
        }

        yield return Parse(
                "parse-mysql-varchar-length",
                "varchar(n) は String(n) として解析される",
                "varchar(50)"
            )
            .Expect(DialectNames.MySql, "ok kind=String len=50");

        foreach (var native in new[] { "tinytext", "text", "mediumtext", "longtext" })
        {
            yield return Parse(
                    $"parse-mysql-text-family-{MySqlSlug(native)}",
                    "tinytext / text / mediumtext / longtext は String(-1) として解析される",
                    native
                )
                .Expect(DialectNames.MySql, "ok kind=String len=-1");
        }

        yield return Parse(
                "parse-mysql-char-length",
                "char(n) は FixedString(n) として解析される",
                "char(10)"
            )
            .Expect(DialectNames.MySql, "ok kind=FixedString len=10");
        yield return Parse(
                "parse-mysql-varbinary-length",
                "varbinary(n) は Binary(n) として解析される",
                "varbinary(255)"
            )
            .Expect(DialectNames.MySql, "ok kind=Binary len=255");

        foreach (var native in new[] { "tinyblob", "blob", "mediumblob", "longblob" })
        {
            yield return Parse(
                    $"parse-mysql-blob-family-{MySqlSlug(native)}",
                    "tinyblob / blob / mediumblob / longblob は Binary(-1) として解析される",
                    native
                )
                .Expect(DialectNames.MySql, "ok kind=Binary len=-1");
        }

        yield return Parse(
                "parse-mysql-binary-length",
                "binary(n) は FixedBinary(n) として解析される",
                "binary(16)"
            )
            .Expect(DialectNames.MySql, "ok kind=FixedBinary len=16");
        yield return Parse(
                "parse-mysql-decimal-precision-scale",
                "decimal(p,s) は Decimal(Precision,Scale) として解析される",
                "decimal(10,2)"
            )
            .Expect(DialectNames.MySql, "ok kind=Decimal p=10 s=2");
        yield return Parse(
                "parse-mysql-datetime-precision",
                "datetime(p) は精度付き DateTime として解析される",
                "datetime(3)"
            )
            .Expect(DialectNames.MySql, "ok kind=DateTime p=3");
        yield return Parse(
                "parse-mysql-timestamp-precision",
                "timestamp(p) は精度付き DateTimeOffset として解析される",
                "timestamp(6)"
            )
            .Expect(DialectNames.MySql, "ok kind=DateTimeOffset p=6");
        yield return Parse(
                "parse-mysql-time-precision",
                "time(p) は精度付き Time として解析される",
                "time(6)"
            )
            .Expect(DialectNames.MySql, "ok kind=Time p=6");
        yield return Parse(
                "parse-mysql-case-and-whitespace",
                "大文字小文字・空白を許容して解析できる",
                "  VARCHAR( 100 ) "
            )
            .Expect(DialectNames.MySql, "ok kind=String len=100");
        yield return Parse(
                "parse-mysql-decimal-without-args",
                "decimal（引数省略）は既定の decimal(10,0) として解析される",
                "decimal"
            )
            .Expect(DialectNames.MySql, "ok kind=Decimal len=- p=10 s=0");

        const string unconvertible = "変換不能な型は TryParse が false を返す";
        foreach (
            var native in new[]
            {
                "enum('a','b')",
                "set('x','y')",
                "bit(8)",
                "geometry",
                "point",
                "no_such_type",
            }
        )
        {
            yield return Parse(
                    $"parse-mysql-unconvertible-{MySqlSlug(native)}",
                    unconvertible,
                    native
                )
                .Expect(DialectNames.MySql, "fail");
        }

        // 負数の型引数は例外ではなく false
        yield return Parse(
                "parse-mysql-unconvertible-varchar-negative",
                unconvertible,
                "varchar(-5)"
            )
            .Boundary(DialectTypeBoundaries.InvalidArg)
            .Expect(DialectNames.MySql, "fail");

        // int 範囲外の型引数は例外ではなく false
        yield return Parse(
                "parse-mysql-unconvertible-varchar-over-int",
                unconvertible,
                "varchar(99999999999)"
            )
            .Boundary(DialectTypeBoundaries.InvalidArg)
            .Expect(DialectNames.MySql, "fail");
        yield return Parse(
                "parse-mysql-unconvertible-decimal-over-int",
                unconvertible,
                "decimal(99999999999,2)"
            )
            .Boundary(DialectTypeBoundaries.InvalidArg)
            .Expect(DialectNames.MySql, "fail");
    }

    private static IEnumerable<DialectTypeRow> MySqlFormatRows()
    {
        const string simple = "TryFormat が主要な正規型からネイティブ型文字列を生成する";
        (CanonicalTypeKind Kind, string Native)[] simples =
        [
            (CanonicalTypeKind.Boolean, "tinyint(1)"),
            (CanonicalTypeKind.TinyInt, "tinyint unsigned"),
            (CanonicalTypeKind.SmallInt, "smallint"),
            (CanonicalTypeKind.Int32, "int"),
            (CanonicalTypeKind.Int64, "bigint"),
            (CanonicalTypeKind.Float32, "float"),
            (CanonicalTypeKind.Float64, "double"),
            (CanonicalTypeKind.Money, "decimal(19,4)"),
            (CanonicalTypeKind.Date, "date"),
            (CanonicalTypeKind.Guid, "char(36)"),
            (CanonicalTypeKind.Xml, "longtext"),
            (CanonicalTypeKind.Json, "json"),
            (CanonicalTypeKind.DateTimeOffset, "timestamp"),
        ];

        foreach (var (kind, native) in simples)
        {
            yield return Format(
                    $"format-mysql-simple-{MySqlSlug(kind.ToString())}",
                    simple,
                    Ct(kind)
                )
                .Expect(DialectNames.MySql, $"ok {native}");
        }

        yield return Format(
                "format-mysql-string-length-varchar",
                "TryFormat(String(n)) は varchar(n) を生成する",
                Ct(CanonicalTypeKind.String, 100)
            )
            .Expect(DialectNames.MySql, "ok varchar(100)");
        yield return Format(
                "format-mysql-string-max-longtext",
                "TryFormat(String(-1)) は longtext を生成する",
                Ct(CanonicalTypeKind.String, -1)
            )
            .Expect(DialectNames.MySql, "ok longtext");
        yield return Format(
                "format-mysql-ansistring-length-varchar",
                "TryFormat(AnsiString(n)) は varchar(n) を生成する",
                Ct(CanonicalTypeKind.AnsiString, 50)
            )
            .Expect(DialectNames.MySql, "ok varchar(50)");
        yield return Format(
                "format-mysql-ansistring-max-longtext",
                "TryFormat(AnsiString(-1)) は longtext を生成する",
                Ct(CanonicalTypeKind.AnsiString, -1)
            )
            .Expect(DialectNames.MySql, "ok longtext");
        yield return Format(
                "format-mysql-fixedstring-length-char",
                "TryFormat(FixedString(n)) は char(n) を生成する",
                Ct(CanonicalTypeKind.FixedString, 10)
            )
            .Expect(DialectNames.MySql, "ok char(10)");
        yield return Format(
                "format-mysql-ansifixedstring-length-char",
                "TryFormat(AnsiFixedString(n)) は char(n) を生成する",
                Ct(CanonicalTypeKind.AnsiFixedString, 5)
            )
            .Expect(DialectNames.MySql, "ok char(5)");
        yield return Format(
                "format-mysql-binary-length-varbinary",
                "TryFormat(Binary(n)) は varbinary(n) を生成する",
                Ct(CanonicalTypeKind.Binary, 255)
            )
            .Expect(DialectNames.MySql, "ok varbinary(255)");
        yield return Format(
                "format-mysql-binary-max-longblob",
                "TryFormat(Binary(-1)) は longblob を生成する",
                Ct(CanonicalTypeKind.Binary, -1)
            )
            .Expect(DialectNames.MySql, "ok longblob");
        yield return Format(
                "format-mysql-fixedbinary-length-binary",
                "TryFormat(FixedBinary(n)) は binary(n) を生成する",
                Ct(CanonicalTypeKind.FixedBinary, 16)
            )
            .Expect(DialectNames.MySql, "ok binary(16)");
        yield return Format(
                "format-mysql-decimal-precision-scale",
                "TryFormat(Decimal) は decimal(p,s) を生成する",
                Ct(CanonicalTypeKind.Decimal, precision: 10, scale: 2)
            )
            .Expect(DialectNames.MySql, "ok decimal(10,2)");
        yield return Format(
                "format-mysql-datetime-precision",
                "TryFormat(DateTime, Precision指定あり) は datetime(p) を生成する",
                Ct(CanonicalTypeKind.DateTime, precision: 3)
            )
            .Expect(DialectNames.MySql, "ok datetime(3)");

        // 上限の表（元は可否だけを見ていたので、書き出せる側は状態だけを写す）
        const string limits =
            "TryFormat は MySQL の上限を超える値と精度の無い Decimal を書き出さない";
        (string Id, CanonicalType Type, string? Boundary, bool Formats)[] rows =
        [
            (
                "datetime-7-over-limit",
                Ct(CanonicalTypeKind.DateTime, precision: 7),
                DialectTypeBoundaries.OverLimit,
                false
            ),
            (
                "datetime-6-at-limit",
                Ct(CanonicalTypeKind.DateTime, precision: 6),
                DialectTypeBoundaries.AtLimit,
                true
            ),
            (
                "time-7-over-limit",
                Ct(CanonicalTypeKind.Time, precision: 7),
                DialectTypeBoundaries.OverLimit,
                false
            ),
            (
                "datetimeoffset-7-over-limit",
                Ct(CanonicalTypeKind.DateTimeOffset, precision: 7),
                DialectTypeBoundaries.OverLimit,
                false
            ),
            (
                "decimal-p66-s2-over-limit",
                Ct(CanonicalTypeKind.Decimal, precision: 66, scale: 2),
                DialectTypeBoundaries.OverLimit,
                false
            ),
            (
                "decimal-p65-s30-at-limit",
                Ct(CanonicalTypeKind.Decimal, precision: 65, scale: 30),
                DialectTypeBoundaries.AtLimit,
                true
            ),
            (
                "decimal-p40-s31-over-limit",
                Ct(CanonicalTypeKind.Decimal, precision: 40, scale: 31),
                DialectTypeBoundaries.OverLimit,
                false
            ),
            (
                "decimal-p10-s11-scale-over-precision",
                Ct(CanonicalTypeKind.Decimal, precision: 10, scale: 11),
                null,
                false
            ),
            (
                "decimal-precisionless",
                Ct(CanonicalTypeKind.Decimal),
                DialectTypeBoundaries.Precisionless,
                false
            ),
            (
                "decimal-precisionless-scale2",
                Ct(CanonicalTypeKind.Decimal, scale: 2),
                DialectTypeBoundaries.Precisionless,
                false
            ),
            (
                "string-16383-at-limit",
                Ct(CanonicalTypeKind.String, 16383),
                DialectTypeBoundaries.AtLimit,
                true
            ),
            (
                "string-16384-over-limit",
                Ct(CanonicalTypeKind.String, 16384),
                DialectTypeBoundaries.OverLimit,
                false
            ),
            (
                "ansistring-16383-at-limit",
                Ct(CanonicalTypeKind.AnsiString, 16383),
                DialectTypeBoundaries.AtLimit,
                true
            ),
            (
                "ansistring-16384-over-limit",
                Ct(CanonicalTypeKind.AnsiString, 16384),
                DialectTypeBoundaries.OverLimit,
                false
            ),
            ("string-max", Ct(CanonicalTypeKind.String, -1), null, true),
            (
                "fixedstring-255-at-limit",
                Ct(CanonicalTypeKind.FixedString, 255),
                DialectTypeBoundaries.AtLimit,
                true
            ),
            (
                "fixedstring-256-over-limit",
                Ct(CanonicalTypeKind.FixedString, 256),
                DialectTypeBoundaries.OverLimit,
                false
            ),
            (
                "ansifixedstring-255-at-limit",
                Ct(CanonicalTypeKind.AnsiFixedString, 255),
                DialectTypeBoundaries.AtLimit,
                true
            ),
            (
                "ansifixedstring-256-over-limit",
                Ct(CanonicalTypeKind.AnsiFixedString, 256),
                DialectTypeBoundaries.OverLimit,
                false
            ),
            (
                "fixedbinary-255-at-limit",
                Ct(CanonicalTypeKind.FixedBinary, 255),
                DialectTypeBoundaries.AtLimit,
                true
            ),
            (
                "fixedbinary-256-over-limit",
                Ct(CanonicalTypeKind.FixedBinary, 256),
                DialectTypeBoundaries.OverLimit,
                false
            ),
            (
                "binary-65532-at-limit",
                Ct(CanonicalTypeKind.Binary, 65532),
                DialectTypeBoundaries.AtLimit,
                true
            ),
            (
                "binary-65533-over-limit",
                Ct(CanonicalTypeKind.Binary, 65533),
                DialectTypeBoundaries.OverLimit,
                false
            ),
            ("binary-max", Ct(CanonicalTypeKind.Binary, -1), null, true),
            // 境界の対（元に無い側）：小数秒の上限ちょうど
            (
                "time-6-at-limit",
                Ct(CanonicalTypeKind.Time, precision: 6),
                DialectTypeBoundaries.AtLimit,
                true
            ),
            (
                "datetimeoffset-6-at-limit",
                Ct(CanonicalTypeKind.DateTimeOffset, precision: 6),
                DialectTypeBoundaries.AtLimit,
                true
            ),
        ];

        foreach (var (id, type, boundary, formats) in rows)
        {
            var row = Format($"format-mysql-limit-{id}", limits, type)
                .Expect(DialectNames.MySql, formats ? "ok" : "fail");
            yield return boundary is null ? row : row.Boundary(boundary);
        }
    }

    private static IEnumerable<DialectTypeRow> MySqlFormatParamRows()
    {
        const string description =
            "TryFormatParameterType は列の上限を当てず、型を書けない規則だけを当てる";
        (string Id, CanonicalType Type, string? Boundary, string Spec)[] rows =
        [
            ("string-20000", Ct(CanonicalTypeKind.String, 20000), null, "ok varchar(20000)"),
            ("fixedstring-300", Ct(CanonicalTypeKind.FixedString, 300), null, "ok char(300)"),
            ("fixedbinary-300", Ct(CanonicalTypeKind.FixedBinary, 300), null, "ok binary(300)"),
            ("binary-70000", Ct(CanonicalTypeKind.Binary, 70000), null, "ok varbinary(70000)"),
            ("datetime-7", Ct(CanonicalTypeKind.DateTime, precision: 7), null, "ok datetime(7)"),
            (
                "decimal-p66-s2",
                Ct(CanonicalTypeKind.Decimal, precision: 66, scale: 2),
                null,
                "ok decimal(66,2)"
            ),
            (
                "string-lengthless",
                Ct(CanonicalTypeKind.String),
                DialectTypeBoundaries.Lengthless,
                "fail"
            ),
            (
                "decimal-negative-scale",
                Ct(CanonicalTypeKind.Decimal, precision: 10, scale: -2),
                DialectTypeBoundaries.NegativeScale,
                "fail"
            ),
            (
                "decimal-precisionless",
                Ct(CanonicalTypeKind.Decimal),
                DialectTypeBoundaries.Precisionless,
                "fail"
            ),
        ];

        foreach (var (id, type, boundary, spec) in rows)
        {
            var row = FormatParam($"format-param-mysql-{id}", description, type)
                .Expect(DialectNames.MySql, spec);
            yield return boundary is null ? row : row.Boundary(boundary);
        }
    }

    private static IEnumerable<DialectTypeRow> MySqlRoundTripRows()
    {
        string[] natives =
        [
            "tinyint(1)",
            "tinyint unsigned",
            "smallint",
            "int",
            "bigint",
            "decimal(10,2)",
            "float",
            "double",
            "varchar(255)",
            "char(10)",
            "longtext",
            "varbinary(255)",
            "binary(16)",
            "longblob",
            "date",
            "time(6)",
            "datetime(3)",
            "timestamp(6)",
            "json",
        ];

        foreach (var native in natives)
        {
            yield return RoundTripText(
                    $"roundtrip-mysql-parse-then-format-{MySqlSlug(native)}",
                    "パース → フォーマットのラウンドトリップで型が保持される",
                    native
                )
                .Expect(DialectNames.MySql, "same");
        }
    }

    private static IEnumerable<DialectTypeRow> MySqlNormalizeRows()
    {
        const string cases = "Normalize: 取込が返す表記へ寄せる";
        (string Input, string Expected)[] rows =
        [
            ("decimal", "decimal(10,0)"),
            ("DECIMAL", "decimal(10,0)"),
            ("decimal(12)", "decimal(12,0)"),
            ("decimal( 12 , 3 )", "decimal(12,3)"),
            ("numeric(18,2)", "decimal(18,2)"),
            ("numeric", "decimal(10,0)"),
            ("decimal(10,2) unsigned", "decimal(10,2) unsigned"),
            ("decimal(10,0)", "decimal(10,0)"),
            ("tinyint(1)", "tinyint(1)"),
            (" varchar(255) ", "varchar(255)"),
            ("datetime(6)", "datetime(6)"),
            ("datetime(0)", "datetime"),
            ("DATETIME( 0 )", "datetime"),
            ("time(0)", "time"),
            ("timestamp(0)", "timestamp"),
            ("time(3)", "time(3)"),
            ("datetime", "datetime"),
        ];

        foreach (var (input, expected) in rows)
        {
            yield return Normalize($"normalize-mysql-cases-{MySqlSlug(input)}", cases, input)
                .Expect(DialectNames.MySql, $"ok {expected}");
        }

        // 取込の出力（COLUMN_TYPE の実際の形）は不動点：入力そのものが期待（冪等は自動の照合も見る）
        string[] fixedPoints =
        [
            "decimal(10,0)",
            "decimal(12,3) unsigned",
            "int",
            "varchar(50)",
            "enum('a','b')",
            "datetime",
            "timestamp",
        ];

        foreach (var columnType in fixedPoints)
        {
            yield return Normalize(
                    $"normalize-mysql-fixed-point-{MySqlSlug(columnType)}",
                    "Normalize: 冪等で、取込の出力は不動点",
                    columnType
                )
                .Expect(DialectNames.MySql, $"ok {columnType}");
        }
    }

    private static IEnumerable<DialectTypeRow> MySqlSameTypeRows() =>
        [
            SameType(
                    "same-type-mysql-wires-decimal-default",
                    "MySQL の capabilities は正規化を配線している",
                    "decimal",
                    "decimal(10,0)"
                )
                .Expect(DialectNames.MySql, "same"),
            SameType(
                    "same-type-mysql-wires-decimal-precision-only",
                    "MySQL の capabilities は正規化を配線している",
                    "decimal(12)",
                    "decimal(12,0)"
                )
                .Expect(DialectNames.MySql, "same"),
            SameType(
                    "same-type-mysql-wires-decimal-different-scale",
                    "MySQL の capabilities は正規化を配線している",
                    "decimal(12)",
                    "decimal(12,2)"
                )
                .Expect(DialectNames.MySql, "different"),
        ];

    private static IEnumerable<DialectTypeRow> MySqlMapRows()
    {
        string[] binaries =
        [
            "tinyblob",
            "blob",
            "mediumblob",
            "longblob",
            "binary(16)",
            "varbinary(100)",
        ];

        foreach (var native in binaries)
        {
            yield return Map(
                    $"map-mysql-binary-bytearray-{MySqlSlug(native)}",
                    "バイナリ系は byte[]（参照型）へマップされる（tinyblob の取りこぼし回帰含む）",
                    native
                )
                .Expect(DialectNames.MySql, "ok type=byte[] ref=1");
        }

        (string Native, string Unbounded)[] unbounded =
        [
            ("blob", "1"),
            ("mediumblob", "1"),
            ("longblob", "1"),
            ("tinyblob", "0"),
            ("binary(16)", "0"),
            ("varbinary(100)", "0"),
        ];

        foreach (var (native, flag) in unbounded)
        {
            yield return Map(
                    $"map-mysql-unbounded-binary-{MySqlSlug(native)}",
                    "blob/mediumblob/longblob は無制限バイナリ、tinyblob/binary(n)/varbinary(n) は有界",
                    native
                )
                .Expect(DialectNames.MySql, $"ok unbounded={flag}");
        }

        foreach (var native in new[] { "varchar(255)", "int" })
        {
            yield return Map(
                    $"map-mysql-non-binary-bounded-{MySqlSlug(native)}",
                    "非バイナリ型は IsUnboundedBinary=false になる",
                    native
                )
                .Expect(DialectNames.MySql, "ok unbounded=0");
        }

        (string Native, string Type)[] bitAndYear =
        [
            ("bit(2)", "ulong"),
            ("bit(8)", "ulong"),
            ("bit(64)", "ulong"),
            ("BIT(8)", "ulong"),
            ("year", "int"),
            ("YEAR", "int"),
        ];

        foreach (var (native, type) in bitAndYear)
        {
            yield return Map(
                    $"map-mysql-bit-year-driver-clr-{MySqlSlug(native)}",
                    "bit(n>1) は ulong、year は int へマップされる",
                    native
                )
                .Expect(DialectNames.MySql, $"ok type={type} ref=0");
        }

        foreach (var native in new[] { "bit", "bit(1)" })
        {
            yield return Map(
                    $"map-mysql-single-bit-bool-{MySqlSlug(native)}",
                    "bit(1) / 引数なし bit は従来どおり bool のまま",
                    native
                )
                .Expect(DialectNames.MySql, "ok type=bool");
        }

        (string Native, string Type)[] unsigned =
        [
            ("tinyint unsigned", "byte"),
            ("smallint unsigned", "ushort"),
            ("mediumint unsigned", "uint"),
            ("int unsigned", "uint"),
            ("integer unsigned", "uint"),
            ("bigint unsigned", "ulong"),
            ("INT UNSIGNED", "uint"),
            ("int unsigned zerofill", "uint"),
        ];

        foreach (var (native, type) in unsigned)
        {
            yield return Map(
                    $"map-mysql-unsigned-integer-{MySqlSlug(native)}",
                    "符号なし整数は byte / ushort / uint / ulong へマップされる",
                    native
                )
                .Expect(DialectNames.MySql, $"ok type={type} ref=0 fallback=0");
        }

        (string Native, string Type)[] signed =
        [
            ("tinyint", "sbyte"),
            ("smallint", "short"),
            ("int", "int"),
            ("bigint", "long"),
            ("int signed", "int"),
        ];

        foreach (var (native, type) in signed)
        {
            yield return Map(
                    $"map-mysql-signed-integer-{MySqlSlug(native)}",
                    "符号付き整数は従来どおりの CLR 型のまま",
                    native
                )
                .Expect(DialectNames.MySql, $"ok type={type}");
        }

        (string Native, string Type)[] mediumInt =
        [
            ("mediumint", "int"),
            ("mediumint(9)", "int"),
            ("mediumint unsigned", "uint"),
        ];

        foreach (var (native, type) in mediumInt)
        {
            yield return Map(
                    $"map-mysql-mediumint-{MySqlSlug(native)}",
                    "mediumint は int / uint へマップされる（フォールバックに落ちない）",
                    native
                )
                .Expect(DialectNames.MySql, $"ok type={type} fallback=0");
        }

        foreach (var native in new[] { "tinyint unsigned", "tinyint(1) unsigned" })
        {
            yield return Map(
                    $"map-mysql-unsigned-tinyint-not-boolean-{MySqlSlug(native)}",
                    "tinyint unsigned は真偽値慣習の対象外で byte になる",
                    native
                )
                .Expect(DialectNames.MySql, "ok type=byte");
        }

        foreach (var native in new[] { "tinyint(1)", "TINYINT(1)" })
        {
            yield return Map(
                    $"map-mysql-signed-tinyint-one-bool-{MySqlSlug(native)}",
                    "tinyint(1)（符号付き）は従来どおり bool のまま",
                    native
                )
                .Expect(DialectNames.MySql, "ok type=bool");
        }

        (string Native, string Type)[] nonInteger =
        [
            ("decimal(10,2) unsigned", "decimal"),
            ("float unsigned", "float"),
            ("double unsigned", "double"),
        ];

        foreach (var (native, type) in nonInteger)
        {
            yield return Map(
                    $"map-mysql-unsigned-non-integer-{MySqlSlug(native)}",
                    "decimal / float / double は unsigned でも CLR 型が変わらない",
                    native
                )
                .Expect(DialectNames.MySql, $"ok type={type}");
        }

        yield return Map(
                "map-mysql-enum-unsigned-value-fallback",
                "enum の値リストの unsigned という語は修飾子として拾わない",
                "enum('signed','unsigned')"
            )
            .Expect(DialectNames.MySql, "ok type=string fallback=1");

        foreach (var native in new[] { "geometry", "enum" })
        {
            yield return Map(
                    $"map-mysql-unknown-type-fallback-{MySqlSlug(native)}",
                    "未知の型は IsFallbackType が true になる",
                    native
                )
                .Expect(DialectNames.MySql, "ok fallback=1");
        }

        yield return Map(
                "map-mysql-tinytext-string-without-fallback",
                "tinytext は string へマップされる（フォールバックに落ちない）",
                "tinytext"
            )
            .Expect(DialectNames.MySql, "ok type=string fallback=0");

        foreach (var native in new[] { "int", "varchar(255)" })
        {
            yield return Map(
                    $"map-mysql-known-type-no-fallback-{MySqlSlug(native)}",
                    "解決できる型は IsFallbackType が false になる",
                    native
                )
                .Expect(DialectNames.MySql, "ok fallback=0");
        }
    }

    private static IEnumerable<DialectTypeRow> MySqlConvertRows()
    {
        (string Source, string Expected)[] toMySql =
        [
            ("nvarchar(50)", "varchar(50)"),
            ("nvarchar(max)", "longtext"),
            ("int", "int"),
            ("bit", "tinyint(1)"),
            ("uniqueidentifier", "char(36)"),
            ("datetime2", "datetime"),
            ("datetimeoffset", "timestamp"),
            ("varbinary(max)", "longblob"),
        ];

        foreach (var (source, expected) in toMySql)
        {
            yield return Convert(
                    $"convert-mysql-from-sqlserver-{MySqlSlug(source)}",
                    "sqlserver → mysql の代表変換",
                    new ConvertInput(DialectNames.SqlServer, source)
                )
                .Expect(DialectNames.MySql, $"converted type={expected}");
        }

        yield return Convert(
                "convert-mysql-from-sqlserver-hierarchyid-unconverted",
                "sqlserver hierarchyid は mysql へ変換できず Unconverted",
                new ConvertInput(DialectNames.SqlServer, "hierarchyid")
            )
            .Expect(DialectNames.MySql, "unconverted");

        (string Source, string Expected)[] toSqlServer =
        [
            ("varchar(50)", "nvarchar(50)"),
            ("longtext", "nvarchar(max)"),
            ("int", "int"),
            ("tinyint(1)", "bit"),
            ("datetime", "datetime2"),
            ("timestamp", "datetimeoffset"),
            ("longblob", "varbinary(max)"),
            ("double", "float"),
            ("json", "nvarchar(max)"),
        ];

        foreach (var (source, expected) in toSqlServer)
        {
            yield return Convert(
                    $"convert-mysql-to-sqlserver-{MySqlSlug(source)}",
                    "mysql → sqlserver の代表変換",
                    new ConvertInput(DialectNames.MySql, source)
                )
                .Expect(DialectNames.SqlServer, $"converted type={expected}");
        }

        yield return Convert(
                "convert-mysql-to-sqlserver-enum-unconverted",
                "mysql enum は sqlserver へ変換できず Unconverted",
                new ConvertInput(DialectNames.MySql, "enum('a','b')")
            )
            .Expect(DialectNames.SqlServer, "unconverted");
    }
}
