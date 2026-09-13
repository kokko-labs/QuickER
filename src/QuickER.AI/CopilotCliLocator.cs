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

    /// <summary>
    /// 起動直前に、解決した copilot のパスが安全に SDK へ渡せることを確かめる
    /// （検証を通ったパスをそのまま返す）。
    /// </summary>
    /// <remarks>
    /// copilot の起動は GitHub.Copilot.SDK の内側で行われ、QuickER は引数を組み立てない。
    /// そのため <c>.cmd</c> シムに対して codex / claude と同じ cmd.exe ラップは掛けられず、
    /// ここで掛けられる防御は「シムのパス自体に引用符が無いこと」の確認だけになる
    /// （引用符は正規の Windows パスには現れず、含まれていれば引用の切断を狙ったもの）。
    /// 残余は docs/ai-chat.md の注意節に明記している。検出（<see cref="IsAvailable"/>）が
    /// 例外を投げないよう、検証は解決ではなくこの起動直前の関門に置く。
    /// </remarks>
    /// <param name="executablePath">解決済みの copilot 実行ファイルパス</param>
    /// <exception cref="InvalidOperationException">シムのパスに引用符が含まれる場合</exception>
    public static string EnsureSafeToLaunch(string executablePath)
    {
        if (
            BatchShimProcessGuard.IsBatchShim(executablePath)
            && executablePath.Contains('"', StringComparison.Ordinal)
        )
        {
            throw new InvalidOperationException(
                string.Format(Resources.Strings.Copilot_PathHasQuote, executablePath)
            );
        }

        return executablePath;
    }

    /// <summary>走査ロジック（テストから PATH 値・存在判定を差し替えて検証する）</summary>
    /// <param name="pathValue">走査対象の PATH 値（区切りは <see cref="Path.PathSeparator"/>）</param>
    /// <param name="fileExists">ファイル存在判定</param>
    internal static string? ResolveExecutablePath(
        string? pathValue,
        Func<string, bool> fileExists
    ) => PathExecutableResolver.Resolve(CommandName, pathValue, fileExists);
}
