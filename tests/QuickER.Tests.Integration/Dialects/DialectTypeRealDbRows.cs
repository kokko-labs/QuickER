using QuickER.Dialects;
using QuickER.Tests.Provider;

namespace QuickER.Tests.Integration.Dialects;

/// <summary>方言の型の表の実 DB の段で確かめること（<see cref="DialectTypeRealDbProbe"/> の種類）</summary>
internal static class DialectTypeRealDbProbeKinds
{
    /// <summary>上限ちょうどの表記：DDL が通り、取り込み直した表記が同期の能力の正規化と等しい</summary>
    public const string AtLimit = "at-limit";

    /// <summary>上限を 1 つ超えた表記：DB が断るか、上限へ丸めて取り込ませる</summary>
    public const string OverLimit = "over-limit";

    /// <summary>型マッパーの型：ドライバの <c>GetFieldType</c> と一致し、その型で読める</summary>
    public const string Driver = "driver";

    /// <summary>すべての種類（次元表の水準の並び）</summary>
    public static IReadOnlyList<string> All { get; } = [AtLimit, OverLimit, Driver];
}

/// <summary>
/// 方言の型の表（<see cref="DialectTypeRows"/>）の 1 行を、1 方言の実 DB で確かめる 1 つの手順
/// </summary>
/// <param name="RowId">表の行の Id（その方言の期待を持つ行。書き出しの行・パラメータの書き出しの行・マッパーの行）</param>
/// <param name="Dialect">方言（<see cref="DialectNames"/> の値）</param>
/// <param name="Kind">種類（<see cref="DialectTypeRealDbProbeKinds"/>）</param>
/// <param name="Spelling">
/// DDL に書く型の表記。null なら行の出力（書き出しの行は <c>TryFormat</c>・パラメータの書き出しの行は
/// <c>TryFormatParameterType</c>・マッパーの行は行の入力）
/// </param>
/// <param name="Imported">
/// 取り込み直した表記の期待（上限ちょうどは null＝<c>NormalizeTypeNotation(表記)</c>。1 つ超えで null なら DB が断る＝
/// 値があれば DB が通して上限へ丸め、取込がこの表記になる。マッパーの行で null なら表記を見ない）
/// </param>
/// <param name="Value">マッパーの行で入れる値の SQL のリテラル（<c>NULL</c> なら列を NULL 許容で作る）</param>
/// <param name="Driver">
/// マッパーの行で、ドライバの <c>GetFieldType</c> が返す CLR 型（null ならドライバの型は見ず、取り込み直した表記と
/// マッパーの型〔<paramref name="MapperType"/>〕だけを見る）
/// </param>
/// <param name="MapperType">
/// マッパーの <c>TypeName</c> がドライバの型の C# の名前と違うことを意図しているときの、マッパーの型（null なら同じ）
/// </param>
/// <param name="ReadBack">ドライバの型で読んだ値の期待（null なら値は見ない）</param>
/// <param name="NarrowingLoses">読んだ <c>double</c> を <c>float</c> へ落とすと値が変わるか（入れた値が倍精度を要することの確認）</param>
internal sealed record DialectTypeRealDbProbe(
    string RowId,
    string Dialect,
    string Kind,
    string? Spelling = null,
    string? Imported = null,
    string? Value = null,
    Type? Driver = null,
    string? MapperType = null,
    object? ReadBack = null,
    bool NarrowingLoses = false
)
{
    /// <summary>NULL を入れる手順か（列を NULL 許容で作り、型で読む確認は飛ばす）</summary>
    public bool InsertsNull => string.Equals(Value, "NULL", StringComparison.OrdinalIgnoreCase);

    /// <summary>失敗の文と一覧に使う名前</summary>
    public string Describe() =>
        Value is null ? $"{RowId} [{Dialect} {Kind}]" : $"{RowId} [{Dialect} {Kind} {Value}]";
}

