using System.Collections.Generic;
using System.Text.Json;
using System.Text.RegularExpressions;
using AwesomeAssertions;
using QuickER.AI.Mock;
using QuickER.Mcp;
using Xunit;

namespace QuickER.Tests.AI.Mock;

/// <summary>
/// モック生成が公開するツール定義（<see cref="MockFolderDesignTools"/>＝モックフォルダ設計 4 ツール／
/// <see cref="MockProjectEmitTools"/>＝<c>emit_file</c>）の固定文（ツール名・説明・入力スキーマ内の各 description）が
/// 中立言語（英語）で統一されていること＝日本語（CJK 文字）が紛れ込んでいないことを守る回帰防止ガード。
/// </summary>
/// <remarks>
/// LLM へ渡すツール定義は英語正本。和文が混入すると回答言語が意図せず引きずられる。既存の個別テストは
/// 説明文に対する数語の否定アサートに留まるため、<c>ErDiagramToolCatalogEnglishGuardTests</c> と同じ流儀で
/// 定義全文（入力スキーマの JSON 全体を含む）を走査する網羅ガードを別に置く。
/// </remarks>
public class MockToolDefinitionEnglishGuardTests
{
    /// <summary>
    /// CJK 文字の検出パターン。ErDiagramToolCatalogEnglishGuardTests と同じ範囲
    /// （U+3000-U+9FFF＝CJK 記号・ひらがな・カタカナ・CJK 統合漢字、U+FF00-U+FFEF＝全角英数記号・半角カナ）を対象にする。
    /// </summary>
    private static readonly Regex CjkPattern = new("[　-鿿＀-￯]", RegexOptions.Compiled);

    /// <summary>モックフォルダ設計ツール定義（名前・説明・入力スキーマ）に日本語が含まれないことを検証する</summary>
    [Fact(DisplayName = "モックフォルダ設計ツール定義に日本語（CJK）が含まれない")]
    public void MockFolderDesignTools_ContainNoCjk()
    {
        AssertNoCjk(
            MockFolderDesignTools.GetDefinitions(),
            "src/QuickER.AI.Mock/MockFolderDesignTools.cs"
        );
    }

    /// <summary>モックプロジェクト生成の emit_file 定義（名前・説明・入力スキーマ）に日本語が含まれないことを検証する</summary>
    [Fact(DisplayName = "emit_file ツール定義に日本語（CJK）が含まれない")]
    public void MockProjectEmitTools_ContainNoCjk()
    {
        AssertNoCjk(
            MockProjectEmitTools.GetDefinitions(),
            "src/QuickER.AI.Mock/MockProjectEmitTools.cs"
        );
    }

    /// <summary>ツール定義一覧の名前・説明・入力スキーマ JSON 全文に CJK が無いことを検証する</summary>
    private static void AssertNoCjk(IReadOnlyList<ToolDefinition> definitions, string sourcePath)
    {
        definitions.Should().NotBeEmpty("走査対象のツール定義が 1 つも見つからない");

        var findings = new List<string>();

        foreach (var definition in definitions)
        {
            var schemaJson = JsonSerializer.Serialize(definition.InputSchema);
            var whole = $"{definition.Name}\n{definition.Description}\n{schemaJson}";

            if (CjkPattern.IsMatch(whole))
            {
                findings.Add($"{definition.Name}: 「{whole}」");
            }
        }

        findings
            .Should()
            .BeEmpty(
                "ツール定義（名前・説明・入力スキーマの description）は英語で統一する必要があります。"
                    + $"上記のツールに日本語が混入しています（{sourcePath} を確認してください）"
            );
    }
}
