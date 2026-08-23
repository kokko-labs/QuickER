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

    /// <summary>
    /// どのターゲットでも提出を許可し得る拡張子の上限集合（大文字小文字を無視して照合する）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 各ターゲットの <see cref="MockProjectTargetProfile.AllowedEmitExtensions"/> はこの部分集合であり、実効の
    /// 許可集合は両者の積になる。ターゲットを追加してもプロファイル側の宣言だけでは上限を超えられないため、
    /// 「ビルド検証がそのままユーザー権限のコード実行になる」種類のファイル（MSBuild が自動 import する
    /// <c>Directory.Build.props</c> / <c>Directory.Build.targets</c> / <c>Directory.Packages.props</c>・
    /// レスポンスファイル・<c>global.json</c> など）は構造的に提出できない。
    /// </para>
    /// <para>
    /// <c>.cs</c> はビルドでは実行されない（ソースジェネレータは csproj 経由でしか追加できず csproj は保護済み）ため、
    /// UI 層のソースとして許可する。
    /// </para>
    /// </remarks>
    public static IReadOnlySet<string> SupportedEmitExtensions { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".cs", ".xaml", ".razor", ".css" };

    /// <summary>提出を拒否するフォルダ名（スキャフォールドが所有するもの＋ビルドの入出力）</summary>
    /// <remarks>
    /// <c>obj</c> / <c>bin</c> はビルド入力（<c>project.assets.json</c> 等）を含むため、拡張子の許可集合に依らず
    /// フォルダごと落とす。
    /// </remarks>
    private static readonly string[] BlockedFolderNames =
    [
        MockProjectScaffoldService.GeneratedFolderName,
        "design",
        "obj",
        "bin",
    ];

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
                    + "Only UI-layer source files whose extension belongs to the target platform may be submitted; a rejection message lists the accepted extensions. "
                    + "Every other file is rejected - project, build, package and configuration files (.csproj, .sln, NuGet.Config, Directory.Build.props, Directory.Build.targets, Directory.Packages.props, global.json, *.rsp and the like) as well as anything under Generated/ (data layer), design/ (design spec), obj/ or bin/, and README-QuickER.md.",
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
    /// 提出パスを検証し、出力先の絶対パスへ解決する（拡張子ホワイトリスト・保護フォルダ・トラバーサル拒否）。
    /// </summary>
    /// <param name="workingDirectory">出力フォルダ（ソリューション直下＝相対パスの基点）</param>
    /// <param name="profile">生成ターゲットのプロファイル（許可拡張子の宣言元）</param>
    /// <param name="path">AI が指定した相対パス</param>
    /// <remarks>
    /// <para>
    /// 受理されるのは「保護フォルダの外にある、実効の許可拡張子を持つ相対パス」だけで、実効の許可集合は
    /// <see cref="MockProjectTargetProfile.AllowedEmitExtensions"/> と <see cref="SupportedEmitExtensions"/> の積。
    /// 新規の UI 層ファイル（App.xaml・Views/・ViewModels/・Components/Pages/ 等）はプロジェクト配下なら自由に追加できる。
    /// </para>
    /// <para>
    /// 拒否条件（いずれも英語メッセージで失敗を返す）:
    /// 空パス／絶対パス・ドライブ文字・先頭スラッシュ／<c>".."</c>／保護フォルダのセグメント配下／
    /// <c>README-QuickER.md</c>／<c>NuGet.Config</c>（パッケージソース設定の追加禁止）／
    /// 拡張子 <c>.sln</c>・<c>.csproj</c>（スキャフォールドが作成済みのため上書き不可）／
    /// 実効の許可集合に無い拡張子（拡張子なしを含む）／正規化後に出力フォルダ外へ出るパス。
    /// </para>
    /// </remarks>
    public static EmitPathResult ResolveEmitPath(
        string workingDirectory,
        MockProjectTargetProfile profile,
        string? path
    )
    {
        ArgumentNullException.ThrowIfNull(profile);

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

            // スキャフォールドが所有するフォルダ（データ層・デザイン仕様）とビルドの入出力フォルダは保護する
            if (BlockedFolderNames.Contains(segment, StringComparer.OrdinalIgnoreCase))
            {
                return new EmitPathResult(
                    false,
                    string.Empty,
                    string.Empty,
                    $"path is not writable (protected '{segment}/' folder): {path}"
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

        // パッケージソース設定の追加は禁止（復元先のすり替え・オフライン固定化を防ぐ）
        if (string.Equals(fileName, "NuGet.Config", StringComparison.OrdinalIgnoreCase))
        {
            return new EmitPathResult(
                false,
                string.Empty,
                string.Empty,
                $"path is not writable (package source configuration is not allowed): {path}"
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

        // ホワイトリスト判定（拒否リストではなく許可リスト）。ビルド検証がそのままコード実行になる
        // MSBuild 制御ファイルを、名前の列挙ではなく「UI 層のソースだけを通す」構造で締め出す。
        var allowedExtensions = ResolveAllowedExtensions(profile);

        if (!allowedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            return new EmitPathResult(
                false,
                string.Empty,
                string.Empty,
                $"path is not writable (only {string.Join(", ", allowedExtensions)} files may be submitted): {path}"
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

    /// <summary>実効の許可拡張子（プロファイルの宣言と <see cref="SupportedEmitExtensions"/> の積）を昇順で返す</summary>
    /// <remarks>順序を固定するのは、拒否メッセージに載る列挙が呼び出しごとに揺れないようにするため。</remarks>
    private static IReadOnlyList<string> ResolveAllowedExtensions(
        MockProjectTargetProfile profile
    ) =>
        profile
            .AllowedEmitExtensions.Where(SupportedEmitExtensions.Contains)
            .OrderBy(extension => extension, StringComparer.OrdinalIgnoreCase)
            .ToArray();
}
