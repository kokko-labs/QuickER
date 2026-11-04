using QuickER.Dialects;
using QuickER.Provider;
using static QuickER.Tests.Provider.DialectTypeRow;

namespace QuickER.Tests.Provider;

internal static partial class DialectTypeRows
{
    private static IEnumerable<DialectTypeRow> SqliteRows() =>
        [
            .. SqliteCatalogRows,
            .. SqliteParseRows,
            .. SqliteFormatRows,
            .. SqliteMapRows,
            .. SqliteConvertRows,
        ];

    private static IEnumerable<DialectTypeRow> SqliteCatalogRows =>
        [
            Catalog("catalog-sqlite-default-int", "DefaultDataType は INT")
                .Expect(DialectNames.Sqlite, "ok default=INT"),
        ];

    /// <summary>解析の行（主要型・別名・複数語の別名・引数つき・解析できない表記）</summary>
    private static IEnumerable<DialectTypeRow> SqliteParseRows
    {
        get
        {
            var rows = new List<DialectTypeRow>();

            (string Slug, string Native, string Kind)[] simple =
            [
                ("bit", "BIT", "Boolean"),
                ("tinyint", "TINYINT", "TinyInt"),
                ("smallint", "SMALLINT", "SmallInt"),
                ("int", "INT", "Int32"),
                ("bigint", "BIGINT", "Int64"),
                ("real", "REAL", "Float64"),
                ("float", "FLOAT", "Float64"),
                ("money", "MONEY", "Money"),
                ("date", "DATE", "Date"),
                ("uniqueidentifier", "UNIQUEIDENTIFIER", "Guid"),
                ("xml", "XML", "Xml"),
                ("json", "JSON", "Json"),
            ];

            foreach (var (slug, native, kind) in simple)
            {
                rows.Add(
                    Parse($"parse-sqlite-simple-{slug}", "主要型を正規型へ解析できる", native)
                        .Expect(DialectNames.Sqlite, $"ok kind={kind}")
                );
            }

            (string Slug, string Native, string Kind)[] aliases =
            [
                ("integer", "INTEGER", "Int64"),
                ("boolean", "BOOLEAN", "Boolean"),
                ("numeric", "NUMERIC", "Decimal"),
                ("double", "DOUBLE", "Float64"),
                ("text", "TEXT", "String"),
                ("blob", "BLOB", "Binary"),
                ("datetime", "DATETIME", "DateTime"),
                ("timestamp", "TIMESTAMP", "DateTime"),
            ];

            foreach (var (slug, native, kind) in aliases)
            {
                rows.Add(
                    Parse(
                            $"parse-sqlite-alias-{slug}",
                            "別名・SQLite 親和性キーワードを正規化して解析できる",
                            native
                        )
                        .Expect(DialectNames.Sqlite, $"ok kind={kind}")
                );
            }

            (string Slug, string Native, string Kind, string Length)[] multiWord =
            [
                ("double-precision", "double precision", "Float64", "-"),
                ("double-precision-two-spaces", "DOUBLE  PRECISION", "Float64", "-"),
                ("character-varying", "character varying(20)", "AnsiString", "20"),
                ("character-varying-two-spaces", "character  varying(20)", "AnsiString", "20"),
                ("character-varying-tab", "character\tvarying(20)", "AnsiString", "20"),
                ("character-space-paren", "CHARACTER (5)", "AnsiFixedString", "5"),
            ];

            foreach (var (slug, native, kind, length) in multiWord)
            {
                rows.Add(
                    Parse(
                            $"parse-sqlite-multiword-{slug}",
                            "複数語の別名も解析でき、型マッパーと同じ型になる",
                            native
                        )
                        .Expect(DialectNames.Sqlite, $"ok kind={kind} len={length}")
                );
            }

            rows.Add(
                Parse(
                        "parse-sqlite-case-whitespace-nvarchar",
                        "大文字小文字・空白を許容して解析できる",
                        "  nvarchar( 100 ) "
                    )
                    .Expect(DialectNames.Sqlite, "ok kind=String len=100")
            );

            (string Id, string Description, string Native, string Spec)[] withArgs =
            [
                (
                    "nvarchar-length",
                    "NVARCHAR(n) は String(n) として解析される",
                    "NVARCHAR(50)",
                    "ok kind=String len=50"
                ),
                (
                    "nvarchar-max",
                    "NVARCHAR(MAX) は String(-1) として解析される",
                    "NVARCHAR(MAX)",
                    "ok kind=String len=-1"
                ),
                (
                    "varchar-length",
                    "VARCHAR(n) は AnsiString(n) として解析される",
                    "VARCHAR(100)",
                    "ok kind=AnsiString len=100"
                ),
                (
                    "nchar-length",
                    "NCHAR(n) は FixedString(n) として解析される",
                    "NCHAR(10)",
                    "ok kind=FixedString len=10"
                ),
                (
                    "char-length",
                    "CHAR(n) は AnsiFixedString(n) として解析される",
                    "CHAR(5)",
                    "ok kind=AnsiFixedString len=5"
                ),
                (
                    "varbinary-length",
                    "VARBINARY(n) は Binary(n) として解析される",
                    "VARBINARY(50)",
                    "ok kind=Binary len=50"
                ),
                (
                    "binary-length",
                    "BINARY(n) は FixedBinary(n) として解析される",
                    "BINARY(16)",
                    "ok kind=FixedBinary len=16"
                ),
                (
                    "decimal-precision-scale",
                    "DECIMAL(p,s) は Decimal(Precision,Scale) として解析される",
                    "DECIMAL(18,2)",
                    "ok kind=Decimal p=18 s=2"
                ),
                (
                    "datetime2-precision",
                    "DATETIME2(p) は精度付き DateTime として解析される",
                    "DATETIME2(3)",
                    "ok kind=DateTime p=3"
                ),
                (
                    "time-precision",
                    "TIME(p) は精度付き Time として解析される",
                    "TIME(6)",
                    "ok kind=Time p=6"
                ),
                (
                    "datetimeoffset-precision",
                    "DATETIMEOFFSET(p) は精度付き DateTimeOffset として解析される",
                    "DATETIMEOFFSET(7)",
                    "ok kind=DateTimeOffset p=7"
                ),
            ];

            foreach (var (id, description, native, spec) in withArgs)
            {
                rows.Add(
                    Parse($"parse-sqlite-{id}", description, native)
                        .Expect(DialectNames.Sqlite, spec)
                );
            }

            // 解析できない表記（負・int の範囲外の引数は例外でなく false）
            const string failDescription = "変換不能な型は TryParse が false を返す";
            rows.Add(
                Parse("parse-sqlite-fail-unknown-name", failDescription, "no_such_type")
                    .Expect(DialectNames.Sqlite, "fail")
            );
            rows.Add(
                Parse("parse-sqlite-fail-negative-length", failDescription, "NVARCHAR(-5)")
                    .Boundary(DialectTypeBoundaries.InvalidArg)
                    .Expect(DialectNames.Sqlite, "fail")
            );
            rows.Add(
                Parse(
                        "parse-sqlite-fail-length-out-of-int-range",
                        failDescription,
                        "NVARCHAR(99999999999)"
                    )
                    .Boundary(DialectTypeBoundaries.InvalidArg)
                    .Expect(DialectNames.Sqlite, "fail")
            );
            rows.Add(
                Parse(
                        "parse-sqlite-fail-precision-out-of-int-range",
                        failDescription,
                        "DECIMAL(99999999999,2)"
                    )
                    .Boundary(DialectTypeBoundaries.InvalidArg)
                    .Expect(DialectNames.Sqlite, "fail")
            );
            rows.Add(
                Parse("parse-sqlite-fail-array-notation", failDescription, "INT[]")
                    .Expect(DialectNames.Sqlite, "fail")
            );
            rows.Add(
                Parse("parse-sqlite-fail-unknown-multiword", failDescription, "unsigned big int")
                    .Expect(DialectNames.Sqlite, "fail")
            );

            // REAL アフィニティの宣言型はすべて倍精度（Float64）として読み、FLOAT と書き戻す
            foreach (var native in new[] { "REAL", "FLOAT", "DOUBLE", "FLOAT8" })
            {
                var slug = native.ToLowerInvariant();
                rows.Add(
                    Parse(
                            $"parse-sqlite-real-affinity-{slug}",
                            "REAL アフィニティの宣言型は Float64 として読む",
                            native
                        )
                        .Expect(DialectNames.Sqlite, "ok kind=Float64 len=- p=- s=-")
                );
                rows.Add(
                    RoundTripText(
                            $"roundtrip-sqlite-real-affinity-{slug}-to-float",
                            "REAL アフィニティの宣言型は Float64 として読む",
                            native
                        )
                        .Expect(DialectNames.Sqlite, "ok FLOAT")
                );
            }

            return rows;
        }
    }

