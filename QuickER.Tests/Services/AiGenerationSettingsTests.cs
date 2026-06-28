using QuickER.Services;
using FluentAssertions;

namespace QuickER.Tests.Services;

/// <summary><see cref="AiGenerationSettings"/> のエンドポイント解決と既定値を検証するテストクラス</summary>
public class AiGenerationSettingsTests
{
    /// <summary>OpenAI プロバイダーで OpenAI の既定エンドポイントを返すことを検証する</summary>
    [Fact(DisplayName = "OpenAI 既定エンドポイントを返す")]
    public void Default_OpenAi()
    {
        var s = new AiGenerationSettings { Provider = AiProvider.OpenAI };
        s.ResolveEndpoint().Should().Be("https://api.openai.com/v1");
    }

    /// <summary>Ollama プロバイダーでローカルの既定エンドポイントを返すことを検証する</summary>
    [Fact(DisplayName = "Ollama 既定エンドポイントを返す")]
    public void Default_Ollama()
    {
        var s = new AiGenerationSettings { Provider = AiProvider.Ollama };
        s.ResolveEndpoint().Should().Be("http://localhost:11434/v1");
    }

    /// <summary>EndpointOverride 指定時はプロバイダー既定より優先されることを検証する</summary>
    [Fact(DisplayName = "EndpointOverride が優先される")]
    public void Override_TakesPrecedence()
    {
        var s = new AiGenerationSettings
        {
            Provider = AiProvider.OpenAI,
            EndpointOverride = "https://example.com/v1",
        };

        s.ResolveEndpoint().Should().Be("https://example.com/v1");
    }

    /// <summary>命名規則の既定値がパスカルケースであることを検証する</summary>
    [Fact(DisplayName = "命名規則の既定値はパスカルケース")]
    public void Default_IdentifierNamingStyle_IsPascalCase()
    {
        var s = new AiGenerationSettings();

        s.IdentifierNamingStyle.Should().Be(AiIdentifierNamingStyle.PascalCase);
    }

    /// <summary>テーブル名の数形の既定値が単数形であることを検証する</summary>
    [Fact(DisplayName = "テーブル名の既定値は単数形")]
    public void Default_TableNameNumberStyle_IsSingular()
    {
        var s = new AiGenerationSettings();

        s.TableNameNumberStyle.Should().Be(AiTableNameNumberStyle.Singular);
    }

    /// <summary>生成モードの既定値が新規生成であることを検証する</summary>
    [Fact(DisplayName = "生成モードの既定値は新規生成")]
    public void Default_GenerationMode_IsCreateNew()
    {
        var s = new AiGenerationSettings();

        s.GenerationMode.Should().Be(AiGenerationMode.CreateNew);
    }
}
