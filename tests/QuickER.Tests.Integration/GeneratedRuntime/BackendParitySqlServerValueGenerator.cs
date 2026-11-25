using System;
using System.Collections.Generic;
using System.Linq;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>値の往復で書く列（<c>BackendParitySqlServerFixture</c> の <c>value_probes</c> の列）</summary>
public enum SqlServerValueColumn
{
    /// <summary><c>at2 datetime2(2)</c></summary>
    At2,

    /// <summary><c>at datetime2</c>（7 桁）</summary>
    At,

    /// <summary><c>clock time(2)</c></summary>
    Clock,

    /// <summary><c>at_offset datetimeoffset</c>（7 桁）</summary>
    AtOffset,

    /// <summary><c>at_offset2 datetimeoffset(2)</c></summary>
    AtOffset2,

    /// <summary><c>day date</c></summary>
    Day,

    /// <summary><c>amount decimal(10,2)</c></summary>
    Amount,

    /// <summary><c>ansi varchar(20)</c></summary>
    Ansi,

    /// <summary><c>code nchar(10)</c></summary>
    Code,

    /// <summary><c>label nvarchar(20)</c></summary>
    Label,

    /// <summary><c>tag varbinary(16)</c></summary>
    Tag,

    /// <summary><c>payload varbinary(max)</c>（除外する無制限バイナリ列）</summary>
    Payload,

    /// <summary><c>legacy_at datetime</c>（1/300 秒）</summary>
    LegacyAt,

    /// <summary><c>small_at smalldatetime</c>（分）</summary>
    SmallAt,

    /// <summary><c>price money</c></summary>
    Price,

    /// <summary><c>fee smallmoney</c></summary>
    Fee,
}

/// <summary>値の往復の書く経路</summary>
public enum SqlServerValuePath
{
    /// <summary>InsertAsync</summary>
    Insert,

    /// <summary>読んでから値を変える UpdateAsync</summary>
    Update,

    /// <summary>Added の行の SaveAsync</summary>
    SaveInsert,

    /// <summary>読んでから値を変えて MarkUpdated した行の SaveAsync</summary>
    SaveUpdate,

    /// <summary>BulkInsertAsync（複数行を 1 回で）</summary>
    Bulk,
}

/// <summary>1 つの列へ書く値（値の形の名前つき）</summary>
/// <param name="Column">列</param>
/// <param name="Shape">値の形（<see cref="SqlServerValueShapes"/> の名前）</param>
/// <param name="Value">書く値</param>
public sealed record SqlServerValueCell(SqlServerValueColumn Column, string Shape, object Value);

/// <summary><c>value_probes</c> の 1 行（書く列だけを持つ。ほかの列は NULL）</summary>
/// <param name="Id">主キー</param>
/// <param name="Cells">書く値</param>
public sealed record SqlServerValueRow(int Id, IReadOnlyList<SqlServerValueCell> Cells);

/// <summary>1 回の書き込み（BulkInsert 以外は 1 行）</summary>
/// <param name="Path">書く経路</param>
/// <param name="Rows">行</param>
public sealed record SqlServerValueWrite(
    SqlServerValuePath Path,
    IReadOnlyList<SqlServerValueRow> Rows
);

/// <summary>値の往復のシナリオ（書き込みの並び）</summary>
/// <param name="Name">名前（決まった形の網羅は <c>fixed</c>、乱数の種は <c>seed-{種}</c>）</param>
/// <param name="Writes">書き込み</param>
public sealed record SqlServerValueScenario(string Name, IReadOnlyList<SqlServerValueWrite> Writes);

/// <summary>
/// 値の往復の値の形：列ごとに、どの形を作れるかと、形から書く値を作る規則
/// </summary>
/// <remarks>
/// 形は列の型の端（小数秒の切り上げ・切り下げ・ちょうど半分・日付の繰り上がり・範囲の端・スケールの外・桁あふれ・長さの超過・
/// 文字の種類）を表す。期待は形でなく値から <see cref="BackendParitySqlServerValueExpectations"/> が作る（形は数えるためだけに使う）。
/// 乱数を渡すと同じ形の中で値を散らす（null なら決まった代表の値）。
/// </remarks>
public static class SqlServerValueShapes
{
    /// <summary>列の型に収まる値</summary>
    public const string Exact = "Exact";

