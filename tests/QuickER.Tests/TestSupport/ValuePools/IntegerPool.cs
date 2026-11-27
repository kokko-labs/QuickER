using System;
using System.Collections.Generic;
using System.Linq;

namespace QuickER.Tests.TestSupport.ValuePools;

/// <summary>整数の値のプール：列の行に当たる整数と、列の型の範囲の外の整数</summary>
public static class IntegerPool
{
    /// <summary>列の行に当たる整数（列の型のまま比べる）</summary>
    public const string Integral = "integral";

    /// <summary>列の型の範囲の外の整数（列の型へ落とすと桁あふれか別の値になる）</summary>
    public const string BeyondColumnRange = "beyond-column-range";

    /// <summary>プールの値（宣言の順。列の型の上限が long の上限なら範囲の外の整数は無い＝<see cref="DecimalPool.BeyondLong"/> を使う）</summary>
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

    /// <summary>列の型の最小の整数（列の型のまま比べる。下限の比較の腕）</summary>
    public const string TypeMin = "type-min";

    /// <summary>列の型の最小より 1 小さい整数（下側の範囲の外。列の型で束縛すると桁あふれになる）</summary>
    public const string BelowTypeMin = "below-type-min";

    /// <summary>
    /// 符号だけの変換（同じ幅か広い符号なしの型への変換）の境目：変換先の符号なしの型の最上位ビットだけが立つ値（uint は 2147483648・ulong は
    /// 9223372036854775808）。負の値を折り返した行だけがこの値以上になる
    /// </summary>
    public const string SignBoundary = "sign-boundary";

    /// <summary>符号だけの変換の変換先の符号なしの型の最大（uint は 4294967295・ulong は 18446744073709551615＝-1 を折り返した値）</summary>
    public const string UnsignedMax = "unsigned-max";

    /// <summary>符号だけの変換の変換先の符号なしの型の最大（-1 を折り返した値）</summary>
    /// <param name="unsignedType">変換先の符号なしの型（<see cref="uint"/> か <see cref="ulong"/>）</param>
    /// <exception cref="ArgumentOutOfRangeException">uint・ulong でない型</exception>
    public static PoolValue<ulong> UnsignedMaxOf(Type unsignedType) =>
        new(
            UnsignedMax,
            unsignedType == typeof(uint) ? uint.MaxValue
                : unsignedType == typeof(ulong) ? ulong.MaxValue
                : throw new ArgumentOutOfRangeException(
                    nameof(unsignedType),
                    unsignedType,
                    "uint・ulong だけ"
                ),
            "符号なしの型の最大（-1 の行を折り返した値で、折り返さないとどの行にも等しくない）"
        );

    /// <summary>列の型の最小の整数</summary>
    /// <param name="columnMin">列の型の最小</param>
    public static PoolValue<long> TypeMinOf(long columnMin) =>
        new(TypeMin, columnMin, "列の型の最小の値（行の値と同じ型のまま束縛できる）");

    /// <summary>列の型の最小より 1 小さい整数（列の型の最小が long の最小なら無い＝<see cref="DecimalPool.BelowLong"/> を使う）</summary>
    /// <param name="columnMin">列の型の最小</param>
    /// <exception cref="ArgumentOutOfRangeException">列の型の最小が long の最小</exception>
    public static PoolValue<long> BelowTypeMinOf(long columnMin) =>
        columnMin == long.MinValue
            ? throw new ArgumentOutOfRangeException(
                nameof(columnMin),
                "long の最小より 1 小さい整数は long に無い（DecimalPool.BelowLong を使う）"
            )
            : new(
                BelowTypeMin,
                columnMin - 1,
                "列の型の最小を 1 下回る値（列の型で束縛すると桁あふれになる。下側の範囲の外）"
            );

    /// <summary>符号だけの変換の境目（変換先の符号なしの型の最上位ビットだけが立つ値）</summary>
    /// <param name="unsignedType">変換先の符号なしの型（<see cref="uint"/> か <see cref="ulong"/>）</param>
    /// <exception cref="ArgumentOutOfRangeException">uint・ulong でない型</exception>
    public static PoolValue<ulong> SignBoundaryOf(Type unsignedType) =>
        new(
            SignBoundary,
            unsignedType == typeof(uint) ? (ulong)int.MaxValue + 1
                : unsignedType == typeof(ulong) ? (ulong)long.MaxValue + 1
                : throw new ArgumentOutOfRangeException(
                    nameof(unsignedType),
                    unsignedType,
                    "uint・ulong だけ"
                ),
            "符号なしの型の最上位ビットだけが立つ値（負の値を折り返した行だけがこの値以上になり、折り返さないとどの行にも当たらない）"
        );

    /// <summary>プールの値を名前で引く</summary>
    /// <param name="hit">列の行に当たる値</param>
    /// <param name="columnMax">列の型の上限</param>
    /// <param name="name">名前</param>
    /// <exception cref="ArgumentException">プールに無い名前</exception>
    public static PoolValue<long> Named(long hit, long columnMax, string name) =>
        Around(hit, columnMax).FirstOrDefault(value => value.Name == name)
        ?? throw new ArgumentException($"整数の値のプールに {name} が無い", nameof(name));
}
