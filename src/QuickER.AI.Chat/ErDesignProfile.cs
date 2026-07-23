using QuickER.AI;
using QuickER.Mcp;

namespace QuickER.AI.Chat;

/// <summary>ER 図設計チャットの用途プロファイル（<see cref="ErChatProfile"/>）を提供する静的クラス</summary>
/// <remarks>
/// システムプロンプト・Codex 指示・ツール定義・MCP サーバー名のいずれも従来のハードコード内容と一致する。
/// 以前は <c>ErChatProfile.ErDesign</c> として Core（QuickER.AI）に置かれていたが、
/// ER 設計固有のプロンプト・ツールが機能側（QuickER.AI.Chat）へ移ったため、ここへ移設した。
/// </remarks>
public static class ErDesignProfile
{
    /// <summary>
    /// ER 図設計チャットの既定プロファイル。
    /// システムプロンプト・Codex 指示・ツール定義・MCP サーバー名のいずれも従来のハードコード内容と一致する。
    /// </summary>
    public static ErChatProfile ErDesign { get; } =
        new(
            ErDesignRules.BuildChatSystemPrompt,
            ErDesignRules.BuildCodexDeveloperInstructions,
            ErDiagramToolCatalog.GetDefinitions(),
            ErDiagramMcpServer.ServerName
        );
}
