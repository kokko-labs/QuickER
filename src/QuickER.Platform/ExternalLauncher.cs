using System.Diagnostics;

namespace QuickER.Platform;

/// <summary>外部のプロセス・URL・フォルダを開く入口の正本</summary>
/// <remarks>
/// <para>
/// QuickER が自分でプロセスを作る経路は、すべてここを通す（<c>src/</c> の素の <c>Process.Start</c>・<c>new Process</c>・
/// <c>UseShellExecute = true</c> は走査で禁じている）。入口ごとに起動を書くと、フルパスの解決・<c>.cmd</c> 経由の引数の扱い・
/// URL のスキームの制限が入口ごとに食い違う。
/// </para>
/// <list type="bullet">
/// <item><see cref="OpenUrl"/>：既定のブラウザで開く。<see cref="UrlPolicy"/> を通らない URL は開かない</item>
/// <item><see cref="OpenFolder"/>：エクスプローラで開く。フルパスで、在るフォルダだけ</item>
/// <item><see cref="StartExecutable"/>：実行ファイルをシェル経由で起動する（自分自身の再起動）。フルパスだけ</item>
/// <item><see cref="CreateProcess"/>：標準入出力をつなぐ子プロセス（AI の CLI・dotnet）を、起動前の状態で返す</item>
/// </list>
/// </remarks>
public static class ExternalLauncher
{
    /// <summary>
    /// 名前のまま起動してよい実行ファイル（フルパスへ解決しない）
    /// </summary>
    /// <remarks>
    /// <c>dotnet</c> だけ。名前だけの起動は <c>.exe</c> を補って探し <c>.cmd</c> を拾わないのに対し、PATH の解決
    /// （<see cref="PathExecutableResolver"/>）は <c>.cmd</c> も候補にするので、フルパスへ解決すると PATH の前方の
    /// <c>dotnet.cmd</c> で引数のガードの無いバッチ経由の起動へ化ける（CLAUDE.md の不変条件「<c>.cmd</c> / <c>.bat</c> シムの起動は
    /// 必ず BatchShimProcessGuard を通す」の、<c>DotnetBuildRunner</c> の <c>dotnet</c> だけは名前のまま、の理由）。
    /// </remarks>
    public static IReadOnlySet<string> BareExecutableNames { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "dotnet" };

    /// <summary>シェル経由の起動（既定の動作）</summary>
    private static void ShellStart(ProcessStartInfo startInfo) =>
        Process.Start(startInfo)?.Dispose();

    /// <summary>URL を既定のブラウザで開く</summary>
    /// <exception cref="ArgumentException">開いてよい URL でない（<see cref="UrlPolicy.IsSafe"/>）</exception>
    public static void OpenUrl(Uri uri) => OpenUrl(uri, ShellStart);

    /// <summary>URL を開く本体（テストは起動の処理を差し替えて、確かめる規則だけを見る）</summary>
    internal static void OpenUrl(Uri uri, Action<ProcessStartInfo> start)
    {
        ArgumentNullException.ThrowIfNull(uri);

        if (!UrlPolicy.IsSafe(uri))
        {
            throw new ArgumentException(
                $"Only absolute http or https URLs with a host can be opened: {uri}",
                nameof(uri)
            );
        }

        start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
    }

    /// <summary>フォルダをエクスプローラで開く</summary>
    /// <exception cref="ArgumentException">フルパスでない、またはフォルダが無い</exception>
    public static void OpenFolder(string path) => OpenFolder(path, ShellStart);

    /// <summary>フォルダを開く本体（テストは起動の処理を差し替える）</summary>
    internal static void OpenFolder(string path, Action<ProcessStartInfo> start)
    {
        ArgumentNullException.ThrowIfNull(path);

        if (!Path.IsPathFullyQualified(path) || !Directory.Exists(path))
        {
            throw new ArgumentException(
                $"Only an existing folder given by its full path can be opened: {path}",
                nameof(path)
            );
        }

        start(new ProcessStartInfo(path) { UseShellExecute = true });
    }

    /// <summary>実行ファイルをシェル経由で起動する（終わりを待たない）</summary>
    /// <exception cref="ArgumentException">フルパスでない</exception>
    public static void StartExecutable(string fullPath) => StartExecutable(fullPath, ShellStart);

    /// <summary>実行ファイルを起動する本体（テストは起動の処理を差し替える）</summary>
    internal static void StartExecutable(string fullPath, Action<ProcessStartInfo> start)
    {
        ArgumentNullException.ThrowIfNull(fullPath);

        if (!Path.IsPathFullyQualified(fullPath))
        {
            throw new ArgumentException(
                $"An executable must be started by its full path: {fullPath}",
                nameof(fullPath)
            );
        }

        start(new ProcessStartInfo(fullPath) { UseShellExecute = true });
    }

    /// <summary>
    /// 標準入出力をつなぐ子プロセスを、起動前の状態で返す（呼び出し側がイベントを購読してから <see cref="Process.Start()"/> する）
    /// </summary>
    /// <remarks>
    /// <para>
    /// 起動の前に確かめること：シェル経由でない（<see cref="ProcessStartInfo.UseShellExecute"/> が false）・実行ファイルがフルパス
    /// （<see cref="BareExecutableNames"/> を除く）・<c>.cmd</c> / <c>.bat</c> のシムでない。シムは
    /// <see cref="BatchShimProcessGuard"/> で <c>cmd.exe</c> の形へ包み直してから渡す（包み直した後の実行ファイルは
    /// <see cref="BatchShimProcessGuard.CmdExePath"/>）。包み直しを忘れた起動はここで断る。
    /// </para>
    /// <para><see cref="Process.EnableRaisingEvents"/> は true にして返す（終了を Exited で受けるため）。</para>
    /// </remarks>
    /// <exception cref="ArgumentException">上の条件のどれかに当たる</exception>
    public static Process CreateProcess(ProcessStartInfo startInfo)
    {
        ArgumentNullException.ThrowIfNull(startInfo);

        if (startInfo.UseShellExecute)
        {
            throw new ArgumentException(
                "A child process with redirected standard streams must not use the shell.",
                nameof(startInfo)
            );
        }

        var fileName = startInfo.FileName;

        if (!Path.IsPathFullyQualified(fileName) && !BareExecutableNames.Contains(fileName))
        {
            throw new ArgumentException(
                $"A child process must be started by its full path: {fileName}",
                nameof(startInfo)
            );
        }

        if (BatchShimProcessGuard.IsBatchShim(fileName))
        {
            throw new ArgumentException(
                $"A .cmd or .bat shim must be wrapped by BatchShimProcessGuard before it is started: {fileName}",
                nameof(startInfo)
            );
        }

        return new Process { StartInfo = startInfo, EnableRaisingEvents = true };
    }
}