    /// <summary>小数を列の桁へ丸めると下がる値</summary>
    public const string RoundDown = "RoundDown";

    /// <summary>小数を列の桁へ丸めると上がる値（ちょうど半分を除く）</summary>
    public const string RoundUp = "RoundUp";

    /// <summary>小数が列の桁のちょうど半分</summary>
    public const string Half = "Half";

    /// <summary>小数秒を丸めると日付が繰り上がる値</summary>
    public const string CarryDay = "CarryDay";

    /// <summary>型の範囲の最後の値の付近（丸めても範囲を出ない）</summary>
    public const string RangeEnd = "RangeEnd";

    /// <summary>型の範囲の外（断られる）</summary>
    public const string OutOfRange = "OutOfRange";

    /// <summary>Kind が Utc の DateTime</summary>
    public const string KindUtc = "KindUtc";

    /// <summary>Kind が Local の DateTime</summary>
    public const string KindLocal = "KindLocal";

    /// <summary>UTC から離れたオフセット</summary>
    public const string Offset = "Offset";

    /// <summary>時刻を持つ DateTime（date の列）</summary>
    public const string TimeOfDay = "TimeOfDay";

    /// <summary>decimal の精度の上限</summary>
    public const string Max = "Max";

    /// <summary>decimal の精度の下限</summary>
    public const string Min = "Min";

    /// <summary>decimal の最小の正の値</summary>
    public const string Smallest = "Smallest";

    /// <summary>decimal の 0</summary>
    public const string Zero = "Zero";

    /// <summary>丸めると整数部の桁があふれる decimal</summary>
    public const string RoundsToOverflow = "RoundsToOverflow";

    /// <summary>整数部の桁があふれる decimal</summary>
    public const string Overflow = "Overflow";

    /// <summary>ASCII だけの文字列（固定長の列では短い値）</summary>
    public const string Ascii = "Ascii";

    /// <summary>BMP の非 ASCII を含む文字列</summary>
    public const string NonAscii = "NonAscii";

    /// <summary>サロゲートペアを含む文字列</summary>
    public const string Surrogate = "Surrogate";

    /// <summary>空の文字列・空のバイト配列</summary>
    public const string Empty = "Empty";

    /// <summary>列の長さちょうど</summary>
    public const string ExactLength = "ExactLength";

    /// <summary>列の長さを超え、超えた分が半角の空白（バイナリは 0x00）だけ</summary>
    public const string OverPadding = "OverPadding";

    /// <summary>列の長さを超え、超えた分に空白（0x00）以外を含む</summary>
    public const string OverContent = "OverContent";

    /// <summary>列の長さを超え、超えた分が半角の空白でない空白類（タブ・U+00A0）</summary>
    public const string OverOtherSpace = "OverOtherSpace";

    /// <summary>Unicode でない列のコードページが表せる非 ASCII</summary>
    public const string CodePageHeld = "CodePageHeld";

    /// <summary>Unicode でない列のコードページが表せない文字</summary>
    public const string CodePageMissing = "CodePageMissing";

    /// <summary>Unicode でない列のコードページで近い文字へ置き換わる文字</summary>
    public const string BestFit = "BestFit";

    /// <summary>短いバイト列</summary>
    public const string Short = "Short";

    /// <summary>長いバイト列（4 KB）</summary>
    public const string Long = "Long";

    /// <summary>基準の日時（端数なし）</summary>
    private static readonly DateTime BaseTime = new(2026, 1, 2, 3, 4, 5);

    /// <summary>基準の日付</summary>
    private static readonly DateTime BaseDay = new(2026, 1, 2);

    /// <summary>小数秒 2 桁の 1 単位の tick</summary>
    private const long Scale2Unit = 100_000;

