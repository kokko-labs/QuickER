using QuickER.AI;

namespace QuickER.AI.Mock;

/// <summary>ER 図から Web モックを生成するチャットの用途プロファイル（<see cref="ErChatProfile"/>）を提供する静的クラス</summary>
/// <remarks>
/// モック生成固有のプロンプト・ツールは機能側（QuickER.AI.Mock）に置く。
/// 現行はモックフォルダ方式（<see cref="FolderMockDesign"/>）のみ。
/// </remarks>
public static class MockDesignProfile
{
    /// <summary>
    /// ER 図から「モックフォルダ」（画面ごとの HTML＋共有 style.css）を生成するチャットのプロファイル。
    /// システムプロンプト・Codex 指示は <see cref="MockFolderDesignPrompts"/>、ツールは
    /// <see cref="MockFolderDesignTools"/>（save_screen / save_stylesheet / get_screen / remove_screen）。
    /// </summary>
    public static ErChatProfile FolderMockDesign { get; } =
        new(
            MockFolderDesignPrompts.BuildSystemPrompt,
            MockFolderDesignPrompts.BuildCodexDeveloperInstructions,
            MockFolderDesignTools.GetDefinitions(),
            "erdesigner_mock"
        );
}
