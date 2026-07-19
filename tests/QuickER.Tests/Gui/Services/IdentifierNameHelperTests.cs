using FluentAssertions;
using QuickER.Services;

namespace QuickER.Tests.Gui.Services;

/// <summary><see cref="IdentifierNameHelper"/> の識別子分解・単複変換・パスカル整形の境界を検証するテストクラス</summary>
public class IdentifierNameHelperTests
{
    /// <summary>null・空白のみの入力は空リストになることを検証する</summary>
    [Theory(DisplayName = "SplitIdentifierWords: null・空白のみは空リスト")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    public void SplitIdentifierWords_NullOrBlank_ReturnsEmpty(string? value)
    {
        IdentifierNameHelper.SplitIdentifierWords(value!).Should().BeEmpty();
    }

    /// <summary>各命名規約が単語列へ正しく分解されることを検証する</summary>
    [Theory(DisplayName = "SplitIdentifierWords: 各命名規約を単語へ分解する")]
    [InlineData("user_name", new[] { "user", "name" })]
    [InlineData("UserName", new[] { "User", "Name" })]
    [InlineData("customer-id", new[] { "customer", "id" })]
    [InlineData("OrderID", new[] { "Order", "ID" })]
    [InlineData("HTTPServer", new[] { "HTTP", "Server" })]
    [InlineData("field1", new[] { "field", "1" })]
    [InlineData("1st", new[] { "1", "st" })]
    [InlineData("user@#name", new[] { "user", "name" })]
    [InlineData("  Order  ", new[] { "Order" })]
    [InlineData("id", new[] { "id" })]
    public void SplitIdentifierWords_SplitsByConvention(string value, string[] expected)
    {
        IdentifierNameHelper.SplitIdentifierWords(value).Should().Equal(expected);
    }

    /// <summary>複数の区切り・境界が混在しても連続空白を潰して分解することを検証する</summary>
    [Fact(DisplayName = "SplitIdentifierWords: 記号・大小・数字の混在を単語へ分解する")]
    public void SplitIdentifierWords_MixedBoundaries()
    {
        IdentifierNameHelper
            .SplitIdentifierWords("orderLine__Item2Detail")
            .Should()
            .Equal("order", "Line", "Item", "2", "Detail");
    }

    /// <summary>1 文字以下は複数形とみなさないことを検証する</summary>
    [Theory(DisplayName = "IsLikelyPlural: 1 文字以下は複数形でない")]
    [InlineData("s")]
    [InlineData("a")]
    [InlineData("")]
    public void IsLikelyPlural_ShortWord_IsFalse(string word)
    {
        IdentifierNameHelper.IsLikelyPlural(word).Should().BeFalse();
    }

    /// <summary>典型的な複数形語尾が複数形と判定されることを検証する</summary>
    [Theory(DisplayName = "IsLikelyPlural: 複数形語尾は真")]
    [InlineData("cats")]
    [InlineData("cities")]
    [InlineData("boxes")]
    [InlineData("dishes")]
    [InlineData("buses")]
    [InlineData("heroes")]
    [InlineData("quizzes")]
    [InlineData("branches")]
    public void IsLikelyPlural_PluralEndings_AreTrue(string word)
    {
        IdentifierNameHelper.IsLikelyPlural(word).Should().BeTrue();
    }

    /// <summary>-ss / -us / -is 語尾や単数語は複数形と判定しないことを検証する</summary>
    [Theory(DisplayName = "IsLikelyPlural: -ss/-us/-is・単数語は偽")]
    [InlineData("class")]
    [InlineData("status")]
    [InlineData("analysis")]
    [InlineData("cat")]
    [InlineData("box")]
    public void IsLikelyPlural_NonPlural_AreFalse(string word)
    {
        IdentifierNameHelper.IsLikelyPlural(word).Should().BeFalse();
    }

    /// <summary>複数形が語尾ルールで単数形へ変換されることを検証する</summary>
    [Theory(DisplayName = "SingularizeWord: 語尾ルールで単数化する")]
    [InlineData("cities", "city")]
    [InlineData("boxes", "box")]
    [InlineData("dishes", "dish")]
    [InlineData("buses", "bus")]
    [InlineData("cats", "cat")]
    [InlineData("heroes", "hero")]
    public void SingularizeWord_ConvertsPlural(string word, string expected)
    {
        IdentifierNameHelper.SingularizeWord(word).Should().Be(expected);
    }

    /// <summary>すでに単数形の語は変化しないことを検証する</summary>
    [Theory(DisplayName = "SingularizeWord: 単数語・不変語はそのまま")]
    [InlineData("cat")]
    [InlineData("class")]
    [InlineData("status")]
    public void SingularizeWord_NonPlural_Unchanged(string word)
    {
        IdentifierNameHelper.SingularizeWord(word).Should().Be(word);
    }

    /// <summary>単数形が語尾ルールで複数形へ変換されることを検証する</summary>
    [Theory(DisplayName = "PluralizeWord: 語尾ルールで複数化する")]
    [InlineData("cat", "cats")]
    [InlineData("city", "cities")]
    [InlineData("box", "boxes")]
    [InlineData("dish", "dishes")]
    [InlineData("hero", "heroes")]
    [InlineData("class", "classes")]
    [InlineData("boy", "boys")]
    [InlineData("quiz", "quizes")]
    public void PluralizeWord_ConvertsSingular(string word, string expected)
    {
        IdentifierNameHelper.PluralizeWord(word).Should().Be(expected);
    }

    /// <summary>母音+y は ies にせず s を付けることを検証する</summary>
    [Fact(DisplayName = "PluralizeWord: 母音+y は s を付ける")]
    public void PluralizeWord_VowelBeforeY_AddsS()
    {
        IdentifierNameHelper.PluralizeWord("day").Should().Be("days");
    }

    /// <summary>すでに複数形の語は変化しないことを検証する</summary>
    [Theory(DisplayName = "PluralizeWord: 複数語はそのまま")]
    [InlineData("cats")]
    [InlineData("cities")]
    public void PluralizeWord_AlreadyPlural_Unchanged(string word)
    {
        IdentifierNameHelper.PluralizeWord(word).Should().Be(word);
    }

    /// <summary>各語がパスカルケース（先頭大文字・以降小文字）へ整えられることを検証する</summary>
    [Theory(DisplayName = "ToPascalWord: 先頭大文字・以降小文字へ整える")]
    [InlineData("hello", "Hello")]
    [InlineData("HELLO", "Hello")]
    [InlineData("Hello", "Hello")]
    [InlineData("a", "A")]
    [InlineData("x", "X")]
    [InlineData("iD", "Id")]
    [InlineData("", "")]
    public void ToPascalWord_NormalizesCasing(string word, string expected)
    {
        IdentifierNameHelper.ToPascalWord(word).Should().Be(expected);
    }
}
