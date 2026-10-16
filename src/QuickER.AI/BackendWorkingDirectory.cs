using System.IO;
using QuickER.Platform;

namespace QuickER.AI;

/// <summary>
/// 内蔵チャットの CLI バックエンド（Codex・Claude Code・Copilot）が子プロセスの作業フォルダに使う一時フォルダ
/// （<c>%TEMP%\QuickER\{backend}\{GUID}</c>）の作成と削除。
/// </summary>
/// <remarks>
/// CLI の作業フォルダをアプリの実行フォルダのままにすると、承認なしの実行でその配下を読めてしまう。そこで
/// バックエンドごとに中身の無い一時フォルダを作って閉じ込める。作成と削除の規則を 1 か所に置き、3 つの
/// バックエンドで食い違わないようにする
/// </remarks>
internal static class BackendWorkingDirectory
{
    /// <summary>空の一時作業フォルダを作り、そのフルパスを返す</summary>
    /// <param name="backend">バックエンド名（フォルダ名の 1 段目。例 <c>codex</c>）</param>
    public static string Create(string backend)
    {
        var path = Path.Combine(AppDataPaths.TempRoot, backend, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>
    /// バックエンドの常駐プロセス自身の作業フォルダ（<c>%TEMP%\QuickER\{backend}</c>）を作り、そのフルパスを返す（消さない）
    /// </summary>
    /// <remarks>
    /// 会話ごとの作業フォルダ（<see cref="Create"/>）はスレッドに渡すもので、プロセスの起動時には決まっていない。
    /// プロセスをアプリのカレントフォルダのまま起動しないための置き場所（中身は持たない）。
    /// </remarks>
    /// <param name="backend">バックエンド名（例 <c>codex</c>）</param>
    public static string ProcessRoot(string backend)
    {
        var path = Path.Combine(AppDataPaths.TempRoot, backend);
        Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>一時作業フォルダを削除する（ベストエフォート。空文字なら何もしない）</summary>
    /// <remarks>子プロセスが掴んでいると消せないので、子プロセスを止めてから呼ぶ</remarks>
    /// <param name="path"><see cref="Create"/> が返したパス</param>
    public static void TryDelete(string path)
    {
        if (path.Length == 0)
        {
            return;
        }

        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (IOException)
        {
            // 使用中などで削除できない場合は無視する
        }
        catch (UnauthorizedAccessException)
        {
            // 権限不足は無視する
        }
    }
}
