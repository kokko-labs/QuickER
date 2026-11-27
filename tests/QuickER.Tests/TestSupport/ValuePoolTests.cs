using System.Text.RegularExpressions;
using AwesomeAssertions;
using QuickER.Tests.TestSupport.Engines;
using QuickER.Tests.TestSupport.ValuePools;

namespace QuickER.Tests.TestSupport;

/// <summary>判定式の境目の表の 1 行（水準・値・列の相・当たるか）</summary>
/// <param name="Edge">水準の Id</param>
/// <param name="Value">値</param>
/// <param name="Column">列の相（相の要らない水準は null）</param>
/// <param name="Hits">その水準に当たるか（当たらない行は境目の 1 つ外の値）</param>
public sealed record ValueEdgeBoundary(string Edge, object Value, ValueColumn? Column, bool Hits);

/// <summary>今のプールの値の分類の表の 1 行（値・列の相・当たる水準の集合）</summary>
/// <param name="Value">値</param>
/// <param name="Column">列の相（値の束縛の生成の表がその値と比べる列の型）</param>
/// <param name="Expected">当たる水準の Id の集合（ちょうど一致）</param>
public sealed record PoolValueClassification(object Value, ValueColumn? Column, string[] Expected);

/// <summary>
/// 値のプールの水準の名簿と分類（<see cref="ValueEdges"/>）の自己検査：名簿の形・代表の値・判定式の境目・今のプールの値の分類
/// </summary>
/// <remarks>
/// テストの土台の自己検査（src を守る表ではない）なので、影響の地図の登録しなくてよいソースの一覧（<c>ImpactMapSources.ScenarioTableExemptions</c>）に載せる。
/// </remarks>
public sealed class ValuePoolTests
{
    private static readonly DateTime Day = new(2024, 1, 1);

    private static readonly ValueColumn DateTime2Of2 = ValueColumn.FromSqlType("datetime2(2)");

    private static readonly ValueColumn Offset2 = ValueColumn.FromSqlType("datetimeoffset(2)");

    private static readonly ValueColumn Time2 = ValueColumn.FromSqlType("time(2)");

    private static readonly ValueColumn Decimal10Of2 = ValueColumn.FromSqlType("decimal(10,2)");

    private static readonly ValueColumn Money = ValueColumn.FromSqlType("money");

    private static readonly ValueColumn IntColumn = ValueColumn.FromSqlType("int");

    private static readonly ValueColumn BigIntColumn = ValueColumn.FromSqlType("bigint");

    private static readonly ValueColumn Varchar4 = ValueColumn.FromSqlType("varchar(4)");

    private static readonly ValueColumn Binary4 = ValueColumn.FromSqlType("binary(4)");

    private static readonly ValueColumn ToInt = new() { CastTarget = typeof(int) };

    private static readonly ValueColumn ToUInt = new() { CastTarget = typeof(uint) };

    /// <summary>系統 → Id の接頭辞</summary>
    private static readonly Dictionary<ValueFamily, string> Prefixes = new()
    {
        [ValueFamily.DateTime] = "dt-",
        [ValueFamily.DateTimeOffset] = "dto-",
        [ValueFamily.TimeSpan] = "ts-",
        [ValueFamily.Decimal] = "dec-",
        [ValueFamily.Integer] = "int-",
        [ValueFamily.Floating] = "fp-",
        [ValueFamily.String] = "str-",
        [ValueFamily.Binary] = "bin-",
        [ValueFamily.Guid] = "guid-",
        [ValueFamily.Boolean] = "bool-",
        [ValueFamily.Narrowing] = "cast-",
    };

    private static ScenarioRow<ValueEdgeBoundary> Hit(
        string edge,
        int index,
        object value,
        ValueColumn? column = null
    ) => new($"{edge}-hit-{index}", null, $"{edge} に当たる値", new(edge, value, column, true));

    private static ScenarioRow<ValueEdgeBoundary> Miss(
        string edge,
        int index,
        object value,
        ValueColumn? column = null
    ) =>
        new(
            $"{edge}-miss-{index}",
            null,
            $"{edge} の境目の 1 つ外の値（当たらない）",
            new(edge, value, column, false)
        );

