using System.Globalization;
using System.Text.RegularExpressions;
using AwesomeAssertions;
using QuickER.AI.Chat;
using QuickER.Mcp.Tools;
using QuickER.Model;

namespace QuickER.Tests.AI.Chat;

/// <summary>
/// 名前付きクエリツールの診断種別（<see cref="QueryToolDiagnosticCode"/>）がすべて、MCP 面の英語フォーマッタ
/// （<see cref="QueryToolEnglishFormatter"/>）と内蔵チャット面のフォーマッタ（<see cref="QueryToolLocalizedFormatter"/>）の
/// 両方に文言を持つことを全数で検証する
/// </summary>
/// <remarks>
/// 種別を足してフォーマッタの片方へ書き忘れると、既定の分岐が「想定外の診断」として種別名をそのまま出す。
/// その種別名が出ていないことと、英語フォーマッタは日本語 UI でも英語のままであることを見る
/// （resx の英日のキーの対応は Resources/ のパリティテストが見る）
/// </remarks>
public class QueryToolDiagnosticCoverageTests
{
    /// <summary>CJK 文字の検出パターン（ErDiagramToolCatalogEnglishGuardTests と同じ範囲）</summary>
    private static readonly Regex CjkPattern = TestSupport.CjkText.Pattern;

    /// <summary>全診断種別</summary>
    public static TheoryData<QueryToolDiagnosticCode> AllCodes =>
        new(Enum.GetValues<QueryToolDiagnosticCode>());

    /// <summary>指定カルチャを CurrentUICulture に設定して関数を評価し、必ず元へ復元する</summary>
    private static T WithCulture<T>(string culture, Func<T> body)
    {
        var previousUi = CultureInfo.CurrentUICulture;

        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo(culture);

            return body();
        }
        finally
        {
            CultureInfo.CurrentUICulture = previousUi;
        }
    }

    /// <summary>指定種別の診断 1 件を警告に持つ set_query 成功結果（プレースホルダは全部埋める）</summary>
    private static QueryToolOutcome OutcomeWith(QueryToolDiagnosticCode code) =>
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

    [Theory(DisplayName = "英語フォーマッタは全診断種別に文言を持ち、日本語 UI でも英語で出す")]
    [MemberData(nameof(AllCodes))]
    public void EnglishFormatter_HasTextForEveryCode(QueryToolDiagnosticCode code)
    {
        var text = WithCulture(
            "ja",
            () => QueryToolEnglishFormatter.FormatSetQuery(OutcomeWith(code))
        );

        text.Should().NotContain(code.ToString());
        CjkPattern.IsMatch(text).Should().BeFalse(text);
    }

    [Theory(DisplayName = "内蔵チャットのフォーマッタは全診断種別に英日の文言を持つ")]
    [MemberData(nameof(AllCodes))]
    public void LocalizedFormatter_HasTextForEveryCode(QueryToolDiagnosticCode code)
    {
        var english = WithCulture(
            "en",
            () => QueryToolLocalizedFormatter.FormatSetQuery(OutcomeWith(code))
        );
        var japanese = WithCulture(
            "ja",
            () => QueryToolLocalizedFormatter.FormatSetQuery(OutcomeWith(code))
        );

        english.Should().NotContain(code.ToString());
        japanese.Should().NotContain(code.ToString());
        CjkPattern.IsMatch(english).Should().BeFalse(english);
    }
}
