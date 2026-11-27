using System;
using System.Collections.Generic;
using System.Linq;

namespace QuickER.Tests.TestSupport.ValuePools;

/// <summary>
/// 値を変える明示の変換（縮める数値の変換・小数から整数）の元の値のプール：変換で値が変わる値（小数部・変換先の型の範囲の外）と、
/// 変わらない値（対照）
/// </summary>
/// <remarks>
/// プールの値は変換の「元の値」。値の側の変換（<c>(int)ratio</c>）では変換する値そのもの、列の側の変換（<c>(byte)x.Id</c>）では変換される
/// 行の値の例で、比べる値はその値を同じ変換に通した値になる（変換が値を変えると、変換を外して比べたときと答えが割れうる）。
/// </remarks>
public static class NarrowingPool
{
    /// <summary>変換で値が変わらない値（対照）</summary>
    public const string KeptByCast = "kept-by-cast";

    /// <summary>小数部を持ち、整数への変換で小数部が落ちる値</summary>
    public const string FractionCutByCast = "fraction-cut-by-cast";

    /// <summary>変換先の型の範囲の外の値（整数への変換で折り返すか、浮動小数点から整数への変換で範囲の端に止まる）</summary>
    public const string BeyondCastRange = "beyond-cast-range";

    /// <summary>プールの値の名前（宣言の順）</summary>
    public static IReadOnlyList<string> Names { get; } =
    [KeptByCast, FractionCutByCast, BeyondCastRange];

    /// <summary>プールの値（宣言の順）</summary>
    /// <param name="hit">列の行に当たる整数の値（変わらない値と、小数部を足す値の基準）</param>
    /// <param name="beyond">変換先の型の範囲の外の値（呼び出し側が列と変換の組から選ぶ）</param>
    public static IReadOnlyList<PoolValue<decimal>> Around(decimal hit, decimal beyond)
    {
        if (hit != decimal.Truncate(hit))
        {
            throw new ArgumentException("基準の値は整数", nameof(hit));
        }

        return
        [
            new(KeptByCast, hit, "整数の値（変換しても同じ値。変換を外しても答えは同じ）"),
            new(
                FractionCutByCast,
                hit + 0.7m,
                "小数部を持つ値（整数への変換で基準の値になり、変換を外すとどの整数の行とも等しくない）"
            ),
            new(
                BeyondCastRange,
                beyond,
                "変換先の型の範囲の外の値（変換で折り返すか範囲の端に止まり、変換を外すと別の値と比べる）"
            ),
        ];
    }

    /// <summary>プールの値を名前で引く</summary>
    /// <param name="hit">列の行に当たる整数の値</param>
    /// <param name="beyond">変換先の型の範囲の外の値</param>
    /// <param name="name">名前（<see cref="Names"/> のどれか）</param>
    /// <exception cref="ArgumentException">プールに無い名前</exception>
    public static PoolValue<decimal> Named(decimal hit, decimal beyond, string name) =>
        Around(hit, beyond).FirstOrDefault(value => value.Name == name)
        ?? throw new ArgumentException($"縮める変換の値のプールに {name} が無い", nameof(name));
}