    /// <summary>判定式の境目の表</summary>
    internal static ScenarioTable<ValueEdgeBoundary> Boundaries { get; } =
        new(
            "ValuePools",
            [
                Hit("dt-kind-utc", 1, DateTime.SpecifyKind(Day, DateTimeKind.Utc)),
                Miss("dt-kind-utc", 1, Day),
                Hit("dt-kind-local", 1, DateTime.SpecifyKind(Day, DateTimeKind.Local)),
                Miss("dt-kind-local", 1, DateTime.SpecifyKind(Day, DateTimeKind.Utc)),
                Hit("dt-midnight", 1, Day),
                Miss("dt-midnight", 1, Day.AddTicks(1)),
                Hit("dt-time-of-day", 1, Day.AddTicks(1)),
                Miss("dt-time-of-day", 1, Day),
                Hit("dt-tick-before-midnight", 1, Day.AddDays(1).AddTicks(-1)),
                Miss("dt-tick-before-midnight", 1, Day.AddDays(1).AddTicks(-2)),
                Hit("dt-sub-microsecond", 1, Day.AddTicks(1)),
                Miss("dt-sub-microsecond", 1, Day.AddTicks(10)),
                Hit("dt-beyond-column-scale", 1, Day.AddTicks(1), DateTime2Of2),
                Miss("dt-beyond-column-scale", 1, Day.AddTicks(100_000), DateTime2Of2),
                Hit("dt-legacy-read-back", 1, Day.AddMilliseconds(3)),
                Hit("dt-legacy-read-back", 2, Day.AddMilliseconds(7)),
                Miss("dt-legacy-read-back", 1, Day.AddMilliseconds(3).AddTicks(1)),
                Miss("dt-legacy-read-back", 2, Day.AddMilliseconds(10)),
                Hit("dt-legacy-off-grid", 1, Day.AddMilliseconds(4)),
                Hit("dt-legacy-off-grid", 2, Day.AddMilliseconds(3).AddTicks(1)),
                Miss("dt-legacy-off-grid", 1, Day.AddMilliseconds(7)),
                Hit("dt-legacy-between-read-back-and-step", 1, Day.AddTicks(30_001)),
                Hit("dt-legacy-between-read-back-and-step", 2, Day.AddTicks(33_333)),
                Hit("dt-legacy-between-read-back-and-step", 3, Day.AddTicks(66_667)),
                Hit("dt-legacy-between-read-back-and-step", 4, Day.AddTicks(69_999)),
                Miss("dt-legacy-between-read-back-and-step", 1, Day.AddTicks(30_000)),
                Miss("dt-legacy-between-read-back-and-step", 2, Day.AddTicks(33_334)),
                Miss("dt-legacy-between-read-back-and-step", 3, Day.AddTicks(66_666)),
                Miss("dt-legacy-between-read-back-and-step", 4, Day.AddTicks(70_000)),
                Hit("dt-small-off-minute", 1, Day.AddSeconds(1)),
                Miss("dt-small-off-minute", 1, Day.AddMinutes(1)),
                Hit("dt-small-rounding-boundary", 1, Day.AddMilliseconds(29_998)),
                Hit("dt-small-rounding-boundary", 2, Day.AddSeconds(30)),
                Miss("dt-small-rounding-boundary", 1, Day.AddMilliseconds(29_998).AddTicks(-1)),
                Miss("dt-small-rounding-boundary", 2, Day.AddSeconds(30).AddTicks(1)),
                Hit("dt-before-sql-datetime-min", 1, ValueEdges.SqlDateTimeMin.AddTicks(-1)),
                Miss("dt-before-sql-datetime-min", 1, ValueEdges.SqlDateTimeMin),
                Hit("dt-before-smalldatetime-min", 1, ValueEdges.SmallDateTimeMin.AddTicks(-1)),
                Miss("dt-before-smalldatetime-min", 1, ValueEdges.SmallDateTimeMin),
                Hit("dt-past-sql-datetime-max", 1, ValueEdges.SqlDateTimeMax.AddTicks(1)),
                Miss("dt-past-sql-datetime-max", 1, ValueEdges.SqlDateTimeMax),
                Hit("dt-past-smalldatetime-max", 1, ValueEdges.SmallDateTimeMax.AddTicks(1)),
                Miss("dt-past-smalldatetime-max", 1, ValueEdges.SmallDateTimeMax),
                Hit("dt-sql-datetime-range-end", 1, ValueEdges.SqlDateTimeMin),
                Hit("dt-sql-datetime-range-end", 2, ValueEdges.SqlDateTimeMax),
                Miss("dt-sql-datetime-range-end", 1, ValueEdges.SqlDateTimeMin.AddTicks(1)),
                Hit("dt-smalldatetime-range-end", 1, ValueEdges.SmallDateTimeMin),
                Hit("dt-smalldatetime-range-end", 2, ValueEdges.SmallDateTimeMax),
                Miss("dt-smalldatetime-range-end", 1, ValueEdges.SmallDateTimeMax.AddTicks(-1)),
                Hit("dt-calendar-end", 1, new DateTime(9999, 1, 1)),
                Miss("dt-calendar-end", 1, new DateTime(9998, 12, 31)),
                Hit("dt-leap-day", 1, new DateTime(2024, 2, 29)),
                Miss("dt-leap-day", 1, new DateTime(2024, 2, 28)),
                Hit("dto-offset-zero", 1, new DateTimeOffset(Day, TimeSpan.Zero)),
                Miss("dto-offset-zero", 1, new DateTimeOffset(Day, TimeSpan.FromMinutes(1))),
                Hit("dto-offset-positive", 1, new DateTimeOffset(Day, TimeSpan.FromMinutes(1))),
                Miss("dto-offset-positive", 1, new DateTimeOffset(Day, TimeSpan.Zero)),
                Hit("dto-offset-negative", 1, new DateTimeOffset(Day, TimeSpan.FromMinutes(-1))),
                Miss("dto-offset-negative", 1, new DateTimeOffset(Day, TimeSpan.Zero)),
                Hit("dto-offset-extreme", 1, new DateTimeOffset(Day, TimeSpan.FromHours(12))),
                Hit("dto-offset-extreme", 2, new DateTimeOffset(Day, TimeSpan.FromHours(-12))),
                Miss("dto-offset-extreme", 1, new DateTimeOffset(Day, new TimeSpan(11, 59, 0))),
                Hit(
                    "dto-local-date-differs",
                    1,
                    new DateTimeOffset(Day.AddHours(5), TimeSpan.FromHours(9))
                ),
                Miss(
                    "dto-local-date-differs",
                    1,
                    new DateTimeOffset(Day.AddHours(9), TimeSpan.FromHours(9))
                ),
                Hit("dto-sub-microsecond", 1, new DateTimeOffset(Day.AddTicks(1), TimeSpan.Zero)),
                Miss("dto-sub-microsecond", 1, new DateTimeOffset(Day.AddTicks(10), TimeSpan.Zero)),
                Hit(
                    "dto-beyond-column-scale",
                    1,
                    new DateTimeOffset(Day.AddTicks(1), TimeSpan.Zero),
                    Offset2
                ),
                Miss(
                    "dto-beyond-column-scale",
                    1,
                    new DateTimeOffset(Day.AddTicks(100_000), TimeSpan.Zero),
                    Offset2
                ),
                Hit("dto-calendar-end", 1, new DateTimeOffset(1, 1, 2, 0, 0, 0, TimeSpan.Zero)),
                Miss("dto-calendar-end", 1, new DateTimeOffset(2, 1, 2, 0, 0, 0, TimeSpan.Zero)),
                Hit("ts-zero", 1, TimeSpan.Zero),
                Miss("ts-zero", 1, TimeSpan.FromTicks(1)),
                Hit("ts-negative", 1, TimeSpan.FromTicks(-1)),
                Miss("ts-negative", 1, TimeSpan.Zero),
                Hit("ts-day-end", 1, TimeSpan.FromDays(1) - TimeSpan.FromTicks(10)),
                Hit("ts-day-end", 2, TimeSpan.FromDays(1) - TimeSpan.FromTicks(1)),
                Miss("ts-day-end", 1, TimeSpan.FromDays(1) - TimeSpan.FromTicks(11)),
                Miss("ts-day-end", 2, TimeSpan.FromDays(1)),
                Hit("ts-one-day", 1, TimeSpan.FromDays(1)),
                Miss("ts-one-day", 1, TimeSpan.FromDays(1) + TimeSpan.FromTicks(1)),
                Hit("ts-beyond-day", 1, TimeSpan.FromDays(1) + TimeSpan.FromTicks(1)),
                Miss("ts-beyond-day", 1, TimeSpan.FromDays(1)),
                Hit("ts-sub-microsecond", 1, TimeSpan.FromTicks(1)),
                Miss("ts-sub-microsecond", 1, TimeSpan.FromTicks(10)),
                Hit("ts-beyond-column-scale", 1, TimeSpan.FromTicks(1), Time2),
                Miss("ts-beyond-column-scale", 1, TimeSpan.FromTicks(100_000), Time2),
                Hit("ts-mysql-range-end", 1, ValueEdges.MySqlTimeMax),
                Hit("ts-mysql-range-end", 2, -ValueEdges.MySqlTimeMax),
                Miss("ts-mysql-range-end", 1, ValueEdges.MySqlTimeMax - TimeSpan.FromTicks(1)),
                Hit("ts-beyond-mysql-range", 1, ValueEdges.MySqlTimeMax + TimeSpan.FromTicks(1)),
                Hit("ts-beyond-mysql-range", 2, -ValueEdges.MySqlTimeMax - TimeSpan.FromTicks(1)),
                Miss("ts-beyond-mysql-range", 1, ValueEdges.MySqlTimeMax),
                Hit("dec-zero", 1, 0m),
                Miss("dec-zero", 1, 0.01m),
                Hit("dec-negative", 1, -0.01m),
                Miss("dec-negative", 1, 0m),
                Hit("dec-fraction", 1, 1.5m),
                Miss("dec-fraction", 1, 1.0m),
                Hit("dec-beyond-long", 1, 9223372036854775808m),
                Hit("dec-beyond-long", 2, -9223372036854775809m),
                Miss("dec-beyond-long", 1, 9223372036854775807m),
                Hit("dec-type-end", 1, decimal.MaxValue),
                Hit("dec-type-end", 2, decimal.MinValue),
                Miss("dec-type-end", 1, decimal.MaxValue - 1m),
                Hit("dec-beyond-column-scale", 1, 1.234m, Decimal10Of2),
                Miss("dec-beyond-column-scale", 1, 1.230m, Decimal10Of2),
                Hit("dec-beyond-column-range", 1, 100000000m, Decimal10Of2),
                Hit("dec-beyond-column-range", 2, 922337203685477.5808m, Money),
                Miss("dec-beyond-column-range", 1, 99999999.99m, Decimal10Of2),
                Miss("dec-beyond-column-range", 2, 922337203685477.5807m, Money),
                Hit("dec-column-range-end", 1, 99999999.99m, Decimal10Of2),
                Hit("dec-column-range-end", 2, -99999999.99m, Decimal10Of2),
                Hit("dec-column-range-end", 3, 922337203685477.5807m, Money),
                Miss("dec-column-range-end", 1, 99999999.98m, Decimal10Of2),
                Hit("dec-smallest-unit", 1, 0.01m, Decimal10Of2),
                Hit("dec-smallest-unit", 2, -0.01m, Decimal10Of2),
                Miss("dec-smallest-unit", 1, 0.02m, Decimal10Of2),
                Hit("int-zero", 1, 0),
                Miss("int-zero", 1, 1),
                Hit("int-negative", 1, -1),
                Miss("int-negative", 1, 0),
                Hit("int-type-min", 1, int.MinValue),
                Hit("int-type-min", 2, long.MinValue),
                Miss("int-type-min", 1, int.MinValue + 1),
                Hit("int-type-max", 1, int.MaxValue),
                Hit("int-type-max", 2, (byte)255),
                Miss("int-type-max", 1, int.MaxValue - 1),
                Hit("int-sign-boundary", 1, (byte)0x80),
                Hit("int-sign-boundary", 2, 0x8000_0000u),
                Hit("int-sign-boundary", 3, 0x8000_0000_0000_0000ul),
                Miss("int-sign-boundary", 1, (byte)0x7F),
                Miss("int-sign-boundary", 2, 0x7FFF_FFFFu),
                Miss("int-sign-boundary", 3, -1),
                Hit("int-beyond-column-range", 1, 2147483648L, IntColumn),
                Hit("int-beyond-column-range", 2, -2147483649L, IntColumn),
                Miss("int-beyond-column-range", 1, 2147483647L, IntColumn),
                Hit("int-beyond-float-precision", 1, 16_777_217),
                Miss("int-beyond-float-precision", 1, 16_777_216),
                Hit("int-beyond-double-precision", 1, 9_007_199_254_740_993L),
                Miss("int-beyond-double-precision", 1, 9_007_199_254_740_992L),
                Hit("fp-negative-zero", 1, -0.0),
                Hit("fp-negative-zero", 2, -0.0f),
                Miss("fp-negative-zero", 1, 0.0),
                Hit("fp-subnormal", 1, double.Epsilon),
                Miss("fp-subnormal", 1, 2.2250738585072014E-308),
                Hit("fp-type-end", 1, double.MaxValue),
                Hit("fp-type-end", 2, float.MinValue),
                Miss("fp-type-end", 1, Math.BitDecrement(double.MaxValue)),
                Hit("fp-non-dyadic", 1, 0.1),
                Miss("fp-non-dyadic", 1, 1.0 / 1024),
                Hit("fp-integer-beyond-2pow24", 1, 16_777_216f),
                Miss("fp-integer-beyond-2pow24", 1, 16_777_215f),
                Miss("fp-integer-beyond-2pow24", 2, 16_777_216d),
                Hit("fp-integer-beyond-2pow53", 1, 9_007_199_254_740_992d),
                Miss("fp-integer-beyond-2pow53", 1, 9_007_199_254_740_991d),
                Hit("fp-beyond-float", 1, 0.1d),
                Miss("fp-beyond-float", 1, 0.5d),
                Miss("fp-beyond-float", 2, 0.1f),
                Hit("str-empty", 1, ""),
                Miss("str-empty", 1, " "),
                Hit("str-non-ascii", 1, "\u0080"),
                Miss("str-non-ascii", 1, "\u007f"),
                Hit("str-non-latin1", 1, "Ā"),
                Miss("str-non-latin1", 1, "ÿ"),
                Hit("str-surrogate", 1, "😀"),
                Miss("str-surrogate", 1, "￿"),
                Hit("str-leading-space", 1, " a"),
                Miss("str-leading-space", 1, "a "),
                Hit("str-trailing-space", 1, "a "),
                Miss("str-trailing-space", 1, " a"),
                Hit("str-other-whitespace", 1, "a\tb"),
                Hit("str-other-whitespace", 2, "\u3000"),
                Miss("str-other-whitespace", 1, "a b"),
                Hit("str-like-wildcard", 1, "a%"),
                Hit("str-like-wildcard", 2, "a_"),
                Miss("str-like-wildcard", 1, "a-"),
                Hit("str-quote", 1, "'"),
                Miss("str-quote", 1, "\""),
                Hit("str-beyond-column-length", 1, "abcde", Varchar4),
                Miss("str-beyond-column-length", 1, "abcd", Varchar4),
                Hit("str-exact-column-length", 1, "abcd", Varchar4),
                Miss("str-exact-column-length", 1, "abc", Varchar4),
                Miss("str-exact-column-length", 2, "abcde", Varchar4),
                Hit("bin-empty", 1, Array.Empty<byte>()),
                Miss("bin-empty", 1, new byte[] { 0 }),
                Hit("bin-trailing-zero", 1, new byte[] { 1, 0 }),
                Miss("bin-trailing-zero", 1, new byte[] { 0 }),
                Miss("bin-trailing-zero", 2, new byte[] { 0, 1 }),
                Hit("bin-leading-zero", 1, new byte[] { 0, 1 }),
                Miss("bin-leading-zero", 1, new byte[] { 0 }),
                Miss("bin-leading-zero", 2, new byte[] { 1, 0 }),
                Hit("bin-beyond-column-length", 1, new byte[] { 1, 2, 3, 4, 5 }, Binary4),
                Miss("bin-beyond-column-length", 1, new byte[] { 1, 2, 3, 4 }, Binary4),
                Hit("bin-exact-column-length", 1, new byte[] { 1, 2, 3, 4 }, Binary4),
                Miss("bin-exact-column-length", 1, new byte[] { 1, 2, 3 }, Binary4),
                Hit("bin-zero-excess", 1, new byte[] { 1, 2, 3, 4, 0 }, Binary4),
                Miss("bin-zero-excess", 1, new byte[] { 1, 2, 3, 4, 1 }, Binary4),
                Miss("bin-zero-excess", 2, new byte[] { 1, 2, 3, 4 }, Binary4),
                Hit("bin-long", 1, new byte[ValueEdges.LongBinaryLength]),
                Miss("bin-long", 1, new byte[ValueEdges.LongBinaryLength - 1]),
                Hit("guid-empty", 1, Guid.Empty),
                Miss("guid-empty", 1, new Guid("00000000-0000-0000-0000-000000000001")),
                Hit("guid-max", 1, Guid.AllBitsSet),
                Miss("guid-max", 1, Guid.Empty),
                Hit("bool-true", 1, true),
                Miss("bool-true", 1, false),
                Hit("bool-false", 1, false),
                Miss("bool-false", 1, true),
                Hit("cast-keeps", 1, 1m, ToInt),
                Miss("cast-keeps", 1, 1.5m, ToInt),
                Miss("cast-keeps", 2, 4294967297m, ToInt),
                Hit("cast-cuts-fraction", 1, 1.5m, ToInt),
                Miss("cast-cuts-fraction", 1, 2m, ToInt),
                Hit("cast-beyond-range", 1, 2147483648L, ToInt),
                Hit("cast-beyond-range", 2, 2147483648.0, ToInt),
                Miss("cast-beyond-range", 1, 2147483647L, ToInt),
                Hit("cast-sign-only", 1, -1, ToUInt),
                Miss("cast-sign-only", 1, -1L, ToUInt),
                Miss("cast-sign-only", 2, 1, ToUInt),
            ]
        );

