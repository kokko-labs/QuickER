using System;
using System.Collections.Generic;
using System.Linq;

namespace QuickER.Tests.TestSupport.ValuePools;

/// <summary>decimal の値のプール：列の行に当たる整数の値・小数の値・列のスケールの外の値と、long の範囲の外の値</summary>
public static class DecimalPool
{
    /// <summary>列の行に当たる整数の値</summary>
    public const string Integral = "integral";

    /// <summary>小数部を持つ値（整数の列とはどの行にも等しくない）</summary>
    public const string Fraction = "fraction";

    /// <summary>列の型に入らない値（スケールの外か long の範囲の外）</summary>
    public const string BeyondColumnRange = "beyond-column-range";

    /// <summary>
    /// 整数の値を持つ decimal（整数の列と比べると、C# は列を decimal へ広げて比べる。<see cref="Integral"/> は整数の列では列の型の整数）
    /// </summary>
    public const string IntegralDecimal = "integral-decimal";

    /// <summary>整数の値を持つ decimal のプールの値</summary>
    /// <param name="hit">列の行に当たる整数の値</param>
    public static PoolValue<decimal> IntegralDecimalOf(decimal hit) =>
        hit == decimal.Truncate(hit)
            ? new(
                IntegralDecimal,
                hit,
                "整数の値の decimal（整数の列の行にちょうど等しい。文字列や小数で比べると等しくならない）"
            )
            : throw new ArgumentException("整数の値でない", nameof(hit));

    /// <summary>プールの値（宣言の順）</summary>
    /// <param name="hit">列の行に当たる値（整数の値）</param>
    /// <param name="scale">列のスケール（範囲の外の値はこの桁の 1 つ下に端数を持つ）</param>
    public static IReadOnlyList<PoolValue<decimal>> Around(decimal hit, int scale)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(scale);

        var finer = 4m;

        for (var digit = 0; digit <= scale; digit++)
        {
            finer /= 10m;
        }

        var smallest = 1m;

        for (var digit = 0; digit < scale; digit++)
        {
            smallest /= 10m;
        }

