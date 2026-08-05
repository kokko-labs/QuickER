using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using QuickER.CodeGen.CSharp;
using QuickER.Model;
using QuickER.Provider;

namespace QuickER.AI.Mock;

/// <summary>決定的スキャフォールドの生成結果</summary>
/// <param name="SolutionFilePath">生成したソリューション（<c>{ProjectName}.sln</c>）の絶対パス（出力フォルダ直下）</param>
/// <param name="ProjectDirectory">プロジェクトフォルダ（<c>{出力フォルダ}/{ProjectName}/</c>）の絶対パス</param>
/// <param name="ProjectFilePath">生成した csproj の絶対パス（プロジェクトフォルダ配下）</param>
/// <param name="GeneratedDirectory">データ層コードを書き出した <c>Generated/</c> の絶対パス</param>
/// <param name="DesignFolderPath">デザイン仕様のモックフォルダ（<c>design/mock/</c>）の絶対パス</param>
/// <param name="ReadmePath">規約ドキュメント（<c>README-QuickER.md</c>）の絶対パス</param>
/// <param name="RepositoryDialect">QuickER 版 Repository を出力した方言（未出力なら null）</param>
/// <param name="WrittenFiles">書き出した全ファイルの絶対パス</param>
public sealed record MockProjectScaffoldResult(
    string SolutionFilePath,
    string ProjectDirectory,
    string ProjectFilePath,
    string GeneratedDirectory,
    string DesignFolderPath,
    string ReadmePath,
    string? RepositoryDialect,
    IReadOnlyList<string> WrittenFiles
);

/// <summary>
/// モックフォルダをデザイン仕様として、WPF モックプロジェクトの「決定的な土台」を出力フォルダへ書き出すサービス。
/// </summary>
/// <remarks>
/// <para>
/// 出力構成は Visual Studio 標準（ソリューション＋プロジェクトフォルダ）とする。ソリューションファイル
/// <c>{ProjectName}.sln</c> を出力フォルダ直下に、プロジェクト一式（csproj スケルトン・<c>README-QuickER.md</c>・
/// デザイン仕様のモックフォルダ <c>design/mock/</c>・データ層コード <c>Generated/</c>）を <c>{出力フォルダ}/{ProjectName}/</c> 配下へ書き出す。
/// データ層コードは Entity/EditModel/Mapper/InMemory＋図の方言が対応方言ならQuickER 版 Repository を Split 出力する。
/// UI 層（App/MainWindow/ビュー・ビューモデル）は AI（Claude Code）に書かせるため、ここでは生成しない。
/// </para>
/// <para>
/// WPF 型に依存しない純ロジックとし、型解決は <see cref="DiagramCodeGenerator"/>（プロバイダ層）へ委譲する。
/// 図の方言（<see cref="ErDiagram.TargetDbms"/>）を <see cref="DatabaseProviderRegistry"/> で解決して型マッパを得る。
/// </para>
/// </remarks>
public sealed class MockProjectScaffoldService
{
    /// <summary>データ層コードを配置するサブフォルダ名（読み取り専用の自動生成コード）</summary>
    public const string GeneratedFolderName = "Generated";

    /// <summary>デザイン仕様のモックフォルダの相対パス（<c>design/mock/</c> にモックフォルダの内容を同梱する）</summary>
    public const string DesignFolderRelativePath = "design/mock";

    /// <summary>規約ドキュメントのファイル名</summary>
    public const string ReadmeFileName = "README-QuickER.md";

    /// <summary>C# プロジェクトのソリューション種別 GUID（.sln の Project 行に埋め込む固定値）</summary>
    private const string CSharpProjectTypeGuid = "FAE04EC0-301F-11D3-BF4B-00C04F79EFBC";

    private readonly DatabaseProviderRegistry _providers;

    /// <summary>プロバイダレジストリを注入して生成する</summary>
    /// <param name="providers">図の方言・QuickER 版 Repository 方言の型マッパを解決するレジストリ</param>
    public MockProjectScaffoldService(DatabaseProviderRegistry providers)
    {
        _providers = providers;
    }

