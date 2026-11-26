using System;
using System.Collections.Generic;
using System.Linq;

namespace QuickER.Tests.TestSupport.ValuePools;

/// <summary>decimal の値の池：列の行に当たる整数の値・小数の値・列のスケールの外の値と、long の範囲の外の値</summary>
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

    /// <summary>整数の値を持つ decimal の池の値</summary>
    /// <param name="hit">列の行に当たる整数の値</param>
    public static PoolValue<decimal> IntegralDecimalOf(decimal hit) =>
        hit == decimal.Truncate(hit)
            ? new(
                IntegralDecimal,
                hit,
                "整数の値の decimal（整数の列の行にちょうど等しい。文字列や小数で比べると等しくならない）"
            )
            : throw new ArgumentException("整数の値でない", nameof(hit));

    /// <summary>池の値（宣言の順）</summary>
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

        return
        [
            new(Integral, hit, "整数の値（行にちょうど等しい）"),
            new(Fraction, hit + 0.5m, "小数部を持つ値（整数の列へ切ると行に等しくなる）"),
            new(
                BeyondColumnRange,
                hit + finer,
                "列のスケールより 1 桁細かい端数を持つ値（列の桁へ丸めると行に等しくなる）"
            ),
        ];
    }

    /// <summary>long の範囲の外の値（bigint の列の型の範囲の外）</summary>
    public static PoolValue<decimal> BeyondLong { get; } =
        new(
            BeyondColumnRange,
            100000000000000000000m,
            "long の範囲の外の整数の値（bigint へ落とすと桁あふれになり、decimal(38,19) の整数部にも入らない）"
        );

    /// <summary>池の値を名前で引く</summary>
    /// <param name="hit">列の行に当たる値</param>
    /// <param name="scale">列のスケール</param>
    /// <param name="name">名前</param>
    /// <exception cref="ArgumentException">池に無い名前</exception>
    public static PoolValue<decimal> Named(decimal hit, int scale, string name) =>
        Around(hit, scale).FirstOrDefault(value => value.Name == name)
        ?? throw new ArgumentException($"decimal の値の池に {name} が無い", nameof(name));
}
