using System.Globalization;
using FluentAssertions;
using QuickER.AI;
using QuickER.AI.Chat;
using QuickER.AI.Chat.Resources;

namespace QuickER.Tests.AI.Chat;

/// <summary><see cref="ErDesignRules"/> の設計ルール文の組み立てを検証するテストクラス</summary>
/// <remarks>
/// 文言は resx（中立＝英語 / ja サテライト＝日本語）に移行済み。言語別の内容検証は静的
/// <c>Strings.Culture</c> を変更せず、<see cref="System.Resources.ResourceManager.GetString(string, CultureInfo)"/>
/// で明示カルチャ指定して読む（クラス並列実行での漏れを避ける安全な方式。StringsLocalizationTests に倣う）。
/// 組み立て（構造）の検証は、言語に依らないトークン（ツール名や ambient カルチャで読んだルール文の一致）で行う。
/// </remarks>
public class ErDesignRulesTests
{
    private static readonly CultureInfo Japanese = new("ja");
    private static readonly CultureInfo English = new("en");

    private static string Ja(string key) => Strings.ResourceManager.GetString(key, Japanese)!;

    private static string En(string key) => Strings.ResourceManager.GetString(key, English)!;

    /// <summary>共通設計原則（英語・日本語）に複合主キー・複合外部キーの禁止文言が含まれることを検証する</summary>
    [Fact(DisplayName = "共通設計原則に複合PK・複合FKの禁止文言が含まれる")]
    public void CommonDesignPrinciples_ContainsCompositeKeyProhibition()
    {
        Ja("ErDesign_CommonDesignPrinciples")
            .Should()
            .Contain("複合主キー（複数列の主キー）は禁止");
        Ja("ErDesign_CommonDesignPrinciples").Should().Contain("複合外部キーは禁止");
        Ja("ErDesign_CommonDesignPrinciples").Should().Contain("ちょうど 1 列");

        En("ErDesign_CommonDesignPrinciples").Should().Contain("composite primary keys");
        En("ErDesign_CommonDesignPrinciples").Should().Contain("Composite foreign keys");
        En("ErDesign_CommonDesignPrinciples").Should().Contain("exactly one primary key column");
    }

    /// <summary>共通設計原則に役割別の複数FKが正当である旨が含まれることを検証する（過剰抑制の防止）</summary>
    [Fact(DisplayName = "共通設計原則に役割が異なる複数FKを許容する文言が含まれる")]
    public void CommonDesignPrinciples_AllowsMultipleRoleForeignKeys()
    {
        Ja("ErDesign_CommonDesignPrinciples").Should().Contain("役割が異なる複数の外部キー");
        Ja("ErDesign_CommonDesignPrinciples")
            .Should()
            .Contain("それぞれ別のリレーションとして定義する");

        En("ErDesign_CommonDesignPrinciples").Should().Contain("different roles");
        En("ErDesign_CommonDesignPrinciples")
            .Should()
            .Contain("define each as its own relationship");
    }

    /// <summary>Codex 用 developerInstructions が設計原則とツール運用手順を含むことを検証する（言語非依存トークン）</summary>
    [Fact(DisplayName = "Codex 用指示は設計原則とツール運用手順を含む")]
    public void BuildCodexDeveloperInstructions_ContainsPrinciplesAndToolWorkflow()
    {
        var instructions = ErDesignRules.BuildCodexDeveloperInstructions();

        // 共通設計原則・単一 PK/FK ルールは ambient カルチャで読んだ値がそのまま埋め込まれる
        instructions.Should().Contain(ErDesignRules.CommonDesignPrinciples);
        instructions.Should().Contain(ErDesignRules.SinglePrimaryKeyRule);
        instructions.Should().Contain(ErDesignRules.SingleColumnForeignKeyRule);

        // ツール名はどの言語でもリテラルで残る
        instructions.Should().Contain("get_diagram_summary");
        instructions.Should().Contain("add_entity");
        instructions.Should().Contain("add_column");
        instructions.Should().Contain("add_relationship");
    }

    /// <summary>ツール指示テンプレートが名前付きクエリツールの運用手順を含むことを検証する（両言語）</summary>
    [Fact(DisplayName = "ツール指示は名前付きクエリツールの運用手順を含む")]
    public void ChatInstructionsTemplate_ContainsNamedQueryWorkflow()
    {
        foreach (
            var template in new[]
            {
                En("ErDesign_ChatInstructionsTemplate"),
                Ja("ErDesign_ChatInstructionsTemplate"),
            }
        )
        {
            template.Should().Contain("list_queries");
            template.Should().Contain("set_query");
            template.Should().Contain("remove_query");
            template.Should().Contain("upsert");
        }
    }

    /// <summary>ツール指示テンプレートが「ユーザーと同じ言語で応答する」指示を含むことを検証する（両言語）</summary>
    [Fact(DisplayName = "ツール指示はユーザー言語での応答指示を含む")]
    public void ChatInstructionsTemplate_ContainsRespondInUserLanguageInstruction()
    {
        En("ErDesign_ChatInstructionsTemplate")
            .Should()
            .Contain("Respond in the same language as the user's most recent message");
        Ja("ErDesign_ChatInstructionsTemplate")
            .Should()
            .Contain("応答は必ずユーザーの直近のメッセージと同じ言語で");
    }

    /// <summary>Codex 用 developerInstructions が命名既定（新規はパスカルケース単数形）を含むことを検証する（両言語テンプレート）</summary>
    [Fact(DisplayName = "ツール指示は命名既定を含む")]
    public void ChatInstructionsTemplate_ContainsNamingDefaults()
    {
        Ja("ErDesign_ChatInstructionsTemplate").Should().Contain("命名規則");
        Ja("ErDesign_ChatInstructionsTemplate").Should().Contain("パスカルケース・単数形");

        En("ErDesign_ChatInstructionsTemplate").Should().Contain("Naming");
        En("ErDesign_ChatInstructionsTemplate").Should().Contain("PascalCase and singular");
    }

    /// <summary>識別子命名規則の指示行がスタイルごとに対応する resx キーへ切り替わることを検証する（ambient カルチャ非依存）</summary>
    [Fact(DisplayName = "命名規則の指示行はスタイルごとに切り替わる")]
    public void BuildNamingInstruction_SwitchesByStyle()
    {
        ErDesignRules
            .BuildNamingInstruction(AiIdentifierNamingStyle.SnakeCase)
            .Should()
            .Be(Strings.ErDesign_NamingSnakeCase);
        ErDesignRules
            .BuildNamingInstruction(AiIdentifierNamingStyle.PascalCase)
            .Should()
            .Be(Strings.ErDesign_NamingPascalCase);
    }

    /// <summary>テーブル名の単数・複数の指示行がスタイルごとに対応する resx キーへ切り替わることを検証する（ambient カルチャ非依存）</summary>
    [Fact(DisplayName = "テーブル名の単複数の指示行はスタイルごとに切り替わる")]
    public void BuildTableNameNumberInstruction_SwitchesByStyle()
    {
        ErDesignRules
            .BuildTableNameNumberInstruction(AiTableNameNumberStyle.Plural)
            .Should()
            .Be(Strings.ErDesign_TableNamePlural);
        ErDesignRules
            .BuildTableNameNumberInstruction(AiTableNameNumberStyle.Singular)
            .Should()
            .Be(Strings.ErDesign_TableNameSingular);
    }
}
