using System.Globalization;
using AwesomeAssertions;
using QuickER.AI.Chat;
using QuickER.Mcp;

namespace QuickER.Tests.AI.Chat;

/// <summary>
/// 設計既定ルール（命名規則・単一主キー・FK 手順・要約先読み）が、内蔵チャットのシステムプロンプト
/// （<see cref="ErDesignRules"/>・resx 対訳）と外部 MCP のサーバ使用指針
/// （<see cref="ErDiagramToolCatalog.ServerInstructions"/>・英語正本）の両方に存在することを守る整合ガード。
/// </summary>
/// <remarks>
/// 両者は経路が異なるため意図的な二重管理（チャット＝対訳 resx・MCP＝英語固定文）であり、
/// 将来ルールを変更する際に片方だけ更新される事故を検知する。文言の一字一句は比較せず、
/// 核心キーワードの含有のみを検証する（軽い整合テスト）。
/// カルチャ切替は静的 <c>Strings.Culture</c> を変更せず、スレッドローカルの
/// <see cref="CultureInfo.CurrentUICulture"/> を try/finally で一時変更・復元して行う。
/// </remarks>
public class DesignGuidelineParityTests
{
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

    /// <summary>命名既定（パスカルケース）が両正本に存在することを検証する</summary>
    [Fact(DisplayName = "命名既定（パスカルケース）がチャットプロンプトと MCP 指針の両方にある")]
    public void NamingConvention_PresentInBothCanonicalTexts()
    {
        WithCulture("en", ErDesignRules.BuildChatSystemPrompt).Should().Contain("PascalCase");
        WithCulture("ja", ErDesignRules.BuildChatSystemPrompt).Should().Contain("パスカルケース");
        ErDiagramToolCatalog.ServerInstructions.Should().Contain("PascalCase");
    }

    /// <summary>単一主キーのルールが両正本に存在することを検証する</summary>
    [Fact(DisplayName = "単一主キーのルールがチャットプロンプトと MCP 指針の両方にある")]
    public void SinglePrimaryKeyRule_PresentInBothCanonicalTexts()
    {
        WithCulture("en", ErDesignRules.BuildChatSystemPrompt)
            .Should()
            .ContainEquivalentOf("primary key");
        WithCulture("ja", ErDesignRules.BuildChatSystemPrompt).Should().Contain("主キー");
        ErDiagramToolCatalog.ServerInstructions.Should().ContainEquivalentOf("primary key");
    }

    /// <summary>FK 手順（add_relationship）と要約先読み（get_diagram_summary）が両正本に存在することを検証する</summary>
    [Fact(DisplayName = "FK 手順と要約先読みの指針がチャットプロンプトと MCP 指針の両方にある")]
    public void ToolWorkflowGuidelines_PresentInBothCanonicalTexts()
    {
        foreach (var culture in new[] { "en", "ja" })
        {
            var prompt = WithCulture(culture, ErDesignRules.BuildChatSystemPrompt);

            prompt.Should().Contain("add_relationship", $"culture={culture}");
            prompt.Should().Contain("get_diagram_summary", $"culture={culture}");
        }

        ErDiagramToolCatalog.ServerInstructions.Should().Contain("add_relationship");
        ErDiagramToolCatalog.ServerInstructions.Should().Contain("get_diagram_summary");
    }
}
