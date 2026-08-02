using System.Collections.Generic;
using System.Text.Json;
using System.Text.RegularExpressions;
using AwesomeAssertions;
using QuickER.Cli;
using Xunit;

namespace QuickER.Tests.Cli;

/// <summary>
/// MCP のコード生成ツール定義（<see cref="CodeGenToolSet"/>＝<c>generate_csharp</c> / <c>generate_ddl</c> /
/// <c>get_generation_config_schema</c>）の固定文（ツール名・説明・入力スキーマ内の各 description）が
/// 中立言語（英語）で統一されていること＝日本語（CJK 文字）が紛れ込んでいないことを守る回帰防止ガード。
/// </summary>
/// <remarks>
/// 外部 AI エージェント（Claude Code / Codex 等）向けの stdio MCP サーバが公開する定義のため英語が正本。
/// <c>ErDiagramToolCatalogEnglishGuardTests</c> と同じ流儀で定義全文を走査する。走査対象は実際に公開される
/// <see cref="QuickER.Mcp.McpToolSet.Tools"/>（<c>file</c> 注入後）＝サーバが送る形そのもの。
/// </remarks>
public class CodeGenToolSetEnglishGuardTests
{
    /// <summary>
    /// CJK 文字の検出パターン。ErDiagramToolCatalogEnglishGuardTests と同じ範囲
    /// （U+3000-U+9FFF＝CJK 記号・ひらがな・カタカナ・CJK 統合漢字、U+FF00-U+FFEF＝全角英数記号・半角カナ）を対象にする。
    /// </summary>
    private static readonly Regex CjkPattern = new("[　-鿿＀-￯]", RegexOptions.Compiled);

    /// <summary>公開される全コード生成ツール定義（名前・説明・入力スキーマ）に日本語が含まれないことを検証する</summary>
    [Fact(DisplayName = "コード生成ツール定義に日本語（CJK）が含まれない")]
    public void Tools_ContainNoCjk()
    {
        var definitions = CodeGenToolSet.Create().Tools;

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
                "コード生成ツール定義（名前・説明・入力スキーマの description）は英語で統一する必要があります。"
                    + "上記のツールに日本語が混入しています（src/QuickER.Cli/CodeGenToolSet.cs を確認してください）"
            );
    }
}