    /// <summary>今のプールの値の分類の表（行 Id は「プール-名前」）</summary>
    internal static ScenarioTable<PoolValueClassification> PoolValues { get; } =
        new(
            "ValuePools",
            [
                .. DateTimePool
                    .Around(Day)
                    .Select(value => new ScenarioRow<PoolValueClassification>(
                        $"datetime-{value.Name}",
                        null,
                        value.Why,
                        new(value.Value, null, DateTimeExpected[value.Name])
                    )),
                .. IntegerPool
                    .Around(1, int.MaxValue)
                    .Select(value => new ScenarioRow<PoolValueClassification>(
                        $"integer-{value.Name}",
                        null,
                        value.Why,
                        new(
                            value.Value,
                            IntColumn,
                            value.Name == IntegerPool.BeyondColumnRange
                                ? ["int-beyond-column-range"]
                                : []
                        )
                    )),
                .. DecimalPool
                    .Around(100m, 2)
                    .Select(value => new ScenarioRow<PoolValueClassification>(
                        $"decimal-{value.Name}",
                        null,
                        value.Why,
                        new(value.Value, Decimal10Of2, DecimalExpected[value.Name])
                    )),
                new(
                    "decimal-beyond-long",
                    null,
                    DecimalPool.BeyondLong.Why,
                    new(
                        DecimalPool.BeyondLong.Value,
                        BigIntColumn,
                        ["dec-beyond-long", "dec-beyond-column-range"]
                    )
                ),
                new(
                    "decimal-integral-decimal",
                    null,
                    "整数の値の decimal",
                    new(DecimalPool.IntegralDecimalOf(100m).Value, Decimal10Of2, [])
                ),
                Named(
                    DecimalPool.BeyondPrecisionOf(10, 2),
                    Decimal10Of2,
                    ["dec-fraction", "dec-beyond-column-range"]
                ),
                Named(
                    DecimalPool.BelowLong,
                    BigIntColumn,
                    ["dec-negative", "dec-beyond-long", "dec-beyond-column-range"],
                    "bigint"
                ),
                Named(
                    DecimalPool.BeyondDoubleFractionValue,
                    BigIntColumn,
                    ["dec-fraction", "dec-beyond-column-scale"]
                ),
                Named(
                    DecimalPool.NearLongMaxFractionValue,
                    BigIntColumn,
                    ["dec-fraction", "dec-beyond-column-scale"]
                ),
                Named(
                    IntegerPool.TypeMinOf(int.MinValue),
                    IntColumn,
                    ["int-negative", "int-type-min"],
                    "int",
                    value => (int)value
                ),
                Named(
                    IntegerPool.BelowTypeMinOf(int.MinValue),
                    IntColumn,
                    ["int-negative", "int-beyond-column-range", "int-beyond-float-precision"],
                    "int"
                ),
                Named(
                    IntegerPool.SignBoundaryOf(typeof(uint)),
                    null,
                    ["int-sign-boundary"],
                    "uint",
                    value => (uint)value
                ),
                Named(
                    IntegerPool.UnsignedMaxOf(typeof(uint)),
                    null,
                    ["int-type-max", "int-sign-boundary", "int-beyond-float-precision"],
                    "uint",
                    value => (uint)value
                ),
                .. NarrowingPool
                    .Around(1m, 4294967297m)
                    .Select(value => new ScenarioRow<PoolValueClassification>(
                        $"narrowing-{value.Name}",
                        null,
                        value.Why,
                        new(value.Value, ToInt, NarrowingExpected[value.Name])
                    )),
                .. EdgeConstantRows(),
                .. BooleanPool.Values.Select(value => new ScenarioRow<PoolValueClassification>(
                    $"boolean-{value.Name}",
                    null,
                    value.Why,
                    new(value.Value, null, [value.Name])
                )),
            ]
        );

