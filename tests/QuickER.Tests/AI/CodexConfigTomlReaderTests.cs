using AwesomeAssertions;
using QuickER.AI;
using QuickER.Services;

namespace QuickER.Tests.AI;

/// <summary><see cref="CodexConfigTomlReader"/> による config.toml の簡易パースをテストするクラス</summary>
public class CodexConfigTomlReaderTests
{
    /// <summary>入力行が無い場合に全プロパティが空で返ることを検証する</summary>
    [Fact(DisplayName = "空文字や空行のみは無視")]
    public void Parse_EmptyLines_ReturnsEmpty()
    {
        var result = CodexConfigTomlReader.Parse([]);

        result.Model.Should().BeEmpty();
        result.ModelProvider.Should().BeEmpty();
        result.ProviderNames.Should().BeEmpty();
    }

    /// <summary>トップレベルの model / model_provider キーから値が引用符なしで取得できることを検証する</summary>
    [Fact(DisplayName = "トップレベルの model と model_provider が取得できる")]
    public void Parse_TopLevelModelAndProvider()
    {
        var lines = new[] { "model = \"gemma4:31b-cloud\"", "model_provider = \"ollama-launch\"" };

        var result = CodexConfigTomlReader.Parse(lines);

        result.Model.Should().Be("gemma4:31b-cloud");
        result.ModelProvider.Should().Be("ollama-launch");
    }

    /// <summary>[model_providers.xxx] セクションヘッダーからプロバイダー名が収集されることを検証する</summary>
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

    /// <summary>行頭 # のコメント行が無視され、値後方のインラインコメントが除去されることを検証する</summary>
    [Fact(DisplayName = "コメント行と # 付き値が正しく無視・除去される")]
    public void Parse_CommentsAreIgnored()
    {
        var lines = new[]
        {
            "# コメント行",
            "#model = \"should-not-appear\"",
            "model = \"gpt-4o\" # インラインコメント",
        };

        var result = CodexConfigTomlReader.Parse(lines);

        result.Model.Should().Be("gpt-4o");
    }
}
