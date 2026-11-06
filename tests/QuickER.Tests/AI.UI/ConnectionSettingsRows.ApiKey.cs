using System.IO;
using AwesomeAssertions;
using QuickER.AI;
using QuickER.AI.UI;
using QuickER.Tests.TestSupport.Engines;
using static QuickER.Tests.AI.UI.ConnectionArea;
using static QuickER.Tests.AI.UI.ConnectionOp;

namespace QuickER.Tests.AI.UI;

internal static partial class ConnectionSettingsRows
{
    /// <summary>API キー接続（プロバイダー・モデル・キー・エンドポイント）と既定</summary>
    private static IEnumerable<ScenarioRow<ConnectionSettingsCase>> ApiKeyRows() =>
        [
            Row(
                "common-defaults-api-key-backend-and-empty-key",
                "既定値は ApiKey バックエンド・ApiKey は空文字",
                [(Common, Defaults), (ApiKey, Defaults)],
                h =>
                {
                    var vm = h.Create();

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
            ),
            Row(
                "common-selected-backend-change-notifies-backend-flags",
                "SelectedBackend 変更で Is* 4 通知が発火する",
                [(Common, Change)],
                h =>
                {
                    var vm = h.Create();
                    var changed = ConnectionSettingsHarness.RecordChanges(vm);

                    vm.SelectedBackend = ErChatBackendKind.Codex;

                    changed.Should().Contain(nameof(vm.IsApiKeyBackend));
                    changed.Should().Contain(nameof(vm.IsCodexBackend));
                    changed.Should().Contain(nameof(vm.IsClaudeCodeBackend));
                    changed.Should().Contain(nameof(vm.IsCopilotBackend));
                    vm.IsCodexBackend.Should().BeTrue();
                }
            ),
            Row(
                "apikey-local-llm-switch-resets-model-and-fills-endpoint",
                "ApiProvider を Local LLM へ切替で候補・Show*・ApiModel リセット・エンドポイント補完",
                [(ApiKey, Change)],
                h =>
                {
                    var vm = h.Create();
                    var changed = ConnectionSettingsHarness.RecordChanges(vm);

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
            ),
            Row(
                "apikey-provider-switch-reloads-key",
                "プロバイダー切替で保存済みキーを読み直す",
                [(ApiKey, Change), (ApiKey, Load)],
                h =>
                {
                    // OpenAI=openai-key / Claude=claude-key を返す loader
                    h.ApiKeyLoader = slot => slot == "OpenAiApiKey" ? "openai-key" : "claude-key";
                    var vm = h.Create();

                    // 初期化で OpenAI 用キーを読み込む
                    vm.Initialize();
                    vm.ApiKey.Should().Be("openai-key");

                    // Claude へ切替でそのプロバイダー用キーへ読み直される
                    vm.ApiProvider = AiProvider.Claude;
                    vm.ApiKey.Should().Be("claude-key");
                }
            ),
            Row(
                "apikey-key-reload-does-not-save",
                "キー読み直し中は saver が発火しない",
                [(ApiKey, Change), (ApiKey, CommitKey)],
                h =>
                {
                    h.ApiKeyLoader = _ => "loaded-key";
                    var vm = h.Create();

                    // プロバイダー切替はキー読み直しを伴うが、読み直し中の ApiKey 変更で保存してはいけない
                    vm.ApiProvider = AiProvider.Claude;

                    h.Saves.Should().BeEmpty();
                }
            ),
            Row(
                "apikey-commit-saves-to-current-provider-slot",
                "手入力キーは確定時に現在プロバイダーの保存名で保存される",
                [(ApiKey, CommitKey)],
                h =>
                {
                    var vm = h.Create();

                    vm.ApiKey = "sk-manual";
                    vm.CommitApiKey();

                    h.Saves.Should().ContainSingle();
                    h.Saves[0].Should().Be(("OpenAiApiKey", "sk-manual"));
                }
            ),
            Row(
                "apikey-save-off-saves-empty",
                "SaveApiKey=false で空文字保存される",
                [(ApiKey, CommitKey)],
                h =>
                {
                    var vm = h.Create();
                    vm.ApiKey = "sk-manual";
                    h.Saves.Clear();

                    vm.SaveApiKey = false;

                    h.Saves.Should().ContainSingle();
                    h.Saves[0].Should().Be(("OpenAiApiKey", string.Empty));
                }
            ),
            Row(
                "apikey-endpoint-override-effective-only-for-local-llm",
                "エンドポイント上書きは Local LLM のときだけ有効",
                [(ApiKey, Change)],
                h =>
                {
                    var vm = h.Create();

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
            ),
            Row(
                "apikey-blank-endpoint-is-not-override",
                "空白のみのエンドポイントは上書きとして扱わない",
                [(ApiKey, Change)],
                h =>
                {
                    var vm = h.Create();
                    vm.ApiProvider = AiProvider.LocalLlm;
                    vm.EndpointOverride = "   ";

                    vm.EffectiveEndpointOverride.Should().BeNull();
                }
            ),
            Row(
                "apikey-local-llm-key-persisted-to-own-slot",
                "Local LLM のキーは確定時に専用スロットへ永続化される",
                [(ApiKey, CommitKey)],
                h =>
                {
                    var vm = h.Create();
                    vm.ApiProvider = AiProvider.LocalLlm;
                    h.Saves.Clear();

                    vm.ApiKey = "local-secret";
                    vm.CommitApiKey();

                    h.Saves.Should().ContainSingle();
                    h.Saves[0].Slot.Should().Be("LocalLlmApiKey");
                    h.Saves[0].Value.Should().Be("local-secret");
                }
            ),
            Row(
                "apikey-record-local-llm-adds-candidate-and-persists",
                "Local LLM 記録で候補先頭に入り JSON へ往復する",
                [(ApiKey, ConnectionOp.Record)],
                h =>
                {
                    var vm = h.Create();
                    vm.ApiProvider = AiProvider.LocalLlm;
                    vm.ApiModel = "qwen3.6:35b";

                    vm.RecordSuccessfulModel();

                    // ローカル LLM はカタログが無いため候補は履歴のみ（× 付き）
                    CandidateNames(vm).Should().Equal("qwen3.6:35b");
                    vm.ApiModelCandidates[0].IsRemovable.Should().BeTrue();

                    // 別インスタンスで読み戻して JSON 永続化を確認する（キーは enum 名の小文字＝"localllm"）
                    var reloaded = h.Store().Load().ApiModelHistory;
                    reloaded.ModelsFor("localllm").Should().Equal("qwen3.6:35b");
                }
            ),
            Row(
                "apikey-record-openai-custom-model-below-catalog",
                "OpenAI のカタログ外モデル記録でカタログの下に × 付きで出る",
                [(ApiKey, ConnectionOp.Record)],
                h =>
                {
                    var vm = h.Create();
                    vm.LoadSettings();

                    // 既定は OpenAI。カタログに無いモデルを手入力した状況を作る
                    vm.ApiModel = "my-custom-gpt";

                    vm.RecordSuccessfulModel();

                    // カタログ（削除不可）が上に固定され、履歴（× 付き）がその下に並ぶ
                    CandidateNames(vm)
                        .Should()
                        .Equal([.. AiModelCatalog.OpenAiModels, "my-custom-gpt"]);
                    vm.ApiModelCandidates[^1].IsRemovable.Should().BeTrue();
                    vm.ApiModelCandidates.Take(AiModelCatalog.OpenAiModels.Count)
                        .Should()
                        .OnlyContain(c => !c.IsRemovable);

                    // JSON はプロバイダキー "openai" で永続化される
                    var reloaded = h.Store().Load().ApiModelHistory;
                    reloaded.ModelsFor("openai").Should().Equal("my-custom-gpt");
                }
            ),
            Row(
                "apikey-record-catalog-model-does-not-record",
                "カタログ在中モデルの成功では記録しない",
                [(ApiKey, ConnectionOp.Record)],
                h =>
                {
                    var vm = h.Create();
                    vm.LoadSettings();

                    // カタログ在中モデル（大文字小文字違いでも一致とみなす）
                    vm.ApiModel = AiModelCatalog.DefaultOpenAiModel.ToUpperInvariant();

                    vm.RecordSuccessfulModel();

                    // 候補はカタログのみ・履歴ファイルは作られない
                    vm.ApiModelCandidates.Should().OnlyContain(c => !c.IsRemovable);
                    File.Exists(h.SettingsPath).Should().BeFalse();
                }
            ),
            Row(
                "apikey-history-isolated-per-provider",
                "API 履歴はプロバイダ別に分離される",
                [(ApiKey, ConnectionOp.Record)],
                h =>
                {
                    var vm = h.Create();
                    vm.LoadSettings();

                    // OpenAI でカタログ外モデルを記録
                    vm.ApiModel = "custom-openai";
                    vm.RecordSuccessfulModel();

                    // Claude でもカタログ外モデルを記録
                    vm.ApiProvider = AiProvider.Claude;
                    vm.ApiModel = "custom-claude";
                    vm.RecordSuccessfulModel();

                    // Claude の候補にはカタログ＋claude の履歴のみ（openai の履歴は出ない）
                    CandidateNames(vm)
                        .Should()
                        .Equal([.. AiModelCatalog.ClaudeModels, "custom-claude"]);

                    // JSON もプロバイダ別に分離される
                    var reloaded = h.Store().Load().ApiModelHistory;
                    reloaded.ModelsFor("openai").Should().Equal("custom-openai");
                    reloaded.ModelsFor("claude").Should().Equal("custom-claude");
                }
            ),
            Row(
                "apikey-record-is-mru-dedup-new-spelling-first",
                "記録は MRU（重複排除・新表記採用・先頭挿入）で並ぶ",
                [(ApiKey, ConnectionOp.Record)],
                h =>
                {
                    var vm = h.Create();
                    vm.ApiProvider = AiProvider.LocalLlm;

                    vm.ApiModel = "a";
                    vm.RecordSuccessfulModel();
                    vm.ApiModel = "b";
                    vm.RecordSuccessfulModel();
                    vm.ApiModel = "A";
                    vm.RecordSuccessfulModel();

                    CandidateNames(vm).Should().Equal("A", "b");
                }
            ),
            Row(
                "apikey-record-trims-to-max-entries",
                "記録は上限 20 件で最古を切り詰める",
                [(ApiKey, ConnectionOp.Record)],
                h =>
                {
                    var vm = h.Create();
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
            ),
            Row(
                "apikey-record-ignored-on-codex-backend",
                "Codex バックエンドでは API 履歴に記録しない",
                [(ApiKey, ConnectionOp.Record)],
                h =>
                {
                    var vm = h.Create();
                    vm.ApiProvider = AiProvider.LocalLlm;
                    vm.ApiModel = "qwen3.6:35b";
                    vm.SelectedBackend = ErChatBackendKind.Codex;

                    vm.RecordSuccessfulModel();

                    File.Exists(h.SettingsPath).Should().BeFalse();
                }
            ),
            Row(
                "apikey-record-ignored-for-blank-model",
                "空白モデル名では記録しない",
                [(ApiKey, ConnectionOp.Record)],
                h =>
                {
                    var vm = h.Create();
                    vm.ApiProvider = AiProvider.LocalLlm;
                    vm.ApiModel = "   ";

                    vm.RecordSuccessfulModel();

                    vm.ApiModelCandidates.Should().BeEmpty();
                    File.Exists(h.SettingsPath).Should().BeFalse();
                }
            ),
            Row(
                "apikey-remove-history-keeps-catalog-and-model",
                "削除コマンドで履歴のみ消えカタログと ApiModel は残る",
                [(ApiKey, ConnectionOp.Record), (ApiKey, Remove)],
                h =>
                {
                    var vm = h.Create();
                    vm.LoadSettings();

                    // OpenAI でカタログ外モデルを記録してから、既定モデルへ戻す
                    vm.ApiModel = "custom-openai";
                    vm.RecordSuccessfulModel();
                    vm.ApiModel = AiModelCatalog.DefaultOpenAiModel;

                    vm.RemoveApiModelHistoryCommand.Execute("custom-openai");

                    // 履歴のみ消え、カタログは残り、選択中モデルは保持される
                    CandidateNames(vm).Should().Equal(AiModelCatalog.OpenAiModels);
                    vm.ApiModel.Should().Be(AiModelCatalog.DefaultOpenAiModel);

                    var reloaded = h.Store().Load().ApiModelHistory;
                    reloaded.ModelsFor("openai").Should().BeEmpty();
                }
            ),
            Row(
                "apikey-history-duplicating-catalog-is-hidden",
                "カタログと同名の履歴は表示しない",
                [(ApiKey, Load)],
                h =>
                {
                    // カタログ既定モデルと大文字小文字違いの履歴を直接仕込む
                    var seeded = h.Store().Load();
                    seeded.ApiModelHistory.Touch(
                        "openai",
                        AiModelCatalog.DefaultOpenAiModel.ToUpperInvariant()
                    );
                    seeded.ApiModelHistory.Touch("openai", "custom-openai");
                    h.Store().Save(seeded);

                    var vm = h.Create();
                    vm.LoadSettings();

                    // 表示はカタログ＋カタログ外履歴のみ（カタログと同名の履歴は表示スキップ）
                    CandidateNames(vm)
                        .Should()
                        .Equal([.. AiModelCatalog.OpenAiModels, "custom-openai"]);
                }
            ),
            Row(
                "apikey-provider-switch-selects-catalog-default-or-history-head",
                "プロバイダ切替で ApiModel がカタログ既定または履歴先頭になる",
                [(ApiKey, Change), (ApiKey, ConnectionOp.Record)],
                h =>
                {
                    var vm = h.Create();
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
            ),
            Row(
                "apikey-key-saved-on-commit-not-per-keystroke",
                "API キーは打鍵ごとには保存せず確定で 1 回保存する",
                [(ApiKey, CommitKey)],
                h =>
                {
                    var vm = h.Create();

                    vm.ApiKey = "s";
                    vm.ApiKey = "sk";
                    vm.ApiKey = "sk-1";
                    h.Saves.Should().BeEmpty();

                    vm.CommitApiKey();
                    h.Saves.Should().Equal(("OpenAiApiKey", "sk-1"));

                    // 変更が無ければ確定しても書かない
                    vm.CommitApiKey();
                    h.Saves.Should().ContainSingle();
                }
            ),
            Row(
                "apikey-provider-switch-saves-typed-key-to-previous-slot",
                "プロバイダーを切り替えると入力中のキーを切替前のスロットへ保存する",
                [(ApiKey, CommitKey), (ApiKey, Change)],
                h =>
                {
                    h.ApiKeyLoader = _ => "loaded";
                    var vm = h.Create();

                    vm.ApiKey = "sk-openai";
                    vm.ApiProvider = AiProvider.Claude;

                    h.Saves.Should().Equal(("OpenAiApiKey", "sk-openai"));
                    vm.ApiKey.Should().Be("loaded", "切替後は切替先のキーを読み直す");

                    // 読み直したキーは未保存の扱いにしない
                    vm.CommitApiKey();
                    h.Saves.Should().ContainSingle();
                }
            ),
            Row(
                "apikey-save-settings-commits-typed-key",
                "設定の保存で入力中の API キーも保存する",
                [(ApiKey, Save), (ApiKey, CommitKey)],
                h =>
                {
                    var vm = h.Create();
                    vm.ApiKey = "sk-typed";

                    h.SaveSettings(vm);

                    h.Saves.Should().Equal(("OpenAiApiKey", "sk-typed"));
                    File.ReadAllText(Path.Combine(h.Folder, AiSettingsStore.DefaultFileName))
                        .Should()
                        .NotContain("sk-typed", "キーは設定の JSON へは入れない");
                }
            ),
            Row(
                "apikey-candidate-rebuild-after-load-reads-latest-shared-file",
                "読み込みの後の候補の再構築は共有ファイルの最新を拾う",
                [(ApiKey, Load), (ApiKey, Change)],
                h =>
                {
                    var vm = h.Create();
                    vm.LoadSettings();

                    // もう一方のダイアログが、カタログに無いモデルの使用を履歴へ記録した
                    var otherStore = h.Store();
                    var settings = otherStore.Load();
                    settings.ApiModelHistory.Touch("openai", "custom-model-x");
                    otherStore.Save(settings);

                    // プロバイダーの切替で候補を作り直す
                    vm.ApiProvider = AiProvider.Claude;
                    vm.ApiProvider = AiProvider.OpenAI;

                    vm.ApiModelCandidates.Select(c => c.Name).Should().Contain("custom-model-x");
                }
            ),
            Row(
                "apikey-connection-endpoint-override-only-for-local-llm",
                "接続のエンドポイント上書きは Local LLM のときだけ渡る",
                [(ApiKey, Build)],
                h =>
                {
                    var vm = h.Create();
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
            ),
            Row(
                "apikey-connection-local-llm-key-optional",
                "Local LLM のキーは入力時そのまま・未入力でダミー",
                [(ApiKey, Build)],
                h =>
                {
                    var vm = h.Create();
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
            ),
            Row(
                "apikey-engine-readiness-follows-current-input",
                "API キー接続のエンジンは送信可否を現在の入力から判定する",
                [(ApiKey, Build), (ApiKey, Change)],
                h =>
                {
                    var vm = h.Create();
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
            ),
        ];
}
