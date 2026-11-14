using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Threading;

namespace QuickER.Tests.TestSupport;

/// <summary>
/// テストが合成の入力を流す間、利用者の実際のマウスのボタンを混ぜない（押されている間は待ち、キーの状態の表の
/// ボタンを「離れている」に固定する）
/// </summary>
/// <remarks>
/// <para>
/// WPF のコントロール（<c>ButtonBase</c> の Space の解放・<c>DataGrid</c> のドラッグ選択・キャプチャを取った瞬間の擬似の移動）は、
/// マウスのボタンをスレッドのキーの状態の表から読む。テストが <c>InputManager.ProcessInput</c> や <c>RaiseEvent</c> で流す入力は
/// 擬似でも、ボタンの状態は実際のマウスのものなので、全件の実行中に利用者がほかのアプリでボタンを押すと結果が変わる
/// （<see cref="GuiFailureDiagnostics"/> の記録で、<c>Mouse.LeftButton=Pressed</c>・<c>GetAsyncKeyState(VK_LBUTTON)</c> の最上位ビットが
/// 立っていたのを観測した＝製品の不具合ではない）。人が触らない状態（表のボタンが離れている）が、合成の入力を流すテストの前提。
/// </para>
/// <para>
/// 合成の入力を流すテストは <see cref="Isolate"/> のスコープを持ち、入力を流す直前に <see cref="Isolation.WaitAndPin"/>
/// （押されている間は待ってから固定）か <see cref="Isolation.Pin"/>（固定だけ。押下と解放の間などメッセージを処理させたくない所）を呼ぶ。
/// 表は実際のボタンが変わると Windows が同期し直すので、押されている間に書いても漏れる（待つのはそのため）。
/// 入れ忘れはソースの規則 <c>synthetic-input-mouse-isolation</c> が断る。
/// </para>
/// <para>
/// 待ち方：押されている間は待ち続け、<see cref="ReportInterval"/> ごとに経過をテストの出力と失敗の記録の時系列へ書く。
/// 止まったままを防ぐ安全の上限（<see cref="SafetyLimit"/>）を過ぎたら、押されているボタンを名指しして失敗させる（スキップしない）。
/// CI（環境変数 <c>CI</c> か <c>GITHUB_ACTIONS</c> が <c>true</c>）では人がボタンを押さないので待たず、押されていればその場で失敗させる。
/// 押されていなければ 1 回読んで進むので、人が触らない回の結果と時間は変わらない。
/// </para>
/// </remarks>
internal static class PhysicalMouseButtons
{
    /// <summary>押されている間に待つ安全の上限（ボタンの固着・リモートデスクトップの状態の残りで止まったままにしないためだけのもの）</summary>
    public static readonly TimeSpan SafetyLimit = TimeSpan.FromMinutes(5);

    /// <summary>待っている間に経過を書く間隔</summary>
    public static readonly TimeSpan ReportInterval = TimeSpan.FromSeconds(5);

    /// <summary>状態を見直す間隔</summary>
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(30);

    /// <summary>待ちの経過を測る単調な時計</summary>
    private static readonly Stopwatch Clock = Stopwatch.StartNew();

    private const int VkLButton = 0x01;
    private const int VkRButton = 0x02;

    /// <summary>
    /// 合成の入力を流す間のスコープを作る（作るときにこのスレッドの表のボタンの値を取り、破棄で戻す）
    /// </summary>
    public static Isolation Isolate() => new();

    /// <summary>
    /// 実際のマウスの左右のボタンがどちらも離されるまで待つ（待つ間は呼び出し元のスレッドのメッセージを処理する）。
    /// 安全の上限を過ぎても押されたままなら（CI ではその場で）テストを失敗させる
    /// </summary>
    /// <returns>待った時間（押されていなければ <see cref="TimeSpan.Zero"/>）</returns>
    public static TimeSpan WaitUntilReleased() =>
        WaitUntilReleased(
            ReadPressedButton,
            WaitPolicy.ForEnvironment(Environment.GetEnvironmentVariable),
            () => Clock.Elapsed,
            () =>
            {
                Thread.Sleep(PollInterval);
                WpfApplicationTestSupport.DoEvents();
            },
            Report
        );