    /// <summary>列が作れる形（宣言の順）</summary>
    public static IReadOnlyList<string> For(SqlServerValueColumn column) =>
        column switch
        {
            SqlServerValueColumn.At2 =>
            [
                Exact,
                RoundDown,
                RoundUp,
                Half,
                CarryDay,
                RangeEnd,
                KindUtc,
                KindLocal,
            ],
            SqlServerValueColumn.At => [Exact, RangeEnd, KindUtc, KindLocal],
            SqlServerValueColumn.Clock => [Exact, RoundDown, RoundUp, Half, RangeEnd, OutOfRange],
            SqlServerValueColumn.AtOffset => [Exact, RangeEnd, Offset],
            SqlServerValueColumn.AtOffset2 =>
            [
                Exact,
                RoundDown,
                RoundUp,
                Half,
                CarryDay,
                RangeEnd,
                Offset,
            ],
            SqlServerValueColumn.Day => [Exact, TimeOfDay, KindUtc, KindLocal],
            SqlServerValueColumn.Amount =>
            [
                Exact,
                Max,
                Min,
                Smallest,
                Zero,
                RoundDown,
                RoundUp,
                Half,
                RoundsToOverflow,
                Overflow,
            ],
            SqlServerValueColumn.Ansi =>
            [
                Ascii,
                Empty,
                ExactLength,
                OverPadding,
                OverContent,
                OverOtherSpace,
                CodePageHeld,
                CodePageMissing,
                BestFit,
                Surrogate,
            ],
            SqlServerValueColumn.Code or SqlServerValueColumn.Label =>
            [
                Ascii,
                NonAscii,
                Surrogate,
                Empty,
                ExactLength,
                OverPadding,
                OverContent,
                OverOtherSpace,
            ],
            SqlServerValueColumn.Tag => [Short, Empty, ExactLength, OverPadding, OverContent],
            SqlServerValueColumn.Payload => [Empty, Short, Long],
            SqlServerValueColumn.LegacyAt =>
            [
                Exact,
                RoundDown,
                RoundUp,
                RangeEnd,
                OutOfRange,
                KindUtc,
            ],
            SqlServerValueColumn.SmallAt =>
            [
                Exact,
                RoundDown,
                RoundUp,
                CarryDay,
                RangeEnd,
                OutOfRange,
            ],
            SqlServerValueColumn.Price or SqlServerValueColumn.Fee =>
            [
                Exact,
                Max,
                Min,
                RoundDown,
                RoundUp,
                Half,
                RoundsToOverflow,
                Overflow,
            ],
            _ => throw new ArgumentOutOfRangeException(nameof(column), column, null),
        };

    /// <summary>断られる値の形（乱数の行で混ぜる割合を抑えるために分ける）</summary>
    public static bool IsRefusing(string shape) =>
        shape is OutOfRange or RoundsToOverflow or Overflow or OverContent or OverOtherSpace;

    /// <summary>形から書く値を作る（<paramref name="random"/> が null なら代表の値）</summary>
    public static object Value(SqlServerValueColumn column, string shape, Random? random) =>
        column switch
        {
            SqlServerValueColumn.At2 => ScaledDateTime(shape, random),
            SqlServerValueColumn.At => FullDateTime(shape, random),
            SqlServerValueColumn.Clock => Clock(shape, random),
            SqlServerValueColumn.AtOffset => FullOffset(shape, random),
            SqlServerValueColumn.AtOffset2 => ScaledOffset(shape, random),
            SqlServerValueColumn.Day => Day(shape, random),
            SqlServerValueColumn.Amount => Amount(shape, random),
            SqlServerValueColumn.Ansi => Text(shape, 20, unicode: false, random),
            SqlServerValueColumn.Code => Text(shape, 10, unicode: true, random),
            SqlServerValueColumn.Label => Text(shape, 20, unicode: true, random),
            SqlServerValueColumn.Tag => Binary(shape, 16, random),
            SqlServerValueColumn.Payload => Blob(shape, random),
            SqlServerValueColumn.LegacyAt => LegacyDateTime(shape, random),
            SqlServerValueColumn.SmallAt => SmallDateTime(shape, random),
            SqlServerValueColumn.Price => Money(shape, random, small: false),
            SqlServerValueColumn.Fee => Money(shape, random, small: true),
            _ => throw new ArgumentOutOfRangeException(nameof(column), column, null),
        };

    /// <summary>datetime の 1 単位（1/300 秒）の tick（端数を持つ）</summary>
    private const double LegacyUnitTicks = TimeSpan.TicksPerSecond / 300.0;

    /// <summary>datetime の最小の値（1753-01-01）</summary>
    public static readonly DateTime LegacyMin = new(1753, 1, 1);

