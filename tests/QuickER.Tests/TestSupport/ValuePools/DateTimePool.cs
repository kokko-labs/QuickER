using System;
using System.Collections.Generic;
using System.Linq;

namespace QuickER.Tests.TestSupport.ValuePools;

/// <summary>
/// 日時の値のプール：基準の日（列のシードの日）から決まる端の値と、SQL Server の datetime の範囲の外の値。Kind は付け替えられる
/// </summary>
/// <remarks>
/// 値はどれも Kind が Unspecified（基準の日の Kind によらない）。Kind を変えるときは <see cref="WithKind"/> を通す
/// （C# の DateTime の比較は Kind を見ないので、同じ tick の値として比べる）。
/// </remarks>
public static class DateTimePool
{
    /// <summary>基準の日の 0 時ちょうど</summary>
    public const string Midnight = "midnight";

    /// <summary>基準の日の 10 時ちょうど（どの日時の型でも刻みの上にある時刻）</summary>
    public const string TimeOfDay = "time-of-day";

    /// <summary>基準の日の翌日の 1 tick 前</summary>
    public const string TickBeforeMidnight = "tick-before-midnight";

    /// <summary>SQL Server の datetime が読み戻す値 .003 と .007 の間（基準の日の 10:00:00.004）</summary>
    public const string BetweenLegacyReadBacks = "between-legacy-read-backs";

    /// <summary>SQL Server の datetime の範囲の最初（1753-01-01）より前の値</summary>
    public const string BeforeSqlDateTimeMin = "before-sql-datetime-min";

    /// <summary>SQL Server の datetime の範囲の最後（9999-12-31 23:59:59.997）を超え、丸めると範囲の外になる値</summary>
    public const string PastSqlDateTimeMax = "past-sql-datetime-max";

    /// <summary>SQL Server の datetime が読み戻す値 .003（基準の日の 10:00:00.003。1/300 秒の 1 単位）</summary>
    public const string LegacyReadBackFirst = "legacy-read-back-first";

    /// <summary>datetime の読み戻す値 .003 と 1/300 秒（.0033333）の間の窓（基準の日の 10:00:00.0031）</summary>
    public const string LegacyWindowAfterReadBack = "legacy-window-after-read-back";

    /// <summary>datetime の 2/300 秒（.0066667）と読み戻す値 .007 の間の窓（基準の日の 10:00:00.0068）</summary>
    public const string LegacyWindowBeforeReadBack = "legacy-window-before-read-back";

    /// <summary>datetime の刻みの上にない値で、ミリ秒の 1 の位が 0〜2 の帯（基準の日の 10:00:00.001）</summary>
    public const string LegacyOffGridLowBand = "legacy-off-grid-low-band";

    /// <summary>datetime の刻みの上にない値で、ミリ秒の 1 の位が 7〜9 の帯（基準の日の 10:00:00.009）</summary>
    public const string LegacyOffGridHighBand = "legacy-off-grid-high-band";

    /// <summary>smalldatetime が分へ切り下げる最後の秒（基準の日の 10:00:29.998）</summary>
    public const string SmallRoundsDown = "small-rounds-down";

    /// <summary>smalldatetime が分へ切り上げる最初の秒（基準の日の 10:00:29.999）</summary>
    public const string SmallRoundsUp = "small-rounds-up";

    /// <summary>smalldatetime の範囲の最初の値ちょうど（1900-01-01 00:00。基準の日に依らない）</summary>
    public const string SmallRangeFirst = "small-range-first";

    /// <summary>smalldatetime の範囲の最後の値ちょうど（2079-06-06 23:59。基準の日に依らない）</summary>
    public const string SmallRangeLast = "small-range-last";

    /// <summary>smalldatetime の範囲の最後の値の 30 秒後（2079-06-06 23:59:30。分へ丸めると範囲の外へあふれる。基準の日に依らない）</summary>
    public const string SmallPastLast = "small-past-last";

