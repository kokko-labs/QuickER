using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using QuickER.Extensibility;
using QuickER.Gui.Abstractions;
using QuickER.Services;
using QuickER.Tests.TestDoubles;
using QuickER.Tests.TestSupport.Engines;
using static QuickER.Tests.Gui.Services.Lifecycle.LifecycleEntrance;
using static QuickER.Tests.Gui.Services.Lifecycle.LifecycleFault;

namespace QuickER.Tests.Gui.Services.Lifecycle;

/// <summary>
/// アプリの一生の表：終了の連鎖（QuickER.Gui.Abstractions の <c>ShutdownSteps</c>、QuickER.Gui の
/// <c>FeatureModuleShutdown</c>・<c>FeatureModuleShutdownOnce</c>）。
/// 終了の連鎖は後段ほど取り返しがつかない後始末（常駐する子プロセスの停止）が並ぶので、
/// 前段の失敗で後段へ届かなくなると子プロセスが孤児として残る
/// </summary>
internal static partial class LifecycleRows
{
    private static IEnumerable<ScenarioRow<LifecycleCase>> ShutdownRows()
    {
        yield return Row(
            "steps-successful-step-runs-and-reports-nothing-none",
            "成功した段は記録されない",
            ShutdownStepsRun,
            None,
            o =>
            {
                var ran = false;

                ShutdownSteps.Run(o.Reporter, "Step", () => ran = true);

                o.Bag["ran"] = ran;
            },
            o =>
            {
                o.Get<bool>("ran").Should().BeTrue();
                o.Reporter.Reports.Should().BeEmpty();
            }
        );

        yield return Row(
            "steps-failed-step-is-recorded-and-swallowed-step-throws",
            "失敗した段は例外を伝播させず、段の名前とともに記録される",
            ShutdownStepsRun,
            StepThrows,
            o =>
            {
                var failure = new InvalidOperationException("保存に失敗");
                o.Bag["failure"] = failure;

                ShutdownSteps.Run(o.Reporter, "AiChat.SaveSettings", () => throw failure);
            },
            o =>
            {
                o.Reporter.Reports.Should().ContainSingle();
                o.Reporter.Reports[0].Step.Should().Be("AiChat.SaveSettings");
                o.Reporter.Reports[0].Exception.Should().BeSameAs(o.Get<Exception>("failure"));
            }
        );

        // 途中の失敗で打ち切ると、エンジンの破棄（常駐プロセスの停止）やクローズへ届かない
        yield return Row(
            "steps-failures-are-recorded-each-and-last-step-is-reached-multiple-throw",
            "複数の段が失敗しても、それぞれ記録されて最後の段まで進む",
            ShutdownStepsRun,
            MultipleStepsThrow,
            o =>
            {
                var executed = new List<string>();

                void Step(string name, bool throws) =>
                    ShutdownSteps.Run(
                        o.Reporter,
                        name,
                        () =>
                        {
                            executed.Add(name);

                            if (throws)
                            {
                                throw new InvalidOperationException(name);
                            }
                        }
                    );

                Step("Interrupt", throws: false);
                Step("SaveSettings", throws: true);
                Step("ShutdownEngines", throws: true);
                Step("Close", throws: false);

                o.Bag["executed"] = executed;
            },
            o =>
            {
                o.Get<List<string>>("executed")
                    .Should()
                    .Equal("Interrupt", "SaveSettings", "ShutdownEngines", "Close");
                o.Reporter.Steps.Should().Equal("SaveSettings", "ShutdownEngines");
            }
        );

        // 契約違反（記録中に投げる）を再現する記録先でも、連鎖を止めないことが器の目的そのもの
        yield return Row(
            "steps-reporter-failure-does-not-propagate-reporter-throws",
            "記録先が投げても呼び出し元へ伝播しない",
            ShutdownStepsRun,
            ReporterThrows,
            _ =>
            {
                var reporter = new ThrowingShutdownFailureReporter();

                ShutdownSteps.Run(
                    reporter,
                    "Step",
                    () => throw new InvalidOperationException("失敗")
                );
            },
            _ => { }
        );

        yield return Row(
            "modules-close-all-notifies-every-module-none",
            "全モジュールへ後始末が通知される",
            ModuleCloseAll,
            None,
            o =>
            {
                var log = new List<string>();
                o.Bag["log"] = log;
                var modules = new IFeatureModule[]
                {
                    new ClosingModule("a", log),
                    new ClosingModule("b", log),
                };

                FeatureModuleShutdown.CloseAll(modules, services: null!, o.Reporter);
            },
            o =>
            {
                o.Get<List<string>>("log").Should().Equal("a", "b");
                o.Reporter.Reports.Should().BeEmpty();
            }
        );

        yield return Row(
            "modules-close-all-continues-to-remaining-modules-after-throw-multiple-throw",
            "1 つのモジュールが投げても後続のモジュールは呼ばれる",
            ModuleCloseAll,
            MultipleStepsThrow,
            o =>
            {
                var log = new List<string>();
                o.Bag["log"] = log;
                var modules = new IFeatureModule[]
                {
                    new ClosingModule(
                        "ai-chat",
                        log,
                        () => throw new InvalidOperationException("ai-chat の後始末に失敗")
                    ),
                    new ClosingModule(
                        "mock-generation",
                        log,
                        () => throw new InvalidOperationException("mock-generation の後始末に失敗")
                    ),
                    new ClosingModule("code-generation", log),
                };

                FeatureModuleShutdown.CloseAll(modules, services: null!, o.Reporter);
            },
            o =>
            {
                o.Get<List<string>>("log")
                    .Should()
                    .Equal("ai-chat", "mock-generation", "code-generation");
                o.Reporter.Steps.Should()
                    .Equal(
                        "FeatureModule.ai-chat.OnMainWindowClosing",
                        "FeatureModule.mock-generation.OnMainWindowClosing"
                    );
            }
        );

        // 後始末の入口が 2 つ（メインウィンドウの Closing とクラッシュ経路）あっても、Closing の後にクラッシュしたとき二重に走らない
        yield return Row(
            "modules-shutdown-once-second-call-does-nothing-reentry",
            "1 回だけ実行のガード: Closing の後にクラッシュ経路から呼ばれても二重に走らない",
            ModuleShutdownOnce,
            Reentry,
            o =>
            {
                var log = new List<string>();
                o.Bag["log"] = log;
                var shutdown = new FeatureModuleShutdownOnce(
                    [new ClosingModule("ai-chat", log), new ClosingModule("mock-generation", log)],
                    new ServiceCollection().BuildServiceProvider(),
                    o.Reporter
                );

                shutdown.CloseAll();
                shutdown.CloseAll();
            },
            o => o.Get<List<string>>("log").Should().Equal("ai-chat", "mock-generation")
        );
    }

    /// <summary>契約違反（記録中に投げる）を再現する検証用の記録先</summary>
    private sealed class ThrowingShutdownFailureReporter : IShutdownFailureReporter
    {
        public void Report(string step, Exception exception) =>
            throw new InvalidOperationException("記録にも失敗した");
    }
}
