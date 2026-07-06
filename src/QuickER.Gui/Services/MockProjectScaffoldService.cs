using System.IO;
using System.Linq;
using System.Text;
using QuickER.Generator;
using QuickER.Model;
using QuickER.Provider;

namespace QuickER.Services;

/// <summary>決定的スキャフォールドの生成結果</summary>
/// <param name="ProjectFilePath">生成した csproj の絶対パス</param>
/// <param name="GeneratedDirectory">データ層コードを書き出した <c>Generated/</c> の絶対パス</param>
/// <param name="DesignHtmlPath">デザイン仕様 HTML（<c>design/mock.html</c>）の絶対パス</param>
/// <param name="ReadmePath">規約ドキュメント（<c>README-QuickER.md</c>）の絶対パス</param>
/// <param name="RepositoryDialect">自作 Repository を出力した方言（未出力なら null）</param>
/// <param name="WrittenFiles">書き出した全ファイルの絶対パス</param>
public sealed record MockProjectScaffoldResult(
    string ProjectFilePath,
    string GeneratedDirectory,
    string DesignHtmlPath,
    string ReadmePath,
    string? RepositoryDialect,
    IReadOnlyList<string> WrittenFiles
);

/// <summary>
/// 確定 HTML をデザイン仕様として、WPF モックプロジェクトの「決定的な土台」を出力フォルダへ書き出すサービス。
/// </summary>
/// <remarks>
/// <para>
/// 生成物: データ層コード（Entity/EditModel/Mapper/InMemory＋図の方言が対応方言なら自作 Repository・Split 出力）を
/// <c>Generated/</c> へ、csproj スケルトン・<c>README-QuickER.md</c>・<c>design/mock.html</c> をフォルダ直下へ書き出す。
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

    /// <summary>デザイン仕様 HTML の相対パス</summary>
    public const string DesignHtmlRelativePath = "design/mock.html";

    /// <summary>規約ドキュメントのファイル名</summary>
    public const string ReadmeFileName = "README-QuickER.md";

    private readonly DatabaseProviderRegistry _providers;

    /// <summary>プロバイダレジストリを注入して生成する</summary>
    /// <param name="providers">図の方言・自作 Repository 方言の型マッパを解決するレジストリ</param>
    public MockProjectScaffoldService(DatabaseProviderRegistry providers)
    {
        _providers = providers;
    }

    /// <summary>
    /// 決定的スキャフォールドを出力フォルダへ書き出す。
    /// </summary>
    /// <param name="diagram">生成元の ER 図（意味モデル）</param>
    /// <param name="outputDirectory">出力フォルダ</param>
    /// <param name="projectName">プロジェクト名（csproj 名・ルート名前空間の由来）</param>
    /// <param name="designHtml">デザイン仕様として同梱する確定 HTML</param>
    /// <exception cref="InvalidOperationException">コード生成にエラーがある場合</exception>
    public MockProjectScaffoldResult Scaffold(
        ErDiagram diagram,
        string outputDirectory,
        string projectName,
        string designHtml
    )
    {
        ArgumentNullException.ThrowIfNull(diagram);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectName);
        ArgumentNullException.ThrowIfNull(designHtml);

        Directory.CreateDirectory(outputDirectory);
        var written = new List<string>();

        // ルート名前空間はプロジェクト名を C# 識別子として妥当な形へ正規化して使う
        var rootNamespace = SanitizeNamespace(projectName);
        var generatedNamespace = $"{rootNamespace}.Generated";

        // 図の方言が対応方言（sqlserver/sqlite）なら自作 Repository も出力する（対称に InMemory も出す）
        var repositoryDialect = ResolveRepositoryDialect(diagram.TargetDbms);

        var options = BuildOptions(generatedNamespace, repositoryDialect);
        var generatedDirectory = Path.Combine(outputDirectory, GeneratedFolderName);
        WriteGeneratedCode(diagram, options, repositoryDialect, generatedDirectory, written);

        var projectFilePath = Path.Combine(outputDirectory, $"{projectName}.csproj");
        WriteText(projectFilePath, BuildCsproj(rootNamespace, repositoryDialect), written);

        var readmePath = Path.Combine(outputDirectory, ReadmeFileName);
        WriteText(readmePath, BuildReadme(projectName, rootNamespace, repositoryDialect), written);

        var designHtmlPath = Path.Combine(
            outputDirectory,
            DesignHtmlRelativePath.Replace('/', Path.DirectorySeparatorChar)
        );
        WriteText(designHtmlPath, designHtml, written);

        return new MockProjectScaffoldResult(
            ProjectFilePath: projectFilePath,
            GeneratedDirectory: generatedDirectory,
            DesignHtmlPath: designHtmlPath,
            ReadmePath: readmePath,
            RepositoryDialect: repositoryDialect,
            WrittenFiles: written
        );
    }

    /// <summary>図の方言が自作 Repository の対応方言（sqlserver/sqlite）なら小文字方言名を、非対応なら null を返す</summary>
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

    /// <summary>スキャフォールドのコード生成オプションを組み立てる（Split 出力・InMemory 必須・対応方言なら自作 Repository）</summary>
    private static CodeGenerationOptions BuildOptions(
        string generatedNamespace,
        string? repositoryDialect
    ) =>
        new()
        {
            NamespaceName = generatedNamespace,
            GenerateEntityClasses = true,
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
            // 自作 Repository を出さない場合は単一辞書の後方互換オーバーロードで足りる
            result = DiagramCodeGenerator.Generate(
                primaryProvider.TypeMapper,
                primaryProvider.TypeCatalog,
                diagram,
                options
            );
        }
        else
        {
            // 自作 Repository の方言バケットは、その方言の型で解決した辞書を使う
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

    /// <summary>csproj スケルトンを組み立てる（WPF・net10.0-windows・必要な PackageReference）</summary>
    private static string BuildCsproj(string rootNamespace, string? repositoryDialect)
    {
        var adoPackage = repositoryDialect switch
        {
            "sqlserver" =>
                "    <PackageReference Include=\"Microsoft.Data.SqlClient\" Version=\"7.0.1\" />\n",
            "sqlite" =>
                "    <PackageReference Include=\"Microsoft.Data.Sqlite\" Version=\"10.0.0\" />\n",
            _ => string.Empty,
        };

        return $@"<Project Sdk=""Microsoft.NET.Sdk"">

  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net10.0-windows</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <UseWPF>true</UseWPF>
    <RootNamespace>{rootNamespace}</RootNamespace>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include=""CommunityToolkit.Mvvm"" Version=""8.4.2"" />
    <PackageReference Include=""Microsoft.Extensions.DependencyInjection"" Version=""10.0.0"" />
{adoPackage}  </ItemGroup>

</Project>
";
    }

    /// <summary>AI・人間の双方向けの規約ドキュメントを組み立てる</summary>
    private static string BuildReadme(
        string projectName,
        string rootNamespace,
        string? repositoryDialect
    )
    {
        // 単一方言生成では方言接尾辞なしの AddGeneratedRepositories(接続文字列) を出す（マルチターゲット時のみ方言別）
        var dialectSwitchGuide = repositoryDialect switch
        {
            "sqlserver" =>
                "3. 実 DB（SQL Server）へ切り替えるには、`AddGeneratedInMemoryRepositories()` を "
                    + "`AddGeneratedRepositories(接続文字列)` に差し替えます。",
            "sqlite" =>
                "3. 実 DB（SQLite）へ切り替えるには、`AddGeneratedInMemoryRepositories()` を "
                    + "`AddGeneratedRepositories(接続文字列)` に差し替えます。",
            _ =>
                "3. 実 DB へ切り替える場合は、QuickER で対応方言（SQL Server / SQLite）の自作 Repository を"
                    + "生成し直し、`AddGeneratedInMemoryRepositories()` を対応する DI 登録へ差し替えます。",
        };

        return $@"# {projectName}

QuickER が確定 HTML モックから生成した WPF モックプロジェクトです。
データ層（`Generated/` 配下）は QuickER が決定的に生成しており、UI 層（App / MainWindow / ビュー・ビューモデル）は
`design/mock.html` のデザイン仕様に沿って実装します。

## プロジェクト構成

- `Generated/` … QuickER が生成したデータ層（Entity / EditModel / Mapper / Repository 契約・実装 / インメモリ実装）。
  **このフォルダは自動生成コードのため、手で編集・削除しないでください（再生成で上書きされます）。**
- `design/mock.html` … 再現すべき画面のデザイン仕様（画面構成・項目・遷移）。
- `{projectName}.csproj` … WPF（net10.0-windows）のプロジェクトファイル。

## 実装の規約

- UI は **CommunityToolkit.Mvvm** を用いた MVVM（`ObservableObject` / `RelayCommand` / `[ObservableProperty]`）で実装します。
- データアクセスは `Generated/` の **`I{{Entity}}Repository`** を DI 経由で受け取って使います
  （リポジトリの具象を直接 `new` しないでください）。
- 画面は `design/mock.html` の構成・項目・遷移を WPF のネイティブ UI で忠実に再現します
  （HTML をそのまま埋め込むのではなく、WPF のウィンドウ／ページ／ユーザーコントロールへ作り直します）。

## 起動時の DI 登録

`App` の起動で `Microsoft.Extensions.DependencyInjection` のコンテナを構成し、`{rootNamespace}.Generated` の
**`AddGeneratedInMemoryRepositories()`** を呼びます（サンプルデータ入りのインメモリ実装が登録され、実 DB なしで動作します）。

```csharp
var services = new ServiceCollection();
services.AddGeneratedInMemoryRepositories(seedSampleData: true);
// ビュー・ビューモデルを登録
var provider = services.BuildServiceProvider();
```

## 実 DB への切り替え手順

1. QuickER の DDL 生成機能で対象 DB のスキーマ（DDL）を出力し、DB に適用します。
2. 接続文字列を用意します。
{dialectSwitchGuide}
";
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