    /// <summary>smalldatetime の範囲の最初の値の 1 秒前（1899-12-31 23:59:59。基準の日に依らない）</summary>
    public const string SmallBeforeFirst = "small-before-first";

    /// <summary>プールの値の名前（宣言の順）</summary>
    public static IReadOnlyList<string> Names { get; } =
    [
        Midnight,
        TimeOfDay,
        TickBeforeMidnight,
        BetweenLegacyReadBacks,
        BeforeSqlDateTimeMin,
        PastSqlDateTimeMax,
        LegacyReadBackFirst,
        LegacyWindowAfterReadBack,
        LegacyWindowBeforeReadBack,
        LegacyOffGridLowBand,
        LegacyOffGridHighBand,
        SmallRoundsDown,
        SmallRoundsUp,
        SmallRangeFirst,
        SmallRangeLast,
        SmallPastLast,
        SmallBeforeFirst,
    ];

    /// <summary>datetime の列の刻み（読み戻す値・窓・BetweenDateTimeReadBacks の帯）で決まる値の名前（datetime の列とだけ組む）</summary>
    public static IReadOnlyList<string> LegacyNames { get; } =
    [
        LegacyReadBackFirst,
        LegacyWindowAfterReadBack,
        LegacyWindowBeforeReadBack,
        LegacyOffGridLowBand,
        LegacyOffGridHighBand,
    ];

    /// <summary>smalldatetime の列の丸めの境目と範囲の端で決まる値の名前（smalldatetime の列とだけ組む）</summary>
    public static IReadOnlyList<string> SmallNames { get; } =
    [
        SmallRoundsDown,
        SmallRoundsUp,
        SmallRangeFirst,
        SmallRangeLast,
        SmallPastLast,
        SmallBeforeFirst,
    ];

