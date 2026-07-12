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
            apiKeySaver: apiKeySaver ?? ((_, _) => { }),
            // モデル履歴ファイル（API キー / Codex）を一時フォルダへ隔離する（実 %APPDATA% を保護）
            apiModelHistoryStore: new ApiModelHistoryStore(folder),
            codexModelHistoryStore: new CodexModelHistoryStore(folder)
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

            changed.Should().Contain(nameof(vm.ShowApiKey));
            changed.Should().Contain(nameof(vm.ShowEndpoint));

            // Ollama は履歴が初期空のため、モデルは空・候補も空（IndexOutOfRange を起こさない）
            vm.ApiModel.Should().BeEmpty();
            vm.ApiModelCandidates.Should().BeEmpty();

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
            var history = new ProviderModelHistory();
            history.Touch("ollama-launch", "gemma4:31b-cloud");
            new CodexModelHistoryStore(folder).Save(history);

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

    /// <summary>候補リストに無い保存済みプロバイダーは openai へフォールバックすることを検証する（リスト選択のみのため）</summary>
    [Fact(DisplayName = "候補に無い保存済み Codex プロバイダーは openai へフォールバック")]
    public void LoadSettings_UnknownCodexProvider_FallsBackToOpenAi()
    {
        var folder = NewFolder();

        try
        {
            // config.toml から消えたプロバイダーが保存されている状況を作る
            new CodexAppServerSettingsStore(folder).Save(
                new CodexAppServerSettings { ModelProvider = "removed-provider", Model = "m" }
            );

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
            new CodexAppServerSettingsStore(folder).Save(
                new CodexAppServerSettings { ModelProvider = "OLLAMA-LAUNCH", Model = "m" }
            );

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

    /// <summary>API キー接続のモデル履歴ファイルの絶対パス（存在確認・非作成の検証に使う）</summary>
    private static string ApiHistoryPath(string folder) =>
        new ApiModelHistoryStore(folder).SettingsPath;

    /// <summary>候補のモデル名一覧（アサート簡略化用）</summary>
    private static IEnumerable<string> CandidateNames(ChatConnectionSettingsViewModel vm) =>
        vm.ApiModelCandidates.Select(c => c.Name);

    /// <summary>Ollama（カタログ無し）の成功記録で候補先頭へ入り、JSON へ往復することを検証する</summary>
    [Fact(DisplayName = "Ollama 記録で候補先頭に入り JSON へ往復する")]
    public void RecordSuccessfulModel_Ollama_AddsToCandidates_AndPersists()
    {
        var folder = NewFolder();

        try
        {
            var vm = CreateVm(folder);
            vm.ApiProvider = AiProvider.Ollama;
            vm.ApiModel = "qwen3.6:35b";

            vm.RecordSuccessfulModel();

            // Ollama はカタログが無いため候補は履歴のみ（× 付き）
            CandidateNames(vm).Should().Equal("qwen3.6:35b");
            vm.ApiModelCandidates[0].IsRemovable.Should().BeTrue();

            // 別インスタンスで読み戻して JSON 永続化を確認する（キーは "ollama"）
            var reloaded = new ApiModelHistoryStore(folder).Load();
            reloaded.ModelsFor("ollama").Should().Equal("qwen3.6:35b");
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
            var reloaded = new ApiModelHistoryStore(folder).Load();
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
            var reloaded = new ApiModelHistoryStore(folder).Load();
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
            vm.ApiProvider = AiProvider.Ollama;

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
            vm.ApiProvider = AiProvider.Ollama;

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
            vm.ApiProvider = AiProvider.Ollama;
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
            vm.ApiProvider = AiProvider.Ollama;
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

            var reloaded = new ApiModelHistoryStore(folder).Load();
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
            var seeded = new ProviderModelHistory();
            seeded.Touch("openai", AiModelCatalog.DefaultOpenAiModel.ToUpperInvariant());
            seeded.Touch("openai", "custom-openai");
            new ApiModelHistoryStore(folder).Save(seeded);

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

    /// <summary>プロバイダ切替で ApiModel がカタログ先頭（OpenAI/Claude）／履歴先頭（Ollama）になることを検証する</summary>
    [Fact(DisplayName = "プロバイダ切替で ApiModel がカタログ先頭または履歴先頭になる")]
    public void ApiProviderChanged_SelectsCatalogOrHistoryFirst()
    {
        var folder = NewFolder();

        try
        {
            var vm = CreateVm(folder);
            vm.LoadSettings();

            // Ollama で履歴を作っておく
            vm.ApiProvider = AiProvider.Ollama;
            vm.ApiModel = "hist-model";
            vm.RecordSuccessfulModel();

            // Claude へ切替 → カタログ先頭（既定モデル）
            vm.ApiProvider = AiProvider.Claude;
            vm.ApiModel.Should().Be(AiModelCatalog.DefaultClaudeModel);

            // OpenAI へ切替 → カタログ先頭（既定モデル）
            vm.ApiProvider = AiProvider.OpenAI;
            vm.ApiModel.Should().Be(AiModelCatalog.DefaultOpenAiModel);

            // Ollama へ戻すと MRU 先頭が自動選択される
            vm.ApiProvider = AiProvider.Ollama;
            vm.ApiModel.Should().Be("hist-model");
        }
        finally
        {
            Cleanup(folder);
        }
    }

    /// <summary>uiFileName の異なる 2 VM（chat/mock 相当）が同一フォルダで履歴を共有することを検証する</summary>
    [Fact(DisplayName = "chat / mock の 2 VM が API モデル履歴を共有する")]
    public void ApiHistory_SharedAcrossDialogs()
    {
        var folder = NewFolder();

        try
        {
            var chat = CreateVm(folder, uiFileName: "ai-chat-ui.json");
            chat.ApiProvider = AiProvider.Ollama;
            chat.ApiModel = "shared-model";
            chat.RecordSuccessfulModel();

            // 別ダイアログ相当（別 UI ファイル名）でも同じ履歴ファイルを共有する
            var mock = CreateVm(folder, uiFileName: "mock-generation-ui.json");
            mock.LoadSettings();
            mock.ApiProvider = AiProvider.Ollama;

            CandidateNames(mock).Should().Contain("shared-model");
        }
        finally
        {
            Cleanup(folder);
        }
    }

    // ── Codex モデル MRU 履歴（非 openai プロバイダ） ──

    /// <summary>Codex モデルの履歴ファイルの絶対パス（存在確認・非作成の検証に使う）</summary>
    private static string CodexHistoryPath(string folder) =>
        new CodexModelHistoryStore(folder).SettingsPath;

    /// <summary>非 openai プロバイダ（ollama-launch）を含む config.toml のスタブ</summary>
    private static CodexConfigToml NonOpenAiConfig() =>
        new() { ProviderNames = new List<string> { "ollama-launch" } };

    /// <summary>非 openai プロバイダを選択済みの Codex バックエンド VM を用意する</summary>
    private static ChatConnectionSettingsViewModel CreateCodexVm(
        string folder,
        string uiFileName = "ai-chat-ui.json"
    )
    {
        var vm = CreateVm(folder, uiFileName, codexConfigReader: NonOpenAiConfig);
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
            var reloaded = new CodexModelHistoryStore(folder).Load();
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

            var reloaded = new CodexModelHistoryStore(folder).Load();
            reloaded.ModelsFor("ollama-launch").Should().Equal("model-y");
        }
        finally
        {
            Cleanup(folder);
        }
    }

    /// <summary>uiFileName の異なる 2 VM（chat/mock 相当）が同一フォルダで Codex 履歴を共有することを検証する</summary>
    [Fact(DisplayName = "chat / mock の 2 VM が Codex 履歴を共有する")]
    public void CodexHistory_SharedAcrossDialogs()
    {
        var folder = NewFolder();

        try
        {
            var chat = CreateCodexVm(folder, uiFileName: "ai-chat-ui.json");
            chat.CodexModel = "shared-codex-model";
            chat.RecordSuccessfulModel();

            // 別ダイアログ相当（別 UI ファイル名）でも同じ履歴ファイルを共有する
            var mock = CreateCodexVm(folder, uiFileName: "mock-generation-ui.json");

            mock.CodexModelCandidates.Select(c => c.Name).Should().Contain("shared-codex-model");
        }
        finally
        {
            Cleanup(folder);
        }
    }
}
