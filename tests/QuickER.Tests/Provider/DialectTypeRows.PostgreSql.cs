using QuickER.Dialects;
using QuickER.Provider;
using static QuickER.Tests.Provider.DialectTypeRow;

namespace QuickER.Tests.Provider;

internal static partial class DialectTypeRows
{
    private const string PgSimpleKind = "主要型を正規型へ解析できる";
    private const string PgAlias = "別名を正規化して解析できる";
    private const string PgMultiWord = "複数語の日付時刻型名を正規化して解析できる";
    private const string PgUnconvertible = "変換不能な型は TryParse が false を返す";
    private const string PgModifier = "修飾子が名称の途中へ入る PostgreSQL の正準表記も解析できる";
    private const string PgFormatSimple =
        "TryFormat が主要な正規型からネイティブ型文字列を生成する";
    private const string PgBinaryAny =
        "TryFormat の Binary / FixedBinary は長さを問わず bytea を生成する";
    private const string PgRoundTrip = "パース → フォーマットのラウンドトリップで型が保持される";
    private const string PgNegScale =
        "numeric(p,-s)（負のスケール）も Decimal として解析され同じ表記へ戻る";
    private const string PgNoNegPrecision =
        "符号を許すのはスケールだけ（負の長さ・精度は従来どおり変換不能）";
    private const string PgLimits =
        "TryFormat は PostgreSQL の上限を超える長さ・精度・スケールを書き出さない";
    private const string PgParamLimits =
        "TryFormatParameterType は列の上限を当てず、型を書けない規則だけを当てる";
    private const string PgNormalize = "Normalize: 図の短縮語彙を取込が返す表記へ寄せる";
    private const string PgIdempotent = "Normalize: 冪等";
    private const string PgImporter = "取込の正規化は同じ関数を通す";
    private const string PgWired = "PostgreSQL の capabilities は正規化を配線している";
    private const string PgTimetz = "time with time zone は DateTimeOffset へマップされる";
    private const string PgTimeNoTz =
        "time without time zone は TimeSpan のまま（EF Core に教える列の型は time）";
    private const string PgTsPrecision = "精度つきでも時間帯の語を落とさない";
    private const string PgUnknownMap = "未知の型は IsFallbackType が true になる";
    private const string PgKnownMap = "解決できる型は IsFallbackType が false になる";
    private const string PgNotUnbounded = "非バイナリ型は IsUnboundedBinary=false になる";
    private const string PgDriverType = "interval・cidr は Npgsql が返す CLR 型へ写す";
    private const string PgEfCoreColumnType =
        "Npgsql の EF Core の既定の対応で書けない列（date・timestamp・time・timetz・json・jsonb・xml）だけが EF Core に教える列の型を持つ";
    private const string PgConvertFromSql = "sqlserver → postgresql の代表変換";
    private const string PgConvertToSql = "postgresql → sqlserver の代表変換";

    private static DialectTypeRow PgParse(
        string id,
        string description,
        string input,
        string spec
    ) => Parse($"parse-postgresql-{id}", description, input).Expect(DialectNames.PostgreSql, spec);

    private static DialectTypeRow PgFormat(
        string id,
        string description,
        CanonicalType canonical,
        string spec,
        string? boundary = null
    )
    {
        var row = Format($"format-postgresql-{id}", description, canonical);

        if (boundary is not null)
        {
            row.Boundary(boundary);
        }

        return row.Expect(DialectNames.PostgreSql, spec);
    }

    private static DialectTypeRow PgFormatParam(
        string id,
        CanonicalType canonical,
        string spec,
        string? boundary = null
    )
    {
        var row = FormatParam($"format-param-postgresql-{id}", PgParamLimits, canonical);

        if (boundary is not null)
        {
            row.Boundary(boundary);
        }

        return row.Expect(DialectNames.PostgreSql, spec);
    }

    private static DialectTypeRow PgRoundTripRow(string id, string input) =>
        RoundTripText($"roundtrip-postgresql-{id}", PgRoundTrip, input)
            .Expect(DialectNames.PostgreSql, "same");

    private static DialectTypeRow PgNorm(
        string id,
        string description,
        string input,
        string expected
    ) =>
        Normalize($"normalize-postgresql-{id}", description, input)
            .Expect(DialectNames.PostgreSql, $"ok {expected}");

    private static DialectTypeRow PgMap(string id, string description, string input, string spec) =>
        Map($"map-postgresql-{id}", description, input).Expect(DialectNames.PostgreSql, spec);

    private static DialectTypeRow PgImport(string id, string name) =>
        ImportSpelling($"import-spelling-postgresql-{id}", PgImporter, new ImportedType(name))
            .Expect(DialectNames.PostgreSql, "ok normalize=1");

