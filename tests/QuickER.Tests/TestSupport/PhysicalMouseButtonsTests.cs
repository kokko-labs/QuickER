using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AwesomeAssertions;
using Xunit.Sdk;
using static QuickER.Tests.TestSupport.WpfApplicationTestSupport;

namespace QuickER.Tests.TestSupport;

/// <summary>
/// <see cref="PhysicalMouseButtons"/> の待ち（押されている間は待って経過を書き、離されたら進む・安全の上限と CI で失敗する）を、
/// ボタンの状態・時計・待ち方・報告先を差し替えて検証し、合成の入力の間のスコープ（<see cref="PhysicalMouseButtons.Isolate"/>）が
/// キーの状態の表のボタンを固定して戻すことを、本物の WPF のボタンで検証するテストクラス
/// </summary>
/// <remarks>
/// スコープの自己検査はキー入力とフォーカスに左右されるので、並列に走らせない（<see cref="WindowActivationTestCollection"/>）。
/// </remarks>
[Collection(WindowActivationTestCollection.Name)]
public sealed class PhysicalMouseButtonsTests
{
    /// <summary>手元の方針（安全の上限 5 分・5 秒ごとに経過を書く）</summary>
    private static readonly PhysicalMouseButtons.WaitPolicy Local =
        PhysicalMouseButtons.WaitPolicy.ForEnvironment(_ => null);

    /// <summary>見直すたびに 1 秒進む偽の時計で待つ（実時間では待たない）</summary>
    private sealed class FakeClock
    {
        public TimeSpan Now { get; private set; }

        public int Waits { get; private set; }

        public void Wait()
        {
            Waits++;
            Now += TimeSpan.FromSeconds(1);
        }
    }

    [Fact(DisplayName = "ボタンが押されている間は待ち、離されたら進んで待った時間を返す")]
    public void WaitsWhilePressed_ProceedsOnceReleased()
    {
        var polls = 0;
        var clock = new FakeClock();

        var waited = PhysicalMouseButtons.WaitUntilReleased(
            () => ++polls <= 3 ? "左ボタン" : null,
            Local,
            () => clock.Now,
            clock.Wait,
            _ => { }
        );

        polls.Should().Be(4, "押されていた 3 回と、離されたのを見た 1 回");
        clock.Waits.Should().Be(3);
        waited.Should().Be(TimeSpan.FromSeconds(3));
    }

    [Fact(DisplayName = "離されていれば待たずに進み、待った時間は 0")]
    public void ProceedsImmediately_WhenReleased()
    {
        var clock = new FakeClock();

        var waited = PhysicalMouseButtons.WaitUntilReleased(
            () => null,
            Local,
            () => clock.Now,
            clock.Wait,
            _ => { }
        );

        clock
            .Waits.Should()
            .Be(0, "待たないので、離されている通常の実行では結果も時間も変わらない");
        waited.Should().Be(TimeSpan.Zero);
    }

    /// <summary>
    /// 全件の最中に利用者がボタンを押し続けても（以前の上限 10 秒を超えて）、離されれば失敗せずに進み、待っている間は
    /// 間隔ごとに経過を報告する（黙って待たない）
    /// </summary>
    [Fact(DisplayName = "安全の上限より短い長押しは失敗せずに進み、間隔ごとに経過を報告する")]
    public void LongPressShorterThanSafetyLimit_ProceedsAndReportsEveryInterval()
    {
        var polls = 0;
        var clock = new FakeClock();
        var reports = new List<string>();

        var waited = PhysicalMouseButtons.WaitUntilReleased(
            () => ++polls <= 20 ? "左ボタン" : null,
            Local,
            () => clock.Now,
            clock.Wait,
            reports.Add
        );

        waited.Should().Be(TimeSpan.FromSeconds(20));
        reports
            .Should()
            .Equal(
                "実際のマウスのボタンが押されている間は待っている（左ボタン・経過 5 秒・300 秒で失敗）",
                "実際のマウスのボタンが押されている間は待っている（左ボタン・経過 10 秒・300 秒で失敗）",
                "実際のマウスのボタンが押されている間は待っている（左ボタン・経過 15 秒・300 秒で失敗）"
            );
    }

