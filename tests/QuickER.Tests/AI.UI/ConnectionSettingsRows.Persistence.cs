using System.IO;
using AwesomeAssertions;
using QuickER.AI;
using QuickER.Tests.TestSupport.Engines;
using static QuickER.Tests.AI.UI.ConnectionArea;
using static QuickER.Tests.AI.UI.ConnectionOp;

namespace QuickER.Tests.AI.UI;

internal static partial class ConnectionSettingsRows
{
    /// <summary>保存と読み込みの往復・旧形式・ダイアログ間の共有と独立</summary>
    private static IEnumerable<ScenarioRow<ConnectionSettingsCase>> PersistenceRows() =>
        [
            Row(
                "common-save-load-round-trip-restores-state",
                "SaveSettings→LoadSettings 往復で設定が復元される",
                [(Codex, Save), (Codex, Load), (ClaudeCode, Save), (ClaudeCode, Load)],
                h =>
                {
                    var config = new CodexConfigToml
                    {
                        ProviderNames = new List<string> { "ollama-launch" },
                    };

                    h.CodexConfig = () => config;
                    var vm = h.Create();
                    vm.LoadSettings();
                    vm.SelectedBackend = ErChatBackendKind.ClaudeCode;
                    vm.CodexModelProvider = "ollama-launch";
                    vm.CodexModel = "gemma4:31b-cloud";
                    vm.ClaudeCodeModel = "opus";
                    h.SaveSettings(vm);

                    // 同じファイル名・フォルダで別インスタンスへ読み戻す
                    var restored = h.Create();
                    restored.LoadSettings();

                    restored.InitialBackend.Should().Be(ErChatBackendKind.ClaudeCode);
                    restored.CodexModelProvider.Should().Be("ollama-launch");
                    restored.CodexModel.Should().Be("gemma4:31b-cloud");
                    restored.ClaudeCodeModel.Should().Be("opus");
                }
            ),
            Row(
                "apikey-save-persists-provider-and-endpoint",
                "SaveSettings でプロバイダーとエンドポイントが保存される",
                [(ApiKey, Save)],
                h =>
                {
                    var vm = h.Create();
                    vm.LoadSettings();
                    vm.ApiProvider = AiProvider.LocalLlm;
                    vm.EndpointOverride = "  http://127.0.0.1:1234/v1  ";

                    h.SaveSettings(vm);

                    var ui = h.Store().Load().UiFor(AiDialogKind.AiChat);
                    ui.ApiProvider.Should().Be(nameof(AiProvider.LocalLlm));

                    // 前後の空白は落として保存する（復元時にそのまま URL として使えるようにする）
                    ui.EndpointOverride.Should().Be("http://127.0.0.1:1234/v1");
                }
            ),
            Row(
                "apikey-load-restores-provider-and-endpoint",
                "LoadSettings でプロバイダーとエンドポイントが復元される",
                [(ApiKey, Save), (ApiKey, Load)],
                h =>
                {
                    var vm = h.Create();
                    vm.LoadSettings();
                    vm.ApiProvider = AiProvider.LocalLlm;
                    vm.EndpointOverride = "http://127.0.0.1:1234/v1";
                    h.SaveSettings(vm);

                    var restored = h.Create();
                    restored.LoadSettings();

                    restored.ApiProvider.Should().Be(AiProvider.LocalLlm);
                    restored.EndpointOverride.Should().Be("http://127.0.0.1:1234/v1");
                    restored.EffectiveEndpointOverride.Should().Be("http://127.0.0.1:1234/v1");
                    restored.ShowEndpoint.Should().BeTrue();
                }
            ),
            Row(
                "apikey-load-local-llm-without-endpoint-fills-default",
                "エンドポイント未保存の Local LLM 復元では既定 URL が補完される",
                [(ApiKey, Load)],
                h =>
                {
                    var seeded = h.Store().Load();
                    seeded.ChatUi.ApiProvider = nameof(AiProvider.LocalLlm);
                    h.Store().Save(seeded);

                    var vm = h.Create();
                    vm.LoadSettings();

                    vm.ApiProvider.Should().Be(AiProvider.LocalLlm);
                    vm.EndpointOverride.Should().Be(LocalLlmDefaults.Endpoint);
                }
            ),
            Row(
                "apikey-unknown-saved-provider-falls-back-to-openai-gemini",
                "未知の保存プロバイダーは OpenAI へフォールバック",
                [(ApiKey, Load)],
                h => UnknownSavedProviderFallsBackToOpenAi(h, "gemini")
            ),
            Row(
                "apikey-unknown-saved-provider-falls-back-to-openai-99",
                "未知の保存プロバイダーは OpenAI へフォールバック",
                [(ApiKey, Load)],
                h => UnknownSavedProviderFallsBackToOpenAi(h, "99")
            ),
            Row(
                "apikey-unknown-saved-provider-falls-back-to-openai-empty",
                "未知の保存プロバイダーは OpenAI へフォールバック",
                [(ApiKey, Load)],
                h => UnknownSavedProviderFallsBackToOpenAi(h, "")
            ),
            Row(
                "common-load-legacy-json-without-new-keys-uses-defaults",
                "新キーなしの旧 JSON は既定値で読める",
                [(Common, Load), (ApiKey, Load)],
                h =>
                {
                    // apiProvider / endpointOverride を持たない旧世代のファイルを直接書く
                    Directory.CreateDirectory(h.Folder);
                    File.WriteAllText(
                        h.SettingsPath,
                        """
                        {
                          "chatUi": { "lastBackend": "ClaudeCode" }
                        }
                        """
                    );

                    var vm = h.Create();
                    vm.LoadSettings();

                    vm.InitialBackend.Should().Be(ErChatBackendKind.ClaudeCode);
                    vm.ApiProvider.Should().Be(AiProvider.OpenAI);
                    vm.EndpointOverride.Should().BeEmpty();
                    vm.EffectiveEndpointOverride.Should().BeNull();
                }
            ),
            Row(
                "apikey-provider-and-endpoint-isolated-per-dialog",
                "プロバイダーとエンドポイントは chat / mock で独立する",
                [(ApiKey, Save), (ApiKey, Load)],
                h =>
                {
                    var chat = h.Create(AiDialogKind.AiChat);
                    chat.LoadSettings();
                    chat.ApiProvider = AiProvider.LocalLlm;
                    chat.EndpointOverride = "http://127.0.0.1:1234/v1";
                    h.SaveSettings(chat);

                    var mock = h.Create(AiDialogKind.MockGeneration);
                    mock.LoadSettings();
                    mock.ApiProvider = AiProvider.Claude;
                    h.SaveSettings(mock);

                    var restoredChat = h.Create(AiDialogKind.AiChat);
                    restoredChat.LoadSettings();
                    restoredChat.ApiProvider.Should().Be(AiProvider.LocalLlm);
                    restoredChat.EndpointOverride.Should().Be("http://127.0.0.1:1234/v1");

                    var restoredMock = h.Create(AiDialogKind.MockGeneration);
                    restoredMock.LoadSettings();
                    restoredMock.ApiProvider.Should().Be(AiProvider.Claude);
                    restoredMock.EndpointOverride.Should().BeEmpty();
                }
            ),
            Row(
                "common-assistant-markdown-defaults-to-on",
                "回答の Markdown 表示は既定でオンで、キーの無い既存の設定もオンで読む",
                [(Common, Load)],
                h =>
                {
                    var fresh = h.Create();
                    fresh.LoadSettings();
                    fresh.ShowAssistantMarkdown.Should().BeTrue("設定ファイルが無いときの既定");

                    // この機能より前の設定ファイル（UI 状態のセクションにキーが無い）を置く
                    Directory.CreateDirectory(h.Folder);
                    File.WriteAllText(
                        h.SettingsPath,
                        "{\"chatUi\":{\"lastBackend\":\"Codex\"},\"mockUi\":{}}"
                    );

                    var chat = h.Create(AiDialogKind.AiChat);
                    chat.LoadSettings();
                    chat.InitialBackend.Should()
                        .Be(ErChatBackendKind.Codex, "置いた設定ファイルが読まれている");
                    chat.ShowAssistantMarkdown.Should().BeTrue();

                    var mock = h.Create(AiDialogKind.MockGeneration);
                    mock.LoadSettings();
                    mock.ShowAssistantMarkdown.Should().BeTrue();
                }
            ),
            Row(
                "common-assistant-markdown-isolated-per-dialog",
                "回答の Markdown 表示の選択は chat / mock で独立して保存される",
                [(Common, Save), (Common, Load)],
                h =>
                {
                    var chat = h.Create(AiDialogKind.AiChat);
                    chat.LoadSettings();
                    chat.ShowAssistantMarkdown = false;
                    h.SaveSettings(chat);

                    var mock = h.Create(AiDialogKind.MockGeneration);
                    mock.LoadSettings();
                    mock.ShowAssistantMarkdown.Should().BeTrue("chat の選択は mock へ波及しない");
                    h.SaveSettings(mock);

                    var restoredChat = h.Create(AiDialogKind.AiChat);
                    restoredChat.LoadSettings();
                    restoredChat.ShowAssistantMarkdown.Should().BeFalse();

                    var restoredMock = h.Create(AiDialogKind.MockGeneration);
                    restoredMock.LoadSettings();
                    restoredMock.ShowAssistantMarkdown.Should().BeTrue();
                }
            ),
            Row(
                "apikey-load-local-llm-selects-history-head-model",
                "Local LLM 復元でモデルは MRU 先頭が選ばれる",
                [(ApiKey, ConnectionOp.Record), (ApiKey, Save), (ApiKey, Load)],
                h =>
                {
                    var vm = h.Create();
                    vm.ApiProvider = AiProvider.LocalLlm;
                    vm.ApiModel = "qwen3.6:35b";
                    vm.RecordSuccessfulModel();
                    h.SaveSettings(vm);

                    var restored = h.Create();
                    restored.LoadSettings();

                    restored.ApiProvider.Should().Be(AiProvider.LocalLlm);
                    restored.ApiModel.Should().Be("qwen3.6:35b");
                }
            ),
            Row(
                "apikey-history-shared-across-dialogs-without-clobbering-ui-section",
                "chat / mock は API モデル履歴を共有し互いの UI セクションを消さない",
                [(ApiKey, ConnectionOp.Record), (ApiKey, Save), (ApiKey, Load)],
                h =>
                {
                    // chat 側: 共有履歴へ記録し、接続タブ（ClaudeCode）を保存する
                    var chat = h.Create(AiDialogKind.AiChat);
                    chat.LoadSettings();
                    chat.ApiProvider = AiProvider.LocalLlm;
                    chat.ApiModel = "shared-model";
                    chat.RecordSuccessfulModel();
                    chat.SelectedBackend = ErChatBackendKind.ClaudeCode;
                    h.SaveSettings(chat);

                    // mock 側: 同じファイルの ApiModelHistory セクションを共有して履歴が見える
                    var mock = h.Create(AiDialogKind.MockGeneration);
                    mock.LoadSettings();
                    mock.ApiProvider = AiProvider.LocalLlm;
                    CandidateNames(mock).Should().Contain("shared-model");

                    // mock 側の接続タブ（Codex）保存は、read-modify-write で chat 側セクションを消さない
                    mock.SelectedBackend = ErChatBackendKind.Codex;
                    h.SaveSettings(mock);

                    var restoredChat = h.Create(AiDialogKind.AiChat);
                    restoredChat.LoadSettings();
                    restoredChat.InitialBackend.Should().Be(ErChatBackendKind.ClaudeCode);

                    var restoredMock = h.Create(AiDialogKind.MockGeneration);
                    restoredMock.LoadSettings();
                    restoredMock.InitialBackend.Should().Be(ErChatBackendKind.Codex);
                }
            ),
            Row(
                "codex-history-shared-across-dialogs-without-clobbering-ui-section",
                "chat / mock は Codex 履歴を共有し互いの UI セクションを消さない",
                [(Codex, ConnectionOp.Record), (Codex, Save), (Codex, Load)],
                h =>
                {
                    // chat 側: 共有 Codex 履歴へ記録し、接続タブ（Codex）を保存する
                    var chat = CreateCodexVm(h, AiDialogKind.AiChat);
                    chat.CodexModel = "shared-codex-model";
                    chat.RecordSuccessfulModel();
                    h.SaveSettings(chat);

                    // mock 側: 同じファイルの CodexModelHistory セクションを共有して履歴が見える
                    var mock = CreateCodexVm(h, AiDialogKind.MockGeneration);
                    mock.CodexModelCandidates.Select(c => c.Name)
                        .Should()
                        .Contain("shared-codex-model");

                    // mock 側の保存（既定 ClaudeCode 以外＝ここでは Codex）でも chat 側 UI セクションは残る
                    mock.SelectedBackend = ErChatBackendKind.ClaudeCode;
                    h.SaveSettings(mock);

                    var restoredChat = CreateCodexVm(h, AiDialogKind.AiChat);
                    restoredChat.InitialBackend.Should().Be(ErChatBackendKind.Codex);

                    // 元のテストは config.toml の指定なし（既定の空）で作り直す
                    h.CodexConfig = () => new CodexConfigToml();
                    var restoredMock = h.Create(AiDialogKind.MockGeneration);
                    restoredMock.LoadSettings();
                    restoredMock.InitialBackend.Should().Be(ErChatBackendKind.ClaudeCode);
                }
            ),
        ];
}
