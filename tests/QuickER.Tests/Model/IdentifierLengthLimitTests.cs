using System.Text;
using AwesomeAssertions;
using QuickER.Model;

namespace QuickER.Tests.Model;

/// <summary>
/// 識別子の長さの上限（<see cref="IdentifierLengthLimit"/> / <see cref="IdentifierLengthLimits"/>）の測り方を検証するテストクラス
/// </summary>
/// <remarks>
/// 上限の値と単位が実 DB と一致することは Integration の <c>IdentifierLengthLimitIntegrationTests</c> が確かめる。
/// ここでは単位ごとの数え方と、上限ちょうど・1 つ超えの境界を確かめる。
/// </remarks>
public class IdentifierLengthLimitTests
{
    private const string Kanji = "表";
    private const string AUmlaut = "Ä";
    private const string Emoji = "😀";

    private static string Repeat(string text, int count) =>
        string.Concat(Enumerable.Repeat(text, count));

    [Theory(DisplayName = "Measure: 単位ごとの数え方（UTF-16・UTF-8・文字）")]
    [InlineData("abc", 3, 3, 3)]
    [InlineData(AUmlaut, 1, 2, 1)]
    [InlineData(Kanji, 1, 3, 1)]
    [InlineData(Emoji, 2, 4, 1)]
    [InlineData("a" + Emoji + Kanji, 4, 8, 3)]
    public void Measure_CountsInEachUnit(string name, int utf16, int utf8, int characters)
    {
        new IdentifierLengthLimit(IdentifierLengthUnit.Utf16CodeUnits, 1)
            .Measure(name)
            .Should()
            .Be(utf16);
        new IdentifierLengthLimit(IdentifierLengthUnit.Utf8Bytes, 1)
            .Measure(name)
            .Should()
            .Be(utf8);
        new IdentifierLengthLimit(IdentifierLengthUnit.Characters, 1)
            .Measure(name)
            .Should()
            .Be(characters);
    }

    /// <summary>各方言の上限ちょうどは通り、1 つ超えると通らない</summary>
    public static TheoryData<string, string, bool> Boundaries =>
        new()
        {
            // SQL Server: 128 UTF-16 コード単位（サロゲート対は 2）
            { "sqlserver", Repeat("a", 128), true },
            { "sqlserver", Repeat("a", 129), false },
            { "sqlserver", Repeat(Kanji, 128), true },
            { "sqlserver", Repeat("a", 126) + Emoji, true },
            { "sqlserver", Repeat("a", 127) + Emoji, false },
            // PostgreSQL: 63 バイト（漢字は 3 バイト＝21 文字まで）
            { "postgresql", Repeat("a", 63), true },
            { "postgresql", Repeat("a", 64), false },
            { "postgresql", Repeat(Kanji, 21), true },
            { "postgresql", Repeat(Kanji, 22), false },
            { "postgresql", Repeat("a", 59) + Emoji, true },
            { "postgresql", Repeat("a", 60) + Emoji, false },
            // MySQL: 64 文字（サロゲート対は 1 文字）
            { "mysql", Repeat("a", 64), true },
            { "mysql", Repeat("a", 65), false },
            { "mysql", Repeat(Kanji, 64), true },
            { "mysql", Repeat(Kanji, 65), false },
            { "mysql", Repeat(Emoji, 64), true },
            // Oracle: 128 バイト（漢字は 42 文字＝126 バイトまで）
            { "oracle", Repeat("a", 128), true },
            { "oracle", Repeat("a", 129), false },
            { "oracle", Repeat(Kanji, 42), true },
            { "oracle", Repeat(Kanji, 43), false },
            // SQLite: 上限なし
            { "sqlite", Repeat("a", 10_000), true },
        };

    [Theory(DisplayName = "Fits: 各方言の上限ちょうどは通り、1 つ超えると通らない")]
    [MemberData(nameof(Boundaries))]
    public void Fits_Boundary(string dbms, string name, bool expected)
    {
        IdentifierLengthLimits.ForDbms(dbms).Fits(name).Should().Be(expected);
    }

