namespace QuickER.AI;

/// <summary>
/// AI チャットの「用途プロファイル」。システムプロンプト・Codex 用 developer instructions・
/// ツール定義セット・MCP サーバー名をひとまとめにし、各エンジン／ドライバ／MCP サーバーへ注入する。
/// </summary>
/// <remarks>
/// これにより「ER 図設計チャット」以外の用途（例: Web モック HTML 生成）を、
/// エンジン実装を変更せずにプロファイル差し替えだけで載せられる。
/// 既定インスタンス <see cref="ErDesign"/> は従来のハードコード内容と完全一致する。
/// </remarks>
/// <param name="BuildSystemPrompt">
/// API キー接続チャット（OpenAI/Anthropic）用のシステムプロンプトを生成する関数
/// </param>
/// <param name="BuildCodexDeveloperInstructions">
/// Codex スレッド開始時に渡す developerInstructions を生成する関数
/// </param>
/// <param name="Tools">ツール定義セット（Codex dynamicTools / OpenAI / Anthropic / MCP へ変換される）</param>
/// <param name="McpServerName">MCP サーバー名（ツール名は <c>mcp__&lt;name&gt;__&lt;tool&gt;</c> になる）</param>
public sealed record ErChatProfile(
    Func<string> BuildSystemPrompt,
    Func<string> BuildCodexDeveloperInstructions,
    IReadOnlyList<CodexDynamicToolDefinition> Tools,
    string McpServerName
)
{
    /// <summary>
    /// ER 図設計チャットの既定プロファイル。
    /// システムプロンプト・Codex 指示・ツール定義・MCP サーバー名のいずれも従来のハードコード内容と一致する。
    /// </summary>
    public static ErChatProfile ErDesign { get; } =
        new(
            ErDesignRules.BuildChatSystemPrompt,
            ErDesignRules.BuildCodexDeveloperInstructions,
            ErDiagramToolDefinitions.GetDefinitions(),
            ErDiagramMcpServer.ServerName
        );

    /// <summary>
    /// ER 図から Web モック HTML を生成するチャットのプロファイル。
    /// システムプロンプト・Codex 指示は <see cref="MockDesignPrompts"/>、ツールは <see cref="MockDesignTools"/>。
    /// </summary>
    public static ErChatProfile MockDesign { get; } =
        new(
            MockDesignPrompts.BuildSystemPrompt,
            MockDesignPrompts.BuildCodexDeveloperInstructions,
            MockDesignTools.GetDefinitions(),
            "erdesigner_mock"
        );
}