    /// <summary>書き出しと往復の行</summary>
    private static IEnumerable<DialectTypeRow> SqliteFormatRows
    {
        get
        {
            var rows = new List<DialectTypeRow>();

            (string Slug, CanonicalTypeKind Kind, string Native)[] simple =
            [
                ("boolean", CanonicalTypeKind.Boolean, "BIT"),
                ("tinyint", CanonicalTypeKind.TinyInt, "TINYINT"),
                ("smallint", CanonicalTypeKind.SmallInt, "SMALLINT"),
                ("int32", CanonicalTypeKind.Int32, "INT"),
                ("int64", CanonicalTypeKind.Int64, "BIGINT"),
                ("float32", CanonicalTypeKind.Float32, "REAL"),
                ("float64", CanonicalTypeKind.Float64, "FLOAT"),
                ("money", CanonicalTypeKind.Money, "MONEY"),
                ("date", CanonicalTypeKind.Date, "DATE"),
                ("guid", CanonicalTypeKind.Guid, "UNIQUEIDENTIFIER"),
                ("xml", CanonicalTypeKind.Xml, "XML"),
                ("json", CanonicalTypeKind.Json, "JSON"),
                ("datetimeoffset", CanonicalTypeKind.DateTimeOffset, "DATETIMEOFFSET"),
            ];

            foreach (var (slug, kind, native) in simple)
            {
                rows.Add(
                    Format(
                            $"format-sqlite-simple-{slug}",
                            "TryFormat が主要な正規型からネイティブ型文字列を生成する",
                            Ct(kind)
                        )
                        .Expect(DialectNames.Sqlite, $"ok {native}")
                );
            }

            rows.Add(
                Format(
                        "format-sqlite-string-length",
                        "TryFormat(String(n)) は NVARCHAR(n) を生成する",
                        Ct(CanonicalTypeKind.String, 100)
                    )
                    .Expect(DialectNames.Sqlite, "ok NVARCHAR(100)")
            );
            rows.Add(
                Format(
                        "format-sqlite-string-max",
                        "TryFormat(String(-1)) は NVARCHAR(MAX) を生成する",
                        Ct(CanonicalTypeKind.String, -1)
                    )
                    .Expect(DialectNames.Sqlite, "ok NVARCHAR(MAX)")
            );
            rows.Add(
                Format(
                        "format-sqlite-ansistring-length",
                        "TryFormat(AnsiString(n)) は VARCHAR(n) を生成する",
                        Ct(CanonicalTypeKind.AnsiString, 50)
                    )
                    .Expect(DialectNames.Sqlite, "ok VARCHAR(50)")
            );
            rows.Add(
                Format(
                        "format-sqlite-binary-length",
                        "TryFormat(Binary(n)) は VARBINARY(n) を生成する",
                        Ct(CanonicalTypeKind.Binary, 50)
                    )
                    .Expect(DialectNames.Sqlite, "ok VARBINARY(50)")
            );
            rows.Add(
                Format(
                        "format-sqlite-fixedbinary-length",
                        "TryFormat(FixedBinary(n)) は BINARY(n) を生成する",
                        Ct(CanonicalTypeKind.FixedBinary, 16)
                    )
                    .Expect(DialectNames.Sqlite, "ok BINARY(16)")
            );
            rows.Add(
                Format(
                        "format-sqlite-decimal-precision-scale",
                        "TryFormat(Decimal) は DECIMAL(p,s) を生成する",
                        Ct(CanonicalTypeKind.Decimal, precision: 18, scale: 2)
                    )
                    .Expect(DialectNames.Sqlite, "ok DECIMAL(18,2)")
            );
            rows.Add(
                Format(
                        "format-sqlite-datetime-precision",
                        "TryFormat(DateTime, Precision指定あり) は DATETIME2(p) を生成する",
                        Ct(CanonicalTypeKind.DateTime, precision: 3)
                    )
                    .Expect(DialectNames.Sqlite, "ok DATETIME2(3)")
            );

            string[] roundTrips =
            [
                "BIT",
                "TINYINT",
                "SMALLINT",
                "INT",
                "BIGINT",
                "DECIMAL(18,2)",
                "FLOAT",
                "MONEY",
                "NVARCHAR(100)",
                "NVARCHAR(MAX)",
                "VARCHAR(50)",
                "NCHAR(10)",
                "CHAR(5)",
                "VARBINARY(50)",
                "BINARY(16)",
                "DATE",
                "TIME(6)",
                "DATETIME2(3)",
                "DATETIMEOFFSET(7)",
                "UNIQUEIDENTIFIER",
                "XML",
                "JSON",
            ];

            foreach (var native in roundTrips)
            {
                rows.Add(
                    RoundTripText(
                            $"roundtrip-sqlite-{SqliteSlug(native)}",
                            "パース → フォーマットのラウンドトリップで全 CanonicalTypeKind の代表宣言型が保持される",
                            native
                        )
                        .Expect(DialectNames.Sqlite, "same")
                );
            }

            return rows;
        }
    }

