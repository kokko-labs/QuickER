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

    /// <summary>固定の自己検査の対照を流し直す上限（1 回は 1 秒に満たない。人の手のクリックが 3 回続けて当たることは無い）</summary>
    public const int ControlAttemptLimit = 3;

    /// <summary>
    /// 固定の自己検査の対照（キーの状態の表に押下を書き、固定を外して流すと押下が WPF のコントロールに届くことを見る側）を、
    /// 途中の本物のクリックで黙って落とさずに流す（流し直しの経過をテストの出力と失敗の記録の時系列へ書く）
    /// </summary>
    /// <param name="attempt">
    /// 対照を 1 回流す（毎回、表に押下を書き直してから流し、スコープ・ハーネスを破棄する前に表の押下が残っているかを読む。
    /// 破棄は作る前の値を書き戻して、消えた跡を隠す）
    /// </param>
    /// <returns>結果（<see cref="ControlRun.Problem"/> が null なら合格。null でなければその文で失敗させる）</returns>
    /// <remarks>
    /// <para>
    /// Windows は実際のボタンが変わるとスレッドの表を実際の状態へ同期し直すので、対照の途中に本物のクリックが入ると、
    /// 書いた押下が消えて押下が届かない（自己検査が効いていないように見える）。落ちる原因そのもの（表の押下が消えた）を、
    /// 流した後の表で見分ける。ほかのアプリの上のクリックは Win32 の報告としてテストの窓に届かず、<c>GetAsyncKeyState</c> を
    /// 何点かで読むだけでは短いクリックを取りこぼすので、使わない。
    /// </para>
    /// <list type="bullet">
    ///   <item>届いた → 合格（何回目かを返す）。</item>
    ///   <item>届かず表が押下のまま → 乱れなしの失敗。流し直さず、すぐに失敗の文を返す（本物の欠陥を隠さない）。</item>
    ///   <item>届かず表が離れている → 外からの乱れ。名指しして流し直す。<paramref name="maxAttempts"/> 回とも乱れたら、
    ///     名指しの失敗の文を返す。</item>
    /// </list>
    /// <para>
    /// 見分けられない形：流している間に押して離し、終わり際にもう一度押していると、表は押下に見えるので、対照が落ちても
    /// 乱れなしの失敗になる（偽の赤の側で、本物の欠陥は隠さない）。
    /// </para>
    /// </remarks>
    internal static ControlRun RunPressedTableControl(
        Func<ControlAttempt> attempt,
        int maxAttempts,
        Action<string> report
    )
    {
        var disturbed = 0;

        for (var number = 1; number <= maxAttempts; number++)
        {
            var result = attempt();

            if (result.PressReached)
            {
                return new(true, number, disturbed, null);
            }

            if (result.TableKeptPress)
            {
                return new(
                    false,
                    number,
                    disturbed,
                    "表に押下が残っていたのに、固定を外しても押下が WPF のコントロールに届かなかった"
                        + "（外からの乱れなし＝固定の自己検査が効いていない）"
                );
            }

            disturbed++;
            report(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"対照の途中で実際のマウスのボタンの状態が変わり、キーの状態の表に書いた押下が消えた（外からの乱れ）。対照を流し直す（{number} / {maxAttempts} 回目）"
                )
            );
        }

        return new(
            false,
            maxAttempts,
            disturbed,
            string.Create(
                CultureInfo.InvariantCulture,
                $"対照を {maxAttempts} 回流したが、どの回も途中で実際のマウスのボタンの状態が変わり、表に書いた押下が消えた（外からの乱れ。人が触らない状態で流し直す）"
            )
        );
    }

    /// <summary>固定の自己検査の対照の流し直しを、テストの出力と失敗の記録の時系列へ書く（待ちの経過と同じ先）</summary>
    internal static void ReportControlRetry(string text) => Report(text);

    /// <summary>固定の自己検査の対照を 1 回流した結果</summary>
    /// <param name="PressReached">表に書いた押下が WPF のコントロールに届いたか（対照が期待どおりか）</param>
    /// <param name="TableKeptPress">流した後（破棄の前）に、表の左ボタンがまだ押下か</param>
    internal readonly record struct ControlAttempt(bool PressReached, bool TableKeptPress);

    /// <summary>固定の自己検査の対照を流した結果</summary>
    /// <param name="PressReached">押下が届いたか（合格）</param>
    /// <param name="Attempts">流した回数</param>
    /// <param name="Disturbed">外からの乱れで流し直した回数</param>
    /// <param name="Problem">失敗の文（合格なら null）</param>
    internal readonly record struct ControlRun(
        bool PressReached,
        int Attempts,
        int Disturbed,
        string? Problem
    );

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
