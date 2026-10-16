using System.Globalization;
using System.IO;
using QuickER.Mcp;
using QuickER.Platform;

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
        MockProjectScaffoldService.DesignRootFolderName,
        .. MockProjectFiles.BuildOutputDirectoryNames,
    ];

    /// <summary>提出を拒否するフォルダ名（プロジェクト名もこの名前を断る＝<see cref="MockProjectScaffoldService.CheckProjectName"/>）</summary>
    internal static IReadOnlyList<string> ProtectedFolderNames => BlockedFolderNames;

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
    /// 判定の前に、区切りを <c>/</c> へ揃え、値全体の前後の空白を除き、空の区間と <c>.</c> の区間を落とす
    /// （AI の出力の境界の前処理。どちらも保護フォルダの別名にはならない。落とした後に区間が残らなければ空パスとして断る）。
    /// </para>
    /// <para>
    /// 拒否条件（いずれも英語メッセージで失敗を返す）:
    /// 空パス／相対パスとして断る形（<see cref="PathSegments.CheckRelativePath"/>＝絶対パス・ドライブ・<c>..</c>・
    /// 使えない文字・先頭と末尾の空白と <c>.</c>・<c>~</c>〔8.3 の別名で保護フォルダ名をすり抜けさせない〕・予約名・長さ）／
    /// 保護フォルダのセグメント配下（正規化の前後で照合）／
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

        // AI の出力の境界の前処理：区切りを '/' へ揃えて値全体の前後の空白を除き、空の区間と '.' の区間を落とす
        // （AI がよく書く "./MockApp/…" や "MockApp//Views/…" を断らない。どちらも保護フォルダの別名にはならない）。
        // 先頭の '/'（ルート・UNC）は落とさずに共有の判定へ渡して断る（落とすと "/a" が "a" になる）。
        // ドライブ指定（"C:…"）は区間を落としても先頭に残るので、そのまま共有の判定が断る
        var normalized = path.Replace('\\', '/').Trim();
        var cleaned = normalized.StartsWith('/')
            ? normalized
            : string.Join(
                '/',
                normalized.Split('/').Where(segment => segment.Length > 0 && segment != ".")
            );

        // 相対パスとして断る形は共有の判定（PathSegments）で断る。'~' を断るのは 8.3 の別名（"GENERA~1"）で
        // 保護フォルダ名との照合をすり抜けさせないため、末尾の '.' と空白を断るのは Windows がそれを落として開く
        // （"Generated./x.cs" は Generated/ への書き込みになる）ため
        if (PathSegments.CheckRelativePath(cleaned) is { } violation)
        {
            return new EmitPathResult(
                false,
                string.Empty,
                string.Empty,
                $"path is invalid ({PathSegments.Describe(violation, CultureInfo.InvariantCulture)}): {path}"
            );
        }

        var segments = PathSegments.SplitRelativePath(cleaned);

        foreach (var segment in segments)
        {
            // スキャフォールドが所有するフォルダ（データ層・デザイン仕様）とビルドの入出力フォルダは保護する
            if (FindBlockedFolder([segment]) is { } blocked)
            {
                return ProtectedFolderRejection(blocked, path);
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
        if (!PathContainment.IsUnder(full, baseFull))
        {
            return new EmitPathResult(
                false,
                string.Empty,
                string.Empty,
                $"path escapes the output folder: {path}"
            );
        }

        // 正規化後の出力フォルダ相対セグメントでも保護フォルダを照合する（上のセグメント検査の二重化＝
        // 正規化が名前を書き換える経路を、照合する側の想定から独立に塞ぐ）
        if (FindBlockedFolderInNormalizedPath(baseFull, full) is { } normalizedBlocked)
        {
            return ProtectedFolderRejection(normalizedBlocked, path);
        }

        return new EmitPathResult(true, relative, full, string.Empty);
    }

    /// <summary>
    /// 正規化済みの絶対パスを出力フォルダ相対のセグメントへ分け、保護フォルダ名に一致する最初のものを返す（無ければ null）。
    /// </summary>
    /// <param name="baseFull">正規化済みの出力フォルダ</param>
    /// <param name="full">正規化済みの提出先（出力フォルダ配下であることは確認済み）</param>
    internal static string? FindBlockedFolderInNormalizedPath(string baseFull, string full) =>
        FindBlockedFolder(
            Path.GetRelativePath(baseFull, full)
                .Split(
                    [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                    StringSplitOptions.RemoveEmptyEntries
                )
        );

    /// <summary>セグメント列のうち保護フォルダ名（大文字小文字を無視）に一致する最初のものを返す（無ければ null）</summary>
    private static string? FindBlockedFolder(IEnumerable<string> segments) =>
        segments.FirstOrDefault(segment =>
            BlockedFolderNames.Contains(segment, StringComparer.OrdinalIgnoreCase)
        );

    /// <summary>保護フォルダへの提出を拒否する結果を作る</summary>
    private static EmitPathResult ProtectedFolderRejection(string segment, string path) =>
        new(
            false,
            string.Empty,
            string.Empty,
            $"path is not writable (protected '{segment}/' folder): {path}"
        );

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
