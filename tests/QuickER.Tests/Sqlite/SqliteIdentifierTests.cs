using AwesomeAssertions;
using QuickER.Sqlite;

namespace QuickER.Tests.Sqlite;

/// <summary>
/// <see cref="SqliteIdentifier"/> の識別子クォート・エスケープ境界を検証するテストクラス。
/// SQLite は識別子を <c>"..."</c> で二重引用符クォートし、内部の <c>"</c> を <c>""</c> へ二重化してエスケープする
/// （<see cref="QuickER.PostgreSql.PgIdentifier"/> と同じ二重引用符方式）。
/// </summary>
public class SqliteIdentifierTests
{
    [Fact(DisplayName = "Quote は単純な識別子を \"name\" 形式でクォートする")]
    public void Quote_SimpleName_WrapsInDoubleQuotes()
    {
        SqliteIdentifier.Quote("users").Should().Be("\"users\"");
    }

    [Fact(
        DisplayName = "Quote はスキーマ修飾名（SQL Server 由来）を \"schema\".\"name\" 形式へ分割してクォートする"
    )]
    public void Quote_SchemaQualifiedName_SplitsIntoTwoQuotedParts()
    {
        SqliteIdentifier.Quote("dbo.users").Should().Be("\"dbo\".\"users\"");
    }

    [Fact(
        DisplayName = "Quote は最初のドットのみで分割し、2 個目以降のドットはそのまま第2部に残る"
    )]
    public void Quote_NameWithMultipleDots_SplitsOnlyOnFirstDot()
    {
        // 3階層以上の名前は非対応。2分割された第2部 "b.c" はそのまま1つの識別子としてクォートされる
        SqliteIdentifier.Quote("a.b.c").Should().Be("\"a\".\"b.c\"");
    }

    [Fact(DisplayName = "QuoteSimple は単一識別子をクォートする")]
    public void QuoteSimple_SimpleName_WrapsInDoubleQuotes()
    {
        SqliteIdentifier.QuoteSimple("users").Should().Be("\"users\"");
    }

    [Fact(DisplayName = "QuoteSimple はドットを含む名前も分割せず単一識別子としてクォートする")]
    public void QuoteSimple_NameWithDot_DoesNotSplit()
    {
        SqliteIdentifier.QuoteSimple("dbo.users").Should().Be("\"dbo.users\"");
    }

    [Fact(DisplayName = "Escape は識別子内の \" を \"\" へ二重化する")]
    public void Escape_ContainingDoubleQuote_DoublesIt()
    {
        SqliteIdentifier.Escape("User\"Name").Should().Be("User\"\"Name");
    }

    [Fact(DisplayName = "Escape は連続する複数の \" をすべて二重化する")]
    public void Escape_ContainingMultipleDoubleQuotes_DoublesEach()
    {
        SqliteIdentifier.Escape("\"\"a\"").Should().Be("\"\"\"\"a\"\"");
    }

    [Fact(DisplayName = "Quote は \" を含む識別子を正しくエスケープしつつクォートする")]
    public void Quote_ContainingDoubleQuote_EscapesBeforeWrapping()
    {
        // "User\"Name" -> エスケープで "User\"\"Name" -> クォートで "\"User\"\"Name\""
        SqliteIdentifier.Quote("User\"Name").Should().Be("\"User\"\"Name\"");
    }

    [Fact(DisplayName = "Quote はスキーマ修飾名の各部を個別にエスケープする")]
    public void Quote_SchemaQualifiedNameWithDoubleQuote_EscapesEachPartIndependently()
    {
        SqliteIdentifier.Quote("db\"o.us\"ers").Should().Be("\"db\"\"o\".\"us\"\"ers\"");
    }

    [Fact(
        DisplayName = "Quote / QuoteSimple / Escape は空白・記号・日本語を含む識別子をそのまま通す"
    )]
    public void Quote_WithWhitespaceSymbolsAndJapanese_PassesThroughUnescaped()
    {
        const string name = "顧客 名前#1";

        SqliteIdentifier.Quote(name).Should().Be("\"顧客 名前#1\"");
        SqliteIdentifier.QuoteSimple(name).Should().Be("\"顧客 名前#1\"");
        SqliteIdentifier.Escape(name).Should().Be(name);
    }

    [Theory(DisplayName = "Quote は空文字・null に対して \"\" を返す（例外にならない）")]
    [InlineData("")]
    [InlineData(null)]
    public void Quote_EmptyOrNull_ReturnsEmptyQuotes(string? name)
    {
        SqliteIdentifier.Quote(name!).Should().Be("\"\"");
    }

    [Theory(DisplayName = "QuoteSimple は空文字・null に対して \"\" を返す（例外にならない）")]
    [InlineData("")]
    [InlineData(null)]
    public void QuoteSimple_EmptyOrNull_ReturnsEmptyQuotes(string? name)
    {
        SqliteIdentifier.QuoteSimple(name!).Should().Be("\"\"");
    }

    [Theory(DisplayName = "Escape は空文字・null に対して空文字を返す（例外にならない）")]
    [InlineData("")]
    [InlineData(null)]
    public void Escape_EmptyOrNull_ReturnsEmptyString(string? name)
    {
        SqliteIdentifier.Escape(name!).Should().Be(string.Empty);
    }
}
