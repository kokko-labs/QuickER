using ERDesigner.Services;
using FluentAssertions;

namespace ERDesigner.Tests.Services;

/// <summary>
/// <see cref="CodexConfigTomlReader"/> のパース動作を検証します。
/// </summary>
public class CodexConfigTomlReaderTests
{
    [Fact(DisplayName = "空文字や空行のみは無視")]
    public void Parse_EmptyLines_ReturnsEmpty()
    {
        var result = CodexConfigTomlReader.Parse([]);

        result.Model.Should().BeEmpty();
        result.ModelProvider.Should().BeEmpty();
        result.ProviderNames.Should().BeEmpty();
    }

    [Fact(DisplayName = "トップレベルの model と model_provider が取得できる")]
    public void Parse_TopLevelModelAndProvider()
    {
        var lines = new[] { "model = \"gemma4:31b-cloud\"", "model_provider = \"ollama-launch\"" };

        var result = CodexConfigTomlReader.Parse(lines);

        result.Model.Should().Be("gemma4:31b-cloud");
        result.ModelProvider.Should().Be("ollama-launch");
    }

    [Fact(DisplayName = "[model_providers.xxx] セクションからプロバイダー名を収集できる")]
    public void Parse_ModelProvidersSection_CollectsProviderNames()
    {
        var lines = new[]
        {
            "model = \"gemma4:31b-cloud\"",
            "model_provider = \"ollama-launch\"",
            "",
            "[model_providers.ollama-launch]",
            "name = \"Ollama\"",
            "base_url = \"http://127.0.0.1:11434/v1/\"",
        };

        var result = CodexConfigTomlReader.Parse(lines);

        result.ProviderNames.Should().Contain("ollama-launch");
    }

    [Fact(DisplayName = "プロバイダー別モデル候補辞書に config.toml のデフォルトモデルが登録される")]
    public void Parse_ProviderModels_ContainsDefaultModel()
    {
        var lines = new[] { "model = \"gemma4:31b-cloud\"", "model_provider = \"ollama-launch\"", "", "[model_providers.ollama-launch]", "name = \"Ollama\"" };

        var result = CodexConfigTomlReader.Parse(lines);

        result.ProviderModels.Should().ContainKey("ollama-launch");
        result.ProviderModels["ollama-launch"].Should().Contain("gemma4:31b-cloud");
    }

    [Fact(DisplayName = "コメント行と # 付き値が正しく無視・除去される")]
    public void Parse_CommentsAreIgnored()
    {
        var lines = new[] { "# コメント行", "#model = \"should-not-appear\"", "model = \"gpt-4o\" # インラインコメント" };

        var result = CodexConfigTomlReader.Parse(lines);

        result.Model.Should().Be("gpt-4o");
    }
}
