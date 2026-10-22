using AwesomeAssertions;
using Xunit.Sdk;

namespace QuickER.Tests.TestSupport;

/// <summary>
/// <see cref="PhysicalMouseButtons"/> の待ち（押されている間は見直し、離されたら進む・上限で失敗する）を、
/// ボタンの状態と待ち方を差し替えて検証するテストクラス
/// </summary>
public sealed class PhysicalMouseButtonsTests
{
    [Fact(DisplayName = "ボタンが押されている間は待ち、離されたら進む")]
    public void WaitsWhilePressed_ProceedsOnceReleased()
    {
        var polls = 0;
        var waits = 0;

        PhysicalMouseButtons.WaitUntilReleased(
            () => ++polls <= 3 ? "左ボタン" : null,
            TimeSpan.FromSeconds(10),
            () => waits++
        );

        polls.Should().Be(4, "押されていた 3 回と、離されたのを見た 1 回");
        waits.Should().Be(3);
    }

    [Fact(DisplayName = "離されていれば待たずに進む")]
    public void ProceedsImmediately_WhenReleased()
    {
        var waits = 0;

        PhysicalMouseButtons.WaitUntilReleased(() => null, TimeSpan.FromSeconds(10), () => waits++);

        waits.Should().Be(0, "待たないので、離されている通常の実行では結果が変わらない");
    }

    [Fact(DisplayName = "上限を過ぎても押されたままなら、押されているボタンを名指しして失敗する")]
    public void FailsAtTimeout_NamingTheButton()
    {
        var act = () =>
            PhysicalMouseButtons.WaitUntilReleased(
                () => "右ボタン",
                TimeSpan.FromMilliseconds(30),
                () => Thread.Sleep(5)
            );

        act.Should()
            .Throw<FailException>()
            .Which.Message.Should()
            .Contain("実際のマウスのボタンが押されたまま")
            .And.Contain("右ボタン");
    }
}
