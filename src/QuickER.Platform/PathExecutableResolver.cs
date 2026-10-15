using System.IO;

namespace QuickER.Platform;

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
    public static string? Resolve(
        string commandName,
        string? pathValue,
        Func<string, bool> fileExists
    )
    {
        if (string.IsNullOrEmpty(pathValue))
        {
            return null;
        }

        // Windows では PATHEXT 相当の候補（npm 製 CLI は .cmd / .bat で入ることが多い）を順に試す。
        // 拡張子なしの名前は候補にしない＝CreateProcess は拡張子のない名前に .exe を補って探すため、
        // npm が同じフォルダへ置く sh スクリプト（拡張子なしの claude 等）を返しても起動できない
        // （しかも見つかった時点で走査が止まり、後ろのフォルダにある本物へ届かない）
        string[] candidates = OperatingSystem.IsWindows()
            ? [commandName + ".exe", commandName + ".cmd", commandName + ".bat"]
            : [commandName];

        foreach (var directory in pathValue.Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(directory))
            {
                continue;
            }

            // Windows の PATH は要素を引用符で囲んでもよい（"C:\Program Files\nodejs" など）ので外す
            // （Unix の PATH に引用の規則は無く、引用符も名前の一部なので触らない）
            var trimmed = OperatingSystem.IsWindows()
                ? directory.Trim().Trim('"').Trim()
                : directory.Trim();

            // 相対パスの PATH 要素（"." など）は読み飛ばす＝解決結果がカレントフォルダ次第で変わり、
            // 呼び出し側へフルパスを返すという約束が崩れるため
            if (!Path.IsPathFullyQualified(trimmed))
            {
                continue;
            }

            foreach (var candidate in candidates)
            {
                var fullPath = Path.Combine(trimmed, candidate);

                if (fileExists(fullPath))
                {
                    return fullPath;
                }
            }
        }

        return null;
    }
}