        return
        [
            new(Integral, hit, "整数の値（行にちょうど等しい）"),
            new(Fraction, hit + 0.5m, "小数部を持つ値（整数の列へ切ると行に等しくなる）"),
            new(
                BeyondColumnRange,
                hit + finer,
                "列のスケールより 1 桁細かい端数を持つ値（列の桁へ丸めると行に等しくなる）"
            ),
            new(Zero, 0m, "0（整数部の桁を数える 0 の腕。どの行にも等しくない）"),
            new(Negative, -hit, "当たる値の符号を変えた負の値（整数部の桁を絶対値で数える腕）"),
            new(
                SmallestUnit,
                smallest,
                "列のスケールちょうどの最小の単位（整数部が 0 で、小数の桁がスケールに等しい境目）"
            ),
            new(
                FractionBelowOne,
                0.999m,
                "1 未満の小数（整数部が 0 で、整数へ丸めると 1 になる値。整数の列と比べると、丸めて比べたときだけ 1 の行に当たる）"
            ),
        ];
    }

    /// <summary>1 未満の小数（0.999）</summary>
    public const string FractionBelowOne = "fraction-below-one";

    /// <summary>2^53 を超える整数の間の小数（double へ丸めると隣の整数になる）</summary>
    public const string BeyondDoubleFraction = "beyond-double-fraction";

    /// <summary>long の上限の近くの小数（double へ丸めると 2^63 になる）</summary>
    public const string NearLongMaxFraction = "near-long-max-fraction";

    /// <summary>
    /// 2^53 を超える整数の間の小数（9999999999999999.5。bigint の列の行 10^16 と 10^16-1 の間で、double へ丸めると 10^16 になる）
    /// </summary>
    public static PoolValue<decimal> BeyondDoubleFractionValue { get; } =
        new(
            BeyondDoubleFraction,
            9999999999999999.5m,
            "2^53 を超える整数の間の小数（double へ丸めると隣の整数 10^16 になり、その行と等しくなる）"
        );

    /// <summary>long の上限の近くの小数（9223372036854775806.5。double へ丸めると 2^63 になり、long の上限の行より大きくなる）</summary>
    public static PoolValue<decimal> NearLongMaxFractionValue { get; } =
        new(
            NearLongMaxFraction,
            (decimal)long.MaxValue - 0.5m,
            "long の上限の近くの小数（double へ丸めると 2^63 になり、long の上限の行が小さい側へ移る）"
        );

    /// <summary>2^53 を超える整数で、スケールを持つ decimal（9999999999999999.0）</summary>
    public const string BeyondDoubleIntegral = "beyond-double-integral";

    /// <summary>
    /// 2^53 を超える整数で、スケールを持つ decimal（9999999999999999.0。bigint の列の行 10^16-1 にちょうど等しく、小数部つきの綴りを double へ
    /// 丸めると 10^16 になる）
    /// </summary>
    public static PoolValue<decimal> BeyondDoubleIntegralValue { get; } =
        new(
            BeyondDoubleIntegral,
            9999999999999999.0m,
            "2^53 を超える整数の decimal（列の型の整数として束縛すれば行にちょうど等しく、小数部つきの綴りのまま比べると double へ丸めて隣の行と等しくなる）"
        );

    /// <summary>0</summary>
    public const string Zero = "zero";

    /// <summary>当たる値の符号を変えた負の値</summary>
    public const string Negative = "negative";

    /// <summary>列のスケールちょうどの最小の単位（decimal(10,2) なら 0.01）</summary>
    public const string SmallestUnit = "smallest-unit";

    /// <summary>列の精度の外の値（整数部の桁が列の整数部の桁より 1 多い）</summary>
    public const string BeyondPrecision = "beyond-precision";

    /// <summary>列の精度の外の値（decimal(10,2) なら整数部 9 桁の 123456789.5）</summary>
    /// <param name="precision">列の精度</param>
    /// <param name="scale">列のスケール</param>
    public static PoolValue<decimal> BeyondPrecisionOf(int precision, int scale)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(scale);
        ArgumentOutOfRangeException.ThrowIfLessThan(precision, scale + 1);

        var integral = 0m;

        for (var digit = 1; digit <= precision - scale + 1; digit++)
        {
            integral = integral * 10m + digit % 10;
        }

        return new(
            BeyondPrecision,
            integral + 0.5m,
            "整数部の桁が列の整数部の桁より 1 多い値（列の型で束縛すると桁あふれになり、どの行にも等しくない）"
        );
    }

    /// <summary>long の最小より 1 小さい値（bigint の列の下側の範囲の外。名前は <see cref="IntegerPool.BelowTypeMin"/>）</summary>
    public static PoolValue<decimal> BelowLong { get; } =
        new(
            IntegerPool.BelowTypeMin,
            (decimal)long.MinValue - 1m,
            "long の最小を 1 下回る整数の値（bigint へ落とすと桁あふれになる。下側の範囲の外）"
        );

    /// <summary>long の範囲の外の値（bigint の列の型の範囲の外）</summary>
    public static PoolValue<decimal> BeyondLong { get; } =
        new(
            BeyondColumnRange,
            100000000000000000000m,
            "long の範囲の外の整数の値（bigint へ落とすと桁あふれになり、decimal(38,19) の整数部にも入らない）"
        );

    /// <summary>プールの値を名前で引く</summary>
    /// <param name="hit">列の行に当たる値</param>
    /// <param name="scale">列のスケール</param>
    /// <param name="name">名前</param>
    /// <exception cref="ArgumentException">プールに無い名前</exception>
    public static PoolValue<decimal> Named(decimal hit, int scale, string name) =>
        Around(hit, scale).FirstOrDefault(value => value.Name == name)
        ?? throw new ArgumentException($"decimal の値のプールに {name} が無い", nameof(name));
}
