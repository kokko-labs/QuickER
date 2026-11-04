using AwesomeAssertions;
using Xunit;

namespace QuickER.Tests.GeneratedFixture;

/// <summary>
/// 生の値（CSV / Excel のセル）から値オブジェクトを起こす <c>TryCreateFrom</c> の、変換不能のメッセージの差し替えの検証。
/// </summary>
/// <remarks>
/// 生値変換の決まった形の表明は <see cref="ValueObjectScenarioTests"/> の表にある。ここに残すのは、
/// 静的な文言（<see cref="ValueObjectValidationMessages.InputNotConvertible"/>）を差し替えて戻すテストだけ
/// （表の行と並べると、差し替えている間に別の行が同じ文言を読み得るため）。
/// </remarks>
public sealed class ValueObjectInputConversionTests
{
    [Fact(DisplayName = "[生値変換] 変換できない値は差し替え可能なメッセージで報告される")]
    public void 変換不能はメッセージフックを通る()
    {
        var original = ValueObjectValidationMessages.InputNotConvertible;

        try
        {
            ValueObjectValidationMessages.InputNotConvertible = (displayName, raw) =>
                $"[{displayName}] cannot read '{raw}'.";

            CustomerIdValue
                .TryCreateFrom("abc", null, out var value, out var messages)
                .Should()
                .BeFalse();

            value.Should().BeNull();
            messages
                .Should()
                .ContainSingle()
                .Which.Should()
                .Be($"[{CustomerIdValue.DisplayName}] cannot read 'abc'.");
        }
        finally
        {
            ValueObjectValidationMessages.InputNotConvertible = original;
        }
    }
}
