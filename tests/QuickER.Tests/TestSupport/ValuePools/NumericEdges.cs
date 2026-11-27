using System;

namespace QuickER.Tests.TestSupport.ValuePools;

/// <summary>TimeSpan の端の名前つきの値（日の長さと、tick 単位の型の端）</summary>
public static class TimeSpanEdges
{
    /// <summary>
    /// 1 日（24:00:00）。水準 <c>ts-one-day</c>：PostgreSQL の time は持て、SQL Server の time は持てない
    /// （この値と比べると列の型で束縛できず、範囲の外の値の比較として畳まれる）
    /// </summary>
    public static readonly TimeSpan OneDay = TimeSpan.FromDays(1);

    /// <summary>
    /// 型の最大（<see cref="TimeSpan.MaxValue"/>）を 10 tick（マイクロ秒）の倍数へ切り下げた値。水準 <c>ts-beyond-day</c>：
    /// マイクロ秒の粒度の列（PostgreSQL の interval・MySQL の time）は型の最大を端数なしでは持てない
    /// </summary>
    public static readonly TimeSpan MaxWholeMicroseconds = TimeSpan.FromTicks(
        TimeSpan.MaxValue.Ticks / 10 * 10
    );

    /// <summary>
    /// 型の最小（<see cref="TimeSpan.MinValue"/>）を 10 tick（マイクロ秒）の倍数へ切り上げた値。水準 <c>ts-negative</c>：
    /// マイクロ秒の粒度の列は型の最小を端数なしでは持てない
    /// </summary>
    public static readonly TimeSpan MinWholeMicroseconds = TimeSpan.FromTicks(
        TimeSpan.MinValue.Ticks / 10 * 10
    );
}

/// <summary>浮動小数点の端の名前つきの値（負のゼロと、非正規化数の最小）</summary>
public static class FloatingEdges
{
    /// <summary>double の負のゼロ。水準 <c>fp-negative-zero</c>：0.0 と比べると等しいが、文字列・ビットでは別の値で、束縛・書式・比較の分岐が割れる</summary>
    public const double NegativeZeroDouble = -0.0d;

    /// <summary>float の負のゼロ。水準 <c>fp-negative-zero</c>：<see cref="NegativeZeroDouble"/> と同じ</summary>
    public const float NegativeZeroSingle = -0.0f;

    /// <summary>double の最小の非正規化数。水準 <c>fp-subnormal</c>：10 進の綴り・ドライバの往復で 0 に落ちたり別の値になったりする</summary>
    public const double DoubleEpsilon = double.Epsilon;

    /// <summary>float の最小の非正規化数。水準 <c>fp-subnormal</c>：<see cref="DoubleEpsilon"/> と同じ（float の列・変換で 0 に落ちうる）</summary>
    public const float SingleEpsilon = float.Epsilon;
}

/// <summary>
/// 2^24・2^53 とその次の整数（float・double が整数を正確に持てる限界）。型を変えて使うときは、定数からの明示の変換
/// （<c>(float)TwoPow24</c>・<c>(double)TwoPow53</c>・<c>(long)TwoPow24Plus1</c>）で同じ値・同じ型にする
/// </summary>
public static class PowerOfTwoEdges
{
    /// <summary>2^24（16777216）。float へ変換した値（<c>(float)TwoPow24</c>）が水準 <c>fp-integer-beyond-2pow24</c>：float が隣の整数を表せなくなる最初の整数で、整数の列との比較が丸めで割れる</summary>
    public const int TwoPow24 = 1 << 24;

    /// <summary>2^24 + 1（16777217）。水準 <c>int-beyond-float-precision</c>：float へ丸めると 2^24 になり、整数の列との比較が C# と割れる</summary>
    public const int TwoPow24Plus1 = TwoPow24 + 1;

    /// <summary>2^53（9007199254740992）。double へ変換した値（<c>(double)TwoPow53</c>）が水準 <c>fp-integer-beyond-2pow53</c>：double が隣の整数を表せなくなる最初の整数で、bigint の列との比較が丸めで割れる</summary>
    public const long TwoPow53 = 1L << 53;

    /// <summary>2^53 + 1（9007199254740993）。水準 <c>int-beyond-double-precision</c>：double へ丸めると 2^53 になり、整数の列との比較が C# と割れる</summary>
    public const long TwoPow53Plus1 = TwoPow53 + 1;
}
