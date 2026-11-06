using System.Globalization;
using QuickER.AI.Chat;
using QuickER.AI.Chat.Resources;
using QuickER.Mcp;

namespace QuickER.Tests.TestSupport.Guards;

/// <summary>必須の文言の表：AiChat（ER 設計のルール文・チャットのツール指示・設計既定ルールのチャットと MCP の両正本）</summary>
/// <remarks>
/// resx の文言（中立＝英語 / ja サテライト＝日本語）は、静的な <c>Strings.Culture</c> を変えず
/// <c>ResourceManager.GetString(key, culture)</c> で明示カルチャを指定して読む。組み立てたプロンプトは UI 言語を
/// <see cref="EnglishTextRows.UnderUiCulture"/> で一時的に差し替えて作る。組み立ての構造（末尾の言語ルール・Codex 指示の構成）は
/// <c>AI.Chat/ErDesignRulesTests</c> が見る。
/// </remarks>
internal static partial class PhraseRows
{
    private static IEnumerable<PhraseRow> AiChat() =>
        [
            // 共通設計原則：複合主キーの禁止と、外部キーの列対応ルール（列ペア・単一列を基本とし複合も可）、
            // 役割が異なる複数の外部キーが正当である旨（過剰抑制の防止）
            new(
                "er-design-common-principles-ja",
                "共通設計原則（日本語）に複合主キーの禁止・外部キーの列対応ルール・役割が異なる複数の外部キーの許容が含まれる",
                () => ChatResource("ErDesign_CommonDesignPrinciples", "ja"),
                [
                    "複合主キー（複数列の主キー）は禁止",
                    "列ペア",
                    "ちょうど 1 列",
                    "役割が異なる複数の外部キー",
                    "それぞれ別のリレーションとして定義する",
                ],
                []
            ),
            new(
                "er-design-common-principles-en",
                "共通設計原則（英語）に複合主キーの禁止・外部キーの列対応ルール・役割が異なる複数の外部キーの許容が含まれる",
                () => ChatResource("ErDesign_CommonDesignPrinciples", "en"),
                [
                    "composite primary keys",
                    "column pairs",
                    "exactly one primary key column",
                    "different roles",
                    "define each as its own relationship",
                ],
                []
            ),
            // ツール指示テンプレート：名前付きクエリツールの運用手順と命名既定（新規はパスカルケース単数形）を含み、
            // 鏡映し（ユーザーと同じ言語で応答）の言語指示を含まない（CLI 接続でユーザー環境のメモリが「ユーザーの声」として混入すると
            // 言語推論が不安定になるため、言語指示は ResponseLanguageRule〔UI 言語既定＋明示切替〕へ一本化した）
            new(
                "er-design-chat-instructions-template-en",
                "ツール指示テンプレート（英語）は名前付きクエリツールの運用手順と命名既定を含み、鏡映しの言語指示を含まない",
                () => ChatResource("ErDesign_ChatInstructionsTemplate", "en"),
                [
                    "list_queries",
                    "set_query",
                    "remove_query",
                    "upsert",
                    "Naming",
                    "PascalCase and singular",
                ],
                ["same language as the user's most recent message"]
            ),
            new(
                "er-design-chat-instructions-template-ja",
                "ツール指示テンプレート（日本語）は名前付きクエリツールの運用手順と命名既定を含み、鏡映しの言語指示を含まない",
                () => ChatResource("ErDesign_ChatInstructionsTemplate", "ja"),
                [
                    "list_queries",
                    "set_query",
                    "remove_query",
                    "upsert",
                    "命名規則",
                    "パスカルケース・単数形",
                ],
                ["直近のメッセージと同じ言語"]
            ),
            // 応答言語ルール：UI 言語を既定にし、利用者が明らかに別の言語で書いたときだけ切り替える
            new(
                "er-design-response-language-rule-ja",
                "応答言語ルール（日本語）は UI 言語を既定にし、明らかに別の言語のときだけ切り替える",
                () => ChatResource("ErDesign_ResponseLanguageRule", "ja"),
                ["既定で日本語", "明らかに別の言語で書かれている場合のみ"],
                []
            ),
            new(
                "er-design-response-language-rule-en",
                "応答言語ルール（英語）は UI 言語を既定にし、明らかに別の言語のときだけ切り替える",
                () => ChatResource("ErDesign_ResponseLanguageRule", "en"),
                ["Default to English", "clearly written in a different language"],
                []
            ),
            // 設計既定ルール（命名規則・単一主キー・FK 手順・要約先読み）は、内蔵チャットのシステムプロンプト（resx 対訳）と
            // 外部 MCP のサーバ使用指針（英語正本）の両方にある。経路が異なるため意図的な二重管理で、片方だけ更新される事故を検知する
            // （文言の一字一句は比べず、核心の語だけを見る）
            new(
                "design-guidelines-chat-prompt-en",
                "設計既定ルール（命名規則・単一主キー・FK 手順・要約先読み）が内蔵チャットのシステムプロンプト（英語 UI）にある",
                () => EnglishTextRows.UnderUiCulture("en", ErDesignRules.BuildChatSystemPrompt),
                ["PascalCase", "primary key", "add_relationship", "get_diagram_summary"],
                []
            ),
            new(
                "design-guidelines-chat-prompt-ja",
                "設計既定ルール（命名規則・単一主キー・FK 手順・要約先読み）が内蔵チャットのシステムプロンプト（日本語 UI）にある",
                () => EnglishTextRows.UnderUiCulture("ja", ErDesignRules.BuildChatSystemPrompt),
                ["パスカルケース", "主キー", "add_relationship", "get_diagram_summary"],
                []
            ),
            new(
                "design-guidelines-mcp-server-instructions",
                "設計既定ルール（命名規則・単一主キー・FK 手順・要約先読み）が外部 MCP のサーバ使用指針"
                    + "（ErDiagramToolCatalog.ServerInstructions）にある",
                () => ErDiagramToolCatalog.ServerInstructions,
                [
                    "PascalCase",
                    "exactly one primary key column",
                    "primary key",
                    "add_relationship",
                    "get_diagram_summary",
                ],
                []
            ),
        ];

    /// <summary>内蔵チャットの resx の値を明示カルチャで読む（無ければ例外）</summary>
    private static string ChatResource(string key, string culture) =>
        Strings.ResourceManager.GetString(key, new CultureInfo(culture))
        ?? throw new InvalidOperationException($"resx のキーが無い: {key}（{culture}）");
}
