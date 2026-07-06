using QuickER.AI;

namespace QuickER.AI.Mock;

/// <summary>ER 図から Web モック HTML を生成するチャットの用途プロファイル（<see cref="ErChatProfile"/>）を提供する静的クラス</summary>
/// <remarks>
/// システムプロンプト・Codex 指示は <see cref="MockDesignPrompts"/>、ツールは <see cref="MockDesignTools"/>。
/// 以前は <c>ErChatProfile.MockDesign</c> として Core（QuickER.AI）に置かれていたが、
/// モック生成固有のプロンプト・ツールが機能側（QuickER.AI.Mock）へ移ったため、ここへ移設した。
/// </remarks>
public static class MockDesignProfile
{
    /// <summary>
    /// ER 図から Web モック HTML を生成するチャットのプロファイル。
    /// システムプロンプト・Codex 指示・ツール定義・MCP サーバー名のいずれも従来のハードコード内容と一致する。
    /// </summary>
    public static ErChatProfile MockDesign { get; } =
        new(
            MockDesignPrompts.BuildSystemPrompt,
            MockDesignPrompts.BuildCodexDeveloperInstructions,
            MockDesignTools.GetDefinitions(),
            "erdesigner_mock"
        );
}
