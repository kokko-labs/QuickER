using System;
using System.Collections.Generic;
using System.Linq;

namespace QuickER.Tests.TestSupport.ValuePools;

/// <summary>整数の値の池：列の行に当たる整数と、列の型の範囲の外の整数</summary>
public static class IntegerPool
{
    /// <summary>列の行に当たる整数（列の型のまま比べる）</summary>
    public const string Integral = "integral";

    /// <summary>列の型の範囲の外の整数（列の型へ落とすと桁あふれか別の値になる）</summary>
    public const string BeyondColumnRange = "beyond-column-range";

    /// <summary>池の値（宣言の順。列の型の上限が long の上限なら範囲の外の整数は無い＝<see cref="DecimalPool.BeyondLong"/> を使う）</summary>
    /// <param name="hit">列の行に当たる値</param>
    /// <param name="columnMax">列の型の上限</param>
    public static IReadOnlyList<PoolValue<long>> Around(long hit, long columnMax) =>
        columnMax == long.MaxValue
            ? [new(Integral, hit, "列の型のままの値（行にちょうど等しい）")]
            :
            [
                new(Integral, hit, "列の型のままの値（行にちょうど等しい）"),
                new(
                    BeyondColumnRange,
                    columnMax + 1,
                    "列の型の上限を 1 超える値（列の型で束縛すると桁あふれになる）"
                ),
            ];

    /// <summary>池の値を名前で引く</summary>
    /// <param name="hit">列の行に当たる値</param>
    /// <param name="columnMax">列の型の上限</param>
    /// <param name="name">名前</param>
    /// <exception cref="ArgumentException">池に無い名前</exception>
    public static PoolValue<long> Named(long hit, long columnMax, string name) =>
        Around(hit, columnMax).FirstOrDefault(value => value.Name == name)
        ?? throw new ArgumentException($"整数の値の池に {name} が無い", nameof(name));
}
