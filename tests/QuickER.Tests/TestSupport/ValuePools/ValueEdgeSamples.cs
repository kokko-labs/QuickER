using System;
using System.Collections.Generic;
using System.Linq;

namespace QuickER.Tests.TestSupport.ValuePools;

/// <summary>水準の代表の値 1 つ（列の相の要る水準は相と組で持つ）</summary>
/// <param name="Value">代表の値（名前は水準の Id）</param>
/// <param name="Column">列の相（相の要らない水準は null）</param>
public sealed record ValueEdgeSample(PoolValue<object> Value, ValueColumn? Column);

/// <summary>
/// 水準ごとの代表の値（エンジン・表が水準を足すときに引く値。どの代表も <see cref="ValueEdges.Classify"/> でその水準に当たる）
/// </summary>
/// <remarks>
/// 型ごとのプール（<see cref="DateTimePool"/> ほか）の名前・値・順序は値の束縛の生成の表の行 Id と既知の失敗が名前に依るので変えず、
/// ここは名簿の水準の Id で引く別の入口にする。
/// </remarks>
public static class ValueEdgeSamples
{
    private static readonly DateTime Day = new(2024, 1, 1);

    private static readonly ValueColumn DateTime2Of2 = ValueColumn.FromSqlType("datetime2(2)");

    private static readonly ValueColumn Decimal10Of2 = ValueColumn.FromSqlType("decimal(10,2)");

    private static readonly ValueColumn Varchar4 = ValueColumn.FromSqlType("varchar(4)");

    private static readonly ValueColumn Binary4 = ValueColumn.FromSqlType("binary(4)");

    private static readonly ValueColumn IntColumn = ValueColumn.FromSqlType("int");

