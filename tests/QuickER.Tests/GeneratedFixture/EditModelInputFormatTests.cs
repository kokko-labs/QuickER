using System;
using System.Globalization;
using AwesomeAssertions;
using Xunit;

namespace QuickER.Tests.GeneratedFixture;

/// <summary>
/// 固定 infra の書式ヘルパー <see cref="EditModelInputFormat"/> を、カルチャ×型×秒未満の有無の行列で検証する。
/// </summary>
/// <remarks>
/// <para>
/// 守る性質は 2 つ。(1) <b>秒未満がゼロの値の表示文字列は従来と 1 文字も変わらない</b>（＝その型自身の
/// <c>ToString()</c> と一致する）。既存画面の見え方を変えないための不変条件。(2) <b>秒未満を持つ値の表示文字列は、
/// バインド setter と同じパース（現在カルチャ・<c>IParsable</c> 経由＝<c>TryParse(text, null, out _)</c>）で
/// tick まで完全に往復する</b>。往復しなければ、同じ行の別の列を編集しただけで切り捨てた値が確定値へ戻る。
/// </para>
/// <para>
/// カルチャは書式の癖が割れる 4 つ（ja-JP＝24 時制・en-US＝AM/PM が秒の後ろに付く・de-DE＝小数点がカンマ・
/// fr-FR）とインバリアントを回す。小数部の区切りは不変カルチャの '.' 固定なので、de-DE のように小数点が
/// カンマのカルチャでもパーサーが受けることをここで確かめている。
/// </para>
/// </remarks>
public sealed class EditModelInputFormatTests
{
    /// <summary>行列で回すカルチャ（既定書式の癖が割れるものを選ぶ）</summary>
    public static TheoryData<string> Cultures =>
        new() { string.Empty, "ja-JP", "en-US", "de-DE", "fr-FR" };

    /// <summary>指定カルチャを現在カルチャにして処理を走らせる（インバリアントは空文字で指す）</summary>
    private static void InCulture(string cultureName, Action action)
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = string.IsNullOrEmpty(cultureName)
            ? CultureInfo.InvariantCulture
            : CultureInfo.GetCultureInfo(cultureName);

        try
        {
            action();
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    private static DateTime WholeSecond() => new(2026, 8, 10, 14, 30, 15, DateTimeKind.Unspecified);

    private static DateTime WithFraction() => WholeSecond().AddTicks(1_234_567);

    // ===== (1) 秒未満ゼロ: 表示は従来（その型の ToString()）と完全一致 =====

    [Theory(DisplayName = "秒未満がゼロなら表示文字列は ToString() と 1 文字も変わらない")]
    [MemberData(nameof(Cultures))]
    public void 秒未満ゼロは既定書式そのまま(string culture) =>
        InCulture(
            culture,
            () =>
            {
                var dateTime = WholeSecond();
                var offset = new DateTimeOffset(dateTime, TimeSpan.FromHours(9));
                var span = new TimeSpan(0, 14, 30, 15);
                var time = new TimeOnly(14, 30, 15);

                EditModelInputFormat.Format(dateTime).Should().Be(dateTime.ToString());
                EditModelInputFormat.Format(offset).Should().Be(offset.ToString());
                EditModelInputFormat.Format(span).Should().Be(span.ToString());
                EditModelInputFormat.Format(time).Should().Be(time.ToString());
            }
        );

    [Theory(DisplayName = "null は空文字（全型）")]
    [MemberData(nameof(Cultures))]
    public void nullは空文字(string culture) =>
        InCulture(
            culture,
            () =>
            {
                EditModelInputFormat.Format((DateTime?)null).Should().BeEmpty();
                EditModelInputFormat.Format((DateTimeOffset?)null).Should().BeEmpty();
                EditModelInputFormat.Format((TimeSpan?)null).Should().BeEmpty();
                EditModelInputFormat.Format((TimeOnly?)null).Should().BeEmpty();
            }
        );

    // ===== (2) 秒未満あり: バインド setter と同じパースで tick まで往復 =====

    [Theory(DisplayName = "秒未満を持つ DateTime は表示→パースで tick 一致（往復）")]
    [MemberData(nameof(Cultures))]
    public void DateTimeは往復する(string culture) =>
        InCulture(
            culture,
            () =>
            {
                var value = WithFraction();
                var text = EditModelInputFormat.Format(value);

                text.Should().Contain(".1234567");
                DateTime.TryParse(text, null, out var parsed).Should().BeTrue(text);
                parsed.Ticks.Should().Be(value.Ticks, text);
            }
        );

    [Theory(DisplayName = "秒未満を持つ DateTimeOffset は表示→パースで tick とオフセットごと往復")]
    [MemberData(nameof(Cultures))]
    public void DateTimeOffsetは往復する(string culture) =>
        InCulture(
            culture,
            () =>
            {
                var value = new DateTimeOffset(WithFraction(), TimeSpan.FromHours(9));
                var text = EditModelInputFormat.Format(value);

                text.Should().Contain(".1234567");
                DateTimeOffset.TryParse(text, null, out var parsed).Should().BeTrue(text);
                parsed.Should().Be(value, text);
                parsed.Offset.Should().Be(value.Offset, text);
            }
        );

    [Theory(
        DisplayName = "秒未満を持つ TimeSpan は表示→パースで tick 一致（既定書式が端数を含む）"
    )]
    [MemberData(nameof(Cultures))]
    public void TimeSpanは往復する(string culture) =>
        InCulture(
            culture,
            () =>
            {
                var value = new TimeSpan(0, 14, 30, 15).Add(TimeSpan.FromTicks(1_234_567));
                var text = EditModelInputFormat.Format(value);

                text.Should().Contain(".1234567");
                TimeSpan.TryParse(text, null, out var parsed).Should().BeTrue(text);
                parsed.Ticks.Should().Be(value.Ticks, text);
            }
        );

    [Theory(
        DisplayName = "秒未満を持つ TimeOnly は表示→パースで tick 一致（秒まで出る書式へ切り替わる）"
    )]
    [MemberData(nameof(Cultures))]
    public void TimeOnlyは往復する(string culture) =>
        InCulture(
            culture,
            () =>
            {
                var value = new TimeOnly(14, 30, 15).Add(TimeSpan.FromTicks(1_234_567));
                var text = EditModelInputFormat.Format(value);

                text.Should().Contain(".1234567");
                TimeOnly.TryParse(text, null, out var parsed).Should().BeTrue(text);
                parsed.Ticks.Should().Be(value.Ticks, text);
            }
        );

    // ===== 端数の綴り方（末尾ゼロなし・'.' 固定） =====

    [Theory(DisplayName = "端数は末尾ゼロを落とし、カルチャの小数点に依らず '.' で綴る")]
    [MemberData(nameof(Cultures))]
    public void 端数の綴り方(string culture) =>
        InCulture(
            culture,
            () =>
            {
                // 100 ミリ秒ちょうど（末尾ゼロを落とせば ".1"）
                var value = WholeSecond().AddTicks(TimeSpan.TicksPerMillisecond * 100);
                var text = EditModelInputFormat.Format(value);

                text.Should().Contain(".1");
                text.Should().NotContain(".1000000");
                DateTime.TryParse(text, null, out var parsed).Should().BeTrue(text);
                parsed.Ticks.Should().Be(value.Ticks, text);
            }
        );
}