    [Theory(
        DisplayName = "FitsTableName: MySQL の表の名前は、ファイル名へ符号化した長さが 251 バイト以内"
    )]
    [InlineData(50, Kanji, true)]
    [InlineData(51, Kanji, false)]
    [InlineData(64, AUmlaut, true)]
    [InlineData(50, "#", true)]
    [InlineData(51, "#", false)]
    [InlineData(64, "Ａ", true)]
    [InlineData(50, Emoji, true)]
    [InlineData(51, Emoji, false)]
    public void FitsTableName_MySqlFileName(int count, string unit, bool expected)
    {
        var name = Repeat(unit, count);

        IdentifierLengthLimits.MySql.FitsTableName(name).Should().Be(expected);
    }

    [Theory(
        DisplayName = "FitsTableName: MySQL の表の名前の符号化は 1・3・5 バイトの混在でも 251 が境目"
    )]
    [InlineData("a", 50, "#", true)]
    [InlineData("aa", 50, "#", false)]
    [InlineData(AUmlaut + AUmlaut, 49, Kanji, true)]
    [InlineData(AUmlaut + AUmlaut + AUmlaut, 49, Kanji, false)]
    public void FitsTableName_MySqlMixedEncoding(string head, int count, string unit, bool expected)
    {
        IdentifierLengthLimits
            .MySql.FitsTableName(head + Repeat(unit, count))
            .Should()
            .Be(expected);
    }

    [Fact(
        DisplayName = "FitsTableName: 列名・制約名にはファイル名の上限を掛けない（51 文字の漢字は列名として通る）"
    )]
    public void Fits_DoesNotApplyFileNameLimit()
    {
        IdentifierLengthLimits.MySql.Fits(Repeat(Kanji, 51)).Should().BeTrue();
    }

    [Theory(DisplayName = "FitsTableName: スキーマ修飾は最初の . で分けて部分ごとに測る")]
    [InlineData("postgresql")]
    [InlineData("sqlserver")]
    [InlineData("oracle")]
    [InlineData("mysql")]
    public void FitsTableName_MeasuresEachPart(string dbms)
    {
        var limit = IdentifierLengthLimits.ForDbms(dbms);
        var part = Repeat("a", limit.Max);
        var over = Repeat("a", limit.Max + 1);

        limit.FitsTableName(part + "." + part).Should().BeTrue("部分ごとなら上限ちょうど");
        limit.FitsTableName(over + "." + part).Should().BeFalse("スキーマの部分が超える");
        limit.FitsTableName(part + "." + over).Should().BeFalse("表の部分が超える");
        limit
            .FitsTableName(part + "." + part + ".x")
            .Should()
            .BeFalse("2 つめ以降の . は表の名前の一部");
    }

    [Fact(DisplayName = "FitsTableName: MySQL のスキーマの部分にはファイル名の上限を掛けない")]
    public void FitsTableName_MySqlSchemaPartHasNoFileNameLimit()
    {
        IdentifierLengthLimits.MySql.FitsTableName(Repeat(Kanji, 60) + ".t").Should().BeTrue();
        IdentifierLengthLimits.MySql.FitsTableName("s." + Repeat(Kanji, 51)).Should().BeFalse();
    }

    [Theory(
        DisplayName = "ForDbms: 方言名は大文字小文字と前後の空白を無視し、知らない方言は上限なし"
    )]
    [InlineData("SqlServer", IdentifierLengthUnit.Utf16CodeUnits, 128)]
    [InlineData(" postgresql ", IdentifierLengthUnit.Utf8Bytes, 63)]
    [InlineData("MYSQL", IdentifierLengthUnit.Characters, 64)]
    [InlineData("Oracle", IdentifierLengthUnit.Utf8Bytes, 128)]
    [InlineData("sqlite", IdentifierLengthUnit.Unlimited, 0)]
    [InlineData("db2", IdentifierLengthUnit.Unlimited, 0)]
    [InlineData(null, IdentifierLengthUnit.Unlimited, 0)]
    public void ForDbms_ResolvesByName(string? dbms, IdentifierLengthUnit unit, int max)
    {
        var limit = IdentifierLengthLimits.ForDbms(dbms);

        limit.Unit.Should().Be(unit);
        limit.Max.Should().Be(max);
    }