    /// <summary>
    /// 決定的スキャフォールドを出力フォルダへ書き出す。
    /// </summary>
    /// <param name="diagram">生成元の ER 図（意味モデル）</param>
    /// <param name="outputDirectory">出力フォルダ</param>
    /// <param name="projectName">プロジェクト名（csproj 名・ルート名前空間の由来。単一セグメント名であること＝<see cref="IsValidProjectName"/> 参照）</param>
    /// <param name="mockFolder">デザイン仕様として同梱するモックフォルダのパス（mock.json＋画面 HTML＋共有 style.css）</param>
    /// <param name="profile">生成ターゲットのプロファイル（csproj／README のターゲット差分の正本）</param>
    /// <exception cref="ArgumentException"><paramref name="projectName"/> が単一セグメント名として不正な場合</exception>
    /// <exception cref="InvalidOperationException">コード生成にエラーがある場合</exception>
    public MockProjectScaffoldResult Scaffold(
        ErDiagram diagram,
        string outputDirectory,
        string projectName,
        string mockFolder,
        MockProjectTargetProfile profile
    )
    {
        ArgumentNullException.ThrowIfNull(diagram);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectName);
        ArgumentException.ThrowIfNullOrWhiteSpace(mockFolder);
        ArgumentNullException.ThrowIfNull(profile);

        // プロジェクト名はプロジェクトフォルダ名・csproj 名・.sln 内の相対パスへそのまま使われるため、
        // 出力フォルダ外への書き込みを防ぐ目的で単一セグメント名として妥当か検証する
        if (!IsValidProjectName(projectName))
        {
            throw new ArgumentException(
                $"プロジェクト名にはパス区切り文字・絶対パス・\".\"/\"..\"・フォルダ名やファイル名に使用できない文字・前後の空白・末尾のピリオドを含められません: {projectName}",
                nameof(projectName)
            );
        }

        Directory.CreateDirectory(outputDirectory);
        var written = new List<string>();

        // ルート名前空間はプロジェクト名を C# 識別子として妥当な形へ正規化して使う
        var rootNamespace = SanitizeNamespace(projectName);
        var generatedNamespace = $"{rootNamespace}.Generated";

        // 図の方言が対応方言（sqlserver/sqlite）ならQuickER 版 Repository も出力する（対称に InMemory も出す）
        var repositoryDialect = ResolveRepositoryDialect(diagram.TargetDbms);

        // Visual Studio 標準構成: プロジェクト一式はプロジェクトフォルダ配下、ソリューションは出力フォルダ直下へ出す
        var projectDirectory = Path.Combine(outputDirectory, projectName);
        Directory.CreateDirectory(projectDirectory);

        var options = BuildOptions(generatedNamespace, repositoryDialect);
        var generatedDirectory = Path.Combine(projectDirectory, GeneratedFolderName);
        WriteGeneratedCode(diagram, options, repositoryDialect, generatedDirectory, written);

        var projectFilePath = Path.Combine(projectDirectory, $"{projectName}.csproj");
        WriteText(projectFilePath, profile.BuildCsproj(rootNamespace, repositoryDialect), written);

        var readmePath = Path.Combine(projectDirectory, ReadmeFileName);
        WriteText(
            readmePath,
            profile.BuildReadme(projectName, rootNamespace, repositoryDialect),
            written
        );

        // モックフォルダの内容（mock.json・画面 HTML・共有 style.css）を design/mock/ へフラットに同梱する
        var designFolderPath = Path.Combine(
            projectDirectory,
            DesignFolderRelativePath.Replace('/', Path.DirectorySeparatorChar)
        );
        CopyMockFolder(mockFolder, designFolderPath, written);

