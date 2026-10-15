namespace QuickER.Dialects;

/// <summary>
/// MySQL が表の名前をファイル名へ符号化したときの長さ（<c>my_charset_filename</c> の移植）
/// </summary>
/// <remarks>
/// <para>
/// 出典は MySQL 8.4.6 のソース <c>strings/ctype-utf8.cc</c> の <c>my_wc_mb_filename</c> と、そこから引く
/// 5 つの表（<c>uni_0C00_05FF</c> / <c>uni_1E00_1FFF</c> / <c>uni_2160_217F</c> / <c>uni_24B0_24EF</c> /
/// <c>uni_FF20_FF5F</c>）。表の中身（符号の値）は要らず、0 でない要素の位置＝3 バイトで書ける文字だけを範囲として写した。
/// 1 文字の長さは次の 3 通り。
/// </para>
/// <list type="bullet">
/// <item>ASCII の英数字と <c>_</c>（<c>filename_safe_char</c>）: 1 バイト（そのまま）</item>
/// <item>上の 5 つの表に載る文字（ラテン・ギリシャ・キリル・アルメニアの文字、ローマ数字、丸囲みの英字、全角英字）: 3 バイト（<c>@</c>＋2 文字）</item>
/// <item>それ以外（記号・空白・CJK など）: 5 バイト（<c>@</c>＋16 進 4 桁）</item>
/// </list>
/// <para>
/// BMP の外の文字（サロゲート対）は、MySQL が識別子の文字集合（<c>utf8mb3</c>）へ変換するときに <c>?</c> へ置き換えるので、
/// <c>?</c> と同じ 5 バイトと数える（実 MySQL 8.4 で <c>a😀b</c> の表が <c>a?b</c> という名前・<c>a@003fb.ibd</c> という
/// ファイルになることを確かめた）。予約されたデバイス名（<c>CON</c> など）に付く <c>@@@</c> は短い名前にしか付かないため数えない。
/// </para>
/// <para>
/// 符号化した長さと実 MySQL の <c>information_schema.INNODB_TABLES</c> の名前との一致は、Integration の
/// <c>IdentifierLengthLimitIntegrationTests</c> が範囲の境界の文字ごとに確かめる。
/// </para>
/// </remarks>
public static class MySqlFileNameEncoding
{
    /// <summary>表の名前をファイル名へ符号化したときの長さ（バイト数）</summary>
    public static int EncodedLength(string tableName)
    {
        var length = 0;

        foreach (var rune in tableName.EnumerateRunes())
        {
            length += EncodedLength(rune.Value);
        }

        return length;
    }

    /// <summary>1 文字（Unicode のスカラー値）をファイル名へ符号化したときの長さ</summary>
    public static int EncodedLength(int codePoint)
    {
        if (codePoint < 128)
        {
            return IsSafeAscii(codePoint) ? 1 : 5;
        }

        return IsLetterWithShortCode(codePoint) ? 3 : 5;
    }

    /// <summary>そのまま書ける ASCII（英数字と <c>_</c>）</summary>
    private static bool IsSafeAscii(int c) =>
        c is >= '0' and <= '9' or >= 'A' and <= 'Z' or >= 'a' and <= 'z' or '_';

    /// <summary>3 バイトの符号を持つ文字か（範囲は二分探索）</summary>
    private static bool IsLetterWithShortCode(int c)
    {
        var low = 0;
        var high = ShortCodeRanges.Length - 1;

        while (low <= high)
        {
            var mid = (low + high) / 2;
            var (first, last) = ShortCodeRanges[mid];

            if (c < first)
            {
                high = mid - 1;
            }
            else if (c > last)
            {
                low = mid + 1;
            }
            else
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>3 バイトの符号を持つ文字の範囲（両端を含む・昇順。出典は型の XmlDoc）</summary>
    /// <remarks>実 MySQL との突き合わせ（Integration）が範囲を列挙するために公開する。</remarks>
    public static IReadOnlyList<(int First, int Last)> ShortCodeRangeList => ShortCodeRanges;

    /// <summary><see cref="ShortCodeRangeList"/> の実体</summary>
    private static readonly (int First, int Last)[] ShortCodeRanges =
    [
        (0x00C0, 0x00D6),
        (0x00D8, 0x00F6),
        (0x00F8, 0x012F),
        (0x0131, 0x01BE),
        (0x01C4, 0x01C4),
        (0x01C6, 0x01C7),
        (0x01C9, 0x01CA),
        (0x01CC, 0x01F1),
        (0x01F3, 0x01F6),
        (0x01F8, 0x0241),
        (0x0250, 0x02AF),
        (0x0386, 0x0386),
        (0x0388, 0x038A),
        (0x038C, 0x038C),
        (0x038E, 0x03A1),
        (0x03A3, 0x03CE),
        (0x03D0, 0x03D7),
        (0x03D9, 0x03F3),
        (0x03F5, 0x03F6),
        (0x03F8, 0x03F8),
        (0x03FB, 0x0481),
        (0x048A, 0x04CE),
        (0x04D0, 0x04F9),
        (0x0500, 0x050F),
        (0x0531, 0x0555),
        (0x0561, 0x0585),
        (0x1E00, 0x1E9B),
        (0x1EA0, 0x1EF9),
        (0x1F00, 0x1F15),
        (0x1F18, 0x1F1D),
        (0x1F20, 0x1F45),
        (0x1F48, 0x1F4D),
        (0x1F50, 0x1F57),
        (0x1F59, 0x1F59),
        (0x1F5B, 0x1F5B),
        (0x1F5D, 0x1F5D),
        (0x1F5F, 0x1F7D),
        (0x1F80, 0x1FB4),
        (0x1FB6, 0x1FBC),
        (0x1FC2, 0x1FC4),
        (0x1FC6, 0x1FCC),
        (0x1FD0, 0x1FD3),
        (0x1FD6, 0x1FDB),
        (0x1FE0, 0x1FEC),
        (0x1FF2, 0x1FF3),
        (0x1FF6, 0x1FFC),
        (0x2160, 0x217F),
        (0x24B6, 0x24E9),
        (0xFF21, 0xFF3A),
        (0xFF41, 0xFF5A),
    ];
}
