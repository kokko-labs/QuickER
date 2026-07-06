using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using FluentAssertions;
using QuickER.AI;
using QuickER.AI.UI;

namespace QuickER.Tests.ViewModels;

/// <summary>
/// <see cref="ChatConnectionSettingsViewModel"/> の既定値・接続方式切替・API キー永続化・
/// Codex 候補・UI 状態往復を検証するテストクラス。
/// </summary>
/// <remarks>
/// API キーストアには触れず、loader / saver seam を注入して隔離する。config.toml も
/// codexConfigReader seam で機械非依存にする。ファイルへ書く UI/設定ストアは一時フォルダへ隔離する。
/// </remarks>
public class ChatConnectionSettingsViewModelTests
{
    /// <summary>注入された保存デリゲートに渡された (slot, value) を記録するスパイ</summary>
    private sealed class KeySaverSpy
    {
        public List<(string Slot, string Value)> Saves { get; } = new();

        public void Save(string slot, string value) => Saves.Add((slot, value));
    }

    /// <summary>指定フォルダに隔離した UI/設定ストアで VM を生成する（ファイル副作用を隔離する）</summary>
    private static ChatConnectionSettingsViewModel CreateVm(
        string folder,
        string uiFileName = "ai-chat-ui.json",
        Func<CodexConfigToml>? codexConfigReader = null,
        Func<string, string?>? apiKeyLoader = null,
        Action<string, string>? apiKeySaver = null
    ) =>
        new(
            uiFileName,
            codexSettingsStore: new CodexAppServerSettingsStore(folder),
            uiSettingsStore: new ChatUiSettingsStore(uiFileName, folder),
            claudeCodeSettingsStore: new ClaudeCodeSettingsStore(folder),
            codexConfigReader: codexConfigReader ?? (() => new CodexConfigToml()),
            // 既定ではキーストアに触れないよう loader は空・saver は無操作にする
            apiKeyLoader: apiKeyLoader ?? (_ => string.Empty),
            apiKeySaver: apiKeySaver ?? ((_, _) => { })
        );

    private static string NewFolder() =>
        Path.Combine(Path.GetTempPath(), "QuickERTests", Guid.NewGuid().ToString("N"));

    private static void Cleanup(string folder)
    {
        if (Directory.Exists(folder))
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>PropertyChanged の発火名を記録するヘルパ（通知検証に使う）</summary>
    private static List<string> RecordChanges(ChatConnectionSettingsViewModel vm)
    {
        var changed = new List<string>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName ?? string.Empty);
        return changed;
    }

    /// <summary>既定値（SelectedBackend=ApiKey・ApiKey は空文字＝PasswordBoxBehavior 不変条件）を検証する</summary>
    [Fact(DisplayName = "既定値は ApiKey バックエンド・ApiKey は空文字")]
    public void Defaults_ApiKeyBackend_AndEmptyApiKey()
    {
        var folder = NewFolder();

        try
        {
            var vm = CreateVm(folder);

            vm.SelectedBackend.Should().Be(ErChatBackendKind.ApiKey);
            vm.IsApiKeyBackend.Should().BeTrue();
            vm.IsCodexBackend.Should().BeFalse();
            vm.IsClaudeCodeBackend.Should().BeFalse();

            // PasswordBoxBehavior の不変条件: 初期値は空文字（null 不可）
            vm.ApiKey.Should().Be(string.Empty);
            vm.ApiProvider.Should().Be(AiProvider.OpenAI);
            vm.SaveApiKey.Should().BeTrue();
        }
        finally
        {
            Cleanup(folder);
        }
    }

    /// <summary>SelectedBackend 変更で Is* 3 プロパティの通知が発火することを検証する</summary>
    [Fact(DisplayName = "SelectedBackend 変更で Is* 3 通知が発火する")]
    public void SelectedBackendChanged_NotifiesIsBackendFlags()
    {
        var folder = NewFolder();

        try
        {
            var vm = CreateVm(folder);
            var changed = RecordChanges(vm);

            vm.SelectedBackend = ErChatBackendKind.Codex;

            changed.Should().Contain(nameof(vm.IsApiKeyBackend));
            changed.Should().Contain(nameof(vm.IsCodexBackend));
            changed.Should().Contain(nameof(vm.IsClaudeCodeBackend));
            vm.IsCodexBackend.Should().BeTrue();
        }
        finally
        {
            Cleanup(folder);
        }
    }