/// <summary>
/// 方言の型の表の実 DB の段の手順（行の Id で表の行を引く。入力と単体の期待は表の行だけが持つ）
/// </summary>
/// <remarks>
/// <para>
/// 上限の対は、DB の上限が製品の宣言（<c>TryFormat</c> が書き出す上限）と一致することを確かめる：ちょうどは DDL が通って
/// 取り込み直すと同期の能力の正規化（<c>SyncDialectCapabilities.NormalizeTypeNotation</c>）と同じ表記に戻り、1 つ超えは DB が
/// 断る（だから書き出さない）。PostgreSQL の小数秒だけは DB が断らずに 6 へ丸める。
/// </para>
/// <para>
/// マッパーの行は、ドライバの実測の CLR 型が正（生成コードはマッパーの型で読む）。行の入力の表記と取り込み直した表記の
/// どちらもマッパーが同じ型（代替の型でない）へ写し、ドライバの型で読めることを確かめる。
/// </para>
/// </remarks>
internal static class DialectTypeRealDbRows
{
    private const string MySql = DialectNames.MySql;
    private const string PostgreSql = DialectNames.PostgreSql;

    /// <summary>すべての手順（方言ごとの派生クラスが自分の方言の分を流す）</summary>
    public static IReadOnlyList<DialectTypeRealDbProbe> All { get; } =
    [
        .. MySqlLimits(),
        .. PostgreSqlLimits(),
        .. SqlServerLimits(),
        .. OracleLimits(),
        .. SqlServerDrivers(),
        .. MySqlDrivers(),
        .. OracleDrivers(),
        .. PostgreSqlDrivers(),
        .. SqliteDrivers(),
    ];

    /// <summary>方言の手順</summary>
    /// <param name="dialect">方言</param>
    public static IReadOnlyList<DialectTypeRealDbProbe> For(string dialect) =>
        [.. All.Where(probe => string.Equals(probe.Dialect, dialect, StringComparison.Ordinal))];

    /// <summary>
    /// MySQL の上限の対（1 つ超えの表記は書き出されないので書き下ろす。表は 1 列だけ＝<c>varbinary</c> は列の上限を持たず
    /// 行全体の上限〔65,535 バイト〕だけが効くので、ほかの列と並べると上限ちょうどでも断られる）
    /// </summary>
    private static IEnumerable<DialectTypeRealDbProbe> MySqlLimits()
    {
        foreach (
            var (kind, limit, beyond) in new[]
            {
                ("fixedstring", 255, "char(256)"),
                ("fixedbinary", 255, "binary(256)"),
                ("string", 16383, "varchar(16384)"),
                ("binary", 65532, "varbinary(65533)"),
            }
        )
        {
            yield return new(
                $"format-mysql-limit-{kind}-{limit}-at-limit",
                MySql,
                DialectTypeRealDbProbeKinds.AtLimit
            );
            yield return new(
                $"format-mysql-limit-{kind}-{limit + 1}-over-limit",
                MySql,
                DialectTypeRealDbProbeKinds.OverLimit,
                Spelling: beyond
            );
        }
    }

    /// <summary>
    /// PostgreSQL の上限の対（1 つ超えの表記はパラメータの型の書き出し＝列の上限を当てない書き出しの出力。小数秒の 7 は
    /// PostgreSQL が 6 へ丸める）
    /// </summary>
    private static IEnumerable<DialectTypeRealDbProbe> PostgreSqlLimits()
    {
        foreach (
            var name in new[]
            {
                "decimal-precision",
                "decimal-scale",
                "decimal-negative-scale",
                "string-length",
                "fixedstring-length",
            }
        )
        {
            yield return new(
                $"format-postgresql-{name}-at-limit",
                PostgreSql,
                DialectTypeRealDbProbeKinds.AtLimit
            );
            yield return new(
                $"format-param-postgresql-{name}-over-limit",
                PostgreSql,
                DialectTypeRealDbProbeKinds.OverLimit
            );
        }

        foreach (
            var (name, rounded) in new[]
            {
                ("datetime-precision", "timestamp(6)"),
                ("datetimeoffset-precision", "timestamptz(6)"),
                ("time-precision", "time(6)"),
            }
        )
        {
            yield return new(
                $"format-postgresql-{name}-at-limit",
                PostgreSql,
                DialectTypeRealDbProbeKinds.AtLimit
            );
            yield return new(
                $"format-param-postgresql-{name}-over-limit",
                PostgreSql,
                DialectTypeRealDbProbeKinds.OverLimit,
                Imported: rounded
            );
        }
    }

