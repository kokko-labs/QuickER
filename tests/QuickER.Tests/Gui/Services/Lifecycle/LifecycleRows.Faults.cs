using System.Diagnostics;
using System.IO;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using QuickER.AI.UI;
using QuickER.Extensibility;
using QuickER.Gui.Abstractions;
using QuickER.Services;
using QuickER.Tests.TestDoubles;
using QuickER.Tests.TestSupport.Engines;
using static QuickER.Tests.Gui.Services.Lifecycle.LifecycleEntrance;
using static QuickER.Tests.Gui.Services.Lifecycle.LifecycleFault;

namespace QuickER.Tests.Gui.Services.Lifecycle;

/// <summary>
/// アプリの一生の表：器の入口（モジュールの後始末・DI の破棄から最後の処理まで・クラッシュの UI 入口）へ、
/// 器そのものの行（<c>ShutdownSteps.Run</c>・<c>HandleCrash</c>）と同じ失敗を重ねる行
/// </summary>
/// <remarks>
/// 期待は器の契約と同じ：失敗は記録して先へ進み、外へ出さない。入口が器を通していることを、後続の段の実行で確かめる。
/// 総当たりの 2 本（<c>ShutdownChainExhaustive*</c>・<c>PostExitExhaustive*</c>）は <c>ShutdownSteps.Run</c> と
/// <c>PostExitActions.Run</c> 直で流すので、<c>FeatureModuleShutdown</c>・<c>CompleteExit</c>・<c>HandleUiCrash</c> の入口は通らない。
/// </remarks>
internal static partial class LifecycleRows
{
    /// <summary>3 つのモジュール（<paramref name="throwingId"/> のものだけが後始末で投げる）</summary>
    private static IFeatureModule[] ThreeModules(List<string> log, string? throwingId) =>
        new[] { "a", "b", "c" }
            .Select(id =>
                (IFeatureModule)
                    new ClosingModule(
                        id,
                        log,
                        id == throwingId
                            ? () => throw new InvalidOperationException($"{id} の後始末に失敗")
                            : null
                    )
            )
            .ToArray();

    /// <summary>1 回だけ実行の入口（空の DI を渡す）</summary>
    private static FeatureModuleShutdownOnce OnceOf(
        IFeatureModule[] modules,
        IShutdownFailureReporter reporter
    ) => new(modules, new ServiceCollection().BuildServiceProvider(), reporter);

