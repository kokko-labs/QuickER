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
        Resolve(
            commandName,
            Environment.GetEnvironmentVariable("PATH"),
            File.Exists,
            Environment.GetEnvironmentVariable("PATHEXT")
        );

    /// <summary>走査ロジック本体（テストから PATH 値・存在判定を差し替えて検証する。拡張子の順は既定の PATHEXT の順）</summary>
    /// <param name="commandName">拡張子なしのコマンド名</param>
    /// <param name="pathValue">走査対象の PATH 値（区切りは <see cref="Path.PathSeparator"/>）</param>
    /// <param name="fileExists">ファイル存在判定</param>
    public static string? Resolve(
        string commandName,
        string? pathValue,
        Func<string, bool> fileExists
    ) => Resolve(commandName, pathValue, fileExists, pathExtValue: null);

    /// <summary>走査ロジック本体（PATHEXT の値も差し替えて検証する）</summary>
    /// <param name="commandName">拡張子なしのコマンド名</param>
    /// <param name="pathValue">走査対象の PATH 値（区切りは <see cref="Path.PathSeparator"/>）</param>
    /// <param name="fileExists">ファイル存在判定</param>
    /// <param name="pathExtValue">PATHEXT の値（null・空・候補の拡張子を 1 つも含まないときは既定の順）</param>
    public static string? Resolve(
        string commandName,
        string? pathValue,
        Func<string, bool> fileExists,
        string? pathExtValue
    )
    {
        if (string.IsNullOrEmpty(pathValue))
        {
            return null;
        }

        // Windows では .exe / .cmd / .bat（npm 製 CLI は .cmd / .bat で入ることが多い）を PATHEXT の順に試す。
        // 同じフォルダに複数あるとき、コマンドラインで名前を打って起動されるのと同じものを選ぶため。
        // 拡張子なしの名前は候補にしない＝CreateProcess は拡張子のない名前に .exe を補って探すため、
        // npm が同じフォルダへ置く sh スクリプト（拡張子なしの claude 等）を返しても起動できない
        // （しかも見つかった時点で走査が止まり、後ろのフォルダにある本物へ届かない）
        string[] candidates = OperatingSystem.IsWindows()
            ? [.. CandidateExtensions(pathExtValue).Select(extension => commandName + extension)]
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

    /// <summary>候補にする拡張子（<c>.exe</c> / <c>.cmd</c> / <c>.bat</c>）の、PATHEXT での並び</summary>
    /// <remarks>
    /// PATHEXT に無い・壊れているときは、Windows の既定の PATHEXT（<c>.COM;.EXE;.BAT;.CMD;…</c>）の順
    /// ＝<c>.exe</c>・<c>.bat</c>・<c>.cmd</c>。PATHEXT にあっても候補の 3 つ以外（<c>.com</c>・<c>.vbs</c>・<c>.ps1</c> など）は試さない。
    /// </remarks>
    /// <param name="pathExtValue">PATHEXT の値</param>
    internal static IReadOnlyList<string> CandidateExtensions(string? pathExtValue)
    {
        string[] supported = [".exe", ".cmd", ".bat"];
        var ordered = (pathExtValue ?? string.Empty)
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(extension => extension.ToLowerInvariant())
            .Where(extension => supported.Contains(extension, StringComparer.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        return ordered.Count > 0 ? ordered : [".exe", ".bat", ".cmd"];
    }
}
