using System.IO;

namespace QuickER.AI;

/// <summary>
/// GitHub Copilot CLI（<c>copilot</c>）を PATH から検出する共有ロケーター。
/// </summary>
/// <remarks>
/// <para>
/// GitHub.Copilot.SDK は既定でビルド時に取得した CLI を同梱して起動するが、QuickER は
/// <c>CopilotSkipCliDownload</c> でそれをオプトアウトし、ユーザーが自分でインストールした
/// copilot をここで検出して <c>RuntimeConnection.ForStdio(検出パス)</c> へ渡す
/// （認証もユーザーのログイン状態をそのまま使うため、CLI の実体はユーザーのものである必要がある）。
/// </para>
/// <para><see cref="CodexCliLocator"/> と同型で、走査本体は <see cref="PathExecutableResolver"/> と共有する。</para>
/// </remarks>
public static class CopilotCliLocator
{
    /// <summary>検出対象のコマンド名（拡張子なし）</summary>
    private const string CommandName = "copilot";

    /// <summary>copilot 実行ファイルが PATH で解決できるか</summary>
    public static bool IsAvailable() => ResolveExecutablePath() is not null;

    /// <summary>PATH から copilot 実行ファイルを解決する（見つからなければ null）</summary>
    public static string? ResolveExecutablePath() => PathExecutableResolver.Resolve(CommandName);

    /// <summary>走査ロジック（テストから PATH 値・存在判定を差し替えて検証する）</summary>
    /// <param name="pathValue">走査対象の PATH 値（区切りは <see cref="Path.PathSeparator"/>）</param>
    /// <param name="fileExists">ファイル存在判定</param>
    internal static string? ResolveExecutablePath(
        string? pathValue,
        Func<string, bool> fileExists
    ) => PathExecutableResolver.Resolve(CommandName, pathValue, fileExists);
}
