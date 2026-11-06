using System.IO;
using AwesomeAssertions;
using QuickER.AI;
using QuickER.Tests.TestSupport.Engines;
using static QuickER.Tests.AI.UI.ConnectionArea;
using static QuickER.Tests.AI.UI.ConnectionOp;

namespace QuickER.Tests.AI.UI;

internal static partial class ConnectionSettingsRows
{
    /// <summary>Copilot 接続（モデル・列挙・履歴）</summary>
    private static IEnumerable<ScenarioRow<ConnectionSettingsCase>> CopilotRows() =>
        [
            Row(
                "copilot-defaults-empty-model-and-pending-status",
                "Copilot の既定はモデル空・状態は Pending",
                [(Copilot, Defaults)],
                h =>
                {
                    var vm = h.Create();

                    // 空＝Copilot CLI の既定モデルに任せる（Claude Code 接続と同じ思想）
                    vm.CopilotModel.Should().BeEmpty();
                    vm.CopilotStatusLevel.Should().Be(ConnectionHealth.Pending);
                    vm.CopilotAvailableModels.Should().BeEmpty();
                    vm.CopilotModelCandidates.Should().BeEmpty();
                }
            ),
            Row(
                "copilot-available-models-fill-candidates-as-non-removable",
                "Copilot 候補は実行時列挙が上段（削除不可）",
                [(Copilot, Change)],
                h =>
                {
                    var vm = h.Create();

                    vm.CopilotAvailableModels = new List<string> { "gpt-5", "claude-sonnet-4.5" };

                    vm.CopilotModelCandidates.Select(c => c.Name)
                        .Should()
                        .Equal("gpt-5", "claude-sonnet-4.5");
                    vm.CopilotModelCandidates.Should().OnlyContain(c => !c.IsRemovable);
                }
            ),
            Row(
                "copilot-history-below-available-models-without-duplicates",
                "Copilot 候補は実行時列挙＋履歴の 2 層で重複しない",
                [(Copilot, ConnectionOp.Record)],
                h =>
                {
                    var vm = h.Create();
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
                    var reloaded = h.Store().Load().CopilotModelHistory;
                    reloaded
                        .ModelsFor(CopilotSettings.HistoryProviderKey)
                        .Should()
                        .Equal("GPT-5", "custom-copilot-model");
                }
            ),
            Row(
                "copilot-record-guards-backend-and-blank-model",
                "Copilot 以外・空モデルでは Copilot 履歴に記録しない",
                [(Copilot, ConnectionOp.Record)],
                h =>
                {
                    var vm = h.Create();
                    vm.LoadSettings();

                    // ①Copilot モデルが入っていても、バックエンドが Claude Code なら記録しない
                    vm.CopilotModel = "gpt-5";
                    vm.SelectedBackend = ErChatBackendKind.ClaudeCode;
                    vm.RecordSuccessfulModel();
                    File.Exists(h.SettingsPath).Should().BeFalse();

                    // ②Copilot でもモデルが空（＝CLI 既定に任せる指定）なら記録するモデル名が無い
                    vm.SelectedBackend = ErChatBackendKind.Copilot;
                    vm.CopilotModel = "   ";
                    vm.RecordSuccessfulModel();

                    vm.CopilotModelCandidates.Should().BeEmpty();
                    File.Exists(h.SettingsPath).Should().BeFalse();
                }
            ),
            Row(
                "copilot-remove-history-keeps-available-models-and-model",
                "Copilot 削除コマンドで履歴のみ消え列挙と CopilotModel は残る",
                [(Copilot, ConnectionOp.Record), (Copilot, Remove)],
                h =>
                {
                    var vm = h.Create();
                    vm.LoadSettings();
                    vm.SelectedBackend = ErChatBackendKind.Copilot;
                    vm.CopilotAvailableModels = new List<string> { "gpt-5" };
                    vm.CopilotModel = "custom-copilot-model";
                    vm.RecordSuccessfulModel();

                    vm.RemoveCopilotModelHistoryCommand.Execute("custom-copilot-model");

                    vm.CopilotModelCandidates.Select(c => c.Name).Should().Equal("gpt-5");
                    vm.CopilotModel.Should().Be("custom-copilot-model");

                    var reloaded = h.Store().Load().CopilotModelHistory;
                    reloaded.ModelsFor(CopilotSettings.HistoryProviderKey).Should().BeEmpty();
                }
            ),
            Row(
                "copilot-model-tab-and-history-round-trip",
                "Copilot のモデル・タブ選択・履歴が往復する",
                [(Copilot, ConnectionOp.Record), (Copilot, Save), (Copilot, Load)],
                h =>
                {
                    var vm = h.Create();
                    vm.LoadSettings();
                    vm.SelectedBackend = ErChatBackendKind.Copilot;
                    vm.CopilotModel = "claude-sonnet-4.5";
                    vm.RecordSuccessfulModel();
                    h.SaveSettings(vm);

                    var restored = h.Create();
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
            ),
            Row(
                "copilot-save-does-not-clobber-other-sections",
                "Copilot 保存は他セクションを消さない",
                [(Copilot, Save)],
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
                    vm.CodexModel = "gemma4:31b-cloud";
                    vm.ClaudeCodeModel = "opus";
                    vm.CopilotModel = "gpt-5";
                    vm.SelectedBackend = ErChatBackendKind.Copilot;
                    h.SaveSettings(vm);

                    var saved = h.Store().Load();
                    saved.Copilot.Model.Should().Be("gpt-5");
                    saved.CodexAppServer.Model.Should().Be("gemma4:31b-cloud");
                    saved.ClaudeCode.Model.Should().Be("opus");
                    saved.ChatUi.LastBackend.Should().Be(nameof(ErChatBackendKind.Copilot));
                }
            ),
            Row(
                "copilot-same-available-models-do-not-rebuild-candidates",
                "Copilot のモデル一覧が同じ内容なら候補を作り直さない",
                [(Copilot, Change)],
                h =>
                {
                    var vm = h.Create();
                    vm.CopilotAvailableModels = new List<string> { "gpt-a", "gpt-b" };

                    var rebuilds = 0;
                    vm.CopilotModelCandidates.CollectionChanged += (_, _) => rebuilds++;

                    vm.CopilotAvailableModels = new List<string> { "gpt-a", "gpt-b" };
                    rebuilds.Should().Be(0);

                    vm.CopilotAvailableModels = new List<string> { "gpt-a", "gpt-c" };
                    rebuilds.Should().BeGreaterThan(0);
                    vm.CopilotModelCandidates.Select(c => c.Name).Should().Contain("gpt-c");
                }
            ),
        ];
}
