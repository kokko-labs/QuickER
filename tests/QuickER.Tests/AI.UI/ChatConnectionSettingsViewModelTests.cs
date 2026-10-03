using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using AwesomeAssertions;
using QuickER.AI;
using QuickER.AI.UI;

namespace QuickER.Tests.AI.UI;

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

    /// <summary>指定フォルダに隔離した AI 設定ストアで VM を生成する（ファイル副作用を隔離する）</summary>
    private static ChatConnectionSettingsViewModel CreateVm(
        string folder,
        AiDialogKind dialogKind = AiDialogKind.AiChat,
        Func<CodexConfigToml>? codexConfigReader = null,
        Func<string, string?>? apiKeyLoader = null,
        Action<string, string>? apiKeySaver = null
    ) =>
        new(
            dialogKind,
            // 設定・UI 状態・モデル履歴を集約した 1 ファイルを一時フォルダへ隔離する（実 %LOCALAPPDATA% を保護）
            settingsStore: new AiSettingsStore(folder),
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
            vm.IsCopilotBackend.Should().BeFalse();

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

    /// <summary>SelectedBackend 変更で Is* 4 プロパティの通知が発火することを検証する</summary>
    [Fact(DisplayName = "SelectedBackend 変更で Is* 4 通知が発火する")]
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
            changed.Should().Contain(nameof(vm.IsCopilotBackend));
            vm.IsCodexBackend.Should().BeTrue();
        }
        finally
        {
            Cleanup(folder);
        }
    }

    /// <summary>ApiProvider 切替で候補・Show* 通知・ApiModel リセット・ローカル LLM エンドポイント補完が起きることを検証する</summary>
    [Fact(
        DisplayName = "ApiProvider を Local LLM へ切替で候補・Show*・ApiModel リセット・エンドポイント補完"
    )]
    public void ApiProviderChanged_ResetsModel_NotifiesAndFillsEndpoint()
    {
        var folder = NewFolder();

        try
        {
            var vm = CreateVm(folder);
            var changed = RecordChanges(vm);

            vm.ApiProvider = AiProvider.LocalLlm;

            changed.Should().Contain(nameof(vm.ShowApiKey));
            changed.Should().Contain(nameof(vm.ShowEndpoint));

            // ローカル LLM は履歴が初期空のため、モデルは空・候補も空（IndexOutOfRange を起こさない）
            vm.ApiModel.Should().BeEmpty();
            vm.ApiModelCandidates.Should().BeEmpty();

            // ローカル LLM はキー欄表示（キーは任意）・エンドポイント欄表示・エンドポイント自動補完
            vm.IsLocalLlmProvider.Should().BeTrue();
            vm.ShowApiKey.Should().BeTrue();
            vm.ShowEndpoint.Should().BeTrue();
            vm.EndpointOverride.Should().Be(LocalLlmDefaults.Endpoint);
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

    /// <summary>手入力したキーが、確定時に現在プロバイダーの保存名で保存されることを検証する</summary>
    [Fact(DisplayName = "手入力キーは確定時に現在プロバイダーの保存名で保存される")]
    public void ApiKeyCommit_PersistsForCurrentProvider()
    {
        var folder = NewFolder();

        try
        {
            var spy = new KeySaverSpy();
            var vm = CreateVm(folder, apiKeySaver: spy.Save);

            vm.ApiKey = "sk-manual";
            vm.CommitApiKey();

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

    /// <summary>
    /// エンドポイント上書きがローカル LLM のときだけ有効になることを検証する。
    /// エンドポイント欄はローカル LLM 選択時のみ表示されるため、値を入れたまま OpenAI へ切り替えると
    /// 「見えない欄に残った URL が OpenAI 接続に使われる」事故になる。その回帰を防ぐ。
    /// </summary>
    [Fact(DisplayName = "エンドポイント上書きは Local LLM のときだけ有効")]
    public void EffectiveEndpointOverride_AppliesOnlyToLocalLlm()
    {
        var folder = NewFolder();

        try
        {
            var vm = CreateVm(folder);

            // ローカル LLM で URL を入力する（切替時の自動補完も同じ値になる）
            vm.ApiProvider = AiProvider.LocalLlm;
            vm.EndpointOverride = "http://127.0.0.1:1234/v1";
            vm.EffectiveEndpointOverride.Should().Be("http://127.0.0.1:1234/v1");

            // OpenAI / Claude へ切り替えると、欄に値が残っていても無視される
            vm.ApiProvider = AiProvider.OpenAI;
            vm.EndpointOverride.Should().Be("http://127.0.0.1:1234/v1");
            vm.EffectiveEndpointOverride.Should().BeNull();

            vm.ApiProvider = AiProvider.Claude;
            vm.EffectiveEndpointOverride.Should().BeNull();

            // ローカル LLM へ戻すと再び有効になる
            vm.ApiProvider = AiProvider.LocalLlm;
            vm.EffectiveEndpointOverride.Should().Be("http://127.0.0.1:1234/v1");
        }
        finally
        {
            Cleanup(folder);
        }
    }

    /// <summary>ローカル LLM でもエンドポイント欄が空白のみなら上書きなし（プロバイダ既定）になることを検証する</summary>
    [Fact(DisplayName = "空白のみのエンドポイントは上書きとして扱わない")]
    public void EffectiveEndpointOverride_BlankIsIgnored()
    {
        var folder = NewFolder();

        try
        {
            var vm = CreateVm(folder);
            vm.ApiProvider = AiProvider.LocalLlm;
            vm.EndpointOverride = "   ";

            vm.EffectiveEndpointOverride.Should().BeNull();
        }
        finally
        {
            Cleanup(folder);
        }
    }

    /// <summary>
    /// ローカル LLM でもキーは（任意ながら）専用スロット "LocalLlmApiKey" へ永続化されることを検証する。
    /// 認証を課すローカルサーバー向けにキーを保持できる必要があるため、OpenAI / Claude と同じ機構に乗せる。
    /// </summary>
    [Fact(DisplayName = "Local LLM のキーは確定時に専用スロットへ永続化される")]
    public void LocalLlmProvider_PersistsKeyToOwnSlot()
    {
        var folder = NewFolder();

        try
        {
            var spy = new KeySaverSpy();
            var vm = CreateVm(folder, apiKeySaver: spy.Save);
            vm.ApiProvider = AiProvider.LocalLlm;
            spy.Saves.Clear();

            vm.ApiKey = "local-secret";
            vm.CommitApiKey();

            spy.Saves.Should().ContainSingle();
            spy.Saves[0].Slot.Should().Be("LocalLlmApiKey");
            spy.Saves[0].Value.Should().Be("local-secret");
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
            };
            var vm = CreateVm(folder, codexConfigReader: () => config);

            vm.LoadSettings();

            // 先頭は必ず openai・config 由来の ollama-launch が続く・openai は重複しない
            vm.CodexModelProviderCandidates.First().Should().Be("openai");
            vm.CodexModelProviderCandidates.Should().ContainSingle(p => p == "openai");
            vm.CodexModelProviderCandidates.Should().Contain("ollama-launch");

            // 既定（openai）のモデル候補は OpenAI カタログ（すべて削除不可の固定候補）
            vm.CodexModelCandidates.Select(c => c.Name)
                .Should()
                .BeEquivalentTo(AiModelCatalog.OpenAiModels);
            vm.CodexModelCandidates.Should().OnlyContain(c => !c.IsRemovable);
        }
        finally
        {
            Cleanup(folder);
        }
    }

    /// <summary>Codex プロバイダーを非 openai へ切替でモデル候補がそのプロバイダーの履歴へ更新されることを検証する</summary>
    [Fact(DisplayName = "Codex プロバイダー切替で候補がそのプロバイダーの履歴へ更新される")]
    public void CodexModelProviderChanged_RefreshesCandidates()
    {
        var folder = NewFolder();

        try
        {
            var config = new CodexConfigToml
            {
                ProviderNames = new List<string> { "ollama-launch" },
            };

            // 履歴を仕込んでおき、プロバイダー切替で読み込まれることを確認する
            var seeded = new AiSettingsStore(folder).Load();
            seeded.CodexModelHistory.Touch("ollama-launch", "gemma4:31b-cloud");
            new AiSettingsStore(folder).Save(seeded);

            var vm = CreateVm(folder, codexConfigReader: () => config);
            vm.LoadSettings();

            vm.CodexModelProvider = "ollama-launch";

            // 非 openai の候補は履歴のみ（すべて × で削除可能）
            vm.CodexModelCandidates.Should().ContainSingle();
            vm.CodexModelCandidates[0].Name.Should().Be("gemma4:31b-cloud");
            vm.CodexModelCandidates[0].IsRemovable.Should().BeTrue();
        }
        finally
        {
            Cleanup(folder);
        }
    }

    /// <summary>履歴が無い非 openai プロバイダーへの切替で候補が空になることを検証する（初期状態は空）</summary>
    [Fact(DisplayName = "履歴なしの Codex プロバイダー切替で候補は空")]
    public void CodexModelProviderChanged_NoHistory_EmptyCandidates()
    {
        var folder = NewFolder();

        try
        {
            var config = new CodexConfigToml
            {
                ProviderNames = new List<string> { "ollama-launch" },
            };
            var vm = CreateVm(folder, codexConfigReader: () => config);
            vm.LoadSettings();

            vm.CodexModelProvider = "ollama-launch";

            vm.CodexModelCandidates.Should().BeEmpty();
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

    // ── API キー接続の選択（プロバイダー・エンドポイント）の永続化 ──

    /// <summary>プロバイダーとエンドポイントが設定ストア（UI セクション）へ保存されることを検証する</summary>
    [Fact(DisplayName = "SaveSettings でプロバイダーとエンドポイントが保存される")]
    public void SaveSettings_PersistsApiProviderAndEndpoint()
    {
        var folder = NewFolder();

        try
        {
            var vm = CreateVm(folder);
            vm.LoadSettings();
            vm.ApiProvider = AiProvider.LocalLlm;
            vm.EndpointOverride = "  http://127.0.0.1:1234/v1  ";

            vm.SaveSettings();

            var ui = new AiSettingsStore(folder).Load().UiFor(AiDialogKind.AiChat);
            ui.ApiProvider.Should().Be(nameof(AiProvider.LocalLlm));
            // 前後の空白は落として保存する（復元時にそのまま URL として使えるようにする）
            ui.EndpointOverride.Should().Be("http://127.0.0.1:1234/v1");
        }
        finally
        {
            Cleanup(folder);
        }
    }

    /// <summary>保存済みのプロバイダー・エンドポイントが LoadSettings で復元され、実効値として効くことを検証する</summary>
    [Fact(DisplayName = "LoadSettings でプロバイダーとエンドポイントが復元される")]
    public void LoadSettings_RestoresApiProviderAndEndpoint()
    {
        var folder = NewFolder();

        try
        {
            var vm = CreateVm(folder);
            vm.LoadSettings();
            vm.ApiProvider = AiProvider.LocalLlm;
            vm.EndpointOverride = "http://127.0.0.1:1234/v1";
            vm.SaveSettings();

            var restored = CreateVm(folder);
            restored.LoadSettings();

            restored.ApiProvider.Should().Be(AiProvider.LocalLlm);
            restored.EndpointOverride.Should().Be("http://127.0.0.1:1234/v1");
            restored.EffectiveEndpointOverride.Should().Be("http://127.0.0.1:1234/v1");
            restored.ShowEndpoint.Should().BeTrue();
        }
        finally
        {
            Cleanup(folder);
        }
    }

    /// <summary>
    /// エンドポイント未保存のローカル LLM を復元すると、プロバイダー変更フックの既定 URL 補完が効くことを検証する
    /// （保存値がある場合だけ後から上書きする、という復元順序の回帰を防ぐ）。
    /// </summary>
    [Fact(DisplayName = "エンドポイント未保存の Local LLM 復元では既定 URL が補完される")]
    public void LoadSettings_LocalLlmWithoutEndpoint_FillsDefaultEndpoint()
    {
        var folder = NewFolder();

        try
        {
            var seeded = new AiSettingsStore(folder).Load();
            seeded.ChatUi.ApiProvider = nameof(AiProvider.LocalLlm);
            new AiSettingsStore(folder).Save(seeded);

            var vm = CreateVm(folder);
            vm.LoadSettings();

            vm.ApiProvider.Should().Be(AiProvider.LocalLlm);
            vm.EndpointOverride.Should().Be(LocalLlmDefaults.Endpoint);
        }
        finally
        {
            Cleanup(folder);
        }
    }

    /// <summary>解釈できない保存値（未知の名前・定義外の数値）は既定プロバイダー（OpenAI）へフォールバックすることを検証する</summary>
    [Theory(DisplayName = "未知の保存プロバイダーは OpenAI へフォールバック")]
    [InlineData("gemini")]
    [InlineData("99")]
    [InlineData("")]
    public void LoadSettings_UnknownApiProvider_FallsBackToOpenAi(string saved)
    {
        var folder = NewFolder();

        try
        {
            var seeded = new AiSettingsStore(folder).Load();
            seeded.ChatUi.ApiProvider = saved;
            new AiSettingsStore(folder).Save(seeded);

            var vm = CreateVm(folder);
            vm.LoadSettings();

            vm.ApiProvider.Should().Be(AiProvider.OpenAI);
            vm.EndpointOverride.Should().BeEmpty();
        }
        finally
        {
            Cleanup(folder);
        }
    }

    /// <summary>新キーを持たない旧 JSON でも読めて既定値（OpenAI・上書きなし）になることを検証する（後方互換）</summary>
    [Fact(DisplayName = "新キーなしの旧 JSON は既定値で読める")]
    public void LoadSettings_LegacyJsonWithoutNewKeys_UsesDefaults()
    {
        var folder = NewFolder();

        try
        {
            // apiProvider / endpointOverride を持たない旧世代のファイルを直接書く
            Directory.CreateDirectory(folder);
            File.WriteAllText(
                new AiSettingsStore(folder).SettingsPath,
                """
                {
                  "chatUi": { "lastBackend": "ClaudeCode" }
                }
                """
            );

            var vm = CreateVm(folder);
            vm.LoadSettings();

            vm.InitialBackend.Should().Be(ErChatBackendKind.ClaudeCode);
            vm.ApiProvider.Should().Be(AiProvider.OpenAI);
            vm.EndpointOverride.Should().BeEmpty();
            vm.EffectiveEndpointOverride.Should().BeNull();
        }
        finally
        {
            Cleanup(folder);
        }
    }

    /// <summary>プロバイダー・エンドポイントは LastBackend と同じ粒度（ダイアログ別セクション）で保存されることを検証する</summary>
    [Fact(DisplayName = "プロバイダーとエンドポイントは chat / mock で独立する")]
    public void SaveSettings_ApiProviderAndEndpoint_IsolatedPerDialog()
    {
        var folder = NewFolder();

        try
        {
            var chat = CreateVm(folder, dialogKind: AiDialogKind.AiChat);
            chat.LoadSettings();
            chat.ApiProvider = AiProvider.LocalLlm;
            chat.EndpointOverride = "http://127.0.0.1:1234/v1";
            chat.SaveSettings();

            var mock = CreateVm(folder, dialogKind: AiDialogKind.MockGeneration);
            mock.LoadSettings();
            mock.ApiProvider = AiProvider.Claude;
            mock.SaveSettings();

            var restoredChat = CreateVm(folder, dialogKind: AiDialogKind.AiChat);
            restoredChat.LoadSettings();
            restoredChat.ApiProvider.Should().Be(AiProvider.LocalLlm);
            restoredChat.EndpointOverride.Should().Be("http://127.0.0.1:1234/v1");

            var restoredMock = CreateVm(folder, dialogKind: AiDialogKind.MockGeneration);
            restoredMock.LoadSettings();
            restoredMock.ApiProvider.Should().Be(AiProvider.Claude);
            restoredMock.EndpointOverride.Should().BeEmpty();
        }
        finally
        {
            Cleanup(folder);
        }
    }

    /// <summary>ローカル LLM を復元したとき、モデルは MRU 履歴の先頭が選ばれることを検証する（カタログ無しプロバイダーの既定選択）</summary>
    [Fact(DisplayName = "Local LLM 復元でモデルは MRU 先頭が選ばれる")]
    public void LoadSettings_LocalLlm_SelectsHistoryHeadModel()
    {
        var folder = NewFolder();

        try
        {
            var vm = CreateVm(folder);
            vm.ApiProvider = AiProvider.LocalLlm;
            vm.ApiModel = "qwen3.6:35b";
            vm.RecordSuccessfulModel();
            vm.SaveSettings();

            var restored = CreateVm(folder);
            restored.LoadSettings();

            restored.ApiProvider.Should().Be(AiProvider.LocalLlm);
            restored.ApiModel.Should().Be("qwen3.6:35b");
        }
        finally
        {
            Cleanup(folder);
        }
    }

    /// <summary>候補リストに無い保存済みプロバイダーは openai へフォールバックすることを検証する（リスト選択のみのため）</summary>
    [Fact(DisplayName = "候補に無い保存済み Codex プロバイダーは openai へフォールバック")]
    public void LoadSettings_UnknownCodexProvider_FallsBackToOpenAi()
    {
        var folder = NewFolder();

        try
        {
            // config.toml から消えたプロバイダーが保存されている状況を作る
            var seeded = new AiSettingsStore(folder).Load();
            seeded.CodexAppServer = new CodexAppServerSettings
            {
                ModelProvider = "removed-provider",
                Model = "m",
            };
            new AiSettingsStore(folder).Save(seeded);

            var vm = CreateVm(folder);
            vm.LoadSettings();

            vm.CodexModelProvider.Should().Be("openai");
        }
        finally
        {
            Cleanup(folder);
        }
    }

    /// <summary>保存済みプロバイダーの大文字小文字ゆれは候補の表記へ正規化されることを検証する（SelectedItem 一致のため）</summary>
    [Fact(DisplayName = "保存済み Codex プロバイダーの表記ゆれは候補の表記へ正規化")]
    public void LoadSettings_CodexProvider_NormalizesCasingToCandidate()
    {
        var folder = NewFolder();

        try
        {
            var seeded = new AiSettingsStore(folder).Load();
            seeded.CodexAppServer = new CodexAppServerSettings
            {
                ModelProvider = "OLLAMA-LAUNCH",
                Model = "m",
            };
            new AiSettingsStore(folder).Save(seeded);

            var config = new CodexConfigToml
            {
                ProviderNames = new List<string> { "ollama-launch" },
            };
            var vm = CreateVm(folder, codexConfigReader: () => config);
            vm.LoadSettings();

            // SelectedItem バインドで候補と一致させるため、候補側の表記を採用する
            vm.CodexModelProvider.Should().Be("ollama-launch");
        }
        finally
        {
            Cleanup(folder);
        }
    }

    // ── API キー接続のモデル MRU 履歴（プロバイダ別・カタログ外の手入力モデルのみ記録） ──

    /// <summary>AI 設定ファイルの絶対パス（履歴が書かれていない＝ファイル非作成の検証に使う）</summary>
    private static string ApiHistoryPath(string folder) => new AiSettingsStore(folder).SettingsPath;

    /// <summary>候補のモデル名一覧（アサート簡略化用）</summary>
    private static IEnumerable<string> CandidateNames(ChatConnectionSettingsViewModel vm) =>
        vm.ApiModelCandidates.Select(c => c.Name);

    /// <summary>ローカル LLM（カタログ無し）の成功記録で候補先頭へ入り、JSON へ往復することを検証する</summary>
    [Fact(DisplayName = "Local LLM 記録で候補先頭に入り JSON へ往復する")]
    public void RecordSuccessfulModel_LocalLlm_AddsToCandidates_AndPersists()
    {
        var folder = NewFolder();

        try
        {
            var vm = CreateVm(folder);
            vm.ApiProvider = AiProvider.LocalLlm;
            vm.ApiModel = "qwen3.6:35b";

            vm.RecordSuccessfulModel();

            // ローカル LLM はカタログが無いため候補は履歴のみ（× 付き）
            CandidateNames(vm).Should().Equal("qwen3.6:35b");
            vm.ApiModelCandidates[0].IsRemovable.Should().BeTrue();

            // 別インスタンスで読み戻して JSON 永続化を確認する（キーは enum 名の小文字＝"localllm"）
            var reloaded = new AiSettingsStore(folder).Load().ApiModelHistory;
            reloaded.ModelsFor("localllm").Should().Equal("qwen3.6:35b");
        }
        finally
        {
            Cleanup(folder);
        }
    }

    /// <summary>OpenAI でカタログ外モデルを記録するとカタログの下に × 付きで出て、JSON へ往復することを検証する（本命）</summary>
    [Fact(DisplayName = "OpenAI のカタログ外モデル記録でカタログの下に × 付きで出る")]
    public void RecordSuccessfulModel_OpenAiCustomModel_AppendsBelowCatalog_AndPersists()
    {
        var folder = NewFolder();

        try
        {
            var vm = CreateVm(folder);
            vm.LoadSettings();
            // 既定は OpenAI。カタログに無いモデルを手入力した状況を作る
            vm.ApiModel = "my-custom-gpt";

            vm.RecordSuccessfulModel();

            // カタログ（削除不可）が上に固定され、履歴（× 付き）がその下に並ぶ
            CandidateNames(vm).Should().Equal([.. AiModelCatalog.OpenAiModels, "my-custom-gpt"]);
            vm.ApiModelCandidates[^1].IsRemovable.Should().BeTrue();
            vm.ApiModelCandidates.Take(AiModelCatalog.OpenAiModels.Count)
                .Should()
                .OnlyContain(c => !c.IsRemovable);

            // JSON はプロバイダキー "openai" で永続化される
            var reloaded = new AiSettingsStore(folder).Load().ApiModelHistory;
            reloaded.ModelsFor("openai").Should().Equal("my-custom-gpt");
        }
        finally
        {
            Cleanup(folder);
        }
    }

    /// <summary>カタログ在中モデルの成功ターンでは履歴ファイルに記録しないことを検証する（本命）</summary>
    [Fact(DisplayName = "カタログ在中モデルの成功では記録しない")]
    public void RecordSuccessfulModel_CatalogModel_DoesNotRecord()
    {
        var folder = NewFolder();

        try
        {
            var vm = CreateVm(folder);
            vm.LoadSettings();
            // カタログ在中モデル（大文字小文字違いでも一致とみなす）
            vm.ApiModel = AiModelCatalog.DefaultOpenAiModel.ToUpperInvariant();

            vm.RecordSuccessfulModel();

            // 候補はカタログのみ・履歴ファイルは作られない
            vm.ApiModelCandidates.Should().OnlyContain(c => !c.IsRemovable);
            File.Exists(ApiHistoryPath(folder)).Should().BeFalse();
        }
        finally
        {
            Cleanup(folder);
        }
    }

    /// <summary>Claude の履歴はプロバイダ別に分離される（openai の履歴は claude に出ない）ことを検証する</summary>
    [Fact(DisplayName = "API 履歴はプロバイダ別に分離される")]
    public void RecordSuccessfulModel_ApiHistoryIsolatedPerProvider()
    {
        var folder = NewFolder();

        try
        {
            var vm = CreateVm(folder);
            vm.LoadSettings();

            // OpenAI でカタログ外モデルを記録
            vm.ApiModel = "custom-openai";
            vm.RecordSuccessfulModel();

            // Claude でもカタログ外モデルを記録
            vm.ApiProvider = AiProvider.Claude;
            vm.ApiModel = "custom-claude";
            vm.RecordSuccessfulModel();

            // Claude の候補にはカタログ＋claude の履歴のみ（openai の履歴は出ない）
            CandidateNames(vm).Should().Equal([.. AiModelCatalog.ClaudeModels, "custom-claude"]);

            // JSON もプロバイダ別に分離される
            var reloaded = new AiSettingsStore(folder).Load().ApiModelHistory;
            reloaded.ModelsFor("openai").Should().Equal("custom-openai");
            reloaded.ModelsFor("claude").Should().Equal("custom-claude");
        }
        finally
        {
            Cleanup(folder);
        }
    }

    /// <summary>MRU: "a"→"b"→"A" の記録で履歴が ["A", "b"]（大文字小文字問わず重複排除・新表記採用）になることを検証する</summary>
    [Fact(DisplayName = "記録は MRU（重複排除・新表記採用・先頭挿入）で並ぶ")]
    public void RecordSuccessfulModel_MruOrder_DedupCaseInsensitive()
    {
        var folder = NewFolder();

        try
        {
            var vm = CreateVm(folder);
            vm.ApiProvider = AiProvider.LocalLlm;

            vm.ApiModel = "a";
            vm.RecordSuccessfulModel();
            vm.ApiModel = "b";
            vm.RecordSuccessfulModel();
            vm.ApiModel = "A";
            vm.RecordSuccessfulModel();

            CandidateNames(vm).Should().Equal("A", "b");
        }
        finally
        {
            Cleanup(folder);
        }
    }

    /// <summary>上限（20 件/プロバイダ）を超えて記録すると最古が末尾から切り詰められることを検証する</summary>
    [Fact(DisplayName = "記録は上限 20 件で最古を切り詰める")]
    public void RecordSuccessfulModel_TrimsToMaxEntries()
    {
        var folder = NewFolder();

        try
        {
            var vm = CreateVm(folder);
            vm.ApiProvider = AiProvider.LocalLlm;

            // model-0 .. model-20 の 21 件を古い順に記録する
            for (var i = 0; i <= 20; i++)
            {
                vm.ApiModel = $"model-{i}";
                vm.RecordSuccessfulModel();
            }

            vm.ApiModelCandidates.Should().HaveCount(ProviderModelHistory.MaxEntries);
            // 最新（model-20）が先頭・最古（model-0）は消える
            vm.ApiModelCandidates[0].Name.Should().Be("model-20");
            CandidateNames(vm).Should().NotContain("model-0");
        }
        finally
        {
            Cleanup(folder);
        }
    }

    /// <summary>Codex バックエンド（＝API キー接続でない）では API 履歴に記録しないことを検証する（ガード）</summary>
    [Fact(DisplayName = "Codex バックエンドでは API 履歴に記録しない")]
    public void RecordSuccessfulModel_NonApiKeyBackend_DoesNothing()
    {
        var folder = NewFolder();

        try
        {
            var vm = CreateVm(folder);
            vm.ApiProvider = AiProvider.LocalLlm;
            vm.ApiModel = "qwen3.6:35b";
            vm.SelectedBackend = ErChatBackendKind.Codex;

            vm.RecordSuccessfulModel();

            File.Exists(ApiHistoryPath(folder)).Should().BeFalse();
        }
        finally
        {
            Cleanup(folder);
        }
    }

    /// <summary>空白のみのモデル名では記録されず履歴ファイルも作られないことを検証する（ガード）</summary>
    [Fact(DisplayName = "空白モデル名では記録しない")]
    public void RecordSuccessfulModel_BlankModel_DoesNothing()
    {
        var folder = NewFolder();

        try
        {
            var vm = CreateVm(folder);
            vm.ApiProvider = AiProvider.LocalLlm;
            vm.ApiModel = "   ";

            vm.RecordSuccessfulModel();

            vm.ApiModelCandidates.Should().BeEmpty();
            File.Exists(ApiHistoryPath(folder)).Should().BeFalse();
        }
        finally
        {
            Cleanup(folder);
        }
    }

    /// <summary>削除コマンドで履歴が候補・JSON から消え、カタログと選択中モデル（ApiModel）は変わらないことを検証する</summary>
    [Fact(DisplayName = "削除コマンドで履歴のみ消えカタログと ApiModel は残る")]
    public void RemoveApiModelHistoryCommand_RemovesHistoryOnly_KeepsCatalogAndModel()
    {
        var folder = NewFolder();

        try
        {
            var vm = CreateVm(folder);
            vm.LoadSettings();
            // OpenAI でカタログ外モデルを記録してから、既定モデルへ戻す
            vm.ApiModel = "custom-openai";
            vm.RecordSuccessfulModel();
            vm.ApiModel = AiModelCatalog.DefaultOpenAiModel;

            vm.RemoveApiModelHistoryCommand.Execute("custom-openai");

            // 履歴のみ消え、カタログは残り、選択中モデルは保持される
            CandidateNames(vm).Should().Equal(AiModelCatalog.OpenAiModels);
            vm.ApiModel.Should().Be(AiModelCatalog.DefaultOpenAiModel);

            var reloaded = new AiSettingsStore(folder).Load().ApiModelHistory;
            reloaded.ModelsFor("openai").Should().BeEmpty();
        }
        finally
        {
            Cleanup(folder);
        }
    }

    /// <summary>カタログと同名（大文字小文字問わず）の履歴が表示で重複しないことを検証する</summary>
    [Fact(DisplayName = "カタログと同名の履歴は表示しない")]
    public void RefreshApiCandidates_SkipsHistoryDuplicatingCatalog()
    {
        var folder = NewFolder();

        try
        {
            // カタログ既定モデルと大文字小文字違いの履歴を直接仕込む
            var seeded = new AiSettingsStore(folder).Load();
            seeded.ApiModelHistory.Touch(
                "openai",
                AiModelCatalog.DefaultOpenAiModel.ToUpperInvariant()
            );
            seeded.ApiModelHistory.Touch("openai", "custom-openai");
            new AiSettingsStore(folder).Save(seeded);

            var vm = CreateVm(folder);
            vm.LoadSettings();

            // 表示はカタログ＋カタログ外履歴のみ（カタログと同名の履歴は表示スキップ）
            CandidateNames(vm).Should().Equal([.. AiModelCatalog.OpenAiModels, "custom-openai"]);
        }
        finally
        {
            Cleanup(folder);
        }
    }

    /// <summary>プロバイダ切替で ApiModel がカタログ既定（OpenAI/Claude）／履歴先頭（ローカル LLM）になることを検証する</summary>
    [Fact(DisplayName = "プロバイダ切替で ApiModel がカタログ既定または履歴先頭になる")]
    public void ApiProviderChanged_SelectsCatalogDefaultOrHistoryFirst()
    {
        var folder = NewFolder();

        try
        {
            var vm = CreateVm(folder);
            vm.LoadSettings();

            // ローカル LLM で履歴を作っておく
            vm.ApiProvider = AiProvider.LocalLlm;
            vm.ApiModel = "hist-model";
            vm.RecordSuccessfulModel();

            // Claude へ切替 → カタログ既定（＝先頭ではない。カタログは上位モデルから並ぶ表示順）
            vm.ApiProvider = AiProvider.Claude;
            vm.ApiModel.Should().Be(AiModelCatalog.DefaultClaudeModel);
            vm.ApiModel.Should()
                .NotBe(
                    AiModelCatalog.ClaudeModels[0],
                    "既定はカタログ先頭ではなく Default* が正本であること（先頭＝最上位モデルを既定にしない）"
                );

            // OpenAI へ切替 → カタログ既定（同上）
            vm.ApiProvider = AiProvider.OpenAI;
            vm.ApiModel.Should().Be(AiModelCatalog.DefaultOpenAiModel);
            vm.ApiModel.Should().NotBe(AiModelCatalog.OpenAiModels[0]);

            // ローカル LLM へ戻すと MRU 先頭が自動選択される
            vm.ApiProvider = AiProvider.LocalLlm;
            vm.ApiModel.Should().Be("hist-model");
        }
        finally
        {
            Cleanup(folder);
        }
    }

    /// <summary>
    /// chat / mock の 2 VM が同一ファイルの共有セクション（ApiModelHistory）を共有し、かつ各自の
    /// SaveSettings が read-modify-write で互いの UI セクション（LastBackend）を消さないことを検証する。
    /// </summary>
    [Fact(DisplayName = "chat / mock は API モデル履歴を共有し互いの UI セクションを消さない")]
    public void ApiHistory_SharedAcrossDialogs_AndSaveDoesNotClobberUiSection()
    {
        var folder = NewFolder();

        try
        {
            // chat 側: 共有履歴へ記録し、接続タブ（ClaudeCode）を保存する
            var chat = CreateVm(folder, dialogKind: AiDialogKind.AiChat);
            chat.LoadSettings();
            chat.ApiProvider = AiProvider.LocalLlm;
            chat.ApiModel = "shared-model";
            chat.RecordSuccessfulModel();
            chat.SelectedBackend = ErChatBackendKind.ClaudeCode;
            chat.SaveSettings();

            // mock 側: 同じファイルの ApiModelHistory セクションを共有して履歴が見える
            var mock = CreateVm(folder, dialogKind: AiDialogKind.MockGeneration);
            mock.LoadSettings();
            mock.ApiProvider = AiProvider.LocalLlm;
            CandidateNames(mock).Should().Contain("shared-model");

            // mock 側の接続タブ（Codex）保存は、read-modify-write で chat 側セクションを消さない
            mock.SelectedBackend = ErChatBackendKind.Codex;
            mock.SaveSettings();

            var restoredChat = CreateVm(folder, dialogKind: AiDialogKind.AiChat);
            restoredChat.LoadSettings();
            restoredChat.InitialBackend.Should().Be(ErChatBackendKind.ClaudeCode);

            var restoredMock = CreateVm(folder, dialogKind: AiDialogKind.MockGeneration);
            restoredMock.LoadSettings();
            restoredMock.InitialBackend.Should().Be(ErChatBackendKind.Codex);
        }
        finally
        {
            Cleanup(folder);
        }
    }

    // ── Codex モデル MRU 履歴（非 openai プロバイダ） ──

    /// <summary>AI 設定ファイルの絶対パス（履歴が書かれていない＝ファイル非作成の検証に使う）</summary>
    private static string CodexHistoryPath(string folder) =>
        new AiSettingsStore(folder).SettingsPath;

    /// <summary>非 openai プロバイダ（ollama-launch）を含む config.toml のスタブ</summary>
    private static CodexConfigToml NonOpenAiConfig() =>
        new() { ProviderNames = new List<string> { "ollama-launch" } };

    /// <summary>非 openai プロバイダを選択済みの Codex バックエンド VM を用意する</summary>
    private static ChatConnectionSettingsViewModel CreateCodexVm(
        string folder,
        AiDialogKind dialogKind = AiDialogKind.AiChat
    )
    {
        var vm = CreateVm(folder, dialogKind, codexConfigReader: NonOpenAiConfig);
        vm.LoadSettings();
        vm.SelectedBackend = ErChatBackendKind.Codex;
        vm.CodexModelProvider = "ollama-launch";
        return vm;
    }

    /// <summary>Codex×非 openai の記録で履歴候補（IsRemovable=true）が入り、JSON へ往復することを検証する</summary>
    [Fact(DisplayName = "Codex 記録で履歴候補が入り JSON へ往復する")]
    public void RecordSuccessfulModel_Codex_AddsHistory_AndPersists()
    {
        var folder = NewFolder();

        try
        {
            var vm = CreateCodexVm(folder);

            // 初期状態（履歴なし）は空
            vm.CodexModelCandidates.Should().BeEmpty();

            vm.CodexModel = "qwen3.6:35b";
            vm.RecordSuccessfulModel();

            // 候補は履歴のみ（× 付き）
            vm.CodexModelCandidates.Select(c => c.Name).Should().Equal("qwen3.6:35b");
            vm.CodexModelCandidates[0].IsRemovable.Should().BeTrue();

            // 別インスタンスで読み戻して JSON 永続化を確認する
            var reloaded = new AiSettingsStore(folder).Load().CodexModelHistory;
            reloaded.ModelsFor("ollama-launch").Should().Equal("qwen3.6:35b");
        }
        finally
        {
            Cleanup(folder);
        }
    }

    /// <summary>プロバイダ別に履歴が分離されている（provider A の履歴は provider B に出ない）ことを検証する</summary>
    [Fact(DisplayName = "Codex 履歴はプロバイダ別に分離される")]
    public void RecordSuccessfulModel_Codex_HistoryIsolatedPerProvider()
    {
        var folder = NewFolder();

        try
        {
            // 2 プロバイダを持つ config
            var config = new CodexConfigToml
            {
                ProviderNames = new List<string> { "ollama-launch", "other-provider" },
            };
            var vm = CreateVm(folder, codexConfigReader: () => config);
            vm.LoadSettings();
            vm.SelectedBackend = ErChatBackendKind.Codex;

            // provider A（ollama-launch）で記録
            vm.CodexModelProvider = "ollama-launch";
            vm.CodexModel = "model-for-a";
            vm.RecordSuccessfulModel();

            // provider B へ切り替えると A の履歴は出ない（B の履歴は空）
            vm.CodexModelProvider = "other-provider";
            vm.CodexModelCandidates.Should().BeEmpty();

            // provider B で記録しても A の履歴と混ざらない
            vm.CodexModel = "model-for-b";
            vm.RecordSuccessfulModel();
            vm.CodexModelCandidates.Select(c => c.Name).Should().Equal("model-for-b");

            vm.CodexModelProvider = "ollama-launch";
            vm.CodexModelCandidates.Select(c => c.Name).Should().Equal("model-for-a");
        }
        finally
        {
            Cleanup(folder);
        }
    }

    /// <summary>openai プロバイダ（静的カタログ）では記録されず履歴ファイルも作られないことを検証する（ガード）</summary>
    [Fact(DisplayName = "Codex×openai では記録しない")]
    public void RecordSuccessfulModel_CodexOpenAiProvider_DoesNothing()
    {
        var folder = NewFolder();

        try
        {
            var vm = CreateVm(folder);
            vm.LoadSettings();
            vm.SelectedBackend = ErChatBackendKind.Codex;
            // 既定プロバイダは openai・モデルはカタログ既定
            vm.CodexModel = "gpt-5.4-mini";

            vm.RecordSuccessfulModel();

            vm.CodexModelCandidates.Should().OnlyContain(c => !c.IsRemovable);
            File.Exists(CodexHistoryPath(folder)).Should().BeFalse();
        }
        finally
        {
            Cleanup(folder);
        }
    }

    /// <summary>ApiKey バックエンドでは（非 openai の Codex 設定が残っていても）Codex 履歴に記録しないことを検証する（ガード）</summary>
    [Fact(DisplayName = "ApiKey バックエンドでは Codex 履歴に記録しない")]
    public void RecordSuccessfulModel_ApiKeyBackend_DoesNotRecordCodexHistory()
    {
        var folder = NewFolder();

        try
        {
            var vm = CreateCodexVm(folder);
            vm.CodexModel = "qwen3.6:35b";
            // バックエンドを API キー（OpenAI プロバイダ）へ戻す
            vm.SelectedBackend = ErChatBackendKind.ApiKey;

            vm.RecordSuccessfulModel();

            File.Exists(CodexHistoryPath(folder)).Should().BeFalse();
        }
        finally
        {
            Cleanup(folder);
        }
    }

    /// <summary>削除コマンドで履歴が候補・JSON から消え、CodexModel（選択中モデル名）は不変なことを検証する</summary>
    [Fact(DisplayName = "Codex 削除コマンドで履歴のみ消え CodexModel は残る")]
    public void RemoveCodexModelHistoryCommand_RemovesHistory_KeepsModel()
    {
        var folder = NewFolder();

        try
        {
            var vm = CreateCodexVm(folder);
            vm.CodexModel = "model-x";
            vm.RecordSuccessfulModel();
            vm.CodexModel = "model-y";
            vm.RecordSuccessfulModel();

            // 候補: 履歴 MRU（y, x）・現在の選択は "model-y"
            vm.CodexModelCandidates.Select(c => c.Name).Should().Equal("model-y", "model-x");

            vm.RemoveCodexModelHistoryCommand.Execute("model-x");

            // 履歴 "model-x" のみ消え、選択中モデルは保持される
            vm.CodexModelCandidates.Select(c => c.Name).Should().Equal("model-y");
            vm.CodexModel.Should().Be("model-y");

            var reloaded = new AiSettingsStore(folder).Load().CodexModelHistory;
            reloaded.ModelsFor("ollama-launch").Should().Equal("model-y");
        }
        finally
        {
            Cleanup(folder);
        }
    }

    /// <summary>
    /// chat / mock の 2 VM が同一ファイルの共有セクション（CodexModelHistory）を共有し、かつ各自の
    /// SaveSettings が read-modify-write で互いの UI セクション（LastBackend）を消さないことを検証する。
    /// </summary>
    [Fact(DisplayName = "chat / mock は Codex 履歴を共有し互いの UI セクションを消さない")]
    public void CodexHistory_SharedAcrossDialogs_AndSaveDoesNotClobberUiSection()
    {
        var folder = NewFolder();

        try
        {
            // chat 側: 共有 Codex 履歴へ記録し、接続タブ（Codex）を保存する
            var chat = CreateCodexVm(folder, dialogKind: AiDialogKind.AiChat);
            chat.CodexModel = "shared-codex-model";
            chat.RecordSuccessfulModel();
            chat.SaveSettings();

            // mock 側: 同じファイルの CodexModelHistory セクションを共有して履歴が見える
            var mock = CreateCodexVm(folder, dialogKind: AiDialogKind.MockGeneration);
            mock.CodexModelCandidates.Select(c => c.Name).Should().Contain("shared-codex-model");

            // mock 側の保存（既定 ClaudeCode 以外＝ここでは Codex）でも chat 側 UI セクションは残る
            mock.SelectedBackend = ErChatBackendKind.ClaudeCode;
            mock.SaveSettings();

            var restoredChat = CreateCodexVm(folder, dialogKind: AiDialogKind.AiChat);
            restoredChat.InitialBackend.Should().Be(ErChatBackendKind.Codex);

            var restoredMock = CreateVm(folder, dialogKind: AiDialogKind.MockGeneration);
            restoredMock.LoadSettings();
            restoredMock.InitialBackend.Should().Be(ErChatBackendKind.ClaudeCode);
        }
        finally
        {
            Cleanup(folder);
        }
    }

    // ── GitHub Copilot 接続（モデル候補＝実行時列挙＋MRU 履歴・固定キーの履歴・設定往復） ──

    /// <summary>Copilot の既定値（モデル空＝CLI 既定・状態は未確認の灰）を検証する</summary>
    [Fact(DisplayName = "Copilot の既定はモデル空・状態は Pending")]
    public void CopilotDefaults_EmptyModel_AndPendingStatus()
    {
        var folder = NewFolder();

        try
        {
            var vm = CreateVm(folder);

            // 空＝Copilot CLI の既定モデルに任せる（Claude Code 接続と同じ思想）
            vm.CopilotModel.Should().BeEmpty();
            vm.CopilotStatusLevel.Should().Be(ConnectionHealth.Pending);
            vm.CopilotAvailableModels.Should().BeEmpty();
            vm.CopilotModelCandidates.Should().BeEmpty();
        }
        finally
        {
            Cleanup(folder);
        }
    }

    /// <summary>実行時列挙が候補の上段（削除不可）に入ることを検証する（静的カタログを持たない Copilot の候補源）</summary>
    [Fact(DisplayName = "Copilot 候補は実行時列挙が上段（削除不可）")]
    public void CopilotAvailableModels_FillCandidatesAsNonRemovable()
    {
        var folder = NewFolder();

        try
        {
            var vm = CreateVm(folder);

            vm.CopilotAvailableModels = new List<string> { "gpt-5", "claude-sonnet-4.5" };

            vm.CopilotModelCandidates.Select(c => c.Name)
                .Should()
                .Equal("gpt-5", "claude-sonnet-4.5");
            vm.CopilotModelCandidates.Should().OnlyContain(c => !c.IsRemovable);
        }
        finally
        {
            Cleanup(folder);
        }
    }

    /// <summary>成功記録で履歴が列挙の下段（× 付き）に並び、列挙と同名の履歴は表示されないことを検証する</summary>
    [Fact(DisplayName = "Copilot 候補は実行時列挙＋履歴の 2 層で重複しない")]
    public void RecordSuccessfulModel_Copilot_AppendsHistoryBelowAvailableModels()
    {
        var folder = NewFolder();

        try
        {
            var vm = CreateVm(folder);
            vm.LoadSettings();
            vm.SelectedBackend = ErChatBackendKind.Copilot;
            vm.CopilotAvailableModels = new List<string> { "gpt-5" };

            // 列挙外のモデル（手入力）と、列挙と同名（大文字小文字違い）のモデルを両方記録する
            vm.CopilotModel = "custom-copilot-model";
            vm.RecordSuccessfulModel();
            vm.CopilotModel = "GPT-5";
            vm.RecordSuccessfulModel();

            // 表示は「列挙（削除不可）＋列挙に無い履歴（× 付き）」のみ
            vm.CopilotModelCandidates.Select(c => c.Name)
                .Should()
                .Equal("gpt-5", "custom-copilot-model");
            vm.CopilotModelCandidates[0].IsRemovable.Should().BeFalse();
            vm.CopilotModelCandidates[1].IsRemovable.Should().BeTrue();

            // JSON は固定プロバイダーキー（"copilot"）で永続化される
            var reloaded = new AiSettingsStore(folder).Load().CopilotModelHistory;
            reloaded
                .ModelsFor(CopilotSettings.HistoryProviderKey)
                .Should()
                .Equal("GPT-5", "custom-copilot-model");
        }
        finally
        {
            Cleanup(folder);
        }
    }

    /// <summary>Copilot 以外のバックエンド・空モデルでは Copilot 履歴へ記録しないことを検証する（ガード）</summary>
    [Fact(DisplayName = "Copilot 以外・空モデルでは Copilot 履歴に記録しない")]
    public void RecordSuccessfulModel_Copilot_GuardsBackendAndBlankModel()
    {
        var folder = NewFolder();

        try
        {
            var vm = CreateVm(folder);
            vm.LoadSettings();

            // ①Copilot モデルが入っていても、バックエンドが Claude Code なら記録しない
            vm.CopilotModel = "gpt-5";
            vm.SelectedBackend = ErChatBackendKind.ClaudeCode;
            vm.RecordSuccessfulModel();
            File.Exists(new AiSettingsStore(folder).SettingsPath).Should().BeFalse();

            // ②Copilot でもモデルが空（＝CLI 既定に任せる指定）なら記録するモデル名が無い
            vm.SelectedBackend = ErChatBackendKind.Copilot;
            vm.CopilotModel = "   ";
            vm.RecordSuccessfulModel();

            vm.CopilotModelCandidates.Should().BeEmpty();
            File.Exists(new AiSettingsStore(folder).SettingsPath).Should().BeFalse();
        }
        finally
        {
            Cleanup(folder);
        }
    }

    /// <summary>削除コマンドで履歴のみ消え、実行時列挙と選択中モデル（CopilotModel）は残ることを検証する</summary>
    [Fact(DisplayName = "Copilot 削除コマンドで履歴のみ消え列挙と CopilotModel は残る")]
    public void RemoveCopilotModelHistoryCommand_RemovesHistoryOnly()
    {
        var folder = NewFolder();

        try
        {
            var vm = CreateVm(folder);
            vm.LoadSettings();
            vm.SelectedBackend = ErChatBackendKind.Copilot;
            vm.CopilotAvailableModels = new List<string> { "gpt-5" };
            vm.CopilotModel = "custom-copilot-model";
            vm.RecordSuccessfulModel();

            vm.RemoveCopilotModelHistoryCommand.Execute("custom-copilot-model");

            vm.CopilotModelCandidates.Select(c => c.Name).Should().Equal("gpt-5");
            vm.CopilotModel.Should().Be("custom-copilot-model");

            var reloaded = new AiSettingsStore(folder).Load().CopilotModelHistory;
            reloaded.ModelsFor(CopilotSettings.HistoryProviderKey).Should().BeEmpty();
        }
        finally
        {
            Cleanup(folder);
        }
    }

    /// <summary>Copilot のモデル・タブ選択が SaveSettings→LoadSettings で往復し、履歴が候補へ復元されることを検証する</summary>
    [Fact(DisplayName = "Copilot のモデル・タブ選択・履歴が往復する")]
    public void SaveSettings_Copilot_RoundTrips()
    {
        var folder = NewFolder();

        try
        {
            var vm = CreateVm(folder);
            vm.LoadSettings();
            vm.SelectedBackend = ErChatBackendKind.Copilot;
            vm.CopilotModel = "claude-sonnet-4.5";
            vm.RecordSuccessfulModel();
            vm.SaveSettings();

            var restored = CreateVm(folder);
            restored.LoadSettings();

            restored.InitialBackend.Should().Be(ErChatBackendKind.Copilot);
            restored.CopilotModel.Should().Be("claude-sonnet-4.5");
            // 未接続（実行時列挙が空）でも履歴が候補として残るため、直近のモデルを選び直せる
            restored
                .CopilotModelCandidates.Select(c => c.Name)
                .Should()
                .Equal("claude-sonnet-4.5");
            restored.CopilotModelCandidates.Should().OnlyContain(c => c.IsRemovable);
        }
        finally
        {
            Cleanup(folder);
        }
    }

    /// <summary>Copilot 設定の保存が他セクション（Codex / Claude Code / UI）を消さないことを検証する</summary>
    [Fact(DisplayName = "Copilot 保存は他セクションを消さない")]
    public void SaveSettings_Copilot_DoesNotClobberOtherSections()
    {
        var folder = NewFolder();

        try
        {
            var config = new CodexConfigToml
            {
                ProviderNames = new List<string> { "ollama-launch" },
            };

            var vm = CreateVm(folder, codexConfigReader: () => config);
            vm.LoadSettings();
            vm.CodexModelProvider = "ollama-launch";
            vm.CodexModel = "gemma4:31b-cloud";
            vm.ClaudeCodeModel = "opus";
            vm.CopilotModel = "gpt-5";
            vm.SelectedBackend = ErChatBackendKind.Copilot;
            vm.SaveSettings();

            var saved = new AiSettingsStore(folder).Load();
            saved.Copilot.Model.Should().Be("gpt-5");
            saved.CodexAppServer.Model.Should().Be("gemma4:31b-cloud");
            saved.ClaudeCode.Model.Should().Be("opus");
            saved.ChatUi.LastBackend.Should().Be(nameof(ErChatBackendKind.Copilot));
        }
        finally
        {
            Cleanup(folder);
        }
    }

    // ── 1-7 の対応（API キーの保存の確定点・候補の再構築の読み直し・接続の組み立て） ──

    /// <summary>
    /// API キーは打鍵ごとには保存せず、確定（<see cref="ChatConnectionSettingsViewModel.CommitApiKey()"/>）で最後の値を 1 回だけ保存することを検証する。
    /// </summary>
    [Fact(DisplayName = "API キーは打鍵ごとには保存せず確定で 1 回保存する")]
    public void ApiKey_IsNotSavedPerKeystroke_ButOnCommit()
    {
        var folder = NewFolder();

        try
        {
            var spy = new KeySaverSpy();
            var vm = CreateVm(folder, apiKeySaver: spy.Save);

            vm.ApiKey = "s";
            vm.ApiKey = "sk";
            vm.ApiKey = "sk-1";
            spy.Saves.Should().BeEmpty();

            vm.CommitApiKey();
            spy.Saves.Should().Equal(("OpenAiApiKey", "sk-1"));

            // 変更が無ければ確定しても書かない
            vm.CommitApiKey();
            spy.Saves.Should().ContainSingle();
        }
        finally
        {
            Cleanup(folder);
        }
    }

    /// <summary>
    /// 入力中にプロバイダーを切り替えると、入力したキーを切替前のプロバイダーのスロットへ保存することを検証する。
    /// </summary>
    /// <remarks>
    /// 切替後は別のプロバイダーのキーを読み直すので、ここで保存しないと入力したキーが失われる。
    /// </remarks>
    [Fact(DisplayName = "プロバイダーを切り替えると入力中のキーを切替前のスロットへ保存する")]
    public void ProviderSwitch_SavesTypedKeyToPreviousSlot()
    {
        var folder = NewFolder();

        try
        {
            var spy = new KeySaverSpy();
            var vm = CreateVm(folder, apiKeyLoader: _ => "loaded", apiKeySaver: spy.Save);

            vm.ApiKey = "sk-openai";
            vm.ApiProvider = AiProvider.Claude;

            spy.Saves.Should().Equal(("OpenAiApiKey", "sk-openai"));
            vm.ApiKey.Should().Be("loaded", "切替後は切替先のキーを読み直す");

            // 読み直したキーは未保存の扱いにしない
            vm.CommitApiKey();
            spy.Saves.Should().ContainSingle();
        }
        finally
        {
            Cleanup(folder);
        }
    }

    /// <summary>設定の保存（ウィンドウを閉じるとき）で、入力中の API キーも保存することを検証する。</summary>
    [Fact(DisplayName = "設定の保存で入力中の API キーも保存する")]
    public void SaveSettings_CommitsTypedApiKey()
    {
        var folder = NewFolder();

        try
        {
            var spy = new KeySaverSpy();
            var vm = CreateVm(folder, apiKeySaver: spy.Save);
            vm.ApiKey = "sk-typed";

            vm.SaveSettings();

            spy.Saves.Should().Equal(("OpenAiApiKey", "sk-typed"));
            File.ReadAllText(Path.Combine(folder, AiSettingsStore.DefaultFileName))
                .Should()
                .NotContain("sk-typed", "キーは設定の JSON へは入れない");
        }
        finally
        {
            Cleanup(folder);
        }
    }

    /// <summary>
    /// キーの保存に失敗しても、入力は反映されて変更通知も出て、次の確定で保存し直すことを検証する。
    /// </summary>
    /// <remarks>
    /// 打鍵ごとに保存していたときは、保存の例外が変更通知より前に抜けていた
    /// （束縛が例外を黙って捨てるので、値は入るのに通知が出ず、送信できる状態の再評価が漏れた。実測）。
    /// </remarks>
    [Fact(DisplayName = "キーの保存が失敗しても通知は出て次の確定で保存し直す")]
    public void ApiKeySaveFailure_KeepsNotifying_AndRetriesOnNextCommit()
    {
        var folder = NewFolder();

        try
        {
            var fail = true;
            var saved = new List<(string, string)>();
            var vm = CreateVm(
                folder,
                apiKeySaver: (slot, value) =>
                {
                    if (fail)
                    {
                        throw new System.Security.Cryptography.CryptographicException("dpapi");
                    }

                    saved.Add((slot, value));
                }
            );
            var changes = RecordChanges(vm);

            vm.ApiKey = "sk-1";
            changes.Should().Contain(nameof(ChatConnectionSettingsViewModel.ApiKey));

            var commit = () => vm.CommitApiKey();
            commit.Should().NotThrow();
            saved.Should().BeEmpty();

            fail = false;
            vm.CommitApiKey();
            saved.Should().Equal(("OpenAiApiKey", "sk-1"));
        }
        finally
        {
            Cleanup(folder);
        }
    }

    /// <summary>
    /// Copilot のモデル一覧に同じ内容が届いても候補を作り直さず、内容が変わったときだけ作り直すことを検証する。
    /// </summary>
    /// <remarks>
    /// 接続の状態通知のたびに同じ一覧が届き、作り直しは共有ファイルの読み直しを伴う。
    /// </remarks>
    [Fact(DisplayName = "Copilot のモデル一覧が同じ内容なら候補を作り直さない")]
    public void CopilotAvailableModels_SameContent_DoesNotRebuild()
    {
        var folder = NewFolder();

        try
        {
            var vm = CreateVm(folder);
            vm.CopilotAvailableModels = new List<string> { "gpt-a", "gpt-b" };

            var rebuilds = 0;
            vm.CopilotModelCandidates.CollectionChanged += (_, _) => rebuilds++;

            vm.CopilotAvailableModels = new List<string> { "gpt-a", "gpt-b" };
            rebuilds.Should().Be(0);

            vm.CopilotAvailableModels = new List<string> { "gpt-a", "gpt-c" };
            rebuilds.Should().BeGreaterThan(0);
            vm.CopilotModelCandidates.Select(c => c.Name).Should().Contain("gpt-c");
        }
        finally
        {
            Cleanup(folder);
        }
    }

    /// <summary>
    /// 読み込みの後の候補の再構築は、もう一方のダイアログが共有ファイルへ書いた履歴を拾うことを検証する
    /// （読み込み中だけ使い回す設定が、読み込みの後まで残っていない）。
    /// </summary>
    [Fact(DisplayName = "読み込みの後の候補の再構築は共有ファイルの最新を拾う")]
    public void CandidateRebuild_AfterLoad_ReadsLatestSharedFile()
    {
        var folder = NewFolder();

        try
        {
            var vm = CreateVm(folder);
            vm.LoadSettings();

            // もう一方のダイアログが、カタログに無いモデルの使用を履歴へ記録した
            var otherStore = new AiSettingsStore(folder);
            var settings = otherStore.Load();
            settings.ApiModelHistory.Touch("openai", "custom-model-x");
            otherStore.Save(settings);

            // プロバイダーの切替で候補を作り直す
            vm.ApiProvider = AiProvider.Claude;
            vm.ApiProvider = AiProvider.OpenAI;

            vm.ApiModelCandidates.Select(c => c.Name).Should().Contain("custom-model-x");
        }
        finally
        {
            Cleanup(folder);
        }
    }

    /// <summary>
    /// 接続の組み立てで、エンドポイント上書きがローカル LLM のときだけ渡ることを検証する。
    /// 欄が非表示のまま残った URL が OpenAI 接続へ紛れ込む事故（回帰）を防ぐ。
    /// </summary>
    [Fact(DisplayName = "接続のエンドポイント上書きは Local LLM のときだけ渡る")]
    public void BuildOpenAiConnection_AppliesEndpointOnlyForLocalLlm()
    {
        var folder = NewFolder();

        try
        {
            var vm = CreateVm(folder);
            vm.ApiProvider = AiProvider.LocalLlm;
            vm.EndpointOverride = "http://127.0.0.1:1234/v1";

            var local = vm.BuildOpenAiConnection();
            local.Provider.Should().Be(AiProvider.LocalLlm);
            local.EndpointOverride.Should().Be("http://127.0.0.1:1234/v1");

            // 欄は非表示になるが値は残る。それでも OpenAI 接続には渡さない
            vm.ApiProvider = AiProvider.OpenAI;
            vm.EndpointOverride.Should().Be("http://127.0.0.1:1234/v1");

            var openAi = vm.BuildOpenAiConnection();
            openAi.EndpointOverride.Should().BeNull();
            openAi.ResolveEndpoint().Should().Be("https://api.openai.com/v1");
        }
        finally
        {
            Cleanup(folder);
        }
    }

    /// <summary>ローカル LLM のキーは、入力すればそのまま接続へ渡り、未入力ならダミーになることを検証する</summary>
    [Fact(DisplayName = "Local LLM のキーは入力時そのまま・未入力でダミー")]
    public void BuildOpenAiConnection_LocalLlmApiKey_IsOptional()
    {
        var folder = NewFolder();

        try
        {
            var vm = CreateVm(folder);
            vm.ApiProvider = AiProvider.LocalLlm;

            vm.ApiKey = "local-secret";
            var withKey = vm.BuildOpenAiConnection();
            withKey.ApiKey.Should().Be("local-secret");
            withKey.ResolveApiKey().Should().Be("local-secret");

            vm.ApiKey = string.Empty;
            vm.BuildOpenAiConnection()
                .ResolveApiKey()
                .Should()
                .Be(LocalLlmDefaults.PlaceholderApiKey);
        }
        finally
        {
            Cleanup(folder);
        }
    }

    /// <summary>
    /// API キー接続のエンジンは、送信できるかをこの VM の現在の入力から判定する（組み立て後の変更も効く）ことを検証する。
    /// </summary>
    [Fact(DisplayName = "API キー接続のエンジンは送信可否を現在の入力から判定する")]
    public void CreateApiKeyEngine_ReadinessFollowsCurrentInput()
    {
        var folder = NewFolder();

        try
        {
            var vm = CreateVm(folder);
            var engine = vm.CreateApiKeyEngine(
                QuickER.AI.Mock.MockDesignProfile.FolderMockDesign,
                new NoopToolHost(),
                new SyncUiDispatcher()
            );

            vm.ApiProvider = AiProvider.OpenAI;
            vm.ApiKey = string.Empty;
            engine.IsReady.Should().BeFalse();
            vm.IsApiKeyConnectionReady.Should().BeFalse();

            vm.ApiKey = "sk-1";
            engine.IsReady.Should().BeTrue();
            vm.IsApiKeyConnectionReady.Should().BeTrue();

            // ローカル LLM はキーが無くても送信できる
            vm.ApiProvider = AiProvider.LocalLlm;
            vm.ApiKey = string.Empty;
            engine.IsReady.Should().BeTrue();
            engine
                .AttachmentSupport.Should()
                .Be(AttachmentSupportResolver.ForApiKeyProvider(AiProvider.LocalLlm));
        }
        finally
        {
            Cleanup(folder);
        }
    }

    /// <summary>UI スレッドへのマーシャリングを同期実行で代替するテスト用ディスパッチャ</summary>
    private sealed class SyncUiDispatcher : IUiDispatcher
    {
        public T Invoke<T>(Func<T> func) => func();
    }

    /// <summary>何もしないツールホスト</summary>
    private sealed class NoopToolHost : IErDiagramToolHost
    {
        public (string Result, bool Success) Execute(string toolName, string argumentsJson) =>
            (string.Empty, true);
    }
}
