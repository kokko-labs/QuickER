using AwesomeAssertions;
using QuickER.AI;
using QuickER.AI.Chat;
using QuickER.AI.UI;
using QuickER.Tests.TestSupport.Engines;
using static QuickER.Tests.AI.Chat.ChatDialogOp;
using static QuickER.Tests.AI.Chat.ChatDialogState;
using ChatStrings = QuickER.AI.Chat.Resources.Strings;

namespace QuickER.Tests.AI.Chat;

internal static partial class ChatDialogRows
{
    /// <summary>接続の確認（Codex・Copilot・Claude Code の検出・ログイン・再確認・接続断）</summary>
    private static IEnumerable<ScenarioRow<ChatDialogCase>> ConnectRows() =>
        [
            Row(
                "connect-codex-cli-missing-shows-install-guidance",
                "codex 未検出なら赤・インストール案内・ログインパネル非表示",
                [(Idle, Connect)],
                h =>
                {
                    var client = h.Codex;
                    var vm = h.Create();
                    client.IsCliAvailable = false;

                    vm.Connection.SelectedBackend = ErChatBackendKind.Codex;

                    client.StartCount.Should().Be(0, "未検出ならプロセス起動を試みない");
                    vm.CodexStatusLevel.Should().Be(ConnectionHealth.NeedsAction);
                    vm.CodexAccountSummary.Should()
                        .Be(QuickER.AI.Resources.Strings.Codex_Status_NotFound);
                    vm.CodexGuidance.Should()
                        .Be(QuickER.AI.Resources.Strings.Codex_Guidance_Install);
                    vm.ShowCodexGuidance.Should().BeTrue();
                    vm.ShowCodexLoginPanel.Should().BeFalse("未検出ではログインしても解決しない");
                    vm.CanStartConversation.Should().BeFalse();
                }
            ),
            Row(
                "connect-codex-detected-not-logged-in-shows-login-panel",
                "codex 検出済み・未ログインならログインパネルを出す",
                [(Idle, Connect)],
                h =>
                {
                    var client = h.Codex;
                    var vm = h.Create();
                    client.NextAccountInfo = new CodexAccountInfo { RequiresOpenAiAuth = true };

                    vm.Connection.SelectedBackend = ErChatBackendKind.Codex;

                    client.StartCount.Should().Be(1);
                    vm.ShowCodexLoginPanel.Should().BeTrue();
                    vm.CodexGuidance.Should().BeEmpty();
                    vm.ShowCodexGuidance.Should().BeFalse();
                    vm.CodexStatusLevel.Should().Be(ConnectionHealth.NeedsAction);
                }
            ),
            Row(
                "connect-codex-logged-in-notifies-ready-status-level",
                "codex ログイン済みなら接続解決後にドットが緑として通知される",
                [(Idle, Connect)],
                h =>
                {
                    var client = h.Codex;
                    var vm = h.Create();
                    client.NextAccountInfo = new CodexAccountInfo
                    {
                        RequiresOpenAiAuth = true,
                        AuthMode = CodexAuthMode.ChatGpt,
                        Email = "user@example.com",
                        PlanType = "plus",
                    };

                    // CodexStatusLevel が通知された時点の値を記録する（バインディングが観測する値の追跡）
                    var observed = new List<ConnectionHealth>();
                    vm.PropertyChanged += (_, e) =>
                    {
                        if (e.PropertyName == nameof(vm.CodexStatusLevel))
                        {
                            observed.Add(vm.CodexStatusLevel);
                        }
                    };

                    vm.Connection.SelectedBackend = ErChatBackendKind.Codex;

                    vm.CodexStatusLevel.Should().Be(ConnectionHealth.Ready);
                    observed.Should().NotBeEmpty();
                    observed[^1]
                        .Should()
                        .Be(
                            ConnectionHealth.Ready,
                            "最後に通知された時点の値が緑でないと、バインドされたドットは灰のまま残る"
                        );
                    vm.CodexAccountSummary.Should()
                        .Be(
                            string.Format(
                                QuickER.AI.Resources.Strings.Codex_Account_EmailLoggedIn,
                                "user@example.com / plus"
                            )
                        );
                    vm.CodexGuidance.Should()
                        .Be(QuickER.AI.Resources.Strings.Codex_Guidance_LoggedIn);
                    vm.ShowCodexGuidance.Should().BeTrue();
                }
            ),
            Row(
                "connect-copilot-switch-connects-and-applies-status-and-models",
                "Copilot へ切替で接続し状態とモデル候補が反映される",
                [(Idle, Connect)],
                h =>
                {
                    var copilot = h.Copilot;
                    var vm = h.Create();

                    vm.Connection.SelectedBackend = ErChatBackendKind.Copilot;

                    vm.Connection.IsCopilotBackend.Should().BeTrue();
                    copilot.IsStarted.Should().BeTrue();

                    // ログイン済み（フェイクの既定）なら緑・利用案内
                    vm.Connection.CopilotStatusLevel.Should().Be(ConnectionHealth.Ready);
                    vm.Connection.CopilotStatusSummary.Should()
                        .Be(
                            string.Format(
                                QuickER.AI.Resources.Strings.Copilot_Status_LoggedInAs,
                                copilot.AuthInfo.Login
                            )
                        );
                    vm.Connection.CopilotGuidance.Should()
                        .Be(QuickER.AI.Resources.Strings.Copilot_Guidance_LoggedIn);

                    // 静的カタログを持たないため、候補は実行時列挙（削除不可）で埋まる
                    vm.Connection.CopilotModelCandidates.Select(c => c.Name)
                        .Should()
                        .Equal(copilot.Models);
                    vm.Connection.CopilotModelCandidates.Should().OnlyContain(c => !c.IsRemovable);

                    vm.CanStartConversation.Should().BeTrue();
                }
            ),
            Row(
                "connect-copilot-cli-missing-shows-install-guidance",
                "copilot 未検出なら赤・インストール案内・起動を試みない",
                [(Idle, Connect)],
                h =>
                {
                    var copilot = h.Copilot;
                    var vm = h.Create();
                    copilot.Available = false;

                    vm.Connection.SelectedBackend = ErChatBackendKind.Copilot;

                    copilot.IsStarted.Should().BeFalse("未検出ならプロセス起動を試みない");
                    vm.Connection.CopilotStatusLevel.Should().Be(ConnectionHealth.NeedsAction);
                    vm.Connection.CopilotStatusSummary.Should()
                        .Be(QuickER.AI.Resources.Strings.Copilot_Status_NotFound);
                    vm.Connection.CopilotGuidance.Should()
                        .Be(QuickER.AI.Resources.Strings.Copilot_Guidance_Install);
                    vm.CanStartConversation.Should().BeFalse();
                }
            ),
            Row(
                "connect-copilot-connection-lost-can-send-again-and-reconnects",
                "Copilot の接続が切れて失敗したターンの後も送信でき、次の送信で接続し直す",
                [(Conversation, Connect), (Conversation, Turn)],
                async h =>
                {
                    var copilot = h.Copilot;
                    var vm = h.Create();

                    vm.Connection.SelectedBackend = ErChatBackendKind.Copilot;
                    vm.StartConversationCommand.Execute(null);
                    copilot.AutoIdleAfterSend = true;
                    vm.UserInput = "1 回目";
                    await vm.SendMessageCommand.ExecuteAsync(null);

                    copilot.FailNextSendWithConnectionLost();
                    vm.UserInput = "2 回目";
                    await vm.SendMessageCommand.ExecuteAsync(null);

                    vm.IsTurnInProgress.Should().BeFalse();
                    copilot
                        .IsStarted.Should()
                        .BeFalse("接続が切れた失敗でクライアントは接続を捨てる");
                    vm.UserInput = "3 回目";
                    vm.CanSendMessage.Should()
                        .BeTrue("次の送信で接続し直すので、送信ボタンは押せる");

                    await vm.SendMessageCommand.ExecuteAsync(null);

                    copilot.StartCallCount.Should().Be(2);
                    copilot.StartSessionCallCount.Should().Be(2);
                    copilot.Sends.Select(send => send.Prompt).Should().Equal("1 回目", "3 回目");
                    vm.IsTurnInProgress.Should().BeFalse();
                }
            ),
            Row(
                "connect-copilot-connection-lost-mid-stream-watch-ends-turn",
                "Copilot の応答の途中で接続が切れると、見張りがターンを終えて送信できる",
                [(TurnInProgress, Connect), (TurnInProgress, Turn)],
                async h =>
                {
                    var copilot = h.Copilot;
                    var vm = h.Create();

                    vm.Connection.SelectedBackend = ErChatBackendKind.Copilot;
                    vm.StartConversationCommand.Execute(null);
                    vm.UserInput = "長い応答を";
                    await vm.SendMessageCommand.ExecuteAsync(null);
                    copilot.RaiseDelta("途中まで");
                    vm.IsTurnInProgress.Should().BeTrue();

                    copilot.FailPingsWithConnectionLost();
                    await ChatDialogHarness.WaitTurnEndedAsync(vm);

                    copilot
                        .IsStarted.Should()
                        .BeFalse("接続が切れた失敗でクライアントは接続を捨てる");
                    vm.UserInput = "もう一度";
                    vm.CanSendMessage.Should().BeTrue();

                    copilot.AutoIdleAfterSend = true;
                    await vm.SendMessageCommand.ExecuteAsync(null);

                    copilot.StartCallCount.Should().Be(2);
                    copilot
                        .Sends.Select(send => send.Prompt)
                        .Should()
                        .Equal("長い応答を", "もう一度");
                    vm.IsTurnInProgress.Should().BeFalse();
                }
            ),
            Row(
                "connect-copilot-connection-lost-interrupt-ends-turn",
                "Copilot の接続が切れた状態の「中断」でターンが終わり送信できる",
                [(TurnInProgress, Connect), (TurnInProgress, Turn)],
                async h =>
                {
                    var copilot = h.Copilot;
                    var vm = h.Create();

                    vm.Connection.SelectedBackend = ErChatBackendKind.Copilot;
                    vm.StartConversationCommand.Execute(null);
                    vm.UserInput = "長い応答を";
                    await vm.SendMessageCommand.ExecuteAsync(null);
                    copilot.RaiseDelta("途中まで");

                    copilot.FailNextAbortWithConnectionLost();
                    await vm.InterruptCommand.ExecuteAsync(null);

                    vm.IsTurnInProgress.Should().BeFalse();
                    vm.UserInput = "もう一度";
                    vm.CanSendMessage.Should().BeTrue();
                }
            ),
            Row(
                "connect-codex-login-https-url-opens-browser",
                "Codex 認証: https の認証 URL はブラウザで開く",
                [(Idle, Connect)],
                async h =>
                {
                    var client = h.Codex;
                    var vm = h.Create();
                    client.ChatGptAuthUrl = "https://auth.example/login?code=abc";
                    var opened = new List<string>();
                    vm.OpenBrowser = opened.Add;

                    await vm.CodexStartChatGptLoginCommand.ExecuteAsync(null);

                    opened.Should().Equal("https://auth.example/login?code=abc");
                }
            ),
            .. new[]
            {
                ("file-url", "file:///C:/Windows/System32/calc.exe"),
                ("javascript-url", "javascript:alert(1)"),
                ("not-a-url", "not a url at all"),
            }.Select(entry =>
                Row(
                    $"connect-codex-login-non-http-url-does-not-open-browser-{entry.Item1}",
                    "Codex 認証: http/https 以外の認証 URL は開かない",
                    [(Idle, Connect)],
                    async h =>
                    {
                        var url = entry.Item2;
                        var client = h.Codex;
                        var vm = h.Create();
                        client.ChatGptAuthUrl = url;
                        var opened = new List<string>();
                        vm.OpenBrowser = opened.Add;

                        await vm.CodexStartChatGptLoginCommand.ExecuteAsync(null);

                        opened.Should().BeEmpty("http/https 以外の URL はシェル起動しない");
                        vm.StatusMessage.Should().Contain(url, "開けない URL をユーザーへ提示する");
                    }
                )
            ),
            Row(
                "connect-claude-code-switch-initialize-failure-reports-status",
                "タブ切替の初期化の失敗はステータスへ出る",
                [(Idle, Connect), (Idle, SwitchBackend)],
                h =>
                {
                    h.ClaudeCode = new ThrowingClaudeCodeClient();
                    var vm = h.Create();

                    h.TryChangeBackend(vm, ErChatBackendKind.ClaudeCode).Should().BeTrue();

                    vm.StatusMessage.Should()
                        .Be(
                            string.Format(
                                ChatStrings.Chat_ConnectionCheckFailedFormat,
                                "claude-detect-failed"
                            )
                        );
                }
            ),
            Row(
                "connect-claude-code-refresh-failure-reports-status",
                "Claude Code の再確認の失敗はステータスへ出る",
                [(Idle, Connect)],
                async h =>
                {
                    h.ClaudeCode = new ThrowingClaudeCodeClient();
                    var vm = h.Create();

                    var refresh = () => vm.ClaudeCodeRefreshCommand.ExecuteAsync(null);

                    await refresh.Should().NotThrowAsync();
                    vm.StatusMessage.Should()
                        .Be(
                            string.Format(
                                ChatStrings.Chat_ConnectionCheckFailedFormat,
                                "claude-detect-failed"
                            )
                        );
                }
            ),
        ];
}
