using System;
using System.Collections.Generic;
using System.Linq;

namespace QuickER.Tests.TestSupport.ValuePools;

/// <summary>
/// 日時の値の池：基準の日（列のシードの日）から決まる端の値と、SQL Server の datetime の範囲の外の値。Kind は付け替えられる
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

    /// <summary>池の値の名前（宣言の順）</summary>
    public static IReadOnlyList<string> Names { get; } =
    [
        Midnight,
        TimeOfDay,
        TickBeforeMidnight,
        BetweenLegacyReadBacks,
        BeforeSqlDateTimeMin,
        PastSqlDateTimeMax,
    ];

    /// <summary>基準の日から決まる池の値（宣言の順）</summary>
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
        ];
    }

    /// <summary>基準の日から決まる池の値を名前で引く</summary>
    /// <param name="anchorDay">基準の日</param>
    /// <param name="name">名前（<see cref="Names"/> のどれか）</param>
    /// <exception cref="ArgumentException">池に無い名前</exception>
    public static PoolValue<DateTime> Named(DateTime anchorDay, string name) =>
        Around(anchorDay).FirstOrDefault(value => value.Name == name)
        ?? throw new ArgumentException($"日時の値の池に {name} が無い", nameof(name));

    /// <summary>値の Kind を付け替える（tick は変えない）</summary>
    /// <param name="value">池の値</param>
    /// <param name="kind">付ける Kind</param>
    public static PoolValue<DateTime> WithKind(this PoolValue<DateTime> value, DateTimeKind kind)
    {
        ArgumentNullException.ThrowIfNull(value);
        return value with { Value = DateTime.SpecifyKind(value.Value, kind) };
    }
}
