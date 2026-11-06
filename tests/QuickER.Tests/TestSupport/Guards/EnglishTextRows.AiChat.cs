using QuickER.AI.Chat;
using QuickER.Mcp.Tools;
using QuickER.Tests.AI.Chat;

namespace QuickER.Tests.TestSupport.Guards;

/// <summary>英語の文言の表：AiChat（内蔵チャットのクエリツールの応答の英語版）</summary>
internal static partial class EnglishTextRows
{
    /// <summary>英語の UI 言語</summary>
    private const string EnglishUi = "en";

    private static IEnumerable<EnglishTextRow> AiChat() =>
        [
            // 内蔵チャット面のフォーマッタは UI 言語に追従する（日本語 UI で日本語が出ることは要求しない）。英語 UI の出力に日本語の文言が
            // 混ざらないことを、全診断種別で見る（全種別に英日の文言があることは QueryToolDiagnosticCoverageTests が見る）
            new(
                "query-tool-localized-formatter-english",
                "内蔵チャットのクエリツールの応答（QueryToolLocalizedFormatter）は英語 UI では英語（全診断種別。英語 UI で作った出力を見る）",
                () =>
                    [
                        .. Enum.GetValues<QueryToolDiagnosticCode>()
                            .Select(code => new EnglishText(
                                $"diagnostic {code}",
                                UnderUiCulture(
                                    EnglishUi,
                                    () =>
                                        QueryToolLocalizedFormatter.FormatSetQuery(
                                            QueryToolDiagnosticCoverageTests.OutcomeWith(code)
                                        )
                                )
                            )),
                    ]
            ),
        ];
}
