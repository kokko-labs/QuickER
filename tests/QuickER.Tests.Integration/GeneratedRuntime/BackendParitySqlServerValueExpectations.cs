using System;
using System.Globalization;
using System.Linq;
using System.Text;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// 値の往復の期待：SQL Server の列へ書いた値を読み戻すとどうなるか（docs の生成 Repository の節の約束の写し）
/// </summary>
/// <remarks>
/// <para>
/// 実装の挙動を写さない。docs が約束する規則だけを書き、QuickER 版と EF Core 版のどの書く経路（BulkInsert を含む）にも同じ期待を掛ける。
/// </para>
/// <list type="bullet">
/// <item>DateTime は Kind が Unspecified で戻る。小数秒の桁を持つ列（datetime2(n)・time(n)・datetimeoffset(n)）は値を n 桁へ
/// 四捨五入する（ちょうど半分は上げる。日付も繰り上がる）。丸めると型の範囲を出る値は、その桁の最後の値で止まる。date は日付だけを保つ。
/// datetimeoffset はローカルの時刻を丸め、オフセットを保つ。time は 0 以上 24 時間未満の外を断る。</item>
/// <item>decimal(p,s) は s 桁へ四捨五入し（ちょうど半分は 0 から遠い側）、列のスケールで戻る。丸めた値の整数部が p−s 桁に入らなければ断る。</item>
/// <item>文字列は長さ（UTF-16 のコード単位）が列の長さ以下ならそのまま。超えた分が半角の空白（U+0020）だけなら列の長さへ切って保存し、
/// それ以外は断る。nchar は読み戻すと空白で列の長さに詰まる。varchar は列のコードページ（既定の照合順序は 1252）で表せる文字を保ち、
/// 表せない文字はコード単位ごとに元と違う 1 文字（'?' か近い文字）になる。</item>
/// <item>バイナリはバイトを保つ（空は空）。varbinary(n) の超過は、超えた分が 0x00 だけなら n バイトへ切って保存し、それ以外は断る。</item>
/// <item>断られた書き込みは何も残さない（BulkInsert は 1 行でも断られれば、どの行も残らない）。断ったときの例外の型と文は経路で違い、比べない。</item>
/// </list>
/// </remarks>
public static class BackendParitySqlServerValueExpectations
{
    /// <summary>小数秒の桁（7 桁の列は 7）</summary>
    public static int FractionalScale(SqlServerValueColumn column) =>
        column switch
        {
            SqlServerValueColumn.At2
            or SqlServerValueColumn.Clock
            or SqlServerValueColumn.AtOffset2 => 2,
            _ => 7,
        };

    /// <summary>この値の書き込みを列が断るか</summary>
    public static bool IsRefused(SqlServerValueColumn column, object value) =>
        column switch
        {
            SqlServerValueColumn.Clock => value is TimeSpan time
                && (time < TimeSpan.Zero || time >= TimeSpan.FromDays(1)),
            SqlServerValueColumn.Amount => value is decimal amount
                && !FitsAfterRounding(amount, 10, 2),
            SqlServerValueColumn.Ansi => value is string text && Stored(text, 20) is null,
            SqlServerValueColumn.Code => value is string text && Stored(text, 10) is null,
            SqlServerValueColumn.Label => value is string text && Stored(text, 20) is null,
            SqlServerValueColumn.Tag => value is byte[] bytes && Stored(bytes, 16) is null,
            _ => false,
        };

    /// <summary>
    /// 読み戻した値が期待に合うか（合えば null、合わなければ食い違いの説明）。断られない値にだけ呼ぶ
    /// </summary>
    public static string? Check(SqlServerValueColumn column, object written, object? read)
    {
        if (read is null)
        {
            return $"null が戻った（期待 {Format(Expected(column, written))}）";
        }

        if (column == SqlServerValueColumn.Ansi)
        {
            return CheckCodePage((string)written, read);
        }

        var expected = Expected(column, written);

        return Format(expected) == Format(read)
            ? null
            : $"期待 {Format(expected)}・実際 {Format(read)}";
    }

    /// <summary>期待の値（varchar の表せない文字だけは <see cref="CheckCodePage"/> が述語で見る）</summary>
    public static object Expected(SqlServerValueColumn column, object written) =>
        column switch
        {
            SqlServerValueColumn.At2 or SqlServerValueColumn.At => RoundDateTime(
                (DateTime)written,
                FractionalScale(column)
            ),
            SqlServerValueColumn.Clock => TimeSpan.FromTicks(
                RoundTicks(((TimeSpan)written).Ticks, 2, TimeSpan.TicksPerDay - 1)
            ),
            SqlServerValueColumn.AtOffset or SqlServerValueColumn.AtOffset2 => RoundOffset(
                (DateTimeOffset)written,
                FractionalScale(column)
            ),
            SqlServerValueColumn.Day => DateTime.SpecifyKind(
                ((DateTime)written).Date,
                DateTimeKind.Unspecified
            ),
            SqlServerValueColumn.Amount => Math.Round(
                (decimal)written,
                2,
                MidpointRounding.AwayFromZero
            ) + 0.00m,
            SqlServerValueColumn.Code => Stored((string)written, 10)!.PadRight(10, ' '),
            SqlServerValueColumn.Label => Stored((string)written, 20)!,
            SqlServerValueColumn.Ansi => Stored((string)written, 20)!,
            SqlServerValueColumn.Tag => Stored((byte[])written, 16)!,
            SqlServerValueColumn.Payload => (byte[])written,
            _ => throw new ArgumentOutOfRangeException(nameof(column), column, null),
        };

