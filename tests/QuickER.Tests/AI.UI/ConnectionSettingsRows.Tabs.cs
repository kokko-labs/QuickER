using AwesomeAssertions;
using QuickER.AI;
using QuickER.AI.UI;
using QuickER.Tests.TestSupport.Engines;
using static QuickER.Tests.AI.UI.ConnectionArea;
using static QuickER.Tests.AI.UI.ConnectionOp;

namespace QuickER.Tests.AI.UI;

internal static partial class ConnectionSettingsRows
{
    /// <summary>接続方式のタブの選択（BackendTabSelection）と接続の確認（BackendConnectionChecks）</summary>
    private static IEnumerable<ScenarioRow<ConnectionSettingsCase>> TabRows() =>
        [
            Row(
                "tabs-selection-is-pending-until-posted-commit-runs",
                "選択は暫定値として受け入れ、確定は投函の後に行う",
                [(Common, Change)],
                h =>
                {
                    var connection = h.Create();
                    var selection = new BackendTabSelection(connection, Accepting(connection));
                    var posted = new List<Action>();
                    selection.SetUiPost(posted.Add);
                    var values = RecordSelected(selection);

                    selection.Selected = ErChatBackendKind.Codex;

                    selection
                        .Selected.Should()
                        .Be(ErChatBackendKind.Codex, "タブは切替先へ動かしておく");
                    connection.SelectedBackend.Should().Be(ErChatBackendKind.ApiKey, "確定はまだ");
                    values.Should().Equal(ErChatBackendKind.Codex);
                    posted.Should().ContainSingle();

                    posted[0]();

                    connection.SelectedBackend.Should().Be(ErChatBackendKind.Codex);
                    selection.Selected.Should().Be(ErChatBackendKind.Codex);
                }
            ),
            Row(
                "tabs-rejected-selection-reverts-with-change-notification",
                "確定を断られたら元の接続方式を通知し直す",
                [(Common, Change)],
                h =>
                {
                    var connection = h.Create();
                    var attempts = new List<ErChatBackendKind>();
                    var selection = new BackendTabSelection(
                        connection,
                        backend =>
                        {
                            attempts.Add(backend);
                            return false;
                        }
                    );
                    var values = RecordSelected(selection);

                    selection.Selected = ErChatBackendKind.Copilot;

                    attempts.Should().Equal(ErChatBackendKind.Copilot);
                    connection.SelectedBackend.Should().Be(ErChatBackendKind.ApiKey);
                    selection.Selected.Should().Be(ErChatBackendKind.ApiKey);
                    values.Should().Equal(ErChatBackendKind.Copilot, ErChatBackendKind.ApiKey);
                }
            ),
            Row(
                "tabs-only-latest-pending-selection-is-committed",
                "確定を待つ間に選び直したら最後の選択だけを確定する",
                [(Common, Change)],
                h =>
                {
                    var connection = h.Create();
                    var attempts = new List<ErChatBackendKind>();
                    var selection = new BackendTabSelection(
                        connection,
                        backend =>
                        {
                            attempts.Add(backend);
                            connection.SelectedBackend = backend;
                            return true;
                        }
                    );
                    var posted = new List<Action>();
                    selection.SetUiPost(posted.Add);

                    selection.Selected = ErChatBackendKind.Codex;
                    selection.Selected = ErChatBackendKind.ClaudeCode;
                    posted.ForEach(commit => commit());

                    attempts.Should().Equal(ErChatBackendKind.ClaudeCode);
                    connection.SelectedBackend.Should().Be(ErChatBackendKind.ClaudeCode);
                    selection.Selected.Should().Be(ErChatBackendKind.ClaudeCode);
                }
            ),
            Row(
                "tabs-connection-change-is-relayed-to-selected",
                "確定した接続方式の変化をタブの表示へ中継する",
                [(Common, Change)],
                h =>
                {
                    var connection = h.Create();
                    var selection = new BackendTabSelection(connection, Accepting(connection));
                    var values = RecordSelected(selection);

                    connection.SelectedBackend = ErChatBackendKind.Copilot;

                    values.Should().Equal(ErChatBackendKind.Copilot);
                }
            ),
            Row(
                "tabs-selecting-current-backend-does-nothing",
                "同じ接続方式の選択は何もしない",
                [(Common, Change)],
                h =>
                {
                    var connection = h.Create();
                    var attempts = 0;
                    var selection = new BackendTabSelection(
                        connection,
                        _ =>
                        {
                            attempts++;
                            return true;
                        }
                    );
                    var values = RecordSelected(selection);

                    selection.Selected = ErChatBackendKind.ApiKey;

                    attempts.Should().Be(0);
                    values.Should().BeEmpty();
                }
            ),
            Row(
                "checks-start-only-the-selected-backends-check",
                "切り替えた接続方式の確認だけを起動し、API キーでは何もしない",
                [(Common, Change)],
                h =>
                {
                    var connection = h.Create();
                    var (_, started, failures) = CreateChecks(connection);

                    connection.SelectedBackend = ErChatBackendKind.Codex;
                    connection.SelectedBackend = ErChatBackendKind.ClaudeCode;
                    connection.SelectedBackend = ErChatBackendKind.Copilot;
                    connection.SelectedBackend = ErChatBackendKind.ApiKey;

                    started.Should().Equal("codex", "claude", "copilot");
                    failures.Should().BeEmpty();
                }
            ),
            Row(
                "checks-other-property-changes-do-not-start-checks",
                "接続方式以外の変更では確認を起動しない",
                [(Common, Change)],
                h =>
                {
                    var connection = h.Create();
                    var (_, started, _) = CreateChecks(connection);

                    connection.CodexModel = "gpt-test";
                    connection.ClaudeCodeModel = "sonnet";

                    started.Should().BeEmpty();
                }
            ),
            Row(
                "checks-failure-on-switch-is-reported-not-thrown",
                "切替で起動した確認の失敗は知らせ先へ渡り外へ漏れない",
                [(Common, Change)],
                h =>
                {
                    var connection = h.Create();
                    var failure = new InvalidOperationException("probe failure");
                    var (_, started, failures) = CreateChecks(connection, failure);

                    var act = () => connection.SelectedBackend = ErChatBackendKind.Copilot;

                    act.Should().NotThrow();
                    started.Should().Equal("copilot");
                    failures.Should().Equal(failure);
                }
            ),
            Row(
                "checks-run-async-failure-is-reported-not-thrown",
                "再確認の失敗も知らせ先へ渡り外へ漏れない",
                [(Common, Change)],
                h =>
                {
                    var connection = h.Create();
                    var (checks, _, failures) = CreateChecks(connection);
                    var failure = new InvalidOperationException("refresh failure");

                    checks.RunAsync(() => Task.FromException(failure)).GetAwaiter().GetResult();

                    failures.Should().Equal(failure);
                }
            ),
        ];
}
