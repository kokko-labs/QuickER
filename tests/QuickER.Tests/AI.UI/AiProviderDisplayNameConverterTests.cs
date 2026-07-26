using System.Globalization;
using FluentAssertions;
using QuickER.AI;
using QuickER.AI.UI;

namespace QuickER.Tests.AI.UI;

/// <summary>
/// <see cref="AiProviderDisplayNameConverter"/> の表示名変換を検証するテストクラス。
/// ComboBox が enum 名をそのまま出すと内部表記（<c>LocalLlm</c>）が UI へ漏れるため、
/// 3 プロバイダーすべてで意図した表示になることを固定する。
/// </summary>
public class AiProviderDisplayNameConverterTests
{
    private static object? Convert(object? value) =>
        new AiProviderDisplayNameConverter().Convert(
            value,
            typeof(string),
            null,
            CultureInfo.InvariantCulture
        );

    [Theory(DisplayName = "各プロバイダーが意図した表示名になる")]
    [InlineData(AiProvider.OpenAI, "OpenAI")]
    [InlineData(AiProvider.Claude, "Claude")]
    [InlineData(AiProvider.LocalLlm, "Local LLM")]
    public void EachProvider_HasExpectedDisplayName(AiProvider provider, string expected)
    {
        Convert(provider).Should().Be(expected);
        AiProviderDisplayNameConverter.ToDisplayName(provider).Should().Be(expected);
    }

    /// <summary>enum 名がそのまま出ていないこと（変換の存在意義）を明示的に押さえる</summary>
    [Fact(DisplayName = "LocalLlm の enum 名は UI へ出さない")]
    public void LocalLlm_DoesNotLeakEnumName() =>
        Convert(AiProvider.LocalLlm).Should().NotBe(nameof(AiProvider.LocalLlm));

    [Fact(DisplayName = "enum 以外（null 等）は素通しする")]
    public void NonProvider_PassesThrough() => Convert(null).Should().BeNull();

    [Fact(DisplayName = "逆変換は非対応")]
    public void ConvertBack_IsNotSupported()
    {
        var act = () =>
            new AiProviderDisplayNameConverter().ConvertBack(
                "Local LLM",
                typeof(AiProvider),
                null,
                CultureInfo.InvariantCulture
            );

        act.Should().Throw<NotSupportedException>();
    }
}