    /// <summary>名前つきの値 1 つの行（行 Id は「named-名前」か「named-名前-列」。<c>Around</c> の並びに入らない値）</summary>
    private static ScenarioRow<PoolValueClassification> Named<T>(
        PoolValue<T> value,
        ValueColumn? column,
        string[] expected,
        string? columnSuffix = null,
        Func<T, object>? asBound = null
    ) =>
        new(
            columnSuffix is null ? $"named-{value.Name}" : $"named-{value.Name}-{columnSuffix}",
            null,
            value.Why,
            new(asBound is null ? value.Value! : asBound(value.Value), column, expected)
        );

    private static ScenarioRow<PoolValueClassification> EdgeConstant(
        string id,
        object value,
        string[] expected,
        string why
    ) => new($"edge-constant-{id}", null, why, new(value, null, expected));

    /// <summary>
    /// 端の名前つきの定数（<c>DateTimeEdges</c>・<c>TimeSpanEdges</c>・<c>FloatingEdges</c>・<c>PowerOfTwoEdges</c>・<c>TextEdges</c>）が、
    /// XmlDoc に書いた水準へ分類される行（列の相が要る水準は含めない）
    /// </summary>
    private static IEnumerable<ScenarioRow<PoolValueClassification>> EdgeConstantRows() =>
        [
            EdgeConstant(
                "sql-datetime-max-rounding-down",
                DateTimeEdges.SqlDateTimeMaxRoundingDown,
                PastMaxExpected,
                "datetime の範囲の最後の 1 ミリ秒後"
            ),
            EdgeConstant(
                "sql-datetime-max-rounding-overflow",
                DateTimeEdges.SqlDateTimeMaxRoundingOverflow,
                PastMaxExpected,
                "datetime の範囲の最後の 2 ミリ秒後"
            ),
            EdgeConstant(
                "day-before-sql-datetime-min",
                DateTimeEdges.DayBeforeSqlDateTimeMin,
                ["dt-midnight", "dt-before-sql-datetime-min", "dt-before-smalldatetime-min"],
                "datetime の範囲の最初の 1 日前"
            ),
            EdgeConstant(
                "sql-datetime-min-rounding-up",
                DateTimeEdges.SqlDateTimeMinRoundingUp,
                BeforeMinWithTimeExpected,
                "datetime の範囲の最初の 1 ミリ秒前"
            ),
            EdgeConstant(
                "smalldatetime-max-rounding-down",
                DateTimeEdges.SmallDateTimeMaxRoundingDown,
                ["dt-time-of-day", "dt-small-off-minute", "dt-past-smalldatetime-max"],
                "smalldatetime の範囲の最後の 20 秒後"
            ),
            EdgeConstant(
                "day-after-smalldatetime-max",
                DateTimeEdges.DayAfterSmallDateTimeMax,
                ["dt-midnight", "dt-past-smalldatetime-max"],
                "smalldatetime の範囲の最後の日の翌日"
            ),
            EdgeConstant(
                "minute-before-smalldatetime-min",
                DateTimeEdges.MinuteBeforeSmallDateTimeMin,
                ["dt-time-of-day", "dt-before-smalldatetime-min"],
                "smalldatetime の範囲の最初の 1 分前"
            ),
            EdgeConstant(
                "smalldatetime-min-rounding-up",
                DateTimeEdges.SmallDateTimeMinRoundingUp,
                ["dt-time-of-day", "dt-small-off-minute", "dt-before-smalldatetime-min"],
                "smalldatetime の範囲の最初の 20 秒前"
            ),
            EdgeConstant(
                "smalldatetime-min-rounding-up-at-half-minute",
                DateTimeEdges.SmallDateTimeMinRoundingUpAtHalfMinute,
                [
                    "dt-time-of-day",
                    "dt-small-off-minute",
                    "dt-small-rounding-boundary",
                    "dt-before-smalldatetime-min",
                ],
                "smalldatetime の範囲の最初の 30 秒前"
            ),
            EdgeConstant(
                "one-tick-after-datetime",
                DateTimeEdges.OneTickAfter(Day),
                [
                    "dt-time-of-day",
                    "dt-sub-microsecond",
                    "dt-legacy-off-grid",
                    "dt-small-off-minute",
                ],
                "日時を 1 tick 進めた値"
            ),
            EdgeConstant(
                "one-tick-after-datetimeoffset",
                DateTimeEdges.OneTickAfter(new DateTimeOffset(Day, TimeSpan.Zero)),
                ["dto-offset-zero", "dto-sub-microsecond"],
                "時差つきの日時を 1 tick 進めた値"
            ),
            EdgeConstant(
                "one-tick-before-datetime",
                DateTimeEdges.OneTickBefore(Day.AddDays(1)),
                [
                    "dt-time-of-day",
                    "dt-tick-before-midnight",
                    "dt-sub-microsecond",
                    "dt-legacy-off-grid",
                    "dt-small-off-minute",
                ],
                "翌日の 0 時を 1 tick 戻した値"
            ),
            EdgeConstant(
                "calendar-first-year-offset",
                new DateTimeOffset(DateTimeEdges.CalendarFirstYear, 1, 1, 0, 0, 0, TimeSpan.Zero),
                ["dto-offset-zero", "dto-calendar-end"],
                "暦の最初の年の時差つきの日時"
            ),
            EdgeConstant(
                "calendar-last-year-offset",
                new DateTimeOffset(DateTimeEdges.CalendarLastYear, 1, 1, 0, 0, 0, TimeSpan.Zero),
                ["dto-offset-zero", "dto-calendar-end"],
                "暦の最後の年の時差つきの日時"
            ),
            EdgeConstant(
                "legacy-window-after-read-back",
                Day.AddTicks(DateTimeEdges.LegacyWindowAfterReadBackTicks),
                [
                    "dt-time-of-day",
                    "dt-legacy-off-grid",
                    "dt-legacy-between-read-back-and-step",
                    "dt-small-off-minute",
                ],
                "datetime が読み戻す .003 の直後の窓"
            ),
            EdgeConstant(
                "legacy-window-before-read-back",
                Day.AddTicks(DateTimeEdges.LegacyWindowBeforeReadBackTicks),
                [
                    "dt-time-of-day",
                    "dt-legacy-off-grid",
                    "dt-legacy-between-read-back-and-step",
                    "dt-small-off-minute",
                ],
                "datetime が読み戻す .007 の直前の窓"
            ),
            EdgeConstant(
                "time-span-one-day",
                TimeSpanEdges.OneDay,
                ["ts-one-day"],
                "ちょうど 1 日"
            ),
            EdgeConstant(
                "time-span-max-whole-microseconds",
                TimeSpanEdges.MaxWholeMicroseconds,
                ["ts-beyond-day", "ts-beyond-mysql-range"],
                "型の最大をマイクロ秒の倍数へ切り下げた値"
            ),
            EdgeConstant(
                "time-span-min-whole-microseconds",
                TimeSpanEdges.MinWholeMicroseconds,
                ["ts-negative", "ts-beyond-mysql-range"],
                "型の最小をマイクロ秒の倍数へ切り上げた値"
            ),
            EdgeConstant(
                "negative-zero-double",
                FloatingEdges.NegativeZeroDouble,
                ["fp-negative-zero"],
                "double の負のゼロ"
            ),
            EdgeConstant(
                "negative-zero-single",
                FloatingEdges.NegativeZeroSingle,
                ["fp-negative-zero"],
                "float の負のゼロ"
            ),
            EdgeConstant(
                "double-epsilon",
                FloatingEdges.DoubleEpsilon,
                ["fp-subnormal", "fp-non-dyadic", "fp-beyond-float"],
                "double の最小の非正規化数"
            ),
            EdgeConstant(
                "single-epsilon",
                FloatingEdges.SingleEpsilon,
                ["fp-subnormal", "fp-non-dyadic"],
                "float の最小の非正規化数"
            ),
            EdgeConstant(
                "two-pow-24-as-float",
                (float)PowerOfTwoEdges.TwoPow24,
                ["fp-integer-beyond-2pow24"],
                "2^24 の float"
            ),
            EdgeConstant(
                "two-pow-24-plus-1",
                PowerOfTwoEdges.TwoPow24Plus1,
                ["int-beyond-float-precision"],
                "2^24 + 1 の整数"
            ),
            EdgeConstant(
                "two-pow-53-as-double",
                (double)PowerOfTwoEdges.TwoPow53,
                ["fp-integer-beyond-2pow53"],
                "2^53 の double"
            ),
            EdgeConstant(
                "two-pow-53-plus-1",
                PowerOfTwoEdges.TwoPow53Plus1,
                ["int-beyond-float-precision", "int-beyond-double-precision"],
                "2^53 + 1 の整数"
            ),
            EdgeConstant("text-latin-e-acute", TextEdges.LatinSmallEAcute, ["str-non-ascii"], "é"),
            EdgeConstant(
                "text-euro-sign",
                TextEdges.EuroSign,
                ["str-non-ascii", "str-non-latin1"],
                "€"
            ),
            EdgeConstant(
                "text-kanji-nihon",
                TextEdges.KanjiNihon,
                ["str-non-ascii", "str-non-latin1"],
                "日本"
            ),
            EdgeConstant(
                "text-soft-hyphen",
                TextEdges.SoftHyphen,
                ["str-non-ascii"],
                "ソフトハイフン"
            ),
            EdgeConstant(
                "text-ideographic-space",
                TextEdges.IdeographicSpace,
                ["str-non-ascii", "str-non-latin1", "str-other-whitespace"],
                "全角の空白"
            ),
            EdgeConstant("text-tab", TextEdges.Tab, ["str-other-whitespace"], "タブ"),
            EdgeConstant(
                "text-fullwidth-capital-a",
                TextEdges.FullwidthCapitalA,
                ["str-non-ascii", "str-non-latin1"],
                "全角の A"
            ),
            EdgeConstant(
                "text-fullwidth-digit-one",
                TextEdges.FullwidthDigitOne,
                ["str-non-ascii", "str-non-latin1"],
                "全角の 1"
            ),
            EdgeConstant(
                "text-grinning-face",
                TextEdges.GrinningFace,
                ["str-non-ascii", "str-non-latin1", "str-surrogate"],
                "サロゲートペア"
            ),
        ];

