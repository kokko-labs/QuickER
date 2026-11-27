using System;
using System.Numerics;

namespace QuickER.Tests.TestSupport.ValuePools;

/// <summary>decimal・整数・浮動小数点・値を変える明示の変換の水準</summary>
public static partial class ValueEdges
{
    private static EdgeRule[] DecimalRules =>
        [
            Edge<decimal>(
                "dec-zero",
                ValueFamily.Decimal,
                "0（符号とスケールの扱いが分岐しうる）",
                v => v == 0m
            ),
            Edge<decimal>(
                "dec-negative",
                ValueFamily.Decimal,
                "負の値（範囲の端・丸めの向き・負号の書式が分岐する）",
                v => v < 0m
            ),
            Edge<decimal>(
                "dec-fraction",
                ValueFamily.Decimal,
                "小数部を持つ値（整数の列と比べると、整数へ切るか数として比べるかで答えが割れる）",
                v => v != decimal.Truncate(v)
            ),
            Edge<decimal>(
                "dec-beyond-long",
                ValueFamily.Decimal,
                "long の範囲の外の値（bigint へ落とすと桁あふれになり、decimal(38,19) の整数部にも入らない）",
                v => v > long.MaxValue || v < long.MinValue
            ),
            Edge<decimal>(
                "dec-type-end",
                ValueFamily.Decimal,
                "decimal の型の端（decimal.MaxValue・MinValue。どの列の精度にも入らず、列の型で束縛すると桁あふれになる）",
                v => v == decimal.MaxValue || v == decimal.MinValue
            ),
            ColumnEdge<decimal>(
                "dec-beyond-column-scale",
                ValueFamily.Decimal,
                "列のスケールより細かい端数を持つ値（列の型で束縛すると丸められ、値の型で束縛し直す）",
                column => column.Scale is >= 0 and <= 28,
                (v, column) => decimal.Round(v, column.Scale!.Value) != v
            ),
            ColumnEdge<decimal>(
                "dec-beyond-column-range",
                ValueFamily.Decimal,
                "列の型の範囲の外の値（精度から決まる整数部の外か money・整数の列の範囲の外。列の型で束縛すると桁あふれになる）",
                HasDecimalRange,
                (v, column) => BeyondDecimalRange(v, column)
            ),
            ColumnEdge<decimal>(
                "dec-column-range-end",
                ValueFamily.Decimal,
                "列の型の範囲の端にちょうど等しい値（範囲の中か外かの判定の境目）",
                HasDecimalRange,
                (v, column) =>
                    DecimalRangeEnds(column) is { } ends && (v == ends.Min || v == ends.Max)
            ),
            ColumnEdge<decimal>(
                "dec-smallest-unit",
                ValueFamily.Decimal,
                "列のスケールの最小の単位にちょうど等しい値（0.01 など。丸めで 0 と区別される境目）",
                column => column.Scale is > 0 and <= 28,
                (v, column) => Math.Abs(v) == Unit(column.Scale!.Value)
            ),
        ];

    private static EdgeRule[] IntegerRules =>
        [
            Edge<object>("int-zero", ValueFamily.Integer, "0", v => ToBig(v).IsZero),
            Edge<object>(
                "int-negative",
                ValueFamily.Integer,
                "負の値（符号だけの変換・符号なしの列・範囲の端が分岐する）",
                v => ToBig(v).Sign < 0
            ),
            Edge<object>(
                "int-type-min",
                ValueFamily.Integer,
                "値の CLR の型の最小（変換・否定・絶対値が桁あふれしうる）",
                v => ToBig(v) == IntegerRange(v.GetType()).Min
            ),
            Edge<object>(
                "int-type-max",
                ValueFamily.Integer,
                "値の CLR の型の最大（変換・加算が桁あふれしうる）",
                v => ToBig(v) == IntegerRange(v.GetType()).Max
            ),
            Edge<object>(
                "int-sign-boundary",
                ValueFamily.Integer,
                "符号なしの値で最上位ビットが立つ値（同じ幅の符号つきの型へ変換すると負になる）",
                IsSignBoundary
            ),
            ColumnEdge<object>(
                "int-beyond-column-range",
                ValueFamily.Integer,
                "列の型の範囲の外の整数（列の型で束縛すると桁あふれか別の値になる）",
                column => column.Min is not null && column.Max is not null,
                (v, column) =>
                    ToBig(v) < new BigInteger(column.Min!.Value)
                    || ToBig(v) > new BigInteger(column.Max!.Value)
            ),
            Edge<object>(
                "int-beyond-float-precision",
                ValueFamily.Integer,
                "float へ変換して戻すと値が変わる整数（2^24 を超える。C# は浮動小数点へ丸めて比べ、DB は丸めずに比べうる）",
                v => new BigInteger((double)ToFloat(v)) != ToBig(v)
            ),
            Edge<object>(
                "int-beyond-double-precision",
                ValueFamily.Integer,
                "double へ変換して戻すと値が変わる整数（2^53 を超える。SQLite は INTEGER と REAL を丸めずに比べる）",
                v => new BigInteger(ToDouble(v)) != ToBig(v)
            ),
        ];

