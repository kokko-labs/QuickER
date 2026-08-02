using AwesomeAssertions;
using QuickER.AI;
using QuickER.AI.Chat;
using QuickER.AI.Mock;
using QuickER.Mcp;

namespace QuickER.Tests.AI;

/// <summary>
/// 用途プロファイル化後も、既定（ER 図設計）プロファイルの内容が
/// 従来のハードコード（<see cref="ErDesignRules"/> / <see cref="ErDiagramToolCatalog"/>）と一致することを検証する。
/// </summary>
public class ErChatProfileTests
{
    /// <summary>既定プロファイルのシステムプロンプトが従来の ER 設計プロンプトと一致することを検証する</summary>
    [Fact(DisplayName = "既定プロファイルのシステムプロンプトは従来の ER 設計プロンプトと一致する")]
    public void ErDesign_SystemPrompt_MatchesErDesignRules()
    {
        ErDesignProfile
            .ErDesign.BuildSystemPrompt()
            .Should()
            .Be(ErDesignRules.BuildChatSystemPrompt());
    }

    /// <summary>既定プロファイルの Codex 指示が従来の developer instructions と一致することを検証する</summary>
    [Fact(DisplayName = "既定プロファイルの Codex 指示は従来の developer instructions と一致する")]
    public void ErDesign_CodexInstructions_MatchesErDesignRules()
    {
        ErDesignProfile
            .ErDesign.BuildCodexDeveloperInstructions()
            .Should()
            .Be(ErDesignRules.BuildCodexDeveloperInstructions());
    }

    /// <summary>既定プロファイルのツール定義が従来の ER 図操作ツール定義と一致することを検証する</summary>
    [Fact(DisplayName = "既定プロファイルのツール定義は従来の ER 図操作ツール定義と一致する")]
    public void ErDesign_Tools_MatchesErDiagramToolDefinitions()
    {
        var expected = ErDiagramToolCatalog.GetDefinitions();
        var actual = ErDesignProfile.ErDesign.Tools;

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
        ErDesignProfile.ErDesign.McpServerName.Should().Be(ErDiagramMcpServer.ServerName);
    }

    /// <summary>既定プロファイルのツールを OpenAI 形式へ変換すると、全ツールが名前付きで生成されることを検証する</summary>
    [Fact(DisplayName = "ToOpenAiTools は既定プロファイルの全ツールを変換する")]
    public void ToOpenAiTools_WithProfileTools_ConvertsAll()
    {
        var definitions = ErDesignProfile.ErDesign.Tools;
        var tools = ChatToolConverter.ToOpenAiTools(definitions);

        tools
            .Select(tool => tool.FunctionName)
            .Should()
            .Equal(definitions.Select(definition => definition.Name));
    }

    /// <summary>既定プロファイルのツールを Anthropic 形式へ変換すると、全ツールが名前付きで生成されることを検証する</summary>
    [Fact(DisplayName = "ToAnthropicTools は既定プロファイルの全ツールを変換する")]
    public void ToAnthropicTools_WithProfileTools_ConvertsAll()
    {
        var definitions = ErDesignProfile.ErDesign.Tools;
        var tools = ChatToolConverter.ToAnthropicTools(definitions);

        tools
            .Select(tool => tool.Name)
            .Should()
            .Equal(definitions.Select(definition => definition.Name));
    }

    /// <summary>モックフォルダ生成プロファイルが 4 つのフォルダツールを持つことを検証する</summary>
    [Fact(DisplayName = "モックフォルダ生成プロファイルは save_screen 等の 4 ツールを持つ")]
    public void FolderMockDesign_HasFolderTools()
    {
        MockDesignProfile
            .FolderMockDesign.Tools.Select(tool => tool.Name)
            .Should()
            .BeEquivalentTo(
                MockFolderDesignTools.SaveScreenToolName,
                MockFolderDesignTools.RemoveScreenToolName,
                MockFolderDesignTools.SaveStylesheetToolName,
                MockFolderDesignTools.GetScreenToolName
            );
    }
}
