namespace QuickER.Mcp;

/// <summary>
/// MCP サーバへ公開する 1 まとまりのツール群。公開するツール定義と、
/// ツール名・引数 JSON を受け取り結果テキストと成否を返す実行デリゲートを対で保持する。
/// </summary>
/// <param name="Tools">公開するツール定義一覧</param>
/// <param name="Execute">ツール実行デリゲート（ツール名・引数 JSON → 結果テキストと成否）</param>
public sealed record McpToolSet(
    IReadOnlyList<ToolDefinition> Tools,
    Func<string, string, (string Result, bool Success)> Execute
);
