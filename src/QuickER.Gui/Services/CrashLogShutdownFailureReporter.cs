using QuickER.Gui.Abstractions;

namespace QuickER.Services;

/// <summary>
/// アプリ終了の連鎖で受け止めた失敗を、<c>%LOCALAPPDATA%\QuickER\shutdown-*.log</c> へ記録する
/// <see cref="IShutdownFailureReporter"/> の既定実装。
/// </summary>
/// <remarks>
/// <para>
/// 本文の形式はクラッシュログと共有する（<see cref="CrashHandlingService.FormatDetails"/>）が、
/// ファイル名の接頭辞だけを分ける。アプリは落ちていないのに <c>crash-*.log</c> が増えると
/// 「落ちた」と読み違えるため。
/// </para>
/// <para>
/// <b>このクラスは決して例外を投げない。</b><see cref="CrashHandlingService.WriteCrashLog"/> は
/// 自前で握り潰すが、契約が変わっても終了の連鎖を止めないよう二重に守る。
/// </para>
/// </remarks>
public sealed class CrashLogShutdownFailureReporter : IShutdownFailureReporter
{
    /// <summary>ログの保存先フォルダ（テスト隔離用。null なら <c>%LOCALAPPDATA%\QuickER</c>）</summary>
    private readonly string? _logBaseDirOverride;

    /// <summary>既定の保存先（<c>%LOCALAPPDATA%\QuickER</c>）へ記録する記録器を生成する</summary>
    /// <param name="logBaseDirOverride">保存先フォルダ（テスト隔離用。null なら既定フォルダ）</param>
    public CrashLogShutdownFailureReporter(string? logBaseDirOverride = null)
    {
        _logBaseDirOverride = logBaseDirOverride;
    }

    /// <inheritdoc />
    /// <remarks>
    /// 段の名前は <see cref="CrashHandlingService.FormatDetails"/> が最初に出す行へ載せるため、
    /// 元の例外を内側に持つ例外で包んで渡す（本文の先頭を見れば「どの段で失敗したか」が分かる）。
    /// </remarks>
    public void Report(string step, Exception exception)
    {
        try
        {
            CrashHandlingService.WriteCrashLog(
                new ShutdownStepFailedException(step, exception),
                CrashHandlingService.ResolveAppVersion(),
                _logBaseDirOverride,
                CrashHandlingService.ShutdownLogPrefix
            );
        }
        catch
        {
            // 記録できなくても終了の連鎖は続ける（＝ここから例外を出さないことがこのクラスの契約）
        }
    }
}
