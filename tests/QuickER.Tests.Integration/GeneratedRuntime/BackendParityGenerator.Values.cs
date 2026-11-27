using System;
using System.Linq;
using QuickER.Tests.TestSupport.ValuePools;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// 一致テストの乱数の操作の列の値の幅：本流の乱数の列（<c>_random</c>）が引いた値を、別の乱数の列（<c>_values</c>）で低い確率で
/// 端の値に置き換える。本流の列の引き方を変えないので、操作の列（手の種類・対象の行・順）は値の幅が無いときと同じ。
/// </summary>
/// <remarks>
/// <para>
/// 置き換える値：日時の Kind（Unspecified・Utc・Local）と端数（整数秒・7 桁の tick）と形の端（0 時・翌日の 1 tick 前・暦の最初と最後）、decimal の精度・スケールの端と 0、blob の空と
/// 長いもの、文字列の非 ASCII（BMP の文字）とサロゲートペアと空文字列。数えるのは <see cref="BackendParityValueDimensions"/>。
/// </para>
/// <para>
/// 一意な列（顧客の名前・code）の値は置き換えない。大文字小文字だけ違う非 ASCII の一意な値は、照合順序の差として同期の
/// 一意な値の別表記と一緒に扱う。
/// </para>
/// </remarks>
public sealed partial class BackendParityGenerator
{
    /// <summary>端数の tick（datetime2(7) と SQLite の TEXT が保つ 7 桁）</summary>
    public const long FractionTicks = 1234567;

    /// <summary>長い blob の長さ</summary>
    public const int LongBlobLength = 4096;

    /// <summary>注文の金額（decimal(10,2)）の端：精度の上下限・最小の正の値・0</summary>
    public static readonly decimal[] AmountEdges = [99999999.99m, -99999999.99m, 0.01m, 0.00m];

    /// <summary>明細の割引（decimal(5,2)）の端：精度の上下限・最小の正の値・0</summary>
    public static readonly decimal[] DiscountEdges = [999.99m, -999.99m, 0.01m, 0.00m];

    /// <summary>非 ASCII（BMP の文字）の前置き</summary>
    public const string NonAsciiPrefix =
        TextEdges.KanjiNihon + TextEdges.KanjiGo + TextEdges.LatinSmallEAcute;

    /// <summary>サロゲートペアの文字</summary>
    public const string SurrogatePair = TextEdges.GrinningFace;

    /// <summary>値の幅を引く乱数の列の種（本流の列と別にする）</summary>
    public static int ValueSeed(int seed) => unchecked(seed * 6007 + 7919);

    /// <summary>
    /// 日時の形（0 時・翌日の 1 tick 前・暦の端）と空文字列を足す乱数の列の種（本流の列・値の幅の列と別にする）。
    /// 値の幅の列（<c>_values</c>）の引く回数を変えないので、その列が決める手（NOT NULL への null の手）は変わらない
    /// </summary>
    public static int EdgeSeed(int seed) => unchecked(seed * 6029 + 8191);

    /// <summary>日時：Kind を 3 つから、端数を整数秒か 7 桁の tick から選ぶ（null はそのまま）</summary>
    private DateTime? WidenDate(DateTime? value)
    {
        if (value is null)
        {
            return null;
        }

        var kind = (DateTimeKind)_values.Next(3);
        var ticks = _values.Next(2) == 0 ? 0 : FractionTicks;
        var widened = new DateTime(value.Value.Ticks + ticks, kind);

        // 10 回に 4 回、日時の形の端に置き換える（0 時・翌日の 1 tick 前・暦の最初と最後。Kind は上で引いたまま）
        return _edges.Next(10) switch
        {
            0 => new DateTime(widened.Date.Ticks, kind),
            1 => new DateTime(DateTimeEdges.OneTickBefore(widened.Date.AddDays(1)).Ticks, kind),
            2 => new DateTime(DateTime.MinValue.Ticks, kind),
            3 => new DateTime(DateTime.MaxValue.Ticks, kind),
            _ => widened,
        };
    }

    /// <summary>decimal：3 回に 1 回、端の値に置き換える（null はそのまま）</summary>
    private decimal? WidenDecimal(decimal? value, decimal[] edges) =>
        value is not null && _values.Next(3) == 0 ? edges[_values.Next(edges.Length)] : value;

    /// <summary>blob：4 回に 1 回ずつ空・長いものに置き換える（null はそのまま）</summary>
    private byte[]? WidenBlob(byte[]? value) =>
        value is null
            ? null
            : _values.Next(4) switch
            {
                0 => [],
                1 =>
                [
                    .. Enumerable
                        .Range(0, LongBlobLength)
                        .Select(index => (byte)(value[0] + index)),
                ],
                _ => value,
            };

    /// <summary>文字列：3 回に 1 回ずつ非 ASCII・サロゲートペアを含む値に置き換える（null はそのまま）</summary>
    private string? WidenText(string? value)
    {
        if (value is null)
        {
            return null;
        }

        var widened = _values.Next(3) switch
        {
            0 => NonAsciiPrefix + value,
            1 => SurrogatePair + value + SurrogatePair,
            _ => value,
        };

        // 6 回に 1 回、空文字列に置き換える（NULL との区別を JSON・EF Core の往復で確かめる）
        return _edges.Next(6) == 0 ? "" : widened;
    }
}
