namespace QuickER.Tests.TestSupport;

/// <summary>引数の総当たり（3 つの入口）の既知の失敗（ケース ID → 期待する失敗の理由の断片）。直したら外す</summary>
internal static class ToolArgumentShapeFuzzKnownFailures
{
    /// <summary>MCP の ER 図操作ツール</summary>
    public static readonly IReadOnlyDictionary<string, string> Mcp = new Dictionary<string, string>
    { };

    /// <summary>内蔵チャット（GUI の図）</summary>
    public static readonly IReadOnlyDictionary<string, string> Chat = new Dictionary<string, string>
    { };

    /// <summary>MCP のコード生成ツール</summary>
    public static readonly IReadOnlyDictionary<string, string> CodeGen = new Dictionary<
        string,
        string
    >
    { };
}