    [Fact(
        DisplayName = "安全の上限を過ぎても押されたままなら、押されているボタンを名指しして失敗する"
    )]
    public void FailsAtSafetyLimit_NamingTheButton()
    {
        var clock = new FakeClock();

        var act = () =>
            PhysicalMouseButtons.WaitUntilReleased(
                () => "右ボタン",
                Local,
                () => clock.Now,
                clock.Wait,
                _ => { }
            );

        act.Should()
            .Throw<FailException>()
            .Which.Message.Should()
            .Contain("実際のマウスのボタンが押されたまま 300 秒を過ぎた")
            .And.Contain("右ボタン");
        clock.Now.Should().Be(PhysicalMouseButtons.SafetyLimit, "上限までは待つ");
    }

    [Theory(DisplayName = "CI では待たず、押されていればその場で名指しして失敗する")]
    [InlineData("CI")]
    [InlineData("GITHUB_ACTIONS")]
    public void Ci_DoesNotWait_AndFailsAtOnce(string variable)
    {
        var policy = PhysicalMouseButtons.WaitPolicy.ForEnvironment(name =>
            name == variable ? "true" : null
        );
        var clock = new FakeClock();

        var act = () =>
            PhysicalMouseButtons.WaitUntilReleased(
                () => "左ボタン",
                policy,
                () => clock.Now,
                clock.Wait,
                _ => { }
            );

        policy.Limit.Should().Be(TimeSpan.Zero);
        act.Should()
            .Throw<FailException>()
            .Which.Message.Should()
            .Contain("CI では待たない")
            .And.Contain("左ボタン");
        clock.Waits.Should().Be(0, "CI では 1 回も待たない");
    }

    [Theory(
        DisplayName = "CI の印が無いか true でなければ、手元の方針（安全の上限まで待つ）になる"
    )]
    [InlineData(null)]
    [InlineData("false")]
    [InlineData("")]
    public void NotCi_UsesSafetyLimit(string? value)
    {
        var policy = PhysicalMouseButtons.WaitPolicy.ForEnvironment(_ => value);

        policy.Limit.Should().Be(PhysicalMouseButtons.SafetyLimit);
        policy.ReportInterval.Should().Be(PhysicalMouseButtons.ReportInterval);
    }

    [Fact(
        DisplayName = "スコープの待ちは、実際に待った回数と時間だけを数え、待った後に表を固定する"
    )]
    public void Isolation_CountsOnlyActualWaits()
    {
        const int Left = 0x01;

        RunSta(() =>
        {
            var original = MouseButtonKeyState.Capture();

            try
            {
                var results = new Queue<TimeSpan>([TimeSpan.Zero, TimeSpan.FromSeconds(2)]);

                using var isolation = PhysicalMouseButtons.Isolate();
                isolation.Wait = results.Dequeue;

                MouseButtonKeyState.SetForTests(Left, true);
                isolation.WaitAndPin();
                MouseButtonKeyState.IsDown(Left).Should().BeFalse("待った後に固定する");

                MouseButtonKeyState.SetForTests(Left, true);
                isolation.WaitAndPin();

                isolation.WaitedCount.Should().Be(1, "待たなかった回（0）は数えない");
                isolation.Waited.Should().Be(TimeSpan.FromSeconds(2));
                MouseButtonKeyState.IsDown(Left).Should().BeFalse();
            }
            finally
            {
                original.Restore();
            }
        });
    }