    /// <summary>
    /// SQL Server の上限の対（単体の行 <c>format-sqlserver-limit-{名前}-at</c> / <c>-over</c>。1 つ超えの表記は書き出されないので書き下ろす）
    /// </summary>
    private static IEnumerable<DialectTypeRealDbProbe> SqlServerLimits() =>
        LimitPairs(
            DialectNames.SqlServer,
            [
                (
                    "format-sqlserver-limit-string-at",
                    "format-sqlserver-limit-string-over",
                    "nvarchar(4001)"
                ),
                (
                    "format-sqlserver-limit-fixedstring-at",
                    "format-sqlserver-limit-fixedstring-over",
                    "nchar(4001)"
                ),
                (
                    "format-sqlserver-limit-ansistring-at",
                    "format-sqlserver-limit-ansistring-over",
                    "varchar(8001)"
                ),
                (
                    "format-sqlserver-limit-ansifixedstring-at",
                    "format-sqlserver-limit-ansifixedstring-over",
                    "char(8001)"
                ),
                (
                    "format-sqlserver-limit-binary-at",
                    "format-sqlserver-limit-binary-over",
                    "varbinary(8001)"
                ),
                (
                    "format-sqlserver-limit-fixedbinary-at",
                    "format-sqlserver-limit-fixedbinary-over",
                    "binary(8001)"
                ),
                (
                    "format-sqlserver-limit-decimal-precision-at",
                    "format-sqlserver-limit-decimal-precision-over",
                    "decimal(39,2)"
                ),
                (
                    "format-sqlserver-limit-datetime-fraction-at",
                    "format-sqlserver-limit-datetime-fraction-over",
                    "datetime2(8)"
                ),
                (
                    "format-sqlserver-limit-time-fraction-at",
                    "format-sqlserver-limit-time-fraction-over",
                    "time(8)"
                ),
                (
                    "format-sqlserver-limit-datetimeoffset-fraction-at",
                    "format-sqlserver-limit-datetimeoffset-fraction-over",
                    "datetimeoffset(8)"
                ),
            ]
        );

    /// <summary>
    /// Oracle の上限の対（単体の行 <c>format-oracle-limit-*-ok</c> / <c>-fails</c>。1 つ超えの表記は書き出されないので書き下ろす。
    /// 文字列・RAW の上限はコンテナの既定〔<c>MAX_STRING_SIZE=STANDARD</c>〕のもの。固定長の RAW は可変長と同じ RAW へ書き出す）
    /// </summary>
    private static IEnumerable<DialectTypeRealDbProbe> OracleLimits() =>
        LimitPairs(
            DialectNames.Oracle,
            [
                (
                    "format-oracle-limit-string-2000-ok",
                    "format-oracle-limit-string-2001-fails",
                    "NVARCHAR2(2001)"
                ),
                (
                    "format-oracle-limit-ansistring-4000-ok",
                    "format-oracle-limit-ansistring-4001-fails",
                    "VARCHAR2(4001)"
                ),
                (
                    "format-oracle-limit-fixedstring-1000-ok",
                    "format-oracle-limit-fixedstring-1001-fails",
                    "NCHAR(1001)"
                ),
                (
                    "format-oracle-limit-ansifixedstring-2000-ok",
                    "format-oracle-limit-ansifixedstring-2001-fails",
                    "CHAR(2001)"
                ),
                (
                    "format-oracle-limit-binary-2000-ok",
                    "format-oracle-limit-binary-2001-fails",
                    "RAW(2001)"
                ),
                (
                    "format-oracle-limit-fixedbinary-2000-ok",
                    "format-oracle-limit-fixedbinary-2001-fails",
                    "RAW(2001)"
                ),
                (
                    "format-oracle-limit-decimal-p38-s2-ok",
                    "format-oracle-limit-decimal-p39-s2-fails",
                    "NUMBER(39,2)"
                ),
                (
                    "format-oracle-limit-decimal-scale127-ok",
                    "format-oracle-limit-decimal-p10-s128-fails",
                    "NUMBER(10,128)"
                ),
                (
                    "format-oracle-limit-decimal-p10-sm84-ok",
                    "format-oracle-limit-decimal-p10-sm85-fails",
                    "NUMBER(10,-85)"
                ),
                (
                    "format-oracle-limit-datetime-p9-ok",
                    "format-oracle-limit-datetime-p10-fails",
                    "TIMESTAMP(10)"
                ),
                (
                    "format-oracle-limit-datetimeoffset-9-ok",
                    "format-oracle-limit-datetimeoffset-p10-fails",
                    "TIMESTAMP(10) WITH TIME ZONE"
                ),
            ]
        );