    /// <summary>水準の Id → 代表の値</summary>
    private static readonly Dictionary<
        string,
        (object Value, ValueColumn? Column, string Why)[]
    > Table = new(StringComparer.Ordinal)
    {
        ["dt-kind-utc"] = [(DateTime.SpecifyKind(Day, DateTimeKind.Utc), null, "Kind が Utc")],
        ["dt-kind-local"] =
        [
            (DateTime.SpecifyKind(Day, DateTimeKind.Local), null, "Kind が Local"),
        ],
        ["dt-midnight"] = [(Day, null, "0 時ちょうど")],
        ["dt-time-of-day"] = [(Day.AddHours(10), null, "10 時")],
        ["dt-tick-before-midnight"] = [(Day.AddDays(1).AddTicks(-1), null, "翌日の 1 tick 前")],
        ["dt-sub-microsecond"] = [(Day.AddTicks(1), null, "1 tick")],
        ["dt-beyond-column-scale"] =
        [
            (Day.AddMilliseconds(1), DateTime2Of2, "datetime2(2) の列と 1 ミリ秒"),
        ],
        ["dt-legacy-read-back"] = [(Day.AddMilliseconds(3), null, ".003")],
        ["dt-legacy-off-grid"] = [(Day.AddMilliseconds(4), null, ".004")],
        ["dt-legacy-between-read-back-and-step"] = [(Day.AddTicks(31_000), null, ".0031")],
        ["dt-small-off-minute"] = [(Day.AddSeconds(10), null, "10 秒")],
        ["dt-small-rounding-boundary"] = [(Day.AddMilliseconds(29_999), null, "29.999 秒")],
        ["dt-before-sql-datetime-min"] = [(new DateTime(1752, 12, 31), null, "1752-12-31")],
        ["dt-before-smalldatetime-min"] = [(new DateTime(1899, 12, 31), null, "1899-12-31")],
        ["dt-past-sql-datetime-max"] =
        [
            (new DateTime(9999, 12, 31, 23, 59, 59, 999), null, ".999"),
        ],
        ["dt-past-smalldatetime-max"] = [(new DateTime(2079, 6, 7), null, "2079-06-07")],
        ["dt-sql-datetime-range-end"] = [(ValueEdges.SqlDateTimeMin, null, "1753-01-01")],
        ["dt-smalldatetime-range-end"] = [(ValueEdges.SmallDateTimeMax, null, "2079-06-06 23:59")],
        ["dt-calendar-end"] = [(new DateTime(1, 1, 1), null, "0001-01-01")],
        ["dt-leap-day"] = [(new DateTime(2024, 2, 29), null, "2024-02-29")],
        ["dto-offset-zero"] = [(new DateTimeOffset(Day, TimeSpan.Zero), null, "時差 0")],
        ["dto-offset-positive"] =
        [
            (new DateTimeOffset(Day, TimeSpan.FromHours(9)), null, "+09:00"),
        ],
        ["dto-offset-negative"] =
        [
            (new DateTimeOffset(Day, TimeSpan.FromHours(-5)), null, "-05:00"),
        ],
        ["dto-offset-extreme"] =
        [
            (new DateTimeOffset(Day, TimeSpan.FromHours(14)), null, "+14:00"),
        ],
        ["dto-local-date-differs"] =
        [
            (new DateTimeOffset(Day.AddHours(5), TimeSpan.FromHours(9)), null, "05:00+09:00"),
        ],
        ["dto-sub-microsecond"] =
        [
            (new DateTimeOffset(Day.AddTicks(1), TimeSpan.Zero), null, "1 tick"),
        ],
        ["dto-beyond-column-scale"] =
        [
            (
                new DateTimeOffset(Day.AddMilliseconds(1), TimeSpan.Zero),
                ValueColumn.FromSqlType("datetimeoffset(2)"),
                "datetimeoffset(2) と 1 ミリ秒"
            ),
        ],
        ["dto-calendar-end"] =
        [
            (new DateTimeOffset(9999, 1, 1, 0, 0, 0, TimeSpan.Zero), null, "9999 年"),
        ],
        ["ts-zero"] = [(TimeSpan.Zero, null, "0")],
        ["ts-negative"] = [(TimeSpan.FromHours(-1), null, "-1 時間")],
        ["ts-day-end"] = [(TimeSpan.FromDays(1) - TimeSpan.FromTicks(1), null, "1 日の 1 tick 前")],
        ["ts-one-day"] = [(TimeSpan.FromDays(1), null, "1 日")],
        ["ts-beyond-day"] = [(new TimeSpan(1, 1, 0, 0), null, "1 日 1 時間")],
        ["ts-sub-microsecond"] = [(TimeSpan.FromTicks(1), null, "1 tick")],
        ["ts-beyond-column-scale"] =
        [
            (
                TimeSpan.FromMilliseconds(1),
                ValueColumn.FromSqlType("time(2)"),
                "time(2) と 1 ミリ秒"
            ),
        ],
        ["ts-mysql-range-end"] = [(ValueEdges.MySqlTimeMax, null, "838:59:59")],
        ["ts-beyond-mysql-range"] = [(new TimeSpan(34, 23, 0, 0), null, "839:00:00")],
        ["dec-zero"] = [(0m, null, "0")],
        ["dec-negative"] = [(-1.5m, null, "-1.5")],
        ["dec-fraction"] = [(1.5m, null, "1.5")],
        ["dec-beyond-long"] = [(100000000000000000000m, null, "10^20")],
        ["dec-type-end"] =
        [
            (decimal.MaxValue, null, "decimal.MaxValue"),
            (decimal.MinValue, null, "decimal.MinValue"),
        ],
        ["dec-beyond-column-scale"] = [(1.234m, Decimal10Of2, "decimal(10,2) と 1.234")],
        ["dec-beyond-column-range"] = [(123456789.5m, Decimal10Of2, "decimal(10,2) と整数部 9 桁")],
        ["dec-column-range-end"] = [(99999999.99m, Decimal10Of2, "decimal(10,2) の最大")],
        ["dec-smallest-unit"] = [(0.01m, Decimal10Of2, "decimal(10,2) の 0.01")],
        ["int-zero"] = [(0, null, "0")],
        ["int-negative"] = [(-1, null, "-1")],
        ["int-type-min"] = [(int.MinValue, null, "int の最小")],
        ["int-type-max"] = [(long.MaxValue, null, "long の最大")],
        ["int-sign-boundary"] = [(4294967295u, null, "uint の最大")],
        ["int-beyond-column-range"] = [(2147483648L, IntColumn, "int の列と 2^31")],
        ["int-beyond-float-precision"] = [(16_777_217, null, "2^24 + 1")],
        ["int-beyond-double-precision"] = [(9_007_199_254_740_993L, null, "2^53 + 1")],
        ["fp-negative-zero"] = [(-0.0, null, "-0.0")],
        ["fp-subnormal"] = [(double.Epsilon, null, "double の最小の非正規化数")],
        ["fp-type-end"] = [(float.MaxValue, null, "float の最大")],
        ["fp-non-dyadic"] = [(0.1, null, "0.1")],
        ["fp-integer-beyond-2pow24"] = [(16_777_216f, null, "2^24 の float")],
        ["fp-integer-beyond-2pow53"] = [(9_007_199_254_740_992d, null, "2^53 の double")],
        ["fp-beyond-float"] = [(0.1, null, "0.1 の double")],
        ["str-empty"] = [("", null, "空文字列")],
        ["str-non-ascii"] = [("é", null, "é")],
        ["str-non-latin1"] = [("日本", null, "日本")],
        ["str-surrogate"] = [("😀", null, "サロゲートペア")],
        ["str-leading-space"] = [(" a", null, "先頭の空白")],
        ["str-trailing-space"] = [("a ", null, "末尾の空白")],
        ["str-other-whitespace"] = [("a\tb", null, "タブ")],
        ["str-like-wildcard"] = [("50%", null, "%")],
        ["str-quote"] = [("it's", null, "単引用符")],
        ["str-beyond-column-length"] = [("abcde", Varchar4, "varchar(4) と 5 文字")],
        ["str-exact-column-length"] = [("abcd", Varchar4, "varchar(4) と 4 文字")],
        ["bin-empty"] = [(Array.Empty<byte>(), null, "空")],
        ["bin-trailing-zero"] = [(new byte[] { 0x01, 0x00 }, null, "01 00")],
        ["bin-leading-zero"] = [(new byte[] { 0x00, 0x01 }, null, "00 01")],
        ["bin-beyond-column-length"] =
        [
            (new byte[] { 1, 2, 3, 4, 5 }, Binary4, "binary(4) と 5 バイト"),
        ],
        ["bin-exact-column-length"] =
        [
            (new byte[] { 1, 2, 3, 4 }, Binary4, "binary(4) と 4 バイト"),
        ],
        ["bin-zero-excess"] =
        [
            (new byte[] { 1, 2, 3, 4, 0 }, Binary4, "binary(4) と超えた分が 0x00"),
        ],
        ["bin-long"] = [(new byte[ValueEdges.LongBinaryLength], null, "1024 バイト")],
        ["guid-empty"] = [(Guid.Empty, null, "全部 0")],
        ["guid-max"] = [(Guid.AllBitsSet, null, "全部 0xFF")],
        ["bool-true"] = [(true, null, "真")],
        ["bool-false"] = [(false, null, "偽")],
        ["cast-keeps"] = [(1m, new ValueColumn { CastTarget = typeof(int) }, "1 を int へ")],
        ["cast-cuts-fraction"] =
        [
            (1.7m, new ValueColumn { CastTarget = typeof(int) }, "1.7 を int へ"),
        ],
        ["cast-beyond-range"] =
        [
            (4294967297.0, new ValueColumn { CastTarget = typeof(int) }, "2^32 + 1 を int へ"),
        ],
        ["cast-sign-only"] = [(-1, new ValueColumn { CastTarget = typeof(uint) }, "-1 を uint へ")],
    };

    /// <summary>水準の代表の値（名簿に無い Id は空）</summary>
    /// <param name="edgeId">水準の Id</param>
    public static IReadOnlyList<ValueEdgeSample> For(string edgeId) =>
        Table.TryGetValue(edgeId, out var samples)
            ?
            [
                .. samples.Select(sample => new ValueEdgeSample(
                    new PoolValue<object>(edgeId, sample.Value, sample.Why),
                    sample.Column
                )),
            ]
            : [];
}
