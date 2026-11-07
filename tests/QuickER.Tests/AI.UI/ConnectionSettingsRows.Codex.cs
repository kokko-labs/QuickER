using System.IO;
using AwesomeAssertions;
using QuickER.AI;
using QuickER.Tests.TestSupport.Engines;
using static QuickER.Tests.AI.UI.ConnectionArea;
using static QuickER.Tests.AI.UI.ConnectionOp;

namespace QuickER.Tests.AI.UI;

internal static partial class ConnectionSettingsRows
{
    /// <summary>Codex 接続（プロバイダー・モデル・履歴）</summary>
    private static IEnumerable<ScenarioRow<ConnectionSettingsCase>> CodexRows() =>
        [
            Row(
                "codex-defaults-openai-provider-and-default-model",
                "Codex の既定はプロバイダー openai・モデルは既定のモデル（設定ファイルが無いまま読み込んでも同じ）",
                [(Codex, Defaults)],
                h =>
                {
                    // 既定のモデルの正本は AiModelCatalog.DefaultOpenAiModel（その XmlDoc）。組の config.toml は空なので機械に依らない
                    var vm = h.Create();

                    vm.CodexModelProvider.Should().Be("openai");
                    vm.CodexModel.Should().Be(AiModelCatalog.DefaultOpenAiModel);

                    vm.LoadSettings();

                    vm.CodexModelProvider.Should().Be("openai");
                    vm.CodexModel.Should().Be(AiModelCatalog.DefaultOpenAiModel);
                    File.Exists(h.SettingsPath)
                        .Should()
                        .BeFalse("読み込みは設定ファイルを作らない");
                }
            ),
            Row(
                "codex-candidates-openai-first-and-deduped",
                "Codex 候補は openai 先頭固定・config.toml のプロバイダーを dedup 追加",
                [(Codex, Load)],
                h =>
                {
                    // openai を重複して含む config（dedup で 1 つに畳まれる想定）
                    var config = new CodexConfigToml
                    {
                        ProviderNames = new List<string> { "ollama-launch", "openai" },
                    };
                    h.CodexConfig = () => config;
                    var vm = h.Create();

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
            ),
            Row(
                "codex-provider-switch-refreshes-candidates-from-history",
                "Codex プロバイダー切替で候補がそのプロバイダーの履歴へ更新される",
                [(Codex, Load), (Codex, Change)],
                h =>
                {
                    var config = new CodexConfigToml
                    {
                        ProviderNames = new List<string> { "ollama-launch" },
                    };

                    // 履歴を仕込んでおき、プロバイダー切替で読み込まれることを確認する
                    var seeded = h.Store().Load();
                    seeded.CodexModelHistory.Touch("ollama-launch", "gemma4:31b-cloud");
                    h.Store().Save(seeded);

                    h.CodexConfig = () => config;
                    var vm = h.Create();
                    vm.LoadSettings();

                    vm.CodexModelProvider = "ollama-launch";

                    // 非 openai の候補は履歴のみ（すべて × で削除可能）
                    vm.CodexModelCandidates.Should().ContainSingle();
                    vm.CodexModelCandidates[0].Name.Should().Be("gemma4:31b-cloud");
                    vm.CodexModelCandidates[0].IsRemovable.Should().BeTrue();
                }
            ),
            Row(
                "codex-provider-switch-without-history-has-empty-candidates",
                "履歴なしの Codex プロバイダー切替で候補は空",
                [(Codex, Load), (Codex, Change)],
                h =>
                {
                    var config = new CodexConfigToml
                    {
                        ProviderNames = new List<string> { "ollama-launch" },
                    };
                    h.CodexConfig = () => config;
                    var vm = h.Create();
                    vm.LoadSettings();

                    vm.CodexModelProvider = "ollama-launch";

                    vm.CodexModelCandidates.Should().BeEmpty();
                }
            ),
            Row(
                "codex-unknown-saved-provider-falls-back-to-openai",
                "候補に無い保存済み Codex プロバイダーは openai へフォールバック",
                [(Codex, Load)],
                h =>
                {
                    // config.toml から消えたプロバイダーが保存されている状況を作る
                    var seeded = h.Store().Load();
                    seeded.CodexAppServer = new CodexAppServerSettings
                    {
                        ModelProvider = "removed-provider",
                        Model = "m",
                    };
                    h.Store().Save(seeded);

                    var vm = h.Create();
                    vm.LoadSettings();

                    vm.CodexModelProvider.Should().Be("openai");
                }
            ),
            Row(
                "codex-saved-provider-casing-normalized-to-candidate",
                "保存済み Codex プロバイダーの表記ゆれは候補の表記へ正規化",
                [(Codex, Load)],
                h =>
                {
                    var seeded = h.Store().Load();
                    seeded.CodexAppServer = new CodexAppServerSettings
                    {
                        ModelProvider = "OLLAMA-LAUNCH",
                        Model = "m",
                    };
                    h.Store().Save(seeded);

                    var config = new CodexConfigToml
                    {
                        ProviderNames = new List<string> { "ollama-launch" },
                    };
                    h.CodexConfig = () => config;
                    var vm = h.Create();
                    vm.LoadSettings();

                    // SelectedItem バインドで候補と一致させるため、候補側の表記を採用する
                    vm.CodexModelProvider.Should().Be("ollama-launch");
                }
            ),
            Row(
                "codex-record-adds-history-and-persists",
                "Codex 記録で履歴候補が入り JSON へ往復する",
                [(Codex, ConnectionOp.Record)],
                h =>
                {
                    var vm = CreateCodexVm(h);

                    // 初期状態（履歴なし）は空
                    vm.CodexModelCandidates.Should().BeEmpty();

                    vm.CodexModel = "qwen3.6:35b";
                    vm.RecordSuccessfulModel();

                    // 候補は履歴のみ（× 付き）
                    vm.CodexModelCandidates.Select(c => c.Name).Should().Equal("qwen3.6:35b");
                    vm.CodexModelCandidates[0].IsRemovable.Should().BeTrue();

                    // 別インスタンスで読み戻して JSON 永続化を確認する
                    var reloaded = h.Store().Load().CodexModelHistory;
                    reloaded.ModelsFor("ollama-launch").Should().Equal("qwen3.6:35b");
                }
            ),
            Row(
                "codex-history-isolated-per-provider",
                "Codex 履歴はプロバイダ別に分離される",
                [(Codex, ConnectionOp.Record), (Codex, Change)],
                h =>
                {
                    // 2 プロバイダを持つ config
                    var config = new CodexConfigToml
                    {
                        ProviderNames = new List<string> { "ollama-launch", "other-provider" },
                    };
                    h.CodexConfig = () => config;
                    var vm = h.Create();
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
            ),
            Row(
                "codex-record-ignored-for-openai-provider",
                "Codex×openai では記録しない",
                [(Codex, ConnectionOp.Record)],
                h =>
                {
                    var vm = h.Create();
                    vm.LoadSettings();
                    vm.SelectedBackend = ErChatBackendKind.Codex;

                    // 既定プロバイダは openai・モデルはカタログ既定
                    vm.CodexModel = "gpt-5.4-mini";

                    vm.RecordSuccessfulModel();

                    vm.CodexModelCandidates.Should().OnlyContain(c => !c.IsRemovable);
                    File.Exists(h.SettingsPath).Should().BeFalse();
                }
            ),
            Row(
                "codex-record-ignored-on-api-key-backend",
                "ApiKey バックエンドでは Codex 履歴に記録しない",
                [(Codex, ConnectionOp.Record)],
                h =>
                {
                    var vm = CreateCodexVm(h);
                    vm.CodexModel = "qwen3.6:35b";

                    // バックエンドを API キー（OpenAI プロバイダ）へ戻す
                    vm.SelectedBackend = ErChatBackendKind.ApiKey;

                    vm.RecordSuccessfulModel();

                    File.Exists(h.SettingsPath).Should().BeFalse();
                }
            ),
            Row(
                "codex-remove-history-keeps-model",
                "Codex 削除コマンドで履歴のみ消え CodexModel は残る",
                [(Codex, ConnectionOp.Record), (Codex, Remove)],
                h =>
                {
                    var vm = CreateCodexVm(h);
                    vm.CodexModel = "model-x";
                    vm.RecordSuccessfulModel();
                    vm.CodexModel = "model-y";
                    vm.RecordSuccessfulModel();

                    // 候補: 履歴 MRU（y, x）・現在の選択は "model-y"
                    vm.CodexModelCandidates.Select(c => c.Name)
                        .Should()
                        .Equal("model-y", "model-x");

                    vm.RemoveCodexModelHistoryCommand.Execute("model-x");

                    // 履歴 "model-x" のみ消え、選択中モデルは保持される
                    vm.CodexModelCandidates.Select(c => c.Name).Should().Equal("model-y");
                    vm.CodexModel.Should().Be("model-y");

                    var reloaded = h.Store().Load().CodexModelHistory;
                    reloaded.ModelsFor("ollama-launch").Should().Equal("model-y");
                }
            ),
        ];
}