    private static string[] PastMaxExpected =>
        [
            "dt-time-of-day",
            "dt-legacy-off-grid",
            "dt-small-off-minute",
            "dt-past-sql-datetime-max",
            "dt-past-smalldatetime-max",
            "dt-calendar-end",
        ];

    private static string[] BeforeMinWithTimeExpected =>
        [
            "dt-time-of-day",
            "dt-legacy-off-grid",
            "dt-small-off-minute",
            "dt-before-sql-datetime-min",
            "dt-before-smalldatetime-min",
        ];

    private static Dictionary<string, string[]> DateTimeExpected =>
        new(StringComparer.Ordinal)
        {
            [DateTimePool.Midnight] = ["dt-midnight"],
            [DateTimePool.TimeOfDay] = ["dt-time-of-day"],
            [DateTimePool.TickBeforeMidnight] =
            [
                "dt-time-of-day",
                "dt-tick-before-midnight",
                "dt-sub-microsecond",
                "dt-legacy-off-grid",
                "dt-small-off-minute",
            ],
            [DateTimePool.BetweenLegacyReadBacks] =
            [
                "dt-time-of-day",
                "dt-legacy-off-grid",
                "dt-small-off-minute",
            ],
            [DateTimePool.BeforeSqlDateTimeMin] =
            [
                "dt-midnight",
                "dt-before-sql-datetime-min",
                "dt-before-smalldatetime-min",
            ],
            [DateTimePool.PastSqlDateTimeMax] =
            [
                "dt-time-of-day",
                "dt-legacy-off-grid",
                "dt-small-off-minute",
                "dt-past-sql-datetime-max",
                "dt-past-smalldatetime-max",
                "dt-calendar-end",
            ],
            [DateTimePool.LegacyReadBackFirst] =
            [
                "dt-time-of-day",
                "dt-legacy-read-back",
                "dt-small-off-minute",
            ],
            [DateTimePool.LegacyWindowAfterReadBack] =
            [
                "dt-time-of-day",
                "dt-legacy-off-grid",
                "dt-legacy-between-read-back-and-step",
                "dt-small-off-minute",
            ],
            [DateTimePool.LegacyWindowBeforeReadBack] =
            [
                "dt-time-of-day",
                "dt-legacy-off-grid",
                "dt-legacy-between-read-back-and-step",
                "dt-small-off-minute",
            ],
            [DateTimePool.LegacyOffGridLowBand] =
            [
                "dt-time-of-day",
                "dt-legacy-off-grid",
                "dt-small-off-minute",
            ],
            [DateTimePool.LegacyOffGridHighBand] =
            [
                "dt-time-of-day",
                "dt-legacy-off-grid",
                "dt-small-off-minute",
            ],
            [DateTimePool.SmallRoundsDown] =
            [
                "dt-time-of-day",
                "dt-legacy-off-grid",
                "dt-small-off-minute",
                "dt-small-rounding-boundary",
            ],
            [DateTimePool.SmallRoundsUp] =
            [
                "dt-time-of-day",
                "dt-legacy-off-grid",
                "dt-small-off-minute",
                "dt-small-rounding-boundary",
            ],
            [DateTimePool.SmallRangeFirst] = ["dt-midnight", "dt-smalldatetime-range-end"],
            [DateTimePool.SmallRangeLast] = ["dt-time-of-day", "dt-smalldatetime-range-end"],
            [DateTimePool.SmallPastLast] =
            [
                "dt-time-of-day",
                "dt-small-off-minute",
                "dt-small-rounding-boundary",
                "dt-past-smalldatetime-max",
            ],
            [DateTimePool.SmallBeforeFirst] =
            [
                "dt-time-of-day",
                "dt-small-off-minute",
                "dt-before-smalldatetime-min",
            ],
        };

