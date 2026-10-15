using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using QuickER.CodeGen.CSharp;
using QuickER.Model;
using QuickER.Platform;
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
/// モックフォルダをデザイン仕様として、モックプロジェクト（WPF / Blazor）の「決定的な土台」を出力フォルダへ書き出すサービス。
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
/// UI フレームワークの型に依存しない純ロジックとし、型解決は <see cref="DiagramCodeGenerator"/>（プロバイダ層）へ委譲する。
/// 図の方言（<see cref="ErDiagram.TargetDbms"/>）を <see cref="DatabaseProviderRegistry"/> で解決して型マッパを得る。
/// </para>
/// </remarks>
public sealed class MockProjectScaffoldService
{
    /// <summary>データ層コードを配置するサブフォルダ名（読み取り専用の自動生成コード）</summary>
    public const string GeneratedFolderName = "Generated";

    /// <summary>デザイン仕様を置く最上位フォルダ名（API キー方式はこの配下への提出を拒否する）</summary>
    public const string DesignRootFolderName = "design";

    /// <summary>デザイン仕様のモックフォルダの相対パス（<c>design/mock/</c> にモックフォルダの内容を同梱する）</summary>
    public const string DesignFolderRelativePath = DesignRootFolderName + "/mock";

    /// <summary>規約ドキュメントのファイル名</summary>
    public const string ReadmeFileName = "README-QuickER.md";

    /// <summary>C# プロジェクトのソリューション種別 GUID（.sln の Project 行に埋め込む固定値）</summary>
    private const string CSharpProjectTypeGuid = "FAE04EC0-301F-11D3-BF4B-00C04F79EFBC";

    /// <summary>
    /// プロジェクトフォルダ（<c>{出力フォルダ}/{プロジェクト名}/</c>）のパスを返す。
    /// </summary>
    /// <remarks>
    /// 出力レイアウトの正本。<see cref="Scaffold"/> 自身に加えて、成果物の探索範囲（<see cref="MockProjectAgentRunner"/>・
    /// 各エージェント）と上書き確認（<see cref="MockProjectOverwriteScanner"/>）が同じ規則を共有するため、
    /// 組み立てをここ 1 箇所に置く。
    /// </remarks>
    public static string GetProjectDirectory(string outputDirectory, string projectName) =>
        Path.Combine(outputDirectory, projectName);

    /// <summary>
    /// ソリューションファイル（<c>{出力フォルダ}/{プロジェクト名}.sln</c>）のパスを返す。
    /// </summary>
    /// <remarks>
    /// <see cref="GetProjectDirectory"/> と同じく出力レイアウトの正本。最終ビルドはこのパスを明示して
    /// 実行する（フォルダ指定のままだと、出力フォルダに別のソリューションがあるときに MSB1011 で落ちる）。
    /// </remarks>
    public static string GetSolutionFilePath(string outputDirectory, string projectName) =>
        Path.Combine(outputDirectory, $"{projectName}.sln");

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
        if (CheckProjectName(projectName) is { } violation)
        {
            throw new ArgumentException(
                $"The project name must be a single folder name ({PathSegments.Describe(violation, System.Globalization.CultureInfo.InvariantCulture)}): {projectName}",
                nameof(projectName)
            );
        }

        // 書き直す前に空にするフォルダ（Generated/・design/mock/）が、コピー元のモックフォルダと重なっていたら
        // 何も書かずに止める。前回の出力の design/mock/ をそのままモックフォルダに指定する使い方では、
        // 先に消すとコピー元（利用者の作業の正本）が失われる。
        var projectDirectory = GetProjectDirectory(outputDirectory, projectName);
        var generatedDirectory = Path.Combine(projectDirectory, GeneratedFolderName);
        var designFolderPath = Path.Combine(
            projectDirectory,
            DesignFolderRelativePath.Replace('/', Path.DirectorySeparatorChar)
        );
        EnsureDoesNotOverlapMockFolder(mockFolder, generatedDirectory);
        EnsureDoesNotOverlapMockFolder(mockFolder, designFolderPath);

        Directory.CreateDirectory(outputDirectory);
        var written = new List<string>();

        // ルート名前空間はプロジェクト名を C# 識別子として妥当な形へ正規化して使う
        var rootNamespace = SanitizeNamespace(projectName);
        var generatedNamespace = $"{rootNamespace}.Generated";

        // 図の方言が対応方言（sqlserver/sqlite）ならQuickER 版 Repository も出力する（対称に InMemory も出す）
        var repositoryDialect = ResolveRepositoryDialect(diagram.TargetDbms);

        // Visual Studio 標準構成: プロジェクト一式はプロジェクトフォルダ配下、ソリューションは出力フォルダ直下へ出す
        Directory.CreateDirectory(projectDirectory);

        var options = BuildOptions(generatedNamespace, repositoryDialect);
        WriteGeneratedCode(diagram, options, repositoryDialect, generatedDirectory, written);

