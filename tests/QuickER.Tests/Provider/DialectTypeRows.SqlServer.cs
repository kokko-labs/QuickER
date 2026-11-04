using System.Globalization;
using QuickER.Dialects;
using QuickER.Provider;
using static QuickER.Tests.Provider.DialectTypeRow;

namespace QuickER.Tests.Provider;

/// <summary>方言の型の表：SQL Server の型カタログ・型マッパー・取込の表記の行</summary>
internal static partial class DialectTypeRows
{
    private static IEnumerable<DialectTypeRow> SqlServerRows() =>
        [
            .. SqlServerParseRows,
            .. SqlServerFormatRows,
            .. SqlServerRoundTripRows,
            .. SqlServerMapRows,
            .. SqlServerNotationRows,
            SameType(
                    "same-type-sqlserver-numeric-default-scale",
                    "SQL Server の capabilities は正規化を配線している",
                    "numeric(18,0)",
                    "numeric(18)"
                )
                .Expect(DialectNames.SqlServer, "same"),
        ];

    /// <summary>SQL Server の行の Id に使うスラッグ（英小文字・数字・ハイフン）</summary>
    private static string SqlServerSlug(string text)
    {
        var chars = text.ToLowerInvariant().Select(c => char.IsAsciiLetterOrDigit(c) ? c : '-');

        var slug = string.Join(
            "-",
            new string(chars.ToArray()).Split('-', StringSplitOptions.RemoveEmptyEntries)
        );

        // 大文字・空白だけが違う入力（TIMESTAMP・FLOAT ( 24 ) など）も別の Id にする
        return slug
            + (text.Any(char.IsUpper) ? "-upper" : string.Empty)
            + (text.Contains(' ', StringComparison.Ordinal) ? "-spaced" : string.Empty);
    }

    private static DialectTypeRow SqlServerParse(
        string id,
        string description,
        string input,
        string spec
    ) => Parse($"parse-sqlserver-{id}", description, input).Expect(DialectNames.SqlServer, spec);

