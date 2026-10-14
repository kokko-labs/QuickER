using System;
using AwesomeAssertions;
using QuickER.Tests.GeneratedSyncModelFixture;
using Xunit;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// 日時の主キーのジャーナルのテキスト（#25）。どの Kind で書いた値も、読み戻した行（Kind を持たない）と同じテキストに
/// なること、以前の版が Kind つきで書いたテキストを同じ形へ書き直して読むことを確かめる。
/// </summary>
/// <remarks>
/// ランの初めの書き直し（<c>SyncJournal.NormalizeKeyTextsAsync</c>）がジャーナルに残った Kind つきのテキストも直すので、
/// 整形の誤りは同期の結果からは見えないことがある（ランの最中に記録したテキストだけが残る）。そのため整形そのものを
/// 生成された記述子で直接確かめる。
/// </remarks>
[Trait("Category", "Integration")]
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
        GeneratedSyncTables.ModelEvent.NormalizeKeyText!(keyText).Should().Be(Expected);

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

    [Fact(DisplayName = "[Sync/キー] 主キーが日時でない表は書き直しを持たない")]
    public void NonDateTimeKey_HasNoNormalizer()
    {
        GeneratedSyncTables.ModelItem.NormalizeKeyText.Should().BeNull();
        GeneratedSyncTables.ModelLabel.NormalizeKeyText.Should().BeNull();
    }
}
