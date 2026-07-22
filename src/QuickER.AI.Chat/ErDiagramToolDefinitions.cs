using Anthropic.Models.Messages;
using OpenAI.Chat;
using QuickER.AI;
using QuickER.Mcp;

namespace QuickER.AI.Chat;

/// <summary>ER 図操作ツールの定義（スキーマ）を各 LLM SDK 形式へ変換する。Codex dynamicTools / OpenAI / Anthropic 対応</summary>
/// <remarks>
/// 定義データの正本は <see cref="ErDiagramToolCatalog"/>（QuickER.Mcp）にあり、本クラスはそれを取得しつつ
/// LLM SDK 依存の形式変換（<see cref="ChatToolConverter"/> 経由）を AI 層に留めるための橋渡しを担う。
/// 実行は app 側 <c>ErDiagramDynamicTools.Execute</c> が担う。
/// </remarks>
public static class ErDiagramToolDefinitions
{
    /// <summary>ER 図操作ツール定義を OpenAI SDK の <see cref="ChatTool"/> 一覧へ変換する（Function Calling 用）</summary>
    /// <remarks>定義・説明文は <see cref="GetDefinitions"/> と共有し、二重管理を避ける</remarks>
    public static IReadOnlyList<ChatTool> ToOpenAiTools() => ToOpenAiTools(GetDefinitions());

    /// <summary>任意のツール定義一覧を OpenAI SDK の <see cref="ChatTool"/> 一覧へ変換する（用途プロファイル対応）</summary>
    public static IReadOnlyList<ChatTool> ToOpenAiTools(
        IReadOnlyList<ToolDefinition> definitions
    ) => ChatToolConverter.ToOpenAiTools(definitions);

    /// <summary>ER 図操作ツール定義を Anthropic SDK の <see cref="Tool"/> 一覧へ変換する（Claude の Tool Use 用）</summary>
    /// <remarks>定義・説明文・入力スキーマは <see cref="GetDefinitions"/> と共有し、二重管理を避ける</remarks>
    public static IReadOnlyList<Tool> ToAnthropicTools() => ToAnthropicTools(GetDefinitions());

    /// <summary>任意のツール定義一覧を Anthropic SDK の <see cref="Tool"/> 一覧へ変換する（用途プロファイル対応）</summary>
    public static IReadOnlyList<Tool> ToAnthropicTools(IReadOnlyList<ToolDefinition> definitions) =>
        ChatToolConverter.ToAnthropicTools(definitions);

    /// <summary>全 ER 図操作ツールの定義一覧を返す（正本は <see cref="ErDiagramToolCatalog.GetDefinitions"/>）</summary>
    public static IReadOnlyList<ToolDefinition> GetDefinitions() =>
        ErDiagramToolCatalog.GetDefinitions();
}