    private static Dictionary<string, string[]> DecimalExpected =>
        new(StringComparer.Ordinal)
        {
            [DecimalPool.Integral] = [],
            [DecimalPool.Fraction] = ["dec-fraction"],
            [DecimalPool.BeyondColumnRange] = ["dec-fraction", "dec-beyond-column-scale"],
            [DecimalPool.Zero] = ["dec-zero"],
            [DecimalPool.Negative] = ["dec-negative"],
            [DecimalPool.SmallestUnit] = ["dec-fraction", "dec-smallest-unit"],
            [DecimalPool.FractionBelowOne] = ["dec-fraction", "dec-beyond-column-scale"],
        };

    private static Dictionary<string, string[]> NarrowingExpected =>
        new(StringComparer.Ordinal)
        {
            [NarrowingPool.KeptByCast] = ["cast-keeps"],
            [NarrowingPool.FractionCutByCast] = ["dec-fraction", "cast-cuts-fraction"],
            [NarrowingPool.BeyondCastRange] = ["cast-beyond-range"],
        };

    public static TheoryData<string> BoundaryIds() => Boundaries.Ids();

    public static TheoryData<string> PoolValueIds() => PoolValues.Ids();

    public static TheoryData<string> EdgeIds() => [.. ValueEdges.All.Select(edge => edge.Id)];

