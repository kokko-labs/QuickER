using System;
using AwesomeAssertions;
using QuickER.Tests.GeneratedSyncModelFixture;
using QuickER.Tests.TestSupport;
using Xunit;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// 主キーのテキスト（#25）。日時はどの Kind で書いた値も、読み戻した行（Kind を持たない）と同じテキストになること、
/// Kind つきのテキストも Kind を落として読むこと、decimal は指数表記を使わずスケールに依らないテキストになることを、
/// 生成された記述子とキーのテキストの関数で直接確かめる。
/// </summary>
[Trait("Category", "Integration")]
[RuntimeCell(RuntimeFeatures.SyncSupport, RuntimeTarget.AdoSqlite)]
public sealed class SyncKeyTextRuntimeTests
{
    private const string Expected = "2026-07-03T08:00:00.0000009";

    private static readonly DateTime ClockTime = new DateTime(2026, 7, 3, 8, 0, 0).AddTicks(9);

    [Theory(
        DisplayName = "[Sync/キー] 日時の主キーは Kind を落としたテキストになり、どの Kind で書いても同じテキストになる"
    )]
    [InlineData(DateTimeKind.Utc)]
    [InlineData(DateTimeKind.Local)]
    [InlineData(DateTimeKind.Unspecified)]
    public void DateTimeKey_IsFormattedWithoutKind(DateTimeKind kind)
    {
        var key = DateTime.SpecifyKind(ClockTime, kind);

        GeneratedSyncTables.ModelEvent.FormatKey(key).Should().Be(Expected);
    }

    [Theory(
        DisplayName = "[Sync/キー] 以前の版が書いた Kind つきのテキストは、Kind を落とした形へ書き直して読む"
    )]
    [InlineData("2026-07-03T08:00:00.0000009Z")]
    [InlineData("2026-07-03T08:00:00.0000009+09:00")]
    [InlineData("2026-07-03T08:00:00.0000009-05:30")]
    [InlineData("2026-07-03T08:00:00.0000009")]
    public void LegacyText_IsNormalizedAndParsed(string keyText)
    {
        SyncKeyText.WithoutDateTimeKind(keyText).Should().Be(Expected);

        var parsed = GeneratedSyncTables.ModelEvent.ParseKey(keyText);
        parsed.Ticks.Should().Be(ClockTime.Ticks, "時計の時刻を保ったまま Kind だけを落とす");
        parsed.Kind.Should().Be(DateTimeKind.Unspecified);
    }

    [Theory(DisplayName = "[Sync/キー] 日時の往復書式でないテキストは書き直さない")]
    [InlineData("abc")]
    [InlineData("2026-07-03 08:00:00Z")]
    [InlineData("")]
    public void OtherText_IsLeftAsItIs(string keyText) =>
        SyncKeyText.WithoutDateTimeKind(keyText).Should().Be(keyText);

    [Theory(
        DisplayName = "[Sync/キー] decimal の主キーは指数表記を使わずに書き、以前の指数表記のテキストも読む"
    )]
    [InlineData("0.000001", "1E-06")]
    [InlineData("-0.000001", "-1E-06")]
    [InlineData("0.0000000000000000000000000001", "1E-28")]
    [InlineData("1.5", "1.5")]
    [InlineData("100", "100")]
    [InlineData("0", "0")]
    public void DecimalKey_IsWrittenWithoutExponent(string expected, string legacy)
    {
        var value = decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture);

        SyncKeyText.FormatDecimal(value).Should().Be(expected);
        SyncKeyText.ParseDecimal(expected).Should().Be(value);
        SyncKeyText.ParseDecimal(legacy).Should().Be(value);
    }

    [Fact(
        DisplayName = "[Sync/キー] decimal の書式は末尾のゼロを落とし、宣言のスケールに依らず同じテキストになる"
    )]
    public void DecimalKey_NormalizesScale()
    {
        SyncKeyText.FormatDecimal(1.50m).Should().Be("1.5");
        SyncKeyText.FormatDecimal(1.5000000000m).Should().Be("1.5");
        SyncKeyText.FormatDecimal(-0.0m).Should().Be("0");
        SyncKeyText.FormatDecimal(decimal.MaxValue).Should().Be("79228162514264337593543950335");
    }
}
