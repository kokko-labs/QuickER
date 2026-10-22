using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace QuickER.Tests.TestSupport;

/// <summary>
/// 実際のマウスのボタン（左・右）が押されている間、キー入力を流すのを待つ
/// </summary>
/// <remarks>
/// <para>
/// WPF の <c>ButtonBase</c> は Space の解放で Click を発火し（<c>ToggleButton</c> はそこで切り替わる）、マウスのキャプチャを外すが、
/// それは左右のボタンがどちらも離されているときだけ（<c>ButtonBase.OnKeyUp</c> がマウスのボタンの状態を見る）。
/// テストが <c>InputManager.ProcessInput</c> で流す Space は擬似の入力でも、ボタンの状態は実際のマウスのものなので、
/// 利用者がテストの実行中にボタンを押したままにしていると Click が起きず、Space で押せることを見るテストが落ちる
/// （<see cref="GuiFailureDiagnostics"/> の記録で、<c>Mouse.LeftButton=Pressed</c>・<c>GetAsyncKeyState(VK_LBUTTON)</c> の最上位ビットが立ち、
/// キャプチャがボタンに残っていたのを観測した＝製品の不具合ではない）。
/// </para>
/// <para>
/// Enter は押下の時点で Click するのでボタンの状態を見ない。待つのは Space の押下を流す前だけでよい
/// （押下と解放の間にメッセージを処理させないため、解放の前には待たない）。
/// </para>
/// </remarks>
internal static class PhysicalMouseButtons
{
    /// <summary>待つ上限の既定値</summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);

    /// <summary>状態を見直す間隔</summary>
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(30);

    private const int VkLButton = 0x01;
    private const int VkRButton = 0x02;

    /// <summary>
    /// 実際のマウスの左右のボタンがどちらも離されるまで待つ（待つ間は呼び出し元のスレッドのメッセージを処理する）。
    /// 上限を過ぎても押されたままならテストを失敗させる（スキップしない）
    /// </summary>
    public static void WaitUntilReleased() =>
        WaitUntilReleased(
            ReadPressedButton,
            DefaultTimeout,
            () =>
            {
                Thread.Sleep(PollInterval);
                WpfApplicationTestSupport.DoEvents();
            }
        );

    /// <summary>
    /// 押されているボタンの名前を返す関数が null を返すまで、<paramref name="wait"/> を挟んで見直す
    /// （単体テストで状態と待ち方を差し替えるための形）
    /// </summary>
    /// <param name="pressedButton">押されているボタンの名前（どれも離されていれば null）</param>
    /// <param name="timeout">待つ上限</param>
    /// <param name="wait">見直しの間に行う待ち</param>
    /// <exception cref="Xunit.Sdk.FailException">上限を過ぎても押されたまま</exception>
    internal static void WaitUntilReleased(
        Func<string?> pressedButton,
        TimeSpan timeout,
        Action wait
    )
    {
        var watch = Stopwatch.StartNew();

        while (pressedButton() is { } button)
        {
            if (watch.Elapsed >= timeout)
            {
                Assert.Fail(
                    $"実際のマウスのボタンが押されたまま {timeout.TotalSeconds:0.###} 秒を過ぎた（{button}）。"
                        + "押されている間は WPF のボタンが Space で Click しないので、ボタンを離してから実行し直す"
                );
            }

            wait();
        }
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
}