    private static IEnumerable<ScenarioRow<LifecycleCase>> FaultRows()
    {
        yield return Row(
            "modules-close-all-reporter-failure-does-not-propagate-reporter-throws",
            "FeatureModuleShutdown.CloseAll: 記録先が投げても全モジュールへ通知する",
            ModuleCloseAll,
            ReporterThrows,
            o =>
            {
                var log = new List<string>();
                o.Bag["log"] = log;

                FeatureModuleShutdown.CloseAll(
                    ThreeModules(log, "b"),
                    services: null!,
                    new ThrowingShutdownFailureReporter()
                );
            },
            o => o.Get<List<string>>("log").Should().Equal("a", "b", "c")
        );

        yield return Row(
            "modules-close-all-continues-to-remaining-modules-after-throw-step-throws",
            "FeatureModuleShutdown.CloseAll: 1 つのモジュールだけが投げても後続は呼ばれ、その 1 件が記録される",
            ModuleCloseAll,
            StepThrows,
            o =>
            {
                var log = new List<string>();
                o.Bag["log"] = log;

                FeatureModuleShutdown.CloseAll(ThreeModules(log, "b"), services: null!, o.Reporter);
            },
            o =>
            {
                o.Get<List<string>>("log").Should().Equal("a", "b", "c");
                o.Reporter.Steps.Should().Equal("FeatureModule.b.OnMainWindowClosing");
            }
        );

        yield return Row(
            "modules-shutdown-once-notifies-every-module-none",
            "1 回だけ実行の入口: 1 回呼べば全モジュールへ通知され、何も記録されない",
            ModuleShutdownOnce,
            None,
            o =>
            {
                var log = new List<string>();
                o.Bag["log"] = log;

                OnceOf(ThreeModules(log, throwingId: null), o.Reporter).CloseAll();
            },
            o =>
            {
                o.Get<List<string>>("log").Should().Equal("a", "b", "c");
                o.Reporter.Reports.Should().BeEmpty();
            }
        );

        yield return Row(
            "modules-shutdown-once-continues-to-remaining-modules-after-throw-step-throws",
            "1 回だけ実行の入口: 1 つのモジュールだけが投げても後続は呼ばれ、その 1 件が記録される",
            ModuleShutdownOnce,
            StepThrows,
            o =>
            {
                var log = new List<string>();
                o.Bag["log"] = log;

                OnceOf(ThreeModules(log, "b"), o.Reporter).CloseAll();
            },
            o =>
            {
                o.Get<List<string>>("log").Should().Equal("a", "b", "c");
                o.Reporter.Steps.Should().Equal("FeatureModule.b.OnMainWindowClosing");
            }
        );

        yield return Row(
            "modules-shutdown-once-reporter-failure-does-not-propagate-reporter-throws",
            "1 回だけ実行の入口: 記録先が投げても全モジュールへ通知する",
            ModuleShutdownOnce,
            ReporterThrows,
            o =>
            {
                var log = new List<string>();
                o.Bag["log"] = log;

                OnceOf(ThreeModules(log, "b"), new ThrowingShutdownFailureReporter()).CloseAll();
            },
            o => o.Get<List<string>>("log").Should().Equal("a", "b", "c")
        );

        yield return Row(
            "post-exit-complete-exit-runs-dispose-then-scheduled-actions-none",
            "PostExitActions.CompleteExit: 破棄の後に登録された更新の適用を実行し、何も記録しない",
            CompleteExit,
            None,
            o =>
            {
                var actions = new PostExitActions();
                actions.ScheduleUpdateApply(restart => o.Calls.Add($"apply({restart})"), true);

                actions.CompleteExit(o.Reporter, () => o.Calls.Add("dispose"));
            },
            o =>
            {
                o.Calls.Should().Equal("dispose", "apply(True)");
                o.Reporter.Reports.Should().BeEmpty();
            }
        );

        yield return Row(
            "post-exit-complete-exit-reporter-failure-still-runs-scheduled-actions-reporter-throws",
            "PostExitActions.CompleteExit: 破棄が投げ、記録先も投げても更新の適用まで届く",
            CompleteExit,
            ReporterThrows,
            o =>
            {
                var actions = new PostExitActions();
                actions.ScheduleUpdateApply(restart => o.Calls.Add($"apply({restart})"), true);

                actions.CompleteExit(
                    new ThrowingShutdownFailureReporter(),
                    () =>
                    {
                        o.Calls.Add("dispose");
                        throw new InvalidOperationException("dispose failed");
                    }
                );
            },
            o => o.Calls.Should().Equal("dispose", "apply(True)")
        );

        yield return Row(
            "post-exit-complete-exit-apply-failure-falls-back-to-relaunch-apply-fails",
            "PostExitActions.CompleteExit: 適用が失敗したら記録し、自分で再起動する",
            CompleteExit,
            ApplyFails,
            o =>
            {
                var actions = new PostExitActions();
                actions.RelaunchWith(() => o.Calls.Add("relaunch"));
                actions.ScheduleUpdateApply(
                    restart =>
                    {
                        o.Calls.Add($"apply({restart})");
                        throw new InvalidOperationException("no updater");
                    },
                    true
                );

                actions.CompleteExit(o.Reporter, () => o.Calls.Add("dispose"));
            },
            o =>
            {
                o.Calls.Should().Equal("dispose", "apply(True)", "relaunch");
                o.Reporter.Steps.Should().Equal("Update.ApplyOnExit");
            }
        );

        yield return Row(
            "post-exit-apply-failure-with-relaunch-falls-back-to-relaunch-reporter-throws",
            "PostExitActions: 適用の失敗を記録できなくても、自分での再起動まで届く",
            PostExitRun,
            ReporterThrows,
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

                actions.Run(new ThrowingShutdownFailureReporter());

                o.Bag["relaunched"] = relaunched;
            },
            o => o.Get<int>("relaunched").Should().Be(1)
        );