    private static IEnumerable<DialectTypeRow> SqlServerParseRows
    {
        get
        {
            const string simple = "主要型を正規型へ解析できる";
            (string Input, string Kind)[] simples =
            [
                ("bit", "Boolean"),
                ("tinyint", "TinyInt"),
                ("smallint", "SmallInt"),
                ("int", "Int32"),
                ("bigint", "Int64"),
                ("real", "Float32"),
                ("float", "Float64"),
                ("float(1)", "Float32"),
                ("float(24)", "Float32"),
                ("float(25)", "Float64"),
                ("FLOAT ( 53 )", "Float64"),
                ("money", "Money"),
                ("date", "Date"),
                ("uniqueidentifier", "Guid"),
                ("xml", "Xml"),
            ];

            foreach (var (input, kind) in simples)
            {
                yield return SqlServerParse(
                    $"simple-{SqlServerSlug(input)}",
                    simple,
                    input,
                    $"ok kind={kind}"
                );
            }

            yield return SqlServerParse(
                "case-whitespace-nvarchar",
                "大文字小文字・空白を許容して解析できる",
                "  NVARCHAR( 100 ) ",
                "ok kind=String len=100"
            );
            yield return SqlServerParse(
                "nvarchar-length",
                "nvarchar(n) は String(n) として解析される",
                "nvarchar(100)",
                "ok kind=String len=100"
            );
            yield return SqlServerParse(
                "nvarchar-max",
                "nvarchar(max) は String(-1) として解析される",
                "nvarchar(max)",
                "ok kind=String len=-1"
            );
            yield return SqlServerParse(
                "varchar-length",
                "varchar(n) は AnsiString(n) として解析される",
                "varchar(50)",
                "ok kind=AnsiString len=50"
            );
            yield return SqlServerParse(
                "decimal-precision-scale",
                "decimal(p,s) は Decimal(Precision,Scale) として解析される",
                "decimal(10,2)",
                "ok kind=Decimal p=10 s=2"
            );

            foreach (var input in new[] { "decimal", "numeric" })
            {
                yield return SqlServerParse(
                    $"{input}-without-args-default",
                    "decimal / numeric（引数省略）は既定の decimal(18,0) として解析される",
                    input,
                    "ok kind=Decimal len=- p=18 s=0"
                );
            }

            yield return SqlServerParse(
                "numeric-parse-only",
                "numeric(p,s) は parse-only で Decimal として解析される",
                "numeric(18,0)",
                "ok kind=Decimal p=18 s=0"
            );
            yield return SqlServerParse(
                "ntext-parse-only",
                "ntext は parse-only で String(-1) として解析される",
                "ntext",
                "ok kind=String len=-1"
            );
            yield return SqlServerParse(
                "text-parse-only",
                "text は parse-only で AnsiString(-1) として解析される",
                "text",
                "ok kind=AnsiString len=-1"
            );
            yield return SqlServerParse(
                "image-parse-only",
                "image は parse-only で Binary(-1) として解析される",
                "image",
                "ok kind=Binary len=-1"
            );
            yield return SqlServerParse(
                "datetime-parse-only",
                "datetime は parse-only で DateTime として解析される",
                "datetime",
                "ok kind=DateTime"
            );
            yield return SqlServerParse(
                "smalldatetime-parse-only",
                "smalldatetime は parse-only で DateTime として解析される",
                "smalldatetime",
                "ok kind=DateTime"
            );
            yield return SqlServerParse(
                "smallmoney-parse-only",
                "smallmoney は parse-only で Money として解析される",
                "smallmoney",
                "ok kind=Money"
            );
            yield return SqlServerParse(
                "datetime2-precision",
                "datetime2(p) は精度付き DateTime として解析される",
                "datetime2(3)",
                "ok kind=DateTime p=3"
            );
            yield return SqlServerParse(
                "time-precision",
                "time(p) は精度付き Time として解析される",
                "time(7)",
                "ok kind=Time p=7"
            );
            yield return SqlServerParse(
                "datetimeoffset-precision",
                "datetimeoffset(p) は精度付き DateTimeOffset として解析される",
                "datetimeoffset(7)",
                "ok kind=DateTimeOffset p=7"
            );

            foreach (
                var input in new[]
                {
                    "hierarchyid",
                    "geography",
                    "geometry",
                    "sql_variant",
                    "no_such_type",
                }
            )
            {
                yield return SqlServerParse(
                    $"unconvertible-{SqlServerSlug(input)}",
                    "変換不能な型は TryParse が false を返す",
                    input,
                    "fail"
                );
            }

            // 負・int の範囲外の型引数は例外ではなく false
            foreach (
                var input in new[]
                {
                    "nvarchar(-5)",
                    "nvarchar(99999999999)",
                    "decimal(99999999999,2)",
                }
            )
            {
                yield return SqlServerParse(
                        $"unconvertible-{SqlServerSlug(input)}",
                        "変換不能な型は TryParse が false を返す",
                        input,
                        "fail"
                    )
                    .Boundary(DialectTypeBoundaries.InvalidArg);
            }

            foreach (var input in new[] { "rowversion", "timestamp", "TIMESTAMP" })
            {
                yield return SqlServerParse(
                    $"rowversion-kind-{SqlServerSlug(input)}",
                    "rowversion / timestamp は行バージョンの正規型として解析される",
                    input,
                    "ok kind=RowVersion"
                );
            }
        }
    }

