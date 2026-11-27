using System;

namespace QuickER.Tests.TestSupport.ValuePools;

/// <summary>日時・時差つきの日時・時間の水準</summary>
public static partial class ValueEdges
{
    /// <summary>SQL Server の datetime の範囲の最初（1753-01-01）</summary>
    public static readonly DateTime SqlDateTimeMin = new(1753, 1, 1);

    /// <summary>SQL Server の datetime の範囲の最後（9999-12-31 23:59:59.997）</summary>
    public static readonly DateTime SqlDateTimeMax = new(9999, 12, 31, 23, 59, 59, 997);

    /// <summary>SQL Server の smalldatetime の範囲の最初（1900-01-01）</summary>
    public static readonly DateTime SmallDateTimeMin = new(1900, 1, 1);

    /// <summary>SQL Server の smalldatetime の範囲の最後（2079-06-06 23:59）</summary>
    public static readonly DateTime SmallDateTimeMax = new(2079, 6, 6, 23, 59, 0);

    /// <summary>MySQL の time の範囲の端（838:59:59）</summary>
    public static readonly TimeSpan MySqlTimeMax = new(34, 22, 59, 59);

    /// <summary>datetime の刻みの 10 ミリ秒の tick 数</summary>
    private const long TenMilliseconds = 10 * TimeSpan.TicksPerMillisecond;

    /// <summary>smalldatetime が分へ丸める境目（分の中の位置 29.998 秒）の tick 数</summary>
    private const long SmallRoundingFrom = 29_998 * TimeSpan.TicksPerMillisecond;

    /// <summary>smalldatetime が分へ丸める境目の終わり（分の中の位置 30 秒）の tick 数</summary>
    private const long SmallRoundingTo = 30 * TimeSpan.TicksPerSecond;