    private static DialectTypeRow PgSame(string id, string left, string right, string spec) =>
        SameType($"same-type-postgresql-{id}", PgWired, left, right)
            .Expect(DialectNames.PostgreSql, spec);

    private static DialectTypeRow PgConvertFromSqlServer(
        string id,
        string input,
        string expected
    ) =>
        Convert(
                $"convert-postgresql-from-sqlserver-{id}",
                PgConvertFromSql,
                new ConvertInput(DialectNames.SqlServer, input)
            )
            .Expect(DialectNames.PostgreSql, $"converted type={expected}");

    private static DialectTypeRow PgConvertToSqlServer(string id, string input, string expected) =>
        Convert(
                $"convert-postgresql-to-sqlserver-{id}",
                PgConvertToSql,
                new ConvertInput(DialectNames.PostgreSql, input)
            )
            .Expect(DialectNames.SqlServer, $"converted type={expected}");

    private static IEnumerable<DialectTypeRow> PostgreSqlRows() =>
        [
            // ---- 型カタログの事実 ----
            Catalog("catalog-postgresql-default-integer", "DefaultDataType は integer")
                .Expect(DialectNames.PostgreSql, "ok default=integer"),
            // ---- 解析：主要型 ----
            PgParse("simple-boolean", PgSimpleKind, "boolean", "ok kind=Boolean"),
            PgParse("simple-smallint", PgSimpleKind, "smallint", "ok kind=SmallInt"),
            PgParse("simple-integer", PgSimpleKind, "integer", "ok kind=Int32"),
            PgParse("simple-bigint", PgSimpleKind, "bigint", "ok kind=Int64"),
            PgParse("simple-real", PgSimpleKind, "real", "ok kind=Float32"),
            PgParse("simple-double-precision", PgSimpleKind, "double precision", "ok kind=Float64"),
            PgParse("simple-money", PgSimpleKind, "money", "ok kind=Money"),
            PgParse("simple-date", PgSimpleKind, "date", "ok kind=Date"),
            PgParse("simple-uuid", PgSimpleKind, "uuid", "ok kind=Guid"),
            PgParse("simple-xml", PgSimpleKind, "xml", "ok kind=Xml"),
            PgParse("simple-bytea", PgSimpleKind, "bytea", "ok kind=Binary"),
            // ---- 解析：別名 ----
            PgParse("alias-character-varying", PgAlias, "character varying", "ok kind=String"),
            PgParse("alias-character", PgAlias, "character", "ok kind=FixedString"),
            PgParse("alias-bpchar", PgAlias, "bpchar", "ok kind=FixedString"),
            PgParse("alias-int", PgAlias, "int", "ok kind=Int32"),
            PgParse("alias-int4", PgAlias, "int4", "ok kind=Int32"),
            PgParse("alias-int2", PgAlias, "int2", "ok kind=SmallInt"),
            PgParse("alias-int8", PgAlias, "int8", "ok kind=Int64"),
            PgParse("alias-bool", PgAlias, "bool", "ok kind=Boolean"),
            PgParse("alias-float4", PgAlias, "float4", "ok kind=Float32"),
            PgParse("alias-float8", PgAlias, "float8", "ok kind=Float64"),
            PgParse("alias-decimal", PgAlias, "decimal", "ok kind=Decimal"),
            // ---- 解析：複数語の日付時刻型名 ----
            PgParse(
                "multiword-timestamp-without-time-zone",
                PgMultiWord,
                "timestamp without time zone",
                "ok kind=DateTime"
            ),
            PgParse(
                "multiword-timestamp-with-time-zone",
                PgMultiWord,
                "timestamp with time zone",
                "ok kind=DateTimeOffset"
            ),
            PgParse("multiword-timestamptz", PgMultiWord, "timestamptz", "ok kind=DateTimeOffset"),
            PgParse(
                "multiword-time-without-time-zone",
                PgMultiWord,
                "time without time zone",
                "ok kind=Time"
            ),
            PgParse(
                "multiword-time-with-time-zone",
                PgMultiWord,
                "time with time zone",
                "ok kind=Time"
            ),
            PgParse("multiword-timetz", PgMultiWord, "timetz", "ok kind=Time"),
            // ---- 解析：空白・大文字小文字・長さ・精度 ----
            PgParse(
                "case-and-whitespace-varchar",
                "大文字小文字・空白を許容して解析できる",
                "  VARCHAR( 100 ) ",
                "ok kind=String len=100"
            ),
            PgParse(
                "extra-whitespace-double-precision",
                "複数語型名の空白ゆれ（DOUBLE  PRECISION）を許容する",
                "DOUBLE   PRECISION",
                "ok kind=Float64"
            ),
            PgParse(
                "varchar-length",
                "varchar(n) は String(n) として解析される",
                "varchar(50)",
                "ok kind=String len=50"
            ),
            PgParse(
                "text-max",
                "text は String(-1) として解析される",
                "text",
                "ok kind=String len=-1"
            ),
            PgParse(
                "char-length",
                "char(n) は FixedString(n) として解析される",
                "char(10)",
                "ok kind=FixedString len=10"
            ),
            PgParse(
                "numeric-precision-scale",
                "numeric(p,s) は Decimal(Precision,Scale) として解析される",
                "numeric(10,2)",
                "ok kind=Decimal p=10 s=2"
            ),
            PgParse(
                "numeric-negative-scale",
                PgNegScale,
                "numeric(10,-2)",
                "ok kind=Decimal p=10 s=-2"
            ),
            PgParse(
                "numeric-no-args",
                "numeric（引数省略）は Precision/Scale が null で解析される",
                "numeric",
                "ok kind=Decimal p=- s=-"
            ),
            PgParse(
                "timestamp-precision",
                "timestamp(p) は精度付き DateTime として解析される",
                "timestamp(3)",
                "ok kind=DateTime p=3"
            ),
            PgParse(
                "timestamptz-precision",
                "timestamptz(p) は精度付き DateTimeOffset として解析される",
                "timestamptz(6)",
                "ok kind=DateTimeOffset p=6"
            ),
            PgParse(
                "time-precision",
                "time(p) は精度付き Time として解析される",
                "time(6)",
                "ok kind=Time p=6"
            ),
            PgParse("json", "json は parse-only で Json として解析される", "json", "ok kind=Json"),
            PgParse("jsonb", "jsonb は Json として解析される", "jsonb", "ok kind=Json"),
            // ---- 解析：修飾子が名称の途中へ入る正準表記 ----
            PgParse(
                "modifier-timestamp-without-time-zone",
                PgModifier,
                "timestamp(3) without time zone",
                "ok kind=DateTime p=3"
            ),
            PgParse(
                "modifier-timestamp-with-time-zone",
                PgModifier,
                "timestamp(3) with time zone",
                "ok kind=DateTimeOffset p=3"
            ),
            PgParse(
                "modifier-time-without-time-zone",
                PgModifier,
                "time(3) without time zone",
                "ok kind=Time p=3"
            ),
            PgParse(
                "modifier-time-with-time-zone",
                PgModifier,
                "time(3) with time zone",
                "ok kind=Time p=3"
            ),
            // ---- 解析：変換不能（負の長さ・精度・int 範囲外は読めない引数の境界） ----
            PgParse("unconvertible-serial", PgUnconvertible, "serial", "fail"),
            PgParse("unconvertible-bigserial", PgUnconvertible, "bigserial", "fail"),
            PgParse("unconvertible-smallserial", PgUnconvertible, "smallserial", "fail"),
            PgParse("unconvertible-integer-array", PgUnconvertible, "integer[]", "fail"),
            PgParse("unconvertible-text-array", PgUnconvertible, "text[]", "fail"),
            PgParse("unconvertible-inet", PgUnconvertible, "inet", "fail"),
            PgParse("unconvertible-cidr", PgUnconvertible, "cidr", "fail"),
            PgParse("unconvertible-macaddr", PgUnconvertible, "macaddr", "fail"),
            PgParse("unconvertible-interval", PgUnconvertible, "interval", "fail"),
            PgParse("unconvertible-point", PgUnconvertible, "point", "fail"),
            PgParse("unconvertible-tsvector", PgUnconvertible, "tsvector", "fail"),
            PgParse("unconvertible-no-such-type", PgUnconvertible, "no_such_type", "fail"),
            PgParse("invalid-arg-varchar-negative", PgUnconvertible, "varchar(-5)", "fail")
                .Boundary(DialectTypeBoundaries.InvalidArg),
            PgParse(
                    "invalid-arg-varchar-out-of-int-range",
                    PgUnconvertible,
                    "varchar(99999999999)",
                    "fail"
                )
                .Boundary(DialectTypeBoundaries.InvalidArg),
            PgParse(
                    "invalid-arg-numeric-precision-out-of-int-range",
                    PgUnconvertible,
                    "numeric(99999999999,2)",
                    "fail"
                )
                .Boundary(DialectTypeBoundaries.InvalidArg),
            PgParse(
                    "invalid-arg-numeric-precision-negative",
                    PgNoNegPrecision,
                    "numeric(-10,2)",
                    "fail"
                )
                .Boundary(DialectTypeBoundaries.InvalidArg),
            PgParse("invalid-arg-timestamp-negative", PgNoNegPrecision, "timestamp(-1)", "fail")
                .Boundary(DialectTypeBoundaries.InvalidArg),
            // ---- 書き出し：主要型 ----
            PgFormat("simple-boolean", PgFormatSimple, Ct(CanonicalTypeKind.Boolean), "ok boolean"),
            PgFormat(
                "simple-smallint",
                PgFormatSimple,
                Ct(CanonicalTypeKind.SmallInt),
                "ok smallint"
            ),
            PgFormat("simple-int32", PgFormatSimple, Ct(CanonicalTypeKind.Int32), "ok integer"),
            PgFormat("simple-int64", PgFormatSimple, Ct(CanonicalTypeKind.Int64), "ok bigint"),
            PgFormat("simple-float32", PgFormatSimple, Ct(CanonicalTypeKind.Float32), "ok real"),
            PgFormat(
                "simple-float64",
                PgFormatSimple,
                Ct(CanonicalTypeKind.Float64),
                "ok double precision"
            ),
            PgFormat("simple-money", PgFormatSimple, Ct(CanonicalTypeKind.Money), "ok money"),
            PgFormat("simple-date", PgFormatSimple, Ct(CanonicalTypeKind.Date), "ok date"),
            PgFormat("simple-guid", PgFormatSimple, Ct(CanonicalTypeKind.Guid), "ok uuid"),
            PgFormat("simple-xml", PgFormatSimple, Ct(CanonicalTypeKind.Xml), "ok xml"),
            PgFormat(
                "tinyint-smallint",
                "TryFormat(TinyInt) は smallint を生成する（PG に tinyint は無い）",
                Ct(CanonicalTypeKind.TinyInt),
                "ok smallint"
            ),
            PgFormat(
                "string-length",
                "TryFormat(String(n)) は varchar(n) を生成する",
                Ct(CanonicalTypeKind.String, 100),
                "ok varchar(100)"
            ),
            PgFormat(
                "string-max",
                "TryFormat(String(-1)) は text を生成する",
                Ct(CanonicalTypeKind.String, -1),
                "ok text"
            ),
            PgFormat(
                "ansistring-length",
                "TryFormat(AnsiString(n)) は varchar(n) を生成する",
                Ct(CanonicalTypeKind.AnsiString, 50),
                "ok varchar(50)"
            ),
            PgFormat(
                "ansistring-max",
                "TryFormat(AnsiString(-1)) は text を生成する",
                Ct(CanonicalTypeKind.AnsiString, -1),
                "ok text"
            ),
            PgFormat(
                "fixedstring-length",
                "TryFormat(FixedString(n)) は char(n) を生成する",
                Ct(CanonicalTypeKind.FixedString, 10),
                "ok char(10)"
            ),
            PgFormat(
                "ansifixedstring-length",
                "TryFormat(AnsiFixedString(n)) は char(n) を生成する",
                Ct(CanonicalTypeKind.AnsiFixedString, 5),
                "ok char(5)"
            ),
            PgFormat("binary-50", PgBinaryAny, Ct(CanonicalTypeKind.Binary, 50), "ok bytea"),
            PgFormat("binary-max", PgBinaryAny, Ct(CanonicalTypeKind.Binary, -1), "ok bytea"),
            PgFormat(
                "fixedbinary-16",
                PgBinaryAny,
                Ct(CanonicalTypeKind.FixedBinary, 16),
                "ok bytea"
            ),
            PgFormat(
                "decimal-precision-scale",
                "TryFormat(Decimal) は numeric(p,s) を生成する",
                Ct(CanonicalTypeKind.Decimal, precision: 10, scale: 2),
                "ok numeric(10,2)"
            ),
            PgFormat(
                "datetime-precision",
                "TryFormat(DateTime, Precision指定あり) は timestamp(p) を生成する",
                Ct(CanonicalTypeKind.DateTime, precision: 3),
                "ok timestamp(3)"
            ),
            PgFormat(
                "datetimeoffset-timestamptz",
                "TryFormat(DateTimeOffset) は timestamptz を生成する",
                Ct(CanonicalTypeKind.DateTimeOffset),
                "ok timestamptz"
            ),
            PgFormat(
                "json-jsonb",
                "TryFormat(Json) は jsonb を生成する",
                Ct(CanonicalTypeKind.Json),
                "ok jsonb"
            ),
            // ---- 書き出し：上限の境界（ちょうどと 1 つ超え） ----
            PgFormat(
                "decimal-precision-at-limit",
                PgLimits,
                Ct(CanonicalTypeKind.Decimal, precision: 1000, scale: 0),
                "ok",
                DialectTypeBoundaries.AtLimit
            ),
            PgFormat(
                "decimal-precision-over-limit",
                PgLimits,
                Ct(CanonicalTypeKind.Decimal, precision: 1001, scale: 0),
                "fail",
                DialectTypeBoundaries.OverLimit
            ),
            PgFormat(
                "decimal-scale-at-limit",
                PgLimits,
                Ct(CanonicalTypeKind.Decimal, precision: 10, scale: 1000),
                "ok",
                DialectTypeBoundaries.AtLimit
            ),
            PgFormat(
                "decimal-scale-over-limit",
                PgLimits,
                Ct(CanonicalTypeKind.Decimal, precision: 10, scale: 1001),
                "fail",
                DialectTypeBoundaries.OverLimit
            ),
            PgFormat(
                "decimal-negative-scale-at-limit",
                PgLimits,
                Ct(CanonicalTypeKind.Decimal, precision: 10, scale: -1000),
                "ok",
                DialectTypeBoundaries.AtLimit
            ),
            PgFormat(
                "decimal-negative-scale-over-limit",
                PgLimits,
                Ct(CanonicalTypeKind.Decimal, precision: 10, scale: -1001),
                "fail",
                DialectTypeBoundaries.OverLimit
            ),
            PgFormat(
                "datetime-precision-at-limit",
                PgLimits,
                Ct(CanonicalTypeKind.DateTime, precision: 6),
                "ok",
                DialectTypeBoundaries.AtLimit
            ),
            PgFormat(
                "datetime-precision-over-limit",
                PgLimits,
                Ct(CanonicalTypeKind.DateTime, precision: 7),
                "fail",
                DialectTypeBoundaries.OverLimit
            ),
            PgFormat(
                "datetimeoffset-precision-at-limit",
                PgLimits,
                Ct(CanonicalTypeKind.DateTimeOffset, precision: 6),
                "ok",
                DialectTypeBoundaries.AtLimit
            ),
            PgFormat(
                "datetimeoffset-precision-over-limit",
                PgLimits,
                Ct(CanonicalTypeKind.DateTimeOffset, precision: 7),
                "fail",
                DialectTypeBoundaries.OverLimit
            ),
            PgFormat(
                "time-precision-at-limit",
                PgLimits,
                Ct(CanonicalTypeKind.Time, precision: 6),
                "ok",
                DialectTypeBoundaries.AtLimit
            ),
            PgFormat(
                "time-precision-over-limit",
                PgLimits,
                Ct(CanonicalTypeKind.Time, precision: 7),
                "fail",
                DialectTypeBoundaries.OverLimit
            ),
            PgFormat(
                "string-length-at-limit",
                PgLimits,
                Ct(CanonicalTypeKind.String, 10485760),
                "ok",
                DialectTypeBoundaries.AtLimit
            ),
            PgFormat(
                "string-length-over-limit",
                PgLimits,
                Ct(CanonicalTypeKind.String, 10485761),
                "fail",
                DialectTypeBoundaries.OverLimit
            ),
            PgFormat(
                "ansistring-length-at-limit",
                PgLimits,
                Ct(CanonicalTypeKind.AnsiString, 10485760),
                "ok",
                DialectTypeBoundaries.AtLimit
            ),
            PgFormat(
                "ansistring-length-over-limit",
                PgLimits,
                Ct(CanonicalTypeKind.AnsiString, 10485761),
                "fail",
                DialectTypeBoundaries.OverLimit
            ),
            PgFormat(
                "fixedstring-length-at-limit",
                PgLimits,
                Ct(CanonicalTypeKind.FixedString, 10485760),
                "ok",
                DialectTypeBoundaries.AtLimit
            ),
            PgFormat(
                "fixedstring-length-over-limit",
                PgLimits,
                Ct(CanonicalTypeKind.FixedString, 10485761),
                "fail",
                DialectTypeBoundaries.OverLimit
            ),
            PgFormat(
                "ansifixedstring-length-at-limit",
                PgLimits,
                Ct(CanonicalTypeKind.AnsiFixedString, 10485760),
                "ok",
                DialectTypeBoundaries.AtLimit
            ),
            PgFormat(
                "ansifixedstring-length-over-limit",
                PgLimits,
                Ct(CanonicalTypeKind.AnsiFixedString, 10485761),
                "fail",
                DialectTypeBoundaries.OverLimit
            ),
            PgFormat(
                "string-max-under-limits",
                PgLimits,
                Ct(CanonicalTypeKind.String, -1),
                "ok text"
            ),
            PgFormat(
                "binary-large-under-limits",
                PgLimits,
                Ct(CanonicalTypeKind.Binary, 100000000),
                "ok bytea"
            ),
            // ---- パラメータ型の書き出し：列の上限を当てない ----
            PgFormatParam(
                "decimal-precision-over-limit",
                Ct(CanonicalTypeKind.Decimal, precision: 1001, scale: 0),
                "ok numeric(1001,0)",
                DialectTypeBoundaries.OverLimit
            ),
            PgFormatParam(
                "datetime-precision-over-limit",
                Ct(CanonicalTypeKind.DateTime, precision: 7),
                "ok timestamp(7)",
                DialectTypeBoundaries.OverLimit
            ),
            PgFormatParam(
                "string-length-over-limit",
                Ct(CanonicalTypeKind.String, 10485761),
                "ok varchar(10485761)",
                DialectTypeBoundaries.OverLimit
            ),
            PgFormatParam(
                "decimal-scale-over-limit",
                Ct(CanonicalTypeKind.Decimal, precision: 10, scale: 1001),
                "ok numeric(10,1001)",
                DialectTypeBoundaries.OverLimit
            ),
            PgFormatParam(
                "decimal-negative-scale-over-limit",
                Ct(CanonicalTypeKind.Decimal, precision: 10, scale: -1001),
                "ok numeric(10,-1001)",
                DialectTypeBoundaries.OverLimit
            ),
            PgFormatParam(
                "fixedstring-length-over-limit",
                Ct(CanonicalTypeKind.FixedString, 10485761),
                "ok char(10485761)",
                DialectTypeBoundaries.OverLimit
            ),
            PgFormatParam(
                "datetimeoffset-precision-over-limit",
                Ct(CanonicalTypeKind.DateTimeOffset, precision: 7),
                "ok timestamptz(7)",
                DialectTypeBoundaries.OverLimit
            ),
            PgFormatParam(
                "time-precision-over-limit",
                Ct(CanonicalTypeKind.Time, precision: 7),
                "ok time(7)",
                DialectTypeBoundaries.OverLimit
            ),
            PgFormatParam(
                "decimal-scale-without-precision",
                Ct(CanonicalTypeKind.Decimal, scale: 2),
                "fail"
            ),
            // ---- ラウンドトリップ ----
            PgRoundTripRow("boolean", "boolean"),
            PgRoundTripRow("smallint", "smallint"),
            PgRoundTripRow("integer", "integer"),
            PgRoundTripRow("bigint", "bigint"),
            PgRoundTripRow("numeric-10-2", "numeric(10,2)"),
            PgRoundTripRow("real", "real"),
            PgRoundTripRow("double-precision", "double precision"),
            PgRoundTripRow("money", "money"),
            PgRoundTripRow("varchar-100", "varchar(100)"),
            PgRoundTripRow("text", "text"),
            PgRoundTripRow("char-10", "char(10)"),
            PgRoundTripRow("bytea", "bytea"),
            PgRoundTripRow("date", "date"),
            PgRoundTripRow("time-6", "time(6)"),
            PgRoundTripRow("timestamp-3", "timestamp(3)"),
            PgRoundTripRow("timestamptz-6", "timestamptz(6)"),
            PgRoundTripRow("uuid", "uuid"),
            PgRoundTripRow("xml", "xml"),
            PgRoundTripRow("jsonb", "jsonb"),
            RoundTripText(
                    "roundtrip-postgresql-numeric-negative-scale",
                    PgNegScale,
                    "numeric(10,-2)"
                )
                .Expect(DialectNames.PostgreSql, "same"),
            // ---- 正規化（冪等と同期の能力の結線は自動に照合される） ----
            PgNorm("timestamp", PgNormalize, "timestamp", "timestamp(6)"),
            PgNorm("timestamp-upper", PgNormalize, "TIMESTAMP", "timestamp(6)"),
            PgNorm("timestamptz", PgNormalize, "timestamptz", "timestamptz(6)"),
            PgNorm("time", PgNormalize, "time", "time(6)"),
            PgNorm("timestamp-3", PgNormalize, "timestamp(3)", "timestamp(3)"),
            PgNorm("timestamptz-spaced-0", PgNormalize, "timestamptz ( 0 )", "timestamptz(0)"),
            PgNorm("timestamp-array", PgNormalize, "timestamp[]", "timestamp(6)[]"),
            PgNorm("numeric-18-0", PgNormalize, "numeric(18,0)", "numeric(18)"),
            PgNorm("numeric-18-0-upper", PgNormalize, "NUMERIC(18, 0)", "numeric(18)"),
            PgNorm("numeric-10-2", PgNormalize, "numeric(10,2)", "numeric(10,2)"),
            PgNorm("character-varying-50", PgNormalize, "CHARACTER VARYING(50)", "varchar(50)"),
            PgNorm(
                "time-with-time-zone",
                PgNormalize,
                "time with time zone",
                "time with time zone"
            ),
            PgNorm("integer-padded", PgNormalize, " integer ", "integer"),
            Normalize(
                    "normalize-postgresql-idempotent-timestamp-without-time-zone",
                    PgIdempotent,
                    "timestamp without time zone"
                )
                .Expect(DialectNames.PostgreSql, "ok"),
            Normalize(
                    "normalize-postgresql-idempotent-character-varying-20-array",
                    PgIdempotent,
                    "character varying(20)[]"
                )
                .Expect(DialectNames.PostgreSql, "ok"),
            // ---- 取込の正規化は同じ関数を通す ----
            PgImport("timestamp-without-time-zone", "timestamp without time zone"),
            PgImport("numeric-18-0", "numeric(18,0)"),
            PgImport("character-varying-50", "character varying(50)"),
            // ---- 同期の能力の結線 ----
            PgSame("timestamp-default-precision", "timestamp", "timestamp(6)", "same"),
            PgSame("numeric-zero-scale", "numeric(18,0)", "numeric(18)", "same"),
            PgSame("timestamp-different-precision", "timestamp", "timestamp(3)", "different"),
            // ---- 型マッパー ----
            PgMap(
                "bytea-byte-array",
                "bytea は byte[]（参照型）へマップされる",
                "bytea",
                "ok type=byte[] ref=1"
            ),
            PgMap(
                "bytea-unbounded",
                "bytea は無制限バイナリと判定される",
                "bytea",
                "ok unbounded=1"
            ),
            PgMap("varchar-not-unbounded", PgNotUnbounded, "varchar(100)", "ok unbounded=0"),
            PgMap("integer-not-unbounded", PgNotUnbounded, "integer", "ok unbounded=0"),
            PgMap(
                "timetz-words",
                PgTimetz,
                "time with time zone",
                "ok type=DateTimeOffset ref=0 fallback=0 efcol=\"time with time zone\""
            ),
            PgMap(
                "timetz-short",
                PgTimetz,
                "timetz",
                "ok type=DateTimeOffset ref=0 fallback=0 efcol=\"time with time zone\""
            ),
            PgMap(
                "timetz-upper",
                PgTimetz,
                "TIME WITH TIME ZONE",
                "ok type=DateTimeOffset ref=0 fallback=0 efcol=\"time with time zone\""
            ),
            PgMap(
                "timetz-precision-words",
                PgTimetz,
                "time(3) with time zone",
                "ok type=DateTimeOffset ref=0 fallback=0 efcol=\"time with time zone\""
            ),
            PgMap(
                "timetz-precision-short",
                PgTimetz,
                "timetz(3)",
                "ok type=DateTimeOffset ref=0 fallback=0 efcol=\"time with time zone\""
            ),
            PgMap("time-plain", PgTimeNoTz, "time", "ok type=TimeSpan efcol=\"time\""),
            PgMap("time-precision", PgTimeNoTz, "time(6)", "ok type=TimeSpan efcol=\"time\""),
            PgMap(
                "time-without-time-zone",
                PgTimeNoTz,
                "time without time zone",
                "ok type=TimeSpan efcol=\"time\""
            ),
            PgMap(
                "time-precision-without-time-zone",
                PgTimeNoTz,
                "time(6) without time zone",
                "ok type=TimeSpan efcol=\"time\""
            ),
            PgMap(
                "timestamp-precision-with-time-zone",
                PgTsPrecision,
                "timestamp(6) with time zone",
                "ok type=DateTimeOffset efcol=-"
            ),
            PgMap(
                "timestamptz-precision",
                PgTsPrecision,
                "timestamptz(6)",
                "ok type=DateTimeOffset efcol=-"
            ),
            PgMap(
                "timestamp-precision-without-time-zone",
                PgTsPrecision,
                "timestamp(6) without time zone",
                "ok type=DateTime efcol=\"timestamp without time zone\""
            ),
            PgMap(
                "timestamp-precision-plain",
                PgTsPrecision,
                "timestamp(6)",
                "ok type=DateTime efcol=\"timestamp without time zone\""
            ),
            PgMap("geometry-fallback", PgUnknownMap, "geometry", "ok fallback=1"),
            PgMap("point-fallback", PgUnknownMap, "point", "ok fallback=1"),
            PgMap("integer-resolved", PgKnownMap, "integer", "ok fallback=0"),
            PgMap("varchar-50-resolved", PgKnownMap, "varchar(50)", "ok fallback=0"),
            PgMap("interval", PgDriverType, "interval", "ok type=TimeSpan ref=0 fallback=0"),
            PgMap(
                "interval-day-to-second",
                PgDriverType,
                "interval day to second(3)",
                "ok type=TimeSpan ref=0 fallback=0"
            ),
            PgMap(
                "interval-year-to-month",
                PgDriverType,
                "interval year to month",
                "ok type=TimeSpan ref=0 fallback=0"
            ),
            PgMap(
                "interval-precision",
                PgDriverType,
                "interval(3)",
                "ok type=TimeSpan ref=0 fallback=0"
            ),
            PgMap("cidr", PgDriverType, "cidr", "ok type=System.Net.IPNetwork ref=0 fallback=0"),
            // ---- 生成 EF Core の DbContext が列の型を教える列（宣言のセルに無い表記。セルの表記は matrix-map-* が見る）----
            PgMap(
                "json-efcore-column-type",
                PgEfCoreColumnType,
                "json",
                "ok type=string efcol=json"
            ),
            PgMap(
                "timestamp-plain-efcore-column-type",
                PgEfCoreColumnType,
                "timestamp",
                "ok type=DateTime efcol=\"timestamp without time zone\""
            ),
            PgMap(
                "timestamp-words-efcore-column-type",
                PgEfCoreColumnType,
                "timestamp without time zone",
                "ok type=DateTime efcol=\"timestamp without time zone\""
            ),
            PgMap(
                "text-no-efcore-column-type",
                PgEfCoreColumnType,
                "text",
                "ok type=string efcol=-"
            ),
            // 配列は要素の列の型に [] を付けて引き継ぐ（要素が教えない型なら配列も教えない）
            .. new (string Slug, string Native, string Spec)[]
            {
                ("date", "date[]", "ok type=DateTime[] array=1 efcol=\"date[]\""),
                ("time", "time[]", "ok type=TimeSpan[] array=1 efcol=\"time[]\""),
                (
                    "timestamp-6",
                    "timestamp(6)[]",
                    "ok type=DateTime[] array=1 efcol=\"timestamp without time zone[]\""
                ),
                (
                    "timetz-words",
                    "time with time zone[]",
                    "ok type=DateTimeOffset[] array=1 efcol=\"time with time zone[]\""
                ),
                ("json", "json[]", "ok type=string[] array=1 efcol=\"json[]\""),
                ("jsonb", "jsonb[]", "ok type=string[] array=1 efcol=\"jsonb[]\""),
                ("xml", "xml[]", "ok type=string[] array=1 efcol=\"xml[]\""),
                ("integer", "integer[]", "ok type=int[] array=1 efcol=-"),
                ("text", "text[]", "ok type=string[] array=1 efcol=-"),
                ("timestamptz", "timestamptz[]", "ok type=DateTimeOffset[] array=1 efcol=-"),
            }.Select(item =>
                PgMap(
                    $"array-{item.Slug}-efcore-column-type",
                    PgEfCoreColumnType,
                    item.Native,
                    item.Spec
                )
            ),
            PgMap(
                "timestamptz-no-efcore-column-type",
                PgEfCoreColumnType,
                "timestamptz",
                "ok type=DateTimeOffset efcol=-"
            ),
            // ---- 方言切替：SQL Server → PostgreSQL ----
            PgConvertFromSqlServer("nvarchar-50", "nvarchar(50)", "varchar(50)"),
            PgConvertFromSqlServer("nvarchar-max", "nvarchar(max)", "text"),
            PgConvertFromSqlServer("int", "int", "integer"),
            PgConvertFromSqlServer("uniqueidentifier", "uniqueidentifier", "uuid"),
            PgConvertFromSqlServer("datetime2", "datetime2", "timestamp"),
            PgConvertFromSqlServer("datetimeoffset", "datetimeoffset", "timestamptz"),
            PgConvertFromSqlServer("varbinary-max", "varbinary(max)", "bytea"),
            PgConvertFromSqlServer("bit", "bit", "boolean"),
            Convert(
                    "convert-postgresql-from-sqlserver-hierarchyid-unconverted",
                    "sqlserver hierarchyid は postgresql へ変換できず Unconverted",
                    new ConvertInput(DialectNames.SqlServer, "hierarchyid")
                )
                .Expect(DialectNames.PostgreSql, "unconverted"),
            // ---- 方言切替：PostgreSQL → SQL Server ----
            PgConvertToSqlServer("varchar-50", "varchar(50)", "nvarchar(50)"),
            PgConvertToSqlServer("text", "text", "nvarchar(max)"),
            PgConvertToSqlServer("integer", "integer", "int"),
            PgConvertToSqlServer("uuid", "uuid", "uniqueidentifier"),
            PgConvertToSqlServer("timestamp", "timestamp", "datetime2"),
            PgConvertToSqlServer("timestamptz", "timestamptz", "datetimeoffset"),
            PgConvertToSqlServer("bytea", "bytea", "varbinary(max)"),
            PgConvertToSqlServer("boolean", "boolean", "bit"),
            PgConvertToSqlServer("double-precision", "double precision", "float"),
            PgConvertToSqlServer("jsonb", "jsonb", "nvarchar(max)"),
            Convert(
                    "convert-postgresql-to-sqlserver-serial-unconverted",
                    "postgresql serial は sqlserver へ変換できず Unconverted",
                    new ConvertInput(DialectNames.PostgreSql, "serial")
                )
                .Expect(DialectNames.SqlServer, "unconverted"),
        ];
}