    /// <summary>型マッパーの行</summary>
    private static IEnumerable<DialectTypeRow> SqliteMapRows
    {
        get
        {
            var rows = new List<DialectTypeRow>();

            // 複数語の別名は型マッパーでも同じ型になる
            (string Slug, string Native, string Clr)[] multiWord =
            [
                ("double-precision", "double precision", "double"),
                ("double-precision-two-spaces", "DOUBLE  PRECISION", "double"),
                ("character-varying", "character varying(20)", "string"),
                ("character-varying-two-spaces", "character  varying(20)", "string"),
                ("character-varying-tab", "character\tvarying(20)", "string"),
                ("character-space-paren", "CHARACTER (5)", "string"),
            ];

            foreach (var (slug, native, clr) in multiWord)
            {
                rows.Add(
                    Map(
                            $"map-sqlite-multiword-{slug}",
                            "複数語の別名も解析でき、型マッパーと同じ型になる",
                            native
                        )
                        .Expect(DialectNames.Sqlite, $"ok type={clr}")
                );
            }

            (string Slug, string Native, string? Unbounded)[] binary =
            [
                ("blob", "blob", "1"),
                ("blob-1000", "blob(1000)", "0"),
                ("binary", "binary", "1"),
                ("varbinary", "varbinary", null),
                ("varbinary-100", "varbinary(100)", "0"),
                ("varbinary-max", "varbinary(max)", "1"),
            ];

            foreach (var (slug, native, unbounded) in binary)
            {
                rows.Add(
                    Map(
                            $"map-sqlite-binary-{slug}-byte-array",
                            "バイナリ宣言型は byte[]（参照型）へマップされる",
                            native
                        )
                        .Expect(DialectNames.Sqlite, "ok type=byte[] ref=1")
                );

                if (unbounded is not null)
                {
                    rows.Add(
                        Map(
                                $"map-sqlite-binary-{slug}-unbounded",
                                "長さ宣言なし・(MAX) は無制限バイナリ、長さ付きは有界と判定される",
                                native
                            )
                            .Expect(DialectNames.Sqlite, $"ok unbounded={unbounded}")
                    );
                }
            }

            rows.Add(
                Map(
                        "map-sqlite-nonbinary-nvarchar-not-unbounded",
                        "非バイナリ型は IsUnboundedBinary=false になる",
                        "nvarchar(100)"
                    )
                    .Expect(DialectNames.Sqlite, "ok unbounded=0")
            );
            rows.Add(
                Map(
                        "map-sqlite-nonbinary-int-not-unbounded",
                        "非バイナリ型は IsUnboundedBinary=false になる",
                        "int"
                    )
                    .Expect(DialectNames.Sqlite, "ok unbounded=0")
            );

            foreach (var native in new[] { "geometry", "weird_type" })
            {
                rows.Add(
                    Map(
                            $"map-sqlite-unknown-{native.Replace('_', '-')}-fallback",
                            "未知の宣言型は IsFallbackType が true になる",
                            native
                        )
                        .Expect(DialectNames.Sqlite, "ok fallback=1")
                );
            }

            rows.Add(
                Map(
                        "map-sqlite-known-int-not-fallback",
                        "解決できる型は IsFallbackType が false になる",
                        "int"
                    )
                    .Expect(DialectNames.Sqlite, "ok fallback=0")
            );
            rows.Add(
                Map(
                        "map-sqlite-known-nvarchar-50-not-fallback",
                        "解決できる型は IsFallbackType が false になる",
                        "nvarchar(50)"
                    )
                    .Expect(DialectNames.Sqlite, "ok fallback=0")
            );

            string[] realAffinity =
            [
                "REAL",
                "real",
                "FLOAT",
                "DOUBLE",
                "DOUBLE PRECISION",
                "FLOAT8",
                "REAL(10)",
            ];

            foreach (var native in realAffinity)
            {
                var slug = native == "real" ? "real-lower" : SqliteSlug(native).Replace(' ', '-');
                rows.Add(
                    Map(
                            $"map-sqlite-real-affinity-{slug}-double",
                            "REAL アフィニティの宣言型は double へ写す",
                            native
                        )
                        .Expect(DialectNames.Sqlite, "ok type=double fallback=0")
                );
            }

            // 先のアフィニティ規則（INT → TEXT 系 → BLOB → REAL）に当たる名前は double にしない（元の表明は否定だけ）
            foreach (var native in new[] { "FLOATINT", "REALCHAR", "DOUBLEBLOB" })
            {
                rows.Add(
                    Map(
                            $"map-sqlite-earlier-affinity-{native.ToLowerInvariant()}",
                            "先のアフィニティ規則に当たる名前は double にしない",
                            native
                        )
                        .Expect(DialectNames.Sqlite, "ok type=not:double")
                );
            }

            return rows;
        }
    }