    /// <summary>datetime の最後の値（9999-12-31 23:59:59.997 を DateTime で表した値）</summary>
    public static readonly DateTime LegacyMax = System.Data.SqlTypes.SqlDateTime.MaxValue.Value;

    /// <summary>
    /// datetime の値。Exact は 10 ミリ秒の倍数（1/300 秒の 3 単位＝ちょうど表せる）、RoundDown・RoundUp は 10 ミリ秒の倍数から
    /// 半単位未満・半単位を超えた位置（半単位のちょうどは tick で表せない）、RangeEnd は最後の値から半単位未満だけ前、OutOfRange は
    /// 最小の値から 1 単位を超えて前か、最後の値から半単位を超えて後ろ（丸めると範囲の外。決まった形は後ろ）
    /// </summary>
    private static DateTime LegacyDateTime(string shape, Random? random)
    {
        var half = (long)(LegacyUnitTicks / 2);

        return shape switch
        {
            Exact => BaseTime.AddTicks(100_000 * Pick(random, 0, 99, 12)),
            RoundDown => BaseTime.AddTicks(
                100_000 * Pick(random, 0, 99, 12) + Pick(random, 1, half, 12_345)
            ),
            RoundUp => BaseTime.AddTicks(
                100_000 * Pick(random, 0, 99, 12) + Pick(random, half + 1, 33_332, 23_456)
            ),
            RangeEnd => LegacyMax.AddTicks(-Pick(random, 0, half, 0)),
            OutOfRange => Pick(random, 0, 1, 1) == 0
                ? LegacyMin.AddTicks(
                    -Pick(random, 33_334, TimeSpan.TicksPerDay, TimeSpan.TicksPerDay)
                )
                : LegacyMax.AddTicks(Pick(random, half + 1, 29_999, half + 1)),
            KindUtc => DateTime.SpecifyKind(LegacyDateTime(Exact, random), DateTimeKind.Utc),
            _ => throw new ArgumentOutOfRangeException(nameof(shape), shape, null),
        };
    }

    /// <summary>smalldatetime の最小の値</summary>
    public static readonly DateTime SmallMin = new(1900, 1, 1);

    /// <summary>smalldatetime の最後の値</summary>
    public static readonly DateTime SmallMax = new(2079, 6, 6, 23, 59, 0);

    /// <summary>
    /// smalldatetime の値（秒はミリ秒の単位で作る：29.998 秒以下は分を切り下げ、29.999 秒以上は切り上げる）。
    /// CarryDay は日の最後の分の後半、RangeEnd は最後の分の前半、OutOfRange は最小の値の前の分か、最後の分の後半。
    /// 決まった形は切り上げの境目（RoundUp は 29.999 秒・CarryDay は 30.000 秒・OutOfRange は最後の分の 29.999 秒）
    /// </summary>
    private static DateTime SmallDateTime(string shape, Random? random)
    {
        var minute = new DateTime(2026, 1, 2, 3, 4, 0).AddMinutes(Pick(random, 0, 1_000, 7));

        return shape switch
        {
            Exact => minute,
            RoundDown => minute.AddMilliseconds(Pick(random, 1, 29_998, 29_998)),
            RoundUp => minute.AddMilliseconds(Pick(random, 29_999, 59_999, 29_999)),
            CarryDay => BaseDay
                .AddDays(1)
                .AddMinutes(-1)
                .AddMilliseconds(Pick(random, 29_999, 59_999, 30_000)),
            RangeEnd => SmallMax.AddMilliseconds(Pick(random, 0, 29_998, 29_998)),
            OutOfRange => Pick(random, 0, 1, 1) == 0
                ? SmallMin.AddMinutes(-1).AddMilliseconds(Pick(random, 0, 29_998, 0))
                : SmallMax.AddMilliseconds(Pick(random, 29_999, 59_999, 29_999)),
            _ => throw new ArgumentOutOfRangeException(nameof(shape), shape, null),
        };
    }

    /// <summary>money の範囲（最小・最大）</summary>
    public static (decimal Min, decimal Max) MoneyRange(bool small) =>
        small
            ? (-214_748.3648m, 214_748.3647m)
            : (-922_337_203_685_477.5808m, 922_337_203_685_477.5807m);