    private static IEnumerable<DialectTypeRow> SqlServerFormatRows
    {
        get
        {
            const string simple = "TryFormat が主要な正規型からネイティブ型文字列を生成する";
            (CanonicalTypeKind Kind, string Native)[] simples =
            [
                (CanonicalTypeKind.Boolean, "bit"),
                (CanonicalTypeKind.TinyInt, "tinyint"),
                (CanonicalTypeKind.SmallInt, "smallint"),
                (CanonicalTypeKind.Int32, "int"),
                (CanonicalTypeKind.Int64, "bigint"),
                (CanonicalTypeKind.Float32, "real"),
                (CanonicalTypeKind.Float64, "float"),
                (CanonicalTypeKind.Money, "money"),
                (CanonicalTypeKind.Date, "date"),
                (CanonicalTypeKind.Guid, "uniqueidentifier"),
                (CanonicalTypeKind.Xml, "xml"),
            ];

            foreach (var (kind, native) in simples)
            {
                yield return Format(
                        $"format-sqlserver-simple-{SqlServerSlug(kind.ToString())}",
                        simple,
                        Ct(kind)
                    )
                    .Expect(DialectNames.SqlServer, $"ok {native}");
            }

            yield return Format(
                    "format-sqlserver-string-length",
                    "TryFormat(String(n)) は nvarchar(n) を生成する",
                    Ct(CanonicalTypeKind.String, 100)
                )
                .Expect(DialectNames.SqlServer, "ok nvarchar(100)");
            yield return Format(
                    "format-sqlserver-string-max",
                    "TryFormat(String(-1)) は nvarchar(max) を生成する",
                    Ct(CanonicalTypeKind.String, -1)
                )
                .Expect(DialectNames.SqlServer, "ok nvarchar(max)");
            yield return Format(
                    "format-sqlserver-decimal-precision-scale",
                    "TryFormat(Decimal) は decimal(p,s) を生成する",
                    Ct(CanonicalTypeKind.Decimal, precision: 10, scale: 2)
                )
                .Expect(DialectNames.SqlServer, "ok decimal(10,2)");
            yield return Format(
                    "format-sqlserver-datetime-precision",
                    "TryFormat(DateTime, Precision指定あり) は datetime2(p) を生成する",
                    Ct(CanonicalTypeKind.DateTime, precision: 3)
                )
                .Expect(DialectNames.SqlServer, "ok datetime2(3)");
            yield return Format(
                    "format-sqlserver-datetime-without-precision",
                    "TryFormat(DateTime, Precisionなし) は datetime2 を生成する",
                    Ct(CanonicalTypeKind.DateTime)
                )
                .Expect(DialectNames.SqlServer, "ok datetime2");
            yield return Format(
                    "format-sqlserver-json-nvarchar-max",
                    "TryFormat(Json) は nvarchar(max) を生成する（json 型が候補に無いため）",
                    Ct(CanonicalTypeKind.Json)
                )
                .Expect(DialectNames.SqlServer, "ok nvarchar(max)");

            foreach (var row in SqlServerLimitRows)
            {
                yield return row;
            }
        }
    }

    /// <summary>書き出しの上限の行（元の Theory の行に、境界の対のもう一方を足す）</summary>
    private static IEnumerable<DialectTypeRow> SqlServerLimitRows
    {
        get
        {
            const string limits =
                "TryFormat は SQL Server の上限を超える値と精度の無い Decimal を書き出さない";
            const string over = DialectTypeBoundaries.OverLimit;
            const string at = DialectTypeBoundaries.AtLimit;
            const string none = DialectTypeBoundaries.None;

            DialectTypeRow Limit(string id, CanonicalType type, string spec, string boundary = none)
            {
                var row = Format($"format-sqlserver-limit-{id}", limits, type);

                return (boundary == none ? row : row.Boundary(boundary)).Expect(
                    DialectNames.SqlServer,
                    spec
                );
            }

            // nvarchar / nchar（4000）
            yield return Limit("string-over", Ct(CanonicalTypeKind.String, 4001), "fail", over);
            yield return Limit("string-at", Ct(CanonicalTypeKind.String, 4000), "ok", at);
            yield return Limit("string-zero", Ct(CanonicalTypeKind.String, 0), "fail");
            yield return Limit(
                "fixedstring-over",
                Ct(CanonicalTypeKind.FixedString, 4001),
                "fail",
                over
            );
            yield return Limit("fixedstring-at", Ct(CanonicalTypeKind.FixedString, 4000), "ok", at);

            // varchar / char（8000）
            yield return Limit(
                "ansistring-over",
                Ct(CanonicalTypeKind.AnsiString, 8001),
                "fail",
                over
            );
            yield return Limit("ansistring-at", Ct(CanonicalTypeKind.AnsiString, 8000), "ok", at);
            yield return Limit(
                "ansifixedstring-far-over",
                Ct(CanonicalTypeKind.AnsiFixedString, 9000),
                "fail"
            );
            yield return Limit(
                "ansifixedstring-over",
                Ct(CanonicalTypeKind.AnsiFixedString, 8001),
                "fail",
                over
            );
            yield return Limit(
                "ansifixedstring-at",
                Ct(CanonicalTypeKind.AnsiFixedString, 8000),
                "ok",
                at
            );

            // varbinary / binary（8000）
            yield return Limit("binary-over", Ct(CanonicalTypeKind.Binary, 8001), "fail", over);
            yield return Limit("binary-at", Ct(CanonicalTypeKind.Binary, 8000), "ok", at);
            yield return Limit(
                "fixedbinary-over",
                Ct(CanonicalTypeKind.FixedBinary, 8001),
                "fail",
                over
            );
            yield return Limit("fixedbinary-at", Ct(CanonicalTypeKind.FixedBinary, 8000), "ok", at);

            // decimal（精度 38・スケールは精度以下・精度なしは書き出さない）
            yield return Limit(
                "decimal-far-over",
                Ct(CanonicalTypeKind.Decimal, precision: 50, scale: 2),
                "fail"
            );
            yield return Limit(
                "decimal-precision-over",
                Ct(CanonicalTypeKind.Decimal, precision: 39, scale: 2),
                "fail",
                over
            );
            yield return Limit(
                "decimal-precision-at",
                Ct(CanonicalTypeKind.Decimal, precision: 38, scale: 2),
                "ok",
                at
            );
            yield return Limit(
                "decimal-scale-over-precision",
                Ct(CanonicalTypeKind.Decimal, precision: 10, scale: 11),
                "fail"
            );
            yield return Limit(
                "decimal-precisionless",
                Ct(CanonicalTypeKind.Decimal),
                "fail",
                DialectTypeBoundaries.Precisionless
            );
            yield return Limit(
                "decimal-precisionless-scale",
                Ct(CanonicalTypeKind.Decimal, scale: 2),
                "fail",
                DialectTypeBoundaries.Precisionless
            );

            // 小数秒（7）
            yield return Limit(
                "datetime-far-over",
                Ct(CanonicalTypeKind.DateTime, precision: 9),
                "fail"
            );
            yield return Limit(
                "datetime-fraction-over",
                Ct(CanonicalTypeKind.DateTime, precision: 8),
                "fail",
                over
            );
            yield return Limit(
                "datetime-fraction-at",
                Ct(CanonicalTypeKind.DateTime, precision: 7),
                "ok",
                at
            );
            yield return Limit(
                "time-fraction-over",
                Ct(CanonicalTypeKind.Time, precision: 8),
                "fail",
                over
            );
            yield return Limit(
                "time-fraction-at",
                Ct(CanonicalTypeKind.Time, precision: 7),
                "ok",
                at
            );
            yield return Limit(
                "datetimeoffset-fraction-over",
                Ct(CanonicalTypeKind.DateTimeOffset, precision: 8),
                "fail",
                over
            );
            yield return Limit(
                "datetimeoffset-fraction-at",
                Ct(CanonicalTypeKind.DateTimeOffset, precision: 7),
                "ok",
                at
            );
        }
    }

