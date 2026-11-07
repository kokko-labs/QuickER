using QuickER.Dialects;
using QuickER.Provider;

namespace QuickER.Tests.Provider;

/// <summary>方言の型の表：全正規型 × 5 方言の書き出しと往復の表（代表値を書き出して読み戻す）</summary>
/// <remarks>
/// セルの宣言から、種類ごとに書き出しの行（<c>matrix-format-*</c>）と往復の行（<c>matrix-roundtrip-*</c>）を作る。
/// セルが 5 方言そろわない種類があれば、表を作る時点で名指しして断る（宣言の抜け・余りを止める）。
/// </remarks>
internal static partial class DialectTypeRows
{
    /// <summary>種類ごとの代表値（長さ 42・精度 12 / スケール 3・小数秒 6。型トークンの往復の行も同じ値を使う）</summary>
    /// <param name="kind">正規型の種類</param>
    internal static CanonicalType RepresentativeSample(CanonicalTypeKind kind) =>
        kind switch
        {
            CanonicalTypeKind.String
            or CanonicalTypeKind.AnsiString
            or CanonicalTypeKind.FixedString
            or CanonicalTypeKind.AnsiFixedString
            or CanonicalTypeKind.Binary
            or CanonicalTypeKind.FixedBinary => new CanonicalType(kind, Length: 42),
            CanonicalTypeKind.Decimal => new CanonicalType(kind, Precision: 12, Scale: 3),
            CanonicalTypeKind.Time
            or CanonicalTypeKind.DateTime
            or CanonicalTypeKind.DateTimeOffset => new CanonicalType(kind, Precision: 6),
            _ => new CanonicalType(kind),
        };

    private static IEnumerable<DialectTypeRow> MatrixRows()
    {
        var cells = CheckedMatrixCells();

        foreach (var kind in Enum.GetValues<CanonicalTypeKind>())
        {
            var sample = RepresentativeSample(kind);
            var name = kind.ToString().ToLowerInvariant();
            var format = DialectTypeRow.Format(
                $"matrix-format-{name}",
                $"全 CanonicalTypeKind × 5 方言の TryFormat が宣言表と一致する（{kind}）",
                sample
            );
            var roundTrip = DialectTypeRow.RoundTripCanonical(
                $"matrix-roundtrip-{name}",
                $"全 CanonicalTypeKind × 5 方言の TryFormat→TryParse 往復が宣言表と一致する（{kind}）",
                sample
            );

            foreach (var cell in cells.Where(cell => cell.Kind == kind))
            {
                format.Expect(cell.Dialect, cell.Native is null ? "fail" : $"ok {cell.Native}");
                roundTrip.Expect(
                    cell.Dialect,
                    cell switch
                    {
                        { Native: null } => "format-fail",
                        { Reparsed: null } => $"parse-fail text=\"{cell.Native}\"",
                        _ => $"ok text=\"{cell.Native}\" back={cell.Reparsed}",
                    }
                );
            }

            yield return format;
            yield return roundTrip;
        }
    }

    /// <summary>
    /// 宣言のセル（種類 × 方言ごとにちょうど 1 つあることを確かめた後。抜け・余りは名指しして断る）
    /// </summary>
    private static IReadOnlyList<MatrixCell> CheckedMatrixCells()
    {
        var cells = MatrixCells().ToList();
        var problems = new List<string>();

        foreach (var kind in Enum.GetValues<CanonicalTypeKind>())
        {
            foreach (var dialect in DialectNames.All)
            {
                var count = cells.Count(cell => cell.Kind == kind && cell.Dialect == dialect);

                if (count != 1)
                {
                    problems.Add($"{dialect} × {kind} のセルが {count} 個");
                }
            }
        }

        if (problems.Count > 0)
        {
            throw new InvalidOperationException(
                "往復の表のセルが整っていない：" + string.Join("、", problems)
            );
        }

        return cells;
    }

    /// <summary>1 セル（書き出しの表記・読み戻した正規型のトークン。書き出せなければ両方 null）</summary>
    private sealed record MatrixCell(
        string Dialect,
        CanonicalTypeKind Kind,
        string? Native,
        string? Reparsed
    )
    {
        /// <summary>
        /// 表記を解析して書き出し直した表記（<see cref="Native"/> と違うときだけ宣言する。null なら <see cref="Native"/> に戻る）
        /// </summary>
        public string? Rewritten { get; init; }

        /// <summary>
        /// 取込の表記（方言の <c>TypeNotation.Normalize</c> の結果。<see cref="Native"/> と違うときだけ宣言する。null なら
        /// <see cref="Native"/> のまま）
        /// </summary>
        public string? Imported { get; init; }
    }

