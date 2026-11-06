using AwesomeAssertions;
using QuickER.AI.Chat;
using QuickER.Mcp.Tools;
using QuickER.Model;
using QuickER.Tests.TestSupport.Guards;

namespace QuickER.Tests.AI.Chat;

/// <summary>
/// 名前付きクエリツールの診断種別（<see cref="QueryToolDiagnosticCode"/>）がすべて、MCP 面の英語フォーマッタ
/// （<see cref="QueryToolEnglishFormatter"/>）と内蔵チャット面のフォーマッタ（<see cref="QueryToolLocalizedFormatter"/>）の
/// 両方に文言を持つことを全数で検証する
/// </summary>
/// <remarks>
/// 種別を足してフォーマッタの片方へ書き忘れると、既定の分岐が「想定外の診断」として種別名をそのまま出す。
/// その種別名が出ていないことを見る。英語フォーマッタは日本語 UI でも英語のままであること・内蔵チャットのフォーマッタの英語 UI の
/// 出力に日本語が無いことは、英語の文言の表（<c>PhraseTests</c> の英語の行 <c>query-tool-english-formatter</c>・
/// <c>query-tool-localized-formatter-english</c>）が全種別で見る
/// （resx の英日のキーの対応は照合の表の行 <c>resx-ja-key-sets</c> が見る）
/// </remarks>
public class QueryToolDiagnosticCoverageTests
{
    /// <summary>全診断種別</summary>
    public static TheoryData<QueryToolDiagnosticCode> AllCodes =>
        new(Enum.GetValues<QueryToolDiagnosticCode>());

    /// <summary>指定種別の診断 1 件を警告に持つ set_query 成功結果（プレースホルダは全部埋める）</summary>
    internal static QueryToolOutcome OutcomeWith(QueryToolDiagnosticCode code) =>
        new()
        {
            Status = QueryToolStatus.Success,
            TableName = "orders",
            QueryName = "ListAll",
            Returns = QueryReturnShape.List,
            Implementation = QueryImplementationKind.Dsl,
            Warnings =
            [
                new QueryToolDiagnostic(
                    code,
                    Name: "p",
                    Column: "c",
                    Table: "orders",
                    Dialect: "sqlite",
                    Detail: "dsl"
                ),
            ],
        };

    [Theory(DisplayName = "英語フォーマッタは全診断種別に文言を持つ（日本語 UI で作る）")]
    [MemberData(nameof(AllCodes))]
    public void EnglishFormatter_HasTextForEveryCode(QueryToolDiagnosticCode code)
    {
        var text = EnglishTextRows.UnderUiCulture(
            EnglishTextRows.JapaneseUi,
            () => QueryToolEnglishFormatter.FormatSetQuery(OutcomeWith(code))
        );

        text.Should().NotContain(code.ToString());
    }

    [Theory(DisplayName = "内蔵チャットのフォーマッタは全診断種別に英日の文言を持つ")]
    [MemberData(nameof(AllCodes))]
    public void LocalizedFormatter_HasTextForEveryCode(QueryToolDiagnosticCode code)
    {
        var english = EnglishTextRows.UnderUiCulture(
            "en",
            () => QueryToolLocalizedFormatter.FormatSetQuery(OutcomeWith(code))
        );
        var japanese = EnglishTextRows.UnderUiCulture(
            EnglishTextRows.JapaneseUi,
            () => QueryToolLocalizedFormatter.FormatSetQuery(OutcomeWith(code))
        );

        english.Should().NotContain(code.ToString());
        japanese.Should().NotContain(code.ToString());
    }
}
