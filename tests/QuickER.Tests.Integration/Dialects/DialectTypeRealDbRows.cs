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

    /// <summary>
    /// 型マッパーの型：生成 EF Core のコードと同じ構成（列名と、マッパーが EF Core に教える列の型だけを指定し、精度・Unicode を指定しない）のモデルで、
    /// マッパーの型として入れた行を読み、同じ型の値を書いて読み戻せる
    /// </summary>
    public const string EfCore = "efcore";

    /// <summary>
    /// 型マッパーの型：列の型の範囲の端と外の値を、生成 EF Core のコードと同じ構成（EF Core の手順と同じモデル＋
    /// <c>AddGeneratedEfCoreRepositories</c> が足すインターセプター）で、書き込みのどの経路（SaveChanges の挿入・更新・一括挿入の形・
    /// ExecuteUpdate の SET）で書いても、DB が断るか同じ値で読み戻す（黙って別の値を保存しない）。範囲の中の値は同じ値で読み戻す
    /// </summary>
    public const string EfCoreWriteRange = "efcore-write-range";

    /// <summary>すべての種類（次元表の水準の並び）</summary>
    public static IReadOnlyList<string> All { get; } =
    [AtLimit, OverLimit, Driver, EfCore, EfCoreWriteRange];
}

/// <summary>書き込みの範囲の手順で書く値 1 つ</summary>
/// <param name="Value">マッパーの型の値</param>
/// <param name="Fits">列の型の範囲の中か（中なら同じ値で読み戻す。外なら DB が断るか同じ値で読み戻す）</param>
internal sealed record DialectTypeRealDbWrite(object Value, bool Fits);

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
/// <param name="ReadBack">
/// ドライバの型で読んだ値の期待（null なら値は見ない）。EF Core の手順では、マッパーの型として EF Core で読んだ値の期待（必須）
/// </param>
/// <param name="NarrowingLoses">読んだ <c>double</c> を <c>float</c> へ落とすと値が変わるか（入れた値が倍精度を要することの確認）</param>
/// <param name="Write">EF Core の手順で、マッパーの型として書いて読み戻す値</param>
/// <param name="GapReason">
/// 読み書きできないと分かっている組の理由（製品の候補＝P5。直す区切りがこの手順を通る形へ書き換える）。null なら通るべき手順
/// </param>
/// <param name="GapSymptom">
/// 読み書きできない組で、今出ている食い違いの文に含まれる文字列（出なくなったら手順が赤になり、直ったことを知らせる）
/// </param>
/// <param name="Writes">書き込みの範囲の手順で書く値（範囲の端と外）</param>
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
    bool NarrowingLoses = false,
    object? Write = null,
    string? GapReason = null,
    string? GapSymptom = null,
    IReadOnlyList<DialectTypeRealDbWrite>? Writes = null
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
/// マッパーの行は、読み取る経路が読み書きできる型が正（生成コードはマッパーの型で読む）。QuickER 版 Repository（ADO）が
/// 読む SQL Server・SQLite は、ドライバの実測の CLR 型と比べる（ドライバの手順）。行の入力の表記と取り込み直した表記の
/// どちらもマッパーが同じ型（代替の型でない）へ写し、ドライバの型で読めることを確かめる。
/// </para>
/// <para>
/// PostgreSQL・MySQL・Oracle の列を読むのは生成 EF Core のコードとそのプロバイダだけなので、マッパーの型とドライバの型が
/// 違う組は、EF Core の手順（生成 DbContext と同じ構成のモデル）で読み書きできることを確かめる。
/// 確かめたプロバイダは、Integration が持つ <c>UseOracle</c>（Oracle.EntityFrameworkCore）・<c>UseMySQL</c>
/// （MySql.EntityFrameworkCore＝ドライバは MySql.Data。Pomelo は確かめていない）・<c>UseNpgsql</c>・<c>UseSqlServer</c>・
/// <c>UseSqlite</c>。読み書きできないと分かっている組は、理由（<see cref="DialectTypeRealDbProbe.GapReason"/>）と今の症状
/// （<see cref="DialectTypeRealDbProbe.GapSymptom"/>）つきで残す（製品の候補＝P5。直す区切りが通る形へ書き換える）。
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
        .. OracleEfCore(),
        .. MySqlEfCore(),
        .. PostgreSqlEfCore(),
        .. SqlServerEfCore(),
        .. SqliteEfCore(),
        .. EfCoreWriteRanges(),
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
    /// 時間帯なしの <c>time</c>（取り込み直した表記もマッパーが <see cref="TimeSpan"/> へ写す。Npgsql の <c>GetFieldType</c> は
    /// <see cref="TimeOnly"/> を返すので、ドライバの型とは比べない。EF Core が <see cref="TimeSpan"/> で読み書きできることは
    /// <see cref="PostgreSqlEfCore"/> の手順が確かめる）
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

    /// <summary>
    /// Oracle の整数の <c>NUMBER(p)</c>（ODP.NET の <c>GetFieldType</c> はマッパーの型と違う。読むのは生成 EF Core のコードだけで、
    /// Oracle.EntityFrameworkCore はマッパーの型のまま境界の値を読み書きできる）
    /// </summary>
    private static IEnumerable<DialectTypeRealDbProbe> OracleEfCore() =>
        [
            EfCore("matrix-map-oracle-number-1-upper", DialectNames.Oracle, "1", true, false),
            EfCore(
                "matrix-map-oracle-number-3-upper",
                DialectNames.Oracle,
                "255",
                (byte)255,
                (byte)0
            ),
            EfCore(
                "matrix-map-oracle-number-5-upper",
                DialectNames.Oracle,
                "-32768",
                short.MinValue,
                short.MaxValue
            ),
            EfCore(
                "matrix-map-oracle-number-10-upper",
                DialectNames.Oracle,
                "2147483647",
                int.MaxValue,
                int.MinValue
            ),
            EfCore(
                "matrix-map-oracle-number-19-upper",
                DialectNames.Oracle,
                "9223372036854775807",
                long.MaxValue,
                long.MinValue
            ),
        ];

    /// <summary>
    /// MySQL の、マッパーの型とドライバの型が違う列と、マッパーをドライバの型へそろえた列（読むのは生成 EF Core のコードだけ。
    /// 確かめたのは MySql.EntityFrameworkCore）
    /// </summary>
    private static IEnumerable<DialectTypeRealDbProbe> MySqlEfCore() =>
        [
            // MySql.Data は year を Int16 で返す（MySqlConnector は Int32）が、EF Core は int のまま読み書きできる
            EfCore("map-mysql-bit-year-driver-clr-year", MySql, "2026", 2026, 2155),
            // ドライバは bit(1) を UInt64 で返すが、EF Core は bool のまま読み書きできる
            EfCore("map-mysql-single-bit-bool-bit-1", MySql, "b'1'", true, false),
            // timestamp は DateTime で読み書きする（両ドライバの型。DateTimeOffset で読むと MySql.Data が小数秒を落とす）。
            // 小数秒 6 桁が読んでも書いて読み戻しても残る
            EfCore(
                "matrix-map-mysql-timestamp-6",
                MySql,
                "'2026-01-02 03:04:05.123456'",
                new DateTime(2026, 1, 2, 3, 4, 5).AddTicks(1234560),
                new DateTime(2026, 5, 6, 7, 8, 9).AddTicks(1234560)
            ),
            EfCore(
                "map-mysql-timestamp-datetime-timestamp-3",
                MySql,
                "'2026-01-02 03:04:05.123'",
                new DateTime(2026, 1, 2, 3, 4, 5, 123),
                new DateTime(2026, 5, 6, 7, 8, 9, 456)
            ),
            // char(36) は Guid で読み書きする（両ドライバが既定で Guid として返す）。大文字の UUID も Guid として読める
            EfCore(
                "matrix-map-mysql-char-36",
                MySql,
                "'0F8FAD5B-D9CB-469F-A165-70867728950E'",
                Guid.Parse("0f8fad5b-d9cb-469f-a165-70867728950e"),
                Guid.Parse("1b4e28ba-2fa1-11d2-883f-0016d3cca427")
            ),
        ];

    /// <summary>
    /// PostgreSQL の、マッパーの型とドライバの型が違う列と、生成 DbContext が列の型（HasColumnType）を教える列（読むのは生成
    /// EF Core のコードだけ）。<c>date</c>・<c>timestamp</c>・<c>timetz</c>・<c>json</c>・<c>jsonb</c>・<c>xml</c> は、列の型を
    /// 教えないと Npgsql の EF Core のプロバイダの既定の対応で送られて書き込みを断られる
    /// </summary>
    private static IEnumerable<DialectTypeRealDbProbe> PostgreSqlEfCore() =>
        [
            // money は小数 2 桁を持つ（列の型の規則で丸める）。EF Core が money として読み書きできる
            EfCore("matrix-map-postgresql-money", PostgreSql, "1.23", 1.23m, 9876.54m),
            // Npgsql は time を TimeOnly で返すが、EF Core は TimeSpan のまま読み書きできる
            EfCore(
                "map-postgresql-time-without-time-zone",
                PostgreSql,
                "'10:30:00.123456'",
                new TimeSpan(0, 10, 30, 0).Add(TimeSpan.FromTicks(1234560)),
                new TimeSpan(0, 23, 59, 59).Add(TimeSpan.FromTicks(9999990))
            ),
            EfCore(
                "matrix-map-postgresql-time-6",
                PostgreSql,
                "'10:30:00.123456'",
                new TimeSpan(0, 10, 30, 0).Add(TimeSpan.FromTicks(1234560)),
                new TimeSpan(0, 1, 2, 3, 456)
            ),
            // Npgsql は timestamptz を DateTime で返すが、EF Core は DateTimeOffset のまま読み書きできる（+00:00 以外の値は
            // Npgsql が書き込みを断る＝Npgsql の仕様）
            EfCore(
                "matrix-map-postgresql-timestamptz-6",
                PostgreSql,
                "'2026-01-02 03:04:05.123456+00'",
                new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero).AddTicks(1234560),
                new DateTimeOffset(2026, 5, 6, 7, 8, 9, TimeSpan.Zero).AddTicks(1234560)
            ),
            EfCore(
                "matrix-map-postgresql-date",
                PostgreSql,
                "'2026-01-02'",
                new DateTime(2026, 1, 2),
                new DateTime(2026, 5, 6)
            ),
            EfCore(
                "matrix-map-postgresql-timestamp-6",
                PostgreSql,
                "'2026-01-02 03:04:05.123456'",
                new DateTime(2026, 1, 2, 3, 4, 5).AddTicks(1234560),
                new DateTime(2026, 5, 6, 7, 8, 9).AddTicks(1234560)
            ),
            EfCore(
                "matrix-map-postgresql-jsonb",
                PostgreSql,
                "'{\"a\": 1}'",
                "{\"a\": 1}",
                "{\"b\": 2}"
            ),
            EfCore("matrix-map-postgresql-xml", PostgreSql, "'<a>1</a>'", "<a>1</a>", "<b>2</b>"),
            EfCore(
                "map-postgresql-json-efcore-column-type",
                PostgreSql,
                "'{\"a\": 1}'",
                "{\"a\": 1}",
                "{\"b\": 2}"
            ),
            // 配列は要素の列の型に [] を付けて教える（教えないと DateTime[] は timestamptz[]・string[] は text[] で送られ断られる）
            EfCore(
                "map-postgresql-array-date-efcore-column-type",
                PostgreSql,
                "'{2026-01-02,2026-03-04}'",
                new[] { new DateTime(2026, 1, 2), new DateTime(2026, 3, 4) },
                new[] { new DateTime(2026, 5, 6) }
            ),
            EfCore(
                "map-postgresql-array-timestamp-6-efcore-column-type",
                PostgreSql,
                "'{\"2026-01-02 03:04:05.123456\"}'",
                new[] { new DateTime(2026, 1, 2, 3, 4, 5).AddTicks(1234560) },
                new[] { new DateTime(2026, 5, 6, 7, 8, 9).AddTicks(1234560) }
            ),
            EfCore(
                "map-postgresql-array-timetz-words-efcore-column-type",
                PostgreSql,
                "'{\"10:30:00+07\"}'",
                new[] { new DateTimeOffset(1, 1, 2, 10, 30, 0, TimeSpan.FromHours(7)) },
                new[] { new DateTimeOffset(1, 1, 2, 23, 4, 5, TimeSpan.FromHours(-5)) }
            ),
            EfCore(
                "map-postgresql-array-json-efcore-column-type",
                PostgreSql,
                "ARRAY['{\"a\": 1}']::json[]",
                new[] { "{\"a\": 1}" },
                new[] { "{\"b\": 2}" }
            ),
            EfCore(
                "map-postgresql-array-jsonb-efcore-column-type",
                PostgreSql,
                "ARRAY['{\"a\": 1}']::jsonb[]",
                new[] { "{\"a\": 1}" },
                new[] { "{\"b\": 2}" }
            ),
            EfCore(
                "map-postgresql-array-xml-efcore-column-type",
                PostgreSql,
                "ARRAY['<a>1</a>']::xml[]",
                new[] { "<a>1</a>" },
                new[] { "<b>2</b>" }
            ),
            // timetz は列の型を教えると UTC 以外のオフセットも書ける（教えないと timestamptz として送られ Npgsql が断る）。
            // Npgsql は日付を持たない timetz を 0001-01-02 の DateTimeOffset で返す
            EfCore(
                "map-postgresql-timetz-words",
                PostgreSql,
                "'10:30:00.123456+07'",
                new DateTimeOffset(1, 1, 2, 10, 30, 0, TimeSpan.FromHours(7)).AddTicks(1234560),
                new DateTimeOffset(1, 1, 2, 23, 4, 5, TimeSpan.FromHours(-5)).AddTicks(1234560)
            ),
        ];

    /// <summary>SQL Server の EF Core の経路（マッパーの型はドライバの型と同じ。EF Core 単独の生成でも同じ型で読み書きできる）</summary>
    private static IEnumerable<DialectTypeRealDbProbe> SqlServerEfCore() =>
        [
            EfCore(
                "map-sqlserver-float-mantissa-float-24",
                DialectNames.SqlServer,
                "2.5",
                2.5f,
                1.5f
            ),
            EfCore(
                "matrix-map-sqlserver-datetimeoffset-6",
                DialectNames.SqlServer,
                "'2026-01-02 03:04:05.123456 +09:00'",
                new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.FromHours(9)).AddTicks(1234560),
                new DateTimeOffset(2026, 5, 6, 7, 8, 9, TimeSpan.FromHours(-5)).AddTicks(1234560)
            ),
            // money・smallmoney は小数 4 桁を持つ。EF Core が列の型を知らないと既定の decimal(18,2) で書き、4 桁目まで落とす
            EfCore(
                "matrix-map-sqlserver-money",
                DialectNames.SqlServer,
                "1.2345",
                1.2345m,
                9876.5432m
            ),
            EfCore(
                "map-sqlserver-sqldb-smallmoney",
                DialectNames.SqlServer,
                "1.2345",
                1.2345m,
                9876.5432m
            ),
        ];

    /// <summary>SQLite の EF Core の経路（REAL アフィニティの倍精度・BIT の真偽値）</summary>
    private static IEnumerable<DialectTypeRealDbProbe> SqliteEfCore() =>
        [
            EfCore(
                "map-sqlite-real-affinity-real-double",
                DialectNames.Sqlite,
                "0.1234567890123",
                0.1234567890123,
                1.25
            ),
            EfCore("matrix-map-sqlite-bit-upper", DialectNames.Sqlite, "1", true, false),
        ];

    /// <summary>
    /// TimeSpan の列の型の範囲の端と外の値の書き込み（範囲は docs の TimeSpan の段落：SQL Server の time は 0〜24 時間未満、
    /// PostgreSQL の time は 0〜24:00:00、MySQL の time は ±838:59:59、SQLite は文字列で持ち範囲を持たない）。値はマイクロ秒の粒度
    /// （PostgreSQL・MySQL・time(6) が端数を落とさない）。SQLite だけは 100 ナノ秒の端数も持つ
    /// </summary>
    /// <remarks>
    /// Oracle は TimeSpan へ写す型を持たない（型マッパーに TIME が無く、INTERVAL DAY TO SECOND も TimeSpan に写さない）ので手順を持たない
    /// </remarks>
    private static IEnumerable<DialectTypeRealDbProbe> EfCoreWriteRanges()
    {
        var micro = TimeSpan.FromTicks(10);
        var clock = new TimeSpan(0, 10, 30, 0).Add(TimeSpan.FromTicks(1234560));
        var day = TimeSpan.FromDays(1);
        var dayAndHour = new TimeSpan(1, 1, 0, 0);
        var hour = TimeSpan.FromHours(1);
        var mySqlMax = new TimeSpan(34, 22, 59, 59);
        var mySqlBeyond = new TimeSpan(34, 23, 0, 0);

        return
        [
            WriteRange(
                "map-postgresql-time-plain",
                PostgreSql,
                Fits(TimeSpan.Zero, clock, day),
                Beyond(day + micro, dayAndHour, -micro, -hour, mySqlMax)
            ),
            WriteRange(
                "map-postgresql-array-time-efcore-column-type",
                PostgreSql,
                Fits(new[] { hour, clock }, new[] { day }),
                Beyond(new[] { hour, dayAndHour }, new[] { -hour })
            ),
            WriteRange(
                "matrix-map-sqlserver-time-6",
                DialectNames.SqlServer,
                Fits(TimeSpan.Zero, clock, day - micro),
                Beyond(day, dayAndHour, -micro)
            ),
            WriteRange(
                "matrix-map-mysql-time-6",
                MySql,
                Fits(TimeSpan.Zero, clock, dayAndHour, -hour, mySqlMax, -mySqlMax),
                Beyond(mySqlBeyond, -mySqlBeyond)
            ),
            WriteRange(
                "matrix-map-sqlite-time-6-upper",
                DialectNames.Sqlite,
                Fits(
                    TimeSpan.Zero,
                    TimeSpan.FromHours(10).Add(TimeSpan.FromTicks(1)),
                    dayAndHour,
                    -hour,
                    mySqlBeyond,
                    -mySqlBeyond
                ),
                []
            ),
        ];

        static DialectTypeRealDbWrite[] Fits(params object[] values) =>
            [.. values.Select(value => new DialectTypeRealDbWrite(value, Fits: true))];

        static DialectTypeRealDbWrite[] Beyond(params object[] values) =>
            [.. values.Select(value => new DialectTypeRealDbWrite(value, Fits: false))];
    }

    private static DialectTypeRealDbProbe WriteRange(
        string rowId,
        string dialect,
        DialectTypeRealDbWrite[] fits,
        DialectTypeRealDbWrite[] beyond
    ) =>
        new(
            rowId,
            dialect,
            DialectTypeRealDbProbeKinds.EfCoreWriteRange,
            Writes: [.. fits, .. beyond]
        );

    private static DialectTypeRealDbProbe EfCore(
        string rowId,
        string dialect,
        string value,
        object readBack,
        object write,
        string? gapReason = null,
        string? gapSymptom = null
    ) =>
        new(
            rowId,
            dialect,
            DialectTypeRealDbProbeKinds.EfCore,
            Value: value,
            ReadBack: readBack,
            Write: write,
            GapReason: gapReason,
            GapSymptom: gapSymptom
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
