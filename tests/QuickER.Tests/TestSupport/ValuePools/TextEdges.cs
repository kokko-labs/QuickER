namespace QuickER.Tests.TestSupport.ValuePools;

/// <summary>
/// ASCII の外の文字の部品（エンジン・表のテストのソースが非 ASCII の文字列を直に書かずに引く正本。文字はコードポイントのエスケープで書く）
/// </summary>
/// <remarks>
/// 文字列は部品の連結で組む（<c>"a" + TextEdges.KanjiNichi</c>）。連結はコンパイル時の定数で、元のリテラルと同じ文字列になる。
/// どの部品も水準 <c>str-non-ascii</c>（ASCII の外の文字を含む）に当たり、U+00FF を超える文字を含む部品は <c>str-non-latin1</c>、
/// サロゲートペアの部品は <c>str-surrogate</c> にも当たる。
/// </remarks>
public static class TextEdges
{
    /// <summary>é（U+00E9）。コードページ 1252 が表せ、Unicode でない列でも保てる非 ASCII</summary>
    public const string LatinSmallEAcute = "é";

    /// <summary>ä（U+00E4）。大文字小文字・アクセントの畳み込みで答えが割れる（Ä と対）</summary>
    public const string LatinSmallAUmlaut = "ä";

    /// <summary>Ä（U+00C4）。ä と大文字小文字だけが違う</summary>
    public const string LatinCapitalAUmlaut = "Ä";

    /// <summary>ß（U+00DF）。大文字小文字を畳む変換（ss）で長さの変わる文字</summary>
    public const string LatinSmallSharpS = "ß";

    /// <summary>€（U+20AC）。コードページ 1252 が表せ（0x80）、Latin-1 の範囲を超える</summary>
    public const string EuroSign = "€";

    /// <summary>ÿ（U+00FF）。コードページ 1252 が表せる非 ASCII のうち、Latin-1 の最後の文字</summary>
    public const string LatinSmallYDiaeresis = "ÿ";

    /// <summary>ñ（U+00F1）。コードページ 1252 が表せる非 ASCII</summary>
    public const string LatinSmallNTilde = "ñ";

    /// <summary>Ā（U+0100）。コードページ 1252 では近い文字（A）へ置き換わる</summary>
    public const string LatinCapitalAMacron = "Ā";

    /// <summary>ō（U+014D）。コードページ 1252 では近い文字（o）へ置き換わる</summary>
    public const string LatinSmallOMacron = "ō";

    /// <summary>Ł（U+0141）。コードページ 1252 では近い文字（L）へ置き換わる</summary>
    public const string LatinCapitalLStroke = "Ł";

    /// <summary>日（U+65E5）。コードページ 1252 が表せず近い文字も無い（Unicode でない列では ? に化ける）</summary>
    public const string KanjiNichi = "日";

    /// <summary>本（U+672C）。コードページ 1252 が表せず近い文字も無い</summary>
    public const string KanjiHon = "本";

    /// <summary>語（U+8A9E）。コードページ 1252 が表せず近い文字も無い</summary>
    public const string KanjiGo = "語";

    /// <summary>ア（U+30A2）。コードページ 1252 が表せず近い文字も無い</summary>
    public const string KatakanaA = "ア";

    /// <summary>日本（U+65E5 U+672C）。Unicode でない列で ? に化け、列の長さの数え方（文字数・バイト数）も割れる</summary>
    public const string KanjiNihon = KanjiNichi + KanjiHon;

    /// <summary>ソフトハイフン（U+00AD）。文字列比較でカルチャが無視する文字で、序数比較では別の文字</summary>
    public const string SoftHyphen = "­";

    /// <summary>改行しない空白（U+00A0）。半角の空白（U+0020）ではない空白類で、末尾の空白の判定が割れる</summary>
    public const string NoBreakSpace = " ";

    /// <summary>全角の空白（U+3000）。半角の空白（U+0020）ではない空白類で、末尾の空白の割り切りに入らない</summary>
    public const string IdeographicSpace = "\u3000";

    /// <summary>タブ（U+0009）。半角の空白（U+0020）ではない空白類で、末尾の空白の割り切りに入らない</summary>
    public const string Tab = "\t";

    /// <summary>全角の A（U+FF21）。全角と半角を区別しない照合順序（SQL Server の既定・MySQL の既定）で半角の A と同じ文字になる</summary>
    public const string FullwidthCapitalA = "\uFF21";

    /// <summary>全角の 1（U+FF11）。全角と半角を区別しない照合順序で半角の 1 と同じ文字になる</summary>
    public const string FullwidthDigitOne = "\uFF11";

    /// <summary>😀（U+1F600）。サロゲートペア（UTF-16 で 2 文字）で、文字数・バイト数・列の長さの数え方が割れる</summary>
    public const string GrinningFace = "\U0001F600";

    /// <summary>𠀋（U+2000B）。基本多言語面の外の漢字（サロゲートペア）</summary>
    public const string CjkExtensionBCharacter = "\U0002000B";
}
