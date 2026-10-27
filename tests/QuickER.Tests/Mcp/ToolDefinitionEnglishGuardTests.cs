using System.Text.Json;
using AwesomeAssertions;
using QuickER.AI.Mock;
using QuickER.Cli;
using QuickER.Mcp;

namespace QuickER.Tests.Mcp;

/// <summary>
/// AI へ渡すツール定義の固定文（ツール名・説明・入力スキーマ内の各 description）が中立言語（英語）で統一されていること
/// ＝日本語（CJK 文字）が紛れ込んでいないことを守る回帰防止ガード。
/// </summary>
/// <remarks>
/// <para>
/// 外部 AI エージェント（Claude Code / Codex 等）向けの MCP サーバと、内蔵チャット・モック生成が LLM へ渡すツール定義は
/// 英語が正本。和文が混入すると、意図せず日本語で提示され、回答の言語も引きずられる。型検査・ビルドでは検出できないため、
/// 定義を JSON 化した全文に CJK が 1 文字も無いことをテストで固定する。
/// </para>
/// <para>
/// 行はツール定義の出どころごと（<see cref="Sources"/>）。コード生成ツールは実際に公開される
/// <see cref="McpToolSet.Tools"/>（<c>file</c> 注入後）＝サーバが送る形そのものを走査する。
/// </para>
/// </remarks>
public sealed class ToolDefinitionEnglishGuardTests
{
    /// <summary>ツール定義の出どころ（行の ID → 定義の一覧と、確かめるソースのパス）</summary>
    private static readonly IReadOnlyDictionary<
        string,
        (Func<IReadOnlyList<ToolDefinition>> Definitions, string SourcePath)
    > Sources = new Dictionary<string, (Func<IReadOnlyList<ToolDefinition>>, string)>
    {
        ["er-diagram-catalog"] = (
            () => ErDiagramToolCatalog.GetDefinitions(),
            "src/QuickER.Mcp/ErDiagramToolCatalog.cs"
        ),
        ["mock-folder-design"] = (
            () => MockFolderDesignTools.GetDefinitions(),
            "src/QuickER.AI.Mock/MockFolderDesignTools.cs"
        ),
        ["mock-emit-file"] = (
            () => MockProjectEmitTools.GetDefinitions(),
            "src/QuickER.AI.Mock/MockProjectEmitTools.cs"
        ),
        ["codegen-toolset"] = (
            () => CodeGenToolSet.Create().Tools,
            "src/QuickER.Cli/CodeGenToolSet.cs"
        ),
    };

    /// <summary>Theory へ流す出どころの ID</summary>
    public static TheoryData<string> SourceIds() => [.. Sources.Keys];

    /// <summary>ツール定義の一覧の名前・説明・入力スキーマ JSON 全文に CJK が無いことを検証する</summary>
    [Theory(DisplayName = "ツール定義に日本語（CJK）が含まれない")]
    [MemberData(nameof(SourceIds))]
    public void Definitions_ContainNoCjk(string source)
    {
        var (definitions, sourcePath) = Sources[source];
        var list = definitions();

        list.Should().NotBeEmpty("走査対象のツール定義が 1 つも見つからない");

        var findings = new List<string>();

        foreach (var definition in list)
        {
            var schemaJson = JsonSerializer.Serialize(definition.InputSchema);
            var whole = $"{definition.Name}\n{definition.Description}\n{schemaJson}";

            if (TestSupport.CjkText.Pattern.IsMatch(whole))
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
