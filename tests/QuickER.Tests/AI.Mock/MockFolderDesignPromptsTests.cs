using System.Globalization;
using FluentAssertions;
using QuickER.AI.Mock;

namespace QuickER.Tests.AI.Mock;

/// <summary>
/// <see cref="MockFolderDesignPrompts"/> のシステムプロンプト／Codex 指示が、モックフォルダ方式の核心要素
/// （提案合意フロー・save_screen / get_screen / style.css）を含み、表示言語に追従することを検証するテストクラス。
/// </summary>
/// <remarks>
/// カルチャ切替は静的 <c>Strings.Culture</c> を変更せず、スレッドローカルの
/// <see cref="CultureInfo.CurrentUICulture"/> を try/finally で一時変更・復元して行う（tasks/lessons.md の方針）。
/// </remarks>
public class MockFolderDesignPromptsTests
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

    /// <summary>英語プロンプトに核心キーワード（ツール名・style.css・提案合意フロー）が含まれることを検証する</summary>
    [Fact(DisplayName = "英語プロンプトに核心キーワードが含まれる")]
    public void SystemPrompt_English_ContainsCoreKeywords()
    {
        var prompt = WithCulture("en", MockFolderDesignPrompts.BuildSystemPrompt);

        prompt.Should().Contain("save_screen");
        prompt.Should().Contain("save_stylesheet");
        prompt.Should().Contain("get_screen");
        prompt.Should().Contain("style.css");
        // 提案→合意フローの明示
        prompt.Should().Contain("propose");
        prompt.Should().Contain("agree");
        // ツール機構ラベルの差し替え（{0}）が解決されていること
        prompt.Should().Contain("function tools");
        prompt.Should().NotContain("{0}");
    }

    /// <summary>日本語プロンプトに核心キーワードが含まれることを検証する</summary>
    [Fact(DisplayName = "日本語プロンプトに核心キーワードが含まれる")]
    public void SystemPrompt_Japanese_ContainsCoreKeywords()
    {
        var prompt = WithCulture("ja", MockFolderDesignPrompts.BuildSystemPrompt);

        prompt.Should().Contain("save_screen");
        prompt.Should().Contain("get_screen");
        prompt.Should().Contain("style.css");
        prompt.Should().Contain("提案");
        prompt.Should().Contain("合意");
        prompt.Should().Contain("関数ツール");
        prompt.Should().NotContain("{0}");
    }

    /// <summary>Codex 指示は dynamicTools ラベルへ差し替えられ、本文はシステムプロンプトと同旨であることを検証する</summary>
    [Fact(DisplayName = "Codex 指示は dynamicTools ラベルを用いる")]
    public void CodexInstructions_UseDynamicToolsLabel()
    {
        var codex = WithCulture("en", MockFolderDesignPrompts.BuildCodexDeveloperInstructions);

        codex.Should().Contain("dynamicTools");
        codex.Should().Contain("save_screen");
        codex.Should().NotContain("{0}");
    }
}
