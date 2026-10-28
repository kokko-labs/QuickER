using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.TestSupport;

/// <summary>
/// 引数の総当たり（3 つの入口）の既知の失敗（<see cref="KnownCaseLedger.Case"/> で作る項目＝ケース ID・期待する失敗の理由の断片）。
/// 直したら外す
/// </summary>
internal static class ToolArgumentShapeFuzzKnownFailures
{
    /// <summary>MCP の ER 図操作ツール</summary>
    public static readonly IReadOnlyList<KnownEntry> Mcp = [];

    /// <summary>内蔵チャット（GUI の図）</summary>
    public static readonly IReadOnlyList<KnownEntry> Chat = [];

    /// <summary>MCP のコード生成ツール</summary>
    public static readonly IReadOnlyList<KnownEntry> CodeGen = [];
}
