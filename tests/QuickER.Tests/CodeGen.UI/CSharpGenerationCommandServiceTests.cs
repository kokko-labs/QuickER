using System.IO;
using AwesomeAssertions;
using QuickER.CodeGen.CSharp;
using QuickER.CodeGen.UI;
using QuickER.Model;
using QuickER.Provider;
using QuickER.Provider.SqlServer;
using QuickER.Tests.TestDoubles;
using CodeGenStrings = QuickER.CodeGen.UI.Resources.Strings;

namespace QuickER.Tests.CodeGen.UI;

/// <summary>
/// <see cref="CSharpGenerationCommandService"/> の結果提示が、情報／エラーの詳細ダイアログ
/// （<c>ShowInformationDetails</c> / <c>ShowErrorDetails</c>）へ正しく振り分けられることを検証するテストクラス。
/// </summary>
/// <remarks>
/// <c>MainViewModel</c> 由来の旧テスト（MainViewModelCSharpGenerationTests）を、
/// フィーチャーモジュール側サービスへ移植したもの。ホストは <see cref="StubErDiagramHost"/>、
/// ダイアログ提示はテスト内フェイクに差し替える。resx 期待値は CodeGen.UI の厳密型アクセサ経由。
/// </remarks>
public class CSharpGenerationCommandServiceTests
{
    /// <summary>SQL Server プロバイダのみを登録したレジストリ（図の TargetDbms 解決に使う）</summary>
    private static DatabaseProviderRegistry SqlServerRegistry() =>
        new(new IDatabaseProvider[] { new SqlServerProvider() });

    /// <summary>PK 列を 1 つ持つ有効な図（生成が成功する最小構成）</summary>
    private static ErDiagram DiagramWithEntity() =>
        new()
        {
            TargetDbms = SqlServerProvider.ProviderName,
            Entities =
            {
                new Entity
                {
                    TableName = "NewTable",
                    Columns =
                    {
                        new Column
                        {
                            Name = "ID",
                            DataType = "int",
                            IsPrimaryKey = true,
                            IsNullable = false,
                        },
                    },
                },
            },
        };

    /// <summary>エンティティを持たない図（NoEntities のエラー診断を発生させる）</summary>
    private static ErDiagram EmptyDiagram() =>
        new() { TargetDbms = SqlServerProvider.ProviderName };

    /// <summary>成功かつ詳細（PackageReference 案内）がある場合は、要約＋詳細の情報ダイアログで提示される</summary>
    [Fact(
        DisplayName = "生成成功・詳細ありは ShowInformationDetails（message=完了・details に案内）"
    )]
    public void Run_SuccessWithDetails_ShowsInformationDetails()
    {
        using var output = new TempOutputDirectory();
        var dialogs = new StubDialogService();
        var host = new StubErDiagramHost
        {
            DiagramToReturn = DiagramWithEntity(),
            ProvidersToReturn = SqlServerRegistry(),
        };
        var presenter = new FakeCSharpPresenter(
            new CSharpGenerationDialogResult(
                new CodeGenerationOptions
                {
                    RootNamespace = "Sample.Domain",
                    // 固定コードをパッケージ参照へ切り替えると、PackageReference 案内が「詳細」に載る
                    UseRuntimePackages = true,
                },
                output.Path
            )
        );
        var service = new CSharpGenerationCommandService(host, dialogs, presenter);

        service.Run();

        // 詳細ダイアログへ振り分けられ、単文の ShowInformation は使われない
        dialogs.InformationMessages.Should().BeEmpty();
        var entry = dialogs.InformationDetailsMessages.Should().ContainSingle().Subject;
        entry.Message.Should().Be(CodeGenStrings.Csharp_GeneratedSuccess);
        entry.Title.Should().Be(CodeGenStrings.Common_Complete);
        // 詳細には PackageReference 案内（<PackageReference ...>）が含まれる
        entry.Details.Should().Contain("PackageReference");
        // プロバイダは図の TargetDbms からレジストリで解決してダイアログへ渡している
        presenter.LastProvider.Should().BeOfType<SqlServerProvider>();
    }