    private static EdgeRule[] FloatingRules =>
        [
            Edge<object>(
                "fp-negative-zero",
                ValueFamily.Floating,
                "-0.0（0.0 と等しいが符号を持つ。書式と符号の判定が割れうる）",
                v => AsDouble(v) == 0 && double.IsNegative(AsDouble(v))
            ),
            Edge<object>(
                "fp-subnormal",
                ValueFamily.Floating,
                "非正規化数（DB の型が持てない・0 へ丸められうる）",
                v => v is float f ? float.IsSubnormal(f) : double.IsSubnormal((double)v)
            ),
            Edge<object>(
                "fp-type-end",
                ValueFamily.Floating,
                "型の最大・最小（±MaxValue。書式の往復と DB の範囲が分岐しうる）",
                v =>
                    v is float f
                        ? f is float.MaxValue or float.MinValue
                        : (double)v is double.MaxValue or double.MinValue
            ),
            Edge<object>(
                "fp-non-dyadic",
                ValueFamily.Floating,
                "2 進で正確に表せない小数（0.1 など。decimal・リテラルの綴りとの比較が丸めで割れる）",
                v =>
                    double.IsFinite(AsDouble(v))
                    && Math.Abs(AsDouble(v)) < 1e300
                    && Math.Floor(AsDouble(v) * 1024) != AsDouble(v) * 1024
            ),
            Edge<object>(
                "fp-integer-beyond-2pow24",
                ValueFamily.Floating,
                "2^24 以上の整数の値の float（隣の整数を表せない。整数の列との比較が丸めで割れる）",
                v => v is float f && float.IsInteger(f) && Math.Abs(f) >= 16_777_216f
            ),
            Edge<object>(
                "fp-integer-beyond-2pow53",
                ValueFamily.Floating,
                "2^53 以上の整数の値の double（隣の整数を表せない。bigint の列との比較が丸めで割れる）",
                v => v is double d && double.IsInteger(d) && Math.Abs(d) >= 9_007_199_254_740_992d
            ),
            Edge<object>(
                "fp-beyond-float",
                ValueFamily.Floating,
                "float へ変換すると値が変わる double（real・float の列と比べると、単精度へ丸めて別の行に当たる）",
                v => v is double d && !double.IsNaN(d) && (double)(float)d != d
            ),
        ];

    private static EdgeRule[] GuidRules =>
        [
            Edge<Guid>(
                "guid-empty",
                ValueFamily.Guid,
                "全部 0 の Guid（未設定の値と区別されない・並びの端）",
                v => v == Guid.Empty
            ),
            Edge<Guid>(
                "guid-max",
                ValueFamily.Guid,
                "全部 0xFF の Guid（SQL Server の uniqueidentifier の並びの端）",
                v => v == Guid.AllBitsSet
            ),
        ];

    private static EdgeRule[] BooleanRules =>
        [
            Edge<bool>("bool-true", ValueFamily.Boolean, "真（SQL では 1）", v => v),
            Edge<bool>(
                "bool-false",
                ValueFamily.Boolean,
                "偽（SQL では 0。NULL の行はどちらにも当たらない）",
                v => !v
            ),
        ];

