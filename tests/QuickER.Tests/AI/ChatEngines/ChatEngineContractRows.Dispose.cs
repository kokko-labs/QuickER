using System.IO;
using AwesomeAssertions;
using QuickER.Tests.TestSupport.Engines;
using static QuickER.Tests.AI.ChatEngines.ChatEngineCapability;

namespace QuickER.Tests.AI.ChatEngines;

internal static partial class ChatEngineContractRows
{
    /// <summary>破棄（クライアントを破棄し、遅れて届いた通知を流さず、作業フォルダを残さない）</summary>
    private static IEnumerable<ScenarioRow<ChatContractScenario>> Disposal() =>
        [
            Row(
                "dispose-releases-backend",
                "破棄でクライアントも破棄する（子プロセスを残さない）",
                ChatToolHostKind.None,
                async c =>
                {
                    await c.Rig.StartConversationAsync(c.CancellationToken);
                    await c.Rig.DisposeAsync();

                    c.Rig.BackendDisposed.Should().BeTrue();
                },
                OwnsBackend
            ),
            Row(
                "dispose-stops-completion-notifications",
                "破棄の後に遅れて届いたターンの完了の通知は流さない",
                ChatToolHostKind.None,
                async c =>
                {
                    await c.Rig.StartConversationAsync(c.CancellationToken);
                    await c.Rig.DisposeAsync();
                    c.Rig.RaiseStrayCompletion();

                    c.Rig.Events.Completions.Should().BeEmpty();
                },
                AsyncCompletion
            ),
            Row(
                "dispose-deletes-working-directory",
                "破棄でエンジンが作った作業フォルダを消す",
                ChatToolHostKind.None,
                async c =>
                {
                    await c.Rig.PlayTurnAsync(
                        "やあ",
                        ChatTurnScript.Of(new ChatTurnEnding.Success()),
                        c.CancellationToken
                    );
                    var directory = c.Rig.WorkingDirectory;
                    directory.Should().NotBeNullOrEmpty();
                    Directory.Exists(directory).Should().BeTrue();

                    await c.Rig.DisposeAsync();

                    Directory.Exists(directory).Should().BeFalse();
                },
                CreatesWorkingDirectory
            ),
        ];
}
