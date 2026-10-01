using AwesomeAssertions;
using QuickER.Services;
using QuickER.Tests.TestDoubles;

namespace QuickER.Tests.Gui.Services;

/// <summary>
/// 正常終了の最後に行う処理（<see cref="PostExitActions"/>＝更新の適用と再起動）を検証するテストクラス。
/// </summary>
public sealed class PostExitActionsTests
{
    /// <summary>再起動だけが要求されていれば、自分で新しいインスタンスを起動する</summary>
    [Fact(DisplayName = "PostExitActions: 再起動だけなら自分で起動する")]
    public void Run_RelaunchOnly_Relaunches()
    {
        var actions = new PostExitActions();
        var relaunched = 0;
        actions.ScheduleRelaunch(() => relaunched++);

        actions.Run(new RecordingShutdownFailureReporter());

        relaunched.Should().Be(1);
    }

    /// <summary>更新の適用だけが要求されていれば、登録した再起動の有無のまま適用する</summary>
    [Theory(DisplayName = "PostExitActions: 更新の適用だけなら登録した再起動の有無で適用する")]
    [InlineData(true)]
    [InlineData(false)]
    public void Run_UpdateOnly_AppliesWithRegisteredRestart(bool restart)
    {
        var actions = new PostExitActions();
        var applied = new List<bool>();
        actions.ScheduleUpdateApply(applied.Add, restart);

        actions.Run(new RecordingShutdownFailureReporter());

        applied.Should().Equal(restart);
    }

    /// <summary>
    /// 更新の適用と再起動が両方要求されていれば、再起動は更新プログラムに任せ、自分では起動しない
    /// </summary>
    /// <remarks>
    /// 更新プログラムは適用の前にインストール先で動いている QuickER を止めるため、自分で起動すると
    /// そのインスタンスが止められて再起動が失われる。どちらが先に要求されても同じ。
    /// </remarks>
    [Theory(DisplayName = "PostExitActions: 更新と再起動が両方なら更新プログラムに再起動を任せる")]
    [InlineData(true)]
    [InlineData(false)]
    public void Run_UpdateAndRelaunch_LetsUpdaterRestart(bool updateFirst)
    {
        var actions = new PostExitActions();
        var applied = new List<bool>();
        var relaunched = 0;

        if (updateFirst)
        {
            actions.ScheduleUpdateApply(applied.Add, restart: false);
            actions.ScheduleRelaunch(() => relaunched++);
        }
        else
        {
            actions.ScheduleRelaunch(() => relaunched++);
            actions.ScheduleUpdateApply(applied.Add, restart: false);
        }

        actions.Run(new RecordingShutdownFailureReporter());

        applied.Should().Equal(true);
        relaunched.Should().Be(0, "自分で起動したインスタンスは適用の前に止められる");
    }

    /// <summary>実行した登録は消える（同じ処理を 2 回行わない）</summary>
    [Fact(DisplayName = "PostExitActions: 実行した登録は消える")]
    public void Run_ClearsScheduledActions()
    {
        var actions = new PostExitActions();
        var applied = 0;
        actions.ScheduleUpdateApply(_ => applied++, restart: true);

        actions.Run(new RecordingShutdownFailureReporter());
        actions.Run(new RecordingShutdownFailureReporter());

        applied.Should().Be(1);
    }

    /// <summary>更新の適用が失敗しても外へ投げず、終了の連鎖の 1 段として記録する</summary>
    [Fact(DisplayName = "PostExitActions: 適用の失敗は投げずに記録する")]
    public void Run_ApplyFails_IsReportedNotThrown()
    {
        var actions = new PostExitActions();
        var reporter = new RecordingShutdownFailureReporter();
        actions.ScheduleUpdateApply(_ => throw new InvalidOperationException("no updater"), true);

        var act = () => actions.Run(reporter);

        act.Should().NotThrow();
        reporter.Steps.Should().Equal("Update.ApplyOnExit");
    }

    /// <summary>
    /// 更新の適用が失敗したら、任せるはずだった再起動を自分で行う（言語切替で閉じたまま戻らない、を防ぐ）
    /// </summary>
    [Fact(DisplayName = "PostExitActions: 適用が失敗したら登録済みの再起動を自分で行う")]
    public void Run_ApplyFailsWithRelaunch_FallsBackToRelaunch()
    {
        var actions = new PostExitActions();
        var reporter = new RecordingShutdownFailureReporter();
        var relaunched = 0;
        actions.ScheduleUpdateApply(_ => throw new InvalidOperationException("no updater"), false);
        actions.ScheduleRelaunch(() => relaunched++);

        actions.Run(reporter);

        relaunched.Should().Be(1);
        reporter.Steps.Should().Equal("Update.ApplyOnExit");
    }

    /// <summary>DI コンテナの破棄が投げても、後段（更新の適用）まで届く</summary>
    [Fact(DisplayName = "PostExitActions: 破棄が投げても更新の適用まで届く")]
    public void CompleteExit_DisposeThrows_StillRunsScheduledActions()
    {
        var actions = new PostExitActions();
        var reporter = new RecordingShutdownFailureReporter();
        var calls = new List<string>();
        actions.ScheduleUpdateApply(restart => calls.Add($"apply({restart})"), true);

        actions.CompleteExit(
            reporter,
            () =>
            {
                calls.Add("dispose");
                throw new InvalidOperationException("dispose failed");
            }
        );

        calls.Should().Equal("dispose", "apply(True)");
        reporter.Steps.Should().Equal("App.DisposeServices");
    }

    /// <summary>何も登録されていなければ何もしない</summary>
    [Fact(DisplayName = "PostExitActions: 登録が無ければ何もしない")]
    public void Run_NothingScheduled_DoesNothing()
    {
        var reporter = new RecordingShutdownFailureReporter();

        new PostExitActions().Run(reporter);

        reporter.Reports.Should().BeEmpty();
    }
}