    /// <summary>money・smallmoney の値（小数 4 桁の外の 5 桁目で丸めの向きを作る）</summary>
    private static decimal Money(string shape, Random? random, bool small)
    {
        var (min, max) = MoneyRange(small);
        var whole = Pick(random, -99_999, 99_999, 1);
        var sign = whole < 0 ? -1m : 1m;
        var basis = whole + sign * Pick(random, 0, 9_999, 2_345) / 10_000m;
        var positive = Pick(random, 0, 1, 0) == 0;

        return shape switch
        {
            Exact => basis,
            Max => max,
            Min => min,
            RoundDown => basis + sign * Pick(random, 1, 4, 2) / 100_000m,
            RoundUp => basis + sign * Pick(random, 6, 9, 7) / 100_000m,
            Half => basis + sign * 0.00005m,
            RoundsToOverflow => positive ? max + 0.00005m : min - 0.00005m,
            Overflow => positive
                ? max + 1m + Pick(random, 0, 99, 0)
                : min - 1m - Pick(random, 0, 99, 0),
            _ => throw new ArgumentOutOfRangeException(nameof(shape), shape, null),
        };
    }

    /// <summary>小数秒 2 桁の単位の中の端数（形ごと）</summary>
    private static long Fraction(string shape, Random? random) =>
        shape switch
        {
            RoundDown => Pick(random, 1, 49_999, 12_345),
            RoundUp => Pick(random, 50_001, 99_999, 72_345),
            Half => Scale2Unit / 2,
            _ => throw new ArgumentOutOfRangeException(nameof(shape), shape, null),
        };

    private static DateTime ScaledDateTime(string shape, Random? random) =>
        shape switch
        {
            Exact => BaseTime.AddTicks(Scale2Unit * Pick(random, 0, 99, 12)),
            RoundDown or RoundUp or Half => BaseTime.AddTicks(
                Scale2Unit * Pick(random, 0, 98, 12) + Fraction(shape, random)
            ),
            CarryDay => BaseDay
                .AddDays(1)
                .AddTicks(-Pick(random, 1, Scale2Unit / 2, Scale2Unit / 2)),
            RangeEnd => DateTime.MaxValue.AddTicks(-Pick(random, 0, Scale2Unit / 2, 0)),
            KindUtc => DateTime.SpecifyKind(ScaledDateTime(RoundUp, random), DateTimeKind.Utc),
            KindLocal => DateTime.SpecifyKind(
                ScaledDateTime(RoundDown, random),
                DateTimeKind.Local
            ),
            _ => throw new ArgumentOutOfRangeException(nameof(shape), shape, null),
        };

    private static DateTime FullDateTime(string shape, Random? random) =>
        shape switch
        {
            Exact => BaseTime.AddTicks(Pick(random, 0, TimeSpan.TicksPerSecond - 1, 1_234_567)),
            RangeEnd => DateTime.MaxValue.AddTicks(-Pick(random, 0, 9, 0)),
            KindUtc => DateTime.SpecifyKind(FullDateTime(Exact, random), DateTimeKind.Utc),
            KindLocal => DateTime.SpecifyKind(FullDateTime(Exact, random), DateTimeKind.Local),
            _ => throw new ArgumentOutOfRangeException(nameof(shape), shape, null),
        };

    private static TimeSpan Clock(string shape, Random? random) =>
        shape switch
        {
            Exact or RoundDown or RoundUp or Half => TimeSpan.FromTicks(
                ScaledDateTime(shape, random).TimeOfDay.Ticks
            ),
            RangeEnd => TimeSpan.FromDays(1)
                - TimeSpan.FromTicks(Pick(random, 1, Scale2Unit / 2, Scale2Unit / 2)),
            OutOfRange => Pick(random, 0, 1, 0) == 0
                ? TimeSpan.FromDays(1)
                    + TimeSpan.FromTicks(Pick(random, 0, TimeSpan.TicksPerHour, 0))
                : TimeSpan.FromTicks(
                    -Pick(random, 1, TimeSpan.TicksPerHour, TimeSpan.TicksPerSecond)
                ),
            _ => throw new ArgumentOutOfRangeException(nameof(shape), shape, null),
        };

