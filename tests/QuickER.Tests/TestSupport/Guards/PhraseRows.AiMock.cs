using QuickER.AI.Mock;

namespace QuickER.Tests.TestSupport.Guards;

/// <summary>必須の文言の表：AiMock（モックフォルダ方式の設計チャットのシステムプロンプト・Codex 指示）</summary>
/// <remarks>
/// <see cref="MockFolderDesignPrompts"/> のシステムプロンプト／Codex 指示が、モックフォルダ方式の核心要素
/// （提案合意フロー・save_screen / get_screen / style.css）を含み、表示言語に追従すること。UI 言語は
/// <see cref="EnglishTextRows.UnderUiCulture"/> で一時的に差し替えて作る。
/// </remarks>
internal static partial class PhraseRows
{
    private static IEnumerable<PhraseRow> AiMock() =>
        [
            new(
                "mock-folder-design-system-prompt-en",
                "モック設計チャットの英語のシステムプロンプトは核心の語（ツール名・style.css・提案→合意フロー・ツール機構のラベル・"
                    + "画面×エンティティ CRUD の宣言）を含み、ラベルの差し替え（{0}）が解決されている",
                () =>
                    EnglishTextRows.UnderUiCulture("en", MockFolderDesignPrompts.BuildSystemPrompt),
                [
                    "save_screen",
                    "save_stylesheet",
                    "get_screen",
                    "style.css",
                    "propose",
                    "agree",
                    "function tools",
                    "entities",
                    "operations",
                ],
                ["{0}"]
            ),
            new(
                "mock-folder-design-system-prompt-ja",
                "モック設計チャットの日本語のシステムプロンプトは核心の語（ツール名・style.css・提案→合意フロー・ツール機構のラベル・"
                    + "エンティティの宣言）を含み、ラベルの差し替え（{0}）が解決されている",
                () =>
                    EnglishTextRows.UnderUiCulture("ja", MockFolderDesignPrompts.BuildSystemPrompt),
                [
                    "save_screen",
                    "get_screen",
                    "style.css",
                    "提案",
                    "合意",
                    "関数ツール",
                    "entities",
                    "エンティティ",
                ],
                ["{0}"]
            ),
            new(
                "mock-folder-design-codex-instructions-en",
                "モック設計チャットの Codex 指示は dynamicTools のラベルへ差し替えられ、本文はシステムプロンプトと同旨",
                () =>
                    EnglishTextRows.UnderUiCulture(
                        "en",
                        MockFolderDesignPrompts.BuildCodexDeveloperInstructions
                    ),
                ["dynamicTools", "save_screen"],
                ["{0}"]
            ),
            // 応答言語ルール（UI 言語既定＋明示切替）を含み、鏡映し（ユーザーと同じ言語で）の指示を含まない。鏡映しは CLI 接続の
            // ユーザー環境のメモリに引きずられ不安定なため、ErDesignRules と同方式の決定的な文言へ一本化した。
            // 含むかだけを見る（末尾にあることまでは見ない）
            new(
                "mock-folder-design-response-language-en",
                "モック設計チャットの英語のシステムプロンプトは応答言語ルールを含み、鏡映しの言語指示を含まない",
                () =>
                    EnglishTextRows.UnderUiCulture("en", MockFolderDesignPrompts.BuildSystemPrompt),
                ["# Response language", "Default to English"],
                ["same language as the user's most recent message"]
            ),
            new(
                "mock-folder-design-response-language-ja",
                "モック設計チャットの日本語のシステムプロンプトは応答言語ルールを含み、鏡映しの言語指示を含まない",
                () =>
                    EnglishTextRows.UnderUiCulture("ja", MockFolderDesignPrompts.BuildSystemPrompt),
                ["# 応答言語", "既定で日本語"],
                ["直近のメッセージと同じ言語"]
            ),
        ];
}
