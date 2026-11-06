using AwesomeAssertions;
using QuickER.Services;
using QuickER.Tests.TestDoubles;
using QuickER.Tests.TestSupport.Engines;
using static QuickER.Tests.Gui.Services.Lifecycle.LifecycleEntrance;
using static QuickER.Tests.Gui.Services.Lifecycle.LifecycleFault;

namespace QuickER.Tests.Gui.Services.Lifecycle;

/// <summary>
/// アプリの一生の表：正常終了の最後の処理（QuickER.Gui の <c>PostExitActions</c>＝更新の適用と再起動）
/// </summary>
internal static partial class LifecycleRows
{
    private static IEnumerable<ScenarioRow<LifecycleCase>> PostExitRows()
    {
        yield return Row(
            "post-exit-relaunch-only-relaunches-itself-none",
            "PostExitActions: 再起動だけなら自分で起動する",
            PostExitRun,
            None,
            o =>
            {
                var actions = new PostExitActions();
                var relaunched = 0;
                actions.RelaunchWith(() => relaunched++);
                actions.RequestRelaunch();

                actions.Run(o.Reporter);

                o.Bag["relaunched"] = relaunched;
            },
            o => o.Get<int>("relaunched").Should().Be(1)
        );

        foreach (var restart in new[] { true, false })
        {
            yield return Row(
                $"post-exit-update-only-applies-with-registered-restart-{(restart ? "true" : "false")}-none",
                "PostExitActions: 更新の適用だけなら登録した再起動の有無で適用する",
                PostExitRun,
                None,
                o =>
                {
                    var actions = new PostExitActions();
                    var applied = new List<bool>();
                    actions.ScheduleUpdateApply(applied.Add, restart);

                    actions.Run(o.Reporter);

                    o.Bag["applied"] = applied;
                },
                o => o.Get<List<bool>>("applied").Should().Equal(restart)
            );
        }

        // 更新プログラムは適用の前にインストール先で動いている QuickER を止めるため、自分で起動するとそのインスタンスが
        // 止められて再起動が失われる。どちらが先に要求されても同じ
        foreach (var updateFirst in new[] { true, false })
        {
            yield return Row(
                $"post-exit-update-and-relaunch-lets-updater-restart-{(updateFirst ? "update-first" : "relaunch-first")}-update-and-relaunch",
                "PostExitActions: 更新と再起動が両方なら更新プログラムに再起動を任せる",
                PostExitRun,
                UpdateAndRelaunch,
                o =>
                {
                    var actions = new PostExitActions();
                    var applied = new List<bool>();
                    var relaunched = 0;
                    actions.RelaunchWith(() => relaunched++);

                    if (updateFirst)
                    {
                        actions.ScheduleUpdateApply(applied.Add, restart: false);
                        actions.RequestRelaunch();
                    }
                    else
                    {
                        actions.RequestRelaunch();
                        actions.ScheduleUpdateApply(applied.Add, restart: false);
                    }

                    actions.Run(o.Reporter);

                    o.Bag["applied"] = applied;
                    o.Bag["relaunched"] = relaunched;
                },
                o =>
                {
                    o.Get<List<bool>>("applied").Should().Equal(true);
                    o.Get<int>("relaunched")
                        .Should()
                        .Be(0, "自分で起動したインスタンスは適用の前に止められる");
                }
            );
        }

        yield return Row(
            "post-exit-run-clears-scheduled-actions-reentry",
            "PostExitActions: 実行した登録は消える",
            PostExitRun,
            Reentry,
            o =>
            {
                var actions = new PostExitActions();
                var applied = 0;
                actions.ScheduleUpdateApply(_ => applied++, restart: true);

                actions.Run(new RecordingShutdownFailureReporter());
                actions.Run(new RecordingShutdownFailureReporter());

                o.Bag["applied"] = applied;
            },
            o => o.Get<int>("applied").Should().Be(1)
        );

        yield return Row(
            "post-exit-apply-failure-is-reported-not-thrown-apply-fails",
            "PostExitActions: 適用の失敗は投げずに記録する",
            PostExitRun,
            ApplyFails,
            o =>
            {
                var actions = new PostExitActions();
                actions.ScheduleUpdateApply(
                    _ => throw new InvalidOperationException("no updater"),
                    true
                );

                actions.Run(o.Reporter);
            },
            o => o.Reporter.Steps.Should().Equal("Update.ApplyOnExit")
        );

        // 言語切替で閉じたまま戻らない、を防ぐ
        yield return Row(
            "post-exit-apply-failure-with-relaunch-falls-back-to-relaunch-apply-fails",
            "PostExitActions: 適用が失敗したら登録済みの再起動を自分で行う",
            PostExitRun,
            ApplyFails,
            o =>
            {
                var actions = new PostExitActions();
                var relaunched = 0;
                actions.RelaunchWith(() => relaunched++);
                actions.ScheduleUpdateApply(
                    _ => throw new InvalidOperationException("no updater"),
                    false
                );
                actions.RequestRelaunch();

                actions.Run(o.Reporter);

                o.Bag["relaunched"] = relaunched;
            },
            o =>
            {
                o.Get<int>("relaunched").Should().Be(1);
                o.Reporter.Steps.Should().Equal("Update.ApplyOnExit");
            }
        );

        // 更新の経路は起動処理を自分では持たない。起動処理を要求と別に登録しておかないと、
        // 更新プログラムを起動できなかったとき代わりに起動する手段が無く、QuickER が閉じたまま戻らない
        yield return Row(
            "post-exit-apply-failure-with-update-restart-falls-back-to-relaunch-apply-fails",
            "PostExitActions: 更新の「今すぐ再起動」で適用が失敗しても自分で起動し直す",
            PostExitRun,
            ApplyFails,
            o =>
            {
                var actions = new PostExitActions();
                actions.ReleaseSingleInstanceBeforeRelaunch(() => o.Calls.Add("release"));
                actions.RelaunchWith(() => o.Calls.Add("relaunch"));
                actions.ScheduleUpdateApply(
                    _ => throw new InvalidOperationException("no updater"),
                    true
                );

                actions.Run(o.Reporter);
            },
            o =>
            {
                o.Calls.Should().Equal("release", "relaunch");
                o.Reporter.Steps.Should().Equal("Update.ApplyOnExit");
            }
        );

        yield return Row(
            "post-exit-relaunch-request-without-starter-does-nothing-no-starter",
            "PostExitActions: 起動処理が無ければ再起動の要求があっても何もしない",
            PostExitRun,
            NoStarter,
            o =>
            {
                var actions = new PostExitActions();
                var released = 0;
                actions.ReleaseSingleInstanceBeforeRelaunch(() => released++);
                actions.RequestRelaunch();

                actions.Run(o.Reporter);

                o.Bag["released"] = released;
            },
            o =>
            {
                o.Get<int>("released").Should().Be(0);
                o.Reporter.Reports.Should().BeEmpty();
            }
        );

        yield return Row(
            "post-exit-complete-exit-dispose-failure-still-runs-scheduled-actions-step-throws",
            "PostExitActions: 破棄が投げても更新の適用まで届く",
            CompleteExit,
            StepThrows,
            o =>
            {
                var actions = new PostExitActions();
                actions.ScheduleUpdateApply(restart => o.Calls.Add($"apply({restart})"), true);

                actions.CompleteExit(
                    o.Reporter,
                    () =>
                    {
                        o.Calls.Add("dispose");
                        throw new InvalidOperationException("dispose failed");
                    }
                );
            },
            o =>
            {
                o.Calls.Should().Equal("dispose", "apply(True)");
                o.Reporter.Steps.Should().Equal("App.DisposeServices");
            }
        );

        yield return Row(
            "post-exit-relaunch-releases-single-instance-first-none",
            "PostExitActions: 自分で再起動する直前に単一インスタンスの座を手放す",
            PostExitRun,
            None,
            o =>
            {
                var actions = new PostExitActions();
                actions.ReleaseSingleInstanceBeforeRelaunch(() => o.Calls.Add("release"));
                actions.RelaunchWith(() => o.Calls.Add("relaunch"));
                actions.RequestRelaunch();

                actions.Run(o.Reporter);
            },
            o => o.Calls.Should().Equal("release", "relaunch")
        );

        // 更新プログラムに再起動を任せる経路と、再起動しない終了では座を手放さない（プロセスの終了で手放す）
        foreach (var updateScheduled in new[] { true, false })
        {
            yield return Row(
                $"post-exit-without-own-relaunch-does-not-release-{(updateScheduled ? "update-scheduled" : "nothing-requested")}-{(updateScheduled ? "update-and-relaunch" : "none")}",
                "PostExitActions: 自分で再起動しない終了では座を手放さない",
                PostExitRun,
                updateScheduled ? UpdateAndRelaunch : None,
                o =>
                {
                    var actions = new PostExitActions();
                    var released = 0;
                    actions.ReleaseSingleInstanceBeforeRelaunch(() => released++);
                    actions.RelaunchWith(() => { });

                    if (updateScheduled)
                    {
                        actions.ScheduleUpdateApply(_ => { }, restart: false);
                        actions.RequestRelaunch();
                    }

                    actions.Run(o.Reporter);

                    o.Bag["released"] = released;
                },
                o => o.Get<int>("released").Should().Be(0)
            );
        }

        yield return Row(
            "post-exit-apply-failure-with-relaunch-releases-before-relaunch-apply-fails",
            "PostExitActions: 適用が失敗して自分で再起動するときも座を手放す",
            PostExitRun,
            ApplyFails,
            o =>
            {
                var actions = new PostExitActions();
                actions.ReleaseSingleInstanceBeforeRelaunch(() => o.Calls.Add("release"));
                actions.RelaunchWith(() => o.Calls.Add("relaunch"));
                actions.ScheduleUpdateApply(
                    _ => throw new InvalidOperationException("no updater"),
                    false
                );
                actions.RequestRelaunch();

                actions.Run(o.Reporter);
            },
            o => o.Calls.Should().Equal("release", "relaunch")
        );

        yield return Row(
            "post-exit-release-failure-still-relaunches-release-fails",
            "PostExitActions: 座の手放しが失敗しても再起動は試みる",
            PostExitRun,
            ReleaseFails,
            o =>
            {
                var actions = new PostExitActions();
                var relaunched = 0;
                actions.ReleaseSingleInstanceBeforeRelaunch(() =>
                    throw new InvalidOperationException("release failed")
                );
                actions.RelaunchWith(() => relaunched++);
                actions.RequestRelaunch();

                actions.Run(o.Reporter);

                o.Bag["relaunched"] = relaunched;
            },
            o =>
            {
                o.Get<int>("relaunched").Should().Be(1);
                o.Reporter.Steps.Should().Equal("App.ReleaseSingleInstance");
            }
        );

        yield return Row(
            "post-exit-nothing-scheduled-does-nothing-none",
            "PostExitActions: 登録が無ければ何もしない",
            PostExitRun,
            None,
            o =>
            {
                var actions = new PostExitActions();
                var relaunched = 0;
                actions.RelaunchWith(() => relaunched++);

                actions.Run(o.Reporter);

                o.Bag["relaunched"] = relaunched;
            },
            o =>
            {
                o.Reporter.Reports.Should().BeEmpty();
                o.Get<int>("relaunched").Should().Be(0, "起動処理の登録は再起動の要求ではない");
            }
        );
    }
}
