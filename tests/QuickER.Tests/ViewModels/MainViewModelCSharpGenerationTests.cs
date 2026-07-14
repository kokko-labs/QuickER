using System.Collections.Generic;
using System.IO;
using FluentAssertions;
using QuickER.CodeGen.CSharp;
using QuickER.CodeGen.UI;
using QuickER.Gui.Abstractions;
using QuickER.Model;
using QuickER.Provider;
using QuickER.Services;
using QuickER.Tests.TestDoubles;
using QuickER.ViewModels;
using GuiStrings = QuickER.Resources.Strings;

namespace QuickER.Tests.ViewModels;

/// <summary>
/// C# コード生成コマンド（<see cref="MainViewModel.GenerateCSharpCodeCommand"/>）の結果提示が、
/// 情報／エラーの詳細ダイアログ（<see cref="IDialogService.ShowInformationDetails"/> /
/// <see cref="IDialogService.ShowErrorDetails"/>）へ正しく振り分けられることを検証するテストクラス。
/// </summary>
public class MainViewModelCSharpGenerationTests
{
    /// <summary>成功かつ詳細（PackageReference 案内）がある場合は、要約＋詳細の情報ダイアログで提示される</summary>
    [Fact(
        DisplayName = "生成成功・詳細ありは ShowInformationDetails（message=完了・details に案内）"
    )]
    public void Generate_SuccessWithDetails_ShowsInformationDetails()
    {
        using var output = new TempOutputDirectory();
        var dialogs = new StubDialogService();
        var appDialogs = new StubAppDialogService(
            new CodeGenerationOptions
            {
                NamespaceName = "Sample.Domain",
                // 固定コードをパッケージ参照へ切り替えると、PackageReference 案内が「詳細」に載る
                UseRuntimePackages = true,
            },
            output.Path
        );
        var vm = new MainViewModel(dialogs, appDialogs);
        vm.AddEntityCommand.Execute(null);

        vm.GenerateCSharpCodeCommand.Execute(null);

        // 詳細ダイアログへ振り分けられ、単文の ShowInformation は使われない
        dialogs.InformationMessages.Should().BeEmpty();
        var entry = dialogs.InformationDetailsMessages.Should().ContainSingle().Subject;
        entry.Message.Should().Be(GuiStrings.Csharp_GeneratedSuccess);
        entry.Title.Should().Be(GuiStrings.Common_Complete);
        // 詳細には PackageReference 案内（<PackageReference ...>）が含まれる
        entry.Details.Should().Contain("PackageReference");
    }

    /// <summary>成功かつ詳細が無い（診断ゼロ・パッケージ案内なし）場合は、従来の単文完了通知へフォールバックする</summary>
    [Fact(DisplayName = "生成成功・詳細なしは ShowInformation にフォールバック")]
    public void Generate_SuccessWithoutDetails_FallsBackToShowInformation()
    {
        using var output = new TempOutputDirectory();
        var dialogs = new StubDialogService();
        var appDialogs = new StubAppDialogService(
            new CodeGenerationOptions { NamespaceName = "Sample.Domain" },
            output.Path
        );
        var vm = new MainViewModel(dialogs, appDialogs);
        vm.AddEntityCommand.Execute(null);

        vm.GenerateCSharpCodeCommand.Execute(null);

        // 詳細が無いため、大型の詳細ダイアログは出さず単文の完了通知に落ちる
        dialogs.InformationDetailsMessages.Should().BeEmpty();
        dialogs
            .InformationMessages.Should()
            .ContainSingle()
            .Which.Should()
            .Be(GuiStrings.Csharp_GeneratedSuccess);
    }

    /// <summary>生成エラー時は、導入文（message）と診断一覧（details）を分けてエラー詳細ダイアログで提示する</summary>
    [Fact(DisplayName = "生成エラーは ShowErrorDetails（message=導入文・details に診断）")]
    public void Generate_Error_ShowsErrorDetails()
    {
        using var output = new TempOutputDirectory();
        var dialogs = new StubDialogService();
        var appDialogs = new StubAppDialogService(
            new CodeGenerationOptions { NamespaceName = "Sample.Domain" },
            output.Path
        );
        // エンティティを 1 つも追加しない＝空の図でエラー診断（NoEntities）を発生させる
        var vm = new MainViewModel(dialogs, appDialogs);

        vm.GenerateCSharpCodeCommand.Execute(null);

        // 単文の ShowError ではなく、要約＋詳細の ShowErrorDetails へ移行している
        dialogs.ErrorMessages.Should().BeEmpty();
        var entry = dialogs.ErrorDetailsMessages.Should().ContainSingle().Subject;
        entry.Message.Should().Be(GuiStrings.Csharp_GenerationFailedIntro);
        entry.Title.Should().Be(GuiStrings.Csharp_GenerationErrorTitle);
        // 詳細にはエラー診断（[Error] ...）が並ぶ
        entry.Details.Should().Contain("[Error]");
    }

    /// <summary>指定した生成オプションと出力先を返すアプリダイアログスタブ（他ダイアログは既定挙動）</summary>
    private sealed class StubAppDialogService(CodeGenerationOptions options, string outputDirectory)
        : IAppDialogService
    {
        public CSharpGenerationDialogResult? ShowCSharpGenerationDialog(
            IDatabaseProvider currentProvider
        ) => new(options, outputDirectory);

        public List<QueryDefinition>? ShowQueryDefinitionDialog(ErDiagram diagram) => null;

        public DbConnectionDialogResult? ShowDbConnectionDialog(
            DbConnectionDialogMode mode,
            IDatabaseProvider? fixedProvider = null,
            string? title = null
        ) => null;

        public void ShowSchemaSyncDialog(
            IDatabaseProvider provider,
            DbConnectionSettings settings,
            IReadOnlyList<Entity> entities,
            IReadOnlyList<Relationship> relationships
        ) { }

        public PrintOptions? ShowPrintOptionsDialog(string? defaultTitle) => null;
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