    /// <summary>ApiProvider 切替で候補・Show* 通知・ApiModel リセット・Ollama エンドポイント補完が起きることを検証する</summary>
    [Fact(
        DisplayName = "ApiProvider を Ollama へ切替で候補・Show*・ApiModel リセット・エンドポイント補完"
    )]
    public void ApiProviderChanged_ResetsModel_NotifiesAndFillsEndpoint()
    {
        var folder = NewFolder();

        try
        {
            var vm = CreateVm(folder);
            var changed = RecordChanges(vm);

            vm.ApiProvider = AiProvider.Ollama;

            changed.Should().Contain(nameof(vm.ApiModelCandidates));
            changed.Should().Contain(nameof(vm.ShowApiKey));
            changed.Should().Contain(nameof(vm.ShowEndpoint));

            // モデルは新プロバイダー候補の先頭へリセットされる
            vm.ApiModel.Should().Be(AiModelCatalog.OllamaModels[0]);
            vm.ApiModelCandidates.Should().BeEquivalentTo(AiModelCatalog.OllamaModels);

            // Ollama はキー欄非表示・エンドポイント欄表示・エンドポイント自動補完
            vm.ShowApiKey.Should().BeFalse();
            vm.ShowEndpoint.Should().BeTrue();
            vm.EndpointOverride.Should().Be("http://localhost:11434/v1");
        }
        finally
        {
            Cleanup(folder);
        }
    }

    /// <summary>プロバイダー切替でそのプロバイダー用の保存済みキーが読み直されることを検証する（loader 注入）</summary>
    [Fact(DisplayName = "プロバイダー切替で保存済みキーを読み直す")]
    public void ApiProviderChanged_ReloadsKeyForProvider()
    {
        var folder = NewFolder();

        try
        {
            // OpenAI=openai-key / Claude=claude-key を返す loader
            var vm = CreateVm(
                folder,
                apiKeyLoader: slot => slot == "OpenAiApiKey" ? "openai-key" : "claude-key"
            );

            // 初期化で OpenAI 用キーを読み込む
            vm.Initialize();
            vm.ApiKey.Should().Be("openai-key");

            // Claude へ切替でそのプロバイダー用キーへ読み直される
            vm.ApiProvider = AiProvider.Claude;
            vm.ApiKey.Should().Be("claude-key");
        }
        finally
        {
            Cleanup(folder);
        }
    }

    /// <summary>プロバイダー切替に伴うキー読み直し中は saver が発火しないことを検証する（_isInitializing 抑止）</summary>
    [Fact(DisplayName = "キー読み直し中は saver が発火しない")]
    public void ApiProviderChanged_DoesNotPersistDuringReload()
    {
        var folder = NewFolder();

        try
        {
            var spy = new KeySaverSpy();
            var vm = CreateVm(folder, apiKeyLoader: _ => "loaded-key", apiKeySaver: spy.Save);

            // プロバイダー切替はキー読み直しを伴うが、読み直し中の ApiKey 変更で保存してはいけない
            vm.ApiProvider = AiProvider.Claude;

            spy.Saves.Should().BeEmpty();
        }
        finally
        {
            Cleanup(folder);
        }
    }

    /// <summary>手入力したキーが現在プロバイダーの保存名で保存されることを検証する</summary>
    [Fact(DisplayName = "手入力キーは現在プロバイダーの保存名で保存される")]
    public void ApiKeyChanged_PersistsForCurrentProvider()
    {
        var folder = NewFolder();

        try
        {
            var spy = new KeySaverSpy();
            var vm = CreateVm(folder, apiKeySaver: spy.Save);

            vm.ApiKey = "sk-manual";

            spy.Saves.Should().ContainSingle();
            spy.Saves[0].Should().Be(("OpenAiApiKey", "sk-manual"));
        }
        finally
        {
            Cleanup(folder);
        }
    }

    /// <summary>SaveApiKey=false のとき空文字で保存（＝削除相当）されることを検証する</summary>
    [Fact(DisplayName = "SaveApiKey=false で空文字保存される")]
    public void SaveApiKeyFalse_PersistsEmpty()
    {
        var folder = NewFolder();

        try
        {
            var spy = new KeySaverSpy();
            var vm = CreateVm(folder, apiKeySaver: spy.Save);
            vm.ApiKey = "sk-manual";
            spy.Saves.Clear();

            vm.SaveApiKey = false;

            spy.Saves.Should().ContainSingle();
            spy.Saves[0].Should().Be(("OpenAiApiKey", string.Empty));
        }
        finally
        {
            Cleanup(folder);
        }
    }

