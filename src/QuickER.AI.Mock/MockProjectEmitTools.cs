using System.IO;
using QuickER.Mcp;

namespace QuickER.AI.Mock;

/// <summary>
/// API キー方式の WPF モックプロジェクト生成（固定パイプライン）で AI にファイルを提出させる唯一のツール定義。
/// </summary>
/// <remarks>
/// <para>
/// エージェント型（Claude Code / Codex）はネイティブのファイル編集・コマンド実行を使うが、API キー方式は
/// 探索させず（読み取り・ビルド実行のツールは与えない）、完成した実装ファイルを <c>emit_file</c> で丸ごと提出させる。
/// ツール定義は中立言語（英語）を正本とする（<see cref="ErDiagramToolCatalog"/> / <see cref="MockFolderDesignTools"/>
/// と同流儀・ハードコード）。実行と検証（パス保護）は <see cref="ApiKeyMockProjectAgent"/> が担う。
/// </para>
/// </remarks>
public static class MockProjectEmitTools
{
    /// <summary>完全なファイル内容を提出（upsert）するツール名</summary>
    public const string EmitFileToolName = "emit_file";

    /// <summary>API キー方式の固定パイプラインで公開するツール定義一覧を返す（<c>emit_file</c> の 1 つ）</summary>
    public static IReadOnlyList<ToolDefinition> GetDefinitions()
    {
        return
        [
            new ToolDefinition
            {
                Name = EmitFileToolName,
                Description =
                    "Submits one complete source file into the output project. "
                    + "This is the only way to submit implementation files - writing code into the chat body has no effect. "
                    + "Always submit the entire file content (a diff is not allowed). "
                    + "Re-submitting the same path overwrites the previous content. "
                    + "You cannot read files or run a build; produce complete, compilable files from the information you are given. "
                    + "Do not write to Generated/ (data layer), design/ (design spec), README-QuickER.md, or the .sln/.csproj files - those are owned by the scaffold and such submissions are rejected.",
                DeferLoading = false,
                InputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        path = new
                        {
                            type = "string",
                            description = "Relative path under the output project (forward slashes recommended, e.g. \"MockApp/Views/OrderListView.xaml\"). "
                                + "Absolute paths, drive letters and '..' are not allowed.",
                        },
                        content = new
                        {
                            type = "string",
                            description = "The complete content of the file (whole file, not a diff).",
                        },
                    },
                    required = new[] { "path", "content" },
                },
            },
        ];
    }

    /// <summary>パス検証の結果（成功なら <see cref="RelativePath"/>／<see cref="FullPath"/> が入る・失敗なら <see cref="Error"/>）</summary>
    /// <param name="Ok">検証に通ったか</param>
    /// <param name="RelativePath">スラッシュ正規化済みの相対パス（成功時）</param>
    /// <param name="FullPath">出力先の絶対パス（成功時）</param>
    /// <param name="Error">失敗理由（英語・失敗時）</param>
    public readonly record struct EmitPathResult(
        bool Ok,
        string RelativePath,
        string FullPath,
        string Error
    );

    /// <summary>
    /// 提出パスを検証し、出力先の絶対パスへ解決する（スキャフォールド成果物の保護・トラバーサル拒否）。
    /// </summary>
    /// <param name="workingDirectory">出力フォルダ（ソリューション直下＝相対パスの基点）</param>
    /// <param name="path">AI が指定した相対パス</param>
    /// <remarks>
    /// 拒否条件（いずれも英語メッセージで失敗を返す）:
    /// 空パス／絶対パス・ドライブ文字・先頭スラッシュ／<c>".."</c>／<c>Generated</c> または <c>design</c> セグメント配下／
    /// <c>README-QuickER.md</c>／拡張子 <c>.sln</c>・<c>.csproj</c>（スキャフォールドが作成済みのため上書き不可）。
    /// 新規の UI 層ファイル（App.xaml・Views/・ViewModels/ 等）はプロジェクト配下なら自由に追加できる。
    /// </remarks>
    public static EmitPathResult ResolveEmitPath(string workingDirectory, string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return new EmitPathResult(false, string.Empty, string.Empty, "path is empty.");
        }

        var normalized = path.Replace('\\', '/').Trim();

        // 絶対パス・ドライブ文字・先頭スラッシュは拒否（出力フォルダ外への書き込みを防ぐ）
        if (
            normalized.StartsWith('/')
            || Path.IsPathRooted(path)
            || normalized.Contains(':', StringComparison.Ordinal)
        )
        {
            return new EmitPathResult(
                false,
                string.Empty,
                string.Empty,
                $"path must be a relative path (absolute paths and drive letters are not allowed): {path}"
            );
        }

        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);

        if (segments.Length == 0)
        {
            return new EmitPathResult(false, string.Empty, string.Empty, "path is empty.");
        }

        foreach (var segment in segments)
        {
            if (segment == "..")
            {
                return new EmitPathResult(
                    false,
                    string.Empty,
                    string.Empty,
                    $"path must not contain '..': {path}"
                );
            }

            // スキャフォールドが所有するフォルダ（データ層・デザイン仕様）は保護する
            if (
                string.Equals(
                    segment,
                    MockProjectScaffoldService.GeneratedFolderName,
                    StringComparison.OrdinalIgnoreCase
                ) || string.Equals(segment, "design", StringComparison.OrdinalIgnoreCase)
            )
            {
                return new EmitPathResult(
                    false,
                    string.Empty,
                    string.Empty,
                    $"path is not writable (scaffold-owned '{segment}/' folder): {path}"
                );
            }
        }

        var fileName = segments[^1];

        if (
            string.Equals(
                fileName,
                MockProjectScaffoldService.ReadmeFileName,
                StringComparison.OrdinalIgnoreCase
            )
        )
        {
            return new EmitPathResult(
                false,
                string.Empty,
                string.Empty,
                $"path is not writable (scaffold-owned {MockProjectScaffoldService.ReadmeFileName}): {path}"
            );
        }

        var extension = Path.GetExtension(fileName);

        if (
            string.Equals(extension, ".sln", StringComparison.OrdinalIgnoreCase)
            || string.Equals(extension, ".csproj", StringComparison.OrdinalIgnoreCase)
        )
        {
            return new EmitPathResult(
                false,
                string.Empty,
                string.Empty,
                $"path is not writable (the {extension} file is created by the scaffold): {path}"
            );
        }

        var relative = string.Join('/', segments);
        var baseFull = Path.GetFullPath(workingDirectory);
        var full = Path.GetFullPath(Path.Combine(baseFull, relative));

        // 正規化後に出力フォルダ外へ出ていないか最終防御（symlink 等ではなく単純な脱出を弾く）
        var basePrefix = baseFull.EndsWith(Path.DirectorySeparatorChar)
            ? baseFull
            : baseFull + Path.DirectorySeparatorChar;

        if (!full.StartsWith(basePrefix, StringComparison.OrdinalIgnoreCase))
        {
            return new EmitPathResult(
                false,
                string.Empty,
                string.Empty,
                $"path escapes the output folder: {path}"
            );
        }

        return new EmitPathResult(true, relative, full, string.Empty);
    }
}