    /// <summary>
    /// 押されているボタンの名前を返す関数が null を返すまで、<paramref name="wait"/> を挟んで見直す
    /// （単体テストで状態・時計・待ち方・報告先を差し替えるための形）
    /// </summary>
    /// <param name="pressedButton">押されているボタンの名前（どれも離されていれば null）</param>
    /// <param name="policy">待つ上限と経過を書く間隔</param>
    /// <param name="clock">単調な時計（差を経過として読む）</param>
    /// <param name="wait">見直しの間に行う待ち</param>
    /// <param name="report">経過の報告先</param>
    /// <returns>待った時間（押されていなければ <see cref="TimeSpan.Zero"/>）</returns>
    /// <exception cref="Xunit.Sdk.FailException">上限を過ぎても押されたまま（上限 0 ならその場で）</exception>
    internal static TimeSpan WaitUntilReleased(
        Func<string?> pressedButton,
        WaitPolicy policy,
        Func<TimeSpan> clock,
        Action wait,
        Action<string> report
    )
    {
        var start = clock();
        var nextReport = policy.ReportInterval;
        var waited = false;

        while (pressedButton() is { } button)
        {
            var elapsed = clock() - start;

            if (elapsed >= policy.Limit)
            {
                Assert.Fail(
                    policy.Limit == TimeSpan.Zero
                        ? $"実際のマウスのボタンが押されている（{button}）。CI では待たない。"
                            + "押されている間は WPF のコントロールが合成の入力を正しく受けないので、ボタンを離してから実行し直す"
                        : $"実際のマウスのボタンが押されたまま {policy.Limit.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture)} 秒を過ぎた（{button}）。"
                            + "押されている間は WPF のコントロールが合成の入力を正しく受けないので、ボタンを離してから実行し直す"
                );
            }

            if (elapsed >= nextReport)
            {
                report(
                    $"実際のマウスのボタンが押されている間は待っている（{button}・経過 {elapsed.TotalSeconds.ToString("0", CultureInfo.InvariantCulture)} 秒・"
                        + $"{policy.Limit.TotalSeconds.ToString("0", CultureInfo.InvariantCulture)} 秒で失敗）"
                );
                nextReport += policy.ReportInterval;
            }

            waited = true;
            wait();
        }

        return waited ? clock() - start : TimeSpan.Zero;
    }

    /// <summary>待ちの経過を、テストの出力と失敗の記録の時系列へ書く</summary>
    private static void Report(string text)
    {
        TestContext.Current.TestOutputHelper?.WriteLine(text);
        GuiFailureDiagnostics.Note(text);
    }

    /// <summary>実際のマウスの左右のボタンのうち押されているもの（どちらも離されていれば null）</summary>
    private static string? ReadPressedButton()
    {
        if (IsDown(VkLButton))
        {
            return "左ボタン";
        }

        return IsDown(VkRButton) ? "右ボタン" : null;
    }

    /// <summary>仮想キーが今押されているか（GetAsyncKeyState の最上位ビット）</summary>
    private static bool IsDown(int virtualKey) => (GetAsyncKeyState(virtualKey) & 0x8000) != 0;

    [DllImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern short GetAsyncKeyState(int vKey);

    /// <summary>待つ上限（0 なら待たない）と、待っている間に経過を書く間隔</summary>
    internal readonly record struct WaitPolicy(TimeSpan Limit, TimeSpan ReportInterval)
    {
        /// <summary>CI では待たず、手元では安全の上限まで待つ方針を、環境変数から作る</summary>
        /// <param name="environment">環境変数を読む関数（単体テストで差し替える）</param>
        internal static WaitPolicy ForEnvironment(Func<string, string?> environment) =>
            IsTrue(environment("CI")) || IsTrue(environment("GITHUB_ACTIONS"))
                ? new(TimeSpan.Zero, PhysicalMouseButtons.ReportInterval)
                : new(SafetyLimit, PhysicalMouseButtons.ReportInterval);

        private static bool IsTrue(string? value) =>
            string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 合成の入力を流す間のスコープ。作るときにこのスレッドの表のマウスのボタンの値を取り、破棄で戻す
    /// （例外の道でも <c>using</c> で戻す。二重の破棄では何もしない）
    /// </summary>
    internal sealed class Isolation : IDisposable
    {
        private readonly MouseButtonKeyState.Saved _saved = MouseButtonKeyState.Capture();
        private bool _disposed;

        /// <summary>
        /// 表のボタンを固定するか（既定は固定する。自己検査が、外すと利用者の押下が混ざることを確かめるためだけに false にする）
        /// </summary>
        internal bool Pins { get; set; } = true;

        /// <summary>
        /// 実際のボタンが離れるまで待つ処理（既定は <see cref="WaitUntilReleased()"/>。自己検査が、待つ回数を数えるため・
        /// 待った形を作るためだけに差し替える）
        /// </summary>
        internal Func<TimeSpan> Wait { get; set; } = WaitUntilReleased;

        /// <summary>実際に待った回数（押されていて待ちが生じた回数）</summary>
        public int WaitedCount { get; private set; }

        /// <summary>実際に待った時間の合計</summary>
        public TimeSpan Waited { get; private set; }

        /// <summary>実際のボタンが離れるまで待ってから、表のボタンを「離れている」に固定する</summary>
        public void WaitAndPin()
        {
            var waited = Wait();

            if (waited > TimeSpan.Zero)
            {
                WaitedCount++;
                Waited += waited;
            }

            Pin();
        }

        /// <summary>表のボタンを「離れている」に固定する（メッセージを処理しない）</summary>
        public void Pin()
        {
            if (Pins)
            {
                MouseButtonKeyState.Release();
            }
        }

        /// <summary>表のボタンを、スコープを作る前の値へ戻す</summary>
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _saved.Restore();
        }
    }
}