    private static TimeSpan OffsetOf(Random? random) =>
        TimeSpan.FromMinutes(15 * Pick(random, -14 * 4, 14 * 4, 9 * 4));

    private static DateTimeOffset FullOffset(string shape, Random? random) =>
        shape switch
        {
            Exact => new DateTimeOffset(FullDateTime(Exact, random), TimeSpan.Zero),
            RangeEnd => DateTimeOffset.MaxValue.AddTicks(-Pick(random, 0, 9, 0)),
            Offset => new DateTimeOffset(FullDateTime(Exact, random), OffsetOf(random)),
            _ => throw new ArgumentOutOfRangeException(nameof(shape), shape, null),
        };

    private static DateTimeOffset ScaledOffset(string shape, Random? random) =>
        shape switch
        {
            Exact or RoundDown or RoundUp or Half or CarryDay => new DateTimeOffset(
                ScaledDateTime(shape, random),
                TimeSpan.Zero
            ),
            RangeEnd => DateTimeOffset.MaxValue.AddTicks(-Pick(random, 0, Scale2Unit / 2, 0)),
            Offset => new DateTimeOffset(ScaledDateTime(RoundUp, random), OffsetOf(random)),
            _ => throw new ArgumentOutOfRangeException(nameof(shape), shape, null),
        };

    private static DateTime Day(string shape, Random? random) =>
        shape switch
        {
            Exact => BaseDay.AddDays(Pick(random, 0, 400, 0)),
            TimeOfDay => BaseDay.AddTicks(
                Pick(random, 1, TimeSpan.TicksPerDay - 1, TimeSpan.TicksPerDay - 1)
            ),
            KindUtc => DateTime.SpecifyKind(Day(Exact, random), DateTimeKind.Utc),
            KindLocal => DateTime.SpecifyKind(Day(Exact, random), DateTimeKind.Local),
            _ => throw new ArgumentOutOfRangeException(nameof(shape), shape, null),
        };

    private static decimal Amount(string shape, Random? random)
    {
        var whole = Pick(random, -99_999, 99_999, 1);
        var cents = Pick(random, 0, 99, 23);
        var sign = whole < 0 ? -1m : 1m;
        var basis = whole + sign * cents / 100m;

        return shape switch
        {
            Exact => basis,
            Max => 99_999_999.99m,
            Min => -99_999_999.99m,
            Smallest => 0.01m,
            Zero => 0.00m,
            RoundDown => basis + sign * Pick(random, 1, 4_999, 1_234) / 1_000_000m,
            RoundUp => basis + sign * Pick(random, 5_001, 9_999, 7_654) / 1_000_000m,
            Half => basis + sign * 0.005m,
            RoundsToOverflow => (Pick(random, 0, 1, 0) == 0 ? 1m : -1m) * 99_999_999.995m,
            Overflow => (Pick(random, 0, 1, 0) == 0 ? 1m : -1m)
                * (100_000_000m + Pick(random, 0, 99, 0)),
            _ => throw new ArgumentOutOfRangeException(nameof(shape), shape, null),
        };
    }

    /// <summary>コードページ 1252 が表せる非 ASCII</summary>
    private static readonly string[] HeldCharacters = ["é", "€", "ÿ", "ñ"];

    /// <summary>コードページ 1252 が表せない文字（近い文字も無い）</summary>
    private static readonly string[] MissingCharacters = ["日", "本", "ア"];

    /// <summary>コードページ 1252 で近い文字へ置き換わる文字</summary>
    private static readonly string[] BestFitCharacters = ["Ā", "ō", "Ł"];

    /// <summary>BMP の非 ASCII（Unicode の列）</summary>
    private static readonly string[] NonAsciiCharacters = ["日", "é", "Ā", "ア"];

    /// <summary>サロゲートペア</summary>
    private static readonly string[] SurrogateCharacters = ["😀", "𠀋"];

    /// <summary>半角の空白でない空白類</summary>
    private static readonly string[] OtherSpaces = ["\u0009", " "];

