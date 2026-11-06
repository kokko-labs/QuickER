using AwesomeAssertions;
using QuickER.Services;
using QuickER.Tests.TestDoubles;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.Gui.Services.Lifecycle;

/// <summary>更新の適用の登録（<c>ScheduleUpdateApply</c>）</summary>
internal enum PostExitUpdate
{
    /// <summary>登録しない</summary>
    None,

    /// <summary>適用する・restart: true</summary>
    Restart,

    /// <summary>適用する・restart: false</summary>
    NoRestart,

    /// <summary>適用が投げる・restart: true</summary>
    ThrowsRestart,

    /// <summary>適用が投げる・restart: false</summary>
    ThrowsNoRestart,
}

/// <summary>再起動の要求（<c>RequestRelaunch</c>）</summary>
internal enum PostExitRequest
{
    /// <summary>要求しない</summary>
    No,

    /// <summary>要求する</summary>
    Yes,
}

/// <summary>起動処理の登録（<c>RelaunchWith</c>）</summary>
internal enum PostExitStarter
{
    /// <summary>登録しない</summary>
    No,

    /// <summary>登録する</summary>
    Yes,
}

/// <summary>座の手放しの登録（<c>ReleaseSingleInstanceBeforeRelaunch</c>）</summary>
internal enum PostExitRelease
{
    /// <summary>登録しない</summary>
    None,

    /// <summary>登録する（成功する）</summary>
    Ok,

    /// <summary>登録する（投げる）</summary>
    Throws,
}

/// <summary>終了の最後の処理の総当たりの 1 組</summary>
internal sealed record PostExitExhaustiveCase(
    PostExitUpdate Update,
    PostExitRequest Request,
    PostExitStarter Starter,
    PostExitRelease Release
);

/// <summary>
/// 終了の最後の処理の総当たり（QuickER.Gui の <c>PostExitActions</c>）：更新・再起動の要求・起動処理・座の手放しの全 60 組を
/// 決定的に列挙し、<c>Run</c> を 2 回呼んで呼び出しの列と記録先の段名をテスト側のオラクルと照合する。
/// オラクルは製品の文書（更新と再起動が両方なら更新プログラムに任せる・適用の失敗は自分で起動し直す・
/// 自分で起動する直前だけ座を手放す）から導き、製品のコードを写さない
/// </summary>
internal static class PostExitExhaustiveRows
{
    /// <summary>表（全組）</summary>
    public static ScenarioTable<PostExitExhaustiveCase> Table =>
        new(
            EngineIds.OsBoundary,
            from update in Enum.GetValues<PostExitUpdate>()
            from request in Enum.GetValues<PostExitRequest>()
            from starter in Enum.GetValues<PostExitStarter>()
            from release in Enum.GetValues<PostExitRelease>()
            let input = new PostExitExhaustiveCase(update, request, starter, release)
            select new ScenarioRow<PostExitExhaustiveCase>(
                $"post-exit-{update}-{request}-{starter}-{release}".ToLowerInvariant(),
                null,
                $"終了の最後の処理: 更新={update}・要求={request}・起動処理={starter}・手放し={release}",
                input
            )
        );

    /// <summary>1 組を流してオラクルと照合する。常に true</summary>
    public static bool Run(PostExitExhaustiveCase c)
    {
        var calls = new List<string>();
        var reporter = new RecordingShutdownFailureReporter();
        var actions = new PostExitActions();

        var registeredRestart = c.Update is PostExitUpdate.Restart or PostExitUpdate.ThrowsRestart;
        var applyThrows =
            c.Update is PostExitUpdate.ThrowsRestart or PostExitUpdate.ThrowsNoRestart;

        if (c.Update != PostExitUpdate.None)
        {
            actions.ScheduleUpdateApply(
                restart =>
                {
                    calls.Add($"apply({restart})");

                    if (applyThrows)
                    {
                        throw new InvalidOperationException("no updater");
                    }
                },
                registeredRestart
            );
        }

        if (c.Request == PostExitRequest.Yes)
        {
            actions.RequestRelaunch();
        }

        if (c.Starter == PostExitStarter.Yes)
        {
            actions.RelaunchWith(() => calls.Add("relaunch"));
        }

        if (c.Release != PostExitRelease.None)
        {
            actions.ReleaseSingleInstanceBeforeRelaunch(() =>
            {
                calls.Add("release");

                if (c.Release == PostExitRelease.Throws)
                {
                    throw new InvalidOperationException("release failed");
                }
            });
        }

        // オラクル（文書から導く）
        var requested = c.Request == PostExitRequest.Yes;
        var relaunches =
            c.Starter == PostExitStarter.Yes
            && (
                (c.Update == PostExitUpdate.None && requested)
                || (applyThrows && (requested || registeredRestart))
            );
        var expectedCalls = new List<string>();
        var expectedSteps = new List<string>();

        if (c.Update != PostExitUpdate.None)
        {
            expectedCalls.Add($"apply({registeredRestart || requested})");
        }

        if (applyThrows)
        {
            expectedSteps.Add("Update.ApplyOnExit");
        }

        if (relaunches)
        {
            if (c.Release != PostExitRelease.None)
            {
                expectedCalls.Add("release");
            }

            if (c.Release == PostExitRelease.Throws)
            {
                expectedSteps.Add("App.ReleaseSingleInstance");
            }

            expectedCalls.Add("relaunch");
        }

        var act = () => actions.Run(reporter);

        act.Should().NotThrow("終了の最後の処理は例外を外へ出さない");
        calls.Should().Equal(expectedCalls);
        reporter.Steps.Should().Equal(expectedSteps);

        act.Should().NotThrow();
        calls.Should().Equal(expectedCalls, "2 回目の Run は何も呼ばない");
        reporter.Steps.Should().Equal(expectedSteps, "2 回目の Run は何も記録しない");
        return true;
    }
}