    [Fact(
        DisplayName = "[値のプール] 名簿の Id は一意で、系統の接頭辞で始まる小文字とハイフンの形、Branch は空でない"
    )]
    public void Roster_IsWellFormed()
    {
        var pattern = new Regex("^[a-z]+(?:-[a-z0-9]+)+$", RegexOptions.CultureInvariant);

        ValueEdges.All.Select(edge => edge.Id).Should().OnlyHaveUniqueItems();
        ValueEdges
            .All.Where(edge =>
                !pattern.IsMatch(edge.Id)
                || !edge.Id.StartsWith(Prefixes[edge.Family], StringComparison.Ordinal)
                || string.IsNullOrWhiteSpace(edge.Branch)
            )
            .Select(edge => edge.Id)
            .Should()
            .BeEmpty();
        Enum.GetValues<ValueFamily>()
            .Where(family => ValueEdges.All.All(edge => edge.Family != family))
            .Should()
            .BeEmpty("どの系統も水準を 1 つ以上持つ");
    }

    [Theory(DisplayName = "[値のプール] 水準ごとに代表の値が 1 つ以上あり、代表はその水準に当たる")]
    [MemberData(nameof(EdgeIds))]
    public void Samples_HitTheirEdge(string edgeId)
    {
        var samples = ValueEdgeSamples.For(edgeId);

        samples.Should().NotBeEmpty();
        samples
            .Where(sample =>
                !ValueEdges.Classify(sample.Value.Value, sample.Column).Contains(edgeId)
            )
            .Select(sample => sample.Value.Why)
            .Should()
            .BeEmpty();
    }

