using System.IO;
using System.Linq;

namespace QuickER.AI.Mock;

/// <summary>出力フォルダの走査結果（スキャフォールドを実行する前に利用者へ見せる判断材料）</summary>
/// <param name="ExistingPaths">
/// 出力フォルダにある既存のファイル・フォルダの相対パス（表示順は固定）。スキャフォールドが
/// 書き出すものと同名のものと、同じ場所にある別のソリューション・プロジェクトの両方を含む。
/// 出力フォルダの中を列挙できなければ、出力フォルダ自身を <c>.\</c> の 1 件として含む。
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
/// 隠し属性・システム属性のものも拾う（属性で確認から外れる同居物を作らない）。
/// </para>
/// <para>
/// 出力フォルダの中を列挙できない（一覧の読み取りを拒否する ACL・中を列挙できない reparse フォルダ）ときは、
/// 例外にせず、出力フォルダ自身を <see cref="UnlistableOutputFolderEntry"/> の 1 件として一覧に出す。
/// この走査は生成コマンドの実行中フラグを立てる前・try の外から呼ばれるので、投げるとコマンドの未処理例外になる。
/// かといって黙って空にすると、中にある別のソリューション・プロジェクトを見落としたまま確認なしに進む。
/// 中を確かめられないものは確認に出す（最終ビルド前の変更検知が、一覧を読めないフォルダを毎回出すのと同じ向き）。
/// </para>
/// </remarks>
internal static class MockProjectOverwriteScanner
{
    /// <summary>出力フォルダ直下で「自分以外のソリューション・プロジェクト」として拾う拡張子パターン</summary>
    private static readonly string[] ForeignSolutionPatterns = ["*.sln", "*.slnx", "*.csproj"];

    /// <summary>中を列挙できない出力フォルダ自身を表す一覧の 1 件（出力フォルダからの相対パスとしての「.」）</summary>
    internal static readonly string UnlistableOutputFolderEntry = "." + Path.DirectorySeparatorChar;

    /// <summary>直下だけを列挙するオプション（属性で飛ばさない・列挙できなければ例外にして受け止める）</summary>
    /// <remarks>
    /// 既定の <see cref="EnumerationOptions.AttributesToSkip"/>（Hidden / System）は、隠しの別ソリューションを確認から外すので 0 にする。
    /// <see cref="EnumerationOptions.IgnoreInaccessible"/> を true にすると起点のアクセス拒否まで黙って空になるので false にし、
    /// 列挙の失敗は <see cref="TryListForeignSolutions"/> が受け止める（直下だけなので、効くのは起点の失敗だけ）。
    /// </remarks>
    private static readonly EnumerationOptions TopLevelOptions = new()
    {
        RecurseSubdirectories = false,
        IgnoreInaccessible = false,
        AttributesToSkip = 0,
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

        // 出力フォルダ直下の別ソリューション・別プロジェクト（自分の .sln は上で出し済み）。
        // 中を列挙できなければ、出力フォルダ自身を 1 件として出す（読めた分は捨てる）
        existing.AddRange(
            TryListForeignSolutions(outputDirectory, solutionFileName)
                ?? [UnlistableOutputFolderEntry]
        );

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

    /// <summary>
    /// 出力フォルダ直下の自分以外のソリューション・プロジェクトのファイル名を表示順で返す。
    /// 中を列挙できなければ（アクセス拒否・列挙できない reparse フォルダ）null を返す。
    /// </summary>
    private static List<string>? TryListForeignSolutions(
        string outputDirectory,
        string solutionFileName
    )
    {
        try
        {
            return ForeignSolutionPatterns
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
                .ToList();
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
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