    /// <summary>読み戻した値の比べる表記（DateTime は tick と Kind・decimal は値とスケール・文字列はコード単位・バイトは 16 進）</summary>
    public static string Format(object? value) =>
        value switch
        {
            null => "null",
            DateTime d => $"{d.Ticks}|{d.Kind}",
            DateTimeOffset o => $"{o.Ticks}{o.Offset.Ticks:+0;-0}",
            TimeSpan t => t.Ticks.ToString(CultureInfo.InvariantCulture),
            decimal m => m.ToString(CultureInfo.InvariantCulture),
            string s => $"\"{Escape(s)}\"",
            byte[] b => Convert.ToHexString(b),
            _ => value.ToString() ?? "?",
        };

    /// <summary>ASCII の外と制御文字を \uXXXX で書く（失敗の説明用）</summary>
    public static string Escape(string text)
    {
        var builder = new StringBuilder();

        foreach (var c in text)
        {
            builder.Append(c is >= ' ' and <= '~' ? c.ToString() : $"\\u{(int)c:X4}");
        }

        return builder.ToString();
    }

    /// <summary>列の長さを超えた分が半角の空白だけなら切った値、超えた分に別の文字があれば null（断られる）</summary>
    private static string? Stored(string text, int length) =>
        text.Length <= length ? text
        : text[length..].All(c => c == ' ') ? text[..length]
        : null;

    /// <summary>列の長さを超えた分が 0x00 だけなら切った値、超えた分に別のバイトがあれば null（断られる）</summary>
    private static byte[]? Stored(byte[] bytes, int length) =>
        bytes.Length <= length ? bytes
        : bytes[length..].All(b => b == 0) ? bytes[..length]
        : null;

    /// <summary>s 桁へ四捨五入した値の整数部が p−s 桁に入るか</summary>
    private static bool FitsAfterRounding(decimal value, int precision, int scale)
    {
        var rounded = Math.Round(value, scale, MidpointRounding.AwayFromZero);
        var limit = 1m;

        for (var i = 0; i < precision - scale; i++)
        {
            limit *= 10;
        }

        return Math.Abs(rounded) < limit;
    }

    /// <summary>tick を小数秒 n 桁へ四捨五入する（丸めると上限を出る値は、上限以下で n 桁の最後の値で止める）</summary>
    private static long RoundTicks(long ticks, int scale, long maxTicks)
    {
        var unit = 1L;

        for (var i = scale; i < 7; i++)
        {
            unit *= 10;
        }

        var remainder = ticks % unit;
        var rounded = ticks - remainder + (remainder * 2 >= unit ? unit : 0);

        return rounded <= maxTicks ? rounded : maxTicks - maxTicks % unit;
    }

    private static DateTime RoundDateTime(DateTime value, int scale) =>
        new(RoundTicks(value.Ticks, scale, DateTime.MaxValue.Ticks), DateTimeKind.Unspecified);

    /// <summary>ローカルの時刻を丸め、オフセットを保つ（UTC の範囲を出るなら、範囲の中の最後の値で止める）</summary>
    private static DateTimeOffset RoundOffset(DateTimeOffset value, int scale)
    {
        var maxLocal = DateTimeOffset.MaxValue.UtcTicks + value.Offset.Ticks;
        var ticks = RoundTicks(value.Ticks, scale, Math.Min(maxLocal, DateTime.MaxValue.Ticks));

        return new DateTimeOffset(ticks, value.Offset);
    }

    /// <summary>varchar：コードページ 1252 が表せる文字は保ち、表せない文字はコード単位ごとに元と違う 1 文字</summary>
    private static string? CheckCodePage(string written, object read)
    {
        var expected = Stored(written, 20)!;

        if (read is not string actual || actual.Length != expected.Length)
        {
            return $"期待 {Format(expected)} と同じ長さ・実際 {Format(read)}";
        }

        for (var i = 0; i < expected.Length; i++)
        {
            var held = IsHeldByCodePage(expected[i]);

            if (
                held
                    ? actual[i] != expected[i]
                    : actual[i] == expected[i] || !IsHeldByCodePage(actual[i])
            )
            {
                return $"{i} 文字目：期待 {(held ? Format(expected[i].ToString()) : "表せない文字の置き換え")}・実際 {Format(read)}";
            }
        }

        return null;
    }

    /// <summary>コードページ 1252 が 1 文字（サロゲートの片方を除く）を表せるか</summary>
    private static bool IsHeldByCodePage(char c)
    {
        if (char.IsSurrogate(c))
        {
            return false;
        }

        try
        {
            var bytes = CodePage.GetBytes([c]);
            return bytes.Length == 1 && CodePage.GetString(bytes) == c.ToString();
        }
        catch (EncoderFallbackException)
        {
            return false;
        }
    }

    /// <summary>コードページ 1252（表せない文字は例外）</summary>
    private static readonly Encoding CodePage = CodePagesEncodingProvider.Instance.GetEncoding(
        1252,
        EncoderFallback.ExceptionFallback,
        DecoderFallback.ExceptionFallback
    )!;
}