    [Theory(DisplayName = "Describe: 上限を英語で表す")]
    [InlineData("sqlserver", "128 UTF-16 code units")]
    [InlineData("postgresql", "63 UTF-8 bytes")]
    [InlineData("mysql", "64 characters (table names also 251 bytes as a file name)")]
    [InlineData("oracle", "128 UTF-8 bytes")]
    [InlineData("sqlite", "no limit")]
    public void Describe_IsEnglish(string dbms, string expected)
    {
        IdentifierLengthLimits.ForDbms(dbms).Describe().Should().Be(expected);
    }

    [Theory(DisplayName = "Fit: 上限内の名前はそのまま返す")]
    [InlineData("sqlserver")]
    [InlineData("postgresql")]
    [InlineData("mysql")]
    [InlineData("oracle")]
    [InlineData("sqlite")]
    public void Fit_WithinLimit_ReturnsSameName(string dbms)
    {
        var limit = IdentifierLengthLimits.ForDbms(dbms);
        var name = Repeat("a", limit.IsUnlimited ? 500 : limit.Max);

        limit.Fit(name).Should().BeSameAs(name);
    }

    [Theory(
        DisplayName = "Fit: 上限を超える名前は、先頭の部分と _ と縮める前の名前のハッシュ 8 桁で上限ちょうどに収める"
    )]
    [InlineData("sqlserver", "a")]
    [InlineData("postgresql", "a")]
    [InlineData("mysql", "a")]
    [InlineData("oracle", "a")]
    [InlineData("postgresql", Kanji)]
    [InlineData("oracle", Kanji)]
    [InlineData("sqlserver", Emoji)]
    [InlineData("postgresql", Emoji)]
    public void Fit_OverLimit_ShortensWithHash(string dbms, string unit)
    {
        var limit = IdentifierLengthLimits.ForDbms(dbms);
        var name = "PK_" + Repeat(unit, limit.Max);

        var fitted = limit.Fit(name);

        limit.Fits(fitted).Should().BeTrue();
        fitted.Should().MatchRegex("_[0-9a-f]{8}$");
        name.Should().StartWith(fitted[..^9], "先頭の部分は元の名前の先頭");
        fitted.EnumerateRunes().Should().NotContain(Rune.ReplacementChar, "文字の途中で切らない");
        limit.Fit(name).Should().Be(fitted, "決定的");
        (limit.Max - limit.Measure(fitted))
            .Should()
            .BeLessThan(4, "上限まで使う（1 文字分の端数だけ余り得る）");
    }

    [Fact(DisplayName = "Fit: 上限の後ろでだけ違う 2 つの名前は、縮めても別の名前になる")]
    public void Fit_KeepsNamesDistinct()
    {
        var limit = IdentifierLengthLimits.PostgreSql;
        var first = "FK_" + Repeat("b", 70) + "_1";
        var second = "FK_" + Repeat("b", 70) + "_2";

        limit.Fit(first).Should().NotBe(limit.Fit(second));
    }

    [Theory(
        DisplayName = "Truncate: PostgreSQL と同じく上限以内の最長の先頭部分（文字の途中では切らない）"
    )]
    [InlineData("a", 70, 63)]
    [InlineData(Kanji, 22, 21)]
    [InlineData(Emoji, 16, 15)]
    public void Truncate_CutsAtCharacterBoundary(string unit, int count, int expectedUnits)
    {
        IdentifierLengthLimits
            .PostgreSql.Truncate(Repeat(unit, count))
            .Should()
            .Be(Repeat(unit, expectedUnits));
    }

    [Fact(DisplayName = "Truncate: 上限内の名前・上限なしはそのまま")]
    public void Truncate_WithinLimit_ReturnsSame()
    {
        IdentifierLengthLimits.PostgreSql.Truncate("abc").Should().Be("abc");
        IdentifierLengthLimit.Unlimited.Truncate(Repeat("a", 500)).Should().Be(Repeat("a", 500));
    }
}