    /// <summary>書き出して読み戻せるセル</summary>
    private static MatrixCell C(
        string dialect,
        CanonicalTypeKind kind,
        string native,
        string reparsed
    ) => new(dialect, kind, native, reparsed);

    /// <summary>書き出せるが同じ方言で読み戻せないセル</summary>
    private static MatrixCell F(string dialect, CanonicalTypeKind kind, string native) =>
        new(dialect, kind, native, null);

    /// <summary>書き出せない（方言切替で変換できない列になる）セル</summary>
    private static MatrixCell U(string dialect, CanonicalTypeKind kind) =>
        new(dialect, kind, null, null);

    /// <summary>
    /// セルの宣言（方言ごと・種類の宣言の順）。右辺のトークンが左辺と違う種類なら非可逆で、理由をコメントに書く
    /// </summary>
    private static IEnumerable<MatrixCell> MatrixCells() =>
        [
            // ---- SQL Server: json 以外は全種別が可逆（正規型の設計基準となる方言）----
            C(DialectNames.SqlServer, CanonicalTypeKind.Boolean, "bit", "boolean"),
            C(DialectNames.SqlServer, CanonicalTypeKind.TinyInt, "tinyint", "tinyint"),
            C(DialectNames.SqlServer, CanonicalTypeKind.SmallInt, "smallint", "smallint"),
            C(DialectNames.SqlServer, CanonicalTypeKind.Int32, "int", "int32"),
            C(DialectNames.SqlServer, CanonicalTypeKind.Int64, "bigint", "int64"),
            C(DialectNames.SqlServer, CanonicalTypeKind.Decimal, "decimal(12,3)", "decimal(12,3)"),
            C(DialectNames.SqlServer, CanonicalTypeKind.Float32, "real", "float32"),
            C(DialectNames.SqlServer, CanonicalTypeKind.Float64, "float", "float64"),
            C(DialectNames.SqlServer, CanonicalTypeKind.Money, "money", "money"),
            C(DialectNames.SqlServer, CanonicalTypeKind.String, "nvarchar(42)", "string(42)"),
            C(
                DialectNames.SqlServer,
                CanonicalTypeKind.AnsiString,
                "varchar(42)",
                "ansistring(42)"
            ),
            C(
                DialectNames.SqlServer,
                CanonicalTypeKind.FixedString,
                "nchar(42)",
                "fixedstring(42)"
            ),
            C(
                DialectNames.SqlServer,
                CanonicalTypeKind.AnsiFixedString,
                "char(42)",
                "ansifixedstring(42)"
            ),
            C(DialectNames.SqlServer, CanonicalTypeKind.Binary, "varbinary(42)", "binary(42)"),
            C(
                DialectNames.SqlServer,
                CanonicalTypeKind.FixedBinary,
                "binary(42)",
                "fixedbinary(42)"
            ),
            C(DialectNames.SqlServer, CanonicalTypeKind.Date, "date", "date"),
            C(DialectNames.SqlServer, CanonicalTypeKind.Time, "time(6)", "time(6)"),
            C(DialectNames.SqlServer, CanonicalTypeKind.DateTime, "datetime2(6)", "datetime(6)"),
            C(
                DialectNames.SqlServer,
                CanonicalTypeKind.DateTimeOffset,
                "datetimeoffset(6)",
                "datetimeoffset(6)"
            ),
            C(DialectNames.SqlServer, CanonicalTypeKind.Guid, "uniqueidentifier", "guid"),
            C(DialectNames.SqlServer, CanonicalTypeKind.Xml, "xml", "xml"),
            // 非可逆（種別）: SqlServerDataTypes に json 型が無く nvarchar(max) を代替に使うため文字列へ落ちる
            C(DialectNames.SqlServer, CanonicalTypeKind.Json, "nvarchar(max)", "string(max)"),
            // 取込の表記は timestamp（docs/database.md の same-type-spellings の表「rowversion = timestamp」）
            C(DialectNames.SqlServer, CanonicalTypeKind.RowVersion, "rowversion", "rowversion") with
            {
                Imported = "timestamp",
            },
            // ---- PostgreSQL ----
            C(DialectNames.PostgreSql, CanonicalTypeKind.Boolean, "boolean", "boolean"),
            // 非可逆（種別）: PostgreSQL に tinyint が無く smallint で受ける
            C(DialectNames.PostgreSql, CanonicalTypeKind.TinyInt, "smallint", "smallint"),
            C(DialectNames.PostgreSql, CanonicalTypeKind.SmallInt, "smallint", "smallint"),
            C(DialectNames.PostgreSql, CanonicalTypeKind.Int32, "integer", "int32"),
            C(DialectNames.PostgreSql, CanonicalTypeKind.Int64, "bigint", "int64"),
            C(DialectNames.PostgreSql, CanonicalTypeKind.Decimal, "numeric(12,3)", "decimal(12,3)"),
            C(DialectNames.PostgreSql, CanonicalTypeKind.Float32, "real", "float32"),
            C(DialectNames.PostgreSql, CanonicalTypeKind.Float64, "double precision", "float64"),
            C(DialectNames.PostgreSql, CanonicalTypeKind.Money, "money", "money"),
            C(DialectNames.PostgreSql, CanonicalTypeKind.String, "varchar(42)", "string(42)"),
            // 非可逆（種別）: PostgreSQL の varchar は Unicode 可変長のみ＝Ansi の区別が消える
            C(DialectNames.PostgreSql, CanonicalTypeKind.AnsiString, "varchar(42)", "string(42)"),
            C(
                DialectNames.PostgreSql,
                CanonicalTypeKind.FixedString,
                "char(42)",
                "fixedstring(42)"
            ),
            // 非可逆（種別）: 同上（固定長も Ansi の区別が消える）
            C(
                DialectNames.PostgreSql,
                CanonicalTypeKind.AnsiFixedString,
                "char(42)",
                "fixedstring(42)"
            ),
            // 非可逆（引数）: bytea は長さの概念を持たないため上限なしのバイナリになる
            C(DialectNames.PostgreSql, CanonicalTypeKind.Binary, "bytea", "binary(max)"),
            // 非可逆（種別）: 固定長バイナリも bytea 単一型へ落ちる
            C(DialectNames.PostgreSql, CanonicalTypeKind.FixedBinary, "bytea", "binary(max)"),
            C(DialectNames.PostgreSql, CanonicalTypeKind.Date, "date", "date"),
            C(DialectNames.PostgreSql, CanonicalTypeKind.Time, "time(6)", "time(6)"),
            C(DialectNames.PostgreSql, CanonicalTypeKind.DateTime, "timestamp(6)", "datetime(6)"),
            C(
                DialectNames.PostgreSql,
                CanonicalTypeKind.DateTimeOffset,
                "timestamptz(6)",
                "datetimeoffset(6)"
            ),
            C(DialectNames.PostgreSql, CanonicalTypeKind.Guid, "uuid", "guid"),
            C(DialectNames.PostgreSql, CanonicalTypeKind.Xml, "xml", "xml"),
            C(DialectNames.PostgreSql, CanonicalTypeKind.Json, "jsonb", "json"),
            // 変換不能: 行バージョンは SQL Server 固有（方言切替で「変換できない列」に載る）
            U(DialectNames.PostgreSql, CanonicalTypeKind.RowVersion),
            // ---- MySQL ----
            C(DialectNames.MySql, CanonicalTypeKind.Boolean, "tinyint(1)", "boolean"),
            C(DialectNames.MySql, CanonicalTypeKind.TinyInt, "tinyint unsigned", "tinyint"),
            C(DialectNames.MySql, CanonicalTypeKind.SmallInt, "smallint", "smallint"),
            C(DialectNames.MySql, CanonicalTypeKind.Int32, "int", "int32"),
            C(DialectNames.MySql, CanonicalTypeKind.Int64, "bigint", "int64"),
            C(DialectNames.MySql, CanonicalTypeKind.Decimal, "decimal(12,3)", "decimal(12,3)"),
            C(DialectNames.MySql, CanonicalTypeKind.Float32, "float", "float32"),
            C(DialectNames.MySql, CanonicalTypeKind.Float64, "double", "float64"),
            // 非可逆（種別）: MySQL に通貨型が無く decimal(19,4) で受ける（format 専用）
            C(DialectNames.MySql, CanonicalTypeKind.Money, "decimal(19,4)", "decimal(19,4)"),
            C(DialectNames.MySql, CanonicalTypeKind.String, "varchar(42)", "string(42)"),
            // 非可逆（種別）: utf8mb4 前提のため Ansi の区別が消える
            C(DialectNames.MySql, CanonicalTypeKind.AnsiString, "varchar(42)", "string(42)"),
            C(DialectNames.MySql, CanonicalTypeKind.FixedString, "char(42)", "fixedstring(42)"),
            // 非可逆（種別）: 同上
            C(DialectNames.MySql, CanonicalTypeKind.AnsiFixedString, "char(42)", "fixedstring(42)"),
            C(DialectNames.MySql, CanonicalTypeKind.Binary, "varbinary(42)", "binary(42)"),
            C(DialectNames.MySql, CanonicalTypeKind.FixedBinary, "binary(42)", "fixedbinary(42)"),
            C(DialectNames.MySql, CanonicalTypeKind.Date, "date", "date"),
            C(DialectNames.MySql, CanonicalTypeKind.Time, "time(6)", "time(6)"),
            C(DialectNames.MySql, CanonicalTypeKind.DateTime, "datetime(6)", "datetime(6)"),
            C(
                DialectNames.MySql,
                CanonicalTypeKind.DateTimeOffset,
                "timestamp(6)",
                "datetimeoffset(6)"
            ),
            // 非可逆（種別）: MySQL に UUID 型が無く char(36) で受ける（format 専用）
            C(DialectNames.MySql, CanonicalTypeKind.Guid, "char(36)", "fixedstring(36)"),
            // 非可逆（種別）: MySQL に XML 型が無く longtext で受ける（format 専用）
            C(DialectNames.MySql, CanonicalTypeKind.Xml, "longtext", "string(max)"),
            C(DialectNames.MySql, CanonicalTypeKind.Json, "json", "json"),
            // 変換不能: 行バージョンは SQL Server 固有
            U(DialectNames.MySql, CanonicalTypeKind.RowVersion),
            // ---- Oracle ----
            C(DialectNames.Oracle, CanonicalTypeKind.Boolean, "NUMBER(1)", "boolean"),
            C(DialectNames.Oracle, CanonicalTypeKind.TinyInt, "NUMBER(3)", "tinyint"),
            C(DialectNames.Oracle, CanonicalTypeKind.SmallInt, "NUMBER(5)", "smallint"),
            C(DialectNames.Oracle, CanonicalTypeKind.Int32, "NUMBER(10)", "int32"),
            C(DialectNames.Oracle, CanonicalTypeKind.Int64, "NUMBER(19)", "int64"),
            C(DialectNames.Oracle, CanonicalTypeKind.Decimal, "NUMBER(12,3)", "decimal(12,3)"),
            C(DialectNames.Oracle, CanonicalTypeKind.Float32, "BINARY_FLOAT", "float32"),
            C(DialectNames.Oracle, CanonicalTypeKind.Float64, "BINARY_DOUBLE", "float64"),
            // 非可逆（種別）: Oracle に通貨型が無く NUMBER(19,4) で受ける
            C(DialectNames.Oracle, CanonicalTypeKind.Money, "NUMBER(19,4)", "decimal(19,4)"),
            C(DialectNames.Oracle, CanonicalTypeKind.String, "NVARCHAR2(42)", "string(42)"),
            C(DialectNames.Oracle, CanonicalTypeKind.AnsiString, "VARCHAR2(42)", "ansistring(42)"),
            C(DialectNames.Oracle, CanonicalTypeKind.FixedString, "NCHAR(42)", "fixedstring(42)"),
            C(
                DialectNames.Oracle,
                CanonicalTypeKind.AnsiFixedString,
                "CHAR(42)",
                "ansifixedstring(42)"
            ),
            C(DialectNames.Oracle, CanonicalTypeKind.Binary, "RAW(42)", "binary(42)"),
            // 非可逆（種別）: 可変長・固定長ともに RAW(n) 単一型へ落ちる
            C(DialectNames.Oracle, CanonicalTypeKind.FixedBinary, "RAW(42)", "binary(42)"),
            // 非可逆（種別）: Oracle の DATE は時刻を含むため、日付のみが日時になる（設計上の非対称）。
            // 書き出し直すと精度なしの日時の TIMESTAMP になる（roundtrip-oracle-date-asymmetric の行と同じ事実）
            C(DialectNames.Oracle, CanonicalTypeKind.Date, "DATE", "datetime") with
            {
                Rewritten = "TIMESTAMP",
            },
            // 変換不能: Oracle に TIME 型が無い
            U(DialectNames.Oracle, CanonicalTypeKind.Time),
            C(DialectNames.Oracle, CanonicalTypeKind.DateTime, "TIMESTAMP(6)", "datetime(6)"),
            C(
                DialectNames.Oracle,
                CanonicalTypeKind.DateTimeOffset,
                "TIMESTAMP(6) WITH TIME ZONE",
                "datetimeoffset(6)"
            ),
            // 非可逆（種別）: Oracle に UUID 型が無く RAW(16) で表現する（format 専用）
            C(DialectNames.Oracle, CanonicalTypeKind.Guid, "RAW(16)", "binary(16)"),
            C(DialectNames.Oracle, CanonicalTypeKind.Xml, "XMLTYPE", "xml"),
            // 非可逆（種別）: 19c に JSON 型が無く CLOB で表現する（format 専用）
            C(DialectNames.Oracle, CanonicalTypeKind.Json, "CLOB", "ansistring(max)"),
            // 変換不能: 行バージョンは SQL Server 固有
            U(DialectNames.Oracle, CanonicalTypeKind.RowVersion),
            // ---- SQLite（型親和性のため SQL Server 表記をほぼそのまま受ける）----
            C(DialectNames.Sqlite, CanonicalTypeKind.Boolean, "BIT", "boolean"),
            C(DialectNames.Sqlite, CanonicalTypeKind.TinyInt, "TINYINT", "tinyint"),
            C(DialectNames.Sqlite, CanonicalTypeKind.SmallInt, "SMALLINT", "smallint"),
            C(DialectNames.Sqlite, CanonicalTypeKind.Int32, "INT", "int32"),
            C(DialectNames.Sqlite, CanonicalTypeKind.Int64, "BIGINT", "int64"),
            C(DialectNames.Sqlite, CanonicalTypeKind.Decimal, "DECIMAL(12,3)", "decimal(12,3)"),
            // SQLite に単精度は無い。REAL（8 バイトの倍精度）へ写し、読み戻すと Float64＝精度を広げる方向の非可逆
            // 書き出し直すと Float64 の FLOAT になる（src/QuickER.Provider.Sqlite/CLAUDE.md「型表記」：REAL の列は FLOAT へ畳まれる）
            C(DialectNames.Sqlite, CanonicalTypeKind.Float32, "REAL", "float64") with
            {
                Rewritten = "FLOAT",
            },
            C(DialectNames.Sqlite, CanonicalTypeKind.Float64, "FLOAT", "float64"),
            C(DialectNames.Sqlite, CanonicalTypeKind.Money, "MONEY", "money"),
            C(DialectNames.Sqlite, CanonicalTypeKind.String, "NVARCHAR(42)", "string(42)"),
            C(DialectNames.Sqlite, CanonicalTypeKind.AnsiString, "VARCHAR(42)", "ansistring(42)"),
            C(DialectNames.Sqlite, CanonicalTypeKind.FixedString, "NCHAR(42)", "fixedstring(42)"),
            C(
                DialectNames.Sqlite,
                CanonicalTypeKind.AnsiFixedString,
                "CHAR(42)",
                "ansifixedstring(42)"
            ),
            C(DialectNames.Sqlite, CanonicalTypeKind.Binary, "VARBINARY(42)", "binary(42)"),
            C(DialectNames.Sqlite, CanonicalTypeKind.FixedBinary, "BINARY(42)", "fixedbinary(42)"),
            C(DialectNames.Sqlite, CanonicalTypeKind.Date, "DATE", "date"),
            C(DialectNames.Sqlite, CanonicalTypeKind.Time, "TIME(6)", "time(6)"),
            C(DialectNames.Sqlite, CanonicalTypeKind.DateTime, "DATETIME2(6)", "datetime(6)"),
            C(
                DialectNames.Sqlite,
                CanonicalTypeKind.DateTimeOffset,
                "DATETIMEOFFSET(6)",
                "datetimeoffset(6)"
            ),
            C(DialectNames.Sqlite, CanonicalTypeKind.Guid, "UNIQUEIDENTIFIER", "guid"),
            C(DialectNames.Sqlite, CanonicalTypeKind.Xml, "XML", "xml"),
            C(DialectNames.Sqlite, CanonicalTypeKind.Json, "JSON", "json"),
            // 非可逆（種別）: SQLite に行バージョンの概念が無く、値だけを写せる BLOB（ミラー列）へ落とす。
            // 読み戻すと上限なしのバイナリになるため、SQL Server へ戻しても varbinary(max) にしかならない
            // 書き出し直すと上限なしのバイナリの VARBINARY(MAX) になる（convert-sqlite-from-sqlserver-varbinary-max の行と同じ書き出し）
            C(DialectNames.Sqlite, CanonicalTypeKind.RowVersion, "BLOB", "binary(max)") with
            {
                Rewritten = "VARBINARY(MAX)",
            },
        ];
}
