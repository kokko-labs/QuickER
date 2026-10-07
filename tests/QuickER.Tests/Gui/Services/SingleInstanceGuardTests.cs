using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.RegularExpressions;
using AwesomeAssertions;
using QuickER.Services;
using QuickER.Tests.Resources;
using QuickER.Tests.TestDoubles;

namespace QuickER.Tests.Gui.Services;

/// <summary>
/// 単一インスタンス制御（<see cref="SingleInstanceGuard"/>）と、再起動の直前に座を手放す配線を検証するテストクラス。
/// </summary>
/// <remarks>
/// 本物のアプリの名前を使うと、開発機で動いている QuickER と干渉するため、ケースごとに一意の名前を使う。
/// 名前付きオブジェクトはプロセス内でも名前で開き直せるので、2 つ目の起動は同じプロセスの 2 回目の取得で再現できる。
/// </remarks>
public sealed class SingleInstanceGuardTests
{
    /// <summary>ケースごとに一意な (Mutex 名, イベント名) を作る</summary>
    private static (string Mutex, string Event) UniqueNames()
    {
        var id = Guid.NewGuid().ToString("N");
        return (
            $@"Local\QuickER.Tests.SingleInstance.{id}",
            $@"Local\QuickER.Tests.SingleInstance.{id}.Activate"
        );
    }