    /// <summary>上限ちょうどの行と 1 つ超えの行の対から、2 つの手順を作る（1 つ超えは DB が断る）</summary>
    private static IEnumerable<DialectTypeRealDbProbe> LimitPairs(
        string dialect,
        IEnumerable<(string AtRow, string OverRow, string OverSpelling)> pairs
    )
    {
        foreach (var (atRow, overRow, overSpelling) in pairs)
        {
            yield return new(atRow, dialect, DialectTypeRealDbProbeKinds.AtLimit);
            yield return new(
                overRow,
                dialect,
                DialectTypeRealDbProbeKinds.OverLimit,
                Spelling: overSpelling
            );
        }
    }

    /// <summary>
    /// SQL Server の浮動小数点（<c>float(1)</c>〜<c>float(24)</c> は SQL Server では <c>real</c>＝単精度で、ドライバは
    /// <see cref="float"/> を返す。マッパーが引数を見ずに <c>double</c> とすると、生成コードの速い読み取りが
    /// <see cref="InvalidCastException"/> になる）
    /// </summary>
    private static IEnumerable<DialectTypeRealDbProbe> SqlServerDrivers() =>
        [
            Driver(
                "map-sqlserver-float-mantissa-float-1",
                DialectNames.SqlServer,
                "1.5",
                typeof(float)
            ),
            Driver(
                "map-sqlserver-float-mantissa-float-24",
                DialectNames.SqlServer,
                "2.5",
                typeof(float)
            ),
            Driver(
                "map-sqlserver-float-mantissa-float-25",
                DialectNames.SqlServer,
                "3.5",
                typeof(double)
            ),
            Driver(
                "map-sqlserver-float-mantissa-float-53",
                DialectNames.SqlServer,
                "4.5",
                typeof(double)
            ),
            Driver(
                "map-sqlserver-float-mantissa-float",
                DialectNames.SqlServer,
                "5.5",
                typeof(double)
            ),
            Driver(
                "map-sqlserver-float-mantissa-real",
                DialectNames.SqlServer,
                "6.5",
                typeof(float)
            ),
        ];

    /// <summary>
    /// MySQL の <c>bit</c>・<c>year</c>・符号なし整数・<c>mediumint</c>・<c>tinytext</c> / <c>tinyblob</c>（取込は
    /// <c>COLUMN_TYPE</c> をそのまま表記に採る。<c>bit(1)</c> だけはドライバの <c>ulong</c> に対し、<c>tinyint(1)</c> と対の
    /// 真偽値慣習で <c>bool</c> へ寄せる意図的な非対称。MySQL 8.4 は符号なし tinyint から表示幅を落とすので、
    /// <c>tinyint(1) unsigned</c> は <c>tinyint unsigned</c> として取り込まれ、真偽値慣習の対象外であることが DB 側からも裏づけられる）
    /// </summary>
    private static IEnumerable<DialectTypeRealDbProbe> MySqlDrivers() =>
        [
            Driver(
                "map-mysql-single-bit-bool-bit-1",
                MySql,
                "b'1'",
                typeof(ulong),
                mapperType: "bool"
            ),
            Driver(
                "map-mysql-bit-year-driver-clr-bit-8",
                MySql,
                "b'10101010'",
                typeof(ulong),
                imported: "bit(8)"
            ),
            Driver("map-mysql-bit-year-driver-clr-bit-64", MySql, "b'1111'", typeof(ulong)),
            Driver(
                "map-mysql-bit-year-driver-clr-year",
                MySql,
                "2026",
                typeof(int),
                imported: "year"
            ),
            Driver("map-mysql-bit-year-driver-clr-year", MySql, "NULL", typeof(int)),
            Driver("map-mysql-signed-integer-tinyint", MySql, "-128", typeof(sbyte)),
            Driver("map-mysql-unsigned-integer-tinyint-unsigned", MySql, "255", typeof(byte)),
            Driver(
                "map-mysql-unsigned-tinyint-not-boolean-tinyint-1-unsigned",
                MySql,
                "1",
                typeof(byte),
                imported: "tinyint unsigned"
            ),
            Driver("map-mysql-unsigned-integer-smallint-unsigned", MySql, "65535", typeof(ushort)),
            Driver("map-mysql-mediumint-mediumint", MySql, "-8388608", typeof(int)),
            Driver(
                "map-mysql-unsigned-integer-mediumint-unsigned",
                MySql,
                "16777215",
                typeof(uint),
                imported: "mediumint unsigned"
            ),
            Driver(
                "map-mysql-unsigned-integer-int-unsigned",
                MySql,
                "4294967295",
                typeof(uint),
                imported: "int unsigned"
            ),
            Driver(
                "map-mysql-unsigned-integer-bigint-unsigned",
                MySql,
                "18446744073709551615",
                typeof(ulong)
            ),
            Driver(
                "map-mysql-unsigned-non-integer-decimal-10-2-unsigned",
                MySql,
                "1.25",
                typeof(decimal)
            ),
            Driver(
                "map-mysql-tinytext-string-without-fallback",
                MySql,
                "'abc'",
                typeof(string),
                imported: "tinytext"
            ),
            Driver(
                "map-mysql-binary-bytearray-tinyblob",
                MySql,
                "x'0102'",
                typeof(byte[]),
                imported: "tinyblob"
            ),
        ];

