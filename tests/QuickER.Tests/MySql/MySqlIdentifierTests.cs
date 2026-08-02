using AwesomeAssertions;
using QuickER.MySql;

namespace QuickER.Tests.MySql;

/// <summary>
/// <see cref="MySqlIdentifier"/> の識別子クォート・エスケープ境界を検証するテストクラス。
/// MySQL は識別子を <c>`..`</c> でバッククォートし、内部の <c>`</c> を <c>``</c> へ二重化してエスケープする。
/// </summary>
public class MySqlIdentifierTests
{
    [Fact(DisplayName = "Quote は単純な識別子を `name` 形式でクォートする")]
    public void Quote_SimpleName_WrapsInBackticks()
    {
        MySqlIdentifier.Quote("users").Should().Be("`users`");
    }

    [Fact(DisplayName = "Quote はスキーマ修飾名を `schema`.`name` 形式へ分割してクォートする")]
    public void Quote_SchemaQualifiedName_SplitsIntoTwoQuotedParts()
    {
        MySqlIdentifier.Quote("mydb.users").Should().Be("`mydb`.`users`");
    }

    [Fact(
        DisplayName = "Quote は最初のドットのみで分割し、2 個目以降のドットはそのまま第2部に残る"
    )]
    public void Quote_NameWithMultipleDots_SplitsOnlyOnFirstDot()
    {
        // 3階層以上の名前は非対応。2分割された第2部 "b.c" はそのまま1つの識別子としてクォートされる
        MySqlIdentifier.Quote("a.b.c").Should().Be("`a`.`b.c`");
    }

    [Fact(DisplayName = "QuoteSimple は単一識別子をクォートする")]
    public void QuoteSimple_SimpleName_WrapsInBackticks()
    {
        MySqlIdentifier.QuoteSimple("users").Should().Be("`users`");
    }

    [Fact(DisplayName = "QuoteSimple はドットを含む名前も分割せず単一識別子としてクォートする")]
    public void QuoteSimple_NameWithDot_DoesNotSplit()
    {
        MySqlIdentifier.QuoteSimple("mydb.users").Should().Be("`mydb.users`");
    }

    [Fact(DisplayName = "Escape は識別子内の ` を `` へ二重化する")]
    public void Escape_ContainingBacktick_DoublesIt()
    {
        MySqlIdentifier.Escape("User`Name").Should().Be("User``Name");
    }

    [Fact(DisplayName = "Escape は連続する複数の ` をすべて二重化する")]
    public void Escape_ContainingMultipleBackticks_DoublesEach()
    {
        MySqlIdentifier.Escape("``a`").Should().Be("````a``");
    }

    [Fact(DisplayName = "Quote は ` を含む識別子を正しくエスケープしつつクォートする")]
    public void Quote_ContainingBacktick_EscapesBeforeWrapping()
    {
        // "User`Name" -> エスケープで "User``Name" -> クォートで "`User``Name`"
        MySqlIdentifier.Quote("User`Name").Should().Be("`User``Name`");
    }

    [Fact(DisplayName = "Quote はスキーマ修飾名の各部を個別にエスケープする")]
    public void Quote_SchemaQualifiedNameWithBacktick_EscapesEachPartIndependently()
    {
        MySqlIdentifier.Quote("my`db.us`ers").Should().Be("`my``db`.`us``ers`");
    }

    [Fact(
        DisplayName = "Quote / QuoteSimple / Escape は空白・記号・日本語を含む識別子をそのまま通す"
    )]
    public void Quote_WithWhitespaceSymbolsAndJapanese_PassesThroughUnescaped()
    {
        const string name = "顧客 名前#1";

        MySqlIdentifier.Quote(name).Should().Be("`顧客 名前#1`");
        MySqlIdentifier.QuoteSimple(name).Should().Be("`顧客 名前#1`");
        MySqlIdentifier.Escape(name).Should().Be(name);
    }

    [Theory(DisplayName = "Quote は空文字・null に対して `` を返す（例外にならない）")]
    [InlineData("")]
    [InlineData(null)]
    public void Quote_EmptyOrNull_ReturnsEmptyBackticks(string? name)
    {
        MySqlIdentifier.Quote(name!).Should().Be("``");
    }

    [Theory(DisplayName = "QuoteSimple は空文字・null に対して `` を返す（例外にならない）")]
    [InlineData("")]
    [InlineData(null)]
    public void QuoteSimple_EmptyOrNull_ReturnsEmptyBackticks(string? name)
    {
        MySqlIdentifier.QuoteSimple(name!).Should().Be("``");
    }

    [Theory(DisplayName = "Escape は空文字・null に対して空文字を返す（例外にならない）")]
    [InlineData("")]
    [InlineData(null)]
    public void Escape_EmptyOrNull_ReturnsEmptyString(string? name)
    {
        MySqlIdentifier.Escape(name!).Should().Be(string.Empty);
    }
}
