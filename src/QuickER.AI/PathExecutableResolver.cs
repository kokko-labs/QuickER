using System.IO;

namespace QuickER.AI;

/// <summary>
/// PATH 環境変数を走査してコマンドの実行ファイルを解決する共有ロジック。
/// 各 CLI バックエンド（codex / claude / copilot）のロケーターが同じ規則を共有するための 1 本化。
/// </summary>
/// <remarks>
/// 結果はキャッシュしない（接続タブを開き直すたびに再評価できるようにするため。
/// PATH 環境変数自体がプロセス起動時のスナップショットである制約は変わらない）。
/// </remarks>
public static class PathExecutableResolver
{
    /// <summary>PATH から実行ファイルを解決する（見つからなければ null）</summary>
    /// <param name="commandName">拡張子なしのコマンド名（例 <c>copilot</c>）</param>
    public static string? Resolve(string commandName) =>
        Resolve(commandName, Environment.GetEnvironmentVariable("PATH"), File.Exists);

    /// <summary>走査ロジック本体（テストから PATH 値・存在判定を差し替えて検証する）</summary>
    /// <param name="commandName">拡張子なしのコマンド名</param>
    /// <param name="pathValue">走査対象の PATH 値（区切りは <see cref="Path.PathSeparator"/>）</param>
    /// <param name="fileExists">ファイル存在判定</param>
    internal static string? Resolve(
        string commandName,
        string? pathValue,
        Func<string, bool> fileExists
    )
    {
        if (string.IsNullOrEmpty(pathValue))
        {
            return null;
        }

        // Windows では PATHEXT 相当の候補（npm 製 CLI は .cmd / .bat で入ることが多い）も順に試す
        string[] candidates = OperatingSystem.IsWindows()
            ? [commandName + ".exe", commandName + ".cmd", commandName + ".bat", commandName]
            : [commandName];

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
                    // 不正な文字を含む PATH 要素は読み飛ばす
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
