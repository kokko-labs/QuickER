using QuickER.Mcp;

namespace QuickER.AI;

/// <summary>
/// AI チャットの「用途プロファイル」。システムプロンプト・Codex 用 developer instructions・
/// ツール定義セット・MCP サーバー名をひとまとめにし、各エンジン／ドライバ／MCP サーバーへ注入する。
/// </summary>
/// <remarks>
/// これにより「ER 図設計チャット」以外の用途（例: Web モック HTML 生成）を、
/// エンジン実装を変更せずにプロファイル差し替えだけで載せられる。
/// 具体プロファイルは機能側が提供する（ER 設計＝<c>QuickER.AI.Chat.ErDesignProfile.ErDesign</c>、
/// モック生成＝<c>QuickER.AI.Mock.MockDesignProfile.FolderMockDesign</c>）。
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
    IReadOnlyList<ToolDefinition> Tools,
    string McpServerName
);