    private static string Text(string shape, int length, bool unicode, Random? random)
    {
        var letters = new string((char)('a' + Pick(random, 0, 25, 1)), length);

        return shape switch
        {
            Ascii => letters[..Pick(random, 1, length - 1, 3)],
            Empty => "",
            ExactLength => letters,
            OverPadding => PaddedBeyond(
                letters,
                Pick(random, 0, length, length),
                Pick(random, 1, 5, 2)
            ),
            OverContent => letters + new string(' ', Pick(random, 0, 3, 1)) + "x",
            OverOtherSpace => letters + Choose(random, OtherSpaces),
            CodePageHeld => "ab" + Choose(random, HeldCharacters) + "c",
            CodePageMissing => "ab" + Choose(random, MissingCharacters) + "c",
            BestFit => "ab" + Choose(random, BestFitCharacters) + "c",
            NonAscii => "a" + Choose(random, NonAsciiCharacters),
            Surrogate => unicode && Pick(random, 0, 1, 0) == 1
                ? string.Concat(Enumerable.Repeat(Choose(random, SurrogateCharacters), length / 2))
                : "a" + Choose(random, SurrogateCharacters) + "b",
            _ => throw new ArgumentOutOfRangeException(nameof(shape), shape, null),
        };
    }

    /// <summary>先頭の <paramref name="kept"/> 文字の後ろを空白で埋め、列の長さを <paramref name="beyond"/> 文字だけ超えさせる</summary>
    private static string PaddedBeyond(string letters, int kept, int beyond) =>
        letters[..kept] + new string(' ', letters.Length - kept + beyond);

    private static byte[] Binary(string shape, int length, Random? random)
    {
        var start = (byte)Pick(random, 1, 200, 1);
        byte[] Filled(int count) => [.. Enumerable.Range(0, count).Select(i => (byte)(start + i))];

        return shape switch
        {
            Short => Filled(Pick(random, 1, length - 1, 4)),
            Empty => [],
            ExactLength => Filled(length),
            OverPadding => [.. Filled(length), .. new byte[Pick(random, 1, 3, 1)]],
            OverContent => [.. Filled(length), .. new byte[Pick(random, 0, 2, 0)], 0x20],
            _ => throw new ArgumentOutOfRangeException(nameof(shape), shape, null),
        };
    }

    private static byte[] Blob(string shape, Random? random)
    {
        var start = (byte)Pick(random, 0, 255, 7);

        return shape switch
        {
            Empty => [],
            Short =>
            [
                .. Enumerable.Range(0, Pick(random, 1, 64, 3)).Select(i => (byte)(start + i)),
            ],
            Long =>
            [
                .. Enumerable
                    .Range(0, BackendParityGenerator.LongBlobLength)
                    .Select(i => (byte)(start + i)),
            ],
            _ => throw new ArgumentOutOfRangeException(nameof(shape), shape, null),
        };
    }

    /// <summary>乱数があれば [min, max] から、無ければ代表の値</summary>
    private static int Pick(Random? random, int min, int max, int representative) =>
        random is null ? representative : random.Next(min, max + 1);

    /// <summary>乱数があれば [min, max] から、無ければ代表の値</summary>
    private static long Pick(Random? random, long min, long max, long representative) =>
        random is null ? representative : random.NextInt64(min, max + 1);

    /// <summary>乱数があれば候補から、無ければ先頭</summary>
    private static string Choose(Random? random, string[] candidates) =>
        random is null ? candidates[0] : candidates[random.Next(candidates.Length)];
}

/// <summary>
/// 値の往復の生成器：決まった形の網羅（列 × 値の形 × 書く経路を 1 回ずつ）と、乱数の種ごとの行（1 行に複数の列の端の値を混ぜ、
/// 複数行の BulkInsert で 1 行だけ断られる形も作る）
/// </summary>
/// <remarks>
/// 乱数は実装先どうしの一致の本流（<see cref="BackendParityGenerator"/>）と別の種の式から引く。本流の乱数の引き方と種の指紋は変えない。
/// 作らない形は <see cref="BackendParitySqlServerValueNotGenerated"/> に宣言する。
/// </remarks>
public static class BackendParitySqlServerValueGenerator
{
    /// <summary>除外する無制限バイナリ列を書かない経路（除外列は UPDATE で書かず、Stream で書く）</summary>
    public static bool Writes(SqlServerValuePath path, SqlServerValueColumn column) =>
        column != SqlServerValueColumn.Payload
        || path is not (SqlServerValuePath.Update or SqlServerValuePath.SaveUpdate);

