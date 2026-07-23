namespace QuickER.Mcp;

/// <summary>
/// LLM／MCP へ公開する 1 ツールの定義（スキーマ）。VM・DB 非依存の純粋な POCO で、
/// クラス名は JSON 直列化に影響せず（プロパティ名のみ使用）、各 LLM SDK 形式・MCP ツールへ変換される。
/// </summary>
public sealed class ToolDefinition
{
    /// <summary>ツール名</summary>
    public required string Name { get; init; }

    /// <summary>ツールの説明</summary>
    public required string Description { get; init; }

    /// <summary>遅延ロードするかどうか（Codex dynamicTools 用のフラグ。MCP 経由では未使用）</summary>
    public bool DeferLoading { get; init; } = true;

    /// <summary>入力 JSON Schema（匿名型ツリーまたは JsonNode）</summary>
    public required object InputSchema { get; init; }
}