    private static IEnumerable<DialectTypeRow> SqlServerRoundTripRows
    {
        get
        {
            string[] inputs =
            [
                "bit",
                "tinyint",
                "smallint",
                "int",
                "bigint",
                "decimal(10,2)",
                "real",
                "float",
                "money",
                "nvarchar(100)",
                "nvarchar(max)",
                "varchar(50)",
                "varchar(max)",
                "nchar(10)",
                "char(10)",
                "varbinary(50)",
                "varbinary(max)",
                "binary(50)",
                "date",
                "time(7)",
                "datetime2(3)",
                "datetimeoffset(7)",
                "uniqueidentifier",
                "xml",
            ];

            foreach (var input in inputs)
            {
                yield return RoundTripText(
                        $"roundtrip-sqlserver-{SqlServerSlug(input)}",
                        "パース → フォーマットのラウンドトリップで型が保持される",
                        input
                    )
                    .Expect(DialectNames.SqlServer, "same");
            }

            // timestamp は非推奨の別名のため、書き出しは代表表記 rowversion の 1 つに寄せる
            foreach (var input in new[] { "rowversion", "timestamp", "TIMESTAMP" })
            {
                yield return RoundTripText(
                        $"roundtrip-sqlserver-rowversion-{SqlServerSlug(input)}",
                        "rowversion / timestamp は行バージョンの正規型として解析される",
                        input
                    )
                    .Expect(DialectNames.SqlServer, "ok rowversion");
            }
        }
    }

    private static DialectTypeRow SqlServerMap(
        string id,
        string description,
        string input,
        string spec
    ) => Map($"map-sqlserver-{id}", description, input).Expect(DialectNames.SqlServer, spec);