    private static EdgeRule[] DateTimeRules =>
        [
            Edge<DateTime>(
                "dt-kind-utc",
                ValueFamily.DateTime,
                "Kind が Utc の値（Npgsql の timestamp・timestamptz の振り分けと、Kind を外して送る変換が分岐する）",
                v => v.Kind == DateTimeKind.Utc
            ),
            Edge<DateTime>(
                "dt-kind-local",
                ValueFamily.DateTime,
                "Kind が Local の値（Kind を外して送る変換と、時差で換算する比較が分岐する）",
                v => v.Kind == DateTimeKind.Local
            ),
            Edge<DateTime>(
                "dt-midnight",
                ValueFamily.DateTime,
                "時刻を持たない値（date の列とちょうど等しくなりうる・date の型で束縛できる）",
                v => v.TimeOfDay == TimeSpan.Zero
            ),
            Edge<DateTime>(
                "dt-time-of-day",
                ValueFamily.DateTime,
                "時刻を持つ値（date の列と比べると、date で束縛すると時刻が落ちる）",
                v => v.TimeOfDay != TimeSpan.Zero
            ),
            Edge<DateTime>(
                "dt-tick-before-midnight",
                ValueFamily.DateTime,
                "その日の最後の tick（datetime・smalldatetime へ丸めると翌日になり、マイクロ秒へ切ると端数が落ちる）",
                v => v.TimeOfDay.Ticks == TimeSpan.TicksPerDay - 1
            ),
            Edge<DateTime>(
                "dt-sub-microsecond",
                ValueFamily.DateTime,
                "マイクロ秒より細かい端数を持つ値（PostgreSQL・MySQL・datetime2(6) が切り捨て・丸める）",
                v => v.Ticks % 10 != 0
            ),
            ColumnEdge<DateTime>(
                "dt-beyond-column-scale",
                ValueFamily.DateTime,
                "列の小数秒の桁より細かい端数を持つ値（列の型で束縛すると丸められ、値の型なら列と比べ直す）",
                column => column.Scale is >= 0 and <= 7,
                (v, column) => v.Ticks % Pow10(7 - column.Scale!.Value) != 0
            ),
            Edge<DateTime>(
                "dt-legacy-read-back",
                ValueFamily.DateTime,
                "datetime が書いたまま読み戻す値（ミリ秒ちょうどで 1 の位が 3・7。列の型で束縛してよい値）",
                v => v.Ticks % TimeSpan.TicksPerMillisecond == 0 && v.Millisecond % 10 is 3 or 7
            ),
            Edge<DateTime>(
                "dt-legacy-off-grid",
                ValueFamily.DateTime,
                "datetime の読み戻しの格子の外の値（datetime で束縛すると 1/300 秒へ丸められ、読み戻す値の間へ置き直す）",
                v =>
                    v.Ticks % TimeSpan.TicksPerMillisecond != 0
                    || v.Millisecond % 10 is not (0 or 3 or 7)
            ),
            Edge<DateTime>(
                "dt-legacy-between-read-back-and-step",
                ValueFamily.DateTime,
                "datetime の読み戻す値と 1/300 秒の刻みの間の値（.0031 など。datetime2 で送るとサーバーが正確な刻みで比べ、C# と割れる）",
                v =>
                    (v.Ticks % TenMilliseconds) is > 30_000 and < 33_334
                    || (v.Ticks % TenMilliseconds) is > 66_666 and < 70_000
            ),
            Edge<DateTime>(
                "dt-small-off-minute",
                ValueFamily.DateTime,
                "分ちょうどでない値（smalldatetime の列の型で束縛すると分へ丸められる）",
                v => v.Ticks % TimeSpan.TicksPerMinute != 0
            ),
            Edge<DateTime>(
                "dt-small-rounding-boundary",
                ValueFamily.DateTime,
                "smalldatetime が分を切り上げるか切り捨てるかの境目（分の中の 29.998〜30 秒）",
                v =>
                    (v.Ticks % TimeSpan.TicksPerMinute)
                        is >= SmallRoundingFrom
                            and <= SmallRoundingTo
            ),
            Edge<DateTime>(
                "dt-before-sql-datetime-min",
                ValueFamily.DateTime,
                "datetime の範囲の最初より前の値（datetime で送ると断られ、datetime2 で束縛し直す）",
                v => v < SqlDateTimeMin
            ),
            Edge<DateTime>(
                "dt-before-smalldatetime-min",
                ValueFamily.DateTime,
                "smalldatetime の範囲の最初より前の値（smalldatetime で送ると断られる）",
                v => v < SmallDateTimeMin
            ),
            Edge<DateTime>(
                "dt-past-sql-datetime-max",
                ValueFamily.DateTime,
                "datetime の範囲の最後を超える値（丸めると範囲の外になり、datetime で送ると断られる）",
                v => v > SqlDateTimeMax
            ),
            Edge<DateTime>(
                "dt-past-smalldatetime-max",
                ValueFamily.DateTime,
                "smalldatetime の範囲の最後を超える値（smalldatetime で送ると断られる）",
                v => v > SmallDateTimeMax
            ),
            Edge<DateTime>(
                "dt-sql-datetime-range-end",
                ValueFamily.DateTime,
                "datetime の範囲の端にちょうど等しい値（範囲の中か外かの判定の境目）",
                v => v == SqlDateTimeMin || v == SqlDateTimeMax
            ),
            Edge<DateTime>(
                "dt-smalldatetime-range-end",
                ValueFamily.DateTime,
                "smalldatetime の範囲の端にちょうど等しい値（範囲の中か外かの判定の境目）",
                v => v == SmallDateTimeMin || v == SmallDateTimeMax
            ),
            Edge<DateTime>(
                "dt-calendar-end",
                ValueFamily.DateTime,
                "暦の端の年（0001・9999。日付の加算・時差の換算・型の範囲が桁あふれしうる）",
                v => v.Year is 1 or 9999
            ),
            Edge<DateTime>(
                "dt-leap-day",
                ValueFamily.DateTime,
                "うるう日（日付部品・日付の加算と、日付の文字列の読み書きが分岐しうる）",
                v => v.Month == 2 && v.Day == 29
            ),
        ];