    /// <summary>方言切替の行（期待の方言は移行先）</summary>
    private static IEnumerable<DialectTypeRow> SqliteConvertRows
    {
        get
        {
            var rows = new List<DialectTypeRow>();

            (string Source, string Expected)[] toSqlite =
            [
                ("int", "INT"),
                ("bigint", "BIGINT"),
                ("smallint", "SMALLINT"),
                ("tinyint", "TINYINT"),
                ("bit", "BIT"),
                ("nvarchar(50)", "NVARCHAR(50)"),
                ("nvarchar(max)", "NVARCHAR(MAX)"),
                ("varchar(50)", "VARCHAR(50)"),
                ("decimal(18,2)", "DECIMAL(18,2)"),
                ("uniqueidentifier", "UNIQUEIDENTIFIER"),
                ("datetime2", "DATETIME2"),
                ("datetimeoffset", "DATETIMEOFFSET"),
                ("varbinary(max)", "VARBINARY(MAX)"),
            ];

            foreach (var (source, expected) in toSqlite)
            {
                rows.Add(
                    Convert(
                            $"convert-sqlite-from-sqlserver-{SqliteSlug(source)}",
                            "sqlserver → sqlite の代表変換",
                            new ConvertInput(DialectNames.SqlServer, source)
                        )
                        .Expect(DialectNames.Sqlite, $"converted type={expected}")
                );
            }

            (string Source, string Expected)[] toSqlServer =
            [
                ("INT", "int"),
                ("BIGINT", "bigint"),
                ("BIT", "bit"),
                ("NVARCHAR(50)", "nvarchar(50)"),
                ("VARCHAR(50)", "varchar(50)"),
                ("DECIMAL(18,2)", "decimal(18,2)"),
                ("UNIQUEIDENTIFIER", "uniqueidentifier"),
                ("DATETIME2", "datetime2"),
                ("VARBINARY(MAX)", "varbinary(max)"),
                ("TEXT", "nvarchar(max)"),
                ("BLOB", "varbinary(max)"),
                ("JSON", "nvarchar(max)"),
            ];

            foreach (var (source, expected) in toSqlServer)
            {
                rows.Add(
                    Convert(
                            $"convert-sqlite-to-sqlserver-{SqliteSlug(source)}",
                            "sqlite → sqlserver の代表変換",
                            new ConvertInput(DialectNames.Sqlite, source)
                        )
                        .Expect(DialectNames.SqlServer, $"converted type={expected}")
                );
            }

            // 往復：sqlserver → sqlite の出力をそのまま sqlite → sqlserver の入力にする（1 段目は変換されることだけを見る）
            string[] roundTrips =
            [
                "int",
                "bigint",
                "smallint",
                "tinyint",
                "bit",
                "decimal(18,2)",
                "money",
                "float",
                "nvarchar(50)",
                "nvarchar(max)",
                "varchar(255)",
                "nchar(10)",
                "char(5)",
                "varbinary(50)",
                "varbinary(max)",
                "binary(16)",
                "date",
                "time(7)",
                "datetime2(3)",
                "datetimeoffset(7)",
                "uniqueidentifier",
                "xml",
            ];

            const string roundTripDescription = "sqlserver → sqlite → sqlserver の往復が無損失";

            foreach (var source in roundTrips)
            {
                rows.Add(
                    Convert(
                            $"convert-sqlite-roundtrip-{SqliteSlug(source)}",
                            roundTripDescription,
                            new ConvertInput(
                                DialectNames.SqlServer,
                                source,
                                BackTo: DialectNames.SqlServer
                            )
                        )
                        .Expect(DialectNames.Sqlite, $"converted back=converted back-type={source}")
                );
            }

            // 単精度 real は REAL（倍精度）を経て float へ広がる
            const string realDescription =
                "sqlserver real は sqlite REAL を経て float（倍精度）へ広がる";
            rows.Add(
                Convert(
                        "convert-sqlite-widen-real-step1",
                        realDescription,
                        new ConvertInput(DialectNames.SqlServer, "real")
                    )
                    .Expect(DialectNames.Sqlite, "converted type=REAL")
            );
            rows.Add(
                Convert(
                        "convert-sqlite-widen-real-step2",
                        realDescription,
                        new ConvertInput(DialectNames.Sqlite, "REAL")
                    )
                    .Expect(DialectNames.SqlServer, "converted type=float")
            );

            rows.Add(
                Convert(
                        "convert-sqlite-from-sqlserver-hierarchyid-unconverted",
                        "sqlserver hierarchyid は sqlite へ変換できず Unconverted",
                        new ConvertInput(DialectNames.SqlServer, "hierarchyid")
                    )
                    .Expect(DialectNames.Sqlite, "unconverted")
            );
            rows.Add(
                Convert(
                        "convert-sqlite-to-sqlserver-unknown-type-unconverted",
                        "sqlite の未知型は sqlserver へ変換できず Unconverted",
                        new ConvertInput(DialectNames.Sqlite, "no_such_type")
                    )
                    .Expect(DialectNames.SqlServer, "unconverted")
            );

            return rows;
        }
    }

    private static string SqliteSlug(string native) =>
        native.ToLowerInvariant().Replace("(", "-").Replace(")", "").Replace(",", "-");
}