    [Fact(
        DisplayName = "[値のプール] 判定式の境目の表は、どの水準にも当たる行と当たらない行を持つ"
    )]
    public void Boundaries_CoverEveryEdgeBothWays()
    {
        var hits = Boundaries
            .Where(row => row.Input.Hits)
            .Select(row => row.Input.Edge)
            .ToHashSet();
        var misses = Boundaries
            .Where(row => !row.Input.Hits)
            .Select(row => row.Input.Edge)
            .ToHashSet();

        ValueEdges
            .All.Select(edge => edge.Id)
            .Where(id => !hits.Contains(id) || !misses.Contains(id))
            .Should()
            .BeEmpty();
        Boundaries
            .Select(row => row.Input.Edge)
            .Where(id => ValueEdges.Find(id) is null)
            .Should()
            .BeEmpty("境目の表の水準は名簿にある");
    }

    [Theory(
        DisplayName = "[値のプール] 判定式の境目：当たる値は当たり、境目の 1 つ外の値は当たらない"
    )]
    [MemberData(nameof(BoundaryIds))]
    public void Boundary_IsClassified(string id)
    {
        var row = Boundaries[id].Input;

        ValueEdges.Classify(row.Value, row.Column).Contains(row.Edge).Should().Be(row.Hits);
    }

    [Theory(DisplayName = "[値のプール] 今のプールの値の分類は、表に書いた期待の集合と同じ")]
    [MemberData(nameof(PoolValueIds))]
    public void PoolValue_IsClassifiedAsExpected(string id)
    {
        var row = PoolValues[id].Input;

        ValueEdges.Classify(row.Value, row.Column).Should().BeEquivalentTo(row.Expected);
    }

    [Fact(
        DisplayName = "[値のプール] リスト・配列は要素ごとの分類の和、null は空、相が無ければ相の要る水準を判定しない"
    )]
    public void Classify_HandlesListsNullAndMissingColumns()
    {
        ValueEdges.Classify(null, null).Should().BeEmpty();
        ValueEdges
            .Classify(new List<object?> { 0m, null, -1.5m }, null)
            .Should()
            .BeEquivalentTo(["dec-zero", "dec-negative", "dec-fraction"]);
        ValueEdges.Classify(1.234m, null).Should().NotContain("dec-beyond-column-scale");
        ValueEdges
            .Classify(1.234m, new ValueColumn { SqlType = "decimal" })
            .Should()
            .NotContain("dec-beyond-column-scale");
        ValueEdges
            .Classify(1, new ValueColumn())
            .Should()
            .NotContain(edge => edge.StartsWith("cast-", StringComparison.Ordinal));
    }

    [Theory(
        DisplayName = "[値のプール] 宣言の型の名前から列の相を読む（読めない型は相を空にする）"
    )]
    [InlineData("decimal(10,2)", 10, 2, null, null, null)]
    [InlineData("NUMBER(12,3)", 12, 3, null, null, null)]
    [InlineData("nvarchar(20)", null, null, 20, false, true)]
    [InlineData("varchar(20)", null, null, 20, false, false)]
    [InlineData("char(4)", null, null, 4, true, false)]
    [InlineData("nchar(10)", null, null, 10, true, true)]
    [InlineData("binary(4)", null, null, 4, true, null)]
    [InlineData("varbinary(16)", null, null, 16, false, null)]
    [InlineData("varbinary(max)", null, null, null, false, null)]
    [InlineData("datetime2(2)", null, 2, null, null, null)]
    [InlineData("time(2)", null, 2, null, null, null)]
    [InlineData("datetimeoffset(2)", null, 2, null, null, null)]
    [InlineData("time[]", null, 7, null, null, null)]
    [InlineData("geography", null, null, null, null, null)]
    public void FromSqlType_ReadsFacets(
        string sqlType,
        int? precision,
        int? scale,
        int? length,
        bool? fixedLength,
        bool? unicode
    )
    {
        var column = ValueColumn.FromSqlType(sqlType);

        column.SqlType.Should().Be(sqlType);
        (column.Precision, column.Scale, column.Length, column.FixedLength, column.Unicode)
            .Should()
            .Be((precision, scale, length, fixedLength, unicode));
    }

    [Fact(DisplayName = "[値のプール] money・smallmoney と整数の列は範囲を持つ")]
    public void FromSqlType_ReadsRanges()
    {
        ValueColumn
            .FromSqlType("money")
            .Should()
            .Match<ValueColumn>(column =>
                column.Min == -922_337_203_685_477.5808m
                && column.Max == 922_337_203_685_477.5807m
                && column.Scale == 4
            );
        ValueColumn.FromSqlType("smallmoney").Max.Should().Be(214_748.3647m);
        ValueColumn
            .FromSqlType("int")
            .Should()
            .Match<ValueColumn>(column =>
                column.Min == int.MinValue && column.Max == int.MaxValue && column.Scale == 0
            );
        ValueColumn.FromSqlType("bigint unsigned").Max.Should().Be(ulong.MaxValue);
    }
}
