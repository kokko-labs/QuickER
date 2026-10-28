using System;
using System.Collections;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;

namespace QuickER.Tests.TestSupport.Engines;

/// <summary>
/// 種の指紋（<see cref="SeedFingerprint"/>）の元にする、生成器の入力の要約を書く部品
/// </summary>
/// <remarks>
/// <para>
/// 値は型の名前つきで、カルチャに依らない書式で書く（<c>1</c> の int と long、空白の有無を見分ける）。
/// 文字列は引用符で囲み、ASCII の印字できる文字以外を <c>\uXXXX</c> で書く。日時は往復の書式（Kind を含む。Local は機械の時差を付けず <c>Local:</c> と時差なしの時刻を書く）、
/// バイト列は 16 進、一覧は要素を並べて展開する（<c>ToString</c> が型の名前しか出さない一覧の中身を見落とさない）。
/// </para>
/// <para>
/// <see cref="Object"/> は公開のインスタンスのプロパティを名前の序数順に展開する（record の操作を 1 つずつ書かなくても、
/// 乱数で決まる値をすべて文字列に出す）。<see cref="Type"/> は名前だけ、デリゲートは印だけを書く。
/// 要約は読むだけで、生成器の乱数を引かない。
/// </para>
/// </remarks>
public static class SeedSummary
{
    /// <summary>展開する入れ子の深さの上限（循環する参照で止まらなくならないように）</summary>
    private const int MaxDepth = 12;

    /// <summary>要約を作る間だけ、CurrentCulture と CurrentUICulture をインバリアントに固定する</summary>
    /// <remarks>
    /// 指紋の記録は日本語の機械で作り、CI は英語で動く。要約に resx の文（差分の説明など）や、カルチャに従う書式が入っても、
    /// 機械の言語で指紋が変わらないようにする。指紋のドリフトテストは <see cref="SeedFingerprint.Compute"/> を通して必ずこの中で作る。
    /// </remarks>
    /// <param name="build">要約を作る処理</param>
    public static string UnderInvariantCulture(Func<string> build)
    {
        ArgumentNullException.ThrowIfNull(build);
        var culture = CultureInfo.CurrentCulture;
        var uiCulture = CultureInfo.CurrentUICulture;

        try
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;
            return build();
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = uiCulture;
        }
    }

    /// <summary>日時の書き方（Local は機械の時差を付けない＝タイムゾーンの違う機械で同じ要約になる）</summary>
    /// <param name="value">日時</param>
    public static string DateTimeText(DateTime value) =>
        value.Kind == DateTimeKind.Local
            ? "Local:"
                + DateTime
                    .SpecifyKind(value, DateTimeKind.Unspecified)
                    .ToString("o", CultureInfo.InvariantCulture)
            : value.ToString("o", CultureInfo.InvariantCulture);

    /// <summary>値を 1 つ書く（オブジェクトはプロパティを展開する）</summary>
    /// <param name="value">値</param>
    public static string Object(object? value)
    {
        var text = new StringBuilder();
        Append(text, value, 0);
        return text.ToString();
    }

    /// <summary>文字列を引用符つきで書く（空白と非 ASCII を見せる）</summary>
    /// <param name="value">文字列（null なら <c>null</c>）</param>
    public static string Quote(string? value)
    {
        if (value is null)
        {
            return "null";
        }

        var text = new StringBuilder("\"");

        foreach (var c in value)
        {
            if (c is >= ' ' and <= '~' and not '"' and not '\\')
            {
                text.Append(c);
            }
            else
            {
                text.Append(CultureInfo.InvariantCulture, $"\\u{(int)c:X4}");
            }
        }

        return text.Append('"').ToString();
    }

    private static void Append(StringBuilder text, object? value, int depth)
    {
        switch (value)
        {
            case null:
                text.Append("null");
                return;
            case string s:
                text.Append(Quote(s));
                return;
            case bool b:
                text.Append(b ? "true" : "false");
                return;
            case char c:
                text.Append("char:").Append(Quote(c.ToString()));
                return;
            case Enum e:
                text.Append(e.GetType().Name).Append('.').Append(e.ToString());
                return;
            case DateTime dt:
                text.Append("DateTime:").Append(DateTimeText(dt));
                return;
            case DateTimeOffset dto:
                text.Append("DateTimeOffset:")
                    .Append(dto.ToString("o", CultureInfo.InvariantCulture));
                return;
            case TimeSpan ts:
                text.Append("TimeSpan:").Append(ts.ToString("c", CultureInfo.InvariantCulture));
                return;
            case byte[] bytes:
                text.Append("bytes:").Append(Convert.ToHexString(bytes));
                return;
            case Type type:
                text.Append("type:").Append(type.FullName ?? type.Name);
                return;
            case Delegate:
                text.Append("<delegate>");
                return;
            case IFormattable formattable:
                text.Append(value.GetType().Name)
                    .Append(':')
                    .Append(formattable.ToString(null, CultureInfo.InvariantCulture));
                return;
        }

        if (depth >= MaxDepth)
        {
            text.Append("<depth>");
            return;
        }

        if (value is IEnumerable sequence)
        {
            text.Append('[');
            var first = true;

            foreach (var item in sequence)
            {
                if (!first)
                {
                    text.Append(", ");
                }

                Append(text, item, depth + 1);
                first = false;
            }

            text.Append(']');
            return;
        }

        var properties = value
            .GetType()
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.GetIndexParameters().Length == 0 && p.CanRead)
            .OrderBy(p => p.Name, StringComparer.Ordinal);

        text.Append(value.GetType().Name).Append('{');
        var firstProperty = true;

        foreach (var property in properties)
        {
            if (!firstProperty)
            {
                text.Append(", ");
            }

            text.Append(property.Name).Append('=');
            object? propertyValue;

            try
            {
                propertyValue = property.GetValue(value);
            }
            catch (TargetInvocationException ex)
            {
                text.Append("<throws:").Append(ex.InnerException?.GetType().Name).Append('>');
                firstProperty = false;
                continue;
            }

            Append(text, propertyValue, depth + 1);
            firstProperty = false;
        }

        text.Append('}');
    }
}
