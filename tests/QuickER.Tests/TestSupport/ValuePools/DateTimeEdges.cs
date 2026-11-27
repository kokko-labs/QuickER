using System;

namespace QuickER.Tests.TestSupport.ValuePools;

/// <summary>
/// 日時の範囲の端・1 tick・datetime の刻みの名前つきの値（エンジン・表のテストのソースが日時の端を直に書かずに引く正本）
/// </summary>
/// <remarks>
/// SQL Server の datetime・smalldatetime の範囲の端そのもの（1753-01-01・9999-12-31 23:59:59.997・1900-01-01・2079-06-06 23:59）は
/// <see cref="ValueEdges"/> が持つ。ここはそこから組める値（端の前後）と、年・tick・刻みの数を名前にする。
/// 組んだ値は Kind が Unspecified の同じ tick（<c>new DateTime(...)</c> で書いた値と同じ）。
/// </remarks>
public static class DateTimeEdges
{
    /// <summary>暦の最初の年（0001 年）。水準 <c>dt-calendar-end</c>：<c>new DateTimeOffset(年, …)</c> の年の引数で、時刻だけを運ぶ値や暦の端の値になる</summary>
    public const int CalendarFirstYear = 1;

    /// <summary>暦の最後の年（9999 年）。水準 <c>dt-calendar-end</c>：どの日時の型も、これを超える値を持てない</summary>
    public const int CalendarLastYear = 9999;

    /// <summary>
    /// datetime の 1 単位（1/300 秒）の tick（端数を持つ）。水準 <c>dt-legacy-off-grid</c>・<c>dt-legacy-between-read-back-and-step</c>：
    /// datetime が値を読み戻す刻みで、これに乗らない値は列の型で束縛すると丸められる
    /// </summary>
    public const double LegacyStepTicks = TimeSpan.TicksPerSecond / 300.0;

    /// <summary>
    /// 読み戻す値（.003）のすぐ後ろの窓の tick（.0031 秒）。水準 <c>dt-legacy-between-read-back-and-step</c>：
    /// SQL Server の datetime の列と比べると、値のまま datetime2 で束縛するか読み戻す値の間へ置き直すかで答えが割れる
    /// </summary>
    public const long LegacyWindowAfterReadBackTicks = 31_000;

    /// <summary>
    /// 読み戻す値（.007）のすぐ手前の窓の tick（.0068 秒）。水準 <c>dt-legacy-between-read-back-and-step</c>：
    /// <see cref="LegacyWindowAfterReadBackTicks"/> と同じく、列の刻みの間の値の束縛で答えが割れる
    /// </summary>
    public const long LegacyWindowBeforeReadBackTicks = 68_000;

    /// <summary>
    /// datetime の範囲の最後（.997）を超え、datetime の刻みへ丸めると範囲の最後へ戻る値（9999-12-31 23:59:59.998）。
    /// 水準 <c>dt-past-sql-datetime-max</c>：読み戻せない値で、列の型で束縛すると範囲の最後の行に等しくなる
    /// </summary>
    public static readonly DateTime SqlDateTimeMaxRoundingDown =
        ValueEdges.SqlDateTimeMax.AddMilliseconds(1);

    /// <summary>
    /// datetime の刻みへ丸めると範囲の外（10000 年）へあふれる値（9999-12-31 23:59:59.999）。
    /// 水準 <c>dt-past-sql-datetime-max</c>：datetime の型では送れず、datetime2 で束縛し直す
    /// </summary>
    public static readonly DateTime SqlDateTimeMaxRoundingOverflow =
        ValueEdges.SqlDateTimeMax.AddMilliseconds(2);

    /// <summary>SQL Server の datetime の範囲の最初（1753-01-01）の 1 日前（1752-12-31）。水準 <c>dt-before-sql-datetime-min</c>：datetime の型で送ると断られる</summary>
    public static readonly DateTime DayBeforeSqlDateTimeMin = ValueEdges.SqlDateTimeMin.AddDays(-1);