    /// <summary>
    /// Oracle の ANSI の <c>INTEGER</c> / <c>SMALLINT</c>（Oracle では <c>NUMBER(*,0)</c>＝精度なし・スケール 0 の列になり、
    /// ODP.NET は <see cref="decimal"/> で返す。20 桁の値を入れて、桁あふれせずに読めることも確かめる）
    /// </summary>
    private static IEnumerable<DialectTypeRealDbProbe> OracleDrivers() =>
        [
            Driver(
                "map-oracle-ansi-integer-decimal",
                DialectNames.Oracle,
                "12345678901234567890",
                typeof(decimal),
                imported: "NUMBER(*,0)"
            ),
            Driver(
                "map-oracle-ansi-smallint-decimal",
                DialectNames.Oracle,
                "7",
                typeof(decimal),
                imported: "NUMBER(*,0)"
            ),
        ];

    /// <summary>
    /// PostgreSQL の <c>time with time zone</c>（Npgsql は <see cref="DateTimeOffset"/> で返す。<see cref="TimeSpan"/> へ
    /// 写すと読み取り自体が失敗し、行の組み立ては全列をまとめて読むので、その列が 1 本あるだけで表が丸ごと使えない）と、
    /// 時間帯なしの <c>time</c>（取り込み直した表記もマッパーが <see cref="TimeSpan"/> へ写す。EF Core が <see cref="TimeSpan"/>
    /// で読み書きできる。Npgsql の <c>GetFieldType</c> は <see cref="TimeOnly"/> を返すので、ドライバの型とは比べない）
    /// </summary>
    private static IEnumerable<DialectTypeRealDbProbe> PostgreSqlDrivers() =>
        [
            Driver(
                "map-postgresql-timetz-words",
                PostgreSql,
                "'10:30:00+07'",
                typeof(DateTimeOffset),
                imported: "time with time zone"
            ),
            Driver(
                "map-postgresql-time-without-time-zone",
                PostgreSql,
                "'10:30:00'",
                driver: null,
                mapperType: "TimeSpan"
            ),
        ];

    /// <summary>
    /// SQLite の REAL アフィニティの列（ドライバは <see cref="double"/> で返す。単精度の <see cref="float"/> で受けると、
    /// 倍精度で格納された値の下位の桁が落ちる）
    /// </summary>
    private static IEnumerable<DialectTypeRealDbProbe> SqliteDrivers() =>
        new[] { "real", "float", "double" }.Select(slug =>
            Driver(
                $"map-sqlite-real-affinity-{slug}-double",
                DialectNames.Sqlite,
                "0.1234567890123",
                typeof(double),
                readBack: 0.1234567890123,
                narrowingLoses: true
            )
        );

    private static DialectTypeRealDbProbe Driver(
        string rowId,
        string dialect,
        string value,
        Type? driver,
        string? imported = null,
        string? mapperType = null,
        object? readBack = null,
        bool narrowingLoses = false
    ) =>
        new(
            rowId,
            dialect,
            DialectTypeRealDbProbeKinds.Driver,
            Imported: imported,
            Value: value,
            Driver: driver,
            MapperType: mapperType,
            ReadBack: readBack,
            NarrowingLoses: narrowingLoses
        );
}
