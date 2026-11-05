using System;
using System.Collections.Generic;
using System.Linq;
using AwesomeAssertions;
using QuickER.Dialects;
using QuickER.Model;
using QuickER.Provider;
using QuickER.Provider.MySql;
using QuickER.Provider.Oracle;
using QuickER.Provider.PostgreSql;
using QuickER.Provider.Sqlite;
using QuickER.Provider.SqlServer;
using Xunit;

namespace QuickER.Tests.Provider;

/// <summary>
/// 説明の BMP の外の文字（絵文字など）を保存できない方言（MySQL）で、差分が保存される形で比べ、同期の計画が警告し、
/// DDL が注記することを、方言の宣言（<see cref="IdentifierLengthLimit.RepresentsSupplementaryCharacters"/>）と照合する
/// </summary>
/// <remarks>
/// 判定は同期のケーパビリティが指す識別子の上限（<see cref="SyncDialectCapabilities.IdentifierLengthLimit"/>）と、
/// DDL の生成器の方言の上限の宣言だけで行い、方言名で分岐しない。実 DB の往復は
/// 同期計画の期待の行 <c>realdb-description-supplementary-*</c>。
/// </remarks>
public class DescriptionStoredTextTests
{
    /// <summary>絵文字（サロゲート対）を 2 つ含む説明</summary>
    private const string Emoji = "a😀b😀c";

    /// <summary>MySQL が保存する形</summary>
    private const string EmojiStored = "a?b?c";

    /// <summary>保存する形の規則（MySQL の実測）: サロゲート対は 1 つの ?、対になっていないサロゲートは U+FFFD</summary>
    [Theory(DisplayName = "説明の BMP の外の文字: 保存できない方言の保存する形の規則")]
    [InlineData("abc", "abc")]
    [InlineData(Emoji, EmojiStored)]
    [InlineData("😀😀", "??")]
    [InlineData("?", "?")]
    public void StoredText_FollowsMySqlRule(string text, string expected)
    {
        IdentifierLengthLimits.MySql.StoredText(text).Should().Be(expected);
        IdentifierLengthLimits.SqlServer.StoredText(text).Should().Be(text);
        IdentifierLengthLimit.Unlimited.StoredText(text).Should().Be(text);
    }

    /// <summary>
    /// 対になっていないサロゲートは U+FFFD になる（xUnit の引数は UTF-8 を経て対になっていないサロゲートを壊すので Fact で書く）
    /// </summary>
    [Fact(DisplayName = "説明の BMP の外の文字: 対になっていないサロゲートは U+FFFD になる")]
    public void StoredText_LoneSurrogate_BecomesReplacementCharacter()
    {
        var limit = IdentifierLengthLimits.MySql;

        limit.StoredText("x\uD800y").Should().Be("x�y");
        limit.StoredText("x\uDC00").Should().Be("x�");
        limit.StoredText("\uDE00\uD83D").Should().Be("��");
        IdentifierLengthLimits.PostgreSql.StoredText("x\uD800y").Should().Be("x\uD800y");
    }
}
