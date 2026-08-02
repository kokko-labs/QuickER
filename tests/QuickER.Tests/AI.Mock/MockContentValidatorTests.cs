using AwesomeAssertions;
using QuickER.AI.Mock;

namespace QuickER.Tests.AI.Mock;

/// <summary>
/// <see cref="MockContentValidator"/> の外部参照検出・リンク実在・遷移宣言整合・共有 CSS 参照の検証をテストする。
/// </summary>
public class MockContentValidatorTests
{
    private const string StyleLink = "<link rel=\"stylesheet\" href=\"style.css\">";

    private static string Screen(string body) =>
        $"<!DOCTYPE html><html><head>{StyleLink}</head><body>{body}</body></html>";

    private static IReadOnlyList<string> ValidateScreen(
        string html,
        IReadOnlyList<MockTransition>? transitions = null,
        params string[] knownScreens
    ) =>
        MockContentValidator.ValidateScreen(
            "Current.html",
            html,
            transitions ?? Array.Empty<MockTransition>(),
            knownScreens
        );

    [Fact(DisplayName = "外部 src/href の絶対 URL を警告する")]
    public void DetectsExternalSrcAndHref()
    {
        var html = Screen(
            "<img src=\"https://cdn.example.com/a.png\">"
                + "<a href=\"http://example.com\">外部</a>"
        );

        var warnings = ValidateScreen(html);

        warnings.Should().Contain(w => w.Contains("https://cdn.example.com/a.png"));
        warnings.Should().Contain(w => w.Contains("http://example.com"));
    }

    [Fact(DisplayName = "外部 url() と @import を警告する")]
    public void DetectsExternalCssUrlAndImport()
    {
        var css =
            "@import \"https://fonts.example.com/f.css\";"
            + " body { background: url('https://img.example.com/bg.png'); }";

        var warnings = MockContentValidator.ValidateStylesheet(css);

        warnings.Should().Contain(w => w.Contains("https://fonts.example.com/f.css"));
        warnings.Should().Contain(w => w.Contains("https://img.example.com/bg.png"));
    }

    [Fact(DisplayName = "外部参照なしなら外部参照の警告は出ない")]
    public void NoExternalReference_NoWarning()
    {
        var html = Screen("<a href=\"Other.html\">遷移</a>");

        var warnings = ValidateScreen(html, knownScreens: new[] { "Other.html" });

        warnings.Should().NotContain(w => w.Contains("External reference"));
    }

    [Fact(DisplayName = "既知画面へのリンクは警告しない")]
    public void KnownLinkTarget_NoWarning()
    {
        var html = Screen("<a href=\"OrderDetail.html\">詳細</a>");

        var warnings = ValidateScreen(html, knownScreens: new[] { "OrderDetail.html" });

        warnings.Should().NotContain(w => w.Contains("Link target"));
    }

    [Fact(DisplayName = "遷移で予告済みのリンク先は警告しない")]
    public void TransitionPredeclaredTarget_NoWarning()
    {
        var html = Screen("<a href=\"OrderNew.html\">新規</a>");
        var transitions = new[]
        {
            new MockTransition { From = "Current.html", To = "OrderNew.html" },
        };

        var warnings = ValidateScreen(html, transitions);

        warnings.Should().NotContain(w => w.Contains("Link target"));
    }

    [Fact(DisplayName = "未知のリンク先は警告する")]
    public void UnknownLinkTarget_Warns()
    {
        var html = Screen("<a href=\"Ghost.html\">存在しない</a>");

        var warnings = ValidateScreen(html);

        warnings.Should().Contain(w => w.Contains("Link target") && w.Contains("Ghost.html"));
    }

    [Fact(DisplayName = "遷移 To が既存にもリンクにもなければ警告する")]
    public void TransitionTargetNotLinked_Warns()
    {
        var html = Screen("<h1>本文のみ・リンクなし</h1>");
        var transitions = new[]
        {
            new MockTransition { From = "Current.html", To = "Dangling.html" },
        };

        var warnings = ValidateScreen(html, transitions);

        warnings
            .Should()
            .Contain(w => w.Contains("Transition target") && w.Contains("Dangling.html"));
    }

    [Fact(DisplayName = "共有 CSS 未参照を警告する")]
    public void MissingStylesheetLink_Warns()
    {
        var html = "<!DOCTYPE html><html><head></head><body><h1>x</h1></body></html>";

        var warnings = ValidateScreen(html);

        warnings.Should().Contain(w => w.Contains("style.css"));
    }

    [Fact(DisplayName = "共有 CSS 参照ありなら CSS 警告は出ない")]
    public void HasStylesheetLink_NoWarning()
    {
        var html = Screen("<h1>x</h1>");

        var warnings = ValidateScreen(html);

        warnings.Should().NotContain(w => w.Contains("shared stylesheet"));
    }
}
