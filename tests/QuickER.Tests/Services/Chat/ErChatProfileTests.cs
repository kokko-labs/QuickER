using FluentAssertions;
using QuickER.AI;

namespace QuickER.Tests.Services.Chat;

/// <summary>
/// 用途プロファイル化後も、既定（ER 図設計）プロファイルの内容が
/// 従来のハードコード（<see cref="ErDesignRules"/> / <see cref="ErDiagramToolDefinitions"/>）と一致することを検証する。
/// </summary>
public class ErChatProfileTests
{
    /// <summary>既定プロファイルのシステムプロンプトが従来の ER 設計プロンプトと一致することを検証する</summary>
    [Fact(DisplayName = "既定プロファイルのシステムプロンプトは従来の ER 設計プロンプトと一致する")]
    public void ErDesign_SystemPrompt_MatchesErDesignRules()
    {
        ErChatProfile
            .ErDesign.BuildSystemPrompt()
            .Should()
            .Be(ErDesignRules.BuildChatSystemPrompt());
    }

    /// <summary>既定プロファイルの Codex 指示が従来の developer instructions と一致することを検証する</summary>
    [Fact(DisplayName = "既定プロファイルの Codex 指示は従来の developer instructions と一致する")]
    public void ErDesign_CodexInstructions_MatchesErDesignRules()
    {
        ErChatProfile
            .ErDesign.BuildCodexDeveloperInstructions()
            .Should()
            .Be(ErDesignRules.BuildCodexDeveloperInstructions());
    }

    /// <summary>既定プロファイルのツール定義が従来の ER 図操作ツール定義と一致することを検証する</summary>
    [Fact(DisplayName = "既定プロファイルのツール定義は従来の ER 図操作ツール定義と一致する")]
    public void ErDesign_Tools_MatchesErDiagramToolDefinitions()
    {
        var expected = ErDiagramToolDefinitions.GetDefinitions();
        var actual = ErChatProfile.ErDesign.Tools;

        actual.Select(tool => tool.Name).Should().Equal(expected.Select(tool => tool.Name));
        actual
            .Select(tool => tool.Description)
            .Should()
            .Equal(expected.Select(tool => tool.Description));
    }

    /// <summary>既定プロファイルの MCP サーバー名が従来値と一致することを検証する</summary>
    [Fact(DisplayName = "既定プロファイルの MCP サーバー名は従来値と一致する")]
    public void ErDesign_McpServerName_MatchesErDiagramMcpServer()
    {
        ErChatProfile.ErDesign.McpServerName.Should().Be(ErDiagramMcpServer.ServerName);
    }

    /// <summary>ツール形式変換の一般化オーバーロードが従来の無引数版とバイト不変であることを検証する（OpenAI）</summary>
    [Fact(DisplayName = "ToOpenAiTools は既定ツールで従来と同一結果を返す")]
    public void ToOpenAiTools_WithDefaultTools_IsUnchanged()
    {
        var withDefinitions = ErDiagramToolDefinitions.ToOpenAiTools(
            ErDiagramToolDefinitions.GetDefinitions()
        );
        var parameterless = ErDiagramToolDefinitions.ToOpenAiTools();

        withDefinitions
            .Select(tool => tool.FunctionName)
            .Should()
            .Equal(parameterless.Select(tool => tool.FunctionName));
    }

    /// <summary>ツール形式変換の一般化オーバーロードが従来の無引数版とバイト不変であることを検証する（Anthropic）</summary>
    [Fact(DisplayName = "ToAnthropicTools は既定ツールで従来と同一結果を返す")]
    public void ToAnthropicTools_WithDefaultTools_IsUnchanged()
    {
        var withDefinitions = ErDiagramToolDefinitions.ToAnthropicTools(
            ErDiagramToolDefinitions.GetDefinitions()
        );
        var parameterless = ErDiagramToolDefinitions.ToAnthropicTools();

        withDefinitions
            .Select(tool => tool.Name)
            .Should()
            .Equal(parameterless.Select(tool => tool.Name));
    }

    /// <summary>モック生成プロファイルが save_mock_html ツールを 1 つだけ持つことを検証する</summary>
    [Fact(DisplayName = "モック生成プロファイルは save_mock_html ツールを 1 つ持つ")]
    public void MockDesign_HasSingleSaveMockHtmlTool()
    {
        ErChatProfile
            .MockDesign.Tools.Should()
            .ContainSingle()
            .Which.Name.Should()
            .Be(MockDesignTools.SaveMockHtmlToolName);
    }
}
