using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using QuickER.Mcp;

namespace QuickER.Tests.Mcp;

/// <summary>
/// ER 図操作ツール定義（<see cref="ErDiagramToolCatalog"/>）の固定文（ツール名・説明・入力スキーマ内の各 description）が
/// 中立言語（英語）で統一されていること＝日本語（CJK 文字）が紛れ込んでいないことを守る回帰防止ガード。
/// </summary>
/// <remarks>
/// 外部 AI エージェントは英語ツール説明を前提とするため、和文が混入すると意図せず日本語で提示される。
/// 型検査・ビルドでは検出できないため、定義を JSON 化した全文に CJK が 1 文字も無いことをテストで固定する。
/// </remarks>
public sealed class ErDiagramToolCatalogEnglishGuardTests
{
    /// <summary>
    /// CJK 文字の検出パターン。GeneratedOutputEnglishGuardTests と同じ範囲
    /// （U+3000-U+9FFF＝CJK 記号・ひらがな・カタカナ・CJK 統合漢字、U+FF00-U+FFEF＝全角英数記号・半角カナ）を対象にする。
    /// </summary>
    private static readonly Regex CjkPattern = new("[　-鿿＀-￯]", RegexOptions.Compiled);

    /// <summary>全 ER 図操作ツール定義（名前・説明・入力スキーマ）に日本語（CJK）が含まれないことを検証する</summary>
    [Fact(DisplayName = "ER 図操作ツール定義に日本語（CJK）が含まれない")]
    public void GetDefinitions_ContainsNoCjk()
    {
        var definitions = ErDiagramToolCatalog.GetDefinitions();
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
                "ER 図操作ツール定義（名前・説明・入力スキーマの description）は英語で統一する必要があります。"
                    + "上記のツールに日本語が混入しています（src/QuickER.Mcp/ErDiagramToolCatalog.cs を確認してください）"
            );
    }
}
