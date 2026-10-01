using QuickER.Gui.Abstractions;

namespace QuickER.Services;

/// <summary>
/// アプリの正常終了の最後（<c>Application.Exit</c>）に行う処理＝更新の適用と再起動を 1 か所で決めるクラス。
/// </summary>
/// <remarks>
/// <para>
/// どちらも<b>通常の終了経路を通った後</b>でしか行わない。メインウィンドウの <c>Closing</c>
/// （自動保存・機能モジュールの終了連鎖）が済んでから走るので、編集内容は復旧用の自動保存に残り、
/// AI の子プロセスも止まっている。終了がキャンセルされた場合と、クラッシュ経路
/// （<see cref="Environment.Exit(int)"/>）では <see cref="Run"/> が呼ばれないため、何も起きない。
/// 登録はその次の正常終了まで残る。
/// </para>
/// <para>
/// 更新の適用と再起動の要求が両方あるときは、<b>更新プログラムに再起動まで任せ、自分では起動しない</b>。
/// 更新プログラムはこのプロセスの終了を待ってから適用し、適用の前にインストール先で動いている
/// QuickER を止めるため、こちらで先に新しいインスタンスを起動すると、それが止められて再起動が失われる。
/// 登録の順序（どちらが先に要求されたか）には依らない。
/// </para>
/// </remarks>
public sealed class PostExitActions
{
    /// <summary>終了の最後に呼ぶ更新の適用（引数＝適用後に再起動するか。未要求なら null）</summary>
    private Action<bool>? _applyUpdate;

    /// <summary>更新の適用後に再起動するか</summary>
    private bool _restartAfterUpdate;

    /// <summary>終了の最後に呼ぶ再起動（未要求なら null）</summary>
    private Action? _relaunch;

    /// <summary>正常終了の最後に更新を適用するよう登録する</summary>
    /// <param name="applyOnExit">
    /// 更新を適用する処理（引数＝適用後に再起動するか）。このプロセスの終了を待って適用する
    /// 更新プログラムを起動するだけで、呼び出しから戻ること
    /// </param>
    /// <param name="restart">適用後に QuickER を再起動するか</param>
    /// <remarks>重ねて登録したときは後の処理を採り、再起動はどちらかが求めていれば行う</remarks>
    public void ScheduleUpdateApply(Action<bool> applyOnExit, bool restart)
    {
        _applyUpdate = applyOnExit;
        _restartAfterUpdate |= restart;
    }

    /// <summary>正常終了の最後に QuickER を起動し直すよう登録する</summary>
    /// <param name="relaunch">新しいインスタンスを起動する処理</param>
    public void ScheduleRelaunch(Action relaunch)
    {
        _relaunch = relaunch;
    }

    /// <summary>DI コンテナの破棄から登録された処理の実行までの、正常終了の最後の連鎖を行う</summary>
    /// <param name="reporter">失敗の記録先（各段を終了の連鎖の 1 段として扱う）</param>
    /// <param name="disposeServices">DI コンテナを破棄する処理</param>
    /// <remarks>
    /// 破棄も 1 段として受け止める。破棄が投げても後段（更新の適用・再起動）へ必ず届かせるため。
    /// </remarks>
    public void CompleteExit(IShutdownFailureReporter reporter, Action disposeServices)
    {
        ShutdownSteps.Run(reporter, "App.DisposeServices", disposeServices);
        Run(reporter);
    }

    /// <summary>登録された処理を実行する（正常終了の最後に 1 回だけ呼ぶ）</summary>
    /// <param name="reporter">失敗の記録先（終了の連鎖の 1 段として扱う）</param>
    /// <remarks>
    /// 更新の適用が失敗した（更新プログラムを起動できなかった）ときは、再起動の要求があれば自分で起動する。
    /// 適用が始まらない以上、起動したインスタンスが止められることもなく、任せた再起動が失われるだけになるため。
    /// </remarks>
    public void Run(IShutdownFailureReporter reporter)
    {
        var applyUpdate = _applyUpdate;
        var relaunch = _relaunch;
        _applyUpdate = null;
        _relaunch = null;

        if (applyUpdate is not null)
        {
            // 再起動の要求があれば更新プログラムに任せる（自分で起動したインスタンスは適用の前に止められる）
            var restart = _restartAfterUpdate || relaunch is not null;
            var applied = false;

            ShutdownSteps.Run(
                reporter,
                "Update.ApplyOnExit",
                () =>
                {
                    applyUpdate(restart);
                    applied = true;
                }
            );

            if (applied)
            {
                return;
            }
        }

        if (relaunch is not null)
        {
            ShutdownSteps.Run(reporter, "App.Relaunch", relaunch);
        }
    }
}
