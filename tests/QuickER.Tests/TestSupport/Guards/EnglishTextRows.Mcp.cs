using System.Globalization;
using System.Text.Json;
using QuickER.AI.Mock;
using QuickER.Cli;
using QuickER.Mcp;
using QuickER.Mcp.Tools;
using QuickER.Tests.AI.Chat;
using QuickER.Tests.Mcp.Tools;

namespace QuickER.Tests.TestSupport.Guards;

/// <summary>英語の文言の表：Mcp（ツール定義・サーバ使用指針・MCP 面のクエリツールの応答）</summary>
internal static partial class EnglishTextRows
{
    /// <summary>英語固定の出力を作るときに当てる UI 言語（resx の日本語サテライトが混ざる経路を通す）</summary>
    internal const string JapaneseUi = "ja";

    private static IEnumerable<EnglishTextRow> Mcp() =>
        [
            // 外部 AI エージェント（Claude Code / Codex 等）向けの MCP サーバと、内蔵チャット・モック生成が LLM へ渡すツール定義は英語が正本。
            // 和文が混入すると、意図せず日本語で提示され、回答の言語も引きずられる。定義を JSON 化した全文（名前・説明・入力スキーマ）を見る。
            // 行はツール定義の出どころごと。コード生成ツールは実際に公開される CodeGenToolSet.Tools（file 注入後）＝サーバが送る形そのもの
            ToolDefinitionRow(
                "tool-definitions-er-diagram-catalog",
                () => ErDiagramToolCatalog.GetDefinitions(),
                "src/QuickER.Mcp/ErDiagramToolCatalog.cs"
            ),
            ToolDefinitionRow(
                "tool-definitions-mock-folder-design",
                () => MockFolderDesignTools.GetDefinitions(),
                "src/QuickER.AI.Mock/MockFolderDesignTools.cs"
            ),
            ToolDefinitionRow(
                "tool-definitions-mock-emit-file",
                () => MockProjectEmitTools.GetDefinitions(),
                "src/QuickER.AI.Mock/MockProjectEmitTools.cs"
            ),
            ToolDefinitionRow(
                "tool-definitions-codegen-toolset",
                () => CodeGenToolSet.Create().Tools,
                "src/QuickER.Cli/CodeGenToolSet.cs"
            ),
            new(
                "mcp-server-instructions",
                "サーバ使用指針（ErDiagramToolCatalog.ServerInstructions）は外部エージェント向けの英語正本"
                    + "（src/QuickER.Mcp/ErDiagramToolCatalog.cs を確認する）",
                () => [new("ServerInstructions", ErDiagramToolCatalog.ServerInstructions)]
            ),
            // 外部 AI エージェント向け stdio MCP サーバはヘッドレス実行＝英語固定が正本。診断が resx 経由でローカライズされる都合上、
            // 日本語 OS では英語応答に日本語診断が混ざる回帰が起きやすい。UI 言語を ja にして、DSL パーサ・生 SQL アナライザ由来の診断と
            // 全診断種別の文言を英語フォーマッタへ流す（英語の文言が出ることは QueryToolEnglishFormatterLanguageTests、
            // 全種別に文言があることは QueryToolDiagnosticCoverageTests が見る）
            new(
                "query-tool-english-formatter",
                "MCP 面のクエリツールの応答（QueryToolEnglishFormatter）は UI 言語に依らず英語（日本語 UI で作った出力を見る）",
                () =>
                    [
                        new(
                            "set_query condition diagnostic",
                            QueryToolEnglishFormatterLanguageTests.ConditionDiagnosticUnderJapaneseUi()
                        ),
                        new(
                            "set_query raw SQL diagnostic",
                            QueryToolEnglishFormatterLanguageTests.RawSqlDiagnosticUnderJapaneseUi()
                        ),
                        .. Enum.GetValues<QueryToolDiagnosticCode>()
                            .Select(code => new EnglishText(
                                $"diagnostic {code}",
                                UnderUiCulture(
                                    JapaneseUi,
                                    () =>
                                        QueryToolEnglishFormatter.FormatSetQuery(
                                            QueryToolDiagnosticCoverageTests.OutcomeWith(code)
                                        )
                                )
                            )),
                    ]
            ),
        ];

    /// <summary>ツール定義の出どころ 1 つの行（どこか＝ツール名、本文＝名前・説明・入力スキーマの JSON。日本語 UI で取得する）</summary>
    private static EnglishTextRow ToolDefinitionRow(
        string id,
        Func<IReadOnlyList<ToolDefinition>> definitions,
        string sourcePath
    ) =>
        new(
            id,
            "ツール定義（名前・説明・入力スキーマの description）は英語で統一する"
                + $"（{sourcePath} を確認する）",
            () =>
                UnderUiCulture(
                    JapaneseUi,
                    () =>
                        definitions()
                            .Select(definition => new EnglishText(
                                definition.Name,
                                $"{definition.Name}\n{definition.Description}\n"
                                    + JsonSerializer.Serialize(definition.InputSchema)
                            ))
                            .ToList()
                )
        );

    /// <summary>
    /// UI 言語（<see cref="CultureInfo.CurrentUICulture"/>）を一時的に差し替えて関数を評価し、必ず元へ戻す
    /// </summary>
    /// <remarks>
    /// 文言の表の行と、行が呼ぶテストの関数が共有する唯一の置き場（静的な <c>Strings.Culture</c> は変えない＝スレッドローカルの
    /// UI 言語だけを変えるので、並列のテストへ漏れない）。書式のカルチャ（CurrentCulture）は変えない。
    /// </remarks>
    internal static T UnderUiCulture<T>(string culture, Func<T> body)
    {
        var previous = CultureInfo.CurrentUICulture;

        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo(culture);

            return body();
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }
}
