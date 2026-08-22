using QuickER.AI;
using QuickER.Mcp;

namespace QuickER.AI.Chat;

/// <summary>ER 図設計チャットの用途プロファイル（<see cref="ErChatProfile"/>）を提供する静的クラス</summary>
/// <remarks>
/// ER 設計固有のプロンプト・ツール定義は機能側（QuickER.AI.Chat）の持ち物のため、
/// それらを束ねるプロファイルもここに置く（Core の QuickER.AI は用途非依存に保つ）。
/// </remarks>
public static class ErDesignProfile
{
    /// <summary>
    /// ER 図設計チャットの既定プロファイル
    /// （システムプロンプト・Codex 指示・ツール定義・MCP サーバー名の組）。
    /// </summary>
    public static ErChatProfile ErDesign { get; } =
        new(
            ErDesignRules.BuildChatSystemPrompt,
            ErDesignRules.BuildCodexDeveloperInstructions,
            ErDiagramToolCatalog.GetDefinitions(),
            ErDiagramMcpServer.ServerName
        );
}