    private static EdgeRule[] NarrowingRules =>
        [
            ColumnEdge<object>(
                "cast-keeps",
                ValueFamily.Narrowing,
                "変換で値が変わらない元の値（対照。変換を外しても答えは同じ）",
                column => column.CastTarget is not null,
                (v, column) => Narrowing(v, column.CastTarget!) == NarrowingResult.Keeps
            ),
            ColumnEdge<object>(
                "cast-cuts-fraction",
                ValueFamily.Narrowing,
                "整数への変換で小数部が落ちる元の値（変換を外すとどの整数とも等しくない）",
                column => column.CastTarget is not null,
                (v, column) => HasFraction(v) && IsIntegerType(column.CastTarget!)
            ),
            ColumnEdge<object>(
                "cast-beyond-range",
                ValueFamily.Narrowing,
                "変換先の型の範囲の外の元の値（整数への変換で折り返すか範囲の端に止まる）",
                column => column.CastTarget is not null,
                (v, column) => Narrowing(v, column.CastTarget!) == NarrowingResult.BeyondRange
            ),
            ColumnEdge<object>(
                "cast-sign-only",
                ValueFamily.Narrowing,
                "負の値を同じ幅の符号なしの型へ変換する元の値（符号だけの変換＝C# は折り返す。CASE で書き直す腕）",
                column => column.CastTarget is not null,
                (v, column) => Narrowing(v, column.CastTarget!) == NarrowingResult.SignOnly
            ),
        ];

    private static bool HasDecimalRange(ValueColumn column) =>
        (column.Min is not null && column.Max is not null)
        || (column.Precision is > 0 and <= 38 && column.Scale is >= 0);

    /// <summary>列の型の範囲の端（Min・Max があればそれ、無ければ精度とスケールから。整数部が 29 桁以上なら null）</summary>
    private static (decimal Min, decimal Max)? DecimalRangeEnds(ValueColumn column)
    {
        if (column.Min is { } min && column.Max is { } max)
        {
            return (min, max);
        }

        var digits = column.Precision!.Value - column.Scale!.Value;

        if (digits >= 29 || column.Scale.Value > 28)
        {
            return null;
        }

        var end = Pow10Decimal(digits) - Unit(column.Scale.Value);

        return (-end, end);
    }

    private static bool BeyondDecimalRange(decimal v, ValueColumn column)
    {
        if (column.Min is { } min && column.Max is { } max)
        {
            return v < min || v > max;
        }

        var digits = column.Precision!.Value - column.Scale!.Value;

        return digits < 29 && Math.Abs(decimal.Truncate(v)) >= Pow10Decimal(digits);
    }

    private static decimal Pow10Decimal(int n)
    {
        var value = 1m;

        for (var i = 0; i < n; i++)
        {
            value *= 10m;
        }

        return value;
    }

    /// <summary>10 の -scale 乗</summary>
    private static decimal Unit(int scale)
    {
        var value = 1m;

        for (var i = 0; i < scale; i++)
        {
            value /= 10m;
        }

        return value;
    }

    private static BigInteger ToBig(object v) =>
        v switch
        {
            sbyte x => x,
            byte x => x,
            short x => x,
            ushort x => x,
            int x => x,
            uint x => x,
            long x => x,
            ulong x => x,
            _ => throw new ArgumentException($"整数でない値 {v.GetType().Name}", nameof(v)),
        };

    /// <summary>整数を C# の変換と同じに double へ（型ごとに直に変換する）</summary>
    private static double ToDouble(object v) =>
        v switch
        {
            sbyte x => x,
            byte x => x,
            short x => x,
            ushort x => x,
            int x => x,
            uint x => x,
            long x => x,
            ulong x => x,
            _ => throw new ArgumentException($"整数でない値 {v.GetType().Name}", nameof(v)),
        };

    /// <summary>整数を C# の変換と同じに float へ（型ごとに直に変換する。double を経ると 2 回丸める）</summary>
    private static float ToFloat(object v) =>
        v switch
        {
            sbyte x => x,
            byte x => x,
            short x => x,
            ushort x => x,
            int x => x,
            uint x => x,
            long x => x,
            ulong x => x,
            _ => throw new ArgumentException($"整数でない値 {v.GetType().Name}", nameof(v)),
        };

    private static double AsDouble(object v) => v is float f ? f : (double)v;

