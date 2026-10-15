using System.Globalization;
using AwesomeAssertions;
using QuickER.Platform;

namespace QuickER.Tests.Platform;

/// <summary><see cref="PathSegments"/> の判定（1 区間の名前・相対パス）と理由の文言を検証する</summary>
public sealed class PathSegmentsTests
{
    /// <summary>1 区間の名前の判定（断る理由・断らない名前）</summary>
    public static TheoryData<string, PathSegmentViolation?> Segments =>
        new()
        {
            { "", PathSegmentViolation.Empty },
            { ".", PathSegmentViolation.DotSegment },
            { "..", PathSegmentViolation.DotSegment },
            { "a/b", PathSegmentViolation.Separator },
            { "a\\b", PathSegmentViolation.Separator },
            { "a:b", PathSegmentViolation.InvalidCharacter },
            { "C:", PathSegmentViolation.InvalidCharacter },
            { "a*b", PathSegmentViolation.InvalidCharacter },
            { "a?b", PathSegmentViolation.InvalidCharacter },
            { "a<b", PathSegmentViolation.InvalidCharacter },
            { "a>b", PathSegmentViolation.InvalidCharacter },
            { "a|b", PathSegmentViolation.InvalidCharacter },
            { "a\"b", PathSegmentViolation.InvalidCharacter },
            { "a\u0001b", PathSegmentViolation.InvalidCharacter },
            { "a\u0009b", PathSegmentViolation.InvalidCharacter },
            { " a", PathSegmentViolation.LeadingWhitespace },
            { "\u3000a", PathSegmentViolation.LeadingWhitespace },
            { "a ", PathSegmentViolation.TrailingDotOrWhitespace },
            { "a.", PathSegmentViolation.TrailingDotOrWhitespace },
            { "GENERA~1", PathSegmentViolation.Tilde },
            { "a~b", PathSegmentViolation.Tilde },
            { "CON", PathSegmentViolation.ReservedDeviceName },
            { "con", PathSegmentViolation.ReservedDeviceName },
            { "NUL.txt", PathSegmentViolation.ReservedDeviceName },
            { "CON .txt", PathSegmentViolation.ReservedDeviceName },
            { "COM1", PathSegmentViolation.ReservedDeviceName },
            { "LPT9.md", PathSegmentViolation.ReservedDeviceName },
            { "CONIN$", PathSegmentViolation.ReservedDeviceName },
            { "conout$.txt", PathSegmentViolation.ReservedDeviceName },
            { "COM¹", PathSegmentViolation.ReservedDeviceName },
            { "com².txt", PathSegmentViolation.ReservedDeviceName },
            { "LPT³", PathSegmentViolation.ReservedDeviceName },
            { "LPT¹.md", PathSegmentViolation.ReservedDeviceName },
            { "COM⁴", null },
            { "CONIN", null },
            { new string('a', PathSegments.MaxLength + 1), PathSegmentViolation.TooLong },
            { new string('a', PathSegments.MaxLength), null },
            { "COM10", null },
            { "CONSOLE", null },
            { "a..b", null },
            { "MyApp.Domain", null },
            { "日本語", null },
            { ".gitignore", null },
        };

    /// <summary>相対パスの判定（断る理由・断らないパス）</summary>
    public static TheoryData<string, PathSegmentViolation?> RelativePaths =>
        new()
        {
            { "", PathSegmentViolation.Empty },
            { "/a", PathSegmentViolation.Rooted },
            { "\\a", PathSegmentViolation.Rooted },
            { "C:\\a", PathSegmentViolation.Rooted },
            { "C:a", PathSegmentViolation.Rooted },
            { "a//b", PathSegmentViolation.Empty },
            { "a/./b", PathSegmentViolation.DotSegment },
            { "a/../b", PathSegmentViolation.DotSegment },
            { "./a", PathSegmentViolation.DotSegment },
            { "a/ b", PathSegmentViolation.LeadingWhitespace },
            { "a /b", PathSegmentViolation.TrailingDotOrWhitespace },
            { "a./b", PathSegmentViolation.TrailingDotOrWhitespace },
            { "a/CON/b", PathSegmentViolation.ReservedDeviceName },
            { "a/x~1", PathSegmentViolation.Tilde },
            { "MyApp.Domain/Generated", null },
            { "MyApp.Domain\\Generated", null },
            { "Domain/", null },
            { "a\\b\\", null },
        };

    [Theory(DisplayName = "CheckSegment: 1 区間の名前として断る理由を返す")]
    [MemberData(nameof(Segments))]
    public void CheckSegment_ReturnsViolation(string segment, PathSegmentViolation? expected)
    {
        PathSegments.CheckSegment(segment).Should().Be(expected);
    }

    [Theory(DisplayName = "CheckRelativePath: 出力先の中に収まる相対パスとして断る理由を返す")]
    [MemberData(nameof(RelativePaths))]
    public void CheckRelativePath_ReturnsViolation(string path, PathSegmentViolation? expected)
    {
        PathSegments.CheckRelativePath(path).Should().Be(expected);
    }

    [Fact(DisplayName = "Describe: すべての理由に英語と日本語の文言があり、両者は違う")]
    public void Describe_EveryViolation_HasEnglishAndJapaneseText()
    {
        foreach (var violation in Enum.GetValues<PathSegmentViolation>())
        {
            var english = PathSegments.Describe(violation, CultureInfo.InvariantCulture);
            var japanese = PathSegments.Describe(violation, new CultureInfo("ja-JP"));

            english.Should().NotBeNullOrWhiteSpace(violation.ToString());
            japanese.Should().NotBeNullOrWhiteSpace(violation.ToString());
            japanese.Should().NotBe(english, violation.ToString());
        }
    }

    [Fact(DisplayName = "Describe: 未定義の理由は ArgumentOutOfRangeException")]
    public void Describe_UndefinedViolation_Throws()
    {
        var act = () =>
            PathSegments.Describe((PathSegmentViolation)99, CultureInfo.InvariantCulture);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
