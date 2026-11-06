using System.Globalization;
using AwesomeAssertions;
using QuickER.Platform;

namespace QuickER.Tests.Platform;

/// <summary><see cref="PathSegments"/> の判定（1 区間の名前・相対パス）と理由の文言を検証する</summary>
public sealed class PathSegmentsTests
{
    /// <summary>1 区間の名前の判定（断る理由・断らない名前）</summary>
    public static TheoryData<string, PathSegmentViolation?> Segments =>
        Rows(PathSegmentForms.SegmentChecks);

    /// <summary>相対パスの判定（断る理由・断らないパス）</summary>
    public static TheoryData<string, PathSegmentViolation?> RelativePaths =>
        Rows(PathSegmentForms.RelativeChecks);

    /// <summary>形の一覧（<see cref="PathSegmentForms"/>）を Theory のデータにする</summary>
    private static TheoryData<string, PathSegmentViolation?> Rows(
        IEnumerable<PathSegmentForms.PathForm> forms
    )
    {
        var data = new TheoryData<string, PathSegmentViolation?>();

        foreach (var form in forms)
        {
            data.Add(form.Form, form.Violation);
        }

        return data;
    }

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
