using AwesomeAssertions;
using QuickER.Gui.Abstractions;
using QuickER.Tests.TestDoubles;

namespace QuickER.Tests.Gui.Common;

/// <summary>
/// アプリ終了の連鎖を段ごとに受け止める器（<see cref="ShutdownSteps"/>）を検証するテストクラス。
/// </summary>
/// <remarks>
/// 終了の連鎖は後段ほど取り返しがつかない後始末（常駐する子プロセスの停止）が並ぶため、
/// 前段の失敗で後段へ届かなくなると子プロセスが孤児として残る。
/// 「失敗しても次へ進む」「失敗した段の名前が記録される」の 2 点を固定する。
/// </remarks>
public class ShutdownStepsTests
{
    /// <summary>成功する段はそのまま実行され、何も記録されないことを検証する</summary>
    [Fact(DisplayName = "成功した段は記録されない")]
    public void Run_WhenActionSucceeds_ReportsNothing()
    {
        var reporter = new RecordingShutdownFailureReporter();
        var ran = false;

        ShutdownSteps.Run(reporter, "Step", () => ran = true);

        ran.Should().BeTrue();
        reporter.Reports.Should().BeEmpty();
    }

    /// <summary>段が投げても呼び出し元へ伝播せず、段の名前と例外が記録されることを検証する</summary>
    [Fact(DisplayName = "失敗した段は例外を伝播させず、段の名前とともに記録される")]
    public void Run_WhenActionThrows_RecordsStepAndSwallows()
    {
        var reporter = new RecordingShutdownFailureReporter();
        var failure = new InvalidOperationException("保存に失敗");

        var act = () => ShutdownSteps.Run(reporter, "AiChat.SaveSettings", () => throw failure);

        act.Should().NotThrow();
        reporter.Reports.Should().ContainSingle();
        reporter.Reports[0].Step.Should().Be("AiChat.SaveSettings");
        reporter.Reports[0].Exception.Should().BeSameAs(failure);
    }

    /// <summary>
    /// 複数の段が失敗しても、それぞれが記録されたうえで最後の段まで到達することを検証する。
    /// </summary>
    /// <remarks>
    /// 途中の失敗で打ち切ると、エンジンの破棄（常駐プロセスの停止）やクローズへ届かない。
    /// </remarks>
    [Fact(DisplayName = "複数の段が失敗しても、それぞれ記録されて最後の段まで進む")]
    public void Run_WhenMultipleStepsThrow_RecordsEachAndReachesLastStep()
    {
        var reporter = new RecordingShutdownFailureReporter();
        var executed = new List<string>();

        void Step(string name, bool throws) =>
            ShutdownSteps.Run(
                reporter,
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

        executed.Should().Equal("Interrupt", "SaveSettings", "ShutdownEngines", "Close");
        reporter.Steps.Should().Equal("SaveSettings", "ShutdownEngines");
    }

    /// <summary>記録先が投げても連鎖を止めないことを検証する（器の目的そのもの）</summary>
    [Fact(DisplayName = "記録先が投げても呼び出し元へ伝播しない")]
    public void Run_WhenReporterThrows_DoesNotPropagate()
    {
        var reporter = new ThrowingShutdownFailureReporter();

        var act = () =>
            ShutdownSteps.Run(reporter, "Step", () => throw new InvalidOperationException("失敗"));

        act.Should().NotThrow();
    }

    /// <summary>契約違反（記録中に投げる）を再現する検証用の記録先</summary>
    private sealed class ThrowingShutdownFailureReporter : IShutdownFailureReporter
    {
        public void Report(string step, Exception exception) =>
            throw new InvalidOperationException("記録にも失敗した");
    }
}