    /// <summary>
    /// datetime の範囲の最初（1753-01-01）より前で、datetime の刻みへ丸めると範囲の最初へ上がる値（1752-12-31 23:59:59.999）。
    /// 水準 <c>dt-before-sql-datetime-min</c>：範囲の外の値で、丸めの向きだけで範囲に入る
    /// </summary>
    public static readonly DateTime SqlDateTimeMinRoundingUp =
        ValueEdges.SqlDateTimeMin.AddMilliseconds(-1);

    /// <summary>
    /// smalldatetime の範囲の最後（2079-06-06 23:59）を超え、分へ丸めると範囲の最後へ戻る値（2079-06-06 23:59:20）。
    /// 水準 <c>dt-past-smalldatetime-max</c>：29.998 秒以下は切り下げる規則で範囲に収まる
    /// </summary>
    public static readonly DateTime SmallDateTimeMaxRoundingDown =
        ValueEdges.SmallDateTimeMax.AddSeconds(20);

    /// <summary>smalldatetime の範囲の最後の日の翌日（2079-06-07）。水準 <c>dt-past-smalldatetime-max</c>：smalldatetime の型で送ると断られる</summary>
    public static readonly DateTime DayAfterSmallDateTimeMax =
        ValueEdges.SmallDateTimeMax.Date.AddDays(1);

    /// <summary>
    /// smalldatetime の範囲の最初（1900-01-01）の 1 分前の分ちょうどの値（1899-12-31 23:59:00）。
    /// 水準 <c>dt-before-smalldatetime-min</c>：丸めても範囲に入らない範囲の外の値
    /// </summary>
    public static readonly DateTime MinuteBeforeSmallDateTimeMin =
        ValueEdges.SmallDateTimeMin - TimeSpan.FromMinutes(1);

    /// <summary>
    /// smalldatetime の範囲の最初（1900-01-01）より前で、分へ丸めると範囲の最初へ上がる値（1899-12-31 23:59:40）。
    /// 水準 <c>dt-before-smalldatetime-min</c>：29.999 秒以上は切り上げる規則で範囲に入る
    /// </summary>
    public static readonly DateTime SmallDateTimeMinRoundingUp =
        ValueEdges.SmallDateTimeMin - TimeSpan.FromSeconds(20);

    /// <summary>
    /// smalldatetime の範囲の最初（1900-01-01）より前で、分の中のちょうど半分（30 秒）にある値（1899-12-31 23:59:30）。
    /// 水準 <c>dt-before-smalldatetime-min</c>・<c>dt-small-rounding-boundary</c>：切り上げの境の上にあり、丸めると範囲の最初へ上がる
    /// </summary>
    public static readonly DateTime SmallDateTimeMinRoundingUpAtHalfMinute =
        ValueEdges.SmallDateTimeMin - TimeSpan.FromSeconds(30);

    /// <summary>値を 1 tick 進める。水準 <c>dt-sub-microsecond</c>：マイクロ秒の列・刻みの粗い列へ書くと端数が落ちる</summary>
    /// <param name="value">元の値（Kind を保つ）</param>
    public static DateTime OneTickAfter(DateTime value) => value.AddTicks(1);

    /// <summary>値を 1 tick 進める。水準 <c>dto-sub-microsecond</c>：マイクロ秒の列・刻みの粗い列へ書くと端数が落ちる</summary>
    /// <param name="value">元の値（時差を保つ）</param>
    public static DateTimeOffset OneTickAfter(DateTimeOffset value) => value.AddTicks(1);

    /// <summary>値を 1 tick 戻す。水準 <c>dt-tick-before-midnight</c>・<c>dt-before-sql-datetime-min</c> など：範囲の端の 1 つ外の値を作る</summary>
    /// <param name="value">元の値（Kind を保つ）</param>
    public static DateTime OneTickBefore(DateTime value) => value.AddTicks(-1);
}
