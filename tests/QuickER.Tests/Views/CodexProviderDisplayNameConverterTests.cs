using System.Globalization;
using FluentAssertions;
using QuickER.AI.UI;

namespace QuickER.Tests.Views;

/// <summary>
/// <see cref="CodexProviderDisplayNameConverter"/> の表示名変換
/// （内部値 "openai" のみ "OpenAI" 表示・他プロバイダー ID は素通し）を検証するテストクラス。
/// </summary>
public class CodexProviderDisplayNameConverterTests
{
    private static object? Convert(object? value) =>
        new CodexProviderDisplayNameConverter().Convert(
            value,
            typeof(string),
            null,
            CultureInfo.InvariantCulture
        );

    [Fact(DisplayName = "openai は OpenAI と表示する")]
    public void OpenAi_IsDisplayedAsOpenAI() => Convert("openai").Should().Be("OpenAI");

    [Fact(DisplayName = "大文字小文字・前後空白の違いも OpenAI へ寄せる")]
    public void OpenAi_CaseAndWhitespaceInsensitive() =>
        Convert("  OPENAI  ").Should().Be("OpenAI");

    [Fact(DisplayName = "config.toml 由来のプロバイダー ID は素通しする")]
    public void OtherProvider_PassesThrough() =>
        Convert("ollama-launch").Should().Be("ollama-launch");

    [Fact(DisplayName = "文字列以外（null 等）は素通しする")]
    public void NonString_PassesThrough() => Convert(null).Should().BeNull();
}