        // HandleCrash の書き込めない保存先の行（ログが書けなくてもダイアログまで進み、ログのパスは null で渡す）
        yield return Row(
            "crash-handle-unwritable-log-path-still-shows-dialog-with-null-log-path-unwritable-path",
            "HandleCrash: ログを書けない保存先でも緊急保存・後始末・ダイアログを実行し、ログのパスは null で渡す",
            HandleCrash,
            UnwritablePath,
            o =>
            {
                ResetCrashGuard(o);
                var file = BlockingFile(o);

                o.Bag["handled"] = CrashHandlingService.HandleCrash(
                    NestedException(),
                    "1.2.3",
                    () => o.Calls.Add("save"),
                    () => o.Calls.Add("close"),
                    logPath =>
                    {
                        o.Calls.Add("dialog");
                        o.Bag["receivedLogPath"] = logPath;
                    },
                    file
                );
            },
            o =>
            {
                o.Get<bool>("handled").Should().BeTrue();
                o.Calls.Should().Equal("save", "close", "dialog");
                o.Get<string?>("receivedLogPath")
                    .Should()
                    .BeNull("書けなかったログのパスは渡さない");
            }
        );

        // HandleUiCrash の本体は `if (HandleCrash(...)) exit();`。HandleCrash が各段の失敗を握り潰すので、
        // どの形でも終了は全段の後に 1 回だけ呼ばれる
        var uiCrashFaults = new (LifecycleFault Fault, string Slug, string Note)[]
        {
            (None, "none", "失敗がなければ緊急保存・後始末・ダイアログの後に 1 回だけ終了する"),
            (
                StepThrows,
                "step-throws",
                "機能モジュールの後始末が失敗してもダイアログを出し、その後に 1 回だけ終了する"
            ),
            (
                EmergencySaveThrows,
                "emergency-save-throws",
                "緊急保存が失敗してもログとダイアログへ進み、その後に 1 回だけ終了する"
            ),
            (DialogThrows, "dialog-throws", "ダイアログが失敗しても例外を出さず、1 回だけ終了する"),
            (
                HangingCleanup,
                "hanging-cleanup",
                "投げる・ハングする後始末が混ざっても上限で戻り、ダイアログの後に 1 回だけ終了する"
            ),
            (
                UnwritablePath,
                "unwritable-path",
                "ログを書けない保存先でもダイアログを出し、ログのパスは null で渡して 1 回だけ終了する"
            ),
        };

        foreach (var (fault, slug, note) in uiCrashFaults)
        {
            yield return Row(
                $"crash-handle-ui-crash-exits-once-after-all-steps-{slug}",
                $"HandleUiCrash: {note}",
                HandleUiCrash,
                fault,
                o => ActUiCrash(o, fault),
                o => ExpectUiCrash(o, fault)
            );
        }

