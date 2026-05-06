using ERDesigner.Services;
using FluentAssertions;

namespace ERDesigner.Tests.Services;

/// <summary>
/// <see cref="AiGenerationSettings.ResolveEndpoint"/> のテスト。
/// </summary>
public class AiGenerationSettingsTests
{
    [Fact(DisplayName = "OpenAI 既定エンドポイントを返す")]
    public void Default_OpenAi()
    {
        var s = new AiGenerationSettings { Provider = AiProvider.OpenAi };
        s.ResolveEndpoint().Should().Be("https://api.openai.com/v1");
    }

    [Fact(DisplayName = "Ollama 既定エンドポイントを返す")]
    public void Default_Ollama()
    {
        var s = new AiGenerationSettings { Provider = AiProvider.Ollama };
        s.ResolveEndpoint().Should().Be("http://localhost:11434/v1");
    }

    [Fact(DisplayName = "EndpointOverride が優先される")]
    public void Override_TakesPrecedence()
    {
        var s = new AiGenerationSettings { Provider = AiProvider.OpenAi, EndpointOverride = "https://example.com/v1" };

        s.ResolveEndpoint().Should().Be("https://example.com/v1");
    }

    [Fact(DisplayName = "命名規則の既定値はパスカルケース")]
    public void Default_IdentifierNamingStyle_IsPascalCase()
    {
        var s = new AiGenerationSettings();

        s.IdentifierNamingStyle.Should().Be(AiIdentifierNamingStyle.PascalCase);
    }

    [Fact(DisplayName = "テーブル名の既定値は単数形")]
    public void Default_TableNameNumberStyle_IsSingular()
    {
        var s = new AiGenerationSettings();

        s.TableNameNumberStyle.Should().Be(AiTableNameNumberStyle.Singular);
    }
}