    /// <summary>
    /// スコープの固定の自己検査：キーの状態の表の左ボタンを押下にしてから、固定して Space の押下と解放を流すと、WPF の
    /// ボタンは Click しチェックボックスは切り替わる。固定を外すと Click も切り替えも起きない（押下が WPF に届く形を作れていて、
    /// 自己検査が効いていることの確かめ）。破棄で表のボタンを作る前の値へ戻し（成功・例外の 2 つの道）、ほかのキーに触らない
    /// </summary>
    [Fact(
        DisplayName = "スコープは表のボタンを離れているに固定して Space で押せるようにし、破棄で元へ戻す"
    )]
    public void Isolation_PinsButtonsForSyntheticSpaceAndRestores()
    {
        const int Left = 0x01;
        const int Marker = 0x87; // VK_F24：ボタンでないキー（触らないことを見る印）

        RunSta(() =>
        {
            var original = MouseButtonKeyState.Capture();
            var markerOriginal = MouseButtonKeyState.IsDown(Marker);
            var button = new Button { Content = "Button" };
            var checkBox = new CheckBox { Content = "CheckBox" };
            var panel = new StackPanel();
            panel.Children.Add(button);
            panel.Children.Add(checkBox);
            var window = new Window
            {
                Content = panel,
                Width = 200,
                Height = 200,
                ShowInTaskbar = false,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -10000,
                Top = -10000,
            };
            var clicks = 0;
            button.Click += (_, _) => clicks++;
            GuiFailureDiagnostics.Track(window);

            try
            {
                window.Show();
                DoEvents();
                MouseButtonKeyState.SetForTests(Marker, true);

                // 固定あり：押下が表にあっても、固定すれば Space で押せる
                MouseButtonKeyState.SetForTests(Left, true);

                using (var isolation = PhysicalMouseButtons.Isolate())
                {
                    PressSpace(isolation, button);
                    PressSpace(isolation, checkBox);
                }

                clicks.Should().Be(1, "固定すれば、ボタンは Space の解放で Click する");
                checkBox
                    .IsChecked.Should()
                    .BeTrue("固定すれば、チェックボックスは Space で切り替わる");
                MouseButtonKeyState.IsDown(Left).Should().BeTrue("破棄で作る前の値（押下）へ戻す");
                MouseButtonKeyState.IsDown(Marker).Should().BeTrue("ボタンでないキーには触らない");

                // 固定なし：表の押下が WPF に届き、Click も切り替えも起きない
                using (var isolation = PhysicalMouseButtons.Isolate())
                {
                    isolation.Pins = false;
                    PressSpace(isolation, button);
                    PressSpace(isolation, checkBox);
                }

                clicks.Should().Be(1, "固定を外すと押下が WPF に届く（自己検査が効いている）");
                checkBox.IsChecked.Should().BeTrue("固定を外すと切り替わらない");
                Mouse.Capture(null);

                // 例外の道：使う途中で投げても破棄で戻す
                var act = () =>
                {
                    using var isolation = PhysicalMouseButtons.Isolate();
                    isolation.Pin();
                    throw new InvalidOperationException("途中で投げる");
                };

                act.Should().Throw<InvalidOperationException>();
                MouseButtonKeyState.IsDown(Left).Should().BeTrue("例外の後も戻す");
            }
            finally
            {
                window.Close();
                original.Restore();
                MouseButtonKeyState.SetForTests(Marker, markerOriginal);
                DoEvents();
            }
        });
    }

    /// <summary>要素へフォーカスを移し、スコープの固定の後に Space の押下と解放を続けて流す（間にメッセージを処理させない）</summary>
    private static void PressSpace(PhysicalMouseButtons.Isolation isolation, UIElement element)
    {
        element.Focus();
        DoEvents();
        var source = PresentationSource.FromVisual(element)!;
        isolation.Wait = () => TimeSpan.Zero;
        isolation.WaitAndPin();
        SendKey(source, down: true);
        isolation.Pin();
        SendKey(source, down: false);
        DoEvents();
    }

    private static void SendKey(PresentationSource source, bool down) =>
        InputManager.Current.ProcessInput(
            new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, Key.Space)
            {
                RoutedEvent = down ? Keyboard.PreviewKeyDownEvent : Keyboard.PreviewKeyUpEvent,
            }
        );
}