    /// <summary>乱数の列の種（実装先どうしの一致の本流・値の幅の列と別にする）</summary>
    public static int Seed(int seed) => unchecked(seed * 7013 + 104729);

    /// <summary>決まった形の網羅：列 × 値の形 × 書く経路の成り立つ組を、代表の値で 1 行ずつ</summary>
    public static SqlServerValueScenario Fixed()
    {
        var writes = new List<SqlServerValueWrite>();
        var id = 0;

        foreach (var column in Enum.GetValues<SqlServerValueColumn>())
        {
            foreach (var shape in SqlServerValueShapes.For(column))
            {
                foreach (var path in Enum.GetValues<SqlServerValuePath>())
                {
                    if (!Writes(path, column))
                    {
                        continue;
                    }

                    var cell = new SqlServerValueCell(
                        column,
                        shape,
                        SqlServerValueShapes.Value(column, shape, null)
                    );
                    writes.Add(
                        new SqlServerValueWrite(path, [new SqlServerValueRow(++id, [cell])])
                    );
                }
            }
        }

        return new SqlServerValueScenario("fixed", writes);
    }

    /// <summary>乱数の種の行：BulkInsert 以外の経路に 1 行ずつ、BulkInsert に 2〜5 行</summary>
    public static SqlServerValueScenario Create(int seed)
    {
        var random = new Random(Seed(seed));
        var id = seed * 100;
        var writes = new List<SqlServerValueWrite>();

        foreach (var path in Enum.GetValues<SqlServerValuePath>())
        {
            var rowCount = path == SqlServerValuePath.Bulk ? random.Next(2, 6) : 1;
            var rows = new List<SqlServerValueRow>();

            for (var i = 0; i < rowCount; i++)
            {
                rows.Add(new SqlServerValueRow(++id, Cells(random, path)));
            }

            // 2 回に 1 回、1 つの値を断られる形へ置き換える（BulkInsert なら行の 1 つだけ）
            if (random.Next(2) == 0)
            {
                var target = random.Next(rows.Count);
                rows[target] = WithRefusingCell(random, path, rows[target]);
            }

            writes.Add(new SqlServerValueWrite(path, rows));
        }

        return new SqlServerValueScenario($"seed-{seed}", writes);
    }

    /// <summary>断られない形の値を 2〜4 列に書く行</summary>
    private static IReadOnlyList<SqlServerValueCell> Cells(Random random, SqlServerValuePath path)
    {
        var columns = Enum.GetValues<SqlServerValueColumn>()
            .Where(column => Writes(path, column))
            .OrderBy(_ => random.Next())
            .Take(random.Next(2, 5))
            .OrderBy(column => column)
            .ToList();

        return
        [
            .. columns.Select(column =>
            {
                var shapes = SqlServerValueShapes
                    .For(column)
                    .Where(shape => !SqlServerValueShapes.IsRefusing(shape))
                    .ToList();
                var shape = shapes[random.Next(shapes.Count)];

                return new SqlServerValueCell(
                    column,
                    shape,
                    SqlServerValueShapes.Value(column, shape, random)
                );
            }),
        ];
    }

    /// <summary>行の 1 つの値を、その列の断られる形の値に置き換える（断られる形の無い列なら足す）</summary>
    private static SqlServerValueRow WithRefusingCell(
        Random random,
        SqlServerValuePath path,
        SqlServerValueRow row
    )
    {
        // 列と断られる形の組から一様に選ぶ（列ごとに選ぶと、断られる形の多い列の形が出にくい）
        var candidates = Enum.GetValues<SqlServerValueColumn>()
            .Where(column => Writes(path, column))
            .SelectMany(column =>
                SqlServerValueShapes
                    .For(column)
                    .Where(SqlServerValueShapes.IsRefusing)
                    .Select(shape => (Column: column, Shape: shape))
            )
            .ToList();
        var (column, shape) = candidates[random.Next(candidates.Count)];
        var cell = new SqlServerValueCell(
            column,
            shape,
            SqlServerValueShapes.Value(column, shape, random)
        );

        return row with
        {
            Cells =
            [
                .. row.Cells.Where(c => c.Column != column).Append(cell).OrderBy(c => c.Column),
            ],
        };
    }
}