    private static (BigInteger Min, BigInteger Max) IntegerRange(Type type) =>
        type == typeof(sbyte) ? (sbyte.MinValue, sbyte.MaxValue)
        : type == typeof(byte) ? (byte.MinValue, byte.MaxValue)
        : type == typeof(short) ? (short.MinValue, short.MaxValue)
        : type == typeof(ushort) ? (ushort.MinValue, ushort.MaxValue)
        : type == typeof(int) ? (int.MinValue, int.MaxValue)
        : type == typeof(uint) ? (uint.MinValue, uint.MaxValue)
        : type == typeof(long) ? (long.MinValue, long.MaxValue)
        : type == typeof(ulong) ? (ulong.MinValue, ulong.MaxValue)
        : throw new ArgumentException($"整数の型でない {type.Name}", nameof(type));

    private static bool IsIntegerType(Type type) =>
        type == typeof(sbyte)
        || type == typeof(byte)
        || type == typeof(short)
        || type == typeof(ushort)
        || type == typeof(int)
        || type == typeof(uint)
        || type == typeof(long)
        || type == typeof(ulong);

    private static bool IsUnsigned(Type type) =>
        type == typeof(byte)
        || type == typeof(ushort)
        || type == typeof(uint)
        || type == typeof(ulong);

    private static int WidthOf(Type type) =>
        type == typeof(sbyte) || type == typeof(byte) ? 1
        : type == typeof(short) || type == typeof(ushort) ? 2
        : type == typeof(int) || type == typeof(uint) ? 4
        : type == typeof(long) || type == typeof(ulong) ? 8
        : 0;

    private static bool IsSignBoundary(object v) =>
        v switch
        {
            byte x => x >= 0x80,
            ushort x => x >= 0x8000,
            uint x => x >= 0x8000_0000u,
            ulong x => x >= 0x8000_0000_0000_0000ul,
            _ => false,
        };

    private static bool HasFraction(object v) =>
        v switch
        {
            decimal d => d != decimal.Truncate(d),
            float f => float.IsFinite(f) && !float.IsInteger(f),
            double d => double.IsFinite(d) && !double.IsInteger(d),
            _ => false,
        };

    /// <summary>値を変える明示の変換の結果の分類</summary>
    private enum NarrowingResult
    {
        /// <summary>値が変わらない</summary>
        Keeps,

        /// <summary>小数部だけが落ちる（範囲の中）</summary>
        CutsFraction,

        /// <summary>範囲の外</summary>
        BeyondRange,

        /// <summary>負の値を同じ幅の符号なしの型へ（符号だけの変換）</summary>
        SignOnly,

        /// <summary>判定しない（整数でない変換先で値が変わる）</summary>
        Other,
    }

    /// <summary>元の値 <paramref name="v"/> を <paramref name="target"/> へ明示に変換したときの分類</summary>
    private static NarrowingResult Narrowing(object v, Type target)
    {
        target = Nullable.GetUnderlyingType(target) ?? target;

        if (!IsIntegerType(target))
        {
            // 整数でない変換先（double → float・小数どうし）：値が変わらなければ対照
            return target == typeof(float) && v is double d && (double)(float)d != d
                ? NarrowingResult.Other
                : NarrowingResult.Keeps;
        }

        BigInteger truncated;

        switch (v)
        {
            case decimal dec:
                truncated = new BigInteger(decimal.Truncate(dec));
                break;

            case float
            or double:
                var value = AsDouble(v);

                if (!double.IsFinite(value))
                {
                    return NarrowingResult.BeyondRange;
                }

                truncated = new BigInteger(Math.Truncate(value));
                break;

            default:
                truncated = ToBig(v);
                break;
        }

        var (min, max) = IntegerRange(target);

        if (truncated >= min && truncated <= max)
        {
            return HasFraction(v) ? NarrowingResult.CutsFraction : NarrowingResult.Keeps;
        }

        // 同じ幅の符号つきの値の負の値を符号なしへ（C# は折り返す）
        if (
            truncated.Sign < 0
            && IsUnsigned(target)
            && FamilyOf(v) == ValueFamily.Integer
            && !IsUnsigned(v.GetType())
            && WidthOf(v.GetType()) == WidthOf(target)
        )
        {
            return NarrowingResult.SignOnly;
        }

        return NarrowingResult.BeyondRange;
    }
}