        // ソリューションはプロジェクトを 1 つ参照する最小構成で出力フォルダ直下へ出す（GUID は名前から決定的に導出）
        var solutionFilePath = Path.Combine(outputDirectory, $"{projectName}.sln");
        WriteText(solutionFilePath, BuildSolution(projectName), written);

        return new MockProjectScaffoldResult(
            SolutionFilePath: solutionFilePath,
            ProjectDirectory: projectDirectory,
            ProjectFilePath: projectFilePath,
            GeneratedDirectory: generatedDirectory,
            DesignFolderPath: designFolderPath,
            ReadmePath: readmePath,
            RepositoryDialect: repositoryDialect,
            WrittenFiles: written
        );
    }

    /// <summary>図の方言がQuickER 版 Repository の対応方言（sqlserver/sqlite）なら小文字方言名を、非対応なら null を返す</summary>
    private static string? ResolveRepositoryDialect(string? targetDbms)
    {
        if (string.IsNullOrWhiteSpace(targetDbms))
        {
            return null;
        }

        var match = CodeGenerationOptions.SupportedRepositoryDialects.FirstOrDefault(dialect =>
            string.Equals(dialect, targetDbms.Trim(), StringComparison.OrdinalIgnoreCase)
        );

        return match;
    }

    /// <summary>スキャフォールドのコード生成オプションを組み立てる（Split 出力・InMemory 必須・対応方言ならQuickER 版 Repository）</summary>
    private static CodeGenerationOptions BuildOptions(
        string generatedNamespace,
        string? repositoryDialect
    ) =>
        new()
        {
            RootNamespace = generatedNamespace,
            GenerateEditModels = true,
            GenerateMappers = true,
            GenerateInMemoryRepositories = true,
            GenerateRepositories = repositoryDialect is not null,
            RepositoryDialects = repositoryDialect is null ? null : [repositoryDialect],
            GenerateEfCore = false,
            IncludeDataAnnotations = true,
            SplitFilesByCategory = true,
        };

    /// <summary>型解決してコードを生成し、<c>Generated/</c> 配下へ書き出す</summary>
    private void WriteGeneratedCode(
        ErDiagram diagram,
        CodeGenerationOptions options,
        string? repositoryDialect,
        string generatedDirectory,
        List<string> written
    )
    {
        // 図の方言の型マッパ／型カタログを解決する。非対応方言でも Entity/EditModel/Mapper/InMemory は
        // 図の方言の型解決で生成できる（InMemory は方言非依存）。方言が未解決なら SQL Server を既定にする。
        var primaryProvider = ResolveProvider(diagram.TargetDbms);

        CodeGenerationResult result;

        if (repositoryDialect is null)
        {
            // QuickER 版 Repository を出さない場合は単一辞書の後方互換オーバーロードで足りる
            result = DiagramCodeGenerator.Generate(
                primaryProvider.TypeMapper,
                primaryProvider.TypeCatalog,
                diagram,
                options
            );
        }
        else
        {
            // QuickER 版 Repository の方言バケットは、その方言の型で解決した辞書を使う
            var dialectMappers = new Dictionary<string, IColumnTypeMapper>(
                StringComparer.OrdinalIgnoreCase
            );

            if (_providers.TryGet(repositoryDialect, out var dialectProvider))
            {
                dialectMappers[repositoryDialect] = dialectProvider.TypeMapper;
            }

            result = DiagramCodeGenerator.Generate(
                primaryProvider.TypeMapper,
                primaryProvider.TypeCatalog,
                dialectMappers,
                diagram,
                options
            );
        }

        if (result.HasErrors)
        {
            var message = string.Join(
                Environment.NewLine,
                result
                    .Diagnostics.Where(diagnostic =>
                        diagnostic.Severity == GenerationDiagnosticSeverity.Error
                    )
                    .Select(diagnostic => diagnostic.Message)
            );
            throw new InvalidOperationException(
                $"データ層コードの生成に失敗しました。{Environment.NewLine}{message}"
            );
        }

        var writer = new GeneratedFileWriter();
        written.AddRange(writer.WriteFiles(generatedDirectory, result));
    }

    /// <summary>図の方言に対応するプロバイダを解決する（未解決なら SQL Server を既定にする）</summary>
    private IDatabaseProvider ResolveProvider(string? targetDbms)
    {
        if (
            !string.IsNullOrWhiteSpace(targetDbms)
            && _providers.TryGet(targetDbms, out var provider)
        )
        {
            return provider;
        }

        return _providers.Get("sqlserver");
    }

    /// <summary>
    /// プロジェクトを 1 つ参照する最小のソリューションテキスト（Format Version 12.00）を組み立てる。
    /// </summary>
    /// <remarks>
    /// プロジェクト GUID はプロジェクト名から決定的に導出する（同名なら常に同 GUID＝テストが決定的になる）。
    /// Debug/Release × Any CPU の標準構成を含める。プロジェクトへの相対パスは <c>{ProjectName}\{ProjectName}.csproj</c>。
    /// 改行は Visual Studio 標準に合わせて CRLF で固定する。
    /// </remarks>
    private static string BuildSolution(string projectName)
    {
        // GUID は大文字・波括弧付きが VS の慣習
        var projectGuid = DeriveDeterministicProjectGuid(projectName)
            .ToString("B")
            .ToUpperInvariant();
        var typeGuid = "{" + CSharpProjectTypeGuid + "}";
        var projectRelativePath = $@"{projectName}\{projectName}.csproj";

        var lines = new[]
        {
            "Microsoft Visual Studio Solution File, Format Version 12.00",
            "# Visual Studio Version 17",
            $"Project(\"{typeGuid}\") = \"{projectName}\", \"{projectRelativePath}\", \"{projectGuid}\"",
            "EndProject",
            "Global",
            "\tGlobalSection(SolutionConfigurationPlatforms) = preSolution",
            "\t\tDebug|Any CPU = Debug|Any CPU",
            "\t\tRelease|Any CPU = Release|Any CPU",
            "\tEndGlobalSection",
            "\tGlobalSection(ProjectConfigurationPlatforms) = postSolution",
            $"\t\t{projectGuid}.Debug|Any CPU.ActiveCfg = Debug|Any CPU",
            $"\t\t{projectGuid}.Debug|Any CPU.Build.0 = Debug|Any CPU",
            $"\t\t{projectGuid}.Release|Any CPU.ActiveCfg = Release|Any CPU",
            $"\t\t{projectGuid}.Release|Any CPU.Build.0 = Release|Any CPU",
            "\tEndGlobalSection",
            "\tGlobalSection(SolutionProperties) = preSolution",
            "\t\tHideSolutionNode = FALSE",
            "\tEndGlobalSection",
            "EndGlobal",
        };

        return string.Join("\r\n", lines) + "\r\n";
    }

    /// <summary>プロジェクト名から決定的な GUID を導出する（MD5 ハッシュの先頭 16 バイトを Guid 化）</summary>
    /// <remarks>
    /// 暗号強度は不要（識別子の決定性が目的）。同名なら常に同一 GUID となり、テストが決定的になる。
    /// </remarks>
    private static Guid DeriveDeterministicProjectGuid(string projectName)
    {
        var hash = MD5.HashData(Encoding.UTF8.GetBytes(projectName));
        return new Guid(hash);
    }

    /// <summary>
    /// プロジェクト名が単一セグメントのフォルダ／ファイル名として妥当か判定する。
    /// </summary>
    /// <remarks>
    /// プロジェクト名はそのまま <c>{出力フォルダ}/{ProjectName}/</c>（プロジェクトフォルダ名）・
    /// <c>{ProjectName}.csproj</c>・.sln 内の相対パスへ使われるため、出力フォルダ外への書き込みを防ぐ目的で検証する。
    /// パス区切り文字・絶対パス・<c>"."</c>/<c>".."</c>・<see cref="Path.GetInvalidFileNameChars"/> に含まれる文字を拒否する
    /// （検証水準は <see cref="MockFolderStore"/> の画面ファイル名検証に合わせる。Windows 予約名 (<c>CON</c> 等) は
    /// 画面ファイル名検証と同様に対象外＝同水準）。加えて前後の空白・末尾のピリオドも拒否する
    /// （Windows がフォルダ名の末尾空白・ピリオドを切り捨てるため、指定名と実フォルダ名が食い違って
    /// 途中の書き込みが失敗し、生成物の残骸だけが残るのを防ぐ）。第2ステップの生成可否判定（<see cref="MockGenerationDialogViewModel"/>）と
    /// <see cref="Scaffold"/> の入力検証の双方から呼ばれる共有ロジック。
    /// </remarks>
    internal static bool IsValidProjectName(string? projectName)
    {
        if (string.IsNullOrWhiteSpace(projectName))
        {
            return false;
        }

        if (
            projectName.Contains('/', StringComparison.Ordinal)
            || projectName.Contains('\\', StringComparison.Ordinal)
            || Path.IsPathRooted(projectName)
        )
        {
            return false;
        }

        if (projectName is "." or "..")
        {
            return false;
        }

        // 末尾の空白・ピリオドは Windows のフォルダ作成で切り捨てられ、指定名と実フォルダ名が食い違う
        // （末尾空白では途中の書き込みが失敗して残骸を残す）。先頭の空白も同じ流儀で一貫して弾く
        if (projectName != projectName.Trim() || projectName.EndsWith('.'))
        {
            return false;
        }

        return projectName.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;
    }

    /// <summary>プロジェクト名を C# 名前空間として妥当な形へ正規化する（識別子でない文字を除去・数字始まりを回避）</summary>
    private static string SanitizeNamespace(string projectName)
    {
        var builder = new StringBuilder(projectName.Length);
        var lastWasDot = false;

        foreach (var ch in projectName.Trim())
        {
            if (ch == '.')
            {
                // 連続ドット・先頭ドットは畳む
                if (builder.Length > 0 && !lastWasDot)
                {
                    builder.Append('.');
                    lastWasDot = true;
                }

                continue;
            }

            if (char.IsLetterOrDigit(ch) || ch == '_')
            {
                builder.Append(ch);
                lastWasDot = false;
            }
            // それ以外（空白・ハイフン等）はセグメント区切りにせず単に除去する
        }

        var result = builder.ToString().Trim('.');

        if (result.Length == 0)
        {
            return "MockApp";
        }

        // 各セグメントが数字始まりなら接頭辞を付けて識別子違反を避ける
        var segments = result
            .Split('.', StringSplitOptions.RemoveEmptyEntries)
            .Select(segment => char.IsDigit(segment[0]) ? "_" + segment : segment);

        return string.Join('.', segments);
    }

    /// <summary>
    /// モックフォルダ直下のファイル（mock.json・画面 HTML・共有 style.css 等）を同梱先へフラットにコピーする。
    /// </summary>
    /// <remarks>
    /// モックフォルダはフラット構成（サブフォルダなし）が前提のため、直下のファイルのみを対象にする。
    /// 同名ファイルは上書きし、コピーしたパスを書き出し記録へ加える。
    /// </remarks>
    private static void CopyMockFolder(string mockFolder, string designFolder, List<string> written)
    {
        Directory.CreateDirectory(designFolder);

        foreach (var source in Directory.EnumerateFiles(mockFolder))
        {
            var destination = Path.Combine(designFolder, Path.GetFileName(source));
            File.Copy(source, destination, overwrite: true);
            written.Add(destination);
        }
    }

    /// <summary>テキストをフォルダ作成込みで BOM なし UTF-8 で書き出し、書き出しパスを記録する</summary>
    private static void WriteText(string path, string content, List<string> written)
    {
        var directory = Path.GetDirectoryName(path);

        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(path, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        written.Add(path);
    }
}