    /// <summary>基準の日から決まるプールの値（宣言の順）</summary>
    /// <param name="anchorDay">基準の日（時刻を持たない日付）</param>
    /// <exception cref="ArgumentException">基準の日が時刻を持つ</exception>
    public static IReadOnlyList<PoolValue<DateTime>> Around(DateTime anchorDay)
    {
        if (anchorDay.TimeOfDay != TimeSpan.Zero)
        {
            throw new ArgumentException("基準の日は時刻を持たない", nameof(anchorDay));
        }

        var day = DateTime.SpecifyKind(anchorDay, DateTimeKind.Unspecified);

        return
        [
            new(Midnight, day, "日付だけの値（date の列とちょうど等しくなりうる）"),
            new(
                TimeOfDay,
                day.AddHours(10),
                "時刻を持つ値（date の列と等しくならず、時刻を落とすと等しくなる）"
            ),
            new(
                TickBeforeMidnight,
                day.AddDays(1).AddTicks(-1),
                "その日の最後の tick（datetime・smalldatetime へ丸めると翌日の 0 時になり、マイクロ秒へ切ると端数が落ちる）"
            ),
            new(
                BetweenLegacyReadBacks,
                day.AddHours(10).AddMilliseconds(4),
                "datetime の刻み（1/300 秒）の上にない値（datetime へ丸めると .003 になり、smalldatetime へ丸めると分ちょうどになる）"
            ),
            new(
                BeforeSqlDateTimeMin,
                new DateTime(1752, 12, 31),
                "datetime（1753-01-01 から）・smalldatetime（1900-01-01 から）の範囲の外（その型で送ると断られる）"
            ),
            new(
                PastSqlDateTimeMax,
                new DateTime(9999, 12, 31, 23, 59, 59, 999),
                "datetime の最後の値 .997 を超え、丸めると範囲の外になる値（smalldatetime の範囲の外でもある）"
            ),
            new(
                LegacyReadBackFirst,
                day.AddHours(10).AddMilliseconds(3),
                "datetime が 1/300 秒の 1 単位を読み戻す値（.003。列の型のまま束縛でき、その刻みの行に等しい）"
            ),
            new(
                LegacyWindowAfterReadBack,
                day.AddHours(10).AddTicks(DateTimeEdges.LegacyWindowAfterReadBackTicks),
                "読み戻す値 .003 と 1/300 秒の刻み .0033333 の間の値（値のまま datetime2 で束縛すると互換レベル 130 以上で C# と割れ、読み戻す値の間へ置き直すと割れない）"
            ),
            new(
                LegacyWindowBeforeReadBack,
                day.AddHours(10).AddTicks(DateTimeEdges.LegacyWindowBeforeReadBackTicks),
                "2/300 秒の刻み .0066667 と読み戻す値 .007 の間の値（同じく、値のまま datetime2 で束縛すると C# と割れる）"
            ),
            new(
                LegacyOffGridLowBand,
                day.AddHours(10).AddMilliseconds(1),
                "datetime の刻みの上にない値で、ミリ秒の 1 の位が 0〜2 の帯（読み戻す値の間へ置き直すとき、直前の読み戻す値を .000 から取る）"
            ),
            new(
                LegacyOffGridHighBand,
                day.AddHours(10).AddMilliseconds(9),
                "datetime の刻みの上にない値で、ミリ秒の 1 の位が 7〜9 の帯（読み戻す値の間へ置き直すとき、直前の読み戻す値を .007 から取る）"
            ),
            new(
                SmallRoundsDown,
                day.AddHours(10).AddSeconds(30).AddMilliseconds(-2),
                "smalldatetime が分へ切り下げる最後の秒（29.998 秒。列の型で束縛すると分ちょうどになり、その分の行に等しくなる）"
            ),
            new(
                SmallRoundsUp,
                day.AddHours(10).AddSeconds(30).AddMilliseconds(-1),
                "smalldatetime が分へ切り上げる最初の秒（29.999 秒。列の型で束縛すると次の分になり、次の分の行に等しくなる）"
            ),
            new(
                SmallRangeFirst,
                ValueEdges.SmallDateTimeMin,
                "smalldatetime の範囲の最初の値ちょうど（分ちょうどで範囲の中なので、列の型のまま束縛できる）"
            ),
            new(
                SmallRangeLast,
                ValueEdges.SmallDateTimeMax,
                "smalldatetime の範囲の最後の値ちょうど（分ちょうどで範囲の中なので、列の型のまま束縛できる）"
            ),
            new(
                SmallPastLast,
                ValueEdges.SmallDateTimeMax.AddSeconds(30),
                "smalldatetime の範囲の最後の値の 30 秒後（分へ丸めると 2079-06-07 0:00 になり範囲の外へあふれる。列の型で束縛すると断られる）"
            ),
            new(
                SmallBeforeFirst,
                ValueEdges.SmallDateTimeMin.AddSeconds(-1),
                "smalldatetime の範囲の最初の値の 1 秒前（分へ丸めると範囲の最初の値になる範囲の外の値。列の型で束縛すると断られる）"
            ),
        ];
    }

    /// <summary>基準の日から決まるプールの値を名前で引く</summary>
    /// <param name="anchorDay">基準の日</param>
    /// <param name="name">名前（<see cref="Names"/> のどれか）</param>
    /// <exception cref="ArgumentException">プールに無い名前</exception>
    public static PoolValue<DateTime> Named(DateTime anchorDay, string name) =>
        Around(anchorDay).FirstOrDefault(value => value.Name == name)
        ?? throw new ArgumentException($"日時の値のプールに {name} が無い", nameof(name));

    /// <summary>値の Kind を付け替える（tick は変えない）</summary>
    /// <param name="value">プールの値</param>
    /// <param name="kind">付ける Kind</param>
    public static PoolValue<DateTime> WithKind(this PoolValue<DateTime> value, DateTimeKind kind)
    {
        ArgumentNullException.ThrowIfNull(value);
        return value with { Value = DateTime.SpecifyKind(value.Value, kind) };
    }
}
