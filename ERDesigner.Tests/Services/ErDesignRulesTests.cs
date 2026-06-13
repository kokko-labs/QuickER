using ERDesigner.Services;
using FluentAssertions;

namespace ERDesigner.Tests.Services;

/// <summary><see cref="ErDesignRules"/> の設計ルール文の組み立てを検証するテストクラス</summary>
public class ErDesignRulesTests
{
    /// <summary>共通設計原則に複合主キー・複合外部キーの禁止文言が含まれることを検証する</summary>
    [Fact(DisplayName = "共通設計原則に複合PK・複合FKの禁止文言が含まれる")]
    public void CommonDesignPrinciples_ContainsCompositeKeyProhibition()
    {
        ErDesignRules.CommonDesignPrinciples.Should().Contain("複合主キー（複数列の主キー）は禁止");
        ErDesignRules.CommonDesignPrinciples.Should().Contain("複合外部キーは禁止");
        ErDesignRules.CommonDesignPrinciples.Should().Contain("ちょうど 1 列");
    }

    /// <summary>共通設計原則に役割別の複数FKが正当である旨が含まれることを検証する（過剰抑制の防止）</summary>
    [Fact(DisplayName = "共通設計原則に役割が異なる複数FKを許容する文言が含まれる")]
    public void CommonDesignPrinciples_AllowsMultipleRoleForeignKeys()
    {
        ErDesignRules.CommonDesignPrinciples.Should().Contain("役割が異なる複数の外部キー");
        ErDesignRules.CommonDesignPrinciples.Should().Contain("それぞれ別のリレーションとして定義する");
    }

    /// <summary>Codex 用 developerInstructions が設計原則とツール運用手順を含むことを検証する</summary>
    [Fact(DisplayName = "Codex 用指示は設計原則とツール運用手順を含む")]
    public void BuildCodexDeveloperInstructions_ContainsPrinciplesAndToolWorkflow()
    {
        var instructions = ErDesignRules.BuildCodexDeveloperInstructions();

        instructions.Should().Contain(ErDesignRules.CommonDesignPrinciples);
        instructions.Should().Contain("get_diagram_summary");
        instructions.Should().Contain("add_entity");
        instructions.Should().Contain("add_column");
        instructions.Should().Contain("add_relationship");
        instructions.Should().Contain(ErDesignRules.SinglePrimaryKeyRule);
        instructions.Should().Contain(ErDesignRules.SingleColumnForeignKeyRule);
    }

    /// <summary>Codex 用 developerInstructions が命名既定（既存図優先・新規はパスカルケース単数形）を含むことを検証する</summary>
    [Fact(DisplayName = "Codex 用指示は命名既定を含む")]
    public void BuildCodexDeveloperInstructions_ContainsNamingDefaults()
    {
        var instructions = ErDesignRules.BuildCodexDeveloperInstructions();

        instructions.Should().Contain("命名規則");
        instructions.Should().Contain("パスカルケース・単数形");
    }

    /// <summary>識別子命名規則の指示行がスタイルごとに切り替わることを検証する</summary>
    [Theory(DisplayName = "命名規則の指示行はスタイルごとに切り替わる")]
    [InlineData(AiIdentifierNamingStyle.SnakeCase, "スネークケース")]
    [InlineData(AiIdentifierNamingStyle.PascalCase, "パスカルケース")]
    public void BuildNamingInstruction_SwitchesByStyle(AiIdentifierNamingStyle style, string expected)
    {
        ErDesignRules.BuildNamingInstruction(style).Should().Contain(expected);
    }

    /// <summary>テーブル名の単数・複数の指示行がスタイルごとに切り替わることを検証する</summary>
    [Theory(DisplayName = "テーブル名の単複数の指示行はスタイルごとに切り替わる")]
    [InlineData(AiTableNameNumberStyle.Plural, "複数形")]
    [InlineData(AiTableNameNumberStyle.Singular, "単数形")]
    public void BuildTableNameNumberInstruction_SwitchesByStyle(AiTableNameNumberStyle style, string expected)
    {
        ErDesignRules.BuildTableNameNumberInstruction(style).Should().Contain(expected);
    }
}
