using System.Text.RegularExpressions;
using AwesomeAssertions;
using QuickER.Mcp;

namespace QuickER.Tests.Mcp;

/// <summary>
/// ER 図操作ツールのサーバ使用指針（<see cref="ErDiagramToolCatalog.ServerInstructions"/>）が中立言語（英語）で書かれ、
/// 既定の設計指針を含むことを守る回帰防止ガード。
/// </summary>
/// <remarks>
/// ツール定義（名前・説明・入力スキーマ）の英語のガードは、ほかの出どころのツール定義と一緒に
/// <see cref="ToolDefinitionEnglishGuardTests"/> が持つ。
/// </remarks>
public sealed class ErDiagramToolCatalogEnglishGuardTests
{
    /// <summary>
    /// CJK 文字の検出パターン。GeneratedOutputEnglishGuardTests と同じ範囲
    /// （U+3000-U+9FFF＝CJK 記号・ひらがな・カタカナ・CJK 統合漢字、U+FF00-U+FFEF＝全角英数記号・半角カナ）を対象にする。
    /// </summary>
    private static readonly Regex CjkPattern = TestSupport.CjkText.Pattern;

    /// <summary>サーバ使用指針（MCP instructions）に日本語（CJK）が含まれないことを検証する</summary>
    [Fact(DisplayName = "サーバ使用指針（instructions）に日本語（CJK）が含まれない")]
    public void ServerInstructions_ContainsNoCjk()
    {
        CjkPattern
            .IsMatch(ErDiagramToolCatalog.ServerInstructions)
            .Should()
            .BeFalse(
                "サーバ使用指針（ServerInstructions）は外部エージェント向けの英語正本です"
                    + "（src/QuickER.Mcp/ErDiagramToolCatalog.cs を確認してください）"
            );
    }

    /// <summary>サーバ使用指針が既定の設計指針（命名規則・単一主キー）を含むことを検証する</summary>
    [Fact(DisplayName = "サーバ使用指針が命名規則・単一主キーの指針を含む")]
    public void ServerInstructions_ContainsDesignGuidelines()
    {
        ErDiagramToolCatalog.ServerInstructions.Should().Contain("PascalCase");
        ErDiagramToolCatalog.ServerInstructions.Should().Contain("exactly one primary key column");
    }
}