    private static IEnumerable<DialectTypeRow> SqlServerMapRows
    {
        get
        {
            (string Input, string SqlDb)[] sqlDbTypes =
            [
                ("char(10)", "Char"),
                ("varchar(50)", "VarChar"),
                ("nchar(5)", "NChar"),
                ("nvarchar(100)", "NVarChar"),
                ("nvarchar(max)", "NVarChar"),
                ("text", "Text"),
                ("ntext", "NText"),
                ("xml", "Xml"),
                ("decimal(10,2)", "Decimal"),
                ("numeric(18,4)", "Decimal"),
                ("money", "Money"),
                ("smallmoney", "SmallMoney"),
                ("bit", "Bit"),
                ("tinyint", "TinyInt"),
                ("smallint", "SmallInt"),
                ("int", "Int"),
                ("bigint", "BigInt"),
                ("float", "Float"),
                ("real", "Real"),
                ("float(24)", "Real"),
                ("float(25)", "Float"),
                ("date", "Date"),
                ("time", "Time"),
                ("datetime", "DateTime"),
                ("datetime2", "DateTime2"),
                ("smalldatetime", "SmallDateTime"),
                ("datetimeoffset", "DateTimeOffset"),
                ("uniqueidentifier", "UniqueIdentifier"),
                ("binary(8)", "Binary"),
                ("varbinary(max)", "VarBinary"),
                ("image", "Image"),
                ("rowversion", "Timestamp"),
                ("timestamp", "Timestamp"),
            ];

            foreach (var (input, sqlDb) in sqlDbTypes)
            {
                yield return SqlServerMap(
                    $"sqldb-{SqlServerSlug(input)}",
                    "SQL Server 型を SqlDbType 列挙名へ解決できる",
                    input,
                    $"ok sqldb={sqlDb}"
                );
            }

            (string Input, string Type)[] floats =
            [
                ("float(1)", "float"),
                ("float(24)", "float"),
                ("FLOAT ( 24 )", "float"),
                ("float(25)", "double"),
                ("float(53)", "double"),
                ("float", "double"),
                ("real", "float"),
            ];

            foreach (var (input, type) in floats)
            {
                yield return SqlServerMap(
                    $"float-mantissa-{SqlServerSlug(input)}",
                    "float(n) は n が 24 以下なら float、それより大きいか素の float なら double になる",
                    input,
                    $"ok type={type}"
                );
            }

            yield return SqlServerMap(
                "unknown-sqldb-null",
                "未知の型は SqlDbTypeName が null になる",
                "geography",
                "ok sqldb=-"
            );

            foreach (var input in new[] { "geometry", "hierarchyid", "sql_variant" })
            {
                yield return SqlServerMap(
                    $"unknown-fallback-{SqlServerSlug(input)}",
                    "未知の型は IsFallbackType が true になる",
                    input,
                    "ok fallback=1"
                );
            }

            foreach (var input in new[] { "int", "nvarchar(50)" })
            {
                yield return SqlServerMap(
                    $"known-not-fallback-{SqlServerSlug(input)}",
                    "解決できる型は IsFallbackType が false になる",
                    input,
                    "ok fallback=0"
                );
            }

            (string Input, int Length)[] declared =
            [
                ("varchar(50)", 50),
                ("nvarchar(max)", -1),
                ("varbinary(max)", -1),
                ("binary(16)", 16),
                ("text", 0),
            ];

            foreach (var (input, length) in declared)
            {
                yield return SqlServerMap(
                    $"declared-length-{SqlServerSlug(input)}",
                    "宣言長は n / max=-1 / 無指定=0 の三値で解決される",
                    input,
                    $"ok declen={length.ToString(CultureInfo.InvariantCulture)}"
                );
            }

            foreach (var input in new[] { "int", "decimal(10,2)" })
            {
                yield return SqlServerMap(
                    $"non-string-declared-length-zero-{SqlServerSlug(input)}",
                    "値型・非文字列は宣言長 0 になる",
                    input,
                    "ok declen=0"
                );
            }

            yield return SqlServerMap(
                "case-whitespace-nvarchar",
                "大文字小文字・前後空白を許容して SqlDbTypeName を解決できる",
                "  NVARCHAR(100) ",
                "ok sqldb=NVarChar declen=100"
            );

            (string Input, bool Unbounded)[] unbounded =
            [
                ("varbinary(max)", true),
                ("image", true),
                ("varbinary(100)", false),
                ("binary(16)", false),
                ("rowversion", false),
                ("timestamp", false),
            ];

            foreach (var (input, flag) in unbounded)
            {
                yield return SqlServerMap(
                    $"unbounded-binary-{SqlServerSlug(input)}",
                    "無制限バイナリ（varbinary(max) / image）だけ IsUnboundedBinary=true になる",
                    input,
                    $"ok unbounded={(flag ? 1 : 0)}"
                );
            }

            foreach (var input in new[] { "nvarchar(max)", "int" })
            {
                yield return SqlServerMap(
                    $"non-binary-not-unbounded-{SqlServerSlug(input)}",
                    "非バイナリ型は IsUnboundedBinary=false になる",
                    input,
                    "ok unbounded=0"
                );
            }
        }
    }

