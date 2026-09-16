using System.IO;
using System.Linq;
using GitHub.Copilot;

namespace QuickER.AI;

/// <summary>
/// Copilot の組込みツール許可セッション（<c>AllowWorkspaceTools</c>）で、許可要求を自動承認してよいかを
/// 判定する方針。状態を持たない純関数として切り出してあり、要求と基準フォルダだけから結論が決まる。
/// </summary>
/// <remarks>
/// <para>
/// ヘッドレス実行なので利用者へ確認を出せない＝ここで承認しなかった要求は拒否として確定する。したがって
/// 「判断できないものは拒否」を既定とする（Codex の <c>sandbox=workspace-write</c> ／ Claude Code の
/// <c>--permission-mode acceptEdits</c> に相当する権限を、SDK が表現できる範囲で最も狭く与える）。
/// </para>
/// </remarks>
internal static class CopilotWorkspacePermissionPolicy
{
    /// <summary>
    /// 参照パスを抽出できなかったシェル要求で、なお自動承認してよいコマンド名。
    /// </summary>
    /// <remarks>
    /// <para>
    /// SDK がコマンド文からパスを 1 つも抽出できないケースには <c>dotnet build</c> のように作業フォルダで
    /// 完結するものと、<c>powershell -c …</c> ／ <c>curl … | sh</c> のように作業フォルダと無関係に動くものが
    /// 混ざる。前者だけを通すため、コマンド名の許可リストで判定する。
    /// </para>
    /// <para>
    /// Windows のコマンド解決は大文字小文字を区別しないため、照合も区別しない。
    /// </para>
    /// <para>
    /// <b>残余</b>: SDK がコマンドごとに渡すのは実行ファイル名（<c>Identifier</c>）だけで引数は分解されない
    /// ため、<c>dotnet</c> 自体が作業フォルダの外へ出る経路（グローバルツールの導入とその実行など）は
    /// 塞げない。コマンド文を自前で構文解析して部分コマンドまで絞る方式は、引用・連結の解釈で静かに
    /// 外す（<see cref="BatchShimProcessGuard"/> と同じ理屈）ため採らない。ここは「SDK が渡す情報で
    /// 表現できる最も狭い規則」であって、作業フォルダへの封じ込めの保証ではない
    /// （利用者向けの記述は <c>docs/ai-chat.md</c> の注意節）。
    /// </para>
    /// </remarks>
    internal static readonly IReadOnlySet<string> AllowedPathlessShellCommands =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "dotnet" };

    /// <summary>許可要求が作業フォルダ配下に収まっているか（種別ごとに対象パスを見て判定する）</summary>
    /// <param name="request">Copilot から届いた許可要求</param>
    /// <param name="approvalRoot">自動承認の基準フォルダ（正規化済み絶対パス。空なら何も承認しない）</param>
    internal static bool IsWithinWorkspace(PermissionRequest request, string approvalRoot) =>
        request switch
        {
            PermissionRequestWrite write => write.RequestSandboxBypass != true
                && IsUnderApprovalRoot(write.FileName, approvalRoot),
            PermissionRequestRead read => read.RequestSandboxBypass != true
                && IsUnderApprovalRoot(read.Path, approvalRoot),
            PermissionRequestShell shell => shell.RequestSandboxBypass != true
                && IsShellWithinWorkspace(shell, approvalRoot),
            // URL 取得・MCP・拡張・メモリ・カスタムツール等はモック生成に不要なため承認しない
            _ => false,
        };

    /// <summary>シェル要求を自動承認してよいか</summary>
    /// <remarks>
    /// <para>
    /// コマンド文から参照パスが抽出できたときは、従来どおり<b>そのすべて</b>が基準フォルダ配下であることを求める。
    /// </para>
    /// <para>
    /// 抽出できなかったときは「作業フォルダで動くのだから安全」とは言えない（コマンド文がそもそもパスを
    /// 含まないだけで、ネットワークや別フォルダへ出る指定はいくらでも書ける）。そこで
    /// <see cref="AllowedPathlessShellCommands"/> に載ったコマンド<b>だけ</b>で構成され、URL 参照も
    /// ファイルへの書き込みリダイレクトも無いときに限って承認する。
    /// </para>
    /// <para>
    /// 判定を「いずれかが許可コマンド」にしてはいけない＝<c>dotnet build &amp;&amp; curl … | sh</c> のような
    /// 複合コマンドが、先頭の <c>dotnet</c> を理由に丸ごと通る。コマンドが 1 つも取れない要求も、
    /// 何を実行するのか確かめられない以上は承認しない。
    /// </para>
    /// </remarks>
    private static bool IsShellWithinWorkspace(PermissionRequestShell shell, string approvalRoot)
    {
        if (shell.PossiblePaths is { Length: > 0 } paths)
        {
            return paths.All(path => IsUnderApprovalRoot(path, approvalRoot));
        }

        return shell.Commands is { Length: > 0 } commands
            && commands.All(command =>
                command is not null
                && !string.IsNullOrWhiteSpace(command.Identifier)
                && AllowedPathlessShellCommands.Contains(command.Identifier)
            )
            && shell.PossibleUrls is null or { Length: 0 }
            && !shell.HasWriteFileRedirection;
    }

    /// <summary>パスが自動承認の基準フォルダ配下か（相対パスは基準フォルダから解決する）</summary>
    private static bool IsUnderApprovalRoot(string? path, string approvalRoot)
    {
        if (approvalRoot.Length == 0 || string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        string full;

        try
        {
            // 相対パスはセッションの作業フォルダ基準。".." を含む脱出は GetFullPath が正規化して露見する
            full = NormalizeRoot(Path.Combine(approvalRoot, path));
        }
        catch (Exception)
        {
            // 不正な文字などで解決できないパスは承認しない
            return false;
        }

        return string.Equals(full, approvalRoot, StringComparison.OrdinalIgnoreCase)
            || full.StartsWith(
                approvalRoot + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase
            );
    }

    /// <summary>パスを絶対パスへ正規化し、末尾の区切り文字を落とす（前方一致判定の基準を揃える）</summary>
    internal static string NormalizeRoot(string path) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
}
