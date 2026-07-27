using System.IO;

namespace QuickER.AI;

/// <summary>
/// codex CLI を PATH から検出する共有ロケーター。
/// AI チャット（<see cref="CodexChatEngine"/> 経由の <see cref="CodexAppServerClient"/>）と
/// AI モック生成（CodexMockProjectAgent）が同じ判定を使う。
/// </summary>
/// <remarks>
/// <see cref="ClaudeCodeProcessClient"/> の claude 検出と同型の PATH 走査。
/// 結果はキャッシュしない（接続タブを開き直すたびに再評価できるようにするため。
/// PATH 環境変数自体がプロセス起動時のスナップショットである制約は変わらない）。
/// </remarks>
public static class CodexCliLocator
{
    /// <summary>codex 実行ファイルが PATH で解決できるか</summary>
    public static bool IsAvailable() => ResolveExecutablePath() is not null;

    /// <summary>PATH から codex 実行ファイルを解決する（見つからなければ null）</summary>
    public static string? ResolveExecutablePath() =>
        ResolveExecutablePath(Environment.GetEnvironmentVariable("PATH"), File.Exists);

    /// <summary>走査ロジック本体（テストから PATH 値・存在判定を差し替えて検証する）</summary>
    /// <param name="pathValue">走査対象の PATH 値（区切りは <see cref="Path.PathSeparator"/>）</param>
    /// <param name="fileExists">ファイル存在判定</param>
    internal static string? ResolveExecutablePath(string? pathValue, Func<string, bool> fileExists)
    {
        if (string.IsNullOrEmpty(pathValue))
        {
            return null;
        }

        string[] candidates = OperatingSystem.IsWindows()
            ? ["codex.exe", "codex.cmd", "codex.bat", "codex"]
            : ["codex"];

        foreach (var directory in pathValue.Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(directory))
            {
                continue;
            }

            foreach (var candidate in candidates)
            {
                string fullPath;

                try
                {
                    fullPath = Path.Combine(directory.Trim(), candidate);
                }
                catch (ArgumentException)
                {
                    continue;
                }

                if (fileExists(fullPath))
                {
                    return fullPath;
                }
            }
        }

        return null;
    }
}