    /// <summary>API キー不要のプロバイダー（Ollama）ではキーを保存しないことを検証する</summary>
    [Fact(DisplayName = "Ollama ではキーを永続化しない")]
    public void OllamaProvider_DoesNotPersistKey()
    {
        var folder = NewFolder();

        try
        {
            var spy = new KeySaverSpy();
            var vm = CreateVm(folder, apiKeySaver: spy.Save);
            vm.ApiProvider = AiProvider.Ollama;
            spy.Saves.Clear();

            // Ollama は CurrentApiKeyStoreName が null のため保存 seam を呼ばない
            vm.ApiKey = "ignored";

            spy.Saves.Should().BeEmpty();
        }
        finally
        {
            Cleanup(folder);
        }
    }

    /// <summary>LoadSettings で openai が候補先頭に固定され、config.toml のプロバイダーが重複なく続くことを検証する</summary>
    [Fact(DisplayName = "Codex 候補は openai 先頭固定・config.toml のプロバイダーを dedup 追加")]
    public void LoadSettings_CodexCandidates_OpenAiFirst_AndDedup()
    {
        var folder = NewFolder();

        try
        {
            // openai を重複して含む config（dedup で 1 つに畳まれる想定）
            var config = new CodexConfigToml
            {
                ProviderNames = new List<string> { "ollama-launch", "openai" },
                ProviderModels = new Dictionary<string, IReadOnlyList<string>>(
                    StringComparer.OrdinalIgnoreCase
                )
                {
                    ["ollama-launch"] = new List<string> { "gemma4:31b-cloud" },
                },
            };
            var vm = CreateVm(folder, codexConfigReader: () => config);

            vm.LoadSettings();

            // 先頭は必ず openai・config 由来の ollama-launch が続く・openai は重複しない
            vm.CodexModelProviderCandidates.First().Should().Be("openai");
            vm.CodexModelProviderCandidates.Should().ContainSingle(p => p == "openai");
            vm.CodexModelProviderCandidates.Should().Contain("ollama-launch");

            // 既定（openai）のモデル候補は OpenAI カタログ
            vm.CodexModelCandidates.Should().BeEquivalentTo(AiModelCatalog.OpenAiModels);
        }
        finally
        {
            Cleanup(folder);
        }
    }

    /// <summary>Codex プロバイダーを config 由来へ切替でモデル候補が config のモデルへ更新されることを検証する</summary>
    [Fact(DisplayName = "Codex プロバイダー切替で候補が config のモデルへ更新される")]
    public void CodexModelProviderChanged_RefreshesCandidates()
    {
        var folder = NewFolder();

        try
        {
            var config = new CodexConfigToml
            {
                ProviderNames = new List<string> { "ollama-launch" },
                ProviderModels = new Dictionary<string, IReadOnlyList<string>>(
                    StringComparer.OrdinalIgnoreCase
                )
                {
                    ["ollama-launch"] = new List<string> { "gemma4:31b-cloud" },
                },
            };
            var vm = CreateVm(folder, codexConfigReader: () => config);
            vm.LoadSettings();

            vm.CodexModelProvider = "ollama-launch";

            vm.CodexModelCandidates.Should().ContainSingle();
            vm.CodexModelCandidates[0].Should().Be("gemma4:31b-cloud");
        }
        finally
        {
            Cleanup(folder);
        }
    }

    /// <summary>SaveSettings→LoadSettings 往復で接続タブ・モデルが復元されることを検証する（ファイル名明示でフォーマット互換を固定化）</summary>
    [Fact(DisplayName = "SaveSettings→LoadSettings 往復で設定が復元される")]
    public void SaveSettings_RoundTrip_RestoresState()
    {
        var folder = NewFolder();

        try
        {
            var config = new CodexConfigToml
            {
                ProviderNames = new List<string> { "ollama-launch" },
                ProviderModels = new Dictionary<string, IReadOnlyList<string>>(
                    StringComparer.OrdinalIgnoreCase
                )
                {
                    ["ollama-launch"] = new List<string> { "gemma4:31b-cloud" },
                },
            };

            var vm = CreateVm(folder, codexConfigReader: () => config);
            vm.LoadSettings();
            vm.SelectedBackend = ErChatBackendKind.ClaudeCode;
            vm.CodexModelProvider = "ollama-launch";
            vm.CodexModel = "gemma4:31b-cloud";
            vm.ClaudeCodeModel = "opus";
            vm.SaveSettings();

            // 同じファイル名・フォルダで別インスタンスへ読み戻す
            var restored = CreateVm(folder, codexConfigReader: () => config);
            restored.LoadSettings();

            restored.InitialBackend.Should().Be(ErChatBackendKind.ClaudeCode);
            restored.CodexModelProvider.Should().Be("ollama-launch");
            restored.CodexModel.Should().Be("gemma4:31b-cloud");
            restored.ClaudeCodeModel.Should().Be("opus");
        }
        finally
        {
            Cleanup(folder);
        }
    }
}
