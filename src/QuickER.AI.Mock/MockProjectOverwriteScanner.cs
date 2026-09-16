using System.IO;
using System.Linq;

namespace QuickER.AI.Mock;

/// <summary>出力フォルダの走査結果（スキャフォールドを実行する前に利用者へ見せる判断材料）</summary>
/// <param name="ExistingPaths">
/// 出力フォルダにある既存のファイル・フォルダの相対パス（表示順は固定）。スキャフォールドが
/// 書き出すものと同名のものと、同じ場所にある別のソリューション・プロジェクトの両方を含む。
/// </param>
internal sealed record MockProjectOverwriteScan(IReadOnlyList<string> ExistingPaths)
{
    /// <summary>利用者へ確認を出すべきか（既存のファイル・フォルダが 1 つでもあるか）</summary>
    internal bool RequiresConfirmation => ExistingPaths.Count > 0;
}

/// <summary>
/// モックプロジェクト生成の出力フォルダを、スキャフォールド実行前に走査して
/// 「上書き・同居の対象になる既存のファイル・フォルダ」を洗い出すスキャナ。
/// </summary>
/// <remarks>
/// <para>
/// スキャフォールド（<see cref="MockProjectScaffoldService.Scaffold"/>）はソリューション・csproj・
/// <c>README-QuickER.md</c>・<c>Generated/</c>・<c>design/mock/</c> を無条件に上書きする。同一プロジェクト名での
/// 再生成は正当な用途なので禁止はせず、何が上書きされるかを列挙して確認を取る材料にする。
/// </para>
/// <para>
/// あわせて出力フォルダ直下にある「自分以外のソリューション・プロジェクト」も拾う。生成物はそこへ同居する形になり、
/// 利用者が意図しない場所（既存リポジトリの直下など）を選んでいる可能性が高いサインだから。
/// なお最終ビルドは自分のソリューションを明示して実行するため、別ソリューションの同居自体はビルドを壊さない。
/// </para>
/// </remarks>
internal static class MockProjectOverwriteScanner
{
    /// <summary>出力フォルダ直下で「自分以外のソリューション・プロジェクト」として拾う拡張子パターン</summary>
    private static readonly string[] ForeignSolutionPatterns = ["*.sln", "*.slnx", "*.csproj"];

    /// <summary>直下だけを列挙するオプション（アクセス拒否は黙って飛ばす）</summary>
    /// <remarks>
    /// この走査は生成コマンドの実行中フラグを立てる前・try の外から呼ばれるため、素の
    /// <see cref="Directory.EnumerateFiles(string, string, SearchOption)"/> が投げる
    /// <see cref="UnauthorizedAccessException"/> がコマンドの未処理例外になる。読めないものは
    /// 「上書きの衝突として挙げられない」だけで、確認そのものを妨げる理由にはならない。
    /// </remarks>
    private static readonly EnumerationOptions TopLevelOptions = new()
    {
        RecurseSubdirectories = false,
        IgnoreInaccessible = true,
    };

    /// <summary>出力フォルダを走査し、既存のファイル・フォルダを表示順で洗い出す</summary>
    /// <param name="outputDirectory">出力フォルダ（存在しなければ空の結果）</param>
    /// <param name="projectName">プロジェクト名（プロジェクトフォルダ名・ソリューション名の由来）</param>
    internal static MockProjectOverwriteScan Scan(string outputDirectory, string projectName)
    {
        var existing = new List<string>();

        if (string.IsNullOrWhiteSpace(outputDirectory) || !Directory.Exists(outputDirectory))
        {
            return new MockProjectOverwriteScan(existing);
        }

        var solutionPath = MockProjectScaffoldService.GetSolutionFilePath(
            outputDirectory,
            projectName
        );
        var solutionFileName = Path.GetFileName(solutionPath);

        if (File.Exists(solutionPath))
        {
            existing.Add(solutionFileName);
        }

        // 出力フォルダ直下の別ソリューション・別プロジェクト（自分の .sln は上で出し済み）
        foreach (
            var foreign in ForeignSolutionPatterns
                .SelectMany(pattern =>
                    Directory.EnumerateFiles(outputDirectory, pattern, TopLevelOptions)
                )
                .Select(Path.GetFileName)
                .Where(name =>
                    name is not null
                    && !string.Equals(name, solutionFileName, StringComparison.OrdinalIgnoreCase)
                )
                .Select(name => name!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
        )
        {
            existing.Add(foreign);
        }

        var projectDirectory = MockProjectScaffoldService.GetProjectDirectory(
            outputDirectory,
            projectName
        );

        if (!Directory.Exists(projectDirectory))
        {
            return new MockProjectOverwriteScan(existing);
        }

        // プロジェクトフォルダ自体（中身が空でも「そこへ書き込む」ことは伝える）
        existing.Add(projectName + Path.DirectorySeparatorChar);

        // スキャフォールドが書き出すものと同名の既存物（表示順はスキャフォールドの書き出し順に合わせる）
        AddIfFile(existing, projectName, projectDirectory, $"{projectName}.csproj");
        AddIfFile(
            existing,
            projectName,
            projectDirectory,
            MockProjectScaffoldService.ReadmeFileName
        );
        AddIfDirectory(
            existing,
            projectName,
            projectDirectory,
            MockProjectScaffoldService.GeneratedFolderName
        );
        AddIfDirectory(
            existing,
            projectName,
            projectDirectory,
            MockProjectScaffoldService.DesignFolderRelativePath.Replace(
                '/',
                Path.DirectorySeparatorChar
            )
        );

        return new MockProjectOverwriteScan(existing);
    }

    /// <summary>プロジェクトフォルダ配下の相対パスが実ファイルなら一覧へ加える</summary>
    private static void AddIfFile(
        List<string> existing,
        string projectName,
        string projectDirectory,
        string relativePath
    )
    {
        if (File.Exists(Path.Combine(projectDirectory, relativePath)))
        {
            existing.Add(Path.Combine(projectName, relativePath));
        }
    }

    /// <summary>プロジェクトフォルダ配下の相対パスが実フォルダなら一覧へ加える（末尾に区切り記号を付ける）</summary>
    private static void AddIfDirectory(
        List<string> existing,
        string projectName,
        string projectDirectory,
        string relativePath
    )
    {
        if (Directory.Exists(Path.Combine(projectDirectory, relativePath)))
        {
            existing.Add(Path.Combine(projectName, relativePath) + Path.DirectorySeparatorChar);
        }
    }
}
