using AwesomeAssertions;
using QuickER.SqlServer;

namespace QuickER.Tests.SqlServer;

/// <summary>
/// <see cref="SqlIdentifier"/> の識別子クォート・エスケープ境界を検証するテストクラス。
/// SQL Server は識別子を <c>[..]</c> で括弧付けし、内部の <c>]</c> を <c>]]</c> へ二重化してエスケープする。
/// </summary>
public class SqlIdentifierTests
{
    [Fact(DisplayName = "Bracket は単純な識別子を [name] 形式で括弧付けする")]
    public void Bracket_SimpleName_WrapsInBrackets()
    {
        SqlIdentifier.Bracket("Users").Should().Be("[Users]");
    }

    [Fact(DisplayName = "Bracket はスキーマ修飾名を [schema].[name] 形式へ分割して括弧付けする")]
    public void Bracket_SchemaQualifiedName_SplitsIntoTwoBracketedParts()
    {
        SqlIdentifier.Bracket("dbo.Users").Should().Be("[dbo].[Users]");
    }

    [Fact(
        DisplayName = "Bracket は最初のドットのみで分割し、2 個目以降のドットはそのまま第2部に残る"
    )]
    public void Bracket_NameWithMultipleDots_SplitsOnlyOnFirstDot()
    {
        // 3階層以上の名前は非対応。2分割された第2部 "b.c" はそのまま1つの識別子として括弧付けされる
        SqlIdentifier.Bracket("a.b.c").Should().Be("[a].[b.c]");
    }

    [Fact(DisplayName = "BracketSimple は単一識別子を括弧付けする")]
    public void BracketSimple_SimpleName_WrapsInBrackets()
    {
        SqlIdentifier.BracketSimple("Users").Should().Be("[Users]");
    }

    [Fact(DisplayName = "BracketSimple はドットを含む名前も分割せず単一識別子として括弧付けする")]
    public void BracketSimple_NameWithDot_DoesNotSplit()
    {
        SqlIdentifier.BracketSimple("dbo.Users").Should().Be("[dbo.Users]");
    }

    [Fact(DisplayName = "Escape は識別子内の ] を ]] へ二重化する")]
    public void Escape_ContainingClosingBracket_DoublesIt()
    {
        SqlIdentifier.Escape("User]Name").Should().Be("User]]Name");
    }

    [Fact(DisplayName = "Escape は連続する複数の ] をすべて二重化する")]
    public void Escape_ContainingMultipleClosingBrackets_DoublesEach()
    {
        SqlIdentifier.Escape("]]a]").Should().Be("]]]]a]]");
    }

    [Fact(DisplayName = "Escape は [ をエスケープしない（] のみが閉じ括弧のため）")]
    public void Escape_ContainingOpeningBracket_LeavesItUnescaped()
    {
        SqlIdentifier.Escape("[Name").Should().Be("[Name");
    }

    [Fact(DisplayName = "Bracket は ] を含む識別子を正しくエスケープしつつ括弧付けする")]
    public void Bracket_ContainingClosingBracket_EscapesBeforeWrapping()
    {
        // "User]Name" -> エスケープで "User]]Name" -> 括弧付けで "[User]]Name]"
        SqlIdentifier.Bracket("User]Name").Should().Be("[User]]Name]");
    }

    [Fact(DisplayName = "Bracket はスキーマ修飾名の各部を個別にエスケープする")]
    public void Bracket_SchemaQualifiedNameWithBracket_EscapesEachPartIndependently()
    {
        SqlIdentifier.Bracket("db]o.Us]ers").Should().Be("[db]]o].[Us]]ers]");
    }

    [Fact(
        DisplayName = "Bracket / BracketSimple / Escape は空白・記号・日本語を含む識別子をそのまま通す"
    )]
    public void Bracket_WithWhitespaceSymbolsAndJapanese_PassesThroughUnescaped()
    {
        const string name = "顧客 名前#1";

        SqlIdentifier.Bracket(name).Should().Be("[顧客 名前#1]");
        SqlIdentifier.BracketSimple(name).Should().Be("[顧客 名前#1]");
        SqlIdentifier.Escape(name).Should().Be(name);
    }

    [Theory(DisplayName = "Bracket は空文字・null に対して [] を返す（例外にならない）")]
    [InlineData("")]
    [InlineData(null)]
    public void Bracket_EmptyOrNull_ReturnsEmptyBrackets(string? name)
    {
        SqlIdentifier.Bracket(name!).Should().Be("[]");
    }

    [Theory(DisplayName = "BracketSimple は空文字・null に対して [] を返す（例外にならない）")]
    [InlineData("")]
    [InlineData(null)]
    public void BracketSimple_EmptyOrNull_ReturnsEmptyBrackets(string? name)
    {
        SqlIdentifier.BracketSimple(name!).Should().Be("[]");
    }

    [Theory(DisplayName = "Escape は空文字・null に対して空文字を返す（例外にならない）")]
    [InlineData("")]
    [InlineData(null)]
    public void Escape_EmptyOrNull_ReturnsEmptyString(string? name)
    {
        SqlIdentifier.Escape(name!).Should().Be(string.Empty);
    }
}
