using System.IO;
using AwesomeAssertions;
using QuickER.AI;
using QuickER.AI.Chat;
using QuickER.Tests.TestSupport.Engines;
using static QuickER.Tests.AI.Chat.ChatDialogOp;
using static QuickER.Tests.AI.Chat.ChatDialogState;
using ChatStrings = QuickER.AI.Chat.Resources.Strings;

namespace QuickER.Tests.AI.Chat;

internal static partial class ChatDialogRows
{
    /// <summary>SaveSettings・モデルの履歴の記録・保存の失敗</summary>
    private static IEnumerable<ScenarioRow<ChatDialogCase>> PersistRows() =>
        [
            Row(
                "persist-save-settings-restores-selected-backend",
                "SaveSettings が接続タブを保存し次回の InitialBackend に復元される",
                [(Idle, SwitchBackend), (Idle, Persist)],
                h =>
                {
                    var vm = h.Create();

                    // 保存が無い初回は API キータブが既定
                    vm.Connection.InitialBackend.Should().Be(ErChatBackendKind.ApiKey);

                    h.TryChangeBackend(vm, ErChatBackendKind.ClaudeCode).Should().BeTrue();
                    vm.SaveSettings();

                    var restored = h.Create();

                    restored.Connection.InitialBackend.Should().Be(ErChatBackendKind.ClaudeCode);
                }
            ),
            Row(
                "persist-codex-successful-turn-does-not-record-api-history",
                "Codex 成功ターンでは API モデル履歴を記録しない",
                [(TurnInProgress, Persist), (TurnInProgress, Turn)],
                async h =>
                {
                    var client = h.Codex;
                    var vm = h.Create();

                    // ローカル LLM＋モデルを設定してからバックエンドを Codex へ切り替える
                    // （ガードが無ければ記録されうる状態を作り、ガードが効くことを確かめる）
                    vm.Connection.ApiProvider = AiProvider.LocalLlm;
                    vm.Connection.ApiModel = "qwen3.6:35b";
                    vm.Connection.SelectedBackend = ErChatBackendKind.Codex;

                    // ターンを開始してから成功完了を発火させる（エンジンは実行中でないターンの完了を転送しない）
                    vm.UserInput = "やあ";
                    await vm.SendMessageCommand.ExecuteAsync(null);
                    client.RaiseTurnCompleted("completed");

                    // API 履歴には記録されない（ai-settings.json は作られない）
                    File.Exists(h.Store().SettingsPath).Should().BeFalse();
                }
            ),
            Row(
                "persist-codex-successful-turn-non-openai-provider-records-codex-history",
                "Codex×非 openai の成功ターンで使用モデルが履歴へ記録される",
                [(TurnInProgress, Persist), (TurnInProgress, Turn)],
                async h =>
                {
                    var client = h.Codex;
                    var vm = h.Create();

                    // 注意: Chat VM の Connection は実 config.toml を読む（seam 無し）ため、
                    // 実環境のプロバイダ設定に依存しないよう、プロバイダ・モデルともテスト固有の名前を使う
                    vm.Connection.SelectedBackend = ErChatBackendKind.Codex;
                    vm.Connection.CodexModelProvider = "mru-e2e-provider";
                    vm.Connection.CodexModel = "mru-e2e-model";

                    // ターンを開始してから成功完了を発火させる（エンジンは実行中でないターンの完了を転送しない）
                    vm.UserInput = "やあ";
                    await vm.SendMessageCommand.ExecuteAsync(null);
                    client.RaiseTurnCompleted("completed");

                    // プロバイダ別履歴へ記録され、候補にも × 付きで現れる
                    var reloaded = h.Store().Load().CodexModelHistory;
                    reloaded.ModelsFor("mru-e2e-provider").Should().Equal("mru-e2e-model");
                    vm.Connection.CodexModelCandidates.Should()
                        .Contain(c => c.Name == "mru-e2e-model" && c.IsRemovable);
                }
            ),
            Row(
                "persist-copilot-successful-turn-records-copilot-history",
                "Copilot の成功ターンで使用モデルが履歴へ記録される",
                [(TurnInProgress, Persist), (TurnInProgress, Turn)],
                h =>
                {
                    var copilot = h.Copilot;
                    var vm = h.Create();

                    vm.Connection.SelectedBackend = ErChatBackendKind.Copilot;
                    // 実行時列挙に無い（手入力の）モデルを使い、履歴側の候補として現れることを確かめる
                    vm.Connection.CopilotModel = "mru-copilot-model";

                    vm.StartConversationCommand.Execute(null);
                    vm.UserInput = "本のテーブルを作って";
                    vm.SendMessageCommand.Execute(null);
                    copilot.Sends.Should().ContainSingle();

                    // アイドル復帰＝ターン成功
                    copilot.RaiseIdle();

                    var reloaded = h.Store().Load().CopilotModelHistory;
                    reloaded
                        .ModelsFor(CopilotSettings.HistoryProviderKey)
                        .Should()
                        .Equal("mru-copilot-model");
                    vm.Connection.CopilotModelCandidates.Should()
                        .Contain(c => c.Name == "mru-copilot-model" && c.IsRemovable);
                }
            ),
            Row(
                "persist-save-settings-restores-copilot-backend",
                "Copilot タブの選択が保存・復元される",
                [(Idle, SwitchBackend), (Idle, Persist)],
                h =>
                {
                    var vm = h.Create();

                    h.TryChangeBackend(vm, ErChatBackendKind.Copilot).Should().BeTrue();
                    vm.SaveSettings();

                    h.Copilot = new FakeCopilotRuntimeClient();
                    var restored = h.Create();

                    restored.Connection.InitialBackend.Should().Be(ErChatBackendKind.Copilot);
                }
            ),
            Row(
                "persist-model-history-save-failure-keeps-successful-turn-successful",
                "モデル履歴の保存に失敗しても成功したターンは成功のまま",
                [(TurnInProgress, Persist), (TurnInProgress, Turn)],
                h =>
                {
                    // 保存先フォルダと同じパスにファイルを置く＝設定の書き込みが必ず失敗する
                    h.BlockSettingsFolder();
                    var copilot = h.Copilot;
                    var vm = h.Create();

                    // 前提の確認: この構成では設定の保存が実際に落ちる
                    var save = vm.SaveSettings;
                    save.Should().Throw<Exception>();

                    vm.Connection.SelectedBackend = ErChatBackendKind.Copilot;
                    vm.Connection.CopilotModel = "mru-copilot-model";
                    ChatDialogHarness.StartCopilotTurn(vm);

                    var raise = () => copilot.RaiseIdle();
                    raise.Should().NotThrow();

                    vm.IsTurnInProgress.Should().BeFalse();
                    vm.StatusMessage.Should()
                        .StartWith(
                            ChatDialogHarness.FormatPrefix(
                                ChatStrings.Chat_ModelHistorySaveFailedFormat
                            )
                        );
                    vm.Messages.Should()
                        .NotContain(m =>
                            m.Role == ErChatMessageRole.System
                            && m.Content.StartsWith(
                                ChatDialogHarness.FormatPrefix(ChatStrings.Chat_ErrorSystemFormat),
                                StringComparison.Ordinal
                            )
                        );
                }
            ),
        ];
}
