using AwesomeAssertions;
using QuickER.AI;
using QuickER.AI.Chat;
using QuickER.AI.Chat.Resources;

namespace QuickER.Tests.AI.Chat;

/// <summary><see cref="ErDesignRules"/> の設計ルール文の組み立てを検証するテストクラス</summary>
/// <remarks>
/// 組み立て（構造）の検証は、言語に依らないトークン（ツール名や ambient カルチャで読んだルール文の一致）で行う。
/// 文言（resx の中立＝英語 / ja サテライト＝日本語）が決めた語を含む・含まないことは、必須の文言の表
/// （<c>PhraseTests</c> の必須の文言の行 <c>er-design-common-principles-*</c>・<c>er-design-chat-instructions-template-*</c>・
/// <c>er-design-response-language-rule-*</c>）が見る。
/// </remarks>
public class ErDesignRulesTests
{
    /// <summary>Codex 用 developerInstructions が設計原則とツール運用手順を含むことを検証する（言語非依存トークン）</summary>
    [Fact(DisplayName = "Codex 用指示は設計原則とツール運用手順を含む")]
    public void BuildCodexDeveloperInstructions_ContainsPrinciplesAndToolWorkflow()
    {
        var instructions = ErDesignRules.BuildCodexDeveloperInstructions();

        // 共通設計原則・単一 PK/FK ルールは ambient カルチャで読んだ値がそのまま埋め込まれる
        instructions.Should().Contain(ErDesignRules.CommonDesignPrinciples);
        instructions.Should().Contain(ErDesignRules.SinglePrimaryKeyRule);
        instructions.Should().Contain(ErDesignRules.ForeignKeyColumnMappingRule);

        // ツール名はどの言語でもリテラルで残る
        instructions.Should().Contain("get_diagram_summary");
        instructions.Should().Contain("add_entity");
        instructions.Should().Contain("add_column");
        instructions.Should().Contain("add_relationship");
    }

    /// <summary>
    /// チャット指示文（API キー / Codex / Claude Code 共通）が最優先の言語ルールで終わることを検証する。
    /// CLI エージェント接続ではユーザー環境のメモリファイル等に応答言語が引きずられることがあるため、
    /// 直近位置（最後尾）への付加を構造として固定する（ルール文の中身は必須の文言の行 <c>er-design-response-language-rule-*</c>）
    /// </summary>
    [Fact(DisplayName = "チャット指示文は最優先の言語ルールで終わる")]
    public void ChatInstructions_EndWithResponseLanguageRule()
    {
        ErDesignRules
            .BuildChatSystemPrompt()
            .Should()
            .EndWith(Strings.ErDesign_ResponseLanguageRule);
        ErDesignRules
            .BuildCodexDeveloperInstructions()
            .Should()
            .EndWith(Strings.ErDesign_ResponseLanguageRule);
    }
}