    private static EdgeRule[] DateTimeOffsetRules =>
        [
            Edge<DateTimeOffset>(
                "dto-offset-zero",
                ValueFamily.DateTimeOffset,
                "時差 0 の値（Npgsql の timestamptz が受け付ける唯一の時差）",
                v => v.Offset == TimeSpan.Zero
            ),
            Edge<DateTimeOffset>(
                "dto-offset-positive",
                ValueFamily.DateTimeOffset,
                "正の時差の値（局所の時刻の順と瞬間の順が割れうる）",
                v => v.Offset > TimeSpan.Zero
            ),
            Edge<DateTimeOffset>(
                "dto-offset-negative",
                ValueFamily.DateTimeOffset,
                "負の時差の値（局所の時刻の順と瞬間の順が割れうる）",
                v => v.Offset < TimeSpan.Zero
            ),
            Edge<DateTimeOffset>(
                "dto-offset-extreme",
                ValueFamily.DateTimeOffset,
                "時差の大きい値（12 時間以上。UTC の日付が変わり、型の範囲の端で桁あふれしうる）",
                v => v.Offset.Duration() >= TimeSpan.FromHours(12)
            ),
            Edge<DateTimeOffset>(
                "dto-local-date-differs",
                ValueFamily.DateTimeOffset,
                "局所の日付と UTC の日付が違う値（日付部品と時差を落とす比較が割れる）",
                v => v.Date != v.UtcDateTime.Date
            ),
            Edge<DateTimeOffset>(
                "dto-sub-microsecond",
                ValueFamily.DateTimeOffset,
                "マイクロ秒より細かい端数を持つ値（PostgreSQL・datetimeoffset(6) が切り捨て・丸める）",
                v => v.Ticks % 10 != 0
            ),
            ColumnEdge<DateTimeOffset>(
                "dto-beyond-column-scale",
                ValueFamily.DateTimeOffset,
                "列の小数秒の桁より細かい端数を持つ値（列の型で束縛すると丸められる）",
                column => column.Scale is >= 0 and <= 7,
                (v, column) => v.Ticks % Pow10(7 - column.Scale!.Value) != 0
            ),
            Edge<DateTimeOffset>(
                "dto-calendar-end",
                ValueFamily.DateTimeOffset,
                "暦の端の年（0001・9999。時差の換算が範囲の外へ出うる）",
                v => v.Year is 1 or 9999 || v.UtcDateTime.Year is 1 or 9999
            ),
        ];

    private static EdgeRule[] TimeSpanRules =>
        [
            Edge<TimeSpan>(
                "ts-zero",
                ValueFamily.TimeSpan,
                "0（time の列の範囲の最初）",
                v => v == TimeSpan.Zero
            ),
            Edge<TimeSpan>(
                "ts-negative",
                ValueFamily.TimeSpan,
                "負の値（SQL Server・PostgreSQL の time の範囲の外。比較を C# の答えへ畳むか interval で送る）",
                v => v < TimeSpan.Zero
            ),
            Edge<TimeSpan>(
                "ts-day-end",
                ValueFamily.TimeSpan,
                "1 日の最後のマイクロ秒の中の値（time(n) へ丸めると 24 時間になりうる）",
                v => v >= TimeSpan.FromDays(1) - TimeSpan.FromTicks(10) && v < TimeSpan.FromDays(1)
            ),
            Edge<TimeSpan>(
                "ts-one-day",
                ValueFamily.TimeSpan,
                "ちょうど 1 日（PostgreSQL の time は持て、SQL Server の time は持てない）",
                v => v == TimeSpan.FromDays(1)
            ),
            Edge<TimeSpan>(
                "ts-beyond-day",
                ValueFamily.TimeSpan,
                "1 日を超える値（time の列の範囲の外。interval で送ると日の部分が黙って落ちうる）",
                v => v > TimeSpan.FromDays(1)
            ),
            Edge<TimeSpan>(
                "ts-sub-microsecond",
                ValueFamily.TimeSpan,
                "マイクロ秒より細かい端数を持つ値（PostgreSQL・MySQL・time(6) が切り捨て・丸める）",
                v => v.Ticks % 10 != 0
            ),
            ColumnEdge<TimeSpan>(
                "ts-beyond-column-scale",
                ValueFamily.TimeSpan,
                "列の小数秒の桁より細かい端数を持つ値（列の型で束縛すると丸められる）",
                column => column.Scale is >= 0 and <= 7,
                (v, column) => v.Ticks % Pow10(7 - column.Scale!.Value) != 0
            ),
            Edge<TimeSpan>(
                "ts-mysql-range-end",
                ValueFamily.TimeSpan,
                "MySQL の time の範囲の端にちょうど等しい値（±838:59:59）",
                v => v == MySqlTimeMax || v == -MySqlTimeMax
            ),
            Edge<TimeSpan>(
                "ts-beyond-mysql-range",
                ValueFamily.TimeSpan,
                "MySQL の time の範囲の外の値（書くと断られるか端へ丸められる）",
                v => v > MySqlTimeMax || v < -MySqlTimeMax
            ),
        ];

    /// <summary>10 の n 乗（n は 0〜7）</summary>
    private static long Pow10(int n)
    {
        var value = 1L;

        for (var i = 0; i < n; i++)
        {
            value *= 10;
        }

        return value;
    }
}
