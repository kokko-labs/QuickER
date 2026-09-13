using AwesomeAssertions;
using QuickER.Provider;
using Xunit;

namespace QuickER.Tests.Provider;

/// <summary>
/// <see cref="SqlComment"/> のサニタイズ規則を検証するテストクラス。
/// </summary>
/// <remarks>
/// <c>--</c> は行末までの行コメントなので、図の名前・説明に改行が混じるとコメントが途中で終わり、
/// 2 行目以降が実行される SQL として解釈される。ここでは「畳む対象」と「1 文字 → 空白 1 つ」の規則を固定する。
/// </remarks>
public class SqlCommentTests
{
    /// <summary>C0 制御文字（STX）。ソースへ生の制御文字を書かないよう文字コードから組み立てる</summary>
    private static readonly string Stx = ((char)0x02).ToString();

    /// <summary>DEL（U+007F）。C0 の範囲外だが同じく畳む対象</summary>
    private static readonly string Del = ((char)0x7F).ToString();

    [Fact(DisplayName = "Sanitize は改行（CRLF / LF / CR）を空白へ畳む")]
    public void Sanitize_NewLines_FoldedToSpaces()
    {
        SqlComment.Sanitize("a\r\nb").Should().Be("a  b");
        SqlComment.Sanitize("a\nb").Should().Be("a b");
        SqlComment.Sanitize("a\rb").Should().Be("a b");
    }

    [Fact(DisplayName = "Sanitize はタブ・C0 制御文字・DEL を空白へ畳む")]
    public void Sanitize_ControlCharacters_FoldedToSpaces()
    {
        SqlComment.Sanitize("a\tb").Should().Be("a b");
        SqlComment.Sanitize("a" + Stx + "b").Should().Be("a b");
        SqlComment.Sanitize("a" + Del + "b").Should().Be("a b");
    }

    [Fact(DisplayName = "Sanitize は制御文字 1 文字を空白 1 つへ置き換え、長さを変えない")]
    public void Sanitize_PreservesLength()
    {
        SqlComment.Sanitize("a\r\n\r\nb").Should().Be("a    b");
    }

    [Fact(DisplayName = "Sanitize は制御文字を含まない文字列をそのまま返す")]
    public void Sanitize_PlainText_Unchanged()
    {
        SqlComment.Sanitize("orders テーブル (v2)").Should().Be("orders テーブル (v2)");
        SqlComment.Sanitize(null).Should().Be(string.Empty);
        SqlComment.Sanitize(string.Empty).Should().Be(string.Empty);
    }

    [Fact(DisplayName = "ContainsControlCharacter は改行・制御文字の有無だけを判定する")]
    public void ContainsControlCharacter_DetectsOnlyControlCharacters()
    {
        SqlComment.ContainsControlCharacter("a\nb").Should().BeTrue();
        SqlComment.ContainsControlCharacter("a" + Del + "b").Should().BeTrue();

        // クォート類は制御文字ではない（DB 取込で実在し得るためエスケープで扱い、診断の Error にはしない）
        SqlComment.ContainsControlCharacter("o'brien").Should().BeFalse();
        SqlComment.ContainsControlCharacter("a\"b").Should().BeFalse();
        SqlComment.ContainsControlCharacter("a]b").Should().BeFalse();
        SqlComment.ContainsControlCharacter(null).Should().BeFalse();
    }
}