        // Report は WriteCrashLog と同じ書き出しを通る。時刻を渡す差し込み点が無いので、同じ秒（ミリ秒）に落ちる保証は無く、
        // 同じ秒にならなくても成り立つ弱い行（既存の WriteCrashLog の同じ秒の行と同じ弱さ）。
        // 同じ時刻の衝突そのものは CrashHandlingServiceTests の 64 並行の行が WriteCrashLog に対して見ている
        yield return Row(
            "crash-report-consecutive-reports-do-not-overwrite-same-second",
            "Report: 続けて 2 回記録しても上書きせず、2 つのログができる",
            ShutdownFailureReport,
            SameSecond,
            o =>
            {
                var reporter = new CrashLogShutdownFailureReporter(o.Folder);

                reporter.Report("Step.First", new InvalidOperationException("1 件目"));
                reporter.Report("Step.Second", new InvalidOperationException("2 件目"));
            },
            o =>
            {
                Directory.GetFiles(o.Folder, "crash-*.log").Should().BeEmpty();
                var logs = Directory.GetFiles(o.Folder, "shutdown-*.log");

                logs.Should().HaveCount(2);
                logs.Select(File.ReadAllText)
                    .Should()
                    .Contain(c => c.Contains("1 件目") && c.Contains("Step.First"))
                    .And.Contain(c => c.Contains("2 件目") && c.Contains("Step.Second"));
            }
        );
    }

    /// <summary>フォルダの位置に置くファイル（<c>Directory.CreateDirectory</c> が必ず失敗する保存先）を作って返す</summary>
    private static string BlockingFile(LifecycleObservation o)
    {
        var file = Path.Combine(o.Folder, $"crash-block-{Guid.NewGuid()}.tmp");
        File.WriteAllText(file, "block");
        return file;
    }

    /// <summary>HandleUiCrash の入口を、失敗の形に応じて組んで呼ぶ（呼び出しは <c>o.Calls</c> へ順に記録する）</summary>
    private static void ActUiCrash(LifecycleObservation o, LifecycleFault fault)
    {
        ResetCrashGuard(o);

        Action save = () => o.Calls.Add("save");
        Action close = () => o.Calls.Add("close");
        var logFolder = o.Folder;

        switch (fault)
        {
            case EmergencySaveThrows:
                save = () =>
                {
                    o.Calls.Add("save");

                    throw new InvalidOperationException("緊急保存の二次例外");
                };
                break;
            case StepThrows:
                close = () =>
                {
                    o.Calls.Add("close");

                    throw new InvalidOperationException("後始末の二次例外");
                };
                break;
            case HangingCleanup:
                var hangingEngine = new HangingAsyncDisposable();
                o.OnDispose(hangingEngine.Release);
                close = OnceOf(
                    [
                        new ClosingModule(
                            "throws",
                            o.Calls,
                            () => throw new InvalidOperationException("壊れた UI")
                        ),
                        new ClosingModule(
                            "hangs",
                            o.Calls,
                            () =>
                                EngineShutdown.DisposeAll(
                                    TimeSpan.FromMilliseconds(300),
                                    hangingEngine
                                )
                        ),
                        new ClosingModule("last", o.Calls),
                    ],
                    o.Reporter
                ).CloseAll;
                break;
            case UnwritablePath:
                logFolder = BlockingFile(o);
                break;
        }

        var stopwatch = Stopwatch.StartNew();

        CrashHandlingService.HandleUiCrash(
            NestedException(),
            "1.2.3",
            save,
            close,
            logPath =>
            {
                o.Calls.Add("dialog");
                o.Bag["receivedLogPath"] = logPath;

                if (fault == DialogThrows)
                {
                    throw new InvalidOperationException("ダイアログの二次例外");
                }
            },
            () => o.Calls.Add("exit"),
            logFolder
        );

        stopwatch.Stop();
        o.Bag["elapsed"] = stopwatch.Elapsed;
    }

    /// <summary>HandleUiCrash の行の表明（全段が記録の順に実行され、終了が最後に 1 回）</summary>
    private static void ExpectUiCrash(LifecycleObservation o, LifecycleFault fault)
    {
        if (fault == HangingCleanup)
        {
            o.Calls.Should().Equal("save", "throws", "hangs", "last", "dialog", "exit");
            o.Reporter.Steps.Should().Equal("FeatureModule.throws.OnMainWindowClosing");
            o.Get<TimeSpan>("elapsed")
                .Should()
                .BeLessThan(TimeSpan.FromSeconds(5), "ハングした破棄は上限で戻る");
        }
        else
        {
            o.Calls.Should().Equal("save", "close", "dialog", "exit");
        }

        var receivedLogPath = o.Get<string?>("receivedLogPath");

        if (fault == UnwritablePath)
        {
            receivedLogPath.Should().BeNull("書けなかったログのパスは渡さない");
            return;
        }

        receivedLogPath.Should().NotBeNull();
        File.Exists(receivedLogPath!).Should().BeTrue();
    }
}