    /// <summary>診断ゼロ・パッケージ案内なしでも、書き出したファイルの一覧を詳細ダイアログで提示する</summary>
    [Fact(DisplayName = "生成成功・診断なしでも ShowInformationDetails に出力ファイル一覧を載せる")]
    public void Run_SuccessWithoutDiagnostics_ShowsWrittenFiles()
    {
        using var output = new TempOutputDirectory();
        var dialogs = new StubDialogService();
        var host = new StubErDiagramHost
        {
            DiagramToReturn = DiagramWithEntity(),
            ProvidersToReturn = SqlServerRegistry(),
        };
        var presenter = new FakeCSharpPresenter(
            new CSharpGenerationDialogResult(
                new CodeGenerationOptions
                {
                    RootNamespace = "Sample.Domain",
                    OutputFileName = "Sample.g.cs",
                },
                output.Path
            )
        );
        var service = new CSharpGenerationCommandService(host, dialogs, presenter);

        service.Run();

        dialogs.InformationMessages.Should().BeEmpty();
        var entry = dialogs.InformationDetailsMessages.Should().ContainSingle().Subject;
        entry.Message.Should().Be(CodeGenStrings.Csharp_GeneratedSuccess);
        entry
            .Details.Should()
            .StartWith(CodeGenStrings.Csharp_OutputFilesHeader)
            .And.Contain(Path.Combine(output.Path, "Sample.g.cs"));
    }

    /// <summary>
    /// 生成コードと API リファレンスが別のサブフォルダへ出る構成で、両方の実際の書き出し先が一覧に載る
    /// </summary>
    [Fact(DisplayName = "出力ファイル一覧はサブフォルダ込みの実際の書き出し先を示す")]
    public void Run_WithSubdirectories_ListsActualWrittenPaths()
    {
        using var output = new TempOutputDirectory();
        var dialogs = new StubDialogService();
        var host = new StubErDiagramHost
        {
            DiagramToReturn = DiagramWithEntity(),
            ProvidersToReturn = SqlServerRegistry(),
        };
        var presenter = new FakeCSharpPresenter(
            new CSharpGenerationDialogResult(
                new CodeGenerationOptions
                {
                    RootNamespace = "Sample.Domain",
                    OutputFileName = "Sample.g.cs",
                    CodeSubdirectory = "Generated",
                    GenerateApiDocs = true,
                    ApiDocsLanguage = ApiDocsLanguage.Both,
                    ApiDocsSubdirectory = "docs",
                },
                output.Path
            )
        );
        var service = new CSharpGenerationCommandService(host, dialogs, presenter);

        service.Run();

        var expected = new[]
        {
            Path.Combine(output.Path, "Generated", "Sample.g.cs"),
            Path.Combine(output.Path, "docs", "Sample.g.md"),
            Path.Combine(output.Path, "docs", "Sample.ja.g.md"),
        };
        var details = dialogs.InformationDetailsMessages.Should().ContainSingle().Subject.Details;

        foreach (var path in expected)
        {
            // 一覧に載ったパスは実在するファイルそのもの（書き出し結果から組み立てている）
            File.Exists(path).Should().BeTrue();
            details.Should().Contain(path);
        }
    }

    /// <summary>生成エラー時は、導入文（message）と診断一覧（details）を分けてエラー詳細ダイアログで提示する</summary>
    [Fact(DisplayName = "生成エラーは ShowErrorDetails（message=導入文・details に診断）")]
    public void Run_Error_ShowsErrorDetails()
    {
        var dialogs = new StubDialogService();
        var host = new StubErDiagramHost
        {
            // エンティティを 1 つも持たない空の図でエラー診断（NoEntities）を発生させる
            DiagramToReturn = EmptyDiagram(),
            ProvidersToReturn = SqlServerRegistry(),
        };
        var presenter = new FakeCSharpPresenter(
            new CSharpGenerationDialogResult(
                new CodeGenerationOptions { RootNamespace = "Sample.Domain" },
                Path.GetTempPath()
            )
        );
        var service = new CSharpGenerationCommandService(host, dialogs, presenter);

        service.Run();

        // 単文の ShowError ではなく、要約＋詳細の ShowErrorDetails へ移行している
        dialogs.ErrorMessages.Should().BeEmpty();
        var entry = dialogs.ErrorDetailsMessages.Should().ContainSingle().Subject;
        entry.Message.Should().Be(CodeGenStrings.Csharp_GenerationFailedIntro);
        entry.Title.Should().Be(CodeGenStrings.Csharp_GenerationErrorTitle);
        // 詳細にはエラー診断（[Error] ...）が並ぶ
        entry.Details.Should().Contain("[Error]");
    }

    /// <summary>ダイアログをキャンセル（null 返却）したときは、生成もダイアログ提示も行わない</summary>
    [Fact(DisplayName = "ダイアログのキャンセルでは何も提示しない")]
    public void Run_DialogCancelled_DoesNothing()
    {
        var dialogs = new StubDialogService();
        var host = new StubErDiagramHost
        {
            DiagramToReturn = DiagramWithEntity(),
            ProvidersToReturn = SqlServerRegistry(),
        };
        var service = new CSharpGenerationCommandService(
            host,
            dialogs,
            new FakeCSharpPresenter(result: null)
        );

        service.Run();

        dialogs.InformationMessages.Should().BeEmpty();
        dialogs.InformationDetailsMessages.Should().BeEmpty();
        dialogs.ErrorMessages.Should().BeEmpty();
        dialogs.ErrorDetailsMessages.Should().BeEmpty();
    }

