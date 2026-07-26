using System.IO;
using FluentAssertions;
using QuickER.AI;

namespace QuickER.Tests.AI;

/// <summary>
/// <see cref="AiSettingsStore"/> の保存・読込（各セクションの往復・欠損/破損時の既定フォールバック・
/// ダイアログ別 UI 状態の分離）と、<see cref="ChatUiSettings.ParseLastBackend"/> /
/// <see cref="ChatUiSettings.ParseApiProvider"/> の解釈を検証するテストクラス。
/// </summary>
/// <remarks>
/// 旧構成（ChatUi / Codex / Claude Code / モデル履歴を別ファイルの個別ストアで持つ）を
/// 1 ファイル <c>ai-settings.json</c> のセクションへ統合した後の等価な振る舞いを守る。
/// </remarks>
public class AiSettingsStoreTests
{
    /// <summary>一時フォルダに隔離したストアを生成する</summary>
    private static (AiSettingsStore store, string folder) CreateStore()
    {
        var folder = Path.Combine(Path.GetTempPath(), "QuickERTests", Guid.NewGuid().ToString("N"));
        return (new AiSettingsStore(folder), folder);
    }

    private static void Cleanup(string folder)
    {
        if (Directory.Exists(folder))
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>すべてのセクション（UI 状態・Codex・Claude Code・モデル履歴）が往復で保持されることを検証する</summary>
    [Fact(DisplayName = "全セクションが Save→Load で復元される")]
    public void SaveAndLoad_RoundTripsAllSections()
    {
        var (store, folder) = CreateStore();

        try
        {
            var expected = new AiSettings
            {
                ChatUi = new ChatUiSettings { LastBackend = "ClaudeCode" },
                MockUi = new ChatUiSettings { LastBackend = "Codex" },
                ClaudeCode = new ClaudeCodeSettings { Model = "sonnet" },
                CodexAppServer = new CodexAppServerSettings
                {
                    ModelProvider = "ollama-launch",
                    Model = "gemma4:31b-cloud",
                },
            };
            expected.ApiModelHistory.Touch("openai", "custom-openai");
            expected.CodexModelHistory.Touch("ollama-launch", "gemma4:31b-cloud");

            store.Save(expected);

            var actual = store.Load();
            actual.ChatUi.LastBackend.Should().Be("ClaudeCode");
            actual.MockUi.LastBackend.Should().Be("Codex");
            actual.ClaudeCode.Model.Should().Be("sonnet");
            actual.CodexAppServer.ModelProvider.Should().Be("ollama-launch");
            actual.CodexAppServer.Model.Should().Be("gemma4:31b-cloud");
            actual.ApiModelHistory.ModelsFor("openai").Should().Equal("custom-openai");
            actual.CodexModelHistory.ModelsFor("ollama-launch").Should().Equal("gemma4:31b-cloud");
        }
        finally
        {
            Cleanup(folder);
        }
    }

    /// <summary>UiFor が種別ごとに別セクション（ChatUi / MockUi）を返し、互いに独立していることを検証する</summary>
    [Fact(DisplayName = "UiFor はダイアログ種別ごとに別セクションを返す")]
    public void UiFor_SelectsSectionPerDialogKind()
    {
        var settings = new AiSettings();
        settings.UiFor(AiDialogKind.AiChat).LastBackend = "ClaudeCode";
        settings.UiFor(AiDialogKind.MockGeneration).LastBackend = "Codex";

        settings.ChatUi.LastBackend.Should().Be("ClaudeCode");
        settings.MockUi.LastBackend.Should().Be("Codex");
        settings.UiFor(AiDialogKind.AiChat).LastBackend.Should().Be("ClaudeCode");
        settings.UiFor(AiDialogKind.MockGeneration).LastBackend.Should().Be("Codex");
    }

    /// <summary>ファイルが無ければ既定値（各セクション初期状態）を返すことを検証する</summary>
    [Fact(DisplayName = "ファイルが無ければ既定値を返す")]
    public void Load_ReturnsDefault_WhenFileMissing()
    {
        var (store, folder) = CreateStore();

        try
        {
            var loaded = store.Load();
            loaded.ChatUi.LastBackend.Should().BeEmpty();
            loaded.MockUi.LastBackend.Should().BeEmpty();
            loaded.ClaudeCode.Model.Should().BeEmpty();
            loaded.CodexAppServer.ModelProvider.Should().BeEmpty();
            loaded.ApiModelHistory.Providers.Should().BeEmpty();
            loaded.CodexModelHistory.Providers.Should().BeEmpty();
        }
        finally
        {
            Cleanup(folder);
        }
    }

    /// <summary>破損ファイルは既定値へフォールバックすることを検証する</summary>
    [Fact(DisplayName = "破損ファイルは既定値へフォールバックする")]
    public void Load_ReturnsDefault_WhenFileBroken()
    {
        var (store, folder) = CreateStore();

        try
        {
            Directory.CreateDirectory(folder);
            File.WriteAllText(store.SettingsPath, "{ broken json");

            store.Load().ChatUi.LastBackend.Should().BeEmpty();
        }
        finally
        {
            Cleanup(folder);
        }
    }

    /// <summary>片方のダイアログのセクション保存が、他方のセクションを消さないことを検証する（read-modify-write の前提）</summary>
    [Fact(DisplayName = "セクション別保存は他セクションを消さない")]
    public void Save_PreservesOtherSections()
    {
        var (store, folder) = CreateStore();

        try
        {
            // 先に mock 側のみを書く
            var first = store.Load();
            first.MockUi.LastBackend = "Codex";
            store.Save(first);

            // 後から chat 側を read-modify-write で書く（mock 側は温存されるべき）
            var second = store.Load();
            second.ChatUi.LastBackend = "ClaudeCode";
            store.Save(second);

            var reloaded = store.Load();
            reloaded.ChatUi.LastBackend.Should().Be("ClaudeCode");
            reloaded.MockUi.LastBackend.Should().Be("Codex");
        }
        finally
        {
            Cleanup(folder);
        }
    }

    [Theory(DisplayName = "ParseLastBackend は名前を大文字小文字を無視して解釈する")]
    [InlineData("ApiKey", ErChatBackendKind.ApiKey)]
    [InlineData("codex", ErChatBackendKind.Codex)]
    [InlineData("CLAUDECODE", ErChatBackendKind.ClaudeCode)]
    public void ParseLastBackend_ParsesKnownNames(string value, ErChatBackendKind expected)
    {
        new ChatUiSettings { LastBackend = value }
            .ParseLastBackend()
            .Should()
            .Be(expected);
    }

    [Theory(DisplayName = "ParseLastBackend は空・不正値を null にする")]
    [InlineData("")]
    [InlineData("Unknown")]
    public void ParseLastBackend_ReturnsNull_ForInvalidValues(string value)
    {
        new ChatUiSettings { LastBackend = value }
            .ParseLastBackend()
            .Should()
            .BeNull();
    }

    [Theory(DisplayName = "ParseApiProvider は名前を大文字小文字を無視して解釈する")]
    [InlineData("OpenAI", AiProvider.OpenAI)]
    [InlineData("claude", AiProvider.Claude)]
    [InlineData("LOCALLLM", AiProvider.LocalLlm)]
    public void ParseApiProvider_ParsesKnownNames(string value, AiProvider expected)
    {
        new ChatUiSettings { ApiProvider = value }
            .ParseApiProvider()
            .Should()
            .Be(expected);
    }

    /// <summary>空・未知の名前・定義外の数値文字列がいずれも null（＝呼び出し側で既定へフォールバック）になることを検証する</summary>
    [Theory(DisplayName = "ParseApiProvider は空・不正値・定義外の数値を null にする")]
    [InlineData("")]
    [InlineData("Gemini")]
    [InlineData("99")]
    public void ParseApiProvider_ReturnsNull_ForInvalidValues(string value)
    {
        new ChatUiSettings { ApiProvider = value }
            .ParseApiProvider()
            .Should()
            .BeNull();
    }

    /// <summary>API キー接続の選択（プロバイダー・エンドポイント）がダイアログ別セクションで往復することを検証する</summary>
    [Fact(DisplayName = "プロバイダー・エンドポイントはダイアログ別に往復する")]
    public void SaveLoad_ApiProviderAndEndpoint_RoundTripPerDialog()
    {
        var (store, folder) = CreateStore();

        try
        {
            var settings = store.Load();
            settings.ChatUi.ApiProvider = nameof(AiProvider.LocalLlm);
            settings.ChatUi.EndpointOverride = "http://127.0.0.1:1234/v1";
            settings.MockUi.ApiProvider = nameof(AiProvider.Claude);
            store.Save(settings);

            var actual = new AiSettingsStore(folder).Load();

            actual.ChatUi.ParseApiProvider().Should().Be(AiProvider.LocalLlm);
            actual.ChatUi.EndpointOverride.Should().Be("http://127.0.0.1:1234/v1");
            actual.MockUi.ParseApiProvider().Should().Be(AiProvider.Claude);
            actual.MockUi.EndpointOverride.Should().BeEmpty();
        }
        finally
        {
            Cleanup(folder);
        }
    }
}