        var projectFilePath = Path.Combine(projectDirectory, $"{projectName}.csproj");
        WriteText(projectFilePath, profile.BuildCsproj(rootNamespace, repositoryDialect), written);

        var readmePath = Path.Combine(projectDirectory, ReadmeFileName);
        WriteText(
            readmePath,
            profile.BuildReadme(projectName, rootNamespace, repositoryDialect),
            written
        );

        // モックフォルダの内容（mock.json・画面 HTML・共有 style.css）を design/mock/ へフラットに同梱する。
        // 前回の同梱物を先に空にする（削除した画面の HTML が残ると、AI が無い画面まで再現する）
        ClearOwnedDirectory(designFolderPath);
        CopyMockFolder(mockFolder, designFolderPath, written);

        // ソリューションはプロジェクトを 1 つ参照する最小構成で出力フォルダ直下へ出す（GUID は名前から決定的に導出）
        var solutionFilePath = GetSolutionFilePath(outputDirectory, projectName);
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
            GenerateEfCoreRepositories = false,
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
                $"Generating the data-layer code failed.{Environment.NewLine}{message}"
            );
        }

        // 前回の生成物を先に空にする（別方言で生成した Repositories.Sqlite.g.cs のような残骸が残ると、
        // 型が二重に定義されてコンパイルできない）。生成に失敗したときは消さない＝ここは生成が通った後
        ClearOwnedDirectory(generatedDirectory);

        var writer = new GeneratedFileWriter();
        written.AddRange(writer.WriteFiles(generatedDirectory, result));
    }

    /// <summary>
    /// スキャフォールドが所有するフォルダ（<c>Generated/</c>・<c>design/mock/</c>）を書き直す前に空にする。
    /// 消しきれなければ例外で止める（前回の残骸の上に書くと、残骸と新しい生成物が混ざる）。
    /// </summary>
    /// <remarks>
    /// 読み取り専用の属性を外してから消し、リンクはリンクだけを外す（リンク先の中身には触れない）。
    /// どちらのフォルダも上書き確認（<see cref="MockProjectOverwriteScanner"/>）の一覧に出しており、
    /// README で「手で編集しない」と宣言している。
    /// </remarks>
    private static void ClearOwnedDirectory(string directory)
    {
        MockProjectFiles.TryDeleteDirectory(directory);

        if (Directory.Exists(directory))
        {
            throw new IOException(
                $"Could not clear '{directory}' before regenerating it. Close any program that is using the files there and try again."
            );
        }
    }

    /// <summary>
    /// 空にするフォルダとコピー元のモックフォルダが同じか、どちらかがもう一方の中にあれば例外で止める。
    /// </summary>
    /// <remarks>
    /// 判定は <see cref="MockProjectFiles.PathsOverlap"/>（フルパスへ正規化・途中のリンクは実体まで解決・
    /// 大文字小文字を区別しない・解決できなければ重なる扱い）。
    /// </remarks>
    private static void EnsureDoesNotOverlapMockFolder(string mockFolder, string ownedDirectory)
    {
        if (MockProjectFiles.PathsOverlap(mockFolder, ownedDirectory))
        {
            throw new InvalidOperationException(
                $"The mock folder '{mockFolder}' overlaps '{ownedDirectory}', which the generation clears and rewrites. "
                    + "Choose a different output folder, or copy the mock folder somewhere else first."
            );
        }
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
    /// プロジェクト名を単一セグメントのフォルダ／ファイル名として断る理由を返す（断らないなら <c>null</c>）。
    /// </summary>
    /// <remarks>
    /// プロジェクト名はそのまま <c>{出力フォルダ}/{ProjectName}/</c>（プロジェクトフォルダ名）・
    /// <c>{ProjectName}.csproj</c>・.sln 内の相対パスへ使われるため、出力フォルダ外への書き込みと、Windows が名前を
    /// 書き換えて（末尾の空白・'.' を落とす・予約名をデバイスとして開く）指定名と実フォルダ名が食い違うのを防ぐ。
    /// 判定の正本は <see cref="PathSegments.CheckSegment"/>。第2ステップの生成可否判定（<see cref="MockGenerationDialogViewModel"/>）と
    /// <see cref="Scaffold"/> の入力検証の双方から呼ばれる。
    /// </remarks>
    internal static PathSegmentViolation? CheckProjectName(string? projectName) =>
        PathSegments.CheckSegment(projectName ?? string.Empty);

    /// <summary>プロジェクト名が単一セグメントのフォルダ／ファイル名として妥当か判定する（<see cref="CheckProjectName"/>）</summary>
    internal static bool IsValidProjectName(string? projectName) =>
        CheckProjectName(projectName) is null;

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
            AtomicFile.WriteAllBytes(destination, File.ReadAllBytes(source));
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

        AtomicFile.WriteAllText(path, content, TextEncodings.Utf8NoBomLenient);
        written.Add(path);
    }
}