    /// <summary>1 ファイル出力の生成を 1 回実行し、書き出したファイルのパスを返す（上書き確認テストの前準備）</summary>
    private static (CSharpGenerationCommandService Service, string GeneratedPath) GenerateOnce(
        StubDialogService dialogs,
        string outputDirectory
    )
    {
        var host = new StubErDiagramHost
        {
            DiagramToReturn = DiagramWithEntity(),
            ProvidersToReturn = SqlServerRegistry(),
        };
        var presenter = new FakeCSharpPresenter(
            new CSharpGenerationDialogResult(
                new CodeGenerationOptions
                {
                    RootNamespace = "Sample.Domain",
                    OutputFileName = "Sample.g.cs",
                },
                outputDirectory
            )
        );
        var service = new CSharpGenerationCommandService(host, dialogs, presenter);

        service.Run();

        return (service, Path.Combine(outputDirectory, "Sample.g.cs"));
    }

    [Fact(
        DisplayName = "上書き確認: 手で編集された生成ファイルがあれば一覧を示し、キャンセルなら何も書かない"
    )]
    public void Run_EditedFileAndCancel_KeepsEditsAndShowsNothingElse()
    {
        using var output = new TempOutputDirectory();
        var dialogs = new StubDialogService();
        var (service, generatedPath) = GenerateOnce(dialogs, output.Path);
        var edited = File.ReadAllText(generatedPath) + "// hand-written\r\n";
        File.WriteAllText(generatedPath, edited);
        dialogs.InformationDetailsMessages.Clear();
        dialogs.ConfirmResult = false;

        service.Run();

        var confirm = dialogs.WarningConfirmDetailsMessages.Should().ContainSingle().Subject;
        confirm.Message.Should().Be(CodeGenStrings.Csharp_ModifiedFilesIntro);
        confirm.Title.Should().Be(CodeGenStrings.Csharp_ModifiedFilesTitle);
        confirm.Details.Should().Be(generatedPath);
        File.ReadAllText(generatedPath).Should().Be(edited, "キャンセルでは上書きしない");
        dialogs.InformationDetailsMessages.Should().BeEmpty("完了の通知も出さない");
        dialogs.ErrorMessages.Should().BeEmpty();
    }

    [Fact(DisplayName = "上書き確認: OK を選ぶと編集済みファイルを上書きして完了を通知する")]
    public void Run_EditedFileAndConfirm_Overwrites()
    {
        using var output = new TempOutputDirectory();
        var dialogs = new StubDialogService();
        var (service, generatedPath) = GenerateOnce(dialogs, output.Path);
        var original = File.ReadAllText(generatedPath);
        File.WriteAllText(generatedPath, original + "// hand-written\r\n");
        dialogs.InformationDetailsMessages.Clear();

        service.Run();

        dialogs.WarningConfirmDetailsMessages.Should().ContainSingle();
        File.ReadAllText(generatedPath).Should().Be(original);
        dialogs.InformationDetailsMessages.Should().ContainSingle();
    }

    [Fact(DisplayName = "上書き確認: 編集されていなければ確認を出さずに再生成する")]
    public void Run_UneditedFile_RegeneratesWithoutConfirmation()
    {
        using var output = new TempOutputDirectory();
        var dialogs = new StubDialogService();
        var (service, _) = GenerateOnce(dialogs, output.Path);
        dialogs.InformationDetailsMessages.Clear();

        service.Run();

        dialogs.WarningConfirmDetailsMessages.Should().BeEmpty();
        dialogs.InformationDetailsMessages.Should().ContainSingle();
    }

    /// <summary>指定した確定結果を返し、渡されたプロバイダを記録するダイアログ提示フェイク</summary>
    private sealed class FakeCSharpPresenter(CSharpGenerationDialogResult? result)
        : ICSharpGenerationDialogPresenter
    {
        /// <summary>直近の <see cref="Show"/> に渡されたプロバイダ</summary>
        public IDatabaseProvider? LastProvider { get; private set; }

        public CSharpGenerationDialogResult? Show(IDatabaseProvider currentProvider)
        {
            LastProvider = currentProvider;
            return result;
        }
    }

    /// <summary>生成物の書き出し先となる一時ディレクトリ（Dispose で再帰削除する）</summary>
    private sealed class TempOutputDirectory : IDisposable
    {
        public string Path { get; } =
            System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "QuickER_GenTest_" + Guid.NewGuid().ToString("N")
            );

        public TempOutputDirectory() => Directory.CreateDirectory(Path);

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