    /// <summary>1 つ目は座を取り、2 つ目は座を取らずに既存インスタンスへアクティブ化を頼むことを検証する</summary>
    [Fact(DisplayName = "2 つ目の起動は座を取らず、既存インスタンスへアクティブ化を頼む")]
    public void SecondAcquire_SignalsExistingInstance()
    {
        var (mutexName, eventName) = UniqueNames();
        using var first = SingleInstanceGuard.TryAcquire(
            mutexName,
            eventName,
            () => { },
            out var firstOutcome
        );
        firstOutcome.Should().Be(SingleInstanceOutcome.Acquired);
        first.Should().NotBeNull();

        using var activated = new ManualResetEventSlim();
        first!.ListenForActivation(activated.Set);

        var second = SingleInstanceGuard.TryAcquire(
            mutexName,
            eventName,
            () => { },
            out var secondOutcome
        );

        second.Should().BeNull();
        secondOutcome.Should().Be(SingleInstanceOutcome.AlreadyRunning);
        activated
            .Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken)
            .Should()
            .BeTrue("既存インスタンスへアクティブ化が届く");
    }

    /// <summary>前面化の権利を、アクティブ化を頼む（イベントを立てる）より前に渡すことを検証する</summary>
    /// <remarks>
    /// 後に渡すと、既存インスタンスの Activate() が権利を受け取る前に走り、前面ロックで止められて点滅に留まる
    /// </remarks>
    [Fact(DisplayName = "前面化の権利はアクティブ化を頼む前に渡す")]
    public void SecondAcquire_AllowsForegroundBeforeSignaling()
    {
        var (mutexName, eventName) = UniqueNames();
        using var first = SingleInstanceGuard.TryAcquire(mutexName, eventName, () => { }, out _);
        using var observer = EventWaitHandle.OpenExisting(eventName);
        bool? signaledWhenAllowed = null;

        SingleInstanceGuard.TryAcquire(
            mutexName,
            eventName,
            () => signaledWhenAllowed = observer.WaitOne(0),
            out _
        );

        signaledWhenAllowed.Should().BeFalse("権利を渡した時点ではまだアクティブ化を頼んでいない");
        observer.WaitOne(0).Should().BeTrue("その後にアクティブ化を頼む");
    }

    /// <summary>前面化の権利を渡す処理が失敗しても、アクティブ化は頼み、起動の流れを止めないことを検証する</summary>
    [Fact(DisplayName = "前面化の権利を渡せなくてもアクティブ化は頼む")]
    public void AllowForegroundFailure_DoesNotStopActivation()
    {
        var (mutexName, eventName) = UniqueNames();
        using var first = SingleInstanceGuard.TryAcquire(mutexName, eventName, () => { }, out _);
        using var observer = EventWaitHandle.OpenExisting(eventName);

        var act = () =>
            SingleInstanceGuard.TryAcquire(
                mutexName,
                eventName,
                () => throw new InvalidOperationException("権利を渡せない"),
                out var outcome
            );

        act.Should().NotThrow();
        observer.WaitOne(0).Should().BeTrue();
    }

    /// <summary>本物の前面化の権利の宣言（user32）が呼べることを検証する（失敗は false で返り、例外にならない）</summary>
    [Fact(DisplayName = "AllowSetForegroundWindow の宣言は例外なく呼べる")]
    public void NativeAllowSetForegroundWindow_IsCallable()
    {
        var act = () =>
            NativeMethods.AllowSetForegroundWindow(NativeMethods.AllowSetForegroundAnyProcess);

        act.Should().NotThrow("宣言（エントリポイント・引数の型）が正しい");
    }

    /// <summary>
    /// 権限の違うインスタンスが作った Mutex を開けないとき、例外で落ちずに「開けない」を返すことを検証する
    /// </summary>
    /// <remarks>
    /// 管理者として起動したインスタンスの名前付きオブジェクトは、既定の DACL が通常の利用者に開くことを許さない。
    /// ここでは現在の利用者に権限を与えない DACL の Mutex を作って、管理者権限なしで同じ状況を再現する。
    /// </remarks>
    [Fact(DisplayName = "権限の違うインスタンスの Mutex を開けないときは「開けない」を返す")]
    public void InaccessibleMutex_ReturnsInaccessible()
    {
        var (mutexName, eventName) = UniqueNames();
        var security = new MutexSecurity();
        security.AddAccessRule(
            new MutexAccessRule(
                new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
                MutexRights.FullControl,
                AccessControlType.Allow
            )
        );
        using var elevated = MutexAcl.Create(false, mutexName, out var created, security);
        created.Should().BeTrue();

        SingleInstanceGuard? guard = null;
        var outcome = SingleInstanceOutcome.Acquired;
        var act = () =>
            guard = SingleInstanceGuard.TryAcquire(mutexName, eventName, () => { }, out outcome);

        act.Should().NotThrow("WPF の初期化前に例外で落とさない");
        guard.Should().BeNull();
        outcome.Should().Be(SingleInstanceOutcome.Inaccessible);
    }

    /// <summary>座を手放す処理を登録しておくと、終了処理の途中の再起動で新しいインスタンスが座を取れることを検証する</summary>
    /// <remarks>
    /// 再起動はこのプロセスの終了処理の途中（Application.Exit）で行うので、手放さないと新しいインスタンスが
    /// 「既に起動中」と判断して黙って終わる（ここでは同じプロセスの 2 回目の取得で再現する）
    /// </remarks>
    [Fact(DisplayName = "再起動の直前に座を手放すので、新しいインスタンスが座を取れる")]
    public void Relaunch_AfterReleasingSingleInstance_CanAcquire()
    {
        var (mutexName, eventName) = UniqueNames();
        var current = SingleInstanceGuard.TryAcquire(mutexName, eventName, () => { }, out _);
        current.Should().NotBeNull();
        var actions = new PostExitActions();
        actions.ReleaseSingleInstanceBeforeRelaunch(current!.Dispose);
        SingleInstanceGuard? relaunched = null;
        var relaunchedOutcome = SingleInstanceOutcome.AlreadyRunning;
        actions.RelaunchWith(() =>
            relaunched = SingleInstanceGuard.TryAcquire(
                mutexName,
                eventName,
                () => { },
                out relaunchedOutcome
            )
        );
        actions.RequestRelaunch();

        try
        {
            actions.Run(new RecordingShutdownFailureReporter());

            relaunchedOutcome.Should().Be(SingleInstanceOutcome.Acquired);
            relaunched.Should().NotBeNull();
        }
        finally
        {
            relaunched?.Dispose();

            // using の終わりの 2 回目の破棄に当たる（冪等）
            current.Dispose();
        }
    }

    /// <summary>Program が座の手放しを登録し、「開けない」を案内することをソース上で固定する</summary>
    /// <remarks>Program はエントリポイントでテストから実行できないため、配線だけをソースで見張る</remarks>
    [Fact(DisplayName = "Program は再起動の前の座の手放しを登録し、開けないときは案内する")]
    public void Program_RegistersReleaseAndReportsInaccessible()
    {
        var source = File.ReadAllText(
            Path.Combine(NeutralResxFiles.FindRepositoryRoot(), "src", "QuickER.Gui", "Program.cs")
        );

        source.Should().Contain("ReleaseSingleInstanceBeforeRelaunch(singleInstance.Dispose)");
        source
            .Should()
            .Contain(
                "RelaunchWith(WpfApplicationRestartService.StartNewInstance)",
                "更新の適用に失敗したときの代わりの再起動も、起動時に登録した起動処理を使う"
            );
        source.Should().Contain("outcome == SingleInstanceOutcome.Inaccessible");
        Regex
            .Matches(source, @"SingleInstanceGuard\.TryAcquire\(\)")
            .Should()
            .BeEmpty("結果を受け取らない形へ戻っていないこと");
    }
}
