using AwesomeAssertions;
using QuickER.AI;
using QuickER.Tests.TestSupport.Engines;
using static QuickER.Tests.AI.ChatEngines.ChatEngineCapability;

namespace QuickER.Tests.AI.ChatEngines;

internal static partial class ChatEngineContractRows
{
    /// <summary>接続の準備（CLI の検出・常駐プロセスの起動・再確認・重ねた接続・やり直し）</summary>
    private static IEnumerable<ScenarioRow<ChatContractScenario>> Ready() =>
        [
            Row(
                "ready-cli-missing-is-not-ready-with-install-guidance",
                "CLI が見つからなければ起動せず、送信できない状態でインストールを案内する",
                ChatToolHostKind.None,
                async c =>
                {
                    c.Rig.SetCliAvailable(false);

                    await c.Rig.ConnectAsync(c.CancellationToken);

                    var readiness = c.Rig.Readiness;
                    readiness.IsReady.Should().BeFalse();
                    readiness.Summary.Should().Be(c.Adapter.ExpectedCliMissingSummary);
                    readiness.Guidance.Should().Be(c.Adapter.ExpectedInstallGuidance);
                    ExpectNeedsAction(c, readiness);

                    if (c.Adapter.Has(ExposesCliMissing))
                    {
                        readiness.CliMissing.Should().BeTrue();
                    }

                    if (c.Adapter.Has(LaunchesRuntime))
                    {
                        c.Rig.RuntimeStarts.Should().Be(0);
                        readiness.RuntimeStarted.Should().BeFalse();
                    }
                },
                CliLifecycle
            ),
            Row(
                "ready-runtime-start-failure-keeps-reason-in-guidance",
                "常駐プロセスの起動に失敗したら、送信できない状態で理由を案内に残す",
                ChatToolHostKind.None,
                async c =>
                {
                    c.Rig.FailRuntimeStart(new InvalidOperationException("起動できません"));

                    await c.Rig.ConnectAsync(c.CancellationToken);

                    var readiness = c.Rig.Readiness;
                    readiness.IsReady.Should().BeFalse();
                    readiness.RuntimeStarted.Should().BeFalse();
                    readiness.Guidance.Should().Contain("起動できません");
                    readiness.CliMissing.Should().BeFalse();
                    ExpectNeedsAction(c, readiness);
                },
                LaunchesRuntime
            ),
            Row(
                "ready-recheck-after-cli-becomes-available-connects",
                "CLI が見つからなかった後に見つかるようになれば、再確認で接続する",
                ChatToolHostKind.None,
                async c =>
                {
                    c.Rig.SetCliAvailable(false);
                    await c.Rig.ConnectAsync(c.CancellationToken);
                    c.Rig.Readiness.CliMissing.Should().BeTrue();

                    c.Rig.SetCliAvailable(true);
                    await c.Rig.RecheckAsync(c.CancellationToken);

                    var readiness = c.Rig.Readiness;
                    readiness.CliMissing.Should().BeFalse();
                    readiness.RuntimeStarted.Should().BeTrue();
                    c.Rig.RuntimeStarts.Should().Be(1);

                    if (readiness.StatusLevel is not null)
                    {
                        readiness.StatusLevel.Should().Be(ConnectionHealth.Ready);
                    }
                },
                LaunchesRuntime
            ),
            Row(
                "ready-concurrent-connects-start-runtime-once",
                "接続の途中に重ねて接続（再確認）しても、常駐プロセスの起動は 1 回だけ",
                ChatToolHostKind.None,
                async c =>
                {
                    var gate = new TaskCompletionSource();
                    c.Rig.GateRuntimeStart(gate.Task);

                    var first = c.Rig.ConnectAsync(c.CancellationToken);
                    var second = c.Rig.RecheckAsync(c.CancellationToken);
                    c.Rig.RuntimeStarts.Should()
                        .BeLessThanOrEqualTo(1, "保留中に 2 本目の起動が始まらない");

                    gate.SetResult();
                    await first.WaitAsync(TimeSpan.FromSeconds(10), c.CancellationToken);
                    await second.WaitAsync(TimeSpan.FromSeconds(10), c.CancellationToken);

                    c.Rig.RuntimeStarts.Should().Be(1);
                    c.Rig.Readiness.RuntimeStarted.Should().BeTrue();
                },
                LaunchesRuntime
            ),
            Row(
                "ready-connect-after-failed-start-retries",
                "常駐プロセスの起動が失敗した後の接続は、失敗を返し続けずに起動をやり直す",
                ChatToolHostKind.None,
                async c =>
                {
                    c.Rig.FailRuntimeStart(new InvalidOperationException("first start fails"));
                    await c.Rig.ConnectAsync(c.CancellationToken);
                    c.Rig.Readiness.RuntimeStarted.Should().BeFalse();

                    c.Rig.FailRuntimeStart(null);
                    await c.Rig.ConnectAsync(c.CancellationToken);

                    c.Rig.Readiness.RuntimeStarted.Should().BeTrue();
                },
                LaunchesRuntime
            ),
        ];

    /// <summary>状態の色を持つエンジンでは、利用者の対応が要る色（赤）であることを表明する</summary>
    private static void ExpectNeedsAction(ChatContractContext c, ChatReadiness readiness)
    {
        if (readiness.StatusLevel is not null)
        {
            readiness.StatusLevel.Should().Be(ConnectionHealth.NeedsAction);
        }
    }
}