    private static IEnumerable<DialectTypeRow> SqlServerNotationRows
    {
        get
        {
            (string Input, string Expected)[] cases =
            [
                ("datetime2(7)", "datetime2"),
                ("DATETIME2 ( 7 )", "datetime2"),
                ("time(7)", "time"),
                ("datetimeoffset(7)", "datetimeoffset"),
                ("datetime2(3)", "datetime2(3)"),
                ("time(0)", "time(0)"),
                ("decimal", "decimal(18)"),
                ("numeric", "numeric(18)"),
                ("numeric(18,0)", "numeric(18)"),
                ("decimal(10, 0)", "decimal(10)"),
                ("decimal(10)", "decimal(10)"),
                ("decimal(10,2)", "decimal(10,2)"),
                ("rowversion", "timestamp"),
                ("timestamp", "timestamp"),
                ("float(1)", "real"),
                ("FLOAT ( 24 )", "real"),
                ("float(25)", "float"),
                ("float(53)", "float"),
                ("float", "float"),
                ("float(54)", "float(54)"),
                ("datetime2", "datetime2"),
                (" nvarchar(50) ", "nvarchar(50)"),
                ("int", "int"),
                ("", ""),
            ];

            foreach (var (input, expected) in cases)
            {
                yield return Normalize(
                        $"normalize-sqlserver-cases-{(input.Length == 0 ? "empty" : SqlServerSlug(input))}",
                        "Normalize: 取込が返す表記へ寄せる",
                        input
                    )
                    .Expect(
                        DialectNames.SqlServer,
                        expected.Length == 0 ? "ok text=\"\"" : $"ok {expected}"
                    );
            }

            // 冪等は全行の自動の照合が見る。ここは入力だけを持つ行（状態だけ）
            foreach (
                var input in new[]
                {
                    "numeric(18,0)",
                    "decimal",
                    "datetime2(7)",
                    "datetimeoffset(3)",
                    "rowversion",
                    "geography",
                }
            )
            {
                yield return Normalize(
                        $"normalize-sqlserver-idempotent-{SqlServerSlug(input)}",
                        "Normalize: 冪等",
                        input
                    )
                    .Expect(DialectNames.SqlServer, "ok");
            }

            (ImportedType Type, string Slug)[] imported =
            [
                (
                    new ImportedType(
                        "decimal",
                        Length: null,
                        Precision: 18,
                        Scale: 0,
                        DateTimePrecision: null
                    ),
                    "decimal-18-0"
                ),
                (
                    new ImportedType(
                        "numeric",
                        Length: null,
                        Precision: 10,
                        Scale: 2,
                        DateTimePrecision: null
                    ),
                    "numeric-10-2"
                ),
                (
                    new ImportedType(
                        "datetime2",
                        Length: null,
                        Precision: null,
                        Scale: null,
                        DateTimePrecision: 3
                    ),
                    "datetime2-3"
                ),
                (
                    new ImportedType(
                        "datetime2",
                        Length: null,
                        Precision: null,
                        Scale: null,
                        DateTimePrecision: 7
                    ),
                    "datetime2-7"
                ),
                (
                    new ImportedType(
                        "time",
                        Length: null,
                        Precision: null,
                        Scale: null,
                        DateTimePrecision: 0
                    ),
                    "time-0"
                ),
                (
                    new ImportedType(
                        "nvarchar",
                        Length: -1,
                        Precision: null,
                        Scale: null,
                        DateTimePrecision: null
                    ),
                    "nvarchar-max"
                ),
                (
                    new ImportedType(
                        "timestamp",
                        Length: null,
                        Precision: null,
                        Scale: null,
                        DateTimePrecision: null
                    ),
                    "timestamp"
                ),
            ];

            foreach (var (type, slug) in imported)
            {
                yield return ImportSpelling(
                        $"import-spelling-sqlserver-fixed-point-{slug}",
                        "Normalize: 取込の出力は不動点",
                        type
                    )
                    .Expect(DialectNames.SqlServer, "ok fixed=1");
            }
        }
    }
}
